import { ModRegistrar } from "cs2/modding";
import { HideWhileToolActive, InterchangePanel } from "mods/InterchangePanel";
import { QuickButton } from "mods/QuickButton";

// 把立交工具面板挂到游戏界面上；面板自己在工具未激活时返回 null
const register: ModRegistrar = (moduleRegistry) => {
  moduleRegistry.append("Game", InterchangePanel);
  // 左上角的快捷按钮：打开 / 关闭立交工具
  moduleRegistry.append("GameTopLeft", QuickButton);
  // 工具开着时隐藏底部的道路服务（资产）栏，横向面板占它的位置
  moduleRegistry.extend("game-ui/game/components/asset-menu/asset-menu.tsx", "AssetMenu", HideWhileToolActive);
  moduleRegistry.extend("game-ui/game/components/asset-menu/console-asset-menu.tsx", "ConsoleAssetMenu", HideWhileToolActive);
};

export default register;
