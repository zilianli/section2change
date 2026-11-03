using System;
using InterchangeMod.Generation.Endpoints;
using Xunit;

namespace InterchangeMod.Generation.Tests
{
    public class EndpointPreviewMessageTests
    {
        private static RetreatSearchResult Fail(RetreatSearchFailure f=RetreatSearchFailure.NoCandidate,int attempts=21,
            SpatialCandidateFailure spatial=SpatialCandidateFailure.InvalidGeometry,string detail="Arm 3 incoming approach: points backwards.",bool? coreMatched=null)=>
            new RetreatSearchResult(f,attempts,null,Array.Empty<EndpointArm>(),Array.Empty<RoadRetreatResult>(),Array.Empty<float>(),
                PlanarCandidateFailure.None,spatial,clearanceDetail:detail,coreMatched:coreMatched);
        [Fact]
        public void ACoreWithNoLocalWindowIsNotReportedAsAnUnsafeInternalLayout()
        {
            var message=EndpointPreviewMessages.StackFailure(Fail(attempts:0,coreMatched:true),500);
            Assert.Contains("接路",message);Assert.DoesNotContain("内部布局暂未满足",message);
        }
        [Fact]
        public void ExplainsSpaceLimitInChineseWithoutRawSolverDetails()
        {
            var message=EndpointPreviewMessages.StackFailure(Fail(),500);
            Assert.Contains("500",message);Assert.Contains("1000",message);
            Assert.DoesNotContain("Arm",message);Assert.DoesNotContain("points backwards",message);
            Assert.Contains("接路",message);
        }
        [Fact]
        public void DoesNotSuggestExceedingTheHardLimit()
        {
            var message=EndpointPreviewMessages.StackFailure(Fail(),1000);
            Assert.Contains("1000",message);Assert.Contains("端点",message);
            Assert.DoesNotContain("2000",message);
        }
        [Theory]
        [InlineData(SpatialCandidateFailure.GradeExceeded,"坡度")]
        [InlineData(SpatialCandidateFailure.PillarConflict,"桥墩")]
        [InlineData(SpatialCandidateFailure.None,"净空")]
        public void IdentifiesTheConstraintInsteadOfShowingInternalCurveData(SpatialCandidateFailure failure,string expected)
        {
            var message=EndpointPreviewMessages.StackFailure(Fail(spatial:failure,detail:"LeftCrossA world curve (4000,500,-5000)"),500);
            Assert.Contains(expected,message);Assert.DoesNotContain("LeftCrossA",message);Assert.DoesNotContain("4000",message);
        }
        [Fact]
        public void CoreAndBudgetFailuresHaveActionableMessages()
        {
            Assert.Contains("内部布局",EndpointPreviewMessages.StackFailure(Fail(attempts:0),500));
            Assert.Contains("次数",EndpointPreviewMessages.StackFailure(Fail(f:RetreatSearchFailure.BudgetExceeded),500));
            Assert.Contains("重新选点",EndpointPreviewMessages.StackFailure(Fail(f:RetreatSearchFailure.InvalidInput),500));
        }
    }
}
