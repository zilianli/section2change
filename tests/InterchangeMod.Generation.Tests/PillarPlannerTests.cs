using System.Linq;
using Xunit;

namespace InterchangeMod.Generation.Tests
{
    public class PillarPlannerTests
    {
        [Theory]
        [InlineData(100f, 40f, 2)]   // 100 / 40 − 0.5 = 2
        [InlineData(55f, 40f, 0)]    // 段长 < 1.5 倍间距时没有中部立柱
        [InlineData(60f, 40f, 1)]
        public void EvenSpacing_FollowsGameRule(float length, float spacing, int expectedCount)
        {
            // 游戏：n = (int)(L / 间距 − 0.5)
            Assert.Equal(expectedCount, PillarPlanner.InnerPillars(0f, length, spacing, maxLength: 1000f).Count);
        }

        [Fact]
        public void DenseSpacingOverWideRoad_IsReportedAsImpossible()
        {
            // 每 20 米一根立柱、下层 30 米宽且没有分隔带：总有立柱落在路上
            var span = PillarPlanner.Plan(0f, 15f, 0f, 20f, 4f, 80f);
            Assert.NotNull(span.Error);
        }

        [Fact]
        public void LongSpan_IsSplitIntoEqualParts()
        {
            // 100 米、最长 80 米 → 等分 2 份，切点在 50 米处
            Assert.Equal(new[] { 50f }, PillarPlanner.InnerPillars(0f, 100f, null, 80f));
        }

        [Fact]
        public void NoMiddlePillars_GivesSymmetricSpan()
        {
            var span = PillarPlanner.Plan(0f, 10f, 0f, null, 4f, 80f);
            Assert.Null(span.Error);
            Assert.Equal(-span.Lo, span.Hi);
            Assert.True(span.Hi >= 10f + PillarPlanner.Margin);
        }

        [Fact]
        public void SingleMiddlePillar_GoesIntoWideMedian()
        {
            // 下层双幅占地 ±23，分隔带 ±7：正中一根 4 米宽的立柱放得进分隔带 → 保持对称
            var span = PillarPlanner.Plan(0f, 23f, 7f, 0f, 4f, 80f);
            Assert.Null(span.Error);
            Assert.Equal(-span.Lo, span.Hi);
        }

        [Fact]
        public void SingleMiddlePillar_WithoutMedian_ShiftsSpanOffCenter()
        {
            // 下层单幅占地 ±10，正中立柱会压在路上 → 跨越段向一侧加长，让中点越过下层道路
            var span = PillarPlanner.Plan(0f, 10f, 0f, 0f, 4f, 80f);
            Assert.Null(span.Error);
            float middle = (span.Lo + span.Hi) / 2f;
            Assert.True(System.Math.Abs(middle) >= 10f + 2f);
            Assert.True(span.Lo <= -10f - PillarPlanner.Margin && span.Hi >= 10f + PillarPlanner.Margin);
        }

        [Fact]
        public void EvenSpacing_ShortSpanHasNoMiddlePillar()
        {
            // 间距 40：对称跨越段 2 × (10 + 3 + 2) = 30 < 60，没有中部立柱
            var span = PillarPlanner.Plan(0f, 10f, 0f, 40f, 4f, 80f);
            Assert.Null(span.Error);
            Assert.Empty(PillarPlanner.InnerPillars(span.Lo, span.Hi, 40f, 80f));
        }

        [Theory]
        [InlineData(23f, 7f, 0f)]
        [InlineData(10f, 0f, 0f)]
        [InlineData(15f, 0f, 40f)]
        [InlineData(45f, 15f, null)]
        public void PlannedSpan_KeepsEveryPillarOffTheLowerRoad(float footprint, float medianHalf, float? spacing)
        {
            var span = PillarPlanner.Plan(0f, footprint, medianHalf, spacing, 4f, 80f);
            Assert.Null(span.Error);
            foreach (float pillar in PillarPlanner.InnerPillars(span.Lo, span.Hi, spacing, 80f).Concat(new[] { span.Lo, span.Hi }))
            {
                float offset = System.Math.Abs(pillar);
                Assert.True(offset >= footprint + 2f || offset + 2f <= medianHalf, $"pillar at {pillar:F1}");
            }
        }

        [Theory]
        [InlineData(null)]
        [InlineData(0f)]
        [InlineData(40f)]
        public void PlanNodes_PutsPillarsInGapsBetweenSeveralRoads(float? spacing)
        {
            // 桥下三处占地，总跨度约 130 米（超过 80 米的单跨上限），中间有两个空隙
            var occupied = new[] { (-70f, -55f), (-12f, 12f), (50f, 62f) };
            var plan = PillarPlanner.PlanNodes(occupied, spacing, 2f, 80f);
            Assert.Null(plan.Error);
            Assert.True(plan.Nodes[0] < -70f && plan.Nodes[plan.Nodes.Count - 1] > 62f);

            var pillars = plan.Nodes.ToList();
            for (int i = 0; i + 1 < plan.Nodes.Count; i++)
                pillars.AddRange(PillarPlanner.InnerPillars(plan.Nodes[i], plan.Nodes[i + 1], spacing, 80f));
            foreach (float x in pillars)
                Assert.All(occupied, z => Assert.True(x <= z.Item1 - 1f || x >= z.Item2 + 1f, $"pillar at {x} is on ({z.Item1}, {z.Item2})"));
        }

        [Fact]
        public void PlanNodes_FailsWhenOneRoadIsLongerThanTheSpanLimit()
        {
            var plan = PillarPlanner.PlanNodes(new[] { (-50f, 50f) }, null, 2f, 80f);
            Assert.NotNull(plan.Error);
        }
    }
}
