# Three Way Node Preview Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans or the explicitly scoped independent task dispatch. Steps use checkbox syntax.

**Goal:** 新增大坡度玩家提示，并将喇叭、定向T、半定向T推进到可部署节点预览。

**Architecture:** 独立三向模板核心适配器复用EndpointStackCoreResult端口/路段合同；三向搜索与按端口附加接路独立实现，复用安全认证、真实RoadRetreat与现有点选UI，不修改四向搜索契约。

**Tech Stack:** C# generation/netstandard2.0、Mod/net48、React/TypeScript UI、xUnit/net8。

**Spec:** docs/superpowers/specs/2026-10-31-three-way-nodes-design.md

## Global Constraints

- autochange、0.0.1-beta，不清理历史dirty，不提交发布；正式放置锁定。
- 最大坡度30%，20°接路认证，8.5米道路净空、实际Prefab支撑及原版报警保留。
- 真实退接上限0～1000米/臂，失败无部分网络；不自动改动无碰撞辅助。
- 六个有向外部端口与六个必要非掉头动作，首轮三方向双幅。
- 部署前游戏正常关闭、备份及完整文件SHA256核验。

## Review Focus

- 支路进/出路径只有一个外部端口，不可套用四向贯通path规则。
- LHT及主支路高度交换不能串臂、反转流向或错用道路支撑元数据。
- 车道平衡曲线偏移与共享逻辑节点需同时保留。
- 退接组合不得越过原路窗口，所有核心与附件试探共用预算。
- 大坡度提示不能被误解为已自动启用无碰撞或能替代几何验证。

### Task 1: 三向核心

- [x] 六端口/六动作、三模板与LHT、高差测试RED；补两层地形基准反例。
- [x] 实现EndpointThreeWayCore按实际切点重新求解模板，认证坡度、自交、净空与支柱门槛。
- [x] 核心53项GREEN，真实四车道及采集曲线回放通过。

### Task 2: 三向退接与附加接路

- [x] 真正三向端口接路6项、搜索11项RED/GREEN，覆盖独立窗口、预算及失败原子性。
- [x] 实现EndpointCoreAttachment与EndpointThreeWaySearch，复用已有BuildApproach几何；最终同cuts重求。
- [x] 完整三向候选安全复核与112项集成GREEN。

### Task 3: 玩家流程与提示

- [x] 接入三向层高、选项、搜索路由与明确的道路形式限制；两层使用真实地形。
- [x] 超过18%即时显示无碰撞放置提示，保留所有报警；未自动启用其他Mod。
- [x] UI类型检查/构建成功，实际面板及游戏效果待用户验收。

### Task 4: 交付

- [x] 独立复核及地形窄改复核通过；1645项全量通过；Release成功，三种真实曲线几何图已检查。
- [x] 用户关闭游戏后备份部署，26/26文件SHA256及0.0.1-beta核验一致；类型清单与docs/32报告/游戏测试清单已更新。

## Rulings

- 本轮先完成三种T型；Y型没有贯通主线，另阶段适配，不把输入登记当作几何支持。
- 端部接路按实际CorePort连接，支路路径允许只有一个外部端口；四向原入口委托此通用连接并以既有回归保护行为。
- 三T核心的端口高度遵循模板实际语义，原路高差由外部坡道过渡，不把branchHeight参数冒充所有支路外端的高度。
- 两层下穿必须传入真实地形参考，按实际离地高程规划/验证完整隧道；缺少地形或基层条件不满足则安全拒绝。
