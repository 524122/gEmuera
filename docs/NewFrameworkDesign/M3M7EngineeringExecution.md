# M3–M7 工程执行协议与工单闭环

## 文档地位

本文把 M3–M7 已有的目标设计补成可审查、可拆分、可回退的工程执行协议。它不改变 [M0M2ImplementationBaseline](M0M2ImplementationBaseline.md) 是当前唯一实施授权的事实，也不把任何 M3–M7 阶段从 NotStarted / Blocked / PreviousGate:* 推进为已开工。

“工程性文档”在这里不等于“已经获准实现”。它表示：前置门一旦获批，维护者无需再猜测 work package 的输入、允许边界、报告、回退和签署条件。阶段仍只有在前置 gate decision、报告 hash、fixture 与对应设备证据齐备后，才能转入 InProgress。

## 阶段依赖与允许并行度

| 阶段 | 启动前的不可省略输入 | 阶段内可并行的工作 | 不可并行/不可越过的边界 |
| --- | --- | --- | --- |
| M3 | M2 已签署；M0–M2 source、fixture、toolchain、runtime identity 已冻结 | 纯合同、fixture、fuzz corpus、只读差分 harness | 未完成的 parser/variable/save 不能直接替换旧 owner；Core 不接触 Godot |
| M4 | M3 对应 Core 包已 Passed；像素格式/返回值 fixture 已冻结 | catalog/decode 与 Bridge ledger 可分别准备 | PixelStore CPU truth 先于 Texture/RID 投影；不能先改 renderer |
| M5 | M4 的 generation/revision/资源账本已 Passed | 各 typed port adapter 可在相同 port manifest 下分包实施 | 新 port 不能绕过 VM completion；SAF 只能 canary，不能替换旧路径 |
| M6 | M5 已 Passed；实验 identity、基线、阈值、回退 artifact 已冻结 | 调度实验与 renderer 实验可独立进行 | 两个实验不得共用开关或共用可变 Session/Resource；任何一个失败都不能拖低旧路径 |
| M7 | 所有受影响的 M3–M6 work package 已 Passed；两个可观测发布周期可执行 | 不同 owner 的 removal packet 可准备 | 未独立证明的 subsystem 不得同一变更集删除；没有回退演练不得删除 |

M6 的 renderer 与 scheduler 只在共同的实验基线完成后才可并行。M7 不要求不相关的实验通过，但每个删除候选都必须列出它依赖的 M3–M6 package 与对应 Passed gate decision。

## Work package 记录

每个未来实施包在写业务代码前都必须新建并冻结一份版本化 work-package record。它是未来生成的机器可读交付物，不是手工修改本 Markdown 后即可获得的放行证明。最小字段如下：

| 字段 | 要求 |
| --- | --- |
| schema、workPackageId、phase | 使用稳定 schema 和唯一 ID；不得复用已关闭包的 ID |
| executionStatus、gateStatus、blockerCode | 使用 [M0M2ImplementationBaseline](M0M2ImplementationBaseline.md) 的三字段模型，禁止写成一个模糊状态词 |
| predecessors | 列出前置 gate decision、报告 hash、CompatibilityMatrix key 与失效条件 |
| source/toolchain/runtime identity | 源码树、Core/Bridge/App artifact、游戏/fixture、配置、profile、flag、设备与命令 hash |
| allowedPaths、forbiddenPaths、owner | 明确允许的目录、禁止触碰的旧路径、Core/Bridge/App/测试 owner 与审核人 |
| commands、fixtures、comparators | 非交互命令、seed、locale、timezone、timeout、输入 trace、比较器版本和预期 exit code |
| reports、uncovered | 必须生成的报告及其 SHA-256；未跑的平台、游戏、异常和设备必须显式列出 |
| rollback | flag、旧 adapter/artifact、数据备份、恢复命令和实际回退验证结果 |
| decision | preparedBy、reviewedBy、approvedBy、UTC 时间、结论、例外 ADR、报告失效条件 |

记录中的输入 hash 一旦变化，或回退 artifact、fixture、工具链无法取得，包必须变为 InProgress / Blocked / ReportInvalidated，不能沿用旧的 Passed 结论。

## 编辑迭代与 work package 关闭

work-package record 在一个包开始时建立，完整证据包在该包准备关闭时生成；两者都不是要求每一个 AI 补丁重复填写或重复跑 PhaseRelease。日常编辑先按 [AIDevelopmentWorkflow](AIDevelopmentWorkflow.md) 选择 Explore 或 FastLoop：只运行任务包指定的定向 build、合同测试、场景测试或文档守卫。

一旦改动跨越 Core/Bridge/App owner、影响 CompatibilityPlan、Session generation、存档、资源 revision、平台 port、显示 transaction 或 feature flag，必须立即升级到本章定义的 work package。FastLoop 结果可作为证据包的一部分，但不能替代 differential、lifecycle、device、rollback、uncovered 或 gate decision。

## 统一状态迁移

| 当前状态 | 可迁移到 | 必要条件 |
| --- | --- | --- |
| NotStarted / Blocked / PreviousGate:* | InProgress / Blocked / EvidenceMissing | 前置阶段已正式批准，且本包的 scope、回退和 evidence plan 已签署 |
| InProgress / Blocked / EvidenceMissing | InProgress / ReadyForReview / None | 所有必需报告已生成、hash 已记录、未覆盖项已分类，尚未得到批准结论 |
| InProgress / ReadyForReview / None | Passed / Passed / None | 比较、生命周期、平台和回退检查均通过，且 gate decision 已签署 |
| 任意 Passed | InProgress / Blocked / ReportInvalidated | 输入 identity、fixture、工具链、flag、回退路径或关键报告任一失效 |
| 任意 InProgress | InProgress / Blocked / 稳定失败码 | 差分失败、预算拒绝、设备失败、取消泄漏或安全限制触发；保留失败报告 |

“代码编译”“文档存在”“单个桌面样例通过”都不能直接触发 Passed。Uncovered 是结果的一部分，不得用空报告、截图或正常化规则隐藏。

## 证据包与报告位置

报告目录遵循 [ProjectStructure](ProjectStructure.md) 的 build、differential、performance、devices 与外部 artifact store 约定；大 APK、原始日志和外部游戏字节不提交仓库。每个 work package 至少交付下列可定位文件：

| 文件/报告 | 最小内容 |
| --- | --- |
| work-package.json | 本文上一节的 scope、前置、命令、flag、状态和审批字段 |
| input-identity.json | source/toolchain/runtime/fixture/profile/flag/device 的 hash 与来源 |
| command-result.json | 完整非交互命令、exit code、超时、比较器与报告 SHA-256 |
| differential-summary.json | baseline↔target 的 state/display/effect/error/timeline 结论；每个差异有 stable key |
| lifecycle-memory.json | 本包相关 Node、Resource、RID、task、handle、reservation、managed/native/GPU/RSS before-after |
| rollback.json | 关闭 flag 或恢复旧 artifact 后的同一 fixture 结果；存档/导入包的备份和 journal 位置 |
| uncovered.json | 未运行的 profile、fixture、设备、故障路径、授权或环境缺口 |
| gate-decision.json | 审批人、UTC、所有输入/报告 hash、结论、ADR、失效条件 |

报告缺失、exit code 非零、fixture 授权/来源不明，或者只具有视觉截图而缺少模型/hit/trace 中应有的一项，都只能得到 Blocked 或 Uncovered。

## M3 工程包

| ID | 允许的首要边界 | 必须关闭的工程问题 | 最小报告 |
| --- | --- | --- | --- |
| M3-CORE-01 | src/Core、tools/core-contracts 与对应测试 | 扩展现有 GEmuera.Core 合同切片而非复制第二套 Session/plan/VM 定义；无 Godot 依赖和 deterministic build | core-build、architecture-boundary、public-api |
| M3-CORE-02 | Core Parsing 与 parser differential 测试 | 解析、诊断、source span 与旧 parser 的可比较合同 | parser-diff、fuzz-corpus |
| M3-CORE-03 | Core Variables 与 state snapshot 测试 | scope、Float/Ref、稀疏数组、candidate load 与跨 Session 隔离 | variable-diff、session-isolation |
| M3-CORE-04 | Core Saves 与 SaveCompatibility 测试 | profile-aware candidate codec、错误 profile 拒绝、原件不原地写回 | save-codec-diff、conversion-rollback |
| M3-CORE-05 | 单一 legacy adapter 及其测试 | 旧 runner 与候选 Core 双跑但不共享 static/cache；默认路径仍可回退 | legacy-adapter-diff、rollback |
| M3-CORE-06 | CompatibilityPlan 的窄消费端 | 只消费冻结 plan/registry/policy；不读取全局 CoreProfile | dialect-consumption、unselected-module-invariance |

当前已有的 src/Core/Compatibility、src/Core/Session、src/Core/Runtime 和 tools/core-contracts 是 M3-CORE-01 的输入，不是 M3 全部完成的证据。M3-CORE-05 之前不得把它们描述成已经接线旧 Parser/VM/Godot View。

## M4 工程包

| ID | 交付焦点 | 最小报告与回退 |
| --- | --- | --- |
| M4-RES-01 | PixelSurface 格式、坐标、alpha、revision publish 与 CPU truth | pixel-contract-diff；关闭 graphics.pixel_store 回到 LegacyGraphicsAdapter |
| M4-RES-02 | ResourceCatalog、content token、唯一 decode owner 与 reservation | resource-catalog-diff、decode-backpressure |
| M4-RES-03 | 主线程 ResourceBridge upload、generation/revision 检查、Texture/RID/Node ledger | bridge-lifecycle、stale-upload |
| M4-RES-04 | G/CBG/Sprite、dispose mode、dynamic map 与 data-only transaction 双跑 | graphics-effect-diff、display-resource-order |
| M4-RES-05 | 低/中档移动设备与桌面的预算、取消、快速切换和 ExitTree 压力 | resource-pressure、lifecycle-memory、rollback |

任何 M4 包都必须把逻辑资源 handle/revision 与 Godot 投影分开记录；GPU readback、后台线程创建 Node、依靠 GC 或 queue_free 隐藏泄漏均为失败。

## M5 工程包

M5-PORT-01 先建立唯一 port manifest；后续每个 adapter 包都必须引用同一 versioned portTypeId、capability、completion mode、最大输入/输出、取消与错误映射。新 port 未登记前不得接入 Core。

| ID | 交付焦点 | 最小报告 |
| --- | --- | --- |
| M5-PORT-01 | port 合同、manifest、completion dispatcher | port-manifest、architecture-boundary |
| M5-PORT-02 | Desktop/Android/iOS adapter 的 session-scoped factory | adapter-diff、handle-ledger |
| M5-PORT-03 | InputCoordinator、VirtualCursor 与 pointer policy | input-trace-diff、stale-input |
| M5-PORT-04 | SAF/import-cache canary、journal、原子 commit | saf-canary、revoke-recovery、old-path-rollback |
| M5-PORT-05 | SQLite/Map/XML/DataTable 生命周期与边界 | database-lifecycle、security-fault |
| M5-PORT-06 | lifecycle/audio generation reset | app-lifecycle、audio-cancel |
| M5-PORT-07 | eraFL capability 组合与未选择模块不变性 | capability-composition、two-sided-fixture |

Godot View 只向编排器发送 requestId、generation 和简单值；编排器向 session-scoped component 调用，component 用 signal 或 typed completion 向上报告。兄弟 Node/组件不得相互调用，Autoload 也不得持有游戏业务状态或跨会话 Node 引用。

## M6 工程包

| ID | 交付焦点 | 最小报告 |
| --- | --- | --- |
| M6-EXP-01 | 可重复实验定义：假设、基线、release artifact、设备、warm-up、阈值和独立 flags | experiment-definition、input-identity、threshold-approval |
| M6-EXP-02 | 全量 yieldability inventory 与同步路径分类 | yield-audit、blocking-path-review |
| M6-EXP-03 | 专用线程与 cooperative VM 的隔离双跑 | vm-dual-run、ordering-diff、cancel-latency |
| M6-EXP-04 | 候选 renderer 的 DTO/PixelStore 投影、visual/hit/accessibility/scroll golden | renderer-golden、accessibility、renderer-rollback |
| M6-EXP-05 | Android/desktop 性能、内存、快速切换、暂停恢复和 ExitTree 压力 | experiment-performance、lifecycle-memory、device-report |

阈值必须在运行前写入 experiment-definition 并由审批记录引用；禁止根据一次结果临时改阈值。自绘后端只读不可变 snapshot，Theme/StyleBox 在根 Theme 资源或实例独立副本中管理，不能在 draw/process 热路径创建或修改，也不能用透明 Control 吞掉输入。

## M7 工程包

| ID | 交付焦点 | 最小报告 |
| --- | --- | --- |
| M7-REL-01 | removal inventory、调用计数与受影响 M3–M6 gate 映射 | removal-inventory、dependency-proof |
| M7-REL-02 | 默认关闭候选路径后的两个独立发布周期观测 | zero-traffic、platform-release、compatibility-summary |
| M7-REL-03 | 单一 owner 的入口、adapter、实现分步删除 | removal-diff、architecture-guard、lifecycle-memory |
| M7-REL-04 | 前一稳定 artifact、flag、存档/SAF 备份与恢复演练 | rollback-drill、recovery-log |
| M7-REL-05 | 发布签署、限制/矩阵同步和下一周期监控 | approval、known-limitations-sync |

一个发布周期必须记录非重叠的 artifact version、观察窗口、目标平台/代表 fixture 覆盖、fallback/rollback 调用计数和异常查询；它不是“日历过了两周”或“静态搜索无引用”的同义词。任何零流量结论缺少这些字段都无效。

## 执行顺序与 Gate 公式

每个包严格按以下顺序推进：

1. 绑定前置 gate decision 与输入 identity，确认没有 ReportInvalidated。
2. 记录 allowed/forbidden paths、owner、flag、rollback 和未覆盖项，生成 work-package record。
3. 以默认关闭的路径实现最小垂直切片；不得顺带重构相邻 subsystem。
4. 运行 Core/Bridge/App 层测试、差分、fuzz、生命周期与设备检查，并保留失败原始报告。
5. 关闭 flag 或恢复旧 artifact，重放相同 fixture，验证 rollback。
6. 生成证据包，审查未覆盖项与 CompatibilityMatrix/KnownLimitations 同步。
7. 由独立的 prepared/reviewed/approved 决策写入 gate-decision；未批准时保持 ReadyForReview 或 Blocked。

Gate 仅当“前置 gate 有效、必需命令 exit code 为零、语义差分通过或有已批准 IntentionalDifference、必须设备证据存在、生命周期账本稳定、回退复现成功、未覆盖项未触及发布范围、审批完整”同时成立时为 Passed。任何条件缺失都不得用性能提升、截图或代码量抵消。

## 与阶段文档的关系

本协议统一工单和报告形状；具体语义仍分别由 [M3CoreExtraction](M3CoreExtraction.md)、[M4ResourceGraphics](M4ResourceGraphics.md)、[M5PlatformComposition](M5PlatformComposition.md)、[M6SchedulingRenderingExperiment](M6SchedulingRenderingExperiment.md) 与 [M7CleanupReleaseGovernance](M7CleanupReleaseGovernance.md) 定义。测试维度以 [VerificationPlan](VerificationPlan.md) 为准，目录/报告约定以 [ProjectStructure](ProjectStructure.md) 为准，当前实施授权始终以 [M0M2ImplementationBaseline](M0M2ImplementationBaseline.md) 为准。
