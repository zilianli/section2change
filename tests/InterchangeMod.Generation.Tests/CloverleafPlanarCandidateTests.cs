using System;
using System.Collections.Generic;
using System.Linq;
using InterchangeMod.Generation.Endpoints;
using InterchangeMod.Generation.Geometry;
using Xunit;

namespace InterchangeMod.Generation.Tests
{
    public class CloverleafPlanarCandidateTests
    {
        private static EndpointArm[] Arms(float distance = 252, bool rightHandTraffic = true) =>
            EndpointFixtures.DualCross(distance, rightHandTraffic);

        [Fact]
        public void BuildsDistinctDirectAndLoopPathsFromMeasuredEndpointPositions()
        {
            var candidate = PlanarCloverleafCandidate.Create(Arms(), true, 30, 100);
            Assert.Equal(PlanarCandidateFailure.None, candidate.Failure);
            Assert.False(candidate.IsPlacementReady);
            Assert.Equal(8, candidate.Ramps.Count);
            var direct = candidate.Ramps.Single(r => r.SourceArm == 0 && r.Kind == CloverEdgeKind.DirectRamp);
            var loop = candidate.Ramps.Single(r => r.SourceArm == 0 && r.Kind == CloverEdgeKind.LoopRamp);
            Assert.Equal(3, direct.TargetArm);
            Assert.Equal(1, loop.TargetArm);
            Assert.True(Vec3.Distance(direct.Curves[0].P0, new Vec3(-12, 0, 112)) < 0.01f);
            Assert.True(Vec3.Distance(direct.Curves[^1].P3, new Vec3(-112, 0, 12)) < 0.01f);
            Assert.True(Vec3.Distance(loop.Curves[0].P0, new Vec3(-12, 0, -42)) < 0.01f);
            Assert.True(Vec3.Distance(loop.Curves[^1].P3, new Vec3(-42, 0, -12)) < 0.01f);
            Assert.InRange(direct.SweepRadians, Math.PI / 2 - 0.001, Math.PI / 2 + 0.001);
            Assert.InRange(loop.SweepRadians, 3 * Math.PI / 2 - 0.001, 3 * Math.PI / 2 + 0.001);
            foreach (var ramp in candidate.Ramps)
            {
                foreach (var curve in ramp.Curves)
                    Assert.True(curve.Length() <= 70.1f);
                for (int i = 1; i < ramp.Curves.Count; i++) Assert.Equal(ramp.Curves[i - 1].P3, ramp.Curves[i].P0);
                var sourceLine = candidate.Lines.Single(l => l.SourceArm == ramp.SourceArm);
                var destinationLine = candidate.Lines.Single(l => l.SourceArm == (ramp.TargetArm + 2) % 4);
                var startKind = ramp.Kind == CloverEdgeKind.DirectRamp ? CloverNodeKind.DirectExit : CloverNodeKind.LoopExit;
                var endKind = ramp.Kind == CloverEdgeKind.DirectRamp ? CloverNodeKind.DirectMerge : CloverNodeKind.LoopMerge;
                Assert.Equal(sourceLine.Junctions.Single(j => j.Node.Kind == startKind).Position, ramp.Curves[0].P0);
                Assert.Equal(destinationLine.Junctions.Single(j => j.Node.Kind == endKind).Position, ramp.Curves[^1].P3);
            }

            var line = candidate.Lines.Single(l => l.SourceArm == 0);
            Assert.Equal(new[] { CloverNodeKind.DirectExit, CloverNodeKind.LoopMerge, CloverNodeKind.LoopExit, CloverNodeKind.DirectMerge },
                line.Junctions.Select(j => j.Node.Kind));
            Assert.Equal(new[] { 140f, 210f, 294f, 364f }, line.Junctions.Select(j => (float)Math.Round(j.Along)));
            Assert.Equal(direct.Curves[0].P0, line.Junctions[0].Position);
        }

        [Theory]
        [InlineData(true, 3, 1)]
        [InlineData(false, 1, 3)]
        public void KeepsPhysicalTurnDestinationsForBothTrafficSides(bool rightHandTraffic, int directTarget, int loopTarget)
        {
            var candidate = PlanarCloverleafCandidate.Create(Arms(rightHandTraffic: rightHandTraffic), rightHandTraffic, 30, 100);
            Assert.Equal(PlanarCandidateFailure.None, candidate.Failure);
            Assert.Equal(directTarget, candidate.Ramps.Single(r => r.SourceArm == 0 && r.Kind == CloverEdgeKind.DirectRamp).TargetArm);
            Assert.Equal(loopTarget, candidate.Ramps.Single(r => r.SourceArm == 0 && r.Kind == CloverEdgeKind.LoopRamp).TargetArm);
        }

        [Theory]
        [InlineData(CloverEdgeKind.DirectRamp)]
        [InlineData(CloverEdgeKind.LoopRamp)]
        public void PlanarRampCanReceiveAContinuousProfileWithFlatEnds(CloverEdgeKind kind)
        {
            var candidate = PlanarCloverleafCandidate.Create(Arms(), true, 30, 100);
            var ramp = candidate.Ramps.Single(r => r.SourceArm == 0 && r.Kind == kind);
            var profile = VerticalProfile.ApplyWithFlatEnds(ramp.Curves, 10, 20, 30, 30, 0.30f);
            Assert.Equal(VerticalProfileFailure.None, profile.Failure);
            Assert.Equal(10f, profile.Curves[0].P0.Y, 4);
            Assert.Equal(20f, profile.Curves[^1].P3.Y, 4);
            foreach (var curve in profile.Curves)
                Assert.True(curve.MaxGrade(256) <= 0.30001f);
            for (int i = 1; i < profile.Curves.Count; i++)
                Assert.Equal(profile.Curves[i - 1].P3, profile.Curves[i].P0);
        }

        [Fact]
        public void RejectsWhenRequestedRampsDoNotFitAvailableApproaches()
        {
            Assert.Equal(PlanarCandidateFailure.AttachmentsOutsideApproach,
                PlanarCloverleafCandidate.Create(Arms(distance: 80), true, 30, 100).Failure);
        }

        [Fact]
        public void BoundedRetreatLetsEachApproachMoveOutwardWithoutMutatingSelectedNodes()
        {
            var arms = Arms(distance: 80);
            var requested = new[] { 200f, 220f, 240f, 260f };
            var candidate = PlanarCloverleafCandidate.Create(arms, true, 30, 100, requested);
            Assert.Equal(PlanarCandidateFailure.None, candidate.Failure);
            var northToSouth = candidate.Lines.Single(l => l.SourceArm == 0);
            Assert.Equal(280f, northToSouth.Start.Z, 3);
            Assert.Equal(-320f, northToSouth.End.Z, 3);
            Assert.Equal(80f, arms[0].Endpoints[0].Position.Z, 3);
            Assert.Equal(-80f, arms[2].Endpoints[1].Position.Z, 3);
            requested[0] = 0f;
            Assert.Equal(new[] { 200f, 220f, 240f, 260f }, candidate.RetreatByArm);
            Assert.False(candidate.IsPlacementReady);
        }

        [Fact]
        public void RejectsInvalidRetreatLimits()
        {
            var choices = new[]
            {
                new[] { 1f, 2f, 3f }, new[] { -1f, 0f, 0f, 0f },
                new[] { float.NaN, 0f, 0f, 0f }, new[] { 0f, float.PositiveInfinity, 0f, 0f },
            };
            foreach (var retreat in choices)
            {
                var result = PlanarCloverleafCandidate.Create(Arms(), true, 30, 100, retreat);
                Assert.Equal(PlanarCandidateFailure.InvalidRetreat, result.Failure);
                Assert.Empty(result.Ramps);
                Assert.False(result.IsPlacementReady);
            }
        }

        [Theory]
        [InlineData(100f)]
        [InlineData(120f)]
        public void WidthPlannedSegmentsAvoidNodesInsideMergeOverlap(float directRadius)
        {
            var candidate = PlanarCloverleafCandidate.Create(Arms(), true, 30, directRadius, rampWidth: 8, sideGap: 4);
            Assert.Equal(PlanarCandidateFailure.None, candidate.Failure);
            foreach (var ramp in candidate.Ramps)
            {
                float radius = ramp.Kind == CloverEdgeKind.DirectRamp ? directRadius : 30f;
                // (12 m road + 8 m ramp)/2 + 4 m gap = 14 m.
                float escape = radius * (float)Math.Acos(1 - 14 / radius);
                Assert.NotNull(ramp.RequiredFlatStart);
                Assert.NotNull(ramp.RequiredFlatEnd);
                Assert.True(ramp.RequiredFlatStart!.Value > escape);
                Assert.True(ramp.RequiredFlatEnd!.Value > escape);
                Assert.True(ramp.Curves[0].Length(128) >= escape);
                Assert.True(ramp.Curves[^1].Length(128) >= escape);
                foreach (var curve in ramp.Curves) Assert.InRange(curve.Length(128), 9.99f, 70.1f);
            }
            var direct = candidate.Ramps.Single(r => r.SourceArm == 0 && r.Kind == CloverEdgeKind.DirectRamp);
            if (directRadius == 100f) Assert.True(direct.Curves[0].Length(128) > 53.55f);
            foreach (var line in candidate.Lines)
            {
                Assert.Equal(4, line.ProtectedOverlaps.Count);
                foreach (var node in line.Junctions)
                    Assert.DoesNotContain(line.ProtectedOverlaps,
                        z => node.Along > z.StartAlong + 0.001f && node.Along < z.EndAlong - 0.001f);
            }
        }

        [Fact]
        public void WidthPlanningRejectsInvalidWidthsAndUnclearableMerge()
        {
            Assert.Equal(PlanarCandidateFailure.InvalidRoadWidth,
                PlanarCloverleafCandidate.Create(Arms(), true, 30, 100, rampWidth: float.NaN).Failure);
            Assert.Equal(PlanarCandidateFailure.InvalidRoadWidth,
                PlanarCloverleafCandidate.Create(Arms(), true, 30, 100, rampWidth: 8, sideGap: -1).Failure);
            var tooWide = PlanarCloverleafCandidate.Create(Arms(), true, 30, 100, rampWidth: 120, sideGap: 4);
            Assert.Equal(PlanarCandidateFailure.MergeCannotClear, tooWide.Failure);
            Assert.Empty(tooWide.Ramps);
        }

        [Fact]
        public void WidthPlanningUsesSourceAndTargetRoadWidthsSeparately()
        {
            var arms = Arms();
            var old = arms[0].Endpoints[0];
            var wider = new SelectedEndpoint(old.NodeIndex, old.NodeVersion, old.Position, old.OutwardTangent,
                old.OneWay, old.TravelOutward, 16, old.Lanes, old.Prefab);
            arms[0] = new EndpointArm(0, new[] { wider, arms[0].Endpoints[1] });
            var candidate = PlanarCloverleafCandidate.Create(arms, true, 30, 120, rampWidth: 8, sideGap: 4);
            Assert.Equal(PlanarCandidateFailure.None, candidate.Failure);
            var direct = candidate.Ramps.Single(r => r.SourceArm == 0 && r.Kind == CloverEdgeKind.DirectRamp);
            Assert.True(direct.RequiredFlatStart!.Value > direct.RequiredFlatEnd!.Value + 1f);
        }

        [Fact]
        public void RejectsAnotherMainlineNodeInsideAPlannedMergeOverlap()
        {
            // The 50 m loop puts its merge about 30 m beyond the 100 m
            // direct diverge, but that direct ramp has not yet cleared.
            var candidate = PlanarCloverleafCandidate.Create(Arms(), true, 50, 100, rampWidth: 8, sideGap: 4);
            Assert.Equal(PlanarCandidateFailure.MergeOverlapConflict, candidate.Failure);
            Assert.Empty(candidate.Ramps);
        }

        [Fact]
        public void RejectsConflictingAttachmentOrderEvenWhenAllAreWithinTheApproach()
        {
            Assert.Equal(PlanarCandidateFailure.ConflictingAttachmentOrder,
                PlanarCloverleafCandidate.Create(Arms(), true, 100, 30).Failure);
        }

        [Fact]
        public void RejectsParallelCrossingWithoutProducingCurves()
        {
            EndpointArm Dual(int arm, float x, float z, bool north)
            {
                Vec3 outward = north ? Vec3.UnitZ : -Vec3.UnitZ;
                float incomingX = north ? x - 12 : x + 12;
                float outgoingX = north ? x + 12 : x - 12;
                return new EndpointArm(arm, new[]
                {
                    new SelectedEndpoint(arm * 10 + 1, 1, new Vec3(incomingX, 0, z), outward, true, false, 12, 2, "Synthetic"),
                    new SelectedEndpoint(arm * 10 + 2, 1, new Vec3(outgoingX, 0, z), outward, true, true, 12, 2, "Synthetic"),
                });
            }
            var parallel = new[] { Dual(0, 0, 252, true), Dual(1, 100, 252, true), Dual(2, 0, -252, false), Dual(3, 100, -252, false) };
            var candidate = PlanarCloverleafCandidate.Create(parallel, true, 30, 100);
            Assert.Equal(PlanarCandidateFailure.DegenerateCrossing, candidate.Failure);
            Assert.Empty(candidate.Ramps);
        }

        [Fact]
        public void ReportsUnsupportedTwoWayAndNonStraightApproach()
        {
            var twoWay = Enumerable.Range(0, 4).Select(i => new EndpointArm(i, new[]
            {
                new SelectedEndpoint(i + 1, 1, Vec3.UnitZ * (i + 1), Vec3.UnitZ, false, false, 20, 2, "Synthetic")
            })).ToArray();
            Assert.Equal(PlanarCandidateFailure.UnsupportedRoadForm, PlanarCloverleafCandidate.Create(twoWay, true, 30, 100).Failure);

            var arms = Arms();
            var old = arms[2].Endpoints[1];
            var moved = new SelectedEndpoint(old.NodeIndex, old.NodeVersion, old.Position + Vec3.UnitX,
                old.OutwardTangent, old.OneWay, old.TravelOutward, old.Width, old.Lanes, old.Prefab);
            arms[2] = new EndpointArm(2, new[] { arms[2].Endpoints[0], moved });
            Assert.Equal(PlanarCandidateFailure.NonStraightApproach, PlanarCloverleafCandidate.Create(arms, true, 30, 100).Failure);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(float.NaN)]
        [InlineData(float.PositiveInfinity)]
        public void RejectsInvalidRadius(float radius)
        {
            Assert.Equal(PlanarCandidateFailure.InvalidRadius, PlanarCloverleafCandidate.Create(Arms(), true, radius, 100).Failure);
        }
    }
}
