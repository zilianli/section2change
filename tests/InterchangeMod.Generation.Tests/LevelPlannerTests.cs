using Xunit;

namespace InterchangeMod.Generation.Tests
{
    public class LevelPlannerTests
    {
        private static LegSpec Leg(HeightRange ground, HeightRange elevated) =>
            new LegSpec(0f, 0) { GroundHeight = ground, ElevatedHeight = elevated };

        [Fact]
        public void AutoGap_FromGround_LiftsUpperToBridgeHeight()
        {
            // 基层 0 米：7.5 米会是路堤（挡土墙），自动提高到 8.5 米成为高架桥
            var leg = new LegSpec(0f, 0);
            Assert.Equal(8.5f, LevelPlanner.ResolveGap(leg, leg, 0f, manualGap: 0f));
        }

        [Fact]
        public void AutoGap_FromRaisedBase_UsesMeasuredMinimum()
        {
            // 基层 5 米：上层 12.5 米已是高架桥，按实测最小 7.5 米
            var leg = new LegSpec(0f, 0);
            Assert.Equal(7.5f, LevelPlanner.ResolveGap(leg, leg, 5f, manualGap: 0f));
        }

        [Theory]
        [InlineData(-13f, 13f)]
        [InlineData(-20f, 20f)]
        public void AutoGap_FromTunnel_PutsUpperAtGround(float baseElevation, float expectedGap)
        {
            // 隧道不和地面道路碰撞：上层直接放在地面 0 米
            var leg = new LegSpec(0f, 0);
            Assert.Equal(expectedGap, LevelPlanner.ResolveGap(leg, leg, baseElevation, manualGap: 0f));
        }

        [Fact]
        public void AutoGap_UsesUpperRoadsElevatedRange()
        {
            // 下层在 2 米（地面范围，净空顶 6），上层估计在 9.5 米已是高架桥（结构底 -3）→ 9 米
            var lower = Leg(new HeightRange(-1f, 6f), new HeightRange(-3f, 6f));
            var upper = Leg(new HeightRange(-1f, 6f), new HeightRange(-3f, 6f));
            Assert.Equal(9f, LevelPlanner.RequiredGap(lower, upper, 2f));
        }

        [Fact]
        public void ManualGap_IsKeptEvenWhenTooSmall()
        {
            var leg = new LegSpec(0f, 0);
            Assert.Equal(5f, LevelPlanner.ResolveGap(leg, leg, 0f, manualGap: 5f));
        }

        [Theory]
        [InlineData(10f, ElevationBand.Elevated)]
        [InlineData(8.5f, ElevationBand.Elevated)]
        [InlineData(7.5f, ElevationBand.Raised)]   // 游戏内实测：7.5 米建成带挡土墙的路堤
        [InlineData(4f, ElevationBand.Raised)]
        [InlineData(2f, ElevationBand.Ground)]
        [InlineData(0f, ElevationBand.Ground)]
        [InlineData(-2f, ElevationBand.Ground)]
        [InlineData(-6f, ElevationBand.Cutting)]
        [InlineData(-12.5f, ElevationBand.Cutting)]
        [InlineData(-13f, ElevationBand.Tunnel)]
        public void BandOf_MatchesGameThresholds(float elevation, ElevationBand expected)
        {
            Assert.Equal(expected, LevelPlanner.BandOf(elevation));
        }
    }
}
