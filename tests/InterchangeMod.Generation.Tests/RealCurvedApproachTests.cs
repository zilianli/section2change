using System.Reflection;
using System.Text.Json;
using InterchangeMod.Generation.Endpoints;
using InterchangeMod.Generation.Geometry;
using InterchangeMod.Generation.Roads;
using Xunit.Abstractions;

namespace InterchangeMod.Generation.Tests;

public class RealCurvedApproachTests
{
    private readonly ITestOutputHelper output;
    public RealCurvedApproachTests(ITestOutputHelper output){this.output=output;}
    internal static RoadApproachTrace[] Traces()
    {
        using var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream(
            "InterchangeMod.Generation.Tests.Fixtures.curved-no-preview-20261031.json")!;
        using var doc=JsonDocument.Parse(stream);
        Vec3 V(JsonElement e)=>new(e.GetProperty("x").GetSingle(),e.GetProperty("y").GetSingle(),e.GetProperty("z").GetSingle());
        return doc.RootElement.EnumerateArray().Select(e=>
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
    }
    internal static EndpointArm[] Arms(RoadApproachTrace[] traces)=>Enumerable.Range(0,4)
        .Select(i=>new EndpointArm(i,traces.Skip(i*2).Take(2).Select(t=>t.Origin))).ToArray();
    internal static RoadInfo Ramp()=>new("Highway Oneway - 1 lane",true,1,0,8){ElevatedLength=80,PillarWidth=4};
    internal static CloverleafRoadDesign Design()=>new(Enumerable.Range(0,4).Select(i=>
        new EndpointLaneBalance(i,"Highway Oneway - 3 lanes",16,3,1)),
        bridgeRules:Enumerable.Range(0,4).Select(i=>new EndpointBridgeRule(i,80,null,20,4,
            new HeightRange(-.4f,5),new HeightRange(-2,5))),
        supportRules:new[]{new EndpointPrefabSupportRule("Highway Oneway - 4 lanes",80,null,4),
            new EndpointPrefabSupportRule("Highway Oneway - 3 lanes",80,null,4),
            new EndpointPrefabSupportRule("Highway Oneway - 1 lane",80,null,4)});

    // Actual control points, directions and entity snapshots captured from the failing
    // game scene. Bridge spacing, widths and height ranges follow the catalog log;
    // the unlogged pillar envelope uses 4m, not a claim of full game-prefab replay.
    [Theory]
    [InlineData(InterchangeType.Stack,.16f)]
    [InlineData(InterchangeType.Stack,.18f)]
    [InlineData(InterchangeType.Turbine,.16f)]
    [InlineData(InterchangeType.Turbine,.18f)]
    public void GeneratesForCapturedCurvedRoads(InterchangeType type,float grade)
    {
        var traces=Traces();var arms=Arms(traces);var design=Design();var ramp=Ramp();
        var result=EndpointStackSearch.Search(arms,traces,type,true,488.6323f,498.6323f,ramp,grade,500,design);
        Assert.True(result.Failure==RetreatSearchFailure.None,
            result.Failure==RetreatSearchFailure.None?null:
                $"{result.Failure}; core={result.CoreMatched}; attempts={result.Attempts}; {result.ClearanceDetail}\n{Angles(arms,traces,type,design,ramp,grade)}");
        output.WriteLine($"{type} {grade:P0}: attempts={result.Attempts}, retreat=[{string.Join(",",result.Distances)}], segments={result.Spatial!.Segments.Count}");
        Assert.All(result.Distances,d=>Assert.InRange(d,0,500));
        Assert.Equal(CurveClearanceResult.Clear,EndpointNetworkClearance.Check(result.Spatial!,8.5f,out _));
        Assert.Equal(CurveClearanceResult.Clear,EndpointNetworkSupports.Check(result.Spatial!,design.BridgeRules,ramp,out _,design.SupportRules));
        Assert.All(result.Spatial!.Segments,s=>Assert.Equal(VerticalProfileFailure.None,VerticalProfile.CheckGrade(s.WorldCurve,grade)));
        foreach(var s in result.Spatial.Segments.Where(s=>s.Kind==CloverEdgeKind.Main))
        {
            var c=s.WorldCurve;var delta=c.P3-c.P0;var axis=Vec3.Normalize(new Vec3(delta.X,0,delta.Z));
            for(int i=0;i<=64;i++)
            {
                var v=c.Derivative(i/64f);
                Assert.True(Directions.Dot(Vec3.Normalize(new Vec3(v.X,0,v.Z)),axis)>=Math.Cos(20*Math.PI/180)-.00001);
            }
        }
        foreach(var cut in result.Cuts)
        {
            var p=cut.Port!;
            var curve=Assert.Single(result.Spatial.Segments.Where(s=>p.TravelOutward?s.WorldCurve.P3==p.Position:s.WorldCurve.P0==p.Position)).WorldCurve;
            var tangent=p.TravelOutward?curve.Derivative(1):-curve.Derivative(0);
            Assert.True(Directions.Dot(Vec3.Normalize(tangent),Vec3.Normalize(p.OutwardTangent))>.99999f);
        }
    }

    private static string Angles(EndpointArm[] arms,RoadApproachTrace[] traces,InterchangeType type,CloverleafRoadDesign design,RoadInfo ramp,float grade)
    {
        double Angle(Vec3 a,Vec3 b)=>Math.Acos(Math.Clamp(((double)a.X*b.X+(double)a.Z*b.Z)/
            Math.Sqrt(((double)a.X*a.X+(double)a.Z*a.Z)*((double)b.X*b.X+(double)b.Z*b.Z)),-1,1))*180/Math.PI;
        return string.Join("\n",EndpointStackCore.Candidates(arms,type,true,488.6323f,498.6323f,ramp,grade,design)
            .Where(c=>c.Success).Select((c,ci)=>$"core{ci}: "+string.Join("; ",arms.Select(a=>
            {
                double best=180;int at=0;
                for(int d=0;d<=500;d+=25)
                {
                    double worst=0;
                    foreach(var p in a.Endpoints)
                    {
                        var cut=RoadRetreat.Cut(traces.Single(t=>t.Origin.NodeIndex==p.NodeIndex),d);
                        if(cut.Port==null){worst=180;break;}
                        var cp=c.Ports.Single(x=>x.Arm==a.Index&&x.TravelOutward==p.TravelOutward);
                        var chord=p.TravelOutward?cut.Port.Position-cp.Position:cp.Position-cut.Port.Position;
                        worst=Math.Max(worst,Math.Max(Angle(chord,cp.Tangent),Angle(chord,p.TravelOutward?cut.Port.OutwardTangent:-cut.Port.OutwardTangent)));
                    }
                    if(worst<best){best=worst;at=d;}
                }
                return $"arm{a.Index+1} min={best:F1}deg at{at}m";
            }))));
    }
}
