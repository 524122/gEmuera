# M6 调度与渲染实验

## 文档地位

M6 是实验阶段，不是把 cooperative VM 或新 renderer 视为自然升级。默认生产路径仍是 M5 的专用 `VmHostThread`、现有 Controls/Canvas 和兼容音频。M6 的每个实验必须独立 feature flag、独立 artifact、独立差分报告和一键回退；VM 调度实验与 renderer 实验不能绑成一个总开关。

## 实验原则

实验只回答可测问题：主线程 Step/Resume 是否降低端到端延迟、候选 renderer 是否降低 CPU/GPU/RSS 或改善移动适配，同时不改变脚本可观察顺序。不能用“Node 更少”“代码更现代”作为放行理由。

| Flag | 默认 | 实验对象 | 回退 |
| --- | --- | --- | --- |
| `vm.main_thread_experiment` | off | cooperative `Step/Resume` | `vm.host_thread=true` |
| `render.backend.candidate` | off | 新 Control/Canvas/自绘候选 | legacy Controls/Canvas |
| `render.accessibility_fallback` | on | AccessibleConsole fallback | 旧文本 fallback |
| `experiment.dual_run` | off | 旧/新 runner 与后端并行观察 | 单路径运行 |

实验 flag 必须进入 runtime identity、fixture manifest、截图目录、trace 和 rollback report。不能在用户没有看到 profile/风险的情况下持久化到存档或全局配置。

## Cooperative VM 进入门

主线程实验前必须完成完整 yieldability audit：

- Regex、HTML、ERB lazy load、CSV、XML、Map、DataTable、SQLite、文件枚举/文本 I/O、GZip/save、图片 decode、G/Sprite/CBG、插件、日志和大数组。
- 每条路径记录最大同步时长、可分块性、取消点、跨指令可观察中间状态、memory reservation 和 Android p99。
- `WaitPort` completion、Display transaction、INPUT/WAIT、save/load、fault 和 application effect 的 ordering point 不能被 frame boundary 重排。
- 任何未分类同步路径、无界分配、Godot API 调用或捕获旧 Node 的 continuation 都使实验保持 `Rejected`。

Step 合同：

```text
Step(budget, cancellation, generation)
  -> Completed(effect batch) | Yield(wait port) | Fault | Stopped
Resume(completion)
  -> validates operationId + generation before state mutation
```

Step 预算同时包含 VM 指令、effect reduce、layout request、texture upload preparation 和 input dispatch；只测 VM 指令时间会掩盖 UI 长尾。禁止在 `_physics_process` 或 `_process` 中 `await`，禁止把 `call_deferred()` 当作 scheduler。

## 专用线程与 cooperative 双跑

旧 runner 和 cooperative runner 接收相同的 immutable input、clock、RNG、file manifest、CompatibilityPlan 和 seed。两个实例不共享 static/cache/Resource/PixelStore。比较：

- state mutation、Display/CBG transaction、pixel revision、input request 和 save result；
- completion mode、effect sequence、wait/resume、error/fault 和 generation；
- frame CPU、VM busy、queue depth、layout/upload、managed/native/GPU/RSS、cancel latency。

任何行为差异先标 `Failed` 或 `IntentionalDifference` 并保留 trace；不能用 normalizer 删除顺序、文本、错误或截图来造绿。

## 候选 renderer

候选 backend 只能消费 M2/M4 的完整 Display DTO、PixelStore revision 和 scroll/data-only transaction。它不能重写 markup、字体测量、hit test 规则或把动态地图 scope 变成 renderer 私有启发式。

候选实现选型顺序：

1. 可见窗口固定 Node pool（visible + overscan），而不是每行一个 Node；pool 节点在 `_ExitTree` 清空并解除 signal/Callable。
2. 自绘/Canvas backend 缓存 shaped runs、hit spans、Theme/Font revision；`_draw()` 只读 snapshot，不修改 VM/Display 状态。
3. 10K+ 重复图形才考虑 RenderingServer RID；RID 依赖的 Texture/Material 必须由 registry 强持有，退出时手动 free。
4. Accessibility fallback 使用独立 Control/RichTextLabel，不能与视觉 backend 共享可变 children。

renderer 的 viewport、safe area、DPI、CJK/emoji/RTL、字体 fallback、touch target 和 dynamic map scroll 都要在同一 fixture 运行。截图差异不能覆盖 interaction rect/hit 失败；headless 空帧不能当视觉通过。

## Godot 生命周期与线程安全

实验 attach 流程：

```text
candidate session commit
  -> MainThread attach backend(generation)
  -> drain only matching batches
  -> render/upload within frame budget
  -> detach: stop intake, cancel, disconnect, kill tweens, free RIDs
```

规则：

- `SceneTree`/Node/Control/Theme/Font/Texture/AudioServer 仅主线程；worker 只构建不可变 snapshot 或独占 buffer。
- `_ready()`/`@onready` 之前不访问 children；不能以 `call_deferred()` 修复未知初始化依赖。
- `_exit_tree()` 先停止新消息，再取消 CTS/断开 signal，观察 task completion，清 Node pool，最后释放 GPU/Audio handle。
- Tween 绑定 owning Node；节点可能被 `queue_free()` 时先 kill，不能留下 continuation。
- `RenderingServer`/`PhysicsServer` getter 不放在热路径；请求会同步 flush。
- `@export Resource` 如需实例独立必须 Local-to-Scene 或 duplicate，不能跨 Session 意外共享 Theme/StyleBox/texture。

## 内存与性能判定

M6 报告必须分离：

| 维度 | 采样 |
| --- | --- |
| VM | instruction/work unit、yield、busy、queue backpressure |
| Main | batch reduce、layout、upload、draw preparation、input dispatch |
| GPU | frame time、draw call、texture/atlas allocation |
| Managed/native | GC、RSS、native image、SQLite/audio buffer、Node/Resource/RID count |
| UX | p50/p95/p99、first frame、input latency、scroll stability |
| 生命周期 | orphan Node、live Callable/tween/task、stale completion、reservation |

比较必须使用同一 release artifact、工具链、分辨率、VSync、warm-up、fixture、设备和采样协议。建议目标遵循 [PerformanceOptimization](PerformanceOptimization.md)，但未实测数字不能写成产品承诺。任何候选若降低 FPS 但改变了语义顺序，均不接受；若性能更好但内存峰值超设备档位，也不接受。

## erafl 与魔改游戏实验集

M6 必须把 eraFL 当作完整压力基线，而不只是截图：

- dynamic-map transaction、底部删除后 viewport anchor、div/srcb/负坐标/box overflow；
- data-only button generation 不重建视觉节点；
- `INPUTS ,1`、空白右/中键整数 `-1`、VirtualCursor、多点触摸和 focus/no-focus；
- 相对 dynamic sprite root、`SPRITEDISPOSEALL 0`、G/CBG revision、异步补图；
- unknown markup angle text、私有字体、旋转 pivot、错误编码 XML 和 SQL/Map/DT；
- 输入等待、后台恢复、快速切换 generation、stale texture/audio/save completion。

其他魔改游戏通过 `CompatibilityPack` 选择相同 capability 时，实验报告应证明未选择模块对 v24/Snake baseline 不变；不能因为某个 eraFL fixture 通过就宣称所有魔改兼容。

## 实验包、阈值与判定记录

M6 先完成 M6-EXP-01 的实验定义，才可启动任何 scheduler 或 renderer 代码。该定义必须在运行前冻结假设、baseline artifact、fixture、设备、warm-up、采样窗口、p50/p95/p99/RSS/GPU 阈值、flag 和回退 artifact；完整字段和报告形状见 [M3M7EngineeringExecution](M3M7EngineeringExecution.md)。

M6-EXP-02 的 yieldability audit 是 M6-EXP-03 的前置；M6-EXP-04 可在相同 baseline 下独立进行 renderer 实验；M6-EXP-05 对每个实际启用的实验分别执行设备与生命周期压力。阈值、normalizer、截图掩码或基线在看到候选结果后发生变化时，原实验必须 ReportInvalidated 并从 M6-EXP-01 重跑。

候选 renderer 的 Godot Theme、Font、StyleBox 与 focus 样式由根 Theme 或每实例独立副本拥有。自绘路径只从该 Theme 读取快照，不在 draw/process 热路径创建或修改资源；透明容器不得以 STOP 吞掉后方按钮，键盘/控制器 focus 必须保留可见替代反馈。这样视觉优化和无障碍验收共享同一 fixture，而不是在两套场景中各自解释。

## 实验门禁与失败处理

M6 通过的最小条件：

1. 100% yieldability audit 和无未分类同步路径。
2. 专用线程与 cooperative 的 canonical state/effect/error/order 双跑一致。
3. legacy 与候选 renderer 的 visual golden、hit/accessibility、scroll/data-only golden 通过。
4. Desktop、Android 低/中档目标设备的 p95/p99、RSS/GPU/Node/RID/task/reservation 在批准阈值内。
5. 快速切换、暂停/恢复、cancel、OOM/预算拒绝和 `_ExitTree` 压力测试无增长或旧 generation 写入。
6. 任一 flag 独立关闭后能立即回到稳定路径，回退 artifact 和报告可追溯。

失败只关闭实验 flag，不修改旧逻辑以适应候选。M6 初始状态为 `executionStatus=NotStarted; gateStatus=Blocked; blockerCode=PreviousGate:M5`；实验报告不得把 `Passed` 误写为默认生产路径已替换。

## 并行 M0-M2 协作

另一 AI 的 M0-M2 工作可以继续运行。M6 文档和 benchmark fixture 只能旁路读取 M0 trace/DTO/显示基线；如果 baseline report、字段或 generation 规则发生变化，实验 artifact 必须标记 invalidated 并重新运行，不能覆盖或手工修订 M0 结论。
