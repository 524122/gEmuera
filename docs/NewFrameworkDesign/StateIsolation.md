# 会话事务、重启语义与状态隔离

## 会话状态机

```text
NoSession
Current(A) --switch request B--> Preparing(B,g+1)
Preparing --load/validate success & current generation--> Committing
Committing --> Current(B) --> Disposing(A)
Preparing --cancel/fault/stale--> Discarding(B) --> Current(A)
```

只有 SessionCoordinator 修改 CurrentSession。generation 发布和旧 CTS 取消发生在长加载锁之外；候选各自加载。`SemaphoreSlim`/锁只覆盖最终比较和原子交换的短 commit 临界区，因此后来的 request 能立即取消仍在加载的前一个候选。

## SwitchGameAsync 伪代码

```text
generation = atomic_increment(nextGeneration)
previousCts = exchange(activeCandidateCts, new CTS)
cancel previousCts
candidate = new GameSessionCandidate(defaultConfig, generation)
old = null
try:
  source = await PlatformGateway.OpenSelection(selection, ct)
  await candidate.LoadAndValidate(source, limits, ct)
  if generation != latestGeneration or ct cancelled: stale
  lock shortCommitGate:
    if generation != latestGeneration or ct cancelled: stale
    old = Interlocked.Exchange(Current, candidate.Seal())
    emit session_committed(generation)
  unlock shortCommitGate
finally:
  dispose uncommitted candidate
  dispose old after Bridge detach
```

候选资源、配置、变量、VM、历史都与旧会话隔离。Load/parse progress 只使用 operation id，不可让旧 candidate 更新新 loading UI。

## 当前 M1 外壳实现边界（2026-07-14）

`src/Core/Session/SessionCoordinator` 已实现上述流程的前半段为 two-phase lease：`PrepareSwitchAsync` 只构造候选，`SessionSwitchLease.CommitAsync` 才在短 gate 内改变 `Current`。`LegacySessionFacade` 在候选与 commit 之间持有单独的 legacy backend gate，执行“停止旧 `EmueraThread` → 启动候选后端 → commit”；后端启动失败、调用方取消或 generation stale 时，候选不会成为 `Current`，且 façade 会尝试恢复前一后端。Core smoke 已覆盖普通 A→B、A→B→A、启动失败、取消、stale completion 与 backend generation 对齐。

这只是对旧进程级运行时的事务壳：`GlobalStatic` / `Program` 仍可变；本轮已将 immutable `CompatibilityPlan` 的 profile+canonical hash 身份 fail-closed 绑定到 startup/parser 边界，并在 legacy `IdentifierDictionary` 构建后校验其 instruction/function descriptor registry，缺失 descriptor 会 fail-closed。Parser/VM 仍未替换 handler 或执行 typed-policy 行为。`LegacySessionBackend` 仅把 reset/start/stop 收口在 Godot bridge；它没有把变量、资源、View、输入、保存或所有 static cache 移进 `GameSession`。显式 runner 已在首等待、无输入条件下观察到跨游戏/profile A→B→A 的 A 恢复和两份副本零 mutation，但这不覆盖多次切换、异步 completion、全部 static 或 Android；`[migration] session_isolation` 继续默认 false，M1 保持 `InProgress / Blocked`。

## 配置隔离

每个候选调用 complete defaults factory，再只应用自己的配置。UserPreferences 可跨游戏，但通过只读 copy 注入。缺配置/字段永远不读取旧 GameConfig。

## 状态重置表

| 操作 | 变量/VM | 输入 | Display/CBG | 资源 | 音频 | Config |
| --- | --- | --- | --- | --- | --- | --- |
| RESTART | 当前 frame IP→ParentLabelLine；变量不全清 | 当前指令语义决定 | 保持 | 保持 | 保持 | 保持 |
| 返回标题 | 按 XEmuera system flow fixture | 取消 pending | 按流程清理 | 动态 G 依源码清理 | 按流程 | 同会话 |
| SwitchGame | 销毁整个旧 Session | 全取消/旧结果丢弃 | 清历史/CBG | 全释放 | generation reset | 新 defaults+override |
| QUIT | 应用退出 | 取消 | 释放 | 释放 | 停止 | flush app prefs |
| *_AND_RESTART | 进程/应用重启请求 | 同退出 | 同退出 | 同退出 | 同退出 | app-level 持久设置按策略 |

返回标题的细节不能由 SwitchGame 推导，须从系统流程 fixture 补齐。

SwitchGame 同时销毁旧 PixelStore、ExtendedData、SQLite reader/transaction；VarExt STATIC 是否跨“返回标题”保持由 profile fixture 决定，但绝不跨不同 GameSession。外部 trusted DLL 一旦装入默认 AssemblyLoadContext，不能靠 Session dispose 证明卸载，切换/撤销授权时提示进程重启。

## 回调 guard

输入、资源 decode、保存、音频、文件 picker、导入 copy 的 completion 都含 session generation + operation id。Bridge handler 先检查 generation，再解包/创建 Godot 对象；不匹配只清理 payload。`CancellationToken` 不是 guard 的替代，因为外部 API 可能不可取消。

## 清理顺序

停止新 effect → cancel input/operations → detach View → stop audio → release layout/texture/CBG → finalize save critical section/temp → dispose VM/variables/config。旧会话 Dispose 幂等，异常被聚合记录但不阻止其他资源释放。

## 静态状态审计

Core 禁止可变 static 保存当前游戏。允许 immutable lookup/default schema。第三方/global cache 必须 key 包含 generation/content hash 并有清除策略。架构测试扫描 mutable static 字段，白名单逐项 ADR。

M0-SES-01 已把最中心的旧会话根固化为可重建报告：`GlobalStatic` 14 项、`Program` 17 项，全部带旧 owner、候选 `LegacySessionFacade` owner、跨会话预期、fixture 和迁移阶段。共观察到 14 个中心 reset site：旧 `GlobalStatic.Reset()` 12 项，canary-only `ResetCanarySessionState()` 清除 `ctrlZ`，并在 `UEMUERA_DEBUG` 下清空 `StackList`；479 个直接引用来自 56 个源码文件。`StackList` 的观察仅覆盖 debug canary，baseline runtime 仍是条件编译路径。该范围故意不冒充“所有 static 已盘点”：其它缓存、Godot View、platform/diagnostic state 仍须在 M1 门禁前逐项纳入。报告只读且 `currentRuntimeIsolation=Failed`，不能把该 inventory 本身当作 runtime generation guard、A→B→A 隔离或 façade 已实现的证据；当前 `src/Core` 的独立合同、最小 façade 和默认关闭 canary 仍须由各自 smoke/runner/真实游戏报告验证。

M0-DIA-10 仅把该 inventory 作为离线方言预检的会话证据摘要；它没有把状态移入对象，也没有构造 session。startup/parser 现在绑定并校验 plan identity，并在 legacy registry 建成后消费/校验冻结 descriptor 的边界快照，但尚未替换 Parser/VM handler 或执行 typed policy。即使 `v24pure` 与 `snake` 的静态投影都有独立 semantic hash，也不能关闭 M1：最小 runtime façade、generation guard、A→B→A、stale completion 与 rollback 的 deterministic Core smoke，以及默认关闭 canary 已接线，但真实游戏静态隔离、真实 canary rollout、完整 rollback 报告、descriptor 行为替换、policy consumption 与 Android 证据仍缺失，`m1Eligibility=Blocked` 必须持续。

## 验证

- A/B/C 快速切换，只有最后允许提交。
- B parse 失败，A 完整保留。
- commit 前/后取消的确定结果。
- picker/decode/audio/input/save 迟到结果。
- B 缺配置字段恢复 default。
- 100 次切换后无任务、事件、Node、texture、audio 泄漏。
- RESTART/返回标题/SwitchGame/进程重启状态表差分。
