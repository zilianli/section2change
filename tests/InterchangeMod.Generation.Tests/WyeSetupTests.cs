using InterchangeMod.Generation.Endpoints;
using InterchangeMod.Generation.Geometry;
using InterchangeMod.Generation.Roads;

namespace InterchangeMod.Generation.Tests;

public class WyeSetupTests
{
    private static RoadInfo Road(string name,int lanes,float width,HeightRange? ground=null,HeightRange? elevated=null)
        =>new(name,true,lanes,0,width,groundHeight:ground,elevatedHeight:elevated){ElevatedLength=80,PillarWidth=4};
    private static RoadInfo[] Catalog(EndpointArm[] arms)=>arms.SelectMany(a=>a.Endpoints)
        .GroupBy(p=>p.Prefab).Select(g=>Road(g.Key,g.First().Lanes,g.First().Width))
        .Concat(new[]{Road("Ramp8",1,8),Road("Ramp12",2,12),Road("Ramp16",3,16)}).ToArray();

    [Theory]
    [InlineData(2,"Ramp8")]
    [InlineData(4,"Ramp12")]
    [InlineData(6,"Ramp16")]
    public void ChoosesExactHalfLaneRampInsteadOfStickyPreviousType(int lanes,string expected)
    {
        var arms=WyeNodeFixtures.Arms().Select(a=>new EndpointArm(a.Index,a.Endpoints.Select(p=>
            new SelectedEndpoint(p.NodeIndex,p.NodeVersion,p.Position,p.OutwardTangent,true,p.TravelOutward,p.Width,lanes,p.Prefab)))).ToArray();
        var setup=EndpointWyeSetup.Create(arms,Catalog(arms),0,0,83,"Ramp8");
        Assert.True(setup.Success,setup.Diagnostic);Assert.Equal(expected,setup.Ramp!.Name);
        Assert.Equal(lanes,setup.Ramp.LanesPerDirection*2);Assert.Equal(103,setup.BaseHeight);Assert.Equal(113,setup.UpperHeight);
        Assert.Equal(3,setup.BridgeRules.Count);Assert.Equal(8.5f,setup.RequiredClearance);
    }

    [Fact]
    public void ThreeArmsShareMeanBaseAndKeepTheirOriginalEndpointHeights()
    {
        var arms=WyeNodeFixtures.Arms(mixed:true);var originals=arms.SelectMany(a=>a.Endpoints).Select(p=>p.Position).ToArray();
        var setup=EndpointWyeSetup.Create(arms,Catalog(arms),2,0,83);
        Assert.True(setup.Success,setup.Diagnostic);Assert.Equal(105,setup.BaseHeight);Assert.Equal(115,setup.UpperHeight);
        Assert.Equal(originals,arms.SelectMany(a=>a.Endpoints).Select(p=>p.Position));
        Assert.Equal(24,setup.BridgeRules.Single(r=>r.Arm==1).Width);
    }

    [Fact]
    public void ActualVerticalEnvelopesRaiseAutoGapAndRejectExplicitInsufficientGap()
    {
        var arms=WyeNodeFixtures.Arms();var catalog=Catalog(arms).Where(r=>r.Name!="Ramp12")
            .Append(Road("Ramp12",2,12,new HeightRange(0,20),new HeightRange(-20,20))).ToArray();
        var auto=EndpointWyeSetup.Create(arms,catalog,0,0,83);
        Assert.True(auto.Success,auto.Diagnostic);Assert.Equal(40,auto.RequiredClearance);Assert.True(auto.UpperHeight-auto.BaseHeight>40);
        var specified=EndpointWyeSetup.Create(arms,catalog,0,10,83);
        Assert.False(specified.Success);Assert.Null(specified.Ramp);Assert.Empty(specified.BridgeRules);Assert.Contains("40",specified.Diagnostic);
    }

    [Theory]
    [InlineData("odd")]
    [InlineData("unequal")]
    [InlineData("within-arm")]
    [InlineData("reverse-ramp")]
    [InlineData("missing-road")]
    [InlineData("bad-range")]
    public void UnsupportedRoadDataFailsWithAnActionableMessage(string error)
    {
        var arms=WyeNodeFixtures.Arms();
        if(error=="odd"||error=="unequal"||error=="within-arm")
            arms=arms.Select(a=>new EndpointArm(a.Index,a.Endpoints.Select(p=>new SelectedEndpoint(p.NodeIndex,p.NodeVersion,
                p.Position,p.OutwardTangent,true,p.TravelOutward,p.Width,
                error=="odd"?3:error=="unequal"&&a.Index==1?2:error=="within-arm"&&a.Index==0&&p.TravelOutward?2:4,p.Prefab)))).ToArray();
        var catalog=Catalog(arms);
        if(error=="reverse-ramp")catalog=catalog.Where(r=>r.Name!="Ramp12").Append(new RoadInfo("Reverse",true,0,2,12)).ToArray();
        if(error=="missing-road")catalog=catalog.Where(r=>r.Name!="Main20").ToArray();
        if(error=="bad-range")catalog=catalog.Where(r=>r.Name!="Ramp12").Append(Road("Ramp12",2,12,new HeightRange(10,-10))).ToArray();
        var result=EndpointWyeSetup.Create(arms,catalog,0,0,83);
        Assert.False(result.Success);Assert.False(string.IsNullOrWhiteSpace(result.Diagnostic));
        Assert.Null(result.Ramp);Assert.Empty(result.BridgeRules);
    }

    [Theory]
    [InlineData(float.NaN,0,83)]
    [InlineData(0,-1,83)]
    [InlineData(0,8,83)]
    [InlineData(0,0,float.NaN)]
    [InlineData(0,float.PositiveInfinity,83)]
    public void InvalidLevelSettingsAreRejected(float offset,float gap,float ground)
    {
        var arms=WyeNodeFixtures.Arms();var result=EndpointWyeSetup.Create(arms,Catalog(arms),offset,gap,ground);
        Assert.False(result.Success);Assert.Null(result.Ramp);Assert.Empty(result.BridgeRules);
    }
}
