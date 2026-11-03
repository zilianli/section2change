using System.Reflection;
using InterchangeMod.Generation.Endpoints;
using InterchangeMod.Generation.Geometry;

namespace InterchangeMod.Generation.Tests;

public class EndpointApproachSafetyTests
{
    private static SpatialCandidateResult Approach(Vec3 roadPosition,float grade=0)
    {
        var road=new SelectedEndpoint(1,1,roadPosition,new Vec3(0,grade,1),true,false,20,4,"Road");
        var core=new EndpointCorePort(0,false,new Vec3(0,10,0),-Vec3.UnitZ,"Road",20);
        return (SpatialCandidateResult)typeof(EndpointStackSearch).GetMethod("Approach",BindingFlags.NonPublic|BindingFlags.Static)!
            .Invoke(null,new object[]{road,core,0,"Main",.18f})!;
    }
    [Fact]
    public void RejectsAnApproachThatFoldsAcrossTheCoreEndPlane()
    {
        // The chord points forward, but its long lateral S has a middle derivative
        // pointing back through the unchanged core. Same-PathId exemptions require
        // core-axis monotonicity, not just monotonicity along this diagonal chord.
        var result=Approach(new Vec3(1000,10,100));
        Assert.NotEqual(SpatialCandidateFailure.None,result.Failure);
        Assert.Empty(result.Segments);
    }
    [Fact]
    public void RejectsMoreThan128CoursesBeforeProducingGeometry()
    {
        var result=Approach(new Vec3(0,10,20000));
        Assert.NotEqual(SpatialCandidateFailure.None,result.Failure);
        Assert.Empty(result.Segments);
    }
    [Fact]
    public void CurvedCoursesPreserveTangencyAndStayWithin20Degrees()
    {
        var start=new Vec3(200,10,300);
        var result=Approach(start,.018f);
        Assert.Equal(SpatialCandidateFailure.None,result.Failure);
        Assert.Equal(start,result.Segments[0].WorldCurve.P0);
        Assert.Equal(new Vec3(0,10,0),result.Segments[^1].WorldCurve.P3);
        var curves=result.Segments.Select(s=>s.WorldCurve).ToArray();
        Assert.True(Directions.Dot(Vec3.Normalize(curves[0].Derivative(0)),Vec3.Normalize(new Vec3(0,-.018f,-1)))>.99999f);
        Assert.True(Directions.Dot(Vec3.Normalize(curves[^1].Derivative(1)),-Vec3.UnitZ)>.99999f);
        Assert.All(curves.Zip(curves.Skip(1)),pair=>
        {
            Assert.Equal(pair.First.P3,pair.Second.P0);
            Assert.True(Directions.Dot(Vec3.Normalize(pair.First.Derivative(1)),Vec3.Normalize(pair.Second.Derivative(0)))>.99999f);
        });
        foreach(var curve in curves)
        {
            Assert.InRange(curve.Length(128),10,70.01f);
            var delta=curve.P3-curve.P0;
            var axis=Vec3.Normalize(new Vec3(delta.X,0,delta.Z));
            for(int i=0;i<=100;i++)
            {
                var derivative=curve.Derivative(i/100f);
                var tangent=Vec3.Normalize(new Vec3(derivative.X,0,derivative.Z));
                Assert.True(Directions.Dot(tangent,axis)>=Math.Cos(20*Math.PI/180)-.00001);
                Assert.True(Directions.Dot(tangent,-Vec3.UnitZ)>0);
            }
        }
    }
}
