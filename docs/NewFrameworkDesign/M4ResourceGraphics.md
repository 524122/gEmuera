# M4 资源、PixelStore 与图形投影

## 文档地位

M4 依赖 M3 Core 合同，但不要求一次性替换旧 Graphics/CBG/Canvas。它把脚本可观察的像素和资源生命周期固定在 `GameSession`，再把 Godot `ImageTexture`、Atlas、Canvas item 和 Node 作为可回收的派生投影。M0-M2 期间只能继续使用旧路径或旁路 capture；`graphics.pixel_store` 默认关闭，不能因为本章存在就提前启用。

## 目标与非目标

目标：

- 用 `PixelStore` 作为 G、SpriteGetColor、GDraw 等脚本可观察像素的唯一 CPU 真相。
- 用 `ResourceCatalog` 保存 CSV 声明、逻辑 key、crop、animation、dynamic sprite 和 CBG 逻辑状态。
- 让纹理上传、Canvas/RID 和 Control 后端按 `(handle, revision, generation)` 投影，并可在 stale completion 时安全释放。
- 用 `MemoryBudget` 同时计量压缩源、CPU surface、临时 decode、GPU texture、atlas/crop 和 Node/handle 数量。

非目标：

- 不以 GPU readback 作为脚本读像素路径。
- 不在 M4 重写 HTML/layout、旧 Canvas/Control hit test 或字体测量。
- 不把每一行 Display 历史创建为 Node；不把 RID 当作资源引用。
- 不用 `queue_free()`、GC 或驱逐缓存掩盖仍被 Display/CBG/脚本引用的对象。

## 资源流水线

```text
CSV/manifest candidate
  -> ResourceCatalog descriptor (Core, immutable)
  -> bounded source/header validation
  -> decode PixelSurface under MemoryBudget reservation
  -> PixelStore handle + revision (VM owner thread)
  -> main-thread upload ImageTexture/Atlas/RID
  -> Console/CBG backend projection
```

CSV 顺序、重复 key、大小写、相对路径、ANIME/frame、crop 和缺图行为以 [ResourceSystem](ResourceSystem.md) 为准。source path 与 logical `ResourceKey` 永远分离；dynamic sprite 可按 eraFL 的游戏根再 `resources/` 顺序查找，但仍必须通过 root containment 和内容 token 验证。

## PixelStore 合同

`PixelStore` 属于 `GameSession`，只由 VM owner thread 修改。每个 surface 包含 `width`、`height`、format、stride、拥有的 bytes、revision、dirty rect 和 source provenance。具体 32-bit alpha/混合格式、颜色矩阵和坐标边界必须由 upstream/legacy/erafl fixture 锁定，不能从 Godot `Image` 默认格式推导。

| 操作 | Core 语义 | Bridge 语义 |
| --- | --- | --- |
| G create/load | 建立 CPU surface 后才返回成功 | 可异步解码，但 completion 带 operation/generation |
| G get color | 直接读 PixelStore | 不读 GPU，不等待渲染帧 |
| G draw/set pixel | 串行修改并递增 revision | 只投影已提交 revision |
| Sprite source color | 从 source PixelStore/decoded surface 读取 | texture 仅缓存投影 |
| CBG set/clear | 修改逻辑 layer/revision | 整个提交 transaction 后可见 |
| dispose | 清除逻辑 handle 和强引用 | 等 view/CBG refs=0 后释放 texture/RID |

一个 revision 必须是原子可观察单位：脚本不能读到一半合成的 surface，View 也不能显示中间 dirty rect。若需要分块合成，只能在同一 VM owner transaction 内完成，或创建不可观察的临时 revision，最后一次性 publish。

## Godot 节点与资源生命周期

Godot 资源和 SceneTree 只能在主线程创建/修改。推荐的 owner 链：

```text
GameSession.PixelStore (CPU truth)
  -> ResourceBridge (Node, main-thread owner)
      -> TextureCache / AtlasCache (CLR dictionaries + Godot refs)
          -> ConsoleBackend / CBG backend (View refs)
```

生命周期状态：`Unrequested -> Decoding -> CpuReady -> UploadPending -> Projected -> Evictable -> Disposed`。每次状态转移都检查 generation 和 revision。

1. `ResourceBridge` 在候选 Session commit 后 attach；旧 generation 的 decode completion 不能创建 `ImageTexture`、`AtlasTexture`、`Sprite2D` 或 Canvas item。
2. `_ExitTree` 先停止接受新 upload，再取消 decode CTS、断开动态 signal、停止 tween、从 View/CBG 清除 handle，最后释放 texture/RID 和 cache reservation。
3. `queue_free()` 只处理真正的 View Node；任何持有 Node 的 Array/Dictionary、Callable、signal lambda 或 tween 必须先清空/断开。
4. `RID` 必须显式 `free()`；RID 不保持 Texture/Material/Font 活跃，Bridge registry 必须强持有依赖 Resource 到 RID 释放。
5. `ImageTexture`/`AtlasTexture` 只代表某个 revision，不允许原地修改仍被其他 revision 引用的共享 Resource；需要改写时 duplicate 或创建新 revision。
6. `AudioStream`、字体和 Theme 不是 PixelStore 的所有者；它们遵循各自 Bridge 生命周期，不能通过 texture cache 的引用计数释放。

M4 禁止在 `_Process` 中调用 RenderingServer/PhysicsServer getter 读取状态；这些调用会强制同步 flush。上传采用有界每帧预算，不能用 `call_deferred()` 无限堆积。

## CBG、Sprite 与 eraFL 特殊点

eraFL 相关行为不能被“统一成一张纹理”掩盖：

- `CBGSETSPRITE` 与旧 gEmuera 的 `CBGSETCIMG` alias 按 CompatibilityPlan/profile 分开注册。
- CBG button map、selected `src`/`srcb`、z/order、dynamic map scope 和 data-only refresh 都属于逻辑 Display/CBG transaction；纹理投影只是结果。
- `SPRITEDISPOSEALL 0` 只清动态 Sprite 的语义必须由资源 effect 保留，不能在 Bridge 里扩大为全局卸载。
- 相对路径查找、crop、pivot、旋转和私有图标字体分别记录 source/revision；不能通过游戏名分支或 UI 启发式重算。
- 动态地图删除底部后保持 viewport 的 scroll intent 进入 transaction；ResourceBridge 不得自己修改滚动位置。

## 缓存和内存账本

缓存 key 至少为 `sourceIdentity + crop + importOptions + revision`。每条记录：owner generation、source token、CPU bytes、GPU estimate、derived bytes、strong refs、cache refs、last use、evictable、RID/Texture IDs。

| 计量类别 | 计量方式 | 释放条件 |
| --- | --- | --- |
| compressed source | 文件大小/压缩 buffer | source reader 和 pending decode 均关闭 |
| CPU PixelSurface | width×height×format + stride | PixelStore/脚本/CBG strong refs 为 0，且允许驱逐 |
| temporary decode | decoder buffer 实际 reservation | decode success/fault/cancel 的 finally |
| GPU Texture/Atlas | format+mips/layers 估算或 monitor | View/CBG/cache refs=0，RID 已 free |
| derived crop/region | 实际新分配 | region refs=0；不能重复计 source atlas |
| Node/Canvas item | registry count + orphan check | `_ExitTree`/explicit dispose；Canvas item RID free |

所有 decode/upload 先 `TryReserve`，失败时按退化阶梯处理：暂停预取、驱逐无强引用派生缓存、降低 atlas/overscan、最后拒绝当前资源但保留当前 Session。不能释放仍被 Display/CBG 或 G slot 引用的 CPU/GPU 对象。

## 并发与背压

同一个 source identity 只允许一个 decode owner；其他请求等待同一不可变结果或被合并。Array/List 扩容只在 owner thread 或受 Mutex 保护的 worker 中进行。主线程 upload queue 有最大 bytes、最大 jobs 和 cancellation latency；旧 generation completion 在 payload 解包前释放自己的 reservation。

结果类消息（像素 revision、CBG transaction、错误、输入/保存 completion）不可丢弃。只允许同 generation、同 operation 的非语义进度或明确有 reducer fixture 的 texture latest-wins。队列满先背压 VM/暂停低优先级预取，不得丢弃逻辑结果维持 FPS。

## 工程工单与原子化观测

M4 的工单 ID、输入 identity、报告和回退形状以 [M3M7EngineeringExecution](M3M7EngineeringExecution.md) 为准。M4-RES-01 必须先锁定 PixelSurface 的脚本可观察合同；M4-RES-02 与 M4-RES-03 可在同一冻结输入上分别准备，但不得发布任何新的 Texture/RID 投影；M4-RES-04 只在前两者通过后接入 G/CBG/Sprite 双跑；M4-RES-05 为每个已启用路径做压力、取消和回退验证。

每个 PixelStore publish、decode completion 与 upload completion 都要在报告中能串回同一 generation、operation、handle、revision 和 reservation。允许合并的仅是已由 fixture 证明为非语义的进度；G 返回值、CBG transaction、资源错误、dispose 和 Display 顺序必须逐项保留。这样可区分“上传变慢”与“脚本看到错误 revision”，也能在旧 generation 到达时证明 payload 已释放而非悄悄丢失。

## M4 门禁与回退

M4 关闭门禁必须有：

1. G/CBG/Sprite/GraphicsImage 的尺寸、alpha、混合、像素返回、revision 和错误双跑报告。
2. eraFL 动态地图、div/srcb、data-only、button generation、Sprite dispose 和双后端截图/hit fixture。
3. 100 次 Session 切换、取消 decode、快速切换 BGM/纹理、`_ExitTree` 和 stale completion 后 Node/RID/Texture/task/reservation 稳定。
4. Mobile-Low/Mid 与 Desktop 的 CPU/GPU/RSS/managed 峰值和 upload p95 报告。
5. `graphics.pixel_store=false` 与旧 GraphicsAdapter 回退报告；失败时旧 Canvas/Control 和 Android 路径可运行。

任何像素/返回值/时序差异都阻止启用；“截图看起来相同”不能覆盖 Core PixelStore 差分。M4 初始阶段状态为 `executionStatus=NotStarted; gateStatus=Blocked; blockerCode=PreviousGate:M3`。

## 交付边界

M4 可以新增 `PixelStore`、`ResourceCatalog`、`ResourceBridge` 和测试，但不得修改 M0-M2 生成报告字段或抢占另一并行 AI 正在编辑的旧资源/显示文件。所有新文件附 feature flag、source/toolchain/runtime identity、memory ledger 和 rollback report；没有设备/GPU 证据的项保持 `Uncovered`。
