using System.Linq;
using InterchangeMod.Generation.Endpoints;
using InterchangeMod.Generation.Geometry;
using Xunit;
namespace InterchangeMod.Generation.Tests
{
    public class RetreatCloverleafTests
    {
        [Theory][InlineData(true)][InlineData(false)]
        public void SearchDoesNotAcceptIntersectingDirectAndLoopRamps(bool rightHandTraffic)
        {
            var arms=EndpointFixtures.DualCross(100,rightHandTraffic).Select(a=>new EndpointArm(a.Index,a.Endpoints.Select(p=>
                new SelectedEndpoint(p.NodeIndex,p.NodeVersion,p.Position,p.OutwardTangent,true,p.TravelOutward,20,4,p.Prefab)))).ToArray();
            var traces=arms.SelectMany(a=>a.Endpoints).Select(p=>new RoadApproachTrace(p,new[]{
                new RoadTracePiece(p.NodeIndex+1000,1,Bezier3.Line(p.Position,p.Position+Vec3.Normalize(p.OutwardTangent)*600),false,p.NodeIndex+2000,1)})).ToArray();
            var result=RetreatCloverleafSearch.Search(arms,traces,rightHandTraffic,10,22,8,.19f,500);
            Assert.Equal(RetreatSearchFailure.None,result.Failure);
            Assert.True(result.ClearanceRejected>0,"Must continue searching after rejecting a grade-feasible crossing.");
            var ramps=result.Spatial!.Segments.Where(s=>s.Kind!=CloverEdgeKind.Main).ToArray();
            for(int i=0;i<ramps.Length;i++)
                for(int j=i+1;j<ramps.Length;j++)
                    if(ramps[i].SourceArm!=ramps[j].SourceArm||ramps[i].Kind!=ramps[j].Kind)
                        Assert.True(CurveClearance.Check(ramps[i].WorldCurve,4,ramps[j].WorldCurve,4,float.MaxValue)==CurveClearanceResult.Clear,
                            $"ramp collision: {ramps[i].SourceArm}/{ramps[i].Kind} {ramps[i].WorldCurve} and {ramps[j].SourceArm}/{ramps[j].Kind} {ramps[j].WorldCurve}");
            var blocked=RetreatCloverleafSearch.Search(arms,traces,rightHandTraffic,10,22,8,.19f,500,
                maxCandidates:600,verticalClearance:50);
            Assert.Equal(RetreatSearchFailure.BudgetExceeded,blocked.Failure);
            Assert.Null(blocked.Spatial);
            Assert.True(blocked.ClearanceRejected>0);
            Assert.False(string.IsNullOrWhiteSpace(blocked.ClearanceDetail));
            Assert.Equal(RetreatSearchFailure.InvalidInput,RetreatCloverleafSearch.Search(arms,traces,rightHandTraffic,
                10,22,8,.19f,500,verticalClearance:float.NaN).Failure);
        }
        [Fact]
        public void SearchesLoggedPositionsWithSyntheticStraightRoadContinuations()
        {
            // Rounded positions from the user's 2026-10-30 log. Continuations and exact tangents are synthetic;
            // this checks scale/grade/retreat behavior, not the user's unseen original edge geometry.
            var positions=new[]{new Vec3(-3681.96f,488.63f,-1322.51f),new Vec3(-3674.93f,488.63f,-1295.43f),
                new Vec3(-3566.12f,500.67f,-1214.54f),new Vec3(-3540.68f,500.66f,-1227.68f),
                new Vec3(-3447.27f,488.63f,-1354.48f),new Vec3(-3454.29f,488.63f,-1381.56f),
                new Vec3(-3601.27f,500.42f,-1451.16f),new Vec3(-3626.7f,500.43f,-1437.95f)};
            var arms=Enumerable.Range(0,4).Select(i=>new EndpointArm(i,Enumerable.Range(0,2).Select(k=>{
                var p=positions[i*2+k];var other=positions[((i+2)%4)*2+1-k];
                return new SelectedEndpoint(i*10+k+1,1,p,p-other,true,k==1,20,4,"Road");
            }))).ToArray();
            var traces=arms.SelectMany(a=>a.Endpoints).Select(p=>new RoadApproachTrace(p,new[]{
                new RoadTracePiece(p.NodeIndex+1000,1,Bezier3.Line(p.Position,p.Position+Vec3.Normalize(p.OutwardTangent)*600),false,p.NodeIndex+2000,1)})).ToArray();
            var zero=RetreatCloverleafSearch.Search(arms,traces,true,488.63f,500.55f,8,.16f,0);
            Assert.Equal(RetreatSearchFailure.NoCandidate,zero.Failure);
            var result=RetreatCloverleafSearch.Search(arms,traces,true,488.63f,500.55f,8,.16f,500);
            Assert.Equal(RetreatSearchFailure.None,result.Failure);
            Assert.All(result.Distances,d=>Assert.InRange(d,0,500));
            Assert.Contains(result.Distances,d=>d>0);
            Assert.All(result.Spatial!.Segments,s=>Assert.InRange(s.WorldCurve.MaxGrade(256),0,.16001f));
            var styled=RetreatCloverleafSearch.Search(arms,traces,true,488.63f,500.55f,8,.16f,500,
                design:new CloverleafRoadDesign(Enumerable.Range(0,4).Select(i=>new EndpointLaneBalance(i,"Reduced",16,3,1))));
            Assert.True(styled.Failure==RetreatSearchFailure.None,$"Styled logged positions: {styled.Failure}; {styled.LastPlanarFailure}; {styled.LastSpatialFailure}; {styled.ClearanceDetail}");
            Assert.Equal(8,styled.Spatial!.Segments.Count(s=>s.Prefab=="Reduced"));
            Assert.All(styled.Spatial.Segments,s=>Assert.InRange(s.WorldCurve.MaxGrade(256),0,.16001f));
            var limited=RetreatCloverleafSearch.Search(arms,traces,true,488.63f,500.55f,8,.16f,500,maxCandidates:1);
            Assert.Equal(RetreatSearchFailure.BudgetExceeded,limited.Failure);Assert.Equal(1,limited.Attempts);Assert.Null(limited.Spatial);
            Assert.Equal(RetreatSearchFailure.InvalidInput,RetreatCloverleafSearch.Search(arms,traces,true,0,10,8,.31f,500).Failure);
        }
        [Theory][InlineData(true)][InlineData(false)]
        public void ExtendsSmallSiteOnRealTraceWithoutRaisingChosenGrade(bool rightHandTraffic)
        {
            var arms=EndpointFixtures.DualCross(100,rightHandTraffic).Select(a=>new EndpointArm(a.Index,a.Endpoints.Select(p=>
                new SelectedEndpoint(p.NodeIndex,p.NodeVersion,p.Position,p.OutwardTangent,true,p.TravelOutward,20,4,p.Prefab)))).ToArray();
            var shortPlan=PlanarCloverleafCandidate.Create(arms,rightHandTraffic,50,100,rampWidth:8);
            Assert.Equal(PlanarCandidateFailure.AttachmentsOutsideApproach,shortPlan.Failure);
            var retreated=arms.Select(a=>new EndpointArm(a.Index,a.Endpoints.Select(p=>{
                var c=Bezier3.Line(p.Position,p.Position+Vec3.Normalize(p.OutwardTangent)*500);
                var trace=new RoadApproachTrace(p,new[]{new RoadTracePiece(p.NodeIndex+1000,1,c,false,p.NodeIndex+2000,1)});
                return RoadRetreat.Cut(trace,250).Port!;
            }))).ToArray();
            var planar=PlanarCloverleafCandidate.Create(retreated,rightHandTraffic,50,100,rampWidth:8,directMiddleLength:120);
            Assert.Equal(PlanarCandidateFailure.None,planar.Failure);
            var spatial=SpatialCloverleafCandidate.Create(retreated,planar,10,22,0,.16f);
            Assert.Equal(SpatialCandidateFailure.None,spatial.Failure);
            Assert.All(spatial.Segments,s=>Assert.InRange(s.WorldCurve.MaxGrade(256),0,.16001f));
            foreach(var p in retreated.SelectMany(a=>a.Endpoints))
                Assert.Contains(spatial.Segments,s=>s.WorldCurve.P0==p.Position||s.WorldCurve.P3==p.Position);
            Assert.False(spatial.IsPlacementReady);
        }
    }
}
