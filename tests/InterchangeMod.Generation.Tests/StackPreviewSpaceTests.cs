using System;
using System.Linq;
using InterchangeMod.Generation.Endpoints;
using InterchangeMod.Generation.Geometry;
using InterchangeMod.Generation.Roads;
using Xunit;

namespace InterchangeMod.Generation.Tests
{
    public class StackPreviewSpaceTests
    {
        private static readonly RoadInfo Ramp=new RoadInfo("Ramp",true,1,0,8){ElevatedLength=80,PillarWidth=4};
        private static CloverleafRoadDesign Design()=>new CloverleafRoadDesign(
            Enumerable.Range(0,4).Select(i=>new EndpointLaneBalance(i,"Reduced",16,3,1)),
            bridgeRules:Enumerable.Range(0,4).Select(i=>new EndpointBridgeRule(i,80,null,20,pillarWidth:4)),
            supportRules:new[]{new EndpointPrefabSupportRule("Full",80,null,4),new EndpointPrefabSupportRule("Reduced",80,null,4),
                new EndpointPrefabSupportRule("Ramp",80,null,4)});
        [Theory]
        [InlineData(InterchangeType.Stack,false)][InlineData(InterchangeType.Stack,true)][InlineData(InterchangeType.Turbine,false)]
        public void CoreEndsAtProtectedMainlineInsteadOfDuplicatingExternalAdapters(InterchangeType type,bool leftExit)
        {
            var core=EndpointStackCore.Create(MixedApproachTests.LoggedArms(false),type,true,488.632f,498.632f,Ramp,.16f,Design(),leftExit);
            Assert.True(core.Success,core.Diagnostic);
            foreach(var path in core.Segments.Where(s=>s.Kind==CloverEdgeKind.Main).GroupBy(s=>s.PathId))
            {
                var first=path.First();var direction=Vec3.Normalize(first.WorldCurve.Derivative(0));
                var exit=core.Segments.Where(s=>s.Kind!=CloverEdgeKind.Main&&s.SourceArm==first.SourceArm)
                    .Select(s=>s.StartNode).Where(n=>path.Any(main=>main.StartNode==n||main.EndNode==n))
                    .OrderBy(n=>Vec3.Distance(first.StartNode,n)).First();
                // The legacy solver shares one family end across both carriageways,
                // so the less constrained direction can retain more than the 20m minimum.
                Assert.True(Vec3.Distance(first.StartNode,exit)>=19.98f);
                foreach(var piece in path)
                {
                    var c=piece.WorldCurve;
                    Assert.True(Vec3.Distance(Vec3.Normalize(c.Derivative(0)),direction)<.001f);
                    Assert.True(Vec3.Distance(Vec3.Normalize(c.Derivative(1)),direction)<.001f);
                }
            }
        }
        [Fact]
        public void StackCanUseItsFiveHundredMetreLimitWithoutRedundantExternalGeometry()
        {
            var arms=MixedApproachTests.LoggedArms(false);
            var traces=arms.SelectMany(a=>a.Endpoints).Select(p=>new RoadApproachTrace(p,new[]{new RoadTracePiece(
                p.NodeIndex+1000,1,Bezier3.Line(p.Position,p.Position+Vec3.Normalize(p.OutwardTangent)*1100),false,p.NodeIndex+2000,1)})).ToArray();
            var r=EndpointStackSearch.Search(arms,traces,InterchangeType.Stack,true,488.632f,498.632f,Ramp,.16f,500,Design());
            Assert.True(r.Failure==RetreatSearchFailure.None,r.ClearanceDetail);
            Assert.All(r.Distances,d=>Assert.InRange(d,0,500));
            Assert.Equal(CurveClearanceResult.Clear,EndpointNetworkClearance.Check(r.Spatial!,8.5f,out _));
            Assert.Equal(CurveClearanceResult.Clear,EndpointNetworkSupports.Check(r.Spatial!,Design().BridgeRules,Ramp,out _,Design().SupportRules));
        }
    }
}
