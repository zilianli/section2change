using System;

namespace InterchangeMod.Generation.Roads
{
    /// <summary>
    /// 一种道路的基本信息，由落地层从游戏预制体读出（车道来自预制体的默认车道列表）。
    /// 生成层只用这些数字做匹配，不依赖游戏。
    /// </summary>
    public sealed class RoadInfo
    {
        public RoadInfo(string name, bool isHighway, int forwardLanes, int backwardLanes, float width, int publicTransportLanes = 0,
            HeightRange? groundHeight = null, HeightRange? elevatedHeight = null, float maxSlope = 0f)
        {
            MaxSlope = maxSlope;
            Name = name;
            IsHighway = isHighway;
            ForwardLanes = forwardLanes;
            BackwardLanes = backwardLanes;
            Width = width;
            PublicTransportLanes = publicTransportLanes;
            GroundHeight = groundHeight;
            ElevatedHeight = elevatedHeight;
        }

        /// <summary>道路在地面时的竖向范围（NetGeometryData.m_DefaultHeightRange）；未知时为 null。</summary>
        public HeightRange? GroundHeight { get; }

        /// <summary>道路作为高架时的竖向范围（NetGeometryData.m_ElevatedHeightRange）；未知时为 null。</summary>
        public HeightRange? ElevatedHeight { get; }

        /// <summary>游戏允许的最大纵坡（NetGeometryData.m_MaxSlopeSteepness，0.1 = 10%）；0 表示未知。</summary>
        public float MaxSlope { get; }

        /// <summary>高架时路段中部立柱（见 LegSpec.MiddlePillarSpacing）：null 没有，0 正中一根，&gt;0 均布间距。</summary>
        public float? MiddlePillarSpacing { get; set; }

        /// <summary>立柱宽度（米）；0 表示未知。</summary>
        public float PillarWidth { get; set; }

        /// <summary>高架时游戏不切分的最长路段（NetGeometryData.m_ElevatedLength）；0 表示未知。</summary>
        public float ElevatedLength { get; set; }

        /// <summary>道路选择表里显示的图标地址（游戏 UI 资源路径）；未知时为 null。</summary>
        public string? Icon { get; set; }

        public string Name { get; }

        /// <summary>公路（游戏里的 UseHighwayRules），否则是街道。</summary>
        public bool IsHighway { get; }

        /// <summary>沿道路方向行驶的普通车道数（不含公交专用道）。</summary>
        public int ForwardLanes { get; }

        /// <summary>逆道路方向行驶的普通车道数。</summary>
        public int BackwardLanes { get; }

        public float Width { get; }

        /// <summary>公交专用道数（两个方向合计）。</summary>
        public int PublicTransportLanes { get; }

        public bool IsOneWay => (ForwardLanes == 0) != (BackwardLanes == 0);

        /// <summary>
        /// 每个行驶方向的车道数：单行道路就是全部车道；双向道路取较少的一侧（不对称道路偏保守）。
        /// </summary>
        public int LanesPerDirection => IsOneWay ? Math.Max(ForwardLanes, BackwardLanes) : Math.Min(ForwardLanes, BackwardLanes);

        public override string ToString() =>
            $"{Name} | {(IsHighway ? "highway" : "street")} | {ForwardLanes}+{BackwardLanes} lanes" +
            (PublicTransportLanes > 0 ? $" +{PublicTransportLanes} bus" : "") + $" | {Width:F0} m" +
            (GroundHeight.HasValue ? $" | height ground {GroundHeight}, elevated {ElevatedHeight}" : "") +
            (MaxSlope > 0f ? $" | max slope {MaxSlope:P0}" : "") +
            (ElevatedLength > 0f ? $" | elevated edge {ElevatedLength:F0} m" : "") +
            $" | middle pillars {(MiddlePillarSpacing == null ? "none" : MiddlePillarSpacing == 0f ? "single" : $"every {MiddlePillarSpacing:F0} m")}" +
            (PillarWidth > 0f ? $", width {PillarWidth:F1} m" : "");
    }
}
