import { useValue } from "cs2/api";
import { FloatingButton } from "cs2/ui";
import { PanelState, parseJson, state$, toggleTool } from "./bindings";
import { getStrings } from "./i18n";
import quickIcon from "./icons/interchange-quick.svg";

/**
 * 游戏界面左上角的快捷按钮（斜向视图的跨线桥、四个向外的箭头）：打开 / 关闭立交工具，与快捷键相同；工具开着时高亮。
 * FloatingButton 默认 tinted（只取图标的透明度、按主题涂成白色，选中时的浅色底上看不清）：关掉，用图标自己的黑色。
 */
export const QuickButton = () => {
  const state = parseJson<PanelState>(useValue(state$), { active: false });
  return (
    <FloatingButton src={quickIcon} tinted={false} selected={state.active} tooltipLabel={getStrings(state.locale).title} onSelect={toggleTool} />
  );
};
