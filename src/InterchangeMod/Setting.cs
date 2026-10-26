using Colossal;
using Colossal.IO.AssetDatabase;
using Game.Input;
using Game.Modding;
using Game.Settings;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace InterchangeMod
{
    /// <summary>道路形式选项。Auto：所选道路是单行道路时用双幅，否则双向。</summary>
    public enum RoadFormOption
    {
        Auto,
        TwoWay,
        Dual,
    }

    /// <summary>
    /// 本 mod 的设置。立交参数在工具面板里修改（选项菜单中隐藏），但仍保存在这里，重启游戏后保留。
    /// 选项菜单只显示快捷键和调试开关。
    /// </summary>
    [FileLocation(nameof(InterchangeMod))]
    [SettingsUIGroupOrder(kKeybindingGroup, kDebugGroup)]
    [SettingsUIShowGroupName(kKeybindingGroup, kDebugGroup)]
    [SettingsUIKeyboardAction(Mod.kToggleToolActionName, ActionType.Button, usages: new string[] { Usages.kDefaultUsage, Usages.kToolUsage })]
    [SettingsUIKeyboardAction(Mod.kRotateLeftActionName, ActionType.Button, usages: new string[] { Usages.kToolUsage })]
    [SettingsUIKeyboardAction(Mod.kRotateRightActionName, ActionType.Button, usages: new string[] { Usages.kToolUsage })]
    public class Setting : ModSetting
    {
        public const string kSection = "Main";

        public const string kKeybindingGroup = "KeyBinding";
        public const string kDebugGroup = "Debug";

        public Setting(IMod mod) : base(mod)
        {
            SetDefaults();
        }

        // —— 立交参数（工具面板修改）——

        /// <summary>立交类型：Diamond 菱形 / ExpresswayNode 中式快速路节点 / ExpresswayT 快速路 T 型 / SinglePoint 单点立交 / Trumpet 喇叭形。</summary>
        [SettingsUIHidden] public string InterchangeType { get; set; }

        /// <summary>中式快速路节点的地面辅路（单行道路）预制体名。空 = Medium Road Oneway。</summary>
        [SettingsUIHidden] public string FrontageRoadPrefab { get; set; }

        /// <summary>匝道分出/汇入后主线减少车道、辅路增加车道（自动匹配道路）。</summary>
        [SettingsUIHidden] public bool LaneBalance { get; set; }

        /// <summary>中式快速路节点里匝道与主线、辅路之间的间距（米）；越小占地越窄。</summary>
        [SettingsUIHidden] public float RampGap { get; set; }

        /// <summary>面板里的间距（分隔带、匝道间距）按游戏网格的格数显示（1 格 = 8 米），存储仍是米。</summary>
        [SettingsUIHidden] public bool SpacingInCells { get; set; }

        /// <summary>对准已有路口/道路时基层跟随已有道路的实际高度（默认）；false 时按面板的基层。</summary>
        [SettingsUIHidden] public bool BaseFollowsMatch { get; set; }

        /// <summary>T 型立交空地放置时支路在主线方向的左侧（false = 右侧）。对准已有 T 型路口时由路口决定。</summary>
        [SettingsUIHidden] public bool BranchOnLeft { get; set; }

        /// <summary>喇叭形的环道用于驶出主线（true，默认）或驶入主线。</summary>
        [SettingsUIHidden] public bool TrumpetLoopForExit { get; set; }

        /// <summary>环道半径（米）；0 = 自动（能放下的最小半径）。</summary>
        [SettingsUIHidden] public float LoopRadius { get; set; }

        /// <summary>喇叭形近侧直连匝道半径（米）；0 = 自动（满足坡长约束的最小值）。</summary>
        [SettingsUIHidden] public float TrumpetNearRampRadius { get; set; }

        /// <summary>坡道最大坡度（%）。实际还不超过所用道路自身允许的坡度；越陡占地越小。</summary>
        [SettingsUIHidden] public float MaxGradePercent { get; set; }

        /// <summary>基层（下层）的离地高度。负数为下沉/隧道。改它会让整座立交一起升降。</summary>
        [SettingsUIHidden] public float BaseElevation { get; set; }

        /// <summary>上层比基层高多少。0 = 自动：取两种道路之间需要的最小净空（不低于 7.5 米）。</summary>
        [SettingsUIHidden] public float LevelGap { get; set; }

        /// <summary>true = 主线在上层、被交道路在基层；false 相反。</summary>
        [SettingsUIHidden] public bool MainOnTop { get; set; }

        /// <summary>苜蓿叶：被交道路在上层（默认主线在上层）。</summary>
        [SettingsUIHidden] public bool CloverleafCrossOnTop { get; set; }

        /// <summary>苜蓿叶：右转匝道贴着环道外侧走，相邻那段与环道合成一条双向匝道（默认 false = 外侧大弧线）。</summary>
        [SettingsUIHidden] public bool CloverleafHugLoop { get; set; }

        /// <summary>部分苜蓿叶：环道用于驶出主线（B2，默认），否则用于驶入主线（A2）。</summary>
        [SettingsUIHidden] public bool ParcloLoopsForExit { get; set; }

        /// <summary>环岛式：环岛沿主线拉长成跑道形（默认 false = 圆形）。</summary>
        [SettingsUIHidden] public bool RoundaboutRacetrack { get; set; }

        /// <summary>定向式：左转匝道左出左进（默认 false = 右出右进）。</summary>
        [SettingsUIHidden] public bool StackLeftExit { get; set; }

        /// <summary>定向 / 半定向 T 型：两层（一条左转匝道下穿主线），默认 false = 三层。</summary>
        [SettingsUIHidden] public bool DirectionalTwoLevel { get; set; }

        /// <summary>三向立交对准立体交叉（十字当 T 型）时哪条是主线：auto = 公路优先、否则上层（默认）；upper；lower。</summary>
        [SettingsUIHidden] public string TeeMainRoad { get; set; }

        /// <summary>
        /// 苜蓿叶集散车道：none = 直接在主线上交织（默认）；auto = 按道路类型，是公路的路加集散车道（两条都是公路时两条都加）；
        /// main = 仅主线；both = 两条路都加。
        /// </summary>
        [SettingsUIHidden] public string CloverleafCollectorMode { get; set; }

        /// <summary>主线与被交道路的夹角，90 为正交。</summary>
        [SettingsUIHidden] public float CrossingAngle { get; set; }

        /// <summary>匝道最短长度；坡度需要时工具会自动加长。</summary>
        [SettingsUIHidden] public float RampLength { get; set; }

        [SettingsUIHidden] public RoadFormOption MainRoadForm { get; set; }

        [SettingsUIHidden] public RoadFormOption CrossRoadForm { get; set; }

        /// <summary>主线道路预制体名。空 = 道路菜单里最近选的道路。</summary>
        [SettingsUIHidden] public string MainRoadPrefab { get; set; }

        /// <summary>被交道路预制体名。空 = Medium Road。</summary>
        [SettingsUIHidden] public string CrossRoadPrefab { get; set; }

        [SettingsUIHidden] public float MedianWidth { get; set; }

        /// <summary>匝道在分流/合流节点处与主线的夹角。分流段长度由它和道路宽度算出。</summary>
        [SettingsUIHidden] public float RampDivergeAngle { get; set; }

        /// <summary>匝道车道数。0 = 自动：主线每方向车道数的一半（至少 1）。</summary>
        [SettingsUIHidden] public int RampLanes { get; set; }

        /// <summary>匝道道路预制体名。空 = 按车道数自动匹配。</summary>
        [SettingsUIHidden] public string RampPrefab { get; set; }

        /// <summary>鼠标指着已有道路时，对准它所在的路口（方向、道路、分隔带都跟随路口）。</summary>
        [SettingsUIHidden] public bool MatchJunctions { get; set; }

        /// <summary>弯道适配：对准弯道时立交整体顺着道路弯曲（关掉时按直线放置）。</summary>
        [SettingsUIHidden] public bool CurveFollow { get; set; }

        /// <summary>最近使用的道路（RecentList 序列化）。</summary>
        [SettingsUIHidden] public string RecentRoads { get; set; }

        // —— 选项菜单 ——

        [SettingsUIKeyboardBinding(BindingKeyboard.I, Mod.kToggleToolActionName, shift: true)]
        [SettingsUISection(kSection, kKeybindingGroup)]
        public ProxyBinding ToggleToolBinding { get; set; }

        [SettingsUIKeyboardBinding(BindingKeyboard.Comma, Mod.kRotateLeftActionName)]
        [SettingsUISection(kSection, kKeybindingGroup)]
        public ProxyBinding RotateLeftBinding { get; set; }

        [SettingsUIKeyboardBinding(BindingKeyboard.Period, Mod.kRotateRightActionName)]
        [SettingsUISection(kSection, kKeybindingGroup)]
        public ProxyBinding RotateRightBinding { get; set; }

        [SettingsUISection(kSection, kKeybindingGroup)]
        public bool ResetBindings
        {
            set
            {
                Mod.log.Info("Reset key bindings");
                ResetKeyBindings();
            }
        }

        /// <summary>写入性能测量、道路车道表等调试信息。</summary>
        [SettingsUISection(kSection, kDebugGroup)]
        public bool DebugLogging { get; set; }

        public override void SetDefaults()
        {
            InterchangeType = "Diamond";
            FrontageRoadPrefab = "";
            LaneBalance = true;
            RampGap = 1f;
            SpacingInCells = false;
            BaseFollowsMatch = true;
            BranchOnLeft = false;
            TrumpetLoopForExit = true;
            LoopRadius = 0f;
            CloverleafCollectorMode = "none";
            ParcloLoopsForExit = true;
            RoundaboutRacetrack = false;
            StackLeftExit = false;
            DirectionalTwoLevel = false;
            TeeMainRoad = "auto";
            MaxGradePercent = 8f;
            BaseElevation = 0f;
            LevelGap = 0f;
            MainOnTop = false;
            CrossingAngle = 90f;
            RampLength = 250f;
            MainRoadForm = RoadFormOption.Auto;
            CrossRoadForm = RoadFormOption.TwoWay;
            MainRoadPrefab = "";
            CrossRoadPrefab = "";
            MedianWidth = 4f;
            RampDivergeAngle = 15f;
            RampLanes = 0;
            RampPrefab = "";
            MatchJunctions = true;
            CurveFollow = true;
            RecentRoads = "";
            DebugLogging = false;
        }

        /// <summary>
        /// 工具面板改参数时调用。数值按取值范围限制。返回是否识别了这个参数。
        /// </summary>
        public bool SetValue(string key, string value)
        {
            float number = float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed) ? parsed : 0f;
            switch (key)
            {
                case nameof(BaseElevation): BaseElevation = Clamp(number, -30f, 30f); return true;
                case nameof(LevelGap): LevelGap = Clamp(number, 0f, 30f); return true;
                case nameof(MainOnTop): MainOnTop = value == "true"; return true;
                case nameof(CloverleafCrossOnTop): CloverleafCrossOnTop = value == "true"; return true;
                case nameof(CloverleafHugLoop): CloverleafHugLoop = value == "true"; return true;
                case nameof(ParcloLoopsForExit): ParcloLoopsForExit = value == "true"; return true;
                case nameof(RoundaboutRacetrack): RoundaboutRacetrack = value == "true"; return true;
                case nameof(StackLeftExit): StackLeftExit = value == "true"; return true;
                case nameof(DirectionalTwoLevel): DirectionalTwoLevel = value == "true"; return true;
                case nameof(TeeMainRoad): TeeMainRoad = value == "upper" || value == "lower" || value == "swap" ? value : "auto"; return true;
                case nameof(CloverleafCollectorMode):
                    CloverleafCollectorMode = value == "auto" || value == "main" || value == "both" ? value : "none";
                    return true;
                case nameof(MatchJunctions): MatchJunctions = value == "true"; return true;
                case nameof(CurveFollow): CurveFollow = value == "true"; return true;
                case nameof(CrossingAngle): CrossingAngle = Clamp(number, 45f, 135f); return true;
                case nameof(RampLength): RampLength = Clamp(number, 60f, 500f); return true;
                case nameof(MedianWidth): MedianWidth = Clamp(number, 0f, 96f); return true;
                case nameof(RampDivergeAngle): RampDivergeAngle = Clamp(number, 5f, 45f); return true;
                case nameof(RampLanes): RampLanes = (int)Clamp(number, 0f, 5f); return true;
                case nameof(MainRoadForm): MainRoadForm = ParseForm(value); return true;
                case nameof(CrossRoadForm): CrossRoadForm = ParseForm(value); return true;
                case nameof(MainRoadPrefab): MainRoadPrefab = value ?? ""; return true;
                case nameof(CrossRoadPrefab): CrossRoadPrefab = value ?? ""; return true;
                case nameof(RampPrefab): RampPrefab = value ?? ""; return true;
                case nameof(InterchangeType):
                    InterchangeType = value == "ExpresswayNode" || value == "ExpresswayT" || value == "SinglePoint" || value == "Trumpet" || value == "Cloverleaf" || value == "PartialCloverleaf" || value == "Roundabout" || value == "DirectionalT" || value == "SemiDirectionalT" || value == "Stack" || value == "Turbine" ? value : "Diamond";
                    return true;
                case nameof(FrontageRoadPrefab): FrontageRoadPrefab = value ?? ""; return true;
                case nameof(LaneBalance): LaneBalance = value == "true"; return true;
                case nameof(RampGap): RampGap = Clamp(number, 0.5f, 16f); return true;
                case nameof(SpacingInCells): SpacingInCells = value == "true"; return true;
                case nameof(BaseFollowsMatch): BaseFollowsMatch = value == "true"; return true;
                case nameof(BranchOnLeft): BranchOnLeft = value == "true"; return true;
                case nameof(TrumpetLoopForExit): TrumpetLoopForExit = value == "true"; return true;
                case nameof(LoopRadius): LoopRadius = number <= 0f ? 0f : Clamp(number, 30f, 250f); return true;
                case nameof(TrumpetNearRampRadius): TrumpetNearRampRadius = number <= 0f ? 0f : Clamp(number, 30f, 1000f); return true;
                case nameof(MaxGradePercent): MaxGradePercent = Clamp(number, 3f, 25f); return true;
                default: return false;
            }
        }

        private static float Clamp(float v, float min, float max) => Math.Max(min, Math.Min(max, v));

        private static RoadFormOption ParseForm(string value) =>
            Enum.TryParse(value, out RoadFormOption form) ? form : RoadFormOption.Auto;
    }

    public class LocaleEN : IDictionarySource
    {
        private readonly Setting m_Setting;
        public LocaleEN(Setting setting)
        {
            m_Setting = setting;
        }
        public IEnumerable<KeyValuePair<string, string>> ReadEntries(IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts)
        {
            return new Dictionary<string, string>
            {
                { m_Setting.GetSettingsLocaleID(), "Interchange Generator" },
                { m_Setting.GetOptionTabLocaleID(Setting.kSection), "Main" },

                { m_Setting.GetOptionGroupLocaleID(Setting.kKeybindingGroup), "Key bindings" },
                { m_Setting.GetOptionGroupLocaleID(Setting.kDebugGroup), "Debug" },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.ToggleToolBinding)), "Toggle interchange tool" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.ToggleToolBinding)), "Open or close the interchange tool. All interchange settings are in the tool's panel." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.RotateLeftBinding)), "Rotate left" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.RotateLeftBinding)), "Rotate the interchange preview 15° counterclockwise (the mouse wheel also rotates: 15° per notch, 1° with Shift)." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.RotateRightBinding)), "Rotate right" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.RotateRightBinding)), "Rotate the interchange preview 15° clockwise (the mouse wheel also rotates: 15° per notch, 1° with Shift)." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.ResetBindings)), "Reset key bindings" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.ResetBindings)), "Reset all key bindings of the mod" },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.DebugLogging)), "Debug logging" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.DebugLogging)), "Write timing measurements and the road lane table to the mod log." },

                { m_Setting.GetBindingKeyLocaleID(Mod.kToggleToolActionName), "Toggle interchange tool" },
                { m_Setting.GetBindingKeyLocaleID(Mod.kRotateLeftActionName), "Rotate left" },
                { m_Setting.GetBindingKeyLocaleID(Mod.kRotateRightActionName), "Rotate right" },
                { m_Setting.GetBindingMapLocaleID(), "Interchange Generator" },
            };
        }

        public void Unload()
        {

        }
    }

    public class LocaleZH : IDictionarySource
    {
        private readonly Setting m_Setting;
        public LocaleZH(Setting setting)
        {
            m_Setting = setting;
        }
        public IEnumerable<KeyValuePair<string, string>> ReadEntries(IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts)
        {
            return new Dictionary<string, string>
            {
                { m_Setting.GetSettingsLocaleID(), "立交桥生成器" },
                { m_Setting.GetOptionTabLocaleID(Setting.kSection), "主要" },

                { m_Setting.GetOptionGroupLocaleID(Setting.kKeybindingGroup), "快捷键" },
                { m_Setting.GetOptionGroupLocaleID(Setting.kDebugGroup), "调试" },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.ToggleToolBinding)), "开关立交工具" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.ToggleToolBinding)), "打开或关闭立交工具。所有立交参数都在工具面板里设置。" },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.RotateLeftBinding)), "向左旋转" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.RotateLeftBinding)), "立交预览逆时针旋转 15°（也可以用滚轮：每格 15°，Shift + 滚轮每格 1°）。" },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.RotateRightBinding)), "向右旋转" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.RotateRightBinding)), "立交预览顺时针旋转 15°（也可以用滚轮：每格 15°，Shift + 滚轮每格 1°）。" },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.ResetBindings)), "重置快捷键" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.ResetBindings)), "把本 mod 的所有快捷键恢复默认" },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.DebugLogging)), "调试日志" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.DebugLogging)), "在 mod 日志中写入性能测量和道路车道表。" },

                { m_Setting.GetBindingKeyLocaleID(Mod.kToggleToolActionName), "开关立交工具" },
                { m_Setting.GetBindingKeyLocaleID(Mod.kRotateLeftActionName), "向左旋转" },
                { m_Setting.GetBindingKeyLocaleID(Mod.kRotateRightActionName), "向右旋转" },
                { m_Setting.GetBindingMapLocaleID(), "立交桥生成器" },
            };
        }

        public void Unload()
        {

        }
    }
}
