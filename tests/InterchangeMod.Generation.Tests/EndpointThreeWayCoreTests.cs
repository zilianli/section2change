using InterchangeMod.Generation.Endpoints;
using InterchangeMod.Generation.Geometry;
using InterchangeMod.Generation.Roads;

namespace InterchangeMod.Generation.Tests;

public sealed class EndpointThreeWayCoreTests
{
    private static readonly RoadInfo Ramp = new("Ramp8", true, 1, 0, 8);

    public static IEnumerable<object[]> Configurations()
    {
        foreach (var type in new[] { InterchangeType.Trumpet, InterchangeType.DirectionalT, InterchangeType.SemiDirectionalT })
        foreach (bool rht in new[] { true, false })
        foreach (bool option in new[] { true, false })
            yield return new object[] { type, rht, option };
    }

    // Catches mirrored flow, four-way arm arithmetic and omitting a branch chain.
    [Theory]
    [MemberData(nameof(Configurations))]
    public void PreservesSixExternalPortsAndEveryDirectedMovement(InterchangeType type, bool rht, bool option)
    {
        var arms = Arms(rht);
        var result = Create(arms, type, rht, 103, type == InterchangeType.Trumpet ? 113 : 103,
            new CloverleafRoadDesign(), option, type != InterchangeType.Trumpet && option);
        Assert.True(result.Success, result.Diagnostic);
        Assert.Equal(6, result.Ports.Count);
        foreach (int arm in Enumerable.Range(0, 3))
        {
            var input = Assert.Single(result.Ports.Where(p => p.Arm == arm && !p.TravelOutward));
            var output = Assert.Single(result.Ports.Where(p => p.Arm == arm && p.TravelOutward));
            var original = arms[arm].Endpoints[0];
            Assert.Equal(original.Prefab, input.Prefab);
            Assert.Equal(original.Width, input.Width);
            Assert.Equal(original.Prefab, output.Prefab);
            Assert.Equal(original.Width, output.Width);
            Assert.Equal(103, input.Position.Y, 3);
            Assert.Equal(103, output.Position.Y, 3);
            Assert.True(Dot(input.Tangent, -original.OutwardTangent) > .9999f);
            Assert.True(Dot(output.Tangent, original.OutwardTangent) > .9999f);
            var reachable = Reachable(result.Segments, input.Position);
            foreach (int target in Enumerable.Range(0, 3).Where(t => t != arm))
                Assert.Contains(result.Ports.Single(p => p.Arm == target && p.TravelOutward).Position, reachable);
            Assert.DoesNotContain(output.Position, reachable);
        }
        Assert.Equal(8, result.Segments.Select(s => s.PathId).Distinct().Count());
        foreach (var path in result.Segments.GroupBy(s => s.PathId))
        {
            var pieces = path.ToArray();
            for (int i = 1; i < pieces.Length; i++) Assert.Equal(pieces[i - 1].EndNode, pieces[i].StartNode);
        }
        Assert.All(result.Segments, s => Assert.Equal(VerticalProfileFailure.None, VerticalProfile.CheckGrade(s.WorldCurve, .12f)));
        var candidate = new SpatialCandidateResult(SpatialCandidateFailure.None, result.Segments);
        Assert.True(EndpointNetworkClearance.Check(candidate, 8.5f, out var deck) == CurveClearanceResult.Clear, deck);
        Assert.True(EndpointNetworkSupports.Check(candidate, Array.Empty<EndpointBridgeRule>(), Ramp, out var supports) == CurveClearanceResult.Clear, supports);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TrumpetSupportsMainAboveBranchAndPreservesBalance(bool rht)
    {
        var design = new CloverleafRoadDesign(new[] {
            new EndpointLaneBalance(0, "ReducedMain12", 12, 2, 1),
            new EndpointLaneBalance(2, "ReducedMain12", 12, 2, 1),
            new EndpointLaneBalance(1, "ReducedBranch8", 8, 1, 1) });
        var result = Create(Arms(rht), InterchangeType.Trumpet, rht, 113, 103, design);
        Assert.True(result.Success, result.Diagnostic);
        Assert.Contains(result.Segments, s => s.Prefab == "ReducedMain12" && s.RoadWidth == 12);
        Assert.Contains(result.Segments, s => s.Prefab == "ReducedBranch8" && s.RoadWidth == 8);
        Assert.All(result.Ports, p => Assert.Equal(p.Arm == 1 ? 103 : 113, p.Position.Y, 3));
        Assert.All(result.Segments.Where(s => s.Kind == CloverEdgeKind.Main && s.Prefab!.StartsWith("Branch")), s => Assert.Equal(1, s.SourceArm));
    }

    [Fact]
    public void RejectsSingleCarriagewayWithoutPartialOutput()
    {
        var arms = Arms();
        arms[1] = new EndpointArm(1, new[] { new SelectedEndpoint(99, 1, new Vec3(100, 0, 100), new Vec3(1, 0, 0), false, false, 20, 2, "TwoWay") });
        var result = Create(arms, InterchangeType.Trumpet, true, 103, 113, new CloverleafRoadDesign());
        Assert.False(result.Success); Assert.NotEmpty(result.Diagnostic); Assert.Empty(result.Segments); Assert.Empty(result.Ports);
    }

    [Theory]
    [InlineData(InterchangeType.Trumpet, 75, .08f)]
    [InlineData(InterchangeType.Trumpet, 105, .30f)]
    [InlineData(InterchangeType.DirectionalT, 75, .08f)]
    [InlineData(InterchangeType.DirectionalT, 105, .30f)]
    [InlineData(InterchangeType.SemiDirectionalT, 75, .08f)]
    [InlineData(InterchangeType.SemiDirectionalT, 105, .30f)]
    public void UsesActualWideRoadSupportRulesAtSkewAngles(InterchangeType type, float crossing, float grade)
    {
        var arms = Arms(false, crossing, 20, 16);
        var design = new CloverleafRoadDesign(new[] {
            new EndpointLaneBalance(0, "MainReduced16", 16, 2, 1), new EndpointLaneBalance(2, "MainReduced16", 16, 2, 1),
            new EndpointLaneBalance(1, "BranchReduced12", 12, 1, 1) },
            bridgeRules: new[] { new EndpointBridgeRule(0, 80, null, 20, 4), new EndpointBridgeRule(2, 80, null, 20, 4), new EndpointBridgeRule(1, 80, null, 16, 4) },
            supportRules: new[] { "Main20", "Branch16", "MainReduced16", "BranchReduced12", "Ramp8" }.Select(p => new EndpointPrefabSupportRule(p, 80, null, 4)));
        var ramp = new RoadInfo("Ramp8", true, 1, 0, 8) { PillarWidth = 4, ElevatedLength = 80 };
        var result = EndpointThreeWayCore.Create(arms, type, false, 103, 113, ramp, grade, design);
        Assert.True(result.Success, result.Diagnostic);
        var candidate = new SpatialCandidateResult(SpatialCandidateFailure.None, result.Segments);
        Assert.True(EndpointNetworkClearance.Check(candidate, 8.5f, out var decks) == CurveClearanceResult.Clear, decks);
        Assert.True(EndpointNetworkSupports.Check(candidate, design.BridgeRules, ramp, out var supports, design.SupportRules) == CurveClearanceResult.Clear, supports);
        Assert.All(result.Segments, s => Assert.Equal(VerticalProfileFailure.None, VerticalProfile.CheckGrade(s.WorldCurve, grade)));
        foreach (var segment in result.Segments.Where(s => s.Kind == CloverEdgeKind.Main))
            Assert.Equal(segment.Prefab!.StartsWith("Branch") ? 1 : segment.SourceArm, segment.SourceArm);
        Assert.Contains(result.Segments, s => s.StartNode != s.WorldCurve.P0 || s.EndNode != s.WorldCurve.P3);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(.31f)]
    [InlineData(0)]
    public void RejectsInvalidMaximumGradeAtomically(float grade)
    {
        var result = EndpointThreeWayCore.Create(Arms(), InterchangeType.Trumpet, true, 103, 113, Ramp, grade, new CloverleafRoadDesign());
        Assert.False(result.Success); Assert.NotEmpty(result.Diagnostic); Assert.Empty(result.Segments); Assert.Empty(result.Ports);
    }

    [Fact]
    public void RejectsAsymmetricMainBalanceAndMissingActualSupportPrefab()
    {
        foreach (var design in new[] {
            new CloverleafRoadDesign(new[] { new EndpointLaneBalance(0, "Reduced", 12, 2, 1) }),
            new CloverleafRoadDesign(supportRules: new[] { new EndpointPrefabSupportRule("Main16", 80, null, 4) }) })
        {
            var result = EndpointThreeWayCore.Create(Arms(), InterchangeType.Trumpet, true, 103, 113, Ramp, .12f, design);
            Assert.False(result.Success); Assert.NotEmpty(result.Diagnostic); Assert.Empty(result.Segments); Assert.Empty(result.Ports);
        }
    }

    // The shared clearance checker exempts all pieces with one PathId. Its producer
    // must reject a nonlocal self-crossing before publishing that identity.
    [Fact]
    public void PathCertificateRejectsAChainThatCrossesItsOwnEarlierPiece()
    {
        var points = new[] { new Vec3(0, 0, 0), new Vec3(10, 0, 10), new Vec3(0, 0, 10), new Vec3(10, 0, 0) };
        var curves = Enumerable.Range(0, 3).Select(i => Bezier3.Line(points[i], points[i + 1])).ToArray();
        Assert.False(EndpointThreeWayCore.IsSimplePath(curves, out _));
        var simple = new[] { Bezier3.Line(points[0], points[1]), Bezier3.Line(points[1], points[2]) };
        Assert.True(EndpointThreeWayCore.IsSimplePath(simple, out _));
    }

    [Theory]
    [InlineData(InterchangeType.Trumpet)]
    [InlineData(InterchangeType.DirectionalT)]
    [InlineData(InterchangeType.SemiDirectionalT)]
    public void SolvesAStableSecantCoreWhenActualMainEndTangentsBend(InterchangeType type)
    {
        var arms = Arms();
        foreach (int arm in new[] { 0, 2 })
        {
            var tangent = Directions.FromAngle(arm == 0 ? 31 : 195);
            var side = Directions.RightOf(tangent);
            var midpoint = (arms[arm].Endpoints[0].Position + arms[arm].Endpoints[1].Position) * .5f;
            arms[arm] = new EndpointArm(arm, arms[arm].Endpoints.Select(p => new SelectedEndpoint(p.NodeIndex, p.NodeVersion,
                midpoint + side * 12 * (p.TravelOutward ? 1 : -1), tangent, true, p.TravelOutward, p.Width, p.Lanes, p.Prefab)));
        }
        var result = EndpointThreeWayCore.Create(arms, type, true, 103, 113, Ramp, .12f, new CloverleafRoadDesign());
        Assert.True(result.Success, result.Diagnostic);
        Assert.Equal(6, result.Ports.Count);
        Assert.True(result.ReferenceCenter.HasValue);
        Assert.True(Vec3.Distance(result.ReferenceCenter!.Value, new Vec3(400, 0, -230)) < .01f);
        foreach (var port in result.Ports.Where(p => p.Arm != 1))
            Assert.True(Math.Abs(Dot(port.Tangent, Directions.FromAngle(23))) > .99999f, $"arm={port.Arm}, outward={port.TravelOutward}, tangent={port.Tangent}, dot={Dot(port.Tangent, Directions.FromAngle(23)):R}");
    }

    [Theory]
    [MemberData(nameof(Configurations))]
    public void PreservesMovementsWithBranchOnOtherSideAtThirtyPercent(InterchangeType type, bool rht, bool option)
    {
        var arms = Arms(rht, 270);
        var result = EndpointThreeWayCore.Create(arms, type, rht, 481, type == InterchangeType.Trumpet ? 493 : 469,
            Ramp, .30f, new CloverleafRoadDesign(), option, type != InterchangeType.Trumpet && option,
            terrainHeight: type != InterchangeType.Trumpet && option ? 481 : (float?)null);
        Assert.True(result.Success, result.Diagnostic);
        foreach (var input in result.Ports.Where(p => !p.TravelOutward))
        {
            var reachable = Reachable(result.Segments, input.Position);
            foreach (var output in result.Ports.Where(p => p.TravelOutward))
                Assert.Equal(input.Arm != output.Arm, reachable.Contains(output.Position));
        }
        Assert.All(result.Segments, s => Assert.Equal(VerticalProfileFailure.None, VerticalProfile.CheckGrade(s.WorldCurve, .30f)));
        Assert.All(result.Segments.Where(s => s.Kind == CloverEdgeKind.Main && s.Prefab == "Branch12"), s => Assert.Equal(1, s.SourceArm));
    }

    [Fact]
    public void RejectsUnknownTypeAndMismatchedMainRoadsAtomically()
    {
        var arms = Arms();
        var old = arms[2].Endpoints[0];
        arms[2] = new EndpointArm(2, new[] { new SelectedEndpoint(old.NodeIndex, old.NodeVersion, old.Position, old.OutwardTangent,
            true, false, old.Width, old.Lanes, "OtherPrefab"), arms[2].Endpoints[1] });
        foreach (var type in new[] { InterchangeType.Trumpet, InterchangeType.Stack })
        {
            var result = EndpointThreeWayCore.Create(arms, type, true, 103, 113, Ramp, .12f, new CloverleafRoadDesign());
            Assert.False(result.Success); Assert.NotEmpty(result.Diagnostic); Assert.Empty(result.Ports); Assert.Empty(result.Segments);
        }
    }

    [Theory]
    [InlineData(InterchangeType.DirectionalT, 103f)]
    [InlineData(InterchangeType.SemiDirectionalT, 103f)]
    [InlineData(InterchangeType.DirectionalT, 100f)]
    [InlineData(InterchangeType.SemiDirectionalT, 100f)]
    public void TwoLevelUnderpassUsesActualTerrainDatum(InterchangeType type, float mainHeight)
    {
        var result = CreateWithTerrain(Arms(), type, mainHeight, mainHeight, true, 100);
        Assert.True(result.Success, result.Diagnostic);
        var underpass = result.Segments.Where(s => s.PathId == "BranchToFar").ToArray();
        Assert.NotEmpty(underpass);
        Assert.True(underpass.Min(s => Math.Min(s.WorldCurve.P0.Y, s.WorldCurve.P3.Y)) <= 87.0001f,
            "The lower crossing must be at least 13m below terrain100, not 13m below the world-space road height.");
        var near = result.Segments.First(s => s.PathId == "MainNear");
        var side = Directions.RightOf(Directions.FromAngle(23));
        var crossing = underpass.SelectMany(s => Enumerable.Range(0, 65).Select(i => s.WorldCurve.Evaluate(i / 64f)))
            .Where(p => Math.Abs(Directions.Dot(p - near.WorldCurve.P0, side)) <= (near.RoadWidth!.Value + Ramp.Width) / 2).ToArray();
        Assert.NotEmpty(crossing);
        Assert.All(crossing, p => Assert.True(p.Y <= 87.0001f, $"The lower road crossing the near mainline is an open cutting at world Y={p.Y}."));
        Assert.All(result.Ports, p => Assert.Equal(mainHeight, p.Position.Y, 3));
    }

    [Theory]
    [InlineData(InterchangeType.DirectionalT)]
    [InlineData(InterchangeType.SemiDirectionalT)]
    public void TwoLevelRejectsUnknownOrNonFiniteTerrainDatum(InterchangeType type)
    {
        foreach (float? terrain in new float?[] { null, float.NaN, float.PositiveInfinity })
        {
            var result = CreateWithTerrain(Arms(), type, 103, 103, true, terrain);
            Assert.False(result.Success); Assert.NotEmpty(result.Diagnostic); Assert.Empty(result.Ports); Assert.Empty(result.Segments);
        }
    }

    [Theory]
    [InlineData(InterchangeType.DirectionalT)]
    [InlineData(InterchangeType.SemiDirectionalT)]
    public void TwoLevelPreservesLegacyElevatedMainlineGuard(InterchangeType type)
    {
        var result = CreateWithTerrain(Arms(), type, 110, 110, true, 100);
        Assert.False(result.Success); Assert.NotEmpty(result.Diagnostic); Assert.Empty(result.Ports); Assert.Empty(result.Segments);
    }

    [Theory]
    [InlineData(InterchangeType.Trumpet)]
    [InlineData(InterchangeType.DirectionalT)]
    [InlineData(InterchangeType.SemiDirectionalT)]
    public void KnownTerrainPreservesRaisedUpperDeckRejection(InterchangeType type)
    {
        var result = CreateWithTerrain(Arms(), type, 100, type == InterchangeType.Trumpet ? 110 : 100, false, 105);
        Assert.False(result.Success); Assert.NotEmpty(result.Diagnostic); Assert.Empty(result.Ports); Assert.Empty(result.Segments);
    }

    private static EndpointStackCoreResult CreateWithTerrain(EndpointArm[] arms, InterchangeType type, float hm, float hb, bool twoLevel, float? terrain)
        => EndpointThreeWayCore.Create(arms, type, true, hm, hb, Ramp, .12f, new CloverleafRoadDesign(), true, twoLevel, terrain);

    private static EndpointStackCoreResult Create(EndpointArm[] arms, InterchangeType type, bool rht,
        float hm, float hb, CloverleafRoadDesign design, bool loopExit = true, bool twoLevel = false)
        => EndpointThreeWayCore.Create(arms, type, rht, hm, hb, Ramp, .12f, design, loopExit, twoLevel, twoLevel ? hm : (float?)null);

    private static float Dot(Vec3 a, Vec3 b) => Directions.Dot(Vec3.Normalize(a), Vec3.Normalize(b));
    private static HashSet<Vec3> Reachable(IEnumerable<SpatialSegment> roads, Vec3 start)
    {
        var seen = new HashSet<Vec3> { start }; var queue = new Queue<Vec3>(); queue.Enqueue(start);
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            foreach (var segment in roads.Where(s => s.StartNode == node).ToArray())
                if (seen.Add(segment.EndNode)) queue.Enqueue(segment.EndNode);
        }
        return seen;
    }

    internal static EndpointArm[] Arms(bool rht = true, float crossing = 90, float mainWidth = 16, float branchWidth = 12)
    {
        return Enumerable.Range(0, 3).Select(arm => {
            var outward = Directions.FromAngle(arm == 0 ? 23 : arm == 1 ? 23 + crossing : 203);
            var side = Directions.RightOf(outward) * (rht ? 1 : -1);
            float width = arm == 1 ? branchWidth : mainWidth;
            var center = new Vec3(400, 0, -230) + outward * 1300;
            return new EndpointArm(arm, new[] { false, true }.Select(travel =>
                new SelectedEndpoint(arm * 10 + (travel ? 2 : 1), 1, center + side * ((width + 8) / 2) * (travel ? 1 : -1),
                    outward, true, travel, width, arm == 1 ? 2 : 3, (arm == 1 ? "Branch" : "Main") + width)));
        }).ToArray();
    }
}
