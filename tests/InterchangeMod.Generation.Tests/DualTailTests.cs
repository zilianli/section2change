using System.Collections.Generic;
using System.Linq;
using InterchangeMod.Generation.Geometry;
using InterchangeMod.Generation.Templates;
using Xunit;

namespace InterchangeMod.Generation.Tests
{
    /// <summary>单点立交、部分苜蓿叶的被交道路在立交内是双向，外部是双幅时两端分回双幅。</summary>
    public class DualTailTests
    {
        private const float OneWayWidth = 8f, OuterMedian = 4f;

        private static void ExternalDual(InterchangeParams p)
        {
            foreach (int i in new[] { 1, 3 })
            {
                p.Legs[i].ExternalForm = RoadForm.Dual;
                p.Legs[i].ExternalPrefab = "Highway Oneway - 1 lane";
                p.Legs[i].ExternalWidth = OneWayWidth;
                p.Legs[i].ExternalMedian = OuterMedian;
            }
        }

        private static void AssertDualEnds(IReadOnlyList<RoadSegmentSpec> segments, InterchangeParams p)
        {
            // 被交道路两端各两个臂端（两幅），离被交道路中心线 (分隔带 + 单幅宽) / 2
            float offset = (OuterMedian + OneWayWidth) / 2f;
            Vec3 cross = p.Legs[1].Direction;
            var ends = InterchangeLayout.LegEnds(segments.Where(s => s.Role == RoadRole.CrossRoad)).ToList();
            Assert.Equal(4, ends.Count);
            foreach (var e in ends)
                Assert.Equal(offset, System.Math.Abs(e.X * cross.Z - e.Z * cross.X), 1);
            // 分出的两幅用外部的单行道路，接在双向道路的端点节点上
            var tails = segments.Where(s => s.Prefab == "Highway Oneway - 1 lane").ToList();
            Assert.Equal(4, tails.Count);
            var crossNodes = segments.Where(s => s.Role == RoadRole.CrossRoad && s.Prefab != "Highway Oneway - 1 lane")
                .SelectMany(s => new[] { s.StartNode, s.EndNode }).ToHashSet();
            Assert.All(tails, t => Assert.True(crossNodes.Contains(t.StartNode) || crossNodes.Contains(t.EndNode), t.Name));
            // 驶离立交的一幅（-out）在外指方向的行驶一侧：右侧通行在右、左侧通行在左；驶入的一幅（-in）在另一侧
            foreach (var t in tails)
            {
                bool outbound = t.Name.EndsWith("-out");
                Vec3 far = outbound ? t.EndNode : t.StartNode;
                Vec3 dir = new[] { p.Legs[1].Direction, p.Legs[3].Direction }.OrderByDescending(d => Directions.Dot(far, d)).First();
                float lateral = Directions.Dot(far, Directions.RightOf(dir));
                Assert.True(outbound == p.RightHandTraffic ? lateral > 0f : lateral < 0f,
                    $"{t.Name} is on the {(lateral > 0f ? "right" : "left")} ({(p.RightHandTraffic ? "right" : "left")}-hand traffic)");
            }
        }

        [Theory]
        [InlineData(90f, true)]
        [InlineData(70f, true)]
        [InlineData(90f, false)]
        [InlineData(70f, false)]
        public void SinglePoint(float angle, bool rightHand)
        {
            var p = new InterchangeParams { Legs = InterchangeParams.CrossLegs(angle, 0, 1), RampWidth = 8f, RightHandTraffic = rightHand };
            p.SetRoad(0, 2, RoadForm.Dual, 16f, 4f, "Highway Oneway");
            p.SetRoad(1, 3, RoadForm.TwoWay, 20f, 0f, "Cross");
            p.Levels[0].Elevation = 10f;
            p.Levels[1].Elevation = 0f;
            ExternalDual(p);
            var template = new SinglePointTemplate();
            Assert.Empty(template.ValidateParams(p));
            var segments = template.Generate(p);
            Assert.Empty(InterchangeLayout.ValidateGeometry(segments, p));
            AssertDualEnds(segments, p);
        }

        [Theory]
        [InlineData(90f, true, true)]
        [InlineData(90f, false, true)]
        [InlineData(75f, true, true)]
        [InlineData(90f, true, false)]
        [InlineData(75f, false, false)]
        public void PartialCloverleaf(float angle, bool loopsForExit, bool rightHand)
        {
            var p = new InterchangeParams
            {
                Legs = InterchangeParams.CrossLegs(angle, 0, 1),
                RampWidth = 8f,
                RightHandTraffic = rightHand,
                ParcloLoopsForExit = loopsForExit,
                SharedRampPrefab = "Highway Twoway",
                SharedRampWidth = 16f,
            };
            p.SetRoad(0, 2, RoadForm.Dual, 16f, 4f, "Highway Oneway");
            p.SetRoad(1, 3, RoadForm.TwoWay, 20f, 0f, "Medium Road");
            p.Levels[0].Elevation = 10f;
            p.Levels[1].Elevation = 0f;
            ExternalDual(p);
            var template = new PartialCloverleafTemplate();
            Assert.Empty(template.ValidateParams(p));
            var segments = template.Generate(p);
            Assert.Empty(InterchangeLayout.ValidateGeometry(segments, p));
            AssertDualEnds(segments, p);
        }
    }
}
