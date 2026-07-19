# 差分、fixture、fuzz、构建与真机验证

## Harness 拓扑

```text
fixture manifest
 ├─ upstream XEmuera runner ─┐
 ├─ legacy gEmuera runner ───┼→ profile-aware canonical reports
 ├─ SN/FL/AN game replay ────┤
 └─ Target Core/Godot runner ┘
                                  ↓
                            normalizer/comparator
                                  ↓
                  Passed / Failed / Intentional / Uncovered
```

runner 进程隔离，固定 locale/timezone/config/seed。旧 gEmuera M0 runner 先保持当前 `EmueraThread`、Canvas/Control、VirtualCursor 和 Android export 不变，用来捕获已修好的行为。超时由外部 harness 控制；stdout 不是唯一结果。

## 快反馈、工作包与发布验证

本章定义的 canonical report、三层 runner、真实游戏、可视显示和设备矩阵属于 work package 或发布验证，不是每一次编辑的默认命令。日常 AI 修改先按 [AIDevelopmentWorkflow](AIDevelopmentWorkflow.md) 运行任务包指定的定向 FastLoop；只有触及本章的 fixture、transaction、平台或性能边界时才升级。

纯 Core 行为优先使用无 Godot 的定向测试或 contract smoke；单一 Node、signal、focus 或触摸场景可用 gdUnit4 的单套件验证。gdUnit4 的输入模拟需要带窗口环境，不能把 headless 结果当作触摸、鼠标或键盘通过。真实游戏、Controls/Canvas 可视截图、Android APK、性能和重复性报告仍必须留在独立 runner/设备队列。

FastLoop 成功只能说明局部契约；当前章节所列 state、display、effects、errors、timeline 与设备报告仍是阶段 gate 的完整证据。

## canonical report

- `state.json`：变量/角色/系统状态的白名单快照。
- `display.json`：完整 Line/Div tree、src/srcb、style/interaction、transaction/scroll/data-only。
- `effects.json`：资源/音频/应用 effect sequence。
- `errors.json`：分类、source position、调用栈符号。
- `timeline.json`：step、wait、input、event、load flow。
- `semantic-trace.json`：用于 repeat comparison 的脚本可观察顺序投影；完整 transport trace 仍保留在 `trace.raw.json`/`trace.json`。
- `metrics.json`：仅诊断，不用于语义 equality。

normalizer 只移除时间戳、绝对路径、对象 id 等非确定字段；不得把顺序或文本差异“归一化掉”。唯一独立的 comparison projection 是 `semantic-trace.json`：它只能排除已被仪器明确标记为 `ui_projection` 的 Godot 传输批次，保留其余脚本可观察事件并连续编号；raw/canonical trace 本身不删除事件。

## fixture 集合

| 套件 | 关键覆盖 |
| --- | --- |
| SaveCompatibility | 四 file type、压缩/普通、全部 data type、稀疏、Unicode、损坏/旧版本 |
| InstructionMatrix | 每个注册指令/函数 parse+execute+error boundary |
| Variables | 四 save count、TFLAG、角色、局部、1D/2D/3D |
| GEmueraExtensions | float/FUNCTIONF/REFF、VarExt、SQL、NF、HOTKEY、setting.json |
| ResourceCompatibility | CSV 顺序/重复/路径/crop/ANIME/G/Sprite/CBG |
| HtmlGolden | 每 tag/attr/entity/comment/error/button/line |
| EncodingCorpus | UTF-8/BOM/CP932/歧义/截断/非法 |
| InputScheduling | INPUT/TINPUT/ONEINPUT/WAIT/skip/timeout/duplicate/stale |
| ControlFlow | RESTART、CALL/Event/UserFunction、quit/restart/load flow |
| SessionSwitch | rapid switch/failure/cancel/late completions/config reset |
| LegacyDisplayInput | eraFL div/srcb、dynamic map、scroll/data-only、VirtualCursor/MOUSEBUTTON |
| Security | path/link/bomb/huge media/deep markup/cycle |

## 原版存档 fixture

至少一组由可识别上游 XEmuera build、另一组由旧 gEmuera float/VarExt profile 实际写出。测试执行：各 profile baseline read→canonical；target read→canonical；target write→对应 baseline read；错误 profile 必须稳定拒绝。特别覆盖 0x20–0x23 类型码冲突。

## HTML golden

上游与旧 gEmuera `Html2DisplayLine` 结果转换为不含平台对象的 canonical DTO。每例比较 line/div tree、box、position、src/srcb、style/button/alignment/overflow 和 error。eraFL 页面作为强制 golden；Godot backend 再做截图/hit/accessibility 测试。

## 调度测试

使用 fake clock 和可控 completion queue，不 sleep。Legacy runner 与 resumable runner 重放同一 trace；断言 completionMode、ordering point、唯一 completion、effect sequence 和 generation guard。专用 VM thread 另测 queue 背压/heartbeat；主线程实验另测完整 yieldability 清单。

## fuzz

解析器/HTML/CSV/path/save fuzzer 使用固定 seed、最大输入、外部进程 timeout 和内存限制。保存 crash input 与 seed，最小化后进入 regression corpus。属性：不 crash/OOM/hang、不越根、不部分提交、结果确定。

## 恶意资源包

每个 SecurityLimits 攻击面至少一个 seed，报告硬上限、error code、候选清理、当前会话是否不变。巨型逻辑输入优先用稀疏/生成器，避免仓库真实占用数 GiB。

## Godot tests

- ApiSmoke：锁定 C# binding 编译。
- SceneIntegration：Main/Console/Input/Error/Loading 各场景 F6-safe、signal cleanup、mouse_filter/focus。
- Rendering benchmark：后端 A-D 相同 Display fixture。
- Visual golden：分辨率/DPI/Theme/字体/CJK/emoji/RTL。
- Export smoke：安装/启动/内置 fixture/save/quit。

## 移动真机矩阵

每个平台至少目标低/中档设备；Android 记录 target SDK/provider，iOS 记录设备/OS/Xcode。覆盖导入大包、取消、覆盖、权限撤销、空间不足、后台、进程杀死/恢复、safe area、虚拟键盘、触摸拖动不误触。模拟器不能替代最终真机列。

## 性能

Release export、固定 fixture、warm-up、p50/p95/p99。分别报告 VM/layout/draw/native/GPU/I/O/decode/memory。VSync/Profiler 开销标明。回归使用批准 baseline，不使用测试总数代替覆盖。

## 报告与状态

每份报告含 source layer、compatibility profile、feature flags、toolchain lock、source/artifact hash、fixture/game hash、命令、平台和结果。比较器分别输出 upstream↔legacy、legacy↔target、game↔target；一个 profile 通过不代表另一个。Graphics 报告含 dimensions、return、pixels/hash、revision sequence。

## 当前状态

协议已定义，旧 gEmuera Godot 工程、本地 legacy runner/trace 和 resolved fixture manifest 已存在；manifest 明确显示 upstream runner、eraFL、授权复核、artifact store、目标 Core build 与真机报告仍缺失。因此相关项继续为 BlockedEvidence/Uncovered，本地 Partial 不能替代归档和签署。
