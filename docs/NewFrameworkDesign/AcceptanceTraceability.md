# 规格、任务与验收追踪

规格状态与证据状态必须分列，禁止再用 `Revised`、`Design complete` 或 `Static partial` 混合表达“写过文档”和“已经验证”：

- `specStatus`：`Draft` / `Documented`。只评价权威规格是否完整、无冲突、可追踪。
- `evidenceStatus`：`Uncovered` / `BlockedEvidence` / `ReadyForReview` / `Passed`。`Passed` 只能由可访问报告和 gate decision 给出。

文档完成不自动等于兼容测试通过；本表当前没有任何运行时 `Passed` 项。

## P0 追踪

| 任务 | 权威交付物 | specStatus | evidenceStatus | 关闭所需证据 |
| --- | --- | --- | --- | --- |
| P0-01 事实索引/矩阵 | [GEmueraBaseline](GEmueraBaseline.md)、[EvidenceIndex](EvidenceIndex.md)、[CompatibilityMatrix](CompatibilityMatrix.md) | Documented | BlockedEvidence | upstream/legacy/target 三集合生成与游戏差分 |
| P0-02 原版/legacy 存档 | [SaveFormat](SaveFormat.md)、[VariableSystem](VariableSystem.md)、M0-SAV-01 static baseline | Documented | BlockedEvidence | M0-SAV-01 已锁定 5 source、4 file type、19 data type、11 marker 和 0x20–0x22 conflict，但仍需四类 Upstream1808 + gEmuera float/VarExt profile 原始 fixture 往返、offset map 与冲突拒绝 |
| P0-03 读档/快照 | [SaveLoadSystem](SaveLoadSystem.md) | Documented | BlockedEvidence | 撕裂快照、半加载回滚测试 |
| P0-04 RESTART | [ScriptEngine](ScriptEngine.md)、[StateIsolation](StateIsolation.md) | Documented | BlockedEvidence | 嵌套/事件/等待差分 |
| P0-05 AppContents | [ResourceSystem](ResourceSystem.md) | Documented | BlockedEvidence | CSV 顺序、重复、动画、缺图 fixture |
| P0-06 Graphics/CBG/音频 | [ExecutionContract](ExecutionContract.md)、[InstructionRenderMap](InstructionRenderMap.md)、[AudioSystem](AudioSystem.md) | Documented | BlockedEvidence | PixelStore/完成模式/compat audio 逐项差分 |
| P0-07 HTML | [MarkupSystem](MarkupSystem.md)、[EvidenceIndex](EvidenceIndex.md) | Documented | BlockedEvidence | HTML→DisplayLine golden/fuzz |
| P0-08 输入/UPCHECK | [InputSystem](InputSystem.md) | Documented | BlockedEvidence | 边界时序与差分 |
| P0-09 编码/配置 | [EncodingSystem](EncodingSystem.md)、[ConfigSystem](ConfigSystem.md) | Documented | BlockedEvidence | UTF-8/CP932 语料差分、切换测试 |
| P0-10 独立 Core | [MigrationPlan](MigrationPlan.md)、[M1CoreRuntimeContractSlice](M1CoreRuntimeContractSlice.md)、[DependencyGraph](DependencyGraph.md)、[ProjectStructure](ProjectStructure.md) | Documented | BlockedEvidence | M1-CORE-01/02 的 Core csproj、profile 目录、architecture guard/smoke 和最小 legacy façade/canary 已通过，但 M0/M1 gate、完整架构测试和运行时双跑仍缺 |
| P0-11 可挂起 VM | [ExecutionContract](ExecutionContract.md)、[ScriptEngine](ScriptEngine.md) | Documented | BlockedEvidence | 默认专用 thread；主线程实验需全量 yieldability audit |
| P0-12 Godot API | [GodotIntegration](GodotIntegration.md)、[HowToRun](HowToRun.md) | Documented | BlockedEvidence | 工具链 lock + ApiSmoke build |
| P0-13 一致性 | [ConflictLedger](ConflictLedger.md)、本表 | Documented | ReadyForReview | 本地 guard 通过；CI artifact 与人工交叉审阅尚未归档 |
| P0-14 差分体系 | [VerificationPlan](VerificationPlan.md) | Documented | BlockedEvidence | 本地 legacy runner、typed trace、fixture catalog 与 eraFL 复杂页 Controls/Canvas PNG/45-hit 已运行；plan-binding repeat3 已证明两端 semantic report 可重复，且每次 screenshot/hit 均已 Captured，但 transport trace hash 仍不一致；仍缺 nested/srcb/dynamic-map、upstream/target harness、归档与签署 |

## P1–P3 追踪

| 任务 | 权威交付物 | specStatus | evidenceStatus | 当前限制 |
| --- | --- | --- | --- | --- |
| P1-01 虚拟控制台 | [RenderingSystem](RenderingSystem.md)、[PerformanceOptimization](PerformanceOptimization.md) | Documented | BlockedEvidence | 后端 ADR 和原型 benchmark 尚未执行 |
| P1-02 移动存储 | [HowToRun](HowToRun.md)、[SecurityLimits](SecurityLimits.md) | Documented | BlockedEvidence | Android/iOS bridge 与真机证据缺失 |
| P1-03 会话事务 | [StateIsolation](StateIsolation.md)、[M1CoreRuntimeContractSlice](M1CoreRuntimeContractSlice.md)、M0-SES-01 root inventory | Partial | BlockedEvidence | central root state 已静态盘点；M1 lease/legacy backend 已有 deterministic A→B→A、failure/cancel/stale rollback smoke、最小 Godot 接线、default-off canary，以及首等待/无输入的跨 game/profile cross-ABA observation；startup/parser 的 plan identity 与 legacy descriptor registry guard 已绑定，但其余 static、真实游戏输入/资源/音频/保存竞态、完整 rollback、Android rollout report、handler replacement 与 typed-policy behavior consumption 仍缺失 |
| P1-03a M1-IDENTITY-01 | [M1CoreRuntimeContractSlice](M1CoreRuntimeContractSlice.md)、`Test-InProcessSessionCycle.ps1` | CompleteForScope | ReadyForReview | startup/parser profile+canonical-hash hand-off、failed-start cleanup 与 ordering contract 已完成；descriptor registry presence guard 由独立 `M1-DESC-CONSUMPTION-01` 覆盖，不代表 handler/typed-policy behavior 或 parent M1 gate |
| P1-03b M0-SES-CTRLZ-01 | [StateIsolation](StateIsolation.md)、`Test-InProcessSessionCycle.ps1`、`legacy-session-state-inventory.json` | CompleteForScope | ReadyForReview | canary reset 与 inventory scanner 已清空/记录 CtrlZ undo inputs/save markers/random seed/flags；baseline inventory 仍保留 debug StackList 这一条件 root，不代表完整 static isolation |
| P1-03c M0-SES-STACKLIST-01 | [StateIsolation](StateIsolation.md)、`Test-InProcessSessionCycle.ps1`、`legacy-session-state-inventory.json` | CompleteForScope | ReadyForReview | `UEMUERA_DEBUG` canary reset 与 inventory scanner 已观察 `StackList.Clear()`；14 个中心 `GlobalStatic` 条目均有 reset site 分类，但 baseline runtime 仍为条件编译路径，不代表完整 static isolation |
| P1-03d M1-DESC-CONSUMPTION-01 | [M1CoreRuntimeContractSlice](M1CoreRuntimeContractSlice.md)、`LegacyCompatibilityPlanConsumption.cs`、`CoreContractSmoke`、`Test-InProcessSessionCycle.ps1` | CompleteForScope | ReadyForReview | legacy registry 建成后消费冻结 instruction/function descriptor，模块归属和缺失 descriptor 均 fail-closed；真实 cross-ABA runner 通过。仍不代表 handler replacement、typed-policy 行为、完整隔离或 parent M1 gate |
| P1-04 事务存档 | [SaveLoadSystem](SaveLoadSystem.md) | Documented | BlockedEvidence | 平台替换/崩溃测试缺失 |
| P1-05 威胁模型 | [SecurityLimits](SecurityLimits.md)、[ResourceSystem](ResourceSystem.md) | Documented | BlockedEvidence | 恶意语料未建立 |
| P1-06 缓存/内存 | [LifecycleMemory](LifecycleMemory.md) | Documented | BlockedEvidence | 压力报告缺失 |
| P1-07 音频取消 | [AudioSystem](AudioSystem.md) | Documented | BlockedEvidence | 快速换曲测试缺失 |
| P1-08 错误恢复 | [ErrorRecovery](ErrorRecovery.md) | Documented | BlockedEvidence | 平台 watchdog/OOM 证据缺失 |
| P1-09 性能预算 | [PerformanceOptimization](PerformanceOptimization.md) | Documented | BlockedEvidence | 目标硬件数据缺失 |
| P1-10 构建能力 | [HowToRun](HowToRun.md) | Documented | BlockedEvidence | Godot 未安装/未锁定；不能关闭 |
| P2-01 字体/国际化 | [RenderingSystem](RenderingSystem.md)、[MarkupSystem](MarkupSystem.md) | Documented | BlockedEvidence | golden screenshot 缺失 |
| P2-02 限制/迁移 | [KnownLimitations](KnownLimitations.md) | Documented | ReadyForReview | 稳定 key 已关联；仍需 CI/人工审阅 |
| P3-01 文档收尾 | 全部文档、[doc guard](../../tools/doc-guards/README.md) | Documented | ReadyForReview | 本地 guard 已通过；CI artifact/人工签署缺失 |

## M3-M7 后续阶段与工程执行协议追踪

[M3M7EngineeringExecution](M3M7EngineeringExecution.md) 规定未来 work package 和证据包的形状，本身不是运行时证据，也不改变本表各阶段的 BlockedEvidence 状态。

以下条目只记录后续阶段的规格入口和前置门，不表示这些阶段已经开始实施或通过。`specStatus=Documented` 只说明设计文档存在；`evidenceStatus=BlockedEvidence` 保持直到对应报告、fixture、设备和 gate decision 可访问。

| 阶段任务 | 权威文档 | specStatus | evidenceStatus | 关闭所需证据 |
| --- | --- | --- | --- | --- |
| M3-CORE 纯 Core 抽取 | [M3CoreExtraction](M3CoreExtraction.md)、[DependencyGraph](DependencyGraph.md) | Documented | BlockedEvidence | M0-M2 gates、无 Godot 引用、Core deterministic build、parser/variable/save/dialect 双跑、回退报告 |
| M4-RESOURCE PixelStore/图形 | [M4ResourceGraphics](M4ResourceGraphics.md)、[ResourceSystem](ResourceSystem.md) | Documented | BlockedEvidence | M3 gate、G/CBG/Sprite/PixelStore fixture、Node/RID/Texture 清理、内存与 GPU/RSS 报告 |
| M5-PORT 平台组合 | [M5PlatformComposition](M5PlatformComposition.md)、[SecurityLimits](SecurityLimits.md) | Documented | BlockedEvidence | M4 gate、typed port trace、Android/iOS/desktop、SAF canary 真机、erafl capability fixture |
| M6-EXPERIMENT 调度/渲染 | [M6SchedulingRenderingExperiment](M6SchedulingRenderingExperiment.md)、[ExecutionContract](ExecutionContract.md) | Documented | BlockedEvidence | M5 gate、100% yield audit、cooperative/legacy 双跑、候选 renderer visual/hit/accessibility、p99 与回退 |
| M7-RELEASE 清理/发布 | [M7CleanupReleaseGovernance](M7CleanupReleaseGovernance.md) | Documented | BlockedEvidence | M6 gate、两个发布周期零流量、资产签字、回退演练、生命周期/内存稳定、矩阵同步 |

| M3-M7 工程工单闭环 | [M3M7EngineeringExecution](M3M7EngineeringExecution.md)、[ProjectStructure](ProjectStructure.md) | Documented | BlockedEvidence | versioned work package、输入 identity、命令 exit code、差分/生命周期/回退/未覆盖报告及 gate decision |

## 开始迁移代码前的阻断门

| Gate | 要求 | gateStatus | 当前已有/阻塞原因 |
| --- | --- | --- | --- |
| G0 三层 baseline | 旧 gEmuera APK/游戏 hash/trace + 上游 runner + SN/FL/AN fixtures | Blocked | 本地已有 M0 identity/runner/trace、resolved fixture manifest、eraFL 授权/hash/首等待与复杂页双后端显示；plan-binding repeat3 的 semantic report 可重复且每次 screenshot/hit 已 Captured，但 raw transport trace 不稳定，且缺归档签署、upstream runner、nested/srcb/dynamic-map/AN fixture、APK/真机及其它授权复核 |
| G1 扩展库存 | FunctionCode/Creator、小数/VarExt/SQL/NF/HOTKEY/profile 全分类 | Blocked | M0-DIA 当前源码身份已固定 97 分支、326/360 注册与 profile 快照，并得到 326/326 instruction args/effective flags、360/360 function argument+return 静态解析；DIA-07/08/09 分别补齐归属 provenance、旧 lookup contract 与可见性候选，DIA-10/11 固定 source-only profile preflight 和旧 launcher/marker precedence，DIA-12/13 固定静态 CompatibilityPack 与声明词汇，DIA-14/15 固定唯一 decision owner 与 10 个 future port，DIA-16 再固定 `gemuera.v24`→`game.snake` 的 2 module/1 edge/10 port/2 closure 静态 DAG。它们都不是 owner/replacement、可信内容 resolver、runtime module catalog 或 runtime plan；`ICVariable` comparer 为静态捕获、`ICFunction` current-culture `ToUpper` 有风险，686 semantic alias/replacement 仍未决，defaults/errors/restructure/completion/effect behavior 全 Uncovered，runtime isolation Failed。静态 DIA 报告的 `parserVmConsumption=NotConsumed` 只表示报告本身未接线到 legacy Parser/VM 行为；独立 startup/parser 已消费窄 `CompatibilityPlan` descriptor presence/ownership guard，验证 plan identity/profile/hash 与 legacy descriptor presence/module ownership，但不替换 legacy handler 或执行 typed policy。M1 仍 Blocked，且缺 target、两侧 fixture 与 D1/D2 runtime |
| G2 Display v2 | div/srcb/盒模型/溢出/transaction/data-only golden | Blocked | 规格已写；缺 DTO tree/timeline golden |
| G3 PixelStore | 唯一 CPU truth、同步返回/revision/pixel hash | Blocked | 规格已写；缺实现与像素 fixture |
| G4 迁移骨架 | LegacySession/Console/Graphics façade、flags、双跑和回退 | Blocked | M0-SES-01 已固定 central `GlobalStatic` 14 项与 `Program` 17 项、14 个中心 reset site（12 baseline + canary-only `ctrlZ` 与 debug `StackList`）；最小 façade、generation rollback smoke 与默认关闭 canary 已存在，但 `StackList` 仅有 debug canary 证据，范围外 static、真实双跑/rollback report 仍缺 |
| G5 VM ADR | 保留专用 thread；只有 yield audit+Android p99 后才允许主线程实验 | Blocked | 默认线程已裁决；实验仍缺全量 audit 和 Android p99 |

G0–G5 未关闭前，只允许建立 runner、fixture、façade 和边界，不允许批量重写/删除旧解释器、显示后端或 Android 输入链。

G1 的 M0-DIA-12 仅补充 future `CompatibilityPack` 的静态声明边界：它以 DIA-10/11 hash 锁定 `v24pure`/`snake` 的内置 module 集，要求 capability 数组显式存在并拒绝可执行载荷、未知 module 与 `SnakeModernMobile`。内容 fingerprint、game manifest、pin、save/fixture capability、runtime resolver/plan、D2 frozen registry 和 Parser/VM consumption 均不在该报告范围；G1 仍为 Blocked。

G1 的 M0-DIA-13 只把 DIA-01 的分类 provenance 收敛成 10 个 `BehaviorKey` 与 4 个 `CapabilityId`：每一项固定 source classification、目标 module、fixture 和命中数，并要求 DIA-12 当前 pack capability exposure 保持空。它不决定任何 policy value、行为兼容或 runtime capability，也不批准 manifest allowlist、policy manager、resolver/plan、D2 registry 或 Parser/VM 接线；G1 仍为 Blocked。

G1 的 M0-DIA-14 只把上述 10 个 `BehaviorKey` 固定为 10 条 future consumer contract/decision owner 边界，并从 DIA-01/DIA-13 静态报告锁定 14 个分类与 16 个源文件绑定。特别是 scoped variable 的 config schema 是 `ConfigurationInput`，不拥有最终 decision；registration guard 才是 `DecisionConsumer`。这不是 C# policy interface/manager、runtime plan/resolver、D2 frozen registry 或 Parser/VM 接线，也不证明 policy value、错误、completion/effect 或行为兼容；runtime isolation=`Failed`、静态 DIA-14 报告的 `parserVmConsumption=NotConsumed`、M1=`Blocked`，G1 仍为 Blocked。

G1 的 M0-DIA-15 再将上述唯一 owner 分配为 10 个 future `portTypeId`（6 `PolicyDecision`、2 `BridgeProjection`、2 `FrozenCatalogContribution`），使后续 D3 不会沿用旧文档的下划线键或未对齐 interface 名称。所有项仍是 `InterfaceDraftOnly`、policy value=`Unspecified`、runtime type=`NotImplemented`；它既不添加 C# type/method/DTO/default，也不创建 policy manager、resolver/plan、D2 registry 或 Parser/VM 接线。G1 仍为 Blocked。

G1 的 M0-DIA-16 只把 DIA-12 的两个静态 pack 与 DIA-15 的 10 个 port 组成 `gemuera.v24@1.0.0`、`game.snake@1.0.0` 的 dependency-first descriptor DAG：基础模块零依赖/零 port，Snake 模块依赖基础模块并拥有全部 10 个 port，`v24pure`/`snake` closure 仅是离线投影。它不加载 module assembly、不执行 version-range resolution、不绑定真实内容/存档/fixture，也不创建 runtime catalog、resolver/plan、D2 registry 或 Parser/VM 接线；G1 仍为 Blocked。

G1 的 M0-DIA-17 再把 DIA-13/15 的 10 个 BehaviorKey/port 锁为 fixture contract：每项只复用既有 fixture ID，要求 `v24pure`/`snake` 双侧、baseline/extension/undeclared 与 input/result/error/completion/effect 记录。当前 10 项全部 `Planned`/`Uncovered`/`BlockedByFixture`，0 个实际行为结果；它严格禁止 policy value、C# interface/method/DTO 或 runtime payload，因此没有推进 policy manager、resolver/plan、D2 registry、Parser/VM 或 M1。G1 仍为 Blocked。

当前实施顺序和 work package 以 [M0M2ImplementationBaseline](M0M2ImplementationBaseline.md) 为准。M0 已开始建立 identity/runner/trace，因此为 `executionStatus=InProgress, gateStatus=Blocked, blockerCode=EvidenceMissing`；M1 的最小 legacy session shell 也为 `executionStatus=InProgress, gateStatus=Blocked, blockerCode=PreviousGate:M0`；M2 仍为 `NotStarted / Blocked / PreviousGate:M1`。G2/G3 的规格已写清不等于放行，不批准提前实现新 layout、PixelStore 或渲染后端。

已实现的 M1-CORE-01/02 现在包含最小 `LegacySessionFacade`、Godot legacy backend 接线、profile→内置 module 目录、deterministic rollback smoke，以及默认关闭的 `migration.session_isolation` canary；startup/parser 已完成 `CompatibilityPlan` identity/profile+hash hand-off，并消费窄 descriptor presence/ownership guard，验证 legacy descriptor presence/module ownership。它不替换旧 handler 或执行 typed policy，仍不含旧 VM 对冻结 registry/descriptor/policy 的行为消费、完整 static isolation 或真实游戏运行时回退报告，因此不改变 M1 被阻断、M2 未开始的结论。

## Checklist A–P 映射

| Checklist | 证据入口 |
| --- | --- |
| A 范围/事实 | [README](README.md)、[GEmueraBaseline](GEmueraBaseline.md)、[EvidenceIndex](EvidenceIndex.md)、[ConflictLedger](ConflictLedger.md) |
| B 存档/读档 | [SaveFormat](SaveFormat.md)、[SaveLoadSystem](SaveLoadSystem.md)、[VariableSystem](VariableSystem.md) |
| C 指令兼容 | [CompatibilityMatrix](CompatibilityMatrix.md)、[InstructionInventory](InstructionInventory.md)、[InstructionRenderMap](InstructionRenderMap.md) |
| D 资源/图形 | [ResourceSystem](ResourceSystem.md)、[EvidenceIndex](EvidenceIndex.md) |
| E HTML/输入 | [MarkupSystem](MarkupSystem.md)、[InputSystem](InputSystem.md)、[RenderingSystem](RenderingSystem.md) |
| F 编码/配置 | [EncodingSystem](EncodingSystem.md)、[ConfigSystem](ConfigSystem.md) |
| G Core/Godot | [DependencyGraph](DependencyGraph.md)、[GodotIntegration](GodotIntegration.md) |
| H VM/时间片 | [ExecutionContract](ExecutionContract.md)、[ScriptEngine](ScriptEngine.md)、[KeyClasses](KeyClasses.md) |
| I API/示例 | [HowToRun](HowToRun.md)、[GodotIntegration](GodotIntegration.md)、[VerificationPlan](VerificationPlan.md) |
| J 生命周期/切换 | [Architecture](Architecture.md)、[StateIsolation](StateIsolation.md)、[LifecycleMemory](LifecycleMemory.md) |
| K 渲染/无障碍 | [RenderingSystem](RenderingSystem.md)、[PerformanceOptimization](PerformanceOptimization.md) |
| L 移动文件 | [HowToRun](HowToRun.md)、[SecurityLimits](SecurityLimits.md)、[ResourceSystem](ResourceSystem.md) |
| M 安全 | [SecurityLimits](SecurityLimits.md)、[ExtensionRuntime](ExtensionRuntime.md)、[SaveLoadSystem](SaveLoadSystem.md) |
| N 音频/缓存 | [AudioSystem](AudioSystem.md)、[LifecycleMemory](LifecycleMemory.md) |
| O 错误/性能 | [ErrorRecovery](ErrorRecovery.md)、[PerformanceOptimization](PerformanceOptimization.md) |
| P 验证/一致性 | [VerificationPlan](VerificationPlan.md)、[ConflictLedger](ConflictLedger.md)、本表 |

## 关闭规则

本目录当前可作为**M0 runner、fixture、façade 和边界工作的设计输入**，不能作为直接重写实施基线。只有 G0–G5 关闭、`BlockedEvidence` 转为可复查的 `Passed`，才可批准进入后续替换阶段；任何阶段仍须保留 feature flag 回退。

## 静态审查记录与文档守卫

2026-07-11 的记录是历史快照，不能手工维护成当前事实。文档数量、行数和 bytes 必须由 guard 每次生成；新增/删除文档后静态数字自动失效。该记录只证明当时文档结构，不替代 build、fixture、benchmark 或真机报告。

| 检查 | 结果 |
| --- | --- |
| Markdown 文档/总体积 | 历史值 36 份 / 2,374 行 / 224,032 bytes；当前值由 guard 生成，不作为手填验收项 |
| 缺失一级标题、空文档 | 0 / 0 |
| 失效相对 Markdown 链接 | 0 |
| 未闭合 fenced code block | 0 |
| Markdown 表格列数不一致 | 0 |
| UTF-8 U+FFFD 替换字符 | 0 |
| tasks.md 唯一任务 ID / 追踪缺失 | 27 / 0 |
| 上游/旧 Creator 静态键 | 248 / 358；行为分类尚未完成 |
| 旧 `CBGSETSPRITE Pending/未检出` 结论 | 0 |

检查脚本已落入 [tools/doc-guards](../../tools/doc-guards/README.md)，可输出带 guard 版本、source manifest hash、动态计数和 exit code 的 JSON。P0-13/P3-01 仍需在 CI 归档报告并完成人工交叉审阅；本地通过不得手工改写为运行时兼容 `Passed`。

生产 guard 检查：Markdown 数量/行数/bytes 的生成值、唯一一级标题、空文档、相对链接、fence、表格列、U+FFFD、重复权威定义、M0–M2 阶段引用、KnownLimitations/CompatibilityMatrix 关键 key 对应、硬编码旧文档数量和无证据完成声明。CI 报告必须记录 guard 版本、source manifest hash、命令和 exit code。
2026-07-15 trace note: plan identity is now handed from the committed facade through the Godot backend into `Program` and `ParserMediator`, with reset-time clearing. Descriptor/policy execution is still `BlockedEvidence`, so this does not change M1/M2 gate status.
M0-SAV-01 fixture audit evidence is now present as a local artifact (`fixtureSetHash=8801c74f...72d09`, 10 candidates, 7 recognized ordinary 1808 headers). It remains `BlockedEvidence` because the tool deliberately does not parse, decompress, write, bind profiles or prove round trips.
The post-binding 100-switch cross-ABA report is reproducible and keeps `gateStatus=Blocked`; it strengthens observation coverage but does not satisfy the M1 memory, full-static, input/late-completion, Android or approval gates.
The M0-DSP-01 post-source-change repeat3 artifact (`C:\Users\Han\AppData\Local\Temp\gemuera-m0-display-repeat3-plan-binding-eadb73ce785c4a0494ea98a9cce793f1`) has runner metadata `compatibility=None: default-off observation only`; it adds three Controls and three Canvas runs. Semantic reports repeat consistently and screenshot/hit statuses are `Captured`, while transport traces differ on every run; this is retained as diagnostic evidence and does not close P0-14 or G0 or prove Controls↔Canvas equivalence.
M0-SAV-01 bounded slice: `Test-LegacySaveRoundTripEvidence.ps1` covers explicit profile validation, source-root containment refusal, source hash immutability, and isolated byte-copy round-trip. It is `CompleteForScope / ReadyForReview` for this evidence slice only; semantic codec round-trip, offset maps, profile conflict resolution, runtime resolver, and the parent M0 gate remain `BlockedEvidence`.
