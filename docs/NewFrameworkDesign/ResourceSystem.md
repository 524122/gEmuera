# AppContents 资源模型、导入与缓存

## 兼容事实

上游 XEmuera `AppContents.LoadContents` 定义 CSV 资源语义。旧 gEmuera 还增加 lazy CSV 索引、路径级纹理 cache、CSV sprite 生命周期保护、异步纹理 pending 重试和游戏根/`resources/` 路径回退；这些不是可以丢弃的实现偶然，必须由 GE/SN/FL fixture 判定哪些上升为目标契约。

## CSV 记录

```text
ANIME declaration:
  name,ANIME,width,height

sprite/frame:
  name,file[,x,y,width,height[,offsetX,offsetY[,delayMs]]]
```

- name trim 后大写，形成逻辑 ResourceKey。
- file 中反斜杠换成平台 separator，并相对当前 CSV 目录解析。
- 空行、trim 后空行和 `;` 起始注释跳过。
- tokens 少于 2、空 name/file 返回无定义。
- `ANIME` 需要正 width/height 且不超过 8192。
- 普通文件必须含扩展名；缺失/解码失败警告并忽略该行。
- 默认 crop 为整图，默认 offset=(0,0)，delay=1000。
- 显式 crop 宽高必须为正，并与父图至少相交；是否裁切到边界需 fixture。
- delay 解析成功且小于等于 0 时警告并拒绝帧。
- 当前动画非空且名称相同则追加 frame，否则创建 SpriteF。
- 重复 Sprite key：保留先项，警告并 Dispose 后项。

CSV 发现顺序依赖 `FileUtils.GetFiles` 的实际顺序；在机械 fixture 确认前标 Pending。目标导入必须把枚举顺序记录进 manifest，不能声称跨平台稳定后改变重复项结果。

## Core descriptors

Core `ResourceCatalog` 保存 ResourceKey、SourceFileToken、crop、offset、animation frames 与声明位置。GraphicsImage/G 是按 int ID 管理的可变图形槽；动态 Sprite 与 CBG layer 是会话逻辑状态。所有模型都不含 Godot Image/Texture/RID/Color。

## CPU 像素真相与 Bridge 解码

```text
descriptor requested
 → catalog resolves logical key
 → content source opens bounded stream
 → worker reads header and validates dimensions/pixels
 → decoder port produces owned PixelSurface under reservation
 → VM/session PixelStore registers authoritative pixels + revision
 → main-thread completion generation guard
 → upload immutable revision to ImageTexture/AtlasTexture/backend handle
 → register ownership/memory cost
```

PixelStore 是 GGETCOLOR、SPRITEGETCOLOR、GDraw 等脚本可观察结果的唯一真相；Texture/RID 只能是 `(handle,revision)` 派生投影，禁止依赖 GPU readback。首次文件 decode 若指令必须同步返回，使用 `WaitPort`；迁移期可以在专用 VM thread bounded blocking，但不能在 Godot 主线程同步 `load()`。导入文件不伪装 `res://resources`。

## Graphics、Sprite 与 CBG

| 对象 | owner | 可变操作 | 清理 |
| --- | --- | --- | --- |
| source image descriptor | ResourceCatalog | 候选加载期固定 | session dispose |
| decoded CPU source image | Session PixelStore/source cache | decode、pixel query、G draw source | 强引用为零且预算驱逐 |
| GPU texture/atlas region | ResourceBridge | 主线程按 revision 创建/更新 | view refs=0 + cache evict |
| GraphicsImage/G | Session PixelStore/G slot table | VM thread 同步 create/draw/get pixel/dispose | title/game reset matrix |
| SpriteF/Anime | ResourceCatalog/session dynamic catalog | create/frame/pos/transform/dispose | explicit dispose/session unload |
| CBG layer/button map | Console display state | set/clear/range/button | clear/session dispose |

`UnloadContents` 对应释放 source resource、Sprite 和 G；`UnloadGraphicList` 只处理动态 G。每次像素修改原子增加 revision 并记录 dirty rect；Bridge 只上传完整已提交 revision，不能显示中间合成状态。目标引用计数/句柄表必须表达清理差异。

## 同名和路径

逻辑 key 与 canonical source path 分开。不同目录同 basename 可作为不同 source 被不同 key 引用，不会因 basename 静默覆盖。相同 key 的重复处理遵循 CSV 顺序/警告。路径经过[SecurityLimits](SecurityLimits.md)的 root containment 与链接检查。

## 威胁模型

游戏包完全不可信。枚举文件数/深度/总量；CSV 限行/字段；图片在 decoder 前限 header、8192 尺寸与像素；动画限帧；引用链限深并检测 cycle；音频由 AudioSystem 施加时长/PCM 预算。失败区分：

- Reject package：路径逃逸、总预算、结构性恶意。
- Reject resource：单图损坏/超限，若兼容允许继续。
- Placeholder：显示可继续且脚本行为允许缺图。
- Warning：原版只警告的兼容行为，必须进入差分报告。

## 缓存与计量

cache key 至少包含 source identity、crop、import options 和 revision。分别计 CPU PixelStore、GPU texture、derived crop/atlas、animation frame refs 和 compressed source。所有 decode/upload 先向会话 `MemoryBudget` 原子 reserve，失败则排队、驱逐或拒绝，finally release；LRU 只能移除 cache 持有，Display/CBG 的强引用仍存在时不可释放。

## 移动导入

Android SAF/iOS picker 先复制到 `user://imports/<generation>.tmp`，边复制边计算 hash/总量。验证通过后 rename 为 content-addressed cache，Session Catalog 引用 token。覆盖同一游戏使用新 generation 目录，提交成功后再回收旧目录，避免进程中断破坏当前包。

## fixture

覆盖 CSV 顺序/重复、lazy 首次实体化、大小写 key、路径级同名隔离、ANIME/frame、缺图/pending 重试、crop、动态 G/Sprite 生命周期、CBG 和恶意包。G 套件逐操作比较 dimensions、返回值、代表像素/全图 hash、revision、下一条指令读回和最终纹理截图。
