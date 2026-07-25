# 线程模型与跨线程通信

## 双线程架构

gEmuera-future 采用严格的双线程模型：

```
┌─────────────────────────────────┐    ┌─────────────────────────────────┐
│        主线程 (Godot)            │    │       脚本线程 (EmueraThread)     │
│                                 │    │                                 │
│  _Process() 每帧回调             │    │  Program.Main() → Process       │
│  ├─ 消费 GPU 工作队列            │    │  ├─ 加载 CSV/ERB                 │
│  ├─ 消费文本渲染队列             │    │  ├─ runScriptProc() 循环         │
│  ├─ 检查 dirty 标志             │    │  │   ├─ 执行 PRINT/IF/CALL       │
│  ├─ 拉取新 ConsoleDisplayLine   │    │  │   ├─ 遇到 INPUT → 等待        │
│  ├─ Canvas/Control 渲染         │    │  │   └─ inputEvent.Wait()        │
│  ├─ 处理输入事件                 │    │  └─ 循环直到游戏结束             │
│  └─ 纹理异步上传                 │    │                                 │
│                                 │    │                                 │
│  UI 响应 (无阻塞)               │    │  纯逻辑执行 (无 Godot API 调用)   │
└────────────────┬────────────────┘    └────────────────┬────────────────┘
                 │                                      │
                 └──────────── 通信通道 ────────────────┘
```

## 通信通道

### 1. 输入通道 (主线程 → 脚本线程)

```csharp
// 主线程侧
EmueraThread.instance.Input(text, from_button, skip, mouseVk);
// 内部: input = text; inputEvent.Set();

// 脚本线程侧
inputEvent.Wait();  // 阻塞等待
string result = input;
inputEvent.Reset();
```

**同步机制**: `ManualResetEventSlim`  
**方向**: 单向 (主 → 脚本)  
**频率**: 低频 (仅用户操作时)

### 2. 显示通道 (脚本线程 → 主线程)

```csharp
// 脚本线程侧
EmueraConsole.Print() → displayLineList.Add()
MainWindow.Refresh()  → dirty_ = true, refreshRequestGeneration++

// 主线程侧 (_Process)
if (dirty_) {
    拉取 displayLineList 新增行
    添加到 Canvas/Control 渲染
    dirty_ = false
}
```

**同步机制**: volatile dirty 标志 + generation 计数器  
**方向**: 单向 (脚本 → 主)  
**频率**: 高频 (每行输出/刷新)

### 3. GPU 工作通道 (脚本线程 → 主线程)

```csharp
// 脚本线程侧 (图片处理需要 GPU)
var item = EmueraMain.GpuSubmitColorMatrix(image, region, colorMatrix);
item.Completed.Wait();  // 阻塞等 GPU 完成
var result = item.ResultImage;

// 主线程侧 (_Process, GpuRenderComponent)
while (gpuQueue.TryDequeue(out var item)) {
    // 在 SubViewport 执行着色器
    item.ResultImage = processedImage;
    item.Completed.Set();  // 唤醒脚本线程
}
```

**同步机制**: `ConcurrentQueue` + `ManualResetEventSlim`  
**方向**: 双向 (提交 → 结果)  
**频率**: 中频 (图片处理时)

### 4. 文本渲染通道 (脚本线程 → 主线程)

```csharp
// 脚本线程侧 (需要字体测量/渲染)
var item = EmueraMain.SubmitTextRender(text, fontName, fontSize, ...);
item.Completed.Wait();
var renderedImage = item.ResultImage;

// 主线程侧 (_Process, TextRenderComponent)
while (textRenderQueue.TryDequeue(out var item)) {
    // 使用 Godot Font 渲染文字到 Image
    item.ResultImage = rendered;
    item.Completed.Set();
}
```

**同步机制**: 同 GPU 通道  
**方向**: 双向  
**频率**: 低频 (特殊文字纹理)

## 线程安全规则

### 脚本线程可以做的
- 读写 `VariableData` (完全独占)
- 修改 `displayLineList` (添加行)
- 调用 `MainWindow.Refresh()` (原子操作)
- 提交 GPU/文本队列 (ConcurrentQueue)
- 读取 `Config` 静态属性 (初始化后只读)

### 脚本线程不能做的
- 调用任何 Godot API (Node/Control/Texture/...)
- 直接修改 UI 状态
- 直接上传纹理到 GPU

### 主线程可以做的
- 读取 `displayLineList` (拉取模式)
- 所有 Godot API 操作
- 设置 `inputEvent` (唤醒脚本线程)
- 消费 GPU/文本队列

### 主线程不能做的
- 修改 `VariableData` (脚本线程独占)
- 直接调用 `Process` 方法
- 阻塞等待脚本线程完成

## 典型时序图

### 用户点击按钮流程

```
主线程                                    脚本线程
  │                                         │
  │ 用户触摸按钮                              │ (阻塞在 inputEvent.Wait())
  ├─→ HandleContentPointerInput()           │
  ├─→ 确定按钮 input 值                      │
  ├─→ EmueraThread.Input("3", true, ...)    │
  │   └─→ inputEvent.Set() ──────────────→  │ 唤醒
  │                                         ├─→ 读取 input = "3"
  │                                         ├─→ RESULT = 3
  │                                         ├─→ 继续 runScriptProc()
  │                                         ├─→ 执行 PRINT "选择了3"
  │                                         ├─→ 执行 INPUT (下一次等待)
  │                                         ├─→ Refresh() → dirty = true
  │                                         └─→ inputEvent.Wait() (再次阻塞)
  │
  ├─→ _Process() 检测 dirty = true
  ├─→ 拉取新 ConsoleDisplayLine
  ├─→ 渲染 "选择了3" 到 Canvas
  └─→ dirty = false
```

### 图片加载时序

```
主线程                                    脚本线程
  │                                         │
  │                                         ├─→ SPRITELOAD 指令
  │                                         ├─→ 后台解码图片为 Image
  │                                         ├─→ 需要 ColorMatrix GPU 处理
  │                                         ├─→ GpuSubmitColorMatrix()
  │                                         └─→ item.Completed.Wait() (阻塞)
  │
  ├─→ _Process() → GpuRenderComponent
  ├─→ gpuQueue.TryDequeue() 取出 item
  ├─→ SubViewport 执行着色器
  ├─→ 读回结果到 item.ResultImage
  ├─→ item.Completed.Set() ──────────────→  │ 唤醒
  │                                         ├─→ 使用处理后的图片
  │                                         └─→ 继续执行
```

## 异步纹理模型

对于不需要阻塞脚本线程的纹理（如 CBG 背景、HTML 内联图片）：

1. 脚本线程在 `GraphicsImage` / `AppContents` 中记录加载请求
2. 主线程的 `SpriteManager` 检测待处理项
3. 后台 `WorkerThreadPool` 解码图片
4. 完成后主线程在下一帧上传为 `ImageTexture`
5. `EmueraContent` 检测纹理就绪，更新对应行的渲染

这种模式下脚本线程不阻塞，但显示可能延后 1-2 帧出现图片。

## GpuReady 守卫

```csharp
public static bool GpuReady { get; private set; } = false;
```

在 `EmueraMain._Process()` 首次被调用前，GPU 管线未就绪。此时脚本线程提交的 GPU 工作会在队列中等待。`GpuReady` 标志在首帧 `_Process()` 后置为 true，确保 SubViewport 渲染管线已初始化。
