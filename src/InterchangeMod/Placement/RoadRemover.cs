using System.Collections.Generic;
using System.Linq;
using Colossal.Collections;
using Colossal.Entities;
using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using InterchangeMod.Generation.Geometry;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace InterchangeMod.Placement
{
    /// <summary>
    /// 原路口改造：拆掉立交范围（Footprint）内的原有道路，范围外的部分保留并接回原来的节点。
    ///   每条和范围相交的原有道路写一个删除定义（和推土机一样），
    ///   范围外的每一段再写一个新建定义：在原有节点那一端接回原节点，在范围边界那一端新建节点。
    /// 这些定义和立交本身一起预览、一起确认，可以用游戏的撤销恢复。
    /// </summary>
    public static class RoadRemover
    {
        /// <summary>和要清理的各层高度（levels）都相差超过这个值的道路（上方的高架、下方的隧道）不拆。</summary>
        public const float kElevationTolerance = 4.5f;

        /// <summary>保留部分短于这个长度就不建了（游戏也建不出这么短的路段）。</summary>
        private const float kMinRemainderLength = 2f;

        /// <summary>臂端在原有道路上的投影离切断点不超过这个距离、高度差不超过 kConnectElevation 时接回立交。</summary>
        private const float kConnectDistance = 8f;
        // 原有高架沿线高度会随地形略有起伏（游戏内实测交叉处 11.1 米、两端 12.1 米），容差放宽到 3 米；接上后端点高度取臂端的
        private const float kConnectElevation = 3f;
        /// <summary>臂端离原有道路中心线的最大距离（米）。</summary>
        private const float kConnectLateral = 6f;
        /// <summary>接不上臂端的切断端再往外退的距离（米），另加这条路的半宽：宽路斜着穿出范围时，端头两角不伸进立交。</summary>
        private const float kUnconnectedGap = 10f;

        /// <summary>
        /// 找出要拆的原有道路：和范围相交、离某一层高度（levels：普通改造只有基层，立体交叉还有被替换高架的高度）不远、
        /// 不属于建筑等其他对象的普通道路路段。
        /// footprint 是立交局部坐标（origin 为原点、yaw 旋转前）。
        /// </summary>
        public static List<Entity> FindEdges(EntityManager entityManager, NativeQuadTree<Entity, QuadTreeBoundsXZ> netTree,
            Footprint footprint, float3 origin, float yaw, IReadOnlyList<float> levels, ref TerrainHeightData terrain,
            ICollection<Entity> alwaysRemove = null)
        {
            var candidates = new NativeList<Entity>(64, Allocator.Temp);
            var result = new List<Entity>();
            try
            {
                var iterator = new BoundsIterator { m_Bounds = WorldBounds(footprint, origin, yaw), m_Result = candidates };
                netTree.Iterate(ref iterator);

                var seen = new HashSet<Entity>();
                foreach (Entity entity in candidates)
                {
                    if (!seen.Add(entity) || !IsRemovableRoad(entityManager, entity))
                        continue;
                    Bezier4x3 world = entityManager.GetComponentData<Curve>(entity).m_Bezier;
                    // 立体交叉匹配到的路段（如被替换的高架）不论高度都拆，其余只拆基层附近的
                    bool overlaps = alwaysRemove != null && alwaysRemove.Contains(entity)
                        ? footprint.Intersects(ToLocal(world, origin, yaw))
                        : OverlapsAtBaseLevel(ToLocal(world, origin, yaw), world, footprint, levels, ref terrain);
                    if (overlaps)
                        result.Add(entity);
                }
            }
            finally
            {
                candidates.Dispose();
            }
            return result;
        }

        /// <summary>
        /// 为每条要拆的道路写删除定义，并为范围外的部分写新建定义。
        /// 保留部分在范围边界那一端如果附近有同一高度的臂端（legEnds，局部坐标），就直接从臂端开始，接回立交。
        /// 返回接上的臂端数。
        /// </summary>
        public static int CreateDefinitions(EntityCommandBuffer commandBuffer, EntityManager entityManager, List<Entity> edges,
            Footprint footprint, IReadOnlyList<Vec3> legEnds, float3 origin, float yaw, ref TerrainHeightData terrain, int randomSeed)
        {
            // 和立交路段用同一个换算，得到完全相同的世界坐标，游戏才会把两个节点合并
            var ends = new List<(float3 World, float Elevation)>();
            foreach (Vec3 end in legEnds)
                ends.Add((InterchangePlacer.ToWorld(end, origin, yaw, ref terrain), end.Y));
            var used = new bool[ends.Count];
            int connected = 0;

            foreach (Entity entity in edges)
            {
                Edge edge = entityManager.GetComponentData<Edge>(entity);
                Bezier4x3 curve = entityManager.GetComponentData<Curve>(entity).m_Bezier;
                Entity prefab = entityManager.GetComponentData<PrefabRef>(entity).m_Prefab;
                float halfWidth = entityManager.TryGetComponent(prefab, out NetGeometryData prefabGeometry) ? prefabGeometry.m_DefaultWidth / 2f : 0f;

                CreateDelete(commandBuffer, entity, edge, curve);

                // 保留部分沿用原来的方向（单行道路的行驶方向不变）
                float edgeLength = MathUtils.Length(curve);
                foreach (var (outsideStart, outsideEnd) in footprint.OutsideIntervals(ToLocal(curve, origin, yaw)))
                {
                    float start = outsideStart, end = outsideEnd;
                    // 切断端：附近有同一高度的臂端就接上；接不上（比如在高架的臂端下面）就再退 kUnconnectedGap，避开臂端节点和立柱
                    int startTarget = start > 0f ? FindEnd(ends, used, curve, edgeLength, ref start, ref terrain) : -1;
                    if (startTarget >= 0)
                        used[startTarget] = true;
                    int endTarget = end < 1f ? FindEnd(ends, used, curve, edgeLength, ref end, ref terrain) : -1;
                    if (endTarget >= 0)
                        used[endTarget] = true;
                    // 高架的切断端还要多退一些：末端节点下有立柱，不能落在立交新建的道路上
                    if (start > 0f && startTarget < 0)
                        start += (kUnconnectedGap + halfWidth + ElevatedExtra(MathUtils.Position(curve, start), ref terrain)) / edgeLength;
                    if (end < 1f && endTarget < 0)
                        end -= (kUnconnectedGap + halfWidth + ElevatedExtra(MathUtils.Position(curve, end), ref terrain)) / edgeLength;

                    Bezier4x3 part = MathUtils.Cut(curve, new float2(start, end));
                    if (end <= start || MathUtils.Length(part) < kMinRemainderLength)
                    {
                        if (startTarget >= 0) used[startTarget] = false;
                        if (endTarget >= 0) used[endTarget] = false;
                        continue;
                    }
                    float startElevation = ElevationAt(part.a, ref terrain), endElevation = ElevationAt(part.d, ref terrain);
                    if (startTarget >= 0)
                    {
                        Snap(ends[startTarget], ref part.a, ref part.b, ref startElevation);
                        connected++;
                    }
                    if (endTarget >= 0)
                    {
                        Snap(ends[endTarget], ref part.d, ref part.c, ref endElevation);
                        connected++;
                    }

                    // 保留部分太短（比路宽还短）时游戏会报“形状无效”。如果它另一端是普通的路中节点（只接着同一种道路的下一段），
                    // 就把下一段也拆掉，从臂端直接重建到下一段的另一端，两段合成一段
                    float length = MathUtils.Length(part);
                    float width = entityManager.TryGetComponent(prefab, out NetGeometryData geometry) ? geometry.m_DefaultWidth : 0f;
                    bool merged = false;
                    if (length < width && (startTarget >= 0) != (endTarget >= 0))
                    {
                        Entity node = startTarget >= 0 ? edge.m_End : edge.m_Start;
                        var legEnd = startTarget >= 0 ? ends[startTarget] : ends[endTarget];
                        merged = (startTarget >= 0 ? end >= 1f : start <= 0f)
                            && TryMergeThroughNode(commandBuffer, entityManager, node, entity, prefab, edges, legEnd, ref terrain, randomSeed++);
                    }
                    if (Debug)
                        Mod.log.Info($"[debug] remainder of edge {entity.Index}: {length:F1} m (road {width:F0} m), " +
                            $"start {(startTarget >= 0 ? "leg end" : start <= 0f ? "original node" : "cut")}, " +
                            $"end {(endTarget >= 0 ? "leg end" : end >= 1f ? "original node" : "cut")}{(merged ? ", merged with the next edge" : "")}");
                    if (merged)
                        continue;
                    NetCourseBuilder.CreateSegment(commandBuffer, prefab, part, startElevation, endElevation, randomSeed++,
                        start <= 0f ? edge.m_Start : Entity.Null,
                        end >= 1f ? edge.m_End : Entity.Null);
                }
            }
            return connected;
        }

        /// <summary>
        /// 切断点 t 附近、同一高度、还没接过的臂端：臂端在原有道路上的投影离切断点不超过 kConnectDistance，
        /// 离道路中心线不超过 kConnectLateral。找到时把 t 改成投影位置（在那里重新切，保留部分不会折回），返回臂端序号；没有时返回 -1。
        /// </summary>
        private static int FindEnd(List<(float3 World, float Elevation)> ends, bool[] used, Bezier4x3 curve, float edgeLength,
            ref float t, ref TerrainHeightData terrain)
        {
            int best = -1;
            float bestLateral = kConnectLateral, bestT = t;
            for (int i = 0; i < ends.Count; i++)
            {
                if (used[i])
                    continue;
                float projected = NearestT(curve, ends[i].World);
                float3 onRoad = MathUtils.Position(curve, projected);
                float lateral = math.distance(onRoad.xz, ends[i].World.xz);
                if (lateral > bestLateral || math.abs(projected - t) * edgeLength > kConnectDistance
                    || math.abs(ends[i].Elevation - ElevationAt(onRoad, ref terrain)) > kConnectElevation)
                    continue;
                best = i;
                bestLateral = lateral;
                bestT = projected;
            }
            t = bestT;
            return best;
        }

        /// <summary>曲线上俯视离 point 最近的参数 t：先粗采样，再在最近样本附近二分细化。</summary>
        private static float NearestT(Bezier4x3 curve, float3 point)
        {
            const int samples = 64;
            float best = 0f, bestDistance = float.MaxValue;
            for (int i = 0; i <= samples; i++)
            {
                float t = i / (float)samples;
                float distance = math.distancesq(MathUtils.Position(curve, t).xz, point.xz);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = t;
                }
            }
            float lo = math.max(0f, best - 1f / samples), hi = math.min(1f, best + 1f / samples);
            for (int i = 0; i < 24; i++)
            {
                float a = lo + (hi - lo) / 3f, b = hi - (hi - lo) / 3f;
                if (math.distancesq(MathUtils.Position(curve, a).xz, point.xz) < math.distancesq(MathUtils.Position(curve, b).xz, point.xz))
                    hi = b;
                else
                    lo = a;
            }
            return (lo + hi) * 0.5f;
        }

        /// <summary>把切断端（point，相邻控制点 handle）移到臂端上；手柄一起平移，保持方向。</summary>
        private static void Snap((float3 World, float Elevation) end, ref float3 point, ref float3 handle, ref float elevation)
        {
            handle += end.World - point;
            point = end.World;
            elevation = end.Elevation;
        }

        /// <summary>写调试日志（由工具按设置里的调试开关设置）。</summary>
        public static bool Debug { get; set; }

        /// <summary>
        /// 太短的保留部分：它的另一端 node 若只接着同一种道路的下一段（普通路中节点），就把下一段拆掉，
        /// 从臂端 legEnd 直接重建到下一段的另一端（下一段的方向、形状不变，只把 node 那一端移到臂端）。成功返回 true。
        /// </summary>
        private static bool TryMergeThroughNode(EntityCommandBuffer commandBuffer, EntityManager entityManager, Entity node, Entity self,
            Entity prefab, List<Entity> removing, (float3 World, float Elevation) legEnd, ref TerrainHeightData terrain, int randomSeed)
        {
            if (node == Entity.Null || !entityManager.HasBuffer<ConnectedEdge>(node))
                return false;
            Entity next = Entity.Null;
            foreach (ConnectedEdge connected in entityManager.GetBuffer<ConnectedEdge>(node, true))
            {
                if (connected.m_Edge == self || !IsRemovableRoad(entityManager, connected.m_Edge))
                    continue;
                if (next != Entity.Null)
                    return false;   // 节点是路口（接着不止一段），不能合并
                next = connected.m_Edge;
            }
            if (next == Entity.Null || removing.Contains(next) || entityManager.GetComponentData<PrefabRef>(next).m_Prefab != prefab)
                return false;

            Edge nextEdge = entityManager.GetComponentData<Edge>(next);
            Bezier4x3 curve = entityManager.GetComponentData<Curve>(next).m_Bezier;
            CreateDelete(commandBuffer, next, nextEdge, curve);
            bool nodeAtStart = nextEdge.m_Start == node;
            float startElevation = ElevationAt(curve.a, ref terrain), endElevation = ElevationAt(curve.d, ref terrain);
            if (nodeAtStart)
                Snap(legEnd, ref curve.a, ref curve.b, ref startElevation);
            else
                Snap(legEnd, ref curve.d, ref curve.c, ref endElevation);
            NetCourseBuilder.CreateSegment(commandBuffer, prefab, curve, startElevation, endElevation, randomSeed,
                nodeAtStart ? Entity.Null : nextEdge.m_Start, nodeAtStart ? nextEdge.m_End : Entity.Null);
            return true;
        }

        /// <summary>和推土机（BulldozeToolSystem）删除路段的写法一样。</summary>
        private static void CreateDelete(EntityCommandBuffer commandBuffer, Entity entity, Edge edge, Bezier4x3 curve)
        {
            var course = new NetCourse
            {
                m_Curve = curve,
                m_Length = MathUtils.Length(curve),
                m_FixedIndex = -1,
            };
            course.m_StartPosition.m_Entity = edge.m_Start;
            course.m_StartPosition.m_Position = curve.a;
            course.m_StartPosition.m_Rotation = NetUtils.GetNodeRotation(MathUtils.StartTangent(curve));
            course.m_StartPosition.m_CourseDelta = 0f;
            course.m_EndPosition.m_Entity = edge.m_End;
            course.m_EndPosition.m_Position = curve.d;
            course.m_EndPosition.m_Rotation = NetUtils.GetNodeRotation(MathUtils.EndTangent(curve));
            course.m_EndPosition.m_CourseDelta = 1f;

            Entity definition = commandBuffer.CreateEntity();
            commandBuffer.AddComponent(definition, new CreationDefinition { m_Original = entity, m_Flags = CreationFlags.Delete });
            commandBuffer.AddComponent(definition, default(Updated));
            commandBuffer.AddComponent(definition, course);
        }

        private static bool IsRemovableRoad(EntityManager entityManager, Entity entity)
        {
            if (!entityManager.HasComponent<Edge>(entity) || !entityManager.HasComponent<Curve>(entity)
                || !entityManager.HasComponent<PrefabRef>(entity)
                || entityManager.HasComponent<Temp>(entity) || entityManager.HasComponent<Deleted>(entity)
                || entityManager.HasComponent<Owner>(entity))   // 建筑、车站等自带的道路不动
                return false;
            return entityManager.HasComponent<RoadData>(entityManager.GetComponentData<PrefabRef>(entity).m_Prefab);
        }

        /// <summary>道路在范围内的部分里，有没有一点离某一层高度不远。</summary>
        private static bool OverlapsAtBaseLevel(Bezier3 local, Bezier4x3 world, Footprint footprint, IReadOnlyList<float> levels,
            ref TerrainHeightData terrain)
        {
            const int samples = 32;
            for (int i = 0; i <= samples; i++)
            {
                float t = i / (float)samples;
                if (!footprint.Contains(local.Evaluate(t)))
                    continue;
                float elevation = ElevationAt(MathUtils.Position(world, t), ref terrain);
                if (levels.Any(level => math.abs(elevation - level) <= kElevationTolerance))
                    return true;
            }
            return false;
        }

        /// <summary>高架（离地达到桥梁高度）的切断端多退的距离（米）。</summary>
        private const float kElevatedCutGap = 20f;

        private static float ElevatedExtra(float3 position, ref TerrainHeightData terrain) =>
            ElevationAt(position, ref terrain) >= InterchangeMod.Generation.HeightRange.BridgeThreshold ? kElevatedCutGap : 0f;

        private static float ElevationAt(float3 position, ref TerrainHeightData terrain) =>
            position.y - TerrainUtils.SampleHeight(ref terrain, position);

        /// <summary>世界坐标 → 立交局部坐标（InterchangePlacer.ToWorld 的逆变换，高度不用）。</summary>
        private static Bezier3 ToLocal(Bezier4x3 world, float3 origin, float yaw) =>
            new Bezier3(ToLocal(world.a, origin, yaw), ToLocal(world.b, origin, yaw), ToLocal(world.c, origin, yaw), ToLocal(world.d, origin, yaw));

        private static Vec3 ToLocal(float3 world, float3 origin, float yaw) =>
            new Vec3(world.x - origin.x, 0f, world.z - origin.z).RotateY(-yaw);

        private static Bounds2 WorldBounds(Footprint footprint, float3 origin, float yaw)
        {
            var bounds = new Bounds2(new float2(float.MaxValue), new float2(float.MinValue));
            foreach (var (x, z) in new[] { (footprint.MinX, footprint.MinZ), (footprint.MinX, footprint.MaxZ), (footprint.MaxX, footprint.MinZ), (footprint.MaxX, footprint.MaxZ) })
            {
                Vec3 rotated = new Vec3(x, 0f, z).RotateY(yaw);
                bounds |= new float2(origin.x + rotated.X, origin.z + rotated.Z);
            }
            return bounds;
        }

        /// <summary>从游戏的道路空间索引里取出包围盒相交的实体（节点和路段都有，之后再筛）。</summary>
        internal struct BoundsIterator : INativeQuadTreeIterator<Entity, QuadTreeBoundsXZ>
        {
            public Bounds2 m_Bounds;
            public NativeList<Entity> m_Result;

            public bool Intersect(QuadTreeBoundsXZ bounds) => MathUtils.Intersect(bounds.m_Bounds.xz, m_Bounds);

            public void Iterate(QuadTreeBoundsXZ bounds, Entity entity)
            {
                if (MathUtils.Intersect(bounds.m_Bounds.xz, m_Bounds))
                    m_Result.Add(in entity);
            }
        }
    }
}
