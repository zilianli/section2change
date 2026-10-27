using System;
using System.Collections.Generic;
using System.Linq;
using InterchangeMod.Generation.Geometry;

namespace InterchangeMod.Generation.Templates
{
    /// <summary>
    /// 中式快速路节点（四向）。主线（臂 0/2，双幅）高架直行跨过被交道路（臂 1/3）；
    /// 主线两侧各有一幅地面辅路（单行，与同侧主线同向），匝道都在同侧：
    ///   下匝道在路口前从主线分出、落地并入辅路；上匝道在路口后从辅路分出、爬升并入主线。
    /// 地面只有辅路与被交道路的平交口（被交道路上两个节点），被交道路从高架下穿过。
    ///
    /// 横向（从主线中心向外，右侧通行时 NB 在右）：
    ///   主线一幅 ±om ── 匝道 ±cl ── 辅路 ±og，cl = om + S1，og = cl + S2
    ///   S1 = 主线半宽 + 匝道半宽 + 间距（匝道离开主线），S2 = 辅路半宽 + 匝道半宽 + 间距（匝道离开辅路）。
    ///
    /// 每条匝道三段，处处切线连续：
    ///   平移段（在主线高度，与主线相切的反向曲线，横移 S1）→ 升降段（俯视直线，竖向两端水平，坡度不超过上限）
    ///   → 平移段（在地面，横移 S2，与辅路相切）。下匝道按行驶顺序是主线 → 辅路，上匝道反过来。
    /// </summary>
    public sealed class ExpresswayNodeTemplate : IInterchangeTemplate
    {
        private const float kMinCrossingAngle = 45f;
        private const float kOppositeTolerance = 1f;
        /// <summary>升降段坡度留一点余量，避免数值误差超出上限。</summary>
        private const float kGradeMargin = 0.98f;

        /// <param name="tee">
        /// true = T 型（三向）：被交道路只在一侧（臂 1，支路），从高架下穿过，在远侧辅路处结束，
        /// 与两侧辅路各形成一个 T 字路口；其余（主线、辅路、四条匝道）与十字型相同。
        /// </param>
        public ExpresswayNodeTemplate(bool tee = false)
        {
            m_Tee = tee;
        }

        private readonly bool m_Tee;

        public InterchangeType Type => m_Tee ? InterchangeType.ExpresswayT : InterchangeType.ExpresswayNode;

        public string DisplayName => m_Tee ? "快速路 T 型节点" : "中式快速路节点";

        public int LegCount => m_Tee ? 3 : 4;

        /// <summary>被交道路（支路）另一端的臂：十字型是臂 3，T 型没有，用臂 1 自己。</summary>
        private int OtherCrossLeg => m_Tee ? 1 : 3;

        public IReadOnlyList<string> ValidateParams(InterchangeParams p)
        {
            var errors = p.ValidateLegsAndLevels(LegCount);
            if (errors.Count > 0)
                return errors;

            var legs = p.Legs;
            if (Directions.AngleBetween(legs[0].Angle, legs[2].Angle) < 180f - kOppositeTolerance
                || (!m_Tee && Directions.AngleBetween(legs[1].Angle, legs[3].Angle) < 180f - kOppositeTolerance))
                errors.Add(m_Tee ? "快速路 T 型节点要求主线（臂 0/2）是直线" : "快速路节点要求主线（臂 0/2）和被交道路（臂 1/3）各自是直线");

            float crossing = Directions.AngleBetween(legs[0].Angle, legs[1].Angle);
            if (crossing < kMinCrossingAngle || crossing > 180f - kMinCrossingAngle)
            {
                errors.Add($"相交角度 {crossing:F0}° 太斜：快速路节点需要 {kMinCrossingAngle:F0}°–{180f - kMinCrossingAngle:F0}°");
                return errors;
            }

            if (legs[0].Level != legs[2].Level || legs[1].Level != legs[OtherCrossLeg].Level)
                errors.Add("同一条道路两端必须在同一层");
            if (!DiamondTemplate.SameRoad(legs[0], legs[2]) || !DiamondTemplate.SameRoad(legs[1], legs[OtherCrossLeg]))
                errors.Add("同一条道路两端的形式、宽度和分隔带必须一致");

            if (legs[0].Form != RoadForm.Dual)
                errors.Add("快速路主线需要是双幅（两条对向单行道路）：请把主线形式改为双幅，或选择单行的公路");
            if (string.IsNullOrEmpty(p.FrontagePrefab) || p.FrontageWidth <= 0f)
                errors.Add("没有指定地面辅路（单行道路）");

            // 主线高架在被交道路和辅路之上，且必须是高架桥（4 ~ 8.5 米会建成挡住地面道路的路堤）
            float hMain = p.ElevationOf(legs[0]), hGround = p.ElevationOf(legs[1]);
            float gap = DiamondTemplate.RequiredGap(p);
            if (hMain - hGround < gap - HeightRange.GapTolerance)
                errors.Add($"主线比地面道路高 {hMain - hGround:F1} 米，至少需要 {gap:F1} 米净空：主线要在上层");
            else if (LevelPlanner.BandOf(hMain) != ElevationBand.Elevated)
                errors.Add($"主线离地 {hMain:F1} 米会建成路堤（挡土墙），被交道路无法穿过：主线需要至少 {HeightRange.BridgeThreshold:F1} 米");

            // 匝道比它分出/并入的路还宽时，游戏无法生成分流节点的形状
            if (p.RampWidth > legs[0].RoadWidth)
                errors.Add($"匝道宽 {p.RampWidth:F0} 米，比主线（{legs[0].RoadWidth:F0} 米）还宽：请减少匝道车道数");
            if (p.RampWidth > p.FrontageWidth)
                errors.Add($"匝道宽 {p.RampWidth:F0} 米，比辅路（{p.FrontageWidth:F0} 米）还宽：请换更宽的辅路或减少匝道车道数");

            // 主线跨越被交道路处的避让节点要落在匝道节点之间
            var layout = new Layout(p);
            foreach (float lateral in DiamondTemplate.Laterals(legs[0]))
            {
                var span = DiamondTemplate.LineSpan(p, onMain: true, DiamondTemplate.ReducedLateral(p, lateral));
                if (span.Error != null && !errors.Contains(span.Error))
                    errors.Add(span.Error);
                else if (span.Lo <= -layout.MainNode + 1f || span.Hi >= layout.MainNode - 1f)
                    errors.Add("匝道太短，主线在跨越处放不下避让节点：请加长匝道");
            }
            return errors;
        }

        public IReadOnlyList<RoadSegmentSpec> Generate(InterchangeParams p)
        {
            var legs = p.Legs;
            var L = new Layout(p);
            var segments = new List<RoadSegmentSpec>();
            Vec3 main = legs[0].Direction, cross = legs[1].Direction;
            Vec3 side = Directions.RightOf(main) * (p.RightHandTraffic ? 1f : -1f);   // NB 一侧
            var up = new Vec3(0f, 1f, 0f);
            Vec3 Point(float along, float lateral, float h) => main * along + side * lateral + up * h;

            // —— 被交道路与辅路的交点（两条线共用同一个 Vec3）——
            Vec3 crossSide = Directions.RightOf(cross) * (p.RightHandTraffic ? 1f : -1f);
            float oc = legs[1].CarriagewayOffset;
            float[] crossLaterals = DiamondTemplate.Laterals(legs[1]);
            Vec3 Junction(float frontageLateral, float crossLateral) =>
                Intersect(side * frontageLateral, main, crossSide * crossLateral, cross) + up * L.Ground;

            // —— 主线两端：外部道路比主线低（原有道路在地面、主线是新建高架）时下坡落地，再接外部道路 ——
            float mainOuter = L.MainNode + legs[0].StubLength;
            var ends = new[] { 1, -1 }.Select(e => (e, end: EndOf(p, L, e, mainOuter))).ToArray();

            // —— 主线（高架，双幅）——
            foreach (int sign in new[] { 1, -1 })
            {
                float lateral = sign * L.Om;
                // 匝道之间的中段减车道时按内侧（直行车道）对齐
                float inner = DiamondTemplate.ReducedLateral(p, lateral);
                var span = DiamondTemplate.LineSpan(p, onMain: true, inner);
                var mainNodes = new List<Vec3>
                {
                    Point(-L.MainNode, lateral, L.Main), Point(span.Lo, inner, L.Main), Point(span.Hi, inner, L.Main),
                    Point(L.MainNode, lateral, L.Main),
                };
                foreach (var (e, end) in ends)
                {
                    if (!end.Landing)
                    {
                        mainNodes.Add(Point(e * mainOuter, lateral, L.Main));
                        continue;
                    }
                    // 坡顶 → 坡底（地面）→ 外端（或过渡段起点）
                    mainNodes.Add(Point(e * end.SlopeTop, lateral, L.Main));
                    mainNodes.Add(Point(e * end.SlopeBottom, lateral, end.Elevation));
                    mainNodes.Add(Point(e * end.Outer, lateral, end.Elevation));
                }
                int first = segments.Count;
                DiamondTemplate.AddRoadLine(segments, sign > 0 ? "MainNB" : "MainSB", RoadRole.MainRoad, main, travelPositive: sign > 0,
                    legs[2].RoadPrefab, legs[0].RoadPrefab, "2", "0", mainNodes, -L.MainNode, L.MainNode, p.MainReducedPrefab, side * (inner - lateral));   // 车道平衡：匝道分出点和汇入点之间减车道，边缘对齐
                // 坡道段用两端水平的竖曲线，和前后的水平段衔接没有竖向折角
                for (int i = first; i < segments.Count; i++)
                {
                    var s = segments[i];
                    if (MathF.Abs(s.Curve.P0.Y - s.Curve.P3.Y) > 0.1f)
                        segments[i] = new RoadSegmentSpec(s.Name, s.Role, Bezier3.SmoothSlope(s.Curve.P0, s.Curve.P3), s.Prefab, s.StartNode, s.EndNode);
                }
            }
            foreach (var (e, end) in ends)
            {
                var leg = legs[e > 0 ? 0 : 2];
                if (end.Landing && leg.NeedsDualToTwoWayTransition)
                    DiamondTemplate.AddTransition(segments, e > 0 ? "Main-0" : "Main-2", RoadRole.MainRoad, (along, lat) => Point(along, lat, end.Elevation),
                        e, end.Outer, L.Om, end.Outer + leg.StubLength, p.LegTransitionAngle, leg.RoadPrefab, leg.ExternalPrefab);
            }

            // —— 辅路（地面，两侧各一幅，与同侧主线同向）——
            foreach (int sign in new[] { 1, -1 })
            {
                float lateral = sign * L.Og;
                var nodes = new List<Vec3> { Point(-L.Landing, lateral, L.Ground), Point(L.Landing, lateral, L.Ground) };
                // 主线落地的一端：辅路不并回主线，和主线一起延伸到外端后结束（留给玩家自行连接）
                foreach (var (e, end) in ends)
                    nodes.Add(Point(e * (end.Landing ? end.Outer : mainOuter), lateral, L.Ground));
                // 加宽段（落地点到上匝道分出点之间，含平交口）按外侧（直行车道）对齐
                nodes.AddRange(crossLaterals.Select(c => Junction(sign * L.FrontInner, c)));
                string name = sign > 0 ? "FrontNB" : "FrontSB";
                DiamondTemplate.AddRoadLine(segments, name, RoadRole.FrontageRoad, main, travelPositive: sign > 0,
                    p.FrontagePrefab, p.FrontagePrefab, "2", "0", nodes, -L.Landing, L.Landing, p.FrontageWidenedPrefab, side * (sign * L.FrontInner - lateral));
            }

            // —— 被交道路（地面）：与两幅辅路平交，从高架下穿过 ——
            float crossOuter = new[] { L.FrontInner, -L.FrontInner }.SelectMany(f => crossLaterals.Select(c => MathF.Abs(Directions.Dot(Junction(f, c), cross)))).Max()
                + legs[1].StubLength;
            foreach (float lateral in crossLaterals)
            {
                var span = DiamondTemplate.LineSpan(p, onMain: false, lateral);
                bool travelPositive = lateral >= 0f;
                string name = crossLaterals.Length == 1 ? "Cross" : travelPositive ? "CrossPos" : "CrossNeg";
                var nodes = new List<Vec3>
                {
                    Junction(L.FrontInner, lateral), Junction(-L.FrontInner, lateral),
                    cross * span.Lo + crossSide * lateral + up * L.Ground,
                    cross * span.Hi + crossSide * lateral + up * L.Ground,
                    cross * crossOuter + crossSide * lateral + up * L.Ground,
                };
                // T 型：支路只伸向臂 1 一侧，另一端就在远侧辅路的 T 字路口结束
                if (!m_Tee)
                    nodes.Add(cross * -crossOuter + crossSide * lateral + up * L.Ground);
                DiamondTemplate.AddRoadLine(segments, name, RoadRole.CrossRoad, cross, travelPositive,
                    legs[OtherCrossLeg].RoadPrefab, legs[1].RoadPrefab, m_Tee ? "far" : "3", "1", nodes);
            }

            // —— 匝道 ——
            foreach (int sign in new[] { 1, -1 })
            {
                string dir = sign > 0 ? "NB" : "SB";
                Vec3 travel = main * sign;
                // 沿行驶方向的坐标 u：u < 0 在路口前，u > 0 在路口后；实际沿主线坐标 = sign × u
                Vec3 At(float u, float lateral, float h) => Point(sign * u, sign * lateral, h);

                // 下匝道（路口前）：主线 → 平移 → 下降 → 平移 → 辅路
                Vec3 offMain = At(-L.MainNode, L.Om, L.Main);
                Vec3 offTop = At(-L.MainNode + L.MainShift, L.Cl, L.Main);
                Vec3 offBottom = At(-L.Landing - L.GroundShift, L.Cl, L.Ground);
                Vec3 offFront = At(-L.Landing, L.Og, L.Ground);
                Vec3 offLane = At(-L.Landing, L.RampLane, L.Ground);
                DiamondTemplate.AddElevatedRamp(segments, p, $"{dir}-off-shift", Bezier3.FromTangents(offMain, travel, offTop, travel, L.MainShift / 3f), nodeAtEnd: false);
                segments.Add(new RoadSegmentSpec($"{dir}-off-down", RoadRole.Ramp, Bezier3.SmoothSlope(offTop, offBottom), p.RampPrefab));
                segments.Add(new RoadSegmentSpec($"{dir}-off-merge", RoadRole.Ramp, Bezier3.FromTangents(offBottom, travel, offLane, travel, L.GroundShift / 3f), p.RampPrefab,
                    endNode: offFront));

                // 上匝道（路口后）：辅路 → 平移 → 爬升 → 平移 → 主线
                Vec3 onFront = At(L.Landing, L.Og, L.Ground);
                Vec3 onLane = At(L.Landing, L.RampLane, L.Ground);
                Vec3 onBottom = At(L.Landing + L.GroundShift, L.Cl, L.Ground);
                Vec3 onTop = At(L.MainNode - L.MainShift, L.Cl, L.Main);
                Vec3 onMain = At(L.MainNode, L.Om, L.Main);
                segments.Add(new RoadSegmentSpec($"{dir}-on-diverge", RoadRole.Ramp, Bezier3.FromTangents(onLane, travel, onBottom, travel, L.GroundShift / 3f), p.RampPrefab,
                    startNode: onFront));
                segments.Add(new RoadSegmentSpec($"{dir}-on-up", RoadRole.Ramp, Bezier3.SmoothSlope(onBottom, onTop), p.RampPrefab));
                DiamondTemplate.AddElevatedRamp(segments, p, $"{dir}-on-shift", Bezier3.FromTangents(onTop, travel, onMain, travel, L.MainShift / 3f), nodeAtEnd: true);
            }
            return segments;
        }

        /// <summary>主线落地端：坡顶离最外匝道节点的距离（米）。</summary>
        private const float kEndStub = 20f;
        /// <summary>坡底到双幅 → 双向过渡段起点的距离（米）。</summary>
        private const float kEndTransitionGap = 20f;

        /// <summary>主线一端（沿主线 e = +1 为臂 0 一端，−1 为臂 2 一端）的布置，距离都是离路口中心的距离。</summary>
        private readonly struct EndPlan
        {
            public EndPlan(bool landing, float elevation, float slopeTop, float slopeBottom, float outer)
            {
                Landing = landing;
                Elevation = elevation;
                SlopeTop = slopeTop;
                SlopeBottom = slopeBottom;
                Outer = outer;
            }

            /// <summary>这一端主线要下坡落地（外部道路比主线低）。</summary>
            public bool Landing { get; }
            /// <summary>落地后的高度（外部道路的高度）。</summary>
            public float Elevation { get; }
            public float SlopeTop { get; }
            public float SlopeBottom { get; }
            /// <summary>主线双幅的外端（外部是双向道路时是过渡段起点）；辅路也延伸到这里结束。</summary>
            public float Outer { get; }
        }

        /// <summary>
        /// 这一端的外部道路（LegSpec.ExternalElevation，原路口改造时由工具设置）比高架主线低时：
        /// 主线在最外匝道节点之后 kEndStub 处开始用平滑坡道（长度 ≈ 1.5 × 高差 ÷ 坡度上限）降到外部高度，
        /// 再延伸一段接外部道路；外部是双向道路时在这里双幅收拢成双向（与菱形的臂端过渡相同）。
        /// 辅路不并回主线，延伸到同一位置后结束（游戏内反馈：辅路不需要再连回快速路）。
        /// </summary>
        private static EndPlan EndOf(InterchangeParams p, Layout L, int e, float mainOuter)
        {
            var leg = p.Legs[e > 0 ? 0 : 2];
            if (!(leg.ExternalElevation is float external) || L.Main - external <= InterchangeLayout.ApproachTolerance)
                return new EndPlan(false, L.Main, 0f, 0f, mainOuter);
            float top = L.MainNode + kEndStub;
            float bottom = top + 1.5f * (L.Main - external) / (p.MaxGrade * kGradeMargin);
            float outer = leg.NeedsDualToTwoWayTransition ? bottom + kEndTransitionGap : bottom + leg.StubLength;
            return new EndPlan(true, external, top, bottom, outer);
        }

        /// <summary>两条直线（点 + 方向）在水平面上的交点。</summary>
        private static Vec3 Intersect(Vec3 pa, Vec3 da, Vec3 pb, Vec3 db)
        {
            float denominator = DiamondTemplate.Cross2(da, db);
            float s = DiamondTemplate.Cross2(pb - pa, db) / denominator;
            Vec3 point = pa + da * s;
            return new Vec3(point.X, 0f, point.Z);
        }

        /// <summary>由参数算出的各处距离（沿主线的位置都是离路口中心的距离）。</summary>
        private readonly struct Layout
        {
            public readonly float Main, Ground;
            /// <summary>主线一幅、匝道升降段、辅路的横向偏移。</summary>
            public readonly float Om, Cl, Og;
            /// <summary>辅路加宽段（含平交口）的横向偏移：加宽时向内平移，保持外侧边缘不变。</summary>
            public readonly float FrontInner;
            /// <summary>
            /// 匝道在落地节点（辅路中心线上）处的曲线端点横向位置：辅路加宽时匝道就是加宽段里侧多出来的车道，
            /// 内侧边缘和加宽段对齐（游戏内：匝道接在辅路中心线上，落地前一大段压在辅路上，报形状无效）；不加宽时就是辅路中心线。
            /// </summary>
            public readonly float RampLane;
            /// <summary>匝道在主线高度 / 地面上的平移段长度。</summary>
            public readonly float MainShift, GroundShift;
            /// <summary>匝道接辅路的节点离路口中心的距离。</summary>
            public readonly float Landing;
            /// <summary>匝道接主线的节点离路口中心的距离。</summary>
            public readonly float MainNode;

            public Layout(InterchangeParams p)
            {
                var legs = p.Legs;
                Main = p.ElevationOf(legs[0]);
                Ground = p.ElevationOf(legs[1]);
                float k = MathF.Tan(p.RampDivergeAngle * Directions.DegToRad);
                float s1 = legs[0].RoadWidth / 2f + p.RampWidth / 2f + p.RampSideGap;
                // 匝道落地前与辅路（未加宽）并行；加宽段在落地点之后，向内加宽正好接住匝道
                float s2 = p.FrontageWidth / 2f + p.RampWidth / 2f + p.RampSideGap;
                Om = legs[0].CarriagewayOffset;
                Cl = Om + s1;
                Og = Cl + s2;
                FrontInner = Og - p.FrontageAddShift;
                RampLane = p.FrontageAddShift > 0f ? Math.Max(Cl, FrontInner - p.FrontageWidenedWidth / 2f + p.RampWidth / 2f) : Og;
                // 反向曲线横移 s 的平均角度约为分流角的一半，长度约为同角度直线的两倍
                MainShift = 2f * s1 / k;
                GroundShift = 2f * s2 / k;

                // 落地点在辅路与被交道路平交口之前 RampTerminalOffset 处（斜交时按辅路上的交点算）
                float sin = MathF.Abs(DiamondTemplate.Cross2(legs[0].Direction, legs[1].Direction));
                float cot = MathF.Abs(Directions.Dot(legs[0].Direction, legs[1].Direction)) / sin;
                Landing = p.RampTerminalOffset + legs[1].HalfExtent / sin + Og * cot;

                float rise = MathF.Abs(Main - Ground);
                // 升降段两端水平（SmoothSlope），最陡处约为平均坡度的 1.5 倍
                float climb = Math.Max(1.5f * rise / (p.MaxGrade * kGradeMargin), p.RampLength - MainShift - GroundShift);
                MainNode = Landing + GroundShift + climb + MainShift;
            }
        }
    }
}
