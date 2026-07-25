# 项目结构、构建单元与测试资产

## 迁移约束

下列结构是远期模块边界，不是要求先创建空项目并重写约 168 个旧 C# 文件。实际实施在可运行 gEmuera 中增量增加 `src/Core`、`Contracts` 和 `Migration`；当前已落地的是 `src/Core` 合同切片，旧 `Scripts/Emuera` 仍通过原运行链工作，文件只有在差分通过后才移动。阶段、flags 和删除门禁见 [MigrationPlan](MigrationPlan.md)。

```text
gEmuera-future/                     # M0 可运行工程保持启动/导出
├─ project.godot / gemuera-c#.csproj
├─ Scripts/                         # 旧实现，按阶段逐步收缩
├─ src/Core/                        # 标准 Microsoft.NET.Sdk；当前先落地 CompatibilityPlan/Session contracts
├─ Contracts/                       # Display v2、ports、trace schema
├─ Migration/
│  ├─ LegacySessionFacade.cs
│  ├─ LegacyConsoleAdapter.cs
│  ├─ LegacyGraphicsAdapter.cs
│  └─ FeatureFlags.cs
├─ Tests/                           # 正式回归永久保留
└─ Fixtures/                        # UP/GE/SN/FL/AN manifest 与 golden
```

## 远期目录边界

```text
GodotEmuera/
├─ global.json                         # 锁定 .NET SDK
├─ Directory.Packages.props            # 集中 NuGet 版本
├─ packages.lock.json
├─ ToolchainLock.json                  # Godot/.NET/Android/iOS 精确版本与哈希
├─ src/
│  ├─ Core/
│  │  ├─ XEmuera.Godot.Core.csproj
│  │  ├─ Common/                       # Result、limits、checked helpers
│  │  ├─ Encoding/                     # 严格 UTF-8/CP932
│  │  ├─ Parsing/                      # ERH/ERB/CSV/HTML
│  │  ├─ Expressions/
│  │  ├─ Vm/                           # runner、frame、可选 continuation/scheduler
│  │  ├─ Variables/
│  │  ├─ Saves/                        # 原版 codec、snapshot、transaction model
│  │  ├─ Resources/                    # descriptors、PixelStore、catalog
│  │  ├─ Display/                      # div/srcb/transaction/scroll DTO v2
│  │  ├─ Input/
│  │  └─ Ports/
│  ├─ Bridge/
│  │  ├─ XEmuera.Godot.Bridge.csproj
│  │  ├─ Session/                      # SessionBridge/VmHostThread/UiBatchPump/queue
│  │  ├─ Rendering/                    # backend adapters
│  │  ├─ Audio/
│  │  ├─ Resources/
│  │  └─ Platform/                     # Desktop/Android/iOS adapters
│  └─ App/
│     ├─ project.godot
│     ├─ XEmuera.Godot.App.csproj
│     ├─ scenes/                       # main、console、input、dialogs
│     ├─ themes/                       # root Theme/type variations
│     ├─ assets/
│     └─ export_presets.cfg
├─ tests/
│  ├─ Core/
│  ├─ Architecture/
│  ├─ ApiSmoke/
│  ├─ Differential/
│  ├─ Scheduling/
│  ├─ SaveCompatibility/
│  ├─ HtmlGolden/
│  ├─ EncodingCorpus/
│  ├─ ResourceCompatibility/
│  ├─ SecurityFuzz/
│  ├─ SceneIntegration/
│  └─ Performance/
├─ fixtures/
│  ├─ manifest.json
│  ├─ saves/{normal,global,var,charvar,corrupt}/
│  ├─ html/{inputs,golden}/
│  ├─ encoding/
│  ├─ resources/
│  ├─ schedules/
│  └─ malicious/
├─ tools/
│  ├─ dialect-inventory/          # M0-DIA inventory/snapshots/static descriptor, pack/port/module-composition/fixture evidence; no runtime Parser/VM dependency
│  ├─ core-contracts/             # Core contract smoke/architecture guard; no Godot reference
│  ├─ save-baseline/               # M0-SAV legacy binary source/wire inventory; no save-file I/O or runtime codec dependency
│  ├─ session-state-inventory/    # M0-SES central GlobalStatic/Program root inventory; source-only M1 input, not a facade/runtime switch
│  ├─ extract-instruction-matrix/
│  ├─ xemuera-baseline-runner/
│  ├─ compare-differential/
│  └─ verify-framework-docs/
├─ reports/{differential,build,performance,devices}/
└─ docs/adr/
```

远期拆分只有在 façade 调用量归零、代表游戏/Android 报告通过后才物理重排；不能为了目录整齐提前删除旧后端或线程。

## csproj 约束

Core 使用标准 `Microsoft.NET.Sdk`，目标框架由 ToolchainLock 决定；不引用 GodotSharp。Bridge/App 使用 Godot 锁定 SDK。Tests 通过 `ProjectReference` 引用 Core/Bridge，不链接源文件。所有 NuGet 开启 lock file 和 locked restore；版本升级必须显式更新 lock 与报告。

Core 构建门禁应设置 nullable、warnings-as-errors、deterministic build、分析器和禁止不安全代码（除非单独 ADR）。平台插件不进入 Core restore graph。

## 按功能组织

目录按 feature/业务边界组织，不建“大而全 Managers”目录。每个 Bridge feature 包含 orchestrator/component、DTO mapper、scene 测试和对应 README。View 脚本一脚本一职责；数学/解析逻辑不放 orchestrator。

## fixture manifest

当前 M0 版本化 catalog 见 `Fixtures/manifest.json`，本地路径与逐文件 hash 由 `tools/fixture-manifest` 绑定后写入外部报告目录。catalog 只声明来源/profile/授权/期望报告，不把外部游戏内容或本机绝对路径提交仓库；授权状态与字节 availability 必须分列。

每个条目至少包含：

```json
{
  "id": "save-normal-sparse-001",
  "category": "save",
  "source": "XEmuera commit or manually verified build",
  "input": "saves/normal/sparse_001.dat",
  "sha256": "...",
  "expected": "saves/normal/sparse_001.expected.json",
  "platform": "portable",
  "status": "baseline-confirmed",
  "notes": ""
}
```

二进制 fixture 不得在测试中重建后再当作原版样本；至少一组由 XEmuera 实际写出并保存哈希。恶意 fixture 要记录生成器/seed，避免把危险大文件直接提交仓库时失控。

## 报告 schema

差分报告逐行为：行为键、fixture、baseline hash、target hash、状态（Passed/Failed/IntentionalDifference/Uncovered）、差异摘要和证据链接。构建报告记录命令、exit code、工具链、平台和 artifact hash。真机报告记录设备型号、OS/target SDK、步骤、日志与脱敏截图。

M0-DIA-07 方言静态证据报告另记录 source report hash 链、上游源码相对路径与 SHA-256、公开键、证据状态、候选模块及冲突状态。当前 326/360 项中，上游同名为 284/243、显式当前模块候选 28、未决 131、证据冲突 8；`UpstreamNameMatch`/`ExplicitCurrentModuleCandidate` 不得序列化成 `ResolvedOwnership`。

M0-DIA-08 名称查找报告继续记录锁定的 lookup 源文件、326/360 注册、9 个碰撞、351 个函数投影、677 项指令 lookup surface、684+2 key domain、comparer/normalizer capture、`_Rename` SourceTextRewrite stage 及 semantic 状态。旧 `ICVariable` comparer 捕获和 `ICFunction` current-culture `ToUpper` 只能作为 D2 设计输入；全部 686 项 semantic alias/replacement 必须保持 `Unresolved`。`NewFrameworkDesign/generated` 中的 DIA 报告是可重建证据，不是 Parser/VM 的运行时输入；DIA-08 的 `parserVmConsumption=NotConsumed` 只表示该静态报告本身未接线到 legacy Parser/VM 行为。独立 startup/parser 已消费窄 `CompatibilityPlan` descriptor presence/ownership guard，验证 plan identity/profile/hash 与 legacy descriptor presence/module ownership，但不替换 legacy handler 或执行 typed policy；这不代表 D1/D2 已实现。

M0-DIA-09 模块可见性报告继续引用 DIA-02/07/08 的 hash 链，并只枚举 DIA-07 的 131 个 ownership 未决 key：123 个 `V24VisibleCandidate`、8 个 `SnakeOnlyCandidate`、0 个 missing Snake projection（指令 8/6，表达式函数 115/2）。每项保留 v24/Snake projection moduleId/currentContribution 和 lookup contract，避免把测试投影成员关系误当稳定分发 module id、最终 owner、`AliasOf` 或 `ReplacementDeclaration`。它同样为 `currentRuntimeIsolation=Failed`、ownership=`Unresolved`；其静态报告的 `parserVmConsumption=NotConsumed` 只表示报告本身未接线到 legacy Parser/VM 行为。独立 startup/parser 的窄 `CompatibilityPlan` descriptor presence/ownership guard 验证 plan identity/profile/hash 与 legacy descriptor presence/module ownership，但不替换 legacy handler 或执行 typed policy；它不是 D1/D2 的运行时输入。

M0-DIA-10 的 `tools/dialect-inventory/DialectPlanPreflight.psm1`、版本化 catalog/schema 和 `generated/dialect-plan-preflight.json` 是下一层离线会话预检：只接受 DIA-02 的 `v24`/`snake` 投影与 M0-SES-01 inventory，深复制为 `v24pure`/`snake` 证据 DTO，并为未来多版本解释器分发保留 profile→legacy enum→projection 的明确接口。它的语义 hash 不包含请求来源或 generation，完整 set hash 仍包含可复现证据；`SnakeModernMobile` 必须保持 `Uncovered`。该目录中的文件不是运行时 plan、Parser/VM 输入或 session façade，状态固定为 runtime Failed、NotConsumed、NotImplemented、M1 Blocked。

M0-DIA-11 的 `DialectProfileSelection.psm1`、versioned catalog/schema 与 `generated/dialect-profile-selection.json` 继续把多版本分发入口前移为只读源码证据：它锁定 `FirstWindow`、`Program`、`LegacyRunnerConfig` 的 SHA-256、launch profile normalizer、modern/legacy marker 文件及真实 precedence。报告只能告诉未来 resolver 哪些旧输入目前存在以及 `SnakeModernMobile` 为什么不能外推为 Snake；它不读取游戏目录、不调用 marker、不是 runtime resolver，也不改变 M1/D2 状态。

M0-DIA-12 的 `DialectCompatibilityPack.psm1`、versioned catalog/schema 与 `generated/dialect-compatibility-pack.json` 继续在工具层预留多版本分发边界：它只消费 DIA-10/11 hash，验证 `emuera.compatibility-pack/v1` 的内置 module、显式 capability arrays 与不可执行载荷规则。当前只有 `v24pure`=`gemuera.v24` 和 `snake`=`gemuera.v24+game.snake`；`legacy.current.*` 只能作为 static projection support，不能成为分发 module id。报告不绑定游戏内容或存档、拒绝 `SnakeModernMobile`/未知 module/DLL/type/script/path/URL，且明确为 content=`NotBound`、save/fixture=`Uncovered`、distribution=`Blocked`、runtime resolver/plan=`NotImplemented`。

M0-DIA-13 的 `DialectDeclarationVocabulary.psm1`、versioned catalog/schema 与 `generated/dialect-declaration-vocabulary.json` 位于同一离线工具边界：它把当前 DIA-01 的 30 classification、97 branch hit 聚合为 10 个 `BehaviorKey` 和 4 个 `CapabilityId`，并逐项保留 target module、fixture 与命中 provenance。全部只标为 `StaticCandidate`/`NotEligible`；工具还读取 DIA-12 并断言两个 pack 没有 allowed/required/optional runtime capability。该目录中的词汇表不是 policy manager、runtime module catalog、manifest allowlist 或 Parser/VM 输入。

M0-DIA-14 的 `DialectPolicyConsumerBoundary.psm1`、versioned catalog/schema 与 `generated/dialect-policy-consumer-boundary.json` 继续留在离线工具边界：它钉扎 DIA-01/DIA-13 hash，把 10 个 BehaviorKey 映射到 10 个唯一 future consumer contract/decision owner，并深复制 14 个 classification 与 16 个 source-file binding、current/intended owner、module、fixture。`ConfigurationInput` 只能供应 future decision consumer；它不能成为 sibling direct call，scoped config schema 与 `FunctionIdentifier` registration guard 的角色因此显式分离。报告不是 C# policy interface/manager、resolver、CompatibilityPlan、frozen registry 或 Parser/VM runtime input，状态固定为 `BoundaryDraftOnly`/`NotImplemented`、runtime Failed/NotConsumed、M1 Blocked。

M0-DIA-15 的 `DialectPolicySurface.psm1`、versioned catalog/schema 与 `generated/dialect-policy-surface.json` 在同一离线目录中把 DIA-14 的 owner boundary 进一步分配为 future `portTypeId`：6 个 `PolicyDecision`、2 个 `BridgeProjection`、2 个 `FrozenCatalogContribution`。每个 port 仍深复制 source binding/provenance，但强制 `InterfaceDraftOnly`、`policyValueStatus=Unspecified`、`runtimeStatus=NotImplemented`；它不新增任何 C# type、method、assembly、reflection 或运行时输入。该目录只为 D3 的 API review 和两侧 fixture 计划提供稳定名字，不能被误当 runtime policy manager/resolver/plan/D2/Parser/VM 或 M1 façade。

M0-DIA-16 的 `DialectModuleComposition.psm1`、versioned catalog/schema 与 `generated/dialect-module-composition.json` 仍位于同一离线工具边界：它只读取 DIA-12 CompatibilityPack 与 DIA-15 policy surface 的 generated JSON，输出 2 个 `StaticCandidate` module、1 条 dependency、10 个 port declaration 和 2 个 profile closure。`gemuera.v24@1.0.0` 是零依赖/零 port 基础，`game.snake@1.0.0` 以 `[1.0.0,2.0.0)` 依赖它并贡献全部 future port；报告用稳定 dependency-first 顺序投影 `v24pure`/`snake`。该文件不是 module assembly、reflection/type scan、runtime catalog/resolver/plan/D2/Parser/VM 输入，也不绑定内容、存档、fixture 或 runtime capability。

M0-DIA-17 的 `DialectBehaviorFixtureContracts.psm1`、versioned catalog/schema 与 `generated/dialect-behavior-fixture-contracts.json` 同样只在离线证据层工作：它读取 DIA-13 的 BehaviorKey 词汇和 DIA-15 的 future port，深复制为 10 个 fixture contract。每项只列既有 fixture ID、`v24pure`/`snake` 双侧 profile、baseline/extension/undeclared 情形和 input/result/error/completion/effect 记录要求；严格不保存 policy value、方法签名、DTO 或可执行载荷。所有项固定 `Planned`/`Uncovered`/`BlockedByFixture`，因此报告不能作为 C# type、policy manager、resolver、CompatibilityPlan、D2 registry 或 Parser/VM runtime input。

M0-SAV-01 的 `tools/save-baseline/LegacySaveBaseline.psm1`、versioned catalog/schema 与 `generated/legacy-save-baseline.json` 同样是离线证据：它只读取五个 legacy source 文件，钉扎 1808 header、4 file type、19 data type、11 sparse marker、读写入口与两个 normal load mutation literal。`Float=0x20..0x23` 和上游 0x20–0x22 解释冲突固定为 `StaticConflictObserved`，`automaticSelectionStatus=Unbound`。这个目录不枚举、读取、复制或写入任何 `.sav/.dat`，也不创建 codec registry、SaveProfile resolver、candidate store、session façade 或 Parser/VM runtime input。

M0-SES-01 报告只覆盖 central legacy root：`GlobalStatic` 的 14 个直接静态字段与 `Program` 的 17 个 mutable static/private-set property。每项有 catalog owner、候选 M1 owner、fixture、reset/引用位置；当前静态可见 14 个中心 reset site（12 个 `GlobalStatic.Reset()` 清理和 canary-only `ctrlZ`、debug `StackList` 清理），479 个直接引用来自 56 文件。`StackList` 仅在 `UEMUERA_DEBUG` canary 下有清理证据。它不覆盖其它 static cache、Godot/platform/diagnostic state，也不创建 `LegacySessionFacade`、feature flag、plan 或 Parser/VM runtime input；必须保持 `InProgress/Blocked/Partial`。

## 命名约定

- Core 类型用业务名：`VmFrame`、`SaveSnapshot`、`ResourceDescriptor`，不使用 `Manager` 泛化所有职责。
- Godot Node 后缀只用于 Bridge/View：`UiBatchPumpNode`、`ConsoleViewportControl`；`VmHostThread` 是 CLR service，不伪装 Node。
- `Dto` 只用于跨边界数据，内部领域模型不滥用 DTO。
- async 方法以 `Async` 结尾并接收 CancellationToken；生命周期事件携带 generation。
- 逻辑资源使用 `ResourceKey`，平台文件使用 `ContentToken`，不互相当字符串路径。

## 文档与示例

完整可编译 C# 示例必须移入 `tests/ApiSmoke` 或示例工程，并由文档链接符号。文档中的片段统一标“伪代码”。源码引用只用路径+符号，不写漂移行号。

## 测试层级

1. Core unit：解析、变量、codec、scheduler，完全无 Godot。
2. Differential：同一 fixture 跑 XEmuera 和目标 Core。
3. Architecture：assembly/namespace/API 边界。
4. ApiSmoke：锁定 Godot C# API 编译。
5. Scene integration：F6、signal、生命周期、输入传播。
6. Export smoke：桌面/Android/iOS 最小启动和文件导入。
7. Performance/device：固定硬件/语料采样。

## 测试资产生命周期

正式 xUnit/GDUnit、architecture guard、differential fixture、HTML/image golden、fuzz seed、benchmark scenario 与 device protocol 是产品资产，必须提交或以 manifest/hash 长期归档。只有一次性诊断探针、临时日志和可由生成器稳定重建的巨大输出可以在任务结束删除。删除正式回归需要独立评审和等价覆盖证明；旧 `AGENT.md` 的笼统“删除测试文件”不适用于新工程治理。

## CI 阶段

`restore-locked → core-build → architecture → unit → differential → docs → api-smoke → export-smoke → publish-reports`。移动真机和 iOS 导出可为受控 runner，但缺失时发布状态必须是 Uncovered，不能绿色替代。
