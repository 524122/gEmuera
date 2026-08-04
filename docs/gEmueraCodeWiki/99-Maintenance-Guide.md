# Code Wiki 维护指南

本 Wiki 的价值在于它随代码变化保持可信，而不是一次性“写完后不再更新”。修改源码、场景、构建边界、运行方式或 owner 时，必须判断是否需要同步更新本目录。

## 文档 owner 与边界

| 文档范围 | 本 Wiki 是否负责 | 权威补充来源 |
| --- | --- | --- |
| 当前仓库目录、关键类型、启动/调用/线程/依赖地图 | **负责** | 当前源码、CodeGraph、csproj/scene/config。 |
| 当前默认 legacy owner 和 Core/Host 的区分 | **负责** | `DeveloperHandoff.md`、`ERBAPI.md`。 |
| 迁移目标、阶段 gate、work package、验收证据 | 摘要和链接；**不取代权威** | `docs/NewFrameworkDesign/`、机器报告。 |
| ERB 扩展规则、TDD、DIA contract | 定位/提示；**不取代权威** | `ERBAPI.md`。 |
| 外部 XEmuera 参考架构 | 只说明引用关系 | `docs/xEmueraCodeWiki/`。 |
| 历史设计 | 只标注过时性 | `docs/OriginalFrameworkDesign/`。 |

## 何时必须更新

| 改动类型 | 必须检查 / 更新的 Wiki 页面 |
| --- | --- |
| 新/改主场景、Autoload、launcher、profile 选择、启动/停止/重启 | `01`、`02`、`07`、`10`。 |
| 改 `EmueraThread`、UI queue、跨线程任务、资源销毁、generation | `02`、`05`、`06`、`07`、`10`。 |
| 改 ERB loader、parser、label、lazy loading、`Process`、wait/resume | `03`、`04`（如数据接口变化）、`10`。同时按 `ERBAPI.md` 检查。 |
| 改 expression function、instruction、argument、variable、CSV/constant | `03`、`04`、`10`。同时同步 `ERBAPI.md` 所需证据/contract。 |
| 改 console line、HTML、image、sprite、ColorMatrix、input UI | `05`、`07`、`10`。 |
| 改 `src/Core`、session、compatibility、DTO、resource/save/ports/runtime | `01`、`06`、`07`、`10`，并更新对应 `NewFrameworkDesign` owner 文档/状态。 |
| 改 build target、csproj、native lib、export preset、test command | `01`、`08`、`10`。 |
| 改 diagnostics/config/log/replay tool | `08`、`10`。 |
| 新增/删除/移动 C# 文件，或改 primary type 名 | `10`（运行生成器）及受影响的模块页。 |
| 新增/删除/移动 `scenes/` 场景资产，或改其中挂载的脚本路径 | `10`（运行生成器）、`02`、`05`。 |
| 改阶段状态、gate、已知限制或 release evidence | 本 Wiki 的摘要仅按权威文档同步；先更新 `docs/NewFrameworkDesign/` / report，不可反向猜测。 |

`01` 等数字代表本文目录中的文件，例如 [`01-Repository-Overview.md`](01-Repository-Overview.md)。

## 固定更新流程

1. **先判断改动层。** 当前默认 legacy、Godot host/presentation、Core contract、tool/evidence，还是多个层。
2. **用 CodeGraph 获取真实调用链。** 以精确符号或问题描述查询；不要仅依靠文件名或旧 Wiki 推断实现。
3. **更新最小准确页面。** 不需要重写所有文档，但任何改变 owner/入口/contract 的事实必须更新。
4. **重新生成 source index。** 只要 C# 文件/类型拓扑变了就执行命令。
5. **审校链接、路径、措辞与状态。** 除了文件存在，也检查“默认/候选/contract”没有被写反。
6. **运行匹配的 build/test/document checks。** 记录真实 exit code 和未覆盖项；不以 Wiki 更新代替验证。
7. **在 PR/交付中报告。** 列出更新了哪些 Wiki 页、为何更新、哪些状态仍未覆盖。

## 生成 source index

`10-Source-Index.md` 由本目录的脚本生成，防止手工目录表过时：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass `
  -File docs/gEmueraCodeWiki/Update-SourceMap.ps1
```

该脚本：

- 扫描 `Scripts/`、`src/Core/`、`test/`、`tools/core-contracts/` 的 C# 文件；
- 同时输出 `scenes/` 下的 `.tscn` 场景资产（根节点类型 + ext_resource 挂载脚本）；
- 排除 `bin/`、`obj/`、`.godot/`、`android/build/`、`addons/gdUnit4/` 与 `node_modules/`；
- 按目录输出路径和轻量正则提取的声明名；
- 不运行游戏、不会改业务代码，只覆盖 `10-Source-Index.md`。

> 声明索引不是调用图，也不说明 public API。对 partial type、nested type、conditional compile 或实际调用关系，仍需要查源码和 CodeGraph。

### Source index 生成后的最小审校

```powershell
# 确认生成器可以重复运行
powershell -NoProfile -ExecutionPolicy Bypass `
  -File docs/gEmueraCodeWiki/Update-SourceMap.ps1

# 检查是否把不该纳入的产物目录写入索引
Select-String -Path docs/gEmueraCodeWiki/10-Source-Index.md `
  -Pattern 'android/build|addons/gdUnit4|node_modules|/bin/|/obj/'
```

第二条没有输出才符合当前排除规则。

## 写作规则

### 事实表达

- 明确标记 **默认生产路径**、**Host/bridge**、**Core contract/prototype**、**工具/evidence**。
- 若某能力只有类型/设计/合同，写“合同存在”“候选”“sidecar”；不要写“已替换”“已上线”“兼容已通过”。
- 对阶段状态使用权威文档的原始状态（如 `Blocked`、`EvidenceMissing`、`Partial`）；不要因 build 成功升格。
- 只记录能由当前源码、scene/config、命令输出或权威文档支持的事实。未知时写明待确认，而非补全猜测。

### 定位质量

- 提供相对路径与关键类型/方法，而不是模糊地写“在核心里”。
- 面对 partial class，列出所有相关文件（例如 `Process*.cs`、`EmueraContent*.cs`）。
- 目录改名/移动后更新所有相对 Markdown 链接，避免仅修 index。
- 大型列表交给 `10-Source-Index.md`；模块页只列关键 owner 和可读的调用关系。

### 语言与格式

- 主文使用中文；C# 类型、方法、目录、命令保持原始拼写并使用反引号。
- 使用短表格、ASCII/Markdown 调用图和明确的“不能推断”说明。
- 避免复制大段源码；Wiki 负责导航与责任说明，源码仍是细节真相。
- 避免记录本机绝对路径、用户游戏目录、私人日志、APK 或受限 fixture 内容。

## 文档与验证的协作

| 改动区域 | 最小文档动作 | 最小验证动作（仍按任务扩展） |
| --- | --- | --- |
| Core pure logic | 更新 `06` / `07` / `10` | Core build + CoreContractSmoke。 |
| legacy ERB | 更新 `03` / `04` / `10` | build + 相关 dialect/fixture/trace。 |
| Godot scene/bridge | 更新 `02` / `05` / `06` / `10` | Godot build + 单一 GDUnit4 suite。 |
| display/resource | 更新 `05` / `07` / `10` | display trace/pixel diff + needed device evidence。 |
| save/platform | 更新 `06` / `08` / `10` | candidate/rollback/fixture + Android/device when applicable。 |
| tooling/config | 更新 `08` / `10` | tool command + schema/report checks。 |

## 与项目规则的衔接

- 如果 `CODE_MAP.md` 在当前工作区可用，按其规则一起更新；如果不可用，不能假装已更新它。当前 Wiki 不应声称取代一个不存在的旧地图。
- 修改 `Scripts/**/*.cs` 后，至少根据本表判断是否需要同步 Wiki；重要架构变化通常还影响 `docs/NewFrameworkDesign/`。
- `action_maps/` 为本地日志，不提交。需要 action log 的任务按项目规则记录命令、RED/GREEN、风险、回退和未覆盖项。
- 不删除 legacy fallback、fixture、report 或设计记录来让 Wiki 看起来更“干净”。

## 提交前 checklist

- [ ] 本次变更的 owner、入口、线程和 fallback 在 Wiki 中仍然正确。
- [ ] 新增/移动/删除 C# 文件后已运行 `Update-SourceMap.ps1`。
- [ ] `README.md` 目录表仍链接到所有实际页面。
- [ ] 内部链接、`../../` 根路径和代码路径存在。
- [ ] 默认 legacy 路径与 Core/sidecar 的状态未被混淆。
- [ ] 对 `Blocked` / `Uncovered` / `Partial` 的叙述没有被 build/smoke 误升格。
- [ ] 没有将游戏目录、存档、APK、设备日志、本机绝对路径写入 Wiki。
- [ ] 相关 `NewFrameworkDesign`、`ERBAPI.md`、test/tool 文档已按 owner 检查。
- [ ] 实际运行的验证命令、exit code、未覆盖项和回退已在任务交付中记录。

## 维护回路

```text
code/scene/config/tool change
  -> classify owner and affected contracts
  -> CodeGraph exact flow
  -> update module page(s)
  -> regenerate 10-Source-Index.md
  -> validate links and appropriate tests
  -> report evidence + remaining limitations
```

通过这个回路，Wiki 能持续降低无目的检索，而不会演变成脱离代码的第二套“猜测性架构”。