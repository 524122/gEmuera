# 术语表

## ERA/Emuera 领域术语

| 术语 | 说明 |
|------|------|
| **ERA** | 一种日本同人游戏引擎格式，原始实现为 eramaker |
| **Emuera** | ERA 游戏的开源模拟器 (Emulator of ERA)，Windows 桌面版 |
| **XEmuera** | Emuera 的跨平台移动端移植，本项目 |
| **ERB** | ERA 脚本文件，包含游戏逻辑 |
| **CSV** | ERA 数据定义文件，定义变量名、角色数据等 |
| **ERH** | ERA 头文件，用于宏定义和常量 |

## 脚本系统术语

| 术语 | 说明 |
|------|------|
| **InstructionLine** | 命令行，如 PRINT、IF、CALL 等 |
| **FunctionLabelLine** | 函数定义行，以 `@` 开头 |
| **GotoLabelLine** | 跳转标签行，以 `$` 开头 |
| **LogicalLine** | 逻辑行的抽象基类 |
| **Event函数** | 系统事件函数，如 @EVENTSHOP、@EVENTTRAIN |
| **System函数** | 系统内置函数，如 @SYSTEM_TITLE |
| **#SINGLE** | 事件函数修饰符，表示只执行第一个匹配 |
| **#LATER** | 事件函数修饰符，表示在最后执行 |
| **#PRI** | 事件函数修饰符，表示优先执行 |
| **#FUNCTION/#FUNCTIONS** | 用户自定义返回值函数 (Int64/string) |

## 变量系统术语

| 术语 | 说明 |
|------|------|
| **VariableCode** | 变量标识枚举 |
| **VariableToken** | 变量运行时实例 |
| **CharaVariable** | 角色变量，每个角色一份 (如 ABL, TALENT) |
| **LOCAL/LOCALS** | 函数局部变量 (整型/字符串) |
| **ARG/ARGS** | 函数参数变量 |
| **GLOBAL/GLOBALS** | 全局持久变量 |
| **SAVEDATA** | 存档数据操作 |
| **REF变量** | 引用变量，通过 `#DIM REF` 声明 |

## 游戏流程术语

| 术语 | 说明 |
|------|------|
| **SystemStateCode** | 游戏状态机代码 |
| **BEGIN TRAIN** | 进入训练流程 |
| **BEGIN SHOP** | 进入商店流程 |
| **BEGIN ABLUP** | 进入能力提升流程 |
| **BEGIN TURNEND** | 进入回合结束流程 |
| **BEGIN AFTERTRAIN** | 进入训练后流程 |
| **UPCHECK** | 参数变动检查 |
| **COM** | 训练命令 |
| **CHARA** | 角色 |
| **TARGET/MASTER/ASSI** | 目标/主人/助手角色索引 |

## 显示系统术语

| 术语 | 说明 |
|------|------|
| **ConsoleDisplayLine** | 显示行 |
| **ConsoleButtonString** | 可点击按钮区域 |
| **ConsoleStyledString** | 带样式的文本片段 |
| **PrintStringBuffer** | 打印缓冲区 |
| **CBG (Client BackGround)** | 客户端背景图层系统 |
| **DisplayLineAlignment** | 行对齐方式 (LEFT/CENTER/RIGHT) |
| **StringMeasure** | 文本测量工具 |

## 平台相关术语

| 术语 | 说明 |
|------|------|
| **Xamarin.Forms** | 跨平台 UI 框架 |
| **SkiaSharp** | 2D 图形渲染库 |
| **SKCanvas** | SkiaSharp 画布对象 |
| **SKCanvasView** | SkiaSharp 的 Xamarin 视图控件 |
| **DependencyService** | Xamarin 依赖注入服务 |
| **IPlatformService** | 平台抽象接口 |
| **EraPictureBox (mainPicBox)** | 主渲染画布 |

## 代码命名约定

| 前缀/后缀 | 含义 |
|-----------|------|
| `exm` | ExpressionMediator 实例 |
| `vEvaluator` | VariableEvaluator 实例 |
| `idDic` | IdentifierDictionary 实例 |
| `wc` | WordCollection (词法分析结果) |
| `st` / `stream` | StringStream (字符流) |
| `func` | FunctionIdentifier 或 InstructionLine |
| `trerror` | Lang.Error 的别名 |
| `trsl` | Lang.SystemLine 的别名 |
| `Int1D` / `Str2D` | 变量维度和类型 (Int/Str + 1D/2D/3D) |
| `Chara*` | 角色关联变量 |
| `SP` | Special (特殊命令参数类) |
