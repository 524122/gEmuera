# 构建与运行指南

## 项目类型

XEmuera 是一个 Xamarin.Forms 跨平台移动应用，目标平台为 Android 和 iOS。

## 先决条件

### 开发环境
- **Visual Studio 2019/2022** (Windows) 或 Visual Studio for Mac
- **.NET 工作负载**: 使用 .NET 的移动开发 (Xamarin)
- **Android SDK**: API Level 21+ (Android 5.0+)
- **iOS SDK** (仅 Mac): Xcode 12+

### NuGet 依赖
- Xamarin.Forms
- Xamarin.Essentials
- SkiaSharp
- SkiaSharp.Views.Forms

## 解决方案结构

```
XEmuera.sln
├── XEmuera/XEmuera/              # 共享代码 (.NET Standard)
├── XEmuera/XEmuera.Android/      # Android 平台项目
└── XEmuera/XEmuera.iOS/          # iOS 平台项目
```

## 构建方式

### Visual Studio (GUI)
1. 打开 `XEmuera.sln`
2. 还原 NuGet 包
3. 选择目标平台 (Android/iOS)
4. 选择构建配置 (Debug/Release)
5. 构建解决方案

### 命令行构建 (Android)

项目提供了 PowerShell 脚本：

```powershell
# 构建 Android APK
.\build-android.ps1

# 打包 Android 发布版本
.\package-android.ps1
```

### MSBuild 命令行
```bash
# 还原 NuGet
nuget restore XEmuera.sln

# 构建 Android Debug
msbuild XEmuera/XEmuera.Android/XEmuera.Android.csproj /p:Configuration=Debug

# 构建 Android Release
msbuild XEmuera/XEmuera.Android/XEmuera.Android.csproj /p:Configuration=Release
```

## 运行方式

### Android
1. 连接 Android 设备或启动模拟器
2. 在 Visual Studio 中选择设备
3. F5 运行

### 使用游戏数据
XEmuera 是一个 ERA 游戏引擎模拟器，需要游戏数据才能运行：
1. 安装 APK 到设备
2. 将 ERA 游戏文件放到设备存储的指定目录
3. 游戏文件结构要求：
   ```
   游戏目录/
   ├── csv/        # 必需 - CSV 数据文件
   ├── erb/        # 必需 - ERB 脚本文件
   ├── resources/  # 可选 - 图片资源
   ├── sound/      # 可选 - 音频资源
   └── emuera.config  # 可选 - 配置文件
   ```

## 启动流程

```
1. App() 构造
   ├── Task.Run(() => GameUtils.Load())  // 后台加载
   └── MainPage = new MainPage()        // 设置首页

2. 用户选择游戏目录
   └── 导航到 MainWindow

3. MainWindow.Load()
   ├── Sys.Init()                       // 系统初始化
   ├── Program.Init()                   // 验证目录、加载配置
   └── InitGameView() / InitEmuera()    // 初始化游戏视图

4. EmueraConsole.Initialize()
   ├── 创建 Process
   ├── Process.Initialize()             // 加载所有脚本
   └── callEmueraProgram("")            // 开始执行 @SYSTEM_TITLE
```

## 配置文件

### emuera.config
游戏运行配置，位于游戏根目录：
```ini
;表示コード
WindowX=800
FontSize=18
LineHeight=19
FontName=MS ゴシック
ForeColor=192,192,192
BackColor=0,0,0
;...更多配置
```

主要配置项：
- `WindowX` - 窗口宽度（像素）
- `FontSize` - 字体大小
- `LineHeight` - 行高
- `FontName` - 字体名称
- `ForeColor` / `BackColor` - 前景/背景色
- `FPS` - 帧率限制
- `InfiniteLoopAlertTime` - 无限循环检测时间(ms)
- `SaveDataNos` - 存档槽数量

## 调试

### Debug 模式
在 Debug 配置下编译会启用：
- `Program.debugMode = true`
- 调试命令支持
- `__FILE__`/`__FUNCTION__`/`__LINE__` 伪变量
- 堆栈跟踪列表 (`GlobalStatic.StackList`)

### 常见问题
1. **"csvフォルダが見つかりません"** - 找不到 csv 目录，检查游戏路径
2. **"erbフォルダが見つかりません"** - 找不到 erb 目录
3. **无限循环警告** - 脚本可能存在死循环，调整 `InfiniteLoopAlertTime`
4. **字体显示异常** - 移动设备可能缺少指定字体，会自动 fallback

## 平台特殊处理

### Android
- `MainActivity.cs` - 入口 Activity
- `Renderer/` - 自定义渲染器
- 需要存储权限访问游戏文件
- 支持屏幕方向锁定

### iOS
- `AppDelegate.cs` - 入口委托
- `Main.cs` - 应用入口点
- `Renderer/` - 自定义渲染器
