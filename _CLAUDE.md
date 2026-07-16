# CLAUDE.md

本文面向 Claude Code、其他 AI CLI、AI IDE 和自动化 Agent。开始任何任务前，必须先读本文、`IDEAS.md` 和 `CODE_MAP.md`。

## 必读顺序

1. `IDEAS.md`：项目工作约定、协作规则、日志规则、GitHub 规则。
2. `CODE_MAP.md`：代码地图，用于减少无目的文件检索。
3. 本文件：面向 Claude/AI 工具的执行流程。
4. 需要改代码时，再按 `CODE_MAP.md` 定位具体源码。

如果本文与 `IDEAS.md` 冲突，以 `IDEAS.md` 为准。

## 项目定位

本项目是 Godot 4.7 + C# 的 Emuera 模拟器核心复刻/移植。项目用 Godot 节点和 C# 兼容层替代原 Windows Forms/GDI 渲染，同时保留 Emuera 脚本执行、变量、表达式、资源、控制台和存档等核心行为。

默认目标：

- 首要运行和测试目标是 Android/手机端，方案默认以导出 APK 后可用为准。
- 桌面端可以用于调试，但不能替代 Android/APK 验证结论。
- 默认测试游戏目录：`E:\Godot_v4.6.2-stable_mono_win64\snake\EraTW-Magic_DLC-update_base`。
- 参考实现：
  - `E:\MyCode\Era\emuera_lazyloading_selfmodified_version-main-skiasharp`
  - `E:\MyCode\GodotCode\gemuera\uEmuera-0.2.9d`
  - `E:\MyCode\GodotCode\gemuera\XEmuera-0.5.1`
- 涉及 Godot 架构、UI、性能、平台适配时，应使用 `godot-master` skill 辅助判断。

## 架构速览

核心运行链：

```text
project.godot
  -> first_window.tscn
  -> FirstWindow._Ready()
  -> main.tscn
  -> EmueraMain._Ready()
  -> EmueraThread.Start()
  -> Program.Main()
  -> Process.Initialize()
  -> Process.DoScript() / Process.ScriptProc.runScriptProc()
  -> EmueraConsole / GenericUtils / EmueraContent
```

核心层：

- `Scripts/`：Godot UI、主入口、线程桥、精灵缓存、输入面板、快速按钮、缩放、诊断入口。
- `Scripts/Emuera/GameView/`：Emuera 控制台显示模型，负责文本、按钮、HTML、图片、形状、输入等待。
- `Scripts/Emuera/GameProc/`：ERB 加载、逻辑行解析、label 索引、脚本执行状态机、lazy loading。
- `Scripts/Emuera/GameData/`：变量、表达式、常量、函数方法、角色数据。
- `Scripts/Emuera/Content/`：图片、精灵、Graphics surface、ColorMatrix 绘制。
- `Scripts/uEmuera/`：`System.Drawing` / `System.Windows.Forms` 兼容层。
- `Scripts/Diagnostics/`：运行期诊断、日志路由、导出、输入回放、诊断面板。

不要靠猜测定位文件。先查 `CODE_MAP.md`。

## 开始任务前

执行任何会修改文件的任务前：

1. 读 `IDEAS.md` 和 `CODE_MAP.md`。
2. 查看当前工作区状态，识别已有用户改动。
3. 不要回滚、覆盖或整理与当前任务无关的改动。
4. 统计 `action_maps/` 中已有日志文件数量。
5. 如果本次会修改文件，在本地 `action_maps/` 新增一份中文操作日志。
6. 新增日志后总数不得超过 30 个；如果会超过，先询问仓库主人如何归档或删除旧日志。

`action_maps/` 是本地日志目录：

- 不提交 GitHub。
- 不放进 PR。
- 不使用 `git add -f action_maps/` 强行加入版本控制。
- `.gitignore` 必须保持忽略 `action_maps/`。

## 修改代码原则

总体原则：保持现有效果和兼容行为，不为重构而重构。

要求：

- 优先保持原 Emuera 行为、Snake 兼容逻辑、移动端布局、输入流程、图片/精灵渲染。
- 修改前先判断责任边界：UI、GameView、GameProc、GameData、Content、uEmuera、Diagnostics 分别处理不同问题。
- 手机端性能优先，避免在热路径增加无界分配、全量扫描、同步 I/O、逐帧重建节点或纹理。
- 优先沿用当前项目已有模式；只有明确降低复杂度或重复时才新增抽象。
- 复杂逻辑需要中文注释，说明原因、边界和兼容性要求。
- 跨线程、UI 队列、输入等待、日志导出、资源加载、ColorMatrix、存档兼容相关代码要额外谨慎。
- 每次修改 `Scripts/**/*.cs` 后，必须判断是否需要更新 `CODE_MAP.md`。

## 日志问题处理

用户提供日志时，先诊断再改代码。

常见日志：

- `emuera.log`
- `emuera_xxx.log`
- `gemuera_xxx.log`
- `emuera_startup_debug`
- `chara_debug` 或类似角色调试日志

规则：

- 如果同时存在 `emuera_xxx.log` 和 `gemuera_xxx.log`，先确认两个日志的日期/时间对应同一次测试。
- 如果两份日志日期不一致，不要基于它们修复代码；先向用户确认并要求同一次运行的日志。
- 不要只根据单行报错下结论，要结合调用链、平台、游戏目录、最近变更和复现路径。
- 修复后说明根因、修改位置、验证方式，以及是否需要重新导出 APK 测试。

诊断相关入口：

- 配置：`config.toml`
- 诊断模块：`Scripts/Diagnostics/`
- 日志桥接：`Scripts/GenericUtils.cs`
- 导出：`DiagnosticLogExporter`、`GenericUtils.ExportDiagnosticPackage`

新增诊断前，先检查 `RuntimeDiagnosticsConfig` 和 `DiagnosticLogRouter` 是否已有分类或开关。

## GitHub 协作规则

仓库：

- GitHub：`https://github.com/wwwXiaoHan17/gEmuera`
- 默认协作分支：`dev`

规则：

- 除仓库主人明确要求外，不要直接向 `dev` 或主分支提交代码。
- 每个任务从最新 `dev` 新建独立工作分支，建议 `ai/<任务简述>` 或 `fix/<问题简述>`。
- 普通协作者或 AI 完成任务后，应提交到任务分支，并发起 Pull Request 指向 `dev`。
- PR 合并前必须由仓库主人或指定维护者审核。
- PR 标题和说明必须使用中文。
- 每个 PR 尽量只解决一个明确问题，避免混入无关重构、格式化和资源变更。
- 禁止擅自强制推送、硬重置、删除远端分支、回滚他人提交。

如果发现同一文件已有用户或其他 AI 的未合并改动，要以最新文件为准；不能用旧上下文覆盖。

## Action Maps 日志要求

每次修改文件的任务，都要在本地 `action_maps/` 写中文日志。

日志必须包含：

- 任务目标：用户要求和本次执行范围。
- 初始状态：当前分支、工作区状态、相关已有改动、日志文件计数。
- 操作步骤：读取了哪些文件、执行了哪些命令、每一步得到什么结论。
- 修改记录：每个被修改文件的具体改动、原因、影响范围。
- 验证记录：执行过的命令、结果、失败原因、未验证内容。
- `CODE_MAP.md` 判断：是否需要更新代码地图，以及原因。
- 风险评估：可能影响的模块、平台、兼容行为。
- 回退建议：如果本次改动出错，优先回退哪些文件或 hunk。

日志不是 Git 的替代品，也不是仓库历史的一部分。真正回退仍应依赖 Git diff、commit、PR 或反向 patch。

## 验证要求

根据改动风险选择验证范围：

- 纯文档修改：检查 Markdown 结构、关键规则可搜索、必要链接存在。
- C# 代码修改：优先运行相关构建或最小可行检查。
- Godot UI/渲染/输入修改：至少说明桌面验证结果；移动端问题必须说明 APK 是否验证。
- Android 相关修复：如果不能导出/安装/运行 APK，最终回复必须明确未验证原因。
- 日志/诊断修改：验证配置读取、日志开关、导出路径或至少说明未验证点。

不要把“代码能编译”当作唯一完成标准。必须结合 Android/APK 和 Emuera 兼容目标判断。

## 最终回复要求

每次任务结束时，用中文说明：

- 改了什么。
- 为什么这样改。
- 修改了哪些文件。
- 执行了哪些验证。
- 哪些内容没有验证以及原因。
- 是否更新了 `CODE_MAP.md`。
- 是否写入了本地 `action_maps/` 日志。
- 是否存在风险或需要用户重新导出 APK 测试。

## 常用定位入口

- 项目约定：`IDEAS.md`
- 代码地图：`CODE_MAP.md`
- Claude/AI 工具规则：`CLAUDE.md`
- 本地操作日志：`action_maps/`，不提交 GitHub
- Godot 主场景：`first_window.tscn`、`main.tscn`
- 主入口：`Scripts/FirstWindow.cs`、`Scripts/EmueraMain.cs`
- 后台线程：`Scripts/EmueraThread.cs`
- UI 渲染：`Scripts/EmueraContent.cs`
- 脚本执行：`Scripts/Emuera/GameProc/Process*.cs`
- 控制台输出：`Scripts/Emuera/GameView/EmueraConsole*.cs`
- 表达式/变量：`Scripts/Emuera/GameData/Expression/`、`Scripts/Emuera/GameData/Variable/`
- 图片/精灵：`Scripts/Emuera/Content/`、`Scripts/SpriteManager.cs`
- 诊断日志：`Scripts/Diagnostics/`、`Scripts/GenericUtils.cs`
