using System.Reflection;
using InterchangeMod.Generation.Endpoints;
using InterchangeMod.Generation.Geometry;
using InterchangeMod.Generation.Roads;
using InterchangeMod.Generation.Templates;

namespace InterchangeMod.Generation.Tests;

public sealed class EndpointRoundaboutCoreTests
{
    // Reflection keeps this first regression executable before the new core exists.
    // There is deliberately no temporary production stub.
    [Fact]
    public void ExposesTheApprovedRoundaboutCoreContract()
    {
        var type = typeof(EndpointStackCore).Assembly.GetType("InterchangeMod.Generation.Endpoints.EndpointRoundaboutCore");
        Assert.NotNull(type);
        Assert.NotNull(type!.GetMethod("Create", BindingFlags.Public | BindingFlags.Static));
        Assert.NotNull(type.GetMethod("Candidates", BindingFlags.Public | BindingFlags.Static));
    }

    public static IEnumerable<object[]> Cases()
    {
        foreach (bool rht in new[] { true, false })
        foreach (bool racetrack in new[] { false, true })
        foreach (float grade in new[] { .08f, .16f, .30f })
            yield return new object[] { rht, racetrack, grade };
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void ResolvesActualRoadsIntoEightPortsTwelveMovementsAndOneLegalRing(bool rht, bool racetrack, float grade)
    {
        var arms = CompactGridFixtures.Arms(rht, distance: 700);
        var catalog = CompactGridFixtures.Catalog();
        var core = Create(arms, rht, 113, 103, catalog[0], catalog[3], grade, Design(arms, catalog), 93, racetrack);
        AssertCore(core, arms, catalog[0], catalog[3], grade);
        Assert.All(core.Ports, p => Assert.Equal(p.Arm % 2 == 0 ? 113 : 103, p.Position.Y));
        Assert.Contains(core.Segments, s => s.Prefab == "Road3" && s.Kind == CloverEdgeKind.Main);
        Assert.DoesNotContain(core.Segments, s => s.Prefab == "Road3" && s.SourceArm % 2 != 0);
        Assert.Equal(8, core.Segments.Where(s => s.PathId.StartsWith("RingArc")).Select(s => s.PathId).Distinct().Count());
        foreach (var piece in core.Segments.Where(s => s.PathId.StartsWith("RingArc")))
        {
            var towardIsland = core.ReferenceCenter!.Value - piece.WorldCurve.Evaluate(.5f);
            var side = Directions.Dot(towardIsland, Directions.RightOf(Vec3.Normalize(piece.WorldCurve.Derivative(.5f))));
            Assert.True(rht ? side < 0 : side > 0, "The ring runs on the wrong side of its island.");
        }
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void UsesCutpointSecantsWhenActualCurvedRoadTangentsAreNotTheCoreAxes(bool rht)
    {
        var original = CompactGridFixtures.Arms(rht, distance: 700);
        var arms = original.Select(a => new EndpointArm(a.Index, a.Endpoints.Select(e => {
            var axis = e.OutwardTangent.RotateY((a.Index % 2 == 0 ? 1 : -1) * .12f);
            var mid = (a.Endpoints[0].Position + a.Endpoints[1].Position) * .5f;
            return new SelectedEndpoint(e.NodeIndex, e.NodeVersion,
                mid + Directions.RightOf(axis) * (rht ? 1 : -1) * (e.TravelOutward ? 14 : -14),
                axis, true, e.TravelOutward, e.Width, e.Lanes, e.Prefab);
        }))).ToArray();
        var catalog = CompactGridFixtures.Catalog();
        var core = Create(arms, rht, 113, 103, catalog[0], catalog[3], .16f, Design(arms, catalog), 93);
        AssertCore(core, arms, catalog[0], catalog[3], .16f);
        Assert.True(Vec3.Distance(new Vec3(400, 0, -230), core.ReferenceCenter!.Value) < .01f);
        foreach (var port in core.Ports)
        {
            var axis = Directions.FromAngle(port.Arm * 90) * (port.TravelOutward ? 1 : -1);
            Assert.True(Directions.Dot(axis, port.Tangent) > .9999f);
        }
    }

    [Theory]
    [InlineData("grade")] [InlineData("terrain")] [InlineData("gap")]
    [InlineData("bridge-threshold")] [InlineData("ring-reverse")] [InlineData("ring-lanes")]
    [InlineData("cross-reduced")] [InlineData("main-lanes")] [InlineData("missing-balance")]
    [InlineData("ring-support")] [InlineData("support-missing")] [InlineData("deep-envelope")]
    public void RejectsUnsafeRealMetadataWithoutAPartialCore(string invalid)
    {
        var arms = CompactGridFixtures.Arms(distance: 700); var catalog = CompactGridFixtures.Catalog();
        var ramp = catalog[0]; var ring = catalog[3]; var design = Design(arms, catalog);
        float grade = .16f, hm = 113, hc = 103, terrain = 93;
        switch (invalid)
        {
            case "grade": grade = .301f; break;
            case "terrain": terrain = float.NaN; break;
            case "gap": hm = 111; break;
            case "bridge-threshold": hm = 100; hc = 90; terrain = 93; break;
            case "ring-reverse": ring = new RoadInfo("Reverse", true, 0, 4, 20); break;
            case "ring-lanes": ring = catalog[0]; break;
            case "cross-reduced": design = new CloverleafRoadDesign(design.Balances.Append(new EndpointLaneBalance(1, "Road3", 16, 3, 1))); break;
            case "main-lanes": design = new CloverleafRoadDesign(new[] { new EndpointLaneBalance(0, "Road2", 12, 2, 1), new EndpointLaneBalance(2, "Road2", 12, 2, 1) }); break;
            case "missing-balance": design = new CloverleafRoadDesign(); break;
            case "ring-support": ring.MiddlePillarSpacing = -1; break;
            case "support-missing": design = new CloverleafRoadDesign(design.Balances, bridgeRules: design.BridgeRules,
                supportRules: design.SupportRules.Where(s => s.Prefab != "Road3")); break;
            case "deep-envelope": ring = new RoadInfo("DeepRing", true, 4, 0, 20,
                groundHeight: new HeightRange(0, 20), elevatedHeight: new HeightRange(-20, 20)); break;
        }
        var core = Create(arms, true, hm, hc, ramp, ring, grade, design, terrain);
        Assert.False(core.Success); Assert.NotEmpty(core.Diagnostic); Assert.Empty(core.Segments); Assert.Empty(core.Ports);
    }

    [Fact]
    public void CarriesActualPrefabSupportsAndPreservesPhysicalLogicalMergeOffsets()
    {
        var arms = CompactGridFixtures.Arms(distance: 700); var catalog = CompactGridFixtures.Catalog();
        var ramp = catalog[0]; var ring = catalog[3]; var design = Design(arms, catalog);
        var core = Create(arms, true, 113, 103, ramp, ring, .16f, design, 93);
        AssertCore(core, arms, ramp, ring, .16f);
        var offset = core.Segments.Where(s => s.WorldCurve.P0 != s.StartNode || s.WorldCurve.P3 != s.EndNode).ToArray();
        Assert.NotEmpty(offset);
        Assert.All(offset, s => Assert.Equal(s.Kind == CloverEdgeKind.Main ? 2 : 6,
            Math.Max(Vec3.Distance(s.StartNode, s.WorldCurve.P0), Vec3.Distance(s.EndNode, s.WorldCurve.P3)), 3));
        var spatial = new SpatialCandidateResult(SpatialCandidateFailure.None, core.Segments);
        Assert.Equal(CurveClearanceResult.Clear, EndpointNetworkSupports.Check(spatial, design.BridgeRules, ramp, out _, design.SupportRules));
        var huge = new RoadInfo("HugeRing", true, 4, 0, 20) { ElevatedLength = 80, PillarWidth = 600 };
        var hugeDesign = new CloverleafRoadDesign(design.Balances, bridgeRules: design.BridgeRules,
            supportRules: design.SupportRules.Append(new EndpointPrefabSupportRule(huge.Name, 80, null, 600)));
        var rejected = Create(arms, true, 103, 113, ramp, huge, .16f, hugeDesign, 93);
        Assert.False(rejected.Success); Assert.Contains("support", rejected.Diagnostic, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("main")] [InlineData("cross")] [InlineData("ramp")] [InlineData("ring")]
    public void ClearanceUnionsEveryActualGroundAndElevatedEnvelope(string part)
    {
        var arms = CompactGridFixtures.Arms(); var catalog = CompactGridFixtures.Catalog();
        var design = Design(arms, catalog); var ramp = catalog[0]; var ring = catalog[3];
        var deep = new RoadInfo("Deep", true, part == "ramp" ? 1 : 4, 0, part == "ramp" ? 8 : 20,
            groundHeight: new HeightRange(0, 20), elevatedHeight: new HeightRange(-20, 20));
        if (part == "ramp") ramp = deep;
        if (part == "ring") ring = deep;
        if (part == "main" || part == "cross")
        {
            int arm = part == "main" ? 0 : 1;
            design = new CloverleafRoadDesign(design.Balances,
                bridgeRules: design.BridgeRules.Select(r => r.Arm == arm ? new EndpointBridgeRule(arm, 80, null, 20, 4,
                    new HeightRange(0, 20), new HeightRange(-20, 20)) : r));
        }
        Assert.Equal(40, EndpointRoundaboutCore.RequiredClearance(design, ramp, ring));
    }

    [Fact]
    public void ActualClearanceLeavesARealMarginInsideTheTenMetreSetupSeparation()
    {
        var arms = CompactGridFixtures.Arms(); var catalog = CompactGridFixtures.Catalog();
        Assert.Equal(HeightRange.BridgeThreshold, EndpointRoundaboutCore.RequiredClearance(Design(arms, catalog), catalog[0], catalog[3]));
        Assert.True(EndpointRoundaboutCore.RequiredClearance(Design(arms, catalog), catalog[0], catalog[3]) < 10);
    }

    [Fact]
    public void CandidateEnumerationIsFiniteAndEverySuccessRetainsTheFullNetworkCertificate()
    {
        var arms = CompactGridFixtures.Arms(distance: 700); var catalog = CompactGridFixtures.Catalog();
        var trials = EndpointRoundaboutCore.Candidates(arms, true, 113, 103, catalog[0], catalog[3], .16f,
            Design(arms, catalog), 93).Take(25).ToArray();
        Assert.InRange(trials.Length, 1, 6); Assert.Contains(trials, core => core.Success);
        foreach (var core in trials.Where(core => core.Success)) AssertCore(core, arms, catalog[0], catalog[3], .16f);
    }

    [Fact]
    public void EmitsTheActualNMinusRPrefabWhenItsWidthEqualsTheFullRoad()
    {
        var arms = CompactGridFixtures.Arms(distance: 700); var catalog = CompactGridFixtures.Catalog(); var original = Design(arms, catalog);
        const string reduced = "EqualWidthReduced";
        var design = new CloverleafRoadDesign(new[] { 0, 2 }.Select(i => new EndpointLaneBalance(i, reduced, 20, 3, 1)),
            bridgeRules: original.BridgeRules, supportRules: original.SupportRules.Append(new EndpointPrefabSupportRule(reduced, 80, null, 4)));
        var core = Create(arms, true, 113, 103, catalog[0], catalog[3], .16f, design, 93);
        Assert.True(core.Success, core.Diagnostic);
        Assert.Contains(core.Segments, s => s.Kind == CloverEdgeKind.Main && s.Prefab == reduced);
    }

    [Fact]
    public void ActualTemplateSubstitutesAnEqualWidthReducedPrefabWithoutMovingAnyGeometry()
    {
        var p = new InterchangeParams { Legs = InterchangeParams.CrossLegs(90, 0, 1),
            RampPrefab = "Road1", RampWidth = 8, RingPrefab = "Road4", RingWidth = 20, LoopRadius = 0, MaxGrade = .16f };
        p.SetRoad(0, 2, RoadForm.Dual, 20, 8, "Road4"); p.SetRoad(1, 3, RoadForm.Dual, 20, 8, "Road4");
        p.Levels[0].Elevation = 20; p.Levels[1].Elevation = 10;
        var template = new RoundaboutTemplate(); Assert.Empty(template.ValidateParams(p));
        var full = template.Generate(p); p.MainReducedPrefab = "EqualWidthReduced"; p.MainReducedWidth = 20;
        Assert.Empty(template.ValidateParams(p)); var reduced = template.Generate(p);
        Assert.Contains(reduced, s => s.Prefab == "EqualWidthReduced");
        Assert.Equal(full.Count, reduced.Count);
        for (int i = 0; i < full.Count; i++)
        {
            Assert.Equal(full[i].Name, reduced[i].Name); Assert.Equal(full[i].StartNode, reduced[i].StartNode);
            Assert.Equal(full[i].EndNode, reduced[i].EndNode); Assert.Equal(full[i].Curve.P0, reduced[i].Curve.P0);
            Assert.Equal(full[i].Curve.P1, reduced[i].Curve.P1); Assert.Equal(full[i].Curve.P2, reduced[i].Curve.P2);
            Assert.Equal(full[i].Curve.P3, reduced[i].Curve.P3);
        }
    }

    [Fact]
    public void AcceptsConservativeMainEnvelopesThatAlsoCoverItsActualReducedRoad()
    {
        var arms = CompactGridFixtures.Arms(distance: 700); var catalog = CompactGridFixtures.Catalog(); var original = Design(arms, catalog);
        var design = new CloverleafRoadDesign(original.Balances, bridgeRules: original.BridgeRules.Select(r => r.Arm % 2 == 0
            ? new EndpointBridgeRule(r.Arm, r.MaxLength, r.MiddleSpacing, r.Width, r.PillarWidth,
                new HeightRange(-4, 9), new HeightRange(-4, 9)) : r), supportRules: original.SupportRules);
        Assert.Equal(13, EndpointRoundaboutCore.RequiredClearance(design, catalog[0], catalog[3]));
        var core = Create(arms, true, 123, 103, catalog[0], catalog[3], .16f, design, 93);
        Assert.True(core.Success, core.Diagnostic);
    }

    [Fact]
    public void NativeCirclePiecesInsideACrossRoadJunctionEnvelopeShareItsActualNode()
    {
        var p = new InterchangeParams { Legs = InterchangeParams.CrossLegs(90, 0, 1),
            RampPrefab = "Road1", RampWidth = 8, RingPrefab = "Road4", RingWidth = 20, LoopRadius = 0, MaxGrade = .16f };
        p.SetRoad(0, 2, RoadForm.Dual, 20, 8, "Road4"); p.SetRoad(1, 3, RoadForm.Dual, 20, 8, "Road4");
        p.Levels[0].Elevation = 20; p.Levels[1].Elevation = 10;
        var template = new RoundaboutTemplate(); Assert.Empty(template.ValidateParams(p)); var segments = template.Generate(p);
        float radius = RoundaboutTemplate.ResolveRadius(p);
        foreach (var piece in segments.Where(s => s.Name.StartsWith("Ring")))
        foreach (float t in new[] { 0f, 1f })
        {
            var point = piece.Curve.Evaluate(t); var radial = new Vec3(point.X, 0, point.Z);
            Assert.InRange(Math.Abs(radial.Length() - radius), 0, .001f);
            Assert.InRange(Math.Abs(Directions.Dot(Vec3.Normalize(radial), Vec3.Normalize(piece.Curve.Derivative(t)))), 0, .00002f);
        }
        foreach (var cross in segments.Where(s => s.Role == RoadRole.CrossRoad))
        foreach (var ring in segments.Where(s => s.Name.StartsWith("Ring")))
        {
            var clearance = CurveClearance.Check(cross.Curve, 10, ring.Curve, 10, HeightRange.BridgeThreshold);
            if (clearance == CurveClearanceResult.Clear) continue;
            Assert.True(cross.StartNode == ring.StartNode || cross.StartNode == ring.EndNode
                || cross.EndNode == ring.StartNode || cross.EndNode == ring.EndNode,
                $"{cross.Name} overlaps independent ring piece {ring.Name} across an artificial internal seam.");
        }
    }

    private static EndpointStackCoreResult Create(EndpointArm[] arms, bool rht, float hm, float hc, RoadInfo ramp,
        RoadInfo ring, float grade, CloverleafRoadDesign design, float terrain, bool racetrack = false)
        => EndpointRoundaboutCore.Create(arms, rht, hm, hc, ramp, ring, grade, design, terrain, racetrack);

    internal static CloverleafRoadDesign Design(EndpointArm[] arms, RoadInfo[] catalog)
    {
        var balances = new[] { 0, 2 }.Select(i => new EndpointLaneBalance(i, catalog[2].Name, catalog[2].Width, 3, 1));
        var bridges = arms.Select(a => { var road = catalog.Single(r => r.Name == a.Endpoints[0].Prefab);
            return new EndpointBridgeRule(a.Index, road.ElevatedLength, road.MiddlePillarSpacing, road.Width,
                road.PillarWidth, road.GroundHeight, road.ElevatedHeight); });
        var support = catalog.Take(4).Select(r => new EndpointPrefabSupportRule(r.Name, r.ElevatedLength, r.MiddlePillarSpacing, r.PillarWidth));
        return new CloverleafRoadDesign(balances, bridgeRules: bridges, supportRules: support);
    }

    private static void AssertCore(EndpointStackCoreResult core, EndpointArm[] arms, RoadInfo ramp, RoadInfo ring, float grade)
    {
        Assert.True(core.Success, core.Diagnostic); Assert.Equal(8, core.Ports.Count);
        Assert.Equal(8, core.Ports.Select(p => (p.Arm, p.TravelOutward)).Distinct().Count());
        bool Reach(Vec3 from, Vec3 to)
        {
            var seen = new HashSet<Vec3> { from }; var queue = new Queue<Vec3>(); queue.Enqueue(from);
            while (queue.Count > 0) { var at = queue.Dequeue(); foreach (var s in core.Segments.Where(s => s.StartNode == at))
                if (seen.Add(s.EndNode)) queue.Enqueue(s.EndNode); }
            return seen.Contains(to);
        }
        foreach (var input in core.Ports.Where(p => !p.TravelOutward))
        foreach (var output in core.Ports.Where(p => p.TravelOutward))
            Assert.True(Reach(input.Position, output.Position), $"Missing legal roundabout movement {input.Arm}->{output.Arm}");
        var arcs = core.Segments.Where(s => s.PathId.StartsWith("RingArc")).ToArray(); Assert.NotEmpty(arcs);
        foreach (var node in arcs.SelectMany(s => new[] { s.StartNode, s.EndNode }).Distinct())
        { Assert.Single(arcs.Where(s => s.StartNode == node)); Assert.Single(arcs.Where(s => s.EndNode == node)); }
        Assert.All(arcs, s => { Assert.Equal(ring.Name, s.Prefab); Assert.Equal(ring.Width, s.RoadWidth); Assert.Equal(CloverEdgeKind.LoopRamp, s.Kind); });
        foreach (var port in core.Ports)
        { var actual = arms.Single(a => a.Index == port.Arm).Endpoints.Single(e => e.TravelOutward == port.TravelOutward);
            Assert.Equal(actual.Prefab, port.Prefab); Assert.Equal(actual.Width, port.Width); }
        foreach (var segment in core.Segments)
        {
            Assert.InRange(segment.SourceArm, 0, 3);
            Assert.Equal(VerticalProfileFailure.None, VerticalProfile.CheckGrade(segment.WorldCurve, grade));
            Assert.Equal(CurveCurvatureResult.Clear, CurveCurvature.CheckMinimumRadius(segment.WorldCurve, Math.Max(12, 2 * segment.RoadWidth!.Value)));
        }
        Assert.Equal(CurveClearanceResult.Clear, EndpointNetworkClearance.Check(new SpatialCandidateResult(SpatialCandidateFailure.None, core.Segments), HeightRange.BridgeThreshold, out _));
    }
}
