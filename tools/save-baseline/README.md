# M0-SAV-01 legacy 存档协议静态基线

该工具只读锁定当前 legacy 二进制 reader/writer、变量和角色读写入口的源码事实。它输出公共头、四个 `EraSaveFileType`、19 个 `EraSaveDataType`、11 个稀疏 marker、读写入口和已观察到的旧就地 load mutation；不打开真实 `.sav/.dat`，不写隔离副本，不读取 `E:\MyCode\Era`，也不改变 `Scripts/**/*.cs`。

## 运行

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\save-baseline\Invoke-LegacySaveBaseline.ps1 -ProjectRoot .
powershell -NoProfile -ExecutionPolicy Bypass -File tools\save-baseline\Test-LegacySaveBaseline.ps1 -ProjectRoot .
```

如需审计外部 Era 游戏库中的现有样本，可单独运行只读 fixture audit（不会更新上述 source-only 报告）：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\save-baseline\Invoke-LegacySaveFixtureAudit.ps1 `
  -RootPath 'E:\MyCode\Era' `
  -OutputPath "$env:TEMP\gemuera-m0-save-fixture-audit.json"
powershell -NoProfile -ExecutionPolicy Bypass -File tools\save-baseline\Test-LegacySaveFixtureAudit.ps1 -ProjectRoot .
```

审计器只以共享读方式计算样本 SHA-256，并记录 1808 header、version、dataCount 和 payload 的 0/8/12/16 偏移；不会调用 legacy parser、解压、写入或覆盖源文件。报告的 `roundTripStatus` 固定为 `Uncovered`、`profileBindingStatus` 固定为 `Unbound`，因此该证据不能替代隔离副本读写往返或 gate 决策。

默认输出为 `NewFrameworkDesign/generated/legacy-save-baseline.json`。catalog 精确锁定五个 source file 的 SHA-256 与 byte length；源码、公共头、enum、marker、入口、profile conflict 或 legacy mutation provenance 漂移时 fail-fast。catalog 枚举顺序和调用方后续变异不得改变 `saveBaselineSetHash`。

## 边界

`Float=0x20..0x23` 的旧源码存在与设计中的 Upstream1808 `0x20..0x22` 冲突会被标为 `StaticConflictObserved`，而不是自动选择 `GEmueraSnake` 或 `Upstream1808`。报告固定 `automaticSelectionStatus=Unbound`，并把 `offsetMap`、真实 read/write round-trip、content/profile binding、runtime SaveProfile resolver、candidate parse/commit 与 M1 session isolation 保持 `Uncovered`/`NotImplemented`/`Blocked`。

后续 M0 fixture 必须对原件只读，并在隔离副本上写入：四种 file type、普通/压缩、profile 冲突拒绝、float/VarExt、offset map 和 baseline read/write 结果都需要独立证据。该工具不是新 codec、`SaveCodecRegistry`、manifest parser、用户 pin、`LegacySessionFacade`、Parser/VM runtime input 或迁移命令。
## Isolated-copy evidence slice

Use `Invoke-LegacySaveRoundTripEvidence.ps1` for a bounded, read-only check after an explicit profile decision:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\save-baseline\Invoke-LegacySaveRoundTripEvidence.ps1 `
  -RootPath 'E:\MyCode\Era' `
  -OutputPath "$env:TEMP\gemuera-m0-save-roundtrip.json" `
  -ProfileId Upstream1808
```

The command copies candidate files to an external isolated directory and copies them once more inside that directory. It records source-before/source-after, isolated, and copy-back SHA-256 values. `semanticRoundTripStatus` is intentionally `Uncovered`; this is not a legacy parser/writer round-trip, profile conflict resolver, or runtime save commit.
