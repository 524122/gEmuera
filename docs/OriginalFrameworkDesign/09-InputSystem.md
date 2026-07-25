# 输入系统

## 概述

gEmuera-future 的输入系统需要在移动触摸环境中模拟原版 Emuera 的键盘+鼠标操作。系统支持：按钮点击、文本输入、虚拟光标（模拟三键鼠标）、快捷按钮、缩放手势和虚拟键盘。

## 输入架构

```
┌──────────────────────────────────────────────────────────┐
│                Godot InputEvent 系统                       │
│  (Touch/Mouse/Key)                                       │
└────────────────────────────┬─────────────────────────────┘
                             │
┌────────────────────────────▼─────────────────────────────┐
│              EmueraContent (UI 输入路由)                    │
│  ┌──────────────────────────────────────────────────┐    │
│  │ HandleContentTouchGesture (手势判定)               │    │
│  │   ├─ 双指缩放 → Scalepad.ApplyPinch()            │    │
│  │   └─ 单指 → HandleContentPointerInput()          │    │
│  ├──────────────────────────────────────────────────┤    │
│  │ HandleContentPointerInput (指针处理)               │    │
│  │   ├─ VirtualCursor 拦截 (虚拟光标模式)            │    │
│  │   ├─ 按钮命中测试 (点击)                          │    │
│  │   └─ 滚动/惯性 (拖拽)                            │    │
│  └──────────────────────────────────────────────────┘    │
└────────────────────────────┬─────────────────────────────┘
                             │
┌────────────────────────────▼─────────────────────────────┐
│              EmueraThread.Input() (提交到后台线程)         │
│  inputEvent.Set() → 唤醒脚本执行循环                      │
└──────────────────────────────────────────────────────────┘
```

## 输入模式

### 1. 按钮点击 (最常用)

```
用户触摸 → 命中测试(ConsoleButtonString) → 提取 input value
→ EmueraThread.Input(value, fromButton=true, mouseVk=0x01)
→ Console.PressEnterKey(value) → RESULT = value
→ 脚本继续执行
```

### 2. 文本输入 (INPUT/INPUTS 指令)

```
脚本执行 INPUTS → Console 状态 = WaitInput
→ 显示软键盘 / 输入框
→ 用户输入文字 + 确认
→ EmueraThread.Input(text, fromButton=false)
→ RESULTS = text / RESULT = parseInt(text)
```

### 3. 任意键等待 (WAIT/WAITANYKEY)

```
脚本执行 WAIT → Console 状态 = WaitAnyKey
→ 任何触摸/按键事件
→ EmueraThread.Input("", skip=true)
→ 脚本继续
```

## 鼠标按键抽象

移动端通过虚拟光标模拟三键鼠标：

| 虚拟键码 | 含义 | 触发方式 |
|----------|------|---------|
| 0x01 | 左键 | 短按（默认） |
| 0x02 | 右键 | 长按 (≥0.45s) |
| 0x04 | 中键 | 虚拟光标中键按钮 |

### ERB 读取鼠标状态

```erb
; RESULT:1 存储鼠标键码
INPUTS , 1
IF RESULT:1 == 2   ; 右键
    ; 切换状态/菜单
ENDIF
```

### VirtualCursor 实现

```
VirtualCursor (CanvasLayer, Layer=92)
├── 可见光标精灵 (跟随手指移动)
├── 中键按钮 (右上角常驻)
└── 手势判定逻辑
    ├── 单指拖动 → 移动光标 (触摸板模式)
    ├── 短按 (< 0.45s, 位移 < 10px) → 左键
    └── 长按 (≥ 0.45s, 位移 < 10px) → 右键
```

光标移动同步双通道：
- **通道A**: `GenericUtils.SetPointingButton` → ERB `MOUSEBUTTON()` 读取
- **通道B**: `SetCanvasVisualButton` → Canvas 渲染高亮

## 输入子系统

### Inputpad (输入面板)

软键盘辅助面板，提供常用数字/命令快捷输入：
- 数字键 0-9
- 功能键 (确认/取消/跳过)
- 可自定义布局

### QuickButtons (快捷按钮)

ERB 脚本可注册的快捷按钮栏：
```erb
PRINTBUTTON [攻击], 1
PRINTBUTTON [防御], 2
PRINTBUTTON [逃跑], 3
```
自动在底部生成可点击按钮。

### Scalepad (缩放控制)

- 双指捏合 → 缩放画布
- `Fit` 按钮 → 自适应屏幕宽度
- 手动缩放值记忆

### WinInput (传统输入兼容)

```csharp
// _Library/WinInput.cs - 轮询式键盘/鼠标状态
static class WinInput
{
    static bool GetKeyState(int vk);      // 按键状态
    static int GetMouseButton();          // 鼠标按键
    static Point GetMousePosition();      // 鼠标位置
}
```

## 输入等待机制

### ManualResetEventSlim 模式

```csharp
// EmueraThread
ManualResetEventSlim inputEvent;

// 后台线程等待
void WaitForInput()
{
    inputEvent.Reset();
    inputEvent.Wait();     // 阻塞直到有输入
    // 读取 input/skipflag/inputMouseButton
}

// 主线程提交
void Input(string c, bool fromButton, bool skip, int mouseVk)
{
    input = c;
    skipflag = skip;
    inputMouseButton = mouseVk;
    inputEvent.Set();      // 唤醒后台线程
}
```

### 输入类型判定

```csharp
enum InputType
{
    IntValue,     // INPUT - 期望整数
    StrValue,     // INPUTS - 期望字符串
    AnyKey,       // WAIT - 任意键
    EnterKey,     // WAITANYKEY - 确认键
    Void          // 无输入状态
}
```

## 坐标换算

触摸坐标需经过多层变换才能对应到控制台内容：

```
屏幕触摸坐标 (像素)
  → Godot 控件本地坐标
  → ScrollContainer 滚动偏移补正
  → scaledContentRoot 缩放逆变换
  → 控制台逻辑坐标
  → 按钮命中测试 (ConsoleButtonHit)
```

### 命中测试

```csharp
struct ConsoleButtonHit
{
    public Rect2 Bounds;        // 按钮区域
    public string InputValue;   // 提交值
    public int Generation;      // 按钮世代 (防过期)
}
```

## 特殊输入处理

### 空区域右键 (eraFL 兼容)

eraFL 使用 `INPUTS , 1` 读取点击，空白区域右键需要：
- `RESULT:1 == 2` (右键码)
- `RESULTS == ""` (无输入值)
- `RESULT:0 == -1` (未点击按钮)

### 限时输入 (TINPUT)

```erb
TINPUT 5000, 0    ; 5秒超时，默认值0
```

超时后自动提交默认值，脚本继续执行。

### 跳过模式 (Skip)

按住特定键可跳过所有 WAIT/WAITANYKEY，自动推进脚本执行。
