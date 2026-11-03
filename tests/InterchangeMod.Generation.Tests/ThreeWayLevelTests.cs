using System.Linq;
using InterchangeMod.Generation.Endpoints;
using InterchangeMod.Generation.Geometry;

namespace InterchangeMod.Generation.Tests;
public class ThreeWayLevelTests
{
    [Fact]
    public void FlatTeeUsesTheExistingTenMetreDefault()
    {
        var levels=EndpointLevels.Resolve(ThreeWayFixtures.Arms(),true,0,0,93);
        Assert.Equal(103,levels.Main,3);Assert.Equal(113,levels.Cross,3);
    }
    [Fact]
    public void MixedMainAndBranchHeightsRemainMeasuredTargets()
    {
        var arms=ThreeWayFixtures.Arms();
        arms[1]=new EndpointArm(1,arms[1].Endpoints.Select(p=>new SelectedEndpoint(p.NodeIndex,p.NodeVersion,
            p.Position+new Vec3(0,20,0),p.OutwardTangent,p.OneWay,p.TravelOutward,p.Width,p.Lanes,p.Prefab)).ToArray());
        var levels=EndpointLevels.Resolve(arms,true,0,0,93);
        Assert.Equal(103,levels.Main,3);Assert.Equal(123,levels.Cross,3);
    }
}
