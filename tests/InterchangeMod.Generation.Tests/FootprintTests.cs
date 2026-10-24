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
    }
}
