using System.Collections.Generic;
using System.Linq;
using InterchangeMod.Generation.Geometry;
using InterchangeMod.Generation.Templates;
using Xunit;

namespace InterchangeMod.Generation.Tests
{
    public class TrumpetTemplateTests
    {
        private readonly TrumpetTemplate _template = new TrumpetTemplate();

        private const float MainWidth = 16f, BranchWidth = 12f, RampWidth = 8f;

        /// <summary>双幅公路主线在地面，双幅支路跨线桥 10 米；branchUnder 时反过来：主线高架 10 米，支路在地面从下面穿过。</summary>
        private static InterchangeParams Make(float branchAngle = 90f, bool loopForExit = true, bool branchUnder = false)
        {
            var p = new InterchangeParams
            {
                Legs = InterchangeParams.TLegs(branchAngle, 0, 1),
                RampWidth = RampWidth,
                TrumpetLoopForExit = loopForExit,
            };
            p.SetRoad(0, 2, RoadForm.Dual, MainWidth, 4f, "Highway Oneway");
            p.SetRoad(1, 1, RoadForm.Dual, BranchWidth, 6f, "Branch Oneway");
            p.Levels[0].Elevation = branchUnder ? 10f : 0f;
            p.Levels[1].Elevation = branchUnder ? 0f : 10f;
            // 斜交较大时默认环道半径可能放不下：按面板提示加大（玩家也会这样做）
            while (p.LoopRadius < 250f && new TrumpetTemplate().ValidateParams(p).Any(e => e.Contains("环道半径至少")))
                p.LoopRadius += 5f;
            return p;
        }

        public static IEnumerable<object[]> Cases() =>
            from angle in new[] { 90f, 270f, 80f, 100f, 260f, 60f, 120f, 240f, 300f, 35f, 145f, 215f, 325f }
            from loopForExit in new[] { true, false }
            from branchUnder in new[] { false, true }
            select new object[] { angle, loopForExit, branchUnder };

        [Theory]
        [MemberData(nameof(Cases))]
        public void Generated_IsValidAndConnected(float branchAngle, bool loopForExit, bool branchUnder)
        {
            var p = InterchangeLayout.FitRampLength(_template, Make(branchAngle, loopForExit, branchUnder));
            Assert.Empty(_template.ValidateParams(p));
            var segments = _template.Generate(p);
            Assert.Empty(InterchangeLayout.ValidateGeometry(segments, p));
            TestHelpers.AssertRampsConnected(segments);
            // 主线两端各两幅 + 支路外端两幅
            Assert.Equal(6, InterchangeLayout.LegEnds(segments).Count);
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void NoTwoRoadsCrossAtGrade(float branchAngle, bool loopForExit, bool branchUnder)
        {
            // 任意两段不共用节点的路，俯视下靠得比两者半宽之和还近时，高差必须足够让一条从另一条上面过
            var p = Make(branchAngle, loopForExit, branchUnder);
            var segments = _template.Generate(p);
            float Width(RoadSegmentSpec s) => s.Role == RoadRole.MainRoad ? MainWidth : s.Role == RoadRole.CrossRoad ? BranchWidth : RampWidth;
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

        private static List<RoadSegmentSpec> Pieces(IEnumerable<RoadSegmentSpec> segments, string name) =>
            segments.Where(s => s.Name.StartsWith(name) && char.IsDigit(s.Name[name.Length])).OrderBy(s => int.Parse(s.Name.Substring(name.Length))).ToList();

        [Fact]
        public void LoopIsA270DegreeRightTurn_LeavingMainTangentially()
        {
            var p = Make();
            var segments = _template.Generate(p);
            var loop = Pieces(segments, "Loop");
            Vec3 start = Vec3.Normalize(loop[0].Curve.Derivative(0f));
            Vec3 end = Vec3.Normalize(loop[loop.Count - 1].Curve.Derivative(1f));
            // 270° 后与起点方向垂直
            Assert.True(System.Math.Abs(Directions.Dot(new Vec3(start.X, 0f, start.Z), new Vec3(end.X, 0f, end.Z))) < 0.05f);
            // 环道直接从主线节点外切驶出（没有横移段），起点方向与主线平行
            var mainFar = segments.Where(s => s.Role == RoadRole.MainRoad).ToList();
            Assert.Contains(mainFar, s => s.StartNode == loop[0].Curve.P0 || s.EndNode == loop[0].Curve.P0);
            Assert.Contains(mainFar, s => System.Math.Abs(Directions.Dot(Vec3.Normalize(s.Curve.Derivative(0.5f)), start)) > 0.999f);
            // 环道爬升到跨线桥高度；与主线并行重叠的起始一段保持主线高度
            Assert.Equal(0f, loop[0].Curve.P0.Y, 3);
            Assert.Equal(0f, loop[0].Curve.P3.Y, 3);
            Assert.Equal(10f, loop[loop.Count - 1].Curve.P3.Y, 3);
        }

        [Fact]
        public void RampsStayWithinMaxGrade_WhenBranchPassesUnder()
        {
            var p = Make(branchUnder: true);
            foreach (var s in _template.Generate(p).Where(s => s.Role == RoadRole.Ramp))
            {
                // 按 40 段折线估算最陡处坡度
                for (int i = 0; i < 40; i++)
                {
                    Vec3 a = s.Curve.Evaluate(i / 40f), b = s.Curve.Evaluate((i + 1) / 40f);
                    float run = MathF.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Z - a.Z) * (b.Z - a.Z));
                    if (run > 0.5f)
                        Assert.True(System.Math.Abs(b.Y - a.Y) / run <= p.MaxGrade * 1.25f, $"{s.Name} too steep at t={i / 40f:F2}");
                }
            }
        }

        [Fact]
        public void SemiDirectWrapsLoopConcentrically()
        {
            // 半定向匝道的 180° 圆弧与环道同心，间距 = 支路两幅的间距（2 × 幅偏移）
            var p = Make();
            var segments = _template.Generate(p);
            float ob = p.Legs[1].CarriagewayOffset;
            var loop = Pieces(segments, "Loop").Select(s => s.Curve).ToList();
            var semi = Pieces(segments, "SemiDirect").Select(s => s.Curve).ToList();
            // 环道圆心：环道最后一段（270° 圆弧上）的三个点求外接圆
            Vec3 Flat(Vec3 v) => new Vec3(v.X, 0f, v.Z);
            var last = loop[loop.Count - 1];
            Vec3 a = Flat(last.P0), b = Flat(last.Evaluate(0.5f)), c3 = Flat(last.P3);
            float d = 2f * (a.X * (b.Z - c3.Z) + b.X * (c3.Z - a.Z) + c3.X * (a.Z - b.Z));
            float a2 = a.X * a.X + a.Z * a.Z, b2 = b.X * b.X + b.Z * b.Z, c2 = c3.X * c3.X + c3.Z * c3.Z;
            Vec3 center = new Vec3((a2 * (b.Z - c3.Z) + b2 * (c3.Z - a.Z) + c2 * (a.Z - b.Z)) / d, 0f,
                (a2 * (c3.X - b.X) + b2 * (a.X - c3.X) + c2 * (b.X - a.X)) / d);
            float loopRadius = Vec3.Distance(a, center);
            Assert.Equal(p.LoopRadius, loopRadius, 0);
            // 半定向匝道前两段（180° 圆弧）离环道圆心的距离都约等于 环道半径 + 2·ob
            foreach (var c in semi.Take(2))
                foreach (float t in new[] { 0f, 0.5f, 1f })
                    Assert.InRange(Vec3.Distance(Flat(c.Evaluate(t)), center), loopRadius + 2f * ob - 2f, loopRadius + 2f * ob + 2f);
        }

        [Theory]
        [InlineData(80f, true)]
        [InlineData(100f, true)]
        [InlineData(80f, false)]
        [InlineData(100f, false)]
        [InlineData(60f, true)]
        [InlineData(120f, false)]
        public void SkewedBranch_TailEndsOnTheBranchLine(float branchAngle, bool loopForExit)
        {
            // 近侧支路沿支路真实方向：外端在穿过立交中心、沿支路方向的直线上（横向偏差很小），才接得回原有道路
            var p = Make(branchAngle, loopForExit);
            var segments = _template.Generate(p);
            Vec3 dir = p.Legs[1].Direction;
            var ends = InterchangeLayout.LegEnds(segments);
            // 支路外端：离主线（沿主线方向的直线）最远的那些臂端
            Vec3 mainDir = p.Legs[0].Direction;
            var branchEnds = ends.Where(e => System.Math.Abs(mainDir.X * e.Z - mainDir.Z * e.X) > 150f).ToList();
            Assert.Equal(2, branchEnds.Count);
            float ob = p.Legs[1].CarriagewayOffset;
            foreach (var e in branchEnds)
            {
                float lateral = System.Math.Abs(dir.X * e.Z - dir.Z * e.X);
                Assert.InRange(lateral, ob - 6f, ob + 6f);
            }
        }

        [Fact]
        public void ManualNearRampRadius_BelowMinimum_IsRejected()
        {
            var p = Make();
            p.TrumpetNearRampRadius = 40f;
            Assert.Contains(_template.ValidateParams(p), e => e.Contains("直连匝道半径至少"));
            p.TrumpetNearRampRadius = 600f;
            Assert.Empty(_template.ValidateParams(p));
            var segments = _template.Generate(p);
            Assert.Empty(InterchangeLayout.ValidateGeometry(segments, p));
            TestHelpers.AssertRampsConnected(segments);
        }

        [Fact]
        public void BranchUnder_MainDivergeStaysShortOfElevatedSplitLength()
        {
            // 主线高架时，直连匝道与主线并行重叠的那段（保持主线高度）要短于游戏切分高架路段的长度，否则切分点的立柱落在主线上
            var p = Make(branchUnder: true);
            var segments = _template.Generate(p);
            foreach (var name in new[] { "NearToBranch", "BranchToNear" })
            {
                var pieces = Pieces(segments, name);
                var atMain = name == "NearToBranch" ? pieces[0] : pieces[pieces.Count - 1];
                Assert.Equal(10f, atMain.Curve.P0.Y, 2);
                Assert.Equal(10f, atMain.Curve.P3.Y, 2);
                Assert.True(atMain.Curve.Length() < p.Legs[0].MaxElevatedEdgeLength, $"{atMain.Name} {atMain.Curve.Length():F0} m");
            }
        }

        [Fact]
        public void TwoWayBranch_EdgeAlignedSplit()
        {
            // 外部双向道路宽度正好等于两幅外缘之间的宽度：两幅平行接到同一个节点（曲线端点横移），外端接一段双向道路
            var p = Make();
            var branch = p.Legs[1];
            branch.ExternalForm = RoadForm.TwoWay;
            branch.ExternalPrefab = "Branch TwoWay";
            branch.ExternalWidth = 2f * branch.CarriagewayOffset + branch.RoadWidth;
            Assert.Empty(_template.ValidateParams(p));
            var segments = _template.Generate(p);
            Assert.Empty(InterchangeLayout.ValidateGeometry(segments, p));
            var tail = Assert.Single(segments, s => s.Prefab == "Branch TwoWay");
            var joined = segments.Where(s => s.Role == RoadRole.CrossRoad && s.HasNodeOffset).ToList();
            Assert.Equal(2, joined.Count);
            Assert.All(joined, s => Assert.True(s.EndNode == tail.StartNode || s.StartNode == tail.StartNode));
            Assert.DoesNotContain(segments, s => s.Name.Contains("merge"));
            // 主线两端各两幅 + 支路外端一条双向道路
            Assert.Equal(5, InterchangeLayout.LegEnds(segments).Count);
        }

        [Fact]
        public void LaneBalance_MainReducedBetweenRampNodes()
        {
            var p = Make();
            p.MainReducedPrefab = "Highway Oneway Reduced";
            p.MainReducedWidth = 12f;
            var segments = _template.Generate(p);
            Assert.Empty(InterchangeLayout.ValidateGeometry(segments, p));
            TestHelpers.AssertRampsConnected(segments);
            // 近侧、远侧各一段减车道，两端边缘对齐（曲线端点相对节点平移）
            var reduced = segments.Where(s => s.Prefab == "Highway Oneway Reduced").ToList();
            Assert.Equal(2, reduced.Count);
            Assert.All(reduced, s => Assert.True(s.HasNodeOffset));
            // 匝道在主线一端平行接入减车道后的主线外侧：曲线端点横移 (主线宽 − 匝道宽)/2，方向与主线平行
            var mainNodes = segments.Where(s => s.Role == RoadRole.MainRoad).SelectMany(s => new[] { s.StartNode, s.EndNode }).ToList();
            var atMain = segments.Where(s => s.Role == RoadRole.Ramp && (mainNodes.Contains(s.StartNode) || mainNodes.Contains(s.EndNode))).ToList();
            Assert.Equal(4, atMain.Count);
            foreach (var ramp in atMain)
            {
                bool start = mainNodes.Contains(ramp.StartNode);
                Vec3 node = start ? ramp.StartNode : ramp.EndNode, end = start ? ramp.Curve.P0 : ramp.Curve.P3;
                Assert.Equal((MainWidth - RampWidth) / 2f, Vec3.Distance(node, end), 2);
                Vec3 tangent = Vec3.Normalize(start ? ramp.Curve.Derivative(0f) : ramp.Curve.Derivative(1f));
                Assert.True(System.Math.Abs(Directions.Dot(tangent, p.Legs[0].Direction)) > 0.999f, $"{ramp.Name} not parallel to the main");
            }
        }

        [Fact]
        public void LeftHandTraffic_IsRejected()
        {
            var p = Make();
            p.RightHandTraffic = false;
            Assert.Contains(_template.ValidateParams(p), e => e.Contains("右侧通行"));
        }

        [Fact]
        public void IsRegistered()
        {
            Assert.IsType<TrumpetTemplate>(TemplateRegistry.Get(InterchangeType.Trumpet));
        }
    }
}
