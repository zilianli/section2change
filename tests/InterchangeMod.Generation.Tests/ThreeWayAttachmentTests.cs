using System;
using System.Linq;
using InterchangeMod.Generation.Endpoints;
using InterchangeMod.Generation.Geometry;
using InterchangeMod.Generation.Roads;

namespace InterchangeMod.Generation.Tests;

internal static class ThreeWayFixtures
{
    internal static RoadInfo Ramp=>new RoadInfo("Ramp8",true,1,0,8);
    internal static EndpointArm[] Arms(bool rht=true,float length=1500)
        =>Enumerable.Range(0,3).Select(i=>
        {
            var axis=Directions.FromAngle(i*90);var side=Directions.RightOf(axis)*(rht?1:-1);
            float width=i==1?16:20;var center=new Vec3(400,103,-230)+axis*length;
            return new EndpointArm(i,new[]{
                new SelectedEndpoint(i*10+1,1,center-side*((width+8)/2),axis,true,false,width,i==1?2:3,i==1?"Branch16":"Main20"),
                new SelectedEndpoint(i*10+2,1,center+side*((width+8)/2),axis,true,true,width,i==1?2:3,i==1?"Branch16":"Main20")});
        }).ToArray();
    internal static RoadApproachTrace[] Traces(EndpointArm[] arms,float length=1000)
        =>arms.SelectMany(a=>a.Endpoints).Select(p=>new RoadApproachTrace(p,new[]{new RoadTracePiece(p.NodeIndex+1000,1,
            Bezier3.Line(p.Position,p.Position+p.OutwardTangent*length),false,p.NodeIndex+2000,1)})).ToArray();
}

public class ThreeWayAttachmentTests
{
    [Theory]
    [InlineData(InterchangeType.Trumpet,true)]
    [InlineData(InterchangeType.Trumpet,false)]
    [InlineData(InterchangeType.DirectionalT,true)]
    [InlineData(InterchangeType.DirectionalT,false)]
    [InlineData(InterchangeType.SemiDirectionalT,true)]
    [InlineData(InterchangeType.SemiDirectionalT,false)]
    public void JoinsEachOfSixPortsIncludingHalfOpenBranchPaths(InterchangeType type,bool rht)
    {
        var arms=ThreeWayFixtures.Arms(rht);
        var core=EndpointThreeWayCore.Create(arms,type,rht,103,113,ThreeWayFixtures.Ramp,.16f,new CloverleafRoadDesign());
        Assert.True(core.Success,core.Diagnostic);
        var result=EndpointStackSearch.AttachWithHandle(core,arms,.16f,1f/3);
        Assert.Equal(SpatialCandidateFailure.None,result.Failure);
        var leaves=result.Segments.SelectMany(s=>new[]{s.StartNode,s.EndNode}).GroupBy(p=>p).Where(g=>g.Count()==1).Select(g=>g.Key).ToArray();
        Assert.Equal(6,leaves.Length);
        foreach(var p in arms.SelectMany(a=>a.Endpoints))Assert.Contains(p.Position,leaves);
        Assert.All(result.Segments,s=>Assert.Equal(VerticalProfileFailure.None,VerticalProfile.CheckGrade(s.WorldCurve,.16f)));
        Assert.Equal(CurveClearanceResult.Clear,EndpointNetworkClearance.Check(result,8.5f,out _));
    }
}
