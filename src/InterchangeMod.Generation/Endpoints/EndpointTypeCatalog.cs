using System;
using System.Collections.Generic;
using System.Linq;

namespace InterchangeMod.Generation.Endpoints
{
    public enum EndpointLayout { Crossroads, Tee, Wye }

    /// <summary>A required terminal movement, not an implemented internal road or lane connection.</summary>
    public readonly struct EndpointMovement
    {
        public EndpointMovement(int fromArm, int toArm) { FromArm = fromArm; ToArm = toArm; }
        public int FromArm { get; }
        public int ToArm { get; }
    }

    public sealed class EndpointTypeContract
    {
        internal EndpointTypeContract(InterchangeType type, EndpointLayout layout,bool nodeOnly=false)
        {
            Type = type; Layout = layout;
            NodeOnly=nodeOnly;
            ArmCount = layout == EndpointLayout.Crossroads ? 4 : 3;
            RequiredMovements = Array.AsReadOnly(Enumerable.Range(0, ArmCount)
                .SelectMany(a => Enumerable.Range(0, ArmCount).Where(b => a != b)
                    .Select(b => new EndpointMovement(a, b))).ToArray());
        }
        public InterchangeType Type { get; }
        public EndpointLayout Layout { get; }
        public bool NodeOnly {get;}
        public int ArmCount { get; }
        public IReadOnlyList<EndpointMovement> RequiredMovements { get; }
    }

    /// <summary>Input contracts for existing section2change types. Registration does not enable placement.</summary>
    public static class EndpointTypeCatalog
    {
        private static readonly IReadOnlyList<EndpointTypeContract> contracts = Array.AsReadOnly(new[]
        {
            new EndpointTypeContract(InterchangeType.Diamond, EndpointLayout.Crossroads),
            new EndpointTypeContract(InterchangeType.ExpresswayNode, EndpointLayout.Crossroads),
            new EndpointTypeContract(InterchangeType.SinglePoint, EndpointLayout.Crossroads),
            new EndpointTypeContract(InterchangeType.Cloverleaf, EndpointLayout.Crossroads),
            new EndpointTypeContract(InterchangeType.PartialCloverleaf, EndpointLayout.Crossroads),
            new EndpointTypeContract(InterchangeType.Roundabout, EndpointLayout.Crossroads),
            new EndpointTypeContract(InterchangeType.Stack, EndpointLayout.Crossroads),
            new EndpointTypeContract(InterchangeType.Turbine, EndpointLayout.Crossroads),
            new EndpointTypeContract(InterchangeType.ExpresswayT, EndpointLayout.Tee),
            new EndpointTypeContract(InterchangeType.Trumpet, EndpointLayout.Tee),
            new EndpointTypeContract(InterchangeType.SemiDirectionalT, EndpointLayout.Tee),
            new EndpointTypeContract(InterchangeType.DirectionalT, EndpointLayout.Tee),
            new EndpointTypeContract(InterchangeType.Wye, EndpointLayout.Wye),
            new EndpointTypeContract(InterchangeType.CompactGrid, EndpointLayout.Crossroads,true),
        });
        public static IReadOnlyList<EndpointTypeContract> All => contracts;
        public static EndpointTypeContract? Find(InterchangeType type) => contracts.FirstOrDefault(c => c.Type == type);
        public static EndpointTypeContract Get(InterchangeType type) => Find(type) ?? throw new ArgumentOutOfRangeException(nameof(type));
    }
}
