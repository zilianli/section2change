using System.Reflection;
using System.Text.Json;
using InterchangeMod.Generation.Endpoints;
using InterchangeMod.Generation.Geometry;
using InterchangeMod.Generation.Roads;

namespace InterchangeMod.Generation.Tests;

public class NativeTwoWayRoundaboutRegressionTests
{
    private static RoadApproachTrace[] Traces()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(
            "InterchangeMod.Generation.Tests.Fixtures.twoway-roundabout-20261102.json")!;
        using var document = JsonDocument.Parse(stream);
        Vec3 V(JsonElement e) => new(e.GetProperty("x").GetSingle(), e.GetProperty("y").GetSingle(), e.GetProperty("z").GetSingle());
        return document.RootElement.EnumerateArray().Select(e =>
        {
            var o = e.GetProperty("origin");
            var origin = new SelectedEndpoint(o.GetProperty("nodeIndex").GetInt32(), o.GetProperty("nodeVersion").GetInt32(),
                V(o.GetProperty("position")), V(o.GetProperty("outwardTangent")), o.GetProperty("oneWay").GetBoolean(),
                o.GetProperty("travelOutward").GetBoolean(), o.GetProperty("width").GetSingle(),
                o.GetProperty("lanes").GetInt32(), o.GetProperty("prefab").GetString()!);
            return new RoadApproachTrace(origin, e.GetProperty("pieces").EnumerateArray().Select(p =>
            {
                var c = p.GetProperty("curve");
                return new RoadTracePiece(p.GetProperty("edgeIndex").GetInt32(), p.GetProperty("edgeVersion").GetInt32(),
                    new Bezier3(V(c.GetProperty("p0")), V(c.GetProperty("p1")), V(c.GetProperty("p2")), V(c.GetProperty("p3"))),
                    p.GetProperty("reversed").GetBoolean(), p.GetProperty("outerNodeIndex").GetInt32(), p.GetProperty("outerNodeVersion").GetInt32());
            }));
        }).ToArray();
    }

    // Captured curves/identities are exact. This subset uses logged road widths,
    // lanes, ranges, spans and slopes; the unlogged pillar width assumes 4m.
    private static RoadInfo[] Catalog() => new[] {
        Road("XL Road Divided", 4, 4, 40), Road("Medium Road Oneway", 4, 0, 24),
        Road("Large Road Oneway", 6, 0, 32), Road("Small Road Oneway - 3 lanes", 3, 0, 16),
        Road("Small Road Oneway", 2, 0, 16), Road("Alley Oneway", 1, 0, 8, .30f),
        new RoadInfo("ExtradosedBridge04", false, 4, 0, 19, groundHeight: new HeightRange(-1, 5),
            elevatedHeight: new HeightRange(-2, 5), maxSlope: .20f)
            { ElevatedLength = 448, MiddlePillarSpacing = 0, PillarWidth = 56.1f },
        Road("HarborGateRoadOneway03", 4, 0, 32) };
    private static RoadInfo Road(string name, int forward, int backward, float width, float slope = .20f) =>
        new(name, false, forward, backward, width, groundHeight: new HeightRange(-1.8f, 5),
            elevatedHeight: new HeightRange(-2, 5), maxSlope: slope) { ElevatedLength = 80, PillarWidth = 4 };

    [Fact]
    public void CapturedNativeJoinAcceptsFloatPrecisionWithoutChangingSourceGeometry()
    {
        var traces = Traces(); var trace = traces[1];
        var before = EndpointTraceDump.Encode(traces);
        double gap = Vec3.Distance(trace.Pieces[3].Curve.P3, trace.Pieces[4].Curve.P0);
        Assert.InRange(gap, .05001, .0501);
        Assert.All(traces, t => Assert.Equal(RoadRetreatFailure.None, RoadRetreat.Cut(t, 0).Failure));
        var cut = RoadRetreat.Cut(trace, 900);
        Assert.Equal(RoadRetreatFailure.None, cut.Failure);
        Assert.Contains(trace.Pieces[4], cut.Consumed);
        Assert.Equal(before, EndpointTraceDump.Encode(traces));
    }

    [Theory]
    [InlineData(true, false, .16f)] [InlineData(false, false, .16f)]
    [InlineData(true, true, .16f)] [InlineData(true, false, .27f)]
    public void CapturedAllTwoWayRoundaboutProducesARealSourceBoundPreview(bool rht, bool racetrack, float grade)
    {
        var traces = Traces(); var arms = traces.Select((t, i) => new EndpointArm(i, new[] { t.Origin })).ToArray();
        var setup = EndpointMixedRoadSetup.Create(arms, Catalog(), InterchangeType.Roundabout, rht, 0, 0, 478.6323f,
            "Alley Oneway", racetrack: racetrack);
        Assert.True(setup.Success, setup.Diagnostic);
        Assert.All(setup.ProxyArms.SelectMany(a => a.Endpoints), p => Assert.Equal("Medium Road Oneway", p.Prefab));
        Assert.Equal("Medium Road Oneway", setup.Ring!.Name);
        Assert.Equal("Alley Oneway", setup.Ramp!.Name); // Explicit preference is preserved.
        var result = EndpointMixedRoadSearch.Search(arms, traces, setup, grade, 1000);
        Assert.True(result.Failure == RetreatSearchFailure.None,
            $"{result.Failure}; attempts={result.Attempts}; {result.ClearanceDetail}; {result.SupportDetail}");
        Assert.Equal(4, result.Cuts.Count);
        Assert.InRange(result.Attempts, 1, 128);
        var sources = NodePreviewSources.Bind(traces, result.Cuts);
        Assert.True(NodePlacementSnapshot.Capture(sources, result.Cuts).Matches(sources, result.Cuts));
        Assert.All(result.Arms, a => Assert.False(Assert.Single(a.Endpoints).OneWay));
        Assert.Contains(result.Spatial!.Segments, s => s.Prefab == "XL Road Divided" && s.RoadWidth == 40);
        Assert.Equal(CurveClearanceResult.Clear, EndpointNetworkClearance.Check(result.Spatial, setup.RequiredClearance, out _));
        Assert.Equal(CurveClearanceResult.Clear, EndpointNetworkSupports.Check(result.Spatial, setup.Design!.BridgeRules,
            setup.Ramp!, out _, setup.Design.SupportRules, nativeJoinedNodes: true));
    }

    [Fact]
    public void TrueDisconnectionAtSameWorldScaleIsStillRejected()
    {
        var trace = Traces()[1]; var pieces = trace.Pieces.ToArray(); var old = pieces[4]; var c = old.Curve;
        pieces[4] = new RoadTracePiece(old.EdgeIndex, old.EdgeVersion,
            new Bezier3(c.P0 + new Vec3(.1f, 0, 0), c.P1, c.P2, c.P3), old.Reversed, old.OuterNodeIndex, old.OuterNodeVersion);
        Assert.Equal(RoadRetreatFailure.InvalidTrace, RoadRetreat.Cut(new RoadApproachTrace(trace.Origin, pieces), 0).Failure);
    }

    [Fact]
    public void FloatAllowanceDoesNotRelaxFrozenSourceSnapshotEquality()
    {
        var trace = Traces()[1]; var cut = RoadRetreat.Cut(trace, 300);
        Assert.Equal(RoadRetreatFailure.None, cut.Failure);
        var snapshot = NodePlacementSnapshot.Capture(new[] { trace }, new[] { cut });
        var pieces = trace.Pieces.ToArray(); var old = pieces[4]; var c = old.Curve;
        pieces[4] = new RoadTracePiece(old.EdgeIndex, old.EdgeVersion,
            new Bezier3(c.P0, c.P1 + new Vec3(.001f, 0, 0), c.P2, c.P3), old.Reversed, old.OuterNodeIndex, old.OuterNodeVersion);
        Assert.False(snapshot.Matches(new[] { new RoadApproachTrace(trace.Origin, pieces) }, new[] { cut }));
    }

    [Theory]
    [InlineData(48)] [InlineData(70)] [InlineData(75)]
    public void NativeConversionMovesBeyondAShortTailWithoutChangingInternalPorts(float minimum)
    {
        var trace = ShortPieces();
        var method = typeof(EndpointMixedRoadSearch).GetMethod("CutNativeBeyond", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        var cut = (RoadRetreatResult)method!.Invoke(null, new object[] { trace, minimum, 130f })!;
        Assert.Equal(RoadRetreatFailure.None, cut.Failure);
        Assert.Equal(90, cut.Distance);
        Assert.True(EndpointApproachGeometry.HasRetainedRoom(cut));
        Assert.Equal(2, cut.Consumed.Count);
        Assert.Equal(trace.Origin.NodeIndex, cut.Port!.NodeIndex);
        Assert.Equal(trace.Pieces[1].Curve.P3, cut.Remainder!.Value.P3);
    }

    [Theory]
    [InlineData(79, false)] [InlineData(80, true)]
    public void NativeTailExtensionNeverBorrowsBeyondTheRealUserLimit(float limit, bool succeeds)
    {
        var method = typeof(EndpointMixedRoadSearch).GetMethod("CutNativeBeyond", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        var cut = (RoadRetreatResult)method!.Invoke(null, new object[] { ShortPieces(), 70f, limit })!;
        Assert.Equal(succeeds, EndpointApproachGeometry.HasRetainedRoom(cut));
        Assert.InRange(cut.Distance, 0, limit);
        if (succeeds) { Assert.Equal(80, cut.Distance); Assert.Equal(20, cut.BindingNodeIndex); }
    }

    private static RoadApproachTrace ShortPieces()
    {
        var port = new SelectedEndpoint(10, 1, new Vec3(0, 10, 0), Vec3.UnitX, false, false, 40, 4, "XL Road Divided");
        return new RoadApproachTrace(port, new[] {
            new RoadTracePiece(100, 1, Bezier3.Line(port.Position, new Vec3(80, 10, 0)), false, 20, 1),
            new RoadTracePiece(101, 1, Bezier3.Line(new Vec3(80, 10, 0), new Vec3(160, 10, 0)), true, 21, 1) });
    }

    [Fact]
    public void ExplicitExistingSpecialCarriagewayIsPreservedAsTheOppositeCounterpart()
    {
        var traces = Traces(); var catalog = Catalog();
        var arms = traces.Select((t, i) => new EndpointArm(i, new[] { t.Origin })).ToArray();
        var center = arms[2].Endpoints[0];
        arms[2] = new EndpointArm(2, new[] { false, true }.Select(outward => new SelectedEndpoint(40000 + (outward ? 1 : 0),
            1, center.Position + new Vec3(0, 0, outward ? 20 : -20), center.OutwardTangent, true, outward, 19, 4, "ExtradosedBridge04")));
        var setup = EndpointMixedRoadSetup.Create(arms, catalog, InterchangeType.Roundabout, true, 0, 0, 478.6323f, "Alley Oneway");
        Assert.True(setup.Success, setup.Diagnostic);
        Assert.All(setup.ProxyArms[0].Endpoints, p => Assert.Equal("ExtradosedBridge04", p.Prefab));
        Assert.All(setup.ProxyArms[2].Endpoints, p => Assert.Equal("ExtradosedBridge04", p.Prefab));
        Assert.All(setup.ProxyArms[1].Endpoints.Concat(setup.ProxyArms[3].Endpoints), p => Assert.Equal("Medium Road Oneway", p.Prefab));
        Assert.Equal("Medium Road Oneway", setup.Ring!.Name);
    }

    [Fact]
    public void AutomaticStreetRampAlsoUsesOrdinaryRoadsForTheCapturedScene()
    {
        var traces = Traces(); var arms = traces.Select((t, i) => new EndpointArm(i, new[] { t.Origin })).ToArray();
        var catalog = Catalog().Append(Road("Small Road Oneway - 1 lane", 1, 0, 16)).ToArray();
        var setup = EndpointMixedRoadSetup.Create(arms, catalog, InterchangeType.Roundabout, true, 0, 0, 478.6323f);
        Assert.True(setup.Success, setup.Diagnostic);
        Assert.Equal("Small Road Oneway - 1 lane", setup.Ramp!.Name);
        var result = EndpointMixedRoadSearch.Search(arms, traces, setup, .16f, 1000);
        Assert.True(result.Failure == RetreatSearchFailure.None, $"{result.Failure}: {result.ClearanceDetail}; {result.SupportDetail}");
        Assert.Equal(4, NodePreviewSources.Bind(traces, result.Cuts).Count);
    }

    [Fact]
    public void UnusableUnselectedCatalogEntryCannotBreakOrdinaryRoadFiltering()
    {
        var arms = Traces().Select((t, i) => new EndpointArm(i, new[] { t.Origin })).ToArray();
        var catalog = Catalog().Append(new RoadInfo(null!, false, 1, 0, 8)).ToArray();
        var setup = EndpointMixedRoadSetup.Create(arms, catalog, InterchangeType.Roundabout, true, 0, 0, 478.6323f, "Alley Oneway");
        Assert.True(setup.Success, setup.Diagnostic);
    }
}
