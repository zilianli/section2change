using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using InterchangeMod.Generation.Geometry;
using InterchangeMod.Generation.Roads;

namespace InterchangeMod.Generation.Endpoints
{
    public static partial class EndpointTwoWayTransition
    {
        private sealed class NativeSmoothingPose
        {
            internal Vec3 Position, Axis;
            internal float Grade;
            internal Bezier3 Stem;
        }

        // Optional appearance pass. The caller has already certified baseline.
        // Every generated complete network consumes its remaining shared budget.
        internal static SpatialCandidateResult ImproveBounded(SpatialCandidateResult core, SpatialCandidateResult baseline,
            IReadOnlyList<EndpointArm> actual, IReadOnlyList<EndpointArm> proxies,
            IReadOnlyList<RoadApproachTrace> traces, IReadOnlyList<RoadRetreatResult> cuts, bool rht, float grade,
            IReadOnlyList<RoadInfo> catalog, CloverleafRoadDesign design, float clearance, int budget, out int attempts)
            =>ImproveBoundedCancellable(core,baseline,actual,proxies,traces,cuts,rht,grade,catalog,design,clearance,budget,out attempts);

        internal static SpatialCandidateResult ImproveBoundedCancellable(SpatialCandidateResult core, SpatialCandidateResult baseline,
            IReadOnlyList<EndpointArm> actual, IReadOnlyList<EndpointArm> proxies,
            IReadOnlyList<RoadApproachTrace> traces, IReadOnlyList<RoadRetreatResult> cuts, bool rht, float grade,
            IReadOnlyList<RoadInfo> catalog, CloverleafRoadDesign design, float clearance, int budget, out int attempts,CancellationToken cancellation=default)
        {
            attempts = 0;
            cancellation.ThrowIfCancellationRequested();
            if (budget <= 0 || baseline.Failure != SpatialCandidateFailure.None || actual.All(a => a.Endpoints.Count == 2)) return baseline;
            IReadOnlyList<RoadApproachTrace> sources;
            try { sources = NodePreviewSources.Bind(traces, cuts); }
            catch (ArgumentException) { return new SpatialCandidateResult(SpatialCandidateFailure.InvalidSelection, Array.Empty<SpatialSegment>(), "Smoothing source snapshot changed."); }
            if (cuts.Count != actual.Sum(a => a.Endpoints.Count) || actual.SelectMany(a => a.Endpoints)
                .Any(p => !cuts.Any(c => c.Port != null && NodePlacementSnapshot.SameEndpoint(p, c.Port))))
                return new SpatialCandidateResult(SpatialCandidateFailure.InvalidSelection, Array.Empty<SpatialSegment>(), "Smoothing real cuts changed.");
            var best = baseline; var quality = NativeQuality(best, core, cuts, sources);
            var poses = new Dictionary<int, NativeSmoothingPose>();
            bool curved = false;
            foreach (var arm in actual.Where(a => a.Endpoints.Count == 1))
            {
                var p = arm.Endpoints[0]; var source = sources.Single(t => t.Origin.NodeIndex == p.NodeIndex && t.Origin.NodeVersion == p.NodeVersion);
                var cut = cuts.Single(c => c.Port!.NodeIndex == p.NodeIndex && c.Port.NodeVersion == p.NodeVersion);
                if (!SampleSource(source, cut.Distance - Math.Max(40, 2 * p.Width + 10), out var position, out var tangent, out double firstK)
                    || !SampleSource(source, cut.Distance, out _, out _, out double lastK)) return baseline;
                var axis = Unit(tangent);
                var stem = Bezier3.FromTangents(Flat(position), axis, Flat(p.Position), p.OutwardTangent,
                    (float)H(p.Position - position) / 3);
                if (TryCurvatureBlend(Flat(position), axis, Flat(p.Position), p.OutwardTangent, firstK,
                    lastK, 1f / 3f, out var matched)) stem = matched;
                curved |= Math.Abs(firstK) > 1e-4 || Math.Abs(lastK) > 1e-4
                    || Enumerable.Range(0,17).Any(i => Math.Abs(CurveCurvature.Signed(stem,i/16f)) > 1e-4);
                poses.Add(arm.Index, new NativeSmoothingPose { Position = position, Axis = axis,
                    Grade = (float)(tangent.Y / H(tangent)), Stem = stem });
            }
            if (!curved) return baseline;
            foreach (float firstSpan in new[] { .90f, .75f, .60f })
                foreach (float handle in new[] { .25f, 1f / 3f, .42f })
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (attempts >= Math.Min(9, budget)) return best;
                    attempts++;
                    var output = new List<SpatialSegment>(core.Segments);
                    var paths = new HashSet<string>(output.Select(s => s.PathId), StringComparer.Ordinal);
                    bool valid = true;
                    foreach (var arm in actual.Where(a => a.Endpoints.Count == 1))
                    {
                        var proxy = proxies.Single(a => a.Index == arm.Index);
                        var stem = catalog.Single(r => r.Name == arm.Endpoints[0].Prefab);
                        var incoming = catalog.Single(r => r.Name == proxy.Endpoints.Single(p => !p.TravelOutward).Prefab);
                        var outgoing = catalog.Single(r => r.Name == proxy.Endpoints.Single(p => p.TravelOutward).Prefab);
                        float Span(RoadInfo r) => Math.Min(r.ElevatedLength > 0 ? r.ElevatedLength : 80,
                            design.SupportRules.FirstOrDefault(s => s.Prefab == r.Name)?.MaxLength ?? 80);
                        var addition = Build(arm, proxy, stem, incoming, outgoing, core, rht, grade, Span, firstSpan, paths, poses[arm.Index], handle);
                        if (addition.Failure != SpatialCandidateFailure.None) { valid = false; break; }
                        output.AddRange(addition.Segments);
                    }
                    if (!valid) continue;
                    var candidate = new SpatialCandidateResult(SpatialCandidateFailure.None, output);
                    var next = NativeQuality(candidate, core, cuts, sources);
                    if (!FiniteQuality(next) || next.PeakCurvature > quality.PeakCurvature + 1e-9
                        || next.CurvatureJump >= quality.CurvatureJump || next.Score >= quality.Score) continue;
                    if (EndpointNetworkClearance.Check(candidate, clearance, out _) != CurveClearanceResult.Clear
                        || EndpointNetworkSupports.Check(candidate, design.BridgeRules, catalog.Single(r => r.Name == output[0].Prefab),
                            out _, design.SupportRules, nativeJoinedNodes: true) != CurveClearanceResult.Clear) continue;
                    best = candidate; quality = next;
                }
            return best;
        }

        // The inner fork is rebuilt, not retained: sample geometry without the
        // actual removal cut's ten-metre remainder restriction.
        private static bool SampleSource(RoadApproachTrace source,float distance,out Vec3 position,out Vec3 tangent,out double curvature)
        {
            position=default;tangent=default;curvature=0;
            if (!Finite(distance)||distance<0) return false;
            double remaining=distance;
            foreach(var piece in source.Pieces)
            {
                double length=RoadRetreat.HorizontalLength(piece.Curve);
                if(remaining>length+.001){remaining-=length;continue;}
                float t=1;
                if(remaining<length-.001)
                {
                    float low=0,high=1;
                    for(int i=0;i<28;i++){float mid=(low+high)/2;
                        if(RoadRetreat.HorizontalLength(piece.Curve.Slice(0,mid))<remaining)low=mid;else high=mid;}
                    t=(low+high)/2;
                }
                position=t==1?piece.Curve.P3:piece.Curve.Evaluate(t);tangent=piece.Curve.Derivative(t);
                curvature=CurveCurvature.Signed(piece.Curve,t);
                return Finite(position)&&Finite(tangent)&&H(tangent)>1e-5&&!double.IsNaN(curvature)&&!double.IsInfinity(curvature);
            }
            return false;
        }

        private static bool FiniteQuality(EndpointApproachQuality q) => !double.IsNaN(q.Score) && !double.IsInfinity(q.Score);
        private static bool IsNative(SpatialSegment s) => s.PathId.StartsWith("Native two-way/", StringComparison.Ordinal);
        private static EndpointApproachQuality NativeQuality(SpatialCandidateResult candidate, SpatialCandidateResult core,
            IReadOnlyList<RoadRetreatResult> cuts, IReadOnlyList<RoadApproachTrace> sources)
        {
            double peak = 0, jump = 0, energy = 0, length = 0;
            var native = candidate.Segments.Where(IsNative).ToArray();
            foreach (var group in native.GroupBy(s => s.PathId))
            {
                var path = group.ToArray();
                foreach (var s in path)
                {
                    var previous = s.WorldCurve.P0; double last = CurveCurvature.Signed(s.WorldCurve, 0);
                    for (int i = 0; i <= 128; i++)
                    {
                        var p = s.WorldCurve.Evaluate(i / 128f); double k = CurveCurvature.Signed(s.WorldCurve, i / 128f);
                        double ds = Vec3.Distance(previous, p); peak = Math.Max(peak, Math.Abs(k));
                        energy += .5 * (last * last + k * k) * ds; length += ds; last = k; previous = p;
                    }
                }
                for (int i = 1; i < path.Length; i++) jump += Math.Abs(CurveCurvature.Signed(path[i-1].WorldCurve, 1) - CurveCurvature.Signed(path[i].WorldCurve, 0));
                bool stem = group.Key.EndsWith("/stem", StringComparison.Ordinal), outgoing = group.Key.EndsWith("/outgoing", StringComparison.Ordinal);
                if (stem)
                {
                    var cut = cuts.Single(c => c.Port!.Position == path[path.Length-1].WorldCurve.P3);
                    var trace = sources.Single(t => t.Origin.NodeIndex == cut.Port!.NodeIndex && t.Origin.NodeVersion == cut.Port.NodeVersion);
                    var retained = cut.Remainder ?? (cut.Consumed.Count < trace.Pieces.Count ? (Bezier3?)trace.Pieces[cut.Consumed.Count].Curve : null);
                    if (retained.HasValue) jump += Math.Abs(CurveCurvature.Signed(path[path.Length-1].WorldCurve, 1) - CurveCurvature.Signed(retained.Value, 0));
                }
                else
                {
                    var port = outgoing ? path[0].WorldCurve.P0 : path[path.Length-1].WorldCurve.P3;
                    var other = core.Segments.Single(s => outgoing ? s.WorldCurve.P3 == port : s.WorldCurve.P0 == port);
                    jump += Math.Abs(CurveCurvature.Signed(outgoing ? path[0].WorldCurve : path[path.Length-1].WorldCurve, outgoing ? 0 : 1)
                        - CurveCurvature.Signed(other.WorldCurve, outgoing ? 1 : 0));
                    var stemStart = native.First(s => s.SourceArm == path[0].SourceArm && s.PathId.EndsWith("/stem", StringComparison.Ordinal));
                    jump += Math.Abs(CurveCurvature.Signed(outgoing ? path[path.Length-1].WorldCurve : path[0].WorldCurve, outgoing ? 1 : 0)
                        - (outgoing ? 1 : -1) * CurveCurvature.Signed(stemStart.WorldCurve, 0));
                }
            }
            return new EndpointApproachQuality(peak, jump, energy, length, native);
        }

        private static bool TryCurvatureBlend(Vec3 start, Vec3 startDirection, Vec3 end, Vec3 endDirection,
            double startCurvature, double endCurvature, float initial, out Bezier3 curve)
        {
            var a = Unit(startDirection); var b = Unit(endDirection); var delta = end - start;
            double Cross(Vec3 p, Vec3 q) => (double)p.X * q.Z - (double)p.Z * q.X;
            double size = H(delta), x = size * initial, y = x, c = Cross(a, b), d0 = Cross(a, delta), d1 = Cross(b, delta);
            curve = default;
            for (int i = 0; i < 24; i++)
            {
                double f = 1.5 * startCurvature * x * x + c * y - d0;
                double g = 1.5 * endCurvature * y * y + c * x + d1;
                if (Math.Abs(f) + Math.Abs(g) < 1e-5)
                {
                    if (x < 1 || y < 1 || x > .8 * size || y > .8 * size) return false;
                    curve = new Bezier3(start, start + a * (float)x, end - b * (float)y, end); return true;
                }
                double j0 = 3 * startCurvature * x, j1 = 3 * endCurvature * y, determinant = j0 * j1 - c * c;
                if (Math.Abs(determinant) < 1e-12) return false;
                double dx = (j1 * f - c * g) / determinant, dy = (-c * f + j0 * g) / determinant;
                double step = 1;
                while (step > 1.0 / 128 && (x - step * dx < 1 || y - step * dy < 1
                    || x - step * dx > .8 * size || y - step * dy > .8 * size)) step *= .5;
                x -= step * dx; y -= step * dy;
                if (double.IsNaN(x) || double.IsNaN(y)) return false;
            }
            return false;
        }
    }
}
