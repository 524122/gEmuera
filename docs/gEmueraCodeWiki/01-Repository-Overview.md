# 仓库总览与模块责任

## 项目组成

gEmuera 是 Godot 4.7 Mono + C# 的 Emuera 兼容引擎工程。它不是单一的新 Core 重写工程，而是 **可运行的 legacy Emuera 移植** 与 **正在验证的 Core/Host 迁移合同** 并存的仓库。

```text
Godot project / launcher
  ├─ legacy runtime（当前默认行为 owner）
  │   ├─ Scripts/Emuera/GameProc       ERB 加载、解析、执行状态机
  │   ├─ Scripts/Emuera/GameData       变量、表达式、函数、CSV/常量
  │   ├─ Scripts/Emuera/GameView       控制台显示语义、HTML、输入等待
  │   ├─ Scripts/Emuera/Content        图片/精灵/字体/Graphics surface
  │   ├─ Scripts/uEmuera               WinForms/System.Drawing 兼容层
  │   ├─ Scripts/Panels                Godot 工具面板组件（M2 组件化，场景资产见 scenes/）
  │   └─ Scripts/*.cs                  Godot UI、线程桥、纹理、诊断接线
  └─ candidate Core / migration contracts
      ├─ src/Core                       无 Godot 的 session、ports、DTO、资源等
      ├─ Scripts/GodotHost              Core/legacy session 与 Godot bridge
      └─ tools/*                        contract、fixture、trace、阶段证据
```

## 构建单元与依赖方向

| 单元 | 文件 | Target framework / 责任 | 依赖规则 |
| --- | --- | --- | --- |
| Godot Host | `gemuera-c#.csproj` | 桌面为 `net8.0`；`GodotTargetPlatform=android` 时为 `net9.0`。编译 Godot C# 脚本。 | 引用 `src/Core`；可引用 Godot API、SkiaSharp、SQLite。 |
| Core contracts | `src/Core/GEmuera.Core.csproj` | `net8.0;net9.0` 的纯 .NET 程序集。 | **不得**引用 GodotSharp、Godot Node/Resource/RID 或主机路径。 |
| Core smoke | `tools/core-contracts/CoreContractSmoke.csproj` | `net9.0` 控制台合同测试。 | 引用 Core；只 source-link 少量无 Godot 的 GodotHost 路由/探测文件。 |
| Godot regression | `test/GodotHost/PrototypeHostRegressionTest.gd` | GDUnit4 测试场景/host prototype。 | 运行于 Godot，覆盖 Reload/Toggle/Detach 等 sidecar 行为。 |

依赖图的详细规则见 [`07-Dependencies-and-Threading.md`](07-Dependencies-and-Threading.md)。

## 顶层目录

| 路径 | 责任 | 修改时的注意事项 |
| --- | --- | --- |
| `Scripts/` | 应用的 C# 源码：Godot presentation、legacy runtime、诊断、Host、M0 工具支撑。 | 当前默认运行的主要实现；改动后要判定是否同步更新本 Wiki。 |
| `scenes/` | M2 起新增的工具面板场景资产（Inputpad/Scalepad/QuickButtons/OptionWindow/RuntimeDiagnosticsPanel 的 `.tscn`）。 | 只含根节点与脚本 `ext_resource` 引用；面板脚本位于 `Scripts/Panels/`（诊断面板在 `Scripts/Diagnostics/`）。 |
| `src/Core/` | 将来架构所需的 pure .NET contract/prototype。 | 不要引入 Godot 类型、文件系统路径泄漏或反射式运行时发现。 |
| `test/` | 当前 GDUnit4 回归入口。 | 场景/Node 相关行为应优先在此类测试覆盖。 |
| `tools/` | identity、fixture、legacy runner、dialect、save、Core、文档和治理工具。 | 生成报告不等于 gate 已通过；保留真实状态。 |
| `docs/NewFrameworkDesign/` | 当前迁移设计、hand-off、阶段证据与验收。 | 设计/门禁的权威入口，不是默认运行代码。 |
| `docs/gEmueraCodeWiki/` | 本 Wiki。 | 源码拓扑、key type、调用关系或维护规则变化时同步更新。 |
| `docs/OriginalFrameworkDesign/` | 早期历史设计。 | 已过时，只作背景参考。 |
| `docs/xEmueraCodeWiki/` | 外部 XEmuera 参考说明。 | 不应当作本仓库行为的依据。 |
| `Resources/`、`Fonts/`、`Icons/`、`Text/`、`Lang/` | 运行时资源、字体、文本和语言数据。 | 注意 Godot 导入/平台差异与资源路径。 |
| `NativeLibs/` | Android native SQLite 库等。 | 与 csproj Android copy target 关联；不要随意替换 ABI/文件名。 |
| `android/` | Android 导出模板/构建相关内容。 | `android/build/` 是生成产物，不做手写业务改动。 |
| `addons/gdUnit4/` | vendored Godot 测试插件。 | 除测试框架维护外，不作为业务代码 owner。 |
| `.godot/`、`bin/`、`obj/`、`node_modules/` | 编辑器/构建/包管理生成目录。 | 不纳入代码导航或提交物。 |

## `Scripts/` 模块地图

| 目录 / 文件群 | 当前 owner | 关键类型 / 文件 | 主要入口 |
| --- | --- | --- | --- |
| 根级 `Scripts/*.cs` | Godot presentation 与 legacy bridge | `FirstWindow`、`EmueraMain`、`EmueraThread`、`EmueraContent`、`GenericUtils`、`SpriteManager` | 场景回调、UI queue、输入、纹理上传、显示投影。 |
| `Scripts/Panels/` | 组件化工具面板（M2） | `Inputpad`、`QuickButtons`、`Scalepad`、`OptionWindow` | 由 `scenes/*.tscn` 实例化并挂载到 `EmueraContent`；导出布局度量与 `PadShown/PadHidden`、`PopupOpened/PopupClosed` 信号。 |
| `Scripts/Emuera/GameProc/` | legacy ERB 加载/执行 | `ErbLoader`、`LogicalLineParser`、`LabelDictionary`、partial `Process`、`ProcessState` | `Process.Initialize()`、`DoScript()`、`runScriptProc()`。 |
| `Scripts/Emuera/GameData/` | legacy 数据与表达式 | `ExpressionMediator`、`ExpressionParser`、`VariableEvaluator`、`VariableData`、`FunctionMethodCreator` | 解析/求值、函数注册、变量读写、CSV/常量。 |
| `Scripts/Emuera/GameView/` | legacy 控制台语义 | `EmueraConsole`、`PrintStringBuffer`、`ConsoleDisplayLine`、`HtmlManager` | 输出 line/parts、输入请求、按钮和等待恢复。 |
| `Scripts/Emuera/Content/` | legacy 图像/字体内容 | `AppContents`、`GraphicsImage`、`ConstImage`、sprite types | 图片加载、sprite 定义、合成和 Graphics surface。 |
| `Scripts/Emuera/Config/` | legacy 配置/宏 | `Config`、`ConfigData`、`JSONConfig`、`KeyMacro` | 游戏目录下配置读取和运行参数。 |
| `Scripts/Emuera/Runtime/`、`Modern/`、`Sub/`、`_Library/` | runtime helper 与兼容设施 | lexer、CSV/data、系统/计时/输入 helper 等 | 被 legacy 解析器与 view 调用。 |
| `Scripts/uEmuera/` | WinForms / Drawing / Media compatibility shim | `Application`、`Window.MainWindow`、`Forms`、`Drawing`、`Utils` | 将原 Emuera 上层 API 映射到 Godot host。 |
| `Scripts/GodotHost/` | application/host/canary bridge | `AppBootstrap`、`PlatformGateway`、`LegacySessionBackend`、`PrototypeRuntimeNode` | Autoload、sidecar session、桥接、生命周期。 |
| `Scripts/Diagnostics/` | 运行期日志/诊断/导出 | `RuntimeDiagnosticsConfig`、`DiagnosticLogRouter`、`InputReplayBuffer` | `config.toml`、panel、breadcrumb、export。 |
| `Scripts/M0/` | legacy baseline/trace 支撑 | `LegacyRunnerHost`、`LegacyTraceRecorder`、`LegacyDisplayObservation` | `tools/legacy-runner` 的隔离/回放/证据路径。 |

## `src/Core/` 模块地图

| Core 目录 | 关键类型 | 当前用途 |
| --- | --- | --- |
| `Application/` | `GameSession`、`GameSessionOptions` | 组合一个 session 的变量、资源、保存、解析 adapter 等候选服务。 |
| `Compatibility/` | `CompatibilityPlan`、catalog、module、descriptor route | profile/module/route 的冻结模型；并非完整 legacy 行为替换。 |
| `Session/` | `SessionCoordinator`、`SessionSwitchLease`、`LegacySessionFacade`、generation types | candidate/commit/rollback 的会话迁移合同。 |
| `Parsing/` | `ErbParser`、`ErbParseResult`、diagnostics | 简化解析合同，不能等同 legacy 完整 grammar。 |
| `State/` | `VariableStore`、`CoreValue`、snapshot/candidate | typed 变量候选模型。 |
| `Display/` | `DisplayTransaction`、line/part/barrier DTO、tee | 未来显示交易合同；默认 renderer 尚未切换。 |
| `Resources/` | `PixelStore`、`ResourceRuntime`、catalog/budget | CPU pixel truth、revision、资源 admission/projection 合同。 |
| `Save/` | `SaveService`、`DeterministicSaveCodec`、blob store | 保存候选模型；未绑定完整 legacy game save profile。 |
| `Ports/` | `PortManifest`、adapter scope、completion dispatcher、runtime port hub | platform input/storage/database/audio/lifecycle contract。 |
| `Runtime/` | `IErbInterpreterHost`、catalog、`LegacyCoreAdapter` | step/resume/effect contract 与 candidate parse bridge。 |
| `Experiments/` | scheduler、yieldability audit | 默认关闭的 M6 实验合同。 |
| `Governance/` | runtime lease、removal/release/rollback records | M7 治理库存。 |

## 非代码关键入口

| 文件 | 用途 |
| --- | --- |
| `project.godot` | main scene、Autoload、Compatibility/OpenGL ES 3 renderer、application feature flags。 |
| `first_window.tscn` | 启动器场景，只挂载 `FirstWindow.cs`。 |
| `main.tscn` | legacy `EmueraMain` 加 Core prototype child nodes。 |
| `scenes/*.tscn` | M2 组件化工具面板场景资产，供 `EmueraContent` 通过 `GD.Load<PackedScene>` 实例化挂载。 |
| `config.toml` | diagnostics 与 session-isolation canary 的运行期配置。 |
| `export_presets.cfg` | desktop / Android export preset。 |
| `gemuera-c#.sln` | Godot host solution。 |
| `ERBAPI.md` | 修改 ERB 指令、函数、变量或方言时的实施约束。 |

## 从“问题”到“文件”的快速表

| 现象 / 需求 | 优先检查 |
| --- | --- |
| 启动器扫描不到游戏、profile 错误、权限/SafeArea 问题 | `FirstWindow.cs`、`project.godot`、[`02`](02-Startup-and-Lifecycle.md)。 |
| 游戏不进入、线程未启动、重启/退出卡住 | `EmueraMain.cs`、`EmueraThread.cs`、`Program.cs`、`LegacyThreadQuiescence.cs`。 |
| ERB 加载/label/懒加载/执行错误 | `GameProc/ErbLoader.cs`、`LogicalLineParser.cs`、`Process*.cs`、[`03`](03-Legacy-Interpreter.md)。 |
| 函数、变量、表达式或 CSV 错误 | `GameData/` 对应子目录、[`04`](04-Legacy-Data-and-Expressions.md)、`ERBAPI.md`。 |
| 文本、HTML、图片、按钮、滚动或输入异常 | `GameView/`、`EmueraContent*.cs`、`EmueraImage.cs`、[`05`](05-Console-Rendering-and-Resources.md)。 |
| 输入面板/快捷按钮/缩放/设置窗口布局或行为 | `Scripts/Panels/`、`scenes/*.tscn`、`EmueraContent`。 |
| session/profile/canary/ports prototype 问题 | `src/Core/`、`Scripts/GodotHost/`、[`06`](06-GodotHost-and-Core.md)。 |
| 日志、回放、性能样本、保存 trail | `Scripts/Diagnostics/`、`GenericUtils.cs`、`config.toml`、[`08`](08-Operations-Testing-and-Diagnostics.md)。 |
