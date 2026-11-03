# Retreated Center Smoothing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 根据最终退接点重解中心与方向，减少接路曲率突变和短宽道路段，并保留所有真实游戏报警。

**Architecture:** 保留EndpointStackSearch作为安全基线，在新的有界拟合器中固定候选退接向量，再根据该向量的真实端点求核心及接路。纯几何曲率认证和近似等弧长切分单独实现；原路节点身份、原路曲线和放置锁定契约保留。

**Tech Stack:** C# generation/netstandard2.0；xUnit/net8；Cities2 ECS只读诊断；现有Release暂存与部署流程。

**Spec:** docs/superpowers/specs/2026-10-31-retreated-center-smoothing.md

## Global Constraints

- 版本0.0.1-beta、autochange现有工作区；不清理历史未提交内容。
- 最多8组新增退接向量，与基线共享128次完整候选预算，不超过用户退接上限。
- 20°逐段切线、核心方向单调性、严格最大坡度、8.5米净空和实际支撑仍是必需条件。
- 新平滑候选最小半径至少2倍道路宽度；接路/尾段水平弧长至少max(10,道路宽度)，输出段三维长度不超过70米。
- 不隐藏警告，不强行关游戏，部署前备份并核验全部文件。

## Review Focus

- 非对称/弯曲双幅：最终核心中心应等于本候选实际切点中线交点。
- 高低路交换及LHT：原Arm、Prefab、宽度、切线和高度不串位。
- 13m/17.5m尾段：宽20m道路不能继续选择这些切点。
- 短宽/高曲率段：半径认证和等弧长切分不能绕过20°、最短长度或预算。
- 原版会修改Y：保留Native报警及碰撞Target，区分生成层与游戏结果。

### Task 1: 接路几何与预留

- [x] 写失败测试：日志两处短尾、18m高曲率边、等弧长长度边界；追加坡度掩盖平面不足与65米预留边界的RED/GREEN。
- [x] 实现曲率界认证、接路切分和宽度预留，保留Generic Cut兼容。
- [x] 运行针对性回归，确认短尾及高曲率真实样例被拒；最终54/54。

### Task 2: 固定切点后重解

- [x] 写真实四臂回放失败测试，涵盖定向左右出入和涡轮，证明新中心来自最终切点。
- [x] 实现最多8个退接向量、cut参考专用核心、曲率评分和共享预算。
- [x] 对比旧/新PNG及曲率数据，确认生成层检查通过且外部接路变缓；最终分段修改后刷新导出图。

### Task 3: 集成与交付

- [x] 节点工具接入拟合器，保留游戏报警；追加Target日志只读诊断。
- [x] 独立代码复核通过；完整回归1536/1536；Release暂存构建成功。
- [x] 用户关闭游戏后完成备份与部署，26/26文件SHA256及0.0.1-beta版本核验一致；测试清单见docs/30，游戏验收待用户完成。

按用户既有授权直接推进本方案；不执行提交、推送或发布。
