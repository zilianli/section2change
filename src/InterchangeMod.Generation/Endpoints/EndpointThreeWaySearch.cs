using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using InterchangeMod.Generation.Geometry;
using InterchangeMod.Generation.Roads;

namespace InterchangeMod.Generation.Endpoints
{
    public static class EndpointThreeWaySearch
    {
        public static RetreatSearchResult Search(IReadOnlyList<EndpointArm> arms,IReadOnlyList<RoadApproachTrace> traces,
            InterchangeType type,bool rightHandTraffic,float mainHeight,float branchHeight,RoadInfo ramp,float maxGrade,
            float maxRetreat,CloverleafRoadDesign design,bool trumpetLoopForExit=true,bool directionalTwoLevel=false,
            int maxCandidates=128,float verticalClearance=8.5f,float? terrainHeight=null)
        {
            int attempts=0,deckBad=0,supportBad=0,coreTrials=0,factoryCalls=0;
            bool hadCore=false,coreBudget=false;string detail="";
            var last=SpatialCandidateFailure.None;
            RetreatSearchResult Fail(RetreatSearchFailure f)=>new RetreatSearchResult(f,attempts,null,
                Array.Empty<EndpointArm>(),Array.Empty<RoadRetreatResult>(),Array.Empty<float>(),PlanarCandidateFailure.None,
                last,deckBad,detail,supportBad,coreMatched:hadCore);
            bool Finite(float v)=>!float.IsNaN(v)&&!float.IsInfinity(v);
            if(arms==null||traces==null||design==null||!Supported(type)||EndpointConstraints.Validate(arms,type).Count!=0
                ||arms.Any(a=>a.Endpoints.Count!=2)||ramp==null||!ramp.IsOneWay||ramp.LanesPerDirection<1
                ||!Finite(ramp.Width)||ramp.Width<=0||string.IsNullOrWhiteSpace(ramp.Name)
                ||!Finite(mainHeight)||!Finite(branchHeight)||!Finite(maxGrade)||maxGrade<=0||maxGrade>.30f
                ||!Finite(maxRetreat)||maxRetreat<0||maxRetreat>1000||maxCandidates<1||!Finite(verticalClearance)||verticalClearance<=0
                ||(terrainHeight.HasValue&&!Finite(terrainHeight.Value))
                ||(type==InterchangeType.Wye&&!terrainHeight.HasValue)
                ||(directionalTwoLevel&&(type==InterchangeType.DirectionalT||type==InterchangeType.SemiDirectionalT)&&!terrainHeight.HasValue))
                return Fail(RetreatSearchFailure.InvalidInput);
            float clearance=type==InterchangeType.Wye?Math.Max(verticalClearance,EndpointWyeCore.RequiredClearance(design,ramp)):verticalClearance;
            if(!Finite(clearance)||clearance<=0)return Fail(RetreatSearchFailure.InvalidInput);
            int budget=Math.Min(maxCandidates,128);
            var lookup=new Dictionary<(int,int),RoadApproachTrace>();
            foreach(var trace in traces)
            {
                if(trace==null||trace.Origin==null||RoadRetreat.Cut(trace,0).Failure!=RoadRetreatFailure.None
                    ||lookup.ContainsKey((trace.Origin.NodeIndex,trace.Origin.NodeVersion)))return Fail(RetreatSearchFailure.InvalidInput);
                lookup.Add((trace.Origin.NodeIndex,trace.Origin.NodeVersion),trace);
            }
            var ordered=arms.OrderBy(a=>a.Index).ToArray();
            foreach(var p in ordered.SelectMany(a=>a.Endpoints))
                if(!lookup.TryGetValue((p.NodeIndex,p.NodeVersion),out var trace)||!Same(p,trace.Origin))
                    return Fail(RetreatSearchFailure.InvalidInput);
            var windows=new List<Sample>[3];
            foreach(var arm in ordered)
            {
                windows[arm.Index]=new List<Sample>();
                float limit=Math.Max(0,Math.Min(maxRetreat,arm.Endpoints.Min(p=>lookup[(p.NodeIndex,p.NodeVersion)].Length)-10));
                for(float d=0;;d=Math.Min(limit,d+25))
                {
                    var cuts=arm.Endpoints.Select(p=>RoadRetreat.Cut(lookup[(p.NodeIndex,p.NodeVersion)],d)).ToArray();
                    if(cuts.All(EndpointApproachGeometry.HasRetainedRoom))
                        windows[arm.Index].Add(new Sample(d,new EndpointArm(arm.Index,cuts.Select(c=>c.Port!).ToArray()),cuts));
                    if(d>=limit)break;
                }
                if(windows[arm.Index].Count==0){detail=$"Arm {arm.Index+1} has no width-safe retained road window.";return Fail(RetreatSearchFailure.NoCandidate);}
            }
            string Key(Sample[] s)=>string.Join(",",s.Select(p=>p.Distance.ToString("R",CultureInfo.InvariantCulture)));
            var cache=new Dictionary<string,List<EndpointStackCoreResult>>();
            List<EndpointStackCoreResult> Cores(Sample[] samples)
            {
                string key=Key(samples);if(cache.TryGetValue(key,out var saved))return saved;
                var result=new List<EndpointStackCoreResult>();
                if(factoryCalls>=64||coreTrials>=256){coreBudget=true;return result;}
                factoryCalls++;
                var cutArms=samples.Select(s=>s.Arm).ToArray();
                var candidates=type==InterchangeType.Wye
                    ?EndpointWyeCore.Candidates(cutArms,rightHandTraffic,mainHeight,branchHeight,ramp,maxGrade,design,terrainHeight!.Value)
                    :EndpointThreeWayCore.Candidates(cutArms,type,rightHandTraffic,mainHeight,branchHeight,ramp,maxGrade,design,
                        trumpetLoopForExit,directionalTwoLevel,terrainHeight);
                using(var iterator=candidates.GetEnumerator())
                {
                    while(coreTrials<256&&iterator.MoveNext())
                    {
                        coreTrials++;var core=iterator.Current;
                        if(core.Success){hadCore=true;result.Add(core);if(result.Count==2)break;}
                        else detail=core.Diagnostic;
                    }
                }
                if(coreTrials>=256)coreBudget=true;
                cache.Add(key,result);return result;
            }
            bool LocalFit(EndpointStackCoreResult core,Sample sample)
            {
                foreach(var road in sample.Arm.Endpoints)
                {
                    var port=core.Ports.Single(p=>p.Arm==sample.Arm.Index&&p.TravelOutward==road.TravelOutward);
                    var fit=EndpointStackSearch.BuildApproach(road,port,sample.Arm.Index,"window",maxGrade,0);
                    if(fit.Failure!=SpatialCandidateFailure.None){last=fit.Failure;detail=fit.Diagnostic;return false;}
                }
                return true;
            }
            var seedSeen=new HashSet<string>();var finalSeen=new HashSet<string>();
            for(float progress=0;;progress=Math.Min(maxRetreat,progress+50))
            {
                var seed=windows.Select(w=>w.FirstOrDefault(s=>s.Distance>=Math.Min(progress,w[w.Count-1].Distance))??w[w.Count-1]).ToArray();
                if(seedSeen.Add(Key(seed)))foreach(var seedCore in Cores(seed))
                {
                    // Solve each real arm window independently, then regenerate a
                    // core from those exact cuts before any complete-network check.
                    var selected=new Sample[3];bool fits=true;
                    for(int i=0;i<3;i++)
                    {
                        var pick=windows[i].FirstOrDefault(s=>s.Distance>=seed[i].Distance&&LocalFit(seedCore,s));
                        if(pick==null){fits=false;break;}selected[i]=pick;
                    }
                    if(!fits||!finalSeen.Add(Key(selected)))continue;
                    var cutArms=selected.Select(s=>s.Arm).ToArray();
                    var cuts=selected.SelectMany(s=>s.Cuts).ToArray();var distances=selected.Select(s=>s.Distance).ToArray();
                    foreach(var core in Cores(selected))foreach(float handle in new[]{0f,.25f,.4f})
                    {
                        if(attempts>=budget)return Fail(RetreatSearchFailure.BudgetExceeded);
                        attempts++;
                        var candidate=EndpointCoreAttachment.Attach(core,cutArms,maxGrade,handle);
                        if(candidate.Failure!=SpatialCandidateFailure.None){last=candidate.Failure;detail=candidate.Diagnostic;continue;}
                        var original=new HashSet<SpatialSegment>(core.Segments);
                        if(candidate.Segments.Where(s=>!original.Contains(s)).Any(s=>
                            CurveCurvature.CheckMinimumRadius(s.WorldCurve,2*s.RoadWidth!.Value)!=CurveCurvatureResult.Clear))
                        {last=SpatialCandidateFailure.InvalidGeometry;detail="An external connection is too tight to certify.";continue;}
                        var deck=EndpointNetworkClearance.Check(candidate,clearance,out detail);
                        if(deck!=CurveClearanceResult.Clear){deckBad++;last=deck==CurveClearanceResult.Uncertain?SpatialCandidateFailure.Uncertain:SpatialCandidateFailure.None;continue;}
                        var support=EndpointNetworkSupports.Check(candidate,design.BridgeRules,ramp,out detail,design.SupportRules);
                        if(support!=CurveClearanceResult.Clear){supportBad++;last=SpatialCandidateFailure.PillarConflict;continue;}
                        return new RetreatSearchResult(RetreatSearchFailure.None,attempts,candidate,cutArms,cuts,distances,
                            PlanarCandidateFailure.None,SpatialCandidateFailure.None,deckBad,"",supportBad,coreMatched:true,
                            referenceCenter:core.ReferenceCenter,referenceFromCuts:true);
                    }
                }
                if(coreBudget)return Fail(RetreatSearchFailure.BudgetExceeded);
                if(progress>=maxRetreat)break;
            }
            return Fail(RetreatSearchFailure.NoCandidate);
        }
        public static bool Supported(InterchangeType type)=>type==InterchangeType.Trumpet
            ||type==InterchangeType.DirectionalT||type==InterchangeType.SemiDirectionalT||type==InterchangeType.Wye;
        private static bool Same(SelectedEndpoint a,SelectedEndpoint b)=>a.NodeIndex==b.NodeIndex&&a.NodeVersion==b.NodeVersion
            &&a.Position==b.Position&&a.OutwardTangent==b.OutwardTangent&&a.OneWay==b.OneWay&&a.TravelOutward==b.TravelOutward
            &&a.Prefab==b.Prefab&&a.Width==b.Width&&a.Lanes==b.Lanes;
        private sealed class Sample
        {
            internal Sample(float distance,EndpointArm arm,RoadRetreatResult[] cuts){Distance=distance;Arm=arm;Cuts=cuts;}
            internal float Distance {get;}internal EndpointArm Arm {get;}internal RoadRetreatResult[] Cuts {get;}
        }
    }
}
