# Godot Host、Core 合同与迁移 Sidecar

> 本页的第一原则：**默认游戏行为仍由 legacy owner 承担。** Godot Host 和 `src/Core` 可以封装、观察、验证或受控切换候选能力，但没有正式 gate decision 前，不得接管/删除 legacy Parser、VM、renderer、resource、save 或 platform 路径。

## 当前位置：三层并存

```text
1. Legacy default runtime (player-visible behavior owner)
   EmueraMain -> EmueraThread -> Program -> legacy Parser/VM/View

2. Godot Host / bridge
   Autoload, scene lifecycle, main-thread rendering, launch allowlist,
   Core session sidecar, prototype input/audio/resource/status bridges

3. Pure Core contracts / prototypes
   GameSession, session switch, compatibility plan, display DTO, pixel store,
   save, ports, interpreter contracts, experiments and governance
```

不要把 `src/Core` 中存在的类型理解为已经替换第 1 层。当前阶段、未覆盖项和硬 gate 以 [`../NewFrameworkDesign/DeveloperHandoff.md`](../NewFrameworkDesign/DeveloperHandoff.md) 为准。

## Godot Host 责任

### Application Autoload

| 类型 | 文件 | owner 与约束 |
| --- | --- | --- |
| `AppBootstrap` | `Scripts/GodotHost/AppBootstrap.cs` | application feature flags 与 bootstrap signals；不持游戏业务 state。 |
| `PlatformGateway` | `Scripts/GodotHost/PlatformGateway.cs` | platform id/capabilities、application pause/resume；不拥有 session。 |

### Legacy startup / lifecycle components

| 类型 | 文件 | 作用 |
| --- | --- | --- |
| `EmueraStartupComponent` | `Scripts/GodotHost/EmueraStartupComponent.cs` | 将选定游戏/启动参数组织到 host startup 边界。 |
| `EmueraLifecycleComponent` | `Scripts/GodotHost/EmueraLifecycleComponent.cs` | 组织启动、停止、scene lifecycle 的 Godot-side component。 |
| `EmueraStartupOverlayView` | `Scripts/GodotHost/EmueraStartupOverlayView.cs` | 主线程 startup overlay presentation。 |
| `EmueraGpuRenderComponent` | `Scripts/GodotHost/EmueraGpuRenderComponent.cs` | 主线程离屏/GPU 请求协调。 |
| `EmueraTextRenderComponent` | `Scripts/GodotHost/EmueraTextRenderComponent.cs` | 主线程 text-render 请求协调。 |

这些 component 与 `EmueraMain` 的 legacy host 职责相关，但不可将 `Node`/`Resource`/`RID` 传入 Core。

### Legacy session canary bridge

| 类型 | 关键语义 |
| --- | --- |
| `LegacySessionLaunchConfiguration` | 将绝对 game root + profile 规范化为 opaque `GameId` / `SessionSelection`；Core 不获得文件系统路径。 |
| `LegacySessionLaunchRegistry` | immutable allowlist，将 Core selection 映射回 host 所拥有的 legacy launch input；不能猜路径或静默换 profile。 |
| `LegacySessionBackend` | `ILegacySessionBackend` 的 Godot-host 实现；接收 frozen plan/selection，实际设置 legacy `Program`、`GlobalStatic`，启动/停止 `EmueraThread`。 |
| `LegacyThreadQuiescence` | 在 detach/clean/resource disposal 前证明 legacy worker 已停止。 |
| `GameContentProbe` / `GameCompatibilityDetector` | host-side 游戏内容探测/兼容解析支撑；不是 Core 路径解析接口。 |

`LegacySessionBackend.StartAsync(...)` 的设计边界：Core 交付 `SessionSelection`、`CompatibilityPlan`、`SessionStamp`，Host 用 allowlist resolve 到实际目录/profile，随后调用 legacy startup。启动失败时清除未配对的 compatibility plan，避免失败 candidate 污染下一次 switch。

## Prototype sidecar

`main.tscn` 中的 prototype nodes 用于在同一场景下验证 Core session 与 bridge；默认 player path 仍由 `EmueraMain` 驱动。

| 类型 | 责任 | 不应做什么 |
| --- | --- | --- |
| `PrototypeRuntimeNode` | 创建/切换/释放 Core runtime/session，发布 prototype status，挂接 bridge。 | 成为默认 legacy VM 的替换品。 |
| `PrototypeInputBridge` | 将 typed input request/action 以 generation-bound DTO 形式投影/观测。 | 无证据地消费 legacy input 或直接改 Core variable。 |
| `PrototypeAudioBridge` | 有界 command queue、voice pool、pause/resume、generation 过滤。 | 让旧 generation 音频 completion 影响新 session。 |
| `PrototypeResourceBridge` | 资源 projection、generation/revision 绑定与 Godot-side resource 生命周期。 | 让 Core 持有 Node/RID。 |
| `PrototypeStatusView` | 显示 status 文字。 | 保存业务 truth。 |
| `PrototypeCommandPanel` | 人工 prototype 操作/状态投影。 | 用 UI 直接绕开 session orchestrator。 |

### Prototype bridge 通用规则

- bridge 的 command/effect 必须绑定 **generation**；收到 stale generation 时拒绝或丢弃。
- Godot 节点只在 main thread 创建、修改、释放；Core 返回 immutable snapshot/DTO/effect。
- application pause/resume 由 `PlatformGateway` 和 bridge 处理，不改变 Core/legacy owner 的职责。
- `PrototypeAudioBridge` 有 hard pool/queue upper bound；队列满、detached、paused、not-ready、stale-generation 都是需要显式处理的状态。
- prototype regression 覆盖 Reload、Toggle、Detach、observational input，详见 [`08-Operations-Testing-and-Diagnostics.md`](08-Operations-Testing-and-Diagnostics.md)。

## `GEmuera.Core` 的架构

`src/Core/GEmuera.Core.csproj` 是 pure .NET 构建单元。它不引用 GodotSharp，目标是让 session/contract 层可用普通 .NET contract smoke 验证。

### Application 与 session

| 类型 | 责任 |
| --- | --- |
| `CoreApplicationRuntime` | application composition / runtime switch 的候选组合根。 |
| `GameSessionOptions` | immutable construction input：selection、frozen plan、generation、source token、resource capacity。 |
| `GameSession` | session-scoped CLR object，持有 typed variables、pixel store、resource runtime、save service、legacy parse adapter。 |
| `GameSessionSnapshot` | 供 host/diagnostics 投影的 immutable session 状态。 |
| `SessionCoordinator` | 管理 current session、candidate、prepare/commit/abort 边界。 |
| `SessionSwitchLease` | 未 commit candidate 的 disposable lease；dispose 不改变 current session。 |
| `SessionGeneration` / `SessionStamp` / `SessionCompletionGuard` | 代次、operation identity、stale completion guard。 |
| `LegacySessionFacade` | Core 的 legacy backend 事务外壳；在 host adapter 上执行 controlled start/stop。 |

`GameSession` 是 CLR object，不是 Godot `Node`。它在 `Start()` 后可提供 Core parse/pixel/save/resource 服务；`DisposeAsync()` 释放其中的 Core services，但不负责 scene node 生命周期。

### Compatibility

| 类型群 | 责任 | 重要限制 |
| --- | --- | --- |
| `CompatibilityProfileCatalog` / resolver | profile 定义、probe evidence、候选解析。 | Core 不解析/暴露 host 文件路径。 |
| `DialectModuleCatalog`、`IDialectModule`、contribution types | module 与 instruction/function descriptor composition。 | built-in contribution / descriptor 不是 legacy handler。 |
| `CompatibilityPlan` / builder / snapshot | session 内冻结的 module/profile/descriptor/port plan。 | 不能在 session 中随意修改，也不自动实现 runtime isolation。 |
| `CompatibilityDescriptorRoute` | 只在已有 legacy handler 完整可见面中做 route。 | 禁止用单个增量 descriptor 塞进空/不完整 plan。 |
| `DialectPlanConsumer` | 对 frozen plan 的窄消费视图。 | 不得反向改变当前 profile。 |
| `EraFlCompatibilityModule` | eraFL-related candidate module/utility。 | 不等同已投产的 profile migration。 |

### Display contracts

文件：`src/Core/Display/DisplayDtos.cs`

`DisplayTransaction`、`DisplayLine`、`DisplayPart`、`DisplayStyle`、`DisplayInteraction`、`DisplayOrderBarrier`、`DisplayDataOnlyPatch`、`DisplayTee` 等描述：

- session/generation 和 effect sequence；
- text/image/shape/opaque part；
- div/placement/scroll/style/interaction；
- data-only patch 与 visual rebuild 区分；
- commit/wait 等 ordering barrier；
- deep copy / canonical hash / observer tee。

**现状：** DTO 可用于 contract smoke 和 tee/observation；默认 legacy console data source/render path 未获准切换。改变这个状态需要 M0/M1/M2 前置证据与正式 gate。

### State、parsing 与 runtime contracts

| 区域 | 关键类型 | 当前定位 |
| --- | --- | --- |
| `State/VariableStore.cs` | `VariableStore`、`VariableKey`、`CoreValue`、snapshot/candidate | typed state candidate，不是 legacy VariableData 的完全替换。 |
| `Parsing/ErbParsing.cs` | `ErbParser`、`ErbLogicalLine`、diagnostics/source span | 基础 ERB parse contract；未覆盖 legacy 完整 grammar、label、lazy/VM 语义。 |
| `Runtime/LegacyCoreAdapter.cs` | `LegacyCoreAdapter`、`CoreSessionBoundary` | candidate parse bridge，不是运行中的 legacy VM 包装替换。 |
| `Runtime/ErbExecution.cs` | `IErbInterpreterHost`、catalog/factory、`VmStepResult`、`VmEffect` | step/resume/effect/completion 合同；仓库非测试生产路径未提供完整新 VM factory/host。 |

### Resources、save 与 ports

| 模块 | 关键类型 | 所有权/边界 |
| --- | --- | --- |
| Resources | `PixelStore`、`PixelSurfaceSnapshot`、`PixelHandle`、`PixelRevision`、`ResourceRuntime`、`ResourceBridgeLedger` | Core 保存 CPU truth、revision/dirty rect/budget；pixel mutation 受 VM owner thread 约束；Godot upload 由 bridge。 |
| Save | `SaveService`、`SaveCandidate`、`DeterministicSaveCodec`、`FileSaveBlobStore` | candidate/atomic-ish persistence contract；没有等价绑定真实游戏 save profile/完整 round trip 前不得替换 legacy codec。 |
| Ports | `PortManifest`、`PortSessionScope`、`CompletionDispatcher`、`RuntimePortHub`、input/storage/database/audio/lifecycle contracts | typed platform boundary，规定 payload、timeout、cancel、fallback、owner scheduler；生产 platform adapter 仍未整体切换。 |

#### `PixelStore` 的特殊约束

`PixelStore` 保存 published `PixelSurfaceSnapshot` 并强制 mutation 在创建者 VM owner thread 执行；snapshot 可供 bridge 安全读取。它本身不创建 Godot texture。若要投影为 Godot resource，必须通过 resource bridge 且校验 generation/revision。

### Experiments 与 governance

| 目录 | 内容 | 状态 |
| --- | --- | --- |
| `Experiments/` | cooperative VM runner、scheduler、yieldability audit、M6 evidence/metric contracts | 默认关闭的实验，不是生产 scheduler。 |
| `Governance/` | runtime lease、release cycle、rollback drill、removal packet | M7 合同库存，不是删除 legacy 路径的授权。 |

## Core ↔ Godot 的允许调用方向

```text
Core (no Godot reference)
  -> immutable snapshot / DTO / effect / port completion
  -> Godot Host/bridge (main-thread application layer)
  -> Godot Node/Resource/RID / UI / platform APIs

Legacy VM worker
  -> legacy display/input/resource semantic calls
  -> GenericUtils queue / host bridge
  -> Godot main thread presentation
```

禁止方向：

- Core 引用 GodotSharp 或保存 `Node` / `Resource` / `RID`；
- Core 接收 host filesystem path 而不是 opaque selection/token；
- presentation 直接改 `GameSession` / `VariableStore`；
- 未选择 module 改变 frozen `CompatibilityPlan`；
- 旧 generation effect/completion 写入新 session/bridge；
- 以 Core contracts 已存在为理由移除 legacy fallback。

## 当前迁移状态的安全表述

可以说：

- Core contract assembly、session façade、generation/candidate guard、host sidecar、typed display/resource/save/port contracts 已存在。
- Godot Host 已能承载 prototype runtime、status、input/audio/resource bridge 和 runner-only / canary 观察入口。
- 默认仍是 legacy runtime，M0/M1 等前置证据和 gate 尚未全部关闭。

不能说：

- Core 已成为当前生产 ERB interpreter / renderer；
- CompatibilityPlan 已完成 v24/Snake runtime isolation；
- Typed ports / SAF capability 已在 Android 真实设备完成验证；
- Display DTO、PixelStore 或 deterministic save 已替换 legacy display/resource/save；
- 文件存在、Core build 或一个 smoke 就说明迁移阶段通过。

## 相关页面

- 启动器、场景、legacy startup：[`02-Startup-and-Lifecycle.md`](02-Startup-and-Lifecycle.md)
- legacy ERB owner：[`03-Legacy-Interpreter.md`](03-Legacy-Interpreter.md)
- legacy display/resource owner：[`05-Console-Rendering-and-Resources.md`](05-Console-Rendering-and-Resources.md)
- project edges/thread constraints：[`07-Dependencies-and-Threading.md`](07-Dependencies-and-Threading.md)
- phase/migration authority：[`../NewFrameworkDesign/DeveloperHandoff.md`](../NewFrameworkDesign/DeveloperHandoff.md)