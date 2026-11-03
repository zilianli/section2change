using System.Linq;
using InterchangeMod.Generation.Templates;
using Xunit;

namespace InterchangeMod.Generation.Tests
{
    public class GroundMergeSubdivisionTests
    {
        private static InterchangeParams LoggedRoads(float angle)
        {
            var p = new InterchangeParams { Legs = InterchangeParams.CrossLegs(angle, 0, 1), RampWidth = 12, RampSideGap = 4 };
            p.SetRoad(0, 2, RoadForm.Dual, 20, 2, "Full");
            p.SetRoad(1, 3, RoadForm.Dual, 20, 2, "Full");
            p.MainReducedPrefab = p.CrossReducedPrefab = "Reduced";
            p.MainReducedWidth = p.CrossReducedWidth = 12;
            foreach (var leg in p.Legs)
            {
                leg.ExternalForm = RoadForm.TwoWay; leg.ExternalPrefab = "TwoWay"; leg.ExternalWidth = 40;
                leg.GroundHeight = new HeightRange(-0.4f, 5); leg.ElevatedHeight = new HeightRange(-2, 5);
            }
            p.Levels[0].Elevation = 0; p.Levels[1].Elevation = 10;
            return p;
        }

        [Theory]
        [InlineData(false, 89.00903f)]
        [InlineData(false, 90f)]
        [InlineData(true, 89.00903f)]
        [InlineData(true, 90f)]
        public void GroundMainAndRampCoursesDoNotForceGameCutsInsideMerge(bool turbine, float angle)
        {
            var p = LoggedRoads(angle);
            var template = new StackTemplate(turbine);
            Assert.Empty(template.ValidateParams(p));
            var roads = template.Generate(p);
            var ground = roads.Where(s => s.Curve.P0.Y < HeightRange.BridgeThreshold && s.Curve.P3.Y < HeightRange.BridgeThreshold).ToList();
            Assert.NotEmpty(ground);
            foreach (var segment in ground)
                Assert.True(segment.Curve.Length() < 200f, $"{segment.Name} is {segment.Curve.Length():F1} m: the observed game split is about 200 m");
            Assert.Empty(InterchangeLayout.ValidateGeometry(roads, p));
            TestHelpers.AssertRampsConnected(roads);
            Assert.Equal(4, InterchangeLayout.LegEnds(roads).Count);
        }
    }
}
