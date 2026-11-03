using System;
using System.Collections.Generic;
using System.Linq;
using InterchangeMod.Generation.Roads;

namespace InterchangeMod.Generation.Endpoints
{
    public sealed class EndpointCompactGridSetup
    {
        private EndpointCompactGridSetup(bool success,string diagnostic,RoadInfo? ramp,IReadOnlyList<EndpointGridArmRoads> roads,
            CloverleafRoadDesign? design,float height,float gap,float required,float[] layers)
        {Success=success;Diagnostic=diagnostic;Ramp=ramp;Roads=roads;Design=design;BaseHeight=height;LevelGap=gap;RequiredClearance=required;Layers=Array.AsReadOnly(layers);}
        public bool Success {get;}public string Diagnostic {get;}public RoadInfo? Ramp {get;}
        public IReadOnlyList<EndpointGridArmRoads> Roads {get;}public CloverleafRoadDesign? Design {get;}
        public float BaseHeight {get;}public float LevelGap {get;}public float RequiredClearance {get;}
        public IReadOnlyList<float> Layers {get;}
        private static EndpointCompactGridSetup Fail(string message)=>new EndpointCompactGridSetup(false,message,null,
            Array.Empty<EndpointGridArmRoads>(),null,0,0,0,Array.Empty<float>());
        private static bool Finite(float v)=>!float.IsNaN(v)&&!float.IsInfinity(v);
        private static bool Range(HeightRange? r)=>!r.HasValue||(Finite(r.Value.Min)&&Finite(r.Value.Max)&&r.Value.Min<=r.Value.Max);
        private static bool Valid(RoadInfo r)=>r!=null&&!string.IsNullOrWhiteSpace(r.Name)&&r.ForwardLanes>0&&r.BackwardLanes==0
            &&r.PublicTransportLanes==0&&Finite(r.Width)&&r.Width>0&&r.Width<=1000&&Range(r.GroundHeight)&&Range(r.ElevatedHeight)
            &&Finite(r.ElevatedLength)&&r.ElevatedLength>=0&&(r.ElevatedLength==0||(r.ElevatedLength>=10&&r.ElevatedLength<=1000))
            &&Finite(r.PillarWidth)&&r.PillarWidth>=0&&r.PillarWidth<=1000
            &&(!r.MiddlePillarSpacing.HasValue||(Finite(r.MiddlePillarSpacing.Value)&&r.MiddlePillarSpacing.Value>=0));
        private static float Length(RoadInfo r)=>r.ElevatedLength>0?r.ElevatedLength:80;
        private static float Pillar(RoadInfo r)=>r.PillarWidth>0?r.PillarWidth:4;

        public static EndpointCompactGridSetup Create(IReadOnlyList<EndpointArm> arms,IReadOnlyList<RoadInfo> catalog,
            float offset,float gap,float terrainHeight,string? preferredRamp=null,bool ordinaryAutomaticRoads=false)
        {
            if(arms==null||catalog==null||EndpointConstraints.Validate(arms,InterchangeType.CompactGrid).Count!=0
                ||arms.Any(a=>a.Endpoints.Count!=2))return Fail("井字首轮需要四个方向各一进一出的双幅开放端点，共八个点。");
            if(!Finite(offset)||!Finite(gap)||gap<0||!Finite(terrainHeight))return Fail("井字高程、层差或地形数据无效，请调整参数。");
            var selected=arms.OrderBy(a=>a.Index).ToArray();var full=new RoadInfo[4];
            foreach(var arm in selected)
            {
                var p=arm.Endpoints[0];
                if(arm.Endpoints.Any(e=>e.Prefab!=p.Prefab||e.Width!=p.Width||e.Lanes!=p.Lanes))return Fail("同一方向的两幅道路须同款、同宽、同车道。");
                full[arm.Index]=catalog.FirstOrDefault(r=>Valid(r)&&r.Name==p.Prefab&&r.ForwardLanes==p.Lanes&&r.Width==p.Width)!;
                if(full[arm.Index]==null)return Fail($"方向{arm.Index+1}的道路目录与所选断面不一致，或桥墩/净空数据无效，请重新选点。");
                if(p.Lanes<3)return Fail("井字需要同时分配直行、左转和右转，每幅至少3车道；较窄组合尚未接入。");
            }
            for(int i=0;i<2;i++)if(full[i].Name!=full[i+2].Name||full[i].Width!=full[i+2].Width)
                return Fail("井字首轮要求每组相对道路同款同宽，变断面贯通转换尚未接入。");
            var bridge=selected.Select(a=>new EndpointBridgeRule(a.Index,Length(full[a.Index]),full[a.Index].MiddlePillarSpacing,
                full[a.Index].Width,Pillar(full[a.Index]),full[a.Index].GroundHeight,full[a.Index].ElevatedHeight)).ToArray();
            bool highway=full.Count(r=>r.IsHighway)>=2;string last="缺少可精确分配车道的直行、集散或匝道道路。";
            foreach(var ramp in catalog.Where(r=>Valid(r)&&(!ordinaryAutomaticRoads||RampMatcher.IsOrdinaryRoad(r)||r.Name==preferredRamp)
                &&full.All(f=>f.ForwardLanes>=2*r.ForwardLanes+1&&r.Width<=f.Width))
                .OrderBy(r=>r.Name==preferredRamp?0:1).ThenBy(r=>r.ForwardLanes).ThenBy(r=>r.IsHighway==highway?0:1)
                .ThenBy(r=>r.Width).ThenBy(r=>r.Name,StringComparer.Ordinal))
            {
                var roads=new List<EndpointGridArmRoads>();bool found=true;
                for(int i=0;i<4;i++)
                {
                    RoadInfo? Pick(int lanes,float minimumWidth=0)=>catalog.Where(r=>Valid(r)&&(!ordinaryAutomaticRoads||RampMatcher.IsOrdinaryRoad(r))
                        &&r.IsHighway==full[i].IsHighway&&r.ForwardLanes==lanes&&r.Width<=full[i].Width&&r.Width>=minimumWidth)
                        .OrderBy(r=>r.Width).ThenBy(r=>r.Name,StringComparer.Ordinal).FirstOrDefault();
                    var through=Pick(full[i].ForwardLanes-2*ramp.ForwardLanes);var collector=Pick(2*ramp.ForwardLanes,ramp.Width);
                    if(through==null||collector==null){last=$"方向{i+1}缺少精确{full[i].ForwardLanes-2*ramp.ForwardLanes}车道直行道路或{2*ramp.ForwardLanes}车道集散道，请更换道路组合。";found=false;break;}
                    roads.Add(new EndpointGridArmRoads(i,through,collector));
                }
                if(!found)continue;
                var all=full.Concat(roads.SelectMany(r=>new[]{r.Through,r.Collector})).Append(ramp).GroupBy(r=>r.Name).Select(g=>g.First()).ToArray();
                var design=new CloverleafRoadDesign(bridgeRules:bridge,supportRules:all.Select(r=>new EndpointPrefabSupportRule(r.Name,Length(r),r.MiddlePillarSpacing,Pillar(r))));
                float required=EndpointCompactGridCore.RequiredClearance(roads,design,ramp);
                if(!Finite(required))return Fail("井字道路的结构高度或净空数据无效。");
                if(gap>0&&gap<required)return Fail($"井字相邻层至少需要{required:F1}米高差，请增大层差或设为0自动。");
                float separation=gap==0?Math.Max(10,(float)(Math.Ceiling(required*2)/2+.5)):gap;
                double mean=selected.SelectMany(a=>a.Endpoints).Average(p=>(double)p.Position.Y)+offset;
                if(double.IsNaN(mean)||double.IsInfinity(mean)||Math.Abs(mean)>float.MaxValue)return Fail("井字基层高程超出有效范围。");
                float height=(float)mean,low=Math.Max(terrainHeight,height-separation/2);
                if(height<terrainHeight)return Fail("井字首轮基层不能低于参考地形，请提高基层偏移或更换端点。");
                var layers=Enumerable.Range(0,4).Select(i=>low+i*separation).ToArray();
                if(!Finite(height-terrainHeight)||layers.Any(h=>!Finite(h)||!Finite(h-terrainHeight)))return Fail("井字四层高度超出有效范围。");
                return new EndpointCompactGridSetup(true,"",ramp,roads.AsReadOnly(),design,height,separation,required,layers);
            }
            return Fail(last);
        }
    }
}
