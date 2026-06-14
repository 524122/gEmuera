# Claude Code Workflow（Ultracode）工作原理深度分析与实现参考

> 基于对 Claude Code 2.1.167 系统提示词、运行时文件和日志的一手分析
> 更新时间：2026-06-06（第三版：补充版本历史、研究预览背景、模型 ID 更新、Codex 实现路线图）
> 数据来源：系统提示词 Workflow Tool Definition、Agent Tool Definition、ToolSearch Tool Definition、Skill Tool Definition、journal.jsonl、agent-*.jsonl、agent-*.meta.json、roster.json、daemon 日志、Anthropic 官方博客、Claude Code Changelog
> 目标读者：需要实现相同 Workflow 插件功能的开发者（如 Codex）

### 重要背景

**Dynamic Workflow（动态工作流）是 Claude Code 的内部实现细节，未公开文档化。** Anthropic 官方文档仅提及 subagent、worktree、plan mode 等高层概念，不包含 Workflow DSL、meta 块、agent()/parallel()/pipeline() API、Agent 类型注册表、质量模式或 Journal/Resume 机制的任何说明。本文档的所有 API 定义均来自系统提示词的一手逆向分析，是目前已知的最完整技术参考。

### 版本历史

| 版本 | 日期 | 关键变更 |
|------|------|---------|
| 2.1.152 | 2026-05-27 | 简化 Workflow 工具的内联进度显示，实时 agent 计数移至持久化状态行 |
| **2.1.154** | **2026-05-28** | **Dynamic Workflow 以研究预览形式发布**——Claude 可动态编写编排脚本，在后台运行数十到数百个并行 subagent |
| 2.1.157 | 2026-05-29 | 引入 worktree 隔离模式（`isolation: 'worktree'`） |
| 2.1.158 | 2026-05-30 | 新增 'Workflow keyword trigger' 设置（`/config`），可禁用 'workflow' 关键词自动触发 |
| **2.1.160** | **2026-06-02** | **触发关键词从 'workflow' 重命名为 'ultracode'**；'workflow' 不再自动触发工作流运行；修复 ultracode 在不支持 xhigh 的模型上的错误提示 |
| 2.1.161 | 2026-06-02 | 修复 worktree 隔离的后台 agent 被阻止在自己的 worktree 内编辑文件的问题 |
| 2.1.167 | 2026-06-06 | 当前版本（本文档分析基础） |

**可用平台**：Claude Code CLI、Desktop App、VS Code 扩展（Max/Team/Enterprise 计划），以及 Claude API、Amazon Bedrock、Vertex AI、Microsoft Foundry。

---

## 目录

1. [整体架构概览](#1-整体架构概览)
   - 1.1 Claude 4.X 模型族
2. [Workflow 工具完整 API 定义](#2-workflow-工具完整-api-定义)
3. [脚本 DSL 规范](#3-脚本-dsl-规范)
4. [脚本执行引擎](#4-脚本执行引擎)
   - 4.1 全局 API（agent/parallel/pipeline/phase/log/args/budget/workflow）
   - 4.2 内置工具与工具发现（ToolSearch/Skill/Agent 类型注册表）
5. [Agent 调度与通信](#5-agent-调度与通信)
   - 5.5 Agent 间通信（SendMessage）
   - 5.6 批量并发启动
6. [并发控制](#6-并发控制)
7. [Journal 与 Resume 机制](#7-journal-与-resume-机制)
   - 7.6 Resume 回退机制（Journal 不可用时）
8. [Budget 预算系统](#8-budget-预算系统)
9. [Pipeline 与 Parallel 语义详解](#9-pipeline-与-parallel-语义详解)
10. [质量模式（Quality Patterns）](#10-质量模式quality-patterns)
11. [故障处理与 Stall 检测](#11-故障处理与-stall-检测)
12. [嵌套工作流](#12-嵌套工作流)
13. [隔离层级详解](#13-隔离层级详解)
14. [Daemon 进程管理](#14-daemon-进程管理)
15. [关键文件路径映射](#15-关键文件路径映射)
16. [实现指南：如何复刻此系统](#16-实现指南如何复刻此系统)
   - 步骤 8-10：ToolSearch 解析器/Skill 调度器/Agent 类型注册表
17. [已知问题与限制](#17-已知问题与限制)
   - 17.3 平台可用性
18. [Codex 实现路线图](#18-codex-实现路线图)
   - 18.1 实现策略选择
   - 18.2 最小可行实现（MVP）
   - 18.3 Codex 平台映射
   - 18.4 关键实现细节
   - 18.5 与现有 Codex Workflow 插件的集成

---

## 1. 整体架构概览

```
┌─────────────────────────────────────────────────────────────────────────┐
│                           User Terminal                                  │
│                    (Claude Code CLI 2.1.167)                            │
└─────────────────────────────────┬───────────────────────────────────────┘
                                  │
                                  ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                         Claude Code Daemon                               │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────────────────────────┐  │
│  │ Supervisor  │  │  Roster     │  │  Worker Processes (sessions)    │  │
│  │  (PID)      │  │  (JSON)     │  │  • PTY master/slave pair        │  │
│  │             │  │             │  │  • Named Pipe rendezvous        │  │
│  └─────────────┘  └─────────────┘  └─────────────────────────────────┘  │
└─────────────────────────────────┬───────────────────────────────────────┘
                                  │ Named Pipes (Windows: \\.\pipe\cc-daemon-*)
                                  ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                         Main Agent (Claude)                              │
│  ┌─────────────────────────────────────────────────────────────────┐   │
│  │  Context: Full session history + Tools + Skills + Project rules │   │
│  │  Decision: "Should I use Workflow for this task?"               │   │
│  └─────────────────────────────────────────────────────────────────┘   │
└─────────────────────────────────┬───────────────────────────────────────┘
                                  │
                    ┌─────────────┼─────────────┐
                    ▼             ▼             ▼
            ┌──────────┐  ┌──────────┐  ┌──────────┐
            │Script    │  │ Journal  │  │ Subagent │
            │Engine    │  │ System   │  │ Spawner  │
            └────┬─────┘  └────┬─────┘  └────┬─────┘
                 │             │             │
                 └─────────────┴─────────────┘
                               │
                               ▼
              ┌────────────────────────────────┐
              │      Subagent Pool (≤16)       │
              │  ┌────┐ ┌────┐ ┌────┐ ┌────┐  │
              │  │SA1 │ │SA2 │ │SA3 │ │... │  │
              │  └────┘ └────┘ └────┘ └────┘  │
              └────────────────────────────────┘
```

**核心发现**：Claude Code 采用 **Daemon-Worker 架构**，主进程（Daemon）通过 Windows Named Pipe 管理多个 Worker 进程（每个 session 一个）。Workflow 是在 Worker 进程内部运行的编排层，使用受限 JavaScript 沙箱执行脚本，通过确定性缓存实现 resume。

**设计哲学**：Workflow 用于**确定性控制流**编排（循环、条件判断、扇出/扇入），而非模型驱动的决策。控制逻辑在脚本中显式表达，模型只负责每个 agent() 调用内部的具体工作。

**触发条件**：当 Ultracode 开启时（`--effort xhigh` 或 `/effort` 命令），主 Agent 对**每个实质性任务**判断是否需要 Workflow：

```
任务是否需要 Workflow？
├── 是 → 自动生成/使用 workflow 脚本
│       ├── 内联脚本（script 参数）
│       ├── 文件引用（scriptPath 参数）
│       └── 命名工作流（name 参数）
└── 否 → 单 Agent 直接处理
```

### 1.1 Claude 4.X 模型族

Workflow 中的 `model` 参数映射到以下 Claude 4.X 模型：

| 参数值 | 模型 ID | 说明 |
|--------|---------|------|
| `"opus"` | `claude-opus-4-8` | 最强能力，用于复杂推理和编排 |
| `"sonnet"` | `claude-sonnet-4-6` | 平衡性能与速度，subagent 默认 |
| `"haiku"` | `claude-haiku-4-5-20251001` | 最快速度，用于简单子任务 |

**Fast Mode**：Claude Code 支持 Fast mode（通过 `/fast` 切换），使用 Claude Opus 的更快输出变体，不降级到更小模型。适用于 Opus 4.8/4.7/4.6。

---

## 2. Workflow 工具完整 API 定义

### 2.1 工具参数

Workflow 工具接受以下参数（JSON Schema）：

| 参数 | 类型 | 必需 | 说明 |
|------|------|------|------|
| `script` | string | 否* | 内联工作流脚本。最大 524288 字符。必须以 `export const meta = {...}` 开头 |
| `scriptPath` | string | 否* | 工作流脚本文件路径。优先于 `script` 和 `name` |
| `name` | string | 否* | 预定义工作流名称（从 `.claude/workflows/` 注册表解析） |
| `args` | any | 否 | 传入脚本的参数，作为全局 `args` 变量。数组/对象用实际 JSON 值，**不要**用 JSON 字符串 |
| `resumeFromRunId` | string | 否 | 恢复先前运行。格式 `wf_[a-z0-9-]{6,}`。同会话内有效 |
| `description` | string | 否 | **已废弃**——在脚本的 `meta` 块中设置 |
| `title` | string | 否 | **已废弃**——在脚本的 `meta` 块中设置 |

*三者至少需要一个。优先级：`scriptPath` > `script` > `name`。

### 2.2 返回值

Workflow 工具返回：
- `runId`：运行标识符，格式 `wf_[a-z0-9-]{6,}`
- 脚本 `return` 语句的序列化值
- 如果用户跳过或 subagent 在重试后终端 API 错误时死亡，返回 `null`

### 2.3 使用示例

```javascript
// 方式 1：内联脚本
Workflow({
  script: `
    export const meta = {
      name: 'find-bugs',
      description: 'Find and verify bugs',
      phases: [{ title: 'Find' }, { title: 'Verify' }]
    }
    const bugs = await agent('Find bugs', { schema: BUGS_SCHEMA })
    return bugs
  `
})

// 方式 2：文件引用
Workflow({
  scriptPath: '/path/to/workflow.js',
  args: { targetDir: '/src' }
})

// 方式 3：恢复先前运行
Workflow({
  scriptPath: '/path/to/workflow.js',
  resumeFromRunId: 'wf_abc123'
})
```

---

## 3. 脚本 DSL 规范

### 3.1 脚本结构

每个 Workflow 脚本**必须**以 `export const meta` 块开头，后跟脚本主体：

```javascript
export const meta = {
  name: 'workflow-name',           // 必需：工作流名称
  description: 'One-line summary', // 必需：描述（显示在权限对话框中）
  phases: [                        // 可选：阶段定义（与 phase() 调用标题精确匹配）
    { title: 'Phase1', detail: 'Phase1 description' },
    { title: 'Phase2', detail: 'Phase2 description', model: 'opus' }  // model 可选：覆盖该阶段的模型
  ]
}
// 脚本主体从这里开始 — 使用 agent()/parallel()/pipeline()/phase()/log()
phase('Phase1')
const result = await agent('Do something')
// ...
return { result }
```

### 3.2 meta 验证规则

`meta` 对象**必须是纯字面量**——不允许任何计算表达式：

```javascript
// ✅ 正确
export const meta = {
  name: 'my-workflow',
  description: '描述',
  phases: [{ title: 'A', detail: 'B' }]
}

// ❌ 错误：包含变量、函数调用、模板字符串、展开运算符
export const meta = {
  name: `wf-${Date.now()}`,     // 变量 + 非确定性函数
  description: getDescription(), // 函数调用
  phases: [...phases]            // 展开运算符
}
```

**验证发生在工具调用层**——如果 meta 不是纯字面量，工具调用直接失败。

### 3.3 运行时环境

脚本在**受限 JavaScript 沙箱**中执行（非 Node.js）：

| 特性 | 状态 | 说明 |
|------|------|------|
| `export const meta` | ✅ 必需 | 纯字面量对象 |
| `await` | ✅ 支持 | 顶层 await 可用 |
| `JSON` / `Math` / `Array` | ✅ 支持 | 标准内置对象 |
| `Date.now()` | ❌ 禁用 | 破坏 resume 确定性 |
| `Math.random()` | ❌ 禁用 | 破坏 resume 确定性 |
| `new Date()`（无参） | ❌ 禁用 | 破坏 resume 确定性 |
| `fs` / `path` / `process` | ❌ 无 Node API | 沙箱环境，无文件系统访问 |

**关键设计**：禁用所有非确定性函数，确保脚本的**幂等性**——同样的输入永远产生同样的执行路径，这是 resume 机制的基础。

---

## 4. 脚本执行引擎

### 4.1 全局 API

脚本执行时，以下全局对象和函数可用：

#### `agent(prompt, opts?)` → `Promise<any>`

创建并运行一个 subagent。

**参数**：

| opts 字段 | 类型 | 说明 |
|-----------|------|------|
| `label` | string | 显示标签（覆盖进度展示中的默认标签） |
| `phase` | string | 所属阶段（在 `pipeline()`/`parallel()` 内部用于将 agent 分配到正确的进度分组） |
| `schema` | object (JSON Schema) | **强制结构化输出**：subagent 被迫调用 `StructuredOutput` 工具，返回验证后的对象。无 schema 时返回 agent 的最终文本 |
| `model` | `"sonnet"` \| `"opus"` \| `"haiku"` | 覆盖模型。默认继承主循环模型。对应 Claude 4.X 模型族：opus → claude-opus-4-8, sonnet → claude-sonnet-4-6, haiku → claude-haiku-4-5-20251001 |
| `isolation` | `"worktree"` | 创建临时 git worktree 隔离。**昂贵**（~200-500ms 设置 + 磁盘），仅用于并行 agent 修改文件时避免冲突 |
| `agentType` | string | 自定义 agent 类型（从 agent 注册表解析）。系统提示词会追加 `StructuredOutput` 指令 |
| `name` | string | agent 名称，可通过 `SendMessage({to: name})` 地址化通信 |
| `run_in_background` | boolean | 后台异步运行，完成后通过通知回调。不阻塞当前脚本执行 |
| `mode` | string | 权限模式，控制 subagent 对文件系统的访问权限 |
| `team_name` | string | 团队名称（用于团队调度）。省略时使用当前团队上下文 |

**mode 可选值详解**：

| mode 值 | 说明 |
|---------|------|
| `"default"` | 默认权限模式，遵循用户设置的全局权限 |
| `"acceptEdits"` | 自动接受文件编辑，但其他操作仍需确认 |
| `"auto"` | 自动批准大部分操作 |
| `"bypassPermissions"` | 绕过所有权限检查（最高权限） |
| `"dontAsk"` | 不询问用户，自动拒绝未授权操作 |
| `"plan"` | 仅允许规划，不允许实际修改文件 |

**返回值**：
- 无 `schema`：返回 agent 的最终文本字符串
- 有 `schema`：返回经过 JSON Schema 验证的对象（验证在工具调用层发生，模型会在不匹配时重试）
- 失败/跳过：返回 `null`

**错误处理**：
- 无 `schema` 的 agent 将最终文本作为返回值返回（非人类消息）
- 有 `schema` 的 agent 通过 `StructuredOutput` 工具强制输出
- 抛出的异常会导致该 item 的剩余阶段被跳过，resolve 为 null
- 用户中途跳过 agent 或 subagent 在重试后终端 API 错误死亡时，返回 `null`

#### `parallel(thunks)` → `Promise<any[]>`

**屏障模式**：并发运行所有 thunk，**全部完成后**才返回。

```javascript
const results = await parallel([
  () => agent('Task A'),
  () => agent('Task B'),
  () => agent('Task C'),
])
// results = [resultA, resultB, resultC]
```

**关键语义**：
- 每个 thunk 是一个返回 Promise 的函数
- 失败的 thunk resolve 为 `null`，**不会 reject**
- `parallel` 本身永远不会 reject
- 在调用 `.filter(Boolean)` 前使用结果

#### `pipeline(items, stage1, stage2, ...)` → `Promise<any[]>`

**流水线模式**：每个 item 独立流过所有阶段，**无阶段间屏障**。

```javascript
const results = await pipeline(
  ITEMS,
  item => agent(`Analyze ${item}`, { label: `analyze:${item}` }),
  (analysis, item) => agent(`Review ${item}: ${analysis}`, { label: `review:${item}` })
)
```

**关键语义**：
- Item A 可以在 stage 3 时，Item B 还在 stage 1
- 每个 stage 回调接收 `(prevResult, originalItem, index)`
- 抛出异常的 stage 会将该 item 降级为 `null`，跳过剩余 stage
- 使用 `originalItem`/`index` 在后续 stage 中标记工作，无需通过 stage 1 返回值传递上下文

#### `phase(title)` → `void`

声明当前阶段，影响进度展示分组。后续 `agent()` 调用被分组到此阶段下。

#### `log(message)` → `void`

输出进度消息到用户界面（显示为进度树上方的旁白行）。

#### `args` → `any`

Workflow 调用时传入的参数（verbatim）。通过 `Workflow({args: [...]})` 传入。

#### `budget` → `BudgetObject`

```javascript
budget: {
  total: number | null,  // 用户设置的 token 上限。null 表示无限制
  spent(): number,       // 主循环和所有 workflow 的已消耗 token（共享池）
  remaining(): number    // max(0, total - spent())，无限制时返回 Infinity
}
```

#### `workflow(nameOrRef, args?)` → `Promise<any>`

嵌套工作流调用。**限制 1 层深度**——在子工作流内部调用 `workflow()` 会抛出异常。

| 参数 | 类型 | 说明 |
|------|------|------|
| `nameOrRef` | string \| `{scriptPath: string}` | 预定义名称或脚本文件路径 |
| `args` | any | 可选参数（子工作流的 `args` 全局变量） |

**关键限制**：
- 嵌套工作流共享父级的并发上限、agent 计数器、中止信号和 token 预算
- 子工作流的 agent 在 `/workflows` 进度树中显示为 "▸ name" 组
- 子工作流的 token 计入 `budget.spent()`

### 4.2 内置工具与工具发现

Workflow 脚本内的 agent 可以使用所有会话连接的 MCP 工具。工具 schema 通过 **ToolSearch** 按需加载——deferred tools 在 `<system-reminder>` 中按名称出现，直到通过 ToolSearch 获取完整 JSON Schema 定义后才能调用。

#### ToolSearch 工具

**用途**：获取 deferred tools 的完整 schema 定义，使其可被调用。

**查询语法**：

| 查询形式 | 说明 | 示例 |
|----------|------|------|
| `select:<tool1>,<tool2>` | 精确选择，按名称直接获取 | `select:Read,Edit,Grep` |
| `<keyword1> <keyword2>` | 关键词搜索，最多返回 max_results 个匹配 | `notebook jupyter` |
| `+<keyword> <other>` | 要求名称中包含指定关键词 | `+slack send` |

**返回格式**：每个匹配的工具以 `<function>` 块返回，包含 description、name 和完整的 parameters JSON Schema。

```javascript
// Subagent 内部使用示例（由模型自动调用）
ToolSearch({ query: "select:Read,Edit", max_results: 2 })
// → 返回 Read 和 Edit 工具的完整 schema，之后即可正常调用
```

#### Skill 工具

**用途**：在主对话中执行预定义技能。技能列表在 `<system-reminder>` 中声明。

**调用规则**：
- `skill` 参数必须是 available-skills 列表中的精确名称
- 用户输入 `/<name>` 时触发对应的 skill
- 不要猜测 skill 名称——不在列表中的 skill 不存在
- 正在运行的 skill 不要重复调用
- 内置 CLI 命令（`/help`、`/clear` 等）不通过 Skill 工具调用

```javascript
// 调用示例
Skill({ skill: "godot-master", args: "optional arguments" })
```

#### 内置 agent 类型注册表

当 `agent()` 调用指定 `agentType` 时，从以下注册表解析：

| agentType 名称 | 用途 | 特殊能力 |
|----------------|------|----------|
| `claude` | 默认 catch-all 类型 | 所有工具可用 |
| `claude-code-guide` | 回答关于 Claude Code 的问题 | 仅搜索和获取，不修改文件 |
| `deep-research-synthesizer` | 深度研究与综合 | 全部工具可用，用于高风险决策和前沿话题 |
| `Explore` | 只读搜索 agent | 全部工具（除 Agent/Edit/Write），用于广泛文件扫描 |
| `file-retrieval-analyst` | 文件检索分析 | 模式匹配、内容查找、目录探索 |
| `general-purpose` | 通用 agent | 全部工具可用 |
| `Plan` | 软件架构师 | 全部工具（除 Agent/Edit/Write），用于设计实现方案 |
| `quality-validator` | 质量评估 | 全部工具可用，在工作单元完成后自动评估质量 |
| `statusline-setup` | 配置状态栏 | 仅 Read 和 Edit |

**自定义 agent 类型**：可通过 `~/.claude/agents/<name>.md` 文件定义。系统提示词会自动追加 StructuredOutput 指令。

**注意**：交互式认证的 MCP 服务器（如 claude.ai）在无头/cron 运行中可能不可用。

---

## 5. Agent 调度与通信

### 5.1 Subagent 创建流程

```
1. Workflow 引擎调用 agent(prompt, opts)
2. 生成 deterministic key：v2:SHA256(prompt + opts)
3. 检查 journal.jsonl：
   - 存在相同 key + 有 result → 返回缓存结果（resume 路径）
   - 不存在 → 继续创建
4. 分配 agentId：随机 17 位十六进制字符串
5. 写入 journal：{"type":"started", "key": "...", "agentId": "..."}
6. 创建子进程/线程（通过 Daemon 调度）
7. 初始化 Subagent 上下文：
   a. 发送 user 消息（包含 prompt）
   b. 发送 deferred_tools_delta（可用工具列表）
   c. 发送 skill_listing（可用技能列表）
   d. 如果有 schema，追加 StructuredOutput 指令
8. Subagent 开始执行，独立调用工具
9. Subagent 完成后，结果返回给 Workflow 引擎
10. 写入 journal：{"type":"result", "key": "...", "agentId": "...", "result": ...}
```

### 5.2 Subagent 消息格式

每个 `agent-*.jsonl` 文件是完整的对话记录：

```json
// 1. 初始用户消息（系统注入的 prompt）
{
  "type": "user",
  "isSidechain": true,
  "agentId": "ac8876ca14c724d5c",
  "promptId": "abc487f0-5376-409d-83c7-a19996230741",
  "message": { "role": "user", "content": "请分析..." }
}

// 2. 工具增量消息（可用工具列表）
{
  "type": "attachment",
  "attachment": { "type": "deferred_tools_delta", "addedNames": ["Read", "Edit", ...] }
}

// 3. 技能列表消息
{
  "type": "attachment",
  "attachment": { "type": "skill_listing", "content": "...", "names": [...] }
}

// 4. Assistant 响应（模型输出）
{
  "type": "assistant",
  "attributionAgent": "workflow-subagent",
  "message": {
    "role": "assistant",
    "content": [{ "type": "tool_use", "name": "Read", "input": {...} }]
  },
  "model": "claude-sonnet-4-6"
}

// 5. 工具结果消息
{
  "type": "user",
  "message": { "role": "user", "content": [{ "type": "tool_result", ... }] }
}
```

### 5.3 关键字段解析

| 字段 | 含义 |
|------|------|
| `isSidechain: true` | 标记为侧链/旁路 agent，区别于主对话线程 |
| `promptId` | 同一个 workflow 的所有 subagent 共享相同的 promptId |
| `parentUuid` | 父消息的 UUID，形成消息链（首条为 null） |
| `attributionAgent: "workflow-subagent"` | 标记消息来源为 workflow 子代理 |
| `sourceToolAssistantUUID` | 触发此消息的工具调用来源 |

### 5.4 上下文边界

**Subagent 不继承主 Agent 的完整对话历史**。它接收：
- ✅ 系统提示词（包含工具定义、规则）
- ✅ 自己的 prompt（由 workflow 脚本注入）
- ✅ 可用工具列表（deferred_tools_delta）
- ✅ 可用技能列表（skill_listing）
- ✅ 如果有 schema，追加 StructuredOutput 指令
- ❌ 主 Agent 的用户对话历史
- ❌ 其他 subagent 的内部状态

**这意味着**：Subagent 是**无状态**的，所有上下文必须通过 prompt 显式传递。

### 5.5 Agent 间通信（SendMessage）

当 `agent()` 调用指定了 `name` 参数时，该 subagent 变为**可地址化**——其他 agent 可以通过 `SendMessage({to: name})` 向其发送消息。这允许构建多 agent 协作模式：

```javascript
// 创建可地址化的 agent
agent('Worker agent', { name: 'worker-1', run_in_background: true })

// 其他 agent 可以通过 SendMessage 与 worker-1 通信
// SendMessage({to: 'worker-1', message: '...'})
```

### 5.6 批量并发启动

当需要独立运行多个 agent 时，可以在**单个消息**中发送多个工具调用以实现并发（无需 `parallel()` 包装）。这是 Claude Code harness 的优化——同一消息中的多个 `agent()` 调用会自动并发执行。

### 5.7 StructuredOutput 机制

当 `agent()` 调用指定了 `schema` 参数时：

1. 引擎将 JSON Schema 追加到 subagent 的系统提示词
2. Subagent 被**强制**调用 `StructuredOutput` 工具输出结构化数据
3. **验证发生在工具调用层**——如果输出不符合 schema，模型会自动重试
4. 返回给调用方的是经过验证的 JavaScript 对象（非 JSON 字符串）

```javascript
const BUGS_SCHEMA = {
  type: 'object',
  properties: {
    bugs: {
      type: 'array',
      items: {
        type: 'object',
        properties: {
          file: { type: 'string' },
          line: { type: 'number' },
          description: { type: 'string' },
          severity: { type: 'string', enum: ['critical', 'high', 'medium', 'low'] }
        },
        required: ['file', 'line', 'description']
      }
    }
  },
  required: ['bugs']
}

const result = await agent('Find all bugs', { schema: BUGS_SCHEMA })
// result 已经是 { bugs: [...] } 对象，无需 JSON.parse
```

---

## 6. 并发控制

### 6.1 并发上限

```javascript
// 每个 workflow 实例的并发限制
并发上限 = min(16, cpu_cores - 2)
总 Agent 数上限 = 1000（防失控，每个 workflow 生命周期内）

// 单次 parallel()/pipeline() 调用的项目数限制
单次调用最大 items = 4096
```

### 6.2 队列机制

当并行 agent 数量超过上限时：

```
并行请求: [A, B, C, D, E, F, G, H, I, J]  // 10 个
并发槽位: [_, _, _, _, _, _, _, _, _, _, _, _, _, _, _, _]  // 16 个

情况 1：16 个空槽位
  → 同时启动所有 10 个 agent

情况 2：已有 14 个运行中
  → 启动 2 个（A, B）
  → C, D, E... 进入队列等待
  → 每当一个 agent 完成，从队列取下一个启动
```

### 6.3 Pipeline vs Parallel 的并发差异

| 模式 | 并发特征 | 适用场景 |
|------|---------|---------|
| `parallel()` | 真正屏障：所有任务同时启动（受槽位限制），全部完成后才返回 | 需要所有结果一起进行下一步 |
| `pipeline()` | 伪流水线：item A 可以在 stage 3 时，item B 还在 stage 1 | 任务之间无交叉依赖 |

**选择原则**：默认使用 `pipeline()`。只有当 stage N 确实需要所有 stage N-1 结果一起进行时，才使用屏障。

**何时屏障是正确的**：
- 跨所有结果的去重/合并后再进行昂贵下游工作
- 如果总数为零则提前退出（"0 bugs found → 跳过验证"）
- Stage N 的提示词引用"其他发现"进行比较

**何时屏障是错误的**：
- "我需要先 flatten/map/filter"——在 pipeline stage 内部做
- "stage 概念上是分开的"——分开的 stage ≠ 同步的 stage
- "代码更整洁"——屏障延迟是真实的

---

## 7. Journal 与 Resume 机制

### 7.1 Journal 文件结构

```jsonl
{"type":"started","key":"v2:sha256...","agentId":"ae9e8925c751caf74"}
{"type":"started","key":"v2:sha256...","agentId":"a9471c8cd11c4a4f4"}
{"type":"result","key":"v2:sha256...","agentId":"ae9e8925c751caf74","result":"..."}
```

### 7.2 Key 的生成规则

`key` 格式为 `v2:` + SHA256 哈希，输入包括：
- Prompt 文本
- Options（label, phase, schema, model 等）
- **不包含**：当前时间、随机数、外部状态

```
v2:48465b6e8f291f955e565826632699bcfafbfc207b17af94a5e1bf92d0ee22ce
└┬┘└──────────────────────────────┬──────────────────────────────┘
  │                                └── SHA256(prompt + options)
  └── 协议版本
```

### 7.3 Resume 工作原理

```javascript
// 调用方式
Workflow({
  scriptPath: '/path/to/workflow.js',
  resumeFromRunId: 'wf_abc123'  // 先前运行的 runId
})
```

**执行流程**：

```
1. 重新解析脚本
2. 对每个 agent() 调用，计算 key
3. 查询 journal.jsonl：
   ├─ key 匹配 + 有 result → 直接返回缓存结果（不创建 subagent）
   └─ key 不匹配 或 无 result → 创建新 subagent
4. 只执行新增/修改的 agent() 调用
```

**缓存命中规则**：已完成的 `agent()` 调用，如果 prompt 和 opts 完全不变，返回缓存结果**即时**（~0ms）。

**最长不变前缀优化**：Resume 时，脚本中**最长的连续不变的 agent() 调用前缀**会返回缓存结果——第一个被编辑/新增的 agent() 调用及之后的所有调用会重新执行。这确保了编辑脚本尾部时，前面的工作不会被重复。

```javascript
// 原始脚本
agent('A')  // key: v2:aaa
agent('B')  // key: v2:bbb
agent('C')  // key: v2:ccc

// 编辑后（只改了 C）
agent('A')  // key: v2:aaa → ✅ 缓存命中（前缀不变）
agent('B')  // key: v2:bbb → ✅ 缓存命中（前缀不变）
agent('C2') // key: v2:xxx → 🆕 重新执行（前缀断裂点）
```

### 7.4 为什么禁用非确定性函数

如果脚本包含 `Date.now()` / `Math.random()` / 无参 `new Date()`：
- 每次执行生成不同的 key
- 缓存永远失效
- Resume 退化为全量重新执行

**设计决策**：脚本必须是**纯函数**——同样的输入永远产生同样的执行路径。

### 7.5 Resume 实际行为

```javascript
// 第一次执行
agent('任务 A') → key: v2:abc... → journal 无记录 → 创建 agent → 执行 → 写入 result
agent('任务 B') → key: v2:def... → journal 无记录 → 创建 agent → 执行 → 写入 result

// 用户编辑脚本，重新执行（resumeFromRunId）
agent('任务 A') → key: v2:abc... → journal 有记录 → ✅ 直接返回缓存
agent('任务 B 修改版') → key: v2:xyz... → journal 无记录 → 🆕 创建新 agent
```

### 7.6 Resume 回退机制（Journal 不可用时）

当 journal 文件不可用（损坏、丢失、跨会话）时，系统提供回退路径：

1. **读取 transcript 文件**：从会话目录读取 `agent-*.jsonl` 文件（完整的 subagent 对话记录）
2. **手动重建上下文**：从 jsonl 中提取 prompt、opts 和 result
3. **手写 continuation 脚本**：基于提取的数据编写恢复脚本

**时间戳传递**：由于沙箱中 `Date.now()` 不可用，时间戳必须通过 `args` 传入：

```javascript
// 调用时传入时间戳
Workflow({
  scriptPath: '/path/to/workflow.js',
  args: { timestamp: Date.now() }  // 在脚本外部获取时间
})

// 脚本内部使用 args.timestamp
log(`Started at ${args.timestamp}`)
```

---

## 8. Budget 预算系统

### 8.1 Budget 对象

```javascript
budget: {
  total: number | null,  // 用户通过 "+500k" 指令设置。null = 未设置目标（无限）
  spent(): number,       // 主循环 + 所有 workflow 的已消耗 output tokens（共享池）
  remaining(): number    // max(0, total - spent())，未设置目标时返回 Infinity
}
```

**关键语义**：
- `budget.total` 为 `null` 表示用户**未设置** token 目标——`remaining()` 返回 `Infinity`
- `spent()` 返回的是 **output tokens**（非 input tokens），跨主循环和所有 workflow 共享
- 预算是**共享池**——子工作流的 token 消耗直接计入父级的 `spent()`

### 8.2 Budget 约束

- `budget.total` 是**硬上限**，不是建议
- 一旦 `spent()` 达到 `total`，后续 `agent()` 调用**抛出异常**
- 子工作流共享父级预算
- 预算跨主循环和所有 workflow 共享

### 8.3 动态循环模式

```javascript
// Loop-until-budget：根据剩余预算动态调整工作量
const bugs = []
while (budget.total && budget.remaining() > 50_000) {
  const result = await agent('Find bugs in this codebase.', { schema: BUGS_SCHEMA })
  bugs.push(...result.bugs)
  log(`${bugs.length} found, ${Math.round(budget.remaining()/1000)}k remaining`)
}
```

**注意**：无 `budget.total` 时 `remaining()` 返回 `Infinity`，循环会运行到 1000 agent 上限。

### 8.4 预算缩放

```javascript
// 根据总预算动态调整并发数
const FLEET = budget.total ? Math.floor(budget.total / 100_000) : 5
```

---

## 9. Pipeline 与 Parallel 语义详解

### 9.1 Pipeline 详解

```javascript
// 基本形式
const results = await pipeline(
  items,           // 输入数组
  stage1Fn,        // (prevResult, originalItem, index) → any
  stage2Fn,        // (prevResult, originalItem, index) → any
  stage3Fn         // (prevResult, originalItem, index) → any
)
```

**执行模型**：
```
时间 →
Item A: [stage1]──→[stage2]──→[stage3]
Item B:    [stage1]──→[stage2]──→[stage3]
Item C:       [stage1]──→[stage2]──→[stage3]
              ↑ 无屏障，最大化并发
```

**回调参数语义**：
- `prevResult`：上一个 stage 的返回值
- `originalItem`：原始输入 item（不变）
- `index`：item 在原始数组中的索引

### 9.2 Parallel 详解

```javascript
const results = await parallel([
  () => agent('Task A'),
  () => agent('Task B'),
  () => agent('Task C'),
])
// 所有完成后才返回
```

**执行模型**：
```
时间 →
Task A: [agent]──────────────→ ✓
Task B: [agent]────────→ ✓
Task C: [agent]──────────────────→ ✓
                                ↑ 屏障：全部完成后返回
```

### 9.3 组合模式

```javascript
// Pipeline + 内部 Parallel：维度审查 + 并行验证
const results = await pipeline(
  DIMENSIONS,
  d => agent(d.prompt, { schema: FINDINGS_SCHEMA }),  // stage 1
  review => parallel(                                    // stage 2
    review.findings.map(f => () =>
      agent(`Verify: ${f.title}`, { schema: VERDICT_SCHEMA })
    )
  )
)
// 维度 'bugs' 的发现可以在维度 'perf' 还在审查时完成验证
```

### 9.4 错误处理对比

| 场景 | Pipeline | Parallel |
|------|----------|----------|
| 单个 agent 失败 | 该 item 剩余 stage 跳过，resolve 为 null | 该 thunk resolve 为 null |
| 整体结果 | 失败的 item 为 null | 失败的 thunk 为 null |
| 是否 reject | 否 | 否 |
| 过滤方式 | `.filter(Boolean)` | `.filter(Boolean)` |

---

## 10. 质量模式（Quality Patterns）

系统提示词定义了多种可组合的质量保证模式：

### 10.1 Adversarial Verify（对抗验证）

为每个发现生成 N 个独立怀疑者，每个被提示**反驳**。如果多数反驳成功则杀死该发现。

```javascript
const votes = await parallel(
  Array.from({length: 3}, () => () =>
    agent(`Try to refute: ${claim}. Default to refuted=true if uncertain.`, {
      schema: VERDICT_SCHEMA
    })
  )
)
const survives = votes.filter(Boolean).filter(v => !v.refuted).length >= 2
```

### 10.2 Perspective-Diverse Verify（视角多样化验证）

当一个发现可能以多种方式失败时，给每个验证者不同的视角（correctness、security、perf、repro），而非 N 个相同的反驳者。

```javascript
const judged = await parallel(
  fresh.map(b => () =>
    parallel(
      ['correctness', 'security', 'repro'].map(lens => () =>
        agent(`Judge "${b.desc}" via the ${lens} lens — real?`, {
          phase: 'Verify',
          schema: VERDICT
        })
      )
    ).then(vs => ({
      b,
      real: vs.filter(Boolean).filter(v => v.real).length >= 2
    }))
  )
)
```

### 10.3 Judge Panel（评委小组）

从不同角度生成 N 个独立尝试（如 MVP-first、risk-first、user-first），用并行评委评分，从获胜者合成并移植亚军的最佳想法。

### 10.4 Loop-Until-Dry（循环直到枯竭）

用于未知大小的发现（bug、问题、边缘情况），持续生成直到连续 K 轮无新发现。

```javascript
const seen = new Set(), confirmed = []
let dry = 0
while (dry < 2) {
  const found = (await parallel(
    FINDERS.map(f => () => agent(f.prompt, { schema: BUGS }))
  )).filter(Boolean).flatMap(r => r.bugs)

  const fresh = found.filter(b => !seen.has(key(b)))
  if (!fresh.length) { dry++; continue }
  dry = 0
  fresh.forEach(b => seen.add(key(b)))
  // ... 验证逻辑
}
```

**去重必须对比 `seen`，非 `confirmed`**——否则被评委拒绝的发现每轮重新出现，永不收敛。

### 10.5 Multi-Modal Sweep（多模态扫描）

并行 agent 各用不同方式搜索（by-container、by-content、by-entity、by-time）。每个对其他结果盲区可见，适用于单角度无法找到所有内容的场景。

### 10.6 Completeness Critic（完整性评论家）

最终 agent 询问"什么遗漏了——未运行的模态、未验证的声明、未读的来源？"发现的内容成为下一轮工作。

### 10.7 No Silent Caps（不静默截断）

如果 workflow 绑定了覆盖率限制（top-N、no-retry、采样），必须用 `log()` 记录被丢弃的内容——静默截断读作"覆盖了全部"，实际并未。

---

## 11. 故障处理与 Stall 检测

### 11.1 Agent 失败模式

| 失败类型 | 表现 | 处理方式 |
|---------|------|---------|
| **工具调用失败** | `API Error: 400 Invalid request Error` | resolve 为 null，继续执行 |
| **模型错误** | 模型返回错误响应 | resolve 为 null，继续执行 |
| **异常抛出** | 脚本运行时错误 | 该 item 的剩余阶段被跳过，resolve 为 null |
| **Agent 死亡** | 终端 API 错误，重试后仍失败 | 返回 null |

```javascript
// 容错语义
const results = await parallel([
  () => agent('任务 A'),  // 如果失败 → results[0] = null
  () => agent('任务 B'),  // 如果失败 → results[1] = null
])
// parallel 本身不会 reject，失败的 thunk resolve 为 null
```

### 11.2 Stall 检测机制

Workflow 引擎**自动监控**每个 agent 的进展状态：

```
[stall] agent "integration" stalled (no progress) after 13713s — retrying (1/5)
[stall] agent "integration" stalled (no progress) after 22059s — retrying (2/5)
```

**关键行为**：
- 当 agent 长时间无进展时，引擎标记为 `stalled`
- **自动重试**：最多重试 5 次
- 重试间隔递增（非固定间隔）
- 5 次重试后仍然卡住，workflow 失败

### 11.3 sourcesStatus 与 API Error 的关系

```
sourcesStatus: "success"     // 附件加载状态
result: "API Error: 400"     // API 调用结果
```

| 字段 | 含义 | 何时为 success |
|------|------|---------------|
| `sourcesStatus` | **附件加载状态** | deferred_tools_delta、skill_listing 等附件成功附加到 subagent 会话 |
| `result` | **API 调用结果** | API 调用成功返回内容，或 API 错误被捕获为字符串 |

**结论**：`sourcesStatus: "success"` ≠ "subagent 执行成功"。

---

## 12. 嵌套工作流

### 12.1 基本用法

```javascript
// 在 workflow 脚本中调用另一个 workflow
const childResult = await workflow('child-workflow-name', { param: 'value' })

// 或通过脚本路径
const childResult = await workflow({ scriptPath: '/path/to/child.js' }, { param: 'value' })
```

### 12.2 限制

- **深度限制**：最多 1 层嵌套。在子工作流内部调用 `workflow()` 会**抛出异常**
- **资源共享**：子工作流共享父级的并发上限、agent 计数器、中止信号和 token 预算
- **进度显示**：子工作流的 agent 在进度树中显示为 "▸ name" 组
- **错误传播**：子工作流的错误会向上传播

### 12.3 嵌套工作流的 args 传递

```javascript
// 父工作流
export const meta = {
  name: 'parent',
  description: 'Parent workflow',
  phases: [{ title: 'Main' }]
}

const childResult = await workflow('child', { target: '/src' })
// args 在子工作流中作为全局变量可用

// 子工作流
export const meta = {
  name: 'child',
  description: 'Child workflow'
}

// args.target === '/src'
const analysis = await agent(`Analyze ${args.target}`)
return { analysis }
```

---

## 13. 隔离层级详解

```
Level 1: 进程隔离
  ├── 每个 worker 独立 OS 进程 (PID)
  ├── 每个 subagent 独立 agentId
  └── 崩溃不影响其他 worker

Level 2: 管道隔离 (Windows Named Pipe)
  ├── 命名规则: cc-daemon-{pipeKey}-{type}-{shortId}
  ├── rendezvous pipe: 控制信令隔离
  └── pty pipe: 终端 I/O 隔离

Level 3: 会话隔离
  ├── 每个 worker 独立 session.jsonl 持久化文件
  ├── 不同 worker 可配置不同权限模式
  └── 不同 worker 可配置不同 flagArgs

Level 4: 上下文隔离 (isolation 字段)
  ├── "none": 无隔离，继承 supervisor 环境（默认）
  ├── "worktree": Git worktree 隔离（agent opts 中设置）
  │   ├── 创建临时 git worktree
  │   ├── agent 在隔离目录中工作
  │   └── 完成后自动清理（如果未更改）
  └── "full": 完全沙箱隔离（当前未启用）
```

**worktree 隔离的代价**：
- ~200-500ms 设置时间
- 额外磁盘空间
- 仅在多个 agent 并行修改文件时需要

---

## 14. Daemon 进程管理

### 14.1 Roster 文件结构

```json
{
  "proto": 1,
  "supervisorPid": 29404,
  "updatedAt": 1780254970947,
  "workers": {
    "61a6219f": {
      "pid": 22712,
      "sessionId": "61a6219f-1a4c-4222-9b3a-8d32bb5312e2",
      "rendezvousSock": "\\\\.\\pipe\\cc-daemon-8b9d27474b62d75e-rv-61a6219f",
      "ptySock": "\\\\.\\pipe\\cc-daemon-8b9d27474b62d75e-pty-61a6219f",
      "cliVersion": "2.1.167",
      "dispatch": {
        "launch": {
          "mode": "resume",
          "sessionId": "...jsonl",
          "fork": true,
          "flagArgs": ["--effort", "xhigh", "--permission-mode", "acceptEdits"]
        },
        "isolation": "none"
      }
    }
  }
}
```

### 14.2 关键组件

| 组件 | 说明 |
|------|------|
| `supervisorPid` | Daemon 监控进程 PID，负责 worker 生命周期管理 |
| `rendezvousSock` | **会合套接字**（Named Pipe），用于 worker 向 daemon 注册和心跳 |
| `ptySock` | **PTY 套接字**（Named Pipe），用于 daemon 向 worker 发送用户输入和接收输出 |
| `fork: true` | Worker 以 fork 模式启动，继承父进程的部分状态 |
| `isolation` | 隔离级别：`none`（共享工作区）或 `worktree`（Git worktree 隔离） |

### 14.3 通信协议

Daemon 与 Worker 之间使用**自定义二进制协议**通过 Named Pipe 通信：
- **RV Pipe**：Worker → Daemon（注册、状态上报）
- **PTY Pipe**：Daemon → Worker（用户输入、控制命令）
- **协议版本**：`proto: 1`

### 14.4 当前状态观察

从实际运行日志观察：
- Daemon 不一定持续运行——可能按需启动
- 交互式会话可以直接运行（不经过 Daemon）
- Daemon 主要用于后台任务管理

---

## 15. 关键文件路径映射

| 文件类型 | 路径示例 | 用途 |
|----------|----------|------|
| 工作流脚本 | `.../<session>/workflows/scripts/<name>-wf_<hash>.js` | 可执行脚本 |
| 工作流日志 | `.../<session>/subagents/workflows/wf_<hash>/journal.jsonl` | 执行事件记录 |
| Subagent 对话 | `.../<session>/subagents/workflows/wf_<hash>/agent-<id>.jsonl` | 消息历史 |
| Subagent 元数据 | `.../<session>/subagents/workflows/wf_<hash>/agent-<id>.meta.json` | agent 类型 |
| Daemon 注册表 | `~/.claude/daemon/roster.json` | Worker 进程注册表 |
| Daemon 密钥 | `~/.claude/daemon/pipe.key` | 命名管道实例标识 |
| Worker PID | `~/.claude/daemon/pty-pids/<shortId>.pid` | 进程 ID 文件 |
| 会话持久化 | `~/.claude/projects/.../<sessionId>.jsonl` | 主 Agent 对话历史 |
| Agent 定义 | `~/.claude/agents/<name>.md` | 自定义 agent 类型 |
| 计划文件 | `~/.claude/plans/<name>.md` | 执行计划 |
| 团队配置 | `~/.claude/teams/<teamId>/` | 团队任务和收件箱 |

---

## 16. 实现指南：如何复刻此系统

### 16.1 核心组件

要实现一个类似的 Workflow 系统，需要以下核心组件：

```
┌──────────────────────────────────────────────────────────────┐
│                        Workflow Engine                        │
│  ┌─────────────┐  ┌─────────────┐  ┌──────────────────────┐ │
│  │ Script      │  │ Agent       │  │ Journal              │ │
│  │ Executor    │  │ Spawner     │  │ Manager              │ │
│  └─────────────┘  └─────────────┘  └──────────────────────┘ │
│  ┌─────────────┐  ┌─────────────┐  ┌──────────────────────┐ │
│  │ Budget      │  │ Concurrency │  │ Resume               │ │
│  │ Tracker     │  │ Controller  │  │ Engine               │ │
│  └─────────────┘  └─────────────┘  └──────────────────────┘ │
│  ┌─────────────┐  ┌─────────────┐  ┌──────────────────────┐ │
│  │ ToolSearch  │  │ Skill       │  │ Agent Type           │ │
│  │ Resolver    │  │ Dispatcher  │  │ Registry             │ │
│  └─────────────┘  └─────────────┘  └──────────────────────┘ │
└──────────────────────────────────────────────────────────────┘
```

### 16.2 实现步骤

#### 步骤 1：脚本沙箱

```typescript
interface WorkflowSandbox {
  // 全局对象
  agent: (prompt: string, opts?: AgentOpts) => Promise<any>
  parallel: (thunks: Array<() => Promise<any>>) => Promise<any[]>
  pipeline: (items: any[], ...stages: Function[]) => Promise<any[]>
  phase: (title: string) => void
  log: (message: string) => void
  args: any
  budget: BudgetObject
  workflow: (nameOrRef: string | {scriptPath: string}, args?: any) => Promise<any>

  // 内置对象
  JSON: typeof JSON
  Math: typeof Math
  Array: typeof Array

  // 禁用项
  // Date.now, Math.random, new Date(), fs, path, process
}
```

**关键**：沙箱必须禁用所有非确定性函数。

#### 步骤 2：Deterministic Key 生成

```typescript
function generateKey(prompt: string, opts: AgentOpts): string {
  const input = JSON.stringify({ prompt, opts })
  const hash = sha256(input)
  return `v2:${hash}`
}
```

#### 步骤 3：Journal 管理

```typescript
interface JournalEntry {
  type: 'started' | 'result'
  key: string
  agentId: string
  result?: any
}

class JournalManager {
  private entries: Map<string, JournalEntry> = new Map()

  hasResult(key: string): boolean
  getResult(key: string): any | null
  recordStart(key: string, agentId: string): void
  recordResult(key: string, agentId: string, result: any): void
  persist(): void  // 写入 journal.jsonl
  load(path: string): void  // 从 journal.jsonl 加载
}
```

#### 步骤 4：Agent Spawner

```typescript
class AgentSpawner {
  private activeAgents: Map<string, AgentInstance> = new Map()
  private queue: Array<() => Promise<void>> = []
  private maxConcurrency: number  // min(16, cpuCores - 2)
  private totalAgentCount: number = 0
  private maxTotalAgents: number = 1000

  async spawn(prompt: string, opts: AgentOpts): Promise<any> {
    if (this.totalAgentCount >= this.maxTotalAgents) {
      throw new Error('Agent lifetime cap reached')
    }

    return new Promise((resolve, reject) => {
      const task = () => this.executeAgent(prompt, opts, resolve)
      if (this.activeAgents.size < this.maxConcurrency) {
        task()
      } else {
        this.queue.push(task)
      }
    })
  }

  private async executeAgent(prompt, opts, resolve) {
    // 1. 生成 key
    // 2. 检查 journal 缓存
    // 3. 创建 subagent
    // 4. 等待完成
    // 5. 写入 journal
    // 6. resolve 结果
    // 7. 从队列取下一个任务
  }
}
```

#### 步骤 5：Pipeline 和 Parallel 实现

```typescript
// Parallel - 屏障模式
async function parallel(thunks: Array<() => Promise<any>>): Promise<any[]> {
  const promises = thunks.map(thunk =>
    thunk().catch(() => null)  // 失败 resolve 为 null
  )
  return Promise.all(promises)
}

// Pipeline - 流水线模式
async function pipeline(items: any[], ...stages: Function[]): Promise<any[]> {
  return Promise.all(
    items.map(async (item, index) => {
      let result = item
      for (const stage of stages) {
        try {
          result = await stage(result, item, index)
        } catch {
          return null  // 失败跳过剩余 stage
        }
      }
      return result
    })
  )
}
```

#### 步骤 6：Budget 追踪

```typescript
class BudgetTracker {
  total: number | null
  private spentTokens: number = 0

  spent(): number { return this.spentTokens }
  remaining(): number {
    if (this.total === null) return Infinity
    return Math.max(0, this.total - this.spentTokens)
  }

  consume(tokens: number): void {
    this.spentTokens += tokens
    if (this.total !== null && this.spentTokens >= this.total) {
      throw new Error('Budget exhausted')
    }
  }
}
```

#### 步骤 7：Resume 引擎

```typescript
class ResumeEngine {
  async resume(
    scriptPath: string,
    runId: string,
    journalPath: string
  ): Promise<any> {
    const journal = await JournalManager.load(journalPath)
    const script = await loadScript(scriptPath)

    // 用包装过的 agent() 替换原始 agent()
    // 包装版本：计算 key → 检查缓存 → 命中则返回缓存，未命中则创建新 agent
    const wrappedAgent = createCachedAgent(journal)

    // 执行脚本，使用包装后的 agent
    return executeScript(script, { agent: wrappedAgent })
  }
}
```

#### 步骤 8：ToolSearch 解析器

```typescript
class ToolSearchResolver {
  private toolRegistry: Map<string, ToolDefinition>  // 所有可用工具

  // 解析查询并返回匹配工具的完整 schema
  search(query: string, maxResults: number): ToolDefinition[] {
    if (query.startsWith('select:')) {
      // 精确选择模式：select:Read,Edit,Grep
      const names = query.slice(7).split(',')
      return names.map(n => this.toolRegistry.get(n)).filter(Boolean)
    }
    if (query.startsWith('+')) {
      // 名称必需模式：+slack send
      const [required, ...rest] = query.slice(1).split(' ')
      return this.searchByKeywords(rest, { nameMustContain: required }, maxResults)
    }
    // 关键词搜索模式
    return this.searchByKeywords(query.split(' '), {}, maxResults)
  }
}
```

#### 步骤 9：Skill 调度器

```typescript
class SkillDispatcher {
  private registeredSkills: Map<string, SkillDefinition>

  async invoke(skillName: string, args?: string): Promise<any> {
    const skill = this.registeredSkills.get(skillName)
    if (!skill) throw new Error(`Unknown skill: ${skillName}`)
    if (skill.isRunning) throw new Error(`Skill already running: ${skillName}`)

    // 加载 skill 定义并执行
    return skill.execute(args)
  }
}
```

#### 步骤 10：Agent 类型注册表

```typescript
class AgentTypeRegistry {
  private types: Map<string, AgentTypeDefinition> = new Map([
    ['claude', { tools: '*', description: 'Default catch-all' }],
    ['Explore', { tools: '*,-Agent,-Edit,-Write', description: 'Read-only search' }],
    ['Plan', { tools: '*,-Agent,-Edit,-Write', description: 'Architecture planning' }],
    ['deep-research-synthesizer', { tools: '*', description: 'Deep research' }],
    ['file-retrieval-analyst', { tools: 'Glob,Grep,Read,WebFetch,WebSearch,...' }],
    ['quality-validator', { tools: '*', description: 'Quality assessment' }],
    // ... 其他类型
  ])

  // 从 ~/.claude/agents/<name>.md 加载自定义类型
  async loadCustom(name: string): Promise<AgentTypeDefinition> {
    const md = await fs.readFile(`~/.claude/agents/${name}.md`, 'utf-8')
    return this.parseAgentDefinition(md)
  }
}
```

### 16.3 实现注意事项

1. **确定性是核心**：所有非确定性函数必须禁用，否则 resume 无法工作
2. **并发控制必须精确**：超出限制会导致资源耗尽
3. **错误处理必须优雅**：失败的 agent 不能阻塞整个 workflow
4. **Journal 必须持久化**：写入文件，而非内存
5. **Key 必须稳定**：相同输入永远生成相同 key
6. **Budget 是硬限制**：超出预算必须立即停止
7. **嵌套限制必须强制**：防止无限递归
8. **ToolSearch 延迟加载**：deferred tools 必须先通过 ToolSearch 获取 schema 才能调用——实现时需要维护一个 pending 工具队列
9. **Agent 类型隔离**：不同 agentType 有不同的工具访问权限，实现时需要在 subagent 初始化时过滤工具列表
10. **批量并发优化**：同一消息中的多个 agent() 调用应自动并发执行，无需等待 parallel() 屏障
11. **SendMessage 路由**：需要维护 name → agentId 的映射表，支持跨 agent 消息路由
12. **mode 权限传播**：subagent 的 mode 设置需要传递给其调用的所有工具，影响文件系统访问权限

---

## 17. 已知问题与限制

### 17.1 非 Claude 模型的兼容性问题

当使用非 Claude 模型（如 kimi-for-coding）作为 subagent 时：

| 层级 | 问题 | 详细说明 |
|------|------|---------|
| **工具 schema** | 格式不兼容 | 非 Claude 模型对工具的请求体格式与 Claude Code harness 预期不匹配 |
| **系统提示词** | 解析偏差 | subagent 接收的系统提示词（100+ 工具定义、复杂编排规则）是面向 Claude 优化的 |
| **Token 窗口** | 提示词过大 | Subagent 初始化时需接收完整的 deferred_tools_delta + skill_listing，约 9k-10k tokens |
| **上下文理解** | 角色混淆 | `isSidechain: true` 的语义可能未被正确理解 |

**解决方案**：
```javascript
agent('任务', { model: 'sonnet' })  // 强制使用 Claude 模型
```

### 17.2 已知限制

1. **脚本大小限制**：内联脚本最大 524288 字符
2. **并行项数限制**：单次 `parallel()`/`pipeline()` 最多 4096 项
3. **Agent 生命周期上限**：每个 workflow 最多 1000 个 agent
4. **嵌套深度**：最多 1 层嵌套工作流
5. **Resume 范围**：仅当前会话内有效
6. **确定性约束**：脚本中不能使用 `Date.now()`、`Math.random()` 等
7. **无文件系统访问**：沙箱内无 `fs`、`path`、`process` 等 Node API
8. **MCP 工具可用性**：交互式认证的 MCP 服务器在无头运行中可能不可用
9. **ToolSearch 延迟加载**：deferred tools 必须先通过 ToolSearch 获取 schema 才能调用，不能直接使用名称调用
10. **Skill 唯一性**：同一时间不能运行同名的多个 skill 实例
11. **agentType 自定义限制**：自定义 agent 类型通过 `.md` 文件定义，其系统提示词会自动追加 StructuredOutput 指令

### 17.3 平台可用性

Dynamic Workflow 自 v2.1.154（2026-05-28）起以**研究预览**形式提供：

| 平台 | 可用性 | 计划要求 |
|------|--------|---------|
| Claude Code CLI | ✅ | Max / Team / Enterprise |
| Claude Code Desktop App | ✅ | Max / Team / Enterprise |
| VS Code 扩展 | ✅ | Max / Team / Enterprise |
| Claude API | ✅ | 直接 API 调用 |
| Amazon Bedrock | ✅ | AWS 账户 |
| Vertex AI | ✅ | GCP 账户 |
| Microsoft Foundry | ✅ | Azure 账户 |

**注意**：从 2026-06-15 起，Agent SDK 和 `claude -p` 的使用将从订阅计划中独立的月度 Agent SDK 额度扣除。

### 17.4 待解之谜

1. **Workflow 脚本引擎的具体实现**：是 QuickJS、V8 Lite 还是自定义解释器？
2. **Agent 之间的实际 IPC 机制**：subagent 是进程、线程还是协程？
3. **Named Pipe 协议细节**：Daemon 与 Worker 之间的二进制消息格式是什么？
4. **缓存持久化策略**：journal 缓存保留多久？跨会话是否有效？
5. **StructuredOutput 工具的内部实现**：如何在工具调用层验证 JSON Schema？
6. **批量并发优化**：同一消息中多个 agent() 调用的具体调度算法是什么？
7. **研究预览限制**：Dynamic Workflow 作为研究预览功能，API 和行为可能在后续版本中变更
8. **多平台一致性**：CLI、Desktop、VS Code 扩展、API 之间的 Workflow 行为是否完全一致？

---

## 18. Codex 实现路线图

本节为 Codex 开发者提供具体的实现路径，将 Claude Code Workflow 的核心能力移植到 Codex 平台。

### 18.1 实现策略选择

| 策略 | 说明 | 适用场景 |
|------|------|---------|
| **A. 原生复刻** | 在 Codex 内实现完整的 JS 沙箱 + Journal + Resume | Codex 支持自定义工具和脚本执行 |
| **B. 指令层模拟** | 通过 Skill/Plugin 指令让 Codex 按 Workflow 模式行动 | Codex 不支持自定义脚本引擎 |
| **C. 混合方案** | 核心编排用原生代码，DSL 用指令层解释 | 平衡实现复杂度与功能完整性 |

**推荐**：如果 Codex 支持自定义工具（如 `multi_tool_use.parallel`），采用策略 B 或 C；如果需要完整的确定性 Resume，采用策略 A。

### 18.2 最小可行实现（MVP）

实现以下核心功能即可覆盖 80% 的使用场景：

```
Phase 1: 编排层
├── meta 块解析（name, description, phases）
├── agent() 调用（prompt + opts → subagent）
├── parallel() 屏障模式
├── pipeline() 流水线模式
└── phase() + log() 进度展示

Phase 2: 持久化层
├── Journal JSONL 写入
├── Deterministic Key 生成（v2:SHA256）
└── Resume 缓存查询

Phase 3: 高级功能
├── Budget 追踪
├── StructuredOutput schema 验证
├── ToolSearch 延迟加载
├── Agent 类型注册表
└── SendMessage 跨 agent 通信
```

### 18.3 Codex 平台映射

| Claude Code 实现 | Codex 对应实现 | 复杂度 |
|-----------------|---------------|--------|
| JS 沙箱执行引擎 | Codex 内置脚本解释 或 指令层模拟 | 高 |
| Subagent 进程创建 | `spawn_agent` / `multi_tool_use.parallel` | 中 |
| Journal JSONL | 本地文件写入（`.codex/workflows/<run>/journal.jsonl`） | 低 |
| Deterministic Key | SHA-256(prompt + opts)，已有 Python 实现 | 低 |
| StructuredOutput | Codex 原生 JSON Schema 验证 或 prompt 约束 | 中 |
| ToolSearch | 工具注册表 + 查询接口 | 中 |
| Agent 类型注册表 | 配置文件 + 工具过滤逻辑 | 低 |
| worktree 隔离 | Git worktree 创建/清理 | 中 |
| Daemon-Worker IPC | 不需要——Codex 使用不同的进程模型 | N/A |

### 18.4 关键实现细节

#### 18.4.1 Deterministic Key 生成

```python
import hashlib, json

def generate_key(prompt: str, opts: dict) -> str:
    """与 Claude Code 完全兼容的 key 生成"""
    body = json.dumps({"prompt": prompt, "opts": opts},
                      ensure_ascii=False, sort_keys=True,
                      separators=(",", ":"))
    return "v2:" + hashlib.sha256(body.encode("utf-8")).hexdigest()
```

#### 18.4.2 Journal 格式

```jsonl
{"type":"started","key":"v2:sha256...","agentId":"random17hex"}
{"type":"result","key":"v2:sha256...","agentId":"random17hex","result":{...}}
```

#### 18.4.3 Resume 逻辑

```
对每个 agent() 调用：
1. 计算 key = generate_key(prompt, opts)
2. 在 journal.jsonl 中查找：
   - 存在 key + type="result" → 返回缓存 result
   - 不存在 → 创建新 agent
3. 最长不变前缀优化：第一个 key 不匹配的 agent 之后的所有 agent 重新执行
```

#### 18.4.4 并发控制

```
max_concurrency = min(16, cpu_cores - 2)
max_total_agents = 1000
max_items_per_call = 4096

当 parallel() 中的 thunk 数 > max_concurrency：
  → 前 max_concurrency 个立即执行
  → 其余进入 FIFO 队列
  → 每当一个 agent 完成，从队列取下一个
```

### 18.5 与现有 Codex Workflow 插件的集成

已有插件位于 `C:\Users\Han\plugins\codex-workflow\`，包含：

| 文件 | 功能 | 状态 |
|------|------|------|
| `plugin.json` | 插件清单 | ✅ 已完成 |
| `SKILL.md` | Skill 定义（编排指导） | ✅ 已完成 |
| `codex_workflow.py` | Journal 辅助脚本 | ✅ 已完成 |
| `claude-workflow-model.md` | Claude 概念映射参考 | ⚠️ 需更新 |
| `codex-workflow-template.md` | 工作流计划模板 | ✅ 已完成 |

**下一步**：
1. 更新 `claude-workflow-model.md` 以反映本文档的最新内容
2. 在 Codex 中注册 Workflow 相关工具（如需要）
3. 测试 Resume 机制的端到端流程
4. 验证并发控制在 Codex 平台上的行为

---

## 附录 A：完整数据流

```
步骤 1: 用户输入触发
─────────────────────────────────────────
用户: /workflow deep-compare-emuera-cores
        │
        ▼
主 Agent 识别 workflow 命令
        │
        ▼
步骤 2: 工作流初始化
─────────────────────────────────────────
引擎读取: workflows/scripts/<name>-wf_<hash>.js
        │
        ├── 提取 meta: { name, description, phases }
        ├── 创建 journal.jsonl (append-only)
        └── 初始化全局变量: args, budget, agent, parallel, pipeline, phase, log, workflow
        │
        ▼
步骤 3: 脚本执行 - 阶段 1
─────────────────────────────────────────
phase('Research')  ──► 写入 journal
        │
        ▼
parallel([agent1, agent2, ...])
        │
        ├── 每个 agent() 调用:
        │   ├── 生成 key: v2:SHA256(prompt+opts)
        │   ├── 检查 journal: 是否已有相同 key 的 result?
        │   │   ├── 有 → 直接返回缓存结果
        │   │   └── 无 → 创建 subagent 进程
        │   ├── subagent 执行:
        │   │   ├── 初始化: deferred_tools_delta (100+ 工具)
        │   │   ├── 加载技能: skill_listing (10+ 技能)
        │   │   ├── 如果有 schema: 追加 StructuredOutput 指令
        │   │   ├── 循环: assistant → tool_use → user(tool_result)
        │   │   └── 完成: 返回结果（有 schema 时返回验证对象）
        │   └── 写入 journal: {type:'result', key, agentId, result}
        │
        └── 等待全部 agent 完成（屏障）
        │
        ▼
步骤 4: 脚本执行 - 阶段 2
─────────────────────────────────────────
phase('Synthesize') ──► 写入 journal
        │
        ▼
agent(汇总 prompt, { schema: REPORT_SCHEMA })
        │
        ├── 将多个 subagent 的结果拼接
        ├── 创建新的 subagent 执行汇总
        └── 返回结构化报告（经过 schema 验证）
        │
        ▼
步骤 5: 结果返回
─────────────────────────────────────────
脚本执行 return {...}
        │
        ├── 引擎序列化返回值
        ├── 写入 workflow-result.json
        └── 返回给主 Agent
        │
        ▼
步骤 6: 主 Agent 呈现
─────────────────────────────────────────
主 Agent 收到 workflow 结果
        │
        └── 整合到对话中，呈现给用户
```

---

## 附录 B：脚本模式参考

### 基本审查模式

```javascript
export const meta = {
  name: 'review-changes',
  description: 'Review changed files across dimensions, verify each finding',
  phases: [{ title: 'Review' }, { title: 'Verify' }],
}

const DIMENSIONS = [
  {key: 'bugs', prompt: '...'},
  {key: 'perf', prompt: '...'}
]

const results = await pipeline(
  DIMENSIONS,
  d => agent(d.prompt, {label: `review:${d.key}`, phase: 'Review', schema: FINDINGS_SCHEMA}),
  review => parallel(review.findings.map(f => () =>
    agent(`Adversarially verify: ${f.title}`, {label: `verify:${f.file}`, phase: 'Verify', schema: VERDICT_SCHEMA})
      .then(v => ({...f, verdict: v}))
  ))
)

const confirmed = results.flat().filter(Boolean).filter(f => f.verdict?.isReal)
return { confirmed }
```

### Loop-Until-Count 模式

```javascript
export const meta = {
  name: 'find-bugs',
  description: 'Find a target number of bugs',
  phases: [{ title: 'Find' }],
}

const bugs = []
while (bugs.length < 10) {
  const result = await agent('Find bugs in this codebase.', {schema: BUGS_SCHEMA})
  bugs.push(...result.bugs)
  log(`${bugs.length}/10 found`)
}
return { bugs }
```

### Loop-Until-Budget 模式

```javascript
export const meta = {
  name: 'exhaustive-search',
  description: 'Search until budget exhausted',
  phases: [{ title: 'Search' }],
}

const findings = []
while (budget.total && budget.remaining() > 50_000) {
  const result = await agent('Find issues.', {schema: ISSUES_SCHEMA})
  findings.push(...result.issues)
  log(`${findings.length} found, ${Math.round(budget.remaining()/1000)}k remaining`)
}
return { findings }
```

### 完整审查模式（Loop-Until-Dry + Adversarial Verify）

```javascript
export const meta = {
  name: 'exhaustive-review',
  description: 'Find, dedup, and adversarially verify bugs',
  phases: [{ title: 'Find' }, { title: 'Verify' }],
}

const seen = new Set(), confirmed = []
let dry = 0

while (dry < 2) {
  const found = (await parallel(FINDERS.map(f => () =>
    agent(f.prompt, {phase: 'Find', schema: BUGS})
  ))).filter(Boolean).flatMap(r => r.bugs)

  const fresh = found.filter(b => !seen.has(key(b)))
  if (!fresh.length) { dry++; continue }
  dry = 0
  fresh.forEach(b => seen.add(key(b)))

  const judged = await parallel(fresh.map(b => () =>
    parallel(['correctness','security','repro'].map(lens => () =>
      agent(`Judge "${b.desc}" via the ${lens} lens — real?`, {phase: 'Verify', schema: VERDICT})
    )).then(vs => ({ b, real: vs.filter(Boolean).filter(v => v.real).length >= 2 }))
  ))

  confirmed.push(...judged.filter(v => v.real).map(v => v.b))
}

return { confirmed }
```

---

*本文档基于 Claude Code 2.1.167 系统提示词中的 Workflow Tool Definition，以及运行时文件和日志的一手分析。所有 API 定义均可通过检查系统提示词验证。*
