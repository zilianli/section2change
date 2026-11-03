using InterchangeMod.Generation.Endpoints;
using InterchangeMod.Generation.Geometry;
using InterchangeMod.Generation.Roads;
using Xunit.Abstractions;

namespace InterchangeMod.Generation.Tests;

public class GridCurvedMergeTests
{
    private readonly ITestOutputHelper output;
    public GridCurvedMergeTests(ITestOutputHelper output){this.output=output;}
    internal static RoadInfo[] Catalog()=>Enumerable.Range(1,5).Select(n=>new RoadInfo($"Highway Oneway - {n} {(n==1?"lane":"lanes")}",true,n,0,4+4*n,
        groundHeight:new HeightRange(-.4f,5),elevatedHeight:new HeightRange(-2,5)){ElevatedLength=80,PillarWidth=4}).ToArray();

    [Theory]
    [InlineData(true,.16f)] [InlineData(false,.16f)]
    [InlineData(true,.30f)] [InlineData(false,.30f)]
    public void CapturedCurvedGridKeepsRoomBetweenItsFullWidthCarriageways(bool rht,float grade)
    {
        Vec3 Mirror(Vec3 p)=>new(-p.X,p.Y,p.Z);
        var traces=RealCurvedApproachTests.Traces();
        if(!rht)traces=traces.Select(t=>
        {
            var p=t.Origin;
            return new RoadApproachTrace(new SelectedEndpoint(p.NodeIndex,p.NodeVersion,Mirror(p.Position),Mirror(p.OutwardTangent),
                p.OneWay,p.TravelOutward,p.Width,p.Lanes,p.Prefab),t.Pieces.Select(c=>new RoadTracePiece(c.EdgeIndex,c.EdgeVersion,
                    new Bezier3(Mirror(c.Curve.P0),Mirror(c.Curve.P1),Mirror(c.Curve.P2),Mirror(c.Curve.P3)),c.Reversed,c.OuterNodeIndex,c.OuterNodeVersion)));
        }).ToArray();
        var arms=RealCurvedApproachTests.Arms(traces);var snapshots=arms.SelectMany(a=>a.Endpoints).Select(p=>p.Position).ToArray();
        var setup=EndpointCompactGridSetup.Create(arms,Catalog(),0,0,478.6323f);
        Assert.True(setup.Success,setup.Diagnostic);
        var result=EndpointCompactGridSearch.Search(arms,traces,rht,setup.BaseHeight,setup.LevelGap,setup.Ramp!,setup.Roads,
            grade,1000,setup.Design!,terrainHeight:478.6323f);
        Assert.True(result.Failure==RetreatSearchFailure.None,$"{result.Failure}: {result.ClearanceDetail}; {result.SupportDetail}");
        var full=result.Spatial!.Segments.Where(s=>s.Prefab=="Highway Oneway - 4 lanes").ToArray();
        double distance=double.PositiveInfinity;string nearest="";
        for(int i=0;i<full.Length;i++)for(int j=i+1;j<full.Length;j++)
        {
            var a=full[i];var b=full[j];if(a.PathId==b.PathId)continue;
            for(int p=0;p<=64;p++)for(int q=0;q<=64;q++)
            {
                var x=a.WorldCurve.Evaluate(p/64f);var y=b.WorldCurve.Evaluate(q/64f);
                if(Math.Abs(x.Y-y.Y)>1)continue;
                double d=Math.Sqrt((double)(x.X-y.X)*(x.X-y.X)+(double)(x.Z-y.Z)*(x.Z-y.Z));
                if(d<distance){distance=d;nearest=$"{a.PathId} {a.WorldCurve} / {b.PathId} {b.WorldCurve}";}
            }
        }
        output.WriteLine($"Grid curve rht={rht}, grade={grade}: retreat=[{string.Join(",",result.Distances)}], attempts={result.Attempts}, minFullDistance={distance:F4}; {nearest}");
        Assert.True(distance>=24,$"The original 8m median was pinched below a 4m connection buffer: centres {distance:F4}m apart. {nearest}");
        Assert.Equal(CurveClearanceResult.Clear,EndpointNetworkClearance.Check(result.Spatial!,setup.RequiredClearance,out _));
        Assert.Equal(CurveClearanceResult.Clear,EndpointNetworkSupports.Check(result.Spatial!,setup.Design!.BridgeRules,setup.Ramp!,out _,setup.Design.SupportRules));
        Assert.Equal(snapshots,arms.SelectMany(a=>a.Endpoints).Select(p=>p.Position));
        Assert.All(result.Spatial!.Segments,s=>Assert.Equal(VerticalProfileFailure.None,VerticalProfile.CheckGrade(s.WorldCurve,grade)));
        foreach(var cut in result.Cuts)
        {
            var port=cut.Port!;
            var segment=Assert.Single(result.Spatial!.Segments.Where(s=>port.TravelOutward?s.EndNode==port.Position:s.StartNode==port.Position));
            var tangent=port.TravelOutward?segment.WorldCurve.Derivative(1):-segment.WorldCurve.Derivative(0);
            Assert.True(Directions.Dot(Vec3.Normalize(tangent),Vec3.Normalize(port.OutwardTangent))>.9999f);
        }
    }

    [Theory]
    [InlineData(true,.16f)]
    [InlineData(false,.16f)]
    [InlineData(true,.30f)]
    [InlineData(false,.30f)]
    public void ThroughCollectorForksEaseTheirCurvatureToZero(bool rht,float grade)
    {
        var arms=CompactGridFixtures.Arms(rht);var setup=EndpointCompactGridSetup.Create(arms,CompactGridFixtures.Catalog(),0,0,93);
        var core=EndpointCompactGridCore.Create(arms,rht,setup.BaseHeight,setup.LevelGap,setup.Ramp!,setup.Roads,grade,setup.Design!,93);
        Assert.True(core.Success,core.Diagnostic);
        foreach(var path in core.Segments.GroupBy(s=>s.PathId))
        {
            if(path.Key.StartsWith("GridLeft")||path.Key.StartsWith("GridRight"))continue;
            var chain=path.ToArray();
            // Exact incoming/outgoing fork boundaries must meet their adjoining
            // straight full-width road without an instantaneous curvature step.
            for(int i=1;i<chain.Length;i++)
            {
                double before=CurveCurvature.Signed(chain[i-1].WorldCurve,1);
                double after=CurveCurvature.Signed(chain[i].WorldCurve,0);
                Assert.True(Math.Abs(before-after)<.00002,$"{path.Key} join {i}: curvature jumps {before:F6} -> {after:F6}");
            }
            if(path.Key.StartsWith("GridInCollector"))Assert.InRange(Math.Abs(CurveCurvature.Signed(chain[0].WorldCurve,0)),0,.00002);
            if(path.Key.StartsWith("GridOutCollector"))Assert.InRange(Math.Abs(CurveCurvature.Signed(chain[^1].WorldCurve,1)),0,.00002);
        }
    }

    [Fact]
    public void SecantReferenceDoesNotRequireIntersectingTangentLines()
    {
        var arms=CompactGridFixtures.Arms().Select(a=>
        {
            var direction=a.Index<2?Vec3.UnitZ:-Vec3.UnitZ;
            var center=Directions.FromAngle(a.Index*90)*700+new Vec3(0,103,0);
            return new EndpointArm(a.Index,a.Endpoints.Select(p=>new SelectedEndpoint(p.NodeIndex,p.NodeVersion,
                center+Directions.RightOf(direction)*(p.TravelOutward?14:-14),direction,p.OneWay,p.TravelOutward,p.Width,p.Lanes,p.Prefab)));
        }).ToArray();
        Assert.Equal(EndpointFrameFailure.ParallelAxes,EndpointFrame.Estimate(arms,InterchangeType.Stack).Failure);
        var setup=EndpointCompactGridSetup.Create(arms,CompactGridFixtures.Catalog(),0,0,93);
        Assert.True(setup.Success,setup.Diagnostic);
        var cores=EndpointCompactGridCore.Candidates(arms,true,setup.BaseHeight,setup.LevelGap,setup.Ramp!,setup.Roads,.16f,setup.Design!,93,secantReference:true);
        Assert.Contains(cores,c=>c.Success);
    }

    [Theory]
    [InlineData(true,80f,true)] [InlineData(false,80f,true)]
    [InlineData(true,100f,true)] [InlineData(false,100f,true)]
    [InlineData(true,79.5f,false)] [InlineData(false,79.5f,false)]
    [InlineData(true,100.5f,false)] [InlineData(false,100.5f,false)]
    public void SecantReferenceRetainsItsPhysicalRoadFamilyAngleBounds(bool rht,float angle,bool valid)
    {
        var arms=CompactGridFixtures.Arms(rht).Select(a=>
        {
            var direction=Directions.FromAngle(a.Index%2==0?a.Index*90:angle+(a.Index==3?180:0));
            var center=direction*700+new Vec3(0,103,0);
            return new EndpointArm(a.Index,a.Endpoints.Select(p=>new SelectedEndpoint(p.NodeIndex,p.NodeVersion,
                center+Directions.RightOf(direction)*(rht?1:-1)*(p.TravelOutward?14:-14),direction,p.OneWay,p.TravelOutward,p.Width,p.Lanes,p.Prefab)));
        }).ToArray();
        var setup=EndpointCompactGridSetup.Create(arms,CompactGridFixtures.Catalog(),0,0,93);
        Assert.True(setup.Success,setup.Diagnostic);
        var options=EndpointCompactGridCore.Candidates(arms,rht,setup.BaseHeight,setup.LevelGap,setup.Ramp!,setup.Roads,.16f,setup.Design!,93,secantReference:true).ToArray();
        Assert.Equal(valid,options.Any(c=>c.Success));
        if(!valid)Assert.All(options,c=>Assert.Contains("80 to 100",c.Diagnostic));
    }

    [Theory]
    [InlineData(0f,21f,true)] [InlineData(2f,20.5f,false)] [InlineData(2f,21.5f,true)]
    [InlineData(8f,22f,false)] [InlineData(8f,24.5f,true)]
    [InlineData(20f,23f,false)] [InlineData(20f,24.5f,true)]
    public void ConnectionBufferUsesTheOriginalMedianAndCapsAtFourMetres(float median,float separation,bool valid)
    {
        var narrow=CompactGridFixtures.Arms();var wide=CompactGridFixtures.Arms(lanes:6);
        var arms=narrow.Select(a=>a.Index%2==0?a:wide[a.Index]).Select(a=>
        {
            var center=(a.Endpoints[0].Position+a.Endpoints[1].Position)*.5f;
            return new EndpointArm(a.Index,a.Endpoints.Select(p=>new SelectedEndpoint(p.NodeIndex,p.NodeVersion,
                center+Directions.RightOf(p.OutwardTangent)*(p.TravelOutward?1:-1)*((p.Width+median)*.5f),
                p.OutwardTangent,p.OneWay,p.TravelOutward,p.Width,p.Lanes,p.Prefab)));
        }).ToArray();
        var segments=Enumerable.Range(0,4).Select(i=>
        {
            var p=arms[i].Endpoints[0];float distance=separation+(i%2==1?8:0),x=i<2?0:distance,z=i%2==0?0:500;
            return new SpatialSegment(i,CloverEdgeKind.Main,Bezier3.Line(new Vec3(x,103,z),new Vec3(x,103,z+100)),
                prefab:p.Prefab,roadWidth:p.Width,pathId:"GridMain"+i);
        });
        var result=EndpointGridConnectionClearance.Check(new SpatialCandidateResult(SpatialCandidateFailure.None,segments),arms,8.5f,out _);
        Assert.Equal(valid,result==CurveClearanceResult.Clear);
    }

    [Theory]
    [InlineData(true,40f)] [InlineData(false,40f)]
    [InlineData(true,60f)] [InlineData(false,60f)]
    [InlineData(true,100f)] [InlineData(false,100f)]
    public void WideMediansUseTheCollectorDisplacementToSizeTheirTapers(bool rht,float median)
    {
        var arms=CompactGridFixtures.Arms(rht).Select(a=>
        {
            var center=(a.Endpoints[0].Position+a.Endpoints[1].Position)*.5f;
            return new EndpointArm(a.Index,a.Endpoints.Select(p=>new SelectedEndpoint(p.NodeIndex,p.NodeVersion,
                center+Directions.RightOf(p.OutwardTangent)*(rht?1:-1)*(p.TravelOutward?1:-1)*((p.Width+median)*.5f),
                p.OutwardTangent,p.OneWay,p.TravelOutward,p.Width,p.Lanes,p.Prefab)));
        }).ToArray();
        var setup=EndpointCompactGridSetup.Create(arms,CompactGridFixtures.Catalog(),0,0,93);
        var core=EndpointCompactGridCore.Create(arms,rht,setup.BaseHeight,setup.LevelGap,setup.Ramp!,setup.Roads,.16f,setup.Design!,93);
        Assert.True(core.Success,core.Diagnostic);
        foreach(var segment in core.Segments.Where(s=>!s.PathId!.StartsWith("GridLeft")&&!s.PathId.StartsWith("GridRight")))
            Assert.Equal(CurveCurvatureResult.Clear,CurveCurvature.CheckMinimumRadius(segment.WorldCurve,40));
    }
}
