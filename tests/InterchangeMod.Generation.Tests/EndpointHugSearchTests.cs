using System;
using System.Linq;
using InterchangeMod.Generation.Endpoints;
using InterchangeMod.Generation.Geometry;
using InterchangeMod.Generation.Roads;
using Xunit;
using Xunit.Abstractions;

namespace InterchangeMod.Generation.Tests
{
    public class EndpointHugSearchTests
    {
        private readonly ITestOutputHelper output;
        public EndpointHugSearchTests(ITestOutputHelper output){this.output=output;}
        [Theory]
        [InlineData(false,true,.16f)][InlineData(false,false,.27f)]
        [InlineData(true,true,.16f)][InlineData(true,true,.27f)]
        public void SearchesSharedVariantWithRealRetreatAndBridgeAndGradeChecks(bool mixed,bool rht,float grade)
        {
            // The logged selection is real; the road continuation is synthetic.
            var arms=mixed?MixedApproachTests.LoggedArms(true):EndpointFixtures.DualCross(180,rht)
                .Select(a=>new EndpointArm(a.Index,a.Endpoints.Select(p=>new SelectedEndpoint(p.NodeIndex,p.NodeVersion,
                    p.Position,p.OutwardTangent,true,p.TravelOutward,20,4,"Full")))).ToArray();
            var traces=arms.SelectMany(a=>a.Endpoints).Select(p=>new RoadApproachTrace(p,new[]{new RoadTracePiece(
                p.NodeIndex+1000,1,Bezier3.Line(p.Position,p.Position+Vec3.Normalize(p.OutwardTangent)*600),false,p.NodeIndex+2000,1)})).ToArray();
            var design=new CloverleafRoadDesign(Enumerable.Range(0,4).Select(i=>new EndpointLaneBalance(i,"Reduced",16,3,1)),
                bridgeRules:Enumerable.Range(0,4).Select(i=>new EndpointBridgeRule(i,80,null,20)),
                hugLoops:true,sharedRampPrefab:"Shared",sharedRampWidth:12);
            var levels=mixed?EndpointLevels.Resolve(arms,true,0,0,488.63f):EndpointLevels.Resolve(arms,true,0,0,0);
            var result=RetreatCloverleafSearch.Search(arms,traces,rht,levels.Main,levels.Cross,8,grade,500,design:design);
            Assert.True(result.Failure==RetreatSearchFailure.None,$"{result.Failure} {result.Attempts} {result.LastPlanarFailure}/{result.LastSpatialFailure}: {result.ClearanceDetail} {result.SupportDetail}");
            output.WriteLine($"mixed={mixed}, rht={rht}, grade={grade}, attempts={result.Attempts}, retreat={string.Join("/",result.Distances)}");
            Assert.All(result.Spatial!.Segments,s=>Assert.InRange(s.WorldCurve.MaxGrade(256),0,grade+.00001f));
            Assert.Equal(4,result.Spatial.Segments.Where(s=>s.Kind==CloverEdgeKind.SharedRamp).Select(s=>s.PathId).Distinct().Count());
            Assert.All(result.Spatial.Segments.Where(s=>s.Kind==CloverEdgeKind.SharedRamp),s=>Assert.Equal("Shared",s.Prefab));
            Assert.Contains(result.Spatial.Segments,s=>s.Prefab=="Reduced");
            foreach(var port in result.Arms.SelectMany(a=>a.Endpoints))
            {
                var edge=Assert.Single(result.Spatial.Segments.Where(s=>port.TravelOutward?s.EndNode==port.Position:s.StartNode==port.Position));
                var actual=Vec3.Normalize(edge.WorldCurve.Derivative(port.TravelOutward?1:0));
                var expected=Vec3.Normalize(port.OutwardTangent)*(port.TravelOutward?1:-1);
                Assert.True(Vec3.Distance(actual,expected)<.001f);
            }
            Assert.False(result.Spatial.IsPlacementReady);
        }

        [Fact]
        public void ClearanceRejectsSeparatePhysicalPathsEvenWhenTheyShareSourceAndKind()
        {
            var arms=EndpointFixtures.DualCross(600);
            var design=new CloverleafRoadDesign(hugLoops:true,sharedRampPrefab:"Shared",sharedRampWidth:12);
            var plan=PlanarCloverleafCandidate.Create(arms,true,80,120,rampWidth:8,design:design);
            Assert.Equal(PlanarCandidateFailure.None,plan.Failure);
            var spatial=SpatialCloverleafCandidate.Create(arms,plan,10,20,0,.30f);
            Assert.Equal(SpatialCandidateFailure.None,spatial.Failure);
            Assert.Equal(CurveClearanceResult.Clear,CloverleafRoadClearance.Check(spatial,plan,8,8.5f,out _));
            var paths=spatial.Segments.Where(s=>s.Kind==CloverEdgeKind.LoopRamp&&s.SourceArm==0).GroupBy(s=>s.PathId).ToArray();
            Assert.Equal(2,paths.Length);
            var target=paths[0].Skip(1).First().WorldCurve;
            var moving=paths[1].Skip(1).First();
            var p=target.Evaluate(.5f);var d=target.Derivative(.5f);var side=Vec3.Normalize(new Vec3(d.Z,0,-d.X))*20;
            var crossing=Bezier3.Line(p-side,p+side);
            var replacement=new SpatialSegment(moving.SourceArm,moving.Kind,crossing,prefab:moving.Prefab,
                roadWidth:moving.RoadWidth,pathId:moving.PathId);
            var bad=new SpatialCandidateResult(SpatialCandidateFailure.None,spatial.Segments.Select(s=>ReferenceEquals(s,moving)?replacement:s));
            Assert.Equal(CurveClearanceResult.Conflict,CloverleafRoadClearance.Check(bad,plan,8,8.5f,out _));
        }

        [Fact]
        public void SharedRoadRequiresExactBothDirectionsAndPrefersSameRoadRules()
        {
            var ramp=new RoadInfo("Ramp",true,1,0,8);
            var candidates=new[]{new RoadInfo("Asymmetric",true,1,2,12),new RoadInfo("Street",false,1,1,10),
                new RoadInfo("Highway",true,1,1,12),new RoadInfo("Bus",true,1,1,10,1),new RoadInfo("Invalid",true,1,1,float.NaN)};
            Assert.Equal("Highway",CloverleafRoadDesign.SelectSharedRamp(candidates,ramp)!.Name);
            Assert.Null(CloverleafRoadDesign.SelectSharedRamp(candidates.Where(r=>r.Name=="Asymmetric").ToArray(),ramp));
        }
    }
}
