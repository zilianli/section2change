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
                var edge = entityManager.GetComponentData<Edge>(hit);
                float3 start = entityManager.GetComponentData<Node>(edge.m_Start).m_Position;
                float3 end = entityManager.GetComponentData<Node>(edge.m_End).m_Position;
                return math.distancesq(start.xz, hitPosition.xz) <= math.distancesq(end.xz, hitPosition.xz) ? edge.m_Start : edge.m_End;
            }
            return Entity.Null;
        }

        public static List<ExternalEdge> Read(EntityManager entityManager, PrefabSystem prefabSystem, Entity seed,
            IReadOnlyDictionary<string, RoadInfo> roads, ref TerrainHeightData terrain)
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
                    });
                }
            }
            return result;
        }

        /// <summary>立体交叉：鼠标离交叉点不超过这个距离时按交叉点匹配。</summary>
        public const float kCrossingRadius = 60f;

        /// <summary>
        /// 立体交叉（两条道路相交、没有共用节点，如高架跨过街道）：从鼠标指着的路段出发，找离鼠标最近的、
        /// 与它相交但不相连的道路，然后两边各收集与对方相交的平行路段（双幅时就是两幅），
        /// 每条路段在交叉点处切成两半作为离开交叉点的路段（见 CrossingReader）。
        /// 鼠标没指着路段、附近没有立体交叉时返回 null。matched 收到参与交叉的所有路段（改造时要拆掉）。
        /// </summary>
        public static List<ExternalEdge> ReadCrossing(EntityManager entityManager, PrefabSystem prefabSystem,
            NativeQuadTree<Entity, QuadTreeBoundsXZ> netTree, Entity hit, float3 cursor,
            IReadOnlyDictionary<string, RoadInfo> roads, ref TerrainHeightData terrain, List<Entity> matched, out (Entity, Entity) key)
        {
            key = default;
            if (hit != Entity.Null && entityManager.Exists(hit) && entityManager.HasComponent<Temp>(hit))
                hit = entityManager.GetComponentData<Temp>(hit).m_Original;
            if (hit == Entity.Null || !entityManager.Exists(hit) || !TryGetRoadEdge(entityManager, hit, out _, out _))
                return null;

            var fromHit = FindCrossings(entityManager, netTree, hit);
            if (fromHit.Count == 0)
                return null;
            var nearest = fromHit.OrderBy(c => math.distance(c.Point.xz, cursor.xz)).First();
            if (math.distance(nearest.Point.xz, cursor.xz) > kCrossingRadius)
                return null;
            Entity other = nearest.Edge;
            float3 center = nearest.Point;
            key = (hit, other);

            // 两边各收集：与对方道路相交、方向与本方相近、离交叉点不远的路段
            var sideA = FindCrossings(entityManager, netTree, other).Where(c => Near(c, center) && Parallel(entityManager, c.Edge, hit)).ToList();
            var sideB = fromHit.Where(c => Near(c, center) && Parallel(entityManager, c.Edge, other)).ToList();

            var result = new List<ExternalEdge>();
            foreach (var crossing in sideA.Concat(sideB))
            {
                if (matched.Contains(crossing.Edge)) continue;
                if (!prefabSystem.TryGetPrefab(entityManager.GetComponentData<PrefabRef>(crossing.Edge).m_Prefab, out PrefabBase prefab)
                    || !roads.TryGetValue(prefab.name, out RoadInfo info))
                    continue;
                Bezier4x3 curve = entityManager.GetComponentData<Curve>(crossing.Edge).m_Bezier;
                var road = new CrossingRoad
                {
                    Curve = Flat(curve),
                    Road = info.Name,
                    OneWay = info.IsOneWay,
                    Forward = info.ForwardLanes > 0,
                    Width = info.Width,
                    LanesPerDirection = info.LanesPerDirection,
                    IsHighway = info.IsHighway,
                };
                float3 point = MathUtils.Position(curve, crossing.T);
                result.AddRange(CrossingReader.Split(road, crossing.T, point.y - TerrainUtils.SampleHeight(ref terrain, point)));
                matched.Add(crossing.Edge);
            }
            return result;
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

        private static bool Near(CrossingHit hit, float3 center) => math.distance(hit.Point.xz, center.xz) <= kCrossingRadius;

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
