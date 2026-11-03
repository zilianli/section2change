using System;
using System.Collections.Generic;
using System.Linq;
using InterchangeMod.Generation.Geometry;

namespace InterchangeMod.Generation.Endpoints
{
    /// <summary>Retain median space between the two full-width outer connections of each real arm.</summary>
    internal static class EndpointRoundaboutConnectionClearance
    {
        internal static CurveClearanceResult Check(SpatialCandidateResult candidate,IReadOnlyList<EndpointArm> arms,float clearance,out string detail)
        {
            detail="";
            foreach(var arm in arms)
            {
                var input=arm.Endpoints.Single(p=>!p.TravelOutward);var output=arm.Endpoints.Single(p=>p.TravelOutward);
                var direction=Vec3.Normalize(new Vec3(input.OutwardTangent.X,0,input.OutwardTangent.Z))
                    +Vec3.Normalize(new Vec3(output.OutwardTangent.X,0,output.OutwardTangent.Z));
                direction=Vec3.Normalize(direction);
                float gap=Math.Max(0,Math.Abs(Directions.Dot(output.Position-input.Position,Directions.RightOf(direction)))-(input.Width+output.Width)/2);
                float buffer=Math.Min(4,gap/2);if(buffer<=.001f)continue;
                var original=candidate.Segments.Where(s=>s.Prefab==input.Prefab&&s.RoadWidth==input.Width).ToArray();
                var incoming=original.Where(s=>s.WorldCurve.P0==input.Position).Select(s=>s.PathId).Distinct().ToArray();
                var outgoing=original.Where(s=>s.WorldCurve.P3==output.Position).Select(s=>s.PathId).Distinct().ToArray();
                if(incoming.Length!=1||outgoing.Length!=1||incoming[0]==outgoing[0])
                {detail="Roundabout outer roads lack distinct exact incoming/outgoing paths.";return CurveClearanceResult.InvalidInput;}
                foreach(var a in original.Where(s=>s.PathId==incoming[0]))foreach(var b in original.Where(s=>s.PathId==outgoing[0]))
                {
                    var result=CurveClearance.Check(a.WorldCurve,(input.Width+buffer)/2,b.WorldCurve,(output.Width+buffer)/2,clearance);
                    if(result!=CurveClearanceResult.Clear){detail=$"Roundabout arm {arm.Index+1} needs {buffer:F2}m median buffer: {result}";return result;}
                }
            }
            return CurveClearanceResult.Clear;
        }
    }
}
