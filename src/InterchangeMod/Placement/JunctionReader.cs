using System.Collections.Generic;
using System.Linq;
using Colossal.Collections;
using Game.Common;
using Colossal.Mathematics;
using Game.Net;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using InterchangeMod.Generation.Geometry;
using InterchangeMod.Generation.Junctions;
using InterchangeMod.Generation.Roads;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace InterchangeMod.Placement
{
    /// <summary>
    /// 从鼠标指着的已有道路读出它所在的路口：
    ///   从最近的节点出发，把半径 kClusterRadius 内、由短路段相连的节点都算作同一个路口
    ///   （双幅道路相交时一个路口由多个节点组成）；
    ///   每条一端在路口内、另一端在路口外的路段就是一条离开路口的路段（ExternalEdge）。
    /// 道路类型、宽度、车道和单行方向来自道路目录（RoadInfo）。
    /// </summary>
    public static class JunctionReader
    {
        public const float kClusterRadius = 60f;

        /// <summary>鼠标指着的实体对应的路口起始节点；不是道路时返回 Entity.Null。临时实体（预览）换成它对应的原有实体。</summary>
        public static Entity FindSeedNode(EntityManager entityManager, Entity hit, float3 hitPosition)
        {
            if (hit == Entity.Null || !entityManager.Exists(hit))
                return Entity.Null;
            if (entityManager.HasComponent<Temp>(hit))
            {
                hit = entityManager.GetComponentData<Temp>(hit).m_Original;
                if (hit == Entity.Null || !entityManager.Exists(hit))
                    return Entity.Null;
            }
            if (entityManager.HasComponent<Node>(hit) && entityManager.HasBuffer<ConnectedEdge>(hit))
                return hit;
            if (entityManager.HasComponent<Edge>(hit))
            {
                // 路段两端：鼠标离路口（三条及以上道路相接的节点）不超过 kJunctionSnap 时优先对准路口，
                // 不必指在路段靠近路口的那一半（长路段上很难指准）；都不是路口时取近的一端
                var edge = entityManager.GetComponentData<Edge>(hit);
                float3 start = entityManager.GetComponentData<Node>(edge.m_Start).m_Position;
                float3 end = entityManager.GetComponentData<Node>(edge.m_End).m_Position;
                float toStart = math.distance(start.xz, hitPosition.xz), toEnd = math.distance(end.xz, hitPosition.xz);
                bool startJunction = IsJunction(entityManager, edge.m_Start) && toStart <= kJunctionSnap;
                bool endJunction = IsJunction(entityManager, edge.m_End) && toEnd <= kJunctionSnap;
                if (startJunction && (!endJunction || toStart <= toEnd))
                    return edge.m_Start;
                if (endJunction)
                    return edge.m_End;
                return toStart <= toEnd ? edge.m_Start : edge.m_End;
            }
            return Entity.Null;
        }

        /// <summary>鼠标在路段上时，离路口节点不超过这个距离（米）就对准路口。</summary>
        private const float kJunctionSnap = 120f;

        /// <summary>三条及以上道路路段相接的节点。</summary>
        private static bool IsJunction(EntityManager entityManager, Entity node)
        {
            if (!entityManager.HasBuffer<ConnectedEdge>(node))
                return false;
            int roads = 0;
            foreach (ConnectedEdge connected in entityManager.GetBuffer<ConnectedEdge>(node, true))
                if (TryGetRoadEdge(entityManager, connected.m_Edge, out _, out _))
                    roads++;
            return roads >= 3;
        }

        /// <param name="roadEdges">收到路口的全部道路路段（离开路口的和路口内部的短路段；原路口改造时按凸包拆）。</param>
        public static List<ExternalEdge> Read(EntityManager entityManager, PrefabSystem prefabSystem, Entity seed,
            IReadOnlyDictionary<string, RoadInfo> roads, ref TerrainHeightData terrain, ICollection<Entity> roadEdges = null)
        {
            float3 seedPosition = entityManager.GetComponentData<Node>(seed).m_Position;

            // 1. 路口节点：从起始节点沿短路段扩展
            var cluster = new HashSet<Entity> { seed };
            var queue = new Queue<Entity>();
            queue.Enqueue(seed);
            while (queue.Count > 0)
            {
                Entity node = queue.Dequeue();
                foreach (ConnectedEdge connected in entityManager.GetBuffer<ConnectedEdge>(node, true))
                {
                    if (!TryGetRoadEdge(entityManager, connected.m_Edge, out Edge edge, out Curve curve))
                        continue;
                    Entity other = edge.m_Start == node ? edge.m_End : edge.m_Start;
                    if (cluster.Contains(other) || curve.m_Length >= kClusterRadius)
                        continue;
                    float3 otherPosition = entityManager.GetComponentData<Node>(other).m_Position;
                    if (math.distance(otherPosition.xz, seedPosition.xz) >= kClusterRadius)
                        continue;
                    cluster.Add(other);
                    queue.Enqueue(other);
                }
            }

            // 2. 离开路口的路段
            var result = new List<ExternalEdge>();
            var seen = new HashSet<Entity>();
            foreach (Entity node in cluster)
            {
                float3 nodePosition = entityManager.GetComponentData<Node>(node).m_Position;
                foreach (ConnectedEdge connected in entityManager.GetBuffer<ConnectedEdge>(node, true))
                {
                    Entity edgeEntity = connected.m_Edge;
                    if (!seen.Add(edgeEntity) || !TryGetRoadEdge(entityManager, edgeEntity, out Edge edge, out Curve curve))
                        continue;
                    roadEdges?.Add(edgeEntity);
                    bool atStart = edge.m_Start == node;
                    Entity other = atStart ? edge.m_End : edge.m_Start;
                    if (cluster.Contains(other) && curve.m_Length < kClusterRadius)
                        continue;   // 路口内部的短路段
                    if (!prefabSystem.TryGetPrefab(entityManager.GetComponentData<PrefabRef>(edgeEntity).m_Prefab, out PrefabBase prefab)
                        || !roads.TryGetValue(prefab.name, out RoadInfo info))
                        continue;

                    float3 tangent = atStart ? MathUtils.StartTangent(curve.m_Bezier) : -MathUtils.EndTangent(curve.m_Bezier);
                    float2 direction = math.normalizesafe(tangent.xz);
                    // 单行道路：正向车道沿曲线（起点→终点）行驶
                    bool forward = info.ForwardLanes > 0;
                    result.Add(new ExternalEdge
                    {
                        Anchor = new Vec3(nodePosition.x, 0f, nodePosition.z),
                        Direction = new Vec3(direction.x, 0f, direction.y),
                        Road = info.Name,
                        OneWay = info.IsOneWay,
                        TravelOutward = atStart == forward,
                        Width = info.Width,
                        LanesPerDirection = info.LanesPerDirection,
                        IsHighway = info.IsHighway,
                        Elevation = nodePosition.y - TerrainUtils.SampleHeight(ref terrain, nodePosition),
                        // 弯道适配：沿这条路往外追踪中心线
                        Trace = Trace(entityManager, atStart ? curve.m_Bezier : Reverse(curve.m_Bezier), other, edgeEntity),
                    });
                }
            }
            return result;
        }

        /// <summary>立体交叉：鼠标离交叉点不超过这个距离时按交叉点匹配。</summary>
        public const float kCrossingRadius = 100f;

        /// <summary>
        /// 立体交叉（两条道路相交、没有共用节点，如高架跨过街道）：从鼠标指着的路段出发，找离鼠标最近的、
        /// 与它相交但不相连的道路，然后两边各收集与对方相交的平行路段（双幅时就是两幅），
        /// 每条路段在交叉点处切成两半作为离开交叉点的路段（见 CrossingReader）。
        /// 鼠标没指着路段、附近没有立体交叉时返回 null。matched 收到参与交叉的所有路段（改造时要拆掉）。
        /// </summary>
        /// <summary>最近一次 ReadCrossing 没认出立体交叉的原因（调试日志用）。</summary>
        public static string LastCrossingReason = "";

        /// <summary>
        /// ReadCrossing 找到的就是当前匹配的那一处（isCurrent 判定）时返回它：没有重新收集、追踪路段（鼠标停在道路上时每帧都会读，
        /// 收集时每条路段两半各追踪 2 千米，只在换了一处交叉时才做）。
        /// </summary>
        public static readonly List<ExternalEdge> Unchanged = new List<ExternalEdge>();

        /// <param name="isCurrent">找到的交叉（路段对、交叉点）是不是当前匹配的那一处；是时返回 Unchanged。</param>
        public static List<ExternalEdge> ReadCrossing(EntityManager entityManager, PrefabSystem prefabSystem,
            NativeQuadTree<Entity, QuadTreeBoundsXZ> netTree, Entity hit, float3 cursor,
            IReadOnlyDictionary<string, RoadInfo> roads, ref TerrainHeightData terrain, List<Entity> matched, out (Entity, Entity) key,
            System.Func<(Entity, Entity), float3, bool> isCurrent = null)
        {
            key = default;
            if (hit != Entity.Null && entityManager.Exists(hit) && entityManager.HasComponent<Temp>(hit))
                hit = entityManager.GetComponentData<Temp>(hit).m_Original;
            if (hit == Entity.Null || !entityManager.Exists(hit) || !TryGetRoadEdge(entityManager, hit, out _, out _))
                return null;

            var fromHit = FindCrossings(entityManager, netTree, hit);
            var nearest = fromHit.OrderBy(c => math.distance(c.Point.xz, cursor.xz)).FirstOrDefault();
            LastCrossingReason = fromHit.Count == 0 ? "no road crosses the hovered edge"
                : $"nearest crossing {math.distance(nearest.Point.xz, cursor.xz):F0} m from the cursor (limit {kCrossingRadius:F0} m)";
            // 没有真正相交的道路时，再看是不是“丁字”：一条路的尽头停在另一条路旁边（如地面道路在高架下方结束）
            if (fromHit.Count == 0 || math.distance(nearest.Point.xz, cursor.xz) > kCrossingRadius)
                return ReadTee(entityManager, prefabSystem, netTree, hit, cursor, roads, ref terrain, matched, out key, isCurrent);
            Entity other = nearest.Edge;
            float3 center = nearest.Point;
            key = (hit, other);
            if (isCurrent != null && isCurrent(key, center))
            {
                LastCrossingReason = "same crossing as the current match";
                return Unchanged;
            }

            var collected = CollectAt(entityManager, prefabSystem, netTree, center, hit, other, roads, ref terrain, matched);
            LastCrossingReason = $"crossing found, {collected.Count} edges collected" + (s_Rejected.Count > 0 ? "; rejected: " + string.Join("; ", s_Rejected) : "");
            return collected;
        }

        /// <summary>虚拟丁字路口：支路尽头离主线中心线不超过这个距离（米）。</summary>
        private const float kTeeReach = 30f;

        /// <summary>
        /// 虚拟丁字路口：一条路（支路）在另一条路（主线）旁边结束、两者不相连，例如地面道路停在高架下方。
        /// 鼠标指着主线时找尽头停在它旁边的道路；指着支路时找从它尽头旁边经过的道路。
        /// 主线一侧收集与它平行、离交汇点不远的路段（双幅时两幅），在交汇点处切成两半；
        /// 支路一侧收集尽头也停在主线旁边的平行路段（双幅时两幅），各取离开尽头的那一半。
        /// 交汇点取支路尽头在主线中心线上的投影。找不到时返回 null。
        /// </summary>
        private static List<ExternalEdge> ReadTee(EntityManager entityManager, PrefabSystem prefabSystem,
            NativeQuadTree<Entity, QuadTreeBoundsXZ> netTree, Entity hit, float3 cursor,
            IReadOnlyDictionary<string, RoadInfo> roads, ref TerrainHeightData terrain, List<Entity> matched, out (Entity, Entity) key,
            System.Func<(Entity, Entity), float3, bool> isCurrent)
        {
            key = default;
            var candidates = FindTeeEnds(entityManager, netTree, hit).Concat(FindTeeMains(entityManager, netTree, hit)).ToList();
            if (candidates.Count == 0)
                return null;
            var tee = candidates.OrderBy(c => math.distance(c.Point.xz, cursor.xz)).First();
            if (math.distance(tee.Point.xz, cursor.xz) > kCrossingRadius)
                return null;
            key = (tee.Main, tee.Branch);
            if (isCurrent != null && isCurrent(key, tee.Point))
                return Unchanged;
            return CollectAt(entityManager, prefabSystem, netTree, tee.Point, tee.Main, tee.Branch, roads, ref terrain, matched);
        }

        /// <summary>
        /// 在虚拟交汇点 center 周围收集两条道路的路段：与代表路段 roadA / roadB 方向相近（双幅时两幅都算）、
        /// 高度相近（3 米内，避免把高架旁边的地面辅路当成高架的一幅）、离交汇点不远的路段。
        ///   横穿交汇点的路段：离交汇点最近处在交汇点正侧方（沿路方向偏离 5 米内），在那里切成两半；
        ///   停在交汇点旁边结束的路段（虚拟丁字）：最近处是它的尽头，可以离交汇点 kTeeReach，只取离开尽头的那一半；
        ///   伸过交汇点一点点（kTeeReach 内）就到头的那一半不算一条臂（宽容处理）。
        /// 交汇点正好落在节点上时，两侧路段各贡献一半。
        /// </summary>
        private static List<ExternalEdge> CollectAt(EntityManager entityManager, PrefabSystem prefabSystem,
            NativeQuadTree<Entity, QuadTreeBoundsXZ> netTree, float3 center, Entity roadA, Entity roadB,
            IReadOnlyDictionary<string, RoadInfo> roads, ref TerrainHeightData terrain, List<Entity> matched)
        {
            float ElevationNear(Entity edge, ref TerrainHeightData heights)
            {
                Bezier4x3 c = entityManager.GetComponentData<Curve>(edge).m_Bezier;
                MathUtils.Distance(c.xz, center.xz, out float t);
                float3 p = MathUtils.Position(c, t);
                return p.y - TerrainUtils.SampleHeight(ref heights, p);
            }
            float elevationA = ElevationNear(roadA, ref terrain), elevationB = ElevationNear(roadB, ref terrain);
            s_Rejected.Clear();

            var result = new List<ExternalEdge>();
            foreach (Entity candidate in NearbyRoadEdges(entityManager, netTree, center, kPartnerSearch))
            {
                bool groupA = candidate == roadA || (candidate != roadB && Parallel(entityManager, candidate, roadA));
                bool groupB = !groupA && (candidate == roadB || Parallel(entityManager, candidate, roadB));
                if ((!groupA && !groupB) || matched.Contains(candidate))
                    continue;
                if (!TryGetRoad(entityManager, prefabSystem, candidate, roads, out RoadInfo info))
                {
                    s_Rejected.Add($"{(groupA ? "A" : "B")}: not a known road prefab");
                    continue;
                }
                Edge edge = entityManager.GetComponentData<Edge>(candidate);
                Bezier4x3 curve = entityManager.GetComponentData<Curve>(candidate).m_Bezier;
                if (MathUtils.Distance(curve.xz, center.xz, out float t) > kPartnerSearch)
                    continue;
                float3 point = MathUtils.Position(curve, t);
                float elevation = point.y - TerrainUtils.SampleHeight(ref terrain, point);
                if (math.abs(elevation - (groupA ? elevationA : elevationB)) > 3f)
                {
                    s_Rejected.Add($"{(groupA ? "A" : "B")} {info.Name}: elevation {elevation:F1} vs {(groupA ? elevationA : elevationB):F1}");
                    continue;
                }
                float2 direction = math.normalizesafe(MathUtils.Tangent(curve, t).xz);
                float along = math.abs(math.dot(point.xz - center.xz, direction));
                bool atEnd = t <= 0.01f || t >= 0.99f;
                if (along > (atEnd ? kTeeReach : 5f))
                    continue;
                float lengthAfter = MathUtils.Length(MathUtils.Cut(curve, new float2(t, 1f)));
                float lengthBefore = MathUtils.Length(MathUtils.Cut(curve, new float2(0f, t)));
                bool forward = lengthAfter > 0.5f && !(lengthAfter < kTeeReach && IsDeadEnd(entityManager, edge.m_End));
                bool backward = lengthBefore > 0.5f && !(lengthBefore < kTeeReach && IsDeadEnd(entityManager, edge.m_Start));
                if (!forward && !backward)
                    continue;
                var halves = CrossingReader.Split(ToCrossingRoad(curve, info), t, elevation, forward, backward).ToList();
                // 弯道适配：两半各自往外追踪（Split 先给沿曲线方向的一半）
                int half = 0;
                if (forward)
                    halves[half++].Trace = Trace(entityManager, MathUtils.Cut(curve, new float2(t, 1f)), edge.m_End, candidate);
                if (backward)
                    halves[half].Trace = Trace(entityManager, Reverse(MathUtils.Cut(curve, new float2(0f, t))), edge.m_Start, candidate);
                result.AddRange(halves);
                matched.Add(candidate);
            }
            return result;
        }

        private static readonly List<string> s_Rejected = new List<string>();

        /// <summary>虚拟丁字路口的一个候选：支路 Branch 的一端（AtStart）停在主线 Main 旁边，Point 是它在主线中心线上的投影。</summary>
        private readonly struct TeeEnd
        {
            public TeeEnd(Entity main, Entity branch, bool atStart, float3 point)
            {
                Main = main;
                Branch = branch;
                AtStart = atStart;
                Point = point;
            }

            public Entity Main { get; }
            public Entity Branch { get; }
            public bool AtStart { get; }
            public float3 Point { get; }
        }

        /// <summary>把 main 当主线：找一端停在它中心线旁边（kTeeReach 内）、方向大致垂直、与它不相连的道路。</summary>
        private static List<TeeEnd> FindTeeEnds(EntityManager entityManager, NativeQuadTree<Entity, QuadTreeBoundsXZ> netTree, Entity main)
        {
            var result = new List<TeeEnd>();
            if (!TryGetRoadEdge(entityManager, main, out Edge mainEdge, out Curve mainCurve))
                return result;
            var bounds = MathUtils.Bounds(mainCurve.m_Bezier.xz);
            foreach (Entity candidate in RoadEdgesIn(entityManager, netTree, new Bounds2(bounds.min - kTeeReach, bounds.max + kTeeReach)))
            {
                if (candidate == main || !TryGetRoadEdge(entityManager, candidate, out Edge edge, out Curve curve) || Connected(edge, mainEdge))
                    continue;
                foreach (bool atStart in new[] { true, false })
                {
                    float3 end = atStart ? curve.m_Bezier.a : curve.m_Bezier.d;
                    if (MathUtils.Distance(mainCurve.m_Bezier.xz, end.xz, out float t) > kTeeReach)
                        continue;
                    float2 branchDirection = math.normalizesafe((atStart ? MathUtils.StartTangent(curve.m_Bezier) : -MathUtils.EndTangent(curve.m_Bezier)).xz);
                    float2 mainDirection = math.normalizesafe(MathUtils.Tangent(mainCurve.m_Bezier, t).xz);
                    if (math.abs(math.dot(branchDirection, mainDirection)) > math.cos(math.radians(30f)))
                        continue;   // 与主线几乎平行：不是丁字
                    result.Add(new TeeEnd(main, candidate, atStart, MathUtils.Position(mainCurve.m_Bezier, t)));
                }
            }
            return result;
        }

        /// <summary>把 branch 当支路：它的某一端停在另一条不相连道路的中心线旁边时，返回那条道路作为主线。</summary>
        private static List<TeeEnd> FindTeeMains(EntityManager entityManager, NativeQuadTree<Entity, QuadTreeBoundsXZ> netTree, Entity branch)
        {
            var result = new List<TeeEnd>();
            if (!TryGetRoadEdge(entityManager, branch, out _, out Curve curve))
                return result;
            foreach (bool atStart in new[] { true, false })
            {
                float3 end = atStart ? curve.m_Bezier.a : curve.m_Bezier.d;
                foreach (Entity main in NearbyRoadEdges(entityManager, netTree, end, kTeeReach))
                    result.AddRange(FindTeeEnds(entityManager, netTree, main).Where(t => t.Branch == branch && t.AtStart == atStart));
            }
            return result;
        }

        /// <summary>节点是道路尽头：只接着一条道路路段。</summary>
        private static bool IsDeadEnd(EntityManager entityManager, Entity node)
        {
            if (!entityManager.HasBuffer<ConnectedEdge>(node))
                return false;
            int roads = 0;
            foreach (ConnectedEdge connected in entityManager.GetBuffer<ConnectedEdge>(node, true))
                if (TryGetRoadEdge(entityManager, connected.m_Edge, out _, out _))
                    roads++;
            return roads <= 1;
        }

        private static bool Connected(Edge a, Edge b) =>
            a.m_Start == b.m_Start || a.m_Start == b.m_End || a.m_End == b.m_Start || a.m_End == b.m_End;

        /// <summary>包围盒内的道路路段（不含临时、已删除、属于建筑的）。</summary>
        private static List<Entity> RoadEdgesIn(EntityManager entityManager, NativeQuadTree<Entity, QuadTreeBoundsXZ> netTree, Bounds2 bounds)
        {
            var candidates = new NativeList<Entity>(32, Allocator.Temp);
            var result = new List<Entity>();
            try
            {
                var iterator = new RoadRemover.BoundsIterator { m_Bounds = bounds, m_Result = candidates };
                netTree.Iterate(ref iterator);
                foreach (Entity candidate in candidates)
                {
                    if (!result.Contains(candidate) && TryGetRoadEdge(entityManager, candidate, out _, out _)
                        && !entityManager.HasComponent<Deleted>(candidate) && !entityManager.HasComponent<Owner>(candidate))
                        result.Add(candidate);
                }
            }
            finally
            {
                candidates.Dispose();
            }
            return result;
        }

        private static List<Entity> NearbyRoadEdges(EntityManager entityManager, NativeQuadTree<Entity, QuadTreeBoundsXZ> netTree, float3 point, float radius) =>
            RoadEdgesIn(entityManager, netTree, new Bounds2(point.xz - radius, point.xz + radius));

        private static CrossingRoad ToCrossingRoad(Bezier4x3 curve, RoadInfo info) => new CrossingRoad
        {
            Curve = Flat(curve),
            Road = info.Name,
            OneWay = info.IsOneWay,
            Forward = info.ForwardLanes > 0,
            Width = info.Width,
            LanesPerDirection = info.LanesPerDirection,
            IsHighway = info.IsHighway,
        };

        /// <summary>找对向一幅的最大距离（米，中心线之间）。</summary>
        private const float kPartnerSearch = 60f;

        /// <summary>
        /// 鼠标指着双幅道路中的一幅（单行道路）时，找出对向的那一幅，量出实际分隔带宽度：
        /// 附近与它平行、行驶方向相反、在它左侧（右侧通行；左侧通行时在右侧）的单行道路，取最近的一条。
        /// 中心线距离减去两幅半宽之和就是分隔带。找不到（或指着的不是单行道路）时返回 null。
        /// </summary>
        public static float? MeasureDualMedian(EntityManager entityManager, PrefabSystem prefabSystem,
            NativeQuadTree<Entity, QuadTreeBoundsXZ> netTree, Entity hit, IReadOnlyDictionary<string, RoadInfo> roads, bool rightHandTraffic)
        {
            if (hit != Entity.Null && entityManager.Exists(hit) && entityManager.HasComponent<Temp>(hit))
                hit = entityManager.GetComponentData<Temp>(hit).m_Original;
            if (hit == Entity.Null || !entityManager.Exists(hit) || !TryGetRoadEdge(entityManager, hit, out _, out Curve curve)
                || !TryGetRoad(entityManager, prefabSystem, hit, roads, out RoadInfo self) || !self.IsOneWay)
                return null;

            float3 point = MathUtils.Position(curve.m_Bezier, 0.5f);
            float2 travel = math.normalizesafe(MathUtils.Tangent(curve.m_Bezier, 0.5f).xz) * (self.ForwardLanes > 0 ? 1f : -1f);
            float2 partnerSide = rightHandTraffic ? new float2(-travel.y, travel.x) : new float2(travel.y, -travel.x);   // 左侧（右侧通行）

            var candidates = new NativeList<Entity>(32, Allocator.Temp);
            float? best = null;
            try
            {
                var bounds = new Bounds2(point.xz - kPartnerSearch, point.xz + kPartnerSearch);
                var iterator = new RoadRemover.BoundsIterator { m_Bounds = bounds, m_Result = candidates };
                netTree.Iterate(ref iterator);
                foreach (Entity candidate in candidates)
                {
                    if (candidate == hit || !TryGetRoadEdge(entityManager, candidate, out _, out Curve other)
                        || !TryGetRoad(entityManager, prefabSystem, candidate, roads, out RoadInfo info) || !info.IsOneWay)
                        continue;
                    MathUtils.Distance(other.m_Bezier.xz, point.xz, out float t);
                    float2 offset = MathUtils.Position(other.m_Bezier, t).xz - point.xz;
                    float2 otherTravel = math.normalizesafe(MathUtils.Tangent(other.m_Bezier, t).xz) * (info.ForwardLanes > 0 ? 1f : -1f);
                    float across = math.dot(offset, partnerSide);
                    // 平行（15° 以内）、反向、在指定一侧、横向距离主要是分隔带方向（不是前后接着的路段）
                    if (math.dot(otherTravel, travel) > -math.cos(math.radians(15f)) || across <= 0f
                        || math.abs(math.dot(offset, travel)) > 5f || across > kPartnerSearch)
                        continue;
                    float median = across - (self.Width + info.Width) / 2f;
                    if (median >= 0f && (best == null || median < best))
                        best = median;
                }
            }
            finally
            {
                candidates.Dispose();
            }
            return best;
        }

        private static bool TryGetRoad(EntityManager entityManager, PrefabSystem prefabSystem, Entity edge,
            IReadOnlyDictionary<string, RoadInfo> roads, out RoadInfo info)
        {
            info = null;
            return prefabSystem.TryGetPrefab(entityManager.GetComponentData<PrefabRef>(edge).m_Prefab, out PrefabBase prefab)
                && roads.TryGetValue(prefab.name, out info);
        }

        private readonly struct CrossingHit
        {
            public CrossingHit(Entity edge, float t, float3 point)
            {
                Edge = edge;
                T = t;
                Point = point;
            }

            /// <summary>与之相交的路段。</summary>
            public Entity Edge { get; }
            /// <summary>交叉点在这条路段上的参数。</summary>
            public float T { get; }
            public float3 Point { get; }
        }


        /// <summary>与 self 相交（水平面上）但不相连的道路路段，以及交叉点在它们上面的参数。</summary>
        private static List<CrossingHit> FindCrossings(EntityManager entityManager, NativeQuadTree<Entity, QuadTreeBoundsXZ> netTree, Entity self)
        {
            Edge selfEdge = entityManager.GetComponentData<Edge>(self);
            Bezier4x3 selfCurve = entityManager.GetComponentData<Curve>(self).m_Bezier;
            var bounds = MathUtils.Bounds(selfCurve.xz);
            var candidates = new NativeList<Entity>(32, Allocator.Temp);
            var result = new List<CrossingHit>();
            try
            {
                var iterator = new RoadRemover.BoundsIterator { m_Bounds = bounds, m_Result = candidates };
                netTree.Iterate(ref iterator);
                var seen = new HashSet<Entity>();
                foreach (Entity candidate in candidates)
                {
                    if (candidate == self || !seen.Add(candidate) || !TryGetRoadEdge(entityManager, candidate, out Edge edge, out Curve curve)
                        || entityManager.HasComponent<Deleted>(candidate) || entityManager.HasComponent<Owner>(candidate))
                        continue;
                    // 相连的路段（共用节点）是普通路口，不算立体交叉
                    if (edge.m_Start == selfEdge.m_Start || edge.m_Start == selfEdge.m_End || edge.m_End == selfEdge.m_Start || edge.m_End == selfEdge.m_End)
                        continue;
                    if (CrossingReader.Intersect(Flat(selfCurve), Flat(curve.m_Bezier), out _, out float t))
                        result.Add(new CrossingHit(candidate, t, MathUtils.Position(curve.m_Bezier, t)));
                }
            }
            finally
            {
                candidates.Dispose();
            }
            return result;
        }

        /// <summary>两条路段方向相近（同向或反向，25° 以内）。</summary>
        private static bool Parallel(EntityManager entityManager, Entity a, Entity b)
        {
            float2 da = math.normalizesafe(MathUtils.Tangent(entityManager.GetComponentData<Curve>(a).m_Bezier, 0.5f).xz);
            float2 db = math.normalizesafe(MathUtils.Tangent(entityManager.GetComponentData<Curve>(b).m_Bezier, 0.5f).xz);
            return math.abs(math.dot(da, db)) >= math.cos(math.radians(25f));
        }

        /// <summary>弯道适配：沿道路往外追踪中心线的长度（米，大型立交缓坡时臂端离中心可达一千多米）和取点间距。</summary>
        private const float kTraceLength = 2000f, kTraceStep = 4f;

        /// <summary>
        /// 弯道适配（docs/04）：从 first（已按离开路口的方向）出发往外取俯视点，到头的节点上接着走与当前方向最一致
        /// （夹角 30° 内）的道路路段，直到 kTraceLength 米或没有路可走（之后按直线延伸，见 Centerline）。
        /// </summary>
        private static List<Vec3> Trace(EntityManager entityManager, Bezier4x3 first, Entity farNode, Entity fromEdge)
        {
            var points = new List<Vec3>();
            var visited = new HashSet<Entity> { fromEdge };
            float length = 0f, alignedEnough = math.cos(math.radians(30f));
            Bezier4x3 curve = first;
            Entity node = farNode;
            while (true)
            {
                float curveLength = MathUtils.Length(curve.xz);
                int n = math.max(1, (int)math.ceil(curveLength / kTraceStep));
                for (int i = points.Count == 0 ? 0 : 1; i <= n; i++)
                {
                    float3 p = MathUtils.Position(curve, (float)i / n);
                    points.Add(new Vec3(p.x, 0f, p.z));
                }
                length += curveLength;
                if (length >= kTraceLength || node == Entity.Null || !entityManager.HasBuffer<ConnectedEdge>(node))
                    break;
                float2 direction = math.normalizesafe(MathUtils.EndTangent(curve).xz);
                Entity next = Entity.Null, nextNode = Entity.Null;
                Bezier4x3 nextCurve = default;
                float best = alignedEnough;
                foreach (ConnectedEdge connected in entityManager.GetBuffer<ConnectedEdge>(node, true))
                {
                    if (visited.Contains(connected.m_Edge) || !TryGetRoadEdge(entityManager, connected.m_Edge, out Edge edge, out Curve c))
                        continue;
                    bool atStart = edge.m_Start == node;
                    Bezier4x3 oriented = atStart ? c.m_Bezier : Reverse(c.m_Bezier);
                    float alignment = math.dot(direction, math.normalizesafe(MathUtils.StartTangent(oriented).xz));
                    if (alignment > best)
                    {
                        best = alignment;
                        next = connected.m_Edge;
                        nextCurve = oriented;
                        nextNode = atStart ? edge.m_End : edge.m_Start;
                    }
                }
                if (next == Entity.Null)
                    break;
                visited.Add(next);
                curve = nextCurve;
                node = nextNode;
            }
            return points;
        }

        private static Bezier4x3 Reverse(Bezier4x3 c) => new Bezier4x3(c.d, c.c, c.b, c.a);

        private static Bezier3 Flat(Bezier4x3 curve) => new Bezier3(
            new Vec3(curve.a.x, 0f, curve.a.z), new Vec3(curve.b.x, 0f, curve.b.z),
            new Vec3(curve.c.x, 0f, curve.c.z), new Vec3(curve.d.x, 0f, curve.d.z));

        private static bool TryGetRoadEdge(EntityManager entityManager, Entity entity, out Edge edge, out Curve curve)
        {
            edge = default;
            curve = default;
            if (!entityManager.HasComponent<Edge>(entity) || !entityManager.HasComponent<Curve>(entity)
                || entityManager.HasComponent<Temp>(entity) || !entityManager.HasComponent<PrefabRef>(entity))
                return false;
            Entity prefab = entityManager.GetComponentData<PrefabRef>(entity).m_Prefab;
            if (!entityManager.HasComponent<RoadData>(prefab))
                return false;
            edge = entityManager.GetComponentData<Edge>(entity);
            curve = entityManager.GetComponentData<Curve>(entity);
            return true;
        }
    }
}
