# 三向节点主线识别修复计划

> **For agentic workers:** Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 修复喇叭、定向T、半定向T在玩家从支路开始选点时无法预览的问题。

**Architecture:** 在不可变输入请求生成时识别相对主线，将三组端点及退接上限一同规范为主线0/2、支路1。运行时采用同一份规范输入并同步选点颜色编号；保留六个实体及真实曲线、高程、交通方向和所有安全检查。

**Tech Stack:** C# Generation/xUnit、CS2 ECS、React/TypeScript。

**Spec:** 延续 docs/superpowers/specs/2026-10-31-three-way-nodes-design.md。

- [x] 用本次日志捕获的两套六端点数据复现失败；补全三向分组排列、不同断面和退接上限的测试。
- [x] 自动识别明确相对的主线；同步运行时分组、自动预览触发和选点说明。不能明确识别时保留原输入，不按车道数猜主线。
- [x] 按实际地形基准回放两套场景的三种T型，排查规范分组后剩余的核心、桥墩或接路失败。
- [x] 针对性测试176项、完整回归1695项通过，UI检查、Release构建、独立代码复核通过。
- [x] 游戏正常关闭后备份旧包，部署并逐一核验26个文件及版本；保留正式放置锁定，不提交或发布。
