# Changelog

## 1.0.8-beta — 2026-10-28

- Fix water bridge previews incorrectly blocked by applying toolbar elevation offsets to the bridge-to-riverbed distance; preserve world geometry and actual terrain elevation.
- Fix long-arm turbine/stack replacements losing outer road candidates at a global 64-segment traversal cap. Fully traverse matched chains within the footprint and consistently replace graded approaches.
- Use conservative curve collision intervals for old-road filtering and cutting; validate matched-road identity, tangents and traffic direction when reconnecting, and block ambiguous candidates.
- Add water and endpoint diagnostics, cache failed previews, and guard game deployment against a running Cities2 process.
- Validation: 989 automated tests passed. The user confirmed water replacement succeeded and the long-arm replacement preview no longer reported errors. These confirmations apply to the tested scenarios; vehicle-flow verification was not separately reported.

## 1.0.7.1-beta — 2026-10-28

- Long-arm replacement hotfix: fully follow matched road chains within the footprint instead of stopping at 64 segments. Clear matched graded approaches consistently; log chain counts and endpoint rejection reasons for gameplay validation.

- Water preview hotfix: match the game's physical-course elevation limits instead of treating the toolbar's ±50 m offsets as a limit on bridge-to-riverbed distance. Preserve actual world heights and terrain elevation in NetCourse. Cache failed preview results until inputs change to stop repeated diagnostics.

- Replaced fixed old-road collision samples with conservative Bezier interval subdivision; filtering and cutting use the same collision intervals.
- Restrict reconnections to matched road chains and compatible outward tangents and traffic directions. Ambiguous candidates now prevent confirmation.
- Added initial water/shoreline diagnostic logging and numeric tests. Water surface clearance remains separate from actual terrain elevation. Automatic water-based placement is not implemented in this release; gameplay replacement validation remains pending.
- Builds stage files in `.artifacts/staging` by default. Explicit game deployment checks that Cities2 is closed before touching the installed mod.

## 1.0.7-beta — 2026-10-28

- Lane balance for stack left-exit (median-side lanes drop between the left-turn ramps) 定向左出左进车道平衡
- Lane balance on divided cross roads of cloverleaf 苜蓿叶被交道路车道平衡
- Trumpet branch: loop-only and semi-direct-only stretches use the reduced road 喇叭形支路车道平衡
- Lane balance for partial cloverleaf and roundabout mains 部分苜蓿叶、环岛主线车道平衡
- Ramps are edge-aligned where they join or leave the road 匝道接入处边缘对齐

## 1.0.5-beta — 2026-10-28

- Ramps keep at least 3 m (default 4 m) from the main road — fixes merge overlaps 匝道与主线间距至少 3 米，修复汇入段重叠
- Elevated diverges leave on an 80 m arc with a node where the ramp clears the main 高架分流用 80 米圆弧离开主线
- Diamond: two-way mains are split into carriageways inside the interchange 菱形双向主线在立交内拆成双幅
- Turbine / stack keep the full game clearance; level gaps of exactly 7.5 m are accepted 涡轮、定向净空修正
- Wye: steadier matching, legs reconnect to the original roads 修复 Y 型匹配跳动与臂端接不上
- Slopes: the interchange follows one reference grade fitted to the existing roads 坡地按统一坡度放置
- Usage guide reordered by importance; placement cannot be undone 使用指南重排，说明不能撤销

## 1.0.1-beta — 2026-10-27

- Single-point retrofit slopes down to the existing road; left-hand traffic fix for divided tails
- Retrofit removes other roads only where the new roads hit them
- First-run usage guide 首次使用指南

## 1.0.0-beta — 2026-10-27

- First beta: 13 interchange types, junction matching, curve following, quick button 首个测试版
