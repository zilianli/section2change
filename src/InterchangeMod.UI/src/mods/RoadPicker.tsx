import { useMemo, useState } from "react";
import { Button, Scrollable } from "cs2/ui";
import { isOneWay, lanesPerDirection, RoadRow } from "./bindings";
import { Strings } from "./i18n";
import styles from "./panel.module.scss";

type TypeFilter = "all" | "street" | "highway";
type DirectionFilter = "all" | "oneWay" | "twoWay";

interface RoadPickerProps {
  roads: RoadRow[];
  /** 当前选中的道路名，"" 表示默认/自动。 */
  selected: string;
  /** 第一行“默认/自动”选项的文字。 */
  defaultText: string;
  /** 初始方向筛选：匝道只列单行道路。 */
  initialDirection?: DirectionFilter;
  strings: Strings;
  onPick: (name: string) => void;
}

/** 道路选择表：最近使用的排最前，其余按名称；可按街道/公路、单行/双向筛选，默认隐藏桥梁等特殊道路。 */
export const RoadPicker = ({ roads, selected, defaultText, initialDirection = "all", strings, onPick }: RoadPickerProps) => {
  const [typeFilter, setTypeFilter] = useState<TypeFilter>("all");
  const [directionFilter, setDirectionFilter] = useState<DirectionFilter>(initialDirection);
  const [showSpecial, setShowSpecial] = useState(false);

  const rows = useMemo(() => {
    return roads
      .filter((road) => showSpecial || road.o)
      .filter((road) => typeFilter === "all" || road.h === (typeFilter === "highway"))
      .filter((road) => directionFilter === "all" || isOneWay(road) === (directionFilter === "oneWay"))
      .sort((a, b) => {
        // 最近使用（r >= 0）按排名在前，其余按名称
        const ra = a.r < 0 ? Number.MAX_SAFE_INTEGER : a.r;
        const rb = b.r < 0 ? Number.MAX_SAFE_INTEGER : b.r;
        return ra !== rb ? ra - rb : a.n.localeCompare(b.n);
      });
  }, [roads, typeFilter, directionFilter, showSpecial]);

  const filterButton = <T extends string>(current: T, value: T, text: string, set: (v: T) => void) => (
    <Button
      variant="flat"
      selected={current === value}
      className={current === value ? styles.choiceSelected : styles.choice}
      onSelect={() => set(value)}
    >
      {text}
    </Button>
  );

  return (
    <div className={styles.picker}>
      <div className={styles.row}>
        {filterButton(typeFilter, "all", strings.all, setTypeFilter)}
        {filterButton(typeFilter, "street", strings.street, setTypeFilter)}
        {filterButton(typeFilter, "highway", strings.highway, setTypeFilter)}
        <div className={styles.spacer} />
        {filterButton(directionFilter, "all", strings.all, setDirectionFilter)}
        {filterButton(directionFilter, "oneWay", strings.oneWay, setDirectionFilter)}
        {filterButton(directionFilter, "twoWay", strings.twoWay, setDirectionFilter)}
      </div>
      <div className={styles.row}>
        <Button
          variant="flat"
          selected={showSpecial}
          className={showSpecial ? styles.choiceSelected : styles.choice}
          onSelect={() => setShowSpecial(!showSpecial)}
        >
          {strings.showSpecial}
        </Button>
      </div>
      <div className={styles.tableHeader}>
        <div className={styles.colRecent} />
        <div className={styles.colIcon} />
        <div className={styles.colName}>{strings.name}</div>
        <div className={styles.colType}>{strings.type}</div>
        <div className={styles.colLanes}>{strings.lanes}</div>
        <div className={styles.colWidth}>{strings.width}</div>
      </div>
      <Scrollable className={styles.tableBody} vertical>
        <div className={selected === "" ? styles.tableRowSelected : styles.tableRow} onClick={() => onPick("")}>
          <div className={styles.colRecent} />
          <div className={styles.colIcon} />
          <div className={styles.colName}>{defaultText}</div>
        </div>
        {rows.map((road) => (
          <div
            key={road.n}
            className={road.n === selected ? styles.tableRowSelected : styles.tableRow}
            onClick={() => onPick(road.n)}
          >
            <div className={styles.colRecent}>{road.r >= 0 ? "★" : ""}</div>
            <div className={styles.colIcon}>{road.i && <img className={styles.roadIcon} src={road.i} />}</div>
            <div className={styles.colName}>{road.n}</div>
            {/* 每格拼成一个字符串：游戏 UI 把相邻的多个文字片段分成多行显示 */}
            <div className={styles.colType}>
              {`${road.h ? strings.highway : strings.street} · ${isOneWay(road) ? strings.oneWay : strings.twoWay}`}
            </div>
            <div className={styles.colLanes}>
              {`${isOneWay(road) ? lanesPerDirection(road) : `${road.f}+${road.b}`}${road.p > 0 ? ` +${road.p} ${strings.bus}` : ""}`}
            </div>
            <div className={styles.colWidth}>{`${Math.round(road.w)} m`}</div>
          </div>
        ))}
      </Scrollable>
    </div>
  );
};
