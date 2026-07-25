# Emuera HTML 语法、Display 转换与 BBCode 安全

## 权威实现

语法以 `Emuera/GameView/HtmlManager.cs` 的 `Html2DisplayLine`、`tagAnalyze`、`Escape`、`Unescape` 为准。目标 Core 直接解析为 DisplayLine/DisplayPart，不要求先转 BBCode；RichTextLabel 只是候选后端。

## 实体和注释

| 输入 | 结果/规则 |
| --- | --- |
| `&nbsp;` | 普通空格 |
| `&amp; &gt; &lt; &quot; &apos;` | 对应字符 |
| `&#123;` | 十进制 UTF-16 code unit |
| `&#x7b;` | 十六进制；x 小写规则需 fixture |
| 数值 <0 或 >0xFFFF | `CodeEE` |
| 无分号、连续 `&;`、未知实体 | `CodeEE` |
| `<!-- ... -->` | 注释丢弃；未闭合报错 |

`Escape` 转义 `& > < " '`。这不是完整标准 HTML parser，不能套浏览器容错规则。

## 标签矩阵

| 标签 | 属性 | 主要约束 | Display 结果 |
| --- | --- | --- | --- |
| b/i/u/s | 无 | 不可重复开启；需正确关闭 | StyleToken flag |
| br | 无 | 强制行断点；文本换行同等处理 | 新 DisplayLine |
| nobr | 无 | 必须在行首；重复/位置错误报错；闭合可省略 | NoWrap line |
| p | `align=left/center/right` | 必须在行首；闭合可省略 | alignment |
| font | `face,color,bcolor` | 属性不可重复；嵌套继承未指定项 | style stack |
| button | value/title/pos 等以源码为准 | button/nonbutton 不可嵌套 | Interaction |
| nonbutton | title/pos | 形成非提交交互/tooltip 语义 | non-button segment |
| clearbutton | tooltip 扩展 | 不可嵌套；改变内部按钮化 | state flag |
| img | src/srcb/width/height/xpos/ypos/display/cm | 属性唯一；普通/选中资源、定位、翻转/矩阵语义 | ImagePart(normal,selected,placement) |
| shape | type/param/color/bcolor | type 与参数由源码方法验证 | ShapePart |
| div | xpos/ypos/width/height/depth/color/display/margin/padding/border/radius/bcolor | 递归子行、自闭合扩展、负坐标/溢出；关闭规则非标准 HTML | DivPart + children tree |

完整属性表必须由 `tagAnalyze` 机械提取并与 golden 结果维护；表中“等”不能作为实现省略依据。

## 状态机

Parser 保存 LineHead、FontStyle flags、font stack、Nobr/P 状态、Current/LastButtonTag、clearbutton flags、pending BR/button。遇到文本先 Unescape 并创建 styled part；标签可产生 part 或修改状态。BR/按钮边界把累积 parts 转为 ConsoleButtonString 等价交互，再由 line builder 排版。

结束时：button/font/style 未闭合报错；nobr/p 可省略关闭。locked X 仅在 nobr + left alignment 合法。错误输入不能部分追加到 DisplayHistory：先构造本次输出 batch，解析成功后一次提交。

## Core Display 映射

- styled text → TextPart + immutable StyleToken。
- img/srcb → 正常/选中两个 ResourceKey、SizeSpec、PositionSpec、DisplayMode、flip 和可选 ColorMatrixRef。
- shape → ShapePart + checked integer params/Rgb24。
- button/nonbutton → Interaction(value、tooltip、lockedX)。
- br → DisplayLine boundary；div → `DivPart`，完整保存位置、尺寸、depth、背景、盒模型、display mode 和嵌套子行，绝不简化成行边界。

View 不再次解析原 HTML。这样自绘、Control 和 accessible fallback 共用相同 golden model。

## BBCode 后端

若 RichTextLabel 原型使用 BBCode：外部文本中的 `[`/`]` 必须转义；只由 trusted mapper 生成允许标签。`meta` 是内部 typed token，不接受 URL/文件路径。禁止 `[img]` 直接读取游戏提供路径，图片由 ResourceBridge 注册 logical handle 后以受控 API 插入。`meta_clicked` 只发 intent，不在主线程执行重逻辑。

## 限制

输入最大 4 MiB、嵌套 128、属性 64/标签、总标签 100k；超限返回 typed fault，不追加任何行。循环/深 div、超长实体、超大图片由 SecurityLimits 拒绝。

## golden/fuzz

golden 覆盖每个标签/属性、嵌套 style、button/nonbutton/clearbutton、div、空白、换行、注释、实体、数字边界、未闭合/重复/未知标签、locked pos 与图片/shape。eraFL 套件必须覆盖嵌套 relative/absolute div、depth、负坐标、跨行溢出、盒模型、`src/srcb` 切换、自闭合 div 和异步图片补齐。比较完整 Display tree、layout box、interaction、transaction 和 error code，不只比较最终纯文本。

fuzzer 从合法 grammar 变异标签/属性/实体，限制输入大小和执行时间。任何 crash、OOM、越界资源访问或部分提交均为失败。
