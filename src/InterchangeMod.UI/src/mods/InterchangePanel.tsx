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

type PickerSlot = "main" | "cross" | "ramp" | "frontage" | null;

// 注意：游戏 UI 把相邻的多个文字片段（JSX 里 {a}{b}）分成多行显示，
// 所以凡是一行文字都先拼成一个字符串再放进元素。

/** 首页的类型：四向、三向两组（顺序同原来的类型按钮）。 */
const fourWayTypes = ["Diamond", "ExpresswayNode", "SinglePoint", "Cloverleaf", "PartialCloverleaf", "Roundabout", "Stack", "Turbine"];
const threeWayTypes = ["ExpresswayT", "Trumpet", "SemiDirectionalT", "DirectionalT", "Wye"];

const typeNameOf = (t: Strings, type: string) =>
  ({
    Diamond: t.typeDiamond, ExpresswayNode: t.typeExpressway, SinglePoint: t.typeSinglePoint, Cloverleaf: t.typeCloverleaf,
    PartialCloverleaf: t.typeParclo, Roundabout: t.typeRoundabout, Stack: t.typeStack, Turbine: t.typeTurbine,
    ExpresswayT: t.typeExpresswayT, Trumpet: t.typeTrumpet, SemiDirectionalT: t.typeSemiDirectionalT, DirectionalT: t.typeDirectionalT,
    Wye: t.typeWye,
  } as Record<string, string>)[type] ?? t.typeDiamond;

const introOf = (t: Strings, type: string) =>
  ({
    Diamond: t.introDiamond, ExpresswayNode: t.introExpresswayNode, SinglePoint: t.introSinglePoint, Cloverleaf: t.introCloverleaf,
    PartialCloverleaf: t.introPartialCloverleaf, Roundabout: t.introRoundabout, Stack: t.introStack, Turbine: t.introTurbine,
    ExpresswayT: t.introExpresswayT, Trumpet: t.introTrumpet, SemiDirectionalT: t.introSemiDirectionalT, DirectionalT: t.introDirectionalT,
    Wye: t.introWye,
  } as Record<string, string>)[type] ?? "";

/** 注意事项（设置页里默认收起）：每种立交的结构、要求和交角范围。 */
const notesOf = (t: Strings, type: string) =>
  ({
    Diamond: t.diamondNote, ExpresswayNode: t.expresswayNote, SinglePoint: t.singlePointNote, Cloverleaf: t.cloverleafNote,
    PartialCloverleaf: t.parcloNote, Roundabout: t.roundaboutNote, Stack: t.stackNote, Turbine: t.turbineNote,
    ExpresswayT: t.expresswayTNote, Trumpet: t.trumpetNote, SemiDirectionalT: t.semiDirectionalTNote, DirectionalT: t.directionalTNote,
    Wye: t.wyeNote,
  } as Record<string, string>)[type] ?? "";

/**
 * 立交工具面板：工具激活时显示在屏幕左侧。
 * 首页按四向、三向排出各类立交的缩略图，点选后进入这种立交的设置页（这时工具才开始求解、预览）；
 * 设置页上方是缩略图、简介和默认收起的注意事项，下面是设置和高级设置；错误、确认放置和操作提示固定在底部。
 */
export const InterchangePanel = () => {
  const state = parseJson<PanelState>(useValue(state$), { active: false });
  const roads = parseJson<RoadRow[]>(useValue(roads$), []);
  const [picker, setPicker] = useState<PickerSlot>(null);
  const [showAdvanced, setShowAdvanced] = useState(false);
  const [showNotes, setShowNotes] = useState(false);
  const [showGuide, setShowGuide] = useState(false);
  const anarchyOn = useValue(anarchyEnabled$);

  if (!state.active || !state.settings) return null;
  const settings = state.settings;
  const t = getStrings(state.locale);

  // 自己画的顶栏（游戏 Panel 自带的标题栏居中、不好放别的按钮）：设置页左边是返回，标题靠左，
  // 右边是无视碰撞开关（装了 Anarchy 时，开启时标红）和关闭
  const titleBar = (title: string, back: boolean) => (
    <div className={styles.titleBar}>
      {back && <Button variant="flat" className={styles.backButton} onSelect={() => browseTypes(true)}>{"<"}</Button>}
      <div className={styles.titleText}>{title}</div>
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

  // 使用指南：第一次打开工具时（没看过）首页先显示它，之后从首页的“使用指南”按钮再看
  if (state.browsing && (showGuide || settings.GuideSeen !== true)) {
    const done = () => {
      if (settings.GuideSeen !== true) setValue("GuideSeen", true);
      setShowGuide(false);
    };
    return (
      <div className={styles.container}>
        <Panel className={styles.panel}>
          {titleBar(`${t.title} · ${t.guide}`, false)}
          <Scrollable className={styles.homeContent} vertical trackVisibility="scrollable">
            <div className={styles.homeHint}>{t.guideIntro}</div>
            {t.guideSections.map((section) => (
              <div key={section.title} className={styles.section}>
                <div className={styles.sectionTitle}>{section.title}</div>
                {section.items.map((item) => <div key={item} className={styles.guideItem}>{item}</div>)}
              </div>
            ))}
          </Scrollable>
          <div className={styles.confirmRow}>
            <Button variant="flat" className={styles.confirmButton} onSelect={done}>{t.guideDone}</Button>
          </div>
        </Panel>
      </div>
    );
  }

  // 首页：只选类型，工具不求解
  if (state.browsing) {
    const open = (type: string) => {
      if (type !== settings.InterchangeType) setValue("InterchangeType", type);
      setShowNotes(false);
      setPicker(null);
      browseTypes(false);
    };
    const grid = (types: string[]) => (
      <div className={styles.typeGrid}>
        {types.map((type) => (
          <div key={type} className={type === settings.InterchangeType ? styles.typeCardSelected : styles.typeCard} onClick={() => open(type)}>
            <img className={styles.typeThumb} src={thumbnails[type]} />
            <div className={styles.typeName}>{typeNameOf(t, type)}</div>
          </div>
        ))}
      </div>
    );
    return (
      <div className={styles.container}>
        <Panel className={styles.panel}>
          {titleBar(t.title, false)}
          <Scrollable className={styles.homeContent} vertical trackVisibility="scrollable">
            <div className={styles.homeHintRow}>
              <div className={styles.homeHint}>{t.chooseType}</div>
              <Button variant="flat" className={styles.guideButton} onSelect={() => setShowGuide(true)}>{t.guide}</Button>
            </div>
            <div className={styles.groupTitle}>{t.fourWay}</div>
            {grid(fourWayTypes)}
            <div className={styles.groupTitle}>{t.threeWay}</div>
            {grid(threeWayTypes)}
          </Scrollable>
          {state.anarchy && anarchyOn && <div className={styles.errors}>{t.anarchyWarning}</div>}
        </Panel>
      </div>
    );
  }

  // 设置页模式：自动时参数全部用默认值（C# 同样处理），只显示提示、结果和报错；手动时显示全部设置
  const manual = settings.ManualMode === true;
  const s = manual ? settings : autoSettings(settings);
  const r = state.resolved;
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
  const laneBalanceUsed = !parclo && !roundabout && !(stack && s.StackLeftExit) && !wye;
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

  // 游戏没暂停：设置页整个挡住，只给暂停提示和按钮（人口多时求解容易卡死，工具这时也不预览）
  if (state.paused === false)
    return (
      <div className={styles.container}>
        <Panel className={styles.panel}>
          {titleBar(`${t.title} · ${typeName}`, true)}
          <div className={styles.pauseBlock}>
            <div className={styles.pauseTitle}>{t.pauseTitle}</div>
            <div>{t.pauseHint}</div>
            <Button variant="flat" className={styles.confirmButton} onSelect={pauseGame}>{t.pauseGame}</Button>
          </div>
        </Panel>
      </div>
    );

  // 底部提示：固定时说明确认 / 取消；没固定时左键固定、右键退出，能旋转时加上朝向和旋转方式
  const hint = state.pinned ? t.hintPinned
    : t.hint + (state.headingFree && state.heading !== undefined ? ` · ${t.heading} ${Math.round(state.heading) % 360}° · ${t.rotateKeys}` : "");

  return (
    <div className={styles.container}>
      <Panel className={styles.panel}>
        {titleBar(`${t.title} · ${typeName}`, true)}
        <Scrollable className={styles.content} vertical trackVisibility="scrollable">
          {/* 缩略图、简介；注意事项默认收起 */}
          <div className={styles.hero}>
            <img className={styles.heroThumb} src={thumbnails[s.InterchangeType]} />
            <div className={styles.heroText}>
              <div className={styles.heroName}>{typeName}</div>
              <div className={styles.intro}>{introOf(t, s.InterchangeType)}</div>
              <Button variant="flat" selected={showNotes} className={showNotes ? styles.choiceSelected : styles.choice}
                onSelect={() => setShowNotes(!showNotes)}>
                {`${t.notes} · ${showNotes ? t.collapse : t.show}`}
              </Button>
            </div>
          </div>
          {showNotes && <div className={styles.notesBox}>{notesOf(t, s.InterchangeType)}</div>}

          {/* 主开关：自动 / 手动 */}
          <div className={styles.modeRow}>
            <Button variant="flat" selected={!manual} className={manual ? styles.modeButton : styles.modeButtonSelected}
              onSelect={() => manual && setValue("ManualMode", false)}>{t.modeAuto}</Button>
            <Button variant="flat" selected={manual} className={manual ? styles.modeButtonSelected : styles.modeButton}
              onSelect={() => !manual && setValue("ManualMode", true)}>{t.modeManual}</Button>
          </div>

          {/* 自动：对准路口照常，道路、匝道、分层等全部自动；只显示提示和结果（报错在底部） */}
          {!manual && (
            <>
              <Section title={t.hints}>
                <div className={styles.note}>{t.autoNote}</div>
                <MatchDetails strings={t} match={match} blocked={!!r && r.gameErrors.some((error) => error.blocking)} curveFollow={true} />
                {tee && roadAligned && <div className={styles.note}>{t.branchSideByCursor}</div>}
              </Section>
              {r && (
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
            </>
          )}

          {manual && (
            <>

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
            {s.MatchJunctions && <MatchDetails strings={t} match={match} blocked={!!r && r.gameErrors.some((error) => error.blocking)} curveFollow={s.CurveFollow} />}
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

          <Section title={mainTitle}>
            <RoadChooser strings={t} chosen={s.MainRoadPrefab || defaultMain}
              open={picker === "main"} onToggle={() => togglePicker("main")} />
            {picker === "main" && (
              <RoadPicker roads={roads} selected={s.MainRoadPrefab} defaultText={defaultMain}
                strings={t} onPick={pick("MainRoadPrefab")} />
            )}
            {mainAlwaysDual ? (
              <div className={styles.note}>{t.mainAlwaysDual}</div>
            ) : (
              <Choice label={t.form} value={s.MainRoadForm} options={formOptions} onChange={(v) => setValue("MainRoadForm", v)} />
            )}
            {r && <ResolvedLine strings={t} road={r.mainRoad} form={r.mainForm} lanes={r.mainLanes} />}
            {(matched || roadAligned) && <div className={styles.note}>{t.decidedByMatch}</div>}
            {/* Y 型分向空地放置时三条路都用这条道路，分隔带在这里调 */}
            {wye && (
              <SpacingStepper label={t.median} meters={s.MedianWidth} stepMeters={1} minMeters={0} maxMeters={96}
                inCells={s.SpacingInCells} cellUnit={t.cellUnit} onChange={(v) => setValue("MedianWidth", v)} />
            )}
          </Section>

          {!wye && (
          <Section title={crossTitle}>
            <RoadChooser strings={t} chosen={s.CrossRoadPrefab || t.defaultCrossRoad}
              open={picker === "cross"} onToggle={() => togglePicker("cross")} />
            {picker === "cross" && (
              <RoadPicker roads={roads} selected={s.CrossRoadPrefab} defaultText={t.defaultCrossRoad}
                strings={t} onPick={pick("CrossRoadPrefab")} />
            )}
            {parclo ? (
              <div className={styles.note}>{t.crossNeedsTwoWay}</div>
            ) : (
              <Choice label={t.form} value={s.CrossRoadForm} options={formOptions} onChange={(v) => setValue("CrossRoadForm", v)} />
            )}
            {r && <ResolvedLine strings={t} road={r.crossRoad} form={r.crossForm} lanes={r.crossLanes} />}
            {matched && <div className={styles.note}>{t.decidedByMatch}</div>}
            {(r?.mainForm === "Dual" || r?.crossForm === "Dual") && (
              <SpacingStepper label={t.median} meters={s.MedianWidth} stepMeters={1} minMeters={0} maxMeters={96}
                inCells={s.SpacingInCells} cellUnit={t.cellUnit} onChange={(v) => setValue("MedianWidth", v)} />
            )}
          </Section>
          )}

          {expressway && (
            <Section title={t.frontage}>
              <RoadChooser strings={t} chosen={s.FrontageRoadPrefab || t.defaultFrontage}
                open={picker === "frontage"} onToggle={() => togglePicker("frontage")} />
              {picker === "frontage" && (
                <RoadPicker roads={roads} selected={s.FrontageRoadPrefab} defaultText={t.defaultFrontage} initialDirection="oneWay"
                  strings={t} onPick={pick("FrontageRoadPrefab")} />
              )}
              {r && r.frontageRoad && <div className={styles.resolved}>{`${t.actual}：${r.frontageRoad} · ${r.frontageLanes} ${t.lanes}`}</div>}
            </Section>
          )}

          <Section title={t.ramps}>
            <Choice label={t.rampLanes} value={s.RampLanes}
              options={[0, 1, 2, 3, 4, 5].map((n) => ({ value: n, text: n === 0 ? t.auto : String(n) }))}
              onChange={(v) => setValue("RampLanes", v)} />
            <RoadChooser strings={t} chosen={s.RampPrefab || t.autoRamp}
              open={picker === "ramp"} onToggle={() => togglePicker("ramp")} />
            {picker === "ramp" && (
              <RoadPicker roads={roads} selected={s.RampPrefab} defaultText={t.autoRamp} initialDirection="oneWay"
                strings={t} onPick={pick("RampPrefab")} />
            )}
            {r && <div className={styles.resolved}>{rampLine}</div>}
            {/* 自动选的配套道路：部分苜蓿叶的双向匝道主干、环岛式的环岛 */}
            {r && r.extraRoad && (parclo || roundabout) && (
              <div className={styles.resolved}>{`${parclo ? t.stemRoad : t.ringRoad}：${r.extraRoad}`}</div>
            )}
            {laneBalanceUsed && (
              <Choice label={t.laneBalance} value={s.LaneBalance ? "on" : "off"}
                options={[{ value: "on", text: t.on }, { value: "off", text: t.off }]}
                onChange={(v) => setValue("LaneBalance", v === "on")} />
            )}
            {laneBalanceUsed && s.LaneBalance && r && <div className={styles.resolved}>{laneBalanceLine(t, r, expressway)}</div>}
            {laneBalanceUsed && s.LaneBalance && <div className={styles.note}>{t.laneBalanceNote}</div>}
          </Section>

          <div className={styles.row}>
            <div className={styles.label}>{t.advanced}</div>
            <Button variant="flat" selected={showAdvanced} className={showAdvanced ? styles.choiceSelected : styles.choice}
              onSelect={() => setShowAdvanced(!showAdvanced)}>
              {showAdvanced ? t.collapse : t.show}
            </Button>
          </div>
          {showAdvanced && (
            <Section>
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
                <SpacingStepper label={t.rampGap} meters={s.RampGap} stepMeters={0.5} minMeters={0.5} maxMeters={16}
                  inCells={s.SpacingInCells} cellUnit={t.cellUnit} onChange={(v) => setValue("RampGap", v)} />
              )}
            </Section>
          )}

            </>
          )}
        </Scrollable>
        {/* 错误和警告放在滚动区外面单独一块，滚动或调参数时始终看得到 */}
        <Scrollable className={styles.status} vertical trackVisibility="scrollable">
          {state.anarchy && anarchyOn && <div className={styles.errors}>{t.anarchyWarning}</div>}
          {/* 喇叭形交角很斜（偏离 90° 超过 45°）时，节点和立柱容易压到被拆道路的切断端或新路：提示开启无碰撞放置 */}
          {loops && steepSkew && !anarchyOn && (
            <div className={styles.warnings}>{(state.anarchy ? t.steepSkewAnarchy : t.steepSkewNoAnarchy).replace("{lo}", String(90 - skewLimit)).replace("{hi}", String(90 + skewLimit))}</div>
          )}
          {!r && <div className={styles.hint}>{t.noPreview}</div>}
          {r && r.errors.length > 0 && (
            <div className={styles.errors}>
              <div className={styles.errorsTitle}>{t.errors}</div>
              {r.errors.map((error, i) => (
                <div key={i}>{error}</div>
              ))}
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
        </Scrollable>
        {/* 两步放置：左键固定预览后，在这里确认放置（绿）或取消固定（红） */}
        {state.pinned && (
          <div className={styles.confirmRow}>
            <Button variant="flat" className={styles.confirmButton} onSelect={confirmPlacement}
              disabled={!r || r.errors.length > 0 || r.gameErrors.some((error) => error.blocking)}>
              {t.confirmPlace}
            </Button>
            <Button variant="flat" className={styles.cancelButton} onSelect={unpinPreview}>{t.unpin}</Button>
          </div>
        )}
        <div className={styles.hint}>{hint}</div>
      </Panel>
    </div>
  );
};

const bandName = (t: Strings, band: ElevationBand) =>
  ({ Elevated: t.bandElevated, Raised: t.bandRaised, Ground: t.bandGround, Cutting: t.bandCutting, Tunnel: t.bandTunnel })[band];

/** 例：主线中段：Highway Oneway - 2 lanes（2 车道）· 辅路加宽段：Large Road Oneway（3 车道） */
const laneBalanceLine = (t: Strings, r: Resolved, expressway: boolean) => {
  const parts = [r.mainReducedRoad ? `${t.mainReduced}：${r.mainReducedRoad}（${r.mainReducedLanes} ${t.lanes}）` : `${t.mainReduced}：${t.laneBalanceNone}`];
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

const RoadChooser = ({ strings, chosen, open, onToggle }: { strings: Strings; chosen: string; open: boolean; onToggle: () => void }) => (
  <div className={styles.row}>
    <div className={styles.label}>{strings.road}</div>
    <div className={styles.chosenRoad}>{chosen}</div>
    <Button variant="flat" selected={open} className={open ? styles.choiceSelected : styles.choice} onSelect={onToggle}>
      {open ? strings.collapse : strings.choose}
    </Button>
  </div>
);

const ResolvedLine = ({ strings, road, form, lanes }: { strings: Strings; road: string; form: string; lanes: number }) => (
  <div className={styles.resolved}>
    {`${strings.actual}：${road} · ${form === "Dual" ? strings.formDual : strings.formTwoWay} · ${lanes} ${strings.lanesPerDirection}`}
  </div>
);
