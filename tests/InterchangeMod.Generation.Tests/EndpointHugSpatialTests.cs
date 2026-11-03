using System;
using System.Collections.Generic;
using System.Linq;
using InterchangeMod.Generation.Endpoints;
using InterchangeMod.Generation.Geometry;
using Xunit;

namespace InterchangeMod.Generation.Tests
{
    public class EndpointHugSpatialTests
    {
        private static EndpointArm[] WideArms(bool rightHandTraffic) => EndpointFixtures.DualCross(600, rightHandTraffic)
            .Select(a => new EndpointArm(a.Index, a.Endpoints.Select(p => new SelectedEndpoint(p.NodeIndex, p.NodeVersion,
                p.Position, p.OutwardTangent, p.OneWay, p.TravelOutward, 20, p.Lanes, p.Prefab)))).ToArray();

        private static (EndpointArm[] Arms, PlanarCandidateResult Planar, SpatialCandidateResult Spatial) Build(
            bool rightHandTraffic, float loopRadius = 80, float directRadius = 120)
        {
            var arms = WideArms(rightHandTraffic);
            var design = new CloverleafRoadDesign(hugLoops: true, sharedRampPrefab: "Synthetic Shared", sharedRampWidth: 16);
            var planar = PlanarCloverleafCandidate.Create(arms, rightHandTraffic, loopRadius, directRadius,
                rampWidth: 8, sideGap: 4, design: design);
            Assert.Equal(PlanarCandidateFailure.None, planar.Failure);
            return (arms, planar, SpatialCloverleafCandidate.Create(arms, planar, 10, 20, 0, .30f));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void BuildsClearSharedPathsWithExactSplitNodesAndTenMetreLevelDifference(bool rightHandTraffic)
        {
            var (_, planar, spatial) = Build(rightHandTraffic);
            Assert.Equal(4, planar.Ramps.Count(r => r.Kind == CloverEdgeKind.SharedRamp));
            Assert.Equal(planar.Ramps.Count, planar.Ramps.Select(r => r.PathId).Distinct().Count());
            Assert.All(planar.Ramps, r => Assert.False(string.IsNullOrWhiteSpace(r.PathId)));
            Assert.Equal(SpatialCandidateFailure.None, spatial.Failure);
            Assert.False(spatial.IsPlacementReady);
            Assert.All(spatial.Segments, s => Assert.InRange(s.WorldCurve.MaxGrade(256), 0, .30001f));

            string Id(SpatialSegment segment) => segment.PathId;
            var routes = spatial.Segments.Where(s => s.Kind != CloverEdgeKind.Main).GroupBy(Id).ToArray();
            Assert.Equal(planar.Ramps.Count, routes.Length);
            foreach (var shared in routes.Where(g => g.First().Kind == CloverEdgeKind.SharedRamp))
            {
                Assert.All(shared, s => Assert.Equal("Synthetic Shared", s.Prefab));
                Assert.All(shared, s => Assert.Equal(16f, s.RoadWidth));
                var start = shared.First().StartNode; var end = shared.Last().EndNode;
                // At either shared endpoint, the inner loop and outer connector
                // use the same logical node despite their lane-centre offsets.
                Assert.Equal(2, routes.Count(g => g.Key != shared.Key
                    && (g.First().StartNode.Equals(start) || g.Last().EndNode.Equals(start))));
                Assert.Equal(2, routes.Count(g => g.Key != shared.Key
                    && (g.First().StartNode.Equals(end) || g.Last().EndNode.Equals(end))));
            }
            Assert.All(spatial.Segments.Where(s => s.Kind == CloverEdgeKind.Main), s => Assert.Equal("", Id(s)));
            var clearance = CloverleafRoadClearance.Check(spatial, planar, 8, 8.5f, out string detail);
            Assert.True(clearance == CurveClearanceResult.Clear, $"{clearance}: {detail}");
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void CoupledGradeUsesShortSharedRiseWithoutAddingSplitPlatforms(bool rightHandTraffic)
        {
            var (_, planar, spatial) = Build(rightHandTraffic, 60, 80);
            Assert.Equal(SpatialCandidateFailure.None, spatial.Failure);
            Assert.Equal(CurveClearanceResult.Clear,CloverleafRoadClearance.Check(spatial,planar,8,8.5f,out _));
            Assert.All(spatial.Segments,s=>Assert.InRange(s.WorldCurve.MaxGrade(256),0,.30001f));
        }

        [Theory]
        [InlineData(null, 16)]
        [InlineData("", 16)]
        [InlineData(" ", 16)]
        [InlineData("Shared", 0)]
        [InlineData("Shared", -16)]
        [InlineData("Shared", 7.99f)]
        [InlineData("Shared", float.NaN)]
        [InlineData("Shared", float.PositiveInfinity)]
        public void RejectsMissingSharedPrefabAndInvalidSharedWidth(string? prefab, float width)
        {
            var design = new CloverleafRoadDesign(hugLoops: true, sharedRampPrefab: prefab, sharedRampWidth: width);
            var result = PlanarCloverleafCandidate.Create(WideArms(true), true, 80, 120, rampWidth: 8, design: design);
            Assert.Equal(PlanarCandidateFailure.InvalidRoadWidth, result.Failure);
            Assert.Empty(result.Ramps);
            Assert.Empty(result.Lines);
        }

        [Fact]
        public void RejectsHugDesignWithoutAnOrdinaryRampWidth()
        {
            var design = new CloverleafRoadDesign(hugLoops: true, sharedRampPrefab: "Shared", sharedRampWidth: 16);
            var result = PlanarCloverleafCandidate.Create(WideArms(true), true, 80, 120, design: design);
            Assert.Equal(PlanarCandidateFailure.InvalidRoadWidth, result.Failure);
            Assert.Empty(result.Ramps);
        }

        [Fact]
        public void BridgePlanningSplitsMainSpanAndPreservesSharedPathMetadata()
        {
            var upper = new SpatialSegment(0, CloverEdgeKind.Main,
                Bezier3.Line(new Vec3(-80, 20, 0), new Vec3(80, 20, 0)), roadWidth: 20, isWeaving: true);
            var lower = new SpatialSegment(1, CloverEdgeKind.Main,
                Bezier3.Line(new Vec3(0, 10, -100), new Vec3(0, 10, 100)), roadWidth: 20, isWeaving: true);
            var shared = new SpatialSegment(2, CloverEdgeKind.SharedRamp,
                Bezier3.Line(new Vec3(300, 20, 300), new Vec3(400, 20, 300)),
                prefab: "Synthetic Shared", roadWidth: 16, pathId: "Hug2-Shared");
            var input = new SpatialCandidateResult(SpatialCandidateFailure.None, new[] { upper, lower, shared });
            var output = CloverleafBridgePlanner.Apply(input, new[] { new EndpointBridgeRule(0, 80, null, 20) });
            Assert.Equal(SpatialCandidateFailure.None, output.Failure);
            Assert.True(output.Segments.Count(s => s.SourceArm == 0) > 1);
            var preserved = Assert.Single(output.Segments.Where(s => s.Kind == CloverEdgeKind.SharedRamp));
            Assert.Same(shared, preserved);
            Assert.Equal("Hug2-Shared", preserved.PathId);
            Assert.Equal("Synthetic Shared", preserved.Prefab);
            Assert.Equal(16f, preserved.RoadWidth);
            Assert.All(output.Segments.Where(s => s.Kind == CloverEdgeKind.Main), s => Assert.Equal("", s.PathId));
        }

        private static bool ReachDirected(SpatialCandidateResult spatial, EndpointArm[] arms, int source, int target,
            bool rightHandTraffic, bool allowReverseShared = true)
        {
            bool through = target == (source + 2) % 4;
            bool direct = target == (source + (rightHandTraffic ? 3 : 1)) % 4;
            var movement = direct ? CloverEdgeKind.DirectRamp : CloverEdgeKind.LoopRamp;
            int targetLine = (target + 2) % 4;
            var edges = new List<(Vec3 From, Vec3 To, CloverEdgeKind Kind, int Arm, int SharedDirection)>();
            foreach (var main in spatial.Segments.Where(s => s.Kind == CloverEdgeKind.Main))
                edges.Add((main.StartNode, main.EndNode, main.Kind, main.SourceArm, 0));
            foreach (var path in spatial.Segments.Where(s => s.Kind != CloverEdgeKind.Main).GroupBy(s => s.PathId))
            {
                var first = path.First(); var last = path.Last();
                edges.Add((first.StartNode, last.EndNode, first.Kind, first.SourceArm,
                    first.Kind == CloverEdgeKind.SharedRamp ? 1 : 0));
                // Mainlines and every connector/loop piece are strictly one-way.
                // Only the explicitly shared two-way road permits reverse travel.
                if (first.Kind == CloverEdgeKind.SharedRamp && allowReverseShared)
                    edges.Add((last.EndNode, first.StartNode, first.Kind, first.SourceArm, -1));
            }
            var start = arms[source].Endpoints.Single(p => !p.TravelOutward).Position;
            var end = arms[target].Endpoints.Single(p => p.TravelOutward).Position;
            var queue = new Queue<(Vec3 Node, bool UsedShared)>();
            var seen = new HashSet<(Vec3 Node, bool UsedShared)>();
            queue.Enqueue((start, false));
            while (queue.Count > 0)
            {
                var state = queue.Dequeue();
                if (!seen.Add(state)) continue;
                if (state.Node.Equals(end) && state.UsedShared == !through) return true;
                foreach (var edge in edges.Where(e => e.From.Equals(state.Node)))
                {
                    if (edge.Kind == CloverEdgeKind.Main)
                    {
                        if (edge.Arm == source || (!through && edge.Arm == targetLine))
                            queue.Enqueue((edge.To, state.UsedShared));
                    }
                    else if (!through && edge.Kind == CloverEdgeKind.SharedRamp)
                    {
                        if (!state.UsedShared && edge.SharedDirection == (direct ? 1 : -1))
                            queue.Enqueue((edge.To, true));
                    }
                    else if (!through && edge.Kind == movement && edge.Arm == source)
                        queue.Enqueue((edge.To, state.UsedShared));
                }
            }
            return false;
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void AllTwelveMovementsFollowDirectedRoadsAndUseBothSharedDirections(bool rightHandTraffic)
        {
            var (arms, _, spatial) = Build(rightHandTraffic);
            Assert.Equal(SpatialCandidateFailure.None, spatial.Failure);
            for (int source = 0; source < 4; source++)
            {
                for (int target = 0; target < 4; target++)
                    if (target != source)
                        Assert.True(ReachDirected(spatial, arms, source, target, rightHandTraffic), $"{source}->{target}");
                int loopTarget = (source + (rightHandTraffic ? 1 : 3)) % 4;
                Assert.False(ReachDirected(spatial, arms, source, loopTarget, rightHandTraffic, allowReverseShared: false));
            }
        }

        [Fact]
        public void CarriesPhysicalPathMetadataAndInterpolatedEndpointLevelsIntoSpace()
        {
            var arms = EndpointFixtures.DualCross();
            var original = PlanarCloverleafCandidate.Create(arms, true, 30, 100);
            Assert.Equal(PlanarCandidateFailure.None, original.Failure);
            var first = original.Ramps[0];
            var physical = new PlanarRamp(first.SourceArm, first.TargetArm, first.Kind, first.StartAlong, first.EndAlong,
                first.SweepRadians, first.Curves, first.RequiredFlatStart, first.RequiredFlatEnd, first.StartNode, first.EndNode,
                pathId: "SharedFixture", prefab: "Synthetic Shared", roadWidth: 16, startLevelFactor: .25f, endLevelFactor: .75f);
            var planar = new PlanarCandidateResult(PlanarCandidateFailure.None,
                new[] { physical }.Concat(original.Ramps.Skip(1)), original.Lines, original.RetreatByArm, original.InputArms,
                original.Design, original.RightHandTraffic, 8);

            var spatial = SpatialCloverleafCandidate.Create(arms, planar, 10, 20, 30, .30f);
            Assert.Equal(SpatialCandidateFailure.None, spatial.Failure);
            var route = spatial.Segments.Where(s => s.SourceArm == first.SourceArm && s.Kind == first.Kind).ToArray();
            Assert.NotEmpty(route);
            Assert.Equal(12.5f, route[0].WorldCurve.P0.Y);
            Assert.Equal(17.5f, route[route.Length - 1].WorldCurve.P3.Y);
            Assert.Equal(12.5f, route[0].StartNode.Y);
            Assert.Equal(17.5f, route[route.Length - 1].EndNode.Y);
            Assert.All(route, s => Assert.Equal("Synthetic Shared", s.Prefab));
            Assert.All(route, s => Assert.Equal(16f, s.RoadWidth));
            Assert.All(route, s => Assert.Equal("SharedFixture", s.PathId));
            Assert.All(spatial.Segments.Where(s => s.Kind == CloverEdgeKind.Main),
                s => Assert.Equal("", s.PathId));
            Assert.All(spatial.Segments.Where(s => s.Kind != CloverEdgeKind.Main && !route.Contains(s)),
                s => Assert.Equal(8f, s.RoadWidth));
        }
    }
}
