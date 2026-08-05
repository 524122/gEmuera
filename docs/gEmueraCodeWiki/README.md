# gEmuera Code Wiki

本目录是 **当前工作树的代码定位与维护文档**。目标是让 Agent 和开发者先理解 owner、数据流与边界，再进入源码或使用 CodeGraph，避免把目标架构、实验合同和默认生产路径混为一谈。

> **阅读原则：代码与机器验证优先。** 本 Wiki 描述的是当前实现结构；迁移阶段、门禁结论与未覆盖项以 [`../NewFrameworkDesign/DeveloperHandoff.md`](../NewFrameworkDesign/DeveloperHandoff.md)、对应设计文档和机器报告为准。

## 先读哪一页

| 你的目的 | 先读 |
| --- | --- |
| 只想快速定位一个入口或目录 | [`01-Repository-Overview.md`](01-Repository-Overview.md) → [`10-Source-Index.md`](10-Source-Index.md) |
| 理解启动、场景、生命周期或线程 | [`02-Startup-and-Lifecycle.md`](02-Startup-and-Lifecycle.md) → [`07-Dependencies-and-Threading.md`](07-Dependencies-and-Threading.md) |
| 改 ERB 加载、解析、执行或输入等待 | [`03-Legacy-Interpreter.md`](03-Legacy-Interpreter.md) → [`../../ERBAPI.md`](../../ERBAPI.md) |
| 改表达式、函数、变量、CSV 或常量 | [`04-Legacy-Data-and-Expressions.md`](04-Legacy-Data-and-Expressions.md) |
| 改显示、HTML、精灵、图片或触摸输入 | [`05-Console-Rendering-and-Resources.md`](05-Console-Rendering-and-Resources.md) |
| 改 Godot Host、Core session、ports 或迁移 sidecar | [`06-GodotHost-and-Core.md`](06-GodotHost-and-Core.md) → `docs/NewFrameworkDesign/` |
| 构建、回归、诊断、导出或确认门禁 | [`08-Operations-Testing-and-Diagnostics.md`](08-Operations-Testing-and-Diagnostics.md) |
| 更新本 Wiki | [`99-Maintenance-Guide.md`](99-Maintenance-Guide.md) |

## 最重要的当前结论

1. **默认可运行游戏路径仍由 legacy owner 承担。** 入口为 `FirstWindow` → `EmueraMain` → `EmueraThread` → `Program` → legacy `GameProc` / `GameView`。`Scripts/Emuera/` 的 parser、VM、变量、显示和资源路径不能因为 `src/Core/` 已存在而被绕开或删除。
2. **`src/Core/` 是无 Godot 依赖的合同/原型程序集。** 它提供 session、compatibility、display DTO、资源、保存、ports、运行时与治理模型；其中多数仍是迁移候选或合同库存，不是默认游戏解释器或渲染器。
3. **`main.tscn` 同时放置 legacy 主入口和 Core prototype sidecar。** `PrototypeRuntimeNode` 与其 input/audio/resource/status bridge 用于观察和验证，默认不替代 legacy 游戏循环。
4. **跨线程与 Godot 主线程边界不可随意打破。** Godot `Node`、`Resource`、纹理和 GPU 操作由主线程 host/presentation 拥有；legacy 脚本在 `EmueraThread` 工作线程执行；Core 不引用 `GodotSharp`。
5. **阶段状态不能靠文件存在或 build 推断。** 当前 M0、M1、M2 及 M3–M7 的放行状态有明确阻断项；详见 `DeveloperHandoff.md`。

## 文档地图

| 文件 | 内容 |
| --- | --- |
| [`00-How-to-Use-This-Wiki.md`](00-How-to-Use-This-Wiki.md) | Wiki 的证据等级、术语、推荐检索流程与避免误判的规则。 |
| [`01-Repository-Overview.md`](01-Repository-Overview.md) | 仓库结构、构建单元、运行时 owner、目录责任与快速定位表。 |
| [`02-Startup-and-Lifecycle.md`](02-Startup-and-Lifecycle.md) | `project.godot`、场景、Autoload、启动、停止、重启和 profile 路由。 |
| [`03-Legacy-Interpreter.md`](03-Legacy-Interpreter.md) | legacy ERB 加载、解析、label、`Process` 执行、等待/恢复与扩展入口。 |
| [`04-Legacy-Data-and-Expressions.md`](04-Legacy-Data-and-Expressions.md) | 变量、表达式、函数、常量、CSV/角色数据与配置系统。 |
| [`05-Console-Rendering-and-Resources.md`](05-Console-Rendering-and-Resources.md) | console 显示模型、Godot 投影、HTML/图片/精灵、ColorMatrix 与输入。 |
| [`06-GodotHost-and-Core.md`](06-GodotHost-and-Core.md) | Godot Host bridge、Core 模块、session/canary/ports 与迁移边界。 |
| [`07-Dependencies-and-Threading.md`](07-Dependencies-and-Threading.md) | 项目依赖、调用图、线程/所有权矩阵、禁止依赖与关键不变量。 |
| [`08-Operations-Testing-and-Diagnostics.md`](08-Operations-Testing-and-Diagnostics.md) | config、诊断、构建、GDUnit4、Core smoke、工具和已知门禁。 |
| [`09-Change-Impact-Map.md`](09-Change-Impact-Map.md) | 按改动入口定位 owner、blast radius、Wiki 同步页与最小回归选择。 |
| [`10-Source-Index.md`](10-Source-Index.md) | 241 个仓库自有 C# 源文件与 `scenes/` 场景资产的生成式索引（路径 + 类型声明 / 挂载脚本）。 |
| [`99-Maintenance-Guide.md`](99-Maintenance-Guide.md) | Wiki 更新触发器、生成命令、审校清单和文档 owner 规则。 |

## 典型检索路径

```text
“按钮点了为何没有继续执行？”
  EmueraContent / Inputpad
    -> EmueraThread.Input
    -> EmueraConsole.PressEnterKey / callEmueraProgram
    -> Process.DoScript / runScriptProc

“新增一个生产 ERB 函数该改哪里？”
  ERBAPI.md
    -> Scripts/Emuera/GameData/Function/
    -> legacy 注册表、参数解析、执行实现、定向 fixture
    -> 不把 src/Core descriptor 当作生产 handler

“某个显示行如何变成 Godot 控件或 Canvas？”
  EmueraConsole.Print / PrintStringBuffer
    -> ConsoleDisplayLine + display parts
    -> GenericUtils UI queue
    -> EmueraContent / ConsoleRenderSurface / EmueraImage
```

## 不在本 Wiki 中替代的文档

- `docs/NewFrameworkDesign/`：迁移目标、阶段合同、证据门禁和工作包，不可用其长期设计直接推断当前默认行为。
- `docs/OriginalFrameworkDesign/`：历史参考，已标记为过时。
- `docs/xEmueraCodeWiki/`：外部参考项目的架构说明，不是本仓库源码真相。
- [`../../ERBAPI.md`](../../ERBAPI.md)：ERB 扩展、注册、兼容性与 TDD 的强制实施规则。
- `addons/gdUnit4/ADDON.md`：Godot/GDUnit4 测试框架的具体使用说明。