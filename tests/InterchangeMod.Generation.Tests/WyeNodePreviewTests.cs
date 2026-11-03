using InterchangeMod.Generation.Endpoints;
using InterchangeMod.Generation.Geometry;
using InterchangeMod.Generation.Roads;
using Xunit.Abstractions;

namespace InterchangeMod.Generation.Tests;

internal static class WyeNodeFixtures
{
    internal static EndpointArm[] Arms(bool rht=true,float length=100,bool mixed=false,bool skew=false)
        =>Enumerable.Range(0,3).Select(i=>
        {
            var axis=Directions.FromAngle(skew?new[]{0f,110f,235f}[i]:i*120);
            var side=Directions.RightOf(axis)*(rht?1:-1);
            var center=new Vec3(400,mixed?new[]{103f,113f,93f}[i]:103,-230)+axis*length;
            float width=mixed&&i==1?24:20;string prefab=width==24?"Main24":"Main20";
            return new EndpointArm(i,new[]{false,true}.Select(outward=>new SelectedEndpoint(i*10+(outward?2:1),1,
                center+side*((width+8)/2)*(outward?1:-1),axis,true,outward,width,4,prefab)));
        }).ToArray();
    internal static RoadInfo Ramp()=>new("Ramp12",true,2,0,12){ElevatedLength=80,PillarWidth=4};
    internal static RoadApproachTrace[] Traces(EndpointArm[] arms,bool curved=false)=>arms.SelectMany(a=>a.Endpoints).Select(p=>
    {
        var axis=p.OutwardTangent;var end=p.Position+axis*1000+(curved?Directions.RightOf(axis)*100:Vec3.Zero);
        var curve=new Bezier3(p.Position,p.Position+axis*(1000f/3),end-axis*(1000f/3),end);
        return new RoadApproachTrace(p,new[]{new RoadTracePiece(1000+p.NodeIndex,1,curve,false,2000+p.NodeIndex,1)});
    }).ToArray();
    internal static CloverleafRoadDesign Design(EndpointArm[] arms)=>new(bridgeRules:arms.Select(a=>
        new EndpointBridgeRule(a.Index,80,null,a.Endpoints[0].Width,4,new HeightRange(-.4f,5),new HeightRange(-2,5))),
        supportRules:arms.SelectMany(a=>a.Endpoints).Select(p=>p.Prefab).Append("Ramp12").Distinct().Select(name=>new EndpointPrefabSupportRule(name,80,null,4)));
}

public class WyeNodePreviewTests
{
    private readonly ITestOutputHelper output;
    public WyeNodePreviewTests(ITestOutputHelper output){this.output=output;}
    [Fact]
    public void WyeIsSupportedWithoutTeeReordering()
    {
        Assert.True(EndpointThreeWaySearch.Supported(InterchangeType.Wye));
        var arms=ThreeWaySelectedMainTests.Captured("straight").Arms;
        var request=EndpointTypeRequest.Create(InterchangeType.Wye,arms,true,.16f,new[]{100f,200f,300f});
        Assert.True(request.InputReady);
        foreach(var arm in request.Arms)Assert.Equal(arms[arm.Index].Endpoints,arm.Endpoints);
        Assert.Equal(new[]{100f,200f,300f},request.RetreatLimits);
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
    public void SelectedYRoadsGenerateCertifiedSixEndpointPreview(bool rht,float grade,bool mixed,bool curved)
    {
        var arms=WyeNodeFixtures.Arms(rht,mixed:mixed,skew:curved);var traces=WyeNodeFixtures.Traces(arms,curved);
        var design=WyeNodeFixtures.Design(arms);var ramp=WyeNodeFixtures.Ramp();
        var result=EndpointThreeWaySearch.Search(arms,traces,InterchangeType.Wye,rht,103,113,ramp,grade,1000,design,
            directionalTwoLevel:true,terrainHeight:83);
        Assert.True(result.Failure==RetreatSearchFailure.None,$"{result.Failure}: {result.ClearanceDetail}; {result.SupportDetail}");
        output.WriteLine($"Y rht={rht}, grade={grade:P0}, mixed={mixed}, curved={curved}: retreat=[{string.Join(",",result.Distances)}], attempts={result.Attempts}, segments={result.Spatial!.Segments.Count}");
        Assert.Equal(6,result.Cuts.Count);Assert.Equal(3,result.Distances.Count);
        Assert.InRange(result.Attempts,1,128);Assert.All(result.Distances,d=>Assert.InRange(d,0,1000));
        Assert.Equal(CurveClearanceResult.Clear,EndpointNetworkClearance.Check(result.Spatial!,8.5f,out _));
        Assert.Equal(CurveClearanceResult.Clear,EndpointNetworkSupports.Check(result.Spatial!,design.BridgeRules,ramp,out _,design.SupportRules));
        Assert.All(result.Spatial!.Segments,s=>Assert.Equal(VerticalProfileFailure.None,VerticalProfile.CheckGrade(s.WorldCurve,grade)));
        foreach(var cut in result.Cuts)
        {
            var p=cut.Port!;
            var edge=Assert.Single(result.Spatial.Segments.Where(s=>p.TravelOutward?s.EndNode==p.Position:s.StartNode==p.Position));
            var tangent=p.TravelOutward?edge.WorldCurve.Derivative(1):-edge.WorldCurve.Derivative(0);
            Assert.True(Directions.Dot(Vec3.Normalize(tangent),Vec3.Normalize(p.OutwardTangent))>.9999f);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CapturedRealRoadCurveShapesCanBeReposedAsYArms(bool rht)
    {
        var captured=ThreeWaySelectedMainTests.Captured("curved");
        var transformed=new List<RoadApproachTrace>();
        foreach(var arm in captured.Arms)
        {
            var mid=(arm.Endpoints[0].Position+arm.Endpoints[1].Position)*.5f;
            var oldAxis=Vec3.Normalize(arm.Endpoints.Select(p=>Vec3.Normalize(new Vec3(p.OutwardTangent.X,0,p.OutwardTangent.Z))).Aggregate(Vec3.Zero,(a,b)=>a+b));
            var oldRight=Directions.RightOf(oldAxis);var axis=Directions.FromAngle(23+arm.Index*120);var right=Directions.RightOf(axis);
            var target=new Vec3(450,488.6323f,-320)+axis*100;
            Vec3 Rotate(Vec3 v)=>right*(Directions.Dot(v,oldRight)*(rht?1:-1))+axis*Directions.Dot(v,oldAxis)+new Vec3(0,v.Y,0);
            Vec3 P(Vec3 v)=>target+Rotate(v-mid);
            foreach(var original in arm.Endpoints)
            {
                var trace=captured.Traces.Single(t=>t.Origin.NodeIndex==original.NodeIndex);
                var port=new SelectedEndpoint(original.NodeIndex,original.NodeVersion,P(original.Position),Rotate(original.OutwardTangent),
                    true,original.TravelOutward,original.Width,original.Lanes,original.Prefab);
                transformed.Add(new RoadApproachTrace(port,trace.Pieces.Select(p=>new RoadTracePiece(p.EdgeIndex,p.EdgeVersion,
                    new Bezier3(P(p.Curve.P0),P(p.Curve.P1),P(p.Curve.P2),P(p.Curve.P3)),p.Reversed,p.OuterNodeIndex,p.OuterNodeVersion))));
            }
        }
        var traces=transformed.ToArray();
        var arms=Enumerable.Range(0,3).Select(i=>new EndpointArm(i,traces.Skip(i*2).Take(2).Select(t=>t.Origin))).ToArray();
        var catalog=arms.SelectMany(a=>a.Endpoints).GroupBy(p=>p.Prefab).Select(g=>new RoadInfo(g.Key,true,4,0,20,
            groundHeight:new HeightRange(-.4f,5),elevatedHeight:new HeightRange(-2,5)){ElevatedLength=80,PillarWidth=4})
            .Append(new RoadInfo("Highway Oneway - 2 lanes",true,2,0,12,groundHeight:new HeightRange(-.4f,5),elevatedHeight:new HeightRange(-2,5))
                {ElevatedLength=80,PillarWidth=4}).ToArray();
        var setup=EndpointWyeSetup.Create(arms,catalog,0,0,captured.Terrain);
        Assert.True(setup.Success,setup.Diagnostic);
        var design=new CloverleafRoadDesign(bridgeRules:setup.BridgeRules,supportRules:catalog.Select(r=>new EndpointPrefabSupportRule(r.Name,80,null,4)));
        var result=EndpointThreeWaySearch.Search(arms,traces,InterchangeType.Wye,rht,setup.BaseHeight,setup.UpperHeight,
            setup.Ramp!,.16f,1000,design,terrainHeight:captured.Terrain);
        Assert.True(result.Failure==RetreatSearchFailure.None,$"{result.Failure}: {result.ClearanceDetail}; {result.SupportDetail}");
        Assert.Equal(6,result.Cuts.Count);Assert.Equal(CurveClearanceResult.Clear,EndpointNetworkClearance.Check(result.Spatial!,setup.RequiredClearance,out _));
        output.WriteLine($"Reposed captured curves rht={rht}: retreat=[{string.Join(",",result.Distances)}], attempts={result.Attempts}, segments={result.Spatial!.Segments.Count}");
    }

    [Fact]
    public void NoRetreatRoomAndUnknownTerrainProduceNoPartialNetwork()
    {
        var arms=WyeNodeFixtures.Arms(length:10);var traces=WyeNodeFixtures.Traces(arms);var design=WyeNodeFixtures.Design(arms);
        foreach(float? terrain in new float?[]{null,83})
        {
            var result=EndpointThreeWaySearch.Search(arms,traces,InterchangeType.Wye,true,103,113,WyeNodeFixtures.Ramp(),.16f,0,design,terrainHeight:terrain);
            Assert.NotEqual(RetreatSearchFailure.None,result.Failure);Assert.Null(result.Spatial);
            Assert.Empty(result.Cuts);Assert.Empty(result.Arms);Assert.InRange(result.Attempts,0,128);
        }
    }
}
