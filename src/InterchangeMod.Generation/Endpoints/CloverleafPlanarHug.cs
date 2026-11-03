using System;
using System.Collections.Generic;
using System.Linq;
using InterchangeMod.Generation.Geometry;
using InterchangeMod.Generation.Templates;

namespace InterchangeMod.Generation.Endpoints
{
    public static partial class PlanarCloverleafCandidate
    {
        private static PlanarCandidateFailure CreateHugRamps(Line[] lines,EndpointArm[] selected,List<PlanarRamp> ramps,
            bool rht,float loopRadius,float connectorRadius,float rampWidth,float sideGap,float minimumFlat,CloverleafRoadDesign design)
        {
            var original=ramps.ToArray();ramps.Clear();
            float sharedWidth=design.SharedRampWidth;
            Vec3 Origin(int i)=>lines[i].Start+new Vec3(lines[i].Direction.Z,0,-lines[i].Direction.X)*(rht?1:-1)
                *Math.Max(0,(selected[i].Endpoints.First(p=>!p.TravelOutward).Width-rampWidth)/2);
            foreach(var loopPlan in original)
            {
                int b=loopPlan.SourceArm,a=(loopPlan.TargetArm+2)%4;
                var loop=RampPath.Turn(Origin(b),lines[b].Direction,Origin(a),lines[a].Direction,rht?-1:1,loopRadius,out _,out _).Arcs[0];
                if(!EndpointHugGeometry.TryCreate(loop,Origin(a),lines[a].Direction,Origin(b),lines[b].Direction,
                    connectorRadius,sharedWidth,out var h)||h==null)return PlanarCandidateFailure.MergeCannotClear;
                if(h.OnA<10||h.OnA>lines[a].Length-10||h.OnB<10||h.OnB>lines[b].Length-10)
                    return PlanarCandidateFailure.AttachmentsOutsideApproach;
                float MainFlat(int arm)
                {
                    float width=selected[arm].Endpoints.First(p=>!p.TravelOutward).Width;
                    double reach=(width+rampWidth)/2+sideGap-Math.Max(0,(width-rampWidth)/2);
                    return reach>=2*connectorRadius?float.PositiveInfinity:
                        Math.Max(minimumFlat,Math.Max(10,(float)(connectorRadius*Math.Acos(1-reach/connectorRadius))+5));
                }
                Vec3 nodeA=lines[a].Start+lines[a].Direction*h.OnA,nodeB=lines[b].Start+lines[b].Direction*h.OnB;
                float levelB=b%2,levelA=a%2;
                float Factor(float s)
                {
                    float start=loopPlan.RequiredFlatStart??0,end=loopPlan.RequiredFlatEnd??0;
                    float t=Math.Max(0,Math.Min(1,(s-start)/(loop.Length-start-end)));
                    return levelB+(levelA-levelB)*t*t*(3-2*t);
                }
                float factorA=Factor(h.SplitAlongLoopA),factorB=Factor(h.SplitAlongLoopB);
                float GradeFactor(float s)
                {
                    float start=loopPlan.RequiredFlatStart??0,end=loopPlan.RequiredFlatEnd??0;
                    float span=loop.Length-start-end,t=Math.Max(0,Math.Min(1,(s-start)/span));
                    return (levelA-levelB)*6*t*(1-t)/span;
                }
                float gradeA=GradeFactor(h.SplitAlongLoopA),gradeB=GradeFactor(h.SplitAlongLoopB);
                // Each physical path is emitted once. The shared centre is midway
                // between inner loop and outer right-turn lane centre lines.
                bool Add(string id,int source,int target,CloverEdgeKind kind,RampPath path,Vec3 startNode,Vec3 endNode,
                    float startLevel,float endLevel,float protectStart,float protectEnd,float flatStart,float flatEnd,
                    float startGradeFactor,float endGradeFactor,bool shared=false)
                {
                    float length=path.Length;
                    if(!Finite(length)||!Finite(protectStart)||!Finite(protectEnd)||protectStart<10||protectEnd<10
                        ||length-protectStart-protectEnd<10)return false;
                    float maxPiece=Math.Min(70,path.Arcs[0].Radius*(float)Math.PI/2);
                    if(protectStart>maxPiece||protectEnd>maxPiece)return false;
                    float middle=length-protectStart-protectEnd;
                    int count=(int)Math.Ceiling(middle/maxPiece);
                    if(count<1||middle/count<10)return false;
                    var cuts=new List<float>{0,protectStart};
                    for(int n=1;n<=count;n++)cuts.Add(protectStart+middle*n/count);
                    cuts.Add(length);
                    var specs=new List<RoadSegmentSpec>();
                    path.EmitAt(specs,id,RoadRole.Ramp,cuts,new float[cuts.Count],null,path.Start,path.End);
                    ramps.Add(new PlanarRamp(source,target,kind,0,0,Math.Abs(path.Arcs[0].Angle),specs.Select(s=>s.Curve),
                        specs[0].Curve.Length(64),specs[specs.Count-1].Curve.Length(64),startNode,endNode,id,
                        shared?design.SharedRampPrefab:null,shared?sharedWidth:rampWidth,startLevel,endLevel,
                        flatStart>0?specs[0].Curve.Length(64):0,flatEnd>0?specs[specs.Count-1].Curve.Length(64):0,
                        startGradeFactor,endGradeFactor));
                    return true;
                }
                // Never squeeze a shared-node protection piece to 10-15m to make
                // a candidate fit. Game logs show InvalidShape on those short pieces.
                // This is geometric overlap protection, not a level platform.
                float splitProtection=Math.Max(20,sharedWidth*2);
                float loopFlatB=loopPlan.RequiredFlatStart??10,loopFlatA=loopPlan.RequiredFlatEnd??10;
                float directFlatA=MainFlat(a),directFlatB=MainFlat(b);
                string key=$"Hug{a}-";
                if(!Add(key+"LoopIn",b,(a+2)%4,CloverEdgeKind.LoopRamp,h.LoopIn,loopPlan.StartNode,h.SplitB,
                    levelB,factorB,loopFlatB,splitProtection,loopFlatB,0,0,gradeB)
                    ||!Add(key+"LoopOut",b,(a+2)%4,CloverEdgeKind.LoopRamp,h.LoopOut,h.SplitA,loopPlan.EndNode,
                    factorA,levelA,splitProtection,loopFlatA,0,loopFlatA,gradeA,0)
                    ||!Add(key+"Shared",a,(b+2)%4,CloverEdgeKind.SharedRamp,h.Shared,h.SplitA,h.SplitB,
                    factorA,factorB,splitProtection,splitProtection,0,0,-gradeA,-gradeB,true)
                    ||!Add(key+"DirectIn",a,(b+2)%4,CloverEdgeKind.DirectRamp,h.ConnA,nodeA,h.SplitA,
                    levelA,factorA,directFlatA,splitProtection,directFlatA,0,0,-gradeA)
                    ||!Add(key+"DirectOut",a,(b+2)%4,CloverEdgeKind.DirectRamp,h.ConnB,h.SplitB,nodeB,
                    factorB,levelB,splitProtection,directFlatB,0,directFlatB,-gradeB,0))return PlanarCandidateFailure.MergeCannotClear;
                lines[a].Junctions[0]=new PlanarJunction(new CloverNode(a,CloverNodeKind.DirectExit),nodeA,h.OnA);
                lines[b].Junctions[3]=new PlanarJunction(new CloverNode(b,CloverNodeKind.DirectMerge),nodeB,h.OnB);
                var directIn=ramps[ramps.Count-2];var directOut=ramps[ramps.Count-1];
                lines[a].ProtectedOverlaps.Add(new PlanarOverlap(new CloverNode(a,CloverNodeKind.DirectExit),h.OnA,
                    Dot(directIn.Curves[0].P3-lines[a].Start,lines[a].Direction)));
                lines[b].ProtectedOverlaps.Add(new PlanarOverlap(new CloverNode(b,CloverNodeKind.DirectMerge),
                    Dot(directOut.Curves[directOut.Curves.Count-1].P0-lines[b].Start,lines[b].Direction),h.OnB));
            }
            return PlanarCandidateFailure.None;
        }
    }
}
