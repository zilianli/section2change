# autochange 平顺纵断面实施计划

> **For agentic workers:** Use superpowers:executing-plans. Continue the requested development; no automatic commits or deployment.

**Goal:** 将连续平面 Bézier 曲线提升为端点高度和纵坡可控的平顺三维曲线，严格限制最大坡度，为后续立交纵断面求解提供基础。

**Architecture:** 以平面分段的累计近似水平长度为参数，使用带首末坡度的全局三次 Hermite 曲线计算每个分段的高度及纵坡；在共用节点处转换成三维 Bézier 控制点，保持纵坡连续。对每段的二次导数控制多边形自适应细分，保守界定水平速度下界和竖向速度上界；超过或无法证明坡度限制时拒绝。

**Tech Stack:** C# 9、netstandard2.0、xUnit、`Bezier3`、`Vec3`。

**Spec:** `docs/superpowers/specs/2026-10-29-autochange-cloverleaf-design.md`。

## Global Constraints

- 坡度按 `|dy| / horizontalDistance`，最大可设为 0.30；30°不是本计划的单位。
- 返回数据仅是几何工具结果，不能代表道路碰撞、净空、桥墩或游戏放置已通过。
- 端点水平切线退化、不连续的平面输入返回明确错误；不能用采样替代整段可证明的坡度上限。
- 总长度为平面曲线的采样近似值；这个近似仅用于分配高度，坡度验证直接检查每段导数，不依赖长度近似准确。

## Review Focus

1. 30%是上限，超过上限和设置 30° 数值等价的 0.577 必须拒绝。
2. 两段共享接点的高度和实际纵坡连续，不仅是同一个顶点。
3. 起点和终点自带的纵坡可以保留，不强制全部道路端点水平。
4. 中间局部水平速度趋零时不可以用平均坡度放行。
5. 斜率检查无法在有限细分内证明通过时返回 `Uncertain`，不是成功。

## Task 1: 三维平顺纵断面及保守坡度验证

**Files:** Create `src/InterchangeMod.Generation/Endpoints/VerticalProfile.cs`; test `tests/InterchangeMod.Generation.Tests/VerticalProfileTests.cs`。

**Interfaces:** `VerticalProfileFailure { None, InvalidInput, InvalidGeometry, GradeExceeded, Uncertain }`; `VerticalProfileResult` exposes `Failure` and immutable `IReadOnlyList<Bezier3> Curves`; `VerticalProfile.Apply(IReadOnlyList<Bezier3> planar, float startHeight, float endHeight, float startGrade, float endGrade, float maxGrade)` returns a result.

- [ ] Add a test for 50 m straight horizontal distance, 10 m rise, zero end slopes and 30% limit: success, endpoint grades zero and mid-grade exactly 30%.
- [ ] Add failure tests for the same 10 m rise in 40 m, a 57.7% limit, a zero-horizontal tangent and discontinuous planar pieces.
- [ ] Add a test with two straight 25 m pieces: the shared node is at height 5 m and the vertical slopes before/after it both equal 30%; test descending grade and nonzero endpoint slopes.
- [ ] Run `dotnet test tests/InterchangeMod.Generation.Tests/InterchangeMod.Generation.Tests.csproj --no-restore --filter FullyQualifiedName~VerticalProfileTests`; verify it fails for missing API.
- [ ] Implement the profile and conservative adaptive grade check with bounded depth. Return `Uncertain` when the derivative convex-hull bound cannot be separated from zero or tightened sufficiently; never report such a case as success.
- [ ] Rerun focused tests and the complete generation suite. Record limitations including terrain, actual roads, collision, bridge supports and UI.
