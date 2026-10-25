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
        /// <summary>无法匹配时的原因。</summary>
        public string? Error { get; set; }
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

        private sealed class Leg
        {
            public readonly List<ExternalEdge> Edges = new List<ExternalEdge>();
            public float Angle;
            public Vec3 Direction;
            public Vec3 CenterPoint;
            public MatchedRoad Road = new MatchedRoad();
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
                var outward = leg.Edges.Concat(other.Edges).First(e => e.TravelOutward);
                Vec3 travel = outward.Direction;
                Vec3 driveSide = Directions.RightOf(travel) * (rightHandTraffic ? 1f : -1f);
                linePoint = linePoint - driveSide * ((dualMedian + outward.Width) / 2f);
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
                return Fail(result, $"T 型路口里没有一条直行的道路（最直的两条臂夹角 {straightness:F0}°），无法匹配");

            Leg mainLeg = Directions.AngleBetween(preferredAngle, mainA.Angle) <= Directions.AngleBetween(preferredAngle, mainB.Angle) ? mainA : mainB;
            result.MainAngle = mainLeg.Angle;
            result.CrossingAngle = Normalize(branch.Angle - mainLeg.Angle);
            result.Main = mainLeg.Road;
            result.Cross = branch.Road;
            result.Center = Intersect(Midpoint(mainA, mainB), mainLeg.Direction, branch.CenterPoint, branch.Direction);
            return result;
        }

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
                var sum = leg.Edges.Aggregate(Vec3.Zero, (acc, e) => acc + e.Direction);
                leg.Direction = Vec3.Normalize(new Vec3(sum.X, 0f, sum.Z));
                leg.Angle = AngleOf(leg.Direction);
                var anchors = leg.Edges.Aggregate(Vec3.Zero, (acc, e) => acc + e.Anchor);
                leg.CenterPoint = anchors * (1f / leg.Edges.Count);
            }
            return legs;
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
            float separation = Math.Abs(Directions.Dot(outward.Anchor - inward.Anchor, right));
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
