# autochange 开放端点平面参考框架实施计划

> **For agentic workers:** Use superpowers:executing-plans. Continue development as requested; no automatic commits or deployment.

**Goal:** 根据四个方向的实际端点和切线，提供与旧连通路口匹配器无关、能够处理不对称道路的平面参考中心和每臂测量。

**Architecture:** 每臂取实际端点中心及外指切线的水平单位方向；最小化四条无限直线的垂距平方，解对称 2×2 方程。返回中心、残差和有符号沿臂距离，保留每个原端点高度给后续三维求解，不拟合旧模式逐臂坡面。平面框架只提供搜索初值，不能当作道路已连接或空间已满足。

**Tech Stack:** C# 9、netstandard2.0、xUnit、`EndpointArm` / `EndpointConstraints` / `Vec3`。

**Spec:** `docs/superpowers/specs/2026-10-29-autochange-cloverleaf-design.md`。

## Global Constraints

- 端点输入必须先通过 `EndpointConstraints.Validate`，无效选择不得产生中心。
- 对称或斜交路口都使用实际坐标和切线；不要求对向道路完美共线，不把高差投影掉或偷偷改变端点高度。
- 当四臂方向使 2×2 系统奇异或近奇异，返回 `ParallelAxes`，不产生 NaN/无穷或声称求解成功。
- `.cok` 用户资产数据可用于测试位置，交通方向和道路断面未解析时必须标作合成测试数据。

## Review Focus

1. 四臂顺序打乱后中心和每臂度量按 `EndpointArm.Index` 保持一致。
2. 大世界坐标时计算使用 double 中间值，避免 float 乘积溢出或坐标误差扩大。
3. 不共线的对向臂使用全部四条线，而不是只取第一条的交点。
4. 退化的近平行输入只返回明确失败，不能输出随机极大中心。
5. 不合法输入先通过现有校验；无效实体、方向或车道不一致不得隐式进入搜索。

## Task 1: 估算端点平面参考框架

**Files:** Create `src/InterchangeMod.Generation/Endpoints/EndpointFrame.cs`; test `tests/InterchangeMod.Generation.Tests/EndpointFrameTests.cs`。

**Interfaces:** `EndpointFrameFailure { None, InvalidSelection, ParallelAxes }`; `EndpointFrameResult` exposes `Failure`, `Vec3? Center`, `float ResidualRms`, `IReadOnlyList<float> AlongByArm`; `EndpointFrame.Estimate(IReadOnlyList<EndpointArm> arms)` returns an `EndpointFrameResult`。

- [x] Test the eight measured 丁香花 endpoint positions `(±12,10,±252)` and `(±252,10,±12)` with explicitly synthetic directions and 2-lane road metadata. Assert a center near `(0,0,0)`, residual near 0 and along distance 252 m for each arm.
- [x] Test four offset axis-aligned roads whose center lines are `x=0`, `x=10`, `z=0`, `z=20`: the best fit is `(5,0,10)` with RMS `sqrt(62.5)`; verify output is invariant when arms are reordered.
- [x] Test parallel/near-parallel tangents return `ParallelAxes` and missing arms return `InvalidSelection`; add a large-world offset case and preserve horizontal translation to within 0.1 m. Incompatible lanes are rejected by `EndpointConstraintsTests`.
- [x] Run `dotnet test tests/InterchangeMod.Generation.Tests/InterchangeMod.Generation.Tests.csproj --no-restore --filter FullyQualifiedName~EndpointFrameTests`; verify failure for missing API, implement the constrained least-squares fit with double intermediates, then rerun to green.
- [x] Run the complete generation suite: **1185 passed, 0 failed, 0 skipped**, `.artifacts/autochange/test-results/autochange-frame.trx`. Continue to true geometry generation and game input; this framework does not provide a preview or a solvable layout.
