using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using InterchangeMod.Generation.Geometry;
using InterchangeMod.Generation.Roads;

namespace InterchangeMod.Generation.Endpoints
{
    /// <summary>
    /// Replaces a mathematical dual-carriageway port with a native bidirectional
    /// stem and two directed, edge-aligned branches. Entity identities are never
    /// copied into the resulting geometry; original-road cutting remains the caller's job.
    /// </summary>
    public static partial class EndpointTwoWayTransition
    {
        public static SpatialCandidateResult Connect(SpatialCandidateResult internalCandidate,
            IReadOnlyList<EndpointArm> actualCutArms, IReadOnlyList<EndpointArm> proxyCutArms,
            bool rht, float maxGrade, IReadOnlyList<RoadInfo> catalog, CloverleafRoadDesign design,
            float verticalClearance)
            => ConnectBounded(internalCandidate, actualCutArms, proxyCutArms, rht, maxGrade, catalog, design,
                verticalClearance, 3, out _);

        /// <summary>Charges each distinct span allocation to the caller's shared search budget.</summary>
        public static SpatialCandidateResult ConnectBounded(SpatialCandidateResult internalCandidate,
            IReadOnlyList<EndpointArm> actualCutArms,IReadOnlyList<EndpointArm> proxyCutArms,bool rht,float maxGrade,
            IReadOnlyList<RoadInfo> catalog,CloverleafRoadDesign design,float verticalClearance,int maxCandidates,out int attempts)
            =>ConnectBoundedCore(internalCandidate,actualCutArms,proxyCutArms,rht,maxGrade,catalog,design,verticalClearance,maxCandidates,out attempts,false);

        internal static SpatialCandidateResult ConnectBoundedCore(SpatialCandidateResult internalCandidate,
            IReadOnlyList<EndpointArm> actualCutArms, IReadOnlyList<EndpointArm> proxyCutArms,
            bool rht, float maxGrade, IReadOnlyList<RoadInfo> catalog, CloverleafRoadDesign design,
            float verticalClearance, int maxCandidates, out int attempts,bool jointGrade=false,CancellationToken cancellation=default)
        {
            attempts = 0;
            cancellation.ThrowIfCancellationRequested();
            SpatialCandidateResult Fail(SpatialCandidateFailure failure, string detail) =>
                new SpatialCandidateResult(failure, Array.Empty<SpatialSegment>(), detail);
            if (internalCandidate == null || internalCandidate.Failure != SpatialCandidateFailure.None
                || internalCandidate.Segments.Count == 0 || actualCutArms == null || proxyCutArms == null
                || actualCutArms.Count < 1 || actualCutArms.Count > 4 || actualCutArms.Count != proxyCutArms.Count
                || actualCutArms.Any(a => a == null) || proxyCutArms.Any(a => a == null)
                || actualCutArms.Select(a => a.Index).Distinct().Count() != actualCutArms.Count
                || proxyCutArms.Select(a => a.Index).Distinct().Count() != proxyCutArms.Count
                || actualCutArms.Any(a => a.Index < 0 || a.Index > 3 || !proxyCutArms.Any(p => p.Index == a.Index)))
                return Fail(SpatialCandidateFailure.InvalidSelection, "Native transitions require matching, distinct actual and mathematical arms.");

            // Preserve the existing, already certified all-dual route byte for byte.
            if (actualCutArms.All(a => a.Endpoints.Count == 2 && a.Endpoints.All(p => p != null && p.OneWay)))
                return internalCandidate;
            if (maxCandidates < 1 || !Finite(maxGrade) || maxGrade <= 0 || maxGrade > .30f || !Finite(verticalClearance)
                || verticalClearance <= 0 || catalog == null || design == null || catalog.Any(r => r == null))
                return Fail(SpatialCandidateFailure.InvalidParameter, "Invalid native-transition grade, clearance or road catalog.");

            var native = new List<(EndpointArm Actual, EndpointArm Proxy, RoadInfo Stem, RoadInfo Incoming, RoadInfo Outgoing)>();
            foreach (var arm in actualCutArms)
            {
                var proxy = proxyCutArms.Single(a => a.Index == arm.Index);
                if (proxy.Endpoints.Count != 2 || proxy.Endpoints.Any(p => !Valid(p) || !p.OneWay)
                    || proxy.Endpoints[0].TravelOutward == proxy.Endpoints[1].TravelOutward)
                    return Fail(SpatialCandidateFailure.InvalidSelection, $"Arm {arm.Index + 1} needs distinct incoming and outgoing mathematical ports.");
                if (arm.Endpoints.Count == 2)
                {
                    if (arm.Endpoints.Any(p => !Valid(p) || !p.OneWay)
                        || arm.Endpoints.Any(p => !proxy.Endpoints.Any(q => SamePort(p, q))))
                        return Fail(SpatialCandidateFailure.InvalidSelection, $"Arm {arm.Index + 1} changed an original dual-carriageway cut.");
                    continue;
                }
                if (arm.Endpoints.Count != 1 || !Valid(arm.Endpoints[0]) || arm.Endpoints[0].OneWay)
                    return Fail(SpatialCandidateFailure.InvalidSelection, $"Arm {arm.Index + 1} is not a symmetric native two-way cut.");
                var selected = arm.Endpoints[0];
                RoadInfo? Find(SelectedEndpoint p) => catalog.Count(r => r.Name == p.Prefab) == 1
                    ? catalog.Single(r => r.Name == p.Prefab) : null;
                var stem = Find(selected);
                var incoming = Find(proxy.Endpoints.Single(p => !p.TravelOutward));
                var outgoing = Find(proxy.Endpoints.Single(p => p.TravelOutward));
                if (!Valid(stem) || stem!.IsOneWay || stem.ForwardLanes != selected.Lanes || stem.BackwardLanes != selected.Lanes
                    || stem.PublicTransportLanes != 0 || stem.Width != selected.Width
                    || !Matches(incoming, proxy.Endpoints.Single(p => !p.TravelOutward), selected.Lanes)
                    || !Matches(outgoing, proxy.Endpoints.Single(p => p.TravelOutward), selected.Lanes)
                    || incoming!.IsHighway != stem.IsHighway || outgoing!.IsHighway != stem.IsHighway
                    || incoming.Width > stem.Width || outgoing.Width > stem.Width)
                    return Fail(SpatialCandidateFailure.InvalidSelection,
                        $"Arm {arm.Index + 1} needs exact symmetric native lanes and real one-way prefabs of the same road class and per-direction lane count.");
                native.Add((arm, proxy, stem!, incoming!, outgoing!));
            }

            var supportRules = new List<EndpointPrefabSupportRule>();
            var names = internalCandidate.Segments.Select(s => s.Prefab)
                .Concat(native.SelectMany(a => new[] { a.Stem.Name, a.Incoming.Name, a.Outgoing.Name })).Distinct().ToArray();
            foreach (var name in names)
            {
                if (string.IsNullOrWhiteSpace(name) || catalog.Count(r => r.Name == name) != 1)
                    return Fail(SpatialCandidateFailure.InvalidSelection, "Every transition and internal road needs an explicit real prefab in the catalog.");
                var road = catalog.Single(r => r.Name == name);
                if (!Valid(road)) return Fail(SpatialCandidateFailure.InvalidParameter, $"Invalid real support metadata for {name}.");
                var supplied = design.SupportRules.Where(r => r.Prefab == name).ToArray();
                if (supplied.Length > 1 || supplied.Any(r => !Finite(r.MaxLength) || r.MaxLength < 10 || r.MaxLength > 1000))
                    return Fail(SpatialCandidateFailure.InvalidParameter, $"Invalid or duplicate support rule for {name}.");
                float maximum = road.ElevatedLength > 0 ? road.ElevatedLength : supplied.FirstOrDefault()?.MaxLength ?? 80;
                if (supplied.Length == 1) maximum = Math.Min(maximum, supplied[0].MaxLength);
                supportRules.Add(new EndpointPrefabSupportRule(name!, maximum, road.MiddlePillarSpacing,
                    road.PillarWidth > 0 ? road.PillarWidth : 3));
            }
            if (internalCandidate.Segments.Any(s => !s.RoadWidth.HasValue
                || catalog.Single(r => r.Name == s.Prefab).Width != s.RoadWidth.Value))
                return Fail(SpatialCandidateFailure.InvalidGeometry, "Internal road widths must match their explicit real prefabs.");

            string diagnostic = "Native two-way transition has no certified bounded geometry.";
            SpatialCandidateFailure last = SpatialCandidateFailure.InvalidGeometry;
            // The first course must remain a single real native road segment: only
            // that terminal course participates in the legal shared-node fork.
            // Three finite span allocations allow different actual source tangents
            // and widths without weakening any geometry or clearance requirement.
            foreach (float firstSpan in new[] { .90f, .75f, .60f }.Take(Math.Min(3, maxCandidates)))
            {
                cancellation.ThrowIfCancellationRequested();
                attempts++;
                var output = new List<SpatialSegment>(internalCandidate.Segments);
                var usedPaths = new HashSet<string>(output.Select(s => s.PathId), StringComparer.Ordinal);
                bool valid = true;
                foreach (var arm in native)
                {
                    cancellation.ThrowIfCancellationRequested();
                    float Span(RoadInfo r) => supportRules.Single(s => s.Prefab == r.Name).MaxLength;
                    var addition = Build(arm.Actual, arm.Proxy, arm.Stem, arm.Incoming, arm.Outgoing,
                        internalCandidate, rht, maxGrade, Span, firstSpan, usedPaths,jointGrade:jointGrade);
                    if (addition.Failure != SpatialCandidateFailure.None)
                    { diagnostic = addition.Diagnostic; last = addition.Failure; valid = false; break; }
                    output.AddRange(addition.Segments);
                }
                if (!valid) continue;
                var result = new SpatialCandidateResult(SpatialCandidateFailure.None, output);
                var deck = EndpointNetworkClearance.Check(result, verticalClearance, out diagnostic);
                if (deck != CurveClearanceResult.Clear)
                { last = deck == CurveClearanceResult.Uncertain ? SpatialCandidateFailure.Uncertain : SpatialCandidateFailure.InvalidGeometry; continue; }
                var support = EndpointNetworkSupports.Check(result, design.BridgeRules, native[0].Incoming,
                    out diagnostic, supportRules, nativeJoinedNodes: true);
                if (support != CurveClearanceResult.Clear)
                { last = support == CurveClearanceResult.Uncertain ? SpatialCandidateFailure.Uncertain : SpatialCandidateFailure.PillarConflict; continue; }
                cancellation.ThrowIfCancellationRequested();
                return result;
            }
            return Fail(last, $"Native two-way transition: {diagnostic}");
        }

        private static SpatialCandidateResult Build(EndpointArm arm, EndpointArm proxy, RoadInfo stem,
            RoadInfo incomingRoad, RoadInfo outgoingRoad, SpatialCandidateResult candidate, bool rht,
            float maxGrade, Func<RoadInfo, float> span, float firstSpan, HashSet<string> paths,
            NativeSmoothingPose? smoothing = null, float handleScale = 1f / 3f,bool jointGrade=false)
        {
            SpatialCandidateResult Fail(string detail, SpatialCandidateFailure failure = SpatialCandidateFailure.InvalidGeometry) =>
                new SpatialCandidateResult(failure, Array.Empty<SpatialSegment>(), $"Arm {arm.Index + 1} native transition: {detail}");
            string Path(string suffix)
            {
                string name = $"Native two-way/{arm.Index}/{suffix}";
                while (!paths.Add(name)) name += "/new";
                return name;
            }
            var source = arm.Endpoints[0];
            var axis = smoothing?.Axis ?? Unit(source.OutwardTangent);
            var side = new Vec3(axis.Z, 0, -axis.X) * (rht ? 1 : -1);
            float run = Math.Max(40, 2 * stem.Width + 10);
            float sourceGrade = (float)(source.OutwardTangent.Y / H(source.OutwardTangent));
            var fork = smoothing?.Position ?? source.Position - axis * run;
            if (smoothing == null) fork.Y = source.Position.Y - .5f * sourceGrade * run;
            float forkGrade = smoothing?.Grade ?? 0;
            if(jointGrade&&smoothing==null)
            {
                // Share the whole conversion's vertical Hermite profile at its
                // real fork station instead of flattening the stem prematurely.
                var target=proxy.Endpoints.Select(p=>p.Position).Aggregate(Vec3.Zero,(a,b)=>a+b)*.5f;
                double length=H(source.Position-target),u=(length-run)/length;
                double startGrade=proxy.Endpoints.Average(p=>p.OutwardTangent.Y/H(p.OutwardTangent));
                if(length>run+10&&u>0&&u<1)
                {
                    double u2=u*u,u3=u2*u;
                    fork.Y=(float)((2*u3-3*u2+1)*target.Y+(u3-2*u2+u)*startGrade*length
                        +(-2*u3+3*u2)*source.Position.Y+(u3-u2)*sourceGrade*length);
                    forkGrade=(float)(((6*u2-6*u)*target.Y+(3*u2-4*u+1)*startGrade*length
                        +(-6*u2+6*u)*source.Position.Y+(3*u2-2*u)*sourceGrade*length)/length);
                }
            }
            var stemPlanar = smoothing?.Stem ?? Bezier3.Line(Flat(fork), Flat(source.Position));
            var profile = VerticalProfile.Apply(new[] { stemPlanar }, fork.Y, source.Position.Y, forkGrade, sourceGrade, maxGrade);
            if (profile.Failure != VerticalProfileFailure.None)
                return Fail($"native stem grade cannot be certified ({profile.Failure}).", SpatialCandidateFailure.GradeExceeded);
            var stemCurve = Bind(profile.Curves[0], fork, source.Position, forkGrade, sourceGrade);
            var stemPieces = Split(stemCurve, stem, span(stem), maxGrade, axis);
            if (stemPieces == null) return Fail("native stem cannot fit the actual prefab span, radius and course cone.");
            string stemPath = Path("stem");
            var output = stemPieces.Select(c => new SpatialSegment(arm.Index, CloverEdgeKind.Main, c,
                prefab: stem.Name, roadWidth: stem.Width, pathId: stemPath)).ToList();

            foreach (bool outward in new[] { false, true })
            {
                var port = proxy.Endpoints.Single(p => p.TravelOutward == outward);
                var road = outward ? outgoingRoad : incomingRoad;
                var boundary = candidate.Segments.Where(s => outward ? s.WorldCurve.P3 == port.Position : s.WorldCurve.P0 == port.Position).ToArray();
                if (boundary.Length != 1 || boundary[0].Prefab != port.Prefab || boundary[0].RoadWidth != port.Width)
                    return Fail("a mathematical cut must identify exactly one matching real-prefab curve boundary.");
                var travel = boundary[0].WorldCurve.Derivative(outward ? 1 : 0);
                var expected = outward ? port.OutwardTangent : -port.OutwardTangent;
                if (DirectionDot(travel, expected) < .99999)
                    return Fail("mathematical cut and existing candidate boundary tangents disagree.");
                var inwardEnd = outward ? -travel : travel;
                var inward = -axis;
                float sign = outward ? 1 : -1;
                float drop = (stem.Width - road.Width) / 2;
                var start = fork + side * (sign * drop);
                float lead = span(road) * firstSpan;
                float safeOffset = Math.Max(drop, road.Width / 2 + 2);
                var launch = start + inward * lead + side * (sign * (safeOffset - drop));
                float remaining = (float)Dot(port.Position - launch, inward);
                if (remaining < Math.Max(30, 2 * road.Width))
                    return Fail($"reserved source-road buffer is too short; needs stem {run:F0}m, fork course {lead:F0}m and at least {Math.Max(30, 2 * road.Width):F0}m after it.");
                var first = new Bezier3(Flat(start), Flat(start + inward * (lead / 3)),
                    Flat(launch - inward * (lead / 3)), Flat(launch));
                if (smoothing != null)
                {
                    float h = lead * handleScale;
                    double rootCurvature = -CurveCurvature.Signed(stemPieces[0], 0);
                    var normal = new Vec3(-inward.Z, 0, inward.X);
                    first = new Bezier3(Flat(start), Flat(start + inward * h),
                        Flat(start + inward * (lead * (1 - handleScale)) + normal * (float)(1.5 * rootCurvature * h * h)), Flat(launch));
                }
                var continuationDirection = smoothing == null ? inward : Unit(first.Derivative(1));
                var last = Bezier3.FromTangents(Flat(launch), inward, Flat(port.Position), inwardEnd,
                    (float)(H(port.Position - launch) / 3));
                if (smoothing != null)
                {
                    double targetCurvature = (outward ? -1 : 1) * CurveCurvature.Signed(boundary[0].WorldCurve, outward ? 1 : 0);
                    if (!TryCurvatureBlend(Flat(launch), continuationDirection, Flat(port.Position), inwardEnd,
                        CurveCurvature.Signed(first, 1), targetCurvature, handleScale, out last))
                        last = Bezier3.FromTangents(Flat(launch), continuationDirection, Flat(port.Position), inwardEnd,
                            (float)H(port.Position - launch) * handleScale);
                }
                float endGrade = (float)(inwardEnd.Y / H(inwardEnd));
                var heights = VerticalProfile.Apply(new[] { first, last }, fork.Y, port.Position.Y, -forkGrade, endGrade, maxGrade);
                if (heights.Failure != VerticalProfileFailure.None)
                    return Fail($"directed fan grade cannot be certified ({heights.Failure}).", SpatialCandidateFailure.GradeExceeded);
                var first3d = Bind(heights.Curves[0], start, heights.Curves[0].P3, -forkGrade,
                    (float)(heights.Curves[0].Derivative(1).Y / H(heights.Curves[0].Derivative(1))));
                var last3d = Bind(heights.Curves[1], first3d.P3, port.Position,
                    (float)(heights.Curves[1].Derivative(0).Y / H(heights.Curves[1].Derivative(0))), endGrade);
                // A strictly positive derivative projection certifies that this
                // whole fan is simple, including all subsequently sliced courses.
                if (!Course(first3d, road, span(road), maxGrade, inward))
                    return Fail("the terminal fork course cannot reach deck separation within the actual prefab span, radius or cone.");
                var rest = Split(last3d, road, span(road), maxGrade, inward);
                if (rest == null) return Fail("fan continuation has an uncertified span, grade, radius, cone or forward projection.");
                var fan = new[] { first3d }.Concat(rest).ToArray();
                if (outward) fan = fan.Reverse().Select(c => c.Reverse()).ToArray();
                string path = Path(outward ? "outgoing" : "incoming");
                for (int i = 0; i < fan.Length; i++) output.Add(new SpatialSegment(arm.Index, CloverEdgeKind.DirectRamp, fan[i],
                    !outward && i == 0 ? fork : fan[i].P0,
                    outward && i + 1 == fan.Length ? fork : fan[i].P3,
                    road.Name, road.Width, pathId: path));
            }
            return new SpatialCandidateResult(SpatialCandidateFailure.None, output);
        }

        private static Bezier3 Bind(Bezier3 curve, Vec3 start, Vec3 end, float startGrade, float endGrade)
        {
            var p1 = curve.P1; var p2 = curve.P2;
            p1.Y = start.Y + startGrade * (float)H(p1 - start);
            p2.Y = end.Y - endGrade * (float)H(end - p2);
            return new Bezier3(start, p1, p2, end);
        }

        private static IReadOnlyList<Bezier3>? Split(Bezier3 curve, RoadInfo road, float maximum, float grade, Vec3 direction)
        {
            if (VerticalProfile.CheckGrade(curve, grade) != VerticalProfileFailure.None
                || CurveCurvature.CheckMinimumRadius(curve, 2 * road.Width) != CurveCurvatureResult.Clear
                || !Forward(curve, direction)) return null;
            float minimum = Math.Max(10, road.Width);
            int first = Math.Max(1, (int)Math.Ceiling(curve.Length(128) / (maximum * .95f)));
            int last = Math.Min(128, (int)Math.Floor(RoadRetreat.HorizontalLength(curve) / minimum));
            for (int count = first; count <= last; count++)
            {
                var result = new List<Bezier3>(); var shared = curve.P0;
                for (int i = 0; i < count; i++)
                {
                    var piece = count == 1 ? curve : curve.Slice(i / (float)count, (i + 1) / (float)count);
                    var end = i + 1 == count ? curve.P3 : piece.P3;
                    result.Add(new Bezier3(shared, piece.P1, piece.P2, end)); shared = end;
                }
                if (result.All(c => Course(c, road, maximum, grade, direction))) return result;
            }
            return null;
        }

        private static bool Course(Bezier3 c, RoadInfo road, float maximum, float grade, Vec3 direction)
        {
            // The control polygon bounds the complete 3D arc length from above;
            // a sampled underestimate must never authorize an over-span course.
            double upperLength = Vec3.Distance(c.P0, c.P1) + Vec3.Distance(c.P1, c.P2) + Vec3.Distance(c.P2, c.P3);
            if (RoadRetreat.HorizontalLength(c) < Math.Max(10, road.Width) || upperLength > maximum
                || VerticalProfile.CheckGrade(c, grade) != VerticalProfileFailure.None
                || CurveCurvature.CheckMinimumRadius(c, 2 * road.Width) != CurveCurvatureResult.Clear || !Forward(c, direction)) return false;
            var chord = c.P3 - c.P0;
            double cosine = Math.Cos(CloverleafRoadDesign.MaxApproachDeflectionDegrees * Math.PI / 180);
            return new[] { c.P1 - c.P0, c.P2 - c.P1, c.P3 - c.P2 }.All(v =>
                H(v) > 0 && Dot(v, chord) >= cosine * H(v) * H(chord));
        }
        private static bool Forward(Bezier3 c, Vec3 direction) =>
            new[] { c.P1 - c.P0, c.P2 - c.P1, c.P3 - c.P2 }.All(v => Dot(v, direction) > 0);
        private static bool Matches(RoadInfo? road, SelectedEndpoint p, int lanes) => Valid(road) && road!.IsOneWay
            && road.ForwardLanes == lanes && road.BackwardLanes == 0 && road.PublicTransportLanes == 0
            && road.Width == p.Width && p.Lanes == lanes;
        private static bool Valid(SelectedEndpoint p) => p != null && Finite(p.Position) && Finite(p.OutwardTangent)
            && H(p.OutwardTangent) > 1e-5 && Finite(p.Width) && p.Width > 0 && p.Width <= 1000
            && p.Lanes > 0 && !string.IsNullOrWhiteSpace(p.Prefab);
        private static bool Valid(RoadInfo? r) => r != null && !string.IsNullOrWhiteSpace(r.Name) && Finite(r.Width)
            && r.Width > 0 && r.Width <= 1000 && Finite(r.ElevatedLength) && r.ElevatedLength >= 0 && r.ElevatedLength <= 1000
            && (r.ElevatedLength == 0 || r.ElevatedLength >= 10) && Finite(r.PillarWidth) && r.PillarWidth >= 0 && r.PillarWidth <= 1000
            && (!r.MiddlePillarSpacing.HasValue || Finite(r.MiddlePillarSpacing.Value) && r.MiddlePillarSpacing.Value >= 0);
        private static bool SamePort(SelectedEndpoint a, SelectedEndpoint b) => a.NodeIndex == b.NodeIndex
            && a.NodeVersion == b.NodeVersion && a.Position == b.Position && a.OutwardTangent == b.OutwardTangent
            && a.Prefab == b.Prefab && a.Width == b.Width && a.Lanes == b.Lanes && a.OneWay == b.OneWay && a.TravelOutward == b.TravelOutward;
        private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
        private static bool Finite(Vec3 p) => Finite(p.X) && Finite(p.Y) && Finite(p.Z);
        private static Vec3 Flat(Vec3 p) => new Vec3(p.X, 0, p.Z);
        private static double Dot(Vec3 a, Vec3 b) => (double)a.X * b.X + (double)a.Z * b.Z;
        private static double H(Vec3 p) => Math.Sqrt(Dot(p, p));
        private static Vec3 Unit(Vec3 p) => Flat(p) * (float)(1 / H(p));
        private static double DirectionDot(Vec3 a, Vec3 b) =>
            ((double)a.X * b.X + (double)a.Y * b.Y + (double)a.Z * b.Z) / a.Length() / b.Length();
    }
}
