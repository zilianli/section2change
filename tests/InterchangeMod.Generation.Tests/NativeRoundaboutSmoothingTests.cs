using System.Reflection;
using System.Text.Json;
using InterchangeMod.Generation.Endpoints;
using InterchangeMod.Generation.Geometry;
using InterchangeMod.Generation.Roads;
using Xunit.Abstractions;

namespace InterchangeMod.Generation.Tests;

public class NativeRoundaboutSmoothingTests
{
    private readonly ITestOutputHelper output;
    public NativeRoundaboutSmoothingTests(ITestOutputHelper output) { this.output = output; }

    // Positions/directions and road dimensions follow the successful 20:21 game
    // log. Continuations are controlled curves, not a full replay of that save.
    internal static (EndpointArm[] Arms, RoadApproachTrace[] Traces, RoadInfo[] Catalog) Fixture(bool mirror = false, float sourceGrade = 0)
    {
        var ports = new[] {
            new SelectedEndpoint(217733,3,new(859.58f,488.6323f,-2649.53f),new(-5.22f,0,-25.03f),true,false,20,4,"Highway Oneway - 4 lanes"),
            new SelectedEndpoint(217728,3,new(832.17f,488.6323f,-2643.81f),new(-5.17f,0,-24.76f),true,true,20,4,"Highway Oneway - 4 lanes"),
            new SelectedEndpoint(51458,31,new(756.16f,488.6323f,-2453.10f),new(-23.87f,0,6.71f),false,false,32,3,"Large Road Divided"),
            new SelectedEndpoint(217056,3,new(895.72f,488.6323f,-2299.43f),new(5.52f,0,24.31f),true,false,20,4,"Highway Oneway - 4 lanes"),
            new SelectedEndpoint(217042,3,new(923.03f,488.6323f,-2305.63f),new(5.86f,0,25.82f),true,true,20,4,"Highway Oneway - 4 lanes"),
            new SelectedEndpoint(51766,15,new(1010.30f,488.6323f,-2524.58f),new(23.52f,0,-9.46f),false,true,32,3,"Large Road Divided") };
        Vec3 Mirror(Vec3 v) => mirror ? new Vec3(-v.X, v.Y, v.Z) : v;
        var arms = new[] { new EndpointArm(0, ports.Take(2)), new EndpointArm(1, ports.Skip(2).Take(1)),
            new EndpointArm(2, ports.Skip(3).Take(2)), new EndpointArm(3, ports.Skip(5)) };
        var traces = ports.Select(p => {
            var u = Vec3.Normalize(p.OutwardTangent); var side = Directions.RightOf(u);
            float bend = p.OneWay ? 30 : -100;
            var curve = new Bezier3(p.Position, p.Position + u * 400,
                p.Position + u * 800 + side * bend, p.Position + u * 1200 + side * (2 * bend));
            Vec3 Grade(Vec3 v) => new(v.X, v.Y + sourceGrade * Directions.Dot(v - p.Position, u), v.Z);
            curve = new Bezier3(Grade(curve.P0), Grade(curve.P1), Grade(curve.P2), Grade(curve.P3));
            var pieces = Enumerable.Range(0, 16).Select(i => {
                var c = curve.Slice(i / 16f, (i + 1) / 16f);
                return new RoadTracePiece(p.NodeIndex * 20 + i, 1,
                    new Bezier3(Mirror(c.P0), Mirror(c.P1), Mirror(c.P2), Mirror(c.P3)), i % 2 == 0, p.NodeIndex * 20 + 100 + i, 1);
            }).ToArray();
            var tangent = p.OutwardTangent; tangent.Y = sourceGrade * (float)Math.Sqrt(tangent.X * tangent.X + tangent.Z * tangent.Z);
            var origin = new SelectedEndpoint(p.NodeIndex, p.NodeVersion, Mirror(p.Position), Mirror(tangent),
                p.OneWay, p.TravelOutward, p.Width, p.Lanes, p.Prefab);
            return new RoadApproachTrace(origin, pieces);
        }).ToArray();
        arms = arms.Select(a => new EndpointArm(a.Index, a.Endpoints.Select(p => traces.Single(t => t.Origin.NodeIndex == p.NodeIndex).Origin))).ToArray();
        RoadInfo R(string n, bool h, int f, int b, float w) => new(n, h, f, b, w,
            groundHeight: new HeightRange(h ? -.4f : -1.8f, 5), elevatedHeight: new HeightRange(-2, 5), maxSlope: .20f)
            { ElevatedLength = 80, PillarWidth = 4 };
        return (arms, traces, new[] { R("Highway Oneway - 4 lanes",true,4,0,20), R("Highway Oneway - 3 lanes",true,3,0,16),
            R("Highway Oneway - 1 lane",true,1,0,8), R("Large Road Divided",false,3,3,32),
            R("Small Road Oneway - 3 lanes",false,3,0,16), R("Small Road Oneway",false,2,0,16), R("Small Road Oneway - 1 lane",false,1,0,16) });
    }

    [Theory]
    [InlineData(false, false, 0)] [InlineData(true, false, 0)] [InlineData(false, true, 0)] [InlineData(false, false, .03f)]
    public void NativeCurvedConnectionsImproveWithoutMovingRealCutsOrCore(bool mirror, bool racetrack, float sourceGrade)
    {
        var f = Fixture(mirror, sourceGrade); bool rht = !mirror;
        var setup = EndpointMixedRoadSetup.Create(f.Arms, f.Catalog, InterchangeType.Roundabout, rht, 0, 0,
            478.6323f, "Highway Oneway - 1 lane", racetrack: racetrack);
        Assert.True(setup.Success, setup.Diagnostic);
        var seed = EndpointMixedRoadSearch.Search(f.Arms, f.Traces, setup, .16f, 1000);
        Assert.True(seed.Failure == RetreatSearchFailure.None, $"{seed.Failure}: {seed.ClearanceDetail}; {seed.SupportDetail}");
        var core = new SpatialCandidateResult(SpatialCandidateFailure.None, seed.Spatial!.Segments.Where(s => !Native(s)));
        var proxy = ProxyCuts(seed);
        var baseline = EndpointTwoWayTransition.ConnectBounded(core, seed.Arms, proxy, rht, .16f, f.Catalog,
            setup.Design!, setup.RequiredClearance, 3, out _);
        Assert.Equal(SpatialCandidateFailure.None, baseline.Failure);
        var before = Measure(baseline, core, seed.Cuts, f.Traces);
        var integrated = Measure(seed.Spatial!, core, seed.Cuts, f.Traces);
        Assert.True(integrated.Jump < before.Jump * .75, "Runtime mixed search must use the smoothing pass.");
        output.WriteLine($"baseline peak={before.Peak:R}, jump={before.Jump:R}");
        Export(baseline, baseline, f.Traces, seed.Cuts, mirror, racetrack, sourceGrade);
        var method = typeof(EndpointTwoWayTransition).GetMethod("ImproveBounded", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        object[] args = { core, baseline, seed.Arms, proxy, f.Traces, seed.Cuts, rht, .16f, f.Catalog,
            setup.Design!, setup.RequiredClearance, 9, 0 };
        var improved = (SpatialCandidateResult)method!.Invoke(null, args)!;
        var after = Measure(improved, core, seed.Cuts, f.Traces);
        output.WriteLine($"improved peak={after.Peak:R}, jump={after.Jump:R}");
        Export(baseline, improved, f.Traces, seed.Cuts, mirror, racetrack, sourceGrade);
        Assert.Equal(SpatialCandidateFailure.None, improved.Failure);
        Assert.InRange((int)args[^1], 1, 9);
        Assert.True(after.Jump < before.Jump * .75, $"curvature jump {before.Jump:R} → {after.Jump:R}");
        Assert.True(after.Peak <= before.Peak * 1.00001, $"peak {before.Peak:R} → {after.Peak:R}");
        Assert.All(core.Segments, s => Assert.Contains(s, improved.Segments));
        Assert.Equal(CurveClearanceResult.Clear, EndpointNetworkClearance.Check(improved, setup.RequiredClearance, out _));
        Assert.Equal(CurveClearanceResult.Clear, EndpointNetworkSupports.Check(improved, setup.Design!.BridgeRules, setup.Ramp!, out _,
            setup.Design.SupportRules, nativeJoinedNodes: true));
        Assert.All(improved.Segments, s => Assert.Equal(VerticalProfileFailure.None, VerticalProfile.CheckGrade(s.WorldCurve, .16f)));
        AssertSmoothDirections(improved, core, seed);
    }

    [Fact]
    public void InternalForkSamplingDoesNotApplyARetainedTailRule()
    {
        var origin = new SelectedEndpoint(1, 1, new Vec3(0, 10, 0), Vec3.UnitX, false, false, 32, 3, "Street");
        var trace = new RoadApproachTrace(origin, new[] { new RoadTracePiece(2, 1,
            Bezier3.Line(origin.Position, new Vec3(80, 14, 0)), false, 3, 1) });
        Assert.Equal(RoadRetreatFailure.ShortRemainder, RoadRetreat.Cut(trace, 75).Failure);
        var method = typeof(EndpointTwoWayTransition).GetMethod("SampleSource", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        object[] args = { trace, 75f, Vec3.Zero, Vec3.Zero, 0d };
        Assert.True((bool)method!.Invoke(null, args)!);
        var point = (Vec3)args[2]; var tangent = (Vec3)args[3];
        Assert.InRange(point.X, 74.999, 75.001);
        Assert.InRange(point.Y, 13.749, 13.751);
        Assert.InRange(tangent.Y / Math.Sqrt(tangent.X * tangent.X + tangent.Z * tangent.Z), .0499, .0501);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(9)] [InlineData(20)]
    public void AppearancePassIsBoundedAndCannotWorsenTheSafeSeed(int budget)
    {
        var f = Fixture(); var setup = EndpointMixedRoadSetup.Create(f.Arms, f.Catalog, InterchangeType.Roundabout, true, 0, 0,
            478.6323f, "Highway Oneway - 1 lane");
        var seed = EndpointMixedRoadSearch.Search(f.Arms, f.Traces, setup, .16f, 1000);
        Assert.Equal(RetreatSearchFailure.None, seed.Failure);
        var core = new SpatialCandidateResult(SpatialCandidateFailure.None, seed.Spatial!.Segments.Where(s => !Native(s)));
        var proxy = ProxyCuts(seed);
        var baseline = EndpointTwoWayTransition.ConnectBounded(core, seed.Arms, proxy, true, .16f, f.Catalog,
            setup.Design!, setup.RequiredClearance, 3, out _);
        var snapshot = NodePlacementSnapshot.Capture(NodePreviewSources.Bind(f.Traces, seed.Cuts), seed.Cuts);
        object[] args = { core, baseline, seed.Arms, proxy, f.Traces, seed.Cuts, true, .16f, f.Catalog,
            setup.Design!, setup.RequiredClearance, budget, 0 };
        var result = (SpatialCandidateResult)typeof(EndpointTwoWayTransition).GetMethod("ImproveBounded", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, args)!;
        Assert.InRange((int)args[^1], 0, Math.Min(9, budget));
        if (budget == 0) Assert.Same(baseline, result);
        var before = Measure(baseline, core, seed.Cuts, f.Traces); var after = Measure(result, core, seed.Cuts, f.Traces);
        Assert.True(after.Peak <= before.Peak + 1e-9 && after.Jump <= before.Jump + 1e-9);
        Assert.True(snapshot.Matches(NodePreviewSources.Bind(f.Traces, seed.Cuts), seed.Cuts));
    }

    [Fact]
    public void StraightSourceTransitionsKeepTheExistingExactSafeShape()
    {
        var f = Fixture();
        var traces = f.Traces.Select(t => {
            var p = t.Origin; var u = Vec3.Normalize(p.OutwardTangent);
            return new RoadApproachTrace(p, Enumerable.Range(0, 16).Select(i => new RoadTracePiece(p.NodeIndex * 20 + i, 1,
                Bezier3.Line(p.Position + u * (75 * i), p.Position + u * (75 * (i + 1))), false, p.NodeIndex * 20 + 100 + i, 1)));
        }).ToArray();
        var setup = EndpointMixedRoadSetup.Create(f.Arms, f.Catalog, InterchangeType.Roundabout, true, 0, 0, 478.6323f, "Highway Oneway - 1 lane");
        var seed = EndpointMixedRoadSearch.Search(f.Arms, traces, setup, .16f, 1000);
        Assert.Equal(RetreatSearchFailure.None, seed.Failure);
        var core = new SpatialCandidateResult(SpatialCandidateFailure.None, seed.Spatial!.Segments.Where(s => !Native(s)));
        var proxy = ProxyCuts(seed);
        var baseline = EndpointTwoWayTransition.ConnectBounded(core, seed.Arms, proxy, true, .16f, f.Catalog, setup.Design!, setup.RequiredClearance, 3, out _);
        object[] args = { core, baseline, seed.Arms, proxy, traces, seed.Cuts, true, .16f, f.Catalog, setup.Design!, setup.RequiredClearance, 9, 0 };
        var result = (SpatialCandidateResult)typeof(EndpointTwoWayTransition).GetMethod("ImproveBounded", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, args)!;
        Assert.Equal(0, (int)args[^1]); Assert.Same(baseline, result);
    }

    internal static void AssertSmoothDirections(SpatialCandidateResult candidate, SpatialCandidateResult core, RetreatSearchResult seed)
    {
        double Dot(Vec3 a, Vec3 b) => Vec3.Normalize(a).X * Vec3.Normalize(b).X + Vec3.Normalize(a).Y * Vec3.Normalize(b).Y + Vec3.Normalize(a).Z * Vec3.Normalize(b).Z;
        foreach (var group in candidate.Segments.Where(Native).GroupBy(s => s.PathId)) {
            var path = group.ToArray();
            for (int i = 1; i < path.Length; i++) {
                Assert.Equal(path[i-1].WorldCurve.P3, path[i].WorldCurve.P0);
                Assert.True(Dot(path[i-1].WorldCurve.Derivative(1), path[i].WorldCurve.Derivative(0)) > .99999);
            }
            if (group.Key.EndsWith("/stem", StringComparison.Ordinal)) {
                var port = seed.Arms.Single(a => a.Index == path[0].SourceArm).Endpoints.Single();
                Assert.Equal(port.Position, path[^1].WorldCurve.P3);
                Assert.True(Dot(port.OutwardTangent, path[^1].WorldCurve.Derivative(1)) > .99999);
            } else {
                bool outgoing = group.Key.EndsWith("/outgoing", StringComparison.Ordinal);
                var stem = candidate.Segments.First(s => s.SourceArm == path[0].SourceArm && Native(s) && s.PathId.EndsWith("/stem", StringComparison.Ordinal));
                Assert.True(Dot(outgoing ? path[^1].WorldCurve.Derivative(1) : -path[0].WorldCurve.Derivative(0), stem.WorldCurve.Derivative(0)) > .99999);
            }
        }
    }

    [Fact]
    public void AChangedRealSourceCannotBeSmoothedUsingOldCuts()
    {
        var f = Fixture(); var setup = EndpointMixedRoadSetup.Create(f.Arms, f.Catalog, InterchangeType.Roundabout, true, 0, 0,
            478.6323f, "Highway Oneway - 1 lane");
        var seed = EndpointMixedRoadSearch.Search(f.Arms, f.Traces, setup, .16f, 1000);
        Assert.Equal(RetreatSearchFailure.None, seed.Failure);
        var core = new SpatialCandidateResult(SpatialCandidateFailure.None, seed.Spatial!.Segments.Where(s => !Native(s)));
        var proxy = ProxyCuts(seed);
        var baseline = EndpointTwoWayTransition.ConnectBounded(core, seed.Arms, proxy, true, .16f, f.Catalog,
            setup.Design!, setup.RequiredClearance, 3, out _);
        var changed = f.Traces.ToArray(); var source = changed[0]; var pieces = source.Pieces.ToArray(); var old = pieces[0]; var c = old.Curve;
        pieces[0] = new RoadTracePiece(old.EdgeIndex, old.EdgeVersion,
            new Bezier3(c.P0, c.P1 + new Vec3(1, 0, 0), c.P2, c.P3), old.Reversed, old.OuterNodeIndex, old.OuterNodeVersion);
        changed[0] = new RoadApproachTrace(source.Origin, pieces);
        object[] args = { core, baseline, seed.Arms, proxy, changed, seed.Cuts, true, .16f, f.Catalog, setup.Design!, setup.RequiredClearance, 9, 0 };
        var result = (SpatialCandidateResult)typeof(EndpointTwoWayTransition).GetMethod("ImproveBounded", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, args)!;
        Assert.Equal(SpatialCandidateFailure.InvalidSelection, result.Failure);
        Assert.Empty(result.Segments); Assert.Equal(0, (int)args[^1]);
    }

    internal static bool Native(SpatialSegment s) => s.PathId.StartsWith("Native two-way/", StringComparison.Ordinal);
    internal static EndpointArm[] ProxyCuts(RetreatSearchResult result) => result.Arms.Select(a => {
        if (a.Endpoints.Count == 2) return a;
        return new EndpointArm(a.Index, new[] { false, true }.Select(outward => {
            var path = result.Spatial!.Segments.Where(s => s.SourceArm == a.Index && Native(s)
                && s.PathId.EndsWith(outward ? "/outgoing" : "/incoming", StringComparison.Ordinal)).ToArray();
            var c = outward ? path[0].WorldCurve : path[^1].WorldCurve;
            return new SelectedEndpoint(10000 + a.Index * 2 + (outward ? 1 : 0), 1, outward ? c.P0 : c.P3,
                outward ? c.Derivative(0) : -c.Derivative(1), true, outward, path[0].RoadWidth!.Value,
                a.Endpoints[0].Lanes, path[0].Prefab!);
        }));
    }).ToArray();

    internal static (double Peak, double Jump) Measure(SpatialCandidateResult candidate, SpatialCandidateResult core,
        IReadOnlyList<RoadRetreatResult> cuts, IReadOnlyList<RoadApproachTrace> traces)
    {
        double peak = 0, jump = 0;
        foreach (var path in candidate.Segments.Where(Native).GroupBy(s => s.PathId)) {
            var pieces = path.ToArray();
            foreach (var s in pieces) for (int i = 0; i <= 128; i++) peak = Math.Max(peak, Math.Abs(CurveCurvature.Signed(s.WorldCurve, i / 128f)));
            for (int i = 1; i < pieces.Length; i++) jump += Math.Abs(CurveCurvature.Signed(pieces[i-1].WorldCurve, 1) - CurveCurvature.Signed(pieces[i].WorldCurve, 0));
            bool stem = path.Key.EndsWith("/stem", StringComparison.Ordinal), outgoing = path.Key.EndsWith("/outgoing", StringComparison.Ordinal);
            if (stem) {
                var cut = cuts.Single(c => c.Port!.Position == pieces[^1].WorldCurve.P3);
                if (cut.Remainder.HasValue) jump += Math.Abs(CurveCurvature.Signed(pieces[^1].WorldCurve, 1) - CurveCurvature.Signed(cut.Remainder.Value, 0));
            } else {
                var port = outgoing ? pieces[0].WorldCurve.P0 : pieces[^1].WorldCurve.P3;
                var other = core.Segments.Single(s => outgoing ? s.WorldCurve.P3 == port : s.WorldCurve.P0 == port);
                jump += Math.Abs(CurveCurvature.Signed(outgoing ? pieces[0].WorldCurve : pieces[^1].WorldCurve, outgoing ? 0 : 1)
                    - CurveCurvature.Signed(other.WorldCurve, outgoing ? 1 : 0));
                var stemFirst = candidate.Segments.First(s => s.SourceArm == pieces[0].SourceArm && Native(s) && s.PathId.EndsWith("/stem", StringComparison.Ordinal));
                jump += Math.Abs(CurveCurvature.Signed(outgoing ? pieces[^1].WorldCurve : pieces[0].WorldCurve, outgoing ? 1 : 0)
                    - (outgoing ? 1 : -1) * CurveCurvature.Signed(stemFirst.WorldCurve, 0));
            }
        }
        return (peak, jump);
    }

    private static void Export(SpatialCandidateResult before, SpatialCandidateResult after, IReadOnlyList<RoadApproachTrace> traces,
        IReadOnlyList<RoadRetreatResult> cuts, bool mirror, bool racetrack, float sourceGrade)
    {
        var directory = Environment.GetEnvironmentVariable("NATIVE_ROUNDABOUT_APPEARANCE_EXPORT");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        float[] P(Vec3 p) => new[] { p.X, p.Y, p.Z };
        object C(SpatialCandidateResult c) => c.Segments.Select(s => new { s.PathId, s.SourceArm, s.Prefab, s.RoadWidth,
            curve = new[] { P(s.WorldCurve.P0), P(s.WorldCurve.P1), P(s.WorldCurve.P2), P(s.WorldCurve.P3) } });
        File.WriteAllText(Path.Combine(directory, $"native-roundabout-{mirror}-{racetrack}-{sourceGrade.ToString(System.Globalization.CultureInfo.InvariantCulture)}.json"),
            JsonSerializer.Serialize(new { before = C(before), after = C(after),
                retained = cuts.Where(c => c.Remainder.HasValue).Select(c => new[] { P(c.Remainder!.Value.P0), P(c.Remainder.Value.P1), P(c.Remainder.Value.P2), P(c.Remainder.Value.P3) }) }));
    }
}
