# 生命周期、资源引用与内存账本

## 生命周期矩阵

| 对象 | 创建 | 所有者 | 销毁/重置 | 跨游戏 |
| --- | --- | --- | --- | --- |
| AppBootstrap/PlatformGateway | Godot boot | SceneTree root | app exit | 是 |
| SessionCoordinator | boot | AppBootstrap | app exit | 是，只保留当前引用 |
| GameSession | candidate load | SessionCoordinator | switch/dispose | 否 |
| VM/Variables/Config/Save/ResourceCatalog | Session 构造 | GameSession | Session dispose | 否 |
| DisplayHistory/InputCoordinator | Session 构造 | GameSession | cancel/clear | 否 |
| Bridge nodes | Main scene | MainOrchestrator | scene exit | 节点可保留，state 不保留 |
| PixelStore/decoded CPU image | demand decode/G create | GameSession | G dispose/ref=0+驱逐/session dispose | 否 |
| GPU texture/RID | main-thread materialize | ResourceBridge | ref=0/evict/session detach | 否 |
| AudioStream/player voice | AudioBridge | pool/current track | stop/release/generation reset | 节点池可保留 |
| SaveSnapshot | save safe point | SaveOperation | completion/fault | 否 |

## Godot 对象规则

Node 只在 SceneTree/主线程；RefCounted/Resource 引用计数不表示所有资源线程安全。RID 手动 free，且强持有其依赖 Resource。动态 signal 特别是捕获 lambda 在 `_ExitTree` 显式断开。Tween 绑定节点并在退出时 kill；不对即将 queue_free 的节点留下 continuation。

Core 普通 IDisposable/IAsyncDisposable 按所有权释放；finalizer 不承担业务清理。Dispose 幂等，取消 token 后观察任务，避免 unobserved exception。

## 内存账本

| 类别 | 估算/观测 | 避免重复计量 |
| --- | --- | --- |
| managed arrays/strings/AST | GC/自有 size estimator | shared immutable segment 按 owner 计一次 |
| CPU PixelStore/source image | reservation token + width×height×format | source compressed bytes另计 |
| GPU texture | format+mips+layers 估算/monitor | Atlas region 不重复算整张；只记引用 |
| derived crop/texture | cache ledger actual allocation | 与 source image 分开 |
| animation | frame descriptor + unique texture refs | 同图帧只引用 |
| audio compressed/PCM | file bytes + sample rate×channels×duration | streaming buffer 单列 |
| Godot Node/Resource/RID | object/monitor/registry counts | bridge registry 是权威 |
| save snapshot | retained segments + copied buffers | 与 VariableStore 共享段标记 shared |

`GC.GetTotalMemory` 仅 managed heap；不能替代进程 RSS、native、GPU 或系统内存压力。

## 引用关系

DisplayPart 只持 ResourceKey/PixelHandle，不直接持 Texture。PixelStore CPU surface 是脚本真相；ConsoleBackend/CBG 只持某 revision 的 GPU handle。cache 自己的一份引用可被 LRU 移除，但脚本 G slot、Display/CBG strong count >0 时不释放对应 CPU/GPU 对象。

派生 texture/atlas/crop 也登记 key、source、bytes、strong refs、last use、generation。Session detach 先让 View/CBG/Audio 释放 handle，再驱逐 cache。

## 会话清理顺序

1. SessionCoordinator 标记旧 generation 非当前，拒绝新操作。
2. 取消 InputRequest、save/import/decode/SQL CTS；停止接收 trusted plugin 调用。
3. MainOrchestrator detach View，停止消费旧 effect。
4. AudioBridge 取消 tween/停止 voice/释放 stream handle。
5. ConsoleBackend 释放 visible layout/texture/RID/selection。
6. ResourceBridge drain/丢弃旧 completion，释放 cache。
7. SaveService 结束临界 replace 或清 temp/journal。
8. VM thread join/observe；RuntimeDataStore/PixelStore/Display/Variables/Config Dispose。

迟到 completion 先比较 generation，随后只做 payload cleanup。

## 线程安全

SceneTree/Godot Node 操作主线程。后台集合除非独占/不可变，不共享；Array/List resize 受 lock 或 message passing。SaveSnapshot 和 decode bytes 是不可变所有权转移。不要用 `call_deferred` 修复模糊初始化顺序。

所有大对象/任务持 `MemoryReservation`；多任务并发总量由 Session budget 决定。reservation 泄漏与内存泄漏同等失败。外部 DLL 在同进程可能保留任意静态引用，因此 trusted plugin 模式不能承诺完全卸载；切换后若插件已加载，最安全策略是提示重启进程。

## 压力与泄漏测试

- 连续切换 100 次，managed/native/GPU/Node/task 数回到稳定区间。
- 100k 行滚动、裁剪、Theme/字体切换后 layout cache 可回收。
- 资源导入取消/失败不遗留临时文件、Image、Texture、RID。
- 快速换 BGM 不增长 player/tween/task。
- 保存 snapshot 反复创建/取消无 retained segment 泄漏。
- debug orphan/RID registry/handle ledger before-after diff；release 另用平台内存工具。

预算和退化见[PerformanceOptimization](PerformanceOptimization.md)，硬上限见[SecurityLimits](SecurityLimits.md)。
