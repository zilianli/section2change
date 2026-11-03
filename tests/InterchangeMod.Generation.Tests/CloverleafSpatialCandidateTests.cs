using System;
using System.Collections.Generic;
using System.Linq;
using InterchangeMod.Generation.Endpoints;
using InterchangeMod.Generation.Geometry;
using Xunit;

namespace InterchangeMod.Generation.Tests
{
    public class CloverleafSpatialCandidateTests
    {
        private static bool Reach(IReadOnlyList<SpatialSegment> segments, Vec3 start, Vec3 end)
        {
            var queue = new Queue<(Vec3 Node, string? Ramp)>();
            var seen = new HashSet<(Vec3 Node, string? Ramp)>();
            queue.Enqueue((start, null));
            while (queue.Count > 0)
            {
                var state = queue.Dequeue();
                if (!seen.Add(state)) continue;
                if (state.Node.Equals(end)) return true;
                foreach (var segment in segments.Where(s => s.WorldCurve.P0.Equals(state.Node)))
                {
                    string? used = state.Ramp;
                    if (segment.Kind != CloverEdgeKind.Main)
                    {
                        string group = $"{segment.SourceArm}:{segment.Kind}";
                        if (used != null && used != group) continue;
                        used = group;
                    }
                    queue.Enqueue((segment.WorldCurve.P3, used));
                }
            }
            return false;
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void BuildsConnectedWorldHeightSkeletonForEveryMovement(bool rightHandTraffic)
        {
            var arms = EndpointFixtures.DualCross(rightHandTraffic: rightHandTraffic);
            var planar = PlanarCloverleafCandidate.Create(arms, rightHandTraffic, 30, 100);
            var spatial = SpatialCloverleafCandidate.Create(arms, planar, 10, 20, 30, 0.30f);
            Assert.Equal(SpatialCandidateFailure.None, spatial.Failure);
            Assert.False(spatial.IsPlacementReady);
            Assert.NotEmpty(spatial.Segments);
            foreach (var segment in spatial.Segments)
                Assert.True(segment.WorldCurve.MaxGrade(256) <= 0.30001f);

            for (int arm = 0; arm < 4; arm++)
            {
                var start = arms[arm].Endpoints.Single(p => !p.TravelOutward).Position;
                for (int destination = 0; destination < 4; destination++)
                {
                    var end = arms[destination].Endpoints.Single(p => p.TravelOutward).Position;
                    Assert.Equal(destination != arm, Reach(spatial.Segments, start, end));
                }
            }
            var missing = spatial.Segments.Where(s => !(s.Kind == CloverEdgeKind.LoopRamp && s.SourceArm == 0)).ToArray();
            var from = arms[0].Endpoints.Single(p => !p.TravelOutward).Position;
            var loopDestination = arms[rightHandTraffic ? 1 : 3].Endpoints.Single(p => p.TravelOutward).Position;
            Assert.False(Reach(missing, from, loopDestination));
        }

        [Fact]
        public void TooLowGradeLimitReturnsNoPartialRoads()
        {
            var arms = EndpointFixtures.DualCross();
            var planar = PlanarCloverleafCandidate.Create(arms, true, 30, 100);
            var result = SpatialCloverleafCandidate.Create(arms, planar, 10, 20, 30, 0.08f);
            Assert.Equal(SpatialCandidateFailure.GradeExceeded, result.Failure);
            Assert.Empty(result.Segments);
            Assert.False(result.IsPlacementReady);
        }

        [Fact]
        public void UsesEndpointHeightAndOutwardSlopeWhenEstimatingRetreat()
        {
            var arms = EndpointFixtures.DualCross();
            var old = arms[0].Endpoints[0];
            var modified = new SelectedEndpoint(old.NodeIndex, old.NodeVersion, new Vec3(old.Position.X, 12, old.Position.Z),
                new Vec3(old.OutwardTangent.X, 0.02f, old.OutwardTangent.Z), old.OneWay, old.TravelOutward, old.Width, old.Lanes, old.Prefab);
            arms[0] = new EndpointArm(0, new[] { modified, arms[0].Endpoints[1] });
            var planar = PlanarCloverleafCandidate.Create(arms, true, 30, 100, new[] { 20f, 0f, 0f, 0f });
            var result = SpatialCloverleafCandidate.Create(arms, planar, 10, 20, 30, 0.30f);
            Assert.Equal(SpatialCandidateFailure.None, result.Failure);
            var start = result.Segments.Single(s => s.Kind == CloverEdgeKind.Main && s.SourceArm == 0 && Math.Abs(s.WorldCurve.P0.Z - 272f) < 0.01f).WorldCurve;
            Assert.Equal(12.4f, start.P0.Y, 4);
            var tangent = start.Derivative(0);
            float horizontal = (float)Math.Sqrt(tangent.X * tangent.X + tangent.Z * tangent.Z);
            Assert.Equal(-0.02f, tangent.Y / horizontal, 5);
        }

        [Fact]
        public void RejectsStalePlanarInputAndInvalidParameters()
        {
            var arms = EndpointFixtures.DualCross();
            var planar = PlanarCloverleafCandidate.Create(arms, true, 30, 100);
            var old = arms[0].Endpoints[0];
            var changed = new SelectedEndpoint(old.NodeIndex, old.NodeVersion + 1, old.Position, old.OutwardTangent,
                old.OneWay, old.TravelOutward, old.Width, old.Lanes, old.Prefab);
            arms[0] = new EndpointArm(0, new[] { changed, arms[0].Endpoints[1] });
            var stale = SpatialCloverleafCandidate.Create(arms, planar, 10, 20, 30, 0.30f);
            Assert.Equal(SpatialCandidateFailure.StalePlanar, stale.Failure);
            Assert.Empty(stale.Segments);
            var freshArms = EndpointFixtures.DualCross();
            Assert.Equal(SpatialCandidateFailure.InvalidParameter,
                SpatialCloverleafCandidate.Create(freshArms, planar, float.NaN, 20, 30, 0.30f).Failure);
        }

        [Fact]
        public void WidthPlannedSpatialCandidateUsesEachRampRequiredFlatWithoutExtraShortNodes()
        {
            var arms = EndpointFixtures.DualCross();
            var planar = PlanarCloverleafCandidate.Create(arms, true, 30, 120, rampWidth: 8, sideGap: 4);
            Assert.Equal(PlanarCandidateFailure.None, planar.Failure);
            var spatial = SpatialCloverleafCandidate.Create(arms, planar, 10, 20, 0, 0.30f);
            Assert.Equal(SpatialCandidateFailure.None, spatial.Failure);
            foreach (var segment in spatial.Segments.Where(s => s.Kind != CloverEdgeKind.Main))
            {
                Assert.InRange(segment.WorldCurve.Length(128), 9.9f, 70.5f);
                Assert.True(segment.WorldCurve.MaxGrade(256) <= 0.30001f);
            }
            Assert.False(spatial.IsPlacementReady);
        }

        [Fact]
        public void RejectsFlatExtensionThatWouldInsertNewNodesNextToPlannedMergeNodes()
        {
            var arms = EndpointFixtures.DualCross();
            var planar = PlanarCloverleafCandidate.Create(arms, true, 30, 120, rampWidth: 8, sideGap: 4);
            var result = SpatialCloverleafCandidate.Create(arms, planar, 10, 20, 60, 0.30f);
            Assert.Equal(SpatialCandidateFailure.MergeBoundaryConflict, result.Failure);
            Assert.Empty(result.Segments);

            // A 30 m loop cannot keep 60 m in one <=90° piece; a larger
            // loop makes the requested 60 m flat feasible without extra nodes.
            var replanned = PlanarCloverleafCandidate.Create(arms, true, 40, 120,
                rampWidth: 8, sideGap: 4, minimumFlatLength: 60);
            Assert.Equal(PlanarCandidateFailure.None, replanned.Failure);
            var level = SpatialCloverleafCandidate.Create(arms, replanned, 10, 10, 60, 0.30f);
            Assert.Equal(SpatialCandidateFailure.None, level.Failure);
            foreach (var segment in level.Segments.Where(s => s.Kind != CloverEdgeKind.Main))
                Assert.InRange(segment.WorldCurve.Length(128), 9.9f, 70.5f);
        }
    }
}
