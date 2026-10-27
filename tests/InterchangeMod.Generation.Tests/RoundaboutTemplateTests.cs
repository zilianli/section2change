using System.Collections.Generic;
using System.Linq;
using InterchangeMod.Generation.Geometry;
using InterchangeMod.Generation.Templates;
using Xunit;

namespace InterchangeMod.Generation.Tests
{
    public class RoundaboutTemplateTests
    {
        private readonly RoundaboutTemplate _template = new RoundaboutTemplate();

        private const float MainWidth = 16f, CrossWidth = 20f, RampWidth = 8f, RingWidth = 12f;
        private const string Ring = "Ring Oneway";

        /// <summary>双幅公路主线、被交道路（双向或双幅）；mainOnTop 时主线 10 米高架跨过地面环岛，否则环岛在上层。半径自动。</summary>
        private static InterchangeParams Make(float crossingAngle = 90f, bool mainOnTop = true, bool racetrack = false, bool crossDual = false,
            bool rightHand = true)
        {
            var p = new InterchangeParams
            {
                Legs = InterchangeParams.CrossLegs(crossingAngle, 0, 1),
                RampWidth = RampWidth,
                LoopRadius = 0f,
                RingPrefab = Ring,
                RingWidth = RingWidth,
                RoundaboutRacetrack = racetrack,
                RightHandTraffic = rightHand,
            };
            p.SetRoad(0, 2, RoadForm.Dual, MainWidth, 4f, "Highway Oneway");
            if (crossDual)
                p.SetRoad(1, 3, RoadForm.Dual, 8f, 2f, "Small Oneway");
            else
                p.SetRoad(1, 3, RoadForm.TwoWay, CrossWidth, 0f, "Medium Road");
            p.Levels[0].Elevation = mainOnTop ? 10f : 0f;
            p.Levels[1].Elevation = mainOnTop ? 0f : 10f;
            return p;
        }

        public static IEnumerable<object[]> Cases() =>
            from angle in new[] { 90f, 75f, 105f, 60f, 120f }
            from mainOnTop in new[] { true, false }
            from racetrack in new[] { false, true }
            from crossDual in new[] { false, true }
            select new object[] { angle, mainOnTop, racetrack, crossDual };

        [Theory]
        [MemberData(nameof(Cases))]
        public void LaneBalance_ReducesTheMainBetweenOffAndOnRamps(float angle, bool mainOnTop, bool racetrack, bool crossDual)
        {
            // 驶出匝道分出点 ~ 驶入匝道汇入点之间主线减车道（内侧边缘不变），两条匝道在主线节点处外缘对齐
            var p = Make(angle, mainOnTop, racetrack, crossDual);
            p.MainReducedPrefab = "Highway Reduced";
            p.MainReducedWidth = MainWidth - 4f;
            Assert.Empty(_template.ValidateParams(p));
            var segments = _template.Generate(p);
            Assert.Empty(InterchangeLayout.ValidateGeometry(segments, p));
            TestHelpers.AssertRampsConnected(segments);
            var reduced = segments.Where(s => s.Prefab == "Highway Reduced").ToList();
            Assert.NotEmpty(reduced);
            Assert.Equal(4, reduced.Sum(s => (s.StartNode == s.Curve.P0 ? 0 : 1) + (s.EndNode == s.Curve.P3 ? 0 : 1)));
            var reducedNodes = reduced.SelectMany(s => new[] { s.StartNode, s.EndNode }).ToHashSet();
            Assert.Equal(4, segments.Count(s => s.Name.StartsWith("Ramp") && s.HasNodeOffset
                && (reducedNodes.Contains(s.StartNode) || reducedNodes.Contains(s.EndNode))));
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void Generated_IsValidAndConnected(float angle, bool mainOnTop, bool racetrack, bool crossDual)
        {
            var p = Make(angle, mainOnTop, racetrack, crossDual);
            Assert.Empty(_template.ValidateParams(p));
            var segments = _template.Generate(p);
            Assert.Empty(InterchangeLayout.ValidateGeometry(segments, p));
            TestHelpers.AssertRampsConnected(segments);
            // 主线两幅各两端 + 被交道路两头（双幅时每头两幅）
            Assert.Equal(crossDual ? 8 : 6, InterchangeLayout.LegEnds(segments).Count);

            // 环岛首尾相接成一圈：每个节点进出各一次
            var ring = segments.Where(s => s.Prefab == Ring).ToList();
            foreach (var node in ring.SelectMany(s => new[] { s.StartNode, s.EndNode }).Distinct())
            {
                Assert.Equal(1, ring.Count(s => s.StartNode == node));
                Assert.Equal(1, ring.Count(s => s.EndNode == node));
            }
            // 四条匝道、被交道路都接在环岛节点上
            var ringNodes = ring.Select(s => s.StartNode).ToList();
            var ramps = segments.Where(s => s.Role == RoadRole.Ramp && s.Prefab != Ring).ToList();
            Assert.Equal(4, ramps.SelectMany(s => new[] { s.StartNode, s.EndNode }).Count(ringNodes.Contains));
            Assert.Equal(crossDual ? 4 : 2, segments.Where(s => s.Role == RoadRole.CrossRoad)
                .SelectMany(s => new[] { s.StartNode, s.EndNode }).Count(ringNodes.Contains));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void RingKeepsTheIslandOnTheNonDrivingSide(bool rightHand)
        {
            // 右侧通行：环岛中心在行驶方向左侧（逆时针）；左侧通行反过来
            var segments = _template.Generate(Make(rightHand: rightHand));
            foreach (var s in segments.Where(s => s.Prefab == Ring))
            {
                Vec3 mid = s.Curve.Evaluate(0.5f);
                Vec3 d = s.Curve.Evaluate(0.51f) - s.Curve.Evaluate(0.49f);
                float side = Directions.Dot(new Vec3(-mid.X, 0f, -mid.Z), Directions.RightOf(Vec3.Normalize(new Vec3(d.X, 0f, d.Z))));
                Assert.True(rightHand ? side < 0f : side > 0f, $"{s.Name} runs the wrong way");
            }
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void NoTwoRoadsCrossAtGrade(float angle, bool mainOnTop, bool racetrack, bool crossDual)
        {
            var p = Make(angle, mainOnTop, racetrack, crossDual);
            var segments = _template.Generate(p);
            float Width(RoadSegmentSpec s) => s.Role == RoadRole.MainRoad ? MainWidth : s.Role == RoadRole.CrossRoad ? (crossDual ? 8f : CrossWidth)
                : s.Prefab == Ring ? RingWidth : RampWidth;
            // 环岛上的路口附近（匝道、被交道路接入环岛）本来就挨着环岛的相邻路段，不算
            var ringNodes = segments.Where(s => s.Prefab == Ring).Select(s => s.StartNode).ToList();
            var terminals = ringNodes.Where(n => segments.Count(s => s.StartNode == n || s.EndNode == n) > 2).ToList();
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
                    // 环岛是一条连续的闭合路，相邻两段隔着一小段时采样点也挨得近，不算
                    if (a.Prefab == Ring && b.Prefab == Ring)
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
        public void BothLevelsElevated_WideRing_RampsLeaveTheRingBeforeClimbing()
        {
            // 游戏内的情形：环岛 10 米高架、主线 20 米，四车道公路作环岛（20 米宽），双幅公路被交道路
            var p = Make(crossDual: true);
            p.RingWidth = 20f;
            p.SetRoad(1, 3, RoadForm.Dual, 20f, 8f, "Highway Oneway");
            p.Levels[0].Elevation = 20f;
            p.Levels[1].Elevation = 10f;
            Assert.Empty(_template.ValidateParams(p));
            var segments = _template.Generate(p);
            Assert.Empty(InterchangeLayout.ValidateGeometry(segments, p));
            // 匝道离开环岛路面（斜 60° 穿出半幅）之后还要保持环岛高度一段，才开始升降
            float needed = 10f / System.MathF.Sin(System.MathF.PI / 3f) + 15f;
            var ringNodes = segments.Where(s => s.Prefab == Ring).Select(s => s.StartNode).ToList();
            foreach (var ramp in segments.Where(s => s.Role == RoadRole.Ramp && s.Prefab != Ring
                && (ringNodes.Contains(s.StartNode) || ringNodes.Contains(s.EndNode))))
            {
                Vec3 node = ringNodes.Contains(ramp.StartNode) ? ramp.StartNode : ramp.EndNode;
                Vec3 other = node == ramp.StartNode ? ramp.EndNode : ramp.StartNode;
                Assert.Equal(10f, other.Y, 2);
                Assert.True(Vec3.Distance(node, other) >= needed - 0.5f, $"{ramp.Name} flat only {Vec3.Distance(node, other):F1} m");
            }
            // 高架环岛每段不超过游戏切分长度
            foreach (var s in segments.Where(s => s.Prefab == Ring))
                Assert.True(s.Curve.Length() <= 80f, $"{s.Name} is {s.Curve.Length():F0} m");
        }

        [Fact]
        public void RingPrefabRequired()
        {
            var p = Make();
            p.RingPrefab = null;
            Assert.Contains(_template.ValidateParams(p), e => e.Contains("环岛"));
        }

        [Fact]
        public void IsRegistered()
        {
            Assert.IsType<RoundaboutTemplate>(TemplateRegistry.Get(InterchangeType.Roundabout));
        }
    }
}
