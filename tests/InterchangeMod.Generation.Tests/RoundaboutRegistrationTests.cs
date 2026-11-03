using InterchangeMod.Generation.Endpoints;
using InterchangeMod.Generation.Templates;

namespace InterchangeMod.Generation.Tests;

public class RoundaboutRegistrationTests
{
    [Fact]
    public void RoundaboutKeepsLegacyGenerationAlongsideItsFourWayEndpointContract()
    {
        var contract=EndpointTypeCatalog.Get(InterchangeType.Roundabout);
        Assert.False(contract.NodeOnly);
        Assert.Equal(EndpointLayout.Crossroads,contract.Layout);
        Assert.Equal(4,contract.ArmCount);
        Assert.Equal(12,contract.RequiredMovements.Select(m=>(m.FromArm,m.ToArm)).Distinct().Count());
        Assert.All(contract.RequiredMovements,m=>Assert.NotEqual(m.FromArm,m.ToArm));
        Assert.Throws<ArgumentOutOfRangeException>(()=>new NodeOnlyInterchangeTemplate(InterchangeType.Roundabout));
        IInterchangeTemplate template=new RoundaboutTemplate();
        Assert.Equal(InterchangeType.Roundabout,template.Type);
        var parameters=new InterchangeParams{Legs=InterchangeParams.CrossLegs(90,0,1),RampWidth=8,LoopRadius=0,
            RingPrefab="Ring",RingWidth=12};
        parameters.SetRoad(0,2,RoadForm.Dual,16,4,"Highway");
        parameters.SetRoad(1,3,RoadForm.TwoWay,20,0,"Cross");
        parameters.Levels[0].Elevation=10;
        parameters.Levels[1].Elevation=0;
        Assert.NotEmpty(template.Generate(parameters));
    }
}
