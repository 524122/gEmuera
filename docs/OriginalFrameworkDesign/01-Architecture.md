# 分层架构设计

## 总体分层

gEmuera-future 采用四层架构，每层职责分明，信号/数据单向流动：

```
┌─────────────────────────────────────────────────────────────┐
│              Godot Platform Layer (平台层)                    │
│  Godot Engine Runtime / Mobile Renderer / Input System       │
│  ─ 提供渲染管线、输入事件、音频、文件系统                       │
└────────────────────────────┬────────────────────────────────┘
                             │
┌────────────────────────────▼────────────────────────────────┐
│           Presentation Layer (表现层)                         │
│  EmueraContent / ConsoleRenderSurface / VirtualCursor        │
│  Inputpad / QuickButtons / Scalepad / OptionWindow           │
│  ─ Godot Control/Canvas 混合渲染、用户交互、UI 布局            │
└────────────────────────────┬────────────────────────────────┘
                             │
┌────────────────────────────▼────────────────────────────────┐
│            Bridge Layer (桥接层)                              │
│  uEmuera/ (Window, Drawing, Forms, Media, Utils)             │
│  EmueraThread / GenericUtils / SpriteManager                 │
│  ─ 将 WinForms/SkiaSharp API 映射为 Godot 等价操作            │
└────────────────────────────┬────────────────────────────────┘
                             │
┌────────────────────────────▼────────────────────────────────┐
│            Engine Core Layer (引擎核心层)                     │
│  Scripts/Emuera/                                             │
│  ┌──────────┬───────────┬──────────┬──────────┬──────────┐  │
│  │ GameProc │ GameData  │ GameView │ Content  │  Config  │  │
│  │ 执行流程  │ 数据/表达式│ 控制台逻辑 │ 资源定义  │ 配置管理 │  │
│  └──────────┴───────────┴──────────┴──────────┴──────────┘  │
│  ─ 纯 C# 逻辑，不依赖 Godot API，可独立测试                   │
└─────────────────────────────────────────────────────────────┘
```

## 数据流向

### 输出流 (脚本 → 显示)

```
ERB 脚本执行 PRINT/PRINTBUTTON/HTML 等指令
    │
    ▼ (脚本线程)
EmueraConsole.Print() → PrintStringBuffer 累积
    │
    ▼ NewLine() 生成 ConsoleDisplayLine
ConsoleDisplayLine 添加到 displayLineList
    │
    ▼ Refresh() 标记 dirty
MainWindow.Refresh() → dirty_ = true
    │
    ▼ (主线程，Godot _Process)
EmueraContent.Update() 检测 dirty
    │
    ├─→ Canvas 后端: ConsoleRenderSurface._Draw() 批量绘制
    └─→ Control 后端: 创建/更新 Godot Control 节点
```

### 输入流 (用户 → 脚本)

```
用户触摸/点击/键盘输入
    │
    ▼ (主线程)
EmueraContent.HandleContentPointerInput()
    │
    ▼ 确定点击的按钮或输入文本
EmueraThread.Input(text, from_button, skip, mouseVk)
    │
    ▼ inputEvent.Set() 唤醒脚本线程
Process.runScriptProc() 继续执行
    │
    ▼ 读取 RESULT/RESULTS 变量获取输入值
```

### 资源流 (文件 → GPU)

```
ERB 脚本调用图片相关指令 (SPRITELOAD, CBG 等)
    │
    ▼ (脚本线程)
AppContents / GraphicsImage 发起加载请求
    │
    ▼ SpriteManager 异步解码
后台线程解码图片为 Godot.Image
    │
    ▼ (主线程)
EmueraContent._Process() 检查完成队列
    │
    ▼ 创建 ImageTexture 上传 GPU
纹理绑定到 Canvas overlay 或 Control 节点
```

## 关键边界

| 边界 | 左侧 | 右侧 | 通信方式 |
|------|-------|-------|----------|
| 线程边界 | 脚本线程 (EmueraThread) | 主线程 (Godot) | ManualResetEventSlim + ConcurrentQueue |
| 引擎/平台边界 | Scripts/Emuera/ | Scripts/uEmuera/ | 桥接接口 (Drawing, Forms, Window) |
| 渲染后端边界 | ConsoleDisplayLine 数据 | Canvas/Control 节点 | EmueraContent 路由决策 |
| 纹理生命周期边界 | SpriteManager 持有逻辑引用 | Godot Texture2D GPU 资源 | TextureInfo pin/unpin 机制 |

## 目录结构映射

```
gEmuera-future/
├── Scripts/
│   ├── Emuera/                    # 引擎核心层 (从 XEmuera 移植)
│   │   ├── Config/                # 配置管理
│   │   ├── Content/               # 资源定义与字体
│   │   ├── GameData/              # 数据模型与表达式系统
│   │   │   ├── Expression/        # 表达式解析与求值
│   │   │   ├── Function/          # 内置函数定义
│   │   │   └── Variable/          # 变量系统
│   │   ├── GameProc/              # 执行流程
│   │   │   └── Function/          # 指令实现
│   │   ├── GameView/              # 控制台逻辑 (非 Godot 依赖)
│   │   ├── Modern/                # 扩展功能 (SQL 等)
│   │   ├── Runtime/               # 运行时工具 (插件系统)
│   │   ├── Sub/                   # 基础工具 (异常、IO、词法)
│   │   └── _Library/              # 系统工具 (GDI 桥、随机数)
│   ├── uEmuera/                   # 桥接层
│   │   ├── partial/               # 核心类的 Godot 侧 partial 扩展
│   │   ├── Window.cs              # MainWindow/DebugDialog 桥接
│   │   ├── Drawing.cs             # Color/Font/Bitmap 桥接
│   │   ├── Forms.cs               # MessageBox/Timer 桥接
│   │   ├── Media.cs               # 音频桥接
│   │   └── Utils.cs               # 路径/编码工具
│   ├── GodotHost/                 # Godot 宿主组件
│   │   ├── EmueraStartupComponent.cs
│   │   ├── EmueraLifecycleComponent.cs
│   │   ├── EmueraGpuRenderComponent.cs
│   │   └── EmueraTextRenderComponent.cs
│   ├── EmueraMain.cs              # 主节点 (入口/GPU队列)
│   ├── EmueraThread.cs            # 脚本线程管理
│   ├── EmueraContent.cs           # 表现层核心
│   ├── EmueraContent.Canvas.cs    # Canvas 渲染后端
│   └── ...                        # 其他 UI 组件
├── Resources/                     # Godot 资源 (字体/图标/着色器)
├── Fonts/                         # 内置字体文件
├── addons/                        # Godot 插件 (gdUnit4 测试)
└── project.godot                  # Godot 项目配置
```
