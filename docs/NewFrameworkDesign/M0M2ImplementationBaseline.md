# M0–M2 生产实施基线

## 文档地位

本文是当前唯一允许直接拆工单实施的迁移基线。它只批准按顺序完成 M0、M1、M2；[Architecture](Architecture.md) 等文档描述长期目标，[MigrationPlan](MigrationPlan.md) 描述全程路线，但 M3 及以后在本文门禁关闭前均不得开工。

阶段必须串行放行：M0 报告获批后才进入 M1，M1 隔离报告获批后才进入 M2。阶段内允许并行制作互不共享可变状态的 fixture、报告工具和只读适配器，但不得把后阶段抽象提前塞进旧 VM、渲染或 Android 路径。

### M1 名称消歧

本文中的 M1 专指把旧 GlobalStatic、Program 和 EmueraThread 包入 LegacySessionFacade，并把 generation guard 接到旧运行时的迁移阶段。已落地的 [M1CoreRuntimeContractSlice](M1CoreRuntimeContractSlice.md) 提供纯 .NET 合同、确定性 hash、会话候选协调和 smoke，并已有最小 Godot legacy backend/startup canary 接线；startup/parser 已绑定并校验 plan identity，但旧 Parser/VM、Godot View 或业务静态状态仍不消费 `CompatibilityPlan` 的冻结 registry/descriptor/policy 行为，也不构成 M1 阶段放行。

因此，合同切片可以作为接口和架构守卫的输入，但不构成 M1 阶段启动、放行或完成。下文所有 M1 的 executionStatus、gateStatus 和 blockerCode 均指 LegacySessionFacade 迁移阶段。

## 第一阶段边界

### 允许

- 维护和验证 M1-CORE-01/02 的纯 .NET 合同、最小 legacy bridge 与默认关闭 canary；不得把其结果扩张为旧 Parser/VM 的 plan/policy 接线。
- 固定旧 gEmuera runner、fixture、trace、artifact/game/toolchain hash 和报告 schema。
- 用 `LegacySessionFacade` 包住现有 `GlobalStatic`、`Program`、`EmueraThread` 和配置选择，不改变其内部语义。
- 在所有异步 completion 入口加 `generation + operationId` 校验，并验证 A→B→A 切换不泄漏。
- 从旧 `ConsoleDisplayLine`、`ConsoleButtonString` 和全部 Part 深复制出 DTO；旧 Canvas/Control 继续作为显示真相。
- 增加 feature flag、只读观测、差分和回退证据。

### 禁止

- 将 M1-CORE-01 合同切片扩张为生产 Core 抽取，或批量搬迁/重写 Parser、VM、变量和存档语义。
- 用 PixelStore 替换 `GraphicsImage`，或改变 G/CBG/Sprite 的同步返回和像素规则。
- 重写 layout、HTML parser、字体测量、hit test、Canvas/Control fallback。
- 以 SAF 替换当前可用 Android 外部存储路径，或改变现有输入/虚拟光标路径。
- 实现完整 Dialect manager、resolver 或大量策略接口；D0–D2 只做库存、会话快照和注册不变性。
- 删除旧 runner、renderer、platform adapter、兼容分支或正式回归资产。

任何越界需求必须提交 ADR，说明为何不能在 M0–M2 外解决、增加的行为差分、回退开关和批准人；未批准前保持 `Blocked`。

## 通用交付契约

每个阶段 PR/变更集必须只服务一个 work package。下表是 work package 关闭、评审或改变阶段状态时必须具备的交付契约；它不是要求 AI 的每一次内部编辑都重跑 APK、完整真实游戏 replay 或全部设备矩阵。

| 字段 | 要求 |
| --- | --- |
| Work package | 稳定 ID，例如 `M0-RUN-01` |
| Source identity | 源码 revision 或源码树 hash；无 Git 元数据时必须使用 tree manifest hash |
| Toolchain identity | Godot/.NET/JDK/Android SDK/NDK/export template 版本和 hash |
| Runtime identity | artifact/APK hash、游戏目录 manifest hash、配置 hash、profile/flags |
| Reproduction | 非交互命令、输入 trace、seed、locale、timezone 和超时 |
| Result | machine-readable report、日志/截图索引和明确 exit code |
| Compatibility | 影响的 CompatibilityMatrix keys；无影响也要写 `None` |
| Rollback | flag/旧 artifact/反向变更，以及回退后验证结果 |
| Uncovered | 未运行的平台、设备、游戏或异常路径，不得省略 |
| Ownership | `preparedBy`、`reviewedBy`、`approvedBy` 的可追踪身份与 UTC 时间；无人签署不得放行 |
| Gate decision | `Approved`/`Rejected`/`NeedsEvidence`、适用报告 hash、例外 ADR 和失效条件 |

大 APK、游戏包和原始日志可进入 artifact store；小型 fixture、manifest、hash、normalizer、报告和回归种子必须版本化。测试通过后不得按“清理临时文件”删除正式回归资产。

## 快反馈与阶段门分离

日常编辑遵循 [AIDevelopmentWorkflow](AIDevelopmentWorkflow.md) 的 Explore 和 FastLoop 路径：纯逻辑只跑定向合同/行为测试，单一 Godot 场景只跑相关 gdUnit4 套件，文档只跑文档守卫。一次 FastLoop 通过只能说明局部改动可继续，不得改变 M0、M1 或 M2 的 executionStatus、gateStatus 或兼容结论。

当改动触及 GlobalStatic、generation、Display transaction、存档、资源、Android 路径、真实游戏行为或旧/新 adapter 切换时，必须升级为 work package 检查。此时才需要本节的完整 identity、差分、回退、未覆盖项和 gate decision；不能以快速测试成功替代，也不能把全量门禁下放为每一行代码的默认成本。

## M0：固定旧 gEmuera 行为

### 目标

把当前可运行旧工程变成可重复比较的被测系统。M0 不引入会话抽象，不改变执行、显示、输入、资源、存档或平台行为。

### Work packages

| ID | 交付物 | 最小内容 | 验收 |
| --- | --- | --- | --- |
| M0-ID-01 | `BaselineIdentity` | source tree、toolchain、artifact/APK、游戏、配置、字体/资源 manifest hash | 同一输入能唯一定位所有字节来源 |
| M0-RUN-01 | legacy runner | 启动、输入重放、超时、退出码、诊断包收集 | 连续 3 次 canonical 语义结果一致 |
| M0-TRC-01 | trace schema | 输入、clock/RNG、wait、display commit、effects、errors | 顺序字段不被 normalizer 删除 |
| M0-FIX-01 | fixture manifest | upstream/legacy/game、profile、授权来源、期望报告 | 缺失 fixture 明确为 Uncovered |
| M0-DSP-01 | 显示基线 | Controls、Canvas、eraFL div/srcb/动态地图/data-only、截图/hit | 两个旧后端状态分别归档，不互相替代 |
| M0-SAV-01 | 存档基线 | 四 file type、profile、字节 hash/offset map、读写结果 | 原件只读；每个写测试使用隔离副本 |
| M0-AND-01 | Android 基线 | APK 安装/启动、目录、输入、虚拟光标、后台/恢复、日志 | 至少记录目标真机；未跑时 M0 Android 保持 Blocked |
| M0-PER-01 | 性能基线 | 固定场景、warm-up、p50/p95/p99、managed/native/GPU/RSS | 只作比较基线，不把单机结果写成产品承诺 |

M0-SAV-01 的 [legacy save baseline](generated/legacy-save-baseline.json) 当前仅完成 source-only 子集：5 个 legacy source hash、公共 header、4 file type、19 data type、11 sparse marker、入口和 `Float=0x20..0x23` conflict 已被机械锁定；自动 profile 选择保持 `Unbound`。它没有打开原始存档，也没有 offset map、四类读写结果、压缩样本或 profile round-trip，因此仍是 `InProgress / Blocked / EvidenceMissing / Partial`，不能满足本表中的“原件只读、隔离副本写入”验收。

### Canonical 输出

M0 runner 至少生成 `identity.json`、`state.json`、`display.json`、`effects.json`、`errors.json`、`timeline.json`、`semantic-trace.json`、`metrics.json` 和 `artifacts.json`。字段遵循 [VerificationPlan](VerificationPlan.md)；stdout 只作诊断，不能是唯一结果。

`trace.raw.json` 与 `trace.json` 保留全部事件和原 sequence；canonical normalizer 只允许移除绝对路径、进程/对象 ID 和非语义时间戳。`semantic-trace.json` 不是对 raw trace 的删除式 normalizer：它仅排除明确标成 `ui_projection` 的 Godot 队列传输批次，并为保留的脚本可观察事件重新编号。逻辑 `display_commit`、文本、错误类型、effect 相对顺序、wait/input 顺序、按钮 generation 和滚动意图均不可被归一化掉；未被明确标记的事件不得进入该排除规则。

### M0 放行门

以下全部满足才可进入 M1：

1. 旧 runner 和报告能在干净环境非交互运行，失败返回非零 exit code。
2. Snake 与 eraFL 代表 trace 已归档；上游/普通 profile 缺失时保持明确阻断项。
3. APK、游戏目录、工具链和配置 hash 可从报告追到 artifact。
4. 存档、HTML/Display、输入、G/CBG 至少已有不可变原始样本或明确的 fixture 获取工单。
5. 连续三次运行的语义报告一致；允许的性能波动不进入语义 diff。
6. M0 没有改变旧业务代码；若为可观测性必须插桩，默认关闭且已证明开关两侧语义一致。

## M1：LegacySessionFacade 与 generation guard

### 目标

本节的 M1 是运行时会话外壳阶段，不是 M1-CORE-01 合同切片。后者的 build/smoke 只能提供接口信心，不能替代本节的 runtime isolation 证据。

建立单一会话所有权边界，但仍由旧对象执行全部语义。M1 的成功标准是“旧实现被可靠包住”，不是“新 Core 已出现”。

### 所有权表

| 状态/入口 | M1 owner | 允许修改者 | 观察者/投影 |
| --- | --- | --- | --- |
| `Program.CoreProfile`/`IsSnakeProfile` | `LegacySessionFacade` 的兼容投影 | 仅 candidate commit adapter | runner/report |
| `GlobalStatic` 对象图 | 当前 facade generation | 旧初始化/清理流程，经 facade 调用 | 禁止新代码直接读取 |
| `EmueraThread` | facade | facade start/stop/input adapter | heartbeat/trace |
| config/setting.json 合并结果 | candidate snapshot | candidate builder | 旧 Config adapter |
| 异步操作表 | facade generation | 各 typed adapter | diagnostics |
| Godot Node/Resource | Godot 主线程旧 View | 旧 View | facade 只能持弱/受控句柄 |

`LegacySessionFacade` 不是万能 manager：它只编排生命周期、所有权、generation、旧入口和诊断；Parser、VM、资源、显示和存档业务逻辑继续留在原 owner。禁止把新策略判断堆进 facade。

### 切换不变量

1. 新 request 先原子发布 generation，再在 commit gate 外取消旧 candidate。
2. 长加载和旧会话 dispose 不持有短 commit gate。
3. completion 先比较 generation 和 operationId，再解包 payload 或创建 Godot 对象。
4. stale completion 只释放自己的 payload/reservation，不写 UI、VM、配置、缓存、日志面包屑或进度。
5. candidate 完整验证后才短暂 commit；旧 session 在 gate 外停止和释放。
6. `CancellationToken` 只负责尽快停止，不能替代 generation guard。

### M1 必测序列

- A→B→A：缺失配置字段恢复各自默认，profile/注册/字体/资源/存档根不串联。
- A 加载慢、B 后发先完成：只提交 B，A 的进度和资源 completion 丢弃。
- 切换发生在 INPUT/WAIT、图片 decode、音频、存档、文件 picker 和退出期间。
- candidate 初始化失败：当前 session 完全不变且仍可输入/保存/退出。
- 连续切换和重启后：线程、handle、Node、RID、缓存、SQLite reader、临时文件无增长。
- `session.isolation=false`：完整回到 M0 路径，M0 canonical 结果不变。

### M1 放行门

- M1-CORE-01 的 Core build、smoke 和无 Godot 依赖守卫可作为辅助输入，但不计为本阶段运行时门禁。
- 所有 mutable static 已进入库存：owner、初始化、reset、跨会话预期、fixture、迁移阶段齐全。
- 架构守卫阻止新代码绕过 facade 新增 `GlobalStatic`/`CoreProfile` 访问。
- A→B→A 和 stale completion 的确定性 schedule 测试通过。
- feature flag 双向切换及回退报告通过；关闭 flag 后与 M0 报告一致。
- Android 现有输入、目录和 renderer 路径没有替换。

## M2：无损 Display DTO 边界

### 目标

先证明旧显示模型可以无损、深复制、确定性地导出，再考虑让 View 消费 DTO。M2 不批准新 layout 或新 renderer。

### 两步切换

| 子阶段 | 数据路径 | 允许行为 |
| --- | --- | --- |
| M2.0 tee/capture | 旧 Display 对象同时送旧 renderer，并深复制为 DTO 供报告 | renderer 仍消费原对象；DTO 不回写、不控制滚动 |
| M2.1 canary adapter | 旧 VM → DTO → 现有 View adapter；旧直接路径仍可切回 | 只有 M2.0 tree/timeline golden 通过后启用 |

不得为了“让 DTO 更漂亮”丢弃旧字段。无法解释的旧字段先进入带 provenance 的 opaque/legacy metadata，并建立关闭工单；在 golden 证明无观察者前不得删除。

### 深复制不变量

- DTO 不持有 `ConsoleDisplayLine`、Part、Godot `Node/Resource/Texture/RID` 或可变集合引用。
- Line/Part/Div 子树、style、src/srcb、relative/absolute、depth、负坐标、盒模型、overflow、shape、按钮、locked X 全量复制。
- Interaction value、generation、input type、hit metadata 与视觉字段分离；data-only patch 不重建视觉树。
- 每个 transaction 带 generation、transactionId、effect sequence 范围、scroll intent、atomic visibility 和 provenance。
- 转换不得做 layout、字体测量、坐标取整、图片 fallback 或 HTML 再解析。
- 同一旧对象快照重复转换产生相同 canonical DTO hash。

### 队列、合并与保序语义

| 消息/边界 | 能否合并 | 规则 |
| --- | --- | --- |
| 已提交 `DisplayTransaction` | 否 | 可装入同一 transport batch，但 transaction 边界、顺序和 scroll intent 原样保留 |
| 同一未提交 transaction 内的同 Line data-only patch | 有条件 | 相同 generation/interaction key 且中间无读取、flush、wait 或 commit barrier 时可 last-write；保留 sequence 范围 |
| 普通 append/update/remove | 默认否 | 只有 CompatibilityMatrix 对具体 reducer 有 fixture 时才可合并 |
| 动态地图/BitmapCache | 仅提交前内部 reduce | commit 后作为 `AtomicVisibility` 整体可见；不能跨 WAIT/INPUT/强制 flush |
| REDRAW/显式 refresh/scroll intent | 否 | 是投影顺序屏障；不能跨越折叠前后 transaction |
| WAIT/INPUT/TINPUT/NF request | 否 | 先前 Display commit 必须已入队；请求唯一且不可丢弃/重复 |
| input/save/file completion、fault、application effect | 否 | 结果类消息反压，不 drop、不 latest-wins |
| 加载进度、诊断采样 | 是 | 仅同 operationId/generation latest-wins；不得携带脚本可观察状态 |
| texture projection revision | 有条件 | 同 handle 可跳到最新 revision，但不得改变对应 Display/CBG transaction 的逻辑顺序；CPU truth 不受影响 |

队列满时先停止 VM producer 或降低非语义诊断频率；不得通过删除结果类消息“保持流畅”。所有新增可合并类型都必须先在 [CompatibilityMatrix](CompatibilityMatrix.md) 增加 reducer、barrier、fixture 和失败语义。

### M2 放行门

- eraFL div/srcb/动态地图/data-only 的 DTO tree 和 timeline golden 与旧模型一致。
- Controls 与 Canvas 继续通过各自 M0 视觉/hit 基线；M2 不以两个后端互相替代。
- tee/capture 开关关闭时与 M0 一致；canary 失败可即时切回旧直接路径。
- 100k 历史转换的分配、队列深度和主线程预算有报告，但性能不能覆盖语义失败。
- DTO consumer 只负责 View 投影，不拥有 VM、Save、Resource 或 CompatibilityPlan。

## M0–M2 之后仍阻断的工作

即使 M2 通过，M3+ 仍须单独批准。至少以下证据存在后才评审对应阶段：

- Dialect：D0 完整库存、D1 plan snapshot、D2 未选择模块不变性和注册快照。
- PixelStore：像素格式、alpha/混合/ColorMatrix、返回值、revision 和内存预算 fixture。
- Save：两个 codec profile 语料、无证据 profile 用户流程和显式转换报告。
- Android SAF：与现有目录路径并行的 canary、导入/撤权/恢复真机报告和回退。
- 新 renderer 或 cooperative VM：各自独立 ADR、feature flag、Android p99 和完整差分。

## 变更控制与阶段状态

不得再用 `NotStarted/BlockedEvidence` 这类复合字符串混写执行进度和门禁。阶段记录必须拆成三个字段：

| 字段 | 允许值 | 含义 |
| --- | --- | --- |
| `executionStatus` | `NotStarted` / `InProgress` / `Passed` | 实际工作进度；`Passed` 只表示本阶段门禁已正式批准 |
| `gateStatus` | `Blocked` / `ReadyForReview` / `Passed` | 证据是否足以进入评审或已获批准 |
| `blockerCode` | `None` / `EvidenceMissing` / `PreviousGate` / `ReportInvalidated` / 稳定扩展码 | 为什么不能放行；不能把原因塞入 executionStatus |

`Passed` 必须链接不可变报告和 gate decision；“代码完成”“编译通过”“设计完成”均不能替代。报告失效、fixture hash 改变或回退路径不可用时，`executionStatus` 退回 `InProgress`，`gateStatus` 退回 `Blocked`，并记录 `ReportInvalidated`。

| 阶段 | executionStatus | gateStatus | blockerCode | 当前唯一可做工作 |
| --- | --- | --- | --- | --- |
| M0 | InProgress | Blocked | EvidenceMissing | runner、fixture、trace、hash、报告和必要的默认关闭插桩 |
| M1 | InProgress | Blocked | PreviousGate:M0 | 仅维护可回退的实验性 `LegacySessionFacade` / runtime guard；不得将其作为发布路径或放行 M2 |
| M2 | NotStarted | Blocked | PreviousGate:M1 | 无；仅可维护旧显示库存，不得切换数据路径 |

M1 已有最小 runtime 外壳实现，但 M0 未归档签署前它只能保持 `InProgress / Blocked / PreviousGate:M0`；不得把 build、Core smoke 或单次游戏启动写为 `Passed`，更不得启动 M2。单人维护时角色可由同一维护者承担，但报告仍须分别记录 prepared/reviewed/approved 决策，避免匿名或事后补写的 `Passed`。
