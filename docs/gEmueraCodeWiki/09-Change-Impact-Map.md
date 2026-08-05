# 变更影响地图与回归选择

本页将常见改动映射到 **真实 owner → 相邻边界 → 必要 Wiki → 最小验证**。它用于在修改前缩小 blast radius；不能替代任务专用的 fixture、Android 设备验证或正式阶段 gate。

## 核心改动影响矩阵

| 改动入口 | 直接 owner | 高风险相邻面 | 必读 Wiki / 规则 | 最小反馈 |
| --- | --- | --- | --- | --- |
| `FirstWindow.cs` | launcher / game selection | `Program` 目录、profile、Android permission/SafeArea、`launcher.cfg` | `02`、`07` | Godot build + launcher/selected-game scenario。 |
| `project.godot` / `main.tscn` | Godot application / scene composition | Autoload、feature flag、prototype child paths、startup lifecycle | `01`、`02`、`06` | Godot build + relevant GDUnit4 scene suite。 |
| `EmueraMain.cs` | legacy Godot host | worker lifecycle、UI queue、GPU/text queue、sprite upload、exit cleanup | `02`、`05`、`07` | Godot build + lifecycle/display smoke。 |
| `EmueraThread.cs` | legacy worker lifecycle | `ManualResetEventSlim`、input wakeup、quiescence、static roots | `02`、`03`、`07` | build + start/stop/replay/canary scenario。 |
| `GenericUtils.cs` | cross-thread UI/diagnostics bridge | queue budget、pending counters、input/audio, diagnostics, main thread ID | `05`、`07`、`08` | targeted UI/trace/perf test。 |
| `EmueraContent*.cs` | Godot console presentation | console line semantics、input/hit test、scroll/layout, Canvas vs Control parity | `05`、`07` | GDUnit4/display baseline + mobile/manual check as needed。 |
| `EmueraImage.cs` / `AnimatedWebpSpriteFrames.cs` | Godot image presentation / animation | source switching、ColorMatrix, texture cleanup, main-thread upload | `05`、`07` | image/display fixture + resource cleanup check。 |
| `SpriteManager.cs` / `AppContents.cs` | legacy resource / texture bridge | background I/O, main-thread texture upload, dynamic sprites, cache/session cleanup | `05`、`07` | resource/display trace + Android stress if applicable。 |
| `GameProc/ErbLoader.cs` | legacy loader | `Config.GetFiles`, `LabelDictionary`, parser warnings, lazy index | `03`、`04`、`ERBAPI.md` | build + load/full/lazy/reload fixture。 |
| `GameProc/LogicalLineParser.cs` | legacy grammar/line parser | argument builder, expression parser, errors, label/position | `03`、`04`、`ERBAPI.md` | RED/GREEN parser/error fixture + dialect contracts。 |
| `GameProc/Process*.cs` | legacy VM / state machine | execution context, call stack, wait/resume, exception/error, system proc | `03`、`07`、`ERBAPI.md` | runtime fixture + replay + state/error cases。 |
| `GameProc/Function/*` | legacy instruction registry/arguments/handlers | profile visibility, name collision, parser, Process, UI/save/resource effects | `03`、`04`、`ERBAPI.md` | signature/registry/behavior fixture。 |
| `GameData/Expression/*` | expression parsing/evaluation | term caching, function invocation, type coercion, variable state | `04`、`ERBAPI.md` | expression RED/GREEN + user function/side-effect test。 |
| `GameData/Function/*` | expression-function registry/handlers | return/argument/type, restructure, data/resource/platform effect | `04`、`ERBAPI.md` | function signature/return/collision fixture。 |
| `GameData/Variable/*` | legacy variables | local/arg scope, save/reset, character data, token lookup | `04`、`03` | scope/reset/save/old-save test。 |
| `GameView/*` | console display/input semantics | line parts, HTML, buttons, wait state, queue ordering | `03`、`05` | display/input trace + line/hit scenario。 |
| `uEmuera/*` | compatibility shim | `MainWindow`, timer/forms/drawing APIs, Godot bridge | `02`、`05`、`07` | host regression + changed API scenario。 |
| `Scripts/GodotHost/*` | host/canary/prototype | session generation, lifecycle, port bridge, Node main-thread ownership | `02`、`06`、`07` | prototype GDUnit4 + Core/host contract as applicable。 |
| `src/Core/Application|Session/*` | Core session contract | generation, candidate/commit/abort, legacy backend adapter, rollback | `06`、`07`、NewFramework M1 docs | Core smoke + session/canary evidence。 |
| `src/Core/Compatibility/*` | profile/module/plan contract | descriptor route, registry consumer, profile visibility, host resolver | `06`、`ERBAPI.md` | Core smoke + dialect/plan contract; no implicit production claim。 |
| `src/Core/Display/*` | display DTO contract | deep copy, ordering/barrier, tee, renderer migration gate | `05`、`06`、M0/M2 docs | Core display contracts + legacy capture/tee evidence。 |
| `src/Core/Resources/*` | pixel/resource contract | VM thread affinity, revision, bridge projection, memory budget | `05`、`06`、M4 docs | Core smoke + pixel/resource/generation evidence。 |
| `src/Core/Save/*` | save candidate contract | profile binding, source-only baseline, atomic commit, old-save compatibility | `06`、`08`、SaveFormat docs | save baseline + isolated round-trip + rollback proof。 |
| `src/Core/Ports/*` | typed platform contracts | manifest freeze, payload/timeout/cancel, adapter owner, stale completion | `06`、`07`、M5 docs | Core/port test + target platform/device evidence。 |
| `Scripts/Diagnostics/*` / `config.toml` | diagnostics policy | privacy, category gating, runtime config, export, shutdown breadcrumb | `08`、`10` | config parser/diagnostic export target check。 |
| csproj/export/native libs | build/package | TFM, Android native libraries, Godot templates, artifact identity | `01`、`08` | restore/build/export + artifact/device evidence。 |
| `tools/*` | evidence/test automation | schemas, report identity, fixture contracts, phase status | `08`、NewFramework docs | tool-specific test + review of output state。 |

## High-risk cross-cutting behaviors

### 1. Profile and compatibility selection

```text
launcher directory route
  -> FirstWindow.SelectedCoreProfileName
  -> Program profile/config state
  -> Parser/registry visible behavior
  -> session/canary CompatibilityPlan (candidate only)
```

Any change can affect discovery, static profile markers, parser visibility, registry collision, canary route and old fallback. Do not solve it by adding an isolated hot-path `if` or by updating only Core descriptors.

### 2. Session switch / game reload

```text
selected game/profile
  -> session selection + generation
  -> legacy static reset / worker start
  -> UI/resource queue and caches
  -> input/audio/resource bridge effects
  -> stop/quiesce old worker
  -> detach old generation
```

Failure modes: old worker alive, old UI action executes late, texture/audio completion hits new game, launcher persistence is accidentally changed by a test injection, or Core candidate is committed before its legacy backend is stable.

### 3. Display transaction and visual parity

```text
legacy ConsoleDisplayLine model
  -> UI queue batching
  -> Godot control/canvas rendering
  -> trace/semantic display observation
  -> Core DisplayTransaction tee (candidate)
```

A display change can pass text screenshots while breaking button hit order, image overlap, nested div/srcb, dynamic map, `WAIT` ordering, mobile scroll, or trace batching. Test the actual part/input scenarios.

### 4. Input and resume

```text
InputRequest semantic type
  -> Godot projection
  -> pointer/key action
  -> console validation + `Process.Input*`
  -> worker wake
  -> ProcessState resume
```

Changing a UI control, pointer policy or input function can corrupt `RESULT` / `RESULTS` / arrays, macro/skip state, no-focus behavior, timeout or system input. Keep process/console ownership intact.

### 5. Save / storage

```text
legacy variable + codec state
  -> save baseline/fixture
  -> candidate/profile/atomic commit
  -> external file system / platform storage adapter
```

Treat external saves as untrusted. Do not write original user data in place; preserve source identity, candidate, backup, atomic commit and rollback evidence.

## Change archetypes

### Local legacy bug fix

Use when behavior owner remains one existing legacy module and does not cross session/CompatibilityPlan/display transaction/save/resource revision/platform port boundary.

1. Locate owner with `10-Source-Index.md` + CodeGraph.
2. Add/reproduce a narrow behavior test or fixture.
3. Implement the smallest owner-local change.
4. Run relevant build/fixture; update the corresponding Wiki section.
5. Do not expand into unrelated refactor/format/resource changes.

### Cross-boundary change

Use when touching session switch, CompatibilityPlan, display transaction, save, resource revision or platform port.

1. Treat it as a `NewFrameworkDesign` work package.
2. Identify input identity, old/new owners, generation, cancellation, timeout, rollback and evidence state.
3. Preserve default legacy fallback.
4. Run Core/host/fixture/device gates appropriate to the boundary.
5. Update `06`/`07`/`08` plus phase documentation; do not proclaim a phase through a code-only change.

### Documentation-only topology change

Use when files moved/renamed, classes become central, or build/test commands change without behavior change.

1. Update the relevant source/navigation page.
2. Run `Update-SourceMap.ps1`.
3. Check all internal relative links.
4. Do not upgrade test/phase status simply because the docs are cleaner.

## Pre-merge impact checklist

- [ ] Identified direct owner and every cross-thread/session edge.
- [ ] Confirmed whether change affects default legacy path, candidate Core path, or both.
- [ ] Listed valid fallback and rollback behavior.
- [ ] Tested all changed observable semantics, not just compilation.
- [ ] Included profile/no-profile and stale-generation cases where relevant.
- [ ] Regenerated `10-Source-Index.md` if any source topology changed.
- [ ] Updated the appropriate module pages and `ERBAPI.md`/NewFramework docs when their ownership requires it.

## Related pages

- Repository/module owners: [`01-Repository-Overview.md`](01-Repository-Overview.md)
- Startup and lifecycle: [`02-Startup-and-Lifecycle.md`](02-Startup-and-Lifecycle.md)
- Legacy VM/data/display: [`03-Legacy-Interpreter.md`](03-Legacy-Interpreter.md), [`04-Legacy-Data-and-Expressions.md`](04-Legacy-Data-and-Expressions.md), [`05-Console-Rendering-and-Resources.md`](05-Console-Rendering-and-Resources.md)
- Core / host / dependency boundaries: [`06-GodotHost-and-Core.md`](06-GodotHost-and-Core.md), [`07-Dependencies-and-Threading.md`](07-Dependencies-and-Threading.md)
- Commands/evidence: [`08-Operations-Testing-and-Diagnostics.md`](08-Operations-Testing-and-Diagnostics.md)