# AGENTS.md

本文面向 AI 编码 Agent。开始任何任务前，请先阅读本文、`IDEAS.md` 和 `CODE_MAP.md`。

## 项目概览

`gEmuera` 是基于 Godot 4.6 + C# 的 Emuera 文字游戏引擎跨平台移植版。

Emuera 是日本 eramaker 系列文字游戏的执行引擎，通过解析 `.ERB` 脚本文件和 `.CSV` 数据文件来运行游戏。本项目将原版 Windows Forms / GDI+ 渲染架构替换为 Godot 节点系统，实现了桌面端（Windows/Linux）和 Android 移动端的跨平台支持。

项目规模：
- C# 文件数：约 148 个
- C# 代码行数：约 82,000 行
- 主仓库：`https://github.com/wwwXiaoHan17/gEmuera`
- 默认协作分支：`dev`

## 技术栈

| 层级 | 技术 |
|------|------|
| 游戏引擎 | Godot 4.6（.NET / Mono 版本） |
| 编程语言 | C# |
| 目标框架 | .NET 9.0（桌面端 .NET 8.0 亦可，Android 构建需要 .NET 9.0） |
| NuGet 依赖 | `GodotSharp` 4.6.2、`Godot.SourceGenerators` 4.6.2、`Microsoft.Data.Sqlite` 8.0.0、`SQLitePCLRaw.bundle_e_sqlite3` 2.1.6 |
| 着色器 | Godot GDShader（`canvas_item` 类型，用于 ColorMatrix 颜色变换） |
| 物理引擎 | Jolt Physics 3D（项目以 2D UI 为主，使用默认配置） |
| 版本控制 | Git |

## 关键配置文件

| 文件 | 说明 |
|------|------|
| `project.godot` | Godot 项目主配置。主场景为 `first_window.tscn`，渲染方法 `mobile`，Windows 使用 D3D12 驱动 |
| `export_presets.cfg` | 导出预设。当前配置了 Android APK 导出（`gemuera.apk`），架构 `arm64-v8a`，包名 `com.godot.gemuera` |
| `config.toml` | 运行期诊断配置。控制日志级别、模块开关、触摸诊断、图片诊断、UI 几何诊断、性能采样、诊断包导出等。是排查问题的核心入口 |
| `.editorconfig` | 仅指定 `charset = utf-8` |
| `.gitignore` | 忽略 `.godot/`、`action_maps/`、游戏内容目录 `era*/`、导出产物 `*.apk` 等 |

注意：**本项目没有手工维护的 `.csproj` 或 `.sln` 文件**。Godot 编辑器会自动生成构建所需的 `.csproj` 到 `.godot/mono/temp/` 目录下，并通过 `dotnet build` 编译。

## 构建与运行

### 环境要求

- Godot 4.6（.NET 版本）
- .NET 8.0 SDK（桌面端）
- .NET 9.0 SDK（Android 构建必需）

### 构建命令

```bash
# 桌面端（通过 dotnet）
dotnet build

# Android 端（通过 dotnet）
dotnet build -p:GodotTargetPlatform=android

# 也可以通过 Godot 编辑器导出界面生成 APK
```

### 运行方式

1. 用 Godot 4.6 (.NET) 打开项目
2. 将游戏文件夹（文件夹名必须以 `era` 开头）放到正确位置：
   - **桌面端**：与可执行文件同目录，或 Godot 项目 `res://` 目录下
   - **Android**：`/storage/emulated/0/emuera/`
3. 运行项目，在启动界面 `first_window.tscn` 中选择游戏

## 代码组织与模块划分

```
Scripts/
├── *.cs                        # Godot 主入口、UI、线程桥、渲染、精灵管理、输入面板等
├── Diagnostics/                # 运行期诊断、日志路由、内存环形日志、导出、面板、输入回放
├── Emuera/
│   ├── Config/                 # 配置系统（Config、ConfigCode、ConfigData 等）
│   ├── Content/                # 图片、精灵、Graphics surface、ColorMatrix 绘制
│   ├── GameData/               # 变量、表达式、常量、函数方法、角色数据
│   │   ├── Expression/         # 表达式解析与求值
│   │   ├── Function/           # 内置函数方法（Creator、FunctionMethod 等）
│   │   └── Variable/           # 变量系统（VariableData、VariableEvaluator 等）
│   ├── GameProc/               # ERB 加载、逻辑行解析、Label 索引、脚本执行状态机、懒加载
│   ├── GameView/               # 控制台显示模型（EmueraConsole、ConsoleDisplayLine、HtmlManager 等）
│   ├── Modern/                 # 现代扩展函数（ModernSqlManager 等）
│   ├── Runtime/                # SQLite 运行时、插件系统
│   ├── Sub/                    # 词法分析、二进制流、异常、存档读写
│   └── _Library/               # Win/GDI/随机数/语言兼容工具
├── Shaders/
│   └── color_matrix.gdshader   # 5x5 ColorMatrix GPU 着色器
└── uEmuera/                    # System.Drawing / System.Windows.Forms 兼容层
```

核心运行链：

```
project.godot
  -> first_window.tscn
  -> FirstWindow._Ready()          扫描 era* 游戏目录
  -> main.tscn
  -> EmueraMain._Ready()           初始化 GPU 队列、EmueraContent
  -> EmueraThread.Start()          启动后台线程
  -> Program.Main()                创建 MainWindow / EmueraConsole / Process
  -> Process.Initialize()          读取 config / csv / erb，建立 LabelDictionary
  -> Process.DoScript()            执行 ERB 指令
  -> EmueraConsole / GenericUtils / EmueraContent   输出与交互
```

### 线程模型

引擎采用**双线程架构**：

- **Godot 主线程**：UI 渲染、输入处理、GPU ColorMatrix 工作队列、`SpriteManager.UpdateOtherThreads()`（每帧最多加载 1 张纹理）
- **后台线程**（`EmueraThread`）：ERB 脚本执行、精灵合成、纹理文件 I/O、阻塞式输入等待

跨线程通信：
- 后台 → 主线程：`GenericUtils.uiQueue`（`ConcurrentQueue<Action>`）
- 主线程 → 后台：`EmueraThread.Input()` + `ManualResetEventSlim`
- GPU 工作：`EmueraMain.gpuQueue`（仅桌面端）

## 开发约定

### 首要目标

- **Android / 手机端是首要运行和测试目标**。任何方案默认以导出 APK 后可用为准。
- 桌面端可以作为调试入口，**不能替代 Android/APK 验证结论**。
- 默认测试游戏目录（仓库主人环境）：`E:\Godot_v4.6.2-stable_mono_win64\snake\EraTW-Magic_DLC-update_base`

### 代码修改原则

- **优先保持现有效果和兼容行为**，不为了重构而重构。
- 不要破坏原 Emuera 行为、Snake 兼容逻辑、移动端布局、输入流程、图片/精灵渲染。
- 修改前先判断责任边界：UI 层、`GameView`、`GameProc`、`GameData`、`Content`、`uEmuera` 兼容层分别处理不同问题。
- **性能默认按手机端考虑**：避免在热路径中增加无界分配、全量扫描、同步 I/O、逐帧重建节点或纹理。
- 优先沿用当前项目已有模式；只有能明显降低复杂度或重复时才新增抽象。
- **复杂逻辑需要写中文注释**，说明原因、边界和兼容性要求。
- 对跨线程、UI 队列、输入等待、日志导出、资源加载、ColorMatrix、存档兼容相关代码要**额外谨慎**。

### 注释语言

- 新写和修改的复杂逻辑注释使用**中文**。
- `Scripts/Emuera/` 核心层保留了大量原版日文注释，修改时不需要翻译，但需要保持上下文可读。
- Godot 层/UI 层注释以中文为主。

### 代码地图维护

- 修改或新增 `Scripts/**/*.cs` 后，**必须判断**是否需要同步更新 `CODE_MAP.md`。
- 如果变更影响入口流程、目录职责、接口契约、核心函数或常见问题定位，必须更新。
- 如果只是局部实现细节、注释、格式或不改变定位信息的小修，可以不更新，但要在最终说明中明确判断结果。

## 测试策略

### 测试框架

- 安装了 `gdUnit4`（v6.1.1）Godot 测试插件，主要用于 GDScript 测试。
- `addons/godot_mcp/tests/` 下包含一些 Godot MCP 插件自带的回归测试脚本（GDScript）。
- **本项目目前没有独立的 C# 单元测试项目**。验证主要依赖：
  1. `dotnet build` 编译通过
  2. Godot 编辑器内运行
  3. Android APK 导出并实机运行

### 验证要求

- 纯文档修改：检查 Markdown 结构、关键规则可搜索。
- C# 代码修改：优先运行 `dotnet build` 或最小可行检查。
- Godot UI / 渲染 / 输入修改：至少说明桌面验证结果；移动端问题必须说明 APK 是否验证。
- Android 相关修复：如果不能导出/安装/运行 APK，最终回复必须明确未验证原因。
- 日志/诊断修改：验证配置读取、日志开关、导出路径，或至少说明未验证点。

不要把“代码能编译”当作唯一完成标准。必须结合 Android/APK 和 Emuera 兼容目标判断。

## 诊断与日志

项目内置了一套企业级运行时诊断系统，用于排查移动端和生产环境问题。

### 相关文件

- `config.toml` — 诊断配置（TOML 格式）
- `Scripts/Diagnostics/` — 诊断模块实现
- `Scripts/GenericUtils.cs` — 日志桥接、UI 队列、诊断导出入口
- `DiagnosticLogExporter` / `DiagnosticLogRouter` / `RuntimeDiagnosticsConfig`

### 诊断能力

- 多语言日志（中文 / 日文 / 英文）
- 内存环形日志（默认 1000 条），避免持续写盘
- 模块级开关：general、sprite、audio、input、script、ui、file_system、load、save、config、performance、touch、statement_recognition
- 专项诊断：触摸输入、图片加载、UI 几何、性能采样、存档读写、Android 存储
- 诊断包导出：一键导出日志、配置快照、设备信息、屏幕信息、APK 信息、错误摘要
- 输入回放缓冲（最近 N 条输入摘要，不保存完整用户文本）
- 限流与脱敏：高频日志限流、路径截断、用户输入截断

### 使用建议

- Android/APK 实机游玩时，默认只保留 `Error` 级别日志，关闭高频调试，避免额外 GC 与帧尖峰。
- 排查问题时只打开正在排查的最小模块；不要一次性打开全部子项。
- 新增诊断信息前，先检查 `RuntimeDiagnosticsConfig` 和 `DiagnosticLogRouter` 是否已有分类/开关。

## Action Maps 操作日志

`action_maps/` 是本地 AI 操作日志目录，用于任务审计、回退和多 AI 协作追溯。

### 规则

- **不提交 GitHub**，不放进 PR，不能用 `git add -f action_maps/` 强行加入版本控制。
- `.gitignore` 已忽略 `action_maps/`。
- 每次 AI 执行会修改文件的任务，都必须在 `action_maps/` 中新增一份**中文**操作日志。
- 文件命名建议：`YYYY-MM-DD-NNN-任务简述.md`
- 一个任务对应一份日志；不要把多个无关任务混写到同一份日志。
- 日志文件数量**不得超过 30 个**。新增前必须统计现有数量；如果会超过，先向仓库主人确认归档/删除策略。

### 日志必须包含

- 任务目标：用户要求和本次执行范围
- 初始状态：当前分支、工作区状态、相关已有改动、日志文件计数
- 操作步骤：读取了哪些文件、执行了哪些命令、每一步结论
- 修改记录：每个被修改文件的具体改动、原因、影响范围
- 验证记录：执行过的命令、结果、失败原因、未验证内容
- `CODE_MAP.md` 判断：是否需要更新，以及原因
- 风险评估：可能影响的模块、平台、兼容行为
- 回退建议：如果出错，优先回退哪些文件或 hunk

## GitHub 协作规则

- 除仓库主人明确要求外，**不要直接向 `dev` 或主分支提交代码**。
- 每个任务从最新 `dev` 新建独立工作分支，建议 `ai/<任务简述>` 或 `fix/<问题简述>`。
- 普通协作者或 AI 完成任务后，应提交到任务分支，并发起 Pull Request 指向 `dev`。
- PR 合并前必须由仓库主人或指定维护者审核。
- **PR 标题和说明必须使用中文**。
- 每个 PR 尽量只解决一个明确问题，避免混入无关重构、格式化和资源变更。
- **禁止**擅自强制推送、硬重置、删除远端分支、回滚他人提交。

## 安全与隐私注意事项

- `config.toml` 中配置了日志脱敏策略（`[logging.redaction]`）：默认截断路径、脚本文本和用户输入，避免诊断包泄露隐私。
- Android 端游戏文件夹扫描路径为 `/storage/emulated/0/emuera/`，涉及外部存储权限。
- 诊断文件保留策略限制 `gemuera` 诊断文件总大小不超过 128MB、数量不超过 20 个，防止无限增长。
- 用户提供的游戏内容（`era*` 文件夹）不会被提交到版本控制。

## 常见问题定位入口

| 问题领域 | 入口文件/目录 |
|----------|---------------|
| Godot 主场景 | `first_window.tscn`、`main.tscn` |
| 启动器 / 游戏扫描 | `Scripts/FirstWindow.cs` |
| 主入口 / GPU 队列 | `Scripts/EmueraMain.cs` |
| 后台线程 | `Scripts/EmueraThread.cs` |
| UI 渲染 / 控制台 | `Scripts/EmueraContent.cs` |
| 脚本执行引擎 | `Scripts/Emuera/GameProc/Process*.cs` |
| 控制台输出模型 | `Scripts/Emuera/GameView/EmueraConsole*.cs` |
| 表达式 / 变量 | `Scripts/Emuera/GameData/Expression/`、`Scripts/Emuera/GameData/Variable/` |
| 图片 / 精灵 / 纹理缓存 | `Scripts/Emuera/Content/`、`Scripts/SpriteManager.cs` |
| ColorMatrix / GPU | `Scripts/ColorMatrixGPU.cs`、`Scripts/Shaders/color_matrix.gdshader` |
| 诊断 / 日志 / 导出 | `Scripts/Diagnostics/`、`Scripts/GenericUtils.cs` |
| 兼容层 | `Scripts/uEmuera/` |
| 运行时诊断配置 | `config.toml` |
| 项目约定 | `IDEAS.md` |
| 代码地图 | `CODE_MAP.md` |
| AI 工具执行指南 | `CLAUDE.md`（本文） |
