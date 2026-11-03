# section2change（平改立）

A Cities: Skylines II mod that places a **complete interchange in one placement**: pick a type, aim at an existing junction, crossing or road (or empty ground), check the preview, confirm.

一个天际线 2 mod：**一次放下一整座立交**。选类型，对准已有路口、立体交叉或道路（也可以在空地上），确认预览后放置。中文名“平改立”取自道路工程里的“平面交叉改立体交叉”。

Development branch: **autochange** · Version: **1.0.9-beta** · License: [MIT](LICENSE)

本版归档 autochange 的**点选节点、整座立交约束求解和确认放置**功能。节点模式已接入苜蓿叶（普通／贴环道）、定向（右进右出／左进左出）、涡轮、喇叭形、定向T、半定向T、Y型、井字和环岛（圆形／跑道形），支持双向、双幅及混合输入。自动／手动模式保留原 section2change 功能。范围与验证记录见 [1.0.9-beta 归档说明](docs/48-1.0.9-beta版本归档.md)。

![Turbine on a curved crossing 弯道上的涡轮](docs/images/01-overview.jpg)

| Before 原路口 | Preview 预览 | After 放置后 |
|---|---|---|
| ![](docs/images/03-retrofit-before.jpg) | ![](docs/images/04-retrofit-preview.jpg) | ![](docs/images/05-retrofit-after.jpg) |

| Diamond 菱形 | Cloverleaf 苜蓿叶 | Stack 定向 |
|---|---|---|
| ![](docs/images/06-diamond.jpg) | ![](docs/images/07-cloverleaf.jpg) | ![](docs/images/08-stack.jpg) |

## Features · 功能

- **13 interchange types** — four-way: diamond, single-point, Chinese expressway node, cloverleaf, partial cloverleaf, roundabout, stack, turbine; three-way: expressway T, trumpet, semi-directional T, directional T; wye
  **13 种立交**：四向的菱形、单点、中式快速路节点、苜蓿叶、部分苜蓿叶、环岛、定向、涡轮；三向的快速路 T 型、喇叭形、半定向 T、定向 T；Y 型分向
- **Upgrade existing junctions** — matches roads, levels and angle, removes the old junction and reconnects every leg; roads passing under bridges with enough clearance are kept
  **平改立**：自动匹配原有道路、层高和交角，拆掉原路口并接回各臂；桥下净空够的道路保留
- **Curves and slopes** — follows curved roads (radius ≥ 150 m) and places the whole interchange on the roads' grade
  **弯道和坡道**：顺着弯道整体弯曲，按原有道路的坡度整体放置
- **Lane balance, edge-aligned lane drops, pillars placed clear of roads below, grade and clearance checks before placing**
  车道平衡、边缘对齐的减车道、立柱避开下层道路、放置前检查坡度和净空
- **Auto mode** (point and confirm) and **manual mode** (every parameter adjustable); Chinese and English UI
  **自动模式**一指即放，**手动模式**可调所有参数；中英文界面
- **Node mode** selects open road endpoints, solves the complete interchange, reconstructs bounded approach sections and confirms placement after native game checks.
  **点选节点**：彩色端点反馈、准确车道匹配、双向／双幅混合接入、有界自动退接、曲线和混合高程适配；定向双向／混合求解支持后台计算与取消旧请求。

### 点选节点

1. 选择已支持的立交类型，再进入“点选节点”。每个方向选择一个对称双向端点，或一进一出的两个单向端点；三向类型使用三个方向。
2. 选齐后查看自动预览，或点击“生成预览”。需要更多空间时可调整每臂退接上限；最大坡度参数使用百分比，最高30%。Y型每方向须偶数车道，井字每方向至少3车道；缺少准确匹配道路时会拒绝生成。
3. 检查面板、虚线改造范围、接回原路的位置与原生错误，满足条件后在面板“确认放置”。大坡度及紧凑斜交可能仍需玩家自行开启无碰撞辅助；程序保留原生放置检查。

节点模式不开发部分苜蓿叶、菱形、单点、中式快速路及快速路T；梅花类型已移除。自动／手动模式的原有类型不受此范围说明影响。

## Usage · 使用

1. Open the tool with the button at the top left or **Shift + I**. 左上角按钮或 **Shift + I** 打开。
2. Pick a type. Hover a junction / road to match it, or empty ground. 选类型，鼠标对准路口、道路或空地。
3. Left-click to pin the preview, check the result and errors in the panel, then **Confirm**. 左键固定预览，看面板里的结果和报错，点“确认放置”。

Read the in-game guide on first use. Key points: 首次打开会显示使用指南，要点：

- **Placement cannot be undone** (the game does not support undo) — save first if in doubt. **放置后不能撤销**，必要时先存档。
- Works best on flat ground with straight, uniform-grade roads; keep road type and curvature steady within the interchange. 最好在平地、平直且坡度均匀的道路上使用。
- Trumpet, semi-directional / directional T, stack, turbine and wye support right-hand traffic only. 喇叭形、定向类、涡轮、Y 型目前只支持右侧通行。

## Reporting bugs · 反馈问题

Please [open an issue](https://github.com/zilianli/section2change/issues/new/choose) with a screenshot of the panel and the mod log:
请附面板截图和 mod 日志提交 issue：

`%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\Logs\InterchangeMod.Mod.log`

Turn on **debug logging** in the mod settings before reproducing — the log then records the junction and parameters so the case can be replayed exactly.
复现前在 mod 设置里打开**调试日志**，日志会记下路口和参数，便于一比一还原。

## Building · 构建

Requirements: Cities: Skylines II with the official modding toolchain, .NET SDK, Node.js.

```bash
cd src/InterchangeMod && DOTNET_ROLL_FORWARD=Major dotnet build -c Release
```

Builds stage the complete package in `.artifacts/staging`. Install UI dependencies with `npm ci` in `src/InterchangeMod.UI` first. To deploy to the game after closing Cities2, use `dotnet msbuild src/InterchangeMod/InterchangeMod.csproj -restore -t:Build -p:Configuration=Release -p:DeployToGame=true` from the repository root. Keep `DOTNET_ROLL_FORWARD=Major` if only .NET 8 is installed. Deployment refuses a running game.

默认构建仅暂存到 `.artifacts/staging`。关闭游戏后，显式指定 `DeployToGame=true` 才部署。继承基线的功能范围见 [1.0.8.1 修复与测试说明](docs/13-1.0.8.1修复与测试说明.md) 和 [道路形式支持矩阵](docs/12-1.0.8.1道路形式支持审核.md)。autochange 的逐轮开发与验证见 docs/14 至 docs/48；最新功能基线通过2319项自动测试，用户已确认本轮定向复测通过。该确认不扩展为所有道路组合或交通场景均已验证。

```bash
cd tests && DOTNET_ROLL_FORWARD=Major dotnet test InterchangeMod.Generation.Tests
```

- `src/InterchangeMod.Generation` — game-independent geometry (templates, matching, curve warp), unit-tested
- `src/InterchangeMod` — game integration (tool system, placement, removal)
- `src/InterchangeMod.UI` — panel (React / TypeScript)
- `docs/` — design notes (Chinese) 设计文档

## License

[MIT](LICENSE) © 2026 Ifliz
