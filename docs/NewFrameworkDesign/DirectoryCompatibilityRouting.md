# 基于目录约定的游戏兼容路由设计

## 状态声明

本文取代原“游戏内容自动识别与兼容计划解析”方案。当前决策为：`Accepted design / Host implementation complete / Android device verification pending`。

正常启动流程不得读取 `GAMEBASE.CSV`、标题、ERB/ERH 锚点或能力特征来猜测游戏身份，也不得维护按游戏代码、目录名片段或脚本内容匹配的自动识别规则。游戏使用哪个兼容 profile，完全由其所在目录显式决定。

当前代码已经注册 `v24pure`、`snake`、`erafl` 三个 profile，以及 `gemuera.v24`、`game.snake`、`game.erafl` 三个内建 module。`FirstWindow` 已实现本文定义的固定深度 `compat/<profile>/<game>` 扫描，并用 `LauncherGameEntry` 将每个游戏路径、profile 与来源一起传给启动边界；`Program` 也不再从 marker 文件改写启动器已经选定的 profile。桌面 `dotnet build "gemuera-c#.sln" --no-restore` 已通过；尚未完成 Android APK/实机验证，不能据此声称移动端发布门禁已通过。

## 目标

- 主界面继续只展示 `v24`、`snake` 和公告，不随兼容模块数量增加按钮。
- eraFL 或未来大幅魔改游戏拥有独立 profile/module，不把专属行为继续堆入 v24。
- 游戏目录位置就是显式配置；启动器不做任何游戏内容自动识别。
- 新增内建 profile 后，目录扫描和主界面代码不需要新增游戏名分支。
- v24 与 Snake 的现有放置方式保持兼容。
- Android 只进行固定深度目录枚举，不在主线程扫描大量 ERB 内容。

## 非目标

- 不根据 `GAMEBASE_GAMECODE`、标题、版本号、ERB label、函数名或资源特征推断 profile。
- 不允许游戏目录提供 DLL、类型名、处理器路径或任意 module 组合。
- 不为 eraFL 或未来 profile 增加新的顶层标签、按钮或独立启动场景。
- 不在脚本已经开始执行后切换 profile/module。
- 不用目录约定替代 `CompatibilityProfileCatalog`、依赖 DAG、typed policy、capability 或 save profile 校验。

## 核心决策

启动器中的“游戏放置分类”和 Core 中的“兼容 profile”是两个不同概念：

| 层级 | 是否展示给普通用户 | 作用 | 当前示例 |
| --- | --- | --- | --- |
| Launcher lane | 是 | 决定扫描入口与默认回退 | `v24`、`snake` |
| Directory route | 否 | 从固定目录结构产生 profile id | `compat/erafl` |
| Compatibility profile | 仅高级诊断可见 | 选择根 module、默认 port/capability/save profile | `erafl` |
| Dialect module | 否 | 实现并隔离游戏专属差异 | `game.erafl` |

因此 eraFL 可以显示在 v24 游戏列表中，同时在内部使用 `erafl` profile：

```text
v24 标签中的 eraFL0.48
  -> LauncherGameEntry.ProfileId = "erafl"
  -> SessionSelection(GameId, "erafl")
  -> erafl profile
  -> game.erafl module
  -> dependency: gemuera.v24
```

“表面归入 v24”不等于“把 eraFL 行为写进 v24”。前者只是启动器展示方式，后者才是需要禁止的核心耦合。

## 目录契约

### 通用结构

```text
emuera/
├─ eraNormalGame/                    -> v24pure
├─ eraAnotherGame/                   -> v24pure
├─ compat/                           -> 保留容器，不是游戏目录
│  ├─ erafl/                         -> profile id = erafl
│  │  └─ eraFL0.48/                  -> erafl
│  └─ future_profile/                -> profile id = future_profile
│     └─ eraFutureGame/              -> future_profile
└─ snake/
   └─ eraTheWorld/                   -> snake
```

固定映射规则：

```text
emuera/<game>                         -> v24pure
emuera/snake/<game>                   -> snake
emuera/compat/<profile-id>/<game>     -> <profile-id>
```

`compat` 是唯一新增的保留目录。profile id 必须是小写稳定标识，并通过 Core 标识符校验与内建 profile allowlist。目录不能直接声明 module、capability、port、save profile 或版本范围；这些内容仍由受信任的 profile 定义拥有。

### Android 示例

```text
/storage/emulated/0/emuera/eraNormalGame/
/storage/emulated/0/emuera/snake/eraTheWorld/
/storage/emulated/0/emuera/compat/erafl/eraFL0.48/
```

### 桌面示例

```text
<游戏库>/eraNormalGame/
<游戏库>/snake/eraTheWorld/
<游戏库>/compat/erafl/eraFL0.48/
```

同一个游戏移动到不同路由目录后，下一次启动使用新目录对应的 profile。这是明确配置变化，不需要迁移缓存或重新识别内容。

## 启动器表现

主界面仍保持：

```text
[v24] [snake] [公告]
```

### v24 标签

合并展示两类条目：

1. 普通根目录中的游戏，profile 为 `v24pure`。
2. `compat/<profile-id>/` 中的游戏，profile 为对应 profile id。

`compat` 和 profile 容器本身不显示为游戏，也不生成额外标签。正常列表只显示最终游戏目录名；发生同名冲突时才补充安全的相对位置说明。

### snake 标签

继续只展示 `snake/<game>`，默认 profile 为 `snake`。本设计不改变 Snake 的现有目录和入口，以免破坏已有用户放置习惯。

### 高级兼容模式

目录路由是正常启动的权威来源，不要求用户选择。高级兼容模式只作为诊断或恢复入口：

- 默认关闭。
- 显示当前条目的目录来源与最终 profile。
- 当前实现的手动选择是明确的“本次启动”覆盖入口，但所选值仍随启动器设置保存；将其收敛成单游戏覆盖是后续 R4 工作，不能把现状误写成已完成的 per-game override。
- 手动覆盖不得增加顶层按钮，也不得绕过 profile allowlist 和计划构建校验。

正常优先级为：

```text
用户显式开启的当前游戏/会话覆盖
  > compat 目录路由
  > v24 或 snake lane 默认值
```

不存在内容探测、置信度、自动 fallback rule 或后台重新判定。

## 启动器数据模型

扫描阶段必须生成包含路径与 profile 的不可变条目，不能继续只把字符串路径塞进列表后再从当前标签猜 profile。

```csharp
public enum LauncherGameSource
{
    V24Root,
    SnakeRoot,
    CompatibilityDirectory,
}

public sealed record LauncherGameEntry(
    string DisplayName,
    string GameRoot,
    string ProfileId,
    LauncherGameSource Source);
```

Godot `ItemList` 只负责展示与选中；条目数据由 `FirstWindow` 的 host-side 集合拥有。点击开始时取回完整 `LauncherGameEntry`，将 `GameRoot` 与 `ProfileId` 一起交给启动边界。

不要让 UI 文本、按钮名称或当前可见标签成为 profile 的唯一数据源。

## 固定深度扫描算法

扫描必须是有界、确定且不读取游戏内容的目录枚举。

### v24 lane

1. 枚举游戏库根目录的直接子目录。
2. 跳过保留目录 `snake` 与 `compat`。
3. 对直接子目录执行现有“可用 era 游戏目录”结构检查，成功则产生 `v24pure` 条目。
4. 打开根目录下的 `compat`，只枚举其第一层 profile 目录。
5. 将 profile 目录名规范化并交给 `CompatibilityProfileCatalog.TryResolve`。
6. profile 不存在或不允许目录选择时，记录稳定错误并跳过其中游戏，不能按 v24 启动。
7. 对有效 profile 目录只枚举其直接游戏子目录，产生携带该 profile id 的条目。

### snake lane

1. 定位每个游戏库根目录下的 `snake`。
2. 只枚举其直接游戏子目录。
3. 产生 `snake` 条目。

### 约束

- 不递归搜索任意深度。
- 不进入游戏目录扫描 ERB/CSV 来决定 profile。
- profile 目录数量、每个 profile 的游戏数量和总条目数量必须有硬上限。
- 多个扫描根命中同一规范化绝对路径时只保留一个条目。
- Windows 大小写不敏感与 Android 大小写敏感环境必须得到相同 profile 结果；规范要求目录 id 使用小写。
- `v24pure` 与 `snake` 是 launcher lane 保留 profile，不应通过 `compat/v24pure` 或 `compat/snake` 重复暴露。

## Profile 与模块解析

目录只产生一个 profile id。真正的 module 组合由 Core catalog 决定：

```text
compat/erafl/...
  -> profile id: erafl
  -> CompatibilityProfileCatalog.Resolve("erafl")
  -> root module: game.erafl
  -> DialectModuleCatalog resolves dependency closure
  -> gemuera.v24 + game.erafl
  -> frozen CompatibilityPlan
```

游戏路径不能直接产生：

- module 数组；
- typed policy 值；
- capability id；
- save profile；
- C# 类型或程序集路径。

这保证目录约定只负责“选择哪个受信任 profile”，不会变成游戏包注入解释器行为的通道。

## 所有权边界

### `FirstWindow` / Godot Host

- 拥有游戏库根路径和固定深度目录枚举。
- 产生 `LauncherGameEntry`。
- 展示条目并把路径/profile 一起交给会话启动边界。
- 显示未知 profile、目录越界和权限失败等错误。
- 不读取 GAMEBASE、ERB 或资源内容来识别游戏。

### `CompatibilityProfileCatalog`

- 拥有受信任 profile allowlist。
- 把 profile id 映射到根 module、默认 port/capability 和 save profile。
- 在首个会话前冻结，拒绝重复或未知 profile。
- 不接触 Godot 节点或游戏绝对路径。

### 游戏兼容模块

- `game.erafl` 只拥有 eraFL 特有 quirk 和默认组合。
- `game.snake` 只拥有 Snake/TW 特有差异。
- 多游戏可复用的行为应继续提取为 capability 或窄 typed policy。
- Parser、VM、Resource、Save 和 View 消费冻结计划，不根据目录名或游戏名自行分支。

### `SessionCoordinator`

- 接收已经确定的 `SessionSelection`。
- 验证 profile/module/capability/save profile 并构建候选计划。
- 只提交当前 generation 的候选会话。
- 会话开始后不允许根据后续脚本内容改换 profile。

## 错误策略

| 场景 | 行为 |
| --- | --- |
| `compat` 不存在 | 正常，只显示普通 v24 游戏 |
| `compat/erafl` 且 profile 已注册 | 游戏条目使用 `erafl` |
| `compat/unknown` | 显示“不支持的兼容目录 unknown”，不把其中游戏当 v24 |
| profile 目录大小写不规范 | 显示目录命名错误，不做跨平台含糊匹配 |
| profile 目录中没有有效游戏 | 忽略空目录，可在诊断中记录 |
| 游戏同时通过多个库根被发现 | 按规范化绝对路径去重 |
| `compat/<profile>` 下继续嵌套多层 | 不递归寻找，提示目录层级不符合约定 |
| profile 已注册但计划依赖/能力不满足 | 候选会话失败并报告真实缺项，不回退 v24 |
| 外部存储权限不足 | 保持启动器可用并显示路径级错误，不启动错误 profile |

未知、拼写错误或不完整 profile 必须 fail closed。静默回退 v24 会让游戏以错误语义继续运行，产生比启动失败更难定位的脚本或存档问题。

## Android 与性能边界

- Android 首要路径为 `/storage/emulated/0/emuera/`。
- 扫描仅包含根目录、`snake`、`compat`、profile 目录和直接游戏子目录，深度固定。
- 不建立 ERB 内容摘要，不读取脚本文本，不维护识别缓存。
- 目录枚举只在启动器扫描/刷新时执行，不进入 `_Process` 热路径。
- UI 更新在 Godot 主线程完成；如果外部存储枚举在目标设备上出现明显延迟，再把纯路径枚举移到有界 worker，不提前引入复杂异步缓存。
- 诊断只记录截断后的逻辑路径、profile id、条目数量和稳定错误码，不记录完整用户目录或脚本文本。

与自动识别相比，本方案删除了 GAMEBASE 解码、固定锚点读取、能力扫描、置信度计算、内容 hash 和缓存失效成本。

## 安全限制

- `compat` 下的第一层名称只解释为 profile id，不解释为文件路径、URL、程序集或类型名。
- profile 必须通过标识符语法校验并存在于冻结 catalog。
- 禁止 `..`、绝对路径、设备路径和越界链接进入逻辑相对路径。
- 禁止游戏目录通过文件名要求任意 DLL、module、capability 或 policy。
- 路径中的 profile 只选择预编译模块；Android/iOS 不进行运行时程序集加载。
- profile 目录不能覆盖 `v24pure`、`snake` 等保留 lane 语义。

## 实施切片

| 切片 | 交付 | 验证 | 回退 |
| --- | --- | --- | --- |
| R0 文档与契约 | 已完成：冻结目录结构、保留名称、错误策略与条目模型 | 文档守卫、路径样例评审 | 仅文档，无运行时影响 |
| R1 Host 条目模型 | 已完成：`LauncherGameEntry` 让列表选择携带 path/profile/source | 桌面构建通过；实际目录矩阵待启动器运行验证 | 恢复字符串路径元数据 |
| R2 固定深度扫描 | 已完成：v24 合并普通与 compat 游戏，snake 保持独立固定目录 | 桌面构建通过；未知 profile、大小写、去重待运行时矩阵 | 关闭 compat 根扫描 |
| R3 会话接线 | 已完成当前 legacy 边界：选择条目后 profile 传入既有启动链，marker 不再改写选择 | 桌面构建通过；v24/Snake/eraFL 实际启动和 plan hash 待验证 | 只保留 v24/snake lane |
| R4 高级诊断 | 部分完成：显示来源/profile；手动覆盖仍是持久化启动器设置，尚未收敛为单游戏 override | 多游戏切换不串 profile | 关闭高级覆盖 |
| R5 Android 门禁 | 未完成：APK、外部存储权限、首次扫描、重启与路径移动验证 | Android 真机报告 | 发布版关闭 compat 扫描 |

不得在 R1-R3 期间重新引入 `GameContentProbe`、GAMEBASE 代码映射、ERB 锚点或 `if (path.Contains("erafl"))`。profile 必须来自通用的 `compat/<profile-id>` 目录槽位。

## 验证矩阵

| 场景 | 预期 |
| --- | --- |
| `emuera/eraNormal` | 出现在 v24 标签，profile=`v24pure` |
| `emuera/snake/eraTW` | 只出现在 snake 标签，profile=`snake` |
| `emuera/compat/erafl/eraFL0.48` | 出现在 v24 标签，无 eraFL 顶层按钮，profile=`erafl` |
| 新注册 `future_profile` 并放入对应目录 | 不修改启动器按钮或游戏名分支即可使用该 profile |
| `compat/unknown/eraGame` | 不启动，显示未知 profile 错误 |
| `compat/EraFl/eraGame` | 按小写目录契约报错，Windows/Android 结果一致 |
| `compat/erafl/group/eraGame` | 不递归命中，提示层级错误 |
| 同名普通游戏与 compat 游戏 | 两者都可区分选择，内部 profile 不串用 |
| 从 `compat/erafl` 移到普通根 | 下一次扫描后使用 `v24pure`，不保留隐藏识别结果 |
| 快速切换 v24/Snake/eraFL 条目 | 每次启动使用所选条目携带的 profile，旧 generation 不污染新会话 |
| 游戏内容修改但目录不变 | profile 不变；不触发扫描、hash 或规则重新判定 |
| Android 无 `compat` 目录 | 正常显示现有 v24/snake 游戏，无报错 |
| Android 外部存储拒绝访问 | 显示权限错误，不崩溃、不错误回退 profile |

## 完成定义

以下条件全部满足后，目录路由才能从“Host implementation complete”提升为已通过完整发布门禁：

- `compat/<profile-id>/<game>` 的固定深度扫描已实现，没有内容探测调用。
- v24、Snake 与 eraFL 三类条目携带正确且彼此隔离的 profile。
- 新增一个测试 profile 时不需要修改启动器按钮、标签或按游戏名称分支。
- 未知/非法 profile、目录层级错误和计划构建失败均 fail closed。
- 直接根目录与 `snake` 的现有行为有回归证据。
- Core profile/module/port/capability/save profile 校验仍由冻结计划负责。
- 桌面构建、路径矩阵和 Godot 启动器验证通过。
- Android APK 导出、外部存储权限、真机首次扫描和重启验证有可复查记录。
- `CODE_MAP.md`、`CompatibilityMatrix.md`、`KnownLimitations.md` 与实际实现状态一致。

## 被取代方案

以下方案不进入正常启动路径：

- GAMEBASE 游戏代码与标题识别；
- ERB/ERH 固定锚点匹配；
- 全目录能力扫描；
- 置信度与冲突评分；
- 内容 hash、识别缓存和自动失效；
- 为每个游戏增加独立启动按钮；
- 在普通 v24 代码中继续堆叠游戏名判断。

保留在仓库中的 `GameContentProbe` 或 `GameCompatibilityResolver` 不构成本方案的依赖。是否删除这些未接入代码，应作为独立清理任务评审，不能在目录路由实现中顺手扩大范围。

## 与其他设计的关系

- [DialectExtensionSystem](DialectExtensionSystem.md) 定义 module、profile、typed policy、capability 和冻结计划；本文只定义启动器如何显式选择 profile。
- [Architecture](Architecture.md) 定义 `GameSession`、generation、候选提交和纯 Core 边界。
- [GodotIntegration](GodotIntegration.md) 定义 Host I/O、Godot 主线程和平台组合方式。
- [SecurityLimits](SecurityLimits.md) 定义不可信路径、标识符、资源预算和外部代码限制。
- [VerificationPlan](VerificationPlan.md) 与 [AcceptanceTraceability](AcceptanceTraceability.md) 负责桌面、Android、fixture 与发布证据。
- [CompatibilityMatrix](CompatibilityMatrix.md) 记录 profile/module 的实际完成状态；本文不能提升 eraFL 或 Snake 的兼容结论。
