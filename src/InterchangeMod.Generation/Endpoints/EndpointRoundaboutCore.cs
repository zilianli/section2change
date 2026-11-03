using System;
using System.Collections.Generic;
using System.Linq;
using InterchangeMod.Generation.Geometry;
using InterchangeMod.Generation.Roads;
using InterchangeMod.Generation.Templates;

namespace InterchangeMod.Generation.Endpoints
{
    /// <summary>Re-solves the original two-level roundabout in the actual cut-road secant frame.</summary>
    public static class EndpointRoundaboutCore
    {
        private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
        private static Vec3 Flat(Vec3 v) => new Vec3(v.X, 0, v.Z);
        private static bool Range(HeightRange? r) => !r.HasValue || Finite(r.Value.Min) && Finite(r.Value.Max) && r.Value.Min <= r.Value.Max;
        private static bool Covers(HeightRange? planned, HeightRange? actual) => !planned.HasValue || !actual.HasValue
            || planned.Value.Min <= actual.Value.Min && planned.Value.Max >= actual.Value.Max;
        private static bool Spacing(float? s) => !s.HasValue || Finite(s.Value) && s.Value >= 0;
        private static Vec3 Direction(EndpointArm arm) => Vec3.Normalize(arm.Endpoints.Select(p => Vec3.Normalize(Flat(p.OutwardTangent)))
            .Aggregate(Vec3.Zero, (a, b) => a + b));
        private static EndpointStackCoreResult Fail(string message) => new EndpointStackCoreResult(false, message,
            Array.Empty<SpatialSegment>(), Array.Empty<EndpointCorePort>());
        private static bool RoadValid(RoadInfo road) => road != null && !string.IsNullOrWhiteSpace(road.Name)
            && road.ForwardLanes > 0 && road.BackwardLanes == 0 && road.PublicTransportLanes == 0
            && Finite(road.Width) && road.Width > 0 && road.Width <= 1000
            && Finite(road.ElevatedLength) && road.ElevatedLength >= 0
            && (road.ElevatedLength == 0 || road.ElevatedLength >= 10 && road.ElevatedLength <= 1000)
            && Finite(road.PillarWidth) && road.PillarWidth >= 0 && road.PillarWidth <= 1000
            && Spacing(road.MiddlePillarSpacing) && Range(road.GroundHeight) && Range(road.ElevatedHeight);

        /// <summary>All actual full-road, ramp and ring variants participate in the two-layer envelope.</summary>
        public static float RequiredClearance(CloverleafRoadDesign design, RoadInfo ramp, RoadInfo ring)
        {
            if (design == null || ramp == null || ring == null) return float.NaN;
            var fallback = new HeightRange(0, HeightRange.MeasuredMinimumGap);
            var ranges = new List<HeightRange>();
            foreach (var road in new[] { ramp, ring })
            { ranges.Add(road.GroundHeight ?? fallback); ranges.Add(road.ElevatedHeight ?? fallback); }
            foreach (var rule in design.BridgeRules)
            {
                if (rule == null) return float.NaN;
                ranges.Add(rule.GroundHeight ?? fallback); ranges.Add(rule.ElevatedHeight ?? fallback);
            }
            if (design.BridgeRules.Count < 4) ranges.Add(fallback);
            if (ranges.Any(r => !Range(r))) return float.NaN;
            // Setup reserves at least 10m between layers. Clearance is the actual
            // envelope/bridge minimum, leaving that placement margin intact; an
            // exactly-equal threshold cannot receive a strict clearance certificate.
            float required = HeightRange.BridgeThreshold;
            foreach (var lower in ranges) foreach (var upper in ranges)
                required = Math.Max(required, HeightRange.RequiredGap(lower, upper));
            return Finite(required) ? required : float.NaN;
        }

        public static EndpointStackCoreResult Create(IReadOnlyList<EndpointArm> arms, bool rightHandTraffic,
            float mainHeight, float crossHeight, RoadInfo ramp, RoadInfo ring, float maxGrade,
            CloverleafRoadDesign design, float terrainHeight, bool racetrack = false)
        {
            var failures = new List<string>();
            foreach (var trial in Candidates(arms, rightHandTraffic, mainHeight, crossHeight, ramp, ring,
                maxGrade, design, terrainHeight, racetrack))
            {
                if (trial.Success) return trial;
                failures.Add(trial.Diagnostic);
            }
            return Fail(string.Join("; ", failures));
        }

        /// <summary>Finite analytic alternatives; generated geometry is never scaled or deformed.</summary>
        public static IEnumerable<EndpointStackCoreResult> Candidates(IReadOnlyList<EndpointArm> arms, bool rightHandTraffic,
            float mainHeight, float crossHeight, RoadInfo ramp, RoadInfo ring, float maxGrade,
            CloverleafRoadDesign design, float terrainHeight, bool racetrack = false)
        {
            string invalid = Validate(arms, mainHeight, crossHeight, ramp, ring, maxGrade, design, terrainHeight);
            if (invalid.Length != 0) { yield return Fail(invalid); yield break; }
            var selected = arms.OrderBy(a => a.Index).ToArray();
            Vec3 Mid(int i) => Flat(selected[i].Endpoints[0].Position * .5f + selected[i].Endpoints[1].Position * .5f);
            var forward = Vec3.Normalize(Mid(0) - Mid(2));
            var cross = Vec3.Normalize(Mid(1) - Mid(3));
            var right = Directions.RightOf(forward); float reflect = rightHandTraffic ? 1 : -1;
            if (!Finite(forward.X) || !Finite(forward.Z) || !Finite(cross.X) || !Finite(cross.Z)
                || Math.Abs(Directions.Dot(forward, cross)) > .5f + .000001f)
            { yield return Fail("Roundabout cut secants require a finite crossing angle of 60 to 120 degrees."); yield break; }
            var axes = new[] { forward, cross, -forward, -cross };
            if (Enumerable.Range(0, 4).Any(i => !Finite(Direction(selected[i]).X)
                || !Finite(Direction(selected[i]).Z) || Directions.Dot(axes[i], Direction(selected[i])) <= 0))
            { yield return Fail("Roundabout cut roads face backward from their opposite-family secants."); yield break; }
            double Cross(Vec3 a, Vec3 b) => (double)a.X * b.Z - (double)a.Z * b.X;
            double along = Cross(Mid(1) - Mid(0), cross) / Cross(forward, cross);
            if (double.IsNaN(along) || double.IsInfinity(along) || Math.Abs(along) > float.MaxValue)
            { yield return Fail("Roundabout cut secants have no finite common reference."); yield break; }
            var center = Mid(0) + forward * (float)along;
            if (!Finite(center.X) || !Finite(center.Z)
                || Enumerable.Range(0, 4).Any(i => Directions.Dot(Mid(i) - center, axes[i]) <= 0))
            { yield return Fail("Roundabout cutpoints must lie outside their finite core reference."); yield break; }
            int[] original = Directions.Dot(cross, right) * reflect > 0 ? new[] { 0, 1, 2, 3 } : new[] { 0, 3, 2, 1 };
            var templateCross = axes[original[1]];
            float angle = (float)(Math.Atan2(Directions.Dot(templateCross, right) * reflect,
                Directions.Dot(templateCross, forward)) * 180 / Math.PI);
            var medians = new float[4];
            foreach (var arm in selected)
            {
                var input = arm.Endpoints.Single(p => !p.TravelOutward); var output = arm.Endpoints.Single(p => p.TravelOutward);
                float median = Directions.Dot(Flat(output.Position - input.Position), Directions.RightOf(Direction(arm))) * reflect - input.Width;
                if (!Finite(median) || median < -.01f || median > 1000)
                { yield return Fail($"Roundabout arm {arm.Index + 1} has overlapping or incorrectly ordered carriageways."); yield break; }
                medians[arm.Index] = Math.Max(0, median);
            }
            foreach (var alternative in new[] { (Gap: 3f, Extra: 0f, Factor: 1f), (Gap: 5f, Extra: 0f, Factor: 1f),
                (Gap: 8f, Extra: 0f, Factor: 1f), (Gap: 3f, Extra: 10f, Factor: 1f),
                (Gap: 3f, Extra: 0f, Factor: .85f), (Gap: 5f, Extra: 20f, Factor: .85f) })
            {
                var trial = Solve(selected, original, axes, center, forward, right, reflect, angle, medians,
                    mainHeight, crossHeight, ramp, ring, maxGrade, design, terrainHeight, racetrack,
                    alternative.Gap, alternative.Extra, alternative.Factor);
                yield return trial.Success ? trial : Fail($"Roundabout side gap {alternative.Gap:F0}m, radius extra {alternative.Extra:F0}m, grade factor {alternative.Factor:F2}: {trial.Diagnostic}");
            }
        }

        private static string Validate(IReadOnlyList<EndpointArm> arms, float hm, float hc, RoadInfo ramp,
            RoadInfo ring, float grade, CloverleafRoadDesign design, float terrain)
        {
            // The four-way divided-road structural contract is shared with stack;
            // roundabout reachability is validated separately, including its legal cycle.
            if (arms == null || EndpointConstraints.Validate(arms, InterchangeType.Stack).Count != 0 || arms.Any(a => a.Endpoints.Count != 2))
                return "Roundabout requires four valid divided-road arms with eight directed endpoints.";
            if (!Finite(hm) || !Finite(hc) || !Finite(terrain) || !Finite(hm - terrain) || !Finite(hc - terrain)
                || Math.Min(hm, hc) < terrain || !Finite(grade) || grade <= 0 || grade > .30f
                || !RoadValid(ramp) || !RoadValid(ring) || design == null)
                return "Invalid roundabout height, terrain, strict maximum grade, actual ramp, ring or design.";
            var selected = arms.OrderBy(a => a.Index).ToArray();
            foreach (var family in new[] { new[] { 0, 2 }, new[] { 1, 3 } })
            {
                var endpoints = family.SelectMany(i => selected[i].Endpoints).ToArray(); var first = endpoints[0];
                if (first.Width > 1000 || endpoints.Any(p => p.Prefab != first.Prefab || p.Width != first.Width || p.Lanes != first.Lanes))
                    return "Roundabout opposite road families require identical actual prefabs, widths and lanes.";
            }
            if (ramp.Width > selected[0].Endpoints[0].Width || ramp.ForwardLanes >= selected[0].Endpoints[0].Lanes
                || ring.ForwardLanes < Math.Max(2, selected[1].Endpoints[0].Lanes))
                return "Roundabout requires a suitable mainline ramp and a ring carrying at least two and all cross-road lanes.";
            if (design.Balances.Count != 2 || design.Balances.Any(b => b == null || b.Arm != 0 && b.Arm != 2
                || string.IsNullOrWhiteSpace(b.Prefab) || !Finite(b.Width) || b.Width <= 0 || b.Width > selected[b.Arm].Endpoints[0].Width
                || b.Lanes < 1 || b.RampLanes != ramp.ForwardLanes || b.Lanes + b.RampLanes != selected[b.Arm].Endpoints[0].Lanes)
                || design.Balances.Select(b => b.Arm).Distinct().Count() != 2)
                return "Roundabout requires exact N-R lane balances on main arms 0/2 only.";
            var balance = design.Balances[0]; var otherBalance = design.Balances[1];
            if (balance.Prefab != otherBalance.Prefab || balance.Width != otherBalance.Width || balance.Lanes != otherBalance.Lanes)
                return "Roundabout main arms require the same actual reduced road.";
            if (design.BridgeRules.Any(r => r == null || r.Arm < 0 || r.Arm > 3 || !Finite(r.MaxLength) || r.MaxLength < 10 || r.MaxLength > 1000
                || !Finite(r.Width) || r.Width != selected[r.Arm].Endpoints[0].Width || !Finite(r.PillarWidth) || r.PillarWidth <= 0 || r.PillarWidth > 1000
                || !Spacing(r.MiddleSpacing) || !Range(r.GroundHeight) || !Range(r.ElevatedHeight))
                || design.BridgeRules.Select(r => r.Arm).Distinct().Count() != design.BridgeRules.Count)
                return "Invalid roundabout full-road bridge support metadata.";
            if (design.SupportRules.Any(r => r == null || string.IsNullOrWhiteSpace(r.Prefab) || !Finite(r.MaxLength) || r.MaxLength < 10 || r.MaxLength > 1000
                || !Spacing(r.MiddleSpacing) || !Finite(r.PillarWidth) || r.PillarWidth <= 0 || r.PillarWidth > 1000)
                || design.SupportRules.Select(r => r.Prefab).Distinct().Count() != design.SupportRules.Count)
                return "Invalid roundabout actual prefab support metadata.";
            var used = selected.Select(a => a.Endpoints[0].Prefab).Concat(design.Balances.Select(b => b.Prefab))
                .Concat(new[] { ramp.Name, ring.Name }).Distinct().ToArray();
            if (design.SupportRules.Count != 0 && used.Any(name => !design.SupportRules.Any(r => r.Prefab == name)))
                return "Roundabout is missing actual prefab support metadata.";
            foreach (var road in new[] { ramp, ring })
            {
                if (selected.SelectMany(a => a.Endpoints).Any(p => p.Prefab == road.Name && (p.Width != road.Width || p.Lanes != road.ForwardLanes))
                    || design.Balances.Any(b => b.Prefab == road.Name && (b.Width != road.Width || b.Lanes != road.ForwardLanes)))
                    return "Roundabout has conflicting road metadata for the same actual prefab.";
                var rule = design.SupportRules.FirstOrDefault(r => r.Prefab == road.Name);
                if (rule != null && (road.ElevatedLength > 0 && rule.MaxLength != road.ElevatedLength
                    || rule.MiddleSpacing != road.MiddlePillarSpacing || road.PillarWidth > 0 && rule.PillarWidth != road.PillarWidth))
                    return "Roundabout actual ramp/ring support metadata cannot be overridden.";
            }
            if (ramp.Name == ring.Name && (ramp.Width != ring.Width || ramp.ForwardLanes != ring.ForwardLanes
                || ramp.ElevatedLength != ring.ElevatedLength || ramp.MiddlePillarSpacing != ring.MiddlePillarSpacing
                || ramp.PillarWidth != ring.PillarWidth || !ramp.GroundHeight.Equals(ring.GroundHeight) || !ramp.ElevatedHeight.Equals(ring.ElevatedHeight)))
                return "Roundabout ramp and ring have conflicting identities for one actual prefab.";
            foreach (var group in design.BridgeRules.GroupBy(r => selected[r.Arm].Endpoints[0].Prefab))
            {
                var first = group.First(); var actual = design.SupportRules.FirstOrDefault(r => r.Prefab == group.Key);
                if (group.Any(r => r.MaxLength != first.MaxLength || r.MiddleSpacing != first.MiddleSpacing || r.PillarWidth != first.PillarWidth)
                    || actual != null && (actual.MaxLength != first.MaxLength || actual.MiddleSpacing != first.MiddleSpacing || actual.PillarWidth != first.PillarWidth))
                    return "Roundabout has conflicting full-road prefab support metadata.";
                foreach (var road in new[] { ramp, ring }.Where(r => r.Name == group.Key))
                    if (road.ElevatedLength > 0 && road.ElevatedLength != first.MaxLength || road.MiddlePillarSpacing != first.MiddleSpacing
                        || road.PillarWidth > 0 && road.PillarWidth != first.PillarWidth
                        || group.Any(rule => !Covers(rule.GroundHeight, road.GroundHeight) || !Covers(rule.ElevatedHeight, road.ElevatedHeight)))
                        return "Roundabout actual road and full-road support/envelope metadata disagree.";
            }
            if (selected.SelectMany(a => a.Endpoints).Any(p => p.Prefab == balance.Prefab && (p.Width != balance.Width || p.Lanes != balance.Lanes)))
                return "Roundabout reduced-road identity conflicts with a full road.";
            float clearance = RequiredClearance(design, ramp, ring);
            if (!Finite(clearance) || Math.Abs(hm - hc) < clearance)
                return $"Roundabout needs at least {clearance:F2}m between all actual road envelopes.";
            if (Math.Max(hm, hc) - terrain < HeightRange.BridgeThreshold)
                return "Roundabout upper road must reach the actual elevated bridge threshold above terrain.";
            return "";
        }

        private static EndpointStackCoreResult Solve(EndpointArm[] arms, int[] original, Vec3[] axes, Vec3 center,
            Vec3 forward, Vec3 right, float reflect, float angle, float[] medians, float hm, float hc,
            RoadInfo ramp, RoadInfo ring, float limit, CloverleafRoadDesign design, float terrain, bool racetrack,
            float sideGap, float radiusExtra, float factor)
        {
            var p = new InterchangeParams {
                Legs = InterchangeParams.CrossLegs(angle, 0, 1), RightHandTraffic = true,
                RampPrefab = ramp.Name, RampWidth = ramp.Width, RingPrefab = ring.Name, RingWidth = ring.Width,
                RoundaboutRacetrack = racetrack, LoopRadius = 0, RampSideGap = sideGap, MaxGrade = limit * factor,
            };
            p.Levels[0].Elevation = hm - terrain; p.Levels[1].Elevation = hc - terrain;
            p.SetRoad(0, 2, RoadForm.Dual, arms[0].Endpoints[0].Width, (medians[0] + medians[2]) / 2, arms[0].Endpoints[0].Prefab);
            p.SetRoad(1, 3, RoadForm.Dual, arms[1].Endpoints[0].Width, (medians[1] + medians[3]) / 2, arms[1].Endpoints[0].Prefab);
            foreach (int slot in Enumerable.Range(0, 4))
            {
                var leg = p.Legs[slot]; leg.StubLength = 40;
                var rule = design.BridgeRules.FirstOrDefault(r => r.Arm == original[slot])
                    ?? design.BridgeRules.FirstOrDefault(r => r.Arm == original[(slot + 2) % 4]);
                if (rule == null) continue;
                leg.MaxElevatedEdgeLength = rule.MaxLength; leg.MiddlePillarSpacing = rule.MiddleSpacing; leg.PillarWidth = rule.PillarWidth;
                if (rule.GroundHeight.HasValue) leg.GroundHeight = rule.GroundHeight.Value;
                if (rule.ElevatedHeight.HasValue) leg.ElevatedHeight = rule.ElevatedHeight.Value;
            }
            var balance = design.Balances.First(b => b.Arm == 0);
            p.MainReducedPrefab = balance.Prefab; p.MainReducedWidth = balance.Width;
            Vec3 World(Vec3 v) => center + right * (v.X * reflect) + forward * v.Z + new Vec3(0, v.Y + terrain, 0);
            var template = new RoundaboutTemplate(); RoadSegmentSpec[] built = Array.Empty<RoadSegmentSpec>();
            bool certified = false;
            for (int attempt = 0; attempt < 4; attempt++)
            {
                p.LoopRadius = 0;
                if (radiusExtra > 0) p.LoopRadius = Math.Min(300, RoundaboutTemplate.ResolveRadius(p) + radiusExtra);
                var errors = template.ValidateParams(p);
                if (errors.Count != 0) return Fail(string.Join("; ", errors));
                built = template.Generate(p).Select(s => s.Transform(World)).ToArray();
                if (built.Length == 0) return Fail("The original roundabout template produced no geometry.");
                certified = built.All(s => VerticalProfile.CheckGrade(s.Curve, limit) == VerticalProfileFailure.None);
                if (certified) break;
                float worst = built.Max(s => s.Curve.MaxGrade(128));
                if (!Finite(worst) || worst <= 0) return Fail("Invalid roundabout maximum-grade geometry.");
                p.MaxGrade = Math.Min(p.MaxGrade * .95f, p.MaxGrade * limit / worst * .97f);
            }
            if (!certified) return Fail("Roundabout exceeds strict maximum grade after bounded analytic re-solving.");
            bool Ring(RoadSegmentSpec s) => s.Name.StartsWith("Ring", StringComparison.Ordinal);
            string Path(RoadSegmentSpec s)
            {
                string suffix = s.Name.EndsWith("B", StringComparison.Ordinal) ? "B" : "";
                foreach (string prefix in new[] { "Main", "CrossIn", "CrossOut", "RampOff", "RampOn" })
                    if (s.Name.StartsWith(prefix, StringComparison.Ordinal)) return prefix + suffix;
                return "";
            }
            var paths = new List<(string Id, RoadSegmentSpec[] Pieces)>();
            foreach (var group in built.Where(s => !Ring(s)).GroupBy(Path))
            {
                if (group.Key.Length == 0) return Fail("Unknown original roundabout path identity.");
                var remaining = group.ToList(); var ordered = new List<RoadSegmentSpec>();
                var heads = remaining.Where(s => !remaining.Any(t => t.EndNode == s.StartNode)).ToArray();
                if (heads.Length != 1) return Fail($"Roundabout path {group.Key} has no unique directed start.");
                var next = heads[0];
                while (remaining.Count > 0)
                {
                    ordered.Add(next); remaining.Remove(next);
                    if (remaining.Count == 0) break;
                    var following = remaining.Where(s => s.StartNode == next.EndNode).ToArray();
                    if (following.Length != 1) return Fail($"Roundabout path {group.Key} is not one directed chain.");
                    next = following[0];
                }
                paths.Add((group.Key, ordered.ToArray()));
            }
            var ringPieces = built.Where(Ring).ToArray();
            if (ringPieces.Length == 0 || ringPieces.Any(s => s.Role != RoadRole.Ramp || s.Prefab != ring.Name))
                return Fail("The original roundabout did not produce the actual ring prefab.");
            var ringNodes = ringPieces.SelectMany(s => new[] { s.StartNode, s.EndNode }).Distinct().ToArray();
            if (ringNodes.Any(n => ringPieces.Count(s => s.StartNode == n) != 1 || ringPieces.Count(s => s.EndNode == n) != 1))
                return Fail("The roundabout ring is not a directed cycle with one incoming and outgoing edge at each node.");
            var junctions = ringNodes.Where(n => built.Any(s => !Ring(s) && (s.StartNode == n || s.EndNode == n))).ToArray();
            if (junctions.Length != 8) return Fail("The roundabout ring requires four ramp and four cross-road junctions.");
            var unused = new HashSet<RoadSegmentSpec>(ringPieces);
            foreach (var node in junctions)
            {
                var chain = new List<RoadSegmentSpec>(); var at = node;
                do {
                    var next = ringPieces.Single(s => s.StartNode == at);
                    if (!unused.Remove(next)) return Fail("The roundabout ring has a repeated or disconnected directed cycle.");
                    chain.Add(next); at = next.EndNode;
                } while (!junctions.Contains(at));
                paths.Add(($"RingArc{paths.Count(path => path.Id.StartsWith("RingArc", StringComparison.Ordinal))}", chain.ToArray()));
            }
            if (unused.Count != 0) return Fail("The roundabout has an isolated extra ring cycle.");
            var degree = new Dictionary<Vec3, int>();
            foreach (var piece in built) foreach (var node in new[] { piece.StartNode, piece.EndNode })
                degree[node] = degree.TryGetValue(node, out int count) ? count + 1 : 1;
            var ports = new List<EndpointCorePort>();
            foreach (var path in paths.Where(path => path.Pieces[0].Role != RoadRole.Ramp))
            foreach (bool end in new[] { false, true })
            {
                var piece = end ? path.Pieces.Last() : path.Pieces.First(); var node = end ? piece.EndNode : piece.StartNode;
                if (degree[node] != 1) continue;
                var tangent = Vec3.Normalize(piece.Curve.Derivative(end ? 1 : 0)); var outward = tangent * (end ? 1 : -1);
                var matches = Enumerable.Range(0, 4).Where(i => Directions.Dot(outward, axes[i]) > .9999f).ToArray();
                if (matches.Length != 1 || node != (end ? piece.Curve.P3 : piece.Curve.P0))
                    return Fail("Roundabout external port does not match its original cut-road secant.");
                int arm = matches[0]; var road = arms[arm].Endpoints.Single(e => e.TravelOutward == end);
                if (piece.Prefab != road.Prefab) return Fail("Roundabout external port uses a reduced or unknown prefab.");
                if (Flat(node - center).Length() > 1400) return Fail("Roundabout exceeds its finite practical core extent.");
                ports.Add(new EndpointCorePort(arm, end, node, tangent, road.Prefab, road.Width));
            }
            if (ports.Count != 8 || ports.Select(port => (port.Arm, port.TravelOutward)).Distinct().Count() != 8)
                return Fail("Roundabout did not produce eight distinct original carriageway ports.");
            bool Reach(Vec3 from, Vec3 to)
            {
                var seen = new HashSet<Vec3> { from }; var queue = new Queue<Vec3>(); queue.Enqueue(from);
                while (queue.Count > 0) { var at = queue.Dequeue(); foreach (var s in built.Where(s => s.StartNode == at))
                    if (seen.Add(s.EndNode)) queue.Enqueue(s.EndNode); }
                return seen.Contains(to);
            }
            foreach (var input in ports.Where(port => !port.TravelOutward))
            foreach (var output in ports.Where(port => port.TravelOutward))
                if (!Reach(input.Position, output.Position)) return Fail("Roundabout lost a directed movement, including a legal same-arm ring movement.");
            var roadSources = new Dictionary<string, int>();
            foreach (var path in paths.Where(path => path.Pieces[0].Role != RoadRole.Ramp))
            {
                var external = ports.Where(port => port.Position == path.Pieces[0].StartNode || port.Position == path.Pieces.Last().EndNode)
                    .OrderBy(port => port.TravelOutward).First();
                roadSources.Add(path.Id, external.Arm);
            }
            int RampSource(RoadSegmentSpec[] pieces)
            {
                foreach (var path in paths.Where(path => path.Id == "Main" || path.Id == "MainB"))
                    if (path.Pieces.Any(s => s.StartNode == pieces[0].StartNode || s.EndNode == pieces[0].StartNode
                        || s.StartNode == pieces.Last().EndNode || s.EndNode == pieces.Last().EndNode)) return roadSources[path.Id];
                return -1;
            }
            int RingSource(Vec3 node)
            {
                foreach (var path in paths.Where(path => !path.Id.StartsWith("RingArc", StringComparison.Ordinal)))
                    if (path.Pieces[0].StartNode == node || path.Pieces.Last().EndNode == node)
                        return roadSources.TryGetValue(path.Id, out int arm) ? arm : RampSource(path.Pieces);
                return -1;
            }
            var segments = new List<SpatialSegment>();
            foreach (var path in paths)
            {
                bool isRing = path.Id.StartsWith("RingArc", StringComparison.Ordinal); bool road = path.Pieces[0].Role != RoadRole.Ramp;
                int source = road ? roadSources[path.Id] : isRing ? RingSource(path.Pieces[0].StartNode) : RampSource(path.Pieces);
                if (source < 0) return Fail($"Roundabout path {path.Id} has no actual road source.");
                if (!EndpointThreeWayCore.IsSimplePath(path.Pieces.Select(s => s.Curve).ToArray(), out string simpleDetail))
                    return Fail($"Roundabout path {path.Id} is not provably simple: {simpleDetail}");
                foreach (var piece in path.Pieces)
                {
                    float width = isRing ? ring.Width : !road ? ramp.Width : piece.Prefab == balance.Prefab ? balance.Width : arms[source].Endpoints[0].Width;
                    if (piece.Prefab != (isRing ? ring.Name : !road ? ramp.Name : arms[source].Endpoints[0].Prefab)
                        && (!road || source % 2 != 0 || piece.Prefab != balance.Prefab)) return Fail($"Roundabout path {path.Id} has unknown actual prefab metadata.");
                    var curvature = CurveCurvature.CheckMinimumRadius(piece.Curve, Math.Max(12, 2 * width));
                    if (curvature != CurveCurvatureResult.Clear) return Fail($"Roundabout path {path.Id} strict actual-width curvature {curvature}.");
                    segments.Add(new SpatialSegment(source, road ? CloverEdgeKind.Main : isRing ? CloverEdgeKind.LoopRamp : CloverEdgeKind.DirectRamp,
                        piece.Curve, piece.StartNode, piece.EndNode, piece.Prefab, width, pathId: path.Id));
                }
            }
            var candidate = new SpatialCandidateResult(SpatialCandidateFailure.None, segments);
            var deck = EndpointNetworkClearance.Check(candidate, RequiredClearance(design, ramp, ring), out string detail);
            if (deck != CurveClearanceResult.Clear) return Fail($"Roundabout core deck {deck}: {detail}");
            var supportRules = EffectiveSupports(arms, design, ramp, ring);
            var supports = EndpointNetworkSupports.Check(candidate, design.BridgeRules, ramp, out detail, supportRules);
            if (supports != CurveClearanceResult.Clear) return Fail($"Roundabout core support {supports}: {detail}");
            return new EndpointStackCoreResult(true, "Original roundabout geometry; source endpoint heights require external transitions.",
                segments.AsReadOnly(), ports.OrderBy(port => port.Arm).ThenBy(port => port.TravelOutward).ToList().AsReadOnly(), center);
        }

        private static IReadOnlyList<EndpointPrefabSupportRule> EffectiveSupports(EndpointArm[] arms,
            CloverleafRoadDesign design, RoadInfo ramp, RoadInfo ring)
        {
            var rules = design.SupportRules.ToList();
            void Add(string prefab, float max, float? spacing, float pillar)
            { if (!rules.Any(r => r.Prefab == prefab)) rules.Add(new EndpointPrefabSupportRule(prefab, max > 0 ? max : 80, spacing, pillar > 0 ? pillar : 4)); }
            foreach (var road in new[] { ramp, ring }) Add(road.Name, road.ElevatedLength, road.MiddlePillarSpacing, road.PillarWidth);
            foreach (var arm in arms)
            {
                var bridge = design.BridgeRules.FirstOrDefault(r => r.Arm == arm.Index);
                Add(arm.Endpoints[0].Prefab, bridge?.MaxLength ?? 80, bridge?.MiddleSpacing, bridge?.PillarWidth ?? 4);
            }
            foreach (var balance in design.Balances) Add(balance.Prefab, 80, null, 4);
            return rules.AsReadOnly();
        }
    }
}
