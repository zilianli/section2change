using System;
using System.Collections.Generic;
using System.Linq;
using InterchangeMod.Generation.Endpoints;
using InterchangeMod.Generation.Geometry;
using InterchangeMod.Generation.Roads;

namespace InterchangeMod.Generation.Templates
{
    /// <summary>
    /// 井字（紧凑定向）立交的替换模式：两层内侧转向集散、两层外侧直行。几何由连接模式的井字求解器
    /// （EndpointCompactGridSetup / EndpointCompactGridSearch）生成：把四条臂换算成虚拟端点（每个方向一进一出两幅，
    /// 离中心 kEndpointDistance 米，高度为各自的层高），原路轨迹是沿臂方向的直线，求解器放不下时沿直线向外退接。
    /// 结果按立交局部坐标给出（Y 是离参考平面的高度），只与参数有关、与摆放位置无关，按参数缓存。
    /// </summary>
    public sealed class CompactGridTemplate : IInterchangeTemplate
    {
        /// <summary>虚拟端点离立交中心的距离（米），与连接模式测试用的距离相同；放不下时求解器沿臂向外退接。</summary>
        private const float kEndpointDistance = 100f;
        /// <summary>沿臂向外退接的上限（米）。</summary>
        private const float kRetreatLimit = 1000f;
        /// <summary>虚拟原路轨迹的长度（米），要长于退接上限。</summary>
        private const float kTraceLength = 1200f;

        /// <summary>按参数缓存的求解结果（最近 kCacheSize 个）：替换时在后台线程求解、主线程读取，所以加锁。</summary>
        private readonly Dictionary<string, Solution> m_Cache = new Dictionary<string, Solution>();
        private readonly Queue<string> m_CacheOrder = new Queue<string>();
        private const int kCacheSize = 16;

        public InterchangeType Type => InterchangeType.CompactGrid;

        public string DisplayName => "定向（紧凑井字）";

        public int LegCount => 4;

        public IReadOnlyList<string> ValidateParams(InterchangeParams p)
        {
            var errors = p.ValidateLegsAndLevels(LegCount);
            if (errors.Count > 0)
                return errors;
            if (p.Legs.Any(l => l.Form != RoadForm.Dual || l.ExternalForm == RoadForm.TwoWay))
            {
                errors.Add("井字（紧凑定向）需要四个方向都是双幅道路（两条对向单行道路），暂不支持双向道路：请对准双幅道路的路口，或用连接模式点选端点");
                return errors;
            }
            if (p.RoadCatalog == null)
            {
                errors.Add("缺少道路目录数据，无法为井字挑选直行道和集散道");
                return errors;
            }
            var solution = Solve(p);
            if (solution.Error != null)
                errors.Add(solution.Error);
            return errors;
        }

        public IReadOnlyList<RoadSegmentSpec> Generate(InterchangeParams p)
        {
            var solution = Solve(p);
            if (solution.Error != null)
                throw new InvalidOperationException(solution.Error);
            return solution.Segments;
        }

        /// <summary>
        /// 这组参数已经求解过（ValidateParams、Generate 不会再花时间）。参数本身不合法（不是双幅、缺道路目录）时也算，
        /// 由 ValidateParams 直接报错。替换模式先用它判断，没算过时放到后台线程 Precompute，免得预览卡住。
        /// </summary>
        public bool IsReady(InterchangeParams p)
        {
            if (p.Legs.Count != LegCount || p.RoadCatalog == null || p.Legs.Any(l => l.Form != RoadForm.Dual || l.ExternalForm == RoadForm.TwoWay))
                return true;
            lock (m_Cache)
                return m_Cache.ContainsKey(Key(p));
        }

        /// <summary>求解并存入缓存（可在后台线程调用；p 要是调用方不再修改的副本）。</summary>
        public void Precompute(InterchangeParams p) => Solve(p);

        /// <summary>求解时用的键（同一组参数的后台求解只启动一次）。</summary>
        public static string SolveKey(InterchangeParams p) => Key(p);

        private Solution Solve(InterchangeParams p)
        {
            string key = Key(p);
            lock (m_Cache)
                if (m_Cache.TryGetValue(key, out var cached))
                    return cached;
            var solution = Compute(p);
            lock (m_Cache)
            {
                if (!m_Cache.ContainsKey(key))
                {
                    m_Cache[key] = solution;
                    m_CacheOrder.Enqueue(key);
                    while (m_CacheOrder.Count > kCacheSize)
                        m_Cache.Remove(m_CacheOrder.Dequeue());
                }
            }
            return solution;
        }

        private static Solution Compute(InterchangeParams p)
        {
            var catalog = p.RoadCatalog!;
            var arms = new List<EndpointArm>();
            for (int i = 0; i < 4; i++)
            {
                var leg = p.Legs[i];
                var road = catalog.FirstOrDefault(r => r.Name == leg.RoadPrefab && r.IsOneWay);
                if (road == null)
                    return new Solution($"方向 {i + 1} 的道路“{leg.RoadPrefab}”不是单行道路或不在道路目录里");
                if (road.LanesPerDirection < 3)
                    return new Solution($"井字每幅至少需要 3 车道（直行、左转、右转各一条），方向 {i + 1} 的“{road.Name}”只有 {road.LanesPerDirection} 车道：请换 3 车道以上的单行道路");
                Vec3 axis = leg.Direction;
                // 右侧通行时沿臂向外行驶的一幅在臂的右侧
                Vec3 side = Directions.RightOf(axis) * (p.RightHandTraffic ? 1f : -1f);
                Vec3 mid = axis * kEndpointDistance + new Vec3(0f, p.ElevationOf(leg), 0f);
                arms.Add(new EndpointArm(i, new[] { false, true }.Select(outward => new SelectedEndpoint(
                    100 + i * 2 + (outward ? 1 : 0), 1, mid + side * (leg.CarriagewayOffset * (outward ? 1f : -1f)), axis,
                    true, outward, road.Width, road.LanesPerDirection, road.Name))));
            }
            // 自动挑的直行道、集散道和匝道只用原版普通道路（不选 RoadBuilder 等自制道路，见 RampMatcher.IsOrdinaryRoad）
            var setup = EndpointCompactGridSetup.Create(arms, catalog, 0f, 0f, 0f, p.RampPrefab, ordinaryAutomaticRoads: true);
            if (!setup.Success)
                return new Solution(setup.Diagnostic);
            var traces = arms.SelectMany(a => a.Endpoints).Select(e => new RoadApproachTrace(e, new[]
            {
                new RoadTracePiece(e.NodeIndex + 1000, 1, Bezier3.Line(e.Position, e.Position + e.OutwardTangent * kTraceLength), false, e.NodeIndex + 2000, 1),
            })).ToArray();
            var result = EndpointCompactGridSearch.Search(arms, traces, p.RightHandTraffic, setup.BaseHeight, setup.LevelGap,
                setup.Ramp!, setup.Roads, p.MaxGrade, kRetreatLimit, setup.Design!, terrainHeight: 0f);
            if (result.Failure != RetreatSearchFailure.None || result.Spatial == null)
                return new Solution($"井字在当前道路和坡度上放不下（{result.Failure}）：请加大最大坡度、换车道更多的双幅道路，或用连接模式点选端点");
            var segments = result.Spatial.Segments.Select((s, i) => new RoadSegmentSpec(
                $"{s.Kind}{s.SourceArm}{(s.PathId.Length > 0 ? "-" + s.PathId : "")}-{i}",
                s.Kind == CloverEdgeKind.Main ? (s.SourceArm % 2 == 0 ? RoadRole.MainRoad : RoadRole.CrossRoad) : RoadRole.Ramp,
                s.WorldCurve, s.Prefab, s.StartNode, s.EndNode)).ToList();
            return new Solution(segments);
        }

        /// <summary>求解只与这些参数有关：臂的方向、道路、高度、分隔带，匝道、坡度和通行方向。</summary>
        private static string Key(InterchangeParams p) =>
            string.Join("|", p.Legs.Select(l => $"{l.Angle:R},{l.RoadPrefab},{l.Form},{l.MedianWidth:R},{l.RoadWidth:R},{p.ElevationOf(l):R},{l.ExternalForm}"))
            + $"|{p.RampPrefab}|{p.MaxGrade:R}|{p.RightHandTraffic}|{p.RoadCatalog?.Count}|{(p.RoadCatalog != null ? System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(p.RoadCatalog) : 0)}";

        private sealed class Solution
        {
            public Solution(string error) { Error = error; Segments = Array.Empty<RoadSegmentSpec>(); }
            public Solution(IReadOnlyList<RoadSegmentSpec> segments) { Segments = segments; }
            public string? Error { get; }
            public IReadOnlyList<RoadSegmentSpec> Segments { get; }
        }
    }
}
