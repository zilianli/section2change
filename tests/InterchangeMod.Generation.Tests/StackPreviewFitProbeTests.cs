using InterchangeMod.Generation.Endpoints;
using InterchangeMod.Generation.Geometry;
using InterchangeMod.Generation.Roads;

namespace InterchangeMod.Generation.Tests;

public sealed class StackPreviewFitProbeTests
{
    // Only the rounded selected snapshots match the 2026-10-31 game log. These
    // straight continuation roads are synthetic and are not an original-road replay.
    [Theory]
    [InlineData(InterchangeType.Stack)]
    [InlineData(InterchangeType.Turbine)]
    public void FitsLoggedSkewedSelectionWithBoundedSyntheticContinuations(InterchangeType type)
    {
        var arms = MixedApproachTests.LoggedArms(false);
        var traces = arms.SelectMany(a => a.Endpoints).Select(p => new RoadApproachTrace(p, new[] {
            new RoadTracePiece(p.NodeIndex + 1000, 1, Bezier3.Line(p.Position,
                p.Position + Vec3.Normalize(p.OutwardTangent) * 1100), false, p.NodeIndex + 2000, 1) })).ToArray();
        var ramp = new RoadInfo("Ramp8", true, 1, 0, 8) { ElevatedLength = 80, PillarWidth = 4 };
        var design = new CloverleafRoadDesign(
            Enumerable.Range(0, 4).Select(i => new EndpointLaneBalance(i, "Reduced", 16, 3, 1)),
            bridgeRules: Enumerable.Range(0, 4).Select(i => new EndpointBridgeRule(i, 80, null, 20, 4)),
            supportRules: new[] { "Full", "Reduced", "Ramp8" }.Select(p => new EndpointPrefabSupportRule(p, 80, null, 4)));
        var result = EndpointStackSearch.Search(arms, traces, type, true, 488.632f, 498.632f,
            ramp, .16f, 1000, design);

        Assert.True(result.Failure == RetreatSearchFailure.None,
            $"{result.Failure}; {result.LastSpatialFailure}; {result.ClearanceDetail}");
        Assert.NotNull(result.Spatial);
        Assert.Equal(4, result.Distances.Count);
        Assert.Equal(8, result.Cuts.Count);
        Assert.All(result.Distances, distance => Assert.InRange(distance, 0, 1000));
        Assert.Equal(CurveClearanceResult.Clear, EndpointNetworkClearance.Check(result.Spatial!, 8.5f, out var detail));
        Assert.Empty(detail);
        Assert.Equal(CurveClearanceResult.Clear, EndpointNetworkSupports.Check(result.Spatial!,
            design.BridgeRules, ramp, out detail, design.SupportRules));
        Assert.Empty(detail);
        Assert.All(result.Spatial!.Segments, segment =>
            Assert.Equal(VerticalProfileFailure.None, VerticalProfile.CheckGrade(segment.WorldCurve, .16f)));
        foreach (var cut in result.Cuts)
        {
            var port = cut.Port!;
            var approach = Assert.Single(result.Spatial.Segments.Where(s => port.TravelOutward
                ? s.WorldCurve.P3 == port.Position : s.WorldCurve.P0 == port.Position));
            var actual = Vec3.Normalize(approach.WorldCurve.Derivative(port.TravelOutward ? 1 : 0));
            var expected = Vec3.Normalize(port.OutwardTangent) * (port.TravelOutward ? 1 : -1);
            Assert.True(Directions.Dot(actual, expected) > .99999f);
            Assert.Equal(port.Prefab, approach.Prefab);
            Assert.Equal(port.Width, approach.RoadWidth);
        }
    }
}
