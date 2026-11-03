using System;
using System.Linq;
using InterchangeMod.Generation.Geometry;
using InterchangeMod.Generation.Templates;
using Xunit;

namespace InterchangeMod.Generation.Tests
{
    public class StackRoadFormRegressionTests
    {
        private static InterchangeParams Roads(bool left = false)
        {
            var p = new InterchangeParams { RampWidth = 8, StackLeftExit = left };
            p.SetRoad(0, 2, RoadForm.Dual, 12, 4, "Main");
            p.SetRoad(1, 3, RoadForm.Dual, 12, 4, "Cross");
            p.Levels[0].Elevation = 0; p.Levels[1].Elevation = 10;
            return p;
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void NarrowExternalTwoWay_DoesNotForceWideParallelCarriagewaysIntoOneNode(bool turbine)
        {
            var p = Roads();
            foreach (var leg in p.Legs)
            { leg.ExternalForm = RoadForm.TwoWay; leg.ExternalPrefab = "TwoWay"; leg.ExternalWidth = 20; }
            var template = new StackTemplate(turbine);
            Assert.Empty(template.ValidateParams(p));
            var segments = template.Generate(p);
            int mergeJoints = 0;
            foreach (var tail in segments.Where(s => s.Prefab == "TwoWay"))
                foreach (var node in new[] { tail.StartNode, tail.EndNode })
                {
                    var incoming = segments.Where(s => s.Prefab != "TwoWay" && (s.StartNode == node || s.EndNode == node)).ToList();
                    if (incoming.Count != 2) continue;
                    mergeJoints++;
                    foreach (var road in incoming)
                    {
                        var tangent = road.Curve.Derivative(road.StartNode == node ? 0 : 1);
                        var axis = tail.Curve.P3 - tail.Curve.P0;
                        float sine = Math.Abs(tangent.X * axis.Z - tangent.Z * axis.X) / (tangent.Length() * axis.Length());
                        Assert.True(sine > 0.08f, $"{road.Name} incorrectly uses parallel edge alignment into a 20 m two-way road");
                    }
                }
            Assert.Equal(4, mergeJoints);
            Assert.Empty(InterchangeLayout.ValidateGeometry(segments, p));
            Assert.Equal(4, InterchangeLayout.LegEnds(segments).Count);
            TestHelpers.AssertRampsConnected(segments);
        }

        [Theory]
        [InlineData(90f)]
        [InlineData(75f)]
        [InlineData(105f)]
        public void LeftExit_ReducesBothCarriagewaysOfBothRoads(float angle)
        {
            var p = Roads(true);
            p.Legs = InterchangeParams.CrossLegs(angle, 0, 1);
            p.SetRoad(0, 2, RoadForm.Dual, 20, 8, "Main");
            p.SetRoad(1, 3, RoadForm.Dual, 20, 8, "Cross");
            p.MainReducedPrefab = "MainReduced"; p.MainReducedWidth = 12;
            p.CrossReducedPrefab = "CrossReduced"; p.CrossReducedWidth = 12;
            var template = new StackTemplate();
            Assert.Empty(template.ValidateParams(p));
            var roads = template.Generate(p);
            foreach (var prefix in new[] { "MainA-", "MainB-", "CrossA-", "CrossB-" })
                Assert.Contains(roads, s => s.Name.StartsWith(prefix) && s.Prefab!.EndsWith("Reduced"));
            Assert.Empty(InterchangeLayout.ValidateGeometry(roads, p));
            TestHelpers.AssertRampsConnected(roads);
        }

        [Fact]
        public void LeftExit_LoggedTwoAndThreeLaneRoads_KeepOuterStraightEdges()
        {
            var p = Roads(true);
            p.SetRoad(0, 2, RoadForm.Dual, 12, 2, "Main");
            p.SetRoad(1, 3, RoadForm.Dual, 16, 2, "Cross");
            p.MainReducedPrefab = "MainReduced"; p.MainReducedWidth = 8;
            p.CrossReducedPrefab = "CrossReduced"; p.CrossReducedWidth = 12;
            p.Levels[0].Elevation = 0; p.Levels[1].Elevation = 8.5f;
            var template = new StackTemplate();
            Assert.Empty(template.ValidateParams(p));
            var roads = template.Generate(p);
            foreach (var prefix in new[] { "MainA-", "MainB-", "CrossA-", "CrossB-" })
            {
                var full = roads.First(s => s.Name.StartsWith(prefix) && s.Prefab == (prefix.StartsWith("Main") ? "Main" : "Cross"));
                var tangent = Vec3.Normalize(full.Curve.P3 - full.Curve.P0);
                var right = Directions.RightOf(tangent);
                float width = prefix.StartsWith("Main") ? 12 : 16, reducedWidth = width - 4;
                float outer = Directions.Dot(full.Curve.P0, right) + width / 2;
                var reduced = roads.Where(s => s.Name.StartsWith(prefix) && s.Prefab!.EndsWith("Reduced")).ToList();
                Assert.NotEmpty(reduced);
                foreach (var segment in reduced)
                {
                    Assert.InRange(Math.Abs(Directions.Dot(segment.Curve.P0, right) + reducedWidth / 2 - outer), 0, 0.02f);
                    Assert.InRange(Math.Abs(Directions.Dot(segment.Curve.P3, right) + reducedWidth / 2 - outer), 0, 0.02f);
                }
            }
        }
    }
}
