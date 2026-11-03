using System;
using System.Linq;
using InterchangeMod.Generation.Geometry;
using InterchangeMod.Generation.Templates;

namespace InterchangeMod.Generation.Endpoints
{
    /// <summary>Three tangent circular bends, following the legacy cloverleaf's small-end/large-middle approach.</summary>
    internal static class SmoothDirectPath
    {
        public static RampPath Create(Vec3 from,Vec3 heading,Vec3 to,Vec3 targetHeading,float sign,
            float endRadius,float middleRadius,float startReach,float endReach,out float fromAt,out float toAt)
        {
            float theta=Planar.RotAngle(heading,targetHeading,sign);
            float Angle(float reach)=>(float)Math.Min(theta*.4,Math.Acos(Math.Max(-1,Math.Min(1,1-reach/endRadius)))+5/endRadius);
            float a=Angle(startReach),b=Angle(endReach);
            var first=new RampArc(Vec3.Zero,heading,endRadius,sign*a);
            var middle=new RampArc(first.End,first.EndHeading,middleRadius,sign*(theta-a-b));
            var last=new RampArc(middle.End,middle.EndHeading,endRadius,sign*b);
            float Cross(Vec3 x,Vec3 y)=>x.X*y.Z-x.Z*y.X;
            var w=to-from-last.End;float cross=Cross(heading,targetHeading);
            fromAt=Cross(w,targetHeading)/cross;toAt=Cross(w,heading)/cross;
            var offset=from+heading*fromAt;
            return RampPath.FromArcs(new[]{first,middle,last}.Select(r=>new RampArc(r.From+offset,r.Heading,r.Radius,r.Angle)).ToArray());
        }
    }
}
