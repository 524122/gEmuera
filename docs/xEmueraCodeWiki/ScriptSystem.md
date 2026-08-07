# ERB/CSV 脚本加载与解析系统

## 概述

XEmuera 运行的游戏由两部分脚本组成：
- **CSV 文件** - 数据定义（角色属性名、物品名、常量等）
- **ERB 文件** - 游戏逻辑脚本（函数定义、流程控制、输出等）

## 目录结构约定

```
游戏根目录/
├── csv/              # CSV 数据文件
│   ├── gamebase.csv  # 游戏基本信息
│   ├── ABL.csv       # 能力名定义
│   ├── TALENT.csv    # 素质名定义
│   ├── PALAM.csv     # 参数名定义
│   ├── ITEM.csv      # 物品名定义
│   ├── CHARA*.csv    # 角色定义
│   └── ...
├── erb/              # ERB 脚本文件
│   ├── SYSTEM_TITLE.ERB
│   ├── EVENTCOM.ERB
│   ├── COM*.ERB
│   └── ...
├── resources/        # 图片资源
├── sound/            # 音频资源
├── dat/              # 存档数据
└── emuera.config     # 配置文件
```

## CSV 加载流程

CSV 文件由 `ConstantData` 和 `GameBase` 类加载：

1. `GameBase` 读取 `gamebase.csv`，获取游戏标题、作者、版本等
2. `ConstantData` 读取各数据 CSV，建立名称→索引映射
3. 角色 CSV (`CHARA*.csv`) 定义预设角色数据

## ERB 加载流程

### 阶段一：头文件加载 (HeaderFileLoader)

加载 `.ERH` 文件，处理全局声明：
- `#DIM` - 声明整型变量
- `#DIMS` - 声明字符串变量
- 全局宏定义

### 阶段二：ERB 脚本加载 (ErbLoader)

```
ErbLoader.loadErbs()
    │
    ├── 遍历 erb/ 目录下所有 .ERB 文件
    │   (支持子目录递归，可按配置排序)
    │
    └── 对每个文件调用 loadErb()
        │
        ├── 逐行读取文件
        ├── 识别 @函数名 行 → 创建 FunctionLabelLine
        ├── 识别 $标签名 行 → 创建 GotoLabelLine
        ├── 普通行 → 通过 LogicalLineParser 解析
        └── 注册到 LabelDictionary
```

### 阶段三：延迟解析

参数不在加载时解析，而是在首次执行时解析（`ArgumentParser.SetArgumentTo`），节省加载时间。

## LogicalLine 类型体系

```
LogicalLine (抽象基类)
├── InstructionLine       # 指令行 (PRINT, IF, CALL 等)
├── FunctionLabelLine     # 函数标签行 (@函数名)
├── GotoLabelLine         # 跳转标签行 ($标签名)
├── NullLine              # 空行/文件结束
└── InvalidLine           # 错误行
```

### InstructionLine 关键属性
```csharp
FunctionIdentifier Function;  // 指令标识
FunctionCode FunctionCode;    // 指令代码
Argument Argument;            // 解析后的参数（延迟解析）
StringStream RawStream;       // 原始参数文本
FunctionLabelLine ParentLabelLine; // 所属函数
```

## 解析器组件

### LexicalAnalyzer (词法分析)
将源代码文本分解为 `Word` 序列：
- `IdentifierWord` - 标识符
- `OperatorWord` - 运算符
- `LiteralIntegerWord` - 整数字面量
- `LiteralStringWord` - 字符串字面量

### LogicalLineParser (行解析器)
将文本行解析为 `LogicalLine`：
- 识别指令名 → 查找 `FunctionIdentifier`
- 识别赋值语句 (变量 = 表达式)
- 识别前置自增/自减 (++/-- 变量)
- 处理 `#` 特殊指令行（#SINGLE, #DIM 等）

### ExpressionParser (表达式解析器)
解析数学/逻辑表达式为 `IOperandTerm` 树：
- 运算符优先级处理
- 函数调用表达式
- 变量引用
- 三元运算符

### ArgumentParser (参数解析器)
为每种指令类型解析其特定的参数格式。

## LabelDictionary (标签字典)

所有解析完成的函数标签注册到 `LabelDictionary`：
- 按名称查找函数
- 事件函数列表（同名多个实现，按 PRI/LATER 排序）
- 系统函数查找

## 关键枚举

### FunctionCode (部分)
```
PRINT, PRINTL, PRINTFORM, PRINTBUTTON,
IF, ELSEIF, ELSE, ENDIF,
REPEAT, REND, FOR, NEXT,
CALL, JUMP, GOTO, RETURN,
BEGIN, QUIT,
INPUT, INPUTS, WAIT,
SAVEDATA, LOADDATA,
SETCOLOR, SETBGCOLOR, FONTSTYLE,
DRAWLINE, ALIGNMENT, REDRAW,
...
```

## _Rename.csv 与 _Replace.csv

- `_Rename.csv` - 标识符重命名（允许脚本使用别名）
- `_Replace.csv` - 宏替换（文本级别替换）

## 编码支持

ERB 文件支持多种编码：
- Shift-JIS (传统日文编码)
- UTF-8 (现代编码)
- 通过配置中的 ANSI 设置选择东亚语言编码
