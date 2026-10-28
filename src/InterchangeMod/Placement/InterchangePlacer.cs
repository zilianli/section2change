using System.Collections.Generic;
using System.Linq;
using Colossal.Mathematics;
using Game.Prefabs;
using Game.Simulation;
using InterchangeMod.Generation;
using InterchangeMod.Generation.Geometry;
using Unity.Entities;
using Unity.Mathematics;

namespace InterchangeMod.Placement
{
    /// <summary>
    /// 把生成层的局部结果（Y = 离地高度）摆到世界里：绕 Y 轴旋转、平移到 origin，
    /// 每个控制点按“地面参考平面”换算成世界高度，然后为每条路段写一个定义实体。
    /// </summary>
    public static class InterchangePlacer
    {
        /// <summary>
        /// 地面参考平面：过立交中心处的地面、坡度按立交范围内的原有道路或地形拟合。局部高度都相对它：同一个线性函数加到所有控制点上，
        /// 贝塞尔曲线上每一点也正好加上同样的值，两条路交叉处的高差就是层高差。原来每个控制点按自己脚下的地形抬高，
        /// 曲线中间的高度是各控制点地形的插值，交叉处两条路各自插值不一样，起伏地形上净空不足、叠放、坡度过陡（游戏内坡地上的定向式）。
        /// </summary>
        private struct GroundPlane
        {
            public bool Set;
            public float2 Origin;
            public float Height;
            public float2 Slope;

            public float HeightAt(float3 world) => Height + math.dot(Slope, world.xz - Origin);
        }

        private static GroundPlane s_Ground;

        /// <summary>
        /// 原路口改造时被改造的原有道路上的采样点（世界坐标）和所属的道路（按方向分组）：参考平面的坡度按它们拟合，
        /// 新路的臂端才能和原有道路同高（原有道路不一定顺着地形，如高架有自己的纵坡：按地形拟合时臂端比原有道路高出 10 米、接不上）。
        /// 没有（空地放置）时按地形拟合。
        /// </summary>
        public static IReadOnlyList<(float3 Point, int Road)> RoadSamples { get; set; }

        /// <summary>地面参考平面的坡度上限：再陡就不是“整体有坡”，而是地形太乱，平面贴不住。</summary>
        private const float kMaxGroundSlope = 0.15f;

        /// <summary>
        /// 先检查全部路段（预制体、高度范围），全部通过才创建定义实体，避免只造出一半。
        /// 返回错误列表；为空表示已创建。
        /// </summary>
        public static List<string> CreateDefinitions(EntityCommandBuffer commandBuffer, IReadOnlyList<RoadSegmentSpec> segments,
            float3 origin, float yaw, ref TerrainHeightData terrain, PrefabResolver prefabs, NetPrefab fallbackRoad, int randomSeed)
        {
            FitGround(segments, origin, yaw, ref terrain);
            var errors = new List<string>();
            var elevationErrors = new List<string>();
            var prefabEntities = new Entity[segments.Count];
            var worlds = new Bezier4x3[segments.Count];
            for (int i = 0; i < segments.Count; i++)
            {
                var segment = segments[i];
                var curve = segment.Curve;
                worlds[i] = new Bezier4x3(
                    ToWorld(curve.P0, origin, yaw, ref terrain),
                    ToWorld(curve.P1, origin, yaw, ref terrain),
                    ToWorld(curve.P2, origin, yaw, ref terrain),
                    ToWorld(curve.P3, origin, yaw, ref terrain));
                if (!prefabs.TryResolve(segment.Prefab, fallbackRoad, out NetPrefab prefab))
                {
                    errors.Add($"{segment.Name}: road prefab '{segment.Prefab}' not found");
                    continue;
                }

                // Actual terrain elevation selects geometry; signed course restrictions
                // are distinct from the toolbar's water/ground-relative offsets.
                var w = worlds[i];
                float e0 = TerrainElevation(w.a, ref terrain), e1 = TerrainElevation(w.b, ref terrain);
                float e2 = TerrainElevation(w.c, ref terrain), e3 = TerrainElevation(w.d, ref terrain);
                float min = math.min(math.min(e0, e1), math.min(e2, e3)), max = math.max(math.max(e0, e1), math.max(e2, e3));
                if (!prefabs.TryGetEntityForElevation(prefab, min, max, out prefabEntities[i], out string error))
                    elevationErrors.Add($"{segment.Name}：{error}");
            }
            // Physical course restrictions differ from the toolbar's adjustable offsets.
            if (elevationErrors.Count > 0)
                errors.Add($"有 {elevationErrors.Count} 段路不符合道路的地上/地下高度限制（如 {elevationErrors[0]}），请调整层高或道路类型");
            if (errors.Count > 0)
                return errors;

            for (int i = 0; i < segments.Count; i++)
            {
                var w = worlds[i];
                // 节点坐标与曲线端点可以不同（边缘对齐）：节点决定连到哪个节点，曲线决定路面位置
                NetCourseBuilder.CreateSegment(commandBuffer, prefabEntities[i], w,
                    TerrainElevation(w.a, ref terrain), TerrainElevation(w.d, ref terrain), randomSeed + i,
                    startNodePosition: ToWorld(segments[i].StartNode, origin, yaw, ref terrain),
                    endNodePosition: ToWorld(segments[i].EndNode, origin, yaw, ref terrain));
            }
            return errors;
        }

        /// <summary>
        /// 拟合地面参考平面：过 origin 处的地面，坡度按被改造的原有道路（RoadSamples），没有时按各路段控制点脚下地形的最小二乘
        /// （斜率限制在 kMaxGroundSlope 内）。平地上就是水平面，和原来按地形抬高没有区别。
        /// </summary>
        private static void FitGround(IReadOnlyList<RoadSegmentSpec> segments, float3 origin, float yaw, ref TerrainHeightData terrain)
        {
            float height = TerrainUtils.SampleHeight(ref terrain, origin);
            float2 terrainSlope = TerrainSlope(segments, origin, yaw, height, ref terrain);
            float2 slope = RoadSamples is { Count: > 0 } samples ? RoadSlope(samples, terrainSlope) : terrainSlope;
            float steep = math.length(slope);
            if (steep > kMaxGroundSlope)
                slope *= kMaxGroundSlope / steep;
            s_Ground = new GroundPlane { Set = true, Origin = origin.xz, Height = height, Slope = slope };
        }

        /// <summary>
        /// 按原有道路拟合坡度：每条路的采样点减去自己的平均（各条路高度不同，只看坡度），合起来做最小二乘；
        /// 只有一个方向的路时另一个方向定不出来，用很小的权重拉向地形坡度。
        /// </summary>
        private static float2 RoadSlope(IReadOnlyList<(float3 Point, int Road)> samples, float2 fallback)
        {
            float sxx = 0f, sxz = 0f, szz = 0f, sxh = 0f, szh = 0f;
            foreach (var group in samples.GroupBy(s => s.Road))
            {
                var points = group.Select(s => s.Point).ToList();
                float3 mean = points.Aggregate(float3.zero, (acc, p) => acc + p) / points.Count;
                foreach (float3 p in points)
                {
                    float dx = p.x - mean.x, dz = p.z - mean.z, dh = p.y - mean.y;
                    sxx += dx * dx; sxz += dx * dz; szz += dz * dz; sxh += dx * dh; szh += dz * dh;
                }
            }
            float ridge = 1e-3f * (sxx + szz) + 1f;
            sxx += ridge; szz += ridge; sxh += ridge * fallback.x; szh += ridge * fallback.y;
            float det = sxx * szz - sxz * sxz;
            return det > 1e-3f ? new float2((sxh * szz - szh * sxz) / det, (szh * sxx - sxh * sxz) / det) : fallback;
        }

        /// <summary>按各路段控制点脚下地形拟合的坡度（过 origin 处的地面）。</summary>
        private static float2 TerrainSlope(IReadOnlyList<RoadSegmentSpec> segments, float3 origin, float yaw, float height, ref TerrainHeightData terrain)
        {
            float sxx = 0f, sxz = 0f, szz = 0f, sxh = 0f, szh = 0f;
            foreach (var segment in segments)
                foreach (Vec3 local in new[] { segment.Curve.P0, segment.Curve.Evaluate(0.5f), segment.Curve.P3 })
                {
                    Vec3 rotated = local.RotateY(yaw);
                    var p = new float3(origin.x + rotated.X, 0f, origin.z + rotated.Z);
                    float dx = rotated.X, dz = rotated.Z, dh = TerrainUtils.SampleHeight(ref terrain, p) - height;
                    sxx += dx * dx; sxz += dx * dz; szz += dz * dz; sxh += dx * dh; szh += dz * dh;
                }
            float det = sxx * szz - sxz * sxz;
            return det > 1e-3f ? new float2((sxh * szz - szh * sxz) / det, (szh * sxx - sxh * sxz) / det) : float2.zero;
        }

        /// <summary>
        /// 局部点 → 世界点：高度 = 地面参考平面 + 局部高度（还没拟合平面时按脚下地形）。同一个局部点总是得到完全相同的世界坐标
        /// （游戏靠坐标相等合并节点）。
        /// </summary>
        public static float3 ToWorld(Vec3 local, float3 origin, float yaw, ref TerrainHeightData terrain)
        {
            Vec3 rotated = local.RotateY(yaw);
            var world = new float3(origin.x + rotated.X, 0f, origin.z + rotated.Z);
            world.y = (s_Ground.Set ? s_Ground.HeightAt(world) : TerrainUtils.SampleHeight(ref terrain, world)) + local.Y;
            return world;
        }

        /// <summary>世界点在立交局部坐标里的高度（相对地面参考平面），与生成层的层高直接比较。</summary>
        public static float LocalHeight(float3 world, ref TerrainHeightData terrain) =>
            world.y - (s_Ground.Set ? s_Ground.HeightAt(world) : TerrainUtils.SampleHeight(ref terrain, world));

        /// <summary>世界点离脚下地形的高度（游戏的 Elevation：选高架 / 隧道版本、写进 NetCourse）。</summary>
        public static float TerrainElevation(float3 world, ref TerrainHeightData terrain) =>
            world.y - TerrainUtils.SampleHeight(ref terrain, world);
    }
}
