# 2026-10-02 CompatPack 数据包 + 会话所有权 + capability 激活解耦（解释器轻量化地基）

## 任务目标与成果

一句话：按架构评审结论推进"解释器 mod / 方言兼容包"方向——把 CompatPack 从
"只有内置模块在场才能工作的表面选择器"推进为"社区可写 manifest-only 数据包、行为激活
不依赖内置模块、投影数据随 plan 会话内聚"的地基，并修复评审发现的默认路径会话生命周期 P0。

本批落地：

1. **manifest-only 数据包**：无 `ICompatPack` 入口类的程序集按数据包加载；manifest 新增
   `surface` 名单（add/hide instructions/functions）与 `baseProfileId`（缺省 v24pure，
   不匹配当前会话基线即拒载）；新增函数返回类型由引擎六 profile 清单解析。
2. **saveProfileId v1 显式拒载**：legacy 存档路径尚不消费 plan.SaveProfileId，携带即
   拒载（不再是静默 no-op）；组装器同时预留 v2 聚合语义（跨包同键同值幂等、异值拒载、
   覆盖基线并进入 plan envelope hash）。
3. **投影数据 plan 内聚**：`DialectPlan` 新增 `PackModuleIds` / `VariantSelections`；
   `LegacyCompatibilityProfile.Create(plan, scoped)` 只从 plan 重建投影；删除
   `CompatPackHost.ActivePackModuleIds/ActiveVariantSelections/ResetActiveSessionProjection`
   及所有手动复位点（同一提交内不再有 process-wide 投影静态）。
4. **capability 激活与 module.Apply 解耦**：snake/erafl/megaten 三个策略统一在
   `LegacyCompatibilityModuleCatalog.Compose` 从 `plan.CapabilityIds` 构造；模块选中只决定
   `IsEnabled`；具体 flag 从 capability 派生。snake DialectFunctionContract 也改为
   "模块选中或完整 snake capability 集"时统一激活。社区包在 v24pure 上声明完整 snake
   capability 集即可激活 policy + DFC（SurfaceSmoke 增哨兵；megaten 补三个 behavior id）。
5. **默认路径 P0 修复**：`EmueraMain.StopLegacySession` 不再因 `!backend.IsRunning` 早退；
   Back/Restart/ERB 重启后无条件执行 `StopLegacyBaselineAsync`（幂等），确保
   `GlobalStatic.Reset + ClearCompatibilityPlan` 一定执行，同一进程内第二次启动不再被
   hash 防御拒绝。
6. **launcher 提示**：已选 pack 且有效 profile 非 v24pure 时状态栏明确提示会被拒载回退。
7. **真实数据包夹具**：新增 `tests/xUnitTest/DataOnlyCompatPackFixture`（仅内嵌 manifest、
   无入口类），端到端验证 manifest-only 加载路径。

## 关键决策与 Why

1. **不做"数据包纯 JSON 文件"**：仍坚持"程序集即包"的版本/哈希/分发载体；但允许程序集
   零入口代码，社区只需编译一个带内嵌 manifest 的壳，既降低门槛又不破坏 plan 哈希链。
2. **baseProfileId 而非继续叠加任意 profile**：评审确认"校验基准 v24 + 组装基准所选
   profile"会让同一个包在不同 profile 上静默 no-op/覆盖。v1 明确只允许 v24pure 基座，
   与 P-D 单基线方向一致；高级 profile + pack 组合在 UI 提示、在 host 拒载回退。
3. **capability 激活集中化**：旧实现里 policy 构造点绑在 module.Apply；只要内置模块
   还在场就无法验证"退役为包"的等价性。集中后 capability 是唯一行为开关，
   moduleSelected 仅保留模块语义（IsEnabled/日志/startup 错误日志路径）。
4. **删除静态而非只修复位点**：P0 的根因是投影数据活在 plan 之外；plan 本来已包含包模块
   快照、variant 也进了哈希，提升为具名字段后无需任何跨会话静态，手动复位这类缺陷类型
   整体消失。
5. **数据包函数返回类型由引擎清单解析**：避免包作者手填 returnType 与真实 handler 漂移；
   缺失返回类型元数据时规则层拒载，而不是把 `Unknown` 写进 plan。

## AI 表现复盘

- 有效：先把三条独立评审的 P0/P1 收敛成"数据包 + plan 内聚 + capability 解耦 + 生命周期"
  四个可验证增量；每个增量都配真实测试或 smoke 哨兵，没有只改注释。
- 低效：一次批量文本替换的 slice 边界打到了 Disabled 策略类，导致策略区语法错位，靠
  编译错误定位后整段重建；教训是**跨多个同类 class 的切片必须用唯一锚点或行号范围校验**，
  不能只用方法名首次出现位置。

## 验证记录

- `dotnet test tests/xUnitTest/GEmuera.Core.Tests -c Release`：98/98 通过。
- `dotnet test tests/xUnitTest/EmueraFacade.Tests -c Release`：35/35 通过。
- `dotnet run --project tools/dialect-inventory/LegacyDialectSurfaceSmoke -c Release`：通过
  （含 v24pure + 包 capability 激活 snake/erafl policy flag 哨兵）。
- `dotnet run --project tools/dialect-inventory/LegacyDialectRuntimeSmoke -c Release`：通过。
- `dotnet run --project tools/core-contracts/CoreContractSmoke.csproj -c Release`：通过
  （需 `DOTNET_ROLL_FORWARD=Major`，本机仅装 net10 runtime）。
- `dotnet build gemuera-c#.csproj -c Release -nodeReuse:false -m:1`：0 错误（3 条既有
  AgentLlmMethods nullable 注释警告）。

## 未完成 / 后续任务

- v2 代码贡献接线（IInstructionVariantContribution/IPolicyContribution）与会话持有
  CompatPackSet 的确定性 Unload；当前 v1 仍拒载代码贡献。
- `baseSurfaceHash` 生成/校验端；engine identity/targetEngineApi 与 interpreter descriptor
  统一；ALC allow-list 硬化与契约程序集拆分。
- 默认路径 e2e 真机探针：pack 游戏启动 → Back/Restart → 同游戏/异 profile 再启动。
- P-D 内置模块退役（profile/闭包迁移）；本批只解除 capability 激活的机制阻塞。
