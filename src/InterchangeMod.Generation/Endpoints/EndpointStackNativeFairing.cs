using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using InterchangeMod.Generation.Geometry;
using InterchangeMod.Generation.Roads;

namespace InterchangeMod.Generation.Endpoints
{
    // Keep all joins and support nodes. Replace alternating circular-arc handles
    // with a clamped C2 horizontal spline, without shortcutting through left ramps.
    internal static class EndpointStackNativeFairing
    {
        internal static SpatialCandidateResult Improve(SpatialCandidateResult baseline,
            IReadOnlyList<RoadInfo> catalog,CloverleafRoadDesign design,RoadInfo ramp,
            float grade,float clearance,int budget,out int attempts,CancellationToken cancellation=default,Action<string>? diagnostic=null)
        {
            attempts=0;var best=baseline;
            var paths=baseline.Segments.Where(s=>s.Kind==CloverEdgeKind.DirectRamp&&s.PathId.StartsWith("Right",StringComparison.Ordinal))
                .Select(s=>s.PathId).Distinct().ToArray();
            foreach(var path in paths)
            {
                cancellation.ThrowIfCancellationRequested();
                if(attempts>=Math.Min(4,budget))break;
                var original=best.Segments.Where(s=>s.PathId==path).ToArray();
                if(original.Length<2||original.Any(s=>s.Prefab!=original[0].Prefab||s.RoadWidth!=original[0].RoadWidth)
                    ||original.Zip(original.Skip(1),(a,b)=>a.EndNode==b.StartNode&&a.WorldCurve.P3==b.WorldCurve.P0).Any(same=>!same))continue;
                attempts++;
                var road=catalog.Single(r=>r.Name==original[0].Prefab);
                float maximum=Math.Min(EndpointMixedRoadSetup.Span(road),design.SupportRules.FirstOrDefault(r=>r.Prefab==road.Name)?.MaxLength??80);
                var curves=Spline(original);
                float limit=road.MaxSlope>0?Math.Min(grade,road.MaxSlope):grade;
                if(curves.Any(c=>VerticalProfile.CheckGrade(c,limit)!=VerticalProfileFailure.None
                    ||CurveCurvature.CheckMinimumRadius(c,2*road.Width)!=CurveCurvatureResult.Clear
                    ||new[]{c.P1-c.P0,c.P2-c.P1,c.P3-c.P2}.Any(v=>v.X*(c.P3.X-c.P0.X)+v.Z*(c.P3.Z-c.P0.Z)<=0)
                    ||Vec3.Distance(c.P0,c.P1)+Vec3.Distance(c.P1,c.P2)+Vec3.Distance(c.P2,c.P3)>maximum))
                {diagnostic?.Invoke($"fair {path}: local certificate failed");continue;}
                if(Jump(curves)>=Jump(original.Select(s=>s.WorldCurve).ToArray())*.5)continue;
                var replacement=original.Select((s,i)=>new SpatialSegment(s.SourceArm,s.Kind,curves[i],s.StartNode,s.EndNode,
                    s.Prefab,s.RoadWidth,s.IsWeaving,s.PathId));
                var candidate=new SpatialCandidateResult(SpatialCandidateFailure.None,best.Segments.Where(s=>s.PathId!=path).Concat(replacement));
                if(EndpointNetworkClearance.Check(candidate,clearance,out var detail)!=CurveClearanceResult.Clear)
                {diagnostic?.Invoke($"fair {path}: {detail}");continue;}
                cancellation.ThrowIfCancellationRequested();
                if(EndpointNetworkSupports.Check(candidate,design.BridgeRules,ramp,out detail,design.SupportRules,nativeJoinedNodes:true)!=CurveClearanceResult.Clear)
                {diagnostic?.Invoke($"fair {path}: {detail}");continue;}
                best=candidate;diagnostic?.Invoke($"fair {path}: accepted, curvature jump {Jump(curves):F7}");
            }
            cancellation.ThrowIfCancellationRequested();return best;
        }

        private static Bezier3[] Spline(SpatialSegment[] segments)
        {
            int n=segments.Length;
            var points=segments.Select(s=>s.WorldCurve.P0).Append(segments[n-1].WorldCurve.P3).ToArray();
            var h=segments.Select(s=>RoadRetreat.HorizontalLength(s.WorldCurve)).ToArray();
            var a=new double[n+1];var b=new double[n+1];var c=new double[n+1];
            var x=new double[n+1];var z=new double[n+1];b[0]=b[n]=1;
            var first=segments[0].WorldCurve.Derivative(0)*(float)(1/h[0]);var last=segments[n-1].WorldCurve.Derivative(1)*(float)(1/h[n-1]);
            x[0]=first.X;z[0]=first.Z;x[n]=last.X;z[n]=last.Z;
            for(int i=1;i<n;i++)
            {
                double prev=h[i-1],next=h[i];a[i]=next;b[i]=2*(prev+next);c[i]=prev;
                x[i]=3*(next/prev*(points[i].X-points[i-1].X)+prev/next*(points[i+1].X-points[i].X));
                z[i]=3*(next/prev*(points[i].Z-points[i-1].Z)+prev/next*(points[i+1].Z-points[i].Z));
            }
            for(int i=1;i<=n;i++){double factor=a[i]/b[i-1];b[i]-=factor*c[i-1];x[i]-=factor*x[i-1];z[i]-=factor*z[i-1];}
            x[n]/=b[n];z[n]/=b[n];
            for(int i=n-1;i>=0;i--){x[i]=(x[i]-c[i]*x[i+1])/b[i];z[i]=(z[i]-c[i]*z[i+1])/b[i];}
            return segments.Select((s,i)=>{
                var old=s.WorldCurve;float scale=(float)(h[i]/3);
                var p1=i==0?old.P1:new Vec3(old.P0.X+(float)x[i]*scale,old.P1.Y,old.P0.Z+(float)z[i]*scale);
                var p2=i+1==n?old.P2:new Vec3(old.P3.X-(float)x[i+1]*scale,old.P2.Y,old.P3.Z-(float)z[i+1]*scale);
                return new Bezier3(old.P0,p1,p2,old.P3);
            }).ToArray();
        }

        private static double Jump(Bezier3[] curves)=>curves.Zip(curves.Skip(1),(a,b)=>
            Math.Abs(CurveCurvature.Signed(a,1)-CurveCurvature.Signed(b,0))).DefaultIfEmpty(0).Max();
    }
}
