using InterchangeMod.Generation;
using InterchangeMod.Generation.Endpoints;
using InterchangeMod.Generation.Geometry;

namespace InterchangeMod.Generation.Tests;

public class NodePlacementContractTests
{
    private static RoadApproachTrace Trace(int version = 1, bool reversed = false, int outer = 3, float bend = 0,
        string prefab = "road", int source = 1)
    {
        var p = new SelectedEndpoint(source, 1, Vec3.Zero, new Vec3(1, 0, 0), true, false, 12, 2, prefab);
        return new RoadApproachTrace(p, new[] {
            new RoadTracePiece(2, version, new Bezier3(Vec3.Zero, new Vec3(33, bend, 0), new Vec3(66, 0, 0), new Vec3(100, 0, 0)), reversed, outer, 1),
            new RoadTracePiece(4, 1, Bezier3.Line(new Vec3(100,0,0),new Vec3(200,0,0)),false,5,1) });
    }

    [Fact]
    public void SnapshotAcceptsEquivalentRereadAndRecut()
    {
        var trace = Trace();
        var snapshot = NodePlacementSnapshot.Capture(new[] { trace }, new[] { RoadRetreat.Cut(trace, 50) });
        var fresh = Trace();
        Assert.True(snapshot.Matches(new[] { fresh }, new[] { RoadRetreat.Cut(fresh, 50) }));
    }

    [Theory]
    [InlineData(2, false, 3, 0, "road", 1)]
    [InlineData(1, true, 3, 0, "road", 1)]
    [InlineData(1, false, 8, 0, "road", 1)]
    [InlineData(1, false, 3, .001f, "road", 1)]
    [InlineData(1, false, 3, 0, "other", 1)]
    [InlineData(1, false, 3, 0, "road", 9)]
    public void SnapshotRejectsChangedSourceIdentityCurveOrientationOrFarNode(int version, bool reverse, int outer, float bend, string prefab, int source)
    {
        var trace = Trace();
        var snapshot = NodePlacementSnapshot.Capture(new[] { trace }, new[] { RoadRetreat.Cut(trace, 50) });
        var fresh = Trace(version, reverse, outer, bend, prefab, source);
        Assert.False(snapshot.Matches(new[] { fresh }, new[] { RoadRetreat.Cut(fresh, 50) }));
    }

    [Fact]
    public void SnapshotIncludesUnconsumedCorridorAndExactCutDistance()
    {
        var trace = Trace();
        var snapshot = NodePlacementSnapshot.Capture(new[] { trace }, new[] { RoadRetreat.Cut(trace, 50) });
        var changedFarPiece = new RoadApproachTrace(trace.Origin, new[] { trace.Pieces[0],
            new RoadTracePiece(4, 2, trace.Pieces[1].Curve, false, 5, 1) });
        Assert.False(snapshot.Matches(new[] { changedFarPiece }, new[] { RoadRetreat.Cut(changedFarPiece, 50) }));
        Assert.False(snapshot.Matches(new[] { trace }, new[] { RoadRetreat.Cut(trace, 50.01f) }));
    }

    [Fact]
    public void SnapshotIsFrozenAgainstCallerListMutationAndRejectsMalformedCuts()
    {
        var trace = Trace();
        var traces = new List<RoadApproachTrace> { trace };
        var cuts = new List<RoadRetreatResult> { RoadRetreat.Cut(trace, 50) };
        var snapshot = NodePlacementSnapshot.Capture(traces, cuts);
        traces.Clear(); cuts.Clear();
        Assert.True(snapshot.Matches(new[] { trace }, new[] { RoadRetreat.Cut(trace, 50) }));
        Assert.Throws<ArgumentException>(() => NodePlacementSnapshot.Capture(new[] { trace }, new[] { RoadRetreat.Cut(trace, 500) }));
    }

    [Fact]
    public void SnapshotRejectsForgedBindingTailAndSyntheticPortUsedAsOriginalSource()
    {
        var trace = Trace();
        var cut = RoadRetreat.Cut(trace, 50);
        var snapshot = NodePlacementSnapshot.Capture(new[] { trace }, new[] { cut });
        var wrongBinding = new RoadRetreatResult(cut.Failure, cut.Port, cut.Distance, cut.Consumed, cut.ModifiedCurves, cut.Remainder, 99, 1);
        var wrongTail = new RoadRetreatResult(cut.Failure, cut.Port, cut.Distance, cut.Consumed, cut.ModifiedCurves,
            Bezier3.Line(cut.Port!.Position,new Vec3(101,0,0)),cut.BindingNodeIndex,cut.BindingNodeVersion);
        Assert.False(snapshot.Matches(new[] { trace }, new[] { wrongBinding }));
        Assert.False(snapshot.Matches(new[] { trace }, new[] { wrongTail }));
        Assert.False(snapshot.Matches(new[] { new RoadApproachTrace(cut.Port!,trace.Pieces) },new[] {cut}));
    }

    [Fact]
    public void ConfirmationRequiresNativeCheckedPreviewAndConsumesExactlyOnce()
    {
        var gate = new NodePlacementGate();
        gate.Stage();
        int revision = gate.Revision;
        Assert.False(gate.CanConfirm(true));
        gate.ObserveNative(false, true);
        Assert.False(gate.CanConfirm(true));
        gate.ObserveNative(true, false);
        Assert.False(gate.CanConfirm(true));
        gate.ObserveNative(true, true);
        Assert.False(gate.CanConfirm(false)); // dirty, settings, pause, mode and selection context are authoritative
        Assert.True(gate.CanConfirm(true));
        Assert.True(gate.TryConsume(revision, true));
        Assert.False(gate.TryConsume(revision, true));
    }

    [Fact]
    public void OldConfirmCannotApplyNewPreviewBuiltInSameFrame()
    {
        var gate = new NodePlacementGate();
        gate.Stage(); gate.ObserveNative(true, true);
        int oldRequest = gate.Revision;
        gate.Invalidate(); gate.Stage(); gate.ObserveNative(true, true);
        Assert.False(gate.TryConsume(oldRequest, true));
        Assert.True(gate.CanConfirm(true));
    }

    [Fact]
    public void RequestFromStaleVisiblePanelCannotQueueAlreadyMaturedReplacementPreview()
    {
        var gate = new NodePlacementGate();
        gate.Stage(); gate.ObserveNative(true, true);
        int displayedRevision = gate.Revision;
        gate.Invalidate(); gate.Stage();
        Assert.False(gate.ObserveBatch("replacement preview"));
        Assert.True(gate.ObserveBatch("replacement preview"));
        gate.ObserveNative(true, true);
        Assert.True(gate.CanConfirm(true)); // backend R2 is ready while the panel still displays R1
        Assert.False(gate.CanQueue(displayedRevision, true));
        Assert.False(gate.CanQueue(-1, true));
        Assert.False(gate.CanQueue(gate.Revision, false));
        Assert.True(gate.CanQueue(gate.Revision, true));
        Assert.True(gate.TryConsume(gate.Revision, true));
        Assert.False(gate.CanQueue(gate.Revision, true));
    }

    [Fact]
    public void NativeMaturityRequiresConsecutiveObservationsOfSameActualBatch()
    {
        var gate = new NodePlacementGate();
        gate.Stage();
        Assert.False(gate.ObserveBatch(null));
        Assert.False(gate.ObserveBatch(null));
        Assert.False(gate.ObserveBatch("first generated batch"));
        Assert.True(gate.ObserveBatch("first generated batch"));
        gate.ObserveNative(true, true);
        Assert.True(gate.CanConfirm(true));
        Assert.False(gate.ObserveBatch("native geometry changed"));
        Assert.False(gate.CanConfirm(true));
        Assert.False(gate.ObserveBatch(null));
        Assert.False(gate.ObserveBatch("native geometry changed"));
        Assert.True(gate.ObserveBatch("native geometry changed"));
        gate.Invalidate(); gate.Stage();
        Assert.False(gate.ObserveBatch("native geometry changed"));
    }

    [Theory]
    [InlineData(InterchangeType.Cloverleaf)] [InlineData(InterchangeType.Stack)] [InlineData(InterchangeType.Turbine)]
    [InlineData(InterchangeType.Trumpet)] [InlineData(InterchangeType.DirectionalT)] [InlineData(InterchangeType.SemiDirectionalT)]
    [InlineData(InterchangeType.Wye)] [InlineData(InterchangeType.CompactGrid)] [InlineData(InterchangeType.Roundabout)]
    public void ImplementedNodeTypesMayConfirm(InterchangeType type) => Assert.True(NodePlacementGate.Supports(type));

    [Theory]
    [InlineData(InterchangeType.Diamond)] [InlineData(InterchangeType.SinglePoint)] [InlineData(InterchangeType.ExpresswayNode)]
    [InlineData(InterchangeType.ExpresswayT)] [InlineData(InterchangeType.PartialCloverleaf)]
    public void OtherTypesRemainLocked(InterchangeType type) => Assert.False(NodePlacementGate.Supports(type));
}
