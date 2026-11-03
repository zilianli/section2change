using System;
using System.Collections.Generic;
using InterchangeMod.Generation.Endpoints;
using InterchangeMod.Generation.Geometry;
using Xunit;

namespace InterchangeMod.Generation.Tests
{
    public class EndpointNetworkClearanceTests
    {
        [Fact]
        public void RejectsAtGradeCrossingBetweenIndependentRampPaths()
        {
            var candidate = Candidate(
                Road("Right", CloverEdgeKind.DirectRamp, new Vec3(-100, 0, 0), new Vec3(100, 0, 0)),
                Road("Left", CloverEdgeKind.LeftRamp, new Vec3(0, 0, -100), new Vec3(0, 0, 100)));

            Assert.Equal(CurveClearanceResult.Conflict, Check(candidate, out var detail));
            Assert.Contains("Right", detail);
            Assert.Contains("Left", detail);
        }

        [Theory]
        [InlineData(CloverEdgeKind.Main, CloverEdgeKind.Main)]
        [InlineData(CloverEdgeKind.Main, CloverEdgeKind.LeftRamp)]
        [InlineData(CloverEdgeKind.DirectRamp, CloverEdgeKind.LeftRamp)]
        public void AllowsCrossingWithSufficientVerticalClearance(CloverEdgeKind first, CloverEdgeKind second)
        {
            var candidate = Candidate(
                Road("Lower", first, new Vec3(-100, 0, 0), new Vec3(100, 0, 0)),
                Road("Upper", second, new Vec3(0, 8.6f, -100), new Vec3(0, 8.6f, 100)));

            Assert.Equal(CurveClearanceResult.Clear, Check(candidate, out var detail));
            Assert.Equal("", detail);
        }

        [Fact]
        public void RejectsLowCrossingEvenBetweenDistinctMainPaths()
        {
            var candidate = Candidate(
                Road("MainA", CloverEdgeKind.Main, new Vec3(-100, 0, 0), new Vec3(100, 0, 0)),
                Road("CrossA", CloverEdgeKind.Main, new Vec3(0, 8.4f, -100), new Vec3(0, 8.4f, 100)));

            Assert.Equal(CurveClearanceResult.Conflict, Check(candidate, out _));
        }

        [Fact]
        public void UsesEachRoadsExplicitWidth()
        {
            var main = Road("Main", CloverEdgeKind.Main, new Vec3(-20, 0, 0), new Vec3(20, 0, 0), 20);
            var overlap = Road("Left", CloverEdgeKind.LeftRamp, new Vec3(-20, 0, 13.9f), new Vec3(20, 0, 13.9f));
            var clear = Road("Left", CloverEdgeKind.LeftRamp, new Vec3(-20, 0, 14.1f), new Vec3(20, 0, 14.1f));

            Assert.Equal(CurveClearanceResult.Conflict, Check(Candidate(main, overlap), out _));
            Assert.Equal(CurveClearanceResult.Clear, Check(Candidate(main, clear), out _));
        }

        [Fact]
        public void PreservesUncertainBoundaryContactInsteadOfAcceptingIt()
        {
            var candidate = Candidate(
                Road("Main", CloverEdgeKind.Main, new Vec3(-20, 0, 0), new Vec3(20, 0, 0)),
                Road("Left", CloverEdgeKind.LeftRamp, new Vec3(-20, 0, 8), new Vec3(20, 0, 8)));

            Assert.Equal(CurveClearanceResult.Uncertain, Check(candidate, out var detail));
            Assert.Contains("Uncertain", detail);
        }

        [Fact]
        public void AllowsOnlyTerminalRampPieceToMergeAtIncidentMainNode()
        {
            var junction = Vec3.Zero;
            var candidate = Candidate(
                Road("MainA", CloverEdgeKind.Main, new Vec3(-100, 0, 0), junction),
                Road("MainA", CloverEdgeKind.Main, junction, new Vec3(100, 0, 0)),
                Road("LeftMainA", CloverEdgeKind.LeftRamp, junction, new Vec3(0, 0, 40)),
                Road("LeftMainA", CloverEdgeKind.LeftRamp, new Vec3(0, 0, 40), new Vec3(40, 0, 40)));

            Assert.Equal(CurveClearanceResult.Clear, Check(candidate, out _));
        }

        [Fact]
        public void ChecksLaterCrossingAfterLegalRampMerge()
        {
            var candidate = Candidate(
                Road("MainA", CloverEdgeKind.Main, new Vec3(-100, 0, 0), Vec3.Zero),
                Road("LeftMainA", CloverEdgeKind.LeftRamp, Vec3.Zero, new Vec3(0, 0, 40)),
                Road("LeftMainA", CloverEdgeKind.LeftRamp, new Vec3(0, 0, 40), new Vec3(-50, 0, -40)),
                Road("LeftMainA", CloverEdgeKind.LeftRamp, new Vec3(-50, 0, -40), new Vec3(-100, 0, -40)));

            Assert.Equal(CurveClearanceResult.Conflict, Check(candidate, out _));
        }

        [Fact]
        public void AllowsLastRampPieceToMergeWhenMainIsListedLater()
        {
            var candidate = Candidate(
                Road("LeftMainA", CloverEdgeKind.LeftRamp, new Vec3(-40, 0, 40), new Vec3(0, 0, 40)),
                Road("LeftMainA", CloverEdgeKind.LeftRamp, new Vec3(0, 0, 40), Vec3.Zero),
                Road("MainA", CloverEdgeKind.Main, new Vec3(-100, 0, 0), Vec3.Zero));

            Assert.Equal(CurveClearanceResult.Clear, Check(candidate, out _));
        }

        [Fact]
        public void DoesNotExemptMainThatOnlyPassesThroughMergeNode()
        {
            var candidate = Candidate(
                Road("MainA", CloverEdgeKind.Main, new Vec3(-100, 0, 0), new Vec3(100, 0, 0)),
                Road("LeftMainA", CloverEdgeKind.LeftRamp, Vec3.Zero, new Vec3(0, 0, 40)));

            Assert.Equal(CurveClearanceResult.Conflict, Check(candidate, out _));
        }

        [Fact]
        public void DoesNotExemptDifferentMainPathsSharingEndpoint()
        {
            var candidate = Candidate(
                Road("MainA", CloverEdgeKind.Main, new Vec3(-100, 0, 0), Vec3.Zero),
                Road("CrossA", CloverEdgeKind.Main, Vec3.Zero, new Vec3(0, 0, 100)));

            Assert.Equal(CurveClearanceResult.Conflict, Check(candidate, out _));
        }

        [Fact]
        public void AllowsTwoRampTerminalsSharingExactThreeDimensionalNode()
        {
            var candidate = Candidate(
                Road("LeftMainA", CloverEdgeKind.LeftRamp, new Vec3(-100, 0, 0), Vec3.Zero),
                Road("RightMainA", CloverEdgeKind.DirectRamp, Vec3.Zero, new Vec3(0, 0, 100)));

            Assert.Equal(CurveClearanceResult.Clear, Check(candidate, out _));
        }

        [Fact]
        public void DoesNotExemptRampInteriorNodeSharedWithOtherRampTerminal()
        {
            var candidate = Candidate(
                Road("Left", CloverEdgeKind.LeftRamp, new Vec3(-100, 0, 0), new Vec3(-50, 0, 0)),
                Road("Left", CloverEdgeKind.LeftRamp, new Vec3(-50, 0, 0), Vec3.Zero),
                Road("Left", CloverEdgeKind.LeftRamp, Vec3.Zero, new Vec3(50, 0, 0)),
                Road("Left", CloverEdgeKind.LeftRamp, new Vec3(50, 0, 0), new Vec3(100, 0, 0)),
                Road("Right", CloverEdgeKind.DirectRamp, Vec3.Zero, new Vec3(0, 0, 100)));

            Assert.Equal(CurveClearanceResult.Conflict, Check(candidate, out _));
        }

        [Theory]
        [InlineData(0.001f, 0f)]
        [InlineData(0f, 0.001f)]
        public void DoesNotTreatNearbyOrDifferentHeightNodesAsShared(float horizontalOffset, float heightOffset)
        {
            var candidate = Candidate(
                Road("MainA", CloverEdgeKind.Main, new Vec3(-100, 0, 0), Vec3.Zero),
                Road("LeftMainA", CloverEdgeKind.LeftRamp, new Vec3(horizontalOffset, heightOffset, 0), new Vec3(0, 0, 40)));

            Assert.Equal(CurveClearanceResult.Conflict, Check(candidate, out _));
        }

        [Fact]
        public void DoesNotSkipDifferentPathsWithSameSourceAndKind()
        {
            var candidate = Candidate(
                Road("LeftMainA", CloverEdgeKind.LeftRamp, new Vec3(-100, 0, 0), new Vec3(100, 0, 0)),
                Road("LeftMainB", CloverEdgeKind.LeftRamp, new Vec3(0, 0, -100), new Vec3(0, 0, 100)));

            Assert.Equal(CurveClearanceResult.Conflict, Check(candidate, out _));
        }

        [Fact]
        public void ValidatesAllSegmentsBeforeApplyingAnyPathExemption()
        {
            var valid = Road("MainA", CloverEdgeKind.Main, new Vec3(-100, 0, 0), Vec3.Zero);
            var invalid = Road("MainA", CloverEdgeKind.Main, Vec3.Zero, new Vec3(float.NaN, 0, 0));

            Assert.Equal(CurveClearanceResult.InvalidInput, Check(Candidate(valid, invalid), out _));
        }

        [Theory]
        [MemberData(nameof(InvalidCandidates))]
        public void ReturnsInvalidInputWithoutThrowingForMalformedCandidates(SpatialCandidateResult? candidate)
        {
            Assert.Equal(CurveClearanceResult.InvalidInput, Check(candidate!, out _));
        }

        public static IEnumerable<object?[]> InvalidCandidates()
        {
            var line = Bezier3.Line(Vec3.Zero, new Vec3(100, 0, 0));
            yield return new object?[] { null };
            yield return new object?[] { Candidate() };
            yield return new object?[] { new SpatialCandidateResult(SpatialCandidateFailure.InvalidGeometry,
                new[] { new SpatialSegment(0, CloverEdgeKind.Main, line, roadWidth: 8, pathId: "MainA") }) };
            yield return new object?[] { Candidate(new SpatialSegment[] { null! }) };
            foreach (var width in new float?[] { null, 0, -1, float.NaN, float.PositiveInfinity })
                yield return new object?[] { Candidate(new SpatialSegment(0, CloverEdgeKind.Main, line, roadWidth: width, pathId: "MainA")) };
            foreach (var pathId in new[] { "", " ", null })
                yield return new object?[] { Candidate(new SpatialSegment(0, CloverEdgeKind.Main, line, roadWidth: 8, pathId: pathId!)) };
            yield return new object?[] { Candidate(new SpatialSegment(0, CloverEdgeKind.Main,
                new Bezier3(Vec3.Zero, new Vec3(1, float.PositiveInfinity, 0), new Vec3(2, 0, 0), new Vec3(3, 0, 0)),
                roadWidth: 8, pathId: "MainA")) };
            yield return new object?[] { Candidate(new SpatialSegment(0, CloverEdgeKind.Main, line,
                startNode: new Vec3(float.NaN, 0, 0), roadWidth: 8, pathId: "MainA")) };
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(float.NaN)]
        [InlineData(float.PositiveInfinity)]
        public void ReturnsInvalidInputForInvalidClearance(float clearance)
        {
            var candidate = Candidate(Road("MainA", CloverEdgeKind.Main, Vec3.Zero, new Vec3(100, 0, 0)));

            Assert.Equal(CurveClearanceResult.InvalidInput, EndpointNetworkClearance.Check(candidate, clearance, out _));
        }

        private static SpatialSegment Road(string pathId, CloverEdgeKind kind, Vec3 start, Vec3 end, float width = 8) =>
            new SpatialSegment(0, kind, Bezier3.Line(start, end), roadWidth: width, pathId: pathId);

        private static SpatialCandidateResult Candidate(params SpatialSegment[] segments) =>
            new SpatialCandidateResult(SpatialCandidateFailure.None, segments);

        private static CurveClearanceResult Check(SpatialCandidateResult candidate, out string detail) =>
            EndpointNetworkClearance.Check(candidate, 8.5f, out detail);
    }
}
