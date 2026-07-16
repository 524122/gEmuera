# 存档/读档事务、恢复与平台 I/O

字节格式只引用[SaveFormat](SaveFormat.md)，变量范围引用[VariableSystem](VariableSystem.md)，安全上限引用[SecurityLimits](SecurityLimits.md)。本章负责操作生命周期和原子性。

## SaveService 所有权

SaveService 属于 GameSession，持有游戏 save root、storage port、操作表和 journal，不是 Autoload。每个操作有 `operationId`、session generation、slot/type、CTS、阶段、temp token 和 result。并发策略：同一 slot 串行；不同 slot 可并行序列化，但平台 replace 受 storage adapter 约束。

## 保存流程

```text
ValidateSlot
 → reach VM snapshot safe point
 → BuildImmutableSnapshot (budgeted)
 → Resolve target under save root
 → Create unique same-directory temp
 → Serialize bounded bytes
 → Flush stream + durable flush where supported
 → Validate temp by reopening/header check
 → Backup/atomic replace
 → Flush directory/journal where supported
 → Complete result and release snapshot
```

取消在 snapshot/serialize 前可直接结束；进入 replace 临界区后执行平台定义的最小不可取消段，然后报告最终状态。任何 `finally` 关闭 handle、删除未提交 temp 并释放 snapshot segment。

## 临时文件和 journal

临时名由 adapter 生成，如 `.<target>.<operationId>.tmp`，用户不可控制。journal 至少记录 target/temp/backup、阶段、expected hash、generation 和创建时间，写入后自身也 flush。不能只凭 `.tmp` 后缀恢复。

## 原子替换策略

| 平台能力 | 策略 | 崩溃后规则 |
| --- | --- | --- |
| 原子 replace + backup | flush temp → replace(target,backup) | target 优先；校验失败回 backup |
| 原子 rename 同卷但无 replace | target→backup；temp→target；每步 journal | 根据阶段和 hash 选择完整文件 |
| content URI 无 rename | 写 app-owned `user://` 主存档；导出 URI 是副本 | 主存档不受导出中断污染 |

恢复只接受完整通过 header/limit/hash 校验的候选；如果 target 与 backup 都合法，按 journal generation/时间选择并提示。绝不把半文件拼接或静默覆盖。

## 读取流程

```text
Validate slot/path/token
 → open read-only bounded stream
 → inspect header/version/type without allocation
 → bounded decompress if needed
 → parse to ParsedSave candidate
 → validate game identity/version/variables/characters
 → VM owner-thread generation guard
 → atomic VariableStore replacement or append semantics
 → schedule SYSTEM_LOADEND → EVENTLOAD via current VM runner
```

解析错误、取消、不同游戏、不同版本或 codec profile 冲突只销毁 candidate。Normal load commit 后设置 LastLoad 元数据并进入系统流程；Var/CharVar/Global 按各自行为提交。M2–M5 系统事件可在专用 VM thread 的 legacy runner 执行；主线程实验必须可挂起。

缺少可信 profile 证据时，读取流程在 header/identity 预检后进入 `AwaitingProfileDecision`，不得进入 parse commit。用户决策、只读 dry-run、持久化 pin、备份转换和错误回退遵循 [SaveFormat](SaveFormat.md)；原文件不能被探测流程写入或替换。

当前 [M0-SAV-01 静态基线](generated/legacy-save-baseline.json) 只确认旧 source 中存在二进制头、file type、float code 冲突与就地 mutation 位置；它没有 `ParsedSave`、`AwaitingProfileDecision`、SaveProfile resolver 或真实存档读取。因此该报告只能帮助后续 fixture 和 façade 定位入口，不能降低本章的候选解析、原件只读、generation guard 和 atomic commit 要求。

RuntimeDataStore 的 Map/XML/DataTable 按 VarExt SAVE/GLOBAL/STATIC 域分别形成 candidate：普通读档只能替换 SAVE 域，global 只处理 GLOBAL 域，STATIC 和未声明保存的运行态数据按兼容 fixture 保留。任何域在全部解析验证前都不得就地清空。

## 快照策略

保存请求不直接读取活动数组。VM 在指令安全点创建 snapshot root；大数组采用深复制、immutable segment 或写时复制。后台 serializer 只持 snapshot；会话取消可让它完成清理，但结果无法写回新 generation。

## slot 与路径

Normal 数字 slot 必须在兼容配置范围内；脚本名 slot 通过与 `CheckDatFilename` 差分等价的 validator。文件名由 type/slot formatter 生成。canonical root containment、重解析点和 content URI 规则见 SecurityLimits。

## Android/iOS

Android 游戏包导入经 SAF；活动存档写 `user://`，用户导出再通过 content URI stream，避免对 URI 伪装文件 rename。iOS 同理使用 app sandbox 主存档，document picker/export bridge 处理外部副本。权限/选择只等待真实原生回调，进程恢复使用 operation journal。

## 错误契约

| 阶段 | 错误 | 当前会话可信 | 用户操作 |
| --- | --- | --- | --- |
| slot/path | InvalidSlot/PathEscape | 是 | 选择合法 slot |
| snapshot | SnapshotCancelled/OOM risk | 是 | 重试/降低预算 |
| serialize | Format/Limit | 是 | 报告游戏/存档问题 |
| write/flush | NoSpace/Permission/IO | 是 | 清空间/换位置 |
| replace | ReplaceFailed | 是；旧 target 保留或按 journal | 恢复/重试 |
| parse | Corrupt/Truncated/Bomb | 是 | 选备份/其他存档 |
| validate | DifferentGame/Version | 是 | 选择匹配游戏 |
| commit | StaleGeneration | 是 | 丢弃旧结果 |

## 测试

- 在创建 temp、写一半、flush 后、backup 后、replace 后分别模拟崩溃并运行 Recover。
- target/backup/temp 各种合法/损坏组合。
- 路径穿越、非法 slot、junction/symlink race。
- GZip 超输出/超比率、巨大字符串/维度、截断 marker。
- snapshot 后活动变量持续修改，保存仍一致。
- Load 解析失败不改变任一当前变量。
- Normal commit 后 SYSTEM_LOADEND/EVENTLOAD 可让出并保持顺序。
- Upstream1808 与 GEmueraSnake profile 选择、float code 冲突拒绝和显式转换备份。
- 无 sidecar/manifest、模糊 profile、用户取消、错误选择 dry-run、撤销 pin 和转换后重读。
- VarExt SAVE/GLOBAL/STATIC 不发生越域清理。
- Android/iOS 选择取消、权限失效、空间不足、后台终止和恢复。
## M0-SAV-01 isolated-copy evidence (bounded)

`tools/save-baseline/Invoke-LegacySaveRoundTripEvidence.ps1` requires an explicit `Upstream1808` or `GEmueraSnake` profile pin, reads candidate save files with shared-read access, copies them to an external isolated directory, and performs a second byte-preserving copy-back. The report records source-before/source-after hashes, isolated hashes, wire metadata, profile pin, and `copyRoundTripStatus`.

This proves source immutability and deterministic copy hashes only. `semanticRoundTripStatus` remains `Uncovered`: no legacy parser, decompressor, variable commit, offset map, or codec resolver is invoked. The report remains `gateStatus=Blocked` and does not close M0-SAV-01.
