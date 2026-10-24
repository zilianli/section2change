import { Button } from "cs2/ui";
import styles from "./panel.module.scss";

interface StepperProps {
  label: string;
  value: number;
  step: number;
  min: number;
  max: number;
  unit?: string;
  onChange: (value: number) => void;
}

/** 数值调节：游戏 UI 库没有公开滑块，用 −/+ 按钮代替（C# 端也会限制范围）。 */
export const Stepper = ({ label, value, step, min, max, unit = "", onChange }: StepperProps) => {
  const change = (delta: number) => {
    const next = Math.min(max, Math.max(min, Math.round((value + delta) / step) * step));
    if (next !== value) onChange(next);
  };
  return (
    <div className={styles.row}>
      <div className={styles.label}>{label}</div>
      <Button variant="flat" className={styles.stepButton} onSelect={() => change(-step)} disabled={value <= min}>
        −
      </Button>
      {/* 必须拼成一个字符串：游戏 UI 把相邻的多个文字片段分成多行显示 */}
      <div className={styles.value}>{`${Number(value.toFixed(2))}${unit}`}</div>
      <Button variant="flat" className={styles.stepButton} onSelect={() => change(step)} disabled={value >= max}>
        +
      </Button>
    </div>
  );
};

/** 游戏网格一格 = 8 米；平行道路工具的间距范围是 0.25 ~ 12 格。 */
export const kCellSize = 8;

interface SpacingStepperProps {
  label: string;
  /** 当前值（米）。 */
  meters: number;
  minMeters: number;
  maxMeters: number;
  /** 按米调节时的步长；按格调节时固定 0.25 格。 */
  stepMeters: number;
  inCells: boolean;
  cellUnit: string;
  onChange: (meters: number) => void;
}

/** 间距调节：可按米或按游戏网格的格数显示和调节，存储始终是米。 */
export const SpacingStepper = ({ label, meters, minMeters, maxMeters, stepMeters, inCells, cellUnit, onChange }: SpacingStepperProps) =>
  inCells ? (
    <Stepper label={label} value={meters / kCellSize} step={0.25} min={Math.max(0.25, minMeters / kCellSize)} max={maxMeters / kCellSize}
      unit={cellUnit} onChange={(cells) => onChange(cells * kCellSize)} />
  ) : (
    <Stepper label={label} value={meters} step={stepMeters} min={minMeters} max={maxMeters} unit=" m" onChange={onChange} />
  );

interface ChoiceProps<T extends string | number> {
  label: string;
  value: T;
  options: { value: T; text: string }[];
  onChange: (value: T) => void;
}

/** 一组互斥按钮（如 自动/双向/双幅）。 */
export function Choice<T extends string | number>({ label, value, options, onChange }: ChoiceProps<T>) {
  return (
    <div className={styles.row}>
      <div className={styles.label}>{label}</div>
      <div className={styles.options}>
        {options.map((option) => (
          <Button
            key={option.value}
            variant="flat"
            selected={option.value === value}
            className={option.value === value ? styles.choiceSelected : styles.choice}
            onSelect={() => option.value !== value && onChange(option.value)}
          >
            {option.text}
          </Button>
        ))}
      </div>
    </div>
  );
}
