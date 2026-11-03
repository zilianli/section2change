using System;
using System.Linq;
using InterchangeMod.Generation.Endpoints;
using InterchangeMod.Generation.Geometry;
using InterchangeMod.Generation.Roads;
using Xunit;

namespace InterchangeMod.Generation.Tests
{
    public class EndpointLaneBalanceTests
    {
        private static EndpointArm[] WideArms(bool hand)=>EndpointFixtures.DualCross(450,hand).Select(a=>
            new EndpointArm(a.Index,a.Endpoints.Select(p=>new SelectedEndpoint(p.NodeIndex,p.NodeVersion,p.Position,
                p.OutwardTangent,p.OneWay,p.TravelOutward,20,4,"Full")))).ToArray();

        [Theory][InlineData(true)][InlineData(false)]
        public void RampOuterEdgeMatchesFullWidthCarriageway(bool hand)
        {
            var arms=WideArms(hand);
            var plan=PlanarCloverleafCandidate.Create(arms,hand,40,100,rampWidth:8,directMiddleLength:180,
                design:new CloverleafRoadDesign(smoothDirect:false));
            Assert.Equal(PlanarCandidateFailure.None,plan.Failure);
            foreach(var ramp in plan.Ramps)
            {
                var line=plan.Lines.Single(l=>l.SourceArm==ramp.SourceArm);
                var mainDirection=Vec3.Normalize(line.End-line.Start);
                var outer=new Vec3(mainDirection.Z,0,-mainDirection.X)*(hand?1:-1);
                var junction=line.Junctions.Single(j=>j.Node.Kind==(ramp.Kind==CloverEdgeKind.DirectRamp?CloverNodeKind.DirectExit:CloverNodeKind.LoopExit));
                var offset=ramp.Curves[0].P0-junction.Position;
                Assert.InRange(offset.X*outer.X+offset.Z*outer.Z,5.99f,6.01f);
            }
        }

        [Theory][InlineData(true)][InlineData(false)]
        public void ReducesTwoSectionsPerCarriagewayAndPreservesInnerEdgeAndAllMovements(bool hand)
        {
            var arms=WideArms(hand);
            var balances=Enumerable.Range(0,4).Select(i=>new EndpointLaneBalance(i,"Reduced",16,3,1)).ToArray();
            var plan=PlanarCloverleafCandidate.Create(arms,hand,40,100,rampWidth:8,directMiddleLength:180,
                design:new CloverleafRoadDesign(balances,smoothDirect:false));
            Assert.Equal(PlanarCandidateFailure.None,plan.Failure);
            var spatial=SpatialCloverleafCandidate.Create(arms,plan,10,22,0,.19f);
            Assert.Equal(SpatialCandidateFailure.None,spatial.Failure);
            Assert.Equal(8,spatial.Segments.Count(s=>s.Prefab=="Reduced"));
            for(int arm=0;arm<4;arm++)
            {
                var main=spatial.Segments.Where(s=>s.Kind==CloverEdgeKind.Main&&s.SourceArm==arm).ToArray();
                Assert.Equal(new[]{"Full","Reduced","Full","Reduced","Full"},main.Select(s=>s.Prefab).ToArray());
                var direction=Vec3.Normalize(plan.Lines.Single(l=>l.SourceArm==arm).End-plan.Lines.Single(l=>l.SourceArm==arm).Start);
                var outer=new Vec3(direction.Z,0,-direction.X)*(hand?1:-1);
                foreach(var reduced in main.Where(s=>s.Prefab=="Reduced"))
                {
                    var offset=reduced.WorldCurve.P0-reduced.StartNode;
                    Assert.InRange(offset.X*outer.X+offset.Z*outer.Z,-2.001f,-1.999f);
                    Assert.Equal(16,reduced.RoadWidth);
                }
                var queue=new System.Collections.Generic.Queue<Vec3>();var seen=new System.Collections.Generic.HashSet<Vec3>();
                queue.Enqueue(arms[arm].Endpoints.Single(p=>!p.TravelOutward).Position);
                while(queue.Count>0)
                {
                    var node=queue.Dequeue();if(!seen.Add(node))continue;
                    foreach(var s in spatial.Segments.Where(s=>s.StartNode==node))queue.Enqueue(s.EndNode);
                }
                for(int destination=0;destination<4;destination++)
                    if(destination!=arm)Assert.Contains(arms[destination].Endpoints.Single(p=>p.TravelOutward).Position,seen);
            }
            Assert.Equal(CurveClearanceResult.Clear,CloverleafRoadClearance.Check(spatial,plan,8,8.5f,out var detail));
        }

        [Fact]
        public void SelectsExactForwardLaneCountAndFallsBackWhenUnavailable()
        {
            var arms=WideArms(true);
            var full=new RoadInfo("Full",true,4,0,20);
            var ramp=new RoadInfo("Ramp",true,1,0,8);
            var wrongWay=new RoadInfo("Backwards",true,0,3,16);
            var wrongType=new RoadInfo("Street",false,3,0,16);
            var wrongCount=new RoadInfo("Two",true,2,0,12);
            var reduced=new RoadInfo("Reduced",true,3,0,16);
            Assert.Empty(EndpointLaneBalance.Select(arms,new[]{full,wrongWay,wrongType,wrongCount},ramp));
            Assert.All(EndpointLaneBalance.Select(arms,new[]{full,wrongWay,wrongCount,reduced},ramp),b=>Assert.Equal("Reduced",b.Prefab));
            Assert.Equal(4,EndpointLaneBalance.Select(arms,new[]{full,reduced},ramp).Count);
        }

        [Theory][InlineData(true)][InlineData(false)]
        public void SmoothDirectRampsUseTangentContinuousCurvesInsteadOfDiagonalStraights(bool hand)
        {
            var arms=WideArms(hand);
            var plan=PlanarCloverleafCandidate.Create(arms,hand,40,100,rampWidth:8,directMiddleLength:180,
                design:new CloverleafRoadDesign());
            Assert.Equal(PlanarCandidateFailure.None,plan.Failure);
            foreach(var ramp in plan.Ramps.Where(r=>r.Kind==CloverEdgeKind.DirectRamp))
                for(int i=0;i<ramp.Curves.Count;i++)
                {
                    var c=ramp.Curves[i];
                    var a=Vec3.Normalize(c.Derivative(0));var b=Vec3.Normalize(c.Derivative(1));
                    Assert.True(Math.Abs(a.X*b.Z-a.Z*b.X)>1e-4f,"The direct ramp still contains a straight diagonal.");
                    Assert.InRange(c.Length(128),9.99f,70.1f);
                    if(i>0)
                    {
                        Assert.Equal(ramp.Curves[i-1].P3,c.P0);
                        var previous=Vec3.Normalize(ramp.Curves[i-1].Derivative(1));
                        Assert.True(previous.X*a.X+previous.Z*a.Z>.9999f);
                    }
                }
        }

        [Theory][InlineData(true,.16f)][InlineData(false,.16f)][InlineData(true,.19f)][InlineData(false,.19f)]
        public void StyledSearchPreservesGradeAndLaneBalanceWithRealRetreat(bool hand,float grade)
        {
            var arms=WideArms(hand).Select(a=>new EndpointArm(a.Index,a.Endpoints.Select(p=>new SelectedEndpoint(p.NodeIndex,
                p.NodeVersion,p.Position-Vec3.Normalize(p.OutwardTangent)*350,new Vec3(p.OutwardTangent.X,0,p.OutwardTangent.Z),p.OneWay,p.TravelOutward,20,4,p.Prefab)))).ToArray();
            var traces=arms.SelectMany(a=>a.Endpoints).Select(p=>new RoadApproachTrace(p,new[]{
                new RoadTracePiece(p.NodeIndex+1000,1,Bezier3.Line(p.Position,p.Position+Vec3.Normalize(p.OutwardTangent)*1200),false,p.NodeIndex+2000,1)})).ToArray();
            var balances=Enumerable.Range(0,4).Select(i=>new EndpointLaneBalance(i,"Reduced",16,3,1));
            var result=RetreatCloverleafSearch.Search(arms,traces,hand,10,22,8,grade,500,
                design:new CloverleafRoadDesign(balances));
            Assert.True(result.Failure==RetreatSearchFailure.None,$"{result.Failure}, planar={result.LastPlanarFailure}, spatial={result.LastSpatialFailure}, clearance={result.ClearanceDetail}");
            Assert.Equal(8,result.Spatial!.Segments.Count(s=>s.Prefab=="Reduced"));
            Assert.All(result.Spatial.Segments,s=>Assert.InRange(s.WorldCurve.MaxGrade(256),0,grade+.00001f));
            var ramps=result.Spatial.Segments.Where(s=>s.Kind!=CloverEdgeKind.Main).ToArray();
            for(int i=0;i<ramps.Length;i++)
                for(int j=i+1;j<ramps.Length;j++)
                    if(ramps[i].SourceArm!=ramps[j].SourceArm||ramps[i].Kind!=ramps[j].Kind)
                        Assert.Equal(CurveClearanceResult.Clear,CurveClearance.Check(ramps[i].WorldCurve,4,ramps[j].WorldCurve,4,float.MaxValue));
        }

        [Fact]
        public void RejectsInvalidBalanceWithoutProducingPartialGeometry()
        {
            var arms=WideArms(true);
            foreach(var balances in new[]{new[]{new EndpointLaneBalance(0,"Reduced",30,3,1)},
                new[]{new EndpointLaneBalance(0,"Reduced",16,2,1)},
                new[]{new EndpointLaneBalance(0,"Reduced",16,3,1),new EndpointLaneBalance(0,"Other",16,3,1)}})
            {
                var plan=PlanarCloverleafCandidate.Create(arms,true,40,100,rampWidth:8,design:new CloverleafRoadDesign(balances));
                Assert.Equal(PlanarCandidateFailure.InvalidRoadWidth,plan.Failure);
                Assert.Empty(plan.Ramps);
            }
        }

        [Theory][InlineData(true,15f)][InlineData(false,15f)][InlineData(true,20f)][InlineData(false,20f)]
        public void RoundedSearchRetainsSolvableSkewInterchanges(bool hand,float skew)
        {
            var arms=WideArms(hand).Select(a=>new EndpointArm(a.Index,a.Endpoints.Select(p=>{
                float angle=a.Index%2==1?skew*(float)Math.PI/180:0;
                return new SelectedEndpoint(p.NodeIndex,p.NodeVersion,(p.Position+Vec3.Normalize(p.OutwardTangent)*350).RotateY(angle),
                    p.OutwardTangent.RotateY(angle),p.OneWay,p.TravelOutward,p.Width,p.Lanes,p.Prefab);
            }))).ToArray();
            var traces=arms.SelectMany(a=>a.Endpoints).Select(p=>new RoadApproachTrace(p,new[]{
                new RoadTracePiece(p.NodeIndex+1000,1,Bezier3.Line(p.Position,p.Position+Vec3.Normalize(p.OutwardTangent)*1200),false,p.NodeIndex+2000,1)})).ToArray();
            var balances=Enumerable.Range(0,4).Select(i=>new EndpointLaneBalance(i,"Reduced",16,3,1)).ToArray();
            var baseline=RetreatCloverleafSearch.Search(arms,traces,hand,10,22,8,.30f,0,
                design:new CloverleafRoadDesign(balances,smoothDirect:false));
            Assert.Equal(RetreatSearchFailure.None,baseline.Failure);
            var styled=RetreatCloverleafSearch.Search(arms,traces,hand,10,22,8,.30f,0,
                design:new CloverleafRoadDesign(balances));
            Assert.True(styled.Failure==RetreatSearchFailure.None,$"Skew {skew}: {styled.Failure}; {styled.ClearanceDetail}");
            var largerLimit=RetreatCloverleafSearch.Search(arms,traces,hand,10,22,8,.30f,1000,
                design:new CloverleafRoadDesign(balances));
            Assert.True(largerLimit.Failure==RetreatSearchFailure.None,$"Larger retreat limit: {largerLimit.Failure}");
        }
    }
}
