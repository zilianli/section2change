using System.Collections.Generic;
using System.Linq;
using InterchangeMod.Generation.Geometry;
using InterchangeMod.Generation.Templates;
using Xunit;

namespace InterchangeMod.Generation.Tests
{
    public class DualCarriagewayTests
    {
        private readonly DiamondTemplate _template = new DiamondTemplate();

        private static InterchangeParams Make(bool dualMain, bool dualCross, float crossingAngle = 90f, bool rightHand = true)
        {
            var p = new InterchangeParams
            {
                Legs = InterchangeParams.CrossLegs(crossingAngle, 0, 1),
                RightHandTraffic = rightHand,
            };
            if (dualMain)
                p.SetRoad(0, 2, RoadForm.Dual, 12f, 4f, "Oneway");
            if (dualCross)
                p.SetRoad(1, 3, RoadForm.Dual, 12f, 4f, "Oneway");
            return p;
        }

        private static Dictionary<string, Bezier3> ByName(IEnumerable<RoadSegmentSpec> segments) =>
            segments.ToDictionary(s => s.Name, s => s.Curve);

        public static IEnumerable<object[]> AllForms() => new[]
        {
            new object[] { false, false, 90f },
            new object[] { true, false, 90f },
            new object[] { false, true, 90f },
            new object[] { true, true, 90f },
            new object[] { true, true, 60f },
            new object[] { true, true, 120f },
        };

        [Theory]
        [MemberData(nameof(AllForms))]
        public void EveryRampEndpoint_IsConnected(bool dualMain, bool dualCross, float angle)
        {
            TestHelpers.AssertRampsConnected(_template.Generate(Make(dualMain, dualCross, angle)));
        }

        [Theory]
        [MemberData(nameof(AllForms))]
        public void AllForms_PassValidationAndGradeLimit(bool dualMain, bool dualCross, float angle)
        {
            // 与工具一致：先按坡度自动加长匝道
            var p = InterchangeLayout.FitRampLength(_template, Make(dualMain, dualCross, angle));
            Assert.Empty(_template.ValidateParams(p));
            Assert.Empty(InterchangeLayout.ValidateGeometry(_template.Generate(p), p));
        }

        [Fact]
        public void DualMain_HasTwoOneWayChainsInOppositeDirections()
        {
            var segments = _template.Generate(Make(dualMain: true, dualCross: false));
            var nb = segments.Where(s => s.Name.StartsWith("MainNB")).Select(s => s.Curve).ToList();
            var sb = segments.Where(s => s.Name.StartsWith("MainSB")).Select(s => s.Curve).ToList();
            Assert.Equal(5, nb.Count);
            Assert.Equal(5, sb.Count);

            foreach (var (chain, northbound) in new[] { (nb, true), (sb, false) })
            {
                for (int i = 1; i < chain.Count; i++)
                    Assert.Equal(chain[i - 1].P3, chain[i].P0);
                // 主线沿 +Z，NB 向北行驶，SB 向南
                Assert.All(chain, c => Assert.Equal(northbound, c.P3.Z > c.P0.Z));
            }
            // 右侧通行：NB 那幅在东侧（+X）
            Assert.All(nb, c => Assert.True(c.P0.X > 0f));
            Assert.All(sb, c => Assert.True(c.P0.X < 0f));
        }

        [Fact]
        public void DualMain_RampsDivergeAndMergeOnTheirOwnCarriageway()
        {
            var c = ByName(_template.Generate(Make(dualMain: true, dualCross: false)));

            // 分流：NB-diverge 从 MainNB-2 的终点（= MainNB-M1 起点）分出
            Assert.Equal(c["MainNB-2"].P3, c["NB-diverge"].P0);
            Assert.Equal(c["MainNB-M1"].P0, c["NB-diverge"].P0);
            // 合流：NB-merge 汇入 MainNB-M3（最后一段跨越后的路段）的终点
            Assert.Equal(c["MainNB-M3"].P3, c["NB-merge"].P3);
            // SB 同理
            Assert.Equal(c["MainSB-0"].P3, c["SB-diverge"].P0);
            Assert.Equal(c["MainSB-M1"].P3, c["SB-merge"].P3);
        }

        [Fact]
        public void DualMain_OffRampLeavesTangentiallyThenTurnsOutward()
        {
            var c = ByName(_template.Generate(Make(dualMain: true, dualCross: false)));
            // 向北行驶，右侧向东：起点与主线相切，终点切线向东偏
            Vec3 start = c["NB-diverge"].Derivative(0f);
            Vec3 end = c["NB-diverge"].Derivative(1f);
            Assert.True(start.Z > 0f && System.Math.Abs(start.X) < 1e-3f);
            Assert.True(end.Z > 0f && end.X > 0f);
        }

        [Fact]
        public void DualCross_OffRampLinkAndOnRampFormAThroughLine()
        {
            var p = Make(dualMain: false, dualCross: true);
            var c = ByName(_template.Generate(p));

            Assert.Equal(c["NB-off"].P3, c["NB-link"].P0);
            Assert.Equal(c["NB-link"].P3, c["NB-on"].P0);
            Assert.Equal(c["SB-off"].P3, c["SB-link"].P0);
            Assert.Equal(c["SB-link"].P3, c["SB-on"].P0);

            // 连接线两端分别落在两幅上（被交道路沿 +X，两幅分别在 Z 两侧）
            Assert.True(c["NB-link"].P0.Z * c["NB-link"].P3.Z < 0f);
            Assert.True(c["SB-link"].P0.Z * c["SB-link"].P3.Z < 0f);
        }

        [Fact]
        public void DualCross_CarriagewaysRunInOppositeDirections()
        {
            var segments = _template.Generate(Make(dualMain: false, dualCross: true));
            var pos = segments.Where(s => s.Name.StartsWith("CrossPos")).Select(s => s.Curve).ToList();
            var neg = segments.Where(s => s.Name.StartsWith("CrossNeg")).Select(s => s.Curve).ToList();

            for (int i = 1; i < pos.Count; i++)
            {
                Assert.Equal(pos[i - 1].P3, pos[i].P0);
                Assert.Equal(neg[i - 1].P3, neg[i].P0);
            }
            // 被交道路沿 +X（东）；向东那幅在其右侧（南，-Z）
            Assert.All(pos, cv => Assert.True(cv.P3.X > cv.P0.X && cv.P0.Z < 0f));
            Assert.All(neg, cv => Assert.True(cv.P3.X < cv.P0.X && cv.P0.Z > 0f));
        }

        [Fact]
        public void LeftHandTraffic_MirrorsDualCarriageways()
        {
            var segments = _template.Generate(Make(dualMain: true, dualCross: false, rightHand: false));
            Assert.All(segments.Where(s => s.Name.StartsWith("MainNB")), s => Assert.True(s.Curve.P0.X < 0f));
        }

        [Fact]
        public void MismatchedRoadEnds_AreRejected()
        {
            var p = Make(dualMain: true, dualCross: false);
            p.Legs[2].MedianWidth = 10f;
            Assert.Contains(_template.ValidateParams(p), e => e.Contains("一致"));
        }

        [Fact]
        public void WideDualMain_NeedsLargerTerminalOffset()
        {
            var p = Make(dualMain: true, dualCross: false);
            // 外缘 = (30 + 30) / 2 + 30 / 2 = 45 米，加匝道半宽 6 米和间距 1 米，超过默认接口偏移 40 米
            p.SetRoad(0, 2, RoadForm.Dual, 30f, 30f, "Oneway");
            Assert.Contains(_template.ValidateParams(p), e => e.Contains("接口"));

            p.RampTerminalOffset = 60f;
            p.RampLength = 360f;   // 宽主线的分流段更长，匝道要相应加长
            Assert.Empty(_template.ValidateParams(p));
        }

        [Theory]
        [MemberData(nameof(AllForms))]
        public void FitRampLength_ExtendsShortRampsUntilGradeFits(bool dualMain, bool dualCross, float angle)
        {
            var p = Make(dualMain, dualCross, angle);
            p.RampLength = 100f;
            Assert.NotEmpty(_template.ValidateParams(p));

            var fitted = InterchangeLayout.FitRampLength(_template, p);
            Assert.True(fitted.RampLength > 100f);
            Assert.Empty(_template.ValidateParams(fitted));
            Assert.Empty(InterchangeLayout.ValidateGeometry(_template.Generate(fitted), fitted));
            Assert.Equal(100f, p.RampLength);
        }

        [Fact]
        public void FitRampLength_LeavesUnfixableParamsAlone()
        {
            var p = Make(dualMain: true, dualCross: false);
            p.Levels[1].Elevation = 3f;   // 净空不足，加长匝道解决不了
            Assert.Same(p, InterchangeLayout.FitRampLength(_template, p));
        }

        [Fact]
        public void DualRoads_UseLegPrefabs()
        {
            var p = Make(dualMain: true, dualCross: true);
            Assert.All(_template.Generate(p).Where(s => s.Role != RoadRole.Ramp), s => Assert.Equal("Oneway", s.Prefab));
        }

        [Fact]
        public void LaneBalance_ShiftsReducedMainTowardMedian()
        {
            var p = InterchangeLayout.FitRampLength(_template, Make(dualMain: true, dualCross: false));
            float om = p.Legs[0].CarriagewayOffset;
            p.MainReducedPrefab = "Oneway 2";
            p.MainReducedWidth = p.Legs[0].RoadWidth - 4f;   // 中段窄 4 米 → 向分隔带平移 2 米
            Assert.Empty(_template.ValidateParams(p));
            var segments = _template.Generate(p);
            Assert.Empty(InterchangeLayout.ValidateGeometry(segments, p));
            TestHelpers.AssertRampsConnected(segments);

            Vec3 side = Directions.RightOf(p.Legs[0].Direction);
            var reduced = segments.Where(s => s.Prefab == "Oneway 2").ToList();
            Assert.NotEmpty(reduced);
            Assert.All(reduced.SelectMany(s => new[] { s.Curve.P0, s.Curve.P3 }),
                v => Assert.True(new[] { om, om - 2f }.Any(x => System.Math.Abs(System.Math.Abs(Directions.Dot(v, side)) - x) < 1e-3f)));
        }
    }
}
