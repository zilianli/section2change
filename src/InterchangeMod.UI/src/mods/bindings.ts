import { bindValue, trigger } from "cs2/api";

// 与 C# InterchangeUISystem 对应：两个 JSON 字符串绑定 + set/close 两个触发器
const GROUP = "interchangeMod";

export type RoadForm = "Auto" | "TwoWay" | "Dual";

export type InterchangeType = "Diamond" | "ExpresswayNode" | "ExpresswayT" | "SinglePoint" | "Trumpet" | "Cloverleaf" | "PartialCloverleaf" | "Roundabout" | "DirectionalT" | "SemiDirectionalT";

export interface PanelSettings {
  /** 基层（下层）离地高度。 */
  BaseElevation: number;
  /** 层间高差，0 = 自动。 */
  LevelGap: number;
  /** true = 主线在上层。 */
  MainOnTop: boolean;
  CrossingAngle: number;
  RampLength: number;
  MainRoadForm: RoadForm;
  CrossRoadForm: RoadForm;
  MainRoadPrefab: string;
  CrossRoadPrefab: string;
  MedianWidth: number;
  RampDivergeAngle: number;
  RampLanes: number;
  RampPrefab: string;
  /** 鼠标指着已有道路时对准它所在的路口。 */
  MatchJunctions: boolean;
  /** 立交类型。 */
  InterchangeType: InterchangeType;
  /** 中式快速路节点的辅路预制体名，"" = 默认。 */
  FrontageRoadPrefab: string;
  /** 车道平衡：匝道分出后主线减车道、辅路接纳匝道后加车道。 */
  LaneBalance: boolean;
  /** 中式快速路节点里匝道与主线、辅路的间距（米）。 */
  RampGap: number;
  /** 间距按游戏网格的格数显示（1 格 = 8 米）。 */
  SpacingInCells: boolean;
  /** 对准已有道路时基层跟随已有道路的高度。 */
  BaseFollowsMatch: boolean;
  /** T 型空地放置时支路在左侧。 */
  BranchOnLeft: boolean;
  TrumpetLoopForExit: boolean;
  LoopRadius: number;
  CloverleafCrossOnTop: boolean;
  CloverleafHugLoop: boolean;
  ParcloLoopsForExit: boolean;
  RoundaboutRacetrack: boolean;
  DirectionalTwoLevel: boolean;
  TeeMainRoad: "auto" | "upper" | "lower" | "swap";
  CloverleafCollectorMode: "none" | "auto" | "main" | "both";
  MaxGradePercent: number;
  TrumpetNearRampRadius: number;
}

/** 路口匹配结果。main/cross 为“预制体|形式|分隔带”。 */
export interface MatchInfo {
  state: "none" | "matched" | "road" | "error";
  /** 基层跟随已有道路（不是面板设定）。 */
  baseFollows: boolean;
  /** 实际用的基层高度。 */
  baseElevation: number;
  error: string;
  legs: number;
  angle: number;
  main: string;
  cross: string;
  /** true = 在原有道路上方/下方叠建；false = 原路口改造。 */
  stacked: boolean;
  /** 立体交叉（两条道路没有共用节点，按俯视交点匹配）。 */
  crossing: boolean;
  dropsLeg: boolean;
  /** 原路口改造时要拆除的原有路段数。 */
  removed: number;
  /** 接回立交的臂端数。 */
  connected: number;
  /** 主线外部双向道路名：立交内部改用双幅并在臂端过渡时非空。 */
  mainTransition: string;
}

/** 离地高度分段（与 C# ElevationBand 一致）。 */
export type ElevationBand = "Elevated" | "Raised" | "Ground" | "Cutting" | "Tunnel";

/** 最近一次预览实际用到的道路和结果。 */
export interface Resolved {
  /** 中式快速路节点实际用的辅路；菱形时为空。 */
  frontageRoad: string;
  frontageLanes: number;
  /** 部分苜蓿叶的匝道主干、环岛式的环岛实际用的道路；其他类型为空。 */
  extraRoad: string;
  /** 车道平衡实际用的道路；为空表示没有变化。 */
  mainReducedRoad: string;
  mainReducedLanes: number;
  frontageWidenedRoad: string;
  frontageWidenedLanes: number;
  mainElevation: number;
  crossElevation: number;
  mainBand: ElevationBand;
  crossBand: ElevationBand;
  /** 实际层间高差。 */
  levelGap: number;
  /** 这两种道路之间需要的最小高差。 */
  minimumGap: number;
  mainRoad: string;
  mainForm: "TwoWay" | "Dual";
  mainLanes: number;
  crossRoad: string;
  crossForm: "TwoWay" | "Dual";
  crossLanes: number;
  rampRoad: string;
  rampLanes: number;
  rampTarget: number;
  rampAuto: boolean;
  rampLength: number;
  loopRadius: number;
  /** 生成前检查出的问题（参数、坡度、预制体）。 */
  errors: string[];
  /** 游戏对预览报的问题：type 是游戏 ErrorType 名，elevation 是该处离地高度。 */
  gameErrors: { type: string; road: string; elevation: number; blocking: boolean }[];
}

export interface PanelState {
  active: boolean;
  locale?: string;
  selectedRoad?: string;
  /** 装了 Anarchy 模组（无视碰撞）并已注册本工具。 */
  anarchy?: boolean;
  settings?: PanelSettings;
  match?: MatchInfo;
  resolved?: Resolved;
}

/** 道路表的一行：n 名称，h 公路，f/b 正/反向车道，p 公交道，w 宽度，o 普通道路，r 最近使用排名（-1 = 没用过）。 */
export interface RoadRow {
  n: string;
  h: boolean;
  f: number;
  b: number;
  p: number;
  w: number;
  o: boolean;
  r: number;
  /** 图标地址，"" 表示没有。 */
  i: string;
}

export const state$ = bindValue<string>(GROUP, "state", '{"active":false}');
export const roads$ = bindValue<string>(GROUP, "roads", "[]");

// Anarchy 模组（无视碰撞）自己的绑定：组 "Anarchy"，值 AnarchyEnabled，触发 AnarchyToggled。
// 没装 Anarchy 时这个值一直是默认的 false，面板也不显示开关。
export const anarchyEnabled$ = bindValue<boolean>("Anarchy", "AnarchyEnabled", false);
export const toggleAnarchy = () => trigger("Anarchy", "AnarchyToggled");

export function setValue(key: keyof PanelSettings, value: string | number | boolean) {
  trigger(GROUP, "set", key, String(value));
}

export function closeTool() {
  trigger(GROUP, "close");
}

export function parseJson<T>(text: string, fallback: T): T {
  try {
    return JSON.parse(text) as T;
  } catch {
    return fallback;
  }
}

export const isOneWay = (road: RoadRow) => (road.f === 0) !== (road.b === 0);
export const lanesPerDirection = (road: RoadRow) =>
  isOneWay(road) ? Math.max(road.f, road.b) : Math.min(road.f, road.b);
