using System.Collections.Generic;
using System.Linq;
using InterchangeMod.Generation.Geometry;
using InterchangeMod.Generation.Templates;
using Xunit;

namespace InterchangeMod.Generation.Tests
{
    public class DirectionalTTemplateTests
    {
        private const float MainWidth = 16f, BranchWidth = 12f, RampWidth = 8f;

        /// <summary>双幅公路主线、双幅支路，都在地面；层间高差 10 米（第一层跨线桥）。</summary>
        private static InterchangeParams Make(float branchAngle = 90f, bool twoLevel = false, float baseElevation = 0f)
        {
            var p = new InterchangeParams
            {
                Legs = InterchangeParams.TLegs(branchAngle, 0, 0),
                RampWidth = RampWidth,
                DirectionalTwoLevel = twoLevel,
            };
            p.SetRoad(0, 2, RoadForm.Dual, MainWidth, 4f, "Highway Oneway");
            p.SetRoad(1, 1, RoadForm.Dual, BranchWidth, 4f, "Branch Oneway");
            p.Levels[0].Elevation = baseElevation;
            p.Levels[1].Elevation = baseElevation + 10f;
            return p;
        }

        public static IEnumerable<object[]> Cases() =>
            from directional in new[] { false, true }
            from angle in new[] { 90f, 75f, 105f, 60f, 120f }
            from twoLevel in new[] { false, true }
            select new object[] { directional, angle, twoLevel };

        [Theory]
        [MemberData(nameof(Cases))]
        public void Generated_IsValidAndConnected(bool directional, float angle, bool twoLevel)
        {
            var template = new DirectionalTTemplate(directional);
            var p = Make(angle, twoLevel);
            Assert.Empty(template.ValidateParams(p));
            var segments = template.Generate(p);
            Assert.Empty(InterchangeLayout.ValidateGeometry(segments, p));
            TestHelpers.AssertRampsConnected(segments);
            // 主线两幅各两端 + 支路两幅外端
            Assert.Equal(6, InterchangeLayout.LegEnds(segments).Count);
            // 高架路段短于游戏切分长度（80 米），否则切分点的立柱位置不受控制
            foreach (var s in segments.Where(s => s.Curve.Evaluate(0.5f).Y > 7.5f))
                Assert.True(s.Curve.Length() <= 80f, $"{s.Name} is {s.Curve.Length():F0} m");
            // 坡度不超过上限
            foreach (var s in segments)
            {
                float rise = System.Math.Abs(s.Curve.Evaluate(1f).Y - s.Curve.Evaluate(0f).Y);
                Assert.True(rise <= p.MaxGrade * s.Curve.Length() + 0.05f, $"{s.Name} too steep");
            }
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void NoRampNodeInsideTheOverlapWithAMainCarriageway(bool directional, float angle, bool twoLevel)
        {
            // 匝道与主线并行重叠的那段要整段：中间有节点时游戏报“形状无效 / 重叠”（S 形离开远侧幅时曾在 S 形中点断开）
            var p = Make(angle, twoLevel);
            var segments = new DirectionalTTemplate(directional).Generate(p);
            var mainPoints = segments.Where(s => s.Role == RoadRole.MainRoad)
                .SelectMany(s => Enumerable.Range(0, 41).Select(i => s.Curve.Evaluate(i / 40f))).ToList();
            var mainNodes = segments.Where(s => s.Role == RoadRole.MainRoad).SelectMany(s => new[] { s.StartNode, s.EndNode }).ToHashSet();
            foreach (var ramp in segments.Where(s => s.Role == RoadRole.Ramp))
            {
                foreach (var node in new[] { ramp.StartNode, ramp.EndNode })
                {
                    if (mainNodes.Contains(node) || System.Math.Abs(node.Y) > 1f)
                        continue;
                    float nearest = mainPoints.Min(v => (v.X - node.X) * (v.X - node.X) + (v.Z - node.Z) * (v.Z - node.Z));
                    Assert.True(System.MathF.Sqrt(nearest) >= (MainWidth + RampWidth) / 2f,
                        $"{ramp.Name} has a node {System.MathF.Sqrt(nearest):F1} m from a main carriageway");
                }
            }
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void NoTwoRoadsCrossAtGrade(bool directional, float angle, bool twoLevel)
        {
            var template = new DirectionalTTemplate(directional);
            var p = Make(angle, twoLevel);
            var segments = template.Generate(p);
            float Width(RoadSegmentSpec s) => s.Role == RoadRole.MainRoad ? MainWidth : s.Role == RoadRole.CrossRoad ? BranchWidth : RampWidth;
            // 匝道接主线、支路的节点附近本来就挨着，不算
            var rampNodes = segments.Where(s => s.Role == RoadRole.Ramp).SelectMany(s => new[] { s.StartNode, s.EndNode })
                .Where(n => segments.Any(s => s.Role != RoadRole.Ramp && (s.StartNode == n || s.EndNode == n))).ToList();
            bool NearAttach(Vec3 v) => rampNodes.Any(t => (t.X - v.X) * (t.X - v.X) + (t.Z - v.Z) * (t.Z - v.Z) < 60f * 60f && System.Math.Abs(t.Y - v.Y) < 1f);
            var samples = segments.Select(s => Enumerable.Range(0, 41).Select(i => s.Curve.Evaluate(i / 40f)).Where(v => !NearAttach(v)).ToArray()).ToArray();
            for (int i = 0; i < segments.Count; i++)
            {
                for (int j = i + 1; j < segments.Count; j++)
                {
                    var a = segments[i];
                    var b = segments[j];
                    var nodesA = new[] { a.StartNode, a.EndNode };
                    if (nodesA.Contains(b.StartNode) || nodesA.Contains(b.EndNode))
                        continue;
                    // 同一条匝道（或同一幅道路）的相邻路段隔着一小段时采样点也挨得近
                    if (a.Name.TrimEnd("0123456789-".ToCharArray()) == b.Name.TrimEnd("0123456789-".ToCharArray()))
                        continue;
                    float reach = (Width(a) + Width(b)) / 2f;
                    foreach (var u in samples[i])
                        foreach (var v in samples[j])
                        {
                            float dx = u.X - v.X, dz = u.Z - v.Z;
                            Assert.False(dx * dx + dz * dz < reach * reach && System.Math.Abs(u.Y - v.Y) < 7f,
                                $"{a.Name} and {b.Name} cross at grade near ({u.X:F0}, {u.Y:F1}, {u.Z:F0})");
                        }
                }
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void LeftTurnRampsCrossAtDifferentLevels(bool twoLevel)
        {
            // 三层时支路 → 远侧幅那条在上面（20 米）跨过另一条；两层时它以隧道（至少 13 米深）从主线下面穿过
            var segments = new DirectionalTTemplate(false).Generate(Make(twoLevel: twoLevel));
            var toFar = segments.Where(s => s.Name.StartsWith("BranchToFar")).ToList();
            float extreme = twoLevel ? toFar.Min(s => s.Curve.Evaluate(0.5f).Y) : toFar.Max(s => s.Curve.Evaluate(0.5f).Y);
            Assert.Equal(twoLevel ? -13f : 20f, extreme, 0);
            Assert.Equal(10f, segments.Where(s => s.Name.StartsWith("FarToBranch")).Max(s => s.Curve.Evaluate(0.5f).Y), 0);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void RightTurnRampsShareTheBranchNodesAndAreSymmetric(bool directional)
        {
            // 支路进向一幅在一个节点分成右转匝道和左转匝道，出向一幅在一个节点合二为一；两侧节点离中心一样远，右转匝道一样长
            var segments = new DirectionalTTemplate(directional).Generate(Make());
            Vec3 outNode = segments.Where(s => s.Name.StartsWith("FarToBranch")).Last().EndNode;
            Vec3 inNode = segments.First(s => s.Name.StartsWith("BranchToFar")).StartNode;
            Assert.Equal(outNode, segments.Where(s => s.Name.StartsWith("NearToBranch")).Last().EndNode);
            Assert.Equal(inNode, segments.First(s => s.Name.StartsWith("BranchToNear")).StartNode);
            // 两条匝道并成支路一幅：曲线端点在节点两侧各横移 (支路宽 − 匝道宽) / 2 = 2 米，外缘分别对齐支路两侧边缘
            Vec3 leftEnd = segments.Where(s => s.Name.StartsWith("FarToBranch")).Last().Curve.Evaluate(1f);
            Vec3 rightEnd = segments.Where(s => s.Name.StartsWith("NearToBranch")).Last().Curve.Evaluate(1f);
            Assert.Equal(2f, Vec3.Distance(leftEnd, outNode), 1);
            Assert.Equal(2f, Vec3.Distance(rightEnd, outNode), 1);
            Assert.Equal(4f, Vec3.Distance(leftEnd, rightEnd), 1);
            float Length(string prefix) => segments.Where(s => s.Name.StartsWith(prefix)).Sum(s => s.Curve.Length());
            Assert.Equal(Length("NearToBranch"), Length("BranchToNear"), 0);
        }

        [Fact]
        public void Directional_WidensTheMedianForTheLeftTurnRamps()
        {
            // 定向：远侧幅在立交范围内向外横移，匝道在拉开的分隔带里进出
            var segments = new DirectionalTTemplate(true).Generate(Make());
            var far = segments.Where(s => s.Name.StartsWith("MainFar")).ToList();
            float spread = far.Max(s => System.Math.Abs(s.Curve.Evaluate(0.5f).Z)) - far.Min(s => System.Math.Abs(s.Curve.Evaluate(0.5f).Z));
            Assert.True(spread > 5f, $"far carriageway shifts only {spread:F1} m");
        }

        [Theory]
        [InlineData(false, 90f)]
        [InlineData(true, 90f)]
        [InlineData(false, 75f)]
        [InlineData(true, 105f)]
        public void ElevatedBase_IsValid(bool directional, float angle)
        {
            // 对准两层都在高架上的立体交叉时，基层（主线、支路）本身是高架（10 米），跨线桥在 20、30 米
            var template = new DirectionalTTemplate(directional);
            var p = Make(angle, baseElevation: 10f);
            Assert.Empty(template.ValidateParams(p));
            var segments = template.Generate(p);
            Assert.Empty(InterchangeLayout.ValidateGeometry(segments, p));
            TestHelpers.AssertRampsConnected(segments);
            foreach (var s in segments)
                Assert.True(s.Curve.Length() <= 80f, $"{s.Name} is {s.Curve.Length():F0} m on an elevated base");
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void StackedCrossingIsRejected(bool directional)
        {
            // 只用于主线和支路同层的路口：对准立体交叉（支路原来在 20 米、主线 10 米）时提示改用喇叭形
            var p = Make(baseElevation: 10f);
            p.Legs[1].ExternalElevation = 20f;
            Assert.Contains(new DirectionalTTemplate(directional).ValidateParams(p), e => e.Contains("喇叭形"));
        }

        [Theory]
        [InlineData(false, 90f)]
        [InlineData(true, 90f)]
        [InlineData(false, 70f)]
        [InlineData(true, 110f)]
        public void LaneBalance_MainDropsLanesBetweenExitAndEntryAndRampsJoinInParallel(bool directional, float angle)
        {
            // 车道平衡：近侧幅在右转匝道驶出点和汇入点之间、远侧幅在左转匝道驶出点和汇入点之间改用减车道的道路；
            // 匝道在主线一端平行接入（曲线端点在主线外缘处，节点是主线的节点）
            var p = Make(angle);
            p.MainReducedPrefab = "Highway Reduced";
            p.MainReducedWidth = 12f;
            var template = new DirectionalTTemplate(directional);
            Assert.Empty(template.ValidateParams(p));
            var segments = template.Generate(p);
            Assert.Empty(InterchangeLayout.ValidateGeometry(segments, p));
            TestHelpers.AssertRampsConnected(segments);
            Assert.Contains(segments, s => s.Name.StartsWith("MainNear") && s.Prefab == "Highway Reduced");
            Assert.Contains(segments, s => s.Name.StartsWith("MainFar") && s.Prefab == "Highway Reduced");
            var mainNodes = segments.Where(s => s.Role == RoadRole.MainRoad).SelectMany(s => new[] { s.StartNode, s.EndNode }).ToHashSet();
            foreach (var name in new[] { "NearToBranch", "BranchToNear", "FarToBranch", "BranchToFar" })
            {
                var pieces = segments.Where(s => s.Name.StartsWith(name)).ToList();
                var atMain = pieces.First(s => mainNodes.Contains(s.StartNode) || mainNodes.Contains(s.EndNode));
                bool start = mainNodes.Contains(atMain.StartNode);
                Vec3 node = start ? atMain.StartNode : atMain.EndNode, end = atMain.Curve.Evaluate(start ? 0f : 1f);
                // 曲线端点离节点 (主线宽 − 匝道宽) / 2 = 4 米（匝道外缘对齐主线外缘）
                Assert.Equal(4f, Vec3.Distance(node, end), 1);
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void NarrowBranch_LeftTurnRampsKeepRoomForEmbankments(bool directional)
        {
            // 窄支路（每幅 8 米、分隔带 2 米）配 12 米宽的匝道：立交内支路两幅拉开，两条左转匝道沿支路升降时
            // 中线相距至少一个匝道宽加两侧路堤余量；支路外端 S 形并回原来的分隔带
            var p = Make();
            p.RampWidth = 12f;
            p.SetRoad(1, 1, RoadForm.Dual, 8f, 2f, "Branch Oneway");
            var template = new DirectionalTTemplate(directional);
            Assert.Empty(template.ValidateParams(p));
            var segments = template.Generate(p);
            Assert.Empty(InterchangeLayout.ValidateGeometry(segments, p));
            TestHelpers.AssertRampsConnected(segments);
            Vec3 aEnd = segments.Where(s => s.Name.StartsWith("FarToBranch")).Last().EndNode;
            Vec3 bStart = segments.First(s => s.Name.StartsWith("BranchToFar")).StartNode;
            Assert.True(Vec3.Distance(aEnd, bStart) >= 12f + 16f - 0.1f, $"left-turn ramps only {Vec3.Distance(aEnd, bStart):F1} m apart");
            Assert.Contains(segments, s => s.Name == "BranchOut-merge");
        }

        [Fact]
        public void AreRegistered()
        {
            Assert.IsType<DirectionalTTemplate>(TemplateRegistry.Get(InterchangeType.DirectionalT));
            Assert.IsType<DirectionalTTemplate>(TemplateRegistry.Get(InterchangeType.SemiDirectionalT));
        }
    }
}
