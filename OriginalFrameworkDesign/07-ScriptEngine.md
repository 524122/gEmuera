# 脚本执行引擎

## 概述

gEmuera-future 的脚本引擎是一个 ERB/CSV 解释器，在后台线程上运行，支持完整的 ERA 游戏脚本语义：函数调用栈、事件系统、流程控制、表达式求值和变量系统。

## ERB/CSV 脚本格式

### CSV - 数据定义

```csv
; ABL.csv - 能力名定义
0,体力
1,知力
2,魅力
```

```csv
; CHARA0.csv - 角色定义
番号,0
名前,主人公
基礎,0,100
基礎,1,80
```

### ERB - 游戏逻辑

```erb
@SYSTEM_TITLE
PRINTL ERA ゲーム タイトル
PRINTBUTTON [開始], 1
PRINTBUTTON [続きから], 2
WAIT

@EVENTTRAIN
PRINTL ターン {COUNT} 開始
CALL SHOW_STATUS
CALL SHOW_COMMAND
INPUT
; ...
```

## 加载流程

```
Process.Initialize()
├── 1. 加载 CSV 数据
│   ├── GameBase ← gamebase.csv (标题、版本)
│   ├── ConstantData ← ABL.csv, TALENT.csv, ITEM.csv, ...
│   └── CharacterData ← CHARA*.csv (预设角色)
│
├── 2. 创建 VariableData (分配变量存储)
│
├── 3. 加载 ERH 头文件 (HeaderFileLoader)
│   └── 处理 #DIM / #DIMS 全局变量声明
│
└── 4. 加载 ERB 脚本 (ErbLoader)
    ├── 递归扫描 erb/ 目录
    ├── 逐文件解析为 LogicalLine 序列
    ├── 注册函数标签到 LabelDictionary
    └── 支持延迟解析 (首次执行时解析参数)
```

### 延迟解析 (Lazy Parsing)

```csharp
// 加载时只识别指令名，不解析参数
InstructionLine line = new InstructionLine(funcId, rawStream);
// line.Argument == null

// 首次执行时解析
if (line.Argument == null)
    ArgumentParser.SetArgumentTo(line);  // 解析参数并缓存
```

优势：加载速度快（大型游戏可能有数万行 ERB），未执行的分支永远不解析。

## LogicalLine 类型体系

```
LogicalLine (抽象基类)
├── InstructionLine       // 指令行: PRINT, IF, CALL, ...
│   ├── FunctionIdentifier Function    // 指令标识
│   ├── FunctionCode FunctionCode      // 指令枚举
│   ├── Argument Argument              // 解析后参数 (延迟)
│   └── StringStream RawStream         // 原始文本
├── FunctionLabelLine     // 函数定义: @函数名
│   ├── LabelName                      // 函数名
│   ├── IsEvent                        // 是否事件函数
│   └── Attributes (#SINGLE, #PRI...) // 函数修饰
├── GotoLabelLine         // 跳转标签: $标签名
├── NullLine              // 空行/文件结束标记
└── InvalidLine           // 解析错误行 (跳过)
```

## 执行主循环

```csharp
// Process.ScriptProc.cs
private void runScriptProc()
{
    while (true)
    {
        state.ShiftNextLine();        // 推进到下一行
        checkInfiniteLoop();          // 每 10000 行检测一次

        LogicalLine line = state.CurrentLine;

        if (line is InstructionLine func)
        {
            // 延迟解析参数
            if (func.Argument == null)
                ArgumentParser.SetArgumentTo(func);

            // 分发执行
            if (func.Function.Instruction != null)
                func.Function.Instruction.DoInstruction(exm, func, state);
            else if (func.Function.IsFlowControl())
                doFlowControlFunction(func);
            else
                doNormalFunction(func);
        }
        else if (line is NullLine || line is FunctionLabelLine)
        {
            // 函数体结束或遇到下一个函数标签 → 返回
            state.Return(0);
        }

        // 检查退出条件
        if (!console.IsRunning || state.ScriptEnd)
            return;
    }
}
```

## ProcessState - 调用栈管理

```csharp
sealed class ProcessState
{
    List<CalledFunction> functionList;   // 调用栈 (栈顶 = 最后元素)
    LogicalLine currentLine;            // 当前执行行
    int lineCount;                      // 累计行数 (无限循环检测)
    Int64 methodReturnValue;            // #FUNCTION 返回值

    void ShiftNextLine()                // 前进到下一逻辑行
    void Return(int returnValue)        // 函数返回, 弹出栈顶
    void JumpTo(FunctionLabelLine)      // GOTO 跳转 (不入栈)
    void CallFunction(CalledFunction)   // CALL 调用 (入栈)
}
```

### CalledFunction (调用帧)

```csharp
class CalledFunction
{
    FunctionLabelLine TopLabel;      // 函数入口
    LogicalLine CurrentLine;         // 当前行
    LogicalLine[] Lines;             // 函数体所有行
    int CurrentIndex;                // 当前行索引
    VariableLocal LocalVars;         // 局部变量 (LOCAL, ARG)
}
```

## 系统状态机

游戏有预定义的系统流程，通过 `SystemStateCode` 管理：

```
┌─────────────────────────────────────────────┐
│              SystemStateCode                  │
├─────────────────────────────────────────────┤
│ Title_Begin → Opening                        │
│   └─ 调用 @SYSTEM_TITLE                      │
│                                              │
│ Train_Begin → Train_CallEventTrain           │
│   → Train_CallShowStatus                     │
│   → Train_CallComAbleXX                      │
│   → Train_CallShowUserCom                    │
│   → Train_WaitInput                          │
│   → Train_CallEventCom                       │
│   → Train_CallComXX                          │
│   → Train_CallSourceCheck                    │
│   → Train_CallEventComEnd                    │
│   → (循环回 Train_CallEventTrain)            │
│                                              │
│ Shop_Begin → Shop_CallEventShop              │
│   → Shop_CallShowShop                        │
│   → Shop_WaitInput → Shop_CallEventBuy       │
│                                              │
│ Normal (自由态, 可 BEGIN/SAVE)               │
└─────────────────────────────────────────────┘
```

### BEGIN 指令转换

```erb
BEGIN TRAIN    ; → 进入训练流程
BEGIN SHOP     ; → 进入商店流程
BEGIN ABLUP    ; → 进入能力提升
BEGIN TURNEND  ; → 回合结束处理
```

## 函数调用模型

### 普通调用 (CALL/JUMP)

```erb
CALL FUNCTION_NAME, arg1, arg2   ; 入栈, 等待返回
JUMP FUNCTION_NAME               ; 跳转, 不返回当前位置
```

### 事件调用

系统自动调用所有同名事件函数，按优先级排序：

```
@EVENTTRAIN (脚本A, #PRI)   → 第1个执行
@EVENTTRAIN (脚本B)          → 第2个执行
@EVENTTRAIN (脚本C, #LATER)  → 最后执行
```

事件修饰符：
- `#PRI` - 优先执行
- `#LATER` - 后置执行
- `#SINGLE` - 只执行一个
- `#ONLY` - 独占 (只执行自己)

### #FUNCTION 表达式函数

```erb
@MY_CALC, ARG:0, ARG:1
#FUNCTION
RETURNF ARG:0 + ARG:1

; 调用方
PRINTFORML 结果 = {MY_CALC(3, 5)}   ; → 结果 = 8
```

## 指令系统

### 指令注册

```csharp
// FunctionIdentifier 注册表
static Dictionary<string, FunctionIdentifier> identifiers;

// 每个指令关联一个 IInstruction 实现
interface IInstruction
{
    void DoInstruction(ExpressionMediator exm, InstructionLine func, ProcessState state);
}
```

### 指令分类

| 类别 | 指令示例 | 实现位置 |
|------|---------|---------|
| 流程控制 | IF/ELSE/ENDIF, FOR/NEXT, WHILE/WEND | doFlowControlFunction() |
| 输出 | PRINT/PRINTL/PRINTFORM/PRINTBUTTON | Instraction.Child.cs |
| 输入 | INPUT/INPUTS/WAIT/WAITANYKEY | Instraction.Child.cs |
| 变量 | VARSET/SWAP/SPLIT/TOSTR | Instraction.Child.cs |
| 角色 | ADDCHARA/DELCHARA/PICKUPCHARA | Instraction.Child.cs |
| 存档 | SAVEDATA/LOADDATA | Instraction.Child.cs |
| 显示 | SETCOLOR/SETBGCOLOR/FONTSTYLE/ALIGNMENT | Instraction.Child.cs |
| 图形 | CBGSETG/SPRITECREATE/SETIMGLAYER | Instraction.Child.cs |
| 调试 | DEBUGPRINT/THROW/ASSERT | Instraction.Child.cs |

## 表达式系统

### 表达式节点 (IOperandTerm)

```
IOperandTerm<T> (T = Int64 或 string)
├── ConstTerm          // 常量: 42, "hello"
├── VariableTerm       // 变量引用: FLAG:3, ABL:TARGET:0
├── BinaryTerm         // 二元运算: a + b, a && b
├── UnaryTerm          // 一元运算: -a, !a
├── TernaryTerm        // 三元: a ? b : c
├── FunctionMethodTerm // 内置函数: ABS(x), SUBSTRING(s,i,n)
└── UserDefinedMethodTerm // #FUNCTION: MY_FUNC(args)
```

### 运算符优先级

从低到高：`||` → `&&` → `|` → `^` → `&` → `==`/`!=` → `<`/`>`/`<=`/`>=` → `<<`/`>>` → `+`/`-` → `*`/`/`/`%`

## 无限循环检测

```csharp
void checkInfiniteLoop()
{
    state.lineCount++;
    if (state.lineCount % 10000 != 0)
        return;

    long elapsed = stopwatch.ElapsedMilliseconds;
    if (elapsed > Config.InfiniteLoopAlertTime)
    {
        // 抛出警告, 允许用户选择继续或终止
        throw new InfiniteLoopException();
    }
}
```

## 性能优化

| 优化项 | 说明 |
|--------|------|
| 延迟解析 | 只在首次执行时解析参数，加载阶段零开销 |
| SelectCase 跳表 | `SelectCaseJumpTable` 将多分支 SELECT CASE 编译为 Dictionary 跳表 |
| StringName 缓存 | 变量名/函数名使用 `StringName` 比较 |
| 稀疏数组 | `SparseArray` 用于大型稀疏变量 |
| 批量 CSV 加载 | CSV 加载阶段一次性分配所有变量存储 |
