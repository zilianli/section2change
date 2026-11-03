using System;
using System.Linq;
using InterchangeMod.Generation.Templates;
using InterchangeMod.Generation.Geometry;
using Xunit;

namespace InterchangeMod.Generation.Tests
{
    public class RetainedRoadOrientationTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void GeneratedStackAndTurbineArmsAcceptBothOldCurveOrientations(bool turbine)
        {
            var p = new InterchangeParams { RampWidth = 8 };
            p.SetRoad(0, 2, RoadForm.Dual, 12, 4, "Main");
            p.SetRoad(1, 3, RoadForm.Dual, 12, 4, "Cross");
            p.Levels[0].Elevation = 0; p.Levels[1].Elevation = 10;
            var template = new StackTemplate(turbine);
            Assert.Empty(template.ValidateParams(p));
            var segments = template.Generate(p);
            var safety = new RetainedRoadSafety(segments.Select(s => (s.Curve, s.Role == RoadRole.Ramp ? 4f : 6f)), 2);
            foreach (var end in InterchangeLayout.LegEnds(segments))
            {
                int owner = Enumerable.Range(0, segments.Count).First(i => segments[i].StartNode == end || segments[i].EndNode == end);
                bool atEnd = segments[owner].EndNode == end;
                var outward = Vec3.Normalize(segments[owner].Curve.Derivative(atEnd ? 1 : 0) * (atEnd ? 1 : -1));
                var old = Bezier3.Line(end, end + outward * 100);
                Assert.False(safety.Collides(old, 6, owner, end, outward), $"outgoing {segments[owner].Name}");
                Assert.False(safety.Collides(old.Reverse(), 6, owner, end, outward), $"incoming {segments[owner].Name}");
            }
        }

        [Theory]
        [InlineData(false, 0f)]
        [InlineData(true, 0f)]
        [InlineData(false, 20f)]
        [InlineData(true, 20f)]
        public void LegalIncomingAndOutgoingGeometryHaveSameCollisionResult(bool incoming, float degrees)
        {
            var end = new Vec3(0, 10, 0);
            var owner = Bezier3.Line(new Vec3(-100, 10, 0), end);
            var safety = new RetainedRoadSafety(new[] { (owner, 20f) }, 2f);
            float angle = degrees * (float)Math.PI / 180;
            var road = Bezier3.Line(end, end + new Vec3(100 * (float)Math.Cos(angle), 0, 100 * (float)Math.Sin(angle)));
            Assert.False(safety.Collides(incoming ? road.Reverse() : road, 20, 0, end, Vec3.UnitX));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ReverseParameterDoesNotHideAnotherRoadOrPillar(bool pillar)
        {
            var end = new Vec3(0, 10, 0);
            var owner = Bezier3.Line(new Vec3(-100, 10, 0), end);
            var crossing = Bezier3.Line(new Vec3(40, pillar ? 20 : 10, 0), new Vec3(40, pillar ? 20 : 10, 100));
            var safety = new RetainedRoadSafety(new[] { (owner, 5f), (crossing, 5f) }, 2f);
            var road = Bezier3.Line(end, new Vec3(100, 10, 0));
            Assert.True(safety.Collides(road, 5, 0, end, Vec3.UnitX));
            Assert.True(safety.Collides(road.Reverse(), 5, 0, end, Vec3.UnitX));
        }

        [Fact]
        public void ReversingReturningCurveDoesNotTurnItIntoALegalJoin()
        {
            var safety = new RetainedRoadSafety(new[] { (Bezier3.Line(new Vec3(-100, 0, 0), Vec3.Zero), 5f) }, 2f);
            var returning = new Bezier3(Vec3.Zero, new Vec3(150, 0, 0), new Vec3(-100, 0, 0), new Vec3(-80, 0, 0));
            Assert.True(safety.Collides(returning, 5, 0, Vec3.Zero, Vec3.UnitX));
            Assert.True(safety.Collides(returning.Reverse(), 5, 0, Vec3.Zero, Vec3.UnitX));
        }
    }
}
