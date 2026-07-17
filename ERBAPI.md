# ERB 语法扩展与魔改安全指南

本文供修改 gEmuera ERB 语法、指令、内置函数、变量和脚本可见能力的 Agent 使用。目标不是让任何位置都可以“快速加一个关键字”，而是让每项扩展都有唯一 owner、明确 profile、完整行为证据和可回退路径，并且不改变未选择该扩展的会话。

文档状态：`Reviewed / Migration-aware / Not a stable public plugin API`。本文描述 2026-07-16 的真实代码边界；权威架构裁决仍以 [DialectExtensionSystem](NewFrameworkDesign/DialectExtensionSystem.md)、[CompatibilityMatrix](NewFrameworkDesign/CompatibilityMatrix.md) 和 [AIDevelopmentWorkflow](NewFrameworkDesign/AIDevelopmentWorkflow.md) 为准。`NewFrameworkDesign/generated/` 中的机器报告只有在 source identity 和 contract tests 通过时才优先于旧叙述数字；当前 DIA-01 门禁为红，见下表。

最重要的规则：**先证明未选择扩展的一侧不变，再实现选择扩展的一侧。**

## 当前审查结论

### 已确认的边界与风险

| 等级 | 审查结论 | 代码/证据 | 对 Agent 的约束 |
| --- | --- | --- | --- |
| 阻断 | 当前 dialect evidence 已失效。DIA-01/DIA-02 测试在 `PrototypeRuntimeNode.cs` 发现未分类的 `profile.selected-name` 命中 | `Scripts/GodotHost/PrototypeRuntimeNode.cs:44`；`Test-DialectInventory.ps1`、`Test-DialectRegistrySnapshot.ps1` 当前失败 | 先由 owner 裁决分类并按依赖链重生成 DIA 报告；不得直接沿用旧 hash 或自行猜 `targetModule` |
| 阻断 | legacy 指令与函数注册尚未按 profile 隔离。v24 和 Snake 注册函数仍在同一静态构造中无条件执行 | `Scripts/Emuera/GameProc/Function/FunctionIdentifier.cs:397`、`:457`；`generated/dialect-registry-snapshots.json` 为 `currentRuntimeIsolation=Failed` | 不能继续把新的 profile 专属 key 直接注册进 legacy 静态表并宣称它只对该 profile 可见 |
| 阻断 | Core `IDialectModule` 当前只贡献 descriptor，不提供 legacy handler；内置 v24/Snake module 的 contribution 仍为空 | `src/Core/Compatibility/DialectRuntime.cs:66`、`BuiltInDialectCatalog.cs:28` | `IDialectContribution` 不是当前可独立使用的“新增可执行语法”接口 |
| 阻断 | 非空 descriptor 路由是完整可见面，不是增量补丁。只加入一个 descriptor 会让未列出的旧指令/函数不可见；descriptor 对应 key 未先存在于 legacy 表则直接失败 | `CompatibilityDescriptorRoute.cs:23`、`IdentifierDictionary.cs:616` | 禁止向当前 built-in plan 单独塞一个新 descriptor；必须先有完整 profile registry 与两侧行为证据 |
| 高 | `IdentifierDictionary` 在 plan 收窄前用完整 legacy 表构建名称冲突表；将来即使隐藏某个 key，它仍可能被当作系统保留名 | `IdentifierDictionary.cs:139`、`:172`、`:191`、`:616` | D2 隔离不能只收窄 lookup，还必须验证 label/变量/macro 冲突语义 |
| 高 | legacy 注册表由静态可变 `Dictionary` 持有并直接返回；指令 comparer 还在首次静态初始化时捕获 `Config.ICVariable` | `FunctionIdentifier.cs:38`、`:63`；`Creator.cs:398` | 不得在启动后修改返回字典，不得把当前 comparer/初始化顺序提升为未来稳定 API |
| 高 | 外部 DLL 插件使用默认 `AssemblyLoadContext`、反射和进程内完全信任；方法注册为覆盖式赋值 | `PluginManager.cs:38`、`:54`、`:257` | `PluginManager`/`CALLSHARP` 不能作为移动端方言机制，也不能用于绕过 module、fixture 和冲突检查 |

最后一次成功生成的 DIA 基线为 326 个指令注册、360 个表达式函数注册；测试投影为 v24 `290/358`、Snake `326/360`，Snake 差集为 `36/2`。该报告生成于 2026-07-15，早于当前未分类的 prototype host 变更，因此这些数字只能作为上一份基线，不能写成本次 source identity 已通过。上一份测试投影的“未选择模块不变性”已通过，但旧 runtime 隔离仍失败；这两项也必须同时陈述，不能把静态报告写成 D2 已完成。

### 当前可安全使用到什么程度

- 可以在 legacy owner 中做小范围、带双侧 fixture 的兼容修复，并保留旧运行路径。
- 可以使用 Core module/catalog 合同验证依赖、重复 key、冻结和 plan hash，但它们目前不是完整 Parser/VM 语义实现。
- 可以增加 inventory、signature、ownership 和 lookup 静态证据，但静态扫描不能替代 parse/execute/error/completion/effect 行为测试。
- 在当前 DIA-01 未分类命中关闭前，不能把旧 generated hash 当作新 ERB 变更的可信基线。
- 不能宣称已有通用第三方 ERB 插件 API、完整 profile 隔离、任意方言动态加载或 Android DLL 扩展。

## What：允许扩展什么

按从低风险到高风险排序：

| 扩展种类 | 适用目标 | 首选形式 | 风险 |
| --- | --- | --- | --- |
| ERB/ERH 脚本层能力 | 能用用户函数、宏、用户变量或现有指令组合表达 | 游戏脚本或数据，不改解释器 | 低 |
| 表达式内置函数 | 从参数计算并返回 Integer/String/Float；通常不挂起 VM | `FunctionMethod` + `FunctionMethodCreator` 注册 | 中 |
| 语句指令 | 修改状态、控制流、输出、输入等待或产生外部 effect | `AbstractInstruction` + 参数 builder + 注册 | 中高 |
| `#` directive/label 语法 | 改变函数、局部变量或加载期元数据 | `LogicalLineParser`/逻辑行模型 | 高 |
| 词法、运算符和表达式 grammar | 现有函数/指令/directive 无法表达的新语言结构 | Lexer + parser + AST/term + diagnostics | 很高 |
| 脚本可见系统变量 | 新的 engine-owned 状态、类型、维度或 scope | variable descriptor/store/token 全链路 | 很高 |
| 存档格式或 VarExt 域 | 新 wire type、保存域或 codec profile | 显式 `SaveProfileId` 与候选读写 | 极高 |
| 平台/运行时能力 | SQL、文件、输入、音频、资源等脚本可观察服务 | typed Core port/capability + ERB facade | 高 |
| 外部 DLL | 用户明确授权的桌面完全信任代码 | 独立 trusted plugin 路径 | 极高且移动端不支持 |

以下不属于可接受的 ERB 扩展：

- 在 Godot Node、Control、Autoload 或 signal 中实现 ERB 语义。
- 依据游戏目录名、函数名或 `Program.IsSnakeProfile` 在 Parser/VM hot path 增加新分支。
- 让 CompatibilityPack 指定 assembly、C# 类型、脚本回调、任意路径或 URL。
- 用 `_Rename.csv` 文本替换冒充 registry alias/replacement。
- 用反射扫描程序集来发现移动端方言模块。

## Where：改动应落在哪里

ERB 可观察行为的调用方向必须保持：

```text
ERB source
  -> lexer/parser
  -> typed logical line / expression term
  -> VM instruction/function owner
  -> state mutation + typed effect/fault/completion
  -> Bridge port
  -> Godot projection
```

Godot 只能投影 effect 或完成 port 请求，不能反向拥有关键字、参数默认值、错误或执行顺序。

| 目标 | 当前 owner/入口 | 通常还需检查 | 未来 owner |
| --- | --- | --- | --- |
| 表达式函数 | `Scripts/Emuera/GameData/Function/Creator.cs`、`Creator.Method*.cs`、`FunctionMethod.cs` | `IdentifierDictionary.GetFunctionMethod`、同名指令投影、`CanRestructure` | frozen `FunctionDescriptor` + Core evaluator |
| 指令 | `GameProc/Function/BuiltInFunctionCode.cs`、`FunctionIdentifier.cs`、`Instraction.Child.cs` | `Instruction.cs`、`ArgumentBuilder.cs`、`Argument.cs`、`ArgumentParser.cs`、flags/flow/wait | frozen `InstructionDescriptor` + VM handler |
| `#` directive/label | `GameProc/LogicalLineParser.cs`、`LogicalLine.cs` | `ErbLoader.cs`、`LabelDictionary.cs`、lazy label scan、reload/analysis mode | Syntax/Directive registry + Core parser |
| 词法/表达式 | `Sub/LexicalAnalyzer.cs`、`Word*.cs`、`GameData/Expression/*` | comparer、macro、source span、错误恢复 | Core lexer/parser/typed diagnostics |
| 系统变量 | `GameData/Variable/VariableCode.cs`、`VariableDescriptor.cs`、`VariableIdentifier.cs` | `VariableData`、`VariableToken`、`VariableEvaluator`、reset、save reader/writer | session `VariableSchema` + `VariableStore` |
| Map/XML/DT/SQL | `Creator.Method.Map/Xml/DT/Sql.cs`、`RuntimeDataStore`、runtime manager | VarExt SAVE/GLOBAL/STATIC、Android SQLite | typed extension/runtime port |
| 方言元数据 | `src/Core/Compatibility/DialectRuntime.cs`、`BuiltInDialectCatalog.cs` | `CompatibilityDescriptorRoute.cs`、`IdentifierDictionary.BindCompatibilityPlan` | session-frozen complete registry |
| 外部 DLL | `Runtime/Utils/PluginSystem/*`、`CALLSHARP` handler | 授权、hash、平台、进程重启 | desktop-only `TrustedPluginHost` |
| 静态证据 | `tools/dialect-inventory/*` | `NewFrameworkDesign/generated/*` | 只用于 evidence/gate，不是 runtime 输入 |

文件名 `Instraction.Child.cs` 是仓库保留的历史拼写，不要为了命名正确顺手重命名。

## Why：为什么必须这样分层

每项扩展都必须保持以下不变量：

1. **未选择模块不变**：新增或升级模块不能改变未选择它的 profile 的注册集合、plan hash 或行为 fixture。
2. **会话所有权**：语义属于 `GameSession` 的冻结计划，不属于进程级当前游戏名或可变 static。
3. **重复即失败**：同名 instruction/function/variable 默认冲突；不得依赖发现顺序或 last-wins。
4. **公开合同完整**：public key、参数、默认值、返回类型、flags、错误、completion、effect、时序和重构纯度都属于语义。
5. **handler 与 descriptor 一致**：descriptor 不能只是文档；其 key、签名、owner 和完成模式必须与真正执行者一致。
6. **名称规则显式**：当前 legacy 的 comparer、current-culture `ToUpper` 和跨表碰撞只是兼容事实，不是未来默认设计。
7. **存档 ABI 显式**：变量 code、wire tag、维度和保存域影响旧档；不能因类型看起来相似就复用编号。
8. **平台与语言分离**：Android 文件、输入、音频、SQLite 或资源实现通过 typed port 提供，不进入 parser。
9. **AOT 可构建**：内置方言使用编译期 allowlist；移动端不依赖反射或 DLL 动态加载。
10. **证据不夸大**：inventory/signature/hash 证明结构，不证明运行结果；截图也不证明 Core state/error/effect 顺序。

这些约束防止三类常见回归：Snake-only key 泄漏到 v24、局部语法修补改变保存或等待顺序、以及 UI/平台代码逐渐成为第二套解释器。

## When：什么时候选择哪种扩展

按顺序回答，命中后停止继续扩大修改面：

1. 能否只用 ERB/ERH 用户函数、宏、用户变量、CSV 或现有函数组合？能则不改解释器。
2. 是否只是“输入参数，立即返回一个 typed value”？是则优先表达式 `FunctionMethod`。
3. 是否需要状态修改、控制流、输出、等待、资源或平台 effect？是则使用语句指令，或现有指令调用 typed capability。
4. 是否只是加载期元数据，而且必须使用新的 `#` 结构？是则扩展 directive，不碰通用 lexer。
5. 现有 token/表达式 grammar 是否确实无法表示？只有此时才改 lexer/parser。
6. 是否需要 engine-owned、脚本可直接寻址的新状态？先判断用户变量或 extension store 是否足够；只有 ABI 必需时才加系统变量。
7. 是否会持久化？若会，立刻升级为 Save/Variable work package，定义 codec profile、候选加载、round-trip 和旧档拒绝语义。
8. 是否需要第三方任意 C#？这不是方言模块；只能进入桌面 trusted plugin 评审，移动端报告 Unsupported。

下列任一条件出现时，任务不能停留在 FastLoop，必须升级为 WorkPackage：

- 修改 `LexicalAnalyzer`、`LogicalLineParser`、`VariableCode`、save reader/writer 或静态注册初始化。
- 新增/改变 wait、input、CALL/JUMP/RETURN、Display、resource、file、SQL、audio effect。
- 增加新的 profile、BehaviorKey、CapabilityId、module dependency、alias 或 replacement。
- 需要 `Program.IsSnakeProfile`、全局配置、游戏路径或 Godot 类型才能完成实现。
- 预期改变 v24/Snake 任一侧现有错误、completion、effect sequence 或存档 bytes。

## How：Agent 的强制实施流程

### 1. 先写扩展合同

编辑代码前，在任务记录或测试 fixture 中填完以下字段。未知项写 `Uncovered`，不能猜：

```yaml
featureKey: stable.task.key
publicKeys: [PUBLIC_KEY]
kind: expression-function | instruction | directive | grammar | variable | capability
ownerModule: gemuera.v24 | game.snake | reviewed-new-module
profiles: [v24pure, snake]
signature: arguments, optional/default rules, return type
stateMutation: exact owner and ordering
errors: parse/load/runtime faults and source position
completion: immediate | wait-port | commit-then-project | application-effect
effects: ordered typed effects, including explicit none
saveImpact: none | schema | codec-profile | migration
fixtures: baseline, extension, undeclared
rollback: registry/flag/adapter and expected old snapshot
```

这里的 YAML 是 Agent 任务记录模板，不是 CompatibilityPack schema，不能被游戏包加载。

### 2. 做库存和冲突检查

至少检查公开 key、handler、注册位置、同名函数/指令/变量、profile ownership 和现有 fixture：

```powershell
rg -n --fixed-strings "<PUBLIC_KEY>" Scripts src tools NewFrameworkDesign
rg -n "addV24CompatibilityFunctions|addSnakeCompatibilityFunctions|GetMethodList|GetInstructionNameDic" Scripts/Emuera
powershell -NoProfile -ExecutionPolicy Bypass -File tools/dialect-inventory/Test-DialectInventory.ps1 -ProjectRoot .
powershell -NoProfile -ExecutionPolicy Bypass -File tools/dialect-inventory/Test-DialectRegistrySnapshot.ps1 -ProjectRoot .
```

名称相同不证明语义相同，`SNAKE_*` 类名也不证明 owner。先查 `dialect-classification.json`、ownership/visibility/lookup generated report，再由 fixture 裁决。

### 3. 先得到 RED

行为测试至少形成以下矩阵：

| 场景 | 必须断言 |
| --- | --- |
| baseline profile 未选择扩展 | 原注册快照/hash/行为不变；扩展 key 不可见或按既有规则处理 |
| extension profile 选择扩展 | parse、参数绑定、执行结果/state、error、completion、effect 顺序符合合同 |
| undeclared/缺依赖/重复 key | 候选构建或加载稳定失败，当前会话不变 |
| boundary 参数 | 空值、省略、类型错、最小/最大、溢出、无效资源或取消均有确定结果 |

当前仓库没有正式 xUnit test project。纯 Core 合同可先进入 `tools/core-contracts` 的定向 smoke；legacy ERB 语义应先做可重复的特征化 fixture/trace。`tools/dialect-inventory` 只验证静态结构，不能单独作为 TDD 的 GREEN。若现有 runner 无法驱动该行为，应先建立最小纯 C# seam 或专用 fixture，不要用启动完整 Godot 后人工点击代替自动断言。

只有涉及 Node、signal、Godot 输入或场景生命周期时才使用 GDUnit4；纯 ERB parser/function/instruction 不应依赖 Godot 测试。

### 4. 实现最小 owner

#### 新增表达式函数

1. 在职责相符的 `Creator.Method*.cs` 中实现 `FunctionMethod`；不要把所有新函数继续堆入无关文件。
2. 构造函数显式设置 `ReturnType`、`argumentTypeArray` 和 `CanRestructure`。
3. 可选、可变参数或 nullable 参数必须 override `CheckArgumentType`，并锁定错误文本/位置语义。
4. 只 override 与 `ReturnType` 匹配的 `GetIntValue`、`GetStrValue`、`GetFloatValue` 或确有需要的 `GetReturnValue`。
5. 读取 RNG、时间、变量、资源、配置或产生副作用时，`CanRestructure=false`；只有纯净、确定且常量折叠不改变错误/时序时才允许 true。
6. 在 `Creator.cs` 用唯一 public key 注册，并检查它与 instruction 表的跨表碰撞。旧 `FunctionIdentifier` 会把未碰撞的表达式函数投影为 METHOD instruction，不能忽略这个可见面。

#### 新增语句指令

1. 仅在确有稳定 code identity 时增加 `FunctionCode`。
2. 在 `Instraction.Child.cs` 或职责明确的新 partial 文件实现 `AbstractInstruction`。
3. 优先复用现有 `ArgumentBuilder`；新参数形状才增加 builder/`Argument`，并使用 `EraType` 表达脚本类型。
4. 明确设置所有有效 flags：flow、jump、try、method-safe、partial、force-arg、print/input/wait 等不能只凭 handler 名推断。
5. `DoInstruction` 必须明确 state mutation、错误、输出/effect 和推进点；等待指令还要覆盖 request、resume、timeout、cancel 与 late completion。
6. 注册到经过 ownership 裁决的贡献位置。若需求是“只在 Snake 可见”，当前 runtime 隔离失败意味着不能仅在 `addSnakeCompatibilityFunctions()` 增加 key 后就完成任务；必须先关闭 D2 路由/名称隔离，或把任务保持 Blocked。

#### 新增 directive 或 grammar

1. 先证明表达式函数/指令/directive 现有形状不能表达需求。
2. directive 同步检查 `ParseSharpLine`、`LogicalLine` 模型、`ErbLoader`、analysis mode、reload 和 `Process.LazyLoading` 的轻量 label 扫描。
3. grammar 同步检查 lexer token、expression parser、macro/rename 阶段、source span、错误恢复和大小写策略。
4. full load、lazy load、partial reload 和内存脚本必须得到一致 logical line；不能只让正常启动路径通过。
5. `_Rename.csv` 仍是 lexer 前的 `SourceTextRewrite`，不能借其替代 alias、replacement 或 parser production。

#### 新增系统变量或保存能力

1. 首先证明用户定义变量、局部/私有变量或 `RuntimeDataStore` 不能满足需求。
2. 不得只改 `VariableCode`。必须同步 descriptor 的 kind/dimension/attributes、identifier、token、store、reset/scope 和所有读写路径。
3. Integer/String/Float/Ref、1D/2D/3D、character/local/global/save/constant 均需精确区分；`VariableCode` bit flag 必须经 `VariableDescriptor` 解读。
4. 任何保存变化都使用显式 `SaveProfileId`，先 candidate parse 再 atomic commit；不能自动猜测 `0x20..0x23` 等冲突 wire type。
5. 覆盖旧档读取、未知 key/type/dimension、sparse、round-trip、错误 profile 不污染当前 store，以及 SAVE/GLOBAL/STATIC 域。

#### 新增平台或运行时能力

1. ERB facade 只形成 typed request/effect；Core port 接受受限 token/DTO，不接受 Godot Node 或任意绝对路径。
2. Bridge/平台 adapter 实现文件、SQL、输入、音频或资源操作，并带 generation、cancel、budget 和 fault。
3. Android/iOS 能力必须由最终导出验证；桌面 DLL/反射结果不能外推到移动端。
4. 外部 DLL 不进入 `DialectModuleCatalog`。需要完全信任插件时走独立 desktop policy，并记录 hash 授权与进程重启边界。

### 5. 同步 descriptor 时避免“半张表”

当前 Core descriptor route 只能选择已经存在的 legacy handler，并把非空 descriptor 集合作为完整可见面。因此：

1. 不要为单个 legacy 新函数/指令立即向 `BuiltInDialectCatalog` 加一条孤立 contribution。
2. D2 接线必须一次提供所选 profile 的完整 instruction/function surface，而不是只提供增量模块差集。
3. 完整 surface 必须同时处理 lookup、名称冲突表、跨表碰撞、comparer/normalizer 和 hidden-key 不可见性。
4. descriptor 的 signature、module、completion/return 必须来自已求值 inventory 和行为 fixture，不从类名猜。
5. 只有完整 v24/Snake registry snapshot、两侧 behavior 和 undeclared-side rejection 都通过后，才能让 non-empty descriptor plan 成为默认 runtime 路由。

### 6. GREEN 后运行分层验证

只改 legacy ERB C# 时，最低 FastLoop 为：

```powershell
dotnet build gemuera-c#.sln -c Debug --no-restore
powershell -NoProfile -ExecutionPolicy Bypass -File tools/dialect-inventory/Test-DialectInventory.ps1 -ProjectRoot .
powershell -NoProfile -ExecutionPolicy Bypass -File tools/dialect-inventory/Test-DialectRegistrySnapshot.ps1 -ProjectRoot .
powershell -NoProfile -ExecutionPolicy Bypass -File tools/dialect-inventory/Test-DialectSignatureInventory.ps1 -ProjectRoot .
```

按变更追加：

| 变更 | 追加验证 |
| --- | --- |
| 指令参数/flags | `Test-DialectSignatureResolution.ps1`、`Test-DialectInstructionFlagResolution.ps1` |
| 函数参数/返回 | `Test-DialectFunctionSignatureResolution.ps1` |
| 名称/comparer/碰撞 | `Test-DialectNameLookupContract.ps1` |
| module/profile/BehaviorKey | 对应 DIA-09 至 DIA-17 contract tests |
| Core module/plan | Core build、`CoreContractSmoke`、`Test-CoreArchitecture.ps1` |
| legacy 行为 | 任务专用 v24/Snake fixture/trace；命令和 artifact 见 `tools/legacy-runner/README.md` |
| 保存 | save baseline/fixture/round-trip tests，且原文件 hash 不变 |
| Godot 场景 | 单一相关 GDUnit4 suite |
| Android 能力 | WorkPackage/PhaseRelease 的 APK 与真机报告 |

Core 合同命令：

```powershell
dotnet build src/Core/GEmuera.Core.csproj -c Release --no-restore
dotnet run --project tools/core-contracts/CoreContractSmoke.csproj -c Release --no-restore
powershell -NoProfile -ExecutionPolicy Bypass -File tools/core-contracts/Test-CoreArchitecture.ps1 -ProjectRoot .
```

registration 或 profile source 变化会使下游 DIA hash 失效。按 `tools/dialect-inventory/README.md` 的依赖顺序重新生成和测试 DIA-01 至 DIA-17；DIA-07 需要锁定的 XEmuera 源目录。不能只修改期望计数或只重生成最后一份报告来“恢复绿色”。所有 `Partial/Failed/Uncovered/Blocked` 状态必须保留，直到对应行为证据真实关闭。

### 7. 评审、文档和回退

- 修改 `Scripts/**/*.cs` 后判断并更新 `CODE_MAP.md`；新增公开 key、ownership 或语义还要更新本指南和对应权威设计/evidence。
- action log 记录 RED、GREEN、命令、exit code、artifact/hash、未覆盖项和回退。
- 回退必须恢复旧 registry snapshot/hash；保存相关变更还要保留旧 codec 和原档备份。
- 不删除为稳定回归建立的正式测试。只删除一次性探针和无复用价值的临时文件。
- 不把 build、inventory 或单个游戏启动写成“完整兼容”。

## Definition of Done

Agent 只有在以下项目全部满足或明确标为 Blocked 时才能结束任务：

- [ ] public key、kind、module/profile owner、依赖和版本已明确。
- [ ] 全表与跨表 collision 已检查；无静默覆盖。
- [ ] signature、默认值、return、flags、errors、completion、effects 和 `CanRestructure` 已固定。
- [ ] baseline、extension、undeclared 三类场景先 RED 后 GREEN。
- [ ] v24 和 Snake 两侧均有证据；未选择模块的 registry/hash/行为不变。
- [ ] parser 改动覆盖 full/lazy/reload；wait 改动覆盖 resume/cancel/late completion。
- [ ] variable/save 改动覆盖 schema、reset、candidate commit、旧档和 round-trip。
- [ ] Core 无 Godot 类型、路径、反射模块发现或可变 session truth。
- [ ] inventory/signature/lookup/module 报告已按依赖链更新，状态没有夸大。
- [ ] 定向 build/test、行为 fixture 和必要的平台门已记录。
- [ ] `CODE_MAP.md` 判断、action log、风险和回退步骤已完成。

## 禁止合并的快捷方式

- 新增 `if (Program.IsSnakeProfile)`、`CoreProfile ==` 或按游戏 id 判断的 Parser/VM 语义。
- 启动后修改 `GetInstructionNameDic()` 或 `GetMethodList()` 返回的字典。
- 只加 `FunctionCode`/`VariableCode`/descriptor，不实现和测试完整链路。
- 将一个增量 descriptor 当作完整 plan，或用空表 fallback 掩盖缺失注册。
- 依赖 `Dictionary[key] = value`、注册顺序或函数向指令投影实现覆盖。
- 未经 comparer/culture fixture 直接新增 `ToUpper()`、ignore-case 或 Unicode alias。
- 为本可用函数/指令表达的能力修改 lexer grammar。
- 在 expression function 中允许常量折叠，却读取时间、RNG、变量、资源或产生副作用。
- 用 Godot signal、Node 或 Autoload 保存 ERB state/等待状态。
- 将 reflection DLL、CompatibilityPack 或 `_Rename.csv` 当成内置方言注册机制。

## 参考入口

- [方言、兼容模块与魔改接口](NewFrameworkDesign/DialectExtensionSystem.md)
- [指令与内置函数库存](NewFrameworkDesign/InstructionInventory.md)
- [变量系统](NewFrameworkDesign/VariableSystem.md)
- [扩展运行时与插件安全](NewFrameworkDesign/ExtensionRuntime.md)
- [AI 分层验证流程](NewFrameworkDesign/AIDevelopmentWorkflow.md)
- [运行与验证命令](NewFrameworkDesign/HowToRun.md)
- [代码地图](CODE_MAP.md)
