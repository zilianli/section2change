using System;
using System.Collections.Generic;
using System.Linq;
using InterchangeMod.Generation.Geometry;

namespace InterchangeMod.Generation.Endpoints
{
    /// <summary>Fits the existing legacy corridor warp to this candidate's actual cut roads.</summary>
    internal static class EndpointCoreBend
    {
        internal static CurveWarp? Create(IReadOnlyList<EndpointArm> arms,int[] originalArm,Vec3 center,
            Vec3 right,Vec3 forward,float reflect,float crossingAngle,float strength=1)
        {
            Vec3 Local(Vec3 v)=>new Vec3(Directions.Dot(v,right)*reflect,0,Directions.Dot(v,forward));
            var lines=new Centerline[4];
            for(int i=0;i<4;i++)
            {
                var arm=arms[originalArm[i]];
                var end=Local((arm.Endpoints[0].Position*.5f+arm.Endpoints[1].Position*.5f)-center);
                var heading=Vec3.Normalize(Local(arm.Endpoints.Select(p=>Vec3.Normalize(p.OutwardTangent))
                    .Aggregate(Vec3.Zero,(a,b)=>a+b)));
                float angle=i%2==0?(i==0?0:180):crossingAngle+(i==1?0:180);
                var axis=Directions.FromAngle(angle);
                heading=Vec3.Normalize(axis*(1-strength)+heading*strength);
                float length=end.Length();
                if(!Finite(length)||length<20||Directions.Dot(end,axis)<=0||Directions.Dot(heading,axis)<=0)return null;
                var curve=new Bezier3(Vec3.Zero,axis*(length/3),end-heading*(length/3),end);
                // A positive projection of all derivative controls certifies no foldback.
                if(new[]{curve.P1-curve.P0,curve.P2-curve.P1,curve.P3-curve.P2}
                    .Any(v=>Directions.Dot(v,axis)<=0))return null;
                var line=Centerline.FromPoints(Enumerable.Range(0,257).Select(k=>curve.Evaluate(k/256f)).ToArray(),axis);
                if(line.NearlyStraight(.5f))lines[i]=Centerline.Straight;
                else
                {
                    if(CurveCurvature.CheckMinimumRadius(curve,150)!=CurveCurvatureResult.Clear)return null;
                    lines[i]=line;
                }
            }
            return new CurveWarp(crossingAngle,lines[0],lines[2],lines[1],lines[3]);
        }
        private static bool Finite(float x)=>!float.IsNaN(x)&&!float.IsInfinity(x);
    }
}
