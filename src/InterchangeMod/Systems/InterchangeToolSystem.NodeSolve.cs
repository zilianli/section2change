using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using Game.Simulation;
using InterchangeMod.Generation;
using InterchangeMod.Generation.Endpoints;
using InterchangeMod.Generation.Roads;
using InterchangeMod.Placement;
using Unity.Mathematics;

namespace InterchangeMod.Systems
{
    public partial class InterchangeToolSystem
    {
        // A world can be destroyed while its cancelled pure search is still unwinding.
        // Keep real computation serial even if another tool instance is then created.
        private static readonly SemaphoreSlim s_MixedNodeSolveGate = new SemaphoreSlim(1, 1);
        private readonly LatestNodeSolve<MixedNodeSolveInput, RetreatSearchResult> m_MixedNodeSolve =
            new LatestNodeSolve<MixedNodeSolveInput, RetreatSearchResult>(SolveMixedNodeSnapshot);
        private bool m_MixedNodeSolvePending;

        private sealed class MixedNodeSolveInput : IEquatable<MixedNodeSolveInput>
        {
            public MixedNodeSolveInput(EndpointTypeRequest request, EndpointMixedRoadSetup setup,
                IReadOnlyList<RoadApproachTrace> traces, IReadOnlyList<RoadInfo> catalog,
                string settingsKey, int retreat, float ground)
            {
                Request = request; Setup = setup; Traces = Array.AsReadOnly(traces.ToArray());
                Catalog = catalog; SettingsKey = settingsKey; Retreat = retreat; Ground = ground;
            }
            public EndpointTypeRequest Request { get; }
            public EndpointMixedRoadSetup Setup { get; }
            public IReadOnlyList<RoadApproachTrace> Traces { get; }
            public IReadOnlyList<RoadInfo> Catalog { get; }
            public string SettingsKey { get; }
            public int Retreat { get; }
            public float Ground { get; }
            public bool Equals(MixedNodeSolveInput other) => other != null && SettingsKey == other.SettingsKey
                && Ground == other.Ground && SameNodeSolveArms(Request.Arms, other.Request.Arms)
                && Traces.Count == other.Traces.Count && Traces.Zip(other.Traces, SameNodeSolveTrace).All(same => same)
                && SameNodeSolveCatalog(Catalog, other.Catalog);
            public override bool Equals(object obj) => Equals(obj as MixedNodeSolveInput);
            public override int GetHashCode() => SettingsKey.GetHashCode();
        }

        private static IReadOnlyList<RoadInfo> FreezeNodeSolveCatalog(IReadOnlyList<RoadInfo> catalog) =>
            Array.AsReadOnly(catalog.Select(r => new RoadInfo(r.Name, r.IsHighway, r.ForwardLanes, r.BackwardLanes,
                r.Width, r.PublicTransportLanes, r.GroundHeight, r.ElevatedHeight, r.MaxSlope) {
                    MiddlePillarSpacing = r.MiddlePillarSpacing, PillarWidth = r.PillarWidth,
                    ElevatedLength = r.ElevatedLength, Icon = r.Icon
                }).ToArray());

        private static bool SameNodeSolveCatalog(IReadOnlyList<RoadInfo> a, IReadOnlyList<RoadInfo> b) => a.Count == b.Count
            && a.Zip(b, (x, y) => x.Name == y.Name && x.IsHighway == y.IsHighway && x.ForwardLanes == y.ForwardLanes
                && x.BackwardLanes == y.BackwardLanes && x.Width == y.Width && x.PublicTransportLanes == y.PublicTransportLanes
                && Nullable.Equals(x.GroundHeight, y.GroundHeight) && Nullable.Equals(x.ElevatedHeight, y.ElevatedHeight)
                && x.MaxSlope == y.MaxSlope && x.MiddlePillarSpacing == y.MiddlePillarSpacing
                && x.PillarWidth == y.PillarWidth && x.ElevatedLength == y.ElevatedLength).All(same => same);

        private static bool SameNodeSolveArms(IReadOnlyList<EndpointArm> a, IReadOnlyList<EndpointArm> b) => a.Count == b.Count
            && a.Zip(b, (x, y) => x.Index == y.Index && x.Endpoints.Count == y.Endpoints.Count
                && x.Endpoints.Zip(y.Endpoints, NodePlacementSnapshot.SameEndpoint).All(same => same)).All(same => same);

        private static bool SameNodeSolveTrace(RoadApproachTrace a, RoadApproachTrace b) =>
            NodePlacementSnapshot.SameEndpoint(a.Origin, b.Origin) && a.Pieces.Count == b.Pieces.Count
            && a.Pieces.Zip(b.Pieces, (x, y) => x.EdgeIndex == y.EdgeIndex && x.EdgeVersion == y.EdgeVersion
                && x.OuterNodeIndex == y.OuterNodeIndex && x.OuterNodeVersion == y.OuterNodeVersion && x.Reversed == y.Reversed
                && x.Curve.P0 == y.Curve.P0 && x.Curve.P1 == y.Curve.P1 && x.Curve.P2 == y.Curve.P2 && x.Curve.P3 == y.Curve.P3)
                .All(same => same);

        private string NodeSolveSettingsKey() => string.Join("|", new[] {
            m_SettingsVersion.ToString(CultureInfo.InvariantCulture), setting.InterchangeType, selectedRoadName,
            nodeGrade.ToString(CultureInfo.InvariantCulture), nodeRetreat.ToString(CultureInfo.InvariantCulture),
            nodeSetting.BaseElevation.ToString("R", CultureInfo.InvariantCulture), nodeSetting.LevelGap.ToString("R", CultureInfo.InvariantCulture),
            nodeSetting.RampPrefab, nodeSetting.CloverleafCrossOnTop.ToString(), nodeSetting.CloverleafHugLoop.ToString(),
            nodeSetting.StackLeftExit.ToString(), nodeSetting.TrumpetLoopForExit.ToString(), nodeSetting.DirectionalTwoLevel.ToString(),
            nodeSetting.RoundaboutRacetrack.ToString(), m_CityConfigurationSystem.leftHandTraffic.ToString()
        });

        private static RetreatSearchResult SolveMixedNodeSnapshot(MixedNodeSolveInput input, CancellationToken token)
        {
            // This method deliberately has no tool/world/terrain/native handles.
            s_MixedNodeSolveGate.Wait(token);
            try
            {
                token.ThrowIfCancellationRequested();
                var result = EndpointMixedRoadSearch.SearchCancellable(input.Request.Arms, input.Traces, input.Setup,
                    input.Request.MaxGrade, input.Retreat,token);
                token.ThrowIfCancellationRequested();
                return result;
            }
            finally { s_MixedNodeSolveGate.Release(); }
        }

        private void InvalidateNodeSolve()
        {
            m_MixedNodeSolve.Invalidate();
            m_MixedNodeSolvePending = false;
            InvalidateNodeJob();
        }

        private void QueueMixedNodeSolve(EndpointTypeRequest request, EndpointMixedRoadSetup setup,
            IReadOnlyList<RoadApproachTrace> traces, IReadOnlyList<RoadInfo> catalog, float ground)
        {
            var input = new MixedNodeSolveInput(request, setup, traces, catalog, NodeSolveSettingsKey(), nodeRetreat, ground);
            long revision = m_MixedNodeSolve.Request(input);
            m_MixedNodeSolvePending = true;
            BeginNodeSolveProgress("正在求解定向立交（混合道路）");
            nodePreviewFailed = false;
            nodeMessage = "正在后台计算定向立交预览；可继续操作面板，更改端点或参数会使旧结果失效。";
            Mod.log.Info($"[autochange] mixed Stack queued: revision={revision}; single worker, latest request only");
        }

        // Called only by the game update thread, before native eligibility checks.
        // True preserves the existing one-update gap between emission and native checking.
        private bool FinishCompletedMixedNodeSolve()
        {
            if (!m_MixedNodeSolve.TryTakeCompletion(out var completed)) return false;
            m_MixedNodeSolvePending = false;
            EndNodeSolveProgress();
            var input = completed.Input;
            if (!isActive || !nodeMode || browsing || !paused || m_NodeDirty
                || input.SettingsKey != NodeSolveSettingsKey() || !SameNodeSolveArms(input.Request.Arms, nodeArms)
                || !SameNodeSolveCatalog(input.Catalog, m_RoadCatalog))
            {
                nodeMessage = "后台求解期间输入已变化，旧结果已丢弃；请重新生成预览。";
                return true;
            }
            try
            {
                if (completed.Error != null) ExceptionDispatchInfo.Capture(completed.Error).Throw();
                var search = completed.Result;
                if (search.Failure == RetreatSearchFailure.None && search.Spatial != null)
                {
                    // Read every original corridor again before emitting any native definitions.
                    // Node identities alone do not detect changed intermediate edges or tails.
                    foreach (var source in input.Traces)
                    {
                        var origin = source.Origin;
                        if (!OpenEndpointReader.TryRead(EntityManager, m_PrefabSystem, m_RoadsByName, NodeEntity(origin), out var fresh, out _)
                            || !NodePlacementSnapshot.SameEndpoint(origin, fresh)
                            || !SameNodeSolveTrace(source, RoadApproachReader.Read(EntityManager, m_PrefabSystem, m_RoadsByName,
                                fresh, input.Retreat, out _)))
                        {
                            nodePreviewFailed = true;
                            nodeMessage = "后台求解期间原路路廊发生变化，旧结果已丢弃；请重新选点或生成预览。";
                            return true;
                        }
                    }
                }
                var terrain = m_TerrainSystem.GetHeightData();
                var center = EndpointFrame.Estimate(input.Request.Arms, input.Request.Type).Center.Value;
                if (TerrainUtils.SampleHeight(ref terrain, new float3(center.X, 0, center.Z)) != input.Ground)
                {
                    nodePreviewFailed = true;
                    nodeMessage = "后台求解期间地形已变化，旧结果已丢弃；请重新生成预览。";
                    return true;
                }
                CompleteMixedNodePreview(input.Request, input.Setup, input.Traces, search, ref terrain);
                nodePreviewFailed = !m_HasPreview;
            }
            catch (Exception error)
            {
                m_HasPreview = false; nodePreviewFailed = true;
                snapshot = new PanelSnapshot();
                snapshot.Errors.Add("后台节点求解异常：" + error.Message);
                nodeMessage = "后台节点求解异常：" + error.Message + "；详情已记录到日志，请重新生成预览。";
                Mod.log.Error(error);
            }
            return true;
        }
    }
}
