# 整体架构设计

## 目标与非目标

目标是在保持上游 XEmuera 语义、旧 gEmuera 已验证扩展和实际游戏行为的前提下，把解析、变量、VM、存档和完整显示模型逐步放入可独立测试的纯 .NET Core，将 Godot 限定为生命周期、输入、渲染、音频和平台文件访问的适配层。三层事实规则见 [GEmueraBaseline](GEmueraBaseline.md)，迁移顺序见 [MigrationPlan](MigrationPlan.md)。

本设计不预设“现代化后更合理”的行为，不把扩展存档冒充原版格式，不承诺尚未测量的固定帧率，也不让 Godot Node、Texture 或 Variant 渗入 Core。

## 分层架构

```text
Presentation: Godot Control / Canvas / Audio / File Picker
      │ intent / Variant-safe signal
      ▼
Bridge: orchestrator / main-thread queue / platform ports / DTO mapping
      │ CLR method / port / C# event
      ▼
Core Logic: parser / resumable VM / variables / save codec / display builder
      │ owns
      ▼
Session Data: GameConfig / VariableStore / resources / history / snapshots
```

依赖只向下。Core 不知道 Bridge/View；View 不直接访问 VariableStore；Bridge 不复制业务规则。Godot 层的具体场景树、信号方向和 Theme 规则见[GodotIntegration](GodotIntegration.md)。

## 所有权矩阵

| 状态 | 唯一所有者 | 允许修改者 | 观察者 | 生命周期 |
| --- | --- | --- | --- | --- |
| 当前会话引用/generation | `SessionCoordinator` | 切换事务 | MainOrchestrator、PlatformGateway | 应用级 |
| VM frame/调用栈/等待 | `GameSession.Vm` | 专用 VM owner thread；未来可选 `Step/Resume` | Bridge 只读 batch/result | 会话级 |
| 变量与角色 | `VariableStore` | 变量指令/受控 load commit | save snapshot、显示 effect | 会话级 |
| GameConfig | `GameSession` | 候选会话构造阶段 | VM、Bridge | 会话级只读 |
| CompatibilityPlan/DialectPlan | `GameSession` | 候选 resolver/builder；提交后冻结 | Parser、VM、Save、Resource、诊断摘要 | 会话级只读 |
| 逻辑资源描述 | `ResourceCatalog` | 候选资源加载器 | VM、ResourceBridge | 会话级 |
| 脚本可观察 CPU pixels | `GameSession.PixelStore` | VM owner thread 的 G/Sprite 操作 | 表达式、纹理投影器 | 会话级权威状态 |
| Godot Texture/RID | `ResourceBridge` | 主线程 revision 投影器 | ConsoleView/CBG | 会话视图级派生缓存 |
| Display 历史 | `DisplayHistory` | VM effect reducer | ConsoleView | 会话级 |
| 输入请求 | `InputCoordinator` | VM 创建；原子完成器完成 | InputPanel | 会话级 |
| 保存快照 | `SaveService` | snapshot builder | background serializer | 单次操作 |
| 音频播放节点 | `AudioBridge` | 音频 effect handler | UI 状态投影 | Bridge 级，generation 隔离 |
| UI Theme/缩放 | View root | Theme/Accessibility settings | 所有 Control | 应用级资源 |

任何状态如果无法回答“谁拥有、谁修改、谁观察”，不得进入实现。

## 应用级服务与 Autoload

Autoload 仅保留应用级编排，目标最多四个：

1. `AppBootstrap`：验证工具链/配置、创建 Main、显式初始化其他服务。
2. `PlatformGateway`：桌面、Android、iOS 回调；只返回 platform token/stream，不保存游戏业务。
3. `SessionCoordinator`：切换锁、generation、候选会话原子提交。
4. `Telemetry`（可选）：脱敏诊断和性能采样。

Save、Resource、VM、GameConfig、DisplayHistory 和输入请求均归 `GameSession`。AudioBridge 可跨场景保留节点，但必须按 generation 清空播放状态；该决定在音频 ADR 中记录。Autoload 不在 `_init()` 互相访问，按显式初始化顺序启动，避免初始化环。

## 组件通信

遵循“信号向上、调用向下”：MainOrchestrator 调用 SessionHost/Console/Input/Audio；组件只发 signal 告知意图或结果；同级组件不直接引用。Core 通过普通返回值、port 和 C# event 通信，不使用 Godot signal。

全局 signal 仅用于应用生命周期，例如 `session_committed(generation)`、`application_paused`、`fatal_error_presented`，总数保持在 15 以下。会话内高频显示、输入和资源结果走直接队列/方法，不进入全局总线。

## 目标启动流程与迁移入口

```text
Godot boot
 → AppBootstrap validates lock/capabilities
 → PlatformGateway initialize
 → Main.tscn instantiate and F6-safe stubs replaced
 → SessionCoordinator.SwitchGameAsync(default selection)
 → create complete default config
 → resolve dialect modules/capabilities → freeze CompatibilityPlan
 → open bounded source → decode/parse → build candidate
 → validate resource catalog + scripts + VM initial state
 → compare generation → atomic commit
 → attach Bridge → start dedicated VmHostThread
```

启动失败分为应用配置失败、平台能力缺失、游戏包拒绝和脚本错误。只有候选会话验证通过才设置 `CurrentSession`；此前 UI 保持加载/错误场景。迁移期不是另建空工程后一次替换，而是从旧 Godot 启动链通过 `LegacySessionFacade`、`LegacyConsoleAdapter` 和 feature flag 逐段导流。

## 游戏切换事务

`SwitchGameAsync` 返回 `Task<SwitchResult>`，禁止 `async void`。过程：

1. 在长加载锁之外原子递增 generation，并立即交换/取消上一候选 CTS；后请求不等待前请求完成即可取消它。
2. 从完整默认值创建新 Config；不复用旧对象。
3. 打开受限文件源，构造独立候选 Session；候选之间不共享可变 cache。
4. 解析、加载资源描述、验证脚本和初始 VM；所有后台结果携带 generation。
5. 加载完成先比较 generation/取消，再进入只覆盖交换动作的短 commit gate。
6. gate 内再次比较并原子交换 `CurrentSession`、发布 committed generation，随后立即退出 gate。
7. gate 外让 Bridge detach/attach，再取消旧输入/音频/资源任务并释放资源。

失败/取消只销毁候选。旧会话在新会话提交前一直可用，避免空窗和半加载状态。详见[StateIsolation](StateIsolation.md)。

## 线程模型

主线程拥有 SceneTree、Node、Control、AudioServer 和 Godot Resource。迁移期及默认生产候选由一个专用 VM owner thread 串行拥有 VM、变量、RuntimeDataStore 和 PixelStore；它与主线程只交换不可变 batch/completion。其他 worker 只接收不可变快照或独占流。所有队列有容量、背压、generation 和 operation id。

纯 Core 不等于 VM 必须在 Godot 主线程。主线程 cooperative VM 只有完成 Regex/XML/Map/DT/SQLite/文件/数组/Graphics/插件/lazy ERB 的 yieldability 审计并把 effect/layout/upload 一并纳入预算后才可实验。禁止非 owner worker 修改 VariableStore、同一 Resource 多线程加载、在 `_Process` 查询造成 server stall，或用 `call_deferred` 掩盖初始化依赖。详见 [ExecutionContract](ExecutionContract.md)。

## 效果模型

VM 指令不直接调用 UI、音频或文件系统，而产生 typed effect：

| 行为 | 逻辑状态位置 | 完成模式 |
| --- | --- | --- |
| Display/CBG | Core history/layer revision | `CommitThenProject` |
| 输入/NF/WAIT | InputCoordinator + continuation | `WaitPort` |
| G 像素读写 | Core PixelStore | `CoreImmediate`；纹理另行投影 |
| 文件 decode/GSAVE/GLOAD/文本/枚举 | typed platform/storage port | `WaitPort`；迁移期仅 VM thread 可 bounded blocking |
| SAVE/LOAD | SaveService candidate/transaction | 按兼容事实 `WaitPort`，失败回 VM |
| SQL | RuntimeDataStore/DB port | 内存操作 immediate；I/O bounded blocking 或 wait |
| 音频 | logical command + AudioBridge | 逐指令由 fixture 裁决 fire/wait/error |
| 插件 | 默认禁用 | trusted desktop 才允许 bounded call；不宣称沙箱 |
| Application | SessionCoordinator/AppBootstrap | 顺序屏障后处理 |

`CommitThenProject` 行为先更新 Core 可观察状态再由 Bridge 投影；`WaitPort` 在成功 completion 前不推进到要求结果的下一指令；`FireAndContinue` 只用于有证据表明失败不回脚本的行为。完整顺序以 ExecutionContract 为准。

## 设计不变量

- Core 项目无 GodotSharp 项目/包引用，公开 API 无 Godot 类型。
- 所有脚本调用路径通过同一 VM runner；M2–M5 允许专用线程中的旧同步 runner，主线程模式不得存在同步跑到底旁路。
- 指令/函数/变量/行为注册来自当前 Session 的冻结 CompatibilityPlan；禁止进程级 profile 和 last-wins 静态注册。
- View 永远不直接写变量或会话配置。
- 外部内容在候选上下文中解析，失败不污染当前会话。
- 所有延迟结果携带 request id 和 session generation。
- 原版格式、扩展格式、平台缓存使用不同入口和标识。
- Display DTO v2 完整保留 div、srcb、定位、盒模型和事务语义；渲染后端经 ADR/benchmark 选择。
- 外部插件默认关闭；任何启用都记录平台、hash、信任和完整进程权限警告。

## 关键 ADR

| ADR | 决策 | 状态 |
| --- | --- | --- |
| A-001 | Core 为独立纯 .NET assembly | Accepted，待项目证据 |
| A-002 | 专用 VM owner thread 为迁移和默认生产候选；主线程 cooperative 仅实验 | Accepted / experiment pending |
| A-003 | GameSession 拥有全部游戏级服务 | Accepted |
| A-004 | Upstream1808 与 GEmueraSnake codec profile 显式分离，禁止冲突字节自动猜测 | Accepted，fixture pending |
| A-005 | Display DTO v2 表达完整 div/srcb/事务；后端由原型决定 | Required，golden 后 Accepted |
| A-006 | Android SAF/import cache；iOS picker/security scope | Proposed，待真机 |
| A-007 | 音频节点可跨场景但状态按 generation 重置 | Proposed，待实现测试 |
| A-008 | PixelStore 是脚本可观察像素唯一真相 | Accepted，待像素差分 |
| A-009 | 外部 DLL 默认禁用；桌面显式信任也不视为沙箱 | Accepted |
| A-010 | 解释器兼容采用内置组合式 DialectModule + 数据型 CompatibilityPack；每会话冻结计划，外部 DLL 不作为默认方言接口 | Accepted，待 D0–D6 实现/差分 |

## 验收

架构测试必须证明 Core 无 Godot 引用、依赖无环、View 不反向修改数据、Autoload 初始化无互相 `_init()` 访问、会话切换竞态不会提交旧结果。任务状态见[AcceptanceTraceability](AcceptanceTraceability.md)。
