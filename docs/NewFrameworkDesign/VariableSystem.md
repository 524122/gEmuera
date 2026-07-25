# 变量存储、访问与持久化范围

## 事实源

变量基线分两层：上游 XEmuera 定义原版整数/字符串语义；旧 gEmuera `VariableCode`、`EraType`、`VariableData`、`VariableEvaluator` 和 `SparseArray<T>` 还定义 Snake/v24 小数与稀疏扩展。目标必须同时表达，不能把扩展变量从 Core 模型中删掉。

## 存储模型

```text
VariableStore
├─ scalarStrings[]
├─ scalarIntegers[]
├─ stringArrays1D/2D/3D[]
├─ integerArrays1D/2D/3D[]
├─ scalarFloats[]
├─ floatArrays1D/2D/3D[]
├─ characters[] -> CharacterData equivalents
├─ userDefined registry
└─ local/private scopes owned by VmFrame/function
```

`VmValue/EraType` 至少覆盖 Integer、String、Float、Reference、Void。旧 gEmuera 已实现 `#FUNCTIONF`、LOCALF、ARGF、RESULTF、REFF/REFF2D/REFF3D、float private/user variables 和 `SparseArray<double>`；数组统计、排序、REF、VARSET/CVARSET 都必须按类型分派。`VariableCode` flag 不能脱离 descriptor 解读。

## 标准顺序保存

`VariableData.SaveToStream`/`LoadFromStream` 使用四个独立计数：

1. `__COUNT_SAVE_STRING__` → scalar string。
2. `__COUNT_SAVE_INTEGER__` → scalar integer（源码注释说明实际上底层结构需谨慎验证）。
3. `__COUNT_SAVE_INTEGER_ARRAY__` → integer 1D arrays。
4. `__COUNT_SAVE_STRING_ARRAY__` → string 1D arrays。

不得把 integer 与 integer array count 混用。`TFLAG = 0x04 | __INTEGER__ | __ARRAY_1D__ ...`，因此标准保存属于 integer array 范围。TFLAG 的重置时机是独立生命周期问题；没有 fixture 前不写“只有 RESETBH 重置”。

## 1808 二进制保存

二进制 writer 为每项写 `EraSaveDataType` + string key + typed payload。数组写维度并用 sparse markers 压缩零/空字符串。Reader 依据 key 找目标 token；不存在或类型不匹配时的跳过/错误语义必须由 reader 源码和 fixture 逐项覆盖。

加载时先解析为 `ParsedVariables` 候选：

```text
validate type/key → validate dimensions/product → bounded allocate candidate
→ decode payload → validate game invariants → build characters
→ commit by replacing VariableStore root
```

任何一步失败都 Dispose candidate，当前 Store 不变。

## gEmuera 小数与 VarExt profile

旧 gEmuera codec 使用 `PcFloat/PcFloatArray/2D/3D = 0x04..0x07`，并保留 legacy `Float/FloatArray/2D/3D = 0x20..0x23`；这与当前上游 XEmuera 对 `0x20..0x22` 的私家 Map/XML/DT 扩展解释存在直接冲突。读取器不能自动猜测：codec profile 由 manifest/game profile 和可验证 header/context 明确选择，错误 profile 必须拒绝而非错读。

`RuntimeDataStore` 的 Map/XML/DataTable 另按 `VarExt*.csv` 声明的 SAVE/GLOBAL/STATIC 域保存和清理。它们不是普通 float 数组，也不能在读档前全部清空。详细字节布局必须由 gEmuera 实际存档 fixture 固定后写入 SaveFormat。

## 快照一致性

异步保存允许四种实现：VM 安全点暂停并同步复制、深复制、结构共享不可变集合、写时复制。禁止浅复制 `CharacterList`/数组后让 VM 与 serializer 并发访问。

建议分层 snapshot：小型标量直接复制；大数组以 immutable segment/owner token 固定；角色列表复制索引和每角色 segment；保存结束释放 segment 引用。snapshot 创建本身受 work budget，在创建完成前不启动 writer。

## 局部变量与 continuation

函数局部/私有动态变量归 VmFrame 或函数 scope。原版存档不持久化任意执行 continuation，因此不保存当前局部/调用栈并不意味着可以恢复任意点。若未来扩展保存 continuation，必须独立版本、迁移、函数 identity 校验和资源等待恢复。

## 内存与安全

维度乘积用 checked 64 位，先比较[安全上限](SecurityLimits.md)再分配。超大数组操作分块并计 work units。字符串 intern/共享只能用于不可变常量，不让用户可变字符串无限进入全局 intern 表。

## 验证

- 每个保存计数边界前后变量。
- TFLAG 标准保存往返与独立 reset 行为。
- 1D/2D/3D sparse zero/empty 编码。
- key 不存在、类型/维度变化、较大/较小目标数组。
- 角色数量、角色变量和用户定义扩展。
- `#FUNCTIONF` 返回、LOCALF/ARGF 独立调用帧、RESULTF、REFF 元素/数组和 float 1D/2D/3D。
- 上游 private data 与 gEmuera float code 冲突 profile 不会互相误读。
- VarExt SAVE/GLOBAL/STATIC 的 Map/XML/DT 读档清理边界。
- snapshot 期间 VM 修改同一数组，证明无撕裂。
- 恶意负数/溢出维度在分配前拒绝。
