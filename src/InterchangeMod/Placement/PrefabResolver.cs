using System.Collections.Generic;
using Game.Prefabs;
using Unity.Entities;

namespace InterchangeMod.Placement
{
    /// <summary>
    /// 把生成层给出的道路名换成游戏预制体，并检查/切换高度：
    /// 每种道路有允许的离地高度范围，低于下限且该道路有地下版本时换成地下版本。
    /// </summary>
    public sealed class PrefabResolver
    {
        private readonly PrefabSystem m_PrefabSystem;
        private readonly Dictionary<string, NetPrefab> m_Cache = new Dictionary<string, NetPrefab>();
        private readonly HashSet<string> m_ReportedMissing = new HashSet<string>();

        public PrefabResolver(PrefabSystem prefabSystem)
        {
            m_PrefabSystem = prefabSystem;
        }

        /// <summary>name 为 null 时使用 fallback（玩家在道路菜单里选的道路）。</summary>
        public bool TryResolve(string name, NetPrefab fallback, out NetPrefab prefab)
        {
            if (name == null)
            {
                prefab = fallback;
                return prefab != null;
            }

            if (m_Cache.TryGetValue(name, out prefab))
                return true;

            if (m_PrefabSystem.TryGetPrefab(new PrefabID(nameof(RoadPrefab), name), out PrefabBase found) && found is NetPrefab net)
            {
                m_Cache[name] = net;
                prefab = net;
                return true;
            }

            if (m_ReportedMissing.Add(name))
                Mod.log.Warn($"Road prefab '{name}' not found");
            return false;
        }

        /// <summary>道路宽度（米），读不到时返回 fallback。</summary>
        public float GetWidth(NetPrefab prefab, float fallback = 12f) =>
            m_PrefabSystem.TryGetComponentData(prefab, out NetGeometryData geometry) && geometry.m_DefaultWidth > 0f
                ? geometry.m_DefaultWidth
                : fallback;

        /// <summary>
        /// 是否为单行道路。游戏初始化道路时，车道只朝一个方向的道路会被标上
        /// DefaultIsForward 或 DefaultIsBackward（NetInitializeSystem）。
        /// 注意不能用 GeometryFlags.Directional：所有道路都带这个标志。
        /// </summary>
        public bool IsOneWay(NetPrefab prefab) =>
            m_PrefabSystem.TryGetComponentData(prefab, out RoadData road)
            && (road.m_Flags & (RoadFlags.DefaultIsForward | RoadFlags.DefaultIsBackward)) != 0;

        /// <summary>
        /// 按世界曲线的实际离地高度选择预制体，遵循 CourseSplitSystem.LimitElevation：
        /// 非负下限和负上限约束地上/地下道路；工具栏的正上限不限制桥墩到河床的距离。
        /// 保持世界坐标不变，不钳制桥面高度。
        /// </summary>
        public bool TryGetEntityForElevation(NetPrefab prefab, float minElevation, float maxElevation, out Entity entity, out string error)
        {
            entity = m_PrefabSystem.GetEntity(prefab);
            error = null;
            if (!m_PrefabSystem.TryGetComponentData(prefab, out PlaceableNetData placeable))
                return true;

            if (minElevation < placeable.m_ElevationRange.min && placeable.m_UndergroundPrefab != Entity.Null)
            {
                entity = placeable.m_UndergroundPrefab;
                if (m_PrefabSystem.TryGetPrefab(entity, out NetPrefab underground)
                    && m_PrefabSystem.TryGetComponentData(underground, out PlaceableNetData undergroundPlaceable))
                    placeable = undergroundPlaceable;
            }

            if (!InterchangeMod.Generation.Geometry.PhysicalElevation.Allowed(minElevation, maxElevation,
                placeable.m_ElevationRange.min, placeable.m_ElevationRange.max))
            {
                error = $"{prefab.name} 离地 {minElevation:F0}–{maxElevation:F0} 米，超出这种道路允许的 {placeable.m_ElevationRange.min:F0}–{placeable.m_ElevationRange.max:F0} 米";
                return false;
            }
            return true;
        }

    }
}
