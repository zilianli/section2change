# Compact grid Implementation Plan

> **For agentic workers:** Use superpowers:subagent-driven-development for the independent core task; root integrates input/search/runtime/UI and final verification.

**Goal:** 完成独立井字八端点临时预览并交付游戏测试。

**Architecture:** EndpointCompactGridCore与EndpointGridArmRoads实现参数化20链核心，EndpointCompactGridSetup匹配真实车道断面与层差，EndpointCompactGridSearch执行有限四向退接，复用通用端口接路。

**Tech Stack:** C# Generation/xUnit、CS2 ECS、React/TypeScript、自绘SVG。

**Spec:** docs/superpowers/specs/2026-11-01-compact-grid-design.md。

- [x] 核心任务：20链/8端口/12动作与实际断面/支撑认证；复核后补交角、LHT标签、宽断面附加接路，67项核心测试通过。
- [x] 集成任务：独立CompactGrid、节点专用保护、真实断面/四层、有限搜索与UI接入；真实Setting源码往返、断面回退及混合道路覆盖通过。
- [x] 交付任务：针对性156项、完整1880项通过；独立复核、UI/Release构建通过；用户正常关闭游戏后备份部署，27个文件核验通过；docs/15、docs/35已更新。

Ruling: 本轮使用新参数化井字拓扑，资产仅用于拓扑/尺寸比较 — 保留实际Prefab与安全认证，符合既定方案B — 不能承诺任何断面都能达到参考资产尺寸。

Ruling: 井字作为节点专用独立类型，旧自动/手动不可回落到其他模板 — 旧代码的默认模板分支不认识新类型 — UI与后端均须保护。

Ruling: 不并行实现花形和环岛 — 新拓扑需独立的游戏验证闭环 — 后续两类仍保留排期。

Ruling: Setting类型选择改为规范枚举名与登记目录共同校验 — 人工白名单漏掉新类型会实际回退成菱形 — 用真实Setting源码的菜单往返回归保护，保留未知值回退行为。

Ruling: 不扩展碰撞豁免以绕过宽道路接路失败，改按完整道路与集散道半宽增加核心外端平坦段 — 4项完整接路和4项直接接路回归有RED/GREEN证据 — 较宽断面可能增加少量占地。
