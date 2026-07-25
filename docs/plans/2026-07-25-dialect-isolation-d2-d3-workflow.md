---
intent: 完成 D2+D3+DIA-17，将 v24/snake/erafl 三方言从"软隔离"升级为"物理隔离 + Core-level typed policy port"，并补全 DIA-17 全量行为 fixture。
success_criteria:
  - D2: v24pure profile 下 SNAKE_* handler 类实例化计数 = 0
  - D3: dialect-inventory generated/dialect-typed-policies.json 中 ownership=Unresolved 计数 = 0
  - DIA-17: 10 BehaviorKey × 3 profile = 30 fixture 全绿
  - 既有 CoreContractSmoke + Test-CoreArchitecture.ps1 + dialect-inventory 报告全绿
risk_level: high
auto_approve: false
branch: ai/dialect-isolation-d2-d3
---

## Steps

- [ ] **Step 1: 创建工作分支**
action: 从最新 dev 创建分支 ai/dialect-isolation-d2-d3 并切换。命令：`git checkout dev && git pull origin dev && git checkout -b ai/dialect-isolation-d2-d3`
loop: false
verify: git branch --show-current | grep "^ai/dialect-isolation-d2-d3$"
gate: auto

- [ ] **Step 2: 读取关键既有文件确认起点**
action: 读取以下文件确认当前状态（不改代码）：`src/Core/Compatibility/BuiltInDialectCatalog.cs`、`src/Core/Compatibility/EraFlCompatibilityModule.cs`、`Scripts/Emuera/Compatibility/LegacyCompatibilityModules.cs`、`Scripts/Emuera/Compatibility/LegacyCompatibilityProfile.cs`、`Scripts/Emuera/GameProc/Function/FunctionIdentifier.cs`（行 100-500）。记录 SNAKE_* handler 类的完整清单与所在文件。
loop: false
verify:
  type: artifact
  path: docs/designs/2026-07-25-phase-D2-D4-dialect-isolation-design.md
  assert:
    kind: exists
gate: auto

- [ ] **Step 3: 在 Core 层新建 IInstructionContribution / IFunctionContribution 接口**
action: 在 `src/Core/Compatibility/` 新建 `DialectContributions.cs`，定义 `IInstructionContribution`（方法 `ContributeInstructions(IInstructionRegistryBuilder builder)`）和 `IFunctionContribution`（方法 `ContributeFunctions(IFunctionRegistryBuilder builder)`）。同时定义 `IInstructionRegistryBuilder` / `IFunctionRegistryBuilder` 接口（用于注册 key→handler 映射）。接口签名遵循 `docs/NewFrameworkDesign/DialectExtensionSystem.md` line 161-176 的设计。
loop: false
verify: dotnet build src/Core/Compatibility/
gate: auto

- [ ] **Step 4: 扩展 IDialectModule 接口支持 contribution**
action: 修改 `src/Core/Compatibility/BuiltInDialectCatalog.cs` 中的 `IDialectModule` 接口，使其继承 `IInstructionContribution` 和 `IFunctionContribution`（默认实现为空方法，避免破坏既有实现）。同时给 `EraFlCompatibilityModule` 添加空实现。
loop: until dotnet build src/Core/Compatibility/ 通过
max_iterations: 3
verify: dotnet build src/Core/Compatibility/
gate: auto

- [ ] **Step 5: 新建 V24CompatibilityModule Core-level 实现**
action: 在 `src/Core/Compatibility/` 新建 `V24CompatibilityModule.cs`，实现 `IDialectModule`。模块 ID = `gemuera.v24`。`ContributeInstructions` 暂留空（v24 baseline 指令仍由 FunctionIdentifier 静态注册保留，因 v24pure 必需）。模块元数据（id/version/依赖/port）参照 `BuiltInDialectCatalog.cs` 中既有 v24 注册。
loop: until dotnet build 通过
max_iterations: 3
verify: dotnet build src/Core/Compatibility/
gate: auto

- [ ] **Step 6: 新建 SnakeCompatibilityModule Core-level 实现**
action: 在 `src/Core/Compatibility/` 新建 `SnakeCompatibilityModule.cs`，实现 `IDialectModule`。模块 ID = `game.snake`，依赖 `gemuera.v24 [1.0.0,2.0.0)`。`ContributeInstructions` 中声明 Snake-only 指令 key 清单（CALLSHARP/PLAYSOUND/PLAYBGM/STOPSOUND/STOPBGM/SETSOUNDVOLUME/SETBGMVOLUME/UPDATECHECK/TOOLTIP_SETFONT/TOOLTIP_SETFONTSIZE/TOOLTIP_CUSTOM/TOOLTIP_FORMAT/TOOLTIP_IMG），但不在此处实例化 handler（由 bridge 层 LegacySnakeCompatibilityModule 委托注册）。
loop: until dotnet build 通过
max_iterations: 3
verify: dotnet build src/Core/Compatibility/
gate: auto

- [ ] **Step 7: 实现 LegacySnakeCompatibilityModule.ContributeInstructions**
action: 修改 `Scripts/Emuera/Compatibility/LegacyCompatibilityModules.cs` 中 `LegacySnakeCompatibilityModule`，实现 `IInstructionContribution`。在 `ContributeInstructions` 中实例化并注册 8 个 SNAKE_* handler：`SNAKE_CALLSHARP_Instruction` / `SNAKE_PLAYSOUND_Instruction` / `SNAKE_PLAYBGM_Instruction` / `SNAKE_STOPSOUND_Instruction` / `SNAKE_STOPBGM_Instruction` / `SNAKE_SETVOLUME_Instruction` / `SNAKE_UPDATECHECK_Instruction` / 5 个 `SNAKE_TOOLTIP_*_Instruction`。注册 key 来自 `FunctionCode` 枚举。handler 类保持在原 `Scripts/Emuera/GameProc/Function/` 命名空间。
loop: until dotnet build 通过
max_iterations: 3
verify: dotnet build Scripts/Emuera/
gate: auto

- [ ] **Step 8: 从 FunctionIdentifier.addV24CompatibilityFunctions 移除 SNAKE_* handler**
action: 修改 `Scripts/Emuera/GameProc/Function/FunctionIdentifier.cs` 行 136-159：从 `addV24CompatibilityFunctions()` 中删除 `CALLSHARP/PLAYSOUND/PLAYBGM/STOPSOUND/STOPBGM/SETSOUNDVOLUME/SETBGMVOLUME/UPDATECHECK` 共 8 行 `data.Add(...)` 语句（这些是混入 v24 的 SNAKE_* handler）。同时删除行 466-470 和 486 的 TOOLTIP_* handler 注册（移到 Snake module）。保留 `addV24CompatibilityFunctions()` 中真正的 v24 baseline 函数。
loop: until dotnet build 通过
max_iterations: 3
verify: dotnet build Scripts/Emuera/
gate: auto

- [ ] **Step 9: 重构 FunctionIdentifier static ctor 为按 profile 选择性注册**
action: 修改 `FunctionIdentifier.cs` static ctor（行 425, 486 附近）：不再无条件调用 `addSnakeCompatibilityFunctions()`。改为：static ctor 只调用 `addV24CompatibilityFunctions()`（v24 baseline，所有 profile 必需）；Snake-only 指令通过 `IInstructionContribution.ContributeInstructions` 在 plan 构建时注册。`addSnakeCompatibilityFunctions()` 方法保留但不再在 static ctor 中调用（由 LegacySnakeCompatibilityModule 调用）。
loop: until dotnet build 通过
max_iterations: 3
verify: dotnet build Scripts/Emuera/
gate: auto

- [ ] **Step 10: 实现 CompatibilityPlan.BuildInstructionRegistry 工厂方法**
action: 在 `src/Core/Compatibility/` 的 `CompatibilityPlan` 类中新增 `BuildInstructionRegistry()` 方法。该方法遍历 plan 的 active modules，调用每个 module 的 `ContributeInstructions(builder)`，返回物理隔离的 `InstructionRegistry` 实例。`InstructionRegistry` 是新建类（或扩展现有），承载 key→handler 映射，与 static `FunctionIdentifier` 注册表分离。
loop: until dotnet build 通过
max_iterations: 3
verify: dotnet build src/Core/Compatibility/
gate: auto

- [ ] **Step 11: LegacyCompatibilityProfile 新增 InstructionRegistry 字段**
action: 修改 `Scripts/Emuera/Compatibility/LegacyCompatibilityProfile.cs`：在 `Create` / `CreateForProfile` 方法中调用 `plan.BuildInstructionRegistry()` 并存为实例字段 `InstructionRegistry`。新增公开属性 `Instructions` 暴露该实例。保留既有 `IsInstructionVisible` / `IsFunctionVisible` 方法（仍用于 legacy visibility 过滤，但现在注册表本身已物理隔离）。
loop: until dotnet build 通过
max_iterations: 3
verify: dotnet build Scripts/Emuera/
gate: auto

- [ ] **Step 12: 更新 CoreContractSmoke D2 物理隔离断言**
action: 修改 `tools/core-contracts/Program.cs`（行 315-490 附近）：新增断言组 `D2_PhysicalIsolation`。断言：(a) v24pure profile 下 `SNAKE_CALLSHARP_Instruction` 等 8 个 SNAKE_* handler 类实例化计数 = 0（通过反射查询类型初始化器状态或注册表 key 不存在）；(b) v24pure profile 下 `FunctionIdentifier.GetInstructionNameDic()` 不包含 CALLSHARP/PLAYSOUND/PLAYBGM 等 key；(c) snake profile 下上述 SNAKE_* handler 在 profile.Instructions 中存在；(d) RegistrySurfaceHash 三 profile 互不相同。
loop: until dotnet test tools/core-contracts/ 全绿
max_iterations: 5
verify: cd tools/core-contracts && dotnet test
gate: auto

- [ ] **Step 13: 更新架构守卫 Test-CoreArchitecture.ps1 D2 规则**
action: 修改 `tools/core-contracts/Test-CoreArchitecture.ps1`：新增规则——禁止 `Scripts/Emuera/` 下任何文件（除 `FunctionIdentifier.cs` 的 static ctor 与 `LegacyCompatibilityModules.cs` 的 ContributeInstructions）直接实例化 `SNAKE_*_Instruction` 类。同时保留既有 line 70 规则（禁止 Program.cs 之外引用 IsSnakeProfile/IsEraFlProfile）。更新既有 visibility 过滤相关规则以反映物理隔离机制。
loop: until powershell -File tools/core-contracts/Test-CoreArchitecture.ps1 退出 0
max_iterations: 3
verify: powershell -File tools/core-contracts/Test-CoreArchitecture.ps1
gate: auto

- [ ] **Step 14: 运行 D2 阶段全量验证**
action: 依次执行：(1) `cd tools/core-contracts && dotnet test`；(2) `powershell -File tools/core-contracts/Test-CoreArchitecture.ps1`；(3) `powershell -File tools/m3-m7/Test-M3M7Governance.ps1`；(4) `powershell -File tools/dialect-inventory/Invoke-DialectInventory.ps1`。检查 v24pure profile 下 SNAKE_* handler 实例化计数 = 0、snake profile 下 ≥ 1、erafl profile 下 GMAP 指令在注册表、RegistrySurfaceHash 三 profile 互不相同。
loop: until 所有 4 个命令均退出 0 且检查项全通过
max_iterations: 3
verify:
  - type: shell
    command: cd tools/core-contracts && dotnet test
  - type: shell
    command: powershell -File tools/core-contracts/Test-CoreArchitecture.ps1
  - type: shell
    command: powershell -File tools/dialect-inventory/Invoke-DialectInventory.ps1
gate: human

- [ ] **Step 15: 增强 BehaviorPortSnapshot 承载 Snake 9 + EraFl 5 typed policy**
action: 修改 `src/Core/Compatibility/` 中 `BehaviorPortSnapshot` 类（或在 `DialectContributions.cs` 中扩展）：新增 9 个 Snake BehaviorKey 声明（AllowsExtraCallArguments / ContinuesAfterStartupFault / UsesFastDisplayRefresh / AllowsPrivateArguments / AllowsScopedVariablePreRegistration / AllowsUserDefinedVariableResolution / UsesParserDiagnostics / UsesLazyResourceIndex / UsesExtendedDisplayHistory）和 5 个 EraFl CapabilityId 声明（markup.div-v2 / input.pointer-protocol / display.dual-state-image / resource.dynamic-path / input.omitted-default-argument）。每个声明包含 key、kind（bool/port）、ownership（module id）。`BehaviorPortSnapshot` 在 `CompatibilityPlan` 中 frozen（不可变）。
loop: until dotnet build 通过
max_iterations: 3
verify: dotnet build src/Core/Compatibility/
gate: auto

- [ ] **Step 16: 重构 ISnakeCompatibilityPolicy 为 BehaviorPortSnapshot view**
action: 修改 `Scripts/Emuera/Compatibility/LegacyCompatibilityProfile.cs` 中 `ISnakeCompatibilityPolicy` 接口：实现类 `LegacySnakeCompatibilityPolicy` 构造时注入 `BehaviorPortSnapshot`，9 个 bool getter 全部委托给 snapshot 查询（不再有独立 bool 字段）。`DisabledSnakeCompatibilityPolicy` 改为对空 snapshot 的 view（所有 bool 返回 false）。
loop: until dotnet build 通过
max_iterations: 3
verify: dotnet build Scripts/Emuera/
gate: auto

- [ ] **Step 17: 重构 IEraFlCompatibilityPolicy 为 BehaviorPortSnapshot view**
action: 修改 `LegacyCompatibilityProfile.cs` 中 `IEraFlCompatibilityPolicy` 接口：实现类 `LegacyEraFlCompatibilityPolicy` 构造时注入 `BehaviorPortSnapshot`，5 个 port 方法全部委托给 snapshot。`DisabledEraFlCompatibilityPolicy` 改为对空 snapshot 的 view。
loop: until dotnet build 通过
max_iterations: 3
verify: dotnet build Scripts/Emuera/
gate: auto

- [ ] **Step 18: 更新架构守卫强制 bridge view 无独立 bool 字段**
action: 修改 `Test-CoreArchitecture.ps1`：新增规则——禁止 `LegacySnakeCompatibilityPolicy` / `LegacyEraFlCompatibilityPolicy` 类定义任何 `private bool` 或 `private readonly bool` 字段（强制委托给 snapshot）。同时禁止 bridge 层独立设置 policy 值（必须通过 Core module 的 BehaviorPortSnapshot）。
loop: until powershell -File tools/core-contracts/Test-CoreArchitecture.ps1 退出 0
max_iterations: 3
verify: powershell -File tools/core-contracts/Test-CoreArchitecture.ps1
gate: auto

- [ ] **Step 19: 重新生成 dialect-inventory 报告验证 ownership=Unresolved=0**
action: 执行 `powershell -File tools/dialect-inventory/Invoke-DialectInventory.ps1` 重新生成 `docs/NewFrameworkDesign/generated/dialect-*.json`。检查 `dialect-typed-policies.json`（或对应报告）中 `ownership=Unresolved` 计数 = 0（原 131 项全部 Resolved）。如未清零，定位未 resolve 项并补充 BehaviorPortSnapshot 声明。
loop: until ownership=Unresolved 计数 = 0
max_iterations: 5
verify: powershell -File tools/dialect-inventory/Invoke-DialectInventory.ps1
gate: auto

- [ ] **Step 20: 运行 D3 阶段全量验证**
action: 依次执行 CoreContractSmoke + Test-CoreArchitecture.ps1。检查 ISnakeCompatibilityPolicy 9 项 bool 全部从 BehaviorPortSnapshot 派生、IEraFlCompatibilityPolicy 5 项 port 全部从 BehaviorPortSnapshot 派生、BehaviorPortSnapshot 在 CompatibilityPlan 中 frozen。
loop: until 所有命令退出 0 且检查项全通过
max_iterations: 3
verify:
  - type: shell
    command: cd tools/core-contracts && dotnet test
  - type: shell
    command: powershell -File tools/core-contracts/Test-CoreArchitecture.ps1
gate: human

- [ ] **Step 21: 识别 10 BehaviorKey × 3 profile fixture 矩阵**
action: 读取 `docs/NewFrameworkDesign/generated/dialect-behavior-fixture-contracts.json` 与 `dialect-policy-surface.json`，列出 10 个 BehaviorKey 与每个 profile 的期望行为。输出到 `docs/plans/dia17-fixture-matrix.md`（临时文件，PR1 合并前删除）。每个 fixture 包含：BehaviorKey、profile、输入、期望输出/状态。
loop: false
verify:
  type: artifact
  path: docs/plans/dia17-fixture-matrix.md
  assert:
    kind: exists
gate: auto

- [ ] **Step 22: 新建 DIA17Fixtures.cs xUnit 测试文件**
action: 在 `tools/core-contracts/` 新建 `DIA17Fixtures.cs`。添加 `[Trait("Category", "DIA17")]` 标签。为每个 BehaviorKey × profile 组合写一个 `[Fact]` 测试方法（共 30 个）。每个测试方法构造对应 profile 的 CompatibilityPlan，执行输入，断言期望输出。fixture 内容参照 Step 21 的矩阵。纯 C# 逻辑用 xUnit。
loop: until dotnet build tools/core-contracts/ 通过
max_iterations: 5
verify: dotnet build tools/core-contracts/
gate: auto

- [ ] **Step 23: 补充 GDUnit4 场景 fixture（如有 Godot 场景相关 BehaviorKey）**
action: 识别 10 个 BehaviorKey 中是否有需要 Godot 场景的（如 UsesFastDisplayRefresh 涉及 EmueraConsole 渲染）。如有，在 `tests/gdunit4/`（如不存在则创建）新建对应 fixture，使用 GDUnit4 框架。其余纯逻辑 fixture 保留在 xUnit。如全部为纯逻辑，跳过此步骤。
loop: until GDUnit4 测试通过（或确认全部为纯逻辑）
max_iterations: 5
verify: cd tests/gdunit4 && godot --headless --path . -d -s addons/gdUnit4/bin/GdUnitRunner.cmd
gate: auto

- [ ] **Step 24: 运行 DIA-17 全量 fixture 验证**
action: 执行 `cd tools/core-contracts && dotnet test --filter Category=DIA17`。确认 30 个 fixture 全绿。每个 fixture 包含两侧断言（v24pure 期望 vs snake/erafl 期望差分）。如有失败，定位失败项并修正 fixture 或实现。
loop: until 30 个 fixture 全绿
max_iterations: 5
verify: cd tools/core-contracts && dotnet test --filter Category=DIA17
gate: human

- [ ] **Step 25: 重新生成 dialect-inventory 报告更新 DIA-17 状态**
action: 执行 `powershell -File tools/dialect-inventory/Invoke-DialectInventory.ps1` 重新生成报告。检查 `dialect-behavior-fixture-contracts.json` 中 DIA-17 状态从 Planned/Uncovered 更新为 Covered。
loop: until DIA-17 状态全部 Covered
max_iterations: 3
verify: powershell -File tools/dialect-inventory/Invoke-DialectInventory.ps1
gate: auto

- [ ] **Step 26: 清理临时文件**
action: 删除 `docs/plans/dia17-fixture-matrix.md`（Step 21 创建的临时文件）。AGENTS.md 要求任务结束后删除冗余临时测试文件。
loop: false
verify:
  - type: artifact
    path: docs/plans/dia17-fixture-matrix.md
    assert:
      kind: not-exists
gate: auto

- [ ] **Step 27: 运行 PR1 全量回归验证**
action: 依次执行：(1) `cd tools/core-contracts && dotnet test`（全部测试含 D2/D3/DIA17）；(2) `powershell -File tools/core-contracts/Test-CoreArchitecture.ps1`；(3) `powershell -File tools/m3-m7/Test-M3M7Governance.ps1`；(4) `powershell -File tools/dialect-inventory/Invoke-DialectInventory.ps1`。同时用 Godot 编辑器打开项目确认无错误，F6 运行 first_window.tscn 可见启动器界面。
loop: until 全部命令退出 0 且 Godot 编辑器无错误
max_iterations: 3
verify:
  - type: shell
    command: cd tools/core-contracts && dotnet test
  - type: shell
    command: powershell -File tools/core-contracts/Test-CoreArchitecture.ps1
  - type: shell
    command: powershell -File tools/m3-m7/Test-M3M7Governance.ps1
gate: human

- [ ] **Step 28: 提交 PR1 代码**
action: 暂存改动文件（仅 PR1 相关：src/Core/Compatibility/、Scripts/Emuera/Compatibility/、Scripts/Emuera/GameProc/Function/FunctionIdentifier.cs、tools/core-contracts/、tests/gdunit4/、docs/NewFrameworkDesign/generated/）。提交信息（中文）：`完成 D2+D3+DIA-17：v24/snake/erafl 方言物理隔离与 typed policy port 升级`。不推送、不开 PR（等用户确认）。
loop: false
verify: git log -1 --pretty=format:"%s" | grep "D2+D3+DIA-17"
gate: human

- [ ] **Step 29: 推送分支并创建 PR**
action: 推送 `ai/dialect-isolation-d2-d3` 分支到 origin。使用 `gh pr create --base dev --title "完成 D2+D3+DIA-17：v24/snake/erafl 方言物理隔离与 typed policy port 升级" --body "..."` 创建 PR，body 用中文描述改动范围、验证结果、与设计文档链接。PR body 包含：改动摘要、D2/D3/DIA-17 验证结果、设计文档链接、PR2（D4）后续计划。
loop: false
verify: gh pr view --json url --jq .url
gate: human
