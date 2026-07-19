# 解释器方言、兼容模块与魔改接口

## 目标

Emuera 生态并不是一个只需跟随单一上游版本的封闭解释器生态。游戏可能依赖上游语义、v24 行为、Snake 分支、作者私有指令、宽松解析规则、不同存档扩展或平台适配。目标因此不是建立一个越来越大的 `SnakeMode`，而是建立一套满足以下条件的兼容系统：

- 标准 Emuera/v24 的行为在未选择扩展时保持不变。
- Snake、eraFL 或未来魔改只影响显式选择该模块/能力组合的 `GameSession`。
- 指令、内置函数、变量、解析规则、存档、资源和可观察时序都能被分类扩展。
- 冲突必须在候选会话加载时显式失败，不能依赖注册顺序或静默覆盖。
- Android/iOS 可使用编译期内置模块，不依赖运行时反射或 DLL 动态加载。
- 新兼容模块有独立 fixture、版本、诊断和回退，不需要在解析器/VM 中继续增加 `if (Snake)`。

本章定义的是**受控解释器兼容模块**。它不等同于 [ExtensionRuntime](ExtensionRuntime.md) 中拥有完整进程权限的外部 DLL 插件，也不允许游戏包通过兼容 manifest 注入任意 C# 代码。

## 旧工程事实与根因

只读审计 `E:/MyCode/GodotCode/gEmuera-future` 得到以下事实：

| 事实 | 旧源码符号 | 风险 |
| --- | --- | --- |
| Profile 是进程级静态状态 | `Program.EmueraCoreProfile`、`Program.CoreProfile` | 多会话不能拥有不同语义；测试容易互相污染 |
| 选择只分 `V24Pure/Snake/SnakeModernMobile` | `Program.DetectCoreProfile` | 每增加一个分支就扩大组合爆炸 |
| 文件 marker 会参与自动识别 | `snake_core.txt`、`legacy_snake_core.txt`、`modern_core.txt` | 弱信号可能静默选择错误语义 |
| v24 与 Snake 注册都无条件执行 | `FunctionIdentifier.addV24CompatibilityFunctions`、`addSnakeCompatibilityFunctions` | v24 也得到 Snake 名称；能力边界无法证明 |
| Snake 判断散落在 10 个源码文件、30 个命中行 | `Program.IsSnakeProfile`/`CoreProfile` 引用 | 修改一个游戏时可能改变另一个 profile |
| CALL 多余参数规则直接读取全局 Profile | `CalledFunction.ConvertArg` | 同一个解析/调用器包含互斥语义 |
| 解析错误是否继续启动直接读取全局 Profile | `Process.SystemProc` | 错误策略和游戏身份硬编码 |
| 变量、私有参数、资源和刷新时序也读取 Profile | `ExpressionParser`、`ErbLoader`、`AppContents`、`EmueraConsole` | 语言、资源与 View 策略彼此耦合 |

本次审计对象哈希：`Program.cs` 为 `357D5CB90DCBEB70019FEA47C37D7439641EF63C80C050B058D768793DE01C88`，`FunctionIdentifier.cs` 为 `0674DD19C63F07A72328F986C862EC197AFD59FD5EC423EF7264C51B16A93629`，`Process.SystemProc.cs` 为 `51ED63AAB8E8E7A74398C2F8F7D0CFC0BE2A057296B1C65C1E7544356C3C86A2`，`Process.CalledFunction.cs` 为 `2ACE74727707589B5B29FEEE90C9ED96B9A7E0C5B2BA258F8DE10AEE083D3691`。哈希只固定事实来源，不代表这些行为已通过目标实现。

根因不是“Snake 修得不够干净”，而是旧结构同时存在三种耦合：

1. **选择耦合**：进程级 Profile 决定全部会话。
2. **注册耦合**：所有扩展先进入共享静态字典，再在执行期判断。
3. **策略耦合**：解析、调用、资源、显示和平台层直接询问游戏名/Profile。

新架构必须同时消除三者，只把旧可观察行为保留下来。

## 术语和边界

| 术语 | 含义 | 示例 |
| --- | --- | --- |
| `DialectModule` | 编译期受信任、贡献解释器语义的模块 | `emuera.upstream.1808`、`gemuera.v24`、`game.snake` |
| `CompatibilityPack` | 早期静态契约/实验数据，不含可执行代码；不作为当前正常启动器的 profile 来源 | 迁移证据与离线组合验证 |
| `DialectPlan` | 候选加载期间解析并冻结的解释器注册表和行为策略 | 指令表、函数表、变量 schema、错误策略 |
| `CompatibilityPlan` | `DialectPlan` 加存档 profile、资源策略和脚本可观察 runtime capability 的会话计划 | Snake + float codec + SQL capability |
| `RuntimeCapability` | 平台或 Bridge 可提供的受控能力 | SQLite、文件枚举、Android 虚拟光标 |
| `ExternalPlugin` | 用户显式信任的第三方 DLL | 仅桌面可选；不属于默认方言机制 |
| `BehaviorKey` | 稳定、受 schema 管理的窄行为插槽 | `call.extra-arguments.v1` |
| `CapabilityId` | 模块提供或要求的稳定能力标识 | `value.float.v1`、`input.nofocus.v1` |

平台差异默认不属于解释器方言。安全区域、Theme、触摸手势和纹理质量只进入 `EffectivePlatformConfig`；只有能被 ERB 查询或改变脚本结果的能力，才作为 `RuntimeCapability` 绑定进 `CompatibilityPlan`。因此 Android 虚拟光标实现不会自动改变 v24 的解析语义。

## 组合模型

禁止通过 `SnakeDialect : V24Dialect : BaseDialect` 的深继承表达兼容关系。继承会让覆盖来源隐蔽，也难以组合另一个游戏分支。采用依赖 DAG 和显式贡献：

```text
CompatibilityPlan
 ├─ base: emuera.upstream.1808@1.x
 ├─ modules:
 │   ├─ gemuera.v24@1.x
 │   └─ game.snake@2.x
 ├─ policies:
 │   ├─ call.extra-arguments.v1 = <fixture-validated decision>
 │   └─ parser.startup-fault.v1 = <fixture-validated decision>
 ├─ saveProfile: gemuera.snake.float-v1
 ├─ capabilities:
 │   ├─ value.float.v1
 │   ├─ input.nofocus.v1
 │   └─ data.sqlite.v1 → AndroidSqlitePort
 └─ canonicalHash: SHA-256(...)
```

`game.snake` 可以声明依赖 `gemuera.v24` 的一个版本范围，但只能贡献清单内能力或显式替换行为插槽。模块不能读取“当前游戏名”后自行改变其他注册。未来另一个魔改解释器可复用 float/NF/SQL capability，而不必继承整个 Snake。

推荐把兼容拆成小而稳定的模块，而不是每个游戏一个巨型模块：

- 基线模块：上游语法、标准指令、标准变量与错误语义。
- 版本模块：v24 增量语义。
- 能力模块：Float、NF Input、Scoped Variables、VarExt、SQL 等可独立验证集合。
- 游戏模块：仅保留该游戏真正独有的 alias、quirk 和默认组合。
- 资源/显示模块：仅包含脚本可观察的资源查找、HTML 或层语义。
- 平台绑定：将已要求的 capability 绑定到桌面/Android/iOS 实现，不修改语言注册表。

## eraFL 作为第二条设计基线

eraFL 不能只作为一个 HTML golden。旧 gEmuera 对它的兼容已经横跨 Parser、VM 调用栈、输入提交、资源查找、Display diff、Godot Control/Canvas 和 Android 虚拟光标，是检验本系统是否真正可扩展、而非“把 SnakeMode 换个名字”的第二条基线。

旧代码中已定位的 eraFL 专用/强相关处理包括：

| 旧处理 | 证据 | 新契约归属 |
| --- | --- | --- |
| 用固定函数名/前缀识别动态地图输出范围 | `Process.State.dynamicMapRenderRootFunctionNames`、`IsInDynamicMapFunctionScope` | `IDisplayScopePolicy`，在 VM 输出时写稳定 scope metadata |
| `INPUTS ,1` 被解释为省略默认字符串 | `ArgumentBuilder` | `input.omitted_default_argument.v1` typed parse policy |
| 空白区域右/中键给整数输入补 `-1` | `EmueraThread` | `IPointerInputSubmissionPolicy`；Core 输入 trace 可差分 |
| 未知 `<A>/<C>/<S>` 作为正文保留 | `HtmlManager.Html2PlainText` | `markup.unknown_angle_text.v1` |
| `SPRITEDISPOSEALL 0` 只清动态 Sprite | `AppContents.SpriteDisposeAll` | 资源生命周期 capability/fixture |
| 动态 Sprite 相对路径依次查游戏根和 `resources/` | `AppContents.ResolveDynamicSpriteFilePath` | `resource.dynamic_sprite_roots.v1`，仍受 content root 安全限制 |
| div/srcb/depth/负坐标/跨行叠放与异步补图 | `ConsoleDivPart`、`ConsoleImagePart`、`EmueraContent*` | Display DTO v2 + renderer capability，不硬编码游戏名 |
| 删除底部后重画地图时保持 viewport | `uEmuera.Window.DecideScrollModeForDisplayDelta` | `DisplayTransaction.ScrollIntent`/动态 scope reducer |
| 只刷新按钮 generation/data，不重建视觉行 | `linesToRefreshData`/`dataOnlyLines` | `DisplayDataPatch` |
| 私有区图标字体、旋转 pivot、错误编码 XML 等兼容 | `EmueraContent`、`Creator.Method` | 分别归 View font profile、graphics policy、bounded decoder policy |

这些补丁目前分散且部分互相遮蔽。例如 `EmueraContent.Canvas.CanRenderLineOnCanvas` 先对所有 `ConsoleDivPart` 返回 false，后面的“相对 div 可局部 overlay”分支因同一类型判断而不可达；动态地图又通过游戏函数名和 UI 端启发式共同判断。这些实现可作为行为证据和 fallback，不能照搬成新架构边界。

eraFL 的目标组合示例：

```text
game.erafl@1.x
  requires: resolved Emuera/v24 base (由 fixture 决定，不在文档中猜测)
  requires capabilities:
    markup.div-v2
    markup.image-dual-src.v1
    display.dynamic-map-transaction.v1
    input.pointer-button.v1
    resource.dynamic-sprite.v1
  contributes typed policies:
    input.omitted_default_argument.v1
    input.pointer_blank_integer.v1
    markup.unknown_angle_text.v1
    display.dynamic_scope_classifier.v1
```

`game.erafl` 只声明游戏独有的选择器/quirk；通用 div、动态地图事务、双态图片、虚拟光标和资源生命周期仍是可复用 capability。若后续另一个魔改游戏也需要它们，应直接复用 capability，而不是复制 `if (EraFL)`。精确 base 版本、函数选择器、默认值和错误语义必须由 FL fixture 固定。

## 稳定类型与接口

以下是 C# 风格伪代码。模块入口只描述元数据和贡献者，具体领域用窄接口，避免形成新的万能 `CompatibilityManager`。

```csharp
readonly record struct DialectId(string Value);
readonly record struct CapabilityId(string Value);
readonly record struct BehaviorKey<TPolicy>(string Value);

record DialectDescriptor(
    DialectId Id,
    SemanticVersion Version,
    int ModuleApiVersion,
    ImmutableArray<ModuleRequirement> Requires,
    ImmutableArray<ModuleConflict> Conflicts,
    ImmutableArray<CapabilityId> Provides,
    ImmutableArray<ReplacementDeclaration> Replaces);

interface IInterpreterDialectModule
{
    DialectDescriptor Descriptor { get; }
    ImmutableArray<IDialectContribution> Contributions { get; }
}

interface IDialectContribution { }
interface ISyntaxContribution : IDialectContribution { void Apply(SyntaxRegistryBuilder b); }
interface IInstructionContribution : IDialectContribution { void Apply(InstructionRegistryBuilder b); }
interface IFunctionContribution : IDialectContribution { void Apply(FunctionRegistryBuilder b); }
interface IVariableContribution : IDialectContribution { void Apply(VariableSchemaBuilder b); }
interface ISaveContribution : IDialectContribution { void Apply(SaveCodecRegistryBuilder b); }
interface IMarkupContribution : IDialectContribution { void Apply(MarkupRegistryBuilder b); }
interface IResourceContribution : IDialectContribution { void Apply(ResourcePolicyBuilder b); }
interface IInputContribution : IDialectContribution { void Apply(InputRegistryBuilder b); }
interface IBehaviorContribution : IDialectContribution { void Apply(BehaviorPolicyBuilder b); }
```

构建器只能在候选会话加载阶段存在。成功后产出深不可变计划：

```csharp
sealed record DialectPlan(
    ImmutableArray<ResolvedModule> OrderedModules,
    FrozenDictionary<InstructionName, InstructionDescriptor> Instructions,
    FrozenDictionary<FunctionName, FunctionDescriptor> Functions,
    FrozenDictionary<DirectiveName, DirectiveDescriptor> Directives,
    VariableSchema Variables,
    SaveCodecRegistry SaveCodecs,
    MarkupCapabilitySet Markup,
    InputCapabilitySet Input,
    BehaviorPolicySet Policies,
    CapabilitySet ProvidedCapabilities,
    PlanHash CanonicalHash);

sealed record CompatibilityPlan(
    DialectPlan Dialect,
    SaveProfileId SaveProfile,
    ResourceBehaviorSet ResourceBehaviors,
    RuntimeCapabilityBindings Runtime,
    ImmutableArray<ResolutionEvidence> Evidence,
    PlanHash CanonicalHash);
```

Parser、VM、SaveService 等只接收自己需要的窄视图，例如 `IInstructionCatalog` 或 `IArgumentBindingPolicy`；它们不能接收 resolver，更不能调用 `Program.IsSnakeProfile`。

## 注册表与运行时成本

候选加载时一次完成模块排序、贡献应用、冲突验证和表冻结。执行 hot path 直接按规范化名称或整数 id 查询冻结表，不逐指令遍历模块、不反射扫描程序集、不使用 Godot node 查找服务。

| 注册表 | 记录内容 | 主要消费者 |
| --- | --- | --- |
| Syntax/Directive | token、指令头、解析器、source rules | Header/ERB parser |
| Instruction | 公开名、参数 schema、flags、handler、完成模式 | logical line parser/VM |
| Function | 公开名、参数/返回类型、purity、handler | expression parser/evaluator |
| VariableSchema | Integer/String/Float/Ref、维度、scope、保存域 | variable parser/store |
| SaveCodec | profile id、magic/version/type map、限制 | SaveService |
| Markup/Display | tag/attribute、DTO 能力、错误策略 | HTML parser/display builder |
| Resource | key/path/csv/duplicate/layer policy | ResourceCatalog/PixelStore |
| Input | 输入种类、NF、mouse/hotkey 可观察能力 | VM/InputCoordinator |
| BehaviorPolicy | typed quirk 与默认实现 | 各窄消费者 |
| RuntimeCapability | capability 到 port 的绑定和平台状态 | VM/Bridge |

字符串输入受长度/数量限制并使用明确 comparer；解析后尽早转为稳定 id。计划构建和 lookup 都进入 benchmark，不能为了“可扩展”在每条指令上引入 service locator 或 delegate 链。

## 冲突与替换规则

默认规则是**重复即失败**，绝不使用“最后注册者获胜”。

1. 依赖先做拓扑排序；缺依赖、版本不匹配或依赖环均为候选 fatal。
2. 两个模块注册同一指令/函数/变量名时，若只是同义名必须用 `AliasOf`；否则失败。
3. 替换必须在模块元数据中声明目标 module id、version range、注册 key 和 replacement reason。
4. 替换的参数类型、返回类型、完成模式或持久化布局若改变，必须使用新的 capability/schema version；不能伪装兼容替换。
5. `emuera.upstream` 的安全边界、路径隔离、内存 reservation 和 candidate commit 等 sealed invariant 不允许游戏模块关闭。
6. 两个模块提供互斥 capability 时必须显式选择；resolver 不能按发现顺序猜测。
7. 计划验证输出完整 provenance：每个公开名/BehaviorKey 来自哪个模块、是否 alias/replace、替换了什么。

旧工程中 v24/Snake 注册无条件混合的模式在新架构中必须产生测试失败：只选择 `gemuera.v24` 时，`game.snake` 独有名称不能出现在注册表。

## Typed behavior policy

不是所有差异都值得复制一套 parser 或 handler，但也不能提供任意回调让模块侵入所有执行点。对已确认的差异建立窄、typed、可枚举的策略接口：

在 M0，接口的**名称和唯一消费者**可以静态冻结，但 C# interface、方法签名、输入 DTO、返回 decision enum、默认值和异常/完成/时序语义都不能提前实现或假定。下面的 `portTypeId` 来自 [M0-DIA-15 静态 policy surface](generated/dialect-policy-surface.json)，仅是未来受控接口的名字；它们统一为 `InterfaceDraftOnly`/`Unspecified`，不能被当作已有类型或可调用 policy。

| BehaviorKey | future portTypeId | consumer contract | contract kind | 唯一 decision owner |
| --- | --- | --- | --- | --- |
| `call.extra-arguments.v1` | `IExtraArgumentPolicy` | `policy.call.extra-arguments.v1` | `PolicyDecision` | `IExtraArgumentPolicy` |
| `call.private-argument-shape.v1` | `IPrivateArgumentShapePolicy` | `policy.call.private-argument-shape.v1` | `PolicyDecision` | `IPrivateArgumentShapePolicy` |
| `display.history-capacity.v1` | `IEffectiveDisplayConfigurationProjection` | `bridge.display.history-capacity.v1` | `BridgeProjection` | effective display configuration projection |
| `display.refresh-timing.v1` | `IDisplayRefreshTimingPolicy` | `bridge.display.refresh-timing.v1` | `BridgeProjection` | display timing policy consumed by Bridge |
| `function.snake-fallen-state.v1` | `IExpressionFunctionCatalog` | `catalog.expression.snake-fallen-state.v1` | `FrozenCatalogContribution` | frozen expression function catalog |
| `instruction.scoped-variable-registration.v1` | `IInstructionCatalogBuilder` | `catalog.instruction.scoped-variable-registration.v1` | `FrozenCatalogContribution` | frozen DialectPlan instruction catalog builder |
| `parser.startup-fault.v1` | `IStartupFaultPolicy` | `policy.parser.startup-fault.v1` | `PolicyDecision` | `IStartupFaultPolicy` |
| `parser.user-variable-resolution.v1` | `IUserVariableResolutionPolicy` | `policy.parser.user-variable-resolution.v1` | `PolicyDecision` | `IUserVariableResolutionPolicy` |
| `parser.warning-routing.v1` | `IParserDiagnosticsSinkPolicy` | `policy.parser.warning-routing.v1` | `PolicyDecision` | parser diagnostics sink policy |
| `resource.lazy-index.v1` | `IResourceLazyIndexPolicy` | `policy.resource.lazy-index.v1` | `PolicyDecision` | resource behavior consumer |

`instruction.scoped-variable-registration.v1` 还有一个关键边界：`scoped-variable-config-schema` 是 `ConfigurationInput`，不拥有最终注册决策；`scoped-variable-registration-guard` 才是 `DecisionConsumer`。因此 future configuration snapshot 只能由编排者向 catalog builder 下传数据，不能与 catalog builder 横向直接调用。`display.refresh-timing.v1` 由 Bridge 消费，但仍进入未来 plan/hash，因为它可能改变脚本和输入观察时序。新增 BehaviorKey/port 必须经过 schema 评审、基线默认、至少两侧 fixture 和迁移说明；禁止创建 `Dictionary<string, object>` 式万能 quirks。

## 解析与选择流程

方言选择发生在候选 `GameSession` 构造中，绝不写进全局 `Program.CoreProfile`：

```text
fixed-depth launcher directory scan
 → create LauncherGameEntry(path, profileId, source)
 → validate profileId against the frozen profile catalog
 → resolve module DAG and versions
 → bind platform/runtime capabilities
 → build registries and typed policies
 → validate conflicts, save profile and fixture requirements
 → freeze CompatibilityPlan and canonical hash
 → parse scripts/resources using that exact plan
 → short atomic session commit
```

正常启动的 profile 来源只有显式目录路由：

1. `emuera/<game>` 选择 `v24pure`。
2. `emuera/snake/<game>` 选择 `snake`。
3. `emuera/compat/<profile-id>/<game>` 选择经过 allowlist 校验的 `<profile-id>`。
4. 用户显式开启的高级覆盖只作用于当前游戏或当前会话，不得成为所有游戏共享的全局 profile。

正常启动不读取 GAMEBASE、标题、ERB/ERH 锚点、marker 或 capability 特征来猜测 profile。未知目录 profile 直接停止候选并显示错误，不能静默回退 v24。选择另一 profile 仍等同于重新构建新候选，禁止在运行中热换注册表。

完整目录契约、UI 合并规则、固定扫描深度、错误策略与 Android 路径见 [DirectoryCompatibilityRouting](DirectoryCompatibilityRouting.md)。

## 目录路由安全边界

- `compat` 下第一层只解释为 profile id，不解释为 module、程序集、类型、URL 或任意路径。
- profile id 必须符合稳定标识符语法，并存在于首个会话前冻结的 catalog。
- 游戏目录不能声明 module 数组、capability、typed policy、save profile 或版本范围。
- profile 目录数量、每个 profile 的游戏数量、总条目和扫描深度都有硬上限。
- `v24pure` 与 `snake` 是 launcher lane 保留 profile，不通过 `compat` 重复暴露。
- 目录路由只选择预编译的受信任 profile；移动端不加载外部程序集。

## 版本与可重现性

版本分三层，不能混用：

- `ModuleApiVersion`：模块与构建器接口版本；不兼容变更才递增。
- 模块 `SemanticVersion`：该模块行为/注册内容的版本。
- schema/capability version：例如 `call.extra-arguments.v1`、`value.float.v1`，代表脚本可观察契约。

`CompatibilityPlan.CanonicalHash` 对排序后的 module id/version、注册 key/signature、typed policies、save profile、script-observable capability bindings 和兼容配置做规范化哈希。诊断/差分报告同时记录 Core build hash、content hash 和 plan hash，任何一项不同都不能直接比较为同一 profile。

存档原有二进制格式不为写入 plan metadata 而改变。应用可在受控目录保存 sidecar/session index，记录存档 hash、content identity、SaveProfileId 和 plan hash；sidecar 缺失时仍按显式 codec/profile 流程识别。改变 save profile 前先备份并要求迁移器证明可往返，禁止仅因当前游戏选择了 Snake 就猜测冲突类型码。

[M0-SAV-01 静态基线](generated/legacy-save-baseline.json) 只把当前 legacy source 的 header、file/data type、sparse marker、入口和 `Float=0x20..0x23` 冲突固定下来；它不产生 `CompatibilityPlan.SaveProfile`，也没有将 DIA-12/16 的 profile/module 声明绑定到 codec。故 `snake` 模块存在、marker 存在或 source 能读取某种 byte 都不能替代 explicit `SaveProfileId`、content evidence、只读 dry-run 和 round-trip fixture。

## GameSession 所有权与 Godot 组合

`GameSession` 唯一拥有冻结的 `CompatibilityPlan`。候选 builder 是唯一修改者；提交后 Parser/VM/Save/Resource 只能读取。View 只得到脱敏摘要：模块、版本、目录路由来源、缺失 capability、plan hash 和警告，不直接修改策略。

Godot 层遵循“调用向下、信号向上”：

- `FirstWindow` 根据固定目录路由产生携带 path/profile 的游戏条目。
- `CompatibilityPanel` 只在高级模式下发出当前游戏/会话覆盖 intent。
- `MainOrchestrator` 调用 `SessionCoordinator.SwitchGameAsync(selection, override)`。
- Core 验证显式 profile 并返回 typed plan/build result，不发 Godot signal。
- `SessionCoordinator` 只有在完整候选通过后提交。
- UI 不通过 Autoload 改全局 profile；切换选择总是新 generation。

兼容 UI 在高级模式下至少显示：目录来源、显式 profile、最终模块、缺失/冲突 capability、存档 profile 和 plan hash。正常界面只保留 `v24` / `snake` lane，不为 eraFL 或未来 profile 增加顶层按钮，也不提供“恢复自动检测”入口。

## 与外部插件的安全隔离

| 机制 | 是否执行第三方代码 | 平台 | 默认 | 用途 |
| --- | --- | --- | --- | --- |
| 内置 DialectModule | 否；代码随应用编译并评审 | 全平台/AOT | allowlist | 解释器语义 |
| CompatibilityPack | 否；受 schema 限制的数据 | 全平台 | 正常启动不读取 | 迁移证据与离线组合实验 |
| RuntimeCapability binding | 否；应用内置 port | 按平台 | capability allowlist | SQL、文件、输入等 |
| ExternalPlugin DLL | 是，完整进程权限 | 仅桌面候选 | 禁用 | 无法声明化的受信任扩展 |

不得为了方便兼容魔改游戏而把 DLL 放入 `DialectModuleCatalog` 动态扫描。若未来确有无法声明化的第三方解释器扩展，必须先选择：合并为经过评审的内置模块，或走桌面受信任插件/独立进程 broker；Android/iOS 不通过反射绕过 AOT 和平台限制。

## 迁移旧 v24/Snake 分支

本系统按小步替换旧 Profile，不要求一次重写解释器：

| 子阶段 | 改动 | 关闭门禁 | 回退 |
| --- | --- | --- | --- |
| D0 分支库存 | 提取所有 `CoreProfile`/`IsSnakeProfile`、条件注册和 setting 字段，按注册/typed policy/platform 分组 | 每个命中有 owner、BehaviorKey/capability 和 fixture id | 纯文档/runner |
| D1 会话上下文 | 在 `LegacySessionFacade` 加只读 `CompatibilityPlan`，旧 `CoreProfile` 仅由 adapter 投影 | A/B session 不共享 plan；报告 plan hash | `compat.plan=false` |
| D2 冻结注册表 | 把现有标准/v24/Snake 注册函数改为候选构建贡献；先保持旧 handler | v24-only 表不含 Snake-only key；注册快照差分 | 旧静态字典 |
| D3 typed policies | 逐个替换 CALL 参数、解析错误、私有参数、资源和刷新分支 | 每替换一个运行 v24+Snake 两侧 fixture | adapter 继续提供旧布尔值 |
| D4 去全局化 | Parser/VM/Resource/View 不再读取 `Program.CoreProfile` | Roslyn/文本架构守卫为零；多会话测试 | D1 adapter |
| D5 Directory Route/UI | 加固定深度 `compat/<profile>/<game>` 路由、条目 profile 元数据和错误报告 | v24/snake/compat、未知 profile、路径层级、切换竞态测试 | 关闭 compat 扫描，只保留旧 lane |
| D6 新魔改模板 | 发布模块模板、contract kit 和审核清单 | 至少一个非 Snake 示例证明组合能力 | 仅内置现有模块 |

D0–D2 关闭前冻结“稳定类型与接口”章节为设计草案：除 `LegacySessionFacade` 所需的只读 plan snapshot、registry snapshot 和测试 DTO 外，不得批量实现 catalog/resolver/policy manager。D0 的库存与归属证据先证明真正需要哪些 BehaviorKey，并把上游同名、当前模块候选和未决项分开；D2 的未选择模块不变性先证明拆分降低了复杂度。任何新接口必须能指出唯一 owner、唯一可观察差异和至少一个两侧 fixture，否则留在文档而不进入代码。

D0 的“30 个命中行/10 个文件”只是当前静态起点，还要审计未直接引用 Profile 但以 `SNAKE_` 类名无条件注册、由 `Config.UseScopedVariableInstruction` 控制或通过 FirstWindow 选择的路径。迁移完成条件不是删除字符串 `Snake`，而是所有可观察差异都有明确模块/策略、所有未选择 profile 的不变性测试通过。

## 模块 Contract Test Kit

每个模块必须运行统一 contract kit：

1. descriptor/schema/version 可解析，依赖 DAG 无环。
2. 注册 key 唯一；alias/replacement provenance 完整。
3. 构建两次得到相同 registry snapshot 和 canonical hash。
4. 模块未被选择时，基础 plan hash、注册集合和 fixture 结果完全不变。
5. 新增/升级 `game.snake` 必须同时跑 `emuera.upstream` 和 `gemuera.v24` 全套回归。
6. 每个新增指令/函数覆盖 parse、execute、返回、错误、等待/完成模式和边界参数。
7. 每个 typed policy 覆盖基线值、扩展值和未声明值拒绝。
8. dependency missing/version mismatch/cycle、duplicate、illegal replacement、capability conflict 均稳定失败。
9. 目录路由覆盖普通根、snake、compat、未知 profile、非法层级、大小写差异、路径去重和 stale generation。
10. SaveProfile/plan 变化覆盖备份、拒绝、sidecar 丢失和错误 codec 不污染当前会话。
11. Android AOT/export 验证 built-in catalog 完整，无运行时 assembly/type 扫描。
12. lookup/plan build benchmark 不超过批准阈值；执行 hot path 无逐模块扫描。

组合测试只覆盖产品声明支持的组合、依赖边和高风险 pairwise，不承诺穷举所有理论排列。任何允许通过 `compat/<profile>` 选择的正式组合必须有固定 fixture profile。最重要的元属性是：**向构建中加入一个模块，不得改变未选择该模块的会话**。

## 架构守卫

建立代码后 CI 增加以下守卫：

- Core/Bridge 中禁止新增 `Program.IsSnakeProfile`、`CoreProfile ==` 或按 game id 的执行分支。
- 禁止静态可变 instruction/function registry 成为会话真相。
- 禁止无 `ReplacementDeclaration` 的重复注册和 last-wins API。
- 禁止把 `ICVariable` 静态初始化时捕获的 comparer 或 `ICFunction` current-culture `ToUpper` 直接提升为未来 frozen registry 策略；D2 必须显式选择 comparer/normalizer 并用文化语料验证。
- `_Rename.csv` 的词法分析前 `SourceTextRewrite` 必须与 registry `AliasOf`/`ReplacementDeclaration` 分离，不能用文本替换顺序模拟模块覆盖。
- `GameSession.Compatibility` 深不可变，运行期无 setter/reload。
- 模块程序集不得引用 GodotSharp；平台 binding 不能向 Core 暴露 Node/Variant。
- 移动构建的 module catalog 由显式代码/源生成器产生，不使用反射枚举。
- compatibility 目录解析器只能产生 allowlisted profile id，不接受 module 列表、任意类型、回调或路径。
- 任何新增模块 PR 必须提供 base-profile invariance report、模块 registry diff 和 fixture report。

## 验收状态

本章关闭了“如何防止 Snake 修改破坏 v24”的架构裁决，并由 [M0-DIA-01 机器库存](generated/dialect-inventory.json) 固定当前源码身份 `a30d210f...ccdc2` 下 97 个 profile/setting 命中与 326/360 注册。[M0-DIA-02 测试快照](generated/dialect-registry-snapshots.json) 生成 v24=290/358、Snake=326/360、差集=36/2，证明未选择 Snake 不改变 v24 测试 hash，同时记录旧 runtime isolation=`Failed`。[M0-DIA-03 签名库存](generated/dialect-signature-inventory.json) 固定原始候选；[M0-DIA-04 指令参数](generated/dialect-signature-resolution.json)、[M0-DIA-05 函数签名](generated/dialect-function-signature-resolution.json) 与 [M0-DIA-06 指令 flags](generated/dialect-instruction-flag-resolution.json) 分别逐 key 求值，得到 326/326 指令参数/有效 flags 和 360/360 `CompleteStatic` 函数 signature。[M0-DIA-07 归属证据](generated/dialect-ownership-evidence.json) 又以锁定 hash 的上游源码对照全部 686 个公开键：指令/函数上游同名为 284/243，显式当前模块候选 28，仍有 131 项 `Unresolved`，并记录 8 项当前 target 与上游候选冲突。[M0-DIA-08 名称查找契约](generated/dialect-name-lookup-contract.json) 再固定当前旧 runtime 的 326/360 注册、9 个跨表碰撞、351 个函数投影和 677 项指令 lookup surface，并记录 `ICVariable` comparer 的静态捕获、`ICFunction` current-culture `ToUpper` 风险以及 `_Rename.csv` 的词法分析前 `SourceTextRewrite`。这只使 686 项当前 lookup contract 获得静态证据；684+2 的 key domain 不证明 Unicode/culture 兼容，全部 686 项 semantic alias/replacement 仍未决，completion/effect 和行为 fixture 仍为 Uncovered。DIA-08 的静态报告保持 `currentRuntimeIsolation=Failed`、`parserVmConsumption=NotConsumed`；该状态只表示静态报告本身未接线到 legacy Parser/VM 行为。独立 startup/parser 已消费窄 `CompatibilityPlan` descriptor presence/ownership guard，验证 plan identity/profile/hash 与 legacy descriptor presence/module ownership，但不替换 legacy handler 或执行 typed policy；D1 plan behavior 与 D2 runtime switch 尚未实现。因此状态仍是 `Documented / BlockedEvidence`，不能宣称已支持任意魔改解释器、ownership 已关闭或 D2 已完成。

M0-DIA-09 [模块可见性证据](generated/dialect-module-visibility.json) 再将 DIA-07 的 131 项 ownership 未决按 DIA-02 静态投影机械分区：123 项为 `V24VisibleCandidate`，8 项为 `SnakeOnlyCandidate`（指令 8/6，表达式函数 115/2），且没有未出现在 Snake 投影的未决 key。它保留每项 current target/contribution 与 DIA-08 lookup contract，因为 `CALLSHARP` 或 TOOLTIP 等 Snake-only 可见项的旧贡献文本也可能来自 v24/common；因此可见性不能倒推 owner、AliasOf、ReplacementDeclaration 或行为兼容。DIA-09 的 catalog/visibility hash 分别为 `4fffa254...d9574e`/`bc2b5470...bfdf`，并继续保持 ownership=`Unresolved`、runtime isolation=`Failed`。静态报告本身仍 `parserVmConsumption=NotConsumed`；该状态只表示静态报告本身未接线到 legacy Parser/VM 行为。独立 startup/parser 已消费窄 `CompatibilityPlan` descriptor presence/ownership guard，验证 plan identity/profile/hash 与 legacy descriptor presence/module ownership，但不替换 legacy handler 或执行 typed policy。D1 plan 与 D2 runtime switch 仍未实现。

M0-DIA-10 [会话计划静态预检](generated/dialect-plan-preflight.json) 只把上述 DIA-02 测试投影与 M0-SES-01 root-state inventory 组合成可复现的离线证据 DTO：`v24pure` 固定映射 `V24Pure`/`v24`=290/358，`snake` 固定映射 `Snake`/`snake`=326/360。其 `planSemanticHash` 仅覆盖 profile、legacy enum、投影 id、投影 canonical hash、选择模块和计数，故不受 selection source、requested generation 或生成时间影响；`preflightSetHash` 则保留完整来源与 catalog 证据。`SnakeModernMobile` 明确为 `Uncovered`，不能从 Snake 名称或 marker 推断等价语义。该报告仍为 source-only：`currentRuntimeIsolation=Failed`、`parserVmConsumption=NotConsumed`、`compatibilityPlanRuntime=NotImplemented`、`m1Eligibility=Blocked`，没有创建运行时 `CompatibilityPlan`、`LegacySessionFacade`、feature flag、resolver 或 D2 frozen registry。

M0-DIA-11 [旧 profile 选择证据](generated/dialect-profile-selection.json) 把当前 `Program.DetectCoreProfile` 和启动器/runner 的真实静态选择面固定下来：空 `ExeDir` 先返回 `V24Pure`；随后 launcher 的 `snake` 覆盖 marker；接着 modern marker 产生 `SnakeModernMobile`，legacy marker 产生 `Snake`，最后默认 `V24Pure`。`FirstWindow` normalizer 与 M0 runner 都只支持 `v24pure`/`snake`，未知 launcher 值回退 v24；因此 `SnakeModernMobile` 必须同时标为 marker-only、DIA-10 `Uncovered` 与 runner `Unsupported`。这不是运行时 resolver、内容 fingerprint、manifest、pin 或 capability negotiation；报告不访问游戏目录，也继续保持 runtime isolation=`Failed`、Parser/VM=`NotConsumed`、runtime plan=`NotImplemented`、M1=`Blocked`。

M0-DIA-12 [CompatibilityPack 静态声明证据](generated/dialect-compatibility-pack.json) 把上述未来 manifest 边界进一步落为 versioned、fail-fast 的离线 catalog：它以 DIA-10/11 的 set hash 钉扎 `emuera.compatibility-pack/v1`，目前仅允许 `v24pure`→内置 `gemuera.v24`、`snake`→内置 `gemuera.v24`+`game.snake`。DIA-02 的 `legacy.current.common/expression` 仍只是 static projection support，不能伪装为正式 distribution module。每份 pack 必须显式声明 required/optional capability 数组；M0 尚无 runtime capability 证据，故两者均为空，content binding=`NotBound`、save/fixture=`Uncovered`、distribution eligibility=`Blocked`。catalog 会拒绝未知 module、`SnakeModernMobile`、DLL/assembly/type/script/path/URL 等可执行载荷及陈旧来源 hash；它不读取游戏目录或包文件，不计算 fingerprint、不建立 user pin、不实现 manifest parser/resolver/runtime `CompatibilityPlan`/D2 frozen registry，也不接入 Parser/VM。

M0-DIA-13 [声明词汇表静态证据](generated/dialect-declaration-vocabulary.json) 继续把“未来接口的名字从哪里来”锁在 D0 分类事实内：当前 DIA-01 的 30 个 classification、97 个 branch hit 被规范化为 10 个 `BehaviorKey` 与 4 个 `CapabilityId`，每项固定 source classification、target module、fixture 和命中数。所有名称均是 `StaticCandidate`，当前 `CompatibilityPack` eligibility=`NotEligible`、runtime=`NotImplemented`；工具还反向断言 DIA-12 的 allowed/required/optional capability exposure 均为空，避免把静态名字误当已经绑定的平台/脚本能力。这不是 `Dictionary<string, object>` policy manager、module registry、manifest allowlist 或语义兼容结论；任何 value/default/error/completion/effect 与 runtime binding 都必须由 schema 评审、两侧 fixture 和后续 D1–D3 阶段单独关闭。

M0-DIA-14 [行为键消费边界静态审计](generated/dialect-policy-consumer-boundary.json) 只把 DIA-13 的 10 个 `BehaviorKey` 与 DIA-01 的分类 provenance 组合为未来窄消费者的所有权草案：10 条 boundary 各有唯一 `consumerContractId`/`decisionOwner`，并固定 14 个 source classification 与 16 个 source-file binding、current/intended owner、target module 和 fixture。所有 source role 只能是 `DecisionConsumer` 或 `ConfigurationInput`；特别是 `instruction.scoped-variable-registration.v1` 的 `scoped-variable-config-schema` 仅是候选配置输入，而 `scoped-variable-registration-guard` 才能指向 frozen instruction catalog builder。这样将来配置快照不会与 catalog builder 形成 sibling direct call。report set hash 为 `bfeb5396...50bf`，并固定 `BoundaryDraftOnly`/`NotImplemented` 与 DIA-13 空 capability exposure。

这不是任何 policy interface、policy manager、resolver 或运行时计划：DIA-14 不给 policy 赋默认值，不决定错误、completion、effect、时序或脚本结果，也不创建 `CompatibilityPlan`、`LegacySessionFacade`、feature flag 或 D2 frozen registry，更不会接入 Parser/VM。它只能作为未来 schema 评审和两侧 fixture 计划的静态输入，故 `currentRuntimeIsolation=Failed`、`parserVmConsumption=NotConsumed`、runtime plan/policy manager=`NotImplemented`、M1=`Blocked` 继续生效。

M0-DIA-15 [策略接口表面静态契约](generated/dialect-policy-surface.json) 进一步消除了设计草案与机器 evidence 的命名漂移：它以 DIA-14 boundary set hash 和 DIA-13 provenance hash 钉扎 10 个 BehaviorKey 的 future `portTypeId`，分为 6 个 `PolicyDecision`、2 个 `BridgeProjection` 和 2 个 `FrozenCatalogContribution`。这只分配接口名称和 contract family；每项仍为 `InterfaceDraftOnly`、policy value=`Unspecified`、runtime=`NotImplemented`，并保留 DIA-14 的唯一 consumer/owner、source role、模块和 fixture。M0-DIA-15 明确使用 `IPrivateArgumentShapePolicy`、`IStartupFaultPolicy`、`IResourceLazyIndexPolicy` 等稳定名字，替代未对齐的旧文档草案。

它同样不是 C# type declaration、policy manager、resolver 或 runtime plan：方法签名、输入/输出 DTO、default、error、completion、effect、timing 和 script behavior 都等待 schema review 与 v24/Snake 两侧 fixture；没有 `CompatibilityPlan`、`LegacySessionFacade`、D2 frozen registry 或 Parser/VM 接线，runtime isolation=`Failed`、Parser/VM=`NotConsumed`、runtime type/policy manager=`NotImplemented`、M1=`Blocked` 保持不变。

M0-DIA-16 [模块组合静态契约](generated/dialect-module-composition.json) 把 DIA-12 的静态 CompatibilityPack 与 DIA-15 的 future port surface 连接成可重建的最小分发 DAG。它只固定两个 descriptor：`gemuera.v24@1.0.0` 无依赖、无 port；`game.snake@1.0.0` 依赖 `gemuera.v24 [1.0.0,2.0.0)` 并唯一贡献全部 10 个 future `portTypeId`。报告固定 2 个模块、1 条依赖、10 个 port 和 2 个 profile closure：`v24pure` 解析为 `gemuera.v24`，`snake` 按依赖优先顺序解析为 `gemuera.v24, game.snake`。该顺序是稳定的拓扑投影，不信任 catalog 原始枚举顺序；catalog/port/依赖顺序、来源对象后续变异、陈旧 DIA-12/15 hash、未知依赖、重复 port、依赖环或提前 capability exposure 都会 fail-fast。catalog/report hash 分别为 `17853d1b...561c71`/`ff5c38c2...4b89`。

该 DAG 只表明“将来哪些内置模块可组合、哪个模块声明哪个抽象 port”，并不证明版本范围已被 runtime resolver 执行、port 有 C# 实现、内容/存档/fixture 已绑定或任何游戏可分发。DIA-16 不加载 assembly、不反射扫描、不创建 runtime module catalog、resolver、`CompatibilityPlan`、`LegacySessionFacade` 或 D2 frozen registry，亦不接入 Parser/VM；因此 `currentRuntimeIsolation=Failed`、`parserVmConsumption=NotConsumed`、runtime catalog/resolver/plan=`NotImplemented`、M1=`Blocked` 必须继续保持。

M0-DIA-17 [行为 fixture 契约](generated/dialect-behavior-fixture-contracts.json) 将 DIA-13 的 10 个 `BehaviorKey` 与 DIA-15 的唯一 future port 再次交叉校验，但只形成实现前的证据门槛。每个 contract 必须复用其分类已有 fixture ID，并同时要求 `v24pure`/`snake`、baseline/extension/undeclared 三种情形以及 input、observable-result、error、completion、effect 五个 trace facet。它不规定任何 expected result；将来“不产生 completion/effect”也必须作为显式观测记录，不能用缺字段冒充兼容。

DIA-17 严格拒绝 fixture/source hash 漂移、未知/重复 BehaviorKey、提前声明 Captured，以及 policy value、method signature、DTO、assembly/type/path/script/URL 等运行时载荷。其 10 项均为 `Planned`/`Uncovered`/`NotImplemented`/`BlockedByFixture`，report set hash 为 `6f32a360...73eb1`。因此它既不是 C# interface、policy manager、resolver 或 `CompatibilityPlan`，也不提供 Parser/VM 输入、运行时 capability 或多版本分发资格；只让后续 D3 API review 无法绕过两侧行为证据。

进入解释器替换前至少需要：D0 完整库存、D1 会话计划外壳、D2 v24/Snake 冻结注册快照、v24 基线不变性测试，以及一个由显式 `compat/<profile>` 目录路由选择内建 profile 的端到端候选加载报告。
The plan-identity bridge, descriptor-consumption boundary and StackList canary reset added on 2026-07-15 change only the static evidence identities. DIA-01 still records 97 branch hits (parser-boundary references remain classified provenance); the session inventory now has 14 observed central reset sites with inventory hash `a7451ff2...6572b`. DIA-03 through DIA-17 were regenerated in dependency order. Current hashes are pinned in `EvidenceIndex.md` (`DIA-01=a30d210f...ccdc2`, `DIA-03=d347bdd7...2fa9`, `DIA-04=2f9995f8...80e2`, `DIA-05=18f2e9f4...223e`, `DIA-06=16f8ecab...2717`, `DIA-07=a665945d...e749`, `DIA-08=169ca816...f65c`, `DIA-09=bc2b5470...bfdf`, `DIA-10=d5cc089f...256b`, `DIA-11=0960994b...bd71d`, `DIA-12=e9ffd094...8c46`, `DIA-13=766c3f84...98c4`, `DIA-14=beedf972...c6b`, `DIA-15=2213bf38...e012`, `DIA-16=2b7361ad...ea75`, `DIA-17=1fed13e5...73c4`); earlier numeric/hash prose is historical and does not override the generated reports. The current IdentifierDictionary route projection narrows lookup to selected descriptors when a non-empty plan is supplied, while legacy handlers remain the behavior owner.

The subsequent `IdentifierDictionary` route adapter regenerated DIA-08 and DIA-09 once more: current generated hashes are DIA-08 contract `e1f84151...98056` and DIA-09 visibility `755289bb...40ba29`. Earlier `169ca...`/`bc2b...` shorthand remains historical provenance only.
