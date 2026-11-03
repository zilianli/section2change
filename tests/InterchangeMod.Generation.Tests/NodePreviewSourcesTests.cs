using InterchangeMod.Generation.Endpoints;
using InterchangeMod.Generation.Geometry;

namespace InterchangeMod.Generation.Tests;

public class NodePreviewSourcesTests
{
    private static RoadApproachTrace Trace(int id, bool oneWay)
    {
        var origin=new SelectedEndpoint(id,1,new Vec3(id*10,0,0),Vec3.UnitZ,oneWay,false,
            oneWay?12:24,2,oneWay?"Dual":"TwoWay");
        return new RoadApproachTrace(origin,new[]{new RoadTracePiece(id+1000,1,
            Bezier3.Line(origin.Position,origin.Position+Vec3.UnitZ*200),false,id+2000,1)});
    }

    [Fact]
    public void CutOrderBindsBackToOriginalMixedSourcesInsteadOfDisplacedSolverPorts()
    {
        var twoWay=Trace(1,false);var incoming=Trace(2,true);var outgoing=Trace(3,true);
        var cuts=new[]{RoadRetreat.Cut(outgoing,60),RoadRetreat.Cut(twoWay,40),RoadRetreat.Cut(incoming,50)};
        var sources=NodePreviewSources.Bind(new[]{twoWay,incoming,outgoing},cuts);
        Assert.Same(outgoing,sources[0]);Assert.Same(twoWay,sources[1]);Assert.Same(incoming,sources[2]);
        Assert.False(sources[1].Origin.OneWay);
        Assert.Equal(new Vec3(10,0,0),sources[1].Origin.Position);
        Assert.True(NodePlacementSnapshot.Capture(sources,cuts).Matches(sources,cuts));
    }

    [Fact]
    public void SyntheticCorridorCannotReplaceTheActualTwoWaySource()
    {
        var actual=Trace(1,false);var proxy=Trace(999,true);
        Assert.Throws<ArgumentException>(()=>NodePreviewSources.Bind(new[]{actual},new[]{RoadRetreat.Cut(proxy,40)}));
    }

    [Fact]
    public void MatchingEntityIdentityDoesNotPermitDifferentGeometryOrMissingSources()
    {
        var actual=Trace(1,false);
        var shifted=new SelectedEndpoint(1,1,new Vec3(11,0,0),Vec3.UnitZ,false,false,24,2,"TwoWay");
        var proxy=new RoadApproachTrace(shifted,new[]{new RoadTracePiece(1001,1,
            Bezier3.Line(shifted.Position,shifted.Position+Vec3.UnitZ*200),false,2001,1)});
        Assert.Throws<ArgumentException>(()=>NodePreviewSources.Bind(new[]{actual},new[]{RoadRetreat.Cut(proxy,40)}));
        Assert.Throws<ArgumentException>(()=>NodePreviewSources.Bind(new[]{actual,Trace(2,true)},new[]{RoadRetreat.Cut(actual,40)}));
        Assert.Throws<ArgumentException>(()=>NodePreviewSources.Bind(new[]{actual,actual},new[]{RoadRetreat.Cut(actual,40),RoadRetreat.Cut(actual,50)}));
    }
}
