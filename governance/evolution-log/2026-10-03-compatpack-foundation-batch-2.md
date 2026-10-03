# 2026-10-03 CompatPack 社区化与解释器轻量化批次 2（信任链 / 生命周期 / 契约边界 / 治理守卫）

## 任务目标与成果

一句话：按《CompatPack 社区化与解释器轻量化实施计划》完成批次 2 全部 11 个任务——
从 v1.1 基线出发，把 CompatPack 推进到"社区包可分发（模板 + v18 第一方包 + 等价门禁）、
运行时可信任（ALC allow-list / 同字节哈希 / baseSurfaceHash 对账）、会话可持有
（per-session 租约 + 确定性 Unload）、选择路径正确（per-game env 通道 + 平台键规范化）、
契约与插件 ABI 解耦（Emuera.CompatPack 独立程序集）"，并用 P0 静态回归守卫 + 治理
文档同步收口。

本批落地（对应提交，旧→新）：

1. **社区模板与契约夹具**（c98281f）：`packs/CommunityPackTemplate/`（manifest-only 数据包
   模板）与 `tests/xUnitTest/CompatPackContractOnlyFixture/`（最小 `ICompatPack` 入口类
   对照夹具），社区接入文档 `packs/README.md`。
2. **v18 第一方数据包与表面等价门禁**（a38aae1）：`packs/gemuera.v18` +
   `tools/compat-pack/Generate-V18PackManifest`，v18 表面从基线快照等价导出（设计 §13.10）。
3. **ALC allow-list 硬化**（c938262）：包程序集默认解析回落关闭，清单外绑定
   FileLoadException 拒载——包只能绑定契约程序集（§5.2/§12.6）。
4. **会话持有 pack 集与确定性 Unload**（f1c0742，E2-R2）：`CompatPackSession` 落地，
   `CompatPackHandle`/`CompatPackSet` 实现 `ICompatPackLease`；`Program` 持有租约并在
   `ClearCompatibilityPlan`/`ResetSessionState` 先 Dispose（幂等恰好一次 Unload）再清 plan/profile。
5. **同字节哈希与加载**（7c0a7c4）：manifest 参与哈希的字节与加载字节严格同源，杜绝
   "哈希算的是 A、加载的是 B"类漂移。
6. **baseSurfaceHash 精确对账**（d49f96f，E2-R6）：manifest 声明非空且不等于当前 v24
   表面快照即拒载；算法第三方可复算（§12.9），仓内钉子测试
   `LegacySurfaceHashTests.LegacySurfaceHash_V24_IsStable` 防漂移。
7. **选择路径修正与会话作用域**（74c07df，E2-R3/R4）：`MergeSelection` 按游戏选择优先
   （外部 env 仅诊断输入 + UI 警告）、`GameKeyComparer`/`NormalizeGameKey`/`NormalizePackPath`
   平台正确游戏键、runner/in-process switch per-session 注入刷新（不再沿用上一局启用清单）。
8. **P-D 退役与 v2 贡献契约 spec**（ebd23fc/b80644d/89cfa9a/5610989）：内置模块退役的
   映射与门禁口径、v2 `IInstructionVariantContribution`/`IPolicyContribution` 代码贡献契约
   （含跨包冲突与策略适配器定义），均只交付 spec 不接线。
9. **契约程序集拆分**（113793e/f5cbad7/6aab84b，Task 10）：CompatPack 契约类型迁移至
   独立程序集 `src/EmueraCompatPack/`（`Emuera.CompatPack.dll`），`Emuera.dll` facade 收敛为
   插件 ABI；测试/工具引用更新 + 插件 ABI 边界钉测试 + ADR 记录。
10. **P0 静态回归守卫 + 治理同步**（本任务，Task 11）：新增
    `tools/compat-pack/Test-CompatPackLifecycle.ps1`，静态断言会话生命周期 P0 不回退：
    (1) `EmueraMain.StopLegacySession` 方法体内不得出现 `IsRunning` 条件早退；(2)
    `LegacySessionBackend.StopLegacyBaselineAsync` 必须调用 `ClearCompatibilityPlan`；(3)
    `CompatPackHost` 不得再现 `ActivePackModuleIds`/`ActiveVariantSelections`/
    `ResetActiveSessionProjection`；(4) `DialectPlan` 必须声明 `PackModuleIds` 与
    `VariantSelections`。接入 `tools/governance/Test-Governance.ps1`；`AGENTS.md` 构建与
    验证节 + 设计文档 §13.7/§13.9 同步验收命令与"真机 e2e 仍是最终验收"口径。

## 关键决策与 Why

1. **守卫走 TDD，且 RED 在临时副本树上做**：先把守卫脚本对着"违例样例树"（临时目录里
   的四文件副本，注入 IsRunning 早退 / 删 ClearCompatibilityPlan / 回添投影静态 / 删
   plan 投影声明）验证 exit 1 与逐项 FAIL 文案，再用干净副本控制树 + 真实仓库树验证
   exit 0。绝不为造 RED 破坏真实工作树。
2. **检查 1 必须先剥离注释/字符串再提取方法体**：`StopLegacySession` 内的 P0 说明注释
   本身写着 `!backend.IsRunning`，天真 grep 会在健康树上误报；方法体内的插值格式串
   （`$"…{error}…"`）含大括号，会破坏朴素大括号配对。守卫采用"单遍状态机剥离注释与
   字面量 → 大括号配对提取方法体 → 只认 if 条件含 `IsRunning` 且守卫语句含 `return`
   的早退模式"，违例样例特意用 `if ( !backend.IsRunning )` 空格变体验证不挑格式。
3. **修了 Test-Governance.ps1 的无参调用**：Windows PowerShell 5.1 中 `[CmdletBinding()]`
   脚本的 param 默认值表达式里 `$PSScriptRoot` 为空（脚本体内才有值），导致
   `powershell -NoProfile -ExecutionPolicy Bypass -File tools/governance/Test-Governance.ps1`
   （计划 Step 2 的验收命令）必然解析失败。把缺省路径解析移入脚本体最小修复。
4. **.ps1 含中文必须 UTF-8 BOM**：Windows PowerShell 5.1 对无 BOM 文件按 ANSI（本机
   GBK）解析，中文注释直接把解析器搞出 `UnexpectedToken`。守卫脚本与改后的
   Test-Governance.ps1 都补了 BOM。
5. **静态守卫只做"防回退"，不冒充验收**：四项检查都绑定设计文档条款（§13.6/§13.4），
   FAIL 文案直接指出后果与条款出处；AGENTS.md/设计文档明确"真机 e2e 仍是最终验收"。

## AI 表现复盘

- 有效：守卫先 RED（违例树 exit 1，四项全红且文案可行动）再 GREEN（干净副本树 + 真树
  exit 0），另用干净副本树单独隔离了"注释提及 IsRunning 是否误报"这一风险点；检查 2
  的方法体作用域顺带被真实代码验证（`LegacySessionBackend.cs:72` 另一方法也调用
  `ClearCompatibilityPlan`，守卫只认 `StopLegacyBaselineAsync` 体内的调用，未误报）。
- 低效：定位 Test-Governance.ps1 无参调用失败花了 6 次试错才收敛到 `[CmdletBinding()]`
  × param 默认值 × PS 5.1 的交互。教训：**PowerShell 行为怪癖应立刻转成"最小差异二分"
  （同内容改名的副本、逐行截断、去属性对照），而不是先怀疑路径/编码/环境**；另外带
  管道的 `cmd | tail; echo $?` 拿到的是 tail 的退出码，验证脚本退出码必须先重定向再查 `$?`。

## 验证记录

- **守卫 RED**（违例样例树，`Test-CompatPackLifecycle.ps1 -ProjectRoot <temp>`）：exit 1，
  四项检查全 FAIL——检查 1 报 `if (!backend.IsRunning) return;（条件早退）`（注入的是
  `if ( !backend.IsRunning )` 空格变体，证明不挑格式）；检查 2 报 StopLegacyBaselineAsync
  未调用 ClearCompatibilityPlan；检查 3 报 ActivePackModuleIds / ActiveVariantSelections
  回潮；检查 4 报 DialectPlan 缺少 PackModuleIds / VariantSelections 声明。
- **守卫 GREEN**（真实仓库树 + 未改动副本控制树）：两棵树均 exit 0、4/4 PASS；健康代码
  中 `StopLegacySession` 的 P0 说明注释（提及 `!backend.IsRunning`）未触发误报。
- **治理接入**：`powershell -NoProfile -ExecutionPolicy Bypass -File
  tools/governance/Test-Governance.ps1`（无参）→ 守卫四项 PASS + "M3-M7 governance
  contract passed."，整体 exit 0。
- `dotnet test tests/xUnitTest/GEmuera.Core.Tests -c Release`：112/112 通过。
- `dotnet test tests/xUnitTest/EmueraFacade.Tests -c Release`：35/35 通过。
- `dotnet test tests/xUnitTest/EmueraPluginAbi.Tests -c Release`：3/3 通过。
- `dotnet run --project tools/dialect-inventory/LegacyDialectSurfaceSmoke -c Release`：通过
  （"Legacy dialect surface smoke passed."）。
- `dotnet run --project tools/dialect-inventory/LegacyDialectRuntimeSmoke -c Release`：通过
  （v24MergedInstructions=561; snakeMergedInstructions=668; v24Functions=266;
  snakeFunctions=349）。
- `dotnet run --project tools/core-contracts/CoreContractSmoke.csproj -c Release`：通过
  （需 `DOTNET_ROLL_FORWARD=Major`）。
- `dotnet build "gemuera-c#.csproj" -c Release -nodeReuse:false -m:1`：已成功生成，
  0 警告 0 错误。

## 未完成 / 后续任务

- **Android 真机 e2e（本批最大未验证项）**：pack 游戏启动 → Back/Restart → 同进程二次
  启动（hash 防御）路径、per-game env 通道与 Android 路径大小写规范化的真机行为、
  ALC allow-list 在 Android 运行时的表现——静态守卫与桌面门禁均不能替代 APK 实测；
  等 owner 提供游戏库/设备后按 AGENTS.md 验收。
- legacy-runner 无头执行级三 profile 冒烟（v24pure/snake/erafl）本批未重跑（引擎注册表/
  方言清单无变化，未触发再生与重跑条件）。
- `tools/doc-guards/Invoke-DocGuard.ps1` 在干净 HEAD 上仍报 18 个预存错误（"Required
  authority document is missing"，DeveloperHandoff.md 权威文档缺失；Task 1 已记录的
  既有状态），非本批引入、本批未修；注意它不在 `Test-Governance.ps1` 的调用链里。
- v2 代码贡献运行时接线（IInstructionVariantContribution/IPolicyContribution）、P-D 内置
  模块退役迁移、`baseSurfaceHash` 发布端/CI 生成流水线、engine identity 与
  `targetEngineApi` 统一（设计 §13.9）。
