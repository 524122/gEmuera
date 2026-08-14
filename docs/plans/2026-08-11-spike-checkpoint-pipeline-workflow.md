---
intent: 阶段 1 Spike——打通「Shop_Begin 检查点 → 批量生成 → 预取缓存 → CALLSHARP 注入」最小全链路，验证 v3 方案环形骨架可行性；含 LLM 统一客户端、Profile 加载、离线兜底验收。
success_criteria: ① dotnet build 零错误 ② xUnit 全绿（Provider/Profile/Budget）③ 无 api_key 时游戏行为与无 AI 构建完全一致（仅诊断日志差异）④ 配置三元组后 Shop_Begin 触发一次 LLM 往返且缓存可经 CALLSHARP 读出 ⑤ dialect-inventory 冒烟测试保持绿色（零新增 ERB 指令）
risk_level: medium
auto_approve: true
branch: ai/spike-checkpoint-pipeline
# 工作区存在用户无关 WIP（虚拟鼠标）；隔离 worktree 执行，不触碰主检出
dirty_worktree: allow
# 沙箱限制：外部 worktree 路径不可写，改为当前检出 + 专属分支执行
worktree: false
---

## Steps

- [x] **Step 1: 创建 xUnit 测试项目**
action: 创建 `tests/GEmuera.Core.Tests/GEmuera.Core.Tests.csproj`（net8.0、xunit 2.x、ProjectReference 引用 `src/Core/GEmuera.Core.csproj`），用 `dotnet sln gemuera-c#.sln add tests/GEmuera.Core.Tests/GEmuera.Core.Tests.csproj` 加入解决方案，写一个占位测试 `FrameworkSmokeTest.Passes`（Assert.True(true)）。注意：测试项目不得引用 Godot 程序集。
loop: false
verify: dotnet test tests/GEmuera.Core.Tests/GEmuera.Core.Tests.csproj

- [x] **Step 2: LLM Provider 契约 + OpenAI 兼容客户端**
action: 新建 `src/Core/Agent/Llm/ILlmProvider.cs`：定义 `record LlmChatMessage(string Role, string Content)`、`record LlmRequest(IReadOnlyList<LlmChatMessage> Messages, int MaxTokens)`、`sealed record LlmResult(bool Ok, string Content, string Error, int TokensUsed)`、接口 `Task<LlmResult> CompleteAsync(LlmRequest request, CancellationToken ct)`。新建 `OpenAiCompatProvider.cs`：构造注入 `HttpClient` 与 `baseUrl/apiKey/model` 三元组；POST `{baseUrl}/chat/completions`，Authorization Bearer，JSON body 含 model/messages/max_tokens；解析 `choices[0].message.content`；非 2xx、网络异常、超时一律返回 `Ok=false`（禁止抛异常穿透）。测试用自定义 stub `HttpMessageHandler`：断言请求 URL/头/body 结构、成功解析、HTTP 500 降级、模拟 TaskCanceledWhenAny 超时降级。
loop: until tests pass
max_iterations: 3
verify: dotnet test tests/GEmuera.Core.Tests/GEmuera.Core.Tests.csproj --filter "FullyQualifiedName~Llm"

- [x] **Step 3: Profile 契约模型 + 加载校验**
action: 新建 `src/Core/Agent/Contract/GameProfile.cs`：System.Text.Json 可反序列化模型，字段对齐 `examples/agent-profiles/profile.schema.json`（game/game_id、engine_flow、character_csv.semantic_groups 候选数组、koujou.layers[]、checkpoints.primary、context.prefetch_key、behaviors.whitelist[]、budget、fallback）。新建 `ProfileLoader.cs`：`LoadFromJson(string json)` + `Validate()`，必填缺失/GameBase 比对字段缺失时返回带字段名的错误列表（不抛异常）。测试：加载仓库内 `examples/agent-profiles/akuma-maid/profile.json` 断言 game_id=="akuma-maid"、koujou.layers.Count==6、behaviors.whitelist.Count==3、budget.tokens_per_turn==8000；空 JSON、缺 required 字段、错误 profile_version 各自产出含字段名的错误。
loop: until tests pass
max_iterations: 3
verify: dotnet test tests/GEmuera.Core.Tests/GEmuera.Core.Tests.csproj --filter "FullyQualifiedName~Profile"

- [x] **Step 4: 回合预算执行器**
action: 新建 `src/Core/Agent/Context/TurnBudget.cs`：`TurnBudget(int tokensPerTurn)`、`bool TryConsume(int tokens)`（余量不足返回 false 且不扣减）、`void Reset()`、`int Remaining`。线程安全（解释器线程调用，lock 或 int 交换均可，注明访问线程）。测试：初始可扣、耗尽后拒扣且 Remaining 不变、Reset 后恢复。
loop: until tests pass
max_iterations: 3
verify: dotnet test tests/GEmuera.Core.Tests/GEmuera.Core.Tests.csproj --filter "FullyQualifiedName~Budget"

- [x] **Step 5: 引擎桥——3 个 LLM stub 路由到统一客户端 + config 三元组**
action: 新建 `Scripts/Emuera/Runtime/AgentBridge/AgentLlmMethods.cs`。先读 `config.toml` 与 `Scripts/Diagnostics/RuntimeTomlParser.cs` 了解既有 TOML 读取机制，复用它新增可选 `[agent.llm]` 段（base_url/api_key/model 三个 string 键，缺省为空）。修改 `Scripts/Emuera/Runtime/Utils/PluginSystem/PluginManager.cs` L199-205 的 `RegisterBuiltinMethods`：三个 `MarkLlmUnavailable` 替换为路由到 AgentLlmMethods——无 `[agent.llm]` 配置或字段为空时保持原行为（RESULT=-1，走离线兜底，写一条 DIAG 诊断日志）；有配置时调用 OpenAiCompatProvider（同步等待，PluginManager 是解释器线程同步 CALLSHARP 语义，加 30s 硬超时 CancellationTokenSource），成功 RESULT=1/RESULTS=回复文本并经 TurnBudget 记账，失败 RESULT=-1 + 诊断日志（含 profile 无关的错误码与原因）。`LAUNCH_BROWSER` 方法保持不动。
loop: until build passes
max_iterations: 3
verify: dotnet build gemuera-c#.sln -p:Configuration=Debug

- [x] **Step 6: 检查点流水线骨架（Shop_Begin 挂钩 + 组合节点）**
action: 新建 `Scripts/GodotHost/AgentBridgeHost.cs`（Node，组合容器，挂进 main.tscn）与 `Scripts/GodotHost/AgentBridge/CheckpointPipelineComponent.cs`（子 Node + 脚本，signal `checkpoint_reached(state: StringName)`）、`Scripts/GodotHost/AgentBridge/AgentBridgeQueue.cs`（解释器线程 → 主线程的线程安全队列：Enqueue 无锁、主线程 poll 后 `Callable.From(...).CallDeferred()` 触发信号）。在 `Scripts/Emuera/GameProc/Process.SystemProc.cs` 的 `beginShop` 方法入口处调用静态桥接点（仅入队 `Shop_Begin`，解释器线程零阻塞）。组件间零直连：Pipeline 只发信号，Host 编排。此步 LLM 批处理先不接，只验证「状态转移 → 入队 → 主线程信号」链路，用 RuntimeDiagnostics 日志证明触发。
loop: until build passes
max_iterations: 3
verify: dotnet build gemuera-c#.sln -p:Configuration=Debug

- [x] **Step 7: 预取缓存 + CALLSHARP 读缓存方法**
action: 新建 `src/Core/Agent/Cache/KoujouPrefetchCache.cs`（纯 C#：`Put(string contextKey, string text)`/`TryGet(string contextKey, out string text)`/`Clear()`，StringName 等价用 string 即可）+ 单测（put/get 命中、未命中、Clear）。在 CheckpointPipelineComponent 收到 Shop_Begin 信号后：若 `[agent.llm]` 已配置且 TurnBudget 有余量，用固定模板 prompt（System: 你是era游戏角色口上生成器 + User: 情境上下文占位符）做一次 LLM 往返写入缓存（阶段 1 不做 RAG，情境 key 取 `TARGET|时段|好感档` 占位拼接）；LLM 失败/无配置时缓存为空。在 PluginManager 增加两个内置方法：`AI_PREFETCH_GET`（参数 contextKey，命中 RESULT=1/RESULTS=文本，未命中 RESULT=-1）、`AI_PREFETCH_STATUS`（返回当前缓存条数，Spike 调试用）。
loop: until build and tests pass
max_iterations: 4
verify:
  - type: shell
    command: dotnet build gemuera-c#.sln -p:Configuration=Debug
  - type: shell
    command: dotnet test tests/GEmuera.Core.Tests/GEmuera.Core.Tests.csproj --filter "FullyQualifiedName~Prefetch"

- [ ] **Step 8: 离线兜底 + 全链路真机验收**
action: 人工验收两条路径。路径 A（离线兜底）：删掉/注释 config.toml 的 [agent.llm] 段，启动游戏进入 eraAkumaMaid，进入 SHOP 界面，确认游戏行为与改动前完全一致，诊断日志出现 LLM 降级记录，CALLSHARP CALL_GENERIC_LLM_API（可在 DEBUG 面板触发）返回 -1。路径 B（在线全链路）：配置真实 base_url/api_key/model（api_key 不入仓，本地填写），启动游戏进入 SHOP，确认诊断日志出现 Shop_Begin 检查点触发 + 一次 LLM 往返 + 缓存写入条数 ≥1，AI_PREFETCH_STATUS 返回 ≥1，AI_PREFETCH_GET 能取回文本。同时运行 tools/dialect-inventory 冒烟测试确认零新增指令、保持绿色。
loop: false
gate: human
verify:
  type: human-review
  check: 路径 A 游戏行为与无 AI 构建一致且仅日志差异；路径 B 日志含 Shop_Begin→LLM→缓存链路；dialect-inventory 绿色
