using InterchangeMod.Generation.Endpoints;
using InterchangeMod.Generation.Geometry;
using InterchangeMod.Generation.Roads;

namespace InterchangeMod.Generation.Tests;

public sealed class EndpointWyeCoreTests
{
    public static IEnumerable<object[]> ValidCases()
    {
        foreach (bool rht in new[] { true, false })
        {
            yield return new object[] { rht, 2, 120f, 240f, .08f, 100f };
            yield return new object[] { rht, 4, 120f, 240f, .16f, 100f };
            yield return new object[] { rht, 6, 120f, 240f, .30f, 100f };
            yield return new object[] { rht, 4, 90f, 210f, .08f, 100f };
            yield return new object[] { rht, 4, 150f, 270f, .30f, 100f };
            yield return new object[] { rht, 4, 120f, 240f, .16f, 110f };
        }
    }

    // Catches missing movement chains, a T-style 0/2 main grouping, mirrored
    // traffic, loose grade checks and publishing geometry without certificates.
    [Theory]
    [MemberData(nameof(ValidCases))]
    public void BuildsSixDirectedMovementsWithExactPortsAndCertifiedGeometry(bool rht, int lanes,
        float angle1, float angle2, float grade, float height)
    {
        var arms = Arms(rht, lanes, angle1, angle2);
        var ramp = Ramp(lanes);
        var result = Create(arms, rht, height, height + 10, ramp, grade, new CloverleafRoadDesign(), 100);
        AssertCore(result, arms, ramp, grade, height);
        Assert.Equal(height + 10, result.Segments.Max(s => Math.Max(s.WorldCurve.P0.Y, s.WorldCurve.P3.Y)), 3);
    }

    // Reindexing the three equal-status arms must retain actual widths, prefabs,
    // support metadata and external tangents, even if arm 0 is elsewhere.
    [Theory]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    public void PreservesOriginalGroupsWhenPosedAndPermutedWithDifferentWidths(bool rht, int shift)
    {
        var source = Arms(rht, 4, 100, 230, widths: new[] { 20f, 24f, 28f }, rotation: 73);
        var arms = source.Select((a, i) => new EndpointArm((i + shift) % 3, a.Endpoints.Reverse())).Reverse().ToArray();
        var ramp = Ramp(4);
        var design = new CloverleafRoadDesign(bridgeRules: arms.Select(a => new EndpointBridgeRule(a.Index, 80 + a.Index, null,
            a.Endpoints[0].Width, 3 + a.Index * .1f)), supportRules: arms.Select(a =>
            new EndpointPrefabSupportRule(a.Endpoints[0].Prefab, 80 + a.Index, null, 3)).Append(new EndpointPrefabSupportRule(ramp.Name, 80, null, 3)));
        var result = Create(arms, rht, 110, 120, ramp, .16f, design, 100);
        AssertCore(result, arms, ramp, .16f, 110, design);
        Assert.True(result.ReferenceCenter.HasValue);
        Assert.True(Vec3.Distance(result.ReferenceCenter!.Value, new Vec3(450, 0, -320)) < .02f);
        Assert.Contains(result.Segments, s => s.WorldCurve.P0 != s.StartNode || s.WorldCurve.P3 != s.EndNode);
    }

    [Fact]
    public void DoesNotUseOriginalEndpointHeightsAsInternalRoadLevels()
    {
        var arms = Arms();
        arms = arms.Select(a => new EndpointArm(a.Index, a.Endpoints.Select(e => Copy(e, position: e.Position + new Vec3(0, a.Index * 25 + (e.TravelOutward ? 4 : 0), 0))))).ToArray();
        AssertCore(Create(arms, true, 102, 112, Ramp(4), .16f, new CloverleafRoadDesign(), 100), arms, Ramp(4), .16f, 102);
    }

    public static IEnumerable<object[]> InvalidInputs()
    {
        foreach (string invalid in new[] { "single", "duplicate", "pair-prefab", "pair-width", "pair-lanes", "unequal-lanes", "odd-lanes",
            "ramp-lanes", "ramp-wide", "angle", "overlap", "height-gap", "terrain", "grade", "balance", "support", "support-missing" })
            yield return new object[] { invalid };
    }

    // Catches partial output, silently dropped lanes, accepting a ground crossing,
    // and the old opposite-road equality rule accidentally applied to Y roads.
    [Theory]
    [MemberData(nameof(InvalidInputs))]
    public void RejectsInvalidOrUnsupportedInputWithoutAnyPartialNetwork(string invalid)
    {
        var arms = Arms(); var ramp = Ramp(4); float upper = 110, grade = .16f, terrain = 100;
        var design = new CloverleafRoadDesign();
        var first = arms[0].Endpoints[0];
        switch (invalid)
        {
            case "single": arms[0] = new EndpointArm(0, new[] { Copy(first, oneWay: false) }); break;
            case "duplicate": arms[2] = new EndpointArm(0, arms[2].Endpoints); break;
            case "pair-prefab": arms[0] = new EndpointArm(0, new[] { Copy(first, prefab: "Other"), arms[0].Endpoints[1] }); break;
            case "pair-width": arms[0] = new EndpointArm(0, new[] { Copy(first, width: 21), arms[0].Endpoints[1] }); break;
            case "pair-lanes": arms[0] = new EndpointArm(0, new[] { Copy(first, lanes: 6), arms[0].Endpoints[1] }); break;
            case "unequal-lanes": arms[0] = new EndpointArm(0, arms[0].Endpoints.Select(e => Copy(e, lanes: 6))); break;
            case "odd-lanes": arms = arms.Select(a => new EndpointArm(a.Index, a.Endpoints.Select(e => Copy(e, lanes: 3)))).ToArray(); break;
            case "ramp-lanes": ramp = Ramp(2); break;
            case "ramp-wide": ramp = new RoadInfo("WideRamp", true, 2, 0, 40); break;
            case "angle": arms = Arms(angle1: 80, angle2: 220); break;
            case "overlap": arms[0] = new EndpointArm(0, arms[0].Endpoints.Select(e => Copy(e, position: first.Position))); break;
            case "height-gap": upper = 108; break;
            case "terrain": terrain = float.NaN; break;
            case "grade": grade = .31f; break;
            case "balance": design = new CloverleafRoadDesign(new[] { new EndpointLaneBalance(0, "Reduced", 12, 2, 2) }); break;
            case "support": design = new CloverleafRoadDesign(bridgeRules: new[] { new EndpointBridgeRule(0, float.NaN, null, 20, 3) }); break;
            case "support-missing": design = new CloverleafRoadDesign(supportRules: new[] { new EndpointPrefabSupportRule(ramp.Name, 80, null, 3) }); break;
        }
        var result = Create(arms, true, 100, upper, ramp, grade, design, terrain);
        Assert.False(result.Success); Assert.NotEmpty(result.Diagnostic); Assert.Empty(result.Ports); Assert.Empty(result.Segments);
    }

    [Fact]
    public void RejectsActualRampPillarsThatIntersectTheLowerRoad()
    {
        var ramp = Ramp(4); ramp.PillarWidth = 250;
        var result = Create(Arms(), true, 100, 110, ramp, .16f, new CloverleafRoadDesign(), 100);
        Assert.False(result.Success); Assert.Contains("support", result.Diagnostic, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(result.Ports); Assert.Empty(result.Segments);
    }

    // Fixed 8.5m checks miss the lower clearance top and the upper deck bottom.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RejectsTenMetreLayerGapWhenActualRampOrRoadEnvelopesRequireForty(bool fullRoad)
    {
        var ramp = fullRoad ? Ramp(4) : new RoadInfo("DeepRamp", true, 2, 0, 12,
            groundHeight: new HeightRange(0, 20), elevatedHeight: new HeightRange(-20, 20));
        var design = fullRoad ? new CloverleafRoadDesign(bridgeRules: new[] {
            new EndpointBridgeRule(1, 80, null, 20, 3, new HeightRange(0, 20), new HeightRange(-20, 20)) }) : new CloverleafRoadDesign();
        var result = Create(Arms(), true, 100, 110, ramp, .16f, design, 100);
        Assert.False(result.Success); Assert.Empty(result.Ports); Assert.Empty(result.Segments);
        Assert.Equal(40, RequiredClearance(design, ramp));
    }

    [Fact]
    public void UsesEightPointFiveMetreClearanceFloorForUnknownRoadEnvelopes()
        => Assert.Equal(8.5f, RequiredClearance(new CloverleafRoadDesign(), Ramp(4)));

    [Theory]
    [InlineData(float.NaN, 20)]
    [InlineData(-20, float.PositiveInfinity)]
    [InlineData(20, -20)]
    public void RejectsMalformedActualRampEnvelopes(float min, float max)
    {
        var ramp = new RoadInfo("InvalidEnvelope", true, 2, 0, 12, groundHeight: new HeightRange(min, max));
        var result = Create(Arms(), true, 100, 110, ramp, .16f, new CloverleafRoadDesign(), 100);
        Assert.False(result.Success); Assert.Empty(result.Ports); Assert.Empty(result.Segments);
        Assert.True(float.IsNaN(RequiredClearance(new CloverleafRoadDesign(), ramp)));
    }

    [Fact]
    public void RejectsReverseOnlyRampBecauseGeneratedCurveDirectionsRequireForwardLanes()
    {
        var ramp = new RoadInfo("ReverseRamp", true, 0, 2, 12);
        var result = Create(Arms(), true, 100, 110, ramp, .16f, new CloverleafRoadDesign(), 100);
        Assert.False(result.Success); Assert.Empty(result.Ports); Assert.Empty(result.Segments);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CandidateEnumerationIsFiniteAndEveryPublishedAlternativeIsCertified(bool rht)
    {
        var arms = Arms(rht); var ramp = Ramp(4);
        var candidates = EndpointWyeCore.Candidates(arms, rht, 100, 110, ramp, .16f, new CloverleafRoadDesign(), 100).ToArray();
        Assert.InRange(candidates.Length, 1, 5);
        Assert.Contains(candidates, c => c.Success);
        foreach (var candidate in candidates)
        {
            if (candidate.Success) AssertCore(candidate, arms, ramp, .16f, 100);
            else { Assert.NotEmpty(candidate.Diagnostic); Assert.Empty(candidate.Segments); Assert.Empty(candidate.Ports); }
        }
    }

    private static void AssertCore(EndpointStackCoreResult result, EndpointArm[] arms, RoadInfo ramp, float grade, float height, CloverleafRoadDesign? design = null)
    {
        Assert.True(result.Success, result.Diagnostic);
        Assert.Equal(6, result.Ports.Count);
        Assert.Equal(6, result.Segments.Where(s => s.Kind != CloverEdgeKind.Main).Select(s => s.PathId).Distinct().Count());
        Assert.Equal(12, result.Segments.Select(s => s.PathId).Distinct().Count());
        foreach (var path in result.Segments.GroupBy(s => s.PathId))
        {
            var pieces = path.ToArray();
            for (int i = 1; i < pieces.Length; i++) Assert.Equal(pieces[i - 1].EndNode, pieces[i].StartNode);
            if (pieces[0].Kind == CloverEdgeKind.Main) continue;
            var source = arms.Single(a => a.Index == pieces[0].SourceArm).Endpoints[0];
            var incomingRoad = Assert.Single(result.Segments.Where(s => s.Kind == CloverEdgeKind.Main && s.EndNode == pieces[0].StartNode));
            Assert.Equal(source.Prefab, incomingRoad.Prefab);
            var outgoingRoad = Assert.Single(result.Segments.Where(s => s.Kind == CloverEdgeKind.Main && s.StartNode == pieces[^1].EndNode));
            foreach (var boundary in new[] { (Curve: pieces[0].WorldCurve.P0, Node: pieces[0].StartNode, Road: incomingRoad),
                (Curve: pieces[^1].WorldCurve.P3, Node: pieces[^1].EndNode, Road: outgoingRoad) })
            {
                var delta = boundary.Curve - boundary.Node;
                Assert.Equal((boundary.Road.RoadWidth!.Value - ramp.Width) / 2, MathF.Sqrt(delta.X * delta.X + delta.Z * delta.Z), 2);
                Assert.Equal(0, delta.Y, 3);
            }
        }
        foreach (var port in result.Ports)
        {
            var original = arms.Single(a => a.Index == port.Arm).Endpoints.Single(e => e.TravelOutward == port.TravelOutward);
            Assert.Equal(original.Prefab, port.Prefab); Assert.Equal(original.Width, port.Width); Assert.Equal(height, port.Position.Y, 3);
            Assert.Equal(1, result.Segments.Count(s => s.StartNode == port.Position || s.EndNode == port.Position));
            var terminal = Assert.Single(result.Segments.Where(s => s.StartNode == port.Position || s.EndNode == port.Position));
            Assert.Equal(port.Position, port.TravelOutward ? terminal.WorldCurve.P3 : terminal.WorldCurve.P0);
            Assert.True(Directions.Dot(port.Tangent, original.OutwardTangent * (port.TravelOutward ? 1 : -1)) > .99999f);
            if (port.TravelOutward) continue;
            var reachable = Reachable(result.Segments, port.Position);
            foreach (var output in result.Ports.Where(p => p.TravelOutward))
                Assert.Equal(output.Arm != port.Arm, reachable.Contains(output.Position));
        }
        Assert.All(result.Segments, s => {
            Assert.Equal(VerticalProfileFailure.None, VerticalProfile.CheckGrade(s.WorldCurve, grade));
            Assert.Equal(s.Kind == CloverEdgeKind.Main ? arms.Single(a => a.Index == s.SourceArm).Endpoints[0].Width : ramp.Width, s.RoadWidth);
        });
        var candidate = new SpatialCandidateResult(SpatialCandidateFailure.None, result.Segments);
        Assert.True(EndpointNetworkClearance.Check(candidate, 8.5f, out var decks) == CurveClearanceResult.Clear, decks);
        Assert.True(EndpointNetworkSupports.Check(candidate, design?.BridgeRules ?? Array.Empty<EndpointBridgeRule>(), ramp, out var supports, design?.SupportRules) == CurveClearanceResult.Clear, supports);
    }

    private static EndpointStackCoreResult Create(EndpointArm[] arms, bool rht, float height, float upper, RoadInfo ramp,
        float grade, CloverleafRoadDesign design, float terrain)
        => EndpointWyeCore.Create(arms, rht, height, upper, ramp, grade, design, terrain);

    private static float RequiredClearance(CloverleafRoadDesign design, RoadInfo ramp)
        => EndpointWyeCore.RequiredClearance(design, ramp);

    private static RoadInfo Ramp(int lanes) => new($"Ramp{lanes / 2}", true, lanes / 2, 0, 4 + lanes * 2) { ElevatedLength = 80, PillarWidth = 3 };
    private static SelectedEndpoint Copy(SelectedEndpoint e, Vec3? position = null, bool? oneWay = null, float? width = null, int? lanes = null, string? prefab = null)
        => new(e.NodeIndex, e.NodeVersion, position ?? e.Position, e.OutwardTangent, oneWay ?? e.OneWay, e.TravelOutward, width ?? e.Width, lanes ?? e.Lanes, prefab ?? e.Prefab);
    private static HashSet<Vec3> Reachable(IEnumerable<SpatialSegment> roads, Vec3 start)
    {
        var seen = new HashSet<Vec3> { start }; var queue = new Queue<Vec3>(); queue.Enqueue(start);
        while (queue.Count > 0) { var at = queue.Dequeue(); foreach (var s in roads.Where(s => s.StartNode == at)) if (seen.Add(s.EndNode)) queue.Enqueue(s.EndNode); }
        return seen;
    }

    internal static EndpointArm[] Arms(bool rht = true, int lanes = 4, float angle1 = 120, float angle2 = 240, float[]? widths = null, float rotation = 23)
    {
        return Enumerable.Range(0, 3).Select(i => {
            float width = widths?[i] ?? (8 + lanes * 3);
            var axis = Directions.FromAngle(rotation + (i == 0 ? 0 : i == 1 ? angle1 : angle2));
            var side = Directions.RightOf(axis) * (rht ? 1 : -1);
            var mid = new Vec3(450, 100, -320) + axis * 1800;
            return new EndpointArm(i, new[] { false, true }.Select(outward => new SelectedEndpoint(i * 10 + (outward ? 2 : 1), 1,
                mid + side * ((width + 8) / 2) * (outward ? 1 : -1), axis, true, outward, width, lanes, $"Road{i}-{width}")));
        }).ToArray();
    }
}
