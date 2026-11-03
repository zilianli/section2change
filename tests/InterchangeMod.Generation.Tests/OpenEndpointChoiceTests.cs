using InterchangeMod.Generation.Endpoints;
using InterchangeMod.Generation.Geometry;
using Xunit;
namespace InterchangeMod.Generation.Tests
{
    public class OpenEndpointChoiceTests
    {
        [Fact] public void ChoosesNearOpenEndWithoutJunctionPreference()
        {
            Assert.Equal(1,OpenEndpointChoice.Nearest(Vec3.Zero,new Vec3(100,0,0),new Vec3(100,0,0),32));
            Assert.Equal(0,OpenEndpointChoice.Nearest(Vec3.Zero,new Vec3(100,0,0),new Vec3(1,50,0),32));
            Assert.Equal(-1,OpenEndpointChoice.Nearest(Vec3.Zero,new Vec3(100,0,0),new Vec3(50,0,0),32));
            Assert.Equal(-1,OpenEndpointChoice.Nearest(Vec3.Zero,new Vec3(100,0,0),new Vec3(float.NaN,0,0),32));
        }
    }
}
