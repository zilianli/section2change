using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using InterchangeMod.Generation.Geometry;
namespace InterchangeMod.Generation.Endpoints
{
    public enum RetreatSearchFailure { None,InvalidInput,NoCandidate,BudgetExceeded }
    public sealed class RetreatSearchResult
    {
        internal RetreatSearchResult(RetreatSearchFailure failure,int attempts,SpatialCandidateResult? spatial,
            IEnumerable<EndpointArm> arms,IEnumerable<RoadRetreatResult> cuts,IEnumerable<float> distances,
            PlanarCandidateFailure planarFailure,SpatialCandidateFailure spatialFailure,int clearanceRejected=0,string clearanceDetail="",int supportRejected=0,string supportDetail="",bool? coreMatched=null,
            EndpointApproachQuality? quality=null,Vec3? referenceCenter=null,bool referenceFromCuts=false,bool coreFollowsCurve=false)
        { Failure=failure;Attempts=attempts;Spatial=spatial;Arms=Array.AsReadOnly(arms.ToArray());Cuts=Array.AsReadOnly(cuts.ToArray());
          Distances=Array.AsReadOnly(distances.ToArray());LastPlanarFailure=planarFailure;LastSpatialFailure=spatialFailure;
          ClearanceRejected=clearanceRejected;ClearanceDetail=clearanceDetail;SupportRejected=supportRejected;SupportDetail=supportDetail;CoreMatched=coreMatched;
          ApproachQuality=quality;ReferenceCenter=referenceCenter;ReferenceFromCuts=referenceFromCuts;CoreFollowsCurve=coreFollowsCurve; }
        public RetreatSearchFailure Failure { get; } public int Attempts { get; }
        public SpatialCandidateResult? Spatial { get; }
        public IReadOnlyList<EndpointArm> Arms { get; }
        public IReadOnlyList<RoadRetreatResult> Cuts { get; }
        public IReadOnlyList<float> Distances { get; }
        public PlanarCandidateFailure LastPlanarFailure { get; } public SpatialCandidateFailure LastSpatialFailure { get; }
        public int ClearanceRejected { get; }
        public string ClearanceDetail { get; }
        public int SupportRejected { get; }
        public string SupportDetail { get; }
        public bool? CoreMatched { get; }
        public EndpointApproachQuality? ApproachQuality {get;}
        public Vec3? ReferenceCenter {get;}
        public bool ReferenceFromCuts {get;}
        public bool CoreFollowsCurve {get;}
    }
    public static class RetreatCloverleafSearch
    {
        private static bool Finite(float v)=>!float.IsNaN(v)&&!float.IsInfinity(v);
        public static RetreatSearchResult Search(IReadOnlyList<EndpointArm> arms,IReadOnlyList<RoadApproachTrace> traces,bool rightHandTraffic,
            float mainHeight,float crossHeight,float rampWidth,float maxGrade,float maxRetreat,int maxCandidates=6144,float verticalClearance=8.5f,
            CloverleafRoadDesign? design=null)
            =>SearchCore(arms,traces,rightHandTraffic,mainHeight,crossHeight,rampWidth,maxGrade,maxRetreat,maxCandidates,verticalClearance,design,false);

        internal static RetreatSearchResult SearchCore(IReadOnlyList<EndpointArm> arms,IReadOnlyList<RoadApproachTrace> traces,bool rightHandTraffic,
            float mainHeight,float crossHeight,float rampWidth,float maxGrade,float maxRetreat,int maxCandidates=6144,float verticalClearance=8.5f,
            CloverleafRoadDesign? design=null,bool balancedBudget=false)
        {
            int attempts=0,clearanceRejected=0,supportRejected=0;string clearanceDetail="",supportDetail="";var lastPlanar=PlanarCandidateFailure.None;var lastSpatial=SpatialCandidateFailure.None;
            RetreatSearchResult Fail(RetreatSearchFailure f)=>new RetreatSearchResult(f,attempts,null,Array.Empty<EndpointArm>(),Array.Empty<RoadRetreatResult>(),Array.Empty<float>(),lastPlanar,lastSpatial,clearanceRejected,clearanceDetail,supportRejected,supportDetail);
            if(arms==null||traces==null||EndpointConstraints.Validate(arms).Count!=0||arms.Any(a=>a.Endpoints.Count!=2)
                ||!Finite(mainHeight)||!Finite(crossHeight)||!Finite(rampWidth)||rampWidth<=0||!Finite(maxGrade)||maxGrade<=0||maxGrade>.30f
                ||!Finite(maxRetreat)||maxRetreat<0||maxRetreat>1000||maxCandidates<=0||!Finite(verticalClearance)||verticalClearance<=0) return Fail(RetreatSearchFailure.InvalidInput);
            var lookup=new Dictionary<(int,int),RoadApproachTrace>();
            foreach(var t in traces)
                if(t==null||t.Origin==null||RoadRetreat.Cut(t,0).Failure!=RoadRetreatFailure.None||lookup.ContainsKey((t.Origin.NodeIndex,t.Origin.NodeVersion)))return Fail(RetreatSearchFailure.InvalidInput);
                else lookup.Add((t.Origin.NodeIndex,t.Origin.NodeVersion),t);
            foreach(var p in arms.SelectMany(a=>a.Endpoints))
                if(!lookup.TryGetValue((p.NodeIndex,p.NodeVersion),out var t)||t.Origin.Position!=p.Position||t.Origin.OutwardTangent!=p.OutwardTangent
                    ||t.Origin.Prefab!=p.Prefab||t.Origin.Width!=p.Width||t.Origin.TravelOutward!=p.TravelOutward||t.Origin.Lanes!=p.Lanes||t.Origin.OneWay!=p.OneWay)
                    return Fail(RetreatSearchFailure.InvalidInput);
            // The rounded path spends part of its turn on short end bends; skew crossings
            // may need larger radii. Keep the compact search first so the extended grid
            // cannot consume the entire budget at short, geometrically impossible retreats.
            bool hug=design?.HugLoops==true;
            for(int phase=0;phase<(!hug&&design?.SmoothDirect==true?2:1);phase++)
            {
            var seen=new HashSet<string>();
            bool phaseBudgetReached=false;
            var middleCandidates=hug?new[]{0f}:phase==1?new[]{0f,60f,120f,180f,240f,300f,360f,480f,600f}:new[]{0f,60f,120f,180f};
            var directCandidates=hug?new[]{30f,40f,60f,80f,100f,120f,160f,200f}:phase==1?new[]{80f,100f,120f,140f,160f}:new[]{80f,100f,120f};
            var candidates=(from loop in hug?new[]{30f,40f,50f,60f,80f,100f,120f}:new[]{30f,40f,50f,60f,80f}
                from direct in directCandidates from middle in middleCandidates
                where phase==0||direct>120||middle>180 select (Loop:loop,Direct:direct,Middle:middle)).ToArray();
            int zeroOnlyCount=0;
            if(balancedBudget)
            {
                zeroOnlyCount=phase==0?Math.Min(32,Math.Max(1,maxCandidates/3)):0;
                var zeroSeeds=candidates.Take(zeroOnlyCount).ToArray();
                // Start with a width-aware loop and a generous direct ramp;
                // the existing certified local refinement then shortens it.
                // Starting only with tiny, short ramps wastes the finite budget
                // on grade/crossing failures before a usable retreat is reached.
                float loopTarget=2*arms.Max(a=>a.Endpoints.Max(p=>p.Width))+(hug?design!.SharedRampWidth:0);
                float flatAllowance=4*arms.Max(a=>a.Endpoints.Max(p=>p.Width));
                loopTarget=Math.Max(loopTarget,(1.5f*Math.Abs(mainHeight-crossHeight)/maxGrade+flatAllowance)/(1.5f*(float)Math.PI));
                float middleTarget=Math.Max(180,(float)Math.Ceiling(1.5f*Math.Abs(mainHeight-crossHeight)/maxGrade/60)*60);
                candidates=candidates.OrderBy(c=>Math.Abs(c.Loop-loopTarget))
                    .ThenBy(c=>hug?Math.Abs(c.Direct-c.Loop):Math.Abs(c.Direct-120))
                    .ThenBy(c=>Math.Abs(c.Middle-middleTarget)).ToArray();
                // Preserve the existing cheap zero-retreat compact seed on
                // generous sites before searching wider/longer short-gap seeds.
                if(phase==0)candidates=zeroSeeds.Concat(candidates).ToArray();
            }
            int compactBudget=maxCandidates-maxCandidates/4;
            if(balancedBudget&&maxGrade<.12f)compactBudget=Math.Max(1,maxCandidates/4);
            // Small mixed-road budgets must reach later road windows before
            // exhausting every radius at a short, unusable selected gap.
            // Visit all source windows for each width/grade-aware seed before
            // varying its radii; every generated candidate still consumes budget.
            for(int sweep=0;sweep<(balancedBudget?candidates.Length:1)&&!phaseBudgetReached;sweep++)
            for(float distance=0; ;distance=Math.Min(maxRetreat,distance+25))
            {
                if(balancedBudget&&phase==0&&sweep<zeroOnlyCount&&distance>0)break;
                int profile=balancedBudget?sweep:0;
                var distances=arms.OrderBy(a=>a.Index).Select(a=>Math.Max(0,Math.Min(maxRetreat,a.Endpoints.Min(p=>lookup[(p.NodeIndex,p.NodeVersion)].Length)-10))).Select(v=>Math.Min(v,distance)).ToArray();
                string key=string.Join(",",distances.Select(v=>v.ToString("R",CultureInfo.InvariantCulture)))
                    +(balancedBudget?"/"+profile.ToString(CultureInfo.InvariantCulture):"");
                if(seen.Add(key))
                {
                    var cuts=new List<RoadRetreatResult>();var cutArms=new List<EndpointArm>();bool valid=true;
                    foreach(var a in arms.OrderBy(a=>a.Index))
                    {
                        var ports=new List<SelectedEndpoint>();
                        foreach(var p in a.Endpoints)
                        {
                            var cut=RoadRetreat.Cut(lookup[(p.NodeIndex,p.NodeVersion)],distances[a.Index]);
                            if(cut.Failure!=RoadRetreatFailure.None||cut.Port==null) {valid=false;break;}
                            cuts.Add(cut);ports.Add(cut.Port);
                        }
                        if(!valid)break;
                        cutArms.Add(new EndpointArm(a.Index,ports));
                    }
                    if(valid)
                        foreach(var radii in balancedBudget?new[]{candidates[profile]}:candidates)
                        {
                            if(attempts>=maxCandidates)return Fail(RetreatSearchFailure.BudgetExceeded);
                            // A higher retreat limit must not starve the expanded zero-retreat
                            // shapes. Reserve a quarter of the shared budget for that phase.
                            if(!hug&&phase==0&&design?.SmoothDirect==true&&attempts>=compactBudget)
                            {phaseBudgetReached=true;break;}
                            attempts++;
                            var planar=PlanarCloverleafCandidate.Create(cutArms,rightHandTraffic,radii.Loop,radii.Direct,rampWidth:rampWidth,directMiddleLength:radii.Middle,design:design);
                            lastPlanar=planar.Failure;
                            if(lastPlanar==PlanarCandidateFailure.NonStraightApproach)break; // Independent of ramp radii at this retreat.
                            if(lastPlanar!=PlanarCandidateFailure.None)continue;
                            var spatial=SpatialCloverleafCandidate.Create(cutArms,planar,mainHeight,crossHeight,0,maxGrade);
                            lastSpatial=spatial.Failure;
                            if(lastSpatial==SpatialCandidateFailure.PillarConflict){supportRejected++;supportDetail=spatial.Diagnostic;}
                            if(lastSpatial!=SpatialCandidateFailure.None)continue;
                            if(CloverleafRoadClearance.Check(spatial,planar,rampWidth,verticalClearance,out var detail)!=CurveClearanceResult.Clear)
                            { clearanceRejected++;clearanceDetail=detail;continue; }
                            // Keep this fully checked shape as a fallback. Refinement stays on
                            // these exact original-road cuts and leaves the loop radius alone.
                            // It is a bounded local improvement, not a global optimum search.
                            var best=spatial;
                            float DirectLength(SpatialCandidateResult candidate)=>candidate.Segments
                                .Where(s=>s.Kind==CloverEdgeKind.DirectRamp).Sum(s=>s.WorldCurve.Length(128));
                            float bestLength=DirectLength(best),bestMiddle=radii.Middle;
                            int localAttempts=0;
                            var tried=new HashSet<(float Radius,float Middle)>{(radii.Direct,radii.Middle)};
                            bool improved;
                            do
                            {
                                improved=false;
                                for(float radius=radii.Direct; ;radius=Math.Max(30,radius-10))
                                {
                                    // First shorten the middle on a 30/15m grid, then also
                                    // test the current middle at each smaller direct radius.
                                    var middles=new[]{Math.Max(0,bestMiddle-30),Math.Max(0,bestMiddle-15),bestMiddle,radii.Middle};
                                    foreach(float middle in middles)
                                    {
                                        // A smaller radius can need the original middle for
                                        // grade, while still producing a shorter whole ramp.
                                        if(!tried.Add((radius,middle)))continue;
                                        if(localAttempts>=24||attempts>=maxCandidates)goto Refined;
                                        localAttempts++;attempts++;
                                        var trialPlanar=PlanarCloverleafCandidate.Create(cutArms,rightHandTraffic,radii.Loop,radius,
                                            rampWidth:rampWidth,directMiddleLength:middle,design:design);
                                        if(trialPlanar.Failure!=PlanarCandidateFailure.None)continue;
                                        var trialSpatial=SpatialCloverleafCandidate.Create(cutArms,trialPlanar,mainHeight,crossHeight,0,maxGrade);
                                        if(trialSpatial.Failure!=SpatialCandidateFailure.None)continue;
                                        if(CloverleafRoadClearance.Check(trialSpatial,trialPlanar,rampWidth,verticalClearance,out _)!=CurveClearanceResult.Clear)continue;
                                        float length=DirectLength(trialSpatial);
                                        if(!Finite(length)||length>=bestLength-.001f)continue;
                                        best=trialSpatial;bestLength=length;bestMiddle=middle;improved=true;
                                    }
                                    if(radius<=30)break;
                                }
                            }while(improved&&localAttempts<24&&attempts<maxCandidates);
                        Refined:
                            // Rejected refinements must not replace successful diagnostics or
                            // convert a valid fallback into BudgetExceeded at the budget edge.
                            return new RetreatSearchResult(RetreatSearchFailure.None,attempts,best,cutArms,cuts,distances,
                                PlanarCandidateFailure.None,SpatialCandidateFailure.None,clearanceRejected,clearanceDetail,supportRejected,supportDetail);
                        }
                }
                if(phaseBudgetReached||distance>=maxRetreat)break;
            }
            }
            return Fail(RetreatSearchFailure.NoCandidate);
        }
    }
}
