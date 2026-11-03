using System;
using System.Collections.Generic;
using System.Linq;
using InterchangeMod.Generation.Geometry;
using InterchangeMod.Generation.Roads;
using InterchangeMod.Generation.Templates;

namespace InterchangeMod.Generation.Endpoints
{
    /// <summary>Rigidly poses freshly solved three-way templates in the selected road frame.</summary>
    public static class EndpointThreeWayCore
    {
        private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
        private static Vec3 Flat(Vec3 v) => new Vec3(v.X, 0, v.Z);
        private static Vec3 Direction(EndpointArm arm) => Vec3.Normalize(arm.Endpoints.Select(p =>
            Vec3.Normalize(Flat(p.OutwardTangent))).Aggregate(Vec3.Zero, (a, b) => a + b));
        private static EndpointStackCoreResult Fail(string detail) => new EndpointStackCoreResult(false, detail,
            Array.Empty<SpatialSegment>(), Array.Empty<EndpointCorePort>());

        public static EndpointStackCoreResult Create(IReadOnlyList<EndpointArm> arms, InterchangeType type,
            bool rightHandTraffic, float mainHeight, float branchHeight, RoadInfo ramp, float maxGrade,
            CloverleafRoadDesign design, bool trumpetLoopForExit = true, bool directionalTwoLevel = false, float? terrainHeight = null)
        {
            var failures = new List<string>();
            foreach (var candidate in Candidates(arms, type, rightHandTraffic, mainHeight, branchHeight,
                ramp, maxGrade, design, trumpetLoopForExit, directionalTwoLevel, terrainHeight))
            {
                if (candidate.Success) return candidate;
                failures.Add(candidate.Diagnostic);
            }
            return Fail(string.Join("; ", failures));
        }

        /// <summary>Finite alternatives re-solve geometry; no generated layout is scaled or warped.</summary>
        public static IEnumerable<EndpointStackCoreResult> Candidates(IReadOnlyList<EndpointArm> arms, InterchangeType type,
            bool rightHandTraffic, float mainHeight, float branchHeight, RoadInfo ramp, float maxGrade,
            CloverleafRoadDesign design, bool trumpetLoopForExit = true, bool directionalTwoLevel = false, float? terrainHeight = null)
        {
            string invalid = Validate(arms, type, mainHeight, branchHeight, ramp, maxGrade, design);
            if (invalid.Length > 0) { yield return Fail(invalid); yield break; }
            if (terrainHeight.HasValue && (!Finite(terrainHeight.Value)
                || !Finite(mainHeight - terrainHeight.Value) || !Finite(branchHeight - terrainHeight.Value)))
            { yield return Fail("A finite terrain height is required for the core elevation datum."); yield break; }
            if (type != InterchangeType.Trumpet && directionalTwoLevel && !terrainHeight.HasValue)
            { yield return Fail("Two-level directional cores require a known finite terrain height to certify the underpass as a tunnel."); yield break; }
            var selected = arms.OrderBy(a => a.Index).ToArray();
            Vec3 Mid(EndpointArm arm) => Flat(arm.Endpoints[0].Position * .5f + arm.Endpoints[1].Position * .5f);
            var forward = Vec3.Normalize(Mid(selected[0]) - Mid(selected[2]));
            if (!Finite(forward.X) || !Finite(forward.Z) || forward.Length() < .01f
                || Directions.Dot(forward, Direction(selected[0])) <= 0 || Directions.Dot(-forward, Direction(selected[2])) <= 0)
            { yield return Fail("Main road cutpoints do not define a forward secant core axis."); yield break; }
            var right = Directions.RightOf(forward);
            float reflect = rightHandTraffic ? 1 : -1;
            var branch = Direction(selected[1]);
            double Cross(Vec3 a, Vec3 b) => (double)a.X * b.Z - (double)a.Z * b.X;
            double determinant = Cross(forward, branch);
            if (Math.Abs(determinant) < 1e-6)
            { yield return Fail("Three-way main secant and branch axis are parallel."); yield break; }
            double along = Cross(Mid(selected[1]) - Mid(selected[0]), branch) / determinant;
            if (double.IsNaN(along) || double.IsInfinity(along) || Math.Abs(along) > float.MaxValue)
            { yield return Fail("Three-way core reference is not finite."); yield break; }
            var center = Mid(selected[0]) + forward * (float)along;
            if (!Finite(center.X) || !Finite(center.Z) || Directions.Dot(Mid(selected[1]) - center, branch) <= 0)
            { yield return Fail("The branch cutpoints face backward from the core reference."); yield break; }
            float angle = (float)(Math.Atan2(Directions.Dot(branch, right) * reflect, Directions.Dot(branch, forward)) * 180 / Math.PI);
            if (angle < 0) angle += 360;
            var medians = new float[3];
            for (int arm = 0; arm < 3; arm++)
            {
                var input = selected[arm].Endpoints.Single(p => !p.TravelOutward);
                var output = selected[arm].Endpoints.Single(p => p.TravelOutward);
                var side = Directions.RightOf(Direction(selected[arm])) * reflect;
                medians[arm] = Directions.Dot(Flat(output.Position - input.Position), side) - input.Width;
                if (!Finite(medians[arm]) || medians[arm] < -.01f || medians[arm] > 1000)
                { yield return Fail($"Arm {arm + 1} has overlapping or incorrectly ordered carriageways."); yield break; }
                medians[arm] = Math.Max(0, medians[arm]);
            }
            // Existing templates have a finite analytic radius/cut search of their own.
            // Keep this outer set small and shared with the caller's total candidate budget.
            var alternatives = new[] { (Gap: 3f, Factor: 1f, LoopExtra: 0f), (Gap: 5f, Factor: 1f, LoopExtra: 0f),
                (Gap: 8f, Factor: 1f, LoopExtra: 0f), (Gap: 3f, Factor: .8f, LoopExtra: 0f), (Gap: 5f, Factor: .65f, LoopExtra: 0f) };
            if (type == InterchangeType.Trumpet)
                alternatives = alternatives.Concat(new[] { 10f, 20f, 40f }.Select(extra => (Gap: 3f, Factor: 1f, LoopExtra: extra))).ToArray();
            foreach (var alternative in alternatives)
            {
                var trial = Solve(selected, type, mainHeight, branchHeight, ramp, maxGrade, design,
                    trumpetLoopForExit, directionalTwoLevel, center, forward, right, reflect, angle,
                    medians, alternative.Gap, alternative.Factor, alternative.LoopExtra, terrainHeight);
                yield return trial.Success ? trial : Fail($"side gap {alternative.Gap:F0}m, grade factor {alternative.Factor:F2}, loop extra {alternative.LoopExtra:F0}m: {trial.Diagnostic}");
            }
        }

        private static string Validate(IReadOnlyList<EndpointArm> arms, InterchangeType type, float hm, float hb,
            RoadInfo ramp, float maxGrade, CloverleafRoadDesign design)
        {
            if (type != InterchangeType.Trumpet && type != InterchangeType.DirectionalT && type != InterchangeType.SemiDirectionalT)
                return "Three-way core requires trumpet, directional T or semi-directional T.";
            if (arms == null || EndpointConstraints.Validate(arms, type).Count > 0 || arms.Any(a => a.Endpoints.Count != 2))
                return "Three-way cores require three valid divided-road arms; single or mixed carriageways are unsupported.";
            if (!Finite(hm) || !Finite(hb) || !Finite(maxGrade) || maxGrade <= 0 || maxGrade > .30f
                || ramp == null || !ramp.IsOneWay || ramp.LanesPerDirection < 1 || !Finite(ramp.Width) || ramp.Width <= 0
                || string.IsNullOrWhiteSpace(ramp.Name) || design == null) return "Invalid core height, grade, ramp or road design.";
            var ordered = arms.OrderBy(a => a.Index).ToArray();
            foreach (var family in new[] { new[] { 0, 2 }, new[] { 1 } })
            {
                var ports = family.SelectMany(i => ordered[i].Endpoints).ToArray(); var first = ports[0];
                if (ports.Any(p => p.Prefab != first.Prefab || p.Width != first.Width || p.Lanes != first.Lanes)
                    || first.Width > 1000 || ramp.Width > first.Width || ramp.LanesPerDirection > first.Lanes)
                    return "Road family requires matching actual prefabs, widths and lanes in both directions.";
            }
            if (design.Balances.Any(b => b == null || b.Arm < 0 || b.Arm > 2 || !Finite(b.Width) || b.Width <= 0
                || string.IsNullOrWhiteSpace(b.Prefab) || b.Width > ordered[b.Arm].Endpoints[0].Width
                || b.RampLanes != ramp.LanesPerDirection || b.Lanes < 1 || b.Lanes + b.RampLanes != ordered[b.Arm].Endpoints[0].Lanes
                || (b.Prefab == ordered[b.Arm].Endpoints[0].Prefab && b.Width != ordered[b.Arm].Endpoints[0].Width))
                || design.Balances.Select(b => b.Arm).Distinct().Count() != design.Balances.Count)
                return "Invalid reduced-road lane balance.";
            var a = design.Balances.FirstOrDefault(b => b.Arm == 0); var c = design.Balances.FirstOrDefault(b => b.Arm == 2);
            if ((a == null) != (c == null) || (a != null && c != null && (a.Prefab != c.Prefab || a.Width != c.Width
                || a.Lanes != c.Lanes || a.RampLanes != c.RampLanes))) return "Main arms require the same lane balance.";
            bool Range(HeightRange? range) => !range.HasValue || (Finite(range.Value.Min) && Finite(range.Value.Max) && range.Value.Min <= range.Value.Max);
            if (design.BridgeRules.Any(r => r == null || r.Arm < 0 || r.Arm > 2 || !Finite(r.MaxLength) || r.MaxLength < 10 || r.MaxLength > 1000
                || !Finite(r.Width) || r.Width <= 0 || r.Width > 1000 || !Finite(r.PillarWidth) || r.PillarWidth <= 0 || r.PillarWidth > 1000
                || !Range(r.GroundHeight) || !Range(r.ElevatedHeight) || (r.MiddleSpacing.HasValue && (!Finite(r.MiddleSpacing.Value) || r.MiddleSpacing < 0)))
                || design.BridgeRules.Select(r => r.Arm).Distinct().Count() != design.BridgeRules.Count) return "Invalid bridge support metadata.";
            var ra = design.BridgeRules.FirstOrDefault(r => r.Arm == 0); var rc = design.BridgeRules.FirstOrDefault(r => r.Arm == 2);
            if (ra != null && rc != null && (ra.MaxLength != rc.MaxLength || ra.MiddleSpacing != rc.MiddleSpacing || ra.PillarWidth != rc.PillarWidth
                || !ra.GroundHeight.Equals(rc.GroundHeight) || !ra.ElevatedHeight.Equals(rc.ElevatedHeight))) return "Main arms have inconsistent bridge metadata.";
            return "";
        }

        private static EndpointStackCoreResult Solve(EndpointArm[] arms, InterchangeType type, float hm, float hb,
            RoadInfo ramp, float limit, CloverleafRoadDesign design, bool loopExit, bool twoLevel,
            Vec3 center, Vec3 forward, Vec3 right, float reflect, float angle, float[] median, float sideGap, float factor, float loopExtra,
            float? terrainHeight)
        {
            bool trumpet = type == InterchangeType.Trumpet;
            float baseline = trumpet ? Math.Min(hm, hb) : hm;
            // Template Y is height above terrain, not height above the selected road.
            // Its tunnel and bridge bands must be planned before the world transform.
            // Unknown terrain can only use a conservative elevated reference; the
            // two-level case was rejected above because it needs a real tunnel datum.
            float datum = terrainHeight ?? (baseline - 10);
            float localBase = baseline - datum;
            var p = new InterchangeParams {
                Legs = InterchangeParams.TLegs(angle, trumpet && hm > hb ? 1 : 0, trumpet && hb > hm ? 1 : 0),
                RightHandTraffic = true, RampPrefab = ramp.Name, RampWidth = ramp.Width,
                RampSideGap = sideGap, MaxGrade = limit * factor, LoopRadius = 0,
                TrumpetLoopForExit = loopExit, DirectionalTwoLevel = twoLevel,
            };
            p.Levels[0].Elevation = localBase;
            p.Levels[1].Elevation = localBase + (trumpet ? Math.Abs(hm - hb) : Math.Max(10, Math.Abs(hb - hm)));
            p.SetRoad(0, 2, RoadForm.Dual, arms[0].Endpoints[0].Width, (median[0] + median[2]) / 2, arms[0].Endpoints[0].Prefab);
            p.SetRoad(1, 1, RoadForm.Dual, arms[1].Endpoints[0].Width, median[1], arms[1].Endpoints[0].Prefab);
            foreach (int arm in new[] { 0, 1, 2 })
            {
                var leg = p.Legs[arm]; leg.StubLength = 40;
                var rule = design.BridgeRules.FirstOrDefault(r => r.Arm == arm)
                    ?? (arm == 0 || arm == 2 ? design.BridgeRules.FirstOrDefault(r => r.Arm == 2 - arm) : null);
                if (rule == null) continue;
                leg.MiddlePillarSpacing = rule.MiddleSpacing; leg.PillarWidth = rule.PillarWidth; leg.MaxElevatedEdgeLength = rule.MaxLength;
                if (rule.GroundHeight.HasValue) leg.GroundHeight = rule.GroundHeight.Value;
                if (rule.ElevatedHeight.HasValue) leg.ElevatedHeight = rule.ElevatedHeight.Value;
            }
            var mainBalance = design.Balances.FirstOrDefault(b => b.Arm == 0);
            var branchBalance = design.Balances.FirstOrDefault(b => b.Arm == 1);
            p.MainReducedPrefab = mainBalance?.Prefab; p.MainReducedWidth = mainBalance?.Width ?? 0;
            p.CrossReducedPrefab = branchBalance?.Prefab; p.CrossReducedWidth = branchBalance?.Width ?? 0;
            Vec3 World(Vec3 v) => center + right * (v.X * reflect) + forward * v.Z + new Vec3(0, v.Y + datum, 0);
            IInterchangeTemplate template = trumpet ? (IInterchangeTemplate)new TrumpetTemplate() : new DirectionalTTemplate(type == InterchangeType.DirectionalT);
            IReadOnlyList<RoadSegmentSpec> built = Array.Empty<RoadSegmentSpec>();
            bool certified = false;
            for (int attempt = 0; attempt < 4; attempt++)
            {
                if (trumpet && loopExtra > 0) { p.LoopRadius = 0; p.LoopRadius = Math.Min(250, TrumpetTemplate.ResolveLoopRadius(p) + loopExtra); }
                var errors = template.ValidateParams(p);
                if (errors.Count > 0) return Fail(string.Join("; ", errors));
                built = AlignExternalTangents(template.Generate(p).Select(s => s.Transform(World)).ToArray(), forward, Direction(arms[1]));
                if (built.Count == 0) return Fail("The three-way template produced no geometry.");
                certified = built.All(s => VerticalProfile.CheckGrade(s.Curve, limit) == VerticalProfileFailure.None);
                if (certified) break;
                float worst = built.Max(s => s.Curve.MaxGrade(128));
                if (!Finite(worst) || worst <= 0) return Fail("Invalid core grade geometry.");
                p.MaxGrade = Math.Min(p.MaxGrade * .95f, p.MaxGrade * limit / worst * .97f);
            }
            if (!certified) return Fail("The core exceeds strict maximum-grade certification after bounded re-solving.");
            string Path(string name)
            {
                foreach (var prefix in new[] { "MainNear", "MainFar", "BranchOut", "BranchIn", "NearToBranch", "BranchToNear", "Loop", "SemiDirect", "FarToBranch", "BranchToFar" })
                    if (name.StartsWith(prefix, StringComparison.Ordinal)) return prefix;
                return "";
            }
            var paths = new List<(string Id, RoadSegmentSpec[] Pieces)>();
            foreach (var group in built.GroupBy(s => Path(s.Name)))
            {
                if (group.Key.Length == 0) return Fail("Unknown three-way path identity.");
                var pending = group.ToList(); var ordered = new List<RoadSegmentSpec>();
                var heads = pending.Where(s => !pending.Any(t => t.EndNode == s.StartNode)).ToArray();
                if (heads.Length != 1) return Fail($"Core path {group.Key} has no unique directed start.");
                var next = heads[0];
                while (pending.Count > 0)
                {
                    ordered.Add(next); pending.Remove(next);
                    if (pending.Count == 0) break;
                    var following = pending.Where(s => s.StartNode == next.EndNode).ToArray();
                    if (following.Length != 1) return Fail($"Core path {group.Key} is not one directed chain.");
                    next = following[0];
                }
                if (!IsSimplePath(ordered.Select(s => s.Curve).ToArray(), out string simpleDetail)) return Fail($"Core path {group.Key} could not be certified non-self-intersecting: {simpleDetail}");
                paths.Add((group.Key, ordered.ToArray()));
            }
            var degree = new Dictionary<Vec3, int>();
            foreach (var piece in built)
            foreach (var node in new[] { piece.StartNode, piece.EndNode }) degree[node] = degree.TryGetValue(node, out int count) ? count + 1 : 1;
            int ArmAt(Vec3 tangent, bool outward) {
                var axis = Vec3.Normalize(Flat(tangent)) * (outward ? 1 : -1);
                var axes = new[] { forward, Direction(arms[1]), -forward };
                var matches = Enumerable.Range(0, 3).Where(a => Directions.Dot(axis, axes[a]) > .9999f).ToArray();
                return matches.Length == 1 ? matches[0] : -1;
            }
            var ports = new List<EndpointCorePort>();
            foreach (var path in paths.Where(g => g.Pieces[0].Role != RoadRole.Ramp))
            foreach (bool end in new[] { false, true })
            {
                var piece = end ? path.Pieces.Last() : path.Pieces.First(); var node = end ? piece.EndNode : piece.StartNode;
                if (degree[node] != 1) continue;
                var tangent = Vec3.Normalize(piece.Curve.Derivative(end ? 1 : 0)); int arm = ArmAt(tangent, end);
                if (arm < 0 || node != (end ? piece.Curve.P3 : piece.Curve.P0)) return Fail("An external core port does not match an original road axis.");
                var road = arms[arm].Endpoints.Single(e => e.TravelOutward == end);
                if (piece.Prefab != road.Prefab) return Fail("An external core port uses a reduced or unknown prefab.");
                ports.Add(new EndpointCorePort(arm, end, node, tangent, road.Prefab, road.Width));
            }
            if (ports.Count != 6 || ports.Select(port => (port.Arm, port.TravelOutward)).Distinct().Count() != 6)
                return Fail("The core did not produce six distinct external carriageway ports.");
            bool Reach(Vec3 start, Vec3 target) {
                var seen = new HashSet<Vec3> { start }; var queue = new Queue<Vec3>(); queue.Enqueue(start);
                while (queue.Count > 0) { var at = queue.Dequeue(); foreach (var s in built.Where(s => s.StartNode == at)) if (seen.Add(s.EndNode)) queue.Enqueue(s.EndNode); }
                return seen.Contains(target);
            }
            foreach (var input in ports.Where(port => !port.TravelOutward))
            foreach (var output in ports.Where(port => port.TravelOutward))
                if (Reach(input.Position, output.Position) != (input.Arm != output.Arm)) return Fail("The core lost a directed movement or introduced a U-turn.");
            var segments = new List<SpatialSegment>();
            foreach (var path in paths)
            {
                bool road = path.Pieces[0].Role != RoadRole.Ramp;
                int source = path.Id.StartsWith("Branch", StringComparison.Ordinal) && road ? 1
                    : ports.Where(port => !port.TravelOutward && Reach(port.Position, path.Pieces[0].StartNode)).Select(port => port.Arm).DefaultIfEmpty(-1).First();
                if (source < 0) return Fail($"Core path {path.Id} is not reachable from an incoming carriageway.");
                foreach (var piece in path.Pieces)
                {
                    var balance = design.Balances.FirstOrDefault(b => b.Arm == source);
                    float width = !road ? ramp.Width : balance != null && piece.Prefab == balance.Prefab ? balance.Width : arms[source].Endpoints[0].Width;
                    if (piece.Prefab != (road ? arms[source].Endpoints[0].Prefab : ramp.Name) && (!road || balance == null || piece.Prefab != balance.Prefab))
                        return Fail($"Core path {path.Id} has unknown prefab metadata.");
                    var kind = road ? CloverEdgeKind.Main : path.Id == "Loop" ? CloverEdgeKind.LoopRamp :
                        path.Id == "SemiDirect" || path.Id == "FarToBranch" || path.Id == "BranchToFar" ? CloverEdgeKind.LeftRamp : CloverEdgeKind.DirectRamp;
                    segments.Add(new SpatialSegment(source, kind, piece.Curve, piece.StartNode, piece.EndNode, piece.Prefab, width, pathId: path.Id));
                }
            }
            var candidate = new SpatialCandidateResult(SpatialCandidateFailure.None, segments);
            var clearance = EndpointNetworkClearance.Check(candidate, 8.5f, out string detail);
            if (clearance != CurveClearanceResult.Clear) return Fail($"Core deck {clearance}: {detail}");
            var supports = EndpointNetworkSupports.Check(candidate, design.BridgeRules, ramp, out detail, design.SupportRules);
            if (supports != CurveClearanceResult.Clear) return Fail($"Core support {supports}: {detail}");
            return new EndpointStackCoreResult(true, "Core ports retain template elevations; original endpoint heights require external transitions.",
                segments.AsReadOnly(), ports.OrderBy(port => port.Arm).ThenBy(port => port.TravelOutward).ToList().AsReadOnly(), center);
        }

        private static IReadOnlyList<RoadSegmentSpec> AlignExternalTangents(IReadOnlyList<RoadSegmentSpec> built, Vec3 main, Vec3 branch)
        {
            var degree = new Dictionary<Vec3, int>();
            foreach (var piece in built)
            foreach (var node in new[] { piece.StartNode, piece.EndNode }) degree[node] = degree.TryGetValue(node, out int count) ? count + 1 : 1;
            // The legacy directional template represents straights by a 100000m
            // radius arc. Impose the actual external boundary tangents on terminal
            // control handles, retaining every node, endpoint and handle length.
            return built.Select(piece => {
                if (piece.Role == RoadRole.Ramp) return piece;
                var c = piece.Curve; var a = c.P1; var b = c.P2;
                var axis = piece.Role == RoadRole.MainRoad ? main : branch;
                if (degree[piece.StartNode] == 1)
                    a = c.P0 + axis * (Directions.Dot(c.Derivative(0), axis) >= 0 ? 1 : -1) * Flat(c.P1 - c.P0).Length();
                if (degree[piece.EndNode] == 1)
                    b = c.P3 - axis * (Directions.Dot(c.Derivative(1), axis) >= 0 ? 1 : -1) * Flat(c.P3 - c.P2).Length();
                return new RoadSegmentSpec(piece.Name, piece.Role, new Bezier3(c.P0, a, b, c.P3), piece.Prefab, piece.StartNode, piece.EndNode);
            }).ToArray();
        }

        /// <summary>
        /// Bernstein derivative projections prove each cubic injective. Neighboring
        /// pieces occupy successive slabs along their joint tangent, including lateral
        /// edge alignment offsets. Every non-neighbor pair gets a separating-hull
        /// certificate, recursively subdividing uncertain hulls; uncertainty rejects.
        /// </summary>
        internal static bool IsSimplePath(IReadOnlyList<Bezier3> curves, out string detail)
        {
            detail = "";
            if (curves == null || curves.Count == 0) return false;
            // A pair of quarter-circle cubics can span 180 degrees, leaving a
            // zero projection at the far endpoints. Subdivision makes the local
            // slab certificate strict without any tolerance or exemption.
            curves = curves.SelectMany(c => new[] { c.Slice(0, .5f), c.Slice(.5f, 1) }).ToArray();
            double Dot(Vec3 a, Vec3 b) => (double)a.X * b.X + (double)a.Z * b.Z;
            bool Monotone(Bezier3 curve, Vec3 axis) => Dot(curve.P1 - curve.P0, axis) >= 0
                && Dot(curve.P2 - curve.P1, axis) >= 0 && Dot(curve.P3 - curve.P2, axis) >= 0
                && Dot(curve.P3 - curve.P0, axis) > 1e-8;
            foreach (var curve in curves)
                if (!Monotone(curve, Flat(curve.P3 - curve.P0))) { detail = $"non-monotone cubic {curve}"; return false; }
            int budget = 100000;
            bool Disjoint(Bezier3 a, Bezier3 b, int depth)
            {
                if (--budget < 0) return false;
                var ap = new[] { a.P0, a.P1, a.P2, a.P3 }; var bp = new[] { b.P0, b.P1, b.P2, b.P3 };
                var axes = new List<Vec3> { new Vec3(1, 0, 0), new Vec3(0, 0, 1) };
                foreach (var points in new[] { ap, bp })
                    for (int i = 0; i < 4; i++) for (int j = i + 1; j < 4; j++)
                    { var edge = points[j] - points[i]; axes.Add(new Vec3(-edge.Z, 0, edge.X)); }
                foreach (var axis in axes)
                {
                    if (axis.X == 0 && axis.Z == 0) continue;
                    double amin = ap.Min(v => Dot(v - a.P0, axis)), amax = ap.Max(v => Dot(v - a.P0, axis));
                    double bmin = bp.Min(v => Dot(v - a.P0, axis)), bmax = bp.Max(v => Dot(v - a.P0, axis));
                    if (amax < bmin || bmax < amin) return true;
                }
                if (depth == 14) return false;
                if ((a.P3 - a.P0).Length() >= (b.P3 - b.P0).Length())
                    return Disjoint(a.Slice(0, .5f), b, depth + 1) && Disjoint(a.Slice(.5f, 1), b, depth + 1);
                return Disjoint(a, b.Slice(0, .5f), depth + 1) && Disjoint(a, b.Slice(.5f, 1), depth + 1);
            }
            for (int i = 0; i < curves.Count; i++)
            for (int j = i + 1; j < curves.Count; j++)
            {
                var a = curves[i]; var b = curves[j];
                if (j == i + 1)
                {
                    var axis = Flat(Vec3.Normalize(Flat(a.Derivative(1))) + Vec3.Normalize(Flat(b.Derivative(0))));
                    var offset = Flat(b.P0 - a.P3);
                    // A small lateral bias can separate distinct edge-aligned curve
                    // endpoints without changing the exact logical connection node.
                    if (Dot(offset, axis) < 0 && Dot(offset, offset) > 0)
                        axis += offset * (float)((-Dot(offset, axis) + 1e-5) / Dot(offset, offset));
                    if (Monotone(a, axis) && Monotone(b, axis) && Dot(offset, axis) >= 0) continue;
                    detail = $"joint {i}/{j}, offset={offset}, projection={Dot(offset, axis):R}, a={a}, b={b}";
                    return false;
                }
                if (!Disjoint(a, b, 0)) { detail = $"unseparated pieces {i}/{j}"; return false; }
            }
            return true;
        }
    }
}
