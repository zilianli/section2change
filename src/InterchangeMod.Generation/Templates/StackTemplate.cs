using System;
using System.Collections.Generic;
using System.Linq;
using InterchangeMod.Generation.Geometry;
using static InterchangeMod.Generation.Templates.Planar;

namespace InterchangeMod.Generation.Templates
{
    /// <summary>
    /// 四层定向式立交（Stack，四向全互通，右侧通行）。主线（臂 0/2，双幅）在第 1 层（基层），被交道路（臂 1/3，双幅）在第 2 层跨过主线；
    /// 四个象限角上各一条贴着转角的右转直连匝道（与两条路都相切，在第 1、2 层之间升降）；四条左转匝道右出右进：从来向那幅右侧驶出，
    /// 沿它直行升到桥面，转一个大弯斜着跨过两条路，再沿去向那幅直行下降，从右侧汇入。转角小的一对在第 3 层、
    /// 另一对在第 4 层，正交时从主线出发的在第 3 层（同层的两条在对角，互不相交；不同层的两两交叉）。
    ///
    /// 坐标：每一幅是一条直线（起点 Origin、行驶方向 Dir），立交中心为原点。左转匝道的转弯半径从小到大找第一个可行的，
    /// 升降放在两段直线里（长度按坡度求），跨越其他道路的范围前后保持这一层高度；跨线桥的立柱不落在下面的道路上，
    /// 高架段每段短于游戏切分长度。立交范围内两条路的中央分隔带拉宽到能放立柱，两端 S 形接回。
    /// 右转匝道在左转匝道外侧，比它早驶出、晚汇入，平行走到转角再转小弯。面板可选左出左进（左转匝道在拉宽的分隔带里升降）。
    ///
    /// turbine = true 时是涡轮立交（三层），左转匝道的造型和高度见 StackTemplate.Turbine.cs。
    /// </summary>
    public sealed partial class StackTemplate : IInterchangeTemplate
    {
        private readonly bool m_Turbine;

        public StackTemplate(bool turbine = false)
        {
            m_Turbine = turbine;
        }

        private const float kMaxSkew = 30f;
        private const float kOppositeTolerance = 1f;
        private const float kGradeMargin = 0.98f;
        private const float kPillarMargin = 3f;
        /// <summary>立柱沿桥面方向的厚度（米）。</summary>
        private const float kPillarThickness = 3f;
        /// <summary>匝道与所接道路并行重叠的那段再多保持同高这么远（米）才开始升降。</summary>
        private const float kFlatMargin = 15f;
        /// <summary>右转匝道并行段之后再保持同高的长度（米）：贴角的右转匝道短，升降要尽早开始。</summary>
        private const float kRightFlatMargin = 5f;
        /// <summary>道路上的节点离匝道真正并行重叠段末端的最小距离（米）：节点本身有大小。</summary>
        private const float kNodeClearance = 5f;
        /// <summary>平行走的匝道与旁边道路（主路或另一条匝道）的净距（米）。</summary>
        private const float kParallelGap = 4f;
        /// <summary>右转匝道在转角处的最小转弯半径（米）。</summary>
        private const float kRightRadius = 60f;
        /// <summary>左出左进、道路在地下时，分隔带里并排的两条左转匝道之间给挡土墙多留的间距（米）。</summary>
        private const float kWallClearance = 8f;
        /// <summary>匝道在跨越其他道路的范围前后再多保持这一层高度的长度（米）。</summary>
        private const float kPlateauMargin = 2f;
        /// <summary>左转匝道在高架那头横移出 / 入的 S 形半径（米）：并行重叠段要短于游戏切分高架路段的长度。</summary>
        private const float kElevatedShiftRadius = 50f;
        private const float kMinSegment = 10f;
        /// <summary>道路按长度加节点时每段的最小长度（米）：太短的路段游戏报形状无效。</summary>
        private const float kMinPiece = 20f;
        /// <summary>高架路段每段的最大长度（米），短于游戏切分高架路段的 80 米。</summary>
        private const float kMaxElevatedPiece = 70f;
        private const float kMaxGroundPiece = 600f;
        /// <summary>高架路段放不下立柱时允许的最大长度（米）。</summary>
        private const float kHardMaxPiece = 76f;
        /// <summary>一段路的最大转角（弧度，约 80°）。</summary>
        private const float kMaxPieceTurn = 1.4f;
        /// <summary>两端把拉宽的分隔带接回原来位置的 S 形半径（米）。</summary>
        private const float kShiftRadius = 250f;
        /// <summary>最外侧匝道接点到 S 形之间的直线长度（米）。</summary>
        private const float kEndGap = 20f;
        private const float kStraight = 100000f;
        private const float kStep = 2f;
        /// <summary>匝道作为障碍物、检查间距时的采样间距（米），以及找跨越范围时的粗找步长。</summary>
        private const float kSampleStep = 4f;
        private const float kCoarseMark = 4f;
        /// <summary>左转匝道直线段的试探长度（米），按坡度需要截短。</summary>
        private const float kProbe = 800f;
        /// <summary>左转匝道跨越段连续压在下层道路上（放不下立柱）的最大长度（米），两边的立柱之间一段要短于 kMaxElevatedPiece。</summary>
        private const float kMaxBlocked = 68f;
        /// <summary>左转匝道转弯半径的搜索上限（米）与粗搜步长。</summary>
        private const float kMaxLeftRadius = 900f;
        /// <summary>左转复合弯道两头的小半径（米），以及两头各转过总转角的比例（0 = 单段圆弧）。</summary>
        private const float kLeftEndRadius = 80f;
        private static readonly float[] kTightFractions = { 0f, 0.3f, 0.45f };
        private const float kCoarseStep = 40f;
        /// <summary>左转匝道横向偏移的搜索步长与上限（米）。</summary>
        private const float kExtraStep = 5f;
        private const float kMaxExtra = 100f;
        /// <summary>右转匝道或立柱放不下时最多换几组左转匝道。</summary>
        private const int kAttempts = 6;
        /// <summary>
        /// 立交范围内中央分隔带比匝道宽出的最小宽度（米）：左转匝道斜跨时立柱（垂直于桥面、和桥面一样宽的薄墙）落在分隔带里，
        /// 两侧各离车道 1 米以上。
        /// </summary>
        private const float kPillarMedianExtra = 5f;
        /// <summary>识别出的隧道深度允许比 −13 米浅的余量（米）：原有隧道读出来常是 −12.9 米。</summary>
        private const float kTunnelTolerance = 0.5f;

        public InterchangeType Type => m_Turbine ? InterchangeType.Turbine : InterchangeType.Stack;

        /// <summary>调试：求解失败的原因。</summary>
        public static Action<string>? Trace;

        public string DisplayName => m_Turbine ? "涡轮" : "定向式";

        public int LegCount => 4;

        public IReadOnlyList<string> ValidateParams(InterchangeParams p)
        {
            var errors = p.ValidateLegsAndLevels(LegCount);
            if (errors.Count > 0)
                return errors;
            var legs = p.Legs;
            if (!p.RightHandTraffic)
                errors.Add($"{DisplayName}目前只支持右侧通行");
            if (Directions.AngleBetween(legs[0].Angle, legs[2].Angle) < 180f - kOppositeTolerance
                || Directions.AngleBetween(legs[1].Angle, legs[3].Angle) < 180f - kOppositeTolerance)
                errors.Add($"{DisplayName}要求两条道路都是直线（臂 0/2、臂 1/3 各自相对）");
            float crossing = Directions.AngleBetween(legs[0].Angle, legs[1].Angle);
            if (Math.Abs(crossing - 90f) > kMaxSkew)
                errors.Add($"两条道路夹角 {crossing:F0}°：{DisplayName}需要 {90f - kMaxSkew:F0}°–{90f + kMaxSkew:F0}°");
            if (legs[0].Form != RoadForm.Dual || legs[1].Form != RoadForm.Dual)
                errors.Add($"{DisplayName}的两条道路都需要是双幅（双向道路会在立交内拆成双幅、外端接回）");
            float hm = p.ElevationOf(legs[0]), hc = p.ElevationOf(legs[1]);
            float gap = DiamondTemplate.RequiredGap(p);
            if (hc - hm < gap - HeightRange.GapTolerance)
                errors.Add($"被交道路要在主线上方至少 {gap:F1} 米（第 2 层）：现在高差 {hc - hm:F1} 米");
            // 主线要从被交道路下面穿过：被交道路是高架，或主线在隧道里（被交道路可以在地面）
            else if (LevelPlanner.BandOf(hc) != ElevationBand.Elevated && hm > HeightRange.TunnelThreshold + kTunnelTolerance)
                errors.Add($"第 2 层离地 {hc:F1} 米会建成路堤（挡土墙），主线无法从下面穿过：被交道路需要至少 {HeightRange.BridgeThreshold:F1} 米，或主线在 {HeightRange.TunnelThreshold:F0} 米以下的隧道里");
            if (errors.Count > 0)
                return errors;
            errors.AddRange(PlanFor(p).Errors);
            return errors;
        }

        public IReadOnlyList<RoadSegmentSpec> Generate(InterchangeParams p)
        {
            string key = Key(p);
            if (key != m_SegmentsKey)
            {
                var built = new List<RoadSegmentSpec>();
                PlanFor(p).Build(built);
                InterchangeLayout.AddApproachSlopes(built, p);
                m_Segments = built;
                m_SegmentsKey = key;
            }
            return new List<RoadSegmentSpec>(m_Segments!);
        }

        /// <summary>缓存键：参数指纹去掉本模板用不到的匝道长度、分流角度（预览失败时工具会逐档加长匝道重试）。</summary>
        private static string Key(InterchangeParams p)
        {
            var q = p.Clone();
            q.RampLength = 0f;
            q.RampDivergeAngle = 0f;
            return q.Fingerprint();
        }

        private string? m_SegmentsKey;
        private List<RoadSegmentSpec>? m_Segments;
        /// <summary>最近求过的几组参数的结果（含失败的）：鼠标在几处路口之间来回移动时不重复求解。</summary>
        private readonly List<(string Key, Plan Plan)> m_Plans = new List<(string Key, Plan Plan)>();
        private const int kPlanCache = 8;

        private Plan PlanFor(InterchangeParams p)
        {
            string key = Key(p);
            int index = m_Plans.FindIndex(e => e.Key == key);
            if (index >= 0)
            {
                var hit = m_Plans[index];
                m_Plans.RemoveAt(index);
                m_Plans.Add(hit);
                return hit.Plan;
            }
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var plan = new Plan(p, m_Turbine);
            Trace?.Invoke($"solved in {watch.ElapsedMilliseconds} ms: {(plan.Fits ? "ok " + plan.Summary : plan.Errors[0])}");
            // 记下完整参数（只在调试日志打开时），便于在测试里还原游戏里的情形
            Trace?.Invoke($"params: {key}");
            m_Plans.Add((key, plan));
            if (m_Plans.Count > kPlanCache)
                m_Plans.RemoveAt(0);
            return plan;
        }

        private static RampArc Straight(Vec3 from, Vec3 heading, float length) => new RampArc(Flat0(from), Flat(heading), kStraight, Math.Max(0.01f, length) / kStraight);

        // —— 障碍物：匝道平面上挨着它时要在它上方（或下方）留出净空 ——

        private abstract class Obstacle
        {
            protected Obstacle(float width) { Width = width; }
            public float Width { get; }
            /// <summary>到 v 的俯视距离，以及最近处的高度。</summary>
            public abstract float Distance(Vec3 v, out float height);
            /// <summary>最近处的方向（直线道路用，判断立柱薄墙的伸展；采样路径返回零向量）。</summary>
            public virtual Vec3 Direction => Vec3.Zero;
        }

        /// <summary>直线道路（主路的一幅），同一高度。</summary>
        private sealed class LineObstacle : Obstacle
        {
            private readonly Vec3 m_Origin, m_Dir;
            private readonly float m_Height;

            public LineObstacle(Vec3 origin, Vec3 dir, float width, float height) : base(width)
            {
                m_Origin = Flat0(origin);
                m_Dir = Flat(dir);
                m_Height = height;
            }

            public override Vec3 Direction => m_Dir;

            public override float Distance(Vec3 v, out float height)
            {
                height = m_Height;
                return Math.Abs(DiamondTemplate.Cross2(Flat0(v) - m_Origin, m_Dir));
            }
        }

        /// <summary>按采样点表示的路径（匝道），带高度。采样点按 kCell 见方的网格分桶：只回答 kCell 以内的距离，更远时返回 kCell。</summary>
        private sealed class PathObstacle : Obstacle
        {
            private const float kCell = 20f;
            private readonly Dictionary<(int, int), List<Vec3>> m_Cells = new Dictionary<(int, int), List<Vec3>>();

            public PathObstacle(List<Vec3> points, float width) : base(width)
            {
                foreach (var q in points)
                {
                    var key = ((int)Math.Floor(q.X / kCell), (int)Math.Floor(q.Z / kCell));
                    if (!m_Cells.TryGetValue(key, out var list))
                        m_Cells[key] = list = new List<Vec3>();
                    list.Add(q);
                }
            }

            public override float Distance(Vec3 v, out float height)
            {
                height = 0f;
                int cx = (int)Math.Floor(v.X / kCell), cz = (int)Math.Floor(v.Z / kCell);
                float best = kCell * kCell;
                for (int i = cx - 1; i <= cx + 1; i++)
                    for (int j = cz - 1; j <= cz + 1; j++)
                    {
                        if (!m_Cells.TryGetValue((i, j), out var list))
                            continue;
                        foreach (var q in list)
                        {
                            float dx = q.X - v.X, dz = q.Z - v.Z, d = dx * dx + dz * dz;
                            if (d < best)
                            {
                                best = d;
                                height = q.Y;
                            }
                        }
                    }
                return MathF.Sqrt(best);
            }
        }

        /// <summary>一条匝道：路径、两头水平段、跨越段（保持 Level 高度的范围）、断点。</summary>
        private sealed class Ramp
        {
            public string Name = "";
            public RampPath Path;
            public float FlatStart, FlatEnd, Level, H0, H1, PlateauStart, PlateauEnd;
            /// <summary>两头水平段在真正并行重叠（横向分不开）之外多留的长度（米）。</summary>
            public float FlatMargin = kFlatMargin;
            public bool HasPlateau;
            public List<float> Cuts = new List<float>();
            /// <summary>两头所接的那一幅和接点位置（沿该幅）。</summary>
            public Carriageway From = null!, To = null!;
            public float FromAt, ToAt;
            /// <summary>左转匝道两段直线在匝道间距、立柱余量之外再离开主路的横向距离，以及转弯半径。</summary>
            public float Extra, Radius, Tight;
            /// <summary>左转匝道两段直线在按坡度需要的长度之外加长了多少，以及按新的加长量重新生成。</summary>
            public float Ext1, Ext2;
            public Func<float, float, Ramp?>? Rebuild;
            /// <summary>按弧长给定的高度（涡轮的左转匝道：按途中各处交叉要求求出的纵断面），给定时代替水平段 / 跨越段的升降。</summary>
            public List<(float S, float H)>? Knots;

            public float Length => Path.Length;

            public float HeightAt(float s)
            {
                float total = Length;
                if (Knots != null)
                {
                    if (s <= Knots[0].S)
                        return Knots[0].H;
                    for (int i = 0; i + 1 < Knots.Count; i++)
                        if (s <= Knots[i + 1].S)
                            return Knots[i].H + (Knots[i + 1].H - Knots[i].H) * (s - Knots[i].S) / Math.Max(1e-3f, Knots[i + 1].S - Knots[i].S);
                    return Knots[Knots.Count - 1].H;
                }
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
                return s < end ? Level + (H1 - Level) * (s - PlateauEnd) / Math.Max(1e-3f, end - PlateauEnd) : H1;
            }

            private List<Vec3>? m_Samples;
            private PathObstacle? m_Obstacle;

            /// <summary>作为障碍物（缓存；高度改了要调用 HeightsChanged）。</summary>
            public PathObstacle AsObstacle(float width) => m_Obstacle ??= new PathObstacle(Samples(), width);

            /// <summary>纵断面改了（挪动转折点）：丢掉按旧高度缓存的采样点和障碍物。</summary>
            public void HeightsChanged()
            {
                m_Samples = null;
                m_Obstacle = null;
            }

            public List<Vec3> Samples() => m_Samples ??= ComputeSamples();

            private List<Vec3> ComputeSamples()
            {
                var list = new List<Vec3>();
                int n = Math.Max(1, (int)(Length / kSampleStep));
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

        /// <summary>主路的一幅：直线，沿 Dir 行驶，高度 Height。</summary>
        private sealed class Carriageway
        {
            public Carriageway(string name, Vec3 origin, Vec3 dir, float width, float height, bool main)
            {
                Name = name;
                Origin = Flat0(origin);
                Dir = Flat(dir);
                Width = width;
                Height = height;
                Main = main;
            }

            public string Name { get; }
            public Vec3 Origin { get; }
            public Vec3 Dir { get; }
            public float Width { get; }
            public float Height { get; }
            public bool Main { get; }
            public Vec3 Right => Directions.RightOf(Dir);
            public Vec3 Point(float t) => Origin + Dir * t;
            public float At(Vec3 v) => Directions.Dot(Flat0(v) - Origin, Dir);
        }

        private sealed partial class Plan
        {
            private readonly InterchangeParams P;
            private readonly float hm, hc, g, rw, gap, grade, om, oc;
            /// <summary>
            /// 匝道与途中的道路、别的匝道上下错开的最小高差：层高差的 90%（匝道还在升降时允许略低于整层），但不低于游戏要求的净空
            /// （游戏内：涡轮的左转匝道在离层高差 0.5 米处跨过主线，只差 7.0 米，报净空不足）。
            /// </summary>
            private readonly float minRise;
            /// <summary>左转匝道左出左进（在中央分隔带一侧驶出、汇入），否则右出右进。</summary>
            private readonly bool leftExit;
            private readonly Carriageway[] lines;
            public readonly List<string> Errors = new List<string>();
            private readonly List<Ramp> rights = new List<Ramp>(), lefts = new List<Ramp>();

            /// <summary>涡轮立交（左转匝道绕中心旋转、高度逐处决定），否则定向式。</summary>
            private readonly bool turbine;

            public Plan(InterchangeParams p, bool turbine)
            {
                this.turbine = turbine;
                P = p;
                var legs = p.Legs;
                hm = p.ElevationOf(legs[0]);
                hc = p.ElevationOf(legs[1]);
                g = hc - hm;
                minRise = Math.Max(0.9f * g, DiamondTemplate.RequiredGap(p) - HeightRange.GapTolerance);
                rw = p.RampWidth;
                gap = p.RampSideGap;
                grade = p.MaxGrade * kGradeMargin;
                Vec3 v = Flat(legs[0].Direction), c = Flat(legs[1].Direction);
                // 立交范围内两条路的中央分隔带拉宽到能放左转匝道的立柱（两端 S 形接回原来的分隔带）
                leftExit = p.StackLeftExit && !turbine;
                // 左出左进：分隔带每侧并排走一条驶出、一条驶入的左转匝道（各离本幅 kParallelGap，两者之间也留 kParallelGap；
                // 道路在地下时两条匝道都在路堑里，挡土墙之间再多留 kWallClearance）
                float Median(float height)
                {
                    if (!leftExit)
                        return rw + kPillarMedianExtra;
                    float between = kParallelGap + (Underground(height) ? kWallClearance : 0f);
                    return Math.Max(rw + kPillarMedianExtra, 2f * (rw + kParallelGap) + between);
                }
                om = Math.Max(legs[0].CarriagewayOffset, (Median(hm) + legs[0].RoadWidth) / 2f);
                oc = Math.Max(legs[1].CarriagewayOffset, (Median(hc) + legs[1].RoadWidth) / 2f);
                Vec3 rv = Directions.RightOf(v), rc = Directions.RightOf(c);
                lines = new[]
                {
                    new Carriageway("MainA", rv * om, v, legs[0].RoadWidth, hm, true),
                    new Carriageway("MainB", -rv * om, -v, legs[0].RoadWidth, hm, true),
                    new Carriageway("CrossA", rc * oc, c, legs[1].RoadWidth, hc, false),
                    new Carriageway("CrossB", -rc * oc, -c, legs[1].RoadWidth, hc, false),
                };

                if (turbine)
                {
                    SolveTurbine();
                    return;
                }

                // 先左转（只避开两条路），再右转：右转匝道在左转匝道外侧，比它早驶出、晚汇入，与它平行走到转角再转弯，两者不交叉。
                // 右转匝道或立柱放不下时换一组左转匝道再试：每次排除第 4 层用的那组，每两次再排除第 3 层用的那组
                // 左出左进时右转匝道与左转匝道不在同一侧：先定贴着转角的右转匝道，左转匝道跨过它
                if (leftExit && !SolveCornerRights())
                {
                    Errors.Add("右转匝道放不下：请加大最大坡度或把交角调得更接近 90°");
                    return;
                }
                string? failure = null;
                for (int attempt = 0; attempt < kAttempts; attempt++)
                {
                    lefts.Clear();
                    if (!leftExit)
                        rights.Clear();
                    if (!SolveLefts())
                    {
                        failure ??= "左转匝道放不下：请加大最大坡度、减小层间高差或把交角调得更接近 90°";
                        break;
                    }
                    failure = !leftExit && !SolveRights()
                        ? "右转匝道放不下：请加大最大坡度或把交角调得更接近 90°"
                        : leftExit && !SeparateAttachments()
                        ? "左转匝道与右转匝道的出入口放不开：请加大最大坡度或把交角调得更接近 90°"
                        : !lefts.Concat(rights).All(PlanCuts) || !lines.All(l => LineCuts(l) != null)
                        ? "跨线桥的立柱无法避开下面的道路：请把交角调得更接近 90° 或改变层间高差"
                        : null;
                    if (failure == null)
                        return;
                    var upper = lefts.OrderByDescending(l => l.Level).First();
                    var lower = lefts.OrderBy(l => l.Level).First();
                    m_Banned.Add((upper.Level, upper.Radius, upper.Extra, upper.Tight));
                    if (attempt % 2 == 1)
                        m_Banned.Add((lower.Level, lower.Radius, lower.Extra, lower.Tight));
                }
                Errors.Add(failure!);
            }

            /// <summary>求解右转匝道、立柱失败后不再使用的左转匝道组（层高、半径、横向偏移）。</summary>
            private readonly HashSet<(float Level, float Radius, float Extra, float Tight)> m_Banned = new HashSet<(float Level, float Radius, float Extra, float Tight)>();

            public bool Fits => Errors.Count == 0;

            /// <summary>调试：每层左转匝道的半径、横向偏移、两头小半径的比例。</summary>
            public string Summary => string.Join("; ", lefts.GroupBy(l => l.Level).Select(gr => $"{gr.Key:F0} m: r {gr.First().Radius:F0} +{gr.First().Extra:F0} tight {gr.First().Tight}"));


            private Carriageway LeftTarget(Carriageway x) =>
                lines.Where(l => l.Main != x.Main).OrderByDescending(l => Directions.Dot(l.Dir, -x.Right)).First();

            private Carriageway RightTarget(Carriageway x) =>
                lines.Where(l => l.Main != x.Main).OrderByDescending(l => Directions.Dot(l.Dir, x.Right)).First();

            private float Sep(Carriageway line) => line.Width / 2f + rw / 2f + gap;

            /// <summary>左转匝道两段直线离那一幅中线的横向距离：与它的净距 kParallelGap，再加偏移 extra。</summary>
            private float LeftOffset(Carriageway line, float extra) => line.Width / 2f + rw / 2f + kParallelGap + extra;

            // —— 车道平衡：右出右进时每幅在右转匝道驶出点和汇入点之间减车道（中心线向中央分隔带平移，外侧直行车道边缘不变）；
            // 左出左进时在左转匝道驶出点和汇入点之间减车道（中心线向外侧平移，分隔带一侧边缘让给左转匝道）。
            // 匝道接入时外缘对齐所接那段道路的边缘（曲线端点横移、节点仍在道路上）——

            private string? ReducedPrefab(Carriageway line) => line.Main ? P.MainReducedPrefab : P.CrossReducedPrefab;

            private float ReducedWidth(Carriageway line) => line.Main ? P.MainReducedWidth : P.CrossReducedWidth;

            private bool Balanced(Carriageway line) => ReducedPrefab(line) != null && ReducedWidth(line) > 0f;

            /// <summary>减车道段中心线平移的距离：右出右进时向中央分隔带（左侧），左出左进时向外侧（右侧），见 Build。</summary>
            private float Drop(Carriageway line) => Balanced(line) ? (line.Width - ReducedWidth(line)) / 2f : 0f;

            /// <summary>道路在地面以下（路堑或隧道）。</summary>
            private static bool Underground(float height) => LevelPlanner.BandOf(height) is ElevationBand.Cutting or ElevationBand.Tunnel;

            /// <summary>
            /// 右转匝道在这一幅上接入处的横向位置（向右）：车道平衡或道路在地下时外缘对齐全宽道路的外缘
            /// （地下的匝道从道路中心线分出时与道路重叠太长，游戏报形状无效）。
            /// </summary>
            private float RightAttach(Carriageway line) => Balanced(line) || Underground(line.Height) ? Math.Max(0f, (line.Width - rw) / 2f) : 0f;

            /// <summary>
            /// 左转匝道在这一幅上接入处的横向位置（向右）：右出右进时和右转匝道一样外缘对齐全宽道路的外缘；左出左进时左转匝道
            /// 是分隔带一侧分出去的车道，车道平衡或道路在地下时外缘对齐分隔带一侧的边缘（游戏内：从中心线错开接入，汇入汇出处不顺）。
            /// </summary>
            private float LeftAttach(Carriageway line) =>
                !leftExit ? RightAttach(line) : Balanced(line) || Underground(line.Height) ? -Math.Max(0f, (line.Width - rw) / 2f) : 0f;

            private static float AlongS(float d, float sr) => 2f * sr * MathF.Sin((float)Math.Acos(Math.Max(-1f, 1f - d / (2f * sr))));

            private float Elevated(float h) => LevelPlanner.BandOf(h) == ElevationBand.Elevated ? 1f : 0f;

            /// <summary>匝道离开 / 接入一条直线道路时，并行重叠（横向不足 sep）的长度（沿路径，按采样）。</summary>
            private static float Overlap(RampPath path, Carriageway line, float sep, bool fromStart) => Overlap(path, line.Origin, line.Dir, sep, fromStart);

            private static float Overlap(RampPath path, Vec3 origin, Vec3 dir, float sep, bool fromStart)
            {
                float total = path.Length;
                for (float s = 0f; s <= total; s += 0.5f)
                {
                    Vec3 v = path.PointAt(fromStart ? s : total - s);
                    if (Math.Abs(DiamondTemplate.Cross2(Flat0(v) - Flat0(origin), Flat(dir))) >= sep)
                        return s;
                }
                return total;
            }

            /// <summary>
            /// 匝道最后的 S 形：从 from 横移到这一幅上的接入位置 attach（向右为正）。横移量按 from 的实际横向位置算
            /// （前面的“直线段”是半径很大的圆弧，几百米后会偏出约 1 米，在这里补回）。
            /// </summary>
            private static (RampArc, RampArc) EndShift(Vec3 from, Carriageway line, float attach, float r)
            {
                float shift = attach - Directions.Dot(Flat0(from) - line.Origin, line.Right);
                return SCurve(from, line.Dir, line.Right * (shift >= 0f ? 1f : -1f), Math.Abs(shift), r);
            }

            private static (RampArc, RampArc) SCurve(Vec3 from, Vec3 heading, Vec3 side, float d, float r)
            {
                float theta = (float)Math.Acos(Math.Max(-1f, 1f - d / (2f * r)));
                float sign = DiamondTemplate.Cross2(heading, side) >= 0f ? 1f : -1f;
                var first = new RampArc(Flat0(from), Flat(heading), r, sign * theta);
                var second = new RampArc(first.End, first.EndHeading, r, -sign * theta);
                return (first, second);
            }

            // —— 左转匝道 ——

            /// <summary>
            /// 左转匝道 X → T：S 形从 X 右侧离开到并行线（离 X 一个匝道间距加立柱余量）→ 直行 run1 → 左转半径 r 到 T 的方向，
            /// 落在 T 右侧的并行线上 → 直行 run2 → S 形从右侧接入 T。run1、run2 先取试探长度，按坡度截短。
            /// </summary>
            private Ramp? BuildLeft(Carriageway x, Carriageway t, float r, float extra, float tight, float level, IReadOnlyList<Obstacle> obstacles,
                float ext1 = 0f, float ext2 = 0f)
            {
                float dx = LeftOffset(x, extra), dt = LeftOffset(t, extra);
                float srX = Elevated(x.Height) > 0f ? kElevatedShiftRadius : 120f, srT = Elevated(t.Height) > 0f ? kElevatedShiftRadius : 120f;
                // 两段直线在来向 / 去向那幅的哪一侧：右出右进在右侧，左出左进在左侧
                float side = leftExit ? -1f : 1f;
                Vec3 run1Origin = x.Origin + x.Right * (side * dx), run2Origin = t.Origin + t.Right * (side * dt);
                // 左转弯道：两头各用小半径 kLeftEndRadius 先转过总转角的 tight 倍（很快转陡，斜跨道路的范围短），中间用半径 r；tight = 0 时是单段圆弧
                float turn = (float)Math.Acos(Math.Max(-1f, Math.Min(1f, Directions.Dot(x.Dir, t.Dir))));
                float endSep = kLeftEndRadius * (1f - MathF.Cos(tight * turn));
                var arcs = RampPath.Fillet(run1Origin, x.Dir, run2Origin, t.Dir, tight > 0f ? kLeftEndRadius : r, r, tight > 0f ? kLeftEndRadius : r,
                    endSep, endSep, out float aAt, out float bAt, out _, out _).Arcs;
                float ex = LeftAttach(x), et = LeftAttach(t);
                // 两头的横移：从接入位置到直线段（可能向右也可能向左）
                float shiftX = side * dx - ex, shiftT = et - side * dt;
                float alongX = AlongS(Math.Abs(shiftX), srX), alongT = AlongS(Math.Abs(shiftT), srT);
                RampPath Path(float run1, float run2)
                {
                    Vec3 start = x.Point(aAt - run1 - alongX) + x.Right * ex;
                    var (s1, s2) = SCurve(start, x.Dir, x.Right * Math.Sign(shiftX), Math.Abs(shiftX), srX);
                    var a1 = Straight(s2.End, x.Dir, run1);
                    var a2 = Straight(arcs[arcs.Count - 1].End, t.Dir, run2);
                    var (s3, s4) = EndShift(a2.End, t, et, srT);
                    return RampPath.FromArcs(new[] { s1, s2, a1 }.Concat(arcs).Concat(new[] { a2, s3, s4 }).ToArray());
                }
                var trial = Path(kProbe, kProbe);
                float flatS = Overlap(trial, x, Math.Min(dx, Sep(x)) - 0.5f, true) + kFlatMargin;
                float flatE = Overlap(trial, t, Math.Min(dt, Sep(t)) - 0.5f, false) + kFlatMargin;
                if ((Elevated(x.Height) > 0f && flatS > 75f) || (Elevated(t.Height) > 0f && flatE > 75f))
                    return null;
                var (first, last) = Marks(trial, flatS, flatE, obstacles);
                if (first < 0f)
                    return null;
                float p0 = first - kPlateauMargin, p1 = last + kPlateauMargin;
                float slackS = p0 - flatS - Math.Abs(level - x.Height) / grade;
                float slackE = trial.Length - flatE - p1 - Math.Abs(level - t.Height) / grade;
                if (slackS < 0f || slackE < 0f)
                    return null;
                float run1 = Math.Max(1f, kProbe - slackS) + ext1, run2 = Math.Max(1f, kProbe - slackE) + ext2;
                float cut1 = kProbe - run1;
                var path = Path(run1, run2);
                var ramp = new Ramp
                {
                    Path = path, FlatStart = flatS, FlatEnd = flatE, Level = level, H0 = x.Height, H1 = t.Height,
                    HasPlateau = true, PlateauStart = p0 - cut1, PlateauEnd = p1 - cut1,
                    From = x, To = t, FromAt = x.At(path.Start), ToAt = t.At(path.End), Name = $"Left{x.Name}",
                };
                ramp.Extra = extra;
                ramp.Radius = r;
                ramp.Tight = tight;
                ramp.Ext1 = ext1;
                ramp.Ext2 = ext2;
                ramp.Rebuild = (e1, e2) => BuildLeft(x, t, r, extra, tight, level, obstacles, e1, e2);
                return ramp;
            }

            /// <summary>路径上压在障碍物上的范围（去掉两头水平段）：第一个、最后一个位置；没有时为 −1。</summary>
            private (float First, float Last) Marks(RampPath path, float flatStart, float flatEnd, IReadOnlyList<Obstacle> obstacles)
            {
                float total = path.Length, end = total - flatEnd;
                bool Hit(float s)
                {
                    Vec3 v = path.PointAt(s);
                    return obstacles.Any(o => o.Distance(v, out _) < (rw + o.Width) / 2f + gap * 0.5f);
                }
                // 只要第一个、最后一个：从两头往中间粗找，找到后在上一步之间细找
                float first = -1f, last = -1f;
                for (float s = flatStart; s <= end; s += kCoarseMark)
                    if (Hit(s))
                    {
                        first = s;
                        for (float f = Math.Max(flatStart, s - kCoarseMark + 1f); f < s; f += 1f)
                            if (Hit(f))
                            {
                                first = f;
                                break;
                            }
                        break;
                    }
                if (first < 0f)
                    return (-1f, -1f);
                for (float s = end; s >= first; s -= kCoarseMark)
                    if (Hit(s))
                    {
                        last = s;
                        for (float f = Math.Min(end, s + kCoarseMark - 1f); f > s; f -= 1f)
                            if (Hit(f))
                            {
                                last = f;
                                break;
                            }
                        break;
                    }
                return (first, last);
            }

            /// <summary>两条匝道平面上挨着的地方，高度至少差 minRise。</summary>
            private bool Clear(Ramp one, Ramp other)
            {
                var o = other.AsObstacle(rw);
                float reach = rw + gap * 0.5f;
                foreach (var u in one.Samples())
                    if (o.Distance(u, out float h) < reach && Math.Abs(u.Y - h) < minRise)
                        return false;
                return true;
            }

            /// <summary>
            /// 四条左转匝道：转角小的那一对在第 3 层，另一对在第 4 层（要跨过第 3 层的两条）；转角相同（正交）时从主线出发的在第 3 层。
            /// 转角小的匝道斜着跨路、连续压在下面道路上的范围长，放在第 3 层时下面没有别的左转匝道，立柱才放得下。
            /// 每层的两条用同一半径，从小到大找第一个可行的。
            /// </summary>
            private bool SolveLefts()
            {
                // 要跨过的：两条路、（左出左进时）贴着转角的右转匝道、更低一层的左转匝道
                var roads = lines.Select(l => (Obstacle)new LineObstacle(l.Origin, l.Dir, l.Width, l.Height))
                    .Concat(rights.Select(r => (Obstacle)r.AsObstacle(rw))).ToList();
                float startExtra = 0f;
                float TurnOf(bool main)
                {
                    var x = lines.First(l => l.Main == main);
                    return (float)Math.Acos(Math.Max(-1f, Math.Min(1f, Directions.Dot(x.Dir, LeftTarget(x).Dir))));
                }
                bool mainFirst = TurnOf(true) <= TurnOf(false) + 1e-3f;
                foreach (bool fromMain in new[] { mainFirst, !mainFirst })
                {
                    float level = fromMain == mainFirst ? hc + g : hc + 2f * g;
                    var sources = lines.Where(l => l.Main == fromMain).ToList();
                    var obstacles = roads.Concat(lefts.Select(l => (Obstacle)l.AsObstacle(rw))).ToList();
                    List<Ramp>? Try(float r, float extra, float tight)
                    {
                        if (m_Banned.Contains((level, r, extra, tight)) || (tight > 0f && r <= kLeftEndRadius + 1f))
                            return null;
                        var pair = sources.Select(x => BuildLeft(x, LeftTarget(x), r, extra, tight, level, obstacles)).ToList();
                        if (pair.Any(q => q == null))
                            return null;
                        bool fits = Clear(pair[0]!, pair[1]!) && lefts.Concat(rights).All(l => pair.All(q => Clear(q!, l)))
                            && pair.All(q => MaxBlocked(q!, obstacles) <= kMaxBlocked);
                        return fits ? pair! : null;
                    }
                    // 两段直线离主路的横向距离从小到大（偏移小的才是对角斜跨中心的经典造型）；每个偏移下半径先粗后细从小到大找：
                    // 同层两条要互不相交、跨越段下面能放立柱（连续压在下层道路上的长度不超过 kMaxBlocked）
                    // 每个偏移下先试单段圆弧，再试两头小半径的复合弯道
                    List<Ramp>? AtOffset(float extra)
                    {
                        foreach (float tight in kTightFractions)
                            for (float r = 60f; r <= kMaxLeftRadius; r += kCoarseStep)
                            {
                                if (!(Try(r, extra, tight) is List<Ramp> hit))
                                    continue;
                                for (float fine = r - kCoarseStep + 10f; fine < r; fine += 10f)
                                    if (fine >= 60f && Try(fine, extra, tight) is List<Ramp> better)
                                        return better;
                                return hit;
                            }
                        return null;
                    }
                    // 偏移先按 10 米粗找（第 4 层从第 3 层用的偏移附近开始），找到后再试小 5 米的
                    List<Ramp>? found = null;
                    float maxExtra = leftExit ? 0f : kMaxExtra;
                    for (float extra = startExtra; extra <= maxExtra && found == null; extra += 2f * kExtraStep)
                    {
                        found = AtOffset(extra);
                        if (found != null && extra - kExtraStep >= startExtra && AtOffset(extra - kExtraStep) is List<Ramp> smaller)
                            found = smaller;
                    }
                    if (found == null && startExtra > 0f)
                        for (float extra = startExtra - kExtraStep; extra >= 0f && found == null; extra -= kExtraStep)
                            found = AtOffset(extra);
                    if (found == null)
                    {
                        Trace?.Invoke($"lefts from {(fromMain ? "main" : "cross")}: no radius / offset fits");
                        return false;
                    }
                    lefts.AddRange(found);
                    startExtra = Math.Max(0f, found[0].Extra - 2f * kExtraStep);
                }
                return true;
            }

            // —— 右转匝道 ——

            /// <summary>
            /// 四条右转匝道，在左转匝道外侧（与它净距 kParallelGap）：比来向那条左转匝道早驶出（S 形横移到外侧），与它平行走到转角，
            /// 转一个小弯（半径从 kRightRadius 起找），再在去向那条左转匝道外侧平行走，晚于它汇入。同一象限的左转、右转不交叉，
            /// 左转匝道的立柱也不会落在右转匝道上。
            /// </summary>
            private bool SolveRights()
            {
                foreach (var x in lines)
                {
                    var t = RightTarget(x);
                    var leftOut = lefts.First(l => l.From == x);
                    var leftIn = lefts.First(l => l.To == t);
                    float dX = LeftOffset(x, leftOut.Extra) + rw + kParallelGap, dT = LeftOffset(t, leftIn.Extra) + rw + kParallelGap;
                    float srX = Elevated(x.Height) > 0f ? kElevatedShiftRadius : 120f, srT = Elevated(t.Height) > 0f ? kElevatedShiftRadius : 120f;
                    float ex = RightAttach(x), et = RightAttach(t);
                    var (p1, p2) = SCurve(x.Point(0f) + x.Right * ex, x.Dir, x.Right, dX - ex, srX);
                    var (q1, q2) = SCurve(t.Point(0f) + t.Right * dT, t.Dir, -t.Right, dT - et, srT);
                    float flatS = Overlap(RampPath.FromArcs(p1, p2), x, Sep(x), true) + kRightFlatMargin;
                    float flatE = Overlap(RampPath.FromArcs(q1, q2), t, Sep(t), false) + kRightFlatMargin;
                    if ((Elevated(x.Height) > 0f && flatS > 75f) || (Elevated(t.Height) > 0f && flatE > 75f))
                        return false;
                    // 驶出（连同并行段）在左转匝道驶出之前，汇入在左转匝道汇入（连同并行段）之后；转角更远时再早驶出、晚汇入
                    float latestStart = leftOut.FromAt - flatS - kMinSegment, earliestEnd = leftIn.ToAt + kMinSegment + flatE;
                    float rightSign = DiamondTemplate.Cross2(x.Dir, x.Right) >= 0f ? 1f : -1f;
                    Ramp? found = null;
                    for (float r = kRightRadius; r <= 400f && found == null; r *= 1.15f)
                    {
                        var arc = RampPath.Turn(x.Origin + x.Right * dX, x.Dir, t.Origin + t.Right * dT, t.Dir, rightSign, r, out float aAt, out float bAt).Arcs[0];
                        float startAt = Math.Min(latestStart, aAt - AlongS(dX - ex, srX) - kMinSegment);
                        float endAt = Math.Max(earliestEnd, bAt + AlongS(dT - et, srT) + kMinSegment);
                        float run1 = aAt - startAt - AlongS(dX - ex, srX), run2 = endAt - AlongS(dT - et, srT) - bAt;
                        var (s1, s2) = SCurve(x.Point(startAt) + x.Right * ex, x.Dir, x.Right, dX - ex, srX);
                        var a1 = Straight(s2.End, x.Dir, run1);
                        var a2 = Straight(arc.End, t.Dir, run2);
                        var (s3, s4) = EndShift(a2.End, t, et, srT);
                        var path = RampPath.FromArcs(s1, s2, a1, arc, a2, s3, s4);
                        if (path.Length - flatS - flatE < Math.Abs(t.Height - x.Height) / grade)
                            continue;
                        var ramp = new Ramp
                        {
                            Path = path, FlatStart = flatS, FlatEnd = flatE, FlatMargin = kRightFlatMargin, H0 = x.Height, H1 = t.Height,
                            From = x, To = t, FromAt = x.At(path.Start), ToAt = t.At(path.End), Name = $"Right{x.Name}",
                        };
                        if (lefts.All(l => Clear(ramp, l)))
                            found = ramp;
                    }
                    if (found == null)
                    {
                        Trace?.Invoke($"right turn from {x.Name}: no radius fits");
                        return false;
                    }
                    rights.Add(found);
                }
                return true;
            }

            /// <summary>
            /// 左出左进时每幅上左转、右转匝道的接点互相让开：接点不落在别的匝道与这一幅的并行重叠段里，相邻接点至少隔 kMinSegment。
            /// 冲突时加长左转匝道的直线段（驶出提前、汇入推后）。
            /// </summary>
            private bool SeparateAttachments()
            {
                var ramps = lefts.Concat(rights).ToList();
                // 一幅上某条匝道的接点和并行重叠范围（驶出：接点之后；汇入：接点之前）
                IEnumerable<(Ramp Ramp, float Node, float Lo, float Hi)> Attaches(Carriageway line) =>
                    ramps.Where(r => r.From == line).Select(r => (r, r.FromAt, r.FromAt, r.FromAt + r.FlatStart))
                        .Concat(ramps.Where(r => r.To == line).Select(r => (r, r.ToAt, r.ToAt - r.FlatEnd, r.ToAt)));
                for (int pass = 0; pass < 4; pass++)
                {
                    bool changed = false;
                    for (int i = 0; i < lefts.Count; i++)
                    {
                        var left = lefts[i];
                        // 驶出：往上游让，直到并行范围之后留 kMinSegment 也碰不到别的接点，接点也不在别的并行范围里。
                        // 高架时两个接点相距超过一段的最大长度，中间要加节点（立柱）：它要落在并行范围之后、离下一个接点至少 kMinPiece
                        float Apart(Carriageway line, float span) => Elevated(line.Height) > 0f && span > kMaxElevatedPiece ? kMinPiece : kMinSegment;
                        float e1 = 0f, e2 = 0f;
                        foreach (var other in Attaches(left.From).Where(a => a.Ramp != left))
                        {
                            float lo = left.FromAt, hi = left.FromAt + left.FlatStart;
                            float apart = Apart(left.From, other.Node - lo);
                            if (other.Node > lo - kMinSegment && other.Node < hi + apart)
                                e1 = Math.Max(e1, hi + apart - other.Node);
                            if (lo > other.Lo - kMinSegment && lo < other.Hi + kMinSegment)
                                e1 = Math.Max(e1, lo - other.Lo + kMinSegment);
                        }
                        foreach (var other in Attaches(left.To).Where(a => a.Ramp != left))
                        {
                            float lo = left.ToAt - left.FlatEnd, hi = left.ToAt;
                            float apart = Apart(left.To, hi - other.Node);
                            if (other.Node > lo - apart && other.Node < hi + kMinSegment)
                                e2 = Math.Max(e2, other.Node + apart - lo);
                            if (hi > other.Lo - kMinSegment && hi < other.Hi + kMinSegment)
                                e2 = Math.Max(e2, other.Hi + kMinSegment - hi);
                        }
                        if (e1 <= 0f && e2 <= 0f)
                            continue;
                        var rebuilt = left.Rebuild!(left.Ext1 + e1 + (e1 > 0f ? 1f : 0f), left.Ext2 + e2 + (e2 > 0f ? 1f : 0f));
                        if (rebuilt == null)
                            return false;
                        lefts[i] = rebuilt;
                        ramps = lefts.Concat(rights).ToList();
                        changed = true;
                    }
                    if (!changed)
                        return true;
                }
                return false;
            }

            /// <summary>
            /// 左出左进用的四条右转匝道：在来向那幅右侧 S 形分出（与它净距 kParallelGap），平行走到转角，转一个小弯（kRightRadius），
            /// 再在去向那幅右侧平行走、S 形并入。S 形分出的并行重叠段短，与转角大小无关（高架时也放得下）；两段直线按坡度加长。
            /// 左转匝道在中央分隔带一侧，与它们不在同一侧，从上面跨过。
            /// </summary>
            private bool SolveCornerRights()
            {
                foreach (var x in lines)
                {
                    var t = RightTarget(x);
                    float dX = x.Width / 2f + rw / 2f + kParallelGap, dT = t.Width / 2f + rw / 2f + kParallelGap;
                    float ex = RightAttach(x), et = RightAttach(t);
                    float srX = Elevated(x.Height) > 0f ? kElevatedShiftRadius : 120f, srT = Elevated(t.Height) > 0f ? kElevatedShiftRadius : 120f;
                    float rightSign = DiamondTemplate.Cross2(x.Dir, x.Right) >= 0f ? 1f : -1f;
                    var arc = RampPath.Turn(x.Origin + x.Right * dX, x.Dir, t.Origin + t.Right * dT, t.Dir, rightSign, kRightRadius,
                        out float aAt, out float bAt).Arcs[0];
                    float alongX = AlongS(dX - ex, srX), alongT = AlongS(dT - et, srT);
                    RampPath Path(float run1, float run2)
                    {
                        var (s1, s2) = SCurve(x.Point(aAt - run1 - alongX) + x.Right * ex, x.Dir, x.Right, dX - ex, srX);
                        var a1 = Straight(s2.End, x.Dir, run1);
                        var a2 = Straight(arc.End, t.Dir, run2);
                        var (s3, s4) = EndShift(a2.End, t, et, srT);
                        return RampPath.FromArcs(s1, s2, a1, arc, a2, s3, s4);
                    }
                    var trial = Path(kMinSegment, kMinSegment);
                    float flatS = Overlap(trial, x, Sep(x), true) + kRightFlatMargin, flatE = Overlap(trial, t, Sep(t), false) + kRightFlatMargin;
                    if ((Elevated(x.Height) > 0f && flatS > 75f) || (Elevated(t.Height) > 0f && flatE > 75f))
                    {
                        Trace?.Invoke($"corner right turn from {x.Name}: overlap too long");
                        return false;
                    }
                    // 两段直线按升降需要的长度加长（两边各一半）
                    float lack = Math.Abs(t.Height - x.Height) / grade - (trial.Length - flatS - flatE);
                    float run = kMinSegment + Math.Max(0f, lack) / 2f;
                    var path = Path(run, run);
                    rights.Add(new Ramp
                    {
                        Path = path, FlatStart = flatS, FlatEnd = flatE, FlatMargin = kRightFlatMargin, H0 = x.Height, H1 = t.Height,
                        From = x, To = t, FromAt = x.At(path.Start), ToAt = t.At(path.End), Name = $"Right{x.Name}",
                    });
                }
                return true;
            }

            // —— 分段（立柱）——

            /// <summary>匝道弧长 s 处的立柱压在哪个更低的障碍物上（lower 的下标），没有时为 −1（主路按立柱薄墙在横向上的伸展判断）。</summary>
            private int Blocker(Ramp ramp, float s, IReadOnlyList<Obstacle> lower)
            {
                Vec3 v = ramp.Path.PointAt(s);
                float h = ramp.HeightAt(s);
                if (LevelPlanner.BandOf(h) != ElevationBand.Elevated)
                    return -1;
                Vec3 heading = ramp.Path.HeadingAt(s);
                for (int k = 0; k < lower.Count; k++)
                {
                    var o = lower[k];
                    float reach;
                    if (o.Direction.X != 0f || o.Direction.Z != 0f)
                    {
                        Vec3 n = Directions.RightOf(o.Direction);
                        float extent = rw / 2f * Math.Abs(Directions.Dot(Perp(heading), n)) + kPillarThickness / 2f * Math.Abs(Directions.Dot(heading, n));
                        reach = o.Width / 2f + extent + 1f;
                    }
                    else
                        reach = (rw + o.Width) / 2f + 1f;
                    // 隧道里的道路不挡地面上的立柱
                    if (o.Distance(v, out float hh) < reach && hh < h - 1f && hh > HeightRange.TunnelThreshold + 0.5f)
                        return k;
                }
                return -1;
            }

            /// <summary>匝道（两头水平段之外）连续放不下立柱的最长一段。</summary>
            private float MaxBlocked(Ramp ramp, IReadOnlyList<Obstacle> lower)
            {
                float total = ramp.Length, worst = 0f, from = -1f;
                for (float s = ramp.FlatStart; s <= total - ramp.FlatEnd; s += kStep)
                {
                    if (Blocker(ramp, s, lower) >= 0)
                    {
                        if (from < 0f)
                            from = s;
                        worst = Math.Max(worst, s - from);
                    }
                    else
                        from = -1f;
                }
                return worst;
            }

            /// <summary>
            /// 匝道分段：断点含两头水平段、跨越段的起止和弯直交界处（水平段外）；高架部分每段不超过 kMaxElevatedPiece、转角不超过
            /// kMaxPieceTurn；另加的断点（立柱）不落在更低的道路上方（主路按立柱薄墙在横向上的伸展判断）。
            /// </summary>
            private bool PlanCuts(Ramp ramp)
            {
                float total = ramp.Length;
                var lower = lines.Select(l => (Obstacle)new LineObstacle(l.Origin, l.Dir, l.Width, l.Height))
                    .Concat(lefts.Concat(rights).Where(o => o != ramp).Select(o => (Obstacle)o.AsObstacle(rw))).ToList();
                bool Forbidden(float s) => Blocker(s) != -1;
                // 挡住立柱的：−1 没有，否则是 lower 的下标
                int Blocker(float s) => this.Blocker(ramp, s, lower);
                var required = new List<float> { 0f, ramp.FlatStart, total - ramp.FlatEnd, total };
                if (ramp.HasPlateau)
                {
                    required.Add(ramp.PlateauStart);
                    required.Add(ramp.PlateauEnd);
                }
                // 纵断面的转折点（两点之间按直线升降）；落在挡立柱的位置上时先挪开
                if (ramp.Knots != null && !ShiftKnots(ramp, lower))
                {
                    Fail("knots");
                    return false;
                }
                if (ramp.Knots != null)
                    required.AddRange(ramp.Knots.Select(k => k.S).Where(k => k > ramp.FlatStart + kMinSegment && k < total - ramp.FlatEnd - kMinSegment));
                float offset = 0f;
                for (int i = 0; i + 1 < ramp.Path.Arcs.Count; i++)
                {
                    var arc = ramp.Path.Arcs[i];
                    offset += arc.Length;
                    var next = ramp.Path.Arcs[i + 1];
                    if (Math.Abs(arc.Radius - next.Radius) < 0.5f && Math.Sign(arc.Angle) == Math.Sign(next.Angle))
                        continue;
                    float at = offset;
                    bool inFlat = at < ramp.FlatStart + kMinSegment || at > total - ramp.FlatEnd - kMinSegment;
                    if (!inFlat && required.All(r => Math.Abs(r - at) >= kMinSegment) && !Forbidden(at))
                        required.Add(at);
                }
                // 涡轮：固定的断点（水平段起止、转折点）下面也不能有道路
                if (turbine && required.Any(Forbidden))
                {
                    Fail("fixed cuts");
                    return false;
                }
                var merged = new List<float>();
                foreach (float s in required.Where(s => s >= 0f && s <= total).OrderBy(s => s))
                    if (merged.Count == 0 || s - merged[merged.Count - 1] > 1f)
                        merged.Add(s);
                merged[merged.Count - 1] = total;
                bool ElevatedPiece(float s0, float s1) =>
                    LevelPlanner.BandOf(ramp.HeightAt(s0)) == ElevationBand.Elevated || LevelPlanner.BandOf(ramp.HeightAt(s1)) == ElevationBand.Elevated
                    || LevelPlanner.BandOf(ramp.HeightAt((s0 + s1) / 2f)) == ElevationBand.Elevated;
                var cuts = new List<float> { 0f };
                float cur = 0f;
                for (int i = 1; i < merged.Count; i++)
                {
                    float target = merged[i];
                    bool overlap = cur < ramp.FlatStart - 0.5f || target > total - ramp.FlatEnd + 0.5f;
                    int guard = 0;
                    while (!overlap && guard++ < 100)
                    {
                        float maxLen = ElevatedPiece(cur, target) ? kMaxElevatedPiece : kMaxGroundPiece;
                        if (target - cur <= maxLen && ramp.Path.TurnBetween(cur, target) <= kMaxPieceTurn)
                            break;
                        float Pick(float len)
                        {
                            for (float s = Math.Min(cur + len, target - kMinSegment); s >= cur + kMinSegment; s -= 1f)
                                if (ramp.Path.TurnBetween(cur, s) <= kMaxPieceTurn && !Forbidden(s))
                                    return s;
                            return -1f;
                        }
                        float pick = Pick(maxLen);
                        // 高架段放不下时放宽到 kHardMaxPiece（仍短于游戏切分的 80 米）
                        if (pick < 0f && maxLen < kMaxGroundPiece)
                        {
                            if (target - cur <= kHardMaxPiece && ramp.Path.TurnBetween(cur, target) <= kMaxPieceTurn)
                                break;
                            pick = Pick(kHardMaxPiece);
                        }
                        if (pick < 0f)
                        {
                            Trace?.Invoke($"cuts {ramp.Name} at {cur:F0}..{target:F0} of {total:F0} h {ramp.HeightAt(cur):F1} "
                                + string.Concat(Enumerable.Range(0, (int)((target - cur) / 3f)).Select(k => { int b = Blocker(cur + 3f * k); return b == -1 ? "." : b.ToString(); })));
                            return false;
                        }
                        cuts.Add(pick);
                        cur = pick;
                    }
                    cuts.Add(target);
                    cur = target;
                }
                ramp.Cuts = cuts;
                return true;
            }

            // —— 输出 ——

            public void Build(List<RoadSegmentSpec> segments)
            {
                if (!Fits)
                    return;
                var p = P;
                var legs = p.Legs;
                var ramps = lefts.Concat(rights).ToList();

                // 每一幅上的节点：匝道接点、内端；被交道路（第 2 层）跨越主线处按立柱规划；高架时按长度加节点，避开匝道并行重叠段。
                // 内端之外：S 形从拉宽的分隔带回到原来的位置，再接一段直线臂端
                float mainEnd = LineEnd(true), crossEnd = LineEnd(false);
                var nodeOf = new Dictionary<(Carriageway, float), Vec3>();
                // 每一幅外端（臂端）的节点：(幅, +1 下游 / −1 上游)
                var outerOf = new Dictionary<(Carriageway, int), Vec3>();
                float shiftM = om - legs[0].CarriagewayOffset, shiftC = oc - legs[1].CarriagewayOffset;
                float ShiftLength(float shift) => shift > 0.05f ? 2f * kShiftRadius * MathF.Sin((float)Math.Acos(1f - shift / (2f * kShiftRadius))) : 0f;
                foreach (var line in lines)
                {
                    float end = line.Main ? mainEnd : crossEnd, h = line.Height;
                    var role = line.Main ? RoadRole.MainRoad : RoadRole.CrossRoad;
                    string? prefab = line.Main ? legs[0].RoadPrefab : legs[1].RoadPrefab;
                    bool elevated = LevelPlanner.BandOf(h) == ElevationBand.Elevated;
                    var attaches = Attaches(line);
                    var sorted = LineCuts(line)!;

                    // 车道平衡：右出右进时右转匝道驶出点 ~ 汇入点之间用减车道的道路，节点向中央分隔带平移；左出左进时是左转匝道
                    // 驶出点 ~ 汇入点之间，节点向外侧平移。两头的曲线端点平移、节点不动
                    var balancing = leftExit ? lefts : rights;
                    float lo = balancing.First(r => r.From == line).FromAt, hi = balancing.First(r => r.To == line).ToAt;
                    Vec3 dropShift = line.Right * (leftExit ? Drop(line) : -Drop(line));
                    bool InZone(float t) => Balanced(line) && t > lo + 1e-3f && t < hi - 1e-3f;
                    Vec3 Node(float t) => At(line.Point(t) + (InZone(t) ? dropShift : Vec3.Zero), h);
                    for (int i = 0; i + 1 < sorted.Count; i++)
                    {
                        float t0 = sorted[i], t1 = sorted[i + 1];
                        bool inner = InZone((t0 + t1) / 2f);
                        Vec3 a = Node(t0), b = Node(t1);
                        Vec3 ca = inner && !InZone(t0) ? a + dropShift : a, cb = inner && !InZone(t1) ? b + dropShift : b;
                        segments.Add(new RoadSegmentSpec($"{line.Name}-{i + 1}", role, Bezier3.Line(ca, cb), inner ? ReducedPrefab(line) : prefab, a, b));
                    }
                    foreach (var t in attaches.Select(a => a.Item1))
                        nodeOf[(line, t)] = Node(t);

                    // 两端：S 形回到原来的分隔带位置，再接直线臂端（高架时分段）
                    float shift = line.Main ? shiftM : shiftC, length = ShiftLength(shift), stub = (line.Main ? legs[0] : legs[1]).StubLength;
                    var stubCuts = Enumerable.Range(0, (elevated ? (int)Math.Ceiling(stub / kMaxElevatedPiece) : 1) + 1).ToList();
                    int pieces = stubCuts.Count - 1;
                    Vec3 downFrom = At(line.Point(end), h), upTo = At(line.Point(-end), h);
                    if (length > 0f)
                    {
                        var (d1, d2) = SCurve(line.Point(end), line.Dir, -line.Right, shift, kShiftRadius);
                        var down = RampPath.FromArcs(d1, d2);
                        Vec3 downEnd = At(d2.End, h);
                        down.EmitAt(segments, $"{line.Name}-out-", role, new[] { 0f, d1.Length, down.Length }, new[] { h, h, h }, prefab, downFrom, downEnd);
                        Vec3 upStart = At(line.Point(-end - length) - line.Right * shift, h);
                        var (u1, u2) = SCurve(upStart, line.Dir, line.Right, shift, kShiftRadius);
                        var up = RampPath.FromArcs(u1, u2);
                        up.EmitAt(segments, $"{line.Name}-in-", role, new[] { 0f, u1.Length, up.Length }, new[] { h, h, h }, prefab, upStart, upTo);
                        downFrom = downEnd;
                        upTo = upStart;
                    }
                    Vec3 downTail = downFrom + line.Dir * stub, upTail = upTo - line.Dir * stub;
                    for (int i = 0; i < pieces; i++)
                    {
                        segments.Add(new RoadSegmentSpec($"{line.Name}-endB-{i + 1}", role,
                            Bezier3.Line(Vec3.Lerp(downFrom, downTail, (float)i / pieces), Vec3.Lerp(downFrom, downTail, (float)(i + 1) / pieces)), prefab));
                        segments.Add(new RoadSegmentSpec($"{line.Name}-endA-{i + 1}", role,
                            Bezier3.Line(Vec3.Lerp(upTail, upTo, (float)i / pieces), Vec3.Lerp(upTail, upTo, (float)(i + 1) / pieces)), prefab));
                    }
                    outerOf[(line, 1)] = downTail;
                    outerOf[(line, -1)] = upTail;
                }

                foreach (var ramp in ramps)
                {
                    // 曲线端点在匝道自己的接入位置（车道平衡时外缘对齐），节点是道路上的那个
                    ramp.Path.EmitAt(segments, ramp.Name + "-", RoadRole.Ramp, ramp.Cuts, ramp.Cuts.Select(ramp.HeightAt).ToList(), p.RampPrefab,
                        startNode: nodeOf[(ramp.From, ramp.FromAt)], endNode: nodeOf[(ramp.To, ramp.ToAt)]);
                }

                // 外部是双向道路：两端两幅平行接到双向道路端点（宽路斜向汇合游戏报形状无效），放不下时斜向过渡
                foreach (var (legIndex, main, sign) in new[] { (0, true, 1f), (2, true, -1f), (1, false, 1f), (3, false, -1f) })
                {
                    var leg = legs[legIndex];
                    if (!leg.NeedsDualToTwoWayTransition)
                        continue;
                    var forward = lines.First(l => l.Main == main && Directions.Dot(l.Dir, leg.Direction) > 0f);
                    var backward = lines.First(l => l.Main == main && l != forward);
                    float h = forward.Height;
                    float end = Directions.Dot(Flat0(outerOf[(forward, 1)]), Flat(leg.Direction));
                    Vec3 outEnd = outerOf[(forward, 1)];
                    Vec3 inStart = outerOf[(backward, -1)];
                    Vec3 joint = At(leg.Direction * end, h);
                    int o = segments.FindIndex(sg => sg.EndNode.Equals(outEnd));
                    int i = segments.FindIndex(sg => sg.StartNode.Equals(inStart));
                    if (o < 0 || i < 0)
                        continue;
                    segments[o] = new RoadSegmentSpec(segments[o].Name, segments[o].Role, segments[o].Curve, segments[o].Prefab, segments[o].StartNode, joint);
                    segments[i] = new RoadSegmentSpec(segments[i].Name, segments[i].Role, segments[i].Curve, segments[i].Prefab, joint, segments[i].EndNode);
                    // 臂端双向道路：高架时分成短于游戏切分长度的几段（否则游戏自己加的立柱位置不受控）
                    Vec3 tailEnd = At(leg.Direction * (end + leg.StubLength), h);
                    int pieces = LevelPlanner.BandOf(h) == ElevationBand.Elevated ? (int)Math.Ceiling(leg.StubLength / kMaxElevatedPiece) : 1;
                    for (int k = 0; k < pieces; k++)
                        segments.Add(new RoadSegmentSpec($"{forward.Name}-tail{(pieces > 1 ? $"-{k + 1}" : "")}", segments[o].Role,
                            Bezier3.Line(Vec3.Lerp(joint, tailEnd, (float)k / pieces), Vec3.Lerp(joint, tailEnd, (float)(k + 1) / pieces)), leg.ExternalPrefab));
                }
            }

            private static Vec3 At(Vec3 v, float y) => new Vec3(v.X, y, v.Z);

            /// <summary>主线 / 被交道路各幅的内端（最外侧匝道接点再往外 kEndGap）。</summary>
            private float LineEnd(bool main) => lefts.Concat(rights).SelectMany(r => new[] { (r.From, r.FromAt), (r.To, r.ToAt) })
                .Where(e => e.Item1.Main == main).Max(e => Math.Abs(e.Item2)) + kEndGap;

            /// <summary>一幅上各匝道的接点，以及接点处与匝道并行重叠的范围（驶出：接点之后；汇入：接点之前）。</summary>
            private List<(float, float, float)> Attaches(Carriageway line)
            {
                var ramps = lefts.Concat(rights).ToList();
                return ramps.Where(r => r.From == line).Select(r => (r.FromAt, r.FromAt, r.FromAt + r.FlatStart))
                    .Concat(ramps.Where(r => r.To == line).Select(r => (r.ToAt, r.ToAt - r.FlatEnd, r.ToAt))).ToList();
            }

            /// <summary>
            /// 一幅上的节点（沿该幅的位置）：匝道接点、两端；被交道路（第 2 层）跨越主线处按立柱规划；高架时按长度加节点，
            /// 避开匝道并行重叠段和从下面穿过的匝道。高架段分不开（每段都要短于游戏切分长度）时返回 null。
            /// allowInOverlap：避不开并行重叠段时允许节点落在里面（定向式左出左进时出现过，游戏里可以放置）。
            /// </summary>
            private List<float>? LineCuts(Carriageway line, bool allowInOverlap = true)
            {
                var legs = P.Legs;
                var ramps = lefts.Concat(rights).ToList();
                float end = LineEnd(line.Main);
                var attaches = Attaches(line);
                var nodes = new List<float> { -end, end };
                nodes.AddRange(attaches.Select(a => a.Item1));
                if (!line.Main)
                {
                    // 被交道路跨越主线：两端节点（立柱）避开主线两幅
                    var mainLine = lines.First(l => l.Main);
                    float sin = Math.Abs(DiamondTemplate.Cross2(line.Dir, mainLine.Dir));
                    float center = -DiamondTemplate.Cross2(line.Origin, mainLine.Dir) / DiamondTemplate.Cross2(line.Dir, mainLine.Dir);
                    var span = PillarPlanner.Plan(center, (om + legs[0].RoadWidth / 2f) / sin, (om - legs[0].RoadWidth / 2f) / sin,
                        legs[1].MiddlePillarSpacing, legs[1].PillarWidth, legs[1].MaxElevatedEdgeLength);
                    nodes.Add(span.Lo);
                    nodes.Add(span.Hi);
                }
                var sorted = new List<float>();
                foreach (float t in nodes.OrderBy(t => t))
                {
                    bool fixedNode = attaches.Any(a => Math.Abs(a.Item1 - t) < 1e-3f) || Math.Abs(Math.Abs(t) - end) < 1e-3f;
                    int near = sorted.FindIndex(k => Math.Abs(k - t) < kMinSegment);
                    if (near < 0)
                        sorted.Add(t);
                    else if (fixedNode)
                        sorted[near] = t;
                }
                sorted.Sort();
                if (LevelPlanner.BandOf(line.Height) != ElevationBand.Elevated)
                    return sorted;
                // 高架时按长度加节点（立柱），一定避开从下面穿过的匝道，尽量避开匝道并行重叠段
                var under = new List<(float, float)>();
                float reach = line.Width / 2f + rw / 2f + kPillarThickness;
                foreach (var ramp in ramps)
                    foreach (var v in ramp.Samples())
                        if (v.Y < line.Height - 1f && Math.Abs(DiamondTemplate.Cross2(Flat0(v) - line.Origin, line.Dir)) < reach)
                        {
                            float at = line.At(v);
                            under.Add((at - rw / 2f - kPillarThickness - kPillarMargin, at + rw / 2f + kPillarThickness + kPillarMargin));
                        }
                // 放不开时节点可以落在水平段多留的余量里，但不能落在真正并行重叠（横向还没分开）的那段：
                // 那里的节点之后的路段与匝道第一段不共用节点却相互重叠，游戏报“与现有物体重叠”
                var strict = ramps.Where(r => r.From == line).Select(r => (r.FromAt, r.FromAt + r.FlatStart - r.FlatMargin + kNodeClearance))
                    .Concat(ramps.Where(r => r.To == line).Select(r => (r.ToAt - r.FlatEnd + r.FlatMargin - kNodeClearance, r.ToAt)));
                var cuts = Subdivide(sorted, kMaxElevatedPiece, under.Concat(attaches.Select(a => (a.Item2, a.Item3))).ToList())
                    ?? (allowInOverlap ? Subdivide(sorted, kMaxElevatedPiece, under.Concat(strict).ToList()) : null);
                if (cuts == null && allowInOverlap)
                {
                    cuts = Subdivide(sorted, kMaxElevatedPiece, under);
                    Trace?.Invoke($"{line.Name}: elevated nodes fall inside a ramp overlap");
                }
                // 固定的节点（两端、匝道接点、跨主线处）也不能落在从下面穿过的匝道上方
                return cuts != null && cuts.All(c => !under.Any(z => c > z.Item1 && c < z.Item2)) ? cuts : null;
            }

            /// <summary>在已有断点之间加断点，使每段不超过 maxLength；落在 zones（并行重叠段、下面穿过的匝道）里的挪到它的边上。</summary>
            private static List<float>? Subdivide(List<float> cuts, float maxLength, IReadOnlyList<(float Lo, float Hi)> zones)
            {
                // 相互重叠的范围先合并，挪到边上的断点才不会又落进相邻的范围
                var keepWhole = new List<(float Lo, float Hi)>();
                foreach (var z in zones.OrderBy(z => z.Lo))
                {
                    if (keepWhole.Count > 0 && z.Lo <= keepWhole[keepWhole.Count - 1].Hi)
                        keepWhole[keepWhole.Count - 1] = (keepWhole[keepWhole.Count - 1].Lo, Math.Max(keepWhole[keepWhole.Count - 1].Hi, z.Hi));
                    else
                        keepWhole.Add(z);
                }
                bool Inside(float c) => keepWhole.Any(z => c > z.Lo && c < z.Hi);
                // 每个间隔均匀分段；某个断点落在范围里时就近挪到范围外（前后两段都在 kMinPiece ~ maxLength 之间），
                // 挪不开就多分一段再试；还不行返回 null
                var result = new List<float> { cuts[0] };
                for (int i = 0; i + 1 < cuts.Count; i++)
                {
                    float lo = cuts[i], hi = cuts[i + 1];
                    int n = (int)Math.Ceiling((hi - lo) / maxLength);
                    List<float>? picks = null;
                    for (int extra = 0; extra < 4 && picks == null; extra++)
                    {
                        int m = n + extra;
                        var trial = new List<float>();
                        float prev = lo;
                        bool ok = true;
                        for (int k = 1; k < m && ok; k++)
                        {
                            float target = lo + (hi - lo) * k / m;
                            float best = float.NaN;
                            for (float d = 0f; d <= maxLength / 2f && float.IsNaN(best); d += 1f)
                                foreach (float c in new[] { target - d, target + d })
                                    if (!Inside(c) && c - prev >= kMinPiece && c - prev <= maxLength && hi - c >= kMinPiece
                                        && (hi - c) <= maxLength * (m - k))
                                    {
                                        best = c;
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
                        if (ok && hi - prev <= maxLength)
                            picks = trial;
                    }
                    if (picks == null)
                        return null;
                    result.AddRange(picks);
                    result.Add(hi);
                }
                return result;
            }
        }
    }
}
