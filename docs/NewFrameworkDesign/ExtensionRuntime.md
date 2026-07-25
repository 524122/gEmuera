# Map、XML、DataTable、SQLite 与插件运行时

## 范围与所有权

旧 gEmuera 已包含 `RuntimeDataStore`、Creator.Method.Map/Xml/DT/Sql、SnakeSqlManager/ModernSqlManager 和 PluginManager。它们是实际游戏可能依赖的能力，不得因为不属于上游核心就从新架构消失。

解释器魔改优先进入 [DialectExtensionSystem](DialectExtensionSystem.md) 的编译期内置模块、typed policy 和数据型 CompatibilityPack。该机制不执行游戏提供的代码；外部 DLL 仍按本章的完全信任策略单独处理，不能借“兼容模块”名义默认启用。

| 状态 | owner | 线程 | 生命周期 |
| --- | --- | --- | --- |
| Map/XML/DataTable instances | `GameSession.ExtendedData` | VM owner thread | 按 SAVE/GLOBAL/STATIC 域和 session dispose |
| SQLite connections/readers/transactions | `GameSession.DatabaseService` | 专用 DB executor 或 VM bounded port | session dispose 强制 close/rollback |
| built-in platform capabilities | `PlatformCapabilityService` | typed port | 应用级实现、会话级授权 |
| external DLL registry | `TrustedPluginHost`（desktop optional） | 专用 VM thread 调用 | 进程级加载；切换后建议进程重启 |

不得使用可变 static 作为多会话真相。迁移期 façade 可包装旧 static store，但每次访问带 session generation，并用测试阻止 A 会话数据出现在 B。

## Map/XML/DataTable

- 公开函数名、参数、返回、大小写、错误来自旧 Creator 和实际游戏 fixture。
- VarExt CSV 为每个对象声明 SAVE/GLOBAL/STATIC；未声明对象是 runtime-only。
- LOADDATA 只候选替换 SAVE 域，LOADGLOBAL 只处理 GLOBAL；STATIC 何时清理由 profile fixture 决定。
- Map key/value、XML node/attribute 和 DataTable cell 的 Integer/String/Float/null 类型必须显式保存，禁止用 JSON 字符串丢类型。
- enumeration/select 的顺序、case sensitivity、duplicate key、null 和 conversion failure 是可观察行为。
- XML 外部实体、DTD、网络访问默认禁止；解析有 depth/node/string/memory reservation。

这些纯内存操作通常是 `CoreImmediate`。大 select/merge/serialization 在专用 VM thread 可 bounded 执行；主线程 experiment 前必须分块或证明 p99 上限。

## SQLite

旧工程使用 Microsoft.Data.Sqlite、SQLitePCLRaw 和 Android arm64 native library。目标 `IDatabasePort` 只接受受控数据库 token、参数化 command 和明确 transaction/reader id；游戏输入不得拼接平台绝对路径逃离允许根。

| 操作 | 完成语义 |
| --- | --- |
| connect/disconnect | 结果在下一条可观察，M2 可 VM-thread bounded，目标可 WaitPort |
| execute nonquery/scalar | 返回行数/Integer/String/Float/null 或 typed fault 后才推进 |
| reader open/read/get/close | reader state 属 Session；顺序 immediate，底层 I/O 可专用 executor |
| import/export Map/DT/XML | candidate conversion；失败不部分覆盖目标对象 |
| transaction | begin/commit/rollback 显式；session cancel/dispose 自动 rollback |

设置 busy timeout、command timeout、最大 DB/row/result bytes 和同时 reader 数；所有 result materialization 向 MemoryBudget reserve。SQL 文本属于游戏私有内容，诊断默认只记 hash、operation 和 SQLite code。

Android 验证必须使用最终 APK，覆盖 native library load、WAL/journal、后台/进程终止、空间不足和 ABI；桌面通过不能代替 Android。

## 外部 DLL 插件

旧 PluginManager 使用默认 AssemblyLoadContext、反射和 AssemblyResolve，并向插件暴露 Console、变量、输入等待和 shell-open。这是完全信任代码：同进程无法可靠限制文件、网络、反射、native call、无限循环或静态引用。

目标策略：

1. Android/iOS：`Unsupported`，发现 DLL 给出明确兼容报告，不尝试动态加载/AOT 绕过。
2. 桌面默认：`Disabled`；游戏可继续运行时提示缺少 capability。
3. 受信任桌面：用户查看发布者/路径/hash/权限警告后逐文件授权；更新 hash 必须重新授权。
4. 插件加载后不承诺安全卸载；切换游戏或撤销授权提示重启进程。
5. 签名/allowlist 解决身份和撤销，不解决恶意代码；不使用“沙箱插件”措辞。
6. 未来 untrusted 模式只能是独立进程 broker，协议只暴露 allowlisted DTO/commands，并有 CPU/内存/超时/文件根限制。

旧内置 `LAUNCH_BROWSER`、LLM fallback 等不继续伪装成普通 DLL 插件；它们成为独立 PlatformCapability，URL scheme/path allowlist、用户确认和平台可用性分别验证。

## 时间、随机数、日志与配置

系统时间和 RNG 是 `CoreImmediate` injectable source，差分 runner 固定输入并检查调用次数/顺序。日志使用有界异步 sink，不因磁盘慢阻塞 VM，也不能丢 fatal breadcrumb。脚本可见配置先修改会话 owner 再投影；改变随机算法、VARI/VARS 或 rendering profile 必须写入 manifest。

## fixture

- Map/XML/DT 每函数参数/返回/错误/顺序和 SAVE/GLOBAL/STATIC 往返。
- Float/null、大小写、duplicate、空对象、大 select、转换失败和 XML entity attack。
- SQLite connect/scalar/reader/transaction/import-export、busy/timeout/cancel/no-space、Android APK。
- 插件缺失、默认拒绝、hash 授权/变更、加载异常、无限调用 watchdog 和切换后重启提示。
- clock/RNG 固定 trace、日志队列背压和配置 profile 重放。
