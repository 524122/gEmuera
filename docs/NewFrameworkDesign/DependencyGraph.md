# 依赖图、端口与架构守卫

## 项目级依赖

迁移期先形成：`legacy Godot App → Migration façades → new Core/Contracts`，旧 `Scripts/Emuera` 与新 Core 可同时存在但不能互相循环引用。每移一块，legacy adapter 只能依赖新契约；新 Core 永远不引用 legacy/Godot。远期图如下：

当前已落地的第一块是 `src/Core/GEmuera.Core.csproj`：它只提供 immutable compatibility/session contracts 和 smoke，不被旧 Godot 项目引用。`gemuera-c#.csproj` 显式排除该目录，防止迁移期的默认 C# glob 把 Core 误当成 Godot host 代码。

```text
XEmuera.Godot.Core
  ├─ BCL
  └─ approved pure-.NET packages

XEmuera.Godot.Bridge ──ProjectReference──> Core
XEmuera.Godot.App    ──ProjectReference──> Bridge, Core
XEmuera.Godot.Core.Tests ────────────────> Core
XEmuera.Godot.ApiSmoke ──────────────────> Bridge/App + locked GodotSharp
XEmuera.Godot.DifferentialTests ─────────> Core + fixture protocol
```

Core 不引用 GodotSharp、Bridge、App、场景或平台插件。Bridge 实现 Core port；App 组合节点。测试项目使用 `ProjectReference`，不得以 `Compile Include=../../src/Core/**/*.cs` 重复编译源码。

## 命名空间边界

| 命名空间 | 允许依赖 | 禁止依赖 |
| --- | --- | --- |
| `XEmuera.Core.Parsing` | Core.Common、Core.Expressions | VM View/Bridge |
| `XEmuera.Core.Vm` | Parsing、Variables、Effects、Ports abstractions | Godot、filesystem concrete |
| `XEmuera.Core.Saves` | Variables DTO、bounded streams | Node、FileAccess |
| `XEmuera.Core.Resources` | path tokens、catalog DTO | Texture2D、Image |
| `XEmuera.Core.Display` | immutable style/line DTO | Godot Color/Font/RichTextLabel |
| `XEmuera.Bridge.*` | Core ports/DTO、Godot | Core private implementation details |
| `XEmuera.App.*` | Bridge public components、Godot | VariableStore direct access |

## Core Ports

| Port | Core 请求 | 实现层 | 线程/取消规则 |
| --- | --- | --- | --- |
| `IMonotonicClock` | elapsed ticks | BCL Stopwatch wrapper | 线程安全、无 wall clock |
| `IReadOnlyContentSource` | enumerate/open relative token | desktop/SAF/iOS import adapter | 流受上限、CTS |
| `ISaveStorage` | open temp/replace/recover | platform storage adapter | 同目录事务、journal |
| `ITextDecoder` | bytes→text result | Core EncodingService | 无 Godot API |
| `IBackgroundScheduler` | pure work item | Bridge worker pool | 不接受 Node/Resource |
| `IDiagnosticsSink` | structured sanitized event | Telemetry adapter | 严格体积与隐私限制 |
| `IPixelDecoder` | bounded stream→owned PixelSurface | pure infrastructure decoder | reserve 内存；无 Godot Texture |
| `IFileOperationPort` | text/load/save/enumerate | platform storage adapter | completionMode 明确、路径受控 |
| `IDatabasePort` | SQLite command/reader | desktop/Android capability | 专用 VM thread 或 WaitPort；事务取消 |
| `ISystemValueSource` | clock/random/platform capability | injected implementation | 调用次数/顺序可重放 |

Audio、render、input 不作为同步 port 直接回调 UI，而通过 batch/completion queue 与 VM 交互。每个指令的 immediate/fire/wait/bounded-blocking 模式由 [ExecutionContract](ExecutionContract.md) 决定。

## 初始化顺序

```text
AppBootstrap
  → validate ToolchainLock and application defaults
  → PlatformGateway.Initialize
  → SessionCoordinator.Initialize(portFactory)
  → instantiate Main scene
  → MainOrchestrator.Inject(services)
  → SwitchGameAsync
```

Autoload 排序只能保证创建顺序，不能替代显式初始化。任何较早 Autoload 不得在 `_Init`/构造器访问较晚 Autoload。Main 场景的组件用 typed exports 或注入对象接线。

## Variant/Signal 边界

Godot signal 与 CallDeferred 只使用锁定版本可转换的值。普通 CLR `GameConfig`、`GameBaseDto`、`InputRequestDto`、`SwitchResult` 不直接传入。

| 数据 | 推荐过界方式 | 原因 |
| --- | --- | --- |
| request id/generation | `long` signal 参数 | Variant-safe |
| 输入字符串/整数 | String/long + request id | 可验证、无对象生命周期 |
| Display batch | 主线程队列持有 CLR 引用 | 避免 Variant 深转换和 signal 大 payload |
| 资源完成 | queue item `{generation, handleId, status}` | 旧会话可丢弃 |
| 平台 picker | signal 只传 token/status；CLR Stream 留在 adapter | Stream 不可 Variant 封送 |

## VM ↔ 主线程队列不变量

- 有界容量；满时对进度类消息合并，对结果类消息反压，不静默丢失。
- item 含 generation、operation id、completion kind 和不可变 payload。
- Drain 每帧有数量/时间预算；完成后才释放 payload。
- 取消不等于后台立即终止；迟到结果仍必须通过 generation guard。
- 异常转换为 typed `BackgroundFault`，不得在未观察 Task 中丢失。

## 循环依赖防护

常见潜在环及裁决：

| 潜在环 | 裁决 |
| --- | --- |
| VM ↔ Console | VM 产生 DisplayEffect；Console 不回调 VM，只发 InputIntent 给 orchestrator。 |
| Resource ↔ Display | DisplayPart 保存逻辑 handle；Bridge registry 解析 Texture。 |
| Pixel ↔ Texture | PixelStore 是 CPU truth；Bridge 只上传 `(handle,revision)`，不反向决定脚本像素。 |
| Save ↔ VariableStore | snapshot builder 单向读取；load 构造候选 store 后原子替换。 |
| Audio ↔ Session | AudioBridge 接受 generation；不持有 Session。 |
| Config ↔ Platform | Config 保存 capability-independent 值；Bridge 计算 effective settings。 |

## 架构守卫

建立项目后 CI 必须运行：

1. MSBuild graph 检查 Core 的 PackageReference/ProjectReference。
2. Roslyn/文本守卫拒绝 Core 中 `using Godot`、`Godot.*` 基类和公开签名中的 Node/Texture/Image/Color/Rect2/Variant/Callable/GodotObject。
3. Namespace dependency test 拒绝 Core→Bridge/App 和 App→Core internal。
4. Reflection test 检查 Core assembly references 不含 GodotSharp。
5. 普通 CLR 类型不得带 `[Signal]` 或调用 `EmitSignal`。
6. 搜索所有 `CallDeferred`/signal emit，审查参数类型。
7. 场景测试确保每个 View 可用 stub context 独立实例化并退出，无 orphan node。

伪代码命令契约：

```text
dotnet build src/Core/GEmuera.Core.csproj --no-restore
dotnet run --project tools/core-contracts/CoreContractSmoke.csproj -c Release --no-restore
powershell -NoProfile -ExecutionPolicy Bypass -File tools/core-contracts/Test-CoreArchitecture.ps1 -ProjectRoot .
dotnet test tests/Architecture/XEmuera.Godot.ArchitectureTests.csproj
dotnet test tests/Core/XEmuera.Godot.Core.Tests.csproj
godot --headless --path app --editor --quit-after 1   # 锁定后 ApiSmoke
```

其中 `src/Core` 与 `tools/core-contracts` 是当前仓库已落地的合同切片入口；`tests/Architecture`、`tests/Core` 和 `app` 仍是后续完整迁移门禁，不能据此宣称 façade、runtime resolver 或 Parser/VM 已接入。
