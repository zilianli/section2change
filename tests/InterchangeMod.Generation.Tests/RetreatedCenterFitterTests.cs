using InterchangeMod.Generation.Endpoints;
using InterchangeMod.Generation.Geometry;
using System.Text.Json;
using Xunit.Abstractions;

namespace InterchangeMod.Generation.Tests;

public class RetreatedCenterFitterTests
{
    private readonly ITestOutputHelper output;
    public RetreatedCenterFitterTests(ITestOutputHelper output){this.output=output;}
    [Theory]
    [InlineData(InterchangeType.Turbine,false,.16f)]
    [InlineData(InterchangeType.Turbine,false,.18f)]
    [InlineData(InterchangeType.Stack,false,.16f)]
    [InlineData(InterchangeType.Stack,true,.16f)]
    public void UsesItsActualRetreatedCentersAndGentlerExternalCurves(InterchangeType type,bool left,float grade)
    {
        var traces=RealCurvedApproachTests.Traces();var arms=RealCurvedApproachTests.Arms(traces);
        var ramp=RealCurvedApproachTests.Ramp();var design=RealCurvedApproachTests.Design();
        var baseline=EndpointStackSearch.Search(arms,traces,type,true,488.6323f,498.6323f,ramp,grade,500,design,left);
        Assert.True(baseline.Failure==RetreatSearchFailure.None,baseline.ClearanceDetail);
        var log=new List<string>();
        var result=EndpointStackFitter.Search(arms,traces,type,true,488.6323f,498.6323f,ramp,grade,500,design,left,diagnostic:log.Add);
        Assert.True(result.Failure==RetreatSearchFailure.None,result.ClearanceDetail);
        Assert.True(result.ReferenceFromCuts,"The old selected-node reference must not masquerade as the fitted reference.\n"+string.Join("\n",log));
        Assert.NotNull(result.ReferenceCenter);Assert.NotNull(result.ApproachQuality);
        Assert.True(result.CoreFollowsCurve,"The real curved-road replay must retain the selected curved core.\n"+string.Join("\n",log));
        Assert.Contains(result.Spatial!.Segments,s=>s.Kind==CloverEdgeKind.Main
            &&new[]{"MainA","MainB","CrossA","CrossB"}.Contains(s.PathId)&&CurvedCoreFollowingTests.Turn(s.WorldCurve)>1e-4);
        Vec3 Mid(int i)=>(result.Arms[i].Endpoints[0].Position+result.Arms[i].Endpoints[1].Position)*.5f;
        double Distance(Vec3 p,Vec3 a,Vec3 b)
        {var d=b-a;return Math.Abs((double)d.X*(p.Z-a.Z)-(double)d.Z*(p.X-a.X))/Math.Sqrt((double)d.X*d.X+(double)d.Z*d.Z);}
        Assert.InRange(Distance(result.ReferenceCenter!.Value,Mid(0),Mid(2)),0,.03);
        Assert.InRange(Distance(result.ReferenceCenter.Value,Mid(1),Mid(3)),0,.03);
        Assert.All(result.Distances,d=>Assert.InRange(d,0,500));Assert.InRange(result.Attempts,1,128);
        Assert.All(result.Cuts,c=>Assert.True(EndpointApproachGeometry.HasRetainedRoom(c)));
        foreach(var s in result.ApproachQuality!.Curves)
        {
            Assert.True(RoadRetreat.HorizontalLength(s.WorldCurve)>=Math.Max(10,s.RoadWidth!.Value));
            Assert.Equal(CurveCurvatureResult.Clear,CurveCurvature.CheckMinimumRadius(s.WorldCurve,2*s.RoadWidth.Value));
        }
        Assert.NotNull(baseline.ApproachQuality);
        Assert.True(result.ApproachQuality.Score<baseline.ApproachQuality!.Score,
            $"Fitted score {result.ApproachQuality.Score:F3} must improve baseline {baseline.ApproachQuality.Score:F3}.");
        Assert.Equal(CurveClearanceResult.Clear,EndpointNetworkClearance.Check(result.Spatial!,8.5f,out _));
        Assert.Equal(CurveClearanceResult.Clear,EndpointNetworkSupports.Check(result.Spatial!,design.BridgeRules,ramp,out _,design.SupportRules));
        output.WriteLine($"{type},left={left},{grade:P0}: before={baseline.ApproachQuality.Score:F3}, after={result.ApproachQuality.Score:F3}; peak {baseline.ApproachQuality.PeakCurvature:F6}->{result.ApproachQuality.PeakCurvature:F6}; jump {baseline.ApproachQuality.CurvatureJump:F6}->{result.ApproachQuality.CurvatureJump:F6}; distances=[{string.Join(",",result.Distances)}], attempts={result.Attempts}");
        if(Environment.GetEnvironmentVariable("RETREATED_PREVIEW_DIR") is string directory)
        {
            Directory.CreateDirectory(directory);
            object Point(Vec3 v)=>new[]{v.X,v.Y,v.Z};
            object Lines(SpatialCandidateResult network)=>network.Segments.Select(s=>new{
                kind=s.Kind.ToString(),width=s.RoadWidth,arm=s.SourceArm,path=s.PathId,
                curve=new[]{Point(s.WorldCurve.P0),Point(s.WorldCurve.P1),Point(s.WorldCurve.P2),Point(s.WorldCurve.P3)}}).ToArray();
            object View(RetreatSearchResult r)=>new{segments=Lines(r.Spatial!),center=r.ReferenceCenter.HasValue?Point(r.ReferenceCenter.Value):null,
                cuts=r.Arms.SelectMany(a=>a.Endpoints).Select(p=>Point(p.Position)).ToArray(),distances=r.Distances,
                curvedCore=r.CoreFollowsCurve,peak=r.ApproachQuality!.PeakCurvature,jump=r.ApproachQuality.CurvatureJump,score=r.ApproachQuality.Score};
            var export=new{type=type.ToString(),left,grade,before=View(baseline),after=View(result)};
            File.WriteAllText(Path.Combine(directory,$"{type}-{left}-{Math.Round(grade*100)}.json"),JsonSerializer.Serialize(export));
        }
    }

    [Fact]
    public void OneCompleteCandidateBudgetDoesNotBecomeAnExtraRefitBudget()
    {
        var traces=RealCurvedApproachTests.Traces();var arms=RealCurvedApproachTests.Arms(traces);
        var result=EndpointStackFitter.Search(arms,traces,InterchangeType.Stack,true,488.6323f,498.6323f,
            RealCurvedApproachTests.Ramp(),.16f,500,RealCurvedApproachTests.Design(),maxCandidates:1);
        Assert.InRange(result.Attempts,0,1);
        if(result.Failure==RetreatSearchFailure.None)
        {Assert.NotNull(result.Spatial);Assert.False(result.ReferenceFromCuts);}
        else{Assert.Null(result.Spatial);Assert.Empty(result.Cuts);}
    }
}
