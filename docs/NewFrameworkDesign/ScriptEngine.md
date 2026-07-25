# VM 运行器、可挂起目标与调度协议

本章是执行模型的唯一权威定义。行为基线同时来自上游 XEmuera 和旧 gEmuera 的 Process/ExecutionContext/lazy/NF/float 扩展。迁移 M0–M5 保留专用 VM thread；下面的 continuation/Step 是可选调度能力和最终统一模型，不再预设必须由 Godot 主线程每帧调用。

Runner 的指令、函数和行为策略只来自当前 `GameSession.Compatibility.Dialect` 的冻结窄视图。VM 不读取 `Program.CoreProfile`、游戏 id 或 mutable module catalog；改变方言等同于构建并提交新候选 Session。模块契约见 [DialectExtensionSystem](DialectExtensionSystem.md)。

## 迁移期 Runner

`IVmRunner` 有 `LegacyRunLoopAdapter` 与 `ResumableVmRunner` 两个实现。前者只允许在专用 VM owner thread 运行，把 Godot/静态桥逐步替换为不可变 batch/typed port；后者实现 Step/Resume。两者接受相同 clock、RNG、文件 manifest 和输入 trace，并输出相同 canonical timeline。M5 前禁止为追求分帧而同时重写全部指令。

## 执行状态

```text
Created → Running ↔ YieldedBudget
                  ↔ WaitingInput
                  ↔ WaitingMessage
                  ↔ WaitingPort
                  ↔ DispatchingEvent
                  → Completed
                  → Faulted
                  → Cancelled
```

状态转换只由 VM scheduler 执行。View 提交输入或平台结果时只写 completion queue，不能直接改变 InstructionPointer。

## VmFrame 与 continuation

每个 frame 包含函数身份、`ParentLabelLine`、下一条 InstructionPointer、返回地址、局部变量/参数、frame kind（Top/Event/Call/UserFunction）和错误上下文。VM 另有值栈、调用栈、事件队列、当前等待、effect sequence 和 resume serial。

目标 resumable runner 中，顶层、CALL/JUMP、事件调用与表达式 `#FUNCTION/#FUNCTIONS/#FUNCTIONF` 共享相同 frame push/pop，求值器保存表达式 continuation。Legacy adapter 可以保留旧专用线程同步调用，但这条路径不得进入主线程 experiment。

## Step 算法

```text
Step(budget):
  reject if disposed/cancelled
  drain at most N accepted completions
  if waiting and no matching completion: return Waiting
  while instructions < maxInstructions and work < maxWork:
      if monotonicNow >= deadline: return YieldedBudget
      frame = top frame
      line = resolve(frame.instructionPointer)
      result = executeOne(line)
      apply Core state change atomically at instruction boundary
      append typed effects with increasing sequence
      handle push/pop/jump/wait/fault
      check cancellation at safe point
  return YieldedBudget or semantic stop reason
```

InstructionPointer 指向“下一条待执行行”。普通指令成功后推进；流程控制显式写新位置；产生 wait 的指令保存 resume continuation 后停止。指令中途抛错时不得把 IP 推到下一行。

## 预算

专用线程 runner 使用吞吐、UI batch 背压和取消安全点预算；主线程实验才使用初始 2 ms、10,000 指令、100,000 work units。它们均是待 benchmark 配置，不是性能承诺。Godot 的 batch reduce、layout、texture upload 和 draw preparation 另有独立帧预算，不能把 VM 2 ms 当成总成本。确定性测试使用虚拟时钟和固定 budget。

work unit 示例：解析一个 token=1、复制一个数组元素=1、生成一个 DisplayPart=4、处理一条 CSV 记录=16。各算法声明自己的计费，防止单条“指令”隐藏无限工作。

## 重操作分类

| 操作 | 策略 | 安全点/上限 |
| --- | --- | --- |
| ERB/CSV 解析 | 分块增量 | 每行/token 批次 |
| HTML 解析 | 分块或单输入硬上限 | 标签/文本段边界，4 MiB/128 深度 |
| 长数组操作 | chunked loop | 每 4K 元素检查 budget |
| 存档序列化 | immutable snapshot 后台 | 每 block；总大小限制 |
| GZip | bounded streaming worker | 每 64 KiB；输出/比率限制 |
| 图片/音频解码 | Bridge worker，先 header | decoder 预算；结果主线程提交 |
| Godot resource 创建 | 主线程 | 每帧 completion 数量预算 |
| Regex/Map/XML/DT/SQL | 专用 VM thread bounded；逐项审计后才可分块/worker | 超时、结果顺序、事务边界 |
| 插件反射 | 默认禁用；trusted desktop 仅专用 VM thread | 无可靠进程内沙箱；任意托管代码不能安全强杀 |
| lazy ERB | 专用 VM thread 保持旧语义 | 索引扫描/完整解析边界、Android p99 |

后台任务不得读取共享可变 VariableStore 或操作 Godot 对象。只有不可变 snapshot/bytes/token 可跨线程。

## RESTART 与退出事实

`RESTART_Instruction.DoInstruction` 调用 `state.JumpTo(func.ParentLabelLine)`。目标 VM 对当前 frame 设置 IP 为 ParentLabelLine；不清空全局变量、不销毁会话、不返回标题。嵌套 CALL 中只重启当前函数 frame，调用者仍在栈中。

`QUIT_AND_RESTART` 与 `FORCE_QUIT_AND_RESTART` 是应用效果：XEmuera 设置 `Program.Reboot` 并 Quit/ForceQuit。目标 VM 产生 `ApplicationEffect(Restart, force)`，由 AppBootstrap 处理。普通 QUIT、返回标题、SwitchGame 是不同效果，见[StateIsolation](StateIsolation.md)。

## 读档恢复

原版存档恢复变量后进入 `SYSTEM_LOADEND`、`EVENTLOAD` 系统流程。目标 VM 构造新的系统 flow frame，而不是恢复保存时任意 IP。只有未来定义完整 continuation schema、版本迁移和资源等待恢复后，才能把任意执行点恢复作为独立 Extension。

## 输入恢复

输入指令创建 InputRequestDto，保存等待 continuation 并返回 `WaitingInput`。结果到达 Bridge 后进入 completion queue；下一次 `Step` 校验 request id、generation 和 CAS 完成状态，再把 typed value 写入预定目标并推进 IP。同一 Godot `_Process` 不承诺二次 Step。

## 错误边界

解析错误带 SourcePosition；运行错误带函数、ParentLabelLine 与 call stack；资源/平台错误通过 port completion 返回。Fault 发生后 VM 进入明确定义状态：可恢复脚本错误、会话不可继续错误或应用 fatal。不能 catch 全部异常后继续运行不可信 VM。

协作超时只在安全点触发；后台 heartbeat 只能检测 worker；同线程 Timer 无法检测主线程卡死。平台 watchdog/OOM/崩溃策略见[ErrorRecovery](ErrorRecovery.md)。

## 确定性测试

- 预算恰好在指令前/后耗尽，状态与无限 budget 结果一致。
- 深递归达到配置上限，产生稳定 fault 而非进程栈溢出。
- 长事件链在多帧执行但 effect 顺序与 XEmuera 相同。
- 表达式用户函数能等待/让出，返回值继续原表达式。
- INPUT、消息等待、取消、超时和重复 completion 不双重恢复。
- RESTART 在顶层、嵌套、事件和等待后场景行为正确。
- 单条重操作遵守 work budget 或硬上限。
- 旧 generation 的 worker/input completion 不改变新会话。

## 验收不变量

M0–M5 的验收是：Legacy runner 不触碰 Godot 对象、只经 typed batch/port、专用线程卡顿不冻结主线程且可诊断。主线程实验的验收才要求全文无 continuation 旁路、每个可恢复状态可表达、所有重操作完成 yieldability 审计。两种 runner 的时间线差分都记录状态、输出、错误和顺序。
