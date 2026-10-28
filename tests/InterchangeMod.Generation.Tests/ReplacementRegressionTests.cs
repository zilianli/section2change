using System.Linq;
using InterchangeMod.Generation.Geometry;
using Xunit;

namespace InterchangeMod.Generation.Tests
{
    public class ReplacementRegressionTests
    {
        [Theory]
        [InlineData(12f, true)]
        [InlineData(12.01f, false)]
        public void ConvertedRoadParallelToHullBoundary_IsClassifiedAsAWhole(float z, bool inside)
        {
            var hull = new Footprint(new[] {
                Bezier3.Line(new Vec3(0, 0, -10), new Vec3(2000, 0, 10)),
                Bezier3.Line(new Vec3(0, 0, 10), new Vec3(2000, 0, -10))
            }, 2f);
            var road = Bezier3.Line(new Vec3(100, 0, z), new Vec3(1900, 0, z));
            var watch = System.Diagnostics.Stopwatch.StartNew();
            Assert.Equal(inside ? 0 : 1, hull.OutsideIntervals(road).Count);
            Assert.True(watch.Elapsed < System.TimeSpan.FromSeconds(1));
        }

        [Fact]
        public void LongParallelRoadOnCorridorBoundary_DoesNotRequireCentimetreScan()
        {
            var corridor = Footprint.Corridor(Enumerable.Range(0, 32).Select(i =>
                (Bezier3.Line(new Vec3(0, 0, i * 100), new Vec3(2000, 0, i * 100)), 10f)), 2f);
            var road = Bezier3.Line(new Vec3(100, 0, 17), new Vec3(1900, 0, 17));
            var watch = System.Diagnostics.Stopwatch.StartNew();
            Assert.Empty(corridor.OutsideCollisionIntervals(road, 5f));
            // Generous ceiling: the old implementation takes tens of seconds here.
            Assert.True(watch.Elapsed < System.TimeSpan.FromSeconds(2), $"Boundary collision took {watch.Elapsed}");
        }

        [Fact]
        public void TangentialContactBetweenSamples_IsConservativelyDetected()
        {
            var corridor = Footprint.Corridor(new[] {
                (Bezier3.Line(new Vec3(8, 0, 0), new Vec3(8, 0, 0)), 2f)
            }, 1f);
            var road = Bezier3.Line(new Vec3(0, 0, 5), new Vec3(1024, 0, 5));
            Assert.True(corridor.Intersects(road, extra: 2f));
            Assert.True(corridor.OutsideCollisionIntervals(road, 2f).Count >= 2);
        }

        [Fact]
        public void NonUniformBezierParameter_DoesNotSkipNarrowCrossing()
        {
            var corridor = Footprint.Corridor(new[] {
                (Bezier3.Line(new Vec3(8, 0, -100), new Vec3(8, 0, 100)), 2f)
            }, 1f);
            var road = new Bezier3(Vec3.Zero, Vec3.Zero, Vec3.Zero, new Vec3(4096, 0, 0));
            var outside = corridor.OutsideCollisionIntervals(road, 2f);
            Assert.Equal(2, outside.Count);
            Assert.InRange(road.Evaluate(outside[0].End).X, 2.99f, 3.001f);
            Assert.InRange(road.Evaluate(outside[1].Start).X, 12.999f, 13.01f);
        }

        [Fact]
        public void ConvertedRoad_LevelOverlapInsideNarrowHull_IsDetected()
        {
            var footprint = new Footprint(new[] {
                Bezier3.Line(new Vec3(8, 0, -10), new Vec3(8, 0, 10))
            }, 2f);
            var road = Bezier3.Line(new Vec3(0, 10, 0), new Vec3(1024, 10, 0));
            Assert.True(footprint.IntersectsAtLevels(road, new[] { 10f }, 4.5f));
            Assert.False(footprint.IntersectsAtLevels(road, new[] { 0f }, 4.5f));
        }

        [Theory]
        [InlineData(1024f, 8f)]
        [InlineData(4096f, 24f)]
        public void LongRoad_NarrowCrossingBetweenOldSamples_IsCut(float length, float crossing)
        {
            var road = Bezier3.Line(new Vec3(0, 0, 0), new Vec3(length, 0, 0));
            var corridor = Footprint.Corridor(new[] {
                (Bezier3.Line(new Vec3(crossing, 0, -100), new Vec3(crossing, 0, 100)), 2f)
            }, 1f);
            var outside = corridor.OutsideIntervals(road, extra: 2f);
            Assert.Equal(2, outside.Count);
            Assert.InRange(outside[0].End * length, crossing - 5.1f, crossing - 4.9f);
            Assert.InRange(outside[1].Start * length, crossing + 4.9f, crossing + 5.1f);
            Assert.True(corridor.Intersects(road, extra: 2f));
        }

        [Fact]
        public void LongRoad_TwoNarrowCrossings_PreservesTheMiddle()
        {
            var road = Bezier3.Line(new Vec3(0, 0, 0), new Vec3(1024, 0, 0));
            var corridor = Footprint.Corridor(new[] { 8f, 40f }.Select(x =>
                (Bezier3.Line(new Vec3(x, 0, -100), new Vec3(x, 0, 100)), 2f)), 1f);
            var outside = corridor.OutsideIntervals(road, extra: 2f);
            Assert.Equal(3, outside.Count);
            Assert.InRange(outside[1].Start * 1024, 12.9f, 13.1f);
            Assert.InRange(outside[1].End * 1024, 34.9f, 35.1f);
        }
    }
}
