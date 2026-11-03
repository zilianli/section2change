using InterchangeMod.Generation.Endpoints;
using InterchangeMod.Generation.Geometry;
using InterchangeMod.Generation.Roads;
using Xunit.Abstractions;

namespace InterchangeMod.Generation.Tests;

internal static class CompactGridFixtures
{
    internal static EndpointArm[] Arms(bool rht=true,int lanes=4,bool mixed=false,float distance=100)=>Enumerable.Range(0,4).Select(i=>
    {
        var axis=Directions.FromAngle(i*90);var side=Directions.RightOf(axis)*(rht?1:-1);float width=4+4*lanes;
        var mid=new Vec3(400,mixed?new[]{103f,113f,98f,108f}[i]:103,-230)+axis*distance;
        return new EndpointArm(i,new[]{false,true}.Select(outward=>new SelectedEndpoint(100+i*2+(outward?1:0),1,
            mid+side*((width+8)/2)*(outward?1:-1),axis,true,outward,width,lanes,$"Road{lanes}")));
    }).ToArray();
    internal static RoadInfo[] Catalog()=>Enumerable.Range(1,8).Select(n=>new RoadInfo($"Road{n}",true,n,0,4+4*n,
        groundHeight:new HeightRange(-.4f,5),elevatedHeight:new HeightRange(-2,5)){ElevatedLength=80,PillarWidth=4}).ToArray();
    internal static RoadApproachTrace[] Traces(EndpointArm[] arms,bool curved=false)=>arms.SelectMany(a=>a.Endpoints).Select(p=>
    {
        var v=p.OutwardTangent;var end=p.Position+v*1000+(curved?Directions.RightOf(v)*60:Vec3.Zero);
        return new RoadApproachTrace(p,new[]{new RoadTracePiece(p.NodeIndex+1000,1,
            new Bezier3(p.Position,p.Position+v*(1000f/3),end-v*(1000f/3),end),false,p.NodeIndex+2000,1)});
    }).ToArray();
}

public class CompactGridIntegrationTests
{
    private readonly ITestOutputHelper output;
    public CompactGridIntegrationTests(ITestOutputHelper output){this.output=output;}
    [Fact]
    public void OverwidePreferredRampFallsBackToACompatibleRealCollector()
    {
        var arms=CompactGridFixtures.Arms();
        var wide=new RoadInfo("WideRamp",true,1,0,18){ElevatedLength=80,PillarWidth=4};
        var setup=EndpointCompactGridSetup.Create(arms,CompactGridFixtures.Catalog().Append(wide).ToArray(),0,0,93,"WideRamp");
        Assert.True(setup.Success,setup.Diagnostic);Assert.Equal("Road1",setup.Ramp!.Name);
        Assert.All(setup.Roads,r=>Assert.True(r.Collector.Width>=setup.Ramp.Width));
    }
    [Fact]
    public void BelowTerrainBaseIsRejectedBeforeSearch()
    {
        var setup=EndpointCompactGridSetup.Create(CompactGridFixtures.Arms(),CompactGridFixtures.Catalog(),-30,0,93);
        Assert.False(setup.Success);Assert.Null(setup.Ramp);Assert.Empty(setup.Roads);
    }
    [Theory]
    [InlineData(true,false)]
    [InlineData(false,false)]
    [InlineData(true,true)]
    [InlineData(false,true)]
    public void WiderRampsOrDifferentRoadFamiliesRetainTheirExactPrefabs(bool rht,bool differentFamilies)
    {
        var arms=CompactGridFixtures.Arms(rht,lanes:6);
        if(differentFamilies)
        {
            var narrow=CompactGridFixtures.Arms(rht,lanes:4);
            arms=arms.Select(a=>a.Index%2==0?narrow[a.Index]:a).ToArray();
        }
        var setup=EndpointCompactGridSetup.Create(arms,CompactGridFixtures.Catalog(),0,0,93,differentFamilies?null:"Road2");
        Assert.True(setup.Success,setup.Diagnostic);
        var result=EndpointCompactGridSearch.Search(arms,CompactGridFixtures.Traces(arms),rht,setup.BaseHeight,setup.LevelGap,
            setup.Ramp!,setup.Roads,.16f,1000,setup.Design!,terrainHeight:93);
        Assert.True(result.Failure==RetreatSearchFailure.None,$"{result.Failure}: {result.ClearanceDetail}");
        Assert.All(setup.Roads,r=>Assert.Contains(result.Spatial!.Segments,s=>s.Prefab==r.Through.Name));
        Assert.All(setup.Roads,r=>Assert.Contains(result.Spatial!.Segments,s=>s.Prefab==r.Collector.Name));
        Assert.Equal(CurveClearanceResult.Clear,EndpointNetworkSupports.Check(result.Spatial!,setup.Design!.BridgeRules,setup.Ramp!,out _,setup.Design.SupportRules));
    }
    [Theory]
    [InlineData(3,1,1,2)]
    [InlineData(4,1,2,2)]
    [InlineData(6,1,4,2)]
    [InlineData(6,2,2,4)]
    public void SetupMatchesActualRoadsAndConservesAllLanes(int lanes,int rampLanes,int throughLanes,int collectorLanes)
    {
        var arms=CompactGridFixtures.Arms(lanes:lanes);var setup=EndpointCompactGridSetup.Create(arms,CompactGridFixtures.Catalog(),0,0,93,$"Road{rampLanes}");
        Assert.True(setup.Success,setup.Diagnostic);Assert.Equal(rampLanes,setup.Ramp!.LanesPerDirection);
        Assert.All(setup.Roads,r=>{Assert.Equal(throughLanes,r.Through.LanesPerDirection);Assert.Equal(collectorLanes,r.Collector.LanesPerDirection);
            Assert.Equal(lanes,r.Through.LanesPerDirection+r.Collector.LanesPerDirection);});
        Assert.Equal(103,setup.BaseHeight);Assert.True(setup.LevelGap>=10);Assert.Equal(4,setup.Layers.Count);
        Assert.True(setup.Layers[1]-93>=8.5f);Assert.Empty(setup.Design!.Balances);
    }

    [Theory]
    [InlineData("two-lanes")]
    [InlineData("missing-collector")]
    [InlineData("gap")]
    [InlineData("nan")]
    [InlineData("reverse")]
    public void SetupRejectsUnsupportedDataWithNoPartialPlan(string reason)
    {
        var arms=CompactGridFixtures.Arms(lanes:reason=="two-lanes"?2:4);var catalog=CompactGridFixtures.Catalog();
        if(reason=="missing-collector")catalog=catalog.Where(r=>r.ForwardLanes!=2).ToArray();
        if(reason=="reverse")catalog=catalog.Where(r=>r.ForwardLanes!=1).Append(new RoadInfo("Reverse",true,0,1,8)).ToArray();
        var result=EndpointCompactGridSetup.Create(arms,catalog,0,reason=="gap"?8:0,reason=="nan"?float.NaN:93);
        Assert.False(result.Success);Assert.Null(result.Ramp);Assert.Empty(result.Roads);Assert.Null(result.Design);
        Assert.False(string.IsNullOrWhiteSpace(result.Diagnostic));
    }

    [Theory]
    [InlineData(true,.08f,false,false)]
    [InlineData(false,.08f,false,false)]
    [InlineData(true,.16f,false,false)]
    [InlineData(false,.16f,false,false)]
    [InlineData(true,.30f,false,false)]
    [InlineData(false,.30f,false,false)]
    [InlineData(true,.16f,true,true)]
    [InlineData(false,.16f,true,true)]
    public void EightSelectedEndpointsProduceCertifiedGridPreview(bool rht,float grade,bool mixed,bool curved)
    {
        var arms=CompactGridFixtures.Arms(rht,mixed:mixed);var setup=EndpointCompactGridSetup.Create(arms,CompactGridFixtures.Catalog(),0,0,93);
        Assert.True(setup.Success,setup.Diagnostic);
        var result=EndpointCompactGridSearch.Search(arms,CompactGridFixtures.Traces(arms,curved),rht,setup.BaseHeight,setup.LevelGap,
            setup.Ramp!,setup.Roads,grade,1000,setup.Design!,terrainHeight:93);
        Assert.True(result.Failure==RetreatSearchFailure.None,$"{result.Failure}: {result.ClearanceDetail}; {result.SupportDetail}");
        Assert.Equal(8,result.Cuts.Count);Assert.Equal(4,result.Distances.Count);Assert.All(result.Distances,d=>Assert.InRange(d,0,1000));
        Assert.InRange(result.Attempts,1,128);Assert.True(result.ReferenceFromCuts);
        Assert.Equal(CurveClearanceResult.Clear,EndpointNetworkClearance.Check(result.Spatial!,setup.RequiredClearance,out _));
        Assert.Equal(CurveClearanceResult.Clear,EndpointNetworkSupports.Check(result.Spatial!,setup.Design!.BridgeRules,setup.Ramp!,out _,setup.Design.SupportRules));
        Assert.All(result.Spatial!.Segments,s=>Assert.Equal(VerticalProfileFailure.None,VerticalProfile.CheckGrade(s.WorldCurve,grade)));
        foreach(var cut in result.Cuts)
        {
            var p=cut.Port!;var edge=Assert.Single(result.Spatial.Segments.Where(s=>p.TravelOutward?s.EndNode==p.Position:s.StartNode==p.Position));
            var tangent=p.TravelOutward?edge.WorldCurve.Derivative(1):-edge.WorldCurve.Derivative(0);
            Assert.True(Directions.Dot(Vec3.Normalize(tangent),Vec3.Normalize(p.OutwardTangent))>.9999f);
        }
        output.WriteLine($"Grid rht={rht}, grade={grade:P0}, mixed={mixed}, curved={curved}: retreat=[{string.Join(",",result.Distances)}], attempts={result.Attempts}, segments={result.Spatial!.Segments.Count}");
    }

    [Fact]
    public void ZeroRoomFailureIsAtomicAndBounded()
    {
        var arms=CompactGridFixtures.Arms(distance:10);var setup=EndpointCompactGridSetup.Create(arms,CompactGridFixtures.Catalog(),0,0,93);
        var result=EndpointCompactGridSearch.Search(arms,CompactGridFixtures.Traces(arms),true,setup.BaseHeight,setup.LevelGap,
            setup.Ramp!,setup.Roads,.16f,0,setup.Design!,terrainHeight:93);
        Assert.NotEqual(RetreatSearchFailure.None,result.Failure);Assert.Null(result.Spatial);Assert.Empty(result.Cuts);Assert.Empty(result.Arms);
        Assert.InRange(result.Attempts,0,128);
    }
}
