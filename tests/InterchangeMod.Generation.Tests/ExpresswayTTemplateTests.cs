using System.Linq;
using InterchangeMod.Generation.Geometry;
using InterchangeMod.Generation.Templates;
using Xunit;

namespace InterchangeMod.Generation.Tests
{
    /// <summary>快速路 T 型节点：支路只在一侧，从高架下穿过，在远侧辅路处结束。</summary>
    public class ExpresswayTTemplateTests
    {
        private readonly ExpresswayNodeTemplate _template = new ExpresswayNodeTemplate(tee: true);

        private static InterchangeParams Make(float branchAngle = 90f, bool dualBranch = false)
        {
            var p = new InterchangeParams { Legs = InterchangeParams.TLegs(branchAngle, 0, 1), RampWidth = 8f };
            p.SetRoad(0, 2, RoadForm.Dual, 16f, 4f, "Highway Oneway");
            if (dualBranch)
                p.SetRoad(1, 1, RoadForm.Dual, 12f, 6f, "BranchOneway");
            else
                p.SetRoad(1, 1, RoadForm.TwoWay, 20f, 0f, "Branch");
            p.Levels[0].Elevation = 10f;
            p.Levels[1].Elevation = 0f;
            return p;
        }

        [Theory]
        [InlineData(90f, false)]    // 支路在右侧
        [InlineData(270f, false)]   // 支路在左侧
        [InlineData(60f, false)]
        [InlineData(300f, true)]
        [InlineData(90f, true)]
        public void Generated_IsValidAndConnected(float branchAngle, bool dualBranch)
        {
            var p = Make(branchAngle, dualBranch);
            Assert.Empty(_template.ValidateParams(p));
            var segments = _template.Generate(p);
            Assert.Empty(InterchangeLayout.ValidateGeometry(segments, p));
            TestHelpers.AssertRampsConnected(segments);
            // 主线 4 端 + 辅路 4 端 + 支路（双向 1 端 / 双幅 2 端）
            Assert.Equal(dualBranch ? 10 : 9, InterchangeLayout.LegEnds(segments).Count);
        }

        [Theory]
        [InlineData(90f)]
        [InlineData(270f)]
        public void Branch_EndsAtFarFrontageJunction(float branchAngle)
        {
            var p = Make(branchAngle);
            var segments = _template.Generate(p);
            Vec3 branchDir = p.Legs[1].Direction;
            var branch = segments.Where(s => s.Role == RoadRole.CrossRoad).ToList();
            var front = segments.Where(s => s.Role == RoadRole.FrontageRoad).SelectMany(s => new[] { s.StartNode, s.EndNode }).ToHashSet();

            // 支路只伸向臂 1 一侧：所有节点里只有一个在臂 1 方向之外很远（外端），最远的反方向节点是远侧辅路上的 T 字路口
            var nodes = branch.SelectMany(s => new[] { s.StartNode, s.EndNode }).Distinct().ToList();
            Vec3 farEnd = nodes.OrderBy(v => Directions.Dot(v, branchDir)).First();
            Assert.Contains(farEnd, front);
            Assert.True(Directions.Dot(farEnd, branchDir) < 0f, "the branch should cross under the viaduct to the far side");
            // 两侧辅路各有一个 T 字路口
            Assert.Equal(2, nodes.Count(front.Contains));
        }

        [Fact]
        public void IsRegistered()
        {
            Assert.Equal(3, TemplateRegistry.Get(InterchangeType.ExpresswayT)!.LegCount);
        }

        /// <summary>
        /// 原有道路在地面（外部高度 0），主线是新建高架：两端主线下坡落地、辅路并入，再接外部道路。
        /// externalTwoWay = 外部是双向道路（两端双幅收拢成双向）。
        /// </summary>
        [Theory]
        [InlineData(true, false)]
        [InlineData(true, true)]
        [InlineData(false, false)]
        [InlineData(false, true)]
        public void MainEnds_LandOnGroundRoad(bool tee, bool externalTwoWay)
        {
            var template = new ExpresswayNodeTemplate(tee);
            var p = new InterchangeParams
            {
                Legs = tee ? InterchangeParams.TLegs(90f, 0, 1) : InterchangeParams.CrossLegs(90f, 0, 1),
                RampWidth = 8f,
            };
            p.SetRoad(0, 2, RoadForm.Dual, 16f, 4f, "Highway Oneway");
            p.SetRoad(1, tee ? 1 : 3, RoadForm.TwoWay, 20f, 0f, "Cross");
            p.Levels[0].Elevation = 10f;
            p.Levels[1].Elevation = 0f;
            foreach (var leg in p.Legs)
            {
                leg.ExternalElevation = 0f;
                if (externalTwoWay && leg != p.Legs[1] && (tee || leg != p.Legs[3]))
                {
                    leg.ExternalForm = RoadForm.TwoWay;
                    leg.ExternalPrefab = "Large Road";
                }
            }
            Assert.Empty(template.ValidateParams(p));
            var segments = template.Generate(p);
            Assert.Empty(InterchangeLayout.ValidateGeometry(segments, p));
            TestHelpers.AssertRampsConnected(segments);

            // 所有臂端都在地面；辅路不并回主线，各自伸到外端结束（每端两幅）
            var ends = InterchangeLayout.LegEnds(segments);
            Assert.All(ends, e => Assert.Equal(0f, e.Y, 3));
            int mainEnds = externalTwoWay ? 2 : 4;
            int crossEnds = tee ? 1 : 2;
            Assert.Equal(mainEnds + crossEnds + 4, ends.Count);
            Assert.DoesNotContain(segments, s => s.Name.EndsWith("-merge") && s.Role == RoadRole.FrontageRoad);

            // 坡道两端水平
            foreach (var s in segments.Where(s => s.Role == RoadRole.MainRoad && System.Math.Abs(s.Curve.P0.Y - s.Curve.P3.Y) > 0.1f))
            {
                Assert.True(System.Math.Abs(s.Curve.Derivative(0f).Y) < 1e-3f && System.Math.Abs(s.Curve.Derivative(1f).Y) < 1e-3f, $"{s.Name} slope ends are not level");
                Assert.True(s.Curve.MaxGrade() <= p.MaxGrade + 1e-4f, $"{s.Name} is too steep");
            }
        }
    }
}
