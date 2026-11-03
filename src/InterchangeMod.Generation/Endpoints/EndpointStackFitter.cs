using System.Collections.Generic;
using System;
using System.Linq;
using System.Threading;
using InterchangeMod.Generation.Roads;
using InterchangeMod.Generation.Geometry;

namespace InterchangeMod.Generation.Endpoints
{
    public static partial class EndpointStackFitter
    {
        public static RetreatSearchResult Search(IReadOnlyList<EndpointArm> arms,IReadOnlyList<RoadApproachTrace> traces,
            InterchangeType type,bool rightHandTraffic,float mainHeight,float crossHeight,RoadInfo ramp,float maxGrade,
            float maxRetreat,CloverleafRoadDesign design,bool leftExit=false,int maxCandidates=128,float verticalClearance=8.5f,Action<string>? diagnostic=null)
            =>SearchCore(arms,traces,type,rightHandTraffic,mainHeight,crossHeight,ramp,maxGrade,maxRetreat,design,leftExit,maxCandidates,verticalClearance,diagnostic,false,false);

        internal static RetreatSearchResult SearchCore(IReadOnlyList<EndpointArm> arms,IReadOnlyList<RoadApproachTrace> traces,
            InterchangeType type,bool rightHandTraffic,float mainHeight,float crossHeight,RoadInfo ramp,float maxGrade,
            float maxRetreat,CloverleafRoadDesign design,bool leftExit=false,int maxCandidates=128,float verticalClearance=8.5f,Action<string>? diagnostic=null,bool strictRadii=false,bool firstSafe=false,CancellationToken cancellation=default)
        {
            var seed=EndpointStackSearch.SearchCore(arms,traces,type,rightHandTraffic,mainHeight,crossHeight,ramp,maxGrade,maxRetreat,design,leftExit,maxCandidates,verticalClearance,strictRadii,cancellation);
            if(strictRadii&&seed.Failure==RetreatSearchFailure.NoCandidate&&seed.Attempts<Math.Min(maxCandidates,128))
                seed=FindCutSeed(arms,traces,type,rightHandTraffic,mainHeight,crossHeight,ramp,maxGrade,maxRetreat,
                    design,leftExit,Math.Min(maxCandidates,128),verticalClearance,seed,cancellation);
            if(seed.Failure!=RetreatSearchFailure.None||seed.Spatial==null)return seed;
            if(firstSafe)return seed;
            int budget=Math.Min(maxCandidates,128),attempts=seed.Attempts,clearance=seed.ClearanceRejected,supports=seed.SupportRejected;
            var lookup=traces.ToDictionary(t=>(t.Origin.NodeIndex,t.Origin.NodeVersion));
            var ordered=arms.OrderBy(a=>a.Index).ToArray();
            var limits=ordered.Select(a=>Math.Max(0,Math.Min(maxRetreat,a.Endpoints.Min(p=>lookup[(p.NodeIndex,p.NodeVersion)].Length)-10))).ToArray();
            // A requested +25m step may land a few metres before an old node.
            // Project requests onto actual, width-safe cut samples instead of
            // discarding almost every reference vector on segmented curved roads.
            var allowed=ordered.Select(arm=>
            {
                var values=new List<float>();
                for(int step=0;step<=40;step++)
                {
                    float d=Math.Min(limits[arm.Index],step*25);
                    if(d>=seed.Distances[arm.Index]&&arm.Endpoints.All(p=>
                        EndpointApproachGeometry.HasRetainedRoom(RoadRetreat.Cut(lookup[(p.NodeIndex,p.NodeVersion)],d))))values.Add(d);
                    if(d>=limits[arm.Index])break;
                }
                return values.ToArray();
            }).ToArray();
            RetreatSearchResult best=seed;
            bool fitted=false;
            diagnostic?.Invoke($"seed score={seed.ApproachQuality?.Score:F4}, peak={seed.ApproachQuality?.PeakCurvature:F6}, jump={seed.ApproachQuality?.CurvatureJump:F6}");
            foreach(var distances in Vectors(seed.Distances,limits,allowed))
            {
                cancellation.ThrowIfCancellationRequested();
                if(attempts>=budget)break;
                var cuts=new List<RoadRetreatResult>();var cutArms=new List<EndpointArm>();bool valid=true;
                foreach(var arm in ordered)
                {
                    var ports=new List<SelectedEndpoint>();
                    foreach(var p in arm.Endpoints)
                    {
                        var cut=RoadRetreat.Cut(lookup[(p.NodeIndex,p.NodeVersion)],distances[arm.Index]);
                        if(!EndpointApproachGeometry.HasRetainedRoom(cut)){valid=false;break;}
                        cuts.Add(cut);ports.Add(cut.Port!);
                    }
                    if(!valid)break;
                    cutArms.Add(new EndpointArm(arm.Index,ports));
                }
                if(!valid){diagnostic?.Invoke($"reference [{string.Join(",",distances)}] retained tail rejected");continue;}
                int coreBad=0,attachBad=0,radiusBad=0,deckBad=0,supportBad=0;double safeScore=double.PositiveInfinity;
                // The reference and attachment endpoints are the SAME snapshots.
                // A core may never use one distance vector and attach to another.
                foreach(var core in EndpointStackCore.CutCandidates(cutArms,type,rightHandTraffic,mainHeight,crossHeight,ramp,maxGrade,design,leftExit,cancellation))
                {
                    if(attempts>=budget)break;
                    if(!core.Success){coreBad++;continue;}
                    foreach(float handle in new[]{1f/3,.25f,.4f})
                    {
                        if(attempts>=budget)break;
                        attempts++;
                        var candidate=EndpointStackSearch.AttachWithHandle(core,cutArms,maxGrade,handle);
                        if(candidate.Failure!=SpatialCandidateFailure.None){attachBad++;continue;}
                        var quality=EndpointApproachQuality.Measure(core,candidate,cuts,traces);
                        if(double.IsNaN(quality.Score)||double.IsInfinity(quality.Score))continue;
                        var radiusCurves=strictRadii?candidate.Segments:quality.Curves;
                        if(radiusCurves.Any(s=>CurveCurvature.CheckMinimumRadius(s.WorldCurve,2*(s.RoadWidth??ramp.Width))!=CurveCurvatureResult.Clear)){radiusBad++;continue;}
                        if(EndpointNetworkClearance.Check(candidate,verticalClearance,out _)!=CurveClearanceResult.Clear){clearance++;deckBad++;continue;}
                        if(EndpointNetworkSupports.Check(candidate,design.BridgeRules,ramp,out _,design.SupportRules)!=CurveClearanceResult.Clear){supports++;supportBad++;continue;}
                        safeScore=Math.Min(safeScore,quality.Score);
                        if(fitted&&quality.Score>=best.ApproachQuality!.Score)continue;
                        // Prefer a genuinely smoother fitted layout. Keep the known
                        // safe preview when a new reference cannot improve it.
                        if(!fitted&&seed.ApproachQuality!=null&&quality.Score>=seed.ApproachQuality.Score)continue;
                        fitted=true;
                        best=new RetreatSearchResult(RetreatSearchFailure.None,attempts,candidate,cutArms,cuts,distances,
                            PlanarCandidateFailure.None,SpatialCandidateFailure.None,clearance,"",supports,coreMatched:true,
                            quality:quality,referenceCenter:core.ReferenceCenter,referenceFromCuts:true,coreFollowsCurve:core.CoreFollowsCurve);
                    }
                }
                diagnostic?.Invoke($"reference [{string.Join(",",distances)}] coreBad={coreBad}, attachBad={attachBad}, radiusBad={radiusBad}, deckBad={deckBad}, supportBad={supportBad}, safeScore={safeScore:F4}");
            }
            return new RetreatSearchResult(best.Failure,attempts,best.Spatial,best.Arms,best.Cuts,best.Distances,
                best.LastPlanarFailure,best.LastSpatialFailure,clearance,best.ClearanceDetail,supports,best.SupportDetail,
                best.CoreMatched,best.ApproachQuality,best.ReferenceCenter,best.ReferenceFromCuts,best.CoreFollowsCurve);
        }

        private static IEnumerable<float[]> Vectors(IReadOnlyList<float> seed,float[] limits,float[][] allowed)
        {
            var original=seed.ToArray();
            var paired=new[]{Math.Max(seed[0],seed[2]),Math.Max(seed[1],seed[3]),Math.Max(seed[0],seed[2]),Math.Max(seed[1],seed[3])};
            var balanced=Enumerable.Repeat(seed.Max(),4).ToArray();
            var requests=new[]{original,paired,balanced,original.Select(d=>d+25).ToArray(),paired.Select(d=>d+25).ToArray(),
                balanced.Select(d=>d+25).ToArray(),original.Select(d=>d+50).ToArray(),paired.Select(d=>d+50).ToArray()};
            var seen=new HashSet<(float,float,float,float)>();
            foreach(var request in requests)
            {
                var values=request.Select((d,i)=>allowed[i].OrderBy(v=>Math.Abs(v-Math.Min(d,limits[i]))).ThenByDescending(v=>v).First()).ToArray();
                if(seen.Add((values[0],values[1],values[2],values[3])))yield return values;
            }
        }
    }
}
