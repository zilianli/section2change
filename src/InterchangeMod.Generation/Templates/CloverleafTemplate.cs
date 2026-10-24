using System;
using System.Collections.Generic;
using System.Linq;
using InterchangeMod.Generation.Geometry;
using static InterchangeMod.Generation.Templates.Planar;

namespace InterchangeMod.Generation.Templates
{
    /// <summary>
    /// 全苜蓿叶（四向全互通）。主线（臂 0/2）和被交道路（臂 1/3）都是双幅，一条用桥跨过另一条（只有这一座桥）。
    /// 四个象限各一条环道（左转，270° 右转绕过去，升降一层）和一条外侧直连匝道（右转，同时与两条路相切，绕在环道外侧）：
    ///
    ///            │ ║
    ///      ╭───╮ │ ║ ╭───╮
    ///      │ ○ ╰─┤ ║─╯ ○ │        ○ = 环道
    ///  ════╪═════╪═╬═════╪════    主线（双幅）
    ///  ════╪═════╪═╬═════╪════
    ///      │ ○ ╭─┤ ║─╮ ○ │        外侧直连匝道
    ///      ╰───╯ │ ║ ╰───╯
    ///            │ ║  被交道路（双幅）
    ///
    /// 每一幅上的顺序（右侧通行）：外侧匝道驶出 → 本象限环道汇入 → 过桥 → 下一象限环道驶出 → 外侧匝道汇入；
    /// 桥两侧环道汇入与驶出之间是交织段（主线保持全宽）。
    /// 象限按“哪两幅之间右转”自动确定：外侧匝道从 A 幅右转到 B 幅，同一象限的环道从 B 幅左转（右转 270°）回到 A 幅。
    /// 斜交时道路沿真实方向，圆弧转角随交角变化。坐标单位米，立交中心为原点。
    /// </summary>
    public sealed class CloverleafTemplate : IInterchangeTemplate
    {
        /// <summary>两条道路的夹角范围（相对正交 ±55°，即 35°–145°）。</summary>
        private const float kMaxSkew = 55f;
        private const float kOppositeTolerance = 1f;
        private const float kGradeMargin = 0.98f;
        /// <summary>立柱离道路外缘的距离（米）。</summary>
        private const float kPillarMargin = 3f;
        /// <summary>臂端过渡到外部双向道路时，最外侧匝道节点到过渡段起点的距离（米）。</summary>
        private const float kTransitionGap = 30f;
        /// <summary>匝道两端小半径圆弧的半径（米）：并行重叠段短（也短于游戏切分高架路段的长度）。</summary>
        private const float kDivergeRadius = 80f;
        /// <summary>匝道与所接道路并行重叠的那段再多保持同高这么远（米）才开始升降（见喇叭形）。</summary>
        private const float kFlatMargin = 15f;
        private const float kMinLoopRadius = 30f;
        private const float kMaxLoopRadius = 250f;
        /// <summary>检查两条匝道互不相碰时沿路径的采样间距（米）。</summary>
        private const float kSampleStep = 4f;

        public InterchangeType Type => InterchangeType.Cloverleaf;

        public string DisplayName => "苜蓿叶";

        public int LegCount => 4;

        public IReadOnlyList<string> ValidateParams(InterchangeParams p)
        {
            var errors = p.ValidateLegsAndLevels(LegCount);
            if (errors.Count > 0)
                return errors;
            var legs = p.Legs;
            if (Directions.AngleBetween(legs[0].Angle, legs[2].Angle) < 180f - kOppositeTolerance
                || Directions.AngleBetween(legs[1].Angle, legs[3].Angle) < 180f - kOppositeTolerance)
                errors.Add("苜蓿叶要求两条道路都是直线（臂 0/2、臂 1/3 各自相对）");
            float crossing = Directions.AngleBetween(legs[0].Angle, legs[1].Angle);
            if (Math.Abs(crossing - 90f) > kMaxSkew)
                errors.Add($"两条道路夹角 {crossing:F0}°：苜蓿叶需要 {90f - kMaxSkew:F0}°–{90f + kMaxSkew:F0}°");
            if (legs[0].Form != RoadForm.Dual)
                errors.Add("苜蓿叶的主线需要是双幅（两条对向单行道路）：请选择单行的公路或把主线形式改为双幅");
            if (legs[1].Form != RoadForm.Dual)
                errors.Add("苜蓿叶的被交道路需要是双幅（外部是双向道路时在两端过渡）");

            float hm = p.ElevationOf(legs[0]), hc = p.ElevationOf(legs[1]);
            float gap = DiamondTemplate.RequiredGap(p);
            float upper = Math.Max(hm, hc);
            if (Math.Abs(hm - hc) < gap)
                errors.Add($"两条道路高差 {Math.Abs(hm - hc):F1} 米，至少需要 {gap:F1} 米净空");
            else if (LevelPlanner.BandOf(upper) != ElevationBand.Elevated)
                errors.Add($"上层道路离地 {upper:F1} 米会建成路堤（挡土墙），下层道路无法从下面穿过：需要至少 {HeightRange.BridgeThreshold:F1} 米");
            if (p.RampWidth > Math.Min(legs[0].RoadWidth, legs[1].RoadWidth))
                errors.Add($"匝道宽 {p.RampWidth:F0} 米，比主线或被交道路还宽：请减少匝道车道数");
            if (errors.Count > 0)
                return errors;
            if (p.LoopRadius > 0f && p.LoopRadius < kMinLoopRadius)
                return new[] { $"环道半径至少 {kMinLoopRadius:F0} 米" };

            bool auto = p.LoopRadius <= 0f;
            p = WithLoopRadius(p);
            var plan = PlanFor(p);
            errors.AddRange(plan.Errors);
            if (!plan.LoopsFit)
            {
                float? fits = auto ? null : RadiusSearch.Smallest(r =>
                {
                    var trial = p.Clone();
                    trial.LoopRadius = r;
                    return new Plan(trial, rampsToo: false).LoopsFit;
                }, p.LoopRadius + 5f, kMaxLoopRadius);
                errors.Add(fits is float radius
                    ? $"这个交角下环道半径至少 {radius:F0} 米（环道要接到跨线桥之外、互不相碰，升降坡度不超过上限；加大最大坡度可以更小）"
                    : "这个交角下环道放不下：请把交角调得更接近 90° 或加大最大坡度");
            }
            return errors;
        }

        public IReadOnlyList<RoadSegmentSpec> Generate(InterchangeParams p)
        {
            p = WithLoopRadius(p);
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

        // —— 缓存：鼠标移动时每帧都会重建预览，但参数不变时立交的局部形状不变，环道半径搜索和外侧匝道求解都只算一次 ——
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

        /// <summary>环道半径：手动（LoopRadius &gt; 0）时就是它；自动（0）时取四条环道都能放下的最小半径（5 米一档）。</summary>
        public static float ResolveLoopRadius(InterchangeParams p)
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
                return new Plan(trial, rampsToo: false).LoopsFit;
            }, kMinLoopRadius, kMaxLoopRadius) ?? kMaxLoopRadius;
            s_radiusKey = key;
            return s_radius;
        }

        private static InterchangeParams WithLoopRadius(InterchangeParams p)
        {
            if (p.LoopRadius > 0f)
                return p;
            var resolved = p.Clone();
            resolved.LoopRadius = ResolveLoopRadius(p);
            return resolved;
        }

        /// <summary>一条道路（主线或被交道路，双幅）。</summary>
        private sealed class Road
        {
            public Road(InterchangeParams p, int leg, int oppositeLeg, RoadRole role)
            {
                Leg = p.Legs[leg];
                Opposite = p.Legs[oppositeLeg];
                Axis = Flat(Leg.Direction);
                Offset = Leg.CarriagewayOffset;
                Height = p.ElevationOf(Leg);
                Role = role;
                Width = Leg.RoadWidth;
                Prefab = Leg.RoadPrefab;
                Tag = role == RoadRole.MainRoad ? "Main" : "Cross";
                Sep = Width / 2f + p.RampWidth / 2f + p.RampSideGap;
                extent = Leg.HalfExtent;
            }

            /// <summary>主线 main 的集散车道：在主线每一幅外侧，隔开 max(匝道间距, 2 米)，用匝道道路。</summary>
            public Road(InterchangeParams p, Road main)
            {
                Leg = main.Leg;
                Opposite = main.Opposite;
                Axis = main.Axis;
                Width = p.RampWidth;
                Offset = main.Offset + main.Width / 2f + Math.Max(p.RampSideGap, 2f) + Width / 2f;
                Height = main.Height;
                Role = RoadRole.MainRoad;
                Prefab = p.RampPrefab;
                Tag = "CD";
                Sep = Width / 2f + p.RampWidth / 2f + p.RampSideGap;
                extent = Offset + Width / 2f;
                main.extent = extent;   // 主线连同集散车道一起算占地（被交道路跨越时立柱要避开两者）
            }

            private float extent;
            public float Width { get; }
            public string? Prefab { get; }
            /// <summary>路段名前缀：Main / Cross / CD。</summary>
            public string Tag { get; }

            public LegSpec Leg { get; }
            /// <summary>沿 −Axis 的那条臂。</summary>
            public LegSpec Opposite { get; }
            /// <summary>沿臂 Leg 方向（指向外）的单位向量。</summary>
            public Vec3 Axis { get; }
            public float Offset { get; }
            public float Height { get; }
            public RoadRole Role { get; }
            /// <summary>匝道中心离这条路一幅中心多远才不再与它重叠。</summary>
            public float Sep { get; }
            public bool Elevated => LevelPlanner.BandOf(Height) == ElevationBand.Elevated;
            /// <summary>车道平衡：减车道段用的道路与向中央分隔带的平移（只有主线有）。</summary>
            public string? ReducedPrefab { get; set; }
            public float Shift { get; set; }
            /// <summary>匝道在这条路的节点处的横向位置（向外）：车道平衡时匝道外缘对齐全宽外缘（边缘对齐），否则 0。</summary>
            public float Drop { get; set; }
            public float HalfExtent => extent;
        }

        /// <summary>一幅（沿 Dir·Axis 行驶）。t 为沿行驶方向距中心的距离，lateral 为离道路中心线的距离（向这一幅那侧为正）。</summary>
        private readonly struct Line
        {
            public Line(Road road, float dir, float handSign)
            {
                Road = road;
                Dir = dir;
                V = road.Axis * dir;
                Side = Directions.RightOf(V) * handSign;
            }

            public Road Road { get; }
            public float Dir { get; }
            public Vec3 V { get; }
            public Vec3 Side { get; }
            public Vec3 Point(float t, float lateral, float h = 0f) => V * t + Side * lateral + new Vec3(0f, h, 0f);
            public string Name => Road.Tag + (Dir > 0f ? "+" : "-");
        }

        /// <summary>一个象限：外侧匝道从 A 幅右转到 B 幅，环道从 B 幅转 270° 回到 A 幅。</summary>
        private sealed class Quadrant
        {
            public Line A, B;
            public RampPath Loop;
            /// <summary>环道在 B 上驶出、在 A 上汇入的位置；两头并行重叠（保持所接道路高度）的长度。</summary>
            public float LoopOnB, LoopOnA, LoopFlatB, LoopFlatA;
            public RampPath Outer;
            /// <summary>外侧匝道在 A 上驶出、在 B 上汇入的位置；两头并行重叠的长度；半径。</summary>
            public float OuterOnA, OuterOnB, OuterFlatA, OuterFlatB, OuterRadius;

            // —— 贴环道（OuterHugsLoop）：环道在 SplitB、SplitA 两点之间与右转匝道合成一条双向匝道 Shared ——
            public bool Hug;
            /// <summary>环道的前段（B 驶出 → SplitB）、后段（SplitA → A 汇入）；右转匝道两头的连接弯（A → SplitA、SplitB → B）。</summary>
            public RampPath LoopIn, LoopOut, Shared, ConnA, ConnB;
            /// <summary>双向匝道两端的节点（在它中心线上，Y 为那里的高度）。</summary>
            public Vec3 SplitA, SplitB;
        }

        /// <summary>按参数算出的整体布置。</summary>
        private sealed class Plan
        {
            private readonly InterchangeParams P;
            private readonly float hand, grade;
            private readonly Road main, cross;
            /// <summary>集散车道（没有时为 null）。</summary>
            private readonly Road? cd;
            private readonly Line[] lines;
            /// <summary>集散车道两头接主线的 S 形连接段（沿主线方向的 t：驶出点、汇入点）。</summary>
            private readonly Dictionary<string, (float Diverge, float Merge, RampPath Out, RampPath In)> cdLinks =
                new Dictionary<string, (float, float, RampPath, RampPath)>();
            private readonly List<Quadrant> quads = new List<Quadrant>();
            private readonly Dictionary<string, PillarPlanner.Span> spans = new Dictionary<string, PillarPlanner.Span>();
            public readonly List<string> Errors = new List<string>();
            /// <summary>四条环道都能放下（接到跨线桥之外、坡度不超限、互不相碰、与高架并行段够短）。</summary>
            public readonly bool LoopsFit = true;

            /// <param name="rampsToo">false = 只算环道（找环道半径时用），不求外侧匝道。</param>
            public Plan(InterchangeParams p, bool rampsToo = true)
            {
                P = p;
                hand = p.RightHandTraffic ? 1f : -1f;
                grade = p.MaxGrade * kGradeMargin;
                main = new Road(p, 0, 2, RoadRole.MainRoad);
                cross = new Road(p, 1, 3, RoadRole.CrossRoad);
                if (p.MainReducedPrefab != null)
                {
                    main.ReducedPrefab = p.MainReducedPrefab;
                    main.Shift = p.MainDropShift;
                    main.Drop = Math.Max(0f, (main.Leg.RoadWidth - p.RampWidth) / 2f);
                }
                if (p.CollectorDistributor)
                    cd = new Road(p, main);
                var all = new List<Line> { new Line(main, 1f, hand), new Line(main, -1f, hand), new Line(cross, 1f, hand), new Line(cross, -1f, hand) };
                if (cd != null)
                {
                    all.Add(new Line(cd, 1f, hand));
                    all.Add(new Line(cd, -1f, hand));
                }
                lines = all.ToArray();
                foreach (var line in lines)
                {
                    var span = Span(line);
                    spans[line.Name] = span;
                    bool upper = line.Road.Height > Other(line.Road).Height;
                    if (upper && span.Error != null && !Errors.Contains(span.Error))
                        Errors.Add(span.Error);
                }

                // 象限：A 幅到 B 幅是“外侧”转向（右侧通行时右转）的那四对；有集散车道时匝道接在集散车道上而不是主线上
                var rampLines = lines.Where(l => cd != null ? l.Road != main : true).ToList();
                foreach (var a in rampLines)
                {
                    foreach (var b in rampLines)
                    {
                        if (a.Road == b.Road || (a.Road != cross) == (b.Road != cross) || DiamondTemplate.Cross2(a.V, b.V) * hand >= 0f)
                            continue;
                        quads.Add(new Quadrant { A = a, B = b });
                    }
                }

                // —— 环道：从 B 幅外切驶出，转 270°（斜交时随交角变化）后外切汇入 A 幅 ——
                float loopSign = -hand;
                float R = p.LoopRadius;
                foreach (var q in quads)
                {
                    q.Loop = RampPath.Turn(q.B.Point(0f, Lateral(q.B)), q.B.V, q.A.Point(0f, Lateral(q.A)), q.A.V, loopSign, R,
                        out q.LoopOnB, out q.LoopOnA);
                    q.LoopFlatB = Flat(R, q.B) + kFlatMargin;
                    q.LoopFlatA = Flat(R, q.A) + kFlatMargin;
                    float rise = Math.Abs(q.A.Road.Height - q.B.Road.Height);
                    bool fits = rise <= grade * (q.Loop.Length - q.LoopFlatA - q.LoopFlatB)
                        && q.LoopOnB >= spans[q.B.Name].Hi + RampPath.MinSegment
                        && q.LoopOnA <= spans[q.A.Name].Lo - RampPath.MinSegment
                        && ShortEnough(q.LoopFlatB, q.B) && ShortEnough(q.LoopFlatA, q.A);
                    if (!fits)
                        LoopsFit = false;
                }
                // 环道之间：只看离开所接道路之后的部分（相邻两条环道接在同一条路的两幅上，接口处本来就只隔着分隔带）
                var awaySamples = new Dictionary<Quadrant, Vec3[]>();
                foreach (var q in quads)
                {
                    loopSamples[q] = Samples(q.Loop);
                    awaySamples[q] = loopSamples[q].Where(v => Away(v, q.A) && Away(v, q.B)).ToArray();
                }
                for (int i = 0; i < quads.Count && LoopsFit; i++)
                    for (int j = i + 1; j < quads.Count; j++)
                        if (Distance(awaySamples[quads[i]], awaySamples[quads[j]]) < Clearance)
                            LoopsFit = false;
                if (!LoopsFit || !rampsToo)
                    return;

                // —— 右转匝道：默认是两头小半径圆弧、中间近乎直线的外侧大弧线，自动取能绕过环道外侧的最紧凑的那条；
                //     贴环道时与环道相邻的那段合成一条双向匝道 ——
                bool hug = p.OuterHugsLoop && p.SharedRampPrefab != null && p.SharedRampWidth > 0f;
                foreach (var q in quads)
                {
                    if (!hug)
                        SolveOuter(q, kMinLoopRadius);
                    else if (!SolveHug(q))
                        Errors.Add("贴环道的右转匝道放不下：请加大环道半径，或改回外侧大弧线");
                }
                if (cd != null)
                    SolveCollectors();
            }

            /// <summary>
            /// 集散车道两头：在所有匝道节点（含并行段）之前从主线驶出、之后汇回主线，用两段反向圆弧的 S 形横移过去。
            /// 主线高架时 S 形半径取小（整段短于游戏切分高架路段的长度，且中间不设节点：中间点的立柱会落在主线桥面上）。
            /// </summary>
            private void SolveCollectors()
            {
                var road = cd!;
                float shift = road.Offset - (main.Offset + main.Drop);
                float radius = main.Elevated ? kDivergeRadius : 200f;
                float theta = (float)Math.Acos(Math.Max(-1f, 1f - shift / (2f * radius)));
                float along = 2f * radius * MathF.Sin(theta);
                float spacing = Math.Max(RampPath.MinSegment, P.RampWidth + P.RampSideGap);
                foreach (var line in lines.Where(l => l.Road == road))
                {
                    var mainLine = new Line(main, line.Dir, hand);
                    // 集散车道上最早 / 最晚的匝道节点（含并行段）
                    float first = float.MaxValue, last = float.MinValue;
                    foreach (var q in quads)
                    {
                        if (q.A.Name == line.Name)
                        {
                            first = Math.Min(first, q.OuterOnA);
                            last = Math.Max(last, q.LoopOnA + q.LoopFlatA);
                        }
                        if (q.B.Name == line.Name)
                        {
                            first = Math.Min(first, q.LoopOnB - q.LoopFlatB);
                            last = Math.Max(last, q.OuterOnB);
                        }
                    }
                    float diverge = first - spacing - along, merge = last + spacing + along;
                    float toward = Math.Sign(Directions.Dot(Perp(line.V), line.Side));
                    RampPath S(Vec3 from, float firstTurn) => RampPath.FromArcs(
                        new RampArc(from, line.V, radius, firstTurn * theta),
                        new RampArc(new RampArc(from, line.V, radius, firstTurn * theta).End, Rot(line.V, firstTurn * theta), radius, -firstTurn * theta));
                    var outLink = S(Flat0(mainLine.Point(diverge, Lateral(mainLine))), toward);
                    var inLink = S(Flat0(line.Point(merge - along, road.Offset)), -toward);
                    cdLinks[line.Name] = (diverge, merge, outLink, inLink);
                }
            }

            private Road Other(Road road) => road == cross ? main : cross;

            /// <summary>点 v 离这一幅的横向距离超过匝道与它的重叠间距（即已离开这条路）。</summary>
            private static bool Away(Vec3 v, Line line) =>
                Math.Abs(Directions.Dot(v - line.Point(0f, line.Road.Offset), line.Side)) > line.Road.Sep;

            /// <summary>环道的采样点（算一次，求外侧匝道时反复用）。</summary>
            private readonly Dictionary<Quadrant, Vec3[]> loopSamples = new Dictionary<Quadrant, Vec3[]>();

            /// <summary>
            /// 两条匝道（中心线）之间至少的距离：一条匝道宽 + 匝道间距，再加两侧各 kPillarMargin。
            /// 两条都在半空时桥面护栏和立柱比路宽更占地，只留匝道间距游戏会报重叠。
            /// </summary>
            private float Clearance => P.RampWidth + P.RampSideGap + 2f * kPillarMargin;

            /// <summary>匝道接这一幅时的横向位置（边缘对齐时外移 Drop）。</summary>
            private static float Lateral(Line line) => line.Road.Offset + line.Road.Drop;

            /// <summary>半径 radius 的圆弧切入这一幅时，并行重叠（横向不足 Sep）的长度。</summary>
            private static float Flat(float radius, Line line)
            {
                float sep = Math.Max(1f, line.Road.Sep - line.Road.Drop);
                return radius * (float)Math.Acos(Math.Max(-1f, 1f - sep / radius));
            }

            /// <summary>接高架道路时，与它并行保持同高的那段要短于游戏切分高架路段的长度（否则切分点的立柱落在所接道路上）。</summary>
            private static bool ShortEnough(float flat, Line line) =>
                !line.Road.Elevated || flat < line.Road.Leg.MaxElevatedEdgeLength - 5f;

            /// <summary>这一幅跨越另一条道路的两个节点（沿 t）：在上层且是高架时按立柱规划，立柱避开另一条路的两幅。</summary>
            private PillarPlanner.Span Span(Line line)
            {
                Road other = Other(line.Road);
                Vec3 origin = line.Point(0f, line.Road.Offset);
                float sin = Math.Abs(DiamondTemplate.Cross2(line.V, other.Axis));
                float center = -DiamondTemplate.Cross2(origin, other.Axis) / DiamondTemplate.Cross2(line.V, other.Axis);
                float half = other.HalfExtent / sin;
                bool upper = line.Road.Height > other.Height && line.Road.Elevated;
                if (!upper)
                    return new PillarPlanner.Span(center - half - kPillarMargin, center + half + kPillarMargin);
                return PillarPlanner.Plan(center, half, other.Leg.MedianWidth / 2f / sin, line.Road.Leg.MiddlePillarSpacing,
                    line.Road.Leg.PillarWidth, line.Road.Leg.MaxElevatedEdgeLength);
            }

            /// <summary>
            /// 外侧匝道：从 rMin 起找满足约束的最小（中间段）半径（先倍增再二分），结果存进象限。
            /// 约束：在 A 上比环道汇入点更早驶出、在 B 上比环道驶出点更晚汇入（两头并行段之间至少隔开一段）；
            /// 与所有环道保持一条匝道宽加间距；升降坡度不超限；接高架道路那头用小半径圆弧。
            /// </summary>
            private void SolveOuter(Quadrant q, float rMin)
            {
                float rise = Math.Abs(q.A.Road.Height - q.B.Road.Height);
                float clearance = Clearance;
                float spacing = Math.Max(RampPath.MinSegment, P.RampWidth + P.RampSideGap);
                (bool Ok, RampPath Path, float OnA, float OnB, float FlatA, float FlatB) Try(float r)
                {
                    // 两头都用小半径圆弧（像普通匝道的端部），中间是大半径、近乎直线的斜段：外侧匝道这样切过环道外侧，
                    // 比单一大圆弧紧凑得多（单一圆弧要绕到环道外侧，半径约为环道的 6 倍以上）。r 是中间那段的半径
                    float rA = Math.Min(r, kDivergeRadius);
                    float rB = Math.Min(r, kDivergeRadius);
                    var path = RampPath.Fillet(q.A.Point(0f, Lateral(q.A)), q.A.V, q.B.Point(0f, Lateral(q.B)), q.B.V, rA, r, rB,
                        Math.Max(1f, q.A.Road.Sep - q.A.Road.Drop), Math.Max(1f, q.B.Road.Sep - q.B.Road.Drop),
                        out float onA, out float onB, out float flatA, out float flatB);
                    flatA += kFlatMargin;
                    flatB += kFlatMargin;
                    bool ok = onA + flatA + spacing <= q.LoopOnA - q.LoopFlatA
                        && q.LoopOnB + q.LoopFlatB + spacing <= onB - flatB
                        && rise <= grade * (path.Length - flatA - flatB)
                        && ShortEnough(flatA, q.A) && ShortEnough(flatB, q.B);
                    if (ok)
                    {
                        // 两条路附近（并行段内）由上面的先后次序保证分开，这里只看离开道路之后的部分
                        var points = Samples(path).Where(v => Away(v, q.A) && Away(v, q.B)).ToArray();
                        foreach (var other in quads)
                            if (ok && Distance(points, loopSamples[other]) < clearance)
                                ok = false;
                    }
                    return (ok, path, onA, onB, flatA, flatB);
                }

                float hi = rMin, lo = rMin;
                var result = Try(rMin);
                if (!result.Ok)
                {
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
                    result = Try(hi);
                }
                q.Outer = result.Path;
                q.OuterOnA = result.OnA;
                q.OuterOnB = result.OnB;
                q.OuterFlatA = result.FlatA;
                q.OuterFlatB = result.FlatB;
                q.OuterRadius = hi;
                if (!result.Ok)
                    Errors.Add("外侧匝道放不下：请调整交角、环道半径或最大坡度");
            }

            /// <summary>
            /// 贴环道的右转匝道：从 30 米起找连接弯的最小可用半径。双向匝道中心线在环道外侧 W/4（W = 双向匝道宽），
            /// 环道走它的内半幅、右转匝道走外半幅；右转匝道两头的连接弯与环道同向转（反向曲线），和外半幅中心线（半径 R + W/2）外切。
            /// </summary>
            private bool SolveHug(Quadrant q)
            {
                float? radius = RadiusSearch.Smallest(rt => TryHug(q, rt, apply: false), kMinLoopRadius, 300f);
                return radius is float rt && TryHug(q, rt, apply: true);
            }

            private bool TryHug(Quadrant q, float rt, bool apply)
            {
                var a = q.A;
                var b = q.B;
                RampArc loop = q.Loop.Arcs[0];
                Vec3 c = Flat0(loop.Center);
                float sign = Math.Sign(loop.Angle), total = Math.Abs(loop.Angle), R = loop.Radius;
                float w = P.SharedRampWidth, rc = R + w / 4f, ro = R + w / 2f;

                // 连接弯的圆心：在所接那幅右侧（与环道同向转）rt 处的平行线上，离环道圆心 ro + rt
                bool Touch(Line line, bool before, out float t, out Vec3 foot, out Vec3 center, out Vec3 touch)
                {
                    Vec3 offset = Perp(line.V) * (sign * rt);
                    Vec3 d0 = Flat0(line.Point(0f, Lateral(line))) + offset - c;
                    float half = Directions.Dot(d0, line.V), rest = Directions.Dot(d0, d0) - (ro + rt) * (ro + rt);
                    float disc = half * half - rest;
                    t = disc < 0f ? 0f : -half + (before ? -1f : 1f) * MathF.Sqrt(disc);
                    foot = Flat0(line.Point(t, Lateral(line)));
                    center = foot + offset;
                    touch = center + (c - center) * (rt / (ro + rt));
                    return disc >= 0f;
                }
                if (!Touch(a, true, out float tA, out Vec3 footA, out Vec3 centerA, out Vec3 touchA)
                    || !Touch(b, false, out float tB, out Vec3 footB, out Vec3 centerB, out Vec3 touchB))
                    return false;
                var connA = new RampArc(footA, a.V, rt, sign * RotAngle(footA - centerA, touchA - centerA, sign));
                Vec3 uB = Vec3.Normalize(touchB - c), uA = Vec3.Normalize(touchA - c);
                var connB = new RampArc(touchB, Perp(uB) * -sign, rt, sign * RotAngle(touchB - centerB, footB - centerB, sign));

                // 环道（从 B 驶出、按 sign 方向转）先经过 SplitB 再经过 SplitA
                Vec3 u0 = Vec3.Normalize(Flat0(loop.From) - c);
                float alphaB = RotAngle(u0, uB, sign), alphaA = RotAngle(u0, uA, sign);
                float length = R * total, sB = R * alphaB, sA = R * alphaA;
                if (!(alphaB < alphaA && alphaA < total) || alphaA - alphaB < 10f * Directions.DegToRad
                    || sB < q.LoopFlatB + RampPath.MinSegment || length - sA < q.LoopFlatA + RampPath.MinSegment)
                    return false;

                // 高度沿环道：两头水平段之外线性升降；双向匝道两端的高度取环道在那里的高度，右转匝道连接弯从所接道路升降到它
                float hA = a.Road.Height, hB = b.Road.Height;
                float from = q.LoopFlatB, to = length - q.LoopFlatA;
                float Height(float s) => s <= from ? hB : s >= to ? hA : hB + (hA - hB) * (s - from) / (to - from);
                float yB = Height(sB), yA = Height(sA);
                float flatA = Flat(rt, a) + kFlatMargin, flatB = Flat(rt, b) + kFlatMargin;
                float spacing = Math.Max(RampPath.MinSegment, P.RampWidth + P.RampSideGap);
                bool ok = flatA < connA.Length && flatB < connB.Length
                    && Math.Abs(hA - yA) <= grade * (connA.Length - flatA)
                    && Math.Abs(yB - hB) <= grade * (connB.Length - flatB)
                    && tA + flatA + spacing <= q.LoopOnA - q.LoopFlatA
                    && q.LoopOnB + q.LoopFlatB + spacing <= tB - flatB
                    && ShortEnough(flatA, a) && ShortEnough(flatB, b);
                var connPathA = RampPath.FromArcs(connA);
                var connPathB = RampPath.FromArcs(connB);
                foreach (var other in quads)
                {
                    if (!ok || other == q)
                        continue;
                    ok = Distance(Samples(connPathA), loopSamples[other]) >= Clearance && Distance(Samples(connPathB), loopSamples[other]) >= Clearance;
                }
                if (!ok || !apply)
                    return ok;

                q.Hug = true;
                q.OuterOnA = tA;
                q.OuterOnB = tB;
                q.OuterFlatA = flatA;
                q.OuterFlatB = flatB;
                q.OuterRadius = rt;
                q.ConnA = connPathA;
                q.ConnB = connPathB;
                q.LoopIn = RampPath.FromArcs(new RampArc(loop.From, loop.Heading, R, sign * alphaB));
                q.LoopOut = RampPath.FromArcs(new RampArc(loop.PointAt(sA), loop.HeadingAt(sA), R, sign * (total - alphaA)));
                q.SplitA = c + uA * rc + new Vec3(0f, yA, 0f);
                q.SplitB = c + uB * rc + new Vec3(0f, yB, 0f);
                // 双向匝道沿右转匝道的方向（与环道反向）从 SplitA 走到 SplitB
                q.Shared = RampPath.FromArcs(new RampArc(c + uA * rc, Perp(uA) * -sign, rc, -sign * (alphaA - alphaB)));
                return true;
            }

            /// <summary>两组采样点（俯视）之间最近的距离。</summary>
            private static float Distance(Vec3[] a, Vec3[] b)
            {
                float best = float.MaxValue;
                for (int i = 0; i < a.Length; i++)
                {
                    for (int j = 0; j < b.Length; j++)
                    {
                        float dx = a[i].X - b[j].X, dz = a[i].Z - b[j].Z;
                        float d = dx * dx + dz * dz;
                        if (d < best)
                            best = d;
                    }
                }
                return MathF.Sqrt(best);
            }

            /// <summary>路径按 kSampleStep 采样（俯视）。</summary>
            private static Vec3[] Samples(RampPath path)
            {
                var points = new List<Vec3>();
                foreach (var arc in path.Arcs)
                {
                    int count = Math.Max(2, (int)(arc.Length / kSampleStep));
                    for (int i = 0; i <= count; i++)
                        points.Add(arc.PointAt(arc.Length * i / count));
                }
                return points.ToArray();
            }

            public void Build(List<RoadSegmentSpec> segments)
            {
                if (!LoopsFit || quads.Any(q => q.Outer.Arcs == null && !q.Hug))
                    return;
                var p = P;
                // 每条路两端：最远的匝道节点再往外一段（外部双向道路时留出过渡段）
                var reach = new Dictionary<(Road, float), float>();
                foreach (var road in new[] { main, cross })
                    foreach (float end in new[] { 1f, -1f })
                        reach[(road, end)] = 0f;
                void Reach(Line line, float t)
                {
                    float axial = t * line.Dir;
                    var key = (line.Road, axial >= 0f ? 1f : -1f);
                    reach[key] = Math.Max(reach[key], Math.Abs(axial));
                }
                foreach (var q in quads)
                {
                    if (q.A.Road != cd)
                        Reach(q.A, q.OuterOnA);
                    if (q.B.Road != cd)
                        Reach(q.B, q.OuterOnB);
                }
                foreach (var link in cdLinks)
                {
                    var mainLine = lines.First(l => l.Road == main && l.Name == "Main" + link.Key.Substring(2));
                    Reach(mainLine, link.Value.Diverge);
                    Reach(mainLine, link.Value.Merge);
                }
                float End(Road road, float end)
                {
                    LegSpec leg = end > 0f ? road.Leg : road.Opposite;
                    return reach[(road, end)] + (leg.NeedsDualToTwoWayTransition ? kTransitionGap : leg.StubLength);
                }

                // —— 四幅道路：车道平衡（主线）——外侧匝道驶出到环道汇入、环道驶出到外侧匝道汇入之间减车道，交织段保持全宽 ——
                var firstSegment = new Dictionary<string, int>();
                var lastSegment = new Dictionary<string, int>();
                foreach (var line in lines)
                {
                    bool isCd = line.Road == cd;
                    var nodes = new List<(float T, bool Fixed)> { (spans[line.Name].Lo, false), (spans[line.Name].Hi, false) };
                    if (isCd)
                    {
                        var link = cdLinks[line.Name];
                        float along = link.Out.Length > 0f ? Directions.Dot(link.Out.End - link.Out.Start, line.V) : 0f;
                        nodes.Add((link.Diverge + along, true));
                        nodes.Add((link.Merge - along, true));
                    }
                    else
                    {
                        nodes.Add((-End(line.Road, -line.Dir), true));
                        nodes.Add((End(line.Road, line.Dir), true));
                    }
                    var ranges = new List<(float Lo, float Hi)>();
                    if (line.Road == main && cdLinks.TryGetValue("CD" + line.Name.Substring(4), out var mainLink))
                    {
                        // 主线只有集散车道的一处驶出、一处汇入，其间减车道
                        nodes.Add((mainLink.Diverge, true));
                        nodes.Add((mainLink.Merge, true));
                        ranges.Add((mainLink.Diverge, mainLink.Merge));
                    }
                    foreach (var q in quads)
                    {
                        if (q.A.Name == line.Name)
                        {
                            nodes.Add((q.OuterOnA, true));
                            nodes.Add((q.LoopOnA, true));
                            ranges.Add((q.OuterOnA, q.LoopOnA));
                        }
                        if (q.B.Name == line.Name)
                        {
                            nodes.Add((q.LoopOnB, true));
                            nodes.Add((q.OuterOnB, true));
                            ranges.Add((q.LoopOnB, q.OuterOnB));
                        }
                    }
                    firstSegment[line.Name] = segments.Count;
                    AddLine(segments, line, nodes, line.Road.ReducedPrefab != null ? ranges : new List<(float, float)>());
                    lastSegment[line.Name] = segments.Count - 1;
                }

                // —— 臂端：外部是双向道路时两幅过渡回去（两幅放得进双向道路宽度时边缘对齐接入同一个节点，否则斜向汇合）——
                foreach (var road in new[] { main, cross })
                {
                    foreach (float end in new[] { 1f, -1f })
                    {
                        LegSpec leg = end > 0f ? road.Leg : road.Opposite;
                        if (!leg.NeedsDualToTwoWayTransition)
                            continue;
                        float e = End(road, end);
                        Vec3 axis = road.Axis * end;
                        Vec3 side = Directions.RightOf(axis) * hand;
                        Vec3 Point(float along, float lateral) => axis * along + side * lateral + new Vec3(0f, road.Height, 0f);
                        bool aligned = leg.ExternalWidth > 0f
                            && 2f * road.Offset + leg.RoadWidth <= leg.ExternalWidth + TrumpetTemplate.kMinSplitMedian + 0.5f;
                        string outName = new Line(road, end, hand).Name, inName = new Line(road, -end, hand).Name;
                        if (aligned)
                        {
                            // 驶出这一端的那幅最后一段、驶入的那幅第一段接到同一个节点（曲线端点横移，边缘对齐）
                            Vec3 joint = Point(e, 0f);
                            int o = lastSegment[outName], i = firstSegment[inName];
                            segments[o] = new RoadSegmentSpec(segments[o].Name, segments[o].Role, segments[o].Curve, segments[o].Prefab, segments[o].StartNode, joint);
                            segments[i] = new RoadSegmentSpec(segments[i].Name, segments[i].Role, segments[i].Curve, segments[i].Prefab, joint, segments[i].EndNode);
                            segments.Add(new RoadSegmentSpec($"{outName}-tail", road.Role, Bezier3.Line(joint, Point(e + leg.StubLength, 0f)), leg.ExternalPrefab));
                        }
                        else
                            DiamondTemplate.AddTransition(segments, $"{outName}-end", road.Role, Point, +1, e, road.Offset,
                                e + leg.StubLength, p.LegTransitionAngle, leg.RoadPrefab, leg.ExternalPrefab);
                    }
                }

                // —— 集散车道两头的 S 形连接段（整段一节，不在中间设节点）——
                foreach (var pair in cdLinks)
                {
                    var line = lines.First(l => l.Name == pair.Key);
                    var mainLine = new Line(main, line.Dir, hand);
                    var (diverge, merge, outLink, inLink) = pair.Value;
                    float along = Directions.Dot(outLink.End - outLink.Start, line.V);
                    float h = main.Height;
                    outLink.Emit(segments, $"{pair.Key}in-", mainLine.Point(diverge, Lateral(mainLine), h), line.Point(diverge + along, cd!.Offset, h),
                        outLink.Length, 0f, cd.Prefab, startNode: mainLine.Point(diverge, main.Offset, h));
                    inLink.Emit(segments, $"{pair.Key}out-", line.Point(merge - along, cd.Offset, h), mainLine.Point(merge, Lateral(mainLine), h),
                        inLink.Length, 0f, cd.Prefab, endNode: mainLine.Point(merge, main.Offset, h));
                }

                // —— 匝道：在所接道路那头的曲线端点在外侧 Drop 处，节点是道路的节点（边缘对齐）——
                int index = 0;
                foreach (var q in quads)
                {
                    index++;
                    var a = q.A;
                    var b = q.B;
                    if (q.Hug)
                    {
                        EmitHug(segments, q, index);
                        continue;
                    }
                    q.Outer.Emit(segments, $"Outer{index}-", a.Point(q.OuterOnA, Lateral(a), a.Road.Height), b.Point(q.OuterOnB, Lateral(b), b.Road.Height),
                        q.OuterFlatA, q.OuterFlatB, p.RampPrefab,
                        startNode: a.Point(q.OuterOnA, a.Road.Offset, a.Road.Height), endNode: b.Point(q.OuterOnB, b.Road.Offset, b.Road.Height));
                    q.Loop.Emit(segments, $"Loop{index}-", b.Point(q.LoopOnB, Lateral(b), b.Road.Height), a.Point(q.LoopOnA, Lateral(a), a.Road.Height),
                        q.LoopFlatB, q.LoopFlatA, p.RampPrefab,
                        startNode: b.Point(q.LoopOnB, b.Road.Offset, b.Road.Height), endNode: a.Point(q.LoopOnA, a.Road.Offset, a.Road.Height));
                }
            }

            /// <summary>
            /// 贴环道的象限：环道前段、双向匝道、环道后段；右转匝道两头的连接弯。双向匝道两端的节点在它中心线上，
            /// 环道在内半幅（曲线端点向内 W/4）、右转匝道在外半幅（向外 W/4），边缘对齐接入。
            /// </summary>
            private void EmitHug(List<RoadSegmentSpec> segments, Quadrant q, int index)
            {
                var p = P;
                var a = q.A;
                var b = q.B;
                float hA = a.Road.Height, hB = b.Road.Height;
                Vec3 At(Vec3 v, float h) => new Vec3(v.X, h, v.Z);
                q.LoopIn.Emit(segments, $"Loop{index}a-", b.Point(q.LoopOnB, Lateral(b), hB), At(q.LoopIn.End, q.SplitB.Y),
                    q.LoopFlatB, 0f, p.RampPrefab, startNode: b.Point(q.LoopOnB, b.Road.Offset, hB), endNode: q.SplitB);
                q.Shared.Emit(segments, $"Shared{index}-", q.SplitA, q.SplitB, 0f, 0f, p.SharedRampPrefab);
                q.LoopOut.Emit(segments, $"Loop{index}b-", At(q.LoopOut.Start, q.SplitA.Y), a.Point(q.LoopOnA, Lateral(a), hA),
                    0f, q.LoopFlatA, p.RampPrefab, startNode: q.SplitA, endNode: a.Point(q.LoopOnA, a.Road.Offset, hA));
                q.ConnA.Emit(segments, $"Outer{index}a-", a.Point(q.OuterOnA, Lateral(a), hA), At(q.ConnA.End, q.SplitA.Y),
                    q.OuterFlatA, 0f, p.RampPrefab, startNode: a.Point(q.OuterOnA, a.Road.Offset, hA), endNode: q.SplitA);
                q.ConnB.Emit(segments, $"Outer{index}b-", At(q.ConnB.Start, q.SplitB.Y), b.Point(q.OuterOnB, Lateral(b), hB),
                    0f, q.OuterFlatB, p.RampPrefab, startNode: q.SplitB, endNode: b.Point(q.OuterOnB, b.Road.Offset, hB));
            }

            /// <summary>
            /// 一幅道路：按 t 排好的节点连成路段（相距不到 max(10 米, 路宽) 的节点合并，优先保留匝道节点和两端）。
            /// ranges 内（车道平衡的减车道段）改用减车道的道路，内部节点向中央分隔带平移 Shift，两头节点处曲线端点平移（边缘对齐）。
            /// </summary>
            private void AddLine(List<RoadSegmentSpec> segments, Line line, List<(float T, bool Fixed)> nodes, List<(float Lo, float Hi)> ranges)
            {
                Road road = line.Road;
                float merge = Math.Max(RampPath.MinSegment, road.Width);
                var keep = new List<(float T, bool Fixed)>();
                foreach (var node in nodes.OrderBy(n => n.T))
                {
                    int near = keep.FindIndex(k => Math.Abs(k.T - node.T) < merge);
                    if (near < 0)
                        keep.Add(node);
                    else if (node.Fixed && !keep[near].Fixed)
                        keep[near] = node;
                }
                keep = keep.OrderBy(k => k.T).ToList();

                bool Inside(float t) => ranges.Any(r => t > r.Lo + 1e-3f && t < r.Hi - 1e-3f);
                float inner = road.Offset - road.Shift;
                for (int i = 0; i + 1 < keep.Count; i++)
                {
                    float ta = keep[i].T, tb = keep[i + 1].T;
                    bool reduced = Inside((ta + tb) / 2f);
                    Vec3 nodeA = line.Point(ta, Inside(ta) ? inner : road.Offset, road.Height);
                    Vec3 nodeB = line.Point(tb, Inside(tb) ? inner : road.Offset, road.Height);
                    Vec3 curveA = reduced ? line.Point(ta, inner, road.Height) : nodeA;
                    Vec3 curveB = reduced ? line.Point(tb, inner, road.Height) : nodeB;
                    string? prefab = reduced ? road.ReducedPrefab : road.Prefab;
                    segments.Add(new RoadSegmentSpec($"{line.Name}-{i + 1}", road.Role, Bezier3.Line(curveA, curveB), prefab, nodeA, nodeB));
                }
            }
        }
    }
}
