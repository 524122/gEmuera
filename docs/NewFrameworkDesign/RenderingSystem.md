# 虚拟控制台、字体、Theme 与渲染后端 ADR

## 固定边界

Core 产生 Display DTO v2：Line、Text/Image/Shape/Div tree、Style、Interaction、transaction、scroll intent 和 data-only patch；Godot View 负责 shaping、布局、绘制、焦点、选择和可访问投影。View 不解析 ERB/HTML、不改变量、不持有脚本控制流。

## 功能需求

- 长历史追加、回看、裁剪与增量重排。
- CJK/Latin/组合字符/emoji/RTL shaping、字体 fallback、行高和缩放。
- 文本、样式、图片、shape、按钮、locked X、CBG layer。
- `src/srcb` 双图片、relative/absolute div、depth、负坐标、盒模型、嵌套子行与跨行溢出。
- append/update/remove/data-only 原子批次、动态地图 scope 与滚动意图。
- 鼠标/触摸 hit test、键盘/控制器焦点、文本选择/复制、IME。
- 屏幕阅读器或可访问 fallback。
- Theme、深色/高对比、DPI/safe area、桌面与移动布局。

## 后端候选 ADR

| 候选 | 节点模型 | 优点 | 风险/必须验证 |
| --- | --- | --- | --- |
| A 自绘 Canvas | 1 个 ConsoleCanvas + 少量 overlay controls | 历史规模与绘制批次可控，精确复现 | 自建 shaping/hit/selection/IME/accessibility；server API 生命周期 |
| B 虚拟化 Control | ScrollContainer + pooled line controls | 原生焦点/IME/选择/Theme 较好 | node pool、富组合布局、跨行选择、测量一致性 |
| C RichTextLabel 分块 | 若干虚拟化块 | BBCode/选择/Meta 能力 | Emuera HTML 映射不一一对应、按钮/locked pos/大历史 |
| D 混合 | 自绘主历史 + accessible/control overlay | 性能与无障碍折中 | 两套布局必须同 measurement revision |

旧简化 Display 模型已废止。DTO v2 是 Required，只有 eraFL div/srcb/动态地图 golden 与旧 ConsoleDivPart/ConsoleImagePart 差分通过后才 Accepted。旧 gEmuera 的“Canvas 普通行 + 复杂 div 整行 Control fallback + 全 Controls 开关”作为迁移基线保留；新后端不能先删除这条回退路径。

M2 的首个交付物不是新布局器，而是 `LegacyConsoleAdapter` 的无损深复制。M2.0 只旁路生成 DTO，旧 renderer 仍消费原 `ConsoleDisplayLine`；M2.1 才在独立 flag 下让 DTO 经现有 View adapter 投影。转换阶段禁止字体测量、坐标取整、fallback 决策、HTML 再解析和任何回写，具体门禁见 [M0M2ImplementationBaseline](M0M2ImplementationBaseline.md)。

## 行高索引

必须使用 Fenwick tree、segment tree 或前缀高度+增量重建，提供：

```text
UpdateHeight(lineIndex, delta)     O(log n)
PrefixHeight(exclusiveIndex)       O(log n)
FindLineAtY(documentY)             O(log n)
VisibleRange(scrollY, viewportH)   O(log n + visible)
```

缓存 `LayoutEntry{lineId,revision,width,fontRevision,height,partRuns,hitSpans}`。绘制和 hit test 从 `FindLineAtY` 开始，只遍历 visible + overscan，不从第 0 行扫描。

## 索引更新规则

| 事件 | 操作 |
| --- | --- |
| append | 测量新行、Fenwick append；若跟随底部则在布局完成后的下一 frame 更新 scroll |
| line mutation | revision++；重测该行并 `UpdateHeight` |
| viewport width | 增加 layout width revision；可见优先、后台/分帧重排其余行 |
| font/theme/scale | fontRevision++，清 shaping cache；保持 anchor line + intra-line offset |
| history trim | 批量删除前缀并重建/可删除树；调整 scroll 以保持视觉锚点 |
| image decoded | 只重排行引用该 handle 的行 |
| CBG change | 独立 layer dirty region，不重排行文本 |

`_firstVisibleLine`（或等价字段）是 `FindLineAtY` 的结果并真正用于 draw/hit，不是无效缓存。

## Layout pipeline

```text
Display batch commit
 → apply remove/upsert/data-only as one transaction
 → resolve StyleToken to Theme/font candidates
 → shape text runs with fallback
 → resolve image intrinsic/requested size
 → line break/locked position/alignment
 → create hit spans and selection map
 → update height index
 → queue redraw / update pooled controls
```

同一 shaped run 的测量与绘制使用同一 Font/TextLine/TextServer 结果，避免 `FontSize=0` 或不同 API 引起偏移。`ConsoleButton` hit rect 从实际 part runs 联合产生；Height、HighlightColor、FontSize 均来自明确 layout/theme context。

## Godot API

锁定版本的 ApiSmoke 在 `CanvasItem.DrawString`、`Font.DrawString`、TextLine 或 TextServer 中选实际可编译路径。自绘不在 `_Draw` 修改游戏状态，也不在 hot path 查询 RenderingServer getter。若使用 RID，Bridge 强持有所有资源并在 `_ExitTree` 显式 free RID；RID 不进入 Core。

## Theme

根 Control 挂唯一 Theme；子控件继承。`ConsoleButton`、`DangerButton`、`LinkButton`、`InputCandidate` 使用 type variation。共享 StyleBox 不逐节点 `_Ready` 创建，不在 `_Draw/_Process` 修改。自绘组件在主题变化通知后更新 cached Color/Font/StyleBox 引用并使 fontRevision 失效。

高对比 Theme 保留明显焦点轮廓；不能用空 focus style。颜色来自 Theme palette，Display StyleToken 的游戏颜色与应用 UI Theme 颜色区分。

## 字体发现与 fallback

顺序：游戏声明字体（受限文件 token）→ 用户选择字体 → 应用默认 CJK 字体 → 系统/内置 fallback → 缺字符方框。字体加载失败记录 warning 并继续 fallback，不让整个会话崩溃。

测量覆盖 CJK、Latin、全/半角、组合字符、变体选择符、emoji ZWJ、RTL/bidi 和换行。禁止固定“全角=字号、半角=字号/2”。应用 UI 本地化与游戏文本编码/字体是两个配置域。

## 输入、选择与触摸

hit test：屏幕坐标→documentY→FindLineAtY→该行 hit spans 二分/短线性扫描。按下只建立 candidate；拖动超过 dp 阈值或滚动开始即取消；释放仍命中同 interaction 才提交。多点触控不会误触按钮。

Android 虚拟光标复用完全相同的 hit index；一次命中同时生成 ERB `MOUSEBUTTON()` pointing state 和 visual hover patch。不得为 Canvas 与脚本查询维护两套坐标换算/命中算法。

## 动态地图与滚动事务

`DisplayTransaction.Scroll` 至少是 FollowBottom、PreserveViewport、KeepChoicesVisible。动态地图/BitmapCache 区域直到强制 flush/input wait 才以 `AtomicVisibility=true` 提交，避免旧菜单、半张地图和完整地图依次闪现。data-only patch 只更新按钮 value/generation/hit metadata，不重建未变化的视觉节点。布局后用 line anchor + intra-line offset 恢复视口。

选择保存 lineId/cluster index，不按 UTF-16 code unit 粗切 shaping cluster。复制输出 plain text 或受控 HTML，不含任意 BBCode/meta。键盘焦点使用 Control overlay/accessible nodes；IME 输入交给 InputPanel，不由自绘 Canvas 仿造系统输入法。

## 可访问 fallback

若自绘后端无法暴露每个文本/button 的 accessibility tree，必须同步一个虚拟化 Control/RichText fallback：只包含可见和焦点邻域，语义与 Display model 一致。屏幕阅读器、键盘焦点、选择/复制失败则 ADR 不能把纯自绘设为唯一后端。

## CBG

CBG 是独立逻辑 layer list，按 z 排序。Bridge 将 ResourceKey/G handle 转为 texture、位置和 button map。文本历史 reflow 不重建 CBG；CBG dirty 仅使背景层 redraw。z=0/range/缺图行为由 Instruction fixture 决定。

## benchmark 场景

1. 100k 历史行，混合 80% 文本、10%按钮、5%图片、5%shape。
2. eraFL 状态页：嵌套 div、srcb、负坐标、跨行溢出、动态地图和 data-only 更新。
3. CJK+Latin+emoji+RTL+fallback 反复缩放。
4. 快速 append 同时回看旧历史。
5. 触摸惯性滚动经过按钮。
6. Theme/字体/窗口 width 改变后的分帧 reflow。

报告 p50/p95/p99 frame、layout/draw time、draw calls、managed/native/GPU memory、node/RID 数、输入误触和可访问测试。性能目标由[PerformanceOptimization](PerformanceOptimization.md)定义，未测量前不写保证。
