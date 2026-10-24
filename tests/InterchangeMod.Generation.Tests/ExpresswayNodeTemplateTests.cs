using System.Linq;
using InterchangeMod.Generation.Geometry;
using InterchangeMod.Generation.Templates;
using Xunit;

namespace InterchangeMod.Generation.Tests
{
    public class ExpresswayNodeTemplateTests
    {
        private readonly ExpresswayNodeTemplate _template = new ExpresswayNodeTemplate();

        /// <summary>双幅公路主线高架 10 米，被交道路和辅路在地面。</summary>
        private static InterchangeParams Make(float angle = 90f, bool dualCross = false)
        {
            var p = new InterchangeParams { Legs = InterchangeParams.CrossLegs(angle, 0, 1), RampWidth = 8f };
            p.SetRoad(0, 2, RoadForm.Dual, 16f, 4f, "Highway Oneway");
            if (dualCross)
                p.SetRoad(1, 3, RoadForm.Dual, 12f, 6f, "CrossOneway");
            else
                p.SetRoad(1, 3, RoadForm.TwoWay, 20f, 0f, "Cross");
            p.Levels[0].Elevation = 10f;
            p.Levels[1].Elevation = 0f;
            return p;
        }

        [Theory]
        [InlineData(90f, false)]
        [InlineData(90f, true)]
        [InlineData(60f, false)]
        [InlineData(120f, true)]
        [InlineData(45f, false)]
        [InlineData(135f, false)]
        public void Generated_IsValidAndConnected(float angle, bool dualCross)
        {
            var p = Make(angle, dualCross);
            Assert.Empty(_template.ValidateParams(p));
            var segments = _template.Generate(p);
            Assert.Empty(InterchangeLayout.ValidateGeometry(segments, p));
            TestHelpers.AssertRampsConnected(segments);
            // 主线 4 端 + 辅路 4 端 + 被交道路（双向 2 端 / 双幅 4 端）
            Assert.Equal(dualCross ? 12 : 10, InterchangeLayout.LegEnds(segments).Count);
        }

        [Fact]
        public void Ramps_StayOnTheirSideAndTouchOnlyMainAndFrontage()
        {
            var p = Make();
            var segments = _template.Generate(p);
            var main = segments.Where(s => s.Role == RoadRole.MainRoad).SelectMany(s => new[] { s.Curve.P0, s.Curve.P3 }).ToHashSet();
            var front = segments.Where(s => s.Role == RoadRole.FrontageRoad).SelectMany(s => new[] { s.Curve.P0, s.Curve.P3 }).ToHashSet();
            var cross = segments.Where(s => s.Role == RoadRole.CrossRoad).SelectMany(s => new[] { s.Curve.P0, s.Curve.P3 }).ToHashSet();

            foreach (string dir in new[] { "NB", "SB" })
            {
                var c = segments.ToDictionary(s => s.Name, s => s.Curve);
                Assert.Contains(c[$"{dir}-off-shift"].P0, main);
                Assert.Contains(c[$"{dir}-off-merge"].P3, front);
                Assert.Contains(c[$"{dir}-on-diverge"].P0, front);
                Assert.Contains(c[$"{dir}-on-shift"].P3, main);
            }
            // 匝道不接被交道路：地面只有辅路与被交道路的平交口
            Assert.DoesNotContain(segments.Where(s => s.Role == RoadRole.Ramp).SelectMany(s => new[] { s.Curve.P0, s.Curve.P3 }), cross.Contains);
            // 辅路和被交道路共用交点
            Assert.Equal(2, front.Intersect(cross).Count());
        }

        [Fact]
        public void Ramps_AreTangentToMainAndFrontage()
        {
            var p = Make();
            Vec3 main = p.Legs[0].Direction;
            foreach (var s in _template.Generate(p).Where(s => s.Role == RoadRole.Ramp))
            {
                foreach (float t in new[] { 0f, 1f })
                {
                    Vec3 d = s.Curve.Derivative(t);
                    var flat = Vec3.Normalize(new Vec3(d.X, 0f, d.Z));
                    Assert.True(System.Math.Abs(System.Math.Abs(Directions.Dot(flat, main)) - 1f) < 1e-3f, $"{s.Name} is not parallel to the main line at t={t}");
                }
            }
        }

        [Fact]
        public void Landing_IsBeforeTheJunctionOnTheFrontageRoad()
        {
            var p = Make(60f);
            var c = _template.Generate(p).ToDictionary(s => s.Name, s => s.Curve);
            Vec3 main = p.Legs[0].Direction;
            // NB 辅路上：落地点（沿 NB 行驶方向）在平交口之前，上匝道起点在平交口之后
            float landing = Directions.Dot(c["NB-off-merge"].P3, main);
            float takeoff = Directions.Dot(c["NB-on-diverge"].P0, main);
            var junction = _template.Generate(p).Where(s => s.Name.StartsWith("FrontNB")).Select(s => s.Curve.P3)
                .Where(v => _template.Generate(p).Any(x => x.Role == RoadRole.CrossRoad && (x.Curve.P0 == v || x.Curve.P3 == v))).Single();
            float at = Directions.Dot(junction, main);
            Assert.True(landing < at - p.RampTerminalOffset + 1e-3f);
            Assert.True(takeoff > at + p.RampTerminalOffset - 1e-3f);
        }

        [Theory]
        [InlineData(8f, "路堤")]
        [InlineData(0f, "净空")]
        public void MainMustBeElevatedBridge(float mainElevation, string error)
        {
            var p = Make();
            p.Levels[0].Elevation = mainElevation;
            Assert.Contains(_template.ValidateParams(p), e => e.Contains(error));
        }

        [Fact]
        public void TwoWayMain_IsRejected()
        {
            var p = Make();
            p.SetRoad(0, 2, RoadForm.TwoWay, 20f, 0f, "Road");
            Assert.Contains(_template.ValidateParams(p), e => e.Contains("双幅"));
        }

        [Fact]
        public void IsRegistered()
        {
            Assert.IsType<ExpresswayNodeTemplate>(TemplateRegistry.Get(InterchangeType.ExpresswayNode));
        }

        [Fact]
        public void RampSlopes_StartAndEndLevel()
        {
            // 升降段两端水平，和前后的水平路段衔接时没有竖向折角
            var segments = _template.Generate(Make());
            foreach (var s in segments.Where(s => s.Name.EndsWith("-down") || s.Name.EndsWith("-up")))
            {
                Assert.True(System.Math.Abs(s.Curve.Derivative(0f).Y) < 1e-4f, $"{s.Name} start is not level");
                Assert.True(System.Math.Abs(s.Curve.Derivative(1f).Y) < 1e-4f, $"{s.Name} end is not level");
            }
        }

        [Fact]
        public void LaneBalance_ChangesMainBetweenRampsAndFrontageBetweenLandings()
        {
            var p = Make();
            p.MainReducedPrefab = "Highway Oneway 2";
            p.FrontagePrefab = "Front 2";
            p.FrontageWidenedPrefab = "Front 3";
            p.FrontageWidenedWidth = 16f;
            Assert.Empty(_template.ValidateParams(p));
            var segments = _template.Generate(p);
            Assert.Empty(InterchangeLayout.ValidateGeometry(segments, p));
            var c = segments.ToDictionary(s => s.Name);
            Vec3 main = p.Legs[0].Direction;

            foreach (var s in segments.Where(s => s.Role == RoadRole.MainRoad))
            {
                // NB/SB 两幅：下匝道分出点和上匝道汇入点之间是减车道的道路，之外是原道路
                float off = System.Math.Abs(Directions.Dot(c["NB-off-shift"].Curve.P0, main));
                float middle = System.Math.Abs(Directions.Dot(s.Curve.Evaluate(0.5f), main));
                Assert.Equal(middle < off ? "Highway Oneway 2" : "Highway Oneway", s.Prefab);
            }
            foreach (var s in segments.Where(s => s.Role == RoadRole.FrontageRoad))
            {
                float landing = System.Math.Abs(Directions.Dot(c["NB-off-merge"].Curve.P3, main));
                float middle = System.Math.Abs(Directions.Dot(s.Curve.Evaluate(0.5f), main));
                Assert.Equal(middle < landing ? "Front 3" : "Front 2", s.Prefab);
            }
        }

        [Fact]
        public void LaneBalance_KeepsTheThroughLaneEdgeAligned()
        {
            var p = Make();
            p.MainReducedPrefab = "Highway Oneway 2";
            p.MainReducedWidth = 12f;          // 主线 16 → 12：中段向分隔带平移 2 米，内侧边缘不变
            p.FrontagePrefab = "Front 2";
            p.FrontageWidth = 12f;
            p.FrontageWidenedPrefab = "Front 3";
            p.FrontageWidenedWidth = 16f;      // 辅路 12 → 16：加宽段向内平移 2 米，外侧边缘不变
            Assert.Empty(_template.ValidateParams(p));
            var segments = _template.Generate(p);
            Assert.Empty(InterchangeLayout.ValidateGeometry(segments, p));
            TestHelpers.AssertRampsConnected(segments);

            Vec3 axis = p.Legs[0].Direction;
            Vec3 side = Directions.RightOf(axis);
            float Lateral(Vec3 v) => System.Math.Abs(Directions.Dot(v, side));
            float om = p.Legs[0].CarriagewayOffset;

            var reduced = segments.Where(s => s.Prefab == "Highway Oneway 2").ToList();
            Assert.NotEmpty(reduced);
            // 路面全程直线，内侧边缘（|横向| − 半宽）与原道路相同
            foreach (var s in reduced)
                foreach (var end in new[] { s.Curve.P0, s.Curve.P3 })
                    Assert.Equal(om - 8f, Lateral(end) - 6f, 3);
            // 两端在匝道节点处边缘对齐：节点仍在原道路中心，曲线端点向分隔带偏 2 米（两幅 × 分流后、合流前）
            var aligned = reduced.Where(s => s.HasNodeOffset).ToList();
            Assert.Equal(4, aligned.Count);
            Assert.All(aligned, s => Assert.Contains(new[] { s.StartNode, s.EndNode }, v => System.Math.Abs(Lateral(v) - om) < 1e-3f));

            float og = segments.Where(s => s.Prefab == "Front 2").Max(s => Lateral(s.Curve.P0));
            var widened = segments.Where(s => s.Prefab == "Front 3").ToList();
            Assert.NotEmpty(widened);
            foreach (var s in widened)
                foreach (var end in new[] { s.Curve.P0, s.Curve.P3 })
                    Assert.Equal(og + 6f, Lateral(end) + 8f, 3);   // 外侧边缘不变
            Assert.Equal(4, widened.Count(s => s.HasNodeOffset));   // 落地点和上匝道分出点处边缘对齐
        }

        [Fact]
        public void SmallerRampGap_NarrowsTheFootprint()
        {
            float Width(float gap)
            {
                var p = Make();
                p.RampSideGap = gap;
                Vec3 side = Directions.RightOf(p.Legs[0].Direction);
                return _template.Generate(p).Where(s => s.Role == RoadRole.FrontageRoad)
                    .Max(s => System.Math.Abs(Directions.Dot(s.Curve.P0, side)));
            }
            // 两侧各有两处间距（匝道—主线、匝道—辅路）
            Assert.Equal(Width(3f) - 4f, Width(1f), 3);
        }
    }
}
