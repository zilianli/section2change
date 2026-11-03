using InterchangeMod.Generation.Endpoints;

namespace InterchangeMod.Generation.Tests;

public class CompactGridRegistrationTests
{
    [Fact]
    public void CompactGridIsAnIndependentFourWayContract()
    {
        Assert.True(Enum.TryParse<InterchangeType>("CompactGrid",out var type));
        var contract=EndpointTypeCatalog.Find(type);
        Assert.NotNull(contract);Assert.Equal(4,contract.ArmCount);Assert.Equal(12,contract.RequiredMovements.Count);
        Assert.Equal(EndpointLayout.Crossroads,contract.Layout);
        Assert.True(contract.NodeOnly);
        var request=EndpointTypeRequest.Create(type,EndpointFixtures.DualCross(),true,.16f,new float[4]);
        Assert.True(request.InputReady);Assert.False(request.IsPlacementReady);
    }
    [Fact]
    public void NodeOnlyLegacyAdapterCannotGenerateAnotherInterchange()
    {
        IInterchangeTemplate template=new NodeOnlyInterchangeTemplate();
        Assert.Equal(InterchangeType.CompactGrid,template.Type);Assert.NotEmpty(template.ValidateParams(new InterchangeParams()));
        Assert.Empty(template.Generate(new InterchangeParams()));
        Assert.All(EndpointTypeCatalog.All.Where(c=>c.Type!=InterchangeType.CompactGrid),c=>Assert.False(c.NodeOnly));
    }
    [Theory]
    [InlineData(InterchangeType.Cloverleaf)]
    [InlineData(InterchangeType.Roundabout)]
    public void NodeOnlyAdapterRejectsTypesWithLegacyGenerators(InterchangeType type)
    {
        Assert.Throws<ArgumentOutOfRangeException>(()=>new NodeOnlyInterchangeTemplate(type));
    }
}
