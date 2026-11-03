using System;
using InterchangeMod.Generation.Endpoints;
using InterchangeMod.Generation.Geometry;
using Xunit;

namespace InterchangeMod.Generation.Tests
{
    public class VerticalProfileTests
    {
        private static Bezier3 Line(float start, float end) => Bezier3.Line(new Vec3(start, 0, 0), new Vec3(end, 0, 0));
        private static float Grade(Bezier3 c, float t)
        {
            var d = c.Derivative(t);
            return d.Y / (float)Math.Sqrt(d.X * d.X + d.Z * d.Z);
        }

        [Fact]
        public void A50MeterSmoothRiseOf10MetersReachesButDoesNotExceed30Percent()
        {
            var result = VerticalProfile.Apply(new[] { Line(0, 50) }, 0, 10, 0, 0, 0.30f);
            Assert.Equal(VerticalProfileFailure.None, result.Failure);
            var curve = Assert.Single(result.Curves);
            Assert.Equal(0f, Grade(curve, 0), 5);
            Assert.Equal(0.30f, Grade(curve, 0.5f), 5);
            Assert.Equal(0f, Grade(curve, 1), 5);
        }

        [Fact]
        public void RejectsTooSteepAnIntervalAndOutOfRangeGradeLimit()
        {
            var tooShort = VerticalProfile.Apply(new[] { Line(0, 40) }, 0, 10, 0, 0, 0.30f);
            Assert.Equal(VerticalProfileFailure.GradeExceeded, tooShort.Failure);
            Assert.Empty(tooShort.Curves);
            Assert.Equal(VerticalProfileFailure.InvalidInput,
                VerticalProfile.Apply(new[] { Line(0, 50) }, 0, 10, 0, 0, 0.577f).Failure);
        }

        [Fact]
        public void SplitRiseHasContinuousHeightAndGradeAtTheJoin()
        {
            var result = VerticalProfile.Apply(new[] { Line(0, 25), Line(25, 50) }, 0, 10, 0, 0, 0.30f);
            Assert.Equal(VerticalProfileFailure.None, result.Failure);
            Assert.Equal(5f, result.Curves[0].P3.Y, 4);
            Assert.Equal(result.Curves[0].P3, result.Curves[1].P0);
            Assert.Equal(0.30f, Grade(result.Curves[0], 1), 5);
            Assert.Equal(Grade(result.Curves[0], 1), Grade(result.Curves[1], 0), 5);
        }

        [Fact]
        public void JoiningSegmentsWithDifferentTangentMagnitudesKeepsPhysicalSlopeContinuous()
        {
            var second = new Bezier3(new Vec3(25, 0, 0), new Vec3(35, 0, 0), new Vec3(65, 0, 0), new Vec3(75, 0, 0));
            var result = VerticalProfile.Apply(new[] { Line(0, 25), second }, 0, 10, 0, 0, 0.30f);
            Assert.Equal(VerticalProfileFailure.None, result.Failure);
            Assert.Equal(result.Curves[0].P3, result.Curves[1].P0);
            Assert.Equal(Grade(result.Curves[0], 1), Grade(result.Curves[1], 0), 5);
        }

        [Fact]
        public void RespectsDescendingAndNonzeroEndpointGrades()
        {
            var descending = VerticalProfile.Apply(new[] { Line(0, 100) }, 10, 0, 0, 0, 0.30f);
            Assert.Equal(VerticalProfileFailure.None, descending.Failure);
            Assert.Equal(-0.15f, Grade(descending.Curves[0], 0.5f), 5);
            var sloped = VerticalProfile.Apply(new[] { Line(0, 100) }, 0, 10, 0.05f, 0.05f, 0.30f);
            Assert.Equal(VerticalProfileFailure.None, sloped.Failure);
            Assert.Equal(0.05f, Grade(sloped.Curves[0], 0), 5);
            Assert.Equal(0.05f, Grade(sloped.Curves[0], 1), 5);
        }

        [Fact]
        public void RejectsZeroHorizontalTangentAndDisconnectedPlanarPieces()
        {
            var degenerate = new Bezier3(Vec3.Zero, Vec3.Zero, Vec3.UnitX, Vec3.UnitX * 2);
            Assert.Equal(VerticalProfileFailure.InvalidGeometry,
                VerticalProfile.Apply(new[] { degenerate }, 0, 1, 0, 0, 0.30f).Failure);
            Assert.Equal(VerticalProfileFailure.InvalidGeometry,
                VerticalProfile.Apply(new[] { Line(0, 20), Line(21, 40) }, 0, 1, 0, 0, 0.30f).Failure);
            Assert.Equal(VerticalProfileFailure.InvalidGeometry,
                VerticalProfile.Apply(new[] { Line(0, 20), Line(20, 0) }, 0, 1, 0, 0, 0.30f).Failure);
        }

        [Fact]
        public void RejectsAnInteriorHorizontalCuspEvenIfAverageSlopeLooksAcceptable()
        {
            // Its x derivative is 100*(1-2t)^2, so it vanishes at t=0.5.
            var cusp = new Bezier3(Vec3.Zero, new Vec3(100f / 3f, 0, 0), Vec3.Zero, new Vec3(100f / 3f, 0, 0));
            var result = VerticalProfile.Apply(new[] { cusp }, 0, 1, 0, 0, 0.30f);
            Assert.NotEqual(VerticalProfileFailure.None, result.Failure);
            Assert.Empty(result.Curves);
        }

        [Fact]
        public void DetectsNarrowInteriorSlopeSpikeBetweenOrdinarySamplingPoints()
        {
            // The horizontal derivative is approximately 10000*(t-0.27)^2+0.05.
            // Its endpoint-to-endpoint rise is only 0.02 m, but the true local
            // grade near t=0.27 is well above 30%; a 32-step sample can miss it.
            var narrow = new Bezier3(Vec3.Zero, new Vec3(729.05f / 3f, 0, 0),
                new Vec3(-1241.90f / 3f, 0, 0), new Vec3(4087.15f / 3f, 0, 0));
            var result = VerticalProfile.Apply(new[] { narrow }, 0, 0.02f, 0, 0, 0.30f);
            Assert.Equal(VerticalProfileFailure.GradeExceeded, result.Failure);
            Assert.Empty(result.Curves);
        }

        [Fact]
        public void FlatEndsKeepHeightAndSlopeContinuousAroundTheClimb()
        {
            var result = VerticalProfile.ApplyWithFlatEnds(new[] { Line(0, 100) }, 0, 10, 20, 20, 0.30f);
            Assert.Equal(VerticalProfileFailure.None, result.Failure);
            Assert.Equal(3, result.Curves.Count);
            Assert.InRange(result.Curves[0].P3.X, 19.99f, 20.01f);
            Assert.InRange(result.Curves[1].P3.X, 79.99f, 80.01f);
            Assert.Equal(0f, result.Curves[0].P3.Y, 5);
            Assert.Equal(10f, result.Curves[2].P0.Y, 5);
            Assert.Equal(0f, Grade(result.Curves[1], 0), 5);
            Assert.Equal(0f, Grade(result.Curves[1], 1), 5);
            Assert.InRange(Grade(result.Curves[1], 0.5f), 0.249f, 0.251f);
        }

        [Fact]
        public void FlatEndsRejectTooShortOrInvalidRequests()
        {
            Assert.Equal(VerticalProfileFailure.GradeExceeded,
                VerticalProfile.ApplyWithFlatEnds(new[] { Line(0, 80) }, 0, 10, 30, 30, 0.30f).Failure);
            Assert.Equal(VerticalProfileFailure.InsufficientLength,
                VerticalProfile.ApplyWithFlatEnds(new[] { Line(0, 100) }, 0, 10, 60, 60, 0.30f).Failure);
            Assert.Equal(VerticalProfileFailure.InvalidInput,
                VerticalProfile.ApplyWithFlatEnds(new[] { Line(0, 100) }, 0, 10, -1, 0, 0.30f).Failure);
            Assert.Equal(VerticalProfileFailure.InvalidInput,
                VerticalProfile.ApplyWithFlatEnds(new[] { Line(0, 100) }, 0, 10, float.NaN, 0, 0.30f).Failure);
        }

        [Fact]
        public void FlatEndsAcrossExistingSegmentsKeepEveryJoinContinuous()
        {
            var source = new[] { Line(0, 25), Line(25, 75), Line(75, 100) };
            var result = VerticalProfile.ApplyWithFlatEnds(source, 0, 10, 25, 25, 0.30f);
            Assert.Equal(VerticalProfileFailure.None, result.Failure);
            Assert.Equal(3, result.Curves.Count);
            for (int i = 1; i < result.Curves.Count; i++)
            {
                Assert.Equal(result.Curves[i - 1].P3, result.Curves[i].P0);
                Assert.Equal(Grade(result.Curves[i - 1], 1), Grade(result.Curves[i], 0), 4);
            }
            Assert.Equal(0f, source[1].P0.Y);
            Assert.Equal(0f, source[1].P3.Y);
            Assert.Throws<NotSupportedException>(() => ((System.Collections.Generic.ICollection<Bezier3>)result.Curves).Clear());
        }

        [Fact]
        public void LeadingFlatJoinsAClimbWithANonzeroSplitGrade()
        {
            var result = VerticalProfile.ApplyWithBoundaryGrades(new[] { Line(0, 120) },
                0, 10, 20, 0, 0, .08f, .30f);
            Assert.Equal(VerticalProfileFailure.None, result.Failure);
            Assert.Equal(2, result.Curves.Count);
            var flat = result.Curves[0]; var climb = result.Curves[1];
            Assert.Equal(0f, flat.P0.Y); Assert.Equal(0f, flat.P1.Y);
            Assert.Equal(0f, flat.P2.Y); Assert.Equal(0f, flat.P3.Y);
            Assert.InRange(flat.P3.X, 19.99f, 20.01f);
            Assert.Equal(flat.P3, climb.P0);
            Assert.Equal(0f, Grade(climb, 0), 5);
            Assert.Equal(.08f, Grade(climb, 1), 5);
            Assert.Equal(10f, climb.P3.Y);
        }

        [Fact]
        public void TrailingFlatFollowsADescentWithANonzeroSplitGrade()
        {
            var result = VerticalProfile.ApplyWithBoundaryGrades(new[] { Line(0, 120) },
                10, 0, 0, 20, -.08f, 0, .30f);
            Assert.Equal(VerticalProfileFailure.None, result.Failure);
            Assert.Equal(2, result.Curves.Count);
            var descent = result.Curves[0]; var flat = result.Curves[1];
            Assert.Equal(-.08f, Grade(descent, 0), 5);
            Assert.Equal(0f, Grade(descent, 1), 5);
            Assert.Equal(descent.P3, flat.P0);
            Assert.InRange(flat.P0.X, 99.99f, 100.01f);
            Assert.Equal(0f, flat.P0.Y); Assert.Equal(0f, flat.P1.Y);
            Assert.Equal(0f, flat.P2.Y); Assert.Equal(0f, flat.P3.Y);
        }

        [Fact]
        public void UnflattenedBoundaryGradesRemainContinuousAcrossUnequalCurveTangents()
        {
            var second = new Bezier3(new Vec3(25, 0, 0), new Vec3(35, 0, 0),
                new Vec3(65, 0, 0), new Vec3(75, 0, 0));
            var result = VerticalProfile.ApplyWithBoundaryGrades(new[] { Line(0, 25), second },
                0, 10, 0, 0, .05f, .08f, .30f);
            Assert.Equal(VerticalProfileFailure.None, result.Failure);
            Assert.Equal(2, result.Curves.Count);
            Assert.Equal(0f, result.Curves[0].P0.Y);
            Assert.Equal(10f, result.Curves[1].P3.Y);
            Assert.Equal(.05f, Grade(result.Curves[0], 0), 5);
            Assert.Equal(.08f, Grade(result.Curves[1], 1), 5);
            Assert.Equal(result.Curves[0].P3, result.Curves[1].P0);
            Assert.Equal(Grade(result.Curves[0], 1), Grade(result.Curves[1], 0), 5);
            Assert.All(result.Curves, c => Assert.InRange(c.MaxGrade(256), 0, .30001f));
        }

        [Theory]
        [InlineData(20, 0, .01f, 0)]
        [InlineData(0, 20, 0, -.01f)]
        [InlineData(20, 20, .00000001f, 0)]
        [InlineData(20, 20, 0, -.00000001f)]
        public void RejectsANonzeroGradeAtAnExplicitFlatEnd(float flatStart, float flatEnd,
            float startGrade, float endGrade)
        {
            var result = VerticalProfile.ApplyWithBoundaryGrades(new[] { Line(0, 120) },
                0, 10, flatStart, flatEnd, startGrade, endGrade, .30f);
            Assert.Equal(VerticalProfileFailure.InvalidInput, result.Failure);
            Assert.Empty(result.Curves);
        }

        [Theory]
        [InlineData(float.NaN, 0)]
        [InlineData(0, float.NaN)]
        [InlineData(float.PositiveInfinity, 0)]
        [InlineData(0, float.NegativeInfinity)]
        public void RejectsNonfiniteBoundaryGrades(float startGrade, float endGrade)
        {
            var result = VerticalProfile.ApplyWithBoundaryGrades(new[] { Line(0, 120) },
                0, 10, 0, 0, startGrade, endGrade, .30f);
            Assert.Equal(VerticalProfileFailure.InvalidInput, result.Failure);
            Assert.Empty(result.Curves);
        }

        [Fact]
        public void BoundaryGradeInterfaceStillCertifiesTheWholeClimb()
        {
            var tooSteep = VerticalProfile.ApplyWithBoundaryGrades(new[] { Line(0, 30) },
                0, 10, 10, 0, 0, .08f, .30f);
            Assert.Equal(VerticalProfileFailure.GradeExceeded, tooSteep.Failure);
            Assert.Empty(tooSteep.Curves);
            var invalidJoin = VerticalProfile.ApplyWithBoundaryGrades(new[] { Line(0, 25), Line(26, 100) },
                0, 10, 20, 0, 0, .08f, .30f);
            Assert.Equal(VerticalProfileFailure.InvalidGeometry, invalidJoin.Failure);
            Assert.Empty(invalidJoin.Curves);
        }

        [Fact]
        public void AFlatCoveringTheWholePathCannotMeetANonzeroOppositeBoundaryGrade()
        {
            var result = VerticalProfile.ApplyWithBoundaryGrades(new[] { Line(0, 100) },
                10, 10, 0, 100, .05f, 0, .30f);
            Assert.Equal(VerticalProfileFailure.InsufficientLength, result.Failure);
            Assert.Empty(result.Curves);
        }
    }
}
