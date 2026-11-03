using System;
using System.Collections.Generic;
using System.Linq;
using InterchangeMod.Generation.Geometry;

namespace InterchangeMod.Generation.Endpoints
{
    /// <summary>A snapshot of an existing road end; the tangent points away from the interchange.</summary>
    public sealed class SelectedEndpoint
    {
        public SelectedEndpoint(int nodeIndex, int nodeVersion, Vec3 position, Vec3 outwardTangent,
            bool oneWay, bool travelOutward, float width, int lanes, string prefab)
        {
            NodeIndex = nodeIndex;
            NodeVersion = nodeVersion;
            Position = position;
            OutwardTangent = outwardTangent;
            OneWay = oneWay;
            TravelOutward = travelOutward;
            Width = width;
            Lanes = lanes;
            Prefab = prefab;
        }

        public int NodeIndex { get; }
        public int NodeVersion { get; }
        public Vec3 Position { get; }
        public Vec3 OutwardTangent { get; }
        public bool OneWay { get; }
        public bool TravelOutward { get; }
        public float Width { get; }
        public int Lanes { get; }
        public string Prefab { get; }
    }

    /// <summary>One direction of the interchange, independent of a connected junction.</summary>
    public sealed class EndpointArm
    {
        public EndpointArm(int index, IEnumerable<SelectedEndpoint> endpoints)
        {
            if (endpoints == null) throw new ArgumentNullException(nameof(endpoints));
            Index = index;
            Endpoints = Array.AsReadOnly(endpoints.ToArray());
        }

        public int Index { get; }
        public IReadOnlyList<SelectedEndpoint> Endpoints { get; }
    }

    public enum EndpointProblemCode
    {
        ArmCount, ArmIndex, InvalidPortCount, DuplicateNode, InvalidGeometry, InvalidRoad, InvalidDirection, LaneMismatch,
        UnknownType, InvalidGrade, InvalidRetreat,
    }

    public sealed class EndpointProblem
    {
        public EndpointProblem(EndpointProblemCode code, int armIndex)
        {
            Code = code;
            ArmIndex = armIndex;
        }

        public EndpointProblemCode Code { get; }
        /// <summary>-1 denotes a problem affecting the selection as a whole.</summary>
        public int ArmIndex { get; }
    }

    public static class EndpointConstraints
    {
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(Vec3 value) => Finite(value.X) && Finite(value.Y) && Finite(value.Z);

        public static IReadOnlyList<EndpointProblem> Validate(IReadOnlyList<EndpointArm> arms)
            => Validate(arms, InterchangeType.Cloverleaf);

        public static IReadOnlyList<EndpointProblem> Validate(IReadOnlyList<EndpointArm> arms, InterchangeType type)
        {
            if (arms == null) throw new ArgumentNullException(nameof(arms));
            var problems = new List<EndpointProblem>();
            var contract = EndpointTypeCatalog.Find(type);
            if (contract == null)
                return Array.AsReadOnly(new[] { new EndpointProblem(EndpointProblemCode.UnknownType, -1) });
            if (arms.Count != contract.ArmCount)
                problems.Add(new EndpointProblem(EndpointProblemCode.ArmCount, -1));

            var seenArms = new HashSet<int>();
            var seenNodes = new HashSet<(int, int)>();
            foreach (var arm in arms)
            {
                if (arm == null)
                {
                    problems.Add(new EndpointProblem(EndpointProblemCode.ArmIndex, -1));
                    continue;
                }
                if (arm.Index < 0 || arm.Index >= contract.ArmCount || !seenArms.Add(arm.Index))
                    problems.Add(new EndpointProblem(EndpointProblemCode.ArmIndex, arm.Index));

                if (arm.Endpoints.Count < 1 || arm.Endpoints.Count > 2 || arm.Endpoints.Any(p => p == null)
                    || (arm.Endpoints.Count == 1 && arm.Endpoints[0].OneWay)
                    || (arm.Endpoints.Count == 2 && arm.Endpoints.Any(p => !p.OneWay)))
                {
                    problems.Add(new EndpointProblem(EndpointProblemCode.InvalidPortCount, arm.Index));
                }
                else if (arm.Endpoints.Count == 2 && arm.Endpoints[0].TravelOutward == arm.Endpoints[1].TravelOutward)
                {
                    problems.Add(new EndpointProblem(EndpointProblemCode.InvalidDirection, arm.Index));
                }

                foreach (var port in arm.Endpoints)
                {
                    if (port == null) continue;
                    if (!seenNodes.Add((port.NodeIndex, port.NodeVersion)))
                        problems.Add(new EndpointProblem(EndpointProblemCode.DuplicateNode, arm.Index));
                    var d = port.OutwardTangent;
                    if (!Finite(port.Position) || !Finite(d) || d.X * d.X + d.Z * d.Z < 1e-10f)
                        problems.Add(new EndpointProblem(EndpointProblemCode.InvalidGeometry, arm.Index));
                    if (!Finite(port.Width) || port.Width <= 0f || port.Lanes <= 0 || string.IsNullOrWhiteSpace(port.Prefab))
                        problems.Add(new EndpointProblem(EndpointProblemCode.InvalidRoad, arm.Index));
                }
            }

            // Compare the same stream through the intersection: an incoming
            // port on one arm must match the outgoing port on its opposite.
            if (arms.Count == contract.ArmCount && seenArms.Count == contract.ArmCount
                && seenArms.All(i => i >= 0 && i < contract.ArmCount) && contract.Layout != EndpointLayout.Wye)
            {
                int Lanes(EndpointArm arm, bool outward) => arm.Endpoints.Count == 1
                    ? arm.Endpoints[0].Lanes : arm.Endpoints.First(p => p.TravelOutward == outward).Lanes;
                foreach (int a in contract.Layout == EndpointLayout.Crossroads ? new[] { 0, 1 } : new[] { 0 })
                {
                    var first = arms.First(arm => arm.Index == a);
                    var opposite = arms.First(arm => arm.Index == a + 2);
                    if (problems.Any(p => p.ArmIndex == first.Index || p.ArmIndex == opposite.Index))
                        continue;
                    if (Lanes(first, false) != Lanes(opposite, true) || Lanes(first, true) != Lanes(opposite, false))
                        problems.Add(new EndpointProblem(EndpointProblemCode.LaneMismatch, first.Index));
                }
            }
            return problems.AsReadOnly();
        }
    }
}
