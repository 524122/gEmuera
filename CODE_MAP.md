# CODE_MAP

- 2026-07-17 dev replacement verification: restored the documented `PrototypeRuntimeNode` command subscriptions after the implementation had diverged from its regression suite. Reload/Toggle/Detach commands are serialized, all prototype bridges follow the committed generation, and shutdown drains the active command before disposing the Core runtime. `PrototypeHostRegressionTest.gd` passed 4/4 with Godot 4.7 Mono headless (`0 failures`, `0 orphans`).

- 2026-07-16 prototype host lifecycle hardening: `PrototypeRuntimeNode` now fences post-cancellation bridge/UI work and awaits the active command in a shutdown task before disposing the Core runtime; `PrototypeResourceBridge` has explicit generation attach/detach state so same-generation late projections are rejected after detach; `PrototypeHostRegressionTest.gd` asserts both actual input queue admission and the observational non-consuming default. No test, TDD, ABA, smoke, game, or device run was performed for this follow-up.

- 2026-07-16 prototype host TDD follow-up: `Scripts/GodotHost/PrototypeRuntimeNode.cs` now owns the `PrototypeCommandPanel` subscriptions for same-profile reload, v24pure/Snake toggle, and explicit session detach; input/audio/resource bridges follow the committed generation and detach order. `PrototypeInputBridge.ConsumeUnhandledInput` now defaults to `false`, so the observational bridge still normalizes/queues unhandled events without preventing later `_UnhandledInput` consumers unless a scene explicitly opts in. `test/GodotHost/PrototypeHostRegressionTest.gd` locks these four public behaviors with a focused GDUnit4 RED-to-GREEN suite.

- 2026-07-16 first prototype: `src/Core/Application/GameSession.cs` composes the session-scoped Core runtime; `src/Core/Resources/ResourceRuntime.cs`, `src/Core/Save/SaveService.cs`, `src/Core/Ports/RuntimePortHub.cs`, `src/Core/Experiments/PrototypeScheduler.cs`, and `src/Core/Governance/RuntimeReleaseLedger.cs` provide the first M3-M7 runtime surfaces. `Scripts/GodotHost/Prototype*.cs`, `main.tscn`, `project.godot`, `Scripts/FirstWindow.cs`, and `src/Core/Ports/InputStorageContracts.cs` add the observational Godot prototype bridges and configurable Era library root. The legacy parser/thread/render route remains the default behavior owner; no test, ABA, or smoke result is implied.

- M1 async lifecycle follow-up: `SpriteManager.ForceClear` now waits on an explicit `ManualResetEventSlim` worker-idle boundary after epoch invalidation before draining image results. This prevents stale decode payloads crossing sessions; four-switch semantic and mutation evidence passes, but the Snake memory increment remains unresolved.

- M1 follow-up: `Program.ResetSessionState` and `GlobalStatic.ResetCanarySessionState` now clear candidate-scoped path/profile/analysis/reboot and `ctrlZ` roots. Runner-owned log sinks remain process-scoped by design; a regression that mutated the alternate fixture (`changeCount=2`) was fixed and the rerun returned zero mutations. Memory growth remains unresolved.

## 2026-07-15 M1 canary cleanup evidence

- `Scripts/GodotHost/LegacySessionBackend.cs` now tracks canary-only lifecycle state and releases the Godot view/resource boundary only after worker quiescence on the Godot main thread; the M0 baseline stop path is unchanged.
- `Scripts/EmueraContent.cs` clears HTML/CBG nodes and layers, texture pins, audio streams, font cache, and GraphicsImage texture cache at canary transitions. `AppContents.UnloadContents` disposes sprite owners before dropping dictionaries; `SpriteManager.ForceClear` avoids forced GC.
- Real four-switch cross-ABA evidence kept semantic fingerprints stable and fixture mutation counts at zero, but retained roughly 290 MB per snake activation (final private bytes about 1.20 GB, working set about 1.25 GB). M1 memory isolation remains blocked and M2-M7 are not started.

## 2026-07-14 M1 Core runtime contract slice

| Path | Types/entry points | Responsibility | Boundary |
|---|---|---|---|
| `src/Core/Compatibility/DialectRuntime.cs`, `CompatibilityProfileCatalog.cs`, `BuiltInDialectCatalog.cs` | `IDialectModule`, `DialectModuleCatalog`, `CompatibilityProfileCatalog`, `CompatibilityProfileDefinition`, `CompatibilityPlanBuilder`, `DialectPlan`, `CompatibilityPlan` | Explicit dialect composition, dependency/version validation, registration-time module snapshots, one-way composition freeze at builder/facade startup, immutable profile→trusted module-root projection, frozen instruction/function registries, deterministic hashes | Compile-time allowlist only; no content resolver/reflection loading, no legacy Parser/VM wiring, no Godot types |
| `src/Core/Session/SessionCoordinator.cs` | `SessionCoordinator`, `SessionCandidate`, `SessionSwitchResult` | Generation/cancellation/candidate/short commit gate; stale completions cannot replace the current session | Does not own `GlobalStatic`, Godot view, or resource cleanup |
| `src/Core/Session/LegacySessionFacade.cs` | `LegacySessionFacade`, `ILegacySessionBackend` | Candidate plan → stop/start legacy backend → short Core commit; failed/cancelled/stale activation rolls back | Transaction shell only; old Parser/VM remains the semantic owner |
| `src/Core/Runtime/ErbExecution.cs` | `IErbInterpreterFactory`, `ErbInterpreterKey`, `ErbInterpreterDescriptor`, `IErbInterpreterCatalog`, `ErbInterpreterCatalog`, `IErbInterpreterHost`, `ResumableErbInterpreterHost`, `VmStepResult`, `VmCompletion`, typed effects | Explicit `(engine id, engine version)` ERB selection plus a descriptor-snapshotted frozen reader and reusable Step/Resume state machine; the catalog injects its frozen descriptor into each Host and rejects key/hash mismatches, exact selection is O(1), ambiguous unversioned selection fails fast, and the host validates module/version support, per-call instruction/work budget, session completion, wait operations, effect ordering, terminal states, rejects exposed internal `Created`/`Running` results, and keeps a host cancelled when a late async result arrives after disposal | Compile-time registration only; no Parser/VM semantics, content resolver, reflection/DLL loading or Godot types |
| `Scripts/GodotHost/LegacySessionLaunchRegistry.cs`, `LegacySessionBackend.cs`, `Scripts/EmueraMain.cs`, `Scripts/M0/LegacyRunner*.cs`, `Scripts/Diagnostics/RuntimeDiagnosticsConfig*.cs` | immutable `(gameId, profileId)` host routes, `Start/StopLegacyBaselineAsync`, runner-only `cross-aba`, `migration.session_isolation` | The Godot host resolves opaque Core selections to pre-registered legacy game roots/profiles, owns canary reset/start/stop, and lets only the explicit runner observe A→B→A; startup-only default-false canary records its flag in diagnostics | Core never sees paths; no normal UI hot switch; no Parser/VM `CompatibilityPlan` consumption |
| `tools/core-contracts/` | `TestModules.cs`, extended `Program.cs` smoke | v24/Snake isolation, registry fail-fast, interpreter catalog, hash stability, session race and Core dependency checks | Test-only; does not replace Godot ApiSmoke or legacy runner |

`gemuera-c#.csproj` references the `net8.0` Core assembly while excluding `src/Core` and `tools/core-contracts` source trees from its default compile glob; the M0 bridge remains the default rollback path. Core validation currently uses `net10.0` because that targeting pack is available offline; final net8/net9 host targeting remains a ToolchainLock decision.

## 2026-07-13 M0-DIA-01/02/03/04/05/06/07/08/09/10/11/12/13/14/15/16/17 方言库存、注册快照、静态 Descriptor、归属/可见性证据、会话预检、兼容包、声明词汇、接口表面、模块组合与行为 fixture 契约

- `tools/dialect-inventory/DialectInventory.psm1`：只读扫描 `Scripts/**/*.cs` 的 legacy profile/setting marker，并机械提取 `FunctionIdentifier` 静态公共、v24、Snake 指令贡献及 `Creator.cs` 表达式函数字典。未分类 marker、同一注册表重复公开键或必需 C# block 缺失直接失败；不实例化旧 handler、不改变注册行为。
- `dialect-classification.json`：D0 人工裁决层；每个分支命中必须记录 current/intended owner、目标模块、BehaviorKey 或 CapabilityId、fixture ID。`SNAKE_*`/`Snake*` 类型名只生成 provenance warning，不能自动证明模块归属。
- `Invoke-DialectInventory.ps1`、`Test-DialectInventory.ps1`：生成版本化 `NewFrameworkDesign/generated/dialect-inventory.json`，并验证真实仓库零未分类命中、重复键拒绝、源码变化改变 hash、创建/枚举顺序不影响 canonical hash。该工具不启动 Godot/游戏，不读取 `E:\MyCode\Era`。
- `dialect-inventory.schema.json`、`dialect-classification.schema.json`：固定报告与分类 catalog 结构；阶段状态强制保持 `InProgress + Blocked/EvidenceMissing + Partial`。
- `New-DialectRegistrySnapshot`：把 inventory descriptor 深复制为按 selected module 过滤、ordinal 排序的测试 DTO；available-but-unselected module 不进入 hash，选择集合内重复公开键失败。`legacy.current.common/expression` 只是 unresolved 暂存桶，不能当作正式分发模块 ID。
- `Invoke-DialectRegistrySnapshot.ps1`、`Test-DialectRegistrySnapshot.ps1`、`dialect-registry-snapshots.schema.json`：生成 `generated/dialect-registry-snapshots.json`；固定 v24=290/358、Snake=326/360、Snake-only=36/2，以及选择顺序/未选择模块不变性、深复制、缺模块和重复键契约。
- 当前结论分层：测试投影 invariant=`Passed`；旧 runtime isolation=`Failed`，因为静态构造仍无条件调用两组注册。M0 状态保持 `InProgress + Blocked/EvidenceMissing + Partial`，没有接入 D1 plan 或 D2 runtime frozen registry。
- `DialectSignatureInventory.psm1`：独立只读 signature scanner；按字符串/注释安全的 C# class block 提取 instruction registration API、ArgumentBuilder/handler-owned 参数候选、additional/handler flags，以及 FunctionMethod 的 ReturnType/argumentTypeArray/CheckArgumentType/CanRestructure 来源。它不执行或反射加载 handler。
- `Invoke/Test-DialectSignatureInventory.ps1`、`dialect-signature-inventory.schema.json`：生成 `generated/dialect-signature-inventory.json`；326 个 instruction source 全解析、0 unknown binding，参数 214 Resolved/112 Conditional；360 个 function source/argument 全解析，return 358 Resolved/2 Conditional；31 个 InputWait candidate。completion/effect 只能是 StaticCandidate，behavior fixture 全部 Uncovered。
- M0-DIA-03 descriptor set SHA-256=`98f26b1eda74d9f4fcfe91f8ec756e6fb93ad358087c9ad35ee3ca69c461ba8a`；不改变 M0-DIA-02 snapshot hash，也不是生产 `InstructionDescriptor`/`FunctionDescriptor`。
- `instruction-signature-resolution.json`：M0-DIA-04 版本化裁决 catalog；19 条 `handler + Exact/PublicKeyRegex` 规则逐公开键选择 DIA-03 已有候选，并用 `expectedMatchCount` 锁定源码漂移。规则不能命中已 Resolved 项，漏匹配、双重匹配、重复 ID/selector、陈旧 source hash 或候选越界均失败。
- `DialectSignatureResolution.psm1`、`Invoke/Test-DialectSignatureResolution.ps1` 与两份 schema：生成 `generated/dialect-signature-resolution.json`，保留 214 条唯一静态签名，并把 112 条 Conditional 标记为 `ResolvedStaticByRule`；0 unresolved、19 rules，catalog hash=`75818560...72c9e`、resolution set hash=`34b57e36...5eb8c`。behavior fixture 仍全部 `Uncovered`，且不接入旧 Parser/VM。
- `function-return-resolution.json`：M0-DIA-05 函数返回裁决 catalog；`GETCONFIG`/`GETCONFIGS` 两条 Exact 规则分别选择 `EraType.Integer/String`，同样固定 source descriptor hash、预期匹配数和候选边界。
- `New-DialectFunctionSignatureResolutionReport`、`Invoke/Test-DialectFunctionSignatureResolution.ps1` 与两份 schema：生成 `generated/dialect-function-signature-resolution.json`，保留 358 条唯一返回类型并解析 2 条 Conditional，连同 360 个已解析参数形成 360/360 `CompleteStatic` 函数 signature。catalog hash=`df38daa6...a7edc`、resolution set hash=`125ede37...b27a6`；completion/effect 与 behavior 仍是 Candidate/Uncovered。
- `instruction-flag-resolution.json`：M0-DIA-06 固定 17 个旧 flag 常量和 35 条 additive contribution 规则；规则允许有意重叠并做集合并集，但每条固定 handler、Exact/锚定 regex、预期匹配数和候选来源。
- `DialectInstructionFlagResolution.psm1`、`Invoke/Test-DialectInstructionFlagResolution.ps1` 与两份 schema：将 DIA-03 flag 来源和 DIA-04 参数投影合成 326 项测试 descriptor；67 `DirectRegistrationStatic`、136 `HandlerSingleStatic`、123 `HandlerRulesStatic`、0 unresolved。它显式排除 TWAIT 局部 flag 与 AWAIT/INPUTMOUSEKEY 注释噪声；catalog hash=`659b48d9...7bac3`、resolution hash=`fea357f3...73a21`。
- `DialectOwnershipEvidence.psm1`、`Invoke/Test-DialectOwnershipEvidence.ps1`、`dialect-ownership-evidence.json` 与两份 schema：M0-DIA-07 锁定显式上游源码路径/hash，并把 DIA-01～06 的 326 个指令和 360 个表达式函数与上游公开键做 ordinal 名称证据对照；284 个指令、243 个函数获得 `UpstreamNameMatch`，28 个指令获得 `ExplicitCurrentModuleCandidate`，仍有 14 个指令和 117 个函数（合计 131）为 `Unresolved`。8 项 `CurrentTargetDiffersFromUpstreamCandidate` 只记录证据冲突，不自动覆盖当前分类。
- DIA-07 catalog hash=`efd15e50...c87c`、最终 evidence set hash=`7670ba17557d0ced98242e9a234dd13657b127400bf3b65aff30a1d612a71bbd`。上游同名仅证明 provenance，不证明行为或最终 ownership；DIA-07 自身不裁决 comparer/alias/replacement，behavior/completion/effect 仍为 `Uncovered`。
- `DialectNameLookupContract.psm1`、`Invoke/Test-DialectNameLookupContract.ps1`、`dialect-name-lookup-contract.json` 与两份 schema：M0-DIA-08 锁定 8 个旧 lookup 源文件及 DIA-01/DIA-07 hash 链，固定 326 个指令注册、360 个表达式函数、9 个跨表碰撞、351 个函数到指令投影和最终 677 项旧指令 lookup surface；686 个公开键的 lookup contract 均有静态证据。
- DIA-08 捕获的旧事实包括：指令字典在静态初始化时按 `Config.ICVariable` 选择 `OrdinalIgnoreCase/Ordinal` comparer；表达式字典为 `Ordinal`，但 `Config.ICFunction=true` 时先做 current-culture `ToUpper`，该文化相关行为只是风险证据，不是未来 D2 comparer 决策；`_Rename.csv` 的 `[[name]]` 在词法分析前执行 `SourceTextRewrite`，不是 registry alias/replacement。
- 686 个公开键中 684 个为 ASCII uppercase、2 个为非 ASCII 或 mixed-case；全部 686 项 semantic alias 和 semantic replacement 仍分别为 `Unresolved`。DIA-08 catalog hash=`46e323398dd30160b932487262ae8fbc48ae8a28e49842e62962ecefd16ec25a`、contract set hash=`b4efb0475cfb8f681d81a0f13262ba0d0c62ec7c538f0164a846257622b9294d`；`currentRuntimeIsolation=Failed`、`parserVmConsumption=NotConsumed`，不创建 D1 plan，也不实现 D2 frozen registry。
- `DialectModuleVisibility.psm1`、`Invoke/Test-DialectModuleVisibility.ps1`、`dialect-module-visibility.json` 与两份 schema：M0-DIA-09 只消费 DIA-02 v24/Snake 测试投影、DIA-07 ownership evidence 和 DIA-08 lookup contract，并将 131 个 `Unresolved` 逐 key 固定为 static projection-membership evidence：123 个 `V24VisibleCandidate` 与 8 个 `SnakeOnlyCandidate`（指令 8/6，表达式函数 115/2），0 个未出现在 Snake 投影。
- DIA-09 catalog hash=`4fffa2541a39b5f4d62ec3f5aa04dfab6f8a2b04e8af5a63b7019d08c6d9574e`、visibility set hash=`bc67a40875ead208098447a0b91acc163242412540f658a9544a208ebf0f4b1d`。可见性不是最终 module ownership、AliasOf/ReplacementDeclaration 或行为兼容：报告保留原 current contribution/target，继续为 `ownership=Unresolved`、`currentRuntimeIsolation=Failed`、`parserVmConsumption=NotConsumed`，不创建 D1 plan 或实现 D2 runtime frozen registry。

- `DialectPlanPreflight.psm1`、`Invoke/Test-DialectPlanPreflight.ps1`、`dialect-plan-preflight.json` 与两份 schema：M0-DIA-10 只读取 DIA-02 的 v24/Snake 测试投影和 M0-SES-01 的 root-state inventory，生成不可变的 source-only preflight DTO；`v24pure` 固定映射 `V24Pure`/`v24`=290/358，`snake` 固定映射 `Snake`/`snake`=326/360。语义 hash 只覆盖 profile/enum/projection 及静态注册证据，不含 selection source、requested generation 或报告生成时间；完整 reproducibility hash 另行保留全部证据。
- `SnakeModernMobile` 被显式列为 `Uncovered`，不能因为名称或当前 marker 自动推定为 Snake 等价；缺失 DIA-02 独立投影时请求会 fail-fast。catalog 枚举顺序、A→B→A 请求顺序和源数组后续变异不得改变同一静态语义快照。
- DIA-10 catalog hash=`4e9b62ba6bcb0e0f394a0fe6991f5ba33ecfa5faf6260dd971227fa814a97d5d`，preflight set hash=`6218c734b4b5786ed7507966c624e04a6746ee8abfc9144983c1bd8405e40b8a`。它明确保持 `currentRuntimeIsolation=Failed`、`parserVmConsumption=NotConsumed`、`compatibilityPlanRuntime=NotImplemented`、`m1Eligibility=Blocked`；不创建 `LegacySessionFacade`、feature flag、resolver、D2 frozen registry 或 Parser/VM runtime input，也不启动 Godot/游戏。

- `DialectProfileSelection.psm1`、`Invoke/Test-DialectProfileSelection.ps1`、`dialect-profile-selection.json` 与两份 schema：M0-DIA-11 只读 `FirstWindow.cs`、`Program.cs`、`LegacyRunnerConfig.cs` 和 DIA-10 report，按 source SHA-256 固定旧 `CoreProfile` enum、启动器 normalizer、marker 文件和 `DetectCoreProfile` 的实际优先级。它不读取游戏目录、不调用 marker、不修改 `Scripts`。
- 当前源码优先级固定为：empty `ExeDir`→`V24Pure`；launcher `snake`→`Snake`（优先于 marker）；`modern_core.txt`/`snake_modern_core.txt`→`SnakeModernMobile`；`snake_core.txt`/`legacy_snake_core.txt`→`Snake`；默认→`V24Pure`。启动器和 M0 runner 只接受 `v24pure`/`snake`，未知启动器值回退 `v24pure`；`SnakeModernMobile` 仅为 marker-only，DIA-10 evidence 与 runner 均为 `Uncovered`/`Unsupported`。
- DIA-11 catalog hash=`110683661d70cf128e84756b30b01179d1b03bb6671db2c19b1b3e70e3273993`，selection set hash=`48d6d2187a70394b71c373e9dd714fd192e660fbb055c9bbf659987d8843aa0f`。报告继续保持 `currentRuntimeIsolation=Failed`、`parserVmConsumption=NotConsumed`、`compatibilityPlanRuntime=NotImplemented`、`m1Eligibility=Blocked`；它是未来 resolver 的静态输入，不是 resolver 或运行时多版本分发实现。

- `DialectCompatibilityPack.psm1`、`Invoke/Test-DialectCompatibilityPack.ps1`、`dialect-compatibility-pack.json` 与两份 schema：M0-DIA-12 只读取 DIA-10 静态预检和 DIA-11 选择证据，并以精确 hash 钉扎 versioned `emuera.compatibility-pack/v1` 声明。它只允许 evidence-backed 的 `v24pure`/`snake`，对应内置 `gemuera.v24` 与 `game.snake`；`legacy.current.common/expression` 只保留为 static projection support，绝不能提升为正式分发 module id。
- catalog 的 capability 数组必须显式存在（当前均为空，不能伪称 runtime capability 已验证）；存档/fixture=`Uncovered`、内容绑定=`NotBound`、分发资格=`Blocked`。未知模块、`SnakeModernMobile`、DLL/程序集/类型/脚本/URL 等可执行载荷、缺失 capability 声明和来源 hash 漂移均 fail-fast。catalog hash=`5f36cc13dbf6b051d0bfde3837e0cc772128a9e8d734f26e583e9f9227f3059f`，compatibility pack set hash=`425a97c0eecb750997f72adf3e1a1574c0523a88af32ab87caec7e0a015d51a3`。
- DIA-12 仍为离线声明契约，不读取游戏目录或 manifest、不计算 fingerprint、不创建 runtime resolver/CompatibilityPlan/LegacySessionFacade/feature flag/D2 frozen registry，也不进入 Parser/VM；`currentRuntimeIsolation=Failed`、`parserVmConsumption=NotConsumed`、runtime plan/resolver=`NotImplemented`、M1=`Blocked` 必须保持。

- `DialectDeclarationVocabulary.psm1`、`Invoke/Test-DialectDeclarationVocabulary.ps1`、`dialect-declaration-vocabulary.json` 与两份 schema：M0-DIA-13 只读取 DIA-01 inventory 与 DIA-12 static CompatibilityPack report，按它们的 hash 把 83 个已分类 branch hit 固定为 10 个 `BehaviorKey`、4 个 `CapabilityId` 的静态声明词汇。每个条目锁定 kind、id、命中数、classification id、target module 和 fixture id；catalog/来源枚举顺序、未知 declaration、计数/集合/hash 漂移都会 fail-fast。
- DIA-13 的 `StaticCandidate` 只表示源码分类 provenance；全部为 `currentPackEligibility=NotEligible`、`runtimeStatus=NotImplemented`。它额外验证 DIA-12 两个 static pack 的 allowed/required/optional capability arrays 均为空，禁止把词汇候选提前暴露成 runtime capability。catalog hash=`654914eaf40d1dc28021a6efe6e42e44f34c6fff9664af0d84e6c4c35625b02e`，vocabulary set hash=`61bee2b1b946b752d31bc7ea4afa52049398233097a473d9f7d35f8a11e27521`。
- DIA-13 不决定 policy 默认值、错误/完成/effect 语义、行为兼容或 manifest allowlist，不创建 runtime catalog/policy manager/resolver/CompatibilityPlan/LegacySessionFacade/D2 registry，也不接入 Parser/VM；仍为 runtime isolation=`Failed`、Parser/VM=`NotConsumed`、runtime plan/resolver=`NotImplemented`、M1=`Blocked`。

- `DialectPolicyConsumerBoundary.psm1`、`Invoke/Test-DialectPolicyConsumerBoundary.ps1`、`dialect-policy-consumer-boundary.json` 与两份 schema：M0-DIA-14 只消费 DIA-01 inventory 与 DIA-13 vocabulary，按两者 hash 为 10 个 `BehaviorKey` 固定 10 条唯一 future consumer contract/decision owner 边界、14 个 source classification binding 和 16 个 source-file binding。每项保留 current/intended owner、target module、fixture 与 source role；`instruction.scoped-variable-registration.v1` 的 config schema 必须为 `ConfigurationInput`，而 registration guard 才是 `DecisionConsumer`，禁止两个未来组件横向直接耦合。
- DIA-14 catalog/reported boundary 均固定 `BoundaryDraftOnly`/`NotImplemented`，并反向要求 DIA-13 的 runtime capability exposure 为空。陈旧 DIA-01/13 hash、未知 BehaviorKey、重复 consumer contract/decision owner、错误 source role、来源集合或枚举顺序漂移均 fail-fast；catalog hash=`5a99d3324b769d881cc6c67fd946fcb6198f1ed82067e93e4ee297757b0f35c8`，boundary set hash=`1a47214170ebfe641ef9f990c3bb9dd0138a5fb1cda69f899b009a7fcdb39938`。
- DIA-14 是 future narrow consumer ownership 的静态审计，不实现 C# policy interface/manager、resolver、runtime CompatibilityPlan、LegacySessionFacade、feature flag、D2 frozen registry 或 Parser/VM 接线；必须保持 runtime isolation=`Failed`、Parser/VM=`NotConsumed`、runtime plan/policy manager=`NotImplemented`、M1=`Blocked`，不启动 Godot/游戏或读取 Era 游戏库。

- `DialectPolicySurface.psm1`、`Invoke/Test-DialectPolicySurface.ps1`、`dialect-policy-surface.json` 与两份 schema：M0-DIA-15 只消费 DIA-14 boundary report（并保留 DIA-13 provenance hash），将 10 个 BehaviorKey 进一步分配为 10 个唯一 future `portTypeId`：6 个 `PolicyDecision`、2 个 `BridgeProjection`、2 个 `FrozenCatalogContribution`。port type 仅是后续 C# 接口的静态命名，绝不声明方法签名、默认值或实现。
- DIA-15 强制 `InterfaceDraftOnly`、`policyValueStatus=Unspecified`、`runtimeStatus=NotImplemented`，并深复制 DIA-14 的 current/intended owner、source binding、module 和 fixture。陈旧 DIA-14/DIA-13 hash、未知 BehaviorKey、重复 port type、decision owner 漂移、提前指定 policy value 或 runtime capability exposure 均 fail-fast；catalog hash=`19463afc6f16d718c58639fcfdf945c40a09e138d814d5fbe3865eb71f5d2518`，policy surface set hash=`9b3e721894fa5121759d761525f35ec98e9eb2da3716124f1a23a169b4b17c52`。
- DIA-15 只修复/冻结文档与机器词汇的接口分配漂移（例如 `IPrivateArgumentShapePolicy`、`IStartupFaultPolicy`），不是 C# interface、policy manager、resolver、CompatibilityPlan、LegacySessionFacade、D2 frozen registry 或 Parser/VM 实现；必须保持 runtime isolation=`Failed`、Parser/VM=`NotConsumed`、runtime type/policy manager=`NotImplemented`、M1=`Blocked`，不启动 Godot/游戏或读取 Era 游戏库。
- `DialectModuleComposition.psm1`、`Invoke/Test-DialectModuleComposition.ps1`、`dialect-module-composition.json` 与两份 schema：M0-DIA-16 只读取 DIA-12 CompatibilityPack 和 DIA-15 policy surface 的 generated JSON，以精确来源 hash 生成离线 module descriptor graph。它固定两个 `StaticCandidate` 模块、1 条依赖边、10 个唯一 port declaration 与两个 profile closure：`gemuera.v24@1.0.0` 无依赖/无 port；`game.snake@1.0.0` 依赖 `gemuera.v24 [1.0.0,2.0.0)` 并声明全部 10 个 future port。
- DIA-16 报告使用稳定的 dependency-first 拓扑序，所以基础 `gemuera.v24` 永远先于 `game.snake`；`v24pure` closure 只含前者，`snake` closure 为前者再加后者。catalog 枚举顺序、port/依赖枚举顺序和来源对象后续变异均不得改变输出；陈旧 DIA-12/15 hash、未知依赖、重复 port、依赖环、错误计数或任何 runtime capability exposure 都 fail-fast。catalog hash=`1fe1a86884b6665a993fa3e8a02f412b79dca8134f94dc65a1df3c98f72ae4ad`，module composition set hash=`568b47b7fc64756c7155ee923c454a2ed1fc33727e2886775b1801d8acd5c898`。
- DIA-16 仅为后续 manifest/candidate builder、D3 API review 和 fixture 计划提供静态组合边界，不加载 DLL/assembly、不反射 type、不建立 runtime module catalog/resolver/CompatibilityPlan/LegacySessionFacade/D2 frozen registry，也不接入 Parser/VM；必须保持 runtime isolation=`Failed`、Parser/VM=`NotConsumed`、runtime catalog/resolver/plan=`NotImplemented`、M1=`Blocked`，不启动 Godot/游戏或读取 Era 游戏库。
- `DialectBehaviorFixtureContracts.psm1`、`Invoke/Test-DialectBehaviorFixtureContracts.ps1`、`dialect-behavior-fixture-contract-catalog.json` 与两份 schema：M0-DIA-17 只读取 DIA-13 的 10 个 `BehaviorKey` 和 DIA-15 的 10 个 future port，强制每项锁定既有 fixture ID、`v24pure`/`snake` 双侧 profile、`baseline/extension/undeclared` 证据维度及 input/result/error/completion/effect trace facet。未知/重复 key、fixture/source hash 漂移、提前标为 Captured、夹带 policy value 或方法签名等载荷均 fail-fast。
- DIA-17 report 固定 10 个 `Planned` fixture contract、0 个已覆盖行为证据；catalog hash=`ddf3e73d...cf7b8`、contract set hash=`481caf21...9be88`。它只是后续 C# port implementation 前的证据门槛：没有行为结果、C# interface、policy manager、resolver、CompatibilityPlan、D2 registry 或 Parser/VM 接线，仍为 runtime isolation=`Failed`、Parser/VM=`NotConsumed`、M1=`Blocked`。

## 2026-07-14 M1 Core 合同与 legacy 会话外壳（InProgress / Blocked）

- `src/Core/GEmuera.Core.csproj`：标准 `Microsoft.NET.Sdk` 的纯程序集，目标为 `net8.0;net10.0`；Godot host 通过 net8 `ProjectReference` 消费它，Core 本身不引用 GodotSharp、legacy Parser/VM 或 App。
- `src/Core/Compatibility/DialectRuntime.cs`、`DialectModuleSnapshot.cs`、`CompatibilityPlanSnapshot.cs`：不可变 module/dependency/typed port、冻结 instruction/function registry 与稳定 hash。内置 `game.snake` 仅声明 10 个经 DIA-15/16 审计的 port type，不携带 policy 值或 Parser/VM 绑定。
- `src/Core/Session/SessionCoordinator.cs`：`SessionSelection` 固化 module/port/capability/save-profile 输入；`PrepareSwitchAsync`/`SessionSwitchLease.CommitAsync` 让候选在后端激活成功前不改变 `Current`，generation/cancellation/stale 检查与短 commit gate 均在 Core。
- `src/Core/Session/LegacySessionFacade.cs`：把旧 backend 的 stop/start 包在独立 gate 中，按“candidate → backend start → commit”执行；启动失败、取消或 stale 时尝试恢复前一 backend，且公开 backend generation 供诊断比对。
- `Scripts/GodotHost/LegacySessionLaunchRegistry.cs`、`LegacySessionBackend.cs`：Godot/legacy bridge 将 Core 的 opaque `(gameId, profileId)` 精确解析为预注册游戏根/profile，收口 `GlobalStatic.Reset()` 与 `EmueraThread.Start/End`；canary 会额外清理已观察到的 `AppContents`、`ConfigData` 与 parser warning 根。`Scripts/EmueraMain.cs` 只给显式 runner 提供 internal cross-configuration seam；旧 Parser/VM 仍不读取 plan。
- `tools/core-contracts/CoreContractSmoke.csproj`/`Program.cs`：无第三方 smoke，覆盖 module/hash、interpreter catalog、resumable Step/Resume、stale completion、wait/effect contract、A→B→A、启动失败/取消/stale rollback 与 prepare/commit lease；它承担高风险合同的定向 TDD，不替代真实游戏、Godot 或设备验收。
- `tools/core-contracts/Test-CoreArchitecture.ps1`：文本架构守卫，拒绝 Core 中 Godot using/类型/Signal，并确认 Core 项目无 Godot.NET.Sdk、GodotSharp 或 legacy ProjectReference。
- `gemuera-c#.csproj` 明确排除 `src/Core` 与 `tools/core-contracts` 的 compile glob，但以显式 `ProjectReference` 引用 Core；M1 仍未完成真实 static 隔离、rollout flag、Android 或 M2 DTO 迁移。

## 2026-07-12 M0-SAV-01 旧二进制存档协议静态基线

- `tools/save-baseline/LegacySaveBaseline.psm1`、`Invoke/Test-LegacySaveBaseline.ps1`、版本化 catalog 与两份 schema：只读锁定 `EraBinaryDataReader/Writer`、`VariableEvaluator`、`VariableData`、`CharacterData` 五个 legacy source file 的 SHA-256/byte length，提取 1808 header、4 个 `EraSaveFileType`、19 个 `EraSaveDataType`、11 个 `Ebdb` sparse marker、四类文件的旧读写入口和两个 normal load 就地 mutation 证据。catalog 枚举顺序、来源对象后续变异、source hash、enum/header/marker/入口或 mutation literal 漂移都会 fail-fast。
- `NewFrameworkDesign/generated/legacy-save-baseline.json`：固定普通/Zip header=`0x0A1A0A0D41524589`/`0x0A50495A41524589`、version=1808、DataCount=0、minimum bytes=16、`Encoding.Unicode`；file type=`Normal/Global/Var/CharVar`，source-only protocol count=5/4/19/11。catalog hash=`1bb6edaa4b2c0dab806cfb9d8ef56271b5cc41982926f23f2b625cc627ba3ad7`，baseline set hash=`0c4507f40f95bab072b06777982412511b8e38793c288fdaa1ec1cd1a991ea58`。
- M0-SAV-01 明确观察到 legacy `Float=0x20..0x23`，并把 `0x20..0x22` 与 Upstream1808 私家 Map/XML/DT 解释的设计冲突标为 `StaticConflictObserved`；它保持 `automaticSelectionStatus=Unbound`，不把 source enum 当作 game→SaveProfile 映射。真实 save 文件、offset map、round-trip、content/profile binding、runtime resolver/codec registry、candidate parse/commit、LegacySessionFacade、Parser/VM、游戏和设备证据均为 `Uncovered`/`NotImplemented`/`Blocked`，不访问 `E:\MyCode\Era`。

## 2026-07-12 M0-SES-01 旧会话根状态库存

- `tools/session-state-inventory/LegacySessionStateInventory.psm1`、`Invoke/Test-LegacySessionStateInventory.ps1`、版本化 catalog 与两份 schema：只读扫描 `Scripts/Emuera/GlobalStatic.cs` 的直接静态字段和 `Scripts/Emuera/Program.cs` 的直接静态字段/private-set 自动属性，并为每项固定旧 owner、候选 M1 owner、跨会话预期、reset 预期、fixture 与迁移阶段。catalog 缺项、重复项、源码根状态漂移或输出顺序漂移均 fail-fast。
- `NewFrameworkDesign/generated/legacy-session-state-inventory.json`：当前固定 `GlobalStatic=14`、`Program=17`、31 项全部有 catalog 映射；旧 `GlobalStatic.Reset()` 静态可见地清理 12 项，`ctrlZ` 与仅 debug 的 `StackList` 未观察到清理；采集 478 个直接 `GlobalStatic.*`/`Program.*` 引用，来自 56 个源码文件。inventory set SHA-256=`a50777959b9b26aa7992dfacc4bf5ee29938167d334cd0920a9fe910b89ec5c7`。
- 该报告仅是 M1 `LegacySessionFacade` 的 root-state 输入，不是 façade、feature flag、generation guard、CompatibilityPlan 或 frozen registry。其范围故意不包含其他 static cache、Godot View、platform/diagnostic state；保持 `InProgress + Blocked/EvidenceMissing + Partial`、`currentRuntimeIsolation=Failed`、`parserVmConsumption=NotConsumed`，不启动 Godot 或访问 Era 游戏库。

## 2026-07-12 M0-DSP-01 旧双后端显示基线

- `Scripts/M0/LegacyDisplayObservation.cs`：runner 专用的只读显示证据模型；记录 requested/effective backend、viewport/scroll、retained layout/overlay 计数、div/src/srcb/动态地图/data-only/scroll 覆盖状态、截图 metadata 与真实 hit probe。它不是 M2 Display DTO，不参与旧 renderer 输入或布局决策。
- `EmueraContent.ConfigureM0RunnerDisplayBackend`：只接受 `controls/canvas` 的进程内 override；仅显式 runner scene 调用，不写 `user://settings.cfg`。普通启动仍读取原 `Display.ConsoleRenderBackend`。
- `EmueraContent.M0.cs:BuildM0LegacyDisplayObservation`：从现有 `lineObjects/lineLayoutEntries/lineControls/canvasLineButtonHits/overlay indexes` 只读投影后端状态。Control 按钮从既有 metadata 取矩形；Canvas 按钮从既有 hit cache 取矩形；两者都以矩形中心再次调用现有 `TryFindConsoleButtonAtGlobalPosition`，记录 value/generation/matched，不从截图反推 hit。
- `LegacyRunnerHost`：固定 runner viewport；可视显示服务器下先等待 `RenderingServer.frame_post_draw` 再保存 PNG。headless 不等待不会到达的绘制信号，明确写 `headless_display_driver=Uncovered`。失败/超时路径也能落盘显示报告。
- `LegacyRunnerReportWriter`：`display.json` 保留 requested/effective backend、后端计数、feature coverage 与 screenshot metadata；另写 `screenshots.json`、`hit-test.json`，并把三者纳入 semantic hash。normalizer 只处理原 legacy 文本/诊断，不删除后端、矩形、generation 或截图 hash。
- `tools/legacy-runner/Invoke-LegacyDisplayBaseline.ps1`：分别运行 Controls/Canvas 到独立目录并验证 effective backend，禁止一个报告冒充另一后端；默认使用真实显示服务器，`-Headless` 只生成明确未覆盖的截图证据。`Invoke-LegacyRunner.ps1 -UseDisplayServer` 是显式可视采集开关。
- `legacy-display.schema.json`、`legacy-hit-test.schema.json`、`Test-LegacyDisplay.ps1`、`fixtures/display-smoke.json`：固定报告/config 契约和无依赖 TDD 测试；smoke 使用短内部边界诊断 headless/显示服务器差异，不修改 Parser/VM 等待语义。
- 本地 v24 样例证据：Controls=`34 Control rows/0 Canvas rows`，Canvas=`1 Control fallback/33 Canvas rows`；两边各 15 个 hit 全部匹配，Canvas 中 6 个来自 Canvas cache、9 个来自复杂 div Control fallback；两份固定 1280×720 PNG hash 不同。该样例只证明采集链与旧双后端身份，eraFL nested div/src/srcb/动态地图/data-only 仍为 Uncovered。
- eraFL 首等待可视证据：Controls=`335 Control/0 Canvas`，Canvas=`1 Control fallback/334 Canvas`；两边各 5 个 hit 全匹配、固定 1280×720 PNG hash 不同、隔离副本 mutation=0。首屏捕获 `div=1/src=1/scroll`；nested div/srcb/动态地图/data-only 需要后续输入 replay，保持 Uncovered。headless 360 秒失败不能替代可视显示服务器结果。
- `LegacyRunnerInput.ExpectedInputType/ExpectedButtonGeneration`、`LegacyInputReplayDriver.IsReplayWait`：可选绑定 `EnterKey/AnyKey/IntValue/StrValue` 与按钮 generation；真实等待必须同时满足 `IsWaitingInput`，避免旧 `IsWaitingEnterKey` 在 Error/Quit 上返回真而伪造可推进状态。类型/generation 漂移 fail-fast；消费推进同时接受 generation 变化或离开等待后重新进入，支持同 generation 的连续 `PRINTW`。
- `tools/legacy-runner/Test-LegacyReplay.ps1`、`fixtures/erafl-enter-game.json`：无外部依赖 replay 状态机契约测试，以及从标题经角色选择、6 次 opening EnterKey、领地编辑和教程进入实际领内互动页的 18 步确定性路径。`ONEINPUTS` 长按钮值必须用 `mouseButton=1` 表示真实鼠标提交，否则旧核心会按键盘单字符语义截断。
- eraFL 复杂页双后端单次证据：Controls=`57 Control/0 Canvas`，Canvas=`2 Control fallback/55 Canvas`；两边各 45/45 hit 匹配、`div=44/src=2/data-only=25/26/scroll=2`，PNG 分别为 `d49b5b...b69a8` / `2cf5fe...e6d8ca`。nested div/srcb/dynamic-map scope 仍 Uncovered。旧版 Controls RepeatCount=3 的 `apply_text_changes=683/701/689` 与 PNG 差异只保留为 UI transport 诊断；现 M0-RUN 比较仅排除明确 `ui_projection` 批次且保留 raw trace，复杂页需在当前 runner 下重跑后才能给出新的语义结论。

## 2026-07-12 M0-FIX-01 fixture manifest

- `Fixtures/manifest.json`：版本化三层 fixture catalog；每项固定 `UP/GE/SN/FL` ID、source layer、profile、来源、授权状态、再分发权限、期望报告和覆盖目标。eraFL 本地 archive revision 为 `c4ca29...`，`licence_eraFL.txt` 已核验但属于受限非营利授权，`redistributionAllowed=false`；FL 期望报告使用 Controls/Canvas 双目录相对路径，不能用单后端替代。catalog 不保存本机绝对路径或游戏字节。
- `tools/fixture-manifest/FixtureManifest.psm1`：只读解析 catalog 与显式 root/report bindings；按 ordinal relative path 生成 UTF-8 长度前缀 canonical SHA-256 清单。upstream/legacy 使用源码排除策略，game 不套用源码排除。缺 root、授权证据或 report 均进入稳定 `Uncovered`，不会伪造绿色结果。
- `tools/fixture-manifest/Invoke-FixtureManifest.ps1`：M0-FIX-01 CLI；默认绑定当前 legacy root，可选 upstream/game/eraFL 与各自报告目录。Partial 默认 exit 0 供继续补证据，`-RequireComplete` 下 Partial exit 2，配置/schema/路径错误 exit 1。
- `fixture-catalog.schema.json`、`fixture-manifest.schema.json`：分别固定版本化声明与本地 resolved report；未验证授权强制 `redistributionAllowed=false`，阶段状态固定 `InProgress + Blocked/EvidenceMissing`。
- `Test-FixtureManifest.ps1`：无网络 contract test；验证创建/枚举顺序不影响 hash、字节变化改变 hash、三层 catalog、缺失 fixture/report 的 Uncovered、授权边界、fixture 原目录不被写入。

## 2026-07-12 M0-TRC-01 typed legacy trace

- `Scripts/M0/LegacyTraceEvent.cs`：定义共享 event envelope、clock/RNG/wait/input/display/effect/error/runner typed payload，以及 raw→canonical 克隆和比较专用 `LegacySemanticTraceSnapshot`；raw/canonical 保留事件数、原顺序与全局 `sequence`，语义投影只排除明确 ordering point=`ui_projection` 并为保留事件重新编号，绝不排除逻辑 `display_commit`。
- `Scripts/M0/LegacyTraceRecorder.cs`：runner session 独占的有界 recorder；跨 Godot main/legacy VM thread 在同一临界区分配全局递增序号，容量耗尽后拒绝新事件并累计 `droppedEventCount`，不得静默覆盖。`LegacyTrace` 静态 façade 默认关闭，只有显式 M0 runner scene 启用。
- `EmueraThread.Input/Work`、`EmueraConsole.WaitInput/callEmueraProgram`：只读记录 input submitted/rejected/consumption 与 wait pending/completion；不改变原阻塞、generation、默认值或鼠标键语义。
- `GenericUtils`、`MTRandom`、`DiagnosticLogSinks`：`GenericUtils` 只记录 Godot 队列 `ui_projection` 批次而不冒充逻辑 commit；其余分别记录 SFMT seed/call sequence 与最终 Error sink。普通运行关闭时先通过廉价开关返回，不构造 payload。
- `LegacyRunnerReportWriter`：同时写完整 `trace.raw.json`、canonical `trace.json` 与比较专用 `semantic-trace.json`；`timeline/effects/errors` 从语义投影导出，raw trace 保持为跨领域 transport 诊断。外部 harness 把 `semantic-trace.json` 纳入 semantic SHA-256，并单列 `transportTraceSha256`；溢出时 runner 非零退出。
- `tools/legacy-runner/legacy-trace.schema.json`：JSON Schema 绑定八类 category 与各自 payload，强制 `sequence/threadOwner/orderingPoint/completionMode`。
- `tools/legacy-runner/Test-LegacyTrace.ps1`：无网络依赖的 contract test；覆盖 512 事件并发保序、normalizer 数量/顺序不变、八类 façade 和显式 overflow。真实 Godot/EraTW 首等待仍属于集成测试。

## 2026-07-12 M0-RUN-01 legacy runner

- `Scripts/FirstWindow.cs:ConfigureM0RunnerSession`、`Scripts/Emuera/Program.cs:ConfigureM0RunnerStartupErrorLogPath`：仅供显式 M0 runner scene 在创建 `main.tscn` 前注入本次游戏目录/profile 与绝对 startup-error artifact 路径；Snake 的 `emuera_startup_errors.log` 在 runner 中写入报告目录而非隔离游戏副本，普通 `first_window.tscn` 启动链不调用 override，仍保留 legacy `ExeDir` 路径。
- `Scripts/M0/LegacyRunnerConfig.cs`、`LegacyRunnerDeterminism.cs`：runner JSON 配置、输入项、固定默认 `randomSeed=20260712` 与硬上限验证；`sessionIsolationMode=baseline|canary` 仅供 runner 在 main scene 创建前覆盖默认关闭的启动 flag，普通启动不读取它。`inProcessSessionCycle=disabled|aba` 仅允许 canary+空输入，驱动同一配置游戏的两次 runner-only restart，不是通用热切换。seed 只在显式 runner 场景启动前注入旧 `VariableEvaluator`，拒绝把报告目录放进游戏根，profile 仅允许 `v24pure/snake`。
- `Scripts/M0/LegacyInputReplayDriver.cs`：观察旧 `EmueraConsole` wait/generation，在每个输入被消费或 generation 推进后才提交下一项；不改 VM 内部调度。
- `Scripts/M0/LegacySettlementTracker.cs`、`LegacyRunnerHost.cs`：显式 runner scene 的 Godot orchestrator；在创建 `main.tscn` 前将 runner-only `sessionIsolationMode` 投影到已初始化的 diagnostics snapshot，并配置报告目录 owned Snake startup-error artifact，再挂载原场景、驱动输入、内部超时、首等待/输入消费完成和退出码。`aba` 循环会在三次稳定首等待间调用 internal `EmueraMain.RestartLegacySessionForM0RunnerAsync`，记录两次 commit generation 与三次 canonical logic fingerprint，不接受输入重放。首等待完成前必须观察到连续相同的 Console 文本 hash、trace 进度、输入状态与只读旧 View 指纹，不能用盲等三帧截断队列；普通运行不实例化这些 Node/runner-only 类型。
- `Scripts/M0/LegacyRunnerReportWriter.cs`：从旧 Console、typed trace 与诊断 ring 只读采集 `state/display/effects/errors/timeline/semantic-trace/metrics/diagnostics/artifacts`。原始 Console/trace 分别保存在 `display.raw.json`、`trace.raw.json`，canonical `trace.json` 保留完整 transport，原始诊断保留在 `diagnostics.json/zip`。完整 Display DTO 仍保持 Uncovered。
- `tools/legacy-runner/legacy_runner.tscn`：必须通过 `--scene res://tools/legacy-runner/legacy_runner.tscn` 显式启动的测试场景。
- `tools/legacy-runner/Invoke-LegacyRunner.ps1`：外部 M0 harness；锁定 Godot 4.7 Mono，非 `-SkipBuild` 时构建 `gemuera-c#.sln -c Debug --no-restore`（不直接构建 `.csproj` 或触发受限网络的 Godot SDK restore；缺失已准备的 Godot C# 资产时 fail-fast）、生成/复用 BaselineIdentity、每次复制独立游戏 fixture、启动与硬超时、收集 stdout/stderr/Godot/startup-error log、记录 fixture mutations、删除已验证的隔离副本、生成诊断 ZIP/artifact hash，并比较 1–10 次 semantic report hash；缺省 `inProcessSessionCycle` 机械归一化为 `disabled`，summary 显式记录 `sessionIsolationMode` 与该值，完整 `trace.json` hash 另列为 transport 诊断，不会被隐藏或拿来替代语义门。
- `tools/legacy-runner/Test-LegacyRunner.ps1`：真实 Godot/Era fixture 集成测试入口；验证退出码、全部必需报告/诊断 ZIP、runner-owned startup-error artifact 和隔离副本零 mutation。
- `tools/legacy-runner/Invoke-SessionIsolationCanaryBaseline.ps1`、`Test-SessionIsolationCanary.ps1`：runner-only baseline→canary→baseline 独立进程比较和无外部依赖 contract test；逐 run 断言 only-canary diagnostics 出现 `M1_SESSION_ISOLATION_CANARY`，三段 semantic hash 相同才通过。报告明确它不是同进程 A/B/A、静态隔离或 M1 gate 证据。
- `tools/legacy-runner/Invoke-SessionIsolationInProcessAba.ps1`、`Test-InProcessSessionCycle.ps1`：canary-only 同一游戏首等待 ABA restart 与 contract test；比较器验证 generation 2/3、三次相同 logic fingerprint、repeat semantic hash 和隔离副本零改动，并明确其不覆盖不同 game/profile、晚到 completion、100-switch leak、Android 或 M1 gate。
- `tools/legacy-runner/Invoke-SessionIsolationInProcessCrossAba.ps1`、`fixtures/session-isolation-inprocess-cross-aba.json`：runner-only 跨配置 A(game/profile)→B(game/profile)→A 首等待观察；B 必须在 `main.tscn` 前进入 immutable host route allowlist，比较器验证 generation 2/3、首尾 A 指纹、两份隔离副本零改动及 repeat semantic hash。它只提供局部 runtime evidence，不关闭 M1/M0 gate。
- `tools/legacy-runner/legacy-runner-config.schema.json`、`fixtures/first-wait.json`、`fixtures/session-isolation-canary.json`、`fixtures/session-isolation-inprocess-aba.json`：稳定配置 schema、通用冷启动首等待基线、独立进程 canary 与同进程 ABA fixture。eraFL snake 首等待 RepeatCount=3 均 exit 0，semantic report SHA-256=`1790a2f2...207fe` 相同；raw trace 的 1136/945/938 个 `ui_projection` 批次仍保留并令 transport consistency=false。该工作包不关闭 M0；M0-DSP、APK/真机、upstream/v24 fixture 和人工签署仍为阻断项。

## 2026-07-12 M0-ID-01 基线身份清单

- `tools/baseline-identity/Invoke-BaselineIdentity.ps1`：M0 外部入口；采集项目根、可选游戏目录/运行产物以及 Godot、.NET、Java、Android SDK/NDK、导出模板身份。显式传入但不存在的路径返回非零退出码，未提供的证据进入 `Partial` 报告的 `uncovered`，不会误报完成。
- `tools/baseline-identity/BaselineIdentity.psm1`：无业务侵入的 SHA-256 清单实现；生成 `identity.json`、`source-tree.manifest.json`、配置/资源/字体/游戏/工具链清单。无可用 Git metadata 时以排序后的 `path + bytes + sha256` canonical tree hash 标识源码；只有源码树排除 `.git/.godot/.codegraph`、本地 Agent/IDE、依赖/构建/报告/操作日志目录，外部游戏与资源清单不套用源码排除规则。
- `tools/baseline-identity/identity.schema.json`：固定 `gemuera M0 Baseline Identity 1.0.0` 的必需字段、状态和 SHA-256 格式；work package 固定为 `M0-ID-01`。
- `tools/baseline-identity/Test-BaselineIdentity.ps1`：无外部测试框架的回归入口；验证排除缓存不改变源码 hash、源码字节变化改变 hash、游戏目录不套源码排除、显式缺失 artifact 返回非零退出码。
- `tools/baseline-identity/README.md`：部分/完整采集命令、参数、失败语义和验证方式。该工具只建立 M0 身份证据，不启动 legacy runner、不修改解释器/渲染/Android 输入，也不代表 M0 总门禁已通过。

## 2026-07-08 虚拟鼠标重新设计：可见可拖动光标（替换 L/M/R 选键面板）

- `Icons/cursor.svg`（新文件）+ `.import`：简单箭头光标图标，白色填充+黑色描边，32×32px。
- `Scripts/VirtualCursor.cs`（新文件，替代原 `VirtualMousePad.cs`）：`CanvasLayer(Layer=92)`，虚拟光标模式开启后屏幕上出现可见、可拖动移动的光标。单指拖动=相对位移移动光标（触摸板模式，灵敏度系数 1.0），短按=左键（按下后总位移 < 10px 且松手时长 < 0.45s），长按=右键（总位移 < 10px 且持续时长达 0.45s，在按住过程中立即触发不等松手），常驻中键按钮（右上角小型 "M" 按钮，点击直接对光标当前位置提交中键）。光标移动时调用 `EmueraContent.VirtualCursorUpdateHover` 驱动 hover 高亮，同时同步两条通道（通道A：`GenericUtils.SetPointingButton` → ERB `MOUSEBUTTON()` 读取；通道B：`SetCanvasVisualButton` → Canvas 渲染高亮）。
- `EmueraContent.cs`：删除 `pendingMouseVk`/`pendingMouseSingleShot`/`SetPendingMouseButton`/`PendingMouseVk`/`PendingMouseSingleShot` 及 `virtualMousePad` 字段，新增 `VirtualCursor virtualCursor`；新增一组 `public` 转发方法供 `VirtualCursor` 调用（`VirtualCursorContentToGlobal` 反向坐标换算、`VirtualCursorGetContentViewportRect` 当前可视区域、`VirtualCursorUpdateHover` 命中测试+双通道同步 hover、`VirtualCursorCommitClick` 点击提交）；`HandleContentPointerInput` 在 `HandleContentTouchGesture` 之后、`TryGetPointer` 之前插入虚拟光标手势拦截（`virtualCursor?.HandleGesture`），虚拟光标模式开启时接管单指按下/拖动/释放；触摸事件 `effectiveVk` 兜底逻辑简化为默认左键（0x01），不再有"预选键"分支；`OnMouseTogglePressed` 改名 `OnVirtualCursorTogglePressed`，切换 `virtualCursor.Enable()`/`Disable()`；三处 `virtualMousePad?.ClosePadExternal()` 改为 `virtualCursor?.Disable()`。
- 影响范围：推翻上一版"右上角展开选键面板"设计，改为"可见光标+手势判定"模式。桌面端真实鼠标/键盘输入不受影响，本次只重做 Android 侧虚拟光标交互。双指缩放优先级不变（拦截插在 `HandleContentTouchGesture` 之后）。坐标换算当前实现简化处理（`cursorContentPosition` 直接用 `newGlobal` 近似），在滚动偏移大、缩放非 1.0 的场景下光标位置可能不精确，需要后续完善反向换算公式（参照 `UpdatePointerPosition` 的换算逻辑写镜像反函数）。
- 关键设计要点：hover 双通道同步（`VirtualCursor.MoveCursorBy` 每次移动光标后必须同时更新通道A 和通道B，否则 `MOUSEBUTTON()` 返回空、立绘动效失效）；手势判定阈值（`DragThreshold=10px`、`LongPressDuration=0.45s`）可能需要根据实机触感微调。

## 2026-07-08 eraFL `INPUTS ,1` 空白右键状态切换兼容

- `ArgumentBuilder.cs:SP_INPUTS_ArgumentBuilder`：`INPUTS`/`ONEINPUTS` 在跳过空白后若首字符为逗号，按“第一个默认值参数省略”处理，直接返回无默认值参数，不再把 `,1` 解析成默认字符串。
- 根因：eraFL `ERB/TRAIN/USERCOM_INPUT.ERB` 使用 `INPUTS , 1` 读取点击；空白区域右键应提交 `RESULT:1==2` 且 `RESULTS==""`，随后脚本把 `RESULT:0` 置为 `-1` 并触发地图/状态页切换。旧解析会把 `,1` 当成默认字符串，空输入被替换成非空 `RESULTS`，导致切换条件失败。
- 影响范围：只改变 `INPUTS`/`ONEINPUTS` 首参数省略写法的解析语义；显式默认字符串（如 `INPUTS "x"`）不变。后续若出现“空白区域点击有鼠标键码但脚本条件不触发”，优先检查 `SP_INPUTS_ArgumentBuilder`、`EmueraConsole.PressEnterKey` 的默认值替换，以及 `EmueraThread.Input` 写入 `RESULT_ARRAY[1]` 的链路。

## 2026-07-08 统一鼠标三键抽象 + VirtualMousePad

- `EmueraContent.cs:TryGetPointer`（6参数重载）：新增 `out int mouseVk` 输出，Left=0x01/Right=0x02/Middle=0x04/Touch=-1。5参数旧版本调用新重载，所有旧调用点不变。
- `EmueraContent.cs:HandleContentPointerInput`：press 分支用 `effectiveVk = eventMouseVk>=0 ? eventMouseVk : pendingMouseVk` 替换硬编码 0x01，并记录到 `contentDragMouseVk`；release 传 mouseVk 给 OnButtonPressed；触控单次模式提交后自动复位。
- `EmueraContent.cs:OnButtonPressed`：新增 `int mouseVk=0x01` 参数，调用 `EmueraThread.Input(input, true, skip, mouseVk)`，取代硬编码 1。
- `EmueraContent.cs`：新增 `pendingMouseVk/pendingMouseSingleShot/contentDragMouseVk` 字段及 `SetPendingMouseButton/PendingMouseVk/PendingMouseSingleShot` 公开 API，供 `VirtualMousePad` 调用。
- `Scripts/VirtualMousePad.cs`（新文件）：`CanvasLayer(Layer=91)`，左下角常驻切换按钮（L/M/R），展开后可选左/中/右键；短按=单次，长按=锁定；锁定时按钮变橙色并显示"[X]锁定"提示；`layerRoot.MouseFilter=Ignore` 不干扰 Emuera 点击判定。
- 不改动：`WinInput`（已轮询三键）、`QuickButtons`（保持 mouseVk=1 默认）、`Scalepad`、`HandleContentTouchGesture`（双指仍走 pinch zoom）。
- ERB 端：`RESULT:1` 读到 1=左键 / 2=右键 / 4=中键。

### 空区域右键补充修复（eraFL 地图/状态 Tab 切换）

eraFL 用 `INPUTS , 1`（StrValue 等待）读取点击，`USERCOM_INPUT.ERB` 靠 `RESULT:1==2 && RESULT:0==-1 && RESULTS==""` 判定"空区域右键"。仅让 `RESULT_ARRAY[1]` 写入右键码并不够，还需要让等待状态真正推进，且不能被拖拽逻辑吃掉。补了 4 处：

- `EmueraContent.cs:IsPointerRelease`：原来只认 `MouseButton.Left`，导致 `_Input` 兜底路径识别不到右/中键释放；新增 Right/Middle 识别。
- `EmueraContent.cs:HandleContentPointerInput` motion 分支：`contentDragMouseVk != 0x01` 时跳过拖拽阈值判断，避免右键按下后手指/鼠标轻微移动被误判为滚动（`contentDragMoved=true` 会导致 release 走 `StartContentInertia` 而不是提交按键）。
- `EmueraContent.cs:HandleContentPointerInput` release 分支 / `TryAdvanceTap`：新增 `isNonLeftClick && console.IsWaitingInput` 条件，让右/中键在 INPUT 数值等待状态（不止 EnterKey/AnyKey）下也能 advance；`TryAdvanceTap` 内部同步放宽守卫，否则外层放行了内部仍会 `return false`。
- `EmueraThread.cs:Work()`：`PressEnterKey` 对 `InputType.IntValue` 空字符串会 `Int64.TryParse` 失败直接 `return false`（等待不推进）。右/中键 + 空输入 + IntValue 时补 `submitInput="-1"`（不是 `"0"`——eraFL 用 `RESULT:0==-1` 判定"未点击按钮"）。注意 `USERCOM_INPUT.ERB` 实际是 StrValue 类型，`InputString("")` 本身不会失败，这条補丁只覆盖其他脚本可能用 IntValue 等待右键的场景；`InputInteger`（写 RESULT 数组）和 `InputString`（写 RESULTS）操作不同底层数组，互不覆盖，已用 Agent 核实。

## 2026-07-08 #FUNCTION 参数槽清零与返回值预清零

- `Process.CalledFunction.cs:UserDefinedFunctionArgument.SetTransporter`：for 循环每次迭代开始时清空当前参数槽（TransporterInt/Float/Str/Ref/ElementRef），避免 #FUNCTION 式中函数调用时缓存复用的 UserDefinedFunctionArgument 残留上次调用的 REF/值。null 参数或走 isRef 分支 continue 路径时，旧槽不会被新值覆盖，会把前一次调用的数据传给函数形参，导致 LIST_GET/ADD_MTAR 等函数写入错误的 MTAR 值。
- `Process.cs:GetValue(SuperUserDefinedMethodTerm)`：在 IntoFunction 前清零 `state.MethodReturnValue`，防止函数 fallthrough 或异常截断时读到上次 RETURNF 的返回值。#FUNCTION 调用方通过 `ret = state.MethodReturnValue` 获取返回值，若函数未显式 RETURNF 且跳过了正常 ReturnF(null) 路径，MethodReturnValue 可能保留旧值。
- 影响范围：只影响 #FUNCTION 表达式函数调用，普通 CALL 每次通过 ConvertArg 创建新 UserDefinedFunctionArgument 不受影响。若后续出现 #FUNCTION 调用后参数或返回值异常，优先检查这两处清零是否覆盖了相关边界。

## 2026-07-07 VARSET 裸 1D 数组目标语义修复

- `ArgumentBuilder.SP_VAR_SET_ArgumentBuilder`：`VARSET` 第 1 参数在词法层额外识别裸 1D 非角色数组变量名，例如 `VARSET MTAR, -1`、`VARSET MPLY, -1`，并把它作为整数组写入目标，而不是沿用普通表达式里的 `MTAR == MTAR:0` 读值兼容规则。带下标写法如 `VARSET MTAR:0, -1` 仍按单元素写入处理，普通表达式读取裸数组也不改变。
- 影响范围：修复 eraFL `CLEAR_MTAR` / `CLEAR_MPLY` 清空参与者数组时只清 `:0` 的兼容偏差，避免状态页 `HO_STATUS_WINDOW_SET_TARGET("LEFT_BOTTOM")` 因旧数组残留或目标错误而跳过第二角色框内容。若后续出现类似“数组清空后仍残留旧角色/旧目标”的问题，优先检查 `VARSET`、`ARRAYSORT` 和 `VariableParser` 的裸数组语义边界。

## 2026-07-07 eraFL 右侧信息窗横向可视宽度与 Fit 兜底

- `EmueraContent.GetLineVisualRight` / `CalculateLineContentSize`：滚动内容宽度不再只依赖普通文本行宽和 `Config.DrawableWidth`，还会扫描每行相对/绝对 div、图片与 shape 的可视右边界。eraFL 状态页的右侧房间信息窗、日志窗使用 `x=4150,width=4500` 这类相对 div，窄屏或 Android 安全区下如果不把横向溢出计入内容宽度，会表现为只显示左侧角色框、右侧 UI 消失。
- `EmueraContent.GetCurrentVisualContentWidth` / `Scalepad.OnAutoFit`：缩放面板的 `Fit` 不再只按 `Config.DrawableWidth` 计算比例，而是取配置宽度与当前真实可视内容宽度的较大值，避免 Android 动态安全区宽度把 `Config.WindowX` 缩到屏幕宽后，eraFL 右侧 div 仍无法被 Fit 纳入。
- 影响范围：扩展 `ScrollContainer` 子内容的可显示/可横向滚动宽度，并让手动 `Fit` 使用真实可视宽度；不改变脚本输出、HTML 解析、按钮输入、逻辑行高或默认缩放值。

## 2026-07-07 eraFL 状态页角色框与 shape 矩形绘制修正

- `ConsoleRectangleShapePart`：公开原核心 `SetWidth()` 计算后的真实绘制矩形（偏移、宽高、可见性），区分“片段占用宽度”和“实际填充区域”。Godot Control 后端与 Canvas 后端都改为按真实矩形绘制，避免带 `x/y` 偏移的 `<shape type='rect'>` 被画到错误位置或错误尺寸。
- `EmueraContent.BuildDivControl` / `AddDivBorder`：`<div border='...'>` 未显式指定 `bcolor` 时按 `Config.ForeColor` 绘制边框，兼容 eraFL 状态页角色卡、头像框等只声明边框厚度的写法；显式 `bcolor` 仍按原值逐边生效。
- 影响范围：只影响 HTML/div 边框与 rectangle shape 的 Godot 显示后端，不改变 HTML 解析、ERB 输出顺序、按钮输入或逻辑行号。若后续边框颜色不符合脚本预期，应优先检查脚本是否显式设置了 `bcolor` 或运行期 `ForeColor`。

## 2026-07-07 eraFL 状态页 div 可视溢出高度兜底

- `EmueraContent.GetPartVisualBottom` / `GetLineVisualBottom` / `CalculateLineContentSize`：逻辑行高和可视底边分离。`ConsoleDivPart` 继续不按自身高度撑开普通文本流，避免与 eraFL `NEWLINE(n)` 预留高度重复计算；但相对定位 div 的 `Y + DivHeight` 会参与 `ScrollContainer` 子内容尺寸兜底，防止状态页下半块、第二角色栏或命令区因为父内容边界过小而被裁掉。
- 影响范围：只扩展滚动内容的可绘制/可滚动边界，不改变 `lineNumbers`、逻辑行号、按钮 generation 或普通文本追加顺序。若后续出现 div 重叠，优先检查脚本是否缺少显式空行；若出现 UI 被裁，应检查 `GetLineVisualBottom` 是否覆盖了对应 `display` 模式。

## 2026-07-07 eraFL 状态页相对 div 行高回退

- `EmueraContent.GetPartBottom`：`ConsoleDivPart` 继续只按一行文本高度参与普通行高计算，即使是相对定位 div 也不再使用 `Y + DivHeight` 撑开逻辑显示行。eraFL `UIC_SHOW` / `HTML_PRINT ...,1` 会在输出 UI 容器后自行调用 `NEWLINE(26)` 预留窗口高度，若渲染层再按 div 高度占位，会把状态页下方成员栏和命令区重复推远。
- 影响范围：div 仍会通过 `GetHtmlDivPosition` 按真实坐标绘制并可溢出所在逻辑行；行高、滚动内容高度和后续文本流则交回 ERB 脚本的 `NEWLINE(n)` 控制。若再次出现 div 内容被裁掉，应优先检查父控件 `ClipContents`、HTML 子层级和脚本是否缺少显式空行，而不是全局按 div 高度撑行。

## 2026-07-07 eraFL game-icons 片段字体渲染适配

- `EmueraContent.ResolveConsoleFont`：HTML/ERB 文本片段不再一律使用主控制台字体；当 `ConsoleStyledString.Font.FontFamily.Name` 指向非主字体时，会从游戏目录 `font/Font/fonts/Fonts` 下按 `*.ttf`/`*.otf` 懒加载对应字体并缓存。该路径用于兼容 eraFL `ICON()` 输出的 `@F:game-icons@...@/F@` 和 `<font face='game-icons'>...`。
- `EmueraContent.CreateTextPart` 与 `ConsoleRenderSurface.DrawText`：Controls 后端和 Canvas 后端都按片段字体绘制，避免 `0xf347`、`0xf349` 等私有区图标码点被 `MS Gothic`/主字体误绘成“周”“閉”或方块。
- Android 边界：主控制台字体仍保留 Android 使用内置字体的策略；片段字体不走该短路，会继续尝试加载游戏目录字体。若游戏目录缺少对应字体文件，则回退主字体并记录一次缺失缓存，避免热路径重复 I/O。

## 2026-07-07 eraFL CSV sprite 生命周期与同名 fallback 修复

- `AppContents`：新增 CSV sprite 名称登记表，`LoadContents()`/懒加载 CSV 索引阶段都会登记资源名；`SpriteDisposeAll(false)` 改为只清动态创建的 sprite，保留 `BG01`、立绘等 CSV 定义资源，`SpriteDisposeAll(true)` 才完整清空。
- `AppContents.BuildLazyResourceIndex`：普通 sprite 分支重新写入 `lazyImageDictionary[spriteName] = definition` 并登记 CSV 名称，避免从 snake profile/lazy 模式启动时 `BG01` 未进入懒加载索引。
- `EmueraContent.ShouldUseRawImageResourceFallback` 与 `ConsoleImagePart.TryResolveDynamicImageWidth`：CSV sprite 名称即使当前纹理未就绪，也禁止递归按裸文件名搜索同名图片或推断宽度，阻断 eraFL `BG01` 从 `resources/SYSTEM/BG.csv` 串到 `resources/mapimage/bg01.webp`。
- 兼容边界：这只改变 CSV sprite 生命周期和 HTML 图片 fallback 优先级；真实裸文件路径、动态 cutin、`SPRITECREATEFROMFILE` 生成的非 CSV sprite 仍按原路径解析。

## 2026-07-07 eraFL HTML div 行稳定优先渲染

- `EmueraContent.Canvas.CanRenderPartOnCanvas`：所有包含 `ConsoleDivPart` 的行不再进入 Canvas div overlay 优化路径，而是整行退回 Control 渲染；Canvas 仍负责普通文本、形状和简单图片。
- 根因：eraFL 的状态栏底图、`DRAW_PORTRAIT`/`DRAW_STILL` 立绘、房间/地图框大量依赖 `<div><img></div>` 的裁剪、`depth`、负坐标和跨行叠放。Canvas div overlay 虽能减少节点，但在异步补图、负 y 和兄弟 div 层级上仍存在边界差异。
- 取舍：这是稳定优先方案，可能增加 HTML/div 密集页面的 Godot Control 节点数；但避免全局切回 Controls，普通文本和简单图片仍走 Canvas 快路径。若后续要恢复性能优化，应先用 eraFL 状态栏、立绘、住房/地图界面做逐项视觉回归。

## 2026-07-07 eraFL HTML 图片 div 异步刷新补强

- `EmueraContent.IsPureImageLine`：纯图片行判定递归识别 `ConsoleDivPart` 子树，兼容 eraFL `DRAW_PORTRAIT`、`DRAW_STILL` 和 `SHOW_STATUS.ERB` 状态栏底图常用的 `<div><img ...></div>` 写法。图片还在异步解码/上传时，这类包装图片会和顶层 `<img>` 一样延后提交，避免先显示空 div/spacer 后表现为“有框没图”。
- `RegisterCanvasImageOverlays` / `RegisterCanvasDivOverlays` 以及对应释放路径：overlay 节点集合变化后显式标记 `canvasOverlayRowsDirty`，确保批量输出和异步补图后按最新 `lineLayoutEntries` 重新定位 Canvas overlay。
- 影响范围：仅改变 Canvas 后端对 HTML 图片 div 首帧未就绪与 overlay 重定位的处理；含文字的 div、按钮行和普通文本不进入纯图片延后路径。

## 2026-07-07 eraFL CBG/SETIMAGELAYER 换图刷新触发

- `EmueraConsole.CBG_Clear` / `CBG_ClearRange` / `CBG_SetImage` / `CBG_SetButtonMap` / `CBG_SetButtonImage` 以及 `AddBackgroundImage` / `RemoveBackground` / `SetImageLayer` / `ClearImageLayer*`：背景图层列表或按钮图层状态发生变化后统一调用 `RequestCbgRefresh()`，只唤醒 `uEmuera.Window.MainWindow.Refresh()` 的 dirty 标记，由现有 `Window.Update()` 在下一帧合并拉取 `cbgList` 并调用 `EmueraContent.RefreshCBG`。
- 根因：eraFL 图像显示库实际使用 `CBGSETG/CBGSETSPRITE/CBGSETBUTTONSPRITE/CBGSETBMAPG/CBGCLEAR/CBGREMOVERANGE` 做背景换图；这些函数已实现，但此前只修改后台 `cbgList`，若脚本本轮没有普通文本刷新，Godot 侧不会立即收到背景图层变化。
- 兼容边界：不在 CBG 函数里直接排 Godot UI 队列或重建节点，避免多次连续 `CBGSET*` 在移动端造成热路径抖动；刷新仍走原有显示桥和异步纹理重试逻辑。

## 2026-07-07 eraFL 立绘同步合成与 resources 路径回退

- `SpriteManager.GetTextureInfoForScriptComposition` / `BitmapTexture.EnsureTextureInfoForScriptComposition`：为 ERB 图像合成链路提供同步真实像素读取，不把解码失败或未就绪的占位纹理当作可合成源；若已有占位缓存且本次读到真实图，会覆盖占位缓存。
- `GraphicsImage.GCreateFromF` / `GraphicsImage.GDrawCImg`：`GCREATEFROMFILE`、`GLOAD`、`GDRAWSPRITE` 等脚本合成命令改为按真实绘制成功返回 `1/0`，失败时不再留下 `IsCreated=true` 的空图，避免后续 `SPRITECREATE` 生成空立绘。
- `Creator.Method.ResolveGraphicsResourceFilePath` / `AppContents.ResolveDynamicSpriteFilePath`：相对图片路径按游戏根目录优先、`resources/` 目录回退解析，并避免显式 `resources/` 前缀被拼成 `resources/resources`。这用于兼容 eraFL 固定立绘路径如 `portrait/prt_FIX/*.webp` 实际位于 `resources/portrait/prt_FIX/` 的脚本写法。

## 2026-07-07 GetSpriteTexture 异步纹理 pending 追踪补全

- `EmueraContent.GetSpriteTexture` line 3704：修复 `ti.texture == null` 分支缺失 `TrackAsyncTextureRequestForCurrentRender()` 调用的问题。
- 根因：当 BitmapTexture 的 `CachedTextureInfo` 已存在但 `ti.texture` 为 null 时（ImageTexture.CreateFromImage 失败或首次 lazy create 时 image 解码未完成），原代码直接返回 null 且**未追踪 pending**，导致 `ProcessAsyncTextureRefreshes` 永远不会重试该行。
- 影响：eraFL 状态栏 BG01 底图首次渲染时纹理未就绪 → 画 spacer → 后续异步完成也不重刷 → 底图永久缺失。
- 修复后：即使 ti.texture 为 null 也追踪 pending，确保 `TextureLoadVersion` 递增后会触发 `AddLine(pendingLine, true)` 重绘。

## 2026-07-07 HtmlManager 支持 div 自闭合语法

- `HtmlManager.tagAnalyze` case "div"：检测 `<div ... />` 自闭合语法（wc 最后一个 token 是 `/` 即 OperatorCode.Div）。自闭合 div 直接返回空子行的 `ConsoleDivPart`，等价于 `<div ...></div>`，不再设置 `PendingDivTag` 等待 `ReadDivInnerHtml`。
- 根因：eraFL `SYSTEM/UI\CONTAINER/UI_CONTAINER_MAIN.ERB:96` 包含多个 `<div ... />` 自闭合标签，原 HtmlManager 不支持该语法导致解析失败，`SHOW_STATUS.ERB:70` 调用 `UIC_SHOW` 时抛出异常，line 239 的 BG01 状态栏底图输出根本没被执行。2026-07-07 前三次修复（003/004/005）都在修渲染路径，但渲染代码从未被调用过。

## 2026-07-07 eraFL 状态栏背景资源优先级修复

- `EmueraContent.ShouldUseRawImageResourceFallback`：HTML `<img src>` 先按 `AppContents.GetSprite` 解析 CSV sprite。只要 sprite 定义已经命中，即使本帧纹理仍在异步解码/上传中，也不再按裸文件名递归搜索同名图片；只有完全没有 sprite 定义时才走文件 fallback。该规则避免 eraFL `SHOW_STATUS.ERB` 的 `BG01` 从 `resources/SYSTEM/BG.csv` 指向的天空状态栏误落到 `resources/mapimage/bg01.webp`。

## 2026-07-07 eraFL 状态栏 div 子层级修正

- `EmueraContent.BuildConsoleButton` / `AddPartToContainer`：新增 `allowEscapedPartZ` 传递开关。普通控制台行里的大图仍可用 escaped `ZIndex` 跨出行高；但 `BuildDivControl` 渲染 div 子行时会关闭该抬升，避免 div 内背景图在 Godot 相对 `ZIndex` 下越过外层 HTML `depth`，盖住同批次后续文字 div。该规则用于兼容 eraFL `SHOW_STATUS.ERB` 中背景 div 与文字 div 叠放的状态栏。

## 2026-07-07 eraFL HTML div 层级与制表符测量兼容

- `EmueraContent.GetGodotZIndexForHtmlDepth`：HTML `depth` 仍按数值越大越靠后的语义排序，但整体映射到正向 `ZIndex` 基准之上，避免 Canvas 后端把 `depth='1'` 的 eraFL 房间框压到绘制面背后，只剩无 depth 的通路遮罩可见。
- `StringMeasure.GetDisplayLength`：包含 tab 的字符串不再在 `GRAPHICS` 模式下替换为 8 个空格，而是走固定半角/全角网格测量；这用于兼容 eraFL `TAG_PRINT` 多行字符串把源码缩进带入按钮片段时的底部选项排版。

## 2026-07-03 同名图片跨目录缓存隔离

- `SpriteManager`：文件纹理缓存改为以完整规范化路径为主要 key，不再把 `Path.GetFileName()` 作为全局别名；`GetSprite` 和旧同步 `Loading` 回调也改用同一套路径级 key。这样不同目录下同名 `webp/png/jpg` 不会复用同一个 `TextureInfo`，避免 TW 角色立绘在同名文件跨文件夹时串图。仅在请求名本身是路径或没有文件路径时才保留 name alias。

## 2026-07-03 普通输出追加行滚动修正

- `uEmuera.Window.DecideScrollModeForDisplayDelta`：动态地图函数栈或动态地图视图中的重绘仍使用 `PreserveViewport`，避免地图刷新拉回底部；非动态地图输出如果本批 diff 中存在 `LineNo > previousMaxLineNo` 的真实追加行，即使同时刷新了旧行元数据，也改为 `FollowBottom`。这用于修正 TW 会话/泡茶等普通输出在聊完后停在旧历史位置、不自动跟随最新文本的问题。

## 2026-07-03 动态地图函数栈标记

- `ProcessState.IsInDynamicMapFunctionScope` / `EmueraConsole.IsDynamicMapOutputScopeActive`：输出行生成时在 ERB 后台线程读取当前调用栈，只识别 `DRAW_COLOREDMAP`、`DRAW_MAP`、`FIELDMAP` 等地图绘制根函数；`GETMAP`、`MAP_VIEWING` 等子函数不再单独触发地图标记，降低非地图页面误伤。
- `ConsoleDisplayLine.DynamicMapFunctionScoped` / `PrintStringBuffer` / `EmueraConsole.PrintHtml`：给来自地图根函数的显示行打元数据标记，并递归标到 HTML div 子行；`GenericUtils.LineHasDynamicMapBitmapContext` 仍同时接受 `BITMAP_CACHE_ENABLE` 与函数栈标记作为诊断和地图块识别证据。
- `uEmuera.Window.DecideScrollModeForDisplayDelta`：滚动策略只用 `DynamicMapFunctionScoped` 或已确认的动态地图视图来判定地图重绘；普通 `BITMAP_CACHE_ENABLE` 页面不再直接触发地图滚动策略，避免颜色滑块、立绘履历等非地图 UI 被误判。
- `uEmuera.Window.TryFindDynamicMapWindowStart`：只在显示列表尾部有限范围内寻找动态地图上下文，避免历史中的旧地图块长期影响后续普通文本滚动策略。

## 2026-07-03 去除动态地图视图裁剪实验

- `uEmuera.Window.Update`：动态地图检测仍保留，用于 `dynamicMapViewActive`、诊断日志和滚动策略；但不再把 `displayStartIndex` 裁到地图块开始行，也不再在进入/离开动态地图时调用 `GenericUtils.ClearText()` 重建 Godot 显示层。历史文本会继续参与行级 diff，进入地图后理论上可向上查看前文。
- 风险说明：这会恢复历史内容可见性，但也可能重新暴露 Android 上旧内容、地图块、选项一起刷新时的自动滚动或闪烁问题；本改动用于验证“视图裁剪是否是历史消失主因”。

## 2026-07-03 动态地图滚动事务第一步

- `GenericUtils.ApplyTextChanges` / `EmueraContent.ApplyTextChanges`：在保留旧 `scrollToBottom: bool` 入口的同时新增 `EmueraDisplayScrollMode`，用于把显示刷新后的滚动意图从简单布尔值扩展为“追底部、保留视口、保持当前选项可见”等模式。旧调用方仍按原语义工作，动态地图链路可以逐步迁移到更细的滚动策略。
- `uEmuera.Window.DecideScrollModeForDisplayDelta`：显示差异提交前根据删除尾行、更新旧行、追加新行和动态地图视图状态决定滚动模式。当前动态地图视图直接使用 `PreserveViewport`，避免刷新时拉回底部；`KeepChoicesVisible` 保留为可扩展模式，但不再用于动态地图。
- `EmueraContent.RequestKeepChoicesVisible`：保留“保持当前选项可见”的实现入口，等待 Godot 布局帧稳定后按当前按钮 generation 找选项并做最小补偿；当前动态地图链路不会触发该模式。

## 2026-07-02 动态地图刷新合并补充

- `EmueraConsole.RefreshStrings` / `deleteLine` / `BitmapCacheEnabledForNextLine`：动态地图或状态面板进入 `CLEARLINE`、`BITMAP_CACHE_ENABLE 1...0` 区域重画时，Running 中的普通 `RefreshStrings(false)` 会先合并，不向 Godot UI 提交半成品；进入 `INPUT/TINPUT/WAIT` 或显式 `RefreshStrings(true)` 时一次提交完整显示列表，避免 Android 看到“旧菜单 -> 半张地图 -> 地图主体”的循环中间帧。
- `PrintStringBuffer` / `EmueraConsole.PrintHtml`：`BITMAP_CACHE_ENABLE` 改为区域上下文，开启后直到脚本关闭前产生的普通文本行与 `HTML_PRINT` 行都会带 `BitmapCacheEnabled` 标记；这与 TW 动态地图脚本的成对使用方式一致，也让 UI 侧动态地图诊断和复用判断有连续块依据。
- `EmueraContent.QueueDisplayFollowUp`：当显示差异明确传入 `scrollToBottom=false` 时，会取消尚未完成的滚到底任务并记录当前视口位置，再执行布局边界更新；避免动态地图刷新被上一轮普通输出残留的 pending scroll 拉到底部。

更新时间：2026-06-07

用途：这是给 AI 和维护者快速定位代码用的地图。优先读本文件，再按路径进入源码。地图只记录结构、职责、主要接口和关键函数，不复制源码实现。

维护规则：
- 新增、移动、重命名 `Scripts/**/*.cs` 时，同步更新本文件对应目录表。
- 大文件只写职责和关键入口，不枚举所有私有 helper。
- `addons/**`、`*.uid`、字体、图标、导出产物默认只做目录摘要，不进入 C# 明细。
- 重新扫描可用：

```powershell
rg --files -g '*.cs' -g '!addons/**'
rg -n "^\s*(public|internal|protected).*\(" Scripts -g '*.cs'
rg -n "interface|abstract class|class .*:|enum " Scripts -g '*.cs' -g '!addons/**'
```

## 项目概览

`gEmuera` 是 Godot 4.7 + C# 的 Emuera 文本游戏引擎移植版。项目用 Godot 节点替换原 Windows Forms/GDI 渲染，同时保留大量原 Emuera 核心结构。

本次扫描范围：
- 项目 C#：`Scripts/**/*.cs`
- C# 文件数：156
- C# 代码行数约：85256
- 忽略：`addons/**`、`*.uid`、资源导入文件

核心运行链：

```text
project.godot
  run/main_scene = res://first_window.tscn
	-> FirstWindow._Ready()
	   扫描 era* 游戏目录，选择游戏
	-> main.tscn
	   -> EmueraMain._Ready()
		  初始化路径、配置映射、GPU 队列、EmueraContent
	   -> EmueraThread.Start()
		  后台 Thread 执行 Program.Main()
	   -> Program.Main()
		  创建 MainWindow/EmueraConsole/Process
	   -> Process.Initialize()
		  读取 config/csv/erb，建立 LabelDictionary
	   -> Process.DoScript() / runScriptProc()
		  执行 ERB 指令
	   -> EmueraConsole / GenericUtils / EmueraContent
		  输出文本、按钮、图片、音频和输入交互
```

## 2026-07-02 动态地图模拟器侧适配

- `config.toml` / `RuntimeDiagnosticsConfig`：新增 `[logging].dynamic_map` 简短开关和 `[debug.dynamic_map]` 专项参数，默认关闭；开启后记录动态地图刷新证据，不影响默认性能。
- `GenericUtils`：新增 `DYNAMIC_MAP.*` 结构化日志入口，按 `BitmapCacheEnabled` 与短时间上下文窗口筛选动态地图刷新，输出尾部行、按钮 generation、BitmapCache 标记和截断文本。
- `uEmuera/Window.Update`：在核心显示列表提交到 Godot UI 前判断本次差异是否为纯追加；删除尾部、更新已有行或重绘当前屏幕时传入 `scrollToBottom=false`，避免动态地图/状态面板刷新被当作普通文本追加而自动滚到底。显示差异不再只依赖 `ConsoleDisplayLine` 对象引用相等，而是按 `LineNo` 与视觉内容结构比较；视觉未变的纯显示行会复用既有节点，视觉未变但包含命令按钮的行会进入 data-only 刷新，只更新按钮输入数据与 Canvas 命中区。
- `uEmuera/Window.Update`：动态地图尾部出现连续多行 `BitmapCacheEnabled` 地图块时，会进入 UI 侧动态地图窗口，只向 Godot 显示层提交最后一段地图块及其后续选项行；这不会修改 Emuera 核心 `displayLineList`，用于避免命令菜单、地图追加和地图主体在 Android 上循环切换。
- `GenericUtils.ApplyTextChanges` / `EmueraContent.ApplyTextChanges`：显示差异新增 `scrollToBottom` 与 `dataOnlyLines` 契约；普通追加仍滚到底，重绘/替换批次只刷新布局和缩放边界并保留当前视口；data-only 行不重建 Control/Canvas 节点，用于先把动态地图变化与选项行刷新拆开。
- `EmueraContent.AddLine` / `EmueraContent.Canvas.AddCanvasLine`：按行替换已有内容时会先注销旧行资源；Canvas 行更新会释放旧 overlay 与纹理 pin 后再注册新行，避免从“整段删除重建”改为“单行更新”后留下旧节点。
- `EmueraContent.SetLastButtonGeneration` / `QuickButtons.UpdateButtonGeneration`：快捷按钮先收集完整按钮组，再按按钮顺序与内容生成签名；内容签名未变时复用现有按钮节点，只更新快捷按钮输入 generation，减少 Android 动态地图刷新时的底部按钮闪烁。

## 2026-06-07 移动端性能补充

- `QuickButtons`：快捷按钮面板复用按颜色缓存的 `StyleBoxFlat`，并缓存内容最小尺寸；按钮增删、换行、字体/尺寸变化时才重新计算，避免大量按钮惯性滚动时每帧触发布局测量。
- `EmueraContent.Canvas`：Canvas 图片资源名 fallback 会缓存成功解析路径，减少 Android 外部存储重复探测和递归查找；`SpriteAnime` overlay 在移动端节流刷新，静态文字、按钮、颜色与 fallback 语义不变。
- `SpriteManager`：纹理缓存清理只在超预算时排序，并限制单轮释放数量；仍严格遵守 `pinCount/refcount`，不会回收正在显示的 CBG、Canvas overlay 或行内图片。
- `EraStreamReader` / `StringStream`：ERB/CSV 热路径减少 `Trim/TrimStart/Substring` 分配；行连接 `{}` / `}` 校验语义保持原核心行为。
- `LabelDictionary` / `ErbLoader`：ERB label 字典在加载前按文件数量预估容量，降低大量 ERB 建表时的 rehash，不改变 `setLabelsArg`、`checkScript` 和 lazy 命中后的完整解析流程。
- `ConstantData` / `Preload`：角色模板列表和姓名映射按已知文件/角色数量预分配；移动端预读并行度限制为 2，避免老手机外部存储 I/O 与内存峰值。

线程模型：
- Godot 主线程：UI、输入、`EmueraContent`、`SpriteManager.UpdateOtherThreads()`（接收异步图片解码结果并主线程上传纹理）、GPU ColorMatrix 队列。
- 后台线程：`EmueraThread.Work()` 执行 `Program.Main()`、ERB 解释、阻塞式输入等待。
- 跨线程桥：`GenericUtils` 的 UI 队列、日志队列、显示队列；输入通过 `EmueraThread.Input()` 唤醒后台线程。

## 目录树

```text
.
|-- project.godot                 Godot 项目配置，主场景 first_window.tscn；`canvas_items + expand` 让 Android 宽屏不产生左右黑边
|-- first_window.tscn             启动器场景
|-- main.tscn                     主游戏场景
|-- config.toml                   精简运行期诊断配置；默认开启轻量日志以支持保存 `gemuera_*.log`，`[logging].enabled=false` 时日志/诊断系统完全关闭
|-- IDEAS.md                      项目工作约定，所有 AI 任务优先阅读
|-- CLAUDE.md                     Claude Code/AI CLI/AI IDE 执行指南
|-- action_maps/                  本地 AI/程序操作日志，不提交 GitHub，最多 30 个日志文件
|-- Text/                         Emuera 配置编码映射文本/bytes
|-- Lang/                         UI 多语言文本
|-- Fonts/                        内置字体
|-- Icons/                        UI 图标
|-- NativeLibs/android/           Android native 库
|-- Scripts/
|   |-- *.cs                      Godot UI、线程桥、渲染、精灵管理
|   |-- Diagnostics/              运行期诊断、日志、导出、面板
|   |-- Emuera/
|   |   |-- Config/               Emuera 配置系统
|   |   |-- Content/              图片、精灵、Graphics surface
|   |   |-- GameData/             变量、表达式、常量、函数方法
|   |   |-- GameProc/             ERB 加载、解析、执行状态机
|   |   |-- GameView/             控制台显示模型和 HTML/按钮/图片行
|   |   |-- Modern/               现代扩展函数
|   |   |-- Runtime/              SQLite、插件运行时工具
|   |   |-- Sub/                  词法、流、异常、存档二进制
|   |   `-- _Library/             Win/GDI/随机数/语言兼容工具
|   |-- Shaders/                  ColorMatrix shader
|   `-- uEmuera/                  System.Drawing / Forms 兼容层
|-- addons/
|   |-- gdUnit4/                  Godot 测试插件
|   `-- godot_mcp/                Godot MCP 编辑器插件
`-- patches/                      历史补丁
```

## C# 文件职责地图

### Scripts 根目录

| 文件 | 主要类型 | 职责 | 关键入口/函数 |
|---|---|---|---|
| `Scripts/EmueraMain.cs` | `EmueraMain : Node`, `GpuWorkItem`, `TextRenderItem` | 主场景入口；初始化配置映射、UI 根节点、`LegacySessionFacade` 生命周期与 GPU/文本渲染队列。 | `_Ready`, `StartGameDeferred`, `StartLegacySessionAsync`, `_Process`, `_ExitTree`, `Run`, `Clear`, `Restart`, `GpuSubmitColorMatrix`, `SubmitTextRender` |
| `Scripts/EmueraThread.cs` | `EmueraThread` | 后台执行 Emuera 核心；把 Godot 输入转成阻塞式 console 输入；M0 runner trace 开启时只读记录 submit/consume ordering point。 | `Start`, `End`, `Running`, `Input`, `Work` |
| `Scripts/EmueraContent.cs` | `EmueraContent : Control`, `UiDiagnosticOverlay` | Godot UI/输入/音频核心；创建控制台视口、可切换渲染后端、输入栏、快速按钮、缩放、诊断覆盖层，并保留旧 Control 行渲染作为回退；在移动端读取 Godot display safe area，将主内容、CBG、系统菜单和浮层限制到安全区，并按安全宽度动态更新 Android `WindowX/DrawableWidth`；主控制台缩放时按实际溢出动态启用横向滚动，缩回推荐/安全宽度后清除多余横向偏移；刷新 CBG/SETIMAGELAYER 时遇到占位或本帧上传失败纹理会保留旧角色层节点，避免临时白图替换正常立绘。 | `_Ready`, `GetSafeViewportRect`, `ApplySafeAreaLayout`, `ConfigureContentScrollContainer`, `NormalizeContentHorizontalScroll`, `AddLine`, `AddLines`, `ApplyTextChanges`, `UpdateDisplay`, `RefreshCBG`, `PlaySoundFile`, `PlayBgmFile`, `SetContentScale`, `_Input` |
| `Scripts/EmueraContent.Canvas.cs` | partial `EmueraContent`, `ConsoleRenderSurface`, `ConsoleRenderBackend` | 控制台 Canvas 自绘后端；普通文本/按钮/shape/常规图片按可视区绘制，并复刻原核心按钮选中/焦点背景/BackLog 普通文字颜色语义；ColorMatrix、`SpriteAnime`、非相对定位图片以少量 `EmueraImage` 局部 overlay 混合渲染；相对定位 `ConsoleDivPart` 复用旧 Control 构建为局部 overlay，absolute div 仍整行回退；Canvas 维护行布局 prefix 快照并用二分查找可视行范围，批量输出期间延迟刷新 overlay 行位置；overlay 行定位通过 `canvasRowsWithPositionedNodes` 只刷新实际存在整行 fallback Control、图片 overlay 或 div overlay 的行，避免每次遍历全部历史布局行；overlay 可见性通过“当前可见行/上一轮可见行/逃逸行”目标集合刷新，逃逸 overlay 继续按真实矩形裁剪；动画 overlay 维护 `(LineNo, Index)` 候选 key，避免 `_Process` 扫描历史全部图片 overlay；按钮 hit rect 在 Canvas 行注册时缓存到 `canvasLineButtonHits`，内容、滚动、缩放或视口变化时只标记 dirty，普通 Canvas `_Draw()` 不扫描按钮结构，实际点击进入 `TryHitGlobal` 前才按需重建命中表并用 `hitRectBuckets` 缩小扫描范围，未命中时再回退 overlay/旧控件树；普通移动端默认保留 240 行，Snake/TW 移动端默认 600 行并迁移旧 240 默认，普通桌面默认 360 行，Snake/TW 桌面默认 1500 行并迁移旧 360 默认；`Display.ConsoleRenderBackend=controls` 可切回旧节点后端。 | `CanRenderLineOnCanvas`, `AddCanvasLine`, `NotifyConsoleRenderContentChanged`, `TryGetVisibleCanvasLineLayoutRange`, `RefreshCanvasOverlayRows`, `RefreshCanvasOverlayVisibility`, `RefreshCanvasImageAnimations`, `ConsoleRenderSurface._Draw`, `TryHitGlobal` |
| `Scripts/EmueraImage.cs` | `EmueraImage : Control` | 绘制 `Texture2D` / `AtlasTexture` 的控件，支持 ColorMatrix material。 | `SetColorMatrix`, `_Draw` |
| `Scripts/GenericUtils.cs` | `GenericUtils`, `EmueraLogLevel`, `EmueraLogCategory`, `SnakeAudioInfo` | Emuera 核心到 Godot 的静态桥；日志总开关、诊断热路径闸门、UI 队列、文本输出、音频、输入回放；在 M0 runner trace 开启时记录 Display commit/scroll intent 与音频 effect enqueue；在 `[debug.performance_sampling]` 开启时低频聚合普通帧与 Canvas 控制台渲染采样。 | `InitializeLogging`, `IsLogEnabled`, `IsScrollTraceActive`, `FlushUI`, `AddText`, `ApplyTextChanges`, `SetBackgroundColor`, `PlaySoundFile`, `SamplePerformanceFrame`, `SampleConsoleRenderFrame`, `ExportDiagnosticPackage`, `RestartGame` |
| `Scripts/FirstWindow.cs` | `FirstWindow : Control` | 启动器；扫描 `era*` 游戏目录，切换语言/核心 profile，进入主场景；启动器外边距跟随 display safe area，避免横屏前摄/挖孔遮挡。 | `_Ready`, `ApplyLauncherSafeArea`, `_ExitTree`, `_Notification`, `ResolveStartupGamePath` |
| `Scripts/SpriteManager.cs` | `SpriteManager`, `TextureInfo`, `SpriteInfo` | 图片/精灵纹理缓存；AtlasTexture 管理；文件图片后台 I/O/解码请求；主线程限流接收解码结果并创建纹理；透明占位纹理带 `IsPlaceholder` 标记并按节流重试，真实纹理完成后可覆盖占位缓存，避免外部存储偶发读失败污染角色图层。 | `Init`, `GetSprite`, `GetTextureInfo`, `TryGetTextureInfoCached`, `RequestTextureInfoAsync`, `GetTextureInfoOtherThread`, `UpdateOtherThreads`, `TextureLoadVersion`, `UpdateCleanup`, `ForceClear` |
| `Scripts/ColorMatrixGPU.cs` | `ColorMatrixGPU` | ColorMatrix shader material 创建、缓存、LRU、uniform 设置。 | `CreateMaterial`, `GetSharedMaterial`, `GetMatrixKey`, `SetMatrixUniforms`, `CreateCompositMaterial` |
| `Scripts/QuickButtons.cs` | `QuickButtons : CanvasLayer` | 快捷按钮浮层；显示当前可选输入，处理点击/触摸。 | `_Ready`, `_Process`, `_Input`, `AddButton`, `Clear`, `ShiftLine`, `SetInputEnabled` |
| `Scripts/Inputpad.cs` | `Inputpad : Control` | 屏幕输入面板；数字/文字输入 UI。 | `_Ready`, `_Process`, `UpdateInputType`, `ShowPad`, `HidePad`, `HasInputFocus` |
| `Scripts/Scalepad.cs` | `Scalepad : Control` | UI 缩放控制。 | `_Ready`, `_Notification`, `SetScale`, `SyncScale`, `ShowPad`, `HidePad` |
| `Scripts/OptionWindow.cs` | `OptionWindow : Control` | 选项弹窗。 | `_Ready`, `ShowPopup` |
| `Scripts/SpriteDebugViewer.cs` | `SpriteDebugViewer : Control` | 精灵调试查看器，配合 `SpriteDebugNotifier` 查看加载图片。 | `_Ready`, `_Process`, `_Input`, `_ExitTree` |
| `Scripts/SpriteDebugNotifier.cs` | `SpriteDebugNotifier` | 精灵加载事件通知。 | `Notify`, `ImageLoadedHandler` |
| `Scripts/FrameRateHelper.cs` | `FrameRateHelper` | 应用帧率配置。 | `Apply`, `ApplyConfigFps` |
| `Scripts/ResolutionHelper.cs` | `ResolutionHelper` | 解析/应用窗口分辨率配置；Android 上不调用 `WindowSetSize`，避免横屏宽机型被缩成固定比例画布产生左右黑边。 | `Apply`, `RefreshResolutions` |
| `Scripts/MultiLanguage.cs` | `MultiLanguage` | 读取 `Lang/*.txt`，提供 UI 文案。 | `Load`, `Get`, `CurrentLanguage` |

### Scripts/Diagnostics

| 文件 | 主要类型 | 职责 | 关键入口/函数 |
|---|---|---|---|
| `DiagnosticLogRecord.cs` | `DiagnosticLogRecord` | 单条诊断日志记录和值格式化；`PERF.*` 性能事件镜像到 Godot/logcat 时附带结构化 `data`，便于 Android 实机直接读取采样数值。 | `FormatForGodot`, `FormatForExport` |
| `DiagnosticLogRouter.cs` | `DiagnosticLogRouter` | 日志总闸门、类别过滤、限流、脱敏、record 构造；关闭时热路径直接返回；限流与单调时间戳使用 CLR `Stopwatch`，允许后台线程在 Godot 退出阶段继续安全写诊断。 | `Initialize`, `Reload`, `IsLoggingEnabled`, `IsEnabled`, `CheckRateLimit`, `BuildRecord`, `GetMonotonicMilliseconds`, `RedactPath` |
| `DiagnosticLogSinks.cs` | `DiagnosticLogSinks` | 环形日志缓存和 Godot 输出镜像；M0 trace 开启时把最终 Error sink 记录为 typed error event。 | `Initialize`, `Write`, `Snapshot`, `SetMirrorNonErrorToGodot` |
| `DiagnosticLogExporter.cs` | `DiagnosticLogExporter` | 导出诊断包/日志，记录面包屑，清理保留文件。 | `ExportDiagnosticPackage`, `ExportDiagnosticLog`, `WriteBreadcrumb`, `RunRetentionCleanup` |
| `InputReplayBuffer.cs` | `InputReplayBuffer` | 输入回放环形缓冲；相对时间戳复用诊断路由的 CLR 单调时钟，避免后台线程依赖 Godot `Time`。 | `Capture`, `BuildExportText` |
| `SaveLogOperationTrail.cs` | `SaveLogOperationTrail` | 存档/日志操作轨迹缓存；时间戳复用诊断路由的 CLR 单调时钟。 | `Capture`, `BuildExportText` |
| `RuntimeDiagnosticsConfig.cs` | `RuntimeDiagnosticsConfig` 等配置类 | 运行期诊断配置模型、默认值、精简 logging 开关展开和关闭态清理。 | `CreateDefault`, `ApplyMinimalLoggingConfig`, `DisableAllDiagnostics`, `GetRuntimeLogLevel`, `GetActiveDebugModel` |
| `RuntimeDiagnosticsConfigLoader.cs` | `RuntimeDiagnosticsConfigLoader`, `LoadResult` | 读取 `config.toml` 和用户覆盖配置，支持精简 `[logging]` 总开关和模块开关。 | `Load` |
| `RuntimeDiagnosticsConfigWriter.cs` | `RuntimeDiagnosticsConfigWriter` | 写出精简用户诊断配置 TOML。 | `SaveUserConfig`, `BuildToml` |
| `RuntimeTomlParser.cs` | `RuntimeTomlParser` | 简易 TOML 解析器。 | `Parse` |
| `RuntimeDiagnosticsPanel.cs` | `RuntimeDiagnosticsPanel`, `FloatingDiagnosticsHost` | Godot 内置诊断浮窗。 | `AttachFloatingTo`, `_Ready`, `_Notification` |

### Scripts/uEmuera 兼容层

| 文件 | 主要类型 | 职责 | 关键入口/函数 |
|---|---|---|---|
| `uEmuera/Application.cs` | `Application` | Windows Forms `Application` 兼容桩。 | `EnableVisualStyles`, `SetCompatibleTextRenderingDefault`, `Run` |
| `uEmuera/Drawing.cs` | `Bitmap`, `BitmapTexture`, `Graphics`, `Color`, `Font`, `Rectangle`, `Point`, `Size` | `System.Drawing` 替代层；包装 Godot Image/Texture、颜色、字体、几何类型；`BitmapTexture` 遇到占位缓存时允许 `SpriteManager` 按节流异步重试真实纹理。 | `Bitmap.Save`, `BitmapTexture`, `Color.FromArgb`, `Color.ToGodotColor`, `Rectangle.Intersect` |
| `uEmuera/Forms.cs` | `Timer`, `MessageBox`, `ScrollBar`, `ToolTip`, `PictureBox`, `TextBox` | `System.Windows.Forms` 替代层；Timer 由 Godot loop 手动 Update。 | `Timer.Update`, `MessageBox.Show`, `ToolTip.SetToolTip` |
| `uEmuera/Window.cs` | `MainWindow`, `DebugDialog` | 原主窗体兼容桩，桥接 `EmueraConsole` 和 `Process`。 | `MainWindow.Init`, `Update`, `Refresh`, `WaitForRefreshProcessed`, `Reboot` |
| `uEmuera/Utils.cs` | `Logger`, `Utils` | 文件系统、编码、资源扫描、显示宽度、路径规范化工具；Godot 文件 API 下的通配符枚举在单次目录扫描内复用 Regex 匹配，保持原通配符语义并避免按文件重复构造匹配器；`ResourcePrepare` 读取资源 CSV 时只扫描头部字段，避免为每行创建完整 `string[]`。 | `SHIFTJIS_to_UTF8`, `NormalizePath`, `FileExists`, `GetFilePaths`, `GetDisplayLength`, `ResourcePrepare` |
| `uEmuera/Properties.cs` | `ResourceManager`, `Resources` | 原资源访问兼容。 | `GetString` |
| `uEmuera/VisualBasic.cs` | `Strings`, `VbStrConv` | VB 字符串转换兼容。 | `StrConv` |
| `uEmuera/Media.cs` | `Hand`, `Asterisk` | 系统声音兼容桩。 | `Play` |
| `uEmuera/partial/EmueraConsole.cs` | partial `EmueraConsole` | 给 uEmuera 层访问显示行和输入等待状态的扩展，等待态包含 `WaitInputNoFocus`。 | `GetDisplayLinesForuEmuera`, `GetDisplayLinesCount`, `GetDisplayLinesSnapshotForuEmuera`, `IsWaitingInput` |
| `uEmuera/partial/AConsoleColoredPart.cs` | partial `AConsoleColoredPart` | 显示部件兼容扩展。 | 主要是 partial 补充 |

### Scripts/Emuera 根

| 文件 | 主要类型 | 职责 | 关键入口/函数 |
|---|---|---|---|
| `Emuera/Program.cs` | `Program`, `EmueraCoreProfile` | 原 Emuera 入口；设置目录、核心 profile、配置、窗口、Process。 | `Main`, `AppendSnakeStartupErrorLog`, `DetectCoreProfile`, `ConfigureModernMobileCoreAdapters` |
| `Emuera/GlobalStatic.cs` | `GlobalStatic` | 核心全局对象注册和重置；保存插件存在标志。 | `Reset`, `ExistPlugin` 及静态字段 |

### Scripts/Emuera/Config

| 文件 | 主要类型 | 职责 | 关键入口/函数 |
|---|---|---|---|
| `Config.cs` | `Config` | 运行配置静态访问；字体、路径、窗口尺寸、更新检查、`UPDATECHECK` 禁用、插件警告、`BEFORE_ERROR/THROW` 禁用、`UseScopedVariableInstruction`、debug config。 | `SetConfig`, `GetFont`, `ClearFont`, `CreateSavDir`, `CheckUpdate`, `UpdateWindowWidth`, `SetDebugConfig` |
| `ConfigData.cs` | `ConfigData` | 配置数据实体，保存所有 Emuera 选项，包含插件警告、异常前事件禁用项、`VARI/VARS` 开关、v24 `TextDrawingMode.SKIASHARP` 默认兼容和默认开启 lazy loading。 | 构造/读取/保存配置项 |
| `ConfigCode.cs` | `ConfigCode` 等 enum | 配置项枚举和相关枚举，包含 `PluginAvailableWarn`、`DisableBeforeErrorThrow`、`UseScopedVariableInstruction`、`TextDrawingMode.SKIASHARP` 和 `RenderingBackend` 兼容枚举。 | 枚举定义 |
| `ConfigItem.cs` | `AConfigItem`, `ConfigItem<T>` | 单个配置项的解析/序列化容器。 | `ToString`, value parse 相关 |
| `JSONConfig.cs` | `JSONConfig` | v24/snake `setting.json` 兼容配置层；启动时创建/读取 JSON，映射 `UseScopedVariableInstruction` 到旧配置并公开 JSON-only 开关。 | `Load`, `Save` |
| `JSONConfigData.cs` | `JSONConfigData` | `setting.json` 数据模型，包含 `UseButtonFocusBackgroundColor`、`UseNewRandom`、`UseScopedVariableInstruction`、`RenderingBackend`。 | JSON 属性 |
| `KeyMacro.cs` | `KeyMacro` | 快捷键宏配置。 | `Load`, `Save`, key macro 访问 |

### Scripts/Emuera/Content

| 文件 | 主要类型 | 职责 | 关键入口/函数 |
|---|---|---|---|
| `AContentFile.cs` | `AContentFile : IDisposable` | 内容文件抽象基类。 | `Dispose` |
| `AContentItem.cs` | 空 namespace 占位 | 预留文件，当前不定义类型。 | 无 |
| `AppContents.cs` | `AppContents`, `LazySpriteDefinition` | 资源/精灵表注册；读取资源 CSV；lazy sprite 索引在启动阶段只保存原始 CSV 行和头字段，命中具体 sprite 时再完整裸逗号拆分；资源 CSV 路径存在性/大小写解析缓存；lazy sprite 首次实体化慢调用诊断。 | `CreateSpriteAnime`, `BuildLazyResourceIndex`, `RealizeLazySprite` |
| `ConstImage.cs` | `AbstractImage`, `ConstImage` | 不可变图片资源，基于 Bitmap/Image。 | `CreateFrom`, `Dispose` |
| `CroppedImage.cs` | `ASprite`, `ASpriteSingle`, `SpriteG`, `SpriteF`, `SpriteAnime` | 精灵裁剪、动画帧、绘制接口。 | `SpriteGetColor`, `GraphicsDraw`, `AddFrame`, `PauseAnimation`, `ResumeAnimation`, `GetCurrentFrameInfo` |
| `GraphicsImage.cs` | `GraphicsImage : AbstractImage` | ERB 图形 surface；绘制 sprite、线、文字、多边形、旋转、ColorMatrix；维护 `DisplayRevision` 和稳定快照，后台线程重绘角色差分时 UI 只提交完整稳定帧，资源层暂不可用时保留旧显示，避免动态图像中间态闪白；`ClearLowAlpha` 用 `GetData/SetData` 字节数组一次性把 alpha 低于阈值的像素清零，替代 ERB 侧逐像素 `GGETCOLOR/GSETCOLOR` 双重 FOR 循环（縁取り描边特效 `画像_僅少アルファ除去` 的性能瓶颈）。 | `GCreate`, `GDrawCImg`, `TryCreateDisplaySnapshot`, `ApplyColorMatrixGPU`, `GDrawG`, `GDrawString`, `GRotate`, `GDispose`, `ClearLowAlpha` |

### Scripts/Emuera/GameData

| 文件 | 主要类型 | 职责 | 关键入口/函数 |
|---|---|---|---|
| `ConstantData.cs` | `ConstantData`, `CharacterTemplate`, `LazyErdNameData` | CSV 常量、角色模板、ERD 名称数据；维护整数/字符串/小数 1D 变量默认长度，`LOCALF/ARGF` 使用小数长度表；`VariableSize.CSV`、角色 CSV、名称 CSV、别名 CSV 和 `VarExt*.csv` 热路径使用轻量裸逗号字段读取，避免整行 `Split(',')` 分配；读取 `VarExt*.csv` 中 MAP/XML/DT 的 `SAVE/GLOBAL/STATIC` 保存域声明。 | 常量读取、角色模板访问 |
| `DefineMacro.cs` | `DefineMacro` | `#DEFINE` 宏数据。 | 构造和字段 |
| `EraType.cs` | `EraType`, `EraTypeHelper` | Era 值类型枚举和 CLR `Type` 过渡转换 helper；迁移期用于把旧 `long/string/double` 签名统一映射到整数/字符串/小数语义。 | `FromClrType`, `ToClrType`, 枚举定义 |
| `GameBase.cs` | `GameBase` | 游戏基础信息、版本、标题、更新检查 URL/版本名等；`GAMEBASE.CSV` 保留原核心裸逗号语义，但只扫描前两个字段以减少启动期分配。 | `LoadGameBaseCsv`, 基础字段访问 |
| `IdentifierDictionary.cs` | `IdentifierDictionary` | 变量/函数/宏名解析字典，保留 `REF/REFF` 等关键字并管理局部变量默认尺寸/禁用状态。 | `GetIdentifier`, `Add`, defined-name 管理 |
| `ParserMediator.cs` | `ParserMediator` | 表达式、变量、函数解析的中介和 warning 管理。 | `Initialize`, `GetWarningList`, parse helper |
| `StrForm.cs` | `StrForm`, `FormattedStringMethod` 系列 | 格式化字符串表达式。 | `GetString`, format 方法 |

### Scripts/Emuera/GameData/Expression

| 文件 | 主要类型 | 职责 | 关键入口/函数 |
|---|---|---|---|
| `IOperandTerm.cs` | `IOperandTerm` | 表达式操作数抽象基类；内部以 `EraType` 保存整数/字符串/小数类型，`GetOperandType()` 仅作为旧 CLR `Type` 桥接入口。 | `GetIntValue`, `GetStrValue`, `GetFloatValue`, `GetValue`, `GetEraType`, `GetOperandType`, `Restructure` |
| `Term.cs` | `NullTerm`, `SingleTerm`, `StrFormTerm`, `VariadicArgTerm` | 常量/字符串格式/可变参数表达式项；常量和可变参数类型判断走 `EraType`。 | `GetValue`, `GetIntValue`, `GetStrValue`, `Restructure` |
| `ExpressionParser.cs` | `ExpressionParser` | ERB 表达式解析器。 | `ReduceExpression`, `ReduceArguments`, `ReadExpression` 类方法 |
| `ExpressionMediator.cs` | `ExpressionMediator` | 表达式求值上下文，连接变量和函数；暴露 `CurrentContext` 供局部变量 token 读取当前调用栈的运行期数组。 | 变量/函数访问、运行时上下文 |
| `OperatorCode.cs` | `OperatorCode`, `OperatorManager` | 运算符枚举和查找。 | `GetOperator`, operator metadata |
| `OperatorMethod.cs` | `OperatorMethod`, 多个具体运算符 | 运算符求值实现。 | `OperatorMethodManager.Initialize`, `GetIntValue`, `GetStrValue`, `GetReturnValue` |
| `SafeArithmetic.cs` | `SafeArithmetic` | 整数/浮点安全数学工具。 | `Add`, `Sub`, `Mul`, `Div`, `Pow` 等 |
| `CaseExpression.cs` | `CaseExpression` | `CASE` 条件表达式。 | `IsMatch`, 条件求值 |

### Scripts/Emuera/GameData/Function

| 文件 | 主要类型 | 职责 | 关键入口/函数 |
|---|---|---|---|
| `FunctionMethod.cs` | `FunctionMethod` | 内置函数抽象基类；返回类型和默认参数签名都使用 `EraType`，默认参数校验按脚本语义类型比较表达式。 | `CheckArgumentType`, `GetIntValue`, `GetStrValue`, `GetFloatValue`, `GetReturnValue`, `UniqueRestructure` |
| `FunctionMethodTerm.cs` | `FunctionMethodTerm` | 把 `FunctionMethod` 包装成表达式项。 | `GetIntValue`, `GetStrValue`, `Restructure` |
| `Creator.cs` | partial `FunctionMethodCreator` | 创建内置函数表。 | `GetMethodList` |
| `Creator.Method.cs` | partial `FunctionMethodCreator` | 大量基础内置函数：角色、CSV、字符串、数学、图像、音频、平台识别、变量访问等；`GETNUM/GETNUMB/GETPALAMLV/GETEXPLV` 对齐 CSV 编号与等级查找语义；`EVAL/EVALF/EVALS` 分别执行整数/小数/字符串动态表达式求值；`BITSET/BITGET/BITTOGGLE/BITINDEXOFFIRST` 使用整数 1D 数组作为位图并兼容 `SparseArray<long>`；`ARRAYMSORT/ARRAYMSORTEX` 支持 int/string/float 排序、1D 稀疏数组和 1D/2D/3D 目标数组首维重排；`SUMARRAY/SUMCARRAY`、`MAXARRAY/MINARRAY`、`MATCH/CMATCH`、`GROUPMATCH/NOSAMES/ALLSAMES`、`INRANGEARRAY/INRANGECARRAY` 兼容整数/小数数组统计与检索；`GraphicsClearLowAlphaMethod`（`GCLEARLOWALPHA`）批处理清除低 alpha 像素，供 ERB 脚本替换逐像素 `GGETCOLOR/GSETCOLOR` 双重循环。 | 嵌套 `*Method : FunctionMethod`；统一 override `GetIntValue/GetStrValue/GetReturnValue` |
| `Creator.Method.DT.cs` | partial `FunctionMethodCreator` | DataTable 扩展函数。 | `DtCreate`, `DtRowAdd`, `DtCellGet`, `DtSelect`, XML 互转 |
| `Creator.Method.Map.cs` | partial `FunctionMethodCreator` | Map 扩展函数。 | `MapCreate`, `MapSet`, `MapGet`, `MapKeys`, `MapToXml` |
| `Creator.Method.Sql.cs` | partial `FunctionMethodCreator` | SQL 扩展函数。 | `SqlConnect`, `SqlExecuteReader`, `SqlReaderGet*`, import/export |
| `Creator.Method.Xml.cs` | partial `FunctionMethodCreator` | XML 扩展函数。 | `XmlDocument`, `XmlGet`, `XmlSet`, `XmlAddNode`, `XmlRemoveNode` |
| `RuntimeDataStore.cs` | `RuntimeDataStore` | 运行期 DataTable/Map/XML 静态存储；按 `VarExt*.csv` 的 `SAVE/GLOBAL/STATIC` 声明域清理、筛选和保存 Map/XML/DataTable，避免读档误删非保存域运行期缓存。 | `Clear`, `ClearSaveData`, `ClearGlobalData`, `ClearStaticData`, `DataTables`, `Maps`, `XmlDocuments` |
| `UserDefinedMethodTerm.cs` | `UserDefinedMethodTerm`, `UserDefinedRefMethodTerm` | 用户定义函数调用表达式项；以 `EraType` 接收函数返回类型，并在 `IOperandTerm` 边界桥接回 CLR operand type。 | `Create`, `Restructure`, `GetRefName`, `GetValue` |
| `UserDefinedRefMethod.cs` | `UserDefinedRefMethod` | `#REF/#REFS/#REFF` 引用函数匹配和绑定；`RetType` 使用 `EraType` 与 `FunctionLabelLine.MethodType` 对齐。 | `Create`, `MatchType`, `SetReference` |

### Scripts/Emuera/GameData/Variable

| 文件 | 主要类型 | 职责 | 关键入口/函数 |
|---|---|---|---|
| `VariableCode.cs` | `VariableCode` | Emuera 变量枚举；包含 `COUNT` 禁用标志、`LOCALF/ARGF` 小数局部编号、`GAMEBASE_URL/GAMEBASE_VERSIONNAME` 和 `REFF*` 小数引用变量。 | 枚举定义 |
| `VariableIdentifier.cs` | `VariableIdentifier` | 变量名到 `VariableCode`/scope 的解析；类型、维度和属性判断优先来自 `VariableDescriptor` 元数据。 | `GetVarNameDic`, `GetVariableId`, `GetExtSaveList`, `Descriptor` |
| `VariableDescriptor.cs` | `VariableDescriptor`, `VariableDescriptorTable`, `VariableKind`, `VariableDimension`, `VariableAttribute` | Snake 兼容的变量描述符元数据层；集中解释 `VariableCode` 位标志为类型、维度和属性，目前不改变存储布局或存档格式。 | `FromCode`, `GetDescriptorByCode`, `TryGetDescriptor` |
| `SparseArray.cs` | `SparseArray<T>` | 1D 变量稀疏存储容器；未写入元素按默认值读取，用于降低 Android 上巨型数组的初始内存占用，2D/3D 仍保持 CLR 多维数组契约。 | `Length`, indexer, `Entries`, `FromArray`, `ToArray`, `Shift`, `RemoveRange`, `Sort` |
| `VariableData.cs` | `VariableData : IDisposable` | 全局变量数据容器和变量 token 构造；暴露 GAMEBASE URL/版本名常量，按小数长度表初始化 `LOCALF/ARGF`，全局 1D 整数/字符串数组使用 `SparseArray<T>` 存储。 | 初始化变量、读取/保存、`Dispose` |
| `VariableEvaluator.cs` | `VariableEvaluator : IDisposable` | 变量求值、读写、角色变量访问、局部变量栈；`RESULT_ARRAY`、`RESULTS_ARRAY`、`RESULTF`、`SELECTCOM_ARRAY`、`ITEMSALES`、`RANDDATA` 等结果/工作变量兼容小数和 1D 稀疏存储，`VARSET/CVARSET` 批量赋值按 `EraType` 分派整数/字符串/小数路径，数组求和、匹配计数、最大/最小、区间统计 helper 覆盖小数数组；读档/全局读档按 VarExt 声明域处理 RuntimeDataStore，保留非保存域运行期缓存。 | `GetValue`, `SetValue`, `GetNextRand`, `SetValueAll`, `LoadFrom`, `LoadGlobal`, local/reference 管理 |
| `ElementRefInfo.cs` | `ElementRefInfo` | Snake 兼容的元素级 REF 信息；捕获变量 token、索引和非角色数组实体，供标量 REF 参数读写数组单个元素。 | `GetIntValue`, `GetStrValue`, `GetFloatValue`, `SetValue`, `PlusValue` |
| `NullRefTerm.cs` | `NullRefTerm` | `OUT REF` 参数省略时的空引用占位；读零/空串、写入无操作。 | `GetIntValue`, `GetStrValue`, `GetFloatValue`, `SetValue`, `GetArray` |
| `VariableToken.cs` | `VariableToken` 及大量派生 token | 变量实际存取实现；静态/私有/局部/引用/角色/常量/伪变量，基础类型/维度/保存属性由 `VariableDescriptor` 驱动并公开 `EraType`；`LOCAL/LOCALS/LOCALF/ARG/ARGS/ARGF` 运行期数组按当前 `ExecutionContext` 读取，带 `@FUNCNAME` 的局部变量会在调用栈中查找匹配函数上下文；`REFF/REFF2D/REFF3D` 使用小数引用类型；`ReferenceToken` 保存数组引用、标量引用、元素引用和空引用状态，1D REF 路径同时支持 CLR 数组和 `SparseArray<T>`。 | `GetIntValue`, `GetStrValue`, `GetFloatValue`, `GetEraType`, `SetValue`, `SetValueAll`, `PlusValue`, `In`, `Out`, `SetRef`, `SetNullRef`, `MatchType` |
| `VariableTerm.cs` | `VariableTerm`, `FixedVariableTerm`, `VariableNoArgTerm` | 表达式中的变量访问项；暴露变量 `EraType` 和参数个数供函数参数转换、可变参数和元素级 REF 捕获索引。 | `GetIntValue`, `SetValue`, `GetEraType`, `Restructure`, `ArgumentCount` |
| `VariableStrArgTerm.cs` | `VariableStrArgTerm` | 字符串索引变量表达式项。 | `GetStrValue`, `Restructure` |
| `VariableLocal.cs` | `VariableLocal` | 局部变量 token 注册表；仍负责按函数标签尺寸创建 `LOCAL/LOCALS/LOCALF/ARG/ARGS/ARGF` token，但实际运行期数组已改由 `ExecutionContext` 持有。 | local token 创建、尺寸调整、默认值重置 |
| `VariableParser.cs` | `VariableParser` | 变量表达式解析。 | `Parse`, variable term 构造 |
| `CharacterData.cs` | `CharacterData : IDisposable` | 角色数据数组和角色变量管理；角色 1D 整数/字符串变量及可适配的用户定义角色 1D 变量使用 `SparseArray<T>`，排序键读取兼容稀疏数组。 | 角色增删、保存/读取、`Dispose` |

### Scripts/Emuera/GameProc

| 文件 | 主要类型 | 职责 | 关键入口/函数 |
|---|---|---|---|
| `Process.cs` | partial `Process` | 脚本处理器主类；初始化、输入结果、开始执行、异常处理；脚本错误终止时清理函数栈与 `ExecutionContext`，表达式函数异常路径防止局部上下文残留，并在 `BEFORE_THROW` 内部异常时跳过二次 `BEFORE_ERROR`。 | `Initialize`, `InitializeAsync`, `DoScript`, `BeginTitle`, `InputInteger`, `InputString`, `ReloadErb`, `ReloadErbAsync`, `ReloadPartialErb`, `ReloadPartialErbAsync`, `GetRunningPosition` |
| `Process.ScriptProc.cs` | partial `Process` | 内层脚本执行循环和 debug 执行；函数自然流到末尾时按当前栈顶判断是否为 `#FUNCTION/#FUNCTIONS/#FUNCTIONF`，避免外层表达式函数求值影响普通 `CALL` 的 `RESULT`/返回流程；`THROW` 会记录 pending throw、进入 `BEFORE_THROW`，并在 `BEFORE_THROW/BEFORE_ERROR` 内部只打印消息避免递归错误事件。 | `runScriptProc`, `DoDebugNormalFunction`, `saveCurrentState`, `loadPrevState` |
| `ExecutionContext.cs` | `ExecutionContext` | 函数执行上下文；持有当前调用帧的 `LOCAL/LOCALS/LOCALF/ARG/ARGS/ARGF` 运行期数组和父子关系，用于替代旧的共享局部数组存储。 | 构造函数、`Dispose`, `Parent`, `Local*`, `Arg*` |
| `Process.State.cs` | `ProcessState`, `SystemStateCode`, `BeginType` | CALL/JUMP/RETURN、BEGIN、函数栈、返回值、状态克隆；维护 `ExecutionContext` 栈，进入函数时绑定数组 REF、元素级 REF、OUT 空引用并创建局部执行上下文；区分外层表达式函数根 `IsFunctionMethod` 与当前栈顶 `IsCurrentFunctionMethod`，普通 `CALL` 在 `#FUNCTION` 求值期间返回时不会被误弹成 `RETURNF`；调试/表达式求值可通过 `CaptureCallState`/`RollbackToState` 恢复函数栈、上下文栈和 `CurrentLine`，克隆状态保留原上下文栈供监视表达式读取 `LOCAL@FUNCNAME`；`ClearFunctionListPreserveTrace` 用于错误/THROW 后保留调试调用栈显示。 | `JumpTo`, `SetBegin`, `Begin`, `Return`, `IntoFunction`, `ReturnF`, `IsCurrentFunctionMethod`, `CurrentContext`, `FindContextByLabel`, `CaptureCallState`, `RollbackToState`, `ClearFunctionListPreserveTrace`, `Clone` |
| `Process.SystemProc.cs` | partial `Process` | 系统流程处理。 | 系统状态执行 helper |
| `Process.CalledFunction.cs` | `CalledFunction`, `UserDefinedFunctionArgument` | 调用栈条目和用户函数实参；转换并暂存普通参数、数组 REF、元素级 REF 和 OUT 空引用；用户函数参数按 `EraType` 处理整数到小数的兼容扩展和可变参数类型，`VariadicArgTerm` 不进入普通 transporter，统一由 `ProcessState.IntoFunction` 展开。 | `ConvertArg`, `SetTransporter`, 参数暂存数组 |
| `Process.LazyLoading.cs` | partial `Process`, `LazyStatus` | ERB lazy loading 表、索引、按需加载、缓存；Android 索引缺失/失效时优先用轻量标签扫描建表，预扫描只抽取 `@label` 与 `#FUNCTION/#FUNCTIONS/#FUNCTIONF`，非 Android 保持 BuildTable/full-load 建表路径；首次真实命中仍执行完整 ERB 解析与检查；索引构建和局部更新会排除事件函数与方法文件，避免预解析依赖的方法被延迟加载；运行期维护 file -> functions 反向索引，按需加载后只移除相关映射，避免扫描整张 lazy 表；运行期 lazy ERB 补加载带慢调用诊断；EVENTLOAD 保持命中时按需加载，`PreloadEventLoadLazyErbs` 仅保留为诊断/实验入口，不在系统读档流程调用。 | `TryLazyLoadErb`, `LoadLazyLoadingTable`, `SaveLazyLoadingList`, `SavePartialLazyLoadingList`, `PreloadEventLoadLazyErbs` |
| `ErbLoader.cs` | `ErbLoader`, `PPState` | 读取/预处理 ERB/ERH 文件，生成 logical lines/labels；提供 `LoadErbFilesAsync` / `LoadErbsAsync` 作为主入口，旧同步方法仅做兼容包装。 | `LoadErbFiles`, `LoadErbFilesAsync`, `loadErbs`, `LoadErbsAsync`, `warningDic` |
| `HeaderFileLoader.cs` | `HeaderFileLoader` | 读取头文件/定义。 | header 加载入口 |
| `SelectCaseJumpTable.cs` | `SelectCaseJumpTable` | Snake 兼容的 `SELECTCASE` 常量分支跳转表；对整数/字符串/小数常量 `CASE` 建表，范围、比较和运行期表达式回退顺序扫描。 | `TryBuild`, `Lookup` |
| `LogicalLine.cs` | `LogicalLine`, `InstructionLine`, `FunctionLabelLine`, `GotoLabelLine` | ERB 逻辑行模型；函数标签记录 `LOCAL/LOCALS/LOCALF/ARG/ARGS/ARGF` 尺寸，`FunctionLabelLine.MethodType` 以 `EraType` 保存 `#FUNCTION/#FUNCTIONS/#FUNCTIONF` 返回类型。 | `FunctionLabelLine`, `InstructionLine`, label/goto 访问 |
| `LogicalLineParser.cs` | `LogicalLineParser` | 将文本行解析为 `LogicalLine`；支持 `#FUNCTIONF`、`#LOCALFSIZE`、`#REFF` 和 Snake 小数私有变量兼容解析。 | `ParseSharpLine`, `ParseLine`, `ParseLabelLine` |
| `LabelDictionary.cs` | `LabelDictionary` | 函数 label、事件 label、`$` label 索引；事件 label 合并 `LOCAL/LOCALS/LOCALF` 最大尺寸并同步 ARGF 尺寸。 | `AddLabel`, `SortLabels`, `GetEventLabels`, `GetNonEventLabel`, `GetLabelDollar` |
| `InputRequest.cs` | `InputRequest`, `InputType` | 输入请求类型和值约束，`NoFocus` 标记用于 NF 定时输入。 | 构造和字段 |
| `UserDefinedFunction.cs` | `UserDefinedFunctionData` | 用户定义函数元数据。 | 构造和参数类型 |
| `UserDefinedVariable.cs` | `UserDefinedVariableData`, `DimLineWC` | 用户定义变量元数据。 | 构造、维度/类型信息 |

### Scripts/Emuera/GameProc/Function

| 文件 | 主要类型 | 职责 | 关键入口/函数 |
|---|---|---|---|
| `Instruction.cs` | `AbstractInstruction` | ERB 指令抽象基类。 | `SetJumpTo`, `DoInstruction`, `CreateArgument` |
| `Instraction.Child.cs` | partial `FunctionIdentifier`, 多个 `*Instruction` | 具体 ERB 指令实现；文件名保留原拼写 `Instraction`；包含 Snake/v24 兼容指令、`TINPUTNF/TINPUTSNF/TONEINPUTNF/TONEINPUTSNF` 和渲染控制 API；`TRYCALLF/TRYCALLFORMF` 预解析失败静默返回，普通 `CALLF/CALLFORMF` 继续报告解析警告。 | `PRINT_Instruction`, `TINPUT_Instruction`, `CALL_Instruction`, `CALLF_Instruction`, `GOTO_Instruction`, `RETURNF_Instruction`, `SNAKE_UI_SETTING_Instruction` 等 |
| `FunctionIdentifier.cs` | `FunctionIdentifier` | 指令名/FunctionCode 映射和指令分类，注册 NF 定时输入变体；`VARI/VARS` 由 `Config.UseScopedVariableInstruction` 控制。 | `GetInstructionNameDic`, `IsPrint`, `IsInput`, `IsJump`, `IsMethod`, `IsFlowContorol` |
| `BuiltInFunctionCode.cs` | `FunctionCode` | 内置指令/函数 code 枚举，包含 NF 定时输入 code。 | 枚举定义 |
| `FunctionArgType.cs` | `FunctionArgType` | 指令参数类型枚举。 | 枚举定义 |
| `Argument.cs` | `Argument` 及大量 `Sp*Argument` | 已解析指令参数的数据对象；通用 `ExpressionsArgument.ArgumentTypeArray` 使用 `EraType[]` 保存指令参数类型契约。 | 构造和字段 |
| `ArgumentBuilder.cs` | `ArgumentBuilder`, 多个 `*ArgumentBuilder` | 针对不同指令构造 `Argument`；通用参数表与 `checkArgumentType` 使用 `EraType` 校验，`EraType.Void` 表示任意/省略兼容位。 | `Build`, `CheckArgument`, 各指令 builder |
| `ArgumentParser.cs` | partial `ArgumentParser` | 根据 `FunctionIdentifier` 解析参数。 | `ParseArgument` |

### Scripts/Emuera/GameView

| 文件 | 主要类型 | 职责 | 关键入口/函数 |
|---|---|---|---|
| `EmueraConsole.cs` | partial `EmueraConsole`, `DisplayLineList`, `ClientBackGroundImage` | 控制台状态、输入等待、NF 等待态、CBG/图片层、鼠标/键盘、调试、重载；保存 v24 渲染控制 API 和 `HOTKEY_STATE` 的脚本可见状态；M0 trace 开启时记录 wait request/completion。 | `Initialize`, `WaitInput`, `IsWaitInputState`, `PressEnterKey`, `callEmueraProgram`, `RefreshStrings`, `CBG_SetImage`, `SetImageLayer`, `SetSnakeTextDrawingMode`, `HotkeyStateInitialize`, `TryEvaluateHotkey`, `ReloadErb`, `Dispose` |
| `EmueraConsole.Print.cs` | partial `EmueraConsole` | 打印文本、HTML、按钮、图片、形状、日志输出；维护 `IsLineEnd`、`LINECOUNT`、`CLEARLINE` 的逻辑行语义和 `PrintC/PrintButtonC` 像素制表。 | `Print`, `PrintC`, `PrintButtonC`, `PrintHtml`, `PrintImg`, `PrintShape`, `PrintButton`, `PrintFlush`, `deleteLine`, `OutputLog`, `PopDisplayingLines` |
| `ConsoleDisplayLine.cs` | `ConsoleDisplayLine` | 一行显示内容，包含多个按钮/片段。 | `DrawTo`, `GDIDrawTo`, `ShiftPositionX`, `ChangeStr` |
| `ConsoleButtonString.cs` | `ConsoleButtonString` | 一个可点击/可输入的显示段，包含多个 display part。 | `DivideAt`, `CalcWidth`, `CalcPointX`, `DrawTo` |
| `AConsoleDisplayPart.cs` | `AConsoleDisplayPart`, `AConsoleColoredPart` | 显示片段抽象基类。 | `DrawTo`, `GDIDrawTo`, `ToString` |
| `ConsoleStyledString.cs` | `ConsoleStyledString`, `DisplayMode` | 有样式文本片段；按钮选中态可按 `setting.json` 的 `UseButtonFocusBackgroundColor` 绘制背景。 | `DrawTo`, 样式字段 |
| `ConsoleImagePart.cs` | `ConsoleImagePart` | 行内图片片段。 | `DrawTo`, 图片尺寸/偏移 |
| `ConsoleShapePart.cs` | `ConsoleShapePart`, `ConsoleRectangleShapePart`, `ConsoleSpacePart`, `ConsoleErrorShapePart` | 行内形状/空白/错误占位片段。 | `DrawTo`, shape 参数 |
| `ConsoleDivPart.cs` | `ConsoleDivPart`, `StyledBoxModel` | HTML div/盒模型片段。 | box 计算与绘制 |
| `ButtonStringCreator.cs` | `ButtonStringCreator`, `ButtonPrimitive` | 将文本拆成按钮/显示片段。 | `CreateButtonString` 相关 |
| `HtmlManager.cs` | `HtmlManager` 及 HTML state 类型 | HTML 文本和 display line 互转，支持 style/button/img/shape/div。 | `Html2DisplayLine`, `Html2ButtonList`, `DisplayLine2Html`, `HtmlTagSplit`, `Escape`, `Unescape` |
| `HotkeyState.cs` | `HotkeyState` | v24/snake `HOTKEY.ERB` 简易解释器和状态数组；支持 `HOTKEY_STATE_INIT`、`HOTKEY_STATE`、Ctrl+D 开关和硬件键盘热键转数值输入。 | `Initialize`, `Set`, `Toggle`, `TryEvaluate` |
| `PrintStringBuffer.cs` | `PrintStringBuffer` | 打印缓冲；把连续输出合并成 display line，并提供当前缓冲行像素宽度。 | `Append`, `Flush`, `CurrentLineWidth`, line 构造 |
| `StringMeasure.cs` | `StringMeasure : IDisposable` | 文本宽度测量。 | `GetDisplayLength`, `Dispose` |
| `StringStyle.cs` | `StringStyle` | 文本颜色、字体样式、font name。 | 构造、比较、转换 |
| `MixedNum.cs` | `MixedNum` | HTML 尺寸/位置混合数值。 | 数值字段/解析辅助 |

### Scripts/Emuera/Runtime

| 文件 | 主要类型 | 职责 | 关键入口/函数 |
|---|---|---|---|
| `Runtime/Utils/SqliteRuntime.cs` | `SqliteRuntime` | SQLite 初始化、连接路径、运行时可用性。 | `Initialize`, `OpenConnection`, `Shutdown` |
| `Runtime/Utils/SnakeSqlManager.cs` | `SnakeSqlManager`, `ReaderContext` | Snake profile SQL 兼容层。 | `Connect`, `ExecuteNonQuery`, `ExecuteReader`, `ReaderGet*`, `Disconnect` |
| `Runtime/Utils/PluginSystem/IPluginMethod.cs` | `IPluginMethod` | 插件方法接口。 | `Name`, `Description`, `Execute(PluginMethodParameter[] args)` |
| `Runtime/Utils/PluginSystem/PluginManager.cs` | `PluginManager`, `ReflectionPluginMethod` | 插件 manifest 加载、DLL 存在检测、方法注册和反射调用。 | `LoadPlugins`, `ExecuteMethod`, method registry |
| `Runtime/Utils/PluginSystem/PluginManifestAbstract.cs` | `PluginManifestAbstract` | 插件 manifest 抽象基类。 | manifest 字段/属性 |
| `Runtime/Utils/PluginSystem/PluginMethodParameter.cs` | `PluginMethodParameter`, `PluginMethodParameterBuilder` | 插件方法参数对象和 builder，支持整数、字符串和小数参数。 | `ConvertTerm`, 参数字段 |
| `Modern/Script/Functions/ModernSqlManager.cs` | `ModernSqlManager`, `ReaderContext` | 现代 SQL 扩展函数运行时。 | `Connect`, `ExecuteReader`, `ReaderGet*`, `Disconnect` |

### Scripts/Emuera/Sub

| 文件 | 主要类型 | 职责 | 关键入口/函数 |
|---|---|---|---|
| `EmueraException.cs` | `EmueraException`, `ExeEE`, `CodeEE`, `FileEE`, `ScriptPosition` | 核心异常层次和脚本位置。 | `ScriptPosition`, exception 构造 |
| `EraStreamReader.cs` | `EraStreamReader` | Era 文本读取封装。 | `ReadLine`, `Dispose` |
| `EraDataStream.cs` | `EraDataReader`, `EraDataWriter`, `EraDataState` | 文本存档/数据流读写；提供 `SparseArray<long>` / `SparseArray<string>` 读写重载以保持 1D 稀疏变量存档兼容。 | `Read`, `Write`, `Dispose` |
| `EraBinaryDataReader.cs` | `EraBinaryDataReader`, `EraSaveFileType`, `EraSaveDataType` | 二进制存档读取，含 1808 兼容 reader；支持把 1D 整数/字符串数据读入 `SparseArray<T>`。 | `Read`, `ReadInt64`, `ReadString`, `Dispose` |
| `EraBinaryDataWriter.cs` | `EraBinaryDataWriter` | 二进制存档写入；支持从 `SparseArray<T>` 输出 1D 整数/字符串数据。 | `Write`, `WriteInt64`, `WriteString`, `Dispose` |
| `LexicalAnalyzer.cs` | `LexicalAnalyzer` | ERB 词法分析，生成 `WordCollection`。 | `Analyse`, string/form string 解析 |
| `Word.cs` | `Word` 及各 token word | 词法 token 模型。 | `ToString`, token 字段 |
| `WordCollection.cs` | `WordCollection` | token 列表和当前位置操作。 | `Current`, `ShiftNext`, `GetWord`, `Clone` |
| `SubWord.cs` | `SubWord` 系列 | 格式字符串内嵌片段。 | 派生类型字段 |
| `StringStream.cs` | `StringStream` | 字符串流读取工具。 | `Current`, `ShiftNext`, `Substring`, `Seek` |
| `Preload.cs` | `Preload` | 预加载/路径相关辅助。 | `GetFiles`, preload helper |

### Scripts/Emuera/_Library

| 文件 | 主要类型 | 职责 | 关键入口/函数 |
|---|---|---|---|
| `_Library/GDI.cs` | `GDI`, `StockObjects`, `StretchMode` | GDI 兼容常量/函数桩。 | GDI helper |
| `_Library/LangManager.cs` | `LangManager` | Emuera 内部语言文本管理。 | `Load`, `GetStr` |
| `_Library/SFMT.cs` | `MTRandom` | SFMT/随机数实现；M0 trace 开启时记录 implicit/explicit seed、stream 与 `NextUInt32` 调用顺序和值。 | `Next`, `NextUInt32`, seed 初始化 |
| `_Library/Sys.cs` | `Sys` | 全局路径、exe dir 等系统信息。 | `ExeDir`, path 字段 |
| `_Library/WinInput.cs` | `WinInput`, `MouseButtons` | 鼠标/键盘输入兼容枚举和 helper；提供 `GETKEYTRIGGERED` latch 消费和清理。 | `GetKeyState`, `ConsumeKeyLatch`, `ClearLatches`, mouse helpers |
| `_Library/WinmmTimer.cs` | `WinmmTimer` | WinMM timer 兼容桩。 | 构造/字段 |

## 核心接口和抽象契约

### M1 Core 合同与 legacy 会话边界（部分接线，未迁移 Parser/VM）

| 路径 | 类型/入口 | 职责 | 边界 |
|---|---|---|---|
| `src/Core/GEmuera.Core.csproj` | `GEmuera.Core` | 标准 `Microsoft.NET.Sdk` 的纯 .NET 合同程序集；`net8.0` 供 Godot 4.7 host，`net10.0` 供本地合同验证 | 不引用 GodotSharp、legacy Parser/VM、Bridge 或 App |
| `src/Core/Compatibility/DialectModuleSnapshot.cs` | `DialectModuleSnapshot`, `ModuleDependencySnapshot`, `BehaviorPortSnapshot` | 不可变 module/dependency/typed port 快照；ordinal 唯一排序和引用完整性校验 | 仅合同数据，不加载 assembly、不执行 resolver |
| `src/Core/Compatibility/CompatibilityPlanSnapshot.cs` | `CompatibilityPlanSnapshot` | profile/module/port/capability/save profile 的稳定快照与 SHA-256 `PlanSemanticHash` | 不保存 policy value、handler、Parser/VM 或 Godot 对象 |
| `src/Core/Session/SessionCoordinator.cs` | `SessionCoordinator`, `SessionSwitchLease`, `SessionSelection` | generation/operationId、候选构造、取消、stale guard 与激活后的短 commit | 不直接引用 backend/Godot，不在 commit gate 内加载 |
| `src/Core/Session/LegacySessionFacade.cs` | `LegacySessionFacade`, `ILegacySessionBackend` | Core 计划构建与 legacy backend 的事务顺序、恢复、backend generation 投影 | 仍不证明 legacy static 或 Parser/VM 已按会话隔离 |
| `Scripts/GodotHost/LegacySessionLaunchRegistry.cs`, `LegacySessionBackend.cs` | immutable host route registry, `LegacySessionBackend` | opaque Core selection → legacy game root/profile 的 allowlisted 解析，以及 `GlobalStatic`/`EmueraThread` 生命周期 bridge | Godot/legacy 层；路径不进入 Core；plan 目前只作为启动元数据，未被 Parser/VM 消费 |
| `tools/core-contracts/` | `CoreContractSmoke.csproj`, `Test-CoreArchitecture.ps1` | 离线 smoke 和 Core→Godot 反向依赖文本守卫 | 当前不替代真实游戏多次切换、Android、M2 DTO 或完整 architecture tests |

| 契约 | 位置 | 说明 | 主要成员 |
|---|---|---|---|
| `IPluginMethod` | `Scripts/Emuera/Runtime/Utils/PluginSystem/IPluginMethod.cs` | 真正的 C# interface；插件方法统一入口。 | `Name`, `Description`, `Execute(PluginMethodParameter[] args)` |
| `IOperandTerm` | `GameData/Expression/IOperandTerm.cs` | 名字像接口，实际是 abstract class；所有表达式项的求值契约，类型主存储为 `EraType`，通过 `GetOperandType()` 兼容旧 CLR `Type` 调用。 | `GetIntValue`, `GetStrValue`, `GetFloatValue`, `GetValue`, `GetEraType`, `GetOperandType`, `Restructure` |
| `FunctionMethod` | `GameData/Function/FunctionMethod.cs` | 内置函数契约；所有 `*Method` 嵌套类继承它，默认参数检查按 `EraType` 比较表达式类型。 | `CheckArgumentType`, `GetIntValue`, `GetStrValue`, `GetReturnValue`, `UniqueRestructure` |
| `AbstractInstruction` | `GameProc/Function/Instruction.cs` | ERB 指令执行契约。 | `SetJumpTo`, `DoInstruction`, `CreateArgument`, `ArgBuilder` |
| `ArgumentBuilder` + `Argument` | `GameProc/Function/ArgumentBuilder.cs`, `Argument.cs` | 指令参数解析和参数对象契约；通用指令参数签名使用 `EraType[]`，不再依赖 CLR `Type` 做脚本语义比较。 | builder 构造 `Argument`；`Argument` 派生类保存解析结果 |
| `SparseArray<T>` | `GameData/Variable/SparseArray.cs` | 1D 稀疏变量存储契约；对外提供逻辑长度、默认值读取、已写入项枚举和少量数组操作，调用方不能再假设所有 1D 数组都是 CLR 数组。 | `Length`, indexer, `Entries`, `Shift`, `RemoveRange`, `Sort` |
| `VariableDescriptor` | `GameData/Variable/VariableDescriptor.cs` | 变量元数据契约；把 `VariableCode` 的类型、维度、保存和作用域位标志集中解释，供 identifier/token 复用。 | `VariableDescriptorTable.GetDescriptorByCode`, `VariableDescriptor.FromCode` |
| `VariableToken` | `GameData/Variable/VariableToken.cs` | 变量读写契约，覆盖标量/数组/角色/局部/引用/常量；基础元数据来自 `VariableDescriptor` 并公开 `EraType`，1D 数组访问需兼容 CLR 数组与 `SparseArray<T>`。 | `GetIntValue`, `GetStrValue`, `GetFloatValue`, `GetEraType`, `SetValue`, `SetValueAll`, `PlusValue` |
| `VariableTerm` | `GameData/Variable/VariableTerm.cs` | 表达式中的变量访问契约。 | `Get*Value`, `SetValue`, `Restructure` |
| `AContentFile` / `AbstractImage` / `ASprite` | `Content/*.cs` | 图片资源和精灵绘制契约。 | `Dispose`, `SpriteGetColor`, `GraphicsDraw` |
| `AConsoleDisplayPart` | `GameView/AConsoleDisplayPart.cs` | 一行显示中的最小渲染片段。 | `DrawTo`, `GDIDrawTo`, `ToString` |
| partial `EmueraConsole` | `GameView/EmueraConsole*.cs` | 控制台 facade；输入、输出、刷新、调试、图片层都集中在这里。 | `Print*`, `WaitInput`, `PressEnterKey`, `RefreshStrings`, `CBG_*` |
| partial `Process` | `GameProc/Process*.cs` | ERB 执行核心；初始化、循环、状态、lazy loading 分文件实现。 | `Initialize`, `DoScript`, `runScriptProc`, `TryLazyLoadErb` |

## 关键函数速查

### 启动/生命周期

| 任务 | 优先看 |
|---|---|
| 修改启动器扫描、游戏目录选择 | `FirstWindow._Ready`, `FirstWindow.ResolveStartupGamePath` |
| 修改主场景初始化 | `EmueraMain._Ready`, `EmueraMain.Run`, `EmueraMain.Restart` |
| 修改后台线程和输入唤醒 | `EmueraThread.Start`, `EmueraThread.Input`, `EmueraConsole.PressEnterKey` |
| 修改核心入口或 profile 选择 | `Program.Main`, `Program.DetectCoreProfile` |
| 修改退出/重启 | `EmueraMain._ExitTree`, `EmueraConsole.QuitAndRestart`, `GenericUtils.RestartGame` |

### UI/渲染/输入

| 任务 | 优先看 |
|---|---|
| 文本行添加/删除/刷新 | `GenericUtils.AddText`, `GenericUtils.ApplyTextChanges`, `EmueraContent.AddLine`, `EmueraContent.Canvas.AddCanvasLine`, `EmueraContent.UpdateDisplay` |
| 控制台打印语义 | `EmueraConsole.Print`, `PrintHtml`, `PrintButton`, `PrintFlush` |
| HTML 标签支持 | `HtmlManager.Html2DisplayLine`, `HtmlManager.tagAnalyze`, `ConsoleDivPart`；Canvas 后端相对 div overlay 见 `CanUseCanvasDivOverlay`、`UpdateCanvasDivOverlay`、`FlushCanvasOverlayRowsIfNeeded`、`RefreshCanvasOverlayVisibility`，逃逸 div 行索引用 `canvasRowsWithEscapedOverlays` |
| 行内图片/形状 | `ConsoleImagePart`, `ConsoleShapePart`, `EmueraContent.AddLine`, `ConsoleRenderSurface.DrawImagePart`；Canvas 后端复杂图片 overlay 见 `NeedsCanvasImageOverlay`、`UpdateCanvasImageOverlay`、`FlushCanvasOverlayRowsIfNeeded`、`RefreshCanvasOverlayVisibility`、`RefreshCanvasImageAnimations`，动画候选索引用 `canvasAnimatedImageOverlayKeys` |
| CBG/背景/图片层 | `EmueraConsole.CBG_*`, `SetImageLayer`, `EmueraContent.RefreshCBG` |
| 快捷按钮 | `QuickButtons.AddButton`, `QuickButtons.SetInputEnabled`, `EmueraContent.SubmitQuickButtonInput` |
| 屏幕输入面板 | `Inputpad.UpdateInputType`, `ShowPad`, `HidePad` |
| 缩放 | `Scalepad.SetScale`, `EmueraContent.SetContentScale`, `EmueraContent.NormalizeContentHorizontalScroll`, `ResolutionHelper.Apply` |
| Canvas 性能采样 | `ConsoleRenderSurface._Draw`, `ConsoleRenderSurface.RebuildHitRectsOnly`, `GenericUtils.SampleConsoleRenderFrame`；开启 `[debug.performance_sampling]` 后输出 `PERF.CONSOLE_RENDER`，包含 `draw_ms_avg/p95/max`、可视行、Canvas 行、overlay 行、part、hit rect 和节点规模快照；普通绘制窗口与点击前 hit-only 重建窗口分开统计，命中重建使用行级按钮矩形缓存与垂直桶索引，`PERF.*` 在 Godot/logcat 镜像中保留 `data` 字段 |

### 图片/精灵/ColorMatrix

| 任务 | 优先看 |
|---|---|
| 资源 CSV 到 sprite | `AppContents`, `SpriteManager.GetSprite`, `uEmuera.Utils.ResourcePrepare`；资源 CSV 路径解析缓存、lazy sprite 原始行索引、命中后实体化和慢实体化诊断在 `AppContents`；`ResourcePrepare` 对 CSV 只读头部字段，避免整行 `Split(',')` 分配。 |
| 纹理缓存/异步解码/主线程纹理上传 | `SpriteManager.TryGetTextureInfoCached`, `SpriteManager.RequestTextureInfoAsync`, `SpriteManager.UpdateOtherThreads`, `SpriteManager.TextureLoadVersion`, `TextureInfo.RecreateTexture` |
| Graphics surface 绘制 | `GraphicsImage.GCreate`, `GDrawCImg`, `GDrawG`, `GDrawString`, `GDrawLine`；显示提交前通过 `TryCreateDisplaySnapshot` 等待短暂稳定窗口并避开后台改图锁 |
| GPU ColorMatrix | `ColorMatrixGPU.GetSharedMaterial`, `ColorMatrixGPU.SetMatrixUniforms`, `GraphicsImage.ApplyColorMatrixGPU` |
| Godot 控件绘制图片 | `EmueraImage._Draw` |

### ERB 解析/执行

| 任务 | 优先看 |
|---|---|
| ERB 文件加载 | `ErbLoader.LoadErbFiles`, `ErbLoader.loadErbs` |
| lazy loading | `Process.TryLazyLoadErb`, `LoadLazyLoadingTable`, `SaveLazyLoadingList`；Android 索引缺失/失效优先走轻量标签扫描建表，非 Android 回到 BuildTable/full-load 建表；运行时用反向索引删除已加载文件映射，慢补加载诊断在 `Process.LazyLoading.cs` |
| 行解析 | `LogicalLineParser.ParseLine`, `ParseLabelLine`, `ParseSharpLine` |
| label 查找 | `LabelDictionary.GetEventLabels`, `GetNonEventLabel`, `GetLabelDollar` |
| 指令名映射 | `FunctionIdentifier.GetInstructionNameDic`, `FunctionIdentifier.IsPrint/IsInput/IsJump/IsMethod` |
| 指令执行 | `AbstractInstruction.DoInstruction`, `Instraction.Child.cs` 中对应 `*Instruction` |
| 脚本主循环 | `Process.DoScript`, `Process.ScriptProc.runScriptProc` |
| CALL/RETURN/BEGIN 状态 | `ProcessState.IntoFunction`, `Return`, `ReturnF`, `SetBegin`, `Begin` |

### 表达式/变量/函数

| 任务 | 优先看 |
|---|---|
| 表达式解析 | `ExpressionParser`, `ExpressionMediator`, `IOperandTerm` |
| 运算符 | `OperatorManager`, `OperatorMethodManager`, `SafeArithmetic` |
| 变量名解析 | `VariableIdentifier.GetVariableId`, `VariableParser` |
| 变量读写 | `VariableEvaluator`, `VariableToken`, `VariableTerm` |
| 新增普通内置函数 | `GameData/Function/Creator.Method.cs` + `FunctionMethodCreator.GetMethodList` |
| 新增 SQL/Map/XML/DT 函数 | 对应 `Creator.Method.Sql/Map/Xml/DT.cs` |
| 用户定义函数 | `UserDefinedFunctionData`, `UserDefinedMethodTerm`, `CalledFunction`, `ProcessState.IntoFunction` |

### 诊断/日志

| 任务 | 优先看 |
|---|---|
| 日志开关和限流 | `DiagnosticLogRouter` |
| 写日志 | `GenericUtils.Debug/Info/Warn/Error`, `DiagnosticLogSinks.Write` |
| 导出诊断包 | `GenericUtils.ExportDiagnosticPackage`, `DiagnosticLogExporter.ExportDiagnosticPackage` |
| 运行期配置 | `RuntimeDiagnosticsConfig`, `RuntimeDiagnosticsConfigLoader`, `RuntimeDiagnosticsConfigWriter` |
| 输入回放 | `InputReplayBuffer`, `GenericUtils.CaptureInputReplay` |
| 诊断浮窗 | `RuntimeDiagnosticsPanel.AttachFloatingTo` |
| 性能采样 | `GenericUtils.SamplePerformanceFrame`, `GenericUtils.SampleConsoleRenderFrame`；`PERF.SAMPLE` 记录 FPS/帧耗时/UI 队列/纹理队列，`PERF.CONSOLE_RENDER` 记录 Canvas 控制台可视绘制与命中表重建窗口数据；`PERF.*` 镜像输出会附带 `data` 便于直接 grep logcat |

## 常见修改定位

| 要改什么 | 从这里开始 |
|---|---|
| Android 游戏目录、屏幕宽度策略 | `FirstWindow`, `EmueraContent.GetSafeViewportRect`, `EmueraContent.ApplySafeAreaLayout`, `Program.ApplyAndroidWindowWidthPolicy`, `Config.UpdateWindowWidth` |
| 输入后脚本不继续 | `EmueraThread.Input`, `EmueraConsole.PressEnterKey`, `Process.InputInteger/InputString` |
| 文本没有刷新或顺序错 | `GenericUtils.FlushUI`, `EmueraContent.ApplyTextChanges`, `EmueraConsole.PopDisplayingLines` |
| 图片/立绘不显示 | `AppContents`, `SpriteManager`, `ConsoleImagePart`, `EmueraImage`, `GraphicsImage` |
| ColorMatrix 效果错误 | `ColorMatrixGPU`, `GraphicsImage.GDrawCImg`, shader `Scripts/Shaders/color_matrix*.gdshader` |
| HTML 显示错误 | `HtmlManager`, `ConsoleDivPart`, `ConsoleStyledString`, `PrintStringBuffer` |
| ERB 函数找不到 | `FunctionMethodCreator.GetMethodList`, `FunctionIdentifier`, `LabelDictionary`, lazy loading 表 |
| 变量读写错误 | `VariableIdentifier`, `VariableParser`, `VariableEvaluator`, `VariableToken` |
| 存档兼容 | `EraDataStream`, `EraBinaryDataReader`, `EraBinaryDataWriter`, `VariableData`, `VariableEvaluator`, `CharacterData`；1D 稀疏数组读写、RuntimeDataStore 的 VarExt 保存域读写也在这些入口 |
| SQL/Map/XML/DT 扩展 | `RuntimeDataStore`, `ConstantData` 的 VarExt 保存域声明读取、`SnakeSqlManager`, `ModernSqlManager`, `Creator.Method.*.cs` |
| 日志太多或没有日志 | `config.toml` 的 `[logging].enabled` 与模块开关、`RuntimeDiagnosticsConfig`, `DiagnosticLogRouter`, `GenericUtils.InitializeLogging` |
