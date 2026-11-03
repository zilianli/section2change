using System;
using System.Collections.Generic;
using System.Linq;
using InterchangeMod.Generation.Geometry;
using InterchangeMod.Generation.Roads;
using InterchangeMod.Generation.Templates;

namespace InterchangeMod.Generation.Endpoints
{
    /// <summary>Re-solves the analytic Y core for three equal-status divided-road arms.</summary>
    public static class EndpointWyeCore
    {
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static Vec3 Flat(Vec3 value) => new Vec3(value.X, 0, value.Z);
        private static Vec3 Direction(EndpointArm arm) => Vec3.Normalize(arm.Endpoints.Select(p =>
            Vec3.Normalize(Flat(p.OutwardTangent))).Aggregate(Vec3.Zero, (a, b) => a + b));
        private static EndpointStackCoreResult Fail(string detail) => new EndpointStackCoreResult(false, detail,
            Array.Empty<SpatialSegment>(), Array.Empty<EndpointCorePort>());

        /// <summary>
        /// Conservative clearance for every participating road/elevation envelope.
        /// Unknown envelopes use the measured fallback; malformed envelopes return NaN.
        /// The caller must reject NaN rather than silently substitute a default.
        /// </summary>
        public static float RequiredClearance(CloverleafRoadDesign design, RoadInfo ramp)
        {
            if (design == null || ramp == null) return float.NaN;
            var fallback = new HeightRange(0, HeightRange.MeasuredMinimumGap);
            var ranges = new List<HeightRange> { ramp.GroundHeight ?? fallback, ramp.ElevatedHeight ?? fallback };
            foreach (var rule in design.BridgeRules)
            {
                if (rule == null) return float.NaN;
                ranges.Add(rule.GroundHeight ?? fallback); ranges.Add(rule.ElevatedHeight ?? fallback);
            }
            if (design.BridgeRules.Count < 3) ranges.Add(fallback);
            if (ranges.Any(r => !Finite(r.Min) || !Finite(r.Max) || r.Min > r.Max)) return float.NaN;
            float required = HeightRange.BridgeThreshold;
            foreach (var lower in ranges)
            foreach (var upper in ranges) required = Math.Max(required, HeightRange.RequiredGap(lower, upper));
            return Finite(required) ? required : float.NaN;
        }

        public static EndpointStackCoreResult Create(IReadOnlyList<EndpointArm> arms, bool rightHandTraffic,
            float baseHeight, float upperHeight, RoadInfo ramp, float maxGrade, CloverleafRoadDesign design, float terrainHeight)
        {
            var failures = new List<string>();
            foreach (var candidate in Candidates(arms, rightHandTraffic, baseHeight, upperHeight, ramp, maxGrade, design, terrainHeight))
            {
                if (candidate.Success) return candidate;
                failures.Add(candidate.Diagnostic);
            }
            return Fail(string.Join("; ", failures));
        }

        /// <summary>Small finite alternatives change solve parameters, never scale generated roads.</summary>
        public static IEnumerable<EndpointStackCoreResult> Candidates(IReadOnlyList<EndpointArm> arms, bool rightHandTraffic,
            float baseHeight, float upperHeight, RoadInfo ramp, float maxGrade, CloverleafRoadDesign design, float terrainHeight)
        {
            string invalid = Validate(arms, baseHeight, upperHeight, ramp, maxGrade, design, terrainHeight);
            if (invalid.Length != 0) { yield return Fail(invalid); yield break; }
            var selected = arms.OrderBy(a => a.Index).ToArray();
            var axes = selected.Select(Direction).ToArray();
            if (axes.Any(d => !Finite(d.X) || !Finite(d.Z) || d.Length() < .99f))
            { yield return Fail("Y road arm has no stable outward axis."); yield break; }
            Vec3 Mid(EndpointArm arm) => Flat(arm.Endpoints[0].Position * .5f + arm.Endpoints[1].Position * .5f);
            var mids = selected.Select(Mid).ToArray();
            // Minimise transverse error to all three centerlines equally. There is
            // no opposite pair or main road, even when input groups are permuted.
            var origin = mids.Aggregate(Vec3.Zero, (a, b) => a + b) * (1f / 3);
            double xx = 0, xz = 0, zz = 0, bx = 0, bz = 0;
            for (int i = 0; i < 3; i++)
            {
                var n = Directions.RightOf(axes[i]); var delta = mids[i] - origin;
                double at = (double)n.X * delta.X + (double)n.Z * delta.Z;
                xx += (double)n.X * n.X; xz += (double)n.X * n.Z; zz += (double)n.Z * n.Z;
                bx += n.X * at; bz += n.Z * at;
            }
            double determinant = xx * zz - xz * xz;
            if (determinant < .01)
            { yield return Fail("Y road axes do not define a finite three-way reference."); yield break; }
            var center = origin + new Vec3((float)((bx * zz - bz * xz) / determinant), 0, (float)((bz * xx - bx * xz) / determinant));
            if (!Finite(center.X) || !Finite(center.Z) || Enumerable.Range(0, 3).Any(i => Directions.Dot(mids[i] - center, axes[i]) <= 0))
            { yield return Fail("Y cutpoints must face outward from a finite common reference."); yield break; }
            var forward = axes[0]; var right = Directions.RightOf(forward);
            float reflect = rightHandTraffic ? 1 : -1;
            var angles = axes.Select(d => {
                float a = (float)(Math.Atan2(Directions.Dot(d, right) * reflect, Directions.Dot(d, forward)) * 180 / Math.PI);
                return a < 0 ? a + 360 : a;
            }).ToArray();
            var sorted = angles.OrderBy(a => a).ToArray();
            for (int i = 0; i < 3; i++)
            {
                float gap = (sorted[(i + 1) % 3] - sorted[i] + 360) % 360;
                if (gap < 90 - .001f || gap > 150 + .001f)
                { yield return Fail($"Y adjacent road angle {gap:F2} degrees requires 90 to 150 degrees."); yield break; }
            }
            var medians = new float[3];
            for (int i = 0; i < 3; i++)
            {
                var input = selected[i].Endpoints.Single(e => !e.TravelOutward);
                var output = selected[i].Endpoints.Single(e => e.TravelOutward);
                medians[i] = Directions.Dot(Flat(output.Position - input.Position), Directions.RightOf(axes[i])) * reflect - input.Width;
                if (!Finite(medians[i]) || medians[i] < -.01f || medians[i] > 1000)
                { yield return Fail($"Y arm {selected[i].Index + 1} has overlapping or incorrectly ordered carriageways."); yield break; }
                medians[i] = Math.Max(0, medians[i]);
            }
            foreach (var alternative in new[] { (Gap: 3f, Factor: 1f), (Gap: 5f, Factor: 1f),
                (Gap: 8f, Factor: 1f), (Gap: 3f, Factor: .8f), (Gap: 5f, Factor: .65f) })
            {
                var trial = Solve(selected, axes, angles, medians, center, forward, right, reflect,
                    baseHeight, upperHeight, terrainHeight, ramp, maxGrade, design, alternative.Gap, alternative.Factor);
                yield return trial.Success ? trial : Fail($"Y side gap {alternative.Gap:F0}m, grade factor {alternative.Factor:F2}: {trial.Diagnostic}");
            }
        }

        private static string Validate(IReadOnlyList<EndpointArm> arms, float height, float upper, RoadInfo ramp,
            float grade, CloverleafRoadDesign design, float terrain)
        {
            if (arms == null || EndpointConstraints.Validate(arms, InterchangeType.Wye).Count != 0
                || arms.Any(a => a.Endpoints.Count != 2))
                return "Y cores require three valid divided-road arms, one incoming and one outgoing carriageway per arm.";
            if (!Finite(height) || !Finite(upper) || !Finite(terrain) || !Finite(height - terrain) || !Finite(upper - terrain)
                || !Finite(upper - height))
                return "Y core requires finite terrain and heights.";
            if (!Finite(grade) || grade <= 0 || grade > .30f || ramp == null || ramp.ForwardLanes <= 0 || ramp.BackwardLanes != 0
                || ramp.LanesPerDirection < 1 || !Finite(ramp.Width) || ramp.Width <= 0 || string.IsNullOrWhiteSpace(ramp.Name)
                || design == null) return "Invalid Y maximum grade, ramp or road design.";
            int lanes = arms[0].Endpoints[0].Lanes;
            foreach (var arm in arms)
            {
                var first = arm.Endpoints[0];
                if (arm.Endpoints.Any(e => e.Prefab != first.Prefab || e.Width != first.Width || e.Lanes != first.Lanes)
                    || first.Width > 1000 || ramp.Width > first.Width)
                    return "Each Y arm requires matching actual prefabs, widths and lanes in both directions.";
                if (first.Lanes != lanes || lanes % 2 != 0 || ramp.LanesPerDirection * 2 != lanes)
                    return "Y preview requires equal even carriageway lane counts and ramps with exactly half that lane count.";
            }
            if (design.Balances.Count != 0) return "Y roads split completely into two ramps; reduced main-road balances are unsupported.";
            bool Range(HeightRange? range) => !range.HasValue || (Finite(range.Value.Min) && Finite(range.Value.Max) && range.Value.Min <= range.Value.Max);
            if (design.BridgeRules.Any(r => r == null || r.Arm < 0 || r.Arm > 2 || !Finite(r.MaxLength) || r.MaxLength < 10 || r.MaxLength > 1000
                || !Finite(r.Width) || r.Width != arms.Single(a => a.Index == r.Arm).Endpoints[0].Width
                || !Finite(r.PillarWidth) || r.PillarWidth <= 0 || r.PillarWidth > 1000 || !Range(r.GroundHeight) || !Range(r.ElevatedHeight)
                || (r.MiddleSpacing.HasValue && (!Finite(r.MiddleSpacing.Value) || r.MiddleSpacing.Value < 0)))
                || design.BridgeRules.Select(r => r.Arm).Distinct().Count() != design.BridgeRules.Count)
                return "Invalid Y bridge support metadata.";
            float clearance = RequiredClearance(design, ramp);
            if (!Finite(clearance)) return "Invalid Y road height envelope metadata.";
            if (upper - height < clearance)
                return $"Y core requires at least {clearance:F2}m between its common base and upper level for the actual road height envelopes.";
            return "";
        }

        private static EndpointStackCoreResult Solve(EndpointArm[] arms, Vec3[] axes, float[] angles, float[] medians,
            Vec3 center, Vec3 forward, Vec3 right, float reflect, float height, float upper, float terrain,
            RoadInfo ramp, float limit, CloverleafRoadDesign design, float gap, float factor)
        {
            var p = new InterchangeParams {
                Legs = Enumerable.Range(0, 3).Select(i => new LegSpec(angles[i], 0, arms[i].Endpoints[0].Prefab, 40) {
                    Form = RoadForm.Dual, RoadWidth = arms[i].Endpoints[0].Width, MedianWidth = medians[i],
                }).ToList(), RightHandTraffic = true, RampPrefab = ramp.Name, RampWidth = ramp.Width,
                RampSideGap = gap, MaxGrade = limit * factor,
            };
            p.Levels[0].Elevation = height - terrain;
            p.Levels[1].Elevation = upper - terrain;
            for (int i = 0; i < 3; i++)
            {
                var rule = design.BridgeRules.FirstOrDefault(r => r.Arm == arms[i].Index);
                if (rule == null) continue;
                p.Legs[i].MiddlePillarSpacing = rule.MiddleSpacing; p.Legs[i].PillarWidth = rule.PillarWidth;
                p.Legs[i].MaxElevatedEdgeLength = rule.MaxLength;
                if (rule.GroundHeight.HasValue) p.Legs[i].GroundHeight = rule.GroundHeight.Value;
                if (rule.ElevatedHeight.HasValue) p.Legs[i].ElevatedHeight = rule.ElevatedHeight.Value;
            }
            Vec3 World(Vec3 value) => center + right * (value.X * reflect) + forward * value.Z + new Vec3(0, value.Y + terrain, 0);
            var template = new WyeTemplate();
            IReadOnlyList<RoadSegmentSpec> built = Array.Empty<RoadSegmentSpec>();
            bool certified = false;
            for (int attempt = 0; attempt < 4; attempt++)
            {
                var errors = template.ValidateParams(p);
                if (errors.Count != 0) return Fail(string.Join("; ", errors));
                built = AlignExternalTangents(template.Generate(p).Select(s => s.Transform(World)).ToArray(), axes);
                if (built.Count == 0) return Fail("Y template produced no geometry.");
                certified = built.All(s => VerticalProfile.CheckGrade(s.Curve, limit) == VerticalProfileFailure.None);
                if (certified) break;
                float worst = built.Max(s => s.Curve.MaxGrade(128));
                if (!Finite(worst) || worst <= 0) return Fail("Invalid Y grade geometry.");
                p.MaxGrade = Math.Min(p.MaxGrade * .95f, p.MaxGrade * limit / worst * .97f);
            }
            if (!certified) return Fail("Y core exceeds strict maximum-grade certification after bounded re-solving.");
            var degree = new Dictionary<Vec3, int>();
            foreach (var piece in built)
            foreach (var node in new[] { piece.StartNode, piece.EndNode }) degree[node] = degree.TryGetValue(node, out int count) ? count + 1 : 1;
            var paths = new List<(string Id, int Arm, bool Road, bool Left, RoadSegmentSpec[] Pieces)>();
            string Key(RoadSegmentSpec s) => s.Role == RoadRole.Ramp ? s.Name.Substring(0, s.Name.IndexOf('-')) : s.Name.Substring(0, 5);
            foreach (var group in built.GroupBy(Key))
            {
                bool road = group.First().Role != RoadRole.Ramp;
                bool left = group.Key.StartsWith("Left", StringComparison.Ordinal);
                int source = road ? group.Key[0] - 'A' : group.Key[left ? 4 : 5] - 'A';
                if (source < 0 || source > 2) return Fail("Unknown Y path identity.");
                var pending = group.ToList(); var ordered = new List<RoadSegmentSpec>();
                var heads = pending.Where(s => !pending.Any(t => t.EndNode == s.StartNode)).ToArray();
                if (heads.Length != 1) return Fail($"Y path {group.Key} has no unique directed start.");
                var next = heads[0];
                while (pending.Count > 0)
                {
                    ordered.Add(next); pending.Remove(next);
                    if (pending.Count == 0) break;
                    var following = pending.Where(s => s.StartNode == next.EndNode).ToArray();
                    if (following.Length != 1) return Fail($"Y path {group.Key} is not one exact directed chain.");
                    next = following[0];
                }
                if (!EndpointThreeWayCore.IsSimplePath(ordered.Select(s => s.Curve).ToArray(), out string detail))
                    return Fail($"Y path {group.Key} could not be certified non-self-intersecting: {detail}");
                string id = road ? $"WyeArm{arms[source].Index}{(group.Key.Contains("-in") ? "In" : "Out")}" :
                    $"Wye{(left ? "Left" : "Right")}{arms[source].Index}To{arms[group.Key[left ? 5 : 6] - 'A'].Index}";
                paths.Add((id, source, road, left, ordered.ToArray()));
            }
            if (paths.Count != 12 || paths.Count(path => !path.Road) != 6) return Fail("Y core did not produce six distinct movement chains.");
            var ports = new List<EndpointCorePort>();
            var segments = new List<SpatialSegment>();
            foreach (var path in paths)
            {
                foreach (var piece in path.Pieces)
                {
                    var original = arms[path.Arm].Endpoints[0];
                    if (piece.Prefab != (path.Road ? original.Prefab : ramp.Name)) return Fail("Y core uses unknown prefab metadata.");
                    segments.Add(new SpatialSegment(arms[path.Arm].Index,
                        path.Road ? CloverEdgeKind.Main : path.Left ? CloverEdgeKind.LeftRamp : CloverEdgeKind.DirectRamp,
                        piece.Curve, piece.StartNode, piece.EndNode, piece.Prefab, path.Road ? original.Width : ramp.Width, pathId: path.Id));
                }
                if (!path.Road) continue;
                foreach (bool end in new[] { false, true })
                {
                    var piece = end ? path.Pieces.Last() : path.Pieces.First(); var node = end ? piece.EndNode : piece.StartNode;
                    if (degree[node] != 1) continue;
                    var tangent = Vec3.Normalize(piece.Curve.Derivative(end ? 1 : 0));
                    if (node != (end ? piece.Curve.P3 : piece.Curve.P0) || Directions.Dot(tangent, axes[path.Arm] * (end ? 1 : -1)) < .9999f)
                        return Fail("Y external port has no precise boundary node and true road tangent.");
                    var original = arms[path.Arm].Endpoints.Single(e => e.TravelOutward == end);
                    ports.Add(new EndpointCorePort(arms[path.Arm].Index, end, node, tangent, original.Prefab, original.Width));
                }
            }
            if (ports.Count != 6 || ports.Select(port => (port.Arm, port.TravelOutward)).Distinct().Count() != 6)
                return Fail("Y core did not produce six degree-one external carriageway ports.");
            bool Reach(Vec3 start, Vec3 target)
            {
                var seen = new HashSet<Vec3> { start }; var queue = new Queue<Vec3>(); queue.Enqueue(start);
                while (queue.Count > 0) { var at = queue.Dequeue(); foreach (var s in built.Where(s => s.StartNode == at)) if (seen.Add(s.EndNode)) queue.Enqueue(s.EndNode); }
                return seen.Contains(target);
            }
            foreach (var input in ports.Where(port => !port.TravelOutward))
            foreach (var output in ports.Where(port => port.TravelOutward))
                if (Reach(input.Position, output.Position) != (input.Arm != output.Arm)) return Fail("Y core has a missing directed movement or an unintended U-turn.");
            var candidate = new SpatialCandidateResult(SpatialCandidateFailure.None, segments);
            var clearance = EndpointNetworkClearance.Check(candidate, RequiredClearance(design, ramp), out string diagnostic);
            if (clearance != CurveClearanceResult.Clear) return Fail($"Y core deck {clearance}: {diagnostic}");
            var supports = EndpointNetworkSupports.Check(candidate, design.BridgeRules, ramp, out diagnostic, design.SupportRules);
            if (supports != CurveClearanceResult.Clear) return Fail($"Y core support {supports}: {diagnostic}");
            return new EndpointStackCoreResult(true, "Y core uses one common base; original endpoint heights require external transitions.",
                segments.AsReadOnly(), ports.OrderBy(port => port.Arm).ThenBy(port => port.TravelOutward).ToList().AsReadOnly(), center);
        }

        private static IReadOnlyList<RoadSegmentSpec> AlignExternalTangents(IReadOnlyList<RoadSegmentSpec> built, Vec3[] axes)
        {
            var degree = new Dictionary<Vec3, int>();
            foreach (var piece in built)
            foreach (var node in new[] { piece.StartNode, piece.EndNode }) degree[node] = degree.TryGetValue(node, out int count) ? count + 1 : 1;
            return built.Select(piece => {
                if (piece.Role == RoadRole.Ramp) return piece;
                var c = piece.Curve; var a = c.P1; var b = c.P2; var axis = axes[piece.Name[0] - 'A'];
                if (degree[piece.StartNode] == 1) a = c.P0 + axis * (Directions.Dot(c.Derivative(0), axis) >= 0 ? 1 : -1) * Flat(c.P1 - c.P0).Length();
                if (degree[piece.EndNode] == 1) b = c.P3 - axis * (Directions.Dot(c.Derivative(1), axis) >= 0 ? 1 : -1) * Flat(c.P3 - c.P2).Length();
                return new RoadSegmentSpec(piece.Name, piece.Role, new Bezier3(c.P0, a, b, c.P3), piece.Prefab, piece.StartNode, piece.EndNode);
            }).ToArray();
        }
    }
}
