using System;
using System.Collections.Generic;
using System.Linq;
using InterchangeMod.Generation.Roads;

namespace InterchangeMod.Generation.Endpoints
{
    public sealed class EndpointRoundaboutSetup
    {
        private EndpointRoundaboutSetup(string diagnostic,RoadInfo? ramp=null,RoadInfo? ring=null,CloverleafRoadDesign? design=null,
            float main=0,float cross=0,float required=0)
        {Diagnostic=diagnostic;Ramp=ramp;Ring=ring;Design=design;MainHeight=main;CrossHeight=cross;RequiredClearance=required;}
        public bool Success=>Ramp!=null&&Ring!=null&&Design!=null;
        public string Diagnostic {get;}public RoadInfo? Ramp {get;}public RoadInfo? Ring {get;}
        public CloverleafRoadDesign? Design {get;}public float MainHeight {get;}public float CrossHeight {get;}
        public float RequiredClearance {get;}
        private static bool Finite(float v)=>!float.IsNaN(v)&&!float.IsInfinity(v);
        private static bool Range(HeightRange? r)=>!r.HasValue||Finite(r.Value.Min)&&Finite(r.Value.Max)&&r.Value.Min<=r.Value.Max;
        private static bool Valid(RoadInfo r)=>r!=null&&!string.IsNullOrWhiteSpace(r.Name)&&r.ForwardLanes>0&&r.BackwardLanes==0
            &&r.PublicTransportLanes==0&&Finite(r.Width)&&r.Width>0&&r.Width<=1000&&Range(r.GroundHeight)&&Range(r.ElevatedHeight)
            &&Finite(r.ElevatedLength)&&(r.ElevatedLength==0||r.ElevatedLength>=10&&r.ElevatedLength<=1000)
            &&Finite(r.PillarWidth)&&r.PillarWidth>=0&&r.PillarWidth<=1000
            &&(!r.MiddlePillarSpacing.HasValue||Finite(r.MiddlePillarSpacing.Value)&&r.MiddlePillarSpacing.Value>=0);
        private static HeightRange Union(HeightRange? a,HeightRange? b)
        {var fallback=new HeightRange(0,HeightRange.MeasuredMinimumGap);var x=a??fallback;var y=b??fallback;
            return new HeightRange(Math.Min(x.Min,y.Min),Math.Max(x.Max,y.Max));}
        public static EndpointRoundaboutSetup Create(IReadOnlyList<EndpointArm> arms,IReadOnlyList<RoadInfo> catalog,
            float offset,float gap,float terrainHeight,string? preferredRamp=null,bool ordinaryAutomaticRoads=false)
        {
            EndpointRoundaboutSetup Fail(string s)=>new EndpointRoundaboutSetup(s);
            if(arms==null||catalog==null||EndpointConstraints.Validate(arms,InterchangeType.Roundabout).Count!=0||arms.Any(a=>a.Endpoints.Count!=2))
                return Fail("环岛首轮需要四个方向各一进一出的双幅开放端点，共八个点。");
            if(!Finite(offset)||!Finite(gap)||gap<0||!Finite(terrainHeight))return Fail("环岛高程、层差或地形数据无效。");
            var selected=arms.OrderBy(a=>a.Index).ToArray();var full=new RoadInfo[4];
            foreach(var arm in selected)
            {
                var p=arm.Endpoints[0];
                if(arm.Endpoints.Any(e=>e.Prefab!=p.Prefab||e.Width!=p.Width||e.Lanes!=p.Lanes))return Fail("环岛同一方向两幅道路须同款同宽同车道。");
                full[arm.Index]=catalog.FirstOrDefault(r=>Valid(r)&&r.Name==p.Prefab&&r.Width==p.Width&&r.ForwardLanes==p.Lanes)!;
                if(full[arm.Index]==null)return Fail($"方向{arm.Index+1}缺少与实际道路断面一致的有效目录或桥墩数据。");
            }
            for(int i=0;i<2;i++)if(full[i].Name!=full[i+2].Name||full[i].Width!=full[i+2].Width)
                return Fail("环岛首轮要求相对方向使用同款同宽道路，变断面转换尚未接入。");
            if(full[0].ForwardLanes<2)return Fail("环岛主线每幅至少两车道，以分配直行及转向匝道。");
            var valid=catalog.Where(Valid).ToArray();
            var automatic=ordinaryAutomaticRoads?valid.Where(RampMatcher.IsOrdinaryRoad).ToArray():valid;
            int ringLanes=Math.Max(2,full[1].ForwardLanes);
            var ring=automatic.Where(r=>r.IsHighway==full[1].IsHighway&&r.ForwardLanes>=ringLanes)
                .OrderBy(r=>r.ForwardLanes).ThenBy(r=>r.Width).ThenBy(r=>r.Name,StringComparer.Ordinal).FirstOrDefault();
            if(ring==null)return Fail($"找不到至少{ringLanes}车道的同类单行环道道路。");
            foreach(var ramp in valid.Where(r=>(!ordinaryAutomaticRoads||RampMatcher.IsOrdinaryRoad(r)||r.Name==preferredRamp)
                &&r.ForwardLanes<full[0].ForwardLanes&&r.Width<=full[0].Width)
                .OrderBy(r=>r.Name==preferredRamp?0:1).ThenBy(r=>r.ForwardLanes).ThenBy(r=>r.IsHighway==full[0].IsHighway?0:1)
                .ThenBy(r=>r.Width).ThenBy(r=>r.Name,StringComparer.Ordinal))
            {
                var balances=EndpointLaneBalance.Select(selected,valid,ramp,ordinaryAutomaticRoads).Where(b=>b.Arm%2==0).ToArray();
                if(balances.Length!=2)continue;
                var reduced=valid.Single(r=>r.Name==balances[0].Prefab);
                // The main family includes the actual reduced variant; its union is conservative for every main piece.
                var bridges=selected.Select(a=>new EndpointBridgeRule(a.Index,full[a.Index].ElevatedLength>0?full[a.Index].ElevatedLength:80,
                    full[a.Index].MiddlePillarSpacing,full[a.Index].Width,full[a.Index].PillarWidth>0?full[a.Index].PillarWidth:4,
                    a.Index%2==0?Union(full[a.Index].GroundHeight,reduced.GroundHeight):full[a.Index].GroundHeight,
                    a.Index%2==0?Union(full[a.Index].ElevatedHeight,reduced.ElevatedHeight):full[a.Index].ElevatedHeight)).ToArray();
                var all=full.Concat(new[]{ramp,ring,reduced}).GroupBy(r=>r.Name).Select(g=>g.First()).ToArray();
                var design=new CloverleafRoadDesign(balances,bridgeRules:bridges,supportRules:all.Select(r=>new EndpointPrefabSupportRule(r.Name,
                    r.ElevatedLength>0?r.ElevatedLength:80,r.MiddlePillarSpacing,r.PillarWidth>0?r.PillarWidth:4)));
                float required=EndpointRoundaboutCore.RequiredClearance(design,ramp,ring);
                if(!Finite(required))return Fail("环岛实际道路结构包络数据无效。");
                if(gap>0&&gap<required)return Fail($"环岛两层至少需要{required:F1}米高差，请增大层差或设为0自动。");
                float separation=gap>0?gap:Math.Max(10,(float)(Math.Ceiling(required*2)/2+.5));
                double average=selected.SelectMany(a=>a.Endpoints).Average(p=>(double)p.Position.Y)+offset;
                float cross=(float)Math.Max(terrainHeight,average),main=cross+separation;
                if(double.IsNaN(average)||double.IsInfinity(average)||!Finite(cross)||!Finite(main)||!Finite(main-terrainHeight))return Fail("环岛层高超出有效范围。");
                return new EndpointRoundaboutSetup("",ramp,ring,design,main,cross,required);
            }
            return Fail("找不到满足主线 N-R 精确减车道的匝道和道路组合，请更换主线道路。");
        }
    }
}
