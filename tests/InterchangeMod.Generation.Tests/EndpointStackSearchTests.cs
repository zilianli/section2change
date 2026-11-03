using InterchangeMod.Generation.Endpoints;
using InterchangeMod.Generation.Geometry;
using InterchangeMod.Generation.Roads;

namespace InterchangeMod.Generation.Tests
{
    public sealed class EndpointStackSearchTests
    {
        private static readonly RoadInfo Ramp = new RoadInfo("Ramp8", true, 1, 0, 8);

        public static IEnumerable<object[]> Configurations()
        {
            foreach (bool rht in new[] { true, false })
            {
                yield return new object[] { InterchangeType.Stack, false, rht };
                yield return new object[] { InterchangeType.Stack, true, rht };
                yield return new object[] { InterchangeType.Turbine, false, rht };
            }
        }

        // Catches silently using synthetic extensions or dropping the final deck check:
        // the short selected site must become usable only by cutting its real traces.
        [Theory]
        [MemberData(nameof(Configurations))]
        public void RetreatsShortSiteOnRealRoadAndPreservesCutMetadata(InterchangeType type, bool leftExit, bool rht)
        {
            var arms = ShortArms(rht);
            var traces = Traces(arms);
            var zero = Search(arms, traces, type, rht, 0, leftExit);
            Assert.Equal(RetreatSearchFailure.NoCandidate, zero.Failure);
            Assert.Null(zero.Spatial);
            var result = Search(arms, traces, type, rht, 1000, leftExit);
            Assert.True(result.Failure == RetreatSearchFailure.None, Failure(result));
            Assert.Equal(4, result.Distances.Count);
            Assert.Contains(result.Distances, d => d > 0);
            Assert.Equal(8, result.Cuts.Count);
            Assert.Equal(4, result.Arms.Count);
            Assert.Equal(PlanarCandidateFailure.None, result.LastPlanarFailure);
            Assert.Equal(SpatialCandidateFailure.None, result.LastSpatialFailure);
            Assert.Empty(result.ClearanceDetail);
            Assert.Equal(CurveClearanceResult.Clear, EndpointNetworkClearance.Check(result.Spatial!, 8.5f, out var detail));
            Assert.Empty(detail);
            foreach (var cut in result.Cuts)
            {
                var original = traces.Single(t => t.Origin.NodeIndex == cut.Port!.NodeIndex);
                Assert.True(cut.Distance > 0);
                Assert.NotEmpty(cut.Consumed);
                Assert.NotEmpty(cut.ModifiedCurves);
                Assert.NotNull(cut.Remainder);
                Assert.Equal(0, cut.BindingNodeIndex);
                Assert.Equal(original.Origin.Prefab, cut.Port!.Prefab);
                Assert.Equal(original.Origin.Width, cut.Port.Width);
                Assert.Contains(result.Arms.SelectMany(a => a.Endpoints), p => ReferenceEquals(p, cut.Port));
                var endpoint = cut.Port;
                var approach = endpoint.TravelOutward
                    ? Assert.Single(result.Spatial!.Segments.Where(s => s.WorldCurve.P3 == endpoint.Position))
                    : Assert.Single(result.Spatial!.Segments.Where(s => s.WorldCurve.P0 == endpoint.Position));
                Assert.Equal(CloverEdgeKind.Main, approach.Kind);
                Assert.Equal(endpoint.Prefab, approach.Prefab);
                Assert.Equal(endpoint.Width, approach.RoadWidth);
                Assert.True(Dot(endpoint.TravelOutward ? approach.WorldCurve.Derivative(1) : -approach.WorldCurve.Derivative(0),
                    endpoint.OutwardTangent) > .99999f, "The original cut tangent must survive the approach profile and splitting.");
            }
            Assert.All(result.Spatial!.Segments, s => Assert.InRange(s.WorldCurve.MaxGrade(256), 0, .16001f));
            Assert.False(result.Spatial.IsPlacementReady);
        }

        // Catches moving the retreated core together with the cuts, and corrupting the
        // last endpoint while the vertical Hermite profile is evaluated in world Y.
        [Theory]
        [InlineData(InterchangeType.Stack)]
        [InlineData(InterchangeType.Turbine)]
        public void FitsLoggedMixedHeightsWithSyntheticRealContinuations(InterchangeType type)
        {
            var positions = new[] { new Vec3(-3681.96f,488.63f,-1322.51f),new Vec3(-3674.93f,488.63f,-1295.43f),
                new Vec3(-3566.12f,500.67f,-1214.54f),new Vec3(-3540.68f,500.66f,-1227.68f),
                new Vec3(-3447.27f,488.63f,-1354.48f),new Vec3(-3454.29f,488.63f,-1381.56f),
                new Vec3(-3601.27f,500.42f,-1451.16f),new Vec3(-3626.7f,500.43f,-1437.95f) };
            var arms = Enumerable.Range(0, 4).Select(i => new EndpointArm(i, Enumerable.Range(0, 2).Select(k =>
            {
                var p = positions[i * 2 + k]; var other = positions[((i + 2) % 4) * 2 + 1 - k];
                return new SelectedEndpoint(i * 10 + k + 1, 1, p, p - other, true, k == 1, 20, 4, "Road");
            }))).ToArray();
            // Only the rounded selected positions are from the game log. These straight
            // continuation edges and exact tangents are synthetic, not a game replay.
            var traces = Traces(arms);
            var core = EndpointStackCore.Create(arms, type, true, 488.63f, 500.55f, Ramp, .16f, new CloverleafRoadDesign());
            Assert.True(core.Success, core.Diagnostic);
            var result = EndpointStackSearch.Search(arms, traces, type, true, 488.63f, 500.55f, Ramp, .16f, 1000,
                new CloverleafRoadDesign());
            Assert.True(result.Failure == RetreatSearchFailure.None, Failure(result));
            foreach (var segment in core.Segments)
                Assert.Contains(result.Spatial!.Segments, s => s.WorldCurve.Equals(segment.WorldCurve) && s.PathId == segment.PathId);
            foreach (var cut in result.Cuts)
            {
                var p = cut.Port!;
                Assert.Contains(result.Spatial!.Segments, s => p.TravelOutward ? s.WorldCurve.P3.Equals(p.Position) : s.WorldCurve.P0.Equals(p.Position));
            }
            Assert.All(result.Spatial!.Segments, s => Assert.InRange(s.WorldCurve.MaxGrade(256), 0, .16001f));
        }

        // Catches unbounded retries and returning a grade-feasible, partly checked shape
        // when the candidate budget ends before a safe cut can be reached.
        [Fact]
        public void CandidateBudgetReturnsNoPartialGeometry()
        {
            var arms = ShortArms();
            // Local angle/grade scans are not complete candidate attempts. Force the
            // first fully attached network to fail the independent deck check.
            var result = EndpointStackSearch.Search(arms, Traces(arms), InterchangeType.Stack, true, 10, 22,
                Ramp, .16f, 1000, new CloverleafRoadDesign(), maxCandidates: 1, verticalClearance: 1000);
            Assert.Equal(RetreatSearchFailure.BudgetExceeded, result.Failure);
            Assert.Equal(1, result.Attempts);
            Assert.Null(result.Spatial);
            Assert.Empty(result.Cuts); Assert.Empty(result.Arms); Assert.Empty(result.Distances);
            Assert.NotEmpty(result.ClearanceDetail);
        }

        // Catches losing existing-node bindings at zero retreat, emitting long approach
        // courses, or putting the outgoing approach before its core in the path order.
        [Fact]
        public void ZeroRetreatKeepsExistingNodesAndSplitsOrderedApproaches()
        {
            var arms = ShortArms().Select(a => new EndpointArm(a.Index, a.Endpoints.Select(p =>
                new SelectedEndpoint(p.NodeIndex, p.NodeVersion,
                    p.Position + new Vec3(p.OutwardTangent.X, 0, p.OutwardTangent.Z) * 1200,
                    p.OutwardTangent, true, p.TravelOutward, p.Width, p.Lanes, p.Prefab)))).ToArray();
            var core = EndpointStackCore.Create(arms, InterchangeType.Stack, true, 10, 22, Ramp, .16f,
                new CloverleafRoadDesign());
            Assert.True(core.Success, core.Diagnostic);
            var result = Search(arms, Traces(arms), InterchangeType.Stack, true, 0);
            Assert.True(result.Failure == RetreatSearchFailure.None, Failure(result));
            Assert.All(result.Distances, d => Assert.Equal(0, d));
            foreach (var cut in result.Cuts)
            {
                var original = arms.SelectMany(a => a.Endpoints).Single(p => p.NodeIndex == cut.Port!.NodeIndex);
                Assert.Same(original, cut.Port);
                Assert.Equal(original.NodeIndex, cut.BindingNodeIndex);
                Assert.Equal(original.NodeVersion, cut.BindingNodeVersion);
                Assert.Empty(cut.Consumed); Assert.Empty(cut.ModifiedCurves);
            }
            foreach (var path in result.Spatial!.Segments.Where(s => s.Kind == CloverEdgeKind.Main).GroupBy(s => s.PathId))
            {
                var pieces = path.ToArray();
                Assert.All(pieces.Zip(pieces.Skip(1)), pair => Assert.Equal(pair.First.EndNode, pair.Second.StartNode));
                var corePath = core.Segments.Where(s => s.PathId == path.Key).ToArray();
                int first = Array.FindIndex(pieces, s => s.WorldCurve.Equals(corePath[0].WorldCurve));
                int last = Array.FindIndex(pieces, s => s.WorldCurve.Equals(corePath[^1].WorldCurve));
                Assert.True(first > 0); Assert.True(last + 1 < pieces.Length);
                Assert.All(pieces.Take(first).Concat(pieces.Skip(last + 1)), s =>
                    Assert.InRange(s.WorldCurve.Length(128), 10, 70.01f));
            }
        }

        // Catches returning the first grade-feasible candidate without checking every
        // added approach against the complete network, or accepting uncertain clearance.
        [Fact]
        public void UnsafeDeckClearanceRejectsEveryCandidate()
        {
            var arms = ShortArms();
            var result = EndpointStackSearch.Search(arms, Traces(arms), InterchangeType.Stack, true, 10, 22, Ramp,
                .16f, 1000, new CloverleafRoadDesign(), maxCandidates: 512, verticalClearance: 1000);
            Assert.Equal(RetreatSearchFailure.BudgetExceeded, result.Failure);
            Assert.Equal(128, result.Attempts);
            Assert.True(result.ClearanceRejected > 1);
            Assert.NotEmpty(result.ClearanceDetail);
            Assert.Null(result.Spatial); Assert.Empty(result.Cuts);
        }

        // Catches treating all four approach traces as equally long or charging the
        // same independently clamped distance vector more than once.
        [Fact]
        public void IndependentlyClampedShortTracesStopWithoutRepeatedCandidates()
        {
            var arms = ShortArms();
            var traces = Traces(arms, arm => 20 + arm * 25);
            var result = Search(arms, traces, InterchangeType.Stack, true, 1000);
            Assert.Equal(RetreatSearchFailure.NoCandidate, result.Failure);
            Assert.Equal(0, result.Attempts);
            Assert.Null(result.Spatial); Assert.Empty(result.Cuts);
        }

        [Fact]
        public void RejectsInvalidParametersAndStaleTraceSnapshots()
        {
            var arms = ShortArms(); var traces = Traces(arms);
            Assert.Equal(RetreatSearchFailure.InvalidInput, Search(arms, traces, InterchangeType.Cloverleaf, true, 1000).Failure);
            Assert.Equal(RetreatSearchFailure.InvalidInput, EndpointStackSearch.Search(arms, traces, InterchangeType.Stack, true,
                10, 22, Ramp, .31f, 1000, new CloverleafRoadDesign()).Failure);
            Assert.Equal(RetreatSearchFailure.InvalidInput, EndpointStackSearch.Search(arms, traces, InterchangeType.Stack, true,
                10, 22, Ramp, .16f, 1000, new CloverleafRoadDesign(), verticalClearance: float.NaN).Failure);
            var old = traces[0].Origin;
            var stale = new SelectedEndpoint(old.NodeIndex, old.NodeVersion, old.Position + Vec3.UnitX, old.OutwardTangent,
                old.OneWay, old.TravelOutward, old.Width, old.Lanes, old.Prefab);
            traces[0] = new RoadApproachTrace(stale, Array.Empty<RoadTracePiece>());
            var result = Search(arms, traces, InterchangeType.Stack, true, 1000);
            Assert.Equal(RetreatSearchFailure.InvalidInput, result.Failure); Assert.Equal(0, result.Attempts);
            Assert.Null(result.Spatial); Assert.Empty(result.Cuts);
        }

        private static RetreatSearchResult Search(EndpointArm[] arms, RoadApproachTrace[] traces, InterchangeType type,
            bool rht, float retreat, bool leftExit = false, int maxCandidates = 128) =>
            EndpointStackSearch.Search(arms, traces, type, rht, 10, 22, Ramp, .16f, retreat,
                new CloverleafRoadDesign(), leftExit, maxCandidates);

        private static EndpointArm[] ShortArms(bool rht = true) => EndpointFixtures.DualCross(100, rht)
            .Select(a => new EndpointArm(a.Index, a.Endpoints.Select(p => new SelectedEndpoint(p.NodeIndex, p.NodeVersion,
                new Vec3(p.Position.X, a.Index % 2 == 0 ? 10 : 22, p.Position.Z),
                p.OutwardTangent + new Vec3(0, a.Index % 2 == 0 ? .018f : -.012f, 0), true, p.TravelOutward, 20, 4, "Road")))).ToArray();

        private static RoadApproachTrace[] Traces(EndpointArm[] arms, Func<int, float>? length = null) =>
            arms.SelectMany(a => a.Endpoints.Select(p =>
            {
                var delta = p.OutwardTangent * ((length?.Invoke(a.Index) ?? 1100) / Horizontal(p.OutwardTangent));
                return new RoadApproachTrace(p, new[] { new RoadTracePiece(p.NodeIndex + 1000, 1,
                    Bezier3.Line(p.Position, p.Position + delta), false, p.NodeIndex + 2000, 1) });
            })).ToArray();

        private static float Horizontal(Vec3 p) => (float)Math.Sqrt((double)p.X * p.X + (double)p.Z * p.Z);
        private static float Dot(Vec3 a, Vec3 b) => Directions.Dot(Vec3.Normalize(a), Vec3.Normalize(b));
        private static string Failure(RetreatSearchResult result) =>
            $"{result.Failure}: attempts={result.Attempts}; {result.LastSpatialFailure}; {result.ClearanceDetail}";
    }
}
