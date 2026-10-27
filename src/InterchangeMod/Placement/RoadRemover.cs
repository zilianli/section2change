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
    /// 原路口改造：拆掉立交范围内的原有道路，范围外的部分保留并接回原来的节点。
    ///   被改造的道路（匹配到的路口 / 立体交叉的路段，沿路往外的各段）按凸包范围拆；其他道路只拆被新路压到的部分——
    ///   平面上挨着新路且高度上没有净空、或落在新路的立柱上（弯道上凸包会把弯道内侧几百米外的街道也包进去；
    ///   从高架匝道下面、净空够的地方穿过的地面道路保留）。
    ///   每条和范围相交的原有道路写一个删除定义（和推土机一样），
    ///   范围外的每一段再写一个新建定义：在原有节点那一端接回原节点，在范围边界那一端新建节点。
    ///   臂端压着的原有道路在臂端处切开，朝外的一侧（连同再往外经过普通路中节点、仍在范围里的几段）保留并接回立交。
    /// 这些定义和立交本身一起预览、一起确认（游戏不支持撤销，确认后不能恢复）。
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
        /// <summary>
        /// 臂端落在切掉的那部分里时，最多离切断点多远（米）也接：弯道上立交范围（凸包）横跨弯道内侧，原有道路在臂端之外
        /// 还会在范围里走一段，切断点离臂端远了接不上；这时改在臂端处切，臂端之外那段保留并接回。
        /// </summary>
        private const float kConnectInside = 300f;
        /// <summary>判断臂端哪一侧朝外时，沿原有道路前后各取多远（米）比较离立交中心的距离。</summary>
        private const float kOutwardProbe = 10f;
        /// <summary>臂端之外沿原路整段保留的路段数上限。</summary>
        private const int kMaxKeptEdges = 32;
        /// <summary>接不上臂端的切断端再往外退的距离（米），另加这条路的半宽：宽路斜着穿出范围时，端头两角不伸进立交。</summary>
        private const float kUnconnectedGap = 10f;
        /// <summary>被改造的道路沿路往外最多认多少段（防止沿很长的路一直走下去）。</summary>
        private const int kMaxConvertedEdges = 64;

        /// <summary>
        /// 找出要拆的原有道路（不属于建筑等其他对象的普通道路路段，离某一层高度不远：levels 普通改造是立交各层，立体交叉还有
        /// 两条原有道路原来的高度）：
        ///   被改造的道路（convertedSeeds 匹配到的路段，以及沿路经过普通路中节点往外、仍和凸包相交的各段）：和凸包 hull 相交就拆，
        ///   立体交叉匹配到的路段（alwaysRemove，如被替换的高架）不论高度都拆；
        ///   其他道路：被新路压到时才拆——中心线离新路不到（新路半宽 + 余量 + 自己的半宽）且高度上没有净空，或落在新路的立柱上
        ///   （corridor.Collides，按这条路和那里新路的实际离地高度）。
        /// converted 收到被改造道路的路段（CreateDefinitions 按它选切断的范围）。范围都是立交局部坐标（origin 为原点、yaw 旋转前）。
        /// </summary>
        public static List<Entity> FindEdges(EntityManager entityManager, NativeQuadTree<Entity, QuadTreeBoundsXZ> netTree,
            Footprint hull, Footprint corridor, float3 origin, float yaw, IReadOnlyList<float> levels, ref TerrainHeightData terrain,
            IEnumerable<Entity> convertedSeeds, ICollection<Entity> alwaysRemove, out HashSet<Entity> converted)
        {
            converted = Converted(entityManager, convertedSeeds, hull, origin, yaw);
            var candidates = new NativeList<Entity>(64, Allocator.Temp);
            var result = new List<Entity>();
            try
            {
                var bounds = WorldBounds(hull, origin, yaw) | WorldBounds(corridor, origin, yaw);
                var iterator = new BoundsIterator { m_Bounds = bounds, m_Result = candidates };
                netTree.Iterate(ref iterator);

                var seen = new HashSet<Entity>();
                foreach (Entity entity in candidates)
                {
                    if (!seen.Add(entity) || !IsRemovableRoad(entityManager, entity))
                        continue;
                    Bezier4x3 world = entityManager.GetComponentData<Curve>(entity).m_Bezier;
                    Bezier3 local = ToLocal(world, origin, yaw);
                    bool overlaps = alwaysRemove != null && alwaysRemove.Contains(entity) ? hull.Intersects(local)
                        : converted.Contains(entity) ? OverlapsAtLevels(local, world, hull, levels, ref terrain)
                        : CollidesWithNewRoads(local, world, corridor, HalfWidth(entityManager, entity), ref terrain);
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
        /// 被改造的道路：seeds 里的路段，再从它们两端沿路经过普通路中节点往外，仍和凸包相交的各段（到路口、路的尽头或凸包外为止）。
        /// </summary>
        private static HashSet<Entity> Converted(EntityManager entityManager, IEnumerable<Entity> seeds, Footprint hull, float3 origin, float yaw)
        {
            var result = new HashSet<Entity>();
            var queue = new Queue<Entity>();
            foreach (Entity seed in seeds ?? Enumerable.Empty<Entity>())
                if (entityManager.Exists(seed) && IsRemovableRoad(entityManager, seed) && result.Add(seed))
                    queue.Enqueue(seed);
            while (queue.Count > 0 && result.Count < kMaxConvertedEdges)
            {
                Entity current = queue.Dequeue();
                Edge edge = entityManager.GetComponentData<Edge>(current);
                foreach (Entity node in new[] { edge.m_Start, edge.m_End })
                {
                    Entity next = NextAlongRoad(entityManager, node, current);
                    if (next == Entity.Null || result.Contains(next)
                        || !hull.Intersects(ToLocal(entityManager.GetComponentData<Curve>(next).m_Bezier, origin, yaw)))
                        continue;
                    result.Add(next);
                    queue.Enqueue(next);
                }
            }
            return result;
        }

        /// <summary>原有道路的半宽（米，按它的预制体）。</summary>
        private static float HalfWidth(EntityManager entityManager, Entity entity)
        {
            Entity prefab = entityManager.GetComponentData<PrefabRef>(entity).m_Prefab;
            return entityManager.TryGetComponent(prefab, out NetGeometryData geometry) ? geometry.m_DefaultWidth / 2f : 0f;
        }

        /// <summary>
        /// 为每条要拆的道路写删除定义，并为范围外的部分写新建定义。
        /// 每个臂端（legEnds，局部坐标）先找它压着的那段原有道路，在臂端处切开、保留朝外的一侧接回立交，再往外经过普通路中节点的
        /// 路段整段保留（从 edges 里去掉，不拆）；其余保留部分在范围边界那一端如果附近有同一高度的臂端，也直接从臂端开始。
        /// 范围边界：被改造的道路（converted）是凸包 hull，其他道路是被新路压到的部分（corridor.Collides，加上自己的半宽）。
        /// 返回接上的臂端数。
        /// </summary>
        public static int CreateDefinitions(EntityCommandBuffer commandBuffer, EntityManager entityManager, List<Entity> edges,
            Footprint hull, Footprint corridor, HashSet<Entity> converted, IReadOnlyList<Vec3> legEnds, float3 origin, float yaw,
            ref TerrainHeightData terrain, int randomSeed)
        {
            // 和立交路段用同一个换算，得到完全相同的世界坐标，游戏才会把两个节点合并
            var ends = new List<(float3 World, float Elevation)>();
            foreach (Vec3 end in legEnds)
                ends.Add((InterchangePlacer.ToWorld(end, origin, yaw, ref terrain), end.Y));
            var used = new bool[ends.Count];
            int connected = 0;

            // 只按范围切的话，臂端压在整段都在范围里的路段上、或弯道上范围横跨弯道内侧（原有道路出了臂端还在范围里走一段）时，
            // 切断点离臂端远，接不上：所以先按臂端切，保留离立交中心远的一侧
            var attached = new Dictionary<Entity, List<(float T, bool KeepAfter, int End)>>();
            for (int i = 0; i < ends.Count; i++)
            {
                Entity onEdge = Attach(entityManager, edges, ends[i], origin, ref terrain, out float t, out bool keepAfter);
                if (onEdge == Entity.Null)
                    continue;
                if (!attached.TryGetValue(onEdge, out var list))
                    attached[onEdge] = list = new List<(float T, bool KeepAfter, int End)>();
                // 同一段上两处保留部分重叠（两个臂端朝外的方向相对）说明判断错了，只接先找到的
                if (list.Any(a => keepAfter ? a.KeepAfter || a.T > t : !a.KeepAfter || a.T < t))
                    continue;
                list.Add((t, keepAfter, i));
                used[i] = true;
            }
            var whole = KeepBeyond(entityManager, edges, attached);
            edges.RemoveAll(whole.Contains);
            if (Debug && whole.Count > 0)
                Mod.log.Info($"[debug] kept {whole.Count} edges beyond the leg ends");

            foreach (Entity entity in edges)
            {
                Edge edge = entityManager.GetComponentData<Edge>(entity);
                Bezier4x3 curve = entityManager.GetComponentData<Curve>(entity).m_Bezier;
                Entity prefab = entityManager.GetComponentData<PrefabRef>(entity).m_Prefab;
                float width = entityManager.TryGetComponent(prefab, out NetGeometryData geometry) ? geometry.m_DefaultWidth : 0f;

                CreateDelete(commandBuffer, entity, edge, curve);

                // 保留部分沿用原来的方向（单行道路的行驶方向不变）：臂端压着的段保留臂端外侧，范围外另有和它不重叠的部分照常保留
                float edgeLength = MathUtils.Length(curve);
                var parts = new List<(float Start, float End, int StartTarget, int EndTarget)>();
                attached.TryGetValue(entity, out var mine);
                if (mine != null)
                    foreach (var a in mine)
                        parts.Add(a.KeepAfter ? (a.T, 1f, a.End, -1) : (0f, a.T, -1, a.End));
                Bezier3 local = ToLocal(curve, origin, yaw);
                TerrainHeightData heights = terrain;
                var outside = converted.Contains(entity) ? hull.OutsideIntervals(local)
                    : Footprint.OutsideIntervals(t => corridor.Collides(local.Evaluate(t), ElevationAt(MathUtils.Position(curve, t), ref heights), width / 2f));
                foreach (var (outsideStart, outsideEnd) in outside)
                {
                    if (mine != null && parts.Any(q => q.Start < outsideEnd && outsideStart < q.End))
                        continue;
                    float start = outsideStart, end = outsideEnd;
                    // 切断端：附近有同一高度的臂端就接上；接不上（比如在高架的臂端下面）就再退 kUnconnectedGap，避开臂端节点和立柱
                    int startTarget = start > 0f ? FindEnd(ends, used, curve, edgeLength, ref start, keepAfter: true, ref terrain) : -1;
                    if (startTarget >= 0)
                        used[startTarget] = true;
                    int endTarget = end < 1f ? FindEnd(ends, used, curve, edgeLength, ref end, keepAfter: false, ref terrain) : -1;
                    if (endTarget >= 0)
                        used[endTarget] = true;
                    // 高架的切断端还要多退一些：末端节点下有立柱，不能落在立交新建的道路上
                    if (start > 0f && startTarget < 0)
                        start += (kUnconnectedGap + width / 2f + ElevatedExtra(MathUtils.Position(curve, start), ref terrain)) / edgeLength;
                    if (end < 1f && endTarget < 0)
                        end -= (kUnconnectedGap + width / 2f + ElevatedExtra(MathUtils.Position(curve, end), ref terrain)) / edgeLength;
                    parts.Add((start, end, startTarget, endTarget));
                }

                foreach (var (start, end, startTarget, endTarget) in parts)
                {
                    Bezier4x3 part = MathUtils.Cut(curve, new float2(start, end));
                    float length = end > start ? MathUtils.Length(part) : 0f;
                    // 保留部分太短（比路宽还短）时游戏会报“形状无效”。如果它另一端是普通的路中节点（只接着同一种道路的下一段），
                    // 就把下一段也拆掉，从臂端直接重建到下一段的另一端，两段合成一段（臂端就在节点上、保留部分为零时也这样接上）
                    bool merged = false;
                    if (length < width && (startTarget >= 0) != (endTarget >= 0) && (startTarget >= 0 ? end >= 1f : start <= 0f))
                    {
                        Entity node = startTarget >= 0 ? edge.m_End : edge.m_Start;
                        var legEnd = startTarget >= 0 ? ends[startTarget] : ends[endTarget];
                        merged = TryMergeThroughNode(commandBuffer, entityManager, node, entity, prefab, edges, legEnd, ref terrain, randomSeed++);
                    }
                    if (!merged && length < kMinRemainderLength)
                    {
                        if (startTarget >= 0) used[startTarget] = false;
                        if (endTarget >= 0) used[endTarget] = false;
                        continue;
                    }
                    connected += (startTarget >= 0 ? 1 : 0) + (endTarget >= 0 ? 1 : 0);
                    if (Debug)
                        Mod.log.Info($"[debug] remainder of edge {entity.Index}: {length:F1} m (road {width:F0} m), " +
                            $"start {(startTarget >= 0 ? "leg end" : start <= 0f ? "original node" : "cut")}, " +
                            $"end {(endTarget >= 0 ? "leg end" : end >= 1f ? "original node" : "cut")}{(merged ? ", merged with the next edge" : "")}");
                    if (merged)
                        continue;
                    float startElevation = InterchangePlacer.TerrainElevation(part.a, ref terrain), endElevation = InterchangePlacer.TerrainElevation(part.d, ref terrain);
                    if (startTarget >= 0)
                        Snap(ends[startTarget], ref part.a, ref part.b, ref startElevation, ref terrain);
                    if (endTarget >= 0)
                        Snap(ends[endTarget], ref part.d, ref part.c, ref endElevation, ref terrain);
                    NetCourseBuilder.CreateSegment(commandBuffer, prefab, part, startElevation, endElevation, randomSeed++,
                        start <= 0f ? edge.m_Start : Entity.Null,
                        end >= 1f ? edge.m_End : Entity.Null);
                }
            }
            return connected;
        }

        /// <summary>
        /// 臂端压着的那段要拆的原有道路（离中心线不超过 kConnectLateral、同一高度）：投影位置 t，保留离立交中心远的一侧
        /// （keepAfter：t 之后）。那一侧短到不成一段（臂端就在节点上）的段排在后面，节点另一边那段也在 edges 里时用那段。
        /// 没有时返回 Entity.Null。
        /// </summary>
        private static Entity Attach(EntityManager entityManager, List<Entity> edges, (float3 World, float Elevation) end, float3 origin,
            ref TerrainHeightData terrain, out float t, out bool keepAfter)
        {
            Entity best = Entity.Null;
            float bestScore = float.MaxValue;
            t = 0f;
            keepAfter = false;
            // 调试：离臂端最近的原有道路（不论能不能接）
            float nearest = float.MaxValue, nearestRise = 0f;
            foreach (Entity entity in edges)
            {
                Bezier4x3 curve = entityManager.GetComponentData<Curve>(entity).m_Bezier;
                float projected = NearestT(curve, end.World);
                float3 onRoad = MathUtils.Position(curve, projected);
                float lateral = math.distance(onRoad.xz, end.World.xz);
                float rise = end.Elevation - ElevationAt(onRoad, ref terrain);
                if (lateral < nearest)
                {
                    nearest = lateral;
                    nearestRise = rise;
                }
                if (lateral > kConnectLateral || math.abs(rise) > kConnectElevation)
                    continue;
                float length = MathUtils.Length(curve);
                float step = math.min(1f, kOutwardProbe / math.max(1f, length));
                bool after = math.distancesq(MathUtils.Position(curve, math.min(1f, projected + step)).xz, origin.xz)
                    > math.distancesq(MathUtils.Position(curve, math.max(0f, projected - step)).xz, origin.xz);
                float score = lateral + ((after ? 1f - projected : projected) * length < kMinRemainderLength ? kConnectLateral : 0f);
                if (score >= bestScore)
                    continue;
                best = entity;
                bestScore = score;
                t = projected;
                keepAfter = after;
            }
            if (Debug)
                Mod.log.Info($"[debug] leg end ({end.World.x:F1}, {end.World.z:F1}) elevation {end.Elevation:F1}: nearest old road {nearest:F1} m sideways, " +
                    $"leg end {nearestRise:F1} m above it{(best == Entity.Null ? ", not connected" : "")}");
            return best;
        }

        /// <summary>
        /// 臂端之外沿原路往外：经过普通路中节点的下一段如果也在要拆的路段里（弯道上范围横跨弯道内侧），整段保留，直到路口、
        /// 路的尽头或范围外。返回这些路段。
        /// </summary>
        private static HashSet<Entity> KeepBeyond(EntityManager entityManager, List<Entity> edges,
            Dictionary<Entity, List<(float T, bool KeepAfter, int End)>> attached)
        {
            var whole = new HashSet<Entity>();
            foreach (var pair in attached)
                foreach (var a in pair.Value)
                {
                    Entity current = pair.Key;
                    Edge edge = entityManager.GetComponentData<Edge>(current);
                    Entity node = a.KeepAfter ? edge.m_End : edge.m_Start;
                    for (int step = 0; step < kMaxKeptEdges; step++)
                    {
                        Entity next = NextAlongRoad(entityManager, node, current);
                        if (next == Entity.Null || !edges.Contains(next) || attached.ContainsKey(next) || !whole.Add(next))
                            break;
                        Edge nextEdge = entityManager.GetComponentData<Edge>(next);
                        node = nextEdge.m_Start == node ? nextEdge.m_End : nextEdge.m_Start;
                        current = next;
                    }
                }
            return whole;
        }

        /// <summary>普通路中节点 node 上除 self 外唯一的另一段道路；node 是路口（接着不止一段）或路的尽头时返回 Entity.Null。</summary>
        private static Entity NextAlongRoad(EntityManager entityManager, Entity node, Entity self)
        {
            if (node == Entity.Null || !entityManager.HasBuffer<ConnectedEdge>(node))
                return Entity.Null;
            Entity next = Entity.Null;
            foreach (ConnectedEdge connected in entityManager.GetBuffer<ConnectedEdge>(node, true))
            {
                if (connected.m_Edge == self || !IsRemovableRoad(entityManager, connected.m_Edge))
                    continue;
                if (next != Entity.Null)
                    return Entity.Null;
                next = connected.m_Edge;
            }
            return next;
        }

        /// <summary>
        /// 切断点 t 附近、同一高度、还没接过的臂端：臂端在原有道路上的投影离切断点不超过 kConnectDistance（落在切掉的那部分里时
        /// 放宽到 kConnectInside），离道路中心线不超过 kConnectLateral。keepAfter：保留部分在 t 之后（切断点是保留部分的起点）。
        /// 找到时把 t 改成投影位置（在那里重新切，保留部分不会折回），返回臂端序号；没有时返回 -1。
        /// </summary>
        private static int FindEnd(List<(float3 World, float Elevation)> ends, bool[] used, Bezier4x3 curve, float edgeLength,
            ref float t, bool keepAfter, ref TerrainHeightData terrain)
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
                // 沿路离切断点的距离：往切掉的那部分里为正
                float inward = (keepAfter ? t - projected : projected - t) * edgeLength;
                if (lateral > bestLateral || inward > kConnectInside || inward < -kConnectDistance
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
        private static void Snap((float3 World, float Elevation) end, ref float3 point, ref float3 handle, ref float elevation, ref TerrainHeightData terrain)
        {
            handle += end.World - point;
            point = end.World;
            elevation = InterchangePlacer.TerrainElevation(end.World, ref terrain);
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
            // 节点是路口（接着不止一段）时不能合并
            Entity next = NextAlongRoad(entityManager, node, self);
            if (next == Entity.Null || removing.Contains(next) || entityManager.GetComponentData<PrefabRef>(next).m_Prefab != prefab)
                return false;

            Edge nextEdge = entityManager.GetComponentData<Edge>(next);
            Bezier4x3 curve = entityManager.GetComponentData<Curve>(next).m_Bezier;
            CreateDelete(commandBuffer, next, nextEdge, curve);
            bool nodeAtStart = nextEdge.m_Start == node;
            float startElevation = InterchangePlacer.TerrainElevation(curve.a, ref terrain), endElevation = InterchangePlacer.TerrainElevation(curve.d, ref terrain);
            if (nodeAtStart)
                Snap(legEnd, ref curve.a, ref curve.b, ref startElevation, ref terrain);
            else
                Snap(legEnd, ref curve.d, ref curve.c, ref endElevation, ref terrain);
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

        /// <summary>其他原有道路有没有一点被新路压到（见 Footprint.Collides）；halfWidth 是它自己的半宽。</summary>
        private static bool CollidesWithNewRoads(Bezier3 local, Bezier4x3 world, Footprint corridor, float halfWidth, ref TerrainHeightData terrain)
        {
            const int samples = 32;
            for (int i = 0; i <= samples; i++)
            {
                float t = i / (float)samples;
                if (corridor.Collides(local.Evaluate(t), ElevationAt(MathUtils.Position(world, t), ref terrain), halfWidth))
                    return true;
            }
            return false;
        }

        /// <summary>道路在范围内的部分里，有没有一点离某一层高度不远。</summary>
        private static bool OverlapsAtLevels(Bezier3 local, Bezier4x3 world, Footprint footprint, IReadOnlyList<float> levels,
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
            InterchangePlacer.TerrainElevation(position, ref terrain) >= InterchangeMod.Generation.HeightRange.BridgeThreshold ? kElevatedCutGap : 0f;

        /// <summary>原有道路上一点在立交局部坐标里的高度（相对地面参考平面），和新路的层高、臂端高度比较用。</summary>
        private static float ElevationAt(float3 position, ref TerrainHeightData terrain) =>
            InterchangePlacer.LocalHeight(position, ref terrain);

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
