using System;
using System.Linq;
using InterchangeMod.Generation.Endpoints;
using InterchangeMod.Generation.Geometry;
using Xunit;

namespace InterchangeMod.Generation.Tests
{
    public class MixedApproachTests
    {
        // Rounded game-log snapshots, 2026-10-30. Future road continuations below are synthetic,
        // not a capture of the unseen original road curves beyond the selected endpoints.
        public static EndpointArm[] LoggedArms(bool mixedHeight)
        {
            var p=mixedHeight?new[]{new Vec3(-4171.11f,488.63f,-3552.68f),new Vec3(-4198.13f,488.63f,-3545.36f),
                new Vec3(-4275.66f,478.63f,-3337.51f),new Vec3(-4269.01f,478.63f,-3310.31f),
                new Vec3(-4091.35f,498.63f,-3151.58f),new Vec3(-4064.32f,498.63f,-3158.91f),
                new Vec3(-4057.5f,488.63f,-3362.29f),new Vec3(-4065.38f,488.63f,-3389.16f)}
                :new[]{new Vec3(859.58f,488.63f,-2649.53f),new Vec3(832.17f,488.63f,-2643.81f),
                new Vec3(722.92f,488.63f,-2468.8f),new Vec3(733.1f,488.63f,-2442.72f),
                new Vec3(895.72f,488.63f,-2299.43f),new Vec3(923.03f,488.63f,-2305.63f),
                new Vec3(1012.49f,488.63f,-2533.52f),new Vec3(997.69f,488.63f,-2557.3f)};
            var t=mixedHeight?new[]{new Vec3(-6.91f,0,-25.48f),new Vec3(-6.91f,0,-25.48f),
                new Vec3(-58.02f,0,14.19f),new Vec3(-58.02f,0,14.19f),new Vec3(6.73f,.03f,24.83f),new Vec3(6.71f,.03f,24.75f),
                new Vec3(24.67f,0,-7.24f),new Vec3(24.68f,0,-7.24f)}
                :new[]{new Vec3(-5.22f,0,-25.03f),new Vec3(-5.17f,0,-24.76f),new Vec3(-24.69f,0,9.64f),new Vec3(-22.86f,0,8.93f),
                new Vec3(5.52f,0,24.31f),new Vec3(5.86f,0,25.82f),new Vec3(21.65f,0,-13.47f),new Vec3(22.24f,0,-13.84f)};
            return Enumerable.Range(0,4).Select(i=>new EndpointArm(i,Enumerable.Range(0,2).Select(k=>
                new SelectedEndpoint(i*10+k+1,1,p[i*2+k],t[i*2+k],true,k==1,20,4,"Full")))).ToArray();
        }

        [Theory]
        [InlineData(true,.16f,false,true)][InlineData(false,.16f,false,true)]
        [InlineData(true,.27f,false,false)][InlineData(false,.27f,false,false)]
        [InlineData(true,.16f,true,false)]
        [InlineData(true,.27f,true,true)][InlineData(false,.27f,true,true)]
        public void LoggedMixedAndCurvedArmsCanPreviewWithExactEndpointTangents(bool mixed,float grade,bool curvedTrace,bool crossOnTop)
        {
            var arms=LoggedArms(mixed);
            var traces=arms.SelectMany(a=>a.Endpoints).Select(p=>new RoadApproachTrace(p,new[]{
                new RoadTracePiece(p.NodeIndex+1000,1,curvedTrace
                    ?Bezier3.FromTangents(p.Position,p.OutwardTangent,p.Position+Vec3.Normalize(p.OutwardTangent)*600,
                        p.OutwardTangent.RotateY(-(float)Math.PI/18),200)
                    :Bezier3.Line(p.Position,p.Position+Vec3.Normalize(p.OutwardTangent)*600),false,p.NodeIndex+2000,1)})).ToArray();
            var design=new CloverleafRoadDesign(Enumerable.Range(0,4).Select(i=>new EndpointLaneBalance(i,"Reduced",16,3,1)));
            var levels=EndpointLevels.Resolve(arms,crossOnTop,0,0,488.63f);
            var result=RetreatCloverleafSearch.Search(arms,traces,true,levels.Main,levels.Cross,8,grade,500,design:design);
            Assert.True(result.Failure==RetreatSearchFailure.None,$"{result.Failure}: {result.LastPlanarFailure}/{result.LastSpatialFailure}; {result.ClearanceDetail}");
            Assert.InRange(result.Attempts,1,6144);
            Assert.All(result.Spatial!.Segments,s=>Assert.InRange(s.WorldCurve.MaxGrade(256),0,grade+.00001f));
            foreach(var arm in result.Arms)
                foreach(var port in arm.Endpoints)
                {
                    var s=result.Spatial.Segments.Single(s=>port.TravelOutward?s.EndNode==port.Position:s.StartNode==port.Position);
                    var actual=Vec3.Normalize(s.WorldCurve.Derivative(port.TravelOutward?1:0));
                    var expected=Vec3.Normalize(port.OutwardTangent)*(port.TravelOutward?1:-1);
                    Assert.True(actual.X*expected.X+actual.Y*expected.Y+actual.Z*expected.Z>.99999f);
                }
        }

        [Fact]
        public void ExcessiveDeflectionIsRejectedAndStrictGeometryApiKeepsItsContract()
        {
            var arms=LoggedArms(true);
            Assert.Equal(PlanarCandidateFailure.NonStraightApproach,PlanarCloverleafCandidate.Create(arms,true,40,100).Failure);
            var p=arms[0].Endpoints[0];
            arms[0]=new EndpointArm(0,new[]{new SelectedEndpoint(p.NodeIndex,p.NodeVersion,p.Position,
                p.OutwardTangent.RotateY((float)Math.PI/4),true,false,p.Width,p.Lanes,p.Prefab),arms[0].Endpoints[1]});
            Assert.Equal(PlanarCandidateFailure.NonStraightApproach,PlanarCloverleafCandidate.Create(arms,true,40,100,
                rampWidth:8,design:new CloverleafRoadDesign()).Failure);
        }

        [Fact]
        public void AutomaticMixedLevelsAvoidRaisedSolidUpperRoadAndRespectManualSettings()
        {
            var mixed=LoggedArms(true);
            var automatic=EndpointLevels.Resolve(mixed,true,0,0,488.63f);
            Assert.Equal(497.13f,automatic.Main,2);Assert.Equal(487.13f,automatic.Cross,2);
            Assert.Equal(3.5f,automatic.TerrainLift,2);
            var manual=EndpointLevels.Resolve(mixed,true,2,0,488.63f);
            Assert.Equal(495.63f,manual.Main,2);Assert.Equal(485.63f,manual.Cross,2);Assert.Equal(0,manual.TerrainLift);
            var regular=EndpointLevels.Resolve(LoggedArms(false),true,0,0,488.63f);
            Assert.Equal(488.63f,regular.Main,2);Assert.Equal(498.63f,regular.Cross,2);Assert.Equal(0,regular.TerrainLift);
            Assert.Throws<ArgumentException>(()=>EndpointLevels.Resolve(mixed,true,0,0,float.NaN));
        }

        [Fact]
        public void CurvedApproachSearchDoesNotAcceptOverlappingMainCarriageways()
        {
            // A deliberately separate synthetic road continuation: at the available 16%/500m
            // combinations the reconnection envelopes conflict. Do not turn that into a valid preview.
            var arms=LoggedArms(false);
            var traces=arms.SelectMany(a=>a.Endpoints).Select(p=>new RoadApproachTrace(p,new[]{
                new RoadTracePiece(p.NodeIndex+1000,1,Bezier3.FromTangents(p.Position,p.OutwardTangent,
                    p.Position+Vec3.Normalize(p.OutwardTangent)*600,p.OutwardTangent.RotateY(-(float)Math.PI/18),200),
                    false,p.NodeIndex+2000,1)})).ToArray();
            var levels=EndpointLevels.Resolve(arms,false,0,0,488.63f);
            var result=RetreatCloverleafSearch.Search(arms,traces,true,levels.Main,levels.Cross,8,.16f,500,
                design:new CloverleafRoadDesign(Enumerable.Range(0,4).Select(i=>new EndpointLaneBalance(i,"Reduced",16,3,1))));
            Assert.NotEqual(RetreatSearchFailure.None,result.Failure);
            Assert.Null(result.Spatial);
            Assert.Contains("Main",result.ClearanceDetail);Assert.Contains("Conflict",result.ClearanceDetail);
        }
    }
}
