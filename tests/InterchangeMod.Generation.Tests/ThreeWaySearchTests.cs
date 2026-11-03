using System;
using System.Linq;
using System.Collections.Generic;
using InterchangeMod.Generation.Endpoints;
using InterchangeMod.Generation.Geometry;

namespace InterchangeMod.Generation.Tests;

public class ThreeWaySearchTests
{
    [Fact]
    public void TwoLevelPreviewRequiresAnExplicitTerrainReference()
    {
        var arms=ThreeWayFixtures.Arms();
        var result=EndpointThreeWaySearch.Search(arms,ThreeWayFixtures.Traces(arms),InterchangeType.DirectionalT,true,
            103,113,ThreeWayFixtures.Ramp,.16f,1000,new CloverleafRoadDesign(),directionalTwoLevel:true);
        Assert.Equal(RetreatSearchFailure.InvalidInput,result.Failure);Assert.Null(result.Spatial);Assert.Empty(result.Cuts);
    }
    [Theory]
    [InlineData(InterchangeType.DirectionalT)]
    [InlineData(InterchangeType.SemiDirectionalT)]
    public void TerrainReferenceReachesTheCompleteTwoLevelPreview(InterchangeType type)
    {
        var arms=ThreeWayFixtures.Arms();
        var result=EndpointThreeWaySearch.Search(arms,ThreeWayFixtures.Traces(arms),type,true,103,113,
            ThreeWayFixtures.Ramp,.16f,1000,new CloverleafRoadDesign(),directionalTwoLevel:true,terrainHeight:100);
        Assert.True(result.Failure==RetreatSearchFailure.None,result.ClearanceDetail+result.SupportDetail);
        Assert.Contains(result.Spatial!.Segments,s=>Enumerable.Range(0,65).Any(i=>s.WorldCurve.Evaluate(i/64f).Y<=87.001f));
    }
    private static RetreatSearchResult Search(EndpointArm[] arms,RoadApproachTrace[] traces,InterchangeType type,
        bool rht=true,float limit=1000,int budget=128)=>EndpointThreeWaySearch.Search(arms,traces,type,rht,103,113,
            ThreeWayFixtures.Ramp,.16f,limit,new CloverleafRoadDesign(),maxCandidates:budget);
    [Theory]
    [InlineData(InterchangeType.Trumpet,true)]
    [InlineData(InterchangeType.Trumpet,false)]
    [InlineData(InterchangeType.DirectionalT,true)]
    [InlineData(InterchangeType.DirectionalT,false)]
    [InlineData(InterchangeType.SemiDirectionalT,true)]
    [InlineData(InterchangeType.SemiDirectionalT,false)]
    public void CompletePreviewConnectsSixOriginalPortsAndAllSixMovements(InterchangeType type,bool rht)
    {
        var arms=ThreeWayFixtures.Arms(rht);var result=Search(arms,ThreeWayFixtures.Traces(arms),type,rht);
        Assert.True(result.Failure==RetreatSearchFailure.None,result.ClearanceDetail+result.SupportDetail);
        Assert.NotNull(result.Spatial);Assert.False(result.Spatial!.IsPlacementReady);
        Assert.Equal(6,result.Cuts.Count);Assert.InRange(result.Attempts,1,128);
        var segments=result.Spatial.Segments;
        bool Reaches(Vec3 start,Vec3 end)
        {
            var seen=new HashSet<Vec3>{start};var todo=new Queue<Vec3>();todo.Enqueue(start);
            while(todo.Count>0)
            {var node=todo.Dequeue();foreach(var s in segments.Where(s=>s.StartNode==node))if(seen.Add(s.EndNode))todo.Enqueue(s.EndNode);}
            return seen.Contains(end);
        }
        foreach(var a in result.Arms)foreach(var b in result.Arms.Where(b=>b.Index!=a.Index))
            Assert.True(Reaches(a.Endpoints.Single(p=>!p.TravelOutward).Position,b.Endpoints.Single(p=>p.TravelOutward).Position));
        Assert.Equal(CurveClearanceResult.Clear,EndpointNetworkClearance.Check(result.Spatial,8.5f,out _));
        Assert.All(segments,s=>Assert.Equal(VerticalProfileFailure.None,VerticalProfile.CheckGrade(s.WorldCurve,.16f)));
    }
    [Theory]
    [InlineData(InterchangeType.Trumpet)]
    [InlineData(InterchangeType.DirectionalT)]
    [InlineData(InterchangeType.SemiDirectionalT)]
    public void ExtendsTightOpeningUsingActualRoadCurves(InterchangeType type)
    {
        var arms=ThreeWayFixtures.Arms(length:40);var result=Search(arms,ThreeWayFixtures.Traces(arms),type);
        Assert.True(result.Failure==RetreatSearchFailure.None,result.ClearanceDetail+result.SupportDetail);
        Assert.Contains(result.Distances,d=>d>0);Assert.All(result.Distances,d=>Assert.InRange(d,0,1000));
        Assert.All(result.Cuts,c=>Assert.True(EndpointApproachGeometry.HasRetainedRoom(c)));
    }
    [Fact]
    public void IndependentWindowsDoNotExtendTheShortArmPastItsRealTrace()
    {
        var arms=ThreeWayFixtures.Arms(length:40);arms[0]=ThreeWayFixtures.Arms()[0];
        var traces=arms.SelectMany(a=>a.Endpoints.Select(p=>new RoadApproachTrace(p,new[]{new RoadTracePiece(p.NodeIndex+1000,1,
            Bezier3.Line(p.Position,p.Position+p.OutwardTangent*(a.Index==0?37:1000)),false,p.NodeIndex+2000,1)}))).ToArray();
        var result=Search(arms,traces,InterchangeType.Trumpet);
        Assert.True(result.Failure==RetreatSearchFailure.None,result.ClearanceDetail+result.SupportDetail);
        Assert.InRange(result.Distances[0],0,17);Assert.Contains(result.Distances.Skip(1),d=>d>17);
    }
    [Fact]
    public void FailureAndBudgetNeverExposePartialNetworks()
    {
        var arms=ThreeWayFixtures.Arms(length:40);var traces=ThreeWayFixtures.Traces(arms);
        var failed=Search(arms,traces,InterchangeType.Trumpet,limit:0,budget:1);
        Assert.NotEqual(RetreatSearchFailure.None,failed.Failure);Assert.Null(failed.Spatial);Assert.Empty(failed.Cuts);
        Assert.InRange(failed.Attempts,0,1);
        Assert.Equal(RetreatSearchFailure.InvalidInput,Search(arms,traces,InterchangeType.Wye).Failure);
        Assert.Equal(RetreatSearchFailure.InvalidInput,Search(arms,traces,InterchangeType.Trumpet,limit:1001).Failure);
    }
}
