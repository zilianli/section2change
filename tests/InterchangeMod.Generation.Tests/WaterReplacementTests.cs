using InterchangeMod.Generation.Geometry;
using Xunit;

namespace InterchangeMod.Generation.Tests
{
    public class WaterReplacementTests
    {
        [Fact]
        public void BridgeOverDeepWater_UsesBothRealTerrainAndWaterClearance()
        {
            var audit = new SurfaceAudit();
            audit.Add(60, -100, 150, 2);
            Assert.Equal(160f, audit.MinTerrainElevation);
            Assert.Equal(10f, audit.MinWaterClearance);
            Assert.Equal(0, audit.UnderWaterSamples);
            Assert.False(audit.Mixed);
        }

        [Fact]
        public void ShorelineReplacement_IsMixedAndCannotAssumeEveryPartIsABridge()
        {
            var audit = new SurfaceAudit();
            audit.Add(60, 20, 30, 2); // water surface 50
            audit.Add(60, 58, 0, 2); // dry approach
            Assert.True(audit.Mixed);
            Assert.Equal(2f, audit.MinTerrainElevation);
            Assert.Equal(40f, audit.MaxTerrainElevation);
            Assert.Equal(10f, audit.MinWaterClearance);
            Assert.Equal(0, audit.UnderWaterSamples);
        }

        [Fact]
        public void RoadAboveRiverbedButBelowWater_IsNotAValidWaterBridge()
        {
            var audit = new SurfaceAudit();
            audit.Add(40, 20, 30, 2);
            Assert.Equal(20f, audit.MinTerrainElevation);
            Assert.Equal(-10f, audit.MinWaterClearance);
            Assert.Equal(1, audit.UnderWaterSamples);
        }

        [Theory]
        [InlineData(51f, 1)]
        [InlineData(52f, 0)]
        [InlineData(60f, 0)]
        public void WaterMinimumComesFromTheRoadPrefab(float height, int invalid)
        {
            var audit = new SurfaceAudit();
            audit.Add(height, 20, 30, 2);
            Assert.Equal(invalid, audit.UnderWaterSamples);
        }

        [Theory]
        [InlineData(0f)]
        [InlineData(0.01f)]
        public void DryGroundOrNumericalWaterFilm_DoesNotTriggerWaterMinimum(float depth)
        {
            var audit = new SurfaceAudit();
            audit.Add(50, 50, depth, 2);
            Assert.Equal(0, audit.WetSamples);
            Assert.Equal(0, audit.UnderWaterSamples);
        }

        [Theory]
        [InlineData(0f, true)]
        [InlineData(20f, false)]
        public void LongOldWaterRoad_CollisionFilteringAndCuttingUseTheSameLocalHeight(float oldHeight, bool cut)
        {
            var corridor = Footprint.Corridor(new[] {
                (Bezier3.Line(new Vec3(8, 0, -100), new Vec3(8, 0, 100)), 2f)
            }, 1f);
            var road = Bezier3.Line(new Vec3(0, oldHeight, 0), new Vec3(1024, oldHeight, 0));
            Assert.Equal(cut ? 2 : 1, corridor.OutsideCollisionIntervals(road, 2f).Count);
        }
    }
}
