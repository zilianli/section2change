using System.Collections.Generic;
using System.Linq;
using InterchangeMod.Generation.Geometry;
using InterchangeMod.Generation.Templates;
using Xunit;

namespace InterchangeMod.Generation.Tests
{
    /// <summary>臂端过渡段：立交内部双幅、外部双向时，两幅汇合成一个节点再接双向道路（F8b）。</summary>
    public class LegTransitionTests
    {
        private readonly DiamondTemplate _template = new DiamondTemplate();

        private static InterchangeParams MainDualInsideTwoWayOutside(bool dualCross = false)
        {
            var p = new InterchangeParams { RampLength = 260f };
            p.SetRoad(0, 2, RoadForm.Dual, 12f, 6f, "Oneway");
            foreach (int i in new[] { 0, 2 })
            {
                p.Legs[i].ExternalForm = RoadForm.TwoWay;
                p.Legs[i].ExternalPrefab = "TwoWay";
            }
            if (dualCross)
            {
                p.SetRoad(1, 3, RoadForm.Dual, 12f, 6f, "CrossOneway");
                foreach (int i in new[] { 1, 3 })
                {
                    p.Legs[i].ExternalForm = RoadForm.TwoWay;
                    p.Legs[i].ExternalPrefab = "CrossTwoWay";
                }
            }
            return p;
        }

        [Fact]
        public void DualInsideTwoWayOutside_IsValid()
        {
            var p = MainDualInsideTwoWayOutside();
            Assert.Empty(_template.ValidateParams(p));
            Assert.Empty(InterchangeLayout.ValidateGeometry(_template.Generate(p), p));
        }

        [Fact]
        public void EachEnd_HasTwoTransitionsAndATwoWayTail()
        {
            var segments = _template.Generate(MainDualInsideTwoWayOutside());
            foreach (string end in new[] { "Main-0", "Main-2" })
            {
                var parts = segments.Where(s => s.Name.StartsWith(end + "-")).ToList();
                Assert.Equal(3, parts.Count);
                var tail = parts.Single(s => s.Name.EndsWith("-tail"));
                Assert.Equal("TwoWay", tail.Prefab);
                // 两条过渡段都接在尾段靠立交一侧的端点（汇合点）
                Vec3 merge = end == "Main-0" ? tail.Curve.P0 : tail.Curve.P3;
                foreach (var transition in parts.Where(s => s != tail))
                {
                    Assert.Equal("Oneway", transition.Prefab);
                    Assert.True(transition.Curve.P0 == merge || transition.Curve.P3 == merge);
                }
            }
        }

        [Fact]
        public void Transitions_FollowTravelDirection()
        {
            // 主线沿 +Z；右侧通行：NB 那幅在东（+X）沿 +Z，SB 在西沿 -Z
            var c = _template.Generate(MainDualInsideTwoWayOutside()).ToDictionary(s => s.Name, s => s.Curve);
            Assert.True(c["Main-0-merge+"].P3.Z > c["Main-0-merge+"].P0.Z);   // NB 向北驶入汇合点
            Assert.True(c["Main-0-split-"].P3.Z < c["Main-0-split-"].P0.Z);   // SB 从汇合点向南驶出
            Assert.True(c["Main-2-split+"].P3.Z > c["Main-2-split+"].P0.Z);   // 南端：NB 从汇合点向北驶出
            Assert.True(c["Main-2-merge-"].P3.Z < c["Main-2-merge-"].P0.Z);   // 南端：SB 向南驶入汇合点
            Assert.True(c["Main-0-merge+"].P0.X > 0f);
        }

        [Fact]
        public void Carriageways_EndExactlyWhereTransitionsStart()
        {
            var segments = _template.Generate(MainDualInsideTwoWayOutside(dualCross: true));
            var transitionEnds = segments.Where(s => s.Name.Contains("-merge") || s.Name.Contains("-split"))
                .SelectMany(s => new[] { s.Curve.P0, s.Curve.P3 }).ToList();
            foreach (var prefix in new[] { "MainNB", "MainSB", "CrossPos", "CrossNeg" })
            {
                var chain = segments.Where(s => s.Name.StartsWith(prefix)).ToList();
                Assert.Contains(chain.First().Curve.P0, transitionEnds);
                Assert.Contains(chain.Last().Curve.P3, transitionEnds);
            }
        }

        [Fact]
        public void AllRoadEndpoints_AreConnectedExceptTailOuterEnds()
        {
            var segments = _template.Generate(MainDualInsideTwoWayOutside(dualCross: true));
            var endpoints = segments.SelectMany(s => new[] { s.Curve.P0, s.Curve.P3 }).ToList();
            var tails = segments.Where(s => s.Name.EndsWith("-tail")).ToList();
            Assert.Equal(4, tails.Count);
            var outerEnds = new HashSet<Vec3>();
            foreach (var tail in tails)
            {
                outerEnds.Add(tail.Name.Contains("-0-") || tail.Name.Contains("-1-") ? tail.Curve.P3 : tail.Curve.P0);
            }
            foreach (var point in endpoints.Distinct())
            {
                int uses = endpoints.Count(e => e == point);
                Assert.True(uses >= 2 || outerEnds.Contains(point), $"dangling endpoint {point}");
            }
        }

        [Fact]
        public void TwoWayInsideDualOutside_IsRejected()
        {
            var p = new InterchangeParams();
            p.Legs[0].ExternalForm = RoadForm.Dual;
            Assert.Contains(_template.ValidateParams(p), e => e.Contains("暂不支持"));
        }

        [Fact]
        public void MissingExternalPrefab_IsRejected()
        {
            var p = MainDualInsideTwoWayOutside();
            p.Legs[0].ExternalPrefab = null;
            Assert.Contains(_template.ValidateParams(p), e => e.Contains("外部双向道路"));
        }

        [Fact]
        public void WideTwoWayMain_SplitIntoAlignedCarriageways_JoinsParallelAtOneNode()
        {
            // 游戏内的组合：地面主线 XL 双向（40 米）在菱形内部拆成两幅 18 米宽的单行道路（外缘对齐，分隔带 4 米），上面是高架公路
            var p = new InterchangeParams { RampLength = 300f, RampWidth = 16f, RampSideGap = 3f };
            p.SetRoad(0, 2, RoadForm.Dual, 18f, 4f, "Oneway");
            p.SetRoad(1, 3, RoadForm.Dual, 20f, 8f, "Highway");
            foreach (int i in new[] { 0, 2 })
            {
                p.Legs[i].ExternalForm = RoadForm.TwoWay;
                p.Legs[i].ExternalPrefab = "XL";
                p.Legs[i].ExternalWidth = 40f;
            }
            p.Levels[0].Elevation = 0.25f;
            p.Levels[1].Elevation = 10.25f;
            Assert.Empty(_template.ValidateParams(p));
            var segments = _template.Generate(p);
            Assert.Empty(InterchangeLayout.ValidateGeometry(segments, p));
            TestHelpers.AssertRampsConnected(segments);
            Vec3 main = p.Legs[0].Direction;
            foreach (string end in new[] { "Main-0", "Main-2" })
            {
                // 每端只有一段双向尾段，没有斜向的过渡段
                var parts = segments.Where(s => s.Name.StartsWith(end + "-")).ToList();
                var tail = Assert.Single(parts);
                Assert.Equal("XL", tail.Prefab);
                // 两幅平行延伸到尾段起点（在中心线上），曲线端点仍在各自一幅上（边缘对齐）
                Vec3 joint = tail.StartNode;
                Assert.Equal(0f, joint.X * main.Z - joint.Z * main.X, 3);
                // 接入节点离匝道节点（±RampLength）至少 40 米：两个节点都接着宽路，太近节点区重叠
                Assert.True(System.Math.Abs(Directions.Dot(joint, main)) >= p.RampLength + 40f - 1e-3f, $"{end}: joint at {Directions.Dot(joint, main):F1}");
                var carriageways = segments.Where(s => s.Role == RoadRole.MainRoad && s != tail && (s.StartNode == joint || s.EndNode == joint)).ToList();
                Assert.Equal(2, carriageways.Count);
                Assert.All(carriageways, s => Assert.True(s.HasNodeOffset));
                Assert.All(carriageways, s => Assert.Equal("Oneway", s.Prefab));
            }
        }
    }
}
