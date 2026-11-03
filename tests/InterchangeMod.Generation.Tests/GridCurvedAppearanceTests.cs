using System.Text.Json;
using InterchangeMod.Generation.Endpoints;
using InterchangeMod.Generation.Geometry;
using Xunit.Abstractions;

namespace InterchangeMod.Generation.Tests;

public class GridCurvedAppearanceTests
{
    private readonly ITestOutputHelper output;
    public GridCurvedAppearanceTests(ITestOutputHelper output){this.output=output;}
    internal static RoadApproachTrace[] Captured(bool rht)
    {
        var data=RealCurvedApproachTests.Traces();if(rht)return data;
        Vec3 M(Vec3 p)=>new(-p.X,p.Y,p.Z);
        return data.Select(t=>
        {
            var p=t.Origin;
            return new RoadApproachTrace(new SelectedEndpoint(p.NodeIndex,p.NodeVersion,M(p.Position),M(p.OutwardTangent),p.OneWay,p.TravelOutward,p.Width,p.Lanes,p.Prefab),
                t.Pieces.Select(c=>new RoadTracePiece(c.EdgeIndex,c.EdgeVersion,new Bezier3(M(c.Curve.P0),M(c.Curve.P1),M(c.Curve.P2),M(c.Curve.P3)),c.Reversed,c.OuterNodeIndex,c.OuterNodeVersion)));
        }).ToArray();
    }
    internal static double FullRoadPeak(SpatialCandidateResult candidate)=>candidate.Segments.Where(s=>s.Prefab=="Highway Oneway - 4 lanes")
        .Max(s=>Enumerable.Range(0,129).Max(i=>Math.Abs(CurveCurvature.Signed(s.WorldCurve,i/128f))));

    [Theory]
    [InlineData(true,.16f)] [InlineData(false,.16f)]
    [InlineData(true,.30f)] [InlineData(false,.30f)]
    public void CapturedCurvedLayoutIsSmootherThanTheAcceptedStraightCore(bool rht,float grade)
    {
        var traces=Captured(rht);var arms=RealCurvedApproachTests.Arms(traces);
        var setup=EndpointCompactGridSetup.Create(arms,GridCurvedMergeTests.Catalog(),0,0,478.6323f);
        // 中心小转弯至少 40 米（EndpointCompactGridCore.kMinSmallTurn）后核心大一圈，基准切点各外移 25 米
        var distances=grade<.2f?new[]{275f,275f,200f,350f}:new[]{200f,225f,125f,275f};
        var cuts=arms.SelectMany(a=>a.Endpoints.Select(p=>RoadRetreat.Cut(traces.Single(t=>t.Origin.NodeIndex==p.NodeIndex),distances[a.Index]))).ToArray();
        var cutArms=arms.Select(a=>new EndpointArm(a.Index,cuts.Where(c=>a.Endpoints.Any(p=>p.NodeIndex==c.Port!.NodeIndex)).Select(c=>c.Port!))).ToArray();
        SpatialCandidateResult? baseline=null;EndpointApproachQuality? baselineQuality=null;
        foreach(var core in EndpointCompactGridCore.Candidates(cutArms,rht,setup.BaseHeight,setup.LevelGap,setup.Ramp!,setup.Roads,grade,setup.Design!,478.6323f,secantReference:true).Where(c=>c.Success).Take(2))
        {
            foreach(float handle in new[]{0f,.2f,.15f,.25f,.4f,.5f})
            {
                var candidate=EndpointCoreAttachment.Attach(core,cutArms,grade,handle);
                if(candidate.Failure!=SpatialCandidateFailure.None)continue;
                var quality=EndpointApproachQuality.Measure(core,candidate,cuts,traces);
                if(quality.Curves.Any(s=>CurveCurvature.CheckMinimumRadius(s.WorldCurve,2*s.RoadWidth!.Value)!=CurveCurvatureResult.Clear))continue;
                if(EndpointGridConnectionClearance.Check(candidate,arms,setup.RequiredClearance,out _)!=CurveClearanceResult.Clear)continue;
                if(EndpointNetworkClearance.Check(candidate,setup.RequiredClearance,out _)!=CurveClearanceResult.Clear)continue;
                if(EndpointNetworkSupports.Check(candidate,setup.Design!.BridgeRules,setup.Ramp!,out _,setup.Design.SupportRules)!=CurveClearanceResult.Clear)continue;
                baseline=candidate;baselineQuality=quality;break;
            }
            if(baseline!=null)break;
        }
        Assert.NotNull(baseline);Assert.NotNull(baselineQuality);
        var result=EndpointCompactGridSearch.Search(arms,traces,rht,setup.BaseHeight,setup.LevelGap,setup.Ramp!,setup.Roads,grade,1000,setup.Design!,terrainHeight:478.6323f);
        Assert.True(result.Failure==RetreatSearchFailure.None,$"{result.Failure}: {result.ClearanceDetail}");
        var directory=Environment.GetEnvironmentVariable("GRID_APPEARANCE_EXPORT");
        if(!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
            float[] Point(Vec3 p)=>new[]{p.X,p.Y,p.Z};
            object Export(SpatialCandidateResult candidate,IEnumerable<float> retreat)=>new
            {
                peak=FullRoadPeak(candidate),distances=retreat.ToArray(),
                segments=candidate.Segments.Select(s=>new{arm=s.SourceArm,kind=s.Kind.ToString(),width=s.RoadWidth,
                    curve=new[]{Point(s.WorldCurve.P0),Point(s.WorldCurve.P1),Point(s.WorldCurve.P2),Point(s.WorldCurve.P3)}})
            };
            File.WriteAllText(Path.Combine(directory,$"grid-{rht}-{Math.Round(grade*100)}.json"),
                JsonSerializer.Serialize(new{before=Export(baseline!,distances),after=Export(result.Spatial!,result.Distances)}));
        }
        output.WriteLine($"rht={rht}, grade={grade}: baselinePeak={FullRoadPeak(baseline!):F6}, baselineScore={baselineQuality!.Score:F4}; fittedPeak={FullRoadPeak(result.Spatial!):F6}, fittedScore={result.ApproachQuality?.Score:F4}, attempts={result.Attempts}, retreat=[{string.Join(",",result.Distances)}]");
        Assert.True(result.CoreFollowsCurve,"Curved roads should use a certified curved core, not force all alignment into external S curves.");
        Assert.NotNull(result.ApproachQuality);
        Assert.True(result.ApproachQuality!.Score<baselineQuality!.Score);
        Assert.True(FullRoadPeak(result.Spatial!)<FullRoadPeak(baseline!)*.8);
        Assert.InRange(result.Attempts,1,128);
        GridForkAlignmentTests.AssertAligned(result.Spatial!.Segments,rht);
        Assert.Equal(CurveClearanceResult.Clear,EndpointGridConnectionClearance.Check(result.Spatial!,arms,setup.RequiredClearance,out _));
        Assert.Equal(CurveClearanceResult.Clear,EndpointNetworkClearance.Check(result.Spatial!,setup.RequiredClearance,out _));
        Assert.Equal(CurveClearanceResult.Clear,EndpointNetworkSupports.Check(result.Spatial!,setup.Design!.BridgeRules,setup.Ramp!,out _,setup.Design.SupportRules));
        foreach(var cut in result.Cuts)
        {
            var p=cut.Port!;var s=Assert.Single(result.Spatial!.Segments.Where(s=>p.TravelOutward?s.EndNode==p.Position:s.StartNode==p.Position));
            Assert.True(Directions.Dot(Vec3.Normalize(p.TravelOutward?s.WorldCurve.Derivative(1):-s.WorldCurve.Derivative(0)),Vec3.Normalize(p.OutwardTangent))>.9999f);
        }
    }

    [Theory]
    [InlineData(true,.16f)] [InlineData(false,.16f)]
    [InlineData(true,.30f)] [InlineData(false,.30f)]
    public void ExhaustedImprovementBudgetKeepsTheIdenticalCertifiedSeed(bool rht,float grade)
    {
        var traces=Captured(rht);var arms=RealCurvedApproachTests.Arms(traces);
        var setup=EndpointCompactGridSetup.Create(arms,GridCurvedMergeTests.Catalog(),0,0,478.6323f);
        var seed=EndpointCompactGridSearch.FindSafe(arms,traces,rht,setup.BaseHeight,setup.LevelGap,setup.Ramp!,setup.Roads,grade,1000,setup.Design!,terrainHeight:478.6323f);
        Assert.Equal(RetreatSearchFailure.None,seed.Failure);
        var result=EndpointCompactGridSearch.Search(arms,traces,rht,setup.BaseHeight,setup.LevelGap,setup.Ramp!,setup.Roads,grade,1000,setup.Design!,
            maxCandidates:seed.Attempts,terrainHeight:478.6323f);
        Assert.Equal(RetreatSearchFailure.None,result.Failure);Assert.Equal(seed.Attempts,result.Attempts);Assert.False(result.CoreFollowsCurve);
        Assert.Equal(seed.Distances,result.Distances);Assert.Equal(seed.ApproachQuality!.Score,result.ApproachQuality!.Score);
        object Identity(SpatialSegment s)=>new{s.SourceArm,s.Kind,s.StartNode,s.EndNode,s.Prefab,s.RoadWidth,s.PathId,s.WorldCurve.P0,s.WorldCurve.P1,s.WorldCurve.P2,s.WorldCurve.P3};
        Assert.Equal(seed.Spatial!.Segments.Select(Identity),result.Spatial!.Segments.Select(Identity));
    }

    [Theory]
    [InlineData(-.01f)] [InlineData(1.01f)] [InlineData(float.NaN)]
    public void InvalidCurveStrengthIsRejectedAtomically(float strength)
    {
        var arms=CompactGridFixtures.Arms();var setup=EndpointCompactGridSetup.Create(arms,CompactGridFixtures.Catalog(),0,0,93);
        var results=EndpointCompactGridCore.Candidates(arms,true,setup.BaseHeight,setup.LevelGap,setup.Ramp!,setup.Roads,.16f,setup.Design!,93,true,strength).ToArray();
        Assert.All(results,c=>{Assert.False(c.Success);Assert.Empty(c.Segments);Assert.Empty(c.Ports);});
    }
}
