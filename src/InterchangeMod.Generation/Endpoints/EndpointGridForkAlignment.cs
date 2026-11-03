using System;
using System.Collections.Generic;
using System.Linq;
using InterchangeMod.Generation.Geometry;

namespace InterchangeMod.Generation.Endpoints
{
    /// <summary>Rebind actual edge-aligned fork surfaces after a nonlinear corridor warp.</summary>
    internal static class EndpointGridForkAlignment
    {
        internal static List<SpatialSegment>? Rebind(IReadOnlyList<SpatialSegment> source,bool rht,out string detail)
        {
            detail="";var output=source.ToList();
            var forks=source.SelectMany((s,index)=>new[]{(Node:s.StartNode,Index:index,Start:true),(Node:s.EndNode,Index:index,Start:false)})
                .GroupBy(e=>e.Node).Where(g=>g.Count()==3&&g.Any(e=>source[e.Index].Kind==CloverEdgeKind.Main)).ToArray();
            if(forks.Length!=8){detail="Curved grid needs eight unambiguous full-road forks.";return null;}
            foreach(var fork in forks)
            {
                // 原路是方向与另外两段相反的那一段（驶入分叉：原路在此结束、两条窄路在此开始；驶出相反）。
                // 不能按最宽来认：双向拆成的双幅一幅可能和直行道、集散道一样宽（游戏内 16 米 Large Road Divided）。
                var odd=fork.GroupBy(e=>e.Start).FirstOrDefault(g=>g.Count()==1);
                var fullEnd=odd!=null?odd.First():fork.OrderByDescending(e=>source[e.Index].RoadWidth).First();
                var full=source[fullEnd.Index];var c=full.WorldCurve;
                var position=fullEnd.Start?c.P0:c.P3;
                if(full.Kind!=CloverEdgeKind.Main||position!=fork.Key)
                {detail="Curved grid full-road fork must retain its exact logical node.";return null;}
                var tangent=Vec3.Normalize(c.Derivative(fullEnd.Start?0:1));
                var side=Vec3.Normalize(new Vec3(tangent.Z,0,-tangent.X))*(rht?1:-1);
                foreach(var end in fork.Where(e=>e.Index!=fullEnd.Index))
                {
                    var road=output[end.Index];float drop=(full.RoadWidth!.Value-road.RoadWidth!.Value)/2;
                    if(drop<0||road.Kind!=CloverEdgeKind.Main&&road.Kind!=CloverEdgeKind.DirectRamp)
                    {detail="Curved grid fork has incompatible reduced-road metadata.";return null;}
                    var point=fork.Key+side*(road.Kind==CloverEdgeKind.Main?drop:-drop);
                    var old=road.WorldCurve;
                    var curve=end.Start
                        ?new Bezier3(point,point+tangent*(old.P1-old.P0).Length(),old.P2,old.P3)
                        :new Bezier3(old.P0,old.P1,point-tangent*(old.P3-old.P2).Length(),point);
                    output[end.Index]=new SpatialSegment(road.SourceArm,road.Kind,curve,road.StartNode,road.EndNode,
                        road.Prefab,road.RoadWidth,road.IsWeaving,road.PathId);
                }
            }
            return output;
        }
    }
}
