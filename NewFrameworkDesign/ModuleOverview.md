# 模块职责、输入输出与生命周期

## Core 模块

| 模块 | 拥有数据 | 输入 | 输出 | 错误边界 |
| --- | --- | --- | --- | --- |
| Encoding | 无持久状态 | bounded bytes/stream | decoded text + encoding | invalid/too large |
| Parser | source units、AST/cache | text、config | logical lines/functions | syntax + source position |
| Expressions | expression AST | frame、variables | typed value/effect | CodeEE equivalent |
| VM | runner、frames、IP、event/wait queues | logical line、completion、budget | immutable batch/effects | VmFault/timeout |
| Variables | global/chara/local arrays | typed variable operation | value、snapshot | range/type |
| SaveCodec | 无会话状态 | stream、candidate store | parsed save/write bytes | format/limit/version |
| SaveService | operation/journal | slot、snapshot、storage port | SaveResult | I/O/cancel/recovery |
| ResourceCatalog | descriptors/key index | resources CSV | descriptor lookup | duplicate/missing/invalid |
| Display | line/div tree、transaction、scroll/data-only metadata | DisplayEffect | immutable revision | markup/model errors |
| InputCoordinator | pending request | Request/Submit/Timeout/Cancel | resume result | duplicate/stale |
| ExtendedData | Map/XML/DT domain stores | typed operations/VarExt profile | value/domain snapshot | type/domain/limit |
| DatabaseService | connection/reader/transaction registry | IDatabasePort commands | typed SQL result | SQLite/timeout/cancel |
| PixelStore | CPU surfaces/G slots/revisions | pixel/draw operation | immediate value/texture revision | bounds/decode/budget |
| GameSession | 以上会话服务 | candidate inputs | step/snapshot/dispose | aggregate failure |

## Bridge/View 模块

| 模块 | Node 类型 | 责任 | 不拥有 |
| --- | --- | --- | --- |
| MainOrchestrator | Control | 接线、页面状态、向下委派 | 解析/变量/资源规则 |
| SessionBridge | Node | attach/detach session、generation guard | 会话内部状态 |
| UiBatchPump | Node | 每帧有界 queue reduce/layout/upload | VM 调度规则本身 |
| VmHostThread | CLR service | 专用 owner thread、runner、背压 | Godot 对象/SceneTree |
| ConsoleBackend | Control/Canvas | Display DTO→Godot draw/control | Display 历史真相 |
| InputPanel | Control | 输入展示、焦点、触摸/键盘 intent | request 完成状态 |
| ResourceBridge | Node | descriptor→Image/Texture/AudioStream | 逻辑 key 语义 |
| AudioBridge | Node | bus、voice pool、crossfade | 指令语义/会话数据 |
| PlatformGateway | Autoload | picker、SAF、security scope、lifecycle | 游戏状态 |
| Telemetry | Autoload optional | 脱敏日志/metrics | 私有内容 |

## 创建与销毁

应用启动只创建 Autoload 和 Main scene。GameSession 在候选加载时创建；Bridge 只在原子提交后 attach。会话销毁顺序为取消请求/任务、断开视图、停止音频、释放资源桥接、清历史、关闭存储，最后 Dispose Core。Node 的 `_ExitTree` 不能反向调用已销毁 Session。

## 通信规则

父/编排器向下直接调用，组件向上 signal；兄弟间禁止直接引用。Core 的高频消息使用返回值/有界队列，应用级低频事件才进入全局 signal。详细接线见[GodotIntegration](GodotIntegration.md)。

## 模块验收

每个模块必须有公开行为测试、所有权测试和失败清理测试。组件场景必须可独立实例化；Core 模块必须在未安装 Godot 的 runner 构建。模块边界由[DependencyGraph](DependencyGraph.md)自动守卫。
