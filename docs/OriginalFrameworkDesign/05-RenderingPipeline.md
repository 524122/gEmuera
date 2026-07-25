# 渲染管线

## 渲染架构概览

gEmuera-future 采用双后端渲染：Canvas 后端处理常规文本/形状行以减少节点数，Control 后端处理复杂 HTML/div 行以确保正确性。

```
┌─────────────────────────────────────────────────────────────────┐
│                    ScrollContainer (滚动裁剪)                     │
│  ┌───────────────────────────────────────────────────────────┐  │
│  │              scaledContentRoot (缩放容器)                   │  │
│  │  ┌─────────────────────────────────────────────────────┐  │  │
│  │  │       ConsoleRenderSurface (Canvas 后端)             │  │  │
│  │  │       ├─ _Draw() 批量绘制文本/形状/图片              │  │  │
│  │  │       ├─ CanvasImageOverlay (异步图片覆盖)           │  │  │
│  │  │       └─ CanvasDivOverlay (复杂 div 覆盖)           │  │  │
│  │  ├─────────────────────────────────────────────────────┤  │  │
│  │  │       lineContainer (Control 后端, 仅复杂行)         │  │  │
│  │  │       └─ 每行一个 Control 节点                       │  │  │
│  │  └─────────────────────────────────────────────────────┘  │  │
│  └───────────────────────────────────────────────────────────┘  │
│  ┌───────────────────────────────────────────────────────────┐  │
│  │  cbgContainer (背景图层, z-depth 排序)                     │  │
│  │  └─ TextureRect[] (每个 CBG 图层一个节点)                  │  │
│  └───────────────────────────────────────────────────────────┘  │
│  ┌───────────────────────────────────────────────────────────┐  │
│  │  bgRect (纯色背景)                                        │  │
│  └───────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────┘
```

## 渲染后端选择

```csharp
enum ConsoleRenderBackend
{
    Canvas,   // 默认: 单个 _Draw() 批量绘制，节点数极少
    Control   // 回退: 复杂行使用独立 Control 节点
}
```

### Canvas 后端 (主路径)

- 所有普通文本行通过 `ConsoleRenderSurface._Draw()` 单次绘制
- 使用 `DrawString()` / `DrawRect()` / `DrawTextureRect()` 直接在 CanvasItem 上绘制
- 可见区域裁剪：只绘制 ScrollContainer 视口内的行
- 图片行通过 `CanvasImageOverlay` 节点叠加（需要独立纹理引用）
- 复杂 div 通过 `CanvasDivOverlay` 节点叠加

### Control 后端 (复杂行回退)

- 包含 `ConsoleDivPart` 的行强制退回 Control 渲染
- 每行一个 Control 节点，内部按 Part 子节点排列
- 支持裁剪、负坐标、跨行叠放等 div 特性
- 节点数较多但渲染正确性有保证

### 后端判定逻辑

```csharp
bool CanRenderPartOnCanvas(ConsoleDisplayLine line)
{
    // 包含 ConsoleDivPart → 走 Control 后端
    foreach (var btn in line.Buttons)
        foreach (var part in btn.Parts)
            if (part is ConsoleDivPart) return false;
    return true;
}
```

## 显示元素层次

```
ConsoleDisplayLine (一行)
├── LineNo                    // 行号（全局唯一递增）
├── Alignment                 // LEFT / CENTER / RIGHT
├── ConsoleButtonString[]     // 可点击区域数组
│   ├── Position / Width      // 位置和宽度
│   ├── IsButton              // 是否可点击
│   ├── Input (long)          // 点击提交值
│   └── AConsoleDisplayPart[] // 显示部件数组
│       ├── ConsoleStyledString   // 带样式文本片段
│       │   ├── Str               // 文本内容
│       │   ├── StringStyle       // 颜色/粗斜/字体
│       │   └── Width             // 预计算宽度
│       ├── ConsoleImagePart      // 内联图片
│       │   ├── ResourceName      // sprite 名称
│       │   ├── SrcRect           // 源矩形裁剪
│       │   └── Width/Height      // 显示尺寸
│       ├── ConsoleShapePart      // 几何形状(矩形/线段)
│       │   ├── ShapeType         // rect / line
│       │   ├── Color             // 填充/描边色
│       │   └── Rect              // 绘制矩形
│       └── ConsoleDivPart        // HTML div 容器
│           ├── Position (x, y)   // 相对定位
│           ├── Width / Height    // 容器尺寸
│           ├── Border            // 边框定义
│           └── Children[]        // 子 Part 递归
```

## 渲染流程

### 1. 脚本输出 → 显示行生成

```
ERB: PRINT "Hello"
  → EmueraConsole.Print("Hello", style)
  → PrintStringBuffer.Append("Hello", style)
  → (遇到 PRINTL 或 NewLine)
  → PrintStringBuffer.FlushToLine()
  → 创建 ConsoleDisplayLine (含 ConsoleButtonString[])
  → 添加到 displayLineList
  → Refresh() → dirty = true
```

### 2. 主线程拉取 → 行布局

```
EmueraContent._Process() / Update():
  if (dirty) {
    foreach (new line in displayLineList) {
      lineObjects[line.LineNo] = line;
      lineNumbers.Add(line.LineNo);
      lineSizes[line.LineNo] = CalculateLineSize(line);

      if (CanRenderPartOnCanvas(line)) {
        // Canvas 路径: 记录布局，标记重绘
        canvasLineButtonHits[line.LineNo] = BuildHitRects(line);
      } else {
        // Control 路径: 创建节点
        lineControls[line.LineNo] = BuildControlForLine(line);
      }
    }
    RebuildLineLayout();  // 重新计算 lineLayoutEntries (prefix sums)
    consoleRenderSurface.QueueRedraw();  // 标记 Canvas 重绘
  }
```

### 3. Canvas 绘制 (`_Draw`)

```csharp
// ConsoleRenderSurface._Draw()
void _Draw()
{
    // 1. 确定可见行范围
    var visibleRange = GetVisibleLineRange(scrollOffset, viewportHeight);

    // 2. 遍历可见行
    foreach (var entry in visibleRange)
    {
        var line = lineObjects[entry.LineNo];
        float y = entry.Top;

        // 3. 绘制每个 Part
        foreach (var btn in line.Buttons)
        {
            float x = btn.Position;
            foreach (var part in btn.Parts)
            {
                if (part is ConsoleStyledString text)
                    DrawString(font, pos, text.Str, color, fontSize);
                else if (part is ConsoleImagePart img)
                    DrawTextureRect(texture, rect);
                else if (part is ConsoleShapePart shape)
                    DrawRect(shape.Rect, shape.Color);
                x += part.Width;
            }
        }
    }
}
```

### 4. 按钮命中检测

Canvas 后端无子节点，命中检测靠预计算的 hit rect 数组：

```csharp
ConsoleButtonHit[] hits = canvasLineButtonHits[lineNo];

// 触摸坐标 → 行号 → 按钮查找
int hitLineNo = FindLineAtY(touchY);
foreach (var hit in canvasLineButtonHits[hitLineNo])
{
    if (hit.Rect.HasPoint(localPos))
    {
        // 找到按钮
        selectedInput = hit.Input;
        break;
    }
}
```

## 异步纹理管线

### 纹理生命周期管理 (Pin 机制)

```
SpriteManager
├── TextureInfo
│   ├── Texture2D texture   // GPU 纹理 (可为 null, 等待上传)
│   ├── Image cpuImage      // CPU 图片 (解码完成)
│   ├── int pinCount        // 引用计数
│   └── bool uploaded       // 是否已上传 GPU
│
├── Pin(name) → TextureInfo   // 增加引用计数
├── Unpin(TextureInfo)        // 减少引用计数, 0 时释放
└── UploadPending()           // 主线程每帧调用, 上传就绪图片
```

### 行纹理 Pin

```csharp
// 添加行时 pin 所有引用的纹理
void AddLine(ConsoleDisplayLine line)
{
    var pins = new List<TextureInfo>();
    foreach (var imagePart in line.GetImageParts())
    {
        var info = SpriteManager.Pin(imagePart.ResourceName);
        pins.Add(info);
    }
    lineTexturePins[line.LineNo] = pins;
}

// 移除行时 unpin
void RemoveLine(int lineNo)
{
    foreach (var pin in lineTexturePins[lineNo])
        SpriteManager.Unpin(pin);
    lineTexturePins.Remove(lineNo);
}
```

### 异步加载流程

```
1. 脚本引用图片 → SpriteManager.Pin("BG01")
2. 如果纹理未就绪:
   a. 查询 lazyImageDictionary 获取文件路径
   b. WorkerThreadPool 后台解码为 Image
   c. 主线程 _Process 中 UploadPending()
   d. Image → ImageTexture (GPU 上传)
   e. asyncTexturePendingLineNos 记录待刷新行
3. 下一帧: 重绘包含该纹理的行
```

## CBG 背景图层

```csharp
// 脚本侧
CBGSETG "bg01.png", 0, 0, 100   // name, x, y, zdepth

// 引擎侧
EmueraConsole.CBG_SetImage("bg01", 0, 0, 100);
RequestCbgRefresh();  // 标记需要刷新

// Godot 侧 (下一帧)
EmueraContent.RefreshCBG():
  cbgContainer.ClearChildren();
  foreach (cbg in sortedByZDepth):
    var tex = SpriteManager.Pin(cbg.SpriteName);
    var rect = new TextureRect { Texture = tex.Texture, Position = (x, y) };
    cbgContainer.AddChild(rect);
    cbgTexturePins.Add(tex);
```

## 文字样式与字体

### StringStyle

```csharp
struct StringStyle
{
    Color Color;          // 文字颜色 (ARGB)
    FontStyle FontStyle;  // Bold=1, Italic=2, Strikeout=4, Underline=8
    string Fontname;      // 字体族名 (默认 "MS Gothic" 或配置字体)
}
```

### 字体解析

```csharp
Font ResolveConsoleFont(ConsoleStyledString part)
{
    string family = part.Font?.FontFamily?.Name;
    if (family == null || family == Config.FontName)
        return mainConsoleFont;  // 主字体(预加载)

    // 片段字体: 从游戏目录按名查找
    if (consoleFontCache.TryGetValue(family, out var cached))
        return cached;
    if (missingConsoleFonts.Contains(family))
        return mainConsoleFont;  // 已知缺失, 回退

    // 尝试加载: font/ → Font/ → fonts/ → Fonts/
    var font = TryLoadFontFromGameDir(family);
    consoleFontCache[family] = font ?? mainConsoleFont;
    return consoleFontCache[family];
}
```

## 运行时缩放

```csharp
// Scalepad 提供手动缩放 UI
float scaleFactor = 1.0f;

void ApplyScale(float newScale)
{
    scaleFactor = newScale;
    scaledContentRoot.Scale = new Vector2(scaleFactor, scaleFactor);
    // 重新计算滚动边界
    scrollContainer.GetVScrollBar().MaxValue = totalHeight * scaleFactor;
}

// Fit 按钮: 自动计算使内容完整可见的缩放
void OnAutoFit()
{
    float contentWidth = Math.Max(Config.DrawableWidth, GetCurrentVisualContentWidth());
    float viewportWidth = scrollContainer.Size.X;
    float fitScale = viewportWidth / contentWidth;
    ApplyScale(fitScale);
}
```

## 性能指标

| 场景 | Canvas 行数 | 节点数 | Draw Call |
|------|------------|--------|-----------|
| 普通文本 500 行 | 500 | ~5 (固定) | 1 |
| 含图片 50 行 | 50 | ~55 (overlay) | ~50 |
| 含 div 10 行 | 10 | ~30 (control) | ~30 |
| eraFL 状态页 | ~30 | ~100 | ~50 |

Canvas 后端将普通文本行合并为单次 `_Draw()` 调用，显著减少移动端 draw call。
