# 关键类型、状态与端口契约

本页定义跨模块稳定模型。代码均为 C# 风格伪代码；锁定 Godot 版本后，完整 Bridge 示例必须移入 ApiSmoke。

## 会话与切换

```csharp
record GameSelection(ContentToken Source, GameIdentity? Expected);
record SwitchResult(SwitchStatus Status, long Generation, PublicError? Error);

sealed class GameSession : IAsyncDisposable
{
    long Generation { get; }
    GameConfig Config { get; }
    CompatibilityPlan Compatibility { get; }
    VmMachine Vm { get; }
    VariableStore Variables { get; }
    ResourceCatalog Resources { get; }
    PixelStore Pixels { get; }
    RuntimeDataStore ExtendedData { get; }
    DisplayHistory Display { get; }
    SaveService Saves { get; }
    CancellationToken Lifetime { get; }
}
```

GameSession 构造阶段不发布自身；候选全部验证后由 SessionCoordinator 原子交换。Dispose 幂等，先取消 Lifetime，再等待/观察受控任务并释放资源。

`CompatibilityPlan` 包含冻结的 `DialectPlan`、SaveProfile、资源行为和脚本可观察 runtime capability binding。它只在候选构建阶段产生，提交后不能热换；Parser/VM 只消费窄注册表/typed policy，不读取全局游戏名。类型和解析规则见 [DialectExtensionSystem](DialectExtensionSystem.md)。

## VM 模型

```csharp
record VmFrame(
    FunctionId Function,
    LogicalLineId ParentLabelLine,
    LogicalLineId InstructionPointer,
    ImmutableArray<VmValue> Locals,
    ReturnAddress ReturnTo,
    FrameKind Kind);

record VmContinuation(
    ImmutableArray<VmFrame> CallStack,
    ImmutableArray<VmValue> ValueStack,
    ImmutableQueue<EventInvocation> Events,
    WaitState Wait,
    long ResumeSerial);

record VmBudget(long DeadlineTimestamp, int MaxInstructions, long MaxWorkUnits);
record VmBatchResult(VmStopReason Reason, ImmutableArray<VmEffect> Effects, VmFault? Fault,
                     long FirstSequence, long LastSequence);
```

`VmValue` 至少是 Integer/String/Float/Reference/Void 的判别联合；Float 覆盖 `#FUNCTIONF`、LOCALF/ARGF/RESULTF、REFF/2D/3D 和小数数组。`InstructionPointer` 指下一条待执行逻辑行；指令成功后才推进。M2–M5 可由专用线程中的 legacy runner 适配成 batch；只有 cooperative 实验才要求每条路径完全 continuation 化。

## WaitState

| 类型 | 必需字段 | 恢复条件 |
| --- | --- | --- |
| None | — | 继续 Step |
| Input | request id、generation、kind、deadline | 唯一成功/取消/超时 completion |
| Message | skip policy、generation | 消息继续 intent |
| PortOperation | operation id、kind、generation | queue completion |
| YieldBudget | resume serial | 下一调度机会 |

WaitState 只能由 VM 转换；View 无权直接改为 None。Resume 检查 request id、generation 和完成 CAS，重复/迟到结果返回 `IgnoredStale`。

## 效果 DTO

```csharp
abstract record VmEffect(long Sequence, CompletionMode Completion);
record DisplayTransactionEffect(long Sequence, DisplayTransaction Transaction)
    : VmEffect(Sequence, CompletionMode.CommitThenProject);
record RequestInputEffect(long Sequence, InputRequestDto Request)
    : VmEffect(Sequence, CompletionMode.WaitPort);
record AudioEffect(long Sequence, AudioCommand Command, ResourceKey Key, int Volume,
                   CompletionMode Completion) : VmEffect(Sequence, Completion);
record TextureRevisionEffect(long Sequence, PixelHandle Handle, long Revision, IntRect? Dirty)
    : VmEffect(Sequence, CompletionMode.CommitThenProject);
record PortRequestEffect(long Sequence, OperationId Operation, PortCommand Command,
                         CompletionMode Completion) : VmEffect(Sequence, Completion);
record ApplicationEffect(long Sequence, ApplicationCommand Command)
    : VmEffect(Sequence, CompletionMode.WaitPort);
```

Effect payload 不含 Node/Texture/Image/Color/Rect2/Variant/Callable。Sequence 用于 effect 幂等和差分时间线。

## Display 模型

```csharp
record DisplayLine(LineId Id, ImmutableArray<DisplayPart> Parts, LineAlignment Alignment,
                   bool NoWrap, long Revision, DisplayLineMetadata Metadata);
abstract record DisplayPart(PartId Id, StyleToken Style, Interaction? Interaction);
record TextPart(..., string Text) : DisplayPart(...);
record ImagePart(..., ResourceKey Normal, ResourceKey? Selected, SizeSpec Size,
                 PositionSpec Position, DisplayMode Display, bool FlipX, bool FlipY,
                 ColorMatrixRef? ColorMatrix) : DisplayPart(...);
record ShapePart(..., ShapeKind Kind, ImmutableArray<long> Parameters) : DisplayPart(...);
record DivPart(..., PositionSpec Position, SizeSpec Size, int Depth, DisplayMode Display,
               Rgb24? Background, BoxStyle Box,
               ImmutableArray<DisplayLine> Children) : DisplayPart(...);
record Interaction(ButtonValue Value, string? Tooltip, int? LockedX);
record BoxStyle(EdgeInsets Margin, EdgeInsets Padding, EdgeInsets Border,
                CornerRadii Radius, EdgeColors BorderColors);
record DisplayTransaction(long Revision, int RemoveBottomCount,
                          ImmutableArray<DisplayLine> Upserts,
                          ImmutableArray<DisplayDataPatch> DataOnly,
                          ScrollIntent Scroll, bool AtomicVisibility);
```

`DisplayMode` 覆盖 Relative/Absolute/AbsoluteLeftTop/AbsoluteLeftBottom；Position/Size 保留 px 与 font-relative 单位。Div 不折叠为换行：子行、负坐标、depth、margin/padding/border/radius/bcolor 和溢出都是稳定模型。`DisplayLineMetadata` 至少保留 BitmapCache/dynamic-map scope；data-only patch 可只替换 Interaction/input generation。Color 用 Core 值对象，Bridge 才转 Godot 类型；Display 不保存 Texture 强引用。

## InputRequestDto

字段对应 XEmuera/gEmuera `InputRequest`：`RequestId`、`InputKind`、`OneInput`、`NoFocus`、`StopMessageSkip`、`IsSystemInput`、默认整数/字符串、`TimeLimit`、`DisplayTime`、`TimeUpMessage`，另加 `SessionGeneration` 和严格完成状态。NF 指令通过 `NoFocus=true` 表达，不创建第二套等待状态。

## 资源模型

```csharp
readonly record struct ResourceKey(string CanonicalUpperName);
record SourceFileToken(string NormalizedRelativePath);
record SpriteDescriptor(ResourceKey Key, SourceFileToken Source, IntRect Crop,
                        IntPoint Offset, ImmutableArray<AnimationFrame> Frames);
record AnimationFrame(SourceFileToken Source, IntRect Crop, IntPoint Offset, int DelayMs);
```

ResourceKey 不是文件路径；SourceFileToken 只能由受控内容源解析。Catalog duplicate policy 与 AppContents fixture 一致。

## Save 模型

`SaveSnapshot` 是深不可变结构，包含 `SaveProfileId`、游戏身份、脚本版本、说明、角色、Integer/String/Float 变量和该操作允许的 VarExt domain snapshot；不包含 Node、Texture、文件句柄、pending Task 或未持久化 VM continuation。`ParsedSave` 也是候选对象，完成格式/profile/限制验证后才能按域原子提交。

`ISaveStorage` 提供 `CreateUniqueTempAsync`、`FlushAsync`、`ReplaceAsync`、`RecoverAsync`；slot 先转为受控 `SaveSlot`。平台降级协议由[SaveLoadSystem](SaveLoadSystem.md)定义。

## Fault 与结果

所有可预期失败使用 typed Result/Fault，不用 `null` 混淆缺失与损坏：`VmFault`、`SaveFormatFault`、`ResourceFault`、`PlatformFault`、`CancellationResult`。Fault 包含公开错误码、脱敏上下文和内部 correlation id，不携带用户脚本全文。

## 禁止公开类型

Core assembly 公共/内部边界不得出现 Godot Node、Texture2D、ImageTexture、Image、Color、Rect2、Variant、Callable、GodotObject、`[Signal]` 或 `EmitSignal`。架构测试通过 reflection 与源码扫描双重检查。
