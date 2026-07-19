# M5 平台组合、Typed Ports 与移动兼容

## 文档地位

M5 在 M3 Core 和 M4 资源所有权通过后，把旧 `GenericUtils`、`EmueraContent`、文件选择、输入、音频、SQLite 和应用生命周期包装为 typed ports。M5 保留专用 `VmHostThread`，不把平台 I/O 搬到 Godot `_Process`，也不把 Android 新路径直接替换 M0-M2 已有路径。

M5 的核心判断是“平台能力组合”而不是“平台分支”。erafl、Snake 和其他魔改游戏请求的是可审计的 `CapabilityId`；`PlatformGateway` 只绑定平台实现，不改变 Parser/VM 的语言注册表。

## 进入条件与 work packages

M5 进入评审前：M0-M4 的前置报告可追溯，M4 的 PixelStore/资源 generation guard 已证明不会把旧纹理写入新 Session。M5 work packages：

| ID | 内容 | 关键边界 |
| --- | --- | --- |
| M5-PORT-01 | typed port contracts | Core 只见 token、stream、result、fault |
| M5-PORT-02 | Desktop/Android/iOS adapter | 平台实现不保存游戏业务 state |
| M5-PORT-03 | 输入与虚拟光标 | normalized action + legacy pointer semantics |
| M5-PORT-04 | storage/SAF canary | 候选导入、原子提交、旧路径回退 |
| M5-PORT-05 | SQLite/Map/XML/DT | 参数化、reader/transaction 生命周期 |
| M5-PORT-06 | lifecycle/audio capability | pause/resume、background、audio generation reset |
| M5-PORT-07 | erafl capability pack | div/map/pointer/sprite/markup 组合不依赖游戏名 |

## Typed port 规则

Core port 必须是窄接口，输入/输出使用不可变 DTO 或专用值类型：

```text
Core request(operationId, generation, capability, bounded arguments)
  -> PlatformGateway adapter
  -> typed completion(operationId, generation, result|fault)
```

禁止：

- Core 接收 `Node`、`Control`、`Godot Resource`、Android `Activity` 或绝对路径。
- `GenericUtils` 静态方法继续作为新 Core 的隐式全局入口。
- 端口内部把异常吞掉后伪造成功、把异步结果写进另一个 generation，或把 `Dictionary<string, object>` 当万能扩展 API。
- 主线程在文件/SQLite/解码/压缩上做无界同步调用。

每个 port descriptor 记录 capability id、版本、线程 owner、完成模式、最大输入/输出、取消语义、错误映射和是否影响脚本可观察时序。端口完成必须回到 VM owner thread，再推进 continuation；Bridge 不能直接修改 VariableStore、DisplayHistory 或 CompatibilityPlan。

## 平台适配分层

```text
AppBootstrap (autoload)
  -> PlatformGateway (autoload, capability query + lifecycle)
      -> Session-scoped port factory
          -> Desktop / Android / iOS adapter
              -> Core WaitPort / CoreImmediate completion
```

Autoload 只拥有应用级 platform token、权限句柄和生命周期事件；游戏目录、存档 slot、资源 cache、SQLite reader、输入 request 和 audio state 仍归 `GameSession`。`PlatformGateway._init()` 不访问其他 Autoload；在 Main 显式 `InitializeAsync` 后才允许创建端口。

节点规则：

1. `PlatformGateway` 可为 Autoload，但不能创建 `GameSession` 子节点或持有 Node 的业务引用。
2. `SessionBridge` 在 commit 后 attach session-scoped adapters；`_ExitTree` 先拒绝新 completion，再取消并 drain，最后释放 platform handles。
3. `InputPanel`、`FilePickerOverlay` 和 `AudioBridge` 是 View/Bridge Node，只通过 Variant-safe signal 发送 request id、generation 和简单值。
4. 原生 callback 必须转换为不可变 completion 并排回主线程；不能在 Android/iOS callback 中直接调用 Core 或 SceneTree。
5. Tween、signal lambda、`Callable`、reader/stream、security-scoped access 在退出路径显式取消/断开；不得把 Node 引用留在跨会话队列。

## 输入、等待与 erafl

输入由 `InputCoordinator` 拥有请求状态，由平台 adapter 产生 normalized actions。键盘、控制器、触摸、VirtualCursor 和 MOUSEBUTTON 不能在 View 中各自完成脚本语义。

| 兼容点 | port/策略 | 约束 |
| --- | --- | --- |
| INPUT/TINPUT/ONEINPUT | `IInputPort` + `InputRequest` | 唯一 request id；重复/过期提交拒绝 |
| NF/NoFocus | `input.nofocus.v1` capability | focus policy 进入 plan/hash |
| 空白区域整数输入 `-1` | `IPointerInputSubmissionPolicy` | 结果进入 Core trace，不由 Control 猜测 |
| VirtualCursor | platform input capability | 保存逻辑坐标/按钮状态，不把 Node 当 cursor 真相 |
| skip/timeout/UPCHECK | InputCoordinator + VM clock | completion 顺序按旧 trace 保持 |
| Android touch/gesture | normalized action | 不用 mouse event 冒充 multi-touch；触摸目标至少 48dp/44pt |

eraFL 的 `INPUTS ,1` 省略默认字符串、空白右/中键、动态地图范围和 data-only refresh 必须分别记录 capability/BehaviorKey；一个平台可实现 capability 不等于自动启用对应方言。无 FL fixture 的 policy 保持 `Uncovered`。

## Android 存储迁移

M0-M2 保持旧外部目录/权限路径。M5 才能启动 SAF/import-cache canary，且旧路径继续可回退：

```text
user picker content:// token
  -> persisted read permission
  -> stream copy to user://imports/<generation>.tmp
  -> hash/size/path/security validation
  -> atomic rename to content-addressed cache
  -> candidate ResourceCatalog/Save root
  -> short session commit
```

复制过程中必须持有 `MemoryReservation`、临时文件 token 和 journal。权限撤销、后台、进程杀死、空间不足或 hash 变化只销毁 candidate；不得破坏当前已提交游戏。恢复后重新验证 persisted permission，旧 Stream 不可复用。

SAF canary 的结果不能只由桌面测试推导。最终 APK、目标 SDK、Android ABI/native SQLite library、低/中档真机、导入/导出/存档、恢复和回退报告全部通过后，才允许提高 `platform.saf` 流量；任何未跑设备标 `Uncovered`。

## SQLite 与扩展运行时

`IDatabasePort` 接收受控 database token、参数化 SQL、reader/transaction id 和 bounded result request。连接、reader、transaction 归 `GameSession.DatabaseService`，session dispose 强制 close/rollback。SQL 文本只在诊断记录 hash、operation 和错误码。

Map/XML/DataTable 的内存对象、类型、枚举顺序、SAVE/GLOBAL/STATIC 域由 `ExtendedData` 持有。外部实体、DTD、网络和路径拼接默认拒绝。大 select/merge 在 VM thread bounded 执行或明确 `WaitPort`；不能在 M6 主线程实验前留下未审计同步路径。

外部 DLL 仍然：Android/iOS `Unsupported`，桌面默认 `Disabled`，逐 hash trusted desktop 才能加载且建议重启进程撤销。typed compatibility module 不能借此加载游戏提供的 C# 或反射类型。

## 音频与应用生命周期

`AudioBridge` 消费逻辑 AudioEffect，不知道脚本指令名。M5 默认 `audio.compat_mode=true`：不自动 crossfade、voice stealing 或隐式等待，逐项 completion 由 fixture 裁决。BGM player/voice pool 可以跨场景保留 Node，但必须在 generation commit/detach 时清空旧 stream、tween 和 pending command。

暂停/恢复流程：

1. `PlatformGateway` 发布应用生命周期事件。
2. SessionCoordinator 标记当前 generation 的可暂停/不可暂停操作。
3. VM thread 根据端口合同停止接收新非恢复请求；已提交 Display/Save journal 保持顺序。
4. Android 进程恢复重新验证 content URI、DB、audio stream 和 input focus。
5. 恢复失败只报告 typed fault 或停在兼容选择，不提交半候选。

## 内存与原生资源

M5 额外计量 platform/native 资产：SAF copy buffer、SQLite page/WAL、input event queue、audio PCM/stream buffer、Android JNI/global refs、file descriptors、security scope 和 Node/Callable 数量。每个 port operation 有 reservation 和 max concurrency；取消必须等待/观察底层任务，不能只丢弃 Task 引用。

平台句柄不是 CLR 引用计数对象：`SafeHandle`/token 的 owner、close、revocation 和 generation 必须写入 ledger。Node `_ExitTree` 不代表原生权限自动释放；adapter 必须在 `finally` 停止 scope、close stream/reader、解除 callback。

## Port manifest 与 Godot 组合验收

M5-PORT-01 交付的 port manifest 是后续六个包的唯一接口目录，字段、报告和 gate 规则见 [M3M7EngineeringExecution](M3M7EngineeringExecution.md)。每条记录必须有稳定 portTypeId、capability、VM owner、最大载荷、取消/超时、completion mode、错误码和回退 adapter；未登记的 port 不能以 GenericUtils helper、静态单例或 Dictionary object 形式绕过该目录。

M5-PORT-02 建立 session-scoped adapter factory 后，M5-PORT-03 到 M5-PORT-06 可按独立 port 分包，但都必须由同一个 completion dispatcher 回到 VM owner thread。M5-PORT-07 只组合已验证 capability 与 fixture，不得把 eraFL 名称当作隐式开关。

Godot 一侧遵循单向组合：MainOrchestrator 向下调用受注入的 Bridge component；component 以 Godot signal 或 typed completion 向上报告；兄弟节点不直接互调。InputPanel、FilePickerOverlay、AudioBridge 的 signal 仅传 requestId、generation 和 Variant-safe 简单值，业务状态留在 GameSession。任何动态 signal、Callable、Tween、stream 或原生 callback 都在 detach 路径显式解除并计入 handle ledger。

## M5 门禁与回退

M5 通过必须有：

1. Core architecture test 证明新路径无 `GenericUtils`/Godot/platform concrete type 泄漏。
2. Desktop 与 Android 输入、文件、SQLite、生命周期、音频和 VirtualCursor 的旧/新 trace 差分。
3. SAF canary 导入、撤权、恢复、低空间、进程终止、原子提交和旧路径回退的最终 APK 真机报告。
4. eraFL capability pack 在 parser/VM/input/display/resource 各边界的两侧 fixture；未覆盖行为保持 `Uncovered`。
5. 端口取消、stale completion、session switch 和 `_ExitTree` 后没有任务、句柄、reader、Node、stream、reservation 增长。
6. `ports.typed=false` 与 `platform.saf=false` 能回到旧 adapters，且 M0 canonical 结果不变。

M5 初始状态为 `executionStatus=NotStarted; gateStatus=Blocked; blockerCode=PreviousGate:M4`。Android 未通过不能因 Desktop 通过而放行移动发布。

## 并行协作边界

M5 设计可与另一 AI 的 M0-M2 runner、fixture、DTO 和静态方言库存并行，但不修改其 report schema、旧输入路径或 baseline flags。若 M0-M2 改动改变了 port 输入事实，必须在 M5 开工前重新生成 source/runtime identity 和差分 fixture，不直接手工改结论。
