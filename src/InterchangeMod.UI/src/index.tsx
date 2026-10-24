import { ModRegistrar } from "cs2/modding";
import { InterchangePanel } from "mods/InterchangePanel";

// 把立交工具面板挂到游戏界面上；面板自己在工具未激活时返回 null
const register: ModRegistrar = (moduleRegistry) => {
  moduleRegistry.append("Game", InterchangePanel);
};

export default register;
