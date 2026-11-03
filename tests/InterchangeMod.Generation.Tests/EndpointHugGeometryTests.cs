using System;
using InterchangeMod.Generation.Endpoints;
using InterchangeMod.Generation.Geometry;
using InterchangeMod.Generation.Templates;
using Xunit;

namespace InterchangeMod.Generation.Tests
{
    public class EndpointHugGeometryTests
    {
        private const float Radius = 60f, Width = 12f, ConnectorRadius = 30f;

        private static RampArc BaseLoop() => new RampArc(new Vec3(0, 0, 60), -Vec3.UnitZ, Radius, 1.5f * MathF.PI);
        private static Vec3 OriginA => new Vec3(400, 17, 120);
        private static Vec3 OriginB => new Vec3(0, -9, 500);

        private static void Near(Vec3 expected, Vec3 actual, float tolerance = .002f) =>
            Assert.InRange(Vec3.Distance(expected, actual), 0, tolerance);

        [Fact]
        public void UsesActualIndependentOriginsForOuterConnections()
        {
            Assert.True(EndpointHugGeometry.TryCreate(BaseLoop(), OriginA, -Vec3.UnitX, OriginB, -Vec3.UnitZ,
                ConnectorRadius, Width, out var hug));
            Assert.NotNull(hug);
            // The offset connector-centre lines intersect a circle of radius 96;
            // their perpendicular offset from its centre is 30, hence sqrt(96^2-30^2).
            Assert.InRange(hug!.OnA, 248.807f, 248.809f);
            Assert.InRange(hug.OnB, 531.191f, 531.193f);
            Near(new Vec3(151.1921f, 0, 120), hug.ConnA.Start);
            Near(new Vec3(0, 0, -31.1921f), hug.ConnB.End);
            Assert.InRange(hug.LoopLength, 282.742f, 282.745f);
            Assert.Equal(1f, hug.AngleSign);
        }

        [Theory]
        [InlineData(0, false)]
        [InlineData(1, false)]
        [InlineData(2, false)]
        [InlineData(3, false)]
        [InlineData(0, true)]
        [InlineData(1, true)]
        [InlineData(2, true)]
        [InlineData(3, true)]
        public void PreservesTangentsAndOpposingSharedTrafficInEveryQuadrant(int quadrant, bool mirror)
        {
            Vec3 Transform(Vec3 p)
            {
                if (mirror) p.X = -p.X;
                return p.RotateY(quadrant * MathF.PI / 2f);
            }
            var loop = BaseLoop();
            loop = new RampArc(Transform(loop.From), Transform(loop.Heading), loop.Radius, mirror ? -loop.Angle : loop.Angle);
            var originA = Transform(OriginA); var dirA = Transform(-Vec3.UnitX);
            var originB = Transform(OriginB); var dirB = Transform(-Vec3.UnitZ);
            Assert.True(EndpointHugGeometry.TryCreate(loop, originA, dirA, originB, dirB, ConnectorRadius, Width, out var hug));
            Assert.NotNull(hug);
            var h = hug!;
            Assert.Equal(mirror ? -1f : 1f, h.AngleSign);
            Near(loop.From, h.LoopIn.Start);
            Near(loop.End, h.LoopOut.End);
            Near(Planar.Flat0(originA) + dirA * h.OnA, h.ConnA.Start);
            Near(Planar.Flat0(originB) + dirB * h.OnB, h.ConnB.End);
            Near(dirA, h.ConnA.HeadingAt(0));
            Near(dirB, h.ConnB.HeadingAt(h.ConnB.Length));
            Near(h.Shared.HeadingAt(0), h.ConnA.HeadingAt(h.ConnA.Length));
            Near(h.Shared.HeadingAt(h.Shared.Length), h.ConnB.HeadingAt(0));
            Near(-h.Shared.HeadingAt(h.Shared.Length), h.LoopIn.HeadingAt(h.LoopIn.Length));
            Near(-h.Shared.HeadingAt(0), h.LoopOut.HeadingAt(0));
            Near(h.SplitA, h.Shared.Start);
            Near(h.SplitB, h.Shared.End);
            Near(h.SplitA, (h.LoopOut.Start + h.ConnA.End) * .5f);
            Near(h.SplitB, (h.LoopIn.End + h.ConnB.Start) * .5f);
            Assert.InRange(Vec3.Distance(loop.Center, h.Shared.Start), 62.998f, 63.002f);
            Assert.InRange(Vec3.Distance(loop.Center, h.ConnA.End), 65.998f, 66.002f);
            Assert.Equal(0f, h.SplitA.Y);
            Assert.Equal(0f, h.SplitB.Y);
            Assert.InRange(h.SplitAlongLoopB, RampPath.MinSegment, h.SplitAlongLoopA - RampPath.MinSegment);
            Assert.InRange(h.SplitAlongLoopA, h.SplitAlongLoopB, h.LoopLength - RampPath.MinSegment);
            Assert.InRange(Math.Abs(h.ConnA.Arcs[0].Angle), 0, MathF.PI);
            Assert.InRange(Math.Abs(h.ConnB.Arcs[0].Angle), 0, MathF.PI);
        }

        [Fact]
        public void SolvesSkewedAxesAtLargeWorldCoordinatesWithoutRecenteringThem()
        {
            var local = new RampArc(new Vec3(-60, 0, 0), -Vec3.UnitZ, Radius, 255f * Directions.DegToRad);
            var shift = new Vec3(100000, 0, -150000);
            var loop = new RampArc(local.From + shift, local.Heading, local.Radius, local.Angle);
            var a = local.End - local.EndHeading * 337f + shift;
            var b = local.From - local.Heading * 521f + shift;
            Assert.True(EndpointHugGeometry.TryCreate(loop, a, local.EndHeading * 3f, b, local.Heading * 2f,
                ConnectorRadius, Width, out var hug));
            Assert.NotNull(hug);
            Near(loop.From, hug!.LoopIn.Start, .03f);
            Near(loop.End, hug.LoopOut.End, .03f);
            Near(a + local.EndHeading * hug.OnA, hug.ConnA.Start, .03f);
            Near(b + local.Heading * hug.OnB, hug.ConnB.End, .03f);
            Assert.InRange(Vec3.Distance(shift, hug.SplitA), 62.97f, 63.03f);
            Assert.InRange(Vec3.Distance(shift, hug.SplitB), 62.97f, 63.03f);
            Near(hug.Shared.HeadingAt(0), hug.ConnA.HeadingAt(hug.ConnA.Length), .003f);
            Near(hug.Shared.HeadingAt(hug.Shared.Length), hug.ConnB.HeadingAt(0), .003f);
        }

        [Theory]
        [InlineData(float.NaN, 12)]
        [InlineData(float.PositiveInfinity, 12)]
        [InlineData(0, 12)]
        [InlineData(-30, 12)]
        [InlineData(30, float.NaN)]
        [InlineData(30, float.PositiveInfinity)]
        [InlineData(30, 0)]
        [InlineData(30, -12)]
        public void RejectsInvalidWidthsAndRadii(float connectorRadius, float width)
        {
            Assert.False(EndpointHugGeometry.TryCreate(BaseLoop(), OriginA, -Vec3.UnitX, OriginB, -Vec3.UnitZ,
                connectorRadius, width, out var hug));
            Assert.Null(hug);
        }

        [Fact]
        public void RejectsMalformedInputsAndDegenerateArcPieces()
        {
            void Reject(RampArc loop, Vec3 a, Vec3 dirA, Vec3 b, Vec3 dirB)
            {
                Assert.False(EndpointHugGeometry.TryCreate(loop, a, dirA, b, dirB, ConnectorRadius, Width, out var hug));
                Assert.Null(hug);
            }
            var valid = BaseLoop();
            Reject(valid, new Vec3(float.NaN, 0, 120), -Vec3.UnitX, OriginB, -Vec3.UnitZ);
            Reject(valid, OriginA, Vec3.Zero, OriginB, -Vec3.UnitZ);
            Reject(valid, OriginA, -Vec3.UnitX, OriginB, new Vec3(0, float.PositiveInfinity, -1));
            Reject(new RampArc(valid.From, valid.Heading, 0, valid.Angle), OriginA, -Vec3.UnitX, OriginB, -Vec3.UnitZ);
            Reject(new RampArc(valid.From, valid.Heading, Radius, float.NaN), OriginA, -Vec3.UnitX, OriginB, -Vec3.UnitZ);
            Reject(new RampArc(valid.From, valid.Heading, Radius, 2 * MathF.PI), OriginA, -Vec3.UnitX, OriginB, -Vec3.UnitZ);
            // These axes place A before B along the loop rather than after B.
            Reject(valid, OriginB, -Vec3.UnitZ, OriginA, -Vec3.UnitX);
            // The touch interval consumes the middle of this short loop.
            var shortLoop = new RampArc(valid.From, valid.Heading, Radius, 2.5f);
            Reject(shortLoop, shortLoop.End, shortLoop.EndHeading, OriginB, -Vec3.UnitZ);
            // Reversing a connector would require a long way round (> pi).
            Reject(valid, OriginA, Vec3.UnitX, OriginB, -Vec3.UnitZ);
            // These connector-centre lines do not meet the exterior tangent circle.
            Reject(valid, new Vec3(0, 0, 1000), -Vec3.UnitX, OriginB, -Vec3.UnitZ);
            Reject(valid, new Vec3(float.MaxValue, 0, 120), -Vec3.UnitX,
                new Vec3(0, 0, float.MaxValue), -Vec3.UnitZ);
        }
    }
}
