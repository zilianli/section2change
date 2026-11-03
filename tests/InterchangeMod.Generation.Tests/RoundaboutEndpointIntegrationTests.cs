using InterchangeMod.Generation.Endpoints;
using InterchangeMod.Generation.Geometry;
using Xunit.Abstractions;

namespace InterchangeMod.Generation.Tests;
public class RoundaboutEndpointIntegrationTests
{
    private readonly ITestOutputHelper output;
    public RoundaboutEndpointIntegrationTests(ITestOutputHelper output){this.output=output;}
    [Theory]
    [InlineData(2,1,1)] [InlineData(4,1,3)] [InlineData(6,2,4)]
    public void SetupSelectsActualRingAndMainOnlyNMinusR(int lanes,int ramp,int reduced)
    {
        var a=CompactGridFixtures.Arms(lanes:lanes);
        var s=EndpointRoundaboutSetup.Create(a,CompactGridFixtures.Catalog(),0,0,93,$"Road{ramp}");
        Assert.True(s.Success,s.Diagnostic);Assert.Equal(ramp,s.Ramp!.ForwardLanes);
        Assert.True(s.Ring!.ForwardLanes>=Math.Max(2,lanes));Assert.True(s.MainHeight-s.CrossHeight>s.RequiredClearance);
        Assert.Equal(new[]{0,2},s.Design!.Balances.Select(b=>b.Arm).ToArray());Assert.All(s.Design.Balances,b=>Assert.Equal(reduced,b.Lanes));
        Assert.Contains(s.Design.SupportRules,r=>r.Prefab==s.Ring.Name);
    }
    [Theory]
    [InlineData(true,false,false,.16f)] [InlineData(false,false,true,.30f)]
    [InlineData(true,true,true,.16f)] [InlineData(false,true,false,.08f)]
    public void RealRetreatSearchPreservesAllCertifiedConnections(bool rht,bool curved,bool racetrack,float grade)
    {
        var a=CompactGridFixtures.Arms(rht,mixed:curved);var s=EndpointRoundaboutSetup.Create(a,CompactGridFixtures.Catalog(),0,0,93);
        Assert.True(s.Success,s.Diagnostic);
        var r=EndpointRoundaboutSearch.Search(a,CompactGridFixtures.Traces(a,curved),rht,s.MainHeight,s.CrossHeight,s.Ramp!,s.Ring!,grade,1000,s.Design!,terrainHeight:93,racetrack:racetrack);
        Check(r,s);Assert.Equal(CurveClearanceResult.Clear,EndpointRoundaboutConnectionClearance.Check(r.Spatial!,r.Arms,s.RequiredClearance,out _));
    }
    [Theory]
    [InlineData(true,false,.16f)] [InlineData(false,true,.30f)]
    public void CapturedCurvedGameRoadsGenerateBothRingShapes(bool rht,bool racetrack,float grade)
    {
        var traces=GridCurvedAppearanceTests.Captured(rht);var a=RealCurvedApproachTests.Arms(traces);
        var s=EndpointRoundaboutSetup.Create(a,GridCurvedMergeTests.Catalog(),0,0,478.6323f);
        Assert.True(s.Success,s.Diagnostic);
        var r=EndpointRoundaboutSearch.Search(a,traces,rht,s.MainHeight,s.CrossHeight,s.Ramp!,s.Ring!,grade,1000,s.Design!,terrainHeight:478.6323f,racetrack:racetrack);
        Check(r,s);
        var directory=Environment.GetEnvironmentVariable("ROUNDABOUT_PREVIEW_EXPORT");
        if(!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);float[] P(Vec3 p)=>new[]{p.X,p.Y,p.Z};
            File.WriteAllText(Path.Combine(directory,$"roundabout-{rht}-{racetrack}.json"),System.Text.Json.JsonSerializer.Serialize(new{
                rht,racetrack,grade,distances=r.Distances,segments=r.Spatial!.Segments.Select(t=>new{path=t.PathId,kind=t.Kind.ToString(),width=t.RoadWidth,curve=new[]{P(t.WorldCurve.P0),P(t.WorldCurve.P1),P(t.WorldCurve.P2),P(t.WorldCurve.P3)}})}));
        }
    }
    [Fact]
    public void UnsupportedAndMalformedInputsFailBeforeSearch()
    {
        var a=CompactGridFixtures.Arms(lanes:1);var s=EndpointRoundaboutSetup.Create(a,CompactGridFixtures.Catalog(),0,0,93);
        Assert.False(s.Success);Assert.Contains("两车道",s.Diagnostic);
        s=EndpointRoundaboutSetup.Create(CompactGridFixtures.Arms(),CompactGridFixtures.Catalog(),0,2,93);
        Assert.False(s.Success);Assert.Contains("高差",s.Diagnostic);
        var valid=EndpointRoundaboutSetup.Create(CompactGridFixtures.Arms(),CompactGridFixtures.Catalog(),0,0,93);
        var r=EndpointRoundaboutSearch.Search(CompactGridFixtures.Arms(),Array.Empty<RoadApproachTrace>(),true,valid.MainHeight,valid.CrossHeight,valid.Ramp!,valid.Ring!, .31f,1000,valid.Design!,terrainHeight:93);
        Assert.Equal(RetreatSearchFailure.InvalidInput,r.Failure);Assert.Equal(0,r.Attempts);
    }
    private void Check(RetreatSearchResult r,EndpointRoundaboutSetup s)
    {
        output.WriteLine($"{r.Failure}, attempts={r.Attempts}, detail={r.ClearanceDetail}, cuts={string.Join('/',r.Distances)}");
        Assert.True(r.Failure==RetreatSearchFailure.None,$"{r.Failure}: {r.ClearanceDetail}");Assert.InRange(r.Attempts,1,128);
        Assert.Equal(8,r.Cuts.Count);Assert.All(r.Distances,d=>Assert.InRange(d,0,1000));
        Assert.False(r.CoreFollowsCurve);Assert.True(r.ReferenceFromCuts);
        Assert.Equal(CurveClearanceResult.Clear,EndpointNetworkClearance.Check(r.Spatial!,s.RequiredClearance,out _));
        Assert.Equal(CurveClearanceResult.Clear,EndpointNetworkSupports.Check(r.Spatial!,s.Design!.BridgeRules,s.Ramp!,out _,s.Design.SupportRules));
    }
}
