# gEmuera 第二轮综合优化 Plan

```
topology: multi-module
change-set: gemuera-round2-20260805
coordinator: E:/MyCode/GodotCode/gEmuera-future
spec: docs/staging/specs/2026-08-05-gemuera-round2.md
```

执行模式：goal 模式（分阶段顺序推进，阶段内 Workflow 并行，阶段间验证 gate）。

## Milestones

```
milestone: M1 性能优化（见 ROADMAP）
goal: 完成 H1-H9 与低收益精选项，语义零漂移。
milestone: M2 组件化
goal: UI 功能面板抽成可复用组件，行为不变。
milestone: M3 结构整理 + docs
goal: 目录/命名规范统一，docs/gEmueraCodeWiki 同步。
milestone: M4+5 UI 美化
goal: main.tscn 与按钮深色现代风，动效 + Shader。
```

## 任务（goal 模式下以 Workflow 阶段为单位执行）

### M1 性能优化（legacy-core / rendering / save-io / android-host）

```
goal: 完成 H1-H9 与低收益精选，语义与 v24/Snake 一致，存档/渲染/ERB 行为零漂移。
acceptance: 每项「实现 agent 自验证 + 对抗验证 agent」PASS；合入后主工作区 dotnet build 0 错误；语义项差分/对照验证。
spec: docs/staging/specs/2026-08-05-gemuera-round2.md#任务-1
```

5 个并行实现分组（worktree 隔离，文件不重叠）：
- [ ] G-A 状态栈: H1 ExecutionContext 池化 + M1 LOCAL/ARG 代际缓存（Process.State/ExecutionContext/VariableToken）
- [ ] G-B 加载索引: H2 $标签 Dictionary + M4 目录单遍枚举 + M5 LazyLoading HashSet + LexicalAnalyzer 数字 span/双重扫描
- [ ] G-C CSV: M2 CSV span 解析 + M3 模板密集化（ConstantData）
- [ ] G-D 存档: H3 存档直写 + M7 key 表 + M8 后台落盘 + Zero-run + PUTFORM（EraBinaryDataWriter/Reader/CharacterData/VariableEvaluator）
- [ ] G-E 渲染内存: H4 纹理缓存上限 + H5 CPU Image 释放 + H6 WebP 解码上限 + M6 PRINTFORM 短路 + M9 纹理压缩 + StrForm/PrintStringBuffer/Preload 低收益

每个分组：implementer（worktree）→ spec-reviewer → quality-reviewer → 合入。

### M2 组件化（ui）

```
goal: Inputpad/Scalepad/QuickButtons/OptionWindow/RuntimeDiagnosticsPanel 抽成组件。
acceptance: 场景加载 + 交互行为不变 + build 通过。
spec: docs/staging/specs/2026-08-05-gemuera-round2.md#任务-2
```

### M3 结构整理 + docs（ui / legacy-core / docs）

```
goal: 目录/命名规范统一 + docs 同步。
acceptance: build 通过 + wiki 与代码一致。
spec: docs/staging/specs/2026-08-05-gemuera-round2.md#任务-3
```

### M4+5 UI 美化（ui）

```
goal: main.tscn + 按钮深色现代风（Theme token + 动效 + Shader）。
acceptance: 视觉检查 + 交互可用 + build 通过。
spec: docs/staging/specs/2026-08-05-gemuera-round2.md#任务-45
```
