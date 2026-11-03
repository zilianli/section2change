using InterchangeMod.Generation.Endpoints;
using InterchangeMod.Generation.Templates;

namespace InterchangeMod.Generation.Tests;

public class CompactGridRegistrationTests
{
    [Fact]
    public void CompactGridIsAFourWayContractWithReplaceAndConnect()
    {
        Assert.True(Enum.TryParse<InterchangeType>("CompactGrid",out var type));
        var contract=EndpointTypeCatalog.Find(type);
        Assert.NotNull(contract);Assert.Equal(4,contract.ArmCount);Assert.Equal(12,contract.RequiredMovements.Count);
        Assert.Equal(EndpointLayout.Crossroads,contract.Layout);
        // 替换模式用 CompactGridTemplate，不再只能连接
        Assert.False(contract.NodeOnly);
        Assert.Equal(InterchangeType.CompactGrid,new CompactGridTemplate().Type);
        var request=EndpointTypeRequest.Create(type,EndpointFixtures.DualCross(),true,.16f,new float[4]);
        Assert.True(request.InputReady);Assert.False(request.IsPlacementReady);
    }
    [Fact]
    public void NoTypeIsConnectOnly()
    {
        Assert.All(EndpointTypeCatalog.All,c=>Assert.False(c.NodeOnly));
    }
}
