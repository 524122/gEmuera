# M7 清理、发布与兼容治理

## 文档地位

M7 不是“把旧代码删干净”的整理阶段，而是经过两个发布周期的零流量、回退可用性和兼容证据审查后，才允许移除已替代路径的发布治理阶段。任何仍被 Snake、eraFL、Android、存档、插件或用户回退使用的路径都不能因为目录看起来重复而删除。

## 进入条件

M7 只能在 M3-M6 每个受影响 work package 都有 `Passed` gate decision 后启动；以下任一条件存在时保持 `Blocked`：

- 仍有旧 generation completion 写入、Node/RID/task/reservation 泄漏或 fallback 差分。
- `CompatibilityMatrix`、`KnownLimitations`、fixture manifest 或发布报告中仍有 `Pending/Uncovered/LegacyBehaviorPending` 的必要能力。
- 任何目标平台、Android 真机、代表性 erafl/魔改游戏或存档 profile 没有可复查报告。
- 回退开关、前一稳定 artifact、旧 runner 或迁移备份不可用。
- 并行 M0-M2 工作线仍在修改对应旧路径，且尚未冻结 source/runtime identity。

## 旧路径生命周期分类

每个待清理文件、类、Node、port、flag、fixture 和资源必须进入 inventory：

| 状态 | 含义 | 动作 |
| --- | --- | --- |
| `Active` | 默认或回退仍使用 | 不删除；继续计量 |
| `CanaryOnly` | 仅候选 flag 使用 | 保留直到实验决定 |
| `ZeroTrafficObserved` | 两个完整发布周期无调用 | 进入删除候选，不能直接删 |
| `RetiredPendingEvidence` | 已关闭入口但报告/资产仍依赖 | 只移除运行入口，保留证据资产 |
| `Removed` | 删除后由稳定 tag 回退 | 归档 hash、迁移说明和复原步骤 |

“零流量”必须来自运行时计数、feature flag report 和代表游戏/平台矩阵，不得只靠静态搜索。反射、配置、旧存档和用户显式 fallback 都算潜在调用，直到有证据排除。

## 清理顺序

1. 冻结上一稳定 tag、toolchain lock、CompatibilityPack、fixture manifest、source/artifact/runtime identity。
2. 在新发布中默认关闭待删路径，但保留可见回退和诊断计数。
3. 运行两个发布周期的目标平台、v24/Snake/eraFL/魔改 fixture、存档、输入、资源、显示和 Android 场景。
4. 若无流量且无差分，先删除注册/入口，再删除 adapter 实现；保留报告、golden、fuzz seed、迁移工具和旧 artifact。
5. 更新 `CompatibilityMatrix` 状态为 `Unsupported` 或 `IntentionalDifference`（如确实不再支持），并在 `KnownLimitations` 写替代路径、影响和回退截止版本。
6. 生成 removal diff、架构守卫、Core/Bridge/Scene/Export smoke、内存压力和恢复报告。
7. 打稳定 tag；在下一周期继续监控 crash、fault、rollback、导入失败和兼容选择。

不允许在同一 change set 同时删除多个未独立证明的旧 subsystem；每个 work package 只删除一个明确 owner 的路径，便于回退和定位行为差异。

## Godot Node、Resource 与原生句柄清理

清理前后必须比较：SceneTree Node 数、orphan Node、signal connection、Callable、Tween、AudioStreamPlayer、Texture/ImageTexture、Atlas、RID、file descriptor、SQLite reader/transaction、Android permission handle 和 background task。

标准退出顺序：

```text
stop new input/effect intake
 -> mark generation stale
 -> detach View/backend and clear Node pools
 -> kill tweens / disconnect signals / cancel tasks
 -> release Audio/Texture/RID/native handles
 -> drain or discard stale completion payloads
 -> dispose GameSession/Core/cache/reservations
 -> verify ledger returns to baseline
```

`queue_free()` 后不得保留外部引用；RID 必须显式 free；Resource 引用在最后一个 consumer 释放前不能驱逐；捕获 lambda、timer、tween、线程和原生 callback 必须有可验证的 disconnect/stop。清理报告要区分 managed heap、native/RSS、GPU、媒体和 Node/handle，不能用 GC 数字替代进程内存结论。

## erafl/魔改兼容治理

M7 不把“当前 eraFL 通过”扩展成“所有魔改都兼容”。每个模块/能力仍由 `CompatibilityPack`、content fingerprint、profile、fixture 和 plan hash 标识。清理候选必须检查：

- 动态地图/Display DTO v2、src/srcb、data-only、scroll intent、hit/accessibility；
- input NF、VirtualCursor、空白区域按钮和 touch/keyboard fallback；
- G/Sprite/CBG revision、相对资源 root、dispose mode、PixelStore 同步读回；
- Float/VarExt/Map/XML/DT/SQLite、存档 codec 和冲突 profile；
- 自定义 instruction/function alias/replacement、未知 markup、错误/完成时序；
- Android 外部目录、SAF canary、后台恢复、低空间和插件拒绝。

若某个魔改只依赖已验证的可复用 capability，可复用实现但仍需独立 profile/fixture 证明组合闭包。任何未声明或未验证的模块保持 `Blocked/Uncovered`，不因静态名称相同而放行。

## 删除包与发布周期定义

M7 的 removal packet、证据包和 gate 规则统一见 [M3M7EngineeringExecution](M3M7EngineeringExecution.md)。单个候选按 M7-REL-01 inventory、M7-REL-02 零流量观测、M7-REL-03 分步删除、M7-REL-04 回退演练、M7-REL-05 发布签署推进；不能把五步压缩成一次删除提交。

一个“发布周期”必须绑定非重叠 artifact version、开始/结束 UTC、目标平台和代表 fixture 覆盖、feature flag/fallback/rollback 调用计数、崩溃/typed fault 查询和报告 hash。两个周期都没有调用，才可将候选从 ZeroTrafficObserved 升为 RetiredPendingEvidence；静态搜索没有引用、日历经过一段时间或单一平台没有流量均不足以证明零流量。

删除前后的 architecture guard、SceneTree/Node/Resource/RID、原生 handle、task、reservation 和存档/SAF journal 检查必须进入同一 removal packet。若回退演练无法在不修改原始游戏包和存档的条件下成功，候选保持 Rejected 或 RetiredPendingEvidence，不能标 Removed。

## 发布与回退

每个 M7 发布报告至少包含：

| 报告 | 必要内容 |
| --- | --- |
| `removal-inventory.json` | owner、调用计数、flag、fixture、删除决策 |
| `compatibility-summary.json` | profile/module/capability、Passed/Uncovered、差异与限制 |
| `lifecycle-memory.json` | Node/Resource/RID/task/native/GPU/reservation before-after |
| `platform-release.json` | Desktop/Android/iOS artifact、设备、恢复/导入/存档结果 |
| `rollback.json` | 稳定 tag、旧 artifact、开关、反向迁移和验证命令 |
| `approval.json` | prepared/reviewed/approved 身份、UTC 时间、报告 hash、失效条件 |

回退必须能在不修改用户原始存档和游戏包的情况下恢复：

1. 前一稳定 artifact 或发布包。
2. 兼容 flag/旧 adapter/旧 renderer/旧 platform path。
3. Save/SAF 转换的备份和 journal。
4. 对应 fixture、输入 trace、source/toolchain/runtime identity。

无法回退的删除只能标 `Rejected`，不能以“新版本已编译”替代。

## 并行工作与变更锁

另一 AI 完成 M0-M2 期间，M7 不可删除或重命名其正在维护的 runner、trace、DTO、旧 renderer、Android path、generated evidence 或 guard 入口。M0-M2 通过后仍需冻结 source manifest 和报告 hash，再重新计算 M7 inventory。任何报告失效都将 M7 状态退回 `executionStatus=InProgress; gateStatus=Blocked; blockerCode=ReportInvalidated`。

## 关闭定义

M7 只有在两个发布周期无回退流量、目标平台和代表游戏报告完整、资产清单签字、兼容矩阵与限制同步、清理后内存/生命周期账本稳定、回退演练成功后，才能将具体路径标为 `Removed`。M7 本身不代表永远不再支持新的 erafl/魔改；新增兼容能力必须重新经过 M3-M6 的模块、fixture、资源、平台和实验门禁。
