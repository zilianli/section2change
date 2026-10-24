using System.Linq;
using InterchangeMod.Generation.Geometry;
using InterchangeMod.Generation.Templates;
using Xunit;

namespace InterchangeMod.Generation.Tests
{
    public class SinglePointTemplateTests
    {
        private readonly SinglePointTemplate _template = new SinglePointTemplate();

        /// <summary>双幅公路主线高架 10 米，被交道路（双向）在地面。</summary>
        private static InterchangeParams Make(float angle = 90f)
        {
            var p = new InterchangeParams { Legs = InterchangeParams.CrossLegs(angle, 0, 1), RampWidth = 8f };
            p.SetRoad(0, 2, RoadForm.Dual, 16f, 4f, "Highway Oneway");
            p.SetRoad(1, 3, RoadForm.TwoWay, 20f, 0f, "Cross");
            p.Levels[0].Elevation = 10f;
            p.Levels[1].Elevation = 0f;
            return p;
        }

        [Theory]
        [InlineData(90f)]
        [InlineData(55f)]
        [InlineData(60f)]
        [InlineData(70f)]
        [InlineData(80f)]
        [InlineData(100f)]
        [InlineData(120f)]
        [InlineData(125f)]
        public void Generated_IsValidAndConnected(float angle)
        {
            var p = Make(angle);
            Assert.Empty(_template.ValidateParams(p));
            var segments = _template.Generate(p);
            Assert.Empty(InterchangeLayout.ValidateGeometry(segments, p));
            TestHelpers.AssertRampsConnected(segments);
            // 主线 4 端 + 被交道路 2 端
            Assert.Equal(6, InterchangeLayout.LegEnds(segments).Count);
        }

        [Fact]
        public void AllRampsMeetAtOneCenterNode()
        {
            var p = Make();
            var segments = _template.Generate(p);
            var center = new Vec3(0f, 0f, 0f);
            var turns = segments.Where(s => s.Name.EndsWith("-center")).ToList();
            Assert.Equal(4, turns.Count);
            Assert.All(turns, s => Assert.True(s.Curve.P0.Equals(center) || s.Curve.P3.Equals(center), $"{s.Name} misses the center node"));
            // 被交道路也经过中心节点：中心节点共 6 条路
            int edges = segments.Count(s => s.StartNode.Equals(center) || s.EndNode.Equals(center));
            Assert.Equal(6, edges);
        }

        [Theory]
        [InlineData(90f)]
        [InlineData(55f)]
        public void RampsAtCenter_Are90DegreesApart(float angle)
        {
            var p = Make(angle);
            var c = _template.Generate(p).ToDictionary(s => s.Name, s => s.Curve);
            // 中心节点处各匝道离开中心的方向
            Vec3 Out(string name) => name.Contains("-off-") ? -Vec3.Normalize(c[name].Derivative(1f)) : Vec3.Normalize(c[name].Derivative(0f));
            float dot = Directions.Dot(Out("NB-off-center"), Out("SB-on-center"));
            Assert.True(System.Math.Abs(dot) < 1e-3f);
            dot = Directions.Dot(Out("NB-off-center"), Out("NB-on-center"));
            Assert.True(System.Math.Abs(dot) < 1e-3f);
        }

        [Theory]
        [InlineData(90f)]
        [InlineData(55f)]
        [InlineData(125f)]
        public void MainPillars_StayOffRampsAndCrossRoad(float angle)
        {
            var p = Make(angle);
            var segments = _template.Generate(p);
            Vec3 main = p.Legs[0].Direction;
            Vec3 side = Directions.RightOf(main);
            // 主线跨越段两端节点（立柱）离所有匝道转弯段至少“匝道半宽 + 立柱半宽”
            var mainNodes = segments.Where(s => s.Role == RoadRole.MainRoad).SelectMany(s => new[] { s.StartNode, s.EndNode }).Distinct().ToList();
            foreach (var turn in segments.Where(s => s.Name.EndsWith("-turn") || s.Name.EndsWith("-center")))
                for (int i = 0; i <= 64; i++)
                {
                    Vec3 v = turn.Curve.Evaluate(i / 64f);
                    foreach (var n in mainNodes)
                    {
                        // 节点立柱：门式立柱两条腿在桥面两侧边缘以内（16 米宽的主线），腿半宽 1.5 米；
                        // 腿中心离匝道中心线不到“匝道半宽 + 腿半宽”就算撞上
                        float half = p.Legs[0].RoadWidth / 2f - 1.5f;
                        foreach (float leg in new[] { -half, half })
                        {
                            Vec3 legPoint = n + side * leg;
                            float d = (float)System.Math.Sqrt((v.X - legPoint.X) * (v.X - legPoint.X) + (v.Z - legPoint.Z) * (v.Z - legPoint.Z));
                            Assert.True(d >= p.RampWidth / 2f + 1.5f, $"main node {n} pillar leg {leg} hits {turn.Name}");
                        }
                    }
                }
        }

        [Theory]
        [InlineData(50f)]
        [InlineData(130f)]
        public void TooOblique_IsRejected(float angle)
        {
            Assert.Contains(_template.ValidateParams(Make(angle)), e => e.Contains("相交角度"));
        }

        [Theory]
        [InlineData(90f, 30f)]
        [InlineData(80f, 30f)]
        public void WideCrossRoad_StillFitsUnderOneSpan(float angle, float crossWidth)
        {
            var p = Make(angle);
            p.SetRoad(1, 3, RoadForm.TwoWay, crossWidth, 0f, "Large Road");
            Assert.Empty(_template.ValidateParams(p));
            Assert.Empty(InterchangeLayout.ValidateGeometry(_template.Generate(p), p));
        }

        [Theory]
        [InlineData(90f)]
        [InlineData(60f)]
        [InlineData(120f)]
        public void GameSizedRoads_Fit(float angle)
        {
            // 游戏内截图的组合：4 车道公路（20 米）双幅、分隔带 8 米，2 车道匝道（12 米），被交道路 Large Road（30 米）
            var p = Make(angle);
            p.RampWidth = 12f;
            p.RampSideGap = 1f;
            p.SetRoad(0, 2, RoadForm.Dual, 20f, 8f, "Highway Oneway - 4 lanes");
            p.SetRoad(1, 3, RoadForm.TwoWay, 30f, 0f, "Large Road");
            p.Levels[0].Elevation = 11f;
            Assert.Empty(_template.ValidateParams(p));
            Assert.Empty(InterchangeLayout.ValidateGeometry(_template.Generate(p), p));
        }

        [Fact]
        public void DualCrossRoad_IsRejected()
        {
            var p = Make();
            p.SetRoad(1, 3, RoadForm.Dual, 12f, 6f, "CrossOneway");
            Assert.Contains(_template.ValidateParams(p), e => e.Contains("双向"));
        }

        [Fact]
        public void IsRegistered()
        {
            Assert.IsType<SinglePointTemplate>(TemplateRegistry.Get(InterchangeType.SinglePoint));
        }
    }
}
