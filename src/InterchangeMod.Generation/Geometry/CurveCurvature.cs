using System;

namespace InterchangeMod.Generation.Geometry
{
    public enum CurveCurvatureResult { Clear,Conflict,Uncertain,InvalidInput }
    public static class CurveCurvature
    {
        public static CurveCurvatureResult CheckMinimumRadius(Bezier3 curve,float minimumRadius,int maxDepth=12,int nodeBudget=2048)
        {
            bool Finite(float v)=>!float.IsNaN(v)&&!float.IsInfinity(v);
            bool Valid(Vec3 p)=>Finite(p.X)&&Finite(p.Y)&&Finite(p.Z);
            if(!Valid(curve.P0)||!Valid(curve.P1)||!Valid(curve.P2)||!Valid(curve.P3)
                ||!Finite(minimumRadius)||minimumRadius<=0||maxDepth<0||maxDepth>20||nodeBudget<1)
                return CurveCurvatureResult.InvalidInput;
            return Check(new V(curve.P0),new V(curve.P1),new V(curve.P2),new V(curve.P3),1.0/minimumRadius,0,maxDepth,ref nodeBudget);
        }
        public static double Signed(Bezier3 curve,float t)
        {
            var d0=(new V(curve.P1)-new V(curve.P0))*3;var d1=(new V(curve.P2)-new V(curve.P1))*3;
            var d2=(new V(curve.P3)-new V(curve.P2))*3;
            return At(d0,d1,d2,t);
        }
        private static double At(V d0,V d1,V d2,double t)
        {
            double u=1-t;var v=d0*(u*u)+d1*(2*u*t)+d2*(t*t);
            var a=(d1-d0)*(2*u)+(d2-d1)*(2*t);
            double speed=Math.Sqrt(Dot(v,v));
            return speed<=1e-12?double.PositiveInfinity:Cross(v,a)/(speed*speed*speed);
        }
        private static CurveCurvatureResult Check(V p0,V p1,V p2,V p3,double limit,int depth,int maximum,ref int budget)
        {
            if(--budget<0)return CurveCurvatureResult.Uncertain;
            var d=new[]{(p1-p0)*3,(p2-p1)*3,(p3-p2)*3};
            foreach(double t in new[]{0.0,.5,1.0})
                if(Math.Abs(At(d[0],d[1],d[2],t))>limit*(1+1e-9))return CurveCurvatureResult.Conflict;
            var axis=p3-p0;double length=Math.Sqrt(Dot(axis,axis));
            if(length>1e-12)
            {
                axis=axis*(1/length);double lower=double.PositiveInfinity,upper=0;
                var a=new[]{(d[1]-d[0])*2,(d[2]-d[1])*2};
                foreach(var v in d)
                {
                    lower=Math.Min(lower,Dot(v,axis));
                    foreach(var acceleration in a)upper=Math.Max(upper,Math.Abs(Cross(v,acceleration)));
                }
                // Both Bernstein polynomials are convex combinations of their
                // controls, hence every cross product is bounded by these pairs.
                if(lower>1e-12&&upper/(lower*lower*lower)<=limit*(1-1e-9))return CurveCurvatureResult.Clear;
            }
            if(depth>=maximum)return CurveCurvatureResult.Uncertain;
            // Keep recursive de Casteljau in double precision; certifying rounded
            // float slices would not certify the original submitted curve.
            var p01=(p0+p1)*.5;var p12=(p1+p2)*.5;var p23=(p2+p3)*.5;
            var p012=(p01+p12)*.5;var p123=(p12+p23)*.5;var mid=(p012+p123)*.5;
            var left=Check(p0,p01,p012,mid,limit,depth+1,maximum,ref budget);
            if(left==CurveCurvatureResult.Conflict)return left;
            var right=Check(mid,p123,p23,p3,limit,depth+1,maximum,ref budget);
            if(right==CurveCurvatureResult.Conflict)return right;
            return left==CurveCurvatureResult.Clear&&right==CurveCurvatureResult.Clear?CurveCurvatureResult.Clear:CurveCurvatureResult.Uncertain;
        }
        private static double Dot(V a,V b)=>a.X*b.X+a.Z*b.Z;
        private static double Cross(V a,V b)=>a.X*b.Z-a.Z*b.X;
        private readonly struct V
        {
            public V(Vec3 p){X=p.X;Z=p.Z;}private V(double x,double z){X=x;Z=z;}
            public readonly double X,Z;
            public static V operator+(V a,V b)=>new V(a.X+b.X,a.Z+b.Z);
            public static V operator-(V a,V b)=>new V(a.X-b.X,a.Z-b.Z);
            public static V operator*(V a,double b)=>new V(a.X*b,a.Z*b);
        }
    }
}
