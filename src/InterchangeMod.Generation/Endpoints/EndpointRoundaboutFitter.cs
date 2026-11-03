using System;
using System.Collections.Generic;
using System.Linq;
using InterchangeMod.Generation.Geometry;
using InterchangeMod.Generation.Roads;

namespace InterchangeMod.Generation.Endpoints
{
    /// <summary>Improve the eight outer curves while retaining a certified analytic roundabout seed.</summary>
    internal static class EndpointRoundaboutFitter
    {
        internal static RetreatSearchResult Search(IReadOnlyList<EndpointArm> arms,IReadOnlyList<RoadApproachTrace> traces,
            bool rht,float mainHeight,float crossHeight,RoadInfo ramp,RoadInfo ring,float grade,float maxRetreat,
            CloverleafRoadDesign design,int maxCandidates,float verticalClearance,float? terrain,bool racetrack)
        {
            var seed=EndpointCompactGridSearch.FindSafe(arms,traces,rht,mainHeight,crossHeight,ramp,null,grade,maxRetreat,
                design,maxCandidates,verticalClearance,terrain,roundaboutRing:ring,racetrack:racetrack);
            if(seed.Failure!=RetreatSearchFailure.None||seed.Spatial==null||seed.ApproachQuality==null)return seed;
            int attempts=seed.Attempts,budget=Math.Min(maxCandidates,128),decks=seed.ClearanceRejected,supports=seed.SupportRejected;
            // Curvature is measured in 1/metres: a radius of at least 10km and
            // aggregate join jumps below 1/10000m are already visually straight.
            // Do not move those certified cuts just to shave float noise or length.
            if(attempts>=budget||seed.ApproachQuality.PeakCurvature<=1e-4&&seed.ApproachQuality.CurvatureJump<=1e-4)return seed;

            float clearance=Math.Max(verticalClearance,EndpointRoundaboutCore.RequiredClearance(design,ramp,ring));
            var original=arms.OrderBy(a=>a.Index).ToArray();
            var lookup=traces.ToDictionary(t=>(t.Origin.NodeIndex,t.Origin.NodeVersion));
            var allowed=original.Select(arm=>
            {
                float limit=Math.Max(0,Math.Min(maxRetreat,arm.Endpoints.Min(p=>lookup[(p.NodeIndex,p.NodeVersion)].Length)-10));
                var values=new List<float>();
                for(float distance=0;;distance=Math.Min(limit,distance+25))
                {
                    if(distance>=seed.Distances[arm.Index]&&arm.Endpoints.All(p=>
                        EndpointApproachGeometry.HasRetainedRoom(RoadRetreat.Cut(lookup[(p.NodeIndex,p.NodeVersion)],distance))))values.Add(distance);
                    if(distance>=limit)break;
                }
                return values.ToArray();
            }).ToArray();
            // The seed comes from the same retained-road windows, so each arm has
            // at least that distance. Keep the seed if a future window rule differs.
            if(allowed.Any(values=>values.Length==0))return seed;

            var best=seed;
            foreach(var distances in Vectors(seed.Distances,allowed))
            {
                if(attempts>=budget)break;
                var cutGroups=original.Select(arm=>arm.Endpoints.Select(p=>
                    RoadRetreat.Cut(lookup[(p.NodeIndex,p.NodeVersion)],distances[arm.Index])).ToArray()).ToArray();
                var cuts=cutGroups.SelectMany(group=>group).ToArray();
                var selected=original.Select(arm=>new EndpointArm(arm.Index,cutGroups[arm.Index].Select(c=>c.Port!))).ToArray();
                int acceptedCores=0;
                // Eight vectors, at most two successful cores per vector and three
                // handles bound improvement work independently of the seed search.
                foreach(var core in EndpointRoundaboutCore.Candidates(selected,rht,mainHeight,crossHeight,ramp,ring,grade,design,terrain!.Value,racetrack))
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
                        if(quality.Curves.Any(s=>CurveCurvature.CheckMinimumRadius(s.WorldCurve,2*s.RoadWidth!.Value)!=CurveCurvatureResult.Clear)
                            ||candidate.Segments.Any(s=>VerticalProfile.CheckGrade(s.WorldCurve,grade)!=VerticalProfileFailure.None))continue;
                        // The median is measured at these exact paired cuts, not at
                        // the old selected endpoints before retreating along a bend.
                        if(EndpointRoundaboutConnectionClearance.Check(candidate,selected,clearance,out _)!=CurveClearanceResult.Clear){decks++;continue;}
                        if(EndpointNetworkClearance.Check(candidate,clearance,out _)!=CurveClearanceResult.Clear){decks++;continue;}
                        if(EndpointNetworkSupports.Check(candidate,design.BridgeRules,ramp,out _,design.SupportRules)!=CurveClearanceResult.Clear){supports++;continue;}
                        best=new RetreatSearchResult(RetreatSearchFailure.None,attempts,candidate,selected,cuts,distances,
                            PlanarCandidateFailure.None,SpatialCandidateFailure.None,decks,"",supports,coreMatched:true,quality:quality,
                            referenceCenter:core.ReferenceCenter,referenceFromCuts:true,coreFollowsCurve:false);
                    }
                    if(++acceptedCores>=2)break;
                }
            }
            // Hitting the remaining budget or rejecting every improvement is not a
            // failed search: the original fully checked candidate remains usable.
            return new RetreatSearchResult(best.Failure,attempts,best.Spatial,best.Arms,best.Cuts,best.Distances,
                best.LastPlanarFailure,best.LastSpatialFailure,decks,best.ClearanceDetail,supports,best.SupportDetail,
                best.CoreMatched,best.ApproachQuality,best.ReferenceCenter,best.ReferenceFromCuts,best.CoreFollowsCurve);
        }

        private static IEnumerable<float[]> Vectors(IReadOnlyList<float> seed,float[][] allowed)
        {
            var original=seed.ToArray();
            var paired=new[]{Math.Max(seed[0],seed[2]),Math.Max(seed[1],seed[3]),Math.Max(seed[0],seed[2]),Math.Max(seed[1],seed[3])};
            var balanced=Enumerable.Repeat(seed.Max(),4).ToArray();
            var requests=new[]{original,paired,balanced,original.Select(d=>d+100).ToArray(),paired.Select(d=>d+100).ToArray(),
                balanced.Select(d=>d+100).ToArray(),paired.Select(d=>d+250).ToArray(),balanced.Select(d=>d+400).ToArray()};
            var seen=new HashSet<(float,float,float,float)>();
            foreach(var request in requests)
            {
                var actual=request.Select((distance,i)=>allowed[i].OrderBy(value=>Math.Abs(value-distance)).ThenByDescending(value=>value).First()).ToArray();
                if(seen.Add((actual[0],actual[1],actual[2],actual[3])))yield return actual;
            }
        }
    }
}
