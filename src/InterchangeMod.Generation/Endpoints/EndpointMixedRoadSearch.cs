using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using InterchangeMod.Generation.Geometry;

namespace InterchangeMod.Generation.Endpoints
{
    /// <summary>Uses mathematical road corridors only inside the solver; final cuts always belong to original real traces.</summary>
    public static class EndpointMixedRoadSearch
    {
        public static RetreatSearchResult Search(IReadOnlyList<EndpointArm> actualArms, IReadOnlyList<RoadApproachTrace> actualTraces,
            EndpointMixedRoadSetup setup,float grade,float maxRetreat,int maxCandidates=128)
            =>SearchCore(actualArms,actualTraces,setup,grade,maxRetreat,maxCandidates,null);

        public static RetreatSearchResult SearchCancellable(IReadOnlyList<EndpointArm> arms,IReadOnlyList<RoadApproachTrace> traces,
            EndpointMixedRoadSetup setup,float grade,float retreat,CancellationToken cancellation)
            =>SearchCore(arms,traces,setup,grade,retreat,128,null,cancellation);

        internal static RetreatSearchResult SearchCore(IReadOnlyList<EndpointArm> actualArms, IReadOnlyList<RoadApproachTrace> actualTraces,
            EndpointMixedRoadSetup setup, float grade, float maxRetreat, int maxCandidates = 128,Action<string>? diagnostic=null,CancellationToken cancellation=default)
        {
            cancellation.ThrowIfCancellationRequested();
            int attempts = 0, clearanceRejected = 0, supportRejected = 0;
            string detail = ""; bool coreMatched = false; var last = SpatialCandidateFailure.None;
            RetreatSearchResult Fail(RetreatSearchFailure failure) => new RetreatSearchResult(failure, attempts, null,
                Array.Empty<EndpointArm>(), Array.Empty<RoadRetreatResult>(), Array.Empty<float>(), PlanarCandidateFailure.None,
                last, clearanceRejected, detail, supportRejected, coreMatched: coreMatched);
            if (actualArms == null || actualTraces == null || setup == null || !setup.Success
                || !EndpointMixedRoadSetup.Finite(grade) || grade <= 0 || grade > .30f
                || !EndpointMixedRoadSetup.Finite(maxRetreat) || maxRetreat < 0 || maxRetreat > 1000 || maxCandidates < 1
                || EndpointConstraints.Validate(actualArms, setup.Type).Count != 0 || !SameArms(actualArms, setup.ActualArms))
            { detail = "Mixed-road search requires the unchanged real selection, valid road setup and bounded grade/retreat."; return Fail(RetreatSearchFailure.InvalidInput); }
            int budget = Math.Min(maxCandidates, 128);
            if (budget < 2) { detail = "The shared candidate budget must reserve a complete native-transition certificate."; return Fail(RetreatSearchFailure.BudgetExceeded); }
            var lookup = new Dictionary<(int, int), RoadApproachTrace>();
            foreach (var trace in actualTraces)
                if (trace == null || trace.Origin == null || RoadRetreat.Cut(trace, 0).Failure != RoadRetreatFailure.None
                    || lookup.ContainsKey((trace.Origin.NodeIndex, trace.Origin.NodeVersion)))
                { detail = "Actual source traces are invalid or duplicated."; return Fail(RetreatSearchFailure.InvalidInput); }
                else lookup.Add((trace.Origin.NodeIndex, trace.Origin.NodeVersion), trace);
            var ordered = actualArms.OrderBy(a => a.Index).ToArray();
            foreach (var port in ordered.SelectMany(a => a.Endpoints))
                if (!lookup.TryGetValue((port.NodeIndex, port.NodeVersion), out var trace) || !SamePort(port, trace.Origin))
                { detail = "An actual trace does not match its original selected endpoint snapshot."; return Fail(RetreatSearchFailure.InvalidInput); }
            if (lookup.Count != ordered.Sum(a => a.Endpoints.Count))
            { detail = "Only the selected real source traces may enter mixed-road search."; return Fail(RetreatSearchFailure.InvalidInput); }
            var design = setup.Design!; var ramp = setup.Ramp!;
            var used = setup.Catalog.Where(r => design.SupportRules.Any(s => s.Prefab == r.Name)).ToArray();
            foreach (var road in used) if (road.MaxSlope > 0) grade = Math.Min(grade, road.MaxSlope);
            bool native = ordered.Any(a => a.Endpoints.Count == 1);
            int trials = native ? 3 : 1;
            bool shareNativeRise=setup.Type==InterchangeType.Stack&&grade<.12f&&ordered.Where(a=>a.Endpoints.Count==1)
                .Any(a=>lookup[(a.Endpoints[0].NodeIndex,a.Endpoints[0].NodeVersion)].Pieces
                    .Any(p=>Math.Abs(p.Curve.P3.Y-a.Endpoints[0].Position.Y)>1));
            if(shareNativeRise)trials=5;
            for (int trial = 0; trial < trials; trial++)
            {
                cancellation.ThrowIfCancellationRequested();
                if (budget - attempts < 2) { detail = "Shared mixed-road candidate budget exhausted before the native-transition certificate."; return Fail(RetreatSearchFailure.BudgetExceeded); }
                var reserves = ordered.Select(arm =>
                {
                    if (arm.Endpoints.Count == 2) return 0f;
                    var actual = arm.Endpoints[0]; var proxy = setup.ProxyArms[arm.Index].Endpoints[0];
                    var road = setup.Catalog.Single(r => r.Name == proxy.Prefab);
                    float run = Math.Max(40, 2 * actual.Width + 10), fan = Math.Max(30, 2 * road.Width);
                    float first = Math.Max(180, (float)Math.Ceiling((run + .9f * EndpointMixedRoadSetup.Span(road) + fan + 20) / 25) * 25);
                    return first + (shareNativeRise?0:(trial == 0 ? 0 : trial == 1 ? 80 : 180));
                }).ToArray();
                var proxies = new List<RoadApproachTrace>(); int pieceId = 200000; bool enough = true;
                foreach (var arm in ordered)
                {
                    float realLimit = Math.Min(maxRetreat, arm.Endpoints.Min(p => lookup[(p.NodeIndex, p.NodeVersion)].Length) - 10);
                    float proxyLimit = realLimit - reserves[arm.Index];
                    if (proxyLimit < 0) { detail = $"Arm {arm.Index + 1} needs {reserves[arm.Index]:F0}m native conversion within its {Math.Max(0, realLimit):F0}m real retreat limit."; enough = false; break; }
                    foreach (var proxy in setup.ProxyArms[arm.Index].Endpoints)
                    {
                        var real = arm.Endpoints.Count == 1 ? arm.Endpoints[0] : arm.Endpoints.Single(p => p.TravelOutward == proxy.TravelOutward);
                        var trace = lookup[(real.NodeIndex, real.NodeVersion)];
                        var translation = proxy.Position - real.Position;
                        // Constant translation retains the exact original arc-distance coordinate.
                        float heightScale=shareNativeRise&&arm.Endpoints.Count==1?new[]{1f,.75f,.85f,.8f,.9f}[trial]:1;
                        var translated = trace.Pieces.Select(p => {
                            var curve=Translate(p.Curve,translation);
                            if(heightScale<1)curve=ShareNativeRise(curve,proxy.Position,real.OutwardTangent,heightScale);
                            return new RoadTracePiece(++pieceId,1,curve,p.Reversed,100000+pieceId,1);
                        }).ToArray();
                        // Unchanged dual sources must keep their original control points and
                        // parameterization verbatim: their final curve boundary is the real cut.
                        // The legacy search already applies the shared maxRetreat limit.
                        IReadOnlyList<RoadTracePiece> bounded = arm.Endpoints.Count == 2 ? translated
                            : Prefix(translated, Math.Min(trace.Length, proxyLimit + 10));
                        proxies.Add(new RoadApproachTrace(proxy, bounded));
                    }
                }
                if (!enough) continue;
                // Every outer trial shares the same finite budget, including the final certificate.
                int coreBudget = Math.Max(1, (budget - attempts) / (trials - trial) - 1);
                if(setup.Type==InterchangeType.Cloverleaf)
                    coreBudget=Math.Max(1,budget-attempts-12); // native seed plus its bounded appearance pass
                var result = Dispatch(setup.ProxyArms, proxies, setup, grade, maxRetreat, coreBudget,cancellation);
                cancellation.ThrowIfCancellationRequested();
                diagnostic?.Invoke($"Mixed trial {trial}, reserves [{string.Join(",",reserves)}], core {result.Failure}/{result.Attempts}: {result.ClearanceDetail}; {result.SupportDetail}");
                cancellation.ThrowIfCancellationRequested();
                attempts += result.Attempts; clearanceRejected += result.ClearanceRejected; supportRejected += result.SupportRejected;
                coreMatched |= result.CoreMatched == true || result.Spatial != null;
                last = result.LastSpatialFailure; detail = result.ClearanceDetail + (result.SupportDetail.Length > 0 ? "; " + result.SupportDetail : "");
                if (result.Failure != RetreatSearchFailure.None || result.Spatial == null)
                { if (detail.Length == 0) detail = $"Existing {setup.Type} core search rejected the bounded mathematical corridors ({result.Failure})."; continue; }
                var cuts = new List<RoadRetreatResult>(); var realCutArms = new List<EndpointArm>(); var distances = new List<float>();
                bool valid = result.Distances.Count == ordered.Length;
                foreach (var arm in ordered)
                {
                    if (!valid) break;
                    float distance = result.Distances[arm.Index] + reserves[arm.Index];
                    if (!EndpointMixedRoadSetup.Finite(distance) || distance < 0 || distance > maxRetreat)
                    { detail = "Mathematical retreat plus conversion exceeds the real user limit."; valid = false; break; }
                    var local = new List<SelectedEndpoint>();
                    foreach (var original in arm.Endpoints)
                    {
                        var source = lookup[(original.NodeIndex, original.NodeVersion)];
                        float minimumNative=result.Distances[arm.Index]+Math.Max(40,2*original.Width+10)
                            +.6f*EndpointMixedRoadSetup.Span(setup.Catalog.Single(r=>r.Name==setup.ProxyArms[arm.Index].Endpoints[0].Prefab))
                            +Math.Max(30,2*setup.ProxyArms[arm.Index].Endpoints[0].Width);
                        var cut = arm.Endpoints.Count == 1 ? (setup.Type==InterchangeType.Stack
                            ?CutNativeWindow(source,distance,minimumNative,maxRetreat):CutNativeBeyond(source, distance, maxRetreat))
                            : RoadRetreat.Cut(source, distance);
                        if (!EndpointApproachGeometry.HasRetainedRoom(cut))
                        { detail = $"Arm {arm.Index + 1} real cut {distance:F3}m lacks its native width-safe node allowance ({cut.Failure}, retained {(cut.Remainder.HasValue ? RoadRetreat.HorizontalLength(cut.Remainder.Value) : 0):F3}m, width {original.Width:F1}m)."; valid = false; break; }
                        if (!WithinRetreatLimit(cut, maxRetreat))
                        { detail = $"Arm {arm.Index + 1} actual curved-road arc exceeds the real retreat limit."; valid = false; break; }
                        if (arm.Endpoints.Count == 1) distance = cut.Distance;
                        cuts.Add(cut); local.Add(cut.Port!);
                    }
                    if (!valid) break;
                    distances.Add(distance); realCutArms.Add(new EndpointArm(arm.Index, local));
                }
                if (!valid){diagnostic?.Invoke($"Mixed trial {trial} actual cut: {detail}");continue;}
                var consumed = cuts.SelectMany(c => c.Consumed).Select(p => (p.EdgeIndex, p.EdgeVersion)).ToArray();
                if (consumed.Distinct().Count() != consumed.Length)
                { detail = "Real source cuts overlap an original edge; refusing duplicate removal."; continue; }
                var proxyCuts = result.Arms.OrderBy(a => a.Index).Select(a => ordered[a.Index].Endpoints.Count == 2 ? realCutArms[a.Index] : a).ToArray();
                var explicitCandidate = ExplicitCandidate(result.Spatial, setup);
                var connected = EndpointTwoWayTransition.ConnectBoundedCore(explicitCandidate, realCutArms, proxyCuts, setup.RightHandTraffic,
                    grade, setup.Catalog, design, setup.RequiredClearance, budget - attempts, out int transitionAttempts,jointGrade:shareNativeRise,cancellation:cancellation);
                attempts += transitionAttempts;
                last = connected.Failure;
                if(last!=SpatialCandidateFailure.None)diagnostic?.Invoke($"Mixed trial {trial} native {last}: {connected.Diagnostic}");
                if (connected.Failure != SpatialCandidateFailure.None)
                { detail = connected.Diagnostic; if (last == SpatialCandidateFailure.PillarConflict) supportRejected++; else clearanceRejected++; continue; }
                if (attempts < budget)
                {
                    connected = EndpointTwoWayTransition.ImproveBoundedCancellable(explicitCandidate, connected, realCutArms, proxyCuts,
                        actualTraces, cuts, setup.RightHandTraffic, grade, setup.Catalog, design, setup.RequiredClearance,
                        budget - attempts, out int improvementAttempts,cancellation);
                    attempts += improvementAttempts;
                    if (connected.Failure != SpatialCandidateFailure.None)
                    { last = connected.Failure; detail = connected.Diagnostic; continue; }
                }
                if(setup.Type==InterchangeType.Stack&&attempts<budget)
                {
                    connected=EndpointStackNativeFairing.Improve(connected,setup.Catalog,design,ramp,grade,setup.RequiredClearance,
                        budget-attempts,out int fairingAttempts,cancellation,diagnostic);
                    attempts+=fairingAttempts;
                }
                bool certified = true;
                foreach (var segment in connected.Segments)
                {
                    cancellation.ThrowIfCancellationRequested();
                    var road = setup.Catalog.SingleOrDefault(r => r.Name == segment.Prefab);
                    float slope = road != null && road.MaxSlope > 0 ? Math.Min(grade, road.MaxSlope) : grade;
                    var gradeCheck=VerticalProfile.CheckGrade(segment.WorldCurve,slope);
                    var radiusCheck=road==null?CurveCurvatureResult.InvalidInput:CurveCurvature.CheckMinimumRadius(segment.WorldCurve,2*road.Width);
                    if (road == null || segment.RoadWidth != road.Width || string.IsNullOrWhiteSpace(segment.PathId)
                        || gradeCheck != VerticalProfileFailure.None || radiusCheck != CurveCurvatureResult.Clear)
                    { detail = $"Real road {segment.Prefab}, path {segment.PathId}, arm {segment.SourceArm+1} failed certification: grade={gradeCheck} (limit {slope:P0}), radius={radiusCheck}, width={segment.RoadWidth}/{road?.Width}."; last = SpatialCandidateFailure.InvalidGeometry; certified = false; break; }
                }
                if (!certified){diagnostic?.Invoke(detail);continue;}
                if (EndpointNetworkClearance.Check(connected, setup.RequiredClearance, out detail) != CurveClearanceResult.Clear)
                { clearanceRejected++; continue; }
                if (EndpointNetworkSupports.Check(connected, design.BridgeRules, ramp, out detail, design.SupportRules,
                    nativeJoinedNodes: true) != CurveClearanceResult.Clear)
                { supportRejected++; last = SpatialCandidateFailure.PillarConflict; continue; }
                cancellation.ThrowIfCancellationRequested();
                return new RetreatSearchResult(RetreatSearchFailure.None, attempts, connected, realCutArms, cuts, distances,
                    PlanarCandidateFailure.None, SpatialCandidateFailure.None, clearanceRejected, "", supportRejected,
                    coreMatched: true, referenceCenter: result.ReferenceCenter, referenceFromCuts: result.ReferenceFromCuts,
                    coreFollowsCurve: result.CoreFollowsCurve);
            }
            if (detail.Length == 0) detail = "No certified native conversion fits the bounded original-road corridors.";
            return Fail(attempts >= budget ? RetreatSearchFailure.BudgetExceeded : RetreatSearchFailure.NoCandidate);
        }

        private static RetreatSearchResult Dispatch(IReadOnlyList<EndpointArm> arms, IReadOnlyList<RoadApproachTrace> traces,
            EndpointMixedRoadSetup s, float grade, float retreat, int budget,CancellationToken cancellation)
        {
            if (s.Type == InterchangeType.Cloverleaf) return RetreatCloverleafSearch.SearchCore(arms, traces, s.RightHandTraffic,
                s.MainHeight, s.CrossHeight, s.Ramp!.Width, grade, retreat, budget, s.RequiredClearance, s.Design,balancedBudget:true);
            if (s.Type == InterchangeType.Stack || s.Type == InterchangeType.Turbine) return EndpointStackFitter.SearchCore(arms, traces,
                s.Type, s.RightHandTraffic, s.MainHeight, s.CrossHeight, s.Ramp!, grade, retreat, s.Design!, s.LeftExit, budget, s.RequiredClearance,
                strictRadii:true,firstSafe:s.Type==InterchangeType.Stack,cancellation:cancellation);
            if (s.Type == InterchangeType.CompactGrid) return EndpointCompactGridSearch.Search(arms, traces, s.RightHandTraffic,
                s.MainHeight, s.LevelGap, s.Ramp!, s.GridRoads, grade, retreat, s.Design!, budget, s.RequiredClearance, s.TerrainHeight);
            if (s.Type == InterchangeType.Roundabout) return EndpointRoundaboutSearch.Search(arms, traces, s.RightHandTraffic,
                s.MainHeight, s.CrossHeight, s.Ramp!, s.Ring!, grade, retreat, s.Design!, budget, s.RequiredClearance, s.TerrainHeight, s.Racetrack);
            return EndpointThreeWaySearch.Search(arms, traces, s.Type, s.RightHandTraffic, s.MainHeight, s.CrossHeight, s.Ramp!,
                grade, retreat, s.Design!, s.TrumpetLoopForExit, s.DirectionalTwoLevel, budget, s.RequiredClearance, s.TerrainHeight);
        }

        private static SpatialCandidateResult ExplicitCandidate(SpatialCandidateResult candidate, EndpointMixedRoadSetup setup) =>
            new SpatialCandidateResult(candidate.Failure, candidate.Segments.Select(s => new SpatialSegment(s.SourceArm, s.Kind,
                s.WorldCurve, s.StartNode, s.EndNode, s.Prefab ?? setup.Ramp!.Name, s.RoadWidth ?? setup.Ramp!.Width,
                s.IsWeaving, string.IsNullOrWhiteSpace(s.PathId) ? $"Mixed core/{s.Kind}/{s.SourceArm}" : s.PathId)), candidate.Diagnostic);

        private static Bezier3 Translate(Bezier3 c, Vec3 shift) => new Bezier3(c.P0 + shift, c.P1 + shift, c.P2 + shift, c.P3 + shift);
        // A mathematical native corridor may share its climb with the real
        // conversion fan. Preserve the selected pose and horizontal arc coordinate;
        // original one-way corridors and all actual sources remain untouched.
        internal static Bezier3 ShareNativeRise(Bezier3 c,Vec3 origin,Vec3 direction,float scale)
        {
            var axis=Vec3.Normalize(new Vec3(direction.X,0,direction.Z));
            (float Height,float Tangent) At(Vec3 p,Vec3 tangent)
            {
                double d=Directions.Dot(p-origin,axis),u=Math.Max(0,Math.Min(1,d/200));
                double factor=1-(1-scale)*u*u*(3-2*u);
                double slope=d>0&&d<200?-(1-scale)*6*u*(1-u)/200:0;
                return((float)(origin.Y+(p.Y-origin.Y)*factor),
                    (float)(tangent.Y*factor+(p.Y-origin.Y)*slope*Directions.Dot(tangent,axis)));
            }
            var a=At(c.P0,c.Derivative(0));var b=At(c.P3,c.Derivative(1));
            return new Bezier3(new Vec3(c.P0.X,a.Height,c.P0.Z),new Vec3(c.P1.X,a.Height+a.Tangent/3,c.P1.Z),
                new Vec3(c.P2.X,b.Height-b.Tangent/3,c.P2.Z),new Vec3(c.P3.X,b.Height,c.P3.Z));
        }
        private static IReadOnlyList<RoadTracePiece> Prefix(IReadOnlyList<RoadTracePiece> pieces, float length)
        {
            var output = new List<RoadTracePiece>(); double remaining = length;
            foreach (var piece in pieces)
            {
                double size = RoadRetreat.HorizontalLength(piece.Curve);
                if (remaining >= size - .001) { output.Add(piece); remaining -= size; if (remaining <= .001) break; continue; }
                if (remaining < .01) break;
                float lo = 0, hi = 1;
                for (int n = 0; n < 28; n++) { float mid = (lo + hi) / 2; if (RoadRetreat.HorizontalLength(piece.Curve.Slice(0, mid)) < remaining) lo = mid; else hi = mid; }
                output.Add(new RoadTracePiece(piece.EdgeIndex, piece.EdgeVersion, piece.Curve.Slice(0, (lo + hi) / 2),
                    piece.Reversed, piece.OuterNodeIndex, piece.OuterNodeVersion));
                break;
            }
            return output;
        }
        // The virtual core boundary stays fixed. Only a native conversion's outer
        // real cut may move farther along its own source, avoiding a short retained
        // piece caused by adding the conversion reserve to a virtual cut distance.
        internal static RoadRetreatResult CutNativeBeyond(RoadApproachTrace trace, float minimum, float limit)
        {
            var initial = RoadRetreat.Cut(trace, minimum);
            if (initial.Failure != RoadRetreatFailure.None && initial.Failure != RoadRetreatFailure.ShortRemainder) return initial;
            limit = Math.Min(limit, trace.Length - 10);
            bool Safe(RoadRetreatResult cut) => cut.Distance >= minimum && cut.Distance <= limit
                && EndpointApproachGeometry.HasRetainedRoom(cut) && WithinRetreatLimit(cut, limit);
            if (Safe(initial)) return initial;
            double boundary = 0;
            foreach (var piece in trace.Pieces)
            {
                boundary += RoadRetreat.HorizontalLength(piece.Curve);
                if (boundary < minimum) continue;
                if (boundary > limit) break;
                // Prefer an interior cut on the next real edge. If there is no
                // room, the exact existing node is also a legal retained boundary.
                foreach (float distance in new[] { (float)(boundary + 10), (float)boundary })
                {
                    if (distance > limit) continue;
                    var cut = RoadRetreat.Cut(trace, distance);
                    if (Safe(cut)) return cut;
                }
            }
            return initial;
        }

        // A width-safe real tail can lie slightly inside the nominal reserve.
        // Keep the mathematical core fixed and let the real fan certificate
        // decide whether its remaining buffer is sufficient.
        internal static RoadRetreatResult CutNativeWindow(RoadApproachTrace trace,float target,float minimum,float limit)
        {
            var cut=CutNativeBeyond(trace,target,limit);
            if(EndpointApproachGeometry.HasRetainedRoom(cut)&&WithinRetreatLimit(cut,limit))return cut;
            for(float d=target-5;d>=minimum;d-=5)
            {
                var earlier=RoadRetreat.Cut(trace,d);
                if(EndpointApproachGeometry.HasRetainedRoom(earlier)&&WithinRetreatLimit(earlier,limit))return earlier;
            }
            return cut;
        }

        // Chord sampling is suitable for locating a cut, but underestimates curved
        // arc length. Certify the real modified source curves with an upper bound.
        internal static bool WithinRetreatLimit(RoadRetreatResult cut, float limit)
        {
            if (cut.Failure != RoadRetreatFailure.None || !EndpointMixedRoadSetup.Finite(limit) || limit < 0) return false;
            double total = 0;
            foreach (var c in cut.ModifiedCurves)
            {
                total += ArcUpper((c.P0.X, c.P0.Z), (c.P1.X, c.P1.Z), (c.P2.X, c.P2.Z), (c.P3.X, c.P3.Z), 12);
                if (double.IsNaN(total) || total > limit) return false;
            }
            return total <= limit;
        }

        private static double ArcUpper((double X, double Z) a, (double X, double Z) b,
            (double X, double Z) c, (double X, double Z) d, int depth)
        {
            double Distance((double X, double Z) p, (double X, double Z) q) =>
                Math.Sqrt((p.X - q.X) * (p.X - q.X) + (p.Z - q.Z) * (p.Z - q.Z));
            (double X, double Z) Mid((double X, double Z) p, (double X, double Z) q) =>
                ((p.X + q.X) * .5, (p.Z + q.Z) * .5);
            double polygon = Distance(a, b) + Distance(b, c) + Distance(c, d);
            if (depth == 0 || polygon - Distance(a, d) < .00001) return polygon;
            var ab = Mid(a, b); var bc = Mid(b, c); var cd = Mid(c, d);
            var abc = Mid(ab, bc); var bcd = Mid(bc, cd); var middle = Mid(abc, bcd);
            return ArcUpper(a, ab, abc, middle, depth - 1) + ArcUpper(middle, bcd, cd, d, depth - 1);
        }

        private static bool SamePort(SelectedEndpoint a, SelectedEndpoint b) => a.NodeIndex == b.NodeIndex && a.NodeVersion == b.NodeVersion
            && a.Position == b.Position && a.OutwardTangent == b.OutwardTangent && a.OneWay == b.OneWay && a.TravelOutward == b.TravelOutward
            && a.Width == b.Width && a.Lanes == b.Lanes && a.Prefab == b.Prefab;
        private static bool SameArms(IReadOnlyList<EndpointArm> a, IReadOnlyList<EndpointArm> b) => a.Count == b.Count
            && a.All(arm => arm != null && b.Any(other => other.Index == arm.Index && other.Endpoints.Count == arm.Endpoints.Count
                && arm.Endpoints.All(p => p != null && other.Endpoints.Any(q => SamePort(p, q)))));
    }
}
