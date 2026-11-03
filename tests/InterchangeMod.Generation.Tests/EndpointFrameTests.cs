using System;
using System.Collections.Generic;
using System.Linq;
using InterchangeMod.Generation.Endpoints;
using InterchangeMod.Generation.Geometry;
using Xunit;

namespace InterchangeMod.Generation.Tests
{
    public class EndpointFrameTests
    {
        private static EndpointArm TwoWay(int index, Vec3 position, Vec3 outward) => new EndpointArm(index,
            new[] { new SelectedEndpoint(index + 1, 1, position, outward, false, false, 20, 2, "Synthetic TwoWay") });

        [Fact]
        public void FitsActualLilacAssetEndpointPositionsWithSyntheticRoadData()
        {
            // Positions are from asset04 .Prefab m_BezierCurve endpoints; direction,
            // traffic and road metadata here are synthetic rather than asset claims.
            EndpointArm Dual(int index, int a, int b, Vec3 p, Vec3 q, Vec3 dir) => new EndpointArm(index, new[]
            {
                new SelectedEndpoint(a, 1, p, dir, true, false, 12, 2, "Synthetic OneWay"),
                new SelectedEndpoint(b, 1, q, dir, true, true, 12, 2, "Synthetic OneWay"),
            });
            var arms = new[]
            {
                Dual(0, 39, 45, new Vec3(-12,10,252), new Vec3(12,10,252), Vec3.UnitZ),
                Dual(1, 63, 68, new Vec3(252,10,-12), new Vec3(252,10,12), Vec3.UnitX),
                Dual(2, 62, 66, new Vec3(-12,10,-252), new Vec3(12,10,-252), -Vec3.UnitZ),
                Dual(3, 41, 55, new Vec3(-252,10,-12), new Vec3(-252,10,12), -Vec3.UnitX),
            };
            var frame = EndpointFrame.Estimate(arms);
            Assert.Equal(EndpointFrameFailure.None, frame.Failure);
            Assert.NotNull(frame.Center);
            Assert.True(Vec3.Distance(frame.Center!.Value, Vec3.Zero) < 0.001f);
            Assert.InRange(frame.ResidualRms, 0, 0.001f);
            foreach (float distance in frame.AlongByArm) Assert.InRange(distance, 251.999f, 252.001f);
        }

        private static EndpointArm[] OffsetArms() => new[]
        {
            TwoWay(0, new Vec3(0, 1, 100), Vec3.UnitZ),
            TwoWay(1, new Vec3(100, 2, 0), Vec3.UnitX),
            TwoWay(2, new Vec3(10, 3, -100), -Vec3.UnitZ),
            TwoWay(3, new Vec3(-100, 4, 20), -Vec3.UnitX),
        };

        [Fact]
        public void RejectsFiniteCoordinatesWhoseProjectedDistanceCannotBeRepresented()
        {
            float m = float.MaxValue;
            var arms = new[] {
                TwoWay(0, new Vec3(m,0,m), new Vec3(1,0,1)),
                TwoWay(1, new Vec3(m,0,-m), new Vec3(1,0,-1)),
                TwoWay(2, new Vec3(-m,0,-m), new Vec3(-1,0,-1)),
                TwoWay(3, new Vec3(-m,0,m), new Vec3(-1,0,1)),
            };
            var frame = EndpointFrame.Estimate(arms);
            Assert.Equal(EndpointFrameFailure.InvalidSelection, frame.Failure);
            Assert.Null(frame.Center);
            Assert.Empty(frame.AlongByArm);
        }

        [Theory]
        [InlineData(InterchangeType.Trumpet)]
        [InlineData(InterchangeType.ExpresswayT)]
        [InlineData(InterchangeType.DirectionalT)]
        [InlineData(InterchangeType.SemiDirectionalT)]
        [InlineData(InterchangeType.Wye)]
        public void FitsThreeArmTypesWithRealArmCountAndUnorderedInput(InterchangeType type)
        {
            var center = new Vec3(100000, 0, -150000);
            var arms = Enumerable.Range(0, 3).Select(i => {
                var outward = type == InterchangeType.Wye ? Vec3.UnitZ.RotateY(i * 2 * MathF.PI / 3)
                    : Vec3.UnitZ.RotateY(i * MathF.PI / 2);
                return TwoWay(i, center + outward * (100 + i * 40), outward);
            }).ToArray();
            var a = EndpointFrame.Estimate(arms, type);
            var b = EndpointFrame.Estimate(arms.Reverse().ToArray(), type);
            Assert.Equal(EndpointFrameFailure.None, a.Failure);
            Assert.Equal(a.Center, b.Center);
            Assert.InRange(Vec3.Distance(center, a.Center!.Value), 0, .05f);
            Assert.Equal(3, a.AlongByArm.Count);
            Assert.InRange(a.ResidualRms, 0, .05f);
            for (int i = 0; i < 3; i++) Assert.InRange(a.AlongByArm[i], 99.95f + i*40, 100.05f+i*40);
            Assert.Equal(EndpointFrameFailure.InvalidSelection, EndpointFrame.Estimate(arms).Failure);
        }

        [Fact]
        public void UsesAllFourNonCollinearLinesAndIgnoresInputOrder()
        {
            var arms = OffsetArms();
            var a = EndpointFrame.Estimate(arms);
            var b = EndpointFrame.Estimate(arms.Reverse().ToArray());
            Assert.Equal(EndpointFrameFailure.None, a.Failure);
            Assert.True(Vec3.Distance(a.Center!.Value, new Vec3(5, 0, 10)) < 0.001f);
            Assert.InRange(a.ResidualRms, 7.905f, 7.906f);
            Assert.Equal(a.Center, b.Center);
            Assert.Equal(a.AlongByArm, b.AlongByArm);
        }

        [Fact]
        public void FailsSafelyForMissingOrParallelInput()
        {
            Assert.Equal(EndpointFrameFailure.InvalidSelection, EndpointFrame.Estimate(OffsetArms().Take(3).ToArray()).Failure);
            var arms = Enumerable.Range(0, 4).Select(i => TwoWay(i, new Vec3(i * 10, 0, i * 100), Vec3.UnitZ)).ToArray();
            var result = EndpointFrame.Estimate(arms);
            Assert.Equal(EndpointFrameFailure.ParallelAxes, result.Failure);
            Assert.Null(result.Center);

            var nearlyParallel = Enumerable.Range(0, 4).Select(i => TwoWay(i, new Vec3(i * 10, 0, i * 100),
                new Vec3(i % 2 == 0 ? 0.000001f : -0.000001f, 0, 1))).ToArray();
            Assert.Equal(EndpointFrameFailure.ParallelAxes, EndpointFrame.Estimate(nearlyParallel).Failure);
        }

        [Fact]
        public void PreservesLargeWorldTranslation()
        {
            var arms = OffsetArms();
            var shift = new Vec3(100000, 0, -150000);
            var translated = arms.Select(a => new EndpointArm(a.Index, a.Endpoints.Select(p => new SelectedEndpoint(
                p.NodeIndex, p.NodeVersion, p.Position + shift, p.OutwardTangent, p.OneWay, p.TravelOutward,
                p.Width, p.Lanes, p.Prefab)))).ToArray();
            var result = EndpointFrame.Estimate(translated);
            Assert.Equal(EndpointFrameFailure.None, result.Failure);
            Assert.True(Vec3.Distance(result.Center!.Value, new Vec3(100005, 0, -149990)) < 0.1f);
        }
    }
}
