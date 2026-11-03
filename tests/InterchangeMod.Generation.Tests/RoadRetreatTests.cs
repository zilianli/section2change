using System;
using InterchangeMod.Generation.Endpoints;
using InterchangeMod.Generation.Geometry;
using Xunit;
namespace InterchangeMod.Generation.Tests
{
    public class RoadRetreatTests
    {
        private static RoadApproachTrace Trace(bool reversed=false)
        {
            var p=new SelectedEndpoint(1,1,Vec3.Zero,new Vec3(1,.1f,0),true,false,20,4,"Road");
            return new RoadApproachTrace(p,new[] {
                new RoadTracePiece(10,1,Bezier3.Line(Vec3.Zero,new Vec3(100,10,0)),reversed,2,1),
                new RoadTracePiece(11,1,Bezier3.Line(new Vec3(100,10,0),new Vec3(200,20,0)),!reversed,3,1) });
        }
        [Theory][InlineData(false)][InlineData(true)]
        public void CutsAcrossActualPiecesAndKeepsOriginalRoadDirection(bool reversed)
        {
            var cut=RoadRetreat.Cut(Trace(reversed),150);
            Assert.Equal(RoadRetreatFailure.None,cut.Failure);
            Assert.InRange(cut.Port!.Position.X,149.999f,150.001f);
            Assert.InRange(cut.Port.Position.Y,14.999f,15.001f);
            Assert.Equal(2,cut.Consumed.Count);
            Assert.Equal(!reversed,cut.Consumed[1].Reversed);
            Assert.Equal(cut.Port.Position,cut.Remainder!.Value.P0);
            Assert.Equal(2,cut.ModifiedCurves.Count);
            Assert.Equal(cut.Port.Position,cut.ModifiedCurves[1].P3);
            Assert.Equal(0,cut.BindingNodeIndex); // Middle of a real edge: shared new node, never the selected old entity.
        }
        [Fact] public void PreservesExactExistingNodesAtZeroAndPieceBoundary()
        {
            var zero=RoadRetreat.Cut(Trace(),0);
            Assert.Empty(zero.Consumed); Assert.Equal(1,zero.BindingNodeIndex);
            var boundary=RoadRetreat.Cut(Trace(),100);
            Assert.Equal(new Vec3(100,10,0),boundary.Port!.Position);
            Assert.Equal(2,boundary.BindingNodeIndex); Assert.Single(boundary.Consumed); Assert.Null(boundary.Remainder);
        }
        [Fact] public void RejectsOutsideTraceShortRemaindersAndInvalidValues()
        {
            Assert.Equal(RoadRetreatFailure.OutsideTrace,RoadRetreat.Cut(Trace(),250).Failure);
            Assert.Equal(RoadRetreatFailure.ShortRemainder,RoadRetreat.Cut(Trace(),195).Failure);
            Assert.Equal(RoadRetreatFailure.InvalidInput,RoadRetreat.Cut(Trace(),float.NaN).Failure);
            var t=Trace();
            var broken=new RoadApproachTrace(t.Origin,new[] {new RoadTracePiece(10,1,Bezier3.Line(new Vec3(1,0,0),new Vec3(100,0,0)),false,2,1)});
            Assert.Equal(RoadRetreatFailure.InvalidTrace,RoadRetreat.Cut(broken,50).Failure);
        }
        [Fact] public void RejectsInvalidOriginEvenAtZeroAndNullTracePieces()
        {
            foreach(var bad in new[]{
                new SelectedEndpoint(1,1,new Vec3(float.NaN,0,0),Vec3.UnitX,true,false,20,4,"Road"),
                new SelectedEndpoint(1,1,Vec3.Zero,Vec3.Zero,true,false,20,4,"Road"),
                new SelectedEndpoint(1,1,Vec3.Zero,Vec3.UnitX,true,false,-1,4,"Road") })
                Assert.Equal(RoadRetreatFailure.InvalidInput,RoadRetreat.Cut(new RoadApproachTrace(bad,System.Array.Empty<RoadTracePiece>()),0).Failure);
            Assert.Equal(RoadRetreatFailure.InvalidTrace,RoadRetreat.Cut(new RoadApproachTrace(Trace().Origin,new RoadTracePiece[]{null!}),10).Failure);
        }
    }
}
