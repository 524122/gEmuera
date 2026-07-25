# 依赖关系图

## 整体模块依赖

```
┌─────────────────────────────────────────────────────────────────────────┐
│                      Godot Engine (C# Runtime)                           │
└────────────────────────────────┬────────────────────────────────────────┘
                                 │
┌────────────────────────────────▼────────────────────────────────────────┐
│                      Godot 宿主层 (Scripts/)                             │
│  ┌──────────────┐  ┌──────────────┐  ┌─────────────────────────────┐   │
│  │ EmueraMain   │  │EmueraContent │  │   GodotHost/                │   │
│  │ (场景根节点)  │  │(UI 表面)     │  │   LifecycleComponent        │   │
│  │              │  │              │  │   StartupComponent           │   │
│  │              │  │              │  │   GpuRenderComponent         │   │
│  │              │  │              │  │   TextRenderComponent        │   │
│  └──────┬───────┘  └──────┬───────┘  └─────────────────────────────┘   │
│         │                  │                                            │
│  ┌──────▼──────────────────▼───────────────────────────────────────┐    │
│  │              EmueraThread (后台脚本线程)                          │    │
│  └──────────────────────────┬──────────────────────────────────────┘    │
└─────────────────────────────┼───────────────────────────────────────────┘
                              │
┌─────────────────────────────▼───────────────────────────────────────────┐
│                      桥接层 (Scripts/uEmuera/)                           │
│  ┌──────────┐  ┌──────────┐  ┌──────────┐  ┌────────────┐             │
│  │ Window   │  │ Drawing  │  │  Forms   │  │ Properties │             │
│  │ 窗口桥接  │  │ GDI 替代  │  │ 控件替代  │  │ 属性兼容    │             │
│  └──────────┘  └──────────┘  └──────────┘  └────────────┘             │
└─────────────────────────────┬───────────────────────────────────────────┘
                              │
┌─────────────────────────────▼───────────────────────────────────────────┐
│                      引擎核心层 (Scripts/Emuera/)                        │
│  ┌──────────────────────────────────────────────────────────────────┐   │
│  │                    GlobalStatic (全局引用枢纽)                     │   │
│  └──┬───────────┬────────────┬───────────┬────────────┬─────────────┘   │
│     │           │            │           │            │                  │
│  ┌──▼──┐  ┌────▼────┐  ┌───▼────┐  ┌───▼────┐  ┌───▼──────────┐      │
│  │Config│  │GameProc │  │GameData│  │GameView│  │   Content    │      │
│  │配置   │  │执行流程  │  │数据定义 │  │显示引擎 │  │   资源管理   │      │
│  └──────┘  └────┬────┘  └───┬────┘  └───┬────┘  └──────────────┘      │
│                 │           │            │                               │
│            ┌────▼────┐  ┌──▼─────┐  ┌───▼──────────┐                   │
│            │Process  │  │Variable│  │EmueraConsole │                   │
│            │脚本执行  │  │变量系统 │  │控制台核心     │                   │
│            └─────────┘  └────────┘  └──────────────┘                   │
└─────────────────────────────────────────────────────────────────────────┘
```

## GlobalStatic 引用枢纽

所有引擎级单例通过 `GlobalStatic` 集中访问：

```csharp
GlobalStatic
├── .MainWindow          → uEmuera.Window.MainWindow (桥接窗口)
├── .Console             → EmueraConsole (控制台核心)
├── .Process             → Process (脚本执行引擎)
├── .GameBaseData        → GameBase (游戏基本信息)
├── .ConstantData        → ConstantData (CSV 常量)
├── .VariableData        → VariableData (变量存储)
├── .VEvaluator          → VariableEvaluator (变量访问器)
├── .IdentifierDictionary → IdentifierDictionary (标识符字典)
├── .EMediator           → ExpressionMediator (表达式中介)
└── .LabelDictionary     → LabelDictionary (函数注册表)
```

## 层间依赖规则

| 层 | 可引用 | 禁止引用 |
|----|--------|----------|
| Godot 宿主层 | 桥接层、引擎核心层、Godot API | 无 |
| 桥接层 | 引擎核心层 | Godot API (通过委托回调) |
| 引擎核心层 | Sub/、_Library/ | Godot API、桥接层 |

**关键约束**: 引擎核心层不引用任何 Godot 类型。所有 Godot 交互通过 `uEmuera.Window.MainWindow` 的桥接回调完成。

## 详细依赖链

### 脚本执行依赖链

```
Process.runScriptProc()
  → InstructionLine.Function.Instruction.DoInstruction(exm, func, state)
      ├── exm (ExpressionMediator)
      │   ├── .Console → EmueraConsole (输出指令)
      │   ├── .VEvaluator → VariableEvaluator (变量读写)
      │   └── .Process → Process (流程控制)
      ├── state (ProcessState)
      │   ├── .ShiftNextLine() (推进)
      │   ├── .Return() (返回)
      │   └── .CallFunction() (调用)
      └── func.Argument (参数数据)
```

### 表达式求值依赖链

```
IOperandTerm.GetIntValue(ExpressionMediator)
  ├── VariableTerm
  │   └── VariableToken.GetIntValue()
  │       └── VariableData (读取存储)
  ├── FunctionMethodTerm
  │   └── FunctionMethod.GetIntValue()
  │       └── 可回调 ExpressionMediator
  └── BinaryTerm
      └── left.GetIntValue() OP right.GetIntValue()
```

### 渲染刷新依赖链

```
EmueraConsole (后台线程)
  → Print/NewLine/Refresh
  → displayLineList 修改
  → MainWindow.Refresh() → dirty = true

EmueraContent._Process() (主线程)
  → 检测 dirty
  → 拉取 displayLineList 差量
  → 更新 lineObjects / lineLayoutEntries
  → ConsoleRenderSurface.QueueRedraw()
  → _Draw() 绘制
```

### 输入传递依赖链

```
触摸事件 (Godot InputEvent)
  → EmueraContent.HandleContentPointerInput()
  → 按钮命中测试 → input value
  → EmueraThread.Input(value, fromButton, skip, mouseVk)
  → inputEvent.Set() (唤醒后台线程)
  → EmueraConsole.PressEnterKey(input)
  → Process 从 RESULT/RESULTS 读取
```

## 第三方依赖

| 库/系统 | 用途 | 替代来源 |
|---------|------|----------|
| Godot 4.7 (.NET) | 运行时、渲染、输入、音频 | 替代 Xamarin + SkiaSharp |
| System.Data.SQLite | SAVEDATA SQL 扩展 | NativeLibs/ 目录内置 |
| gdUnit4 | 单元测试框架 | addons/ 目录 |
| Jolt Physics | 物理引擎 (项目配置, 未实际使用) | Godot 内置 |

## 工具依赖

| 工具 | 用途 |
|------|------|
| `patch_getvarf.py` | 自动修补源码兼容差异 |
| `install.cmd` | 初始化本地环境 |
| `config.toml` | 项目级行为配置 |

## 引擎核心内部依赖

```
Config ──────────────────── 被所有模块读取 (只读)
   │
GameBase ←── ConstantData    CSV 数据加载阶段
   │              │
   ▼              ▼
IdentifierDictionary ←── VariableData
   │                          │
   ▼                          ▼
LabelDictionary         VariableEvaluator
   │                          │
   ▼                          ▼
ErbLoader              ExpressionMediator
   │                     ╱         ╲
   ▼                    ▼           ▼
Process ◀─────── EmueraConsole   VariableToken
   │              (输出)           (数据访问)
   │
   ▼
ProcessState → CalledFunction → LogicalLine → Instruction
```

## 循环依赖处理

引擎中存在两个受控循环引用：

1. **Process ↔ EmueraConsole**: Process 通过 Console 输出，Console 通过 inputEvent 唤醒 Process。解耦方式：Console 不直接调用 Process 方法，仅通过事件信号唤醒。

2. **ExpressionMediator ↔ Process**: 表达式求值可能触发用户函数调用 (#FUNCTION)。解耦方式：ExpressionMediator 持有 Process 弱引用，通过 `state.CallFunction()` 间接调用。
