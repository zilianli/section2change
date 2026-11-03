# autochange 苜蓿叶三维候选骨架实施计划

> **For agentic workers:** Use superpowers:executing-plans. Continue development directly; do not commit, publish or deploy.

**Goal:** 将已验证端点约束、平面候选和纵断面组合成可数值检查的四向苜蓿叶三维道路骨架，供后续净空、碰撞、桥墩和游戏适配使用。

**Architecture:** 对四条贯通幅在所有平面接点间生成有向线段，两端从所选原路位置/切线坡度平顺升降到指定主线或被交道路世界高度；每条匝道用指定平段长度及现有纵断面工具连接对应高度。所有接点复用同一组精确坐标，包含四条主线与八条匝道的有向曲线。该阶段不执行碰撞、净空、地形和桥墩检查，不开放游戏放置。

**Tech Stack:** C# 9、netstandard2.0、xUnit、`PlanarCloverleafCandidate`、`VerticalProfile`。

**Spec:** `docs/superpowers/specs/2026-10-29-autochange-cloverleaf-design.md`。

## Global Constraints

- 曲线 Y 是输入的世界高度，与原生成层 `RoadSegmentSpec` 的离地高度语义不同；禁止未转化就交给旧工具。
- 暂不自动决定层高、道路宽度、原路拆改或接入平段长度；调用者显式提供候选参数，任何候选均不可直接放置。
- 回退后的高度沿端点切线推算是临时几何假设，真实道路曲线和地形必须在游戏接入阶段重新检查。
- 最大纵坡不可超过 30%，断点高度和坡度应连续；对路径失败返回原因和空结果，不返回部分道路。

## Review Focus

1. 高度不同的四臂及非零端点纵坡不能被静默设为同高；对指定平层无法在允许坡度内接回时拒绝。
2. 匝道与主线精确共享节点坐标，分段终点与下段起点也精确相同。
3. 使用用户提供的丁香花端点位置构造几何时，车流方向、宽度和预制体仍属合成测试数据。
4. 全部四个入口到三个非原路出口的有向道路路径都需能沿同一条转向匝道到达；拓扑可达不证明游戏车道连通。
5. 所有曲线即使生成成功也必须显示 `IsPlacementReady == false`；碰撞、净空、桥墩检查不能被当作已完成。

## Task 1: 组合具有共享节点的三维候选

**Files:** Create `src/InterchangeMod.Generation/Endpoints/CloverleafSpatialCandidate.cs`; test `tests/InterchangeMod.Generation.Tests/CloverleafSpatialCandidateTests.cs`。

**Interfaces:** `SpatialCandidateFailure { None, InvalidSelection, StalePlanar, PlanarFailure, InvalidParameter, GradeExceeded, InvalidGeometry, Uncertain }`; `SpatialSegment` exposes `SourceArm`, `CloverEdgeKind Kind`, `Bezier3 WorldCurve`; `SpatialCandidateResult` exposes `Failure`, `IReadOnlyList<SpatialSegment> Segments`, `bool IsPlacementReady`; `SpatialCloverleafCandidate.Create(IReadOnlyList<EndpointArm> arms, PlanarCandidateResult planar, float mainHeight, float crossHeight, float rampFlatLength, float maxGrade)`. `PlanarCandidateResult` retains immutable input-arm snapshots so changed entity versions, positions or road metadata cause `StalePlanar` rather than silently generating stale geometry.

- [ ] Build a 3D candidate from the measured ±252/±12 endpoint positions with synthetic road/traffic data, heights 10 m and 20 m, flats 30 m, grade 30%. Assert every segment grade ≤30%, all four incoming and outgoing world coordinates preserved, all ramp endpoints are exact nodes on the appropriate mainline, and `IsPlacementReady` is false.
- [ ] Walk resulting directed segments for all four source arms and verify straight plus both turns reach intended exits using at most one named ramp chain; remove one loop in a copy and verify that movement is no longer reachable.
- [ ] Test 8% max grade on the same candidate returns `GradeExceeded` with no partial geometry; test nonzero external endpoint elevation/grade, stale entity/version snapshots, and invalid heights/flat lengths.
- [ ] Run `dotnet test tests/InterchangeMod.Generation.Tests/InterchangeMod.Generation.Tests.csproj --no-restore --filter FullyQualifiedName~CloverleafSpatialCandidateTests`; verify failure for missing API; implement and rerun.
- [ ] Run all generation tests and document remaining actual clearance, collisions, pillars, road forms, curved approaches, bounded search, ECS selection, UI and deployment work.
