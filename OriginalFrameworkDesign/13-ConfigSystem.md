# 配置系统

## 架构

```
emuera.config (游戏目录下的 INI 文件)
  ↓ 加载
ConfigData (解析和存储)
  ↓ 暴露
Config (静态属性全局访问)
  ↓ 读取
各模块 (渲染、脚本、输入等)
```

## 配置层次

gEmuera 存在两层配置：

### 1. 引擎配置 (emuera.config)

原版 ERA 引擎配置，控制渲染和脚本行为：

```ini
WindowX=800
FontSize=18
LineHeight=19
FontName=MS ゴシック
ForeColor=192,192,192
BackColor=0,0,0
FPS=15
InfiniteLoopAlertTime=5000
```

### 2. Godot 端配置 (config.toml)

gEmuera 特有的 Godot 侧配置：

```toml
[display]
font_scale = 1.0
render_backend = "canvas"

[input]
virtual_cursor_sensitivity = 1.0
long_press_duration = 0.45

[performance]
max_fps = 60
background_fps = 5

[diagnostics]
enable_panel = false
log_level = "warn"
```

## Config 静态类

全局访问点，无需传递引用：

```csharp
// 显示相关
Config.WindowX          // 窗口宽度 (像素)
Config.FontSize         // 字体大小
Config.LineHeight       // 行高
Config.FontName         // 字体名称
Config.ForeColor        // 前景色 (Color)
Config.BackColor        // 背景色 (Color)
Config.FPS              // 帧率
Config.FontScale        // 缩放系数 (移动端)
Config.DrawableWidth    // 实际绘制宽度

// 系统行为
Config.InfiniteLoopAlertTime  // 无限循环检测 (ms)
Config.SaveDataNos            // 存档数量
Config.PrintCPerLine          // PRINTC 每行列数

// 兼容性
Config.ICFunction       // 函数名大小写不敏感
Config.ICVariable       // 变量名大小写不敏感
Config.CompatiRAND      // 兼容旧版随机数
```

## JSONConfig 扩展

gEmuera 新增 JSON 格式配置支持：

```csharp
// JSONConfig.cs / JSONConfigData.cs
// 用于不适合 INI 格式的复杂配置
```

## 运行时缩放

移动端核心功能 — 手势缩放时动态调整配置：

```csharp
// Scalepad.cs 或手势触发
void ApplyScale(float scale)
{
    Config.FontScale = scale;
    // 重算派生值
    Config.ScaledFontSize = (int)(Config.FontSize * scale);
    Config.ScaledLineHeight = (int)(Config.LineHeight * scale);
    Config.ScaledWindowX = (int)(Config.WindowX / scale);
    
    // 通知 UI 重建
    EmueraContent.instance?.OnScaleChanged();
}
```

## 配置加载时序

```
Program.Init()
  ↓
ConfigData.Instance.LoadConfig()
  ├── 读取 emuera.config 文件
  ├── 逐行解析 key=value
  ├── 类型转换 (int, string, Color, bool)
  └── 填充 ConfigItem 表
  ↓
Config 静态字段初始化
  ├── 从 ConfigData 读取值
  └── 计算派生配置
```

## 配置项枚举 (ConfigCode)

每个配置项由枚举标识：

```csharp
enum ConfigCode
{
    WindowX,
    FontSize,
    LineHeight,
    FontName,
    ForeColor,
    BackColor,
    FPS,
    // ...数十个配置项
}
```

## 与 ERB 脚本的交互

部分配置可被脚本运行时修改：

| ERB 命令 | 影响 |
|----------|------|
| `SETCOLOR r,g,b` | 临时修改前景色 |
| `SETBGCOLOR r,g,b` | 临时修改背景色 |
| `FONTSTYLE n` | 临时修改字体样式 |
| `SETFONT "name"` | 临时修改字体 |
| `ALIGNMENT LEFT/CENTER/RIGHT` | 临时修改对齐 |
| `REDRAW 0/1` | 控制刷新模式 |
