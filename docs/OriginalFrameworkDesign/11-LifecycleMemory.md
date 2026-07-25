# Godot 生命周期与内存管理

## Godot 节点生命周期

### 节点回调顺序

```
_EnterTree()          // 节点加入场景树
  ↓
_Ready()              // 所有子节点已就绪 (只调一次)
  ↓
_Process(delta)       // 每帧调用 (可变帧率)
_PhysicsProcess(delta) // 固定频率 (不使用)
  ↓
_Notification(what)   // 系统通知 (后台/恢复/关闭)
  ↓
_ExitTree()           // 节点离开场景树
  ↓
Dispose() / Free()    // C# IDisposable / 释放
```

### gEmuera 核心节点树

```
main.tscn (SceneTree Root)
└── EmueraMain (Node) — 应用入口、GPU 队列处理
    ├── EmueraLifecycleComponent — Android 生命周期
    ├── EmueraStartupComponent — 初始化流程
    ├── EmueraGpuRenderComponent — GPU ColorMatrix
    ├── EmueraTextRenderComponent — 文字纹理渲染
    └── EmueraContent (Control) — 控制台 UI 表面
        ├── ScrollContainer — 滚动视口
        │   └── scaledContentRoot (Control) — 缩放根
        │       ├── ConsoleRenderSurface — Canvas 绘制
        │       ├── lineContainer — Control 行容器
        │       └── htmlIslandContainer — HTML island
        ├── cbgContainer — CBG 背景图层
        ├── Inputpad — 输入面板
        ├── QuickButtons — 快捷按钮
        ├── Scalepad — 缩放控制
        ├── VirtualCursor — 虚拟光标
        └── OptionWindow — 选项窗口
```

## 启动流程

### first_window.tscn → main.tscn

```
App 启动
  ↓
first_window.tscn (FirstWindow.cs)
  ├── 选择游戏目录
  ├── 验证 csv/erb 目录存在
  └── 切换场景到 main.tscn
  ↓
main.tscn
  └── EmueraMain._Ready()
      ├── EmueraStartupComponent.Initialize()
      │   ├── Program.Init() — 配置加载、目录验证
      │   ├── EmueraContent.Initialize() — UI 初始化
      │   └── EmueraThread.Start() — 后台线程启动
      └── 进入主循环
```

### EmueraThread 启动

```csharp
void Start(bool debug, bool use_coroutine)
{
    thread = new Thread(Work);
    thread.Start();
}

void Work()
{
    Program.Main(new string[0]);
    // 内部:
    //   1. 加载 CSV → GameBase, ConstantData
    //   2. 创建 VariableData
    //   3. 加载 ERH → HeaderFileLoader
    //   4. 加载 ERB → ErbLoader
    //   5. callEmueraProgram("") → 系统状态机
    //   6. runScriptProc() → 脚本执行循环
}
```

## 内存所有权模型

### 谁拥有什么

```
EmueraMain (Node, 应用顶层)
├── owns: GPU 队列、文字渲染队列
├── lifetime: 整个应用生命周期

EmueraThread (单例, 非 Node)
├── owns: 后台执行线程、ManualResetEventSlim
├── lifetime: 游戏运行期间

GlobalStatic (静态类)
├── holds refs to: Console, Process, VariableData, ...
├── lifetime: 游戏运行期间 (手动清理)

EmueraContent (Control)
├── owns: 所有 UI 节点、纹理 Pin、Canvas 状态
├── lifetime: main.tscn 存在期间

VariableData (普通 C# 对象)
├── owns: 所有变量数组、角色数据
├── lifetime: Process 存活期间
├── memory: 可达数十 MB

SpriteManager (静态管理器)
├── owns: 纹理缓存、引用计数
├── lifetime: 应用生命周期
```

## 内存管理策略

### C# GC 与 Godot 对象

gEmuera 中两类对象共存：

| 类型 | 内存管理 | 释放方式 |
|------|---------|---------|
| Godot Node/Resource | Godot 引用计数 | `QueueFree()` / 离开树 |
| 纯 C# 对象 | .NET GC | 无引用时自动回收 |
| C# 中持有 Godot 对象 | 混合 | 必须显式释放 Godot 侧 |

### 常见内存泄漏模式

1. **纹理未释放 Pin**
```csharp
// 错误: 行被移除但纹理 Pin 未释放
lineObjects.Remove(lineNo);  // 泄漏!

// 正确:
if (lineTexturePins.TryGetValue(lineNo, out var pins))
{
    foreach (var pin in pins) pin.Release();
    lineTexturePins.Remove(lineNo);
}
lineObjects.Remove(lineNo);
```

2. **信号连接未断开**
```csharp
// 错误: 节点被 QueueFree 但信号还连着
button.Pressed += OnButtonPressed;
button.QueueFree();  // 可能导致已释放对象回调

// 正确: ExitTree 时断开
public override void _ExitTree()
{
    button.Pressed -= OnButtonPressed;
}
```

3. **静态引用阻止 GC**
```csharp
// GlobalStatic 持有的引用需手动清理
public static void ClearAll()
{
    Console = null;
    Process = null;
    VariableData = null;
    // ...
}
```

### 大块内存的生命周期

| 数据 | 大小 | 分配时机 | 释放时机 |
|------|------|---------|---------|
| ERB 逻辑行 | 10-50MB | 启动加载 | 游戏关闭 |
| 变量数据 | 5-30MB | 启动加载 | 游戏关闭 |
| 纹理缓存 | 10-60MB | 按需加载 | LRU 淘汰 / 场景切换 |
| Canvas 缓冲 | 1-5MB | 首次绘制 | 窗口关闭 |
| 显示行历史 | 2-10MB | 运行时增长 | 超限裁剪 |

## Android 生命周期

### EmueraLifecycleComponent

```csharp
// 处理 Android 特有的后台/恢复事件
public bool HandleNotification(int what)
{
    switch (what)
    {
        case NotificationApplicationPaused:
            SetApplicationPaused(true);     // 进入后台
            break;
        case NotificationApplicationResumed:
            SetApplicationPaused(false);    // 恢复前台
            break;
    }
}
```

### 后台策略

```
进入后台:
├── Engine.MaxFps = 5           // 极低帧率
├── 暂停音频播放
├── 后台线程继续运行 (等待输入时自然阻塞)
└── 减少 GPU 提交

恢复前台:
├── 恢复用户帧率
├── 恢复音频
├── 强制刷新显示
└── 重新应用缩放设置
```

### 低内存警告

```csharp
// NotificationApplicationLowMemory
void OnLowMemory()
{
    // 释放非必要缓存
    SpriteManager.TrimCache();
    consoleFontCache.Clear();
    GC.Collect();
}
```

## 游戏重启 / 场景切换

### 重启流程

```
用户选择重启
  ↓
EmueraThread.End()          // 停止后台线程
  ├── running = false
  ├── inputEvent.Set()      // 唤醒阻塞的线程
  └── thread.Join(2000)     // 等待退出
  ↓
GlobalStatic.ClearAll()     // 清理全局引用
  ↓
uEmuera.Utils.ResourceClear() // 清理桥接资源
  ↓
切换到 first_window.tscn   // 重新选择游戏
  ↓
EmueraThread.Start()        // 重新加载并执行
```

### 注意事项

- `EmueraThread` 是单例，`End()` 后字段状态需完全复位
- `GlobalStatic` 静态字段必须逐一置 null
- SkiaSharp / 图片缓存等本机资源需显式 Dispose
- Godot 节点通过 `QueueFree()` 自动处理子树

## 错误恢复

### 脚本异常

```csharp
try
{
    runScriptProc();
}
catch (EmueraException ex)
{
    // 脚本错误: 显示错误信息，等待用户操作
    console.PrintError(ex.Message, ex.Position);
    // 不终止进程，允许重试/跳过
}
catch (Exception ex)
{
    // 引擎错误: 记录日志，尝试恢复
    DiagnosticLogRouter.LogError(ex);
}
```

### 崩溃保护

- `DiagnosticLogRouter` 持续记录操作日志
- `InputReplayBuffer` 记录最近输入序列
- `SaveLogOperationTrail` 追踪存档操作
- 崩溃后可导出诊断日志辅助定位

## 资源清理检查表

游戏结束时需清理的资源：

- [ ] EmueraThread 停止并 Join
- [ ] 所有纹理 Pin 释放 (line + cbg + htmlIsland)
- [ ] 音频播放器停止
- [ ] GlobalStatic 全部置 null
- [ ] SpriteManager 缓存清空
- [ ] 字体缓存清空
- [ ] Canvas overlay 节点 QueueFree
- [ ] ScrollContainer 子节点清空
- [ ] ManualResetEventSlim Dispose
