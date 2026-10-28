using InterchangeMod.Generation.Geometry;
using Xunit;

namespace InterchangeMod.Generation.Tests
{
    public class ConnectionPolicyTests
    {
        [Theory]
        [InlineData(false, 0, 0, 1, 0, false)] // nearby parallel frontage road
        [InlineData(true, 0, 0, 0, 1, false)] // perpendicular branch
        [InlineData(true, 1, -1, 1, 0, false)] // opposite one-way traffic
        [InlineData(true, 0, 1, 1, 0, false)] // two-way to one-way without a transition
        [InlineData(true, 1, 1, -1, 0, false)] // inward remainder
        [InlineData(true, 1, 1, 1, 0, true)]
        [InlineData(true, -1, -1, 1, 0, true)]
        [InlineData(true, 0, 0, 1, 0, true)]
        [InlineData(true, 0, 0, 0, 0, false)] // degenerate tangent
        public void RejectsWrongRoadOrTraffic(bool matched, int flow, int oldFlow, float x, float z, bool expected)
        {
            Assert.Equal(expected, ConnectionPolicy.CanConnect(matched, Vec3.UnitX, flow, new Vec3(x, 0, z), oldFlow));
        }
    }
}
