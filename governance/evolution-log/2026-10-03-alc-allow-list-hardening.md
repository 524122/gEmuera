# 2026-10-03 CompatPack ALC 允许清单硬化（宿主内部程序集对包不可见）

## 任务目标与成果

一句话：把 CompatPack 包加载上下文（`CompatPackLoadContext`）的绑定规则从
「约定」升级为「机制」——允许清单（Emuera 契约程序集 + BCL + 包目录依赖）之外的
任何绑定一律 `FileLoadException` fail-closed 拒载，删除 `GEmuera.Core` 显式分支与
"返回 null 落回默认解析"的泄露通道。对应设计文档 §5.2/§12.6 与 v1.1 增补 §13.9
的遗留项"ALC allow-list 硬化（GEmuera.Core/默认解析回落）"。

背景（v1.1 时点的漏洞形态）：`CompatPackLoadContext.Load` 对 `GEmuera.Core` 显式返回
宿主 Core 实例、对其余未匹配名返回 null 让默认 ALC 接手——默认解析可见宿主全部已加载
程序集，包只要引用宿主内部类型即可完成绑定，信任边界 §6 的"可见面"只是约定。

## 本批落地

1. **ALC 硬化**（`src/Core/Compatibility/Packs/CompatPackLoadContext.cs`）：
   - 删除 `coreAssembly` 字段与 `GEmuera.Core` 分支；
   - 保留规则 1（`Emuera`/`emuera` → 契约程序集）、规则 2（`netstandard`/`System.*`/
     `Microsoft.*` → 默认 ALC 已加载实例，未命中 null 交还默认解析——BCL 名字无泄露面）、
     规则 3（包目录 `<name>.dll` → 仅本 ALC 内加载）语义不变；
   - 规则 4：仍未命中即抛 `FileLoadException`，消息指名被拒绝的绑定程序集、允许清单
     与"宿主内部程序集（如 GEmuera.Core）对包不可见"的指引。拒载路径 `Unload` 干净
     （入口实例化失败 → `ResolvePackEntry` 返回 null → `TryLoad` 回收 ALC，无半加载句柄）。
2. **loader 诊断增强**（`CompatPackLoader.DescribeException`，internal）：
   `Activator.CreateInstance` 把构造器异常包进 `TargetInvocationException`（通用文案），
   拒载错误现在保留最内层异常类型与消息，FileLoadException 的归因信息不再丢失。
   两处"包入口实例化失败"文案前缀不变，仅增补 payload。
3. **TDD 证据**（新增 `tests/xUnitTest/GEmuera.Core.Tests/CompatPackAlcIsolationTests.cs`）：
   - RED：`Alc_HostBindingProbePack_IsRejected` 修复前失败，失败消息为
     "探针在硬化前被成功加载（宿主内部程序集经 ALC 回落泄露给包）"——
     探针（合法 `ICompatPack` 入口，构造器 `typeof(CompatibilityPlan)` 制造
     `GEmuera.Core` 绑定需求）经 `TryLoad` 完整加载成功，漏洞实证；
   - GREEN：硬化后同探针在入口实例化期被拒载，`false` + `handle null` + 错误含
     `GEmuera.Core`/`FileLoadException` 归因；
   - 正向对照：`Alc_ContractOnlyFixture_Loads`——契约夹具（只引用契约程序集）加载成功，
     表面贡献按唯一 `ContributionId` 断言（跨 ALC 类型不统一纪律），并经
     `CompatPackPlanAssembler.TryAssemble` 折叠进 v24pure 基线（`SETANIMETIMER` 落位、
     ModuleId 归属、plan 哈希变化）。
4. **正向加载样本切换**（`CompatPackLoaderTests`）：`TryLoad_ContractOnlyFixture_*`、
   `TryLoad_SamePack_Twice_HashesDeterministic`、`TryLoadSet_SinglePack_Succeeds`、
   `TryLoadSet_DuplicatePackId_RejectsWholeSet` 改用 Task 1 契约夹具；本测试程序集
   保留为负向样本（拒载向用例在规则段被拒，不依赖绑定回落）。
5. **文档与实测记录**：`docs/designs/compat-pack-interface.md` §5.2 改写为四条机制的
   允许清单并记录实测；§12.6 勘误增补"回落已硬化为机制"；§13.9 该项标注完成
   （跨契约程序集拆分仍开放）。

## 关键决策与 Why

1. **探针用部署形态加载而非 bin 目录直载**：探针工程的 bin 输出目录天然含
   `GEmuera.Core.dll`（ProjectReference 依赖流），规则 3（包目录探测）会把它按
   "包自带依赖"加载为 ALC 私有副本——类型不与宿主统一、无宿主内部静态泄露，
   属规则 3 的合法语义而非漏洞。因此拒载用例把探针包文件单独复制进空临时目录
   （真实部署的包目录形态）再 `TryLoad`，直接钉住"默认解析回落"这一漏洞本身。
   RED 阶段两条证据互补：bin 直载证明规则 3 语义（加载成功）、空目录直载证明
   回落漏洞（加载成功=漏洞）；硬化后空目录形态被 FileLoadException 拒载。
2. **包目录规则 3 保持不变**：包作者显式携带的依赖程序集（含宿主程序集的副本）在
   包 ALC 内加载为私有实例，这是依赖隔离的正道；硬化只封"宿主实例经默认解析可见"
   的通道。Core 数据类型的合法获取路径 = 契约程序集暴露或另签契约（fail-closed）。
3. **绑定需求制造点放在实例构造器**：JIT 构造器解析 TypeRef `GEmuera.Core` 时即触发
   绑定，失败发生在 `TryLoad` 内的 `Activator.CreateInstance`（入口实例化段），
   不留半加载句柄；不用静态字段初始化（避免 `TypeInitializationException` 包裹
   改变异常形态），不用 Contributions getter（加载器对它的异常已单独兜底，
   语义上更适合首跳失败的是构造器）。
4. **测试清理对抗 ALC 文件锁**：拒载路径的 ALC 已 `Unload` 但收集由 GC 驱动，
   程序集文件映射短暂滞留——删除前 `GC.Collect` + 有限重试，测试不留临时目录。

## AI 表现复盘

- 有效：先按 brief 直载 bin 目录探针跑 RED（拿到"探针加载成功"的漏洞实证），
  硬化后发现探针仍加载成功，没有硬凑断言，而是顺着规则 3 的语义定位到 bin 目录
  依赖流这一合法路径，改用部署形态加载让测试直接命中目标漏洞——缺陷归因准确，
  未把合法语义误当漏洞封死。
- 低效：`Activator.CreateInstance` 包裹 `TargetInvocationException` 这一点是跑出
  "Filter not matched" 后才确认的，前置调研时可预判（`MethodBase.Invoke` 语义），
  一次往返本可省掉。

## 验证记录

- `dotnet test tests/xUnitTest/GEmuera.Core.Tests/GEmuera.Core.Tests.csproj -c Release`：
  103/103 通过（101 + 2 新增 ALC 隔离用例）。
- `dotnet test tests/xUnitTest/EmueraFacade.Tests/EmueraFacade.Tests.csproj -c Release`：
  35/35 通过。
- `dotnet run --project tools/dialect-inventory/LegacyDialectSurfaceSmoke -c Release`：
  通过。
- `dotnet run --project tools/dialect-inventory/LegacyDialectRuntimeSmoke -c Release`：
  通过。
- `DOTNET_ROLL_FORWARD=Major dotnet run --project tools/core-contracts/CoreContractSmoke.csproj
  -c Release`：通过。
- `dotnet build gemuera-c#.csproj -c Release -nodeReuse:false -m:1`：0 错误，
  `.godot/mono/temp/bin/{Debug,Release}/GEmuera.Core.dll` 时间戳更新。

## 未完成 / 后续任务

- 跨契约程序集拆分（§13.9 同条后半，未在本批范围）。
- v2 代码贡献接线时复核：`IInstructionVariantContribution`/`IPolicyContribution`
  的工厂桥若需引用宿主类型，必须走契约程序集（本硬化已封死 ALC 回落通道）。
- ALC 生命周期仍为"成功路径保持到进程结束"（§12.6 前半，宿主接线深化时处理）。
