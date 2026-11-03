using System;
using System.Collections.Generic;
using System.Linq;

namespace InterchangeMod.Generation.Endpoints
{
    /// <summary>Immutable solver input. Input validity is independent of geometric feasibility.</summary>
    public sealed class EndpointTypeRequest
    {
        private EndpointTypeRequest(InterchangeType type, EndpointArm[] arms, bool rightHandTraffic,
            float maxGrade, float[] retreatLimits, EndpointProblem[] problems)
        {
            Type = type; Arms = Array.AsReadOnly(arms); RightHandTraffic = rightHandTraffic;
            MaxGrade = maxGrade; RetreatLimits = Array.AsReadOnly(retreatLimits); Problems = Array.AsReadOnly(problems);
        }
        public InterchangeType Type { get; }
        public IReadOnlyList<EndpointArm> Arms { get; }
        public bool RightHandTraffic { get; }
        public float MaxGrade { get; }
        /// <summary>Per-arm permitted retreat, not a verified demolition permission or traced road length.</summary>
        public IReadOnlyList<float> RetreatLimits { get; }
        public IReadOnlyList<EndpointProblem> Problems { get; }
        public bool InputReady => Problems.Count == 0;
        public bool IsPlacementReady => false;

        public static EndpointTypeRequest Create(InterchangeType type, IReadOnlyList<EndpointArm> arms,
            bool rightHandTraffic, float maxGrade, IReadOnlyList<float> retreatLimits)
        {
            if (arms == null) throw new ArgumentNullException(nameof(arms));
            if (retreatLimits == null) throw new ArgumentNullException(nameof(retreatLimits));
            var frozenArms = arms.ToArray(); var limits = retreatLimits.ToArray();
            var order=EndpointGridOrder.Identify(type,frozenArms)??EndpointTeeOrder.Identify(type,frozenArms);
            if(order!=null)
            {
                var selected=frozenArms.OrderBy(a=>a.Index).ToArray();
                frozenArms=order.Select((original,index)=>new EndpointArm(index,selected[original].Endpoints)).ToArray();
                if(limits.Length==order.Length)limits=order.Select(original=>limits[original]).ToArray();
            }
            var problems = EndpointConstraints.Validate(frozenArms, type).ToList();
            if (float.IsNaN(maxGrade) || float.IsInfinity(maxGrade) || maxGrade <= 0 || maxGrade > .30f)
                problems.Add(new EndpointProblem(EndpointProblemCode.InvalidGrade, -1));
            var contract = EndpointTypeCatalog.Find(type);
            if (contract == null || limits.Length != contract.ArmCount)
                problems.Add(new EndpointProblem(EndpointProblemCode.InvalidRetreat, -1));
            for (int i = 0; i < limits.Length; i++)
                if (float.IsNaN(limits[i]) || float.IsInfinity(limits[i]) || limits[i] < 0)
                    problems.Add(new EndpointProblem(EndpointProblemCode.InvalidRetreat, i));
            return new EndpointTypeRequest(type, frozenArms, rightHandTraffic, maxGrade, limits, problems.ToArray());
        }
    }
}
