namespace InterchangeMod.Generation
{
    /// <summary>支持（或计划支持）的立交类型。</summary>
    public enum InterchangeType
    {
        /// <summary>菱形立交：4 条直匝道，被交道路上两个平交口。</summary>
        Diamond,
        /// <summary>全苜蓿叶：4 条环形匝道 + 4 条外侧直匝道。</summary>
        Cloverleaf,
        /// <summary>部分苜蓿叶 (ParClo)。</summary>
        PartialCloverleaf,
        /// <summary>喇叭形：T 型路口常用。</summary>
        Trumpet,
        /// <summary>涡轮立交。</summary>
        Turbine,
        /// <summary>定向/堆叠式立交，3–4 层。</summary>
        Stack,
        /// <summary>单点立交 (SPUI)。</summary>
        SinglePoint,
        /// <summary>环岛式立交。</summary>
        Roundabout,
        /// <summary>中式快速路节点：主线高架直行，匝道落到两侧地面辅路，地面只有辅路与被交道路的平交口。</summary>
        ExpresswayNode,
    }
}
