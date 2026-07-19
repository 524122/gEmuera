# 术语表

## ERA/Emuera 领域

| 术语 | 说明 |
|------|------|
| **ERA** | 日本同人游戏引擎格式，原始实现为 eramaker |
| **Emuera** | ERA 游戏开源模拟器 (Emulator of ERA)，Windows 桌面版 |
| **XEmuera** | Emuera 的 Xamarin.Forms 跨平台移动端移植 |
| **gEmuera** | 本项目，Emuera 的 Godot 4 + C# 重新实现 |
| **ERB** | ERA 脚本文件，包含游戏逻辑代码 |
| **CSV** | ERA 数据定义文件，定义变量名、角色数据等 |
| **ERH** | ERA 头文件，用于 `#DIM` 宏定义和全局常量声明 |
| **eraFL** | 一个广泛使用的 ERA 游戏实例，以复杂 HTML/图片/多角色著称 |

## 脚本系统

| 术语 | 说明 |
|------|------|
| **LogicalLine** | 脚本逻辑行的抽象基类 |
| **InstructionLine** | 命令行 (PRINT, IF, CALL 等) |
| **FunctionLabelLine** | 函数定义行，以 `@` 开头 |
| **GotoLabelLine** | 跳转标签行，以 `$` 开头 |
| **FunctionCode** | 内置指令的枚举标识 |
| **FunctionIdentifier** | 指令注册信息 (名称 → 执行方法) |
| **LabelDictionary** | 全局函数/标签注册表 |
| **Event 函数** | 系统事件函数 (如 @EVENTSHOP)，同名可存在多个实现 |
| **#SINGLE** | 事件函数修饰，只执行第一个匹配 |
| **#PRI** | 事件函数修饰，优先执行 |
| **#LATER** | 事件函数修饰，最后执行 |
| **#FUNCTION** | 声明为有返回值的表达式函数 (Int64) |
| **#FUNCTIONS** | 声明为有返回值的表达式函数 (String) |

## 变量系统

| 术语 | 说明 |
|------|------|
| **VariableCode** | 变量标识枚举 |
| **VariableToken** | 变量运行时访问接口 |
| **CharacterData** | 单个角色的全部变量数据容器 |
| **TARGET/MASTER/ASSI** | 当前目标/主人/助手角色索引 |
| **RESULT/RESULTS** | 函数返回值 (整型/字符串) |
| **FLAG/TFLAG** | 通用标志 (持久/临时) |
| **ABL/TALENT/PALAM** | 角色能力/素质/参数 |
| **LOCAL/LOCALS** | 函数内局部变量 |
| **REF** | 引用类型变量，传址语义 |

## 游戏流程

| 术语 | 说明 |
|------|------|
| **SystemStateCode** | 游戏顶层状态机代码 |
| **BEGIN TRAIN** | 进入训练流程 |
| **BEGIN SHOP** | 进入商店流程 |
| **ProcessState** | 执行引擎的调用栈和当前行状态 |
| **CalledFunction** | 调用栈中的一个帧 |
| **InputRequest** | 脚本请求用户输入的封装 |
| **ConsoleState** | 控制台的 I/O 状态 (执行中/等待输入/等待按键) |

## 渲染系统

| 术语 | 说明 |
|------|------|
| **ConsoleDisplayLine** | 一个渲染行 (包含多个按钮区域) |
| **ConsoleButtonString** | 可点击的文本区域 |
| **ConsoleStyledString** | 带样式的文本片段 |
| **ConsoleImagePart** | 内联图片显示 |
| **ConsoleDivPart** | HTML div 容器 (可绝对/相对定位) |
| **ConsoleShapePart** | 几何形状 (矩形等) |
| **PrintStringBuffer** | 打印输出缓冲区 |
| **CBG** | Client BackGround，背景图层系统 |
| **Canvas 后端** | gEmuera 使用 `_Draw()` 直绘的渲染模式 |
| **Control 后端** | 退化为 Godot Control 节点树的渲染模式 |

## gEmuera 架构

| 术语 | 说明 |
|------|------|
| **EmueraMain** | Godot 场景根节点，管理 GPU 工作队列 |
| **EmueraThread** | 后台线程，运行 ERB 脚本引擎 |
| **EmueraContent** | Godot 侧表现层，渲染控制台和处理输入 |
| **EmueraConsole** | 引擎侧控制台核心 (状态管理、输出缓冲) |
| **uEmuera** | 桥接层命名空间，模拟 WinForms/GDI+ API |
| **GodotHost** | Godot 宿主组件集 (生命周期、启动、GPU、文本渲染) |
| **ConsoleRenderSurface** | Canvas 模式的绘图表面 |
| **SpriteManager** | 纹理/精灵资源的生命周期管理 |
| **VirtualCursor** | 移动端触摸板式虚拟光标 |
| **Scalepad** | 缩放控制面板 |
| **QuickButtons** | 快捷操作按钮条 |
| **Inputpad** | INPUT 时弹出的输入面板 |

## Godot 概念

| 术语 | 说明 |
|------|------|
| **Node** | Godot 场景树基本单位 |
| **Control** | UI 节点基类 |
| **ScrollContainer** | 可滚动容器 |
| **CanvasLayer** | 独立绘制层 (用于 UI 覆盖) |
| **_Process()** | 每帧回调 (主线程) |
| **_Draw()** | 自定义绘制回调 |
| **_Input()** | 输入事件回调 |
| **SubViewport** | 离屏渲染目标 |
| **ImageTexture** | 从 Image 创建的动态纹理 |
| **AudioStreamPlayer** | 音频播放节点 |
| **call_deferred()** | 延迟到帧末安全时刻执行 |
| **CallDeferred()** | C# 版延迟调用 |

## 代码命名约定

| 前缀/后缀 | 含义 |
|-----------|------|
| `exm` | ExpressionMediator 实例 |
| `vEvaluator` | VariableEvaluator 实例 |
| `idDic` | IdentifierDictionary 实例 |
| `wc` | WordCollection (词法分析结果) |
| `func` | FunctionIdentifier 或 InstructionLine |
| `state` | ProcessState (调用栈) |
| `console` | EmueraConsole 实例 |
| `Int1D/Str2D` | 变量维度和类型 |
| `Chara*` | 角色关联变量/类 |
| `CBG*` | 背景图层相关方法 |
| `canvas*` | Canvas 渲染后端相关字段 |
| `line*` | 显示行相关字段 |
