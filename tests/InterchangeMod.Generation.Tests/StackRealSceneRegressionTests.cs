using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using InterchangeMod.Generation.Endpoints;
using InterchangeMod.Generation.Geometry;
using InterchangeMod.Generation.Roads;
using Xunit.Abstractions;
namespace InterchangeMod.Generation.Tests;

public class StackRealSceneRegressionTests(ITestOutputHelper output)
{
    internal static RoadApproachTrace[] Captured()
    {
        using var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("InterchangeMod.Generation.Tests.Fixtures.stack-mixed-curved-20261103.json")!;
        using var doc=JsonDocument.Parse(stream);
        Vec3 V(JsonElement v)=>new(v.GetProperty("x").GetSingle(),v.GetProperty("y").GetSingle(),v.GetProperty("z").GetSingle());
        return doc.RootElement.EnumerateArray().Select(e=> {
            var o=e.GetProperty("origin");var p=new SelectedEndpoint(o.GetProperty("nodeIndex").GetInt32(),o.GetProperty("nodeVersion").GetInt32(),
                V(o.GetProperty("position")),V(o.GetProperty("outwardTangent")),o.GetProperty("oneWay").GetBoolean(),o.GetProperty("travelOutward").GetBoolean(),
                o.GetProperty("width").GetSingle(),o.GetProperty("lanes").GetInt32(),o.GetProperty("prefab").GetString()!);
            return new RoadApproachTrace(p,e.GetProperty("pieces").EnumerateArray().Select(x=> {
                var c=x.GetProperty("curve");return new RoadTracePiece(x.GetProperty("edgeIndex").GetInt32(),x.GetProperty("edgeVersion").GetInt32(),
                    new Bezier3(V(c.GetProperty("p0")),V(c.GetProperty("p1")),V(c.GetProperty("p2")),V(c.GetProperty("p3"))),x.GetProperty("reversed").GetBoolean(),
                    x.GetProperty("outerNodeIndex").GetInt32(),x.GetProperty("outerNodeVersion").GetInt32());
            }));
        }).ToArray();
    }
    internal static RoadInfo[] Catalog()=>new[]{
        R("XL Road Divided",false,4,4,40),R("Medium Road Oneway",false,4,0,24),R("Large Road Divided",false,3,3,32),
        R("Small Road Oneway - 3 lanes",false,3,0,16),R("Small Road Oneway",false,2,0,16),R("Small Road Oneway - 1 lane",false,1,0,16),
        R("Highway Oneway - 4 lanes",true,4,0,20),R("Highway Oneway - 3 lanes",true,3,0,16),R("Highway Oneway - 2 lanes",true,2,0,12),R("Highway Oneway - 1 lane",true,1,0,8)};
    private static RoadInfo R(string n,bool h,int f,int b,float w)=>new(n,h,f,b,w,groundHeight:new HeightRange(h?-.4f:-1.8f,5),
        elevatedHeight:new HeightRange(-2,5),maxSlope:.20f){ElevatedLength=80,PillarWidth=4};

    internal static (EndpointArm[] Arms,RoadApproachTrace[] Traces) Scene(bool allNative)
    {
        int[][] groups=allNative?new[]{new[]{51143},new[]{64939},new[]{51142},new[]{64947}}
            :new[]{new[]{246857,246852},new[]{64912},new[]{245986,246012},new[]{64902}};
        var traces=Captured().Where(t=>groups.Any(g=>g.Contains(t.Origin.NodeIndex))).ToArray();
        var arms=groups.Select((g,i)=>new EndpointArm(i,g.Select(id=>traces.Single(t=>t.Origin.NodeIndex==id).Origin))).ToArray();
        return(arms,traces);
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void ActualGameStackInputsHaveCertifiedPreview(bool allNative)
    {
        var f=Scene(allNative);var roads=Catalog();
        var setup=EndpointMixedRoadSetup.Create(f.Arms,roads,InterchangeType.Stack,true,0,0,478.6323f,
            allNative?"Small Road Oneway - 1 lane":"Highway Oneway - 1 lane");
        Assert.True(setup.Success,setup.Diagnostic);
        var before=EndpointTraceDump.Encode(f.Traces);var watch=Stopwatch.StartNew();
        var result=EndpointMixedRoadSearch.SearchCore(f.Arms,f.Traces,setup,.16f,1000,diagnostic:output.WriteLine);
        output.WriteLine($"elapsed {watch.Elapsed.TotalSeconds:F2}s, attempts {result.Attempts}, {result.Failure}: {result.ClearanceDetail}");
        Export(allNative,result);
        Assert.True(result.Failure==RetreatSearchFailure.None,result.ClearanceDetail);
        if(allNative)
            foreach(var path in result.Spatial!.Segments.Where(s=>s.PathId.StartsWith("Right")).GroupBy(s=>s.PathId))
                Assert.True(CurvatureJump(path)<.002, $"{path.Key} still has an abrupt curvature reversal ({CurvatureJump(path):F5}).");
        Assert.Equal(before,EndpointTraceDump.Encode(f.Traces));
        RemainingMixedRoadIntegrationTests.Certify(result,f.Arms,f.Traces,roads,setup,.16f);
    }
    private static double CurvatureJump(IEnumerable<SpatialSegment> path)=>path.Zip(path.Skip(1),(a,b)=>
        Math.Abs(CurveCurvature.Signed(a.WorldCurve,1)-CurveCurvature.Signed(b.WorldCurve,0))).DefaultIfEmpty(0).Max();

    private static void Export(bool allNative,RetreatSearchResult result)
    {
        string? folder=Environment.GetEnvironmentVariable("STACK_REAL_EXPORT");if(folder==null)return;
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder,$"stack-{allNative}.json"),JsonSerializer.Serialize(new{
            Failure=result.Failure.ToString(),result.Attempts,Detail=result.ClearanceDetail,result.Distances,
            Segments=result.Spatial?.Segments.Select(s=>new{s.PathId,s.SourceArm,Kind=s.Kind.ToString(),s.Prefab,s.RoadWidth,
                Curve=new{s.WorldCurve.P0,s.WorldCurve.P1,s.WorldCurve.P2,s.WorldCurve.P3},s.StartNode,s.EndNode})
        },new JsonSerializerOptions{IncludeFields=true}));
    }

    [Fact]
    public void CapturedShortTailCanUseAnEarlierCertifiedNativeWindow()
    {
        var trace=Captured().Single(t=>t.Origin.NodeIndex==64902);
        Assert.False(EndpointApproachGeometry.HasRetainedRoom(EndpointMixedRoadSearch.CutNativeBeyond(trace,650,1000)));
        var cut=EndpointMixedRoadSearch.CutNativeWindow(trace,650,604,1000);
        Assert.True(EndpointApproachGeometry.HasRetainedRoom(cut));Assert.InRange(cut.Distance,604,649.99f);
        Assert.Equal(trace.Origin.Prefab,cut.Port!.Prefab);
    }

    [Fact]
    public void CancelledStackSearchDoesNotStartOrReturnAStaleResult()
    {
        var f=Scene(false);var setup=EndpointMixedRoadSetup.Create(f.Arms,Catalog(),InterchangeType.Stack,true,0,0,478.6323f,"Highway Oneway - 1 lane");
        using var cancelled=new CancellationTokenSource();cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(()=>EndpointMixedRoadSearch.SearchCancellable(f.Arms,f.Traces,setup,.16f,1000,cancelled.Token));
    }

    [Fact]
    public void CancellationAfterACertifiedCoreStopsBeforeNativeConversion()
    {
        var f=Scene(false);var setup=EndpointMixedRoadSetup.Create(f.Arms,Catalog(),InterchangeType.Stack,true,0,0,478.6323f,"Highway Oneway - 1 lane");
        using var cancelled=new CancellationTokenSource();bool reachedCore=false;
        Assert.Throws<OperationCanceledException>(()=>EndpointMixedRoadSearch.SearchCore(f.Arms,f.Traces,setup,.16f,1000,
            diagnostic:message=>{if(message.Contains("core None/")){reachedCore=true;cancelled.Cancel();}},cancellation:cancelled.Token));
        Assert.True(reachedCore);
    }

    [Fact]
    public void LoggedMixedStraightPosesWithConstructedContinuationsHaveCertifiedPreview()
    {
        // The successful game's original source Beziers were not logged. These
        // are the captured poses with explicitly constructed straight traces.
        SelectedEndpoint P(int id,float x,float y,float z,float tx,float tz,bool one,bool outward)=>
            new(id,1,new Vec3(x,y,z),new Vec3(tx,0,tz),one,outward,one?20:40,4,one?"Highway Oneway - 4 lanes":"XL Road Divided");
        var arms=new[]{
            new EndpointArm(0,new[]{P(246656,-1621.08f,488.63f,-4739.52f,-25.03f,7.19f,true,false),P(246660,-1613.35f,488.63f,-4712.6f,-25.03f,7.19f,true,true)}),
            new EndpointArm(1,new[]{P(51136,-1511,478.63f,-4646.39f,19.59f,63.72f,false,false)}),
            new EndpointArm(2,new[]{P(246652,-1463.19f,488.63f,-4755.74f,25.03f,-7.19f,true,false),P(246664,-1470.92f,488.63f,-4782.65f,25.03f,-7.19f,true,true)}),
            new EndpointArm(3,new[]{P(51133,-1569.79f,478.63f,-4837.56f,-19.59f,-63.72f,false,true)})};
        var traces=arms.SelectMany(a=>a.Endpoints).Select(p=>{
            var delta=Vec3.Normalize(p.OutwardTangent)*900;
            return new RoadApproachTrace(p,new[]{new RoadTracePiece(500000+p.NodeIndex,1,
                new Bezier3(p.Position,p.Position+delta*(1f/3),p.Position+delta*(2f/3),p.Position+delta),false,600000+p.NodeIndex,1)});
        }).ToArray();
        var setup=EndpointMixedRoadSetup.Create(arms,Catalog(),InterchangeType.Stack,true,0,0,478.63f,"Highway Oneway - 1 lane");
        Assert.True(setup.Success,setup.Diagnostic);
        var result=EndpointMixedRoadSearch.Search(arms,traces,setup,.16f,1000);
        Assert.True(result.Failure==RetreatSearchFailure.None,result.ClearanceDetail);
        RemainingMixedRoadIntegrationTests.Certify(result,arms,traces,Catalog(),setup,.16f);
    }
}
