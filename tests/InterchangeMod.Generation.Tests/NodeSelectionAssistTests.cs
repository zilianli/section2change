using System.Linq;
using InterchangeMod.Generation.Endpoints;
using InterchangeMod.Generation.Geometry;
using Xunit;

namespace InterchangeMod.Generation.Tests
{
    /// <summary>连接模式选点辅助：双幅自动配对另一幅、选齐一个方向后自动转到下一个方向。</summary>
    public class NodeSelectionAssistTests
    {
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void OppositeCarriageway_PicksTheOtherHalfOfTheSameArm(bool rht)
        {
            var arms = EndpointFixtures.DualCross(252, rht);
            var all = arms.SelectMany(a => a.Endpoints).ToList();
            foreach (var arm in arms)
                foreach (var picked in arm.Endpoints)
                {
                    var pair = NodeFeedback.OppositeCarriageway(picked, all);
                    Assert.NotNull(pair);
                    Assert.Contains(pair, arm.Endpoints);
                    Assert.NotEqual(picked.TravelOutward, pair!.TravelOutward);
                }
        }

        [Fact]
        public void OppositeCarriageway_IgnoresSameDirectionFarAndTwoWayEndpoints()
        {
            var picked = new SelectedEndpoint(1, 1, new Vec3(0, 0, 100), Vec3.UnitZ, true, false, 12, 2, "R");
            var candidates = new[]
            {
                new SelectedEndpoint(2, 1, new Vec3(20, 0, 100), Vec3.UnitZ, true, false, 12, 2, "R"),   // 同向
                new SelectedEndpoint(3, 1, new Vec3(20, 0, 160), Vec3.UnitZ, true, true, 12, 2, "R"),    // 沿路错开太远
                new SelectedEndpoint(4, 1, new Vec3(80, 0, 100), Vec3.UnitZ, true, true, 12, 2, "R"),    // 横向太远
                new SelectedEndpoint(5, 1, new Vec3(20, 0, 100), Vec3.UnitX, true, true, 12, 2, "R"),    // 不平行
                new SelectedEndpoint(6, 1, new Vec3(20, 0, 100), Vec3.UnitZ, false, true, 24, 2, "T"),   // 双向道路
            };
            Assert.Null(NodeFeedback.OppositeCarriageway(picked, candidates));
            var good = new SelectedEndpoint(7, 1, new Vec3(-24, 0, 102), Vec3.UnitZ, true, true, 12, 2, "R");
            Assert.Same(good, NodeFeedback.OppositeCarriageway(picked, candidates.Append(good)));
        }

        [Fact]
        public void NextIncompleteArm_SkipsCompletedDirections()
        {
            var full = EndpointFixtures.DualCross();
            var selection = new EndpointSelection(4);
            foreach (var p in full[0].Endpoints) selection.Toggle(0, p);
            foreach (var p in full[2].Endpoints) selection.Toggle(2, p);
            selection.Toggle(1, full[1].Endpoints[0]);
            var arms = selection.Snapshot();
            Assert.True(NodeFeedback.ArmComplete(arms[0]));
            Assert.False(NodeFeedback.ArmComplete(arms[1]));
            Assert.Equal(1, NodeFeedback.NextIncompleteArm(arms, 0));
            Assert.Equal(3, NodeFeedback.NextIncompleteArm(arms, 1));
            Assert.Equal(1, NodeFeedback.NextIncompleteArm(arms, 3));
            foreach (var p in full[1].Endpoints.Skip(1)) selection.Toggle(1, p);
            foreach (var p in full[3].Endpoints) selection.Toggle(3, p);
            Assert.Equal(2, NodeFeedback.NextIncompleteArm(selection.Snapshot(), 2));
        }
    }
}
