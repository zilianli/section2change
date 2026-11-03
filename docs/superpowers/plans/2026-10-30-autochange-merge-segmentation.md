# autochange 汇入重叠区安全分段计划

> **For agentic workers:** Use superpowers:executing-plans. Continue directly; no automatic commits, publication or deployment.

**Goal:** 使用真实道路宽度、匝道宽度和侧向间距计算圆弧汇入的重叠长度，在重叠区外安排道路分段与水平段，避免复现游戏自动加节点引发的叠放。

**Architecture:** 对当前单圆弧平面候选，利用 `r*(1-cos(s/r))` 计算离起始/终止主线达到宽度之和加侧向间距所需弧长，再加节点安全余量。首末分段以这些位置为边界，中间按长度及转角继续分段；三维纵断面沿用各匝道的实际水平段。其余路段相交、地形、净空和桥墩仍需独立检查。

**Tech Stack:** C# 9、netstandard2.0、xUnit、`PlanarCloverleafCandidate`、`SpatialCloverleafCandidate`。

**Spec:** `docs/superpowers/specs/2026-10-29-autochange-cloverleaf-design.md`。

## Global Constraints

- 道路宽度来自对应输入端点，匝道宽度和侧向间距由调用者提供，不能从用户资产曲率估计道路宽度。
- 不提供匝道宽度时仍可生成仅供实验的未规划平面候选，不能据此放置；提供宽度时必须处理每个端点。
- 首末重叠区内不能出现独立曲线断点；首末段长度不超过 70 m，全部段长不超过 70 m、转角不超过 90°，并保留 10 m 最短段。
- 重叠区内放不下、净距超出圆弧或首末段超过 70 m 时返回类型化失败，不自动忽略或隐藏碰撞。
- 只保证接入对应主线的首末重叠区安排，不代表整个立交没有其他道路/桥墩碰撞；候选仍不可放置。

## Review Focus

1. 100 m 半径、道路宽 12 m、匝道宽 8 m、间距 4 m 的直接匝道：未调整分段的首个节点约 52.36 m，实际达到 14 m 净距需约 53.55 m，应检测并避开。
2. 源和目标道路宽度不相同时，两端要求必须分别计算，不能错误使用同一个值。
3. 270° 环道的外端距达到阈值后可能再次靠近道路；本阶段不声称全程无碰撞。
4. 平段边界和已有长度分段之间的距离可能不足 10 m；必须重新构造分段，不留下短段。
5. 坡度上限通过整段三维曲线验证，30%不能因延长平段被忽略。

## Task 1: 基于宽度安排接入重叠区的分段

**Files:** Modify `src/InterchangeMod.Generation/Endpoints/CloverleafPlanarCandidate.cs` and `CloverleafSpatialCandidate.cs`; test `tests/InterchangeMod.Generation.Tests/CloverleafPlanarCandidateTests.cs`, `CloverleafSpatialCandidateTests.cs`。

**Interfaces:** Extend `PlanarCloverleafCandidate.Create(..., IReadOnlyList<float>? retreatByArm = null, float? rampWidth = null, float sideGap = 4f, float minimumFlatLength = 0)`; `PlanarRamp` exposes nullable `RequiredFlatStart`, `RequiredFlatEnd`; `PlanarCandidateFailure` adds `InvalidRoadWidth`, `MergeCannotClear`. Default `rampWidth=null` is diagnostic only. A width-planned `SpatialCloverleafCandidate` uses the per-ramp required flats. If the later caller asks for a larger minimum than was planned, return `MergeBoundaryConflict` and require replanning with the desired minimum; don't insert a new short piece next to the existing flat boundary.

- [ ] Write tests with asset positions and synthetic 12 m roads: loop radius 30 m, direct radius 120 m, ramp width 8 m and gap 4 m. Assert source/target flats exceed theoretical separation length, no intermediate endpoint lies inside initial/final overlap, all pieces 10–70 m and 90° or less; the legacy 100 m radius should place its first planned split after 53.55 m, not at 52.36 m.
- [ ] Test a much wider ramp/road pair that cannot clear before 70 m, and invalid gap/width; assert typed failures with empty candidates.
- [ ] Test width-planned spatial candidate rises from 10 to 20 m within 30% using per-ramp flats, preserving exact common nodes; if requested grade is too low return a typed failure rather than partial output.
- [ ] Run focused tests, verify failure for absent API, implement overlap-aware segmentation and spatial integration, then rerun.
- [ ] Run the full generation suite and record results; explicitly preserve game, terrain, road/bridge conflict, bridge pillar, and traffic verification as unfinished.
