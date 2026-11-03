using InterchangeMod.Generation.Endpoints;
using InterchangeMod.Generation.Geometry;

namespace InterchangeMod.Generation.Tests;

public class ApproachCurveQualityTests
{
    [Fact]
    public void A65mAllowanceCanUseTwoValid65mCourses()
    {
        var pieces=EndpointApproachGeometry.SplitCourses(Bezier3.Line(Vec3.Zero,new Vec3(0,0,130)),65,.16f,Vec3.UnitZ);
        Assert.NotNull(pieces);Assert.Equal(2,pieces!.Count);
        Assert.All(pieces,p=>Assert.InRange(p.Length(128),65,70));
    }
    [Fact]
    public void NodeAllowanceUsesHorizontalLengthRatherThanSlopeInflation()
    {
        var port=new SelectedEndpoint(1,1,Vec3.Zero,new Vec3(0,.15f,1),true,true,20,4,"Road");
        var trace=new RoadApproachTrace(port,new[]{new RoadTracePiece(10,1,
            Bezier3.Line(Vec3.Zero,new Vec3(0,3.15f,21)),false,2,1)});
        var cut=RoadRetreat.Cut(trace,1.2f);
        Assert.Equal(RoadRetreatFailure.None,cut.Failure);
        Assert.True(cut.Remainder!.Value.Length(128)>20);
        Assert.True(RoadRetreat.HorizontalLength(cut.Remainder.Value)<20);
        Assert.False(EndpointApproachGeometry.HasRetainedRoom(cut));
        Assert.Null(EndpointApproachGeometry.SplitCourses(Bezier3.Line(Vec3.Zero,new Vec3(0,2.97f,19.8f)),20,.16f,Vec3.UnitZ));
    }
    [Theory]
    [InlineData(69795,225,13.0834)]
    [InlineData(69797,350,17.529)]
    public void ExcludesLoggedShortTailsFor20mWideRoads(int node,float retreat,double expectedTail)
    {
        var trace=RealCurvedApproachTests.Traces().Single(t=>t.Origin.NodeIndex==node);
        var cut=RoadRetreat.Cut(trace,retreat);
        Assert.Equal(RoadRetreatFailure.None,cut.Failure);
        Assert.NotNull(cut.Remainder);
        Assert.InRange(cut.Remainder!.Value.Length(256),expectedTail-.03,expectedTail+.03);
        Assert.False(EndpointApproachGeometry.HasRetainedRoom(cut));
    }
    [Fact]
    public void RejectsLoggedTightCurveButCertifiesA100mArc()
    {
        var logged=new Bezier3(new Vec3(507.9453f,488.6323f,-2403.108f),new Vec3(512.3361f,488.7293f,-2404.094f),
            new Vec3(518.6427f,488.2652f,-2403.826f),new Vec3(526.0842f,489.6724f,-2402.847f));
        Assert.Equal(CurveCurvatureResult.Conflict,CurveCurvature.CheckMinimumRadius(logged,40));
        float k=.55228475f;
        var quarter=new Bezier3(new Vec3(100,0,0),new Vec3(100,0,k*100),new Vec3(k*100,0,100),new Vec3(0,0,100));
        Assert.Equal(CurveCurvatureResult.Clear,CurveCurvature.CheckMinimumRadius(quarter,40));
    }
    [Fact]
    public void EqualArcCoursesAvoidShortFirstPieceDespiteNonuniformParameterSpeed()
    {
        var c=new Bezier3(Vec3.Zero,new Vec3(0,0,8),new Vec3(0,0,580),new Vec3(0,0,600));
        Assert.True(c.Slice(0,.1f).Length()<20);
        var pieces=EndpointApproachGeometry.SplitCourses(c,20,.16f,Vec3.UnitZ);
        Assert.NotNull(pieces);Assert.NotEmpty(pieces!);
        Assert.Equal(c.P0,pieces![0].P0);Assert.Equal(c.P3,pieces[^1].P3);
        Assert.All(pieces,p=>Assert.InRange(p.Length(256),20,70.01f));
        Assert.All(pieces.Zip(pieces.Skip(1)),pair=>Assert.Equal(pair.First.P3,pair.Second.P0));
    }
    [Fact]
    public void CurvatureCertificateNeverAcceptsACuspOrInvalidRadius()
    {
        var cusp=new Bezier3(Vec3.Zero,Vec3.UnitZ,-Vec3.UnitZ,Vec3.Zero);
        Assert.NotEqual(CurveCurvatureResult.Clear,CurveCurvature.CheckMinimumRadius(cusp,40));
        Assert.Equal(CurveCurvatureResult.InvalidInput,CurveCurvature.CheckMinimumRadius(Bezier3.Line(Vec3.Zero,Vec3.UnitZ),float.NaN));
    }
}
