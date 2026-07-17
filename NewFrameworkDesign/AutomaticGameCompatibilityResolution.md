# 游戏自动识别与兼容计划解析实施设计

## 状态声明

本文是 [DialectExtensionSystem](DialectExtensionSystem.md) 中 **D5 Resolver/UI** 的实施设计，当前状态为 `Partial / D5-1+D5-2 implemented / D5-3 runtime evidence pending`。当前代码已经落地无路径 Core 证据 DTO、eraFL/eraTW 内建规则、Host 固定文件探测、启动器 profile 自动选择，以及 `game.erafl@1.0.0` 的独立 profile/module/typed-port/capability 计划输入；动态模块目录、manifest/pin、能力扫描、缓存、完整 Parser/VM policy consumption 和 Android/APK 证据仍未实现。

当前代码静态声明 `v24pure`、`snake` 和 `erafl` 三个 profile，以及 `gemuera.v24`、`game.snake`、`game.erafl` 三个 module。`erafl` profile 默认选择五个窄 typed policy、五个 capability 和 `gemuera.erafl` save profile；这代表计划边界已实现，不等于所有 legacy handler、双后端显示、资源生命周期和 Android 行为都已完成验证。

当前切片的实际边界是：可靠识别到 eraFL 后自动选择独立 `erafl` profile/module，识别到 eraTW 后选择现有 `snake` profile；未知、冲突、越界或探测失败默认使用安全基线，不会静默猜 Snake。主界面提供默认关闭的高级兼容模式，只有开启后才显示手动 profile 覆盖。Stage 3 能力扫描、content identity pin、完整会话/UI 提交和 Android 异步缓存仍待后续切片。

## 问题与决策

用户选择一个游戏目录后，应用应在内部完成以下工作：

1. 读取少量、受限且不可执行的内容证据。
2. 识别游戏家族或确认无法可靠识别。
3. 选择受信任的内置兼容 module 和 runtime capability。
4. 构建候选 `CompatibilityPlan`，在会话提交后冻结。
5. 只有模糊、冲突或缺能力时才要求用户干预。

“不需要区分对应游戏”指**普通启动时用户不需要手动选择 eraFL/eraTW/Snake 模式**，不等于内部取消游戏身份。resolver 仍需要稳定的内部 `GameFamilyId` 来选择规则、记录证据、隔离缓存和解释诊断；高级兼容模式只提供显式覆盖，不改变 resolver 的默认职责。Parser、VM、Save、Resource 和 View 不得读取该 id 后自行分支；它们只能消费冻结计划中的 module、typed policy 和 capability。

核心决策是：

- `CSV/GameBase.csv` 的游戏代码是首要身份字段。
- 标题或固定脚本锚点必须复核代码命中，避免代码复用或内容被拼包。
- 缺少有效游戏代码时，只有“规范化标题 + 至少两个独立脚本锚点”才可自动选择会改变语义的游戏 module。
- 只有能力特征时，只能启用不替换既有语义的加法 capability；不能据此猜测整个游戏 profile。
- 目录名、可执行文件名、单个版本字符串和全包 hash 都不作为游戏家族主键。

## 两个游戏的已知证据

本次只读分析得到以下事实。计数用于说明特征强度，不作为稳定匹配阈值；游戏更新后文件数可以变化。

| 游戏 | 强身份 | 复核锚点 | 能力特征 | 目标选择 |
| --- | --- | --- | --- | --- |
| eraFL 0.48 | `GameBase.csv` 代码 `9224518`，标题 `eraFL` | `ERB/SYSTEM/NEWGAME.ERB` 中有 `@SYSTEM_TITLE`、`PRINTL eraFL`；存在 `FL_INIT_LOADER`、`FL_USERCOM` 等稳定入口候选 | `CALLSHARP` 见于 3 个文件，DataTable 见于 147 个文件，Map 见于 42 个文件，省略首参数的 `INPUTS` 见于 13 个文件 | 新增独立候选 `erafl` profile / `game.erafl` module，不继续塞进 v24 或完整继承 Snake |
| eraThe World 画蛇添足版 | `GameBase.csv` 代码 `7153`，标题以 `eraThe World` 开始 | `ERB/DIM.ERH` 声明 `eraTW_Version`；配置中存在 lazy loading 设置 | SQL 见于 16 个文件，Hotkey 见于 7 个文件，Lazy/EventLoad 见于 11 个文件；Skia 字体/质量配置见于 4 个文件 | 解析到现有 `snake` profile / `game.snake` module，再按平台绑定 SQL、Hotkey、lazy loading 等能力 |

`GameBase.ScriptUniqueCode` 已被现有引擎写入存档并用于游戏代码一致性检查，因此它比目录名更接近原有内容身份契约。但 `GAMEBASE.CSV` 在原引擎中允许缺失，代码 `0` 又表示通配兼容，所以 resolver 不能把“缺失/0”误当成唯一身份。

eraFL 使用 `CALLSHARP` 只能证明它需要相应指令语义，不能证明它就是 eraTW/Snake。Skia 设置主要是平台/表现配置，也不能证明游戏身份。两类信号都只能作为复核或 capability evidence。

## 所有权与边界

```text
Godot Host / Platform I/O
  GameContentProbe
    -> immutable GameProbeEvidence
          |
          v
Pure Core
  CompatibilityResolver
    + BuiltInGameRuleCatalog
    + BuiltInDialect/Profile Catalog
    -> ResolutionReport + resolved SessionSelection
          |
          v
Session candidate build
    -> frozen CompatibilityPlan
          |
          v
SessionCoordinator commit gate
```

### Host 所有权

`GameContentProbe` 位于 Godot Host/平台组合层，负责：

- 通过受控 content root 读取文件，处理桌面路径、Android 导入缓存和未来 SAF token。
- 限制文件名、文件数、单文件大小、总读取字节数和探测时限。
- 识别允许的文本编码并输出中立 DTO。
- 计算探测文件的内容摘要和缓存验证信息。
- 携带请求 generation；取消或过期时停止继续读取。

Host 不决定 `game.erafl` 或 `game.snake`，不构建语言注册表，不加载 DLL，也不执行 ERB。

### Core 所有权

`CompatibilityResolver` 位于纯 .NET Core，只消费不可变 evidence DTO，负责：

- 用编译期内置、冻结的规则目录匹配证据。
- 处理用户 pin、内置规则、冲突和安全降级的优先级。
- 解析 profile、module 依赖、capability 要求和 save profile。
- 输出稳定、可诊断的 `ResolutionReport` 与 `SessionSelection`。

Core 不接触 `Node`、`Variant`、Godot 路径 API、文件句柄或平台权限。规则不得回调 Host，也不得携带任意正则、脚本表达式、类型名或程序集路径。

### 会话所有权

每次游戏切换创建新的 generation。识别、候选计划构建和后端准备都属于该 generation。只有 `SessionCoordinator` 可提交候选；提交后 `CompatibilityPlan` 深不可变。任何旧 generation 的探测、hash、权限回调或候选构建结果都直接丢弃，不能覆盖当前会话或缓存为当前结果。

## 中立证据 DTO

以下为接口形状伪代码，名称和字段必须在 D5 API review 中冻结后才可进入代码：

```csharp
public sealed record GameProbeEvidence(
    long RequestedGeneration,
    string ProbeSchema,
    string ContentRootToken,
    GameBaseEvidence? GameBase,
    ImmutableArray<AnchorEvidence> Anchors,
    ImmutableArray<CapabilityEvidence> Capabilities,
    ProbeBudgetReport Budget,
    string ProbeFingerprint,
    ImmutableArray<ProbeWarning> Warnings);

public sealed record GameBaseEvidence(
    long? ScriptUniqueCode,
    string? NormalizedTitle,
    string? VersionName,
    string SourceDigest,
    EvidenceReadStatus Status);

public sealed record AnchorEvidence(
    string AnchorId,
    bool Matched,
    string SourceLogicalPath,
    string SourceDigest);

public sealed record CapabilityEvidence(
    string CapabilityId,
    EvidenceStrength Strength,
    int BoundedHitCount,
    bool ScanComplete);
```

约束：

- `ContentRootToken` 是 Host 发放的不透明标识，不是绝对路径。
- `SourceLogicalPath` 只能来自规范化的相对路径 allowlist，诊断层还要截断/脱敏。
- evidence 只保存规范化值、有限计数和摘要，不保存完整 ERB 行或用户内容。
- `ProbeFingerprint` 只用于本轮证据和缓存一致性，不等同于可分发内容的完整 canonical hash。
- `ScanComplete=false` 表示预算耗尽后的未知，不能解释成“能力不存在”。

## 内置规则目录

游戏识别规则由应用编译期注册并在首个会话前冻结。游戏包可以提供 schema 受限的 compatibility manifest，但 manifest 只能请求 allowlist 中已存在的 module/capability，不能新增规则、执行代码或覆盖内置安全限制。

```csharp
public sealed record GameDetectionRule(
    string RuleId,
    string GameFamilyId,
    ImmutableArray<long> AcceptedGameCodes,
    ImmutableArray<TitleMatcher> TitleMatchers,
    ImmutableArray<AnchorRequirement> AnchorRequirements,
    string TargetProfileId,
    ImmutableArray<string> RootModuleIds,
    ImmutableArray<string> RequiredCapabilities,
    ImmutableArray<string> OptionalCapabilities,
    string? SaveProfileId);
```

规则匹配器只允许有限枚举：整数相等、规范化标题精确/前缀匹配、固定逻辑路径存在、固定 token/label 存在。禁止从游戏包读取任意正则，也禁止用目录遍历结果生成可执行表达式。

首批规则草案：

```text
rule builtin.erafl.v1
  candidate when gameCode == 9224518
  corroborate when normalizedTitle == "erafl"
               OR any stable FL anchor group matches
  fallback when normalizedTitle == "erafl"
                AND at least two independent FL anchors match
  resolve profile "erafl"
  select root module "game.erafl"

rule builtin.eratw-snake.v1
  candidate when gameCode == 7153
  corroborate when normalizedTitle startsWith "eratheworld"
               OR anchor "erb.dim.eratw-version" matches
  fallback when normalizedTitle startsWith "eratheworld"
                AND at least two independent eraTW anchors match
  resolve profile "snake"
  select root module "game.snake"
```

`FL_INIT_LOADER`、`FL_USERCOM`、`eraTW_Version` 等锚点需要先固定逻辑路径、label/token 边界和两侧 fixture，不能只做全目录子串搜索。标题规范化仅做 Unicode 规范化、大小写不敏感、已审查的空白/标点折叠；不得把任意相似标题模糊匹配成已知游戏。

## 分阶段探测

### Stage 0：缓存预检

Host 根据 content root token、导入版本和关键文件元数据查询缓存。只有关键文件摘要与规则目录版本均匹配时才能复用；路径相同不是充分条件。用户 pin 还必须绑定真实 `ContentIdentityHash`，不能只绑定路径或目录名。

### Stage 1：GAMEBASE 小文件

只定位大小写不敏感的 `CSV/GAMEBASE.CSV`，在严格大小上限内解析 `代码`、`タイトル`、`バージョン違い認める`、版本名等身份字段。解码器只尝试 BOM、UTF-8 和经 fixture 证明所需的 CP932/Shift-JIS 路径；替换字符、重复关键字段、越界整数和截断都产生 warning。

这一阶段通常足以产生一个规则候选，但代码命中仍要进入 Stage 2 复核。

### Stage 2：固定锚点

只读取候选规则声明的少量固定文件，并使用有界词法/token 匹配确认 label、变量声明或配置键。不得初始化完整 Parser/VM，不解析 include 图，不执行预处理器副作用。

eraFL 重点复核系统标题入口和稳定的 FL label；eraTW 重点复核 `DIM.ERH` 中的 `eraTW_Version` 声明与已审查的系统入口。一个锚点文件缺失只降低证据等级，不自动切换到另一个游戏。

### Stage 3：受限能力扫描

仅在以下情形进入：身份缺失、候选冲突、manifest 请求 capability，或候选 profile 需要确认平台能力。扫描只针对 `.ERB`、`.ERH` 和允许的配置文件，按文件数、单文件字节数、总字节数、命中数和 wall-clock deadline 设硬上限，并支持取消。

能力扫描必须使用有界 lexer/tokenizer，忽略注释和字符串中的伪命中。它可以发现 SQL、Hotkey、lazy loading、`CALLSHARP`、DataTable、Map 或省略参数 `INPUTS`，但结果按下列边界使用：

- 可复用且不替换现有语义的 capability 可以作为候选计划的加法项。
- 需要改变参数解析、错误处理、存档或执行时序的能力只能由已识别 module/typed policy 提供。
- 扫描未完成时不能断言能力不存在。
- 单个 token 命中不能把未知游戏自动升级为 `game.snake` 或 `game.erafl`。

## 置信度与冲突裁决

不使用难以解释的浮点总分。resolver 输出离散、可审计的证据等级：

| 等级 | 条件 | 自动行为 |
| --- | --- | --- |
| `Verified` | 非零游戏代码命中，且标题或独立锚点复核通过，无相反强证据 | 自动选择对应 profile/module |
| `StrongFallback` | 代码缺失/为 0，规范化标题命中，且至少两个独立锚点通过 | 可自动选择，但 UI/诊断记录 fallback 原因 |
| `CapabilityOnly` | 无可靠游戏身份，只有可复用能力证据 | 保持安全基线，只加入 allowlisted 加法 capability |
| `Ambiguous` | 多条规则达到同级、代码与标题/锚点冲突，或 manifest 与内置身份冲突 | 不提交语义变更模块，要求用户选择或 pin |
| `Unknown` | 证据不足且无必要能力 | 使用明确的标准基线候选或停止，取决于启动策略；不得静默猜 Snake |
| `Invalid` | 文件越界、解析异常、非法 manifest、未知 module/capability | fail closed，并显示脱敏错误 |

裁决顺序为：

1. 与真实 content identity 绑定的用户 pin。
2. 受 schema 限制且通过校验的 compatibility manifest 请求。
3. 内置游戏识别规则。
4. capability-only 安全降级。
5. 明确的默认 v24 基线或停止加载。

高优先级来源不能绕过 catalog allowlist、module 冲突、版本范围、平台 capability 或存档 profile 校验。用户 pin 不是“强制忽略错误”；它只固定已受信任的 profile/module 组合。

## 解析结果与冻结计划

```csharp
public sealed record ResolutionReport(
    long RequestedGeneration,
    ResolutionStatus Status,
    string? GameFamilyId,
    string? ProfileId,
    ResolutionConfidence Confidence,
    ResolutionSource Source,
    ImmutableArray<string> SelectedModuleIds,
    ImmutableArray<string> RequiredCapabilities,
    ImmutableArray<string> OptionalCapabilities,
    ImmutableArray<string> MissingCapabilities,
    ImmutableArray<ResolutionConflict> Conflicts,
    ImmutableArray<string> EvidenceIds,
    string ProbeFingerprint,
    string RuleCatalogVersion);
```

成功流程：

1. `SessionCoordinator` 为选择请求分配 generation 和取消令牌。
2. Host 完成 `GameContentProbe`，返回同 generation 的 evidence。
3. Core resolver 生成 `ResolutionReport` 和完整 `SessionSelection`，不修改当前会话。
4. `CompatibilityProfileCatalog` 与 `DialectModuleCatalog` 验证 profile、依赖 DAG、版本、port 和 capability。
5. 候选构建器生成深不可变 `CompatibilityPlan` 和 canonical plan hash。
6. Host 准备 legacy/new backend；缺 capability、存档冲突或后端失败都只销毁候选。
7. 仅 generation 仍为当前且全部门禁通过时提交；提交后 Parser/VM/Save/Resource 只读计划。

resolver 不能返回“先启动 v24、解析到 `CALLSHARP` 后再把当前会话改成 Snake”。运行中改变 module 会污染已建立的函数/指令表和存档策略。若预检遗漏必需能力，应终止候选、补充诊断并以新的 generation 重新解析。

## eraFL 与 eraTW 的模块边界

### eraFL

`game.erafl` 只拥有 eraFL 独有选择器、quirk 和默认组合。`CALLSHARP`、DataTable、Map、div/srcb、动态地图事务、动态 Sprite、虚拟光标等若能被多个游戏复用，应拆为 capability 或窄 typed policy；不能把已有 `game.snake` 全量挂到 eraFL，也不能把 eraFL 的全部需求塞进 `gemuera.v24`。

首版 `game.erafl` 进入 catalog 后仍需关闭以下发布门禁，才能宣称完整运行时兼容：

- `INPUTS ,1` 省略默认字符串的 parse/execute/error fixture。
- `CALLSHARP` 的签名、错误、完成模式和平台边界 fixture。
- 资源根、动态 Sprite 生命周期和 Android 路径 fixture。
- div/srcb、动态地图、data-only patch、viewport 保持的双后端 fixture。
- 未选择 `game.erafl` 时 v24 和 Snake 计划 hash/注册表不变。

### eraTW

eraTW 当前映射到 `game.snake`，因为其内容身份、版本锚点及 SQL/Hotkey/lazy loading 使用与 Snake 兼容线一致。这个映射仍须由 eraTW fixture 固定，不能只因目录名包含 `eratw` 或出现 SQL 就成立。

Skia 字体和质量配置进入 `EffectivePlatformConfig`；只有脚本可查询或会改变脚本结果的设置才进入 `CompatibilityPlan`。SQL、Hotkey 与 lazy loading 分别通过受控 runtime capability/typed policy 绑定，`game.snake` 不得直接操作 Android 文件路径或 Godot Node。

## Android 与缓存预算

Android 是首要目标，识别不能在 Godot 主线程同步遍历整个游戏目录。

- Stage 1/2 由候选加载任务在 worker/专用 I/O 路径执行，只把结果通过有界队列送回主线程。
- 导入游戏时可预计算允许文件的索引与摘要；启动时优先验证索引版本和关键摘要。
- Stage 3 有独立 `MaxFiles`、`MaxBytesPerFile`、`MaxTotalBytes`、`MaxHits`、`Deadline` 和 `CancellationToken`。
- 缓存按 content identity、probe schema、rule catalog version 和应用 build hash 分区。
- 缓存损坏、版本不符或摘要不一致只导致重探测，不得导致错误 profile 复用。
- 内存中不保留完整脚本文本；读取采用流式 buffer，命中后按规则尽早停止。
- 性能报告至少记录 p50/p95、读取字节、枚举文件数、cache hit 和主线程最长阻塞；正式阈值由 eraFL/eraTW Android fixture 实测确定，本文不伪造固定毫秒预算。

桌面目录和 Android 导入缓存使用相同 evidence schema，但 Host 实现不同。未来 SAF URI 只存在于 Host token 内，Core 和诊断报告不得看到真实 URI。

## 安全限制

- 所有游戏目录、manifest、ERB、ERH、CSV 和配置均视为不可信输入。
- 探测期间不执行 ERB、不求值表达式、不加载 DLL、不反射扫描程序集、不访问网络。
- 逻辑路径必须拒绝 `..`、绝对路径、设备路径、目录逃逸和越界符号链接。
- manifest 不接受程序集路径、类型名、回调、脚本、任意正则、任意文件路径或 URL。
- module id、capability id、policy key/value、save profile 和版本范围均来自当前 build allowlist/schema。
- 标题、脚本行和本地绝对路径不写入普通日志；只记录规范化 id、摘要前缀、有限计数和错误码。
- 文件数、字段数、字符串长度、编码尝试、依赖深度和规则候选数都有硬上限并计入 MemoryBudget。
- `ContentIdentityHash` 与 `ProbeFingerprint` 分开；后者不能用于授权、再分发或存档 codec 自动猜测。

## 诊断与 UI

正常自动命中时 UI 只显示已选择的游戏和加载状态，不要求用户理解 profile/module。主界面默认提供“全部游戏（自动识别）”，高级兼容模式开启后才显示手动 profile 菜单。兼容详情面板提供：

- 检测到的内部游戏家族和证据等级。
- profile、module、版本、来源和 plan hash。
- required/optional/missing capability。
- fallback、冲突、安全降级和缓存状态。
- “恢复自动检测”和“按当前内容 identity 固定已审核选择”。

诊断事件建议：

| 事件 | 关键字段 |
| --- | --- |
| `compat.probe.completed` | generation、stage、status、files、bytes、cache、duration bucket |
| `compat.rule.matched` | rule id、confidence、evidence ids、conflict count |
| `compat.resolution.completed` | game family、profile、module ids、missing capability、plan hash |
| `compat.resolution.rejected` | stable error code、stage、rule id、generation、stale/cancelled |
| `compat.pin.invalidated` | content identity mismatch、catalog version、reason code |

日志遵循现有 `RuntimeDiagnosticsConfig`、限流和脱敏策略。不得把完整标题、完整路径、脚本文本或 manifest 原文写入诊断包。

## 实施切片与门禁

| 切片 | 交付 | 前置门禁 | 回退 |
| --- | --- | --- | --- |
| D5-0 证据固定 | eraFL/eraTW 最小 fixture、GAMEBASE/锚点/capability 报告、授权与 hash | D0 库存与 fixture 规则 | 纯报告，无运行时影响 |
| D5-1 Core 契约 | evidence/result DTO、内置 rule catalog、确定性 resolver contract tests | D1 会话 plan 外壳稳定，D2 descriptor/registry snapshot 可比较 | `compat.auto_resolve=false` |
| D5-2 Host Probe | 桌面/Android import-cache probe、预算、取消、编码与路径防护 | Host/Core 边界架构守卫通过 | 保留手动 profile 入口 |
| D5-3 内建规则 | eraTW→`game.snake`；eraFL 规则与独立 `game.erafl` 计划输入已进入 catalog | 对应运行时 handler、policy、capability 与两侧 fixture 关闭 | 单独禁用 rule id |
| D5-4 会话/UI | 自动 selection、prepare/commit、冲突面板、content-bound pin | stale generation、候选失败与 ABA 测试通过 | 回到旧手动选择 profile |
| D5-5 发布门禁 | Android APK、真机、缓存失效、性能、伪造包与存档回归 | M0-M2 及相关 D3 capability 门禁通过 | 默认关闭 feature flag |

不得先实现一个扫描器然后继续在 `Program.IsSnakeProfile`、Parser 或 VM 中添加游戏名判断。D5 的价值是生成现有冻结计划的输入；如果 D2/D3 尚不能表达差异，该差异应留在文档和 fixture 中，而不是回填全局布尔值。

## 验证矩阵

| 场景 | 预期 |
| --- | --- |
| 原始 eraFL fixture | `Verified`，选择 `game.erafl`；若端口/capability/后端门禁未满足，明确报告缺项，不伪装成 Snake |
| eraFL 改目录名 | 结果不变 |
| eraFL 标题有允许的大小写/空白差异 | 由规范化规则复核，结果稳定 |
| 代码 `9224518` 但标题和 FL 锚点均相反 | `Ambiguous`，不自动提交 `game.erafl` |
| eraFL 无 GAMEBASE，但标题 + 两个独立锚点存在 | `StrongFallback` |
| 仅含 `CALLSHARP` 的未知游戏 | 不推断 eraFL/Snake；最多报告 capability requirement |
| 原始 eraTW fixture | `Verified`，选择 `game.snake` 和已满足的能力绑定 |
| eraTW 改目录名/版本字符串 | 身份结果不依赖目录名或单个版本值 |
| 代码 `7153` 但出现 FL 强锚点 | `Ambiguous`，要求审核 |
| 仅含 SQL 的未知游戏 | 不推断 eraTW；只处理 allowlisted SQL capability |
| GAMEBASE 缺失、代码 0、损坏编码、超大文件 | 有界失败或 fallback，不崩溃、不无限扫描 |
| manifest 请求未知 module/DLL/type/path/URL | `Invalid`，fail closed |
| Stage 3 预算耗尽 | `ScanComplete=false`，不得把未知当不存在 |
| A→B→A 快速切换 | 旧 generation 结果全部丢弃，最终 plan 与最后 A 一致 |
| 缓存关键摘要变化 | cache miss 并重探测，不复用旧选择 |
| 用户 pin 对应 content identity 变化 | pin 失效并回到自动检测 |
| v24 未知普通游戏 | 不选择 `game.snake`/`game.erafl`，基线 plan hash 不变 |
| Android APK 首次/再次启动 | 首次受预算探测，再次命中有效缓存；主线程无全目录同步扫描 |

contract test 还必须覆盖规则枚举顺序变化、重复 rule id、重复 game code、相同优先级多候选、大小写文化差异、Unicode 规范化、非法路径、取消、超时、缓存损坏和 deterministic report hash。

## 完成定义

本设计只有在以下条件全部满足后才能从 `NotImplemented` 进入可发布状态：

- D0-D2 所要求的 module ownership、冻结注册表、会话计划和 v24 不变性证据已关闭。
- eraFL 的 `game.erafl` 与 eraTW 的 `game.snake` 均有 baseline/extension/undeclared 两侧 fixture。
- resolver 在相同 evidence、catalog 和 build 下产生相同 report/plan hash。
- 模糊、伪造、缺能力、缓存失效和 stale generation 全部 fail closed。
- 桌面构建、Godot ApiSmoke、Android export、APK 真机首次/缓存启动均有可复查报告。
- 手动 profile 回退仍可用，且关闭 `compat.auto_resolve` 后恢复旧选择路径。
- `CompatibilityMatrix`、`EvidenceIndex`、`KnownLimitations` 和发布门禁状态与实际证据一致。

## 与其他设计的关系

- [DialectExtensionSystem](DialectExtensionSystem.md) 定义 module、typed policy、capability、manifest 和 D0-D6 总门禁；本文只细化 D5 的内容证据与选择流程。
- [Architecture](Architecture.md) 定义 `GameSession`、generation、候选提交和纯 Core 边界。
- [GodotIntegration](GodotIntegration.md) 定义 Host I/O、主线程、信号和平台 port 的组合方式。
- [CompatibilityMatrix](CompatibilityMatrix.md) 记录每项能力当前是 Mapped、Partial、Blocked 还是 Accepted；本文中的目标名称不能提升其状态。
- [SecurityLimits](SecurityLimits.md) 和 [PerformanceOptimization](PerformanceOptimization.md) 提供不可信内容、MemoryBudget、Android I/O 与性能门禁。
- [VerificationPlan](VerificationPlan.md) 和 [AcceptanceTraceability](AcceptanceTraceability.md) 负责把 eraFL/eraTW fixture、APK 与发布证据落为可审计报告。
