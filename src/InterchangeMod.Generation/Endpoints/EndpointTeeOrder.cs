using System;
using System.Collections.Generic;
using System.Linq;
using InterchangeMod.Generation.Geometry;

namespace InterchangeMod.Generation.Endpoints
{
    /// <summary>Recognizes the through corridor without relying on the first selected arm or road width.</summary>
    internal static class EndpointTeeOrder
    {
        public static int[]? Identify(InterchangeType type,IReadOnlyList<EndpointArm> arms)
        {
            if(type==InterchangeType.Wye||!EndpointThreeWaySearch.Supported(type)||arms.Count!=3
                ||EndpointConstraints.Validate(arms,type).Any(p=>p.Code!=EndpointProblemCode.LaneMismatch))return null;
            var ordered=arms.OrderBy(a=>a.Index).ToArray();
            Vec3 Flat(Vec3 v)=>new Vec3(v.X,0,v.Z);
            Vec3 Mid(EndpointArm a)=>a.Endpoints.Select(p=>Flat(p.Position)*(1f/a.Endpoints.Count)).Aggregate(Vec3.Zero,(v,p)=>v+p);
            var directions=ordered.Select(a=>Vec3.Normalize(a.Endpoints.Select(p=>Vec3.Normalize(Flat(p.OutwardTangent)))
                .Aggregate(Vec3.Zero,(v,d)=>v+d))).ToArray();
            if(directions.Any(d=>float.IsNaN(d.X)||float.IsNaN(d.Z)||d.Length()<.01f))return null;
            var pairs=new[]{(A:0,B:1),(A:0,B:2),(A:1,B:2)}.Select(p=>new{
                p.A,p.B,Angle=Math.Acos(Math.Max(-1,Math.Min(1,Directions.Dot(directions[p.A],directions[p.B]))))*180/Math.PI})
                .OrderByDescending(p=>p.Angle).ToArray();
            // Allow the existing 20-degree end-angle envelope at both ends, but
            // do not guess a main road in a Y-shaped or otherwise ambiguous selection.
            var main=pairs[0];
            if(main.Angle<140-1e-4||main.Angle-pairs[1].Angle<20-1e-4)return null;
            var secant=Vec3.Normalize(Mid(ordered[main.A])-Mid(ordered[main.B]));
            if(float.IsNaN(secant.X)||float.IsNaN(secant.Z)||secant.Length()<.01f||Directions.Dot(secant,directions[main.A])<=0
                ||Directions.Dot(-secant,directions[main.B])<=0)return null;
            return new[]{main.A,3-main.A-main.B,main.B};
        }
    }
}
