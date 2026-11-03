using System;
using System.Collections.Generic;
using InterchangeMod.Generation.Geometry;
using InterchangeMod.Generation.Templates;
namespace InterchangeMod.Generation.Endpoints
{
    /// <summary>Two equal circular bends with a tangent middle, independent of legacy interchange templates.</summary>
    internal sealed class CompoundDirectPath
    {
        private readonly RampArc first,last;
        private readonly float middle;
        public CompoundDirectPath(Vec3 from,Vec3 heading,Vec3 to,Vec3 targetHeading,float sign,float radius,float middleLength,out float fromAt,out float toAt)
        {
            middle=middleLength;
            float half=Planar.RotAngle(heading,targetHeading,sign)*.5f;
            var a=new RampArc(Vec3.Zero,heading,radius,sign*half);
            var b=new RampArc(a.End+a.EndHeading*middle,a.EndHeading,radius,sign*half);
            Vec3 w=to-from-b.End;
            float Cross(Vec3 x,Vec3 y)=>x.X*y.Z-x.Z*y.X;
            float cross=Cross(heading,targetHeading);
            fromAt=Cross(w,targetHeading)/cross;toAt=Cross(w,heading)/cross;
            var start=from+heading*fromAt;
            first=new RampArc(start,heading,radius,sign*half);
            last=new RampArc(first.End+first.EndHeading*middle,first.EndHeading,radius,sign*half);
        }
        public Vec3 Start=>first.From; public Vec3 End=>last.End;
        public float Length=>first.Length+middle+last.Length;
        public float FirstBendEnd=>first.Length;
        public float LastBendStart=>first.Length+middle;
        private Vec3 At(float s)=>s<=first.Length?first.PointAt(s):s<=LastBendStart?first.End+first.EndHeading*(s-first.Length):last.PointAt(s-LastBendStart);
        private Vec3 Heading(float s)=>s<=first.Length?first.HeadingAt(s):s<=LastBendStart?first.EndHeading:last.HeadingAt(s-LastBendStart);
        public List<Bezier3> Emit(IReadOnlyList<float> cuts)
        {
            var result=new List<Bezier3>();
            for(int i=0;i+1<cuts.Count;i++)
            {
                var a=i==0?Start:At(cuts[i]);var b=i+2==cuts.Count?End:At(cuts[i+1]);
                var ha=Heading(cuts[i]);var hb=Heading(cuts[i+1]);
                float delta=(float)Math.Acos(Math.Max(-1,Math.Min(1,ha.X*hb.X+ha.Z*hb.Z)));
                float co=(float)Math.Cos(delta/4);float k=Vec3.Distance(a,b)/(3*co*co);
                result.Add(new Bezier3(a,a+ha*k,b-hb*k,b));
            }
            return result;
        }
    }
}
