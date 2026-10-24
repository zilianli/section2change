using System;
using InterchangeMod.Generation.Geometry;
using Xunit;

namespace InterchangeMod.Generation.Tests
{
    public class GeometryTests
    {
        [Fact]
        public void Line_EvaluatesEndpointsAndMidpoint()
        {
            var line = Bezier3.Line(Vec3.Zero, new Vec3(10, 0, 0));
            Assert.Equal(Vec3.Zero, line.Evaluate(0));
            Assert.Equal(new Vec3(10, 0, 0), line.Evaluate(1));
            Assert.Equal(5f, line.Evaluate(0.5f).X, 3);
            Assert.Equal(10f, line.Length(), 2);
        }

        [Fact]
        public void MaxGrade_OfUniformSlope_EqualsRiseOverRun()
        {
            var ramp = Bezier3.Line(Vec3.Zero, new Vec3(0, 8, 100));
            Assert.Equal(0.08f, ramp.MaxGrade(), 3);
        }

        [Fact]
        public void Arc_QuarterCircle_StaysOnRadius()
        {
            var segments = Arc.Create(Vec3.Zero, 50f, 0f, MathF.PI / 2f, 0f, 0f);
            Assert.Single(segments);
            for (int i = 0; i <= 10; i++)
            {
                var p = segments[0].Evaluate(i / 10f);
                float r = MathF.Sqrt(p.X * p.X + p.Z * p.Z);
                // 标准近似误差约 0.03%
                Assert.InRange(r, 49.9f, 50.1f);
            }
        }

        [Fact]
        public void Arc_FullLoop_SplitsAndConnects()
        {
            var segments = Arc.Create(Vec3.Zero, 60f, 0f, MathF.PI * 1.5f, 0f, 10f);
            Assert.Equal(3, segments.Count);
            for (int i = 1; i < segments.Count; i++)
                Assert.True(Vec3.Distance(segments[i - 1].P3, segments[i].P0) < 1e-3f);
            Assert.Equal(10f, segments[^1].P3.Y, 3);
        }

        [Theory]
        [InlineData(0f, 0f, 1f)]
        [InlineData(90f, 1f, 0f)]
        [InlineData(180f, 0f, -1f)]
        public void Directions_FromAngle_IsClockwiseFromNorth(float degrees, float x, float z)
        {
            var d = Directions.FromAngle(degrees);
            Assert.Equal(x, d.X, 3);
            Assert.Equal(z, d.Z, 3);
        }

        [Theory]
        [InlineData(10f, 350f, 20f)]
        [InlineData(0f, 180f, 180f)]
        [InlineData(90f, -90f, 180f)]
        public void Directions_AngleBetween_WrapsAround(float a, float b, float expected)
        {
            Assert.Equal(expected, Directions.AngleBetween(a, b), 3);
        }

        [Fact]
        public void Place_RotatesAroundYThenTranslates()
        {
            var line = Bezier3.Line(Vec3.Zero, new Vec3(0, 0, 10));
            // 与 Unity 一致：yaw = 90° 时 +Z 转到 +X
            var placed = line.Place(new Vec3(100, 5, 0), MathF.PI / 2f);
            Assert.Equal(110f, placed.P3.X, 3);
            Assert.Equal(5f, placed.P3.Y, 3);
            Assert.Equal(0f, placed.P3.Z, 3);
        }
    }
}
