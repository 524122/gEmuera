# 关键类与函数说明

## 顶层控制类

### EmueraMain

**文件**: `Scripts/EmueraMain.cs`  
**继承**: Godot.Node  
**职责**: 应用主入口节点，管理 GPU/文本渲染跨线程队列

```csharp
public partial class EmueraMain : Node
{
    // Export 配置
    [Export] bool debug;           // 调试模式
    [Export] bool use_coroutine;   // 协程模式（未使用）
    [Export] bool enable_sprite_debug_viewer;

    // 跨线程队列
    static ConcurrentQueue<GpuWorkItem> gpuQueue;      // ColorMatrix GPU 工作
    static ConcurrentQueue<TextRenderItem> textRenderQueue; // 文字渲染请求
    static bool GpuReady;                               // GPU 管线就绪标志

    // 公开 API
    static GpuWorkItem GpuSubmitColorMatrix(Image src, Rect2I region, float[][] cm);
    static TextRenderItem SubmitTextRender(string text, ...);
}
```

**生命周期**:
- `_Ready()`: 初始化子组件、加载字符映射表、注册 GodotHost 组件
- `_Process()`: 消费 GPU 队列和文本渲染队列（由子组件实际处理）

### EmueraThread

**文件**: `Scripts/EmueraThread.cs`  
**类型**: 纯 C# 单例  
**职责**: 脚本线程管理和输入事件同步

```csharp
public class EmueraThread
{
    static EmueraThread instance;

    ManualResetEventSlim inputEvent;  // 输入等待信号量
    Thread thread;                     // 脚本执行线程
    string input;                      // 输入内容
    int inputMouseButton;              // 鼠标键码 (1=左, 2=右, 4=中)
    bool skipflag;                     // 跳过标志

    void Start(bool debug, bool use_coroutine);   // 启动脚本线程
    void End();                                    // 停止脚本线程
    void Input(string c, bool from_button, bool skip, int mouseButton);
    bool Running();
}
```

**线程入口** (`Work` 方法):
1. 设置 `Program.debugMode`
2. 调用 `Program.Main()` — 引擎初始化和执行
3. 结束后清理资源 `Utils.ResourceClear()`

### EmueraContent

**文件**: `Scripts/EmueraContent.cs` + `EmueraContent.Canvas.cs`  
**继承**: Godot.Control  
**职责**: 表现层核心 — 连接引擎输出与 Godot 渲染

```csharp
public partial class EmueraContent : Control
{
    static EmueraContent instance;

    // 核心 UI 节点
    ScrollContainer scrollContainer;
    Control scaledContentRoot;
    ConsoleRenderSurface consoleRenderSurface;  // Canvas 后端画布

    // 行管理
    Dictionary<int, ConsoleDisplayLine> lineObjects;
    SortedSet<int> lineNumbers;
    List<ConsoleLineLayoutEntry> lineLayoutEntries;

    // 纹理生命周期
    Dictionary<int, List<SpriteManager.TextureInfo>> lineTexturePins;
    List<SpriteManager.TextureInfo> cbgTexturePins;

    // 渲染后端选择
    ConsoleRenderBackend consoleRenderBackend;  // Canvas 或 Control
}
```

**关键方法**:
- `Update()` — 每帧检测 dirty 标志，拉取新行
- `AddLine(ConsoleDisplayLine)` — 添加显示行到渲染队列
- `RemoveLine(int lineNo)` — 移除行并释放纹理 pin
- `HandleContentPointerInput()` — 触摸/鼠标输入路由
- `RefreshCBG()` — 刷新背景图层
- `GetSpriteTexture()` — sprite 名称 → Godot Texture2D 转换

## 引擎核心类

### GlobalStatic

**文件**: `Scripts/Emuera/GlobalStatic.cs`  
**职责**: 全局单例引用容器（引擎级 Service Locator）

```csharp
static class GlobalStatic
{
    static MainWindow MainWindow;
    static EmueraConsole Console;
    static Process Process;
    static GameBase GameBaseData;
    static ConstantData ConstantData;
    static VariableData VariableData;
    static VariableEvaluator VEvaluator;
    static IdentifierDictionary IdentifierDictionary;
    static ExpressionMediator EMediator;
    static LabelDictionary LabelDictionary;
}
```

### Process

**文件**: `Scripts/Emuera/GameProc/Process.cs` (partial, 6 文件)  
**职责**: 游戏主进程 — 状态机驱动的脚本解释执行器

```csharp
internal sealed partial class Process
{
    EmueraConsole console;
    ExpressionMediator exm;
    ProcessState state;

    // 初始化
    void Initialize();  // 加载 CSV → VariableData → ERH → ERB

    // 执行循环 (Process.ScriptProc.cs)
    void runScriptProc();  // 逐行执行指令

    // 系统流程 (Process.SystemProc.cs)
    void SystemProc(SystemStateCode);  // TRAIN/SHOP/SAVE 状态机

    // 函数调用 (Process.CalledFunction.cs)
    void callFunction(string name);
    void callEventFunction(string name);
}
```

### EmueraConsole

**文件**: `Scripts/Emuera/GameView/EmueraConsole.cs` (partial)  
**职责**: 控制台引擎 — 管理显示行、输入状态、CBG 背景

```csharp
internal sealed partial class EmueraConsole
{
    List<ConsoleDisplayLine> displayLineList;  // 显示行历史
    PrintStringBuffer printBuffer;              // 打印缓冲
    ConsoleState state;                         // 控制台状态

    // 输出
    void Print(string str, StringStyle style);
    void PrintButton(string str, long input, StringStyle style);
    void NewLine();
    void DrawLine();

    // 输入等待
    bool IsWaitingInput { get; }
    bool IsWaitingEnterKey { get; }
    void ReadAnyKey();
    void PressEnterKey(string input, bool fromButton, int mouseVk);

    // 显示控制
    void RefreshStrings(bool force);
    void SetBackgroundColor(Color color);

    // CBG
    void CBG_SetImage(string name, int x, int y, int zdepth);
    void CBG_Clear(int z);
}
```

### ProcessState

**文件**: `Scripts/Emuera/GameProc/Process.State.cs`  
**职责**: 调用栈管理

```csharp
internal sealed class ProcessState
{
    List<CalledFunction> functionList;   // 调用栈
    LogicalLine currentLine;             // 当前执行行
    int lineCount;                       // 行计数(无限循环检测)

    void ShiftNextLine();                // 前进到下一逻辑行
    void Return(int returnValue);        // 函数返回
    void JumpTo(FunctionLabelLine);      // GOTO 跳转
    void CallFunction(CalledFunction);   // CALL 调用
    Int64 MethodReturnValue;             // #FUNCTION 返回值
}
```

### VariableData

**文件**: `Scripts/Emuera/GameData/Variable/VariableData.cs`  
**职责**: 全局变量存储

```csharp
internal sealed class VariableData
{
    // 系统变量存储
    Int64[][] intArray;     // 一维整型数组 (FLAG, ITEM, MONEY 等)
    string[][] strArray;    // 一维字符串数组

    // 角色数据
    List<CharacterData> characterList;

    // 变量令牌映射
    Dictionary<string, VariableToken> varTokenDic;

    // 操作
    void AddCharacter(CharacterData);
    void DeleteCharacter(int index);
    void SaveTo(BinaryWriter);
    void LoadFrom(BinaryReader);
}
```

### ExpressionMediator

**文件**: `Scripts/Emuera/GameData/Expression/ExpressionMediator.cs`  
**职责**: 表达式求值中介，桥接变量/控制台/进程

```csharp
internal sealed class ExpressionMediator
{
    Process Process;
    VariableEvaluator VEvaluator;
    EmueraConsole Console;

    // 表达式求值入口
    Int64 GetIntValue(IOperandTerm term);
    string GetStrValue(IOperandTerm term);
}
```

## 桥接层关键类

### uEmuera.Window.MainWindow

**文件**: `Scripts/uEmuera/Window.cs`  
**职责**: 原版 MainWindow 的 Godot 空壳实现

```csharp
public class MainWindow : IDisposable
{
    bool dirty_;
    int refreshRequestGeneration;

    int Refresh();              // 标记需要刷新（递增 generation）
    void clear_richText();     // 清屏
}
```

核心行为：引擎调用 `Refresh()` 后，`EmueraContent` 在下一帧 `_Process()` 中检测 `dirty_` 并拉取新数据。

### SpriteManager

**文件**: `Scripts/SpriteManager.cs`  
**职责**: 纹理资源池管理

- 从文件异步加载图片 → `Godot.Image`
- 延迟上传为 `ImageTexture`（在主线程）
- 引用计数 pin/unpin 机制管理 GPU 纹理生命周期
- CSV sprite 名称索引与动态 sprite 分离管理

### GenericUtils

**文件**: `Scripts/GenericUtils.cs`  
**职责**: 全局静态工具集

- 坐标换算（Content 局部 ↔ 全局）
- 主线程判定 `IsOnMainThread()`
- hover 双通道同步（pointing button + canvas visual）
- 滚动/输入 trace 开关
- 生命周期状态通知
