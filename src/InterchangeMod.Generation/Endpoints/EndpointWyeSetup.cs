using System;
using System.Collections.Generic;
using System.Linq;
using InterchangeMod.Generation.Roads;

namespace InterchangeMod.Generation.Endpoints
{
    /// <summary>Y-specific road selection and common core levels, independent of T main/branch rules.</summary>
    public sealed class EndpointWyeSetup
    {
        private EndpointWyeSetup(bool success,string diagnostic,RoadInfo? ramp,float height,float upper,float required,
            IReadOnlyList<EndpointBridgeRule> bridgeRules)
        {Success=success;Diagnostic=diagnostic;Ramp=ramp;BaseHeight=height;UpperHeight=upper;RequiredClearance=required;BridgeRules=bridgeRules;}
        public bool Success {get;}
        public string Diagnostic {get;}
        public RoadInfo? Ramp {get;}
        public float BaseHeight {get;}
        public float UpperHeight {get;}
        public float RequiredClearance {get;}
        public IReadOnlyList<EndpointBridgeRule> BridgeRules {get;}
        private static EndpointWyeSetup Fail(string reason)=>new EndpointWyeSetup(false,reason,null,0,0,0,Array.Empty<EndpointBridgeRule>());
        private static bool Finite(float v)=>!float.IsNaN(v)&&!float.IsInfinity(v);

        public static EndpointWyeSetup Create(IReadOnlyList<EndpointArm> arms,IReadOnlyList<RoadInfo> catalog,
            float offset,float gap,float terrainHeight,string? preferredRamp=null,bool ordinaryAutomaticRoads=false)
        {
            if(arms==null||catalog==null||EndpointConstraints.Validate(arms,InterchangeType.Wye).Count!=0
                ||arms.Any(a=>a.Endpoints.Count!=2))
                return Fail("Y型首轮需要三个方向各一进一出的双幅开放端点，共六个点。");
            if(!Finite(offset)||!Finite(gap)||gap<0||!Finite(terrainHeight))return Fail("Y型层高、层差或地形数据无效，请调整参数后重试。");
            var ports=arms.SelectMany(a=>a.Endpoints).ToArray();int lanes=ports[0].Lanes;
            if(arms.Any(a=>a.Endpoints.Any(p=>p.Prefab!=a.Endpoints[0].Prefab||p.Width!=a.Endpoints[0].Width||p.Lanes!=a.Endpoints[0].Lanes)))
                return Fail("Y型每条臂的进出两幅须同款、同宽、同车道数，请重新选择匹配道路。");
            if(lanes%2!=0||ports.Any(p=>p.Lanes!=lanes))
                return Fail("Y型首轮要求三条臂车道数相同且为偶数，才能精确分为两条半车道数匝道；奇数或不等车道组合尚未接入。");
            var roads=new List<RoadInfo>();var rules=new List<EndpointBridgeRule>();
            foreach(var arm in arms.OrderBy(a=>a.Index))
            {
                var p=arm.Endpoints[0];
                var road=catalog.FirstOrDefault(r=>r!=null&&r.Name==p.Prefab&&r.ForwardLanes==lanes&&r.BackwardLanes==0&&r.Width==p.Width);
                if(road==null)return Fail($"方向{arm.Index+1}的道路目录与已选断面不一致，请重新选点。");
                float length=road.ElevatedLength>0?road.ElevatedLength:80,pillar=road.PillarWidth>0?road.PillarWidth:4;
                if(!Finite(length)||length<10||length>1000||!Finite(pillar)||pillar>1000
                    ||(road.MiddlePillarSpacing.HasValue&&(!Finite(road.MiddlePillarSpacing.Value)||road.MiddlePillarSpacing.Value<0)))
                    return Fail($"方向{arm.Index+1}的桥墩或桥跨数据无效，暂不能安全预览。");
                roads.Add(road);rules.Add(new EndpointBridgeRule(arm.Index,length,road.MiddlePillarSpacing,road.Width,pillar,road.GroundHeight,road.ElevatedHeight));
            }
            bool highway=roads.Count(r=>r.IsHighway)>=2;
            var ramp=catalog.Where(r=>r!=null&&r.ForwardLanes==lanes/2&&r.BackwardLanes==0&&r.PublicTransportLanes==0
                    &&Finite(r.Width)&&r.Width>0&&r.Width<=ports.Min(p=>p.Width)&&!string.IsNullOrWhiteSpace(r.Name)
                    &&(!ordinaryAutomaticRoads||RampMatcher.IsOrdinaryRoad(r)||r.Name==preferredRamp))
                .OrderBy(r=>r.Name==preferredRamp?0:1).ThenBy(r=>r.IsHighway==highway?0:1).ThenBy(r=>r.Width)
                .ThenBy(r=>r.Name,StringComparer.Ordinal).FirstOrDefault();
            if(ramp==null)return Fail($"Y型每幅{lanes}车道需要正向{lanes/2}车道匝道，当前目录没有合适断面，请更换道路。");
            float required=EndpointWyeCore.RequiredClearance(new CloverleafRoadDesign(bridgeRules:rules),ramp);
            if(!Finite(required))return Fail("Y型道路的结构高度或净空数据无效，暂不能安全预览。");
            if(gap>0&&gap<required)return Fail($"Y型实际道路至少需要{required:F1}米层差；请增大层差或设为0自动。");
            float separation=gap==0?Math.Max(10,(float)(Math.Ceiling(required*2)/2+.5)):gap;
            double baseWorld=ports.Average(p=>(double)p.Position.Y)+offset,upperWorld=baseWorld+separation;
            if(double.IsNaN(baseWorld)||double.IsInfinity(baseWorld)||Math.Abs(baseWorld)>float.MaxValue
                ||double.IsNaN(upperWorld)||double.IsInfinity(upperWorld)||Math.Abs(upperWorld)>float.MaxValue
                ||!Finite((float)baseWorld-terrainHeight)||!Finite((float)upperWorld-terrainHeight))
                return Fail("Y型层高计算超出有效范围，请重新选择端点和高程。");
            if(upperWorld-terrainHeight<HeightRange.BridgeThreshold)
                return Fail("Y型上层未达到高架桥高度，请提高基层偏移或层差。");
            return new EndpointWyeSetup(true,"",ramp,(float)baseWorld,(float)upperWorld,required,rules.AsReadOnly());
        }
    }
}
