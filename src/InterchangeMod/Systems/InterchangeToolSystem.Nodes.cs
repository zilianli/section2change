using System;
using System.Collections.Generic;
using System.Linq;
using Game.Tools;
using Game.Common;
using Game.Input;
using Game.Simulation;
using InterchangeMod.Generation;
using InterchangeMod.Generation.Endpoints;
using InterchangeMod.Generation.Geometry;
using InterchangeMod.Placement;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace InterchangeMod.Systems
{
    public partial class InterchangeToolSystem
    {
        public bool nodeMode { get; private set; }
        public int nodeActiveArm { get; private set; }
        public int nodeGradePercent { get; private set; } = 16;
        public int nodeRetreatLimit { get; private set; } = 1000;
        public bool nodePreviewFailed { get; private set; }
        private IReadOnlyList<RoadRetreatResult> m_NodeRetreatPreview;
        private SpatialCandidateResult m_NodeSpatialPreview;
        private string lastTraceDumpFingerprint;
        public void SetNodeRetreatLimit(int distance)
        {
            int next=Math.Max(0,Math.Min(1000,distance));if(next==nodeRetreatLimit)return;
            nodeRetreatLimit=next;m_NodeDirty=true;InvalidateNodeConfirmation();InvalidateNodeSolve();nodePreviewFailed=false;nodeMessage="自动退接上限已更新，请重新生成预览。";
        }
        private string m_NodeMessage = "选择方向分组，再点击该方向的道路开放端点。";
        public string nodeMessage { get => m_NodeMessage; private set { if(m_NodeMessage!=value) Mod.log.Info($"[autochange] {value}"); m_NodeMessage=value; } }
        private EndpointSelection m_NodeSelection = new EndpointSelection(4);
        private string m_NodeType;
        private bool m_NodeWasMode, m_NodeDirty;
        private int m_NodeSettings = -1;
        private bool m_NodeGenerate;
        public IReadOnlyList<EndpointArm> nodeArms => m_NodeSelection.Snapshot();
        public void SetNodeMode(bool enabled)
        {
            if(!enabled&&NodeOnlyType){nodeMessage=CurrentTemplate.DisplayName+"当前仅支持点选节点；四个方向各选一个双向端点，或一进一出的两个单向端点。";return;}
            nodeMode = enabled; m_NodeDirty = true; m_NodeGenerate = false; m_NodeMarkerAge=999; nodePreviewFailed=false;
            InvalidateNodeSolve();
            InvalidateNodeConfirmation();
            if(enabled) nodeMessage="节点模式已开启；选齐有效端点后自动尝试，也可点击底部生成预览。";
        }
        public void SetNodeArm(int arm) { if (arm >= 0 && arm < nodeArms.Count) nodeActiveArm = arm; }
        public void SetNodeGrade(int grade)
        {
            int next=Math.Max(3,Math.Min(30,grade)); if(next==nodeGradePercent) return;
            nodeGradePercent=next; m_NodeDirty=true; InvalidateNodeConfirmation(); InvalidateNodeSolve(); nodePreviewFailed=false; nodeMessage="坡度已更新，旧预览已失效；请点击生成预览。";
        }
        public void ClearNodeArm(int arm)
        {
            if (arm < 0 || arm >= nodeArms.Count) return;
            nodePreviewFailed=false;
            if(nodeArms[arm].Endpoints.Count==0) { nodeMessage=$"方向 {arm+1} 没有端点，请重新选点。"; return; }
            m_NodeSelection.ClearArm(arm); m_NodeDirty = true;
            InvalidateNodeSolve();
            InvalidateNodeConfirmation();
            nodeMessage=$"方向 {arm+1} 已清空，旧预览已失效；请重新选点。";
        }
        public void RequestNodePreview()
        {
            Mod.log.Info($"[autochange] preview button received: nodeMode={nodeMode}, paused={paused}, browsing={browsing}");
            if (nodeMode)
            {
                m_NodeGenerate = true; m_NodeDirty = true; nodePreviewFailed=false;
                InvalidateNodeConfirmation();
                nodeMessage=paused ? "已收到生成请求，等待工具处理。" : "已收到请求，请先暂停游戏后重新生成。";
            }
        }

        private static Entity NodeEntity(SelectedEndpoint p) => new Entity { Index = p.NodeIndex, Version = p.NodeVersion };

        private JobHandle UpdateNodeMode(JobHandle deps)
        {
            m_Pinned = false;
            if (m_ZoomBarrier != null) m_ZoomBarrier.blocked = false;
            if (m_NodeType != setting.InterchangeType)
            {
                InvalidateNodeSolve();
                m_NodeType = setting.InterchangeType;
                var type = Enum.TryParse(m_NodeType,out InterchangeType parsed) ? EndpointTypeCatalog.Find(parsed) : null;
                m_NodeSelection = new EndpointSelection(type?.ArmCount ?? 4);
                nodeActiveArm = 0; m_NodeDirty = true; m_NodeGenerate = false; nodePreviewFailed=false;
                m_NodeSettings=m_SettingsVersion;
                nodeMessage = "选择方向分组，再点击该方向的道路开放端点。";
            }
            if (m_NodeSettings != m_SettingsVersion)
            {
                InvalidateNodeSolve();
                m_NodeSettings = m_SettingsVersion; m_NodeDirty = true; nodePreviewFailed=false;
                nodeMessage="参数已更新，旧预览已失效；请点击生成预览。";
            }
            if (browsing || !paused)
            {
                if(m_MixedNodeSolvePending)InvalidateNodeSolve();
                m_NodeGenerate = false;
                if (m_HasPreview || m_NodeDirty || nodePreviewFailed)
                {
                    if(m_HasPreview || nodePreviewFailed) nodeMessage="预览已清除，请进入设置页并暂停游戏后重新生成。";
                    nodePreviewFailed=false;
                    ResetNodePreview(); m_NodeDirty = false; return Clear(deps);
                }
                applyMode = ApplyMode.None; return deps;
            }
            if (cancelAction.WasPressedThisFrame())
            { InvalidateNodeSolve(); ClearNodeArm(nodeActiveArm); nodeMessage = "已清空当前方向；其他分组保留。"; }
            float3? markerCursor=null;
            Entity hovered=Entity.Null;
            if (!InputManager.instance.mouseOverUI && GetRaycastResult(out Entity markerHit,out RaycastHit markerInfo))
            {
                markerCursor=markerInfo.m_HitPosition;
                hovered=OpenEndpointReader.FromHit(EntityManager,markerHit,markerInfo.m_HitPosition);
                if (applyAction.WasPressedThisFrame())
                {
                    Entity node = hovered;
                    if (OpenEndpointReader.TryRead(EntityManager,m_PrefabSystem,m_RoadsByName,node,out var port,out var error))
                    {
                        var position = port.Position;
                        if (math.distance(new float2(position.X,position.Z),markerInfo.m_HitPosition.xz) > 32)
                            nodeMessage = "请靠近开放端点点击（32 米以内）。";
                        else
                        {
                            var change = m_NodeSelection.Toggle(nodeActiveArm,port);
                            nodeMessage = change == EndpointSelectionChange.Added ? "已加入当前方向。选齐后点击生成预览。"
                                : change == EndpointSelectionChange.Removed ? "已取消该端点。"
                                : change == EndpointSelectionChange.ArmFull ? "每个方向最多两个端点。" : "该端点已在其他方向，请先清除原分组。";
                            if(NodeFeedback.SelectionChanged(change)) { m_NodeDirty = true; InvalidateNodeConfirmation(); InvalidateNodeSolve(); nodePreviewFailed=false; }
                            if(change==EndpointSelectionChange.Added && Enum.TryParse(setting.InterchangeType,out InterchangeType readyType)
                                && NodeFeedback.ReadyForAutoPreview(readyType,nodeArms))
                            { m_NodeGenerate=true; nodePreviewFailed=false; nodeMessage="有效端点已选齐，正在自动尝试预览。"; }
                        }
                    }
                    else nodeMessage = error;
                }
            }
            DrawNodeFeedback(markerCursor,hovered);
            if (m_NodeDirty)
            {
                if(!m_NodeGenerate)InvalidateNodeSolve();
                InvalidateNodeConfirmation(); // A queued click belongs to the old revision, even if this update builds a new preview.
                deps = Clear(deps); ResetNodePreview(); snapshot = null; m_NodeDirty = false;
                m_LastLoggedGameErrors=null;
                if (m_NodeGenerate)
                {
                    Mod.log.Info("[autochange] preview dispatch: entering solver");
                    m_NodeGenerate = false;
                    nodePreviewFailed=false;
                    try { BuildNodePreview(); nodePreviewFailed=!m_HasPreview&&!m_MixedNodeSolvePending; }
                    catch (Exception ex) { InvalidateNodeSolve(); m_NodeDirty = true; m_HasPreview=false; nodePreviewFailed=true; nodeMessage = "节点预览异常，详情已记录到日志。"; Mod.log.Error(ex); }
                }
                return deps;
            }
            if(FinishCompletedMixedNodeSolve())return deps;
            UpdateNodeNativeEligibility();
            if(m_ConfirmRequested)
            {
                m_ConfirmRequested=false;
                var applied=TryApplyNodePreview(deps);
                if(applied.HasValue)return applied.Value;
            }
            applyMode = ApplyMode.None;
            if (m_HasPreview && ++m_ErrorRefreshCounter % kErrorRefreshInterval == 0) RefreshGameErrors();
            return deps;
        }

        private void BuildNodePreview()
        {
            if (!Enum.TryParse(setting.InterchangeType,out InterchangeType type)) { nodeMessage = "未知立交类型。"; return; }
            var frozen = nodeArms;
            Mod.log.Info($"[autochange] preview attempt: type={type}, grade={nodeGradePercent}%, offset={setting.BaseElevation}, gap={setting.LevelGap}; "
                +string.Join("; ",frozen.SelectMany(a=>a.Endpoints.Select(p=>$"arm={a.Index+1}, node={p.NodeIndex}:{p.NodeVersion}, pos={p.Position}, tangent={p.OutwardTangent}, outward={p.TravelOutward}, width={p.Width}, prefab={p.Prefab}"))));
            foreach (var a in frozen)
                foreach (var p in a.Endpoints)
                    if (!OpenEndpointReader.TryRead(EntityManager,m_PrefabSystem,m_RoadsByName,NodeEntity(p),out var fresh,out _) || !OpenEndpointReader.SameNode(p,fresh))
                    { nodeMessage = "已选道路发生变化，请清空对应方向并重新选择。"; return; }
            var request = EndpointTypeRequest.Create(type,frozen,m_CityConfigurationSystem.leftHandTraffic == false,
                nodeGradePercent / 100f,Enumerable.Repeat((float)nodeRetreatLimit,frozen.Count).ToArray());
            if(request.Arms.Any(a=>!a.Endpoints.SequenceEqual(frozen.First(old=>old.Index==a.Index).Endpoints)))
            {
                var reordered=new EndpointSelection(request.Arms.Count);
                var active=frozen.First(a=>a.Index==nodeActiveArm).Endpoints.FirstOrDefault();
                Mod.log.Info("[autochange] node arm order identified: "+string.Join("; ",request.Arms.Select(a=>
                    $"selection={frozen.First(old=>old.Endpoints.Contains(a.Endpoints[0])).Index+1} -> direction={a.Index+1}")));
                foreach(var a in request.Arms)foreach(var p in a.Endpoints)reordered.Toggle(a.Index,p);
                m_NodeSelection=reordered;m_NodeMarkerAge=999;
                if(active!=null)nodeActiveArm=request.Arms.First(a=>a.Endpoints.Contains(active)).Index;
            }
            frozen=request.Arms;
            if (!request.InputReady)
            { nodeMessage = string.Join("；",request.Problems.Select(p=>$"方向 {p.ArmIndex+1}：{NodeProblem(p.Code)}")); return; }
            var frame = EndpointFrame.Estimate(frozen,type);
            if (frame.Failure != EndpointFrameFailure.None) { nodeMessage = "道路轴线无法确定中心，请检查分组和朝向。"; return; }
            bool stacked=type==InterchangeType.Stack||type==InterchangeType.Turbine;
            bool threeWay=EndpointThreeWaySearch.Supported(type);
            bool wye=type==InterchangeType.Wye;
            bool grid=type==InterchangeType.CompactGrid;
            bool roundabout=type==InterchangeType.Roundabout;
            if (type != InterchangeType.Cloverleaf&&!stacked&&!threeWay&&!grid&&!roundabout)
            { nodeMessage = type==InterchangeType.Diamond||type==InterchangeType.SinglePoint||type==InterchangeType.ExpresswayNode||type==InterchangeType.ExpresswayT
                ?"该类型暂不开发新的节点几何，可使用原自动或手动模式。"
                :"端点输入检查通过。当前测试包支持苜蓿叶、定向、涡轮、喇叭、定向T、半定向T、Y型、井字和环岛节点预览。"; return; }
            var terrain=m_TerrainSystem.GetHeightData();
            var center=frame.Center.Value;
            float ground=TerrainUtils.SampleHeight(ref terrain,new float3(center.X,0,center.Z));
            if (frozen.SelectMany(a=>a.Endpoints).Any(p=>!p.OneWay))
            {
                BuildMixedNodePreview(request,ref terrain,ground);
                return;
            }
            if (frozen.SelectMany(a=>a.Endpoints).Any(p=>m_RoadsByName[p.Prefab].ForwardLanes == 0))
            { nodeMessage = "当前预览暂不支持仅逆向车道的道路预制体。"; return; }
            foreach (var arm in frozen)
            {
                if(wye||(threeWay&&arm.Index==1))continue;
                var from=arm.Endpoints.First(p=>!p.TravelOutward);
                var to=frozen.First(a=>a.Index==(arm.Index+2)%4).Endpoints.First(p=>p.TravelOutward);
                if (from.Prefab!=to.Prefab || from.Width!=to.Width)
                { nodeMessage="首轮预览要求每条贯通车流两端使用相同道路（预制体与宽度），变断面转换尚未接入。"; return; }
            }
            var gridSetup=grid?EndpointCompactGridSetup.Create(frozen,m_RoadCatalog,setting.BaseElevation,setting.LevelGap,ground,setting.RampPrefab):null;
            if(grid&&!gridSetup.Success){nodeMessage=gridSetup.Diagnostic;return;}
            var roundaboutSetup=roundabout?EndpointRoundaboutSetup.Create(frozen,m_RoadCatalog,setting.BaseElevation,setting.LevelGap,ground,setting.RampPrefab):null;
            if(roundabout&&!roundaboutSetup.Success){nodeMessage=roundaboutSetup.Diagnostic;return;}
            var wyeSetup=wye?EndpointWyeSetup.Create(frozen,m_RoadCatalog,setting.BaseElevation,setting.LevelGap,ground,setting.RampPrefab):null;
            if(wye&&!wyeSetup.Success){nodeMessage=wyeSetup.Diagnostic;return;}
            var ramp = roundabout?roundaboutSetup.Ramp:grid?gridSetup.Ramp:wye?wyeSetup.Ramp:m_RoadCatalog.FirstOrDefault(r=>r.Name == setting.RampPrefab && r.IsOneWay && r.ForwardLanes>0)
                ?? m_RoadCatalog.Where(r=>r.IsOneWay && r.ForwardLanes>0 && r.LanesPerDirection==1 && r.IsHighway).OrderBy(r=>r.Width).FirstOrDefault();
            if (ramp == null) { nodeMessage = "找不到正向单车道公路匝道，请在手动模式选择单向匝道道路。"; return; }
            IReadOnlyList<EndpointLaneBalance> balances=roundabout?roundaboutSetup.Design.Balances:wye||grid?Array.Empty<EndpointLaneBalance>():EndpointLaneBalance.Select(frozen,m_RoadCatalog,ramp);
            var shared=type==InterchangeType.Cloverleaf&&setting.CloverleafHugLoop?CloverleafRoadDesign.SelectSharedRamp(m_RoadCatalog,ramp):null;
            if(type==InterchangeType.Cloverleaf&&setting.CloverleafHugLoop&&shared==null)
            {nodeMessage="贴环道需要每方向车道数与匝道相等的双向道路，当前未找到；请选择其他匝道道路，或切回外侧大弧线。";return;}
            var balanceWarnings=wye||grid||roundabout?Array.Empty<string>():frozen.Where(a=>!balances.Any(b=>b.Arm==a.Index)).Select(a=>$"方向 {a.Index+1}：未找到精确减车道道路，保留全宽。").ToArray();
            Mod.log.Info("[autochange] lane balance: "+string.Join("; ",balances.Select(b=>$"arm={b.Arm+1}, reduced={b.Prefab}, lanes={b.Lanes}, width={b.Width}, rampLanes={b.RampLanes}")));
            var levels=wye||grid||roundabout?null:EndpointLevels.Resolve(frozen,setting.CloverleafCrossOnTop,setting.BaseElevation,setting.LevelGap,ground);
            float main=roundabout?roundaboutSetup.MainHeight:grid?gridSetup.BaseHeight:wye?wyeSetup.BaseHeight:levels.Main,
                cross=roundabout?roundaboutSetup.CrossHeight:grid?gridSetup.Layers[3]:wye?wyeSetup.UpperHeight:levels.Cross;
            float terrainLift=levels?.TerrainLift??0;
            if(wye)Mod.log.Info($"[autochange] wye setup: ramp={ramp.Name}, lanesPerRamp={ramp.LanesPerDirection}, requiredClearance={wyeSetup.RequiredClearance:F3}");
            if(roundabout)Mod.log.Info($"[autochange] roundabout setup: ramp={ramp.Name}, lanesPerRamp={ramp.LanesPerDirection}, ring={roundaboutSetup.Ring.Name}, ringLanes={roundaboutSetup.Ring.LanesPerDirection}, requiredClearance={roundaboutSetup.RequiredClearance:F3}, racetrack={setting.RoundaboutRacetrack}");
            if(threeWay&&!wye&&type!=InterchangeType.Trumpet&&setting.DirectionalTwoLevel
                &&LevelPlanner.BandOf(main-ground)==ElevationBand.Elevated)
            {nodeMessage="主线在高架上时，两层T型不能下穿；请切换为三层后重新生成预览。";return;}
            // Plan the higher crossing conservatively for every road family. A center
            // terrain sample alone cannot prove that no elevated support will appear.
            IReadOnlyList<EndpointBridgeRule> bridgeRules=grid?gridSetup.Design.BridgeRules:roundabout?roundaboutSetup.Design.BridgeRules:wye?wyeSetup.BridgeRules:frozen.Select(a=>{
                    var road=m_RoadsByName[a.Endpoints.First(p=>!p.TravelOutward).Prefab];
                    return new EndpointBridgeRule(a.Index,road.ElevatedLength>0?road.ElevatedLength:80,road.MiddlePillarSpacing,road.Width,
                        road.PillarWidth>0?road.PillarWidth:4,road.GroundHeight,road.ElevatedHeight);
                }).ToArray();
            var supportNames=frozen.SelectMany(a=>a.Endpoints).Select(p=>p.Prefab).Concat(balances.Select(b=>b.Prefab)).Concat(new[]{ramp.Name}).Distinct();
            IReadOnlyList<EndpointPrefabSupportRule> supportRules=grid?gridSetup.Design.SupportRules:roundabout?roundaboutSetup.Design.SupportRules:supportNames.Select(name=>{
                var road=m_RoadsByName[name];
                return new EndpointPrefabSupportRule(name,road.ElevatedLength>0?road.ElevatedLength:80,
                    road.MiddlePillarSpacing,road.PillarWidth>0?road.PillarWidth:4);
            }).ToArray();
            var design=grid?gridSetup.Design:roundabout?roundaboutSetup.Design:new CloverleafRoadDesign(balances,bridgeRules:bridgeRules,hugLoops:type==InterchangeType.Cloverleaf&&setting.CloverleafHugLoop,
                sharedRampPrefab:shared?.Name,sharedRampWidth:shared?.Width??0,supportRules:supportRules);
            Mod.log.Info("[autochange] bridge rules: "+string.Join("; ",bridgeRules.Select(r=>$"arm={r.Arm+1}, maxEdge={r.MaxLength}, middleSpacing={r.MiddleSpacing?.ToString()??"none"}, width={r.Width}")));
            Mod.log.Info($"[autochange] levels: main={main:F3}, cross={cross:F3}, centerTerrain={ground:F3}, autoLift={terrainLift:F3}, axisResidual={frame.ResidualRms:F3}");
            var traces=new List<RoadApproachTrace>();var boundaries=new List<string>();
            foreach(var a in frozen)
                foreach(var p in a.Endpoints)
                {
                    var trace=RoadApproachReader.Read(EntityManager,m_PrefabSystem,m_RoadsByName,p,nodeRetreatLimit,out var reason);
                    traces.Add(trace);boundaries.Add($"方向{a.Index+1} #{p.NodeIndex} 可退接{Math.Max(0,Math.Min(nodeRetreatLimit,trace.Length-10)):F0}m ({reason})");
                }
            Mod.log.Info("[autochange] traced corridors: "+string.Join("; ",boundaries));
            if(grid)Mod.log.Info($"[autochange] grid setup: layers=[{string.Join(",",gridSetup.Layers)}], ramp={ramp.Name}, rampLanes={ramp.LanesPerDirection}, requiredClearance={gridSetup.RequiredClearance:F3}; "+string.Join("; ",gridSetup.Roads.Select(r=>$"arm={r.Arm+1}, through={r.Through.Name}, throughLanes={r.Through.LanesPerDirection}, collector={r.Collector.Name}, collectorLanes={r.Collector.LanesPerDirection}")));
            var search=roundabout?EndpointRoundaboutSearch.Search(frozen,traces,request.RightHandTraffic,main,cross,ramp,roundaboutSetup.Ring,
                    request.MaxGrade,nodeRetreatLimit,design,verticalClearance:roundaboutSetup.RequiredClearance,terrainHeight:ground,racetrack:setting.RoundaboutRacetrack)
                :grid?EndpointCompactGridSearch.Search(frozen,traces,request.RightHandTraffic,main,gridSetup.LevelGap,ramp,gridSetup.Roads,
                    request.MaxGrade,nodeRetreatLimit,design,terrainHeight:ground):stacked
                ?EndpointStackFitter.Search(frozen,traces,type,request.RightHandTraffic,main,cross,ramp,request.MaxGrade,nodeRetreatLimit,design,setting.StackLeftExit,
                    diagnostic:setting.DebugLogging?(Action<string>)(message=>Mod.log.Info("[autochange-fit] "+message)):null)
                :threeWay?EndpointThreeWaySearch.Search(frozen,traces,type,request.RightHandTraffic,main,cross,ramp,request.MaxGrade,nodeRetreatLimit,design,
                    setting.TrumpetLoopForExit,setting.DirectionalTwoLevel,terrainHeight:ground)
                :RetreatCloverleafSearch.Search(frozen,traces,request.RightHandTraffic,main,cross,ramp.Width,request.MaxGrade,nodeRetreatLimit,design:design);
            Mod.log.Info($"[autochange] search: attempts={search.Attempts}, clearanceRejected={search.ClearanceRejected}, lastClearance={search.ClearanceDetail}, supportRejected={search.SupportRejected}, lastSupport={search.SupportDetail}");
            if(search.Failure!=RetreatSearchFailure.None || search.Spatial==null)
            {
                if(stacked||threeWay||grid||roundabout)
                {
                    try
                    {
                        var lines=EndpointTraceDump.Encode(traces);
                        var fingerprint=string.Join("\n",lines);
                        if(!string.Equals(lastTraceDumpFingerprint,fingerprint,StringComparison.Ordinal))
                        {
                            foreach(var line in lines) Mod.log.Info("[autochange-trace] "+line);
                            lastTraceDumpFingerprint=fingerprint;
                        }
                    }
                    catch(ArgumentException ex)
                    { Mod.log.Info("[autochange] real-road trace dump rejected: "+ex.Message); }
                }
                nodeMessage=stacked||threeWay||grid||roundabout
                    ?EndpointPreviewMessages.StackFailure(search,nodeRetreatLimit)
                    :$"自动退接未找到预览（{search.Attempts} 个候选，{search.Failure}）：{PlanarReason(search.LastPlanarFailure)}；纵断面 {search.LastSpatialFailure}，空间检查拒绝 {search.ClearanceRejected} 个，桥墩检查拒绝 {search.SupportRejected} 个。上限每臂{nodeRetreatLimit}m；遇路口/尽头会停止。详细结果已写入日志。";
                return;
            }
            var candidate=search.Spatial;
            if((stacked||grid||roundabout)&&search.ApproachQuality!=null)
                Mod.log.Info($"[autochange-fit] selected: fromCuts={search.ReferenceFromCuts}, curvedCore={search.CoreFollowsCurve}, center={search.ReferenceCenter}, peakCurvature={search.ApproachQuality.PeakCurvature:F6}, curvatureJump={search.ApproachQuality.CurvatureJump:F6}, score={search.ApproachQuality.Score:F4}");
            var errors=PrepareNodePreview(search,traces,ramp.Name,ref terrain,out var sources,out var prepared);
            snapshot.Warnings.AddRange(balanceWarnings);
            if(grid&&EndpointPlacementHints.ShouldSuggestNoCollisionForCompactGrid(search.Arms))
                snapshot.Warnings.Add(EndpointPlacementHints.CompactGridNoCollisionHint);
            if(terrainLift>0)snapshot.Warnings.Add($"混合高度自动整体抬高 {terrainLift:F1} m，使上层跨线段达到高架桥阈值；外部接点保留原高程。");
            if (errors.Count != 0) { nodeMessage = string.Join("；",errors); return; }
            snapshot.Warnings.Add("节点预览按真实原路退接；确认后将改造虚线范围内的原路并保留外侧尾段。请检查原生错误、接回位置和桥墩后在面板确认，实际通行仍需游戏验证。");
            if(grid)snapshot.Warnings.Add($"井字四层标高：{string.Join(" / ",gridSetup.Layers.Select(h=>$"{h:F1}"))} m；各原路端点高程由外部接路保留。");
            if(roundabout)snapshot.Warnings.Add($"环岛两层标高：主线 {main:F1} m，环道 {cross:F1} m；核心按退接道路割线确定中心与方向，外围有界平滑接路保留原端点高程。预览检查要求12种非同臂流向，环道允许合法绕行掉头；实际车道连接与通行待游戏验证。");
            if(roundabout)snapshot.Warnings.Add($"实际环道：{roundaboutSetup.Ring.Name}，{roundaboutSetup.Ring.LanesPerDirection}车道；实际匝道：{ramp.Name}，{ramp.LanesPerDirection}车道。");
            if(grid)snapshot.Warnings.Add($"实际匝道：{ramp.Name}；"+string.Join("；",gridSetup.Roads.GroupBy(r=>(r.Through.Name,r.Collector.Name)).Select(g=>
                $"方向{string.Join("/",g.Select(r=>r.Arm+1))}直行：{g.Key.Item1}，集散：{g.Key.Item2}")));
            if(stacked)snapshot.Warnings.Add(search.ReferenceFromCuts
                ?search.CoreFollowsCurve?"已按退接道路弯曲主线与相邻匝道，并优化外部接路。":"已按退接后的道路重新确定中心与方向，并优化接路曲率；本次保留直线核心。"
                :"本轮未找到更平滑的安全布局，保留原有预览方案。");
            if(threeWay)snapshot.Warnings.Add(wye?"Y型三条臂无主次，已按各臂断面和交角重新求解；原有高差由外部接路过渡，各端保留原路高程。":"三向核心已按道路断面和交角重新求解；主支路原有高差由外部接路过渡，各端保留原路高程。");
            snapshot.Warnings.Add(grid
                ?"井字车道分配（N=T+2R）："+string.Join("；",gridSetup.Roads.Select(r=>$"方向{r.Arm+1}直行{r.Through.LanesPerDirection}车道+集散{r.Collector.LanesPerDirection}车道，集散再分为左右各{ramp.LanesPerDirection}车道"))+"。实际车道连接待游戏验证。"
                :roundabout
                ?"环岛主线车道分配（N−R）："+string.Join("；",balances.Select(b=>$"方向{b.Arm+1}：{frozen.First(a=>a.Index==b.Arm).Endpoints.First(p=>!p.TravelOutward).Lanes}−{b.RampLanes}={b.Lanes}车道，驶出至汇入间使用{b.Prefab}"))+"；方向2/4直接连接环道。实际车道连接待游戏验证。"
                :wye
                ?$"Y型车道分配：每幅{ramp.LanesPerDirection*2}车道分为两条{ramp.LanesPerDirection}车道匝道；已匹配{ramp.Name}，不设贯通减车道段。实际车道连接待游戏验证。"
                :threeWay
                ?$"车道平衡匹配 {balances.Count}/3 幅；按该T型模板应用分合流和匝道外缘对齐，实际车道连接待游戏验证。"
                :stacked
                ?$"车道平衡匹配 {balances.Count}/4 幅：{(type==InterchangeType.Stack&&setting.StackLeftExit?"左转驶出至汇入间减车道，外侧直行边缘保持":"右转驶出至汇入间减车道")}；分合流及实际车道连接待游戏验证。"
                :$"车道平衡已启用 {balances.Count}/4 幅：分流至汇入间减车道，中央交织段保持全宽；匝道外缘对齐。");
            if(shared!=null)snapshot.Warnings.Add($"贴环道：右转与环道共用双向 {shared.Name}，每方向 {shared.LanesPerDirection} 车道；共享弧线和分合流仍需游戏验证。");
            FinishNodePreview(search,sources,ramp.Name,prepared,type);
            if(grid)nodeMessage+=search.CoreFollowsCurve?" 已随原路曲线匹配。":" 采用平直布局，外围接回原路。";
            if(roundabout)nodeMessage+=$" 环岛两层{(setting.RoundaboutRacetrack?"跑道形":"圆形")}预览，按原路割线确定核心方向，外围有界接回原路。";
        }
        private void BuildMixedNodePreview(EndpointTypeRequest request,ref TerrainHeightData terrain,float ground)
        {
            var frozen=request.Arms;
            // Only mixed Stack uses the worker. Other templates retain their existing
            // owner-thread path, including those with mutable static template caches.
            var catalog=request.Type==InterchangeType.Stack?FreezeNodeSolveCatalog(m_RoadCatalog):m_RoadCatalog;
            var setup=EndpointMixedRoadSetup.Create(frozen,catalog,request.Type,request.RightHandTraffic,
                setting.BaseElevation,setting.LevelGap,ground,setting.RampPrefab,setting.CloverleafCrossOnTop,
                setting.CloverleafHugLoop,setting.StackLeftExit,setting.TrumpetLoopForExit,setting.DirectionalTwoLevel,
                setting.RoundaboutRacetrack);
            if(!setup.Success){nodeMessage=setup.Diagnostic;return;}
            var traces=new List<RoadApproachTrace>();var boundaries=new List<string>();
            foreach(var arm in frozen)
                foreach(var port in arm.Endpoints)
                {
                    // The mathematical divided-road proxies must never be sent to the game reader.
                    var trace=RoadApproachReader.Read(EntityManager,m_PrefabSystem,m_RoadsByName,port,nodeRetreatLimit,out var reason);
                    traces.Add(trace);boundaries.Add($"方向{arm.Index+1} #{port.NodeIndex} 可退接{Math.Max(0,Math.Min(nodeRetreatLimit,trace.Length-10)):F0}m ({reason})");
                }
            Mod.log.Info("[autochange] mixed actual corridors: "+string.Join("; ",boundaries));
            Mod.log.Info($"[autochange] mixed setup: type={request.Type}, ramp={setup.Ramp.Name}, main={setup.MainHeight:F3}, cross={setup.CrossHeight:F3}, requiredClearance={setup.RequiredClearance:F3}");
            if(request.Type==InterchangeType.Stack)
            {
                QueueMixedNodeSolve(request,setup,traces,catalog,ground);
                return;
            }
            var search=EndpointMixedRoadSearch.Search(frozen,traces,setup,request.MaxGrade,nodeRetreatLimit);
            CompleteMixedNodePreview(request,setup,traces,search,ref terrain);
        }
        private void CompleteMixedNodePreview(EndpointTypeRequest request,EndpointMixedRoadSetup setup,
            IReadOnlyList<RoadApproachTrace> traces,RetreatSearchResult search,ref TerrainHeightData terrain)
        {
            var frozen=request.Arms;
            Mod.log.Info($"[autochange] mixed search: attempts={search.Attempts}, clearanceRejected={search.ClearanceRejected}, lastClearance={search.ClearanceDetail}, supportRejected={search.SupportRejected}, lastSupport={search.SupportDetail}");
            if(search.Failure!=RetreatSearchFailure.None||search.Spatial==null)
            {
                try
                {
                    var lines=EndpointTraceDump.Encode(traces);
                    var fingerprint=string.Join("\n",lines);
                    if(!string.Equals(lastTraceDumpFingerprint,fingerprint,StringComparison.Ordinal))
                    {
                        foreach(var line in lines)Mod.log.Info("[autochange-trace] "+line);
                        lastTraceDumpFingerprint=fingerprint;
                    }
                }
                catch(ArgumentException ex){Mod.log.Info("[autochange] real-road trace dump rejected: "+ex.Message);}
                nodeMessage=EndpointPreviewMessages.StackFailure(search,nodeRetreatLimit);
                return;
            }
            var errors=PrepareNodePreview(search,traces,setup.Ramp.Name,ref terrain,out var sources,out var prepared);
            if(errors.Count!=0){nodeMessage=string.Join("；",errors);return;}
            snapshot.Warnings.Add("节点预览按真实原路退接；确认后将改造虚线范围内的原路并保留外侧尾段。请检查原生错误、接回位置和桥墩后在面板确认，实际通行仍需游戏验证。");
            snapshot.Warnings.Add("双向转换已计入各臂退接范围；双向干线与两条单向转换曲线连接既有立交核心，各方向车道数保持不变。请检查转换接点、桥墩和车道连接。");
            foreach(var arm in frozen.Where(a=>a.Endpoints.Count==1))
            {
                var port=arm.Endpoints[0];
                snapshot.Warnings.Add($"方向{arm.Index+1}原路：双向 {port.Prefab}，每方向 {port.Lanes} 车道；外侧接点保留原路高程。");
            }
            snapshot.Warnings.Add($"实际匝道：{setup.Ramp.Name}，{setup.Ramp.LanesPerDirection}车道；内部参考标高 {setup.MainHeight:F1} / {setup.CrossHeight:F1} m。");
            if(setup.Ring!=null)snapshot.Warnings.Add($"实际环道：{setup.Ring.Name}，{setup.Ring.LanesPerDirection}车道；预览要求12种非同臂流向，实际车道连接与通行待游戏验证。");
            if(request.Type==InterchangeType.CompactGrid&&EndpointPlacementHints.ShouldSuggestNoCollisionForCompactGrid(search.Arms))
                snapshot.Warnings.Add(EndpointPlacementHints.CompactGridNoCollisionHint);
            if(setup.Design.SharedRampPrefab!=null)
                snapshot.Warnings.Add($"贴环道共享双向道路：{setup.Design.SharedRampPrefab}；共享弧线和分合流仍需游戏验证。");
            FinishNodePreview(search,sources,setup.Ramp.Name,prepared,request.Type);
            nodeMessage+=" 双向及混合道路已按每方向真实车道数转换，虚线范围包含转换段。";
        }
        private List<string> PrepareNodePreview(RetreatSearchResult search,IReadOnlyList<RoadApproachTrace> actualTraces,
            string rampName,ref TerrainHeightData terrain,out IReadOnlyList<RoadApproachTrace> sources,
            out NodePreviewPlacer.PreparedBatch prepared)
        {
            // Cut ports are displaced solver points. Always bind back to the original, real corridors.
            try{sources=NodePreviewSources.Bind(actualTraces,search.Cuts);}
            catch(ArgumentException ex)
            {
                sources=Array.Empty<RoadApproachTrace>();prepared=null;
                var mappingErrors=new List<string>{"原路退接映射无效，请重新选点后生成预览。"};
                snapshot=new PanelSnapshot();snapshot.Errors.AddRange(mappingErrors);
                Mod.log.Info("[autochange] real-road source binding rejected: "+ex.Message);
                return mappingErrors;
            }
            var errors=NodePreviewPlacer.Create(m_ToolOutputBarrier.CreateCommandBuffer(),search.Spatial,search.Arms,rampName,
                ref terrain,m_PrefabResolver,m_SelectedRoad,m_RandomSeed,EntityManager,search.Cuts,sources,out prepared);
            snapshot=new PanelSnapshot();snapshot.Errors.AddRange(errors);
            return errors;
        }
        private void FinishNodePreview(RetreatSearchResult search,IReadOnlyList<RoadApproachTrace> sources,
            string rampName,NodePreviewPlacer.PreparedBatch prepared,InterchangeType type)
        {
            var candidate=search.Spatial;
            m_NodeRetreatPreview=search.Cuts;
            m_NodeSpatialPreview=candidate;
            m_HasPreview=true;
            StageNodeConfirmation(sources,search.Arms,rampName,prepared);
            nodeMessage=$"已生成 {candidate.Segments.Count} 段临时道路；各方向退接 {string.Join(" / ",search.Distances.Select(d=>$"{d:F0}"))} m，改造 {search.Cuts.Sum(c=>c.Consumed.Count)} 段原路，保留 {search.Cuts.Count(c=>c.Remainder.HasValue)} 段尾段，避开 {search.ClearanceRejected} 个空间冲突候选。虚线标出改造范围，白点为新接点。坡度上限仍为{nodeGradePercent}%；等待游戏检查通过后，在面板确认放置。";
            Mod.log.Info($"[autochange] node preview: {candidate.Segments.Count} segments; grade={nodeGradePercent}%; type={type}");
        }
        private static string NodeProblem(EndpointProblemCode code) => code switch
        {
            EndpointProblemCode.InvalidPortCount => "选一个双向端点，或一进一出的两个单向端点",
            EndpointProblemCode.InvalidDirection => "两幅道路必须一进一出",
            EndpointProblemCode.LaneMismatch => "相对贯通车流的车道数不一致",
            EndpointProblemCode.DuplicateNode => "端点重复", EndpointProblemCode.InvalidGeometry => "端点几何无效",
            _ => code.ToString(),
        };
        private static string PlanarReason(PlanarCandidateFailure code) => code switch
        {
            PlanarCandidateFailure.NonStraightApproach => "端部朝向与主体连线偏差超过 20°，无法平滑接路；请检查分组或换接点",
            PlanarCandidateFailure.AttachmentsOutsideApproach => "选中端点以内的空间不足以放下分合流",
            PlanarCandidateFailure.ConflictingAttachmentOrder => "分合流顺序或节点间距不满足要求，请核对分组方向和空间",
            PlanarCandidateFailure.MergeCannotClear => "道路宽度与匝道半径不能满足汇入避让长度",
            PlanarCandidateFailure.MergeOverlapConflict => "主线节点落入汇入重叠区",
            PlanarCandidateFailure.None => "平面候选通过",
            _ => code.ToString(),
        };
    }
}
