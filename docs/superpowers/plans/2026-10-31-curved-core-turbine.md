# Curved Core and Turbine Fix Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement task-by-task. Steps use checkbox syntax for tracking.

**Goal:** 复用现有弯道匹配，让核心与匝道共同弯曲，修复涡轮短节点桥墩叠放和右转外缘对齐遗漏。

**Architecture:** 新增独立EndpointCoreBend模型构造器；EndpointStackCore在退接切点参考中尝试弯曲/直线核心；模板profile及RightAttach由独立任务修复，最终统一回归。

**Tech Stack:** C# netstandard2.0/net48，xUnit net8，现有CurveWarp/Centerline与原版只读日志。

**Spec:** docs/superpowers/specs/2026-10-31-curved-core-turbine-design.md

## Global Constraints

- autochange、0.0.1-beta；不清理dirty内容，不提交或发布，正式放置锁定。
- 30%坡度上限、原20°接路认证、8.5米净空、真实支撑规则保留。
- 至多8组新增退接向量、与基线共用128次完整候选；原路Prefab/身份/宽度/切线及退接上限保留。
- 不屏蔽Native报警，不强关游戏；备份及26文件SHA256核验。

## Review Focus

- 曲线变换不得破坏精确逻辑节点或匝道边缘对齐。
- LHT、两路上下层交换和四臂索引不能串位。
- 模板短knot移除后必须认证实际修改的profile；拒绝不安全合并。
- 弯曲缩短平面距离可能抬高坡度，所有输出段须重新严格检查。
- 新增核心参考必须计入同一候选预算，回退元数据与提示须准确。

### Task 1: 涡轮模板源头修复

- [x] 添加稳定短段及右转外缘回归，确认RED；追加紧凑汇入、末端profile保护的RED/GREEN。
- [x] 安全合并实际发射节点的profile近邻knot并检查最终cut间距，修复RightAttach与剩余横移量。
- [x] 新增12项GREEN；既有兼容154项及最终集成94项通过。

### Task 2: 核心随道路弯曲

- [x] 添加真实弯路核心非直线的3项RED；新增节点/切线及直路/LHT交换或原子失败回归。
- [x] 实现EndpointCoreBend，接入CutCandidates；保持原入口直线兼容。
- [x] 传播CoreFollowsCurve元数据，更新面板与只读日志，重查所有安全条件；四组真实拟合均采用曲线核心。
- [x] 真实回放及几何对比图验证，已检查涡轮18%与定向左出左进16%的前后图。

### Task 3: 集成交付

- [x] 独立代码复核及缓存窄改复核通过；94项针对性、1564项全量测试通过；Release暂存构建成功。
- [x] 用户正常关闭游戏后完成备份部署、26/26文件SHA256及0.0.1-beta核验；报告和游戏测试清单见docs/31。游戏验收待用户完成。

## Rulings

- 完整弯曲可能使预先规划的桥墩失去余量，采用1、1/2、1/4、0四个有限强度，不放宽支撑判定；全部强度共用原候选预算。
- 映射矩阵验证所有安全输出及回退，不能要求每一个合成姿态都强行使用弯曲核心；桥墩判定拒绝全部固定切点参考时，核查诊断及失败无部分输出。12组是映射/原子失败覆盖，不作为12组均能生成的支持声明。真实四组回放另行明确断言曲线核心实际被采用。
- 只清理真正发射为内部桥节点的近邻knot；不发射的末端knot继续保留原高度，新增0.67米偏差的RED/GREEN回归。
- 复用同一参考枚举内的StackTemplate，利用现有参数缓存，保持几何结果并减少重复求解。
