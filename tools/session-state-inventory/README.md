# M0-SES-01 旧会话根状态库存

本工具只读扫描两个旧核心静态根：

- `Scripts/Emuera/GlobalStatic.cs` 的直接静态字段；
- `Scripts/Emuera/Program.cs` 的直接静态字段与带 `private set` 的静态自动属性。

它将每项与版本化 catalog 关联，并记录声明位置、条件编译、`GlobalStatic.Reset()` 的显式清理、直接 `GlobalStatic.*` / `Program.*` 引用和写入候选。报告是 M1 `LegacySessionFacade` 所需的静态输入，不是运行时 facade、`CompatibilityPlan`、frozen registry 或多会话实现。

## 运行

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/session-state-inventory/Invoke-LegacySessionStateInventory.ps1 -ProjectRoot .
powershell -NoProfile -ExecutionPolicy Bypass -File tools/session-state-inventory/Test-LegacySessionStateInventory.ps1 -ProjectRoot .
```

默认生成 `NewFrameworkDesign/generated/legacy-session-state-inventory.json`。工具不启动 Godot、不会读取或写入 Era 游戏目录，也不改动 `Scripts/**/*.cs`。

## 输出和边界

- `globalStaticStateCount`、`programStateCount`：本工具**固定范围内**的发现数量。
- `catalogMappedCount=observedStateCount`：该范围内每个 root state 都有唯一 owner、预期跨会话行为、fixture 和迁移阶段说明；不代表其他静态缓存已完成盘点。
- `ExplicitlyCleared`：只代表在旧 `GlobalStatic.Reset()` 中静态发现赋值或 `Clear/Close`，不代表线程、异步 completion 或真实 A→B→A 隔离已通过。
- `NoLegacyProgramReset` 仍是 M1 前必须保留的缺口；baseline 扫描不包含 canary-only 清理，`GlobalStatic.ResetCanarySessionState()` 现在显式调用 `CtrlZ.ResetSessionState()` 并在 `UEMUERA_DEBUG` 下清空 `StackList`。因此本报告的 14 个 `GlobalStatic` 条目都有中心 reset site，但 `StackList` 证据只适用于 debug canary，其他 static root、运行时顺序和隔离仍需独立观察。
- `inventorySetHash` 包含 canonical catalog、相关源码身份、声明和直接访问位置；catalog 枚举顺序不影响 hash。

当前报告状态必须保持 `executionStatus=InProgress`、`gateStatus=Blocked`、`result=Partial`。只有 M0 报告获批、其余静态状态也完成库存、generation guard 与 A→B→A 回退证据齐备后，M1 才可以开始；本工具不能提前放行 M1。
