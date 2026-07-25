# 术语与状态词汇表

本文是 NewFrameworkDesign 的检索索引，用来统一阅读时的术语和状态口径；它不替代各主题文档中的行为、格式或验收定义。出现冲突时，以链接的主题所有者为准。

## 项目、资料与证据

| 术语 | 含义 | 主题所有者 |
| --- | --- | --- |
| ERA | 一组以 ERB、CSV、ERH 等文件组织的脚本游戏格式和生态。 | [GEmueraBaseline](GEmueraBaseline.md) |
| Emuera | ERA 的原始模拟器语义及其存档、脚本和数据模型的主要上游参照。 | [EvidenceIndex](EvidenceIndex.md) |
| XEmuera | Emuera 的跨平台实现；本目录用其源码和 FrameworkDesign 作为上游事实来源之一。 | [GEmueraBaseline](GEmueraBaseline.md) |
| 旧 gEmuera | 当前可运行的 Godot C# 工程，是迁移起点和 legacy 行为证据，不等同于目标架构已完成。 | [GEmueraBaseline](GEmueraBaseline.md) |
| 目标新模拟器 | 本目录描述的渐进式目标架构；未经过 fixture 和门禁的部分仍是设计，不是运行时事实。 | [Architecture](Architecture.md) |
| fixture | 可复现的上游、旧工程或实际游戏输入及其授权、身份、期望报告绑定。 | [VerificationPlan](VerificationPlan.md) |
| canonical report | 去除明确非语义噪声后仍保留顺序、错误、effect 和状态的机器可读比较结果。 | [VerificationPlan](VerificationPlan.md) |
| Uncovered | 尚无可访问的实现、测试、设备或报告证据；不能从相近源码或单次截图推断结论。 | [AcceptanceTraceability](AcceptanceTraceability.md) |

## 状态与迁移阶段

| 术语 | 含义 | 主题所有者 |
| --- | --- | --- |
| specStatus | 规格是否已写清且可追踪；只评价文档，不代表运行结果。 | [AcceptanceTraceability](AcceptanceTraceability.md) |
| evidenceStatus | 证据可用性，例如 Uncovered、BlockedEvidence、ReadyForReview、Passed。 | [AcceptanceTraceability](AcceptanceTraceability.md) |
| executionStatus | 阶段实际工作状态：NotStarted、InProgress 或 Passed。 | [M0M2ImplementationBaseline](M0M2ImplementationBaseline.md) |
| gateStatus | 阶段证据门：Blocked、ReadyForReview 或 Passed。 | [M0M2ImplementationBaseline](M0M2ImplementationBaseline.md) |
| blockerCode | 阶段未放行的稳定原因，例如 EvidenceMissing、PreviousGate 或 ReportInvalidated。 | [M0M2ImplementationBaseline](M0M2ImplementationBaseline.md) |
| M0 | 固定旧 gEmuera 的 runner、trace、fixture、身份和基线报告阶段。 | [M0M2ImplementationBaseline](M0M2ImplementationBaseline.md) |
| M1-CORE-01/02 | 已实现的纯 .NET 合同、模块组合、profile→内置 module 目录、会话候选、解释器边界和默认关闭启动 canary；现由最小 `LegacySessionFacade`/Godot backend 消费，但旧 Parser/VM 不读取计划，M1 仍被门禁阻断。 | [M1CoreRuntimeContractSlice](M1CoreRuntimeContractSlice.md) |
| M1 迁移阶段 | LegacySessionFacade 和 runtime generation guard 阶段；当前仍与 M0/M2 串行受门禁约束，不能因 M1-CORE-01/02 或默认关闭 canary 存在而提前启动。 | [M0M2ImplementationBaseline](M0M2ImplementationBaseline.md) |
| M2 | 从旧显示对象无损深复制 Display DTO 的旁路 capture/canary 阶段；不批准重写 layout 或 renderer。 | [M0M2ImplementationBaseline](M0M2ImplementationBaseline.md) |
| M3–M7 | 纯 Core 抽取、资源图形、平台组合、调度/渲染实验和发布治理的后续设计阶段。 | [MigrationPlan](MigrationPlan.md) |

| M3–M7 工程执行协议 | 后续阶段获批后统一使用的 work package、identity、报告、回退、状态迁移和 gate decision 规则；不构成实施授权或运行时证据。 | [M3M7EngineeringExecution](M3M7EngineeringExecution.md) |

## 架构、会话与兼容

| 术语 | 含义 | 主题所有者 |
| --- | --- | --- |
| Core | 不依赖 Godot 类型的纯 .NET 领域逻辑和合同层。 | [DependencyGraph](DependencyGraph.md) |
| Bridge | 把 Core 的 DTO、port、effect 映射到 Godot 主线程、平台能力和 View 的适配层。 | [GodotIntegration](GodotIntegration.md) |
| View / Presentation | Godot 的 Control、Canvas、音频和输入投影；它不直接修改变量或会话配置。 | [Architecture](Architecture.md) |
| GameSession | 某个游戏选择的一组配置、变量、资源、VM、显示历史和冻结兼容计划的唯一所有权边界。 | [Architecture](Architecture.md) |
| candidate | 在当前会话仍可用时独立加载和校验的候选会话；失败或过期时只销毁自己。 | [StateIsolation](StateIsolation.md) |
| generation / operation id | 用于拒绝迟到异步 completion 的会话代次和操作标识；取消令牌不能替代它们。 | [StateIsolation](StateIsolation.md) |
| short commit gate | 只包围最新 generation 比较和 CurrentSession 原子交换的短临界区，不能包住加载或释放。 | [StateIsolation](StateIsolation.md) |
| LegacySessionFacade | M1 阶段对旧 GlobalStatic、Program 和 EmueraThread 的事务生命周期外壳，不是新的业务 manager；默认关闭 canary 时不参与启动。 | [M0M2ImplementationBaseline](M0M2ImplementationBaseline.md) |
| CompatibilityPlan | 每个 GameSession 冻结的 profile、模块、codec、capability 和端口计划；静态预检或合同类型本身不是运行时计划。 | [DialectExtensionSystem](DialectExtensionSystem.md) |
| DialectModule | 受信任的内置方言模块，按显式依赖和版本组合；不以 DLL 反射发现作为默认机制。 | [DialectExtensionSystem](DialectExtensionSystem.md) |
| port | Core 请求、Bridge 或平台实现的窄接口边界；port 名称或合同草案不等于已有运行时实现。 | [DependencyGraph](DependencyGraph.md) |
| effect | VM 发出的显示、输入、音频、保存或应用级有序结果；其完成模式由执行契约裁决。 | [ExecutionContract](ExecutionContract.md) |

## 脚本、显示与 Godot

| 术语 | 含义 | 主题所有者 |
| --- | --- | --- |
| ERB / CSV / ERH | 分别承载游戏逻辑、数据定义和头部宏/常量的 ERA 文件类型。 | [ScriptEngine](ScriptEngine.md) |
| InputRequest | 脚本等待 Enter、任意键、整数、字符串、按钮或鼠标键等输入时的请求和 continuation 边界。 | [InputSystem](InputSystem.md) |
| Display DTO / DisplayTransaction | 从旧显示对象深复制出的不可变显示树及其提交、滚动、data-only 与顺序语义。 | [RenderingSystem](RenderingSystem.md) |
| PixelStore | 目标架构中脚本可观察 CPU 像素的唯一真相；Texture 和 RID 只是主线程派生投影。 | [ExecutionContract](ExecutionContract.md) |
| CBG | Client BackGround，脚本侧背景、图片层和相关交互状态。 | [InstructionRenderMap](InstructionRenderMap.md) |
| Canvas 后端 / Control 后端 | 旧 gEmuera 两条独立显示路径；迁移期的截图和 hit 证据必须分别保留，不能互相替代。 | [RenderingSystem](RenderingSystem.md) |
| Node / Resource / RID | Godot 场景、资源和服务器句柄；由主线程 Bridge 按 generation/revision 生命周期管理，不能进入 Core 所有权。 | [LifecycleMemory](LifecycleMemory.md) |

## 使用规则

- 先用本文确定术语，再回到主题所有者确认行为、边界和验收；不要把词汇表中的短定义当成第二套规格。
- M1-CORE-01/02、静态库存、预检、smoke 和默认关闭 canary 都属于有限证据。只有运行时接线、代表 fixture、回退和 gate decision 齐全后，才可改变迁移阶段或兼容结论。
- 新术语必须链接唯一主题所有者；若会影响状态口径，同时更新 EvidenceIndex、CompatibilityMatrix、KnownLimitations、AcceptanceTraceability 和 ConflictLedger。
