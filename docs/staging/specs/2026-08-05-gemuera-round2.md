# gEmuera 第二轮综合优化 Spec

```
topology: multi-module
change-set: gemuera-round2-20260805
coordinator: E:/MyCode/GodotCode/gEmuera-future
repos:
  - E:/MyCode/GodotCode/gEmuera-future (single repo, multiple modules)
modules:
  - legacy-core: Scripts/Emuera/GameProc + GameData + Sub
  - rendering: Scripts/EmueraContent*.cs + EmueraImage.cs + ColorMatrixGPU.cs + AnimatedWebpSpriteFrames.cs
  - save-io: Scripts/Emuera/Sub/EraDataStream.cs + EraBinaryDataReader/Writer.cs
  - android-host: Scripts/SpriteManager.cs + GodotHost/
  - ui: Scripts/FirstWindow.cs + Inputpad/Scalepad/QuickButtons + main.tscn/first_window.tscn
  - docs: docs/gEmueraCodeWiki/* + docs/NewFrameworkDesign/*
```

## 背景

第一轮 P0-P2 性能优化已完成（SparseArray 密集化、StrConv、EnqueueUI 池、CalledFunction 模板复用、GetFloatValue 转发、GridGlyphTexts、动画帧缓存、字体度量缓存）。本轮含 5 大项：① 第二轮性能优化（已扫描出 H1-H9 + 低收益项）；② UI 功能面板组件化；③ 项目结构整理 + docs 同步；④⑤ main.tscn 与游戏内按钮美化（深色现代风）。

参考实现（语义铁律）：v24 = `E:\MyCode\Era\emuera.em-master`；Snake = `E:\MyCode\Era\emuera_lazyloading_selfmodified_version-main-skiasharp\Emuera`。

## 任务 1：第二轮性能优化（模块 legacy-core / rendering / save-io / android-host）

### decisions

```
contract: 对 H1-H9 与低收益精选项逐项实施，对照 v24/Snake 参考，逐项独立验证。
invariant: 1) ERB 行为语义与参考完全一致（用户铁律：严禁语义漂移）；2) 存档二进制/文本格式逐字节兼容；3) 渲染像素级一致；4) 性能只升不降。
test: 每项用「实现 agent 自验证 + 对抗验证 agent」双保险；关键语义项（ExecutionContext/LOCAL-ARG/$标签/存档直写）做差分或对照测试。
convention: 只改任务必要文件；不做无关重构；参考实现能抄则抄（v24/Snake 有成熟实现）；每项独立 commit。
deferred: MAX_IMAGESIZE、onTrimMemory 回调（留 Android 专项，本轮不做）。
```

### 实施清单

**H（高收益）**
- H1 [legacy-core] `Process.State.cs:1116` ExecutionContext 每次调用分配 6 数组 ≈17KB（LOCAL/ARG 钳制 1000）→ 按实际声明长度分配 + ArrayPool/对象池；对照 v24/Snake IntoFunction 的零分配方案
- H2 [legacy-core] `LabelDictionary.cs:330` $ 标签扁平 List 线性扫描 → 改 Dictionary 结构（O(1)），对照 v24/Snake
- H3 [save-io] `EraBinaryDataWriter.cs:110` 存档每数组 SparseArray.ToArray() 全量拷贝 → writer 直接遍历内部 data/overflow（第一轮密集化后 ToArray 是纯浪费）
- H4 [rendering] `EmueraContent.cs:196` graphicsImageTextureCache 会话内无上限 → 挂钩 GDISPOSE/GCREATE 失效 + 字节预算淘汰
- H5 [android-host] `SpriteManager.cs:171` TextureInfo 同时持 CPU Image+GPU 纹理（预算只计一份）→ 双份核算 + 移动端上传后释放 CPU 副本
- H6 [rendering] `AnimatedWebpSpriteFrames.cs:283` 动画 WebP 全帧急切解码 + 全帧 RGBA8 保留 → 帧纹理压缩 + 并发上限 + 内存上限回退静态首帧

**M（中收益）**
- M1 [legacy-core] `VariableToken.cs:1969` LOCAL/ARG 每次读写走 GetArrayLocal（属性链+字符串比较+栈遍历）→ 代际计数缓存解析结果
- M2 [legacy-core] `ConstantData.cs:1502` CSV 每行 3-8 个短命 string → span 切片解析 + tryToInt64 重载
- M3 [legacy-core] `ConstantData.cs:2081` CharacterTemplate 10 个 Dictionary 逐条拷入 → 模板密集化 + Array.Copy（恢复注释掉的 BlockCopy 路径）
- M4 [legacy-core] `ErbLoader.cs:50` Android 启动 5-6 遍整树枚举 → 复用 Preload 文件索引单遍 + 内存内过滤
- M5 [legacy-core] `Process.LazyLoading.cs:242` AddLazyLoadingEntry 线性去重 O(F²) → HashSet
- M6 [rendering] `Instraction.Child.cs:146` PRINTFORM 每次执行完整管道（CheckEscape 无条件拷贝+重解析）→ 两个 IndexOf 短路（'\\' / '%'）
- M7 [save-io] `CharacterData.cs:484` 存档逐变量 code.ToString() 装箱 30K 次 → 预计算静态 key 表 + per-load token 缓存
- M8 [save-io] `VariableEvaluator.cs:3508` SAVEDATA 主线程同步 I/O + 整档 GZip → MemoryStream + 后台落盘/rename
- M9 [android-host] 所有运行期纹理 RGBA8 未压缩 → 移动端 ETC2/ASTC（条件编译/平台判断）

**低收益精选**：StrForm.GetString StringBuilder→handler（对照 v24/Snake DefaultInterpolatedStringHandler）；LexicalAnalyzer 数字字面量 span 解析 + Analyse 消除双重扫描；Preload 缓存按需释放（CSV/ERB 加载后清条目）；PrintStringBuffer BufferStrLength 缓存；存档 Zero-run 解码避免重复写 0；PUTFORM 用 StringBuilder。

## 任务 2：UI 功能面板组件化（模块 ui）

### decisions

```
contract: Inputpad / Scalepad / QuickButtons / OptionWindow / RuntimeDiagnosticsPanel 抽成独立组件（.tscn + 脚本 + export 参数 + 信号），可挂到 first_window.tscn / main.tscn 复用。
invariant: 组件化后 UI 行为与交互与现状完全一致（语义零漂移）。
test: 各场景加载 + 交互手动验证 + build 通过。
convention: Godot 组合优于继承；组件通过信号通信，不直接依赖父场景节点；不改 ERB 核心/渲染链。
deferred: 渲染链共享逻辑组件化（用户已排除，本轮不做）。
```

## 任务 3：项目结构整理 + docs 同步（模块 ui / legacy-core / docs）

### decisions

```
contract: 目录归类与命名规范统一；删冗余（如已删 _CLAUDE.md）；docs/gEmueraCodeWiki/*.md 与 NewFrameworkDesign/*.md 同步为对应描述。
invariant: 纯结构/文档变更，不改变任何运行时行为。
test: build 通过；wiki 结构与 10-Source-Index.md 与代码一致。
convention: 遵循 docs/gEmueraCodeWiki/99-Maintenance-Guide.md 的同步规则；结构变更前先读 00/10 定位 owner。
```

## 任务 4+5：UI 美化（模块 ui）

### decisions

```
contract: main.tscn 与游戏内按钮采用深色现代风：统一 Theme 资源（配色 token：暗色底 #14161D 系 + 强调色渐变 + 圆角 + 阴影）；入场/hover/press 微动效；按钮统一 StyleBox；必要处用 Godot Shader（渐变/光晕/动效）。
invariant: 交互逻辑与可点性不变（只改视觉/动效层，不改按钮 value/信号）。
test: 视觉检查 + 交互可用 + build 通过。
convention: Web 前端思想（design token、状态反馈、动效时长 150-300ms、hover/press/disabled 态齐全）；配色参考现代深色 UI（可参考 GitHub/Linear/Vercel 系）。
deferred: 具体动效/Shader 细节在实施时设计并让用户看效果。
```

## 全局

```
convention: 每个任务独立 commit；任务 1 性能优化逐项 commit；语义漂移零容忍（对照参考实现）。
roadmap: 分阶段：M1 性能优化 → M2 组件化 → M3 结构+docs → M4+5 UI。
```

## Working notes

- 任务 1 的 H1（ExecutionContext）与 M1（LOCAL/ARG）都触及 Process 状态栈，需一起实施并在同一验证下保证递归/嵌套/事件函数组语义。
- H3（存档直写）依赖第一轮 SparseArray 密集化后的内部结构，需确认 data/overflow 的访问接口。
- 任务 4/5 的 Shader 依赖 Godot Mobile/Compatibility 渲染器（Android），shader 需与默认渲染器兼容。
- HOTL / Brook Lint 技能不在本会话可用列表，任务使用 /godot-master + Praxis（design/plan/worktree/subagents）+ Workflow/Agent 编排。
