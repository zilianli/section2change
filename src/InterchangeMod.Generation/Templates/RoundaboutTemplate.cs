using System;
using System.Collections.Generic;
using System.Linq;
using InterchangeMod.Generation.Geometry;
using static InterchangeMod.Generation.Templates.Planar;

namespace InterchangeMod.Generation.Templates
{
    /// <summary>
    /// 两层环岛式立交。主线（臂 0/2，双幅）直行跨过（或穿过）环岛，环岛与被交道路（臂 1/3）在同一层：
    ///
    ///                 ║ ║  主线
    ///             ╭───╫─╫───╮
    ///         ╱   │   ║ ║   │   ╲      环岛（右侧通行逆时针），从主线桥下穿过两次
    ///   ══════╡   │   ║ ║   │   ╞══════  被交道路接环岛（平交路口）
    ///         ╲   │   ║ ║   │   ╱      匝道：主线每幅一条驶出、一条驶入，接环岛（平交路口）
    ///             ╰───╫─╫───╯
    ///                 ║ ║
    ///
    /// 以主线某一幅（沿 V 行驶，右侧 L）为例：驶出匝道在环岛之前从主线驶出，S 形横移到环岛半径一半的横向位置，
    /// 沿主线方向直行到环岛（与环岛行车方向成 60°）；驶入匝道是它沿 L 轴的镜像并反向。环岛在 L 一侧沿 +V 行驶。
    /// 环岛这一半（L 侧）连同匝道、被交道路这一侧，另一半是它绕中心转 180°。
    /// 环岛形状：圆形，或沿主线拉长成跑道形（两头半圆、中间直线）。坐标单位米，立交中心为原点。
    /// </summary>
    public sealed class RoundaboutTemplate : IInterchangeTemplate
    {
        private const float kMaxSkew = 30f;
        private const float kOppositeTolerance = 1f;
        private const float kGradeMargin = 0.98f;
        private const float kPillarMargin = 3f;
        /// <summary>匝道接高架道路那头的圆弧半径上限（米）：并行重叠段要短于游戏切分高架路段的长度。</summary>
        private const float kDivergeRadius = 80f;
        /// <summary>kDivergeRadius 放不下时逐档减小的步长和下限（米）。</summary>
        private const float kDivergeRadiusStep = 10f, kMinDivergeRadius = 50f;
        /// <summary>匝道 S 形横移的圆弧半径（米，地面时）。</summary>
        private const float kSRadius = 150f;
        /// <summary>匝道与主线并行重叠的那段再多保持同高这么远（米）才开始升降。</summary>
        private const float kFlatMargin = 15f;
        /// <summary>环岛路口前匝道保持环岛高度的长度（米）。</summary>
        private const float kTerminalFlat = 15f;
        private const float kMinRadius = 40f;
        private const float kMaxRadius = 300f;
        /// <summary>跑道形环岛中间直线段的半长 = 半径 × 这个比例。</summary>
        private const float kRacetrackRatio = 0.5f;
        /// <summary>直线段用超大半径的圆弧表示（RampPath 只有圆弧）。</summary>
        private const float kStraight = 100000f;

        public InterchangeType Type => InterchangeType.Roundabout;

        public string DisplayName => "环岛式";

        public int LegCount => 4;

        public IReadOnlyList<string> ValidateParams(InterchangeParams p)
        {
            var errors = p.ValidateLegsAndLevels(LegCount);
            if (errors.Count > 0)
                return errors;
            var legs = p.Legs;
            if (Directions.AngleBetween(legs[0].Angle, legs[2].Angle) < 180f - kOppositeTolerance
                || Directions.AngleBetween(legs[1].Angle, legs[3].Angle) < 180f - kOppositeTolerance)
                errors.Add("环岛式立交要求两条道路都是直线（臂 0/2、臂 1/3 各自相对）");
            float crossing = Directions.AngleBetween(legs[0].Angle, legs[1].Angle);
            if (Math.Abs(crossing - 90f) > kMaxSkew)
                errors.Add($"两条道路夹角 {crossing:F0}°：环岛式立交需要 {90f - kMaxSkew:F0}°–{90f + kMaxSkew:F0}°");
            if (legs[0].Form != RoadForm.Dual)
                errors.Add("环岛式立交的主线需要是双幅（两条对向单行道路）：请选择单行的公路或把主线形式改为双幅");
            if (legs[1].NeedsDualToTwoWayTransition || legs[3].NeedsDualToTwoWayTransition)
                errors.Add("被交道路外部是双向、内部是双幅：环岛式立交请让被交道路保持原来的形式");
            if (string.IsNullOrEmpty(p.RingPrefab) || p.RingWidth <= 0f)
                errors.Add("环岛式立交需要一条单行道路作环岛");

            float hm = p.ElevationOf(legs[0]), hc = p.ElevationOf(legs[1]);
            float gap = DiamondTemplate.RequiredGap(p);
            float upper = Math.Max(hm, hc);
            if (Math.Abs(hm - hc) < gap - HeightRange.GapTolerance)
                errors.Add($"两条道路高差 {Math.Abs(hm - hc):F1} 米，至少需要 {gap:F1} 米净空");
            else if (LevelPlanner.BandOf(upper) != ElevationBand.Elevated)
                errors.Add($"上层道路离地 {upper:F1} 米会建成路堤（挡土墙），下层道路无法从下面穿过：需要至少 {HeightRange.BridgeThreshold:F1} 米");
            if (p.RampWidth > legs[0].RoadWidth)
                errors.Add($"匝道宽 {p.RampWidth:F0} 米，比主线还宽：请减少匝道车道数");
            if (errors.Count > 0)
                return errors;
            if (p.LoopRadius > 0f && p.LoopRadius < kMinRadius)
                return new[] { $"环岛半径至少 {kMinRadius:F0} 米" };

            bool auto = p.LoopRadius <= 0f;
            p = WithRadius(p);
            var plan = PlanFor(p);
            errors.AddRange(plan.Errors);
            if (!plan.Fits)
            {
                float? fits = auto ? null : RadiusSearch.Smallest(r =>
                {
                    var trial = p.Clone();
                    trial.LoopRadius = r;
                    return new Plan(trial).Fits;
                }, p.LoopRadius + 5f, kMaxRadius);
                errors.Add(fits is float radius
                    ? $"环岛半径至少 {radius:F0} 米（主线桥下要放得下环岛，匝道和被交道路的路口之间要留出间距）"
                    : "这个交角下放不下：请把交角调得更接近 90°");
            }
            return errors;
        }

        public IReadOnlyList<RoadSegmentSpec> Generate(InterchangeParams p)
        {
            p = WithRadius(p);
            string key = p.Fingerprint();
            if (key != s_segmentsKey)
            {
                var built = new List<RoadSegmentSpec>();
                PlanFor(p).Build(built);
                InterchangeLayout.AddApproachSlopes(built, p);
                s_segments = built;
                s_segmentsKey = key;
            }
            return new List<RoadSegmentSpec>(s_segments!);
        }

        /// <summary>环岛半径：手动（LoopRadius &gt; 0）时就是它；自动（0）时取能放下的最小半径（5 米一档）。</summary>
        public static float ResolveRadius(InterchangeParams p)
        {
            if (p.LoopRadius > 0f)
                return p.LoopRadius;
            string key = p.Fingerprint();
            if (key == s_radiusKey)
                return s_radius;
            var trial = p.Clone();
            s_radius = RadiusSearch.Smallest(r =>
            {
                trial.LoopRadius = r;
                return new Plan(trial).Fits;
            }, kMinRadius, kMaxRadius) ?? kMaxRadius;
            s_radiusKey = key;
            return s_radius;
        }

        private static InterchangeParams WithRadius(InterchangeParams p)
        {
            if (p.LoopRadius > 0f)
                return p;
            var resolved = p.Clone();
            resolved.LoopRadius = ResolveRadius(p);
            return resolved;
        }

        // —— 缓存（参数不变时立交的局部形状不变，鼠标移动不重复计算）——
        private static string? s_planKey, s_radiusKey, s_segmentsKey;
        private static Plan? s_plan;
        private static float s_radius;
        private static List<RoadSegmentSpec>? s_segments;

        private static Plan PlanFor(InterchangeParams p)
        {
            string key = p.Fingerprint();
            if (key != s_planKey || s_plan == null)
            {
                s_plan = new Plan(p);
                s_planKey = key;
            }
            return s_plan;
        }

        /// <summary>直线段（RampPath 只有圆弧，用超大半径的圆弧代替）。</summary>
        private static RampArc Straight(Vec3 from, Vec3 heading, float length) => new RampArc(Flat0(from), heading, kStraight, length / kStraight);

        private static Vec3 At(Vec3 v, float y) => new Vec3(v.X, y, v.Z);

        private static Vec3 Rotate180(Vec3 v) => new Vec3(-v.X, v.Y, -v.Z);

        /// <summary>
        /// 按参数算出的 L 一侧：主线沿 V 行驶的那一幅及其两条匝道、环岛 L 侧一半、被交道路 L 侧一头；另一侧是它绕中心转 180°。
        /// 坐标：点 = L·横向 + V·纵向 t。环岛两头圆心在 (0, ∓a)，半径 R（中心线）。
        /// </summary>
        private sealed class Plan
        {
            private readonly InterchangeParams P;
            private readonly Vec3 V, L, C, crossSide;
            private readonly float hm, hc, om, R, a;
            private readonly bool mainElevated, ringUpper, ringElevated;
            /// <summary>匝道在环岛路口前保持环岛高度的长度：穿出环岛半幅（斜 60° 接入）之后再留 kTerminalFlat。</summary>
            private readonly float terminalFlat;
            public readonly List<string> Errors = new List<string>();
            public readonly bool Fits = true;

            // 环岛 L 侧一半：从南端 ringStart 沿 +V 方向绕到北端（= ringStart 转 180°）
            private readonly RampPath ring;
            private readonly Vec3 ringStart;
            /// <summary>环岛上的节点（沿 ring 的弧长），不含两端。</summary>
            private readonly List<float> ringNodes = new List<float>();
            private readonly float sOff, sOn;
            /// <summary>被交道路接环岛的路口：沿环岛的弧长、所在那幅的横向偏移（双向时 0）。</summary>
            private readonly List<(float S, float Lateral)> crossTerminals = new List<(float, float)>();

            private readonly PillarPlanner.Span southSpan, northSpan;
            private readonly float tDiverge, rampFlatStart;
            private readonly RampPath offRamp, onRamp;

            public Plan(InterchangeParams p)
            {
                P = p;
                var legs = p.Legs;
                float hand = p.RightHandTraffic ? 1f : -1f;
                float grade = p.MaxGrade * kGradeMargin;
                Vec3 m = Flat(legs[0].Direction), c = Flat(legs[1].Direction);
                V = m;
                L = Directions.RightOf(V) * hand;
                C = Directions.Dot(c, L) > 0f ? c : -c;
                crossSide = Directions.RightOf(C) * hand;
                hm = p.ElevationOf(legs[0]);
                hc = p.ElevationOf(legs[1]);
                om = legs[0].CarriagewayOffset;
                R = p.LoopRadius;
                a = p.RoundaboutRacetrack ? R * kRacetrackRatio : 0f;
                mainElevated = hm > hc && LevelPlanner.BandOf(hm) == ElevationBand.Elevated;
                // 环岛在上层（跨过主线）；环岛是高架（两层都在高架上时环岛在下层也是高架）
                ringUpper = hc > hm && LevelPlanner.BandOf(hc) == ElevationBand.Elevated;
                ringElevated = LevelPlanner.BandOf(hc) == ElevationBand.Elevated;
                float rw = p.RampWidth, wr = p.RingWidth, gap = p.RampSideGap;
                terminalFlat = wr / 2f / MathF.Sin(MathF.PI / 3f) + kTerminalFlat;
                float hw = legs[0].RoadWidth / 2f;
                float sepMain = hw + rw / 2f + gap;
                float rise = Math.Abs(hm - hc);
                if (wr <= 0f || R <= 0f)
                {
                    Fits = false;
                    return;
                }

                // —— 主线跨越环岛（南、北两处）：主线这一幅桥面横向 [om − hw, om + hw] 与环岛路面相交的纵向范围 ——
                float inner = R - wr / 2f, outer = R + wr / 2f;
                if (inner < om + hw + kPillarMargin)
                    Fits = false;
                float lo = -a - MathF.Sqrt(Math.Max(0f, outer * outer - (om - hw) * (om - hw)));
                float hi = -a - MathF.Sqrt(Math.Max(0f, inner * inner - (om + hw) * (om + hw)));
                southSpan = mainElevated
                    ? PillarPlanner.Plan((lo + hi) / 2f, (hi - lo) / 2f, 0f, legs[0].MiddlePillarSpacing, legs[0].PillarWidth, legs[0].MaxElevatedEdgeLength)
                    : new PillarPlanner.Span(lo - kPillarMargin, hi + kPillarMargin);
                if (southSpan.Error != null)
                    Errors.Add(southSpan.Error);
                if (southSpan.Hi + RampPath.MinSegment > -southSpan.Hi)
                    Fits = false;
                northSpan = new PillarPlanner.Span(-southSpan.Hi, -southSpan.Lo);

                // —— 环岛 L 侧一半 ——
                // 环岛在下层时从南端正中出发（节点在主线桥下，地面上没有立柱）；在上层时从主线占地之外 e 处出发，
                // 跨过主线的那段中间没有节点（立柱），北端在 e 处另加一个节点
                float e = legs[0].HalfExtent + kPillarMargin + 2f;
                if (ringUpper && e > R - 1f)
                    Fits = false;
                float theta0 = ringUpper ? -(float)Math.Acos(Math.Min(1f, e / R)) : -MathF.PI / 2f;
                // 北端终点 = 起点绕中心转 180°（横向 −e 或 0）
                float theta1 = ringUpper ? MathF.PI - (float)Math.Acos(Math.Min(1f, e / R)) : MathF.PI / 2f;
                Vec3 southC = V * -a, northC = V * a;
                Vec3 Circle(Vec3 center, float th) => center + (L * MathF.Cos(th) + V * MathF.Sin(th)) * R;
                Vec3 Tangent(float th) => L * -MathF.Sin(th) + V * MathF.Cos(th);
                ringStart = Circle(southC, theta0);
                var arcs = new List<RampArc>();
                var first = RampPath.Arcs1(ringStart, Tangent(theta0), southC - ringStart, R, -theta0).Arcs[0];
                arcs.Add(first);
                Vec3 at = first.End;
                if (a > 0f)
                {
                    var straight = Straight(at, V, 2f * a);
                    arcs.Add(straight);
                    at = straight.End;
                }
                arcs.Add(RampPath.Arcs1(at, V, northC - at, R, theta1).Arcs[0]);
                ring = RampPath.FromArcs(arcs.ToArray());
                float total = ring.Length;
                float SouthS(float th) => R * (th - theta0);
                float NorthS(float th) => R * -theta0 + 2f * a + R * th;

                // —— 匝道接环岛的位置：横向 R/2（与环岛行车方向成 60°）——
                float xP = R / 2f;
                if (xP < om + sepMain + kPillarMargin)
                    Fits = false;
                float thetaP = (float)Math.Acos(xP / R);
                sOff = SouthS(-thetaP);
                sOn = NorthS(thetaP);
                ringNodes.Add(sOff);
                ringNodes.Add(sOn);
                if (ringUpper)
                    ringNodes.Add(NorthS((float)Math.Acos(Math.Min(1f, e / R))));

                // —— 被交道路接环岛：双向一个路口，双幅每幅一个 ——
                var laterals = legs[1].Form == RoadForm.Dual
                    ? new[] { legs[1].CarriagewayOffset, -legs[1].CarriagewayOffset }
                    : new[] { 0f };
                foreach (float lateral in laterals)
                {
                    Vec3 offset = crossSide * lateral;
                    float F(float s) => DiamondTemplate.Cross2(ring.PointAt(s) - offset, C);
                    float? hit = null;
                    float step = 1f;
                    for (float s = sOff; s + step <= sOn && hit == null; s += step)
                    {
                        if (Math.Sign(F(s)) == Math.Sign(F(s + step)))
                            continue;
                        float x0 = s, x1 = s + step;
                        for (int k = 0; k < 30; k++)
                        {
                            float mid = (x0 + x1) / 2f;
                            if (Math.Sign(F(mid)) == Math.Sign(F(x0)))
                                x0 = mid;
                            else
                                x1 = mid;
                        }
                        hit = (x0 + x1) / 2f;
                    }
                    if (hit is float sc && Directions.Dot(ring.PointAt(sc), C) > 0f)
                        crossTerminals.Add((sc, lateral));
                    else
                        Fits = false;
                }
                ringNodes.AddRange(crossTerminals.Select(t => t.S));
                ringNodes.Sort();
                // 相邻路口之间要隔开：匝道路口与被交道路路口之间留出被交道路半宽 + 匝道半宽 + 一段
                float crossHalf = legs[1].RoadWidth / 2f;
                var all = new List<float> { 0f };
                all.AddRange(ringNodes);
                all.Add(total);
                for (int i = 0; i + 1 < all.Count; i++)
                {
                    bool crossA = crossTerminals.Any(t => t.S == all[i]), crossB = crossTerminals.Any(t => t.S == all[i + 1]);
                    float need = crossA && crossB ? RampPath.MinSegment
                        : crossA || crossB ? crossHalf + rw / 2f + RampPath.MinSegment
                        : rw / 2f + RampPath.MinSegment;
                    if (all[i + 1] - all[i] < need)
                        Fits = false;
                }

                // —— 驶出匝道：在 tDiverge 驶出主线，S 形横移到 xP，沿 V 直行到环岛 ——
                // 主线高架时 S 形两段都用小半径：横移量不大，大半径的 S 形要很久才与主线分开，并行重叠段会超过游戏切分长度
                float shift = xP - om;
                if (shift <= sepMain)
                {
                    Fits = false;
                    return;
                }
                // 主线高架时并行重叠段要短于游戏切分长度：80 米半径不够快就逐档减小（匝道离主线间距至少 3 米时，
                // 宽的街道单行道路要更急地分开）
                float r1 = 0f, r2 = 0f, theta = 0f, sAlong = 0f, sLength = 0f;
                for (float r = mainElevated ? kDivergeRadius : kSRadius; ; r -= kDivergeRadiusStep)
                {
                    r1 = r2 = r;
                    theta = (float)Math.Acos(Math.Max(-1f, 1f - shift / (r1 + r2)));
                    sAlong = (r1 + r2) * MathF.Sin(theta);
                    sLength = (r1 + r2) * theta;
                    // 与主线分开（横向 sepMain）的位置：第一段圆弧内，或第二段（往回转 φ 后横向 r1(1−cosθ) + r2(cos(θ−φ) − cosθ)）
                    float firstLateral = r1 * (1f - MathF.Cos(theta));
                    float clearAt = sepMain <= firstLateral
                        ? r1 * (float)Math.Acos(1f - sepMain / r1)
                        : r1 * theta + r2 * (theta - (float)Math.Acos(Math.Min(1f, MathF.Cos(theta) + (sepMain - firstLateral) / r2)));
                    rampFlatStart = clearAt + kFlatMargin;
                    if (!mainElevated || rampFlatStart <= legs[0].MaxElevatedEdgeLength - 5f || r - kDivergeRadiusStep < kMinDivergeRadius)
                        break;
                }
                if (mainElevated && rampFlatStart > legs[0].MaxElevatedEdgeLength - 5f)
                    Fits = false;
                Vec3 terminal = ring.PointAt(sOff);
                float tP = Directions.Dot(terminal, V);
                // S 形在环岛（外缘再加匝道半宽和间距）之外完成；主线在上层时驶出段要在主线跨越环岛的范围之前
                float clearOuter = outer + rw / 2f + gap;
                float tEnd = Math.Min(-a - MathF.Sqrt(Math.Max(0f, clearOuter * clearOuter - om * om)), tP - terminalFlat);
                if (mainElevated)
                    tEnd = Math.Min(tEnd, southSpan.Lo - RampPath.MinSegment - rampFlatStart + sAlong);
                float needRamp = rise / grade + rampFlatStart + terminalFlat;
                if (sLength + (tP - tEnd) < needRamp)
                    tEnd = tP - (needRamp - sLength);
                tDiverge = tEnd - sAlong;
                Vec3 divergePoint = V * tDiverge + L * om;
                var a1 = RampPath.Arcs1(divergePoint, V, L, r1, theta).Arcs[0];
                var a2 = new RampArc(a1.End, a1.EndHeading, r2, -Math.Sign(a1.Angle) * theta);
                offRamp = RampPath.FromArcs(a1, a2, Straight(a2.End, V, tP - tEnd));
                // 驶入匝道：沿 L 轴镜像（t → −t）再反向
                Vec3 Mirror(Vec3 v) => v - V * (2f * Directions.Dot(v, V));
                onRamp = RampPath.FromArcs(offRamp.Arcs.Select(arc => new RampArc(Mirror(arc.From), Mirror(arc.Heading), arc.Radius, -arc.Angle)).ToArray()).Reversed();
            }

            private Vec3 MainPoint(float t, float h) => V * t + L * om + new Vec3(0f, h, 0f);

            public void Build(List<RoadSegmentSpec> segments)
            {
                if (!Fits)
                    return;
                var p = P;
                var legs = p.Legs;

                // —— 主线：这一幅（沿 V），另一幅是它绕中心转 180° ——
                float tMerge = -tDiverge;
                float mainEnd = tMerge + legs[0].StubLength;
                var mainNodes = new List<float> { -mainEnd, tDiverge, southSpan.Lo, southSpan.Hi, northSpan.Lo, northSpan.Hi, tMerge, mainEnd };
                if (mainElevated)
                {
                    // 高架主线的路段超过游戏切分长度会被从中间切开，切点的立柱可能顶到旁边匝道的桥面：
                    // 自己按长度加节点，并且不放在匝道与主线并行重叠的那段里
                    float allowed = legs[0].MaxElevatedEdgeLength - 10f;
                    var sorted = mainNodes.OrderBy(t => t).ToList();
                    for (int i = 0; i + 1 < sorted.Count; i++)
                    {
                        float lo = sorted[i], hi = sorted[i + 1];
                        if (hi - lo <= allowed)
                            continue;
                        float freeLo = lo == tDiverge ? lo + rampFlatStart : lo, freeHi = hi == tMerge ? hi - rampFlatStart : hi;
                        int n = (int)Math.Ceiling((hi - lo) / allowed);
                        for (int k = 1; k < n; k++)
                            mainNodes.Add(Math.Max(freeLo, Math.Min(freeHi, lo + (hi - lo) * k / n)));
                    }
                }
                var side = new List<RoadSegmentSpec>();
                AddLine(side, "Main", RoadRole.MainRoad, legs[0].RoadPrefab, mainNodes, t => MainPoint(t, hm), new[] { tDiverge, tMerge, -mainEnd, mainEnd });

                // —— 环岛 L 侧一半：按节点分段；高架时每段不超过游戏切分长度 ——
                var cuts = new List<float> { 0f };
                cuts.AddRange(ringNodes);
                cuts.Add(ring.Length);
                if (ringElevated)
                {
                    float allowed = legs[1].MaxElevatedEdgeLength - 10f;
                    var extra = new List<float>();
                    for (int i = 0; i + 1 < cuts.Count; i++)
                    {
                        int n = (int)Math.Ceiling((cuts[i + 1] - cuts[i]) / allowed);
                        for (int k = 1; k < n; k++)
                            extra.Add(cuts[i] + (cuts[i + 1] - cuts[i]) * k / n);
                    }
                    cuts.AddRange(extra);
                    cuts.Sort();
                }
                Vec3 RingNode(float s) => s <= 0f ? At(ringStart, hc) : s >= ring.Length ? At(Rotate180(ringStart), hc) : At(ring.PointAt(s), hc);
                for (int i = 0; i + 1 < cuts.Count; i++)
                    ring.Slice(cuts[i], cuts[i + 1]).Emit(side, $"Ring{i + 1}-", RingNode(cuts[i]), RingNode(cuts[i + 1]), 0f, 0f, p.RingPrefab);

                // —— 匝道 ——
                offRamp.Emit(side, "RampOff", MainPoint(tDiverge, hm), RingNode(sOff), rampFlatStart, terminalFlat, p.RampPrefab);
                onRamp.Emit(side, "RampOn", RingNode(sOn), MainPoint(tMerge, hm), terminalFlat, rampFlatStart, p.RampPrefab);

                // —— 被交道路这一头：从环岛上的路口沿被交道路向外 ——
                float crossEnd = crossTerminals.Max(t => Directions.Dot(ring.PointAt(t.S), C)) + legs[1].StubLength;
                foreach (var (s, lateral) in crossTerminals)
                {
                    Vec3 node = RingNode(s);
                    Vec3 far = C * crossEnd + crossSide * lateral + new Vec3(0f, hc, 0f);
                    // 双幅时沿 +C 行驶的那幅（crossSide 一侧）驶离环岛，另一幅驶向环岛
                    var curve = lateral < 0f ? Bezier3.Line(far, node) : Bezier3.Line(node, far);
                    side.Add(new RoadSegmentSpec(lateral == 0f ? "Cross" : lateral > 0f ? "CrossOut" : "CrossIn", RoadRole.CrossRoad, curve, legs[1].RoadPrefab));
                }

                segments.AddRange(side);
                foreach (var s in side)
                {
                    var rotated = s.Transform(Rotate180);
                    segments.Add(new RoadSegmentSpec(s.Name + "B", rotated.Role, rotated.Curve, rotated.Prefab, rotated.StartNode, rotated.EndNode));
                }

                // 外部是双向道路的主线：两端过渡
                float hand = p.RightHandTraffic ? 1f : -1f;
                foreach (float end in new[] { 1f, -1f })
                {
                    LegSpec leg = end > 0f ? legs[0] : legs[2];
                    if (!leg.NeedsDualToTwoWayTransition)
                        continue;
                    Vec3 axis = V * end;
                    Vec3 lateral = Directions.RightOf(axis) * hand;
                    Vec3 Point(float along, float l) => axis * along + lateral * l + new Vec3(0f, hm, 0f);
                    // 两幅放得进外部双向道路的宽度时平行接入，否则斜向汇合
                    if (DiamondTemplate.FitsAligned(leg, om))
                        DiamondTemplate.AddAlignedEnd(segments, end > 0f ? "Main-endA" : "Main-endB", RoadRole.MainRoad, Point, mainEnd, om, leg.StubLength, leg.ExternalPrefab);
                    else
                        DiamondTemplate.AddTransition(segments, end > 0f ? "Main-endA" : "Main-endB", RoadRole.MainRoad, Point, +1, mainEnd, om,
                            mainEnd + leg.StubLength, p.LegTransitionAngle, leg.RoadPrefab, leg.ExternalPrefab);
                }
            }

            /// <summary>沿一条直线按位置把节点连成路段（相距不到 10 米的合并，优先保留 keep 里的节点）；点 at(位置) 给出坐标。</summary>
            private static void AddLine(List<RoadSegmentSpec> segments, string name, RoadRole role, string? prefab, List<float> nodes,
                Func<float, Vec3> at, IReadOnlyList<float> keep)
            {
                var sorted = new List<float>();
                foreach (float t in nodes.OrderBy(t => t))
                {
                    int near = sorted.FindIndex(k => Math.Abs(k - t) < RampPath.MinSegment);
                    if (near < 0)
                        sorted.Add(t);
                    else if (keep.Contains(t))
                        sorted[near] = t;
                }
                sorted.Sort();
                for (int i = 0; i + 1 < sorted.Count; i++)
                    segments.Add(new RoadSegmentSpec($"{name}-{i + 1}", role, Bezier3.Line(at(sorted[i]), at(sorted[i + 1])), prefab));
            }
        }
    }
}
