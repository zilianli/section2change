using System.Linq;
using InterchangeMod.Generation.Geometry;
using InterchangeMod.Generation.Templates;
using Xunit;

namespace InterchangeMod.Generation.Tests
{
    public class ElevatedDivergeTests
    {
        /// <summary>游戏内复测的组合：4 车道公路高架主线（双幅），2 车道匝道，车道平衡后中段也是 2 车道，被交道路 XL 双向。</summary>
        private static InterchangeParams GameParams(float rampLength, float sideGap)
        {
            var p = new InterchangeParams
            {
                Legs = InterchangeParams.CrossLegs(89.4f, 0, 1),
                RampWidth = 12f, RampLength = rampLength, RampSideGap = sideGap,
                RampPrefab = "Highway Oneway - 2 lanes",
                MainReducedPrefab = "Highway Oneway - 2 lanes", MainReducedWidth = 12f,
            };
            p.SetRoad(0, 2, RoadForm.Dual, 20f, 8f, "Highway Oneway - 4 lanes");
            p.SetRoad(1, 3, RoadForm.TwoWay, 40f, 0f, "XL Road Divided");
            p.Levels[0].Elevation = 10.25f;
            p.Levels[1].Elevation = 0.25f;
            return p;
        }

        [Theory]
        [InlineData(InterchangeType.SinglePoint, 250f, 1f)]
        [InlineData(InterchangeType.ExpresswayNode, 250f, 1f)]
        [InlineData(InterchangeType.Diamond, 250f, 3f)]
        [InlineData(InterchangeType.Diamond, 400f, 1f)]
        public void RampAtElevatedMainNode_ClearsTheMainWithinOneGameEdge(InterchangeType type, float rampLength, float sideGap)
        {
            var p = GameParams(rampLength, sideGap);
            var template = TemplateRegistry.Get(type)!;
            // 单点配 40 米被交道路时中心节点放不下（见 SinglePointTemplateTests.WideCrossRoadAtAcuteAngle_IsRejected）
            if (type == InterchangeType.SinglePoint)
                p.SetRoad(1, 3, RoadForm.TwoWay, 24f, 0f, "Medium Road");
            Assert.Empty(template.ValidateParams(p));
            var segments = template.Generate(p);
            TestHelpers.AssertRampsConnected(segments);
            float maxEdge = p.Legs[0].MaxElevatedEdgeLength;
            float clear = DiamondTemplate.RampClear(p);
            Vec3 main = p.Legs[0].Direction;
            var mainNodes = segments.Where(s => s.Role == RoadRole.MainRoad).SelectMany(s => new[] { s.StartNode, s.EndNode }).ToHashSet();
            var ramps = segments.Where(s => s.Role == RoadRole.Ramp && (mainNodes.Contains(s.StartNode) || mainNodes.Contains(s.EndNode))).ToList();
            Assert.Equal(4, ramps.Count);
            foreach (var ramp in ramps)
            {
                // 压着主线的一段在一条游戏路段内（游戏不切开它，切点的立柱不会落在主线桥面上）
                Assert.True(ramp.Curve.Length() <= maxEdge - 5f, $"{type} {ramp.Name}: {ramp.Curve.Length():F1} m");
                // 另一头已经离开主线（横向离主线节点至少 clear）
                bool atStart = mainNodes.Contains(ramp.StartNode);
                Vec3 node = atStart ? ramp.StartNode : ramp.EndNode, far = atStart ? ramp.EndNode : ramp.StartNode;
                Vec3 d = far - node;
                float lateral = System.Math.Abs(main.X * d.Z - main.Z * d.X);
                Assert.True(lateral >= clear - 0.01f, $"{type} {ramp.Name}: lateral {lateral:F2} < {clear:F2}");
                // 不贴着主线缓缓分出：在节点处与主线相切，起点半径不大于 80 米的圆弧（游戏内缓缓分出时节点后约 10 米报形状无效）
                float k = ramp.Curve.Curvature(atStart ? 0f : 1f);
                Assert.True(k >= 0.98f / 80f,$"{type} {ramp.Name}: leaves the main with radius {1f / k:F0} m");
            }
        }

        /// <summary>
        /// 分出段及其后接的一段（name-clear）不急弯：匝道平行贴着主线外走（中式快速路、单点）时，旧做法圆弧之后
        /// 冲过终点再在几米内拐回，最小半径只有 3 ~ 20 米。
        /// </summary>
        [Theory]
        [InlineData(InterchangeType.SinglePoint, 8f, 4f, 89.4f)]
        [InlineData(InterchangeType.SinglePoint, 12f, 4f, 89.4f)]
        [InlineData(InterchangeType.SinglePoint, 8f, 3f, 70f)]
        [InlineData(InterchangeType.ExpresswayNode, 8f, 3f, 89.4f)]
        [InlineData(InterchangeType.ExpresswayNode, 12f, 4f, 89.4f)]
        [InlineData(InterchangeType.ExpresswayNode, 8f, 4f, 70f)]
        [InlineData(InterchangeType.ExpresswayT, 8f, 4f, 90f)]
        [InlineData(InterchangeType.ExpresswayT, 12f, 4f, 70f)]
        public void ElevatedDivergePieces_HaveNoTightBends(InterchangeType type, float rampWidth, float sideGap, float angle)
        {
            var p = GameParams(400f, sideGap);
            p.RampWidth = rampWidth;
            var template = TemplateRegistry.Get(type)!;
            p.Legs = InterchangeParams.CrossLegs(angle, 0, 1);
            p.SetRoad(0, 2, RoadForm.Dual, 20f, 8f, "Highway Oneway - 4 lanes");
            if (type == InterchangeType.SinglePoint)
                p.SetRoad(1, 3, RoadForm.TwoWay, 24f, 0f, "Medium Road");
            else
                p.SetRoad(1, 3, RoadForm.TwoWay, 40f, 0f, "XL Road Divided");
            if (template.LegCount == 3)
            {
                p.Legs = InterchangeParams.TLegs(angle, 0, 1);
                p.SetRoad(0, 2, RoadForm.Dual, 20f, 8f, "Highway Oneway - 4 lanes");
                p.SetRoad(1, 1, RoadForm.TwoWay, 40f, 0f, "XL Road Divided");
            }
            Assert.Empty(template.ValidateParams(p));
            var segments = template.Generate(p);
            TestHelpers.AssertRampsConnected(segments);
            var names = segments.Select(s => s.Name).ToHashSet();
            var pieces = segments.Where(s => s.Role == RoadRole.Ramp
                && (names.Contains(s.Name + "-clear") || s.Name.Contains("-clear"))).ToList();
            Assert.NotEmpty(pieces);
            foreach (var piece in pieces)
            {
                float radius = 1f / piece.Curve.MaxCurvature();
                Assert.True(radius >= 39.5f, $"{type} {piece.Name}: minimum radius {radius:F1} m");
            }
        }
    }
}
