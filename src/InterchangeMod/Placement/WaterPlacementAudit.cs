using System.Collections.Generic;
using Colossal.Mathematics;
using Game.Prefabs;
using Game.Simulation;
using InterchangeMod.Generation;
using InterchangeMod.Generation.Geometry;
using Unity.Mathematics;

namespace InterchangeMod.Placement
{
    /// <summary>首轮水域/岸线测试日志。只诊断，不改参考平面或绕过游戏检查。</summary>
    public static class WaterPlacementAudit
    {
        public static void Log(IReadOnlyList<RoadSegmentSpec> segments, float3 origin, float yaw,
            ref TerrainHeightData terrain, ref WaterSurfaceData<SurfaceWater> water,
            PrefabResolver resolver, PrefabSystem prefabs, NetPrefab fallback)
        {
            var total = new SurfaceAudit();
            string firstInvalid = null;
            foreach (var segment in segments)
            {
                var c = segment.Curve;
                var world = new Bezier4x3(
                    InterchangePlacer.ToWorld(c.P0, origin, yaw, ref terrain),
                    InterchangePlacer.ToWorld(c.P1, origin, yaw, ref terrain),
                    InterchangePlacer.ToWorld(c.P2, origin, yaw, ref terrain),
                    InterchangePlacer.ToWorld(c.P3, origin, yaw, ref terrain));
                if (!resolver.TryResolve(segment.Prefab, fallback, out NetPrefab prefab)) continue;
                float minimum = prefabs.TryGetComponentData(prefab, out PlaceableNetData placeable) ? placeable.m_MinWaterElevation : 0f;
                // Control-polygon length bounds the curve length; at least one sample / 4 m.
                float length = math.distance(world.a, world.b) + math.distance(world.b, world.c) + math.distance(world.c, world.d);
                int count = math.max(1, (int)math.ceil(3f * length / 4f));
                for (int i = 0; i <= count; i++)
                {
                    float3 p = MathUtils.Position(world, i / (float)count);
                    WaterUtils.SampleHeight(ref water, ref terrain, p, out float ground, out float surface, out float depth);
                    int before = total.UnderWaterSamples;
                    total.Add(p.y, ground, depth, minimum);
                    if (firstInvalid == null && total.UnderWaterSamples > before)
                        firstInvalid = $"{segment.Name} at ({p.x:F1},{p.z:F1}): roadY={p.y:F2}, terrainY={ground:F2}, waterY={surface:F2}, requiredAboveWater={minimum:F2}";
                }
            }
            Mod.log.Info($"[water-audit] {(total.Mixed ? "mixed" : total.WetSamples > 0 ? "water" : "dry")}: wet={total.WetSamples}, dry={total.DrySamples}, " +
                $"terrainElevation={total.MinTerrainElevation:F2}..{total.MaxTerrainElevation:F2}, minimumWaterClearance={total.MinWaterClearance:F2}, " +
                $"belowPrefabWaterMinimum={total.UnderWaterSamples}; first={firstInvalid ?? "none"}. Sampled diagnostic only; game validation still required.");
        }
    }
}
