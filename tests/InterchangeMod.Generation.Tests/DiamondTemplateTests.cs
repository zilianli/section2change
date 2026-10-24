using System.Collections.Generic;
using System.Linq;
using InterchangeMod.Generation.Geometry;
using InterchangeMod.Generation.Templates;
using Xunit;

namespace InterchangeMod.Generation.Tests
{
    public class DiamondTemplateTests
    {
        private readonly DiamondTemplate _template = new DiamondTemplate();

        [Fact]
        public void Generate_ProducesEightRampPiecesAndFiveSegmentsPerRoad()
        {
            // 4 条匝道，每条 = 分流/合流段 + 爬坡段
            // 每条路 5 段：引道、匝道节点、跨越处两个避让节点、匝道节点、引道
            var segments = _template.Generate(new InterchangeParams());
            Assert.Equal(8, segments.Count(s => s.Role == RoadRole.Ramp));
            Assert.Equal(5, segments.Count(s => s.Role == RoadRole.MainRoad));
            Assert.Equal(5, segments.Count(s => s.Role == RoadRole.CrossRoad));
        }

        [Theory]
        [InlineData(90f, false, false)]
        [InlineData(60f, false, false)]
        [InlineData(85f, true, true)]
        [InlineData(120f, true, false)]
        public void CrossingSpan_KeepsNodesOffTheOtherRoad(float angle, bool dualMain, bool dualCross)
        {
            // 跨越段的两个端点（立柱位置）必须在对方道路占地之外，否则立柱压在下层道路上
            var p = new InterchangeParams { Legs = InterchangeParams.CrossLegs(angle, 0, 1) };
            if (dualMain) p.SetRoad(0, 2, RoadForm.Dual, 12f, 14f, "Oneway");
            if (dualCross) p.SetRoad(1, 3, RoadForm.Dual, 12f, 14f, "Oneway");
            Assert.Empty(_template.ValidateParams(p));
            var segments = _template.Generate(p);

            Vec3 mainDir = p.Legs[0].Direction, crossDir = p.Legs[1].Direction;
            float mainHalf = p.Legs[0].HalfExtent, crossHalf = p.Legs[1].HalfExtent;
            foreach (var s in segments.Where(s => s.Role != RoadRole.Ramp))
            {
                bool isMain = s.Role == RoadRole.MainRoad;
                // 对方道路的中心线方向与半宽；点到对方中心线的水平距离
                Vec3 other = isMain ? crossDir : mainDir;
                float otherHalf = isMain ? crossHalf : mainHalf;
                foreach (var node in new[] { s.Curve.P0, s.Curve.P3 })
                {
                    float distance = System.Math.Abs(node.X * other.Z - node.Z * other.X);
                    Assert.True(distance > otherHalf + 2f, $"{s.Name} node {node} is {distance:F1} m from the other road (half width {otherHalf})");
                }
            }
        }

        [Fact]
        public void TwoWayMainOffGround_IsRejected()
        {
            foreach (float elevation in new[] { 5f, -8f, -15f })
            {
                var p = new InterchangeParams();
                p.Levels[0].Elevation = elevation;
                p.Levels[1].Elevation = elevation + 10f;
                Assert.Contains(_template.ValidateParams(p), e => e.Contains("双幅"));
            }
        }

        [Theory]
        [InlineData(90f)]
        [InlineData(60f)]
        [InlineData(125f)]
        public void EveryRampEndpoint_IsConnected(float crossingAngle)
        {
            var p = new InterchangeParams { Legs = InterchangeParams.CrossLegs(crossingAngle, 0, 1) };
            TestHelpers.AssertRampsConnected(_template.Generate(p));
        }

        [Fact]
        public void RampTaper_StaysAtMainLevelUntilClearOfMainRoad()
        {
            var p = new InterchangeParams();
            var c = _template.Generate(p).ToDictionary(s => s.Name, s => s.Curve);
            float hMain = p.Levels[0].Elevation;

            foreach (var name in new[] { "NB-diverge", "NB-merge", "SB-diverge", "SB-merge" })
            {
                Assert.Equal(hMain, c[name].P0.Y);
                Assert.Equal(hMain, c[name].P3.Y);
            }
            // 分流段终点：匝道中心离主线中心 ≥ 主线半宽 + 匝道半宽 + 间距
            float needed = p.Legs[0].RoadWidth / 2f + p.RampWidth / 2f + p.RampSideGap;
            Assert.True(System.Math.Abs(c["NB-diverge"].P3.X) >= needed - 1e-3f);
            // 爬坡段从分流段终点开始
            Assert.Equal(c["NB-diverge"].P3, c["NB-off"].P0);
            Assert.Equal(c["NB-on"].P3, c["NB-merge"].P0);
        }

        [Fact]
        public void TaperLongerThanRamp_IsRejected()
        {
            // 5° 时分流段 = (6 + 6 + 3) / tan5° ≈ 171 米，比 150 米的匝道还长
            var p = new InterchangeParams { RampDivergeAngle = 5f, RampLength = 150f };
            Assert.Contains(_template.ValidateParams(p), e => e.Contains("分流段"));
        }

        [Theory]
        [InlineData(15f, 16f, 8f)]   // 3 车道公路主线 + 1 车道匝道（游戏内报错的组合）
        [InlineData(10f, 24f, 12f)]
        [InlineData(25f, 12f, 12f)]
        public void Diverge_IsTangentAndOutlastsTheGameNode(float angle, float mainWidth, float rampWidth)
        {
            var p = new InterchangeParams { RampDivergeAngle = angle, RampWidth = rampWidth, RampLength = 500f };
            p.SetRoad(0, 2, RoadForm.Dual, mainWidth, 4f, "Oneway");
            var diverge = _template.Generate(p).Single(s => s.Name == "NB-diverge").Curve;

            // 外切曲线：起点与主线相切，终点与主线成设定角度，全程保持主线高度
            Vec3 start = diverge.Derivative(0f), end = diverge.Derivative(1f);
            Assert.True(System.Math.Abs(start.X) < 1e-3f);
            Assert.Equal(angle, System.MathF.Atan2(System.Math.Abs(end.X), end.Z) / Directions.DegToRad, 2);
            Assert.Equal(diverge.P0.Y, diverge.P3.Y);
            Vec3 d = diverge.P3 - diverge.P0;

            // 游戏的分流节点延伸到两路边缘不再重叠处：(主线半宽 + 匝道半宽) / tan(角度)。分流段要比它长
            float nodeReach = (mainWidth / 2f + rampWidth / 2f) / System.MathF.Tan(angle * Directions.DegToRad);
            Assert.True(d.Z > nodeReach, $"diverge {d.Z:F1} m is not longer than node reach {nodeReach:F1} m");
        }

        [Fact]
        public void RoadSegments_ChainThroughSharedNodes()
        {
            var segments = _template.Generate(new InterchangeParams());
            foreach (var prefix in new[] { "Main", "Cross" })
            {
                var chain = segments.Where(s => s.Name.StartsWith(prefix)).ToList();
                for (int i = 1; i < chain.Count; i++)
                    Assert.Equal(chain[i - 1].Curve.P3, chain[i].Curve.P0);
            }
        }

        [Fact]
        public void DefaultParams_PassValidationAndGradeLimit()
        {
            var p = new InterchangeParams();
            Assert.Empty(_template.ValidateParams(p));
            Assert.Empty(InterchangeLayout.ValidateGeometry(_template.Generate(p), p));
        }

        [Fact]
        public void RoadsUseTheirLevelElevation()
        {
            var p = new InterchangeParams();
            p.Levels[0].Elevation = -12f;
            p.Levels[1].Elevation = 0f;
            var segments = _template.Generate(p);

            Assert.All(segments.Where(s => s.Role == RoadRole.MainRoad), s => Assert.Equal(-12f, s.Curve.P0.Y));
            Assert.All(segments.Where(s => s.Role == RoadRole.CrossRoad), s => Assert.Equal(0f, s.Curve.P3.Y));
        }

        [Fact]
        public void UndergroundMainLine_IsValid()
        {
            // 地上地下混合：主线下沉 10 米，被交道路在地面（下沉主线必须是双幅）
            var p = new InterchangeParams();
            p.SetRoad(0, 2, RoadForm.Dual, 12f, 4f, "Oneway");
            p.Levels[0].Elevation = -10f;
            p.Levels[1].Elevation = 0f;
            Assert.Empty(_template.ValidateParams(p));
            Assert.Empty(InterchangeLayout.ValidateGeometry(_template.Generate(p), p));
        }

        [Fact]
        public void ShortRamp_IsRejected()
        {
            var p = new InterchangeParams { RampLength = 60f, MaxGrade = 0.08f };
            Assert.NotEmpty(_template.ValidateParams(p));
            Assert.NotEmpty(InterchangeLayout.ValidateGeometry(_template.Generate(p), p));
        }

        [Fact]
        public void InsufficientClearance_IsRejected()
        {
            var p = new InterchangeParams();
            p.Levels[1].Elevation = 3f;
            Assert.Contains(_template.ValidateParams(p), e => e.Contains("净空"));
        }

        [Theory]
        [InlineData(5f, 12f, false)]   // 游戏内实测：高差 7 米报“净空不足”
        [InlineData(5f, 12.5f, true)]  // 7.5 米是最小可行高差
        [InlineData(0f, 10f, true)]
        public void Clearance_UsesMeasuredMinimumWithoutPrefabData(float main, float cross, bool ok)
        {
            var p = new InterchangeParams();
            p.Levels[0].Elevation = main;
            p.Levels[1].Elevation = cross;
            Assert.Equal(ok, !_template.ValidateParams(p).Any(e => e.Contains("净空")));
        }

        [Fact]
        public void Clearance_UsesRoadHeightRangesWhenLarger()
        {
            // 下层在地面、净空顶 6 米；上层高架、桥面结构底 -3 米 → 至少 9 米
            var p = new InterchangeParams();
            foreach (var leg in p.Legs)
            {
                leg.GroundHeight = new HeightRange(-1f, 6f);
                leg.ElevatedHeight = new HeightRange(-3f, 6f);
            }
            p.Levels[1].Elevation = 8.9f;   // 已是高架桥（≥ 8.5），按高架范围要 9 米
            Assert.Contains(_template.ValidateParams(p), e => e.Contains("9.0"));

            p.Levels[1].Elevation = 9f;
            Assert.DoesNotContain(_template.ValidateParams(p), e => e.Contains("净空"));
        }

        [Fact]
        public void UpperLevelAsEmbankment_IsRejected()
        {
            // 游戏内实测：上层 7.5 米建成带挡土墙的路堤，挡住下层道路
            var p = new InterchangeParams();
            p.Levels[1].Elevation = 7.5f;
            Assert.Contains(_template.ValidateParams(p), e => e.Contains("路堤"));

            p.Levels[1].Elevation = 8.5f;
            Assert.DoesNotContain(_template.ValidateParams(p), e => e.Contains("路堤"));
        }

        [Fact]
        public void RampWiderThanMainRoad_IsRejected()
        {
            var p = new InterchangeParams { RampWidth = 24f };
            p.SetRoad(0, 2, RoadForm.TwoWay, 20f, 4f, null);
            Assert.Contains(_template.ValidateParams(p), e => e.Contains("匝道宽"));
        }

        [Theory]
        [InlineData(20f, true)]
        [InlineData(40f, true)]
        [InlineData(45f, false)]
        [InlineData(135f, false)]
        [InlineData(140f, true)]
        public void CrossingAngle_MustBeBetween45And135(float angle, bool rejected)
        {
            var p = new InterchangeParams { Legs = InterchangeParams.CrossLegs(angle, 0, 1) };
            Assert.Equal(rejected, _template.ValidateParams(p).Any(e => e.Contains("相交角度")));
        }

        [Theory]
        [InlineData(45f)]
        [InlineData(70f)]
        [InlineData(135f)]
        public void RampEnds_MeetCrossRoadAtLeast67Degrees(float angle)
        {
            // 斜交时匝道末端转一半角度接入被交道路（游戏内实测：平行到达时斜交过大报“形状无效/重叠”）
            var p = new InterchangeParams { Legs = InterchangeParams.CrossLegs(angle, 0, 1) };
            var c = _template.Generate(p).ToDictionary(s => s.Name, s => s.Curve);
            Vec3 cross = p.Legs[1].Direction;
            foreach (var (name, atEnd) in new[] { ("NB-off", true), ("SB-off", true), ("NB-on", false), ("SB-on", false) })
            {
                Vec3 tangent = c[name].Derivative(atEnd ? 1f : 0f);
                var flat = Vec3.Normalize(new Vec3(tangent.X, 0f, tangent.Z));
                float meet = Directions.AngleBetween(System.MathF.Atan2(flat.Z, flat.X) / Directions.DegToRad, System.MathF.Atan2(cross.Z, cross.X) / Directions.DegToRad);
                Assert.InRange(System.Math.Min(meet, 180f - meet), 67f, 90.01f);
            }
        }

        [Theory]
        [InlineData(45f, false, false)]
        [InlineData(45f, true, true)]
        [InlineData(135f, true, false)]
        [InlineData(135f, false, true)]
        public void ObliqueCrossings_AreValidAndConnected(float angle, bool dualMain, bool dualCross)
        {
            var p = new InterchangeParams { Legs = InterchangeParams.CrossLegs(angle, 0, 1) };
            if (dualMain) p.SetRoad(0, 2, RoadForm.Dual, 12f, 6f, "Oneway");
            if (dualCross) p.SetRoad(1, 3, RoadForm.Dual, 12f, 6f, "Oneway");
            p = InterchangeLayout.FitRampLength(_template, p);
            Assert.Empty(_template.ValidateParams(p));
            var segments = _template.Generate(p);
            Assert.Empty(InterchangeLayout.ValidateGeometry(segments, p));
            TestHelpers.AssertRampsConnected(segments);
        }

        [Fact]
        public void ThreeLegs_IsRejected()
        {
            var p = new InterchangeParams();
            p.Legs.RemoveAt(3);
            Assert.NotEmpty(_template.ValidateParams(p));
        }

        [Fact]
        public void BentMainLine_IsRejected()
        {
            var p = new InterchangeParams();
            p.Legs[2].Angle = 160f;
            Assert.Contains(_template.ValidateParams(p), e => e.Contains("直线"));
        }

        [Fact]
        public void OffRamp_LeavesOnDrivingSide()
        {
            foreach (bool rightHand in new[] { true, false })
            {
                var p = new InterchangeParams { RightHandTraffic = rightHand };
                var nbOff = _template.Generate(p).Single(s => s.Name == "NB-off").Curve;
                // 主线朝 +Z（北）行驶，右侧是 +X
                Assert.Equal(rightHand, nbOff.P3.X > 0f);
            }
        }

        [Fact]
        public void LegPrefabs_AreUsedPerSide()
        {
            var p = new InterchangeParams { RampPrefab = "Ramp" };
            p.Legs[0].RoadPrefab = "North";
            p.Legs[2].RoadPrefab = "South";
            p.Legs[1].RoadPrefab = "East";
            var byName = _template.Generate(p).ToDictionary(s => s.Name, s => s.Prefab);

            Assert.Equal("North", byName["Main-0"]);
            Assert.Equal("South", byName["Main-2"]);
            Assert.Equal("East", byName["Cross-1"]);
            Assert.Null(byName["Cross-3"]);
            Assert.Equal("Ramp", byName["NB-off"]);
        }

        [Fact]
        public void Clone_IsDeep()
        {
            var p = new InterchangeParams();
            var copy = p.Clone();
            copy.Levels[1].Elevation = 99f;
            copy.Legs[0].Angle = 45f;
            Assert.Equal(10f, p.Levels[1].Elevation);
            Assert.Equal(0f, p.Legs[0].Angle);
        }

        [Fact]
        public void Place_MovesOriginToTargetPosition()
        {
            var target = new Vec3(1000, 50, -200);
            var placed = InterchangeLayout.Place(_template.Generate(new InterchangeParams()), target, 0.7f);
            // 中间那段是跨越段，两个避让节点关于交叉点对称
            var mainMid = placed.Single(s => s.Name == "Main-M2").Curve.Evaluate(0.5f);
            Assert.True(Vec3.Distance(mainMid, target) < 1e-2f);
        }

        [Fact]
        public void Registry_ContainsDiamond()
        {
            var template = TemplateRegistry.Get(InterchangeType.Diamond);
            Assert.IsType<DiamondTemplate>(template);
            Assert.Equal(4, template!.LegCount);
        }

        [Fact]
        public void SvgPreview_DrawsEverySegment()
        {
            var segments = _template.Generate(new InterchangeParams());
            var svg = SvgPreview.Render(segments);
            Assert.Equal(segments.Count, svg.Split("<path").Length - 1);
        }
    }
}
