namespace InterchangeMod.Generation
{
    /// <summary>离地高度所处的分段，决定游戏把道路建成什么（见 HeightRange 的阈值说明）。</summary>
    public enum ElevationBand
    {
        /// <summary>≥ 8.5 米：高架桥，有立柱，下方可通行。</summary>
        Elevated,
        /// <summary>4 ~ 8.5 米：路堤，实心带挡土墙，下方不能通行。</summary>
        Raised,
        /// <summary>−4 ~ 4 米：地面。</summary>
        Ground,
        /// <summary>−13 ~ −4 米：下沉路堑。</summary>
        Cutting,
        /// <summary>≤ −13 米：隧道（实测；游戏代码为 −12 米，但按道路边缘测高度）。</summary>
        Tunnel,
    }

    /// <summary>
    /// 分层规划：只设基层（下层）高度，其他层按层间高差往上叠。
    /// 层间高差可手动给，也可自动：
    ///   基层是隧道 → 上层放在地面（0 米）：隧道不和地面道路碰撞，不需要 7.5 米净空；
    ///   否则取两种道路之间需要的最小净空（不低于实测 7.5 米），且上层若在地面以上，必须达到高架桥高度（8.5 米），
    ///   否则会建成路堤挡住下层。
    /// 以后自动连接外部道路时，基层高度取外部最低节点的高度。
    /// </summary>
    public static class LevelPlanner
    {
        /// <summary>
        /// 下层在 baseElevation、上层在其上时需要的最小净空。
        /// 上层是否算高架桥取决于它自己的高度，所以先按实测最小值估一次上层高度，再按道路数据求最终值。
        /// </summary>
        public static float RequiredGap(LegSpec lower, LegSpec upper, float baseElevation)
        {
            float estimate = HeightRange.MeasuredMinimumGap;
            float gap = HeightRange.RequiredGap(lower.HeightAt(baseElevation), upper.HeightAt(baseElevation + estimate));
            // 估出的高差更大时，上层可能从路堤变成高架桥，竖向范围随之变化，再算一次取较大值
            float second = HeightRange.RequiredGap(lower.HeightAt(baseElevation), upper.HeightAt(baseElevation + gap));
            return System.Math.Max(gap, second);
        }

        /// <summary>自动层间高差（见类说明）。</summary>
        public static float AutoGap(LegSpec lower, LegSpec upper, float baseElevation)
        {
            if (BandOf(baseElevation) == ElevationBand.Tunnel)
                return -baseElevation;

            float gap = RequiredGap(lower, upper, baseElevation);
            float upperElevation = baseElevation + gap;
            if (upperElevation > HeightRange.RaisedThreshold && upperElevation < HeightRange.BridgeThreshold)
                gap = HeightRange.BridgeThreshold - baseElevation;
            return gap;
        }

        /// <summary>层间高差：manualGap ≤ 0 表示自动。手动值不合适时原样返回，由模板校验报错。</summary>
        public static float ResolveGap(LegSpec lower, LegSpec upper, float baseElevation, float manualGap) =>
            manualGap > 0f ? manualGap : AutoGap(lower, upper, baseElevation);

        public static ElevationBand BandOf(float elevation)
        {
            if (elevation >= HeightRange.BridgeThreshold)
                return ElevationBand.Elevated;
            if (elevation >= HeightRange.RaisedThreshold)
                return ElevationBand.Raised;
            if (elevation > -HeightRange.RaisedThreshold)
                return ElevationBand.Ground;
            if (elevation > HeightRange.TunnelThreshold)
                return ElevationBand.Cutting;
            return ElevationBand.Tunnel;
        }
    }
}
