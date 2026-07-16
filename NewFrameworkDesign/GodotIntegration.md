# Godot Bridge、场景与组件接线规范

本章把纯 .NET Core 映射到 Godot 应用层。它遵循 `godot-master` 的应用组合规则：编排器向下调用、组件向上发 signal、同级组件不直接通信；表现层只投影状态，不拥有 VM/变量/存档事实。

## 目标场景树

```text
Main.tscn (Control, MainOrchestrator.cs)
├─ SessionHost (Node, SessionBridge.cs)
│  ├─ VmHost (CLR service, dedicated owner thread; not a Node)
│  ├─ UiBatchPump (Node, UiBatchPumpComponent.cs)
│  ├─ PortQueue (Node, MainThreadPortQueue.cs)
│  ├─ AudioBridge (Node, AudioBridgeComponent.cs)
│  │  ├─ BgmA (AudioStreamPlayer)
│  │  ├─ BgmB (AudioStreamPlayer)
│  │  └─ SfxPool (Node)
│  └─ ResourceBridge (Node, ResourceBridgeComponent.cs)
├─ SafeArea (MarginContainer)
│  └─ RootLayout (VBoxContainer)
│     ├─ Toolbar (HBoxContainer)
│     ├─ ConsoleViewport (Control, backend selected by ADR)
│     ├─ InputPanel (Control, InputPanelOrchestrator.cs)
│     └─ StatusBar (HBoxContainer)
├─ ModalLayer (CanvasLayer)
│  ├─ FilePickerOverlay
│  ├─ ErrorDialog
│  └─ LoadingOverlay
└─ AccessibilityLayer (CanvasLayer)
   └─ AccessibleConsoleFallback (Control/RichTextLabel)
```

`MainOrchestrator` 只接线和管理应用状态，不解析脚本、不修改变量、不计算布局。组件通过 Inspector 的 typed export 或构造注入获得依赖；不得通过 `get_parent()` 猜父类型或使用易碎绝对 NodePath。场景内部可使用 `%UniqueName`。

## Autoload

目标上限为四个，初始化顺序固定：

| 顺序 | Autoload | 职责 | 禁止状态 |
| ---: | --- | --- | --- |
| 1 | `AppBootstrap` | 读取 lock/应用设置、注册端口工厂、创建 Main | 游戏变量、资源缓存、存档 |
| 2 | `PlatformGateway` | Android/iOS/桌面回调与 capability query | 当前游戏业务状态 |
| 3 | `SessionCoordinator` | 发布 generation/取消候选、短 commit gate、当前会话引用 | 持锁执行长加载、直接解析/渲染/播放 |
| 4 | `Telemetry`（可选） | 脱敏日志、性能采样、崩溃 breadcrumb | 私有路径、脚本内容 |

Autoload 的 `_init()` 不访问其他 Autoload；显式 `InitializeAsync` 在 Main 启动阶段按顺序调用。Resource、Save、VM、GameConfig、输入请求和 UI 历史全部归 `GameSession`，不放入 Autoload。

## 当前迁移桥接（不是目标场景树）

当前 `main.tscn` 仍以 `EmueraMain` 为旧入口，尚未创建上方的 `SessionHost`/`VmHost` 场景树。它在 UI 与编码映射准备完成后创建 `LegacySessionFacade`，将 `SessionSelection` 交给 Core；`Scripts/GodotHost/LegacySessionBackend` 是唯一允许调用 legacy `GlobalStatic.Reset()` 与 `EmueraThread.Start/End` 的新增 bridge。后端启动成功后，Core lease 才提交 `Current`；失败、取消或 stale 时由 façade 尝试恢复旧后端。

该 bridge 只收口生命周期，不能把 `CompatibilityPlan` 误称为 legacy Parser/VM 的输入。候选构造目前是同步纯 Core 工作，因此启动调用仍发生在 `EmueraMain` 的 Godot 主线程；未来一旦候选包含异步 I/O，backend 必须经显式主线程 dispatcher 回到 SceneTree，不能依赖 continuation 的线程上下文。`_ExitTree` 维持旧的同步顺序：停止 legacy thread、重置 legacy root、再释放 bridge 资源。

## 信号与调用矩阵

| 方向 | 示例 | 机制 |
| --- | --- | --- |
| 编排器 → 组件 | `SessionHost.Attach(session)`、`ConsoleViewport.Apply(batch)` | 直接 typed method call |
| 组件 → 编排器 | `input_submitted(requestId, value)`、`file_selected(token)` | Godot signal，参数仅 Variant-safe |
| VM thread → Bridge | `VmToUiBatch`（Display/texture/audio/application effects） | 有界 CLR channel；不走 Godot signal 大 payload |
| Bridge worker → 主线程 | decode result、file result | 有界 `Channel<T>`/队列，主线程 pump |
| 同级组件 | InputPanel → VmHost/UiBatchPump | 禁止；先 signal 到 orchestrator，再向下调用 |

Godot signal 不携带 `GameSession`、`GameBase`、`InputRequestDto` 等普通 CLR 对象。传递 `long requestId`、`long generation`、字符串/数值/数组等 Variant-safe 值，或由主线程 Callable 闭包捕获 CLR 对象。动态 signal 在 `_ExitTree` 解除；捕获 lambda 保存 token 以便显式断开。

## 主线程批次泵

默认 `UiBatchPump` 在 `_Process` 中只 drain 专用 VM thread 已提交的不可变 batch，并分别限制 reduce、layout request、texture upload 和 audio command 数量。VM 不在这个 Node 上执行。主线程 cooperative 仅在 `vm.main_thread_experiment` 构建中替换 runner，并且仍受完整 effect/layout 预算约束。

```csharp
// 伪代码；锁定 Godot 版本后进入 ApiSmoke。
public override void _Process(double delta)
{
    ForwardPlatformCompletionsToVm(MaxPortCompletionsPerFrame);
    ReduceVmBatches(MaxBatchesPerFrame, MaxReduceUsec);
    ProcessTextureUploads(MaxUploadBytesPerFrame);
    RequestVisibleLayout(MaxLayoutUsec);
}
```

VM owner thread 可执行解释器、变量、RuntimeDataStore 和 PixelStore 的串行状态操作；普通 workers 只处理不可变输入/独占流。任何 Node、SceneTree、Control、AudioServer 或 Godot Resource 的创建/修改回主线程完成。队列有容量、背压和取消策略，旧 generation completion 在解包前丢弃。

## View 组件的 Rock Test

- `ConsoleViewport` 只依赖 `IReadOnlyList<DisplayLine>`、Theme metrics 和 viewport size；换成测试数据也能运行。
- `InputPanel` 只依赖 `InputPresentation` 和 submit/cancel signal；不知道 VM 或 VariableStore。
- `AudioBridge` 只消费 `AudioEffectDto` 和资源句柄；不知道脚本指令名称。
- `ResourceBridge` 只把逻辑资源描述转换为 Godot resource handle；不修改 Core descriptor。

每个 UI 场景必须通过 F6/独立场景测试：没有 Autoload 时使用 inspector 注入的 stub context，不得因缺少当前游戏而崩溃。

## UI 布局与 Theme

根布局使用 Container 和 size flags，不用绝对像素定位主界面。透明覆盖层的 `mouse_filter` 为 PASS/IGNORE，只有真正模态背景 STOP。触摸目标有效尺寸不低于 48dp（iOS 至少 44pt）。SafeArea 根据 `DisplayServer.get_display_safe_area()` 更新 MarginContainer。

Theme 只在根 Control 指定，所有子控件继承；按钮危险/链接/输入候选使用 `theme_type_variation`，不复制场景。运行时修改共享 StyleBox 前必须 duplicate；自绘后端缓存 Theme/Font 引用，并在主题变化通知后重建测量缓存。焦点样式始终可见，高对比 Theme 使用更粗轮廓。

## 生命周期

节点退出树时按顺序：停止接收新 effect → 取消 view CTS → kill/bind 的 Tween → 断开 signal → 停止 AudioStreamPlayer → 释放派生 Texture 引用 → 清空 Node pool。SceneTree Tween 必须绑定生命周期；不能在 `_Process` 每帧创建 Tween。

## API smoke 范围

锁定工具链后最小工程必须编译验证：`CanvasItem.DrawString`/`Font.DrawString` 或 TextLine/TextServer 的实际签名、Theme API、Signal 参数、CallDeferred/Callable、AudioStreamPlayer、FileAccess、ResourceLoader threaded API、DisplayServer safe area 和应用暂停/恢复通知。未进入 smoke 工程的完整代码片段只能标为伪代码。
