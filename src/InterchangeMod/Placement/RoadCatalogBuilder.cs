using System.Collections.Generic;
using Game.Prefabs;
using InterchangeMod.Generation;
using InterchangeMod.Generation.Roads;
using Unity.Collections;
using Unity.Entities;

namespace InterchangeMod.Placement
{
    /// <summary>
    /// 从游戏预制体读出所有道路的车道数、公路/街道类型和宽度。
    ///
    /// 道路预制体上没有现成的车道列表（DefaultNetLane 只挂在管线、电线、水道上），
    /// 所以按游戏 NetInitializeSystem 的做法现算默认截面的车道：
    ///   GetCompositionPieces → CalculateCompositionData → AddCompositionLanes。
    /// 计数规则：只数带 Road 标志的车道；Master 是游戏额外加的“车道组”条目，跳过；
    ///   Invert = 逆道路方向；PublicOnly = 公交专用道，单独计数；自行车道不算。
    /// </summary>
    public static class RoadCatalogBuilder
    {
        /// <summary>计算车道所需的只读查找表，必须在系统的 OnUpdate 里创建。</summary>
        public struct Lookups
        {
            public BufferLookup<NetSubSection> SubSections;
            public BufferLookup<NetSectionPiece> SectionPieces;
            public ComponentLookup<NetPieceData> Pieces;
            public ComponentLookup<NetVertexMatchData> VertexMatches;
            public ComponentLookup<NetLaneData> Lanes;
            public BufferLookup<NetPieceLane> PieceLanes;
        }

        public static List<RoadInfo> Build(EntityManager entityManager, PrefabSystem prefabSystem, Lookups lookups)
        {
            using var query = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<RoadData>(),
                ComponentType.ReadOnly<NetGeometryData>(),
                ComponentType.ReadOnly<NetGeometrySection>(),
                ComponentType.ReadOnly<PrefabData>());
            using var entities = query.ToEntityArray(Allocator.Temp);
            var pieces = new NativeList<NetCompositionPiece>(32, Allocator.Temp);
            var lanes = new NativeList<NetCompositionLane>(32, Allocator.Temp);

            var roads = new List<RoadInfo>(entities.Length);
            try
            {
                foreach (Entity entity in entities)
                {
                    if (!prefabSystem.TryGetPrefab(entity, out PrefabBase prefab))
                        continue;

                    pieces.Clear();
                    lanes.Clear();
                    var sections = entityManager.GetBuffer<NetGeometrySection>(entity, true);
                    NetCompositionHelpers.GetCompositionPieces(pieces, sections.AsNativeArray(), default(CompositionFlags),
                        lookups.SubSections, lookups.SectionPieces);
                    var composition = default(NetCompositionData);
                    NetCompositionHelpers.CalculateCompositionData(ref composition, pieces.AsArray(), lookups.Pieces, lookups.VertexMatches);
                    NetCompositionHelpers.AddCompositionLanes(Entity.Null, ref composition, pieces, lanes,
                        default(DynamicBuffer<NetCompositionCarriageway>), lookups.Lanes, lookups.PieceLanes);

                    CountLanes(lanes, out int forward, out int backward, out int bus);
                    var road = entityManager.GetComponentData<RoadData>(entity);
                    var geometry = entityManager.GetComponentData<NetGeometryData>(entity);
                    bool highway = (road.m_Flags & RoadFlags.UseHighwayRules) != 0;
                    var info = new RoadInfo(prefab.name, highway, forward, backward, geometry.m_DefaultWidth, bus,
                        new HeightRange(geometry.m_DefaultHeightRange.min, geometry.m_DefaultHeightRange.max),
                        new HeightRange(geometry.m_ElevatedHeightRange.min, geometry.m_ElevatedHeightRange.max),
                        geometry.m_MaxSlopeSteepness)
                    {
                        ElevatedLength = geometry.m_ElevatedLength,
                        // 和游戏道路工具栏用同一个图标（UIObject 图标，没有时用缩略图）
                        Icon = Game.UI.ImageSystem.GetThumbnail(prefab),
                    };
                    ReadMiddlePillars(entityManager, entity, info);
                    roads.Add(info);
                }
            }
            finally
            {
                pieces.Dispose();
                lanes.Dispose();
            }

            roads.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            return roads;
        }

        /// <summary>
        /// 路段中部立柱：道路预制体的 SubObject 里，EdgePlacement + MiddlePlacement 且对象带 PillarData 的那一项
        /// （NetInitializeSystem 把“EdgeMiddle”放置的立柱这样登记；EvenSpacing 时 m_Position.z 是间距）。
        /// </summary>
        private static void ReadMiddlePillars(EntityManager entityManager, Entity road, RoadInfo info)
        {
            if (!entityManager.HasBuffer<SubObject>(road))
                return;
            const SubObjectFlags middle = SubObjectFlags.EdgePlacement | SubObjectFlags.MiddlePlacement;
            foreach (SubObject sub in entityManager.GetBuffer<SubObject>(road, true))
            {
                if ((sub.m_Flags & middle) != middle || !entityManager.HasComponent<PillarData>(sub.m_Prefab))
                    continue;
                float spacing = (sub.m_Flags & SubObjectFlags.EvenSpacing) != 0 ? sub.m_Position.z : 0f;
                // 有多种中部立柱时取最密的（间距最小、无间距视为正中一根）
                if (info.MiddlePillarSpacing == null || spacing < info.MiddlePillarSpacing.Value)
                    info.MiddlePillarSpacing = spacing;
                if (entityManager.HasComponent<ObjectGeometryData>(sub.m_Prefab))
                {
                    var size = entityManager.GetComponentData<ObjectGeometryData>(sub.m_Prefab).m_Size;
                    info.PillarWidth = System.Math.Max(info.PillarWidth, System.Math.Max(size.x, size.z));
                }
            }
        }

        private static void CountLanes(NativeList<NetCompositionLane> lanes, out int forward, out int backward, out int bus)
        {
            forward = backward = bus = 0;
            for (int i = 0; i < lanes.Length; i++)
            {
                LaneFlags flags = lanes[i].m_Flags;
                if ((flags & LaneFlags.Road) == 0 || (flags & (LaneFlags.Master | LaneFlags.BicyclesOnly)) != 0)
                    continue;
                if ((flags & LaneFlags.PublicOnly) != 0)
                    bus++;
                else if ((flags & LaneFlags.Invert) != 0)
                    backward++;
                else
                    forward++;
            }
        }

        /// <summary>调试用：每种道路一行，方便和游戏里的实际车道对照。</summary>
        public static void Log(IReadOnlyList<RoadInfo> roads)
        {
            Mod.log.Info($"[debug] {roads.Count} road prefabs (name | type | forward+backward lanes | width):");
            foreach (var road in roads)
                Mod.log.Info($"[debug]   {road}");
        }
    }
}
