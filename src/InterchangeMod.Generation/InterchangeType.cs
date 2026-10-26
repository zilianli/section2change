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
        /// <summary>定向 T 型：两条左转匝道左出左进（远侧幅立交内横移让出空间），以跨线桥跨过主线。</summary>
        DirectionalT,
        /// <summary>半定向 T 型：两条左转匝道右出右进，以跨线桥跨过主线。</summary>
        SemiDirectionalT,
        /// <summary>中式快速路节点：主线高架直行，匝道落到两侧地面辅路，地面只有辅路与被交道路的平交口。</summary>
        ExpresswayNode,
        /// <summary>快速路 T 型节点：中式快速路节点的三向版本，支路从高架下穿过、接两侧辅路。</summary>
        ExpresswayT,
        /// <summary>Y 型分向：三条路约 120° 汇于一处，三向全定向、交点分散（两层）。</summary>
        Wye,
    }
}
