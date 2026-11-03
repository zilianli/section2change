using System;
using System.Collections.Generic;

namespace InterchangeMod.Generation.Endpoints
{
    /// <summary>Logical points on a single one-way through carriageway.</summary>
    public enum CloverNodeKind { In, DirectExit, LoopMerge, LoopExit, DirectMerge, Out }
    public enum CloverEdgeKind { Main, DirectRamp, LoopRamp, SharedRamp, LeftRamp }

    public readonly struct CloverNode : IEquatable<CloverNode>
    {
        public CloverNode(int arm, CloverNodeKind kind) { Arm = arm; Kind = kind; }
        public int Arm { get; }
        public CloverNodeKind Kind { get; }
        public bool Equals(CloverNode other) => Arm == other.Arm && Kind == other.Kind;
        public override bool Equals(object? obj) => obj is CloverNode other && Equals(other);
        public override int GetHashCode() => unchecked((Arm * 397) ^ (int)Kind);
        public override string ToString() => $"{Arm}:{Kind}";
    }

    public readonly struct CloverEdge : IEquatable<CloverEdge>
    {
        public CloverEdge(CloverNode from, CloverNode to, CloverEdgeKind kind) { From = from; To = to; Kind = kind; }
        public CloverNode From { get; }
        public CloverNode To { get; }
        public CloverEdgeKind Kind { get; }
        public bool Equals(CloverEdge other) => From.Equals(other.From) && To.Equals(other.To) && Kind == other.Kind;
        public override bool Equals(object? obj) => obj is CloverEdge other && Equals(other);
        public override int GetHashCode() => unchecked(((From.GetHashCode() * 397) ^ To.GetHashCode()) * 397 ^ (int)Kind);
        public override string ToString() => $"{From}->{To} ({Kind})";
    }

    /// <summary>
    /// Abstract directed cloverleaf graph. A graph route is not proof that the
    /// game creates the corresponding lanes or that the geometry has clearance.
    /// Arms are clockwise; an inbound arm i continues to outbound arm i+2.
    /// </summary>
    public sealed class CloverleafTopology
    {
        private CloverleafTopology(List<CloverNode> nodes, List<CloverEdge> edges)
        {
            Nodes = nodes.AsReadOnly();
            Edges = edges.AsReadOnly();
        }

        public IReadOnlyList<CloverNode> Nodes { get; }
        public IReadOnlyList<CloverEdge> Edges { get; }

        public static CloverleafTopology Create(bool rightHandTraffic)
        {
            var nodes = new List<CloverNode>();
            var edges = new List<CloverEdge>();
            CloverNode N(int arm, CloverNodeKind kind) => new CloverNode(arm, kind);
            var kinds = new[] { CloverNodeKind.In, CloverNodeKind.DirectExit, CloverNodeKind.LoopMerge,
                CloverNodeKind.LoopExit, CloverNodeKind.DirectMerge, CloverNodeKind.Out };
            for (int i = 0; i < 4; i++)
                foreach (var kind in kinds)
                    nodes.Add(N(i, kind));

            for (int i = 0; i < 4; i++)
            {
                edges.Add(new CloverEdge(N(i, CloverNodeKind.In), N(i, CloverNodeKind.DirectExit), CloverEdgeKind.Main));
                edges.Add(new CloverEdge(N(i, CloverNodeKind.DirectExit), N(i, CloverNodeKind.LoopMerge), CloverEdgeKind.Main));
                edges.Add(new CloverEdge(N(i, CloverNodeKind.LoopMerge), N(i, CloverNodeKind.LoopExit), CloverEdgeKind.Main));
                edges.Add(new CloverEdge(N(i, CloverNodeKind.LoopExit), N(i, CloverNodeKind.DirectMerge), CloverEdgeKind.Main));
                edges.Add(new CloverEdge(N(i, CloverNodeKind.DirectMerge), N((i + 2) % 4, CloverNodeKind.Out), CloverEdgeKind.Main));
                edges.Add(new CloverEdge(N(i, CloverNodeKind.DirectExit), N((i + (rightHandTraffic ? 1 : 3)) % 4, CloverNodeKind.DirectMerge), CloverEdgeKind.DirectRamp));
                edges.Add(new CloverEdge(N(i, CloverNodeKind.LoopExit), N((i + (rightHandTraffic ? 3 : 1)) % 4, CloverNodeKind.LoopMerge), CloverEdgeKind.LoopRamp));
            }
            return new CloverleafTopology(nodes, edges);
        }

        /// <summary>Check completeness against either consistent traffic-hand layout.</summary>
        public static IReadOnlyList<string> Validate(IReadOnlyList<CloverEdge> edges)
        {
            if (edges == null) throw new ArgumentNullException(nameof(edges));
            var actual = new HashSet<CloverEdge>(edges);
            var right = new HashSet<CloverEdge>(Create(true).Edges);
            var left = new HashSet<CloverEdge>(Create(false).Edges);
            int Difference(HashSet<CloverEdge> expected)
            {
                int difference = 0;
                foreach (var edge in expected) if (!actual.Contains(edge)) difference++;
                foreach (var edge in actual) if (!expected.Contains(edge)) difference++;
                return difference;
            }
            var best = Difference(right) <= Difference(left) ? right : left;
            var errors = new List<string>();
            if (actual.Count != edges.Count) errors.Add("Duplicate directed edge");
            foreach (var edge in best) if (!actual.Contains(edge)) errors.Add($"Missing {edge}");
            foreach (var edge in actual) if (!best.Contains(edge)) errors.Add($"Unexpected {edge}");
            return errors.AsReadOnly();
        }
    }
}
