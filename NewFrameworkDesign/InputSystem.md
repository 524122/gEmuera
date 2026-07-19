# 输入、等待、消息跳过与 UPCHECK

## XEmuera InputRequest 基线

`Emuera/GameProc/InputRequest.cs` 定义：

| InputType | 值 | 是否需值 | 目标 UI |
| --- | ---: | --- | --- |
| EnterKey | 1 | 否 | 点击/确认继续 |
| AnyKey | 2 | 否 | 任意键/触摸 |
| IntValue | 3 | 是 | 整数编辑框/数字按钮 |
| StrValue | 4 | 是 | 文本编辑框 |
| Void | 5 | 否 | 只等待/可被 skip 规则消解 |
| AnyValue | 6 | 是 | 数字或字符串 |
| IntButton | 7 | 是 | 整数按钮 |
| StrButton | 8 | 是 | 字符串按钮 |
| PrimitiveMouseKey | 11 | 是 | 原始鼠标/键码语义，需差分 |

请求含递增 ID、OneInput、StopMesskip、IsSystemInput、MouseInput、默认整数/字符串、Timelimit、DisplayTime、TimeUpMes。旧 gEmuera 还增加 `NoFocus`，并注册 TINPUTNF/TINPUTSNF/TONEINPUTNF/TONEINPUTSNF。目标 DTO 额外携带 NoFocus、session generation 和 completion serial。

## gEmuera 输入扩展

- NF 变体复用相同 timed-input 状态机，仅 `NoFocus=true`；不得为了“无需焦点”跳过超时、默认值或唯一 completion。
- `HOTKEY_STATE_INIT/HOTKEY_STATE` 及硬件键盘热键属于 Snake/v24 profile；初始化顺序、数组范围、开关和错误通过 GE/SN fixture 固定。
- Android `VirtualCursor` 是 touch adapter：单指相对拖动光标，短按左键、长按右键、显式中键；双指缩放优先于光标手势。
- 命中结果必须同时更新 ERB `MOUSEBUTTON()` 读取通道和 Canvas/Control hover 高亮通道；两者不得各自 hit-test 后漂移。
- eraFL `INPUTS ,1` 省略默认参数、空白右键 `RESULT:1==2` 与空字符串/`-1` 语义进入解析+输入联合 fixture。

## 请求状态机

```text
Created → Presented → Pending
Pending --valid submit CAS--> Completed → Queued → consumed by next VM Step
Pending --timeout CAS-------> TimedOut  → Queued
Pending --cancel CAS--------> Cancelled → Queued
Pending --session dispose---> Abandoned
Completed/TimedOut/Cancelled --any later result--> IgnoredDuplicate
generation mismatch -----------------------------> IgnoredStale
```

完成使用原子 compare-exchange；超时与用户点击同一瞬间只能有一个获胜。UI 在发出 signal 后立即禁用相应提交控件，但正确性不依赖 UI 禁用。

## Godot 输入传播

应用级调试快捷键可在 `_Input`；Control 在 `_GuiInput` 消费编辑、按钮、滚动并 `AcceptEvent`；未被 UI 消费的语义操作才进入 `_UnhandledInput`。不在 `_Process` 轮询一次性操作，不硬编码键；所有桌面键通过 InputMap，位置相关快捷键使用 physical_keycode 以支持键盘布局。

InputPanel 是无业务逻辑投影组件：编排器向下调用 `Present(InputPresentation)`，面板向上发 `submitted(requestId,generation,kind,value)` 或 `cancelled`。它不知道 VariableStore 或 VM。

## 触摸与手势

- 只以 `InputEventScreenTouch`/`InputEventScreenDrag` 追踪真实多点触摸；鼠标仅作为桌面模拟路径。
- 每个 touch index 保存 press position/time/target。释放时仍命中同一交互、累计移动未超过阈值且未参与多指手势，才确认按钮。
- 拖动阈值以 DPI/内容缩放换算，初始建议 8–12 dp；不能写死物理像素。
- 多指进入时取消单击候选；惯性滚动期间 suppress button activation。
- 触摸目标至少 48dp（iOS 44pt）；透明容器 `mouse_filter` 不得吞掉下层按钮。
- 应用失焦清空按下/拖动状态，防止缺失 release 造成卡键。

## 消息跳过

消息跳过是独立状态，不与 UPCHECK 混淆。`StopMesskip` 请求暂停自动跳过；按钮/输入请求建立时冻结相应 skip policy，完成后由 VM 恢复。一次继续 intent 只完成当前 request，不可穿透多个等待。高速跳过仍按 effect sequence 保持输出和事件顺序。

## 超时

Timelimit 的单位和边界从输入指令 fixture 确认，Core 使用单调时钟。UI 显示倒计时只是投影，不能决定超时；VM/InputCoordinator 的 deadline 判定才是权威。暂停/后台时按兼容矩阵决定继续计时或冻结，未差分前标 Pending。

## UPCHECK 精确语义

`VariableEvaluator.UpdateInUpcheck(window, skipPrint)`：

1. 读取全局 `UP`、`DOWN` 与 TARGET 角色 `PALAM`。
2. TARGET 无效时跳到清理；仍清零全部 UP/DOWN。
3. 对三个数组最短长度迭代；UP 和 DOWN 都小于等于 0 的项忽略。
4. `unchecked` 执行 `PALAM[i] += UP[i] - DOWN[i]`。
5. `skipPrint=false` 时输出 `PALAMNAME old+up-down=new` 并换行；true 只抑制文本，不抑制状态变化。
6. 最后清零 UP 与 DOWN 全数组。

`CUpdateInUpcheck(window,target,skipPrint)` 使用目标角色自身 `CUP`/`CDOWN`；无效 target 直接返回，因此不会清理某个有效角色数组。其余更新/输出/清零规则相同。PREVCOM 不由此方法更新。

## 无障碍

输入对话框有明确 label、焦点顺序、确认/取消语义和错误提示；键盘、控制器和屏幕阅读器能访问。IME composition 不被当作最终提交；回车行为根据 OneInput/多行约束。RTL 只改变布局方向，不改变按钮值或请求 ID。

## 测试矩阵

| 场景 | 预期 |
| --- | --- |
| submit 与 timeout 同 tick | 只有一个 completion，按单调顺序规则决定 |
| 双击/双触 | 第二次 IgnoredDuplicate |
| A 会话请求，切到 B 后 A 回调 | IgnoredStale，不影响 B |
| 拖动经过按钮后释放 | 不提交 |
| 两指滚动/缩放 | 不触发单击 |
| NF timed input | 不抢焦点；超时/默认值/结果与对应非 NF 变体一致 |
| 虚拟光标 L/R/M | ERB mouse code 与视觉 hover 命中一致 |
| eraFL 空白右键 | 正确推进等待并保持 RESULTS/RESULT 数组契约 |
| HOTKEY_STATE 未初始化/越界 | 与旧 gEmuera 错误和状态保持一致 |
| IME composing + Enter | 只在 composition commit 后按规则提交 |
| UPCHECK skipPrint | 变量改变、无 DisplayEffect、UP/DOWN 清零 |
| CUPCHECK 无效角色 | 无更新、无清零其他角色、无输出 |

差分报告记录 request 创建、展示、提交、入队、VM 消费的 sequence/timestamp，不只断言最终 RESULT。
