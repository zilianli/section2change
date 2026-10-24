import { useState } from "react";
import { useValue } from "cs2/api";
import { Button, Panel, Scrollable } from "cs2/ui";
import { anarchyEnabled$, closeTool, ElevationBand, MatchInfo, PanelSettings, PanelState, parseJson, Resolved, RoadForm, roads$, RoadRow, setValue, state$, toggleAnarchy } from "./bindings";
import { errorName, getStrings, Strings } from "./i18n";
import { RoadPicker } from "./RoadPicker";
import { Choice, SpacingStepper, Stepper } from "./Stepper";
import styles from "./panel.module.scss";

type PickerSlot = "main" | "cross" | "ramp" | "frontage" | null;

// 注意：游戏 UI 把相邻的多个文字片段（JSX 里 {a}{b}）分成多行显示，
// 所以凡是一行文字都先拼成一个字符串再放进元素。

/** 立交工具面板：工具激活时显示在屏幕左侧，内容超出时可滚动。 */
export const InterchangePanel = () => {
  const state = parseJson<PanelState>(useValue(state$), { active: false });
  const roads = parseJson<RoadRow[]>(useValue(roads$), []);
  const [picker, setPicker] = useState<PickerSlot>(null);
  const [showAdvanced, setShowAdvanced] = useState(false);
  const anarchyOn = useValue(anarchyEnabled$);

  if (!state.active || !state.settings) return null;
  const s = state.settings;
  const r = state.resolved;
  const match = state.match;
  const matched = match?.state === "matched";
  // 只对齐了一条普通道路：主线跟随它，被交道路仍按面板
  const roadAligned = match?.state === "road";
  const t = getStrings(state.locale);
  // 快速路 T 型和十字型一样有地面辅路
  const expresswayT = s.InterchangeType === "ExpresswayT";
  const expressway = s.InterchangeType === "ExpresswayNode" || expresswayT;
  const singlePoint = s.InterchangeType === "SinglePoint";
  const trumpet = s.InterchangeType === "Trumpet";
  const cloverleaf = s.InterchangeType === "Cloverleaf";
  // 有环道的类型：环道半径可自动
  const loops = trumpet || cloverleaf;
  const tee = expresswayT || trumpet;
  // 支路与主线的实际夹角（0~180°）：对准路口时取匹配的，否则取面板的
  const crossing = ((matched ? match!.angle : s.CrossingAngle) % 180 + 180) % 180;
  const steepSkew = Math.abs(crossing - 90) > 45;
  // 中式快速路节点和单点立交的主线固定在上层
  const mainForcedTop = expressway || singlePoint;
  const typeName = cloverleaf ? t.typeCloverleaf : trumpet ? t.typeTrumpet : expresswayT ? t.typeExpresswayT : expressway ? t.typeExpressway : singlePoint ? t.typeSinglePoint : t.typeDiamond;
  // T 型的被交道路叫“支路”
  const crossTitle = tee ? t.branchRoad : t.crossRoad;

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
      ` · ${Math.round(r.rampLength)} m` +
      (r.rampLength > s.RampLength ? `（${t.extended}）` : "")
    : "";

  return (
    <div className={styles.container}>
      <Panel header={`${t.title} · ${typeName}`} onClose={closeTool} className={styles.panel}>
        <Scrollable className={styles.content} vertical trackVisibility="scrollable">
          <Section title={t.interchangeType}>
            <Choice label={t.fourWay} value={s.InterchangeType}
              options={[{ value: "Diamond", text: t.typeDiamond }, { value: "ExpresswayNode", text: t.typeExpressway }, { value: "SinglePoint", text: t.typeSinglePoint }, { value: "Cloverleaf", text: t.typeCloverleaf }]}
              onChange={(v) => setValue("InterchangeType", v)} />
            <Choice label={t.threeWay} value={s.InterchangeType}
              options={[{ value: "ExpresswayT", text: t.typeExpresswayT }, { value: "Trumpet", text: t.typeTrumpet }]}
              onChange={(v) => setValue("InterchangeType", v)} />
            {expressway && !expresswayT && <div className={styles.note}>{t.expresswayNote}</div>}
            {expresswayT && <div className={styles.note}>{t.expresswayTNote}</div>}
            {/* T 型空地放置时选支路在哪一侧；对准已有 T 型路口时由路口决定，只对齐一条道路时放在鼠标所在一侧 */}
            {tee && !matched && !roadAligned && (
              <Choice label={t.branchSide} value={s.BranchOnLeft ? "left" : "right"}
                options={[{ value: "left", text: t.left }, { value: "right", text: t.right }]}
                onChange={(v) => setValue("BranchOnLeft", v === "left")} />
            )}
            {tee && roadAligned && <div className={styles.note}>{t.branchSideByCursor}</div>}
            {singlePoint && <div className={styles.note}>{t.singlePointNote}</div>}
            {trumpet && <div className={styles.note}>{t.trumpetNote}</div>}
            {cloverleaf && <div className={styles.note}>{t.cloverleafNote}</div>}
            {/* 苜蓿叶右转匝道：外侧大弧线（默认），或贴环道外侧、相邻段与环道合成一条双向匝道（占地更小） */}
            {cloverleaf && (
              <Choice label={t.rightTurnRamp} value={s.CloverleafHugLoop ? "hug" : "outer"}
                options={[{ value: "outer", text: t.rightTurnOuter }, { value: "hug", text: t.rightTurnHug }]}
                onChange={(v) => setValue("CloverleafHugLoop", v === "hug")} />
            )}
            {cloverleaf && s.CloverleafHugLoop && <div className={styles.note}>{t.rightTurnHugNote}</div>}
            {/* 苜蓿叶交织段：直接在主线上（默认），或移到主线外侧的集散车道上 */}
            {cloverleaf && (
              <Choice label={t.weaving} value={s.CloverleafCollector ? "cd" : "main"}
                options={[{ value: "main", text: t.weavingMain }, { value: "cd", text: t.weavingCollector }]}
                onChange={(v) => setValue("CloverleafCollector", v === "cd")} />
            )}
            {cloverleaf && s.CloverleafCollector && <div className={styles.note}>{t.weavingCollectorNote}</div>}
            {/* 喇叭形：环道给驶出主线还是驶入主线的车流用，另一方向走半定向匝道 */}
            {trumpet && (
              <Choice label={t.loopUse} value={s.TrumpetLoopForExit ? "exit" : "entry"}
                options={[{ value: "exit", text: t.loopExit }, { value: "entry", text: t.loopEntry }]}
                onChange={(v) => setValue("TrumpetLoopForExit", v === "exit")} />
            )}
            {/* 环道半径：自动 = 能放下的最小半径（随交角、坡度上限变化）；手动可以调大 */}
            {loops && (
              <Choice label={t.loopRadius} value={s.LoopRadius > 0 ? "manual" : "auto"}
                options={[{ value: "auto", text: t.gapAuto }, { value: "manual", text: t.gapManual }]}
                onChange={(v) => setValue("LoopRadius", v === "auto" ? 0 : Math.max(30, Math.round((r?.loopRadius ?? 40) / 5) * 5))} />
            )}
            {loops && s.LoopRadius > 0 && (
              <Stepper label="" value={s.LoopRadius} step={5} min={30} max={250} unit=" m"
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
            {/* 装了 Anarchy 才显示：开关就是 Anarchy 自己的开关，和它的工具栏按钮同步 */}
            {state.anarchy && (
              <div className={styles.row}>
                <div className={styles.label}>{t.anarchy}</div>
                <Button variant="flat" selected={anarchyOn} className={anarchyOn ? styles.anarchyOn : styles.choice}
                  onSelect={toggleAnarchy}>
                  <img className={styles.anarchyIcon} src="coui://uil/Colored/Anarchy.svg" />
                </Button>
                <div className={styles.label}>{anarchyOn ? t.on : t.off}</div>
              </div>
            )}
          </Section>

          <Section title={t.match}>
            <Choice label={t.matchSwitch} value={s.MatchJunctions ? "on" : "off"}
              options={[{ value: "on", text: t.on }, { value: "off", text: t.off }]}
              onChange={(v) => setValue("MatchJunctions", v === "on")} />
            {s.MatchJunctions && <MatchDetails strings={t} match={match} />}
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
            {!mainForcedTop && !trumpet && !cloverleaf && (
            <Choice label={t.topRoad} value={s.MainOnTop ? "main" : "cross"}
              options={[{ value: "cross", text: t.crossRoad }, { value: "main", text: t.mainRoad }]}
              onChange={(v) => setValue("MainOnTop", v === "main")} />
            )}
            {/* 苜蓿叶：默认主线在上层 */}
            {cloverleaf && (
            <Choice label={t.topRoad} value={s.CloverleafCrossOnTop ? "cross" : "main"}
              options={[{ value: "main", text: t.mainRoad }, { value: "cross", text: t.crossRoad }]}
              onChange={(v) => setValue("CloverleafCrossOnTop", v === "cross")} />
            )}
            {r && <div className={styles.resolved}>{levelLine(t, r, cloverleaf ? !s.CloverleafCrossOnTop : !trumpet && (s.MainOnTop || mainForcedTop))}</div>}
            <Stepper label={t.crossingAngle} value={s.CrossingAngle} step={5} min={45} max={135} unit="°"
              onChange={(v) => setValue("CrossingAngle", v)} />
            <div className={styles.note}>{t.elevationGuide}</div>
          </Section>

          <Section title={t.mainRoad}>
            <RoadChooser strings={t} chosen={s.MainRoadPrefab || defaultMain}
              open={picker === "main"} onToggle={() => togglePicker("main")} />
            {picker === "main" && (
              <RoadPicker roads={roads} selected={s.MainRoadPrefab} defaultText={defaultMain}
                strings={t} onPick={pick("MainRoadPrefab")} />
            )}
            <Choice label={t.form} value={s.MainRoadForm} options={formOptions} onChange={(v) => setValue("MainRoadForm", v)} />
            {r && <ResolvedLine strings={t} road={r.mainRoad} form={r.mainForm} lanes={r.mainLanes} />}
            {(matched || roadAligned) && <div className={styles.note}>{t.decidedByMatch}</div>}
          </Section>

          <Section title={crossTitle}>
            <RoadChooser strings={t} chosen={s.CrossRoadPrefab || t.defaultCrossRoad}
              open={picker === "cross"} onToggle={() => togglePicker("cross")} />
            {picker === "cross" && (
              <RoadPicker roads={roads} selected={s.CrossRoadPrefab} defaultText={t.defaultCrossRoad}
                strings={t} onPick={pick("CrossRoadPrefab")} />
            )}
            <Choice label={t.form} value={s.CrossRoadForm} options={formOptions} onChange={(v) => setValue("CrossRoadForm", v)} />
            {r && <ResolvedLine strings={t} road={r.crossRoad} form={r.crossForm} lanes={r.crossLanes} />}
            {matched && <div className={styles.note}>{t.decidedByMatch}</div>}
            {(r?.mainForm === "Dual" || r?.crossForm === "Dual") && (
              <SpacingStepper label={t.median} meters={s.MedianWidth} stepMeters={1} minMeters={0} maxMeters={96}
                inCells={s.SpacingInCells} cellUnit={t.cellUnit} onChange={(v) => setValue("MedianWidth", v)} />
            )}
          </Section>

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
            <Choice label={t.laneBalance} value={s.LaneBalance ? "on" : "off"}
              options={[{ value: "on", text: t.on }, { value: "off", text: t.off }]}
              onChange={(v) => setValue("LaneBalance", v === "on")} />
            {s.LaneBalance && r && <div className={styles.resolved}>{laneBalanceLine(t, r, expressway)}</div>}
            {s.LaneBalance && <div className={styles.note}>{t.laneBalanceNote}</div>}
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
              <Stepper label={t.rampLength} value={s.RampLength} step={10} min={60} max={500} unit=" m"
                onChange={(v) => setValue("RampLength", v)} />
              {/* 分流段长度由分流角度和道路宽度算出：角度越小，分流段越长 */}
              <Stepper label={t.divergeAngle} value={s.RampDivergeAngle} step={1} min={5} max={45} unit="°"
                onChange={(v) => setValue("RampDivergeAngle", v)} />
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

        </Scrollable>
        {/* 错误和警告放在滚动区外面单独一块，滚动或调参数时始终看得到 */}
        <Scrollable className={styles.status} vertical trackVisibility="scrollable">
          {state.anarchy && anarchyOn && <div className={styles.errors}>{t.anarchyWarning}</div>}
          {/* 喇叭形交角很斜（偏离 90° 超过 45°）时，节点和立柱容易压到被拆道路的切断端或新路：提示开启无碰撞放置 */}
          {loops && steepSkew && !anarchyOn && (
            <div className={styles.warnings}>{state.anarchy ? t.steepSkewAnarchy : t.steepSkewNoAnarchy}</div>
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
        <div className={styles.hint}>{t.hint}</div>
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

/** “预制体|形式|分隔带” → “Highway Oneway - 3 lanes（双幅，分隔带 14 m）” */
const describeRoad = (t: Strings, encoded: string) => {
  const [prefab, form, median] = encoded.split("|");
  return form === "Dual" ? `${prefab}（${t.formDual}，${t.median} ${median} m）` : `${prefab}（${t.formTwoWay}）`;
};

/** 路口匹配结果：没指着道路时给提示，无法匹配时说原因，已对准时列出道路和改造/叠建提示。 */
const MatchDetails = ({ strings: t, match }: { strings: Strings; match?: MatchInfo }) => {
  if (!match || match.state === "none")
    return <div className={styles.note}>{t.matchHint}</div>;
  if (match.state === "error")
    return <div className={styles.warnings}>{`${t.matchFailed}：${match.error}`}</div>;
  if (match.state === "road")
    return (
      <>
        <div className={styles.resolved}>{`${t.alignedRoad}：${describeRoad(t, match.main)}`}</div>
        <div className={styles.note}>{t.alignedRoadNote}</div>
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
      <LevelNote strings={t} match={match} />
    </>
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
