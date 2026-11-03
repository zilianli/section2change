using System.Text.Json;
using InterchangeMod.Generation.Endpoints;
using InterchangeMod.Generation.Geometry;
using Xunit.Abstractions;

namespace InterchangeMod.Generation.Tests;

public sealed class RoundaboutCurvedAppearanceTests
{
    private const float Terrain=478.6323f;
    private readonly ITestOutputHelper output;
    public RoundaboutCurvedAppearanceTests(ITestOutputHelper output){this.output=output;}

    [Theory]
    [InlineData(true,false,.16f)]
    [InlineData(false,true,.30f)]
    public void CapturedRoundaboutConnectionsImproveTheFirstSafeCurvature(bool rht,bool racetrack,float grade)
    {
        var traces=GridCurvedAppearanceTests.Captured(rht);var arms=RealCurvedApproachTests.Arms(traces);
        var positions=arms.SelectMany(a=>a.Endpoints).Select(p=>p.Position).ToArray();
        var setup=EndpointRoundaboutSetup.Create(arms,GridCurvedMergeTests.Catalog(),0,0,Terrain);
        Assert.True(setup.Success,setup.Diagnostic);
        var seed=Seed(arms,traces,setup,rht,racetrack,grade);
        Assert.Equal(RetreatSearchFailure.None,seed.Failure);Assert.NotNull(seed.ApproachQuality);
        var result=EndpointRoundaboutSearch.Search(arms,traces,rht,setup.MainHeight,setup.CrossHeight,setup.Ramp!,setup.Ring!,
            grade,1000,setup.Design!,terrainHeight:Terrain,racetrack:racetrack);
        Assert.Equal(RetreatSearchFailure.None,result.Failure);Assert.NotNull(result.ApproachQuality);
        var before=seed.ApproachQuality!;var after=result.ApproachQuality!;
        output.WriteLine($"rht={rht}, racetrack={racetrack}, grade={grade}: seed peak={before.PeakCurvature:F8}, jump={before.CurvatureJump:F8}, score={before.Score:F5}, retreat=[{string.Join(',',seed.Distances)}]; fitted peak={after.PeakCurvature:F8}, jump={after.CurvatureJump:F8}, score={after.Score:F5}, attempts={result.Attempts}, retreat=[{string.Join(',',result.Distances)}]");
        Export(seed,result,rht,racetrack,grade);
        Assert.True(after.Score<before.Score*.8,$"Quality score did not improve sufficiently: {before.Score} -> {after.Score}");
        Assert.True(after.PeakCurvature<before.PeakCurvature*.8,$"Peak connection curvature did not improve sufficiently: {before.PeakCurvature} -> {after.PeakCurvature}");
        Assert.True(after.CurvatureJump<before.CurvatureJump*.8,$"Connection curvature jump did not improve sufficiently: {before.CurvatureJump} -> {after.CurvatureJump}");
        Assert.InRange(result.Attempts,seed.Attempts+1,Math.Min(128,seed.Attempts+48));Assert.Equal(8,result.Cuts.Count);
        Assert.All(result.Distances,d=>Assert.InRange(d,0,1000));
        Assert.Equal(positions,arms.SelectMany(a=>a.Endpoints).Select(p=>p.Position));
        Assert.False(result.CoreFollowsCurve);Assert.True(result.ReferenceFromCuts);
        Assert.Equal(CurveClearanceResult.Clear,EndpointRoundaboutConnectionClearance.Check(result.Spatial!,result.Arms,setup.RequiredClearance,out _));
        Assert.Equal(CurveClearanceResult.Clear,EndpointNetworkClearance.Check(result.Spatial!,setup.RequiredClearance,out _));
        Assert.Equal(CurveClearanceResult.Clear,EndpointNetworkSupports.Check(result.Spatial!,setup.Design!.BridgeRules,setup.Ramp!,out _,setup.Design.SupportRules));
        foreach(var segment in result.Spatial!.Segments)
        {
            Assert.Equal(VerticalProfileFailure.None,VerticalProfile.CheckGrade(segment.WorldCurve,grade));
            Assert.Equal(CurveCurvatureResult.Clear,CurveCurvature.CheckMinimumRadius(segment.WorldCurve,2*segment.RoadWidth!.Value));
        }
        foreach(var cut in result.Cuts)
        {
            Assert.True(EndpointApproachGeometry.HasRetainedRoom(cut));var p=cut.Port!;
            var connection=Assert.Single(result.Spatial.Segments.Where(s=>p.TravelOutward?s.EndNode==p.Position:s.StartNode==p.Position));
            var tangent=p.TravelOutward?connection.WorldCurve.Derivative(1):-connection.WorldCurve.Derivative(0);
            Assert.True(Directions.Dot(Vec3.Normalize(tangent),Vec3.Normalize(p.OutwardTangent))>.9999f);
        }
    }

    [Theory]
    [InlineData(true,false,.16f)]
    [InlineData(false,true,.30f)]
    public void AnExhaustedSharedBudgetReturnsTheIdenticalCertifiedSeed(bool rht,bool racetrack,float grade)
    {
        var traces=GridCurvedAppearanceTests.Captured(rht);var arms=RealCurvedApproachTests.Arms(traces);
        var setup=EndpointRoundaboutSetup.Create(arms,GridCurvedMergeTests.Catalog(),0,0,Terrain);
        var seed=Seed(arms,traces,setup,rht,racetrack,grade);Assert.Equal(RetreatSearchFailure.None,seed.Failure);
        var result=EndpointRoundaboutSearch.Search(arms,traces,rht,setup.MainHeight,setup.CrossHeight,setup.Ramp!,setup.Ring!,grade,
            1000,setup.Design!,maxCandidates:seed.Attempts,terrainHeight:Terrain,racetrack:racetrack);
        AssertSame(seed,result);Assert.Equal(seed.Attempts,result.Attempts);
    }

    [Theory]
    [InlineData(true,false)] [InlineData(false,true)]
    public void AlreadyStraightConnectionsKeepTheCertifiedSeedWithoutExtraAttempts(bool rht,bool racetrack)
    {
        var arms=CompactGridFixtures.Arms(rht,distance:700);var traces=CompactGridFixtures.Traces(arms);
        var setup=EndpointRoundaboutSetup.Create(arms,CompactGridFixtures.Catalog(),0,0,93);
        var seed=Seed(arms,traces,setup,rht,racetrack,.16f,terrain:93);
        Assert.Equal(RetreatSearchFailure.None,seed.Failure);
        output.WriteLine($"Straight rht={rht}, racetrack={racetrack}: seed peak={seed.ApproachQuality!.PeakCurvature:R}, jump={seed.ApproachQuality.CurvatureJump:R}, score={seed.ApproachQuality.Score:R}, attempts={seed.Attempts}");
        Assert.InRange(seed.ApproachQuality.PeakCurvature,0,1e-4);Assert.InRange(seed.ApproachQuality.CurvatureJump,0,1e-4);
        var result=EndpointRoundaboutSearch.Search(arms,traces,rht,setup.MainHeight,setup.CrossHeight,setup.Ramp!,setup.Ring!,
            .16f,1000,setup.Design!,terrainHeight:93,racetrack:racetrack);
        AssertSame(seed,result);Assert.Equal(seed.Attempts,result.Attempts);
    }

    [Theory]
    [InlineData(.31f,1000f,128)] [InlineData(.16f,1001f,128)] [InlineData(.16f,1000f,0)]
    public void InvalidSearchCannotEnterTheImprovementPass(float grade,float retreat,int budget)
    {
        var arms=CompactGridFixtures.Arms();var traces=CompactGridFixtures.Traces(arms);
        var setup=EndpointRoundaboutSetup.Create(arms,CompactGridFixtures.Catalog(),0,0,93);
        var result=EndpointRoundaboutSearch.Search(arms,traces,true,setup.MainHeight,setup.CrossHeight,setup.Ramp!,setup.Ring!,
            grade,retreat,setup.Design!,maxCandidates:budget,terrainHeight:93);
        Assert.Equal(RetreatSearchFailure.InvalidInput,result.Failure);Assert.Equal(0,result.Attempts);
        Assert.Null(result.Spatial);Assert.Empty(result.Cuts);Assert.Null(result.ApproachQuality);
    }

    [Fact]
    public void AFailedSafeSearchDoesNotReturnAnUncertifiedImprovement()
    {
        var traces=GridCurvedAppearanceTests.Captured(true);var arms=RealCurvedApproachTests.Arms(traces);
        var setup=EndpointRoundaboutSetup.Create(arms,GridCurvedMergeTests.Catalog(),0,0,Terrain);
        var seed=Seed(arms,traces,setup,true,false,.16f,maxRetreat:0);
        Assert.NotEqual(RetreatSearchFailure.None,seed.Failure);
        var result=EndpointRoundaboutSearch.Search(arms,traces,true,setup.MainHeight,setup.CrossHeight,setup.Ramp!,setup.Ring!,
            .16f,0,setup.Design!,terrainHeight:Terrain);
        Assert.Equal(seed.Failure,result.Failure);Assert.Equal(seed.Attempts,result.Attempts);
        Assert.Null(result.Spatial);Assert.Empty(result.Cuts);Assert.Null(result.ApproachQuality);
    }

    private static RetreatSearchResult Seed(EndpointArm[] arms,RoadApproachTrace[] traces,EndpointRoundaboutSetup setup,
        bool rht,bool racetrack,float grade,float maxRetreat=1000,float terrain=Terrain)
        =>EndpointCompactGridSearch.FindSafe(arms,traces,rht,setup.MainHeight,setup.CrossHeight,setup.Ramp!,null,grade,
            maxRetreat,setup.Design!,terrainHeight:terrain,roundaboutRing:setup.Ring!,racetrack:racetrack);

    private static void AssertSame(RetreatSearchResult seed,RetreatSearchResult result)
    {
        Assert.Equal(seed.Failure,result.Failure);Assert.Equal(seed.Distances,result.Distances);
        Assert.Equal(seed.ApproachQuality!.Score,result.ApproachQuality!.Score);
        Assert.Equal(seed.ReferenceCenter,result.ReferenceCenter);Assert.Equal(seed.ReferenceFromCuts,result.ReferenceFromCuts);
        Assert.False(result.CoreFollowsCurve);
        object Identity(SpatialSegment s)=>new{s.SourceArm,s.Kind,s.StartNode,s.EndNode,s.Prefab,s.RoadWidth,s.PathId,
            s.WorldCurve.P0,s.WorldCurve.P1,s.WorldCurve.P2,s.WorldCurve.P3};
        Assert.Equal(seed.Spatial!.Segments.Select(Identity),result.Spatial!.Segments.Select(Identity));
    }

    private static void Export(RetreatSearchResult seed,RetreatSearchResult result,bool rht,bool racetrack,float grade)
    {
        var directory=Environment.GetEnvironmentVariable("ROUNDABOUT_APPEARANCE_EXPORT");
        if(string.IsNullOrWhiteSpace(directory))return;
        Directory.CreateDirectory(directory);float[] Point(Vec3 p)=>new[]{p.X,p.Y,p.Z};
        object Candidate(RetreatSearchResult value)=>new{distances=value.Distances,quality=new{value.ApproachQuality!.PeakCurvature,
            value.ApproachQuality.CurvatureJump,value.ApproachQuality.Score},segments=value.Spatial!.Segments.Select(s=>new{
                path=s.PathId,arm=s.SourceArm,kind=s.Kind.ToString(),width=s.RoadWidth,
                curve=new[]{Point(s.WorldCurve.P0),Point(s.WorldCurve.P1),Point(s.WorldCurve.P2),Point(s.WorldCurve.P3)}})};
        File.WriteAllText(Path.Combine(directory,$"roundabout-{rht}-{racetrack}.json"),
            JsonSerializer.Serialize(new{rht,racetrack,grade,before=Candidate(seed),after=Candidate(result)}));
    }
}
