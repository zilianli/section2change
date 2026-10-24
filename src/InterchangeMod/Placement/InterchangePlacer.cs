using System.Collections.Generic;
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
    /// 每个控制点按地形高度换算成世界高度，然后为每条路段写一个定义实体。
    /// </summary>
    public static class InterchangePlacer
    {
        /// <summary>
        /// 先检查全部路段（预制体、高度范围），全部通过才创建定义实体，避免只造出一半。
        /// 返回错误列表；为空表示已创建。
        /// </summary>
        public static List<string> CreateDefinitions(EntityCommandBuffer commandBuffer, IReadOnlyList<RoadSegmentSpec> segments,
            float3 origin, float yaw, ref TerrainHeightData terrain, PrefabResolver prefabs, NetPrefab fallbackRoad, int randomSeed)
        {
            var errors = new List<string>();
            var prefabEntities = new Entity[segments.Count];
            for (int i = 0; i < segments.Count; i++)
            {
                var segment = segments[i];
                if (!prefabs.TryResolve(segment.Prefab, fallbackRoad, out NetPrefab prefab))
                {
                    errors.Add($"{segment.Name}: road prefab '{segment.Prefab}' not found");
                    continue;
                }

                var curve = segment.Curve;
                float min = math.min(math.min(curve.P0.Y, curve.P1.Y), math.min(curve.P2.Y, curve.P3.Y));
                float max = math.max(math.max(curve.P0.Y, curve.P1.Y), math.max(curve.P2.Y, curve.P3.Y));
                if (!prefabs.TryGetEntityForElevation(prefab, min, max, out prefabEntities[i], out string error))
                    errors.Add($"{segment.Name}: {error}");
            }
            if (errors.Count > 0)
                return errors;

            for (int i = 0; i < segments.Count; i++)
            {
                var curve = segments[i].Curve;
                var world = new Bezier4x3(
                    ToWorld(curve.P0, origin, yaw, ref terrain),
                    ToWorld(curve.P1, origin, yaw, ref terrain),
                    ToWorld(curve.P2, origin, yaw, ref terrain),
                    ToWorld(curve.P3, origin, yaw, ref terrain));
                // 节点坐标与曲线端点可以不同（边缘对齐）：节点决定连到哪个节点，曲线决定路面位置
                NetCourseBuilder.CreateSegment(commandBuffer, prefabEntities[i], world, curve.P0.Y, curve.P3.Y, randomSeed + i,
                    startNodePosition: ToWorld(segments[i].StartNode, origin, yaw, ref terrain),
                    endNodePosition: ToWorld(segments[i].EndNode, origin, yaw, ref terrain));
            }
            return errors;
        }

        /// <summary>
        /// 局部点 → 世界点。同一个局部点总是得到完全相同的世界坐标（游戏靠坐标相等合并节点）。
        /// </summary>
        public static float3 ToWorld(Vec3 local, float3 origin, float yaw, ref TerrainHeightData terrain)
        {
            Vec3 rotated = local.RotateY(yaw);
            var world = new float3(origin.x + rotated.X, 0f, origin.z + rotated.Z);
            world.y = TerrainUtils.SampleHeight(ref terrain, world) + local.Y;
            return world;
        }
    }
}
