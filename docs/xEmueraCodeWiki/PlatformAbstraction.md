# 平台抽象层

## 概述

XEmuera 从桌面版 Emuera (Windows Forms) 移植到移动端，使用 Xamarin.Forms 实现跨平台。平台差异通过接口抽象、自定义渲染器和条件编译处理。

## 平台服务接口

### IPlatformService
```csharp
public interface IPlatformService
{
    void CloseApplication();              // 关闭应用
    void EmueraPageAppearing();           // 游戏页面出现时
    void EmueraPageDisappearing();        // 游戏页面消失时
    string GetStoragePath();              // 获取存储路径
    void LockScreenOrientation();         // 锁定屏幕方向
    bool NeedManageFilesPermissions();    // 是否需要文件管理权限
    bool NeedRebootIfLanguageChanged();   // 切换语言是否需要重启
    bool NeedStoragePermissions();        // 是否需要存储权限
    void RequestManageFilesPermissions(); // 请求文件管理权限
    void UnlockScreenOrientation();       // 解锁屏幕方向
}
```

通过 `GameUtils.PlatformService` 全局访问。

## GameUtils 静态工具类

全局状态和工具方法的集合：
```csharp
public static class GameUtils
{
    static MainPage MainPage;              // 主页面引用
    static RelativeLayout MainLayout;      // 主布局
    static SKCanvasView MainPicBox;        // 画布控件
    static IPlatformService PlatformService; // 平台服务
    static bool IsEmueraPage;             // 是否在游戏页面
    
    static void Load();                    // 加载初始化
}
```

## DisplayUtils 显示工具

处理屏幕密度和像素转换：
```csharp
public static class DisplayUtils
{
    static float ScreenDensity;     // 屏幕像素密度
    static int PicBoxWidth;         // 画布宽度 (像素)
    static int PicBoxHeight;        // 画布高度 (像素)
}
```

## 从 Windows Forms 的迁移

### 替换对照表

| 原版 (WinForms) | 移植版 (Xamarin) | 说明 |
|-----------------|-----------------|------|
| `System.Windows.Forms.Form` | `Xamarin.Forms.ContentPage` | 窗口→页面 |
| `PictureBox` + GDI+ | `SKCanvasView` + SkiaSharp | 画布控件 |
| `Graphics.DrawString()` | `SKCanvas.DrawText()` | 文字绘制 |
| `Bitmap` / `Image` | `SKBitmap` / `SKImage` | 图像 |
| `MessageBox.Show()` | 自定义 `MessageBox` 类 | 消息对话框 |
| `Timer` (WinForms) | `System.Timers.Timer` | 定时器 |
| `Application.Run()` | `Navigation.PushAsync()` | 窗口显示 |
| `Clipboard` | 平台相关实现 | 剪贴板 |
| `Form.ClientSize` | `ContentPage + Layout` | 窗口大小 |
| `MouseEventArgs` | `Touch` 事件 | 输入处理 |
| `KeyEventArgs` | 软键盘 + 虚拟控制器 | 按键输入 |

### 保留的 System.Drawing 类型
为减少改动量，项目保留了 `System.Drawing` 中的值类型：
- `Color` - 颜色
- `Point` / `PointF` - 点
- `Rectangle` / `RectangleF` - 矩形
- `Size` - 大小
- `FontStyle` - 字体样式枚举

这些类型仅作为数据载体，不参与实际绘制。

## 自定义渲染器

### Android (`XEmuera.Android/Renderer/`)
- 处理 Android 特有的控件行为
- 软键盘管理
- 触摸事件处理

### iOS (`XEmuera.iOS/Renderer/`)
- iOS 特有控件适配
- Safe Area 处理
- iOS 输入法处理

## 输入适配

移动端没有物理键盘，XEmuera 提供：

1. **文本输入框** (`Entry`) - 用于 INPUT/INPUTS 指令
2. **虚拟控制器** - 模拟方向键和确认键
3. **触摸点击** - 直接点击按钮选择
4. **长按** - 模拟特殊操作
5. **缩放手势** - 运行时调整显示缩放

## 文件系统抽象

### Sys 类 (静态)
封装文件系统操作：
```
Sys.ExeDir     // 游戏根目录
Sys.ExeName    // 应用名称
Sys.Init()     // 初始化路径
```

### 路径处理
- 使用 `Path.DirectorySeparatorChar` 代替硬编码 `\\`
- 同时尝试大小写目录名 (csv/CSV, erb/ERB)
- 通过 `IPlatformService.GetStoragePath()` 获取平台存储路径

## 多语言支持

### Lang 系统
`EvilMask.Emuera.Lang` 命名空间提供 UI 多语言：
- 基于属性标记 (`[Managed]`, `[Translate]`) 的翻译系统
- `TranslatableString` - 可翻译字符串
- 支持运行时切换语言
- 默认语言为日文

### 资源文件
`Resources/Lang/` 目录下存放语言资源文件。

## 兼容性处理

### MainWindow 双文件模式
- `MainWindow.cs` - 原版 WinForms 兼容代码（大部分已注释）
- `MainWindow.xaml.cs` - Xamarin.Forms 实际实现

### 条件编译
- `#if DEBUG` - 调试模式特有代码
- 通过注释保留原版代码供参考

### Forms 命名空间桥接
`XEmuera/Forms/` 目录下有 WinForms 常用类的替代实现：
- `MessageBox` - 消息对话框
- 其他 Forms 辅助类
