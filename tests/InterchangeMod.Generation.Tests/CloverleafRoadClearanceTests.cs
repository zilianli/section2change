using System.Linq;
using InterchangeMod.Generation.Endpoints;
using Xunit;

namespace InterchangeMod.Generation.Tests
{
    public class CloverleafRoadClearanceTests
    {
        private static EndpointArm[] WideArms(bool rightHandTraffic)=>EndpointFixtures.DualCross(400,rightHandTraffic)
            .Select(a=>new EndpointArm(a.Index,a.Endpoints.Select(p=>new SelectedEndpoint(p.NodeIndex,p.NodeVersion,
                p.Position,p.OutwardTangent,p.OneWay,p.TravelOutward,20,4,p.Prefab)))).ToArray();

        [Theory][InlineData(true)][InlineData(false)]
        public void RejectsCompactRampCrossingEvenWhenGradesPass(bool rightHandTraffic)
        {
            var arms=WideArms(rightHandTraffic);
            var plan=PlanarCloverleafCandidate.Create(arms,rightHandTraffic,40,100,rampWidth:8,directMiddleLength:60);
            Assert.Equal(PlanarCandidateFailure.None,plan.Failure);
            var spatial=SpatialCloverleafCandidate.Create(arms,plan,10,22,0,.30f);
            Assert.Equal(SpatialCandidateFailure.None,spatial.Failure);
            Assert.Equal(CurveClearanceResult.Conflict,CloverleafRoadClearance.Check(spatial,plan,8,8.5f,out var detail));
            Assert.Contains("Ramp",detail);
        }

        [Theory][InlineData(true)][InlineData(false)]
        public void AllowsSeparatedRampsAndLocalMergesButRejectsLowMainCrossing(bool rightHandTraffic)
        {
            var arms=WideArms(rightHandTraffic);
            var plan=PlanarCloverleafCandidate.Create(arms,rightHandTraffic,40,100,rampWidth:8,directMiddleLength:180);
            Assert.Equal(PlanarCandidateFailure.None,plan.Failure);
            var clear=SpatialCloverleafCandidate.Create(arms,plan,10,22,0,.30f);
            Assert.Equal(SpatialCandidateFailure.None,clear.Failure);
            Assert.Equal(CurveClearanceResult.Clear,CloverleafRoadClearance.Check(clear,plan,8,8.5f,out var detail));
            var low=SpatialCloverleafCandidate.Create(arms,plan,10,12,0,.30f);
            Assert.Equal(SpatialCandidateFailure.None,low.Failure);
            Assert.Equal(CurveClearanceResult.Conflict,CloverleafRoadClearance.Check(low,plan,8,8.5f,out detail));
            Assert.Contains("Main",detail);
            Assert.Equal(CurveClearanceResult.InvalidInput,CloverleafRoadClearance.Check(clear,plan,float.NaN,8.5f,out _));
            Assert.Equal(CurveClearanceResult.InvalidInput,CloverleafRoadClearance.Check(clear,plan,8,0,out _));
        }
    }
}
