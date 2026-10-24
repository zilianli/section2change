using System.Collections.Generic;
using System.Linq;
using InterchangeMod.Generation.Geometry;
using InterchangeMod.Generation.Templates;
using Xunit;

namespace InterchangeMod.Generation.Tests
{
    public class CloverleafTemplateTests
    {
        private readonly CloverleafTemplate _template = new CloverleafTemplate();

        private const float MainWidth = 16f, CrossWidth = 12f, RampWidth = 8f;

        /// <summary>双幅公路主线、双幅被交道路；mainOnTop 时主线 10 米高架跨过地面的被交道路，否则反过来。环道半径自动。</summary>
        private static InterchangeParams Make(float crossingAngle = 90f, bool mainOnTop = true, float baseElevation = 0f)
        {
            var p = new InterchangeParams
            {
                Legs = InterchangeParams.CrossLegs(crossingAngle, 0, 1),
                RampWidth = RampWidth,
                LoopRadius = 0f,
            };
            p.SetRoad(0, 2, RoadForm.Dual, MainWidth, 4f, "Highway Oneway");
            p.SetRoad(1, 3, RoadForm.Dual, CrossWidth, 4f, "Cross Oneway");
            p.Levels[0].Elevation = baseElevation + (mainOnTop ? 10f : 0f);
            p.Levels[1].Elevation = baseElevation + (mainOnTop ? 0f : 10f);
            return p;
        }

        public static IEnumerable<object[]> Cases() =>
            from angle in new[] { 90f, 75f, 105f, 60f, 120f }
            from mainOnTop in new[] { true, false }
            select new object[] { angle, mainOnTop };

        [Theory]
        [MemberData(nameof(Cases))]
        public void Generated_IsValidAndConnected(float angle, bool mainOnTop)
        {
            var p = Make(angle, mainOnTop);
            Assert.Empty(_template.ValidateParams(p));
            var segments = _template.Generate(p);
            Assert.Empty(InterchangeLayout.ValidateGeometry(segments, p));
            TestHelpers.AssertRampsConnected(segments);
            // 两条路各两端、各两幅
            Assert.Equal(8, InterchangeLayout.LegEnds(segments).Count);
            // 四个象限各一条环道和一条外侧匝道
            Assert.Equal(4, segments.Select(s => s.Name).Where(n => n.StartsWith("Loop")).Select(n => n.Split('-')[0]).Distinct().Count());
            Assert.Equal(4, segments.Select(s => s.Name).Where(n => n.StartsWith("Outer")).Select(n => n.Split('-')[0]).Distinct().Count());
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void NoTwoRoadsCrossAtGrade(float angle, bool mainOnTop)
        {
            // 任意两段不共用节点的路，俯视下靠得比两者半宽之和还近时，高差必须足够让一条从另一条上面过
            var p = Make(angle, mainOnTop);
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
                            Assert.False(close && System.Math.Abs(u.Y - v.Y) < 7f, $"{a.Name} and {b.Name} cross at grade near ({u.X:F0}, {u.Z:F0})");
                        }
                }
            }
        }

        [Fact]
        public void LoopsAre270DegreeTurnsBetweenTheTwoLevels()
        {
            var p = Make();
            var segments = _template.Generate(p);
            foreach (var group in segments.Where(s => s.Name.StartsWith("Loop")).GroupBy(s => s.Name.Split('-')[0]))
            {
                var pieces = group.OrderBy(s => int.Parse(s.Name.Split('-')[1])).ToList();
                Vec3 start = Vec3.Normalize(pieces[0].Curve.Derivative(0f)), end = Vec3.Normalize(pieces[pieces.Count - 1].Curve.Derivative(1f));
                Assert.True(System.Math.Abs(Directions.Dot(new Vec3(start.X, 0f, start.Z), new Vec3(end.X, 0f, end.Z))) < 0.05f);
                float h0 = pieces[0].Curve.P0.Y, h1 = pieces[pieces.Count - 1].Curve.P3.Y;
                Assert.Equal(10f, System.Math.Abs(h1 - h0), 2);
            }
        }

        [Fact]
        public void AutoLoopRadius_IsTheSmallestThatFits()
        {
            var p = Make();
            float auto = CloverleafTemplate.ResolveLoopRadius(p);
            var smaller = Make();
            smaller.LoopRadius = auto - 5f;
            if (auto - 5f >= 30f)
                Assert.Contains(_template.ValidateParams(smaller), e => e.Contains("环道半径至少"));
            var manual = Make();
            manual.LoopRadius = auto;
            Assert.Empty(_template.ValidateParams(manual));
        }

        [Fact]
        public void LaneBalance_ReducedOutsideTheWeavingSection()
        {
            var p = Make();
            p.MainReducedPrefab = "Highway Oneway Reduced";
            p.MainReducedWidth = 12f;
            Assert.Empty(_template.ValidateParams(p));
            var segments = _template.Generate(p);
            Assert.Empty(InterchangeLayout.ValidateGeometry(segments, p));
            TestHelpers.AssertRampsConnected(segments);
            // 每幅主线两段减车道（外侧匝道驶出 → 环道汇入、环道驶出 → 外侧匝道汇入），交织段保持全宽
            var reduced = segments.Where(s => s.Prefab == "Highway Oneway Reduced").ToList();
            Assert.Equal(4, reduced.Count);
            Assert.All(reduced, s => Assert.True(s.HasNodeOffset));
        }

        [Fact]
        public void ElevatedBase_RampOverlapsWithViaductsStayShort()
        {
            var p = Make(baseElevation: 10f);
            Assert.Empty(_template.ValidateParams(p));
            var segments = _template.Generate(p);
            Assert.Empty(InterchangeLayout.ValidateGeometry(segments, p));
            TestHelpers.AssertRampsConnected(segments);
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void HugLoop_SharedTwoWayRampsAreValidAndConnected(float angle, bool mainOnTop)
        {
            // 右转匝道贴环道：每个象限一条双向匝道（环道 + 右转匝道共用），两端边缘对齐分开
            var p = Make(angle, mainOnTop);
            p.OuterHugsLoop = true;
            p.SharedRampPrefab = "Highway Twoway";
            p.SharedRampWidth = 12f;
            Assert.Empty(_template.ValidateParams(p));
            var segments = _template.Generate(p);
            Assert.Empty(InterchangeLayout.ValidateGeometry(segments, p));
            TestHelpers.AssertRampsConnected(segments);
            Assert.Equal(8, InterchangeLayout.LegEnds(segments).Count);
            var shared = segments.Where(s => s.Prefab == "Highway Twoway").ToList();
            Assert.Equal(4, shared.Select(s => s.Name.Split('-')[0]).Distinct().Count());
            // 双向匝道两端各接一段环道和一段右转匝道，它们的曲线端点从节点向两侧错开 W/4
            foreach (var group in shared.GroupBy(s => s.Name.Split('-')[0]))
            {
                var ends = new[] { group.First().StartNode, group.Last().EndNode };
                foreach (var node in ends)
                {
                    var touching = segments.Where(s => s.Prefab != "Highway Twoway" && (s.StartNode == node || s.EndNode == node)).ToList();
                    Assert.Equal(2, touching.Count);
                    foreach (var s in touching)
                    {
                        Vec3 end = s.StartNode == node ? s.Curve.P0 : s.Curve.P3;
                        Assert.Equal(3f, Vec3.Distance(node, end), 1);
                    }
                }
            }
        }

        [Theory]
        [InlineData(90f)]
        [InlineData(65f)]
        public void ElevatedRampsKeepClearOfEachOther(float angle)
        {
            // 游戏内实测：环道与同一象限的外侧匝道在半空只隔约一条匝道宽时报“与现有物体重叠”。
            // 不共用节点、都在半空（离地 2 米以上）的两段匝道，中心线至少相距 匝道宽 + 匝道间距 + 6 米
            var p = Make(angle);
            p.RampSideGap = 1f;
            var segments = _template.Generate(p).Where(s => s.Role == RoadRole.Ramp).ToList();
            float need = RampWidth + p.RampSideGap + 6f - 0.5f;
            var samples = segments.Select(s => Enumerable.Range(0, 21).Select(i => s.Curve.Evaluate(i / 20f)).ToArray()).ToArray();
            for (int i = 0; i < segments.Count; i++)
                for (int j = i + 1; j < segments.Count; j++)
                {
                    var nodes = new[] { segments[i].StartNode, segments[i].EndNode };
                    if (nodes.Contains(segments[j].StartNode) || nodes.Contains(segments[j].EndNode))
                        continue;
                    foreach (var u in samples[i].Where(v => v.Y > 2f))
                        foreach (var v in samples[j].Where(v => v.Y > 2f))
                        {
                            float dx = u.X - v.X, dz = u.Z - v.Z;
                            Assert.False(dx * dx + dz * dz < need * need && System.Math.Abs(u.Y - v.Y) < 7f,
                                $"{segments[i].Name} and {segments[j].Name} too close near ({u.X:F0}, {u.Z:F0})");
                        }
                }
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void CollectorDistributor_RampsOnParallelRoads(float angle, bool mainOnTop)
        {
            // 集散车道：主线每幅外侧一条平行单行道路，四条匝道都接在它上面；主线只有一处驶出、一处汇入，其间减车道
            var p = Make(angle, mainOnTop);
            p.CollectorDistributor = true;
            p.RampPrefab = "Ramp Oneway";
            p.MainReducedPrefab = "Highway Oneway Reduced";
            p.MainReducedWidth = 12f;
            Assert.Empty(_template.ValidateParams(p));
            var segments = _template.Generate(p);
            Assert.Empty(InterchangeLayout.ValidateGeometry(segments, p));
            TestHelpers.AssertRampsConnected(segments);
            Assert.Equal(8, InterchangeLayout.LegEnds(segments).Count);
            Assert.Contains(segments, s => s.Name.StartsWith("CD+") && s.Prefab == "Ramp Oneway");
            Assert.Contains(segments, s => s.Name.StartsWith("CD-") && s.Prefab == "Ramp Oneway");
            // 两幅主线在集散车道驶出到汇入之间减车道
            Assert.Contains(segments, s => s.Name.StartsWith("Main+") && s.Prefab == "Highway Oneway Reduced");
            Assert.Contains(segments, s => s.Name.StartsWith("Main-") && s.Prefab == "Highway Oneway Reduced");
            var mainNodes = segments.Where(s => s.Name.StartsWith("Main")).SelectMany(s => new[] { s.StartNode, s.EndNode }).ToList();
            // 主线上只接着集散车道的 S 形连接段，没有别的匝道
            var onMain = segments.Where(s => s.Role == RoadRole.Ramp && (mainNodes.Contains(s.StartNode) || mainNodes.Contains(s.EndNode))).ToList();
            Assert.Equal(4, onMain.Count);
            Assert.All(onMain, s => Assert.StartsWith("CD", s.Name));
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void CollectorDistributor_OnBothRoads(float angle, bool mainOnTop)
        {
            // 两条路都是快速路：主线和被交道路外侧都有集散车道，四条匝道两头都接在集散车道上，两条路本身只接集散车道的 S 形连接段
            var p = Make(angle, mainOnTop);
            p.CollectorDistributor = true;
            p.CrossCollectorDistributor = true;
            p.RampPrefab = "Ramp Oneway";
            Assert.Empty(_template.ValidateParams(p));
            var segments = _template.Generate(p);
            Assert.Empty(InterchangeLayout.ValidateGeometry(segments, p));
            TestHelpers.AssertRampsConnected(segments);
            Assert.Equal(8, InterchangeLayout.LegEnds(segments).Count);
            foreach (var prefix in new[] { "CD+", "CD-", "CDX+", "CDX-" })
                Assert.Contains(segments, s => s.Name.StartsWith(prefix));
            var roadNodes = segments.Where(s => s.Name.StartsWith("Main") || s.Name.StartsWith("Cross")).SelectMany(s => new[] { s.StartNode, s.EndNode }).ToList();
            var onRoads = segments.Where(s => s.Role == RoadRole.Ramp && (roadNodes.Contains(s.StartNode) || roadNodes.Contains(s.EndNode))).ToList();
            Assert.Equal(8, onRoads.Count);
            Assert.All(onRoads, s => Assert.StartsWith("CD", s.Name));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void CollectorConnectors_LeaveTheRoadClearly(bool mainOnTop)
        {
            // 集散车道两头的 S 形连接段：终点（集散车道那头）离所属道路那一幅中心线的横向距离，
            // 要比两路刚好不重叠的距离（半宽之和）多出至少 4 米，连接段才有一截不在并行重叠（节点区）里
            var p = Make(90f, mainOnTop);
            p.CollectorDistributor = true;
            p.CrossCollectorDistributor = true;
            p.RampPrefab = "Ramp Oneway";
            var segments = _template.Generate(p);
            foreach (var s in segments.Where(s => s.Name.StartsWith("CD") && (s.Name.Contains("in-") || s.Name.Contains("out-"))))
            {
                bool isMain = s.Name.StartsWith("CD+") || s.Name.StartsWith("CD-");
                float roadWidth = isMain ? MainWidth : CrossWidth;
                Vec3 onRoad = s.Name.Contains("in-") ? s.StartNode : s.EndNode;
                Vec3 onCollector = s.Name.Contains("in-") ? s.Curve.P3 : s.Curve.P0;
                Vec3 dir = isMain ? p.Legs[0].Direction : p.Legs[1].Direction;
                Vec3 d = onCollector - onRoad;
                float lateral = System.Math.Abs(dir.X * d.Z - dir.Z * d.X);
                Assert.True(lateral >= roadWidth / 2f + RampWidth / 2f + 4f, $"{s.Name}: {lateral:F1} m");
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void ElevatedBase_CollectorConnectorsShorterThanSplitLength(bool mainOnTop)
        {
            // 下层 10 米、上层 20 米，两条路都有集散车道：两头 S 形连接段（整段一节、半空）要短于游戏切分高架路段的长度
            var p = Make(90f, mainOnTop, baseElevation: 10f);
            p.CollectorDistributor = true;
            p.CrossCollectorDistributor = true;
            p.RampPrefab = "Ramp Oneway";
            Assert.Empty(_template.ValidateParams(p));
            var segments = _template.Generate(p);
            Assert.Empty(InterchangeLayout.ValidateGeometry(segments, p));
            TestHelpers.AssertRampsConnected(segments);
            var connectors = segments.Where(s => s.Name.StartsWith("CD") && (s.Name.Contains("in-") || s.Name.Contains("out-"))).ToList();
            Assert.Equal(8, connectors.Count);
            Assert.All(connectors, s => Assert.True(s.Curve.Length() < p.Legs[0].MaxElevatedEdgeLength - 5f, $"{s.Name} {s.Curve.Length():F0} m"));
        }

        [Theory]
        [InlineData(61f, true)]
        [InlineData(61f, false)]
        [InlineData(90f, true)]
        [InlineData(120f, false)]
        public void SkewedCrossingOverCollectors_PillarsFit(float angle, bool mainOnTop)
        {
            // 游戏内实测：斜交时上层道路的门式立柱（长约 20 米）压到下层的集散车道。两条路都有集散车道、立柱按实际长度时也要放得下
            var p = Make(angle, mainOnTop);
            p.CollectorDistributor = true;
            p.CrossCollectorDistributor = true;
            p.RampPrefab = "Ramp Oneway";
            foreach (var leg in p.Legs)
                leg.PillarWidth = 20f;
            Assert.Empty(_template.ValidateParams(p));
            var segments = _template.Generate(p);
            Assert.Empty(InterchangeLayout.ValidateGeometry(segments, p));
            TestHelpers.AssertRampsConnected(segments);
        }

        [Fact]
        public void IsRegistered()
        {
            Assert.IsType<CloverleafTemplate>(TemplateRegistry.Get(InterchangeType.Cloverleaf));
        }
    }
}
