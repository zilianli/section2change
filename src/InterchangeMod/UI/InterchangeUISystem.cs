using System.Collections.Generic;
using Colossal.UI.Binding;
using Game;
using Game.SceneFlow;
using Game.Simulation;
using Game.UI;
using InterchangeMod.Generation;
using InterchangeMod.Generation.Roads;
using InterchangeMod.Generation.Text;
using InterchangeMod.Systems;

namespace InterchangeMod.UI
{
    /// <summary>
    /// 工具面板与 C# 之间的绑定（组名 interchangeMod）：
    ///   state  —— 面板要显示的全部状态（JSON 字符串）：是否激活、各参数、实际用到的道路、匝道选择、错误；
    ///   roads  —— 道路选择表（JSON 字符串）：每种道路的车道、类型、宽度、最近使用排名；
    ///   set(key, value) —— 面板修改参数，写进设置并保存，下一帧重建预览；
    ///   close —— 关闭工具；toggle —— 打开 / 关闭工具（左上角快捷按钮，与快捷键相同）。
    /// ValueBinding 只在字符串变化时才发给 UI，所以每帧重建 JSON 的开销只在 C# 这边。
    /// </summary>
    public partial class InterchangeUISystem : UISystemBase
    {
        public const string kGroup = "interchangeMod";

        private InterchangeToolSystem m_ToolSystem;
        /// <summary>游戏速度（selectedSpeed 为 0 = 暂停）：面板在游戏运行时提示先暂停，人口多时求解不容易卡死。</summary>
        private SimulationSystem m_SimulationSystem;
        private ValueBinding<string> m_State;
        private ValueBinding<string> m_Roads;
        private IReadOnlyList<RoadInfo> m_RoadsSource;
        private string m_RoadsRecent;
        /// <summary>工具没开时的状态（只带语言，语言变了才重建）。</summary>
        private string m_InactiveState, m_InactiveLocale;

        /// <summary>由 Mod 在加载时注入。</summary>
        public Setting setting { get; set; }

        protected override void OnCreate()
        {
            base.OnCreate();
            m_ToolSystem = World.GetOrCreateSystemManaged<InterchangeToolSystem>();
            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();
            AddBinding(m_State = new ValueBinding<string>(kGroup, "state", "{\"active\":false}"));
            AddBinding(m_Roads = new ValueBinding<string>(kGroup, "roads", "[]"));
            AddBinding(new TriggerBinding<string, string>(kGroup, "set", OnSet));
            AddBinding(new TriggerBinding(kGroup, "close", OnClose));
            AddBinding(new TriggerBinding(kGroup, "toggle", OnToggle));
            AddBinding(new TriggerBinding(kGroup, "confirm", () => m_ToolSystem.RequestConfirm()));
            AddBinding(new TriggerBinding(kGroup, "unpin", () => m_ToolSystem.Unpin()));
            AddBinding(new TriggerBinding(kGroup, "pause", () => m_SimulationSystem.selectedSpeed = 0f));
            AddBinding(new TriggerBinding<bool>(kGroup, "browse", on => m_ToolSystem.Browse(on)));
        }

        protected override void OnUpdate()
        {
            base.OnUpdate();
            if (setting == null)
                return;
            m_State.Update(m_ToolSystem.isActive ? BuildState() : InactiveState());

            // 道路目录建好、或最近使用变化时才重新发送道路表
            if (m_ToolSystem.roadCatalog != null && (m_ToolSystem.roadCatalog != m_RoadsSource || setting.RecentRoads != m_RoadsRecent))
            {
                m_RoadsSource = m_ToolSystem.roadCatalog;
                m_RoadsRecent = setting.RecentRoads;
                m_Roads.Update(BuildRoads());
            }
        }

        private void OnSet(string key, string value)
        {
            if (setting == null || !setting.SetValue(key, value))
            {
                Mod.log.Warn($"Unknown panel setting '{key}'");
                return;
            }
            setting.ApplyAndSave();
            m_ToolSystem.NotifySettingsChanged();
        }

        private void OnClose()
        {
            if (m_ToolSystem.isActive)
                m_ToolSystem.Toggle();
        }

        /// <summary>左上角快捷按钮：只在游戏中（非编辑器）打开 / 关闭工具。</summary>
        private void OnToggle()
        {
            if (GameManager.instance.gameMode.IsGame())
                m_ToolSystem.Toggle();
        }

        /// <summary>工具没开时的状态：也带上语言，左上角快捷按钮的提示要用。</summary>
        private string InactiveState()
        {
            string locale = GameManager.instance.localizationManager.activeLocaleId;
            if (m_InactiveState == null || locale != m_InactiveLocale)
            {
                m_InactiveLocale = locale;
                m_InactiveState = new JsonText().BeginObject().Field("active", false).Field("locale", locale).EndObject().ToString();
            }
            return m_InactiveState;
        }

        private string BuildState()
        {
            var s = setting;
            var json = new JsonText().BeginObject()
                .Field("active", true)
                .Field("locale", GameManager.instance.localizationManager.activeLocaleId)
                .Field("selectedRoad", m_ToolSystem.selectedRoadName)
                .Field("anarchy", m_ToolSystem.anarchyAvailable)
                .Field("pinned", m_ToolSystem.pinned)
                .Field("browsing", m_ToolSystem.browsing)
                .Field("paused", m_SimulationSystem.selectedSpeed == 0f)
                .Field("heading", m_ToolSystem.headingDegrees)
                .Field("headingFree", m_ToolSystem.headingFree)
                .BeginObject("settings")
                .Field(nameof(s.BaseElevation), s.BaseElevation)
                .Field(nameof(s.LevelGap), s.LevelGap)
                .Field(nameof(s.MainOnTop), s.MainOnTop)
                .Field(nameof(s.CrossingAngle), s.CrossingAngle)
                .Field(nameof(s.RampLength), s.RampLength)
                .Field(nameof(s.MainRoadForm), s.MainRoadForm.ToString())
                .Field(nameof(s.CrossRoadForm), s.CrossRoadForm.ToString())
                .Field(nameof(s.MainRoadPrefab), s.MainRoadPrefab)
                .Field(nameof(s.CrossRoadPrefab), s.CrossRoadPrefab)
                .Field(nameof(s.MedianWidth), s.MedianWidth)
                .Field(nameof(s.RampDivergeAngle), s.RampDivergeAngle)
                .Field(nameof(s.RampLanes), s.RampLanes)
                .Field(nameof(s.RampPrefab), s.RampPrefab)
                .Field(nameof(s.MatchJunctions), s.MatchJunctions)
                .Field(nameof(s.ManualMode), s.ManualMode)
                .Field(nameof(s.GuideSeen), s.GuideSeen)
                .Field(nameof(s.CurveFollow), s.CurveFollow)
                .Field(nameof(s.InterchangeType), s.InterchangeType ?? "Diamond")
                .Field(nameof(s.FrontageRoadPrefab), s.FrontageRoadPrefab ?? "")
                .Field(nameof(s.LaneBalance), s.LaneBalance)
                .Field(nameof(s.RampGap), s.RampGap)
                .Field(nameof(s.SpacingInCells), s.SpacingInCells)
                .Field(nameof(s.BaseFollowsMatch), s.BaseFollowsMatch)
                .Field(nameof(s.BranchOnLeft), s.BranchOnLeft)
                .Field(nameof(s.TrumpetLoopForExit), s.TrumpetLoopForExit)
                .Field(nameof(s.LoopRadius), s.LoopRadius)
                .Field(nameof(s.CloverleafCrossOnTop), s.CloverleafCrossOnTop)
                .Field(nameof(s.CloverleafHugLoop), s.CloverleafHugLoop)
                .Field(nameof(s.ParcloLoopsForExit), s.ParcloLoopsForExit)
                .Field(nameof(s.RoundaboutRacetrack), s.RoundaboutRacetrack)
                .Field(nameof(s.StackLeftExit), s.StackLeftExit)
                .Field(nameof(s.DirectionalTwoLevel), s.DirectionalTwoLevel)
                .Field(nameof(s.TeeMainRoad), s.TeeMainRoad ?? "auto")
                .Field(nameof(s.CloverleafCollectorMode), s.CloverleafCollectorMode ?? "none")
                .Field(nameof(s.MaxGradePercent), s.MaxGradePercent)
                .Field(nameof(s.TrumpetNearRampRadius), s.TrumpetNearRampRadius)
                .EndObject();

            PanelSnapshot snap = m_ToolSystem.snapshot;
            if (snap != null)
            {
                json.BeginObject("match")
                    .Field("state", snap.MatchState).Field("error", snap.MatchError).Field("legs", snap.MatchLegs)
                    .Field("angle", snap.MatchAngle).Field("main", snap.MatchMain).Field("cross", snap.MatchCross)
                    .Field("stacked", snap.MatchStacked).Field("crossing", snap.MatchCrossing).Field("dropsLeg", snap.MatchDropsLeg).Field("removed", snap.RemovedRoads).Field("connected", snap.ConnectedEnds).Field("baseFollows", snap.BaseFollows).Field("baseElevation", snap.BaseElevation).Field("mainTransition", snap.MainTransition)
                    .BeginArray("mainRadii").Item(snap.MainRadiusAhead).Item(snap.MainRadiusBehind).EndArray()
                    .BeginArray("crossRadii").Item(snap.CrossRadiusAhead).Item(snap.CrossRadiusBehind).EndArray()
                    .Field("curveGrade", snap.CurveGrade)
                    .BeginArray("legRadii");
                foreach (float radius in snap.LegRadii)
                    json.Item(radius);
                json.EndArray().EndObject();
                json.BeginObject("resolved")
                    .Field("mainElevation", snap.MainElevation).Field("crossElevation", snap.CrossElevation)
                    .Field("mainBand", LevelPlanner.BandOf(snap.MainElevation).ToString())
                    .Field("crossBand", LevelPlanner.BandOf(snap.CrossElevation).ToString())
                    .Field("levelGap", snap.LevelGap).Field("minimumGap", snap.MinimumGap)
                    .Field("mainRoad", snap.MainRoad).Field("mainForm", snap.MainForm).Field("mainLanes", snap.MainLanes)
                    .Field("crossRoad", snap.CrossRoad).Field("crossForm", snap.CrossForm).Field("crossLanes", snap.CrossLanes)
                    .Field("rampRoad", snap.RampRoad).Field("rampLanes", snap.RampLanes).Field("rampTarget", snap.RampTarget)
                    .Field("rampAuto", snap.RampAuto).Field("rampLength", snap.RampLength).Field("loopRadius", snap.LoopRadius)
                    .Field("frontageRoad", snap.FrontageRoad).Field("frontageLanes", snap.FrontageLanes).Field("extraRoad", snap.ExtraRoad)
                    .Field("mainReducedRoad", snap.MainReducedRoad).Field("mainReducedLanes", snap.MainReducedLanes)
                    .Field("frontageWidenedRoad", snap.FrontageWidenedRoad).Field("frontageWidenedLanes", snap.FrontageWidenedLanes)
                    .BeginArray("errors");
                foreach (string error in snap.Errors)
                    json.Item(error);
                json.EndArray().BeginArray("gameErrors");
                foreach (var error in snap.GameErrors)
                    json.BeginObject().Field("type", error.Type).Field("road", error.Road).Field("elevation", error.Elevation)
                        .Field("blocking", error.Blocking).EndObject();
                json.EndArray().EndObject();
            }
            return json.EndObject().ToString();
        }

        /// <summary>道路表：n 名称，h 公路，f/b 正/反向车道，p 公交道，w 宽度，o 可自动选用的普通道路，r 最近使用排名（-1 = 没用过）。</summary>
        private string BuildRoads()
        {
            var recent = RecentList.Deserialize(setting.RecentRoads).Items;
            var json = new JsonText().BeginArray();
            foreach (RoadInfo road in m_ToolSystem.roadCatalog)
            {
                int rank = -1;
                for (int i = 0; i < recent.Count; i++)
                {
                    if (recent[i] == road.Name)
                    {
                        rank = i;
                        break;
                    }
                }
                json.BeginObject()
                    .Field("n", road.Name).Field("h", road.IsHighway)
                    .Field("f", road.ForwardLanes).Field("b", road.BackwardLanes).Field("p", road.PublicTransportLanes)
                    .Field("w", road.Width).Field("o", RampMatcher.IsOrdinaryRoad(road)).Field("r", rank)
                    .Field("i", road.Icon ?? "")
                    .EndObject();
            }
            return json.EndArray().ToString();
        }
    }
}
