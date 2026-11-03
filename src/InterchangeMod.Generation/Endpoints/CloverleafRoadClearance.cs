using System;
using System.Linq;

namespace InterchangeMod.Generation.Endpoints
{
    /// <summary>
    /// Checks independent ramp paths and through carriageways. This is a road-deck check,
    /// not certification of game-generated supports, terrain or retained roads.
    /// </summary>
    public static class CloverleafRoadClearance
    {
        public static CurveClearanceResult Check(SpatialCandidateResult spatial, PlanarCandidateResult planar,
            float rampWidth, float verticalClearance, out string detail)
        {
            detail="";
            if(spatial==null||spatial.Failure!=SpatialCandidateFailure.None||spatial.Segments.Count==0
                ||planar==null||planar.Failure!=PlanarCandidateFailure.None||planar.InputArms.Count!=4
                ||float.IsNaN(rampWidth)||float.IsInfinity(rampWidth)||rampWidth<=0
                ||float.IsNaN(verticalClearance)||float.IsInfinity(verticalClearance)||verticalClearance<=0)
                return CurveClearanceResult.InvalidInput;
            var segments=spatial.Segments;
            for(int i=0;i<segments.Count;i++)
                for(int j=i+1;j<segments.Count;j++)
                {
                    var a=segments[i];var b=segments[j];
                    // Main approaches project monotonically onto their axis (<=20 degree deflection)
                    // and stay outside the internal junctions. Ramps are non-self-intersecting analytic turns.
                    if(a.SourceArm==b.SourceArm&&a.Kind==b.Kind&&a.PathId==b.PathId)continue;
                    if(planar.Design?.HugLoops==true&&LegalSharedJunction(a,b))continue;
                    if(LegalMerge(a,b)||LegalMerge(b,a))continue;
                    float Width(SpatialSegment s)=>s.RoadWidth??(s.Kind==CloverEdgeKind.Main
                        ?planar.InputArms.First(arm=>arm.Index==s.SourceArm).Endpoints.First(p=>!p.TravelOutward).Width:rampWidth);
                    // Cloverleaf ramps occupy separate quadrants/envelopes: do not solve a ramp/deck
                    // collision by lifting it over the other ramp, whose supports could still collide.
                    float height=a.Kind!=CloverEdgeKind.Main&&b.Kind!=CloverEdgeKind.Main?float.MaxValue:verticalClearance;
                    var result=CurveClearance.Check(a.WorldCurve,Width(a)/2,b.WorldCurve,Width(b)/2,height);
                    if(result!=CurveClearanceResult.Clear)
                    {
                        detail=$"{a.SourceArm+1}/{a.Kind} {a.WorldCurve} vs {b.SourceArm+1}/{b.Kind} {b.WorldCurve}: {result}";
                        return result;
                    }
                }
            return CurveClearanceResult.Clear;

            bool LegalMerge(SpatialSegment main,SpatialSegment ramp)
            {
                if(main.Kind!=CloverEdgeKind.Main||ramp.Kind==CloverEdgeKind.Main)return false;
                var plan=planar.Ramps.First(r=>r.SourceArm==ramp.SourceArm&&r.Kind==ramp.Kind&&r.PathId==ramp.PathId);
                var path=spatial.Segments.Where(s=>s.SourceArm==ramp.SourceArm&&s.Kind==ramp.Kind&&s.PathId==ramp.PathId).ToArray();
                bool At(InterchangeMod.Generation.Geometry.Vec3 p)=>
                    InterchangeMod.Generation.Geometry.Vec3.Distance(main.StartNode,p)<.02f
                    ||InterchangeMod.Generation.Geometry.Vec3.Distance(main.EndNode,p)<.02f;
                // Only the prescribed flat first/last curve and main pieces incident to that
                // exact attachment are exempt. The rest of either path remains checked.
                return (ReferenceEquals(ramp,path[0])&&plan.RequiredFlatStart.HasValue
                        &&main.SourceArm==plan.SourceArm&&At(ramp.StartNode))
                    ||(ReferenceEquals(ramp,path[path.Length-1])&&plan.RequiredFlatEnd.HasValue
                        &&main.SourceArm==(plan.TargetArm+2)%4&&At(ramp.EndNode));
            }
            bool LegalSharedJunction(SpatialSegment a,SpatialSegment b)
            {
                if(a.Kind==CloverEdgeKind.Main||b.Kind==CloverEdgeKind.Main)return false;
                bool End(SpatialSegment s,bool start)
                {
                    var path=segments.Where(p=>p.PathId==s.PathId).ToArray();
                    return start?ReferenceEquals(path[0],s):ReferenceEquals(path[path.Length-1],s);
                }
                // Only terminal protected pieces sharing an exact 3D logical node.
                // Distinct branches farther from the split remain fully checked.
                foreach(bool startA in new[]{true,false})
                    foreach(bool startB in new[]{true,false})
                        if(End(a,startA)&&End(b,startB)&&InterchangeMod.Generation.Geometry.Vec3.Distance(
                            startA?a.StartNode:a.EndNode,startB?b.StartNode:b.EndNode)<.02f)return true;
                return false;
            }
        }
    }
}
