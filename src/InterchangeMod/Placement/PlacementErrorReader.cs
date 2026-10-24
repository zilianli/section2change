using System.Collections.Generic;
using Game.Common;
using Game.Net;
using Game.Notifications;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;

namespace InterchangeMod.Placement
{
    /// <summary>游戏对预览报出的一个错误：类型（ErrorType 名）、出错的道路、该处离地高度。</summary>
    public struct GameError
    {
        public string Type;
        public string Road;
        public float Elevation;
        /// <summary>true = 错误（阻止放置）；false = 警告（不阻止，如可被覆盖的物体）。</summary>
        public bool Blocking;

        public override string ToString() => $"{(Blocking ? "error" : "warning")} {Type}: {Road} (elevation {Elevation:F1} m)";
    }

    /// <summary>
    /// 读出游戏对当前预览报的错误。ValidationSystem 为每个错误生成一个提示图标实体：
    /// Owner = 出错的临时道路，图标预制体上的 ToolErrorData.m_Error = 错误类型。
    /// 同一类型、同一道路只报一次。
    /// </summary>
    public static class PlacementErrorReader
    {
        /// <param name="iconQuery">提示图标查询：Icon + Owner + PrefabRef。</param>
        public static List<GameError> Read(EntityManager entityManager, PrefabSystem prefabSystem, EntityQuery iconQuery)
        {
            var errors = new List<GameError>();
            var seen = new HashSet<string>();
            using var icons = iconQuery.ToEntityArray(Allocator.Temp);
            foreach (Entity icon in icons)
            {
                Entity owner = entityManager.GetComponentData<Owner>(icon).m_Owner;
                Entity iconPrefab = entityManager.GetComponentData<PrefabRef>(icon).m_Prefab;
                if (!entityManager.Exists(owner) || !entityManager.HasComponent<Temp>(owner) || !entityManager.HasComponent<ToolErrorData>(iconPrefab))
                    continue;

                string type = entityManager.GetComponentData<ToolErrorData>(iconPrefab).m_Error.ToString();
                string road = "?";
                if (entityManager.HasComponent<PrefabRef>(owner)
                    && prefabSystem.TryGetPrefab(entityManager.GetComponentData<PrefabRef>(owner).m_Prefab, out PrefabBase roadPrefab))
                    road = roadPrefab.name;
                float elevation = 0f;
                if (entityManager.HasComponent<Elevation>(owner))
                {
                    var e = entityManager.GetComponentData<Elevation>(owner).m_Elevation;
                    elevation = (e.x + e.y) / 2f;
                }

                // 图标优先级：Error(200) 阻止放置；Warning(100) 只是提醒（游戏会把可覆盖物体的重叠降为警告）
                bool blocking = entityManager.GetComponentData<Icon>(icon).m_Priority >= IconPriority.Error;
                if (seen.Add($"{type}|{road}|{blocking}"))
                    errors.Add(new GameError { Type = type, Road = road, Elevation = elevation, Blocking = blocking });
            }
            return errors;
        }
    }
}
