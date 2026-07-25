# 原版二进制存档协议（1808）

本章是**上游 1808 profile** 的唯一字节级定义。旧 gEmuera/Snake 小数与 VarExt 是另一兼容 profile，必须显式选择并使用自己的 fixture；任何简化格式、MessagePack 或 Godot Variant 序列化都不能替代这些入口。

## 文件名与文件类型

当前 XEmuera 路径生成符号：

| EraSaveFileType | 值 | 路径形式 | 用途 |
| --- | ---: | --- | --- |
| Normal | 0x00 | `save{index:00}.sav` / `save{name:00}.sav` | 完整游戏存档 |
| Global | 0x01 | `global.sav` | GLOBAL 变量 |
| Var | 0x02 | `var_{id}.dat` | SAVEDATA 变量集合 |
| CharVar | 0x03 | `chara_{id}.dat` | 角色集合 |

路径事实来自 `VariableEvaluator.getSaveDataPath*`。目标平台可把根映射到 `user://`，但 basename/slot 兼容规则需 fixture。用户字符串先经 `CheckDatFilename` 等价验证，不能包含路径。

## 公共头

按 `BinaryWriter` 小端顺序：

| 偏移 | 类型 | 值/含义 |
| ---: | --- | --- |
| 0 | UInt64 | 普通 `0x0A1A0A0D41524589`，或压缩 `0x0A50495A41524589` |
| 8 | UInt32 | format version `1808` |
| 12 | UInt32 | `DataCount`；当前常量为 0 |
| 16 | UInt32 × DataCount | 预留 header data |
| 后续 | payload | 普通直接读取；ZipHeader 时 payload 为 GZip stream |

Reader 最小长度检查为 16 字节，只接受两个 header 和版本 1808。目标实现须先校验 DataCount、header 扩展长度和总文件上限，不能照搬先按不可信 DataCount 分配数组的风险行为。

字符串必须复现源码使用 `BinaryReader/BinaryWriter(..., Encoding.Unicode)` 的实际前缀与编码行为；以 fixture 验证 7-bit length prefix、空串、非 BMP/非法 surrogate，而不是另定义 UTF-8。

## payload 公共前缀

四类二进制 payload 都先写：

```text
byte fileType
Int64 scriptUniqueCode       # fixed 8 bytes, not sparse integer
Int64 scriptVersion          # fixed 8 bytes
BinaryWriter string message
```

读取必须比较 requested file type、game unique code 与 `gamebase.CheckVersion`。类型错、异游戏或版本不兼容均不得部分写入当前变量。

## EraSaveDataType

| 类型字节 | 名称 | payload |
| ---: | --- | --- |
| 0x00 | Int | sparse Int64 |
| 0x01 | IntArray | dims + sparse values |
| 0x02 | IntArray2D | dims + row markers |
| 0x03 | IntArray3D | dims + plane/row markers |
| 0x10 | Str | BinaryWriter string |
| 0x11 | StrArray | dims + string/zero markers |
| 0x12 | StrArray2D | 同上，含 row marker |
| 0x13 | StrArray3D | 同上，含 plane/row marker |
| 0x20/21/22 | Map/Xml/DT | 私家扩展；不是原版核心数据类型 |
| 0xFD | Separator | 数据分段 |
| 0xFE | EOC | 单个角色结束 |
| 0xFF | EOF | 文件/变量段结束 |

普通变量记录为 `type byte + string key + typed payload`；Separator/EOC/EOF 无 key。未知类型、非法 marker 或 payload 截断为格式错误。

### profile 冲突警告

旧 gEmuera 当前 reader 定义 `PcFloat/PcFloatArray/2D/3D = 0x04..0x07`，并定义 `Float/FloatArray/2D/3D = 0x20..0x23`；而本工作区上游 XEmuera 将 `0x20..0x22` 用作 Map/Xml/DT 私家扩展。相同字节在不同基线含义不同，属于阻断自动探测的格式冲突。

目标 `SaveCodecRegistry` 必须由 `SaveProfileId` 选择 codec：`Upstream1808`、`GEmueraSnake` 或未来明确版本。选择依据来自游戏 manifest/用户 profile 和经过验证的文件上下文，绝不能“读到哪个不报错就用哪个”。写入沿用该游戏已选择 profile；迁移另用显式转换命令、备份和报告。

M0-SAV-01 [静态基线报告](generated/legacy-save-baseline.json) 已把当前 legacy 源码的 1808 header、四 file type、19 data type、11 sparse marker、读写入口和就地 load mutation 固定为可重建 evidence，并将 `Float=0x20..0x23`/上游 `0x20..0x22` 的冲突显式标为 `StaticConflictObserved`。该报告不读取任何 `.sav/.dat`，因此不能证明上述 byte protocol 已由原始 fixture 复现，更不能将 `v24pure`、`snake` 或任何 game 自动绑定到 `Upstream1808`/`GEmueraSnake`；`automaticSelectionStatus=Unbound`、offset map/round-trip/content binding=`Uncovered` 与 runtime resolver/candidate commit=`NotImplemented` 必须保持。

### 缺失 profile 证据时的用户流程

旧存档没有可信 sidecar、游戏 manifest 或已固定的游戏 hash→profile 映射时，默认行为是**只读预检后阻断加载和写回**，不是自动猜测：

1. 读取公共 header、file type、game identity 和安全上限，不创建可提交的 VariableStore。
2. 如果冲突字节可能属于多个 profile，返回 `SaveProfileEvidenceMissing` 或 `SaveProfileAmbiguous`；不得用“哪个 codec 没报错”排序。
3. UI 显示来源文件 hash、检测到的冲突、两个 profile 的含义和风险，让用户显式选择“按 Upstream1808 打开副本”“按 GEmueraSnake 打开副本”或取消。
4. 用户选择只对当前游戏内容 hash 生效；持久化 pin 必须记录应用版本、codec 版本、来源和时间，并可撤销。
5. 首次选择只做 dry-run 到候选 Store，生成解析摘要和警告；用户确认后才提交会话。原文件始终保持只读。
6. 转换必须是独立命令：先建立校验过的备份，再写新文件/新目录，重新用目标 codec 读取并生成 offset/hash/report；禁止原地覆盖。
7. dry-run 失败、两种解释都失败或结果仍含未解释数据时保持 Blocked，并提供备份恢复/诊断导出，不允许“强制继续并保存”。

可信 manifest 或已审核的 game hash 映射能唯一确定 profile 时可自动选择，但选择依据和 `SaveProfileId` 必须写入报告；用户手动覆盖后仍遵守副本、dry-run 和备份规则。

## 稀疏整数

`m_WriteInt`/`m_ReadInt`：

| 首字节 | 解码 |
| ---: | --- |
| 0x00..0xCF | 该无符号字节的数值 |
| 0xD0 | 后续 Int16 |
| 0xD1 | 后续 Int32 |
| 0xD2 | 后续 Int64 |
| 其他 | `AbnormalBinaryData` |

数组 marker：`EoA1=0xE0`、`EoA2=0xE1`、`Zero=0xF0`、`ZeroA1=0xF1`、`ZeroA2=0xF2`、`EoD=0xFF`。注意 EoD 与顶层 EOF 字节相同，但解析上下文不同。

### 1D

先写 Int32 length。连续零/空字符串以 `Zero + sparse count` 表示；非零整数直接 sparse int，非空字符串以 `String=0xD8 + BinaryWriter string` 表示；末尾写 EoD，尾部全零无需显式 count。

### 2D/3D

先写各 Int32 维度。含非零值的行写值后 `EoA1`；连续全零行用 `ZeroA1+count`。3D 中含非零行的 plane 以 `EoA2` 结束，连续全零 plane 用 `ZeroA2+count`。最终 EoD。

Reader 对目标数组比保存维度小/大时有复制与丢弃逻辑；目标实现必须通过大小变化 fixture 复现，且在任何 `new array` 前执行 checked product 和安全上限。

## Normal payload

`VariableEvaluator.SaveToStreamBinary` 写：公共前缀 → Int64 角色数 → 每个 `CharacterData.SaveToStreamBinary`（各自 EOC）→ `VariableData.SaveToStreamBinary` → EOF → 私家扩展变量段 → 第二个 EOF。

Reader 在公共校验后先设置默认变量/局部默认值与 LastLoad 元数据，再清角色并加载角色/变量。目标不能直接照搬这一就地修改顺序；必须加载到候选 Store，全部成功后提交。

## Global/Var/CharVar

- Global：公共前缀（空 message）→ `SaveGlobalToStreamBinary` → EOF → 可选私家扩展 → EOF。
- Var：公共前缀 → 对显式变量逐一 `WriteWithKey(name,array)` → EOF。
- CharVar：公共前缀 → Int64 角色数 → CharacterData records → EOF；加载成功后把候选角色追加到当前角色集合。

四类 fixture 必须分开，不能用 Normal 样本代替其他类型。

## GZip

ZipHeader 后的 payload 被 GZip 解压。目标实现改为 bounded streaming：每次读取固定 block，累计展开字节、压缩比和 `MemoryReservation`，达到当前设备档位上限或 200:1 即失败。解压完成前不创建 ParsedSave；截断、CRC 错和 trailing data 规则由 fixture 确认。

## gEmuera/Snake 扩展与其他格式

gEmuera 的 float、PC float 和 VarExt 保存域是现有游戏兼容资产，但不等于上游 XEmuera 通用格式。fixture 必须覆盖 RESULTF/float arrays、SparseArray<double>、Map/XML/DT 的 SAVE/GLOBAL/STATIC 域和 profile 冲突拒绝。

ERAS/MessagePack 若实现，使用独立魔数、扩展名、目录和加载命令，状态为 `Extension`。不得自动把 `.sav/.dat` 当扩展格式探测，也不得宣称 XEmuera 可读。

## fixture 字段解释

每个二进制 fixture 配套 offset map：offset、长度、字段、值、源码 profile/符号。覆盖普通/压缩、四 file type、每种 data type、float profiles、边界 sparse、Unicode、VarExt、旧/错版本、截断和冲突字节。只有目标重编码 bytes、重新读取状态和 profile 选择都通过，才标 Compatible。
### M0-SAV-01 bounded round-trip slice

The current evidence tool accepts an explicit profile pin and performs only a byte-preserving read/copy/copy-back in an isolated directory outside the source fixture root. It proves source immutability and deterministic copy hashes; it does not prove semantic save decoding or re-encoding. `semanticRoundTripStatus=Uncovered` and `profileBindingStatus=BoundExplicit` remain visible until real profile-specific fixtures exercise the legacy reader/writer and state commit path.
