using System;
using System.Collections.Generic;
using System.Linq;
using InterchangeMod.Generation.Roads;

namespace InterchangeMod.Generation.Endpoints
{
    public sealed class EndpointLaneBalance
    {
        public EndpointLaneBalance(int arm,string prefab,float width,int lanes,int rampLanes)
        { Arm=arm;Prefab=prefab;Width=width;Lanes=lanes;RampLanes=rampLanes; }
        public int Arm { get; }
        public string Prefab { get; }
        public float Width { get; }
        public int Lanes { get; }
        public int RampLanes { get; }

        public static IReadOnlyList<EndpointLaneBalance> Select(IReadOnlyList<EndpointArm> arms,
            IReadOnlyList<RoadInfo> catalog,RoadInfo ramp,bool ordinaryAutomaticRoads=false)
        {
            var result=new List<EndpointLaneBalance>();
            foreach(var arm in arms)
            {
                var incoming=arm.Endpoints.First(p=>!p.TravelOutward);
                var original=catalog.FirstOrDefault(r=>r.Name==incoming.Prefab);
                int lanes=incoming.Lanes-ramp.LanesPerDirection;
                if(original==null||lanes<1)continue;
                var reduced=catalog.Where(r=>r.IsHighway==original.IsHighway&&r.IsOneWay&&r.ForwardLanes==lanes
                    &&r.BackwardLanes==0&&r.Width>0&&r.Width<=incoming.Width
                    &&(!ordinaryAutomaticRoads||RampMatcher.IsOrdinaryRoad(r)))
                    .OrderBy(r=>r.Width).ThenBy(r=>r.Name,StringComparer.Ordinal).FirstOrDefault();
                if(reduced!=null)result.Add(new EndpointLaneBalance(arm.Index,reduced.Name,reduced.Width,lanes,ramp.LanesPerDirection));
            }
            return result.AsReadOnly();
        }
    }

    /// <summary>Optional appearance policy; null preserves the original geometry-only candidate API.</summary>
    public sealed class CloverleafRoadDesign
    {
        public CloverleafRoadDesign(IEnumerable<EndpointLaneBalance>? balances=null,bool edgeAligned=true,bool smoothDirect=true,bool approachTransitions=true,
            IEnumerable<EndpointBridgeRule>? bridgeRules=null,bool hugLoops=false,string? sharedRampPrefab=null,float sharedRampWidth=0,
            IEnumerable<EndpointPrefabSupportRule>? supportRules=null)
        { Balances=Array.AsReadOnly((balances??Array.Empty<EndpointLaneBalance>()).ToArray());EdgeAligned=edgeAligned;SmoothDirect=smoothDirect;ApproachTransitions=approachTransitions;
          BridgeRules=Array.AsReadOnly((bridgeRules??Array.Empty<EndpointBridgeRule>()).ToArray());
          HugLoops=hugLoops;SharedRampPrefab=sharedRampPrefab;SharedRampWidth=sharedRampWidth;
          SupportRules=Array.AsReadOnly((supportRules??Array.Empty<EndpointPrefabSupportRule>()).ToArray()); }
        public IReadOnlyList<EndpointLaneBalance> Balances { get; }
        public bool EdgeAligned { get; }
        public bool SmoothDirect { get; }
        public bool ApproachTransitions { get; }
        public IReadOnlyList<EndpointBridgeRule> BridgeRules { get; }
        public bool HugLoops { get; }
        public string? SharedRampPrefab { get; }
        public float SharedRampWidth { get; }
        public IReadOnlyList<EndpointPrefabSupportRule> SupportRules { get; }
        public static RoadInfo? SelectSharedRamp(IReadOnlyList<RoadInfo> catalog,RoadInfo ramp)=>catalog
            .Where(r=>r.ForwardLanes==ramp.LanesPerDirection&&r.BackwardLanes==ramp.LanesPerDirection
                &&r.PublicTransportLanes==0&&r.Width>=ramp.Width&&!float.IsInfinity(r.Width)&&!float.IsNaN(r.Width))
            .OrderBy(r=>r.IsHighway==ramp.IsHighway?0:1).ThenBy(r=>r.Width)
            .ThenBy(r=>r.Name,StringComparer.Ordinal).FirstOrDefault();
        public const float MaxApproachDeflectionDegrees=20f;
    }
}
