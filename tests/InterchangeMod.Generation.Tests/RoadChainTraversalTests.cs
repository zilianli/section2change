using System.Linq;
using InterchangeMod.Generation.Geometry;
using Xunit;

namespace InterchangeMod.Generation.Tests
{
    public class RoadChainTraversalTests
    {
        [Fact]
        public void FourLongCarriageways_AllOuterEndsRemainCandidates()
        {
            var result = RoadChainTraversal.Collect(new[] { 0, 100, 200, 300 },
                edge => edge % 100 < 49 ? new[] { edge + 1 } : new int[0], _ => true);
            Assert.Equal(200, result.Count);
            foreach (var end in new[] { 49, 149, 249, 349 }) Assert.Contains(end, result);
        }

        [Fact]
        public void CycleAndDuplicateSeeds_AreVisitedOnce()
        {
            int visits = 0;
            var result = RoadChainTraversal.Collect(new[] { 0, 0, 5 }, edge => {
                visits++; return new[] { (edge + 1) % 10, (edge + 9) % 10 };
            }, _ => true);
            Assert.Equal(10, result.Count);
            Assert.Equal(10, visits);
        }

        [Fact]
        public void FootprintBoundary_PreventsTraversalToDistantRoads()
        {
            var result = RoadChainTraversal.Collect(new[] { 0 }, edge => new[] { edge + 1 }, edge => edge <= 100);
            Assert.Equal(101, result.Count);
            Assert.DoesNotContain(101, result);
        }

        [Fact]
        public void DeadEndOrJunction_DoesNotExpand()
        {
            var result = RoadChainTraversal.Collect(new[] { 7 }, _ => Enumerable.Empty<int>(), _ => true);
            Assert.Equal(new[] { 7 }, result);
        }
    }
}
