# CODE_MAP

## 2026-07-07 eraFL CSV sprite 生命周期与同名 fallback 修复

- `AppContents`：新增 CSV sprite 名称登记表，`LoadContents()`/懒加载 CSV 索引阶段都会登记资源名；`SpriteDisposeAll(false)` 改为只清动态创建的 sprite，保留 `BG01`、立绘等 CSV 定义资源，`SpriteDisposeAll(true)` 才完整清空。
- `AppContents.BuildLazyResourceIndex`：普通 sprite 分支重新写入 `lazyImageDictionary[spriteName] = definition` 并登记 CSV 名称，避免从 snake profile/lazy 模式启动时 `BG01` 未进入懒加载索引。
- `EmueraContent.ShouldUseRawImageResourceFallback` 与 `ConsoleImagePart.TryResolveDynamicImageWidth`：CSV sprite 名称即使当前纹理未就绪，也禁止递归按裸文件名搜索同名图片或推断宽度，阻断 eraFL `BG01` 从 `resources/SYSTEM/BG.csv` 串到 `resources/mapimage/bg01.webp`。
- 兼容边界：这只改变 CSV sprite 生命周期和 HTML 图片 fallback 优先级；真实裸文件路径、动态 cutin、`SPRITECREATEFROMFILE` 生成的非 CSV sprite 仍按原路径解析。

## 2026-07-07 eraFL HTML div 行稳定优先渲染

- `EmueraContent.Canvas.CanRenderPartOnCanvas`：所有包含 `ConsoleDivPart` 的行不再进入 Canvas div overlay 优化路径，而是整行退回 Control 渲染；Canvas 仍负责普通文本、形状和简单图片。
- 根因：eraFL 的状态栏底图、`DRAW_PORTRAIT`/`DRAW_STILL` 立绘、房间/地图框大量依赖 `<div><img></div>` 的裁剪、`depth`、负坐标和跨行叠放。Canvas div overlay 虽能减少节点，但在异步补图、负 y 和兄弟 div 层级上仍存在边界差异。
- 取舍：这是稳定优先方案，可能增加 HTML/div 密集页面的 Godot Control 节点数；但避免全局切回 Controls，普通文本和简单图片仍走 Canvas 快路径。若后续要恢复性能优化，应先用 eraFL 状态栏、立绘、住房/地图界面做逐项视觉回归。

## 2026-07-07 eraFL HTML 图片 div 异步刷新补强

- `EmueraContent.IsPureImageLine`：纯图片行判定递归识别 `ConsoleDivPart` 子树，兼容 eraFL `DRAW_PORTRAIT`、`DRAW_STILL` 和 `SHOW_STATUS.ERB` 状态栏底图常用的 `<div><img ...></div>` 写法。图片还在异步解码/上传时，这类包装图片会和顶层 `<img>` 一样延后提交，避免先显示空 div/spacer 后表现为“有框没图”。
- `RegisterCanvasImageOverlays` / `RegisterCanvasDivOverlays` 以及对应释放路径：overlay 节点集合变化后显式标记 `canvasOverlayRowsDirty`，确保批量输出和异步补图后按最新 `lineLayoutEntries` 重新定位 Canvas overlay。
- 影响范围：仅改变 Canvas 后端对 HTML 图片 div 首帧未就绪与 overlay 重定位的处理；含文字的 div、按钮行和普通文本不进入纯图片延后路径。

## 2026-07-07 eraFL CBG/SETIMAGELAYER 换图刷新触发

- `EmueraConsole.CBG_Clear` / `CBG_ClearRange` / `CBG_SetImage` / `CBG_SetButtonMap` / `CBG_SetButtonImage` 以及 `AddBackgroundImage` / `RemoveBackground` / `SetImageLayer` / `ClearImageLayer*`：背景图层列表或按钮图层状态发生变化后统一调用 `RequestCbgRefresh()`，只唤醒 `uEmuera.Window.MainWindow.Refresh()` 的 dirty 标记，由现有 `Window.Update()` 在下一帧合并拉取 `cbgList` 并调用 `EmueraContent.RefreshCBG`。
- 根因：eraFL 图像显示库实际使用 `CBGSETG/CBGSETSPRITE/CBGSETBUTTONSPRITE/CBGSETBMAPG/CBGCLEAR/CBGREMOVERANGE` 做背景换图；这些函数已实现，但此前只修改后台 `cbgList`，若脚本本轮没有普通文本刷新，Godot 侧不会立即收到背景图层变化。
- 兼容边界：不在 CBG 函数里直接排 Godot UI 队列或重建节点，避免多次连续 `CBGSET*` 在移动端造成热路径抖动；刷新仍走原有显示桥和异步纹理重试逻辑。

## 2026-07-07 eraFL 立绘同步合成与 resources 路径回退

- `SpriteManager.GetTextureInfoForScriptComposition` / `BitmapTexture.EnsureTextureInfoForScriptComposition`：为 ERB 图像合成链路提供同步真实像素读取，不把解码失败或未就绪的占位纹理当作可合成源；若已有占位缓存且本次读到真实图，会覆盖占位缓存。
- `GraphicsImage.GCreateFromF` / `GraphicsImage.GDrawCImg`：`GCREATEFROMFILE`、`GLOAD`、`GDRAWSPRITE` 等脚本合成命令改为按真实绘制成功返回 `1/0`，失败时不再留下 `IsCreated=true` 的空图，避免后续 `SPRITECREATE` 生成空立绘。
- `Creator.Method.ResolveGraphicsResourceFilePath` / `AppContents.ResolveDynamicSpriteFilePath`：相对图片路径按游戏根目录优先、`resources/` 目录回退解析，并避免显式 `resources/` 前缀被拼成 `resources/resources`。这用于兼容 eraFL 固定立绘路径如 `portrait/prt_FIX/*.webp` 实际位于 `resources/portrait/prt_FIX/` 的脚本写法。

## 2026-07-07 GetSpriteTexture 异步纹理 pending 追踪补全

- `EmueraContent.GetSpriteTexture` line 3704：修复 `ti.texture == null` 分支缺失 `TrackAsyncTextureRequestForCurrentRender()` 调用的问题。
- 根因：当 BitmapTexture 的 `CachedTextureInfo` 已存在但 `ti.texture` 为 null 时（ImageTexture.CreateFromImage 失败或首次 lazy create 时 image 解码未完成），原代码直接返回 null 且**未追踪 pending**，导致 `ProcessAsyncTextureRefreshes` 永远不会重试该行。
- 影响：eraFL 状态栏 BG01 底图首次渲染时纹理未就绪 → 画 spacer → 后续异步完成也不重刷 → 底图永久缺失。
- 修复后：即使 ti.texture 为 null 也追踪 pending，确保 `TextureLoadVersion` 递增后会触发 `AddLine(pendingLine, true)` 重绘。

## 2026-07-07 HtmlManager 支持 div 自闭合语法

- `HtmlManager.tagAnalyze` case "div"：检测 `<div ... />` 自闭合语法（wc 最后一个 token 是 `/` 即 OperatorCode.Div）。自闭合 div 直接返回空子行的 `ConsoleDivPart`，等价于 `<div ...></div>`，不再设置 `PendingDivTag` 等待 `ReadDivInnerHtml`。
- 根因：eraFL `SYSTEM/UI\CONTAINER/UI_CONTAINER_MAIN.ERB:96` 包含多个 `<div ... />` 自闭合标签，原 HtmlManager 不支持该语法导致解析失败，`SHOW_STATUS.ERB:70` 调用 `UIC_SHOW` 时抛出异常，line 239 的 BG01 状态栏底图输出根本没被执行。2026-07-07 前三次修复（003/004/005）都在修渲染路径，但渲染代码从未被调用过。

## 2026-07-07 eraFL 状态栏背景资源优先级修复

- `EmueraContent.ShouldUseRawImageResourceFallback`：HTML `<img src>` 先按 `AppContents.GetSprite` 解析 CSV sprite。只要 sprite 定义已经命中，即使本帧纹理仍在异步解码/上传中，也不再按裸文件名递归搜索同名图片；只有完全没有 sprite 定义时才走文件 fallback。该规则避免 eraFL `SHOW_STATUS.ERB` 的 `BG01` 从 `resources/SYSTEM/BG.csv` 指向的天空状态栏误落到 `resources/mapimage/bg01.webp`。

## 2026-07-07 eraFL 状态栏 div 子层级修正

- `EmueraContent.BuildConsoleButton` / `AddPartToContainer`：新增 `allowEscapedPartZ` 传递开关。普通控制台行里的大图仍可用 escaped `ZIndex` 跨出行高；但 `BuildDivControl` 渲染 div 子行时会关闭该抬升，避免 div 内背景图在 Godot 相对 `ZIndex` 下越过外层 HTML `depth`，盖住同批次后续文字 div。该规则用于兼容 eraFL `SHOW_STATUS.ERB` 中背景 div 与文字 div 叠放的状态栏。

## 2026-07-07 eraFL HTML div 层级与制表符测量兼容

- `EmueraContent.GetGodotZIndexForHtmlDepth`：HTML `depth` 仍按数值越大越靠后的语义排序，但整体映射到正向 `ZIndex` 基准之上，避免 Canvas 后端把 `depth='1'` 的 eraFL 房间框压到绘制面背后，只剩无 depth 的通路遮罩可见。
- `StringMeasure.GetDisplayLength`：包含 tab 的字符串不再在 `GRAPHICS` 模式下替换为 8 个空格，而是走固定半角/全角网格测量；这用于兼容 eraFL `TAG_PRINT` 多行字符串把源码缩进带入按钮片段时的底部选项排版。

## 2026-07-03 同名图片跨目录缓存隔离

- `SpriteManager`：文件纹理缓存改为以完整规范化路径为主要 key，不再把 `Path.GetFileName()` 作为全局别名；`GetSprite` 和旧同步 `Loading` 回调也改用同一套路径级 key。这样不同目录下同名 `webp/png/jpg` 不会复用同一个 `TextureInfo`，避免 TW 角色立绘在同名文件跨文件夹时串图。仅在请求名本身是路径或没有文件路径时才保留 name alias。

## 2026-07-03 普通输出追加行滚动修正

- `uEmuera.Window.DecideScrollModeForDisplayDelta`：动态地图函数栈或动态地图视图中的重绘仍使用 `PreserveViewport`，避免地图刷新拉回底部；非动态地图输出如果本批 diff 中存在 `LineNo > previousMaxLineNo` 的真实追加行，即使同时刷新了旧行元数据，也改为 `FollowBottom`。这用于修正 TW 会话/泡茶等普通输出在聊完后停在旧历史位置、不自动跟随最新文本的问题。

## 2026-07-03 动态地图函数栈标记

- `ProcessState.IsInDynamicMapFunctionScope` / `EmueraConsole.IsDynamicMapOutputScopeActive`：输出行生成时在 ERB 后台线程读取当前调用栈，只识别 `DRAW_COLOREDMAP`、`DRAW_MAP`、`FIELDMAP` 等地图绘制根函数；`GETMAP`、`MAP_VIEWING` 等子函数不再单独触发地图标记，降低非地图页面误伤。
- `ConsoleDisplayLine.DynamicMapFunctionScoped` / `PrintStringBuffer` / `EmueraConsole.PrintHtml`：给来自地图根函数的显示行打元数据标记，并递归标到 HTML div 子行；`GenericUtils.LineHasDynamicMapBitmapContext` 仍同时接受 `BITMAP_CACHE_ENABLE` 与函数栈标记作为诊断和地图块识别证据。
- `uEmuera.Window.DecideScrollModeForDisplayDelta`：滚动策略只用 `DynamicMapFunctionScoped` 或已确认的动态地图视图来判定地图重绘；普通 `BITMAP_CACHE_ENABLE` 页面不再直接触发地图滚动策略，避免颜色滑块、立绘履历等非地图 UI 被误判。
- `uEmuera.Window.TryFindDynamicMapWindowStart`：只在显示列表尾部有限范围内寻找动态地图上下文，避免历史中的旧地图块长期影响后续普通文本滚动策略。

## 2026-07-03 去除动态地图视图裁剪实验

- `uEmuera.Window.Update`：动态地图检测仍保留，用于 `dynamicMapViewActive`、诊断日志和滚动策略；但不再把 `displayStartIndex` 裁到地图块开始行，也不再在进入/离开动态地图时调用 `GenericUtils.ClearText()` 重建 Godot 显示层。历史文本会继续参与行级 diff，进入地图后理论上可向上查看前文。
- 风险说明：这会恢复历史内容可见性，但也可能重新暴露 Android 上旧内容、地图块、选项一起刷新时的自动滚动或闪烁问题；本改动用于验证“视图裁剪是否是历史消失主因”。

## 2026-07-03 动态地图滚动事务第一步

- `GenericUtils.ApplyTextChanges` / `EmueraContent.ApplyTextChanges`：在保留旧 `scrollToBottom: bool` 入口的同时新增 `EmueraDisplayScrollMode`，用于把显示刷新后的滚动意图从简单布尔值扩展为“追底部、保留视口、保持当前选项可见”等模式。旧调用方仍按原语义工作，动态地图链路可以逐步迁移到更细的滚动策略。
- `uEmuera.Window.DecideScrollModeForDisplayDelta`：显示差异提交前根据删除尾行、更新旧行、追加新行和动态地图视图状态决定滚动模式。当前动态地图视图直接使用 `PreserveViewport`，避免刷新时拉回底部；`KeepChoicesVisible` 保留为可扩展模式，但不再用于动态地图。
- `EmueraContent.RequestKeepChoicesVisible`：保留“保持当前选项可见”的实现入口，等待 Godot 布局帧稳定后按当前按钮 generation 找选项并做最小补偿；当前动态地图链路不会触发该模式。

## 2026-07-02 动态地图刷新合并补充

- `EmueraConsole.RefreshStrings` / `deleteLine` / `BitmapCacheEnabledForNextLine`：动态地图或状态面板进入 `CLEARLINE`、`BITMAP_CACHE_ENABLE 1...0` 区域重画时，Running 中的普通 `RefreshStrings(false)` 会先合并，不向 Godot UI 提交半成品；进入 `INPUT/TINPUT/WAIT` 或显式 `RefreshStrings(true)` 时一次提交完整显示列表，避免 Android 看到“旧菜单 -> 半张地图 -> 地图主体”的循环中间帧。
- `PrintStringBuffer` / `EmueraConsole.PrintHtml`：`BITMAP_CACHE_ENABLE` 改为区域上下文，开启后直到脚本关闭前产生的普通文本行与 `HTML_PRINT` 行都会带 `BitmapCacheEnabled` 标记；这与 TW 动态地图脚本的成对使用方式一致，也让 UI 侧动态地图诊断和复用判断有连续块依据。
- `EmueraContent.QueueDisplayFollowUp`：当显示差异明确传入 `scrollToBottom=false` 时，会取消尚未完成的滚到底任务并记录当前视口位置，再执行布局边界更新；避免动态地图刷新被上一轮普通输出残留的 pending scroll 拉到底部。

更新时间：2026-06-07

用途：这是给 AI 和维护者快速定位代码用的地图。优先读本文件，再按路径进入源码。地图只记录结构、职责、主要接口和关键函数，不复制源码实现。

维护规则：
- 新增、移动、重命名 `Scripts/**/*.cs` 时，同步更新本文件对应目录表。
- 大文件只写职责和关键入口，不枚举所有私有 helper。
- `addons/**`、`*.uid`、字体、图标、导出产物默认只做目录摘要，不进入 C# 明细。
- 重新扫描可用：

```powershell
rg --files -g '*.cs' -g '!addons/**'
rg -n "^\s*(public|internal|protected).*\(" Scripts -g '*.cs'
rg -n "interface|abstract class|class .*:|enum " Scripts -g '*.cs' -g '!addons/**'
```

## 项目概览

`gEmuera` 是 Godot 4.7 + C# 的 Emuera 文本游戏引擎移植版。项目用 Godot 节点替换原 Windows Forms/GDI 渲染，同时保留大量原 Emuera 核心结构。

本次扫描范围：
- 项目 C#：`Scripts/**/*.cs`
- C# 文件数：156
- C# 代码行数约：85256
- 忽略：`addons/**`、`*.uid`、资源导入文件

核心运行链：

```text
project.godot
  run/main_scene = res://first_window.tscn
    -> FirstWindow._Ready()
       扫描 era* 游戏目录，选择游戏
    -> main.tscn
       -> EmueraMain._Ready()
          初始化路径、配置映射、GPU 队列、EmueraContent
       -> EmueraThread.Start()
          后台 Thread 执行 Program.Main()
       -> Program.Main()
          创建 MainWindow/EmueraConsole/Process
       -> Process.Initialize()
          读取 config/csv/erb，建立 LabelDictionary
       -> Process.DoScript() / runScriptProc()
          执行 ERB 指令
       -> EmueraConsole / GenericUtils / EmueraContent
          输出文本、按钮、图片、音频和输入交互
```

## 2026-07-02 动态地图模拟器侧适配

- `config.toml` / `RuntimeDiagnosticsConfig`：新增 `[logging].dynamic_map` 简短开关和 `[debug.dynamic_map]` 专项参数，默认关闭；开启后记录动态地图刷新证据，不影响默认性能。
- `GenericUtils`：新增 `DYNAMIC_MAP.*` 结构化日志入口，按 `BitmapCacheEnabled` 与短时间上下文窗口筛选动态地图刷新，输出尾部行、按钮 generation、BitmapCache 标记和截断文本。
- `uEmuera/Window.Update`：在核心显示列表提交到 Godot UI 前判断本次差异是否为纯追加；删除尾部、更新已有行或重绘当前屏幕时传入 `scrollToBottom=false`，避免动态地图/状态面板刷新被当作普通文本追加而自动滚到底。显示差异不再只依赖 `ConsoleDisplayLine` 对象引用相等，而是按 `LineNo` 与视觉内容结构比较；视觉未变的纯显示行会复用既有节点，视觉未变但包含命令按钮的行会进入 data-only 刷新，只更新按钮输入数据与 Canvas 命中区。
- `uEmuera/Window.Update`：动态地图尾部出现连续多行 `BitmapCacheEnabled` 地图块时，会进入 UI 侧动态地图窗口，只向 Godot 显示层提交最后一段地图块及其后续选项行；这不会修改 Emuera 核心 `displayLineList`，用于避免命令菜单、地图追加和地图主体在 Android 上循环切换。
- `GenericUtils.ApplyTextChanges` / `EmueraContent.ApplyTextChanges`：显示差异新增 `scrollToBottom` 与 `dataOnlyLines` 契约；普通追加仍滚到底，重绘/替换批次只刷新布局和缩放边界并保留当前视口；data-only 行不重建 Control/Canvas 节点，用于先把动态地图变化与选项行刷新拆开。
- `EmueraContent.AddLine` / `EmueraContent.Canvas.AddCanvasLine`：按行替换已有内容时会先注销旧行资源；Canvas 行更新会释放旧 overlay 与纹理 pin 后再注册新行，避免从“整段删除重建”改为“单行更新”后留下旧节点。
- `EmueraContent.SetLastButtonGeneration` / `QuickButtons.UpdateButtonGeneration`：快捷按钮先收集完整按钮组，再按按钮顺序与内容生成签名；内容签名未变时复用现有按钮节点，只更新快捷按钮输入 generation，减少 Android 动态地图刷新时的底部按钮闪烁。

## 2026-06-07 移动端性能补充

- `QuickButtons`：快捷按钮面板复用按颜色缓存的 `StyleBoxFlat`，并缓存内容最小尺寸；按钮增删、换行、字体/尺寸变化时才重新计算，避免大量按钮惯性滚动时每帧触发布局测量。
- `EmueraContent.Canvas`：Canvas 图片资源名 fallback 会缓存成功解析路径，减少 Android 外部存储重复探测和递归查找；`SpriteAnime` overlay 在移动端节流刷新，静态文字、按钮、颜色与 fallback 语义不变。
- `SpriteManager`：纹理缓存清理只在超预算时排序，并限制单轮释放数量；仍严格遵守 `pinCount/refcount`，不会回收正在显示的 CBG、Canvas overlay 或行内图片。
- `EraStreamReader` / `StringStream`：ERB/CSV 热路径减少 `Trim/TrimStart/Substring` 分配；行连接 `{}` / `}` 校验语义保持原核心行为。
- `LabelDictionary` / `ErbLoader`：ERB label 字典在加载前按文件数量预估容量，降低大量 ERB 建表时的 rehash，不改变 `setLabelsArg`、`checkScript` 和 lazy 命中后的完整解析流程。
- `ConstantData` / `Preload`：角色模板列表和姓名映射按已知文件/角色数量预分配；移动端预读并行度限制为 2，避免老手机外部存储 I/O 与内存峰值。

线程模型：
- Godot 主线程：UI、输入、`EmueraContent`、`SpriteManager.UpdateOtherThreads()`（接收异步图片解码结果并主线程上传纹理）、GPU ColorMatrix 队列。
- 后台线程：`EmueraThread.Work()` 执行 `Program.Main()`、ERB 解释、阻塞式输入等待。
- 跨线程桥：`GenericUtils` 的 UI 队列、日志队列、显示队列；输入通过 `EmueraThread.Input()` 唤醒后台线程。

## 目录树

```text
.
|-- project.godot                 Godot 项目配置，主场景 first_window.tscn；`canvas_items + expand` 让 Android 宽屏不产生左右黑边
|-- first_window.tscn             启动器场景
|-- main.tscn                     主游戏场景
|-- config.toml                   精简运行期诊断配置；默认开启轻量日志以支持保存 `gemuera_*.log`，`[logging].enabled=false` 时日志/诊断系统完全关闭
|-- IDEAS.md                      项目工作约定，所有 AI 任务优先阅读
|-- CLAUDE.md                     Claude Code/AI CLI/AI IDE 执行指南
|-- action_maps/                  本地 AI/程序操作日志，不提交 GitHub，最多 30 个日志文件
|-- Text/                         Emuera 配置编码映射文本/bytes
|-- Lang/                         UI 多语言文本
|-- Fonts/                        内置字体
|-- Icons/                        UI 图标
|-- NativeLibs/android/           Android native 库
|-- Scripts/
|   |-- *.cs                      Godot UI、线程桥、渲染、精灵管理
|   |-- Diagnostics/              运行期诊断、日志、导出、面板
|   |-- Emuera/
|   |   |-- Config/               Emuera 配置系统
|   |   |-- Content/              图片、精灵、Graphics surface
|   |   |-- GameData/             变量、表达式、常量、函数方法
|   |   |-- GameProc/             ERB 加载、解析、执行状态机
|   |   |-- GameView/             控制台显示模型和 HTML/按钮/图片行
|   |   |-- Modern/               现代扩展函数
|   |   |-- Runtime/              SQLite、插件运行时工具
|   |   |-- Sub/                  词法、流、异常、存档二进制
|   |   `-- _Library/             Win/GDI/随机数/语言兼容工具
|   |-- Shaders/                  ColorMatrix shader
|   `-- uEmuera/                  System.Drawing / Forms 兼容层
|-- addons/
|   |-- gdUnit4/                  Godot 测试插件
|   `-- godot_mcp/                Godot MCP 编辑器插件
`-- patches/                      历史补丁
```

## C# 文件职责地图

### Scripts 根目录

| 文件 | 主要类型 | 职责 | 关键入口/函数 |
|---|---|---|---|
| `Scripts/EmueraMain.cs` | `EmueraMain : Node`, `GpuWorkItem`, `TextRenderItem` | 主场景入口；初始化配置映射、UI 根节点、线程和 GPU/文本渲染队列。 | `_Ready`, `_Process`, `_ExitTree`, `Run`, `Clear`, `Restart`, `GpuSubmitColorMatrix`, `SubmitTextRender` |
| `Scripts/EmueraThread.cs` | `EmueraThread` | 后台执行 Emuera 核心；把 Godot 输入转成阻塞式 console 输入。 | `Start`, `End`, `Running`, `Input` |
| `Scripts/EmueraContent.cs` | `EmueraContent : Control`, `UiDiagnosticOverlay` | Godot UI/输入/音频核心；创建控制台视口、可切换渲染后端、输入栏、快速按钮、缩放、诊断覆盖层，并保留旧 Control 行渲染作为回退；在移动端读取 Godot display safe area，将主内容、CBG、系统菜单和浮层限制到安全区，并按安全宽度动态更新 Android `WindowX/DrawableWidth`；主控制台缩放时按实际溢出动态启用横向滚动，缩回推荐/安全宽度后清除多余横向偏移；刷新 CBG/SETIMAGELAYER 时遇到占位或本帧上传失败纹理会保留旧角色层节点，避免临时白图替换正常立绘。 | `_Ready`, `GetSafeViewportRect`, `ApplySafeAreaLayout`, `ConfigureContentScrollContainer`, `NormalizeContentHorizontalScroll`, `AddLine`, `AddLines`, `ApplyTextChanges`, `UpdateDisplay`, `RefreshCBG`, `PlaySoundFile`, `PlayBgmFile`, `SetContentScale`, `_Input` |
| `Scripts/EmueraContent.Canvas.cs` | partial `EmueraContent`, `ConsoleRenderSurface`, `ConsoleRenderBackend` | 控制台 Canvas 自绘后端；普通文本/按钮/shape/常规图片按可视区绘制，并复刻原核心按钮选中/焦点背景/BackLog 普通文字颜色语义；ColorMatrix、`SpriteAnime`、非相对定位图片以少量 `EmueraImage` 局部 overlay 混合渲染；相对定位 `ConsoleDivPart` 复用旧 Control 构建为局部 overlay，absolute div 仍整行回退；Canvas 维护行布局 prefix 快照并用二分查找可视行范围，批量输出期间延迟刷新 overlay 行位置；overlay 行定位通过 `canvasRowsWithPositionedNodes` 只刷新实际存在整行 fallback Control、图片 overlay 或 div overlay 的行，避免每次遍历全部历史布局行；overlay 可见性通过“当前可见行/上一轮可见行/逃逸行”目标集合刷新，逃逸 overlay 继续按真实矩形裁剪；动画 overlay 维护 `(LineNo, Index)` 候选 key，避免 `_Process` 扫描历史全部图片 overlay；按钮 hit rect 在 Canvas 行注册时缓存到 `canvasLineButtonHits`，内容、滚动、缩放或视口变化时只标记 dirty，普通 Canvas `_Draw()` 不扫描按钮结构，实际点击进入 `TryHitGlobal` 前才按需重建命中表并用 `hitRectBuckets` 缩小扫描范围，未命中时再回退 overlay/旧控件树；普通移动端默认保留 240 行，Snake/TW 移动端默认 600 行并迁移旧 240 默认，普通桌面默认 360 行，Snake/TW 桌面默认 1500 行并迁移旧 360 默认；`Display.ConsoleRenderBackend=controls` 可切回旧节点后端。 | `CanRenderLineOnCanvas`, `AddCanvasLine`, `NotifyConsoleRenderContentChanged`, `TryGetVisibleCanvasLineLayoutRange`, `RefreshCanvasOverlayRows`, `RefreshCanvasOverlayVisibility`, `RefreshCanvasImageAnimations`, `ConsoleRenderSurface._Draw`, `TryHitGlobal` |
| `Scripts/EmueraImage.cs` | `EmueraImage : Control` | 绘制 `Texture2D` / `AtlasTexture` 的控件，支持 ColorMatrix material。 | `SetColorMatrix`, `_Draw` |
| `Scripts/GenericUtils.cs` | `GenericUtils`, `EmueraLogLevel`, `EmueraLogCategory`, `SnakeAudioInfo` | Emuera 核心到 Godot 的静态桥；日志总开关、诊断热路径闸门、UI 队列、文本输出、音频、输入回放；在 `[debug.performance_sampling]` 开启时低频聚合普通帧与 Canvas 控制台渲染采样。 | `InitializeLogging`, `IsLogEnabled`, `IsScrollTraceActive`, `FlushUI`, `AddText`, `ApplyTextChanges`, `SetBackgroundColor`, `PlaySoundFile`, `SamplePerformanceFrame`, `SampleConsoleRenderFrame`, `ExportDiagnosticPackage`, `RestartGame` |
| `Scripts/FirstWindow.cs` | `FirstWindow : Control` | 启动器；扫描 `era*` 游戏目录，切换语言/核心 profile，进入主场景；启动器外边距跟随 display safe area，避免横屏前摄/挖孔遮挡。 | `_Ready`, `ApplyLauncherSafeArea`, `_ExitTree`, `_Notification`, `ResolveStartupGamePath` |
| `Scripts/SpriteManager.cs` | `SpriteManager`, `TextureInfo`, `SpriteInfo` | 图片/精灵纹理缓存；AtlasTexture 管理；文件图片后台 I/O/解码请求；主线程限流接收解码结果并创建纹理；透明占位纹理带 `IsPlaceholder` 标记并按节流重试，真实纹理完成后可覆盖占位缓存，避免外部存储偶发读失败污染角色图层。 | `Init`, `GetSprite`, `GetTextureInfo`, `TryGetTextureInfoCached`, `RequestTextureInfoAsync`, `GetTextureInfoOtherThread`, `UpdateOtherThreads`, `TextureLoadVersion`, `UpdateCleanup`, `ForceClear` |
| `Scripts/ColorMatrixGPU.cs` | `ColorMatrixGPU` | ColorMatrix shader material 创建、缓存、LRU、uniform 设置。 | `CreateMaterial`, `GetSharedMaterial`, `GetMatrixKey`, `SetMatrixUniforms`, `CreateCompositMaterial` |
| `Scripts/QuickButtons.cs` | `QuickButtons : CanvasLayer` | 快捷按钮浮层；显示当前可选输入，处理点击/触摸。 | `_Ready`, `_Process`, `_Input`, `AddButton`, `Clear`, `ShiftLine`, `SetInputEnabled` |
| `Scripts/Inputpad.cs` | `Inputpad : Control` | 屏幕输入面板；数字/文字输入 UI。 | `_Ready`, `_Process`, `UpdateInputType`, `ShowPad`, `HidePad`, `HasInputFocus` |
| `Scripts/Scalepad.cs` | `Scalepad : Control` | UI 缩放控制。 | `_Ready`, `_Notification`, `SetScale`, `SyncScale`, `ShowPad`, `HidePad` |
| `Scripts/OptionWindow.cs` | `OptionWindow : Control` | 选项弹窗。 | `_Ready`, `ShowPopup` |
| `Scripts/SpriteDebugViewer.cs` | `SpriteDebugViewer : Control` | 精灵调试查看器，配合 `SpriteDebugNotifier` 查看加载图片。 | `_Ready`, `_Process`, `_Input`, `_ExitTree` |
| `Scripts/SpriteDebugNotifier.cs` | `SpriteDebugNotifier` | 精灵加载事件通知。 | `Notify`, `ImageLoadedHandler` |
| `Scripts/FrameRateHelper.cs` | `FrameRateHelper` | 应用帧率配置。 | `Apply`, `ApplyConfigFps` |
| `Scripts/ResolutionHelper.cs` | `ResolutionHelper` | 解析/应用窗口分辨率配置；Android 上不调用 `WindowSetSize`，避免横屏宽机型被缩成固定比例画布产生左右黑边。 | `Apply`, `RefreshResolutions` |
| `Scripts/MultiLanguage.cs` | `MultiLanguage` | 读取 `Lang/*.txt`，提供 UI 文案。 | `Load`, `Get`, `CurrentLanguage` |

### Scripts/Diagnostics

| 文件 | 主要类型 | 职责 | 关键入口/函数 |
|---|---|---|---|
| `DiagnosticLogRecord.cs` | `DiagnosticLogRecord` | 单条诊断日志记录和值格式化；`PERF.*` 性能事件镜像到 Godot/logcat 时附带结构化 `data`，便于 Android 实机直接读取采样数值。 | `FormatForGodot`, `FormatForExport` |
| `DiagnosticLogRouter.cs` | `DiagnosticLogRouter` | 日志总闸门、类别过滤、限流、脱敏、record 构造；关闭时热路径直接返回；限流与单调时间戳使用 CLR `Stopwatch`，允许后台线程在 Godot 退出阶段继续安全写诊断。 | `Initialize`, `Reload`, `IsLoggingEnabled`, `IsEnabled`, `CheckRateLimit`, `BuildRecord`, `GetMonotonicMilliseconds`, `RedactPath` |
| `DiagnosticLogSinks.cs` | `DiagnosticLogSinks` | 环形日志缓存和 Godot 输出镜像。 | `Initialize`, `Write`, `Snapshot`, `SetMirrorNonErrorToGodot` |
| `DiagnosticLogExporter.cs` | `DiagnosticLogExporter` | 导出诊断包/日志，记录面包屑，清理保留文件。 | `ExportDiagnosticPackage`, `ExportDiagnosticLog`, `WriteBreadcrumb`, `RunRetentionCleanup` |
| `InputReplayBuffer.cs` | `InputReplayBuffer` | 输入回放环形缓冲；相对时间戳复用诊断路由的 CLR 单调时钟，避免后台线程依赖 Godot `Time`。 | `Capture`, `BuildExportText` |
| `SaveLogOperationTrail.cs` | `SaveLogOperationTrail` | 存档/日志操作轨迹缓存；时间戳复用诊断路由的 CLR 单调时钟。 | `Capture`, `BuildExportText` |
| `RuntimeDiagnosticsConfig.cs` | `RuntimeDiagnosticsConfig` 等配置类 | 运行期诊断配置模型、默认值、精简 logging 开关展开和关闭态清理。 | `CreateDefault`, `ApplyMinimalLoggingConfig`, `DisableAllDiagnostics`, `GetRuntimeLogLevel`, `GetActiveDebugModel` |
| `RuntimeDiagnosticsConfigLoader.cs` | `RuntimeDiagnosticsConfigLoader`, `LoadResult` | 读取 `config.toml` 和用户覆盖配置，支持精简 `[logging]` 总开关和模块开关。 | `Load` |
| `RuntimeDiagnosticsConfigWriter.cs` | `RuntimeDiagnosticsConfigWriter` | 写出精简用户诊断配置 TOML。 | `SaveUserConfig`, `BuildToml` |
| `RuntimeTomlParser.cs` | `RuntimeTomlParser` | 简易 TOML 解析器。 | `Parse` |
| `RuntimeDiagnosticsPanel.cs` | `RuntimeDiagnosticsPanel`, `FloatingDiagnosticsHost` | Godot 内置诊断浮窗。 | `AttachFloatingTo`, `_Ready`, `_Notification` |

### Scripts/uEmuera 兼容层

| 文件 | 主要类型 | 职责 | 关键入口/函数 |
|---|---|---|---|
| `uEmuera/Application.cs` | `Application` | Windows Forms `Application` 兼容桩。 | `EnableVisualStyles`, `SetCompatibleTextRenderingDefault`, `Run` |
| `uEmuera/Drawing.cs` | `Bitmap`, `BitmapTexture`, `Graphics`, `Color`, `Font`, `Rectangle`, `Point`, `Size` | `System.Drawing` 替代层；包装 Godot Image/Texture、颜色、字体、几何类型；`BitmapTexture` 遇到占位缓存时允许 `SpriteManager` 按节流异步重试真实纹理。 | `Bitmap.Save`, `BitmapTexture`, `Color.FromArgb`, `Color.ToGodotColor`, `Rectangle.Intersect` |
| `uEmuera/Forms.cs` | `Timer`, `MessageBox`, `ScrollBar`, `ToolTip`, `PictureBox`, `TextBox` | `System.Windows.Forms` 替代层；Timer 由 Godot loop 手动 Update。 | `Timer.Update`, `MessageBox.Show`, `ToolTip.SetToolTip` |
| `uEmuera/Window.cs` | `MainWindow`, `DebugDialog` | 原主窗体兼容桩，桥接 `EmueraConsole` 和 `Process`。 | `MainWindow.Init`, `Update`, `Refresh`, `WaitForRefreshProcessed`, `Reboot` |
| `uEmuera/Utils.cs` | `Logger`, `Utils` | 文件系统、编码、资源扫描、显示宽度、路径规范化工具；Godot 文件 API 下的通配符枚举在单次目录扫描内复用 Regex 匹配，保持原通配符语义并避免按文件重复构造匹配器；`ResourcePrepare` 读取资源 CSV 时只扫描头部字段，避免为每行创建完整 `string[]`。 | `SHIFTJIS_to_UTF8`, `NormalizePath`, `FileExists`, `GetFilePaths`, `GetDisplayLength`, `ResourcePrepare` |
| `uEmuera/Properties.cs` | `ResourceManager`, `Resources` | 原资源访问兼容。 | `GetString` |
| `uEmuera/VisualBasic.cs` | `Strings`, `VbStrConv` | VB 字符串转换兼容。 | `StrConv` |
| `uEmuera/Media.cs` | `Hand`, `Asterisk` | 系统声音兼容桩。 | `Play` |
| `uEmuera/partial/EmueraConsole.cs` | partial `EmueraConsole` | 给 uEmuera 层访问显示行和输入等待状态的扩展，等待态包含 `WaitInputNoFocus`。 | `GetDisplayLinesForuEmuera`, `GetDisplayLinesCount`, `GetDisplayLinesSnapshotForuEmuera`, `IsWaitingInput` |
| `uEmuera/partial/AConsoleColoredPart.cs` | partial `AConsoleColoredPart` | 显示部件兼容扩展。 | 主要是 partial 补充 |

### Scripts/Emuera 根

| 文件 | 主要类型 | 职责 | 关键入口/函数 |
|---|---|---|---|
| `Emuera/Program.cs` | `Program`, `EmueraCoreProfile` | 原 Emuera 入口；设置目录、核心 profile、配置、窗口、Process。 | `Main`, `AppendSnakeStartupErrorLog`, `DetectCoreProfile`, `ConfigureModernMobileCoreAdapters` |
| `Emuera/GlobalStatic.cs` | `GlobalStatic` | 核心全局对象注册和重置；保存插件存在标志。 | `Reset`, `ExistPlugin` 及静态字段 |

### Scripts/Emuera/Config

| 文件 | 主要类型 | 职责 | 关键入口/函数 |
|---|---|---|---|
| `Config.cs` | `Config` | 运行配置静态访问；字体、路径、窗口尺寸、更新检查、`UPDATECHECK` 禁用、插件警告、`BEFORE_ERROR/THROW` 禁用、`UseScopedVariableInstruction`、debug config。 | `SetConfig`, `GetFont`, `ClearFont`, `CreateSavDir`, `CheckUpdate`, `UpdateWindowWidth`, `SetDebugConfig` |
| `ConfigData.cs` | `ConfigData` | 配置数据实体，保存所有 Emuera 选项，包含插件警告、异常前事件禁用项、`VARI/VARS` 开关、v24 `TextDrawingMode.SKIASHARP` 默认兼容和默认开启 lazy loading。 | 构造/读取/保存配置项 |
| `ConfigCode.cs` | `ConfigCode` 等 enum | 配置项枚举和相关枚举，包含 `PluginAvailableWarn`、`DisableBeforeErrorThrow`、`UseScopedVariableInstruction`、`TextDrawingMode.SKIASHARP` 和 `RenderingBackend` 兼容枚举。 | 枚举定义 |
| `ConfigItem.cs` | `AConfigItem`, `ConfigItem<T>` | 单个配置项的解析/序列化容器。 | `ToString`, value parse 相关 |
| `JSONConfig.cs` | `JSONConfig` | v24/snake `setting.json` 兼容配置层；启动时创建/读取 JSON，映射 `UseScopedVariableInstruction` 到旧配置并公开 JSON-only 开关。 | `Load`, `Save` |
| `JSONConfigData.cs` | `JSONConfigData` | `setting.json` 数据模型，包含 `UseButtonFocusBackgroundColor`、`UseNewRandom`、`UseScopedVariableInstruction`、`RenderingBackend`。 | JSON 属性 |
| `KeyMacro.cs` | `KeyMacro` | 快捷键宏配置。 | `Load`, `Save`, key macro 访问 |

### Scripts/Emuera/Content

| 文件 | 主要类型 | 职责 | 关键入口/函数 |
|---|---|---|---|
| `AContentFile.cs` | `AContentFile : IDisposable` | 内容文件抽象基类。 | `Dispose` |
| `AContentItem.cs` | 空 namespace 占位 | 预留文件，当前不定义类型。 | 无 |
| `AppContents.cs` | `AppContents`, `LazySpriteDefinition` | 资源/精灵表注册；读取资源 CSV；lazy sprite 索引在启动阶段只保存原始 CSV 行和头字段，命中具体 sprite 时再完整裸逗号拆分；资源 CSV 路径存在性/大小写解析缓存；lazy sprite 首次实体化慢调用诊断。 | `CreateSpriteAnime`, `BuildLazyResourceIndex`, `RealizeLazySprite` |
| `ConstImage.cs` | `AbstractImage`, `ConstImage` | 不可变图片资源，基于 Bitmap/Image。 | `CreateFrom`, `Dispose` |
| `CroppedImage.cs` | `ASprite`, `ASpriteSingle`, `SpriteG`, `SpriteF`, `SpriteAnime` | 精灵裁剪、动画帧、绘制接口。 | `SpriteGetColor`, `GraphicsDraw`, `AddFrame`, `PauseAnimation`, `ResumeAnimation`, `GetCurrentFrameInfo` |
| `GraphicsImage.cs` | `GraphicsImage : AbstractImage` | ERB 图形 surface；绘制 sprite、线、文字、多边形、旋转、ColorMatrix；维护 `DisplayRevision` 和稳定快照，后台线程重绘角色差分时 UI 只提交完整稳定帧，资源层暂不可用时保留旧显示，避免动态图像中间态闪白。 | `GCreate`, `GDrawCImg`, `TryCreateDisplaySnapshot`, `ApplyColorMatrixGPU`, `GDrawG`, `GDrawString`, `GRotate`, `GDispose` |

### Scripts/Emuera/GameData

| 文件 | 主要类型 | 职责 | 关键入口/函数 |
|---|---|---|---|
| `ConstantData.cs` | `ConstantData`, `CharacterTemplate`, `LazyErdNameData` | CSV 常量、角色模板、ERD 名称数据；维护整数/字符串/小数 1D 变量默认长度，`LOCALF/ARGF` 使用小数长度表；`VariableSize.CSV`、角色 CSV、名称 CSV、别名 CSV 和 `VarExt*.csv` 热路径使用轻量裸逗号字段读取，避免整行 `Split(',')` 分配；读取 `VarExt*.csv` 中 MAP/XML/DT 的 `SAVE/GLOBAL/STATIC` 保存域声明。 | 常量读取、角色模板访问 |
| `DefineMacro.cs` | `DefineMacro` | `#DEFINE` 宏数据。 | 构造和字段 |
| `EraType.cs` | `EraType`, `EraTypeHelper` | Era 值类型枚举和 CLR `Type` 过渡转换 helper；迁移期用于把旧 `long/string/double` 签名统一映射到整数/字符串/小数语义。 | `FromClrType`, `ToClrType`, 枚举定义 |
| `GameBase.cs` | `GameBase` | 游戏基础信息、版本、标题、更新检查 URL/版本名等；`GAMEBASE.CSV` 保留原核心裸逗号语义，但只扫描前两个字段以减少启动期分配。 | `LoadGameBaseCsv`, 基础字段访问 |
| `IdentifierDictionary.cs` | `IdentifierDictionary` | 变量/函数/宏名解析字典，保留 `REF/REFF` 等关键字并管理局部变量默认尺寸/禁用状态。 | `GetIdentifier`, `Add`, defined-name 管理 |
| `ParserMediator.cs` | `ParserMediator` | 表达式、变量、函数解析的中介和 warning 管理。 | `Initialize`, `GetWarningList`, parse helper |
| `StrForm.cs` | `StrForm`, `FormattedStringMethod` 系列 | 格式化字符串表达式。 | `GetString`, format 方法 |

### Scripts/Emuera/GameData/Expression

| 文件 | 主要类型 | 职责 | 关键入口/函数 |
|---|---|---|---|
| `IOperandTerm.cs` | `IOperandTerm` | 表达式操作数抽象基类；内部以 `EraType` 保存整数/字符串/小数类型，`GetOperandType()` 仅作为旧 CLR `Type` 桥接入口。 | `GetIntValue`, `GetStrValue`, `GetFloatValue`, `GetValue`, `GetEraType`, `GetOperandType`, `Restructure` |
| `Term.cs` | `NullTerm`, `SingleTerm`, `StrFormTerm`, `VariadicArgTerm` | 常量/字符串格式/可变参数表达式项；常量和可变参数类型判断走 `EraType`。 | `GetValue`, `GetIntValue`, `GetStrValue`, `Restructure` |
| `ExpressionParser.cs` | `ExpressionParser` | ERB 表达式解析器。 | `ReduceExpression`, `ReduceArguments`, `ReadExpression` 类方法 |
| `ExpressionMediator.cs` | `ExpressionMediator` | 表达式求值上下文，连接变量和函数；暴露 `CurrentContext` 供局部变量 token 读取当前调用栈的运行期数组。 | 变量/函数访问、运行时上下文 |
| `OperatorCode.cs` | `OperatorCode`, `OperatorManager` | 运算符枚举和查找。 | `GetOperator`, operator metadata |
| `OperatorMethod.cs` | `OperatorMethod`, 多个具体运算符 | 运算符求值实现。 | `OperatorMethodManager.Initialize`, `GetIntValue`, `GetStrValue`, `GetReturnValue` |
| `SafeArithmetic.cs` | `SafeArithmetic` | 整数/浮点安全数学工具。 | `Add`, `Sub`, `Mul`, `Div`, `Pow` 等 |
| `CaseExpression.cs` | `CaseExpression` | `CASE` 条件表达式。 | `IsMatch`, 条件求值 |

### Scripts/Emuera/GameData/Function

| 文件 | 主要类型 | 职责 | 关键入口/函数 |
|---|---|---|---|
| `FunctionMethod.cs` | `FunctionMethod` | 内置函数抽象基类；返回类型和默认参数签名都使用 `EraType`，默认参数校验按脚本语义类型比较表达式。 | `CheckArgumentType`, `GetIntValue`, `GetStrValue`, `GetFloatValue`, `GetReturnValue`, `UniqueRestructure` |
| `FunctionMethodTerm.cs` | `FunctionMethodTerm` | 把 `FunctionMethod` 包装成表达式项。 | `GetIntValue`, `GetStrValue`, `Restructure` |
| `Creator.cs` | partial `FunctionMethodCreator` | 创建内置函数表。 | `GetMethodList` |
| `Creator.Method.cs` | partial `FunctionMethodCreator` | 大量基础内置函数：角色、CSV、字符串、数学、图像、音频、平台识别、变量访问等；`GETNUM/GETNUMB/GETPALAMLV/GETEXPLV` 对齐 CSV 编号与等级查找语义；`EVAL/EVALF/EVALS` 分别执行整数/小数/字符串动态表达式求值；`BITSET/BITGET/BITTOGGLE/BITINDEXOFFIRST` 使用整数 1D 数组作为位图并兼容 `SparseArray<long>`；`ARRAYMSORT/ARRAYMSORTEX` 支持 int/string/float 排序、1D 稀疏数组和 1D/2D/3D 目标数组首维重排；`SUMARRAY/SUMCARRAY`、`MAXARRAY/MINARRAY`、`MATCH/CMATCH`、`GROUPMATCH/NOSAMES/ALLSAMES`、`INRANGEARRAY/INRANGECARRAY` 兼容整数/小数数组统计与检索。 | 嵌套 `*Method : FunctionMethod`；统一 override `GetIntValue/GetStrValue/GetReturnValue` |
| `Creator.Method.DT.cs` | partial `FunctionMethodCreator` | DataTable 扩展函数。 | `DtCreate`, `DtRowAdd`, `DtCellGet`, `DtSelect`, XML 互转 |
| `Creator.Method.Map.cs` | partial `FunctionMethodCreator` | Map 扩展函数。 | `MapCreate`, `MapSet`, `MapGet`, `MapKeys`, `MapToXml` |
| `Creator.Method.Sql.cs` | partial `FunctionMethodCreator` | SQL 扩展函数。 | `SqlConnect`, `SqlExecuteReader`, `SqlReaderGet*`, import/export |
| `Creator.Method.Xml.cs` | partial `FunctionMethodCreator` | XML 扩展函数。 | `XmlDocument`, `XmlGet`, `XmlSet`, `XmlAddNode`, `XmlRemoveNode` |
| `RuntimeDataStore.cs` | `RuntimeDataStore` | 运行期 DataTable/Map/XML 静态存储；按 `VarExt*.csv` 的 `SAVE/GLOBAL/STATIC` 声明域清理、筛选和保存 Map/XML/DataTable，避免读档误删非保存域运行期缓存。 | `Clear`, `ClearSaveData`, `ClearGlobalData`, `ClearStaticData`, `DataTables`, `Maps`, `XmlDocuments` |
| `UserDefinedMethodTerm.cs` | `UserDefinedMethodTerm`, `UserDefinedRefMethodTerm` | 用户定义函数调用表达式项；以 `EraType` 接收函数返回类型，并在 `IOperandTerm` 边界桥接回 CLR operand type。 | `Create`, `Restructure`, `GetRefName`, `GetValue` |
| `UserDefinedRefMethod.cs` | `UserDefinedRefMethod` | `#REF/#REFS/#REFF` 引用函数匹配和绑定；`RetType` 使用 `EraType` 与 `FunctionLabelLine.MethodType` 对齐。 | `Create`, `MatchType`, `SetReference` |

### Scripts/Emuera/GameData/Variable

| 文件 | 主要类型 | 职责 | 关键入口/函数 |
|---|---|---|---|
| `VariableCode.cs` | `VariableCode` | Emuera 变量枚举；包含 `COUNT` 禁用标志、`LOCALF/ARGF` 小数局部编号、`GAMEBASE_URL/GAMEBASE_VERSIONNAME` 和 `REFF*` 小数引用变量。 | 枚举定义 |
| `VariableIdentifier.cs` | `VariableIdentifier` | 变量名到 `VariableCode`/scope 的解析；类型、维度和属性判断优先来自 `VariableDescriptor` 元数据。 | `GetVarNameDic`, `GetVariableId`, `GetExtSaveList`, `Descriptor` |
| `VariableDescriptor.cs` | `VariableDescriptor`, `VariableDescriptorTable`, `VariableKind`, `VariableDimension`, `VariableAttribute` | Snake 兼容的变量描述符元数据层；集中解释 `VariableCode` 位标志为类型、维度和属性，目前不改变存储布局或存档格式。 | `FromCode`, `GetDescriptorByCode`, `TryGetDescriptor` |
| `SparseArray.cs` | `SparseArray<T>` | 1D 变量稀疏存储容器；未写入元素按默认值读取，用于降低 Android 上巨型数组的初始内存占用，2D/3D 仍保持 CLR 多维数组契约。 | `Length`, indexer, `Entries`, `FromArray`, `ToArray`, `Shift`, `RemoveRange`, `Sort` |
| `VariableData.cs` | `VariableData : IDisposable` | 全局变量数据容器和变量 token 构造；暴露 GAMEBASE URL/版本名常量，按小数长度表初始化 `LOCALF/ARGF`，全局 1D 整数/字符串数组使用 `SparseArray<T>` 存储。 | 初始化变量、读取/保存、`Dispose` |
| `VariableEvaluator.cs` | `VariableEvaluator : IDisposable` | 变量求值、读写、角色变量访问、局部变量栈；`RESULT_ARRAY`、`RESULTS_ARRAY`、`RESULTF`、`SELECTCOM_ARRAY`、`ITEMSALES`、`RANDDATA` 等结果/工作变量兼容小数和 1D 稀疏存储，`VARSET/CVARSET` 批量赋值按 `EraType` 分派整数/字符串/小数路径，数组求和、匹配计数、最大/最小、区间统计 helper 覆盖小数数组；读档/全局读档按 VarExt 声明域处理 RuntimeDataStore，保留非保存域运行期缓存。 | `GetValue`, `SetValue`, `GetNextRand`, `SetValueAll`, `LoadFrom`, `LoadGlobal`, local/reference 管理 |
| `ElementRefInfo.cs` | `ElementRefInfo` | Snake 兼容的元素级 REF 信息；捕获变量 token、索引和非角色数组实体，供标量 REF 参数读写数组单个元素。 | `GetIntValue`, `GetStrValue`, `GetFloatValue`, `SetValue`, `PlusValue` |
| `NullRefTerm.cs` | `NullRefTerm` | `OUT REF` 参数省略时的空引用占位；读零/空串、写入无操作。 | `GetIntValue`, `GetStrValue`, `GetFloatValue`, `SetValue`, `GetArray` |
| `VariableToken.cs` | `VariableToken` 及大量派生 token | 变量实际存取实现；静态/私有/局部/引用/角色/常量/伪变量，基础类型/维度/保存属性由 `VariableDescriptor` 驱动并公开 `EraType`；`LOCAL/LOCALS/LOCALF/ARG/ARGS/ARGF` 运行期数组按当前 `ExecutionContext` 读取，带 `@FUNCNAME` 的局部变量会在调用栈中查找匹配函数上下文；`REFF/REFF2D/REFF3D` 使用小数引用类型；`ReferenceToken` 保存数组引用、标量引用、元素引用和空引用状态，1D REF 路径同时支持 CLR 数组和 `SparseArray<T>`。 | `GetIntValue`, `GetStrValue`, `GetFloatValue`, `GetEraType`, `SetValue`, `SetValueAll`, `PlusValue`, `In`, `Out`, `SetRef`, `SetNullRef`, `MatchType` |
| `VariableTerm.cs` | `VariableTerm`, `FixedVariableTerm`, `VariableNoArgTerm` | 表达式中的变量访问项；暴露变量 `EraType` 和参数个数供函数参数转换、可变参数和元素级 REF 捕获索引。 | `GetIntValue`, `SetValue`, `GetEraType`, `Restructure`, `ArgumentCount` |
| `VariableStrArgTerm.cs` | `VariableStrArgTerm` | 字符串索引变量表达式项。 | `GetStrValue`, `Restructure` |
| `VariableLocal.cs` | `VariableLocal` | 局部变量 token 注册表；仍负责按函数标签尺寸创建 `LOCAL/LOCALS/LOCALF/ARG/ARGS/ARGF` token，但实际运行期数组已改由 `ExecutionContext` 持有。 | local token 创建、尺寸调整、默认值重置 |
| `VariableParser.cs` | `VariableParser` | 变量表达式解析。 | `Parse`, variable term 构造 |
| `CharacterData.cs` | `CharacterData : IDisposable` | 角色数据数组和角色变量管理；角色 1D 整数/字符串变量及可适配的用户定义角色 1D 变量使用 `SparseArray<T>`，排序键读取兼容稀疏数组。 | 角色增删、保存/读取、`Dispose` |

### Scripts/Emuera/GameProc

| 文件 | 主要类型 | 职责 | 关键入口/函数 |
|---|---|---|---|
| `Process.cs` | partial `Process` | 脚本处理器主类；初始化、输入结果、开始执行、异常处理；脚本错误终止时清理函数栈与 `ExecutionContext`，表达式函数异常路径防止局部上下文残留，并在 `BEFORE_THROW` 内部异常时跳过二次 `BEFORE_ERROR`。 | `Initialize`, `InitializeAsync`, `DoScript`, `BeginTitle`, `InputInteger`, `InputString`, `ReloadErb`, `ReloadErbAsync`, `ReloadPartialErb`, `ReloadPartialErbAsync`, `GetRunningPosition` |
| `Process.ScriptProc.cs` | partial `Process` | 内层脚本执行循环和 debug 执行；`THROW` 会记录 pending throw、进入 `BEFORE_THROW`，并在 `BEFORE_THROW/BEFORE_ERROR` 内部只打印消息避免递归错误事件。 | `runScriptProc`, `DoDebugNormalFunction`, `saveCurrentState`, `loadPrevState` |
| `ExecutionContext.cs` | `ExecutionContext` | 函数执行上下文；持有当前调用帧的 `LOCAL/LOCALS/LOCALF/ARG/ARGS/ARGF` 运行期数组和父子关系，用于替代旧的共享局部数组存储。 | 构造函数、`Dispose`, `Parent`, `Local*`, `Arg*` |
| `Process.State.cs` | `ProcessState`, `SystemStateCode`, `BeginType` | CALL/JUMP/RETURN、BEGIN、函数栈、返回值、状态克隆；维护 `ExecutionContext` 栈，进入函数时绑定数组 REF、元素级 REF、OUT 空引用并创建局部执行上下文；调试/表达式求值可通过 `CaptureCallState`/`RollbackToState` 恢复函数栈、上下文栈和 `CurrentLine`，克隆状态保留原上下文栈供监视表达式读取 `LOCAL@FUNCNAME`；`ClearFunctionListPreserveTrace` 用于错误/THROW 后保留调试调用栈显示。 | `JumpTo`, `SetBegin`, `Begin`, `Return`, `IntoFunction`, `ReturnF`, `CurrentContext`, `FindContextByLabel`, `CaptureCallState`, `RollbackToState`, `ClearFunctionListPreserveTrace`, `Clone` |
| `Process.SystemProc.cs` | partial `Process` | 系统流程处理。 | 系统状态执行 helper |
| `Process.CalledFunction.cs` | `CalledFunction`, `UserDefinedFunctionArgument` | 调用栈条目和用户函数实参；转换并暂存普通参数、数组 REF、元素级 REF 和 OUT 空引用；用户函数参数按 `EraType` 处理整数到小数的兼容扩展和可变参数类型，`VariadicArgTerm` 不进入普通 transporter，统一由 `ProcessState.IntoFunction` 展开。 | `ConvertArg`, `SetTransporter`, 参数暂存数组 |
| `Process.LazyLoading.cs` | partial `Process`, `LazyStatus` | ERB lazy loading 表、索引、按需加载、缓存；Android 索引缺失/失效时优先用轻量标签扫描建表，预扫描只抽取 `@label` 与 `#FUNCTION/#FUNCTIONS/#FUNCTIONF`，非 Android 保持 BuildTable/full-load 建表路径；首次真实命中仍执行完整 ERB 解析与检查；索引构建和局部更新会排除事件函数与方法文件，避免预解析依赖的方法被延迟加载；运行期维护 file -> functions 反向索引，按需加载后只移除相关映射，避免扫描整张 lazy 表；运行期 lazy ERB 补加载带慢调用诊断；EVENTLOAD 保持命中时按需加载，`PreloadEventLoadLazyErbs` 仅保留为诊断/实验入口，不在系统读档流程调用。 | `TryLazyLoadErb`, `LoadLazyLoadingTable`, `SaveLazyLoadingList`, `SavePartialLazyLoadingList`, `PreloadEventLoadLazyErbs` |
| `ErbLoader.cs` | `ErbLoader`, `PPState` | 读取/预处理 ERB/ERH 文件，生成 logical lines/labels；提供 `LoadErbFilesAsync` / `LoadErbsAsync` 作为主入口，旧同步方法仅做兼容包装。 | `LoadErbFiles`, `LoadErbFilesAsync`, `loadErbs`, `LoadErbsAsync`, `warningDic` |
| `HeaderFileLoader.cs` | `HeaderFileLoader` | 读取头文件/定义。 | header 加载入口 |
| `SelectCaseJumpTable.cs` | `SelectCaseJumpTable` | Snake 兼容的 `SELECTCASE` 常量分支跳转表；对整数/字符串/小数常量 `CASE` 建表，范围、比较和运行期表达式回退顺序扫描。 | `TryBuild`, `Lookup` |
| `LogicalLine.cs` | `LogicalLine`, `InstructionLine`, `FunctionLabelLine`, `GotoLabelLine` | ERB 逻辑行模型；函数标签记录 `LOCAL/LOCALS/LOCALF/ARG/ARGS/ARGF` 尺寸，`FunctionLabelLine.MethodType` 以 `EraType` 保存 `#FUNCTION/#FUNCTIONS/#FUNCTIONF` 返回类型。 | `FunctionLabelLine`, `InstructionLine`, label/goto 访问 |
| `LogicalLineParser.cs` | `LogicalLineParser` | 将文本行解析为 `LogicalLine`；支持 `#FUNCTIONF`、`#LOCALFSIZE`、`#REFF` 和 Snake 小数私有变量兼容解析。 | `ParseSharpLine`, `ParseLine`, `ParseLabelLine` |
| `LabelDictionary.cs` | `LabelDictionary` | 函数 label、事件 label、`$` label 索引；事件 label 合并 `LOCAL/LOCALS/LOCALF` 最大尺寸并同步 ARGF 尺寸。 | `AddLabel`, `SortLabels`, `GetEventLabels`, `GetNonEventLabel`, `GetLabelDollar` |
| `InputRequest.cs` | `InputRequest`, `InputType` | 输入请求类型和值约束，`NoFocus` 标记用于 NF 定时输入。 | 构造和字段 |
| `UserDefinedFunction.cs` | `UserDefinedFunctionData` | 用户定义函数元数据。 | 构造和参数类型 |
| `UserDefinedVariable.cs` | `UserDefinedVariableData`, `DimLineWC` | 用户定义变量元数据。 | 构造、维度/类型信息 |

### Scripts/Emuera/GameProc/Function

| 文件 | 主要类型 | 职责 | 关键入口/函数 |
|---|---|---|---|
| `Instruction.cs` | `AbstractInstruction` | ERB 指令抽象基类。 | `SetJumpTo`, `DoInstruction`, `CreateArgument` |
| `Instraction.Child.cs` | partial `FunctionIdentifier`, 多个 `*Instruction` | 具体 ERB 指令实现；文件名保留原拼写 `Instraction`；包含 Snake/v24 兼容指令、`TINPUTNF/TINPUTSNF/TONEINPUTNF/TONEINPUTSNF` 和渲染控制 API；`TRYCALLF/TRYCALLFORMF` 预解析失败静默返回，普通 `CALLF/CALLFORMF` 继续报告解析警告。 | `PRINT_Instruction`, `TINPUT_Instruction`, `CALL_Instruction`, `CALLF_Instruction`, `GOTO_Instruction`, `RETURNF_Instruction`, `SNAKE_UI_SETTING_Instruction` 等 |
| `FunctionIdentifier.cs` | `FunctionIdentifier` | 指令名/FunctionCode 映射和指令分类，注册 NF 定时输入变体；`VARI/VARS` 由 `Config.UseScopedVariableInstruction` 控制。 | `GetInstructionNameDic`, `IsPrint`, `IsInput`, `IsJump`, `IsMethod`, `IsFlowContorol` |
| `BuiltInFunctionCode.cs` | `FunctionCode` | 内置指令/函数 code 枚举，包含 NF 定时输入 code。 | 枚举定义 |
| `FunctionArgType.cs` | `FunctionArgType` | 指令参数类型枚举。 | 枚举定义 |
| `Argument.cs` | `Argument` 及大量 `Sp*Argument` | 已解析指令参数的数据对象；通用 `ExpressionsArgument.ArgumentTypeArray` 使用 `EraType[]` 保存指令参数类型契约。 | 构造和字段 |
| `ArgumentBuilder.cs` | `ArgumentBuilder`, 多个 `*ArgumentBuilder` | 针对不同指令构造 `Argument`；通用参数表与 `checkArgumentType` 使用 `EraType` 校验，`EraType.Void` 表示任意/省略兼容位。 | `Build`, `CheckArgument`, 各指令 builder |
| `ArgumentParser.cs` | partial `ArgumentParser` | 根据 `FunctionIdentifier` 解析参数。 | `ParseArgument` |

### Scripts/Emuera/GameView

| 文件 | 主要类型 | 职责 | 关键入口/函数 |
|---|---|---|---|
| `EmueraConsole.cs` | partial `EmueraConsole`, `DisplayLineList`, `ClientBackGroundImage` | 控制台状态、输入等待、NF 等待态、CBG/图片层、鼠标/键盘、调试、重载；保存 v24 渲染控制 API 和 `HOTKEY_STATE` 的脚本可见状态。 | `Initialize`, `WaitInput`, `IsWaitInputState`, `PressEnterKey`, `RefreshStrings`, `CBG_SetImage`, `SetImageLayer`, `SetSnakeTextDrawingMode`, `HotkeyStateInitialize`, `TryEvaluateHotkey`, `ReloadErb`, `Dispose` |
| `EmueraConsole.Print.cs` | partial `EmueraConsole` | 打印文本、HTML、按钮、图片、形状、日志输出；维护 `IsLineEnd`、`LINECOUNT`、`CLEARLINE` 的逻辑行语义和 `PrintC/PrintButtonC` 像素制表。 | `Print`, `PrintC`, `PrintButtonC`, `PrintHtml`, `PrintImg`, `PrintShape`, `PrintButton`, `PrintFlush`, `deleteLine`, `OutputLog`, `PopDisplayingLines` |
| `ConsoleDisplayLine.cs` | `ConsoleDisplayLine` | 一行显示内容，包含多个按钮/片段。 | `DrawTo`, `GDIDrawTo`, `ShiftPositionX`, `ChangeStr` |
| `ConsoleButtonString.cs` | `ConsoleButtonString` | 一个可点击/可输入的显示段，包含多个 display part。 | `DivideAt`, `CalcWidth`, `CalcPointX`, `DrawTo` |
| `AConsoleDisplayPart.cs` | `AConsoleDisplayPart`, `AConsoleColoredPart` | 显示片段抽象基类。 | `DrawTo`, `GDIDrawTo`, `ToString` |
| `ConsoleStyledString.cs` | `ConsoleStyledString`, `DisplayMode` | 有样式文本片段；按钮选中态可按 `setting.json` 的 `UseButtonFocusBackgroundColor` 绘制背景。 | `DrawTo`, 样式字段 |
| `ConsoleImagePart.cs` | `ConsoleImagePart` | 行内图片片段。 | `DrawTo`, 图片尺寸/偏移 |
| `ConsoleShapePart.cs` | `ConsoleShapePart`, `ConsoleRectangleShapePart`, `ConsoleSpacePart`, `ConsoleErrorShapePart` | 行内形状/空白/错误占位片段。 | `DrawTo`, shape 参数 |
| `ConsoleDivPart.cs` | `ConsoleDivPart`, `StyledBoxModel` | HTML div/盒模型片段。 | box 计算与绘制 |
| `ButtonStringCreator.cs` | `ButtonStringCreator`, `ButtonPrimitive` | 将文本拆成按钮/显示片段。 | `CreateButtonString` 相关 |
| `HtmlManager.cs` | `HtmlManager` 及 HTML state 类型 | HTML 文本和 display line 互转，支持 style/button/img/shape/div。 | `Html2DisplayLine`, `Html2ButtonList`, `DisplayLine2Html`, `HtmlTagSplit`, `Escape`, `Unescape` |
| `HotkeyState.cs` | `HotkeyState` | v24/snake `HOTKEY.ERB` 简易解释器和状态数组；支持 `HOTKEY_STATE_INIT`、`HOTKEY_STATE`、Ctrl+D 开关和硬件键盘热键转数值输入。 | `Initialize`, `Set`, `Toggle`, `TryEvaluate` |
| `PrintStringBuffer.cs` | `PrintStringBuffer` | 打印缓冲；把连续输出合并成 display line，并提供当前缓冲行像素宽度。 | `Append`, `Flush`, `CurrentLineWidth`, line 构造 |
| `StringMeasure.cs` | `StringMeasure : IDisposable` | 文本宽度测量。 | `GetDisplayLength`, `Dispose` |
| `StringStyle.cs` | `StringStyle` | 文本颜色、字体样式、font name。 | 构造、比较、转换 |
| `MixedNum.cs` | `MixedNum` | HTML 尺寸/位置混合数值。 | 数值字段/解析辅助 |

### Scripts/Emuera/Runtime

| 文件 | 主要类型 | 职责 | 关键入口/函数 |
|---|---|---|---|
| `Runtime/Utils/SqliteRuntime.cs` | `SqliteRuntime` | SQLite 初始化、连接路径、运行时可用性。 | `Initialize`, `OpenConnection`, `Shutdown` |
| `Runtime/Utils/SnakeSqlManager.cs` | `SnakeSqlManager`, `ReaderContext` | Snake profile SQL 兼容层。 | `Connect`, `ExecuteNonQuery`, `ExecuteReader`, `ReaderGet*`, `Disconnect` |
| `Runtime/Utils/PluginSystem/IPluginMethod.cs` | `IPluginMethod` | 插件方法接口。 | `Name`, `Description`, `Execute(PluginMethodParameter[] args)` |
| `Runtime/Utils/PluginSystem/PluginManager.cs` | `PluginManager`, `ReflectionPluginMethod` | 插件 manifest 加载、DLL 存在检测、方法注册和反射调用。 | `LoadPlugins`, `ExecuteMethod`, method registry |
| `Runtime/Utils/PluginSystem/PluginManifestAbstract.cs` | `PluginManifestAbstract` | 插件 manifest 抽象基类。 | manifest 字段/属性 |
| `Runtime/Utils/PluginSystem/PluginMethodParameter.cs` | `PluginMethodParameter`, `PluginMethodParameterBuilder` | 插件方法参数对象和 builder，支持整数、字符串和小数参数。 | `ConvertTerm`, 参数字段 |
| `Modern/Script/Functions/ModernSqlManager.cs` | `ModernSqlManager`, `ReaderContext` | 现代 SQL 扩展函数运行时。 | `Connect`, `ExecuteReader`, `ReaderGet*`, `Disconnect` |

### Scripts/Emuera/Sub

| 文件 | 主要类型 | 职责 | 关键入口/函数 |
|---|---|---|---|
| `EmueraException.cs` | `EmueraException`, `ExeEE`, `CodeEE`, `FileEE`, `ScriptPosition` | 核心异常层次和脚本位置。 | `ScriptPosition`, exception 构造 |
| `EraStreamReader.cs` | `EraStreamReader` | Era 文本读取封装。 | `ReadLine`, `Dispose` |
| `EraDataStream.cs` | `EraDataReader`, `EraDataWriter`, `EraDataState` | 文本存档/数据流读写；提供 `SparseArray<long>` / `SparseArray<string>` 读写重载以保持 1D 稀疏变量存档兼容。 | `Read`, `Write`, `Dispose` |
| `EraBinaryDataReader.cs` | `EraBinaryDataReader`, `EraSaveFileType`, `EraSaveDataType` | 二进制存档读取，含 1808 兼容 reader；支持把 1D 整数/字符串数据读入 `SparseArray<T>`。 | `Read`, `ReadInt64`, `ReadString`, `Dispose` |
| `EraBinaryDataWriter.cs` | `EraBinaryDataWriter` | 二进制存档写入；支持从 `SparseArray<T>` 输出 1D 整数/字符串数据。 | `Write`, `WriteInt64`, `WriteString`, `Dispose` |
| `LexicalAnalyzer.cs` | `LexicalAnalyzer` | ERB 词法分析，生成 `WordCollection`。 | `Analyse`, string/form string 解析 |
| `Word.cs` | `Word` 及各 token word | 词法 token 模型。 | `ToString`, token 字段 |
| `WordCollection.cs` | `WordCollection` | token 列表和当前位置操作。 | `Current`, `ShiftNext`, `GetWord`, `Clone` |
| `SubWord.cs` | `SubWord` 系列 | 格式字符串内嵌片段。 | 派生类型字段 |
| `StringStream.cs` | `StringStream` | 字符串流读取工具。 | `Current`, `ShiftNext`, `Substring`, `Seek` |
| `Preload.cs` | `Preload` | 预加载/路径相关辅助。 | `GetFiles`, preload helper |

### Scripts/Emuera/_Library

| 文件 | 主要类型 | 职责 | 关键入口/函数 |
|---|---|---|---|
| `_Library/GDI.cs` | `GDI`, `StockObjects`, `StretchMode` | GDI 兼容常量/函数桩。 | GDI helper |
| `_Library/LangManager.cs` | `LangManager` | Emuera 内部语言文本管理。 | `Load`, `GetStr` |
| `_Library/SFMT.cs` | `MTRandom` | SFMT/随机数实现。 | `Next`, seed 初始化 |
| `_Library/Sys.cs` | `Sys` | 全局路径、exe dir 等系统信息。 | `ExeDir`, path 字段 |
| `_Library/WinInput.cs` | `WinInput`, `MouseButtons` | 鼠标/键盘输入兼容枚举和 helper；提供 `GETKEYTRIGGERED` latch 消费和清理。 | `GetKeyState`, `ConsumeKeyLatch`, `ClearLatches`, mouse helpers |
| `_Library/WinmmTimer.cs` | `WinmmTimer` | WinMM timer 兼容桩。 | 构造/字段 |

## 核心接口和抽象契约

| 契约 | 位置 | 说明 | 主要成员 |
|---|---|---|---|
| `IPluginMethod` | `Scripts/Emuera/Runtime/Utils/PluginSystem/IPluginMethod.cs` | 真正的 C# interface；插件方法统一入口。 | `Name`, `Description`, `Execute(PluginMethodParameter[] args)` |
| `IOperandTerm` | `GameData/Expression/IOperandTerm.cs` | 名字像接口，实际是 abstract class；所有表达式项的求值契约，类型主存储为 `EraType`，通过 `GetOperandType()` 兼容旧 CLR `Type` 调用。 | `GetIntValue`, `GetStrValue`, `GetFloatValue`, `GetValue`, `GetEraType`, `GetOperandType`, `Restructure` |
| `FunctionMethod` | `GameData/Function/FunctionMethod.cs` | 内置函数契约；所有 `*Method` 嵌套类继承它，默认参数检查按 `EraType` 比较表达式类型。 | `CheckArgumentType`, `GetIntValue`, `GetStrValue`, `GetReturnValue`, `UniqueRestructure` |
| `AbstractInstruction` | `GameProc/Function/Instruction.cs` | ERB 指令执行契约。 | `SetJumpTo`, `DoInstruction`, `CreateArgument`, `ArgBuilder` |
| `ArgumentBuilder` + `Argument` | `GameProc/Function/ArgumentBuilder.cs`, `Argument.cs` | 指令参数解析和参数对象契约；通用指令参数签名使用 `EraType[]`，不再依赖 CLR `Type` 做脚本语义比较。 | builder 构造 `Argument`；`Argument` 派生类保存解析结果 |
| `SparseArray<T>` | `GameData/Variable/SparseArray.cs` | 1D 稀疏变量存储契约；对外提供逻辑长度、默认值读取、已写入项枚举和少量数组操作，调用方不能再假设所有 1D 数组都是 CLR 数组。 | `Length`, indexer, `Entries`, `Shift`, `RemoveRange`, `Sort` |
| `VariableDescriptor` | `GameData/Variable/VariableDescriptor.cs` | 变量元数据契约；把 `VariableCode` 的类型、维度、保存和作用域位标志集中解释，供 identifier/token 复用。 | `VariableDescriptorTable.GetDescriptorByCode`, `VariableDescriptor.FromCode` |
| `VariableToken` | `GameData/Variable/VariableToken.cs` | 变量读写契约，覆盖标量/数组/角色/局部/引用/常量；基础元数据来自 `VariableDescriptor` 并公开 `EraType`，1D 数组访问需兼容 CLR 数组与 `SparseArray<T>`。 | `GetIntValue`, `GetStrValue`, `GetFloatValue`, `GetEraType`, `SetValue`, `SetValueAll`, `PlusValue` |
| `VariableTerm` | `GameData/Variable/VariableTerm.cs` | 表达式中的变量访问契约。 | `Get*Value`, `SetValue`, `Restructure` |
| `AContentFile` / `AbstractImage` / `ASprite` | `Content/*.cs` | 图片资源和精灵绘制契约。 | `Dispose`, `SpriteGetColor`, `GraphicsDraw` |
| `AConsoleDisplayPart` | `GameView/AConsoleDisplayPart.cs` | 一行显示中的最小渲染片段。 | `DrawTo`, `GDIDrawTo`, `ToString` |
| partial `EmueraConsole` | `GameView/EmueraConsole*.cs` | 控制台 facade；输入、输出、刷新、调试、图片层都集中在这里。 | `Print*`, `WaitInput`, `PressEnterKey`, `RefreshStrings`, `CBG_*` |
| partial `Process` | `GameProc/Process*.cs` | ERB 执行核心；初始化、循环、状态、lazy loading 分文件实现。 | `Initialize`, `DoScript`, `runScriptProc`, `TryLazyLoadErb` |

## 关键函数速查

### 启动/生命周期

| 任务 | 优先看 |
|---|---|
| 修改启动器扫描、游戏目录选择 | `FirstWindow._Ready`, `FirstWindow.ResolveStartupGamePath` |
| 修改主场景初始化 | `EmueraMain._Ready`, `EmueraMain.Run`, `EmueraMain.Restart` |
| 修改后台线程和输入唤醒 | `EmueraThread.Start`, `EmueraThread.Input`, `EmueraConsole.PressEnterKey` |
| 修改核心入口或 profile 选择 | `Program.Main`, `Program.DetectCoreProfile` |
| 修改退出/重启 | `EmueraMain._ExitTree`, `EmueraConsole.QuitAndRestart`, `GenericUtils.RestartGame` |

### UI/渲染/输入

| 任务 | 优先看 |
|---|---|
| 文本行添加/删除/刷新 | `GenericUtils.AddText`, `GenericUtils.ApplyTextChanges`, `EmueraContent.AddLine`, `EmueraContent.Canvas.AddCanvasLine`, `EmueraContent.UpdateDisplay` |
| 控制台打印语义 | `EmueraConsole.Print`, `PrintHtml`, `PrintButton`, `PrintFlush` |
| HTML 标签支持 | `HtmlManager.Html2DisplayLine`, `HtmlManager.tagAnalyze`, `ConsoleDivPart`；Canvas 后端相对 div overlay 见 `CanUseCanvasDivOverlay`、`UpdateCanvasDivOverlay`、`FlushCanvasOverlayRowsIfNeeded`、`RefreshCanvasOverlayVisibility`，逃逸 div 行索引用 `canvasRowsWithEscapedOverlays` |
| 行内图片/形状 | `ConsoleImagePart`, `ConsoleShapePart`, `EmueraContent.AddLine`, `ConsoleRenderSurface.DrawImagePart`；Canvas 后端复杂图片 overlay 见 `NeedsCanvasImageOverlay`、`UpdateCanvasImageOverlay`、`FlushCanvasOverlayRowsIfNeeded`、`RefreshCanvasOverlayVisibility`、`RefreshCanvasImageAnimations`，动画候选索引用 `canvasAnimatedImageOverlayKeys` |
| CBG/背景/图片层 | `EmueraConsole.CBG_*`, `SetImageLayer`, `EmueraContent.RefreshCBG` |
| 快捷按钮 | `QuickButtons.AddButton`, `QuickButtons.SetInputEnabled`, `EmueraContent.SubmitQuickButtonInput` |
| 屏幕输入面板 | `Inputpad.UpdateInputType`, `ShowPad`, `HidePad` |
| 缩放 | `Scalepad.SetScale`, `EmueraContent.SetContentScale`, `EmueraContent.NormalizeContentHorizontalScroll`, `ResolutionHelper.Apply` |
| Canvas 性能采样 | `ConsoleRenderSurface._Draw`, `ConsoleRenderSurface.RebuildHitRectsOnly`, `GenericUtils.SampleConsoleRenderFrame`；开启 `[debug.performance_sampling]` 后输出 `PERF.CONSOLE_RENDER`，包含 `draw_ms_avg/p95/max`、可视行、Canvas 行、overlay 行、part、hit rect 和节点规模快照；普通绘制窗口与点击前 hit-only 重建窗口分开统计，命中重建使用行级按钮矩形缓存与垂直桶索引，`PERF.*` 在 Godot/logcat 镜像中保留 `data` 字段 |

### 图片/精灵/ColorMatrix

| 任务 | 优先看 |
|---|---|
| 资源 CSV 到 sprite | `AppContents`, `SpriteManager.GetSprite`, `uEmuera.Utils.ResourcePrepare`；资源 CSV 路径解析缓存、lazy sprite 原始行索引、命中后实体化和慢实体化诊断在 `AppContents`；`ResourcePrepare` 对 CSV 只读头部字段，避免整行 `Split(',')` 分配。 |
| 纹理缓存/异步解码/主线程纹理上传 | `SpriteManager.TryGetTextureInfoCached`, `SpriteManager.RequestTextureInfoAsync`, `SpriteManager.UpdateOtherThreads`, `SpriteManager.TextureLoadVersion`, `TextureInfo.RecreateTexture` |
| Graphics surface 绘制 | `GraphicsImage.GCreate`, `GDrawCImg`, `GDrawG`, `GDrawString`, `GDrawLine`；显示提交前通过 `TryCreateDisplaySnapshot` 等待短暂稳定窗口并避开后台改图锁 |
| GPU ColorMatrix | `ColorMatrixGPU.GetSharedMaterial`, `ColorMatrixGPU.SetMatrixUniforms`, `GraphicsImage.ApplyColorMatrixGPU` |
| Godot 控件绘制图片 | `EmueraImage._Draw` |

### ERB 解析/执行

| 任务 | 优先看 |
|---|---|
| ERB 文件加载 | `ErbLoader.LoadErbFiles`, `ErbLoader.loadErbs` |
| lazy loading | `Process.TryLazyLoadErb`, `LoadLazyLoadingTable`, `SaveLazyLoadingList`；Android 索引缺失/失效优先走轻量标签扫描建表，非 Android 回到 BuildTable/full-load 建表；运行时用反向索引删除已加载文件映射，慢补加载诊断在 `Process.LazyLoading.cs` |
| 行解析 | `LogicalLineParser.ParseLine`, `ParseLabelLine`, `ParseSharpLine` |
| label 查找 | `LabelDictionary.GetEventLabels`, `GetNonEventLabel`, `GetLabelDollar` |
| 指令名映射 | `FunctionIdentifier.GetInstructionNameDic`, `FunctionIdentifier.IsPrint/IsInput/IsJump/IsMethod` |
| 指令执行 | `AbstractInstruction.DoInstruction`, `Instraction.Child.cs` 中对应 `*Instruction` |
| 脚本主循环 | `Process.DoScript`, `Process.ScriptProc.runScriptProc` |
| CALL/RETURN/BEGIN 状态 | `ProcessState.IntoFunction`, `Return`, `ReturnF`, `SetBegin`, `Begin` |

### 表达式/变量/函数

| 任务 | 优先看 |
|---|---|
| 表达式解析 | `ExpressionParser`, `ExpressionMediator`, `IOperandTerm` |
| 运算符 | `OperatorManager`, `OperatorMethodManager`, `SafeArithmetic` |
| 变量名解析 | `VariableIdentifier.GetVariableId`, `VariableParser` |
| 变量读写 | `VariableEvaluator`, `VariableToken`, `VariableTerm` |
| 新增普通内置函数 | `GameData/Function/Creator.Method.cs` + `FunctionMethodCreator.GetMethodList` |
| 新增 SQL/Map/XML/DT 函数 | 对应 `Creator.Method.Sql/Map/Xml/DT.cs` |
| 用户定义函数 | `UserDefinedFunctionData`, `UserDefinedMethodTerm`, `CalledFunction`, `ProcessState.IntoFunction` |

### 诊断/日志

| 任务 | 优先看 |
|---|---|
| 日志开关和限流 | `DiagnosticLogRouter` |
| 写日志 | `GenericUtils.Debug/Info/Warn/Error`, `DiagnosticLogSinks.Write` |
| 导出诊断包 | `GenericUtils.ExportDiagnosticPackage`, `DiagnosticLogExporter.ExportDiagnosticPackage` |
| 运行期配置 | `RuntimeDiagnosticsConfig`, `RuntimeDiagnosticsConfigLoader`, `RuntimeDiagnosticsConfigWriter` |
| 输入回放 | `InputReplayBuffer`, `GenericUtils.CaptureInputReplay` |
| 诊断浮窗 | `RuntimeDiagnosticsPanel.AttachFloatingTo` |
| 性能采样 | `GenericUtils.SamplePerformanceFrame`, `GenericUtils.SampleConsoleRenderFrame`；`PERF.SAMPLE` 记录 FPS/帧耗时/UI 队列/纹理队列，`PERF.CONSOLE_RENDER` 记录 Canvas 控制台可视绘制与命中表重建窗口数据；`PERF.*` 镜像输出会附带 `data` 便于直接 grep logcat |

## 常见修改定位

| 要改什么 | 从这里开始 |
|---|---|
| Android 游戏目录、屏幕宽度策略 | `FirstWindow`, `EmueraContent.GetSafeViewportRect`, `EmueraContent.ApplySafeAreaLayout`, `Program.ApplyAndroidWindowWidthPolicy`, `Config.UpdateWindowWidth` |
| 输入后脚本不继续 | `EmueraThread.Input`, `EmueraConsole.PressEnterKey`, `Process.InputInteger/InputString` |
| 文本没有刷新或顺序错 | `GenericUtils.FlushUI`, `EmueraContent.ApplyTextChanges`, `EmueraConsole.PopDisplayingLines` |
| 图片/立绘不显示 | `AppContents`, `SpriteManager`, `ConsoleImagePart`, `EmueraImage`, `GraphicsImage` |
| ColorMatrix 效果错误 | `ColorMatrixGPU`, `GraphicsImage.GDrawCImg`, shader `Scripts/Shaders/color_matrix*.gdshader` |
| HTML 显示错误 | `HtmlManager`, `ConsoleDivPart`, `ConsoleStyledString`, `PrintStringBuffer` |
| ERB 函数找不到 | `FunctionMethodCreator.GetMethodList`, `FunctionIdentifier`, `LabelDictionary`, lazy loading 表 |
| 变量读写错误 | `VariableIdentifier`, `VariableParser`, `VariableEvaluator`, `VariableToken` |
| 存档兼容 | `EraDataStream`, `EraBinaryDataReader`, `EraBinaryDataWriter`, `VariableData`, `VariableEvaluator`, `CharacterData`；1D 稀疏数组读写、RuntimeDataStore 的 VarExt 保存域读写也在这些入口 |
| SQL/Map/XML/DT 扩展 | `RuntimeDataStore`, `ConstantData` 的 VarExt 保存域声明读取、`SnakeSqlManager`, `ModernSqlManager`, `Creator.Method.*.cs` |
| 日志太多或没有日志 | `config.toml` 的 `[logging].enabled` 与模块开关、`RuntimeDiagnosticsConfig`, `DiagnosticLogRouter`, `GenericUtils.InitializeLogging` |
