using System;
using System.Collections.Generic;
using System.Linq;
using InterchangeMod.Generation.Geometry;
using static InterchangeMod.Generation.Templates.Planar;

namespace InterchangeMod.Generation.Templates
{
    /// <summary>
    /// 定向 T 型 / 半定向 T 型（三向全互通，右侧通行）。主线（臂 0/2，双幅）在基层直行，支路（臂 1，双幅）止于主线近侧，
    /// 不跨过主线。右转是两条与主线、支路都相切的直连匝道（同喇叭形）；两条左转匝道都以跨线桥跨过主线：
    ///
    ///   半定向（右出右进）                                 定向（左出左进）
    ///      ╭──────╮   ╭────╮                            ════╮                ╭════ 远侧幅（立交内向外横移）
    ///   ═══╪══════╪═══╯════╪═══  远侧幅                     ╰════════════════╯
    ///   ═══╪══════╪════════╪═══  近侧幅                          ╲    ╱  左转匝道在拉开的分隔带里升降
    ///      ║      ║                                      ═══════════╪══╪══════════ 近侧幅
    ///      ║      ║  支路                                           ║  ║  支路
    ///
    /// 半定向：远侧幅 → 支路的匝道从远侧幅右侧驶出，先向外再左转跨过整条主线，沿支路出向一幅落地；
    /// 支路 → 远侧幅的匝道从支路进向一幅爬升，跨过主线后左转，从右侧汇入远侧幅。
    /// 定向：两条匝道都在中央分隔带一侧进出远侧幅，只跨过近侧幅；远侧幅在立交范围内 S 形向外横移，留出匝道升降的空间。
    /// 两条左转匝道交叉一次：三层时支路 → 远侧幅那条在第三层；两层时它从下面穿过。
    ///
    /// 局部坐标系同喇叭形：X 沿近侧一幅的行驶方向，Y 指向远侧（支路在 −Y 一侧）；支路方向 f 指向主线，
    /// 出向一幅（沿 −f 行驶）在横向 +ob，进向一幅在 −ob。各匝道的半径、横移量等都从小到大搜索第一个可行的。
    /// </summary>
    public sealed class DirectionalTTemplate : IInterchangeTemplate
    {
        private const float kMaxSkew = 30f;
        private const float kOppositeTolerance = 1f;
        private const float kGradeMargin = 0.98f;
        private const float kPillarMargin = 3f;
        /// <summary>匝道与所接道路并行重叠的那段再多保持同高这么远（米）才开始升降。</summary>
        private const float kFlatMargin = 15f;
        /// <summary>匝道接支路那头保持支路高度的长度（米）。</summary>
        private const float kTerminalFlat = 15f;
        /// <summary>匝道在跨越其他道路的范围前后再多保持这一层高度的长度（米）。</summary>
        private const float kPlateauMargin = 8f;
        /// <summary>匝道离开高架主线的圆弧半径（米）：并行重叠段要短于游戏切分高架路段的长度。</summary>
        private const float kDivergeRadius = 80f;
        private const float kMinSegment = 10f;
        /// <summary>高架匝道每段的最大长度（米），短于游戏切分高架路段的 80 米。</summary>
        private const float kMaxElevatedPiece = 78f;
        /// <summary>立柱沿桥面方向的厚度（米）。</summary>
        private const float kPillarThickness = 3f;
        /// <summary>地面路段的最大长度（米）：地面道路游戏不切分；只是避免一段太长。并行汇合段要整段，不能在中间断开。</summary>
        private const float kMaxGroundPiece = 600f;
        /// <summary>一段路的最大转角（弧度，约 80°）。</summary>
        private const float kMaxPieceTurn = 1.4f;
        /// <summary>定向：远侧幅横移用的 S 形圆弧半径（米）。</summary>
        private const float kShiftRadius = 250f;
        /// <summary>直线段用超大半径的圆弧表示（RampPath 只有圆弧）。</summary>
        private const float kStraight = 100000f;
        /// <summary>直连匝道外端到支路外端或过渡段起点的距离（米）。</summary>
        private const float kTransitionGap = 30f;
        /// <summary>采样间距（米）。</summary>
        private const float kStep = 2f;
        /// <summary>立交内支路两幅（两条左转匝道沿它们升降）匝道边缘到中线的最小距离（米）：容下路堤边坡 / 挡土墙。</summary>
        private const float kEmbankmentClear = 8f;

        private readonly bool directional;

        /// <param name="directional">true：定向 T 型（左出左进）；false：半定向 T 型（右出右进）。</param>
        public DirectionalTTemplate(bool directional)
        {
            this.directional = directional;
        }

        public InterchangeType Type => directional ? InterchangeType.DirectionalT : InterchangeType.SemiDirectionalT;

        public string DisplayName => directional ? "定向 T 型" : "半定向 T 型";

        public int LegCount => 3;

        public IReadOnlyList<string> ValidateParams(InterchangeParams p)
        {
            var errors = p.ValidateLegsAndLevels(LegCount);
            if (errors.Count > 0)
                return errors;
            var legs = p.Legs;
            string name = DisplayName;
            if (!p.RightHandTraffic)
                errors.Add($"{name}目前只支持右侧通行");
            if (Directions.AngleBetween(legs[0].Angle, legs[2].Angle) < 180f - kOppositeTolerance)
                errors.Add($"{name}要求主线（臂 0/2）是直线");
            float crossing = Directions.AngleBetween(legs[0].Angle, legs[1].Angle);
            if (Math.Abs(crossing - 90f) > kMaxSkew)
                errors.Add($"支路与主线夹角 {crossing:F0}°：{name}需要 {90f - kMaxSkew:F0}°–{90f + kMaxSkew:F0}°");
            if (legs[0].Form != RoadForm.Dual)
                errors.Add($"{name}的主线需要是双幅（两条对向单行道路）：请选择单行的公路或把主线形式改为双幅");
            if (legs[1].Form != RoadForm.Dual)
                errors.Add($"{name}的支路需要是双幅（外部是双向道路时在支路外端过渡）");
            float hm = p.ElevationOf(legs[0]);
            if (Math.Abs(p.ElevationOf(legs[1]) - hm) > 0.5f)
                errors.Add($"{name}的支路需要与主线在同一层");
            // 只用于主线和支路同层的路口（地面或高架）：对准立体交叉（两条路原来不在同一高度）时不支持，请用喇叭形
            if (legs.Any(leg => leg.ExternalElevation is float e && Math.Abs(e - hm) > 2f))
                errors.Add($"{name}只用于主线和支路在同一高度的路口：这里两条路原来不在同一层（立体交叉），请改用喇叭形");
            float g = LevelGap(p);
            float upper = hm + g;
            if (g < 1f)
                errors.Add("层间高差太小");
            else if (LevelPlanner.BandOf(upper) != ElevationBand.Elevated)
                errors.Add($"跨线桥离地 {upper:F1} 米会建成路堤（挡土墙），下层道路无法从下面穿过：需要至少 {HeightRange.BridgeThreshold:F1} 米");
            if (p.DirectionalTwoLevel && LevelPlanner.BandOf(hm) == ElevationBand.Elevated)
                errors.Add("主线在高架上时不能下穿：请改为三层");
            if (p.RampWidth > legs[0].RoadWidth)
                errors.Add($"匝道宽 {p.RampWidth:F0} 米，比主线（{legs[0].RoadWidth:F0} 米）还宽：请减少匝道车道数");
            if (errors.Count > 0)
                return errors;
            var plan = PlanFor(p);
            errors.AddRange(plan.Errors);
            return errors;
        }

        public IReadOnlyList<RoadSegmentSpec> Generate(InterchangeParams p)
        {
            string key = AbsoluteKey(p);
            if (key != m_SegmentsKey)
            {
                var built = new List<RoadSegmentSpec>();
                var plan = PlanFor(p);
                plan.Build(built);
                // 布置按相对高度缓存：基层整体升降（对准道路时随地形变化）只把输出平移
                float shift = p.ElevationOf(p.Legs[0]) - plan.BaseHeight;
                if (Math.Abs(shift) > 1e-4f)
                    for (int i = 0; i < built.Count; i++)
                        built[i] = built[i].Transform(v => new Vec3(v.X, v.Y + shift, v.Z));
                InterchangeLayout.AddApproachSlopes(built, p);
                m_Segments = built;
                m_SegmentsKey = key;
            }
            return new List<RoadSegmentSpec>(m_Segments!);
        }

        /// <summary>
        /// 缓存键：参数指纹去掉本模板用不到的匝道长度、分流角度。工具在预览失败时会逐档加长匝道重试，
        /// 这些重试共用一次计算（否则每帧都要把整个搜索重跑几十遍）。
        /// </summary>
        private static string AbsoluteKey(InterchangeParams p)
        {
            var q = p.Clone();
            q.RampLength = 0f;
            q.RampDivergeAngle = 0f;
            return q.Fingerprint();
        }

        /// <summary>
        /// 布置的缓存键：高度按相对基层算（对准道路时基层随地形连续变化，否则每帧都要重新搜索），
        /// 另加各层所在的高度段（高架 / 地面 / 隧道会改变布置）和两层时的下穿深度（按绝对的隧道深度）。
        /// </summary>
        private static string Key(InterchangeParams p)
        {
            var q = p.Clone();
            q.RampLength = 0f;
            q.RampDivergeAngle = 0f;
            float h0 = q.Levels[0].Elevation, g = LevelGap(q);
            foreach (var level in q.Levels)
                level.Elevation -= h0;
            foreach (var leg in q.Legs)
                if (leg.ExternalElevation is float e)
                    leg.ExternalElevation = e - h0;
            float under = Math.Min(h0 - g, HeightRange.TunnelThreshold) - h0;
            return q.Fingerprint() + $"|{LevelPlanner.BandOf(h0)}|{LevelPlanner.BandOf(h0 + g)}|{LevelPlanner.BandOf(h0 + 2f * g)}|{under:F2}";
        }

        /// <summary>层间高差：基层（主线、支路）到第一层跨线桥。</summary>
        private static float LevelGap(InterchangeParams p) =>
            p.Levels.Count > 1 ? p.Levels[1].Elevation - p.Levels[0].Elevation : 0f;

        // —— 缓存（两种 T 型各一个模板实例，各自缓存）——
        private string? m_PlanKey, m_SegmentsKey;
        private Plan? m_Plan;
        private List<RoadSegmentSpec>? m_Segments;

        private Plan PlanFor(InterchangeParams p)
        {
            string key = Key(p);
            if (key != m_PlanKey || m_Plan == null)
            {
                m_Plan = new Plan(p, directional);
                m_PlanKey = key;
            }
            return m_Plan;
        }

        private static RampArc Straight(Vec3 from, Vec3 heading, float length) => new RampArc(Flat0(from), Flat(heading), kStraight, length / kStraight);

        /// <summary>一条道路的俯视采样点（带高度）和宽度，用于检查与匝道的平面重叠和净空。</summary>
        private sealed class Strip
        {
            public Strip(string name, float width, List<Vec3> points)
            {
                Name = name;
                Width = width;
                Points = points;
                minX = points.Min(v => v.X);
                maxX = points.Max(v => v.X);
                minZ = points.Min(v => v.Z);
                maxZ = points.Max(v => v.Z);
            }

            private readonly float minX, maxX, minZ, maxZ;

            public string Name { get; }
            public float Width { get; }
            public List<Vec3> Points { get; }

            /// <summary>到 v 的俯视距离（按采样点）。</summary>
            public float DistanceTo(Vec3 v, out float height)
            {
                height = 0f;
                // 离包围盒很远时不必逐点算（调用方只关心几十米以内）
                float bx = Math.Max(0f, Math.Max(minX - v.X, v.X - maxX)), bz = Math.Max(0f, Math.Max(minZ - v.Z, v.Z - maxZ));
                if (bx > 40f || bz > 40f)
                    return Math.Max(bx, bz);
                float best = float.MaxValue;
                foreach (var q in Points)
                {
                    float dx = q.X - v.X, dz = q.Z - v.Z, d = dx * dx + dz * dz;
                    if (d < best)
                    {
                        best = d;
                        height = q.Y;
                    }
                }
                return MathF.Sqrt(best);
            }
        }

        /// <summary>一条左转匝道：路径、两头水平段、跨越段（保持这一层高度的范围）、断点和断点高度。</summary>
        private sealed class Flyover
        {
            public RampPath Path;
            public float FlatStart, FlatEnd, Level, H0, H1, PlateauStart, PlateauEnd;
            public bool HasPlateau;
            public List<float> Cuts = new List<float>();

            public float Length => Path.Length;

            public float HeightAt(float s)
            {
                float total = Length;
                if (!HasPlateau)
                {
                    float from = FlatStart, to = total - FlatEnd;
                    return s <= from ? H0 : s >= to ? H1 : H0 + (H1 - H0) * (s - from) / Math.Max(1e-3f, to - from);
                }
                if (s <= FlatStart)
                    return H0;
                if (s < PlateauStart)
                    return H0 + (Level - H0) * (s - FlatStart) / Math.Max(1e-3f, PlateauStart - FlatStart);
                if (s <= PlateauEnd)
                    return Level;
                float end = total - FlatEnd;
                if (s < end)
                    return Level + (H1 - Level) * (s - PlateauEnd) / Math.Max(1e-3f, end - PlateauEnd);
                return H1;
            }

            public List<Vec3> Samples()
            {
                var list = new List<Vec3>();
                int n = Math.Max(1, (int)(Length / kStep));
                for (int i = 0; i <= n; i++)
                {
                    float s = Length * i / n;
                    Vec3 v = Path.PointAt(s);
                    v.Y = HeightAt(s);
                    list.Add(v);
                }
                return list;
            }
        }

        private sealed class Plan
        {
            private readonly InterchangeParams P;
            private readonly bool directional;
            public readonly Vec3 X, Y;

            /// <summary>建立布置时的基层高度（缓存的布置用于别的基层时，输出按差值平移）。</summary>
            public float BaseHeight => hm;
            private readonly Vec3 f, n;
            private readonly float hm, g, levelA, levelB, om, ob, obi, rw, mainW, branchW, grade, sepMain, sepBranch, gap;
            private readonly bool mainElevated;
            public readonly List<string> Errors = new List<string>();

            /// <summary>定向：远侧幅横移量（米）；半定向为 0。</summary>
            private float shift;
            private Flyover? a, b;
            private RampPath nearOut, nearIn;
            private float nodeDistance, outFlatMain, outFlatBranch, inFlatMain, inFlatBranch;

            public Plan(InterchangeParams p, bool directional)
            {
                P = p;
                this.directional = directional;
                var legs = p.Legs;
                Vec3 main = legs[0].Direction, branch = legs[1].Direction;
                Vec3 right = Directions.RightOf(main);
                Vec3 near = Directions.Dot(branch, right) > 0f ? right : -right;
                X = Directions.Dot(Directions.RightOf(main), near) > 0f ? main : -main;
                Y = -near;
                f = -Flat(branch);
                n = Perp(f);
                if (Directions.Dot(n, X) > 0f)
                    n = -n;
                hm = p.ElevationOf(legs[0]);
                g = LevelGap(p);
                levelA = hm + g;
                // 两层：下穿段要是完全的隧道（路堑是开口的沟槽，上面的道路过不去），至少到隧道深度
                levelB = p.DirectionalTwoLevel ? Math.Min(hm - g, HeightRange.TunnelThreshold) : hm + 2f * g;
                // 两幅主线之间至少留 kMinSplitMedian（对准的原有双幅道路分隔带可能量出 0 米，两幅贴在一起游戏报重叠）；臂端接回时允许几米偏差
                om = Math.Max(legs[0].CarriagewayOffset, legs[0].RoadWidth / 2f + TrumpetTemplate.kMinSplitMedian / 2f);
                ob = legs[1].CarriagewayOffset;
                rw = p.RampWidth;
                // 立交内支路两幅向外拉开：两条左转匝道沿支路两幅升降，离地 0 ~ 8.5 米时游戏做路堤边坡 / 挡土墙，占地比路面宽，
                // 两条匝道之间至少隔开 2 × kEmbankmentClear；支路外端再 S 形并回原来的分隔带
                obi = Math.Max(ob, rw / 2f + kEmbankmentClear);
                mainW = legs[0].RoadWidth;
                branchW = legs[1].RoadWidth;
                gap = p.RampSideGap;
                grade = p.MaxGrade * kGradeMargin;
                sepMain = mainW / 2f + rw / 2f + gap;
                sepBranch = branchW / 2f + rw / 2f + gap;
                mainElevated = LevelPlanner.BandOf(hm) == ElevationBand.Elevated;

                SolveLeftTurns();
                if (a == null || b == null)
                {
                    Errors.Add("左转匝道放不下：请加大最大坡度、减小层间高差或把交角调得更接近 90°");
                    return;
                }
                if (!SolveRightTurns())
                    Errors.Add("右转直连匝道放不下");
                else if (!PlanCuts(a, "A") || !PlanCuts(b, "B"))
                    Errors.Add("跨线桥的立柱无法避开下面的道路：请把交角调得更接近 90° 或改变层间高差");
            }

            public bool Fits => Errors.Count == 0;

            private Vec3 Point(float x, float y, float h = 0f) => X * x + Y * y + new Vec3(0f, h, 0f);

            /// <summary>支路上的点：沿 f 距中心 u 米（支路一侧为负），横向 lateral（正 = 出向一幅那侧）。</summary>
            private Vec3 B(float u, float lateral, float h = 0f) => f * u + n * lateral + new Vec3(0f, h, 0f);

            private float FarY => om + shift;

            /// <summary>
            /// 支路节点处两条匝道并成支路一幅：左转匝道（沿 ±obi）外缘对齐支路靠分隔带一侧的边缘，右转匝道外缘对齐支路外侧边缘，
            /// 两者曲线端点相对节点各横移 EdgeBranch（节点在支路这一幅的中线 ±(obi + EdgeBranch) 上）。
            /// </summary>
            private float EdgeBranch => Math.Max(0f, (branchW - rw) / 2f);

            /// <summary>立交内支路这一幅中线的横向位置。</summary>
            private float Obc => obi + EdgeBranch;

            /// <summary>右转匝道接支路那头所沿的线（支路外侧那半）。</summary>
            private float ObRight => obi + 2f * EdgeBranch;

            /// <summary>右转匝道离开左转匝道（中线相距 2 × EdgeBranch）还要横移多少才不再重叠；高架时再留立柱余量。</summary>
            private float SepRight => Math.Max(1f, rw + gap + (mainElevated ? kPillarMargin : 0f) - 2f * EdgeBranch);

            /// <summary>
            /// 车道平衡时匝道在主线节点处的横向位置（向匝道一侧）：匝道外缘对齐主线全宽的外缘，与减车道后的主线并排接到同一个节点
            /// （边缘对齐，平行进出，不斜插主线中心）；没有车道平衡时为 0。
            /// </summary>
            private float EdgeOffset => P.MainReducedPrefab != null ? Math.Max(0f, (mainW - rw) / 2f) : 0f;

            /// <summary>匝道离开 / 接入主线时还要横移多少才与主线不再重叠。</summary>
            private float SepAttach => Math.Max(1f, sepMain - EdgeOffset);

            /// <summary>左转匝道在远侧幅上的接入线：半定向在外侧（+Y），定向在中央分隔带一侧（−Y）。</summary>
            private float FarAttachY => directional ? FarY - EdgeOffset : FarY + EdgeOffset;

            /// <summary>远侧幅减车道段相对远侧幅中线的横移：向不接匝道的那一侧（保持直行车道边缘对齐）。</summary>
            private float FarDropY => directional ? P.MainDropShift : -P.MainDropShift;

            // —— 匝道与主线的平面关系（主线两幅都是直线；定向时远侧幅在立交范围内横移到 FarY）——

            /// <summary>v 是否压在主线某一幅上（俯视，留半个匝道间距）。</summary>
            private bool OverMain(Vec3 v)
            {
                float y = Directions.Dot(v, Y), reach = (rw + mainW) / 2f + gap * 0.5f;
                return Math.Abs(y + om) < reach || Math.Abs(y - FarY) < reach;
            }

            /// <summary>
            /// 给定路径和两头水平段，求跨越段（压在主线或 others 上的范围前后各加 kPlateauMargin）和高度；坡长不够返回 null。
            /// extendStart / extendEnd：路径首 / 尾是一段可以伸缩的直线（这里给的是足够长的试探长度），按需要截短。
            /// </summary>
            private Flyover? Fit(RampPath path, float flatStart, float flatEnd, float level, IEnumerable<Strip> others, bool extendStart, bool extendEnd)
            {
                var strips = others.ToList();
                float total = path.Length;
                float first = -1f, last = -1f;
                int count = Math.Max(1, (int)(total / kStep));
                for (int i = 0; i <= count; i++)
                {
                    float s = total * i / count;
                    if (s < flatStart || s > total - flatEnd)
                        continue;
                    Vec3 v = path.PointAt(s);
                    bool hit = OverMain(v) || strips.Any(st => st.DistanceTo(v, out _) < (rw + st.Width) / 2f + gap * 0.5f);
                    if (!hit)
                        continue;
                    if (first < 0f)
                        first = s;
                    last = s;
                }
                var fly = new Flyover { Path = path, FlatStart = flatStart, FlatEnd = flatEnd, Level = level, H0 = hm, H1 = hm };
                if (first < 0f)
                    return fly;
                float rise = Math.Abs(level - hm) / grade;
                float p0 = first - kPlateauMargin, p1 = last + kPlateauMargin;
                float slackStart = p0 - flatStart - rise, slackEnd = total - flatEnd - p1 - rise;
                if (slackStart < -0.01f || slackEnd < -0.01f)
                    return null;
                // 截短可以伸缩的首尾直线：只留升降需要的长度
                if (extendStart && slackStart > 0f)
                {
                    var arcs = path.Arcs.ToList();
                    var s0 = arcs[0];
                    float keep = Math.Max(1f, s0.Length - slackStart);
                    float cut = s0.Length - keep;
                    arcs[0] = new RampArc(s0.PointAt(cut), s0.Heading, s0.Radius, Math.Sign(s0.Angle) * keep / s0.Radius);
                    path = RampPath.FromArcs(arcs.ToArray());
                    p0 -= cut;
                    p1 -= cut;
                }
                if (extendEnd && slackEnd > 0f)
                {
                    var arcs = path.Arcs.ToList();
                    var s1 = arcs[arcs.Count - 1];
                    float keep = Math.Max(1f, s1.Length - slackEnd);
                    arcs[arcs.Count - 1] = new RampArc(s1.From, s1.Heading, s1.Radius, Math.Sign(s1.Angle) * keep / s1.Radius);
                    path = RampPath.FromArcs(arcs.ToArray());
                }
                fly.Path = path;
                fly.HasPlateau = true;
                fly.PlateauStart = p0;
                fly.PlateauEnd = p1;
                return fly;
            }

            /// <summary>匝道离开 / 接入一条直线道路时，并行重叠（横向不足 sep）的长度（沿路径，按采样）。</summary>
            private static float Overlap(RampPath path, Vec3 lineOrigin, Vec3 lineDir, float sep, bool fromStart)
            {
                float total = path.Length;
                for (float s = 0f; s <= total; s += 0.5f)
                {
                    Vec3 v = path.PointAt(fromStart ? s : total - s);
                    if (Math.Abs(DiamondTemplate.Cross2(v - Flat0(lineOrigin), Flat(lineDir))) >= sep)
                        return s;
                }
                return total;
            }

            /// <summary>
            /// 左转匝道（转弯半径 r）沿主线方向直行（升降）的那条线的横向位置：定向在拉开的中央分隔带里（左出左进），离远侧幅一个匝道间距
            /// 加立柱余量；半定向在远侧幅外侧（右出右进），再往外一个转弯半径，转弯在跨过主线之前转完、垂直跨过主线（跨越段最短，立柱好放）。
            /// </summary>
            private float RunY(float r) => directional ? FarY - sepMain - kPillarMargin : FarY + sepMain + kPillarMargin + r;
            // 定向时 shift 已按半径加宽（见 SolveLeftTurns），RunY 离近侧幅正好一个转弯半径加匝道间距和立柱余量

            /// <summary>离开 / 接入远侧幅的 S 形：从 from 沿 heading 横移 d 到 side 一侧，两段反向圆弧。</summary>
            private static (RampArc First, RampArc Second) SCurve(Vec3 from, Vec3 heading, Vec3 side, float d, float r)
            {
                float theta = (float)Math.Acos(Math.Max(-1f, 1f - d / (2f * r)));
                float sign = DiamondTemplate.Cross2(heading, side) >= 0f ? 1f : -1f;
                var first = new RampArc(Flat0(from), Flat(heading), r, sign * theta);
                var second = new RampArc(first.End, first.EndHeading, r, -sign * theta);
                return (first, second);
            }

            private float SRadius => mainElevated ? kDivergeRadius : 120f;

            /// <summary>
            /// 远侧幅 → 支路出向一幅的左转匝道 A（第二层）：S 形离开远侧幅到 RunY → 沿 −X 直行 run（升到桥面）→ 左转半径 r 到支路方向 →
            /// 沿支路出向一幅直行下降落地。run 按爬升需要求出，末尾直线按下降需要截短。
            /// </summary>
            private Flyover? BuildA(float r)
            {
                const float probe = 800f;
                float runY = RunY(r), d = Math.Abs(runY - FarAttachY), sr = SRadius;
                // 左转圆弧：从 (xs, RunY) 沿 −X 出发，终点落在支路出向一幅上
                var shape = RampPath.TurnTo(Vec3.Zero, -X, -Y, r, -f);
                Vec3 b0 = Flat0(B(0f, obi));
                float xs = -DiamondTemplate.Cross2(Y * runY + shape.End - b0, f) / DiamondTemplate.Cross2(X, f);
                Vec3 side = directional ? -Y : Y;
                RampPath Path(float run)
                {
                    float along = 2f * sr * MathF.Sin((float)Math.Acos(Math.Max(-1f, 1f - d / (2f * sr))));
                    Vec3 start = Point(xs + run + along, FarAttachY);
                    var (s1, s2) = SCurve(start, -X, side, d, sr);
                    var straight = Straight(s2.End, -X, run);
                    var turn = RampPath.TurnTo(straight.End, -X, -Y, r, -f);
                    return RampPath.FromArcs(s1, s2, straight, turn, Straight(turn.End, -f, probe));
                }
                Vec3 farOrigin = Point(0f, FarAttachY);
                var trial = Path(1f);
                float flat = Overlap(trial, farOrigin, -X, Math.Min(d, SepAttach) - 0.5f, true) + kFlatMargin;
                if (mainElevated && flat > P.Legs[0].MaxElevatedEdgeLength - 5f)
                    return null;
                var (first, _) = Marks(trial, flat, kTerminalFlat, Array.Empty<Strip>());
                if (first < 0f)
                    return null;
                float need = Math.Abs(levelA - hm) / grade - (first - kPlateauMargin - flat);
                var path = Path(1f + Math.Max(0f, need) + 1f);
                var fly = Fit(path, flat, kTerminalFlat, levelA, Array.Empty<Strip>(), false, true);
                // 落地点要在主线之外
                return fly != null && Directions.Dot(fly.Path.End, f) < -(om + mainW) ? fly : null;
            }

            /// <summary>
            /// 支路进向一幅 → 远侧幅的左转匝道 B（三层时第三层，两层时下穿）：沿支路进向一幅直行升降 → 左转半径 r 到 −X、到达 RunY →
            /// 沿 −X 直行 run（回到主线高度）→ S 形接入远侧幅。起点直线按升降需要截短，run 按下降需要求出。
            /// </summary>
            private Flyover? BuildB(float r, Flyover a, Strip aStrip)
            {
                const float probe = 800f;
                float runY = RunY(r), d = Math.Abs(runY - FarAttachY), sr = SRadius;
                var shape = RampPath.TurnTo(Vec3.Zero, f, -Directions.RightOf(f), r, -X);
                Vec3 b0 = Flat0(B(0f, -obi));
                float u = (runY - Directions.Dot(b0 + shape.End, Y)) / Directions.Dot(f, Y);
                Vec3 arcStart = b0 + f * u;
                Vec3 side = directional ? Y : -Y;
                RampPath Path(float run)
                {
                    var lead = Straight(arcStart - f * probe, f, probe);
                    var turn = RampPath.TurnTo(arcStart, f, -Directions.RightOf(f), r, -X);
                    var straight = Straight(turn.End, -X, run);
                    var (s1, s2) = SCurve(straight.End, -X, side, d, sr);
                    return RampPath.FromArcs(lead, turn, straight, s1, s2);
                }
                Vec3 farOrigin = Point(0f, FarAttachY);
                var trial = Path(1f);
                float flat = Overlap(trial, farOrigin, -X, Math.Min(d, SepAttach) - 0.5f, false) + kFlatMargin;
                if (mainElevated && flat > P.Legs[0].MaxElevatedEdgeLength - 5f)
                    return null;
                var (_, last) = Marks(trial, kTerminalFlat, flat, new[] { aStrip });
                if (last < 0f)
                    return null;
                float need = Math.Abs(levelB - hm) / grade - (trial.Length - flat - (last + kPlateauMargin));
                var path = Path(1f + Math.Max(0f, need) + 1f);
                var fly = Fit(path, kTerminalFlat, flat, levelB, new[] { aStrip }, true, false);
                return fly != null && Directions.Dot(fly.Path.Start, f) < -(om + mainW) && MergeAfterExit(a, fly) && Clear(fly, aStrip) ? fly : null;
            }

            /// <summary>路径上压在主线或 strips 上的范围（去掉两头水平段）：第一个、最后一个位置；没有时为 −1。</summary>
            private (float First, float Last) Marks(RampPath path, float flatStart, float flatEnd, IReadOnlyList<Strip> strips)
            {
                float total = path.Length, first = -1f, last = -1f;
                int count = Math.Max(1, (int)(total / kStep));
                for (int i = 0; i <= count; i++)
                {
                    float s = total * i / count;
                    if (s < flatStart || s > total - flatEnd)
                        continue;
                    Vec3 v = path.PointAt(s);
                    if (!OverMain(v) && !strips.Any(st => st.DistanceTo(v, out _) < (rw + st.Width) / 2f + gap * 0.5f))
                        continue;
                    if (first < 0f)
                        first = s;
                    last = s;
                }
                return (first, last);
            }

            /// <summary>
            /// 从小到大找左转匝道的转弯半径：A、B 都放得下（B 与 A 交叉一次、远侧幅上先出后进），跨线桥的立柱也都能避开下面的道路。
            /// 定向：两条用同一半径，远侧幅横移到中央分隔带里放得下一条匝道、且转弯在跨过近侧幅之前转完（垂直跨过，跨越段最短）。
            /// </summary>
            private void SolveLeftTurns()
            {
                // 放不下时也要很快结束（预览每帧都可能重算）：B 的尝试次数有上限
                int budget = 120;
                if (directional)
                {
                    for (float r = 40f; r <= 400f && budget > 0; r += 10f, budget--)
                    {
                        shift = Math.Max(0f, r + 2f * (sepMain + kPillarMargin) - 2f * om);
                        a = BuildA(r);
                        b = a != null ? BuildB(r, a, new Strip("A", rw, a.Samples())) : null;
                        if (a != null && b != null && PlanCuts(a, "A") && PlanCuts(b, "B"))
                            return;
                    }
                    a = b = null;
                    return;
                }
                for (float ra = 40f; ra <= 400f && budget > 0; ra += 10f)
                {
                    a = BuildA(ra);
                    if (a == null)
                        continue;
                    var aStrip = new Strip("A", rw, a.Samples());
                    for (float rb = 40f; rb <= 400f && budget > 0; rb += 10f)
                    {
                        b = BuildB(rb, a, aStrip);
                        if (b == null)
                            continue;
                        budget--;
                        if (PlanCuts(a, "A") && PlanCuts(b, "B"))
                            return;
                    }
                }
                a = b = null;
            }

            /// <summary>远侧幅上先经过 A 的驶出（连同并行段），再经过 B 的汇入（远侧幅沿 −X 行驶）。</summary>
            private bool MergeAfterExit(Flyover a, Flyover b)
            {
                float xA = Directions.Dot(a.Path.Start, X), xB = Directions.Dot(b.Path.End, X);
                return xA - a.FlatStart - kMinSegment >= xB + b.FlatEnd;
            }

            /// <summary>匝道与另一条匝道（采样 other）平面上挨着的地方，高度至少差一层高差的 90%。</summary>
            private bool Clear(Flyover one, Strip other)
            {
                float reach = rw + gap * 0.5f;
                foreach (var u in one.Samples())
                    if (other.DistanceTo(u, out float h) < reach && Math.Abs(u.Y - h) < g * 0.9f)
                        return false;
                return true;
            }

            /// <summary>
            /// 两条右转直连匝道（与近侧幅、支路都相切的圆弧）和左转匝道共用支路上的节点：支路进向一幅在节点一分为二（右转匝道、左转匝道 B），
            /// 出向一幅在节点合二为一（右转匝道、左转匝道 A）。两侧节点离中心同一距离（右转匝道对称），较近的那条左转匝道的直线加长到那里；
            /// 右转匝道与支路并行重叠的那段在左转匝道的水平段之内。
            /// </summary>
            private bool SolveRightTurns()
            {
                Vec3 mainNear = Point(0f, -om - EdgeOffset);
                // 与近侧幅、支路都相切、切点正好在支路上距中心 d 处的右转匝道（半径越大切点越远，二分求半径）
                (RampPath Path, float FlatMain, float FlatBranch)? Ramp(bool outbound, float d)
                {
                    (RampPath, float, float, float) Try(float r)
                    {
                        // 基层是高架时两头都用小半径：并行重叠段要短于游戏切分高架路段的长度
                        float rEnd = mainElevated ? Math.Min(r, kDivergeRadius) : r;
                        var path = RampPath.Fillet(mainNear, outbound ? X : -X, B(0f, outbound ? ObRight : -ObRight), -f, rEnd, r, rEnd, SepAttach, SepRight,
                            out _, out float t, out _, out _);
                        // 并行重叠长度按实际路径量（斜交时公式估算偏短）
                        float flatMain = Overlap(path, mainNear, X, SepAttach, true), flatBranch = Overlap(path, B(0f, outbound ? ObRight : -ObRight), f, SepRight, false);
                        return (outbound ? path : path.Reversed(), t, flatMain + kFlatMargin, flatBranch + kFlatMargin);
                    }
                    float lo = 20f, hi = 5000f;
                    if (Try(lo).Item2 > d || Try(hi).Item2 < d)
                        return null;
                    for (int i = 0; i < 40 && hi - lo > 0.05f; i++)
                    {
                        float mid = (lo + hi) / 2f;
                        if (Try(mid).Item2 < d)
                            lo = mid;
                        else
                            hi = mid;
                    }
                    var (p, _, fm, fb) = Try(hi);
                    return (p, fm, fb);
                }
                // 最近能放下的距离：右转匝道最小半径时的切点
                const float minRadius = 40f;
                RampPath.Fillet(mainNear, X, B(0f, ObRight), -f, minRadius, minRadius, minRadius, SepAttach, SepRight, out _, out float t1, out _, out _);
                RampPath.Fillet(mainNear, -X, B(0f, -ObRight), -f, minRadius, minRadius, minRadius, SepAttach, SepRight, out _, out float t2, out _, out _);
                float minD = Math.Max(t1, t2);

                (RampPath Path, float FlatMain, float FlatBranch)? outRamp = null, inRamp = null;
                for (int iter = 0; iter < 8; iter++)
                {
                    float dA = -Directions.Dot(a!.Path.End, f), dB = -Directions.Dot(b!.Path.Start, f);
                    float d = Math.Max(Math.Max(dA, dB), minD);
                    if (d > dA + 0.01f)
                        ExtendEnd(a, d - dA);
                    if (d > dB + 0.01f)
                        ExtendStart(b, d - dB);
                    outRamp = Ramp(true, d);
                    inRamp = Ramp(false, d);
                    if (outRamp == null || inRamp == null)
                        return false;
                    // 右转匝道与支路并行重叠的那段要在左转匝道的水平段之内（两者在节点处同高分合）
                    bool changed = false;
                    float needA = outRamp.Value.FlatBranch + 5f, needB = inRamp.Value.FlatBranch + 5f;
                    if (a.FlatEnd < needA)
                    {
                        a.FlatEnd = needA;
                        float slack = a.Length - a.FlatEnd - a.PlateauEnd - Math.Abs(a.Level - hm) / grade;
                        if (slack < 0f)
                            ExtendEnd(a, -slack + 0.5f);
                        changed = true;
                    }
                    if (b.FlatStart < needB)
                    {
                        b.FlatStart = needB;
                        float slack = b.PlateauStart - b.FlatStart - Math.Abs(b.Level - hm) / grade;
                        if (slack < 0f)
                            ExtendStart(b, -slack + 0.5f);
                        changed = true;
                    }
                    if (!changed)
                        break;
                }
                nodeDistance = Math.Max(-Directions.Dot(a!.Path.End, f), -Directions.Dot(b!.Path.Start, f));
                (nearOut, outFlatMain, outFlatBranch) = outRamp!.Value;
                (nearIn, inFlatMain, inFlatBranch) = inRamp!.Value;

                // 右转匝道（地面）离开节点附近后，平面上不与还没升到桥面的左转匝道相交
                var strips = new[] { new Strip("A", rw, a.Samples()), new Strip("B", rw, b.Samples()) };
                foreach (var (path, flatBranch, outbound) in new[] { (nearOut, outFlatBranch, true), (nearIn, inFlatBranch, false) })
                {
                    float total = path.Length;
                    for (float s = 0f; s <= total; s += kStep)
                    {
                        float fromNode = outbound ? total - s : s;
                        if (fromNode < flatBranch)
                            continue;
                        Vec3 v = path.PointAt(s);
                        foreach (var st in strips)
                            if (st.DistanceTo(v, out float h) < (rw + st.Width) / 2f + gap * 0.5f && Math.Abs(h - hm) < g * 0.9f)
                                return false;
                    }
                }
                return true;
            }

            /// <summary>匝道末尾的直线加长 delta（落地点沿支路外移）。</summary>
            private static void ExtendEnd(Flyover fly, float delta)
            {
                var arcs = fly.Path.Arcs.ToList();
                var last = arcs[arcs.Count - 1];
                arcs[arcs.Count - 1] = Straight(last.From, last.Heading, last.Length + delta);
                fly.Path = RampPath.FromArcs(arcs.ToArray());
            }

            /// <summary>匝道开头的直线加长 delta（起点沿支路外移），跨越段位置随之后移。</summary>
            private static void ExtendStart(Flyover fly, float delta)
            {
                var arcs = fly.Path.Arcs.ToList();
                var first = arcs[0];
                arcs[0] = Straight(first.From - first.Heading * delta, first.Heading, first.Length + delta);
                fly.Path = RampPath.FromArcs(arcs.ToArray());
                fly.PlateauStart += delta;
                fly.PlateauEnd += delta;
            }

            /// <summary>
            /// 跨线桥分段：断点含两头水平段和跨越段的起止；高架部分每段不超过 kMaxElevatedPiece，转角不超过 kMaxPieceTurn，
            /// 另加的断点（立柱）不落在下面的道路上方。
            /// </summary>
            private bool PlanCuts(Flyover fly, string name)
            {
                float total = fly.Length;
                var required = new List<float> { 0f, fly.FlatStart, total - fly.FlatEnd, total };
                if (fly.HasPlateau)
                {
                    required.Add(fly.PlateauStart);
                    required.Add(fly.PlateauEnd);
                }
                var lower = new List<Strip>();
                var other = fly == a ? b : a;
                if (other != null)
                    lower.Add(new Strip("other", rw, other.Samples()));
                bool Forbidden(float s)
                {
                    Vec3 v = fly.Path.PointAt(s);
                    float h = fly.HeightAt(s);
                    if (h < hm + 3f && h > hm - 3f)
                        return false;
                    // 主线两幅（直线，高度 hm）：立柱是垂直于桥面方向、长约一个桥面宽的薄墙，按它在主线横向（Y）上的伸展判断，
                    // 匝道垂直跨过主线时薄墙顺着主线方向，放得进中央分隔带
                    Vec3 heading = fly.Path.HeadingAt(s);
                    float extent = rw / 2f * Math.Abs(Directions.Dot(Perp(heading), Y)) + kPillarThickness / 2f * Math.Abs(Directions.Dot(heading, Y));
                    float y = Directions.Dot(v, Y), reach = mainW / 2f + extent + 1f;
                    if (hm < h - 1f && (Math.Abs(y + om) < reach || Math.Abs(y - FarY) < reach))
                        return true;
                    // 下面的另一条匝道：立柱离它的桥面留 1 米（窄支路两侧的匝道本来就挨得近）
                    foreach (var st in lower)
                        if (st.DistanceTo(v, out float hh) < (rw + st.Width) / 2f + 1f && hh < h - 1f)
                            return true;
                    return false;
                }
                // 弯道与直线（或不同半径）交界处也分段：一段路用一条三次曲线近似，弯直混在一段里会明显鼓出去；立柱不能放的地方除外
                float offset = 0f;
                for (int i = 0; i + 1 < fly.Path.Arcs.Count; i++)
                {
                    var arc = fly.Path.Arcs[i];
                    offset += arc.Length;
                    var next = fly.Path.Arcs[i + 1];
                    if (Math.Abs(arc.Radius - next.Radius) < 0.5f && Math.Sign(arc.Angle) == Math.Sign(next.Angle))
                        continue;
                    float at = offset;
                    // 与所接道路并行重叠的两头（水平段）之内不分段：那里的节点会让匝道和所接道路重叠（游戏报形状无效）
                    bool inFlat = at < fly.FlatStart + kMinSegment || at > total - fly.FlatEnd - kMinSegment;
                    if (!inFlat && required.All(r => Math.Abs(r - at) >= kMinSegment) && !Forbidden(at))
                        required.Add(at);
                }
                required = required.Where(s => s >= 0f && s <= total).OrderBy(s => s).ToList();
                var merged = new List<float>();
                foreach (float s in required)
                    if (merged.Count == 0 || s - merged[merged.Count - 1] > 1f)
                        merged.Add(s);
                merged[merged.Count - 1] = total;

                bool Elevated(float s0, float s1) =>
                    LevelPlanner.BandOf(fly.HeightAt(s0)) == ElevationBand.Elevated || LevelPlanner.BandOf(fly.HeightAt(s1)) == ElevationBand.Elevated
                    || LevelPlanner.BandOf(fly.HeightAt((s0 + s1) / 2f)) == ElevationBand.Elevated;

                var cuts = new List<float> { 0f };
                float cur = 0f;
                for (int i = 1; i < merged.Count; i++)
                {
                    float target = merged[i];
                    int guard = 0;
                    while (guard++ < 100)
                    {
                        float maxLen = Elevated(cur, target) ? kMaxElevatedPiece : kMaxGroundPiece;
                        if (target - cur <= maxLen && fly.Path.TurnBetween(cur, target) <= kMaxPieceTurn)
                            break;
                        float pick = -1f;
                        for (float s = Math.Min(cur + maxLen, target - kMinSegment); s >= cur + kMinSegment; s -= 1f)
                        {
                            if (fly.Path.TurnBetween(cur, s) > kMaxPieceTurn || Forbidden(s))
                                continue;
                            pick = s;
                            break;
                        }
                        if (pick < 0f)
                            return false;
                        cuts.Add(pick);
                        cur = pick;
                    }
                    cuts.Add(target);
                    cur = target;
                }
                fly.Cuts = cuts;
                return true;
            }

            public void Build(List<RoadSegmentSpec> segments)
            {
                if (!Fits)
                    return;
                var p = P;
                var legs = p.Legs;
                Flyover fa = a!, fb = b!;

                // —— 匝道与主线、支路的接点 ——
                float xA = Directions.Dot(fa.Path.Start, X), xB = Directions.Dot(fb.Path.End, X);
                Vec3 aStart = Point(xA, FarY, hm), bEnd = Point(xB, FarY, hm);
                // 支路上的共用节点：出向一幅 A 与右转匝道汇合，进向一幅分成右转匝道与 B
                Vec3 aEnd = B(-nodeDistance, Obc, hm), bStart = B(-nodeDistance, -Obc, hm);
                float xOut = Directions.Dot(nearOut.Start, X), xIn = Directions.Dot(nearIn.End, X);
                Vec3 outMain = Point(xOut, -om, hm), inMain = Point(xIn, -om, hm);

                // —— 远侧幅：定向时在立交范围内 S 形横移 shift ——
                float farLo = Math.Min(xB - fb.FlatEnd, xA - fa.FlatStart) - 20f, farHi = Math.Max(xA, xB) + 20f;
                float sAlong = 0f;
                float theta = 0f;
                if (shift > 0f)
                {
                    theta = (float)Math.Acos(Math.Max(-1f, 1f - shift / (2f * kShiftRadius)));
                    sAlong = 2f * kShiftRadius * MathF.Sin(theta);
                }
                float mainEnd = new[] { Math.Abs(xOut), Math.Abs(xIn), Math.Abs(farLo - sAlong), Math.Abs(farHi + sAlong) }.Max() + legs[0].StubLength;

                // 近侧幅（沿 +X，直线）
                // 基层是高架时各条道路自己按长度分段（游戏会把超过 80 米的高架路段从中间切开，切点的立柱可能落在旁边匝道的桥面上），
                // 分段点不放在匝道与道路并行重叠的那段里
                float maxPiece = mainElevated ? kMaxElevatedPiece - 8f : float.MaxValue;
                var nearNodes = Subdivide(new List<float> { -mainEnd, xOut, xIn, mainEnd }, maxPiece,
                    new[] { (xOut, xOut + outFlatMain), (xIn - inFlatMain, xIn) });
                float drop = p.MainDropShift;
                bool InNearDrop(float x) => drop > 0f && x > xOut + 1e-3f && x < xIn - 1e-3f;
                var nearPoints = new List<Vec3>();
                foreach (float x in nearNodes)
                    if (nearPoints.Count == 0 || x - Directions.Dot(nearPoints[nearPoints.Count - 1], X) >= kMinSegment || x == nearNodes[nearNodes.Count - 1])
                        nearPoints.Add(Point(x, InNearDrop(x) ? -om + drop : -om, hm));
                DiamondTemplate.AddRoadLine(segments, "MainNear", RoadRole.MainRoad, X, travelPositive: true, legs[2].RoadPrefab, legs[0].RoadPrefab, "2", "0",
                    nearPoints, xOut, xIn, drop > 0f ? p.MainReducedPrefab : null, Y * drop);

                // 远侧幅（沿 −X）：+mainEnd → (S 形) → 横移段 → (S 形) → −mainEnd
                var farArcs = new List<RampArc>();
                Vec3 at = Point(mainEnd, om);
                if (shift > 0f)
                {
                    float rightSign = DiamondTemplate.Cross2(-X, Y) >= 0f ? 1f : -1f;
                    farArcs.Add(Straight(at, -X, mainEnd - (farHi + sAlong)));
                    var s1 = new RampArc(farArcs[0].End, -X, kShiftRadius, rightSign * theta);
                    var s2 = new RampArc(s1.End, s1.EndHeading, kShiftRadius, -rightSign * theta);
                    farArcs.Add(s1);
                    farArcs.Add(s2);
                    farArcs.Add(Straight(s2.End, -X, farHi - farLo));
                    var s3 = new RampArc(farArcs[3].End, -X, kShiftRadius, -rightSign * theta);
                    var s4 = new RampArc(s3.End, s3.EndHeading, kShiftRadius, rightSign * theta);
                    farArcs.Add(s3);
                    farArcs.Add(s4);
                    farArcs.Add(Straight(s4.End, -X, Directions.Dot(s4.End, X) + mainEnd));
                }
                else
                    farArcs.Add(Straight(at, -X, 2f * mainEnd));
                var farPath = RampPath.FromArcs(farArcs.ToArray());
                float FarS(float x) => shift > 0f
                    ? (mainEnd - (farHi + sAlong)) + 2f * kShiftRadius * theta + (farHi - x)
                    : mainEnd - x;
                var farCuts = new List<float> { 0f };
                if (shift > 0f)
                {
                    float s1 = mainEnd - (farHi + sAlong);
                    farCuts.Add(s1);
                    farCuts.Add(s1 + kShiftRadius * theta);
                    farCuts.Add(s1 + 2f * kShiftRadius * theta);
                }
                farCuts.Add(FarS(xA));
                farCuts.Add(FarS(xB));
                if (shift > 0f)
                {
                    float s3 = FarS(farLo);
                    farCuts.Add(s3);
                    farCuts.Add(s3 + kShiftRadius * theta);
                    farCuts.Add(s3 + 2f * kShiftRadius * theta);
                }
                farCuts.Add(farPath.Length);
                farCuts = Subdivide(farCuts.OrderBy(s => s).ToList(), maxPiece,
                    new[] { (FarS(xA), FarS(xA) + fa.FlatStart), (FarS(xB) - fb.FlatEnd, FarS(xB)) });
                // 匝道接点用同一个坐标（游戏按坐标完全相等合并节点）
                Vec3 FarNode(float s)
                {
                    // 两端与近侧幅对称（臂端过渡按坐标接上）
                    if (s <= 0f)
                        return Point(mainEnd, om, hm);
                    if (s >= farPath.Length - 1e-3f)
                        return Point(-mainEnd, om, hm);
                    if (Math.Abs(s - FarS(xA)) < 1e-3f)
                        return aStart;
                    if (Math.Abs(s - FarS(xB)) < 1e-3f)
                        return bEnd;
                    Vec3 v = farPath.PointAt(s);
                    v.Y = hm;
                    // 减车道段里的节点随减车道的道路一起横移
                    return InFarDrop(s) ? v + Y * FarDropY : v;
                }
                float sA = FarS(xA), sB = FarS(xB);
                bool InFarDrop(float s) => p.MainDropShift > 0f && s > sA + 1e-3f && s < sB - 1e-3f;
                for (int i = 0; i + 1 < farCuts.Count; i++)
                {
                    float s0 = farCuts[i], s1 = farCuts[i + 1];
                    var slice = farPath.Slice(s0, s1);
                    bool reduced = InFarDrop((s0 + s1) / 2f);
                    Vec3 n0 = FarNode(s0), n1 = FarNode(s1);
                    // 减车道段两头接在匝道节点上：曲线端点横移、节点不动（边缘对齐）
                    Vec3 c0 = reduced && !InFarDrop(s0) ? n0 + Y * FarDropY : n0, c1 = reduced && !InFarDrop(s1) ? n1 + Y * FarDropY : n1;
                    slice.EmitAt(segments, $"MainFar-{i + 1}-", RoadRole.MainRoad, new[] { 0f, slice.Length }, new[] { hm, hm },
                        reduced ? p.MainReducedPrefab : legs[0].RoadPrefab, c0, c1, n0, n1);
                }

                // 外部是双向道路的主线：两端过渡
                foreach (float end in new[] { 1f, -1f })
                {
                    LegSpec leg = end > 0f ? legs[0] : legs[2];
                    if (!leg.NeedsDualToTwoWayTransition)
                        continue;
                    bool alignedEnd = leg.ExternalWidth > 0f && 2f * om + leg.RoadWidth <= leg.ExternalWidth + TrumpetTemplate.kMinSplitMedian + 0.5f;
                    if (alignedEnd)
                    {
                        // 驶出这一端的那幅最后一段、驶入的那幅第一段接到双向道路端点的同一个节点（曲线端点横移，边缘对齐）；
                        // 宽的双向道路（如 XL Road Divided）斜向汇合时游戏报“形状无效”
                        Vec3 joint = Point(end * mainEnd, 0f, hm);
                        Vec3 outEnd = end > 0f ? Point(mainEnd, -om, hm) : Point(-mainEnd, om, hm);
                        Vec3 inStart = end > 0f ? Point(mainEnd, om, hm) : Point(-mainEnd, -om, hm);
                        int o = segments.FindIndex(sg => sg.Role == RoadRole.MainRoad && sg.EndNode.Equals(outEnd));
                        int i = segments.FindIndex(sg => sg.Role == RoadRole.MainRoad && sg.StartNode.Equals(inStart));
                        if (o >= 0 && i >= 0)
                        {
                            segments[o] = new RoadSegmentSpec(segments[o].Name, segments[o].Role, segments[o].Curve, segments[o].Prefab, segments[o].StartNode, joint);
                            segments[i] = new RoadSegmentSpec(segments[i].Name, segments[i].Role, segments[i].Curve, segments[i].Prefab, joint, segments[i].EndNode);
                            segments.Add(new RoadSegmentSpec(end > 0f ? "Main-endA-tail" : "Main-endB-tail", RoadRole.MainRoad,
                                Bezier3.Line(joint, Point(end * (mainEnd + leg.StubLength), 0f, hm)), leg.ExternalPrefab));
                            continue;
                        }
                    }
                    Vec3 axis = X * end;
                    Vec3 lateral = Directions.RightOf(axis);
                    Vec3 Pt(float along, float l) => axis * along + lateral * l + new Vec3(0f, hm, 0f);
                    DiamondTemplate.AddTransition(segments, end > 0f ? "Main-endA" : "Main-endB", RoadRole.MainRoad, Pt, +1, mainEnd, om,
                        mainEnd + leg.StubLength, p.LegTransitionAngle, leg.RoadPrefab, leg.ExternalPrefab);
                }

                // —— 支路两幅：出向（A 落地 → 右转匝道汇入 → 外端），进向（外端 → 右转匝道分出 → B 起点）——
                var leg1 = legs[1];
                bool transition = leg1.NeedsDualToTwoWayTransition;
                bool aligned = transition && leg1.ExternalWidth > 0f && 2f * ob + leg1.RoadWidth <= leg1.ExternalWidth + TrumpetTemplate.kMinSplitMedian + 0.5f;
                // 节点处两幅在 ±Obc（拉开），先 S 形并回 ±ob，再直行到外端
                float merge = Obc > ob + 0.01f ? Math.Min(60f, Math.Max(40f, 10f * (Obc - ob))) : 0f;
                // 节点区会一直延伸到并入的两条匝道完全分开（右转匝道与支路并行的那段），S 形要在它之后才开始，否则整段被节点区吃掉
                float run = merge > 0f ? Math.Max(outFlatBranch, inFlatBranch) + kMinSegment : 0f;
                float tBase = nodeDistance + run + merge;
                float tail = transition ? kTransitionGap : leg1.StubLength;
                Vec3 TailPoint(float along, float lateral) => B(-(tBase + along), lateral, hm);
                if (merge > 0f)
                {
                    Vec3 outRun = B(-(nodeDistance + run), Obc, hm), inRun = B(-(nodeDistance + run), -Obc, hm);
                    var runCuts = Subdivide(new List<float> { 0f, run }, maxPiece, Array.Empty<(float, float)>());
                    AddChain(segments, "BranchOut-run", runCuts.Select(c => c >= run ? outRun : B(-(nodeDistance + c), Obc, hm)).ToList(), leg1.RoadPrefab);
                    AddChain(segments, "BranchIn-run", runCuts.AsEnumerable().Reverse().Select(c => c >= run ? inRun : B(-(nodeDistance + c), -Obc, hm)).ToList(), leg1.RoadPrefab);
                    segments.Add(new RoadSegmentSpec("BranchOut-merge", RoadRole.CrossRoad,
                        Bezier3.FromTangents(outRun, -f, TailPoint(0f, ob), -f, merge / 3f), leg1.RoadPrefab));
                    segments.Add(new RoadSegmentSpec("BranchIn-merge", RoadRole.CrossRoad,
                        Bezier3.FromTangents(TailPoint(0f, -ob), f, inRun, f, merge / 3f), leg1.RoadPrefab));
                }
                var outNodes = new List<Vec3> { TailPoint(0f, ob) };
                var inNodes = new List<Vec3> { TailPoint(tail, -ob) };
                var branchCuts = Subdivide(new List<float> { 0f, tail }, maxPiece, Array.Empty<(float, float)>());
                for (int i = 1; i < branchCuts.Count; i++)
                    outNodes.Add(TailPoint(branchCuts[i], ob));
                for (int i = branchCuts.Count - 2; i >= 0; i--)
                    inNodes.Add(TailPoint(branchCuts[i], -ob));
                AddChain(segments, "BranchOut", outNodes, leg1.RoadPrefab);
                int outLast = segments.Count - 1;
                AddChain(segments, "BranchIn", inNodes, leg1.RoadPrefab);
                int inFirst = outLast + 1;
                if (aligned)
                {
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

                // —— 右转直连匝道（地面）——
                var outCuts = PieceCuts(nearOut, outFlatMain, outFlatBranch, maxPiece);
                nearOut.EmitAt(segments, "NearToBranch", RoadRole.Ramp, outCuts, outCuts.Select(_ => hm).ToList(), p.RampPrefab,
                    Point(xOut, -om - EdgeOffset, hm), B(-nodeDistance, ObRight, hm), startNode: outMain, endNode: aEnd);
                var inCuts = PieceCuts(nearIn, inFlatBranch, inFlatMain, maxPiece);
                nearIn.EmitAt(segments, "BranchToNear", RoadRole.Ramp, inCuts, inCuts.Select(_ => hm).ToList(), p.RampPrefab,
                    B(-nodeDistance, -ObRight, hm), Point(xIn, -om - EdgeOffset, hm), startNode: bStart, endNode: inMain);

                // —— 左转跨线桥 ——
                fa.Path.EmitAt(segments, "FarToBranch", RoadRole.Ramp, fa.Cuts, fa.Cuts.Select(fa.HeightAt).ToList(), p.RampPrefab,
                    Point(xA, FarAttachY, hm), B(-nodeDistance, obi, hm), startNode: aStart, endNode: aEnd);
                fb.Path.EmitAt(segments, "BranchToFar", RoadRole.Ramp, fb.Cuts, fb.Cuts.Select(fb.HeightAt).ToList(), p.RampPrefab,
                    B(-nodeDistance, -obi, hm), Point(xB, FarAttachY, hm), startNode: bStart, endNode: bEnd);
            }

            /// <summary>沿主线按位置把节点连成路段（相距不到 kMinSegment 的合并，保留两端）。</summary>
            private void AddLine(List<RoadSegmentSpec> segments, string name, List<float> nodes, Func<float, Vec3> at)
            {
                var sorted = new List<float>();
                foreach (float t in nodes.OrderBy(t => t))
                    if (sorted.Count == 0 || t - sorted[sorted.Count - 1] >= kMinSegment)
                        sorted.Add(t);
                for (int i = 0; i + 1 < sorted.Count; i++)
                    segments.Add(new RoadSegmentSpec($"{name}-{i + 1}", RoadRole.MainRoad, Bezier3.Line(at(sorted[i]), at(sorted[i + 1])), P.Legs[0].RoadPrefab));
            }

            /// <summary>
            /// 在已有断点之间加断点，使每段不超过 maxLength；落在 keepWhole（不能断开的并行重叠段）里的挪到它的边上。
            /// </summary>
            private static List<float> Subdivide(List<float> cuts, float maxLength, IReadOnlyList<(float Lo, float Hi)> keepWhole)
            {
                var sorted = cuts.OrderBy(c => c).ToList();
                var result = new List<float> { sorted[0] };
                for (int i = 0; i + 1 < sorted.Count; i++)
                {
                    float lo = sorted[i], hi = sorted[i + 1];
                    int n = (int)Math.Ceiling((hi - lo) / maxLength);
                    for (int k = 1; k < n; k++)
                    {
                        float c = lo + (hi - lo) * k / n;
                        foreach (var (a, b) in keepWhole)
                            if (c > a && c < b)
                                c = c - a < b - c && a > lo + kMinSegment ? a : b < hi - kMinSegment ? b : a;
                        if (c - result[result.Count - 1] >= kMinSegment && hi - c >= kMinSegment)
                            result.Add(c);
                    }
                    result.Add(hi);
                }
                return result;
            }

            /// <summary>
            /// 一条匝道的断点：两端、两头并行重叠段的边界、弯道交界处（重叠段之外），再按长度和转角加断点（重叠段整段不断开）。
            /// </summary>
            private static List<float> PieceCuts(RampPath path, float flatStart, float flatEnd, float maxLength)
            {
                float total = path.Length;
                var cuts = new List<float> { 0f, flatStart, total - flatEnd, total };
                float offset = 0f;
                for (int i = 0; i + 1 < path.Arcs.Count; i++)
                {
                    offset += path.Arcs[i].Length;
                    float at = offset;
                    if (at > flatStart + kMinSegment && at < total - flatEnd - kMinSegment && cuts.All(c => Math.Abs(c - at) >= kMinSegment))
                        cuts.Add(at);
                }
                cuts = cuts.Where(c => c >= 0f && c <= total).OrderBy(c => c).ToList();
                var merged = new List<float>();
                foreach (float c in cuts)
                    if (merged.Count == 0 || c - merged[merged.Count - 1] > 1f)
                        merged.Add(c);
                merged[merged.Count - 1] = total;
                var result = new List<float> { 0f };
                for (int i = 1; i < merged.Count; i++)
                {
                    float lo = merged[i - 1], hi = merged[i];
                    bool overlap = lo < flatStart - 0.5f || hi > total - flatEnd + 0.5f;
                    int n = overlap ? 1 : Math.Max((int)Math.Ceiling((hi - lo) / maxLength), (int)Math.Ceiling(path.TurnBetween(lo, hi) / kMaxPieceTurn));
                    for (int k = 1; k < n; k++)
                        result.Add(lo + (hi - lo) * k / n);
                    result.Add(hi);
                }
                return result;
            }

            private static void AddChain(List<RoadSegmentSpec> segments, string name, IList<Vec3> nodes, string? prefab)
            {
                for (int i = 0; i + 1 < nodes.Count; i++)
                    segments.Add(new RoadSegmentSpec($"{name}-{i + 1}", RoadRole.CrossRoad, Bezier3.Line(nodes[i], nodes[i + 1]), prefab));
            }
        }
    }
}
