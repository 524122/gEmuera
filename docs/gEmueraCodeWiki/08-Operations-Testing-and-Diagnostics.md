# 构建、测试、诊断与证据工具

## 运行与平台定位

- 首要产品目标是 Android/手机端；桌面运行用于开发/调试，但不能代替 APK、安装、启动和设备行为证据。
- Godot project 使用 Compatibility/OpenGL ES 3 renderer，`project.godot` 的 main scene 为 `first_window.tscn`。这是低端 Android 图形 API 基线；TW 高输出卡顿仍须按 `PERF.SAMPLE`、`PERF.DISPLAY_BRIDGE`、`PERF.CONSOLE_RENDER` 和 VM/GPU 分项数据验证。`PERF.SAMPLE` 同时带有请求的 renderer 配置、Godot 实际方法/驱动（可用时）和图形适配器身份，方便区分渲染器回退与同一路径的冷/热启动。
- Godot host 在桌面使用 `net8.0`，Android 条件目标使用 `net9.0`；Core 是 `net8.0;net9.0` pure .NET assembly。
- 外部 Era 游戏、存档、APK、设备日志、工具链路径和大报告不属于仓库源码；将它们作为有 identity 的测试输入/证据，而不是提交物。

## 正常开发启动

1. 使用 Godot 4.7 Mono 打开项目根目录。
2. 准备包含 `csv/` 与 `erb/` 的 Era 游戏目录；启动器负责选择路径/profile。
3. 运行 `first_window.tscn`，完成 launcher 选择后进入 `main.tscn`。
4. 调试时注意：启动器阶段（权限、扫描、SafeArea）和 `EmueraMain` 后的 legacy runtime 阶段是两段不同的故障域。

用户向快速开始可参考 [`../../readme/README.md`](../../readme/README.md)；当前实现/阶段真相以 [`../NewFrameworkDesign/DeveloperHandoff.md`](../NewFrameworkDesign/DeveloperHandoff.md) 为准。

## 快速构建门禁

> 如首次运行、修改 `TargetFramework(s)`、项目引用，或清理 `obj/`，先 restore。随后才使用 `--no-restore`，避免旧 assets file 导致误报。

```powershell
# 初次或依赖变化后
dotnet restore gemuera-c#.sln
dotnet restore tools/core-contracts/CoreContractSmoke.csproj

# Godot/C# host
dotnet build gemuera-c#.sln -c Debug --no-restore

# 用 Godot 4.7 Mono 编译 C# solution
& '<Godot 4.7 Mono console executable>' `
  --headless --editor --path . --build-solutions --quit

# pure Core + contract smoke
dotnet build src/Core/GEmuera.Core.csproj -c Release --no-restore
dotnet build tools/core-contracts/CoreContractSmoke.csproj -c Release --no-restore
dotnet run --project tools/core-contracts/CoreContractSmoke.csproj -c Release --no-build
```

### 这些命令证明什么，不证明什么

| 命令 | 有效证据 | 不能推断 |
| --- | --- | --- |
| Godot/C# build | host/Core 当前可编译到对应 target。 | ERB/game compatibility、Android 实机、保存 round-trip。 |
| Core contract smoke | Core public contract 的定向行为。 | legacy runtime 已被替换。 |
| GDUnit4 prototype suite | 特定 Godot Node/sidecar lifecycle。 | 玩家完整游戏循环或 Android capability。 |
| legacy runner trace | 指定 fixture/输入/环境下的 legacy 可观察结果。 | 未覆盖游戏或未绑定设备的通用结论。 |
| export/build | 生成某一 artifact。 | 安装、启动、permissions、SAF 或真机场景全部通过。 |

## GDUnit4：Host prototype 回归

当前仓库中的 Godot regression 入口：

```powershell
& '<Godot 4.7 Mono console executable>' `
  --headless --path . `
  -s res://addons/gdUnit4/bin/GdUnitCmdTool.gd `
  --ignoreHeadlessMode `
  -a test/GodotHost/PrototypeHostRegressionTest.gd `
  -c -rd user://gdunit-review-report
```

覆盖目标包括 prototype Reload、Toggle、Detach 以及 observational input 不消费事件。需要新建/修改 scene 相关测试时，先读 `addons/gdUnit4/ADDON.md`，并让测试 ownership 与待改 Godot node 对齐。

## Core、架构与文档门禁

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/core-contracts/Test-CoreArchitecture.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools/doc-guards/Invoke-DocGuard.ps1
```

- Core architecture guard 检查 Core→Godot 等边界，但其当前契约/target-framework 状态应以工具输出和 `ERBAPI.md` 为准；不要预设它必然绿色。
- Doc guard 检查现有设计文档/链接等门禁；本 Wiki 自身仍应额外做 Markdown link、路径和 source inventory 审校。
- 当前没有正式的 `tests/Core/*.Tests.csproj` xUnit 项目；不能在交付中声称已经运行不存在的 xUnit suite。

## Legacy ERB / dialect 定向验证

只改 legacy ERB C# 时，至少从下列 fast loop 起步：

```powershell
dotnet build gemuera-c#.sln -c Debug --no-restore
powershell -NoProfile -ExecutionPolicy Bypass -File tools/dialect-inventory/Test-DialectInventory.ps1 -ProjectRoot .
powershell -NoProfile -ExecutionPolicy Bypass -File tools/dialect-inventory/Test-DialectRegistrySnapshot.ps1 -ProjectRoot .
powershell -NoProfile -ExecutionPolicy Bypass -File tools/dialect-inventory/Test-DialectSignatureInventory.ps1 -ProjectRoot .
```

Snake reference surface/wiring 检查需要显式提供参考项目：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/snake-alignment/Test-SnakeReferenceSurface.ps1 -ProjectRoot . -ReferenceRoot 'E:\MyCode\Era\emuera_lazyloading_selfmodified_version-main-skiasharp'
```

该脚本覆盖 instruction/function key 与 `SEQUENCEINPUT`、`TEXT_BGC_ON`、HTML font/ARGB/div、Canvas/Control renderer 等关键静态接线；输出中的通过结论明确不代表完整运行期 parity。涉及 Godot C# projection 时还应运行：

```powershell
& 'E:\Godot_v4.7-stable_mono_win64\Godot_v4.7-stable_mono_win64_console.exe' --headless --editor --path . --build-solutions --quit
```

按改动类型追加：

| 改动 | 追加验证 |
| --- | --- |
| instruction 参数 / flags | `Test-DialectSignatureResolution.ps1`、`Test-DialectInstructionFlagResolution.ps1`。 |
| expression function 参数 / return | `Test-DialectFunctionSignatureResolution.ps1`。 |
| 名称、comparer、collision | `Test-DialectNameLookupContract.ps1`。 |
| module/profile/behavior key | 对应 `tools/dialect-inventory/Test-Dialect*.ps1`。 |
| legacy 行为 | 任务对应的 v24/Snake/eraFL fixture 或 `tools/legacy-runner/` trace。 |
| save | save baseline、candidate commit、old save 与 isolated round-trip；原档 hash 保持不变。 |
| Godot scene/UI | 单一相关 GDUnit4 suite。 |
| Android capability | 导出 APK + 目标设备/权限/回退证据。 |

当前 dialect inventory 的某些门禁会因 `profile.selected-name` 分类未裁决而 fail-fast。那是应当保留的阻断，不要通过随意填 catalog、重写期望计数或沿用旧 hash 强行变绿。完整规则见 [`../../ERBAPI.md`](../../ERBAPI.md)。

## 运行期 diagnostics

### 配置入口

根目录 `config.toml` 当前包含：

```toml
[migration]
session_isolation = false

[logging]
enabled = true

touch = false
input = false
image = false
ui_layout = false
dynamic_map = false
resource = false
load_save = false
android_storage = false
performance = false
statement_recognition = false
```

`session_isolation=false` 表示默认保留 M0 的 `GlobalStatic` / `EmueraThread` legacy 启动链。改变该值后需要重启；开启也不等于 Parser/VM 已消费 `CompatibilityPlan` 或 M1 通过。

### Diagnostics 模块

| 文件 / 类型 | 作用 |
| --- | --- |
| `RuntimeDiagnosticsConfig` | 日志等级、category、debug model、performance/input replay/snapshot 等有效配置。 |
| `RuntimeDiagnosticsConfigLoader` / `RuntimeTomlParser` | 从 `config.toml` 读取与解析，报告格式错误。 |
| `RuntimeDiagnosticsConfigWriter` | 由 panel 写回可编辑配置。 |
| `DiagnosticLogRouter` | ring/breadcrumb/log routing、rate limit、monotonic time。 |
| `DiagnosticLogExporter` / `DiagnosticLogSinks` | 导出当前诊断包/日志的 sink。 |
| `RuntimeDiagnosticsPanel` | Godot 浮动诊断 UI。 |
| `InputReplayBuffer` | 有界输入回放记录。 |
| `SaveLogOperationTrail` | 有界的保存/核心输入操作 trail；input 会按脱敏/截断策略导出。 |
| `GenericUtils` | old/new logging bridge、UI queue、performance sample、application shutdown breadcrumb。 |

### 诊断使用原则

- 先开最小 category，再复现；不要长期开启所有 high-frequency traces。
- Save/input 日志可能包含用户输入或游戏状态片段，导出前确认脱敏/截断策略；不要把用户存档、游戏目录或设备日志提交到源码仓库。
- UI queue、sprite、performance 的日志是 timing-sensitive，缺少环境/identity 的单条日志不能作为完备结论。
- 应用退出时 `GenericUtils.NotifyApplicationShutdown()` 可写 breadcrumb；启动器也会早期初始化日志以覆盖 Android 权限/扫描阶段。

## 工具目录

| 目录 | 用途 |
| --- | --- |
| `tools/baseline-identity/` | 源码、工具链、游戏、artifact identity 的记录/验证。 |
| `tools/fixture-manifest/` | fixture 来源、许可、缺失与 manifest contract。 |
| `tools/legacy-runner/` | 隔离复制游戏、输入 replay、trace、display baseline、A/B/A/session isolation。 |
| `tools/save-baseline/` | legacy save 静态 baseline、fixture audit、round-trip evidence。 |
| `tools/session-state-inventory/` | `Program` / `GlobalStatic` 等 legacy session root inventory。 |
| `tools/dialect-inventory/` | DIA-01..DIA-17 inventory、signature、profile/module/owner/fixture contracts。 |
| `tools/core-contracts/` | Core smoke、display/M3–M7 contract and architecture checks。 |
| `tools/m3-m7/` | future work package、status/governance contract。 |
| `tools/doc-guards/` | 文档结构、链接与稳定条款门禁。 |

## Export / Android

`export_presets.cfg` 定义 export preset；实际导出需要项目工具链、Godot export templates、Android SDK/JDK/Gradle 和签名等外部条件。

最小结论层次：

```text
build succeeds
  < export succeeds
  < APK identity/hash exists
  < installs and launches
  < permission/storage/game selection works
  < target-device scenario trace passes
  < fallback/rollback evidence exists
```

不要跳过中间层。尤其是 Android SAF：当前有 capability/设计 surface，但没有即可推断的完整 production adapter、撤权恢复、parallel canary 或真机回退证据。

## 已知阶段与证据纪律

| 范围 | 安全结论 |
| --- | --- |
| M0 | runner/trace/display/fixture 工具有进展，但稳定三层 baseline、完整保存、APK/设备/签署等仍有缺口。 |
| M1 | façade、generation、canary 和部分 A/B/A 观察存在；静态/async owner、rollback、memory budget 等未完全关闭。 |
| M2 | display DTO 合同存在，但默认 data/renderer path 未批准切换。 |
| M3–M7 | 类型、smoke、schema 和设计库存存在；不是阶段实现或发布授权。 |

完成任何任务时，报告应同时列出：执行命令、exit code、fixture/artifact identity、覆盖范围、未覆盖项、风险与回退。Build、单一 smoke、截图或静态 inventory 都不能自行将 `Blocked` / `Uncovered` 提升为 `Passed`。

## Related pages

- startup/config ownership：[`02-Startup-and-Lifecycle.md`](02-Startup-and-Lifecycle.md)
- ERB verification boundary：[`03-Legacy-Interpreter.md`](03-Legacy-Interpreter.md)、[`../../ERBAPI.md`](../../ERBAPI.md)
- diagnostics data flow：[`05-Console-Rendering-and-Resources.md`](05-Console-Rendering-and-Resources.md)、[`07-Dependencies-and-Threading.md`](07-Dependencies-and-Threading.md)
- current handoff authority：[`../NewFrameworkDesign/DeveloperHandoff.md`](../NewFrameworkDesign/DeveloperHandoff.md)
