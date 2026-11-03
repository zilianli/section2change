using InterchangeMod.Generation.Endpoints;
using InterchangeMod.Generation.Geometry;
using Xunit;

namespace InterchangeMod.Generation.Tests
{
    public class CurveClearanceTests
    {
        [Fact]
        public void DetectsAtGradeCrossingAndAllowsBridgeWithClearance()
        {
            var a = Bezier3.Line(new Vec3(-100, 0, 0), new Vec3(100, 0, 0));
            var crossing = Bezier3.Line(new Vec3(0, 0, -100), new Vec3(0, 0, 100));
            var bridge = Bezier3.Line(new Vec3(0, 8, -100), new Vec3(0, 8, 100));
            var parallel = Bezier3.Line(new Vec3(-100, 0, 5), new Vec3(100, 0, 5));
            Assert.Equal(CurveClearanceResult.Conflict, CurveClearance.Check(a, 1, crossing, 1, 7.5f));
            Assert.Equal(CurveClearanceResult.Clear, CurveClearance.Check(a, 1, bridge, 1, 7.5f));
            Assert.Equal(CurveClearanceResult.Clear, CurveClearance.Check(a, 1, parallel, 1, 7.5f));
        }

        [Fact]
        public void FindsCrossingAwayFromInitialSamplesAndReportsLimitedBudget()
        {
            var a = Bezier3.Line(new Vec3(-100, 0, 0), new Vec3(100, 0, 0));
            var b = Bezier3.Line(new Vec3(13.7f, 0, -100), new Vec3(13.7f, 0, 100));
            Assert.Equal(CurveClearanceResult.Conflict, CurveClearance.Check(a, 0.5f, b, 0.5f, 7.5f));
            Assert.Equal(CurveClearanceResult.Conflict, CurveClearance.Check(b, 0.5f, a, 0.5f, 7.5f));
            Assert.Equal(CurveClearanceResult.Uncertain, CurveClearance.Check(a, 0.5f, b, 0.5f, 7.5f, maxNodes: 1));
        }

        [Fact]
        public void RejectsInvalidGeometryAndLimits()
        {
            var valid = Bezier3.Line(Vec3.Zero, Vec3.UnitX * 20);
            var bad = Bezier3.Line(new Vec3(float.NaN, 0, 0), Vec3.UnitX);
            Assert.Equal(CurveClearanceResult.InvalidInput, CurveClearance.Check(bad, 1, valid, 1, 7.5f));
            Assert.Equal(CurveClearanceResult.InvalidInput, CurveClearance.Check(valid, 0, valid, 1, 7.5f));
            Assert.Equal(CurveClearanceResult.InvalidInput, CurveClearance.Check(valid, 1, valid, -1, 7.5f));
            Assert.Equal(CurveClearanceResult.InvalidInput, CurveClearance.Check(valid, 1, valid, 1, 0));
            Assert.Equal(CurveClearanceResult.InvalidInput, CurveClearance.Check(valid, 1, valid, 1, 7.5f, maxNodes: 0));
        }

        [Fact]
        public void UsesBothWidthsAndDoesNotCallBoundaryContactClear()
        {
            var a = Bezier3.Line(new Vec3(-20, 0, 0), new Vec3(20, 0, 0));
            var touching = Bezier3.Line(new Vec3(-20, 0, 4), new Vec3(20, 0, 4));
            var overlap = Bezier3.Line(new Vec3(-20, 0, 3.9f), new Vec3(20, 0, 3.9f));
            Assert.Equal(CurveClearanceResult.Uncertain, CurveClearance.Check(a, 1, touching, 3, 7.5f, maxNodes: 100));
            Assert.Equal(CurveClearanceResult.Conflict, CurveClearance.Check(a, 1, overlap, 3, 7.5f));
        }

        [Fact]
        public void SupportsLargeWorldCoordinatesAndRejectsInfiniteInputs()
        {
            var a = Bezier3.Line(new Vec3(100000, 10, 100000), new Vec3(100200, 10, 100000));
            var b = Bezier3.Line(new Vec3(100100, 18, 99900), new Vec3(100100, 18, 100100));
            Assert.Equal(CurveClearanceResult.Clear, CurveClearance.Check(a, 2, b, 3, 7.5f));
            Assert.Equal(CurveClearanceResult.InvalidInput, CurveClearance.Check(a, float.PositiveInfinity, b, 1, 7.5f));
            Assert.Equal(CurveClearanceResult.InvalidInput, CurveClearance.Check(a, 1, b, 1, float.NaN));
        }
    }
}
