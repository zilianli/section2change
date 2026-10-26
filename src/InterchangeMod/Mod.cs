using Colossal.IO.AssetDatabase;
using Colossal.Logging;
using Game;
using Game.Input;
using Game.Modding;
using Game.SceneFlow;
using InterchangeMod.Systems;
using InterchangeMod.UI;
using UnityEngine.InputSystem;

namespace InterchangeMod
{
    public class Mod : IMod
    {
        public static ILog log = LogManager.GetLogger($"{nameof(InterchangeMod)}.{nameof(Mod)}").SetShowsErrorsInUI(false);

        public const string kToggleToolActionName = "ToggleInterchangeTool";
        public const string kRotateLeftActionName = "RotateInterchangeLeft";
        public const string kRotateRightActionName = "RotateInterchangeRight";

        private Setting m_Setting;
        private ProxyAction m_ToggleToolAction;
        private InterchangeToolSystem m_ToolSystem;
        private FrameTimeProbeSystem m_FrameTimeProbe;

        public void OnLoad(UpdateSystem updateSystem)
        {
            log.Info(nameof(OnLoad));

            if (GameManager.instance.modManager.TryGetExecutableAsset(this, out var asset))
                log.Info($"Current mod asset at {asset.path}");

            m_Setting = new Setting(this);
            m_Setting.RegisterInOptionsUI();
            GameManager.instance.localizationManager.AddSource("en-US", new LocaleEN(m_Setting));
            GameManager.instance.localizationManager.AddSource("zh-HANS", new LocaleZH(m_Setting));
            m_Setting.RegisterKeyBindings();
            AssetDatabase.global.LoadSettings(nameof(InterchangeMod), m_Setting, new Setting(this));

            updateSystem.UpdateAt<InterchangeToolSystem>(SystemUpdatePhase.ToolUpdate);
            m_ToolSystem = updateSystem.World.GetOrCreateSystemManaged<InterchangeToolSystem>();
            m_ToolSystem.setting = m_Setting;
            // 面板“自动”模式求解用的默认值设置（不注册、不保存）
            m_ToolSystem.autoSetting = new Setting(this);
            updateSystem.UpdateAt<InterchangeUISystem>(SystemUpdatePhase.UIUpdate);
            updateSystem.World.GetOrCreateSystemManaged<InterchangeUISystem>().setting = m_Setting;
            updateSystem.UpdateAt<InterchangeTooltipSystem>(SystemUpdatePhase.UITooltip);
            updateSystem.UpdateAt<FrameTimeProbeSystem>(SystemUpdatePhase.MainLoop);
            m_FrameTimeProbe = updateSystem.World.GetOrCreateSystemManaged<FrameTimeProbeSystem>();

            m_ToggleToolAction = m_Setting.GetAction(kToggleToolActionName);
            m_ToggleToolAction.shouldBeEnabled = true;
            m_ToggleToolAction.onInteraction += OnToggleTool;
        }

        private void OnToggleTool(ProxyAction action, InputActionPhase phase)
        {
            // 只在游戏中（非主菜单、非编辑器）响应
            if (phase != InputActionPhase.Performed || !GameManager.instance.gameMode.IsGame())
                return;

            if (!m_Setting.DebugLogging)
            {
                m_ToolSystem.Toggle();
                return;
            }

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            bool activating = !m_ToolSystem.isActive;
            m_ToolSystem.Toggle();
            stopwatch.Stop();

            string label = activating ? "activate tool" : "deactivate tool";
            log.Info($"[perf] {label}: Toggle() took {stopwatch.Elapsed.TotalMilliseconds:F1} ms");
            m_FrameTimeProbe.Watch(label);
        }

        public void OnDispose()
        {
            log.Info(nameof(OnDispose));
            if (m_ToggleToolAction != null)
                m_ToggleToolAction.onInteraction -= OnToggleTool;
            if (m_Setting != null)
            {
                m_Setting.UnregisterInOptionsUI();
                m_Setting = null;
            }
        }
    }
}
