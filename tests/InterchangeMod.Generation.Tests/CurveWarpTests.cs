using System.Linq;
using InterchangeMod.Generation.Geometry;
using InterchangeMod.Generation.Templates;
using Xunit;

namespace InterchangeMod.Generation.Tests
{
    public class CurveWarpTests
    {
        private static Vec3 Along(float angle, float s) => Directions.FromAngle(angle) * s;

        private static Vec3 Right(float angle, float w) => Directions.RightOf(Directions.FromAngle(angle)) * w;

        private static float Flat(Vec3 a, Vec3 b) => (float)System.Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z));

        [Fact]
        public void StraightRoads_LeaveEverythingUnchanged()
        {
            var warp = new CurveWarp(75f, 0f, 0f);
            Assert.True(warp.IsIdentity);
            var p = new Vec3(12f, 3f, -40f);
            Assert.Equal(p, warp.Apply(p));
        }

        [Theory]
        [InlineData(90f, 1f / 500f, 0f)]
        [InlineData(90f, -1f / 400f, 1f / 600f)]
        [InlineData(70f, 1f / 450f, -1f / 500f)]
        [InlineData(110f, -1f / 350f, 0f)]
        public void RoadAndItsCarriagewaysFollowParallelArcs(float crossing, float mainCurvature, float crossCurvature)
        {
            var warp = new CurveWarp(crossing, mainCurvature, crossCurvature);
            // 主线：中心线和两侧各幅（横距 ±10 米）沿 400 米落在同心圆上；横距 25 米处（匝道）在两种弯法的过渡区里，离中心 150 米起允许 0.6 米
            var center = Right(0f, 1f / mainCurvature);
            foreach (float w in new[] { 0f, 10f, -10f, 25f })
            {
                float radius = 1f / mainCurvature - w;
                foreach (float s in new[] { -400f, -150f, -40f, 60f, 200f, 400f })
                {
                    if (w != 0f && System.Math.Abs(s) < (System.Math.Abs(w) > 12f ? 150f : 60f))
                        continue; // 中心附近两种弯法交织：各幅从 60 米起、25 米横距的匝道从 150 米起才要求平行
                    var q = warp.Apply(Along(0f, s) + Right(0f, w));
                    Assert.True(System.Math.Abs(Flat(q, center) - System.Math.Abs(radius)) < (System.Math.Abs(w) > 12f ? 0.6f : 0.15f),
                        $"main w {w} s {s}: {Flat(q, center):F2} vs {System.Math.Abs(radius):F2}");
                }
            }
            // 被交道路离开主线走廊（60 米）后两种弯法逐渐过渡，离中心 200 米起要求 0.3 米以内
            if (crossCurvature == 0f)
            {
                // 被交道路是直线：中心线和各幅从 200 米起保持直线（不张开）
                foreach (float w in new[] { 0f, 10f, -10f })
                    foreach (float sign in new[] { 1f, -1f })
                    {
                        var start = warp.Apply(Along(crossing, sign * 200f) + Right(crossing, w));
                        var dir = Directions.FromAngle(crossing);
                        foreach (float s in new[] { 300f, 450f })
                        {
                            var q = warp.Apply(Along(crossing, sign * s) + Right(crossing, w));
                            float off = (q.X - start.X) * dir.Z - (q.Z - start.Z) * dir.X;
                            Assert.True(System.Math.Abs(off) < 0.3f, $"cross w {w} s {sign * s}: off {off:F2}");
                        }
                    }
                return;
            }
            var crossCenter = Right(crossing, 1f / crossCurvature);
            foreach (float w in new[] { 0f, 10f, -10f })
            {
                float radius = 1f / crossCurvature - w;
                foreach (float s in new[] { -300f, -200f, 200f, 300f })
                {
                    var q = warp.Apply(Along(crossing, s) + Right(crossing, w));
                    Assert.True(System.Math.Abs(Flat(q, crossCenter) - System.Math.Abs(radius)) < 0.3f,
                        $"cross w {w} s {s}: {Flat(q, crossCenter):F2} vs {System.Math.Abs(radius):F2}");
                }
            }
        }

        [Fact]
        public void ArcLengthIsKeptAlongTheMainRoad()
        {
            var warp = new CurveWarp(90f, 1f / 500f, 0f);
            Vec3 prev = warp.Apply(Vec3.Zero);
            float length = 0f;
            for (int i = 1; i <= 300; i++)
            {
                Vec3 q = warp.Apply(Along(0f, i));
                length += Flat(q, prev);
                prev = q;
            }
            Assert.InRange(length, 299.9f, 300.1f);
        }

        [Theory]
        [InlineData(1f / 500f, 0f)]
        [InlineData(-1f / 500f, 1f / 600f)]
        public void GeneratedInterchangeStaysConnectedAndSmooth(float mainCurvature, float crossCurvature)
        {
            var template = new DiamondTemplate();
            var p = new InterchangeParams();
            var segments = template.Generate(p);
            var warp = new CurveWarp(p.Legs[1].Angle, mainCurvature, crossCurvature);
            var warped = warp.Apply(segments);
            // 节点仍精确相等（同一个局部点得到同一个结果）
            TestHelpers.AssertRampsConnected(warped);
            // 弯曲后的贝塞尔与逐点弯曲的原曲线大体一致（穿过两种弯法过渡区的长匝道形状本来就不唯一，只防止离谱：2 米内）；
            // 端点高度不变，中间的高度随控制柄缩放略有变化
            for (int i = 0; i < segments.Count; i++)
                for (int k = 0; k <= 10; k++)
                {
                    float t = k / 10f;
                    Vec3 exact = warp.Apply(segments[i].Curve.Evaluate(t)), approx = warped[i].Curve.Evaluate(t);
                    Assert.True(Flat(exact, approx) < 2f, $"{segments[i].Name} t {t}: {Flat(exact, approx):F2} m");
                    Assert.True(System.Math.Abs(exact.Y - approx.Y) < (k == 0 || k == 10 ? 0.001f : 0.5f), $"{segments[i].Name} t {t}: height {exact.Y:F2} vs {approx.Y:F2}");
                }
            // 同一条路前后相接的路段（节点只接这两段）在共用节点处仍相切（俯视）
            foreach (var a in warped)
                foreach (var b in warped.Where(b => b != a && b.StartNode.Equals(a.EndNode)
                    && warped.Count(c => c.StartNode.Equals(a.EndNode) || c.EndNode.Equals(a.EndNode)) == 2))
                {
                    Vec3 u = a.Curve.Derivative(1f), v = b.Curve.Derivative(0f);
                    float cos = (u.X * v.X + u.Z * v.Z) / (float)System.Math.Sqrt((u.X * u.X + u.Z * u.Z) * (v.X * v.X + v.Z * v.Z));
                    Assert.True(cos > 0.9998f, $"{a.Name} -> {b.Name}: cos {cos:F5}");
                }
        }
    }
}
