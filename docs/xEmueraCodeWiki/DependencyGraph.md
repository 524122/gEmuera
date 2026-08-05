# 依赖关系图

## 项目间依赖

```
XEmuera.sln
├── XEmuera (共享核心项目 - .NET Standard)
│   ├── Xamarin.Forms
│   ├── Xamarin.Essentials
│   ├── SkiaSharp
│   ├── SkiaSharp.Views.Forms
│   └── Microsoft.VisualBasic (日文字符转换)
│
├── XEmuera.Android (Android 平台项目)
│   ├── → XEmuera (项目引用)
│   ├── Xamarin.Forms
│   └── SkiaSharp.Views.Forms
│
└── XEmuera.iOS (iOS 平台项目)
    ├── → XEmuera (项目引用)
    ├── Xamarin.Forms
    └── SkiaSharp.Views.Forms
```

## 核心模块依赖关系

```
                    ┌──────────────┐
                    │     App      │ (入口点)
                    └──────┬───────┘
                           │
                    ┌──────▼───────┐
                    │   MainPage   │ (导航页)
                    └──────┬───────┘
                           │
              ┌────────────▼────────────┐
              │      MainWindow         │ (游戏主窗口)
              └────────────┬────────────┘
                           │
              ┌────────────▼────────────┐
              │     EmueraConsole       │ (核心控制台)
              └──┬──────────────────┬───┘
                 │                  │
    ┌────────────▼───────┐  ┌──────▼────────────┐
    │      Process       │  │ PrintStringBuffer  │
    │  (脚本执行引擎)     │  │ (输出缓冲)         │
    └──┬─────┬───────┬───┘  └───────────────────┘
       │     │       │
       │     │  ┌────▼──────────────────┐
       │     │  │    ProcessState       │
       │     │  │  (调用栈/状态机)       │
       │     │  └───────────────────────┘
       │     │
       │  ┌──▼──────────────────────────┐
       │  │   ExpressionMediator        │
       │  │  (表达式求值中介)            │
       │  └──────────────┬──────────────┘
       │                 │
       │     ┌───────────▼──────────────┐
       │     │   VariableEvaluator      │
       │     │  (变量访问/角色管理)       │
       │     └───────────┬──────────────┘
       │                 │
       │     ┌───────────▼──────────────┐
       │     │      VariableData        │
       │     │  (变量存储)               │
       │     └──────────────────────────┘
       │
  ┌────▼─────────────┐    ┌──────────────────┐
  │   ErbLoader      │───▶│ LabelDictionary  │
  │  (脚本加载)       │    │ (函数注册表)      │
  └──────────────────┘    └──────────────────┘
```

## GlobalStatic - 全局引用枢纽

`GlobalStatic` 持有所有核心单例的引用，按创建顺序：

```
GlobalStatic
├── .MainWindow          → MainWindow
├── .Console             → EmueraConsole
├── .Process             → Process
├── .GameBaseData        → GameBase
├── .ConstantData        → ConstantData
├── .VariableData        → VariableData
├── .VEvaluator          → VariableEvaluator
├── .IdentifierDictionary → IdentifierDictionary
├── .EMediator           → ExpressionMediator
└── .LabelDictionary     → LabelDictionary
```

注意：下层对象可能引用上层对象时获得 null（按创建顺序，下方的更晚创建）。

## 表达式求值依赖链

```
IOperandTerm.GetIntValue(ExpressionMediator)
    │
    ├── VariableTerm → VariableToken.GetIntValue()
    │                     └── VariableData (读取实际存储)
    │
    ├── FunctionMethodTerm → FunctionMethod.GetIntValue()
    │                           └── 可能回调 ExpressionMediator
    │
    └── BinaryTerm → 左操作数.GetIntValue() ○ 右操作数.GetIntValue()
```

## 指令执行依赖链

```
InstructionLine.Function.Instruction.DoInstruction(exm, func, state)
    │
    ├── exm.Console.*       → 输出控制
    ├── exm.VEvaluator.*    → 变量读写
    ├── exm.Process.*       → 流程控制
    ├── state.*             → 调用栈操作
    └── func.Argument.*     → 参数取值
```

## 平台抽象依赖

```
共享代码 (XEmuera)
    │
    ├── IPlatformService (接口)
    │       │
    │       ├── Android: PlatformService (实现)
    │       └── iOS: PlatformService (实现)
    │
    ├── GameUtils.PlatformService (运行时绑定)
    │
    └── Xamarin.Forms DependencyService (注入)
```

## 第三方依赖

| 库 | 用途 |
|----|------|
| Xamarin.Forms | 跨平台 UI 框架 |
| Xamarin.Essentials | 平台功能（文件访问、屏幕等） |
| SkiaSharp | 2D 图形渲染引擎 |
| SkiaSharp.Views.Forms | SkiaSharp 的 Xamarin.Forms 集成 |
| Microsoft.VisualBasic | 日文假名转换 (StrConv) |
| System.Drawing.Common | Color/Point/Rectangle 等基础类型 |
