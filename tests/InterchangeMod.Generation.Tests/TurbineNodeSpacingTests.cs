using System;
using System.Linq;
using InterchangeMod.Generation.Geometry;
using InterchangeMod.Generation.Templates;
using Xunit;

namespace InterchangeMod.Generation.Tests
{
    public sealed class TurbineNodeSpacingTests
    {
        [Fact]
        public void RemovingEmittedShortNodesPreservesTheUnemittedEndProfile()
        {
            var template=new StackTemplate(turbine:true);
            var p=BalancedRoads(90,true,10);
            Assert.Empty(template.ValidateParams(p));
            foreach(var path in template.Generate(p).Where(s=>s.Name.StartsWith("Left",StringComparison.Ordinal)).GroupBy(s=>s.Name.Split('-')[0]))
            {
                var last=path.Last();
                Assert.InRange(Math.Abs(last.Curve.P0.Y-last.Curve.P3.Y),0,.35f);
            }
        }
        private static InterchangeParams BalancedRoads(float angle, bool rightHandTraffic, float baseHeight)
        {
            var p = new InterchangeParams
            {
                Legs = InterchangeParams.CrossLegs(angle, 0, 1),
                RampWidth = 8, RampPrefab = "Ramp8", RampSideGap = 3, MaxGrade = .18f,
                MainReducedPrefab = "Reduced16", MainReducedWidth = 16,
                CrossReducedPrefab = "Reduced16", CrossReducedWidth = 16,
                RightHandTraffic = rightHandTraffic,
            };
            p.SetRoad(0, 2, RoadForm.Dual, 20, 8, "Main20");
            p.SetRoad(1, 3, RoadForm.Dual, 20, 8, "Cross20");
            p.Levels[0].Elevation = baseHeight;
            p.Levels[1].Elevation = baseHeight + 10;
            return p;
        }

        private static float Horizontal(Vec3 value) => MathF.Sqrt(value.X * value.X + value.Z * value.Z);
        private static Vec3 FlatDirection(Vec3 value) => Vec3.Normalize(new Vec3(value.X, 0, value.Z));

        // The logged 18% case emitted four 2 m edges at the start of a profile
        // climb. Both endpoints generated new bridge pillars which hit each other.
        [Theory]
        [InlineData(90f, true, 10f)]
        [InlineData(105f, true, 0f)]
        [InlineData(75f, true, 10f)]
        [InlineData(105f, true, 10f)]
        [InlineData(90f, true, 0f)]
        public void ProfileDoesNotEmitAdjacentPillarsOnShortRampEdges(float angle, bool rightHandTraffic, float baseHeight)
        {
            var p = BalancedRoads(angle, rightHandTraffic, baseHeight);
            var template = new StackTemplate(turbine: true);
            Assert.Empty(template.ValidateParams(p));
            var roads = template.Generate(p);
            Assert.Empty(InterchangeLayout.ValidateGeometry(roads, p));
            TestHelpers.AssertRampsConnected(roads);
            Assert.Equal(8, InterchangeLayout.LegEnds(roads).Count);
            var ramps = roads.Where(s => s.Role == RoadRole.Ramp).ToArray();
            Assert.Equal(8, ramps.Select(s => s.Name.Split('-')[0]).Distinct().Count());
            foreach (var segment in ramps)
            {
                float distance = Horizontal(segment.EndNode - segment.StartNode);
                Assert.True(distance >= 9.99f, $"{segment.Name} has bridge nodes only {distance:F3} m apart");
                Assert.True(segment.Curve.MaxGrade(128) <= .18001f, $"{segment.Name} exceeds the requested grade");
                if (Math.Max(segment.Curve.P0.Y, segment.Curve.P3.Y) >= HeightRange.BridgeThreshold)
                    Assert.True(segment.Curve.Length(64) < 80, $"{segment.Name} forces another automatic elevated node");
            }
        }

        // A 20 m road and 8 m ramp must meet with their outside edges aligned:
        // their curve centers are 6 m apart while retaining the same graph node.
        [Theory]
        [InlineData(90f, true, 10f)]
        [InlineData(105f, true, 0f)]
        [InlineData(75f, true, 10f)]
        [InlineData(105f, true, 10f)]
        [InlineData(90f, true, 0f)]
        public void FourRightRampsMeetTheOuterLaneAtSharedTangentNodes(float angle, bool rightHandTraffic, float baseHeight)
        {
            var p = BalancedRoads(angle, rightHandTraffic, baseHeight);
            var template = new StackTemplate(turbine: true);
            Assert.Empty(template.ValidateParams(p));
            var roads = template.Generate(p);
            var paths = roads.Where(s => s.Name.StartsWith("Right", StringComparison.Ordinal))
                .GroupBy(s => s.Name.Split('-')[0]).ToArray();
            Assert.Equal(4, paths.Length);
            foreach (var path in paths)
            {
                var pieces = path.ToArray();
                foreach (bool start in new[] { true, false })
                {
                    var ramp = start ? pieces[0] : pieces[pieces.Length - 1];
                    Vec3 node = start ? ramp.StartNode : ramp.EndNode;
                    Vec3 point = start ? ramp.Curve.P0 : ramp.Curve.P3;
                    var main = roads.First(s => s.Role != RoadRole.Ramp && (s.StartNode == node || s.EndNode == node));
                    Vec3 heading = FlatDirection(main.Curve.Derivative(main.StartNode == node ? 0 : 1));
                    Vec3 tangent = FlatDirection(ramp.Curve.Derivative(start ? 0 : 1));
                    Assert.True(Directions.Dot(heading, tangent) > .99999f, $"{path.Key} is not tangent at {node}");
                    Vec3 outside = Directions.RightOf(heading) * (rightHandTraffic ? 1 : -1);
                    Assert.InRange(Directions.Dot(point - node, outside), 5.999f, 6.001f);
                    Assert.InRange(Math.Abs(Directions.Dot(point - node, heading)), 0, .001f);
                    Assert.InRange(Math.Abs(point.Y - node.Y), 0, .001f);
                }
            }
        }

        [Fact]
        public void BalancedRightMergesKeepACompactTangentApproach()
        {
            var p = BalancedRoads(90, true, 10);
            var template = new StackTemplate(turbine: true);
            Assert.Empty(template.ValidateParams(p));
            var roads = template.Generate(p);
            var paths = roads.Where(s => s.Name.StartsWith("Right", StringComparison.Ordinal))
                .GroupBy(s => s.Name.Split('-')[0]).ToArray();
            Assert.Equal(4, paths.Length);
            foreach (var path in paths)
            {
                var end = path.Last();
                float mergeAlong = Directions.Dot(end.EndNode, FlatDirection(end.Curve.Derivative(1)));
                // The old center-line merge was at 351.3 m. Edge alignment can
                // merge sooner while still leaving space for a main-road pillar.
                Assert.InRange(mergeAlong, 0, 350);
            }
        }
    }
}
