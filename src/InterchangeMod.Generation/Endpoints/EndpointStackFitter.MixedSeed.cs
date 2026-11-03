using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using InterchangeMod.Generation.Geometry;
using InterchangeMod.Generation.Roads;

namespace InterchangeMod.Generation.Endpoints
{
    public static partial class EndpointStackFitter
    {
        // Only strict mixed-road callers enter here, after a valid original
        // reference has no solution. Cut poses are not gated by that rigid seed.
        private static RetreatSearchResult FindCutSeed(IReadOnlyList<EndpointArm> arms,IReadOnlyList<RoadApproachTrace> traces,
            InterchangeType type,bool rht,float main,float cross,RoadInfo ramp,float grade,float maxRetreat,
            CloverleafRoadDesign design,bool leftExit,int budget,float clearance,RetreatSearchResult failed,CancellationToken cancellation=default)
        {
            int attempts=failed.Attempts,coreTrials=0,decks=failed.ClearanceRejected,supports=failed.SupportRejected;
            string detail=failed.ClearanceDetail;
            var lookup=traces.ToDictionary(t=>(t.Origin.NodeIndex,t.Origin.NodeVersion));
            var ordered=arms.OrderBy(a=>a.Index).ToArray();
            var windows=ordered.Select(arm=> {
                var list=new List<(float Distance,EndpointArm Arm,RoadRetreatResult[] Cuts)>();
                float limit=Math.Max(0,Math.Min(maxRetreat,arm.Endpoints.Min(p=>lookup[(p.NodeIndex,p.NodeVersion)].Length)-10));
                for(float d=0;;d=Math.Min(limit,d+25)) {
                    var cuts=arm.Endpoints.Select(p=>RoadRetreat.Cut(lookup[(p.NodeIndex,p.NodeVersion)],d)).ToArray();
                    if(cuts.All(EndpointApproachGeometry.HasRetainedRoom))list.Add((d,new EndpointArm(arm.Index,cuts.Select(c=>c.Port!)),cuts));
                    if(d>=limit)break;
                }
                return list;
            }).ToArray();
            if(windows.Any(w=>w.Count==0))return failed;
            var seen=new HashSet<(float,float,float,float)>();
            foreach(float fraction in new[]{.5f,.75f,1f,.25f,0f,.625f,.375f,.875f,.125f})
            {
                cancellation.ThrowIfCancellationRequested();
                if(attempts>=budget||coreTrials>=128)break;
                var selected=windows.Select(w=>w.OrderBy(s=>Math.Abs(s.Distance-maxRetreat*fraction)).ThenByDescending(s=>s.Distance).First()).ToArray();
                var distances=selected.Select(s=>s.Distance).ToArray();
                if(!seen.Add((distances[0],distances[1],distances[2],distances[3])))continue;
                var cutArms=selected.Select(s=>s.Arm).ToArray();var cuts=selected.SelectMany(s=>s.Cuts).ToArray();
                int localCores=0,localAttempts=0;
                foreach(var core in EndpointStackCore.CutCandidates(cutArms,type,rht,main,cross,ramp,grade,design,leftExit,cancellation))
                {
                    if(attempts>=budget||coreTrials>=128||localCores>=16||localAttempts>=4)break;
                    coreTrials++;localCores++;
                    if(!core.Success){detail=core.Diagnostic;continue;}
                    if(core.Segments.Any(s=>CurveCurvature.CheckMinimumRadius(s.WorldCurve,2*(s.RoadWidth??ramp.Width))!=CurveCurvatureResult.Clear))continue;
                    attempts++;localAttempts++;
                    var candidate=EndpointStackSearch.AttachWithHandle(core,cutArms,grade,1f/3);
                    if(candidate.Failure!=SpatialCandidateFailure.None){detail=candidate.Diagnostic;continue;}
                    if(candidate.Segments.Any(s=>VerticalProfile.CheckGrade(s.WorldCurve,grade)!=VerticalProfileFailure.None
                        ||CurveCurvature.CheckMinimumRadius(s.WorldCurve,2*(s.RoadWidth??ramp.Width))!=CurveCurvatureResult.Clear))continue;
                    if(EndpointNetworkClearance.Check(candidate,clearance,out detail)!=CurveClearanceResult.Clear){decks++;continue;}
                    if(EndpointNetworkSupports.Check(candidate,design.BridgeRules,ramp,out detail,design.SupportRules)!=CurveClearanceResult.Clear){supports++;continue;}
                    var quality=EndpointApproachQuality.Measure(core,candidate,cuts,traces);
                    if(double.IsNaN(quality.Score)||double.IsInfinity(quality.Score))continue;
                    return new RetreatSearchResult(RetreatSearchFailure.None,attempts,candidate,cutArms,cuts,distances,
                        PlanarCandidateFailure.None,SpatialCandidateFailure.None,decks,"",supports,coreMatched:true,quality:quality,
                        referenceCenter:core.ReferenceCenter,referenceFromCuts:true,coreFollowsCurve:core.CoreFollowsCurve);
                }
            }
            return new RetreatSearchResult(attempts>=budget?RetreatSearchFailure.BudgetExceeded:RetreatSearchFailure.NoCandidate,attempts,null,
                Array.Empty<EndpointArm>(),Array.Empty<RoadRetreatResult>(),Array.Empty<float>(),PlanarCandidateFailure.None,
                failed.LastSpatialFailure,decks,detail,supports,coreMatched:failed.CoreMatched);
        }
    }
}
