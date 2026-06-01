# Claude Code Ultracode 使用指南

> 本文档汇总 Claude Code Ultracode 模式的完整用法、配置、编排模式与最佳实践。
> 基于 Claude Code 内部工具定义与官方行为说明整理。

---

## 目录

1. [简介：什么是 Ultracode](#1-简介什么是-ultracode)
2. [如何启用与配置](#2-如何启用与配置)
3. [核心特性详解](#3-核心特性详解)
4. [Workflow 工具与编排模式](#4-workflow-工具与编排模式)
5. [质量模式详解](#5-质量模式详解)
6. [使用示例](#6-使用示例)
7. [最佳实践](#7-最佳实践)
8. [限制与注意事项](#8-限制与注意事项)
9. [常见问题 FAQ](#9-常见问题-faq)
10. [与其他 Effort Level 的对比](#10-与其他-effort-level-的对比)

---

## 1. 简介：什么是 Ultracode

**Ultracode** 是 Claude Code 的最高 effort level，定义为：

> **xhigh + dynamic workflow orchestration**
>
> ——即「极高努力程度 + 动态工作流编排」。

当你在 Claude Code 中执行 `/effort` 并选择 `ultracode` 时，系统会：
- 将当前会话的 effort level 提升到最高档
- **自动启用 Workflow 工具** 处理每一个实质性任务
- **不再以 token 成本为约束**，优先追求答案的完整性、准确性和深度
- 采用多种**质量模式**（对抗性验证、多视角评审、循环穷尽等）

Ultracode 的设计目标是：**对于复杂、高风险、需要全面覆盖的任务，自动编排多个子 Agent 并行工作，确保结果经过充分验证。**

### 适用场景

| 场景 | 是否推荐 Ultracode |
|------|-------------------|
| 代码审查（全面审计） | ✅ 强烈推荐 |
| 复杂功能实现（多文件、多模块） | ✅ 强烈推荐 |
| 架构设计/重构方案 | ✅ 推荐 |
| Bug 根因分析（涉及多个子系统） | ✅ 推荐 |
| 简单单文件修改 | ⚠️ 过度，用 `high` 即可 |
| 纯文档编辑 | ❌ 不建议 |
| 快速原型验证 | ❌ 不建议，用 `medium` 或 `high` |

---

## 2. 如何启用与配置

### 2.1 启用方式

在 Claude Code CLI 中输入：

```bash
/effort
```

然后选择 `ultracode`。

系统会确认：
```
Set effort level to ultracode (this session only): xhigh + dynamic workflow orchestration
```

**注意**：ultracode 只在当前会话生效，关闭 CLI 或新建会话后会恢复默认设置。

### 2.2 通过 Settings 持久化

可以通过修改 `settings.json` 或 `settings.local.json` 来设置默认 effort level：

```json
{
  "effort": "ultracode"
}
```

但通常不建议全局默认开启 ultracode，因为：
- 它会为**每一个实质性任务**自动触发 Workflow
- 对于简单任务，这会造成不必要的延迟和 token 消耗

### 2.3 Token 预算控制

Ultracode 模式下，虽然系统不再以成本为约束，但你可以通过用户指令来设置**软性上限**：

```
+500k    # 为当前任务设置 500k token 上限
```

在 Workflow 脚本中，这个预算通过 `budget` 对象暴露：

```javascript
// workflow 脚本中可用
budget.total       // 用户设置的上限（null 表示无限制）
budget.spent()     // 已消耗的 token 数
budget.remaining() // 剩余 token 数
```

---

## 3. 核心特性详解

### 3.1 动态工作流编排（Dynamic Workflow Orchestration）

这是 Ultracode 的核心机制。当你提出一个任务时：

1. **主 Agent 自动判断**：这个任务是否需要 Workflow？
2. **如果需要**：自动编写 workflow 脚本，定义阶段（phase）、并行/流水线任务
3. **自动调度子 Agent**：每个子 Agent 执行特定子任务
4. **结果整合**：主 Agent 汇总所有子 Agent 的结果，给出最终答案

**关键点**：你不需要手动写 workflow 脚本，Claude 会自动完成。

### 3.2 多阶段流水线（Multi-Phase Pipeline）

Ultracode 的典型工作流遵循以下阶段：

```
理解（Understand） → 设计（Design） → 实现（Implement） → 审查（Review）
```

每个阶段可以包含多个并行的子任务。例如：
- **理解阶段**：并行读取多个相关文件
- **设计阶段**：并行生成多个候选方案
- **实现阶段**：按依赖顺序串行修改文件
- **审查阶段**：并行进行 bug 审查、性能审查、安全审查

### 3.3 默认行为变化

| 行为 | 普通模式 | Ultracode 模式 |
|------|---------|---------------|
| 使用 Workflow | 仅在用户明确要求时 | 每个实质性任务自动使用 |
| 子 Agent 数量 | 无或少量 | 大量并行（最多 ~16 并发） |
| 验证深度 | 单次检查 | 对抗性验证 + 多视角评审 |
| Token 约束 | 遵守 | 不主动限制（除非用户设置） |
| 回答详尽度 | 适中 | 尽可能穷尽 |

---

## 4. Workflow 工具与编排模式

### 4.1 Workflow 工具概述

Ultracode 模式下，Claude Code 使用 `Workflow` 工具来编排任务。Workflow 脚本是一个 JavaScript 模块，定义了任务的执行流程。

### 4.2 脚本结构

每个 workflow 脚本必须以 `meta` 导出开头：

```javascript
export const meta = {
  name: 'my-workflow',           // 工作流名称
  description: '描述这个工作流做什么',  // 单行描述
  phases: [                      // 阶段定义（用于进度展示）
    { title: '扫描', detail: '查找相关代码' },
    { title: '修复', detail: '每个问题一个 agent' },
  ],
}

// 脚本主体
// ...
```

### 4.3 核心 API

#### `agent(prompt, options)` —— 派单子 Agent

```javascript
const result = await agent(
  '请审查这个文件的 bug',
  {
    label: 'review-file',        // 显示标签
    phase: 'Review',             // 所属阶段
    schema: {                    // 可选：强制结构化输出
      type: 'object',
      properties: {
        bugs: { type: 'array', items: { type: 'string' } }
      }
    },
    model: 'sonnet',             // 可选：指定模型
    isolation: 'worktree',       // 可选：隔离模式
    agentType: 'code-reviewer'   // 可选：指定 agent 类型
  }
)
```

#### `parallel(thunks)` —— 并行执行（屏障模式）

```javascript
const results = await parallel([
  () => agent('任务 A'),
  () => agent('任务 B'),
  () => agent('任务 C'),
])
// 等待所有任务完成后返回结果数组
```

**适用场景**：
- 需要**所有**结果一起进行下一步处理
- 需要去重/合并后再执行下游工作
- 需要根据总数决定是否继续（如 "0 bugs → 跳过验证"）

#### `pipeline(items, stage1, stage2, ...)` —— 流水线模式（默认推荐）

```javascript
const results = await pipeline(
  [file1, file2, file3],        // 输入数据
  (file) => agent(`审查 ${file}`),  // 阶段1：审查
  (review) => agent(`验证 ${review}`) // 阶段2：验证
)
```

**特点**：
- 每个 item 独立流过所有阶段
- 无阶段间屏障：item A 可能在阶段3，item B 还在阶段1
- 墙钟时间 = 最慢单条链的时间
- **这是默认推荐模式**

#### `phase(title)` —— 声明阶段

```javascript
phase('Research')   // 后续 agent() 调用归入此阶段
```

#### `log(message)` —— 输出进度

```javascript
log('已找到 5 个潜在 bug')
```

### 4.4 编排模式选择指南

| 场景 | 推荐模式 | 原因 |
|------|---------|------|
| 审查多个文件，每文件独立验证 | `pipeline` | 文件之间无依赖，最大化并行 |
| 收集所有结果后统一去重 | `parallel` | 需要所有结果一起处理 |
| 多维度评审后综合 | `parallel` + `pipeline` | 先并行收集，再流水线验证 |
| 简单的单任务 | `agent` | 无需编排 |

### 4.5 嵌套工作流

```javascript
const subResult = await workflow('sub-task-name', args)
```

**限制**：嵌套深度只能为 **1 层**。

---

## 5. 质量模式详解

Ultracode 采用多种质量模式来确保结果的可靠性：

### 5.1 对抗性验证（Adversarial Verify）

对每个发现，派 N 个独立的「怀疑者」Agent 来**试图证伪**：

```javascript
const votes = await parallel(
  Array.from({length: 3}, () => () =>
    agent(`尝试反驳这个发现: ${claim}. 如果不确定，默认认为被反驳。`, {
      schema: VERDICT_SCHEMA
    })
  )
)
const survives = votes.filter(v => !v.refuted).length >= 2
```

**作用**：防止「看起来合理但实际错误」的发现通过。

### 5.2 视角多样化验证（Perspective-Diverse Verify）

不同于简单的重复验证，每个验证者从**不同角度**审查：

| 角度 | 关注点 |
|------|--------|
| correctness | 逻辑正确性 |
| security | 安全性 |
| perf | 性能影响 |
| reproduce | 是否可复现 |

```javascript
const judged = await parallel(['correctness','security','repro'].map(lens => () =>
  agent(`从 ${lens} 角度评审这个发现`, { schema: VERDICT_SCHEMA })
))
```

### 5.3 评审团模式（Judge Panel）

生成 N 个独立的解决方案，然后评分，综合最优方案：

```javascript
// 并行生成 3 个独立方案
const proposals = await parallel([
  () => agent('从 MVP-first 角度设计方案'),
  () => agent('从 risk-first 角度设计方案'),
  () => agent('从 user-first 角度设计方案'),
])

// 并行评分
const scores = await parallel(proposals.map(p => () => agent(`评分方案: ${p}`)))

// 综合最优方案
```

### 5.4 循环穷尽（Loop-Until-Dry）

对于未知规模的发现任务（找 bug、找问题、找边界情况），持续循环直到连续 K 轮无新发现：

```javascript
const seen = new Set(), confirmed = []
let dry = 0
while (dry < 2) {  // 连续 2 轮无新发现则停止
  const found = await parallel(FINDERS.map(f => () => agent(f.prompt)))
  const fresh = found.filter(b => !seen.has(key(b)))
  if (!fresh.length) { dry++; continue }
  dry = 0
  fresh.forEach(b => seen.add(key(b)))
  // ... 验证 fresh
}
```

### 5.5 多模态扫描（Multi-Modal Sweep）

并行 Agent 从不同角度搜索：
- 按容器（by-container）
- 按内容（by-content）
- 按实体（by-entity）
- 按时间（by-time）

每个 Agent 对其他 Agent 的发现**不可见**，确保覆盖的全面性。

### 5.6 完整性批评（Completeness Critic）

在所有工作完成后，派一个专门的 Agent 检查：
- 「遗漏了什么？」
- 「哪些模态没有运行？」
- 「哪些声明未验证？」
- 「哪些源文件未读？」

发现的问题会成为下一轮工作的输入。

---

## 6. 使用示例

### 示例 1：全面代码审查

```javascript
export const meta = {
  name: 'comprehensive-code-review',
  description: '全面审查代码库，验证每个发现',
  phases: [
    { title: 'Review', detail: '多维度审查代码' },
    { title: 'Verify', detail: '对抗性验证每个发现' },
  ],
}

const DIMENSIONS = [
  { key: 'bugs', prompt: '审查以下代码的 bug...' },
  { key: 'perf', prompt: '审查以下代码的性能问题...' },
  { key: 'security', prompt: '审查以下代码的安全问题...' },
]

// 阶段一：多维度审查
const results = await pipeline(
  DIMENSIONS,
  d => agent(d.prompt, { label: `review:${d.key}`, phase: 'Review', schema: FINDINGS_SCHEMA }),
  // 阶段二：每个发现独立验证
  review => parallel(review.findings.map(f => () =>
    agent(`对抗性验证: ${f.title}`, { label: `verify:${f.file}`, phase: 'Verify', schema: VERDICT_SCHEMA })
      .then(v => ({...f, verdict: v}))
  ))
)

const confirmed = results.flat().filter(f => f.verdict?.isReal)
return { confirmed }
```

### 示例 2：复杂功能实现

```javascript
export const meta = {
  name: 'implement-feature',
  description: '实现复杂功能：理解 → 设计 → 实现 → 审查',
  phases: [
    { title: 'Understand', detail: '读取相关代码' },
    { title: 'Design', detail: '生成候选方案' },
    { title: 'Implement', detail: '实现最优方案' },
    { title: 'Review', detail: '审查实现' },
  ],
}

// 理解阶段
phase('Understand')
const context = await parallel([
  () => agent('读取核心接口定义'),
  () => agent('读取相关实现文件'),
  () => agent('读取测试文件'),
])

// 设计阶段
phase('Design')
const proposals = await parallel([
  () => agent('方案A：最小改动', { model: 'sonnet' }),
  () => agent('方案B：最清晰架构', { model: 'sonnet' }),
  () => agent('方案C：最高性能', { model: 'sonnet' }),
])
const best = await agent('从正确性、可维护性、性能三个角度评分并选择最优方案')

// 实现阶段
phase('Implement')
await agent(`实现选定的方案: ${best.description}`)

// 审查阶段
phase('Review')
const review = await agent('审查实现，检查 bug、边界情况和测试覆盖')

return { proposal: best, review }
```

### 示例 3：Bug 根因分析

```javascript
export const meta = {
  name: 'root-cause-analysis',
  description: '分析 bug 根因并生成修复方案',
}

// 收集相关信息
const [logs, code, config] = await parallel([
  () => agent('分析日志文件，提取错误时间线和堆栈'),
  () => agent('读取相关源码，标记可疑点'),
  () => agent('检查配置文件是否有异常'),
])

// 生成假设
const hypotheses = await agent('基于以上信息，生成 3-5 个可能的根因假设')

// 验证每个假设
const verified = await pipeline(
  hypotheses,
  h => agent(`验证假设: ${h}`),
  v => agent(`评估验证结果的可信度: ${v}`)
)

// 生成修复方案
const fix = await agent('基于最可信的根因，生成修复方案')

return { rootCause: verified, fix }
```

---

## 7. 最佳实践

### 7.1 任务粒度控制

- **不要太细**：每个子 Agent 应该有足够上下文完成一个有意义的任务
- **不要太粗**：单个 Agent 的任务应该在合理时间内完成（避免卡住）
- **黄金法则**：如果一个任务需要超过 5 分钟思考，拆分成多个子任务

### 7.2 Token 预算管理

```javascript
// 在 workflow 中动态控制规模
while (budget.total && budget.remaining() > 50_000) {
  const result = await agent('继续查找 bug', { schema: BUGS_SCHEMA })
  bugs.push(...result.bugs)
  log(`${bugs.length} 个 bug 已找到，剩余 ${Math.round(budget.remaining()/1000)}k tokens`)
}
```

### 7.3 错误处理

```javascript
const results = await parallel([
  () => agent('任务 A').catch(() => null),  // 失败返回 null
  () => agent('任务 B').catch(() => null),
])
const valid = results.filter(Boolean)  // 过滤掉失败项
```

### 7.4 隔离模式选择

| 场景 | 隔离模式 | 原因 |
|------|---------|------|
| 并行修改同一文件 | `worktree` | 避免冲突 |
| 只读分析 | 无隔离 | 减少开销 |
| 修改不同文件 | 无隔离 | 可以共享缓存 |

### 7.5 Resume 友好设计

Workflow 支持从断点恢复，因此：
- **不要**在脚本中使用 `Date.now()` / `Math.random()` / `new Date()`
- **应该**通过 `args` 传递时间戳等动态值
- **应该**保持脚本纯确定性

---

## 8. 限制与注意事项

### 8.1 硬性限制

| 限制项 | 上限 |
|--------|------|
| 并发 Agent 数 | `min(16, cpu cores - 2)` |
| 总 Agent 数 | 1000（防失控） |
| Workflow 嵌套深度 | 1 层 |
| 脚本确定性 | 不可用 `Date.now()` / `Math.random()` / `new Date()` |

### 8.2 使用成本

- Ultracode 会显著增加 token 消耗
- 一个简单的代码审查可能消耗 **100k-500k tokens**
- 复杂任务（全面审计）可能消耗 **数 million tokens**
- **建议**：仅在真正需要时使用，日常开发用 `high` 或 `medium`

### 8.3 不适用场景

- ❌ 简单单文件修改
- ❌ 需要即时响应的交互式任务
- ❌ 对成本敏感的环境
- ❌ 网络不稳定时（workflow 恢复依赖缓存）

### 8.4 平台兼容性注意

使用非 Claude 原生模型（如 kimi）时：
- Subagent 可能无法正确调用工具（如 WebSearch）
- 子 Agent 的上下文理解可能与主 Agent 不一致
- **建议**：在 Ultracode 模式下，确保 subagent 使用 Claude 模型

---

## 9. 常见问题 FAQ

### Q1: Ultracode 和 xhigh 有什么区别？

**A**: Ultracode = xhigh + dynamic workflow orchestration。xhigh 只表示模型输出的努力程度更高，而 ultracode 额外启用了自动工作流编排。

### Q2: 如何为单个任务设置 token 上限？

**A**: 在任务描述后追加 `+500k`（或任意数量）。这会在 workflow 中通过 `budget` 对象暴露，脚本可以根据剩余预算动态调整工作量。

### Q3: Workflow 失败了如何恢复？

**A**: Workflow 会自动缓存每个 agent 的结果。如果失败，可以：
1. 编辑 workflow 脚本修复问题
2. 使用相同的 `scriptPath` 和 `resumeFromRunId` 重新调用
3. 已完成的 agent 会立即返回缓存结果

### Q4: 可以手动控制哪些任务用 Workflow 吗？

**A**: 可以。即使开启了 ultracode，如果任务非常简单（如单轮对话），主 Agent 可能选择不使用 workflow。你也可以明确说「不要用 workflow」来覆盖。

### Q5: Subagent 卡住了怎么办？

**A**:
1. 使用 `/workflows` 查看实时进度
2. 检查 journal.jsonl 了解当前执行状态
3. 如果某个 agent 长时间无响应，可以终止整个 workflow 后恢复
4. **预防措施**：将大任务拆分为小任务，每个 agent 只处理有限范围

### Q6: Ultracode 下如何保证代码修改的正确性？

**A**: Ultracode 采用多层保障：
1. **实现阶段**：主 Agent 编写修改
2. **验证阶段**：并行子 Agent 从不同角度审查
3. **对抗性验证**：专门的 Agent 试图找出问题
4. **完整性检查**：最终检查遗漏

### Q7: 为什么我的 subagent 经常返回 API 错误？

**A**: 如果你使用的是非 Claude 模型（如 kimi）作为 subagent，可能出现：
- 工具调用格式不兼容
- 上下文理解偏差
- **解决方案**：显式指定 `model: 'sonnet'` 让 subagent 使用 Claude 模型

---

## 10. 与其他 Effort Level 的对比

| 特性 | Low | Medium | High | XHigh | **Ultracode** |
|------|-----|--------|------|-------|---------------|
| 模型输出长度 | 简短 | 适中 | 详细 | 非常详细 | 穷尽 |
| 代码审查深度 | 表面 | 常规 | 深入 | 非常深入 | **多轮对抗性验证** |
| 自动使用 Workflow | ❌ | ❌ | ❌ | ❌ | ✅ **自动** |
| 子 Agent 并行 | ❌ | ❌ | ❌ | ❌ | ✅ **大量并行** |
| Token 约束 | 严格 | 正常 | 宽松 | 很宽松 | **无硬性约束** |
| 适用任务复杂度 | 简单 | 中等 | 复杂 | 很复杂 | **最复杂** |
| 典型耗时 | 秒级 | 秒级 | 分钟级 | 分钟级 | **数分钟-数十分钟** |
| 典型 Token 消耗 | <10k | <50k | <100k | <200k | **100k-数M** |

### 选择指南

```
简单问答 / 单文件修改  →  Low / Medium
常规功能开发            →  High
复杂架构设计            →  XHigh
全面代码审计 / 重大重构  →  Ultracode
```

---

## 附录：Workflow 脚本模板

```javascript
export const meta = {
  name: 'template-workflow',
  description: '描述这个工作流',
  phases: [
    { title: 'Phase 1', detail: '描述阶段1' },
    { title: 'Phase 2', detail: '描述阶段2' },
  ],
}

// 阶段一
phase('Phase 1')
const results = await parallel([
  () => agent('子任务 A', { label: 'task-a' }),
  () => agent('子任务 B', { label: 'task-b' }),
])

// 阶段二
phase('Phase 2')
const final = await agent(`综合结果: ${JSON.stringify(results)}`)

return { results, final }
```

---

*文档版本: 2025-06-01*
*基于 Claude Code 2.1.158 内部工具定义整理*
