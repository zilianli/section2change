using InterchangeMod.Generation.Endpoints;
using InterchangeMod.Generation.Geometry;

namespace InterchangeMod.Generation.Tests;

public class EndpointPlacementHintsTests
{
    private static bool Suggest(IReadOnlyList<EndpointArm>? arms)
    {
        var type = typeof(EndpointTypeCatalog).Assembly.GetType("InterchangeMod.Generation.Endpoints.EndpointPlacementHints");
        Assert.NotNull(type);
        var method = type.GetMethod("ShouldSuggestNoCollisionForCompactGrid");
        Assert.NotNull(method);
        return Assert.IsType<bool>(method.Invoke(null, new object?[] { arms }));
    }

    private static EndpointArm[] WithCentres(float crossingDegrees = 90, float rotationDegrees = 0)
    {
        float angle = crossingDegrees * MathF.PI / 180;
        var cross = new Vec3(MathF.Sin(angle), 0, MathF.Cos(angle));
        return EndpointFixtures.DualCross().Select(a =>
        {
            var centre = (a.Index % 2 == 0 ? Vec3.UnitZ : cross) * (a.Index < 2 ? 500 : -500);
            var oldCentre = (a.Endpoints[0].Position + a.Endpoints[1].Position) * .5f;
            return new EndpointArm(a.Index, a.Endpoints.Select(p => new SelectedEndpoint(p.NodeIndex, p.NodeVersion,
                (p.Position - oldCentre + centre).RotateY(rotationDegrees * MathF.PI / 180),
                p.OutwardTangent.RotateY(rotationDegrees * MathF.PI / 180), p.OneWay, p.TravelOutward, p.Width, p.Lanes, p.Prefab)));
        }).ToArray();
    }

    [Theory]
    [InlineData(80)]
    [InlineData(88.5f)]
    [InlineData(91.5f)]
    [InlineData(100)]
    public void SkewedOppositeMidpointSecantsSuggestManualCollisionBypassEvenWithOrthogonalTangents(float crossing)
        => Assert.True(Suggest(WithCentres(crossing)));

    [Theory]
    [InlineData(90, 0)]
    [InlineData(90, 37)]
    [InlineData(90, 90)]
    [InlineData(89.5f, 0)]
    [InlineData(90.5f, 37)]
    public void RightAnglesAndSubDegreeSkewDoNotSuggestCollisionBypass(float crossing, float rotation)
        => Assert.False(Suggest(WithCentres(crossing, rotation)));

    [Fact]
    public void UsesArmIndicesInsteadOfCollectionOrPortOrder()
    {
        var arms = WithCentres(85, 37).Reverse().Select(a => new EndpointArm(a.Index, a.Endpoints.Reverse())).ToArray();
        Assert.True(Suggest(arms));
    }

    [Fact]
    public void InvalidSelectionsCannotProduceAnAdvisory()
    {
        Assert.False(Suggest(null));
        Assert.False(Suggest(Array.Empty<EndpointArm>()));
        Assert.False(Suggest(WithCentres(85).Take(3).ToArray()));
        foreach (string invalid in new[] { "null-arm", "null-port", "duplicate-arm", "zero-axis", "nan", "infinity", "road", "direction" })
        {
            var arms = WithCentres(85);
            if (invalid == "null-arm") arms[0] = null!;
            else if (invalid == "duplicate-arm") arms[0] = new EndpointArm(1, arms[0].Endpoints);
            else if (invalid == "null-port") arms[0] = new EndpointArm(0, new SelectedEndpoint[] { null! });
            else
            {
                arms[0] = new EndpointArm(0, arms[0].Endpoints.Select(p => new SelectedEndpoint(p.NodeIndex, p.NodeVersion,
                    invalid == "zero-axis" ? p.Position - new Vec3(0, 0, 1000)
                        : invalid == "nan" ? new Vec3(float.NaN, 0, 1)
                        : invalid == "infinity" ? new Vec3(0, 0, float.PositiveInfinity) : p.Position,
                    p.OutwardTangent, p.OneWay, invalid == "direction" ? false : p.TravelOutward,
                    invalid == "road" ? 0 : p.Width, p.Lanes, p.Prefab)));
            }
            Assert.False(Suggest(arms), invalid);
        }
    }
}
