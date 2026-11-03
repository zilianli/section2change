using System.Collections.Generic;
using System.Linq;
using InterchangeMod.Generation.Endpoints;
using Xunit;

namespace InterchangeMod.Generation.Tests
{
    public class CloverleafTopologyTests
    {
        private static bool Reach(IReadOnlyList<CloverEdge> edges, int fromArm, int toArm, int maxRamps)
        {
            var queue = new Queue<(CloverNode Node, int Ramps)>();
            var seen = new HashSet<(CloverNode Node, int Ramps)>();
            queue.Enqueue((new CloverNode(fromArm, CloverNodeKind.In), 0));
            while (queue.Count > 0)
            {
                var state = queue.Dequeue();
                if (!seen.Add(state)) continue;
                if (state.Node.Equals(new CloverNode(toArm, CloverNodeKind.Out))) return true;
                foreach (var edge in edges.Where(e => e.From.Equals(state.Node)))
                {
                    int ramps = state.Ramps + (edge.Kind == CloverEdgeKind.Main ? 0 : 1);
                    if (ramps <= maxRamps) queue.Enqueue((edge.To, ramps));
                }
            }
            return false;
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void EachApproachHasThreeCorrectDestinations(bool rightHandTraffic)
        {
            var topology = CloverleafTopology.Create(rightHandTraffic);
            for (int arm = 0; arm < 4; arm++)
            {
                Assert.True(Reach(topology.Edges, arm, (arm + 2) % 4, 0));
                Assert.True(Reach(topology.Edges, arm, (arm + 1) % 4, 1));
                Assert.True(Reach(topology.Edges, arm, (arm + 3) % 4, 1));
                Assert.False(Reach(topology.Edges, arm, arm, 1));
                var direct = topology.Edges.Single(e => e.From.Equals(new CloverNode(arm, CloverNodeKind.DirectExit)) && e.Kind == CloverEdgeKind.DirectRamp);
                var loop = topology.Edges.Single(e => e.From.Equals(new CloverNode(arm, CloverNodeKind.LoopExit)) && e.Kind == CloverEdgeKind.LoopRamp);
                Assert.Equal((arm + (rightHandTraffic ? 1 : 3)) % 4, direct.To.Arm);
                Assert.Equal((arm + (rightHandTraffic ? 3 : 1)) % 4, loop.To.Arm);
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void MissingLoopMakesItsLeftOrRightMovementUnreachable(bool rightHandTraffic)
        {
            var topology = CloverleafTopology.Create(rightHandTraffic);
            var broken = topology.Edges.Where(e => !(e.Kind == CloverEdgeKind.LoopRamp && e.From.Arm == 0)).ToList();
            int target = rightHandTraffic ? 1 : 3;
            Assert.False(Reach(broken, 0, target, 1));
            Assert.True(Reach(broken, 0, 2, 0));
            Assert.True(Reach(broken, 0, rightHandTraffic ? 3 : 1, 1));
            Assert.NotEmpty(CloverleafTopology.Validate(broken));
            Assert.Empty(CloverleafTopology.Validate(topology.Edges));
        }

        [Fact]
        public void RejectsDisconnectedInvalidArmAndMixedTrafficHandedness()
        {
            var right = CloverleafTopology.Create(true);
            var invalid = right.Edges.Append(new CloverEdge(new CloverNode(4, CloverNodeKind.In),
                new CloverNode(4, CloverNodeKind.Out), CloverEdgeKind.Main)).ToList();
            Assert.NotEmpty(CloverleafTopology.Validate(invalid));

            var mixed = right.Edges.Where(e => !(e.Kind == CloverEdgeKind.DirectRamp && e.From.Arm == 0)).ToList();
            mixed.Add(CloverleafTopology.Create(false).Edges.Single(e => e.Kind == CloverEdgeKind.DirectRamp && e.From.Arm == 0));
            Assert.NotEmpty(CloverleafTopology.Validate(mixed));
        }

        [Fact]
        public void ReturnedCollectionsCannotBeMutated()
        {
            var topology = CloverleafTopology.Create(true);
            Assert.Throws<System.NotSupportedException>(() => ((ICollection<CloverEdge>)topology.Edges).Clear());
            Assert.Throws<System.NotSupportedException>(() => ((ICollection<CloverNode>)topology.Nodes).Clear());
        }
    }
}
