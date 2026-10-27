using System.Collections.Generic;
using System.Linq;
using InterchangeMod.Generation.Geometry;
using InterchangeMod.Generation.Templates;
using Xunit;

namespace InterchangeMod.Generation.Tests
{
    public class PartialCloverleafTemplateTests
    {
        private readonly PartialCloverleafTemplate _template = new PartialCloverleafTemplate();

        private const float MainWidth = 16f, CrossWidth = 20f, RampWidth = 8f, SharedWidth = 16f;

        /// <summary>双幅公路主线、双向被交道路；mainOnTop 时主线 10 米高架跨过地面的被交道路，否则反过来。环道半径自动。</summary>
        private static InterchangeParams Make(float crossingAngle = 90f, bool mainOnTop = true, bool loopsForExit = true)
        {
            var p = new InterchangeParams
            {
                Legs = InterchangeParams.CrossLegs(crossingAngle, 0, 1),
                RampWidth = RampWidth,
                LoopRadius = 0f,
                ParcloLoopsForExit = loopsForExit,
                SharedRampPrefab = "Highway Twoway",
                SharedRampWidth = SharedWidth,
            };
            p.SetRoad(0, 2, RoadForm.Dual, MainWidth, 4f, "Highway Oneway");
            p.SetRoad(1, 3, RoadForm.TwoWay, CrossWidth, 0f, "Medium Road");
            p.Levels[0].Elevation = mainOnTop ? 10f : 0f;
            p.Levels[1].Elevation = mainOnTop ? 0f : 10f;
            return p;
        }

        public static IEnumerable<object[]> Cases() =>
            from angle in new[] { 90f, 75f, 105f, 60f, 120f }
            from mainOnTop in new[] { true, false }
            from loopsForExit in new[] { true, false }
            select new object[] { angle, mainOnTop, loopsForExit };

        [Theory]
        [MemberData(nameof(Cases))]
        public void LaneBalance_ReducesTheMainBetweenRampAndLoop(float angle, bool mainOnTop, bool loopsForExit)
        {
            // 直连匝道驶出点 ~ 环道汇入点之间主线减车道（内侧边缘不变），两条匝道在主线节点处外缘对齐
            var p = Make(angle, mainOnTop, loopsForExit);
            p.MainReducedPrefab = "Highway Reduced";
            p.MainReducedWidth = MainWidth - 4f;
            Assert.Empty(_template.ValidateParams(p));
            var segments = _template.Generate(p);
            Assert.Empty(InterchangeLayout.ValidateGeometry(segments, p));
            TestHelpers.AssertRampsConnected(segments);
            Assert.Equal(6, InterchangeLayout.LegEnds(segments).Count);
            var reduced = segments.Where(s => s.Prefab == "Highway Reduced").ToList();
            Assert.NotEmpty(reduced);
            // 两侧各两头（驶出点、汇入点）曲线端点平移
            Assert.Equal(4, reduced.Sum(s => (s.StartNode == s.Curve.P0 ? 0 : 1) + (s.EndNode == s.Curve.P3 ? 0 : 1)));
            var reducedNodes = reduced.SelectMany(s => new[] { s.StartNode, s.EndNode }).ToHashSet();
            foreach (string ramp in new[] { "Ramp", "Loop", "RampB", "LoopB" })
                Assert.Contains(segments, s => s.Name.StartsWith(ramp) && s.HasNodeOffset
                    && (reducedNodes.Contains(s.StartNode) || reducedNodes.Contains(s.EndNode)));
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void Generated_IsValidAndConnected(float angle, bool mainOnTop, bool loopsForExit)
        {
            var p = Make(angle, mainOnTop, loopsForExit);
            Assert.Empty(_template.ValidateParams(p));
            var segments = _template.Generate(p);
            Assert.Empty(InterchangeLayout.ValidateGeometry(segments, p));
            TestHelpers.AssertRampsConnected(segments);
            // 主线两幅各两端 + 被交道路两端
            Assert.Equal(6, InterchangeLayout.LegEnds(segments).Count);
            // 被交道路上两个平交路口：每个路口一条双向主干
            var crossNodes = segments.Where(s => s.Role == RoadRole.CrossRoad).SelectMany(s => new[] { s.StartNode, s.EndNode }).Distinct().ToList();
            var terminals = crossNodes.Where(n => segments.Any(s => s.Role == RoadRole.Ramp && (s.StartNode == n || s.EndNode == n))).ToList();
            Assert.Equal(2, terminals.Count);
            foreach (var n in terminals)
                Assert.Single(segments, s => s.Role == RoadRole.Ramp && (s.StartNode == n || s.EndNode == n) && s.Prefab == "Highway Twoway");
            // 两个分岔点：主干、环道、直连匝道三条交于一点
            Assert.Equal(2, SplitNodes(segments).Count);
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void NoTwoRoadsCrossAtGrade(float angle, bool mainOnTop, bool loopsForExit)
        {
            var p = Make(angle, mainOnTop, loopsForExit);
            var segments = _template.Generate(p);
            float Width(RoadSegmentSpec s) => s.Role == RoadRole.MainRoad ? MainWidth : s.Role == RoadRole.CrossRoad ? CrossWidth
                : s.Prefab == "Highway Twoway" ? SharedWidth : RampWidth;
            // 平交路口附近（匝道斜着接入被交道路）本来就挨着路口另一边的路段，不算
            // 分岔点附近环道和直连匝道本来就相切分开，也不算
            var terminals = segments.Where(s => s.Role == RoadRole.Ramp).SelectMany(s => new[] { s.StartNode, s.EndNode })
                .Where(n => segments.Any(s => s.Role == RoadRole.CrossRoad && (s.StartNode == n || s.EndNode == n)))
                .Concat(SplitNodes(segments)).ToList();
            bool NearTerminal(Vec3 v) => terminals.Any(t => (t.X - v.X) * (t.X - v.X) + (t.Z - v.Z) * (t.Z - v.Z) < 30f * 30f);
            var samples = segments.Select(s => Enumerable.Range(0, 41).Select(i => s.Curve.Evaluate(i / 40f)).Where(v => !NearTerminal(v)).ToArray()).ToArray();
            for (int i = 0; i < segments.Count; i++)
            {
                for (int j = i + 1; j < segments.Count; j++)
                {
                    var a = segments[i];
                    var b = segments[j];
                    var nodesA = new[] { a.StartNode, a.EndNode };
                    if (nodesA.Contains(b.StartNode) || nodesA.Contains(b.EndNode))
                        continue;
                    float reach = (Width(a) + Width(b)) / 2f;
                    foreach (var u in samples[i])
                        foreach (var v in samples[j])
                        {
                            float dx = u.X - v.X, dz = u.Z - v.Z;
                            Assert.False(dx * dx + dz * dz < reach * reach && System.Math.Abs(u.Y - v.Y) < 7f,
                                $"{a.Name} and {b.Name} cross at grade near ({u.X:F0}, {u.Z:F0})");
                        }
                }
            }
        }

        [Fact]
        public void LoopsForExit_LoopsLeaveTheMainBeyondTheCrossRoad()
        {
            // B2：环道从主线驶出，驶出点在被交道路之后（沿主线行驶方向）；A2：环道汇入主线，在被交道路之前
            foreach (bool exit in new[] { true, false })
            {
                var p = Make(loopsForExit: exit);
                var segments = _template.Generate(p);
                var mainNodes = segments.Where(s => s.Role == RoadRole.MainRoad).SelectMany(s => new[] { s.StartNode, s.EndNode }).ToList();
                var loopFirst = segments.First(s => s.Name.StartsWith("Loop") && !s.Name.StartsWith("LoopB"));
                var loopPieces = segments.Where(s => s.Name.StartsWith("Loop") && !s.Name.StartsWith("LoopB")).ToList();
                bool startsOnMain = loopPieces.Any(s => mainNodes.Contains(s.StartNode));
                Assert.Equal(exit, startsOnMain);
            }
        }

        private static List<Vec3> SplitNodes(IReadOnlyList<RoadSegmentSpec> segments) =>
            segments.Where(s => s.Role == RoadRole.Ramp).SelectMany(s => new[] { s.StartNode, s.EndNode }).GroupBy(n => n)
                .Where(g => g.Count() == 3).Select(g => g.Key).ToList();

        [Theory]
        [InlineData(90f)]
        [InlineData(65f)]
        public void WideCrossRoad_StemLeavesTheRoadBeforeItsFirstNode(float angle)
        {
            // 40 米宽的被交道路：主干第一个节点要在路面之外，并且离分岔点还有一段
            var p = Make(angle);
            p.SetRoad(1, 3, RoadForm.TwoWay, 40f, 0f, "XL Road");
            Assert.Empty(_template.ValidateParams(p));
            var segments = _template.Generate(p);
            var cross = segments.Where(s => s.Role == RoadRole.CrossRoad).ToList();
            var splits = SplitNodes(segments);
            foreach (var first in segments.Where(s => s.Prefab == "Highway Twoway" && cross.Any(c => c.StartNode == s.StartNode || c.EndNode == s.StartNode)))
            {
                float sin = System.MathF.Sin(angle * System.MathF.PI / 180f);
                float along = Vec3.Distance(first.StartNode, first.EndNode);
                Assert.True(along * sin > 20f, $"stem node {along:F1} m from the cross road centerline");
                Assert.True(splits.Min(n => Vec3.Distance(n, first.EndNode)) >= 10f || splits.Contains(first.EndNode) && along * sin > 30f);
            }
        }

        [Fact]
        public void TwoWayCrossRoadRequired()
        {
            var p = Make();
            p.SetRoad(1, 3, RoadForm.Dual, 12f, 4f, "Cross Oneway");
            Assert.Contains(_template.ValidateParams(p), e => e.Contains("双向道路"));
        }

        [Fact]
        public void IsRegistered()
        {
            Assert.IsType<PartialCloverleafTemplate>(TemplateRegistry.Get(InterchangeType.PartialCloverleaf));
        }
    }
}
