using System;
using System.Collections.Generic;
using System.Linq;
using InterchangeMod.Generation.Geometry;
using static InterchangeMod.Generation.Templates.Planar;

namespace InterchangeMod.Generation.Templates
{
    /// <summary>
    /// Y 型分向（三向全定向、交点分散，两层，右侧通行）。三条双幅道路（臂 0/1/2，相邻夹角 90°–150°，默认各 120°）在基层汇于一处，
    /// 不分主次。每条路的进向一幅在分岔点一分为二：右转匝道走外侧去右边那条路，左转匝道走里侧去左边那条路；出向一幅由两条匝道并成。
    /// 每两条路之间的扇区里一外一内两条匝道（外侧右转、里侧左转），整体像一个弯边三角形：
    ///
    ///         ╲ ╲                ╱ ╱
    ///          ╲ ╲______________╱ ╱     外侧：右转
    ///           ╲ ╲  ________  ╱ ╱      里侧：左转
    ///            ╲ ╲╱        ╲╱ ╱
    ///             ╲╱╲        ╱╲╱        每条路里侧一处交叉（交点分散）
    ///                ...
    ///
    /// 交叉只有三处，每条路里侧各一处：从这条路出发的左转匝道升到第二层，跨过到达这条路的左转匝道（它在基层从下面穿过）。
    /// 所以左转匝道出发后沿进向一幅内侧直行爬升，转弯斜跨过到达的那条，再降回基层，到达时从下面穿过对面出发的那条，
    /// 沿出向一幅内侧直行到并合点。右转匝道一直在基层，不与别的匝道相交。
    ///
    /// 路径：左转匝道 = 直线 t（沿进向一幅）+ 核心（离中心 c 处起转，“圆弧 – 直线 – 圆弧”同向左转，半径 rL）+ 直线 t（沿出向一幅）；
    /// 右转匝道 = 分岔点到并合点的“圆弧 – 直线 – 圆弧”同向右转，半径取连得上的最大值。分岔、并合点离中心 D = c + t，
    /// 两条匝道边缘对齐那一幅的两侧边缘接到同一个节点。c 从小到大、rL 从大到小找第一个所有约束都满足的：
    /// 交叉要足够陡（跨越段两头的立柱都要避开下面的匝道，桥跨短于游戏切分高架的长度）、爬升和下降放得下、各匝道互不相碰。
    /// </summary>
    public sealed class WyeTemplate : IInterchangeTemplate
    {
        /// <summary>相邻两条路的夹角范围（度）。</summary>
        private const float kMinGap = 90f, kMaxGap = 150f;
        private const float kGradeMargin = 0.98f;
        /// <summary>分岔 / 并合处两条匝道并行重叠的那段再多保持同高这么远（米）才开始升降。</summary>
        private const float kFlatMargin = 15f;
        /// <summary>立柱沿桥面方向的厚度（米）。</summary>
        private const float kPillarThickness = 3f;
        /// <summary>跨越段（不能放立柱的范围）前后再多保持第二层高度的长度（米）。</summary>
        private const float kPlateauMargin = 2f;
        /// <summary>必需断点（并行段终点、高度折点）之间的最小距离（米）：更近时两个节点的立柱挤在一起。</summary>
        private const float kMinSegment = 10f;
        /// <summary>分段时每段的最小长度（米）：太短的路段游戏报形状无效。</summary>
        private const float kMinPiece = 20f;
        /// <summary>高架路段每段的最大长度（米），短于游戏切分高架路段的 80 米。</summary>
        private const float kMaxElevatedPiece = 70f;
        private const float kMaxGroundPiece = 600f;
        /// <summary>一段路的最大转角（弧度，约 80°）。</summary>
        private const float kMaxPieceTurn = 1.4f;
        /// <summary>采样间距（米）。</summary>
        private const float kStep = 2f;
        /// <summary>左转匝道起转点离中心的距离 c 的搜索范围和步长（米）。</summary>
        private const float kMinCore = 20f, kMaxCore = 900f, kCoreStep = 10f;
        /// <summary>左转匝道转弯半径的候选（米，从大到小试）：越小交叉越陡、跨越段越短。</summary>
        private static readonly float[] kLeftRadii = { 500f, 400f, 300f, 250f, 200f, 150f };
        /// <summary>右转匝道的最小、最大转弯半径（米）。</summary>
        private const float kMinRightRadius = 60f, kMaxRadius = 3000f;
        /// <summary>右转匝道两段圆弧各至少转过总转角的这个比例（两头都马上转弯，不先直行）。</summary>
        private const float kMinArcShare = 0.2f;
        /// <summary>
        /// 并排的两条路之间至少留的净距（米）：里侧一出一进两条左转匝道在路中间并行（升降时有挡土墙），高架时立柱横梁比桥面宽；
        /// 分岔 / 并合处两条匝道也要分开这么远，第一个节点（立柱）才不挤在一起。
        /// </summary>
        private const float kInnerGap = 8f;
        /// <summary>右转匝道在分岔点向外、在并合点向里横移的 S 形半径（米）：高架时并行段要短于一段（游戏切分高架的长度）。</summary>
        private const float kForkShiftRadius = 120f, kElevatedForkShiftRadius = 60f;
        /// <summary>立交内两幅向外拉开、臂端 S 形接回原来分隔带的半径（米）。</summary>
        private const float kLegShiftRadius = 200f;
        /// <summary>拉开不到这么多（米）时整条臂直接按拉开后的位置建，不加 S 形（臂端接回时横向差这点能接上）。</summary>
        private const float kMinLegShift = 1f;

        public InterchangeType Type => InterchangeType.Wye;

        public string DisplayName => "Y 型分向";

        public int LegCount => 3;

        /// <summary>调试：求解过程。</summary>
        public static Action<string>? Trace;

        public IReadOnlyList<string> ValidateParams(InterchangeParams p)
        {
            var errors = p.ValidateLegsAndLevels(LegCount);
            if (errors.Count > 0)
                return errors;
            var legs = p.Legs;
            if (!p.RightHandTraffic)
                errors.Add($"{DisplayName}目前只支持右侧通行");
            var angles = legs.Select(l => (l.Angle % 360f + 360f) % 360f).OrderBy(a => a).ToList();
            for (int i = 0; i < 3; i++)
            {
                float between = (angles[(i + 1) % 3] - angles[i] + 360f) % 360f;
                if (between < kMinGap - 0.5f || between > kMaxGap + 0.5f)
                {
                    errors.Add($"三条路相邻夹角 {between:F0}°：{DisplayName}需要 {kMinGap:F0}°–{kMaxGap:F0}°");
                    break;
                }
            }
            if (legs.Any(l => l.Form != RoadForm.Dual))
                errors.Add($"{DisplayName}的三条路都需要是双幅（双向道路在立交内拆成双幅、外端接回）");
            float h0 = p.ElevationOf(legs[0]);
            if (legs.Any(l => Math.Abs(p.ElevationOf(l) - h0) > 0.5f))
                errors.Add($"{DisplayName}的三条路需要在同一层");
            float g = LevelGap(p);
            if (g < 1f)
                errors.Add("层间高差太小");
            else if (LevelPlanner.BandOf(h0 + g) != ElevationBand.Elevated)
                errors.Add($"跨线桥离地 {h0 + g:F1} 米会建成路堤（挡土墙），下面的匝道无法穿过：需要至少 {HeightRange.BridgeThreshold:F1} 米");
            if (legs.Any(l => p.RampWidth > l.RoadWidth))
                errors.Add($"匝道宽 {p.RampWidth:F0} 米，比道路的一幅还宽：请减少匝道车道数");
            if (errors.Count > 0)
                return errors;
            errors.AddRange(PlanFor(p).Errors);
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

        /// <summary>缓存键：参数指纹去掉本模板用不到的匝道长度、分流角度（预览失败时工具会逐档加长匝道重试）。</summary>
        private static string AbsoluteKey(InterchangeParams p)
        {
            var q = p.Clone();
            q.RampLength = 0f;
            q.RampDivergeAngle = 0f;
            return q.Fingerprint();
        }

        /// <summary>布置的缓存键：高度按相对基层算，另加基层和第二层所在的高度段（高架 / 地面会改变分段）。</summary>
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
            return q.Fingerprint() + $"|{LevelPlanner.BandOf(h0)}|{LevelPlanner.BandOf(h0 + g)}";
        }

        /// <summary>层间高差：基层（三条路）到跨线桥。</summary>
        private static float LevelGap(InterchangeParams p) =>
            p.Levels.Count > 1 ? p.Levels[1].Elevation - p.Levels[0].Elevation : 0f;

        private string? m_PlanKey, m_SegmentsKey;
        private Plan? m_Plan;
        private List<RoadSegmentSpec>? m_Segments;

        private Plan PlanFor(InterchangeParams p)
        {
            string key = Key(p);
            if (key != m_PlanKey || m_Plan == null)
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                m_Plan = new Plan(p);
                m_PlanKey = key;
                Trace?.Invoke($"solved in {watch.ElapsedMilliseconds} ms: {(m_Plan.Fits ? m_Plan.Summary : m_Plan.Errors[0])}");
            }
            return m_Plan;
        }

        // —— 路径：精确的直线和圆弧（本模板的直线段常有几百米，RampPath 用大半径圆弧代替直线会偏出几米）——

        /// <summary>首尾相接、切线连续的直线和圆弧（俯视）。曲率带符号：正 = 向左（Perp 一侧）转，0 = 直线。</summary>
        private sealed class Track
        {
            private readonly List<(Vec3 From, Vec3 Heading, float Length, float Curvature)> m_Pieces =
                new List<(Vec3 From, Vec3 Heading, float Length, float Curvature)>();

            public Track(Vec3 start, Vec3 heading)
            {
                End = Flat0(start);
                EndHeading = Flat(heading);
            }

            public float Length { get; private set; }
            public Vec3 Start => m_Pieces.Count > 0 ? m_Pieces[0].From : End;
            public Vec3 End { get; private set; }
            public Vec3 EndHeading { get; private set; }

            public Track Line(float length)
            {
                if (length > 1e-3f)
                    Add(length, 0f);
                return this;
            }

            /// <summary>半径 radius、转角 angle（弧度，正 = 向左）。</summary>
            public Track Arc(float radius, float angle)
            {
                if (Math.Abs(angle) > 1e-5f)
                    Add(radius * Math.Abs(angle), Math.Sign(angle) / radius);
                return this;
            }

            /// <summary>接上另一条（从本条终点出发）的各段。</summary>
            public Track Append(Track other)
            {
                foreach (var piece in other.m_Pieces)
                    Add(piece.Length, piece.Curvature);
                return this;
            }

            private void Add(float length, float curvature)
            {
                m_Pieces.Add((End, EndHeading, length, curvature));
                End = PointOn(m_Pieces[m_Pieces.Count - 1], length);
                EndHeading = HeadingOn(m_Pieces[m_Pieces.Count - 1], length);
                Length += length;
            }

            private static Vec3 PointOn((Vec3 From, Vec3 Heading, float Length, float Curvature) piece, float s)
            {
                if (piece.Curvature == 0f)
                    return piece.From + piece.Heading * s;
                Vec3 center = piece.From + Perp(piece.Heading) * (1f / piece.Curvature);
                return center + Rot(piece.From - center, piece.Curvature * s);
            }

            private static Vec3 HeadingOn((Vec3 From, Vec3 Heading, float Length, float Curvature) piece, float s) =>
                piece.Curvature == 0f ? piece.Heading : Rot(piece.Heading, piece.Curvature * s);

            public Vec3 PointAt(float s)
            {
                s = Math.Max(0f, s);
                foreach (var piece in m_Pieces)
                {
                    if (s <= piece.Length)
                        return PointOn(piece, s);
                    s -= piece.Length;
                }
                return End;
            }

            public Vec3 HeadingAt(float s)
            {
                s = Math.Max(0f, s);
                foreach (var piece in m_Pieces)
                {
                    if (s <= piece.Length)
                        return HeadingOn(piece, s);
                    s -= piece.Length;
                }
                return EndHeading;
            }

            /// <summary>弧长 s0 到 s1 之间累计转过的角度（弧度，不计方向）。</summary>
            public float TurnBetween(float s0, float s1)
            {
                float turn = 0f, offset = 0f;
                foreach (var piece in m_Pieces)
                {
                    float a = Math.Max(s0, offset), b = Math.Min(s1, offset + piece.Length);
                    if (b > a)
                        turn += (b - a) * Math.Abs(piece.Curvature);
                    offset += piece.Length;
                }
                return turn;
            }

            /// <summary>直线和圆弧交接处的弧长（不含两端）。</summary>
            public IEnumerable<float> Joints()
            {
                float offset = 0f;
                for (int i = 0; i + 1 < m_Pieces.Count; i++)
                {
                    offset += m_Pieces[i].Length;
                    yield return offset;
                }
            }
        }

        /// <summary>
        /// 同向转弯的“圆弧 – 直线 – 圆弧”：从 (p0, h0) 出发到 (p1, h1)，两段圆弧半径 r，left 为左转。
        /// 连不上（要多转一圈）时返回 null。
        /// </summary>
        private static Track? Csc(Vec3 p0, Vec3 h0, Vec3 p1, Vec3 h1, bool left, float r, float minShare = 0f)
        {
            float sign = left ? 1f : -1f;
            h0 = Flat(h0);
            h1 = Flat(h1);
            Vec3 c0 = Flat0(p0) + Perp(h0) * (sign * r), c1 = Flat0(p1) + Perp(h1) * (sign * r);
            Vec3 v = c1 - c0;
            float len = MathF.Sqrt(v.X * v.X + v.Z * v.Z);
            Vec3 u = len > 1e-3f ? new Vec3(v.X / len, 0f, v.Z / len) : h1;
            float Wrap(float a) => a > 2f * MathF.PI - 1e-3f ? 0f : a;
            float a1 = Wrap(RotAngle(h0, u, sign)), a2 = Wrap(RotAngle(u, h1, sign)), total = Wrap(RotAngle(h0, h1, sign));
            if (Math.Abs(a1 + a2 - total) > 1e-3f || a1 < minShare * total - 1e-4f || a2 < minShare * total - 1e-4f)
                return null;
            return new Track(p0, h0).Arc(r, sign * a1).Line(len).Arc(r, sign * a2);
        }

        /// <summary>
        /// 同向转弯的“圆弧 – 直线 – 圆弧”取连得上的最大半径（不超过 rMax），两段圆弧各至少转总转角的 minShare
        /// （不先直行：从分岔点一出来就转弯，不与同一节点出发的另一条匝道长距离并行）；rMin 也连不上时返回 null。
        /// </summary>
        private static Track? WidestCsc(Vec3 p0, Vec3 h0, Vec3 p1, Vec3 h1, bool left, float rMin, float rMax, float minShare, out float radius)
        {
            radius = rMax;
            var best = Csc(p0, h0, p1, h1, left, rMax, minShare);
            if (best != null)
                return best;
            radius = rMin;
            best = Csc(p0, h0, p1, h1, left, rMin, minShare);
            if (best == null)
                return null;
            float lo = rMin, hi = rMax;
            for (int i = 0; i < 24 && hi - lo > 0.5f; i++)
            {
                float mid = (lo + hi) / 2f;
                var trial = Csc(p0, h0, p1, h1, left, mid, minShare);
                if (trial != null)
                {
                    best = trial;
                    lo = mid;
                }
                else
                    hi = mid;
            }
            radius = lo;
            return best;
        }

        /// <summary>一条匝道：路径、两头节点、两头与同一节点上另一条匝道并行重叠的长度、高度（按弧长的折线）、断点。</summary>
        private sealed class Ramp
        {
            public string Name = "";
            public int From, To;
            public bool Left;
            public float Radius;
            public Track Path = null!;
            public Vec3 StartNode, EndNode;
            public float FlatStart, FlatEnd;
            public List<(float S, float H)> Knots = new List<(float S, float H)>();
            public List<float> Cuts = new List<float>();

            public float Length => Path.Length;

            public float HeightAt(float s)
            {
                if (s <= Knots[0].S)
                    return Knots[0].H;
                for (int i = 0; i + 1 < Knots.Count; i++)
                    if (s <= Knots[i + 1].S)
                        return Knots[i].H + (Knots[i + 1].H - Knots[i].H) * (s - Knots[i].S) / Math.Max(1e-3f, Knots[i + 1].S - Knots[i].S);
                return Knots[Knots.Count - 1].H;
            }
        }

        /// <summary>检查用的一条路：采样点（带高度和方向）、宽度、两头节点和两头可以与同节点道路重叠的长度。</summary>
        private sealed class Body
        {
            public string Name = "";
            public float Width;
            public List<(Vec3 P, Vec3 H, float S)> Samples = new List<(Vec3 P, Vec3 H, float S)>();
            public Vec3 StartNode, EndNode;
            public float FreeStart, FreeEnd, Length;
            private Dictionary<(int, int), List<int>>? m_Cells;
            private const float kCell = 20f;

            public Dictionary<(int, int), List<int>> Cells
            {
                get
                {
                    if (m_Cells == null)
                    {
                        m_Cells = new Dictionary<(int, int), List<int>>();
                        for (int i = 0; i < Samples.Count; i++)
                        {
                            var key = ((int)Math.Floor(Samples[i].P.X / kCell), (int)Math.Floor(Samples[i].P.Z / kCell));
                            if (!m_Cells.TryGetValue(key, out var list))
                                m_Cells[key] = list = new List<int>();
                            list.Add(i);
                        }
                    }
                    return m_Cells;
                }
            }

            /// <summary>离 v 最近的采样点（只看 kCell 以内）；没有时返回 −1。</summary>
            public int Nearest(Vec3 v, out float distance)
            {
                int cx = (int)Math.Floor(v.X / kCell), cz = (int)Math.Floor(v.Z / kCell), best = -1;
                float bestD = kCell * kCell;
                for (int i = cx - 1; i <= cx + 1; i++)
                    for (int j = cz - 1; j <= cz + 1; j++)
                        if (Cells.TryGetValue((i, j), out var list))
                            foreach (int k in list)
                            {
                                float dx = Samples[k].P.X - v.X, dz = Samples[k].P.Z - v.Z, d = dx * dx + dz * dz;
                                if (d < bestD)
                                {
                                    bestD = d;
                                    best = k;
                                }
                            }
                distance = MathF.Sqrt(bestD);
                return best;
            }
        }

        private sealed class Plan
        {
            private readonly InterchangeParams P;
            public readonly List<string> Errors = new List<string>();
            public bool Fits => Errors.Count == 0;
            public string Summary = "";

            /// <summary>建立布置时的基层高度（缓存的布置用于别的基层时，输出按差值平移）。</summary>
            public float BaseHeight => h0;

            private readonly Vec3[] d = new Vec3[3], R = new Vec3[3];
            /// <summary>o：立交内每一幅中线离道路中线的距离（拉开后的）；oOuter：臂端原来的；legShift：臂端 S 形接回的长度。</summary>
            private readonly float[] o = new float[3], oOuter = new float[3], legShift = new float[3], e = new float[3], w = new float[3];
            /// <summary>每条路进向一幅的右转、左转目标。</summary>
            private readonly int[] rightOf = new int[3], leftOf = new int[3];
            private readonly float h0, g, up, rw, gap, grade, rise;
            private readonly bool baseElevated;

            /// <summary>求出的布置：每条路的分岔 / 并合距离，六条匝道。</summary>
            private float[] D = new float[3];
            private List<Ramp> ramps = new List<Ramp>();

            private static readonly string[] kLetters = { "A", "B", "C" };

            public Plan(InterchangeParams p)
            {
                P = p;
                var legs = p.Legs;
                for (int i = 0; i < 3; i++)
                {
                    d[i] = Flat(legs[i].Direction);
                    R[i] = Directions.RightOf(d[i]);
                    w[i] = legs[i].RoadWidth;
                    // 两幅之间至少留 kMinSplitMedian（对准的原有双幅道路分隔带可能量出 0 米）
                    oOuter[i] = Math.Max(legs[i].CarriagewayOffset, w[i] / 2f + TrumpetTemplate.kMinSplitMedian / 2f);
                    e[i] = Math.Max(0f, (w[i] - p.RampWidth) / 2f);
                    // 里侧两条左转匝道（各贴一幅的内侧边缘）之间至少空 kInnerGap：分隔带窄时立交内两幅向外拉开
                    o[i] = Math.Max(oOuter[i], e[i] + (p.RampWidth + kInnerGap) / 2f);
                    // 拉开很少时不加 S 形：臂端和分岔点必须是同一个节点（差几厘米游戏会建成两个挨着的节点，立柱重叠、形状无效）
                    if (o[i] - oOuter[i] < kMinLegShift)
                        oOuter[i] = o[i];
                    float delta = o[i] - oOuter[i];
                    float shiftRadius = LevelPlanner.BandOf(p.ElevationOf(legs[i])) == ElevationBand.Elevated
                        ? Math.Min(kLegShiftRadius, 35f * 35f / Math.Max(delta, 0.1f)) : kLegShiftRadius;
                    legShift[i] = delta > 0f ? 2f * shiftRadius * MathF.Sin((float)Math.Acos(1f - delta / (2f * shiftRadius))) : 0f;
                }
                // 进向一幅（朝中心行驶）右手边的那条路是右转目标，另一条是左转目标
                for (int i = 0; i < 3; i++)
                {
                    int a = (i + 1) % 3, b = (i + 2) % 3;
                    bool aRight = Directions.Dot(Directions.RightOf(-d[i]), d[a]) > 0f;
                    rightOf[i] = aRight ? a : b;
                    leftOf[i] = aRight ? b : a;
                }
                h0 = p.ElevationOf(legs[0]);
                g = LevelGap(p);
                up = h0 + g;
                rw = p.RampWidth;
                gap = Math.Max(1f, p.RampSideGap);
                grade = p.MaxGrade * kGradeMargin;
                rise = g / grade;
                baseElevated = LevelPlanner.BandOf(h0) == ElevationBand.Elevated;

                if (!Solve())
                    Errors.Add("左转匝道的跨线桥放不下：请加大最大坡度、减小层间高差，或把三条路的夹角调得更接近 120°");
            }

            // —— 几何 ——

            /// <summary>进向一幅中线上离中心 u 处（右转匝道起点在外侧边缘 −R·e，左转在内侧边缘 +R·e）。</summary>
            private Vec3 Inbound(int i, float u, float lateral = 0f) => d[i] * u - R[i] * o[i] + R[i] * lateral;

            /// <summary>出向一幅中线上离中心 u 处（右转匝道到达外侧边缘 +R·e，左转到达内侧边缘 −R·e）。</summary>
            private Vec3 Outbound(int i, float u, float lateral = 0f) => d[i] * u + R[i] * o[i] + R[i] * lateral;

            /// <summary>
            /// 臂上离中心 u 处一幅的中线（inbound：进向一幅）：分岔 / 并合点 D 以内是拉开后的 o，D + legShift 以外是原来的 oOuter，
            /// 中间按 S 形过渡（检查用，取平滑插值）。
            /// </summary>
            private Vec3 LegPoint(int i, float u, bool inbound)
            {
                float t = legShift[i] > 0f ? Math.Max(0f, Math.Min(1f, (u - D[i]) / legShift[i])) : 1f;
                float offset = o[i] + (oOuter[i] - o[i]) * (3f * t * t - 2f * t * t * t);
                return d[i] * u + R[i] * (inbound ? -offset : offset);
            }

            /// <summary>分岔 / 并合处两条匝道中线至少分开这么远才算分开（第一个节点放在这之后）。</summary>
            private float Apart => rw + (baseElevated ? kInnerGap : gap);

            private static List<(Vec3 P, Vec3 H)> SamplePath(Track track, float step)
            {
                var list = new List<(Vec3 P, Vec3 H)>();
                int n = Math.Max(1, (int)Math.Ceiling(track.Length / step));
                for (int i = 0; i <= n; i++)
                {
                    float s = track.Length * i / n;
                    list.Add((track.PointAt(s), track.HeadingAt(s)));
                }
                return list;
            }

            /// <summary>两条路径（俯视）的交点：各自的弧长和交角（弧度，0 ~ π/2）。</summary>
            private static List<(float SA, float SB, float Angle)> Intersections(Track a, Track b)
            {
                var result = new List<(float SA, float SB, float Angle)>();
                var pa = SamplePath(a, kStep);
                var pb = SamplePath(b, kStep);
                float stepA = a.Length / (pa.Count - 1), stepB = b.Length / (pb.Count - 1);
                // b 的线段按 20 米网格分桶，a 的每段只和同格的比
                const float cell = 20f;
                var cells = new Dictionary<(int, int), List<int>>();
                for (int j = 0; j + 1 < pb.Count; j++)
                {
                    var key = ((int)Math.Floor(pb[j].P.X / cell), (int)Math.Floor(pb[j].P.Z / cell));
                    if (!cells.TryGetValue(key, out var list))
                        cells[key] = list = new List<int>();
                    list.Add(j);
                }
                for (int i = 0; i + 1 < pa.Count; i++)
                {
                    Vec3 p0 = pa[i].P, p1 = pa[i + 1].P;
                    float minX = Math.Min(p0.X, p1.X), maxX = Math.Max(p0.X, p1.X), minZ = Math.Min(p0.Z, p1.Z), maxZ = Math.Max(p0.Z, p1.Z);
                    int cx = (int)Math.Floor(p0.X / cell), cz = (int)Math.Floor(p0.Z / cell);
                    var nearby = new List<int>();
                    for (int x = cx - 1; x <= cx + 1; x++)
                        for (int z = cz - 1; z <= cz + 1; z++)
                            if (cells.TryGetValue((x, z), out var list))
                                nearby.AddRange(list);
                    foreach (int j in nearby)
                    {
                        Vec3 q0 = pb[j].P, q1 = pb[j + 1].P;
                        if (Math.Max(q0.X, q1.X) < minX || Math.Min(q0.X, q1.X) > maxX || Math.Max(q0.Z, q1.Z) < minZ || Math.Min(q0.Z, q1.Z) > maxZ)
                            continue;
                        float dx = p1.X - p0.X, dz = p1.Z - p0.Z, ex = q1.X - q0.X, ez = q1.Z - q0.Z;
                        float den = dx * ez - dz * ex;
                        if (Math.Abs(den) < 1e-9f)
                            continue;
                        float fx = q0.X - p0.X, fz = q0.Z - p0.Z;
                        float s = (fx * ez - fz * ex) / den, t = (fx * dz - fz * dx) / den;
                        if (s < 0f || s >= 1f || t < 0f || t >= 1f)
                            continue;
                        float angle = (float)Math.Acos(Math.Min(1f, Math.Abs(Directions.Dot(pa[i].H, pb[j].H))));
                        result.Add(((i + s) * stepA, (j + t) * stepB, angle));
                    }
                }
                return result;
            }

            /// <summary>
            /// 上面那条（over）在交叉处不能放立柱的范围（沿它的弧长）：立柱是垂直于桥面、和桥面一样宽的薄墙，
            /// 墙在下面那条路法向上的伸展加上它的半宽，离下面那条路中线不够远就压在路上。
            /// </summary>
            private (float Lo, float Hi) PillarZone(Track over, float at, Track under, float underWidth)
            {
                var below = SamplePath(under, 1f);
                bool Blocked(float s)
                {
                    Vec3 v = over.PointAt(s), h = over.HeadingAt(s);
                    float best = float.MaxValue;
                    Vec3 bh = Vec3.Zero;
                    foreach (var (q, qh) in below)
                    {
                        float dd = (q.X - v.X) * (q.X - v.X) + (q.Z - v.Z) * (q.Z - v.Z);
                        if (dd < best)
                        {
                            best = dd;
                            bh = qh;
                        }
                    }
                    float cos = Math.Abs(Directions.Dot(h, bh)), sin = MathF.Sqrt(Math.Max(0f, 1f - cos * cos));
                    float reach = underWidth / 2f + rw / 2f * cos + kPillarThickness / 2f * sin + 1f;
                    return MathF.Sqrt(best) < reach;
                }
                float lo = at, hi = at;
                while (lo > 0f && Blocked(lo - 1f))
                    lo -= 1f;
                while (hi < over.Length && Blocked(hi + 1f))
                    hi += 1f;
                return (lo - 1f, hi + 1f);
            }

            /// <summary>下面那条（under）在交叉处与上面那条路面重叠、必须留在基层的范围（沿它的弧长）。</summary>
            private (float Lo, float Hi) DeckZone(Track under, float at, Track over)
            {
                var above = SamplePath(over, 1f);
                float reach = rw + gap;
                bool Near(float s)
                {
                    Vec3 v = under.PointAt(s);
                    return above.Any(q => (q.P.X - v.X) * (q.P.X - v.X) + (q.P.Z - v.Z) * (q.P.Z - v.Z) < reach * reach);
                }
                float lo = at, hi = at;
                while (lo > 0f && Near(lo - 1f))
                    lo -= 1f;
                while (hi < under.Length && Near(hi + 1f))
                    hi += 1f;
                return (lo, hi);
            }

            /// <summary>
            /// 从同一节点出发（或到达同一节点）的两条路径：a 离开 b 到边缘相距 gap 的长度（并行重叠长度）。
            /// fromEnd 时从两条路径的终点往回量。
            /// </summary>
            private float Separation(Track a, Track b, bool fromEnd)
            {
                // 只看 b 在这个节点附近的一段（并行段不会很长）
                const float window = 400f;
                var other = new List<Vec3>();
                for (float s = 0f; s <= Math.Min(window, b.Length); s += 2f)
                    other.Add(b.PointAt(fromEnd ? b.Length - s : s));
                for (float s = 0f; s <= a.Length; s += 2f)
                {
                    Vec3 v = a.PointAt(fromEnd ? a.Length - s : s);
                    float best = other.Min(q => (q.X - v.X) * (q.X - v.X) + (q.Z - v.Z) * (q.Z - v.Z));
                    if (MathF.Sqrt(best) >= Apart)
                        return s;
                    if (s > window - 50f)
                        return s;
                }
                return a.Length;
            }

            // —— 求解 ——

            /// <summary>调试：各种失败原因的次数。</summary>
            private readonly Dictionary<string, int> m_Fails = new Dictionary<string, int>();

            private bool Fail(string reason)
            {
                m_Fails[reason] = m_Fails.TryGetValue(reason, out int n) ? n + 1 : 1;
                return false;
            }

            private bool Solve()
            {
                for (float c = kMinCore; c <= kMaxCore; c += kCoreStep)
                    foreach (float rL in kLeftRadii)
                        if (TryCore(c, rL))
                        {
                            Summary = $"core {c:F0} m, left r {rL:F0}, forks {string.Join("/", D.Select(x => x.ToString("F0")))} m, " +
                                $"right r {string.Join("/", ramps.Where(r => !r.Left).Select(r => r.Radius.ToString("F0")))}";
                            return true;
                        }
                Trace?.Invoke("fails: " + string.Join(", ", m_Fails.Select(kv => $"{kv.Key} {kv.Value}")));
                return false;
            }

            private bool TryCore(float c, float rL)
            {
                // 1. 三条左转匝道的核心：从进向一幅内侧边缘离中心 c 处起转，到左转目标出向一幅内侧边缘离中心 c 处
                var cores = new Track[3];
                for (int i = 0; i < 3; i++)
                {
                    int k = leftOf[i];
                    var core = Csc(Inbound(i, c, e[i]), -d[i], Outbound(k, c, -e[k]), d[k], true, rL);
                    if (core == null)
                        return Fail("core");
                    cores[i] = core;
                }
                // 2. 每条路里侧一处交叉：从这条路出发的（上）跨过到达这条路的（下）；每一对正好交叉一次
                var over = new (float At, float Lo, float Hi)[3];     // 出发的左转匝道在自己核心上的交叉点和不能放立柱的范围
                var under = new (float At, float Lo, float Hi)[3];    // 到达的左转匝道（从 arriving[i] 出发）在它核心上的交叉点和必须在基层的范围
                for (int i = 0; i < 3; i++)
                {
                    int j = Array.IndexOf(leftOf, i);
                    var hits = Intersections(cores[i], cores[j]);
                    if (hits.Count != 1)
                        return Fail($"crossings {hits.Count}");
                    var (sa, sb, _) = hits[0];
                    var zone = PillarZone(cores[i], sa, cores[j], rw);
                    if (zone.Hi - zone.Lo + 2f * kPlateauMargin > kMaxElevatedPiece)
                        return Fail("shallow");   // 交叉太斜：跨越段一跨放不下
                    over[i] = (sa, zone.Lo, zone.Hi);
                    var deck = DeckZone(cores[j], sb, cores[i]);
                    under[j] = (sb, deck.Lo, deck.Hi);
                }
                // 核心上：先跨（出发处），后穿（到达处）
                for (int i = 0; i < 3; i++)
                    if (over[i].Hi >= under[i].Lo)
                        return Fail("order");

                // 3. 直线段 t：出发的左转匝道要在分岔并行段之后爬到第二层，再到不能放立柱的范围之前；按右转匝道算出的并行段迭代
                var t = new float[3];
                for (int iteration = 0; iteration < 10; iteration++)
                {
                    for (int i = 0; i < 3; i++)
                        D[i] = c + t[i];
                    if (!BuildRamps(cores))
                        return Fail("ramps");
                    bool changed = false;
                    for (int i = 0; i < 3; i++)
                    {
                        var left = ramps.First(r => r.Left && r.From == i);
                        float need = left.FlatStart + rise + kPlateauMargin - over[i].Lo;
                        float next = Math.Max(0f, (float)Math.Ceiling(need / 5f) * 5f);
                        if (next > t[i] + 0.5f)
                        {
                            t[i] = next;
                            changed = true;
                        }
                    }
                    if (!changed)
                        break;
                    if (iteration == 9)
                        return Fail("t");
                }

                // 4. 左转匝道的高度：基层 → 爬到第二层 → 跨过 → 降回基层（在到达处穿过之前）
                foreach (var left in ramps.Where(r => r.Left))
                {
                    int i = left.From, k = left.To;
                    float p0 = t[i] + over[i].Lo - kPlateauMargin, p1 = t[i] + over[i].Hi + kPlateauMargin;
                    float climb = p0 - rise, q = p1 + rise, under0 = t[i] + under[i].Lo, flatEnd = left.Length - left.FlatEnd;
                    if (climb < left.FlatStart - 0.5f || q > under0 - kPlateauMargin)
                        return Fail("profile");
                    // 起坡点离分岔并行段终点很近时从并行段终点就开始爬（坡度略缓），降回基层的点离并合并行段起点很近时降到那里，不留一小段
                    if (climb - left.FlatStart < kMinPiece)
                        climb = left.FlatStart;
                    if (flatEnd > q && flatEnd - q < kMinPiece && flatEnd <= under0 - kPlateauMargin)
                        q = flatEnd;
                    left.Knots = new List<(float S, float H)> { (0f, h0), (climb, h0), (p0, up), (p1, up), (q, h0), (left.Length, h0) };
                }

                // 5. 各匝道、各幅道路互不相碰；跨线桥分段放得下立柱
                var bodies = Bodies();
                if (!Clear(bodies))
                    return Fail("clear");
                foreach (var ramp in ramps)
                {
                    var cuts = PlanCuts(ramp, bodies);
                    if (cuts == null)
                        return Fail("cuts");
                    ramp.Cuts = cuts;
                }
                return true;
            }

            /// <summary>按当前的分岔 / 并合距离 D 建六条匝道（左转用给定核心，右转求最大半径），求两头并行重叠长度。</summary>
            private bool BuildRamps(Track[] cores)
            {
                ramps = new List<Ramp>();
                for (int i = 0; i < 3; i++)
                {
                    int j = rightOf[i], k = leftOf[i];
                    // 右转匝道：在分岔点先向外 S 形横移到离左转匝道 Apart（并行段短），中间“圆弧 – 直线 – 圆弧”，到并合点前再向里 S 形横移回来
                    float fr = baseElevated ? kElevatedForkShiftRadius : kForkShiftRadius;
                    float sStart = Math.Max(0f, Apart - 2f * e[i]), sEnd = Math.Max(0f, Apart - 2f * e[j]);
                    float aStart = (float)Math.Acos(1f - sStart / (2f * fr)), aEnd = (float)Math.Acos(1f - sEnd / (2f * fr));
                    var right = new Track(Inbound(i, D[i], -e[i]), -d[i]).Arc(fr, -aStart).Arc(fr, aStart);
                    Vec3 endPre = Outbound(j, D[j], e[j] + sEnd) - d[j] * (2f * fr * MathF.Sin(aEnd));
                    var middle = WidestCsc(right.End, right.EndHeading, endPre, d[j], false, kMinRightRadius, kMaxRadius, kMinArcShare, out float radius);
                    if (middle == null)
                        return false;
                    right.Append(middle).Arc(fr, aEnd).Arc(fr, -aEnd);
                    ramps.Add(new Ramp
                    {
                        Name = $"Right{kLetters[i]}{kLetters[j]}", From = i, To = j, Left = false, Radius = radius, Path = right,
                        StartNode = Inbound(i, D[i]), EndNode = Outbound(j, D[j]),
                    });
                    float tStart = D[i] - Math.Max(0f, Directions.Dot(cores[i].Start, d[i]));
                    float tEnd = D[k] - Math.Max(0f, Directions.Dot(cores[i].End, d[k]));
                    var left = new Track(Inbound(i, D[i], e[i]), -d[i]).Line(tStart).Append(cores[i]).Line(tEnd);
                    ramps.Add(new Ramp
                    {
                        Name = $"Left{kLetters[i]}{kLetters[k]}", From = i, To = k, Left = true, Path = left,
                        StartNode = Inbound(i, D[i]), EndNode = Outbound(k, D[k]),
                    });
                }
                foreach (var ramp in ramps)
                {
                    // 同一分岔点出发的另一条、同一并合点到达的另一条
                    var sibling = ramps.First(r => r != ramp && r.From == ramp.From);
                    var arriving = ramps.First(r => r != ramp && r.To == ramp.To);
                    ramp.FlatStart = Separation(ramp.Path, sibling.Path, false) + kFlatMargin;
                    ramp.FlatEnd = Separation(ramp.Path, arriving.Path, true) + kFlatMargin;
                    if (ramp.FlatStart + ramp.FlatEnd > ramp.Length)
                        return false;
                    // 高架时并行段要在一段里（游戏切分高架路段），节点不能落在并行段中间
                    if (baseElevated && (ramp.FlatStart > kMaxElevatedPiece - 1f || ramp.FlatEnd > kMaxElevatedPiece - 1f))
                        return false;
                    if (!ramp.Left)
                        ramp.Knots = new List<(float S, float H)> { (0f, h0), (ramp.Length, h0) };
                }
                return true;
            }

            /// <summary>检查用：六条匝道和六幅道路（臂端直线）的采样。</summary>
            private List<Body> Bodies()
            {
                var bodies = new List<Body>();
                foreach (var ramp in ramps)
                {
                    var body = new Body
                    {
                        Name = ramp.Name, Width = rw, StartNode = ramp.StartNode, EndNode = ramp.EndNode,
                        FreeStart = ramp.FlatStart, FreeEnd = ramp.FlatEnd, Length = ramp.Length,
                    };
                    int n = Math.Max(1, (int)Math.Ceiling(ramp.Length / kStep));
                    for (int k = 0; k <= n; k++)
                    {
                        float s = ramp.Length * k / n;
                        Vec3 v = ramp.Path.PointAt(s);
                        v.Y = ramp.HeightAt(s);
                        body.Samples.Add((v, ramp.Path.HeadingAt(s), s));
                    }
                    bodies.Add(body);
                }
                for (int i = 0; i < 3; i++)
                {
                    float stub = Math.Max(P.Legs[i].StubLength, kMinPiece), length = legShift[i] + stub;
                    foreach (bool inbound in new[] { true, false })
                    {
                        Vec3 a = inbound ? LegPoint(i, D[i] + length, true) : Outbound(i, D[i]);
                        Vec3 b = inbound ? Inbound(i, D[i]) : LegPoint(i, D[i] + length, false);
                        var body = new Body
                        {
                            Name = $"{kLetters[i]}-{(inbound ? "in" : "out")}", Width = w[i], StartNode = a, EndNode = b,
                            FreeStart = inbound ? 0f : length, FreeEnd = inbound ? length : 0f, Length = length,
                        };
                        int n = Math.Max(1, (int)Math.Ceiling(length / kStep));
                        for (int k = 0; k <= n; k++)
                        {
                            float u = inbound ? D[i] + length * (1f - (float)k / n) : D[i] + length * k / n;
                            Vec3 v = LegPoint(i, u, inbound);
                            v.Y = h0;
                            body.Samples.Add((v, inbound ? -d[i] : d[i], length * k / n));
                        }
                        bodies.Add(body);
                    }
                }
                return bodies;
            }

            /// <summary>
            /// 互不相碰：两条路平面上挨着（路面相距不到半个间距）的地方高度至少差一层高差的 90%。
            /// 共用节点的两条路在各自并行段（Free）里不查。
            /// </summary>
            private bool Clear(List<Body> bodies)
            {
                for (int a = 0; a < bodies.Count; a++)
                    for (int b = a + 1; b < bodies.Count; b++)
                    {
                        var A = bodies[a];
                        var B = bodies[b];
                        bool shareStart = A.StartNode.Equals(B.StartNode) || A.StartNode.Equals(B.EndNode);
                        bool shareEnd = A.EndNode.Equals(B.StartNode) || A.EndNode.Equals(B.EndNode);
                        float reach = (A.Width + B.Width) / 2f + gap * 0.5f;
                        foreach (var (v, _, s) in A.Samples)
                        {
                            if ((shareStart && s <= A.FreeStart) || (shareEnd && s >= A.Length - A.FreeEnd))
                                continue;
                            int k = B.Nearest(v, out float dist);
                            if (k < 0 || dist >= reach)
                                continue;
                            float sb = B.Samples[k].S;
                            bool bStartShared = B.StartNode.Equals(A.StartNode) || B.StartNode.Equals(A.EndNode);
                            bool bEndShared = B.EndNode.Equals(A.StartNode) || B.EndNode.Equals(A.EndNode);
                            if ((bStartShared && sb <= B.FreeStart) || (bEndShared && sb >= B.Length - B.FreeEnd))
                                continue;
                            if (Math.Abs(v.Y - B.Samples[k].P.Y) < g * 0.9f)
                            {
                                Trace?.Invoke($"clash {A.Name} x {B.Name} near ({v.X:F0}, {v.Z:F0})");
                                return false;
                            }
                        }
                    }
                return true;
            }

            /// <summary>
            /// 匝道分段：断点含两头并行段、高度折点和直线 / 圆弧交接处；高架部分每段不超过 kMaxElevatedPiece、转角不超过 kMaxPieceTurn；
            /// 高架部分另加的断点（立柱）不落在更低的道路上方。放不下时返回 null。
            /// </summary>
            private List<float>? PlanCuts(Ramp ramp, List<Body> bodies)
            {
                float total = ramp.Length;
                var self = bodies.First(b => b.Name == ramp.Name);
                // 不能放立柱的范围：这条匝道高出基层、下面有更低的道路挨着（按立柱薄墙的伸展判断）
                var blocked = new List<(float Lo, float Hi)>();
                foreach (var (v, h, s) in self.Samples)
                {
                    if (v.Y < h0 + 1f)
                        continue;
                    foreach (var other in bodies)
                    {
                        if (other == self)
                            continue;
                        int k = other.Nearest(v, out float dist);
                        if (k < 0 || other.Samples[k].P.Y > v.Y - 1f)
                            continue;
                        float cos = Math.Abs(Directions.Dot(h, other.Samples[k].H)), sin = MathF.Sqrt(Math.Max(0f, 1f - cos * cos));
                        float reach = other.Width / 2f + rw / 2f * cos + kPillarThickness / 2f * sin + 1f;
                        if (dist < reach)
                        {
                            blocked.Add((s - kStep, s + kStep));
                            break;
                        }
                    }
                }
                bool Blocked(float s) => blocked.Any(z => s > z.Lo && s < z.Hi);
                // 离地达到桥梁高度的那段（游戏会按长度切分、加立柱）
                bool Elevated(float a, float b)
                {
                    for (float s = a; s <= b; s += kStep)
                        if (ramp.HeightAt(s) >= HeightRange.BridgeThreshold - 0.5f)
                            return true;
                    return ramp.HeightAt(b) >= HeightRange.BridgeThreshold - 0.5f;
                }

                var required = new List<float> { 0f, total };
                foreach (float s in new[] { ramp.FlatStart, total - ramp.FlatEnd }.Concat(ramp.Knots.Select(k => k.S)))
                    if (s > 1f && s < total - 1f)
                        required.Add(s);
                var sorted = required.OrderBy(s => s).ToList();
                for (int i = sorted.Count - 1; i > 0; i--)
                    if (sorted[i] - sorted[i - 1] < 1f)
                        sorted.RemoveAt(i == sorted.Count - 1 ? i - 1 : i);
                for (int i = 1; i < sorted.Count; i++)
                    if (sorted[i] - sorted[i - 1] < kMinSegment)
                        return null;
                // 直线 / 圆弧交接处（不在两头并行段里、离已有断点不太近时）
                foreach (float s in ramp.Path.Joints())
                    if (s > ramp.FlatStart && s < total - ramp.FlatEnd && sorted.All(c => Math.Abs(c - s) >= kMinPiece) && !Blocked(s))
                    {
                        sorted.Add(s);
                        sorted.Sort();
                    }
                foreach (float s in sorted)
                    if (s > 0f && s < total && ramp.HeightAt(s) > h0 + 1f && Blocked(s))
                        return null;

                var cuts = new List<float> { sorted[0] };
                for (int i = 0; i + 1 < sorted.Count; i++)
                {
                    float a = sorted[i], b = sorted[i + 1];
                    bool elevated = Elevated(a, b);
                    float maxLength = elevated ? kMaxElevatedPiece : kMaxGroundPiece;
                    int n = Math.Max(1, Math.Max((int)Math.Ceiling((b - a) / maxLength), (int)Math.Ceiling(ramp.Path.TurnBetween(a, b) / kMaxPieceTurn)));
                    List<float>? picks = null;
                    for (int extra = 0; extra < 4 && picks == null; extra++)
                    {
                        int m = n + extra;
                        var trial = new List<float>();
                        float prev = a;
                        bool ok = true;
                        for (int k = 1; k < m && ok; k++)
                        {
                            float target = a + (b - a) * k / m;
                            float best = float.NaN;
                            for (float off = 0f; off <= maxLength / 2f && float.IsNaN(best); off += 1f)
                                foreach (float s in new[] { target - off, target + off })
                                    if (s - prev >= kMinPiece && b - s >= kMinPiece && s - prev <= maxLength && (b - s) <= maxLength * (m - k)
                                        && !(elevated && Blocked(s)) && ramp.Path.TurnBetween(prev, s) <= kMaxPieceTurn)
                                    {
                                        best = s;
                                        break;
                                    }
                            if (float.IsNaN(best))
                                ok = false;
                            else
                            {
                                trial.Add(best);
                                prev = best;
                            }
                        }
                        if (ok && b - prev <= maxLength && ramp.Path.TurnBetween(prev, b) <= kMaxPieceTurn)
                            picks = trial;
                    }
                    if (picks == null)
                        return null;
                    cuts.AddRange(picks);
                    cuts.Add(b);
                }
                return cuts;
            }

            // —— 输出 ——

            public void Build(List<RoadSegmentSpec> segments)
            {
                if (!Fits)
                    return;
                var legs = P.Legs;
                for (int i = 0; i < 3; i++)
                {
                    var leg = legs[i];
                    var role = i == 0 ? RoadRole.MainRoad : RoadRole.CrossRoad;
                    float stub = Math.Max(leg.StubLength, kMinPiece), shift = legShift[i];
                    int pieces = baseElevated ? (int)Math.Ceiling(stub / kMaxElevatedPiece) : 1;
                    Vec3 At(Vec3 v) => new Vec3(v.X, h0, v.Z);
                    // 分岔 / 并合点到 D + shift：S 形从拉开后的位置接回原来的分隔带
                    if (shift > 0f)
                    {
                        Vec3 inS = At(LegPoint(i, D[i] + shift, true)), inE = At(Inbound(i, D[i]));
                        Vec3 outS = At(Outbound(i, D[i])), outE = At(LegPoint(i, D[i] + shift, false));
                        segments.Add(new RoadSegmentSpec($"{kLetters[i]}-in-shift", role,
                            new Bezier3(inS, inS - d[i] * (shift / 3f), inE + d[i] * (shift / 3f), inE), leg.RoadPrefab));
                        segments.Add(new RoadSegmentSpec($"{kLetters[i]}-out-shift", role,
                            new Bezier3(outS, outS + d[i] * (shift / 3f), outE - d[i] * (shift / 3f), outE), leg.RoadPrefab));
                    }
                    Vec3 inFrom = At(LegPoint(i, D[i] + shift + stub, true)), inTo = At(LegPoint(i, D[i] + shift, true));
                    Vec3 outFrom = At(LegPoint(i, D[i] + shift, false)), outTo = At(LegPoint(i, D[i] + shift + stub, false));
                    // 外部是双向道路：两幅外端接到双向道路端点（臂端过渡），再接一段双向道路
                    Vec3? joint = leg.NeedsDualToTwoWayTransition ? At(d[i] * (D[i] + shift + stub)) : (Vec3?)null;
                    for (int k = 0; k < pieces; k++)
                    {
                        Vec3 a = Vec3.Lerp(inFrom, inTo, (float)k / pieces), b = Vec3.Lerp(inFrom, inTo, (float)(k + 1) / pieces);
                        segments.Add(new RoadSegmentSpec($"{kLetters[i]}-in-{k + 1}", role, Bezier3.Line(a, b), leg.RoadPrefab,
                            k == 0 ? joint : null, null));
                        a = Vec3.Lerp(outFrom, outTo, (float)k / pieces);
                        b = Vec3.Lerp(outFrom, outTo, (float)(k + 1) / pieces);
                        segments.Add(new RoadSegmentSpec($"{kLetters[i]}-out-{k + 1}", role, Bezier3.Line(a, b), leg.RoadPrefab,
                            null, k == pieces - 1 ? joint : null));
                    }
                    if (joint is Vec3 j)
                    {
                        Vec3 tailEnd = At(d[i] * (D[i] + shift + stub + leg.StubLength));
                        int tailPieces = baseElevated ? (int)Math.Ceiling(leg.StubLength / kMaxElevatedPiece) : 1;
                        for (int k = 0; k < tailPieces; k++)
                            segments.Add(new RoadSegmentSpec($"{kLetters[i]}-tail{(tailPieces > 1 ? $"-{k + 1}" : "")}", role,
                                Bezier3.Line(Vec3.Lerp(j, tailEnd, (float)k / tailPieces), Vec3.Lerp(j, tailEnd, (float)(k + 1) / tailPieces)), leg.ExternalPrefab));
                    }
                }
                foreach (var ramp in ramps)
                    Emit(segments, ramp);
            }

            /// <summary>按断点输出一条匝道：断点处高度取高度折线，相邻断点之间线性升降；手柄按弦长和两端方向夹角（圆弧时就是圆弧）。</summary>
            private void Emit(List<RoadSegmentSpec> segments, Ramp ramp)
            {
                var cuts = ramp.Cuts;
                for (int i = 0; i + 1 < cuts.Count; i++)
                {
                    Vec3 a = ramp.Path.PointAt(cuts[i]), b = ramp.Path.PointAt(cuts[i + 1]);
                    a.Y = ramp.HeightAt(cuts[i]);
                    b.Y = ramp.HeightAt(cuts[i + 1]);
                    Vec3 ha = ramp.Path.HeadingAt(cuts[i]), hb = ramp.Path.HeadingAt(cuts[i + 1]);
                    float delta = (float)Math.Acos(Math.Max(-1f, Math.Min(1f, Directions.Dot(ha, hb))));
                    float chord = Vec3.Distance(Flat0(a), Flat0(b));
                    float cosQuarter = MathF.Cos(delta / 4f);
                    float k = chord / (3f * cosQuarter * cosQuarter);
                    Vec3 p1 = a + ha * k, p2 = b - hb * k;
                    p1.Y = a.Y + (b.Y - a.Y) / 3f;
                    p2.Y = a.Y + (b.Y - a.Y) * 2f / 3f;
                    Vec3 startNode = new Vec3(ramp.StartNode.X, h0, ramp.StartNode.Z), endNode = new Vec3(ramp.EndNode.X, h0, ramp.EndNode.Z);
                    segments.Add(new RoadSegmentSpec($"{ramp.Name}-{i + 1}", RoadRole.Ramp, new Bezier3(a, p1, p2, b), P.RampPrefab,
                        i == 0 ? startNode : (Vec3?)null, i + 2 == cuts.Count ? endNode : (Vec3?)null));
                }
            }
        }
    }
}
