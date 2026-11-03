using InterchangeMod.Generation.Endpoints;
using InterchangeMod.Generation.Geometry;
using InterchangeMod.Generation.Roads;

namespace InterchangeMod.Generation.Tests;

public sealed class LowGradeRetreatTests
{
    // Pure synthetic regression: 250m is the original center-to-end distance;
    // 500m limits ADDITIONAL retreat, not the final center-to-end distance.
    [Fact]
    public void EightPercentRespects500mAdditionalRetreatOnTwoLaneRoads()
    {
        var axes=new[]{Vec3.UnitZ,Vec3.UnitX,-Vec3.UnitZ,-Vec3.UnitX};
        var arms=axes.Select((axis,i)=>
        {
            var center=axis*250+new Vec3(0,i%2==0?10:20,0);
            var right=Directions.RightOf(axis);
            return new EndpointArm(i,new[]{
                new SelectedEndpoint(i*10+1,1,center-right*7,axis,true,false,12,2,"Synthetic2Lane12"),
                new SelectedEndpoint(i*10+2,1,center+right*7,axis,true,true,12,2,"Synthetic2Lane12")});
        }).ToArray();
        var traces=arms.SelectMany(a=>a.Endpoints).Select(p=>new RoadApproachTrace(p,new[]{
            new RoadTracePiece(p.NodeIndex+1000,1,Bezier3.Line(p.Position,p.Position+p.OutwardTangent*600),
                !p.TravelOutward,p.NodeIndex+2000,1)})).ToArray();
        var ramp=new RoadInfo("SyntheticRamp8",true,1,0,8);
        var design=new CloverleafRoadDesign();
        var result=EndpointStackSearch.Search(arms,traces,InterchangeType.Stack,true,10,20,ramp,.08f,500,design);
        Assert.True(result.Failure==RetreatSearchFailure.None,result.ClearanceDetail);
        Assert.True(result.CoreMatched);
        Assert.All(result.Distances,d=>Assert.InRange(d,0,500));
        Assert.All(result.Spatial!.Segments,s=>Assert.Equal(VerticalProfileFailure.None,VerticalProfile.CheckGrade(s.WorldCurve,.08f)));
        Assert.Equal(CurveClearanceResult.Clear,EndpointNetworkClearance.Check(result.Spatial,8.5f,out _));
        Assert.Equal(CurveClearanceResult.Clear,EndpointNetworkSupports.Check(result.Spatial,design.BridgeRules,ramp,out _));
        Assert.Equal(8,result.Cuts.Count);
        Assert.False(result.Spatial.IsPlacementReady);
    }
}
