using InterchangeMod.Generation.Endpoints;
using InterchangeMod.Generation.Geometry;
using InterchangeMod.Generation.Roads;

namespace InterchangeMod.Generation.Tests;

public sealed class EndpointMixedRoadSetupTests
{
    internal static RoadInfo[] Catalog(int lanes = 4) => CompactGridFixtures.Catalog().Append(new RoadInfo(
        $"TwoWay{lanes}", true, lanes, lanes, lanes == 2 ? 24 : 36,
        groundHeight: new HeightRange(-.4f, 5), elevatedHeight: new HeightRange(-2, 5))
        { ElevatedLength = 80, PillarWidth = 4 }).ToArray();

    internal static EndpointArm[] Arms(InterchangeType type, bool rht = true, int lanes = 4, int twoWayMask = 15,
        float distance = 700) => Enumerable.Range(0, type is InterchangeType.Trumpet or InterchangeType.DirectionalT
            or InterchangeType.SemiDirectionalT or InterchangeType.Wye ? 3 : 4).Select(i =>
    {
        var axis = Directions.FromAngle(type == InterchangeType.Wye ? i * 120 : i * 90);
        var center = new Vec3(0, 10, 0) + axis * distance;
        if ((twoWayMask & (1 << i)) != 0)
            return new EndpointArm(i, new[] { new SelectedEndpoint(100 + i, 1, center, axis, false, false,
                lanes == 2 ? 24 : 36, lanes, $"TwoWay{lanes}") });
        var side = Directions.RightOf(axis) * (rht ? 1 : -1); float width = 4 + 4 * lanes;
        return new EndpointArm(i, new[] { false, true }.Select(outward => new SelectedEndpoint(200 + i * 2 + (outward ? 1 : 0),
            1, center + side * ((width + 8) / 2) * (outward ? 1 : -1), axis, true, outward, width, lanes, $"Road{lanes}")));
    }).ToArray();

    internal static object Setup(EndpointArm[] arms, RoadInfo[] catalog, InterchangeType type, bool rht = true)
    {
        var setupType = typeof(EndpointArm).Assembly.GetType("InterchangeMod.Generation.Endpoints.EndpointMixedRoadSetup");
        Assert.NotNull(setupType);
        return setupType!.GetMethod("Create")!.Invoke(null, new object?[] { arms, catalog, type, rht, 0f, 0f, 0f,
            null, false, false, false, true, false, false })!;
    }

    [Theory]
    [InlineData(InterchangeType.Cloverleaf)] [InlineData(InterchangeType.Stack)] [InlineData(InterchangeType.Turbine)]
    [InlineData(InterchangeType.Trumpet)] [InlineData(InterchangeType.DirectionalT)] [InlineData(InterchangeType.SemiDirectionalT)]
    [InlineData(InterchangeType.Wye)] [InlineData(InterchangeType.CompactGrid)] [InlineData(InterchangeType.Roundabout)]
    public void PreparesAllNineExistingCoreFamiliesForSymmetricNativeRoads(InterchangeType type)
    {
        var setup = Setup(Arms(type), Catalog(), type);
        Assert.True((bool)setup.GetType().GetProperty("Success")!.GetValue(setup)!,
            (string)setup.GetType().GetProperty("Diagnostic")!.GetValue(setup)!);
    }

    [Fact]
    public void PartialCloverleafIsExplicitlyExcluded()
    {
        var setup = Setup(Arms(InterchangeType.PartialCloverleaf), Catalog(), InterchangeType.PartialCloverleaf);
        Assert.False((bool)setup.GetType().GetProperty("Success")!.GetValue(setup)!);
        Assert.NotEmpty((string)setup.GetType().GetProperty("Diagnostic")!.GetValue(setup)!);
    }

    [Theory]
    [InlineData("asymmetric")] [InlineData("transport")] [InlineData("missing-oneway")]
    [InlineData("grid-two-lanes")] [InlineData("wye-odd-lanes")]
    public void RejectsRoadCombinationsTheRealCatalogOrExistingCoreCannotRepresent(string invalid)
    {
        int lanes = invalid == "grid-two-lanes" ? 2 : invalid == "wye-odd-lanes" ? 3 : 4;
        var type = invalid == "grid-two-lanes" ? InterchangeType.CompactGrid : invalid == "wye-odd-lanes" ? InterchangeType.Wye : InterchangeType.Stack;
        var arms = Arms(type, lanes: lanes); var catalog = Catalog(lanes);
        if (invalid is "asymmetric" or "transport") catalog = catalog.Select(r => r.Name.StartsWith("TwoWay")
            ? new RoadInfo(r.Name, r.IsHighway, lanes, invalid == "asymmetric" ? lanes + 1 : lanes, r.Width,
                publicTransportLanes: invalid == "transport" ? 1 : 0) : r).ToArray();
        if (invalid == "missing-oneway") catalog = catalog.Where(r => r.ForwardLanes != lanes || r.BackwardLanes != 0).ToArray();
        var setup = Setup(arms, catalog, type);
        Assert.False((bool)setup.GetType().GetProperty("Success")!.GetValue(setup)!);
        Assert.NotEmpty((string)setup.GetType().GetProperty("Diagnostic")!.GetValue(setup)!);
    }

    [Fact]
    public void CommonSetupAndSearchContractExists()
    {
        var assembly = typeof(EndpointArm).Assembly;
        var setup = assembly.GetType("InterchangeMod.Generation.Endpoints.EndpointMixedRoadSetup");
        Assert.NotNull(setup);
        Assert.NotNull(setup!.GetMethod("Create"));
        foreach (string property in new[] { "Success", "Diagnostic", "Ramp", "Ring", "Design", "MainHeight", "CrossHeight", "RequiredClearance" })
            Assert.NotNull(setup.GetProperty(property));
        var search = assembly.GetType("InterchangeMod.Generation.Endpoints.EndpointMixedRoadSearch");
        Assert.NotNull(search);
        Assert.NotNull(search!.GetMethod("Search"));
    }
}
