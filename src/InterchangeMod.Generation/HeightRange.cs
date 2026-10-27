namespace InterchangeMod.Generation
{
    /// <summary>
    /// 道路的竖向范围（相对路面，米）：Min 为结构底（桥面厚度，负数），Max 为路面以上要保留的净空顶。
    /// 游戏检查净空时，上层道路的 Min 不能伸进下层道路的 Max 以内。
    /// </summary>
    public readonly struct HeightRange
    {
        // 游戏规则（NetCompositionHelpers.GetElevationFlags，普通道路 m_ElevationLimit = 4 米），
        // 高度按道路两侧边缘、在路段起点/中点/终点分别测，取最小值：
        //   ≥ 2 × 4 = 8 米      → 高架桥（Elevated，有立柱，下方可通行）
        //   4 ~ 8 米           → 路堤（Raised，实心、带挡土墙，下方不能通行）
        //   −4 ~ 4 米          → 地面
        //   −4 ~ −12 米        → 下沉路堑（Lowered）
        //   ≤ −12 米           → 隧道
        // 边缘测量在坡地上会比设定值偏小，所以桥和隧道的阈值各留余量（与游戏内实测一致）。

        /// <summary>离地高度达到这个值，道路成为路堤（m_ElevationLimit）。</summary>
        public const float RaisedThreshold = 4f;

        /// <summary>
        /// 离地高度达到这个值，道路才是高架桥（2 × m_ElevationLimit = 8 米，加 0.5 米余量）。
        /// 跨越其他道路的上层必须达到它，否则建成路堤挡住下层（游戏报“物体重叠”）。
        /// </summary>
        public const float BridgeThreshold = 8.5f;

        /// <summary>
        /// 离地高度不高于这个值时游戏把道路建成隧道。游戏代码是 −3 × m_ElevationLimit = −12 米，
        /// 但游戏按道路两侧边缘测高度，实测要到 −13 米才稳定成为隧道。介于 −4 和这个值之间是下沉路堑。
        /// </summary>
        public const float TunnelThreshold = -13f;

        /// <summary>游戏内实测：两条道路交叉跨越时，路面高差小于 7.5 米报“净空不足”。读不到预制体数据时用它兜底。</summary>
        public const float MeasuredMinimumGap = 7.5f;

        /// <summary>
        /// 判断高差够不够时的浮点余量（米）：从游戏读回的已有道路高度带有误差（如 17.50003 与 10.00004），层高差正好是最小净空时
        /// 算出来差万分之几米（游戏内：对准层高差 7.5 米的立体交叉，定向式、涡轮报“至少 7.5 米：现在高差 7.5 米”）。
        /// </summary>
        public const float GapTolerance = 0.01f;

        /// <summary>
        /// 游戏内实测：两条道路上下平行重叠（双层道路）时，路面高差至少 10 米。
        /// 菱形立交只有交叉跨越用不到；以后的定向/涡轮立交有平行叠放的匝道时用。
        /// </summary>
        public const float MeasuredStackedGap = 10f;

        public HeightRange(float min, float max)
        {
            Min = min;
            Max = max;
        }

        public float Min { get; }

        public float Max { get; }

        /// <summary>
        /// 上下两层道路路面之间至少要多高：下层的净空顶 − 上层的结构底，
        /// 且不低于实测最小值 7.5 米（游戏按道路两侧边缘测高度，坡地上实际高差会比设定值小）。
        /// </summary>
        public static float RequiredGap(HeightRange lower, HeightRange upper) => System.Math.Max(MeasuredMinimumGap, lower.Max - upper.Min);

        /// <summary>按预制体数据算出的高差（不含实测下限），供调试对照。</summary>
        public static float RawGap(HeightRange lower, HeightRange upper) => lower.Max - upper.Min;

        public override string ToString() => $"{Min:F1}..{Max:F1} m";
    }
}
