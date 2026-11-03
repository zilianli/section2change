using InterchangeMod.Generation.Endpoints;
using InterchangeMod.Generation.Geometry;

namespace InterchangeMod.Generation.Tests;

public class GridForkAlignmentTests
{
    [Theory]
    [InlineData(true,4)] [InlineData(false,4)]
    [InlineData(true,6)] [InlineData(false,6)]
    public void ReducedRoadsShareTheLogicalForkButMatchOppositeOuterEdges(bool rht,int lanes)
    {
        var arms=CompactGridFixtures.Arms(rht,lanes:lanes);
        var setup=EndpointCompactGridSetup.Create(arms,CompactGridFixtures.Catalog(),0,0,93);
        var core=EndpointCompactGridCore.Create(arms,rht,setup.BaseHeight,setup.LevelGap,setup.Ramp!,setup.Roads,.16f,setup.Design!,93);
        Assert.True(core.Success,core.Diagnostic);
        AssertAligned(core.Segments,rht);
    }

    internal static void AssertAligned(IReadOnlyList<SpatialSegment> segments,bool rht)
    {
        var endpoints=segments.SelectMany(s=>new[]{(Node:s.StartNode,Point:s.WorldCurve.P0,Tangent:s.WorldCurve.Derivative(0),Segment:s),
            (Node:s.EndNode,Point:s.WorldCurve.P3,Tangent:s.WorldCurve.Derivative(1),Segment:s)});
        var forks=endpoints.GroupBy(e=>e.Node).Where(g=>g.Count()==3&&g.Any(e=>e.Segment.Kind==CloverEdgeKind.Main)).ToArray();
        Assert.Equal(8,forks.Length);
        foreach(var fork in forks)
        {
            var full=fork.OrderByDescending(e=>e.Segment.RoadWidth).First();
            var through=Assert.Single(fork.Where(e=>e.Segment.Kind==CloverEdgeKind.Main&&e.Segment.RoadWidth<full.Segment.RoadWidth));
            var collector=Assert.Single(fork.Where(e=>e.Segment.Kind==CloverEdgeKind.DirectRamp));
            var side=Directions.RightOf(Vec3.Normalize(new Vec3(full.Tangent.X,0,full.Tangent.Z)))*(rht?1:-1);
            float dropThrough=(full.Segment.RoadWidth!.Value-through.Segment.RoadWidth!.Value)/2;
            float dropCollector=(full.Segment.RoadWidth!.Value-collector.Segment.RoadWidth!.Value)/2;
            Assert.Equal(fork.Key,full.Point);
            Assert.InRange(Math.Abs(Directions.Dot(through.Point-fork.Key,side)-dropThrough),0,.001f);
            Assert.InRange(Math.Abs(Directions.Dot(collector.Point-fork.Key,side)+dropCollector),0,.001f);
            var heading=Vec3.Normalize(new Vec3(full.Tangent.X,0,full.Tangent.Z));
            Assert.InRange(Math.Abs(Directions.Dot(through.Point-fork.Key,heading)),0,.002f);
            Assert.InRange(Math.Abs(Directions.Dot(collector.Point-fork.Key,heading)),0,.002f);
            Assert.Equal(fork.Key.Y,through.Point.Y);Assert.Equal(fork.Key.Y,collector.Point.Y);
            Assert.True(Vec3.Distance(through.Point,collector.Point)>1,"Two reduced roads must not occupy the same centreline at their common node.");
            // Endpoint course rotations remain tangent to the original full road;
            // lane alignment is encoded by curve positions, not by moving the node.
            Assert.True(Directions.Dot(Vec3.Normalize(full.Tangent),Vec3.Normalize(through.Tangent))>.9999f);
            Assert.True(Directions.Dot(Vec3.Normalize(full.Tangent),Vec3.Normalize(collector.Tangent))>.9999f);
        }
    }
}
