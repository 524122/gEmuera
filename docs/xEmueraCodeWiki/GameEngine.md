# 游戏执行引擎

## 概述

XEmuera 的游戏执行引擎以 `Process` 类为核心，实现了一个基于状态机的脚本解释执行器。它负责读取 ERB 脚本的逻辑行并逐行执行，管理函数调用栈，处理系统流程（如训练、商店、存档等），并与控制台进行交互。

## Process 类结构

`Process` 是 partial class，分布在 5 个文件中：

| 文件 | 职责 |
|------|------|
| `Process.cs` | 构造函数、Initialize()、基础方法 |
| `Process.ScriptProc.cs` | `runScriptProc()` 脚本指令执行主循环 |
| `Process.SystemProc.cs` | 系统流程状态机（BEGIN TRAIN/SHOP 等） |
| `Process.State.cs` | ProcessState 和 SystemStateCode 定义 |
| `Process.CalledFunction.cs` | CalledFunction 调用帧定义 |

## 执行流程

```
EmueraConsole.Initialize()
    │
    ├── new Process(this)
    ├── Process.Initialize()
    │   ├── 加载 CSV (GameBase, ConstantData)
    │   ├── 创建 VariableData
    │   ├── 加载 ERH 头文件 (HeaderFileLoader)
    │   └── 加载 ERB 脚本 (ErbLoader)
    │
    └── callEmueraProgram("")
        └── 进入系统流程状态机
            └── runScriptProc() [脚本执行循环]
```

## 脚本执行循环 (runScriptProc)

`Process.ScriptProc.cs` 中的 `runScriptProc()` 是引擎的心跳：

```csharp
private void runScriptProc()
{
    while (true)
    {
        state.ShiftNextLine();           // 推进到下一行
        checkInfiniteLoop();             // 无限循环检测（每10000行）
        LogicalLine line = state.CurrentLine;
        
        if (line is InstructionLine func)
        {
            // 解析参数（延迟解析）
            if (func.Argument == null)
                ArgumentParser.SetArgumentTo(func);
            
            // 执行指令
            if (func.Function.Instruction != null)
                func.Function.Instruction.DoInstruction(exm, func, state);
            else if (func.Function.IsFlowControl())
                doFlowControlFunction(func);
            else
                doNormalFunction(func);
        }
        else if (line is NullLine || line is FunctionLabelLine)
        {
            // 函数结束，返回
            state.Return(0);
        }
        
        if (!console.IsRunning || state.ScriptEnd)
            return;
    }
}
```

## 系统状态机 (SystemStateCode)

游戏有多种系统流程，通过 `SystemStateCode` 枚举管理：

```
Title_Begin → Openning
    │
    ├── Train_Begin → Train_CallEventTrain → Train_CallShowStatus
    │   → Train_CallComAbleXX → Train_CallShowUserCom → Train_WaitInput
    │   → Train_CallEventCom → Train_CallComXX → Train_CallSourceCheck
    │   → Train_CallEventComEnd → (循环回 Train_CallEventTrain)
    │
    ├── Shop_Begin → Shop_CallEventShop → Shop_CallShowShop
    │   → Shop_WaitInput → Shop_CallEventBuy
    │
    ├── Ablup_Begin → Ablup_CallShowJuel → Ablup_CallShowAblupSelect
    │   → Ablup_WaitInput → Ablup_CallAblupXX
    │
    ├── SaveGame_Begin → SaveGame_WaitInput → SaveGame_CallSaveInfo
    ├── LoadGame_Begin → LoadGame_WaitInput
    │
    └── Normal (自由状态，可 BEGIN/SAVE)
```

标志位：
- `__CAN_SAVE__` (0x10000) - 该状态下可以呼出存档界面
- `__CAN_BEGIN__` (0x20000) - 该状态下可以执行 BEGIN 命令

## ProcessState - 调用栈管理

```csharp
internal sealed class ProcessState
{
    List<CalledFunction> functionList;  // 调用栈
    LogicalLine currentLine;           // 当前执行行
    int lineCount;                     // 执行行计数（无限循环检测用）
    
    void ShiftNextLine();              // 前进到下一逻辑行
    void Return(int returnValue);      // 函数返回
    void JumpTo(FunctionLabelLine);    // GOTO 跳转
    void CallFunction(CalledFunction); // CALL 调用
}
```

## 指令分类

### 流程控制指令
- `IF` / `ELSEIF` / `ELSE` / `ENDIF`
- `REPEAT` / `REND` / `FOR` / `NEXT`
- `WHILE` / `WEND` / `DO` / `LOOP`
- `CALL` / `JUMP` / `GOTO` / `RETURN`
- `BEGIN` / `QUIT`

### 输出指令
- `PRINT` / `PRINTL` / `PRINTFORM` / `PRINTFORML`
- `PRINTBUTTON` / `PRINTBUTTONC`
- `PRINTPLAIN` / `DRAWLINE` / `CUSTOMDRAWLINE`
- `PRINT_ABL` / `PRINT_TALENT` 等角色信息输出

### 输入指令
- `INPUT` / `INPUTS` - 数值/文字输入
- `TINPUT` / `TINPUTS` - 限时输入
- `WAIT` / `WAITANYKEY` - 等待

### 变量操作指令
- `SWAP` / `VARSIZE`
- `SAVEDATA` / `LOADDATA`
- `SPLIT` / `SETCOLOR` / `SETBGCOLOR`

### 角色管理指令
- `ADDCHARA` / `DELCHARA` / `ADDDEFCHARA`
- `PICKUPCHARA` / `DELALLCHARA`
- `UPCHECK` / `CUPCHECK`

## 函数调用模型

ERB 脚本中的函数以 `@函数名` 定义，调用方式：
- **CALL** - 调用并等待返回（类似子程序调用）
- **JUMP** - 跳转到函数（不返回当前位置）
- **事件调用** - 系统自动调用所有同名事件函数（如 @EVENTTRAIN）

函数属性（通过 `#` 指令设置）：
- `#SINGLE` - 单一执行（只执行一个实现）
- `#LATER` - 后置执行
- `#PRI` - 优先执行
- `#ONLY` - 独占执行
- `#FUNCTION` / `#FUNCTIONS` - 声明为返回值函数

## 输入处理

引擎的输入模型是异步等待式：
1. 脚本执行到 INPUT 类指令时，设置 `InputRequest`
2. 控制台进入等待状态 (`ConsoleState.WaitInput`)
3. 用户输入后，控制台恢复执行
4. Process 从 RESULT/RESULTS 变量读取输入值

## 无限循环检测

每执行 10000 行检测一次，如果超过配置的 `InfiniteLoopAlertTime` 毫秒仍在循环，会抛出错误提示。
