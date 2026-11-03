using InterchangeMod.Generation.Endpoints;
using InterchangeMod.Generation.Geometry;
using InterchangeMod.Generation.Roads;

namespace InterchangeMod.Generation.Tests;

public sealed class EndpointCompactGridCoreTests
{
    public static IEnumerable<object[]> ValidCases()
    {
        foreach (bool rht in new[] { true, false })
        foreach (int lanes in new[] { 4, 6 })
        foreach (float grade in new[] { .08f, .16f, .30f })
            yield return new object[] { rht, lanes, grade };
    }

    // Dropping a collector, joining turns to the wrong arm, or accepting an unsafe
    // grade/deck/support must break this externally observable network contract.
    [Theory]
    [MemberData(nameof(ValidCases))]
    public void BuildsTwentyExactChainsAndTwelveMovementsWithRealLaneBalancedRoads(bool rht, int lanes, float grade)
    {
        var arms = Arms(rht, lanes); var roads = Roads(lanes); var ramp = Ramp();
        var result = Create(arms, rht, roads, ramp, grade);
        AssertCore(result, arms, roads, ramp, grade);
        if (grade == .08f) Assert.All(result.Ports, p => Assert.InRange(Math.Max(Math.Abs(p.Position.X), Math.Abs(p.Position.Z)), 0, 500));
    }

    [Theory]
    [InlineData(true, 0)] [InlineData(true, 1)] [InlineData(true, 2)] [InlineData(true, 3)]
    [InlineData(false, 0)] [InlineData(false, 1)] [InlineData(false, 2)] [InlineData(false, 3)]
    public void PreservesArmIdentityUnderRigidPoseAndCyclicReindexing(bool rht, int shift)
    {
        var arms = Arms(rht, 4).Select(a => new EndpointArm((a.Index + shift) % 4, a.Endpoints.Select(e =>
            new SelectedEndpoint(e.NodeIndex, e.NodeVersion, e.Position.RotateY(.71f) + new Vec3(430, 23, -270),
                e.OutwardTangent.RotateY(.71f), true, e.TravelOutward, e.Width, e.Lanes, e.Prefab)).Reverse())).Reverse().ToArray();
        var result = Create(arms, rht, Roads(4), Ramp(), .16f);
        AssertCore(result, arms, Roads(4), Ramp(), .16f);
        Assert.True(Vec3.Distance(result.ReferenceCenter!.Value, new Vec3(430, 0, -270)) < .02f);
    }

    [Theory]
    [InlineData("through-lanes")] [InlineData("collector-lanes")] [InlineData("reverse")]
    [InlineData("wide")] [InlineData("missing")] [InlineData("duplicate")]
    [InlineData("opposite")] [InlineData("grade")] [InlineData("gap")]
    [InlineData("envelope")] [InlineData("bridge")] [InlineData("support-missing")]
    [InlineData("support-spacing")]
    public void RejectsInvalidRealRoadAndSupportMetadataAtomically(string invalid)
    {
        var roads = Roads(4); var arms = Arms(); var ramp = Ramp();
        float grade = .16f, gap = 10; var design = new CloverleafRoadDesign();
        switch (invalid)
        {
            case "through-lanes": roads[0] = new EndpointGridArmRoads(0, Road("Wrong", 3, 12), roads[0].Collector); break;
            case "collector-lanes": roads[0] = new EndpointGridArmRoads(0, roads[0].Through, Road("Wrong", 1, 8)); break;
            case "reverse": roads[0] = new EndpointGridArmRoads(0, new RoadInfo("Reverse", true, 0, 2, 12), roads[0].Collector); break;
            case "wide": roads[0] = new EndpointGridArmRoads(0, Road("Wide", 2, 80), roads[0].Collector); break;
            case "missing": roads = roads.Take(3).ToArray(); break;
            case "duplicate": roads[3] = new EndpointGridArmRoads(0, roads[3].Through, roads[3].Collector); break;
            case "opposite": arms[2] = new EndpointArm(2, arms[2].Endpoints.Select(e => new SelectedEndpoint(e.NodeIndex, e.NodeVersion,
                e.Position, e.OutwardTangent, true, e.TravelOutward, e.Width, e.Lanes, "Different"))); break;
            case "grade": grade = .301f; break;
            case "gap": gap = 8; break;
            case "envelope": roads[0] = new EndpointGridArmRoads(0, new RoadInfo("Bad", true, 2, 0, 12,
                groundHeight: new HeightRange(float.NaN, 8)), roads[0].Collector); break;
            case "bridge": design = new CloverleafRoadDesign(bridgeRules: new[] { new EndpointBridgeRule(0, float.NaN, null, 24) }); break;
            case "support-missing": design = new CloverleafRoadDesign(supportRules: new[] { new EndpointPrefabSupportRule("Ramp", 80, null, 3) }); break;
            case "support-spacing": roads[0].Through.MiddlePillarSpacing = -1; break;
        }
        var result = EndpointCompactGridCore.Create(arms, true, 10, gap, ramp, roads, grade, design, 0);
        Assert.False(result.Success); Assert.NotEmpty(result.Diagnostic); Assert.Empty(result.Ports); Assert.Empty(result.Segments);
    }

    [Theory]
    [InlineData("full")] [InlineData("through")] [InlineData("collector")] [InlineData("ramp")]
    public void IncludesEveryGroundAndElevatedEnvelopeInLayerClearance(string part)
    {
        var roads = Roads(4); var ramp = Ramp(); var design = new CloverleafRoadDesign();
        var deep = new RoadInfo("Deep", true, part == "ramp" ? 1 : 2, 0, part == "ramp" ? 6 : 12,
            groundHeight: new HeightRange(0, 20), elevatedHeight: new HeightRange(-20, 20));
        if (part == "full") design = new CloverleafRoadDesign(bridgeRules: new[] {
            new EndpointBridgeRule(0, 80, null, 24, 3, new HeightRange(0, 20), new HeightRange(-20, 20)) });
        if (part == "through") roads[0] = new EndpointGridArmRoads(0, deep, roads[0].Collector);
        if (part == "collector") roads[0] = new EndpointGridArmRoads(0, roads[0].Through, deep);
        if (part == "ramp") ramp = deep;
        Assert.Equal(40, EndpointCompactGridCore.RequiredClearance(roads, design, ramp));
        var result = EndpointCompactGridCore.Create(Arms(), true, 10, 10, ramp, roads, .16f, design, 0);
        Assert.False(result.Success); Assert.Empty(result.Ports); Assert.Empty(result.Segments);
    }

    [Fact]
    public void RejectsActualPiersThatCannotClearTheLowerNetwork()
    {
        var roads = Roads(4); foreach (var road in roads) road.Through.PillarWidth = 600;
        var result = Create(Arms(), true, roads, Ramp(), .16f);
        Assert.False(result.Success); Assert.Contains("support", result.Diagnostic, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(result.Segments); Assert.Empty(result.Ports);
    }

    [Fact]
    public void CandidateSearchIsFiniteAndSuccessfulAlternativesCarryFullCertificates()
    {
        var trials = EndpointCompactGridCore.Candidates(Arms(), true, 10, 10, Ramp(), Roads(4), .16f,
            new CloverleafRoadDesign(), 0).Take(25).ToArray();
        Assert.InRange(trials.Length, 1, 24); Assert.Contains(trials, x => x.Success);
        foreach (var result in trials.Where(x => x.Success)) AssertCore(result, Arms(), Roads(4), Ramp(), .16f);
    }

    [Theory]
    // 游戏内 78° 的混合道路路口需要放宽到 70°–110°（EndpointCompactGridCore.kMinGridCrossing）；相对两臂偏差仍不超过 20°
    [InlineData(true, 240f, false)] [InlineData(false, 240f, false)]
    [InlineData(true, 249.9f, false)] [InlineData(false, 249.9f, false)]
    [InlineData(true, 255f, true)] [InlineData(false, 255f, true)]
    [InlineData(true, 260f, true)] [InlineData(false, 260f, true)]
    [InlineData(true, 280f, true)] [InlineData(false, 280f, true)]
    [InlineData(true, 285f, true)] [InlineData(false, 285f, true)]
    [InlineData(true, 290.1f, false)] [InlineData(false, 290.1f, false)]
    public void EnforcesEveryAdjacentArmAngleAtBothSeventyAndOneHundredTenDegreeBoundaries(bool rht, float fourthAngle, bool valid)
    {
        var arms = Arms(rht).Select(arm => {
            if (arm.Index != 3) return arm;
            var axis = Directions.FromAngle(fourthAngle); var side = Directions.RightOf(axis) * (rht ? 1 : -1);
            return new EndpointArm(3, arm.Endpoints.Select(e => new SelectedEndpoint(e.NodeIndex, e.NodeVersion,
                axis * 700 + side * (e.TravelOutward ? 16 : -16) + new Vec3(0, 10, 0), axis, true, e.TravelOutward,
                e.Width, e.Lanes, e.Prefab)));
        }).ToArray();
        var result = Create(arms, rht, Roads(4), Ramp(), .16f);
        Assert.True(result.Success == valid, result.Diagnostic);
        if (valid) AssertCore(result, arms, Roads(4), Ramp(), .16f);
        else { Assert.Empty(result.Segments); Assert.Empty(result.Ports); }
    }

    // Reflection changes actual left/right travel while preserving the graph.
    // Each turn's metadata must agree with the signed world-space tangent turn.
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void ClassifiesTurnsByPhysicalWorldHandednessAfterTrafficReflection(bool rht)
    {
        var result = Create(Arms(rht), rht, Roads(4), Ramp(), .16f);
        Assert.True(result.Success, result.Diagnostic);
        var turns = result.Segments.Where(s => s.PathId.StartsWith("GridLeft") || s.PathId.StartsWith("GridRight"))
            .GroupBy(s => s.PathId).ToArray();
        Assert.Equal(8, turns.Length);
        foreach (var turn in turns)
        {
            var from = turn.First().WorldCurve.Derivative(0); var to = turn.Last().WorldCurve.Derivative(1);
            bool physicalLeft = from.X * to.Z - from.Z * to.X > 0;
            Assert.True(physicalLeft == turn.Key.StartsWith("GridLeft"), $"{turn.Key} has the wrong world turn label.");
            Assert.All(turn, piece => Assert.Equal(physicalLeft ? CloverEdgeKind.LeftRamp : CloverEdgeKind.DirectRamp, piece.Kind));
        }
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void BuildsSixLaneRoadsWithTwoLaneTurnsAndFourLaneCollectors(bool rht)
    {
        var arms = Arms(rht, 6); var ramp = Road("Ramp2", 2, 12);
        var roads = Enumerable.Range(0, 4).Select(arm => new EndpointGridArmRoads(arm,
            Road("Through2", 2, 12), Road("Collector4", 4, 20))).ToArray();
        var result = Create(arms, rht, roads, ramp, .16f);
        AssertCore(result, arms, roads, ramp, .16f);
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void BuildsDifferentRoadFamiliesWithActualNonDefaultBridgeSupports(bool rht)
    {
        var narrow = Arms(rht, 4); var wide = Arms(rht, 6);
        var arms = Enumerable.Range(0, 4).Select(i => i % 2 == 0 ? narrow[i] : wide[i]).ToArray();
        var through2 = Road("Through2", 2, 12); var through4 = Road("Through4", 4, 20);
        // 中间立柱间距 25 米：直行道往里收后核心直段约 60 米，40 米间距不出中间立柱，验证不到非默认立柱规则
        foreach (var road in new[] { through2, through4 })
        { road.ElevatedLength = 96; road.MiddlePillarSpacing = 25; road.PillarWidth = 4; }
        var collector2 = Road("Collector2", 2, 12); collector2.ElevatedLength = 84; collector2.MiddlePillarSpacing = 100; collector2.PillarWidth = 3.5f;
        var ramp = Ramp(); ramp.ElevatedLength = 88; ramp.PillarWidth = 3.5f;
        var roads = Enumerable.Range(0, 4).Select(i => new EndpointGridArmRoads(i,
            i % 2 == 0 ? through2 : through4, collector2)).ToArray();
        var bridges = Enumerable.Range(0, 4).Select(i => new EndpointBridgeRule(i, i % 2 == 0 ? 100 : 104,
            i % 2 == 0 ? 60 : 70, arms[i].Endpoints[0].Width, i % 2 == 0 ? 4 : 5,
            new HeightRange(0, 7), new HeightRange(-1, 7))).ToArray();
        var supports = new[] { new EndpointPrefabSupportRule("Full4", 100, 60, 4), new EndpointPrefabSupportRule("Full6", 104, 70, 5) }
            .Concat(new[] { through2, through4, collector2, ramp }.Select(road => new EndpointPrefabSupportRule(
                road.Name, road.ElevatedLength, road.MiddlePillarSpacing, road.PillarWidth))).ToArray();
        var design = new CloverleafRoadDesign(bridgeRules: bridges, supportRules: supports);
        var result = EndpointCompactGridCore.Create(arms, rht, 10, 10, ramp, roads, .16f, design, 0);
        AssertCore(result, arms, roads, ramp, .16f, design);
        Assert.Contains(result.Segments, segment => segment.Prefab == "Full4");
        Assert.Contains(result.Segments, segment => segment.Prefab == "Full6");
        Assert.Contains(result.Segments.Where(segment => segment.Prefab == "Through4"), segment =>
            PillarPlanner.InnerPillars(0, segment.WorldCurve.Length(64), 25, 96).Count > 0);
        Assert.All(result.Segments, segment => Assert.True(segment.WorldCurve.Length(128)
            < supports.Single(rule => rule.Prefab == segment.Prefab).MaxLength));
    }

    // An external full-width approach is a different segment from the incident
    // fork piece: its end cap must clear the collector's real beginning envelope.
    [Theory]
    [InlineData(true, false)] [InlineData(false, false)]
    [InlineData(true, true)] [InlineData(false, true)]
    public void WideRoadExternalApproachesClearTheCollectorBeyondEverySplit(bool rht, bool differentFamilies)
    {
        var arms = Arms(rht, 6); var ramp = Road("Ramp2", 2, 12);
        var roads = Enumerable.Range(0, 4).Select(i => new EndpointGridArmRoads(i,
            Road("Through2", 2, 12), Road("Collector4", 4, 20))).ToArray();
        if (differentFamilies)
        {
            var narrow = Arms(rht, 4); arms = arms.Select(arm => arm.Index % 2 == 0 ? narrow[arm.Index] : arm).ToArray();
            ramp = Road("Ramp", 1, 8); roads = Enumerable.Range(0, 4).Select(i => new EndpointGridArmRoads(i,
                Road(i % 2 == 0 ? "Through2" : "Through4", i % 2 == 0 ? 2 : 4, i % 2 == 0 ? 12 : 20), Road("Collector2", 2, 12))).ToArray();
        }
        var core = Create(arms, rht, roads, ramp, .16f); Assert.True(core.Success, core.Diagnostic);
        var attached = EndpointCoreAttachment.Attach(core, arms, .16f, 0);
        Assert.True(attached.Failure == SpatialCandidateFailure.None, attached.Diagnostic);
        var clearance = EndpointNetworkClearance.Check(attached, 8.5f, out string detail);
        Assert.True(clearance == CurveClearanceResult.Clear, detail);
        Assert.Equal(CurveClearanceResult.Clear, EndpointNetworkSupports.Check(attached,
            Array.Empty<EndpointBridgeRule>(), ramp, out _));
    }

    // A separately supplied generic support table cannot erase a real prefab's
    // centre pier or maximum span. Conflicting identities must fail before solve.
    [Theory]
    [InlineData("through-support")] [InlineData("collector-support")]
    [InlineData("ramp-support")] [InlineData("full-support")]
    [InlineData("full-identity")]
    public void RejectsConflictingMetadataForAnActualPrefab(string invalid)
    {
        var roads = Roads(4); var ramp = Ramp();
        var rules = new[] { new EndpointPrefabSupportRule("Full4", 80, null, 3),
            new EndpointPrefabSupportRule("Through4", 80, null, 3), new EndpointPrefabSupportRule("Collector", 80, null, 3),
            new EndpointPrefabSupportRule("Ramp", 80, null, 3) };
        var bridges = Array.Empty<EndpointBridgeRule>();
        if (invalid == "through-support") foreach (var road in roads) road.Through.MiddlePillarSpacing = 0;
        if (invalid == "collector-support") foreach (var road in roads) road.Collector.ElevatedLength = 30;
        if (invalid == "ramp-support") ramp.PillarWidth = 25;
        if (invalid == "full-support") bridges = new[] { new EndpointBridgeRule(0, 40, 0, 24, 7) };
        if (invalid == "full-identity") roads = roads.Select(road => new EndpointGridArmRoads(road.Arm,
            Road("Full4", 2, 12), road.Collector)).ToArray();
        var result = EndpointCompactGridCore.Create(Arms(), true, 10, 10, ramp, roads, .16f,
            new CloverleafRoadDesign(bridgeRules: bridges, supportRules: rules), 0);
        Assert.False(result.Success); Assert.Empty(result.Segments); Assert.Empty(result.Ports);
    }

    [Fact]
    public void UsesActualSmallPrefabSpansAndRejectsTheirRealCentrePiersWhenNecessary()
    {
        var roads = Roads(4); foreach (var road in roads) road.Through.ElevatedLength = 30;
        var result = Create(Arms(), true, roads, Ramp(), .16f);
        if (result.Success)
            Assert.All(result.Segments.Where(s => s.Prefab == "Through4"), s => Assert.True(s.WorldCurve.Length(128) < 30));
        else
        {
            // Short real spans can force an extra node inside the fork nose.
            // Either its deck or its pier must reject the entire candidate.
            Assert.True(result.Diagnostic.Contains("support",StringComparison.OrdinalIgnoreCase)
                ||result.Diagnostic.Contains("deck",StringComparison.OrdinalIgnoreCase),result.Diagnostic);
            Assert.Empty(result.Segments); Assert.Empty(result.Ports);
        }
    }

    private static void AssertCore(EndpointStackCoreResult result, EndpointArm[] arms, EndpointGridArmRoads[] roads, RoadInfo ramp, float grade,
        CloverleafRoadDesign? design = null)
    {
        Assert.True(result.Success, result.Diagnostic); Assert.Equal(8, result.Ports.Count);
        Assert.Equal(20, result.Segments.Select(s => s.PathId).Distinct().Count());
        foreach (var path in result.Segments.GroupBy(s => s.PathId))
        {
            var pieces = path.ToArray();
            for (int i = 0; i < pieces.Length; i++)
            {
                void Endpoint(Vec3 point,Vec3 node)
                {
                    if(point==node)return;
                    // A split/merge keeps one exact logical node while narrower
                    // road surfaces meet opposite edges of the full-width road.
                    var incident=result.Segments.Where(s=>s.StartNode==node||s.EndNode==node).ToArray();
                    Assert.Equal(3,incident.Length);
                    var full=Assert.Single(incident.Where(s=>s.Kind==CloverEdgeKind.Main&&s.RoadWidth==incident.Max(edge=>edge.RoadWidth)));
                    Assert.Equal(node.Y,point.Y);
                    Assert.InRange(Math.Abs(Vec3.Distance(point,node)-(full.RoadWidth!.Value-pieces[i].RoadWidth!.Value)/2),0,.002);
                    var heading=Vec3.Normalize(full.WorldCurve.Derivative(full.StartNode==node?0:1));
                    Assert.InRange(Math.Abs(Directions.Dot(point-node,heading)),0,.002);
                }
                Endpoint(pieces[i].WorldCurve.P0,pieces[i].StartNode);Endpoint(pieces[i].WorldCurve.P3,pieces[i].EndNode);
                Assert.Equal(VerticalProfileFailure.None, VerticalProfile.CheckGrade(pieces[i].WorldCurve, grade));
                if (i > 0) Assert.Equal(pieces[i - 1].EndNode, pieces[i].StartNode);
            }
        }
        foreach (var port in result.Ports)
        {
            Assert.Equal(10, port.Position.Y); var selected = arms.Single(a => a.Index == port.Arm).Endpoints.Single(e => e.TravelOutward == port.TravelOutward);
            Assert.Equal(selected.Prefab, port.Prefab); Assert.Equal(selected.Width, port.Width);
            Assert.Single(result.Segments.Where(s => s.StartNode == port.Position || s.EndNode == port.Position));
        }
        foreach (var input in result.Ports.Where(p => !p.TravelOutward))
        {
            var reached = Reachable(result.Segments, input.Position);
            foreach (var output in result.Ports.Where(p => p.TravelOutward)) Assert.Equal(input.Arm != output.Arm, reached.Contains(output.Position));
        }
        var candidate = new SpatialCandidateResult(SpatialCandidateFailure.None, result.Segments);
        design ??= new CloverleafRoadDesign();
        Assert.Equal(CurveClearanceResult.Clear, EndpointNetworkClearance.Check(candidate,
            EndpointCompactGridCore.RequiredClearance(roads, design, ramp), out string detail));
        Assert.True(EndpointNetworkSupports.Check(candidate, design.BridgeRules, ramp, out detail,
            design.SupportRules.Count == 0 ? null : design.SupportRules) == CurveClearanceResult.Clear, detail);
        foreach (int arm in Enumerable.Range(0, 4))
        {
            Assert.Contains(result.Segments, s => s.PathId == $"GridMain{arm}" && s.Prefab == roads[arm].Through.Name);
            Assert.Contains(result.Segments, s => s.PathId == $"GridInCollector{arm}" && s.Prefab == roads[arm].Collector.Name);
            Assert.Contains(result.Segments, s => s.PathId == $"GridOutCollector{arm}" && s.Prefab == roads[arm].Collector.Name);
        }
    }

    private static HashSet<Vec3> Reachable(IReadOnlyList<SpatialSegment> segments, Vec3 start)
    {
        var reached = new HashSet<Vec3> { start }; var queue = new Queue<Vec3>(); queue.Enqueue(start);
        while (queue.Count != 0)
        {
            var at = queue.Dequeue();
            foreach (var segment in segments.Where(s => s.StartNode == at))
                if (reached.Add(segment.EndNode)) queue.Enqueue(segment.EndNode);
        }
        return reached;
    }

    private static EndpointStackCoreResult Create(EndpointArm[] arms, bool rht, EndpointGridArmRoads[] roads, RoadInfo ramp, float grade)
        => EndpointCompactGridCore.Create(arms, rht, 10, 10, ramp, roads, grade, new CloverleafRoadDesign(), 0);
    private static RoadInfo Road(string name, int lanes, float width) => new RoadInfo(name, true, lanes, 0, width);
    private static RoadInfo Ramp() => Road("Ramp", 1, 6);
    private static EndpointGridArmRoads[] Roads(int lanes) => Enumerable.Range(0, 4)
        .Select(i => new EndpointGridArmRoads(i, Road("Through" + lanes, lanes - 2, lanes == 4 ? 12 : 20), Road("Collector", 2, 12))).ToArray();
    private static EndpointArm[] Arms(bool rht = true, int lanes = 4)
        => Enumerable.Range(0, 4).Select(i => {
            var axis = Directions.FromAngle(i * 90); var side = Directions.RightOf(axis) * (rht ? 1 : -1);
            return new EndpointArm(i, new[] { false, true }.Select(output => new SelectedEndpoint(i * 10 + (output ? 2 : 1), 1,
                axis * 700 + side * ((output ? 1 : -1) * (lanes == 4 ? 16 : 20)) + new Vec3(0, 10, 0), axis, true, output,
                lanes == 4 ? 24 : 32, lanes, "Full" + lanes)));
        }).ToArray();
}
