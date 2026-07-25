# 关键类与函数说明

## 顶层控制类

### `App` (App.xaml.cs)
Xamarin.Forms 应用入口。构造函数中异步加载 `GameUtils.Load()`，设置 `MainPage`。

### `MainPage` (MainPage.xaml.cs)
`FlyoutPage` 侧滑导航页面，管理游戏列表和导航。

### `MainWindow` (Emuera/Forms/MainWindow.xaml.cs)
游戏主窗口，核心职责：
- 调用 `Program.Init()` 初始化引擎
- 管理 SkiaSharp 画布 (`mainPicBox`) 和 UI 布局
- 处理触摸事件（点击、长按、滑动）
- 运行时字体缩放 (`ApplyRuntimeDisplayScale`)
- 虚拟控制器管理

关键方法：
- `Load()` - 静态工厂方法，创建并返回 MainWindow 实例
- `InitGameView()` - 初始化画布布局
- `InitEmuera()` - 启动 EmueraConsole
- `Close()` - 清理并退出游戏

### `Program` (Emuera/Program.cs)
引擎初始化入口，关键职责：
- 确定游戏目录结构 (csv/, erb/, resources/, sound/ 等)
- 校验必要目录存在
- 加载配置文件
- 检测多重实例

关键字段：
- `ExeDir` - 游戏根目录
- `CsvDir`, `ErbDir`, `ContentDir`, `MusicDir` - 各子目录路径
- `Reboot` - 重启标志
- `DebugMode` - 调试模式标志

### `GlobalStatic` (Emuera/GlobalStatic.cs)
全局状态容器，持有所有引擎级单例引用：
```csharp
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
```

## 游戏执行类

### `Process` (Emuera/GameProc/Process.cs)
游戏主进程，partial class 分布在多个文件：
- `Process.cs` - 构造函数、Initialize、基础方法
- `Process.ScriptProc.cs` - `runScriptProc()` 脚本执行主循环
- `Process.SystemProc.cs` - 系统流程状态机 (TRAIN/SHOP/SAVE 等)
- `Process.State.cs` - `ProcessState` 状态管理、调用栈
- `Process.CalledFunction.cs` - 函数调用帧

### `ProcessState` (Emuera/GameProc/Process.State.cs)
管理执行栈：
- `functionList` - 函数调用栈
- `CurrentLine` - 当前执行行
- `ShiftNextLine()` - 推进到下一行
- `Return()` - 函数返回

### `EmueraConsole` (Emuera/GameView/EmueraConsole.cs)
控制台引擎核心，partial class：
- 管理显示行列表 (`displayLineList`)
- 输入状态管理 (`ConsoleState`)
- CBG 背景图层系统
- 刷新定时器

关键方法：
- `Initialize()` - 初始化 Process 并开始执行
- `Print()` / `PrintButton()` - 文本输出
- `NewLine()` - 换行
- `ReadAnyKey()` / `ReadLine()` - 输入等待
- `RefreshStrings()` - 刷新显示
- `Quit()` - 退出

### `ErbLoader` (Emuera/GameProc/ErbLoader.cs)
ERB 脚本加载器：
- `loadErbs()` - 批量加载 ERB 文件
- `loadErb()` - 加载单个 ERB 文件，解析为 `LogicalLine` 序列

### `HeaderFileLoader` (Emuera/GameProc/HeaderFileLoader.cs)
ERH 头文件加载器，处理 `#DIM` / `#DIMS` 全局变量定义。

## 数据管理类

### `VariableData` (Emuera/GameData/Variable/VariableData.cs)
全局变量存储，管理所有系统/用户变量：
- 整型数组 (1D/2D/3D)
- 字符串数组 (1D/2D/3D)
- 角色变量
- 用户自定义变量
- `varTokenDic` - 变量名→VariableToken 映射

### `VariableEvaluator`
变量求值器，提供高级变量操作：
- 角色管理 (添加/删除/排序)
- 存档读写
- 特殊变量计算

### `ExpressionMediator` (Emuera/GameData/Expression/ExpressionMediator.cs)
表达式求值中介：
- 持有 `Process`、`VariableEvaluator`、`EmueraConsole` 引用
- 提供字符串转换 (假名变换)
- 控制台输出辅助
- BAR 字符串创建

### `IdentifierDictionary` (Emuera/GameData/IdentifierDictionary.cs)
标识符字典：
- 函数名查找 (`GetFunctionIdentifier`)
- 变量名校验
- 局部变量管理
- 用户标签名检查

## 显示渲染类

### `ConsoleDisplayLine` (Emuera/GameView/ConsoleDisplayLine.cs)
表示一个显示行：
- 包含一组 `ConsoleButtonString`
- 对齐方式 (LEFT/CENTER/RIGHT)
- `DrawTo()` - 绘制到 SKCanvas

### `ConsoleButtonString` (Emuera/GameView/ConsoleButtonString.cs)
可交互按钮区域，包含样式化文字片段。

### `PrintStringBuffer` (Emuera/GameView/PrintStringBuffer.cs)
打印缓冲区，累积文字后统一生成 `ConsoleDisplayLine`。

### `GraphicsImage` (Emuera/Content/GraphicsImage.cs)
封装 SkiaSharp 位图，提供创建/绘制/释放能力。

## 工具类

### `GameUtils` (Utils.cs)
应用级工具：
- `MainPage` / `MainLayout` / `MainPicBox` - UI 引用
- `PlatformService` - 平台服务接口
- `Load()` - 加载应用资源
- `IsEmueraPage` - 当前页面状态

### `DisplayUtils` (Utils.cs)
显示相关工具：
- `ScreenDensity` - 屏幕密度
- `PicBoxWidth` / `PicBoxHeight` - 画布尺寸

### `IPlatformService` (Utils.cs)
平台抽象接口：
- `GetStoragePath()` - 获取存储路径
- `CloseApplication()` - 关闭应用
- `LockScreenOrientation()` / `UnlockScreenOrientation()` - 屏幕旋转
- `NeedStoragePermissions()` - 权限检查
