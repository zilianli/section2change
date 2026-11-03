using System.Collections.Generic;
using System;
using System.Linq;
using InterchangeMod.Generation.Geometry;

namespace InterchangeMod.Generation.Endpoints
{
    public static class EndpointApproachGeometry
    {
        public static bool HasRetainedRoom(RoadRetreatResult cut)=>cut!=null&&cut.Failure==RoadRetreatFailure.None&&cut.Port!=null
            &&(!cut.Remainder.HasValue||RoadRetreat.HorizontalLength(cut.Remainder.Value)>=Math.Max(10,cut.Port.Width));
        public static IReadOnlyList<Bezier3>? SplitCourses(Bezier3 curve,float minimumLength,float maxGrade,Vec3 coreDirection)
        {
            if(minimumLength<10||minimumLength>70||float.IsNaN(minimumLength)||maxGrade<=0||maxGrade>.30f
                ||VerticalProfile.CheckGrade(curve,maxGrade)!=VerticalProfileFailure.None)return null;
            const int resolution=512;
            var arc=new double[resolution+1];var previous=curve.P0;
            for(int i=1;i<=resolution;i++)
            {
                var p=curve.Evaluate(i/(float)resolution);
                double x=(double)p.X-previous.X,z=(double)p.Z-previous.Z;
                arc[i]=arc[i-1]+Math.Sqrt(x*x+z*z);previous=p;
            }
            double total=arc[resolution];
            if(double.IsNaN(total)||double.IsInfinity(total)||total<minimumLength)return null;
            int first=Math.Max(1,(int)Math.Ceiling(total/70));
            int last=(int)Math.Min(128,Math.Floor(total/minimumLength));
            if(first>last)return null;
            float Parameter(double target)
            {
                int lo=0,hi=resolution;
                while(hi-lo>1){int mid=(lo+hi)/2;if(arc[mid]<target)lo=mid;else hi=mid;}
                return (float)((lo+(target-arc[lo])/(arc[hi]-arc[lo]))/resolution);
            }
            double cosine=Math.Cos(CloverleafRoadDesign.MaxApproachDeflectionDegrees*Math.PI/180);
            double Dot(Vec3 a,Vec3 b)=>(double)a.X*b.X+(double)a.Z*b.Z;
            double H(Vec3 a)=>Math.Sqrt(Dot(a,a));
            bool Gentle(Bezier3 c)
            {
                var axis=c.P3-c.P0;double length=H(axis);
                return length>0&&new[]{c.P1-c.P0,c.P2-c.P1,c.P3-c.P2}.All(v=>
                    H(v)>0&&Dot(v,coreDirection)>0&&Dot(v,axis)>=cosine*H(v)*length);
            }
            for(int count=first;count<=last;count++)
            {
                var pieces=new List<Bezier3>();float start=0;var shared=curve.P0;
                for(int i=0;i<count;i++)
                {
                    float end=i+1==count?1:Parameter(total*(i+1)/count);
                    var p=curve.Slice(start,end);var next=i+1==count?curve.P3:p.P3;
                    pieces.Add(new Bezier3(shared,p.P1,p.P2,next));shared=next;start=end;
                }
                if(pieces.All(p=>RoadRetreat.HorizontalLength(p)>=minimumLength&&p.Length(128)<=70&&Gentle(p)
                    &&VerticalProfile.CheckGrade(p,maxGrade)==VerticalProfileFailure.None))return pieces;
            }
            return null;
        }
    }
}
