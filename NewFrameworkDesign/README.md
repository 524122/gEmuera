# 新 Emuera 模拟器：迁移与目标架构设计

## 状态声明

本目录是**迁移与目标架构设计候选**，不是允许直接重写的实施基线。它以“上游 XEmuera + 可运行旧 gEmuera + 实际游戏 fixture”三层证据约束新模拟器，先保住 Snake、eraFL、Android、显示和输入资产，再渐进抽取纯 Core。达到 [MigrationPlan](MigrationPlan.md) 各阶段门禁前，不得删除旧 runner、renderer 或 platform path。

当前唯一允许直接拆工单实施的范围是 [M0M2ImplementationBaseline](M0M2ImplementationBaseline.md)。M0 仍是发布门禁的起点；已实现的 [M1CoreRuntimeContractSlice](M1CoreRuntimeContractSlice.md) 已包含最小 `LegacySessionFacade`/Godot legacy backend 接线，但只处于 `InProgress / Blocked`，不等同于 M1 通过，也不改变 M2 的门禁状态。M3+、PixelStore、新 VM 调度、新 renderer 和 SAF 主路径仍是目标设计，不是已批准工作。

## 证据规则

兼容事实分层而非简单覆盖：上游 XEmuera 定义原版语义；旧 gEmuera 定义现有扩展/修复；Snake、eraFL 和 Android fixture 决定真实依赖。`Compatible` 必须指明 profile 和差分报告；只有源码阅读时最多是 `Mapped`。

全部文档代码默认是伪代码；只有进入未来 `tests/ApiSmoke` 并记录锁定工具链的完整代码才可称可编译。

术语、状态词和 M1 合同切片与 M1 迁移阶段的区别见 [Glossary](Glossary.md)。

## 推荐阅读顺序

1. [Architecture](Architecture.md)：目标、所有权、Autoload、线程和事务。
2. [GEmueraBaseline](GEmueraBaseline.md)、[EvidenceIndex](EvidenceIndex.md)、[InstructionInventory](InstructionInventory.md)：三层事实和名称库存。
3. [DialectExtensionSystem](DialectExtensionSystem.md)、[AutomaticGameCompatibilityResolution](AutomaticGameCompatibilityResolution.md)：v24、Snake 与未来魔改解释器的组合、隔离、扩展契约，以及 eraFL/eraTW 的自动识别与兼容计划解析设计。
4. [DependencyGraph](DependencyGraph.md)、[ProjectStructure](ProjectStructure.md)、[GodotIntegration](GodotIntegration.md)：工程与场景接线。
5. [MigrationPlan](MigrationPlan.md)、[M0M2ImplementationBaseline](M0M2ImplementationBaseline.md)、[M1CoreRuntimeContractSlice](M1CoreRuntimeContractSlice.md)、[ExecutionContract](ExecutionContract.md)、[ScriptEngine](ScriptEngine.md)：迁移、当前实施范围、已落地合同边界、线程和同步顺序。
6. [M3CoreExtraction](M3CoreExtraction.md)、[M4ResourceGraphics](M4ResourceGraphics.md)、[M5PlatformComposition](M5PlatformComposition.md)：M3-M5 的 Core、资源和平台组合设计；这些文档不能替代 M0-M2 门禁。
7. [M6SchedulingRenderingExperiment](M6SchedulingRenderingExperiment.md)、[M7CleanupReleaseGovernance](M7CleanupReleaseGovernance.md)、[M3M7EngineeringExecution](M3M7EngineeringExecution.md)：实验、清理、发布、回退及跨阶段工单/报告闭环。
8. [SaveFormat](SaveFormat.md)、[SaveLoadSystem](SaveLoadSystem.md)、[VariableSystem](VariableSystem.md)：数据正确性。
9. [ResourceSystem](ResourceSystem.md)、[MarkupSystem](MarkupSystem.md)、[InstructionRenderMap](InstructionRenderMap.md)：兼容内容。
10. [RenderingSystem](RenderingSystem.md)、[AudioSystem](AudioSystem.md)、[LifecycleMemory](LifecycleMemory.md)：Godot 表现与资源。
11. [ExtensionRuntime](ExtensionRuntime.md)、[SecurityLimits](SecurityLimits.md)、[ErrorRecovery](ErrorRecovery.md)、[PerformanceOptimization](PerformanceOptimization.md)：扩展运行时与生产边界。
12. [HowToRun](HowToRun.md)、[VerificationPlan](VerificationPlan.md)、[AcceptanceTraceability](AcceptanceTraceability.md)：构建与关闭条件。

## AI 实施与快反馈

[AIDevelopmentWorkflow](AIDevelopmentWorkflow.md) 定义 AI 的任务包、Explore、FastLoop、WorkPackage 与 PhaseRelease 路径。它将局部编辑的定向反馈与真实游戏、Android、性能、回退和阶段签署分开；不改变 M0–M2 当前实施授权，也不降低 M3–M7 的关闭门。

## 核心文档主题

| 主题 | 文档 |
| --- | --- |
| 架构/依赖/模块/类型/目录 | Architecture、DependencyGraph、ModuleOverview、KeyClasses、ProjectStructure |
| VM/指令/变量/输入/状态 | ScriptEngine、InstructionRenderMap、VariableSystem、InputSystem、StateIsolation |
| 存档/编码/配置 | SaveFormat、SaveLoadSystem、EncodingSystem、ConfigSystem |
| 资源/HTML/渲染/音频 | ResourceSystem、MarkupSystem、RenderingSystem、AudioSystem |
| 生命周期/错误/性能/平台 | LifecycleMemory、ErrorRecovery、PerformanceOptimization、HowToRun |
| 限制与入口 | KnownLimitations、README |
| 术语与状态索引 | Glossary |

补充权威文档：GEmueraBaseline、DialectExtensionSystem、MigrationPlan、M0M2ImplementationBaseline、M1CoreRuntimeContractSlice、ExecutionContract、ExtensionRuntime、EvidenceIndex、InstructionInventory、SecurityLimits、GodotIntegration、CompatibilityMatrix、ConflictLedger、VerificationPlan、AcceptanceTraceability。

## M3-M7 阶段文档与工程执行协议

当对应前置门获批后，所有 work package 还必须遵循 [M3M7EngineeringExecution](M3M7EngineeringExecution.md) 的输入 identity、报告、回退与签署协议；该协议不把规划状态提升为运行时证据。

M3-M7 的设计文档只描述后续阶段的边界、门禁和回退，不把目标设计写成已完成实现。阶段必须等待 M0-M2 的串行门禁；另一条并行工作线可以继续完成 M0-M2，但不得把本表的规划状态改成运行时 `Passed`。

| 阶段 | 权威文档 | 重点 |
| --- | --- | --- |
| M3 | [M3CoreExtraction](M3CoreExtraction.md) | 纯 Core、解析/变量/存档合同、方言计划消费 |
| M4 | [M4ResourceGraphics](M4ResourceGraphics.md) | PixelStore、资源目录、Texture/RID/Node 生命周期与内存 |
| M5 | [M5PlatformComposition](M5PlatformComposition.md) | typed ports、输入/音频/SQLite、Android SAF canary、erafl capability |
| M6 | [M6SchedulingRenderingExperiment](M6SchedulingRenderingExperiment.md) | cooperative VM、新 renderer、双跑、yield audit 与 p99 |
| M7 | [M7CleanupReleaseGovernance](M7CleanupReleaseGovernance.md) | 零流量清理、发布、回退和魔改兼容治理 |
| 跨阶段 | [M3M7EngineeringExecution](M3M7EngineeringExecution.md) | work package、状态迁移、证据包、实验阈值、删除 packet 与 gate decision |

## 不可妥协约束

- Core 为无 GodotSharp 的独立 .NET assembly。
- 迁移期保留专用 VM thread；主线程 Step/Resume 仅在全量 yield audit 后实验。
- 上游 1808 与 gEmuera/Snake codec profile 显式分离，冲突类型字节不自动猜测。
- Resource/Save/VM/Config/Cache 属于 GameSession。
- 每个 GameSession 拥有冻结的 CompatibilityPlan；禁止全局 CoreProfile、无条件混合注册和执行期 `if (Snake)`。
- 外部游戏包/存档视为不可信输入并有硬上限。
- Display model 与 Godot backend 分离；后端必须经 ADR/benchmark。
- Display DTO 必须表达 div/srcb/定位/盒模型/动态地图事务，不能先接受简化模型。
- 外部 DLL 默认禁用；桌面显式信任也不宣称沙箱。
- 旧 generation 的任何异步结果不得写入当前会话。
- 未运行的 build/fixture/device 验证保持 Uncovered。

## 完成定义

只有 P0 全部有可访问证据、影响数据/安全/移动/会话的 P1 通过、KnownLimitations 与报告一致、Godot ApiSmoke/export 和真机记录可复查、冲突台账无未裁决矛盾，才可把本修订标记完成。

## 文档治理与快速校验

文档数量、行数、bytes 和结构检查不得手工维护。仓库根目录运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/doc-guards/Invoke-DocGuard.ps1
```

守卫规则、JSON 报告和 CI 用法见 [tools/doc-guards](../tools/doc-guards/README.md)。守卫通过只表示文档结构和不可删除条款一致，不表示 M0、兼容差分、APK 或真机验证已通过。
