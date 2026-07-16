# AGENT.md

本文是 AI Agent（Claude Code、AI CLI、AI IDE）进入项目的首读文档。读完本文后，按需查阅「必读与配套文档」中的详细文档。

## 项目简介

本项目基于 **Godot 4.7 mono** + C# 开发，旨在使用开源技术 + Vibe Coding 开发新的 Emuera 模拟器。

Emuera 核心编译器以 C# 编写，为减少开发成本、方便 AI 对接核心、降低 Token 消耗，开发初期即放弃 Godot 推崇的 GDScript，**以 C# 为主语言**。

- 首要运行与测试目标：Android/手机端，方案以导出 APK 可用为准。
- 桌面端可用于调试，但不能替代 APK 验证结论。
- 默认测试游戏目录：`E:\Godot_v4.6.2-stable_mono_win64\snake\EraTW-Magic_DLC-update_base`。
- 参考实现：`E:\MyCode\Era\emuera_lazyloading_selfmodified_version-main-skiasharp`、`E:\MyCode\GodotCode\gemuera\uEmuera-0.2.9d`、`E:\MyCode\GodotCode\gemuera\XEmuera-0.5.1`。
- 涉及 Godot 架构、UI、性能、平台适配时，使用 `godot-master` skill 辅助判断。

## 必读与配套文档

| 文档 | 状态 | 说明 |
|------|------|------|
| `CODE_MAP.md` | **必读** | 代码地图，减少无目的文件检索；改 `Scripts/**/*.cs` 后判断是否需同步更新 |
| `NewFrameworkDesign/DeveloperHandoff.md` | **接手必读** | 当前已有能力、实际 owner、阶段阻断、验证入口和下一开发顺序 |
| `NewFrameworkDesign/` | 当前设计 | 新架构设计文档（Godot-master skill + 学习 XEmuera 架构重新设计） |
| `OriginalFrameworkDesign/` | 已过时 | 早期架构设计，部分内容有误，仅供参考 |
| `addons/gdUnit4/ADDON.md` | 按需 | GDUnit4 插件使用指南（WHY/WHEN/WHERE/HOW），用 GDUnit 做 TDD 时阅读 |
| `action_maps/` | 本地日志 | Agent 开发动作记录，方便回退与查看进度；不提交 GitHub |
| `_CLAUDE.md` | 归档参考 | 原 Claude/AI 工具执行指南（已由本文替代），含详细规则可备查 |
| `E:\XEmuera-master\CodeWiki` | 外部参考 | xiao_han17 的 XEmuera 架构指导文档；若未找到，提醒用户获取 XEmuera 源码并用 Agent 创建 CodeWiki |

## 使用的工具与项目

1. **GD Agentic** — 专业 Godot 指导 skill，整合架构、设计、UI/UX 等知识。实际开发中只用到其中的 `godot-master`。<https://github.com/thedivergentai/gd-agentic-skills>
2. **CodeGraph（魔改版）** — 基于红黑树为大型项目建立代码模型，Agent 通过 MCP 工具快速检索代码模型，替代内置 grep 搜索，降低 Token 消耗、加快检索。魔改版支持简单跨项目检索。<https://github.com/colbymchenry/codegraph>
3. **CodeGraph-mcp** — CodeGraph 的配套 MCP 工具，让 AI CLI / AI IDE 能使用 CodeGraph 功能。
4. **GDUnit4** — Godot 社区单元测试框架，测试 GD 脚本、C# 脚本和场景。本项目用于对 Godot 场景及需 Godot 组件的功能进行 TDD 开发。详见 `addons/gdUnit4/ADDON.md`。
5. **xUnit** — 本项目 Emuera 核心（ERB 语法解释器，C# 编写）的原生 C# 单元测试框架，用于语法解释器更新时快速高效地做 TDD。

## 开发方式

本项目采取 **TDD（测试驱动开发）**，GDUnit4 与 xUnit 协同配合：

- GDUnit4：测试 Godot 场景与需 Godot 组件的功能。
- xUnit：测试 Emuera 核心（ERB 语法解释器）的纯 C# 逻辑。

任务结束后，删除冗余的临时测试文件，避免造成垃圾文件。

## 架构速览

核心运行链：

```text
project.godot -> first_window.tscn -> FirstWindow._Ready()
  -> main.tscn -> EmueraMain._Ready() -> EmueraThread.Start()
  -> Program.Main() -> Process.Initialize()
  -> Process.DoScript() / runScriptProc()
  -> EmueraConsole / GenericUtils / EmueraContent
```

核心层：

- `Scripts/` — Godot UI、主入口、线程桥、精灵缓存、输入面板、缩放、诊断入口。
- `Scripts/Emuera/GameView/` — 控制台显示模型（文本、按钮、HTML、图片、形状、输入等待）。
- `Scripts/Emuera/GameProc/` — ERB 加载、逻辑行解析、label 索引、脚本执行状态机、lazy loading。
- `Scripts/Emuera/GameData/` — 变量、表达式、常量、函数方法、角色数据。
- `Scripts/Emuera/Content/` — 图片、精灵、Graphics surface、ColorMatrix 绘制。
- `Scripts/uEmuera/` — `System.Drawing` / `System.Windows.Forms` 兼容层。
- `Scripts/Diagnostics/` — 运行期诊断、日志路由、导出、输入回放、诊断面板。

不要靠猜测定位文件，先查 `CODE_MAP.md`。

## 协作规则

### GitHub

- 仓库：`https://github.com/wwwXiaoHan17/gEmuera`，默认协作分支 `dev`。
- 每个任务从最新 `dev` 新建分支：`ai/<任务简述>` 或 `fix/<问题简述>`。
- 不直接向 `dev` 或主分支提交代码；完成任务后提 PR 指向 `dev`，PR 标题与说明用中文。
- 每个 PR 只解决一个明确问题，禁止混入无关重构、格式化和资源变更。
- 禁止擅自强制推送、硬重置、删除远端分支、回滚他人提交。

### action_maps 日志

- 每次修改文件的任务，在 `action_maps/` 写中文操作日志。
- `action_maps/` 不提交 GitHub、不放进 PR、不 `git add -f`，`.gitignore` 保持忽略。
- 日志总数不超过 30 个；超出则先询问仓库主人归档或删除。
- 日志必含：任务目标、初始状态、操作步骤、修改记录、验证记录、`CODE_MAP.md` 判断、风险评估、回退建议。

## 常用文件入口

| 职责 | 文件 |
|------|------|
| 主场景 | `first_window.tscn`、`main.tscn` |
| 主入口 | `Scripts/FirstWindow.cs`、`Scripts/EmueraMain.cs` |
| 后台线程 | `Scripts/EmueraThread.cs` |
| UI 渲染 | `Scripts/EmueraContent.cs` |
| 脚本执行 | `Scripts/Emuera/GameProc/Process*.cs` |
| 控制台输出 | `Scripts/Emuera/GameView/EmueraConsole*.cs` |
| 表达式/变量 | `Scripts/Emuera/GameData/Expression/`、`Scripts/Emuera/GameData/Variable/` |
| 图片/精灵 | `Scripts/Emuera/Content/`、`Scripts/SpriteManager.cs` |
| 诊断日志 | `Scripts/Diagnostics/`、`Scripts/GenericUtils.cs` |
| 运行期配置 | `config.toml`、`Scripts/Diagnostics/RuntimeDiagnosticsConfig*.cs` |
