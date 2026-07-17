# 错误分类、可信状态与恢复边界

## 原则

恢复动作取决于“失败后哪些状态仍可信”，不是统一 catch 后继续。外部内容在候选事务中失败时当前会话可信；VM 指令中途破坏不变量时当前 VM 不可信；主线程卡死/OOM/进程崩溃无法依赖进程内常规恢复。

## 分类矩阵

| 类别 | 典型来源 | 捕获边界 | 状态可信 | 可继续 | 用户操作 |
| --- | --- | --- | --- | --- | --- |
| ScriptSyntax | ERB/ERH/parser | candidate load/懒解析安全点 | 当前已提交会话通常可信 | 按原版策略 | 查看位置、返回/修复包 |
| ScriptRuntime | type/range/CodeEE | 单指令边界 | 取决于是否原子指令 | 由兼容 fixture | 查看调用栈、停止脚本 |
| VmInvariant | 栈/IP/continuation 损坏 | scheduler outer boundary | VM 不可信 | 否 | 返回标题/重载会话 |
| Resource | missing/decode/crop | candidate/resource operation | 当前会话可信；目标资源失败 | 可占位/拒绝 | 查看缺失项 |
| Encoding | strict decode/provider | 文件读取 | 当前会话可信 | 候选失败 | 转码或选择文件 |
| SaveFormat | header/type/length/GZip | ParsedSave candidate | 当前会话可信 | 是 | 备份/其他 slot |
| SaveIO | no space/permission/replace | SaveOperation | 旧存档/会话可信 | 是 | 清空间、重试 |
| CooperativeTimeout | resumable VM safe point | Step | VM 在安全点可信 | 可取消/继续 | 停止或提高预算 |
| VmThreadStall | Regex/SQL/Graphics/lazy/plugin | heartbeat + operation trace | UI 通常可信，VM未知 | 不安全强杀线程 | 等待/提示重启会话或进程 |
| PluginDenied/Fault | 未授权 DLL/反射异常 | capability/trusted boundary | 默认会话可信；已执行任意插件后未知 | 默认禁用 | 授权 hash/禁用并重启 |
| BackgroundFault | decode/import/serialize Task | completion queue | 当前会话可信 | 视操作 | 重试/降级 |
| MainThreadHang | 不可分割调用/driver | 外部 watchdog | 未知 | 进程内不可保证 | 强制终止/重启 |
| OOM | managed/native/GPU | 平台/进程 | 不可信 | 通常否 | 重启、低内存模式 |
| ProcessCrash | runtime/engine/native | OS crash handler | 不可信 | 否 | 下次启动恢复 journal |

## 指令原子性

指令实现先验证参数和所有目标，再一次提交 Core 状态；长指令按 chunk 维护 continuation，但每个 chunk 需有可回滚或明确的中间状态。不允许部分更新数组后抛错仍把 IP 推进。

## 候选事务

游戏加载、资源导入、存档读取和配置解析都写 candidate。成功验证前不发 session_committed，不覆盖当前 store/catalog/config。失败 finally 清流、临时文件、decoded buffer 与任务。

## 协作超时

Resumable runner 使用单调 deadline+指令/work 上限。专用 legacy VM thread 对每个可识别重操作记录 operation/heartbeat；它能证明 UI 未卡死，却不能安全中止正在运行的任意 Regex/native SQL/插件代码。超时 UI 可提示等待或重启进程；在状态边界不可信时不冒险继续同一 VM。

同主线程 Godot Timer 在主线程卡死时不会触发。真正 hang detection 只能来自 OS、Android ANR、iOS watchdog、外部 launcher 或独立线程能观察的 heartbeat；独立线程也不能安全修复 SceneTree，只能记录/请求进程终止。

## OOM 与内存压力

先通过平台警告/自有 budget 做预防：停止预取、驱逐无引用 cache、裁剪历史、降质量、拒绝导入。捕获 managed OOM 后可能无法可靠分配日志/对话框，不能承诺自动保存；只做预分配最小 breadcrumb 的尽力记录。

## 崩溃后恢复

启动读取 save/import journal，只恢复通过完整校验的 target/backup；删除或隔离半成品。不会自动提交上次候选 Session，也不恢复未持久化 VM continuation。

## 诊断模型

`DiagnosticEvent`：correlation id、error code、severity、component、operation id、generation、阶段、工具链版本、有限 stack fingerprint。默认不记录绝对私有路径、游戏脚本行文本、用户输入/存档内容、content URI。路径只保存 basename hash/逻辑 token。

日志 rolling 上限建议 10 MiB×3，崩溃包总上限 25 MiB；用户显式同意后导出。重复错误按 code+fingerprint 聚合，避免循环刷盘。

## UI 呈现

错误对话框提供：发生了什么、当前数据是否安全、可做操作、诊断 id。脚本兼容错误与安全拒绝分开，不能把恶意包限制显示为“引擎崩溃”。屏幕阅读器可访问，按钮焦点明确。

## 测试

每类至少一个可触发测试或平台限制记录。特别覆盖：lazy parse fault、资源占位、save 半加载、worker unobserved exception、VM thread stall、cooperative timeout、插件拒绝/异常、日志脱敏、journal recovery和低内存 reservation。主线程 hang/OOM/进程崩溃若只能平台手工测，状态保持 Uncovered。
