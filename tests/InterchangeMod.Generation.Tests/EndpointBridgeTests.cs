using System;
using System.Linq;
using InterchangeMod.Generation.Endpoints;
using InterchangeMod.Generation.Geometry;
using Xunit;

namespace InterchangeMod.Generation.Tests
{
    public class EndpointBridgeTests
    {
        [Theory][InlineData(.16f)][InlineData(.27f)]
        public void CentralSpanKeepsAllAutomaticAndNodePillarsOffLowerRoads(float grade)
        {
            var arms=MixedApproachTests.LoggedArms(false);
            var traces=arms.SelectMany(a=>a.Endpoints).Select(p=>new RoadApproachTrace(p,new[]{
                new RoadTracePiece(p.NodeIndex+1000,1,Bezier3.Line(p.Position,p.Position+Vec3.Normalize(p.OutwardTangent)*600),false,p.NodeIndex+2000,1)})).ToArray();
            var design=new CloverleafRoadDesign(Enumerable.Range(0,4).Select(i=>new EndpointLaneBalance(i,"Reduced",16,3,1)),
                bridgeRules:Enumerable.Range(0,4).Select(i=>new EndpointBridgeRule(i,80,null,20)));
            var result=RetreatCloverleafSearch.Search(arms,traces,true,488.63f,498.63f,8,grade,500,design:design);
            Assert.True(result.Failure==RetreatSearchFailure.None,$"{result.Failure}/{result.LastSpatialFailure}; {result.SupportDetail}");
            foreach(var upper in result.Spatial!.Segments.Where(s=>s.IsWeaving&&s.SourceArm%2==1))
            {
                var curve=upper.WorldCurve;float length=curve.Length();
                var dir=Vec3.Normalize(curve.P3-curve.P0);var side=new Vec3(dir.Z,0,-dir.X);
                var supports=PillarPlanner.InnerPillars(0,length,null,80).Concat(new[]{0f,length});
                foreach(float at in supports)
                {
                    var point=curve.Evaluate(at/length);
                    var beam=Bezier3.Line(point-side*10,point+side*10);
                    foreach(var lower in result.Spatial.Segments.Where(s=>s.Kind==CloverEdgeKind.Main&&s.SourceArm%2==0))
                        Assert.True(CurveClearance.Check(beam,1.5f,lower.WorldCurve,lower.RoadWidth!.Value/2,float.MaxValue)==CurveClearanceResult.Clear,
                            $"Support at {point} intersects lower carriageway {lower.WorldCurve}");
                }
            }
        }

        [Theory][InlineData(19.9f,true)][InlineData(20f,true)][InlineData(20.1f,false)]
        public void ApproachDeflectionHasTwentyDegreeBoundary(float angle,bool allowed)
        {
            var arms=EndpointFixtures.DualCross(450);
            arms[0]=new EndpointArm(0,arms[0].Endpoints.Select(p=>new SelectedEndpoint(p.NodeIndex,p.NodeVersion,p.Position,
                p.OutwardTangent.RotateY(angle*(float)Math.PI/180),p.OneWay,p.TravelOutward,p.Width,p.Lanes,p.Prefab)));
            var planar=PlanarCloverleafCandidate.Create(arms,true,40,100,rampWidth:8,directMiddleLength:180,design:new CloverleafRoadDesign());
            Assert.Equal(allowed?PlanarCandidateFailure.None:PlanarCandidateFailure.NonStraightApproach,planar.Failure);
        }

        [Fact]
        public void InvalidSupportRulesAndDensePillarsDoNotProducePartialPreview()
        {
            var arms=EndpointFixtures.DualCross(450);
            var plan=PlanarCloverleafCandidate.Create(arms,true,40,100,rampWidth:8,directMiddleLength:180,design:new CloverleafRoadDesign());
            var spatial=SpatialCloverleafCandidate.Create(arms,plan,10,20,0,.30f);
            Assert.Equal(SpatialCandidateFailure.None,spatial.Failure);
            var bad=CloverleafBridgePlanner.Apply(spatial,new[]{new EndpointBridgeRule(1,0,null,12)});
            Assert.Equal(SpatialCandidateFailure.InvalidParameter,bad.Failure);Assert.Empty(bad.Segments);
            var dense=CloverleafBridgePlanner.Apply(spatial,new[]{new EndpointBridgeRule(1,80,10,12)});
            Assert.Equal(SpatialCandidateFailure.PillarConflict,dense.Failure);Assert.Empty(dense.Segments);
        }

        [Fact]
        public void AlternateSupportsCanAvoidRampBlockingTheFirstBridgePlan()
        {
            bool Clear(float at)
            {
                var beam=Bezier3.Line(new Vec3(at,10,-6),new Vec3(at,10,6));
                var road=Bezier3.Line(new Vec3(50,0,-100),new Vec3(50,0,100));
                var ramp=Bezier3.Line(new Vec3(40.5f,0,-100),new Vec3(40.5f,0,100));
                return CurveClearance.Check(beam,2.5f,road,5,float.MaxValue)==CurveClearanceResult.Clear
                    &&CurveClearance.Check(beam,2.5f,ramp,1,float.MaxValue)==CurveClearanceResult.Clear;
            }
            var nodes=BridgeSupportSearch.Find(160,20,80,null,Clear);
            Assert.NotNull(nodes);Assert.Equal(0,nodes![0]);Assert.Equal(160,nodes[nodes.Count-1]);
            Assert.All(nodes,n=>Assert.True(Clear(n)));
            for(int i=0;i+1<nodes.Count;i++)
            {
                Assert.True(nodes[i+1]-nodes[i]>=20);
                Assert.All(PillarPlanner.InnerPillars(nodes[i],nodes[i+1],null,80),n=>Assert.True(Clear(n)));
            }
            Assert.Null(BridgeSupportSearch.Find(160,20,80,null,Clear,maxNodes:1));
        }
    }
}
