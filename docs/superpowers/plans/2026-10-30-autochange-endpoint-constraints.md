# autochange 端点约束输入实施计划

> **For agentic workers:** Use superpowers:executing-plans for this workspace. Steps use checkbox syntax for tracking. The user explicitly requested continuing development; do not stop to ask for an execution method. Do not commit without separate user authorization.

**Goal:** 构造不依赖游戏的四臂节点输入快照与语义验证，为苜蓿叶求解器提供安全、可测试的输入。

**Architecture:** 四条臂各包含一个双向端点或两条方向相反的单行端点；端点保存实体索引与版本、世界位置和外指切线以及真实断面。独立验证器返回可机器识别的错误，允许上层分别处理不完整选择、方向错误和车道不兼容。

**Tech Stack:** C# 9、netstandard2.0、xUnit、现有 `Vec3`。

**Spec:** `docs/superpowers/specs/2026-10-29-autochange-cloverleaf-design.md`。

## Global Constraints

- 开发分支 `autochange`，不覆盖已有未提交修改；不提交、发布或部署。
- 本阶段不将旧模板简单变形冒充新求解器，不改旧自动/手动模式。
- 单向道路的 `TravelOutward` 以立交向外为正，双向道路分别提供进出两种动作。
- 车道兼容检查分别比较 0→2 和 1→3 的对应入口/出口；每臂保留原始道路断面。
- 保留有限非零的水平切线和有限的世界高度；不能因为其他数据合法而忽略 NaN、Infinity 或重复实体。

## Review Focus

1. 实体复用/实体版本不同：重复同一个索引与版本必须拒绝；不同版本需要作为不同身份，但游戏集成必须再次检查实体有效性。
2. 混合双向/双幅：一个双向端点可以匹配对向一对单向端点，两个方向车道均需匹配。
3. 双幅方向颠倒或同向：两个输入同进或同出必须拒绝，不能按空间位置猜交通方向。
4. 道路断面无效或坐标非有限：拒绝零宽、零车道、空预制体、NaN/Infinity 和水平零切线。
5. 端点与分组快照：创建后修改传入集合不能改变已验证输入，避免后台工作读到被修改的数据。

## Task 1: 定义不可变端点快照与四臂模型

**Files:** Create `src/InterchangeMod.Generation/Endpoints/EndpointConstraints.cs`; test `tests/InterchangeMod.Generation.Tests/EndpointConstraintsTests.cs`。

**Interfaces:** `SelectedEndpoint(int nodeIndex, int nodeVersion, Vec3 position, Vec3 outwardTangent, bool oneWay, bool travelOutward, float width, int lanes, string prefab)`；`EndpointArm(int index, IEnumerable<SelectedEndpoint> endpoints)`，只读属性对应构造参数且臂复制输入集合。

- [x] Write `ArmCopiesSourceCollection`：用一条端点创建臂后清空原集合，断言臂仍有原来的端点。
- [x] Run `dotnet test tests/InterchangeMod.Generation.Tests --filter FullyQualifiedName~EndpointConstraintsTests --no-restore`；确认因端点模型尚不存在而失败。
- [x] Implement immutable `SelectedEndpoint` and `EndpointArm` with copied read-only endpoint collection; rerun until test passes.

## Task 2: 输入形态、实体与数值验证

**Files:** Modify `EndpointConstraints.cs`; extend `EndpointConstraintsTests.cs`。

**Interfaces:** `EndpointProblemCode` covers `ArmCount`, `ArmIndex`, `InvalidPortCount`, `DuplicateNode`, `InvalidGeometry`, `InvalidRoad`, `InvalidDirection`, `LaneMismatch`; `EndpointProblem` has `Code` and `ArmIndex`; `EndpointConstraints.Validate(IReadOnlyList<EndpointArm>)` returns `IReadOnlyList<EndpointProblem>`.

- [x] Test valid 4 two-way arms, 4 dual arms, and a two-way/dual mixture using literal positions/directions; assert no problems.
- [x] Test duplicate node IDs, incorrect or duplicated arm indices, no/three ports, a single one-way port, and dual pairs with two inward or two outward ports. Assert the corresponding code.
- [x] Test NaN/Infinity in position/tangent, zero horizontal tangent, width ≤0, lanes ≤0, and empty prefab. Assert geometry or road error.
- [x] Run the focused tests; confirm they fail because validation is missing; implement validation and rerun.

## Task 3: 贯通车道数匹配

**Files:** Modify `EndpointConstraints.cs`; extend `EndpointConstraintsTests.cs`。

- [x] Add tests for 0→2 and 1→3 lane counts where two-way and two one-way ports are mixed. Swap only one corresponding directional count and assert `LaneMismatch`; ensure same-arm asymmetric flows are compared per travel direction rather than by selection order.
- [x] Run focused tests; confirm the new mismatch test fails; implement direction-specific matching for valid arms only; rerun focused tests.

## Task 4: 验证与衔接记录

- [x] Run all generation tests with `DOTNET_ROLL_FORWARD=Major dotnet test tests/InterchangeMod.Generation.Tests/InterchangeMod.Generation.Tests.csproj --no-restore --logger 'console;verbosity=minimal'`: **1175 passed, 0 failed, 0 skipped**, result `.artifacts/autochange/test-results/autochange-endpoints.trx`.
- [x] Record the implemented input validation and outstanding game node reading, cloverleaf solving, collision validation, UI and game testing below.
- [x] Continue with the next focused plan `2026-10-30-autochange-cloverleaf-topology.md`; this task alone does not deliver a usable interchange tool.

Current status: pure endpoint snapshot/validation exists with 15 focused tests. It is not connected to the game or UI. Asset-based geometries are measured separately in `docs/14-autochange袖珍资产几何参考.md`; no asset measurements have been promoted to hard design limits.
