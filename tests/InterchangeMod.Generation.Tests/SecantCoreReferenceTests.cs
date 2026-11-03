using InterchangeMod.Generation.Endpoints;
using InterchangeMod.Generation.Geometry;
using InterchangeMod.Generation.Roads;

namespace InterchangeMod.Generation.Tests;

public class SecantCoreReferenceTests
{
    [Fact]
    public void CoincidentReferencesDoNotSpendTheCompleteCandidateBudgetTwice()
    {
        var arms=EndpointFixtures.DualCross(2000);
        var ramp=new RoadInfo("Ramp",true,1,0,8);var design=new CloverleafRoadDesign();
        var safeOptions=EndpointStackCore.Candidates(arms,InterchangeType.Turbine,true,10,22,ramp,.16f,design).Count(c=>c.Success);
        Assert.True(safeOptions>0&&safeOptions%2==0);
        var traces=arms.SelectMany(a=>a.Endpoints).Select(p=>new RoadApproachTrace(p,Array.Empty<RoadTracePiece>())).ToArray();
        var result=EndpointStackSearch.Search(arms,traces,InterchangeType.Turbine,true,10,22,ramp,.16f,0,design,verticalClearance:1000);
        Assert.Equal(RetreatSearchFailure.NoCandidate,result.Failure);
        Assert.Equal(safeOptions/2,result.Attempts);
        Assert.Null(result.Spatial);Assert.Empty(result.Cuts);
    }
    [Fact]
    public void SecantCoreDoesNotDependOnAValidTangentFrame()
    {
        var arms=EndpointFixtures.DualCross(500).Select(a=>
        {
            var axis=a.Index<2?Vec3.UnitZ:-Vec3.UnitZ;
            var center=new[]{Vec3.UnitZ,Vec3.UnitX,-Vec3.UnitZ,-Vec3.UnitX}[a.Index]*500+new Vec3(0,a.Index%2==0?10:22,0);
            return new EndpointArm(a.Index,a.Endpoints.Select(p=>new SelectedEndpoint(p.NodeIndex,p.NodeVersion,
                center+Directions.RightOf(axis)*(p.TravelOutward?12:-12),axis,true,p.TravelOutward,12,2,"Road")));
        }).ToArray();
        Assert.Equal(EndpointFrameFailure.ParallelAxes,EndpointFrame.Estimate(arms,InterchangeType.Stack).Failure);
        var options=EndpointStackCore.Candidates(arms,InterchangeType.Stack,true,10,22,
            new RoadInfo("Ramp",true,1,0,8),.16f,new CloverleafRoadDesign()).ToArray();
        Assert.All(options.Take(8),c=>Assert.False(c.Success));
        Assert.Contains(options.Skip(8),c=>c.Success);
    }

    [Theory]
    [InlineData(InterchangeType.Stack,false,true,22f,10f,8)]
    [InlineData(InterchangeType.Stack,false,false,10f,22f,8)]
    [InlineData(InterchangeType.Stack,true,false,22f,10f,11)]
    [InlineData(InterchangeType.Turbine,false,false,22f,10f,6)]
    public void SecantPortsKeepOriginalArmTrafficAndHeight(InterchangeType type,bool left,bool rht,float main,float cross,int perPose)
    {
        var arms=RealCurvedApproachTests.Arms(RealCurvedApproachTests.Traces()).Select(a=>new EndpointArm(a.Index,
            a.Endpoints.Select(p=>new SelectedEndpoint(p.NodeIndex,p.NodeVersion,new Vec3(p.Position.X,a.Index%2==0?main:cross,p.Position.Z),
                p.OutwardTangent,true,rht?p.TravelOutward:!p.TravelOutward,p.Width,p.Lanes,a.Index%2==0?"MainFull":"CrossFull")))).ToArray();
        var positions=arms.SelectMany(a=>a.Endpoints).Select(p=>p.Position).ToArray();
        var ramp=new RoadInfo("Ramp",true,1,0,8);
        var design=new CloverleafRoadDesign();
        var options=EndpointStackCore.Candidates(arms,type,rht,main,cross,ramp,.16f,design,left).Skip(perPose).Where(c=>c.Success).ToArray();
        Assert.NotEmpty(options);
        Vec3 Mid(EndpointArm arm)=>(arm.Endpoints[0].Position+arm.Endpoints[1].Position)*.5f;
        foreach(var option in options)
        {
            Assert.Equal(8,option.Ports.Count);
            foreach(var p in option.Ports)
            {
                var original=arms[p.Arm].Endpoints.Single(e=>e.TravelOutward==p.TravelOutward);
                Assert.Equal(original.Prefab,p.Prefab);Assert.Equal(original.Width,p.Width);
                Assert.Equal(p.Arm%2==0?main:cross,p.Position.Y);
                var axis=Mid(arms[p.Arm])-Mid(arms[(p.Arm+2)%4]);axis.Y=0;
                Assert.True(Directions.Dot(Vec3.Normalize(axis),p.TravelOutward?p.Tangent:-p.Tangent)>.99999f);
            }
            var spatial=new SpatialCandidateResult(SpatialCandidateFailure.None,option.Segments);
            Assert.Equal(CurveClearanceResult.Clear,EndpointNetworkClearance.Check(spatial,8.5f,out _));
            Assert.Equal(CurveClearanceResult.Clear,EndpointNetworkSupports.Check(spatial,design.BridgeRules,ramp,out _));
        }
        Assert.Equal(positions,arms.SelectMany(a=>a.Endpoints).Select(p=>p.Position));
    }
}
