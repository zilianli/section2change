using System.Collections.Generic;
using System.Linq;
using InterchangeMod.Generation.Geometry;
using InterchangeMod.Generation.Junctions;
using InterchangeMod.Generation.Templates;
using Xunit;

namespace InterchangeMod.Generation.Tests
{
    /// <summary>弯道适配（docs/04）：追踪出的道路中心线、匹配结果里的中心线、沿中心线弯曲、弯曲后收紧坡度重算。</summary>
    public class CurvedRoadTests
    {
        private static float Flat(Vec3 a, Vec3 b) => (float)System.Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z));

        /// <summary>
        /// 从 start 沿 direction 方向（角度，度）出发的道路中心线点：先直行 straight 米，再以曲率 κ（向右弯为正）走 curved 米，每 4 米一个点。
        /// </summary>
        private static List<Vec3> Road(Vec3 start, float direction, float straight, float curvature, float curved = 800f)
        {
            var points = new List<Vec3>();
            float heading = direction * Directions.DegToRad;
            Vec3 p = start;
            points.Add(p);
            for (float s = 4f; s <= straight + curved; s += 4f)
            {
                // 按每步中点的方向走，弧线才准
                float turn = s > straight ? curvature * 4f : 0f;
                p = p + Directions.FromAngle(0f).RotateY(heading + turn / 2f) * 4f;
                heading += turn;
                points.Add(p);
            }
            return points;
        }

        [Fact]
        public void CenterlineFromPoints_IsCanonicalAndKeepsShape()
        {
            // 任意位置、任意方向出发的一段圆弧：平移到原点、起点朝 +Z，半径读得准
            var line = Centerline.FromPoints(Road(new Vec3(300f, 0f, -50f), 70f, 0f, 1f / 500f), Directions.FromAngle(70f));
            Assert.InRange(line.Point(0f).X, -0.01f, 0.01f);
            Assert.InRange(line.Point(0f).Z, -0.01f, 0.01f);
            Assert.InRange(line.Angle(0f), -0.01f, 0.01f);
            Assert.InRange(line.MinRadius(600f), 480f, 520f);
            var arc = Centerline.Arc(1f / 500f);
            foreach (float s in new[] { 100f, 300f, 600f })
                Assert.True(Flat(line.Point(s), arc.Point(s)) < 0.5f, $"s {s}: {line.Point(s)} vs {arc.Point(s)}");
            // 超出追踪长度：按最后的切线直线延伸
            Assert.InRange(Flat(line.Point(line.Length + 100f), line.Point(line.Length)), 99.5f, 100.5f);
        }

        [Fact]
        public void StraightThenCurve_IsStraightNearTheCenter()
        {
            var line = Centerline.FromPoints(Road(Vec3.Zero, 0f, 200f, -1f / 400f), Directions.FromAngle(0f));
            Assert.InRange(line.Angle(150f), -0.001f, 0.001f);
            Assert.InRange(line.Angle(400f), -0.5f - 0.02f, -0.5f + 0.02f);
            Assert.InRange(line.MinRadius(600f), 380f, 420f);
            Assert.True(float.IsPositiveInfinity(Centerline.FromPoints(Road(Vec3.Zero, 0f, 800f, 0f), Directions.FromAngle(0f)).MinRadius(600f)));
        }

        [Fact]
        public void GentleCurve_IsNotStraight()
        {
            // 游戏内：两条半径 3000 多米的弯道立体交叉，按“半径大于 3000 米当直线”判断时定向式的臂端（离中心 700~850 米）偏出原有道路 50~80 米
            var gentle = Centerline.FromPoints(Road(Vec3.Zero, 0f, 0f, 1f / 3500f, 1200f), Directions.FromAngle(0f));
            Assert.True(gentle.MinRadius(600f) > 3000f);
            Assert.False(gentle.NearlyStraight(0.5f));
            Assert.True(Flat(gentle.Point(800f), new Vec3(0f, 0f, 800f)) > 80f);
            Assert.True(Centerline.FromPoints(Road(Vec3.Zero, 0f, 1200f, 0f, 0f), Directions.FromAngle(0f)).NearlyStraight(0.5f));
        }

        /// <summary>一条双向道路穿过原点：沿 angle 离开的一条臂和反方向那条臂，各带追踪（前方先直行 aheadStraight 米再弯）。</summary>
        private static IEnumerable<ExternalEdge> TwoWayRoad(float angle, float aheadStraight, float aheadCurvature, float behindCurvature, string road, int lanes)
        {
            yield return new ExternalEdge
            {
                Anchor = Vec3.Zero, Direction = Directions.FromAngle(angle), Road = road, Width = 16f, LanesPerDirection = lanes,
                Trace = Road(Vec3.Zero, angle, aheadStraight, aheadCurvature),
            };
            yield return new ExternalEdge
            {
                Anchor = Vec3.Zero, Direction = Directions.FromAngle(angle + 180f), Road = road, Width = 16f, LanesPerDirection = lanes,
                Trace = Road(Vec3.Zero, angle + 180f, 0f, behindCurvature),
            };
        }

        [Fact]
        public void MatchCarriesTracesFromTheCenterAndSwapsThem()
        {
            var edges = TwoWayRoad(0f, 150f, 1f / 500f, 0f, "Main", 3).Concat(TwoWayRoad(90f, 0f, 0f, 0f, "Cross", 2)).ToList();
            var m = JunctionMatcher.Match(edges, preferredAngle: 0f);
            Assert.Null(m.Error);
            Assert.Equal(0f, m.MainAngle, 1);
            // 主线前方先直后弯，后方直：两侧各自的形状
            var ahead = Centerline.FromPoints(m.MainAheadTrace!, Directions.FromAngle(m.MainAngle));
            var behind = Centerline.FromPoints(m.MainBehindTrace!, Directions.FromAngle(m.MainAngle + 180f));
            Assert.InRange(ahead.Angle(100f), -0.001f, 0.001f);
            Assert.True(ahead.Angle(500f) > 0.5f);
            Assert.True(float.IsPositiveInfinity(behind.MinRadius(600f)));
            // 对调：新主线是原被交道路；新被交道路前方是原主线的后方
            var mainBehind = m.MainBehindTrace;
            m.SwapTraces();
            Assert.Same(mainBehind, m.CrossAheadTrace);
        }

        [Fact]
        public void WyeMatch_CarriesEachLegsCenterline()
        {
            // Y 型三岔路口三条臂各有自己的弯：匹配结果里每条臂一条从中心往外的中心线
            float[] curvatures = { 0f, 1f / 500f, -1f / 700f };
            var edges = new List<ExternalEdge>();
            for (int i = 0; i < 3; i++)
                edges.Add(new ExternalEdge
                {
                    Anchor = Vec3.Zero, Direction = Directions.FromAngle(120f * i), Road = "R" + i, Width = 16f, LanesPerDirection = 2,
                    Trace = Road(Vec3.Zero, 120f * i, 0f, curvatures[i]),
                });
            var m = JunctionMatcher.Match(edges, preferredAngle: 0f);
            Assert.Null(m.Error);
            Assert.True(m.Wye);
            for (int i = 0; i < 3; i++)
            {
                var (angle, _, trace) = m.TripleLegs![i];
                var line = Centerline.FromPoints(trace!, Directions.FromAngle(angle));
                if (curvatures[i] == 0f)
                    Assert.True(line.NearlyStraight(0.5f));
                else
                    Assert.InRange(line.MinRadius(600f), 0.95f / System.Math.Abs(curvatures[i]), 1.05f / System.Math.Abs(curvatures[i]));
            }
        }

        [Fact]
        public void ThreeLegWarp_EachLegFollowsItsOwnCurve()
        {
            float[] angles = { 0f, 125f, 235f };
            var lines = new[] { Centerline.Straight, Centerline.Arc(1f / 500f), Centerline.Arc(-1f / 700f) };
            var warp = new CurveWarp(angles, lines);
            Assert.False(warp.IsIdentity);
            Assert.True(new CurveWarp(angles, new[] { Centerline.Straight, Centerline.Straight, Centerline.Straight }).IsIdentity);
            // 臂上离中心 260 米以外：走廊里的点落在这条臂弯道中心线同一弧长处的法线上，横距不变（各幅、匝道精确平行）
            for (int i = 0; i < 3; i++)
            {
                float yaw = angles[i] * Directions.DegToRad;
                Vec3 dir = Directions.FromAngle(angles[i]), right = Directions.RightOf(dir);
                foreach (float a in new[] { 260f, 400f, 700f })
                {
                    Vec3 center = lines[i].Point(a).RotateY(yaw);
                    Vec3 tangent = Vec3.Normalize(lines[i].Point(a + 0.5f).RotateY(yaw) - lines[i].Point(a - 0.5f).RotateY(yaw));
                    foreach (float lateral in new[] { -30f, -14f, 0f, 14f, 30f })
                    {
                        Vec3 off = warp.Apply(dir * a + right * lateral) - center;
                        Assert.InRange(Directions.Dot(off, tangent), -0.05f, 0.05f);
                        Assert.InRange(Directions.Dot(off, Directions.RightOf(tangent)), lateral - 0.05f, lateral + 0.05f);
                    }
                }
            }
            // 立交用到的范围（离中心 350 米内、各臂走廊里 600 米内）处处连续、不折叠：相距 1 米的两点弯曲后相距 0.25–2 米
            // （两条臂都朝同一个扇区弯时那里确实被挤窄：这里臂 1、臂 2 离中心 350 米处夹角从 110° 缩到约 40°，压缩到一半以下是对的）
            bool InUse(Vec3 v) => v.X * v.X + v.Z * v.Z <= 350f * 350f || angles.Any(angle =>
            {
                Vec3 dir = Directions.FromAngle(angle);
                float along = Directions.Dot(v, dir);
                return along >= 0f && along <= 600f && System.Math.Abs(Directions.Dot(v, Directions.RightOf(dir))) <= 60f;
            });
            for (float x = -600f; x <= 600f; x += 10f)
                for (float z = -600f; z <= 600f; z += 10f)
                {
                    var v = new Vec3(x, 0f, z);
                    if (!InUse(v))
                        continue;
                    foreach (var step in new[] { new Vec3(1f, 0f, 0f), new Vec3(0f, 0f, 1f) })
                    {
                        float moved = Flat(warp.Apply(v), warp.Apply(v + step));
                        Assert.True(moved > 0.25f && moved < 2f, $"({x}, {z}) + {step}: {moved:F2}");
                    }
                }
        }

        [Fact]
        public void PlacedOnCurvedRoad_CenterAndHeadingFollowTheRoad()
        {
            // 只对准一条弯道（双向）：鼠标在离节点 200 米处的弯道旁边，中心落在道路上、主线方向取那里的切线
            var edges = TwoWayRoad(0f, 0f, 1f / 500f, -1f / 500f, "Main", 2).ToList();
            var road = Road(Vec3.Zero, 0f, 0f, 1f / 500f);
            Vec3 onRoad = road[50];
            var m = JunctionMatcher.Match(edges, preferredAngle: 0f, cursor: onRoad + new Vec3(3f, 0f, 0f));
            Assert.True(m.RoadOnly);
            Assert.True(Flat(m.Center, onRoad) < 3.5f, $"center {m.Center} vs {onRoad}");
            Assert.InRange(m.MainAngle, 200f * (1f / 500f) / Directions.DegToRad - 2f, 200f * (1f / 500f) / Directions.DegToRad + 2f);
        }

        /// <summary>双幅道路：中心线 points 两侧各一幅（右侧沿点的方向行驶），各在交叉点 t 处切开、各半带追踪。</summary>
        private static IEnumerable<ExternalEdge> DualCrossingHalves(List<Vec3> center, float median, float width, Vec3 crossing, string road, int nodeBehind = 0,
            int duplicateAt = 0)
        {
            float half = (median + width) / 2f;
            foreach (float side in new[] { 1f, -1f })
            {
                // 这一幅：中心线沿法向挪 side·half；右侧那幅沿点的方向走，左侧那幅反着走
                var lane = new List<Vec3>();
                for (int i = 0; i < center.Count; i++)
                {
                    Vec3 d = center[System.Math.Min(center.Count - 1, i + 1)] - center[System.Math.Max(0, i - 1)];
                    lane.Add(center[i] + Directions.RightOf(Vec3.Normalize(d)) * (side * half));
                }
                if (side < 0f)
                    lane.Reverse();
                int k = 0;
                float best = float.MaxValue;
                for (int i = 0; i < lane.Count; i++)
                    if (Flat(lane[i], crossing) < best)
                    {
                        best = Flat(lane[i], crossing);
                        k = i;
                    }
                // 游戏里交叉点附近常有节点：这一幅离开路口那一半从交叉点之前 nodeBehind 个点（每点 4 米）的节点开始
                var forward = lane.Skip(k - nodeBehind).ToList();
                var backward = lane.Take(k + 1).Reverse().ToList();
                var halves = new List<(List<Vec3>, bool)> { (forward, true), (backward, false) };
                // 节点离交叉点不远（30 米内）时游戏里那一幅还会多读出一半：从节点开始往外（一样的方向和追踪），只在一侧的一幅上
                if (duplicateAt > 0 && side > 0f)
                    halves.Add((lane.Skip(k + duplicateAt).ToList(), true));
                foreach (var (trace, outward) in halves)
                    yield return new ExternalEdge
                    {
                        Anchor = trace[0], Direction = Vec3.Normalize(trace[1] - trace[0]), Road = road, OneWay = true, TravelOutward = outward,
                        Width = width, LanesPerDirection = 4, IsHighway = true, Trace = trace,
                    };
            }
        }

        /// <summary>把折线平移、旋转到第 at 个点在原点、那里的方向为 heading（度）。</summary>
        private static List<Vec3> Place(List<Vec3> points, int at, float heading)
        {
            Vec3 d = points[at + 1] - points[at - 1];
            float turn = heading * Directions.DegToRad - (float)System.Math.Atan2(d.X, d.Z);
            return points.Select(p => (p - points[at]).RotateY(turn)).ToList();
        }

        /// <summary>点到折线的俯视距离。</summary>
        private static float DistanceTo(List<Vec3> line, Vec3 p)
        {
            float best = float.MaxValue;
            for (int i = 0; i + 1 < line.Count; i++)
            {
                Vec3 a = line[i], ab = line[i + 1] - a;
                float l2 = ab.X * ab.X + ab.Z * ab.Z;
                float t = l2 < 1e-6f ? 0f : System.Math.Max(0f, System.Math.Min(1f, ((p.X - a.X) * ab.X + (p.Z - a.Z) * ab.Z) / l2));
                best = System.Math.Min(best, Flat(a + ab * t, p));
            }
            return best;
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(2, 0)]
        [InlineData(0, 3)]
        [InlineData(2, 5)]
        public void DualCrossing_BothCurvedThroughTheCrossing_TracesStayOnTheRoads(int nodeBehind, int duplicateAt)
        {
            // 游戏内的情形：两条双幅弯道（R 500、R 700）在弯道中间斜交 77°；立体交叉以鼠标所指那一幅与另一条路一幅的交点为中心切开。
            // 追踪出的中心线在离中心 200~400 米处要落在实际道路中心线上（臂端才接得回原有道路），也不能读出假的急弯
            var mainLine = Place(Road(Vec3.Zero, 0f, 0f, 1f / 500f, 800f), 100, 30f);
            var crossLine = Place(Road(Vec3.Zero, 0f, 0f, -1f / 700f, 800f), 100, 107f);
            Vec3 Lane(List<Vec3> line, int i, float side)
            {
                Vec3 d = line[i + 1] - line[i - 1];
                return line[i] + Directions.RightOf(Vec3.Normalize(d)) * (side * 14f);
            }
            // 主线右幅与被交道路右幅的交点
            Vec3 c0 = Vec3.Zero;
            float closest = float.MaxValue;
            for (int i = 60; i < 140; i++)
                for (int j = 60; j < 140; j++)
                {
                    Vec3 a = Lane(mainLine, i, 1f), b = Lane(crossLine, j, 1f);
                    if (Flat(a, b) < closest)
                    {
                        closest = Flat(a, b);
                        c0 = (a + b) * 0.5f;
                    }
                }
            var edges = DualCrossingHalves(mainLine, 8f, 20f, c0, "Main", nodeBehind, duplicateAt)
                .Concat(DualCrossingHalves(crossLine, 8f, 20f, c0, "Cross", nodeBehind, duplicateAt)).ToList();
            var m = JunctionMatcher.Match(edges, preferredAngle: 30f);
            Assert.Null(m.Error);
            float worst = 0f;
            var report = new List<string>();
            foreach (var (name, trace, line) in new[] { ("main ahead", m.MainAheadTrace, mainLine), ("main behind", m.MainBehindTrace, mainLine),
                ("cross ahead", m.CrossAheadTrace, crossLine), ("cross behind", m.CrossBehindTrace, crossLine) })
            {
                float off = trace!.Where(p => Flat(p, m.Center) > 200f && Flat(p, m.Center) < 400f).Max(p => DistanceTo(line, p));
                float radius = Centerline.FromPoints(trace!, trace![1] - trace[0]).MinRadius(600f);
                report.Add($"{name}: off {off:F2} m, R {radius:F0}");
                worst = System.Math.Max(worst, off);
                Assert.True(radius > 400f, string.Join("; ", report));
            }
            Assert.True(worst < 1.5f, $"center {m.Center} ({Flat(m.Center, Vec3.Zero):F2} m from the true crossing): " + string.Join("; ", report));
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(1, 0)]
        [InlineData(2, 0)]
        [InlineData(0, 2)]
        [InlineData(0, 6)]
        [InlineData(1, 4)]
        public void DualCrossing_CurvedAndStraight_ReadsSaneRadii(int nodeBehind, int duplicateAt)
        {
            // 游戏内的情形：被交道路直、主线一侧直一侧弯（R 300），双幅，立体交叉切开读出。
            // duplicateAt：一侧一幅多读出一半（从交叉点外 duplicateAt 个点的节点开始）——游戏内收集到 5 / 7 段时读出 23~35 米的假半径
            var mainLine = Road(new Vec3(0f, 0f, -400f), 0f, 400f, 1f / 300f, 400f);
            var crossLine = Road(new Vec3(-400f, 0f, 0f), 90f, 800f, 0f, 0f);
            var edges = DualCrossingHalves(mainLine, 8f, 20f, Vec3.Zero, "Main", nodeBehind, duplicateAt)
                .Concat(DualCrossingHalves(crossLine, 8f, 20f, Vec3.Zero, "Cross", nodeBehind, duplicateAt)).ToList();
            var m = JunctionMatcher.Match(edges, preferredAngle: 0f);
            Assert.Null(m.Error);
            float Radius(IReadOnlyList<Vec3>? trace, float angle) => Centerline.FromPoints(trace!, Directions.FromAngle(angle)).MinRadius(600f);
            float mainAhead = Radius(m.MainAheadTrace, m.MainAngle), mainBehind = Radius(m.MainBehindTrace, m.MainAngle + 180f);
            float crossAhead = Radius(m.CrossAheadTrace, m.MainAngle + m.CrossingAngle), crossBehind = Radius(m.CrossBehindTrace, m.MainAngle + m.CrossingAngle + 180f);
            // 弯的那一侧读出 R 300 左右，其余三侧是直线（不能被当成直线，也不能读出假的急弯）
            string all = $"main {m.MainAngle:F0}: {mainAhead:F0} / {mainBehind:F0}, cross {m.CrossingAngle:F0}: {crossAhead:F0} / {crossBehind:F0}";
            var radii = new[] { mainAhead, mainBehind, crossAhead, crossBehind };
            Assert.True(radii.Count(r => r > 250f && r < 360f) == 1 && radii.Count(r => r > 3000f) == 3, all);
        }

        [Fact]
        public void EachSideBendsOnItsOwn()
        {
            // 主线后方直、前方先直行 100 米再向右弯 R 500：后方的点留在直线上，前方的点落在实际中心线上
            var aheadPoints = Road(Vec3.Zero, 0f, 100f, 1f / 500f);
            var warp = new CurveWarp(90f, Centerline.FromPoints(aheadPoints, Directions.FromAngle(0f)), Centerline.Straight, Centerline.Straight, Centerline.Straight);
            foreach (float s in new[] { -300f, -100f, -10f })
            {
                var q = warp.Apply(new Vec3(0f, 0f, s));
                Assert.InRange(q.X, -0.01f, 0.01f);
                Assert.InRange(q.Z, s - 0.01f, s + 0.01f);
            }
            foreach (int i in new[] { 10, 50, 100, 150 })
            {
                var q = warp.Apply(new Vec3(0f, 0f, i * 4f));
                Assert.True(Flat(q, aheadPoints[i]) < 0.3f, $"s {i * 4}: {q} vs {aheadPoints[i]}");
            }
        }

        public static IEnumerable<object[]> Curves() => new[]
        {
            new object[] { 1f / 500f, 0f },
            new object[] { -1f / 400f, 0f },
            new object[] { 1f / 600f, -1f / 700f },
        };

        [Theory]
        [MemberData(nameof(Curves))]
        public void DiamondOnCurve_StaysWithinGrade(float mainCurvature, float crossCurvature)
        {
            var template = new DiamondTemplate();
            var p = InterchangeLayout.FitRampLength(template, new InterchangeParams());
            var warp = new CurveWarp(p.Legs[1].Angle, mainCurvature, crossCurvature);
            var segments = InterchangeLayout.Bend(template, p, warp, out InterchangeParams used);
            Assert.True(used.MaxGrade <= p.MaxGrade + 1e-6f);
            Assert.Empty(InterchangeLayout.ValidateGeometry(segments, p));
            TestHelpers.AssertRampsConnected(segments);
        }

        [Theory]
        [MemberData(nameof(Curves))]
        public void CloverleafOnCurve_StaysWithinGrade(float mainCurvature, float crossCurvature)
        {
            var template = new CloverleafTemplate();
            var p = new InterchangeParams { Legs = InterchangeParams.CrossLegs(90f, 0, 1), RampWidth = 8f, LoopRadius = 0f };
            p.SetRoad(0, 2, RoadForm.Dual, 16f, 4f, "Highway Oneway");
            p.SetRoad(1, 3, RoadForm.Dual, 12f, 4f, "Cross Oneway");
            p.Levels[0].Elevation = 10f;
            p.Levels[1].Elevation = 0f;
            Assert.Empty(template.ValidateParams(p));
            var warp = new CurveWarp(p.Legs[1].Angle, mainCurvature, crossCurvature);
            var segments = InterchangeLayout.Bend(template, p, warp, out InterchangeParams used);
            var errors = InterchangeLayout.ValidateGeometry(segments, p);
            Assert.True(errors.Count == 0, $"used {used.MaxGrade:P1}: " + string.Join("; ", errors));
            TestHelpers.AssertRampsConnected(segments);
        }

        public static IEnumerable<object[]> BigInterchanges() =>
            from kind in new[] { "stack", "stackLeftExit", "turbine" }
            from curve in new[] { (1f / 900f, 0f), (-1f / 1500f, 1f / 1300f) }
            select new object[] { kind, curve.Item1, curve.Item2 };

        /// <summary>
        /// 定向式、涡轮这种大型立交在弯道上（游戏内：分流处报“形状无效 / 物体重叠”）：匝道在分流、汇入处离道路的横距弯曲前后不变——
        /// 贴着道路走的匝道和道路在同一条走廊里一起弯；坡度不超限、各段照样相连。
        /// </summary>
        [Theory]
        [MemberData(nameof(BigInterchanges))]
        public void BigInterchangeOnCurve_RampsKeepTheirOffsetAtDiverges(string kind, float mainCurvature, float crossCurvature)
        {
            var template = new StackTemplate(turbine: kind == "turbine");
            var p = new InterchangeParams { Legs = InterchangeParams.CrossLegs(90f, 0, 1), RampWidth = 12f, RampSideGap = 1f, StackLeftExit = kind == "stackLeftExit" };
            p.SetRoad(0, 2, RoadForm.Dual, 20f, 8f, "Highway Oneway");
            p.SetRoad(1, 3, RoadForm.Dual, 20f, 8f, "Highway Oneway");
            p.Levels[0].Elevation = 0f;
            p.Levels[1].Elevation = 10f;
            Assert.Empty(template.ValidateParams(p));
            var warp = new CurveWarp(p.Legs[1].Angle, mainCurvature, crossCurvature);
            var bent = InterchangeLayout.Bend(template, p, warp, out InterchangeParams used);
            var errors = InterchangeLayout.ValidateGeometry(bent, p);
            Assert.True(errors.Count == 0, $"used {used.MaxGrade:P1}: " + string.Join("; ", errors));
            TestHelpers.AssertRampsConnected(bent);
            // 同一组参数弯曲前后逐段对应：匝道从道路节点出发处沿匝道 5 / 10 / 20 米的点，弯曲后（同一个点变换过去）离最近道路的距离不变
            // （按弧长重新取点不行：走廊里沿路方向会按 1 ± 横距 / 半径 伸缩约 1%，分流斜段上同样弧长处的横距本来就会差十几厘米）
            var straight = template.Generate(used);
            Assert.Equal(straight.Count, bent.Count);
            // 到道路折线（每段 80 份）的垂直距离；沿匝道按实际弧长取点（弯曲后参数和弧长的对应会变）
            float Nearest(Vec3 v, IEnumerable<RoadSegmentSpec> roads)
            {
                float best = float.MaxValue;
                foreach (var r in roads)
                    for (int k = 0; k < 80; k++)
                    {
                        Vec3 a = r.Curve.Evaluate(k / 80f), b = r.Curve.Evaluate((k + 1) / 80f);
                        float dx = b.X - a.X, dz = b.Z - a.Z, len2 = dx * dx + dz * dz;
                        float u = len2 > 0f ? System.Math.Max(0f, System.Math.Min(1f, ((v.X - a.X) * dx + (v.Z - a.Z) * dz) / len2)) : 0f;
                        best = System.Math.Min(best, Flat(new Vec3(a.X + dx * u, 0f, a.Z + dz * u), v));
                    }
                return best;
            }
            Vec3 AtLength(Bezier3 curve, float along)
            {
                float length = 0f;
                Vec3 prev = curve.Evaluate(0f);
                for (int k = 1; k <= 400; k++)
                {
                    Vec3 next = curve.Evaluate(k / 400f);
                    float step = Flat(prev, next);
                    if (length + step >= along)
                        return Vec3.Lerp(prev, next, (along - length) / step);
                    length += step;
                    prev = next;
                }
                return prev;
            }
            var roadsStraight = straight.Where(sg => sg.Role != RoadRole.Ramp).ToList();
            var roadsBent = bent.Where(sg => sg.Role != RoadRole.Ramp).ToList();
            var roadNodes = new HashSet<Vec3>(roadsStraight.SelectMany(sg => new[] { sg.StartNode, sg.EndNode }));
            for (int i = 0; i < straight.Count; i++)
            {
                var ramp = straight[i];
                if (ramp.Role != RoadRole.Ramp || !roadNodes.Contains(ramp.StartNode))
                    continue;
                float length = ramp.Curve.Length();
                foreach (float along in new[] { 5f, 10f, 20f })
                {
                    if (along > length)
                        continue;
                    Vec3 point = AtLength(ramp.Curve, along);
                    float before = Nearest(point, roadsStraight), after = Nearest(warp.Apply(point), roadsBent);
                    Assert.True(System.Math.Abs(after - before) < 0.1f, $"{ramp.Name} {along} m: {before:F2} -> {after:F2}");
                }
            }
        }

        [Fact]
        public void SteepInnerSide_TightensGradeAndSolvesAgain()
        {
            // 弯道很急时内侧匝道被压短：第一次弯曲坡度超限，收紧后重算到上限以内
            var template = new DiamondTemplate();
            var p = InterchangeLayout.FitRampLength(template, new InterchangeParams());
            var warp = new CurveWarp(p.Legs[1].Angle, 1f / 200f, 0f);
            float first = warp.Apply(template.Generate(p)).Max(s => s.Curve.MaxGrade());
            var segments = InterchangeLayout.Bend(template, p, warp, out InterchangeParams used);
            if (first > p.MaxGrade + 0.005f)
                Assert.True(used.MaxGrade < p.MaxGrade, $"first {first:P1}, used {used.MaxGrade:P1}");
            Assert.Empty(InterchangeLayout.ValidateGeometry(segments, p));
        }
    }
}
