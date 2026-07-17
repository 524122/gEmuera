# 已知限制、扩展与迁移指引

本表只列尚未通过证据关闭的项目。严重度：Critical 阻止把文档作为实现/兼容完成依据；High 影响数据/平台可用性；Medium 有降级；Low 文档或体验差异。

| ID | 限制 | 严重度 | 受影响范围 | 当前状态 | 替代/迁移 | 关闭证据 |
| --- | --- | --- | --- | --- | --- | --- |
| L-005 | M1-CORE-01/02、最小 `LegacySessionFacade`、静态 profile 目录和默认关闭 canary 已实现，但不等于 M1 阶段通过 | Critical | 迁移状态判读 | 合同/实验性 runtime shell/default-off canary present；首等待、无输入的跨 game/profile A→B→A 与 100-switch lifecycle observation 已有 / M1 InProgress and Blocked | 只将 Core smoke、bridge build、M0 默认路径 runner 与局部 cross-ABA 作为证据；M0 获批前不发布、不进入 M2；Snake working-set 预算仍需裁决 | 完整 static inventory、输入/资源/音频/保存 stale/rollback、memory budget/Node-RID ledger、旧路径一致性、Android 与签署报告 |
| L-001 | 旧项目声明 Godot 4.7/net8/net9，但未来工具链未锁定 | Critical | 全部 Bridge/API/export | Legacy observed / target Uncovered | 先跑旧工程 baseline，再裁决 ToolchainLock | legacy+target ApiSmoke/export reports |
| L-002 | 完整 Core/Contracts/façade 尚未完成（Core 合同与最小 legacy façade 已存在） | Critical | 架构可实施性 | Core contract + transactional legacy shell present / full isolation Uncovered | 在旧可运行工程内按 MigrationPlan 增量建立；继续保持旧运行链回退 | Core build/architecture/smoke + real-game runtime isolation + legacy fallback |
| L-003 | 无原版二进制 fixture | Critical | 存档兼容 | Uncovered | 暂不迁移唯一存档 | 四类型压缩/非压缩往返 |
| L-004 | M0-DIA 已生成名称、测试快照和 descriptor source；DIA-04/05/06 已得到 326/326 指令参数/有效 flags 与 360/360 函数参数/返回静态解析；DIA-07 仅补充 284/243 上游同名、28 当前模块候选、131 未决与 8 项证据冲突；DIA-08 固定 326/360、9 collision、351 projection、677 lookup surface 及 684+2 key domain；DIA-09 又把 131 未决项静态分为 123 v24-visible candidate 与 8 Snake-only candidate，但这不是 final owner 或 replacement；`ICVariable` comparer 是静态捕获、`ICFunction` current-culture `ToUpper` 仍有文化风险，`_Rename` 只是 SourceTextRewrite；686 semantic alias/replacement、completion/effect/behavior 全 Uncovered，旧 runtime 仍无条件混合注册。静态报告的 `NotConsumed` 仅表示报告本身未接线到 legacy Parser/VM 行为；独立 startup/parser 已消费窄 `CompatibilityPlan` descriptor presence/ownership guard，验证 plan identity/profile/hash 与 legacy descriptor presence/module ownership，但不替换 legacy handler 或执行 typed policy | Critical | 脚本兼容声明/多方言分发 | Static types/flags/provenance/lookup/visibility evidence partial / behavior and runtime isolation Failed | 只将规则、归属、lookup contract 与 visibility candidate 作为设计/测试输入，不把 catalog 接入 Parser/VM | final ownership + explicit D2 comparer/normalizer + 686 semantic alias/replacement + runtime invariance + two-sided defaults/errors/restructure/completion/effect behavior diff |
| L-006 | HTML golden 未建立 | High | 富文本/按钮/错误恢复 | Uncovered | 不宣称显示兼容 | golden/fuzz report |
| L-007 | 旧 Controls/Canvas 已有 eraFL 复杂页 PNG、45/45 hit、44 div、2 src 与 data-only/scroll 基线；plan-binding repeat3 已让两端各 3 次 semantic report 一致、每次 screenshot/hit 均 Captured，但 transport trace hash 每次不同；nested div/srcb/dynamic-map scope 和 Display DTO v2 golden 仍未建立 | Critical | div/srcb/地图/输入/无障碍 | Legacy complex partial / semantic repeat stable / transport repeat unstable / target Uncovered | 保留旧 Canvas+Control fallback；不得把 semantic 稳定或单次 Captured 当作 transport/行为兼容通过 | nested div/srcb/dynamic-map fixture + transport batching root-cause/decision + DTO tree/timeline diff + benchmark ADR |
| L-008 | v24 CJK 样例已有本地截图，emoji/RTL/字体 fallback matrix 仍无证据 | Medium | CJK/emoji/RTL 布局 | Partial | fallback + accessible text | golden screenshot matrix |
| L-009 | Android SAF 未实现/真机测 | High | Android 导入游戏 | Uncovered | 仅 app 内置/受控缓存 | target SDK device report |
| L-010 | iOS picker/security scope 未实现 | High | iOS 导入游戏 | Uncovered | 明示不支持外部导入 | Xcode/device report |
| L-011 | 任意 VM 执行点恢复不支持 | Medium | “随时保存恢复”需求 | Unsupported（原版入口） | 原版变量读档后 SYSTEM_LOADEND/EVENTLOAD | 若扩展则独立 schema/migration |
| L-012 | ERAS/MessagePack 非原版 | Medium | 存档互通 | Extension only | 导出/导入使用独立入口 | 扩展格式 spec/test |
| L-013 | 超 8192 图片处理与当前源码警告存在安全差异 | Medium | 特大图片游戏 | IntentionalDifference(Security) | 可选受限兼容模式 | 安全/兼容 fixture + memory report |
| L-014 | BGM/SFX 音量曲线未差分 | Medium | 音量精度 | Pending | 暂不声称听感/数值一致 | instruction/player fixture |
| L-015 | Core 已有后端启动失败、取消和 stale rollback 的确定性会话测试；音频/输入/资源与真实游戏切换竞态仍未测 | High | 快速切换数据污染 | Core partial / game runtime Uncovered | 不发布多游戏切换 | 真实 game A/B/A + input/audio/resource schedule tests |
| L-016 | 事务 replace 平台能力未验证 | High | 中断写入 | Design only | 保留备份并提示 | crash injection per platform |
| L-017 | 性能数字无硬件基线 | Medium | FPS/内存承诺 | Uncovered | 仅采用设计目标 | release benchmark reports |
| L-018 | 恶意包/fuzz 语料未建立 | High | 安全声明 | Design only | 只导入可信测试包 | malicious corpus report |
| L-019 | 旧可运行工程已有本地 M0 identity/runner/typed trace/fixture manifest/eraFL 首等待及复杂页双后端单次证据，但三层、重复稳定性与 Android baseline 尚未完整归档 | Critical | Snake/eraFL/Android 全部迁移判断 | Local partial / repeat unstable / unapproved | 不开始删除旧路径；upstream、nested/srcb/dynamic-map、APK/真机仍 Uncovered | upstream/v24/SN/FL/AN + stable repeat + APK/device + artifact store + signatures |
| L-020 | PixelStore 尚未实现 | Critical | GGETCOLOR/GDRAW/CBG/Sprite 同步顺序 | Design only | 继续使用 LegacyGraphicsAdapter | pixel hash/return/revision differential |
| L-021 | 主线程 cooperative VM 缺 yieldability 证据 | Critical | UI 卡死与脚本顺序 | Experimental only | 默认保留专用 VM thread | full audit + Android p99 + effect/layout budget |
| L-022 | float/VarExt/SQL/NF/HOTKEY 等旧扩展尚未进入可执行 target runner | Critical | Snake/v24 游戏 | LegacyMapped | compatibility profile 保留旧实现 | GE/SN differential suites |
| L-023 | 外部 DLL 是同进程完全信任代码 | High | 安全、AOT、会话隔离 | Disabled by default | 桌面逐 hash 明示授权；移动不支持 | deny/authorize/fault/restart report |
| L-024 | 上游 private 0x20–22 与 gEmuera float 0x20–23 类型码冲突 | Critical | 存档误读/数据损坏 | Explicit profile required | 禁止自动猜测，写前备份 | two-profile corpus + conflict rejection |
| L-025 | M0-SES-01 已静态固定 central `GlobalStatic` 14 项与 `Program` 17 项 root（14 个中心 reset site：12 项 baseline Reset + canary-only `ctrlZ` 与 `UEMUERA_DEBUG` 下 `StackList`，479 直接引用）；`StackList` 的清理证据仅适用于 debug canary。Core 的 generation/lease、最小 façade、默认关闭 canary、首等待 cross-ABA 与 100-switch lifecycle observation 已接线/观察，但其余 static、双跑和完整真实游戏回退仍未完成 | Critical | 渐进迁移能力 | Root inventory partial / transactional shell + first-wait cross-ABA and 100-switch observation present / runtime isolation Failed | 继续以 M0 report 为输入；M0 获批前不发布 M1 façade 或改写旧 VM | full static inventory + per-stage input/resource/audio/save rollback/线程取消/stale completion/memory budget/Node-RID ledger |
| L-026 | 无 profile 证据的旧存档尚无已实现用户决策/dry-run/转换工具 | Critical | 旧存档迁移与误写风险 | Flow documented / implementation missing | 默认只读阻断；用户显式选择副本；转换前备份 | ambiguity/cancel/wrong-choice/pin-revoke/conversion reports |
| L-027 | Android SAF 与当前外部存储路径尚无并行迁移和回退证据 | High | 现有游戏目录、导入/导出 | Flow documented / device evidence missing | M0–M2 保持旧路径；SAF 后续独立 canary | device import/export/revoke/recovery/rollback |
| L-028 | Display transaction reducer/barrier 尚未从旧行为生成可执行语义表 | Critical | PRINT/REDRAW/WAIT/地图/data-only 顺序 | Table documented / behavior pending | 默认全部结果类保序，仅进度/诊断 latest-wins | queue saturation + FL/SN timeline differential |
| L-029 | M0-DIA-12 的 CompatibilityPack 只验证静态 catalog，不绑定真实内容、存档 profile、fixture 或 runtime capability；当前 capability 显式为空，`v24pure`/`snake` 的分发资格仍为 Blocked | Critical | 未来魔改解释器/多版本分发 | Static declaration contract only / runtime resolver NotImplemented | 只能作为未来 manifest parser/candidate builder 的输入，继续使用旧选择链；不得将 marker 或包声明当作自动兼容授权 | content fingerprint/pin/forgery/ambiguity/capability negotiation + save/fixture evidence + resolver/plan/D2/Parser/VM reports |
| L-030 | M0-DIA-13 的 BehaviorKey/CapabilityId 词汇表只追溯 DIA-01 分类；它不证明任何 policy 默认值、runtime capability、脚本语义或正式 manifest allowlist，且当前 pack exposure 必须为空 | Critical | typed policy/capability 与魔改接口 | Static provenance only / runtime NotImplemented | 只能作为未来 schema 评审和 fixture 计划输入；不得把 `StaticCandidate` 写入运行时包或解析器分支 | two-sided policy/default/error/completion/effect fixtures + capability binding + resolver/plan/D2/Parser/VM reports |
| L-031 | M0-DIA-14 只把 10 个 BehaviorKey 映射到静态 future consumer boundary；14 个分类、16 个文件来源和 scoped configuration input/decision consumer 的分离不等于 policy 已实现或行为已隔离 | Critical | typed policy、冻结 catalog 与多版本解释器接口 | Static ownership draft only / runtime NotImplemented | 只能作为窄接口/schema 与 fixture 计划输入；配置输入不得与 future catalog consumer 横向直接调用 | two-sided policy/default/error/completion/effect/timing fixtures + policy manager/resolver/plan/D2/Parser/VM + M1 session isolation reports |
| L-032 | M0-DIA-15 只分配 future port type/contract family；`InterfaceDraftOnly` 和 `Unspecified` 不等于已经存在的 C# interface、方法签名、默认值或可分发的魔改语义 | Critical | typed policy、Bridge、frozen catalog 与多版本解释器接口 | Static port allocation only / runtime type NotImplemented | 只能作为 D3 实施前的 schema/fixture 输入；不得按 port 名称在旧 Parser/VM 或 manifest 中接线 | two-sided method/default/error/completion/effect/timing fixtures + C# API review + manager/resolver/plan/D2/Parser/VM + M1 isolation reports |
| L-033 | M0-DIA-16 只组合两个静态 module descriptor、依赖边、profile closure 与 port ownership；dependency-first DAG 不等于 module 已安装、版本范围已求解、内容已识别或多版本解释器已可分发 | Critical | 未来模块分发、resolver、frozen catalog 与魔改接口 | Static composition only / runtime module catalog NotImplemented | 只能作为 future manifest/candidate builder、D3 API review 与 fixture 计划输入；不得加载 assembly、反射 type 或让旧 Parser/VM 读取该报告 | content fingerprint/manifest/pin/ambiguity/version-range resolution + save/fixture/capability binding + runtime catalog/resolver/plan/D2/Parser/VM + M1 isolation reports |
| L-034 | M0-SAV-01 只锁定 legacy source 的存档 wire facts；`Float=0x20..0x23`/上游 0x20–0x22 冲突和 `Unbound` 自动选择状态不等于任何真实存档已识别、可读写或已绑定 SaveProfile | Critical | 原版/legacy 存档、SaveProfile、CompatibilityPlan 与迁移 | Static save evidence only / fixture and runtime profile resolution Uncovered | 只能作为 fixture/codec/candidate-store 设计输入；原件保持只读，禁止因 profile、marker 或“能读”尝试自动选 codec 或原地写回 | four file type/normal+gzip/original offset map + profile conflict rejection + baseline/target round-trip + content pin + candidate commit/M1 isolation reports |
| L-035 | M0-DIA-17 已把 10 个 future port 的 source fixture、双侧 profile、证据情形和 trace facet 锁成 machine-readable contract，但没有任何 behavior result；`Planned`/`Uncovered`/`BlockedByFixture` 不等于 policy 可实现或多版本分发已可用 | Critical | typed policy、Bridge、frozen catalog 与魔改接口 | Fixture requirement only / runtime NotImplemented | 仅作为 fixture kit 与 D3 API review 的前置门槛；禁止把 contract 写入旧 Parser/VM、manifest 或 runtime catalog | 每项 v24pure/Snake baseline/extension/undeclared trace，含 input/result/error/completion/effect、C# API review、D2/M1 isolation 与 runtime plan evidence |
| L-036 | M3-M7 已有阶段级设计但不具备实施授权；M0-M2 仍由另一条并行工作线按串行门禁推进 | Critical | 后续 Core/资源/平台/实验/清理 | Planning only / PreviousGate | 只准备合同、fixture 和只读适配器；M0-M2 source/report identity 变化时重新生成后续报告 | M0-M2 gate decisions、对应 M3-M7 evidence/gate/rollback reports |
| L-037 | M3-M7 的 Godot Node/Resource/RID 生命周期、generation detach 和 MemoryBudget 仍是设计约束，不是运行时泄漏/设备证据 | Critical | Session switch、PixelStore、renderer、平台句柄、内存 | Design only / Uncovered | 保留旧 renderer/platform fallback；不使用 GC/queue_free 掩盖引用 | Node/RID/Texture/task/native/GPU/reservation before-after 与 100 次切换报告 |
| L-038 | erafl 作为跨 Parser/VM/Input/Resource/Display/Android 的第二基线尚未完成全量 fixture；单次 HTML/截图不能代表所有魔改兼容 | Critical | eraFL/其他魔改 Emuera、CapabilityPack、typed policy | LegacyBehaviorPending / Uncovered | 按 capability/BehaviorKey 逐项验证；未声明组合保持 Blocked | v24/Snake/FL 双侧及其他魔改组合的 state/error/completion/effect/visual/hit/device 报告 |

| L-039 | M3-M7 工程执行协议只定义未来工单、identity、报告、回退和签署形状；未生成的 package record 或报告不构成任何阶段通过 | Critical | 后续阶段拆工、发布和清理决策 | Documentation protocol only / runtime evidence Uncovered | 前置门关闭前只维护合同、fixture、只读 adapter 和执行协议；不得用模板占位替代差分或设备证据 | 每个获批包的冻结记录、命令 exit code、差分/生命周期/回退/uncovered 报告与独立 gate decision |

| L-040 | 当前项目根尚无独立 tests/test 目录，主项目与 Core 项目未建立 xUnit 或等价测试项目引用；gdUnit4 插件和工具契约脚本不能替代纯 Core 回归层 | High | AI 编辑反馈、Core 重构与 Godot 场景验证 | FastLoop policy documented / infrastructure partial | 先按任务包运行现有定向 Core smoke 或场景套件；逐步建立纯 .NET 单测与 gdUnit4 场景层，不把真实游戏/设备降为单测 | 测试项目、确定性 fixture factory、目标 Core 回归、单场景 gdUnit4、反馈时间与升级门报告 |

## M0-DIA-10 静态预检边界

`M0-DIA-10` 只新增 `v24pure` 和 `snake` 的 source-only profile→legacy enum→DIA-02 projection 预检，分别固定为 290/358 与 326/360。它能防止 catalog 顺序、请求来源或 requested generation 意外改变语义 hash，也会明确拒绝没有独立投影的 `SnakeModernMobile`。这不是运行时 `CompatibilityPlan`、多会话隔离或多版本解释器发布完成：`currentRuntimeIsolation=Failed`、`parserVmConsumption=NotConsumed`、`compatibilityPlanRuntime=NotImplemented`、`m1Eligibility=Blocked` 仍是阻断条件；必须继续取得 M1 façade/generation/rollback、D2 frozen registry、ownership/alias/replacement 和两侧行为 fixture 证据。

`M0-DIA-11` 进一步固定旧选择路径，但不能降低上述门槛：当前 launcher `snake` 优先于所有 marker，modern marker 才会进入 `SnakeModernMobile`，而 launcher/M0 runner 根本不提供该 profile。marker 文件只是旧代码的本地选择线索，不是游戏身份、内容 hash 或可信 manifest；未来多版本 resolver 必须显式处理伪造、冲突、缺失 capability、陈旧 generation 和回退，不得把 marker 当作自动兼容授权。

`M0-DIA-12` 在此基础上增加了版本化 `CompatibilityPack` 声明契约，但仍只验证离线 catalog：`v24pure` 只能声明内置 `gemuera.v24`，`snake` 只能额外声明 `game.snake`；`legacy.current.*` 只是旧测试投影的 support ID，不能被分发。契约拒绝未知 module、`SnakeModernMobile`、DLL/类型/脚本/路径/URL 等可执行载荷及缺失 capability 字段。当前 capability arrays 必须显式存在但为空，内容绑定=`NotBound`、存档/fixture=`Uncovered`、分发=`Blocked`；它不读取游戏目录或 game manifest，绝不是可信 fingerprint、runtime resolver 或可运行的 `CompatibilityPlan`。

`M0-DIA-13` 再把当前 DIA-01 的 30 个分类、97 个 branch hit 固定为 10 个 `BehaviorKey` 和 4 个 `CapabilityId` 的静态词汇表。词汇表只说明“哪些旧源码分支声称需要这个名字、未来由哪个模块消费、对应哪个 fixture”，不说明 policy 的值或行为已经正确；全部保持 `StaticCandidate`、`NotEligible`、`NotImplemented`。工具还会拒绝 DIA-12 将任何候选提前暴露为 capability，因此它既不是 runtime policy registry，也不是游戏 manifest 的 allowlist。

`M0-DIA-14` 进一步固定“未来由谁作最终决定”，而不是给出实现：10 个行为键各只有一条 consumer contract/decision owner boundary，14 个分类和 16 个源文件仍是 DIA-01 provenance。`instruction.scoped-variable-registration.v1` 的配置 schema 只能作为 `ConfigurationInput`，旧 `FunctionIdentifier` guard 才是 `DecisionConsumer`；这禁止未来配置快照和 catalog builder 形成 sibling direct call。所有边界保持 `BoundaryDraftOnly`/`NotImplemented`，DIA-13 的 runtime capability exposure 也仍为空，因此它不能作为 policy manager、resolver、CompatibilityPlan、D2 registry 或 Parser/VM 接线已经完成的证据。

`M0-DIA-15` 只在 DIA-14 之上为后续实现分配稳定接口表面：6 个 `PolicyDecision`、2 个 `BridgeProjection`、2 个 `FrozenCatalogContribution` 各有一个 future `portTypeId`。它修正了 `IPrivateArgumentShapePolicy`、`IStartupFaultPolicy`、`IResourceLazyIndexPolicy` 等名称，但所有 port 都是 `InterfaceDraftOnly`，policy value 必须保持 `Unspecified`。因此它既不是 C# interface/assembly，也不是 policy manager、resolver 或 runtime plan；方法/DTO/default/错误/完成/effect/时序和脚本行为仍须由两侧 fixture 关闭。

`M0-DIA-16` 再把 DIA-12 的静态 pack 与 DIA-15 的 port surface 机械组合为两个内置 module descriptor：`gemuera.v24@1.0.0` 是零依赖/零 port 的基础，`game.snake@1.0.0` 以 `[1.0.0,2.0.0)` 依赖它并声明全部 10 个 port；`v24pure` 与 `snake` 的 closure 仅是稳定的 dependency-first DTO。它不证明 assembly 可加载、semver range 已解析、内容/存档/fixture/capability 已绑定，也不是 runtime module catalog、resolver、`CompatibilityPlan`、D2 registry 或 Parser/VM 输入；这些均继续阻断分发与多会话隔离结论。

`M0-DIA-17` 只把上述 port 的“必须先证明什么”变为可执行静态门：10 个 contract 均要求既有 fixture ID、`v24pure`/`snake`、baseline/extension/undeclared 与 input/result/error/completion/effect。它不携带预期 policy value，也没有捕获任何游戏结果；如果某行为不产生 completion/effect，未来 trace 仍必须显式记录该事实。故所有 contract 继续是 `Planned`/`Uncovered`/`BlockedByFixture`，不得据此新增 C# interface、policy manager、resolver、`CompatibilityPlan`、D2 registry 或 Parser/VM 接线。

## 扩展标记规则

扩展行为必须使用独立名称/格式/入口，在兼容矩阵标 `Extension`，记录开启方式、关闭后的原版行为、对存档/脚本的影响和迁移/卸载方法。Godot 特有 Theme、触摸手势、无障碍 fallback 是表现扩展，不能改变 ERB 结果。

## 用户迁移决策

在上述 Critical 项关闭前，用户不能据此判断现有游戏/存档已可迁移。工具未来应读取 CompatibilityMatrix 报告，按游戏使用的指令、资源、HTML 与存档类型给出：Supported、Partial、Blocked、Unknown，并链接具体限制。

## 同步规则

此表由兼容矩阵状态半自动生成；矩阵 Failed/Uncovered/IntentionalDifference 必须有对应限制，关闭限制必须提供报告链接，不能只删除文字。

评审指出的高风险迁移项使用以下稳定关系，guard 会检查 key 仍存在；新增同类项必须先增加矩阵 key，再增加限制：

| Limitation | CompatibilityMatrix key | 同步含义 |
| --- | --- | --- |
| L-040 | TEST.FAST_FEEDBACK | 局部反馈层尚未完整落地；不能以全量阶段门代替，也不能删除正式验收 |
| L-039 | M3M7.EXECUTION.PROTOCOL | 工单协议只定义证据形状；没有冻结输入、报告和签署仍不能放行 |
| L-024 | `SAVE.PROFILE.FLOAT` | 冲突类型码在 profile 语料通过前不得自动猜测 |
| L-026 | `SAVE.PROFILE.SELECTION` | 无 profile 证据时必须只读阻断、显式副本 dry-run 和备份转换 |
| L-027 | `PLATFORM.ANDROID.STORAGE_MIGRATION` | SAF 与旧外部存储路径并行 canary，真机回退证据关闭限制 |
| L-028 | `DISPLAY.QUEUE_ORDER` | reducer/barrier/背压必须由 timeline fixture 关闭，不由原则文字关闭 |
| L-036 | `M3.CORE.EXTRACTION` | M3-M7 规划不改变 M0-M2 当前实施基线；后续阶段保持 PreviousGate |
| L-037 | `M4.RESOURCE.PIXELSTORE` | Node/Resource/RID/MemoryBudget 约束必须由生命周期/内存报告关闭 |
| L-038 | `ERafl.CAPABILITY.COMPOSITION` | erafl/魔改兼容按 capability 和 fixture 关闭，不能由单一截图或名称匹配关闭 |
| L-040 | `M1.PLAN_IDENTITY_BINDING` | Startup/parser plan identity is now fail-closed, but descriptor/policy behavior remains Uncovered until runtime registry and two-sided fixture evidence exists. |
| L-041 | `M0.SAV.FIXTURE_AUDIT` | Real local save candidates are now hashed and header-classified read-only, but no gzip sample, full offset map, profile binding or round-trip evidence exists. |
| L-042 | `M1.CROSS_ABA_STRESS_REFRESH` | The post-binding 100-switch observation is semantically stable with zero fixture mutation, but the Snake working-set peak is about 667 MB and no leak-budget decision is closed. |
| L-043 | `M0.DISPLAY.REPEAT3_PLAN_BINDING` | Controls/Canvas repeat3 semantic reports, screenshots and hit probes are captured and repeat-consistent, but transport trace hashes differ on every run. Drift is confined to `ui_projection/apply_text_changes` batching caused by frame/96-action/7ms flush timing and worker completion order; nested div/srcb/dynamic-map coverage and signed device evidence remain Uncovered. |
