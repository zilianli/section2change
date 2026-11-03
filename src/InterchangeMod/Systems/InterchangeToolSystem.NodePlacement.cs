using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Text;
using Game.Common;
using Game.Net;
using Game.Tools;
using InterchangeMod.Generation;
using InterchangeMod.Generation.Endpoints;
using InterchangeMod.Placement;
using Unity.Entities;
using Unity.Jobs;
using Unity.Collections;
using Unity.Mathematics;

namespace InterchangeMod.Systems
{
    public partial class InterchangeToolSystem
    {
        private readonly NodePlacementGate m_NodePlacement=new NodePlacementGate();
        private int m_NodeConfirmRevision=-1,m_NodePreviewSettings=-1;
        private string m_NodePreviewType,m_NodePreviewRamp;
        private IReadOnlyList<RoadApproachTrace> m_NodePreviewSources;
        private IReadOnlyList<EndpointArm> m_NodePreviewArms,m_NodeOriginalArms;
        private NodePlacementSnapshot m_NodeSourceSnapshot;
        private string m_NodePreparedFingerprint;
        private EntityQuery m_NodeTempRoadQuery;

        private void CreateNodePlacementQuery()
        {
            m_NodeTempRoadQuery=GetEntityQuery(new EntityQueryDesc {
                All=new[]{ComponentType.ReadOnly<Temp>(),ComponentType.ReadOnly<Edge>()},
                None=new[]{ComponentType.ReadOnly<Deleted>()}
            });
        }
        private bool NodePlacementContext
        {
            get
            {
                if(!isActive||!nodeMode||browsing||!paused||m_NodeDirty||!m_HasPreview||nodePreviewFailed
                    ||m_NodeSettings!=m_SettingsVersion||m_NodePreviewSettings!=m_SettingsVersion
                    ||m_NodeType!=setting.InterchangeType||m_NodePreviewType!=setting.InterchangeType
                    ||!Enum.TryParse(setting.InterchangeType,out InterchangeType type)||!NodePlacementGate.Supports(type)
                    ||m_NodeSourceSnapshot==null||m_NodePreparedFingerprint==null||m_NodeOriginalArms==null
                    ||snapshot==null||snapshot.Errors.Count!=0)return false;
                var current=nodeArms;
                return current.Count==m_NodeOriginalArms.Count&&current.Zip(m_NodeOriginalArms,(a,b)=>
                    a.Index==b.Index&&a.Endpoints.Count==b.Endpoints.Count
                    &&a.Endpoints.Zip(b.Endpoints,OpenEndpointReader.SameNode).All(same=>same)).All(same=>same);
            }
        }
        public bool nodeHasPreview => nodeMode&&m_HasPreview&&m_NodePlacement.HasPreview;
        public bool nodeCanConfirm => m_NodePlacement.CanConfirm(NodePlacementContext);
        public int nodePreviewRevision => nodeHasPreview?m_NodePlacement.Revision:-1;
        public void RequestNodeConfirm(int displayedRevision)
        {
            if(!nodeMode)return;
            m_ConfirmRequested=m_NodePlacement.CanQueue(displayedRevision,NodePlacementContext);
            m_NodeConfirmRevision=m_ConfirmRequested?displayedRevision:-1;
        }
        private void InvalidateNodeConfirmation()
        {
            m_NodePlacement.Invalidate();
            m_ConfirmRequested=false;m_NodeConfirmRevision=-1;
        }
        private void ResetNodePreview()
        {
            InvalidateNodeConfirmation();
            m_HasPreview=false;m_NodeRetreatPreview=null;m_NodeSpatialPreview=null;
            m_NodePreviewSources=null;m_NodePreviewArms=null;m_NodeOriginalArms=null;
            m_NodeSourceSnapshot=null;m_NodePreparedFingerprint=null;
        }
        private void StageNodeConfirmation(IReadOnlyList<RoadApproachTrace> sources,IReadOnlyList<EndpointArm> arms,
            string ramp,NodePreviewPlacer.PreparedBatch prepared)
        {
            m_NodePreviewSources=Array.AsReadOnly(sources.ToArray());
            m_NodePreviewArms=Array.AsReadOnly(arms.ToArray());
            m_NodeOriginalArms=nodeArms;
            m_NodeSourceSnapshot=NodePlacementSnapshot.Capture(sources,m_NodeRetreatPreview);
            m_NodePreparedFingerprint=prepared.Fingerprint;
            m_NodePreviewType=setting.InterchangeType;m_NodePreviewSettings=m_SettingsVersion;m_NodePreviewRamp=ramp;
            m_NodePlacement.Stage();
        }
        private string ReadNodeTempBatchFingerprint()
        {
            if(m_DefinitionQuery.IsEmptyIgnoreFilter||m_NodeTempRoadQuery.IsEmptyIgnoreFilter)return null;
            var b=new StringBuilder();
            void Key(Entity e) => b.Append(e.Index).Append(':').Append(e.Version).Append('|');
            void Number(float value) => b.Append(value.ToString("R",CultureInfo.InvariantCulture)).Append('|');
            void Point(float3 p) { Number(p.x);Number(p.y);Number(p.z); }
            bool hasCreatedRoad=false;
            using(var entities=m_NodeTempRoadQuery.ToEntityArray(Allocator.Temp))
                foreach(var e in entities.ToArray().OrderBy(x=>x.Index).ThenBy(x=>x.Version))
                {
                    Key(e);
                    var temp=EntityManager.GetComponentData<Temp>(e);
                    hasCreatedRoad|=(temp.m_Flags&TempFlags.Create)!=0&&(temp.m_Flags&TempFlags.Delete)==0;
                    Key(temp.m_Original);b.Append((uint)temp.m_Flags).Append('|');
                    var edge=EntityManager.GetComponentData<Edge>(e);
                    Key(edge.m_Start);Key(edge.m_End);
                    if(!EntityManager.HasComponent<Curve>(e))return null;
                    var curve=EntityManager.GetComponentData<Curve>(e).m_Bezier;
                    if(!math.all(math.isfinite(curve.a))||!math.all(math.isfinite(curve.b))
                        ||!math.all(math.isfinite(curve.c))||!math.all(math.isfinite(curve.d)))return null;
                    Point(curve.a);Point(curve.b);Point(curve.c);Point(curve.d);
                    foreach(var node in new[]{edge.m_Start,edge.m_End})
                    {
                        if(!EntityManager.HasComponent<Node>(node))return null;
                        var position=EntityManager.GetComponentData<Node>(node).m_Position;
                        if(!math.all(math.isfinite(position)))return null;
                        Point(position);
                    }
                }
            return hasCreatedRoad?b.ToString():null;
        }
        private void UpdateNodeNativeEligibility()
        {
            if(!NodePlacementContext) {m_NodePlacement.ObserveBatch(null);return;}
            try
            {
                // Build returns before this path. Let native generation and error checks run
                // on unchanged temporary definitions before trusting GetAllowApply.
                bool ready=m_NodePlacement.ObserveBatch(ReadNodeTempBatchFingerprint());
                bool allowed=ready&&GetAllowApply();
                m_NodePlacement.ObserveNative(ready,allowed);
                if(ready)RefreshGameErrors();
            }
            catch(Exception ex)
            {
                InvalidateNodeConfirmation();m_NodeDirty=true;nodePreviewFailed=true;
                nodeMessage="当前临时道路检查异常，旧确认已失效；请重新生成预览。";
                Mod.log.Error(ex);
            }
        }
        private JobHandle RejectNodeConfirmation(JobHandle deps,string reason)
        {
            ResetNodePreview();nodePreviewFailed=true;
            nodeMessage=reason+"；当前预览已失效，请重新生成预览。";
            snapshot=null;
            return Clear(deps);
        }
        private JobHandle? TryApplyNodePreview(JobHandle deps)
        {
            // The panel request holds the revision visible at click time. An edit or
            // a same-frame preview rebuild cannot transfer that click to a new batch.
            if(m_NodeConfirmRevision!=m_NodePlacement.Revision||!nodeCanConfirm)return null;
            try
            {
                var freshSources=new List<RoadApproachTrace>();
                var freshCuts=new List<RoadRetreatResult>();
                for(int i=0;i<m_NodePreviewSources.Count;i++)
                {
                    var origin=m_NodePreviewSources[i].Origin;
                    if(!OpenEndpointReader.TryRead(EntityManager,m_PrefabSystem,m_RoadsByName,NodeEntity(origin),out var fresh,out _)
                        ||!OpenEndpointReader.SameNode(origin,fresh))
                        return RejectNodeConfirmation(deps,"已选开放端点或道路属性发生变化");
                    var trace=RoadApproachReader.Read(EntityManager,m_PrefabSystem,m_RoadsByName,fresh,nodeRetreat,out _);
                    freshSources.Add(trace);
                    freshCuts.Add(RoadRetreat.Cut(trace,m_NodeRetreatPreview[i].Distance));
                }
                if(!m_NodeSourceSnapshot.Matches(freshSources,freshCuts))
                    return RejectNodeConfirmation(deps,"原路路廊、退接接点或保留尾段发生变化");
                var terrain=m_TerrainSystem.GetHeightData();
                // Read only. Never emit a newly rebuilt and unchecked definition batch on confirm.
                var errors=NodePreviewPlacer.Prepare(m_NodeSpatialPreview,m_NodePreviewArms,m_NodePreviewRamp,
                    ref terrain,m_PrefabResolver,m_SelectedRoad,m_RandomSeed,EntityManager,freshCuts,freshSources,out var prepared);
                if(errors.Count!=0||prepared.Fingerprint!=m_NodePreparedFingerprint)
                    return RejectNodeConfirmation(deps,errors.Count!=0?string.Join("；",errors):"原路连接、预制体、节点位置或地形发生变化");
                // Native eligibility is the final decision, after source validation and before any application.
                if(!GetAllowApply())
                {
                    m_NodePlacement.ObserveNative(true,false);RefreshGameErrors();
                    nodeMessage="游戏检查阻止放置，请查看面板中的道路错误；当前预览保留。";
                    return null;
                }
            }
            catch(Exception ex)
            {
                Mod.log.Error(ex);
                return RejectNodeConfirmation(deps,"确认前只读检查异常，未提交道路改造");
            }
            // Leave the read-only failure path before handing the existing batch to
            // the native apply pipeline. No custom rollback is claimed after this point.
            var clearedSelection=new EndpointSelection(m_NodeOriginalArms.Count);
            if(!m_NodePlacement.TryConsume(m_NodeConfirmRevision,NodePlacementContext))return null;
            applyMode=ApplyMode.Apply;
            m_RandomSeed=m_Random.NextInt();
            ResetNodePreview();snapshot=null;m_NodeSelection=clearedSelection;
            m_NodeGenerate=false;m_NodeDirty=false;nodeActiveArm=0;m_NodeMarkerAge=999;nodePreviewFailed=false;
            nodeMessage="已确认当前批次，道路改造已交给游戏处理；请检查接回、车道连接及通行。选择新端点可继续预览。";
            return DestroyDefinitions(m_DefinitionQuery,m_ToolOutputBarrier,deps);
        }
    }
}
