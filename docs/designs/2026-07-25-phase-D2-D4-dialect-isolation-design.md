---
design_type: phase
created_at: 2026-07-25
---

# D2+D3+D4 方言物理隔离与会话化设计

承接 `docs/NewFrameworkDesign/DialectExtensionSystem.md` 战略 initiative 的剩余阶段切片（D2 冻结注册表物理隔离 / D3 typed policy port / D4 去全局化）。当前状态：抽象层已分离约 50-60%，legacy handler 实现层仍靠 visibility 过滤"软隔离"，未达 DialectExtensionSystem.md 要求的"未选择模块不可见 = 注册表物理不包含"硬隔离。

## Intent Contract

```
intent: 完成 D2+D3+D4，将 v24/snake/erafl 三方言从"软隔离"升级为"物理隔离 + 会话级实例化 + 显式参数传递"，符合软件工程接口/模块标准。
constraints:
  - 不破坏既有 Core 抽象（src/Core/Compatibility/）和 bridge 投影（Scripts/Emuera/Compatibility/）的接口形状
  - 保留 LegacyCompatibilityProfile 作为 ICompatibilityProfile 的桥接实现（避免重写 ~20 个调用点的接口形状）
  - 不删除既有 DIA-01~17 静态库存报告与架构守卫
  - 保持 PR 拆分边界：D2+D3 为一个 PR，D4 为另一个 PR
success_criteria:
  - D2: v24pure profile 下 SNAKE_* handler 类不被实例化、不进入指令注册表（物理验证，非 visibility 过滤）
  - D3: 131 项 BehaviorKey ownership 全部 Resolved；Snake/EraFl policy 通过 Core-level BehaviorPortSnapshot 暴露
  - D4: Program.Compatibility 静态字段删除；EmueraCoreProfile enum 删除；IsSnakeProfile/IsEraFlProfile adapter 删除；~20 个调用点改为 ICompatibilityProfile 显式参数
  - 全量 DIA-17 fixture（30+）覆盖 10 个 BehaviorKey × 3 个 profile
  - 既有 CoreContractSmoke + Test-CoreArchitecture.ps1 + dialect-inventory 报告全绿
risk_level: high（D4 一次性干净重写涉及 ~30 文件，且 Program.Compatibility 是进程级单例）
```

## Verification Contract

```
verify_steps:
  # === PR1: D2+D3 ===
  - stage: D2-physical-isolation
    run_tests:
      - "cd tools/core-contracts && dotnet test"
      - "powershell -File tools/core-contracts/Test-CoreArchitecture.ps1"
      - "powershell -File tools/m3-m7/Test-M3M7Governance.ps1"
      - "powershell -File tools/dialect-inventory/Invoke-DialectInventory.ps1"
    check:
      - v24pure profile 下 SNAKE_CALLSHARP_Instruction 等 8 个 SNAKE_* handler 类实例化计数 = 0
      - v24pure profile 下 FunctionIdentifier.GetInstructionNameDic() 不包含 CALLSHARP/PLAYSOUND/PLAYBGM/STOPSOUND/STOPBGM/SETSOUNDVOLUME/SETBGMVOLUME/UPDATECHECK/TOOLTIP_* keys
      - snake profile 下上述 SNAKE_* handler 类实例化计数 ≥ 1
      - erafl profile 下 GMAP 相关指令在注册表中
      - RegistrySurfaceHash 在三 profile 间互不相同
    confirm: "D2 物理隔离生效，未选择模块的 handler 类不被实例化"

  - stage: D3-typed-policy-port
    run_tests:
      - "cd tools/core-contracts && dotnet test"
      - "powershell -File tools/core-contracts/Test-CoreArchitecture.ps1"
    check:
      - ISnakeCompatibilityPolicy 的 9 项 bool 全部从 Core-level BehaviorPortSnapshot 派生
      - IEraFlCompatibilityPolicy 的 5 项 port 全部从 Core-level BehaviorPortSnapshot 派生
      - dialect-inventory generated/dialect-typed-policies.json 中 ownership=Unresolved 计数 = 0
      - BehaviorPortSnapshot 在 CompatibilityPlan 中 frozen
    confirm: "D3 typed policy 升级完成，131 项 ownership 全部 Resolved"

  - stage: DIA-17-full-fixture
    run_tests:
      - "cd tools/core-contracts && dotnet test --filter Category=DIA17"
      - "cd tests/gdunit4 && godot --headless --path . -d -s addons/gdUnit4/bin/GdUnitRunner.cmd"
    check:
      - 10 个 BehaviorKey × 3 个 profile = 30 个 fixture 全绿
      - 每个 fixture 包含两侧断言（v24pure vs snake/erafl 行为差分）
    confirm: "DIA-17 全量行为 fixture 通过"

  # === PR2: D4 ===
  - stage: D4-deglobalization
    run_tests:
      - "cd tools/core-contracts && dotnet test"
      - "powershell -File tools/core-contracts/Test-CoreArchitecture.ps1"
      - F6 运行 first_window.tscn / main.tscn 验证启动链
    check:
      - grep "Program\.Compatibility" Scripts/Emuera/ 返回 0 行
      - grep "Program\.IsSnakeProfile|Program\.IsEraFlProfile" Scripts/ 返回 0 行
      - grep "EmueraCoreProfile\." Scripts/ 返回 0 行
      - 架构守卫更新为"禁止任何代码定义进程级 static compatibility 字段"
      - ~20 个原 Program.Compatibility.* 调用点全部改为 ICompatibilityProfile 显式参数
    confirm: "D4 去全局化完成，进程级 static 状态消除"

  - stage: regression
    run_tests:
      - 全量测试套件
      - Godot 编辑器打开项目无错误
      - F6 运行 first_window.tscn 可见启动器界面
      - 手动选择 v24pure/snake/erafl 三个 profile 各启动一次，无崩溃
    confirm: "回归测试通过，可合并"
```

## Governance Contract

```
approval_gates:
  - gate: PR1-G1-design-approved
    when: after design doc saved and self-checked
    who: user
    action: approve design before writing-plans

  - gate: PR1-G2-plan-approved
    when: after writing-plans generates workflow file
    who: user
    action: approve workflow before execution

  - gate: PR1-G3-D2-complete
    when: D2 阶段所有任务完成、D2 验证步骤全绿
    who: user
    action: review D2 diff before starting D3

  - gate: PR1-G4-D3-complete
    when: D3 阶段所有任务完成、D3 验证步骤全绿
    who: user
    action: review D3 diff before DIA-17 fixture

  - gate: PR1-G5-DIA17-complete
    when: DIA-17 全量 fixture 通过
    who: user
    action: approve PR1 before merging to dev

  - gate: PR2-G1-D4-design-revisit
    when: PR1 merged, before starting PR2
    who: user
    action: revisit D4 design based on PR1 final state

  - gate: PR2-G2-D4-implementation-checkpoint
    when: 每完成 5 个调用点迁移
    who: user
    action: checkpoint review (early failure detection for 一次性干净重写)

  - gate: PR2-G3-D4-complete
    when: D4 验证步骤全绿
    who: user
    action: approve PR2 before merging to dev

rollback:
  PR1:
    - 若 D2 实施失败：回滚 FunctionIdentifier 改动，保留旧 addV24/addSnake 双注册路径
    - 若 D3 实施失败：回滚 BehaviorPortSnapshot 升级，保留 bridge-level ISnakeCompatibilityPolicy 为单一真相
    - 若 PR1 整体不可合并：丢弃分支，dev 不受影响
  PR2:
    - 若 D4 一次性重写遇不可逾越障碍：按 contingency，PR1 已合并部分保留，D4 改为 transitional session-singleton 后续 PR
    - 若 D4 引入回归：回滚 PR2 整体，PR1 已合并状态不受影响
  通用:
    - 所有改动在 ai/dialect-isolation-d2-d3 和 ai/dialect-isolation-d4 分支进行
    - 不强制推送、不硬重置、不删除远端分支（遵守 AGENTS.md 协作规则）

ownership:
  accountable: AI Agent (本会话)
  reviewers: user (代码审查 + approval gates)
  branch_naming: ai/dialect-isolation-d2-d3 (PR1), ai/dialect-isolation-d4 (PR2)
  pr_target: dev 分支
  pr_language: 中文标题与说明（遵守 AGENTS.md）
```

## Scope

| 在范围内 | 不在范围内 |
|---|---|
| D2: FunctionIdentifier 静态注册表按模块物理拆分 | D6: 新增第四个非 Snake/eraFL 魔改模板 |
| D2: 移除 addV24CompatibilityFunctions 中混入的 SNAKE_* handler | 新增 EE/EM 方言支持 |
| D2: static ctor 改为按 profile 选择性注册 | 重写 LegacyCompatibilityProfile visibility 过滤逻辑（已工作） |
| D3: ISnakeCompatibilityPolicy / IEraFlCompatibilityPolicy 升级为 Core-level typed port | 引入新 BehaviorKey（保持 10 个不变） |
| D3: 131 项 ownership 全部 Resolved | 改变 BehaviorKey 命名或语义 |
| D4: 删除 Program.Compatibility 静态字段 | 改变 FirstWindow 启动器目录路由契约 |
| D4: 删除 EmueraCoreProfile enum + IsSnakeProfile/IsEraFlProfile adapter | 改变 BuiltInGameCompatibilityResolver 自动解析逻辑 |
| D4: ~20 个调用点改为 ICompatibilityProfile 显式参数 | 改变 EraFlCompatibilityModule 纯逻辑 |
| DIA-17: 全量 fixture（30+） | Android APK 真机验证（无环境） |
| PR1: D2+D3（可独立合并） | PR2 失败时回滚 PR1（PR1 设计为可独立交付） |
| PR2: D4 一次性干净重写（独立 PR） | |

## Decisions

| # | 决策点 | 选择 | 拒绝的替代方案 | 理由 |
|---|---|---|---|---|
| D1 | D2 物理隔离机制 | 方案 A：每模块贡献注册表。`IDialectModule` 通过 `IInstructionContribution` / `IFunctionContribution` 贡献指令；`InstructionRegistry` 按 session 从 active modules 物理构建 | B: profile-keyed 静态缓存（仍是软隔离）；C: V24/Snake/FunctionIdentifier 子类化（重型） | 满足 D2 物理隔离硬要求 + 与既有 Core `IDialectContribution` 接口家族对齐 + 为 D6 新魔改模板铺路 |
| D2 | contribution 注册时机 | lazy registration：每模块声明 `ContributeInstructions(IInstructionRegistryBuilder)`，按需调用 | eager: static ctor 中全部注册然后过滤 | 真 lazy = v24pure 下 SNAKE_* handler 类不被加载 |
| D3 | typed policy 接口位置 | Core 层 `BehaviorPortSnapshot` 为单一真相，bridge 层 `ISnakeCompatibilityPolicy` 改为 `BehaviorPortSnapshot` 的 view | 保留 bridge-level 接口为单一真相；删除 bridge-level 接口直接用 Core | 单一真相避免漂移；bridge view 保留以最小化 ~20 调用点改动 |
| D4 | 会话级注入方式 | 一次性干净重写：删除 `Program.Compatibility` 静态字段；新建 `ICompatibilityProfile` 参数在调用链上显式传递 | A: transitional session-singleton；C: 包装器不动调用点 | 用户明确选择；干净；避免 transitional 状态长期存留 |
| D5 | 旧 enum 处理 | 删除 `EmueraCoreProfile` enum；profile 选择改为字符串 ID + `CompatibilityPlan` 作为运行时载体 | 保留 enum 作为字符串常量集合 | 删除 enum 才能消除"分支判断"动机；字符串 ID 已存在且支持扩展 |
| D6 | PR 拆分边界 | PR1 = D2+D3（可独立合并、可独立验证）；PR2 = D4（依赖 PR1 但独立提交） | 单一 PR；三个 PR | D2+D3 在抽象层不冲突可合并；D4 风险高独立；遇阻时不阻塞 PR1 |
| D7 | DIA-17 fixture 实现框架 | xUnit（纯 C# 行为差分）+ GDUnit4（涉及 Godot 场景的 fixture） | 仅 xUnit；仅 GDUnit4 | AGENTS.md 明确要求两者协同；纯逻辑用 xUnit 高效，场景相关用 GDUnit4 |
| D8 | 现有测试更新策略 | 既有架构守卫 `Test-CoreArchitecture.ps1` 增强：新增"v24pure profile 下 SNAKE_* handler 不实例化"断言；既有 smoke 测试更新期望 | 新建独立守卫文件 | 守卫集中可读；避免碎片化 |
| D9 | `RegistrySurfaceHash` 命运 | 保留作为 plan identity 的一部分（仍用于 cache key 与可重现性诊断），但不再用于"过滤" | 删除 | cache key 仍有价值；删除会破坏诊断链路 |
| D10 | snake-only handler 类位置 | 保持在原 `Scripts/Emuera/GameProc/Function/` 命名空间，但通过 `LegacySnakeCompatibilityModule.ContributeInstructions()` 注册 | 移动到独立 Snake 命名空间 | 最小化文件移动；模块边界由注册路径决定，不靠物理位置 |

关键架构约束（贯穿所有决策）：

1. **单一真相原则**：Core 层 `BehaviorPortSnapshot` 是 typed policy 的单一真相；bridge 层是 view
2. **物理隔离 = 不实例化**：D2 完成后，`SNAKE_CALLSHARP_Instruction` 类在 v24pure profile 下应 0 次实例化
3. **显式参数 > 静态访问**：D4 完成后，`Program.Compatibility` 字符串在 legacy 代码中应 0 次出现
4. **PR1 可独立交付**：D2+D3 不依赖 D4 任何改动

## Surface

**Core 层（src/Core/Compatibility/）**：扩展 `IDialectModule` 接口家族，新增 `IInstructionContribution` / `IFunctionContribution` 接口（在 DialectExtensionSystem.md 已定义但未实现）。`EraFlCompatibilityModule` 增加 `ContributeInstructions` / `ContributeFunctions` 方法实现。新建 `V24CompatibilityModule` 与 `SnakeCompatibilityModule` Core-level 实现（迁移 legacy bridge 中已有的 module 知识）。`BehaviorPortSnapshot` 增强：承载 Snake 9 项 + EraFl 5 项 typed policy 全部声明，作为 CompatibilityPlan 的 frozen 字段。`CompatibilityPlan` 新增 `BuildInstructionRegistry()` 工厂方法，返回物理隔离的注册表实例。

**Bridge 层（Scripts/Emuera/Compatibility/）**：`LegacyV24CompatibilityModule` / `LegacySnakeCompatibilityModule` / `LegacyEraFlCompatibilityModule` 实现 `IInstructionContribution` / `IFunctionContribution`（通过委托给 Core module 或直接贡献）。`ISnakeCompatibilityPolicy` / `IEraFlCompatibilityPolicy` 改造为 `BehaviorPortSnapshot` 的只读 view（构造时注入 snapshot，所有 getter 委托给 snapshot）。`LegacyCompatibilityProfile` 新增 `InstructionRegistry` 实例字段（从 plan 构建），legacy 代码通过 profile 访问注册表而非 `FunctionIdentifier.GetInstructionNameDic()`。

**Legacy handler 层（Scripts/Emuera/GameProc/Function/）**：`FunctionIdentifier` 的 static ctor 不再无条件调用 `addV24CompatibilityFunctions()` + `addSnakeCompatibilityFunctions()`。改为：保留 v24 baseline 注册（v24pure 必需），SNAKE_* handler 注册移到 `LegacySnakeCompatibilityModule.ContributeInstructions()`，eraFL 特有指令移到 `LegacyEraFlCompatibilityModule.ContributeInstructions()`。`addV24CompatibilityFunctions()` 中混入的 8 个 SNAKE_* handler（CALLSHARP/PLAYSOUND/PLAYBGM/STOPSOUND/STOPBGM/SETSOUNDVOLUME/SETBGMVOLUME/UPDATECHECK）+ TOOLTIP_* handler 全部迁移到 Snake module。

**会话入口（Scripts/Emuera/Program.cs）**：D4 阶段删除 `Compatibility` 静态字段、`EmueraCoreProfile` enum、`IsSnakeProfile` / `IsEraFlProfile` adapter。`Program.DetectCoreProfile()` 改为返回 `CompatibilityPlan`（已存在）。新增 `ICompatibilityProfile` 接口（由 `LegacyCompatibilityProfile` 实现），作为 ~20 个调用点的显式参数类型。`Program.Main()` 接收 profile 参数，沿调用链传递。

**调用点迁移（~20 个文件）**：`Creator.Method.cs` / `EmueraConsole.cs` / `ParserMediator.cs` / `IdentifierDictionary.cs` / `HeaderFileLoader.cs` / `ErbLoader.cs` / `Process.CalledFunction.cs` / `Process.SystemProc.cs` / `Process.State.cs` / `ArgumentBuilder.cs` / `ExpressionParser.cs` / `AppContents.cs` / `EmueraContent.cs` / `EmueraThread.cs` 等。每个文件的 `Program.Compatibility.Snake.*` / `Program.Compatibility.EraFl.*` 改为方法参数 `ICompatibilityProfile compatibility`（或局部变量从上下文获取）。优先通过构造函数注入或方法签名扩展，避免 thread-local 等隐藏机制。

**测试（tools/core-contracts/）**：`CoreContractSmoke` 新增 D2 物理隔离断言（v24pure 下 SNAKE_* handler 类实例化计数 = 0，通过反射或类型初始化器跟踪）。`Test-CoreArchitecture.ps1` 新增架构守卫规则：禁止 legacy 代码引用 `FunctionIdentifier.GetInstructionNameDic()` 未过滤结果；D4 完成后追加"禁止任何代码定义进程级 static compatibility 字段"。新建 `DIA17Fixtures.cs`（xUnit，30+ fixture，覆盖 10 BehaviorKey × 3 profile）。新建 GDUnit4 场景 fixture（如有 Godot 场景相关 BehaviorKey）。

**dialect-inventory 报告**：DIA-17 fixture 完成后，重新生成 `generated/dialect-behavior-fixture-contracts.json` 等报告，状态从 Planned/Uncovered 更新为 Covered。

## Risks & Open Questions

**风险**：

1. **D2 lazy registration 可能影响启动性能**：原 static ctor 一次性注册，改为按 session 构建可能增加首次启动延迟。需在 D2 验证中测量。
2. **D3 BehaviorPortSnapshot 升级可能引入桥接层不一致**：bridge view 必须严格委托给 snapshot，否则会产生漂移。需通过架构守卫强制"bridge 实现无独立 bool 字段"。
3. **D4 一次性干净重写涉及 ~30 文件**：中途遇阻风险高。已与用户确认 contingency：拆分 PR，PR1 已合并部分保留。
4. **Godot 主节点生命周期与 profile 注入的兼容性**：`EmueraMain._Ready()` / `EmueraThread.Start()` 需在正确时机接收 profile 参数。需验证 Godot 节点构造时 profile 已就绪。
5. **架构守卫更新可能触发既有代码违规**：D2 完成后，原有"visibility 过滤"逻辑可能被守卫判定为违规。需同步更新守卫规则以反映新物理隔离机制。

**Open Questions**：

1. D2 中 `IInstructionContribution` 是否需要支持"覆盖"语义（snake 覆盖 v24 的某个 handler）？还是只支持"贡献新 handler"？— 倾向于只支持贡献新 handler，覆盖通过 module DAG 顺序解决。
2. D4 中 `ICompatibilityProfile` 是否应包含 `InstructionRegistry` 引用？还是分离为两个接口？— 倾向于合并，简化调用点签名。
3. DIA-17 fixture 中"行为差分"如何定义？是"相同 ERB 代码在不同 profile 下产生不同输出"，还是"相同指令在不同 profile 下可见性不同"？— 倾向于两者都覆盖，前者验证语义差分，后者验证 visibility 物理隔离。
