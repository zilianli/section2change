using System;
using System.Collections.Generic;
using System.Linq;
using InterchangeMod.Generation.Geometry;

namespace InterchangeMod.Generation.Templates
{
    /// <summary>
    /// 菱形立交（四向）。主线沿汇入臂 0/2，被交道路沿臂 1/3，两者在不同层（可地上地下混合）。
    /// 主线和被交道路各自可以是双向道路或双幅（两条单行道路对向并行）。
    ///
    /// 俯视图（右侧通行，臂 0 朝上，臂 1 朝右）：
    ///            主线 (臂 0)
    ///   SB-off ╲  │  ╱ NB-on
    ///  ─────────●─┼─●───────── 被交道路 (臂 1 →)
    ///   SB-on  ╱  │  ╲ NB-off
    /// NB = 沿臂 2→臂 0 方向行驶；匝道总在行驶方向的右侧（左侧通行时为左侧）。
    ///
    /// 匝道 = 分流段（主线高度，横移到不和主线重叠）+ 爬坡段（升降到被交道路）。
    /// 双幅主线：匝道从本幅节点分出/汇入，形成真正的分流/合流。
    /// 双幅被交道路：驶出匝道止于近侧那幅（右转），一段单行连接线（NB-link/SB-link）
    /// 穿过分隔带到远侧那幅（左转），驶入匝道从远侧那幅出发。
    ///
    /// 跨越处：两条道路在交叉点两侧、对方占地之外各放一个节点。游戏会把过长的路段等分并在切点放节点/立柱
    /// （高架道路每段最长 80 米），不加这两个节点时切点正好落在交叉点，立柱压在下层道路上（报“与现有物体重叠”）。
    /// </summary>
    public sealed class DiamondTemplate : IInterchangeTemplate
    {
        /// <summary>
        /// 最小相交角度。匝道末端转一半角度接入被交道路（见 Approach），
        /// 再斜则匝道转弯过急、两侧匝道接口挤在一起。游戏内实测：匝道平行到达时 60° 以下报“形状无效/重叠”。
        /// </summary>
        private const float kMinCrossingAngle = 45f;
        private const float kOppositeTolerance = 1f;
        /// <summary>跨越节点（立柱）离对方道路外缘的距离。</summary>
        private const float kPillarMargin = 3f;
        /// <summary>节点之间至少留出的距离。</summary>
        private const float kNodeSpacing = 1f;
        /// <summary>过渡段起点离最外侧匝道节点的距离。</summary>
        private const float kTransitionGap = 10f;
        /// <summary>
        /// 两幅平行接入外部双向道路（AddAlignedEnd）时，接入节点离最外的匝道节点的距离（米）：两个节点都接着宽路，
        /// 离得太近节点区相互重叠（斜向汇合的汇合点本来就在更外面，用 kTransitionGap）。
        /// </summary>
        private const float kAlignedEndGap = 40f;

        /// <summary>臂端过渡的起点（离中心）：inner 是最外的匝道节点 / 接口；平行接入时多留 kAlignedEndGap。</summary>
        private static float TransitionStart(LegSpec leg, float inner, float offset) =>
            inner + (FitsAligned(leg, offset) ? kAlignedEndGap : kTransitionGap);
        /// <summary>过渡段汇合点之外双向道路至少延伸的长度。</summary>
        private const float kTransitionTail = 20f;

        public InterchangeType Type => InterchangeType.Diamond;

        public string DisplayName => "菱形立交";

        public int LegCount => 4;

        public IReadOnlyList<string> ValidateParams(InterchangeParams p)
        {
            var errors = p.ValidateLegsAndLevels(LegCount);
            if (errors.Count > 0)
                return errors;

            var legs = p.Legs;
            if (Directions.AngleBetween(legs[0].Angle, legs[2].Angle) < 180f - kOppositeTolerance
                || Directions.AngleBetween(legs[1].Angle, legs[3].Angle) < 180f - kOppositeTolerance)
                errors.Add("菱形立交要求主线（臂 0/2）和被交道路（臂 1/3）各自是直线");

            float crossing = Directions.AngleBetween(legs[0].Angle, legs[1].Angle);
            if (crossing < kMinCrossingAngle || crossing > 180f - kMinCrossingAngle)
            {
                errors.Add($"相交角度 {crossing:F0}° 太斜：菱形立交需要 {kMinCrossingAngle:F0}°–{180f - kMinCrossingAngle:F0}°（太斜时匝道与被交道路的路口无法成形）");
                return errors;
            }

            if (legs[0].Level != legs[2].Level || legs[1].Level != legs[3].Level)
                errors.Add("同一条道路两端必须在同一层");

            if (!SameRoad(legs[0], legs[2]) || !SameRoad(legs[1], legs[3]))
                errors.Add("同一条道路两端的形式、宽度和分隔带必须一致");

            foreach (var leg in legs)
            {
                if (leg.Form == RoadForm.TwoWay && leg.ExternalForm == RoadForm.Dual)
                {
                    errors.Add("外部道路是双幅、立交内部是双向：暂不支持从双幅收拢成双向，请把立交内部也设为双幅");
                    break;
                }
                if (leg.NeedsDualToTwoWayTransition && string.IsNullOrEmpty(leg.ExternalPrefab))
                {
                    errors.Add("臂端需要从双幅过渡到双向，但没有指定外部双向道路");
                    break;
                }
            }

            float hMain = p.ElevationOf(legs[0]);
            float rise = System.Math.Abs(p.ElevationOf(legs[1]) - hMain);
            float taper = TaperLength(p);
            float climb = p.RampLength - taper;
            float neededGap = RequiredGap(p);
            if (climb <= 0f)
                errors.Add($"匝道太短：分流段就需要 {taper:F0} 米（分流角度越小分流段越长）");
            else if (rise < neededGap - HeightRange.GapTolerance)
                errors.Add($"主线和被交道路高差 {rise:F1} 米，这两种道路之间至少需要 {neededGap:F1} 米净空");
            else if (climb < rise / p.MaxGrade)
                errors.Add($"匝道长度至少需要 {rise / p.MaxGrade + taper:F0} 米（含 {taper:F0} 米分流段）才能满足坡度限制");

            // 双向道路的节点在路中心：只有在地面上，匝道从路中心分出时游戏才能生成路口形状
            if (legs[0].Form == RoadForm.TwoWay && LevelPlanner.BandOf(hMain) is not ElevationBand.Ground)
                errors.Add("双向主线在高架、路堤、下沉或隧道中时，匝道无法从路中心分出（游戏报“形状无效”）：请把主线形式改为双幅，或把主线放在地面");

            // 上层在 4 ~ 8.5 米会被建成实心路堤（带挡土墙），挡住下层道路
            float hUpper = System.Math.Max(hMain, p.ElevationOf(legs[1]));
            if (LevelPlanner.BandOf(hUpper) == ElevationBand.Raised)
                errors.Add($"上层离地 {hUpper:F1} 米会建成路堤（挡土墙），下层道路无法穿过：上层需要至少 {HeightRange.BridgeThreshold:F1} 米才是高架桥");

            // 匝道比它分出的那条路还宽时，游戏无法生成分流节点的形状（报“形状无效”）
            if (p.RampWidth > legs[0].RoadWidth)
                errors.Add($"匝道宽 {p.RampWidth:F0} 米，比主线（{legs[0].RoadWidth:F0} 米）还宽，分流处无法成形：请减少匝道车道数");

            // 匝道接口离主线的垂直距离（RampTerminalOffset）要超出主线外缘，否则匝道压在主线上
            float sin = MathF.Sin(crossing * Directions.DegToRad);
            float needed = legs[0].HalfExtent + p.RampWidth / 2f + p.RampSideGap;
            if (p.RampTerminalOffset < needed)
                errors.Add($"匝道接口离主线太近：需要至少 {needed:F0} 米的接口偏移");

            // 跨越段：上层的立柱要避开下层道路；跨越段两端必须落在匝道节点之间
            float cot = MathF.Abs(MathF.Cos(crossing * Directions.DegToRad)) / sin;
            foreach (float lateral in Laterals(legs[0]))
            {
                var span = LineSpan(p, onMain: true, ReducedLateral(p, lateral));
                if (span.Error != null && !errors.Contains(span.Error))
                    errors.Add(span.Error);
                else if (span.Lo <= -p.RampLength + kNodeSpacing || span.Hi >= p.RampLength - kNodeSpacing)
                    errors.Add("匝道太短，主线在跨越处放不下避让节点：请加长匝道");
            }
            // 接口沿被交道路在 ±RampTerminalOffset / sin
            float terminalAlong = TerminalAlong(p);
            foreach (float lateral in Laterals(legs[1]))
            {
                var span = LineSpan(p, onMain: false, lateral);
                if (span.Error != null && !errors.Contains(span.Error))
                    errors.Add(span.Error);
                else if (span.Lo <= -terminalAlong + kNodeSpacing || span.Hi >= terminalAlong - kNodeSpacing)
                    errors.Add("匝道接口离交叉点太近，被交道路在跨越处放不下避让节点");
            }
            return errors;
        }

        public IReadOnlyList<RoadSegmentSpec> Generate(InterchangeParams p)
        {
            var legs = p.Legs;
            float hMain = p.ElevationOf(legs[0]);
            float hCross = p.ElevationOf(legs[1]);
            float l = p.RampLength;
            float sign = p.RightHandTraffic ? 1f : -1f;
            var up = new Vec3(0f, 1f, 0f);
            var segments = new List<RoadSegmentSpec>();

            Vec3 main = legs[0].Direction;
            Vec3 mainSide = Directions.RightOf(main) * sign;   // NB 那幅和 NB 匝道所在一侧
            Vec3 cross = legs[1].Direction;
            Vec3 crossSide = Directions.RightOf(cross) * sign;  // 沿 +cross 行驶的那幅所在一侧
            Vec3 sideCross = Directions.Dot(cross, mainSide) > 0f ? cross : -cross;
            float om = legs[0].CarriagewayOffset;
            float oc = legs[1].CarriagewayOffset;
            Vec3 MainPoint(float along, float lateral) => main * along + mainSide * lateral + up * hMain;
            Vec3 CrossPoint(float along, float lateral) => cross * along + crossSide * lateral + up * hCross;

            // —— 匝道与主线、被交道路的接点（所有共享节点都从这里取同一个 Vec3）——
            Vec3 nbBack = MainPoint(-l, om);
            Vec3 nbFront = MainPoint(l, om);
            Vec3 sbFront = MainPoint(l, -om);
            Vec3 sbBack = MainPoint(-l, -om);
            // NB 匝道接口在 sideCross 一侧，SB 在另一侧；双幅时拆成近侧/远侧两个点
            // 匝道末端方向取“平行主线”和“垂直被交道路”的角平分线：斜交时匝道只转一半，
            // 和被交道路的夹角至少 67.5°（平行到达时斜交过大，游戏报“形状无效/重叠”），又不会急转成 S 形
            Vec3 nbApproach = Approach(main, cross);
            Vec3 sbApproach = -nbApproach;
            // 接口离主线的垂直距离固定为 d（沿被交道路是 d / sin），斜交时匝道也不会贴近主线
            float dc = TerminalAlong(p);
            (Vec3 nbNear, Vec3 nbFar) = Terminal(sideCross * dc + up * hCross, nbApproach, crossSide, oc);
            (Vec3 sbNear, Vec3 sbFar) = Terminal(-sideCross * dc + up * hCross, sbApproach, crossSide, oc);

            // —— 主线 ——
            // 臂端外部是双向道路时，两幅在过渡起点结束，之后平行接入（AddAlignedEnd）或斜向汇合（AddTransition）
            float back = legs[2].NeedsDualToTwoWayTransition ? TransitionStart(legs[2], l, om) : l + legs[2].StubLength;
            float front = legs[0].NeedsDualToTwoWayTransition ? TransitionStart(legs[0], l, om) : l + legs[0].StubLength;
            string? prefab0 = legs[0].RoadPrefab, prefab2 = legs[2].RoadPrefab;
            if (legs[0].Form == RoadForm.Dual)
            {
                // 车道平衡：两幅主线在匝道分出点和汇入点之间（±l）改用车道较少的道路
                AddRoadLine(segments, "MainNB", RoadRole.MainRoad, main, travelPositive: true, prefab2, prefab0, "2", "0",
                    MainLineNodes(om, nbBack, nbFront), -l, l, p.MainReducedPrefab, mainSide * (ReducedLateral(p, om) - om));
                AddRoadLine(segments, "MainSB", RoadRole.MainRoad, main, travelPositive: false, prefab2, prefab0, "2", "0",
                    MainLineNodes(-om, sbBack, sbFront), -l, l, p.MainReducedPrefab, mainSide * (ReducedLateral(p, -om) + om));
            }
            else
            {
                AddRoadLine(segments, "Main", RoadRole.MainRoad, main, travelPositive: true, prefab2, prefab0, "2", "0",
                    MainLineNodes(0f, nbBack, nbFront));
            }

            List<Vec3> MainLineNodes(float lateral, Vec3 rampBack, Vec3 rampFront)
            {
                // 匝道之间的中段减车道时按内侧（直行车道）对齐：中段节点放在平移后的位置，两端在匝道节点处边缘对齐（见 AddRoadLine）
                float inner = ReducedLateral(p, lateral);
                var span = LineSpan(p, onMain: true, inner);
                return new List<Vec3> { MainPoint(-back, lateral), rampBack, MainPoint(span.Lo, inner), MainPoint(span.Hi, inner), rampFront, MainPoint(front, lateral) };
            }

            AddEnd(legs[2], "Main-2", RoadRole.MainRoad, MainPoint, -1, back, om, l, prefab2);
            AddEnd(legs[0], "Main-0", RoadRole.MainRoad, MainPoint, +1, front, om, l, prefab0);

            // 臂端外部是双向道路：两幅放得进它的宽度时平行接入，否则斜向汇合
            void AddEnd(LegSpec leg, string name, RoadRole role, Func<float, float, Vec3> point, int end, float start, float offset, float inner, string? oneWayPrefab)
            {
                if (!leg.NeedsDualToTwoWayTransition)
                    return;
                if (FitsAligned(leg, offset))
                    AddAlignedEnd(segments, name, role, point, end * start, offset, leg.StubLength, leg.ExternalPrefab);
                else
                    AddTransition(segments, name, role, point, end, start, offset, inner + leg.StubLength, p.LegTransitionAngle, oneWayPrefab, leg.ExternalPrefab);
            }

            // —— 被交道路 ——
            // 双幅被交道路上的匝道接口沿道路方向会偏出 dc 一点（匝道斜着穿过分隔带），过渡段和臂端从最外的接口算起
            float reach = new[] { nbNear, nbFar, sbNear, sbFar }.Max(v => MathF.Abs(Directions.Dot(v, cross)));
            float outer3 = legs[3].NeedsDualToTwoWayTransition ? TransitionStart(legs[3], reach, oc) : reach + legs[3].StubLength;
            float outer1 = legs[1].NeedsDualToTwoWayTransition ? TransitionStart(legs[1], reach, oc) : reach + legs[1].StubLength;
            string? prefab1 = legs[1].RoadPrefab, prefab3 = legs[3].RoadPrefab;
            if (legs[1].Form == RoadForm.Dual)
            {
                // 每幅上有两个接口点（NB 侧一个、SB 侧一个）
                AddRoadLine(segments, "CrossPos", RoadRole.CrossRoad, cross, travelPositive: true, prefab3, prefab1, "3", "1",
                    CrossLineNodes(oc, PickSide(crossSide, true, nbNear, nbFar), PickSide(crossSide, true, sbNear, sbFar)));
                AddRoadLine(segments, "CrossNeg", RoadRole.CrossRoad, cross, travelPositive: false, prefab3, prefab1, "3", "1",
                    CrossLineNodes(-oc, PickSide(crossSide, false, nbNear, nbFar), PickSide(crossSide, false, sbNear, sbFar)));

                segments.Add(new RoadSegmentSpec("NB-link", RoadRole.Ramp, Bezier3.Line(nbNear, nbFar), p.RampPrefab));
                segments.Add(new RoadSegmentSpec("SB-link", RoadRole.Ramp, Bezier3.Line(sbNear, sbFar), p.RampPrefab));
            }
            else
            {
                // 双向时两侧接口就是 ±cross·d（与 nbNear/sbNear 是同一个点）
                AddRoadLine(segments, "Cross", RoadRole.CrossRoad, cross, travelPositive: true, prefab3, prefab1, "3", "1",
                    CrossLineNodes(0f, nbNear, sbNear));
            }

            List<Vec3> CrossLineNodes(float lateral, Vec3 terminalA, Vec3 terminalB)
            {
                var span = LineSpan(p, onMain: false, lateral);
                return new List<Vec3> { CrossPoint(-outer3, lateral), terminalA, terminalB, CrossPoint(span.Lo, lateral), CrossPoint(span.Hi, lateral), CrossPoint(outer1, lateral) };
            }

            AddEnd(legs[3], "Cross-3", RoadRole.CrossRoad, CrossPoint, -1, outer3, oc, reach, prefab3);
            AddEnd(legs[1], "Cross-1", RoadRole.CrossRoad, CrossPoint, +1, outer1, oc, reach, prefab1);

            // —— 匝道（单向，曲线方向即行驶方向）——
            // 每条匝道分两段，处处切线连续（不出现折角）：
            //   分流/合流段（*-diverge / *-merge）：在主线高度上的一段曲线，起点与主线相切（外切曲线），
            //     逐渐转到 RampDivergeAngle，直到与主线完全分开（clear 点，再留 RampSideGap）。
            //     游戏的分流节点会延伸到两条路边缘不再重叠处，这一段保持主线高度，节点之后匝道已经不压主线；
            //   爬坡段（*-off / *-on）：从 clear 点沿同一角度出发，转向被交道路，升降到被交道路。
            float k = MathF.Tan(p.RampDivergeAngle * Directions.DegToRad);
            float taper = TaperLength(p);
            float climbHandle = (l - taper) / 3f;
            float divergeHandle = taper / 3f;
            float clearLateral = om + DivergeSeparation(p);
            if (MainElevated(p) && legs[0].Form == RoadForm.Dual)
            {
                // 高架主线：分流段就是一段圆弧（见 AddElevatedRamp），横移到不再压着主线处，之后的爬坡段沿圆弧终点的方向出发
                var (angle, _) = ElevatedArc(RampClear(p) - RampAlign(p));
                k = MathF.Tan(angle);
                divergeHandle = kElevatedDivergeRadius * 4f / 3f * MathF.Tan(angle / 4f);
                clearLateral = om + RampClear(p);
            }
            Vec3 sbSide = -mainSide;
            Vec3 nbOut = main + mainSide * k, nbIn = main - mainSide * k;
            Vec3 sbOut = -main + sbSide * k, sbIn = -main - sbSide * k;

            Vec3 nbOffClear = MainPoint(-l + taper, clearLateral);
            Vec3 nbOnClear = MainPoint(l - taper, clearLateral);
            Vec3 sbOffClear = MainPoint(l - taper, -clearLateral);
            Vec3 sbOnClear = MainPoint(-l + taper, -clearLateral);

            AddElevatedRamp(segments, p, "NB-diverge", Bezier3.FromTangents(nbBack, main, nbOffClear, nbOut, divergeHandle), nodeAtEnd: false);
            segments.Add(new RoadSegmentSpec("NB-off", RoadRole.Ramp,
                Bezier3.FromTangents(nbOffClear, nbOut, nbNear, nbApproach, climbHandle), p.RampPrefab));
            segments.Add(new RoadSegmentSpec("NB-on", RoadRole.Ramp,
                Bezier3.FromTangents(nbFar, nbApproach, nbOnClear, nbIn, climbHandle), p.RampPrefab));
            AddElevatedRamp(segments, p, "NB-merge", Bezier3.FromTangents(nbOnClear, nbIn, nbFront, main, divergeHandle), nodeAtEnd: true);

            AddElevatedRamp(segments, p, "SB-diverge", Bezier3.FromTangents(sbFront, -main, sbOffClear, sbOut, divergeHandle), nodeAtEnd: false);
            segments.Add(new RoadSegmentSpec("SB-off", RoadRole.Ramp,
                Bezier3.FromTangents(sbOffClear, sbOut, sbNear, sbApproach, climbHandle), p.RampPrefab));
            segments.Add(new RoadSegmentSpec("SB-on", RoadRole.Ramp,
                Bezier3.FromTangents(sbFar, sbApproach, sbOnClear, sbIn, climbHandle), p.RampPrefab));
            AddElevatedRamp(segments, p, "SB-merge", Bezier3.FromTangents(sbOnClear, sbIn, sbBack, -main, divergeHandle), nodeAtEnd: true);

            // 原路口改造：臂端升降到外部道路的高度，好接回原有道路
            InterchangeLayout.AddApproachSlopes(segments, p);
            return segments;
        }

        /// <summary>
        /// 分流段沿主线方向的长度：曲线从与主线相切逐渐转到 RampDivergeAngle，平均角度约一半，
        /// 横向分开（主线半宽 + 匝道半宽 + 间距）需要的距离约为同角度直线的两倍。角度越小越长。
        /// 主线是高架（双幅）时，分流段就是 AddElevatedRamp 里那段圆弧。
        /// </summary>
        public static float TaperLength(InterchangeParams p)
        {
            float separation = DivergeSeparation(p);
            float k = MathF.Tan(p.RampDivergeAngle * Directions.DegToRad);
            float taper = 2f * separation / k;
            if (!MainElevated(p) || p.Legs[0].Form != RoadForm.Dual)
                return taper;
            return ElevatedArc(RampClear(p) - RampAlign(p)).Along;
        }

        /// <summary>匝道中心线在分流段末端离主线（本幅）中心线的横向距离：主线半宽 + 匝道半宽 + 间距。</summary>
        private static float DivergeSeparation(InterchangeParams p) =>
            p.Legs[0].RoadWidth / 2f + p.RampWidth / 2f + p.RampSideGap;

        /// <summary>匝道接口中心沿被交道路离交叉点的距离：保证离主线的垂直距离是 RampTerminalOffset。</summary>
        private static float TerminalAlong(InterchangeParams p) =>
            p.RampTerminalOffset / MathF.Abs(Cross2(p.Legs[0].Direction, p.Legs[1].Direction));

        /// <summary>
        /// NB 匝道在接口处的行驶方向：主线方向和被交道路法向（与主线同向的那个）的角平分线。
        /// 正交时就是主线方向；SB 匝道取反方向。
        /// </summary>
        public static Vec3 Approach(Vec3 main, Vec3 cross)
        {
            Vec3 normal = Directions.RightOf(cross);
            if (Directions.Dot(normal, main) < 0f)
                normal = -normal;
            return Vec3.Normalize(main + normal);
        }

        /// <summary>主线与被交道路路面之间需要的最小高差：取各自在所在高度下的竖向范围计算（与游戏净空检查一致）。</summary>
        internal static float RequiredGap(InterchangeParams p)
        {
            LegSpec main = p.Legs[0], cross = p.Legs[1];
            float hMain = p.ElevationOf(main), hCross = p.ElevationOf(cross);
            return hMain <= hCross
                ? HeightRange.RequiredGap(main.HeightAt(hMain), cross.HeightAt(hCross))
                : HeightRange.RequiredGap(cross.HeightAt(hCross), main.HeightAt(hMain));
        }

        internal static bool SameRoad(LegSpec a, LegSpec b) =>
            a.Form == b.Form && a.RoadWidth == b.RoadWidth && (a.Form == RoadForm.TwoWay || a.MedianWidth == b.MedianWidth);

        /// <summary>
        /// 臂端过渡段（内部双幅 → 外部双向）：两幅在 start 处结束，各自以 angle 斜向中心线，
        /// 汇合到中心线上的一个节点，再以外部的双向道路延伸到外端（至少 outerMin，并留 kTransitionTail）。
        /// end = +1 表示道路正方向那一端，−1 表示负方向那一端；point(沿道路距离, 横向偏移) 给出坐标。
        /// 横向 +offset 那幅沿道路正方向行驶（与 AddRoadLine 的约定一致）。
        /// </summary>
        internal static void AddTransition(List<RoadSegmentSpec> segments, string name, RoadRole role, Func<float, float, Vec3> point,
            int end, float start, float offset, float outerMin, float angle, string? oneWayPrefab, string? twoWayPrefab)
        {
            float mergeAt = start + offset / MathF.Tan(angle * Directions.DegToRad);
            float outer = Math.Max(outerMin, mergeAt + kTransitionTail);
            Vec3 merge = point(end * mergeAt, 0f);
            Vec3 plus = point(end * start, offset);    // 沿正方向行驶的那幅
            Vec3 minus = point(end * start, -offset);  // 沿负方向行驶的那幅

            if (end > 0)
            {
                // 正方向端：正向那幅驶出去汇入，负向那幅从汇合点分出驶回来
                segments.Add(new RoadSegmentSpec($"{name}-merge+", role, Bezier3.Line(plus, merge), oneWayPrefab));
                segments.Add(new RoadSegmentSpec($"{name}-split-", role, Bezier3.Line(merge, minus), oneWayPrefab));
                segments.Add(new RoadSegmentSpec($"{name}-tail", role, Bezier3.Line(merge, point(outer, 0f)), twoWayPrefab));
            }
            else
            {
                segments.Add(new RoadSegmentSpec($"{name}-split+", role, Bezier3.Line(merge, plus), oneWayPrefab));
                segments.Add(new RoadSegmentSpec($"{name}-merge-", role, Bezier3.Line(minus, merge), oneWayPrefab));
                segments.Add(new RoadSegmentSpec($"{name}-tail", role, Bezier3.Line(point(-outer, 0f), merge), twoWayPrefab));
            }
        }

        /// <summary>
        /// 匝道接主线的一段（曲线在主线节点处与主线相切，nodeAtEnd = true 表示主线节点在曲线终点 = 汇入）：
        /// 匝道就是主线外侧分出去（汇进来）的那几条车道，曲线端点向匝道一侧横移 RampAlign，外侧边缘和主线对齐，节点仍在主线中心线上。
        /// 主线是高架时，匝道从节点起先走一段半径 kElevatedDivergeRadius 的圆弧（与主线相切），横移到 RampClear 后不再压着主线，
        /// 在那里分段（接主线节点的那段叫 name，另一段叫 name-clear），之后再平顺地转回原来的终点方向：游戏内匝道贴着高架主线
        /// 缓缓分出时，分流节点后约 10 米报形状无效（节点横移与否都一样）；早先 15° 折出、喇叭形用 80 米圆弧分出时都能放置。
        /// </summary>
        internal static void AddElevatedRamp(List<RoadSegmentSpec> segments, InterchangeParams p, string name, Bezier3 curve, bool nodeAtEnd)
        {
            Vec3 node = nodeAtEnd ? curve.P3 : curve.P0, far = nodeAtEnd ? curve.P0 : curve.P3;
            Vec3 tangent = curve.Derivative(nodeAtEnd ? 1f : 0f);
            Vec3 side = Directions.RightOf(Vec3.Normalize(new Vec3(tangent.X, 0f, tangent.Z)));
            if (Directions.Dot(far - node, side) < 0f)
                side = -side;
            Vec3 shift = side * RampAlign(p);
            curve = nodeAtEnd
                ? new Bezier3(curve.P0, curve.P1, curve.P2 + shift, curve.P3 + shift)
                : new Bezier3(curve.P0 + shift, curve.P1 + shift, curve.P2, curve.P3);
            var pieces = MainElevated(p)
                ? ElevatedDiverge(curve, nodeAtEnd, RampClear(p) - RampAlign(p))
                    ?? InterchangeLayout.SplitAtClear(curve, nodeAtEnd, RampClear(p), p.Legs[0].MaxElevatedEdgeLength)
                : new List<Bezier3> { curve };
            for (int i = 0; i < pieces.Count; i++)
            {
                bool atNode = nodeAtEnd ? i == pieces.Count - 1 : i == 0;
                segments.Add(atNode
                    ? new RoadSegmentSpec(name, RoadRole.Ramp, pieces[i], p.RampPrefab, nodeAtEnd ? (Vec3?)null : node, nodeAtEnd ? node : (Vec3?)null)
                    : new RoadSegmentSpec($"{name}-clear", RoadRole.Ramp, pieces[i], p.RampPrefab));
            }
        }

        /// <summary>高架主线上匝道分出 / 汇入时，靠主线节点那段圆弧的半径（米），与喇叭形相同。</summary>
        internal const float kElevatedDivergeRadius = 80f;

        /// <summary>从与主线相切出发、以 kElevatedDivergeRadius 横移 rise 的圆弧：转角（弧度）和沿主线的长度。</summary>
        internal static (float Angle, float Along) ElevatedArc(float rise)
        {
            float angle = (float)Math.Acos(Math.Max(-1f, 1f - rise / kElevatedDivergeRadius));
            return (angle, kElevatedDivergeRadius * MathF.Sin(angle));
        }

        /// <summary>
        /// 把匝道曲线（起点在主线节点处、与主线相切；nodeAtEnd 时反过来）换成：圆弧横移 rise（到不再压着主线处），
        /// 再接一段曲线到原来的终点、终点方向不变。原曲线本身就只横移 rise 时（菱形：分流段就是这段圆弧）只有圆弧。
        /// 横向走不到 rise、或剩下的长度不够转回来时返回 null。
        /// </summary>
        private static List<Bezier3>? ElevatedDiverge(Bezier3 curve, bool nodeAtEnd, float rise)
        {
            var c = nodeAtEnd ? curve.Reverse() : curve;
            Vec3 start = c.P0, far = c.P3;
            Vec3 travel = Flat(c.Derivative(0f)), farDir = Flat(c.Derivative(1f));
            Vec3 side = Directions.RightOf(travel);
            float farLateral = Directions.Dot(far - start, side);
            if (farLateral < 0f)
            {
                side = -side;
                farLateral = -farLateral;
            }
            if (rise < 0.1f || farLateral < rise - 0.01f)
                return null;
            var (angle, along) = ElevatedArc(rise);
            Vec3 heading = travel * MathF.Cos(angle) + side * MathF.Sin(angle);
            Vec3 arcEnd = start + travel * along + side * rise;
            arcEnd.Y = start.Y;
            var arc = Bezier3.FromTangents(start, travel, arcEnd, heading, kElevatedDivergeRadius * 4f / 3f * MathF.Tan(angle / 4f));
            var pieces = new List<Bezier3> { arc };
            float lateralLeft = farLateral - rise, alongLeft = Directions.Dot(far - start, travel) - along;
            if (lateralLeft > 0.05f || alongLeft > 1f)
            {
                if (alongLeft < 3f * lateralLeft || alongLeft < kMinRestLength)
                    return null;
                // 剩下一段：从圆弧终点沿圆弧方向出发，转回原来的终点方向；起点控制柄不让横向冲过终点
                float length = Vec3.Distance(arcEnd, far);
                float h0 = Math.Max(length / 10f, Math.Min(length / 3f, 0.9f * lateralLeft / MathF.Sin(angle)));
                Vec3 p1 = arcEnd + heading * h0, p2 = far - farDir * (length / 3f);
                p1.Y = start.Y + (far.Y - start.Y) / 3f;
                p2.Y = start.Y + (far.Y - start.Y) * 2f / 3f;
                pieces.Add(new Bezier3(arcEnd, p1, p2, far));
            }
            if (!nodeAtEnd)
                return pieces;
            pieces.Reverse();
            return pieces.Select(b => b.Reverse()).ToList();
        }

        /// <summary>ElevatedDiverge 圆弧之后那段的最小长度（米）。</summary>
        private const float kMinRestLength = 20f;

        private static Vec3 Flat(Vec3 v) => Vec3.Normalize(new Vec3(v.X, 0f, v.Z));

        /// <summary>匝道在主线节点处的曲线端点离主线（本幅）中心线的横向距离：外侧边缘与主线对齐。</summary>
        public static float RampAlign(InterchangeParams p) => Math.Max(0f, (p.Legs[0].RoadWidth - p.RampWidth) / 2f);

        internal static bool MainElevated(InterchangeParams p) => LevelPlanner.BandOf(p.ElevationOf(p.Legs[0])) == ElevationBand.Elevated;

        /// <summary>
        /// 匝道中心离分流节点（主线一幅中心线）横移多远就不再压着主线：匝道节点之间主线是减车道的中段（向分隔带平移了
        /// MainDropShift），按中段的宽度算，再留 RampSideGap。
        /// </summary>
        public static float RampClear(InterchangeParams p)
        {
            float deck = p.MainReducedPrefab != null && p.MainReducedWidth > 0f ? p.MainReducedWidth : p.Legs[0].RoadWidth;
            return deck / 2f + p.RampWidth / 2f + p.RampSideGap - p.MainDropShift;
        }

        /// <summary>
        /// 内部双幅的两幅放得进外部双向道路的宽度（BuildParams 按外缘对齐双向道路选的单行道路）：臂端用 AddAlignedEnd 平行接入，
        /// 不用 AddTransition 斜向汇合。
        /// </summary>
        internal static bool FitsAligned(LegSpec leg, float offset) =>
            leg.ExternalWidth > 0f && 2f * offset + leg.RoadWidth <= leg.ExternalWidth + TrumpetTemplate.kMinSplitMedian + 0.5f;

        /// <summary>
        /// 臂端过渡（内部双幅 → 外部双向）的边缘对齐做法：两幅平行延伸到 at（带符号，沿道路），接到中心线上同一个节点
        ///（曲线端点不动，节点横移 = 边缘对齐），再以外部的双向道路延伸 stub。斜向汇合时两幅很宽的话彼此压着走很长一段
        ///（游戏内环岛式：两幅 24 米宽的单行道路斜向汇入 40 米的双向道路，在桥上报形状无效）。
        /// point(沿道路距离, 横向偏移) 给出坐标；两幅在 at 处的节点（横向 ±offset）换成中心线上的节点。
        /// </summary>
        internal static void AddAlignedEnd(List<RoadSegmentSpec> segments, string name, RoadRole role, Func<float, float, Vec3> point,
            float at, float offset, float stub, string? twoWayPrefab)
        {
            Vec3 joint = point(at, 0f), plus = point(at, offset), minus = point(at, -offset);
            Vec3 Moved(Vec3 node) => node.Equals(plus) || node.Equals(minus) ? joint : node;
            for (int i = 0; i < segments.Count; i++)
            {
                var s = segments[i];
                if (s.Role == role && (Moved(s.StartNode) != s.StartNode || Moved(s.EndNode) != s.EndNode))
                    segments[i] = new RoadSegmentSpec(s.Name, s.Role, s.Curve, s.Prefab, Moved(s.StartNode), Moved(s.EndNode));
            }
            segments.Add(new RoadSegmentSpec($"{name}-tail", role, Bezier3.Line(joint, point(at + Math.Sign(at) * stub, 0f)), twoWayPrefab));
        }

        /// <summary>主线中段（减车道）的横向偏移：向中央分隔带平移 MainDropShift，保持直行车道边缘不变。</summary>
        internal static float ReducedLateral(InterchangeParams p, float lateral) =>
            lateral - Math.Sign(lateral) * p.MainDropShift;

        /// <summary>道路各条线的横向偏移：双幅两条（±偏移），双向一条（0）。</summary>
        internal static float[] Laterals(LegSpec leg) =>
            leg.Form == RoadForm.Dual ? new[] { leg.CarriagewayOffset, -leg.CarriagewayOffset } : new[] { 0f };

        /// <summary>
        /// 一条道路线（主线或被交道路上横向偏移 lateral 的那条）跨越另一条道路处的两个节点位置（沿该线）。
        /// 这条路是上层且为高架时，按立柱规划（节点和中部立柱都避开下层道路，见 PillarPlanner）；
        /// 否则只在对方占地两侧各留 kPillarMargin，保证游戏切分路段的切点不落在交叉点上。
        /// </summary>
        internal static PillarPlanner.Span LineSpan(InterchangeParams p, bool onMain, float lateral)
        {
            var legs = p.Legs;
            LegSpec self = onMain ? legs[0] : legs[1];
            LegSpec other = onMain ? legs[1] : legs[0];
            Vec3 axis = self.Direction;
            Vec3 otherAxis = other.Direction;
            Vec3 side = Directions.RightOf(axis) * (p.RightHandTraffic ? 1f : -1f);

            float sin = Cross2(axis, otherAxis);
            float center = -lateral * Cross2(side, otherAxis) / sin;
            float footprint = other.HalfExtent / MathF.Abs(sin);

            float hSelf = p.ElevationOf(self), hOther = p.ElevationOf(other);
            bool elevatedUpper = hSelf > hOther && LevelPlanner.BandOf(hSelf) == ElevationBand.Elevated;
            if (!elevatedUpper)
                return new PillarPlanner.Span(center - footprint - kPillarMargin, center + footprint + kPillarMargin);

            float medianHalf = other.Form == RoadForm.Dual ? other.MedianWidth / 2f / MathF.Abs(sin) : 0f;
            return PillarPlanner.Plan(center, footprint, medianHalf, self.MiddlePillarSpacing, self.PillarWidth, self.MaxElevatedEdgeLength);
        }

        /// <summary>水平面二维叉积（X/Z），等于两方向夹角的正弦（带符号）。</summary>
        internal static float Cross2(Vec3 a, Vec3 b) => a.X * b.Z - a.Z * b.X;

        /// <summary>
        /// 匝道沿 travel 方向穿过被交道路时与两幅的交点。双向道路时两点都是 center。
        /// 近侧点先到达，远侧点后到达。
        /// </summary>
        private static (Vec3 near, Vec3 far) Terminal(Vec3 center, Vec3 travel, Vec3 crossSide, float carriagewayOffset)
        {
            if (carriagewayOffset <= 0f)
                return (center, center);
            float t = carriagewayOffset / MathF.Abs(Directions.Dot(travel, crossSide));
            return (center - travel * t, center + travel * t);
        }

        /// <summary>从同一接口的两个点中挑出位于 crossSide 正侧（或负侧）那幅上的点。</summary>
        private static Vec3 PickSide(Vec3 crossSide, bool positive, Vec3 a, Vec3 b) =>
            (Directions.Dot(a, crossSide) > 0f) == positive ? a : b;

        /// <summary>
        /// 沿一条道路线把节点连成首尾相接的路段。节点按沿 axis 的位置排序（重复的点去掉）；
        /// 名称按位置：最负端为 {name}-{negativeSuffix}，最正端为 {name}-{positiveSuffix}，中间依次为 M1、M2…；
        /// 最负端那段用 negativePrefab，其余用 positivePrefab。travelPositive = false 时按逆 axis 方向输出（单行道路的行驶方向）。
        /// </summary>
        /// <param name="innerLo">中段（沿 axis 位置在 innerLo ~ innerHi 之间的路段）改用 innerPrefab（车道平衡）；innerPrefab 为 null 时不变。</param>
        /// <param name="innerShift">
        /// 中段边缘对齐：中段在 innerLo / innerHi 处的曲线端点相对节点平移 innerShift（游戏道路工具的“边缘对齐”），
        /// 节点仍是两端共用的那个点。中段内部的节点由调用方直接放在平移后的位置。
        /// </param>
        internal static void AddRoadLine(List<RoadSegmentSpec> segments, string name, RoadRole role, Vec3 axis, bool travelPositive,
            string? negativePrefab, string? positivePrefab, string negativeSuffix, string positiveSuffix, List<Vec3> nodes,
            float innerLo = 0f, float innerHi = 0f, string? innerPrefab = null, Vec3 innerShift = default)
        {
            var sorted = nodes.Distinct().OrderBy(n => Directions.Dot(n, axis)).ToList();
            var line = new List<RoadSegmentSpec>();
            int count = sorted.Count - 1;
            for (int i = 0; i < count; i++)
            {
                string suffix = i == 0 ? negativeSuffix : i == count - 1 ? positiveSuffix : $"M{i}";
                Vec3 a = sorted[i], b = sorted[i + 1];
                float middle = Directions.Dot(Vec3.Lerp(a, b, 0.5f), axis);
                bool inner = innerPrefab != null && middle > innerLo && middle < innerHi;
                string? prefab = inner ? innerPrefab : i == 0 ? negativePrefab : positivePrefab;
                Vec3 curveA = a, curveB = b;
                if (inner)
                {
                    if (IsAt(a, axis, innerLo) || IsAt(a, axis, innerHi))
                        curveA = a + innerShift;
                    if (IsAt(b, axis, innerLo) || IsAt(b, axis, innerHi))
                        curveB = b + innerShift;
                }
                var segment = new RoadSegmentSpec($"{name}-{suffix}", role, Bezier3.Line(curveA, curveB), prefab, a, b);
                line.Add(travelPositive ? segment : segment.Reversed());
            }
            if (!travelPositive)
                line.Reverse();
            segments.AddRange(line);
        }

        private static bool IsAt(Vec3 point, Vec3 axis, float along) => Math.Abs(Directions.Dot(point, axis) - along) < 1e-3f;
    }
}
