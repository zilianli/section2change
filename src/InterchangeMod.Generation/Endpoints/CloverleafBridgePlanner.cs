using System;
using System.Collections.Generic;
using System.Linq;
using InterchangeMod.Generation.Geometry;

namespace InterchangeMod.Generation.Endpoints
{
    /// <summary>Explicit central bridge nodes, including the game's automatic split/middle support rules.</summary>
    public static class CloverleafBridgePlanner
    {
        private static float Cross(Vec3 a,Vec3 b)=>a.X*b.Z-a.Z*b.X;
        private static Vec3 Flat(Vec3 p)=>new Vec3(p.X,0,p.Z);
        private static bool Finite(float v)=>!float.IsNaN(v)&&!float.IsInfinity(v);
        public static SpatialCandidateResult Apply(SpatialCandidateResult input,IReadOnlyList<EndpointBridgeRule> rules)
        {
            SpatialCandidateResult Fail(SpatialCandidateFailure f,string detail="")=>new SpatialCandidateResult(f,Array.Empty<SpatialSegment>(),detail);
            if(input.Failure!=SpatialCandidateFailure.None)return input;
            if(rules.Count==0)return input;
            if(rules.Any(r=>r==null||r.Arm<0||r.Arm>=4||!Finite(r.MaxLength)||r.MaxLength<10||r.MaxLength>1000
                ||!Finite(r.Width)||r.Width<=0||r.Width>1000||(r.MiddleSpacing.HasValue&&(!Finite(r.MiddleSpacing.Value)||r.MiddleSpacing<0)))
                ||rules.Select(r=>r.Arm).Distinct().Count()!=rules.Count)return Fail(SpatialCandidateFailure.InvalidParameter);
            var output=new List<SpatialSegment>();
            foreach(var upper in input.Segments)
            {
                var rule=rules.FirstOrDefault(r=>r.Arm==upper.SourceArm);
                if(!upper.IsWeaving||rule==null) {output.Add(upper);continue;}
                var lowerGroups=input.Segments.Where(s=>s.Kind==CloverEdgeKind.Main&&s.SourceArm%2!=upper.SourceArm%2)
                    .GroupBy(s=>s.SourceArm).Where(g=>g.Any(s=>s.IsWeaving&&s.WorldCurve.P0.Y<upper.WorldCurve.P0.Y-1)).ToArray();
                if(lowerGroups.Length==0){output.Add(upper);continue;}
                var c=upper.WorldCurve;var start=Flat(c.P0);var direction=Vec3.Normalize(Flat(c.P3-c.P0));
                float length=Vec3.Distance(Flat(c.P0),Flat(c.P3));
                var zones=new List<(float Lo,float Hi)>();float largestCot=0;
                foreach(var group in lowerGroups)
                {
                    var roads=group.ToArray();var origin=Flat(roads[0].StartNode);
                    var v=Vec3.Normalize(Flat(roads[roads.Length-1].EndNode)-origin);
                    float sin=Math.Abs(Cross(direction,v));
                    if(sin<.02f)return Fail(SpatialCandidateFailure.PillarConflict);
                    float center=Cross(origin-start,v)/Cross(direction,v);
                    float half=roads.Max(s=>s.RoadWidth??rule.Width)/2/sin;
                    zones.Add((center-half,center+half));
                    largestCot=Math.Max(largestCot,Math.Abs(direction.X*v.X+direction.Z*v.Z)/sin);
                }
                // A transverse portal support projects into the lower road's longitudinal
                // clearance by Width*cot(angle); include the baseline's 3m column thickness.
                float effective=3+rule.Width*largestCot;
                float minimumSpacing=Math.Max(10,upper.RoadWidth??rule.Width);
                var side=new Vec3(direction.Z,0,-direction.X);
                bool SupportClear(float at,float extraClearance)
                {
                    var point=c.Evaluate(at/length);
                    var beam=Bezier3.Line(point-side*(rule.Width/2),point+side*(rule.Width/2));
                    foreach(var other in input.Segments)
                    {
                        if(other.Kind==CloverEdgeKind.Main&&other.SourceArm==upper.SourceArm)continue;
                        var q=other.WorldCurve;
                        if(Math.Min(Math.Min(q.P0.Y,q.P1.Y),Math.Min(q.P2.Y,q.P3.Y))>=point.Y-.5f)continue;
                        if(CurveClearance.Check(beam,1.5f+extraClearance,q,(other.RoadWidth??rule.Width)/2,float.MaxValue)!=CurveClearanceResult.Clear)return false;
                    }
                    return true;
                }
                List<float>? cuts=null;
                foreach(float extraMargin in new[]{3f,2f})
                {
                    var plan=PillarPlanner.PlanNodes(zones,rule.MiddleSpacing,effective,rule.MaxLength,
                        nodeClearance:effective/2+extraMargin);
                    if(plan.Error!=null||plan.Nodes.Count<2)continue;
                    var trial=new List<float>{0};
                    // Existing merge/exit nodes may already be valid bridge supports. A
                    // redundant planned support too close to them is omitted, then the
                    // resulting automatic supports are rechecked instead of assumed safe.
                    trial.AddRange(plan.Nodes.Where(x=>Finite(x)&&x>=minimumSpacing&&x<=length-minimumSpacing));
                    trial.Add(length);
                    if(trial.Zip(trial.Skip(1),(a,b)=>b-a).Any(d=>d<minimumSpacing))continue;
                    var positions=trial.ToList();
                    for(int n=0;n+1<trial.Count;n++)
                        positions.AddRange(PillarPlanner.InnerPillars(trial[n],trial[n+1],rule.MiddleSpacing,rule.MaxLength));
                    bool clear=true;
                    foreach(float at in positions)
                    {
                        clear=SupportClear(at,0);
                        if(!clear)break;
                    }
                    if(clear){cuts=trial;break;}
                }
                // If a ramp blocks the first projected solution, search alternate support
                // chains too. This bounded grid reserves an extra 1m physical clearance.
                if(cuts==null)cuts=BridgeSupportSearch.Find(length,minimumSpacing,rule.MaxLength,rule.MiddleSpacing,
                    at=>SupportClear(at,1))?.ToList();
                if(cuts==null)return Fail(SpatialCandidateFailure.PillarConflict,$"arm={upper.SourceArm+1}, span={length:F2}m: cannot place clear supports with {minimumSpacing:F0}m minimum node spacing");
                for(int n=0;n+1<cuts.Count;n++)
                {
                    // The central span is a straight level path; use shared sampled points
                    // verbatim so adjacent NetCourse endpoints merge exactly.
                    var a=n==0?c.P0:c.Evaluate(cuts[n]/length);
                    var b=n+2==cuts.Count?c.P3:c.Evaluate(cuts[n+1]/length);
                    output.Add(new SpatialSegment(upper.SourceArm,upper.Kind,Bezier3.Line(a,b),
                        n==0?upper.StartNode:a,n+2==cuts.Count?upper.EndNode:b,upper.Prefab,upper.RoadWidth,true));
                }
            }
            return new SpatialCandidateResult(SpatialCandidateFailure.None,output);
        }
    }
}
