using System;
using System.Collections.Generic;
using System.Linq;
using InterchangeMod.Generation.Geometry;
using InterchangeMod.Generation.Roads;

namespace InterchangeMod.Generation.Endpoints
{
    /// <summary>Improve a certified grid without losing it or exceeding its shared budget.</summary>
    internal static class EndpointCompactGridFitter
    {
        internal static RetreatSearchResult Search(IReadOnlyList<EndpointArm> arms,IReadOnlyList<RoadApproachTrace> traces,
            bool rht,float height,float gap,RoadInfo ramp,IReadOnlyList<EndpointGridArmRoads> roads,float grade,
            float maxRetreat,CloverleafRoadDesign design,int maxCandidates,float verticalClearance,float? terrain)
        {
            var seed=EndpointCompactGridSearch.FindSafe(arms,traces,rht,height,gap,ramp,roads,grade,maxRetreat,design,maxCandidates,verticalClearance,terrain);
            if(seed.Failure!=RetreatSearchFailure.None||seed.Spatial==null||seed.ApproachQuality==null)return seed;
            int attempts=seed.Attempts,budget=Math.Min(maxCandidates,128),decks=seed.ClearanceRejected,supports=seed.SupportRejected;
            if(attempts>=budget||seed.ApproachQuality.PeakCurvature<1e-7&&seed.ApproachQuality.CurvatureJump<1e-6)return seed;
            float clearance=Math.Max(verticalClearance,EndpointCompactGridCore.RequiredClearance(roads,design,ramp));
            var original=arms.OrderBy(a=>a.Index).ToArray();var lookup=traces.ToDictionary(t=>(t.Origin.NodeIndex,t.Origin.NodeVersion));
            var allowed=original.Select(arm=>
            {
                float limit=Math.Min(maxRetreat,arm.Endpoints.Min(p=>lookup[(p.NodeIndex,p.NodeVersion)].Length)-10);
                var values=new List<float>();
                for(float d=0;;d=Math.Min(limit,d+25))
                {
                    if(d>=seed.Distances[arm.Index]&&arm.Endpoints.All(p=>EndpointApproachGeometry.HasRetainedRoom(RoadRetreat.Cut(lookup[(p.NodeIndex,p.NodeVersion)],d))))values.Add(d);
                    if(d>=limit)break;
                }
                return values.ToArray();
            }).ToArray();
            var best=seed;
            foreach(var distances in Vectors(seed.Distances,allowed))
            {
                if(attempts>=budget)break;
                var cuts=original.SelectMany(a=>a.Endpoints.Select(p=>RoadRetreat.Cut(lookup[(p.NodeIndex,p.NodeVersion)],distances[a.Index]))).ToArray();
                var selected=original.Select(a=>new EndpointArm(a.Index,cuts.Where(c=>a.Endpoints.Any(p=>p.NodeIndex==c.Port!.NodeIndex)).Select(c=>c.Port!))).ToArray();
                foreach(float strength in new[]{1f,.5f,.25f,0f})
                {
                    if(attempts>=budget)break;
                    int accepted=0;
                    foreach(var core in EndpointCompactGridCore.Candidates(selected,rht,height,gap,ramp,roads,grade,design,terrain!.Value,true,strength))
                    {
                        if(attempts>=budget)break;
                        if(!core.Success)continue;
                        foreach(float handle in new[]{.4f,1f/3,.25f})
                        {
                            if(attempts>=budget)break;
                            attempts++;
                            var candidate=EndpointCoreAttachment.Attach(core,selected,grade,handle);
                            if(candidate.Failure!=SpatialCandidateFailure.None)continue;
                            var quality=EndpointApproachQuality.Measure(core,candidate,cuts,traces);
                            if(double.IsNaN(quality.Score)||double.IsInfinity(quality.Score)||quality.Score>=best.ApproachQuality!.Score)continue;
                            if(quality.Curves.Any(s=>CurveCurvature.CheckMinimumRadius(s.WorldCurve,2*s.RoadWidth!.Value)!=CurveCurvatureResult.Clear))continue;
                            if(EndpointGridConnectionClearance.Check(candidate,original,clearance,out _)!=CurveClearanceResult.Clear){decks++;continue;}
                            if(EndpointNetworkClearance.Check(candidate,clearance,out _)!=CurveClearanceResult.Clear){decks++;continue;}
                            if(EndpointNetworkSupports.Check(candidate,design.BridgeRules,ramp,out _,design.SupportRules)!=CurveClearanceResult.Clear){supports++;continue;}
                            best=new RetreatSearchResult(RetreatSearchFailure.None,attempts,candidate,selected,cuts,distances,
                                PlanarCandidateFailure.None,SpatialCandidateFailure.None,decks,"",supports,coreMatched:true,quality:quality,
                                referenceCenter:core.ReferenceCenter,referenceFromCuts:true,coreFollowsCurve:core.CoreFollowsCurve);
                        }
                        if(++accepted>=2)break;
                    }
                }
            }
            return new RetreatSearchResult(best.Failure,attempts,best.Spatial,best.Arms,best.Cuts,best.Distances,
                best.LastPlanarFailure,best.LastSpatialFailure,decks,best.ClearanceDetail,supports,best.SupportDetail,
                best.CoreMatched,best.ApproachQuality,best.ReferenceCenter,best.ReferenceFromCuts,best.CoreFollowsCurve);
        }

        private static IEnumerable<float[]> Vectors(IReadOnlyList<float> seed,float[][] allowed)
        {
            var original=seed.ToArray();var paired=new[]{Math.Max(seed[0],seed[2]),Math.Max(seed[1],seed[3]),Math.Max(seed[0],seed[2]),Math.Max(seed[1],seed[3])};
            var balanced=Enumerable.Repeat(seed.Max(),4).ToArray();
            var requests=new[]{original,paired,balanced,original.Select(d=>d+25).ToArray(),paired.Select(d=>d+25).ToArray(),
                balanced.Select(d=>d+25).ToArray(),original.Select(d=>d+50).ToArray(),paired.Select(d=>d+50).ToArray()};
            var seen=new HashSet<(float,float,float,float)>();
            foreach(var request in requests)
            {
                var actual=request.Select((d,i)=>allowed[i].OrderBy(v=>Math.Abs(v-d)).ThenByDescending(v=>v).First()).ToArray();
                if(seen.Add((actual[0],actual[1],actual[2],actual[3])))yield return actual;
            }
        }
    }
}
