# Changelog

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
