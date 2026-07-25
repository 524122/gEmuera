# 移动端性能优化策略

## 性能约束

移动设备（Android 中低端手机）的核心限制：
- CPU: 4-8 核，主频 1.8-2.4GHz，大小核架构
- GPU: 填充率有限，带宽珍贵
- 内存: 总共 4-8GB，可用 1-3GB
- 电池: 持续高负载会触发降频
- 热量: 散热差，10分钟满载后性能腰斩

## 渲染性能

### Canvas 后端 vs Control 后端

gEmuera 采用双后端策略：

| 特性 | Canvas 后端 | Control 后端 |
|------|-------------|-------------|
| 普通文本 | ✅ 批量绘制 | ❌ 每行一个Control |
| 简单图片 | ✅ Canvas overlay | ❌ TextureRect |
| HTML div | ❌ 不支持复杂嵌套 | ✅ 节点树 |
| 节点数 | 极少 | 行数×部件数 |
| Draw Call | 少 (Canvas合批) | 多 |
| 滚动流畅度 | ✅ | ❌ (大量行时卡顿) |

### 当前策略

```csharp
ConsoleRenderBackend consoleRenderBackend = ConsoleRenderBackend.Canvas;
```

- 普通文本行 → Canvas 快路径 (ConsoleRenderSurface)
- 含 div 的行 → 退回 Control 渲染
- 图片 overlay → CanvasImageOverlay / CanvasDivOverlay

### 性能预算

| 指标 | 目标值 | 说明 |
|------|--------|------|
| 帧时间 | < 16ms (60fps) | 但可降到 30fps 省电 |
| 滚动帧时间 | < 8ms | 滚动时只重绘可见区域 |
| 行绘制 | < 0.1ms/行 | Canvas DrawText 批量 |
| 节点数 | < 500 | 超过后 SceneTree 遍历拖慢 |
| Draw Call | < 100 | Mobile 渲染器硬限 |

### 可见区域裁剪

只绘制当前视口内可见的行：

```csharp
// ConsoleRenderSurface._Draw()
float viewTop = scrollContainer.ScrollVertical;
float viewBottom = viewTop + scrollContainer.Size.Y;

foreach (var entry in lineLayoutEntries)
{
    if (entry.Bottom < viewTop) continue;   // 在视口上方
    if (entry.Top > viewBottom) break;      // 在视口下方
    DrawLine(entry);                        // 只绘制可见行
}
```

### 布局缓存

```csharp
List<ConsoleLineLayoutEntry> lineLayoutEntries;  // 预计算每行 Y 坐标
Dictionary<int, int> lineLayoutIndexByLineNo;    // 行号→布局索引
bool lineLayoutDirty = false;                    // 变更时标记脏

// 只在新行添加/删除时重算布局，滚动不触发
```

## 文字测量缓存

文字宽度测量是高频操作 (每行每个片段都要测量)：

```csharp
// FontMeasureCache.cs
class FontMeasureCache
{
    // 字体+大小+样式 → 字符宽度缓存
    Dictionary<FontKey, Dictionary<char, float>> cache;
    
    // 半角/全角快速路径
    float GetCharWidth(char c, FontKey key)
    {
        if (cache[key].TryGetValue(c, out float w))
            return w;
        w = MeasureFromFont(c, key);
        cache[key][c] = w;
        return w;
    }
}
```

## 图片/纹理管理

### 异步加载

```csharp
// SpriteManager - 避免主线程阻塞
HashSet<int> asyncTexturePendingLineNos;  // 等待纹理就绪的行

// 图片未就绪时:
// 1. 不绘制该行 (IsPureImageLine)
// 2. 标记为 pending
// 3. 纹理就绪后刷新该行
```

### 纹理生命周期

```csharp
// 引用计数式纹理持有
Dictionary<int, List<SpriteManager.TextureInfo>> lineTexturePins;  // 行持有的纹理
List<SpriteManager.TextureInfo> cbgTexturePins;                    // CBG持有的纹理

// 行被移除时释放 Pin:
void RemoveLine(int lineNo)
{
    if (lineTexturePins.TryGetValue(lineNo, out var pins))
    {
        foreach (var pin in pins)
            pin.Release();
        lineTexturePins.Remove(lineNo);
    }
}
```

### VRAM 预算

| 资源类型 | 估算大小 | 说明 |
|----------|---------|------|
| CBG背景 | 2-20MB | 全屏背景×深度层 |
| 立绘 | 5-30MB | 高分辨率角色立绘 |
| 图标字体 | 1-5MB | game-icons 等 |
| 文字渲染 | < 5MB | Canvas 文字缓存 |
| **总计** | < 60MB | 中端手机安全阈值 |

## 脚本执行性能

### 后台线程执行

脚本执行不在主线程，不直接影响帧率：

```
主线程 (Godot _Process)     后台线程 (EmueraThread)
├── 接收输入                 ├── 脚本执行循环
├── 处理 UI 事件            ├── 变量计算
├── 渲染当前帧              ├── PRINT → 输出队列
└── 提交 GPU 绘制           └── INPUT → 等待主线程

两线程通过 Queue + ManualResetEventSlim 通信
```

### 无限循环防护

```csharp
// 每 10000 行检测一次执行时间
void checkInfiniteLoop()
{
    if (++lineCount % 10000 == 0)
    {
        if (elapsed > Config.InfiniteLoopAlertTime)
            throw new InfiniteLoopException();
    }
}
```

### PRINT 批量优化

脚本大量输出时，不逐行刷新 UI：

```csharp
// EmueraConsole
bool redrawRequested = false;  // 标记需要刷新
int redrawSuppressCount = 0;   // REDRAW 0 时抑制

// REDRAW 0: 暂停实时刷新，累积输出
// REDRAW 1: 恢复并一次性刷新所有累积行
```

## 内存优化

### 行历史限制

```csharp
// 保留最近 N 行，早期行回收
const int MaxDisplayLines = 5000;  // 可配置

void TrimOldLines()
{
    while (lineNumbers.Count > MaxDisplayLines)
    {
        int oldest = lineNumbers.Min;
        RemoveLine(oldest);
    }
}
```

### GC 压力控制

- 避免在 `_Process` / `_Draw` 中分配
- 字符串拼接使用 `StringBuilder` 池
- 使用 `struct` 代替 `class` 存储布局数据 (`ConsoleLineLayoutEntry`)
- Canvas 命中测试数组复用

### 字体资源缓存

```csharp
readonly Dictionary<string, Font> consoleFontCache;     // 已加载字体
readonly HashSet<string> missingConsoleFonts;           // 缺失字体 (避免重复IO)
```

## 帧率管理

### FrameRateHelper

```csharp
// 根据设备状态动态调整帧率
static class FrameRateHelper
{
    static int CurrentFrameRate;
    
    static void ApplyConfigFps();           // 应用用户配置
    static void SetLowPowerMode();          // 低电量 → 30fps
    static void SetActiveMode();            // 交互中 → 60fps
}
```

### 后台降频

```csharp
// EmueraLifecycleComponent
void SetApplicationPaused(bool paused)
{
    if (paused)
    {
        Engine.MaxFps = 5;                  // 后台极低帧率
        EmueraContent.instance?.SetApplicationPaused(true);
    }
    else
    {
        Engine.MaxFps = FrameRateHelper.CurrentFrameRate;
        FrameRateHelper.ApplyConfigFps();
    }
}
```

## GPU 工作分派

### ColorMatrix GPU 加速

图像色彩矩阵变换（立绘换色等）从后台线程提交到主线程 GPU 执行：

```csharp
// 后台线程提交
var item = EmueraMain.GpuSubmitColorMatrix(src, region, matrix);
item.Completed.Wait();  // 阻塞等待 GPU 完成
var result = item.ResultImage;

// 主线程 _Process 中处理队列
while (gpuQueue.TryDequeue(out var item))
{
    item.ResultImage = ApplyColorMatrixOnGpu(item);
    item.Completed.Set();
}
```

## 性能诊断

### RuntimeDiagnosticsPanel

运行时可视化面板，显示：
- 当前 FPS
- 内存用量
- 节点数
- Draw Call 数
- 脚本执行行/秒

### PerformanceBenchmark

```csharp
// 关键路径耗时统计
static class PerformanceBenchmark
{
    static void BeginSection(string name);
    static void EndSection(string name);
    static void Report();
}
```

## 电量友好策略

1. **无输入时降帧**: 等待用户输入时降到 10fps
2. **后台暂停**: 切到后台时 5fps + 暂停音频
3. **滚动惯性结束后降帧**: 惯性动画结束后恢复低帧率
4. **避免持续 GPU 提交**: 无变化时不调用 `QueueRedraw()`
