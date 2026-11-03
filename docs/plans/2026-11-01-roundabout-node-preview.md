# 环岛节点预览与梅花下线实施计划

> 实施沿用已批准方案 B 与 section2change 类型顺序；使用 subagent-driven-development 分任务实施与复核，未经用户要求不提交或推送。

目标：按玩家要求移除梅花，直接开发环岛，保持 autochange / 0.0.1-beta 隔离工作区与其他已验收类型。

状态：首轮测试包已部署。195 项相关回归和全部 2047 项回归通过，UI 检查/构建及 Release 构建通过；两轮独立审查通过，游戏关闭时备份部署并核对 27 个文件，0 差异。游戏内验收待玩家完成。

## 已确定设计

- 从枚举、目录、UI、运行时及活动求解链移除 CompactFlower；删除本轮已放弃功能的专用核心/Setup/Search/测试/图标，不碰原始资产和备份。历史设计和测试记录保留并标注停用。
- 旧设置 CompactFlower 规范化为 Roundabout，保证直接属性反序列化和 SetValue 都可迁移，不能仍选中隐藏类型。
- 使用原有 RoundaboutTemplate 的两层环岛：0/2 主线贯通，1/3 被交道路接环道。首轮八端点双幅道路；各相对同款同宽。主线驶出至汇入间按实际目录精确 N-R 减车道，环道按被交道路实际车道及道路类别选择单行道路（至少两车道）。
- 保持已有 Roundabout 枚举/自动/手动功能，新增其点选节点预览，不增加另一近似类型。
- 圆环及跑道形沿用现有 RoundaboutRacetrack 参数；优先验证圆环，再验证跑道形。整体刚体/割线姿态与有界真实曲线接路，不缩放预制布局。
- 原高程由接路保留；自动两层高差至少 10m 且满足实际所有断面结构包络及高架桥阈值，必要时抬高整体。最大坡度仍 30%，每臂退接最多1000m。
- 全部12种非同臂流向须可达。环岛允许合法同臂绕行，不按封闭环路本身误判掉头或非简单路径。
- 保留实际曲率/纵坡/桥跨/路面与桥墩检查、原生报错和正式放置锁定；井字既有人工无碰撞提示保留。

## 任务边界

1. Core：新增 EndpointRoundaboutCore，重解 RoundaboutTemplate 并转换真实 RoadSegmentSpec 物理/逻辑端点、宽度和源臂；8端口、闭环、12流向、左右行、减车道、真实几何和支持校验回归。独立核心代理只写新核心与核心测试。
2. Menu/runtime：下线梅花及其注册/菜单/旧值迁移；在 Nodes.cs 接入 RoundaboutSetup/Create 和 RoundaboutSearch/Search，使用真实环道/匝道/减车道断面及两层提示。专用代理负责枚举/UI/Settings/Runtime及注册迁移测试，不改其他生成几何。
3. Root：RoundaboutSetup 与 Search，搜索共享工厂参数化，删除花形专属代码/测试；实际弯路/混合高度/预算回归及记录。
4. 各任务 RED→GREEN，独立审查，UI 类型检查/构建，全量 Generation 回归，Release暂存构建；玩家正常关闭游戏后备份部署逐文件核对。保留实际游戏验收边界。

## 公共 API 约定

Setup.Create(arms,catalog,offset,gap,terrainHeight,preferredRamp=null)：Success,Diagnostic,Ramp,Ring,Design,MainHeight,CrossHeight,RequiredClearance。

Core.Create/Candidates(arms,rightHandTraffic,mainHeight,crossHeight,ramp,ring,maxGrade,design,terrainHeight,bool racetrack=false)，返回既有 EndpointStackCoreResult。

Search.Search(arms,traces,rightHandTraffic,mainHeight,crossHeight,ramp,ring,maxGrade,maxRetreat,design,int maxCandidates=128,float verticalClearance=8.5f,float? terrainHeight=null,bool racetrack=false)。

默认主线在上层、环道在下层；此阶段不加入新的核心整体弯曲变形开关。后续按玩家验证优化接路与尺寸。
