using System.Reflection;
using InterchangeMod.Generation.Endpoints;
using InterchangeMod.Generation.Geometry;
using InterchangeMod.Generation.Roads;

namespace InterchangeMod.Generation.Tests;

public sealed class EndpointMixedRoadSearchTests
{
    private static RetreatSearchResult Search(EndpointArm[] arms, RoadApproachTrace[] traces, object setup,
        float limit = 1000, int budget = 128)
    {
        var search = typeof(EndpointArm).Assembly.GetType("InterchangeMod.Generation.Endpoints.EndpointMixedRoadSearch");
        Assert.NotNull(search);
        return (RetreatSearchResult)search!.GetMethod("Search")!.Invoke(null, new object[] { arms, traces, setup, .16f, limit, budget })!;
    }

    internal static RoadApproachTrace[] Traces(EndpointArm[] arms, float length = 1300, bool curved = false) =>
        arms.SelectMany(a => a.Endpoints).Select(p =>
        {
            var side = Directions.RightOf(p.OutwardTangent); var end = p.Position + p.OutwardTangent * length + (curved ? side * 45 : Vec3.Zero);
            return new RoadApproachTrace(p, new[] { new RoadTracePiece(p.NodeIndex + 1000, 2,
                new Bezier3(p.Position, p.Position + p.OutwardTangent * (length / 3), end - p.OutwardTangent * (length / 3), end),
                false, p.NodeIndex + 2000, 3) });
        }).ToArray();

    [Theory]
    [InlineData(InterchangeType.Cloverleaf)] [InlineData(InterchangeType.Stack)] [InlineData(InterchangeType.Turbine)]
    [InlineData(InterchangeType.Trumpet)] [InlineData(InterchangeType.DirectionalT)] [InlineData(InterchangeType.SemiDirectionalT)]
    [InlineData(InterchangeType.Wye)] [InlineData(InterchangeType.CompactGrid)] [InlineData(InterchangeType.Roundabout)]
    public void EveryExistingFamilyProducesARealRoadCertifiedCandidate(InterchangeType type)
    {
        var arms = EndpointMixedRoadSetupTests.Arms(type); var catalog = EndpointMixedRoadSetupTests.Catalog();
        var setup = EndpointMixedRoadSetupTests.Setup(arms, catalog, type); var traces = Traces(arms);
        var result = Search(arms, traces, setup);
        Assert.True(result.Failure == RetreatSearchFailure.None, $"{type}: {result.Failure}: {result.ClearanceDetail}; {result.SupportDetail}");
        AssertRealResult(result, arms, traces, catalog, setup);
    }

    [Theory]
    [InlineData(true, 1)] [InlineData(false, 1)] [InlineData(true, 3)] [InlineData(false, 3)]
    public void MixedRealCountsRetainTheirOwnExactOriginalCutIdentities(bool rht, int mask)
    {
        var type = InterchangeType.Stack; var arms = EndpointMixedRoadSetupTests.Arms(type, rht, twoWayMask: mask);
        var catalog = EndpointMixedRoadSetupTests.Catalog(); var traces = Traces(arms, curved: true);
        var setup = EndpointMixedRoadSetupTests.Setup(arms, catalog, type, rht);
        var result = Search(arms, traces, setup);
        Assert.True(result.Failure == RetreatSearchFailure.None, $"{result.Failure}: {result.ClearanceDetail}; {result.SupportDetail}");
        AssertRealResult(result, arms, traces, catalog, setup);
    }

    public static IEnumerable<object[]> CurvedMixedFamilies() => new[] { InterchangeType.Cloverleaf,
        InterchangeType.Stack, InterchangeType.Turbine, InterchangeType.Trumpet, InterchangeType.DirectionalT,
        InterchangeType.SemiDirectionalT, InterchangeType.Wye, InterchangeType.CompactGrid, InterchangeType.Roundabout }
        .SelectMany(type => new[] { true, false }.Select(rht => new object[] { type, rht }));

    [Theory]
    [MemberData(nameof(CurvedMixedFamilies))]
    public void EveryFamilyPreservesMixedCurvedSourceCutsInBothTrafficHands(InterchangeType type, bool rht)
    {
        var arms = EndpointMixedRoadSetupTests.Arms(type, rht, twoWayMask: 5);
        var catalog = EndpointMixedRoadSetupTests.Catalog(); var traces = Traces(arms, curved: true);
        var setup = EndpointMixedRoadSetupTests.Setup(arms, catalog, type, rht);
        var result = Search(arms, traces, setup);
        Assert.True(result.Failure == RetreatSearchFailure.None, $"{type}/{rht}: {result.Failure}: {result.ClearanceDetail}; {result.SupportDetail}");
        AssertRealResult(result, arms, traces, catalog, setup);
        Assert.Equal(type is InterchangeType.Trumpet or InterchangeType.DirectionalT or InterchangeType.SemiDirectionalT or InterchangeType.Wye ? 4 : 6,
            result.Cuts.Count);
    }

    [Theory]
    [InlineData(InterchangeType.Cloverleaf)] [InlineData(InterchangeType.Stack)] [InlineData(InterchangeType.Turbine)]
    [InlineData(InterchangeType.Trumpet)] [InlineData(InterchangeType.DirectionalT)] [InlineData(InterchangeType.SemiDirectionalT)]
    [InlineData(InterchangeType.Wye)] [InlineData(InterchangeType.Roundabout)]
    public void NativeTwoLanesPerDirectionDoNotRequireAnEightLaneSourceRoad(InterchangeType type)
    {
        var arms = EndpointMixedRoadSetupTests.Arms(type, lanes: 2, twoWayMask: 5);
        var catalog = EndpointMixedRoadSetupTests.Catalog(2); var traces = Traces(arms);
        var setup = EndpointMixedRoadSetupTests.Setup(arms, catalog, type);
        var result = Search(arms, traces, setup);
        Assert.True(result.Failure == RetreatSearchFailure.None, $"{type}: {result.Failure}: {result.ClearanceDetail}; {result.SupportDetail}");
        AssertRealResult(result, arms, traces, catalog, setup);
    }

    [Theory]
    [InlineData(InterchangeType.Cloverleaf, true)] [InlineData(InterchangeType.Cloverleaf, false)]
    [InlineData(InterchangeType.Stack, true)] [InlineData(InterchangeType.Stack, false)]
    [InlineData(InterchangeType.Trumpet, true)] [InlineData(InterchangeType.Trumpet, false)]
    [InlineData(InterchangeType.Roundabout, true)] [InlineData(InterchangeType.Roundabout, false)]
    public void ExistingVariantOptionsRemainAvailableWithMixedSources(InterchangeType type, bool rht)
    {
        var arms = EndpointMixedRoadSetupTests.Arms(type, rht, twoWayMask: 5);
        var catalog = EndpointMixedRoadSetupTests.Catalog().Append(new RoadInfo("SharedRamp", true, 1, 1, 12)
            { ElevatedLength = 80, PillarWidth = 3 }).ToArray();
        var setup = EndpointMixedRoadSetup.Create(arms, catalog, type, rht, 0, 0, 0,
            hugLoop: type == InterchangeType.Cloverleaf, leftExit: type == InterchangeType.Stack,
            trumpetLoopForExit: false, racetrack: type == InterchangeType.Roundabout);
        Assert.True(setup.Success, setup.Diagnostic);
        var traces = Traces(arms, curved: true); var result = Search(arms, traces, setup);
        Assert.True(result.Failure == RetreatSearchFailure.None, $"{type}/{rht}: {result.Failure}: {result.ClearanceDetail}; {result.SupportDetail}");
        AssertRealResult(result, arms, traces, catalog, setup);
        if (type == InterchangeType.Cloverleaf) Assert.Contains(result.Spatial!.Segments, s => s.Prefab == "SharedRamp");
    }

    [Fact]
    public void InsufficientTotalRetreatDoesNotBorrowSpaceOutsideTheUserLimit()
    {
        var type = InterchangeType.Stack; var arms = EndpointMixedRoadSetupTests.Arms(type);
        var setup = EndpointMixedRoadSetupTests.Setup(arms, EndpointMixedRoadSetupTests.Catalog(), type);
        var result = Search(arms, Traces(arms), setup, limit: 100);
        Assert.NotEqual(RetreatSearchFailure.None, result.Failure); Assert.Null(result.Spatial); Assert.Empty(result.Cuts);
        Assert.InRange(result.Attempts, 0, 128); Assert.NotEmpty(result.ClearanceDetail);
    }

    [Fact]
    public void CompleteTransitionCertificateConsumesTheSharedBudget()
    {
        var type = InterchangeType.Stack; var arms = EndpointMixedRoadSetupTests.Arms(type);
        var setup = EndpointMixedRoadSetupTests.Setup(arms, EndpointMixedRoadSetupTests.Catalog(), type);
        var result = Search(arms, Traces(arms), setup, budget: 1);
        Assert.Equal(RetreatSearchFailure.BudgetExceeded, result.Failure); Assert.Null(result.Spatial);
        Assert.InRange(result.Attempts, 0, 1);
    }

    [Fact]
    public void CurvedNominalThousandMetreCutCannotExceedTheTrueRetreatLimit()
    {
        var curve = new Bezier3(Vec3.Zero, new Vec3(1300f / 3, 0, 0),
            new Vec3(2600f / 3, 0, 1000), new Vec3(1300, 0, 1000));
        var source = new SelectedEndpoint(1, 1, curve.P0, curve.Derivative(0), false, false, 24, 2, "TwoWay2");
        var trace = new RoadApproachTrace(source, new[] { new RoadTracePiece(2, 1, curve, false, 3, 1) });
        var cut = RoadRetreat.Cut(trace, 1000);
        Assert.Equal(RoadRetreatFailure.None, cut.Failure);
        Assert.True(cut.ModifiedCurves.Sum(c => c.Length(65536)) > 1000);
        var method = typeof(EndpointMixedRoadSearch).GetMethod("WithinRetreatLimit", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        Assert.False((bool)method!.Invoke(null, new object[] { cut, 1000f })!);
        Assert.True((bool)method.Invoke(null, new object[] { cut, 1001f })!);
    }

    [Fact]
    public void IncidentTerminalEnvelopesDeferToNativeChecksButUnrelatedLowerRoadStillBlocks()
    {
        var arms = EndpointMixedRoadSetupTests.Arms(InterchangeType.Cloverleaf, twoWayMask: 5);
        var catalog = EndpointMixedRoadSetupTests.Catalog().Append(new RoadInfo("SharedRamp", true, 1, 1, 12)
            { ElevatedLength = 80, PillarWidth = 3 }).ToArray();
        var setup = EndpointMixedRoadSetup.Create(arms, catalog, InterchangeType.Cloverleaf, true, 0, 0, 0, hugLoop: true);
        var core = RetreatCloverleafSearch.Search(setup.ProxyArms, Traces(setup.ProxyArms.ToArray()), true,
            setup.MainHeight, setup.CrossHeight, setup.Ramp!.Width, .16f, 1000, design: setup.Design);
        Assert.Equal(RetreatSearchFailure.None, core.Failure);
        var explicitCore = (SpatialCandidateResult)typeof(EndpointMixedRoadSearch).GetMethod("ExplicitCandidate",
            BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[] { core.Spatial!, setup })!;
        Assert.NotEqual(CurveClearanceResult.Clear, EndpointNetworkSupports.Check(explicitCore, setup.Design!.BridgeRules,
            setup.Ramp, out _, setup.Design.SupportRules));
        var check = EndpointNetworkSupports.Check(explicitCore, setup.Design.BridgeRules, setup.Ramp, out string detail,
            setup.Design.SupportRules, nativeJoinedNodes: true);
        Assert.True(check == CurveClearanceResult.Clear, detail);
        var upper = explicitCore.Segments.Last(s => s.PathId == "Hug3-LoopIn");
        var node = upper.EndNode;
        var unrelated = new SpatialSegment(0, CloverEdgeKind.Main,
            Bezier3.Line(new Vec3(node.X - 40, 0, node.Z), new Vec3(node.X + 40, 0, node.Z)),
            prefab: setup.Ramp.Name, roadWidth: setup.Ramp.Width, pathId: "Unrelated road below real fork");
        var blocked = new SpatialCandidateResult(SpatialCandidateFailure.None, explicitCore.Segments.Append(unrelated));
        Assert.NotEqual(CurveClearanceResult.Clear, EndpointNetworkSupports.Check(blocked, setup.Design.BridgeRules,
            setup.Ramp, out _, setup.Design.SupportRules, nativeJoinedNodes: true));
    }

    private static void AssertRealResult(RetreatSearchResult result, EndpointArm[] arms, RoadApproachTrace[] traces, RoadInfo[] catalog, object setup)
    {
        Assert.InRange(result.Attempts, 1, 128); Assert.NotNull(result.Spatial);
        Assert.Equal(arms.Sum(a => a.Endpoints.Count), result.Cuts.Count);
        var sourcePorts = arms.SelectMany(a => a.Endpoints).ToArray();
        Assert.All(result.Arms.SelectMany(a => a.Endpoints), p => Assert.Contains(sourcePorts,
            original => original.NodeIndex == p.NodeIndex && original.NodeVersion == p.NodeVersion && original.Prefab == p.Prefab && original.OneWay == p.OneWay));
        foreach (var port in result.Arms.SelectMany(a => a.Endpoints))
        {
            var boundary = Assert.Single(result.Spatial!.Segments.Where(s => s.Prefab == port.Prefab
                && (port.OneWay && !port.TravelOutward ? s.WorldCurve.P0 == port.Position : s.WorldCurve.P3 == port.Position)));
            var tangent = boundary.WorldCurve.Derivative(port.OneWay && !port.TravelOutward ? 0 : 1);
            var expected = port.OneWay && !port.TravelOutward ? -port.OutwardTangent : port.OutwardTangent;
            Assert.True(Vec3.Distance(Vec3.Normalize(tangent), Vec3.Normalize(expected)) < .0001f);
        }
        Assert.All(result.Cuts, cut =>
        {
            var trace = Assert.Single(traces.Where(t => t.Origin.NodeIndex == cut.Port!.NodeIndex));
            Assert.All(cut.Consumed, p => Assert.Contains(p, trace.Pieces));
            Assert.InRange(cut.Distance, 0, 1000); Assert.True(EndpointApproachGeometry.HasRetainedRoom(cut));
        });
        var consumed = result.Cuts.SelectMany(c => c.Consumed).Select(p => (p.EdgeIndex, p.EdgeVersion)).ToArray();
        Assert.Equal(consumed.Length, consumed.Distinct().Count());
        Assert.All(result.Spatial!.Segments, s =>
        {
            var road = Assert.Single(catalog.Where(r => r.Name == s.Prefab));
            Assert.Equal(road.Width, s.RoadWidth); Assert.False(string.IsNullOrWhiteSpace(s.PathId));
            Assert.Equal(VerticalProfileFailure.None, VerticalProfile.CheckGrade(s.WorldCurve, .16f));
        });
        Assert.Contains(result.Spatial.Segments, s => sourcePorts.Any(p => !p.OneWay && p.Prefab == s.Prefab) && s.Kind == CloverEdgeKind.Main);
        float clearance = (float)setup.GetType().GetProperty("RequiredClearance")!.GetValue(setup)!;
        var design = (CloverleafRoadDesign)setup.GetType().GetProperty("Design")!.GetValue(setup)!;
        var ramp = (RoadInfo)setup.GetType().GetProperty("Ramp")!.GetValue(setup)!;
        Assert.Equal(CurveClearanceResult.Clear, EndpointNetworkClearance.Check(result.Spatial, clearance, out _));
        Assert.Equal(CurveClearanceResult.Clear, EndpointNetworkSupports.Check(result.Spatial, design.BridgeRules, ramp, out _, design.SupportRules,
            nativeJoinedNodes: true));
        Assert.False(result.Spatial.IsPlacementReady);
    }
}
