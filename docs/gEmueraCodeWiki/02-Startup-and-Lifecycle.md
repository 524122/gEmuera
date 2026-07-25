# 启动、场景与生命周期

## 场景入口

`project.godot` 是唯一应用级入口：

```text
run/main_scene = res://first_window.tscn
Autoload:
  AppBootstrap   -> Scripts/GodotHost/AppBootstrap.cs
  PlatformGateway -> Scripts/GodotHost/PlatformGateway.cs
application flags:
  prototype_runtime = true
  typed_ports = true
  pixel_store = true
```

`first_window.tscn` 的根节点是 `Control`，挂载 `FirstWindow.cs`。`main.tscn` 的根节点是 `Node`，挂载 `EmueraMain.cs`，并含有 Core prototype sidecar nodes。

```text
project.godot
  ├─ AppBootstrap (autoload)
  ├─ PlatformGateway (autoload)
  └─ first_window.tscn
       └─ FirstWindow : Control
            └─ selected game/profile
                 ↓ scene transition
              main.tscn
                ├─ Main : EmueraMain                    ← legacy default path host
                ├─ PrototypeRuntime : PrototypeRuntimeNode
                ├─ PrototypeStatusView : CanvasLayer
                ├─ PrototypeInputBridge : Node
                ├─ PrototypeAudioBridge : Node
                ├─ PrototypeResourceBridge : Node
                └─ PrototypeCommandPanel : Control
```

## Application-scoped Autoload

### `AppBootstrap`

文件：`Scripts/GodotHost/AppBootstrap.cs`

- 在 `_Ready()` 中 deferred 初始化，读取 `project.godot` 的 `application/prototype_runtime`、`typed_ports`、`pixel_store`。
- 只发布 feature flag 和 `BootstrapReady` / `BootstrapFault` 信号。
- **不持有游戏路径、脚本变量、资源缓存或 session 业务状态。** session 只能在已选定游戏后的 scene host 中创建。

### `PlatformGateway`

文件：`Scripts/GodotHost/PlatformGateway.cs`

- 在 `_Ready()` 初始化平台 id 与 `platform.mobile`、`platform.android`、`platform.desktop`、`platform.saf`、`platform.sqlite` 等 capability snapshot。
- 通过 `_Notification` 发布 application pause/resume。
- `platform.saf=true` 只是 capability surface；不能据此宣布 Android SAF adapter、撤权恢复、APK 或真机 gate 已完成。

## 启动器：`FirstWindow`

文件：`Scripts/FirstWindow.cs`

### 责任

| 责任 | 实现要点 |
| --- | --- |
| 建立 launcher UI | `_Ready()` 调用分辨率/帧率 helper、设置主线程诊断、构建控件、应用 SafeArea，并绑定 viewport size / Android permission 事件。 |
| 有界地发现游戏 | 固定扫描深度与数量上限，避免 Android 首屏因递归枚举异常目录而卡顿。 |
| 保存选择 | 通过 `user://launcher.cfg` 保留最后的游戏路径、profile、advanced compatibility 与手工 profile 选择。 |
| 产生启动输入 | `LauncherGameEntry` 绑定 `DisplayName`、`GameRoot`、`ProfileId`、来源；UI 标签或游戏名不能反推 profile。 |
| profile 路由 | 公开 profile 常量包括 `v24pure`、`snake`、`erafl`；旧 `auto` 值仅为兼容已有 launcher 配置保留，启动器不再做自动猜测。 |
| 转入主场景 | 选定可用 Era 游戏目录与 profile 后，设置静态选择信息并进入 `main.tscn`。 |

### 启动器的不变量

- 游戏目录路径与由目录路由得出的 profile 属于 launcher owner；`ItemList` 只存显示/选中状态。
- M0 runner-only 的 `ConfigureM0RunnerSession(...)` 不写 `launcher.cfg`，不能影响下次交互启动。
- Android 权限、SafeArea 与首屏日志属于启动器生命周期；还未进入 `EmueraMain` 时也应能留下诊断 breadcrumb。

## 默认 legacy 启动链

```text
FirstWindow selection
  -> main.tscn / EmueraMain._Ready()
  -> GenericUtils.SetMainThread() + diagnostics/UI bootstrap
  -> EmueraMain.Run()
  -> EmueraThread.instance.Start(debug, useCoroutine)
  -> worker thread: EmueraThread.Work()
  -> MinorShift.Emuera.Program.Main()
  -> resolve game root + csv/erb/resources/debug directories
  -> ConfigData / JSONConfig + config-dependent policies
  -> uEmuera.Application.Run(MainWindow)
  -> MainWindow.Init()
  -> EmueraConsole + Process initialization
  -> ERB load / parse / execute
```

`EmueraThread` 的同步细节见 [`07-Dependencies-and-Threading.md`](07-Dependencies-and-Threading.md)，ERB 初始化细节见 [`03-Legacy-Interpreter.md`](03-Legacy-Interpreter.md)。

### `EmueraMain`

文件：`Scripts/EmueraMain.cs`

`EmueraMain` 是 legacy Godot 主场景 host，拥有或协调：

- 主线程登记、日志与浮动诊断 panel 初始化；
- 启动/停止 legacy worker，维护 `working`、restart/clear request；
- 每帧 `GenericUtils.FlushUI()`、文本/GPU 队列、sprite 清理与其他线程纹理上传；
- 根据 console 是否等待输入显示/隐藏 `EmueraContent` 输入控件；
- GPU ColorMatrix / text render 的跨线程工作项与主线程完成信号；
- `_ExitTree()` 中通知诊断关闭、停止 legacy session、清理 uEmuera 资源。

它是 Godot `Node`，因此任何 UI、纹理或 GPU 操作仍必须在主线程完成；不要将它迁入 Core。

### `EmueraThread`

文件：`Scripts/EmueraThread.cs`

`EmueraThread` 是 process-wide singleton 形式的 legacy worker wrapper。

| API / 状态 | 作用 |
| --- | --- |
| `Start(bool debug, bool useCoroutine)` | 在 lifecycle lock 下确认旧线程已退出，创建 `ManualResetEventSlim` 和 worker `Thread`，开始 legacy session。 |
| `End()` | 将 `running` 置为 false、唤醒输入等待、等待 worker 安静退出，并只在确认线程已停止后释放资源。 |
| `IsSessionActive` | 给 host lifecycle 用的真实 session/worker 活跃状态；不要将 console 的“正在处理脚本”状态误用为生命周期状态。 |
| input event / `Input(...)` | 将主线程 input 传给 console/worker，并唤醒等待循环。 |

停止时要尊重 `LegacyThreadQuiescence` 和 join timeout。若 worker 未退出，不能清理其 event/reference 后立刻启动新 session。

## Program 与 legacy 初始化

文件：`Scripts/Emuera/Program.cs`、`Scripts/Emuera/GlobalStatic.cs`、`Scripts/uEmuera/Window.cs`

`Program.Main()` 仍负责 legacy 环境初始值：

- 基于 `FirstWindow.SelectedGamePath` 解析 `ExeDir`、`CsvDir`、`ErbDir`、`ContentDir`、`DebugDir` 等兼容目录；
- 读取 legacy config / JSON config，应用 Android window-width 与 FPS policy；
- 验证 `csv`、`erb` 目录，建立 `MainWindow`，经 `uEmuera.Application.Run()` 调用其 `Init()`；
- 静态 `Program` / `GlobalStatic` 被大量 legacy 模块使用，因此 session 切换必须受 host/LegacySessionFacade 的 generation 与静态状态审计保护。

## Core prototype sidecar 生命周期

`main.tscn` 中 Prototype nodes 与 legacy `EmueraMain` 同场景，但职责分离：

```text
PrototypeRuntimeNode
  -> constructs / observes CoreApplicationRuntime + GameSession
  -> attaches generation-scoped bridges
     -> PrototypeInputBridge
     -> PrototypeAudioBridge
     -> PrototypeResourceBridge
  -> projects status
     -> PrototypeStatusView + PrototypeCommandPanel
```

- Prototype 的 Reload / Toggle / Detach 用于回归 Core session/bridge 生命周期。
- 该路径必须保持 **observational / candidate** 性质；它不能无证据地消费或替代 legacy 游戏输入、解释执行、保存或显示。
- `LegacySessionBackend` 是受控 canary 的 host adapter：Core 只给 opaque `SessionSelection` / frozen `CompatibilityPlan`，实际路径解析与 legacy thread 启停留在 Godot host allowlist (`LegacySessionLaunchRegistry`) 内。

## 停止、重启与切换

| 场景 | 正确 owner / 顺序 |
| --- | --- |
| 应用退出 | `EmueraMain._ExitTree()` 通知诊断 → 停止 legacy session → 清理资源；host/bridge 释放各自 Godot node。 |
| 当前场景重载 | `EmueraMain._Process()` 处理 restart request 并让 SceneTree 重载；不能留下存活 legacy worker。 |
| legacy canary 切换 | `LegacySessionFacade` / `SessionCoordinator` 管 candidate、lease、commit/abort；`LegacySessionBackend` 在 host 边界停止/启动 worker。 |
| Core session dispose | `GameSession.DisposeAsync()` 清理其 SaveService、ResourceRuntime、PixelStore；它是 CLR object，不释放 Godot Node。 |

**核心不变量：** 旧 generation 的 completion、资源或 UI 操作不得写入新 session；默认 legacy 路径应始终可回退。