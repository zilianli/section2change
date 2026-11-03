namespace InterchangeMod.Generation.Tests;

public class GridSettingRoundTripTests
{
    public static IEnumerable<object[]> Types()=>Enum.GetNames<InterchangeType>().Select(t=>new object[]{t});
    [Theory,MemberData(nameof(Types))]
    public void ActualSettingSetterPreservesRegisteredTypeAcrossMenuSelection(string type)
    {
        var setting=new global::InterchangeMod.Setting(null!);
        Assert.True(setting.SetValue(nameof(setting.InterchangeType),type));Assert.Equal(type,setting.InterchangeType);
        var reopened=new global::InterchangeMod.Setting(null!);
        Assert.True(reopened.SetValue(nameof(setting.InterchangeType),setting.InterchangeType));Assert.Equal(type,reopened.InterchangeType);
    }
    [Fact]
    public void UnrecognizedTypeKeepsExistingDiamondFallback()
    {
        var setting=new global::InterchangeMod.Setting(null!);
        Assert.True(setting.SetValue(nameof(setting.InterchangeType),"Unregistered"));Assert.Equal("Diamond",setting.InterchangeType);
    }
    [Fact]
    public void RemovedFlowerSettingMigratesToRoundaboutWhenDeserialized()
    {
        var setting=new global::InterchangeMod.Setting(null!);
        setting.InterchangeType="CompactFlower";
        Assert.Equal("Roundabout",setting.InterchangeType);
    }
    [Fact]
    public void RemovedFlowerSettingMigratesToRoundaboutThroughPanelSetter()
    {
        var setting=new global::InterchangeMod.Setting(null!);
        Assert.True(setting.SetValue(nameof(setting.InterchangeType),"CompactFlower"));
        Assert.Equal("Roundabout",setting.InterchangeType);
        var reopened=new global::InterchangeMod.Setting(null!){InterchangeType=setting.InterchangeType};
        Assert.Equal("Roundabout",reopened.InterchangeType);
    }
    [Theory]
    [InlineData("Diamond")]
    [InlineData("Roundabout")]
    [InlineData("Unregistered")]
    [InlineData("compactflower")]
    public void DeserializationMigrationChangesOnlyTheExactRemovedType(string type)
    {
        var setting=new global::InterchangeMod.Setting(null!){InterchangeType=type};
        Assert.Equal(type,setting.InterchangeType);
    }
}
