using System.Reflection;
using System.Text.Json;
using InterchangeMod.Generation.Endpoints;
using InterchangeMod.Generation.Geometry;

namespace InterchangeMod.Generation.Tests;

public class ThreeWaySelectedMainTests
{
    public static IEnumerable<object[]> SelectionOrders()=>
        new[]{InterchangeType.Trumpet,InterchangeType.DirectionalT,InterchangeType.SemiDirectionalT}
        .SelectMany(type=>new[]{true,false}.SelectMany(rht=>
            new[]{new[]{0,1,2},new[]{0,2,1},new[]{1,0,2},new[]{1,2,0},new[]{2,0,1},new[]{2,1,0}}
                .Select(order=>new object[]{type,rht,order})));

    [Theory,MemberData(nameof(SelectionOrders))]
    public void AnyStartAndClockwiseOrderPreservesRoadsAndPerArmRetreat(InterchangeType type,bool rht,int[] order)
    {
        var original=ThreeWayFixtures.Arms(rht);
        var selected=order.Select((from,index)=>new EndpointArm(index,original[from].Endpoints)).ToArray();
        var limits=new[]{100f,200f,300f};
        Assert.True(NodeFeedback.ReadyForAutoPreview(type,selected));
        var request=EndpointTypeRequest.Create(type,selected,rht,.16f,limits);
        Assert.True(request.InputReady);
        Assert.Equal(rht,request.RightHandTraffic);
        Assert.Equal(original[1].Endpoints,request.Arms[1].Endpoints);
        Assert.Equal(new[]{0,1,2},request.Arms.Select(a=>a.Index));
        foreach(var arm in request.Arms)
        {
            var input=Array.FindIndex(selected,a=>a.Endpoints.SequenceEqual(arm.Endpoints));
            Assert.Equal(limits[input],request.RetreatLimits[arm.Index]);
            Assert.All(arm.Endpoints,p=>Assert.Same(original.Single(a=>a.Endpoints.Contains(p)).Endpoints.Single(old=>old==p),p));
        }
        Assert.Equal(original.Where(a=>a.Index!=1).SelectMany(a=>a.Endpoints).Select(p=>p.NodeIndex).OrderBy(i=>i),
            request.Arms.Where(a=>a.Index!=1).SelectMany(a=>a.Endpoints).Select(p=>p.NodeIndex).OrderBy(i=>i));
        Assert.Equal(order.Select(i=>original[i].Endpoints[0].NodeIndex),selected.Select(a=>a.Endpoints[0].NodeIndex));
    }

    [Theory]
    [InlineData(InterchangeType.Trumpet)]
    [InlineData(InterchangeType.DirectionalT)]
    [InlineData(InterchangeType.SemiDirectionalT)]
    [InlineData(InterchangeType.Wye)]
    public void AmbiguousYSelectionKeepsExplicitGroups(InterchangeType type)
    {
        var original=ThreeWayFixtures.Arms().Select(a=>new EndpointArm(a.Index,a.Endpoints.Select(p=>
            new SelectedEndpoint(p.NodeIndex,p.NodeVersion,Directions.FromAngle(a.Index*120)*100,
                Directions.FromAngle(a.Index*120),true,p.TravelOutward,20,3,"Main20")))).ToArray();
        var request=EndpointTypeRequest.Create(type,original,true,.16f,new[]{10f,20f,30f});
        Assert.True(request.InputReady);
        foreach(var a in request.Arms)Assert.Equal(original[a.Index].Endpoints,a.Endpoints);
        Assert.Equal(new[]{10f,20f,30f},request.RetreatLimits);
    }

    [Fact]
    public void InvalidPortDirectionIsRejectedWithoutReordering()
    {
        var arms=Captured("straight").Arms;
        var p=arms[0].Endpoints[1];
        arms[0]=new EndpointArm(0,new[]{arms[0].Endpoints[0],new SelectedEndpoint(p.NodeIndex,p.NodeVersion,
            p.Position,p.OutwardTangent,true,false,p.Width,p.Lanes,p.Prefab)});
        Assert.False(NodeFeedback.ReadyForAutoPreview(InterchangeType.Trumpet,arms));
        var request=EndpointTypeRequest.Create(InterchangeType.Trumpet,arms,true,.16f,new[]{10f,20f,30f});
        Assert.False(request.InputReady);
        Assert.Contains(request.Problems,p=>p.Code==EndpointProblemCode.InvalidDirection&&p.ArmIndex==0);
        Assert.Equal(arms[0].Endpoints,request.Arms[0].Endpoints);
    }

    [Fact]
    public void PermutedInputListUsesArmIndexToMapRetreatLimits()
    {
        var arms=Captured("straight").Arms;
        var request=EndpointTypeRequest.Create(InterchangeType.Trumpet,new[]{arms[2],arms[0],arms[1]},
            true,.16f,new[]{10f,20f,30f});
        Assert.True(request.InputReady);
        Assert.Equal(arms[1].Endpoints,request.Arms[0].Endpoints);
        Assert.Equal(arms[0].Endpoints,request.Arms[1].Endpoints);
        Assert.Equal(new[]{20f,10f,30f},request.RetreatLimits);
    }

    internal static (EndpointArm[] Arms, RoadApproachTrace[] Traces, float Terrain) Captured(string scene)
    {
        using var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream(
            "InterchangeMod.Generation.Tests.Fixtures.three-way-flat-failure-20261031.json")!;
        using var doc=JsonDocument.Parse(stream);
        var data=doc.RootElement.EnumerateArray().Single(s=>s.GetProperty("name").GetString()==scene);
        Vec3 V(JsonElement e)=>new(e.GetProperty("x").GetSingle(),e.GetProperty("y").GetSingle(),e.GetProperty("z").GetSingle());
        var traces=data.GetProperty("traces").EnumerateArray().Select(e=>
        {
            var o=e.GetProperty("origin");
            var port=new SelectedEndpoint(o.GetProperty("nodeIndex").GetInt32(),o.GetProperty("nodeVersion").GetInt32(),
                V(o.GetProperty("position")),V(o.GetProperty("outwardTangent")),o.GetProperty("oneWay").GetBoolean(),
                o.GetProperty("travelOutward").GetBoolean(),o.GetProperty("width").GetSingle(),o.GetProperty("lanes").GetInt32(),o.GetProperty("prefab").GetString()!);
            return new RoadApproachTrace(port,e.GetProperty("pieces").EnumerateArray().Select(p=>
            {
                var c=p.GetProperty("curve");
                return new RoadTracePiece(p.GetProperty("edgeIndex").GetInt32(),p.GetProperty("edgeVersion").GetInt32(),
                    new Bezier3(V(c.GetProperty("p0")),V(c.GetProperty("p1")),V(c.GetProperty("p2")),V(c.GetProperty("p3"))),
                    p.GetProperty("reversed").GetBoolean(),p.GetProperty("outerNodeIndex").GetInt32(),p.GetProperty("outerNodeVersion").GetInt32());
            }));
        }).ToArray();
        return (Enumerable.Range(0,3).Select(i=>new EndpointArm(i,traces.Skip(i*2).Take(2).Select(t=>t.Origin))).ToArray(),
            traces,data.GetProperty("terrain").GetSingle());
    }

    [Theory]
    [InlineData("straight")]
    [InlineData("curved")]
    public void CapturedBranchFirstSelectionIdentifiesOpposedMainRoad(string scene)
    {
        var data=Captured(scene);
        var request=EndpointTypeRequest.Create(InterchangeType.Trumpet,data.Arms,true,.16f,new[]{100f,200f,300f});
        Assert.True(request.InputReady);
        Assert.Equal(data.Arms[1].Endpoints,request.Arms[0].Endpoints);
        Assert.Equal(data.Arms[0].Endpoints,request.Arms[1].Endpoints);
        Assert.Equal(data.Arms[2].Endpoints,request.Arms[2].Endpoints);
        Assert.Equal(new[]{200f,100f,300f},request.RetreatLimits);
        // Original input remains a snapshot of the user's selection order.
        Assert.Equal(data.Traces[0].Origin.NodeIndex,data.Arms[0].Endpoints[0].NodeIndex);
    }

    [Theory]
    [InlineData("straight",InterchangeType.Trumpet)]
    [InlineData("straight",InterchangeType.DirectionalT)]
    [InlineData("straight",InterchangeType.SemiDirectionalT)]
    [InlineData("curved",InterchangeType.Trumpet)]
    [InlineData("curved",InterchangeType.DirectionalT)]
    [InlineData("curved",InterchangeType.SemiDirectionalT)]
    public void CapturedBranchFirstRoadsProduceCertifiedPreview(string scene,InterchangeType type)
    {
        var data=Captured(scene);
        var request=EndpointTypeRequest.Create(type,data.Arms,true,.16f,new[]{1000f,1000f,1000f});
        Assert.True(request.InputReady);
        var levels=EndpointLevels.Resolve(request.Arms,true,0,0,data.Terrain);
        var full=RealCurvedApproachTests.Design();
        var design=new CloverleafRoadDesign(full.Balances.Where(b=>b.Arm<3),bridgeRules:full.BridgeRules.Where(b=>b.Arm<3),supportRules:full.SupportRules);
        var result=EndpointThreeWaySearch.Search(request.Arms,data.Traces,type,true,levels.Main,levels.Cross,
            RealCurvedApproachTests.Ramp(),.16f,1000,design,terrainHeight:data.Terrain);
        Assert.True(result.Failure==RetreatSearchFailure.None,$"{result.Failure}, core={result.CoreMatched}, attempts={result.Attempts}: {result.ClearanceDetail}; {result.SupportDetail}");
        Assert.Equal(6,result.Cuts.Count);
        Assert.Equal(CurveClearanceResult.Clear,EndpointNetworkClearance.Check(result.Spatial!,8.5f,out _));
        Assert.Equal(CurveClearanceResult.Clear,EndpointNetworkSupports.Check(result.Spatial!,design.BridgeRules,
            RealCurvedApproachTests.Ramp(),out _,design.SupportRules));
        Assert.All(result.Spatial!.Segments,s=>Assert.Equal(VerticalProfileFailure.None,VerticalProfile.CheckGrade(s.WorldCurve,.16f)));
    }
}
