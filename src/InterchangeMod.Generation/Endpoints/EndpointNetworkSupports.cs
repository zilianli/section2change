using System;
using System.Collections.Generic;
using System.Linq;
using InterchangeMod.Generation.Geometry;
using InterchangeMod.Generation.Roads;

namespace InterchangeMod.Generation.Endpoints
{
    public static class EndpointNetworkSupports
    {
        public static CurveClearanceResult Check(SpatialCandidateResult candidate,IReadOnlyList<EndpointBridgeRule> rules,
            RoadInfo ramp,out string detail,IReadOnlyList<EndpointPrefabSupportRule>? prefabRules=null,
            bool nativeJoinedNodes=false)
        {
            detail="";
            bool Finite(float x)=>!float.IsNaN(x)&&!float.IsInfinity(x);
            bool ValidSpacing(float? x)=>!x.HasValue||(Finite(x.Value)&&x.Value>=0);
            prefabRules=prefabRules??Array.Empty<EndpointPrefabSupportRule>();
            if(candidate==null||candidate.Failure!=SpatialCandidateFailure.None||candidate.Segments.Count==0||rules==null||ramp==null
                ||!Finite(ramp.ElevatedLength)||ramp.ElevatedLength<0||!ValidSpacing(ramp.MiddlePillarSpacing)
                ||rules.Any(r=>r==null||r.Arm<0||r.Arm>3||!Finite(r.MaxLength)||r.MaxLength<10||r.MaxLength>1000
                    ||!ValidSpacing(r.MiddleSpacing)||!Finite(r.Width)||r.Width<=0||!Finite(r.PillarWidth)||r.PillarWidth<=0)
                ||rules.Select(r=>r.Arm).Distinct().Count()!=rules.Count
                ||prefabRules.Any(r=>r==null||string.IsNullOrWhiteSpace(r.Prefab)||!Finite(r.MaxLength)||r.MaxLength<10||r.MaxLength>1000
                    ||!ValidSpacing(r.MiddleSpacing)||!Finite(r.PillarWidth)||r.PillarWidth<=0||r.PillarWidth>1000)
                ||prefabRules.Select(r=>r.Prefab).Distinct(StringComparer.Ordinal).Count()!=prefabRules.Count
                ||candidate.Segments.Any(s=>s==null||string.IsNullOrWhiteSpace(s.PathId)||!s.RoadWidth.HasValue
                    ||!Finite(s.RoadWidth.Value)||s.RoadWidth.Value<=0||VerticalProfile.CheckGrade(s.WorldCurve,.30f)!=VerticalProfileFailure.None))
            {detail="Invalid support geometry or prefab metadata";return CurveClearanceResult.InvalidInput;}
            var terminals=candidate.Segments.GroupBy(s=>s.PathId,StringComparer.Ordinal)
                .ToDictionary(g=>g.Key,g=>(First:g.First(),Last:g.Last()),StringComparer.Ordinal);
            bool AtNativeJoint(SpatialSegment upper,bool start,SpatialSegment lower)
            {
                var a=terminals[upper.PathId];var b=terminals[lower.PathId];
                if(!ReferenceEquals(upper,start?a.First:a.Last))return false;
                var node=start?upper.StartNode:upper.EndNode;
                return (ReferenceEquals(lower,b.First)&&lower.StartNode.Equals(node))
                    ||(ReferenceEquals(lower,b.Last)&&lower.EndNode.Equals(node));
            }
            int budget=200000;
            foreach(var upper in candidate.Segments)
            {
                var rule=upper.Kind==CloverEdgeKind.Main?rules.FirstOrDefault(r=>r.Arm==upper.SourceArm):null;
                var actual=prefabRules.FirstOrDefault(r=>r.Prefab==upper.Prefab);
                if(prefabRules.Count>0&&upper.Prefab!=null&&actual==null)
                {detail=$"Missing support metadata for {upper.Prefab}";return CurveClearanceResult.InvalidInput;}
                float maxLength=actual?.MaxLength??(upper.Kind==CloverEdgeKind.Main?rule?.MaxLength??80:ramp.ElevatedLength>0?ramp.ElevatedLength:80);
                float? spacing=actual!=null?actual.MiddleSpacing:upper.Kind==CloverEdgeKind.Main?rule?.MiddleSpacing:ramp.MiddlePillarSpacing;
                // Catalog's PillarWidth is the largest horizontal size, so this is a
                // conservative envelope when the prefab's orientation is unknown.
                float halfThickness=Math.Max(3,actual?.PillarWidth??(upper.Kind==CloverEdgeKind.Main?rule?.PillarWidth??3:ramp.PillarWidth))/2;
                if(maxLength<10||maxLength>1000){detail="Invalid automatic support length";return CurveClearanceResult.InvalidInput;}
                float length=upper.WorldCurve.Length(64);
                var positions=new[]{0f,length}.Concat(PillarPlanner.InnerPillars(0,length,spacing,maxLength));
                int count=0;
                foreach(float at in positions)
                {
                    if(++count>4096){detail="Support search budget exceeded";return CurveClearanceResult.Uncertain;}
                    // Invert sampled arc length instead of interpreting length as Bezier t.
                    float t=at<=0?0:at>=length?1:ParameterAt(upper.WorldCurve,at);
                    var point=upper.WorldCurve.Evaluate(t);var tangent=upper.WorldCurve.Derivative(t);
                    var side=Vec3.Normalize(new Vec3(tangent.Z,0,-tangent.X));
                    var beam=Bezier3.Line(point-side*(upper.RoadWidth!.Value/2),point+side*(upper.RoadWidth.Value/2));
                    foreach(var lower in candidate.Segments)
                    {
                        if(lower.PathId==upper.PathId)continue;
                        // Do not interpret coincident incident terminal envelopes
                        // as independent poles on a sloping native junction.
                        // Actual support ownership/combination is prefab-dependent
                        // and remains subject to the final native Temp checks.
                        // Interior supports and unrelated roads are still checked.
                        if(nativeJoinedNodes&&((at==0&&AtNativeJoint(upper,true,lower))
                            ||(at==length&&AtNativeJoint(upper,false,lower))))continue;
                        var check=Below(beam,halfThickness,lower.WorldCurve,lower.RoadWidth!.Value/2,point.Y-.5f,0,ref budget);
                        if(check==CurveClearanceResult.Clear)continue;
                        detail=$"{upper.PathId} support at {point} vs {lower.PathId}: {check}";
                        return check;
                    }
                }
            }
            return CurveClearanceResult.Clear;
        }

        private static float ParameterAt(Bezier3 curve,float length)
        {
            float low=0,high=1;
            for(int i=0;i<20;i++){float mid=(low+high)/2;if(curve.Slice(0,mid).Length(32)<length)low=mid;else high=mid;}
            return (low+high)/2;
        }
        private static CurveClearanceResult Below(Bezier3 beam,float halfThickness,Bezier3 road,float halfWidth,float ceiling,int depth,ref int budget)
        {
            if(--budget<0)return CurveClearanceResult.Uncertain;
            float min=Math.Min(Math.Min(road.P0.Y,road.P1.Y),Math.Min(road.P2.Y,road.P3.Y));
            if(min>=ceiling)return CurveClearanceResult.Clear;
            float max=Math.Max(Math.Max(road.P0.Y,road.P1.Y),Math.Max(road.P2.Y,road.P3.Y));
            var planar=CurveClearance.Check(beam,halfThickness,road,halfWidth,float.MaxValue);
            if(planar==CurveClearanceResult.Clear)return planar;
            if(max<ceiling)return planar;
            if(depth>=12)return CurveClearanceResult.Uncertain;
            var first=Below(beam,halfThickness,road.Slice(0,.5f),halfWidth,ceiling,depth+1,ref budget);
            return first!=CurveClearanceResult.Clear?first:Below(beam,halfThickness,road.Slice(.5f,1),halfWidth,ceiling,depth+1,ref budget);
        }
    }
}
