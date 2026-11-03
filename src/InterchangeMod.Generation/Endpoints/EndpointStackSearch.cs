using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using InterchangeMod.Generation.Geometry;
using InterchangeMod.Generation.Roads;

namespace InterchangeMod.Generation.Endpoints
{
    public static class EndpointStackSearch
    {
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static Vec3 Flat(Vec3 value) => new Vec3(value.X, 0, value.Z);
        private static double Horizontal(Vec3 value) => Math.Sqrt((double)value.X * value.X + (double)value.Z * value.Z);
        private static double Dot(Vec3 a, Vec3 b) => (double)a.X * b.X + (double)a.Z * b.Z;

        /// <summary>
        /// Tries independently bounded cuts on the actual original roads against each
        /// solved core. Only complete networks consume the shared candidate budget.
        /// </summary>
        public static RetreatSearchResult Search(IReadOnlyList<EndpointArm> arms, IReadOnlyList<RoadApproachTrace> traces,
            InterchangeType type,bool rightHandTraffic,float mainHeight,float crossHeight,RoadInfo ramp,float maxGrade,
            float maxRetreat,CloverleafRoadDesign design,bool leftExit=false,int maxCandidates=128,float verticalClearance=8.5f)
            =>SearchCore(arms,traces,type,rightHandTraffic,mainHeight,crossHeight,ramp,maxGrade,maxRetreat,design,leftExit,maxCandidates,verticalClearance,false);

        internal static RetreatSearchResult SearchCore(IReadOnlyList<EndpointArm> arms, IReadOnlyList<RoadApproachTrace> traces,
            InterchangeType type, bool rightHandTraffic, float mainHeight, float crossHeight, RoadInfo ramp,
            float maxGrade, float maxRetreat, CloverleafRoadDesign design, bool leftExit = false,
            int maxCandidates = 128, float verticalClearance = 8.5f,bool strictRadii=false,CancellationToken cancellation=default)
        {
            cancellation.ThrowIfCancellationRequested();
            int attempts = 0, clearanceRejected = 0,supportRejected=0;
            bool hadSafeCore = false;
            string detail = "";
            var lastSpatial = SpatialCandidateFailure.None;
            RetreatSearchResult Fail(RetreatSearchFailure failure) => new RetreatSearchResult(failure, attempts, null,
                Array.Empty<EndpointArm>(), Array.Empty<RoadRetreatResult>(), Array.Empty<float>(),
                PlanarCandidateFailure.None, lastSpatial, clearanceRejected, detail,supportRejected,coreMatched:hadSafeCore);
            if ((type != InterchangeType.Stack && type != InterchangeType.Turbine) || arms == null || traces == null
                || EndpointConstraints.Validate(arms, type).Count != 0 || arms.Any(a => a.Endpoints.Count != 2)
                || !Finite(mainHeight) || !Finite(crossHeight) || ramp == null || !ramp.IsOneWay
                || ramp.LanesPerDirection < 1 || !Finite(ramp.Width) || ramp.Width <= 0 || string.IsNullOrWhiteSpace(ramp.Name)
                || !Finite(maxGrade) || maxGrade <= 0 || maxGrade > .30f || !Finite(maxRetreat) || maxRetreat < 0
                || maxRetreat > 1000 || maxCandidates <= 0 || !Finite(verticalClearance) || verticalClearance <= 0 || design == null)
                return Fail(RetreatSearchFailure.InvalidInput);

            var lookup = new Dictionary<(int, int), RoadApproachTrace>();
            foreach (var trace in traces)
            {
                if (trace == null || trace.Origin == null || RoadRetreat.Cut(trace, 0).Failure != RoadRetreatFailure.None
                    || lookup.ContainsKey((trace.Origin.NodeIndex, trace.Origin.NodeVersion)))
                    return Fail(RetreatSearchFailure.InvalidInput);
                lookup.Add((trace.Origin.NodeIndex, trace.Origin.NodeVersion), trace);
            }
            foreach (var port in arms.SelectMany(a => a.Endpoints))
            {
                if (!lookup.TryGetValue((port.NodeIndex, port.NodeVersion), out var trace) || !SameSnapshot(port, trace.Origin))
                    return Fail(RetreatSearchFailure.InvalidInput);
            }

            var ordered = arms.OrderBy(a => a.Index).ToArray();
            var limits = ordered.Select(a => Math.Max(0, Math.Min(maxRetreat,
                a.Endpoints.Min(p => lookup[(p.NodeIndex, p.NodeVersion)].Length) - 10))).ToArray();
            // Each arm has at most 41 grid/end samples (the user limit is <=1000m).
            // Actual-road cutting is independent of the core and is cached once.
            var samples = ordered.Select(arm => CutSamples(arm, lookup, limits[arm.Index])).ToArray();
            int candidateBudget = Math.Min(maxCandidates, 128);
            var searches = new List<CoreFrontier>();

            RetreatSearchResult? TryComplete(CoreFrontier search)
            {
                cancellation.ThrowIfCancellationRequested();
                var vector = search.Take();
                attempts++;
                var cutArms = vector.Select(sample => sample.Arm!).ToArray();
                var candidate = Attach(search.Core, cutArms, maxGrade);
                if(strictRadii&&candidate.Failure==SpatialCandidateFailure.None
                    &&candidate.Segments.Any(s=>CurveCurvature.CheckMinimumRadius(s.WorldCurve,2*(s.RoadWidth??ramp.Width))!=CurveCurvatureResult.Clear))
                {lastSpatial=SpatialCandidateFailure.InvalidGeometry;detail="A complete approach exceeds its actual minimum turning radius.";return null;}
                lastSpatial = candidate.Failure;
                if (candidate.Failure != SpatialCandidateFailure.None) detail = candidate.Diagnostic;
                else if (EndpointNetworkClearance.Check(candidate, verticalClearance, out var conflict) != CurveClearanceResult.Clear)
                { clearanceRejected++; detail = conflict; }
                else if (EndpointNetworkSupports.Check(candidate, design.BridgeRules, ramp, out var support, design.SupportRules) != CurveClearanceResult.Clear)
                { supportRejected++; lastSpatial = SpatialCandidateFailure.PillarConflict; detail = support; }
                else
                {
                    var cuts=vector.SelectMany(sample=>sample.Cuts).ToArray();
                    return new RetreatSearchResult(RetreatSearchFailure.None, attempts, candidate, cutArms,
                        cuts, vector.Select(sample => sample.Distance),
                        PlanarCandidateFailure.None, SpatialCandidateFailure.None, clearanceRejected, "", supportRejected,coreMatched:true,
                        quality:EndpointApproachQuality.Measure(search.Core,candidate,cuts,traces),referenceCenter:search.Core.ReferenceCenter);
                }
                return null;
            }

            // Solve every core only once, lazily. Give each safe core its best local
            // vector before spending the remaining budget on alternative combinations.
            foreach (var core in EndpointStackCore.Candidates(arms, type, rightHandTraffic, mainHeight, crossHeight,
                ramp, maxGrade, design, leftExit,cancellation))
            {
                if (!core.Success)
                {
                    if (!hadSafeCore) { detail = core.Diagnostic; lastSpatial = SpatialCandidateFailure.InvalidGeometry; }
                    continue;
                }
                if(strictRadii&&core.Segments.Any(s=>CurveCurvature.CheckMinimumRadius(s.WorldCurve,
                    2*(s.RoadWidth??ramp.Width))!=CurveCurvatureResult.Clear))
                {detail="The core exceeds an actual road's minimum turning radius.";continue;}
                hadSafeCore = true;
                // Coincident reference poses (common on straight roads) must not
                // register a second frontier and spend the complete-network budget twice.
                if(searches.Any(s=>SameCore(s.Core,core)))continue;
                var windows = new List<CutSample>[4];
                var unavailable = new List<string>();
                foreach (var arm in ordered)
                {
                    cancellation.ThrowIfCancellationRequested();
                    var window = windows[arm.Index] = new List<CutSample>();
                    string rejected = "";
                    foreach (var sample in samples[arm.Index])
                    {
                        if (sample.Arm == null) { rejected = sample.Diagnostic; continue; }
                        bool valid = true;
                        foreach (var port in sample.Arm.Endpoints)
                        {
                            var corePort = core.Ports.Single(p => p.Arm == arm.Index && p.TravelOutward == port.TravelOutward);
                            int source = port.TravelOutward ? (arm.Index + 2) % 4 : arm.Index;
                            string path = core.Segments.First(s => s.Kind == CloverEdgeKind.Main && s.SourceArm == source).PathId;
                            if (port.Width != corePort.Width || port.Prefab != corePort.Prefab)
                            {
                                rejected = "The cut road metadata does not match the core port.";
                                valid = false; break;
                            }
                            var approach = Approach(port, corePort, source, path, maxGrade);
                            if (approach.Failure == SpatialCandidateFailure.None)
                            {
                                if(!strictRadii||approach.Segments.All(s=>CurveCurvature.CheckMinimumRadius(s.WorldCurve,
                                    2*(s.RoadWidth??ramp.Width))==CurveCurvatureResult.Clear))continue;
                                rejected="The approach exceeds its actual minimum turning radius.";
                                lastSpatial=SpatialCandidateFailure.InvalidGeometry;valid=false;break;
                            }
                            rejected = approach.Diagnostic; lastSpatial = approach.Failure;
                            valid = false; break;
                        }
                        if (valid) window.Add(sample);
                    }
                    if (window.Count == 0)
                        unavailable.Add($"Arm {arm.Index + 1} has no feasible approach window within {limits[arm.Index]:F2}m ({samples[arm.Index].Count} local cuts): {rejected}");
                }
                if (unavailable.Count != 0) { detail = string.Join("; ", unavailable); continue; }
                var search = new CoreFrontier(core, windows);
                searches.Add(search);
                var result = TryComplete(search);
                if (result != null) return result;
                if (attempts >= candidateBudget) return Fail(RetreatSearchFailure.BudgetExceeded);
            }

            // A bounded frontier avoids constructing the Cartesian product. Round
            // robin keeps one core from consuming every complete-network check.
            while (searches.Any(search => search.HasPending))
                foreach (var search in searches)
                {
                    if (!search.HasPending) continue;
                    var result = TryComplete(search);
                    if (result != null) return result;
                    if (attempts >= candidateBudget) return Fail(RetreatSearchFailure.BudgetExceeded);
                }
            return Fail(RetreatSearchFailure.NoCandidate);
        }

        private static bool SameCore(EndpointStackCoreResult a,EndpointStackCoreResult b)
        {
            if(a.Segments.Count!=b.Segments.Count)return false;
            for(int i=0;i<a.Segments.Count;i++)
            {
                var x=a.Segments[i];var y=b.Segments[i];
                if(x.SourceArm!=y.SourceArm||x.Kind!=y.Kind||x.PathId!=y.PathId||x.Prefab!=y.Prefab
                    ||x.RoadWidth!=y.RoadWidth||!x.WorldCurve.Equals(y.WorldCurve)
                    ||x.StartNode!=y.StartNode||x.EndNode!=y.EndNode)return false;
            }
            return true;
        }

        private sealed class CutSample
        {
            public CutSample(float distance, EndpointArm? arm, IReadOnlyList<RoadRetreatResult> cuts, string diagnostic)
            { Distance = distance; Arm = arm; Cuts = cuts; Diagnostic = diagnostic; }
            public float Distance { get; }
            public EndpointArm? Arm { get; }
            public IReadOnlyList<RoadRetreatResult> Cuts { get; }
            public string Diagnostic { get; }
        }

        private static IReadOnlyList<CutSample> CutSamples(EndpointArm arm,
            IReadOnlyDictionary<(int, int), RoadApproachTrace> lookup, float limit)
        {
            var output = new List<CutSample>();
            for (int step = 0; step <= 40; step++)
            {
                float distance = Math.Min(limit, step * 25);
                var cuts = new List<RoadRetreatResult>();
                string diagnostic = "";
                foreach (var port in arm.Endpoints)
                {
                    var cut = RoadRetreat.Cut(lookup[(port.NodeIndex, port.NodeVersion)], distance);
                    if (cut.Failure != RoadRetreatFailure.None || cut.Port == null)
                    { diagnostic = $"Cannot cut the original road at {distance:F2}m: {cut.Failure}."; break; }
                    if(!EndpointApproachGeometry.HasRetainedRoom(cut))
                    {diagnostic=$"The retained road tail is shorter than its {Math.Max(10,port.Width):F0}m node allowance.";break;}
                    cuts.Add(cut);
                }
                output.Add(new CutSample(distance, cuts.Count == 2 ? new EndpointArm(arm.Index,
                    cuts.Select(cut => cut.Port!)) : null, cuts.ToArray(), diagnostic));
                if (distance >= limit) break;
            }
            return output;
        }

        private sealed class CoreFrontier
        {
            private readonly List<CutSample>[] windows;
            private readonly List<int[]> pending = new List<int[]>();
            private readonly HashSet<(int, int, int, int)> seen = new HashSet<(int, int, int, int)>();
            public CoreFrontier(EndpointStackCoreResult core, List<CutSample>[] windows)
            { Core = core; this.windows = windows; Add(new int[4]); }
            public EndpointStackCoreResult Core { get; }
            public bool HasPending => pending.Count != 0;
            public CutSample[] Take()
            {
                pending.Sort(Compare);
                var indices = pending[0]; pending.RemoveAt(0);
                for (int arm = 0; arm < 4; arm++)
                {
                    if (indices[arm] + 1 >= windows[arm].Count) continue;
                    var next = (int[])indices.Clone(); next[arm]++; Add(next);
                }
                return Enumerable.Range(0, 4).Select(arm => windows[arm][indices[arm]]).ToArray();
            }
            private void Add(int[] indices)
            { if (seen.Add((indices[0], indices[1], indices[2], indices[3]))) pending.Add(indices); }
            private int Compare(int[] first, int[] second)
            {
                float firstTotal = 0, secondTotal = 0, firstMaximum = 0, secondMaximum = 0;
                for (int arm = 0; arm < 4; arm++)
                {
                    float a = windows[arm][first[arm]].Distance, b = windows[arm][second[arm]].Distance;
                    firstTotal += a; secondTotal += b;
                    firstMaximum = Math.Max(firstMaximum, a); secondMaximum = Math.Max(secondMaximum, b);
                }
                int comparison = firstTotal.CompareTo(secondTotal);
                if (comparison != 0) return comparison;
                comparison = firstMaximum.CompareTo(secondMaximum);
                if (comparison != 0) return comparison;
                for (int arm = 0; arm < 4; arm++)
                {
                    comparison = first[arm].CompareTo(second[arm]);
                    if (comparison != 0) return comparison;
                }
                return 0;
            }
        }

        private static bool SameSnapshot(SelectedEndpoint a, SelectedEndpoint b) =>
            a.NodeIndex == b.NodeIndex && a.NodeVersion == b.NodeVersion && a.Position == b.Position
            && a.OutwardTangent == b.OutwardTangent && a.Prefab == b.Prefab && a.Width == b.Width
            && a.Lanes == b.Lanes && a.OneWay == b.OneWay && a.TravelOutward == b.TravelOutward;

        private static SpatialCandidateResult Attach(EndpointStackCoreResult core, IReadOnlyList<EndpointArm> arms, float maxGrade)
            =>AttachWithHandle(core,arms,maxGrade,0);

        internal static SpatialCandidateResult AttachWithHandle(EndpointStackCoreResult core,IReadOnlyList<EndpointArm> arms,float maxGrade,float handleFraction)
            =>EndpointCoreAttachment.Attach(core,arms,maxGrade,handleFraction);

        private static SpatialCandidateResult Approach(SelectedEndpoint road, EndpointCorePort core, int source,
            string pathId, float maxGrade)
            =>BuildApproach(road,core,source,pathId,maxGrade,0);

        internal static SpatialCandidateResult BuildApproach(SelectedEndpoint road,EndpointCorePort core,int source,
            string pathId,float maxGrade,float handleFraction)
        {
            SpatialCandidateResult Fail(SpatialCandidateFailure failure, string reason) =>
                new SpatialCandidateResult(failure, Array.Empty<SpatialSegment>(), $"Arm {core.Arm + 1} {(core.TravelOutward ? "outgoing" : "incoming")} approach: {reason}");
            bool outward = road.TravelOutward;
            Vec3 start = outward ? core.Position : road.Position, end = outward ? road.Position : core.Position;
            Vec3 startDirection = outward ? core.Tangent : -road.OutwardTangent;
            Vec3 endDirection = outward ? road.OutwardTangent : core.Tangent;
            Vec3 chord = Flat(end - start);
            double length = Horizontal(chord), minimum = Math.Max(30, road.Width * 2);
            if (length < minimum)
                return Fail(SpatialCandidateFailure.InvalidGeometry, $"needs at least {minimum:F0}m of forward connection space.");
            double cosine = Math.Cos(CloverleafRoadDesign.MaxApproachDeflectionDegrees * Math.PI / 180);
            // A curved approach can have a large overall chord deflection while every
            // individual course turns gently. Reject genuinely backwards connections
            // here; certify the 20-degree cone on every emitted course below.
            if (Dot(chord, startDirection) <= 0 || Dot(chord, endDirection) <= 0)
                return Fail(SpatialCandidateFailure.InvalidGeometry, "connection direction points backwards.");
            bool lateral=Dot(chord,startDirection)<cosine*length*Horizontal(startDirection)
                || Dot(chord,endDirection)<cosine*length*Horizontal(endDirection);
            // Long handles in a lateral S connection overshoot its chord heading and
            // pinch parallel carriageways together. Use a shorter, still tangent-bound
            // curve here; the complete two-carriageway deck check remains mandatory.
            var planar = Bezier3.FromTangents(Flat(start), startDirection, Flat(end), endDirection,
                (float)(length * (handleFraction>0?handleFraction:lateral ? .2 : 1.0/3)));
            // Both the chord and core axis have positive derivative projections.
            // Core-axis monotonicity keeps the approach outside the core end plane,
            // preserving the full main path's same-PathId non-self-overlap proof.
            if (Dot(planar.P1 - planar.P0, chord) <= 0 || Dot(planar.P2 - planar.P1, chord) <= 0
                || Dot(planar.P3 - planar.P2, chord) <= 0
                || Dot(planar.P1-planar.P0,core.Tangent)<=0 || Dot(planar.P2-planar.P1,core.Tangent)<=0
                || Dot(planar.P3-planar.P2,core.Tangent)<=0)
                return Fail(SpatialCandidateFailure.InvalidGeometry, "connection has no strictly forward horizontal projection.");
            float grade = (float)(road.OutwardTangent.Y / Horizontal(road.OutwardTangent));
            var profile = VerticalProfile.Apply(new[] { planar }, start.Y, end.Y, outward ? 0 : -grade, outward ? grade : 0, maxGrade);
            if (profile.Failure != VerticalProfileFailure.None)
                return Fail(profile.Failure == VerticalProfileFailure.GradeExceeded || profile.Failure == VerticalProfileFailure.InsufficientLength
                    ? SpatialCandidateFailure.GradeExceeded : profile.Failure == VerticalProfileFailure.Uncertain
                        ? SpatialCandidateFailure.Uncertain : SpatialCandidateFailure.InvalidGeometry, $"vertical profile failed: {profile.Failure}.");
            var c = profile.Curves[0];
            // Hermite's final Y evaluation can differ by a float ULP in high world
            // coordinates. Bind both boundary points to the actual snapshots verbatim.
            c = new Bezier3(start, c.P1, c.P2, end);
            var strict = VerticalProfile.CheckGrade(c, maxGrade);
            if (strict != VerticalProfileFailure.None)
                return Fail(SpatialCandidateFailure.GradeExceeded, $"exact endpoint profile failed strict grade certification: {strict}.");
            var pieces=EndpointApproachGeometry.SplitCourses(c,Math.Max(10,road.Width),maxGrade,core.Tangent);
            if(pieces==null)return Fail(SpatialCandidateFailure.InvalidGeometry,"connection has a short, over-budget or unproven course.");
            return new SpatialCandidateResult(SpatialCandidateFailure.None, pieces.Select(piece =>
                new SpatialSegment(source, CloverEdgeKind.Main, piece, prefab: road.Prefab, roadWidth: road.Width, pathId: pathId)));
        }
    }
}
