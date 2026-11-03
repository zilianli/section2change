# autochange 汇入水平段纵断面实施计划

> **For agentic workers:** Use superpowers:executing-plans. Continue directly; do not commit or deploy.

**Goal:** 给匝道两端留可指定的同高接入区，再对中段生成平顺、满足坡度上限的纵断面。

**Architecture:** 对已连接的平面 Bézier 路径按近似弧长在两个指定距离处精确切片；两端分别保持恒定高度，中段调用 `VerticalProfile.Apply`，再拼回连续曲线。路径自身不判断水平段是否足以达到道路净距，实际检查属于后续求解阶段。

**Tech Stack:** C# 9、netstandard2.0、xUnit、`Bezier3.Slice`、`VerticalProfile`。

**Spec:** `docs/superpowers/specs/2026-10-29-autochange-cloverleaf-design.md`。

## Global Constraints

- 两端水平段高度与接入道路高度一致；实际道路/匝道间净距和桥墩检查仍未实现。
- 水平长度按平面曲线的采样近似；切割结果必须满足高度和坡度连续性。
- 输入总路径先检查曲线连接和退化问题；不能只检查切割后各组而漏掉组间断点。
- 实际坡度仍由 `VerticalProfile.Apply` 的保守导数界检查。

## Review Focus

1. 前后水平段相加大于总长时必须明确拒绝，不产生倒序曲线。
2. 跨越多个 Bézier 段时切点及节点顺序正确，首尾高度完全保留。
3. 平段很短、恰好位于原有节点或为零时不生成退化碎片。
4. 中段不足以完成升降时保留 `GradeExceeded`，不能借水平段忽略实际坡度。
5. 原始平面曲线不可变，调用者不能修改返回集合。

## Task 1: 可控平段与中间爬坡

**Files:** Modify `src/InterchangeMod.Generation/Endpoints/VerticalProfile.cs`; extend `tests/InterchangeMod.Generation.Tests/VerticalProfileTests.cs`。

**Interfaces:** add `InsufficientLength` to `VerticalProfileFailure`; add `VerticalProfile.ApplyWithFlatEnds(IReadOnlyList<Bezier3> planar, float startHeight, float endHeight, float flatStart, float flatEnd, float maxGrade)` returning `VerticalProfileResult`。

- [ ] Add test: line 0–100 m, height 0→10 m, flats 20 m each, max 30%; assert 3 pieces with level 0 and 10 m ends, zero join slopes, 60 m middle and midpoint slope 25%.
- [ ] Add test: 80 m with 30 m flats each and 10 m rise returns `GradeExceeded`; 60+60 m flats on 100 m return `InsufficientLength`; negative/NaN flats return `InvalidInput`.
- [ ] Add test with a planar path made of 3 continuous pieces where the flats lie on either side of existing nodes; assert all output joins share coordinates and actual slope, and source pieces remain unchanged.
- [ ] Run focused `VerticalProfileTests`, verify missing API failure, implement, rerun to green.
- [ ] Run the complete generation suite and document the exact test count; game validity remains outstanding.
