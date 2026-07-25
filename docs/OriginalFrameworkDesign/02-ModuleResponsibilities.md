# 模块职责划分

## 模块总览

```
┌─────────────────────────────────────────────────────────────────┐
│                     Godot 表现层                                  │
├──────────┬──────────┬──────────┬──────────┬──────────┬──────────┤
│Emuera    │Emuera    │Virtual   │Quick     │Input     │Scale     │
│Content   │Content   │Cursor    │Buttons   │pad       │pad       │
│(.cs)     │.Canvas   │          │          │          │          │
│主表现控制 │Canvas渲染 │虚拟光标   │快捷按钮   │输入面板   │缩放面板   │
├──────────┴──────────┴──────────┴──────────┴──────────┴──────────┤
│                     GodotHost 组件                                │
├──────────┬──────────┬──────────┬──────────────────────────────────┤
│Startup   │Lifecycle │GpuRender │TextRender                        │
│初始化     │生命周期   │GPU队列   │文本渲染队列                        │
├──────────┴──────────┴──────────┴──────────────────────────────────┤
│                     桥接层 (uEmuera)                               │
├──────────┬──────────┬──────────┬──────────┬──────────┬──────────┤
│Window    │Drawing   │Forms     │Media     │Utils     │partial/  │
│窗口桥接   │绘图桥接   │表单桥接   │音频桥接   │工具桥接   │核心扩展   │
├──────────┴──────────┴──────────┴──────────┴──────────┴──────────┤
│                     引擎核心 (Emuera)                              │
├──────────┬──────────┬──────────┬──────────┬──────────┬──────────┤
│GameProc  │GameData  │GameView  │Content   │Config    │Sub       │
│执行流程   │数据模型   │控制台逻辑 │资源管理   │配置管理   │基础工具   │
└──────────┴──────────┴──────────┴──────────┴──────────┴──────────┘
```

## 各模块详细职责

### EmueraMain (主节点)

- **文件**: `Scripts/EmueraMain.cs`
- **类型**: Godot Node (场景树根节点的子节点)
- **职责**:
  - 应用入口，初始化引擎环境
  - 管理 GPU 工作队列（ColorMatrix 跨线程提交）
  - 管理文本渲染队列（后台线程请求主线程绘文字）
  - 配置字符映射表加载（Shift-JIS → UTF-8）
  - 协调 GodotHost 子组件

### EmueraThread (脚本线程)

- **文件**: `Scripts/EmueraThread.cs`
- **类型**: 纯 C# 类 (单例)
- **职责**:
  - 在独立线程中执行 `Program.Main()` 启动 ERA 引擎
  - 管理输入事件信号量 (`ManualResetEventSlim`)
  - 接收用户输入并唤醒脚本执行
  - 线程安全的启动/停止控制

### EmueraContent (表现层核心)

- **文件**: `Scripts/EmueraContent.cs` + `Scripts/EmueraContent.Canvas.cs`
- **类型**: Godot Control 节点
- **职责**:
  - 控制台行的创建、布局、滚动管理
  - Canvas/Control 双后端渲染路由
  - 触摸手势处理（点击、拖拽、缩放）
  - CBG 背景图层管理
  - 音频播放器池管理
  - 异步纹理刷新与 pin 生命周期
  - 虚拟光标和 hover 状态同步

### GodotHost 组件

| 组件 | 文件 | 职责 |
|------|------|------|
| EmueraStartupComponent | GodotHost/EmueraStartupComponent.cs | 启动流程编排（加载配置 → 初始化引擎 → 启动线程） |
| EmueraLifecycleComponent | GodotHost/EmueraLifecycleComponent.cs | Android 后台/恢复生命周期处理，降帧省电 |
| EmueraGpuRenderComponent | GodotHost/EmueraGpuRenderComponent.cs | 每帧消费 GPU 工作队列，执行 ColorMatrix 着色器 |
| EmueraTextRenderComponent | GodotHost/EmueraTextRenderComponent.cs | 每帧消费文本渲染队列，生成文字纹理 |

### 引擎核心 — GameProc (执行流程)

- **目录**: `Scripts/Emuera/GameProc/`
- **职责**:
  - `Process` — 游戏主进程，状态机驱动
  - `Process.ScriptProc` — runScriptProc() 脚本执行主循环
  - `Process.SystemProc` — BEGIN TRAIN/SHOP 等系统流程
  - `Process.State` — ProcessState 调用栈管理
  - `ErbLoader` — ERB 脚本批量加载
  - `HeaderFileLoader` — ERH 头文件加载
  - `LogicalLine` / `LogicalLineParser` — 逻辑行解析
  - `LabelDictionary` — 函数标签注册表
  - `Function/` — 指令实现 (Instruction.Child)

### 引擎核心 — GameData (数据模型)

- **目录**: `Scripts/Emuera/GameData/`
- **职责**:
  - `GameBase` — 游戏基本信息 (gamebase.csv)
  - `ConstantData` — 常量数据 (CSV 定义)
  - `IdentifierDictionary` — 标识符字典
  - `Expression/` — 表达式解析器和求值器
  - `Function/` — 内置函数定义和调用
  - `Variable/` — 变量系统完整实现

### 引擎核心 — GameView (控制台逻辑)

- **目录**: `Scripts/Emuera/GameView/`
- **职责**:
  - `EmueraConsole` — 控制台状态管理、CBG 层、输入等待
  - `EmueraConsole.Print` — 文本输出逻辑
  - `PrintStringBuffer` — 打印缓冲区
  - `ConsoleDisplayLine` — 显示行数据结构
  - `ConsoleButtonString` — 可交互按钮区域
  - `ConsoleStyledString` — 样式化文本片段
  - `ConsoleDivPart` / `ConsoleImagePart` / `ConsoleShapePart` — 复杂显示元素
  - `HtmlManager` — HTML 标记解析
  - `StringMeasure` — 文字宽度测量

### 引擎核心 — Content (资源管理)

- **目录**: `Scripts/Emuera/Content/`
- **职责**:
  - `AppContents` — 资源总管理器 (sprite 加载/卸载/懒加载索引)
  - `GraphicsImage` — 图像封装 (对接 Godot.Image)
  - `FontMeasureCache` — 字体测量缓存
  - `FontModel` — 字体模型抽象

### 引擎核心 — Config (配置管理)

- **目录**: `Scripts/Emuera/Config/`
- **职责**:
  - `Config` — 静态配置属性全局访问点
  - `ConfigData` — 配置文件加载/保存
  - `JSONConfig` / `JSONConfigData` — JSON 格式配置扩展
  - `ConfigCode` — 配置项枚举

### 桥接层 — uEmuera

- **目录**: `Scripts/uEmuera/`
- **职责**:
  - `Window.cs` — MainWindow/DebugDialog 的 Godot 侧空壳实现
  - `Drawing.cs` — Color/Font/Bitmap/Graphics 的纯数据替代
  - `Forms.cs` — MessageBox/Timer 的 Godot 等价
  - `Media.cs` — 音频播放桥接
  - `Utils.cs` — 路径处理、编码转换
  - `partial/` — 核心类的平台扩展方法

### UI 组件

| 组件 | 文件 | 职责 |
|------|------|------|
| VirtualCursor | Scripts/VirtualCursor.cs | 触摸板式虚拟鼠标光标 |
| QuickButtons | Scripts/QuickButtons.cs | 底部快捷操作按钮 |
| Inputpad | Scripts/Inputpad.cs | 数字/文本输入面板 |
| Scalepad | Scripts/Scalepad.cs | 显示缩放控制面板 |
| OptionWindow | Scripts/OptionWindow.cs | 设置窗口 |
| FirstWindow | Scripts/FirstWindow.cs | 游戏选择列表（首屏） |

### 工具/诊断

| 模块 | 目录 | 职责 |
|------|------|------|
| Diagnostics | Scripts/Diagnostics/ | 运行时诊断面板、日志路由、输入录制回放 |
| SpriteManager | Scripts/SpriteManager.cs | 纹理生命周期管理、引用计数、异步上传 |
| GenericUtils | Scripts/GenericUtils.cs | 全局工具（坐标换算、hover 通道、trace 开关） |
| FrameRateHelper | Scripts/FrameRateHelper.cs | 帧率策略管理 |
| ResolutionHelper | Scripts/ResolutionHelper.cs | 分辨率适配 |
