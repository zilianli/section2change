using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit.Abstractions;
using InterchangeMod.Generation.Endpoints;
using InterchangeMod.Generation.Geometry;

namespace InterchangeMod.Generation.Tests;

public class ThreeWayRealRoadTests
{
    private readonly ITestOutputHelper output;
    public ThreeWayRealRoadTests(ITestOutputHelper output){this.output=output;}
    [Theory]
    [InlineData(InterchangeType.Trumpet)]
    [InlineData(InterchangeType.DirectionalT)]
    [InlineData(InterchangeType.SemiDirectionalT)]
    public void FourLaneRoadsWithMixedHeightsUseActualPrefabSupportRules(InterchangeType type)
    {
        var arms=ThreeWayFixtures.Arms(length:100);
        arms=arms.Select(a=>new EndpointArm(a.Index,a.Endpoints.Select(p=>
        {
            var side=Directions.RightOf(p.OutwardTangent);var center=(a.Endpoints[0].Position+a.Endpoints[1].Position)*.5f;
            return new SelectedEndpoint(p.NodeIndex,p.NodeVersion,center+side*(p.TravelOutward?14:-14)
                +new Vec3(0,a.Index==1?10:0,0),p.OutwardTangent,true,p.TravelOutward,20,4,"Highway Oneway - 4 lanes");
        }).ToArray())).ToArray();
        var design=RealCurvedApproachTests.Design();
        design=new CloverleafRoadDesign(design.Balances.Where(b=>b.Arm<3),bridgeRules:design.BridgeRules.Where(b=>b.Arm<3),supportRules:design.SupportRules);
        var result=EndpointThreeWaySearch.Search(arms,ThreeWayFixtures.Traces(arms),type,true,103,113,
            RealCurvedApproachTests.Ramp(),.16f,1000,design,terrainHeight:103);
        CheckAndExport(result,type,"mixed-four-lane");
        foreach(var a in result.Arms)foreach(var p in a.Endpoints)
            Assert.Equal(a.Index==1?113:103,p.Position.Y,3);
    }
    [Theory]
    [InlineData(InterchangeType.Trumpet)]
    [InlineData(InterchangeType.DirectionalT)]
    [InlineData(InterchangeType.SemiDirectionalT)]
    public void CapturedCurvedCorridorsCanConnectToThreeWayCore(InterchangeType type)
    {
        var all=RealCurvedApproachTests.Traces();var arms=RealCurvedApproachTests.Arms(all).Take(3).ToArray();
        var ids=arms.SelectMany(a=>a.Endpoints).Select(p=>p.NodeIndex).ToHashSet();var traces=all.Where(t=>ids.Contains(t.Origin.NodeIndex)).ToArray();
        var full=RealCurvedApproachTests.Design();
        var design=new CloverleafRoadDesign(full.Balances.Where(b=>b.Arm<3),bridgeRules:full.BridgeRules.Where(b=>b.Arm<3),supportRules:full.SupportRules);
        var result=EndpointThreeWaySearch.Search(arms,traces,type,true,488.6323f,498.6323f,
            RealCurvedApproachTests.Ramp(),.16f,1000,design,terrainHeight:488.6323f);
        CheckAndExport(result,type,"captured-curves");
    }
    private void CheckAndExport(RetreatSearchResult result,InterchangeType type,string scene)
    {
        Assert.True(result.Failure==RetreatSearchFailure.None,$"{result.Failure}: {result.ClearanceDetail}; {result.SupportDetail}");
        Assert.NotNull(result.Spatial);Assert.Equal(6,result.Cuts.Count);
        Assert.All(result.Distances,d=>Assert.InRange(d,0,1000));Assert.InRange(result.Attempts,1,128);
        Assert.Equal(CurveClearanceResult.Clear,EndpointNetworkClearance.Check(result.Spatial!,8.5f,out _));
        output.WriteLine($"{type}/{scene}: distances=[{string.Join(",",result.Distances)}], attempts={result.Attempts}, segments={result.Spatial!.Segments.Count}");
        if(Environment.GetEnvironmentVariable("THREE_WAY_PREVIEW_DIR") is string directory)
        {
            Directory.CreateDirectory(directory);object P(Vec3 v)=>new[]{v.X,v.Y,v.Z};
            var data=new{type=type.ToString(),scene,distances=result.Distances,segments=result.Spatial!.Segments.Select(s=>new{
                kind=s.Kind.ToString(),arm=s.SourceArm,path=s.PathId,width=s.RoadWidth,
                curve=new[]{P(s.WorldCurve.P0),P(s.WorldCurve.P1),P(s.WorldCurve.P2),P(s.WorldCurve.P3)}}),
                cuts=result.Arms.SelectMany(a=>a.Endpoints.Select(p=>new{arm=a.Index,outward=p.TravelOutward,position=P(p.Position)}))};
            File.WriteAllText(Path.Combine(directory,$"{type}-{scene}.json"),JsonSerializer.Serialize(data));
        }
    }
}
