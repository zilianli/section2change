# Y型节点预览 Implementation Plan

> **For agentic workers:** Use superpowers:subagent-driven-development for the isolated core task; integrate the runtime and run final verification in this session.

**Goal:** 交付可部署测试的Y型六端点预览，并更新后续开发范围。

**Architecture:** 独立EndpointWyeCore输出通用六端口核心；三向搜索按类型选择核心，保留原分组；运行时使用Y型的基层、精确半车道匝道和专属提示。

**Tech Stack:** C# Generation/xUnit、CS2 ECS、React/TypeScript。

**Spec:** docs/superpowers/specs/2026-10-31-wye-node-preview-design.md。

- [x] 任务1：Y型核心适配及RED/GREEN测试；独立复核发现包络缺陷后，7项回归先失败再通过，核心46项通过。
- [x] 任务2：Y型分层/匝道选择、搜索接入、UI及边界测试；74项Y型直接集成通过，既有T型输入行为保留。
- [x] 任务3：针对性295项、完整1769项通过；UI/Release构建和独立复核通过；正常关闭游戏后备份部署，26个文件核验通过。游戏验证由用户完成。
- [x] 更新docs/15与docs/34：Y型开发在前，井字/梅花/环岛后续评估；菱形/单点/中式快速路不纳入新几何开发。

Ruling: 首轮Y型仅支持三臂相同偶数车道数和精确一半匝道 — 既有模板每条匝道共用断面参数，奇数/不等车道需要新的逐流向分配与变宽几何，不能假借已有几何宣称车道守恒 — 后续扩展需单独实现。

Ruling: 本轮不重构已验证的三种T型核心，不放宽30%纵坡、8.5米净空或桥墩检查 — 控制回归范围并保留游戏原生提示。

Ruling: 8.5米只是最低净空，按真实道路和匝道包络计算更大要求并贯穿核心、层高与最终网络检查 — 独立复核P2有确定反例，已补RED/GREEN并修正 — 自定义厚结构可能需要更大占地，不通过降低安全阈值获取预览。
