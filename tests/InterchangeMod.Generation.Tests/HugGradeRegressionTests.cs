using System;
using System.Linq;
using InterchangeMod.Generation.Endpoints;
using InterchangeMod.Generation.Geometry;
using Xunit;

namespace InterchangeMod.Generation.Tests
{
    public class HugGradeRegressionTests
    {
        private static RetreatSearchResult Search(float grade)
        {
            var arms=MixedApproachTests.LoggedArms(false);
            var traces=arms.SelectMany(a=>a.Endpoints).Select(p=>new RoadApproachTrace(p,new[]{new RoadTracePiece(
                p.NodeIndex+1000,1,Bezier3.Line(p.Position,p.Position+Vec3.Normalize(p.OutwardTangent)*600),false,p.NodeIndex+2000,1)})).ToArray();
            var design=new CloverleafRoadDesign(Enumerable.Range(0,4).Select(i=>new EndpointLaneBalance(i,"Reduced",16,3,1)),
                bridgeRules:Enumerable.Range(0,4).Select(i=>new EndpointBridgeRule(i,80,null,20)),
                hugLoops:true,sharedRampPrefab:"Shared",sharedRampWidth:12);
            return RetreatCloverleafSearch.Search(arms,traces,true,488.632f,498.632f,8,grade,500,design:design);
        }
        private static float HorizontalLength(Bezier3 c)
        {
            Vec3 Flat(Vec3 p)=>new Vec3(p.X,0,p.Z);
            return new Bezier3(Flat(c.P0),Flat(c.P1),Flat(c.P2),Flat(c.P3)).Length(128);
        }
        private static float Slope(SpatialSegment s,bool start)
        {
            var v=s.WorldCurve.Derivative(start?0:1);
            return v.Y/(float)Math.Sqrt(v.X*v.X+v.Z*v.Z);
        }

        [Theory]
        [InlineData(.16f)][InlineData(.17f)][InlineData(.18f)]
        [InlineData(.20f)][InlineData(.25f)][InlineData(.30f)]
        public void LoggedGradeSweepDoesNotCreateTenMetrePiecesBesideSharedSplits(float grade)
        {
            var result=Search(grade);
            Assert.True(result.Failure==RetreatSearchFailure.None,$"{result.Failure}: {result.LastPlanarFailure}/{result.LastSpatialFailure} {result.ClearanceDetail}");
            var routes=result.Spatial!.Segments.Where(s=>s.Kind!=CloverEdgeKind.Main).GroupBy(s=>s.PathId).ToArray();
            foreach(var route in routes)
                foreach(var piece in new[]{route.First(),route.Last()})
                    Assert.True(HorizontalLength(piece.WorldCurve)>=24-.01f,
                        $"grade={grade}, {piece.PathId}, terminal={HorizontalLength(piece.WorldCurve):F3}m");
            Assert.All(result.Spatial.Segments,s=>Assert.InRange(s.WorldCurve.MaxGrade(256),0,grade+.00002f));
            // An end-grade match must not introduce an overshoot elsewhere on a branch.
            foreach(var route in routes)
            {
                float delta=route.Last().WorldCurve.P3.Y-route.First().WorldCurve.P0.Y;
                foreach(var piece in route)
                    for(int n=0;n<=32;n++)
                        Assert.True(piece.WorldCurve.Derivative(n/32f).Y*Math.Sign(delta)>=-.001f,
                            $"{piece.PathId} reverses slope at grade={grade}");
            }
        }

        [Fact]
        public void InternalSharedSplitsHaveContinuousNonzeroGradeInsteadOfFlatPlatforms()
        {
            var result=Search(.30f);
            Assert.Equal(RetreatSearchFailure.None,result.Failure);
            var routes=result.Spatial!.Segments.Where(s=>s.Kind!=CloverEdgeKind.Main).GroupBy(s=>s.PathId).ToArray();
            foreach(var shared in routes.Where(g=>g.First().Kind==CloverEdgeKind.SharedRamp))
            {
                var a=shared.First();var b=shared.Last();
                var intoA=routes.Single(g=>g.First().Kind==CloverEdgeKind.DirectRamp&&g.Last().EndNode==a.StartNode).Last();
                var fromA=routes.Single(g=>g.First().Kind==CloverEdgeKind.LoopRamp&&g.First().StartNode==a.StartNode).First();
                var fromB=routes.Single(g=>g.First().Kind==CloverEdgeKind.DirectRamp&&g.First().StartNode==b.EndNode).First();
                var intoB=routes.Single(g=>g.First().Kind==CloverEdgeKind.LoopRamp&&g.Last().EndNode==b.EndNode).Last();
                Assert.True(Math.Abs(Slope(a,true))>.005f,$"{a.PathId} still has a flat split platform");
                Assert.True(Math.Abs(Slope(b,false))>.005f,$"{b.PathId} still has a flat split platform");
                Assert.InRange(Math.Abs(Slope(a,true)-Slope(intoA,false)),0,.0002f);
                Assert.InRange(Math.Abs(Slope(a,true)+Slope(fromA,true)),0,.0002f);
                Assert.InRange(Math.Abs(Slope(b,false)-Slope(fromB,true)),0,.0002f);
                Assert.InRange(Math.Abs(Slope(b,false)+Slope(intoB,false)),0,.0002f);
                foreach(var piece in new[]{a,b,intoA,fromA,fromB,intoB})
                    Assert.True(Math.Abs(piece.WorldCurve.P3.Y-piece.WorldCurve.P0.Y)>.01f,$"{piece.PathId} still has a finite flat platform");
            }
        }
    }
}
