# 性能预算、采样协议与退化策略

## 原则

先测量后优化。性能数字只有同时记录工具链、build 类型、硬件、平台、fixture、采样时长、VSync/分辨率和统计方法才有效。Debug/editor 数据仅诊断，不作为发布结论；release export 关闭 VSync 后测 CPU/GPU 极限，另开 VSync 测用户体验。

## 指标口径

| 指标 | 采集 | 说明 |
| --- | --- | --- |
| frame CPU | Godot profiler/高精度单调时钟 | 分 VM、queue、layout、draw submit、other |
| GPU frame/draw calls | Visual profiler/Rendering monitor | 与 CPU 分开 |
| renderer identity | project setting + runtime method/driver when available | 区分 Compatibility/OpenGL 与 Mobile/RenderingDevice artifact，不能只写“Android” |
| VM | owner-thread busy、instructions、work units、batch queue、blocked port；实验模式另记 Step/yield | 不只看 FPS |
| layout | shaped chars、lines reflowed、cache hit、usec | 字体/宽度 revision 分组 |
| I/O/decode | bytes、duration、worker queue、cancel latency | 保存/导入/图片/音频分开 |
| memory | managed heap、native、GPU、CPU image、audio PCM、compressed source | `GC.GetTotalMemory` 只代表 managed |
| lifecycle | orphan nodes、live RID/texture/voice/task | debug/release 能力注明 |

微基准使用 microsecond precision；同一 workload warm-up 后多轮，报告 median/p95/p99。不能说 Timer 天然精确或普通 C# 对象天然线程安全。

## 初始目标（待实测）

以下是设计门槛，不是已通过声明：

| 档位 | UI frame target | VM/队列策略 | 历史 | 资源预算 |
| --- | --- | --- | --- | --- |
| Desktop baseline | 60 Hz 下 p95 <16.7 ms | dedicated VM + bounded batches；实验 Step 待测 | 100k 行可回看 | SecurityLimits Desktop 档 |
| Mobile mid-range | 60 Hz 或稳定 30 Hz 档 | dedicated VM + strict UI drain/upload budget | 50k 行可回看 | Mobile-Mid reservation 档 |
| Low-memory fallback | 稳定 30 Hz | 减少 batch/upload/预取并背压 VM | 10k 行/主动裁剪 | Mobile-Low 档、禁动画 |

不能把 skill 的通用预算直接当本项目证据；最终数字由目标硬件报告替换。

迁移回归先以旧 gEmuera 的代表游戏实际 p95/p99、历史 profile、Canvas prefix/bucket hit index、复杂 div Control fallback 和移动解码并行度作为相对 baseline；100k/50k 行是远期压力目标，不能为了追目标先删除旧项目在 Snake/eraFL 上已稳定的优化路径。

Android Compatibility/OpenGL ES 3 是低端发布基线，不是“卡顿已修复”的结论。针对 TW 起床等高输出流程，必须以同一 release APK identity、游戏版本、profile、存档、分辨率和输入序列对比独立的 Mobile 与 Compatibility artifact，并同时记录 `PERF.SAMPLE`、`PERF.DISPLAY_BRIDGE`、`PERF.CONSOLE_RENDER`、VM busy/work units、GPU frame/draw call 与内存峰值。若 Compatibility 后 CPU/显示指标仍出现相同尖峰，优化 owner 是 VM 或 presentation，不能继续把问题归为 Vulkan；完整归因和回退协议见 [RendererDeploymentPolicy](RendererDeploymentPolicy.md)。

## VM 优化顺序

迁移默认优化专用 VM thread：先消除 Godot/静态 UI 调用，设置 batch 背压，测量 Regex/SQL/Graphics/lazy ERB 的长操作；不要先迁回主线程。主线程 experiment 必须把 VM Step、effect reduce、layout、texture upload 和 draw preparation 的合计 p99 作为判定，而非只看 Step 时间。

1. profiler 确认热点与 work units。
2. 消除同步跑到底和重复 parse。
3. 缓存 LogicalLine/identifier 解析结果。
4. 减少 hot path allocation，使用结构化值/池但不牺牲正确性。
5. 大循环分块；纯计算才送 worker。

Dictionary 平均查找通常接近 O(1)，最坏受 hash collision/攻击输入影响；外部 key 应限制长度/数量并使用适当 comparer。StringName 只在 Godot hot path/固定名字适用，Core 不为此引入 Godot 类型。

方言扩展在候选加载时完成 DAG 解析、冲突验证和冻结表构建。运行期按规范化 id 直接查 instruction/function/policy 表，不逐模块扫描、不反射发现、不在每条指令使用 service locator。报告分别列 plan build 时间/分配、registry 大小、lookup p95/p99；加入但未选择的模块不得改变 v24 hot-path 指标和 plan hash。

## 虚拟控制台

可见定位/命中目标 O(log n + visible)。node backend 使用固定大小 pool（visible + overscan），不为 100k 行创建 100k Node。自绘 backend 缓存 shaped runs 与 hit spans。字体/宽度变化分帧重排，优先 viewport；保持 anchor 防止滚动跳跃。

## 资源与内存

先按[LifecycleMemory](LifecycleMemory.md)资产分类计量，再调 LRU。避免 hot path 同步 load、重复裁剪 texture、每帧新 StyleBox/Tween/Node。派生 texture 进 cache/ledger。移动使用 ETC2/ASTC 导入策略只适用于 app 自身资源；外部动态图仍需运行时解码预算。

## 加载

候选游戏包的枚举、hash、编码、CSV、图片 header 可后台执行；Godot texture 创建和 SceneTree attach 主线程分批。queue depth、完成数/帧和 cancellation latency 都进入报告。加载进度基于已知 work/bytes，不伪造平滑百分比。

所有 decode、PixelStore、save candidate 和音频任务先向 `MemoryBudget` reserve；报告同时列当前/峰值 reservation、拒绝/排队次数和档位。单对象未超限不代表并发总量安全。

## 退化阶梯

1. 降低 overscan、动画刷新和非必要重绘。
2. 暂停后台预取/派生 texture。
3. 减小历史上限并提示，保留最近行和书签策略。
4. 驱逐无强引用 CPU/GPU cache。
5. 切换低内存/30 FPS 档、禁高成本视觉效果。
6. 拒绝继续导入并保留当前会话。

不通过释放仍被 Display/CBG 引用的资源“解决”内存。

## 性能门禁

固定 fixture 连续运行预热+采样；报告分脚本、布局、绘制、Godot native、GPU、I/O、解码。回归阈值以 baseline 的百分比和绝对上限双重判断，例如 p95 增长 >10% 且 >0.5 ms 才失败，避免噪声。真实阈值由首轮设备报告批准。

## 工具使用注意

release profile 不留高频 print；Visual Profiler 本身有开销，分别采样；VSync 会掩盖瓶颈；debug-only orphan monitor 不能作为 release 保证。主线程不得在 `_Process` 高频读取会强制同步的 server 状态。

## 验收

每个优化结论附 before/after 同配置报告；不同文档只引用本章目标；无硬件/样本/方法的数字标“建议”而非保证。
