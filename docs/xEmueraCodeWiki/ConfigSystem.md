# 配置系统

## 概述

XEmuera 的配置系统管理游戏运行参数，从 `emuera.config` 文件加载，并提供运行时动态修改能力。配置项涵盖显示、字体、系统行为、兼容性等方面。

## 核心类

### ConfigData (单例)
配置的加载和存储中心：
```csharp
ConfigData.Instance.LoadConfig();    // 加载配置
ConfigData.Instance.ReLoadConfig();  // 重新加载
ConfigData.Instance.GetConfigValue<T>(ConfigCode code); // 获取原始值
```

### Config (静态类)
配置值的全局访问点，提供已解析的静态属性：
```csharp
// 显示相关
Config.WindowX          // 窗口宽度 (int)
Config.FontSize         // 字体大小 (int)
Config.LineHeight       // 行高 (int)
Config.FontName         // 字体名 (string)
Config.ForeColor        // 前景色 (Color)
Config.BackColor        // 背景色 (Color)
Config.FPS              // 帧率 (int)
Config.FontScale        // 字体缩放 (float)

// 系统行为
Config.AllowMultipleInstances  // 允许多实例
Config.InfiniteLoopAlertTime   // 无限循环检测 (ms)
Config.SaveDataNos             // 存档数量
Config.PrintCPerLine           // PRINTC 每行数量
Config.DrawLineString          // 分割线字符串

// 兼容性
Config.CompatiRAND      // 兼容 eramaker 的 RAND
Config.ICFunction       // 函数名大小写不敏感
Config.ICVariable       // 变量名大小写不敏感

// 字符串比较
Config.SCVariable       // 变量名比较规则 (StringComparison)
```

### ConfigCode (枚举)
每个配置项对应一个枚举值：
```csharp
enum ConfigCode
{
    WindowX, WindowY, FontSize, LineHeight,
    FontName, ForeColor, BackColor, FPS,
    FontScale, PrintCPerLine, SaveDataNos,
    InfiniteLoopAlertTime, AllowMultipleInstances,
    // ... 数十个配置项
}
```

### ConfigItem
单个配置项的定义和存储：
```csharp
class ConfigItem
{
    ConfigCode Code;     // 配置代码
    object Value;        // 当前值
    object DefaultValue; // 默认值
    Type ValueType;      // 值类型
}
```

## 配置文件格式

`emuera.config` 位于游戏根目录，INI 风格：
```ini
;コメント行
WindowX=800
FontSize=18
LineHeight=19
FontName=MS ゴシック
ForeColor=192,192,192
BackColor=0,0,0
描画インタフェース=WINAPI
変数についてaliasが存在する場合にaliasを優先する=YES
```

特点：
- 使用日文或英文配置名（由 `EnglishConfigOutput` 控制保存语言）
- 分号 `;` 开头为注释
- 颜色值为逗号分隔的 R,G,B
- 布尔值为 YES/NO 或 0/1

## 运行时缩放

移动端特有功能，支持运行时调整显示缩放：

```csharp
Config.SetRuntimeFontScale(float scale);
Config.ApplyRuntimeDisplaySettings(ConfigData configData);
Config.RefreshDisplayConfig();
```

缩放影响：
- 字体大小 = 基础 FontSize × FontScale
- 行高 = 基础 LineHeight × FontScale
- 窗口宽度相应调整
- PRINTC 列数自动重算

## 配置对话框

### ConfigDialog
运行时可通过菜单打开配置界面，组织为多个分页：
1. **环境** - 基本运行环境设置
2. **表示(显示)** - 渲染相关
3. **ウィンドウ(窗口)** - 窗口尺寸和位置
4. **フォント(字体)** - 字体和颜色
5. **システム(系统)** - 脚本解析行为
6. **システム2(系统2)** - 存档和高级选项
7. **互換性(兼容性)** - eramaker 兼容选项
8. **解析(调试)** - 警告和分析选项

## 配置与脚本的交互

ERB 脚本可通过特定命令影响运行时行为：
- `PRINTCPERLINE` - 读取 PrintCPerLine 配置
- `SAVENOS` - 读取存档数量配置
- `WINDOW_TITLE` - 读写窗口标题
- `DRAWLINESTR` - 读写分割线字符

## ConfigModel (移动端 UI 绑定)

Xamarin.Forms 配置界面使用 MVVM 模式：
```csharp
class ConfigModel
{
    ConfigItem ConfigItem;
    void UpdateValue();          // 将 UI 修改写回配置
    static ConfigModel Get(ConfigCode code);
}
```
