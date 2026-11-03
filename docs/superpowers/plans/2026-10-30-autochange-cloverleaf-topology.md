# autochange 苜蓿叶有向拓扑实施计划

> **For agentic workers:** Use superpowers:executing-plans. The user requested continued development; implement directly. Do not commit without separate authorization.

**Goal:** 建立可独立检查的完整苜蓿叶交通拓扑，明确每个方向的直行、直接转向和环道转向，供后续三维布局求解使用。

**Architecture:** 四臂按顺时针编号 0–3，每条直行幅从 arm i 的入口到 arm (i+2)%4 的出口；入口依次经过直接匝道分流、环道汇入、环道分流、直接匝道汇入。每条匝道都连接对应的有向节点，四个直接匝道与四个环道各自独立。此数据结构只描述行驶关系，不生成道路几何。

**Tech Stack:** C# 9、netstandard2.0、xUnit。

**Spec:** `docs/superpowers/specs/2026-10-29-autochange-cloverleaf-design.md`。

## Global Constraints

- 不将此拓扑视为已接入游戏或已证明车辆可通行；真实网络由后续步骤生成并测试。
- 镜像左右侧通行：RHT 直接匝道为右转、环道为左转；LHT 相反。
- 原 1.0.8.1 未提交修复保持不变；不提交、发布或部署。
- 路径测试只能说明对应抽象有向图的可达性；不推断实际车道连接或禁止所有绕行。

## Review Focus

1. 直行和左右转每个方向都要到达正确出口，而不是仅检查边数量。
2. LHT 镜像必须改变直接匝道和环道的目标方向，保持道路连续。
3. 任一特定环道缺失必须产生目标不可达，证明测试不只检查固定计数。
4. 允许至多一条匝道的直接行驶不能回到本臂；多匝道绕行不属于此结论。
5. 节点和边集合快照不可由外部追加或移除。

## Task 1: 生成有向的完整苜蓿叶

**Files:** Create `src/InterchangeMod.Generation/Endpoints/CloverleafTopology.cs`; test `tests/InterchangeMod.Generation.Tests/CloverleafTopologyTests.cs`。

**Interfaces:** `CloverNodeKind { In, DirectExit, LoopMerge, LoopExit, DirectMerge, Out }`; `CloverEdgeKind { Main, DirectRamp, LoopRamp }`; `CloverNode(int arm, CloverNodeKind kind)` with value equality; `CloverEdge(CloverNode from, CloverNode to, CloverEdgeKind kind)`; `CloverleafTopology.Create(bool rightHandTraffic)` returns read-only node/edge collections.

- [x] Write test `EachApproachHasThreeCorrectDestinations`: independently walk the directed graph with at most one ramp and assert literal targets for all four arms in RHT and LHT, plus no direct U-turn.
- [x] Run `dotnet test tests/InterchangeMod.Generation.Tests/InterchangeMod.Generation.Tests.csproj --no-restore --filter FullyQualifiedName~CloverleafTopologyTests`; verify it fails because the topology API is absent.
- [x] Implement `Create` with 24 nodes, 20 main edges, four direct ramps and four loop ramps. For RHT direct exit i connects to `DirectMerge` of (i+1)%4 and loop exit i to `LoopMerge` of (i+3)%4; LHT swaps those offsets. Main direction i ends at `Out` of (i+2)%4.
- [x] Rerun focused tests; they must pass.

## Task 2: 结构完整性及不可变集合

**Files:** Modify `CloverleafTopology.cs`; extend `CloverleafTopologyTests.cs`。

**Interfaces:** `CloverleafTopology.Validate(IReadOnlyList<CloverEdge> edges)` returns `IReadOnlyList<string>`; results identify missing/unreachable or invalid connections without relying on game entities.

- [x] Add tests removing one loop, adding a disconnected/invalid arm node, and attempting collection mutation; missing ramp must eliminate exactly its intended one-ramp destination and validation must report failure.
- [x] Run tests and confirm the new tests fail; implement validity checks (canonical node IDs, exact kinds, four per ramp kind, directed connections and reachable movements), then rerun.
- [x] Run all generation tests: **1181 passed, 0 failed, 0 skipped**, `.artifacts/autochange/test-results/autochange-topology.trx`. Geometric layout, game selection, actual road and vehicle checks remain outstanding.
