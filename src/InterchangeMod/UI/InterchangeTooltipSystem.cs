using Game.Input;
using Game.SceneFlow;
using Game.UI.Localization;
using Game.UI.Tooltip;
using InterchangeMod.Systems;

namespace InterchangeMod.UI
{
    /// <summary>
    /// 鼠标旁的操作提示（和原版道路工具的费用提示一样跟着鼠标）：没固定时“左键固定预览 · 右键退出”，
    /// 固定后“已固定：在面板确认放置 · 右键取消固定”；朝向由旋转决定（没对准已有道路）时加上滚轮旋转。鼠标在面板上时不显示。
    /// </summary>
    public partial class InterchangeTooltipSystem : TooltipSystemBase
    {
        private InterchangeToolSystem m_ToolSystem;
        private StringTooltip m_Tooltip;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_ToolSystem = World.GetOrCreateSystemManaged<InterchangeToolSystem>();
            m_Tooltip = new StringTooltip { path = "interchangeModHint" };
        }

        protected override void OnUpdate()
        {
            if (!m_ToolSystem.isActive || InputManager.instance.mouseOverUI)
                return;
            string locale = GameManager.instance.localizationManager.activeLocaleId ?? "";
            bool zh = locale.StartsWith("zh");
            string text = m_ToolSystem.pinned
                ? zh ? "已固定：在面板确认放置 · 右键取消固定" : "Pinned: confirm in the panel · Right click to unpin"
                : zh ? "左键固定预览 · 右键退出" : "Left click to pin · Right click to exit";
            if (m_ToolSystem.headingFree)
                text += zh ? $" · 滚轮旋转（Shift 精确）{m_ToolSystem.headingDegrees:F0}°" : $" · Wheel rotates (Shift: fine) {m_ToolSystem.headingDegrees:F0}°";
            m_Tooltip.value = LocalizedString.Value(text);
            AddMouseTooltip(m_Tooltip);
        }
    }
}
