using System.Linq;
using InterchangeMod.Generation.Endpoints;
using InterchangeMod.Generation.Roads;
using Xunit;

namespace InterchangeMod.Generation.Tests
{
    public class EndpointCoreCandidateTests
    {
        [Theory]
        [InlineData(InterchangeType.Stack,false,16)]
        [InlineData(InterchangeType.Stack,true,22)]
        [InlineData(InterchangeType.Turbine,false,12)]
        public void ExposesLaterParameterAlternativesWithoutReturningUncheckedGeometry(InterchangeType type,bool left,int expected)
        {
            var arms=MixedApproachTests.LoggedArms(false);
            var ramp=new RoadInfo("Ramp",true,1,0,8){ElevatedLength=80,PillarWidth=4};
            var design=new CloverleafRoadDesign();
            var options=EndpointStackCore.Candidates(arms,type,true,488.632f,498.632f,ramp,.16f,design,left).ToArray();
            Assert.Equal(expected,options.Length);
            Assert.Contains(options,o=>o.Success);
            foreach(var option in options)
            {
                if(!option.Success){Assert.Empty(option.Segments);Assert.Empty(option.Ports);continue;}
                var spatial=new SpatialCandidateResult(SpatialCandidateFailure.None,option.Segments);
                Assert.Equal(CurveClearanceResult.Clear,EndpointNetworkClearance.Check(spatial,8.5f,out _));
                Assert.Equal(CurveClearanceResult.Clear,EndpointNetworkSupports.Check(spatial,design.BridgeRules,ramp,out _));
                Assert.All(option.Segments,s=>Assert.Equal(VerticalProfileFailure.None,VerticalProfile.CheckGrade(s.WorldCurve,.16f)));
            }
        }
        [Fact]
        public void InvalidGradeCannotBecomeValidByTryingASmallerFactor()
        {
            var options=EndpointStackCore.Candidates(MixedApproachTests.LoggedArms(false),InterchangeType.Stack,true,10,20,
                new RoadInfo("Ramp",true,1,0,8),.31f,new CloverleafRoadDesign()).ToArray();
            Assert.Single(options);Assert.False(options[0].Success);Assert.Empty(options[0].Segments);
        }
    }
}
