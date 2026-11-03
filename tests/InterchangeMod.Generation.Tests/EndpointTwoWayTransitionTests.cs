using System.Reflection;
using InterchangeMod.Generation.Endpoints;
using InterchangeMod.Generation.Geometry;
using InterchangeMod.Generation.Roads;

namespace InterchangeMod.Generation.Tests;

public class EndpointTwoWayTransitionTests
{
    // Removing a real fork, swapping either traffic direction, or narrowing the
    // transition prefab must fail these geometry and lane-conservation assertions.
    [Theory]
    [InlineData(true, 2, 20, 12)]
    [InlineData(false, 2, 20, 12)]
    [InlineData(true, 3, 28, 16)]
    [InlineData(false, 3, 28, 16)]
    [InlineData(true, 4, 36, 20)]
    [InlineData(false, 4, 36, 20)]
    [InlineData(true, 3, 28, 20)]
    public void RealTwoWayStemAndSeparateDirectedFansPreserveExactPorts(bool rht, int lanes, float fullWidth, float oneWidth)
    {
        var fixture = Fixture(rht, lanes, fullWidth, oneWidth);
        var result = Connect(fixture.Candidate, fixture.Actual, fixture.Proxy, rht, .16f, fixture.Catalog);
        Assert.True(result.Failure == SpatialCandidateFailure.None, result.Diagnostic);
        Assert.All(fixture.Candidate.Segments, original => Assert.Contains(original, result.Segments));
        var stems = result.Segments.Where(s => s.Prefab == "TwoWay").ToArray();
        Assert.NotEmpty(stems);
        Assert.All(stems, s => { Assert.Equal(CloverEdgeKind.Main, s.Kind); Assert.Equal(fullWidth, s.RoadWidth); });
        Assert.Equal(fixture.Actual[0].Endpoints[0].Position, stems[^1].WorldCurve.P3);
        SameDirection(fixture.Actual[0].Endpoints[0].OutwardTangent, stems[^1].WorldCurve.Derivative(1));
        var fork = stems[0].StartNode;
        var fans = result.Segments.Except(fixture.Candidate.Segments).Where(s => s.Prefab == "OneWay")
            .GroupBy(s => s.PathId).Select(g => g.ToArray()).ToArray();
        Assert.Equal(2, fans.Length);
        var incoming = fans.Single(p => p[0].StartNode == fork);
        var outgoing = fans.Single(p => p[^1].EndNode == fork);
        Assert.Equal(fixture.Proxy[0].Endpoints.Single(p => !p.TravelOutward).Position, incoming[^1].WorldCurve.P3);
        Assert.Equal(fixture.Proxy[0].Endpoints.Single(p => p.TravelOutward).Position, outgoing[0].WorldCurve.P0);
        float hand = rht ? 1 : -1;
        Assert.Equal(-hand * (fullWidth - oneWidth) / 2, incoming[0].WorldCurve.P0.X, 4);
        Assert.Equal(hand * (fullWidth - oneWidth) / 2, outgoing[^1].WorldCurve.P3.X, 4);
        Assert.NotEqual(incoming[0].WorldCurve.P0, incoming[0].StartNode);
        Assert.All(fans.SelectMany(p => p), s => { Assert.Equal(CloverEdgeKind.DirectRamp, s.Kind); Assert.Equal(oneWidth, s.RoadWidth); });
        AssertCertified(result, fixture.Catalog);
    }

    // A curved source has different actual/proxy tangents and world heights.
    // Flattening the real cut or using the proxy tangent at the stem must fail.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CurvedGradedSourceKeepsRealCutAndSmoothNativeFork(bool rht)
    {
        var fixture = Fixture(rht, 3, 28, 16, curved: true);
        var result = Connect(fixture.Candidate, fixture.Actual, fixture.Proxy, rht, .16f, fixture.Catalog);
        Assert.True(result.Failure == SpatialCandidateFailure.None, result.Diagnostic);
        var stem = result.Segments.Where(s => s.Prefab == "TwoWay").ToArray();
        Assert.Equal(fixture.Actual[0].Endpoints[0].Position, stem[^1].WorldCurve.P3);
        SameDirection(fixture.Actual[0].Endpoints[0].OutwardTangent, stem[^1].WorldCurve.Derivative(1));
        Assert.InRange(Math.Abs(stem[0].WorldCurve.Derivative(0).Y), 0, .0001);
        foreach (var port in fixture.Proxy[0].Endpoints)
        {
            var fan = result.Segments.Except(fixture.Candidate.Segments).Where(s => s.Prefab == "OneWay")
                .Single(s => port.TravelOutward ? s.WorldCurve.P0 == port.Position : s.WorldCurve.P3 == port.Position);
            SameDirection(port.TravelOutward ? port.OutwardTangent : -port.OutwardTangent,
                fan.WorldCurve.Derivative(port.TravelOutward ? 0 : 1));
        }
        AssertCertified(result, fixture.Catalog);
    }

    [Fact]
    public void TwoWaySnapshotTravelFlagCannotReverseTheTrafficSplit()
    {
        var fixture = Fixture(true, 2, 20, 12);
        var original = fixture.Actual[0].Endpoints[0];
        var changed = new[] { new EndpointArm(0, new[] { new SelectedEndpoint(original.NodeIndex, original.NodeVersion,
            original.Position, original.OutwardTangent, false, !original.TravelOutward, original.Width, original.Lanes, original.Prefab) }) };
        var first = Connect(fixture.Candidate, fixture.Actual, fixture.Proxy, true, .16f, fixture.Catalog);
        var second = Connect(fixture.Candidate, changed, fixture.Proxy, true, .16f, fixture.Catalog);
        Assert.Equal(SpatialCandidateFailure.None, first.Failure);
        Assert.Equal(SpatialCandidateFailure.None, second.Failure);
        Assert.Equal(first.Segments.Select(s => s.WorldCurve), second.Segments.Select(s => s.WorldCurve));
    }

    [Fact]
    public void ExistingDualCandidateIsReturnedWithoutGeometryChanges()
    {
        var fixture = Fixture(true, 2, 20, 12);
        Assert.Same(fixture.Candidate, Connect(fixture.Candidate, fixture.Proxy, fixture.Proxy, true, .16f, fixture.Catalog));
    }

    // Lane mismatch, missing native metadata and insufficient reserved room must
    // report no partial geometry rather than silently dropping a traffic lane.
    [Fact]
    public void InvalidMetadataAndInsufficientSpaceReturnEmptyFailures()
    {
        var fixture = Fixture(true, 3, 28, 16);
        var wrong = fixture.Catalog.Select(r => r.Name == "TwoWay" ? new RoadInfo("TwoWay", true, 3, 2, 28) : r).ToArray();
        foreach (var catalog in new[] { wrong, fixture.Catalog.Where(r => r.Name != "OneWay").ToArray() })
        {
            var result = Connect(fixture.Candidate, fixture.Actual, fixture.Proxy, true, .16f, catalog);
            Assert.NotEqual(SpatialCandidateFailure.None, result.Failure); Assert.Empty(result.Segments); Assert.NotEmpty(result.Diagnostic);
        }
        var actual = fixture.Actual[0].Endpoints[0];
        var shortArm = new[] { new EndpointArm(0, new[] { new SelectedEndpoint(actual.NodeIndex, 1,
            new Vec3(0, 20, 170), Vec3.UnitZ, false, false, 28, 3, "TwoWay") }) };
        var shortResult = Connect(fixture.Candidate, shortArm, fixture.Proxy, true, .16f, fixture.Catalog);
        Assert.NotEqual(SpatialCandidateFailure.None, shortResult.Failure); Assert.Empty(shortResult.Segments);
    }

    [Fact]
    public void AddedTransitionMustPassFullNetworkDeckCheck()
    {
        var fixture = Fixture(true, 2, 20, 12);
        var crossing = new SpatialSegment(1, CloverEdgeKind.Main,
            Bezier3.Line(new Vec3(-80, 20, 330), new Vec3(80, 20, 330)),
            prefab: "OneWay", roadWidth: 12, pathId: "Unrelated crossing");
        var blocked = new SpatialCandidateResult(SpatialCandidateFailure.None, fixture.Candidate.Segments.Append(crossing));
        var result = Connect(blocked, fixture.Actual, fixture.Proxy, true, .16f, fixture.Catalog);
        Assert.NotEqual(SpatialCandidateFailure.None, result.Failure); Assert.Empty(result.Segments); Assert.NotEmpty(result.Diagnostic);
    }

    // The road decks are vertically separated; the real two-way prefab's centre
    // pillar at z=475 crosses the lower road. Checking only decks must not pass.
    [Fact]
    public void NativeTwoWayPillarMetadataIsCheckedAgainstTheWholeNetwork()
    {
        var fixture = Fixture(true, 2, 20, 12);
        fixture.Catalog[0].MiddlePillarSpacing = 0;
        var lower = Enumerable.Range(0, 4).Select(i => new SpatialSegment(1, CloverEdgeKind.Main,
            Bezier3.Line(new Vec3(-80 + i * 40, 0, 475), new Vec3(-40 + i * 40, 0, 475)),
            prefab: "OneWay", roadWidth: 12, pathId: "Lower road"));
        var blocked = new SpatialCandidateResult(SpatialCandidateFailure.None, fixture.Candidate.Segments.Concat(lower));
        var result = Connect(blocked, fixture.Actual, fixture.Proxy, true, .16f, fixture.Catalog);
        Assert.NotEqual(SpatialCandidateFailure.None, result.Failure); Assert.Empty(result.Segments); Assert.NotEmpty(result.Diagnostic);
    }

    private static SpatialCandidateResult Connect(SpatialCandidateResult candidate, IReadOnlyList<EndpointArm> actual,
        IReadOnlyList<EndpointArm> proxy, bool rht, float grade, IReadOnlyList<RoadInfo> catalog)
    {
        var type = typeof(EndpointArm).Assembly.GetType("InterchangeMod.Generation.Endpoints.EndpointTwoWayTransition");
        Assert.True(type != null, "EndpointTwoWayTransition shared native-road adapter is missing.");
        var method = type!.GetMethod("Connect", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);
        return (SpatialCandidateResult)method!.Invoke(null, new object[] { candidate, actual, proxy, rht, grade, catalog,
            new CloverleafRoadDesign(), 8.5f })!;
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void EveryHiddenSpanAllocationRespectsTheCallersRemainingBudget(int budget)
    {
        var fixture = Fixture(true, 2, 20, 12);
        var crossing = new SpatialSegment(1, CloverEdgeKind.Main,
            Bezier3.Line(new Vec3(-80, 20, 330), new Vec3(80, 20, 330)),
            prefab: "OneWay", roadWidth: 12, pathId: "Blocking road");
        var blocked = new SpatialCandidateResult(SpatialCandidateFailure.None, fixture.Candidate.Segments.Append(crossing));
        var method = typeof(EndpointTwoWayTransition).GetMethod("ConnectBounded");
        Assert.NotNull(method);
        object[] args = { blocked, fixture.Actual, fixture.Proxy, true, .16f, fixture.Catalog,
            new CloverleafRoadDesign(), 8.5f, budget, 0 };
        var result = (SpatialCandidateResult)method!.Invoke(null, args)!;
        Assert.Equal(budget, (int)args[^1]);
        Assert.NotEqual(SpatialCandidateFailure.None, result.Failure);
        Assert.Empty(result.Segments);
    }

    private static void AssertCertified(SpatialCandidateResult result, RoadInfo[] catalog)
    {
        Assert.Equal(CurveClearanceResult.Clear, EndpointNetworkClearance.Check(result, 8.5f, out var deck));
        Assert.True(string.IsNullOrEmpty(deck), deck);
        var supports = catalog.Select(r => new EndpointPrefabSupportRule(r.Name, r.ElevatedLength, r.MiddlePillarSpacing, 3)).ToArray();
        Assert.Equal(CurveClearanceResult.Clear, EndpointNetworkSupports.Check(result, Array.Empty<EndpointBridgeRule>(),
            catalog.Single(r => r.Name == "OneWay"), out var support, supports));
        Assert.True(string.IsNullOrEmpty(support), support);
        foreach (var path in result.Segments.GroupBy(s => s.PathId))
        {
            var pieces = path.ToArray();
            foreach (var pair in pieces.Zip(pieces.Skip(1)))
            {
                Assert.Equal(pair.First.EndNode, pair.Second.StartNode);
                SameDirection(pair.First.WorldCurve.Derivative(1), pair.Second.WorldCurve.Derivative(0));
            }
            foreach (var s in pieces)
            {
                Assert.Equal(VerticalProfileFailure.None, VerticalProfile.CheckGrade(s.WorldCurve, .16f));
                Assert.Equal(CurveCurvatureResult.Clear, CurveCurvature.CheckMinimumRadius(s.WorldCurve, 2 * s.RoadWidth!.Value));
                Assert.InRange(s.WorldCurve.Length(128), 10, catalog.Single(r => r.Name == s.Prefab).ElevatedLength);
                var chord = Flat(s.WorldCurve.P3 - s.WorldCurve.P0);
                foreach (var edge in new[] { s.WorldCurve.P1 - s.WorldCurve.P0, s.WorldCurve.P2 - s.WorldCurve.P1, s.WorldCurve.P3 - s.WorldCurve.P2 })
                    Assert.True(Dot(chord, Flat(edge)) >= Math.Cos(20 * Math.PI / 180) - .00001);
            }
        }
    }

    private static void SameDirection(Vec3 expected, Vec3 actual) => Assert.True(Dot(expected, actual) > .99999,
        $"Expected tangent {expected}, got {actual}.");
    private static Vec3 Flat(Vec3 p) => new(p.X, 0, p.Z);
    private static double Dot(Vec3 a, Vec3 b) => ((double)a.X * b.X + (double)a.Y * b.Y + (double)a.Z * b.Z) / a.Length() / b.Length();

    private static (SpatialCandidateResult Candidate, EndpointArm[] Actual, EndpointArm[] Proxy, RoadInfo[] Catalog)
        Fixture(bool rht, int lanes, float fullWidth, float oneWidth, bool curved = false)
    {
        var catalog = new[] { new RoadInfo("TwoWay", true, lanes, lanes, fullWidth) { ElevatedLength = 80 },
            new RoadInfo("OneWay", true, lanes, 0, oneWidth) { ElevatedLength = 80 } };
        var actual = new[] { new EndpointArm(0, new[] { new SelectedEndpoint(10, 2,
            curved ? new Vec3(10, 35, 500) : new Vec3(0, 20, 500),
            curved ? new Vec3(.10f, .04f, 1) : Vec3.UnitZ, false, false, fullWidth, lanes, "TwoWay") }) };
        float hand = rht ? 1 : -1, median = oneWidth / 2 + 6;
        var proxy = new[] { new EndpointArm(0, new[] {
            new SelectedEndpoint(10001, 1, new Vec3(-hand * median, 20, 140), curved ? new Vec3(-.02f, .04f, 1) : Vec3.UnitZ,
                true, false, oneWidth, lanes, "OneWay"),
            new SelectedEndpoint(10002, 1, new Vec3(hand * median, 20, 140), curved ? new Vec3(-.02f, .04f, 1) : Vec3.UnitZ,
                true, true, oneWidth, lanes, "OneWay") }) };
        var segments = new List<SpatialSegment>();
        foreach (var p in proxy[0].Endpoints)
        {
            var end = p.Position - p.OutwardTangent * 120;
            var line = p.TravelOutward ? Bezier3.Line(end, p.Position) : Bezier3.Line(p.Position, end);
            for (int i = 0; i < 2; i++) segments.Add(new SpatialSegment(0, CloverEdgeKind.Main,
                line.Slice(i / 2f, (i + 1) / 2f), prefab: "OneWay", roadWidth: oneWidth,
                pathId: p.TravelOutward ? "Core outgoing" : "Core incoming"));
        }
        return (new SpatialCandidateResult(SpatialCandidateFailure.None, segments), actual, proxy, catalog);
    }
}
