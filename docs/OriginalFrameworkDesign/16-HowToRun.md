# 构建与运行指南

## 环境要求

### 必须

- **Godot 4.7** (.NET / C# 版本)
- **.NET 8.0 SDK**
- **操作系统**: Windows 10+, macOS, Linux (开发), Android (目标)

### 可选

- Visual Studio 2022 / JetBrains Rider (C# IDE)
- Android SDK (导出 APK)
- Godot Export Templates

## 项目结构

```
gEmuera-future/
├── project.godot           # Godot 项目配置
├── gemuera-c#.csproj       # C# 项目文件
├── gemuera-c#.sln          # 解决方案文件
├── main.tscn               # 主游戏场景
├── first_window.tscn       # 游戏选择场景
├── Scripts/                 # C# 源码
│   ├── Emuera/             # 引擎核心 (从 XEmuera 移植)
│   ├── uEmuera/            # 平台桥接层
│   ├── GodotHost/          # Godot 宿主组件
│   ├── Diagnostics/        # 诊断工具
│   └── *.cs                # 顶层 UI/工具脚本
├── Resources/              # 内置资源 (字体、图标、语言)
├── Fonts/                  # 字体文件
├── Icons/                  # UI 图标
├── Lang/                   # 多语言文本
├── Text/                   # 文本资源
├── NativeLibs/             # 原生库 (SQLite 等)
├── addons/                 # Godot 插件 (gdUnit4)
├── patches/                # 补丁文件
└── export_presets.cfg      # 导出预设
```

## 编辑器运行

### 步骤

1. 用 Godot 4.7 (.NET) 打开 `project.godot`
2. 等待 C# 解决方案编译完成
3. 按 F5 运行项目
4. 在 FirstWindow 中选择游戏目录
5. 游戏开始执行

### 游戏数据要求

需要一套 ERA 游戏文件：

```
你的游戏目录/
├── csv/              # 必需 — 数据定义
│   ├── gamebase.csv  # 游戏基本信息
│   ├── ABL.csv       # 能力名
│   ├── CHARA*.csv    # 角色定义
│   └── ...
├── erb/              # 必需 — 脚本逻辑
│   ├── SYSTEM_TITLE.ERB
│   └── ...
├── resources/        # 可选 — 图片资源
├── sound/            # 可选 — 音频
└── emuera.config     # 可选 — 引擎配置
```

## 编译

### 命令行编译 C#

```bash
dotnet build gemuera-c#.sln
```

### Godot 编辑器内编译

- 菜单 → Build → Build Project
- 或 Ctrl+Shift+B

## 导出 Android APK

### 准备

1. 安装 Android SDK (API 21+)
2. 配置 Godot Export Templates
3. 生成签名密钥

### 导出

1. Project → Export → Android
2. 选择 export_presets.cfg 中的预设
3. Export Project

### 关键导出设置

```
// export_presets.cfg 注意项
renderer = "mobile"              // 使用移动端渲染器
vram_compression = "etc2/astc"   // 移动端纹理压缩
permissions = ["MANAGE_EXTERNAL_STORAGE"]  // 存储权限
```

## 运行时配置

### config.toml (项目根目录)

Godot 端运行参数：

```toml
[display]
font_scale = 1.0

[performance]
max_fps = 60
```

### emuera.config (游戏目录)

ERA 引擎参数，参见 13-ConfigSystem.md。

## 调试

### Godot Debugger

- 断点调试 C# 代码 (需 IDE 附加)
- Godot 内置 Profiler 查看帧时间
- Remote Inspector 查看节点树

### 运行时诊断面板

```csharp
// RuntimeDiagnosticsPanel
// 在游戏运行时显示性能指标
// 通过 config.toml [diagnostics] enable_panel = true 开启
```

### 日志系统

```csharp
// DiagnosticLogRouter → DiagnosticLogSinks
// 分级日志: Debug, Info, Warn, Error
// 输出: 控制台 + 文件
```

### ERB 调试模式

```csharp
// EmueraMain.debug = true 时启用:
// - 脚本行号追踪
// - 变量监视
// - 断点支持 (ERB DEBUGPRINT)
```

## 常见问题

### "csv フォルダが見つかりません"

游戏目录下找不到 csv 文件夹。确认选择的目录包含 csv/ 子目录。

### 字体显示为方块

游戏使用了自定义字体但 gEmuera 未找到。将字体文件放入游戏目录的 font/ 或 fonts/ 子目录。

### Android 上无法访问文件

需要赋予存储权限。Android 11+ 需要 MANAGE_EXTERNAL_STORAGE 权限。

### 脚本无限循环

调整 emuera.config 中 `InfiniteLoopAlertTime` 的值。默认 5000ms 后报警。

### 内存不足 (Android)

- 减少显示行历史保留数
- 降低纹理缓存上限
- 关闭不必要的诊断功能

## 测试

### 单元测试

项目使用 gdUnit4 插件：

```
addons/gdUnit4/    # 测试框架
```

### 手动测试

使用不同 ERA 游戏验证兼容性：
- eraFL (复杂 HTML/div/图片)
- eraTW (大量角色/变量)
- 基础 ERA 游戏 (核心指令覆盖)
