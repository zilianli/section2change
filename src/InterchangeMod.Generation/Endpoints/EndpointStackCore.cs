using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using InterchangeMod.Generation.Geometry;
using InterchangeMod.Generation.Roads;
using InterchangeMod.Generation.Templates;

namespace InterchangeMod.Generation.Endpoints
{
    public sealed class EndpointCorePort
    {
        public EndpointCorePort(int arm, bool travelOutward, Vec3 position, Vec3 tangent, string prefab, float width)
        { Arm = arm; TravelOutward = travelOutward; Position = position; Tangent = tangent; Prefab = prefab; Width = width; }
        public int Arm { get; }
        public bool TravelOutward { get; }
        public Vec3 Position { get; }
        /// <summary>The curve's direction of travel, at both incoming and outgoing ports.</summary>
        public Vec3 Tangent { get; }
        public string Prefab { get; }
        public float Width { get; }
    }

    public sealed class EndpointStackCoreResult
    {
        internal EndpointStackCoreResult(bool success, string diagnostic, IReadOnlyList<SpatialSegment> segments, IReadOnlyList<EndpointCorePort> ports,Vec3? referenceCenter=null,bool coreFollowsCurve=false)
        { Success = success; Diagnostic = diagnostic; Segments = segments; Ports = ports; ReferenceCenter=referenceCenter; CoreFollowsCurve=coreFollowsCurve; }
        public bool Success { get; }
        public string Diagnostic { get; }
        public IReadOnlyList<SpatialSegment> Segments { get; }
        public IReadOnlyList<EndpointCorePort> Ports { get; }
        public Vec3? ReferenceCenter {get;}
        public bool CoreFollowsCurve {get;}
    }

    public static class EndpointStackCore
    {
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool ValidRange(HeightRange? range) => !range.HasValue
            || (Finite(range.Value.Min) && Finite(range.Value.Max) && range.Value.Min <= range.Value.Max);
        private static EndpointStackCoreResult Fail(string diagnostic) => new EndpointStackCoreResult(false,
            diagnostic, Array.Empty<SpatialSegment>(), Array.Empty<EndpointCorePort>());
        private static Vec3 Flat(Vec3 value) => new Vec3(value.X, 0, value.Z);
        private static Vec3 Direction(EndpointArm arm) => Vec3.Normalize(arm.Endpoints
            .Select(p => Vec3.Normalize(Flat(p.OutwardTangent))).Aggregate(Vec3.Zero, (a, b) => a + b));

        /// <summary>
        /// Re-solves the legacy analytic core using the selected roads. The transform is
        /// rigid, with an X reflection for LHT; it never scales an already-built layout.
        /// External roads and their original endpoint heights are handled by the caller.
        /// </summary>
        public static EndpointStackCoreResult Create(IReadOnlyList<EndpointArm> arms, InterchangeType type,
            bool rightHandTraffic, float mainHeight, float crossHeight, RoadInfo ramp, float maxGrade,
            CloverleafRoadDesign design, bool leftExit = false)
        {
            var failures=new List<string>();
            foreach(var trial in Candidates(arms,type,rightHandTraffic,mainHeight,crossHeight,ramp,maxGrade,design,leftExit))
            {
                if(trial.Success)return trial;
                failures.Add(trial.Diagnostic);
            }
            return Fail(string.Join("; ",failures));
        }

        /// <summary>Lazy, bounded alternatives for joint external-road matching.</summary>
        internal static IEnumerable<EndpointStackCoreResult> Candidates(IReadOnlyList<EndpointArm> arms,InterchangeType type,
            bool rightHandTraffic,float mainHeight,float crossHeight,RoadInfo ramp,float maxGrade,CloverleafRoadDesign design,bool leftExit=false,CancellationToken cancellation=default)
            =>ReferenceCandidates(arms,type,rightHandTraffic,mainHeight,crossHeight,ramp,maxGrade,design,leftExit,false,cancellation);

        internal static IEnumerable<EndpointStackCoreResult> CutCandidates(IReadOnlyList<EndpointArm> arms,InterchangeType type,
            bool rightHandTraffic,float mainHeight,float crossHeight,RoadInfo ramp,float maxGrade,CloverleafRoadDesign design,bool leftExit,CancellationToken cancellation=default)
            =>ReferenceCandidates(arms,type,rightHandTraffic,mainHeight,crossHeight,ramp,maxGrade,design,leftExit,true,cancellation);

        private static IEnumerable<EndpointStackCoreResult> ReferenceCandidates(IReadOnlyList<EndpointArm> arms,InterchangeType type,
            bool rightHandTraffic,float mainHeight,float crossHeight,RoadInfo ramp,float maxGrade,CloverleafRoadDesign design,bool leftExit,bool cutsOnly,CancellationToken cancellation)
        {
            if (!Finite(maxGrade) || maxGrade <= 0 || maxGrade > .30f)
            {yield return Fail("Invalid maximum core grade.");yield break;}
            // The unwarped template is identical across bend strengths. Keep its
            // per-parameter plan cache local to this reference enumeration.
            var sharedTemplate=new StackTemplate(turbine:type==InterchangeType.Turbine);
            // Different grade inputs produce genuinely different ramp lengths and cut
            // plans for a stack. A turbine's small rings can remain unchanged across
            // grades, so re-solve with larger geometric separation instead. Never form
            // a grade-by-gap search. A final bounded left-exit fallback widens only the
            // newly solved core median; the caller adapts to the unchanged real roads.
            // Two bounded reference poses: original tangent axes, then the secants
            // joining opposite selected arm centers. Curved approaches need not lie
            // on the original endpoints' tangent lines far away from those endpoints.
            var alternatives = type == InterchangeType.Turbine
                ? new[] { 3f, 4f, 5f, 6f, 8f, 12f }.Select(gap => (GradeFactor: 1f, SideGap: gap, MedianExtra: 0f))
                : new[] { 3f, 4f, 5f, 6f }.Select(gap => (GradeFactor: 1f, SideGap: gap, MedianExtra: 0f))
                    .Concat(new[] { .9f, .8f, .7f, .6f }.Select(factor => (GradeFactor: factor, SideGap: 3f, MedianExtra: 0f)))
                    .Concat((leftExit ? new[] { 24f, 32f, 40f } : Array.Empty<float>())
                        .Select(extra => (GradeFactor: 1f, SideGap: 3f, MedianExtra: extra)));
            foreach(bool secantReference in cutsOnly?new[]{true}:new[]{false,true})
            foreach (var alternative in alternatives)
            foreach(float bendStrength in cutsOnly?new[]{1f,.5f,.25f,0f}:new[]{0f})
            {
                cancellation.ThrowIfCancellationRequested();
                bool followCurve=bendStrength>0;
                float factor = alternative.GradeFactor, sideGap = alternative.SideGap, medianExtra = alternative.MedianExtra;
                string label = $"{(secantReference?"secant":"tangent")} reference, bend={bendStrength:F2}, grade factor {factor:F1}, side gap {sideGap:F0}m, median extra {medianExtra:F0}m";
                var trial = CreateAtReference(arms, type, rightHandTraffic, mainHeight, crossHeight, ramp,
                    maxGrade * factor, design, leftExit, sideGap, medianExtra,secantReference,followCurve,bendStrength,sharedTemplate);
                if (!trial.Success)
                {
                    yield return Fail($"{label}: {trial.Diagnostic}");
                    continue;
                }
                var candidate = new SpatialCandidateResult(SpatialCandidateFailure.None, trial.Segments);
                var clearance = EndpointNetworkClearance.Check(candidate, 8.5f, out string detail);
                if (clearance != CurveClearanceResult.Clear)
                {
                    yield return Fail($"{label}: core deck {clearance}: {detail}");
                    continue;
                }
                var supports = EndpointNetworkSupports.Check(candidate, design.BridgeRules, ramp, out detail,design.SupportRules);
                if (supports == CurveClearanceResult.Clear)yield return trial;
                else yield return Fail($"{label}: core support {supports}: {detail}");
            }
        }

        private static EndpointStackCoreResult CreateAtGrade(IReadOnlyList<EndpointArm> arms, InterchangeType type,
            bool rightHandTraffic, float mainHeight, float crossHeight, RoadInfo ramp, float maxGrade,
            CloverleafRoadDesign design, bool leftExit, float sideGap = 3, float medianExtra = 0)
            =>CreateAtReference(arms,type,rightHandTraffic,mainHeight,crossHeight,ramp,maxGrade,design,leftExit,sideGap,medianExtra,false);

        private static EndpointStackCoreResult CreateAtReference(IReadOnlyList<EndpointArm> arms, InterchangeType type,
            bool rightHandTraffic, float mainHeight, float crossHeight, RoadInfo ramp, float maxGrade,
            CloverleafRoadDesign design, bool leftExit,float sideGap,float medianExtra,bool secantReference,bool followCurve=false,float bendStrength=1,StackTemplate? sharedTemplate=null)
        {
            if (type != InterchangeType.Stack && type != InterchangeType.Turbine)
                return Fail("Core requires the stack or turbine interchange type.");
            if (arms == null || EndpointConstraints.Validate(arms, type).Count != 0
                || arms.Any(a => a.Endpoints.Count != 2))
                return Fail("Stack and turbine cores require four valid divided-road arms.");
            if (!Finite(mainHeight) || !Finite(crossHeight) || !Finite(maxGrade) || maxGrade <= 0 || maxGrade > .30f
                || ramp == null || !ramp.IsOneWay || ramp.LanesPerDirection < 1 || !Finite(ramp.Width) || ramp.Width <= 0
                || string.IsNullOrWhiteSpace(ramp.Name) || design == null)
                return Fail("Invalid core height, grade, ramp road or design.");

            var selected = arms.OrderBy(a => a.Index).ToArray();
            var incoming = selected.Select(a => a.Endpoints.First(p => !p.TravelOutward)).ToArray();
            for (int family = 0; family < 2; family++)
            {
                var ports = selected.Where(a => a.Index % 2 == family).SelectMany(a => a.Endpoints).ToArray();
                var first = ports[0];
                if (ports.Any(p => p.Prefab != first.Prefab || p.Width != first.Width || p.Lanes != first.Lanes)
                    || first.Width > 1000 || ramp.Width > first.Width || ramp.LanesPerDirection > first.Lanes)
                    return Fail($"Road family {family + 1} requires matching prefabs, widths and lanes in both directions.");
            }
            if (design.Balances.Any(b => b == null || b.Arm < 0 || b.Arm >= 4 || !Finite(b.Width) || b.Width <= 0
                    || string.IsNullOrWhiteSpace(b.Prefab) || b.Width > incoming[b.Arm].Width
                    || b.RampLanes != ramp.LanesPerDirection || b.Lanes < 1 || b.Lanes + b.RampLanes != incoming[b.Arm].Lanes
                    || (b.Prefab == incoming[b.Arm].Prefab && b.Width != incoming[b.Arm].Width))
                || design.Balances.Select(b => b.Arm).Distinct().Count() != design.Balances.Count)
                return Fail("Invalid reduced-road lane balance.");
            var balance = Enumerable.Range(0, 4).Select(i => design.Balances.FirstOrDefault(b => b.Arm == i)).ToArray();
            for (int family = 0; family < 2; family++)
            {
                var a = balance[family]; var b = balance[family + 2];
                if ((a == null) != (b == null) || (a != null && b != null
                    && (a.Prefab != b.Prefab || a.Width != b.Width || a.Lanes != b.Lanes || a.RampLanes != b.RampLanes)))
                    return Fail($"Road family {family + 1} requires the same lane balance in both directions.");
            }
            if (design.BridgeRules.Any(r => r == null || r.Arm < 0 || r.Arm >= 4 || !Finite(r.MaxLength)
                    || r.MaxLength < 10 || r.MaxLength > 1000 || !Finite(r.Width) || r.Width <= 0 || r.Width > 1000
                    || !Finite(r.PillarWidth) || r.PillarWidth <= 0 || r.PillarWidth > 1000
                    || !ValidRange(r.GroundHeight) || !ValidRange(r.ElevatedHeight)
                    || (r.MiddleSpacing.HasValue && (!Finite(r.MiddleSpacing.Value) || r.MiddleSpacing.Value < 0)))
                || design.BridgeRules.Select(r => r.Arm).Distinct().Count() != design.BridgeRules.Count)
                return Fail("Invalid bridge support metadata.");
            for (int family = 0; family < 2; family++)
            {
                var a = design.BridgeRules.FirstOrDefault(r => r.Arm == family);
                var b = design.BridgeRules.FirstOrDefault(r => r.Arm == family + 2);
                if (a != null && b != null && (a.MaxLength != b.MaxLength || a.MiddleSpacing != b.MiddleSpacing
                    || a.PillarWidth != b.PillarWidth || !a.GroundHeight.Equals(b.GroundHeight) || !a.ElevatedHeight.Equals(b.ElevatedHeight)))
                    return Fail($"Road family {family + 1} has inconsistent bridge metadata.");
            }

            var frame = EndpointFrame.Estimate(selected, type);
            if (!secantReference&&(frame.Failure != EndpointFrameFailure.None || !frame.Center.HasValue))
                return Fail("The selected road axes do not define a finite core reference.");
            var axes = new Vec3[2]; var median = new float[2];
            Vec3 Mid(EndpointArm arm)=>Flat(arm.Endpoints[0].Position*.5f+arm.Endpoints[1].Position*.5f);
            for (int family = 0; family < 2; family++)
            {
                var axis = secantReference?Mid(selected[family])-Mid(selected[family+2])
                    :Direction(selected[family]) - Direction(selected[family + 2]);
                if (!Finite(axis.X) || !Finite(axis.Z) || axis.Length() < .01f)
                    return Fail("Opposite road arms do not define an axis.");
                axes[family] = Vec3.Normalize(axis);
                for (int end = family; end < 4; end += 2)
                {
                    var input = incoming[end]; var output = selected[end].Endpoints.First(p => p.TravelOutward);
                    var side = Directions.RightOf(Direction(selected[end])) * (rightHandTraffic ? 1 : -1);
                    float gap = Directions.Dot(Flat(output.Position - input.Position), side) - input.Width;
                    if (!Finite(gap) || gap < -.01f || gap > 1000)
                        return Fail($"Arm {end + 1} has overlapping or incorrectly ordered carriageways.");
                    median[family] += Math.Max(0, gap) * .5f;
                }
            }

            var coreCenter=frame.Center??Vec3.Zero;
            if(secantReference)
            {
                double Cross(Vec3 a,Vec3 b)=>(double)a.X*b.Z-(double)a.Z*b.X;
                double determinant=Cross(axes[0],axes[1]);
                if(Math.Abs(determinant)<1e-6)return Fail("The secant road axes are parallel.");
                double along=Cross(Mid(selected[1])-Mid(selected[0]),axes[1])/determinant;
                if(double.IsNaN(along)||double.IsInfinity(along)||Math.Abs(along)>float.MaxValue)
                    return Fail("The secant road reference is not finite.");
                coreCenter=Mid(selected[0])+axes[0]*(float)along;
                if(!Finite(coreCenter.X)||!Finite(coreCenter.Z))return Fail("The secant road reference is not finite.");
            }

            bool swap = mainHeight > crossHeight;
            int[] originalArm = swap ? new[] { 1, 0, 3, 2 } : new[] { 0, 1, 2, 3 };
            var forward = axes[originalArm[0] % 2];
            var right = Directions.RightOf(forward);
            float reflect = rightHandTraffic ? 1 : -1;
            var other = axes[originalArm[1] % 2];
            float angle = (float)(Math.Atan2(Directions.Dot(other, right) * reflect,
                Directions.Dot(other, forward)) * 180 / Math.PI);
            if (angle < 0) angle += 360;
            float lowerHeight = Math.Min(mainHeight, crossHeight), levelGap = Math.Abs(mainHeight - crossHeight);
            var p = new InterchangeParams
            {
                Legs = InterchangeParams.CrossLegs(angle, 0, 1),
                RampPrefab = ramp.Name, RampWidth = ramp.Width, RampSideGap = sideGap, MaxGrade = maxGrade,
                RightHandTraffic = true, StackLeftExit = leftExit,
            };
            p.Levels[0].Elevation = 10;
            p.Levels[1].Elevation = 10 + levelGap;
            foreach (int internalFamily in new[] { 0, 1 })
            {
                int original = originalArm[internalFamily]; var road = incoming[original];
                p.SetRoad(internalFamily, internalFamily + 2, RoadForm.Dual, road.Width, median[original % 2] + medianExtra, road.Prefab);
                var rule = design.BridgeRules.FirstOrDefault(r => r.Arm == original)
                    ?? design.BridgeRules.FirstOrDefault(r => r.Arm == (original + 2) % 4);
                foreach (int legIndex in new[] { internalFamily, internalFamily + 2 })
                {
                    var leg = p.Legs[legIndex];
                    leg.StubLength = 40;
                    if (rule != null)
                    {
                        leg.MiddlePillarSpacing = rule.MiddleSpacing;
                        leg.PillarWidth = rule.PillarWidth;
                        leg.MaxElevatedEdgeLength = rule.MaxLength;
                        if (rule.GroundHeight.HasValue) leg.GroundHeight = rule.GroundHeight.Value;
                        if (rule.ElevatedHeight.HasValue) leg.ElevatedHeight = rule.ElevatedHeight.Value;
                    }
                }
                if (internalFamily == 0)
                { p.MainReducedPrefab = balance[original]?.Prefab; p.MainReducedWidth = balance[original]?.Width ?? 0; }
                else
                { p.CrossReducedPrefab = balance[original]?.Prefab; p.CrossReducedWidth = balance[original]?.Width ?? 0; }
            }
            Vec3 World(Vec3 value) => coreCenter + right * (value.X * reflect) + forward * value.Z
                + new Vec3(0, value.Y + (lowerHeight - 10), 0);
            var bend=followCurve?EndpointCoreBend.Create(selected,originalArm,coreCenter,right,forward,reflect,angle,bendStrength):null;
            if(followCurve&&(bend==null||bend.IsIdentity))return Fail("Cut roads do not define a nontrivial, bounded curved core.");
            var template = sharedTemplate??new StackTemplate(turbine: type == InterchangeType.Turbine);
            IReadOnlyList<RoadSegmentSpec> built = Array.Empty<RoadSegmentSpec>();
            bool certified = false;
            string gradeDiagnostic = "";
            for (int attempt = 0; attempt < 4; attempt++)
            {
                var errors = template.ValidateParams(p);
                if (errors.Count != 0) return Fail(string.Join("; ", errors));
                // The endpoint adapter already creates the complete external connection.
                // Retain the original planned mainline cuts (including the 20m interval
                // beyond each outermost ramp) and every ramp, but not a second set of
                // legacy outer S transitions and stubs consuming the same road space.
                built = template.Generate(p).Where(s=>s.Role==RoadRole.Ramp
                    ||(!s.Name.Contains("-endA-")&&!s.Name.Contains("-endB-")
                        &&!s.Name.Contains("-in-")&&!s.Name.Contains("-out-")))
                    .Select(s => (bend==null?s:bend.Apply(s)).Transform(World)).ToArray();
                if (built.Count == 0) return Fail("The core solver returned no road geometry.");
                certified = true;
                foreach (var segment in built)
                {
                    var grade = VerticalProfile.CheckGrade(segment.Curve, maxGrade);
                    if (grade == VerticalProfileFailure.None) continue;
                    gradeDiagnostic = $"Core path {segment.Name} fails strict maximum-grade certification: {grade}, sampled={segment.Curve.MaxGrade(128):P3}, limit={maxGrade:P3}.";
                    if (grade != VerticalProfileFailure.GradeExceeded) return Fail(gradeDiagnostic);
                    certified = false;
                }
                if (certified) break;
                // The legacy arc profile has a 0.5 percentage-point tolerance. Its
                // cubic output can therefore exceed the requested grade, particularly
                // for left exits. Re-solve at a tighter input grade; samples guide the
                // next attempt, while the certificate remains the acceptance test.
                float worst = built.Max(s => s.Curve.MaxGrade(128));
                if (!Finite(worst) || worst <= 0) return Fail(gradeDiagnostic);
                p.MaxGrade = Math.Min(p.MaxGrade * .95f, p.MaxGrade * maxGrade / worst * .97f);
            }
            if (!certified) return Fail(gradeDiagnostic);

            int Source(string path)
            {
                if (path.StartsWith("Left", StringComparison.Ordinal)) path = path.Substring(4);
                if (path.StartsWith("Right", StringComparison.Ordinal)) path = path.Substring(5);
                return path switch
                { "MainA" => originalArm[2], "MainB" => originalArm[0], "CrossA" => originalArm[3], "CrossB" => originalArm[1], _ => -1 };
            }
            var outputSegments = new List<SpatialSegment>();
            foreach (var path in built.GroupBy(s => s.Name.Split('-')[0]))
            {
                int source = Source(path.Key);
                if (source < 0) return Fail("The core solver returned an unknown path identity.");
                var pending = path.ToList();
                var heads = pending.Where(s => !pending.Any(t => t.EndNode == s.StartNode)).ToArray();
                if (heads.Length != 1) return Fail($"Core path {path.Key} has no unique directed start.");
                var next = heads[0];
                while (pending.Count > 0)
                {
                    pending.Remove(next);
                    var world = next.Curve;
                    var kind = next.Role != RoadRole.Ramp ? CloverEdgeKind.Main
                        : path.Key.StartsWith("Left", StringComparison.Ordinal) ? CloverEdgeKind.LeftRamp : CloverEdgeKind.DirectRamp;
                    float width;
                    if (kind != CloverEdgeKind.Main) width = ramp.Width;
                    else if (balance[source] != null && next.Prefab == balance[source]!.Prefab) width = balance[source]!.Width;
                    else if (next.Prefab == incoming[source].Prefab) width = incoming[source].Width;
                    else return Fail($"Core path {path.Key} has unknown road metadata.");
                    if (string.IsNullOrWhiteSpace(next.Prefab)) return Fail("Core road prefab is missing.");
                    outputSegments.Add(new SpatialSegment(source, kind, world, next.StartNode, next.EndNode,
                        next.Prefab, width, pathId: path.Key));
                    if (pending.Count == 0) break;
                    var following = pending.Where(s => s.StartNode == next.EndNode).ToArray();
                    if (following.Length != 1) return Fail($"Core path {path.Key} is not one connected directed chain.");
                    next = following[0];
                }
            }

            var degrees = new Dictionary<Vec3, int>();
            foreach (var segment in outputSegments)
                foreach (var node in new[] { segment.StartNode, segment.EndNode })
                    degrees[node] = degrees.TryGetValue(node, out int count) ? count + 1 : 1;
            var portsOut = new List<EndpointCorePort>();
            foreach (var segment in outputSegments.Where(s => s.Kind == CloverEdgeKind.Main))
            {
                if (degrees[segment.StartNode] == 1)
                    portsOut.Add(new EndpointCorePort(segment.SourceArm, false, segment.WorldCurve.P0,
                        Vec3.Normalize(segment.WorldCurve.Derivative(0)), segment.Prefab!, segment.RoadWidth!.Value));
                if (degrees[segment.EndNode] == 1)
                    portsOut.Add(new EndpointCorePort((segment.SourceArm + 2) % 4, true, segment.WorldCurve.P3,
                        Vec3.Normalize(segment.WorldCurve.Derivative(1)), segment.Prefab!, segment.RoadWidth!.Value));
            }
            if (portsOut.Count != 8 || portsOut.Select(port => (port.Arm, port.TravelOutward)).Distinct().Count() != 8)
                return Fail("The core solver did not produce eight unique carriageway ports.");
            return new EndpointStackCoreResult(true, "", outputSegments.AsReadOnly(),
                portsOut.OrderBy(port => port.Arm).ThenBy(port => port.TravelOutward).ToList().AsReadOnly(),coreCenter,followCurve);
        }
    }
}
