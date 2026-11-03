using System.Reflection;
using System.Text.Json;
using InterchangeMod.Generation.Endpoints;
using InterchangeMod.Generation.Geometry;
using InterchangeMod.Generation.Roads;
using Xunit.Abstractions;

namespace InterchangeMod.Generation.Tests;

public class GridArmOrderTests
{
    private readonly ITestOutputHelper output;
    public GridArmOrderTests(ITestOutputHelper output){this.output=output;}
    internal static (EndpointArm[] Arms,RoadApproachTrace[] Traces,float Terrain) Captured()
    {
        using var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream(
            "InterchangeMod.Generation.Tests.Fixtures.grid-order-failure-20261101.json")!;
        using var doc=JsonDocument.Parse(stream);var data=doc.RootElement;
        Vec3 V(JsonElement e)=>new(e.GetProperty("x").GetSingle(),e.GetProperty("y").GetSingle(),e.GetProperty("z").GetSingle());
        var traces=data.GetProperty("traces").EnumerateArray().Select(e=>
        {
            var o=e.GetProperty("origin");
            var port=new SelectedEndpoint(o.GetProperty("nodeIndex").GetInt32(),o.GetProperty("nodeVersion").GetInt32(),
                V(o.GetProperty("position")),V(o.GetProperty("outwardTangent")),o.GetProperty("oneWay").GetBoolean(),
                o.GetProperty("travelOutward").GetBoolean(),o.GetProperty("width").GetSingle(),o.GetProperty("lanes").GetInt32(),o.GetProperty("prefab").GetString()!);
            return new RoadApproachTrace(port,e.GetProperty("pieces").EnumerateArray().Select(p=>
            {
                var c=p.GetProperty("curve");return new RoadTracePiece(p.GetProperty("edgeIndex").GetInt32(),p.GetProperty("edgeVersion").GetInt32(),
                    new Bezier3(V(c.GetProperty("p0")),V(c.GetProperty("p1")),V(c.GetProperty("p2")),V(c.GetProperty("p3"))),
                    p.GetProperty("reversed").GetBoolean(),p.GetProperty("outerNodeIndex").GetInt32(),p.GetProperty("outerNodeVersion").GetInt32());
            }));
        }).ToArray();
        return (Enumerable.Range(0,4).Select(i=>new EndpointArm(i,traces.Skip(i*2).Take(2).Select(t=>t.Origin))).ToArray(),
            traces,data.GetProperty("terrain").GetSingle());
    }
    [Fact]
    public void CapturedSouthWestEastNorthSelectionBecomesClockwiseWithoutLosingLimits()
    {
        var data=Captured();var input=data.Arms;var request=EndpointTypeRequest.Create(InterchangeType.CompactGrid,input,true,.16f,new[]{100f,200f,300f,400f});
        Assert.True(request.InputReady);
        Assert.Equal(new[]{input[0].Endpoints[0].NodeIndex,input[1].Endpoints[0].NodeIndex,input[3].Endpoints[0].NodeIndex,input[2].Endpoints[0].NodeIndex},
            request.Arms.Select(a=>a.Endpoints[0].NodeIndex));
        Assert.Equal(new[]{100f,200f,400f,300f},request.RetreatLimits);
        Assert.Equal(data.Traces[4].Origin.NodeIndex,input[2].Endpoints[0].NodeIndex);
    }
    [Fact]
    public void CapturedMixedHeightRoadsCanGenerateCertifiedPreview()
    {
        var data=Captured();var request=EndpointTypeRequest.Create(InterchangeType.CompactGrid,data.Arms,true,.16f,new[]{1000f,1000f,1000f,1000f});
        var catalog=Enumerable.Range(1,5).Select(n=>new RoadInfo($"Highway Oneway - {n} {(n==1?"lane":"lanes")}",true,n,0,4+4*n,
            groundHeight:new HeightRange(-.4f,5),elevatedHeight:new HeightRange(-2,5)){ElevatedLength=80,PillarWidth=4}).ToArray();
        var setup=EndpointCompactGridSetup.Create(request.Arms,catalog,0,0,data.Terrain);
        Assert.True(setup.Success,setup.Diagnostic);
        var result=EndpointCompactGridSearch.Search(request.Arms,data.Traces,true,setup.BaseHeight,setup.LevelGap,setup.Ramp!,setup.Roads,
            .16f,1000,setup.Design!,terrainHeight:data.Terrain);
        Assert.True(result.Failure==RetreatSearchFailure.None,$"{result.Failure}, attempts={result.Attempts}, core={result.CoreMatched}: {result.ClearanceDetail}; {result.SupportDetail}");
        Assert.Equal(8,result.Cuts.Count);Assert.Equal(CurveClearanceResult.Clear,EndpointNetworkClearance.Check(result.Spatial!,setup.RequiredClearance,out _));
        Assert.Equal(CurveClearanceResult.Clear,EndpointNetworkSupports.Check(result.Spatial!,setup.Design!.BridgeRules,setup.Ramp!,out _,setup.Design.SupportRules));
        Assert.All(result.Spatial!.Segments,s=>Assert.Equal(VerticalProfileFailure.None,VerticalProfile.CheckGrade(s.WorldCurve,.16f)));
        output.WriteLine($"Captured grid: retreat=[{string.Join(",",result.Distances)}], attempts={result.Attempts}, segments={result.Spatial!.Segments.Count}");
    }
    public static IEnumerable<object[]> Orders()
    {
        foreach(bool rht in new[]{true,false})foreach(int a in Enumerable.Range(0,4))foreach(int b in Enumerable.Range(0,4).Where(i=>i!=a))
        foreach(int c in Enumerable.Range(0,4).Where(i=>i!=a&&i!=b))yield return new object[]{rht,new[]{a,b,c,6-a-b-c}};
    }
    [Theory,MemberData(nameof(Orders))]
    public void AllSelectionOrdersKeepFirstDirectionAndRecognizeOpposedFamilies(bool rht,int[] order)
    {
        var narrow=CompactGridFixtures.Arms(rht);var wide=CompactGridFixtures.Arms(rht,lanes:6);
        var original=narrow.Select(a=>a.Index%2==0?a:wide[a.Index]).ToArray();
        var selected=order.Select((old,index)=>new EndpointArm(index,original[old].Endpoints)).ToArray();var limits=new[]{10f,20f,30f,40f};
        Assert.True(NodeFeedback.ReadyForAutoPreview(InterchangeType.CompactGrid,selected));
        var request=EndpointTypeRequest.Create(InterchangeType.CompactGrid,selected,rht,.16f,limits);
        Assert.True(request.InputReady);
        for(int i=0;i<4;i++)
        {
            int physical=(order[0]+i)%4;Assert.Equal(original[physical].Endpoints,request.Arms[i].Endpoints);
            Assert.Equal(limits[Array.IndexOf(order,physical)],request.RetreatLimits[i]);
        }
        Assert.Equal(order.Select(i=>original[i].Endpoints[0].NodeIndex),selected.Select(a=>a.Endpoints[0].NodeIndex));
    }
    [Theory]
    [InlineData(InterchangeType.Cloverleaf)]
    [InlineData(InterchangeType.Stack)]
    [InlineData(InterchangeType.Turbine)]
    public void ExistingFourWayTypesRetainTheirInputConvention(InterchangeType type)
    {
        var arms=Captured().Arms;var request=EndpointTypeRequest.Create(type,arms,true,.16f,new float[4]);
        Assert.True(request.InputReady);foreach(var a in request.Arms)Assert.Equal(arms[a.Index].Endpoints,a.Endpoints);
    }
    [Fact]
    public void DuplicateSelectionRemainsInvalid()
    {
        var arms=Captured().Arms;arms[3]=new EndpointArm(3,arms[0].Endpoints);
        Assert.False(NodeFeedback.ReadyForAutoPreview(InterchangeType.CompactGrid,arms));
        var request=EndpointTypeRequest.Create(InterchangeType.CompactGrid,arms,true,.16f,new float[4]);
        Assert.False(request.InputReady);Assert.Contains(request.Problems,p=>p.Code==EndpointProblemCode.DuplicateNode);
    }
}
