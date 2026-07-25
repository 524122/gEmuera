# 多语言系统 (I18n)

## 概述

XEmuera 支持界面多语言化，通过 `EvilMask.Emuera.Lang` 命名空间下的翻译系统实现。该系统基于属性标记和反射，支持运行时语言切换。

## 核心机制

### TranslatableString

翻译的基本单元，包含默认文本（日文）和可替换的翻译文本：
```csharp
public sealed class TranslatableString
{
    public string Text { get; }           // 当前语言文本
    public string String { get; }         // 同上
}
```

### [Managed] 属性

标记需要翻译管理的类或属性：
```csharp
[Managed]
public sealed class UI
{
    [Managed]
    public sealed class MainWindow { ... }
}
```

### [Translate] 属性

为类提供默认翻译文本：
```csharp
[Translate("ファイル(&F)"), Managed]
public sealed class File
{
    public static string Text { get { return trClass[typeof(File)].Text; } }
}
```

## 翻译分类

### Lang.UI — 用户界面文本
- `Lang.UI.MainWindow` — 主窗口菜单、消息框
- `Lang.UI.ConfigDialog` — 配置对话框所有分页
- `Lang.UI.DebugConfigDialog` — 调试配置

### Lang.Error (trerror) — 错误消息
脚本执行时的错误提示，在代码中通过别名引用：
```csharp
using trerror = EvilMask.Emuera.Lang.Error;
// 使用：
throw new CodeEE(trerror.SetcolorArgOver255.Text);
```

### Lang.SystemLine (trsl) — 系统输出
加载进度、系统状态等显示文本：
```csharp
using trsl = EvilMask.Emuera.Lang.SystemLine;
```

## 语言文件

语言文件位于 `Resources/Lang/` 目录，提供不同语言的翻译覆盖。

## 使用模式

代码中的典型用法：
```csharp
// 菜单项文本
Lang.UI.MainWindow.File.Restart.Text

// 错误消息
trerror.OoRVarArg.Text  // "変数{0}の第{1}引数({2})は範囲外です"

// 消息框
MessageBox.Show(
    Lang.UI.MainWindow.MsgBox.NoCsvFolder.Text,
    Lang.UI.MainWindow.MsgBox.FolderNotFound.Text
);
```

## 移动端 UI 文本

移动端 (`StringsText` 类) 额外维护一套简化文本用于触屏 UI：
```csharp
StringsText.QuitGameConfirm  // 退出确认提示
```
