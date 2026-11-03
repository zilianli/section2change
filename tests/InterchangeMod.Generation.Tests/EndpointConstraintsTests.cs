using System.Collections.Generic;
using System.Linq;
using InterchangeMod.Generation.Endpoints;
using InterchangeMod.Generation.Geometry;
using Xunit;

namespace InterchangeMod.Generation.Tests
{
    public class EndpointConstraintsTests
    {
        private static Vec3 Direction(int arm) => arm switch
        {
            0 => new Vec3(0, 0, 1), 1 => new Vec3(1, 0, 0),
            2 => new Vec3(0, 0, -1), _ => new Vec3(-1, 0, 0),
        };

        private static EndpointArm Arm(int arm, bool dual = false, int incomingLanes = 2, int outgoingLanes = 2)
        {
            var direction = Direction(arm);
            var position = direction * 100;
            if (!dual)
                return new EndpointArm(arm, new[] { new SelectedEndpoint(arm * 10 + 1, 1, position, direction, false, false, 20, incomingLanes, "TwoWay") });
            return new EndpointArm(arm, new[]
            {
                new SelectedEndpoint(arm * 10 + 1, 1, position, direction, true, false, 12, incomingLanes, "OneWay"),
                new SelectedEndpoint(arm * 10 + 2, 1, position + new Vec3(2, 0, 2), direction, true, true, 12, outgoingLanes, "OneWay"),
            });
        }

        private static List<EndpointArm> Arms(bool mainDual = false, bool crossDual = false) =>
            Enumerable.Range(0, 4).Select(i => Arm(i, i % 2 == 0 ? mainDual : crossDual)).ToList();

        private static SelectedEndpoint Port(int id, bool oneWay = false, bool outward = false,
            float width = 12, int lanes = 2, string prefab = "Road", Vec3? position = null, Vec3? tangent = null) =>
            new SelectedEndpoint(id, 1, position ?? Vec3.Zero, tangent ?? Vec3.UnitZ, oneWay, outward, width, lanes, prefab);

        [Fact]
        public void ArmCopiesSourceCollection()
        {
            var endpoint = new SelectedEndpoint(10, 2, new Vec3(10, 5, 0), Vec3.UnitX, false, false, 16, 2, "Road");
            var source = new List<SelectedEndpoint> { endpoint };
            var arm = new EndpointArm(0, source);

            source.Clear();

            Assert.Single(arm.Endpoints);
            Assert.Same(endpoint, arm.Endpoints[0]);
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(true, true)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        public void FourCompleteArmsAcceptTwoWayDualAndMixed(bool mainDual, bool crossDual)
        {
            Assert.Empty(EndpointConstraints.Validate(Arms(mainDual, crossDual)));
        }

        [Fact]
        public void RejectsWrongNumberAndDuplicateArmIndices()
        {
            Assert.Contains(EndpointConstraints.Validate(Arms().Take(3).ToArray()), e => e.Code == EndpointProblemCode.ArmCount);
            var arms = Arms();
            arms[3] = Arm(2);
            Assert.Contains(EndpointConstraints.Validate(arms), e => e.Code == EndpointProblemCode.ArmIndex);
        }

        [Fact]
        public void RejectsSameNodeUsedInTwoArms()
        {
            var arms = Arms();
            arms[1] = new EndpointArm(1, new[] { arms[0].Endpoints[0] });
            Assert.Contains(EndpointConstraints.Validate(arms), e => e.Code == EndpointProblemCode.DuplicateNode);
        }

        [Fact]
        public void DistinctEntityVersionsAreDistinctIdentities()
        {
            var arms = Arms();
            arms[1] = new EndpointArm(1, new[] { new SelectedEndpoint(1, 2, Vec3.UnitX, Vec3.UnitX, false, false, 20, 2, "Road") });
            Assert.DoesNotContain(EndpointConstraints.Validate(arms), e => e.Code == EndpointProblemCode.DuplicateNode);
        }

        [Fact]
        public void RejectsIncompleteOrOverfilledArm()
        {
            foreach (var endpoints in new[] { new SelectedEndpoint[0], new[] { Port(51, true) }, new[] { Port(51), Port(52), Port(53) } })
            {
                var arms = Arms();
                arms[0] = new EndpointArm(0, endpoints);
                Assert.Contains(EndpointConstraints.Validate(arms), e => e.Code == EndpointProblemCode.InvalidPortCount);
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void RejectsSingleDirectionDualArm(bool outward)
        {
            var arms = Arms();
            arms[0] = new EndpointArm(0, new[] { Port(51, true, outward), Port(52, true, outward) });
            Assert.Contains(EndpointConstraints.Validate(arms), e => e.Code == EndpointProblemCode.InvalidDirection);
        }

        [Fact]
        public void RejectsNonFiniteGeometryAndZeroHorizontalTangent()
        {
            var invalid = new[]
            {
                Port(51, position: new Vec3(float.NaN, 0, 0)),
                Port(52, position: new Vec3(0, float.PositiveInfinity, 0)),
                Port(53, tangent: new Vec3(0, float.NegativeInfinity, 1)),
                Port(54, tangent: new Vec3(0, 1, 0)),
            };
            foreach (var endpoint in invalid)
            {
                var arms = Arms();
                arms[0] = new EndpointArm(0, new[] { endpoint });
                Assert.Contains(EndpointConstraints.Validate(arms), e => e.Code == EndpointProblemCode.InvalidGeometry);
            }
        }

        [Fact]
        public void RejectsInvalidRoadData()
        {
            foreach (var endpoint in new[] { Port(51, width: 0), Port(52, width: float.NaN), Port(53, lanes: 0), Port(54, prefab: " ") })
            {
                var arms = Arms();
                arms[0] = new EndpointArm(0, new[] { endpoint });
                Assert.Contains(EndpointConstraints.Validate(arms), e => e.Code == EndpointProblemCode.InvalidRoad);
            }
        }

        [Fact]
        public void ComparesTheOppositeArmByTrafficDirectionRatherThanPortOrder()
        {
            var arms = Arms(mainDual: true, crossDual: true);
            arms[0] = Arm(0, dual: true, incomingLanes: 3, outgoingLanes: 2);
            var opposite = Arm(2, dual: true, incomingLanes: 2, outgoingLanes: 3);
            arms[2] = new EndpointArm(2, opposite.Endpoints.Reverse());
            Assert.Empty(EndpointConstraints.Validate(arms));

            arms[2] = Arm(2, dual: true, incomingLanes: 3, outgoingLanes: 3);
            Assert.Contains(EndpointConstraints.Validate(arms), e => e.Code == EndpointProblemCode.LaneMismatch);
        }

        [Fact]
        public void TwoWayMustMatchBothOppositeOneWayDirections()
        {
            var arms = Arms();
            arms[2] = Arm(2, dual: true, incomingLanes: 2, outgoingLanes: 2);
            Assert.Empty(EndpointConstraints.Validate(arms));

            arms[2] = Arm(2, dual: true, incomingLanes: 1, outgoingLanes: 2);
            Assert.Contains(EndpointConstraints.Validate(arms), e => e.Code == EndpointProblemCode.LaneMismatch);
        }
    }
}
