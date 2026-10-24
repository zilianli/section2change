using System.Collections.Generic;
using Colossal.UI.Binding;
using Game.SceneFlow;
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
    ///   close —— 关闭工具。
    /// ValueBinding 只在字符串变化时才发给 UI，所以每帧重建 JSON 的开销只在 C# 这边。
    /// </summary>
    public partial class InterchangeUISystem : UISystemBase
    {
        public const string kGroup = "interchangeMod";

        private InterchangeToolSystem m_ToolSystem;
        private ValueBinding<string> m_State;
        private ValueBinding<string> m_Roads;
        private IReadOnlyList<RoadInfo> m_RoadsSource;
        private string m_RoadsRecent;

        /// <summary>由 Mod 在加载时注入。</summary>
        public Setting setting { get; set; }

        protected override void OnCreate()
        {
            base.OnCreate();
            m_ToolSystem = World.GetOrCreateSystemManaged<InterchangeToolSystem>();
            AddBinding(m_State = new ValueBinding<string>(kGroup, "state", "{\"active\":false}"));
            AddBinding(m_Roads = new ValueBinding<string>(kGroup, "roads", "[]"));
            AddBinding(new TriggerBinding<string, string>(kGroup, "set", OnSet));
            AddBinding(new TriggerBinding(kGroup, "close", OnClose));
        }

        protected override void OnUpdate()
        {
            base.OnUpdate();
            if (setting == null)
                return;
            m_State.Update(m_ToolSystem.isActive ? BuildState() : "{\"active\":false}");

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

        private string BuildState()
        {
            var s = setting;
            var json = new JsonText().BeginObject()
                .Field("active", true)
                .Field("locale", GameManager.instance.localizationManager.activeLocaleId)
                .Field("selectedRoad", m_ToolSystem.selectedRoadName)
                .Field("anarchy", m_ToolSystem.anarchyAvailable)
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
                    .EndObject();
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
