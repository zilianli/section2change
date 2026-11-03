using System;
using System.Collections.Generic;
using System.Linq;
using InterchangeMod.Generation.Geometry;

namespace InterchangeMod.Generation.Endpoints
{
    /// <summary>Orders the near-orthogonal compact approaches by bearing, keeping the first selected direction.</summary>
    internal static class EndpointGridOrder
    {
        public static int[]? Identify(InterchangeType type,IReadOnlyList<EndpointArm> arms)
        {
            if(type!=InterchangeType.CompactGrid||arms.Count!=4
                ||EndpointConstraints.Validate(arms,type).Any(p=>p.Code!=EndpointProblemCode.LaneMismatch))return null;
            var selected=arms.OrderBy(a=>a.Index).ToArray();
            Vec3 Flat(Vec3 p)=>new Vec3(p.X,0,p.Z);
            var directions=selected.Select(a=>Vec3.Normalize(a.Endpoints.Select(p=>Vec3.Normalize(Flat(p.OutwardTangent)))
                .Aggregate(Vec3.Zero,(v,d)=>v+d))).ToArray();
            if(directions.Any(d=>float.IsNaN(d.X)||float.IsNaN(d.Z)||d.Length()<.99f))return null;
            double Bearing(Vec3 d)=>Math.Atan2(d.X,d.Z);
            double origin=Bearing(directions[0]);
            var ordered=directions.Select((d,index)=>new{Index=index,Angle=(Bearing(d)-origin+2*Math.PI)%(2*Math.PI)})
                .OrderBy(a=>a.Angle).ToArray();
            for(int i=0;i<4;i++)
            {
                double gap=((ordered[(i+1)%4].Angle-ordered[i].Angle+2*Math.PI)%(2*Math.PI))*180/Math.PI;
                if(gap<80-.0001||gap>100+.0001)return null;
            }
            return ordered.Select(a=>a.Index).ToArray();
        }
    }
}
