# 执行线程、同步语义与端口契约

## 当前裁决

纯 .NET Core 与 VM 所在线程是两个独立问题。迁移初期保留旧 gEmuera 的**专用 VM 线程**，先把 Godot/静态桥替换为不可变 DTO、有界队列和明确端口。主线程 cooperative VM 降为实验候选，只有全部指令完成 yieldability 审计、effect 派发纳入帧预算且 Android benchmark 不退化后才能切换。

| 阶段 | VM 所在线程 | 目的 | 退出条件 |
| --- | --- | --- | --- |
| M0/M1 | 旧 `EmueraThread` | 固定行为 baseline | runner/trace 可重复 |
| M2–M4 | 专用 `VmHostThread`，只依赖 Core | 隔离 UI 且避免同步重操作卡 Godot | 无 Godot/静态 UI 访问；队列有背压 |
| M5 实验 | 可选 cooperative scheduler | 测试统一 Step/Resume 的收益 | yield audit 100%、长尾帧和吞吐通过 |
| M6 | 依据 ADR 保留专用线程或切主线程 | 不以“更现代”为结论 | 桌面+Android+代表游戏报告 |

## 线程所有权

- VM thread：VmMachine、VariableStore、ExecutionContext、RuntimeDataStore、PixelStore 的可变状态。
- Godot main thread：SceneTree、Control、Texture2D/ImageTexture、AudioStreamPlayer、Theme、输入事件。
- workers：独占流或不可变 snapshot 上的编码、压缩、文件复制、可并行纯计算。
- SessionCoordinator：应用侧 generation/candidate 引用；不直接修改 VM 内部。

VM thread 与 main thread 之间只有有界 `VmToUiBatch`/`UiToVmCompletion` 队列。队列满时采用背压或合并允许合并的显示 revision，绝不能无界增长，也不能丢失输入、错误、保存或应用命令。

## 完成模式

每个指令/函数必须选择一种且只有一种模式：

| 模式 | 语义 | 适用例 |
| --- | --- | --- |
| `CoreImmediate` | 在 VM thread 完成状态修改并立即返回 | 变量、Map/XML/DataTable 纯内存操作、GGETCOLOR |
| `CommitThenProject` | 先提交 Core 状态，投影可延迟；下一指令可读新状态 | Display append、CBG logical layer、G 像素修改后的纹理上传 |
| `FireAndContinue` | 发命令后不等待；失败只诊断，必须有上游依据 | 部分无返回播放命令，需 fixture 确认 |
| `WaitPort` | 保存 continuation，收到 typed completion 后推进 | 文件选择、必须返回结果的 I/O、异步 decode |
| `VmThreadBlockingBounded` | 迁移期在专用 VM thread 同步调用有界端口 | 尚未异步化但原版同步返回的 GSAVE/GLOAD/SQL/插件；禁止主线程模式使用 |

矩阵新增 `completionMode`、`orderingPoint`、`failureReturn`、`threadOwner`。没有这些字段的行为不得进入迁移实现。

## 领域契约

| 行为族 | 默认模式 | 顺序要求 |
| --- | --- | --- |
| PRINT/HTML/CLEARLINE/data-only | CommitThenProject | Display revision 在下一条脚本指令前已进入逻辑历史；Godot 可稍后投影 |
| INPUT/TINPUT/NF/WAIT | WaitPort | 建立唯一 request 后挂起；completion 在 VM thread 消费 |
| SAVEGAME/SAVEDATA/SAVEVAR | WaitPort 或有证据的 bounded blocking | 成功返回前达到定义的 durability；失败回 VM，不得假成功 |
| LOADDATA/LOADGLOBAL | WaitPort + candidate commit | 解析失败不改当前 Store；成功后按 SYSTEM_LOAD/EVENTLOAD |
| SAVETEXT/LOADTEXT/ENUMFILES | WaitPort | 路径检查和结果必须在继续前确定 |
| G create/draw/get/set | CoreImmediate | PixelStore revision 同步递增；读回永远读 CPU 真相 |
| GCREATEFROMFILE/GLOAD | WaitPort；M2 可 bounded blocking | decode 完成并建立 CPU surface 后才返回成功 |
| GSAVE | WaitPort；M2 可 bounded blocking | 文件结果决定返回值 |
| CBGSET* | CommitThenProject | logical layer 同步更新；纹理投影可延后但 revision 不乱序 |
| PLAY*/STOP*/SET*VOLUME | 由逐项 fixture 决定 FireAndContinue/WaitPort | 兼容模式不自动 crossfade/steal |
| SQL | CoreImmediate（内存 reader state）或 VmThreadBlockingBounded（数据库 I/O） | reader/transaction/result code 在下一条前可观察 |
| 外部插件 | 默认 Unsupported；trusted desktop 为 VmThreadBlockingBounded | 完全信任模式的异常/超时明确；不能在主线程执行 |
| 时间/随机数 | CoreImmediate + injectable port/source | 保持调用次数和顺序；测试使用固定 clock/RNG |
| 配置/日志/debug | CoreImmediate 或 CommitThenProject | 配置可观察值先更新；日志允许异步写但不能无限排队 |

## PixelStore：唯一像素所有者

```text
Core/Runtime PixelStore
  handle -> PixelSurface(width,height,format,stride,owned bytes,revision)
                      │ immutable revision snapshot / dirty rects
                      ▼
Godot ResourceBridge texture cache
  (handle,revision) -> ImageTexture/RID
```

- PixelSurface 使用确定的 32-bit 格式、alpha 和混合规则；具体格式由与 gEmuera/XEmuera 的像素 fixture 决定。
- G、需要 SpriteGetColor 的源图和需要参与 GDraw 的 Sprite 都必须能解析到 CPU pixels。
- Bridge 不能成为脚本可观察像素的唯一副本；GPU readback 禁止进入普通执行路径。
- 图形操作在 VM thread 对 surface 加独占锁或通过串行 owner 执行；完成后产生 revision/dirty rect 投影。
- 大型合成先在专用 VM thread 保持同步兼容；后续 chunking 必须证明中间 revision 不被脚本/UI观察。

## Display batch 与帧预算

`VmToUiBatch` 包含 generation、first/last effect sequence、display transaction、CBG revisions 和诊断摘要。Godot 每帧分别预算：队列 drain、DTO reduce、layout、texture upload、draw preparation；不能只给 VM Step 计时。动态地图批次在 transaction commit 前不可见，data-only 更新只换 Interaction/generation 等非视觉数据。

合并不是 transport 的自由优化。已提交 transaction、REDRAW/显式 refresh、scroll intent、WAIT/INPUT request、completion、fault 和 application effect 全部保序且不可丢弃；多个 transaction 可装入同一 transport batch，但边界不可消失。只有同 generation/operation 的非语义进度/诊断可 latest-wins；data-only 与 texture revision 的条件合并必须有 CompatibilityMatrix reducer 和 fixture。完整规则以 [M0M2ImplementationBaseline](M0M2ImplementationBaseline.md)“队列、合并与保序语义”为当前实施基线。

## 故障回传

WaitPort completion 必须包含 operation id、generation、status、typed value/fault。Bridge 失败由 VM thread 按原行为转换为返回值、警告或异常；Bridge 不自行吞错。FireAndContinue 的错误只在明确不影响脚本控制流时允许进入诊断。

## Yieldability 审计

主线程 cooperative 候选至少覆盖 Regex、XML、Map、DataTable、SQLite、文件枚举/文本 I/O、大数组、Graphics、插件、lazy ERB、HTML、存档和压缩。每项记录最大同步时长、是否可分块、是否跨指令可观察中间状态、取消点和 Android p99。任一未分类同步路径存在时，主线程 VM ADR 保持 Rejected/Experimental。
