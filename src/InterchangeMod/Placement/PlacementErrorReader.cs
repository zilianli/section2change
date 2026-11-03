using System.Collections.Generic;
using Game.Common;
using Game.Net;
using Game.Notifications;
using Game.Prefabs;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

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
        /// <summary>同类错误各个图标的位置（世界坐标）。</summary>
        public List<float3> Positions;
        /// <summary>Debug-only source entities, parallel to Positions. Not sent to UI.</summary>
        public List<string> Sources;

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
        public static List<GameError> Read(EntityManager entityManager, PrefabSystem prefabSystem, EntityQuery iconQuery, bool debug = false)
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
                var iconData = entityManager.GetComponentData<Icon>(icon);
                bool blocking = iconData.m_Priority >= IconPriority.Error;
                if (seen.Add($"{type}|{road}|{blocking}"))
                    errors.Add(new GameError { Type = type, Road = road, Elevation = elevation, Blocking = blocking, Positions = new List<float3>(), Sources = new List<string>() });
                var group = errors.Find(e => e.Type == type && e.Road == road && e.Blocking == blocking);
                group.Positions.Add(iconData.m_Location);
                if (debug)
                {
                    string source = $"owner={owner.Index}:{owner.Version}";
                    try
                    {
                        var temp = entityManager.GetComponentData<Temp>(owner);
                        source += $", original={temp.m_Original.Index}:{temp.m_Original.Version}, flags={temp.m_Flags}";
                        if (entityManager.HasComponent<Curve>(owner))
                        {
                            var c = entityManager.GetComponentData<Curve>(owner).m_Bezier;
                            source += $", worldCurve=[{c.a};{c.b};{c.c};{c.d}]";
                        }
                        Entity current=owner;
                        for(int depth=0;depth<3 && entityManager.Exists(current);depth++)
                        {
                            if(entityManager.HasComponent<Node>(current))
                                source+=$", parent{depth}Node={current.Index}:{current.Version} at {entityManager.GetComponentData<Node>(current).m_Position}";
                            if(entityManager.HasComponent<Game.Objects.Transform>(current))
                                source+=$", object{depth}Position={entityManager.GetComponentData<Game.Objects.Transform>(current).m_Position}";
                            if(!entityManager.HasComponent<Owner>(current))break;
                            current=entityManager.GetComponentData<Owner>(current).m_Owner;
                            source+=$", parent{depth+1}={current.Index}:{current.Version}";
                        }
                    }
                    catch (System.Exception ex)
                    {
                        source += $", ownerDiagnosticError={ex.GetType().Name}: {ex.Message}";
                    }
                    // ValidationSystem preserves the collision counterpart on the icon,
                    // independently of the Temp object which owns the error.
                    try
                    {
                        if (!entityManager.HasComponent<Target>(icon))
                            source += ", target=(none)";
                        else
                        {
                            Entity target = entityManager.GetComponentData<Target>(icon).m_Target;
                            source += $", target={target.Index}:{target.Version}";
                            if (target == Entity.Null)
                                source += " (null)";
                            else if (!entityManager.Exists(target))
                                source += " (missing)";
                            else
                            {
                                string targetPrefab = "(none)";
                                if (entityManager.HasComponent<PrefabRef>(target))
                                {
                                    Entity prefab = entityManager.GetComponentData<PrefabRef>(target).m_Prefab;
                                    targetPrefab = prefabSystem.TryGetPrefab(prefab, out PrefabBase resolved)
                                        ? resolved.name : $"{prefab.Index}:{prefab.Version} (unresolved)";
                                }
                                source += $", targetPrefab={targetPrefab}, targetHidden={entityManager.HasComponent<Hidden>(target)}";
                                if (entityManager.HasComponent<Temp>(target))
                                {
                                    var targetTemp = entityManager.GetComponentData<Temp>(target);
                                    source += $", targetOriginal={targetTemp.m_Original.Index}:{targetTemp.m_Original.Version}, targetFlags={targetTemp.m_Flags}";
                                }
                                else source += ", targetTemp=(none)";
                                if (entityManager.HasComponent<Owner>(target))
                                {
                                    Entity targetOwner = entityManager.GetComponentData<Owner>(target).m_Owner;
                                    source += $", targetOwner={targetOwner.Index}:{targetOwner.Version}";
                                }
                                else source += ", targetOwner=(none)";
                                if (entityManager.HasComponent<Node>(target))
                                    source += $", targetNode={entityManager.GetComponentData<Node>(target).m_Position}";
                                if (entityManager.HasComponent<Curve>(target))
                                {
                                    var c = entityManager.GetComponentData<Curve>(target).m_Bezier;
                                    source += $", targetCurve=[{c.a};{c.b};{c.c};{c.d}]";
                                }
                                if (entityManager.HasComponent<Game.Objects.Transform>(target))
                                    source += $", targetPosition={entityManager.GetComponentData<Game.Objects.Transform>(target).m_Position}";
                            }
                        }
                    }
                    catch (System.Exception ex)
                    {
                        source += $", targetDiagnosticError={ex.GetType().Name}: {ex.Message}";
                    }
                    group.Sources.Add(source);
                }
            }
            return errors;
        }
    }
}
