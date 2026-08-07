# 显示与渲染管线

## 概述

XEmuera 的渲染系统将原版 Emuera 的 GDI+ 渲染替换为 SkiaSharp，实现跨平台 2D 绘图。核心是一个基于文本的控制台模拟器，支持样式文字、可点击按钮、图像显示和背景图层。

## 渲染架构

```
┌─────────────────────────────────────────────┐
│          EraPictureBox (SKCanvasView)        │
│          SkiaSharp 画布控件                    │
└────────────────────┬────────────────────────┘
                     │ OnPaintSurface
┌────────────────────▼────────────────────────┐
│           EmueraConsole (渲染逻辑)            │
│  ┌───────────────────────────────────────┐  │
│  │ CBG 背景图层 (cbgList, z-depth 排序)   │  │
│  ├───────────────────────────────────────┤  │
│  │ displayLineList (显示行列表)           │  │
│  │   └─ ConsoleDisplayLine[]             │  │
│  │       └─ ConsoleButtonString[]        │  │
│  │           └─ AConsoleDisplayPart[]    │  │
│  └───────────────────────────────────────┘  │
└────────────────────┬────────────────────────┘
                     │
┌────────────────────▼────────────────────────┐
│        DrawBitmapUtils / DrawTextUtils        │
│        SkiaSharp 底层绘图封装                  │
└─────────────────────────────────────────────┘
```

## 显示元素层次

### ConsoleDisplayLine (显示行)
代表屏幕上的一行文本：
- 包含一组 `ConsoleButtonString`
- 支持对齐方式: LEFT / CENTER / RIGHT
- `DrawTo(SKCanvas, pointY, isBackLog, force, mode)` - 绘制方法

### ConsoleButtonString (按钮字符串)
代表一个可交互的文本区域：
- 包含一组 `AConsoleDisplayPart`
- 记录位置和宽度
- 处理选中/悬停状态的颜色变化

### AConsoleDisplayPart (显示部件 - 抽象基类)
```
AConsoleDisplayPart
├── ConsoleStyledString    # 样式化文本
├── ConsoleImagePart       # 内联图像
├── ConsoleShapePart       # 几何形状
└── ConsoleDivPart         # 分割线
```

## 渲染流程

### 1. 文本输出 → 缓冲
```
脚本 PRINT "Hello" → EmueraConsole.Print("Hello")
    → PrintStringBuffer.Append(str, style)
    → 累积到 builder/m_stringList
```

### 2. 换行 → 生成显示行
```
脚本 PRINTL 或 NewLine() 
    → PrintStringBuffer.FlushToLine()
    → 创建 ConsoleButtonString[] → 创建 ConsoleDisplayLine
    → 添加到 displayLineList
```

### 3. 刷新 → 绘制
```
RefreshStrings(true) 或定时器触发
    → InvalidateSurface() (请求重绘)
    → OnPaintSurface(SKCanvas)
        ├── 绘制背景色
        ├── 绘制 CBG 背景图层 (按 z-depth)
        └── 遍历可见的 displayLineList
            └── 每行 DrawTo(canvas, y, ...)
                └── 每个按钮 DrawTo(...)
                    └── DrawTextUtils 绘制文字
```

## 文字样式系统

### StringStyle 结构
```csharp
struct StringStyle
{
    Color Color;          // 文字颜色
    bool ColorChanged;    // 是否自定义颜色
    FontStyle FontStyle;  // 粗体/斜体/删除线/下划线
    string Fontname;      // 字体名称
}
```

### 样式控制指令
- `SETCOLOR r,g,b` - 设置文字颜色
- `SETBGCOLOR r,g,b` - 设置背景颜色
- `FONTSTYLE n` - 设置字体样式 (1=粗体, 2=斜体, 4=删除线, 8=下划线)
- `SETFONT "name"` - 设置字体
- `ALIGNMENT LEFT/CENTER/RIGHT` - 设置对齐

## CBG 背景图层系统

CBG (Client BackGround) 允许在文本下方显示图像层：

```csharp
class ClientBackGroundImage
{
    ASprite Img;         // 正常状态图像
    ASprite ImgB;        // 按钮选中状态图像
    int x, y;            // 位置
    int zdepth;          // 深度 (越大越靠前, 0=文字层保留)
    bool isButton;       // 是否可点击
    int buttonValue;     // 按钮值
}
```

操作方法：
- `CBG_SetImage()` - 设置背景图层
- `CBG_SetButtonImage()` - 设置可点击图层
- `CBG_SetButtonMap()` - 设置按钮映射图
- `CBG_Clear()` / `CBG_ClearRange()` - 清除图层

## 滚动与显示管理

- `displayLineList` - 完整的显示行历史
- 当前视图窗口由滚动位置决定
- 支持回看历史日志（BackLog 模式，文字显示为历史色）
- `verticalScrollBarUpdate()` - 更新滚动条状态

## 刷新机制

- **主定时器** (`timer`, 10ms) - 处理动画和自动滚动
- **重绘定时器** (`redrawTimer`, 10ms) - 动画用重绘
- **FPS 控制** - 通过 `msPerFrame` 限制帧率
- `REDRAW 0/1` 指令 - 控制是否实时刷新

## 文字测量

`StringMeasure` 封装 SkiaSharp 的文字测量：
- 根据字体和大小计算文字像素宽度
- 用于自动换行和对齐计算
- `Config.WindowX` - 可用窗口宽度（像素）
- `Config.LineHeight` - 行高（像素）

## 运行时缩放

`MainWindow.ApplyRuntimeDisplayScale(fontScale)`:
1. 更新 `Config.FontScale`
2. 重新计算 `ScaledWindowX` (窗口逻辑宽度)
3. 重建布局 (`InitGameView`)
4. 回流所有显示行 (`ReflowDisplayLinesForCurrentScale`)
5. 刷新画布
