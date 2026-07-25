# AI 实施快反馈与分层验证

## 文档地位

本文规定 AI 按 NewFrameworkDesign 修改代码时，如何选择阅读范围、测试粒度和发布门。目标是缩短一次小改动到获得可信反馈的时间，而不是降低兼容、Android、存档或生命周期的验收标准。

本文不授权绕过 [M0M2ImplementationBaseline](M0M2ImplementationBaseline.md) 的阶段门，不把 FastLoop 结果写成阶段 Passed，也不允许以“AI 很快”为理由删除差分、fixture、回退或真机证据。

## 审计结论

当前慢点不能简单归因于 TDD：

- 当前项目根没有 tests 或 test 目录；主项目和 Core 项目未发现 xUnit、Microsoft.NET.Test.Sdk 等测试项目引用。
- 已有的是 gdUnit4 插件、Core contract smoke，以及 27 个偏向 inventory、baseline、runner、fixture 的 PowerShell 契约测试。
- 因此现在的主要成本不是大量快速单元测试，而是为了迁移阶段准备 identity、真实游戏、可视 Godot、Android、性能、回退和签署证据。
- 把 M0–M7 的关闭报告、APK 或真实游戏 replay 当作每一处 AI 小改动的默认命令，会把正确的发布门误用为编辑反馈环。

TDD 只会在两种情况下显著拖慢速度：测试对象没有可隔离边界，或每次微小修改都被强制运行集成/设备级测试。前者应先创建 seam，后者应改为分层验证，不能直接取消测试。

## 四层验证路径

| 路径 | 适用时机 | AI 在一次编辑内必须做什么 | 不应做什么 | 结果含义 |
| --- | --- | --- | --- | --- |
| Explore | 只读定位、行为不明、准备任务包 | 阅读 CODE_MAP 和最多三个权威任务文档，定位 owner、fixture、风险和禁止路径 | 不修改代码、不跑全量测试 | 形成可实施任务包，不是验证 |
| FastLoop | 单一纯逻辑、局部 adapter、单一场景行为或已知回归 | 运行一个定向、可重复的 build 或测试；记录命令与结果 | 不跑 APK、全游戏 replay、全量性能或所有 fixture | 证明本次编辑没有打破局部契约 |
| WorkPackage | 一个稳定 work package 准备评审 | 汇总目标差分、fuzz、架构、生命周期和回退证据 | 不把未覆盖项隐藏成绿色 | 允许进入 ReadyForReview |
| PhaseRelease | 阶段关闭、canary、发布或删除旧路径 | 运行真实游戏、可视 Godot、重复性、Android/设备、性能、回退与签署 | 不作为每次编辑的默认命令 | 唯一可改变阶段 Passed 或发布结论的路径 |

FastLoop 的目标是可在开发循环内快速得到信号。具体时长由任务包和本机工具链记录，不在本文承诺固定秒数；一旦命令需要真实游戏、显示服务器、APK 或设备，它就属于 WorkPackage 或 PhaseRelease。

## TDD 的正确适用面

| 变更类型 | 推荐方法 | 首选验证 | 不应采用的做法 |
| --- | --- | --- | --- |
| 纯 Core 逻辑，例如 parser、变量、codec、hash、generation guard | 测试先行或红绿重构 | 小型 C# 单元测试；当前过渡期可用 Core smoke 作为最低守卫 | 为了测试去启动 Godot 或真实游戏 |
| 旧 Emuera 行为、错误语义、等待顺序不明 | 特征化测试先行 | trace、fixture、旧/新 differential | 凭设计猜新预期，或先重构再找基线 |
| 单一 Godot 场景、输入控件、signal、focus | 小型场景测试 | gdUnit4 的单套件或 SceneRunner；输入测试使用带窗口环境 | headless 下假定触摸/键鼠模拟可信，或加载完整游戏 |
| 图片、HTML、Canvas、CBG、命中矩形 | 先模型/事务断言，再补 visual golden | 局部 DTO、hit、截图 fixture | 只看截图，或每次改一行就跑全 eraFL |
| Android、SAF、真实游戏、性能、内存、发布删除 | 验收测试与 canary | PhaseRelease 的设备/runner/回退报告 | 把它们伪装成 TDD 的红绿循环 |
| 文档或任务包变更 | 文档守卫 | 文档链接、表格、结构检查 | 跑无关 Godot、APK 或真实游戏 |

对遗留系统而言，先锁住当前可观察行为的特征化测试，通常比强行从空白写“理想单元测试”更快，也更安全。修复确认后再把稳定、纯净的部分下沉为单元测试。

## AI 任务包

每次要求 AI 修改代码时，调用者应提供或让 AI 先生成一页任务包。任务包最多引用三个主文档；只有遇到冲突、未覆盖行为或外部协议时才按链接继续读取。它至少包含：

| 字段 | 作用 |
| --- | --- |
| 目标与非目标 | 一句话说明要改变的可观察行为，以及明确不碰的 subsystem |
| 入口 | 文件、类型、方法、owner 和调用方向；先由 CODE_MAP 定位 |
| 事实来源 | 至多三个权威文档、一个 fixture 或 trace；外部 XEmuera 只在确有语义争议时加入 |
| 验收例 | 输入、期望 state/effect/error/display 或 bug reproduction |
| FastLoop 命令 | 唯一必跑的定向 build、smoke、单元或场景测试，以及预期 exit code |
| 升级条件 | 什么情况必须转入 WorkPackage，例如跨 Session、存档、资源、平台、全局 static、显示 transaction 或安全边界 |
| 禁止路径与回退 | 不可修改的旧链路、flag、旧 adapter 或还原步骤 |

没有任务包时，AI 不应自动阅读整个 NewFrameworkDesign 或把所有阶段门复制到一次小修复。反过来，任务一旦越过升级条件，就必须停止 FastLoop 假设，转入对应阶段文档与证据包。

## 测试基础设施的补齐顺序

当前测试基础设施仍处于迁移期，按以下顺序补齐，而不是要求 AI 一次性覆盖全部历史代码：

1. 为纯 C# Core 建立真正的单元测试项目和最小 fixture factory；Core smoke 保留为架构/合同守卫，但不替代行为测试。
2. 为当前最常改的纯逻辑入口建立少量高价值回归例，例如解析、变量、保存候选、generation guard。
3. 只为有 Godot Node、signal、输入或布局依赖的功能建立 gdUnit4 场景测试；每个套件独立清理 Node、signal、Callable 和临时资源。
4. 把真实游戏、可视显示、Android 和性能测试保留在独立 runner/设备队列，通过 task package 的升级条件触发。

gdUnit4 不用于代替 Android 真机，也不应用于纯 Core 的每个函数。xUnit 或等价纯 .NET 测试不应引用 GodotSharp、SceneTree 或外部游戏目录。

## 推荐执行顺序

1. Explore：从 CODE_MAP 和任务包定位一处 owner，确认是否属于纯逻辑、Godot 场景、遗留兼容或平台边界。
2. 选定 FastLoop：为纯逻辑先补或更新一个最小行为测试；为遗留语义先采集特征化 trace；为场景只运行一个相关 suite。
3. 实施最小改动：一次改动只改变一个 owner，不顺带重构相邻模块。
4. 跑 FastLoop：失败时只在当前任务包范围内诊断；通过后记录结果。
5. 命中升级条件时，建立或更新 work-package record，并运行对应的 WorkPackage/PhaseRelease 检查。
6. 只有 work package 关闭或发布时，才生成完整 identity、差分、生命周期、设备、回退和签署报告。

## 度量与改进

流程改进看反馈质量，而不是测试数量。每个周期记录：

- 从开始编辑到 FastLoop 首次结果的时间；
- FastLoop 捕获的回归数，以及后来被 WorkPackage/PhaseRelease 捕获的遗漏数；
- 因缺少 seam、fixture、命令或任务包而升级为大范围调研的次数；
- 实际需要真实游戏、可视 Godot、APK 或设备的改动比例；
- 回退是否成功，以及任何 ReportInvalidated 的原因。

若 FastLoop 经常需要启动完整 Godot 或 APK，说明测试边界还没有拆开；若每次修改都重新读取全部设计文档，说明任务包和入口索引仍不够精确。两者都不是删除 TDD 的理由。

## 与现有门禁的关系

本流程的唯一作用是把编辑反馈与阶段放行分开。当前 M0–M2 仍以 [M0M2ImplementationBaseline](M0M2ImplementationBaseline.md) 为唯一实施授权；M3–M7 仍以 [M3M7EngineeringExecution](M3M7EngineeringExecution.md) 的 work package、差分、回退和 gate decision 为准。测试维度、fixture 和设备结论仍由 [VerificationPlan](VerificationPlan.md) 与 [HowToRun](HowToRun.md) 定义。
