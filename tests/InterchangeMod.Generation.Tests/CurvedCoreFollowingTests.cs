using System;
using System.Linq;
using InterchangeMod.Generation.Endpoints;
using InterchangeMod.Generation.Geometry;

namespace InterchangeMod.Generation.Tests;

public class CurvedCoreFollowingTests
{
    public static System.Collections.Generic.IEnumerable<object[]> Mappings()
    {
        foreach(bool rht in new[]{true,false})
        foreach(bool upper in new[]{true,false})
        {
            yield return new object[]{InterchangeType.Stack,false,rht,upper};
            yield return new object[]{InterchangeType.Stack,true,rht,upper};
            yield return new object[]{InterchangeType.Turbine,false,rht,upper};
        }
    }
    private static EndpointArm[] Synthetic(bool rht,bool curved)
    {
        return Enumerable.Range(0,4).Select(i=>
        {
            float angle=i%2==0?23+(i==2?180:0):98+(i==3?180:0);
            var axis=Directions.FromAngle(angle);
            var heading=Directions.FromAngle(angle+(curved?(i<2?8:-8):0));
            var side=Directions.RightOf(heading)*(rht?1:-1);
            float width=i%2==0?20:16;var center=new Vec3(400,0,-230)+axis*1300;
            return new EndpointArm(i,new[]{
                new SelectedEndpoint(i*10+1,1,center-side*((width+8)/2),heading,true,false,width,i%2==0?3:2,i%2==0?"Main20":"Cross16"),
                new SelectedEndpoint(i*10+2,1,center+side*((width+8)/2),heading,true,true,width,i%2==0?3:2,i%2==0?"Main20":"Cross16")});
        }).ToArray();
    }
    [Theory]
    [MemberData(nameof(Mappings))]
    public void CutReferencesRetainIdentityOrFailAtomically(InterchangeType type,bool left,bool rht,bool mainUpper)
    {
        float hm=mainUpper?113:103,hc=mainUpper?103:113;
        var arms=Synthetic(rht,true);
        var trials=EndpointStackCore.CutCandidates(arms,type,rht,hm,hc,RealCurvedApproachTests.Ramp(),.12f,
            new CloverleafRoadDesign(),left).ToArray();
        var cores=trials.Where(c=>c.Success).ToArray();
        Assert.NotEmpty(trials);
        // These synthetic poses exercise mapping, not a promise that every
        // bridge arrangement fits. Safety rejection must expose no partial core.
        foreach(var failed in trials.Where(c=>!c.Success))
        {Assert.NotEmpty(failed.Diagnostic);Assert.Empty(failed.Segments);Assert.Empty(failed.Ports);}
        foreach(var c in cores)
        {
            // Some poses legitimately retain the straight safety fallback. Mapping
            // must be correct for both, and the curved flag must describe geometry.
            bool hasBend=c.Segments.Any(s=>s.Kind==CloverEdgeKind.Main&&Turn(s.WorldCurve)>1e-4);
            Assert.Equal(hasBend,c.CoreFollowsCurve);
            Assert.Equal(12,c.Segments.Select(s=>s.PathId).Distinct().Count());
            foreach(var path in c.Segments.GroupBy(s=>s.PathId))
            {
                var pieces=path.ToArray();
                for(int i=1;i<pieces.Length;i++)Assert.Equal(pieces[i-1].EndNode,pieces[i].StartNode);
            }
            Assert.Equal(8,c.Ports.Count);
            foreach(var port in c.Ports)
            {
                Assert.Equal(port.Arm%2==0?hm:hc,port.Position.Y,3);
                Assert.Equal(arms[port.Arm].Endpoints[0].Prefab,port.Prefab);
                float projection=Directions.Dot(port.Tangent,arms[port.Arm].Endpoints[0].OutwardTangent);
                Assert.True(port.TravelOutward?projection>.8f:projection<-.8f);
            }
        }
    }
    [Fact]
    public void StraightCutRoadsKeepTheUnwarpedCore()
    {
        var cores=EndpointStackCore.CutCandidates(Synthetic(true,false),InterchangeType.Stack,true,103,113,
            RealCurvedApproachTests.Ramp(),.16f,new CloverleafRoadDesign(),false).Where(c=>c.Success).ToArray();
        Assert.NotEmpty(cores);Assert.All(cores,c=>Assert.False(c.CoreFollowsCurve));
        Assert.All(cores.SelectMany(c=>c.Segments).Where(s=>s.Kind==CloverEdgeKind.Main),s=>Assert.InRange(Turn(s.WorldCurve),0,1e-4));
    }
    private static EndpointArm[] CutArms(InterchangeType type,bool left)
    {
        var traces=RealCurvedApproachTests.Traces();
        float[] distances=type==InterchangeType.Turbine?new[]{475f,450,450,475}
            :left?new[]{350f,375,350,350}:new[]{400f,400,400,400};
        return RealCurvedApproachTests.Arms(traces).Select(arm=>new EndpointArm(arm.Index,arm.Endpoints.Select(p=>
            RoadRetreat.Cut(traces.Single(t=>t.Origin.NodeIndex==p.NodeIndex),distances[arm.Index]).Port!).ToArray())).ToArray();
    }

    internal static double Turn(Bezier3 curve)
    {
        var a=curve.Derivative(0);var b=curve.Derivative(1);
        return Math.Abs(Math.Atan2((double)a.X*b.Z-(double)a.Z*b.X,(double)a.X*b.X+(double)a.Z*b.Z));
    }

    [Theory]
    [InlineData(InterchangeType.Stack,false)]
    [InlineData(InterchangeType.Stack,true)]
    [InlineData(InterchangeType.Turbine,false)]
    public void CurvedCutRoadsCanBendTheCoreRatherThanOnlyItsExternalConnections(InterchangeType type,bool leftExit)
    {
        float grade=type==InterchangeType.Turbine?.18f:.16f;
        var trials=EndpointStackCore.CutCandidates(CutArms(type,leftExit),type,true,488.6323f,498.6323f,
            RealCurvedApproachTests.Ramp(),grade,RealCurvedApproachTests.Design(),leftExit).ToArray();
        var cores=trials.Where(c=>c.Success).ToArray();
        Assert.NotEmpty(cores);
        Assert.True(cores.Any(c=>c.Segments.Any(s=>s.Kind==CloverEdgeKind.Main&&Turn(s.WorldCurve)>.25*Math.PI/180)),
            string.Join("\n",trials.Where(c=>!c.Success).Select(c=>c.Diagnostic)));
        foreach(var core in cores)
        {
            Assert.Equal(8,core.Ports.Count);
            Assert.Equal(8,core.Ports.Select(p=>(p.Arm,p.TravelOutward)).Distinct().Count());
            Assert.All(core.Segments,s=>Assert.Equal(VerticalProfileFailure.None,VerticalProfile.CheckGrade(s.WorldCurve,grade)));
            Assert.Equal(CurveClearanceResult.Clear,EndpointNetworkClearance.Check(
                new SpatialCandidateResult(SpatialCandidateFailure.None,core.Segments),8.5f,out _));
        }
    }
}
