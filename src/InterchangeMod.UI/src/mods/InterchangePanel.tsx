import { useState } from "react";
import { useValue } from "cs2/api";
import { Button, Panel, Scrollable } from "cs2/ui";
import { anarchyEnabled$, autoSettings, browseTypes, closeTool, confirmPlacement, ElevationBand, pauseGame, MatchInfo, PanelSettings, PanelState, parseJson, Resolved, RoadForm, roads$, RoadRow, setValue, state$, toggleAnarchy, unpinPreview } from "./bindings";
import { errorName, getStrings, Strings } from "./i18n";
import { RoadPicker } from "./RoadPicker";
import { Choice, SpacingStepper, Stepper } from "./Stepper";
import { thumbnails } from "./thumbs";
import closeIcon from "./icons/interchange-close.svg";
import styles from "./panel.module.scss";
import { nodeMode, nodeArm, nodeClear, nodeGrade, nodePreview, nodeRetreat, confirmNodePlacement } from "./bindings";

type PickerSlot = "main" | "cross" | "ramp" | "frontage" | null;
/** 两大类用法：替换（对准已有路口 / 道路 / 空地）、连接（点选开放端点）。 */
type Method = "replace" | "connect";

// 注意：游戏 UI 把相邻的多个文字片段（JSX 里 {a}{b}）分成多行显示，
// 所以凡是一行文字都先拼成一个字符串再放进元素。

/** 首页的类型：四向、三向两组（顺序同原来的类型按钮）。 */
// 井字（紧凑定向）并入定向：首页只列定向，设置页里选“形式：标准 / 紧凑（井字）”
const fourWayTypes = ["Diamond", "ExpresswayNode", "SinglePoint", "Cloverleaf", "PartialCloverleaf", "Roundabout", "Stack", "Turbine"];
/** 首页卡片对应的类型：井字算定向。 */
const cardType = (type: string) => (type === "CompactGrid" ? "Stack" : type);
/** 连接模式四个方向的颜色，与地图上的端点标记一致（NodeOverlay.NodeGroupColor）。 */
const armColors = ["rgb(255, 186, 46)", "rgb(89, 191, 255)", "rgb(217, 107, 255)", "rgb(77, 255, 145)"];
const threeWayTypes = ["ExpresswayT", "Trumpet", "SemiDirectionalT", "DirectionalT", "Wye"];
// 支持“连接”（点选开放端点求解）的类型；其余只有“替换”
const connectTypes = ["Cloverleaf", "Roundabout", "Stack", "Turbine", "CompactGrid", "Trumpet", "SemiDirectionalT", "DirectionalT", "Wye"];
/** 某种用法能用的类型（首页卡片）。 */
const typesFor = (method: Method, types: string[]) => (method === "connect" ? types.filter((type) => connectTypes.includes(type)) : types);

// 使用指南各节按轻重给标题配色：重要提醒红、注意事项橙、说明蓝、其他灰；目录前的色条同色
const guideLevelClass: Record<string, string> = {
  critical: styles.guideCritical,
  warning: styles.guideWarning,
  tip: styles.guideTip,
};
const guideMarkClass: Record<string, string> = {
  critical: styles.tocMarkCritical,
  warning: styles.tocMarkWarning,
  tip: styles.tocMarkTip,
};

const typeNameOf = (t: Strings, type: string) =>
  ({
    Diamond: t.typeDiamond, ExpresswayNode: t.typeExpressway, SinglePoint: t.typeSinglePoint, Cloverleaf: t.typeCloverleaf,
    PartialCloverleaf: t.typeParclo, Roundabout: t.typeRoundabout, Stack: t.typeStack, Turbine: t.typeTurbine,
    ExpresswayT: t.typeExpresswayT, Trumpet: t.typeTrumpet, SemiDirectionalT: t.typeSemiDirectionalT, DirectionalT: t.typeDirectionalT,
    Wye: t.typeWye, CompactGrid: t.typeCompactGrid,
  } as Record<string, string>)[type] ?? t.typeDiamond;

const introOf = (t: Strings, type: string) =>
  ({
    Diamond: t.introDiamond, ExpresswayNode: t.introExpresswayNode, SinglePoint: t.introSinglePoint, Cloverleaf: t.introCloverleaf,
    PartialCloverleaf: t.introPartialCloverleaf, Roundabout: t.introRoundabout, Stack: t.introStack, Turbine: t.introTurbine,
    ExpresswayT: t.introExpresswayT, Trumpet: t.introTrumpet, SemiDirectionalT: t.introSemiDirectionalT, DirectionalT: t.introDirectionalT,
    Wye: t.introWye, CompactGrid: t.introCompactGrid,
  } as Record<string, string>)[type] ?? "";

/** 注意事项（设置页里默认收起）：每种立交的结构、要求和交角范围。 */
const notesOf = (t: Strings, type: string) =>
  ({
    Diamond: t.diamondNote, ExpresswayNode: t.expresswayNote, SinglePoint: t.singlePointNote, Cloverleaf: t.cloverleafNote,
    PartialCloverleaf: t.parcloNote, Roundabout: t.roundaboutNote, Stack: t.stackNote, Turbine: t.turbineNote,
    ExpresswayT: t.expresswayTNote, Trumpet: t.trumpetNote, SemiDirectionalT: t.semiDirectionalTNote, DirectionalT: t.directionalTNote,
    Wye: t.wyeNote, CompactGrid: t.compactGridNote,
  } as Record<string, string>)[type] ?? "";

/** 包装游戏底部的资产栏（道路服务栏）：立交工具开着时不显示，横向面板占它的位置。 */
export const HideWhileToolActive = (Component: any) => (props: any) => {
  const state = parseJson<PanelState>(useValue(state$), { active: false });
  return state.active ? <></> : <Component {...props} />;
};

/**
 * 立交工具面板：工具激活时停靠在屏幕底部、工具栏上方（原道路服务栏的位置），横向分栏。
 * 使用指南、无视碰撞开关和关闭浮在面板右上角外面。
 * 首页：先选用法（替换 / 连接），其余位置显示注意事项；选了用法后只列这种用法支持的类型，并显示自动 / 手动。
 * 设置页：左栏高级与额外设置；中栏这种立交的简介、参数和道路（每条道路一行：左边形式，右边道路下拉）；
 * 连接模式多一栏节点选择；右栏放置与告警。道路下拉在面板上方弹出。游戏没暂停时整个面板蒙上一层、不可操作。
 */
export const InterchangePanel = () => {
  const state = parseJson<PanelState>(useValue(state$), { active: false });
  const roads = parseJson<RoadRow[]>(useValue(roads$), []);
  const [picker, setPicker] = useState<PickerSlot>(null);
  const [showNotes, setShowNotes] = useState(false);
  const [showGuide, setShowGuide] = useState(false);
  const [guideIndex, setGuideIndex] = useState(0);
  const [showNodeDetails, setShowNodeDetails] = useState(false);
  const [typeTab, setTypeTab] = useState<"four" | "three" | null>(null);
  // 首页选的用法；null = 还没选（显示替换 / 连接两块）
  const [method, setMethod] = useState<Method | null>(null);
  const anarchyOn = useValue(anarchyEnabled$);

  if (!state.active || !state.settings) {
    // 工具关掉后再打开从选用法开始（渲染中改自己的状态，React 会立即重渲染）
    if (method !== null) setMethod(null);
    return null;
  }
  const settings = state.settings;
  const t = getStrings(state.locale);

  // 全局按钮：浮在面板右上角外面
  const floatBar = (
    <div className={styles.floatBar}>
      <Button variant="flat" selected={showGuide} className={showGuide ? styles.floatButtonActive : styles.floatButton}
        onSelect={() => setShowGuide(!showGuide)}>{t.guide}</Button>
      {state.anarchy && (
        <Button variant="flat" selected={anarchyOn} className={anarchyOn ? styles.anarchyOn : styles.anarchyOff} onSelect={toggleAnarchy}>
          <img className={styles.anarchyIcon} src="coui://uil/Colored/Anarchy.svg" />
        </Button>
      )}
      <Button variant="flat" className={styles.closeButton} onSelect={closeTool}>
        <img className={styles.closeIcon} src={closeIcon} />
      </Button>
    </div>
  );

  // 顶栏：返回、标题，后面放自动 / 手动开关或提示
  const topBar = (title: string, back: (() => void) | null, middle?: React.ReactNode) => (
    <div className={styles.titleBar}>
      {back && <Button variant="flat" className={styles.backButton} onSelect={back}>{"<"}</Button>}
      <div className={styles.titleText}>{title}</div>
      <div className={styles.topMiddle}>{middle}</div>
    </div>
  );

  // 面板骨架：浮动按钮和道路下拉在面板外面（上方），mask 是暂停时的蒙版
  const dock = (top: React.ReactNode, body: React.ReactNode, columnsClass = styles.columns, popup?: React.ReactNode, mask?: React.ReactNode) => (
    <div className={styles.dockWrap}>
      <div className={styles.dockFrame}>
        {floatBar}
        {popup}
        <Panel className={styles.dock}>
          <div className={styles.dockInner}>
            {top}
            <div className={columnsClass}>{body}</div>
            {mask}
          </div>
        </Panel>
      </div>
    </div>
  );

  // 一栏：标题 + 可滚动内容，footer 固定在栏底（确认放置、操作提示）
  const column = (title: string, className: string, children: React.ReactNode, footer?: React.ReactNode) => (
    <div className={className}>
      <div className={styles.columnTitle}>{title}</div>
      <Scrollable className={styles.columnScroll} vertical trackVisibility="scrollable">{children}</Scrollable>
      {footer}
    </div>
  );

  const modeButton = (label: string, selected: boolean, disabled: boolean, onSelect: () => void) => (
    <Button variant="flat" disabled={disabled} selected={selected} className={selected ? styles.modeButtonSelected : styles.modeButton}
      onSelect={onSelect}>{label}</Button>
  );
  const methodName = (m: Method) => (m === "connect" ? t.modeConnect : t.modeReplace);
  const manualOf = (m: Method) => (m === "connect" ? settings.NodeManualMode === true : settings.ManualMode === true);
  // 自动 / 手动：替换和连接各记各的
  const modeSwitch = (m: Method) => (
    <>
      <div className={styles.modeGroupActive}>
        <div className={styles.modeGroupTitle}>{methodName(m)}</div>
        {modeButton(t.modeAuto, !manualOf(m), false, () => setValue(m === "connect" ? "NodeManualMode" : "ManualMode", false))}
        {modeButton(t.modeManual, manualOf(m), false, () => setValue(m === "connect" ? "NodeManualMode" : "ManualMode", true))}
      </div>
      <div className={styles.topHint}>{manualOf(m) ? t.manualHint : t.autoHint}</div>
    </>
  );

  const notesList = (
    <>
      {t.quickNotes.map((item, i) => (
        <div key={item} className={i === t.quickNotes.length - 1 ? styles.warnings : styles.guideItem}>{item}</div>
      ))}
      {state.anarchy && anarchyOn && <div className={styles.errors}>{t.anarchyWarning}</div>}
    </>
  );

  // 使用指南：第一次打开工具时（没看过）首页先显示它，之后从浮动的“使用指南”再看。左栏目录（按轻重配色），右栏当前一节
  if (showGuide || (state.browsing && settings.GuideSeen !== true)) {
    const sections = t.guideSections;
    const index = Math.min(guideIndex, sections.length - 1);
    const current = sections[index];
    const done = () => {
      if (settings.GuideSeen !== true) setValue("GuideSeen", true);
      setShowGuide(false);
    };
    return dock(topBar(`${t.title} · ${t.guide}`, null, <div className={styles.topHint}>{t.guideIntro}</div>), (
      <>
        <div className={styles.guideToc}>
          <div className={styles.columnTitle}>{t.guide}</div>
          <Scrollable className={styles.columnScroll} vertical trackVisibility="scrollable">
            {sections.map((section, i) => (
              <div key={section.title} className={i === index ? styles.tocItemActive : styles.tocItem} onClick={() => setGuideIndex(i)}>
                <div className={`${styles.tocMark} ${guideMarkClass[section.level] ?? ""}`} />
                <div className={styles.tocText}>{section.title}</div>
              </div>
            ))}
          </Scrollable>
        </div>
        <div className={styles.guideBody}>
          <div className={`${styles.sectionTitle} ${guideLevelClass[current.level] ?? ""}`}>{current.title}</div>
          <Scrollable className={styles.columnScroll} vertical trackVisibility="scrollable">
            {current.items.map((item) => <div key={item} className={styles.guideItem}>{item}</div>)}
          </Scrollable>
          <div className={styles.confirmRow}>
            <Button variant="flat" className={styles.navButton} disabled={index === 0} onSelect={() => setGuideIndex(index - 1)}>{"<"}</Button>
            <div className={styles.navText}>{`${index + 1} / ${sections.length}`}</div>
            <Button variant="flat" className={styles.navButton} disabled={index === sections.length - 1} onSelect={() => setGuideIndex(index + 1)}>{">"}</Button>
            <div className={styles.spacer} />
            <Button variant="flat" className={styles.guideDoneButton} onSelect={done}>{t.guideDone}</Button>
          </div>
        </div>
      </>
    ));
  }

  // 首页第一步：选用法。替换、连接各一块（说明、支持的类型、进入），剩下的位置放注意事项
  if (state.browsing && method === null) {
    const choose = (m: Method) => { setMethod(m); setTypeTab(null); };
    const supported = (m: Method) => typesFor(m, [...fourWayTypes, ...threeWayTypes]).map((type) => typeNameOf(t, type)).join("、");
    const methodBlock = (m: Method, details: string[]) => (
      <div className={styles.methodBlock}>
        <div className={styles.methodTitle}>{methodName(m)}</div>
        <div className={styles.methodHint}>{m === "connect" ? t.modeConnectHint : t.modeReplaceHint}</div>
        <Scrollable className={styles.columnScroll} vertical trackVisibility="scrollable">
          {details.map((item) => <div key={item} className={styles.guideItem}>{item}</div>)}
          <div className={styles.note}>{`${t.supportsTypes}：${supported(m)}`}</div>
        </Scrollable>
        <Button variant="flat" className={styles.methodButton} onSelect={() => choose(m)}>{`${t.enterMethod}${methodName(m)}`}</Button>
      </div>
    );
    return dock(topBar(t.title, null, <div className={styles.topHint}>{t.chooseMethod}</div>), (
      <>
        {methodBlock("replace", t.replaceDetails)}
        {methodBlock("connect", t.connectDetails)}
        {column(t.quickNotesTitle, styles.notesBlock, (
          <>
            {notesList}
            <Button variant="flat" className={styles.choice} onSelect={() => setShowGuide(true)}>{t.fullGuide}</Button>
          </>
        ))}
      </>
    ));
  }

  // 首页第二步：只列这种用法支持的类型（四向 / 三向分页），顶栏是自动 / 手动
  if (state.browsing && method !== null) {
    const tab = typeTab ?? (threeWayTypes.includes(cardType(settings.InterchangeType)) ? "three" : "four");
    const open = (type: string) => {
      if (type !== cardType(settings.InterchangeType)) setValue("InterchangeType", type);
      nodeMode(method === "connect");
      setShowNotes(false);
      setPicker(null);
      browseTypes(false);
    };
    const tabButton = (value: "four" | "three", label: string) => (
      <Button variant="flat" selected={tab === value} className={tab === value ? styles.tabSelected : styles.tab}
        onSelect={() => setTypeTab(value)}>{label}</Button>
    );
    return dock(topBar(`${t.title} · ${methodName(method)}`, () => setMethod(null), modeSwitch(method)), (
      <>
        {column(methodName(method), styles.columnLeft, (
          <>
            <div className={styles.methodHint}>{method === "connect" ? t.modeConnectHint : t.modeReplaceHint}</div>
            {(method === "connect" ? t.connectDetails : t.replaceDetails).map((item) => <div key={item} className={styles.guideItem}>{item}</div>)}
          </>
        ))}
        <div className={styles.columnMiddle}>
          <div className={styles.tabRow}>
            {tabButton("four", t.fourWay)}
            {tabButton("three", t.threeWay)}
            <div className={styles.tabHint}>{t.chooseType}</div>
          </div>
          <Scrollable className={styles.columnScroll} vertical trackVisibility="scrollable">
            <div className={styles.typeGrid}>
              {typesFor(method, tab === "four" ? fourWayTypes : threeWayTypes).map((type) => (
                <div key={type} className={type === cardType(settings.InterchangeType) ? styles.typeCardSelected : styles.typeCard} onClick={() => open(type)}>
                  <img className={styles.typeThumb} src={thumbnails[type]} />
                  <div className={styles.typeName}>{typeNameOf(t, type)}</div>
                </div>
              ))}
            </div>
            <div className={styles.note}>{t.thumbLegend}</div>
          </Scrollable>
        </div>
        {column(t.quickNotesTitle, styles.columnRight, notesList)}
      </>
    ));
  }

  // 设置页模式：自动时参数全部用默认值（C# 同样处理），只显示提示、结果和报错；手动时显示全部设置
  const manual = settings.ManualMode === true;
  const connecting = !!state.nodes?.enabled;
  const nodeManual = settings.NodeManualMode === true;
  const current: Method = connecting ? "connect" : "replace";
  const s = (connecting ? nodeManual : manual) ? settings : autoSettings(settings);
  const compactGrid = s.InterchangeType === "CompactGrid";
  const r = state.resolved;
  const nodeRevision = state.nodes?.revision;
  const match = state.match;
  const matched = match?.state === "matched";
  // 只对齐了一条普通道路：主线跟随它，被交道路仍按面板
  const roadAligned = match?.state === "road";
  // 快速路 T 型和十字型一样有地面辅路
  const expresswayT = s.InterchangeType === "ExpresswayT";
  const expressway = s.InterchangeType === "ExpresswayNode" || expresswayT;
  const singlePoint = s.InterchangeType === "SinglePoint";
  const trumpet = s.InterchangeType === "Trumpet";
  const cloverleaf = s.InterchangeType === "Cloverleaf";
  const parclo = s.InterchangeType === "PartialCloverleaf";
  const roundabout = s.InterchangeType === "Roundabout";
  const directionalT = s.InterchangeType === "DirectionalT";
  const semiDirectionalT = s.InterchangeType === "SemiDirectionalT";
  // 定向式：主线第 1 层、被交道路第 2 层，左转匝道第 3、4 层
  const stack = s.InterchangeType === "Stack";
  // 涡轮：主线第 1 层、被交道路第 2 层，左转匝道绕中心、最高第 3 层
  const turbine = s.InterchangeType === "Turbine";
  // Y 型分向：三条路约 120° 汇于一处，三条臂互不相对，没有主线 / 被交道路之分
  const wye = s.InterchangeType === "Wye";
  // 定向 / 半定向 T 型：支路与主线同层，左转匝道以跨线桥跨过主线
  const flyoverT = directionalT || semiDirectionalT;
  // 有环道的类型：环道半径可自动
  const loops = trumpet || cloverleaf || parclo || roundabout;
  // 直匝道的类型：匝道长度、分流角度只对它们起作用
  const straightRamps = s.InterchangeType === "Diamond" || expressway || singlePoint;
  // 立交内主线总是双幅（双向主线自动拆成双幅、两端过渡）
  const mainAlwaysDual = cloverleaf || parclo || roundabout || stack || turbine || wye;
  // 车道平衡：部分苜蓿叶、环岛式没有用到
  const laneBalanceUsed = !parclo && !roundabout && !wye;
  // 相交角度可调范围
  const [angleMin, angleMax] = cloverleaf ? [35, 145] : parclo || roundabout || flyoverT || stack ? [60, 120] : turbine ? [70, 110] : [45, 135];
  const tee = expresswayT || trumpet || flyoverT;
  // 支路与主线的实际夹角（0~180°）：对准路口时取匹配的，否则取面板的
  const crossing = ((matched ? match!.angle : s.CrossingAngle) % 180 + 180) % 180;
  // 斜交提示的阈值：苜蓿叶偏离 90° 超过 30°（交角小于 60°），喇叭形超过 45°
  const skewLimit = cloverleaf || parclo || roundabout || stack ? 30 : turbine ? 20 : 45;
  const steepSkew = Math.abs(crossing - 90) > skewLimit;
  // 中式快速路节点和单点立交的主线固定在上层
  const mainForcedTop = expressway || singlePoint;
  const typeName = typeNameOf(t, s.InterchangeType);
  // T 型的被交道路叫“支路”
  const crossTitle = tee ? t.branchRoad : t.crossRoad;
  // 有自己选项的类型才显示“立交选项”
  const hasOptions = tee || stack || roundabout || parclo || cloverleaf || loops;
  // 分层说明（结果和“层与角度”里共用）
  const levelSummary = (r: Resolved) => wye ? wyeLevelLine(t, r) : turbine ? turbineLevelLine(t, r) : stack ? stackLevelLine(t, r) : flyoverT ? flyoverLevelLine(t, r, s.DirectionalTwoLevel) : levelLine(t, r, cloverleaf || parclo || roundabout ? !s.CloverleafCrossOnTop : !trumpet && (s.MainOnTop || mainForcedTop));
  // Y 型的“主线”是三条路共用的道路
  const mainTitle = wye ? t.wyeRoads : t.mainRoad;

  const formOptions: { value: RoadForm; text: string }[] = [
    { value: "Auto", text: t.formAuto },
    { value: "TwoWay", text: t.formTwoWay },
    { value: "Dual", text: t.formDual },
  ];
  const togglePicker = (slot: PickerSlot) => setPicker(picker === slot ? null : slot);
  const pick = (key: keyof PanelSettings) => (name: string) => {
    setValue(key, name);
    setPicker(null);
  };
  const defaultMain = `${t.defaultMainRoad}（${state.selectedRoad}）`;

  const rampLine = r
    ? `${t.actual}：${r.rampRoad} · ${r.rampLanes} ${t.lanes}` +
      (r.rampAuto ? `（${t.target} ${r.rampTarget}，${t.mainRoad} ${r.mainLanes} ${t.lanesPerDirection}）` : "") +
      (straightRamps ? ` · ${Math.round(r.rampLength)} m` + (r.rampLength > s.RampLength ? `（${t.extended}）` : "") : "")
    : "";

  // 底部提示：固定时说明确认 / 取消；没固定时左键固定、右键退出，能旋转时加上朝向和旋转方式
  const hint = connecting ? t.nodePlacementHint : state.pinned ? t.hintPinned
    : t.hint + (state.headingFree && state.heading !== undefined ? ` · ${t.heading} ${Math.round(state.heading) % 360}° · ${t.rotateKeys}` : "");

  // 返回类型列表（停在当前用法）
  const back = () => { setPicker(null); setMethod(current); browseTypes(true); };

  // ---- 道路：每条一行，左边形式（自动 / 双向 / 双幅），右边道路下拉（在面板上方弹出选择表） ----
  const formButtons = (value: RoadForm, onChange: (v: RoadForm) => void) => (
    <div className={styles.formButtons}>
      {formOptions.map((option) => (
        <Button key={option.value} variant="flat" selected={option.value === value}
          className={option.value === value ? styles.choiceSelected : styles.choice}
          onSelect={() => option.value !== value && onChange(option.value)}>{option.text}</Button>
      ))}
    </div>
  );
  const roadRow = (title: string, slot: Exclude<PickerSlot, null>, chosen: string, left: React.ReactNode, below?: React.ReactNode) => (
    <div className={styles.roadBlock}>
      <div className={styles.roadRow}>
        <div className={styles.roadRowTitle}>{title}</div>
        <div className={styles.roadRowLeft}>{left}</div>
        <Button variant="flat" selected={picker === slot} className={picker === slot ? styles.dropdownOpen : styles.dropdown}
          onSelect={() => togglePicker(slot)}>
          <div className={styles.dropdownText}>{chosen}</div>
          <div className={picker === slot ? styles.dropdownArrowUp : styles.dropdownArrow} />
        </Button>
      </div>
      {below}
    </div>
  );
  const pickerSlots: Record<Exclude<PickerSlot, null>, { title: string; key: keyof PanelSettings; selected: string; defaultText: string; oneWay: boolean }> = {
    main: { title: mainTitle, key: "MainRoadPrefab", selected: s.MainRoadPrefab, defaultText: defaultMain, oneWay: false },
    cross: { title: crossTitle, key: "CrossRoadPrefab", selected: s.CrossRoadPrefab, defaultText: t.defaultCrossRoad, oneWay: false },
    frontage: { title: t.frontage, key: "FrontageRoadPrefab", selected: s.FrontageRoadPrefab, defaultText: t.defaultFrontage, oneWay: true },
    ramp: { title: t.ramps, key: "RampPrefab", selected: s.RampPrefab, defaultText: t.autoRamp, oneWay: true },
  };
  const slot = picker ? pickerSlots[picker] : null;
  const popup = slot && state.paused !== false && (connecting ? nodeManual : manual) ? (
    <div className={styles.pickerPopup}>
      <div className={styles.pickerPopupHead}>
        <div className={styles.pickerPopupTitle}>{`${slot.title} · ${t.road}`}</div>
        <Button variant="flat" className={styles.closeButton} onSelect={() => setPicker(null)}>
          <img className={styles.closeIcon} src={closeIcon} />
        </Button>
      </div>
      <RoadPicker key={picker!} roads={roads} selected={slot.selected} defaultText={slot.defaultText}
        initialDirection={slot.oneWay ? "oneWay" : "all"} strings={t} onPick={pick(slot.key)} />
    </div>
  ) : null;

  // 游戏没暂停：整个面板蒙上一层、不可操作，只给暂停提示和按钮（人口多时求解容易卡死，工具这时也不预览）
  const pauseMask = state.paused === false ? (
    <div className={styles.pauseMask}>
      <div className={styles.pauseTitle}>{t.pauseTitle}</div>
      <div className={styles.pauseText}>{t.pauseHint}</div>
      <Button variant="flat" className={styles.pauseButton} onSelect={pauseGame}>{t.pauseGame}</Button>
    </div>
  ) : null;

  // ---- 左栏：高级与额外设置 ----
  const leftColumn = connecting ? (
    <>
      {/* 连接 · 手动：坡度、退接、层高都可调；连接 · 自动用默认值 */}
      {nodeManual && state.nodes ? (<>
        <Stepper label="最大坡度" value={state.nodes.grade} min={3} max={30} step={1} unit="%" onChange={nodeGrade} />
        {state.nodes.grade > 18 && <div className={styles.warnings}>{t.nodeHighGradeHint}</div>}
        <Stepper label="退接上限/臂" value={state.nodes.retreatLimit ?? 1000} min={0} max={1000} step={50} unit=" m" onChange={nodeRetreat} />
        <Stepper label="基层高程偏移" value={s.BaseElevation} min={-30} max={30} step={.5} unit=" m" onChange={v => setValue("BaseElevation",v)} />
        <Stepper label={compactGrid ? "相邻层高差" : "两层高差"} value={s.LevelGap} min={0} max={30} step={.5} unit=" m" onChange={v => setValue("LevelGap",v)} />
        <div className={styles.note}>层高差 0 = 自动</div>
      </>) : <div className={styles.note}>{t.connectAutoNote}</div>}
      <div className={styles.note}>空间不足时沿真实同路型道路向外退接，遇路口或尽头停止。白点显示新接点，虚线显示改造范围。{compactGrid ? "井字采用两层转向集散、两层外侧直行；相邻层差自动至少10米，并按实际净空增大。原路端高程由接路保留。" : roundabout ? t.roundaboutHeightNote : s.InterchangeType === "Wye" ? "Y型基层采用六端点平均高程加偏移；自动层差至少10米，并按道路结构净空增大。原路端部高差由接路过渡。" : "自动层高优先保留已有高差，不足时使用10米。"}原生检查通过后可在面板确认放置；确认前会重新检查原路是否变化。</div>
    </>
  ) : !manual ? (
    <div className={styles.note}>{t.autoNote}</div>
  ) : (
    <>
      <Section title={t.match}>
        <Choice label={t.matchSwitch} value={s.MatchJunctions ? "on" : "off"}
          options={[{ value: "on", text: t.on }, { value: "off", text: t.off }]}
          onChange={(v) => setValue("MatchJunctions", v === "on")} />
        {/* 弯道适配：对准弯道时立交整体顺着道路弯曲；关掉时按直线放置 */}
        {s.MatchJunctions && (
          <Choice label={t.curveFollow} value={s.CurveFollow ? "on" : "off"}
            options={[{ value: "on", text: t.on }, { value: "off", text: t.off }]}
            onChange={(v) => setValue("CurveFollow", v === "on")} />
        )}
        {/* 三向立交对准立体交叉（十字当 T 型）：哪条路是主线 */}
        {s.MatchJunctions && tee && matched && match?.crossing && (
          <Choice label={t.teeMainRoad} value={s.TeeMainRoad === "upper" || s.TeeMainRoad === "lower" ? s.TeeMainRoad : "auto"}
            options={[{ value: "auto", text: t.auto }, { value: "upper", text: t.upperRoad }, { value: "lower", text: t.lowerRoad }]}
            onChange={(v) => setValue("TeeMainRoad", v)} />
        )}
        {/* 同层的十字路口当 T 型：没有上下层可分，可以把主线换成另一条路（支路仍取鼠标所在一侧） */}
        {s.MatchJunctions && tee && matched && !match?.crossing && match?.legs === 4 && (
          <Choice label={t.teeMainRoad} value={s.TeeMainRoad === "swap" ? "swap" : "auto"}
            options={[{ value: "auto", text: t.auto }, { value: "swap", text: t.otherRoad }]}
            onChange={(v) => setValue("TeeMainRoad", v)} />
        )}
      </Section>

      <Section title={t.levels}>
        {/* 只设基层高度，上层 = 基层 + 层间高差；改基层整座立交一起升降 */}
        {/* 对准已有道路时基层默认跟随它的实际高度（不一定是地面）；立体交叉总是跟随 */}
        {(matched || roadAligned) && !match?.crossing && (
          <Choice label={t.baseElevation} value={s.BaseFollowsMatch ? "follow" : "manual"}
            options={[{ value: "follow", text: t.baseFollow }, { value: "manual", text: t.baseManual }]}
            onChange={(v) => setValue("BaseFollowsMatch", v === "follow")} />
        )}
        {match?.baseFollows ? (
          <div className={styles.resolved}>{`${t.baseElevation}：${formatMeters(match.baseElevation)}（${t.baseFollowNote}）`}</div>
        ) : (
          <Stepper label={(matched || roadAligned) && !match?.crossing ? "" : t.baseElevation} value={s.BaseElevation}
            step={0.5} min={-30} max={30} unit=" m" onChange={(v) => setValue("BaseElevation", v)} />
        )}
        <Choice label={t.levelGap} value={s.LevelGap > 0 ? "manual" : "auto"}
          options={[{ value: "auto", text: t.gapAuto }, { value: "manual", text: t.gapManual }]}
          onChange={(v) => setValue("LevelGap", v === "auto" ? 0 : Math.ceil((r?.minimumGap ?? 7.5) * 2) / 2)} />
        {s.LevelGap > 0 && (
          <Stepper label="" value={s.LevelGap} step={0.5} min={1} max={30} unit=" m"
            onChange={(v) => setValue("LevelGap", v)} />
        )}
        {!mainForcedTop && !trumpet && !cloverleaf && !parclo && !roundabout && !flyoverT && !stack && !turbine && !wye && (
        <Choice label={t.topRoad} value={s.MainOnTop ? "main" : "cross"}
          options={[{ value: "cross", text: t.crossRoad }, { value: "main", text: t.mainRoad }]}
          onChange={(v) => setValue("MainOnTop", v === "main")} />
        )}
        {/* 苜蓿叶、部分苜蓿叶：默认主线在上层 */}
        {(cloverleaf || parclo || roundabout) && (
        <Choice label={t.topRoad} value={s.CloverleafCrossOnTop ? "cross" : "main"}
          options={[{ value: "main", text: t.mainRoad }, { value: "cross", text: t.crossRoad }]}
          onChange={(v) => setValue("CloverleafCrossOnTop", v === "cross")} />
        )}
        {r && <div className={styles.resolved}>{levelSummary(r)}</div>}
        {/* 对准已有路口时交角取路口的实际角度（见路口匹配），面板的值不起作用 */}
        {!matched && !wye && (
          <Stepper label={t.crossingAngle} value={s.CrossingAngle} step={5} min={angleMin} max={angleMax} unit="°"
            onChange={(v) => setValue("CrossingAngle", v)} />
        )}
        <div className={styles.note}>{t.elevationGuide}</div>
      </Section>

      <Section title={t.advanced}>
        {/* 间距（分隔带、匝道间距）可按米或游戏网格的格数调节，1 格 = 8 米 */}
        <Choice label={t.spacingUnit} value={s.SpacingInCells ? "cell" : "m"}
          options={[{ value: "m", text: t.meters }, { value: "cell", text: t.cells }]}
          onChange={(v) => setValue("SpacingInCells", v === "cell")} />
        {/* 匝道长度、分流角度只对直匝道的类型起作用（环道类的匝道按圆弧和坡度自动求出） */}
        {straightRamps && (
          <Stepper label={t.rampLength} value={s.RampLength} step={10} min={60} max={500} unit=" m"
            onChange={(v) => setValue("RampLength", v)} />
        )}
        {/* 分流段长度由分流角度和道路宽度算出：角度越小，分流段越长 */}
        {straightRamps && (
          <Stepper label={t.divergeAngle} value={s.RampDivergeAngle} step={1} min={5} max={45} unit="°"
            onChange={(v) => setValue("RampDivergeAngle", v)} />
        )}
        {/* 坡道越陡占地越小；实际坡度还不超过所用道路自身允许的坡度 */}
        <Stepper label={t.maxGrade} value={s.MaxGradePercent} step={1} min={3} max={25} unit="%"
          onChange={(v) => setValue("MaxGradePercent", v)} />
        <div className={styles.note}>{t.maxGradeNote}</div>
        {/* 中式快速路节点、单点立交：匝道与主线（和辅路）的间距，越小占地越窄 */}
        {(mainForcedTop || loops) && (
          <SpacingStepper label={t.rampGap} meters={s.RampGap} stepMeters={0.5} minMeters={3} maxMeters={16}
            inCells={s.SpacingInCells} cellUnit={t.cellUnit} onChange={(v) => setValue("RampGap", v)} />
        )}
      </Section>
    </>
  );

  // ---- 中栏：简介、形式、类型选项和道路 ----
  const middleColumn = (
    <>
      {/* 缩略图、简介；注意事项默认收起 */}
      <div className={styles.hero}>
        <img className={styles.heroThumb} src={thumbnails[s.InterchangeType]} />
        <div className={styles.heroText}>
          <div className={styles.intro}>{introOf(t, s.InterchangeType)}</div>
          <Button variant="flat" selected={showNotes} className={showNotes ? styles.choiceSelected : styles.choice}
            onSelect={() => setShowNotes(!showNotes)}>
            {`${t.notes} · ${showNotes ? t.collapse : t.show}`}
          </Button>
        </div>
      </div>
      {showNotes && <div className={styles.notesBox}>{notesOf(t, s.InterchangeType)}</div>}

      {/* 定向的形式：标准 / 紧凑（井字），两种都能替换和连接 */}
      {(stack || compactGrid) && (
        <Choice label={t.stackForm} value={compactGrid ? "compact" : "standard"}
          options={[{ value: "standard", text: t.stackStandard }, { value: "compact", text: t.stackCompact }]}
          onChange={(v) => setValue("InterchangeType", v === "compact" ? "CompactGrid" : "Stack")} />
      )}
      {compactGrid && <div className={styles.note}>{t.stackCompactNote}</div>}

      {/* 连接 · 手动：类型选项和匝道道路（与替换模式共用同一个设置） */}
      {connecting && nodeManual && (
        <>
          {(trumpet || flyoverT || cloverleaf || roundabout || stack) && (
            <Section title={t.typeOptions}>
              {trumpet && <Choice label={t.loopUse} value={s.TrumpetLoopForExit ? "exit" : "entry"}
                options={[{value:"exit",text:t.loopExit},{value:"entry",text:t.loopEntry}]}
                onChange={v=>setValue("TrumpetLoopForExit",v==="exit")} />}
              {flyoverT && <Choice label={t.flyoverLevels} value={s.DirectionalTwoLevel ? "two" : "three"}
                options={[{value:"two",text:t.twoLevels},{value:"three",text:t.threeLevels}]}
                onChange={v=>setValue("DirectionalTwoLevel",v==="two")} />}
              {cloverleaf && <Choice label={t.rightTurnRamp} value={s.CloverleafHugLoop ? "hug" : "outer"}
                options={[{value:"outer",text:t.rightTurnOuter},{value:"hug",text:t.rightTurnHug}]}
                onChange={v=>setValue("CloverleafHugLoop",v==="hug")} />}
              {cloverleaf && s.CloverleafHugLoop && <div className={styles.note}>{t.rightTurnHugNote}</div>}
              {roundabout && <Choice label={t.ringShape} value={s.RoundaboutRacetrack ? "racetrack" : "circle"}
                options={[{value:"circle",text:t.ringCircle},{value:"racetrack",text:t.ringRacetrack}]}
                onChange={v=>setValue("RoundaboutRacetrack",v==="racetrack")} />}
              {stack && <Choice label={t.leftTurnRamps} value={s.StackLeftExit ? "left" : "right"}
                options={[{value:"right",text:t.rightOutRightIn},{value:"left",text:t.leftOutLeftIn}]}
                onChange={v=>setValue("StackLeftExit",v==="left")} />}
              {stack && <div className={styles.note}>{s.StackLeftExit?t.stackLeftExitNote:t.stackRightExitNote}</div>}
            </Section>
          )}
          <Section title={t.paramsTitle}>
            {roadRow(t.ramps, "ramp", s.RampPrefab || t.autoRamp, <div className={styles.note}>{t.oneWayOnly}</div>)}
            <div className={styles.note}>{t.connectRoadsNote}</div>
          </Section>
        </>
      )}

      {!connecting && manual && (
        <>
          <Section title={t.paramsTitle}>
            {roadRow(mainTitle, "main", s.MainRoadPrefab || defaultMain,
              mainAlwaysDual ? <div className={styles.note}>{t.insideDual}</div> : formButtons(s.MainRoadForm, (v) => setValue("MainRoadForm", v)),
              <>
                {r && <ResolvedLine strings={t} road={r.mainRoad} form={r.mainForm} lanes={r.mainLanes} />}
                {(matched || roadAligned) && <div className={styles.note}>{t.decidedByMatch}</div>}
                {/* Y 型分向空地放置时三条路都用这条道路，分隔带在这里调 */}
                {wye && (
                  <SpacingStepper label={t.median} meters={s.MedianWidth} stepMeters={1} minMeters={0} maxMeters={96}
                    inCells={s.SpacingInCells} cellUnit={t.cellUnit} onChange={(v) => setValue("MedianWidth", v)} />
                )}
              </>)}
            {!wye && roadRow(crossTitle, "cross", s.CrossRoadPrefab || t.defaultCrossRoad,
              parclo ? <div className={styles.note}>{t.insideTwoWay}</div> : formButtons(s.CrossRoadForm, (v) => setValue("CrossRoadForm", v)),
              <>
                {r && <ResolvedLine strings={t} road={r.crossRoad} form={r.crossForm} lanes={r.crossLanes} />}
                {matched && <div className={styles.note}>{t.decidedByMatch}</div>}
                {(r?.mainForm === "Dual" || r?.crossForm === "Dual") && (
                  <SpacingStepper label={t.median} meters={s.MedianWidth} stepMeters={1} minMeters={0} maxMeters={96}
                    inCells={s.SpacingInCells} cellUnit={t.cellUnit} onChange={(v) => setValue("MedianWidth", v)} />
                )}
              </>)}
            {expressway && roadRow(t.frontage, "frontage", s.FrontageRoadPrefab || t.defaultFrontage,
              <div className={styles.note}>{t.oneWayOnly}</div>,
              r && r.frontageRoad ? <div className={styles.resolved}>{`${t.actual}：${r.frontageRoad} · ${r.frontageLanes} ${t.lanes}`}</div> : null)}
            {roadRow(t.ramps, "ramp", s.RampPrefab || t.autoRamp,
              <div className={styles.formButtons}>
                {[0, 1, 2, 3, 4, 5].map((n) => (
                  <Button key={n} variant="flat" selected={s.RampLanes === n} className={s.RampLanes === n ? styles.choiceSelected : styles.choiceNarrow}
                    onSelect={() => s.RampLanes !== n && setValue("RampLanes", n)}>{n === 0 ? t.auto : String(n)}</Button>
                ))}
              </div>,
              <>
                {r && <div className={styles.resolved}>{rampLine}</div>}
                {/* 自动选的配套道路：部分苜蓿叶的双向匝道主干、环岛式的环岛 */}
                {r && r.extraRoad && (parclo || roundabout) && (
                  <div className={styles.resolved}>{`${parclo ? t.stemRoad : t.ringRoad}：${r.extraRoad}`}</div>
                )}
              </>)}
            {laneBalanceUsed && (
              <Choice label={t.laneBalance} value={s.LaneBalance ? "on" : "off"}
                options={[{ value: "on", text: t.on }, { value: "off", text: t.off }]}
                onChange={(v) => setValue("LaneBalance", v === "on")} />
            )}
            {laneBalanceUsed && s.LaneBalance && r && <div className={styles.resolved}>{laneBalanceLine(t, r, expressway)}</div>}
            {laneBalanceUsed && s.LaneBalance && <div className={styles.note}>{t.laneBalanceNote}</div>}
          </Section>

          {hasOptions && (
            <Section title={t.typeOptions}>
              {/* T 型空地放置时选支路在哪一侧；对准已有 T 型路口时由路口决定，只对齐一条道路时放在鼠标所在一侧 */}
              {tee && !matched && !roadAligned && (
                <Choice label={t.branchSide} value={s.BranchOnLeft ? "left" : "right"}
                  options={[{ value: "left", text: t.left }, { value: "right", text: t.right }]}
                  onChange={(v) => setValue("BranchOnLeft", v === "left")} />
              )}
              {tee && roadAligned && <div className={styles.note}>{t.branchSideByCursor}</div>}
              {/* 定向 / 半定向 T 型：两条左转匝道交叉一次，三层（一条在第三层）或两层（一条下穿主线） */}
              {flyoverT && (
                <Choice label={t.flyoverLevels} value={s.DirectionalTwoLevel ? "two" : "three"}
                  options={[{ value: "three", text: t.threeLevels }, { value: "two", text: t.twoLevels }]}
                  onChange={(v) => setValue("DirectionalTwoLevel", v === "two")} />
              )}
              {/* 定向式：左转匝道右出右进（默认）或左出左进 */}
              {stack && (
                <Choice label={t.leftTurnRamps} value={s.StackLeftExit ? "left" : "right"}
                  options={[{ value: "right", text: t.rightOutRightIn }, { value: "left", text: t.leftOutLeftIn }]}
                  onChange={(v) => setValue("StackLeftExit", v === "left")} />
              )}
              {stack && <div className={styles.note}>{s.StackLeftExit ? t.stackLeftExitNote : t.stackRightExitNote}</div>}
              {/* 环岛式：圆形（默认）或沿主线拉长的跑道形 */}
              {roundabout && (
                <Choice label={t.ringShape} value={s.RoundaboutRacetrack ? "racetrack" : "circle"}
                  options={[{ value: "circle", text: t.ringCircle }, { value: "racetrack", text: t.ringRacetrack }]}
                  onChange={(v) => setValue("RoundaboutRacetrack", v === "racetrack")} />
              )}
              {/* 部分苜蓿叶：环道用于驶出主线（B2）还是驶入主线（A2） */}
              {parclo && (
                <Choice label={t.loopUse} value={s.ParcloLoopsForExit ? "exit" : "entry"}
                  options={[{ value: "exit", text: t.parcloLoopExit }, { value: "entry", text: t.parcloLoopEntry }]}
                  onChange={(v) => setValue("ParcloLoopsForExit", v === "exit")} />
              )}
              {/* 苜蓿叶右转匝道：外侧大弧线（默认），或贴环道外侧、相邻段与环道合成一条双向匝道（占地更小） */}
              {cloverleaf && (
                <Choice label={t.rightTurnRamp} value={s.CloverleafHugLoop ? "hug" : "outer"}
                  options={[{ value: "outer", text: t.rightTurnOuter }, { value: "hug", text: t.rightTurnHug }]}
                  onChange={(v) => setValue("CloverleafHugLoop", v === "hug")} />
              )}
              {cloverleaf && s.CloverleafHugLoop && <div className={styles.note}>{t.rightTurnHugNote}</div>}
              {/* 苜蓿叶交织段：直接在主线上（默认），或移到主线外侧的集散车道上 */}
              {cloverleaf && (
                <Choice label={t.weaving} value={s.CloverleafCollectorMode ?? "none"}
                  options={[{ value: "none", text: t.weavingMain }, { value: "auto", text: t.weavingAuto },
                    { value: "main", text: t.weavingMainOnly }, { value: "both", text: t.weavingBoth }]}
                  onChange={(v) => setValue("CloverleafCollectorMode", v)} />
              )}
              {cloverleaf && s.CloverleafCollectorMode && s.CloverleafCollectorMode !== "none" && (
                <div className={styles.note}>{s.CloverleafCollectorMode === "auto" ? t.weavingAutoNote : t.weavingCollectorNote}</div>
              )}
              {/* 喇叭形：环道给驶出主线还是驶入主线的车流用，另一方向走半定向匝道 */}
              {trumpet && (
                <Choice label={t.loopUse} value={s.TrumpetLoopForExit ? "exit" : "entry"}
                  options={[{ value: "exit", text: t.loopExit }, { value: "entry", text: t.loopEntry }]}
                  onChange={(v) => setValue("TrumpetLoopForExit", v === "exit")} />
              )}
              {/* 环道半径：自动 = 能放下的最小半径（随交角、坡度上限变化）；手动可以调大 */}
              {loops && (
                <Choice label={roundabout ? t.ringRadius : t.loopRadius} value={s.LoopRadius > 0 ? "manual" : "auto"}
                  options={[{ value: "auto", text: t.gapAuto }, { value: "manual", text: t.gapManual }]}
                  onChange={(v) => setValue("LoopRadius", v === "auto" ? 0 : Math.max(roundabout ? 40 : 30, Math.round((r?.loopRadius ?? 40) / 5) * 5))} />
              )}
              {loops && s.LoopRadius > 0 && (
                <Stepper label="" value={s.LoopRadius} step={5} min={roundabout ? 40 : 30} max={roundabout ? 300 : 250} unit=" m"
                  onChange={(v) => setValue("LoopRadius", v)} />
              )}
              {loops && s.LoopRadius <= 0 && r && r.loopRadius > 0 && (
                <div className={styles.resolved}>{`${t.actual}：${Math.round(r.loopRadius)} m`}</div>
              )}
              {/* 近侧两条直连匝道的半径：自动 = 满足支路下桥坡长的最小值；手动不能小于它（面板会报错并给出下限） */}
              {trumpet && (
                <Choice label={t.nearRampRadius} value={s.TrumpetNearRampRadius > 0 ? "manual" : "auto"}
                  options={[{ value: "auto", text: t.gapAuto }, { value: "manual", text: t.gapManual }]}
                  onChange={(v) => setValue("TrumpetNearRampRadius", v === "auto" ? 0 : 200)} />
              )}
              {trumpet && s.TrumpetNearRampRadius > 0 && (
                <Stepper label="" value={s.TrumpetNearRampRadius} step={10} min={30} max={1000} unit=" m"
                  onChange={(v) => setValue("TrumpetNearRampRadius", v)} />
              )}
            </Section>
          )}
        </>
      )}
    </>
  );

  // ---- 节点选择栏：连接模式才有 ----
  const nodeColumn = state.nodes?.enabled ? (
    <>
      <div className={styles.stepList}>
        {t.connectSteps.map((step, i) => <div key={i} className={styles.stepItem}>{`${i + 1}. ${step}`}</div>)}
      </div>
      {state.nodes.arms.map((a) => {
        const active = state.nodes?.activeArm === a.index;
        const done = (a.ports.length === 1 && a.ports[0].direction === "双向")
          || (a.ports.length === 2 && a.ports.some((p) => p.direction === "驶入") && a.ports.some((p) => p.direction === "驶出"));
        return (
          <div key={a.index}>
            <div className={styles.armRow}>
              <Button variant="flat" selected={active} className={active ? styles.armButtonActive : styles.armButton} onSelect={() => nodeArm(a.index)}>
                <span className={styles.armSwatch} style={{ backgroundColor: armColors[a.index] }} />
                {`${t.armNames[a.index]} · ${a.ports.length} ${t.endpoints}${done ? " · " + t.armDone : ""}`}
              </Button>
              {a.ports.length > 0 && <Button variant="flat" className={styles.armClear} onSelect={() => nodeClear(a.index)}>{t.clear}</Button>}
            </div>
            {active && a.ports.map((p) => <div key={p.id} className={styles.armPort}>{`${p.direction} · ${p.road} · ${p.height.toFixed(1)} m`}</div>)}
          </div>
        );
      })}
      <Button variant="flat" selected={showNodeDetails} className={showNodeDetails ? styles.choiceSelected : styles.choice}
        onSelect={() => setShowNodeDetails(!showNodeDetails)}>{`${t.details} · ${showNodeDetails ? t.collapse : t.show}`}</Button>
      {showNodeDetails && (
        <div className={styles.notesBox}>
          {compactGrid && <div className={styles.note}>井字支持四方向双向、双幅或混合输入，每方向至少3车道；需精确匹配直行和两倍匝道车道数的内侧集散道。4车道通常分为直行2、左转1、右转1。</div>}
          {roundabout && <div className={styles.note}>{t.roundaboutPreviewNote}</div>}
          <div className={styles.note}>{`${compactGrid ? "将四条臂分别归组，可从任一方向开始；选齐后自动按方位排序，颜色编号同步调整，方向1/3和2/4分别相对。" : s.InterchangeType === "Wye" ? "将三条臂分别归组，三条臂无主次，可从任一方向开始。" : state.nodes.arms.length === 3 ? "将三条臂分别归组，可从任一方向开始。选齐后自动识别相对主线，调整为方向 1/3，支路为方向 2；颜色和编号同步调整。" : "按顺时针归入四个方向；方向 1/3 为主线两端。"}每个方向选一个对称双向端点，或一进一出的两个单向端点，可混合选择。${state.nodes.arms.length === 3 ? "三个方向共3～6个端点。" : "四个方向共4～8个端点。"}双向道路按每方向车道数精确匹配单向道路，缺少匹配时会提示更换道路。灰色为可选端点，青色为悬停，选中后按方向着色并标号。选齐后自动尝试预览；重复点击取消，右键清空当前方向。`}</div>
          {s.InterchangeType === "Wye" && <div className={styles.note}>Y型支持三方向双向、双幅或混合输入，相邻交角90°～150°；三条臂每方向车道数须相同且为偶数，自动匹配半车道数匝道。三个端部可不同高程，由接路过渡。</div>}
        </div>
      )}
    </>
  ) : null;

  // ---- 右栏：放置与告警 ----
  const statusList = (
    <>
      {state.anarchy && anarchyOn && <div className={styles.errors}>{t.anarchyWarning}</div>}
      {/* 喇叭形交角很斜（偏离 90° 超过 45°）时，节点和立柱容易压到被拆道路的切断端或新路：提示开启无碰撞放置 */}
      {loops && steepSkew && !anarchyOn && (
        <div className={styles.warnings}>{(state.anarchy ? t.steepSkewAnarchy : t.steepSkewNoAnarchy).replace("{lo}", String(90 - skewLimit)).replace("{hi}", String(90 + skewLimit))}</div>
      )}
      {/* 井字中心的立柱：交角不是 90° 时可能没有位置（游戏报立柱与现有物体重叠），提示开启无碰撞放置 */}
      {compactGrid && !connecting && !anarchyOn && (Math.abs(crossing - 90) > 1
        || (r?.gameErrors ?? []).some((error) => error.road.includes("Pillar"))) && (
        <div className={styles.warnings}>{state.anarchy ? t.gridPillarAnarchy : t.gridPillarNoAnarchy}</div>
      )}
      {!r && !connecting && <div className={styles.hint}>{t.noPreview}</div>}
      {r && r.errors.length > 0 && (
        <div className={styles.errors}>
          <div className={styles.errorsTitle}>{t.errors}</div>
          {r.errors.map((error, i) => (
            <div key={i}>{error}</div>
          ))}
        </div>
      )}
      {r && (r.warnings ?? []).length > 0 && (
        <div className={styles.warnings}>
          {(r.warnings ?? []).map((warning, i) => <div key={i}>{warning}</div>)}
        </div>
      )}
      {/* 游戏的检查结果分两类：错误阻止放置；警告（如可被覆盖的物体）不阻止 */}
      {r && [true, false].map((blocking) => {
        const list = r.gameErrors.filter((error) => error.blocking === blocking);
        if (list.length === 0) return null;
        return (
          <div key={String(blocking)} className={blocking ? styles.errors : styles.warnings}>
            <div className={styles.errorsTitle}>{blocking ? t.gameErrors : t.gameWarnings}</div>
            {list.map((error, i) => (
              <div key={i}>
                {`${errorName(error.type, state.locale)}：${error.road}（${t.elevation} ${error.elevation.toFixed(1)} m）`}
              </div>
            ))}
          </div>
        );
      })}
    </>
  );

  const placementColumn = connecting && state.nodes ? (
    <>
      <Button variant="flat" className={styles.modeButton} onSelect={nodePreview}>{state.nodes.solveStage ? t.solveRestart : t.solveStart}</Button>
      {/* 后台求解中：阶段、已用时间和进度条（按坡度逐档尝试时是真实进度；只算一次时没有可数的步数，按时间趋近满格） */}
      {state.nodes.solveStage && (
        <div className={styles.solveBox}>
          <div>{`${state.nodes.solveStage} · ${t.solveElapsed} ${(state.nodes.solveSeconds ?? 0).toFixed(1)} ${t.seconds}`}</div>
          <div className={styles.solveBar}>
            <div className={styles.solveFill} style={{ width: `${Math.round(100 * ((state.nodes.solveProgress ?? 0) > 0 ? state.nodes.solveProgress! : (1 - Math.exp(-(state.nodes.solveSeconds ?? 0) / 3)) * 0.95))}%` }} />
          </div>
          <div className={styles.solveNote}>{t.solveNote}</div>
        </div>
      )}
      <div className={state.nodes.failure ? styles.nodeFailure : styles.nodeFeedback}>
        {state.nodes.failure ? <div className={styles.nodeFailureTitle}>无法生成预览</div> : null}
        <div className={styles.nodeStatus}>{state.nodes.message}</div>
        {state.nodes.failure && (state.nodes.retreatLimit ?? 500) < 1000 ? (
          <Button variant="flat" className={styles.nodeRetryButton}
            onSelect={() => { nodeRetreat(1000); nodePreview(); }}>上限改为1000米并重试</Button>
        ) : null}
      </div>
      {statusList}
    </>
  ) : (
    <>
      {/* 路口匹配：替换模式对准已有路口或道路时显示匹配到的道路、弯道和改造范围 */}
      {(!manual || s.MatchJunctions) && <MatchDetails strings={t} match={match} blocked={!!r && r.gameErrors.some((error) => error.blocking)} curveFollow={!manual || s.CurveFollow} />}
      {!manual && tee && roadAligned && <div className={styles.note}>{t.branchSideByCursor}</div>}
      {/* 自动：结果一览（手动时每项结果显示在对应设置旁边） */}
      {!manual && r && (
        <Section title={t.results}>
          <div className={styles.resolved}>{levelSummary(r)}</div>
          <div className={styles.resolved}>{`${mainTitle}：${r.mainRoad} · ${r.mainForm === "Dual" ? t.formDual : t.formTwoWay} · ${r.mainLanes} ${t.lanesPerDirection}`}</div>
          {!wye && <div className={styles.resolved}>{`${crossTitle}：${r.crossRoad} · ${r.crossForm === "Dual" ? t.formDual : t.formTwoWay} · ${r.crossLanes} ${t.lanesPerDirection}`}</div>}
          {expressway && r.frontageRoad && <div className={styles.resolved}>{`${t.frontage}：${r.frontageRoad} · ${r.frontageLanes} ${t.lanes}`}</div>}
          <div className={styles.resolved}>{`${t.ramps}：${rampLine.replace(`${t.actual}：`, "")}`}</div>
          {r.extraRoad && (parclo || roundabout) && <div className={styles.resolved}>{`${parclo ? t.stemRoad : t.ringRoad}：${r.extraRoad}`}</div>}
          {loops && r.loopRadius > 0 && <div className={styles.resolved}>{`${roundabout ? t.ringRadius : t.loopRadius}：${Math.round(r.loopRadius)} m`}</div>}
          {laneBalanceUsed && <div className={styles.resolved}>{laneBalanceLine(t, r, expressway)}</div>}
        </Section>
      )}
      {statusList}
    </>
  );

  // 确认放置固定在右栏底部：连接模式等原生检查通过；替换模式左键固定预览后确认（绿）或取消固定（红）
  const placementFooter = (
    <div className={styles.columnFooter}>
      {connecting && state.nodes?.hasPreview && (
        <div className={styles.confirmRow}>
          <Button variant="flat" className={styles.confirmButton}
            onSelect={() => { if (typeof nodeRevision === "number" && nodeRevision >= 0) confirmNodePlacement(nodeRevision); }}
            disabled={typeof nodeRevision !== "number" || nodeRevision < 0 || !state.nodes.canConfirm || !r || r.errors.length > 0 || r.gameErrors.some((error) => error.blocking)}>
            {t.confirmPlace}
          </Button>
        </div>
      )}
      {connecting && state.nodes?.hasPreview && !state.nodes.canConfirm && !r?.gameErrors.some((error) => error.blocking) && <div className={styles.note}>{t.nodeAwaitValidation}</div>}
      {!connecting && state.pinned && (
        <div className={styles.confirmRow}>
          <Button variant="flat" className={styles.confirmButton} onSelect={confirmPlacement}
            disabled={!r || r.errors.length > 0 || r.gameErrors.some((error) => error.blocking)}>
            {t.confirmPlace}
          </Button>
          <Button variant="flat" className={styles.cancelButton} onSelect={unpinPreview}>{t.unpin}</Button>
        </div>
      )}
      <div className={styles.hint}>{hint}</div>
    </div>
  );

  return dock(topBar(`${t.title} · ${typeName}`, back, modeSwitch(current)), (
    <>
      {column(t.colAdvanced, styles.columnLeft, leftColumn)}
      {column(typeName, styles.columnMiddle, middleColumn)}
      {connecting && column(t.colNodes, styles.columnNodes, nodeColumn)}
      {column(t.colPlacement, styles.columnRight, placementColumn, placementFooter)}
    </>
  ), connecting ? `${styles.columns} ${styles.columnsConnect}` : styles.columns, popup, pauseMask);
};

const bandName = (t: Strings, band: ElevationBand) =>
  ({ Elevated: t.bandElevated, Raised: t.bandRaised, Ground: t.bandGround, Cutting: t.bandCutting, Tunnel: t.bandTunnel })[band];

/** 例：主线中段：Highway Oneway - 2 lanes（2 车道）· 辅路加宽段：Large Road Oneway（3 车道） */
const laneBalanceLine = (t: Strings, r: Resolved, expressway: boolean) => {
  const parts = [r.mainReducedRoad ? `${t.mainReduced}：${r.mainReducedRoad}（${r.mainReducedLanes} ${t.lanes}）` : `${t.mainReduced}：${t.laneBalanceNone}`];
  if (r.crossReducedRoad)
    parts.push(`${t.crossRoad}：${r.crossReducedRoad}（${r.crossReducedLanes} ${t.lanes}）`);
  if (expressway)
    parts.push(r.frontageWidenedRoad ? `${t.frontageWidened}：${r.frontageWidenedRoad}（${r.frontageWidenedLanes} ${t.lanes}）` : `${t.frontageWidened}：${t.laneBalanceNone}`);
  return parts.join(" · ");
};

const formatMeters = (value: number) => `${Number(value.toFixed(1))} m`;

/** 例：下层：主线 0 m（路堤）· 上层：被交道路 7.5 m（高架）· 高差 7.5 m（最小 7.5 m） */
const levelLine = (t: Strings, r: Resolved, mainOnTop: boolean) => {
  const main = `${t.mainRoad} ${formatMeters(r.mainElevation)}（${bandName(t, r.mainBand)}）`;
  const cross = `${t.crossRoad} ${formatMeters(r.crossElevation)}（${bandName(t, r.crossBand)}）`;
  const [lower, upper] = mainOnTop ? [cross, main] : [main, cross];
  return `${t.lowerLevel}：${lower} · ${t.upperLevel}：${upper} · ${t.gap} ${formatMeters(r.levelGap)}（${t.minimum} ${formatMeters(r.minimumGap)}）`;
};

/** 例：基层：主线、支路 0 m · 第二层 7.5 m · 第三层 15 m（两层时：下穿 −7.5 m） */
const flyoverLevelLine = (t: Strings, r: Resolved, twoLevel: boolean) => {
  const base = r.mainElevation, gap = r.levelGap;
  // 两层时下穿段是完全的隧道（至少 −13 m）
  const third = twoLevel ? `${t.underpassLevel} ${formatMeters(Math.min(base - gap, -13))}` : `${t.thirdLevel} ${formatMeters(base + 2 * gap)}`;
  return `${t.baseLevel} ${formatMeters(base)} · ${t.secondLevel} ${formatMeters(base + gap)} · ${third}（${t.minimum} ${formatMeters(r.minimumGap)}）`;
};

/** 例：第 1 层：主线 0 m · 第 2 层：被交道路 10 m · 左转匝道：第 3 层 20 m、第 4 层 30 m（最小高差 7.5 m） */
const stackLevelLine = (t: Strings, r: Resolved) => {
  const base = r.mainElevation, gap = r.crossElevation - r.mainElevation;
  return `${t.stackLevels} ${formatMeters(base)} · ${t.stackCrossLevel} ${formatMeters(r.crossElevation)} · ` +
    `${t.stackLeftLevels} ${formatMeters(base + 2 * gap)}、${t.stackFourthLevel} ${formatMeters(base + 3 * gap)}（${t.minimum} ${formatMeters(r.minimumGap)}）`;
};

/** 例：基层：三条路 0 m · 跨线桥 10 m（最小 7.5 m） */
const wyeLevelLine = (t: Strings, r: Resolved) =>
  `${t.wyeBase} ${formatMeters(r.mainElevation)} · ${t.wyeBridge} ${formatMeters(r.mainElevation + r.levelGap)}（${t.minimum} ${formatMeters(r.minimumGap)}）`;

/** 例：第 1 层：主线 0 m · 第 2 层：被交道路 10 m · 左转匝道：最高第 3 层 20 m（最小高差 7.5 m） */
const turbineLevelLine = (t: Strings, r: Resolved) => {
  const gap = r.crossElevation - r.mainElevation;
  return `${t.stackLevels} ${formatMeters(r.mainElevation)} · ${t.stackCrossLevel} ${formatMeters(r.crossElevation)} · ` +
    `${t.turbineLeftLevels} ${formatMeters(r.crossElevation + gap)}（${t.minimum} ${formatMeters(r.minimumGap)}）`;
};

/** “预制体|形式|分隔带” → “Highway Oneway - 3 lanes（双幅，分隔带 14 m）” */
const describeRoad = (t: Strings, encoded: string) => {
  const [prefab, form, median] = encoded.split("|");
  return form === "Dual" ? `${prefab}（${t.formDual}，${t.median} ${median} m）` : `${prefab}（${t.formTwoWay}）`;
};

/** 路口匹配结果：没指着道路时给提示，无法匹配时说原因，已对准时列出道路和改造/叠建提示。 */
/** blocked：游戏报了阻止放置的错误（弯道上时提示先把道路调整平直）。 */
const MatchDetails = ({ strings: t, match, blocked, curveFollow }: { strings: Strings; match?: MatchInfo; blocked?: boolean; curveFollow: boolean }) => {
  if (!match || match.state === "none")
    return <div className={styles.note}>{t.matchHint}</div>;
  if (match.state === "error")
    return <div className={styles.warnings}>{`${t.matchFailed}：${match.error}`}</div>;
  if (match.state === "road")
    return (
      <>
        <div className={styles.resolved}>{`${t.alignedRoad}：${describeRoad(t, match.main)}`}</div>
        <div className={styles.note}>{t.alignedRoadNote}</div>
        <CurveNote strings={t} match={match} blocked={blocked} enabled={curveFollow} />
        <LevelNote strings={t} match={match} />
      </>
    );
  return (
    <>
      <div className={styles.resolved}>{`${t.matched} ${match.legs} ${t.legsWay} · ${t.angle} ${Math.round(match.angle)}°`}</div>
      <div className={styles.resolved}>{`${t.mainRoad}：${describeRoad(t, match.main)}`}</div>
      <div className={styles.resolved}>{`${t.crossRoad}：${describeRoad(t, match.cross)}`}</div>
      {match.mainTransition && <div className={styles.note}>{t.matchTransition}</div>}
      {match.crossing && <div className={styles.infoBox}>{t.matchCrossing}</div>}
      {match.dropsLeg && <div className={styles.infoBox}>{t.matchDropsLeg}</div>}
      <CurveNote strings={t} match={match} blocked={blocked} enabled={curveFollow} />
      <LevelNote strings={t} match={match} />
    </>
  );
};

/** 一条路的弯道半径：两侧都直 → 直线；两侧差不多 → 一个半径；否则两侧分别写。 */
const describeRadii = (t: Strings, [ahead, behind]: [number, number]) => {
  const one = (r: number) => (r > 0 ? `${Math.round(r)} ${t.meters}` : t.straight);
  if (ahead === 0 && behind === 0) return t.straight;
  if (ahead > 0 && behind > 0 && Math.abs(ahead - behind) < 0.05 * Math.max(ahead, behind)) return `${t.radius} ${one(ahead)}`;
  return `${t.radius} ${one(ahead)} / ${one(behind)}`;
};

/** 弯道：显示两条路的半径，提示整体弯曲、求解较慢；坡度收紧时显示实际用的坡度。 */
const CurveNote = ({ strings: t, match, blocked, enabled }: { strings: Strings; match: MatchInfo; blocked?: boolean; enabled: boolean }) => {
  const legRadii = match.legRadii ?? [];
  const curved = [...(match.mainRadii ?? [0, 0]), ...(match.crossRadii ?? [0, 0]), ...legRadii].some((r) => r > 0);
  if (!curved) return null;
  // Y 型分向：三条臂各一个半径
  const one = (r: number) => (r > 0 ? `${t.radius} ${Math.round(r)} ${t.meters}` : t.straight);
  return (
    <div className={styles.warnings}>
      <div>
        {legRadii.length > 0
          ? `${t.curveRoads}：${t.wyeRoads} ${legRadii.map(one).join(" / ")}`
          : `${t.curveRoads}：${t.mainRoad} ${describeRadii(t, match.mainRadii)} · ${t.crossRoad} ${describeRadii(t, match.crossRadii)}`}
      </div>
      {!enabled && <div>{t.curveOff}</div>}
      {enabled && <div>{t.curveNote}</div>}
      {match.curveGrade > 0 && <div>{t.curveGrade.replace("{grade}", (match.curveGrade * 100).toFixed(1))}</div>}
      {blocked && <div>{t.curveBlocked}</div>}
    </div>
  );
};

/** 叠建还是改造；改造时显示预览中要拆的原有路段数。 */
const LevelNote = ({ strings: t, match }: { strings: Strings; match: MatchInfo }) =>
  match.stacked ? (
    <div className={styles.infoBox}>{t.matchStacked}</div>
  ) : (
    <div className={styles.warnings}>
      <div>{t.matchRetrofit}</div>
      <div>{`${t.removedRoads}：${match.removed} · ${t.connectedEnds}：${match.connected}`}</div>
    </div>
  );

const Section = ({ title, children }: { title?: string; children: React.ReactNode }) => (
  <div className={styles.section}>
    {title && <div className={styles.sectionTitle}>{title}</div>}
    {children}
  </div>
);

const ResolvedLine = ({ strings, road, form, lanes }: { strings: Strings; road: string; form: string; lanes: number }) => (
  <div className={styles.resolved}>
    {`${strings.actual}：${road} · ${form === "Dual" ? strings.formDual : strings.formTwoWay} · ${lanes} ${strings.lanesPerDirection}`}
  </div>
);
