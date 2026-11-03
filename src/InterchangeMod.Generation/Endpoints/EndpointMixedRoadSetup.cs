using System;
using System.Collections.Generic;
using System.Linq;
using InterchangeMod.Generation.Geometry;
using InterchangeMod.Generation.Roads;

namespace InterchangeMod.Generation.Endpoints
{
    /// <summary>Real road selection and levels for the existing divided-road cores.</summary>
    public sealed class EndpointMixedRoadSetup
    {
        private EndpointMixedRoadSetup(string diagnostic) { Diagnostic = diagnostic; }
        public bool Success => Ramp != null && Design != null && ProxyArms.Count > 0;
        public string Diagnostic { get; private set; }
        public RoadInfo? Ramp { get; private set; }
        public RoadInfo? Ring { get; private set; }
        public CloverleafRoadDesign? Design { get; private set; }
        public float MainHeight { get; private set; }
        public float CrossHeight { get; private set; }
        public float RequiredClearance { get; private set; }
        internal InterchangeType Type { get; private set; }
        internal bool RightHandTraffic { get; private set; }
        internal bool LeftExit { get; private set; }
        internal bool TrumpetLoopForExit { get; private set; }
        internal bool DirectionalTwoLevel { get; private set; }
        internal bool Racetrack { get; private set; }
        internal float TerrainHeight { get; private set; }
        internal float LevelGap { get; private set; }
        internal IReadOnlyList<EndpointArm> ActualArms { get; private set; } = Array.Empty<EndpointArm>();
        internal IReadOnlyList<EndpointArm> ProxyArms { get; private set; } = Array.Empty<EndpointArm>();
        internal IReadOnlyList<RoadInfo> Catalog { get; private set; } = Array.Empty<RoadInfo>();
        internal IReadOnlyList<EndpointGridArmRoads> GridRoads { get; private set; } = Array.Empty<EndpointGridArmRoads>();

        internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Range(HeightRange? r) => !r.HasValue || Finite(r.Value.Min) && Finite(r.Value.Max) && r.Value.Min <= r.Value.Max;
        internal static bool Valid(RoadInfo road) => road != null && !string.IsNullOrWhiteSpace(road.Name)
            && road.ForwardLanes > 0 && road.BackwardLanes >= 0 && road.PublicTransportLanes == 0
            && Finite(road.Width) && road.Width > 0 && road.Width <= 1000 && Range(road.GroundHeight) && Range(road.ElevatedHeight)
            && Finite(road.ElevatedLength) && (road.ElevatedLength == 0 || road.ElevatedLength >= 10 && road.ElevatedLength <= 1000)
            && Finite(road.PillarWidth) && road.PillarWidth >= 0 && road.PillarWidth <= 1000
            && (!road.MiddlePillarSpacing.HasValue || Finite(road.MiddlePillarSpacing.Value) && road.MiddlePillarSpacing.Value >= 0)
            && Finite(road.MaxSlope) && road.MaxSlope >= 0;
        internal static float Span(RoadInfo road) => road.ElevatedLength > 0 ? road.ElevatedLength : 80;
        private static float Pillar(RoadInfo road) => road.PillarWidth > 0 ? road.PillarWidth : 4;
        private static HeightRange Union(HeightRange? a, HeightRange? b)
        {
            var x = a ?? new HeightRange(0, HeightRange.MeasuredMinimumGap);
            var y = b ?? new HeightRange(0, HeightRange.MeasuredMinimumGap);
            return new HeightRange(Math.Min(x.Min, y.Min), Math.Max(x.Max, y.Max));
        }
        internal static bool Supported(InterchangeType type) => type == InterchangeType.Cloverleaf || type == InterchangeType.Stack
            || type == InterchangeType.Turbine || EndpointThreeWaySearch.Supported(type)
            || type == InterchangeType.CompactGrid || type == InterchangeType.Roundabout;

        public static EndpointMixedRoadSetup Create(IReadOnlyList<EndpointArm> arms, IReadOnlyList<RoadInfo> catalog,
            InterchangeType type, bool rht, float offset, float gap, float terrain, string? preferredRamp = null,
            bool crossOnTop = false, bool hugLoop = false, bool leftExit = false, bool trumpetLoopForExit = true,
            bool directionalTwoLevel = false, bool racetrack = false)
        {
            EndpointMixedRoadSetup Fail(string message) => new EndpointMixedRoadSetup(message);
            if (!Supported(type)) return Fail("此类型尚未接入双向／混合道路；本轮明确跳过部分苜蓿叶。");
            if (arms == null || catalog == null || EndpointConstraints.Validate(arms, type).Count != 0)
                return Fail("请为每个方向选择一个对称双向端点，或一进一出的两个单向端点，并保持相对车道数一致。");
            if (!Finite(offset) || !Finite(gap) || gap < 0 || !Finite(terrain)) return Fail("层高、层差或地形数据无效。");
            if (catalog.Any(r => r == null) || catalog.GroupBy(r => r.Name, StringComparer.Ordinal).Any(g => g.Count() != 1))
                return Fail("道路目录存在重复或无效预制体，请刷新道路目录。");
            var ordered = arms.OrderBy(a => a.Index).ToArray();
            var actualRoads = new Dictionary<int, RoadInfo>();
            foreach (var arm in ordered)
            {
                var first = arm.Endpoints[0];
                var road = catalog.FirstOrDefault(r => r.Name == first.Prefab && Valid(r) && r.Width == first.Width);
                if (road == null || road.ForwardLanes != first.Lanes
                    || (first.OneWay ? road.BackwardLanes != 0 : road.BackwardLanes != first.Lanes)
                    || arm.Endpoints.Any(p => p.Prefab != first.Prefab || p.Width != first.Width || p.Lanes != first.Lanes))
                    return Fail($"方向{arm.Index + 1}须使用目录中准确匹配的同款断面；不支持不对称车道或公交专用车道。");
                actualRoads.Add(arm.Index, road);
            }
            // Use the existing ordinary-road policy for automatic replacements,
            // rings and reduced variants. Narrow bridge/harbor assets are not
            // interchangeable with ordinary roads. Explicit sources and the
            // requested ramp remain available and retain their real metadata.
            var selectionCatalog = catalog.Where(r => Valid(r) && (RampMatcher.IsOrdinaryRoad(r)
                || actualRoads.Values.Any(actual => actual.Name == r.Name) || r.Name == preferredRamp)).ToArray();
            var automaticCatalog = selectionCatalog.Where(RampMatcher.IsOrdinaryRoad).ToArray();
            var rampCatalog = selectionCatalog.Where(r => RampMatcher.IsOrdinaryRoad(r) || r.Name == preferredRamp).ToArray();
            var full = new Dictionary<int, RoadInfo>();
            foreach (var arm in ordered)
            {
                var source = actualRoads[arm.Index];
                if (arm.Endpoints.Count == 2) { full.Add(arm.Index, source); continue; }
                int opposite = arm.Index == 0 ? 2 : arm.Index == 2 ? 0 : ordered.Length == 4 ? (arm.Index + 2) % 4 : -1;
                RoadInfo? preferred = opposite >= 0 && type != InterchangeType.Wye && ordered[opposite].Endpoints.Count == 2
                    ? actualRoads[opposite] : null;
                bool Match(RoadInfo r) => Valid(r) && r.IsHighway == source.IsHighway && r.BackwardLanes == 0
                    && r.ForwardLanes == source.ForwardLanes && r.Width <= source.Width;
                var selected = preferred != null && Match(preferred) ? preferred : automaticCatalog.Where(Match)
                    .OrderBy(r => r.Width).ThenBy(r => r.Name, StringComparer.Ordinal).FirstOrDefault();
                if (selected == null) return Fail($"方向{arm.Index + 1}缺少同类、准确{source.ForwardLanes}车道的真实单向道路，不能无损转换。");
                full.Add(arm.Index, selected);
            }
            var proxies = ordered.Select(arm =>
            {
                var source = arm.Endpoints[0]; var road = full[arm.Index];
                var axis = Vec3.Normalize(new Vec3(source.OutwardTangent.X, 0, source.OutwardTangent.Z));
                var side = Directions.RightOf(axis) * (rht ? 1 : -1);
                float initial = source.Width - road.Width;
                // The first native fork course reaches deck separation before its continuation.
                float spacing = Math.Max(road.Width + 8, initial + (road.Width + 1 - initial) / .368f);
                return new EndpointArm(arm.Index, new[] { false, true }.Select(outward =>
                {
                    var p = arm.Endpoints.Count == 2 ? arm.Endpoints.Single(e => e.TravelOutward == outward) : source;
                    var position = arm.Endpoints.Count == 2 ? p.Position : p.Position + side * (spacing / 2) * (outward ? 1 : -1);
                    return new SelectedEndpoint(10000 + arm.Index * 2 + (outward ? 1 : 0), 1, position,
                        p.OutwardTangent, true, outward, road.Width, road.ForwardLanes, road.Name);
                }));
            }).ToArray();
            if (EndpointConstraints.Validate(proxies, type).Count != 0) return Fail("转换后的准确车道数不满足既有立交约束。");
            if (type != InterchangeType.Wye)
                foreach (int i in ordered.Length == 4 ? new[] { 0, 1 } : new[] { 0 })
                    if (full[i].Name != full[i + 2].Name || full[i].Width != full[i + 2].Width)
                        return Fail("既有核心要求相对贯通方向使用同款同宽单向断面；该组合尚无准确匹配道路。");

            RoadInfo? ramp; RoadInfo? ring = null; CloverleafRoadDesign design;
            float main, cross, levelGap = 0; IReadOnlyList<EndpointGridArmRoads> gridRoads = Array.Empty<EndpointGridArmRoads>();
            if (type == InterchangeType.CompactGrid)
            {
                var grid = EndpointCompactGridSetup.Create(proxies, selectionCatalog, offset, gap, terrain, preferredRamp, ordinaryAutomaticRoads: true);
                if (!grid.Success) return Fail(grid.Diagnostic);
                ramp = grid.Ramp; design = grid.Design!; main = grid.BaseHeight; cross = grid.Layers[3]; levelGap = grid.LevelGap; gridRoads = grid.Roads;
            }
            else if (type == InterchangeType.Roundabout)
            {
                var round = EndpointRoundaboutSetup.Create(proxies, selectionCatalog, offset, gap, terrain, preferredRamp, ordinaryAutomaticRoads: true);
                if (!round.Success) return Fail(round.Diagnostic);
                ramp = round.Ramp; ring = round.Ring; design = round.Design!; main = round.MainHeight; cross = round.CrossHeight;
            }
            else if (type == InterchangeType.Wye)
            {
                var wye = EndpointWyeSetup.Create(proxies, selectionCatalog, offset, gap, terrain, preferredRamp, ordinaryAutomaticRoads: true);
                if (!wye.Success) return Fail(wye.Diagnostic);
                ramp = wye.Ramp; design = new CloverleafRoadDesign(bridgeRules: wye.BridgeRules); main = wye.BaseHeight; cross = wye.UpperHeight;
            }
            else
            {
                ramp = rampCatalog.Where(r => Valid(r) && r.BackwardLanes == 0)
                    .OrderBy(r => r.Name == preferredRamp ? 0 : 1).ThenBy(r => r.ForwardLanes)
                    .ThenBy(r => r.IsHighway == full[0].IsHighway ? 0 : 1).ThenBy(r => r.Width)
                    .ThenBy(r => r.Name, StringComparer.Ordinal).FirstOrDefault();
                if (ramp == null) return Fail("找不到有效的正向单向匝道道路。");
                var balances = EndpointLaneBalance.Select(proxies, selectionCatalog, ramp, ordinaryAutomaticRoads: true);
                var shared = type == InterchangeType.Cloverleaf && hugLoop
                    ? CloverleafRoadDesign.SelectSharedRamp(automaticCatalog, ramp) : null;
                if (type == InterchangeType.Cloverleaf && hugLoop && shared == null) return Fail("贴环道缺少准确匹配匝道每方向车道数的真实双向道路。");
                var bridges = ordered.Select(a => new EndpointBridgeRule(a.Index, Span(full[a.Index]), full[a.Index].MiddlePillarSpacing,
                    full[a.Index].Width, Pillar(full[a.Index]), full[a.Index].GroundHeight, full[a.Index].ElevatedHeight));
                design = new CloverleafRoadDesign(balances, bridgeRules: bridges, hugLoops: type == InterchangeType.Cloverleaf && hugLoop,
                    sharedRampPrefab: shared?.Name, sharedRampWidth: shared?.Width ?? 0);
                var levels = EndpointLevels.Resolve(proxies, crossOnTop, offset, gap, terrain);
                main = levels.Main; cross = levels.Cross;
            }
            var names = actualRoads.Values.Concat(full.Values).Select(r => r.Name).Concat(design.Balances.Select(b => b.Prefab))
                .Concat(gridRoads.SelectMany(r => new[] { r.Through.Name, r.Collector.Name })).Append(ramp!.Name);
            if (ring != null) names = names.Append(ring.Name);
            if (design.SharedRampPrefab != null) names = names.Append(design.SharedRampPrefab);
            var used = names.Distinct(StringComparer.Ordinal).Select(n => catalog.Single(r => r.Name == n)).ToArray();
            if (used.Any(r => !Valid(r))) return Fail("实际道路、匝道或减车道道路的桥跨／净空元数据无效。");
            var bridgesWithEnvelope = design.BridgeRules.Select(rule =>
            {
                var actual = actualRoads[rule.Arm]; var reduced = design.Balances.FirstOrDefault(b => b.Arm == rule.Arm);
                var ground = Union(rule.GroundHeight, actual.GroundHeight); var elevated = Union(rule.ElevatedHeight, actual.ElevatedHeight);
                if (reduced != null)
                {
                    var road = used.Single(r => r.Name == reduced.Prefab); ground = Union(ground, road.GroundHeight); elevated = Union(elevated, road.ElevatedHeight);
                }
                return new EndpointBridgeRule(rule.Arm, rule.MaxLength, rule.MiddleSpacing, rule.Width, rule.PillarWidth, ground, elevated);
            }).ToArray();
            design = new CloverleafRoadDesign(design.Balances, design.EdgeAligned, design.SmoothDirect, design.ApproachTransitions,
                bridgesWithEnvelope, design.HugLoops, design.SharedRampPrefab, design.SharedRampWidth,
                used.Select(r => new EndpointPrefabSupportRule(r.Name, Span(r), r.MiddlePillarSpacing, Pillar(r))));
            var ranges = used.SelectMany(r => new[] { r.GroundHeight ?? new HeightRange(0, HeightRange.MeasuredMinimumGap),
                r.ElevatedHeight ?? new HeightRange(0, HeightRange.MeasuredMinimumGap) }).ToArray();
            float required = HeightRange.BridgeThreshold;
            foreach (var lower in ranges) foreach (var upper in ranges) required = Math.Max(required, HeightRange.RequiredGap(lower, upper));
            if (!Finite(required)) return Fail("实际道路结构净空计算无效。");
            if (gap > 0 && gap < required) return Fail($"实际道路结构至少需要{required:F1}米层差，请增大层差或设为0自动。");
            float separation = gap > 0 ? gap : Math.Max(10, (float)(Math.Ceiling(required * 2) / 2 + .5));
            if (type == InterchangeType.CompactGrid) { levelGap = Math.Max(levelGap, separation); cross = main + 3 * levelGap; }
            else if (Math.Abs(main - cross) < separation)
            { if (main >= cross) main = cross + separation; else cross = main + separation; }
            if (!Finite(main) || !Finite(cross) || !Finite(main - terrain) || !Finite(cross - terrain)) return Fail("实际道路层高超出有效范围。");
            if (directionalTwoLevel && (type == InterchangeType.DirectionalT || type == InterchangeType.SemiDirectionalT)
                && LevelPlanner.BandOf(main - terrain) == ElevationBand.Elevated)
                return Fail("主线在高架上时，两层T型不能下穿；请切换为三层。");
            return new EndpointMixedRoadSetup("") { Type = type, RightHandTraffic = rht, LeftExit = leftExit,
                TrumpetLoopForExit = trumpetLoopForExit, DirectionalTwoLevel = directionalTwoLevel, Racetrack = racetrack,
                TerrainHeight = terrain, LevelGap = levelGap, ActualArms = Array.AsReadOnly(ordered), ProxyArms = Array.AsReadOnly(proxies),
                Catalog = Array.AsReadOnly(catalog.ToArray()), Ramp = ramp, Ring = ring, Design = design, MainHeight = main,
                CrossHeight = cross, RequiredClearance = required, GridRoads = gridRoads };
        }
    }
}
