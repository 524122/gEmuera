# M3 纯 Core 抽取与解释器边界

## 文档地位

M3 是 M0-M2 通过后的后续设计，不是当前实施授权。M0-M2 仍由 [M0M2ImplementationBaseline](M0M2ImplementationBaseline.md) 单独控制；在另一条并行工作线修改 M0-M2 时，本章只提供接口、迁移顺序和验收要求，不移动旧文件、不删除旧 runner、不改变旧 View。

M3 的目标是把可独立验证的解析、变量、存档和运行时合同放入纯 .NET Core。它不以“重写解释器”为目标，也不要求一次性迁移全部 VM 指令。每一批迁移都必须能由 façade 回到旧实现，并用同一 trace 做双跑比较。

## 进入条件与范围

进入 M3 评审前必须满足：

1. M0、M1、M2 的阶段门禁已经有可访问报告；若门禁仍是 `Blocked`，只能提交本设计和无副作用的合同测试。
2. `CompatibilityMatrix` 中受 M3 影响的 source key、profile、fixture 和当前状态已经冻结。
3. `LegacySessionFacade`、generation guard 和 Display DTO 边界已有明确 owner；M3 不重新定义它们。
4. Core build 可以在没有 Godot 编辑器、SceneTree、Android SDK 和 GPU 的环境中运行。

M3 包含：

| Work package | 内容 | 产出 |
| --- | --- | --- |
| M3-CORE-01 | Core assembly 与依赖守卫 | 无 Godot 引用的项目、deterministic build、API 报告 |
| M3-CORE-02 | 文本/ERH/ERB/表达式解析边界 | parser port、诊断 DTO、旧/新 parse diff |
| M3-CORE-03 | VariableStore 与 continuation 数据 | typed value、局部/角色/全局 scope、快照合同 |
| M3-CORE-04 | SaveCodec/SaveSnapshot 纯逻辑 | profile-aware codec port、candidate parse/serialize |
| M3-CORE-05 | Legacy runner adapter | 旧线程仍可执行，新 Core 可旁路双跑 |
| M3-CORE-06 | 方言计划消费切片 | 只消费冻结的窄 registry/policy，不读取全局 Profile |

M3 不包含 PixelStore 的 GPU 投影、Godot Texture/Node 创建、Android SAF 主路径、主线程 cooperative VM、新 renderer 或旧代码删除；这些分别属于 M4-M7。

## Core 边界合同

Core 公开 API 只能使用 CLR 基础类型、不可变 record、`ImmutableArray`/`FrozenDictionary`、自有值类型和 typed ports。以下类型不得出现在 Core 的字段、参数、返回值或异常中：

- Godot `Node`、`Resource`、`Variant`、`Image`、`Texture2D`、`RID`、`Callable`、`Signal`。
- `SceneTree`、`AudioServer`、`DisplayServer`、`FileAccess` 和平台绝对路径。
- 可变全局字典、静态当前游戏名、静态 `CoreProfile` 或可被多个 Session 共享的缓存。

Core 通过下列窄边界交互：

```text
immutable input / content token / CompatibilityPlan
        -> parser / VM / VariableStore / SaveCodec
        -> typed result, effect, fault, snapshot
        -> Bridge-owned port completion
```

`ContentToken` 只标识经过平台层验证的来源，不等同于文件路径。`GameSession` 持有计划、变量、VM、SaveService、RuntimeDataStore 和诊断上下文；Core 服务不能自行寻找当前 Session。

## 抽取顺序

每个 work package 采用“复制合同、接 façade、双跑、再移动实现”的顺序：

1. 从旧符号和现有 fixture 生成行为清单，记录 owner、completion mode、ordering point、failure return 和 thread owner。
2. 在 Core 中建立最小接口和不可变输入，不先复制旧静态依赖。
3. 让 `LegacySessionFacade` 同时能调用旧实现和候选 Core；默认仍指向旧实现。
4. 对纯函数执行同进程双跑；对有状态 VM 使用独立进程或独立 Session，禁止共享 static/cache。
5. 差分通过后才把单一 source file 的 owner 改为 Core；保留 `Legacy*Adapter` 作为可回退路径。
6. 记录新旧程序集、source hash、fixture hash、plan hash 和 feature flags，更新 [CompatibilityMatrix](CompatibilityMatrix.md)。

### 解析与诊断

Parser 只输出逻辑行、表达式树、source span 和 typed diagnostic。它不读取 UI、不创建 DisplayPart、不打开图片，也不决定 Android 输入。HTML/BBCode 的显示模型仍按 [MarkupSystem](MarkupSystem.md) 和 M2 DTO 合同输出；M3 可迁移 parser，但不能简化 div、srcb、负坐标或未知标签策略。

### VariableStore 与快照

变量存储由一个 owner thread 串行修改。快照是不可变、带 generation/sequence 的值集合；保存和诊断只能读取快照，不持有 VariableStore 内部数组。多维数组使用 checked 索引和设备档位限制，稀疏值不能因为迁移而改成密集分配。

局部变量、ARG/RESULT、Float/Ref 和 VarExt 域必须保持显式 scope。`Load` 先生成 candidate store，验证所有类型和保存域后才 commit；解析失败、版本冲突或取消不能修改当前 Session。

### SaveCodec

M3 只抽取 codec 和 snapshot 逻辑，不自动选择冲突 profile。`Upstream1808`、`GEmueraSnake` 和未来扩展使用独立 `SaveProfileId`；`Float=0x20..0x23` 等冲突继续由 [SaveFormat](SaveFormat.md) 负责裁决。无 sidecar/manifest 证据时只做只读预检，用户选择和转换必须生成副本、备份及报告。

## 方言与 erafl 兼容

M3 是让方言计划真正被 Core 消费的第一阶段，但不把 M0-DIA 静态报告误当作运行时完成。Parser、FunctionCatalog、InstructionCatalog、SaveCodecRegistry 和资源/输入策略只接收当前 Session 冻结的 `CompatibilityPlan` 窄视图。

eraFL 作为第二条行为基线，M3 至少要保留以下可被 Core 观察的输入：

| 兼容点 | M3 边界 | 不能做的简化 |
| --- | --- | --- |
| 动态地图 scope | Display effect 携带稳定 scope metadata | 不能按游戏名在 VM 外猜测 |
| `INPUTS ,1` 默认参数 | typed argument policy | 不能在 UI 层补默认值 |
| 空白区域整数输入 `-1` | input policy 的结果进入 trace | 不能把点击改成普通文本输入 |
| 未知 `<A>/<C>/<S>` | Markup policy 返回正文或诊断 | 不能静默丢弃原文 |
| `SPRITEDISPOSEALL 0` | 资源操作 effect 保留 mode | 实际资源清理在 M4 才投影 |
| Map/XML/DT/SQLite/VarExt | typed extension port 的逻辑合同 | 不能用 JSON 字符串替代类型 |

精确 base module、函数选择器、错误和 completion 语义必须由 v24/Snake/FL 两侧 fixture 决定。没有 fixture 的行为只能标 `LegacyBehaviorPending` 或 `Uncovered`。

## Godot 节点生命周期边界

M3 Core 不创建 Node。Godot 侧只有候选 Session 提交后，`SessionBridge` 才 attach Core handle；旧 Session 的 completion 在 generation 比较前不得解包为 Node、Texture 或 Control。

节点规则：

- `VmHostThread` 是 CLR service，不伪装 Node，也不在 `_Process` 中运行 VM。
- `MainOrchestrator`/`SessionBridge` 只持受控 session handle；`_ExitTree` 先停止消费 batch，再取消并等待任务，最后清空 handle。
- `UiBatchPump` 只能消费不可变 Core effect；不能直接访问 VariableStore 或保存候选。
- 组件通过 signal 向上报告，编排器向下调用；禁止 Core 或 View 保存彼此 Node 引用。
- M3 不使用 `call_deferred()` 修复初始化顺序；提交和接线依赖显式 `Attach`/`Detach` 阶段。

与 M0-M2 并行时，M3 只能读取 M2 DTO tee/capture 输出。任何修改 `EmueraConsole`、Canvas/Control、InputPanel 或旧线程的提交都必须保持 `display.dto`、`session.isolation` 和旧路径默认值不变。

## 内存与所有权

| 资产 | M3 owner | 规则 |
| --- | --- | --- |
| 解析树/逻辑行 | Core parser/session | 候选期间独占；提交后只读；按 session 释放 |
| 变量数组/字符串 | VariableStore | 不把内部数组交给 Bridge；快照按段共享并计账 |
| SaveSnapshot/压缩 buffer | SaveService operation | reservation token；完成/取消/fault 都释放 |
| plan/registry | GameSession | build 后冻结；不可跨 Session 共享可变 builder |
| 诊断/trace | 有界 recorder | 丢弃非语义采样，不丢 fault、顺序和输入结果 |
| 临时 Core 对象 | `RefCounted` 等价 CLR owner | 明确 Dispose；不得依赖 finalizer 做业务清理 |

所有大数组、解压、AST 和 save candidate 先向 `MemoryBudget` reserve。`GC.GetTotalMemory` 仅用于 managed 观察，不能替代 RSS/native/GPU 报告。M3 不为了降低 managed 分配而引入会改变顺序的共享池；池化必须有 ownership、清空和异常路径测试。

## 门禁与回退

M3 通过必须同时满足：

1. Core build 在无 Godot 环境 deterministic、warnings-as-errors，并由 architecture test 证明无 Godot 引用。
2. parser、VariableStore、SaveCodec、Map/XML/DT/SQLite 合同有 unit、fuzz 和旧/新差分报告。
3. v24、Snake、eraFL 代表 fixture 的 state、error、completion、effect sequence 一致；未覆盖项显式列出。
4. A→B→A、取消、stale completion 和 candidate load failure 不污染当前 Session。
5. `core.extracted` flag 关闭后报告回到 M0-M2 路径；旧 runner、Canvas/Control、Android 外部目录仍可启动。
6. managed/native/RSS 与 snapshot 峰值在对应设备档位内，reservation 无泄漏。

任何一项失败都回退 façade 到旧 assembly；不通过删除新代码来“修复”差分。M3 的 gate 记录 `executionStatus`、`gateStatus`、`blockerCode` 和完整报告 hash，不能只写“编译通过”。

## 交付与并行协作

M3 文档/合同可以与 M0-M2 runner、DTO 和静态库存并行准备，但必须遵循以下文件边界：

- 只新增 `src/Core`、`Contracts`、M3 tests 和本章引用的报告，不改 M0-M2 生成 JSON 的 schema。
- 不在 `Scripts/Emuera` 中批量重命名或移动文件；若并行 AI 正在修改同一旧文件，先生成 source hash 和 adapter patch，再等待阶段门禁。
- 不把 M3 设计状态写入 M0-M2 的 `executionStatus`；M3 初始状态为 `executionStatus=NotStarted; gateStatus=Blocked; blockerCode=PreviousGate:M2`。
- 每个 change set 附 `preparedBy`、`reviewedBy`、`approvedBy`、source/toolchain/runtime identity、rollback flag 和 uncovered 清单。

## 工程工单锚点

M3 的具体工单、状态迁移、证据包和 gate 公式统一遵循 [M3M7EngineeringExecution](M3M7EngineeringExecution.md)。本章仍负责 Core 语义和边界；执行协议不把其中任一包提前授权。

当前已经存在的纯 .NET 合同切片是 M3-CORE-01 的输入，后续实现必须扩展它，而不是创建同义的第二套会话或 VM 类型：

| 现有锚点 | 可复用事实 | 不能由此推断 |
| --- | --- | --- |
| src/Core/Compatibility | 冻结模块、CompatibilityPlan 与 registry 合同 | Parser、SaveCodec 或运行时 resolver 已接线 |
| src/Core/Session | generation、candidate、短 commit 与 LegacySessionFacade 的纯合同 | GlobalStatic、旧 EmueraThread 或 Godot View 已隔离 |
| src/Core/Runtime | interpreter factory、Step/Resume、effect/completion DTO | legacy Parser/VM 已迁入或 cooperative VM 已放行 |
| tools/core-contracts | Core build、hash、catalog、session race 与反向依赖 smoke | 代表游戏、存档、设备或回退报告已通过 |

M3-CORE-01 必须先为这些锚点建立 public API 与 architecture-boundary 报告；M3-CORE-02 到 M3-CORE-04 只能各自迁移一个可比较 owner；M3-CORE-05 才允许通过单一 adapter 接入旧 runner 双跑；M3-CORE-06 最后把冻结 plan 接到窄 consumer。任一包若需要同时修改 Scripts/Emuera、Godot View 与 Core，则应拆分或提交 ADR，而不是把跨层改动藏在 façade 中。

## 关闭报告

报告至少包含 `core-build.json`、`architecture-boundary.json`、`parser-diff.json`、`variable-diff.json`、`save-codec-diff.json`、`dialect-consumption.json`、`memory.json` 和 `rollback.json`。报告必须引用 [VerificationPlan](VerificationPlan.md)、[LifecycleMemory](LifecycleMemory.md)、[CompatibilityMatrix](CompatibilityMatrix.md) 的稳定 key；没有运行时证据的部分继续保持 `Mapped`、`Partial` 或 `Uncovered`。
