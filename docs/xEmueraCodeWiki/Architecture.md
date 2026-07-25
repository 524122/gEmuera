# 项目整体架构

## 架构概览

XEmuera 采用分层架构，从上到下分为：平台层 → 应用框架层 → 游戏引擎层 → 脚本执行层。

```
┌─────────────────────────────────────────────────┐
│           Platform Layer (平台层)                 │
│   XEmuera.Android / XEmuera.iOS                  │
│   - MainActivity / AppDelegate                   │
│   - Platform Renderers                           │
│   - IPlatformService 实现                         │
└──────────────────────┬──────────────────────────┘
                       │
┌──────────────────────▼──────────────────────────┐
│        Application Layer (应用框架层)             │
│   XEmuera (共享项目)                              │
│   - App.xaml.cs (入口)                           │
│   - MainPage (导航页)                            │
│   - GameUtils / DisplayUtils (工具类)            │
└──────────────────────┬──────────────────────────┘
                       │
┌──────────────────────▼──────────────────────────┐
│         Engine Layer (游戏引擎层)                 │
│   Emuera/                                        │
│   ┌─────────────┬──────────────┬───────────────┐ │
│   │  GameView   │   GameProc   │   GameData    │ │
│   │  (显示渲染)  │  (执行流程)   │  (数据模型)   │ │
│   └─────────────┴──────────────┴───────────────┘ │
│   ┌─────────────┬──────────────┬───────────────┐ │
│   │   Config    │   Content    │    Forms      │ │
│   │  (配置管理)  │  (资源管理)   │  (窗口/UI)    │ │
│   └─────────────┴──────────────┴───────────────┘ │
└──────────────────────┬──────────────────────────┘
                       │
┌──────────────────────▼──────────────────────────┐
│        Script Layer (脚本执行层)                  │
│   ERB 文件 → 解析 → LogicalLine → 指令执行       │
│   CSV 文件 → 常量/角色数据加载                    │
└─────────────────────────────────────────────────┘
```

## 解决方案结构

```
XEmuera.sln
├── XEmuera/XEmuera/              # 共享核心项目 (Xamarin.Forms)
│   ├── App.xaml.cs               # 应用入口
│   ├── MainPage.xaml.cs          # 主导航页 (FlyoutPage)
│   ├── Utils.cs                  # GameUtils, DisplayUtils, IPlatformService
│   ├── Drawing/                  # SkiaSharp 绘图工具
│   ├── Forms/                    # 自定义表单控件
│   ├── Models/                   # 数据模型
│   ├── Views/                    # XAML 视图
│   ├── Resources/                # 字体、图片、语言文件
│   └── Emuera/                   # 核心引擎 (从 Emuera 移植)
│       ├── Program.cs            # 引擎初始化入口
│       ├── GlobalStatic.cs       # 全局单例引用容器
│       ├── Config/               # 配置管理
│       ├── Content/              # 图片资源管理
│       ├── Forms/                # MainWindow (游戏窗口)
│       ├── GameData/             # 数据定义与表达式系统
│       ├── GameProc/             # 游戏执行流程
│       ├── GameView/             # 控制台显示引擎
│       ├── Sub/                  # 异常、IO工具等
│       └── _Library/             # 基础工具库、多语言支持
├── XEmuera/XEmuera.Android/      # Android 平台项目
│   ├── MainActivity.cs           # Android 入口
│   └── Renderer/                 # 平台特定渲染器
└── XEmuera/XEmuera.iOS/          # iOS 平台项目
    ├── AppDelegate.cs            # iOS 入口
    └── Renderer/                 # 平台特定渲染器
```

## 启动流程

1. **平台入口**: `MainActivity` (Android) / `AppDelegate` (iOS) 启动 Xamarin.Forms
2. **App 初始化**: `App.xaml.cs` 构造函数中异步调用 `GameUtils.Load()` 加载基础环境
3. **主页面**: 创建 `MainPage` (FlyoutPage 侧滑导航)
4. **游戏窗口**: 用户选择游戏后导航到 `MainWindow`
5. **引擎初始化**: `MainWindow` 构造中调用 `Program.Init()` → 加载配置、校验目录
6. **控制台初始化**: `EmueraConsole.Initialize()` → 创建 `Process` → 加载 ERB/CSV → 开始执行

## 核心设计理念

- **移植兼容**: 尽量保持与原版 Emuera (Windows WinForms) 的逻辑一致，注释中保留了大量原版代码
- **平台抽象**: 通过 `IPlatformService` 接口抽象平台差异（文件路径、屏幕旋转等）
- **渲染替换**: 将原版 GDI+ 渲染替换为 SkiaSharp，实现跨平台 2D 绘图
- **全局状态**: `GlobalStatic` 类集中管理所有引擎级单例引用
