# CODE_MAP

更新时间：2026-05-28

用途：这是给 AI 和维护者快速定位代码用的地图。优先读本文件，再按路径进入源码。地图只记录结构、职责、主要接口和关键函数，不复制源码实现。

维护规则：
- 新增、移动、重命名 `Scripts/**/*.cs` 时，同步更新本文件对应目录表。
- 大文件只写职责和关键入口，不枚举所有私有 helper。
- `addons/**`、`*.uid`、字体、图标、导出产物默认只做目录摘要，不进入 C# 明细。
- 重新扫描可用：

```powershell
rg --files -g '*.cs' -g '!addons/**'
rg -n "^\s*(public|internal|protected).*\(" Scripts -g '*.cs'
rg -n "interface|abstract class|class .*:|enum " Scripts -g '*.cs' -g '!addons/**'
```

## 项目概览

`gEmuera` 是 Godot 4.6 + C# 的 Emuera 文本游戏引擎移植版。项目用 Godot 节点替换原 Windows Forms/GDI 渲染，同时保留大量原 Emuera 核心结构。

本次扫描范围：
- 项目 C#：`Scripts/**/*.cs`
- C# 文件数：148
- C# 代码行数约：82537
- 忽略：`addons/**`、`*.uid`、资源导入文件

核心运行链：

```text
project.godot
  run/main_scene = res://first_window.tscn
    -> FirstWindow._Ready()
       扫描 era* 游戏目录，选择游戏
    -> main.tscn
       -> EmueraMain._Ready()
          初始化路径、配置映射、GPU 队列、EmueraContent
       -> EmueraThread.Start()
          后台 Thread 执行 Program.Main()
       -> Program.Main()
          创建 MainWindow/EmueraConsole/Process
       -> Process.Initialize()
          读取 config/csv/erb，建立 LabelDictionary
       -> Process.DoScript() / runScriptProc()
          执行 ERB 指令
       -> EmueraConsole / GenericUtils / EmueraContent
          输出文本、按钮、图片、音频和输入交互
```

线程模型：
- Godot 主线程：UI、输入、`EmueraContent`、`SpriteManager.UpdateOtherThreads()`、GPU ColorMatrix 队列。
- 后台线程：`EmueraThread.Work()` 执行 `Program.Main()`、ERB 解释、阻塞式输入等待。
- 跨线程桥：`GenericUtils` 的 UI 队列、日志队列、显示队列；输入通过 `EmueraThread.Input()` 唤醒后台线程。

## 目录树

```text
.
|-- project.godot                 Godot 项目配置，主场景 first_window.tscn
|-- first_window.tscn             启动器场景
|-- main.tscn                     主游戏场景
|-- config.toml                   精简运行期诊断配置；默认开启轻量日志以支持保存 `gemuera_*.log`，`[logging].enabled=false` 时日志/诊断系统完全关闭
|-- IDEAS.md                      项目工作约定，所有 AI 任务优先阅读
|-- CLAUDE.md                     Claude Code/AI CLI/AI IDE 执行指南
|-- action_maps/                  本地 AI/程序操作日志，不提交 GitHub，最多 30 个日志文件
|-- Text/                         Emuera 配置编码映射文本/bytes
|-- Lang/                         UI 多语言文本
|-- Fonts/                        内置字体
|-- Icons/                        UI 图标
|-- NativeLibs/android/           Android native 库
|-- Scripts/
|   |-- *.cs                      Godot UI、线程桥、渲染、精灵管理
|   |-- Diagnostics/              运行期诊断、日志、导出、面板
|   |-- Emuera/
|   |   |-- Config/               Emuera 配置系统
|   |   |-- Content/              图片、精灵、Graphics surface
|   |   |-- GameData/             变量、表达式、常量、函数方法
|   |   |-- GameProc/             ERB 加载、解析、执行状态机
|   |   |-- GameView/             控制台显示模型和 HTML/按钮/图片行
|   |   |-- Modern/               现代扩展函数
|   |   |-- Runtime/              SQLite、插件运行时工具
|   |   |-- Sub/                  词法、流、异常、存档二进制
|   |   `-- _Library/             Win/GDI/随机数/语言兼容工具
|   |-- Shaders/                  ColorMatrix shader
|   `-- uEmuera/                  System.Drawing / Forms 兼容层
|-- addons/
|   |-- gdUnit4/                  Godot 测试插件
|   `-- godot_mcp/                Godot MCP 编辑器插件
`-- patches/                      历史补丁
```

## C# 文件职责地图

### Scripts 根目录

| 文件 | 主要类型 | 职责 | 关键入口/函数 |
|---|---|---|---|
| `Scripts/EmueraMain.cs` | `EmueraMain : Node`, `GpuWorkItem`, `TextRenderItem` | 主场景入口；初始化配置映射、UI 根节点、线程和 GPU/文本渲染队列。 | `_Ready`, `_Process`, `_ExitTree`, `Run`, `Clear`, `Restart`, `GpuSubmitColorMatrix`, `SubmitTextRender` |
| `Scripts/EmueraThread.cs` | `EmueraThread` | 后台执行 Emuera 核心；把 Godot 输入转成阻塞式 console 输入。 | `Start`, `End`, `Running`, `Input` |
| `Scripts/EmueraContent.cs` | `EmueraContent : Control`, `UiDiagnosticOverlay` | Godot UI 渲染核心；固定行高文本、按钮、图片层、输入栏、快速按钮、音频、缩放、诊断覆盖层。 | `_Ready`, `AddLine`, `AddLines`, `ApplyTextChanges`, `UpdateDisplay`, `RefreshCBG`, `PlaySoundFile`, `PlayBgmFile`, `SetContentScale`, `_Input` |
| `Scripts/EmueraImage.cs` | `EmueraImage : Control` | 绘制 `Texture2D` / `AtlasTexture` 的控件，支持 ColorMatrix material。 | `SetColorMatrix`, `_Draw` |
| `Scripts/GenericUtils.cs` | `GenericUtils`, `EmueraLogLevel`, `EmueraLogCategory`, `SnakeAudioInfo` | Emuera 核心到 Godot 的静态桥；日志总开关、诊断热路径闸门、UI 队列、文本输出、音频、输入回放。 | `InitializeLogging`, `IsLogEnabled`, `IsScrollTraceActive`, `FlushUI`, `AddText`, `ApplyTextChanges`, `SetBackgroundColor`, `PlaySoundFile`, `ExportDiagnosticPackage`, `RestartGame` |
| `Scripts/FirstWindow.cs` | `FirstWindow : Control` | 启动器；扫描 `era*` 游戏目录，切换语言/核心 profile，进入主场景。 | `_Ready`, `_ExitTree`, `_Notification`, `ResolveStartupGamePath` |
| `Scripts/SpriteManager.cs` | `SpriteManager`, `TextureInfo`, `SpriteInfo` | 图片/精灵纹理缓存；AtlasTexture 管理；后台请求与主线程限流加载。 | `Init`, `GetSprite`, `GetTextureInfo`, `GetTextureInfoOtherThread`, `UpdateOtherThreads`, `UpdateCleanup`, `ForceClear` |
| `Scripts/ColorMatrixGPU.cs` | `ColorMatrixGPU` | ColorMatrix shader material 创建、缓存、LRU、uniform 设置。 | `CreateMaterial`, `GetSharedMaterial`, `GetMatrixKey`, `SetMatrixUniforms`, `CreateCompositMaterial` |
| `Scripts/QuickButtons.cs` | `QuickButtons : CanvasLayer` | 快捷按钮浮层；显示当前可选输入，处理点击/触摸。 | `_Ready`, `_Process`, `_Input`, `AddButton`, `Clear`, `ShiftLine`, `SetInputEnabled` |
| `Scripts/Inputpad.cs` | `Inputpad : Control` | 屏幕输入面板；数字/文字输入 UI。 | `_Ready`, `_Process`, `UpdateInputType`, `ShowPad`, `HidePad`, `HasInputFocus` |
| `Scripts/Scalepad.cs` | `Scalepad : Control` | UI 缩放控制。 | `_Ready`, `_Notification`, `SetScale`, `SyncScale`, `ShowPad`, `HidePad` |
| `Scripts/OptionWindow.cs` | `OptionWindow : Control` | 选项弹窗。 | `_Ready`, `ShowPopup` |
| `Scripts/SpriteDebugViewer.cs` | `SpriteDebugViewer : Control` | 精灵调试查看器，配合 `SpriteDebugNotifier` 查看加载图片。 | `_Ready`, `_Process`, `_Input`, `_ExitTree` |
| `Scripts/SpriteDebugNotifier.cs` | `SpriteDebugNotifier` | 精灵加载事件通知。 | `Notify`, `ImageLoadedHandler` |
| `Scripts/FrameRateHelper.cs` | `FrameRateHelper` | 应用帧率配置。 | `Apply`, `ApplyConfigFps` |
| `Scripts/ResolutionHelper.cs` | `ResolutionHelper` | 解析/应用窗口分辨率配置。 | `Apply`, `RefreshResolutions` |
| `Scripts/MultiLanguage.cs` | `MultiLanguage` | 读取 `Lang/*.txt`，提供 UI 文案。 | `Load`, `Get`, `CurrentLanguage` |

### Scripts/Diagnostics

| 文件 | 主要类型 | 职责 | 关键入口/函数 |
|---|---|---|---|
| `DiagnosticLogRecord.cs` | `DiagnosticLogRecord` | 单条诊断日志记录和值格式化。 | `FormatForGodot`, `FormatForExport` |
| `DiagnosticLogRouter.cs` | `DiagnosticLogRouter` | 日志总闸门、类别过滤、限流、脱敏、record 构造；关闭时热路径直接返回。 | `Initialize`, `Reload`, `IsLoggingEnabled`, `IsEnabled`, `CheckRateLimit`, `BuildRecord`, `RedactPath` |
| `DiagnosticLogSinks.cs` | `DiagnosticLogSinks` | 环形日志缓存和 Godot 输出镜像。 | `Initialize`, `Write`, `Snapshot`, `SetMirrorNonErrorToGodot` |
| `DiagnosticLogExporter.cs` | `DiagnosticLogExporter` | 导出诊断包/日志，记录面包屑，清理保留文件。 | `ExportDiagnosticPackage`, `ExportDiagnosticLog`, `WriteBreadcrumb`, `RunRetentionCleanup` |
| `InputReplayBuffer.cs` | `InputReplayBuffer` | 输入回放环形缓冲。 | `Capture`, `BuildExportText` |
| `SaveLogOperationTrail.cs` | `SaveLogOperationTrail` | 存档/日志操作轨迹缓存。 | `Capture`, `BuildExportText` |
| `RuntimeDiagnosticsConfig.cs` | `RuntimeDiagnosticsConfig` 等配置类 | 运行期诊断配置模型、默认值、精简 logging 开关展开和关闭态清理。 | `CreateDefault`, `ApplyMinimalLoggingConfig`, `DisableAllDiagnostics`, `GetRuntimeLogLevel`, `GetActiveDebugModel` |
| `RuntimeDiagnosticsConfigLoader.cs` | `RuntimeDiagnosticsConfigLoader`, `LoadResult` | 读取 `config.toml` 和用户覆盖配置，支持精简 `[logging]` 总开关和模块开关。 | `Load` |
| `RuntimeDiagnosticsConfigWriter.cs` | `RuntimeDiagnosticsConfigWriter` | 写出精简用户诊断配置 TOML。 | `SaveUserConfig`, `BuildToml` |
| `RuntimeTomlParser.cs` | `RuntimeTomlParser` | 简易 TOML 解析器。 | `Parse` |
| `RuntimeDiagnosticsPanel.cs` | `RuntimeDiagnosticsPanel`, `FloatingDiagnosticsHost` | Godot 内置诊断浮窗。 | `AttachFloatingTo`, `_Ready`, `_Notification` |

### Scripts/uEmuera 兼容层

| 文件 | 主要类型 | 职责 | 关键入口/函数 |
|---|---|---|---|
| `uEmuera/Application.cs` | `Application` | Windows Forms `Application` 兼容桩。 | `EnableVisualStyles`, `SetCompatibleTextRenderingDefault`, `Run` |
| `uEmuera/Drawing.cs` | `Bitmap`, `BitmapTexture`, `Graphics`, `Color`, `Font`, `Rectangle`, `Point`, `Size` | `System.Drawing` 替代层；包装 Godot Image/Texture、颜色、字体、几何类型。 | `Bitmap.Save`, `BitmapTexture`, `Color.FromArgb`, `Color.ToGodotColor`, `Rectangle.Intersect` |
| `uEmuera/Forms.cs` | `Timer`, `MessageBox`, `ScrollBar`, `ToolTip`, `PictureBox`, `TextBox` | `System.Windows.Forms` 替代层；Timer 由 Godot loop 手动 Update。 | `Timer.Update`, `MessageBox.Show`, `ToolTip.SetToolTip` |
| `uEmuera/Window.cs` | `MainWindow`, `DebugDialog` | 原主窗体兼容桩，桥接 `EmueraConsole` 和 `Process`。 | `MainWindow.Init`, `Update`, `Refresh`, `WaitForRefreshProcessed`, `Reboot` |
| `uEmuera/Utils.cs` | `Logger`, `Utils` | 文件系统、编码、资源扫描、显示宽度、路径规范化工具。 | `SHIFTJIS_to_UTF8`, `NormalizePath`, `FileExists`, `GetFilePaths`, `GetDisplayLength`, `ResourcePrepare` |
| `uEmuera/Properties.cs` | `ResourceManager`, `Resources` | 原资源访问兼容。 | `GetString` |
| `uEmuera/VisualBasic.cs` | `Strings`, `VbStrConv` | VB 字符串转换兼容。 | `StrConv` |
| `uEmuera/Media.cs` | `Hand`, `Asterisk` | 系统声音兼容桩。 | `Play` |
| `uEmuera/partial/EmueraConsole.cs` | partial `EmueraConsole` | 给 uEmuera 层访问显示行和输入等待状态的扩展，等待态包含 `WaitInputNoFocus`。 | `GetDisplayLinesForuEmuera`, `GetDisplayLinesCount`, `GetDisplayLinesSnapshotForuEmuera`, `IsWaitingInput` |
| `uEmuera/partial/AConsoleColoredPart.cs` | partial `AConsoleColoredPart` | 显示部件兼容扩展。 | 主要是 partial 补充 |

### Scripts/Emuera 根

| 文件 | 主要类型 | 职责 | 关键入口/函数 |
|---|---|---|---|
| `Emuera/Program.cs` | `Program`, `EmueraCoreProfile` | 原 Emuera 入口；设置目录、核心 profile、配置、窗口、Process。 | `Main`, `AppendSnakeStartupErrorLog`, `DetectCoreProfile`, `ConfigureModernMobileCoreAdapters` |
| `Emuera/GlobalStatic.cs` | `GlobalStatic` | 核心全局对象注册和重置；保存插件存在标志。 | `Reset`, `ExistPlugin` 及静态字段 |

### Scripts/Emuera/Config

| 文件 | 主要类型 | 职责 | 关键入口/函数 |
|---|---|---|---|
| `Config.cs` | `Config` | 运行配置静态访问；字体、路径、窗口尺寸、更新检查、`UPDATECHECK` 禁用、插件警告、`BEFORE_ERROR/THROW` 禁用开关、debug config。 | `SetConfig`, `GetFont`, `ClearFont`, `CreateSavDir`, `CheckUpdate`, `UpdateWindowWidth`, `SetDebugConfig` |
| `ConfigData.cs` | `ConfigData` | 配置数据实体，保存所有 Emuera 选项，包含插件警告、异常前事件禁用项、v24 `TextDrawingMode.SKIASHARP` 默认兼容和默认开启 lazy loading。 | 构造/读取/保存配置项 |
| `ConfigCode.cs` | `ConfigCode` 等 enum | 配置项枚举和相关枚举，包含 `PluginAvailableWarn`、`DisableBeforeErrorThrow` 和 `TextDrawingMode.SKIASHARP`。 | 枚举定义 |
| `ConfigItem.cs` | `AConfigItem`, `ConfigItem<T>` | 单个配置项的解析/序列化容器。 | `ToString`, value parse 相关 |
| `KeyMacro.cs` | `KeyMacro` | 快捷键宏配置。 | `Load`, `Save`, key macro 访问 |

### Scripts/Emuera/Content

| 文件 | 主要类型 | 职责 | 关键入口/函数 |
|---|---|---|---|
| `AContentFile.cs` | `AContentFile : IDisposable` | 内容文件抽象基类。 | `Dispose` |
| `AContentItem.cs` | 空 namespace 占位 | 预留文件，当前不定义类型。 | 无 |
| `AppContents.cs` | `AppContents`, `LazySpriteDefinition` | 资源/精灵表注册；读取资源 CSV；lazy sprite 索引。 | `CreateSpriteAnime`, `BuildLazyResourceIndex`, `RealizeLazySprite` |
| `ConstImage.cs` | `AbstractImage`, `ConstImage` | 不可变图片资源，基于 Bitmap/Image。 | `CreateFrom`, `Dispose` |
| `CroppedImage.cs` | `ASprite`, `ASpriteSingle`, `SpriteG`, `SpriteF`, `SpriteAnime` | 精灵裁剪、动画帧、绘制接口。 | `SpriteGetColor`, `GraphicsDraw`, `AddFrame`, `PauseAnimation`, `ResumeAnimation`, `GetCurrentFrameInfo` |
| `GraphicsImage.cs` | `GraphicsImage : AbstractImage` | ERB 图形 surface；绘制 sprite、线、文字、多边形、旋转、ColorMatrix。 | `GCreate`, `GDrawCImg`, `ApplyColorMatrixGPU`, `GDrawG`, `GDrawString`, `GRotate`, `GDispose` |

### Scripts/Emuera/GameData

| 文件 | 主要类型 | 职责 | 关键入口/函数 |
|---|---|---|---|
| `ConstantData.cs` | `ConstantData`, `CharacterTemplate`, `LazyErdNameData` | CSV 常量、角色模板、ERD 名称数据。 | 常量读取、角色模板访问 |
| `DefineMacro.cs` | `DefineMacro` | `#DEFINE` 宏数据。 | 构造和字段 |
| `EraType.cs` | `EraType` | Era 值类型枚举。 | 枚举定义 |
| `GameBase.cs` | `GameBase` | 游戏基础信息、版本、标题、更新检查 URL/版本名等。 | `Load`, `Save`, 基础字段访问 |
| `IdentifierDictionary.cs` | `IdentifierDictionary` | 变量/函数/宏名解析字典。 | `GetIdentifier`, `Add`, defined-name 管理 |
| `ParserMediator.cs` | `ParserMediator` | 表达式、变量、函数解析的中介和 warning 管理。 | `Initialize`, `GetWarningList`, parse helper |
| `StrForm.cs` | `StrForm`, `FormattedStringMethod` 系列 | 格式化字符串表达式。 | `GetString`, format 方法 |

### Scripts/Emuera/GameData/Expression

| 文件 | 主要类型 | 职责 | 关键入口/函数 |
|---|---|---|---|
| `IOperandTerm.cs` | `IOperandTerm` | 表达式操作数抽象基类。 | `GetIntValue`, `GetStrValue`, `GetFloatValue`, `GetValue`, `Restructure` |
| `Term.cs` | `NullTerm`, `SingleTerm`, `StrFormTerm`, `VariadicArgTerm` | 常量/字符串格式/可变参数表达式项。 | `GetValue`, `GetIntValue`, `GetStrValue`, `Restructure` |
| `ExpressionParser.cs` | `ExpressionParser` | ERB 表达式解析器。 | `ReduceExpression`, `ReduceArguments`, `ReadExpression` 类方法 |
| `ExpressionMediator.cs` | `ExpressionMediator` | 表达式求值上下文，连接变量和函数。 | 变量/函数访问、运行时上下文 |
| `OperatorCode.cs` | `OperatorCode`, `OperatorManager` | 运算符枚举和查找。 | `GetOperator`, operator metadata |
| `OperatorMethod.cs` | `OperatorMethod`, 多个具体运算符 | 运算符求值实现。 | `OperatorMethodManager.Initialize`, `GetIntValue`, `GetStrValue`, `GetReturnValue` |
| `SafeArithmetic.cs` | `SafeArithmetic` | 整数/浮点安全数学工具。 | `Add`, `Sub`, `Mul`, `Div`, `Pow` 等 |
| `CaseExpression.cs` | `CaseExpression` | `CASE` 条件表达式。 | `IsMatch`, 条件求值 |

### Scripts/Emuera/GameData/Function

| 文件 | 主要类型 | 职责 | 关键入口/函数 |
|---|---|---|---|
| `FunctionMethod.cs` | `FunctionMethod` | 内置函数抽象基类。 | `CheckArgumentType`, `GetIntValue`, `GetStrValue`, `GetFloatValue`, `GetReturnValue`, `UniqueRestructure` |
| `FunctionMethodTerm.cs` | `FunctionMethodTerm` | 把 `FunctionMethod` 包装成表达式项。 | `GetIntValue`, `GetStrValue`, `Restructure` |
| `Creator.cs` | partial `FunctionMethodCreator` | 创建内置函数表。 | `GetMethodList` |
| `Creator.Method.cs` | partial `FunctionMethodCreator` | 大量基础内置函数：角色、CSV、字符串、数学、图像、音频、平台识别、变量访问等。 | 嵌套 `*Method : FunctionMethod`；统一 override `GetIntValue/GetStrValue/GetReturnValue` |
| `Creator.Method.DT.cs` | partial `FunctionMethodCreator` | DataTable 扩展函数。 | `DtCreate`, `DtRowAdd`, `DtCellGet`, `DtSelect`, XML 互转 |
| `Creator.Method.Map.cs` | partial `FunctionMethodCreator` | Map 扩展函数。 | `MapCreate`, `MapSet`, `MapGet`, `MapKeys`, `MapToXml` |
| `Creator.Method.Sql.cs` | partial `FunctionMethodCreator` | SQL 扩展函数。 | `SqlConnect`, `SqlExecuteReader`, `SqlReaderGet*`, import/export |
| `Creator.Method.Xml.cs` | partial `FunctionMethodCreator` | XML 扩展函数。 | `XmlDocument`, `XmlGet`, `XmlSet`, `XmlAddNode`, `XmlRemoveNode` |
| `RuntimeDataStore.cs` | `RuntimeDataStore` | 运行期 DataTable/Map/XML 静态存储。 | `Clear`, `DataTables`, `Maps`, `XmlDocuments` |
| `UserDefinedMethodTerm.cs` | `UserDefinedMethodTerm`, `UserDefinedRefMethodTerm` | 用户定义函数调用表达式项。 | `Create`, `Restructure`, `GetRefName`, `GetValue` |
| `UserDefinedRefMethod.cs` | `UserDefinedRefMethod` | `#REF/#REFS/#REFF` 引用函数匹配和绑定。 | `Create`, `MatchType`, `SetReference` |

### Scripts/Emuera/GameData/Variable

| 文件 | 主要类型 | 职责 | 关键入口/函数 |
|---|---|---|---|
| `VariableCode.cs` | `VariableCode` | Emuera 变量枚举。 | 枚举定义 |
| `VariableIdentifier.cs` | `VariableIdentifier` | 变量名到 `VariableCode`/scope 的解析。 | `GetVarNameDic`, `GetVariableId`, `GetExtSaveList` |
| `VariableData.cs` | `VariableData : IDisposable` | 全局变量数据容器和变量 token 构造。 | 初始化变量、读取/保存、`Dispose` |
| `VariableEvaluator.cs` | `VariableEvaluator : IDisposable` | 变量求值、读写、角色变量访问、局部变量栈。 | `GetValue`, `SetValue`, local/reference 管理 |
| `VariableToken.cs` | `VariableToken` 及大量派生 token | 变量实际存取实现；静态/私有/局部/引用/角色/常量/伪变量。 | `GetIntValue`, `GetStrValue`, `GetFloatValue`, `SetValue`, `SetValueAll`, `PlusValue`, `In`, `Out` |
| `VariableTerm.cs` | `VariableTerm`, `FixedVariableTerm`, `VariableNoArgTerm` | 表达式中的变量访问项。 | `GetIntValue`, `SetValue`, `Restructure` |
| `VariableStrArgTerm.cs` | `VariableStrArgTerm` | 字符串索引变量表达式项。 | `GetStrValue`, `Restructure` |
| `VariableLocal.cs` | `VariableLocal` | 局部变量集合。 | local 变量创建、进入/退出函数 |
| `VariableParser.cs` | `VariableParser` | 变量表达式解析。 | `Parse`, variable term 构造 |
| `CharacterData.cs` | `CharacterData : IDisposable` | 角色数据数组和角色变量管理。 | 角色增删、保存/读取、`Dispose` |

### Scripts/Emuera/GameProc

| 文件 | 主要类型 | 职责 | 关键入口/函数 |
|---|---|---|---|
| `Process.cs` | partial `Process` | 脚本处理器主类；初始化、输入结果、开始执行、异常处理。 | `Initialize`, `DoScript`, `BeginTitle`, `InputInteger`, `InputString`, `ReloadErb`, `GetRunningPosition` |
| `Process.ScriptProc.cs` | partial `Process` | 内层脚本执行循环和 debug 执行。 | `runScriptProc`, `DoDebugNormalFunction`, `saveCurrentState`, `loadPrevState` |
| `Process.State.cs` | `ProcessState`, `SystemStateCode`, `BeginType` | CALL/JUMP/RETURN、BEGIN、函数栈、返回值、状态克隆。 | `JumpTo`, `SetBegin`, `Begin`, `Return`, `IntoFunction`, `ReturnF`, `Clone` |
| `Process.SystemProc.cs` | partial `Process` | 系统流程处理。 | 系统状态执行 helper |
| `Process.CalledFunction.cs` | `CalledFunction`, `UserDefinedFunctionArgument` | 调用栈条目和用户函数实参。 | 构造、参数访问 |
| `Process.LazyLoading.cs` | partial `Process`, `LazyStatus` | ERB lazy loading 表、索引、按需加载、缓存。 | `TryLazyLoadErb`, `LoadLazyLoadingTable`, `SaveLazyLoadingList`, `PreloadEventLoadLazyErbs` |
| `ErbLoader.cs` | `ErbLoader`, `PPState` | 读取/预处理 ERB/ERH 文件，生成 logical lines/labels。 | `LoadErbFiles`, `loadErbs`, `warningDic` |
| `HeaderFileLoader.cs` | `HeaderFileLoader` | 读取头文件/定义。 | header 加载入口 |
| `LogicalLine.cs` | `LogicalLine`, `InstructionLine`, `FunctionLabelLine`, `GotoLabelLine` | ERB 逻辑行模型。 | `FunctionLabelLine`, `InstructionLine`, label/goto 访问 |
| `LogicalLineParser.cs` | `LogicalLineParser` | 将文本行解析为 `LogicalLine`。 | `ParseSharpLine`, `ParseLine`, `ParseLabelLine` |
| `LabelDictionary.cs` | `LabelDictionary` | 函数 label、事件 label、`$` label 索引。 | `AddLabel`, `SortLabels`, `GetEventLabels`, `GetNonEventLabel`, `GetLabelDollar` |
| `InputRequest.cs` | `InputRequest`, `InputType` | 输入请求类型和值约束，`NoFocus` 标记用于 NF 定时输入。 | 构造和字段 |
| `UserDefinedFunction.cs` | `UserDefinedFunctionData` | 用户定义函数元数据。 | 构造和参数类型 |
| `UserDefinedVariable.cs` | `UserDefinedVariableData`, `DimLineWC` | 用户定义变量元数据。 | 构造、维度/类型信息 |

### Scripts/Emuera/GameProc/Function

| 文件 | 主要类型 | 职责 | 关键入口/函数 |
|---|---|---|---|
| `Instruction.cs` | `AbstractInstruction` | ERB 指令抽象基类。 | `SetJumpTo`, `DoInstruction`, `CreateArgument` |
| `Instraction.Child.cs` | partial `FunctionIdentifier`, 多个 `*Instruction` | 具体 ERB 指令实现；文件名保留原拼写 `Instraction`；包含 Snake/v24 兼容指令、`TINPUTNF/TINPUTSNF/TONEINPUTNF/TONEINPUTSNF` 和渲染控制 API。 | `PRINT_Instruction`, `TINPUT_Instruction`, `CALL_Instruction`, `GOTO_Instruction`, `RETURNF_Instruction`, `SNAKE_UI_SETTING_Instruction` 等 |
| `FunctionIdentifier.cs` | `FunctionIdentifier` | 指令名/FunctionCode 映射和指令分类，注册 NF 定时输入变体。 | `GetInstructionNameDic`, `IsPrint`, `IsInput`, `IsJump`, `IsMethod`, `IsFlowContorol` |
| `BuiltInFunctionCode.cs` | `FunctionCode` | 内置指令/函数 code 枚举，包含 NF 定时输入 code。 | 枚举定义 |
| `FunctionArgType.cs` | `FunctionArgType` | 指令参数类型枚举。 | 枚举定义 |
| `Argument.cs` | `Argument` 及大量 `Sp*Argument` | 已解析指令参数的数据对象。 | 构造和字段 |
| `ArgumentBuilder.cs` | `ArgumentBuilder`, 多个 `*ArgumentBuilder` | 针对不同指令构造 `Argument`。 | `Build`, `CheckArgument`, 各指令 builder |
| `ArgumentParser.cs` | partial `ArgumentParser` | 根据 `FunctionIdentifier` 解析参数。 | `ParseArgument` |

### Scripts/Emuera/GameView

| 文件 | 主要类型 | 职责 | 关键入口/函数 |
|---|---|---|---|
| `EmueraConsole.cs` | partial `EmueraConsole`, `DisplayLineList`, `ClientBackGroundImage` | 控制台状态、输入等待、NF 等待态、CBG/图片层、鼠标/键盘、调试、重载；保存 v24 渲染控制 API 的脚本可见状态。 | `Initialize`, `WaitInput`, `IsWaitInputState`, `PressEnterKey`, `RefreshStrings`, `CBG_SetImage`, `SetImageLayer`, `SetSnakeTextDrawingMode`, `ReloadErb`, `Dispose` |
| `EmueraConsole.Print.cs` | partial `EmueraConsole` | 打印文本、HTML、按钮、图片、形状、日志输出；维护 `IsLineEnd`、`LINECOUNT`、`CLEARLINE` 的逻辑行语义和 `PrintC/PrintButtonC` 像素制表。 | `Print`, `PrintC`, `PrintButtonC`, `PrintHtml`, `PrintImg`, `PrintShape`, `PrintButton`, `PrintFlush`, `deleteLine`, `OutputLog`, `PopDisplayingLines` |
| `ConsoleDisplayLine.cs` | `ConsoleDisplayLine` | 一行显示内容，包含多个按钮/片段。 | `DrawTo`, `GDIDrawTo`, `ShiftPositionX`, `ChangeStr` |
| `ConsoleButtonString.cs` | `ConsoleButtonString` | 一个可点击/可输入的显示段，包含多个 display part。 | `DivideAt`, `CalcWidth`, `CalcPointX`, `DrawTo` |
| `AConsoleDisplayPart.cs` | `AConsoleDisplayPart`, `AConsoleColoredPart` | 显示片段抽象基类。 | `DrawTo`, `GDIDrawTo`, `ToString` |
| `ConsoleStyledString.cs` | `ConsoleStyledString`, `DisplayMode` | 有样式文本片段。 | `DrawTo`, 样式字段 |
| `ConsoleImagePart.cs` | `ConsoleImagePart` | 行内图片片段。 | `DrawTo`, 图片尺寸/偏移 |
| `ConsoleShapePart.cs` | `ConsoleShapePart`, `ConsoleRectangleShapePart`, `ConsoleSpacePart`, `ConsoleErrorShapePart` | 行内形状/空白/错误占位片段。 | `DrawTo`, shape 参数 |
| `ConsoleDivPart.cs` | `ConsoleDivPart`, `StyledBoxModel` | HTML div/盒模型片段。 | box 计算与绘制 |
| `ButtonStringCreator.cs` | `ButtonStringCreator`, `ButtonPrimitive` | 将文本拆成按钮/显示片段。 | `CreateButtonString` 相关 |
| `HtmlManager.cs` | `HtmlManager` 及 HTML state 类型 | HTML 文本和 display line 互转，支持 style/button/img/shape/div。 | `Html2DisplayLine`, `Html2ButtonList`, `DisplayLine2Html`, `HtmlTagSplit`, `Escape`, `Unescape` |
| `PrintStringBuffer.cs` | `PrintStringBuffer` | 打印缓冲；把连续输出合并成 display line，并提供当前缓冲行像素宽度。 | `Append`, `Flush`, `CurrentLineWidth`, line 构造 |
| `StringMeasure.cs` | `StringMeasure : IDisposable` | 文本宽度测量。 | `GetDisplayLength`, `Dispose` |
| `StringStyle.cs` | `StringStyle` | 文本颜色、字体样式、font name。 | 构造、比较、转换 |
| `MixedNum.cs` | `MixedNum` | HTML 尺寸/位置混合数值。 | 数值字段/解析辅助 |

### Scripts/Emuera/Runtime

| 文件 | 主要类型 | 职责 | 关键入口/函数 |
|---|---|---|---|
| `Runtime/Utils/SqliteRuntime.cs` | `SqliteRuntime` | SQLite 初始化、连接路径、运行时可用性。 | `Initialize`, `OpenConnection`, `Shutdown` |
| `Runtime/Utils/SnakeSqlManager.cs` | `SnakeSqlManager`, `ReaderContext` | Snake profile SQL 兼容层。 | `Connect`, `ExecuteNonQuery`, `ExecuteReader`, `ReaderGet*`, `Disconnect` |
| `Runtime/Utils/PluginSystem/IPluginMethod.cs` | `IPluginMethod` | 插件方法接口。 | `Name`, `Description`, `Execute(PluginMethodParameter[] args)` |
| `Runtime/Utils/PluginSystem/PluginManager.cs` | `PluginManager`, `ReflectionPluginMethod` | 插件 manifest 加载、DLL 存在检测、方法注册和反射调用。 | `LoadPlugins`, `ExecuteMethod`, method registry |
| `Runtime/Utils/PluginSystem/PluginManifestAbstract.cs` | `PluginManifestAbstract` | 插件 manifest 抽象基类。 | manifest 字段/属性 |
| `Runtime/Utils/PluginSystem/PluginMethodParameter.cs` | `PluginMethodParameter`, `PluginMethodParameterBuilder` | 插件方法参数对象和 builder，支持整数、字符串和小数参数。 | `ConvertTerm`, 参数字段 |
| `Modern/Script/Functions/ModernSqlManager.cs` | `ModernSqlManager`, `ReaderContext` | 现代 SQL 扩展函数运行时。 | `Connect`, `ExecuteReader`, `ReaderGet*`, `Disconnect` |

### Scripts/Emuera/Sub

| 文件 | 主要类型 | 职责 | 关键入口/函数 |
|---|---|---|---|
| `EmueraException.cs` | `EmueraException`, `ExeEE`, `CodeEE`, `FileEE`, `ScriptPosition` | 核心异常层次和脚本位置。 | `ScriptPosition`, exception 构造 |
| `EraStreamReader.cs` | `EraStreamReader` | Era 文本读取封装。 | `ReadLine`, `Dispose` |
| `EraDataStream.cs` | `EraDataReader`, `EraDataWriter`, `EraDataState` | 文本存档/数据流读写。 | `Read`, `Write`, `Dispose` |
| `EraBinaryDataReader.cs` | `EraBinaryDataReader`, `EraSaveFileType`, `EraSaveDataType` | 二进制存档读取，含 1808 兼容 reader。 | `Read`, `ReadInt64`, `ReadString`, `Dispose` |
| `EraBinaryDataWriter.cs` | `EraBinaryDataWriter` | 二进制存档写入。 | `Write`, `WriteInt64`, `WriteString`, `Dispose` |
| `LexicalAnalyzer.cs` | `LexicalAnalyzer` | ERB 词法分析，生成 `WordCollection`。 | `Analyse`, string/form string 解析 |
| `Word.cs` | `Word` 及各 token word | 词法 token 模型。 | `ToString`, token 字段 |
| `WordCollection.cs` | `WordCollection` | token 列表和当前位置操作。 | `Current`, `ShiftNext`, `GetWord`, `Clone` |
| `SubWord.cs` | `SubWord` 系列 | 格式字符串内嵌片段。 | 派生类型字段 |
| `StringStream.cs` | `StringStream` | 字符串流读取工具。 | `Current`, `ShiftNext`, `Substring`, `Seek` |
| `Preload.cs` | `Preload` | 预加载/路径相关辅助。 | `GetFiles`, preload helper |

### Scripts/Emuera/_Library

| 文件 | 主要类型 | 职责 | 关键入口/函数 |
|---|---|---|---|
| `_Library/GDI.cs` | `GDI`, `StockObjects`, `StretchMode` | GDI 兼容常量/函数桩。 | GDI helper |
| `_Library/LangManager.cs` | `LangManager` | Emuera 内部语言文本管理。 | `Load`, `GetStr` |
| `_Library/SFMT.cs` | `MTRandom` | SFMT/随机数实现。 | `Next`, seed 初始化 |
| `_Library/Sys.cs` | `Sys` | 全局路径、exe dir 等系统信息。 | `ExeDir`, path 字段 |
| `_Library/WinInput.cs` | `WinInput`, `MouseButtons` | 鼠标/键盘输入兼容枚举和 helper；提供 `GETKEYTRIGGERED` latch 消费和清理。 | `GetKeyState`, `ConsumeKeyLatch`, `ClearLatches`, mouse helpers |
| `_Library/WinmmTimer.cs` | `WinmmTimer` | WinMM timer 兼容桩。 | 构造/字段 |

## 核心接口和抽象契约

| 契约 | 位置 | 说明 | 主要成员 |
|---|---|---|---|
| `IPluginMethod` | `Scripts/Emuera/Runtime/Utils/PluginSystem/IPluginMethod.cs` | 真正的 C# interface；插件方法统一入口。 | `Name`, `Description`, `Execute(PluginMethodParameter[] args)` |
| `IOperandTerm` | `GameData/Expression/IOperandTerm.cs` | 名字像接口，实际是 abstract class；所有表达式项的求值契约。 | `GetIntValue`, `GetStrValue`, `GetFloatValue`, `GetValue`, `Restructure` |
| `FunctionMethod` | `GameData/Function/FunctionMethod.cs` | 内置函数契约；所有 `*Method` 嵌套类继承它。 | `CheckArgumentType`, `GetIntValue`, `GetStrValue`, `GetReturnValue`, `UniqueRestructure` |
| `AbstractInstruction` | `GameProc/Function/Instruction.cs` | ERB 指令执行契约。 | `SetJumpTo`, `DoInstruction`, `CreateArgument`, `ArgBuilder` |
| `ArgumentBuilder` + `Argument` | `GameProc/Function/ArgumentBuilder.cs`, `Argument.cs` | 指令参数解析和参数对象契约。 | builder 构造 `Argument`；`Argument` 派生类保存解析结果 |
| `VariableToken` | `GameData/Variable/VariableToken.cs` | 变量读写契约，覆盖标量/数组/角色/局部/引用/常量。 | `GetIntValue`, `GetStrValue`, `GetFloatValue`, `SetValue`, `SetValueAll`, `PlusValue` |
| `VariableTerm` | `GameData/Variable/VariableTerm.cs` | 表达式中的变量访问契约。 | `Get*Value`, `SetValue`, `Restructure` |
| `AContentFile` / `AbstractImage` / `ASprite` | `Content/*.cs` | 图片资源和精灵绘制契约。 | `Dispose`, `SpriteGetColor`, `GraphicsDraw` |
| `AConsoleDisplayPart` | `GameView/AConsoleDisplayPart.cs` | 一行显示中的最小渲染片段。 | `DrawTo`, `GDIDrawTo`, `ToString` |
| partial `EmueraConsole` | `GameView/EmueraConsole*.cs` | 控制台 facade；输入、输出、刷新、调试、图片层都集中在这里。 | `Print*`, `WaitInput`, `PressEnterKey`, `RefreshStrings`, `CBG_*` |
| partial `Process` | `GameProc/Process*.cs` | ERB 执行核心；初始化、循环、状态、lazy loading 分文件实现。 | `Initialize`, `DoScript`, `runScriptProc`, `TryLazyLoadErb` |

## 关键函数速查

### 启动/生命周期

| 任务 | 优先看 |
|---|---|
| 修改启动器扫描、游戏目录选择 | `FirstWindow._Ready`, `FirstWindow.ResolveStartupGamePath` |
| 修改主场景初始化 | `EmueraMain._Ready`, `EmueraMain.Run`, `EmueraMain.Restart` |
| 修改后台线程和输入唤醒 | `EmueraThread.Start`, `EmueraThread.Input`, `EmueraConsole.PressEnterKey` |
| 修改核心入口或 profile 选择 | `Program.Main`, `Program.DetectCoreProfile` |
| 修改退出/重启 | `EmueraMain._ExitTree`, `EmueraConsole.QuitAndRestart`, `GenericUtils.RestartGame` |

### UI/渲染/输入

| 任务 | 优先看 |
|---|---|
| 文本行添加/删除/刷新 | `GenericUtils.AddText`, `GenericUtils.ApplyTextChanges`, `EmueraContent.AddLine`, `EmueraContent.UpdateDisplay` |
| 控制台打印语义 | `EmueraConsole.Print`, `PrintHtml`, `PrintButton`, `PrintFlush` |
| HTML 标签支持 | `HtmlManager.Html2DisplayLine`, `HtmlManager.tagAnalyze`, `ConsoleDivPart` |
| 行内图片/形状 | `ConsoleImagePart`, `ConsoleShapePart`, `EmueraContent.AddLine` |
| CBG/背景/图片层 | `EmueraConsole.CBG_*`, `SetImageLayer`, `EmueraContent.RefreshCBG` |
| 快捷按钮 | `QuickButtons.AddButton`, `QuickButtons.SetInputEnabled`, `EmueraContent.SubmitQuickButtonInput` |
| 屏幕输入面板 | `Inputpad.UpdateInputType`, `ShowPad`, `HidePad` |
| 缩放 | `Scalepad.SetScale`, `EmueraContent.SetContentScale`, `ResolutionHelper.Apply` |

### 图片/精灵/ColorMatrix

| 任务 | 优先看 |
|---|---|
| 资源 CSV 到 sprite | `AppContents`, `SpriteManager.GetSprite`, `uEmuera.Utils.ResourcePrepare` |
| 纹理缓存/主线程加载限流 | `SpriteManager.GetTextureInfoOtherThread`, `SpriteManager.UpdateOtherThreads`, `TextureInfo.RecreateTexture` |
| Graphics surface 绘制 | `GraphicsImage.GCreate`, `GDrawCImg`, `GDrawG`, `GDrawString`, `GDrawLine` |
| GPU ColorMatrix | `ColorMatrixGPU.GetSharedMaterial`, `ColorMatrixGPU.SetMatrixUniforms`, `GraphicsImage.ApplyColorMatrixGPU` |
| Godot 控件绘制图片 | `EmueraImage._Draw` |

### ERB 解析/执行

| 任务 | 优先看 |
|---|---|
| ERB 文件加载 | `ErbLoader.LoadErbFiles`, `ErbLoader.loadErbs` |
| lazy loading | `Process.TryLazyLoadErb`, `LoadLazyLoadingTable`, `SaveLazyLoadingList` |
| 行解析 | `LogicalLineParser.ParseLine`, `ParseLabelLine`, `ParseSharpLine` |
| label 查找 | `LabelDictionary.GetEventLabels`, `GetNonEventLabel`, `GetLabelDollar` |
| 指令名映射 | `FunctionIdentifier.GetInstructionNameDic`, `FunctionIdentifier.IsPrint/IsInput/IsJump/IsMethod` |
| 指令执行 | `AbstractInstruction.DoInstruction`, `Instraction.Child.cs` 中对应 `*Instruction` |
| 脚本主循环 | `Process.DoScript`, `Process.ScriptProc.runScriptProc` |
| CALL/RETURN/BEGIN 状态 | `ProcessState.IntoFunction`, `Return`, `ReturnF`, `SetBegin`, `Begin` |

### 表达式/变量/函数

| 任务 | 优先看 |
|---|---|
| 表达式解析 | `ExpressionParser`, `ExpressionMediator`, `IOperandTerm` |
| 运算符 | `OperatorManager`, `OperatorMethodManager`, `SafeArithmetic` |
| 变量名解析 | `VariableIdentifier.GetVariableId`, `VariableParser` |
| 变量读写 | `VariableEvaluator`, `VariableToken`, `VariableTerm` |
| 新增普通内置函数 | `GameData/Function/Creator.Method.cs` + `FunctionMethodCreator.GetMethodList` |
| 新增 SQL/Map/XML/DT 函数 | 对应 `Creator.Method.Sql/Map/Xml/DT.cs` |
| 用户定义函数 | `UserDefinedFunctionData`, `UserDefinedMethodTerm`, `CalledFunction`, `ProcessState.IntoFunction` |

### 诊断/日志

| 任务 | 优先看 |
|---|---|
| 日志开关和限流 | `DiagnosticLogRouter` |
| 写日志 | `GenericUtils.Debug/Info/Warn/Error`, `DiagnosticLogSinks.Write` |
| 导出诊断包 | `GenericUtils.ExportDiagnosticPackage`, `DiagnosticLogExporter.ExportDiagnosticPackage` |
| 运行期配置 | `RuntimeDiagnosticsConfig`, `RuntimeDiagnosticsConfigLoader`, `RuntimeDiagnosticsConfigWriter` |
| 输入回放 | `InputReplayBuffer`, `GenericUtils.CaptureInputReplay` |
| 诊断浮窗 | `RuntimeDiagnosticsPanel.AttachFloatingTo` |

## 常见修改定位

| 要改什么 | 从这里开始 |
|---|---|
| Android 游戏目录、屏幕宽度策略 | `FirstWindow`, `Program.ApplyAndroidWindowWidthPolicy`, `Config.UpdateWindowWidth` |
| 输入后脚本不继续 | `EmueraThread.Input`, `EmueraConsole.PressEnterKey`, `Process.InputInteger/InputString` |
| 文本没有刷新或顺序错 | `GenericUtils.FlushUI`, `EmueraContent.ApplyTextChanges`, `EmueraConsole.PopDisplayingLines` |
| 图片/立绘不显示 | `AppContents`, `SpriteManager`, `ConsoleImagePart`, `EmueraImage`, `GraphicsImage` |
| ColorMatrix 效果错误 | `ColorMatrixGPU`, `GraphicsImage.GDrawCImg`, shader `Scripts/Shaders/color_matrix*.gdshader` |
| HTML 显示错误 | `HtmlManager`, `ConsoleDivPart`, `ConsoleStyledString`, `PrintStringBuffer` |
| ERB 函数找不到 | `FunctionMethodCreator.GetMethodList`, `FunctionIdentifier`, `LabelDictionary`, lazy loading 表 |
| 变量读写错误 | `VariableIdentifier`, `VariableParser`, `VariableEvaluator`, `VariableToken` |
| 存档兼容 | `EraDataStream`, `EraBinaryDataReader`, `EraBinaryDataWriter`, `VariableData`, `CharacterData` |
| SQL/Map/XML/DT 扩展 | `RuntimeDataStore`, `SnakeSqlManager`, `ModernSqlManager`, `Creator.Method.*.cs` |
| 日志太多或没有日志 | `config.toml` 的 `[logging].enabled` 与模块开关、`RuntimeDiagnosticsConfig`, `DiagnosticLogRouter`, `GenericUtils.InitializeLogging` |
