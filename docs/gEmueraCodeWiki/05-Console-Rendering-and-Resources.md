# 控制台显示、Godot 渲染、图片与资源

## 分层模型

显示不是从 `Process` 直接创建 Godot control。当前默认路径先形成 legacy console display model，再通过主线程队列投影到 Godot。

```text
ERB instruction / function
  -> EmueraConsole.Print* / style mutation
  -> PrintStringBuffer
  -> ConsoleDisplayLine[]
       └─ AConsoleDisplayPart family
           ├─ ConsoleStyledString
           ├─ ConsoleImagePart
           ├─ ConsoleShapePart
           ├─ ConsoleDivPart
           └─ ConsoleButtonString
  -> GenericUtils.EnqueueUI(...)
  -> EmueraMain._Process() / GenericUtils.FlushUI()
  -> EmueraContent
       ├─ Control-node projection / layout / scroll / input
       └─ ConsoleRenderSurface (canvas-oriented projection)
  -> EmueraImage / Label / Button / ColorRect / input controls
```

**边界：** `GameView` 拥有显示语义；`EmueraContent` 和其 Godot children 拥有 presentation/Node 生命周期。Core display DTO 目前可 tee/observe，但默认 renderer 尚未获准切换。

## Legacy console model：`GameView/`

| 类型 | 责任 |
| --- | --- |
| `EmueraConsole` / `EmueraConsole.Print.cs` | 输出 API、显示 line list、style/current line、button、输入等待、timer、错误与 console lifecycle。 |
| `PrintStringBuffer` | 累积 styled text/part/button，按测量结果 flush 为 `ConsoleDisplayLine[]`；控制单行缓冲与 line-end metadata。 |
| `ConsoleDisplayLine` | 一个 logical display line 的 immutable-ish display composition/metadata 载体。 |
| `AConsoleDisplayPart` | 文本、图片、形状、div 等 part 的基类/抽象语义。 |
| `ConsoleStyledString` / `StringStyle` | 文字内容、字体、颜色、对齐、背景等 style。 |
| `ConsoleButtonString` / `ButtonStringCreator` | 可点击文本/区域与其输入 payload。 |
| `ConsoleImagePart` / `ConsoleShapePart` / `ConsoleDivPart` | 图片、图形、HTML div 的显示语义。 |
| `HtmlManager` | HTML 转 display line/part、escape/plain text/长度等语义。 |
| `StringMeasure` / `FontMeasureCache` | 排版测量与缓存。 |
| `HotkeyState` | keyboard/macro 等 console 输入状态。 |

### 输出形成

`PrintStringBuffer` 将连续同 style 的文本聚合为 `ConsoleStyledString`，必要时将其转为 `ConsoleButtonString`，随后：

1. 使用 `StringMeasure` 将 buttons/parts 切成物理 `ConsoleDisplayLine`；
2. 将当前 line metadata 附到每一行；
3. 清空 buffer；
4. `EmueraConsole` 将 line 加入其 display list 并通知 Godot bridge。

改动 output 语义时，不能只看文本：button hit region、style、HTML image/source switching、line end、临时行、backlog、scroll、animation 和 input wait 都可能依赖同一 line model。

## Godot display host：`EmueraContent`

文件群：

- `Scripts/EmueraContent.cs`
- `Scripts/EmueraContent.Canvas.cs`
- `Scripts/EmueraContent.AndroidSpriteAnime.cs`
- `Scripts/EmueraContent.M0.cs`
- `Scripts/EmueraImage.cs`
- `Scripts/Panels/Inputpad.cs`、`QuickButtons.cs`、`Scalepad.cs`、`OptionWindow.cs`（M2 组件化面板）
- `scenes/*.tscn`（上述面板的 `.tscn` 场景资产）

### `EmueraContent`

`EmueraContent : Control` 是 legacy bridge 的全局访问点（`EmueraContent.instance`），负责：

- 创建/管理 console viewport、滚动容器、缩放 root、line/container nodes；
- 将 `ConsoleDisplayLine` 和 part 投影为 Godot control 或 canvas draw 内容；
- 保持 fixed-line-height 和 image offset 的兼容 layout；
- 显示/隐藏和定位 text input，处理 pointer/keyboard/button hit；
- 管理滚动、最长 line、回收旧行、M0 observation hooks；
- 与 `EmueraConsole`、`EmueraThread`、`GenericUtils` 协调输入/刷新。

`EmueraContent.Canvas.cs` 中的 `ConsoleRenderSurface` 是 canvas-oriented renderer 的定义位置。涉及 Canvas 与 Control 双路径时，必须保持其可观察 line/button/image 语义一致；不要把一个路径的优化当成另一条路径已同步。

### 固定行高与图片叠加

legacy 兼容显示采用固定行高模型：

- 每个 `ConsoleDisplayLine` 占据有效 line height；
- 图片可用负 Y offset 覆盖在先前行上方；
- 内容可能需要允许 overflow，不能因简单裁剪改变原脚本视觉布局；
- 行/node 数量有上限与批量回收逻辑，避免不受限 backlog 增长。

布局、hit testing 和 scroll 改动必须验证文本、图片、shape、HTML div、button、动态地图与触摸路径，而不是只看简单文字场景。

### HTML font、ARGB 与 div 不变量

`HtmlManager`、`ConsoleStyledString`、`ConsoleDivPart` 和 Godot projection 必须作为一条端到端链路维护：

- `<font size>`、`render`、`edging`、`hinting`、`valign` 会进入 `ConsoleStyledString`，嵌套 font 继承这些字段，`DivideAt` 与 display diff/reuse 也必须保留或比较它们。
- `size` 与 `valign=top|middle|bottom` 已在 legacy draw、Canvas renderer 和 Control renderer 三条路径中接线。Godot 的未显式 `valign` 路径继续保持既有居中 baseline；显式 top/middle/bottom 分别使用 `0`、`freeSpace/2`、`freeSpace` 偏移。
- `render`、`edging`、`hinting` 当前会被保留并参与 HTML round-trip，但 Godot renderer 不会为单个 text run 切换到 WinForms GDI/Skia backend；不能把“属性未丢失”表述为参考 backend 行为已完全实现。
- HTML 颜色的未设置 sentinel 是 `int.MinValue`。`#RRGGBB` 自动补 `FF` alpha，`#AARRGGBB` 按完整 32-bit ARGB 解析；带 alpha 的 ARGB 可能是负 `int`，consumer 禁止用 `< 0` / `>= 0` 判断颜色是否存在。
- `uEmuera.Drawing.Color.FromArgb(int)` 必须 mask 高字节；div background、shape/text color 与 Godot `ColorRect` border 都必须保留 alpha。
- `<div>` 省略 `height` 时，`ConsoleDivPart` 使用 `children.Length * Config.LineHeight + padding.top/bottom + border.top/bottom`。自动高度不应被序列化为脚本中不存在的显式 `height`。

Canvas/Control 双路径的文字字号、baseline、alpha 与 div box model 任一处不同步，都会造成桌面/Android 或 backend 切换后的可观察差异。

## 主线程 UI queue

文件：`Scripts/GenericUtils.cs`、`Scripts/EmueraMain.cs`

```text
legacy worker or background source
  -> GenericUtils.EnqueueUI(action, displayWork?)
  -> ConcurrentQueue<Action>
  -> EmueraMain._Process()
  -> GenericUtils.FlushUI()
       - platform-specific per-frame action cap
       - platform-specific time budget
       - catches/logs UI exceptions
  -> action touches EmueraContent/Godot controls on main thread
```

`GenericUtils` 保存主线程 id，并让 `FlushUI()` 按 Android/desktop 的 action count 与 microsecond budget 消费队列。这个限流是 UI 流畅性与 trace batching 的一部分：

- 不要从 legacy worker 直接创建/修改 Godot `Node`、`Texture2D`、`ImageTexture` 或 shader material；
- 不要在一个 UI action 中做无法受预算控制的大量同步 I/O/解码；
- queue batch 的可见时序可能影响 trace，不能用“内容最终出现”忽略 ordering evidence。

## 输入回流

```text
Godot input / pointer / button / Inputpad
  -> EmueraContent hit-test and input UI
  -> GenericUtils input helpers / EmueraThread.Input(...)
  -> ManualResetEventSlim wakes legacy worker
  -> EmueraConsole.PressEnterKey / callEmueraProgram
  -> Process writes RESULT/RESULTS and resumes script
```

重要规则：

- UI 只把 intent / value 交给 console/thread；不得直接改 Core state 或绕过 `InputRequest` 的 type/validation。
- pointer metadata 对 eraFL `INPUTS ,1` 等兼容场景可能需要一次性同步 `RESULTS:0/1`，不能拆成两个独立输入提交。
- `NoFocus`、`OneInput`、timer、skip/macro、button hover/source switching 都属于 observable console input contract。

## 图片、精灵与 graphics

### Legacy content owner

| 文件 | 关键类型 | 责任 |
| --- | --- | --- |
| `AppContents.cs` | `AppContents`、`LazySpriteDefinition` | 内容目录、图片/sprite 定义、动态创建/释放和缓存入口。 |
| `ConstImage.cs` | `AbstractImage`、`ConstImage` | 静态图片资源模型。 |
| `CroppedImage.cs` | `ASprite`、`SpriteG`、`SpriteF`、`SpriteAnime` | sprite、crop、frame/anime 等 image composition。 |
| `GraphicsImage.cs` | `GraphicsImage` | legacy Graphics surface 与 draw operation。 |
| `FontModel.cs` / `FontMeasureCache.cs` | font/cache | 字体与测量缓存。 |
| `SpriteManager.cs` | texture cache / cleanup / upload coordination | 文件 I/O、主线程 texture upload、延迟清理。 |
| `AnimatedWebpSpriteFrames.cs` | frame cache/decoder queue | 动画 WEBP 的后台 decode 与主线程纹理发布协调。 |

### `EmueraImage`

`EmueraImage` 是 Godot-side image control：

- 保持 texture、source region、draw offset/size、裁剪/翻转/层级等图像 presentation；
- HTML `src` / `srcb` 可在同一 node 内切换 normal/selected source，避免改变原有层级/ColorMatrix/crop contract；
- 通过 `AnimatedWebpSpriteFrames.Acquire(...)` 获得动画 frame sequence，并由自身 process 更新 frame；
- 通过 `SetColorMatrix(...)` 获得共享的 shader material，而不是修改可能正被其他 image 使用的 material。

### ColorMatrix

| 路径 | owner | 约束 |
| --- | --- | --- |
| GPU | `EmueraMain` GPU work queue、`ColorMatrixGPU`、shader material / SubViewport 路径 | 只在支持的桌面主线程 Godot rendering 条件下创建/使用 GPU 资源。 |
| CPU fallback | image/graphics conversion path | Android 或 GPU 不可用时保持兼容结果；避免跨线程 Godot image/texture 创建。 |

ColorMatrix 的矩阵约定、源/region、alpha、shared material cache 与图片动画都相互影响。任何优化需覆盖静态图、HTML srcb、sprite、mask/graphics、Android fallback 与 resource cleanup。

### SpriteManager 的线程角色

`EmueraMain._Process()` 调用 `SpriteManager.UpdateCleanup()` 和 `SpriteManager.UpdateOtherThreads()`。这意味着：

- 后台可以准备 I/O/decoded data；
- Godot texture upload/cleanup 由主线程逐帧处理；
- 移动端需要限速，避免一帧大量纹理创建导致 UI 卡顿；
- 不能把 “后台已读到文件” 等同于 “Godot texture 已可安全显示”。

### M1 渲染/资源性能现状（语义零漂移）

第二轮性能优化（M1）在保留可观察语义的前提下引入了缓存与预算：

- `EmueraContent` 的 `graphicsImageTextureCache` 增加会话级字节预算与 LRU 淘汰；渲染作用域对正在显示的纹理做 pin，`_Process` 周期清扫退役条目；`GraphicsImage` 重建经 `IsCreated` 判定失效并重新上传。
- `SpriteManager` 的 `TextureInfo` 按 CPU image + GPU 纹理双份核算内存；Android 上传后释放 CPU image 副本，`GetPixel/SetPixel/Save` 等访问时按 `sourcePath` 重新解码，脚本像素语义不变。
- `AnimatedWebpSpriteFrames` 增加并发解码上限与帧纹理字节预算，超限保留首帧回退静态显示。
- `StrForm` 改用 `DefaultInterpolatedStringHandler`，`PrintStringBuffer` 缓存长度增量，`CheckEscape`/`PRINTFORMS` 无格式令牌时短路完整管道。

这些优化不改变显示的 line/button/image/HTML 语义；Canvas/Control 双路径的一致性要求不变。

## HTML、shape 与 graphics 关注点

`HtmlManager` 将 HTML 语义转为 legacy display parts；`ConsoleImagePart`、`ConsoleShapePart`、`ConsoleDivPart` 最终由 `EmueraContent`/canvas/control 渲染路径投影。

当变更下列功能时，需要同时评估 display model 与 Godot projection：

- `<img>`、`srcb`、crop、flip、ColorMatrix、animated WEBP；
- shape / rect / custom graphics draw；
- div、nested div、overlay、dynamic map；
- button z-order、pointer hit test、hover/press state；
- line recycle、scroll position、backlog、M0 display observation。

## 资源与销毁不变量

- legacy worker 不拥有 Godot Node/Resource/RID 生命周期；
- session/scene 切换前必须确保旧 worker 安静，并清理旧 generation 的 image/queue completion；
- `SpriteManager` cache、animated WEBP references、shared shader material、graphics resource 和 UI nodes 不能因一个局部 clear 而遗留到下一会话；
- `ClearForCanarySessionTransition` 等 canary 清理只能在 Godot main thread 运行；
- current display behavior owner 仍是 legacy renderer；Core `DisplayTransaction` 只能 tee/observe，未获准替换默认 presentation。

## 相关页面

- ERB output/instruction/wait：[`03-Legacy-Interpreter.md`](03-Legacy-Interpreter.md)
- graphics/sprite expression function 与 variable/registry：[`04-Legacy-Data-and-Expressions.md`](04-Legacy-Data-and-Expressions.md)
- Host/Core display/resource candidate：[`06-GodotHost-and-Core.md`](06-GodotHost-and-Core.md)
- queue/threads/forbidden edges：[`07-Dependencies-and-Threading.md`](07-Dependencies-and-Threading.md)
- diagnostics/performance switches：[`08-Operations-Testing-and-Diagnostics.md`](08-Operations-Testing-and-Diagnostics.md)
