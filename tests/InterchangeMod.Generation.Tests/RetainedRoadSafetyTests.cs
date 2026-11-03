using InterchangeMod.Generation.Geometry;
using InterchangeMod.Generation.Templates;
using System.Linq;
using Xunit;

namespace InterchangeMod.Generation.Tests
{
    public class RetainedRoadSafetyTests
    {
        private static Bezier3 Line(float x0, float y0, float z0, float x1, float y1, float z1) =>
            Bezier3.Line(new Vec3(x0, y0, z0), new Vec3(x1, y1, z1));

        [Fact]
        public void AttachedRemainderMustNotCrossAnotherNewRoad()
        {
            var safety = new RetainedRoadSafety(new[] { (Line(-100, 0, 0, 0, 0, 0), 5f), (Line(40, 0, -50, 40, 0, 50), 5f) }, 2f);
            Assert.True(safety.Collides(Line(0, 0, 0, 100, 0, 0), 5f, 0, Vec3.Zero, Vec3.UnitX));
        }

        [Fact]
        public void AttachedRemainderMustNotPassUnderAnotherRoadPillar()
        {
            var safety = new RetainedRoadSafety(new[] { (Line(-100, 0, 0, 0, 0, 0), 5f), (Line(40, 10, 0, 40, 10, 100), 5f) }, 2f);
            Assert.True(safety.Collides(Line(0, 0, 0, 100, 0, 0), 5f, 0, Vec3.Zero, Vec3.UnitX));
        }

        [Fact]
        public void OrdinarySharedEndpointIsAllowed()
        {
            var safety = new RetainedRoadSafety(new[] { (Line(-100, 0, 0, 0, 0, 0), 8f) }, 2f);
            Assert.False(safety.Collides(Line(0, 0, 0, 100, 0, 0), 8f, 0, Vec3.Zero, Vec3.UnitX));
        }

        [Fact]
        public void WideCollinearTailPredecessorDoesNotBlockSharedEndpoint()
        {
            var safety = new RetainedRoadSafety(new[] { (Line(-80, 10, 0, -40, 10, 0), 20f), (Line(-40, 10, 0, 0, 10, 0), 20f) }, 2f);
            Assert.False(safety.Collides(Line(0, 10, 0, 100, 10, 0), 20f, 1, new Vec3(0, 10, 0), Vec3.UnitX));
        }

        [Fact]
        public void CompatibleTwentyDegreeConnectionOfWideRoadsIsAllowed()
        {
            var safety = new RetainedRoadSafety(new[] { (Line(-100, 10, 0, 0, 10, 0), 20f) }, 2f);
            Assert.False(safety.Collides(Line(0, 10, 0, 93.96926f, 10, 34.20201f), 20f, 0, new Vec3(0, 10, 0), Vec3.UnitX));
        }

        [Theory]
        [InlineData("width")]
        [InlineData("gap")]
        [InlineData("grade")]
        [InlineData("angle")]
        [InlineData("branch")]
        public void NearbyPredecessorMustBeAnUnambiguousExactContinuation(string mismatch)
        {
            var predecessor = mismatch switch
            {
                "gap" => Line(-80, 10, 0, -40.001f, 10, 0),
                "grade" => Line(-80, 0, 0, -40, 10, 0),
                "angle" => Line(-80, 10, -5, -40, 10, 0),
                _ => Line(-80, 10, 0, -40, 10, 0),
            };
            var roads = new System.Collections.Generic.List<(Bezier3, float)>
                { (predecessor, mismatch == "width" ? 21f : 20f), (Line(-40, 10, 0, 0, 10, 0), 20f) };
            if (mismatch == "branch") roads.Add((Line(-40, 10, 0, -40, 10, 40), 8f));
            var safety = new RetainedRoadSafety(roads, 2f);
            Assert.True(safety.Collides(Line(0, 10, 0, 100, 10, 0), 20f, 1, new Vec3(0, 10, 0), Vec3.UnitX));
        }

        [Fact]
        public void ContinuousWideTailAtSameLongitudinalGradeIsAllowed()
        {
            var safety = new RetainedRoadSafety(new[] { (Line(-80, 2, 0, -40, 6, 0), 20f), (Line(-40, 6, 0, 0, 10, 0), 20f) }, 2f);
            Assert.False(safety.Collides(Line(0, 10, 0, 100, 20, 0), 20f, 1, new Vec3(0, 10, 0), new Vec3(1, 0.1f, 0)));
        }

        [Fact]
        public void IncompatibleThirtyFiveDegreeJoinIsBlocked()
        {
            var safety = new RetainedRoadSafety(new[] { (Line(-100, 10, 0, 0, 10, 0), 20f) }, 2f);
            Assert.True(safety.Collides(Line(0, 10, 0, 81.9152f, 10, 57.3576f), 20f, 0, new Vec3(0, 10, 0), Vec3.UnitX));
        }

        [Fact]
        public void SharedElevatedEndpointDoesNotHitItsOwnPillar()
        {
            var safety = new RetainedRoadSafety(new[] { (Line(-100, 10, 0, 0, 10, 0), 8f) }, 2f);
            Assert.False(safety.Collides(Line(0, 10, 0, 100, 10, 0), 8f, 0, new Vec3(0, 10, 0), Vec3.UnitX));
        }

        [Fact]
        public void WholeKeptEdgeBeyondEndpointStillChecksOtherRoads()
        {
            var safety = new RetainedRoadSafety(new[] { (Line(-100, 0, 0, 0, 0, 0), 5f), (Line(65, 0, -40, 65, 0, 40), 5f) }, 2f);
            Assert.True(safety.Collides(Line(30, 0, 0, 100, 0, 0), 5f, 0, Vec3.Zero, Vec3.UnitX));
        }

        [Fact]
        public void OwnSegmentIsNotWhollyExemptIfOldRoadReturnsInward()
        {
            var safety = new RetainedRoadSafety(new[] { (Line(-100, 0, 0, 0, 0, 0), 5f) }, 2f);
            var returning = new Bezier3(Vec3.Zero, new Vec3(150, 0, 0), new Vec3(-100, 0, 0), new Vec3(-80, 0, 0));
            Assert.True(safety.Collides(returning, 5f, 0, Vec3.Zero, Vec3.UnitX));
        }

        [Fact]
        public void ClearUnderpassAwayFromPillarsIsAllowed()
        {
            var safety = new RetainedRoadSafety(new[] { (Line(-100, 0, 0, 0, 0, 0), 5f), (Line(50, 10, -100, 50, 10, 100), 5f) }, 2f);
            Assert.False(safety.Collides(Line(0, 0, 0, 100, 0, 0), 5f, 0, Vec3.Zero, Vec3.UnitX));
        }

        [Fact]
        public void RoadAlongOwnNewSegmentAwayFromJoinIsNotExempt()
        {
            var safety = new RetainedRoadSafety(new[] { (Line(-100, 0, 0, 0, 0, 0), 5f) }, 2f);
            Assert.True(safety.Collides(Line(-90, 0, 0, -40, 0, 0), 5f, 0, Vec3.Zero, Vec3.UnitX));
        }

        [Fact]
        public void NoMatchedRoadMeansNoRequiredConnection()
        {
            Assert.Equal(ConnectionNeed.None, RetainedRoadSafety.ConnectionAt(Vec3.Zero, Vec3.UnitX, 0, 5f, System.Array.Empty<MatchedConnectionRoad>()));
        }

        [Theory]
        [InlineData(0, 0, 0, ConnectionNeed.Required)]
        [InlineData(6, 0, 0, ConnectionNeed.Required)] // height mismatch must be surfaced
        [InlineData(0, 1, 0, ConnectionNeed.Manual)] // one-way to two-way
        [InlineData(0, 1, -1, ConnectionNeed.Manual)] // opposite one-way flow
        public void OutwardMatchedRoadDeclaresAutomaticOrManualNeed(float height, int flow, int oldFlow, ConnectionNeed expected)
        {
            Assert.Equal(expected, RetainedRoadSafety.ConnectionAt(Vec3.Zero, Vec3.UnitX, flow, 5f,
                new[] { new MatchedConnectionRoad(Line(-30, height, 0, 100, height, 0), 5f, oldFlow) }));
        }

        [Fact]
        public void RoadEndingAtLegDoesNotInventAnOutwardConnection()
        {
            Assert.Equal(ConnectionNeed.None, RetainedRoadSafety.ConnectionAt(Vec3.Zero, Vec3.UnitX, 0, 5f,
                new[] { new MatchedConnectionRoad(Line(-100, 0, 0, 0, 0, 0), 5f, 0) }));
        }

        [Fact]
        public void CurvedRoadMayContinueOutwardBeforeTurningBack()
        {
            var curve = new Bezier3(Vec3.Zero, new Vec3(100, 0, 0), new Vec3(50, 0, 100), new Vec3(-50, 0, 100));
            Assert.Equal(ConnectionNeed.Required, RetainedRoadSafety.ConnectionAt(Vec3.Zero, Vec3.UnitX, 0, 5f,
                new[] { new MatchedConnectionRoad(curve, 5f, 0) }));
        }

        [Fact]
        public void PerpendicularOrDistantMatchedRoadDoesNotRequireConnection()
        {
            Assert.Equal(ConnectionNeed.None, RetainedRoadSafety.ConnectionAt(Vec3.Zero, Vec3.UnitX, 0, 5f,
                new[] { new MatchedConnectionRoad(Line(0, 0, -100, 0, 0, 100), 5f, 0),
                    new MatchedConnectionRoad(Line(30, 0, 0, 100, 0, 0), 5f, 0) }));
        }

        [Fact]
        public void OffsetMixedCarriagewayNeedsManualConnectionWarning()
        {
            Assert.Equal(ConnectionNeed.Manual, RetainedRoadSafety.ConnectionAt(Vec3.Zero, Vec3.UnitX, 0, 8f,
                new[] { new MatchedConnectionRoad(Line(-30, 0, 12, 100, 0, 12), 5f, 1) }));
        }

        [Fact]
        public void GeneratedDiamondExternalStraightContinuationsAreAllowed()
        {
            var template = new DiamondTemplate();
            var p = InterchangeLayout.FitRampLength(template, new InterchangeParams());
            var segments = template.Generate(p);
            var safety = new RetainedRoadSafety(segments.Select(s => (s.Curve, 8f)), 2f);
            foreach (var end in InterchangeLayout.LegEnds(segments))
            {
                int owner = Enumerable.Range(0, segments.Count).First(i => segments[i].StartNode == end || segments[i].EndNode == end);
                var curve = segments[owner].Curve;
                Vec3 outward = Vec3.Normalize(curve.Derivative(segments[owner].EndNode == end ? 1f : 0f)
                    * (segments[owner].EndNode == end ? 1f : -1f));
                Assert.False(safety.Collides(Bezier3.Line(end, end + outward * 100f), 8f, owner, end, outward), $"leg {end}");
            }
        }
    }
}
