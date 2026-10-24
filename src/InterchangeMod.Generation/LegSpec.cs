using InterchangeMod.Generation.Geometry;

namespace InterchangeMod.Generation
{
    /// <summary>道路形式。</summary>
    public enum RoadForm
    {
        /// <summary>一条双向道路。</summary>
        TwoWay,
        /// <summary>双幅：两条单行道路对向并行，中间是分隔带。</summary>
        Dual,
    }

    /// <summary>
    /// 一条汇入臂：从立交中心伸出、通向外部道路的方向。三向立交有 3 条，四向有 4 条。
    /// </summary>
    public sealed class LegSpec
    {
        public LegSpec(float angle, int level, string? roadPrefab = null, float stubLength = 80f)
        {
            Angle = angle;
            Level = level;
            RoadPrefab = roadPrefab;
            StubLength = stubLength;
        }

        /// <summary>
        /// 方向角（度），从上往下看顺时针：0 = +Z（北），90 = +X（东）。
        /// 与 Vec3.RotateY 及 Unity 的 Y 轴旋转方向一致。
        /// </summary>
        public float Angle { get; set; }

        /// <summary>所在层在 InterchangeParams.Levels 中的下标。</summary>
        public int Level { get; set; }

        /// <summary>
        /// 道路预制体名。双幅时是每一幅用的单行道路。null 表示使用玩家在道路菜单里最近选的道路。
        /// </summary>
        public string? RoadPrefab { get; set; }

        /// <summary>臂端引道长度：匝道接口之外再延伸多长，用于和外部道路衔接。</summary>
        public float StubLength { get; set; }

        public RoadForm Form { get; set; } = RoadForm.TwoWay;

        /// <summary>道路宽度（米）。双幅时是单幅宽度。由工具层从预制体读出。</summary>
        public float RoadWidth { get; set; } = 12f;

        /// <summary>双幅道路中央分隔带宽度（米）。</summary>
        public float MedianWidth { get; set; } = 4f;

        /// <summary>
        /// 道路在地面时的竖向范围（相对路面，米）：Min 为结构底（负数），Max 为路面以上的净空顶。
        /// 由工具层从预制体 NetGeometryData.m_DefaultHeightRange 读出。
        /// 默认值让净空要求等于 7.5 米：游戏内实测两条道路路面高差至少 7.5 米才不报“净空不足”。
        /// </summary>
        public HeightRange GroundHeight { get; set; } = new HeightRange(0f, HeightRange.MeasuredMinimumGap);

        /// <summary>道路作为高架时的竖向范围（NetGeometryData.m_ElevatedHeightRange）。</summary>
        public HeightRange ElevatedHeight { get; set; } = new HeightRange(0f, HeightRange.MeasuredMinimumGap);

        /// <summary>
        /// 臂端外部道路的形式；null 表示与立交内部（Form）相同。
        /// 内部为双幅、外部为双向时，臂端生成过渡段：两幅汇合成一个节点，再接一段外部的双向道路。
        /// </summary>
        public RoadForm? ExternalForm { get; set; }

        /// <summary>过渡段外侧那段双向道路的预制体名（外部形式与内部不同时使用）。</summary>
        public string? ExternalPrefab { get; set; }

        /// <summary>
        /// 臂端外部道路的离地高度；null 表示不接外部道路。与这条臂所在层的高度不同时，
        /// 臂端再延伸一段坡道升降到这个高度（见 InterchangeLayout.AddApproachSlopes），好接回原有道路。
        /// </summary>
        public float? ExternalElevation { get; set; }

        /// <summary>这条臂的臂端是否需要从内部双幅过渡到外部双向。</summary>
        public bool NeedsDualToTwoWayTransition => Form == RoadForm.Dual && ExternalForm == RoadForm.TwoWay;

        /// <summary>
        /// 高架时路段中部的立柱：null = 没有中部立柱（只在节点有）；0 = 每段正中一根；
        /// > 0 = 按这个间距均布（游戏规则：段长 L 时根数 n = ⌊L / 间距 − 0.5⌋，位于 L·k/(n+1)，L &lt; 1.5 倍间距时没有）。
        /// 由工具层从预制体的 SubObject（EdgePlacement + MiddlePlacement 的立柱）读出。
        /// </summary>
        public float? MiddlePillarSpacing { get; set; }

        /// <summary>立柱宽度（米），用来判断立柱能否放进下层双幅道路的分隔带。</summary>
        public float PillarWidth { get; set; } = 4f;

        /// <summary>高架时游戏不切分的最长路段（NetGeometryData.m_ElevatedLength，普通道路 80 米）。</summary>
        public float MaxElevatedEdgeLength { get; set; } = 80f;

        /// <summary>按离地高度取竖向范围：只有高架桥（≥ 8.5 米）用高架范围，路堤和地面都按地面范围。</summary>
        public HeightRange HeightAt(float elevation) =>
            elevation >= HeightRange.BridgeThreshold ? ElevatedHeight : GroundHeight;

        /// <summary>双幅时每幅中心线离道路中心线的距离；双向道路为 0。</summary>
        public float CarriagewayOffset => Form == RoadForm.Dual ? (MedianWidth + RoadWidth) / 2f : 0f;

        /// <summary>道路外缘离中心线的距离。</summary>
        public float HalfExtent => CarriagewayOffset + RoadWidth / 2f;

        /// <summary>该臂方向的单位向量（水平面）。</summary>
        public Vec3 Direction => Directions.FromAngle(Angle);

        public LegSpec Clone() => (LegSpec)MemberwiseClone();
    }
}
