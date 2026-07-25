# 上游 XEmuera、旧 gEmuera 与游戏证据索引

本索引是 FrameworkDesign 中兼容性事实的统一入口。引用格式固定为“仓库相对路径 + 类型/方法/常量”；行号只用于临时审查，不进入兼容契约。源码发生变化时，先更新本索引和差分 fixture，再修改设计结论。

## 证据优先级

1. 上游 XEmuera：原版语义与格式。
2. 旧可运行 gEmuera：Godot/Snake/eraFL/Android 扩展和修复，身份见 [GEmueraBaseline](GEmueraBaseline.md)。
3. 实际游戏 fixture：UP/GE/SN/FL/AN 输入、状态、截图、存档和设备报告。
4. 锁定 Godot/.NET/平台官方能力与构建证据。
5. FrameworkDesign 的设计推导。

每条矩阵记录 `sourceLayer/profile/hash`。上游和旧项目冲突时保留两列，不用一个“当前源码”覆盖另一层；目标 profile 明确选择。设计与任何目标证据冲突时降级为 Pending/Extension/Unsupported。

## 旧 gEmuera 权威入口

| 领域 | 路径/符号 | 已确认资产 |
| --- | --- | --- |
| 工程/线程 | `project.godot`, `gemuera-c#.csproj`, `Scripts/EmueraThread.cs` | Godot 4.7 C# legacy 工程；专用解释器线程 |
| float | `VariableCode`, `EraType`, `LogicalLineParser`, `SparseArray<double>` | FUNCTIONF、LOCALF/ARGF/RESULTF、REFF、float arrays/save |
| 扩展数据 | `RuntimeDataStore`, `Creator.Method.{Map,Xml,DT,Sql}.cs` | VarExt SAVE/GLOBAL/STATIC、SQLite |
| HTML | `ConsoleDivPart`, `ConsoleImagePart` | 完整 div box/children；src/srcb/position/display/cm |
| 显示事务 | `EmueraContent.ApplyTextChanges`, `uEmuera.Window.Update` | data-only、scroll intent、dynamic-map atomic refresh |
| 输入 | NF instruction registrations、`HotkeyState`, `VirtualCursor` | NoFocus、HOTKEY、Android L/R/M 与 MOUSEBUTTON 双通道 |
| 配置 | `JSONConfig/Data` | Snake/v24 setting.json profile |
| 插件 | `PluginManager` | 默认 AssemblyLoadContext 反射 DLL，属于完全信任代码 |

## M0 本地运行证据（尚未归档/签署）

| Work package | 可版本化入口 | 2026-07-12 本地结果 | 仍缺证据 |
| --- | --- | --- | --- |
| M0-ID-01 | `tools/baseline-identity` | 无 Git 时 tree manifest 可唯一标识源码、游戏、Godot 与 DLL | CI/artifact store 归档与签署 |
| M0-RUN-01 | `tools/legacy-runner/Invoke-LegacyRunner.ps1` | Godot `4.7.stable.mono.official.5b4e0cb0f` + eraFL snake 首等待隔离 RepeatCount=3 均 exit 0；固定 seed=`20260712`，semantic report SHA-256=`1790a2f2...207fe` 一致。raw trace 分别含 1136/945/938 个显式 `ui_projection` 批次，`transportTraceReportsConsistent=false` 保留为诊断，不影响脚本语义比较 | upstream/v24、APK/真机、复杂页当前 runner repeat、三层 fixture、artifact store 与签署 |
| M0-TRC-01 | `Scripts/M0/LegacyTrace*.cs`、`tools/legacy-runner/legacy-trace.schema.json` | 19 个 typed event，`sequence=1..19`、0 overflow；raw/canonical 数量与顺序一致，覆盖 clock/RNG/wait/display/audio/error/runner | 有输入 replay、更多 effect/异常 fixture、归档报告与人工签署 |
| M0-FIX-01 | `Fixtures/manifest.json`、`tools/fixture-manifest` | eraFL 本地 3,280 files/83,472,218 bytes，canonical SHA-256 `8b419464...d9a56`；授权证据 SHA-256 `2b4e86fc...1f95b` 已核验；Controls/Canvas 11 份报告绑定后 FL fixture=`ReadyForReview`，受限许可仍禁止再分发 | upstream runner、其它未核验授权、artifact store 归档与签署 |
| M0-DSP-01 | `Scripts/M0/LegacyDisplayObservation.cs`、`tools/legacy-runner/Invoke-LegacyDisplayBaseline.ps1`、`fixtures/erafl-enter-game.json` | eraFL 18 步复杂 replay 已进入领内互动页；Controls=57/0、Canvas=2 fallback/55，两端各 45/45 hit，`div=44/src=2/data-only=25/26/scroll=2`，PNG=`d49b5b...b69a8`/`2cf5fe...e6d8ca`；单次双后端结果 Captured。2026-07-15 plan-binding repeat3 artifact `C:\Users\Han\AppData\Local\Temp\gemuera-m0-display-repeat3-plan-binding-eadb73ce785c4a0494ea98a9cce793f1`：Controls/Canvas 各 3 次 `semanticReportsConsistent=true`，每次 screenshot/hit 状态均为 `Captured`；三次 transport trace hash 均不同，`transportTraceReportsConsistent=false`。 | 已对 reached div/src 与既有 data-only/scroll 路径执行双后端 repeat3；nested div/srcb/dynamic-map 的 repeat3 仍缺；transport 稳定性、APK/真机、归档与签署仍缺 |
| M0-SAV-01 | `tools/save-baseline`、`generated/legacy-save-baseline.json` | 只读钉扎 5 个 legacy source hash：1808 ordinary/Zip header、4 file type、19 data type、11 sparse marker、四类读写入口和 2 个 normal load mutation；`Float=0x20..0x23` 与上游 `0x20..0x22` 冲突为 `StaticConflictObserved`、profile auto-select=`Unbound`；catalog `1bb6edaa...a3ad7`、set `0c4507f4...1ea58` | 没有原始 save、offset map、baseline/target read-write round-trip、content/profile binding、runtime SaveProfile resolver/codec registry/candidate store、M1 isolation、游戏或设备证据 |
| M0-DIA-01 | `tools/dialect-inventory`、`generated/dialect-inventory.json` | 当前源码身份 `a30d210f...ccdc2` 下 97 个 profile/setting 命中唯一分类；326 指令、360 表达式函数、9 跨表碰撞；重复键/未分类命中 fail-fast | 290/360 模块归属待裁决；缺参数/effect schema、D1 plan、D2 runtime frozen registry 与行为 fixture |
| M0-DIA-02 | `Invoke-DialectRegistrySnapshot.ps1`、`generated/dialect-registry-snapshots.json` | v24=290/358、Snake=326/360、差集=36/2；snapshot set SHA-256 `caad8abf...4e673`；available-but-unselected Snake 不改变 v24 hash | 仅测试 DTO invariant 通过；旧 runtime isolation 明确 Failed；缺正式 module ownership/comparer/signature/replacement、D1 与 D2 runtime switch/fixture |
| M0-DIA-03 | `DialectSignatureInventory.psm1`、`generated/dialect-signature-inventory.json` | instruction source/binding=326/326、args=214 Resolved+112 Conditional；function handler/args=360/360、return=358 Resolved+2 Conditional；31 input-wait candidates；hash `321dc3a2...03608e8` | 112 条条件指令已由 DIA-04 衔接；DIA-03 本身保持原始候选，completion/effect 全为静态候选且 fixture Uncovered |
| M0-DIA-04 | `instruction-signature-resolution.json`、`DialectSignatureResolution.psm1`、`generated/dialect-signature-resolution.json` | 19 条规则恰好解析 112 条 Conditional，连同 214 条 preserved 得到 326/326 指令参数、0 unresolved；catalog `c51c1f91...b7042`、resolution `4006b653...566b0`；stale/ambiguous/duplicate/out-of-candidate fail-fast | 两条条件函数已由 DIA-05 衔接；指令参数默认/错误、completion/effect、ownership/replacement/comparer、D1/D2 runtime 与两侧 fixture 仍缺 |
| M0-DIA-05 | `function-return-resolution.json`、`New-DialectFunctionSignatureResolutionReport`、`generated/dialect-function-signature-resolution.json` | `GETCONFIG/GETCONFIGS` 两条规则解析 Integer/String；358 preserved + 2 by-rule，360/360 argument+return `CompleteStatic`；catalog `c723ed23...f60a6`、resolution `c9ebab56...4ff` | `CompleteStatic` 仅静态类型；默认/错误/restructure/completion/effect、ownership/replacement/comparer、D1/D2 runtime 与两侧 fixture 仍缺 |
| M0-DIA-06 | `instruction-flag-resolution.json`、`DialectInstructionFlagResolution.psm1`、`generated/dialect-instruction-flag-resolution.json` | 17 known flags；67 direct + 136 single + 123 additive-by-rule=326、0 unresolved；排除局部/注释噪声；catalog `64375822...a9c39`、resolution `515641ed...0021a` | effective flags 仅静态构造结果；各 flag 的 Parser/VM 行为、completion/effect、ownership/replacement、D1/D2 runtime 与两侧 fixture 仍缺 |
| M0-DIA-07 | `dialect-ownership-evidence.json`、`DialectOwnershipEvidence.psm1`、`generated/dialect-ownership-evidence.json` | 锁定上游源码 hash/键数并覆盖 326/360：上游同名 284/243，显式当前模块候选 28，未决 131；8 项当前 target/上游候选冲突；catalog `efd15e50...c87c`、当前 evidence `a665945d...e749` | 同名仅为 provenance，不证明行为或最终 ownership；DIA-07 本身不裁决 comparer/alias/replacement，behavior/completion/effect 全 Uncovered；旧 runtime isolation Failed，D1/D2 未实现 |
| M0-DIA-08 | `dialect-name-lookup-contract.json`、`DialectNameLookupContract.psm1`、`generated/dialect-name-lookup-contract.json` | 326 指令、360 函数、9 碰撞；351 函数投影形成 677 项指令 lookup surface；686 lookup contract resolved，key domain=684 ASCII uppercase+2；catalog `d2980de3...e2f6`、当前 contract `169ca816...f65c` | `ICVariable` comparer 为静态捕获，`ICFunction` current-culture `ToUpper` 有文化风险，`_Rename` 只是 SourceTextRewrite；静态 DIA-08 报告本身仍 `parserVmConsumption=NotConsumed`，这只表示该报告未接线到 legacy Parser/VM 行为。独立 startup/parser 已消费窄 `CompatibilityPlan` descriptor presence/ownership guard，验证 plan identity/profile/hash 与 legacy descriptor presence/module ownership，但不替换 legacy handler 或执行 typed policy；686 semantic alias/replacement 与 behavior 仍未消费，D1/D2 未实现 |
| M0-DIA-09 | `dialect-module-visibility.json`、`DialectModuleVisibility.psm1`、`generated/dialect-module-visibility.json` | 串联 DIA-02/07/08 hash；131 ownership 未决项机械分为 123 `V24VisibleCandidate` 与 8 `SnakeOnlyCandidate`（instruction=8/6、expression=115/2），0 缺失 Snake projection；catalog `4fffa254...d9574e`、visibility `bc2b5470...bfdf` | projection membership 不是最终 owner/alias/replacement/行为结论；静态报告的 `parserVmConsumption=NotConsumed` 只表示报告本身未接线到 legacy Parser/VM 行为。独立 startup/parser 的窄 `CompatibilityPlan` descriptor presence/ownership guard 验证 plan identity/profile/hash 与 legacy descriptor presence/module ownership，但不替换 legacy handler 或执行 typed policy；所有 131 项 ownership/alias/replacement 未决、行为/完成/effect 未覆盖，runtime Failed、D1/D2 未实现 |
| M0-DIA-10 | `dialect-plan-preflight.json`、`DialectPlanPreflight.psm1`、`generated/dialect-plan-preflight.json` | 只读 DIA-02 + M0-SES-01：`v24pure` 映射 `V24Pure`/v24=290/358，`snake` 映射 `Snake`/snake=326/360；`SnakeModernMobile=Uncovered`；catalog `4e9b62ba...97d5d`、preflight `d5cc089f...256b`；语义 hash 不受 selection source/requested generation 影响 | 这是 source-only static preflight，不是 runtime `CompatibilityPlan`；descriptor registry presence guard 已在 legacy Parser 初始化边界消费，typed-policy/handler behavior 仍 `NotConsumed`，M1 identity hand-off 已另有证据但 M1 仍 Blocked；本工具不创建 runtime façade/flag/resolver/frozen registry |
| M0-DIA-11 | `dialect-profile-selection.json`、`DialectProfileSelection.psm1`、`generated/dialect-profile-selection.json` | 锁定 FirstWindow/Program/runner source hash：empty→V24Pure、launcher snake→Snake、modern marker→SnakeModernMobile、legacy marker→Snake、default→V24Pure；launcher fallback=v24pure；selection `0960994b...bd71d` | 这是 source-only selection evidence，不检查游戏目录；`SnakeModernMobile` marker-only 且 DIA-10 Uncovered/runner Unsupported；startup/parser 的 profile+hash identity hand-off 已实现，但内容 resolver、descriptor/registry/policy behavior、D2 与完整 M1 isolation 仍未实现 |
| M0-DIA-12 | `dialect-compatibility-pack.json`、`DialectCompatibilityPack.psm1`、`generated/dialect-compatibility-pack.json` | 钉扎 DIA-10/11 hash，固定 `emuera.compatibility-pack/v1` 的两份静态声明：`v24pure`=`gemuera.v24`、`snake`=`game.snake+gemuera.v24`；`legacy.current.*` 仅 projection support；显式空 capability、内容=`NotBound`、save/fixture=`Uncovered`、distribution=`Blocked`；set `e9ffd094...8c46` | 这是 schema/catalog 验证，不是游戏 manifest parser 或 resolver；未知模块、SnakeModernMobile、DLL/type/script/path/URL 和缺 capability 声明被拒绝；没有 fingerprint/pin/runtime plan/D2/Parser/VM/fixture 或设备证据 |
| M0-DIA-13 | `dialect-declaration-vocabulary.json`、`DialectDeclarationVocabulary.psm1`、`generated/dialect-declaration-vocabulary.json` | 当前 DIA-01 的 30 classification/97 branch hit 规范化为 10 `BehaviorKey` + 4 `CapabilityId`，每项固定 source classification、target module、fixture 与命中数；同时验证 DIA-12 两个 static pack 的 capability exposure 仍为空；当前报告 identity `766c3f84...98c4` | 所有项仅 `StaticCandidate`/`NotEligible`，不成为 runtime capability/manifest allowlist，也不证明 policy value、defaults/errors/completion/effect 或行为；没有 policy manager/resolver/plan/D2/Parser/VM/fixture/device 证据 |
| M0-DIA-14 | `dialect-policy-consumer-boundary.json`、`DialectPolicyConsumerBoundary.psm1`、`generated/dialect-policy-consumer-boundary.json` | 钉扎当前 DIA-01 `a30d210f...ccdc2` 与 DIA-13 `766c3f84...98c4`：10 个 BehaviorKey→10 个唯一 consumer contract/decision owner，14 classification binding、16 source-file binding；scoped config input 与 registration decision consumer 分离；当前报告 identity `beedf972...c6b` | 只读静态 ownership draft；`BoundaryDraftOnly`/`NotImplemented` 与空 capability exposure 必须保持。没有 policy interface/manager、resolver、runtime plan/D2/Parser/VM、游戏、APK 或设备证据 |
| M0-DIA-15 | `dialect-policy-surface.json`、`DialectPolicySurface.psm1`、`generated/dialect-policy-surface.json` | 钉扎 DIA-14 `beedf972...c6b` 与 DIA-13 `766c3f84...98c4`：10 个 future port type=6 `PolicyDecision`+2 `BridgeProjection`+2 `FrozenCatalogContribution`；所有 port `InterfaceDraftOnly`、policy value=`Unspecified`；set `2213bf38...e012` | 只消除命名/owner drift 并分配后续接口表面；没有 C# type、method/DTO/default、policy manager、resolver、runtime plan/D2/Parser/VM、游戏或设备证据 |
| M0-DIA-16 | `dialect-module-composition.json`、`DialectModuleComposition.psm1`、`generated/dialect-module-composition.json` | 钉扎 DIA-12 `e9ffd094...8c46` 与 DIA-15 `2213bf38...e012`：2 个 `StaticCandidate` module、1 条依赖、10 个 port declaration、2 个 profile closure；`gemuera.v24@1.0.0` 无依赖/port，`game.snake@1.0.0` 依赖前者并贡献全部 port；set `2b7361ad...ea75` | 只证明静态 descriptor DAG 与依赖优先投影；不加载 assembly、不执行 version range、不绑定内容/存档/fixture，也没有 runtime catalog/resolver/plan/D2/Parser/VM、游戏或设备证据 |
| M0-DIA-17 | `dialect-behavior-fixture-contract-catalog.json`、`DialectBehaviorFixtureContracts.psm1`、`generated/dialect-behavior-fixture-contracts.json` | 钉扎 DIA-13 `766c3f84...98c4` 与 DIA-15 `2213bf38...e012`：10 个 BehaviorKey→10 个 source fixture ID/port，均要求 `v24pure`+`snake`、baseline/extension/undeclared 与 input/result/error/completion/effect 记录；set `1fed13e5...73c4` | 仅固定 fixture 需求，0 行为证据；没有 policy value、C# interface/method/DTO、policy manager、resolver、runtime plan/D2/Parser/VM、游戏或设备证据 |
| M0-SES-01 | `tools/session-state-inventory`、`generated/legacy-session-state-inventory.json` | 只读 root-state inventory：`GlobalStatic=14`、`Program=17`、31 项唯一 catalog mapping；14 个中心 reset site 已观察（12 baseline + 2 canary-only：`ctrlZ`、`UEMUERA_DEBUG` 下 `StackList`），479 个直接引用来自 56 个源码文件；inventory `a7451ff2...6572b` | 仅 central root scope，未盘点其他 static cache/Godot/platform/diagnostic state；`StackList` 仅在 debug canary 路径有清理证据；不证明 runtime reset order、线程安全或 A→B→A；最小 façade/generation/plan runtime 与默认关闭 startup canary 已存在，但不改变 M1 Blocked |

## M1 本地 Core 合同与 legacy façade 证据（InProgress / Blocked）

本节记录 [M1CoreRuntimeContractSlice](M1CoreRuntimeContractSlice.md) 的 M1-CORE-01/02 合同、最小 `LegacySessionFacade` 接线、静态 profile 目录、descriptor-consumption boundary 与默认关闭 canary 证据。它不是 M1 放行证据：M0–M2 基线仍为 `InProgress / Blocked / PreviousGate:M0`，证明 startup/parser 的 plan identity hand-off 和 legacy registry descriptor guard，但不证明 handler replacement、typed-policy 行为、legacy static 隔离、真实 canary rollout 或 Android。

| Work package | 可版本化入口 | 2026-07-14/15 本地结果 | 仍缺证据 |
| --- | --- | --- | --- |
| M1-CORE-01/02 / experimental façade | `src/Core/GEmuera.Core.csproj`、`CompatibilityProfileCatalog.cs`、`src/Core/Session/LegacySessionFacade.cs`、`Scripts/GodotHost/LegacySessionLaunchRegistry.cs`、`LegacySessionBackend.cs`、`Scripts/EmueraMain.cs`、`tools/core-contracts/`、`tools/legacy-runner/Invoke-SessionIsolation*` | Core `net8.0;net10.0` build=0 warnings/0 errors；smoke 覆盖稳定 hash、模块/port、profile 目录、精确 version interpreter catalog、冻结 descriptor 注入、factory 变异后的 snapshot 保持、Host descriptor 不匹配拒绝、prepare/commit lease、A→B→A、后端启动失败/取消/stale rollback 与 generation 对齐，以及 `LegacyCompatibilityPlanConsumption` 的 descriptor registry 校验；architecture guard 通过；`gemuera-c#.sln` 与 Godot 4.7 solution build 通过。default-off canary 的独立进程 baseline→canary→baseline 及同一游戏 ABA 均已通过。最新跨根 Rikaichan `v24pure`→eraFL `snake`→Rikaichan `v24pure` runner-only cross-ABA 通过，SwitchCount=2、semantic=`78bc8e1a...52ad9`、A 指纹首尾一致、两份副本 mutation=0。最新 post-binding 100-switch/101-session artifact 的 semantic=`fed09c96...e98cf`、fixture mutation=0/0；`emuera.log` 的 runner-only artifact redirect 防止 Snake warning 污染备用副本。默认配置仍为 false。 | 这些均为首等待、无输入的 runner-only observation，不是完整 static reset；Snake working-set 约 642-666 MB 的稳定预算风险仍未裁决。descriptor 目前只做 registry presence/module ownership fail-closed，尚缺 handler replacement、typed-policy behavior、输入/音频/资源/保存竞态、晚到 completion、resolver/frozen registry runtime、Android/APK、M0 archive/signature 与 M1 gate approval。一次 eraTW Snake 尝试仍因资源目录读取异常超时，不能记为通过。 |

本地报告必须携带各自输出目录中的 `identity.json` / tree manifest；source tree identity 会随任何源码或本文档修订失效，不能在此写成长期常量。游戏、DLL、工具链与源码 hash 只定位对应的本地报告，未进入 artifact store 前不得标为 `Passed`。

## 存档与变量

| 行为 | 文件 | 符号 | 已确认事实 |
| --- | --- | --- | --- |
| 二进制头与版本 | `XEmuera/XEmuera/Emuera/Sub/EraBinaryDataReader.cs` | `EraBDConst.Header`, `ZipHeader`, `Version1808`, `DataCount` | 普通头 `0x0A1A0A0D41524589`；压缩头 `0x0A50495A41524589`；版本 1808。 |
| 文件类型 | 同上 | `EraSaveFileType` | `Normal=0`, `Global=1`, `Var=2`, `CharVar=3`。 |
| 记录类型 | 同上 | `EraSaveDataType` | Int/数组/字符串、`Separator=0xFD`、`EOC=0xFE`、`EOF=0xFF`；Map/XML/DataTable 属私家扩展。 |
| 稀疏整数编码 | `EraBinaryDataReader.cs`, `EraBinaryDataWriter.cs` | `Ebdb`, `m_ReadInt`, `m_WriteInt`, `writeData` | 0..0xCF 直接字节；D0/D1/D2 后跟 16/32/64 位；Zero/ZeroA1/ZeroA2 与 EoA1/EoA2/EoD 表示稀疏数组。 |
| 普通存档次序 | `Emuera/GameData/Variable/VariableEvaluator.cs` | `SaveToStreamBinary`, `LoadFromStreamBinary` | 文件类型、游戏码、脚本版本、说明、角色、变量、EOF；读入先校验游戏与版本。 |
| 变量范围 | `VariableCode.cs`, `VariableData.cs` | `__COUNT_SAVE_STRING__`, `__COUNT_SAVE_INTEGER__`, `__COUNT_SAVE_INTEGER_ARRAY__`, `__COUNT_SAVE_STRING_ARRAY__`, `SaveToStream` | 四个维度分别计数，不能混用。`TFLAG` 是整数一维数组。 |
| 角色数据 | `CharacterData.cs` | `SaveToStreamBinary`, `LoadFromStreamBinary` | 角色记录使用 EOC 结束。 |
| 读档后流程 | `Emuera/GameProc/Process.SystemProc.cs` | `beginDataLoaded`, `endSystemLoad`, `endEventLoad` | 变量恢复后调用 `SYSTEM_LOADEND`，再调用 `EVENTLOAD`。 |

## VM、指令与输入

| 行为 | 文件 | 符号 | 已确认事实 |
| --- | --- | --- | --- |
| RESTART | `Emuera/GameProc/Function/Instraction.Child.cs` | `RESTART_Instruction.DoInstruction` | `state.JumpTo(func.ParentLabelLine)`，不是返回标题或清空变量。 |
| 进程级重启 | `Emuera/GameProc/Process.ScriptProc.cs`, `FunctionIdentifier.cs` | `QUIT_AND_RESTART`, `FORCE_QUIT_AND_RESTART` | 设置 `Program.Reboot` 并请求 Quit/ForceQuit。 |
| 输入种类 | `Emuera/GameProc/InputRequest.cs` | `InputType`, `InputRequest` | Enter/AnyKey/Int/Str/Void/AnyValue/IntButton/StrButton/PrimitiveMouseKey；请求含递增 ID、默认值、时限与消息跳过标志。 |
| UPCHECK | `VariableEvaluator.cs`, `Process.ScriptProc.cs` | `UpdateInUpcheck` | 对 TARGET 的 PALAM 应用全局 UP-DOWN；正变化才处理；`skipPrint` 只抑制输出；最后清零 UP/DOWN。 |
| CUPCHECK | 同上 | `CUpdateInUpcheck` | 对显式角色的 PALAM 应用该角色 CUP-CDOWN；无效角色直接返回；最后清零 CUP/CDOWN。 |
| 指令注册 | `FunctionIdentifier.cs` | 构造函数中的 `addFunction` | 指令名、参数构造器与扩展标志的事实源。 |
| 内置函数 | `BuiltInFunctionCode.cs`, `Creator.Method.cs` | 枚举与 `FunctionMethod` 子类 | 函数全集、返回类型、参数和执行行为的事实源。 |

## 图形、CBG 与音频

| 行为 | 文件/符号 | 已确认事实 |
| --- | --- | --- |
| GraphicsImage/G | `Content/GraphicsImage.cs`, `AppContents.GetGraphics` | G 槽按数值 ID 懒创建。 |
| Sprite | `Content/CroppedImage.cs`, `AppContents.GetSprite/CreateSpriteG/CreateSpriteAnime/SpriteDispose` | 名称大写化；动态创建、动画与销毁均存在。 |
| CBG | `Emuera/GameData/Function/Creator.cs`: 字典初始化；`Creator.Method.cs`: `CBGClearMethod`, `CBGRemoveRangeMethod`, `CBGSetGraphicsMethod`, `CBGSetBMapGMethod`, `CBGSetCIMGMethod`, `CBGSETButtonSpriteMethod` | 公共名称为 `CBGSETG`、`CBGSETSPRITE`、`CBGCLEAR`、`CBGCLEARBUTTON`、`CBGREMOVERANGE`、`CBGREMOVEBMAP`、`CBGSETBMAPG`、`CBGSETBUTTONSPRITE`；`CBGSETSPRITE` 映射到实现类 `CBGSetCIMGMethod`。 |
| 音量 | `Instraction.Child.cs`: `SETSOUNDVOLUME_Instruction`, `SETBGMVOLUME_Instruction` | 读取整数表达式并设置 SFX/BGM player 音量。Godot dB 映射属于 Bridge 决策，需差分。 |

上游 `Creator.cs` 明确注册 `["CBGSETSPRITE"] = new CBGSetCIMGMethod()`；兼容层必须保留公共脚本名，不能从实现类名反推 API。旧 gEmuera 还显式注册 `CBGSETCIMG` 别名，它只属于对应 legacy profile。完整快照见 [InstructionInventory](InstructionInventory.md)。

## AppContents 资源 CSV

事实源是 `XEmuera/XEmuera/Emuera/Content/AppContents.cs`：

- `LoadContents` 在 `Program.ContentDir` 下递归枚举 CSV，以 `EncodingHelper.ReadAllLines` 读取，空行和 `;` 注释跳过。
- 每个 CSV 文件维护独立 `currentAnime`；`ANIME,w,h` 声明动画，后续同名行追加帧。
- 普通行格式为 `name,file[,x,y,w,h[,offsetX,offsetY[,delay]]]`；名称 `Trim().ToUpper()`，反斜杠转换为平台分隔符。
- 动画尺寸必须为正且不超过 `AbstractImage.MAX_IMAGESIZE=8192`；普通大图在当前源码只警告，目标安全模式必须把超限处理列为有意差异或兼容模式。
- 重复 Sprite 名警告并销毁后项，不是 basename 静默覆盖。
- 裁剪宽高必须为正且与父图相交；动画 delay 必须大于 0。
- `UnloadContents` 释放资源、Sprite 和 G；`UnloadGraphicList` 只释放动态 G。

## HTML 与显示

事实源是 `XEmuera/XEmuera/Emuera/GameView/HtmlManager.cs` 的 `Html2DisplayLine`、`tagAnalyze`、`Escape`、`Unescape`：

- 标签：`b`、`i`、`u`、`s`、`br`、`nobr`、`p align`、`font`、`button`、`nonbutton`、`clearbutton`、`div`、`img`、`shape`。
- 实体：`nbsp`、`amp`、`gt`、`lt`、`quot`、`apos`，以及 `&#decimal;`、`&#xhex;`；数值范围为 `0..0xFFFF`。
- 注释 `<!-- -->` 被专门解析；未闭合注释、标签、非法属性、重复样式和非法嵌套抛 `CodeEE`。
- 文本中的换行按 `<br>` 处理；`</nobr>` 与 `</p>` 可省略，但 button/font/style 必须闭合。
- `button`/`nonbutton` 不能嵌套；`div` 的闭合与内部递归行为必须由 golden fixture 覆盖，不通过简化规则猜测。

## 编码

`XEmuera/XEmuera/EncodingHelper.cs` 是唯一编码事实源：先识别 UTF-8 BOM，然后使用 `UTF8Encoding(false, true)` 完整严格解码；成功为 UTF-8，异常回退 `Encoding.GetEncoding("SHIFT-JIS")`。不存在打分检测。

## 证据维护门禁

每个 `Compatible` 结论至少同时具备：本索引条目、fixture manifest、XEmuera 实际输出、目标实现输出和可重复差分报告。仅有源码阅读时状态最多为 `Mapped`。

## M3-M7 证据准备状态

[M3M7EngineeringExecution](M3M7EngineeringExecution.md) 规定未来证据包应包含的 identity、命令、回退和签署字段，但不会把缺失运行时报告补成绿色结论。

M3-M7 设计文档是后续证据计划的入口，不是运行时证据。当前仍受 M0-M2 串行门禁约束，另一条并行 AI 的 M0-M2 source/report identity 变化会使后续报告失效并要求重跑。

| 阶段 | 设计入口 | 当前证据 | 缺失证据 |
| --- | --- | --- | --- |
| M3 | [M3CoreExtraction](M3CoreExtraction.md) | M1 Core 合同切片可作为接口输入，未接入旧 Parser/VM | pure Core 完整抽取、legacy façade、parser/variable/save/dialect 双跑与回退 |
| M4 | [M4ResourceGraphics](M4ResourceGraphics.md) | 旧 Graphics/Sprite/CBG 事实和 M0 显示基线 | PixelStore 运行时、像素/revision 差分、Node/RID/Texture/内存压力 |
| M5 | [M5PlatformComposition](M5PlatformComposition.md) | 旧输入、VirtualCursor、SQLite、Android 外部路径事实 | typed ports、SAF canary、真机权限/恢复/句柄/音频报告 |
| M6 | [M6SchedulingRenderingExperiment](M6SchedulingRenderingExperiment.md) | 专用 VM thread 设计和旧 Controls/Canvas 基线 | yield audit、cooperative 双跑、候选 renderer visual/hit/accessibility/p99 |
| M7 | [M7CleanupReleaseGovernance](M7CleanupReleaseGovernance.md) | 现有旧路径和回退资产清单 | 两发布周期零流量、删除 inventory、稳定 tag、回退演练 |

erafl 继续作为跨层第二基线；dynamic-map、div/srcb、data-only、VirtualCursor、dynamic Sprite、VarExt/SQL 和 Android 恢复不能用单一截图替代。未有双侧 fixture 的 capability/组合保持 `LegacyBehaviorPending`、`Uncovered` 或 `Blocked`。
2026-07-15 incremental evidence: `LegacySessionBackend` binds the committed immutable `CompatibilityPlan` before starting the legacy worker; `Program` locks profile/hash and `ParserMediator` captures the same plan identity during parser initialization, then clears it during canary reset. Core build, architecture guard, and the in-process session-cycle contract passed. This is a fail-closed, narrow descriptor presence/ownership boundary: it validates plan identity/profile/hash and legacy descriptor presence/module ownership, but does not replace legacy handlers or execute typed policy; M1 remains `InProgress / Blocked / PreviousGate:M0`.
M1-IDENTITY-01 bounded completion (2026-07-15): the in-process contract now asserts bind-before-launch/start, failed-start cleanup, profile/hash mismatch rejection, and rejection of null parser plans while a startup plan is bound. This sub-scope is `CompleteForScope / ReadyForReview`; parent M1 remains blocked.
M0-SES-CTRLZ-01 bounded completion (2026-07-15): `GlobalStatic.ResetCanarySessionState()` has a focused contract and inventory site classification proving `CtrlZ.ResetSessionState()` clears undo inputs, save markers, random seed snapshot and in-progress flags. This remains a narrow canary slice; the subsequent `M0-SES-STACKLIST-01` slice observes `StackList.Clear()` under `UEMUERA_DEBUG`, yielding inventory artifact `a7451ff2...6572b` with all 14 central `GlobalStatic` entries classified as reset-observed. Full static isolation remains open.
M0-SAV-01 fixture increment (2026-07-15): `tools/save-baseline/Invoke-LegacySaveFixtureAudit.ps1` performed a shared-read audit of `E:\MyCode\Era` and wrote artifact `C:\Users\Han\AppData\Local\Temp\gemuera-m0-sav-fixture-era-6200b65286f145b7964612cf51fbacc5.json`. It found 10 candidate files, 7 ordinary 1808 headers, 0 gzip samples and 3 unrecognized text samples; fixture set hash `8801c74ff49531281a15bf31daec5a5810f0178f1ff0cbb4968ef412e2072d09`, offset map `HeaderOnly`, round-trip `Uncovered`, profile binding `Unbound`. No source writes, decompression or parser calls occurred; M0-SAV-01 remains blocked.
Post-plan-binding cross-ABA refresh (2026-07-15): `SwitchCount=100` / 101 sessions, semantic `fed09c96f164098a45aa31ca2aa858a15b1c22408fc8fff06a9488fcef6e98cf`, repeated v24/Snake fingerprints, and zero primary/alternate fixture mutations. Artifact `C:\Users\Han\AppData\Local\Temp\gemuera-m1-plan-binding-cross-stress100-d5dfae0c043a471eb29d367f3806d6c5`; working-set peak was about 667 MB, so the release memory risk remains open.
Historical DIA identity note (superseded by the ParserMediator hash-boundary change below): the prior plan-boundary snapshot used DIA-01 `ddcb9c42...d9e05` and downstream identities recorded before the current source update. All statuses remained `InProgress / Blocked`.
Current DIA identity after the ParserMediator descriptor-consumption source change and StackList canary reset: DIA-01 `a30d210f...ccdc2` / 97 branch hits; DIA-03 `d347bdd7...2fa9`; DIA-04 `2f9995f8...80e2`; DIA-05 `18f2e9f4...223e`; DIA-06 `16f8ecab...2717`; DIA-07 `a665945d...e749`; DIA-08 `169ca816...f65c`; DIA-09 `bc2b5470...bfdf`; DIA-10 `d5cc089f...256b`; DIA-11 `0960994b...bd71d`; DIA-12 `e9ffd094...8c46`; DIA-13 `766c3f84...98c4`; DIA-14 `beedf972...c6b`; DIA-15 `2213bf38...e012`; DIA-16 `2b7361ad...ea75`; DIA-17 `1fed13e5...73c4`. All remain `InProgress / Blocked`; no compatibility gate was advanced.

Post-route regeneration supersedes the preceding DIA-08/09 shorthand: current generated values are DIA-08 contract `e1f84151...98056` and DIA-09 visibility `755289bb...40ba29a`; earlier `169ca...`/`bc2b...` references in historical notes are retained only for provenance.
M0-DSP-01 repeat3 refresh (2026-07-15): artifact `C:\Users\Han\AppData\Local\Temp\gemuera-m0-display-repeat3-plan-binding-eadb73ce785c4a0494ea98a9cce793f1` ran eraFL Snake at 1280x720 with Controls and Canvas three times each under `compatibility=None: default-off observation only`; the directory name does not prove plan consumption. Every semantic report matched; all screenshots and hit probes were `Captured`; reached coverage remains `div/src` plus existing data-only/scroll, while nested `div/srcb` and dynamic-map paths remain `Uncovered`. Each backend's transport trace hashes differed across repeats, so `transportTraceReportsConsistent=false` and M0-DSP-01 stays `InProgress / Blocked / EvidenceMissing`.
Transport audit: the repeat drift is confined to `ui_projection` `apply_text_changes` events; Controls batches were 702/615/600 and Canvas 480/487/489, while all semantic traces shared hash `d12eda9bfc84f96d4754021052e52937c4fbd4c103ef739c4b5fa154defc9579`. `GenericUtils.FlushUI`'s frame/96-action/7ms batching and worker-to-main-thread resource/dictionary completion order make transport chunking timing-dependent. Raw events remain authoritative diagnostics; no normalization or event deletion is permitted. A deterministic transport scheduler or explicit fixture barrier is a future M0-DSP task.
M0-SAV-01 now has a bounded isolated-copy artifact in addition to the source-only baseline. `LegacySaveRoundTripEvidence.psm1` requires an explicit `Upstream1808` or `GEmueraSnake` pin, verifies source-before/source-after hashes, and records deterministic byte-preserving copy/copy-back hashes under an external isolated root. This evidence is `profileBindingStatus=BoundExplicit` and `copyRoundTripStatus=Passed` when all candidates copy cleanly, while `semanticRoundTripStatus=Uncovered` and the M0 gate remain blocked because no legacy codec/parser/state commit was executed.
Real Era sample artifact (2026-07-15): `sourceSaveFilesRead=10`, `copyRoundTripStatus=Passed`, `semanticRoundTripStatus=Uncovered`, explicit profile `Upstream1808`, fixture set hash `021112f35e5dc7a559afaf49b1810344433b3cf0bde652a272e3711da1f0af90`; three unknown-wire files remain uncovered. Report: `C:\Users\Han\AppData\Local\Temp\gemuera-m0-save-roundtrip-era2-5dc6eccffe424e3e9c2f9a4464df638b.json`. This is `Partial / Blocked`, not semantic save compatibility evidence.
