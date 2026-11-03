using System;
using System.Collections.Generic;
using System.Linq;
using InterchangeMod.Generation.Geometry;

namespace InterchangeMod.Generation.Endpoints
{
    /// <summary>Keep part of the existing median as room for native road-node shapes.</summary>
    internal static class EndpointGridConnectionClearance
    {
        internal static CurveClearanceResult Check(SpatialCandidateResult candidate,IReadOnlyList<EndpointArm> original,
            float verticalClearance,out string detail,string mainPrefix="GridMain")
        {
            detail="";
            if(candidate==null||candidate.Failure!=SpatialCandidateFailure.None||original==null||original.Count!=4)
                return CurveClearanceResult.InvalidInput;
            var arms=original.OrderBy(a=>a.Index).ToArray();
            float Gap(EndpointArm arm)
            {
                var a=arm.Endpoints.Single(p=>!p.TravelOutward);var b=arm.Endpoints.Single(p=>p.TravelOutward);
                var direction=Vec3.Normalize(new Vec3(a.OutwardTangent.X,0,a.OutwardTangent.Z))
                    +Vec3.Normalize(new Vec3(b.OutwardTangent.X,0,b.OutwardTangent.Z));
                direction=Vec3.Normalize(direction);var delta=b.Position-a.Position;
                return Math.Max(0,Math.Abs(Directions.Dot(delta,Directions.RightOf(direction)))-(a.Width+b.Width)/2);
            }
            for(int i=0;i<2;i++)
            {
                // Main i travels into arm i and out of its opposed arm. The paired
                // path does the reverse. Select only their original full-width
                // courses, never the legal split/merge of reduced internal roads.
                float buffer=Math.Min(4,Math.Min(Gap(arms[i]),Gap(arms[i+2]))/2);
                if(buffer<=.001f)continue;
                string name=arms[i].Endpoints[0].Prefab;float width=arms[i].Endpoints[0].Width;
                var a=candidate.Segments.Where(s=>s.PathId==mainPrefix+i&&s.Prefab==name&&s.RoadWidth==width).ToArray();
                var b=candidate.Segments.Where(s=>s.PathId==mainPrefix+(i+2)&&s.Prefab==name&&s.RoadWidth==width).ToArray();
                foreach(var x in a)foreach(var y in b)
                {
                    var check=CurveClearance.Check(x.WorldCurve,(width+buffer)/2,y.WorldCurve,(width+buffer)/2,verticalClearance);
                    if(check!=CurveClearanceResult.Clear)
                    {
                        detail=$"{mainPrefix} opposing full-road connections need {buffer:F2}m of median buffer: {x.PathId} vs {y.PathId}: {check}";
                        return check;
                    }
                }
            }
            return CurveClearanceResult.Clear;
        }
    }
}
