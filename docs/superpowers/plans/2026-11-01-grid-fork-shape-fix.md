# 井字分合流原生形状修复

**Goal:** 处理玩家直路混合高程与弯路两组场景的分合流原生InvalidShape，保持真实共享节点及平滑接入。

**Architecture:** 沿用section2change已验证的曲线端点/逻辑节点分离：减车道直行与集散道分别对齐全宽道路外侧和内侧边缘，仍连接原有一个逻辑节点。使用实际Prefab宽度计算偏移，后续横移、爬坡、曲率、净空、跨距和桥墩重新认证。不隐藏原生错误，不取消碰撞，不开放正式放置。

**Tech Stack:** 现有C#/Bezier3/xUnit与NodePreviewPlacer、NetCourseBuilder的边缘对齐支持。autochange，0.0.1-beta，不提交或发布。

- [x] 冻结日志 `.artifacts/autochange/game-grid-native-shape-failure.log`，区分两组场景与错误实体。
- [x] RED：检查八个逻辑分合流节点的两条减道曲线未对齐边缘、中心重合。
- [x] 修复几何端点与逻辑节点关系；保持20链、8端口、12方向动作。
- [x] 回放两组真实道路、16%/30%、左右通行；245项相关回归通过，严格坡度、曲率、全网络净空与真实支撑继续执行。
- [x] 独立复核无剩余阻断项。
- [x] 全量回归1968项通过，UI类型检查/Release构建成功；玩家正常关闭游戏后备份部署并核验27文件，差异0，版本0.0.1-beta。

证据：03:30直路和03:31弯路均返回成功候选后，原生对分合流首段双车道道路报告InvalidShape。日志未记录这些预览的OverlapExisting。当前两条减道曲线起于同一点、同向相切，缺少StackTemplate等旧类型的边缘对齐。既有NodePreviewPlacer/NetCourseBuilder已分别传递WorldCurve和StartNode/EndNode，不需改游戏发射接口。
