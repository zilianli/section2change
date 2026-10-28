using InterchangeMod.Generation.Geometry;
using Xunit;

namespace InterchangeMod.Generation.Tests
{
    public class PhysicalElevationTests
    {
        [Theory]
        [InlineData(103f, 119f, -50f, 50f, true)]
        [InlineData(-119f, -103f, -50f, 50f, true)]
        [InlineData(2f, 20f, 0f, 50f, true)]
        [InlineData(-2f, 20f, 0f, 50f, false)]
        [InlineData(-20f, -12f, -50f, -10f, true)]
        [InlineData(-20f, -5f, -50f, -10f, false)]
        public void PhysicalHeightUsesGameCourseLimits(float min, float max, float lower, float upper, bool expected)
        {
            Assert.Equal(expected, PhysicalElevation.Allowed(min, max, lower, upper));
        }
    }
}
