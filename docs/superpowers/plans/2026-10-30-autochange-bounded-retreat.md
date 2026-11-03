# autochange 有界路头回退候选实施计划

> **For agentic workers:** Use superpowers:executing-plans. Continue the user-requested development; no automatic commits, publication or deployment.

**Goal:** 让平面候选按四臂各自指定的回退距离重新确定外接位置，从而计算多大改造范围能放下候选。

**Architecture:** 在只读端点快照中沿每个端点的外指切线平移指定距离，参与候选构造；原选点不变。输出记录四臂请求值，后续游戏适配必须沿真实原路线追踪、验证可用长度及所有权，不能将直线外推当作已经存在的道路。

**Tech Stack:** C# 9、netstandard2.0、xUnit，既有 `PlanarCloverleafCandidate`。

**Spec:** `docs/superpowers/specs/2026-10-29-autochange-cloverleaf-design.md`。

## Global Constraints

- 此阶段是指定距离的单个平面候选，不是自动求解、拆路计划或游戏放置。
- 四臂距离独立，默认零；拒绝数量不等于 4、负数及非有限值。
- 游戏中要求的真实道路可回退长度、碰撞和道路所有权尚未验证，候选的 `IsPlacementReady` 永远为 false。

## Review Focus

1. 输入道路快照与调用者提供的距离列表不能被修改或被结果持有为可变引用。
2. 高差不同或弯曲的旧道路不能被本阶段的二维切线外推误认为已验证可重建。
3. 回退是沿道路外指方向而不是沿行驶方向，进出口两幅在同一臂均向外移动。
4. 单独某一臂与四臂一起调整时，计算必须使用各自的距离，而不是平均。
5. 不合理距离必须返回类型化错误而不是产生数值无穷或不受约束地向外搜索。

## Task 1: 指定每臂回退距离

**Files:** Modify `src/InterchangeMod.Generation/Endpoints/CloverleafPlanarCandidate.cs`; extend `tests/InterchangeMod.Generation.Tests/CloverleafPlanarCandidateTests.cs`。

**Interfaces:** add `InvalidRetreat` to `PlanarCandidateFailure`; add optional `IReadOnlyList<float>? retreatByArm = null` to `Create`; add immutable `IReadOnlyList<float> RetreatByArm` to `PlanarCandidateResult` on success.

- [x] Test the 80 m synthetic four-arm scene fails with zero retreat but produces a planar candidate with specified retreats; verify arm 0 source and opposite target while input coordinates remain unchanged.
- [x] Test different 200/220/240/260 m arm distances appear at their own line ends and in a copied result after the original array changes.
- [x] Test negative, NaN, Infinity and wrong-sized lists return `InvalidRetreat`, empty ramps, and not placement-ready.
- [x] Run `dotnet test tests/InterchangeMod.Generation.Tests/InterchangeMod.Generation.Tests.csproj --no-restore --filter FullyQualifiedName~CloverleafPlanarCandidateTests`; verify failure caused by missing overload; implement and rerun.
- [x] Full generation suite: **1198 passed, 0 failed, 0 skipped**, `.artifacts/autochange/test-results/autochange-retreat.trx`. Actual old-road geometry, colliders and deletion remain unimplemented.
