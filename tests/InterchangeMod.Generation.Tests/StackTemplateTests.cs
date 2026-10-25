using System.Collections.Generic;
using System.Linq;
using InterchangeMod.Generation.Geometry;
using InterchangeMod.Generation.Templates;
using Xunit;

namespace InterchangeMod.Generation.Tests
{
    public class StackTemplateTests
    {
        private readonly StackTemplate _template = new StackTemplate();

        private const float MainWidth = 16f, CrossWidth = 12f, RampWidth = 8f;

        /// <summary>双幅公路主线在第 1 层（基层），双幅被交道路在第 2 层（高 10 米）跨过主线。</summary>
        private static InterchangeParams Make(float crossingAngle = 90f, float baseElevation = 0f)
        {
            var p = new InterchangeParams
            {
                Legs = InterchangeParams.CrossLegs(crossingAngle, 0, 1),
                RampWidth = RampWidth,
            };
            p.SetRoad(0, 2, RoadForm.Dual, MainWidth, 4f, "Highway Oneway");
            p.SetRoad(1, 3, RoadForm.Dual, CrossWidth, 4f, "Cross Oneway");
            p.Levels[0].Elevation = baseElevation;
            p.Levels[1].Elevation = baseElevation + 10f;
            return p;
        }

        public static IEnumerable<object[]> Cases() =>
            from angle in new[] { 90f, 75f, 105f, 60f, 120f }
            from baseElevation in new[] { 0f, 10f }
            select new object[] { angle, baseElevation };

        [Theory]
        [MemberData(nameof(Cases))]
        public void Generated_IsValidAndConnected(float angle, float baseElevation)
        {
            var p = Make(angle, baseElevation);
            Assert.Empty(_template.ValidateParams(p));
            var segments = _template.Generate(p);
            if (System.Environment.GetEnvironmentVariable("STACK_PREVIEW") is string dir)
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, $"stack{angle:F0}_{baseElevation:F0}.svg"), SvgPreview.Render(segments));
            Assert.Empty(InterchangeLayout.ValidateGeometry(segments, p));
            TestHelpers.AssertRampsConnected(segments);
            Assert.Equal(8, InterchangeLayout.LegEnds(segments).Count);
            Assert.Equal(4, segments.Select(s => s.Name).Where(n => n.StartsWith("Left")).Select(n => n.Split('-')[0]).Distinct().Count());
            Assert.Equal(4, segments.Select(s => s.Name).Where(n => n.StartsWith("Right")).Select(n => n.Split('-')[0]).Distinct().Count());
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void NoTwoRoadsCrossAtGrade(float angle, float baseElevation)
        {
            var p = Make(angle, baseElevation);
            var segments = _template.Generate(p);
            float Width(RoadSegmentSpec s) => s.Role == RoadRole.MainRoad ? MainWidth : s.Role == RoadRole.CrossRoad ? CrossWidth : RampWidth;
            var samples = segments.Select(s => Enumerable.Range(0, 41).Select(i => s.Curve.Evaluate(i / 40f)).ToArray()).ToArray();
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
                            bool close = dx * dx + dz * dz < reach * reach;
                            Assert.False(close && System.Math.Abs(u.Y - v.Y) < 7f, $"{a.Name} and {b.Name} cross at grade near ({u.X:F0}, {u.Z:F0}) {u.Y:F1}/{v.Y:F1}");
                        }
                }
            }
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void ElevatedPiecesAreShort(float angle, float baseElevation)
        {
            // 高架路段短于游戏切分长度（80 米），否则立柱位置不受控
            var segments = _template.Generate(Make(angle, baseElevation));
            foreach (var s in segments.Where(s => s.Curve.P0.Y > 8.5f || s.Curve.P3.Y > 8.5f))
                Assert.True(s.Curve.Length() < 80f, $"{s.Name} is {s.Curve.Length():F0} m");
        }

        [Fact]
        public void LeftRampsUseLevelsThreeAndFour()
        {
            var segments = _template.Generate(Make());
            float Top(string prefix) => segments.Where(s => s.Name.StartsWith(prefix)).Max(s => System.Math.Max(s.Curve.P0.Y, s.Curve.P3.Y));
            Assert.Equal(20f, Top("LeftMain"), 1);
            Assert.Equal(30f, Top("LeftCross"), 1);
        }

        [Fact]
        public void LegEndsKeepTheOriginalMedian()
        {
            // 立交内拉宽的分隔带在两端用 S 形接回：外端仍在原来的位置
            var p = Make();
            var segments = _template.Generate(p);
            var ends = InterchangeLayout.LegEnds(segments);
            Assert.Equal(8, ends.Count);
            foreach (var v in ends)
            {
                float toMain = System.Math.Abs(v.X * p.Legs[0].Direction.Z - v.Z * p.Legs[0].Direction.X);
                float toCross = System.Math.Abs(v.X * p.Legs[1].Direction.Z - v.Z * p.Legs[1].Direction.X);
                Assert.True(System.Math.Abs(toMain - p.Legs[0].CarriagewayOffset) < 0.05f || System.Math.Abs(toCross - p.Legs[1].CarriagewayOffset) < 0.05f,
                    $"leg end ({v.X:F1}, {v.Z:F1}) is off the original carriageways");
            }
        }

        [Theory]
        [InlineData(0f)]
        [InlineData(10f)]
        public void TwoWayRoadsOutsideJoinAligned(float baseElevation)
        {
            var p = Make(baseElevation: baseElevation);
            foreach (var leg in p.Legs)
            {
                leg.ExternalForm = RoadForm.TwoWay;
                leg.ExternalPrefab = "TwoWay";
            }
            Assert.Empty(_template.ValidateParams(p));
            var segments = _template.Generate(p);
            Assert.Empty(InterchangeLayout.ValidateGeometry(segments, p));
            Assert.Equal(4, InterchangeLayout.LegEnds(segments).Count);
            // 高架的臂端也分段，每段短于游戏切分长度
            foreach (var sg in segments.Where(sg => sg.Curve.P0.Y > 8.5f))
                Assert.True(sg.Curve.Length() < 80f, $"{sg.Name} is {sg.Curve.Length():F0} m");
        }

        [Theory]
        [InlineData(91f, 0f)]
        [InlineData(91f, 10f)]
        [InlineData(91f, 20f)]
        [InlineData(66f, 0f)]
        [InlineData(67f, 10f)]
        [InlineData(114f, 0f)]
        public void WideRoadsAndRamps(float angle, float baseElevation)
        {
            // 游戏内实测参数：两条路都是 4 车道公路单行（20 米，分隔带 8 米），2 车道匝道 12 米，层间高差 10 米
            var p = new InterchangeParams { Legs = InterchangeParams.CrossLegs(angle, 0, 1), RampWidth = 12f, RampSideGap = 1f };
            p.SetRoad(0, 2, RoadForm.Dual, 20f, 8f, "Highway Oneway");
            p.SetRoad(1, 3, RoadForm.Dual, 20f, 8f, "Highway Oneway");
            p.Levels[0].Elevation = baseElevation;
            p.Levels[1].Elevation = baseElevation + 10f;
            Assert.Empty(_template.ValidateParams(p));
            var segments = _template.Generate(p);
            Assert.Empty(InterchangeLayout.ValidateGeometry(segments, p));
            TestHelpers.AssertRampsConnected(segments);
            foreach (var sg in segments.Where(sg => sg.Curve.P0.Y > 8.5f || sg.Curve.P3.Y > 8.5f))
                Assert.True(sg.Curve.Length() < 80f, $"{sg.Name} is {sg.Curve.Length():F0} m");
        }

        [Theory]
        [InlineData(90f, 0f)]
        [InlineData(75f, 10f)]
        [InlineData(105f, 0f)]
        public void LaneBalance(float angle, float baseElevation)
        {
            // 两条路都在右转匝道驶出点和汇入点之间减车道：减车道段向中央分隔带平移，匝道外缘对齐
            var p = Make(angle, baseElevation);
            p.MainReducedPrefab = "Highway Oneway Reduced";
            p.MainReducedWidth = 8f;
            p.CrossReducedPrefab = "Cross Oneway Reduced";
            p.CrossReducedWidth = 4f;
            Assert.Empty(_template.ValidateParams(p));
            var segments = _template.Generate(p);
            Assert.Empty(InterchangeLayout.ValidateGeometry(segments, p));
            TestHelpers.AssertRampsConnected(segments);
            Assert.Equal(8, InterchangeLayout.LegEnds(segments).Count);
            foreach (var name in new[] { "MainA", "MainB" })
                Assert.Contains(segments, sg => sg.Name.StartsWith(name + "-") && sg.Prefab == "Highway Oneway Reduced");
            foreach (var name in new[] { "CrossA", "CrossB" })
                Assert.Contains(segments, sg => sg.Name.StartsWith(name + "-") && sg.Prefab == "Cross Oneway Reduced");
            // 减车道段的节点和曲线都在平移后的位置上，首尾相接
            foreach (var sg in segments.Where(sg => sg.Prefab != null && sg.Prefab.EndsWith("Reduced")))
                Assert.True(Vec3.Distance(sg.Curve.P0, sg.StartNode) < 6f && Vec3.Distance(sg.Curve.P3, sg.EndNode) < 6f, sg.Name);
            // 接在减车道段上的左转匝道不与减车道段重合（同宽时完全重合游戏报形状无效）：匝道端点与减车道段曲线端点错开
            foreach (var ramp in segments.Where(sg => sg.Name.StartsWith("Left")))
                foreach (var road in segments.Where(sg => sg.Prefab != null && sg.Prefab.EndsWith("Reduced")))
                {
                    if (ramp.StartNode.Equals(road.StartNode))
                        Assert.True(Vec3.Distance(ramp.Curve.P0, road.Curve.P0) > 2f, ramp.Name);
                    if (ramp.EndNode.Equals(road.EndNode))
                        Assert.True(Vec3.Distance(ramp.Curve.P3, road.Curve.P3) > 2f, ramp.Name);
                }
        }

        [Theory]
        [InlineData(90f, -13f, 0f)]
        [InlineData(70f, -13f, 0f)]
        [InlineData(89f, -12.9f, 4.5f)]
        public void MainInTunnelUnderGroundCrossRoad(float angle, float mainElevation, float crossElevation)
        {
            // 主线在 −13 米的隧道里（游戏里读出来常是 −12.9 米），被交道路在地面（地形起伏时可能是 4.5 米的路堤）
            var p = Make(angle);
            p.Levels[0].Elevation = mainElevation;
            p.Levels[1].Elevation = crossElevation;
            Assert.Empty(_template.ValidateParams(p));
            var segments = _template.Generate(p);
            Assert.Empty(InterchangeLayout.ValidateGeometry(segments, p));
            TestHelpers.AssertRampsConnected(segments);
            Assert.All(segments.Where(sg => sg.Name.StartsWith("MainA-")), sg => Assert.Equal(mainElevation, sg.Curve.P0.Y, 1));
        }

        [Fact]
        public void CrossRoadMustBeAboveMain()
        {
            var p = Make();
            p.Levels[1].Elevation = 0f;
            Assert.NotEmpty(_template.ValidateParams(p));
        }
    }
}
