# Legacy ERB 解释器：加载、解析、执行与等待

> **当前生产行为 owner：** `Scripts/Emuera/GameProc/`、`GameData/`、`GameView/` 及其 legacy registries。`src/Core/Parsing/ErbParser` 和 `Runtime/IErbInterpreterHost` 是候选合同，不能替代本页描述的默认解释器。

## 模块边界

| 文件 / 子目录 | 关键类型 | 责任 |
| --- | --- | --- |
| `ErbLoader.cs` | `ErbLoader` | 枚举 ERB、加载源文件、构造 logical line 链、label 预处理、语法检查、lazy-loading 补载。 |
| `HeaderFileLoader.cs` | `HeaderFileLoader` | 加载 header 相关输入并参与 legacy 初始化。 |
| `LogicalLine.cs` | logical-line family | 表示 label、instruction、null/invalid 等解析后的执行单元。 |
| `LogicalLineParser.cs` | `LogicalLineParser` | 将源文本行归约为 `LogicalLine`。 |
| `LabelDictionary.cs` | `LabelDictionary` | 维护 function/event label 的索引、排序和初始化状态。 |
| `Process.cs` | partial `Process` | 解释器 facade；初始化、系统过程、输入写回、异常处理、主执行循环。 |
| `Process.State.cs` | `ProcessState`、`SystemStateCode`、`BeginType` | 当前行、调用帧、函数/上下文栈、错误位置、wait/return 等执行状态。 |
| `Process.ScriptProc.cs` | partial `Process` | 常规脚本 instruction 的逐行执行循环。 |
| `Process.SystemProc.cs` | partial `Process` | 系统 process/event 的执行。 |
| `Process.CalledFunction.cs` | 调用辅助 | user-defined function 调用帧与返回地址。 |
| `Process.LazyLoading.cs` | `LazyStatus` | lazy-loading index、补载、统计与 Android 适配。 |
| `Function/` | instruction argument/function definitions | 语句 token、参数构建与执行支撑。 |
| `InputRequest.cs` | `InputRequest`、`InputType` | UI/console 与脚本执行间的等待输入合同。 |

完整文件/声明索引见 [`10-Source-Index.md`](10-Source-Index.md)。

## 初始化和加载流程

```text
MainWindow.Init / EmueraConsole
  -> new Process(console)
  -> Process.Initialize() / InitializeAsync()
       -> new ProcessState(console)
       -> ParserMediator.Initialize(console)
       -> ParserMediator.BindCompatibilityPlan(Program.CurrentCompatibilityPlan)
       -> Preload.Clear / Preload.Load(csv, erb)
       -> HeaderFileLoader / data initialization
       -> ErbLoader.LoadErbFiles(Program.ErbDir, ...)
            -> Config.GetFiles("*.ERB")
            -> optional lazy-loading table setup
            -> loadErb(file)
                 -> LogicalLineParser.ParseLine(...)
                 -> assign ParentLabelLine / next-line chain
                 -> register labels and dynamic-variable preconditions
            -> setLabelsArg / label sort
            -> checkScript
       -> label dictionary marked Initialized
```

`Process.Initialize()` 的同步入口只是 `InitializeAsync().GetAwaiter().GetResult()`；加载中的 I/O / parse work 可通过 task 执行，但后续 state 的使用仍需服从 legacy worker 的 owner 约束。

### `ErbLoader` 的关键语义

`ErbLoader.LoadErbFiles(...)` / `LoadErbFilesAsync(...)`：

1. 以 `Config.GetFiles(erbDir, "*.ERB")` 获取脚本；移动端还处理小写扩展名的兼容路径。
2. 清理并预分配 `LabelDictionary`。
3. 若 `Config.UseLazyLoading`，建立或读取 lazy index；非 lazy 则重置 lazy 状态。
4. 逐文件通过 `loadErb` 读取并调用 `LogicalLineParser.ParseLine`；建立 `NextLine` 连接与父 label。
5. 为 label 解析参数、排序、检查 function/script，随后标记 dictionary 已初始化。
6. 通过 `ParserMediator` 累积并刷新 warning；在诊断开启时写 load/scroll trace。

不要把“ERB 文件已枚举”误当作“所有 function 已可执行”：lazy 模式可保留待补载文件；label 初始化、函数参数分析和 script check 都是后续步骤。

### Lazy loading

`Process.LazyLoading.cs` 的 `LazyStatus` 包含 `Disabled`、`NoLazy`、`BuildTable`、`Loaded`、`Error`、`UpdateTable`。运行时 `TryLazyLoadErb(functionName)` 可在找不到目标 function 时查询索引、加载关联 ERB、更新 label dictionary 和运行统计。

- lazy index 的文件路径/工作目录与当前游戏目录关联；canary/game switch 时必须重置 process-wide 缓存。
- Android 有特殊 index 构建/写入限制；不要将桌面文件假设直接迁到手机路径。
- parser、label 或函数可见面改动必须覆盖 full-load、lazy-load、reload 路径，详见 [`../../ERBAPI.md`](../../ERBAPI.md)。

## 执行循环

```text
EmueraConsole receives a submit / button / timer input
  -> EmueraConsole.callEmueraProgram(...)
  -> Process.InputInteger / InputString / input-array assignment as appropriate
  -> Process.DoScript()
       -> while system state requires it: runSystemProc()
       -> runScriptProc()
            -> read ProcessState.CurrentLine
            -> dispatch parsed instruction / control flow / function call
            -> advance, jump, push/pop context, or establish wait
       -> exception path annotates current/error line and reports through console
```

### `Process`

`Process` 是 `internal sealed partial class`，不要只打开一个文件就假设包含全部逻辑。

| 方法 / 区域 | 作用 |
| --- | --- |
| `Initialize()` / `InitializeAsync()` | 建立 `ProcessState`、parser mediator、preload、header/ERB/label 初始化。 |
| `DoScript()` | 顶层驱动：优先处理系统过程，随后执行普通脚本；统一处理 exception、错误位置与调用栈清理。 |
| `runScriptProc()` | regular script loop，执行 logical line/instruction 并驱动 `ProcessState` 迁移。 |
| `runSystemProc()` | system/event 流程的执行。 |
| `InputInteger` / `InputString` / `InputResult5` | 把 console 输入写入 `VariableEvaluator` 的 `RESULT`、`RESULTS` / arrays，同时记录 Ctrl-Z 输入历史。 |
| `GetValue(SuperUserDefinedMethodTerm)` | 在表达式中执行用户定义函数，克隆调用帧、管理递归上限、保存/恢复调用状态。 |
| exception handlers | 将当前 logical line、before-error/skip-before-error 等状态转为可报告的 console 错误，避免脏调用栈泄漏到下一次执行。 |

### `ProcessState`

`ProcessState` 是运行时执行真相，持有或协调：

- 当前/错误 logical line 与 script position；
- function/call stack、context stack、return 地址、`RETURNF` / method return 值；
- 进入/退出函数、jump/loop、系统过程状态；
- pending error、before-error、script end 等控制标志；
- 对 `ExecutionContext` 与 dynamic/local variable scope 的关联。

**改动 `ProcessState` 时必须考虑 resume、error、function return、lazy-loaded label 以及 session reset。** 错误的 stack 清理会把父函数 context 弹出，造成后续 `LOCAL` / `ARG` / `RETURNF` 异常。

## 输入等待与恢复

文件：`InputRequest.cs`、`EmueraConsole.cs`、`EmueraThread.cs`

`InputRequest` 是 legacy view 与 process 的缓冲合同。其 `InputType` 至少覆盖：

| `InputType` | 意义 |
| --- | --- |
| `EnterKey` / `AnyKey` | 等待确认或任意键。 |
| `IntValue` / `StrValue` / `AnyValue` | 等待数值、字符串或任一 value。 |
| `IntButton` / `StrButton` | 等待由 display button 提供的值。 |
| `Void` | 不接受用户值，只能等待/skip 控制。 |
| `PrimitiveMouseKey` | 指针输入兼容路径。 |

额外元数据包括 `OneInput`、`NoFocus`、`StopMesskip`、system-input、默认值、time limit 与 pointer input metadata。

```text
Process / instruction establishes InputRequest
  -> EmueraConsole exposes wait state and requests UI projection
  -> Godot main thread renders input/control
  -> user action reaches EmueraContent / Inputpad / button hit handling
  -> EmueraThread.Input(...) signals ManualResetEventSlim
  -> EmueraConsole.PressEnterKey(...) validates/parses input
  -> callEmueraProgram(...) writes Process result and resumes DoScript
```

注意：`EmueraThread.IsSessionActive` 与 console “当前正处理脚本”不是同一概念；在 intentional input/wait pause 中，session 仍可能是活的。Host stop/start 必须依赖 worker 生命周期。

### Snake 兼容扩展的 legacy owner

Snake 兼容功能仍由 legacy 静态 instruction/function registry 与具体 handler 执行，而不是由 `src/Core` 的 `game.snake` 声明壳执行。当前扩展链路中需要同时保留以下约束：

- `TEXT_BGC_ON` 使用专用 `SP_COLOR_ALPHA` 参数模型，加载期严格要求两个整数参数；执行期继续检查 RGB `0..0xFFFFFF` 与 alpha percent `0..100`。
- Snake HTML print builder 只在自身解析路径启用 `LexAnalyzeFlag.AnalyzePrintV`；不要修改通用 `popTerms()`，否则会改变普通 instruction 的词法语义。
- `SEQUENCEINPUT` 的 `\e` 必须由 `LexicalAnalyzer` 保留为两个字符，之后由 `PressEnterKey` 消费并设置 `MesSkip`；宏被禁用时原始输入必须直接提交。
- instruction key、expression-function key、参数 builder、执行 handler 和 view side effect 都是同一功能的组成部分；只补 registry surface 不算完成。

`tools/snake-alignment/Test-SnakeReferenceSurface.ps1` 只检查 reference key 覆盖与关键静态接线。它不执行 ERB fixture，也不证明 profile registry isolation、NoFocus 滚动或完整运行期行为已经与参考实现一致。

### Profile、策略与 registry 边界

`EmueraMain` 在创建 `EmueraConsole` 和 worker 前，通过 `BuiltInDialectCatalog.CreateLegacySessionPlan` 绑定当前启动器选择的 plan。`Program` 将该 plan 投影为不可变的 `LegacyCompatibilityProfile`；其中 `ISnakeCompatibilityPolicy` 和 `IEraFlCompatibilityPolicy` 分别承载已确认的 parser、CALL、resource、display 和 pointer-input 差异。

`FunctionIdentifier` 仍保存完整 legacy handler table，但只对 `IdentifierDictionary` 暴露以当前 profile 与 config 生成的只读 instruction table。`FunctionMethodCreator` 同样从完整 handler store 投影出 profile-scoped function table。因此 v24 和 eraFL session 不会解析 Snake-only instruction，也不会解析 `陥落状態` / `陷落状态`；Snake 的 `VARI` / `VARS` 还需 scoped-variable config 明确启用。

新增运行期差异必须从 `Program.Compatibility` 的窄策略读取，不得在 `Program.cs` 外重新读取 `Program.IsSnakeProfile` / `Program.IsEraFlProfile`。`Process.State` 中的 eraFL 地图算法仍复用 Core 的纯算法，但入口先经过 eraFL policy gate，未选择 eraFL 时不可达。

这层隔离保留 legacy Parser/VM 和 handler 作为默认行为 owner，且 legacy VM 仍是进程级的。`CoreContractSmoke` 与 `Test-CoreArchitecture.ps1` 仅验证 module closure、registry visibility 和静态消费边界；真实 ERB fixture、A/B/A、APK 和设备证据仍须单独取得。

## 解析、数据与 view 的依赖

```text
GameProc
  ├─ GameData.Expression: parser/evaluator、function、variable token
  ├─ GameData.Variable: mutable legacy game state
  ├─ GameView.EmueraConsole: 输出、input request、errors
  ├─ Config / Program / GlobalStatic: process-wide compatibility/configuration
  ├─ Content: graphics/image/resource instructions
  └─ uEmuera: MainWindow, timer, drawing/form compatibility
```

这也是为什么生产 ERB 改动不能只碰 `src/Core`：legacy process 直接依赖这些 concrete owners。

## ERB 扩展的正确入口

**先读 [`../../ERBAPI.md`](../../ERBAPI.md)，不要从本页直接猜注册点。** 其中规定：

- 普通 instruction、特殊参数 instruction、表达式 function、变量、directive/grammar 各自的 legacy owner；
- v24 / Snake / EraFL profile、module/descriptor 与 legacy handler 的边界；
- 参数、return、flags、constant-folding、collision、lazy/reload 与 fixture 要求；
- Core descriptor/`IErbInterpreterHost` 当前不是生产 handler API；
- 先 RED、后最小 owner implementation、再跑定向 inventory/fixture/Godot/Android gate。

### 禁止快捷方式

- 在 hot path 添加新的 `Program.IsSnakeProfile`、game-id 或 profile if 分支来实现语义；
- 仅新增 enum、`FunctionCode`、`VariableCode`、Core descriptor 或 registry 条目而不实现完整链路；
- 启动后修改 legacy 可变 registry 返回的 dictionary；
- 将简化 Core parser 说成已覆盖 full legacy grammar、label、lazy loading 或执行语义；
- 用 build、单个 smoke 或截图代替旧游戏 fixture / trace / Android 证据。

## 改动后应更新的 Wiki

| 变更 | 更新页面 |
| --- | --- |
| 新启动/初始化阶段、ERB 路径或 profile 绑定 | [`02`](02-Startup-and-Lifecycle.md)、本页、[`07`](07-Dependencies-and-Threading.md)。 |
| loader、label、lazy loading、instruction dispatch、wait/resume | 本页、[`10`](10-Source-Index.md)（运行生成器）。 |
| 函数/变量/表达式接口 | [`04-Legacy-Data-and-Expressions.md`](04-Legacy-Data-and-Expressions.md)、`ERBAPI.md`。 |
| 显示或输入的可观察语义 | [`05-Console-Rendering-and-Resources.md`](05-Console-Rendering-and-Resources.md)。 |
