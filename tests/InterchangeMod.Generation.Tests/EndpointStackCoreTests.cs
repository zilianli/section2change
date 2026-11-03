using System;
using System.Collections.Generic;
using System.Linq;
using InterchangeMod.Generation.Endpoints;
using InterchangeMod.Generation.Geometry;
using InterchangeMod.Generation.Roads;

namespace InterchangeMod.Generation.Tests
{
    public sealed class EndpointStackCoreTests
    {
        private static readonly RoadInfo Ramp = new RoadInfo("Ramp8", true, 1, 0, 8);

        public static IEnumerable<object[]> Configurations()
        {
            foreach (bool rht in new[] { true, false })
                foreach (bool mainUpper in new[] { false, true })
                {
                    yield return new object[] { InterchangeType.Stack, false, rht, mainUpper };
                    yield return new object[] { InterchangeType.Stack, true, rht, mainUpper };
                    yield return new object[] { InterchangeType.Turbine, false, rht, mainUpper };
                }
        }

        // Catches reversed path flow, the wrong LHT reflection, and remapping the selected
        // main family to the legacy lower layer without restoring the original arm IDs.
        [Theory]
        [MemberData(nameof(Configurations))]
        public void PreservesEightPortsAndTwelveDirectedMovements(InterchangeType type, bool leftExit, bool rht, bool mainUpper)
        {
            var arms = Arms(rht);
            float hm = mainUpper ? 113 : 103, hc = mainUpper ? 103 : 113;
            var core = EndpointStackCore.Create(arms, type, rht, hm, hc, Ramp, .12f, new CloverleafRoadDesign(), leftExit);
            Assert.True(core.Success, core.Diagnostic);
            Assert.Equal(8, core.Ports.Count);
            Assert.All(core.Segments, s => Assert.False(string.IsNullOrWhiteSpace(s.PathId)));
            Assert.Equal(12, core.Segments.Select(s => s.PathId).Distinct().Count());
            foreach (var path in core.Segments.GroupBy(s => s.PathId))
            {
                var pieces = path.ToArray();
                Assert.All(pieces, s => Assert.Equal(pieces[0].SourceArm, s.SourceArm));
                for (int n = 1; n < pieces.Length; n++) Assert.Equal(pieces[n - 1].EndNode, pieces[n].StartNode);
            }
            foreach (int arm in Enumerable.Range(0, 4))
            {
                var input = Assert.Single(core.Ports.Where(p => p.Arm == arm && !p.TravelOutward));
                var output = Assert.Single(core.Ports.Where(p => p.Arm == arm && p.TravelOutward));
                float height = arm % 2 == 0 ? hm : hc;
                Assert.Equal(height, input.Position.Y, 3);
                Assert.Equal(height, output.Position.Y, 3);
                var original = arms[arm].Endpoints[0];
                Assert.Equal(original.Prefab, input.Prefab);
                Assert.Equal(original.Width, input.Width);
                Assert.True(Dot(input.Tangent, -original.OutwardTangent) > .9999f);
                Assert.True(Dot(output.Tangent, original.OutwardTangent) > .9999f);
                Assert.Contains(core.Segments, s => s.Kind == CloverEdgeKind.Main && s.WorldCurve.P0 == input.Position
                    && Dot(s.WorldCurve.Derivative(0), input.Tangent) > .9999f);
                Assert.Contains(core.Segments, s => s.Kind == CloverEdgeKind.Main && s.WorldCurve.P3 == output.Position
                    && Dot(s.WorldCurve.Derivative(1), output.Tangent) > .9999f);
                var reachable = Reachable(core.Segments, input.Position);
                foreach (int target in Enumerable.Range(0, 4).Where(t => t != arm))
                    Assert.Contains(core.Ports.Single(p => p.Arm == target && p.TravelOutward).Position, reachable);
                Assert.DoesNotContain(output.Position, reachable);
            }
            foreach (var path in core.Segments.Where(s => s.Kind != CloverEdgeKind.Main).GroupBy(s => s.PathId))
            {
                var pieces = path.ToArray();
                var last = Assert.Single(pieces.Where(s => !pieces.Any(other => other.StartNode == s.EndNode)));
                var reachable = Reachable(core.Segments.Where(s => s.Kind == CloverEdgeKind.Main), last.EndNode);
                int source = pieces[0].SourceArm;
                bool direct = pieces[0].Kind == CloverEdgeKind.DirectRamp;
                int target = (source + ((direct == rht) ? 3 : 1)) % 4;
                Assert.Contains(core.Ports.Single(p => p.Arm == target && p.TravelOutward).Position, reachable);
            }
            Assert.All(core.Segments, s =>
            {
                Assert.True(s.RoadWidth > 0);
                for (int n = 0; n <= 80; n++)
                {
                    var d = s.WorldCurve.Derivative(n / 80f);
                    Assert.True(Math.Abs(d.Y) / Math.Sqrt(d.X * d.X + d.Z * d.Z) <= .120001,
                        $"{s.PathId} exceeds the requested grade");
                }
            });
        }

        // Catches reducing the wrong family after a height swap, or retaining full-width
        // metadata when a different prefab is used for the middle through road.
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void CarriesActualReducedPrefabAndWidth(bool leftExit)
        {
            var balances = new[]
            {
                new EndpointLaneBalance(0, "MainReduced12", 12, 2, 1),
                new EndpointLaneBalance(2, "MainReduced12", 12, 2, 1),
                new EndpointLaneBalance(1, "CrossReduced8", 8, 1, 1),
                new EndpointLaneBalance(3, "CrossReduced8", 8, 1, 1),
            };
            var core = EndpointStackCore.Create(Arms(), InterchangeType.Stack, true, 113, 103, Ramp, .12f,
                new CloverleafRoadDesign(balances), leftExit);
            Assert.True(core.Success, core.Diagnostic);
            foreach (int source in Enumerable.Range(0, 4))
            {
                var roads = core.Segments.Where(s => s.Kind == CloverEdgeKind.Main && s.SourceArm == source).ToArray();
                string reduced = source % 2 == 0 ? "MainReduced12" : "CrossReduced8";
                float width = source % 2 == 0 ? 12 : 8;
                Assert.Contains(roads, s => s.Prefab == reduced);
                Assert.All(roads.Where(s => s.Prefab == reduced), s => Assert.Equal(width, s.RoadWidth));
                Assert.All(roads.Where(s => s.Prefab != reduced), s => Assert.Equal(source % 2 == 0 ? 20 : 16, s.RoadWidth));
            }
            Assert.All(core.Segments.Where(s => s.Kind != CloverEdgeKind.Main), s =>
            { Assert.Equal("Ramp8", s.Prefab); Assert.Equal(8, s.RoadWidth); });
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void RejectsUnsupportedRoadMismatchWithoutPartialOutput(bool widthMismatch)
        {
            var arms = Arms();
            var old = arms[2].Endpoints[1];
            var changed = new SelectedEndpoint(old.NodeIndex, old.NodeVersion, old.Position, old.OutwardTangent,
                true, old.TravelOutward, widthMismatch ? 19 : old.Width, old.Lanes, widthMismatch ? old.Prefab : "Other");
            arms[2] = new EndpointArm(2, new[] { arms[2].Endpoints[0], changed });
            var core = EndpointStackCore.Create(arms, InterchangeType.Stack, true, 103, 113, Ramp, .12f, new CloverleafRoadDesign());
            Assert.False(core.Success);
            Assert.NotEmpty(core.Diagnostic);
            Assert.Empty(core.Segments);
            Assert.Empty(core.Ports);
        }

        [Fact]
        public void RejectsDifferentBalancesOnTheSameFamily()
        {
            var design = new CloverleafRoadDesign(new[] { new EndpointLaneBalance(0, "Reduced", 12, 2, 1) });
            var core = EndpointStackCore.Create(Arms(), InterchangeType.Stack, true, 103, 113, Ramp, .12f, design);
            Assert.False(core.Success);
            Assert.Empty(core.Segments);
            Assert.Empty(core.Ports);
        }

        [Fact]
        public void RejectsInvalidMaximumGradeBeforeAlternativeSearch()
        {
            var core = EndpointStackCore.Create(Arms(), InterchangeType.Stack, true, 103, 113, Ramp, .31f,
                new CloverleafRoadDesign());
            Assert.False(core.Success);
            Assert.Empty(core.Segments);
            Assert.Empty(core.Ports);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void SupportsActualReducedRoadsAtLoggedMixedLevels(bool leftExit)
        {
            var arms = MixedApproachTests.LoggedArms(true);
            var ramp = new RoadInfo("Ramp", true, 1, 0, 8) { PillarWidth = 4, ElevatedLength = 80 };
            var design = new CloverleafRoadDesign(Enumerable.Range(0, 4).Select(i => new EndpointLaneBalance(i, "Reduced", 16, 3, 1)),
                bridgeRules: Enumerable.Range(0, 4).Select(i => new EndpointBridgeRule(i, 80, null, 20, pillarWidth: 4)),
                supportRules: new[] { new EndpointPrefabSupportRule("Full", 80, null, 4),
                    new EndpointPrefabSupportRule("Reduced", 80, null, 4), new EndpointPrefabSupportRule("Ramp", 80, null, 4) });
            var levels = EndpointLevels.Resolve(arms, true, 0, 0, 478.63f);
            var core = EndpointStackCore.Create(arms, InterchangeType.Stack, true, levels.Main, levels.Cross, ramp, .27f, design, leftExit);
            Assert.True(core.Success, core.Diagnostic);
            var candidate = new SpatialCandidateResult(SpatialCandidateFailure.None, core.Segments);
            var deck = EndpointNetworkClearance.Check(candidate, 8.5f, out string deckDetail);
            var support = EndpointNetworkSupports.Check(candidate, design.BridgeRules, ramp, out string supportDetail, design.SupportRules);
            Assert.True(deck == CurveClearanceResult.Clear && support == CurveClearanceResult.Clear, deckDetail + "; " + supportDetail);
            Assert.Contains(core.Segments, s => s.Prefab == "Reduced" && s.RoadWidth == 16);
            Assert.Equal(8, core.Ports.Count);
            foreach (var port in core.Ports)
            {
                Assert.Equal(port.Arm % 2 == 0 ? levels.Main : levels.Cross, port.Position.Y);
                Assert.Equal("Full", port.Prefab); Assert.Equal(20, port.Width);
            }
            Assert.All(core.Segments, s => Assert.Equal(VerticalProfileFailure.None, VerticalProfile.CheckGrade(s.WorldCurve, .27f)));
        }

        // Catches legacy ramp cuts putting a node or automatic pillar through another
        // deck even though the two road surfaces have enough vertical clearance.
        [Theory]
        [InlineData(InterchangeType.Stack, true)]
        [InlineData(InterchangeType.Stack, false)]
        [InlineData(InterchangeType.Turbine, true)]
        public void ReturnedCoreHasClearNodesAndAutomaticSupports(InterchangeType type, bool rht)
        {
            var arms = SupportRegressionArms(type, rht);
            float main = type == InterchangeType.Turbine ? 488.63f : 10;
            float cross = type == InterchangeType.Turbine ? 500.55f : 22;
            bool leftExit = type == InterchangeType.Stack;
            var design = new CloverleafRoadDesign(bridgeRules: Enumerable.Range(0, 4)
                .Select(i => new EndpointBridgeRule(i, 80, null, 20)));
            var core = EndpointStackCore.Create(arms, type, rht, main, cross, Ramp, .16f, design, leftExit);
            Assert.True(core.Success, core.Diagnostic);
            var candidate = new SpatialCandidateResult(SpatialCandidateFailure.None, core.Segments);
            var deck = EndpointNetworkClearance.Check(candidate, 8.5f, out var deckDetail);
            var support = EndpointNetworkSupports.Check(candidate, design.BridgeRules, Ramp, out var supportDetail);
            string evidence = $"Original deck={deck}: {deckDetail}; support={support}: {supportDetail}";
            Assert.True(deck == CurveClearanceResult.Clear && support == CurveClearanceResult.Clear, evidence);
            Assert.Equal(8, core.Ports.Count);
            foreach (var port in core.Ports)
            {
                var selected = arms[port.Arm].Endpoints.Single(p => p.TravelOutward == port.TravelOutward);
                var outward = new Vec3(selected.OutwardTangent.X, 0, selected.OutwardTangent.Z);
                Assert.Equal(port.Arm % 2 == 0 ? main : cross, port.Position.Y);
                Assert.Equal(selected.Prefab, port.Prefab);
                Assert.Equal(selected.Width, port.Width);
                Assert.True(Dot(port.Tangent, port.TravelOutward ? outward : -outward) > .99999f);
            }
            Assert.All(core.Segments, s => Assert.Equal(VerticalProfileFailure.None,
                VerticalProfile.CheckGrade(s.WorldCurve, .16f)));
        }

        private static EndpointArm[] SupportRegressionArms(InterchangeType type, bool rht)
        {
            if (type == InterchangeType.Stack)
                return EndpointFixtures.DualCross(100, rht).Select(a => new EndpointArm(a.Index, a.Endpoints.Select(p =>
                    new SelectedEndpoint(p.NodeIndex, p.NodeVersion,
                        new Vec3(p.Position.X, a.Index % 2 == 0 ? 10 : 22, p.Position.Z),
                        p.OutwardTangent + new Vec3(0, a.Index % 2 == 0 ? .018f : -.012f, 0), true,
                        p.TravelOutward, 20, 4, "Road")))).ToArray();
            var positions = new[] { new Vec3(-3681.96f,488.63f,-1322.51f),new Vec3(-3674.93f,488.63f,-1295.43f),
                new Vec3(-3566.12f,500.67f,-1214.54f),new Vec3(-3540.68f,500.66f,-1227.68f),
                new Vec3(-3447.27f,488.63f,-1354.48f),new Vec3(-3454.29f,488.63f,-1381.56f),
                new Vec3(-3601.27f,500.42f,-1451.16f),new Vec3(-3626.7f,500.43f,-1437.95f) };
            return Enumerable.Range(0, 4).Select(i => new EndpointArm(i, Enumerable.Range(0, 2).Select(k =>
            {
                var p = positions[i * 2 + k]; var other = positions[((i + 2) % 4) * 2 + 1 - k];
                return new SelectedEndpoint(i * 10 + k + 1, 1, p, p - other, true, k == 1, 20, 4, "Road");
            }))).ToArray();
        }

        private static float Dot(Vec3 a, Vec3 b) => Directions.Dot(Vec3.Normalize(a), Vec3.Normalize(b));
        private static HashSet<Vec3> Reachable(IEnumerable<SpatialSegment> roads, Vec3 start)
        {
            var segments = roads.ToArray();
            var seen = new HashSet<Vec3> { start };
            var queue = new Queue<Vec3>(); queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var node = queue.Dequeue();
                foreach (var road in segments.Where(s => s.StartNode == node))
                    if (seen.Add(road.EndNode)) queue.Enqueue(road.EndNode);
            }
            return seen;
        }

        private static EndpointArm[] Arms(bool rht = true)
        {
            // A translated, rotated 75-degree crossing; the core must retain the actual
            // axes instead of building an orthogonal stock shape or moving endpoint Y.
            var result = new EndpointArm[4];
            for (int arm = 0; arm < 4; arm++)
            {
                float angle = arm % 2 == 0 ? 23 + (arm == 2 ? 180 : 0) : 98 + (arm == 3 ? 180 : 0);
                var outward = Directions.FromAngle(angle);
                var right = Directions.RightOf(outward) * (rht ? 1 : -1);
                float width = arm % 2 == 0 ? 20 : 16;
                var center = new Vec3(400, 0, -230) + outward * 1300;
                result[arm] = new EndpointArm(arm, new[]
                {
                    new SelectedEndpoint(arm * 10 + 1, 1, center - right * ((width + 8) / 2), outward, true, false,
                        width, arm % 2 == 0 ? 3 : 2, arm % 2 == 0 ? "Main20" : "Cross16"),
                    new SelectedEndpoint(arm * 10 + 2, 1, center + right * ((width + 8) / 2), outward, true, true,
                        width, arm % 2 == 0 ? 3 : 2, arm % 2 == 0 ? "Main20" : "Cross16"),
                });
            }
            return result;
        }
    }
}
