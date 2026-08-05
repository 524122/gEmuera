# 依赖、调用图、线程与所有权

## 构建依赖图

```text
gemuera-c#.sln
  └─ gemuera-c#.csproj (Godot.NET.Sdk)
       ├─ src/Core/GEmuera.Core.csproj (ProjectReference)
       ├─ Microsoft.Data.Sqlite
       ├─ SkiaSharp
       └─ SQLitePCLRaw.bundle_e_sqlite3

src/Core/GEmuera.Core.csproj
  └─ pure .NET only; no GodotSharp reference

tools/core-contracts/CoreContractSmoke.csproj
  ├─ src/Core/GEmuera.Core.csproj
  └─ source links: selected path-free GodotHost contracts
```

### Android conditional edge

当 `GodotTargetPlatform=android` 时，Godot host 项目切到 `net9.0`，并由 csproj 将：

- `NativeLibs/android/arm64-v8a/libe_sqlite3.so`；
- NuGet `SkiaSharp.NativeAssets.Android` 中的 `libSkiaSharp.so`；

复制到输出。该编译配置 **不等同** 已完成 Android export、签名、安装启动或设备兼容性验证。

## 运行依赖图

```text
                         ┌─────────────────────────────────────┐
                         │  Godot main thread                  │
                         │  scenes, Node, UI, texture, GPU     │
                         └──────────────┬──────────────────────┘
                                        │
  first_window / launcher ── selected game/profile ──┐
                                                      ▼
                         ┌─────────────────────────────────────┐
                         │ EmueraMain / GenericUtils           │
                         │ UI queue + render/upload pump       │
                         └──────────────┬──────────────────────┘
                                        │ Start / Input / End
                                        ▼
                         ┌─────────────────────────────────────┐
                         │ EmueraThread legacy worker          │
                         │ Program -> MainWindow -> Console    │
                         └──────────────┬──────────────────────┘
                                        │
                                        ▼
                         ┌─────────────────────────────────────┐
                         │ Legacy runtime                       │
                         │ GameProc <-> GameData <-> GameView  │
                         │                <-> Content/uEmuera  │
                         └─────────────────────────────────────┘

Core candidate (sidecar):
  PrototypeRuntimeNode -> CoreApplicationRuntime/GameSession
                       -> immutable DTO/effects
                       -> generation-scoped Host bridges
                       -> Godot main-thread projection
```

## 关键调用链

### 启动与停止

```text
project.godot
  -> first_window.tscn / FirstWindow._Ready()
  -> main.tscn / EmueraMain._Ready()
  -> EmueraMain.Run()
  -> EmueraThread.Start()
  -> Program.Main()
  -> uEmuera.Application.Run(MainWindow)
  -> MainWindow.Init()
  -> EmueraConsole / Process.Initialize()

EmueraMain._ExitTree()
  -> GenericUtils.NotifyApplicationShutdown()
  -> StopLegacySession() / EmueraThread.End()
  -> legacy worker quiescence
  -> uEmuera resource cleanup
```

### ERB loading and execution

```text
Process.InitializeAsync()
  -> ParserMediator.Initialize / bind CompatibilityPlan
  -> Preload / HeaderFileLoader
  -> ErbLoader.LoadErbFilesAsync()
  -> LogicalLineParser.ParseLine()
  -> LabelDictionary
  -> Process.DoScript()
       -> runSystemProc() or runScriptProc()
       -> ExpressionMediator / VariableEvaluator / FunctionIdentifier
       -> EmueraConsole output or InputRequest wait
```

### Display and input

```text
EmueraConsole.Print*
  -> PrintStringBuffer.Flush()
  -> ConsoleDisplayLine / parts
  -> GenericUtils.EnqueueUI()
  -> GenericUtils.FlushUI() inside EmueraMain._Process()
  -> EmueraContent / ConsoleRenderSurface / EmueraImage

pointer/key/input pad
  -> EmueraContent hit-test / input UI
  -> EmueraThread.Input()
  -> EmueraConsole.PressEnterKey()
  -> Process.Input* + DoScript()
```

### Core session switching

```text
selection + frozen CompatibilityPlan
  -> LegacySessionFacade / SessionCoordinator.Prepare
  -> SessionSwitchLease(candidate, generation/stamp)
  -> host LegacySessionBackend.StartAsync(...)
       -> launch registry resolves opaque selection
       -> applies legacy launch configuration
       -> starts legacy worker / canary
  -> CommitAsync() OR DisposeAsync()/abort
  -> generation guard rejects stale completions
```

## 线程 / 所有权矩阵

| 资源或状态 | 正确 owner | 可在哪个线程操作 | 观察者 / 传递方式 | 禁止事项 |
| --- | --- | --- | --- | --- |
| Godot `Node`、Control、Canvas、scene tree | `EmueraMain` / `EmueraContent` / GodotHost | Godot main thread | signals、UI queue、immutable status | worker/Core 直接持有或改 Node。 |
| `Texture2D`、`ImageTexture`、shader material、GPU resource | main-thread presentation | Godot main thread | queue / GPU work completion | 后台线程创建 Godot texture、修改 shared material。 |
| legacy script execution / `ProcessState` | `EmueraThread` worker | legacy worker | input event + console/state | main thread 直接改 ProcessState 或 variable storage。 |
| legacy global roots | `Program` / `GlobalStatic` | legacy initialization/execution with controlled host transition | session façade/host lifecycle | 未 quiesce worker 时 reset / new session。 |
| `InputRequest` / console wait state | `EmueraConsole` + Process | worker owns semantic state；main thread projects UI | `EmueraThread.Input` | UI bypass validation/metadata or directly writes variables。 |
| UI action queue | `GenericUtils` | producer can enqueue; consumer main thread | `ConcurrentQueue<Action>` | 动作不受预算控制地阻塞/执行重 I/O。 |
| Sprite decoded source data | content/sprite subsystem | background preparation allowed | `SpriteManager` main-thread upload pump | 将 decode 完成当作 texture 可安全显示。 |
| Core `GameSession` | Core composition / session coordinator | Core owner context; resource-specific rules | snapshot/DTO/effect | Godot node in session; stale completion writes. |
| Core `PixelStore` mutation | VM owner thread | creator/owner thread only | published snapshots to bridge | bridge/main thread mutating store arbitrarily. |
| `CompatibilityPlan` / port manifest | session | built/frozen before session use | immutable view/consumer | runtime mutation / unselected module alters profile. |
| save candidate | save service / explicit commit boundary | Core/legacy owner per codec path | candidate + atomic commit evidence | in-place write of user original save. |

## `GenericUtils` queue contract

`GenericUtils` is the high-frequency cross-thread bridge for legacy UI work:

- `SetMainThread()` records the main thread during launcher/main scene setup.
- `EnqueueUI(...)` wraps an action and pending counters in a `ConcurrentQueue<Action>`.
- `FlushUI()` is invoked by `EmueraMain._Process()` and consumes under platform-specific action/time limits.
- `FlushLogs()` remains compatibility entry; the newer diagnostics router performs logging at its own policy boundary.
- `WaitForUiFrameAfter(...)` can coordinate producer timing with processed UI frame generation; use sparingly to avoid deadlocks.

**Never call a UI queue action synchronously from a path that is itself waiting on the worker or holds a lifecycle lock unless the full wait graph is known.**

## Generation, stale work and session isolation

The migration path introduces explicit identity types (`SessionGeneration`, `SessionOperationId`, `SessionStamp`) because legacy runtime historically contains process-wide statics. Correct sequence:

1. Build a candidate with a new generation.
2. Start/prepare it through controlled host/backend boundary.
3. Commit only after candidate succeeds and is still current.
4. Abort/dispose candidates without changing current session.
5. Reject/detach any old-generation input/audio/resource/display completion.
6. Stop old worker before Godot-side node/resource cleanup.

This does **not** prove full state isolation exists. It is a guardrail around a legacy process-wide runtime while M1 evidence remains incomplete.

## 允许的依赖方向

```text
Presentation (Godot UI) -> intent DTO/input submission
Application/Host        -> session orchestration + projection
Core                    -> pure contracts/state/effects/ports
Legacy runtime          -> legacy parser/VM/data/view/content
```

More concretely:

| Allowed | Why |
| --- | --- |
| Godot host → Core | Host composes a Core session and projects snapshots/effects. |
| Godot host → legacy runtime | Legacy is default behavior owner; host starts/stops it under lifecycle rules. |
| legacy runtime → `GenericUtils` / presentation intent | Worker can enqueue UI work and request input without owning Nodes. |
| Core → pure .NET standard library | Core must stay engine-independent. |
| Core → port interfaces | Platform work goes through typed contracts, not Godot calls. |

| Forbidden | Why |
| --- | --- |
| Core → `GodotSharp` / Node / Resource / RID | Breaks headless contract boundary and main-thread ownership. |
| Core → absolute game path / host filesystem policy | Game selection/path resolution belongs to Host allowlist. |
| Godot UI → direct Core variable mutation | Presentation emits intent; owner/orchestrator applies state changes. |
| new Core route → removal/bypass of legacy fallback | No phase gate grants that authority. |
| old generation queue completion → current session | Cross-game state/resource corruption risk. |
| direct new profile `if` in parser/VM hot path | Hides compatibility policy and defeats registry/plan evidence. |

## Static roots to treat carefully

| Root | Why it is risky |
| --- | --- |
| `MinorShift.Emuera.Program` | Directory/config/profile/compatibility state is widely read by legacy modules. |
| `GlobalStatic` | Holds cross-module legacy process objects and is called from many locations. |
| `EmueraThread.instance` | Process-wide worker lifecycle; starts cannot overlap a still-live old thread. |
| `EmueraContent.instance` | Global presentation access point; only valid for active Godot scene/main thread. |
| `SpriteManager` / animation caches | Can retain decoded/texture state beyond a line/session unless explicit cleanup happens. |
| `GenericUtils` static queue/config/router | Application lifetime; session-specific data must be reset/scoped correctly. |

## Review questions for cross-layer changes

Before modifying an edge crossing two rows in the ownership matrix, answer:

1. Which layer owns the truth before and after the change?
2. Does the new call run on the Godot main thread, legacy worker, Core owner thread or an async continuation?
3. Is the payload immutable and generation-bound?
4. What happens if the session changes while the action is queued?
5. How do cancellation, timeout and cleanup behave?
6. Which legacy fallback remains available and how is rollback verified?
7. Which test/evidence layer proves the observable behavior?

## Related pages

- Scenes/start/stop: [`02-Startup-and-Lifecycle.md`](02-Startup-and-Lifecycle.md)
- Interpreter/data details: [`03-Legacy-Interpreter.md`](03-Legacy-Interpreter.md), [`04-Legacy-Data-and-Expressions.md`](04-Legacy-Data-and-Expressions.md)
- UI/resources: [`05-Console-Rendering-and-Resources.md`](05-Console-Rendering-and-Resources.md)
- Core/host contracts: [`06-GodotHost-and-Core.md`](06-GodotHost-and-Core.md)
- commands/diagnostics: [`08-Operations-Testing-and-Diagnostics.md`](08-Operations-Testing-and-Diagnostics.md)