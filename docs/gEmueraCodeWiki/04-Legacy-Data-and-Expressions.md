# Legacy 数据、变量、表达式与 ERB 注册

> 本页只描述 **当前 legacy 数据/表达式 owner**。Core 的 `VariableStore`、`ErbParser`、descriptor 和 runtime host 见 [`06-GodotHost-and-Core.md`](06-GodotHost-and-Core.md)，它们不能替代 legacy 的完整 runtime 语义。

## 模块总览

```text
ERB source / CSV / config
  -> ParserMediator + ExpressionParser + FunctionIdentifier
  -> ExpressionMediator
      ├─ VariableEvaluator / VariableData / VariableToken
      ├─ FunctionMethodCreator / FunctionMethod
      ├─ OperatorManager / term tree
      ├─ ConstantData / GameBase / CharacterData
      └─ Process / EmueraConsole / AppContents as concrete services
```

| 目录 / 文件 | 关键类型 | 责任 |
| --- | --- | --- |
| `GameData/Expression/` | `ExpressionMediator`、`ExpressionParser`、`IOperandTerm`、`OperatorManager`、term classes | 表达式 token 化、归约、重构、求值与 operator dispatch。 |
| `GameData/Function/` | `FunctionMethodCreator`、`FunctionMethod`、`Creator.Method.cs` | 表达式函数注册/签名/返回值与具体 built-in 实现。 |
| `GameData/Variable/` | `VariableEvaluator`、`VariableData`、`VariableToken`、character/local/user-defined token | 运行期变量存储、scope、token 映射、角色与保存变量。 |
| `GameData/ConstantData.cs` | `ConstantData`、character constants | CSV/常量数据、变量数组维度和初始定义。 |
| `GameData/GameBase.cs` | `GameBase` | 游戏基础元数据与 script/gamebase 定义。 |
| `GameData/IdentifierDictionary.cs` | `IdentifierDictionary` | 名称、macro/variable/function/instruction lookup 的 legacy registry 边界。 |
| `GameData/ParserMediator.cs` | `ParserMediator` | parser 警告、兼容 plan 绑定、统一的 parse-time mediation。 |
| `GameData/DefineMacro.cs`、`StrForm.cs`、`EraType.cs` | macro、formatted string、type model | 语言层的辅助语义。 |
| `GameProc/Function/` | `FunctionIdentifier`、`ArgumentBuilder`、`Instruction`、`FunctionCode` | statement/instruction 名称、参数 builder、可执行 instruction 实现。 |

## 核心对象关系

### `ExpressionMediator`

`ExpressionMediator` 是 Process 与 expression/variable/function 之间的适配层。它让 parser 和执行器通过同一 mediator 获得：

- 变量读写与 `VariableEvaluator`；
- expression function 的解析/调用；
- user-defined function 的 callback 到 `Process.GetValue(...)`；
- data/constant、console、graphics/resource 等 legacy 服务；
- 当前 `ParserMediator` / compatibility plan 的相关 parse-time 信息。

因此表达式函数不应自己保存跨 session 的静态状态，也不应绕过 mediator 直接抓取 Godot node。

### `ExpressionParser`、term 与 operator

`ExpressionParser` 将 ERB 表达式归约为 `IOperandTerm` / `SingleTerm` 等 term 对象，配合：

- `OperatorCode`、`OperatorManager`、`OperatorMethodManager` 选择一元/二元/三元运算；
- `EraType` / `EraTypeHelper` 做 int/string/float 语义；
- `CaseExpression` 处理 case 条件；
- `StrForm` 处理格式化字符串语义；
- `SafeArithmetic`（如启用/适用）处理受限算术路径。

许多 term 会被缓存或重构；带状态/副作用的函数必须正确设置其可重构/常量折叠语义。`ERBAPI.md` 中对 `CanRestructure` 的规则优先于任何局部直觉。

### `FunctionMethodCreator` 与表达式函数

文件：`Scripts/Emuera/GameData/Function/Creator.cs`、`Creator.Method.cs`

`FunctionMethodCreator` 是 expression-function 的 legacy 注册入口；`Creator.Method.cs` 集中实现大量 built-in `FunctionMethod` 子类。它们覆盖数据、字符串、数值、HTML、graphics、sprite、sound、platform、save 等能力。

常见类别：

| 类别 | 例子（按名称） | 典型依赖 |
| --- | --- | --- |
| CSV / character / variable 查询 | `GetcharaMethod`、`CsvDataMethod`、`VarsizeMethod` | `ConstantData`、`VariableEvaluator`。 |
| 数学与数组 | `RandMethod`、`PowerMethod`、`MaxMethod`、array helpers | operator/type/variable state。 |
| 字符串与 HTML | `StrlenMethod`、`ReplaceMethod`、`HtmlToPlainTextMethod`、`HtmlEscapeMethod` | `StringStyle`、`HtmlManager`。 |
| Graphics / Sprite | `GraphicsCreateMethod`、`GraphicsDrawGMethod`、`SpriteCreateMethod` | `AppContents`、`GraphicsImage`、Godot host bridge。 |
| 音频 / input / platform | `GetKeyStateMethod`、`MousePosMethod`、sound helpers、`GetPlatformMethod` | `GenericUtils`、view/input/platform facade。 |
| save / custom extension | `SaveTextMethod`、`LoadTextMethod` 等 | save codecs / legacy data state。 |

**新增生产函数的正确流程不是“向 Core descriptor 加名称”。** 需要先在 [`../../ERBAPI.md`](../../ERBAPI.md) 中判断 owner，然后完成 legacy registry、参数、return、错误、side-effect / restructure、profile visibility、fixture 和报告更新。

## Variable 系统

### `VariableData`

`VariableData` 是 concrete mutable legacy 存储。构造时根据 `ConstantData` 为：

- 标量 integer / string / float；
- 一维 sparse array；
- 二维/三维数组；
- local / arg / localf / argf / locals / args 等 function-scoped 数据；
- character list、user-defined variables、save/global-save 分类；

建立实际数组与 token dictionary。它不能被 Core `VariableStore` 的存在替代；二者的 schema、scope、reset、save 语义尚未完成等价迁移。

### `VariableEvaluator`

`VariableEvaluator` 是 Process/Expression 视角的变量运行接口。它负责把 `VariableToken` / element reference 的读写与：

- `RESULT`、`RESULTS`、`RESULT_ARRAY`、`RESULTS_ARRAY` 等输入/返回通道；
- local/arg scope；
- user-defined、global、character variable；
- save/load/reset 和 CSV-derived defaults；

结合起来。`Process.InputInteger`、`InputString` 等会写入 evaluator 对应的 RESULT family，再恢复 `DoScript()`。

### `VariableToken` 与 identifier lookup

`VariableToken` family 将 ERB 名称与具体存储/索引规则绑定。`VariableData` 在初始化时注册标准变量、常量、debug token 与 local variable token。

- `IdentifierDictionary` / variable token dictionaries 是 legacy 名称解析真相的一部分。
- 直接修改或在启动后覆盖其公开 dictionary 会产生 comparer、collision、初始化顺序和 profile 可见性风险。
- 新变量需覆盖 declaration、name lookup、reset、scope、save schema、legacy 保存兼容与 lazy/reload 行为；不要只增加 `VariableCode` 或 token 类型。

## CSV、常量与游戏基础数据

| 类型 | 责任 |
| --- | --- |
| `GameBase` | 游戏标题、作者、版本、默认角色等 gamebase/script 元数据。 |
| `ConstantData` | character int/string definitions、变量大小、默认值和常量表。 |
| `CharacterData` / `CharacterTemplate` | 角色数据和模板。 |
| `Preload` / `HeaderFileLoader` | 启动时读取、预取和组织 CSV/header 相关输入。 |
| `Config` / `ConfigData` / `JSONConfig` | legacy/game 配置与可兼容选项；与启动路径、显示和 parser 行为耦合。 |

外部游戏 CSV、ERB、config、save、manifest 和 DLL 都是输入，不应当作可信代码。解析/加载边界应保留原有错误、路径和编码处理。

## Instruction 注册与参数解析

Statement instruction 的关键 owner 在 `Scripts/Emuera/GameProc/Function/`：

```text
FunctionIdentifier
  -> built-in instruction name dictionary / profile-specific registration
  -> FunctionCode
  -> ArgumentBuilder / ArgumentParser
  -> AbstractInstruction and concrete instruction classes
  -> Process / ExpressionMediator / EmueraConsole effects
```

| 类型 | 作用 |
| --- | --- |
| `FunctionIdentifier` | instruction 名称到 code/handler 的 legacy 注册及 v24/Snake compatibility 注册区域。 |
| `FunctionCode` | built-in instruction code 表。 |
| `ArgumentBuilder` / `ArgumentParser` | 根据 instruction 语义构造 typed argument；特殊 input、array、call、HTML、save 等参数都有专用 builder。 |
| `AbstractInstruction` 与 `Instraction*.cs` | 实际执行 instruction，包括 print、wait/input、flow control、graphics、sound、save 等。 |

当前 v24 与 Snake 相关指令/函数可在同一静态注册流程中可见；不要仅因一个 handler 位于 “Snake” 命名区域，就断言它只对 Snake profile 可见。可见性、collision 与行为需用完整 registry/fixture 证据验证。

## 数据流示例

### 表达式函数调用

```text
LogicalLineParser / ArgumentBuilder
  -> ExpressionParser builds terms
  -> ExpressionMediator evaluates term
  -> FunctionMethodCreator lookup + FunctionMethod.Get*Value
  -> VariableEvaluator / ConstantData / console / content service
  -> value returns to instruction / ProcessState
```

### 用户定义函数（表达式内）

```text
term refers to SuperUserDefinedMethodTerm
  -> Process.GetValue(...)
  -> clone CalledFunction frame
  -> ProcessState.IntoFunction(...)
  -> runScriptProc()
  -> RETURNF / ProcessState removes current frame
  -> restore caller state or rollback on failure
```

这条路径尤其容易出现 call-stack / local scope 污染，改动必须覆盖 nested function、error、return 和 repeated expression evaluation。

## 变更评审清单

修改本区域前，至少回答：

- 新能力是 instruction、expression function、variable、directive 还是 grammar？
- current legacy owner 是哪个 registry/parser/handler？Core 中是否仅存在 candidate contract？
- 名称是否与现有 instruction/function/variable/macro collision？是否受 comparer/culture 影响？
- 参数、默认值、return type、flags、errors、`CanRestructure`、side effects 是否定义？
- profile 未选择时，registry/hash/行为是否保持不变？
- 对 full load、lazy load、reload、wait/resume、save/reset 的影响是什么？
- 该改动是纯 C# 逻辑、Godot scene 行为还是 Android capability，需要哪一层验证？

详细 Definition of Done 与禁止快捷方式：[`../../ERBAPI.md`](../../ERBAPI.md)。

## 相关页面

- 执行、loader、wait：[`03-Legacy-Interpreter.md`](03-Legacy-Interpreter.md)
- 显示/graphics/sprite 具体投影：[`05-Console-Rendering-and-Resources.md`](05-Console-Rendering-and-Resources.md)
- Core typed variable/parser contract：[`06-GodotHost-and-Core.md`](06-GodotHost-and-Core.md)
- 文件级索引：[`10-Source-Index.md`](10-Source-Index.md)