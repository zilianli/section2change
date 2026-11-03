# autochange 苜蓿叶平面候选实施计划

> **For agentic workers:** Use superpowers:executing-plans. Implement the user-requested work without intermediate approval gates. Do not commit, publish or deploy.

**Goal:** 利用四臂端点构造真实的八条转向路径与四条贯通线路的可检查平面候选，为后续非对称求解和纵断面构造提供实体几何。

**Architecture:** 从每个入端和对向出端建立有向道路轴线，根据拓扑使用现有一般圆弧几何计算每条直接匝道/270°环道的切点；将接点按行驶顺序分配到四条轴线。该阶段不调用现有苜蓿叶模板、缩放或扭曲模板结果。只生成候选，不称为安全布局。

**Tech Stack:** C# 9、netstandard2.0、xUnit、`EndpointConstraints`、`CloverleafTopology`、`RampPath` 与 `Bezier3`。

**Spec:** `docs/superpowers/specs/2026-10-29-autochange-cloverleaf-design.md`。

## Global Constraints

- 本阶段只覆盖可精确构成直线贯通方向的双幅四臂；双向、混合、弯曲或不共线输入返回明确的暂不支持，而不是伪造成功。后续阶段必须扩展。
- 固定半径仅表示一个候选；不能声称已完成按空间自动搜索或紧凑优化。
- 平面 Y=0，不能把此结果用于游戏放置；净空、桥墩、纵坡、碰撞及端口原路改造都仍须单独求解。
- 一条候选匝道由不超过 90°、且目标长度不超过 70 m 的 Bézier 分片表示；端点用共同节点精确保存。
- 对每一幅按行驶方向确保接点顺序为直连分流、环道汇入、环道分流、直连汇入，相邻接点与外部端点至少 10 m；无可行候选时返回具体失败。

## Review Focus

1. 同一端点只能来自匹配的单行方向，LHT 与 RHT 镜像需要真实验证源/目标节点。
2. 非正交或曲线输入不能静默归到近似直线并丢弃切线；暂不支持时明确返回。
3. 匝道切点超出有限臂长、两接点顺序错乱或节点过近时不可报告候选成功。
4. 极小/非有限半径及近乎平行的轴线必须失败，不产生 NaN。
5. 任何候选都不能绕开未来必需的 3D、路面宽度和桥墩碰撞检查。

## Task 1: 双幅四臂的独立平面几何

**Files:** Create `src/InterchangeMod.Generation/Endpoints/CloverleafPlanarCandidate.cs`; test `tests/InterchangeMod.Generation.Tests/CloverleafPlanarCandidateTests.cs`。

**Interfaces:** `PlanarCandidateFailure { None, InvalidSelection, UnsupportedRoadForm, NonStraightApproach, InvalidRadius, DegenerateCrossing, AttachmentsOutsideApproach, ConflictingAttachmentOrder }`; `PlanarRamp` exposes source arm, target arm, `CloverEdgeKind`, start/end distance, sweep radians and read-only Bézier curves; `PlanarCloverleafCandidate.Create(IReadOnlyList<EndpointArm>, bool rightHandTraffic, float loopRadius, float directRadius)` returns result with failure and read-only ramps.

- [x] Write a red test with 丁香花 measured arm positions ±252 m/±12 m and synthetic one-way directions/road data. With RHT loop radius 30 m and direct radius 100 m, independently assert eight paths and arm 0 direct start `(-12,0,112)` to `(-112,0,12)`, loop start `(-12,0,-42)` to `(-42,0,-12)`, with 90° and 270° sweeps. Endpoints/indices from the supplied asset do not establish the asset's traffic directions.
- [x] Test RHT/LHT mirror, short 80 m arms, two-way inputs, a non-collinear opposite endpoint, invalid radius and parallel axes. Require failure codes for unsupported/infeasible input, not empty success.
- [x] Run `dotnet test tests/InterchangeMod.Generation.Tests/InterchangeMod.Generation.Tests.csproj --no-restore --filter FullyQualifiedName~CloverleafPlanarCandidateTests`; verify failure due absent API.
- [x] Implement the candidate from `RampPath.Turn`, directional arms and graph, checking finite radii, turn orientation, endpoint tangent alignment and attachment ordering. Emit piecewise 2D curves as data; no placement.
- [x] Rerun focused tests and full generation suite: **1196 passed, 0 failed, 0 skipped** at this stage, `.artifacts/autochange/test-results/autochange-planar.trx`. Game UI, 3D solving, selective collision checks and all road forms remain incomplete.
