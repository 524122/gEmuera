# M1 Core Runtime Contract Slice

Latest route-boundary refresh (2026-07-15): after descriptor validation, `IdentifierDictionary` builds immutable instruction/function projections from the bound plan and parser lookup uses those projections for non-empty descriptor registries. Missing legacy handlers fail closed; empty plans preserve the baseline path. This routes to existing legacy handlers rather than replacing them or executing typed policy. Current post-plan-binding stress remains the authoritative lifecycle observation: semantic `fed09c96...e98cf`, zero primary/alternate fixture mutations, and working-set peak about 667 MB; memory, late completion, input-in-switch, full static roots, Android and parent M1 approval remain open.

Latest follow-up: `SpriteManager.ForceClear` now increments the async texture epoch, waits up to two seconds for active decode workers to quiesce, and only then drains completed image payloads. This closes a cross-session worker race; the real four-switch regression still reports approximately 23/505/553 MB at the first v24 checkpoint and 630/1202/1257 MB at the final v24 checkpoint (managed/private/working set), so the dominant Snake growth remains unresolved. Latest artifact: `C:\Users\Han\AppData\Local\Temp\gemuera-m1-async-quiescence-b858e5099ddb418d92bddc30b8e7120d`.

Follow-up: canary stop now resets `Program` session paths/profile/analysis/reboot state and the residual `GlobalStatic.ctrlZ` state. Runner-owned startup/default-log sinks intentionally remain process-scoped; clearing them during a switch caused an observed alternate-fixture mutation and was corrected before the passing rerun. Latest four-switch artifact: `C:\Users\Han\AppData\Local\Temp\gemuera-m1-program-reset-fixed-e43da0351dcc499d85c5f808ff8cf086`; semantic and mutation checks pass, while the memory curve remains materially unchanged.

Historical pre-plan-binding stress observation (2026-07-15): the explicit cross-ABA runner completed `SwitchCount=100` (101 sessions) with Godot 4.7 Mono, Rikaichan `v24pure` and eraFL `snake`, isolated game copies, and no input replay. Semantic reports were consistent (`f6fa87b37e80507e33c01d8f05ec303ce0f48aada398fd45c1703cd064ec57e6`), both target fingerprints repeated exactly, and primary/alternate fixture mutation counts were zero. Managed samples stayed in approximately 23-27 MB for v24 and 291-295 MB for Snake; working-set samples ranged approximately 323-663 MB and 642-666 MB respectively. The stable Snake working-set floor is still a release-budget risk, so this is observation evidence rather than a passed leak gate. Artifact: `C:\Users\Han\AppData\Local\Temp\gemuera-m1-cross-stress100-39ac37822d294295af9fa7285a0fee2e`.

## Bounded sub-scope: M1-IDENTITY-01

`M1-IDENTITY-01` is complete for its narrow boundary: `LegacySessionBackend.StartAsync` binds the committed immutable plan before launch resolution/start, failed startup clears that plan, `Program` rejects unsupported profiles or a different canonical hash, and `ParserMediator` rejects a parser plan whose profile or canonical hash does not match the startup-bound plan. `Test-InProcessSessionCycle.ps1` covers these ordering and fail-closed checks; the solution and Godot builds pass.

This sub-scope now includes a narrow descriptor-consumption boundary: after `IdentifierDictionary` builds the legacy instruction/function registry, `ParserMediator` validates every descriptor selected by the bound plan and records an immutable consumption snapshot; missing descriptors fail closed. It still does not claim descriptor handler replacement, typed-policy execution, full static-root isolation, memory-budget clearance, Android evidence, or parent M1 gate approval. Parent M1 remains `InProgress / Blocked / PreviousGate:M0`.

## 2026-07-15 Canary session cleanup boundary

`LegacySessionBackend` now tracks whether the active backend was started through the canary route. Canary stop is separate from `StopLegacyBaselineAsync`: after `EmueraThread.End()` confirms worker quiescence, it requires the Godot main thread, clears `EmueraContent` session view state (HTML, CBG, audio, fonts, GraphicsImage textures), unloads legacy content, clears resource lookup state, then clears `SpriteManager` and `GlobalStatic`. The M0 baseline stop path is unchanged. `SpriteManager.ForceClear` does not force a full GC; ownership release must remain observable rather than being hidden by a collection pause.

TDD evidence:

- RED: `Test-InProcessSessionCycle.ps1` failed because the canary lifecycle state and cleanup boundary were absent.
- GREEN: the contract now checks canary-only routing, main-thread ownership, worker-before-view ordering, texture-after-pin ordering, and the no-forced-GC rule. `dotnet build gemuera-c#.sln -c Debug --no-restore` succeeds with the existing 21 legacy warnings and no new errors.

Real runner evidence (Godot 4.7 Mono, Rikaichan `v24pure` <-> eraFL `snake`, `SwitchCount=4`, display server, `RepeatCount=1`) passed semantic fingerprints and both fixture mutation checks. Artifact: `C:\Users\Han\AppData\Local\Temp\gemuera-m1-cross-cleanup-5677c3e1e0df4a60aad55eb88a85cbb8`.

The memory gate remains blocked. Managed/private/working-set samples were approximately 23/504/554 MB at v24 session 1, 313/822/876 MB at snake session 2, 325/837/892 MB at v24 session 3, 614/1186/1241 MB at snake session 4, and 627/1198/1251 MB at v24 session 5. Handles and threads stayed flat (about 763 and 48), semantic fingerprints stayed stable, and fixture mutations stayed zero, but each snake activation still retains roughly 290 MB. This is evidence of an unresolved legacy static/resource root, not a passed leak budget. M1 remains `InProgress / Blocked / PreviousGate:M0`; M2-M7 remain blocked.

状态：2026-07-14 已从纯 .NET 合同切片推进到旧运行时的最小会话外壳；M1 仍为 `InProgress / Blocked`，不能据此关闭 M1 或放行 M2。

## 与迁移阶段的关系

| 范围 | 当前状态 | 尚不代表什么 |
| --- | --- | --- |
| `GEmuera.Core` 合同与多解释器边界 | 已实现，`net8.0;net10.0` 均可构建 | 未迁移 Parser/VM 语义或第三方模块加载 |
| 内置 profile→module 根目录 | 已实现，编译期 allowlist | 不是内容 fingerprint/manifest resolver，也不加载外部程序集 |
| `LegacySessionFacade`、generation 与旧后端激活顺序 | 已接线，仍缺真实多会话隔离证据 | 未消除 `GlobalStatic` / `Program` 的进程级状态 |
| Godot `EmueraMain` 启动/退出桥接 | 已接线到 `LegacySessionBackend`，`migration.session_isolation=false` 默认保留 M0 路径 | 不是新的 Godot 场景树、DTO、资源或输入架构 |
| M2 Display DTO 边界 | `NotStarted / Blocked / PreviousGate:M1` | 不允许切换显示数据路径 |

## 已实现的合同与事务

- `DialectModuleCatalog` 显式组合可信内置 module，校验依赖 DAG、语义版本范围与重复 module。
- `CompatibilityPlanBuilder` 构建冻结的 instruction/function registry、typed port、capability 与 save-profile 快照，并产生稳定 hash。
- `CompatibilityProfileCatalog` 将 launcher 已选择的 `v24pure`/`snake` profile 映射为可信内置 module 根；定义、集合和 lookup 均不可变且 fail-fast。`CompatibilityPlanBuilder` 冻结 module catalog，`LegacySessionFacade` 冻结 profile catalog，因此 application composition 后不能再追加 module/profile 或通过后到 contribution 改写计划。它只替代 façade 内部的硬编码默认映射，未知 profile（含 `SnakeModernMobile`）仍拒绝，显式调用方 module 仍可覆盖默认根用于受控测试/组合。
- 内置 `game.snake` 现在声明 DIA-15/DIA-16 审计出的 10 个 port type id；这些仅是声明，不含 policy 值、C# policy 实现或 legacy Parser/VM 绑定。
- `SessionSelection` 将 module、port、capability、save profile 作为不可变会话输入，避免 bridge 接收后静默丢弃。
- `SessionCoordinator.PrepareSwitchAsync` 返回候选 lease：候选加载在 commit gate 外进行，只有 lease 激活成功后才能 `CommitAsync`；新 generation 会取消/拒绝旧 lease。
- `LegacySessionFacade` 在独立 backend gate 中执行“停止旧后端 → 启动新后端 → 短 Core commit”。启动失败、取消或 stale completion 会停止候选后端并恢复旧后端和旧 `Current`；长操作不占用 Core commit gate。
- `Scripts/GodotHost/LegacySessionBackend.cs` 是旧 `GlobalStatic.Reset` / `EmueraThread.Start/End` 的唯一桥接收口点；`EmueraMain` 在启动时快照 `[migration] session_isolation`：默认 false 直接复用 M0 bridge，true 才经 `LegacySessionFacade` canary。诊断配置快照记录该值，热重载不会切换已运行会话。
- 显式 `legacy_runner.tscn` 可从 `sessionIsolationMode=baseline|canary` 配置在 `main.tscn` 创建前覆盖该启动快照；普通启动链不读取该字段。`Invoke-SessionIsolationCanaryBaseline.ps1` 固定 baseline→canary→baseline 三个**独立进程**，逐 run 检查 only-canary 的 `M1_SESSION_ISOLATION_CANARY` 诊断并比较 semantic hash；它是默认关闭回退/canary 启动证据，不能冒充真实同进程 A/B/A。
- `inProcessSessionCycle=aba` 是更窄的 runner-only 同进程探针：它仅在 canary、无输入重放的 `legacy_runner.tscn` 中调用 `EmueraMain` 的 internal restart seam，连续执行两次 `LegacySessionFacade` switch，并要求同一已配置游戏三次首等待的逻辑指纹一致。`Invoke-SessionIsolationInProcessAba.ps1` 会验证两次 commit generation、三次指纹、隔离 fixture 的零改动和 repeat semantic hash；普通启动和旧 Parser/VM 都不使用此入口。
- `inProcessSessionCycle=cross-aba` 将该观察扩展为 runner-only A(game/profile)→B(game/profile)→A：`LegacySessionLaunchRegistry` 在 `main.tscn` 进入树前冻结 host allowlist，Core 只收到 opaque game id/profile，`LegacySessionBackend` 才解析路径。canary 停止在 worker quiescence 和 Godot view/resource 清理后，统一重置已确认的配置、内容、parser、路径索引、预加载、音频/输入、Timer、lazy-loading memo 及 render queue；`res://` 编码映射等进程级不可变目录刻意保留。它仍不声称已清空所有 static。`Invoke-SessionIsolationInProcessCrossAba.ps1` 要求 generation 2/3、首尾 A 指纹一致和主/备用副本均零 mutation，B 的指纹可以不同。
- canary reset 还会清除 `KeyMacro` 的游戏宏、`GETKEYTRIGGERED` 的 toggle 数组，并将旧 `Config` projection/font cache 恢复为默认值；宏文件路径在保存时动态解析当前 `Program.ExeDir`，不再由首次类型初始化锁定。
- `ColorMatrixGPU` 的矩阵派生 `ShaderMaterial` LRU 也在 Sprite/视图清理之后释放 cache root；shader catalog 本身保持进程级共享。
- `IErbInterpreterFactory`、`ErbInterpreterKey`、`ErbInterpreterDescriptor`、`IErbInterpreterCatalog`、`ErbInterpreterCatalog`、`IErbInterpreterHost`、`ResumableErbInterpreterHost`、typed step/resume DTO、typed effects 与 `VmCompletion` 保留未来多版本 ERB 解释器入口。Descriptor 固定 engine version/API、支持的 module/version range 与 required module closure；catalog 可并存同一 engine id 的多个精确版本，注册时快照 descriptor，`Freeze()` 返回只读目录。创建 Host 时 catalog 只注入该冻结 snapshot，并验证 Host 回报的 descriptor key/hash；factory 后续可变状态或错误版本的 Host 都会 fail-fast。运行时必须按 `(engine id, engine version)` 精确选择；仅有一个版本时保留旧 id-only convenience，存在歧义即 fail-fast，绝不按注册顺序猜测。catalog 仍按冻结 `CompatibilityPlan` fail-fast。公共 host 只负责 Step/Resume 合法性、generation/operation 校验、effect 序号与等待状态，解释器仍不包含 Parser/VM 语义。

## 已覆盖的行为

`tools/core-contracts` 的无第三方 smoke 覆盖：

- v24/Snake module 隔离、依赖顺序、port 声明、重复注册和 canonical hash；
- profile 目录的输入深复制、空/重复/未知 profile 拒绝、内置 v24/Snake 根声明，以及注入一个新内置 profile 后 façade 的受控组合；
- module contribution collection 的注册期快照、重复 contribution id 拒绝、builder/facade 后的 module/profile 追加拒绝；
- interpreter catalog 的精确 id/version 选择、同 id 多版本并存、冻结 descriptor 注入、factory 变异后的 snapshot 保持、Host descriptor 不匹配拒绝、无版本歧义拒绝和无 Godot 反向依赖；
- resumable host 的预算 yield（每次结果不得超过请求的 instruction/work budget）、input wait、成功/失败 completion、stale completion、终态保护、非法 wait/effect、禁止把内部 `Created`/`Running` 状态暴露给调用方、释放后晚到 async 结果保持 `Cancelled`、未处理 engine exception 的 typed fault，以及 engine id fail-fast；
- prepare/commit lease 的“未提交不改变 Current”和 stale lease 拒绝；
- legacy backend 的 A→B、A→B→A、启动失败回滚、取消回滚、不可取消后端启动完成后的 stale 回滚，以及 committed plan/backend generation 一致性。

## 明确未迁移 / 仍被阻断

- 旧 `Scripts/Emuera` Parser/VM 仍是行为 owner；startup/parser 已 fail-closed 绑定并校验 immutable `CompatibilityPlan` 的 profile+canonical hash identity，且初始化后会校验冻结 instruction/function descriptor 是否存在于 legacy registry 并记录消费快照。它尚未替换 handler、执行 typed-policy 或完成新解释器语义隔离，因此不能声称 v24/Snake 已在新解释器中隔离。
- `GlobalStatic`、`Program`、旧 View、输入、保存、资源、音频、Android 生命周期仍是 legacy 链路；本外壳只收口启动/停止，不证明其余静态状态均已隔离。
- 当前 bridge 的候选构造是同步合同构造；未来接入异步资源加载前，Godot 后端必须具备显式主线程 dispatch，不能把 `ConfigureAwait(false)` 当作线程亲和保证。
- 尚无 content fingerprint/manifest resolver、外部程序集发现、第三方脚本执行或多版本分发实现；内置 catalog 是 allowlisted compile-time 声明。
- `migration.session_isolation` 的默认关闭 canary 已进入启动配置与诊断快照；runner 的同一游戏以及跨 game/profile、首等待、无输入 A→B→A 观察均已可复现，且已补充一次 100-switch lifecycle observation，但仍缺输入中切换、Android APK/device 证据、异步晚到 completion 覆盖、全部 static root 证明，以及由这些报告支持的 rollout 决策。

## 验证记录

- `dotnet build src/Core/GEmuera.Core.csproj -c Release --no-restore -f net8.0`：通过，0 warnings / 0 errors。
- `dotnet build src/Core/GEmuera.Core.csproj -c Release --no-restore -f net10.0`：通过，0 warnings / 0 errors。
- `dotnet run --project tools/core-contracts/CoreContractSmoke.csproj -c Release --no-restore`：通过。
- `tools/core-contracts/Test-CoreArchitecture.ps1 -ProjectRoot .`：通过，Core 未引用 Godot。
- `tools/legacy-runner/Test-InProcessSessionCycle.ps1 -ProjectRoot .`：通过；`M1-IDENTITY-01` 覆盖 bind-before-launch/start、failed-start cleanup、profile/hash mismatch 与 null-plan clearing 拒绝。
- `dotnet run --project tools/core-contracts/CoreContractSmoke.csproj -c Release --no-restore`：通过；`LegacyCompatibilityPlanConsumption` 覆盖空/有 descriptor registry、模块归属、缺失 descriptor fail-closed 与 immutable consumption snapshot。
- `Invoke-SessionIsolationInProcessCrossAba.ps1`（Rikaichan `v24pure`→eraFL `snake`→Rikaichan `v24pure`，SwitchCount=2）：通过；semantic=`78bc8e1a...52ad9`、A 指纹首尾一致、两份副本 mutation=0。该观察仍是 runner-only，不能关闭 M1 memory/late-completion/Android gate。
- `dotnet build gemuera-c#.sln -c Debug --no-restore` 与 Godot 4.7 `--headless --editor --build-solutions --quit`：通过；旧工程已有未使用字段警告和 editor shutdown diagnostic 仍存在。
- `migration.session_isolation=false` 下，M0 display runner 运行 `Emuera.NET 1824+v24+EMv18+EEv55+Rikaichan` 的 v24 首次等待/Controls+Canvas observation fixture：通过；它只验证默认回退路径，不构成 canary 或 M1 放行证据。
- 临时设为 `migration.session_isolation=true` 后，同一 v24 Controls+Canvas observation runner 均 exit 0，诊断明确记录 `M1_SESSION_ISOLATION_CANARY` 和冻结 plan hash；随后已恢复默认 false。该单一启动/退出 smoke 不替代真实 A/B/A、静态隔离或 Android 证据。
- `Invoke-SessionIsolationCanaryBaseline.ps1` 使用同一 v24 游戏副本（每次隔离复制）、`RepeatCount=3` 跑 baseline→canary→baseline：九个 `M0-RUN-01` 子报告均通过且 semantic SHA-256 均为 `74bb121d...05d6f7`；only-canary 三个子报告检测到 `M1_SESSION_ISOLATION_CANARY`，两个 baseline 各三次都没有该诊断。汇总仍为 `InProgress / Blocked / PreviousGate:M0`，且明确只是独立进程 startup/exit 比较。
- `Invoke-SessionIsolationInProcessAba.ps1` 对同一 v24 游戏的隔离副本以 `RepeatCount=3` 执行 canary-only 同进程重启：每次均 commit `generation=2`、`generation=3`，三次首等待逻辑 fingerprint 均为 `490a886c...fef66c`，每 run semantic SHA-256 均为 `04b593ef...cb914`，fixture mutation `changeCount=0`。`trace.json` 的 UI projection transport hash 跨 repeat 仍不同，单列为诊断；该报告明确限制为“同一游戏重启”，不构成不同 profile/game、静态全量隔离、Android 或 M1 gate 通过。
- `Invoke-SessionIsolationInProcessAba.ps1` 对用户提供的 eraFL/Snake 隔离副本以 `RepeatCount=1` 和可视显示服务器执行：两次 commit 为 `generation=2/3`，三次首等待逻辑 fingerprint 均为 `576afa40...cb34d`，semantic SHA-256=`a9a1c62c...eadf41`，fixture mutation `changeCount=0`。此前发现的 `emuera_startup_errors.log` runner-copy 写入已改为仅显式 runner 的报告 artifact/diagnostics ZIP；该观察仍只覆盖同一 game/profile，不能外推为跨游戏/profile 或 M1 gate 通过。
- `Invoke-SessionIsolationInProcessCrossAba.ps1` 对同一 eraFL 根的 `v24pure→snake→v24pure` 以 `RepeatCount=1` 执行：commit 为 `generation=2/3`，首尾 v24 fingerprint 均为 `576afa40...cb34d`，semantic SHA-256=`317be36a...5982a4`，主/备用副本 mutation 均为 0；这验证同一游戏根的 profile route，不代表完整 static isolation。
- 同一 cross-ABA 比较器随后对两个独立游戏根执行 Rikaichan `v24pure`→eraFL `snake`→Rikaichan `v24pure`：commit 为 `generation=2/3`，fingerprint 依次为 `490a886c...fef66c`、`1c8eaac2...1d608`、`490a886c...fef66c`，semantic SHA-256=`527b06b8...41041`，两份副本 mutation 均为 0。首次运行暴露 Snake 默认 `emuera.log` 写入备用副本；显式 runner 现在把仅该默认日志重定向为报告 artifact/diagnostics ZIP，自定义日志路径仍保持旧游戏根限制。该证据仍限首等待、无输入、一次 repeat。
- 对 `eraTW-master-...` 的 Snake profile 尝试已启动为 `CoreProfile=Snake`，但该游戏副本报告资源目录读取异常并在首次等待超时；它是失败/未覆盖证据，不能作为 Snake 兼容通过结论。

## 回退与下一步

当前可回退点是 `migration.session_isolation=false` 的 M0 bridge；跨游戏/profile 的首等待 A/B/A 观察和 100-switch lifecycle observation 已存在，但正式发布前仍必须补输入中切换、晚到 completion、全部 static root 与 Android 报告及 gate decision，才能把 canary 视为可发布路径。下一步应扩展现有 cross-ABA fixture 的切换时机和泄漏采样，并在 M2 前把旧输出复制为 DTO，而不是重写 Parser/VM。
Latest post-plan-binding stress observation (2026-07-15): `SwitchCount=100` / 101 sessions completed with semantic hash `fed09c96f164098a45aa31ca2aa858a15b1c22408fc8fff06a9488fcef6e98cf`; v24 and Snake fingerprints repeated exactly and both isolated fixture mutation counts were zero. Managed samples were about 23-29 MB (v24) and 291-295 MB (Snake); working set reached about 667 MB. Artifact: `C:\Users\Han\AppData\Local\Temp\gemuera-m1-plan-binding-cross-stress100-d5dfae0c043a471eb29d367f3806d6c5`. The binding increment is covered, but memory budget, late completion, input-in-switch, full static roots, Android and gate approval remain open.
