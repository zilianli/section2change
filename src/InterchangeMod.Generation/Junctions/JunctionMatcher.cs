using System;
using System.Collections.Generic;
using System.Linq;
using InterchangeMod.Generation.Geometry;

namespace InterchangeMod.Generation.Junctions
{
    /// <summary>
    /// 一条离开路口的已有路段（由工具层从游戏路网读出）。
    /// Anchor 是它在路口一侧的节点位置；Direction 是离开路口方向的水平单位向量。
    /// </summary>
    public sealed class ExternalEdge
    {
        public Vec3 Anchor { get; set; }
        public Vec3 Direction { get; set; }
        public string Road { get; set; } = "";
        public bool OneWay { get; set; }
        /// <summary>单行道路是否沿离开路口的方向行驶。</summary>
        public bool TravelOutward { get; set; }
        public float Width { get; set; }
        public int LanesPerDirection { get; set; }
        public bool IsHighway { get; set; }
        /// <summary>路口一侧节点的离地高度。</summary>
        public float Elevation { get; set; }
        /// <summary>
        /// 弯道适配：这一幅（或双向道路）从 Anchor 沿离开路口的方向往外追踪的俯视点（世界坐标，跟着同一条路接下去的路段走），
        /// 没有时为 null（当直线）。
        /// </summary>
        public IReadOnlyList<Vec3>? Trace { get; set; }
    }

    /// <summary>匹配出的一条道路（两个相对的汇入臂）。</summary>
    public sealed class MatchedRoad
    {
        /// <summary>道路预制体名：双幅时是单幅用的单行道路。</summary>
        public string Prefab { get; set; } = "";
        public RoadForm Form { get; set; }
        public float Width { get; set; }
        public float MedianWidth { get; set; }
        public int LanesPerDirection { get; set; }
        public bool IsHighway { get; set; }
        /// <summary>这条道路在路口处的离地高度（各路段平均）。</summary>
        public float Elevation { get; set; }
    }

    public sealed class JunctionMatch
    {
        /// <summary>两条道路中心线的交点（Y 为 0，工具层按地形处理）。</summary>
        public Vec3 Center { get; set; }
        /// <summary>主线方向角（度，从上往下看顺时针，0 = +Z）。</summary>
        public float MainAngle { get; set; }
        /// <summary>被交道路相对主线的夹角（度，0~180）。</summary>
        public float CrossingAngle { get; set; }
        public MatchedRoad? Main { get; set; }
        public MatchedRoad? Cross { get; set; }
        public int LegCount { get; set; }
        /// <summary>
        /// true = 指着的是一条普通道路（不是路口）：只对齐主线的方向和道路，Cross 为 null，
        /// 被交道路和相交角度由面板决定；Center 是鼠标在道路中心线上的投影。
        /// </summary>
        public bool RoadOnly { get; set; }
        /// <summary>路口处已有道路的最低离地高度。</summary>
        public float LowestElevation { get; set; }
        /// <summary>
        /// true = 两条道路立体交叉、没有共用节点（如高架跨过街道）：路段是在交叉点处切开读出的，
        /// 改造时两条道路在立交范围内都要拆掉，各自按原高度接回。
        /// </summary>
        public bool Crossing { get; set; }

        /// <summary>十字路口当 T 型用时，主线已按面板换成了匹配结果的另一条路（同层路口用，见工具的主线选择）。</summary>
        public bool TeeSwapped { get; set; }

        /// <summary>
        /// 三向路口的三条臂：世界角度（度，从小到大）、道路和从立交中心沿这条臂往外的中心线（弯道适配，没有追踪时为 null）。
        /// Y 型分向按它们放置；T 型用 Main / Cross。
        /// </summary>
        public IReadOnlyList<(float Angle, MatchedRoad Road, IReadOnlyList<Vec3>? Trace)>? TripleLegs { get; set; }

        /// <summary>三向路口里没有一条直行的道路（三条臂大致均分，Y 型）：只有 Y 型分向能对准，T 型立交不能用。</summary>
        public bool Wye { get; set; }
        /// <summary>无法匹配时的原因。</summary>
        public string? Error { get; set; }

        /// <summary>
        /// 弯道适配（docs/04）：从立交中心沿道路中心线往外的俯视点（世界坐标）。主线 Ahead 沿主线方向（MainAngle）、Behind 沿相反方向；
        /// 被交道路 Ahead 沿 MainAngle + CrossingAngle、Behind 沿相反方向。没有那条臂（只对准一条道路时的被交道路、T 型的另一侧）时为 null。
        /// </summary>
        public IReadOnlyList<Vec3>? MainAheadTrace { get; set; }
        public IReadOnlyList<Vec3>? MainBehindTrace { get; set; }
        public IReadOnlyList<Vec3>? CrossAheadTrace { get; set; }
        public IReadOnlyList<Vec3>? CrossBehindTrace { get; set; }

        /// <summary>主线与被交道路对调时中心线跟着换（工具按上下层或面板换主线时用）：新被交道路方向是原主线的反方向。</summary>
        public void SwapTraces()
        {
            (var mainAhead, var mainBehind) = (MainAheadTrace, MainBehindTrace);
            MainAheadTrace = CrossAheadTrace;
            MainBehindTrace = CrossBehindTrace;
            CrossAheadTrace = mainBehind;
            CrossBehindTrace = mainAhead;
        }
    }

    /// <summary>
    /// 把离开路口的已有路段整理成汇入臂和道路：
    ///   方向相近（±15°）的路段归为同一条臂；一条双向路段 → 双向臂；两条对向单行路段 → 双幅臂（量出分隔带）；
    ///   方向相反（夹角 ≥ 160°）的两条臂配成一条道路；四条臂 = 十字路口，三条臂 = T 型（菱形立交不支持）。
    ///   主线取每方向车道多的（一样多时公路优先，再取离当前朝向近的）。
    /// </summary>
    public static class JunctionMatcher
    {
        private const float kSameLegTolerance = 15f;
        private const float kOppositeTolerance = 20f;
        /// <summary>同一条臂里两个路段在路口处横向相差不到这么多（米，至少；另按路宽的一半）时算同一幅。</summary>
        private const float kSameLineMin = 2f;

        private sealed class Leg
        {
            public readonly List<ExternalEdge> Edges = new List<ExternalEdge>();
            public float Angle;
            public Vec3 Direction;
            public Vec3 CenterPoint;
            public MatchedRoad Road = new MatchedRoad();
            /// <summary>这条臂的道路中心线（世界坐标，从臂的中心点往外）：取一幅的追踪点沿法向横移到中心线；没有追踪时为 null。</summary>
            public List<Vec3>? Trace;
            /// <summary>双幅两幅都有追踪时，离路口 kSeparationFrom 米外两幅中心线的间距（米）；否则为 null（按路口处的锚点算）。</summary>
            public float? TraceSeparation;
        }

        /// <param name="preferredAngle">当前朝向（度）：主线方向在两种可能里取离它近的那个。</param>
        /// <param name="cursor">鼠标位置：指着普通道路时，立交中心取它在道路中心线上的投影。</param>
        /// <param name="dualMedian">指着双幅道路的一幅时，估计另一幅位置用的分隔带宽度。</param>
        /// <param name="rightHandTraffic">决定单幅在双幅中心线的哪一侧。</param>
        public static JunctionMatch Match(IReadOnlyList<ExternalEdge> edges, float preferredAngle,
            Vec3 cursor = default, float dualMedian = 4f, bool rightHandTraffic = true)
        {
            var result = new JunctionMatch
            {
                LowestElevation = edges.Count > 0 ? edges.Min(e => e.Elevation) : 0f,
            };

            var legs = GroupLegs(edges);
            result.LegCount = legs.Count;
            if (legs.Count == 2 && Directions.AngleBetween(legs[0].Angle, legs[1].Angle) >= 180f - kOppositeTolerance)
                return MatchRoad(result, legs, preferredAngle, cursor, dualMedian, rightHandTraffic);
            if (legs.Count == 3)
                return MatchT(result, legs, preferredAngle);
            if (legs.Count != 4)
                return Fail(result, $"路口有 {legs.Count} 个方向，只能匹配四向十字路口");

            foreach (var leg in legs)
            {
                string? error = DescribeLeg(leg);
                if (error != null)
                    return Fail(result, error);
            }

            // 相对的两条臂配成一条道路
            var pairs = new List<(Leg a, Leg b)>();
            var remaining = new List<Leg>(legs);
            while (remaining.Count > 0)
            {
                var a = remaining[0];
                remaining.RemoveAt(0);
                var b = remaining.OrderByDescending(l => Directions.AngleBetween(a.Angle, l.Angle)).First();
                if (Directions.AngleBetween(a.Angle, b.Angle) < 180f - kOppositeTolerance)
                    return Fail(result, "路口的道路不是两两相对的直线，无法配成两条道路");
                remaining.Remove(b);
                pairs.Add((a, b));
            }

            // 主线：每方向车道多 → 公路 → 离当前朝向近
            var ordered = pairs
                .OrderByDescending(p => p.a.Road.LanesPerDirection)
                .ThenByDescending(p => p.a.Road.IsHighway)
                .ThenBy(p => Math.Min(Directions.AngleBetween(preferredAngle, p.a.Angle), Directions.AngleBetween(preferredAngle, p.b.Angle)))
                .ToList();
            var (mainA, mainB) = ordered[0];
            var (crossA, crossB) = ordered[1];

            Leg mainLeg = Directions.AngleBetween(preferredAngle, mainA.Angle) <= Directions.AngleBetween(preferredAngle, mainB.Angle) ? mainA : mainB;
            // 被交道路取相对主线顺时针 0~180° 的那条臂
            Leg crossLeg = Normalize(crossA.Angle - mainLeg.Angle) < 180f ? crossA : crossB;

            result.MainAngle = mainLeg.Angle;
            result.CrossingAngle = Normalize(crossLeg.Angle - mainLeg.Angle);
            result.Main = mainLeg.Road;
            result.Cross = crossLeg.Road;
            result.Center = Intersect(Midpoint(mainA, mainB), mainLeg.Direction, Midpoint(crossA, crossB), crossLeg.Direction);
            result.MainAheadTrace = FromCenter(mainLeg, result.Center);
            result.MainBehindTrace = FromCenter(mainLeg == mainA ? mainB : mainA, result.Center);
            result.CrossAheadTrace = FromCenter(crossLeg, result.Center);
            result.CrossBehindTrace = FromCenter(crossLeg == crossA ? crossB : crossA, result.Center);
            return result;
        }

        /// <summary>
        /// 指着一条普通道路（两个相反方向）：主线对齐这条路。
        ///   双向道路：中心线就是这条路；
        ///   单行道路（双幅中的一幅，或单独的单行道路）：按双幅处理，中心线在这一幅的左侧（右侧通行时）
        ///   偏移 (分隔带 + 宽度) / 2——右侧通行时沿 v 行驶的那幅在双幅中心线右边。
        /// </summary>
        private static JunctionMatch MatchRoad(JunctionMatch result, List<Leg> legs, float preferredAngle, Vec3 cursor, float dualMedian, bool rightHandTraffic)
        {
            result.RoadOnly = true;
            Leg leg = Directions.AngleBetween(preferredAngle, legs[0].Angle) <= Directions.AngleBetween(preferredAngle, legs[1].Angle) ? legs[0] : legs[1];
            Leg other = leg == legs[0] ? legs[1] : legs[0];
            var twoWay = leg.Edges.Concat(other.Edges).FirstOrDefault(e => !e.OneWay);
            Vec3 linePoint = (leg.CenterPoint + other.CenterPoint) * 0.5f;

            if (twoWay != null)
            {
                result.Main = new MatchedRoad
                {
                    Prefab = twoWay.Road, Form = RoadForm.TwoWay, Width = twoWay.Width,
                    LanesPerDirection = twoWay.LanesPerDirection, IsHighway = twoWay.IsHighway,
                };
            }
            else
            {
                // 两条单行道路都驶入这个节点（画反了方向、或两条单行路尾接尾）：看不出是双幅的哪一幅
                var outward = leg.Edges.Concat(other.Edges).FirstOrDefault(e => e.TravelOutward);
                if (outward == null)
                    return Fail(result, "这条路两侧的单行道路方向相对（都驶向这个节点），无法判断是双幅道路的哪一幅");
                Vec3 travel = outward.Direction;
                Vec3 driveSide = Directions.RightOf(travel) * (rightHandTraffic ? 1f : -1f);
                float half = (dualMedian + outward.Width) / 2f;
                linePoint = linePoint - driveSide * half;
                // 两条臂的追踪都是这一幅本身：同样挪到中心线（在行驶方向的另一侧；对每条臂按它自己的方向换算左右）
                foreach (var l in new[] { leg, other })
                    if (l.Trace != null)
                        l.Trace = Shift(l.Trace, half * (rightHandTraffic ? 1f : -1f) * (Directions.Dot(l.Direction, travel) > 0f ? 1f : -1f));
                result.Main = new MatchedRoad
                {
                    Prefab = outward.Road, Form = RoadForm.Dual, Width = outward.Width, MedianWidth = dualMedian,
                    LanesPerDirection = outward.LanesPerDirection, IsHighway = outward.IsHighway,
                };
            }

            result.MainAngle = leg.Angle;
            // 立交中心：鼠标投影到主线中心线上
            Vec3 d = leg.Direction;
            float t = Directions.Dot(new Vec3(cursor.X - linePoint.X, 0f, cursor.Z - linePoint.Z), d);
            result.Center = new Vec3(linePoint.X + d.X * t, 0f, linePoint.Z + d.Z * t);
            // 弯道：中心改取鼠标在实际道路中心线（两条臂的追踪接起来）上的最近点，主线方向取那里的切线，前后各从那里切开
            if (leg.Trace != null && other.Trace != null)
            {
                var road = new List<Vec3>(other.Trace);
                road.Reverse();
                road.AddRange(leg.Trace.Skip(1));
                if (SplitAt(road, cursor, out Vec3 center, out Vec3 direction, out var ahead, out var behind))
                {
                    result.Center = center;
                    result.MainAngle = AngleOf(direction);
                    result.MainAheadTrace = ahead;
                    result.MainBehindTrace = behind;
                }
            }
            return result;
        }

        /// <summary>
        /// T 型（三向）路口：方向最接近相反的两条臂配成直行的主线（不论车道多少），剩下那条是支路。
        /// CrossingAngle 是支路相对主线臂（离当前朝向近的那条）顺时针的角度，0~360°：小于 180° 时支路在右侧。
        /// </summary>
        private static JunctionMatch MatchT(JunctionMatch result, List<Leg> legs, float preferredAngle)
        {
            foreach (var leg in legs)
            {
                string? error = DescribeLeg(leg);
                if (error != null)
                    return Fail(result, error);
            }

            var pairs = new[] { (0, 1, 2), (0, 2, 1), (1, 2, 0) }
                .Select(t => (a: legs[t.Item1], b: legs[t.Item2], branch: legs[t.Item3]))
                .OrderByDescending(t => Directions.AngleBetween(t.a.Angle, t.b.Angle))
                .ToList();
            var (mainA, mainB, branch) = pairs[0];
            float straightness = Directions.AngleBetween(mainA.Angle, mainB.Angle);
            if (straightness < 180f - kOppositeTolerance)
            {
                // Y 型三岔路口：主线取离当前朝向近的臂，被交道路取顺时针下一条（只用于显示）；中心取三条臂中心点的平均
                result.Wye = true;
                var first = legs.OrderBy(l => Directions.AngleBetween(preferredAngle, l.Angle)).First();
                var next = legs.Where(l => l != first).OrderBy(l => Normalize(l.Angle - first.Angle)).First();
                result.MainAngle = first.Angle;
                result.CrossingAngle = Normalize(next.Angle - first.Angle);
                result.Main = first.Road;
                result.Cross = next.Road;
                result.Center = legs.Aggregate(Vec3.Zero, (acc, l) => acc + l.CenterPoint) * (1f / legs.Count);
                result.TripleLegs = TripleLegs(legs, result.Center);
                return result;
            }

            Leg mainLeg = Directions.AngleBetween(preferredAngle, mainA.Angle) <= Directions.AngleBetween(preferredAngle, mainB.Angle) ? mainA : mainB;
            result.MainAngle = mainLeg.Angle;
            result.CrossingAngle = Normalize(branch.Angle - mainLeg.Angle);
            result.Main = mainLeg.Road;
            result.Cross = branch.Road;
            result.Center = Intersect(Midpoint(mainA, mainB), mainLeg.Direction, branch.CenterPoint, branch.Direction);
            result.MainAheadTrace = FromCenter(mainLeg, result.Center);
            result.MainBehindTrace = FromCenter(mainLeg == mainA ? mainB : mainA, result.Center);
            result.CrossAheadTrace = FromCenter(branch, result.Center);
            result.TripleLegs = TripleLegs(legs, result.Center);
            return result;
        }

        private static List<(float Angle, MatchedRoad Road, IReadOnlyList<Vec3>? Trace)> TripleLegs(List<Leg> legs, Vec3 center) =>
            legs.OrderBy(l => Normalize(l.Angle)).Select(l => (Normalize(l.Angle), l.Road, (IReadOnlyList<Vec3>?)FromCenter(l, center))).ToList();

        private static JunctionMatch Fail(JunctionMatch result, string error)
        {
            result.Error = error;
            return result;
        }

        private static List<Leg> GroupLegs(IReadOnlyList<ExternalEdge> edges)
        {
            var legs = new List<Leg>();
            foreach (var edge in edges)
            {
                float angle = AngleOf(edge.Direction);
                var leg = legs.FirstOrDefault(l => Directions.AngleBetween(l.Angle, angle) <= kSameLegTolerance);
                if (leg == null)
                {
                    leg = new Leg { Angle = angle, Direction = edge.Direction };
                    legs.Add(leg);
                }
                leg.Edges.Add(edge);
            }

            foreach (var leg in legs)
            {
                // 臂方向取各路段方向的平均；臂的中心点取各路段在路口一侧节点的平均
                leg.Direction = MeanDirection(leg.Edges);
                // 同一幅读出了不止一半（立体交叉时交叉点附近有节点，从节点开始的那一半也被收进来）：只留离路口最近的那一半，
                // 否则臂的中心点偏向这一幅（双幅时偏几米），中心线从立交中心出发先横跨一下，读出几十米的假半径
                Vec3 right = Directions.RightOf(leg.Direction);
                var kept = new List<ExternalEdge>();
                foreach (var e in leg.Edges.OrderBy(e => Directions.Dot(e.Anchor, leg.Direction)))
                    if (!kept.Any(k => k.OneWay == e.OneWay && k.TravelOutward == e.TravelOutward
                        && Math.Abs(Directions.Dot(e.Anchor - k.Anchor, right)) < Math.Max(kSameLineMin, Math.Min(e.Width, k.Width) / 2f)))
                        kept.Add(e);
                leg.Edges.Clear();
                leg.Edges.AddRange(kept);
                leg.Direction = MeanDirection(leg.Edges);
                leg.Angle = AngleOf(leg.Direction);
                var anchors = leg.Edges.Aggregate(Vec3.Zero, (acc, e) => acc + e.Anchor);
                leg.CenterPoint = anchors * (1f / leg.Edges.Count);
                // 中心线：双幅两幅都有追踪时取两条追踪的中线（两幅在路口接到同一个节点时锚点重合，按锚点横移会把中心线放在一幅上，
                // 游戏内 Y 型：那条臂的臂端偏出 6 米接不上原有道路）；否则取有追踪的一幅，沿它自己的法向横移它在路口处离臂中心点的横距
                var outTraced = leg.Edges.FirstOrDefault(e => e.OneWay && e.TravelOutward && e.Trace != null && e.Trace.Count >= 2);
                var inTraced = leg.Edges.FirstOrDefault(e => e.OneWay && !e.TravelOutward && e.Trace != null && e.Trace.Count >= 2);
                var traced = leg.Edges.FirstOrDefault(e => e.Trace != null && e.Trace.Count >= 2);
                if (outTraced != null && inTraced != null)
                {
                    leg.Trace = Midline(outTraced.Trace!, inTraced.Trace!, leg.CenterPoint, leg.Direction);
                    leg.TraceSeparation = Separation(outTraced.Trace!, inTraced.Trace!, leg.CenterPoint, leg.Direction);
                }
                else if (traced != null)
                    leg.Trace = Shift(traced.Trace!, Directions.Dot(traced.Anchor - leg.CenterPoint, Directions.RightOf(leg.Direction)));
            }
            return legs;
        }

        /// <summary>两条追踪（都从路口往外）的中线：a 上每个点和它在 b 上最近点的中点；只取到两条里较短那条的末端（沿臂方向）。</summary>
        private static List<Vec3> Midline(IReadOnlyList<Vec3> a, IReadOnlyList<Vec3> b, Vec3 center, Vec3 direction)
        {
            float reach = Math.Min(a.Max(p => Directions.Dot(p - center, direction)), b.Max(p => Directions.Dot(p - center, direction)));
            return a.Where(p => Directions.Dot(p - center, direction) <= reach).Select(p => Vec3.Lerp(p, Nearest(b, p), 0.5f)).ToList();
        }

        /// <summary>
        /// 两幅追踪离路口 kSeparationFrom ~ kSeparationTo 米（沿臂方向）那段的间距中位数；那段没有点时取最远处的点。
        /// 双幅道路在路口可能接到同一个节点，路口处的锚点间距不代表外面的分隔带。
        /// </summary>
        private static float Separation(IReadOnlyList<Vec3> a, IReadOnlyList<Vec3> b, Vec3 center, Vec3 direction)
        {
            var gaps = a.Where(p => Directions.Dot(p - center, direction) is float s && s >= kSeparationFrom && s <= kSeparationTo)
                .Select(p => Distance2(p, Nearest(b, p))).OrderBy(g => g).ToList();
            if (gaps.Count == 0)
            {
                Vec3 far = a.OrderBy(p => Directions.Dot(p - center, direction)).Last();
                return Distance2(far, Nearest(b, far));
            }
            return gaps[gaps.Count / 2];
        }

        private const float kSeparationFrom = 60f, kSeparationTo = 300f;

        /// <summary>折线 line 上离 p 最近的点（俯视）。</summary>
        private static Vec3 Nearest(IReadOnlyList<Vec3> line, Vec3 p)
        {
            Vec3 best = line[0];
            float bestDistance = float.MaxValue;
            for (int i = 0; i + 1 < line.Count; i++)
            {
                Vec3 a = line[i], d = line[i + 1] - a;
                float length2 = d.X * d.X + d.Z * d.Z;
                float t = length2 < 1e-6f ? 0f : Math.Max(0f, Math.Min(1f, ((p.X - a.X) * d.X + (p.Z - a.Z) * d.Z) / length2));
                var q = new Vec3(a.X + d.X * t, a.Y + d.Y * t, a.Z + d.Z * t);
                float distance = Distance2(p, q);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = q;
                }
            }
            return best;
        }

        private static float Distance2(Vec3 a, Vec3 b) => (float)Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z));

        private static Vec3 MeanDirection(IEnumerable<ExternalEdge> edges)
        {
            var sum = edges.Aggregate(Vec3.Zero, (acc, e) => acc + e.Direction);
            return Vec3.Normalize(new Vec3(sum.X, 0f, sum.Z));
        }

        /// <summary>判断一条臂是双向还是双幅，填好道路信息。返回错误原因或 null。</summary>
        private static string? DescribeLeg(Leg leg)
        {
            var twoWay = leg.Edges.FirstOrDefault(e => !e.OneWay);
            if (twoWay != null)
            {
                leg.Road = new MatchedRoad
                {
                    Prefab = twoWay.Road,
                    Form = RoadForm.TwoWay,
                    Width = twoWay.Width,
                    LanesPerDirection = twoWay.LanesPerDirection,
                    IsHighway = twoWay.IsHighway,
                    Elevation = leg.Edges.Average(e => e.Elevation),
                };
                return null;
            }

            var outward = leg.Edges.FirstOrDefault(e => e.TravelOutward);
            var inward = leg.Edges.FirstOrDefault(e => !e.TravelOutward);
            if (outward == null || inward == null)
                return "有一个方向只有单向道路（没有对向的另一幅），无法匹配成双向或双幅道路";

            Vec3 right = Directions.RightOf(leg.Direction);
            float separation = leg.TraceSeparation ?? Math.Abs(Directions.Dot(outward.Anchor - inward.Anchor, right));
            float width = Math.Max(outward.Width, inward.Width);
            leg.Road = new MatchedRoad
            {
                Prefab = outward.Road,
                Form = RoadForm.Dual,
                Width = width,
                MedianWidth = Math.Max(0f, separation - width),
                LanesPerDirection = Math.Min(outward.LanesPerDirection, inward.LanesPerDirection),
                IsHighway = outward.IsHighway,
                Elevation = leg.Edges.Average(e => e.Elevation),
            };
            return null;
        }

        private static Vec3 Midpoint(Leg a, Leg b) => (a.CenterPoint + b.CenterPoint) * 0.5f;

        /// <summary>折线每个点沿该处法向（右侧）往回挪 offset：右侧 offset 米的一幅挪到中心线上。</summary>
        private static List<Vec3> Shift(IReadOnlyList<Vec3> points, float offset) => Shift(points, _ => offset);

        /// <summary>沿法向平移 offset，离 center 沿 direction 超过 0 后逐渐减小，到 kCenterBlend 米处为零（平滑过渡）。</summary>
        private static List<Vec3> ShiftTapered(IReadOnlyList<Vec3> points, float offset, Vec3 center, Vec3 direction) =>
            Shift(points, p =>
            {
                float x = Math.Max(0f, Math.Min(1f, Directions.Dot(p - center, direction) / kCenterBlend));
                return offset * (1f - x * x * (3f - 2f * x));
            });

        /// <summary>FromCenter：中心线横移到过立交中心时，横差从中心到这个距离（米）渐变到零。</summary>
        private const float kCenterBlend = 200f;

        private static List<Vec3> Shift(IReadOnlyList<Vec3> points, Func<Vec3, float> offsetAt)
        {
            var result = new List<Vec3>(points.Count);
            for (int i = 0; i < points.Count; i++)
            {
                float offset = offsetAt(points[i]);
                Vec3 d = points[Math.Min(points.Count - 1, i + 1)] - points[Math.Max(0, i - 1)];
                float length = (float)Math.Sqrt(d.X * d.X + d.Z * d.Z);
                Vec3 right = length > 1e-4f ? Directions.RightOf(new Vec3(d.X / length, 0f, d.Z / length)) : Vec3.Zero;
                result.Add(new Vec3(points[i].X, 0f, points[i].Z) - right * offset);
            }
            return result;
        }

        /// <summary>
        /// 从立交中心出发的这条臂的中心线：中心点在前，接臂的中心线（路口内那一小段当直线）；没有追踪时为 null。
        /// 追踪的起点可能在中心后面（立体交叉时交叉点附近有节点，离开路口那一半从节点开始）：跳过没有超过中心 1 米的点，
        /// 否则中心线先往回走再折返，读出来半径只有几米。追踪和中心横向对不上（臂的中心点、交点估得有偏差，或 Y 型三条臂的
        /// 中心线本来就不交于一点）时平移到过中心的位置：中心处整段横差，离中心 kCenterBlend 米处渐变到零（平滑 S 形），
        /// 中心线从中心出发时不先横跨一下（横差 2 米就读出二三十米的假半径），外端仍在原有道路上（整条平移时，游戏内 Y 型
        /// 一条臂两幅的臂端都偏出 6 米，接不上原有道路）。
        /// </summary>
        private static List<Vec3>? FromCenter(Leg leg, Vec3 center)
        {
            if (leg.Trace == null)
                return null;
            var trace = ShiftTapered(leg.Trace, -Lateral(leg.Trace, center), center, leg.Direction);
            int start = 0;
            while (start < trace.Count - 1 && Directions.Dot(trace[start] - center, leg.Direction) < 1f)
                start++;
            var result = new List<Vec3> { new Vec3(center.X, 0f, center.Z) };
            // 路口附近只留沿臂方向往外走的点：双幅道路两幅在不同节点汇合，平移成中心线后开头可能往回折一下，
            // 读出几米的假半径（游戏内 Y 型：三条直路报“最急处半径只有 6 米”，无法预览）
            float reached = 0f;
            foreach (Vec3 p in trace.Skip(start))
            {
                float along = Directions.Dot(p - center, leg.Direction);
                if (along > kMonotonicReach || along > reached + kMinTraceStep)
                {
                    result.Add(p);
                    reached = Math.Max(reached, along);
                }
            }
            return result;
        }

        /// <summary>FromCenter：离路口中心这么远（米）以内的追踪点要沿臂方向逐个往外，往回折的点去掉。</summary>
        private const float kMonotonicReach = 100f;
        /// <summary>FromCenter：保留的追踪点沿臂方向至少前进这么多（米）。</summary>
        private const float kMinTraceStep = 0.5f;

        /// <summary>point 在折线 line 右侧多远（米）：取 point 沿线的投影落在的那一段（在起点之前时用第一段的延长线）。</summary>
        private static float Lateral(IReadOnlyList<Vec3> line, Vec3 point)
        {
            for (int i = 0; i + 1 < line.Count; i++)
            {
                Vec3 d = line[i + 1] - line[i];
                float length = (float)Math.Sqrt(d.X * d.X + d.Z * d.Z);
                if (length < 1e-4f)
                    continue;
                Vec3 dir = new Vec3(d.X / length, 0f, d.Z / length);
                Vec3 offset = new Vec3(point.X - line[i].X, 0f, point.Z - line[i].Z);
                if (Directions.Dot(offset, dir) <= length || i + 2 == line.Count)
                    return Directions.Dot(offset, Directions.RightOf(dir));
            }
            return 0f;
        }

        /// <summary>
        /// 在折线 road 上找离 cursor 最近的点，从那里切成前后两段（都从切点往外）；direction 是那里沿折线方向的切线。
        /// 折线太短时返回 false。
        /// </summary>
        private static bool SplitAt(List<Vec3> road, Vec3 cursor, out Vec3 center, out Vec3 direction, out List<Vec3> ahead, out List<Vec3> behind)
        {
            center = direction = Vec3.Zero;
            ahead = behind = null!;
            int best = -1;
            float bestDistance = float.MaxValue, bestT = 0f;
            for (int i = 0; i + 1 < road.Count; i++)
            {
                Vec3 a = road[i], b = road[i + 1];
                float dx = b.X - a.X, dz = b.Z - a.Z, length2 = dx * dx + dz * dz;
                if (length2 < 1e-6f)
                    continue;
                float t = Math.Max(0f, Math.Min(1f, ((cursor.X - a.X) * dx + (cursor.Z - a.Z) * dz) / length2));
                float px = a.X + dx * t - cursor.X, pz = a.Z + dz * t - cursor.Z, distance = px * px + pz * pz;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = i;
                    bestT = t;
                }
            }
            if (best < 0)
                return false;
            Vec3 p0 = road[best], p1 = road[best + 1];
            center = Vec3.Lerp(p0, p1, bestT);
            direction = Vec3.Normalize(new Vec3(p1.X - p0.X, 0f, p1.Z - p0.Z));
            ahead = new List<Vec3> { center };
            ahead.AddRange(road.Skip(best + 1));
            behind = new List<Vec3> { center };
            for (int i = best; i >= 0; i--)
                behind.Add(road[i]);
            return true;
        }

        /// <summary>两条水平直线的交点（经过点 p1 方向 d1，经过点 p2 方向 d2）。</summary>
        private static Vec3 Intersect(Vec3 p1, Vec3 d1, Vec3 p2, Vec3 d2)
        {
            float denominator = d1.X * d2.Z - d1.Z * d2.X;
            if (Math.Abs(denominator) < 1e-4f)
                return new Vec3(p1.X, 0f, p1.Z);
            Vec3 delta = p2 - p1;
            float t = (delta.X * d2.Z - delta.Z * d2.X) / denominator;
            return new Vec3(p1.X + d1.X * t, 0f, p1.Z + d1.Z * t);
        }

        /// <summary>水平方向 → 方向角（度，0 = +Z，顺时针），范围 [0, 360)。</summary>
        public static float AngleOf(Vec3 direction) => Normalize((float)(Math.Atan2(direction.X, direction.Z) / Directions.DegToRad));

        private static float Normalize(float degrees)
        {
            float d = degrees % 360f;
            return d < 0f ? d + 360f : d;
        }
    }
}
