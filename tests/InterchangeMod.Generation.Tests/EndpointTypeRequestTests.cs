using System;
using System.Linq;
using InterchangeMod.Generation.Endpoints;
using InterchangeMod.Generation.Geometry;
using Xunit;

namespace InterchangeMod.Generation.Tests
{
    public class EndpointTypeRequestTests
    {
        public static System.Collections.Generic.IEnumerable<object[]> Cases() =>
            Enum.GetValues(typeof(InterchangeType)).Cast<InterchangeType>()
                .SelectMany(t => new[] { 0, 1, 2 }.Select(form => new object[] { t, form }));

        private static EndpointArm[] Arms(InterchangeType type, int form)
        {
            var arms = EndpointFixtures.DualCross().Take(EndpointTypeCatalog.Get(type).ArmCount).ToArray();
            return arms.Select(a => form == 1 || (form == 2 && a.Index % 2 == 0)
                ? new EndpointArm(a.Index, new[] { new SelectedEndpoint(100 + a.Index, 1,
                    a.Endpoints[0].Position, a.Endpoints[0].OutwardTangent, false, false, 24, 2, "TwoWay") }) : a).ToArray();
        }

        [Theory, MemberData(nameof(Cases))]
        public void AllExistingTypesAcceptValidRoadFormsAsInputOnly(InterchangeType type, int form)
        {
            var contract = EndpointTypeCatalog.Get(type);
            var request = EndpointTypeRequest.Create(type, Arms(type, form), true, .30f, new float[contract.ArmCount]);
            Assert.Empty(request.Problems);
            Assert.True(request.InputReady);
            Assert.False(request.IsPlacementReady);
            Assert.Equal(contract.ArmCount * (contract.ArmCount - 1), contract.RequiredMovements.Count);
            Assert.Equal(contract.RequiredMovements.Count, contract.RequiredMovements.Select(m => (m.FromArm, m.ToArm)).Distinct().Count());
            Assert.All(contract.RequiredMovements, m => Assert.NotEqual(m.FromArm, m.ToArm));
        }

        [Fact]
        public void TeeChecksOnlyMainlineAndWyeHasNoArtificialOppositePair()
        {
            var arms = Arms(InterchangeType.Trumpet, 1);
            var p = arms[2].Endpoints[0];
            arms[2] = new EndpointArm(2, new[] { new SelectedEndpoint(p.NodeIndex,p.NodeVersion,p.Position,p.OutwardTangent,false,false,p.Width,3,p.Prefab) });
            Assert.Contains(EndpointConstraints.Validate(arms, InterchangeType.Trumpet), p => p.Code == EndpointProblemCode.LaneMismatch);
            Assert.Empty(EndpointConstraints.Validate(arms, InterchangeType.Wye));
            Assert.Contains(EndpointConstraints.Validate(arms), p => p.Code == EndpointProblemCode.ArmCount);
        }

        [Fact]
        public void RejectsInvalidCountDirectionAndDuplicateNode()
        {
            var arms = Arms(InterchangeType.DirectionalT, 0);
            Assert.Contains(EndpointConstraints.Validate(arms.Take(2).ToArray(), InterchangeType.DirectionalT), p => p.Code == EndpointProblemCode.ArmCount);
            arms[1] = new EndpointArm(1, arms[0].Endpoints);
            Assert.Contains(EndpointConstraints.Validate(arms, InterchangeType.DirectionalT), p => p.Code == EndpointProblemCode.DuplicateNode);
            arms[1] = new EndpointArm(1, new[] { arms[1].Endpoints[0] });
            Assert.Contains(EndpointConstraints.Validate(arms, InterchangeType.DirectionalT), p => p.Code == EndpointProblemCode.InvalidPortCount);
        }

        [Fact]
        public void FreezesSelectionAndLimitsAndRejectsInvalidRequests()
        {
            var arms = Arms(InterchangeType.Cloverleaf, 0);
            var limits = new[] { 10f, 20f, 30f, 40f };
            var request = EndpointTypeRequest.Create(InterchangeType.Cloverleaf, arms, false, .3f, limits);
            arms[0] = arms[1]; limits[0] = 999;
            Assert.Equal(0, request.Arms[0].Index);
            Assert.Equal(10, request.RetreatLimits[0]);
            Assert.False(request.RightHandTraffic);
            foreach (float grade in new[] { 0, .3001f, float.NaN, float.PositiveInfinity })
                Assert.False(EndpointTypeRequest.Create(InterchangeType.Cloverleaf, Arms(InterchangeType.Cloverleaf,0),true,grade,new float[4]).InputReady);
            Assert.False(EndpointTypeRequest.Create(InterchangeType.Cloverleaf,Arms(InterchangeType.Cloverleaf,0),true,.3f,new[] {0f,0f,0f,float.PositiveInfinity}).InputReady);
            Assert.False(EndpointTypeRequest.Create(InterchangeType.Cloverleaf,Arms(InterchangeType.Cloverleaf,0),true,.3f,new float[3]).InputReady);
            Assert.False(EndpointTypeRequest.Create((InterchangeType)999,Arms(InterchangeType.Cloverleaf,0),true,.3f,new float[4]).InputReady);
        }
    }
}
