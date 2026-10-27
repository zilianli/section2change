using System.Linq;
using InterchangeMod.Generation;
using InterchangeMod.Generation.Geometry;
using InterchangeMod.Generation.Templates;
using Xunit;

namespace InterchangeMod.Generation.Tests
{
    public class FootprintTests
    {
        // 边长 100 的正方形（由两条对角线撑开凸包），外扩 2 m
        private static Footprint Square() => new Footprint(new[]
        {
            Bezier3.Line(new Vec3(-50, 0, -50), new Vec3(50, 0, 50)),
            Bezier3.Line(new Vec3(-50, 0, 50), new Vec3(50, 0, -50)),
        }, 2f);

        [Fact]
        public void Contains_IncludesMargin()
        {
            var f = Square();
            Assert.True(f.Contains(0, 0));
            Assert.True(f.Contains(51.5f, 0));
            Assert.False(f.Contains(53f, 0));
            Assert.Equal(-52f, f.MinX, 3);
            Assert.Equal(52f, f.MaxZ, 3);
        }

        [Fact]
        public void LineThroughFootprint_KeepsBothOutsideParts()
        {
            var f = Square();
            // 从 x = -200 到 200：范围是 [-52, 52]，对应 t = 0.37 ~ 0.63
            var outside = f.OutsideIntervals(Bezier3.Line(new Vec3(-200, 0, 0), new Vec3(200, 0, 0)));
            Assert.Equal(2, outside.Count);
            Assert.Equal(0f, outside[0].Start);
            Assert.Equal(0.37f, outside[0].End, 3);
            Assert.Equal(0.63f, outside[1].Start, 3);
            Assert.Equal(1f, outside[1].End);
        }

        [Fact]
        public void CurveInsideOrOutside()
        {
            var f = Square();
            Assert.Empty(f.OutsideIntervals(Bezier3.Line(new Vec3(-10, 0, 0), new Vec3(10, 0, 0))));
            var away = Bezier3.Line(new Vec3(100, 0, 0), new Vec3(200, 0, 0));
            Assert.Equal(new[] { (0f, 1f) }, f.OutsideIntervals(away));
            Assert.False(f.Intersects(away));
            Assert.True(f.Intersects(Bezier3.Line(new Vec3(40, 0, 0), new Vec3(200, 0, 0))));
        }

        [Fact]
        public void Diamond_ExistingRoadsAreCutJustBeyondLegEnds()
        {
            var template = new DiamondTemplate();
            var p = InterchangeLayout.FitRampLength(template, new InterchangeParams());
            var segments = template.Generate(p);
            var f = new Footprint(segments.Select(s => s.Curve), 2f);

            // 原有主线从中心向两边各伸出 1000 m
            Vec3 dir = p.Legs[0].Direction;
            float mainEnd = segments.Where(s => s.Role == RoadRole.MainRoad)
                .Max(s => System.Math.Max(System.Math.Abs(Directions.Dot(s.Curve.P0, dir)), System.Math.Abs(Directions.Dot(s.Curve.P3, dir))));
            var outside = f.OutsideIntervals(Bezier3.Line(dir * -1000f, dir * 1000f), 256);
            Assert.Equal(2, outside.Count);
            float cut = 1000f - outside[0].End * 2000f;   // 剩余路段起点离中心的距离
            Assert.InRange(cut, mainEnd + 1.9f, mainEnd + 2.5f);
        }

        [Theory]
        [InlineData(false, false, false, 4)]
        [InlineData(true, false, false, 6)]
        [InlineData(true, true, false, 4)]
        [InlineData(true, false, true, 8)]
        [InlineData(false, false, true, 6)]
        public void LegEnds_OnePerTwoWayEndTwoPerDualEnd(bool dualMain, bool mainTransition, bool dualCross, int expected)
        {
            var template = new DiamondTemplate();
            var p = new InterchangeParams { RampLength = 260f };
            if (dualMain) p.SetRoad(0, 2, RoadForm.Dual, 12f, 6f, "Oneway");
            if (mainTransition)
                foreach (int i in new[] { 0, 2 }) { p.Legs[i].ExternalForm = RoadForm.TwoWay; p.Legs[i].ExternalPrefab = "TwoWay"; }
            if (dualCross) p.SetRoad(1, 3, RoadForm.Dual, 12f, 6f, "CrossOneway");
            p = InterchangeLayout.FitRampLength(template, p);
            int ends = InterchangeLayout.LegEnds(template.Generate(p)).Count;
            Assert.Equal(expected, ends);   // 双向一端 1 个，双幅一端 2 个，过渡成双向后 1 个
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void UpperLegs_SlopeDownToExternalRoad(bool dualCross)
        {
            var template = new DiamondTemplate();
            var p = new InterchangeParams();
            if (dualCross) p.SetRoad(1, 3, RoadForm.Dual, 12f, 6f, "CrossOneway");
            p.Levels[1].Elevation = 8.5f;
            foreach (var leg in p.Legs) leg.ExternalElevation = 0f;
            p = InterchangeLayout.FitRampLength(template, p);
            Assert.Empty(template.ValidateParams(p));
            var segments = template.Generate(p);
            Assert.Empty(InterchangeLayout.ValidateGeometry(segments, p));

            // 被交道路每个臂端（双幅时每幅）多一段坡道；所有臂端都降到 0 米，主线不变
            Assert.Equal(dualCross ? 4 : 2, segments.Count(s => s.Name.EndsWith("-slope")));
            Assert.All(InterchangeLayout.LegEnds(segments), e => Assert.Equal(0f, e.Y));
            foreach (var s in segments.Where(s => s.Name.EndsWith("-slope")))
                Assert.True(s.Curve.MaxGrade() <= p.MaxGrade + 1e-4f, $"{s.Name} grade {s.Curve.MaxGrade():P1}");
        }

        /// <summary>点到折线（曲线每 1/400 取一点）的最近距离，暴力算。</summary>
        private static float Distance(Bezier3 curve, float x, float z)
        {
            float best = float.MaxValue;
            Vec3 previous = curve.Evaluate(0f);
            for (int i = 1; i <= 400; i++)
            {
                Vec3 next = curve.Evaluate(i / 400f);
                float dx = next.X - previous.X, dz = next.Z - previous.Z, len2 = dx * dx + dz * dz;
                float t = len2 > 0f ? System.Math.Max(0f, System.Math.Min(1f, ((x - previous.X) * dx + (z - previous.Z) * dz) / len2)) : 0f;
                float px = previous.X + t * dx - x, pz = previous.Z + t * dz - z;
                best = System.Math.Min(best, (float)System.Math.Sqrt(px * px + pz * pz));
                previous = next;
            }
            return best;
        }

        [Fact]
        public void Corridor_ContainsPointsWithinHalfWidthOfAnyRoad()
        {
            // 走廊按网格分桶查找：和逐段暴力算的结果一致（离边界 0.5 米以内的点不比，采样折线和曲线本身差几厘米）
            var roads = new[]
            {
                (Bezier3.Line(new Vec3(-300, 0, 0), new Vec3(300, 0, 0)), 10f),
                (new Bezier3(new Vec3(0, 0, -200), new Vec3(150, 0, -50), new Vec3(-150, 0, 50), new Vec3(0, 0, 200)), 6f),
                (Bezier3.Line(new Vec3(100, 0, 100), new Vec3(140, 0, 130)), 4f),
            };
            var corridor = Footprint.Corridor(roads, 2f);
            Assert.True(corridor.IsCorridor);
            var random = new System.Random(7);
            for (int i = 0; i < 4000; i++)
            {
                float x = (float)random.NextDouble() * 700f - 350f, z = (float)random.NextDouble() * 500f - 250f;
                float extra = i % 2 == 0 ? 0f : 5f;
                float margin = roads.Min(r => Distance(r.Item1, x, z) - (r.Item2 + 2f + extra));
                if (System.Math.Abs(margin) < 0.5f)
                    continue;
                Assert.True(corridor.Contains(x, z, extra) == margin < 0f, $"({x:F1}, {z:F1}) extra {extra}: {margin:F2} m outside");
            }
        }

        [Fact]
        public void Corridor_CutsCrossingStreetJustOutsideTheNewRoad()
        {
            // 新路沿 X 轴、半宽 10 米：垂直穿过的街道（半宽 5 米）在离中心线 10 + 2 + 5 = 17 米处切开，两侧保留
            var corridor = Footprint.Corridor(new[] { (Bezier3.Line(new Vec3(-300, 0, 0), new Vec3(300, 0, 0)), 10f) }, 2f);
            var street = Bezier3.Line(new Vec3(0, 0, -100), new Vec3(0, 0, 100));
            var outside = corridor.OutsideIntervals(street, 64, 5f);
            Assert.Equal(2, outside.Count);
            Assert.Equal(83f / 200f, outside[0].End, 2);
            Assert.Equal(117f / 200f, outside[1].Start, 2);
            Assert.False(corridor.Intersects(Bezier3.Line(new Vec3(-100, 0, 30), new Vec3(100, 0, 30)), 64, 5f));
        }

        [Fact]
        public void CurvedDiamond_CorridorStaysNearTheNewRoads()
        {
            // 首轮核查第 3 条：原路口改造（主线高架 10 米、两端下坡接回地面原有道路）的菱形按主线 R 600 弯曲后，
            // 凸包把弯道内侧离新路 100 多米的区域也包进去（那里的街道会被拆）；走廊只到新路两侧（半宽 + 余量）
            var template = new DiamondTemplate();
            var retrofit = new InterchangeParams();
            retrofit.Levels[0].Elevation = 10f;
            retrofit.Levels[1].Elevation = 0f;
            foreach (var leg in retrofit.Legs)
                leg.ExternalElevation = 0f;
            var p = InterchangeLayout.FitRampLength(template, retrofit);
            var warp = new CurveWarp(p.Legs[1].Angle, 1f / 600f, 0f);
            var segments = InterchangeLayout.Bend(template, p, warp, out _);
            var hull = new Footprint(segments.Select(sg => sg.Curve), 2f);
            var corridor = Footprint.Corridor(segments.Select(sg => (sg.Curve, 8f)), 2f);
            int hullFar = 0;
            for (float x = hull.MinX; x <= hull.MaxX; x += 10f)
                for (float z = hull.MinZ; z <= hull.MaxZ; z += 10f)
                {
                    float nearest = segments.Min(sg => Distance(sg.Curve, x, z));
                    if (hull.Contains(x, z) && nearest > 100f)
                        hullFar++;
                    if (corridor.Contains(x, z))
                        Assert.True(nearest <= 10.1f, $"({x:F0}, {z:F0}) is {nearest:F1} m from the new roads");
                }
            Assert.True(hullFar > 0, "the convex hull should reach far into the inside of the curve");
        }

        /// <summary>沿 X 轴、离地 height 米的一条新路（半宽 10 米），两端节点在 x = ±300。</summary>
        private static Footprint NewRoadAt(float height) =>
            Footprint.Corridor(new[] { (Bezier3.Line(new Vec3(-300, height, 0), new Vec3(300, height, 0)), 10f) }, 2f);

        [Theory]
        [InlineData(10f, 0f, false)]    // 高架下面穿过的地面道路：净空 10 米，保留
        [InlineData(10f, 5f, true)]     // 离桥面只有 5 米：压到
        [InlineData(0f, 0f, true)]      // 新路在地面：压到
        [InlineData(6f, 0f, true)]      // 新路是 6 米高的路堤（实心）：下面过不去
        [InlineData(-8f, 0f, true)]     // 新路在路堑里（开口）：地面道路过不去
        [InlineData(-8f, -25f, false)]  // 路堑下面深处的隧道：保留
        [InlineData(0f, 20f, false)]    // 原有高架从地面新路上方跨过：保留
        public void Corridor_CollidesOnlyWithoutClearance(float newHeight, float oldElevation, bool collides)
        {
            // 首轮核查第 3 条（游戏内）：其他原有道路只拆和新路同高度压到的，桥下净空够的保留
            Assert.Equal(collides, NewRoadAt(newHeight).Collides(new Vec3(0, 0, 0), oldElevation, 5f));
        }

        [Fact]
        public void Corridor_GroundRoadHitsPillarAtElevatedNode()
        {
            // 高架新路节点下的立柱（垂直于桥面、和桥面一样宽）：从节点下穿过的地面道路压到，节点之间的跨中保留
            var corridor = NewRoadAt(10f);
            Assert.True(corridor.Collides(new Vec3(300, 0, 0), 0f, 5f));
            Assert.True(corridor.Collides(new Vec3(300, 0, 8), 0f, 5f));
            Assert.False(corridor.Collides(new Vec3(150, 0, 0), 0f, 5f));
            // 地面道路沿 Z 方向从 x = 300 的节点下穿过：只在立柱处切开；从跨中穿过的不切
            var street = Bezier3.Line(new Vec3(300, 0, -100), new Vec3(300, 0, 100));
            Assert.Equal(2, Footprint.OutsideIntervals(t => corridor.Collides(street.Evaluate(t), 0f, 5f)).Count);
            var underSpan = Bezier3.Line(new Vec3(0, 0, -100), new Vec3(0, 0, 100));
            Assert.Equal(new[] { (0f, 1f) }, Footprint.OutsideIntervals(t => corridor.Collides(underSpan.Evaluate(t), 0f, 5f)));
        }
    }
}
