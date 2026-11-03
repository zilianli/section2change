using System;
using System.Linq;
using InterchangeMod.Generation.Endpoints;
using InterchangeMod.Generation.Geometry;
using InterchangeMod.Generation.Roads;
using Xunit;
using Xunit.Abstractions;

namespace InterchangeMod.Generation.Tests
{
    public class EndpointNewTypeIntegrationTests
    {
        private readonly ITestOutputHelper output;
        public EndpointNewTypeIntegrationTests(ITestOutputHelper output){this.output=output;}
        [Theory]
        [InlineData(InterchangeType.Stack,false,false,.16f)]
        [InlineData(InterchangeType.Stack,true,false,.16f)]
        [InlineData(InterchangeType.Turbine,false,false,.16f)]
        [InlineData(InterchangeType.Stack,false,true,.27f)]
        [InlineData(InterchangeType.Stack,true,true,.27f)]
        [InlineData(InterchangeType.Turbine,false,true,.27f)]
        public void AdaptsActualLoggedTangentsAndReducedRoadSupportMetadata(InterchangeType type,bool leftExit,bool mixed,float grade)
        {
            var arms=MixedApproachTests.LoggedArms(mixed);
            var traces=arms.SelectMany(a=>a.Endpoints).Select(p=>new RoadApproachTrace(p,new[]{new RoadTracePiece(
                p.NodeIndex+1000,1,Bezier3.Line(p.Position,p.Position+Vec3.Normalize(p.OutwardTangent)*1200),false,p.NodeIndex+2000,1)})).ToArray();
            var ramp=new RoadInfo("Ramp",true,1,0,8){PillarWidth=4,ElevatedLength=80};
            var design=new CloverleafRoadDesign(Enumerable.Range(0,4).Select(i=>new EndpointLaneBalance(i,"Reduced",16,3,1)),
                bridgeRules:Enumerable.Range(0,4).Select(i=>new EndpointBridgeRule(i,80,null,20,pillarWidth:4)),
                supportRules:new[]{new EndpointPrefabSupportRule("Full",80,null,4),new EndpointPrefabSupportRule("Reduced",80,null,4),
                    new EndpointPrefabSupportRule("Ramp",80,null,4)});
            var levels=EndpointLevels.Resolve(arms,true,0,0,478.63f);
            var result=EndpointStackSearch.Search(arms,traces,type,true,levels.Main,levels.Cross,ramp,grade,1000,design,leftExit);
            Assert.True(result.Failure==RetreatSearchFailure.None,$"{type}, left={leftExit}: {result.Failure} {result.ClearanceDetail} {result.SupportDetail}");
            output.WriteLine($"{type}, left={leftExit}, mixed={mixed}, grade={grade}, attempts={result.Attempts}, retreat={string.Join("/",result.Distances)}");
            Assert.Contains(result.Spatial!.Segments,s=>s.Prefab=="Reduced");
            Assert.Equal(CurveClearanceResult.Clear,EndpointNetworkSupports.Check(result.Spatial,design.BridgeRules,ramp,out _,design.SupportRules));
            foreach(var port in result.Arms.SelectMany(a=>a.Endpoints))
            {
                var segment=Assert.Single(result.Spatial.Segments.Where(s=>port.TravelOutward?s.EndNode==port.Position:s.StartNode==port.Position));
                var tangent=Vec3.Normalize(segment.WorldCurve.Derivative(port.TravelOutward?1:0));
                Assert.True(Vec3.Distance(tangent,Vec3.Normalize(port.OutwardTangent)*(port.TravelOutward?1:-1))<.001f);
            }
            Assert.False(result.Spatial.IsPlacementReady);
        }
    }
}
