using System;
using System.Collections.Generic;
using System.Linq;
using InterchangeMod.Generation.Geometry;

namespace InterchangeMod.Generation.Templates
{
    /// <summary>
    /// 单点立交 SPUI（四向）。主线（臂 0/2，双幅）高架直行跨过被交道路（臂 1/3，双向，地面）；
    /// 4 条匝道都汇到桥下被交道路上的同一个节点（中心节点），游戏里只有一组信号灯。
    ///
    /// 每条匝道：主线高度横移（与主线相切）→ 俯视直线升降（两端水平）→ 地面转弯段（内切曲线）进出中心节点。
    /// 转弯段在中心节点处沿“主线方向与被交道路方向的角平分线”进出，所以 4 条匝道在中心节点处两两相差 90°，
    /// 转弯段两端切线等长、只朝一个方向弯（交点法放手柄）。
    /// 匝道在桥下穿过主线投影，主线跨越段的立柱同时避开被交道路和匝道（见 MainNodes）。
    /// </summary>
    public sealed class SinglePointTemplate : IInterchangeTemplate
    {
        /// <summary>
        /// 最小相交角度。中心节点处匝道与被交道路的夹角是所在扇区角度的一半；游戏内实测 55°（125°）是能生成中心节点形状的极限。
        /// 越斜，锐角扇区里的匝道在桥下离中心越远；立柱放在被交道路与匝道之间的空隙里（MainNodes），不受单跨长度限制。
        /// </summary>
        private const float kMinCrossingAngle = 55f;
        private const float kOppositeTolerance = 1f;
        private const float kGradeMargin = 0.98f;
        /// <summary>立柱与匝道之间至少留的距离（米）。</summary>
        private const float kPillarClearance = 1f;
        /// <summary>立柱腿的半宽（米）。</summary>
        private const float kPillarLegHalf = 1.5f;
        /// <summary>地面转弯段的圆弧半径（米）。</summary>
        private const float kTurnRadius = 30f;
        /// <summary>桥面宽于这个值（米）时节点立柱是门式（两条腿在桥面边缘），否则是中心一根。</summary>
        private const float kPortalMinWidth = 10f;
        /// <summary>匝道边缘与立柱腿之间至少留的距离（米）。</summary>
        private const float kLegClearance = 0.5f;

        public InterchangeType Type => InterchangeType.SinglePoint;

        public string DisplayName => "单点立交";

        public int LegCount => 4;

        public IReadOnlyList<string> ValidateParams(InterchangeParams p)
        {
            var errors = p.ValidateLegsAndLevels(LegCount);
            if (errors.Count > 0)
                return errors;

            var legs = p.Legs;
            if (Directions.AngleBetween(legs[0].Angle, legs[2].Angle) < 180f - kOppositeTolerance
                || Directions.AngleBetween(legs[1].Angle, legs[3].Angle) < 180f - kOppositeTolerance)
                errors.Add("单点立交要求主线（臂 0/2）和被交道路（臂 1/3）各自是直线");

            float crossing = Directions.AngleBetween(legs[0].Angle, legs[1].Angle);
            if (crossing < kMinCrossingAngle || crossing > 180f - kMinCrossingAngle)
            {
                errors.Add($"相交角度 {crossing:F0}° 太斜：单点立交需要 {kMinCrossingAngle:F0}°–{180f - kMinCrossingAngle:F0}°（太斜时中心节点处匝道与被交道路夹角过小）");
                return errors;
            }

            if (legs[0].Level != legs[2].Level || legs[1].Level != legs[3].Level)
                errors.Add("同一条道路两端必须在同一层");
            if (!DiamondTemplate.SameRoad(legs[0], legs[2]) || !DiamondTemplate.SameRoad(legs[1], legs[3]))
                errors.Add("同一条道路两端的形式、宽度和分隔带必须一致");

            if (legs[0].Form != RoadForm.Dual)
                errors.Add("单点立交的主线需要是双幅（两条对向单行道路）：请把主线形式改为双幅，或选择单行的公路");
            if (legs[1].Form != RoadForm.TwoWay)
                errors.Add("单点立交的被交道路需要是双向道路（双幅时中心会变成两个节点、两组信号灯）");

            float hMain = p.ElevationOf(legs[0]), hGround = p.ElevationOf(legs[1]);
            float gap = DiamondTemplate.RequiredGap(p);
            if (hMain - hGround < gap)
                errors.Add($"主线比被交道路高 {hMain - hGround:F1} 米，至少需要 {gap:F1} 米净空：主线要在上层");
            else if (LevelPlanner.BandOf(hMain) != ElevationBand.Elevated)
                errors.Add($"主线离地 {hMain:F1} 米会建成路堤（挡土墙），匝道和被交道路无法从桥下穿过：主线需要至少 {HeightRange.BridgeThreshold:F1} 米");

            if (p.RampWidth > legs[0].RoadWidth)
                errors.Add($"匝道宽 {p.RampWidth:F0} 米，比主线（{legs[0].RoadWidth:F0} 米）还宽：请减少匝道车道数");
            if (errors.Count > 0)
                return errors;

            var plan = new Plan(p);
            foreach (int sign in new[] { 1, -1 })
            {
                var nodes = plan.MainNodes(sign);
                if (nodes.Error != null && !errors.Contains(nodes.Error))
                    errors.Add(nodes.Error);
                else if (nodes.Nodes[0] <= plan.MainInnerLo(sign) + 1f || nodes.Nodes[nodes.Nodes.Count - 1] >= plan.MainInnerHi(sign) - 1f)
                    errors.Add("匝道太短，主线在桥下放不下避让节点：请加长匝道");
            }
            return errors;
        }

        public IReadOnlyList<RoadSegmentSpec> Generate(InterchangeParams p)
        {
            var legs = p.Legs;
            var plan = new Plan(p);
            var segments = new List<RoadSegmentSpec>();

            // —— 主线（高架，双幅）：匝道节点之间是桥下跨越段，立柱避开被交道路和匝道 ——
            float mainOuter = plan.Ramps.Max(r => MathF.Abs(Directions.Dot(r.MainNode, plan.MainDir))) + legs[0].StubLength;
            foreach (int sign in new[] { 1, -1 })
            {
                float lateral = sign * plan.Om;
                float inner = DiamondTemplate.ReducedLateral(p, lateral);
                var nodes = new List<Vec3> { plan.MainPoint(-mainOuter, lateral), plan.MainPoint(mainOuter, lateral) };
                nodes.AddRange(plan.MainNodes(sign).Nodes.Select(x => plan.MainPoint(x, inner)));
                nodes.AddRange(plan.Ramps.Where(r => r.Sign == sign).Select(r => r.MainNode));
                DiamondTemplate.AddRoadLine(segments, sign > 0 ? "MainNB" : "MainSB", RoadRole.MainRoad, plan.MainDir, travelPositive: sign > 0,
                    legs[2].RoadPrefab, legs[0].RoadPrefab, "2", "0", nodes,
                    plan.MainInnerLo(sign), plan.MainInnerHi(sign), p.MainReducedPrefab, plan.Side * (inner - lateral));
            }

            // —— 被交道路（地面，双向）：穿过中心节点 ——
            Vec3 cross = legs[1].Direction;
            float crossOuter = plan.Ramps.Max(r => MathF.Abs(Directions.Dot(r.Bottom, cross))) + legs[1].StubLength;
            DiamondTemplate.AddRoadLine(segments, "Cross", RoadRole.CrossRoad, cross, travelPositive: true,
                legs[3].RoadPrefab, legs[1].RoadPrefab, "3", "1", new List<Vec3>
                {
                    cross * -crossOuter + plan.Up * plan.Ground, plan.Center, cross * crossOuter + plan.Up * plan.Ground,
                });

            // —— 匝道 ——
            foreach (var r in plan.Ramps)
            {
                string dir = r.Sign > 0 ? "NB" : "SB";
                if (r.IsOff)
                {
                    segments.Add(new RoadSegmentSpec($"{dir}-off-shift", RoadRole.Ramp, Bezier3.FromTangents(r.MainNode, r.Travel, r.Top, r.Travel, r.ShiftLength / 3f), p.RampPrefab));
                    segments.Add(new RoadSegmentSpec($"{dir}-off-down", RoadRole.Ramp, Bezier3.SmoothSlope(r.Top, r.Bottom), p.RampPrefab));
                    segments.Add(new RoadSegmentSpec($"{dir}-off-turn", RoadRole.Ramp, r.Turn, p.RampPrefab));
                    segments.Add(new RoadSegmentSpec($"{dir}-off-center", RoadRole.Ramp, r.Straight, p.RampPrefab));
                }
                else
                {
                    segments.Add(new RoadSegmentSpec($"{dir}-on-center", RoadRole.Ramp, r.Straight, p.RampPrefab));
                    segments.Add(new RoadSegmentSpec($"{dir}-on-turn", RoadRole.Ramp, r.Turn, p.RampPrefab));
                    segments.Add(new RoadSegmentSpec($"{dir}-on-up", RoadRole.Ramp, Bezier3.SmoothSlope(r.Bottom, r.Top), p.RampPrefab));
                    segments.Add(new RoadSegmentSpec($"{dir}-on-shift", RoadRole.Ramp, Bezier3.FromTangents(r.Top, r.Travel, r.MainNode, r.Travel, r.ShiftLength / 3f), p.RampPrefab));
                }
            }
            // 外部被交道路是双幅时，两端从双向分回双幅
            InterchangeLayout.AddDualTails(segments, p);
            // 原路口改造：高架主线的臂端加坡道降到原有道路的高度
            InterchangeLayout.AddApproachSlopes(segments, p);
            return segments;
        }

        /// <summary>一条匝道的关键点（局部坐标）。</summary>
        private sealed class RampPlan
        {
            /// <summary>+1 = NB（沿主线正方向行驶，位于 Side 一侧），−1 = SB。</summary>
            public int Sign;
            public bool IsOff;
            /// <summary>匝道行驶方向（沿主线）。</summary>
            public Vec3 Travel;
            /// <summary>主线高度上横移段的长度（沿主线）。</summary>
            public float ShiftLength;
            /// <summary>接主线的节点（主线高度）。</summary>
            public Vec3 MainNode;
            /// <summary>横移结束、开始升降处（主线高度，横向 Cl）。</summary>
            public Vec3 Top;
            /// <summary>升降结束、开始转弯处（地面，横向 Cl）。</summary>
            public Vec3 Bottom;
            /// <summary>地面转弯段：半径 kTurnRadius 的圆弧（按行驶方向）。</summary>
            public Bezier3 Turn;
            /// <summary>转弯段与中心节点之间沿角平分线的直线段（按行驶方向）。</summary>
            public Bezier3 Straight;

            public IEnumerable<Bezier3> GroundCurves => new[] { Turn, Straight };
        }

        /// <summary>由参数算出的整体布置。</summary>
        private sealed class Plan
        {
            public readonly InterchangeParams P;
            public readonly Vec3 MainDir, Side, Up = new Vec3(0f, 1f, 0f);
            public readonly float Main, Ground, Om, Cl, MainShift, Climb;
            public readonly Vec3 Center;
            public readonly List<RampPlan> Ramps = new List<RampPlan>();

            public Plan(InterchangeParams p)
            {
                P = p;
                var legs = p.Legs;
                MainDir = legs[0].Direction;
                Side = Directions.RightOf(MainDir) * (p.RightHandTraffic ? 1f : -1f);
                Main = p.ElevationOf(legs[0]);
                Ground = p.ElevationOf(legs[1]);
                Center = Up * Ground;

                float k = MathF.Tan(p.RampDivergeAngle * Directions.DegToRad);
                float s1 = legs[0].RoadWidth / 2f + p.RampWidth / 2f + p.RampSideGap;
                Om = legs[0].CarriagewayOffset;
                Cl = Om + s1;
                MainShift = 2f * s1 / k;
                float rise = MathF.Abs(Main - Ground);
                Climb = Math.Max(1.5f * rise / (p.MaxGrade * kGradeMargin), p.RampLength - MainShift);
                // 桥面立柱腿的外侧占用边界（匝道中心线在它之外就碰不到腿），见 MainNodes
                float deck = p.MainReducedPrefab != null && p.MainReducedWidth > 0f ? p.MainReducedWidth : legs[0].RoadWidth;
                float deckOuter = Om + deck / 2f + p.RampWidth / 2f + kLegClearance;

                Vec3 cross = legs[1].Direction;
                Vec3 crossPos = Directions.Dot(cross, Side) > 0f ? cross : -cross;   // 被交道路指向 NB 一侧的方向
                foreach (int sign in new[] { 1, -1 })
                {
                    foreach (bool isOff in new[] { true, false })
                    {
                        // 匝道所在象限：沿主线在路口前（下匝道）或后（上匝道），横向在本幅一侧
                        float along = isOff ? -sign : sign;
                        Vec3 travel = MainDir * sign;
                        Vec3 legDir = Vec3.Normalize(MainDir * along + crossPos * sign);   // 中心节点指向象限内的角平分线
                        Vec3 lateralDir = Side * sign;
                        // 在主线方向和角平分线方向之间用半径 R 的圆弧转弯，之后沿角平分线直线进出中心节点。
                        // 圆弧要在桥面（立柱腿）范围之外转完，桥下只有一段直线斜穿主线，占用主线的长度最短
                        // （游戏内实测：缓弯时匝道贴着立柱腿走很长一段，桥下放不下立柱）。所以升降段的横向位置取
                        // max(Cl, 桥面外缘 + 圆弧的横向落差)，交点 X 是角平分线到达这个横向位置处
                        Vec3 toCenter = isOff ? -legDir : legDir;   // 直线段的行驶方向
                        float turnAngle = (float)Math.Acos(Math.Min(1f, Math.Abs(Directions.Dot(travel, toCenter))));
                        float rampLateral = Math.Max(Cl, deckOuter + kTurnRadius * (1f - MathF.Cos(turnAngle)));
                        float rampShift = rampLateral - Om;
                        float shiftLength = 2f * rampShift / k;
                        float u = rampLateral / Directions.Dot(legDir, lateralDir);
                        Vec3 x = legDir * u + Up * Ground;
                        float radius = Math.Min(kTurnRadius, 0.8f * u / MathF.Tan(turnAngle / 2f));
                        float tangentLength = radius * MathF.Tan(turnAngle / 2f);
                        Vec3 bottom = isOff ? x - travel * tangentLength : x + travel * tangentLength;
                        Vec3 arcEnd = x - legDir * tangentLength;   // 圆弧与直线段的切点
                        float handle = radius * 4f / 3f * MathF.Tan(turnAngle / 4f);
                        Vec3 top = bottom + travel * (isOff ? -Climb : Climb) + Up * (Main - Ground);
                        Vec3 mainNode = top + travel * (isOff ? -shiftLength : shiftLength) - lateralDir * rampShift;
                        var turn = isOff
                            ? Bezier3.FromTangents(bottom, travel, arcEnd, toCenter, handle)
                            : Bezier3.FromTangents(arcEnd, toCenter, bottom, travel, handle);
                        var straight = isOff ? Bezier3.Line(arcEnd, Center) : Bezier3.Line(Center, arcEnd);
                        Ramps.Add(new RampPlan { Sign = sign, IsOff = isOff, Travel = travel, ShiftLength = shiftLength, MainNode = mainNode, Top = top, Bottom = bottom, Turn = turn, Straight = straight });
                    }
                }
            }

            public Vec3 MainPoint(float along, float lateral) => MainDir * along + Side * lateral + Up * Main;

            private float Along(Vec3 v) => Directions.Dot(v, MainDir);

            /// <summary>这一幅主线上匝道节点之间（减车道中段）沿主线的范围。</summary>
            public float MainInnerLo(int sign) => Ramps.Where(r => r.Sign == sign).Min(r => Along(r.MainNode));
            public float MainInnerHi(int sign) => Ramps.Where(r => r.Sign == sign).Max(r => Along(r.MainNode));

            /// <summary>
            /// 这一幅主线在桥下的节点：被交道路和匝道转弯段在主线立柱线上各占一段，
            /// 立柱可以放在它们之间的空隙里（见 PillarPlanner.PlanNodes）。
            /// 立柱横跨整幅桥面，匝道离主线中心线横向不到“匝道半宽 + 桥面半宽 + 间距”的地方算占用。
            /// </summary>
            public PillarPlanner.NodePlan MainNodes(int sign)
            {
                var legs = P.Legs;
                float lateral = DiamondTemplate.ReducedLateral(P, sign * Om);
                Vec3 cross = legs[1].Direction;
                float sin = DiamondTemplate.Cross2(MainDir, cross);
                // 被交道路中心线与这条主线线的交点（斜交时不在 0）
                float center = -lateral * DiamondTemplate.Cross2(Side, cross) / sin;
                float half = legs[1].HalfExtent / MathF.Abs(sin);
                var occupied = new List<(float Lo, float Hi)> { (center - half, center + half) };

                // 节点立柱：宽道路多是门式立柱（Horizontal Pillar，横梁在头顶，两条腿在桥面两侧边缘），
                // 窄道路是中心一根（Vertical Pillar）。横梁下面可以过车，只有腿和中心柱要避开；
                // 游戏内实测：只按中心柱算时匝道撞上桥面边缘的腿。桥下这段多为减车道后的道路，按它的宽度算腿的位置
                float deck = P.MainReducedPrefab != null && P.MainReducedWidth > 0f ? P.MainReducedWidth : legs[0].RoadWidth;
                // 腿在桥面边缘以内（外侧面与桥面边缘齐平）
                float edgeLeg = deck / 2f - kPillarLegHalf;
                // 宽道路（游戏内实测 12、20 米公路用 Horizontal Pillar 12/20）是门式：横梁 + 两侧各一条腿，中间没有柱；
                // 窄道路是中心一根（游戏按宽度在立柱组里挑选，见 SubObjectSystem）
                float[] legLines = deck >= kPortalMinWidth ? new[] { lateral - edgeLeg, lateral + edgeLeg } : new[] { lateral };
                float reach = P.RampWidth / 2f + kPillarLegHalf + kLegClearance;
                const int samples = 128;
                foreach (Bezier3 curve in Ramps.Where(r => r.Sign == sign).SelectMany(r => r.GroundCurves))
                {
                    for (int i = 0; i <= samples; i++)
                    {
                        Vec3 v = curve.Evaluate(i / (float)samples);
                        // 腿中心离匝道中心线不到 reach 就算撞上：对每条腿线，占用沿主线 ±√(reach² − 横向差²) 的范围
                        float across = Directions.Dot(v, Side);
                        float along = Along(v);
                        foreach (float line in legLines)
                        {
                            float dy = across - line;
                            if (MathF.Abs(dy) < reach)
                            {
                                float w = MathF.Sqrt(reach * reach - dy * dy);
                                occupied.Add((along - w, along + w));
                            }
                        }
                    }
                }
                // 占用区间已含立柱腿和匝道的尺寸，节点只需留采样间隔的余量
                return PillarPlanner.PlanNodes(occupied, legs[0].MiddlePillarSpacing, legs[0].PillarWidth, legs[0].MaxElevatedEdgeLength,
                    nodeClearance: 0.5f);
            }
        }
    }
}
