# 上游、旧 gEmuera 与目标兼容矩阵

## 状态

- `Compatible`：源码符号 + baseline fixture + 目标结果 + 可重复差分全部通过。
- `Mapped`：源码事实和目标设计已明确，但目标实现/差分缺失。
- `LegacyMapped`：旧 gEmuera 源码和目标保留方案已定位，但尚未用归档运行 fixture 证明。
- `LegacyBehaviorPending`：旧代码存在，但其当前可观察行为/游戏依赖尚未稳定提取。
- `Partial`：部分子项通过，必须列出未覆盖集合。
- `IntentionalDifference`：有意安全/平台差异，写影响、开关和迁移。
- `Extension`：非原版能力，独立名称/入口。
- `Unsupported`：确认不实现，有替代。
- `Pending`：证据不足，不能猜测。

旧 gEmuera 有可运行 Godot 工程和大量兼容实现；当前已增加 Core 合同、最小 legacy bridge 与默认关闭 canary，但 typed Bridge/Parser/VM consumption 与目标运行报告仍缺。因此行为项继续记为 `LegacyMapped`/`LegacyBehaviorPending`，不能直接冒充目标 `Compatible`。

已落地的 [M1CoreRuntimeContractSlice](M1CoreRuntimeContractSlice.md) 证明 Core 合同、最小 `LegacySessionFacade` bridge、静态 profile→module 目录和默认 false canary 的代码路径；startup/parser 的 plan identity 已绑定并校验，但不改变本矩阵中 M1 真实隔离、Parser/VM descriptor/registry/policy 行为消费、D2 registry 或 Android 的证据状态。

## 矩阵 schema

每个行为生成一条机器可读记录：

```text
key, category, name, sourceLayer, profile, dialectModules, capabilityIds, planHash, sourceFile, sourceSymbol,
signature, preconditions, stateMutation, output, errors, timing,
targetOwner, completionMode, orderingPoint, failureReturn, threadOwner,
mergePolicy, barrierBefore, barrierAfter, fixtureIds, status, differenceReason
```

## 当前事实矩阵

| Key | 类别 | 行为/集合 | 源码符号 | 目标边界 | 状态 | 必需验证 |
| --- | --- | --- | --- | --- | --- | --- |
| BASELINE.LEGACY_TRACE | 验证 | clock/RNG/wait/input/display/effect/error 全局顺序 | `LegacyTraceRecorder`, `LegacyRunnerReportWriter` | legacy canonical report | Partial | input replay、upstream/v24、APK/真机与归档签署 |
| BASELINE.FIXTURE_MANIFEST | 验证 | upstream/legacy/game 的 profile、来源、授权、内容 hash 与期望报告 | `Fixtures/manifest.json`, `FixtureManifest.psm1` | fixture evidence catalog | Partial | eraFL 双后端报告已绑定；仍缺 upstream runner、未核验授权、artifact store 与签署 |
| BASELINE.LEGACY_DISPLAY | 验证 | Controls/Canvas 后端身份、帧后 PNG、真实 rect/value/generation hit probe 与 feature coverage | `LegacyDisplayObservation`, `Invoke-LegacyDisplayBaseline.ps1` | legacy display/hit/screenshot reports | Partial | eraFL 复杂页双后端已捕获 44 div/2 src/data-only/scroll 和各 45 个匹配 hit；plan-binding repeat3 中 Controls/Canvas 各 3 次 semantic report 一致、每次 screenshot/hit 均 Captured，但 transport trace hash 每次不同（`transportTraceReportsConsistent=false`）；仍缺 nested div/srcb/dynamic-map scope、稳定 transport、APK/真机与签署 |
| SAVE.HEADER.1808 | 存档 | header/version/data count | `EraBDConst` | Core SaveCodec | Mapped | byte offset fixtures |
| SAVE.TYPE.ALL | 存档 | Normal/Global/Var/CharVar | `EraSaveFileType`, VariableEvaluator save/load methods | SaveCodec/Service | Mapped | 四类型往返 |
| SAVE.DATA_TYPES | 存档 | Int/String 及 1D/2D/3D、EOC/EOF | `EraSaveDataType`, reader/writer | Core codec | Mapped | 每类型/损坏 marker |
| SAVE.LEGACY_BINARY_BASELINE | 存档/静态证据 | legacy header、file/data type、sparse marker、入口和 float code conflict 的 source hash inventory | M0-SAV-01：5 source、4 file type、19 data type、11 marker；`Float=0x20..0x23`，0x20–0x22 conflict；profile auto-select=`Unbound` | future SaveProfile fixture / codec / candidate-store input | Partial | 仅源码静态证据；无原始 `.sav/.dat`、offset map、round-trip、content binding、runtime resolver/codec registry/candidate commit 或 session isolation |
| SAVE.GZIP | 存档 | ZipHeader/GZip | CreateReader/Writer.Close | bounded codec | IntentionalDifference(Security) | 输出/比率/CRC |
| SAVE.LOAD_FLOW | 时序 | SYSTEM_LOADEND→EVENTLOAD | `Process.SystemProc` | VM system frames | Mapped | schedule diff |
| SAVE.PROFILE.FLOAT | 存档 | PcFloat 04–07 / legacy Float 20–23 | gEmuera `EraSaveDataType` | explicit GEmueraSnake codec | LegacyMapped | profile conflict/roundtrip |
| SAVE.PROFILE.SELECTION | 存档/迁移 | 无 sidecar/manifest 时只读预检、显式选择副本、dry-run、pin 撤销、备份转换 | 当前存档无可信内嵌 profile 元数据 + [SaveFormat](SaveFormat.md) 决策 | SaveProfile resolver + candidate Store + migration command | Pending | ambiguity/cancel/wrong-choice/pin-revoke/conversion/原件 hash 不变 |
| SAVE.VAREXT.DOMAIN | 存档 | Map/XML/DT SAVE/GLOBAL/STATIC | gEmuera `RuntimeDataStore` | candidate domain commit | LegacyMapped | per-domain fixtures |
| VAR.STANDARD_COUNTS | 变量 | 四维保存计数 | `VariableData.SaveToStream` | VariableStore | Mapped | boundary vars |
| VAR.TFLAG | 变量 | integer array 保存归属 | `VariableCode.TFLAG` | VariableStore | Mapped | save/reset diff |
| VAR.FLOAT | 变量 | FUNCTIONF/LOCALF/ARGF/RESULTF/REFF/arrays | gEmuera EraType/VariableCode/tokens | VmValue.Float/VariableStore | LegacyMapped | GE/SN float suite |
| FLOW.RESTART | 指令 | 当前函数重启 | `RESTART_Instruction` | VmFrame jump | Mapped | top/nested/event/wait |
| FLOW.PROCESS_RESTART | 指令 | quit/force + reboot | `Process.ScriptProc` | ApplicationEffect | Mapped | lifecycle diff |
| INPUT.TYPES | 输入 | InputType/InputRequest 字段 | `InputRequest.cs` | InputCoordinator | Mapped | per type |
| INPUT.TIMING | 输入 | submit/timeout/skip/resume | input instructions + Process | wait state | Pending | deterministic timing |
| INPUT.NF | 输入 | TINPUTNF/TINPUTSNF/TONEINPUTNF/TONEINPUTSNF | gEmuera FunctionIdentifier/InputRequest.NoFocus | shared WaitPort | LegacyMapped | focus/timing/default diff |
| INPUT.HOTKEY_MOUSE | 输入 | HOTKEY_STATE、VirtualCursor、MOUSEBUTTON 双通道 | gEmuera HotkeyState/VirtualCursor/EmueraContent | input adapter | LegacyMapped | SN/FL/Android trace |
| FUNC.UPCHECK | 指令 | PALAM += UP-DOWN，清零 | `UpdateInUpcheck` | Variable+Display effects | Mapped | target/negative/skipPrint |
| FUNC.CUPCHECK | 指令 | PALAM += CUP-CDOWN | `CUpdateInUpcheck` | Variable+Display effects | Mapped | invalid target/cleanup |
| RESOURCE.CSV | 资源 | resources CSV | `AppContents.LoadContents/CreateFromCsv` | ResourceCatalog | Mapped | order/duplicate/frame |
| RESOURCE.G | 图形 | GraphicsImage/G lifecycle | `GetGraphics`, GraphicsImage | session graphics | Mapped | create/draw/dispose |
| RESOURCE.G.PIXEL_ORDER | 图形 | G 操作同步读回/稳定 revision | gEmuera GraphicsImage/Creator methods | PixelStore CoreImmediate | LegacyMapped | pixel hash/return/order |
| RESOURCE.SPRITE | 图形 | SpriteF/Anime/dynamic | AppContents/CroppedImage | catalog/bridge | Mapped | transform/frame/dispose |
| CBG.CORE_SET | 内置函数 | clear/range/G/BMap/Sprite/button | `Creator.cs` dictionary + Creator methods | logical CBG CommitThenProject | Mapped | signature/return/error |
| CBG.SETSPRITE | 内置函数 | 公共名 `CBGSETSPRITE` | `Creator.cs` → `CBGSetCIMGMethod` | logical layer CommitThenProject | Mapped | signature/return/z/error fixture |
| AUDIO.VOLUME | 指令 | SETSOUNDVOLUME/SETBGMVOLUME | instruction classes | AudioEffect | Mapped | volume curve/bounds |
| HTML.GRAMMAR | HTML | tags/attrs/entities/comments/errors | `HtmlManager` | Core markup parser | Mapped | golden/fuzz |
| DISPLAY.DIV_V2 | 显示 | div box/children/depth/overflow | gEmuera ConsoleDivPart | DivPart tree | LegacyMapped | eraFL golden/layout |
| DISPLAY.IMAGE_DUAL | 显示 | img src/srcb/placement/cm | gEmuera ConsoleImagePart | ImagePart normal/selected | LegacyMapped | selected screenshot/hit |
| DISPLAY.TRANSACTION | 显示 | dynamic map、scroll intent、data-only | gEmuera Window/EmueraContent | DisplayTransaction | LegacyMapped | FL timeline/visual |
| DISPLAY.QUEUE_ORDER | 显示/时序 | transaction/REDRAW/WAIT/INPUT/data-only 背压与合并 | gEmuera Console/Window/GenericUtils/EmueraContent | bounded queue + explicit reducer/barrier | LegacyBehaviorPending | sequence、barrier、queue saturation、stale generation |
| ENCODING.DETECT | 编码 | strict UTF-8→Shift-JIS | `EncodingHelper` | EncodingService | Mapped | corpus diff |
| CONFIG.RESET | 配置 | defaults then current overrides | target design + XEmuera config facts待索引 | GameSession | Pending | A→B missing field |
| SESSION.LEGACY_ROOT_INVENTORY | 会话/迁移证据 | `GlobalStatic` 直接 root + `Program` mutable static 的声明、reset、引用与 owner 映射 | M0-SES-01：14+17 项、14 个中心 reset site（12 baseline + canary-only `ctrlZ` 与 debug `StackList`）、479 直接引用/56 文件 | LegacySessionFacade M1 输入 | Partial | 仅中心 root scope；`StackList` 只在 `UEMUERA_DEBUG` canary 有清理证据；仍需其余 static、runtime reset order、generation guard、A→B→A 和 rollback |
| CONFIG.SNAKE_JSON | 配置 | setting.json compatibility fields | gEmuera JSONConfig/Data | CompatibilityProfile | LegacyMapped | missing/default/profile |
| DIALECT.SESSION_PLAN | 方言/静态证据 | 每会话冻结模块/策略/codec/capability 的未来边界 | M0-DIA-10 source-only preflight：`v24pure`=`V24Pure`/v24 290/358、`snake`=`Snake`/snake 326/360；`SnakeModernMobile=Uncovered`；M1 Core 已把可信静态 profile 根构造成 plan/facade 候选，startup/parser 已 fail-closed 消费窄 `CompatibilityPlan` descriptor presence/ownership guard，验证 plan identity/profile/hash 与 legacy descriptor presence/module ownership；DIA-02 behavior runtime 仍 Failed、M0-SES-01 M1 仍 Blocked | M1 `CompatibilityPlan` | Partial | 最小 runtime plan/facade/canary 与 descriptor-consumption boundary 存在；该 guard 不替换 legacy handler 或执行 typed policy，且没有 content resolver、D2 frozen registry、真实隔离或 Android A/B 证据 |
| DIALECT.REGISTRY_ISOLATION | 方言 | v24/Snake 注册集合隔离，无 last-wins | M0-DIA-02 测试投影 v24=290/358、Snake=326/360、diff=36/2；当前 runtime 两组仍无条件调用 | frozen registries | Partial | 测试 projection invariant 通过；runtime isolation Failed；仍需 D2 switch + v24/Snake behavior snapshots |
| DIALECT.DESCRIPTOR_SCHEMA | 方言 | 每个公开键固定已求值 signature、flags、completion/effect 与 provenance | DIA-03 source + DIA-04/05/06：326/326 instruction args/effective flags；360/360 function argument+return `CompleteStatic` | frozen Instruction/Function descriptors | Partial | 类型与 flags 已逐 key 静态求值；defaults/errors/restructure/completion/effect 行为 fixture 全 Uncovered，且尚未进入 plan hash |
| DIALECT.OWNERSHIP_EVIDENCE | 方言/证据 | 公开键的上游同名、当前模块候选、未决与冲突证据 | M0-DIA-07：326/360 全覆盖；上游同名 284/243、显式当前候选 28、未决 131、target/upstream candidate 冲突 8 | ownership/replacement resolution input | Partial | 同名不是行为/ownership 证明；686 comparer/alias/replacement 未决，全部 behavior/completion/effect Uncovered；runtime isolation Failed，未进入 D1/D2 |
| DIALECT.NAME_LOOKUP_CONTRACT | 方言/证据 | 旧 instruction/function comparer、normalizer、跨表投影、碰撞优先与 `_Rename` stage | M0-DIA-08：326/360、9 collision、351 projection、677 instruction lookup surface；684+2 key domain；686 lookup contract resolved | D2 comparer/alias/replacement design input | Partial | `ICVariable` comparer 静态捕获；`ICFunction` current-culture `ToUpper` 未批准为未来策略；`_Rename` 是 SourceTextRewrite；686 semantic alias/replacement Unresolved，runtime Failed。静态报告的 `parserVmConsumption=NotConsumed` 只表示报告本身未接线到 legacy Parser/VM 行为；独立 startup/parser guard 只验证 plan identity/profile/hash 与 legacy descriptor presence/module ownership，不替换 handler 或执行 typed policy |
| DIALECT.MODULE_VISIBILITY | 方言/证据 | DIA-07 ownership 未决 key 在 v24/Snake 测试投影的成员关系 | M0-DIA-09：131 项=123 v24-visible candidate + 8 Snake-only candidate（instruction 8/6、expression 115/2），0 missing Snake projection | D1/D2 ownership/registry 裁决输入 | Partial | membership 不是 module owner、AliasOf、ReplacementDeclaration 或行为兼容；旧贡献来源仍可交叉，ownership 未决，runtime Failed。静态报告的 `parserVmConsumption=NotConsumed` 只表示报告本身未接线到 legacy Parser/VM 行为；独立 startup/parser guard 只验证 plan identity/profile/hash 与 legacy descriptor presence/module ownership，不替换 handler 或执行 typed policy |
| DIALECT.TYPED_POLICIES | 方言 | CALL 多参、启动错误、private arg、资源和刷新差异 | M0-DIA owner/BehaviorKey 分类；13 个跨贡献 Snake handler provenance warning；DIA-09 仅提供 123/8 可见性候选 | typed BehaviorPolicy | LegacyBehaviorPending | 131 项仍需 alias/replacement/BehaviorKey 裁决 + GE/SN two-sided behavior fixtures |
| DIALECT.RESOLUTION | 方言/静态证据 | profile id、marker、优先级、pin/fingerprint/manifest/probe/fallback 的未来边界 | M0-DIA-11 固定 `DetectCoreProfile`：empty→v24、launcher snake→Snake、modern marker→SnakeModernMobile、legacy marker→Snake、default→v24；launcher/runner 仅 v24pure/snake | runtime candidate resolver | Partial | 仅旧源码选择事实；SnakeModernMobile 仍 Uncovered/Unsupported，缺 manifest/fingerprint/ambiguity/forgery/capability/stale generation 与运行时 resolver |
| DIALECT.COMPATIBILITY_PACK_DECLARATION | 方言/静态证据 | 版本化 CompatibilityPack 的内置 module、显式 capability、来源 hash 与不可执行载荷边界 | M0-DIA-12 钉扎 DIA-10/11：仅 `v24pure`→`gemuera.v24`、`snake`→`gemuera.v24+game.snake`；`legacy.current.*` 仅为 projection support；capability=[]、content=`NotBound`、save/fixture=`Uncovered` | future manifest parser/candidate builder input | Partial | 仅离线 declaration catalog；拒绝 unknown module、SnakeModernMobile、DLL/type/script/path/URL 与缺失 capability 字段；没有 content fingerprint、game manifest、resolver、runtime plan、D2 registry 或 Parser/VM consumption |
| DIALECT.DECLARATION_VOCABULARY | 方言/静态证据 | `BehaviorKey`/`CapabilityId` 的稳定命名、来源 classification、target module 与 fixture 追溯 | M0-DIA-13 固定当前 DIA-01 的 97 branch hit→10 BehaviorKey+4 CapabilityId，并钉扎 DIA-12 两个 pack 的空 capability exposure；每项均为 `StaticCandidate`/`NotEligible` | future schema review/policy and capability declaration input | Partial | 不是 policy 默认值、runtime capability 或 manifest allowlist；不证明 errors/completion/effects/behavior，且没有 policy manager、resolver、plan、D2 registry 或 Parser/VM consumption |
| DIALECT.POLICY_CONSUMER_BOUNDARY | 方言/静态证据 | 每个 `BehaviorKey` 的唯一 future consumer contract/decision owner，以及配置输入和真正决策消费者的分离 | M0-DIA-14 钉扎 DIA-01/13：10 boundary、14 classification binding、16 source-file binding；全部 `BoundaryDraftOnly`/`NotImplemented`，DIA-13 exposure=[]；scoped variable config=`ConfigurationInput`、registration guard=`DecisionConsumer` | future narrow policy/catalog consumer design input | Partial | 只证明静态 ownership draft，不证明 policy value/default/errors/completion/effects/behavior；没有 C# policy manager、resolver、runtime plan、D2 registry、Parser/VM consumption 或会话隔离 |
| DIALECT.POLICY_SURFACE | 方言/静态证据 | future typed policy、Bridge 与 frozen catalog 的 port name/contract family 分配 | M0-DIA-15 钉扎 DIA-14/13：10 port type，6 `PolicyDecision`、2 `BridgeProjection`、2 `FrozenCatalogContribution`；全部 `InterfaceDraftOnly`/`Unspecified`/`NotImplemented` | D3 typed policy / Bridge / catalog implementation input | Partial | 只固定名称和唯一 owner，未定义 C# interface、method/DTO/default/errors/completion/effects/timing/behavior；没有 manager/resolver/plan/D2/Parser/VM 或 session isolation |
| DIALECT.BEHAVIOR_FIXTURE_CONTRACTS | 方言/静态证据 | future port 实施前必须具备的双侧行为 fixture、证据情形与 trace facet | M0-DIA-17 钉扎 DIA-13/15：10 个 source fixture ID 均要求 `v24pure`+`snake`、baseline/extension/undeclared 与 input/result/error/completion/effect；均为 `Planned`/`Uncovered`/`BlockedByFixture` | future fixture kit / D3 API review input | Partial | 不含 expected policy value、C# interface/method/DTO 或任何 runtime 载荷；0 行为已覆盖，不能作为 policy manager/resolver/plan/D2/Parser/VM 或多版本分发完成依据 |
| DIALECT.MODULE_COMPOSITION | 方言/静态证据 | 内置模块 descriptor、依赖 DAG、profile closure 与 future port ownership 的组合 | M0-DIA-16 钉扎 DIA-12/15：`gemuera.v24@1.0.0` 无依赖/port；`game.snake@1.0.0`→`gemuera.v24 [1.0.0,2.0.0)` 并声明 10 port；2 module/1 edge/2 closure，dependency-first order | future manifest/candidate builder、D3 API review 与 module catalog implementation input | Partial | 只证明离线静态组合；没有 module assembly、version-range runtime resolution、content/save/fixture binding、runtime catalog/resolver/plan/D2/Parser/VM 或 session isolation |
| DATA.SQL | 扩展 | SQLite functions/readers/import-export | gEmuera Creator.Method.Sql/runtime | IDatabasePort | LegacyMapped | desktop/Android/sql trace |
| PLUGIN.DLL | 扩展/安全 | in-process reflection DLL | gEmuera PluginManager | disabled; trusted desktop capability | IntentionalDifference(Security) | deny/authorize/hash/fault |
| SECURITY.COMPAT_MODE | 安全/迁移 | Safe/LegacyCompatibility/TrustedDesktopPlugin 用户模式 | legacy warning/limit behavior + target policy | per-session CompatibilityPlan | IntentionalDifference(Security) | prompt/pin/hash change/budget/revoke |
| PLATFORM.ANDROID.STORAGE_MIGRATION | 平台 | 旧外部存储路径到 SAF 的并行迁移 | gEmuera FirstWindow/path/input baseline | flagged legacy path + SAF candidate import | Pending | APK/device import/export/revoke/recovery/rollback |
| ERROR.CLASSIFICATION | 错误 | script/resource/save/runtime | CodeEE/FileEE call sites | typed faults | Pending | error diff |
| GODOT.API | 平台 | C#/Variant/Draw/Signal | locked binding missing | ApiSmoke | Pending | build |

## M3-M7 规划输入

这些条目只登记后续阶段的目标边界和证据缺口；它们不能把阶段设计文档或 M1 Core 合同切片提升为运行时兼容结论。

| Key | 类别 | 行为/集合 | 目标边界 | 状态 | 必需验证 |
| --- | --- | --- | --- | --- | --- |
| TEST.FAST_FEEDBACK | 工程验证 | AI 局部编辑的任务包、定向测试与升级门 | Explore/FastLoop/WorkPackage/PhaseRelease 分层 | Pending | 定向 Core/场景测试、任务包命令、升级条件、全量门不降级、反馈指标 |
| M3M7.EXECUTION.PROTOCOL | 迁移治理 | 后续阶段 work package、状态、identity、报告与回退 | versioned package record + gate decision | Pending | 前置 gate、命令 exit code、identity/hash、差分、lifecycle、rollback、uncovered、approval |
| M3.CORE.EXTRACTION | 迁移 | parser/variables/save/extension 逐批抽取 | pure Core + legacy façade + frozen plan consumer | Pending | Core build、无 Godot 引用、旧/新双跑、回退 |
| M4.RESOURCE.PIXELSTORE | 资源/图形 | G/CBG/Sprite/CPU pixel truth/revision | Session PixelStore + ResourceBridge projection | Pending | pixel/return/order、erafl dynamic map、Node/RID/Texture ledger |
| M5.PLATFORM.TYPED_PORTS | 平台/扩展 | input/VirtualCursor/SQLite/file/audio/lifecycle | typed ports + platform adapters | Pending | desktop/Android/iOS trace、cancel、handle cleanup、APK/device |
| M5.PLATFORM.SAF_CANARY | 平台/安全 | content URI import/cache/revoke/recovery | generation candidate import + legacy fallback | Pending | SAF 真机、低空间/后台/进程终止、rollback |
| M6.EXPERIMENT.SCHEDULER | 调度 | cooperative Step/Resume 与专用 VM thread 双跑 | experimental flag only | Pending | 100% yield audit、ordering/effect diff、p99/RSS |
| M6.EXPERIMENT.RENDERER | 渲染 | candidate Control/Canvas/self-draw backend | complete Display DTO + PixelStore projection | Pending | visual/hit/accessibility/scroll/data-only、独立回退 |
| M7.RELEASE.CLEANUP | 发布/治理 | old path zero-traffic removal | previous stable artifact + rollback assets | Pending | 两发布周期、inventory、lifecycle/memory、approval |
| ERafl.CAPABILITY.COMPOSITION | 方言/兼容 | dynamic-map/div/srcb/input/sprite/markup/VarExt/SQL 组合 | CompatibilityPack + CapabilityId + typed policy | LegacyBehaviorPending | v24/Snake/FL 双侧 fixture；其他魔改组合保持 Uncovered |

## 全集生成

上游与旧 gEmuera 名称集合、哈希和 40/110 项差集见 [InstructionInventory](InstructionInventory.md)。生成器输出 upstream/legacy/target 三集合；baseline runner 分别记录上游与旧工程，实际游戏 runner 增加 SN/FL/AN profile。CI 同时检查集合差集、completionMode 和 fixture/report。

## 兼容结论规则

文档中不得写“支持全部/完整兼容”。任何具体支持声明必须引用 key。安全上限改变原版行为时用 `IntentionalDifference(Security)`，不能混在 Compatible。KnownLimitations 从 Failed/Pending/Unsupported/IntentionalDifference 生成。

## 报告维度

差分不仅比较最终值，还比较 state mutation、DisplayLine/Part、error code/source、effect sequence、等待/恢复顺序。平台视觉/字体差异另有 golden screenshot，不替代 Core model diff。
2026-07-15 bridge increment: `LegacySessionBackend` binds the committed `CompatibilityPlan` before legacy startup and `ParserMediator` captures the same profile/hash during parser initialization. This is an identity/profile guard only; legacy descriptor tables remain the behavior owner until two-sided fixtures and runtime registry evidence exist.
M0-SAV-01 now has a real read-only candidate inventory from `E:\MyCode\Era` (10 files / 7 ordinary 1808 / 0 gzip / 3 unrecognized text; fixture set hash `8801c74f...72d09`). This is header-only evidence and intentionally leaves offset maps beyond the header, profile binding, codec reads/writes and round-trip fixtures uncovered.
M1 cross-ABA evidence was refreshed after plan binding (`100` switches / `101` sessions, semantic `fed09c96...e98cf`, zero fixture mutations). It remains runner-only observation evidence; the roughly 667 MB working-set peak and missing input/late-completion/static-root/device reports keep the matrix partial.
The generated DIA reports, rather than earlier narrative snapshots, are authoritative after the 2026-07-15 source identity refresh. The refresh preserves `Partial`/`Blocked` status and does not imply ownership, behavior, D2 registry or Parser/VM compatibility.
M0-DSP-01 repeat3 artifact is `C:\Users\Han\AppData\Local\Temp\gemuera-m0-display-repeat3-plan-binding-eadb73ce785c4a0494ea98a9cce793f1`; its runner metadata is `compatibility=None: default-off observation only`, so the filename is not plan-consumption evidence. It demonstrates deterministic semantic projections and captured screenshots/hit probes for each legacy backend, but not deterministic transport batching or Controls↔Canvas equivalence; the display row therefore remains Partial and the M0 gate remains blocked.
