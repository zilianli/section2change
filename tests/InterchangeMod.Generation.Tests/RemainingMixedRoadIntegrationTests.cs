using InterchangeMod.Generation.Endpoints;
using InterchangeMod.Generation.Geometry;
using InterchangeMod.Generation.Roads;
using Xunit.Abstractions;
using static InterchangeMod.Generation.Tests.NativeRoundaboutSmoothingTests;

namespace InterchangeMod.Generation.Tests;

public class RemainingMixedRoadIntegrationTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Variants() => new[] {
        (InterchangeType.Cloverleaf, "default"), (InterchangeType.Cloverleaf, "hug"),
        (InterchangeType.Stack, "default"), (InterchangeType.Stack, "left"),
        (InterchangeType.Turbine, "default"), (InterchangeType.Trumpet, "default"),
        (InterchangeType.Trumpet, "loop-in"), (InterchangeType.DirectionalT, "default"),
        (InterchangeType.DirectionalT, "two-level"), (InterchangeType.SemiDirectionalT, "default"), (InterchangeType.SemiDirectionalT, "two-level"),
        (InterchangeType.Cloverleaf, "cross-top"), (InterchangeType.Stack, "cross-top"), (InterchangeType.Turbine, "cross-top"),
        (InterchangeType.Wye, "default"), (InterchangeType.CompactGrid, "default") }
        .SelectMany(c => new[] { true, false }.Select(rht => new object[] { c.Item1, c.Item2, rht }));

    internal static RoadInfo Road(string name, bool highway, int forward, int backward, float width) => new(
        name, highway, forward, backward, width, groundHeight: new HeightRange(highway ? -.4f : -1.8f, 5),
        elevatedHeight: new HeightRange(-2, 5), maxSlope: .20f) { ElevatedLength = 80, PillarWidth = 4 };

    // Short selected gaps, six/eight real directions adapted to each family.
    // Based on the logged six-port road dimensions/poses; the 75m continuations,
    // Y pose and native-highway metadata are controlled test data, not game replay.
    internal static (EndpointArm[] Arms, RoadApproachTrace[] Traces, RoadInfo[] Catalog) Scene(
        InterchangeType type, bool rht, bool allNative = false, float sourceGrade = 0)
    {
        var logged = Fixture(!rht, sourceGrade);
        var roads = logged.Catalog.Concat(new[] {
            Road("Highway Oneway - 2 lanes",true,2,0,12), Road("Highway Twoway - 2 lanes",true,1,1,12),
            Road("Native highway four each",true,4,4,40), Road("XL Road Divided",false,4,4,40),
            Road("Medium Road Oneway",false,4,0,24) }).ToArray();
        EndpointArm[] arms;
        if (type == InterchangeType.Wye)
            arms = Enumerable.Range(0,3).Select(i => {
                var u = Directions.FromAngle(120 * i); var center = new Vec3(900,488.6323f,-2500) + u * 120;
                bool native = allNative || i != 0;
                Vec3 M(Vec3 p) => rht ? p : new Vec3(-p.X,p.Y,p.Z);
                if (native) return new EndpointArm(i,new[] { new SelectedEndpoint(700+i,1,M(center),M(u),false,i==2,
                    40,4,i==1 ? "XL Road Divided" : "Native highway four each") });
                var side = Directions.RightOf(u) * 14;
                return new EndpointArm(i,new[] {false,true}.Select(outward => new SelectedEndpoint(710+i*2+(outward?1:0),1,
                    M(center+side*(outward?1:-1)),M(u),true,outward,20,4,"Highway Oneway - 4 lanes")));
            }).ToArray();
        else {
            int count = type is InterchangeType.Trumpet or InterchangeType.DirectionalT or InterchangeType.SemiDirectionalT ? 3 : 4;
            arms = logged.Arms.Take(count).Select(a => allNative && a.Endpoints.Count == 2
                ? new EndpointArm(a.Index,new[] {new SelectedEndpoint(a.Endpoints[0].NodeIndex,a.Endpoints[0].NodeVersion,
                    (a.Endpoints[0].Position+a.Endpoints[1].Position)*.5f,a.Endpoints[0].OutwardTangent,false,false,40,4,
                    "Native highway four each")}) : a).ToArray();
        }
        // The cloverleaf topology contract is clockwise in both traffic hands;
        // reflecting positions alone also reverses those group labels.
        if(!rht&&arms.Length==4)arms=arms.Select(a=>new EndpointArm(a.Index==1?3:a.Index==3?1:a.Index,a.Endpoints)).OrderBy(a=>a.Index).ToArray();
        var traces = arms.SelectMany(a => a.Endpoints).Select(p => {
            var u = Vec3.Normalize(new Vec3(p.OutwardTangent.X,0,p.OutwardTangent.Z));
            var side = Directions.RightOf(u); float bend = p.OneWay ? 30 : -100;
            if (!rht) bend = -bend;
            Vec3 G(Vec3 v) => new(v.X,v.Y+sourceGrade*Directions.Dot(v-p.Position,u),v.Z);
            var curve = new Bezier3(p.Position,G(p.Position+u*400),G(p.Position+u*800+side*bend),
                G(p.Position+u*1200+side*(2*bend)));
            var origin = new SelectedEndpoint(p.NodeIndex,p.NodeVersion,p.Position,curve.Derivative(0),p.OneWay,
                p.TravelOutward,p.Width,p.Lanes,p.Prefab);
            return new RoadApproachTrace(origin,Enumerable.Range(0,16).Select(i => new RoadTracePiece(p.NodeIndex*20+i,1,
                curve.Slice(i/16f,(i+1)/16f),i%2==0,p.NodeIndex*20+100+i,1)));
        }).ToArray();
        arms = arms.Select(a => new EndpointArm(a.Index,a.Endpoints.Select(p => traces.Single(t => t.Origin.NodeIndex==p.NodeIndex).Origin))).ToArray();
        var request = EndpointTypeRequest.Create(type,arms,rht,.16f,Enumerable.Repeat(1000f,arms.Length).ToArray());
        Assert.True(request.InputReady,string.Join(",",request.Problems.Select(p=>p.Code)));
        return (request.Arms.ToArray(),traces,roads);
    }

    internal static EndpointMixedRoadSetup Setup(EndpointArm[] arms, RoadInfo[] roads, InterchangeType type,
        string variant, bool rht) => EndpointMixedRoadSetup.Create(arms,roads,type,rht,0,0,
            variant=="two-level" ? 488.6323f : 478.6323f,
            type==InterchangeType.Wye ? "Highway Oneway - 2 lanes" : "Highway Oneway - 1 lane",
            crossOnTop:variant is "two-level" or "cross-top",hugLoop:variant=="hug",leftExit:variant=="left",trumpetLoopForExit:variant!="loop-in",
            directionalTwoLevel:variant=="two-level");

    [Theory, MemberData(nameof(Variants))]
    public void ShortSegmentedStreetHighwayMixedInputsHaveCertifiedRealCuts(InterchangeType type,string variant,bool rht)
    {
        var f=Scene(type,rht); var setup=Setup(f.Arms,f.Catalog,type,variant,rht);
        Assert.True(setup.Success,setup.Diagnostic);
        var result=EndpointMixedRoadSearch.Search(f.Arms,f.Traces,setup,.16f,1000);
        output.WriteLine($"{type}/{variant}/{rht}: {result.Failure}, attempts {result.Attempts}; {result.ClearanceDetail}; {result.SupportDetail}");
        Assert.True(result.Failure==RetreatSearchFailure.None,result.ClearanceDetail+"; "+result.SupportDetail);
        Certify(result,f.Arms,f.Traces,f.Catalog,setup,.16f);
    }

    [Theory]
    [InlineData(InterchangeType.Cloverleaf)] [InlineData(InterchangeType.Stack)] [InlineData(InterchangeType.Turbine)]
    [InlineData(InterchangeType.Trumpet)] [InlineData(InterchangeType.DirectionalT)] [InlineData(InterchangeType.SemiDirectionalT)]
    [InlineData(InterchangeType.Wye)] [InlineData(InterchangeType.CompactGrid)]
    public void RemainingFamiliesImproveNativeCurvesWithoutMovingTheirCore(InterchangeType type)
    {
        var f=Scene(type,true); var setup=Setup(f.Arms,f.Catalog,type,"default",true);
        Assert.True(setup.Success,setup.Diagnostic);
        var result=EndpointMixedRoadSearch.Search(f.Arms,f.Traces,setup,.16f,1000);
        Assert.True(result.Failure==RetreatSearchFailure.None,$"{type}: {result.ClearanceDetail}; {result.SupportDetail}");
        var core=new SpatialCandidateResult(SpatialCandidateFailure.None,result.Spatial!.Segments.Where(s=>!Native(s)));
        var baseline=EndpointTwoWayTransition.ConnectBounded(core,result.Arms,ProxyCuts(result),true,.16f,f.Catalog,
            setup.Design!,setup.RequiredClearance,3,out _);
        Assert.Equal(SpatialCandidateFailure.None,baseline.Failure);
        var before=Measure(baseline,core,result.Cuts,f.Traces); var after=Measure(result.Spatial!,core,result.Cuts,f.Traces);
        output.WriteLine($"{type}: peak {before.Peak:F6}->{after.Peak:F6}, jump {before.Jump:F6}->{after.Jump:F6}");
        Assert.True(after.Peak<=before.Peak+1e-9 && after.Jump<before.Jump*.95,$"{type}: source curve quality did not improve.");
        Assert.All(core.Segments,s=>Assert.Contains(result.Spatial!.Segments,x=>ReferenceEquals(x,s)));
        Certify(result,f.Arms,f.Traces,f.Catalog,setup,.16f);
    }

    [Theory,MemberData(nameof(Variants))]
    public void AllNativeRoadsKeepRealStemsAndSourceGradesForEveryVariant(InterchangeType type,string variant,bool rht)
    {
        var f=Scene(type,rht,allNative:true,sourceGrade:.03f); var setup=Setup(f.Arms,f.Catalog,type,variant,rht);
        Assert.True(setup.Success,setup.Diagnostic);
        var result=EndpointMixedRoadSearch.Search(f.Arms,f.Traces,setup,.16f,1000);
        Assert.True(result.Failure==RetreatSearchFailure.None,$"{type}/{variant}/{rht}: {result.ClearanceDetail}; {result.SupportDetail}");
        Certify(result,f.Arms,f.Traces,f.Catalog,setup,.16f);
        Assert.Equal(f.Arms.Length,result.Cuts.Count);
    }

    private static readonly InterchangeType[] Families={InterchangeType.Cloverleaf,InterchangeType.Stack,InterchangeType.Turbine,
        InterchangeType.Trumpet,InterchangeType.DirectionalT,InterchangeType.SemiDirectionalT,InterchangeType.Wye,InterchangeType.CompactGrid};
    public static IEnumerable<object[]> Grades()=>Families.SelectMany(t=>new[]{.08f,.30f}.Select(g=>new object[]{t,g}));
    public static IEnumerable<object[]> Budgets()=>Families.SelectMany(t=>new[]{1,9}.Select(b=>new object[]{t,b}));

    [Theory,MemberData(nameof(Grades))]
    public void PracticalGradeSettingsStillRespectActualRoadMetadata(InterchangeType type,float grade)
    {
        var f=Scene(type,true,sourceGrade:.03f); var setup=Setup(f.Arms,f.Catalog,type,"default",true);
        Assert.True(setup.Success,setup.Diagnostic);
        var result=EndpointMixedRoadSearch.SearchCore(f.Arms,f.Traces,setup,grade,1000,diagnostic:output.WriteLine);
        Assert.True(result.Failure==RetreatSearchFailure.None,$"{type}/{grade}: {result.ClearanceDetail}; {result.SupportDetail}");
        Certify(result,f.Arms,f.Traces,f.Catalog,setup,grade);
    }

    [Theory,MemberData(nameof(Budgets))]
    public void EveryFamilyHonoursSmallSharedBudgets(InterchangeType type,int budget)
    {
        var f=Scene(type,true); var setup=Setup(f.Arms,f.Catalog,type,"default",true);
        var result=EndpointMixedRoadSearch.Search(f.Arms,f.Traces,setup,.16f,1000,budget);
        Assert.InRange(result.Attempts,0,budget);
        if(result.Failure==RetreatSearchFailure.None)Certify(result,f.Arms,f.Traces,f.Catalog,setup,.16f);
        else Assert.Null(result.Spatial);
    }

    [Fact]
    public void ExtendedCollectorKeepsC2CurvatureWithinEightyMetreSpans()
    {
        var start=Vec3.Zero;var end=new Vec3(150,0,5);
        var old=EndpointCompactGridCore.Shift(start,end,Vec3.UnitX).ToArray();
        var curves=EndpointCompactGridCore.Shift(start,end,Vec3.UnitX,.48f).ToArray();
        Assert.Equal(3,curves.Length); Assert.Equal(start,curves[0].P0);Assert.Equal(end,curves[^1].P3);
        Assert.True(curves[0].Length(1024)>old[0].Length(1024));
        Assert.All(curves,c=>Assert.InRange(c.Length(1024),2,80));
        for(int i=1;i<curves.Length;i++) {
            Assert.Equal(curves[i-1].P3,curves[i].P0);
            Assert.True(Vec3.Distance(Vec3.Normalize(curves[i-1].Derivative(1)),Vec3.Normalize(curves[i].Derivative(0)))<.00001f);
            Assert.InRange(Math.Abs(CurveCurvature.Signed(curves[i-1],1)-CurveCurvature.Signed(curves[i],0)),0,1e-5);
        }
    }

    [Fact]
    public void SlopedNativeStackSharesRiseWithinEightPercent()
    {
        var f=Scene(InterchangeType.Stack,true,sourceGrade:.03f);var setup=Setup(f.Arms,f.Catalog,InterchangeType.Stack,"default",true);
        var result=EndpointMixedRoadSearch.SearchCore(f.Arms,f.Traces,setup,.08f,1000,diagnostic:output.WriteLine);
        Assert.True(result.Failure==RetreatSearchFailure.None,result.ClearanceDetail);
        Certify(result,f.Arms,f.Traces,f.Catalog,setup,.08f);
    }

    [Theory]
    [InlineData(.03f)] [InlineData(-.03f)]
    public void NativeRiseHypothesisPreservesSelectedPoseAndHorizontalCoordinates(float grade)
    {
        var source=new Bezier3(new Vec3(100,488,-2000),new Vec3(500,488+400*grade,-2000),
            new Vec3(900,488+800*grade,-2100),new Vec3(1300,488+1200*grade,-2200));
        var pieces=Enumerable.Range(0,16).Select(i=>source.Slice(i/16f,(i+1)/16f)).ToArray();
        var shaped=pieces.Select(c=>EndpointMixedRoadSearch.ShareNativeRise(c,source.P0,Vec3.UnitX,.8f)).ToArray();
        Assert.Equal(source.P0,shaped[0].P0);
        Assert.True(Vec3.Distance(Vec3.Normalize(pieces[0].Derivative(0)),Vec3.Normalize(shaped[0].Derivative(0)))<.00001f);
        for(int i=0;i<pieces.Length;i++) {
            var before=new[]{pieces[i].P0,pieces[i].P1,pieces[i].P2,pieces[i].P3};
            var after=new[]{shaped[i].P0,shaped[i].P1,shaped[i].P2,shaped[i].P3};
            for(int j=0;j<4;j++){Assert.Equal(before[j].X,after[j].X);Assert.Equal(before[j].Z,after[j].Z);}
            Assert.InRange(Math.Abs(RoadRetreat.HorizontalLength(pieces[i])-RoadRetreat.HorizontalLength(shaped[i])),0,1e-7);
            if(i>0){Assert.Equal(shaped[i-1].P3,shaped[i].P0);Assert.True(Vec3.Distance(Vec3.Normalize(shaped[i-1].Derivative(1)),Vec3.Normalize(shaped[i].Derivative(0)))<.0001f);}
        }
        Assert.True(Math.Abs(shaped[^1].P3.Y-source.P0.Y)<Math.Abs(source.P3.Y-source.P0.Y));
    }

    internal static void Certify(RetreatSearchResult result,EndpointArm[] original,RoadApproachTrace[] traces,
        RoadInfo[] catalog,EndpointMixedRoadSetup setup,float grade)
    {
        Assert.InRange(result.Attempts,1,128); Assert.NotNull(result.Spatial);
        Assert.Equal(original.Sum(a=>a.Endpoints.Count),result.Cuts.Count);
        var sources=NodePreviewSources.Bind(traces,result.Cuts);
        var recuts=result.Cuts.Select(c=>RoadRetreat.Cut(traces.Single(t=>t.Origin.NodeIndex==c.Port!.NodeIndex),c.Distance)).ToArray();
        Assert.True(NodePlacementSnapshot.Capture(sources,result.Cuts).Matches(NodePreviewSources.Bind(traces,recuts),recuts));
        foreach(var cut in result.Cuts) {
            var trace=Assert.Single(traces.Where(t=>t.Origin.NodeIndex==cut.Port!.NodeIndex));
            Assert.All(cut.Consumed,p=>Assert.Contains(p,trace.Pieces));
            Assert.True(EndpointApproachGeometry.HasRetainedRoom(cut));
            Assert.InRange(cut.Distance,0,1000);
            Assert.Equal(trace.Origin.Prefab,cut.Port!.Prefab); Assert.Equal(trace.Origin.Lanes,cut.Port.Lanes);
        }
        var edges=result.Cuts.SelectMany(c=>c.Consumed).Select(p=>(p.EdgeIndex,p.EdgeVersion)).ToArray();
        Assert.Equal(edges.Length,edges.Distinct().Count());
        foreach(var p in result.Arms.SelectMany(a=>a.Endpoints)) {
            var old=Assert.Single(original.SelectMany(a=>a.Endpoints).Where(q=>q.NodeIndex==p.NodeIndex));
            Assert.Equal(old.NodeVersion,p.NodeVersion); Assert.Equal(old.OneWay,p.OneWay); Assert.Equal(old.Width,p.Width);
            bool start=p.OneWay&&!p.TravelOutward;
            var boundary=Assert.Single(result.Spatial!.Segments.Where(s=>s.Prefab==p.Prefab && (start?s.WorldCurve.P0:s.WorldCurve.P3)==p.Position));
            Assert.True(Vec3.Distance(Vec3.Normalize(boundary.WorldCurve.Derivative(start?0:1)),
                Vec3.Normalize(start?-p.OutwardTangent:p.OutwardTangent))<.0001f);
        }
        Assert.All(result.Spatial!.Segments,s=> {
            var road=Assert.Single(catalog.Where(r=>r.Name==s.Prefab));
            Assert.Equal(road.Width,s.RoadWidth); Assert.False(string.IsNullOrWhiteSpace(s.PathId));
            Assert.Equal(VerticalProfileFailure.None,VerticalProfile.CheckGrade(s.WorldCurve,Math.Min(grade,road.MaxSlope)));
            Assert.Equal(CurveCurvatureResult.Clear,CurveCurvature.CheckMinimumRadius(s.WorldCurve,2*road.Width));
        });
        Assert.Equal(CurveClearanceResult.Clear,EndpointNetworkClearance.Check(result.Spatial!,setup.RequiredClearance,out _));
        Assert.Equal(CurveClearanceResult.Clear,EndpointNetworkSupports.Check(result.Spatial!,setup.Design!.BridgeRules,
            setup.Ramp,out _,setup.Design.SupportRules,nativeJoinedNodes:true));
        var core=new SpatialCandidateResult(SpatialCandidateFailure.None,result.Spatial!.Segments.Where(s=>!Native(s)));
        AssertSmoothDirections(result.Spatial!,core,result);
        Assert.False(result.Spatial!.IsPlacementReady);
    }

}
