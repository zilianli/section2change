using System.Collections.Generic;
using System.Linq;
using InterchangeMod.Generation.Geometry;
using InterchangeMod.Generation.Templates;
using Xunit;

namespace InterchangeMod.Generation.Tests
{
    public class TurbineTemplateTests
    {
        private readonly StackTemplate _template = new StackTemplate(turbine: true);

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
            from angle in new[] { 90f, 75f, 105f }
            from baseElevation in new[] { 0f, 10f }
            select new object[] { angle, baseElevation };

        [Fact]
        public void IsRegisteredAsTurbine()
        {
            Assert.Equal(InterchangeType.Turbine, _template.Type);
            Assert.Equal(InterchangeType.Turbine, TemplateRegistry.Get(InterchangeType.Turbine)?.Type);
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void Generated_IsValidAndConnected(float angle, float baseElevation)
        {
            var p = Make(angle, baseElevation);
            var log = new List<string>();
            StackTemplate.Trace = log.Add;
            var errors = new StackTemplate(turbine: true).ValidateParams(p);
            StackTemplate.Trace = null;
            Assert.True(errors.Count == 0, string.Join("; ", errors.Concat(log)));
            var segments = _template.Generate(p);
            if (System.Environment.GetEnvironmentVariable("STACK_PREVIEW") is string dir)
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, $"turbine{angle:F0}_{baseElevation:F0}.svg"), SvgPreview.Render(segments));
            Assert.Empty(InterchangeLayout.ValidateGeometry(segments, p));
            TestHelpers.AssertRampsConnected(segments);
            Assert.Equal(8, InterchangeLayout.LegEnds(segments).Count);
            Assert.Equal(4, segments.Select(s => s.Name).Where(n => n.StartsWith("Left")).Select(n => n.Split('-')[0]).Distinct().Count());
            Assert.Equal(4, segments.Select(s => s.Name).Where(n => n.StartsWith("Right")).Select(n => n.Split('-')[0]).Distinct().Count());
            // 三层：最高不超过第 3 层
            Assert.True(segments.Max(s => System.Math.Max(s.Curve.P0.Y, s.Curve.P3.Y)) <= baseElevation + 20f + 0.01f);
            // 高架段短于游戏切分长度
            foreach (var sg in segments.Where(sg => sg.Curve.P0.Y > 8.5f || sg.Curve.P3.Y > 8.5f))
                Assert.True(sg.Curve.Length() < 80f, $"{sg.Name} is {sg.Curve.Length():F0} m");
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void NoTwoRoadsCrossAtGrade(float angle, float baseElevation)
        {
            var segments = _template.Generate(Make(angle, baseElevation));
            float Width(RoadSegmentSpec s) => s.Role == RoadRole.MainRoad ? MainWidth : s.Role == RoadRole.CrossRoad ? CrossWidth : RampWidth;
            var samples = segments.Select(s => Enumerable.Range(0, 21).Select(i => s.Curve.Evaluate(i / 20f)).ToArray()).ToArray();
            for (int i = 0; i < segments.Count; i++)
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
                                $"{a.Name} and {b.Name} cross at grade near ({u.X:F0}, {u.Z:F0}) {u.Y:F1}/{v.Y:F1}");
                        }
                }
        }

        [Theory]
        // 游戏内对准已有立体交叉、层高差正好是最小净空 7.5 米的两处：匝道在平面上挨着道路或别的匝道的地方，上下至少差 7.5 米
        //（原来只要求 0.9 个层高，匝道还没升降到位就跨过去，只差 7.0 米，游戏报净空不足）
        [InlineData(20f, 8f, 16f, 4f, 12f, 20f, 0.07f, 8f)]
        [InlineData(20f, 8f, 12f, 2f, 12f, 10f, 0.08f, 0f)]
        // 从游戏读回的高度带误差（10.00004 / 17.50003）：层高差比 7.5 米少万分之几米也要放得下（游戏内定向式、涡轮报“现在高差 7.5 米”）
        [InlineData(20f, 8f, 12f, 2f, 12f, 10.00004f, 0.08f, 0f, -0.00001f)]
        public void MinimumLevelGap_KeepsTheGameClearanceEverywhere(float mainWidth, float mainMedian, float crossWidth, float crossMedian,
            float rampWidth, float baseElevation, float maxGrade, float crossReduced, float gapError = 0f)
        {
            var p = new InterchangeParams
            {
                Legs = InterchangeParams.CrossLegs(90f, 0, 1), RampWidth = rampWidth, RampSideGap = 4f, MaxGrade = maxGrade,
                MainReducedPrefab = "Main Reduced", MainReducedWidth = 12f,
                CrossReducedPrefab = crossReduced > 0f ? "Cross Reduced" : null, CrossReducedWidth = crossReduced,
            };
            p.SetRoad(0, 2, RoadForm.Dual, mainWidth, mainMedian, "Highway Oneway");
            p.SetRoad(1, 3, RoadForm.Dual, crossWidth, crossMedian, "Cross Oneway");
            p.Levels[0].Elevation = baseElevation;
            p.Levels[1].Elevation = baseElevation + HeightRange.MeasuredMinimumGap + gapError;
            Assert.Empty(new StackTemplate(turbine: false).ValidateParams(p));
            var template = new StackTemplate(turbine: true);
            Assert.Empty(template.ValidateParams(p));
            var segments = template.Generate(p);
            float Width(RoadSegmentSpec s) => s.Role == RoadRole.Ramp ? rampWidth
                : s.Prefab == "Main Reduced" ? 12f : s.Prefab == "Cross Reduced" ? crossReduced
                : s.Role == RoadRole.MainRoad ? mainWidth : crossWidth;
            var samples = segments.Select(s => Enumerable.Range(0, 41).Select(i => s.Curve.Evaluate(i / 40f)).ToArray()).ToArray();
            for (int i = 0; i < segments.Count; i++)
                for (int j = i + 1; j < segments.Count; j++)
                {
                    var a = segments[i];
                    var b = segments[j];
                    var nodesA = new[] { a.StartNode, a.EndNode };
                    // 同一条匝道（或道路）前后几段在平面上本来就挨着
                    if (nodesA.Contains(b.StartNode) || nodesA.Contains(b.EndNode) || a.Name.Split('-')[0] == b.Name.Split('-')[0])
                        continue;
                    float reach = (Width(a) + Width(b)) / 2f;
                    foreach (var u in samples[i])
                        foreach (var v in samples[j])
                        {
                            float dx = u.X - v.X, dz = u.Z - v.Z, dy = System.Math.Abs(u.Y - v.Y);
                            // 上下错开（不是同层分流、汇入处挨着）的地方，高差要够游戏的净空
                            Assert.False(dx * dx + dz * dz < reach * reach && dy > 1f && dy < HeightRange.MeasuredMinimumGap - HeightRange.GapTolerance,
                                $"{a.Name} and {b.Name} only {System.Math.Abs(u.Y - v.Y):F2} m apart near ({u.X:F0}, {u.Z:F0}) {u.Y:F2}/{v.Y:F2}");
                        }
                }
        }

        [Theory]
        [InlineData(20f, 8f, 8f, 4f, 12f, 8.5f)]
        [InlineData(20f, 8f, 20f, 8f, 12f, 8.5f)]
        [InlineData(20f, 8f, 20f, 8f, 12f, 10f)]
        // 游戏里空地上放不下的：3 车道公路主线（车道平衡减成 2 车道）、1 或 3 车道被交道路、1 车道匝道，坡度上限 6%
        [InlineData(16f, 4f, 8f, 4f, 8f, 8.5f, 0.06f, 2f, 12f, 0f)]
        [InlineData(16f, 4f, 16f, 4f, 8f, 8.5f, 0.06f, 2f, 12f, 12f)]
        public void GameRoads(float mainWidth, float mainMedian, float crossWidth, float crossMedian, float rampWidth, float gap,
            float maxGrade = 0.08f, float sideGap = 1f, float mainReduced = 0f, float crossReduced = 0f)
        {
            // 游戏内实测参数：单行公路双幅主线和被交道路，自动层高 8.5 米
            var p = new InterchangeParams
            {
                Legs = InterchangeParams.CrossLegs(90f, 0, 1), RampWidth = rampWidth, RampSideGap = sideGap, MaxGrade = maxGrade, StackLeftExit = true,
                MainReducedPrefab = mainReduced > 0f ? "Main Reduced" : null, MainReducedWidth = mainReduced,
                CrossReducedPrefab = crossReduced > 0f ? "Cross Reduced" : null, CrossReducedWidth = crossReduced,
            };
            p.SetRoad(0, 2, RoadForm.Dual, mainWidth, mainMedian, "Highway Oneway");
            p.SetRoad(1, 3, RoadForm.Dual, crossWidth, crossMedian, "Cross Oneway");
            p.Levels[0].Elevation = 0f;
            p.Levels[1].Elevation = gap;
            var log = new List<string>();
            StackTemplate.Trace = log.Add;
            var errors = new StackTemplate(turbine: true).ValidateParams(p);
            StackTemplate.Trace = null;
            Assert.True(errors.Count == 0, string.Join("; ", errors.Concat(log.Where(l => !l.StartsWith("params")))));
            var segments = _template.Generate(p);
            Assert.Empty(InterchangeLayout.ValidateGeometry(segments, p));
            TestHelpers.AssertRampsConnected(segments);
            AssertNothingStandsOnLowerRoads(segments, sg => sg.Role == RoadRole.Ramp ? rampWidth
                : sg.Prefab == "Main Reduced" ? mainReduced : sg.Prefab == "Cross Reduced" ? crossReduced
                : sg.Role == RoadRole.MainRoad ? mainWidth : crossWidth);
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void NothingStandsOnLowerRoads(float angle, float baseElevation)
        {
            var segments = _template.Generate(Make(angle, baseElevation));
            AssertNothingStandsOnLowerRoads(segments, sg => sg.Role == RoadRole.MainRoad ? MainWidth : sg.Role == RoadRole.CrossRoad ? CrossWidth : RampWidth);
        }

        /// <summary>
        /// 高架节点（≥ 8.5 米，下面有立柱）不落在更低的道路上；4–8.5 米的路段是路堤（挡土墙），不能从更低的道路上方经过。
        /// 隧道里的道路不算。
        /// </summary>
        private static void AssertNothingStandsOnLowerRoads(IReadOnlyList<RoadSegmentSpec> segments, System.Func<RoadSegmentSpec, float> width)
        {
            var lines = segments.Select(sg => Enumerable.Range(0, 41).Select(i => sg.Curve.Evaluate(i / 40f)).ToArray()).ToArray();
            // 点到一段路的水平距离和最近处的高度
            (float Distance, float Height) Nearest(Vec3 v, Vec3[] line)
            {
                (float, float) best = (float.MaxValue, 0f);
                for (int i = 0; i + 1 < line.Length; i++)
                {
                    Vec3 a = line[i], b = line[i + 1];
                    float dx = b.X - a.X, dz = b.Z - a.Z, len2 = dx * dx + dz * dz;
                    float t = len2 > 0f ? System.Math.Max(0f, System.Math.Min(1f, ((v.X - a.X) * dx + (v.Z - a.Z) * dz) / len2)) : 0f;
                    float px = a.X + dx * t - v.X, pz = a.Z + dz * t - v.Z, d = (float)System.Math.Sqrt(px * px + pz * pz);
                    if (d < best.Item1)
                        best = (d, a.Y + (b.Y - a.Y) * t);
                }
                return best;
            }
            for (int i = 0; i < segments.Count; i++)
            {
                var a = segments[i];
                for (int j = 0; j < segments.Count; j++)
                {
                    if (i == j)
                        continue;
                    var b = segments[j];
                    foreach (var node in new[] { a.StartNode, a.EndNode }.Where(n => n.Y >= HeightRange.BridgeThreshold))
                    {
                        var (d, h) = Nearest(node, lines[j]);
                        Assert.False(d < width(b) / 2f + 1f && h < node.Y - 1f && h > HeightRange.TunnelThreshold + 0.5f,
                            $"pillar of {a.Name} at ({node.X:F1}, {node.Z:F1}) {node.Y:F1} m stands on {b.Name} ({h:F1} m, {d:F1} m away)");
                    }
                    for (int k = 1; k + 1 < lines[i].Length; k++)
                    {
                        var v = lines[i][k];
                        if (v.Y < HeightRange.RaisedThreshold || v.Y >= HeightRange.BridgeThreshold - 0.01f)
                            continue;
                        var (d, h) = Nearest(v, lines[j]);
                        Assert.False(d < (width(a) + width(b)) / 2f - 0.5f && h < v.Y - 1f && h > HeightRange.TunnelThreshold + 0.5f,
                            $"embankment {a.Name} at ({v.X:F1}, {v.Z:F1}) {v.Y:F1} m over {b.Name} ({h:F1} m)");
                    }
                }
            }
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void ElevatedRoadPillarsMissRampsUnderneath(float angle, float baseElevation)
        {
            // 被交道路（高架）的节点下面有立柱，不能落在从它下面穿过的匝道上
            var segments = _template.Generate(Make(angle, baseElevation));
            var ramps = segments.Where(s => s.Role == RoadRole.Ramp).SelectMany(s => Enumerable.Range(0, 11).Select(i => s.Curve.Evaluate(i / 10f))).ToList();
            foreach (var road in segments.Where(s => s.Role == RoadRole.CrossRoad && s.StartNode.Y > 8.5f))
                foreach (var node in new[] { road.StartNode, road.EndNode })
                {
                    var hit = ramps.Where(v => v.Y < node.Y - 1f && System.Math.Sqrt((v.X - node.X) * (v.X - node.X) + (v.Z - node.Z) * (v.Z - node.Z)) < (CrossWidth + RampWidth) / 2f).ToList();
                    Assert.True(hit.Count == 0, $"{road.Name} node ({node.X:F0},{node.Y:F1},{node.Z:F0}) over ramp at ({(hit.Count > 0 ? hit[0].X : 0):F0},{(hit.Count > 0 ? hit[0].Y : 0):F1},{(hit.Count > 0 ? hit[0].Z : 0):F0}) {string.Join(",", segments.Where(sg => sg.Role == RoadRole.Ramp && Enumerable.Range(0, 11).Any(i => sg.Curve.Evaluate(i / 10f).Equals(hit.FirstOrDefault()))).Select(sg => sg.Name))}");
                }
        }
    }
}
