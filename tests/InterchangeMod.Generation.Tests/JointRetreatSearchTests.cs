using System.Reflection;
using InterchangeMod.Generation.Endpoints;
using InterchangeMod.Generation.Geometry;
using InterchangeMod.Generation.Roads;

namespace InterchangeMod.Generation.Tests;

public sealed class JointRetreatSearchTests
{
    private static readonly RoadInfo Ramp = new RoadInfo("Ramp8", true, 1, 0, 8);

    // Catches searching only [d,d,d,d]. Arm 1 must leave its 20% grade,
    // while arm 2 must keep the window before its original roads rise to 20%.
    // All curves and snapshots here are synthetic; this is not a game trace replay.
    [Fact]
    public void FindsMixedRetreatWithin350WhenEveryUniformSampleFails()
    {
        var (arms, traces) = DisjointCurvedWindows();
        var core = EndpointStackCore.Create(arms, InterchangeType.Stack, true, 10, 22, Ramp, .16f,
            new CloverleafRoadDesign());
        Assert.True(core.Success, core.Diagnostic);
        for (int distance = 0; distance <= 350; distance += 25)
        {
            var uniform = Attach(core, CutArms(arms, traces, new[] { (float)distance, distance, distance, distance }));
            Assert.False(Safe(uniform),$"Uniform {distance}m unexpectedly passed the complete network checks.");
        }

        var mixed = Attach(core, CutArms(arms, traces, new[] { 75f, 0f, 0f, 0f }));
        Assert.Equal(SpatialCandidateFailure.None, mixed.Failure);
        Assert.Equal(CurveClearanceResult.Clear, EndpointNetworkClearance.Check(mixed, 8.5f, out var detail));
        Assert.Empty(detail);
        Assert.Equal(CurveClearanceResult.Clear, EndpointNetworkSupports.Check(mixed,
            new CloverleafRoadDesign().BridgeRules, Ramp, out detail));
        Assert.Empty(detail);

        var result = EndpointStackSearch.Search(arms, traces, InterchangeType.Stack, true, 10, 22,
            Ramp, .16f, 350, new CloverleafRoadDesign());
        Assert.True(result.Failure == RetreatSearchFailure.None,
            $"{result.Failure}; attempts={result.Attempts}; {result.ClearanceDetail}");
        Assert.All(result.Distances, distance => Assert.InRange(distance, 0, 350));
        Assert.True(result.Distances[0] > result.Distances[1]);
        Assert.Equal(0, result.Distances[1]);
        Assert.Equal(8, result.Cuts.Count);
        Assert.Equal(CurveClearanceResult.Clear, EndpointNetworkClearance.Check(result.Spatial!, 8.5f, out detail));
        Assert.Empty(detail);
    }

    // Catches treating local, single-arm failures as complete-network attempts,
    // and losing the arm that has no connection window in the failure report.
    [Fact]
    public void ReportsTheArmWithoutAWindowWithoutChargingAFullCandidate()
    {
        var arms=EndpointFixtures.DualCross(800).Select(a=>new EndpointArm(a.Index,a.Endpoints.Select(p=>
            new SelectedEndpoint(p.NodeIndex,p.NodeVersion,new Vec3(p.Position.X,a.Index%2==0?10:22,p.Position.Z),
                p.OutwardTangent+new Vec3(0,a.Index==0?.20f:0,0),true,p.TravelOutward,p.Width,p.Lanes,p.Prefab)))).ToArray();
        var traces=arms.SelectMany(a=>a.Endpoints).Select(p=>new RoadApproachTrace(p,new[]{
            new RoadTracePiece(p.NodeIndex+1000,1,Bezier3.Line(p.Position,p.Position+p.OutwardTangent*600),false,p.NodeIndex+2000,1)})).ToArray();
        var result = EndpointStackSearch.Search(arms, traces, InterchangeType.Stack, true, 10, 22,
            Ramp, .16f, 0, new CloverleafRoadDesign());
        Assert.Equal(RetreatSearchFailure.NoCandidate, result.Failure);
        Assert.Equal(0, result.Attempts);
        Assert.True(result.CoreMatched);
        Assert.Contains("Arm 1", result.ClearanceDetail);
        Assert.Contains("no feasible", result.ClearanceDetail);
        Assert.Null(result.Spatial);
        Assert.Empty(result.Cuts);
        Assert.Empty(result.Arms);
        Assert.Empty(result.Distances);
    }

    // Catches silently exceeding the user limit or skipping its exact endpoint
    // when it does not coincide with the 25m local scan spacing.
    [Fact]
    public void SamplesExactNonGridUserLimitWithoutExtendingIt()
    {
        var (arms, traces) = DisjointCurvedWindows(10);
        var core = EndpointStackCore.Create(arms, InterchangeType.Stack, true, 10, 22, Ramp, .16f,
            new CloverleafRoadDesign());
        Assert.True(core.Success, core.Diagnostic);
        Assert.Equal(SpatialCandidateFailure.None,
            Attach(core, CutArms(arms, traces, new[] { 17f, 0f, 0f, 0f })).Failure);
        var result = EndpointStackSearch.Search(arms, traces, InterchangeType.Stack, true, 10, 22,
            Ramp, .16f, 17, new CloverleafRoadDesign());
        Assert.True(result.Failure == RetreatSearchFailure.None, result.ClearanceDetail);
        Assert.Equal(17, result.Distances[0]);
        Assert.All(result.Distances, distance => Assert.InRange(distance, 0, 17));
        Assert.Equal(0, result.Distances[1]);
    }

    // A small core leaves long S approaches that pinch the paired roads together.
    // A later, larger core shortens these approaches and clears the same endpoints.
    // This checks whole-network safety rather than the obsolete global chord angle.
    [Fact]
    public void ContinuesToALaterCoreWhenTheFirstHasNoSafePairedConnection()
    {
        var arms = EndpointFixtures.DualCross(700).Select(a => new EndpointArm(a.Index, a.Endpoints.Select(p =>
            new SelectedEndpoint(p.NodeIndex, p.NodeVersion,
                new Vec3(a.Index%2==1?(p.Position.X>0?650:-650):p.Position.X, a.Index % 2 == 0 ? 10 : 22, p.Position.Z),
                p.OutwardTangent.RotateY((p.TravelOutward?1:-1)*20*(float)Math.PI/180),
                true, p.TravelOutward, p.Width, p.Lanes, p.Prefab)))).ToArray();
        var traces=arms.SelectMany(a=>a.Endpoints).Select(p=>new RoadApproachTrace(p,Array.Empty<RoadTracePiece>())).ToArray();
        var first = EndpointStackCore.Create(arms, InterchangeType.Stack, true, 10, 22, Ramp, .16f,
            new CloverleafRoadDesign());
        Assert.True(first.Success, first.Diagnostic);
        Assert.False(Safe(Attach(first,arms)));
        var result = EndpointStackSearch.Search(arms, traces, InterchangeType.Stack, true, 10, 22,
            Ramp, .16f, 0, new CloverleafRoadDesign());
        Assert.True(result.Failure == RetreatSearchFailure.None, result.ClearanceDetail);
        Assert.True(result.Attempts>1);
        Assert.True(result.ClearanceRejected+result.SupportRejected>0);
        Assert.All(result.Distances, distance => Assert.Equal(0, distance));
        Assert.True(Safe(result.Spatial!));
        Assert.False(first.Segments.All(segment=>result.Spatial!.Segments.Any(actual=>actual.WorldCurve.Equals(segment.WorldCurve)&&actual.PathId==segment.PathId)));
    }

    private static (EndpointArm[] Arms, RoadApproachTrace[] Traces) DisjointCurvedWindows(float firstBendLength = 60)
    {
        var arms = EndpointFixtures.DualCross(800).Select(a => new EndpointArm(a.Index, a.Endpoints.Select(p =>
            new SelectedEndpoint(p.NodeIndex, p.NodeVersion,
                new Vec3(p.Position.X, a.Index % 2 == 0 ? 10 : 22, p.Position.Z),
                p.OutwardTangent+new Vec3(0,a.Index==0?.20f:0,0),
                true, p.TravelOutward, p.Width, p.Lanes, p.Prefab)))).ToArray();
        var traces = arms.SelectMany(arm => arm.Endpoints.Select(port =>
        {
            var axis = new[] { Vec3.UnitZ, Vec3.UnitX, -Vec3.UnitZ, -Vec3.UnitX }[arm.Index];
            float length=arm.Index==1?20:firstBendLength;
            var bendEnd=port.Position+axis*length+new Vec3(0,arm.Index==1?length*.1f:0,0);
            var endDirection=axis+new Vec3(0,arm.Index==1?.20f:0,0);
            var bend=new Bezier3(port.Position,port.Position+port.OutwardTangent*(length/3),
                bendEnd-endDirection*(length/3),bendEnd);
            return new RoadApproachTrace(port, new[]
            {
                new RoadTracePiece(port.NodeIndex + 1000, 1, bend, false, port.NodeIndex + 2000, 1),
                new RoadTracePiece(port.NodeIndex + 3000, 1,
                    Bezier3.Line(bendEnd, bendEnd + endDirection * 450), false, port.NodeIndex + 4000, 1),
            });
        })).ToArray();
        return (arms, traces);
    }

    private static EndpointArm[] CutArms(EndpointArm[] arms, RoadApproachTrace[] traces, float[] distances) =>
        arms.Select(a => new EndpointArm(a.Index, a.Endpoints.Select(p =>
        {
            var cut = RoadRetreat.Cut(traces.Single(t => t.Origin.NodeIndex == p.NodeIndex), distances[a.Index]);
            Assert.Equal(RoadRetreatFailure.None, cut.Failure);
            return cut.Port!;
        }))).ToArray();

    private static bool Safe(SpatialCandidateResult result)=>result.Failure==SpatialCandidateFailure.None
        && EndpointNetworkClearance.Check(result,8.5f,out _)==CurveClearanceResult.Clear
        && EndpointNetworkSupports.Check(result,new CloverleafRoadDesign().BridgeRules,Ramp,out _)==CurveClearanceResult.Clear;

    // The fixture checks a fixed solved core, independently of Search's enumeration,
    // so a future accidental return to uniform distances cannot validate itself.
    private static SpatialCandidateResult Attach(EndpointStackCoreResult core, EndpointArm[] arms) =>
        (SpatialCandidateResult)typeof(EndpointStackSearch).GetMethod("Attach", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new object[] { core, arms, .16f })!;
}
