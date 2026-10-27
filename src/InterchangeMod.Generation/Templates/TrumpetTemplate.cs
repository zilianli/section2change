using System;
using System.Collections.Generic;
using System.Linq;
using InterchangeMod.Generation.Geometry;
using static InterchangeMod.Generation.Templates.Planar;

namespace InterchangeMod.Generation.Templates
{
    /// <summary>
    /// 喇叭形（三向全互通，右侧通行）。主线（臂 0/2，双幅）在基层直行，支路（臂 1，双幅）从一侧接入，
    /// 支路两幅用一座桥跨过主线，在主线另一侧（远侧）形成“喇叭口”：
    ///
    ///          ╭──────╮        半定向匝道（支路进向一幅 → 远侧主线）：与环道同心绕过环道，外切汇入
    ///          │ ╭──╮ │
    ///          │(环道)││        环道：从远侧主线外切驶出，270° 右转升降，接跨线桥的出向一幅
    ///   ═══════╯═╰══╯═╪╪═══  远侧主线（与近侧反向）
    ///   ══════════════╪╪═══  近侧主线
    ///         ╰──────╮││╭─────╯   近侧两条直连匝道（右转），与主线、支路都相切的圆弧
    ///                 ││
    ///                支路（近侧沿支路真实方向）
    ///
    /// 局部坐标系：X 沿近侧主线的行驶方向，Y 指向远侧（支路在 −Y 一侧）。右侧通行时近侧一幅沿 +X 行驶，
    /// 远侧一幅沿 −X；支路出向一幅（沿 −Y）在 x = −ob，进向一幅（沿 +Y）在 x = +ob。
    /// 远侧主线上先经过环道的驶出点，再经过半定向匝道的汇入点，没有交织；两条匝道互不相交。
    /// loopForExit = false 时（环道用于驶入主线）整个布置沿 X 镜像并把所有行驶方向反过来。
    /// </summary>
    public sealed class TrumpetTemplate : IInterchangeTemplate
    {
        /// <summary>支路与主线的夹角范围（相对正交 ±55°，即 35°–145°）。</summary>
        private const float kMaxSkew = 55f;
        /// <summary>双向支路拆成双幅时两幅之间至少留的分隔带（米）：游戏画平行道路的最小间距 0.25 格；两幅贴在一起会报重叠。</summary>
        public const float kMinSplitMedian = 2f;
        /// <summary>
        /// 匝道与所接道路并行重叠的那段再多保持同高这么远（米）才开始升降：游戏把重叠部分算进节点区，
        /// 正好在重叠结束处分段时这段路几乎只剩节点区，剩下的几厘米会被判成“坡度过陡”/“形状无效”。
        /// </summary>
        private const float kFlatMargin = 15f;
        /// <summary>环道半径下限（米）。</summary>
        private const float kMinLoopRadius = 30f;
        /// <summary>面板允许的最大环道半径（米）。</summary>
        private const float kMaxLoopRadius = 250f;
        private const float kOppositeTolerance = 1f;
        private const float kGradeMargin = 0.98f;
        /// <summary>立柱离道路外缘的距离（米）。</summary>
        private const float kPillarMargin = 3f;
        /// <summary>直连匝道接支路处（及其并行重叠段）到支路外端或过渡段起点的距离（米），两处节点区不重叠。</summary>
        private const float kTransitionGap = 30f;
        /// <summary>主线在上层时直连匝道离开高架主线那段圆弧的半径上限（米）：这段与主线并行重叠，要短于游戏切分高架路段的长度。</summary>
        private const float kDivergeRadius = 80f;
        /// <summary>同一条道路上两个节点至少相距这么远，否则合并成一个（太短的路段游戏里会报形状无效）。</summary>
        private const float kMinSegment = 10f;

        public InterchangeType Type => InterchangeType.Trumpet;

        public string DisplayName => "喇叭形";

        public int LegCount => 3;

        public IReadOnlyList<string> ValidateParams(InterchangeParams p)
        {
            var errors = p.ValidateLegsAndLevels(LegCount);
            if (errors.Count > 0)
                return errors;
            var legs = p.Legs;
            if (!p.RightHandTraffic)
                errors.Add("喇叭形目前只支持右侧通行");
            if (Directions.AngleBetween(legs[0].Angle, legs[2].Angle) < 180f - kOppositeTolerance)
                errors.Add("喇叭形要求主线（臂 0/2）是直线");
            float crossing = Directions.AngleBetween(legs[0].Angle, legs[1].Angle);
            if (Math.Abs(crossing - 90f) > kMaxSkew)
                errors.Add($"支路与主线夹角 {crossing:F0}°：喇叭形需要 {90f - kMaxSkew:F0}°–{90f + kMaxSkew:F0}°");
            if (legs[0].Form != RoadForm.Dual)
                errors.Add("喇叭形的主线需要是双幅（两条对向单行道路）：请选择单行的公路或把主线形式改为双幅");
            if (legs[1].Form != RoadForm.Dual)
                errors.Add("喇叭形的支路需要是双幅（外部是双向道路时在支路外端过渡）");

            // 通常支路以跨线桥从上层跨过主线；对准已有的立体交叉时跟随原有高度，也可以是主线高架、支路从下面穿过
            float h0 = p.ElevationOf(legs[0]), h1 = p.ElevationOf(legs[1]);
            float gap = DiamondTemplate.RequiredGap(p);
            bool branchUpper = h1 > h0;
            float upper = Math.Max(h0, h1);
            if (Math.Abs(h1 - h0) < gap - HeightRange.GapTolerance)
                errors.Add($"支路与主线高差 {Math.Abs(h1 - h0):F1} 米，至少需要 {gap:F1} 米净空：支路（跨线桥）要在上层");
            else if (LevelPlanner.BandOf(upper) != ElevationBand.Elevated)
                errors.Add($"{(branchUpper ? "跨线桥" : "主线高架")}离地 {upper:F1} 米会建成路堤（挡土墙），下层道路无法从下面穿过：需要至少 {HeightRange.BridgeThreshold:F1} 米");
            if (p.RampWidth > legs[0].RoadWidth)
                errors.Add($"匝道宽 {p.RampWidth:F0} 米，比主线（{legs[0].RoadWidth:F0} 米）还宽：请减少匝道车道数");
            if (errors.Count > 0)
                return errors;
            if (p.LoopRadius > 0f && p.LoopRadius < kMinLoopRadius)
                return new[] { $"环道半径至少 {kMinLoopRadius:F0} 米" };
            bool auto = p.LoopRadius <= 0f;
            p = WithLoopRadius(p);
            if (auto && !LoopFits(new Plan(p), p))
                return new[] { "这个交角下环道放不下：请把交角调得更接近 90° 或加大最大坡度" };

            var plan = new Plan(p);
            errors.AddRange(plan.Errors);
            if (!LoopFits(plan, p))
            {
                // 环道太小（接不到跨线桥之外）或环道 / 半定向匝道升降太陡：找出能放下的最小环道半径
                float radius = p.LoopRadius;
                var bigger = p.Clone();
                while (radius < kMaxLoopRadius)
                {
                    radius += 5f;
                    bigger.LoopRadius = radius;
                    if (LoopFits(new Plan(bigger), bigger))
                        break;
                }
                errors.Add(radius < kMaxLoopRadius
                    ? $"这个交角下环道半径至少 {radius:F0} 米（环道要接到跨线桥之外，升降坡度不超过上限；加大最大坡度可以更小）"
                    : "这个交角下环道放不下：请把交角调得更接近 90° 或加大最大坡度");
            }
            if (p.DirectRampRadius > 0f && p.DirectRampRadius < plan.MinNearRadius - 0.5f)
                errors.Add($"直连匝道半径至少 {MathF.Ceiling(plan.MinNearRadius):F0} 米（受支路下桥的坡长限制；加大最大坡度可以更小）");
            return errors;
        }

        /// <summary>环道能放下：终点和半定向匝道起点在跨线桥之外，两者升降都不超过坡度上限。</summary>
        private static bool LoopFits(Plan plan, InterchangeParams p)
        {
            float rise = Math.Abs(p.ElevationOf(p.Legs[1]) - p.ElevationOf(p.Legs[0]));
            return !plan.LoopTooSmall && rise <= p.MaxGrade * plan.LoopClimbLength && rise <= p.MaxGrade * plan.SemiDirectClimbLength;
        }

        /// <summary>环道半径：手动（LoopRadius &gt; 0）时就是它；自动（0）时取能放下的最小半径（5 米一档，放不下时取上限）。</summary>
        public static float ResolveLoopRadius(InterchangeParams p)
        {
            if (p.LoopRadius > 0f)
                return p.LoopRadius;
            var trial = p.Clone();
            return RadiusSearch.Smallest(r =>
            {
                trial.LoopRadius = r;
                return LoopFits(new Plan(trial), trial);
            }, kMinLoopRadius, kMaxLoopRadius) ?? kMaxLoopRadius;
        }

        private static InterchangeParams WithLoopRadius(InterchangeParams p)
        {
            if (p.LoopRadius > 0f)
                return p;
            var resolved = p.Clone();
            resolved.LoopRadius = ResolveLoopRadius(p);
            return resolved;
        }

        public IReadOnlyList<RoadSegmentSpec> Generate(InterchangeParams p)
        {
            p = WithLoopRadius(p);
            var plan = new Plan(p);
            var segments = new List<RoadSegmentSpec>();
            plan.Build(segments);
            if (!p.TrumpetLoopForExit)
            {
                // 环道用于驶入：沿 X 镜像，再把每条路段反向（行驶方向随之反过来，右侧通行不变）
                Vec3 Mirror(Vec3 v) => v - plan.X * (2f * Directions.Dot(v, plan.X));
                for (int i = 0; i < segments.Count; i++)
                    segments[i] = segments[i].Transform(Mirror).Reversed();
            }
            return segments;
        }

        /// <summary>
        /// 按参数算出的整体布置；坐标单位米，局部坐标（立交中心为原点）。
        /// 支路两侧都沿支路真实方向 f（指向远侧）走直线，斜交时不在桥头折弯；
        /// 环道、半定向匝道、直连匝道都是同时与主线和支路相切的圆弧，转角随交角变化。
        /// </summary>
        private sealed class Plan
        {
            private readonly InterchangeParams P;
            public readonly Vec3 X, Y, Up = new Vec3(0f, 1f, 0f);
            private readonly float h0, h1, hLow, om, ob, R;
            /// <summary>支路在上层（跨线桥）；false 时主线高架、支路从下面穿过。</summary>
            private readonly bool branchUpper;
            /// <summary>f：支路方向（指向远侧）；n：垂直于支路、指向出向一幅那侧。</summary>
            private readonly Vec3 f, n;
            /// <summary>支路出向 / 进向一幅跨越主线的两个节点（沿 f 的位置 u）；支路在上层时按立柱规划，立柱避开主线两幅。</summary>
            private readonly PillarPlanner.Span spanOut, spanIn;
            /// <summary>主线近侧 / 远侧一幅跨越支路的两个节点（沿 X）；主线在上层时按立柱规划，立柱避开支路两幅。</summary>
            private readonly PillarPlanner.Span spanNear, spanFar;
            /// <summary>布置无法成立的原因（立柱规划失败、环道太小放不下等）；可以生成时为空。</summary>
            public readonly List<string> Errors = new List<string>();

            private readonly float climb, sepMain, sepBranch;
            /// <summary>
            /// 车道平衡时匝道在主线节点处的横向位置（米，向外侧）：匝道外缘对齐主线全宽的外缘，与减车道后的主线并排接到同一个节点
            /// （游戏的“边缘对齐”汇入/分出），匝道平行进出、不斜插主线中心。没有车道平衡时为 0（切入主线中心线）。
            /// </summary>
            private readonly float d;
            /// <summary>匝道离开 / 接入主线时，还需要横移多少才与主线不再重叠。</summary>
            private float SepFromMainLine => Math.Max(1f, sepMain - d);
            private readonly RampPath nearOut, nearIn;
            private readonly float tOut, tIn, radiusOut, radiusIn;
            /// <summary>近侧直连匝道两头与主线 / 支路并行重叠（保持所接道路高度）的长度。</summary>
            private readonly float outFlatMain, outFlatBranch, inFlatMain, inFlatBranch;
            /// <summary>近侧直连匝道满足坡长要求的最小半径（两条中较大的那个）。</summary>
            public readonly float MinNearRadius;
            /// <summary>环道半径太小，环道终点或半定向匝道起点落在跨线桥范围内。</summary>
            public readonly bool LoopTooSmall;

            private readonly RampPath loop, semi;
            private readonly float xLoop, xMerge, uLoopEnd, uSemiStart, loopFlat, semiFlat;
            /// <summary>环道升降段长度（去掉与主线并行重叠的那段）。</summary>
            public readonly float LoopClimbLength;
            /// <summary>半定向匝道升降段（绕环道的圆弧）的长度。</summary>
            public readonly float SemiDirectClimbLength;

            public Plan(InterchangeParams p)
            {
                P = p;
                var legs = p.Legs;
                Vec3 main = legs[0].Direction, branch = legs[1].Direction;
                // 近侧（支路一侧）的单位法向；X 取右侧正好是近侧的那个主线方向（右侧通行：近侧一幅沿 +X 行驶）
                Vec3 right = Directions.RightOf(main);
                Vec3 near = Directions.Dot(branch, right) > 0f ? right : -right;
                X = Directions.Dot(Directions.RightOf(main), near) > 0f ? main : -main;
                Y = -near;
                h0 = p.ElevationOf(legs[0]);
                h1 = p.ElevationOf(legs[1]);
                hLow = Math.Min(h0, h1);
                branchUpper = h1 > h0;
                om = legs[0].CarriagewayOffset;
                ob = legs[1].CarriagewayOffset;
                R = p.LoopRadius;

                float rw = p.RampWidth, gap = p.RampSideGap;
                float grade = p.MaxGrade * kGradeMargin;
                sepMain = legs[0].RoadWidth / 2f + rw / 2f + gap;
                d = p.MainReducedPrefab != null ? Math.Max(0f, (legs[0].RoadWidth - rw) / 2f) : 0f;
                sepBranch = legs[1].RoadWidth / 2f + rw / 2f + gap;

                // 支路方向：环道用于驶入时整个布置最后沿 X 镜像，这里先用镜像后的方向，镜像回去正好是真实方向
                Vec3 dW = Flat(branch);
                if (!p.TrumpetLoopForExit)
                    dW = dW - X * (2f * Directions.Dot(dW, X));
                f = -dW;
                n = Perp(f);
                if (Directions.Dot(n, X) > 0f)
                    n = -n;

                spanOut = BranchSpan(ob);
                spanIn = BranchSpan(-ob);
                spanNear = MainSpanAt(-om);
                spanFar = MainSpanAt(om);
                foreach (var span in branchUpper ? new[] { spanOut, spanIn } : new[] { spanNear, spanFar })
                    if (span.Error != null && !Errors.Contains(span.Error))
                        Errors.Add(span.Error);
                // 支路在上层时下桥的坡（两端水平的竖曲线，最陡处是平均坡度的 1.5 倍）；支路在下层时一直是平的
                climb = branchUpper ? 1.5f * (h1 - h0) / grade : 0f;

                // —— 近侧直连匝道：与主线、支路都相切的圆弧（主线在上层时先用较小半径的圆弧尽快离开高架主线，再接大圆弧）——
                // 两条匝道转角不同（斜交时一条小于 90°、一条大于 90°），各自求半径；手动半径不小于各自的下限
                float manual = Math.Max(30f, p.DirectRampRadius);
                (radiusOut, nearOut, tOut, outFlatMain, outFlatBranch) = SolveRamp(true, manual, grade);
                (radiusIn, nearIn, tIn, inFlatMain, inFlatBranch) = SolveRamp(false, manual, grade);
                MinNearRadius = Math.Max(SolveRamp(true, 30f, grade).Radius, SolveRamp(false, 30f, grade).Radius);

                // —— 远侧：环道与远侧主线外切驶出，右转升降，最后与出向一幅相切（正交时转 270°）——
                float rightTurn = DiamondTemplate.Cross2(-X, Y) >= 0f ? 1f : -1f;
                loop = RampPath.Turn(Point(0f, om + d, 0f), -X, B(0f, ob), -f, rightTurn, R, out float aLoop, out float bLoop);
                xLoop = -aLoop;
                uLoopEnd = -bLoop;
                loopFlat = R * (float)Math.Acos(Math.Max(-1f, 1f - SepFromMainLine / R)) + kFlatMargin;
                LoopClimbLength = loop.Length - loopFlat;
                if (uLoopEnd < spanOut.Hi + kMinSegment)
                    LoopTooSmall = true;

                // —— 远侧：半定向匝道（支路进向一幅 → 远侧主线）：与环道同心绕过环道（间距等于支路两幅的间距，至少一条匝道宽加间距），
                //     再反向右转外切汇入远侧主线 ——
                Vec3 c = loop.Arcs[0].Center;
                float r2 = R + Math.Max(2f * ob, rw + gap);
                Vec3 c2 = c + n * (r2 - (Directions.Dot(c, n) + ob));    // 圆心离进向一幅正好 r2
                Vec3 t0 = c2 - n * r2;                                      // 起点：进向一幅上
                uSemiStart = Directions.Dot(t0, f);
                if (uSemiStart < spanIn.Hi + kMinSegment)
                    LoopTooSmall = true;
                float yC2 = Directions.Dot(c2, Y), yC3 = om + d + R;
                float reach = (r2 + R) * (r2 + R) - (yC2 - yC3) * (yC2 - yC3);
                Vec3 c3 = c2 - X * MathF.Sqrt(Math.Max(0f, reach)) + Y * (yC3 - yC2);
                xMerge = Directions.Dot(c3, X);
                Vec3 tangent = c2 + (c3 - c2) * (r2 / (r2 + R));
                float leftTurn = DiamondTemplate.Cross2(f, n) >= 0f ? 1f : -1f;
                var around = new RampArc(t0, f, r2, leftTurn * RotAngle(t0 - c2, tangent - c2, leftTurn));
                Vec3 mergePoint = Point(xMerge, om + d, 0f);
                var into = new RampArc(around.End, around.EndHeading, R, -leftTurn * RotAngle(around.End - c3, mergePoint - c3, -leftTurn));
                semi = RampPath.FromArcs(around, into);
                semiFlat = into.Length;
                SemiDirectClimbLength = around.Length;
                if (reach <= 0f || xMerge > xLoop - kMinSegment)
                    Errors.Add("半定向匝道放不下：请加大环道半径");
            }

            private Vec3 Point(float x, float y, float h) => X * x + Y * y + Up * h;

            /// <summary>支路上的点：沿 f 距中心 u 米，横向 lateral（正 = 出向一幅那侧）。</summary>
            private Vec3 B(float u, float lateral, float h = 0f) => f * u + n * lateral + Up * h;

            /// <summary>支路横向 lateral 的那条线跨越主线的两个节点（沿 f 的 u）。</summary>
            private PillarPlanner.Span BranchSpan(float lateral)
            {
                var legs = P.Legs;
                float fy = Directions.Dot(f, Y);
                float center = -Directions.Dot(n, Y) * lateral / fy;
                float half = legs[0].HalfExtent / fy;
                if (!branchUpper)
                    return new PillarPlanner.Span(center - half - kPillarMargin, center + half + kPillarMargin);
                return PillarPlanner.Plan(center, half, legs[0].MedianWidth / 2f / fy, legs[1].MiddlePillarSpacing,
                    legs[1].PillarWidth, legs[1].MaxElevatedEdgeLength);
            }

            /// <summary>主线横向 y 的那一幅跨越支路的两个节点（沿 X）。</summary>
            private PillarPlanner.Span MainSpanAt(float y)
            {
                var legs = P.Legs;
                float nx = Math.Abs(Directions.Dot(n, X));
                float center = -y * Directions.Dot(n, Y) / Directions.Dot(n, X);
                float half = legs[1].HalfExtent / nx;
                if (branchUpper)
                    return new PillarPlanner.Span(center - half - kPillarMargin, center + half + kPillarMargin);
                return PillarPlanner.Plan(center, half, legs[1].MedianWidth / 2f / nx, legs[0].MiddlePillarSpacing,
                    legs[0].PillarWidth, legs[0].MaxElevatedEdgeLength);
            }

            /// <summary>匝道以半径 radius 切入一条横向间距 separation 的道路时，接入点之前两者并行重叠的长度（约）。</summary>
            private static float Overlap(float radius, float separation) => MathF.Sqrt(2f * radius * separation);

            /// <summary>
            /// 一条近侧直连匝道（outbound = 主线 → 支路出向一幅）：从 rMin 起找满足约束的最小半径。约束：支路在接入点之前与匝道并行重叠的一段
            /// 必须已经下到地面（再留 kTransitionGap）；主线在上层时匝道自己升降，去掉两端水平段后坡长要够。两者都随半径单调变松，先倍增再二分。
            /// </summary>
            private (float Radius, RampPath RampPath, float T, float FlatMain, float FlatBranch) SolveRamp(bool outbound, float rMin, float grade)
            {
                float drop = h0 - hLow;
                bool mainElevated = LevelPlanner.BandOf(h0) == ElevationBand.Elevated;
                bool branchElevated = LevelPlanner.BandOf(hLow) == ElevationBand.Elevated;
                Vec3 mainNear = Point(0f, -om - d, 0f);
                float lateral = outbound ? ob : -ob;
                float foot = -(outbound ? spanOut : spanIn).Lo + climb;
                (bool Ok, RampPath RampPath, float T, float FlatMain, float FlatBranch) Try(float r)
                {
                    // 接高架道路的一头用小半径圆弧：并行重叠段要短于游戏切分高架路段的长度，否则切分点的立柱落在所接道路上
                    //（主线在上层、或基层本身是高架时）。接地面道路的一头用大半径（小半径会让接入点离桥更近，反而要更大的半径）
                    float rMain = drop > 0.1f || mainElevated ? Math.Min(r, kDivergeRadius) : r;
                    float rBranch = branchElevated ? Math.Min(r, kDivergeRadius) : r;
                    // 进向：从主线反向倒推，再整体反过来
                    var path = RampPath.Fillet(mainNear, outbound ? X : -X, B(0f, lateral), -f, rMain, r, rBranch, SepFromMainLine, sepBranch,
                        out _, out float t, out float flatMain, out float flatBranch);
                    flatMain += kFlatMargin;
                    flatBranch += kFlatMargin;
                    if (!outbound)
                        path = path.Reversed();
                    bool ok = t - (flatBranch - kFlatMargin) >= foot + kMinSegment;   // 支路已下到地面：按实际重叠长度算
                    if (drop > 0.1f)
                        ok &= path.Length - flatMain - flatBranch >= drop / grade;
                    return (ok, path, t, flatMain, flatBranch);
                }
                var first = Try(rMin);
                if (first.Ok)
                    return (rMin, first.RampPath, first.T, first.FlatMain, first.FlatBranch);
                float lo = rMin, hi = rMin;
                for (int i = 0; i < 20; i++)
                {
                    hi *= 1.5f;
                    if (Try(hi).Ok)
                        break;
                    lo = hi;
                }
                for (int i = 0; i < 20 && hi - lo > 0.5f; i++)
                {
                    float mid = (lo + hi) / 2f;
                    if (Try(mid).Ok)
                        hi = mid;
                    else
                        lo = mid;
                }
                var result = Try(hi);
                return (hi, result.RampPath, result.T, result.FlatMain, result.FlatBranch);
            }

            public void Build(List<RoadSegmentSpec> segments)
            {
                var p = P;
                var legs = p.Legs;

                // —— 主线（双幅）：车道平衡——匝道分出点和汇入点之间减车道，向中央分隔带平移保持直行车道边缘对齐 ——
                float xOut = Directions.Dot(nearOut.Start, X), xIn = Directions.Dot(nearIn.End, X);
                float mainOuter = Math.Max(Math.Max(xIn, -xOut), Math.Max(-xMerge, Math.Abs(xLoop))) + legs[0].StubLength;
                var nearX = new List<float> { -mainOuter, xOut, xIn, mainOuter };
                var farX = new List<float> { -mainOuter, xMerge, xLoop, mainOuter };
                if (!branchUpper)
                {
                    // 主线高架跨过支路：跨越段两端的节点按立柱规划，立柱避开支路两幅
                    nearX.AddRange(new[] { spanNear.Lo, spanNear.Hi });
                    farX.AddRange(new[] { spanFar.Lo, spanFar.Hi });
                }
                AddMainLine(segments, "MainNear", true, -om, xOut, xIn, nearX);
                AddMainLine(segments, "MainFar", false, om, xMerge, xLoop, farX);

                // —— 支路两幅：远侧（环道终点 / 半定向匝道起点）→ 跨线桥 → 近侧桥头 → 坡底 → 直连匝道节点 → 外端 ——
                var leg1 = legs[1];
                bool transition = leg1.NeedsDualToTwoWayTransition;
                // 两幅外缘大致是外部双向道路的两侧边缘（分隔带至少留 kMinSplitMedian）：边缘对齐接入，不斜向汇合
                bool aligned = transition && leg1.ExternalWidth > 0f && 2f * ob + leg1.RoadWidth <= leg1.ExternalWidth + kMinSplitMedian + 0.5f;
                float tBase = Math.Max(tOut, tIn);
                float tail = transition ? kTransitionGap : leg1.StubLength;
                Vec3 TailPoint(float along, float lateral) => B(-(tBase + along), lateral, hLow);

                var outNodes = new List<Vec3> { B(uLoopEnd, ob, h1) };
                var inNodes = new List<Vec3> { B(uSemiStart, -ob, h1) };
                // 远侧桥头节点离环道终点 / 半定向匝道起点太近时（斜交时会这样）省掉它，由那个节点当桥头（立柱更远离主线）
                if (FarBridgeNodeNeeded(uLoopEnd, spanOut))
                    outNodes.Add(B(spanOut.Hi, ob, h1));
                if (FarBridgeNodeNeeded(uSemiStart, spanIn))
                    inNodes.Add(B(spanIn.Hi, -ob, h1));
                outNodes.Add(B(spanOut.Lo, ob, h1));
                inNodes.Add(B(spanIn.Lo, -ob, h1));
                if (branchUpper)
                {
                    outNodes.Add(B(spanOut.Lo - climb, ob, hLow));
                    inNodes.Add(B(spanIn.Lo - climb, -ob, hLow));
                }
                Vec3 outRampNode = B(-tOut, ob, hLow), inRampNode = B(-tIn, -ob, hLow);
                outNodes.Add(outRampNode);
                inNodes.Add(inRampNode);
                outNodes.Add(TailPoint(tail, ob));
                inNodes.Add(TailPoint(tail, -ob));
                inNodes.Reverse();
                // 车道平衡：驶出的一幅在直连匝道汇入之前只有环道的车流、驶入的一幅在直连匝道驶出之后只剩半定向匝道的车流，
                // 这两段用减车道的道路，向远离直连匝道的一侧平移（另一侧边缘不变）；直连匝道在节点处外缘对齐全宽道路的边缘
                bool balanced = leg1.Form == RoadForm.Dual && p.CrossDropShift > 0f;
                float dropB = balanced ? p.CrossDropShift : 0f, edgeB = balanced ? Math.Max(0f, (leg1.RoadWidth - p.RampWidth) / 2f) : 0f;
                int outRamp = outNodes.IndexOf(outRampNode), inRamp = inNodes.IndexOf(inRampNode);
                Vec3 sideOut = RampSide(nearOut, outRampNode, outNodes[outRamp + 1] - outRampNode, atEnd: true);
                Vec3 sideIn = RampSide(nearIn, inRampNode, inNodes[inRamp + 1] - inRampNode, atEnd: false);
                Vec3 shiftOut = sideOut * -dropB, shiftIn = sideIn * -dropB;
                var outMoved = AddBalancedChain(segments, "BranchOut", RoadRole.CrossRoad, leg1.RoadPrefab, balanced ? p.CrossReducedPrefab : null,
                    outNodes, outRamp, reducedBeforeRamp: true, shiftOut);
                int outLast = segments.Count - 1;
                var inMoved = AddBalancedChain(segments, "BranchIn", RoadRole.CrossRoad, leg1.RoadPrefab, balanced ? p.CrossReducedPrefab : null,
                    inNodes, inRamp, reducedBeforeRamp: false, shiftIn);
                int inFirst = outLast + 1;
                if (aligned)
                {
                    // 两幅平行接到外部双向道路端点的同一个节点，曲线端点相对节点横移 ±幅偏移（游戏道路工具的“边缘对齐”）
                    Vec3 joint = TailPoint(tail, 0f);
                    var o = segments[outLast];
                    segments[outLast] = new RoadSegmentSpec(o.Name, o.Role, o.Curve, o.Prefab, o.StartNode, joint);
                    var s = segments[inFirst];
                    segments[inFirst] = new RoadSegmentSpec(s.Name, s.Role, s.Curve, s.Prefab, joint, s.EndNode);
                    segments.Add(new RoadSegmentSpec("Branch-1-tail", RoadRole.CrossRoad,
                        Bezier3.Line(joint, TailPoint(tail + leg1.StubLength, 0f)), leg1.ExternalPrefab));
                }
                else if (transition)
                    DiamondTemplate.AddTransition(segments, "Branch-1", RoadRole.CrossRoad, TailPoint, +1, tail, ob,
                        tail + leg1.StubLength, p.LegTransitionAngle, leg1.RoadPrefab, leg1.ExternalPrefab);

                // —— 近侧直连匝道：两端与道路并行重叠的部分与所接道路同高，中间升降（主线在上层时）——
                // 匝道在主线一端：曲线端点在外侧 d 处，节点是主线的节点（边缘对齐）
                nearOut.Emit(segments, "NearToBranch", Point(xOut, -om - d, h0), outRampNode + sideOut * edgeB, outFlatMain, outFlatBranch, p.RampPrefab,
                    startNode: Point(xOut, -om, h0), endNode: outRampNode);
                nearIn.Emit(segments, "BranchToNear", inRampNode + sideIn * edgeB, Point(xIn, -om - d, h0), inFlatBranch, inFlatMain, p.RampPrefab,
                    startNode: inRampNode, endNode: Point(xIn, -om, h0));

                // —— 环道、半定向匝道 ——
                loop.Emit(segments, "Loop", Point(xLoop, om + d, h0), outMoved[0], loopFlat, 0f, p.RampPrefab,
                    startNode: Point(xLoop, om, h0));
                semi.Emit(segments, "SemiDirect", inMoved[inMoved.Count - 1], Point(xMerge, om + d, h0), 0f, semiFlat, p.RampPrefab,
                    endNode: Point(xMerge, om, h0));
            }

            /// <summary>
            /// 跨线桥远侧桥头节点是否单独保留：离支路在远侧的端点（环道终点 / 半定向匝道起点）至少 2 × kMinSegment，
            /// 或者省掉它后桥跨会超过游戏切分高架路段的长度时保留。
            /// </summary>
            private bool FarBridgeNodeNeeded(float uEnd, PillarPlanner.Span span) =>
                uEnd - span.Hi >= 2f * kMinSegment || uEnd - span.Lo > P.Legs[1].MaxElevatedEdgeLength;

            /// <summary>
            /// 主线一幅（沿 X）。lo ~ hi 之间（匝道分出点与汇入点之间）车道平衡：改用减车道的道路，
            /// 中间节点放在向中央分隔带平移后的位置，两端节点处曲线端点平移（边缘对齐）。
            /// 相距不到 kMinSegment 的节点合并（保留匝道节点和两端），避免太短的路段。
            /// </summary>
            private void AddMainLine(List<RoadSegmentSpec> segments, string name, bool travelPositive, float lateral, float lo, float hi, List<float> xs)
            {
                var legs = P.Legs;
                float min = xs.Min(), max = xs.Max();
                var keep = new List<float>();
                foreach (float x in xs.OrderBy(x => x))
                {
                    bool fixedNode = Math.Abs(x - lo) < 1e-3f || Math.Abs(x - hi) < 1e-3f || x == min || x == max;
                    int near = keep.FindIndex(k => Math.Abs(k - x) < Math.Max(kMinSegment, legs[0].RoadWidth));
                    if (near < 0)
                        keep.Add(x);
                    else if (fixedNode)
                        keep[near] = x;
                }
                float inner = DiamondTemplate.ReducedLateral(P, lateral);
                var nodes = keep.Select(x => Point(x, x > lo + 1e-3f && x < hi - 1e-3f ? inner : lateral, h0)).ToList();
                DiamondTemplate.AddRoadLine(segments, name, RoadRole.MainRoad, X, travelPositive, legs[0].RoadPrefab, legs[0].RoadPrefab, "2", "0",
                    nodes, lo, hi, P.MainReducedPrefab, Y * (inner - lateral));
            }

            /// <summary>直连匝道在支路一幅节点 node 处从哪一侧接入（沿 along 行驶时的左右，返回那一侧的单位向量）：看匝道离节点 20 米处。</summary>
            private static Vec3 RampSide(RampPath path, Vec3 node, Vec3 along, bool atEnd)
            {
                Vec3 q = path.PointAt(atEnd ? Math.Max(0f, path.Length - 20f) : Math.Min(path.Length, 20f));
                Vec3 right = Directions.RightOf(Flat(along));
                return Directions.Dot(Flat0(q) - Flat0(node), right) >= 0f ? right : -right;
            }

            /// <summary>
            /// 支路一幅（按行驶顺序的节点）：rampIndex 处是直连匝道的节点；它之前（reducedBeforeRamp）或之后的路段用减车道的道路
            /// （reducedPrefab 为 null 时不减），那一段的节点平移 shift，匝道节点处曲线端点平移、节点不动（边缘对齐）。
            /// 返回平移后的节点（环道终点 / 半定向匝道起点要接到平移后的位置）。
            /// </summary>
            private static List<Vec3> AddBalancedChain(List<RoadSegmentSpec> segments, string name, RoadRole role, string? prefab, string? reducedPrefab,
                IList<Vec3> nodes, int rampIndex, bool reducedBeforeRamp, Vec3 shift)
            {
                bool balanced = reducedPrefab != null && rampIndex >= 0;
                bool Reduced(int seg) => balanced && (reducedBeforeRamp ? seg < rampIndex : seg >= rampIndex);
                bool Moved(int k) => balanced && k != rampIndex && (reducedBeforeRamp ? k < rampIndex : k > rampIndex);
                var moved = nodes.Select((v, k) => Moved(k) ? v + shift : v).ToList();
                for (int i = 0; i + 1 < moved.Count; i++)
                {
                    Vec3 a = moved[i], b = moved[i + 1];
                    bool reduced = Reduced(i);
                    Vec3 ca = reduced && i == rampIndex ? a + shift : a, cb = reduced && i + 1 == rampIndex ? b + shift : b;
                    var curve = Math.Abs(a.Y - b.Y) > 0.1f ? Bezier3.SmoothSlope(ca, cb) : Bezier3.Line(ca, cb);
                    segments.Add(new RoadSegmentSpec($"{name}-{i + 1}", role, curve, reduced ? reducedPrefab : prefab, a, b));
                }
                return moved;
            }

        }
    }
}
