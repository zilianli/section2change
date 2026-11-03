using System;
using InterchangeMod.Generation.Endpoints;
using InterchangeMod.Generation.Geometry;
using InterchangeMod.Generation.Roads;
using Xunit;

namespace InterchangeMod.Generation.Tests
{
    public class EndpointNetworkSupportsTests
    {
        private static readonly RoadInfo Ramp=new RoadInfo("Ramp",true,1,0,8);
        private static SpatialSegment Line(int arm,CloverEdgeKind kind,Vec3 a,Vec3 b,string path,float width=20)=>
            new SpatialSegment(arm,kind,Bezier3.Line(a,b),roadWidth:width,pathId:path);
        private static SpatialSegment Lower=>Line(1,CloverEdgeKind.Main,new Vec3(0,10,-100),new Vec3(0,10,100),"Lower");

        [Theory]
        [InlineData(null)][InlineData(0f)][InlineData(30f)]
        public void RejectsAutomaticOrMiddleSupportAboveLowerDeck(float? middle)
        {
            var upper=Line(0,CloverEdgeKind.Main,new Vec3(-80,20,0),new Vec3(80,20,0),"Upper");
            var c=new SpatialCandidateResult(SpatialCandidateFailure.None,new[]{upper,Lower});
            var result=EndpointNetworkSupports.Check(c,new[]{new EndpointBridgeRule(0,80,middle,20),new EndpointBridgeRule(1,80,null,20)},Ramp,out _);
            Assert.Equal(CurveClearanceResult.Conflict,result);
        }
        [Fact]
        public void ClearPlannedNodesDoNotInsertASupportIntoAnEightyMetreSpan()
        {
            var c=new SpatialCandidateResult(SpatialCandidateFailure.None,new[]{
                Line(0,CloverEdgeKind.Main,new Vec3(-80,20,0),new Vec3(-40,20,0),"Upper"),
                Line(0,CloverEdgeKind.Main,new Vec3(-40,20,0),new Vec3(40,20,0),"Upper"),
                Line(0,CloverEdgeKind.Main,new Vec3(40,20,0),new Vec3(80,20,0),"Upper"),Lower});
            Assert.Equal(CurveClearanceResult.Clear,EndpointNetworkSupports.Check(c,new[]{
                new EndpointBridgeRule(0,80,null,20),new EndpointBridgeRule(1,80,null,20)},Ramp,out _));
        }
        [Fact]
        public void UsesRampMiddleSupportMetadata()
        {
            var upper=Line(0,CloverEdgeKind.LeftRamp,new Vec3(-40,20,0),new Vec3(40,20,0),"Left",8);
            var ramp=new RoadInfo("Ramp",true,1,0,8){MiddlePillarSpacing=0,ElevatedLength=80};
            Assert.Equal(CurveClearanceResult.Conflict,EndpointNetworkSupports.Check(
                new SpatialCandidateResult(SpatialCandidateFailure.None,new[]{upper,Lower}),
                new[]{new EndpointBridgeRule(1,80,null,20)},ramp,out _));
        }
        [Fact]
        public void ActualWidePillarEnvelopeReachesTheLowerRoad()
        {
            var c=new SpatialCandidateResult(SpatialCandidateFailure.None,new[]{
                Line(0,CloverEdgeKind.Main,new Vec3(0,20,-40),new Vec3(0,20,40),"Upper"),
                Line(1,CloverEdgeKind.Main,new Vec3(-100,10,-33),new Vec3(100,10,-33),"Lower",8)});
            Assert.Equal(CurveClearanceResult.Conflict,EndpointNetworkSupports.Check(c,new[]{
                new EndpointBridgeRule(0,80,null,20,pillarWidth:8),new EndpointBridgeRule(1,80,null,8)},Ramp,out _));
        }
        [Theory]
        [InlineData(80,0f)][InlineData(40,null)]
        public void ReducedRoadUsesItsOwnMiddleAndAutomaticSupportRules(float maxLength,float? spacing)
        {
            var upper=new SpatialSegment(0,CloverEdgeKind.Main,Bezier3.Line(new Vec3(-40,20,0),new Vec3(40,20,0)),
                prefab:"Reduced",roadWidth:16,pathId:"Upper");
            var c=new SpatialCandidateResult(SpatialCandidateFailure.None,new[]{upper,Lower});
            Assert.Equal(CurveClearanceResult.Conflict,EndpointNetworkSupports.Check(c,new[]{
                new EndpointBridgeRule(0,80,null,20),new EndpointBridgeRule(1,80,null,20)},Ramp,out _,
                new[]{new EndpointPrefabSupportRule("Reduced",maxLength,spacing,4)}));
        }
        [Fact]
        public void InvalidRulesAreRejected()
        {
            Assert.Equal(CurveClearanceResult.InvalidInput,EndpointNetworkSupports.Check(
                new SpatialCandidateResult(SpatialCandidateFailure.None,new[]{Lower}),
                new[]{new EndpointBridgeRule(1,float.NaN,null,20)},Ramp,out _));
        }
    }
}
