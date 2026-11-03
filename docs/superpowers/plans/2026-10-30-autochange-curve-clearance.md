# autochange 曲线间距检查基础计划

> **For agentic workers:** Use superpowers:executing-plans. Continue development directly; no commits, publication or deployment.

**Goal:** 对两条非共享节点的三维道路中心线保守检查水平宽度与竖向净空，为后续整座候选的碰撞校验提供可复用基础。

**Architecture:** 用 Bézier 控制点包围盒为两条曲线给出保守水平/竖向边界；无法排除碰撞时递归切分，并寻找实际采样见证。达到深度或工作量上限仍无法确认时返回 `Uncertain`，不得静默判为安全。此函数不对合法汇入区域自动豁免，调用者需先按节点和对应道路区段划分合法连接范围。

**Tech Stack:** C# 9、netstandard2.0、xUnit、`Bezier3`。

**Spec:** `docs/superpowers/specs/2026-10-29-autochange-cloverleaf-design.md`。

## Global Constraints

- 输入的 Y 是真实世界高度；净空必须由调用者根据预制体、地面/水面、结构厚度和实际规则提供，不在函数里假定所有道路净空相同。
- 半宽和净空必须有限且为正；参数非法时返回 `InvalidInput`。
- 不默认豁免有公共节点的曲线；暂不能把此工具的结果直接当作整座立交的放置授权。
- 有限精度需要给包围盒留数值余量；边界触碰和预算耗尽时优先返回 `Uncertain`。

## Review Focus

1. 水平投影交叉而高差足够时应通过；同高交叉应发现碰撞。
2. 交叉点不位于任何曲线起点、中点、终点时仍能发现，不依赖固定采样点。
3. 宽度和净空各有不同数量级时也要按半宽之和计算，不把车道中心线重合当作唯一条件。
4. 计算预算耗尽和浮点边界不确定时不能返回 `Clear`。
5. 交换两条输入曲线后结论一致，非法数值不进入递归。

## Task 1: 无节点豁免的保守两曲线判定

**Files:** Create `src/InterchangeMod.Generation/Endpoints/CurveClearance.cs`; test `tests/InterchangeMod.Generation.Tests/CurveClearanceTests.cs`。

**Interfaces:** `CurveClearanceResult { Clear, Conflict, Uncertain, InvalidInput }`; `CurveClearance.Check(Bezier3 a, float halfWidthA, Bezier3 b, float halfWidthB, float verticalClearance, int maxNodes = 50000)`.

- [x] Test same-height crossing returns Conflict, the same crossing raised by 8 m with 7.5 m clearance returns Clear, and parallel roads 5 m apart with 1 m half-width each return Clear.
- [x] Test two 200 m lines crossing at x=13.7 m away from the endpoints/midpoints; require Conflict under ordinary budget. A root-only budget must return Uncertain. Swap inputs and assert matching result.
- [x] Test NaN/infinite inputs, zero/negative widths, zero/negative clearance and invalid budget return InvalidInput.
- [x] Run focused tests and observe missing API failure; implement recursive bounds with an actual overlap witness, bounded depth/work and conservative floating margin; rerun.
- [x] Run generation suite and record totals; retain explicit warning that connected-node exemptions, full network pairing and pillar checks are not yet implemented.

2026-10-30：边界用例补充后定向测试 5 项通过，完整生成层测试 1228 项通过。独立静态审查未发现可确认的错误；不代表游戏内道路和桥墩已经验证。
