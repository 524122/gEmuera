# 平台抽象与适配

## 从 XEmuera 到 gEmuera 的移植策略

### 总体方法

```
原版 Emuera (Windows Forms + GDI+)
  ↓ XEmuera 移植
Xamarin.Forms + SkiaSharp (Android/iOS)
  ↓ gEmuera 重写
Godot 4.7 + C# (全平台)
```

### uEmuera 桥接层

`Scripts/uEmuera/` 提供了原版 API 的 Godot 实现：

| 文件 | 模拟的命名空间/功能 |
|------|-------------------|
| `Drawing.cs` | System.Drawing (Color, Font, Bitmap, Graphics) |
| `Forms.cs` | System.Windows.Forms (MessageBox, Keys, Timer) |
| `Window.cs` | MainWindow, DebugDialog |
| `Application.cs` | Application 生命周期 |
| `Media.cs` | 音频相关 |
| `Properties.cs` | 资源属性 |
| `Utils.cs` | 工具方法 |
| `VisualBasic.cs` | VB.NET StrConv (假名转换) |
| `partial/` | partial class 补充 |

### 关键替换映射

| 原版 (WinForms/SkiaSharp) | gEmuera (Godot) |
|---------------------------|-----------------|
| `SKCanvasView` | `Control` + `_Draw()` / Canvas |
| `SKCanvas.DrawText()` | `DrawString()` on RID / Label |
| `SKBitmap` | `Godot.Image` / `ImageTexture` |
| `System.Timers.Timer` | `_Process()` delta 累加 |
| `Touch events` | `_Input(InputEvent)` |
| `Xamarin Navigation` | `SceneTree.ChangeScene()` |
| `SKPaint` | `Font` + `Color` + `Theme` |
| `FileStream` | `FileAccess` / `System.IO` |

## Godot 平台特性利用

### 渲染方式选择

```
project.godot:
  renderer/rendering_method = "mobile"    // 移动端优化渲染器
  textures/vram_compression/import_etc2_astc = true  // ETC2 压缩
```

### 控件架构

利用 Godot 的 Control 系统处理：
- 自适应布局 (Container)
- 触摸输入 (InputEvent)
- 主题/字体 (Theme)
- 滚动 (ScrollContainer)

### 跨平台文件访问

```csharp
// 游戏数据在外部存储，不用 res://
// Android: /storage/emulated/0/gEmuera/games/xxx/
// Desktop: 工作目录下的游戏文件夹

string gameRoot = Program.ExeDir;  // 统一通过 Program.ExeDir 访问
```

## 输入适配

### 桌面端

```
键盘 → _Input(InputEventKey) → WinInput 轮询
鼠标 → _Input(InputEventMouse) → TryGetPointer → HandleContentPointerInput
```

### 移动端

```
触摸 → _Input(InputEventScreenTouch/Drag)
  ├── 单指点击 → 左键点击
  ├── 单指拖动 → 滚动
  ├── 双指缩放 → Scalepad 缩放
  └── 虚拟光标模式:
      ├── 单指拖动 → 移动光标
      ├── 短按释放 → 左键
      ├── 长按 → 右键
      └── M按钮 → 中键
```

### VirtualCursor (移动端虚拟光标)

```csharp
// Scripts/VirtualCursor.cs
// 触摸板模式: 手指相对移动 → 光标绝对移动
// 手势判定:
//   DragThreshold = 10px    // 超过则为拖动
//   LongPressDuration = 0.45s  // 超过则为右键
```

### QuickButtons (快捷按钮)

移动端常用操作的虚拟按钮面板。

### Inputpad (输入面板)

移动端 INPUT/INPUTS 时弹出的软键盘输入界面。

## 屏幕适配

### 缩放策略

```
project.godot:
  window/stretch/mode = "canvas_items"  // UI 按画布缩放
  window/stretch/aspect = "expand"      // 扩展适配宽屏/窄屏
```

### 安全区处理

```csharp
// ResolutionHelper.cs
// 检测 Android 刘海/挖孔屏安全区
// 调整 UI 边距避免被遮挡
```

### 动态缩放

```csharp
// Scalepad.cs
// 用户手动缩放: 双指捏合 或 按钮控制
// Fit 功能: 自动计算最佳缩放使内容填满屏幕
// AutoFit: 根据内容实际宽度自动适配
```

## 文件系统差异

| 平台 | 游戏数据路径 | 存档路径 | 特殊处理 |
|------|-------------|---------|---------|
| Windows | 任意目录 | 游戏目录/dat/ | 无 |
| Android | 外部存储 | 游戏目录/dat/ | 需存储权限 |
| iOS | App 沙箱 | 游戏目录/dat/ | 文件共享 |

### 编码处理

ERA 游戏历史遗留大量 Shift-JIS 编码文件：

```csharp
// EraStreamReader.cs
// 自动检测编码: UTF-8 BOM > UTF-8 无 BOM > Shift-JIS
// 配置可强制指定 ANSI 编码区域
```

## 多语言支持

### LangManager

```csharp
// Scripts/Emuera/_Library/LangManager.cs
// UI 界面多语言 (不是游戏内容翻译)
// 支持: 日文、中文、英文
```

### MultiLanguage

```csharp
// Scripts/MultiLanguage.cs
// Godot 端 UI 文本多语言
```
