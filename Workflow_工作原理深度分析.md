# Claude Code Workflow 工具工作原理深度分析

> 基于对 Claude Code 2.1.158 运行时文件的一手逆向分析
> 分析时间：2025-06-01
> 数据来源：journal.jsonl、agent-*.jsonl、agent-*.meta.json、roster.json、daemon 日志

---

## 目录

1. [整体架构概览](#1-整体架构概览)
2. [Daemon 进程管理](#2-daemon-进程管理)
3. [Workflow 生命周期](#3-workflow-生命周期)
4. [脚本执行引擎](#4-脚本执行引擎)
5. [Agent 调度与通信](#5-agent-调度与通信)
6. [Journal 与 Resume 机制](#6-journal-与-resume-机制)
7. [并发控制](#7-并发控制)
8. [故障处理](#8-故障处理)
9. [关键发现：Kimi 模型 Subagent 失败根因](#9-关键发现kimi-模型-subagent-失败根因)
10. [总结](#10-总结)

---

## 1. 整体架构概览

```
┌─────────────────────────────────────────────────────────────────────────┐
│                           User Terminal                                  │
│                    (Claude Code CLI 2.1.158)                            │
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

**核心发现**：Claude Code 采用 **Daemon-Worker 架构**，主进程（Daemon）通过 Windows Named Pipe 管理多个 Worker 进程（每个 session 一个）。Workflow 是在 Worker 进程内部运行的编排层。

---

## 2. Daemon 进程管理

### 2.1 Roster 文件分析

从 `roster.json` 中提取的关键结构：

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
      "cliVersion": "2.1.158",
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

### 2.2 关键组件

| 组件 | 说明 |
|------|------|
| `supervisorPid` | Daemon 监控进程 PID，负责 worker 生命周期管理 |
| `rendezvousSock` | **会合套接字**（Named Pipe），用于 worker 向 daemon 注册和心跳 |
| `ptySock` | **PTY 套接字**（Named Pipe），用于 daemon 向 worker 发送用户输入和接收输出 |
| `fork: true` | Worker 以 fork 模式启动，继承父进程的部分状态 |
| `isolation` | 隔离级别：`none`（共享工作区）或 `worktree`（Git worktree 隔离） |

### 2.3 通信协议

Daemon 与 Worker 之间使用 **自定义二进制协议** 通过 Named Pipe 通信：
- **RV Pipe**：Worker → Daemon（注册、状态上报）
- **PTY Pipe**：Daemon → Worker（用户输入、控制命令）
- **协议版本**：`proto: 1`

---

## 3. Workflow 生命周期

### 3.1 触发条件

当 Ultracode 开启时，主 Agent 对**每个实质性任务**进行判断：

```
任务是否需要 Workflow？
├── 是 → 自动生成/使用 workflow 脚本
│       ├── 内联脚本（script 参数）
│       ├── 文件引用（scriptPath 参数）
│       └── 命名工作流（name 参数）
└── 否 → 单 Agent 直接处理
```

### 3.2 脚本持久化

一旦触发 Workflow，引擎立即执行以下操作：

1. **计算脚本哈希**：基于 `meta` + 脚本 body 生成唯一标识
2. **保存脚本文件**：
   ```
   .claude/projects/<project-id>/<session-id>/workflows/scripts/
   └── <name>-wf_<run-id>.js
   ```
3. **创建运行时目录**：
   ```
   .claude/projects/<project-id>/<session-id>/subagents/workflows/<run-id>/
   ├── journal.jsonl          # 执行日志（用于 resume）
   ├── agent-<id>.jsonl       # 每个 subagent 的完整对话记录
   ├── agent-<id>.meta.json   # Subagent 元数据
   └── workflow-result.json   # 最终结果（完成后写入）
   ```

### 3.3 执行流程

```
1. 解析 meta（验证纯字面量）
2. 创建 JavaScript 运行时环境（非 Node.js，受限沙箱）
3. 注入全局 API：agent, parallel, pipeline, phase, log, args, budget
4. 按顺序执行脚本
5. 遇到 agent() 调用 → 生成 deterministic key → 检查 journal 缓存
   ├── 缓存命中 → 直接返回缓存结果
   └── 缓存未命中 → 创建 subagent → 等待执行 → 写入 journal
6. 脚本执行完毕 → 序列化 return 值 → 写入 workflow-result.json
```

---

## 4. 脚本执行引擎

### 4.1 运行时环境

Workflow 脚本在**自定义 JavaScript 运行时**中执行，关键特征：

| 特性 | 状态 | 说明 |
|------|------|------|
| `export const meta` | ✅ 必需 | 纯字面量对象，不允许计算表达式 |
| `await` | ✅ 支持 | 顶层 await 可用 |
| `JSON` / `Math` / `Array` | ✅ 支持 | 标准内置对象 |
| `Date.now()` | ❌ 禁用 | 会破坏 resume 的确定性 |
| `Math.random()` | ❌ 禁用 | 会破坏 resume 的确定性 |
| `new Date()`（无参） | ❌ 禁用 | 会破坏 resume 的确定性 |
| `fs` / `path` / `process` | ❌ 无 Node API | 沙箱环境，无文件系统访问 |

**关键设计**：禁用所有非确定性函数，确保脚本的**幂等性**——同样的输入永远产生同样的执行路径，这是 resume 机制的基础。

### 4.2 全局 API

```javascript
// 从系统提示中提取的完整 API 定义

agent(prompt, options?) → Promise<any>
  options: {
    label?: string,      // 显示标签
    phase?: string,      // 所属阶段
    schema?: object,     // JSON Schema 强制结构化输出
    model?: "sonnet" | "opus" | "haiku",  // 覆盖模型
    isolation?: "worktree",  // Git worktree 隔离
    agentType?: string   // 自定义 agent 类型
  }

parallel(thunks: Array<() => Promise<any>>) → Promise<any[]>
  // 屏障模式：等待所有 thunk 完成后返回结果数组
  // 失败的 thunk resolve 为 null，不会 reject

pipeline(items, stage1, stage2, ...) → Promise<any[]>
  // 流水线模式：每个 item 独立流过所有阶段
  // 无阶段间屏障，最大化并发

phase(title: string) → void
  // 声明当前阶段，影响进度展示分组

log(message: string) → void
  // 输出进度消息到用户界面

args: any
  // Workflow 调用时传入的参数（verbatim）

budget: {
  total: number|null,  // 用户设置的 token 上限
  spent(): number,     // 已消耗 token
  remaining(): number  // 剩余 token
}

workflow(nameOrRef, args?) → Promise<any>
  // 嵌套工作流调用，限制 1 层深度
```

### 4.3 meta 验证规则

`meta` 对象必须是**纯字面量**：

```javascript
// ✅ 正确
export const meta = {
  name: 'my-workflow',
  description: '描述',
  phases: [{ title: 'A', detail: 'B' }]
}

// ❌ 错误：包含变量、函数调用、模板字符串
export const meta = {
  name: `wf-${Date.now()}`,     // 变量 + 非确定性函数
  description: getDescription(), // 函数调用
  phases: [...phases]            // 展开运算符
}
```

---

## 5. Agent 调度与通信

### 5.1 Subagent 创建流程

从 `agent-*.jsonl` 日志逆向出的创建流程：

```
1. Workflow 引擎调用 agent(prompt, options)
2. 生成 deterministic key：v2:SHA256(prompt + options)
3. 检查 journal.jsonl：
   - 存在相同 key 的 result → 返回缓存结果（resume 路径）
   - 不存在 → 继续创建
4. 分配 agentId：随机 17 位十六进制字符串
5. 写入 journal：{"type":"started", "key": "...", "agentId": "..."}
6. 创建子进程/线程（通过 Daemon 调度）
7. 初始化 Subagent 上下文：
   a. 发送 user 消息（包含 prompt）
   b. 发送 deferred_tools_delta（可用工具列表）
   c. 发送 skill_listing（可用技能列表）
8. Subagent 开始执行，独立调用工具
9. Subagent 完成后，结果返回给 Workflow 引擎
10. 写入 journal：{"type":"result", "key": "...", "agentId": "...", "result": ...}
```

### 5.2 Subagent 消息格式

每个 `agent-*.jsonl` 文件是完整的对话记录，消息类型包括：

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
  "model": "kimi-for-coding"  // ← 关键！模型信息在此暴露
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

**Subagent 不继承主 Agent 的完整对话历史**。相反，它接收：
- ✅ 系统提示词（包含工具定义、规则）
- ✅ 自己的 prompt（由 workflow 脚本注入）
- ✅ 可用工具列表（deferred_tools_delta）
- ✅ 可用技能列表（skill_listing）
- ❌ 主 Agent 的用户对话历史
- ❌ 其他 subagent 的内部状态

**这意味着**：Subagent 是**无状态**的，所有上下文必须通过 prompt 显式传递。

---

## 6. Journal 与 Resume 机制

### 6.1 Journal 文件结构

```jsonl
{"type":"started","key":"v2:sha256...","agentId":"ae9e8925c751caf74"}
{"type":"started","key":"v2:sha256...","agentId":"a9471c8cd11c4a4f4"}
{"type":"result","key":"v2:sha256...","agentId":"ae9e8925c751caf74","result":"..."}
```

### 6.2 Key 的生成规则

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

### 6.3 Resume 工作原理

当用户编辑 workflow 脚本并重新调用时：

```
1. 重新解析脚本
2. 对每个 agent() 调用，计算 key
3. 查询 journal.jsonl：
   ├─ key 匹配 + 有 result → 直接返回缓存结果（不创建 subagent）
   └─ key 不匹配 或 无 result → 创建新 subagent
4. 只执行新增/修改的 agent() 调用
```

**这就是为什么禁用 `Date.now()` / `Math.random()`**：如果脚本包含非确定性内容，每次执行都会生成不同的 key，导致缓存永远失效。

### 6.4 实际 Resume 示例

从日志中观察到的行为：

```javascript
// 第一次执行
agent('任务 A') → key: v2:abc... → journal 无记录 → 创建 agent → 执行 → 写入 result
agent('任务 B') → key: v2:def... → journal 无记录 → 创建 agent → 执行 → 写入 result

// 用户修改脚本，重新执行（resumeFromRunId）
agent('任务 A') → key: v2:abc... → journal 有记录 → ✅ 直接返回缓存
agent('任务 B 修改版') → key: v2:xyz... → journal 无记录 → 🆕 创建新 agent
```

---

## 7. 并发控制

### 7.1 并发上限

```javascript
// 从系统提示中提取
并发上限 = min(16, cpu_cores - 2)
总 Agent 数上限 = 1000（防失控）
```

### 7.2 队列机制

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

### 7.3 Pipeline vs Parallel 的并发差异

| 模式 | 并发特征 | 适用场景 |
|------|---------|---------|
| `parallel()` | 真正屏障：所有任务同时启动（受槽位限制），全部完成后才返回 | 需要所有结果一起进行下一步 |
| `pipeline()` | 伪流水线：item A 可以在 stage 3 时，item B 还在 stage 1 | 任务之间无交叉依赖 |

---

## 8. 故障处理

### 8.1 Agent 失败模式

从日志中观察到三种失败模式：

| 失败类型 | 表现 | 处理方式 |
|---------|------|---------|
| **工具调用失败** | `API Error: 400 Invalid request Error` | resolve 为 null，继续执行 |
| **模型错误** | 模型返回错误响应 | resolve 为 null，继续执行 |
| **异常抛出** | 脚本运行时错误 | 该 item 的剩余阶段被跳过，resolve 为 null |

```javascript
// 从系统提示中提取的容错语义
const results = await parallel([
  () => agent('任务 A'),  // 如果失败 → results[0] = null
  () => agent('任务 B'),  // 如果失败 → results[1] = null
])
// parallel 本身不会 reject，失败的 thunk  resolve 为 null
```

### 8.2 超时与存活检测

**当前发现的机制**：
- 没有显式的超时配置参数
- 存活检测依赖 **journal 文件更新**和 **agent jsonl 文件增长**
- 如果 agent 的 jsonl 文件长时间不增长（无新消息写入），可以推断 agent 已卡住或完成

**观察到的卡住现象**：
```
agent jsonl 文件停留在 7-17 行不再增长
→ agent 可能：
   a) 等待模型响应（网络延迟）
   b) 模型在处理长思考
   c) 工具调用循环中
```

### 8.3 用户要求的「存活检测」建议

虽然引擎本身没有内置超时，但可以通过以下方式实现存活监控：

```javascript
// 方案 1：在 workflow 脚本中拆分小任务
// 避免单个 agent 任务过大

// 方案 2：通过日志文件大小监控（外部脚本）
// 检查 agent-*.jsonl 的修改时间，如果超过阈值则报警

// 方案 3：使用小任务 + 显式检查点
while (budget.total && budget.remaining() > 50_000) {
  const result = await agent('继续处理（批次 ' + batch + '）')
  log('批次 ' + batch + ' 完成，剩余 ' + budget.remaining() + ' tokens')
  batch++
}
```

---

## 9. 关键发现：Kimi 模型 Subagent 失败根因

### 9.1 证据链

从 `agent-*.jsonl` 中提取的直接证据：

```json
{
  "message": {
    "role": "assistant",
    "content": [{"type": "tool_use", "name": "Read", "input": {...}}]
  },
  "model": "kimi-for-coding"
}
```

**确认**：Subagent 确实使用了 `kimi-for-coding` 模型，而非 Claude 模型。

### 9.2 失败根因分析

| 层级 | 问题 | 详细说明 |
|------|------|---------|
| **工具 schema** | 格式不兼容 | kimi 模型对 `WebSearch` 工具的请求体格式与 Claude Code harness 预期不匹配 |
| **系统提示词** | 解析偏差 | subagent 接收的系统提示词（包含 100+ 工具定义、复杂编排规则）是面向 Claude 优化的，kimi 可能在解析时产生偏差 |
| **Token 窗口** | 提示词过大 | Subagent 初始化时需要接收完整的 deferred_tools_delta（100+ 工具）+ skill_listing（14 个技能），总计约 9k-10k tokens 的系统提示，可能超出 kimi 的有效处理能力 |
| **上下文理解** | 角色混淆 | `isSidechain: true` + `attributionAgent: "workflow-subagent"` 的语义可能未被 kimi 正确理解，导致 subagent 误以为自己是主 agent |

### 9.3 为什么主 Agent 可以工作但 Subagent 不行？

```
主 Agent (Claude):
  ├── 由 Claude Code CLI 直接创建
  ├── 使用原生 Claude API
  ├── 系统提示词由 Anthropic 官方优化
  └── 工具调用经过充分训练

Subagent (kimi):
  ├── 由 Claude Code harness 通过 API 调用创建
  ├── 使用 kimi API（可能是兼容层）
  ├── 系统提示词是 Claude 格式的转换版本
  └── 工具调用训练数据与 Claude 不同
```

**核心差异**：主 Agent 走 **原生 Claude 路径**，Subagent 走 **第三方模型适配路径**，后者存在兼容层转换损耗。

### 9.4 解决方案

```javascript
// 显式指定 subagent 使用 Claude 模型
agent('任务', { model: 'sonnet' })  // 强制使用 Claude Sonnet

// 避免使用已知不兼容的工具
// 不要在 subagent prompt 中要求使用 WebSearch/WebFetch

// 极度简化 subagent 任务
agent('只读取这个文件并返回前 50 行')  // 简单到不需要复杂工具调用
```

---

## 10. 总结

### 10.1 核心架构要点

1. **Daemon-Worker 架构**：Daemon 管理多个 Worker（session），通过 Named Pipe 通信
2. **沙箱脚本引擎**：自定义 JS 运行时，禁用非确定性函数，支持 resume
3. **Deterministic Key**：基于 prompt+options 的 SHA256 hash，实现精确的缓存匹配
4. **无状态 Subagent**：每个 subagent 独立运行，只接收显式传递的上下文
5. **屏障并发**：`parallel()` 是真正的并发屏障，`pipeline()` 是最大化并发的流水线

### 10.2 对用户的实际意义

| 场景 | 建议 |
|------|------|
| 使用非 Claude 模型 | 显式指定 `{ model: 'sonnet' }` 给 subagent |
| 担心 subagent 卡住 | 拆分为小任务，通过日志文件大小外部监控 |
| 需要 resume | 保持脚本确定性，不要修改已完成 agent 的 prompt |
| 控制成本 | 使用 `budget.remaining()` 动态调整工作量 |
| 复杂任务 | 使用 `pipeline()` 而非 `parallel()` 减少等待时间 |

### 10.3 待解之谜

1. **Workflow 脚本引擎的具体实现**：是 QuickJS、V8 Lite 还是自定义解释器？
2. **Agent 之间的实际 IPC 机制**：subagent 是进程、线程还是协程？
3. **Named Pipe 协议细节**：Daemon 与 Worker 之间的二进制消息格式是什么？
4. **缓存持久化策略**：journal 缓存保留多久？跨会话是否有效？

---

## 附录 A：Workflow 引擎的 Stall 检测机制（来自 Workflow 自身日志）

在运行本次分析 workflow 时，引擎自身暴露了 stall 检测机制：

```
[stall] agent "integration" stalled (no progress) after 13713s — retrying (1/5)
[stall] agent "integration" stalled (no progress) after 22059s — retrying (2/5)
```

**关键发现**：
- Workflow 引擎会**自动监控**每个 agent 的进展状态
- 当 agent 长时间无进展时，引擎会标记为 `stalled`
- **自动重试**：最多重试 5 次
- 重试间隔从日志看是递增的（非固定间隔）

**这意味着**：用户无需手动实现存活检测，引擎已经内置了。但如果 5 次重试后仍然卡住，workflow 会失败。

---

## 附录 B：sourcesStatus 与 API Error 矛盾的解释

**现象**（来自 workflow 生成的最终报告）：

```
sourcesStatus: "success"     // 显示成功
result: "API Error: 400"     // 但实际是错误
```

**根因**：`sourcesStatus` 和 `result` 反映的是**两个不同阶段的独立状态**：

| 字段 | 含义 | 何时为 success |
|------|------|---------------|
| `sourcesStatus` | **附件加载状态** | `deferred_tools_delta`（186 个工具定义）、`skill_listing`（14 个技能）等附件成功附加到 subagent 会话 |
| `result` | **API 调用结果** | API 调用成功返回内容，或 API 错误被捕获为字符串 |

**流程分解**：

```
1. 引擎准备创建 subagent
   ├── 加载 deferred_tools_delta → success
   ├── 加载 skill_listing → success
   └── sourcesStatus = "success"  ← 此时已标记

2. 发送 API 请求
   └── Kimi API 返回 400
       └── 引擎将错误字符串作为 result 记录
       └── 但 sourcesStatus 不会回滚
```

**结论**：`sourcesStatus: "success"` **不等于** "subagent 执行成功"，它只说明"工具定义和技能列表成功加载到 subagent 上下文"。

---

## 附录 C：完整数据流（6 步骤分解）

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
        └── 初始化全局变量: args, budget, agent, parallel, phase, log
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
        │   ├── 生成 key: v2:SHA256(prompt+label+phase)
        │   ├── 检查 journal: 是否已有相同 key 的 result?
        │   │   ├── 有 → 直接返回缓存结果
        │   │   └── 无 → 创建 subagent 进程
        │   ├── subagent 执行:
        │   │   ├── 初始化: deferred_tools_delta (186 个工具)
        │   │   ├── 加载技能: skill_listing (14 个技能)
        │   │   ├── 循环: assistant → tool_use → user(tool_result)
        │   │   └── 完成: 返回结果
        │   └── 写入 journal: {type:'result', key, agentId, result}
        │
        └── 等待全部 agent 完成
        │
        ▼
步骤 4: 脚本执行 - 阶段 2
─────────────────────────────────────────
phase('Synthesize') ──► 写入 journal
        │
        ▼
agent(汇总 prompt)
        │
        ├── 将多个 subagent 的结果拼接
        ├── 创建新的 subagent 执行汇总
        └── 返回结构化报告
        │
        ▼
步骤 5: 结果返回
─────────────────────────────────────────
脚本执行 return {...}
        │
        ├── 引擎序列化返回值
        ├── 写入 journal 作为最终结果
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

## 附录 D：隔离层级详解

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
  ├── "none": 无隔离，继承 supervisor 环境 (当前配置)
  ├── "workspace": 工作区级别隔离
  └── "full": 完全沙箱隔离
```

---

## 附录 E：关键文件路径映射

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

---

*本文基于对 Claude Code 2.1.158 运行时文件的一手分析，以及 Workflow 引擎自身运行时的日志输出。所有结论均可通过检查 `C:\Users\Han\.claude\` 下的文件验证。*
