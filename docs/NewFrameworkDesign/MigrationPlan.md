# 从可运行 gEmuera 到新架构的渐进迁移

## 原则

目标是吸收 XEmuera 的清晰领域边界和 Godot 的场景/渲染/平台能力，同时不丢失旧 gEmuera 已验证的游戏兼容性。迁移采用 strangler pattern：每一步都能运行代表游戏、能通过 feature flag 回退、能在同一输入上比较旧/新输出；不同时重写解释器、变量、HTML、渲染、线程和平台层。

## 长期保留的六根主梁

1. 纯 .NET Core，不依赖 GodotSharp。
2. GameSession 统一拥有游戏级状态。
3. generation 隔离迟到异步结果。
4. candidate load + 短 commit 临界区 + atomic swap。
5. 上游/旧项目/实际游戏三层差分验证。
6. 每会话冻结的 CompatibilityPlan；标准/v24/Snake/未来魔改以模块组合，不再以全局布尔分支叠补丁。

## 迁移阶段

当前实施批准范围和工单级验收以 [M0M2ImplementationBaseline](M0M2ImplementationBaseline.md) 为准。阶段必须顺序放行：M0 未关闭时，M1 只能作为可回退的实验性外壳，不能进入发布；M1 未关闭不得实现/放行 M2；M3–M7 当前仅用于规划和接口风险评审。

本段和阶段表中的 M1 专指 LegacySessionFacade 会话外壳及其对旧运行时的 generation guard 接线。[M1CoreRuntimeContractSlice](M1CoreRuntimeContractSlice.md) 现已包含最小 legacy/Godot 桥接：候选后端成功启动后才提交 Core `Current`，失败/stale 时恢复旧后端。它仍不绕过 M0，当前状态为 `InProgress / Blocked`，不授权 M2，也不证明 legacy static 已隔离或 Parser/VM 已消费计划。

| 阶段 | 改动 | 运行路径 | 关闭门禁 | 回退 |
| --- | --- | --- | --- | --- |
| M0 固定旧基线 | 为旧 gEmuera 加 runner/trace，不改行为 | 全旧 | Snake/eraFL/Android、存档、HTML、输入、G、性能报告 | 原二进制 |
| M1 会话外壳 | `LegacySessionFacade` 包住 GlobalStatic/Program/EmueraThread | 旧 Core + 旧 View | A/B 切换无静态泄漏；generation guard | `migration.session_isolation=false` 默认保留 M0 bridge；首等待 cross-game/profile A/B/A observation 已有，但完整 rollout 报告仍缺 |
| M2 DTO 边界 | 旧 EmueraConsole 输出完整 Display DTO；保留现有 Canvas/Control | 旧 VM + DTO adapter + 旧 renderer | div/srcb/地图/data-only golden | `display.dto=false` |
| M3 纯 Core 抽取 | 按 namespace 移动解析、变量、存档；行为不重写 | 新 Core assembly + legacy façades | 无 Godot build、程序集边界、同线程差分 | façade 指向旧 assembly |
| M4 资源/图形 | 引入 PixelStore/ResourceCatalog，纹理为派生投影 | 新 CPU truth + 旧/新 Bridge 可切 | G 像素/返回值、CBG/Sprite、内存报告 | `graphics.pixel_store=false` |
| M5 平台组合 | 替换静态 GenericUtils 为 typed ports；保留专用 VM thread | 新 Core + Godot Bridge | Android 输入/文件/生命周期/APK | 旧 platform adapters |
| M6 调度/渲染实验 | cooperative VM、新渲染后端各自独立 flag | A/B 双跑 | yield audit、p99、视觉/无障碍不退化 | 保留专用线程/旧后端 |
| M7 清理 | 只删除零流量旧路径 | 新路径 | 两个发布周期无回退使用，资产清单签字 | 前一稳定 tag |

M2 分为 tee/capture 和 canary 两步：先让旧对象继续直接送旧 renderer，同时旁路深复制 DTO；只有 DTO tree/timeline golden 通过后，才允许 DTO 经现有 View adapter 投影。M2 不批准新 layout 或新 renderer。

## M3-M7 阶段设计与工程执行入口

阶段文档定义语义边界；[M3M7EngineeringExecution](M3M7EngineeringExecution.md) 统一定义后续获批时的 work package record、输入 identity、允许路径、报告、回退和 gate decision。它只能降低实施时的歧义，不能降低前置门或把规划状态改成运行时证据。

阶段表是总览，细节和门禁分别由以下文档负责：

| 阶段 | 设计入口 | 当前状态 | 关键提醒 |
| --- | --- | --- | --- |
| M3 | [M3CoreExtraction](M3CoreExtraction.md) | `executionStatus=NotStarted; gateStatus=Blocked; blockerCode=PreviousGate:M2` | 只抽取纯 Core；不移动 Godot Node、不自动选存档 profile |
| M4 | [M4ResourceGraphics](M4ResourceGraphics.md) | `executionStatus=NotStarted; gateStatus=Blocked; blockerCode=PreviousGate:M3` | PixelStore 是 CPU 真相；Texture/RID 是 revision 投影 |
| M5 | [M5PlatformComposition](M5PlatformComposition.md) | `executionStatus=NotStarted; gateStatus=Blocked; blockerCode=PreviousGate:M4` | typed ports 与 SAF 先 canary；旧 Android 路径保留 |
| M6 | [M6SchedulingRenderingExperiment](M6SchedulingRenderingExperiment.md) | `executionStatus=NotStarted; gateStatus=Blocked; blockerCode=PreviousGate:M5` | cooperative VM、候选 renderer 必须独立 flag |
| M7 | [M7CleanupReleaseGovernance](M7CleanupReleaseGovernance.md) | `executionStatus=NotStarted; gateStatus=Blocked; blockerCode=PreviousGate:M6` | 两个发布周期零流量后才允许删除 |

这些状态是规划状态，不是对 M0-M2 的实施授权。并行 AI 正在完成 M0-M2 时，M3-M7 只能增加合同、fixture 计划和文档；若 M0-M2 的 source/report identity 变化，后续阶段报告必须重新生成。

## 兼容 façade

迁移期允许以下 façade，但每个都有删除条件：

- `LegacySessionFacade`：把旧 GlobalStatic 聚合为单会话入口，禁止新代码新增静态访问。
- `LegacyConsoleAdapter`：把 ConsoleDisplayLine/Part 深复制为新 DTO，不改变旧对象。
- `LegacyGraphicsAdapter`：在 PixelStore 完成前封装旧 GraphicsImage；记录同步调用和 revision。
- `LegacyPlatformPort`：封装 GenericUtils/EmueraContent 队列；新 Core 只见 port。
- `LegacyConfigProfile`：合并 config、setting.json、Snake/v24 defaults，输出不可变 GameConfig。

façade 不得长期成为第二套业务实现。每个调用带 metrics，达到零调用并完成 fixture 后才能删除。

## Feature flags

| Flag | 默认迁移值 | 目的 |
| --- | --- | --- |
| `session.isolation` | 当前以 `[migration] session_isolation=false` 落地；启动时才快照，canary 可设 true | GameSession 外壳；首等待 cross-ABA 仅为局部观察，无完整 rollout、Android 和 gate decision 时不能据此视为可发布 |
| `display.dto` | off | 新 DTO + 旧渲染器 |
| `graphics.pixel_store` | off | CPU 像素真相迁移 |
| `ports.typed` | off | 替换 GenericUtils 静态桥 |
| `vm.host_thread` | on | 保留专用 VM thread |
| `vm.main_thread_experiment` | off | 仅 benchmark build |
| `render.backend` | legacy-controls/canvas | 旧 Controls、旧 Canvas、新候选可切 |
| `audio.compat_mode` | on | 禁止自动 crossfade/voice stealing |
| `plugins.trusted_desktop` | off | 用户逐哈希授权外部 DLL |
| `compat.plan` | off → canary → on | 会话 CompatibilityPlan 外壳与 plan hash |
| `compat.frozen_registries` | off | 标准/v24/Snake 注册集合按候选冻结 |
| `compat.typed_policies` | off | 逐项替换散落的 `IsSnakeProfile` 行为分支 |
| `compat.resolver` | off | fingerprint/manifest/probe/用户选择；失败回旧手动 profile |

flags 必须进入诊断报告和 fixture manifest；不能让同一存档在不记录 flag 的情况下产生不可解释差异。

## 方言兼容子迁移

v24/Snake 分离不等待 M3 全部 Core 抽取。按 [DialectExtensionSystem](DialectExtensionSystem.md) 的 D0–D6 执行：先库存全局 Profile/无条件注册，再让 `LegacySessionFacade` 持有计划，冻结现有 handler 的注册表，最后逐个提取 typed policy 并移除全局读取。每个 Snake 改动必须同时运行 v24 基线；只有未选择模块的不变性测试通过，才能关闭对应旧分支。

## 双跑与差分

- 纯函数/解析/存档：同进程旧实现和新 Core 对同一不可变输入双跑。
- 有状态 VM：记录输入/clock/RNG/file manifest，分别重放，不让两实例共享 static/cache。
- Display：比较完整 DTO tree、transaction、revision、scroll intent 和 data-only 变化。
- Graphics：比较 surface dimensions、选定像素、全图 hash、返回值和 revision sequence。
- Godot View：旧/新后端分别截图并比较布局区域；字体抗锯齿允许掩码阈值，但 hit rect/交互必须精确。
- Android：同一 APK 内只允许可控 A/B；正式报告还需两份 artifact hash 防止 flag 污染。

## 会话切换并发算法

长加载不能持有 commit gate：

```text
request switch(selection):
  generation = atomic increment
  previousCandidateCts.exchange(newCts)?.cancel()   # gate 外，后请求可立即取消前请求
  candidate = await BuildCandidate(selection, generation, newCts.token)
  if stale/cancelled: dispose candidate; return

  lock shortCommitGate:
    if generation != latest or cancelled: stale
    old = Current
    Current = candidate.Seal()
    publish committed generation
  unlock

  detach/dispose old outside commit gate
```

候选可以并发清理，但只有最新 generation 可进入短 commit。加载进度也带 generation；旧候选不得覆盖新 UI。

## 测试资产治理

- 临时诊断脚本、一次性日志探针：问题关闭后可删除或移入工具历史。
- unit、architecture、differential、fixture、golden、fuzz seed、benchmark scenario、device protocol：必须长期版本化保留。
- 测试输出、APK、原始大日志可放 artifact store，不必提交 Git；manifest/hash/report 必须保留。
- 删除正式测试需要单独评审，证明需求移除或由等价覆盖替代；“避免垃圾文件”不是删除回归保护的理由。

## 每阶段完成定义

必须同时具备：代码/文档边界、旧与新报告、代表游戏结果、Android 状态、性能/内存变化、feature flag 回退验证、KnownLimitations 更新。只有编译成功不能关闭阶段。
