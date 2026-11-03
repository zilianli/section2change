using System;
using System.Linq;
using InterchangeMod.Generation.Endpoints;
using InterchangeMod.Generation.Geometry;
using Xunit;
using Xunit.Abstractions;

namespace InterchangeMod.Generation.Tests
{
    public class GradeCompactnessTests
    {
        private readonly ITestOutputHelper output;
        public GradeCompactnessTests(ITestOutputHelper output) { this.output=output; }

        private static CloverleafRoadDesign Design(bool smoothDirect=true) => new CloverleafRoadDesign(
            Enumerable.Range(0,4).Select(i=>new EndpointLaneBalance(i,"Reduced",16,3,1)),
            smoothDirect:smoothDirect,
            bridgeRules:Enumerable.Range(0,4).Select(i=>new EndpointBridgeRule(i,80,null,20)));

        private static RoadApproachTrace[] Traces(EndpointArm[] arms) => arms.SelectMany(a=>a.Endpoints)
            .Select(p=>new RoadApproachTrace(p,new[]{new RoadTracePiece(p.NodeIndex+1000,1,
                Bezier3.Line(p.Position,p.Position+Vec3.Normalize(p.OutwardTangent)*600),false,p.NodeIndex+2000,1)})).ToArray();

        private static readonly Lazy<EndpointArm[]> FixedRoads=new Lazy<EndpointArm[]>(()=>
        {
            var arms=MixedApproachTests.LoggedArms(false);
            var selected=RetreatCloverleafSearch.Search(arms,Traces(arms),true,488.63f,498.63f,8,.16f,500,design:Design());
            if(selected.Failure!=RetreatSearchFailure.None)throw new InvalidOperationException("The logged-road fixture must have valid cuts.");
            // Freeze the valid original-road cuts, shared by both grades.
            // Search is not allowed to move these endpoints again in the local-refinement tests.
            return selected.Arms.ToArray();
        });

        private static EndpointArm[] FixedLoggedRoads()=>FixedRoads.Value;

        private static float DirectLength(SpatialCandidateResult spatial) => spatial.Segments
            .Where(s=>s.Kind==CloverEdgeKind.DirectRamp).Sum(s=>s.WorldCurve.Length(128));

        private static (SpatialCandidateResult Spatial,int Attempts) FirstPassingAtFixedRoads(EndpointArm[] arms,float grade,bool smoothDirect=true)
        {
            // Characterize the existing compact grid's first valid shape independently of
            // Search's return policy, so returning that first shape is caught as a regression.
            int attempts=0;
            for(int phase=0;phase<(smoothDirect?2:1);phase++)
                foreach(float loop in new[]{30f,40f,50f,60f,80f})
                foreach(float direct in phase==0?new[]{80f,100f,120f}:new[]{80f,100f,120f,140f,160f})
                    foreach(float middle in phase==0?new[]{0f,60f,120f,180f}:new[]{0f,60f,120f,180f,240f,300f,360f,480f,600f})
                    {
                        if(phase==1&&direct<=120&&middle<=180)continue;
                        attempts++;
                        var planar=PlanarCloverleafCandidate.Create(arms,true,loop,direct,rampWidth:8,directMiddleLength:middle,design:Design(smoothDirect));
                        if(planar.Failure!=PlanarCandidateFailure.None)continue;
                        var spatial=SpatialCloverleafCandidate.Create(arms,planar,488.63f,498.63f,0,grade);
                        if(spatial.Failure!=SpatialCandidateFailure.None)continue;
                        if(CloverleafRoadClearance.Check(spatial,planar,8,8.5f,out _)!=CurveClearanceResult.Clear)continue;
                        return (spatial,attempts);
                    }
            throw new InvalidOperationException("The fixed-road fixture must have a valid compact-grid baseline.");
        }

        [Theory]
        [InlineData(.16f,true)]
        [InlineData(.27f,false)]
        public void RefinementKeepsRoadCutsAndLoopsAndOnlyAcceptsShorterValidDirectRamps(float grade,bool hasShorterLocalShape)
        {
            var arms=FixedLoggedRoads();
            var first=FirstPassingAtFixedRoads(arms,grade);
            var result=RetreatCloverleafSearch.Search(arms,Traces(arms),true,488.63f,498.63f,8,grade,0,
                maxCandidates:first.Attempts+100,design:Design());

            Assert.Equal(RetreatSearchFailure.None,result.Failure);
            output.WriteLine($"{grade:P0}: first {DirectLength(first.Spatial):F1}m; refined {DirectLength(result.Spatial!):F1}m; attempts {first.Attempts}->{result.Attempts}");
            Assert.InRange(result.Attempts,first.Attempts+1,first.Attempts+24);
            Assert.True(DirectLength(result.Spatial!)<=DirectLength(first.Spatial)+.001f);
            // At 27% the smaller local shapes conflict with other road decks,
            // so this case exercises retention of a fully checked fallback.
            if(hasShorterLocalShape)Assert.True(DirectLength(result.Spatial!)<DirectLength(first.Spatial)-1,
                $"{grade:P0}: first {DirectLength(first.Spatial):F1}m; refined {DirectLength(result.Spatial!):F1}m");
            Assert.All(result.Distances,d=>Assert.Equal(0,d));
            Assert.Equal(arms.SelectMany(a=>a.Endpoints).Select(p=>p.Position),result.Arms.SelectMany(a=>a.Endpoints).Select(p=>p.Position));
            Assert.Equal(arms.SelectMany(a=>a.Endpoints).Select(p=>p.Position),result.Cuts.Select(c=>c.Port!.Position));
            Assert.Equal(PlanarCandidateFailure.None,result.LastPlanarFailure);
            Assert.Equal(SpatialCandidateFailure.None,result.LastSpatialFailure);
            Assert.All(result.Spatial!.Segments,s=>Assert.InRange(s.WorldCurve.MaxGrade(256),0,grade+.00001f));
            var loopBefore=first.Spatial.Segments.Where(s=>s.Kind==CloverEdgeKind.LoopRamp).Select(s=>s.WorldCurve).ToArray();
            var loopAfter=result.Spatial.Segments.Where(s=>s.Kind==CloverEdgeKind.LoopRamp).Select(s=>s.WorldCurve).ToArray();
            Assert.Equal(loopBefore,loopAfter);
        }

        [Fact]
        public void HigherGradeShortensDirectRampsAtExactlyTheSameFixedRoadEndpoints()
        {
            var arms=FixedLoggedRoads();
            var low=RetreatCloverleafSearch.Search(arms,Traces(arms),true,488.63f,498.63f,8,.16f,0,design:Design());
            var high=RetreatCloverleafSearch.Search(arms,Traces(arms),true,488.63f,498.63f,8,.27f,0,design:Design());
            Assert.Equal(RetreatSearchFailure.None,low.Failure);
            Assert.Equal(RetreatSearchFailure.None,high.Failure);
            output.WriteLine($"Fixed cuts: 16% {DirectLength(low.Spatial!):F1}m; 27% {DirectLength(high.Spatial!):F1}m");
            Assert.Equal(low.Distances,high.Distances);
            Assert.All(low.Distances,d=>Assert.Equal(0,d));
            Assert.True(DirectLength(high.Spatial!)<DirectLength(low.Spatial!)-1,
                $"16%: {DirectLength(low.Spatial!):F1}m; 27%: {DirectLength(high.Spatial!):F1}m");
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(5)]
        public void LocalRefinementNeverSpendsPastBudgetAndRetainsAValidFallback(int spareAttempts)
        {
            var arms=FixedLoggedRoads();
            var first=FirstPassingAtFixedRoads(arms,.27f,false);
            int budget=first.Attempts+spareAttempts;
            var result=RetreatCloverleafSearch.Search(arms,Traces(arms),true,488.63f,498.63f,8,.27f,0,
                maxCandidates:budget,design:Design(false));
            Assert.Equal(RetreatSearchFailure.None,result.Failure);
            Assert.InRange(result.Attempts,first.Attempts,budget);
            Assert.True(DirectLength(result.Spatial!)<=DirectLength(first.Spatial)+.001f);
            Assert.Equal(PlanarCandidateFailure.None,result.LastPlanarFailure);
            Assert.Equal(SpatialCandidateFailure.None,result.LastSpatialFailure);
            if(spareAttempts==0)Assert.Equal(DirectLength(first.Spatial),DirectLength(result.Spatial!));
        }

        [Fact]
        public void HigherGradeCanUseShorterDirectRampsWithSameRoads()
        {
            var arms=MixedApproachTests.LoggedArms(false);
            var traces=arms.SelectMany(a=>a.Endpoints).Select(p=>new RoadApproachTrace(p,new[]{
                new RoadTracePiece(p.NodeIndex+1000,1,Bezier3.Line(p.Position,p.Position+Vec3.Normalize(p.OutwardTangent)*600),false,p.NodeIndex+2000,1)})).ToArray();
            var design=new CloverleafRoadDesign(Enumerable.Range(0,4).Select(i=>new EndpointLaneBalance(i,"Reduced",16,3,1)),
                bridgeRules:Enumerable.Range(0,4).Select(i=>new EndpointBridgeRule(i,80,null,20)));
            var low=RetreatCloverleafSearch.Search(arms,traces,true,488.63f,498.63f,8,.16f,500,design:design);
            var high=RetreatCloverleafSearch.Search(arms,traces,true,488.63f,498.63f,8,.27f,500,design:design);
            Assert.Equal(RetreatSearchFailure.None,low.Failure);Assert.Equal(RetreatSearchFailure.None,high.Failure);
            float Length(RetreatSearchResult r)=>r.Spatial!.Segments.Where(s=>s.Kind==CloverEdgeKind.DirectRamp).Sum(s=>s.WorldCurve.Length(128));
            Assert.True(Length(high)<Length(low)-1,$"16%: {Length(low):F1}m; 27%: {Length(high):F1}m");
        }
    }
}
