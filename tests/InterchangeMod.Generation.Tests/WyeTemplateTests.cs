using System.Collections.Generic;
using System.Linq;
using InterchangeMod.Generation.Geometry;
using InterchangeMod.Generation.Templates;
using Xunit;

namespace InterchangeMod.Generation.Tests
{
    public class WyeTemplateTests
    {
        private readonly WyeTemplate _template = new WyeTemplate();

        private const float RoadWidth = 20f, RampWidth = 12f;

        /// <summary>三条 4 车道双幅公路（20 米，分隔带 8 米）在地面汇于一处，臂 1、臂 2 的角度给定，2 车道匝道 12 米，跨线桥高 10 米。</summary>
        private static InterchangeParams Make(float angle1 = 120f, float angle2 = 240f, float grade = 0.08f, float baseElevation = 0f, float median = 8f,
            float[]? medians = null)
        {
            var p = new InterchangeParams
            {
                Legs = new List<LegSpec> { new LegSpec(0f, 0), new LegSpec(angle1, 0), new LegSpec(angle2, 0) },
                RampWidth = RampWidth,
                RampSideGap = 2f,
                MaxGrade = grade,
                RampPrefab = "Highway Oneway - 2 lanes",
            };
            for (int i = 0; i < 3; i++)
            {
                var leg = p.Legs[i];
                leg.Form = RoadForm.Dual;
                leg.RoadWidth = RoadWidth;
                leg.MedianWidth = medians?[i] ?? median;
                leg.RoadPrefab = "Highway Oneway - 4 lanes";
                leg.StubLength = 80f;
            }
            p.Levels[0].Elevation = baseElevation;
            p.Levels[1].Elevation = baseElevation + 10f;
            return p;
        }

        public static IEnumerable<object[]> Cases() => new[]
        {
            new object[] { 120f, 240f, 0.08f },
            new object[] { 120f, 240f, 0.06f },
            new object[] { 110f, 235f, 0.08f },
            new object[] { 130f, 250f, 0.07f },
            new object[] { 100f, 230f, 0.08f },
        };

        private (IReadOnlyList<RoadSegmentSpec> Segments, InterchangeParams P) Solve(float angle1, float angle2, float grade)
        {
            var p = Make(angle1, angle2, grade);
            var log = new List<string>();
            WyeTemplate.Trace = log.Add;
            var errors = new WyeTemplate().ValidateParams(p);
            WyeTemplate.Trace = null;
            Assert.True(errors.Count == 0, string.Join("; ", errors.Concat(log.Take(20))));
            var segments = _template.Generate(p);
            if (System.Environment.GetEnvironmentVariable("WYE_PREVIEW") is string dir)
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, $"wye_{angle1:F0}_{angle2:F0}_{grade * 100:F0}.svg"), SvgPreview.Render(segments));
            return (segments, p);
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void Generated_IsValidAndConnected(float angle1, float angle2, float grade)
        {
            var (segments, p) = Solve(angle1, angle2, grade);
            Assert.Empty(InterchangeLayout.ValidateGeometry(segments, p));
            TestHelpers.AssertRampsConnected(segments);
            // 三条路各两幅：六个臂端
            Assert.Equal(6, InterchangeLayout.LegEnds(segments).Count);
            // 六条匝道：三条右转一直在基层，三条左转升到跨线桥再降回
            foreach (var name in new[] { "RightAB", "RightBC", "RightCA", "RightAC", "RightBA", "RightCB" })
                if (segments.Any(sg => sg.Name.StartsWith(name + "-")))
                    Assert.All(segments.Where(sg => sg.Name.StartsWith(name + "-")), sg => Assert.True(sg.Curve.P0.Y < 0.01f && sg.Curve.P3.Y < 0.01f, sg.Name));
            Assert.Equal(3, segments.Select(sg => sg.Name).Where(n => n.StartsWith("Right")).Select(n => n.Substring(0, n.IndexOf('-'))).Distinct().Count());
            var lefts = segments.Where(sg => sg.Name.StartsWith("Left")).GroupBy(sg => sg.Name.Substring(0, sg.Name.IndexOf('-'))).ToList();
            Assert.Equal(3, lefts.Count);
            foreach (var left in lefts)
                Assert.InRange(left.Max(sg => System.Math.Max(sg.Curve.P0.Y, sg.Curve.P3.Y)), 9.9f, 10.1f);
            // 高架段每段短于游戏切分高架路段的 80 米
            foreach (var sg in segments.Where(sg => sg.Curve.P0.Y > 8.4f || sg.Curve.P3.Y > 8.4f))
                Assert.True(sg.Curve.Length() < 80f, $"{sg.Name} is {sg.Curve.Length():F0} m");
        }

        /// <summary>游戏里公路单行道路的宽度（米）。</summary>
        private static float Width(RoadSegmentSpec s) => s.Role == RoadRole.Ramp ? RampWidth : RoadWidth;

        [Theory]
        [MemberData(nameof(Cases))]
        public void NoTwoRoadsOverlapAtTheSameHeight(float angle1, float angle2, float grade)
        {
            AssertNoOverlaps(Solve(angle1, angle2, grade).Segments);
        }

        private static void AssertNoOverlaps(IReadOnlyList<RoadSegmentSpec> segments)
        {
            // 同一高度（2 米内）、不共用节点的两段路面不重叠（同一条路 / 匝道相隔一段的两段在弯道上挨着，游戏不报，不查）
            string Chain(RoadSegmentSpec s) => s.Name.Substring(0, s.Name.LastIndexOf('-'));
            var samples = segments.Select(s => Enumerable.Range(0, 41).Select(i => s.Curve.Evaluate(i / 40f)).ToArray()).ToArray();
            for (int i = 0; i < segments.Count; i++)
                for (int j = i + 1; j < segments.Count; j++)
                {
                    var a = segments[i];
                    var b = segments[j];
                    var nodes = new[] { a.StartNode, a.EndNode };
                    if (nodes.Contains(b.StartNode) || nodes.Contains(b.EndNode) || Chain(a) == Chain(b))
                        continue;
                    float reach = (Width(a) + Width(b)) / 2f - 0.5f;
                    foreach (var u in samples[i])
                        foreach (var v in samples[j])
                        {
                            float dx = u.X - v.X, dz = u.Z - v.Z;
                            Assert.False(dx * dx + dz * dz < reach * reach && System.Math.Abs(u.Y - v.Y) < 2f,
                                $"{a.Name} and {b.Name} overlap near ({u.X:F0}, {u.Y:F1}, {u.Z:F0})");
                        }
                }
        }

        public static IEnumerable<object[]> CurvedLegs() => new[]
        {
            new object[] { 1f / 600f, 0f, 0f, 0f },
            new object[] { 0f, -1f / 500f, 1f / 800f, 0f },
            new object[] { 1f / 900f, 1f / 700f, -1f / 600f, 10f },
        };

        [Theory]
        [MemberData(nameof(CurvedLegs))]
        public void OnCurvedRoads_EachLegFollowsItsCurve(float curvature0, float curvature1, float curvature2, float baseElevation)
        {
            // 三条臂各沿自己的弯道弯曲：臂端（离中心 400 多米）两幅精确落在弯道中心线两侧原来的半幅间距处
            var p = Make(120f, 240f, 0.08f, baseElevation: baseElevation);
            var lines = new[] { curvature0, curvature1, curvature2 }.Select(k => k == 0f ? Centerline.Straight : Centerline.Arc(k)).ToList();
            var warp = new CurveWarp(p.Legs.Select(l => l.Angle).ToList(), lines);
            var segments = InterchangeLayout.Bend(_template, p, warp, out InterchangeParams used);
            if (System.Environment.GetEnvironmentVariable("WYE_PREVIEW") is string dir)
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, $"wye_curved_{curvature0 * 1e4:F0}_{curvature1 * 1e4:F0}_{curvature2 * 1e4:F0}.svg"), SvgPreview.Render(segments));
            var errors = InterchangeLayout.ValidateGeometry(segments, p);
            Assert.True(errors.Count == 0, $"used {used.MaxGrade:P1}: " + string.Join("; ", errors));
            TestHelpers.AssertRampsConnected(segments);
            AssertNoOverlaps(segments);
            var ends = InterchangeLayout.LegEnds(segments);
            Assert.Equal(6, ends.Count);
            float half = (RoadWidth + 8f) / 2f;
            foreach (var end in ends)
            {
                int i = Enumerable.Range(0, 3).OrderBy(k => Vec3.Distance(Flat(end), Flat(lines[k].Point(450f).RotateY(p.Legs[k].Angle * Geometry.Directions.DegToRad)))).First();
                float yaw = p.Legs[i].Angle * Geometry.Directions.DegToRad;
                // 离中心线最近的点（每 0.5 米取一个）
                float best = float.MaxValue;
                for (float s = 300f; s <= 600f; s += 0.5f)
                    best = System.Math.Min(best, Vec3.Distance(Flat(end), Flat(lines[i].Point(s).RotateY(yaw))));
                Assert.InRange(best, half - 0.05f, half + 0.05f);
            }
        }

        private static Vec3 Flat(Vec3 v) => new Vec3(v.X, 0f, v.Z);

        [Theory]
        [InlineData(120f, 240f, 0f)]
        [InlineData(126f, 240f, 0f)]
        [InlineData(126f, 240f, 7f)]
        public void ElevatedBase_RampsKeepPillarClearance(float angle1, float angle2, float median)
        {
            // 游戏内：对准 10 米高架的三岔路口（主路分隔带 0 米）时，分岔后两条匝道并行段被切开、里侧两条左转匝道太近，
            // 报“与现有物体重叠”“形状无效”、立柱重叠。同一高度、不共用节点的两条匝道中线至少相距匝道宽 + 6 米（立柱横梁比桥面宽）
            var p = Make(angle1, angle2, 0.08f, baseElevation: 10f, median: median);
            var log = new List<string>();
            WyeTemplate.Trace = log.Add;
            var errors = new WyeTemplate().ValidateParams(p);
            WyeTemplate.Trace = null;
            Assert.True(errors.Count == 0, string.Join("; ", errors.Concat(log.Take(20))));
            var segments = _template.Generate(p);
            if (System.Environment.GetEnvironmentVariable("WYE_PREVIEW") is string dir)
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, $"wye_elevated_{angle1:F0}_{angle2:F0}_m{median:F0}.svg"), SvgPreview.Render(segments));
            Assert.Empty(InterchangeLayout.ValidateGeometry(segments, p));
            TestHelpers.AssertRampsConnected(segments);
            foreach (var sg in segments)
                Assert.True(sg.Curve.Length() < 80f, $"{sg.Name} is {sg.Curve.Length():F0} m");
            string Chain(RoadSegmentSpec s) => s.Name.Substring(0, s.Name.LastIndexOf('-'));
            var ramps = segments.Where(s => s.Role == RoadRole.Ramp).ToList();
            var samples = ramps.Select(s => Enumerable.Range(0, 21).Select(i => s.Curve.Evaluate(i / 20f)).ToArray()).ToArray();
            for (int i = 0; i < ramps.Count; i++)
                for (int j = i + 1; j < ramps.Count; j++)
                {
                    var a = ramps[i];
                    var b = ramps[j];
                    var nodes = new[] { a.StartNode, a.EndNode };
                    if (nodes.Contains(b.StartNode) || nodes.Contains(b.EndNode) || Chain(a) == Chain(b))
                        continue;
                    foreach (var u in samples[i])
                        foreach (var v in samples[j])
                        {
                            float dx = u.X - v.X, dz = u.Z - v.Z;
                            Assert.False(dx * dx + dz * dz < (RampWidth + 6f) * (RampWidth + 6f) && System.Math.Abs(u.Y - v.Y) < 2f,
                                $"{a.Name} and {b.Name} are {System.Math.Sqrt(dx * dx + dz * dz):F1} m apart near ({u.X:F0}, {u.Y:F1}, {u.Z:F0})");
                        }
                }
        }

        [Theory]
        [InlineData(116.4f, 233.1f, 12f, 0f, 7.95f)]
        [InlineData(104.6f, 233.1f, 11f, 0f, 7.95f)]
        [InlineData(101.6f, 216.2f, 9f, 10f, 8f)]
        [InlineData(120f, 240f, 0f, 7.6f, 8.3f)]
        public void ElevatedGameJunctions_NodesStayApartAndLegsConnect(float angle1, float angle2, float medianA, float medianB, float medianC)
        {
            // 游戏内对准 10 米高架的三岔路口：左转匝道分岔并行段终点后 3–5 米就是起坡点，两个节点的立柱重叠；
            // 分隔带约 8 米的那条路两幅只需拉开几厘米，没加 S 形却仍按拉开后的位置接匝道，分岔点和臂端成了两个挨着的节点
            var p = Make(angle1, angle2, 0.08f, baseElevation: 10f, medians: new[] { medianA, medianB, medianC });
            var log = new List<string>();
            WyeTemplate.Trace = log.Add;
            var errors = new WyeTemplate().ValidateParams(p);
            WyeTemplate.Trace = null;
            Assert.True(errors.Count == 0, string.Join("; ", errors.Concat(log.Take(20))));
            var segments = _template.Generate(p);
            Assert.Empty(InterchangeLayout.ValidateGeometry(segments, p));
            TestHelpers.AssertRampsConnected(segments);
            Assert.Equal(6, InterchangeLayout.LegEnds(segments).Count);
            foreach (var sg in segments)
                Assert.True(sg.Curve.Length() >= 10f && sg.Curve.Length() < 80f, $"{sg.Name} is {sg.Curve.Length():F1} m");
        }

        [Theory]
        [InlineData(90f, 180f)]
        [InlineData(160f, 260f)]
        public void LegsTooFarFromEvenlySpaced_AreRejected(float angle1, float angle2)
        {
            Assert.NotEmpty(_template.ValidateParams(Make(angle1, angle2)));
        }
    }
}
