using System;
using InterchangeMod.Generation.Geometry;
namespace InterchangeMod.Generation.Endpoints
{
    public static class OpenEndpointChoice
    {
        public static int Nearest(Vec3 start, Vec3 end, Vec3 hit, float radius)
        {
            bool Finite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);
            if (!Finite(start.X)||!Finite(start.Z)||!Finite(end.X)||!Finite(end.Z)||!Finite(hit.X)||!Finite(hit.Z)
                ||!Finite(radius)||radius<=0) return -1;
            double D(Vec3 p) { double x=(double)p.X-hit.X,z=(double)p.Z-hit.Z; return x*x+z*z; }
            double a=D(start),b=D(end),limit=(double)radius*radius;
            if (Math.Min(a,b)>limit) return -1;
            return a<=b ? 0:1;
        }
    }
}
