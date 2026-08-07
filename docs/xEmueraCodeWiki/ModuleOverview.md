# 主要模块职责

## 模块概览图

```
┌────────────────────────────────────────────────────────────────┐
│                        XEmuera 共享项目                          │
├────────────┬──────────────┬──────────────┬─────────────────────┤
│  Drawing/  │   Forms/     │   Models/    │     Views/          │
│  绘图工具   │  自定义控件   │  数据模型     │    XAML页面          │
├────────────┴──────────────┴──────────────┴─────────────────────┤
│                        Emuera/ (引擎核心)                        │
├──────────┬───────────┬──────────┬──────────┬──────────┬────────┤
│ Config/  │ Content/  │  Forms/  │GameData/ │GameProc/ │GameView│
│ 配置管理  │ 资源管理   │ 游戏窗口  │数据定义   │执行流程   │显示渲染 │
├──────────┴───────────┴──────────┴──────────┴──────────┴────────┤
│          Sub/ (异常/IO)          │      _Library/ (工具/多语言)   │
└──────────────────────────────────┴─────────────────────────────┘
```

## 各模块详细职责

### Drawing/ - 绘图工具层
- `DrawBitmapUtils.cs` - SkiaSharp 位图绘制封装（矩形填充、图片绘制）
- `DrawTextUtils.cs` - 文字绘制、测量封装

### Emuera/Config/ - 配置管理
- `Config.cs` - 静态配置属性（字体、颜色、窗口尺寸等）
- `ConfigCode.cs` - 配置项枚举定义
- `ConfigData.cs` - 配置文件加载/保存 (emuera.config)
- `ConfigItem.cs` - 单个配置项的数据模型
- `KeyMacro.cs` - 键盘宏定义

### Emuera/Content/ - 资源管理
- `AppContents.cs` - 资源总管理器，管理图片资源加载/卸载
- `GraphicsImage.cs` - 可绘制的图形图像（基于 SkiaSharp）
- `CroppedImage.cs` - 裁剪图像
- `ConstImage.cs` - 常量图像
- `AContentFile.cs` / `AContentItem.cs` - 资源抽象基类

### Emuera/Forms/ - 游戏窗口
- `MainWindow.xaml.cs` - 主游戏窗口，处理触摸输入、UI布局、缩放
- `MainWindow.cs` - MainWindow 的业务逻辑部分（初始化 Emuera、输入处理）
- `EraPictureBox.cs` - SkiaSharp 画布控件，承载游戏渲染
- `ConfigDialog.cs` - 配置对话框
- `DebugDialog.cs` - 调试对话框

### Emuera/GameData/ - 数据定义与表达式系统
- `GameBase.cs` - 游戏基本信息（标题、作者、版本等，来自 gamebase.csv）
- `ConstantData.cs` - 常量数据（来自 CSV 文件）
- `IdentifierDictionary.cs` - 标识符字典（变量名、函数名注册与查找）
- `ParserMediator.cs` - 解析中介，管理警告输出和重命名表
- `StrForm.cs` - 字符串格式化处理

#### GameData/Expression/ - 表达式系统
- `ExpressionParser.cs` - 表达式解析器
- `ExpressionMediator.cs` - 表达式求值中介（变量查询桥接）
- `IOperandTerm.cs` - 操作数接口定义
- `Term.cs` - 各种表达式节点实现
- `OperatorMethod.cs` - 运算符方法实现
- `CaseExpression.cs` - CASE 表达式

#### GameData/Function/ - 内置函数定义
- `Creator.cs` / `Creator.Method.cs` - 内置函数创建器
- `FunctionMethod.cs` - 函数方法实现
- `FunctionMethodTerm.cs` - 函数作为表达式项

#### GameData/Variable/ - 变量系统
- `VariableData.cs` - 变量数据存储（全局变量注册表）
- `VariableCode.cs` - 变量代码枚举（数百个系统变量）
- `VariableEvaluator.cs` - 变量求值器
- `VariableToken.cs` - 变量令牌基类和各种派生类
- `VariableParser.cs` - 变量表达式解析
- `CharacterData.cs` - 角色数据结构
- `VariableLocal.cs` - 局部变量管理

### Emuera/GameProc/ - 游戏执行流程
- `Process.cs` - 游戏主进程（partial class 核心）
- `Process.ScriptProc.cs` - 脚本执行循环
- `Process.SystemProc.cs` - 系统流程（TRAIN/SHOP/SAVE等状态机）
- `Process.State.cs` - 进程状态管理 (`ProcessState`, `SystemStateCode`)
- `Process.CalledFunction.cs` - 函数调用栈管理
- `ErbLoader.cs` - ERB 脚本文件加载器
- `HeaderFileLoader.cs` - ERH 头文件加载器
- `LogicalLine.cs` - 逻辑行定义（指令行、标签行等）
- `LogicalLineParser.cs` - 逻辑行解析器
- `LabelDictionary.cs` - 标签/函数字典
- `UserDefinedFunction.cs` - 用户自定义函数
- `InputRequest.cs` - 输入请求管理

#### GameProc/Function/ - 指令系统
- `Instruction.cs` - 指令执行接口
- `Instraction.Child.cs` - 各指令的具体实现
- `FunctionIdentifier.cs` - 函数标识符（内置命令注册）
- `BuiltInFunctionCode.cs` - 内置函数代码枚举
- `ArgumentParser.cs` - 参数解析器
- `Argument.cs` / `ArgumentBuilder.cs` - 参数数据结构

### Emuera/GameView/ - 控制台显示引擎
- `EmueraConsole.cs` - 控制台核心（状态管理、CBG背景图）
- `EmueraConsole.Print.cs` - 打印输出逻辑
- `EmueraConsole.ex.cs` - 控制台扩展功能
- `PrintStringBuffer.cs` - 打印缓冲区
- `ConsoleDisplayLine.cs` - 显示行
- `ConsoleButtonString.cs` - 按钮字符串（可点击区域）
- `ConsoleStyledString.cs` - 样式文字
- `ConsoleImagePart.cs` - 图像显示部件
- `ConsoleShapePart.cs` - 形状显示部件
- `StringMeasure.cs` - 字符串测量
- `StringStyle.cs` - 文字样式定义
- `HtmlManager.cs` - HTML 标记处理
- `ButtonStringCreator.cs` - 按钮字符串工厂

### Emuera/Sub/ - 基础工具
- `EmueraException.cs` - 自定义异常类型 (CodeEE, ExeEE)
- `EraBinaryDataReader.cs` - 二进制存档读取

### Emuera/_Library/ - 工具库
- `Sys.cs` - 系统工具（路径、平台判断）
- `EvilMask/Lang.cs` - 多语言支持系统
- `EvilMask/Utils.cs` - 通用工具函数
