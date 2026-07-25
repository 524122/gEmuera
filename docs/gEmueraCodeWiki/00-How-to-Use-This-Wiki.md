# 如何使用 gEmuera Code Wiki

本 Wiki 是代码地图，不是替代源码、测试或阶段证据的设计说明。它的作用是将“我应该从哪里看、谁拥有行为、改动会穿过哪些边界”回答在进入大文件之前。

## 证据等级与措辞

| 标签 | 含义 | 可以据此做什么 | 不能据此做什么 |
| --- | --- | --- | --- |
| **默认生产路径** | 当前玩家启动游戏实际经过的 legacy 路径。 | 定位行为 owner、修复兼容性问题、设计回归。 | 假设已完成迁移或可删除旧代码。 |
| **Host / bridge** | Godot 主线程的生命周期、UI、平台或兼容桥。 | 投影 immutable 结果、调度资源、管理场景。 | 持有完整 ERB 语义或在 presentation 中直接修改 Core state。 |
| **Core 合同 / prototype** | `src/Core` 的无 Godot 类型、候选实现或未来接口。 | 设计独立契约、编写 contract smoke、做受控 canary。 | 仅添加 DTO/descriptor 就宣称生产功能已实现。 |
| **工具 / 证据** | `tools/`、fixture、trace、report 的生成与门禁。 | 复现、比较、记录覆盖程度。 | 用单次 build、smoke 或截图宣布兼容性已通过。 |
| **目标设计** | `docs/NewFrameworkDesign/` 的迁移计划。 | 确认约束、下一步、验收规则。 | 用目标架构覆盖现有代码 owner。 |

文中“当前”“默认”“legacy”“candidate”等词均按上表理解。遇到不确定的结论，应回到当前源码、机器报告和 [`../NewFrameworkDesign/DeveloperHandoff.md`](../NewFrameworkDesign/DeveloperHandoff.md) 验证。

## 建议的检索流程

1. **分类任务。** 是启动/场景、ERB 执行、数据/表达式、显示/资源、Core/Host，还是工具/诊断？
2. **读对应 Wiki 页。** 先获取目录、关键类型和数据流；不要从全仓库文本搜索开始。
3. **用 CodeGraph 跟进精确符号。** 查询类名、方法名或跨模块流程，例如 `Process.DoScript runScriptProc InputRequest`。
4. **确认 owner 与线程。** 修改前回答：谁拥有状态？谁能写？谁观察变化？该调用在哪个线程？
5. **选择验证层。** pure Core 用 Core smoke / contract；Godot Node 用 GDUnit4；legacy 语义用 fixture、runner 或游戏回放；Android 能力需要 APK/设备证据。
6. **同步 Wiki。** 若目录、入口、public/关键 internal 类型、流程、边界、命令或 owner 发生变化，按 [`99-Maintenance-Guide.md`](99-Maintenance-Guide.md) 更新。

## 快速术语

| 术语 | 本仓库中的意思 |
| --- | --- |
| **legacy** | `Scripts/Emuera/` 为主的原 Emuera 兼容 parser/VM/view/resource 实现，是当前默认行为 owner。 |
| **Godot host** | `Scripts/` 与 `Scripts/GodotHost/` 中直接拥有 Godot `Node`、场景、GPU、UI 或平台能力的层。 |
| **Core** | `src/Core/GEmuera.Core.csproj`，不引用 `GodotSharp` 的 .NET 合同程序集。 |
| **session / generation** | 游戏切换时的会话边界及其代次；旧代异步完成不得写进新会话。 |
| **canary / sidecar** | 与默认 legacy 路径共存、用于观察或验证候选迁移能力的受控路径。 |
| **CompatibilityPlan** | Core 中冻结的 profile/module/descriptor 路由计划；不能自动替代 legacy runtime 的兼容判断。 |
| **port** | Core 与平台能力之间的 typed contract（输入、存储、数据库、音频、生命周期等）。 |
| **display DTO** | Core 的显示交易、line/part/barrier 等数据合同；当前默认 renderer 未获准切换到它。 |
| **fixture / evidence** | 可复现输入、来源与输出的验证资产；没有身份、来源和覆盖范围的报告不是完整通过证明。 |

## 当前工作树的重要边界

- `android/build/` 是导出/构建产物区域，不能作为手写业务源码入口。
- `addons/gdUnit4/` 是测试插件代码；应用功能通常不应改入其中。
- `node_modules/`、`.godot/`、`bin/`、`obj/` 是工具/生成目录，不纳入 `10-Source-Index.md`。
- 游戏目录、APK、设备日志、本机绝对路径和大报告不能当作仓库内可提交的事实来源。
- `action_maps/` 是本地工作日志，按项目规则不提交；该目录不是文档或代码的权威来源。

## 常见误判与纠正

| 误判 | 正确做法 |
| --- | --- |
| “`src/Core` 有同名能力，所以我改 Core 就能影响当前游戏。” | 先从 `EmueraMain` → `EmueraThread` → `Program` → `Process` 确认默认路径；生产 ERB 行为仍需改 legacy owner。 |
| “`main.tscn` 中有 PrototypeRuntime，所以它是主解释器。” | Prototype 是 sidecar；它提供 session/bridge 观察，不能替代 legacy game loop。 |
| “Godot Autoload 可以保存游戏变量。” | `AppBootstrap` / `PlatformGateway` 只持 application/platform 状态；游戏状态必须由 session/legacy owner 管理。 |
| “Build 成功说明 Android/保存/兼容性通过。” | 选择对应 fixture、trace、APK、设备和保存 round-trip 证据；保留 `Blocked`/`Uncovered`。 |
| “使用 UI signal 就能跨边界改数据。” | UI 只发 intent；编排器或 owner 处理状态变更并返回投影。 |
| “找不到旧 `CODE_MAP.md`，就可以臆测结构。” | 使用本 Wiki、`10-Source-Index.md` 和 CodeGraph；在变更记录中诚实说明旧图不可用。 |

## 页面之间的关系

```text
01 Repository overview
  ├─ 02 Startup & lifecycle
  ├─ 03 Legacy interpreter ─┬─ 04 Data & expressions
  │                         └─ 05 Console rendering & resources
  ├─ 06 GodotHost & Core
  ├─ 07 Dependencies & threading
  └─ 08 Operations / testing / diagnostics
       └─ 99 Maintenance

10 Source index：为所有页面提供文件级跳转入口
```