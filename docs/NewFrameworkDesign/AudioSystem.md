# 音频指令、Bridge、混音与取消

## 分层

Core 只产生 `AudioEffectDto`：PlayBgm、StopBgm、PlaySound、StopSound、SetBgmVolume、SetSoundVolume。AudioStreamPlayer、AudioServer、bus、Tween 与 decoder 只在 Bridge/View。AudioBridge 不知道 ERB 指令名，只知道 typed command。

## XEmuera 事实

`SETSOUNDVOLUME_Instruction`/`SETBGMVOLUME_Instruction` 读取整数表达式并设置所有 sound players 或 bgm player 的 `Volume`。PLAYBGM/STOPBGM 的资源查找、循环、错误和覆盖行为必须由指令实现/fixture 补齐。Godot 的 dB 与原版 player Volume 不同，不能直接把整数当 dB。

## bus

```text
Master (limiter only)
├─ BGM
├─ SFX
└─ UI
```

用户偏好 slider 0..1 经 `linear_to_db` 映射；脚本逻辑音量先通过兼容函数转线性增益，再与用户 bus gain 相乘。最终值：`effectiveLinear = scriptGain * userGain * duckGain`，再转 dB。scriptGain 函数由差分音量 fixture 决定。

## 兼容模式与增强模式

默认 `audio.compat_mode=on`：PLAYBGM 的替换、停止、循环、通道和资源缺失行为严格按旧 gEmuera/上游 fixture；不自动 crossfade，不因 pool 压力提前停止脚本指定 voice。脚本命令何时返回、缺资源是 0/警告/异常，由 [ExecutionContract](ExecutionContract.md) 逐项记录。

Crossfade、ducking 和 voice stealing 都是可听见的 `Extension`，默认关闭。启用时写入 capability report；游戏脚本的显式通道优先于 app 策略，且可随时切回兼容模式。

## 增强模式 BGM 状态机

```text
Stopped
 → Loading(trackGeneration)
 → PlayingA/PlayingB
 → Crossfading(from,to,trackGeneration)
 → Stopping
 → Stopped
```

每次 Play/Stop 递增 track generation，并携带 session generation。加载、Tween finished、player finished 的 continuation 在操作前比较两者。旧 fade-out 完成后只停止它记录的 old player，绝不根据“当前 player”字段停止新曲。

`PlayBgmAsync`、`StopBgmAsync`、`CrossfadeAsync` 返回 Task<AudioResult>，接收 CancellationToken，不使用 async void。Crossfade 仅由增强 profile 请求；兼容命令不能被 app preference 偷换成 overlap。

## 双播放器 crossfade

BgmA/B 预创建并路由 BGM bus。新曲完成 decode 后放入 inactive player，从静音线性增益开始播放；一个绑定 AudioBridge 生命周期的 Tween 并行淡出旧、淡入新。结束时验证 generation，停止旧并交换 active id。duration 是 App preference，不改变 Core 指令时序。

## SFX voice pool

pool 预创建，避免每个短音效 new/queue_free。pool 满时兼容模式必须按旧实现的通道/失败语义处理；未经 fixture 不做 voice stealing。增强模式才允许按优先级/最旧策略 stealing，并记录被截断的 voice、原因和 profile。

## 资源与安全

ResourceCatalog 给逻辑音频 key；Bridge 从受控 ContentToken 流读取。先探测容器/时长/采样率/声道，并向会话 MemoryBudget 原子 reserve；具体移动/桌面档位见 SecurityLimits。外部 URL 禁止。大音频优先 streaming；decoder 任务携带 generation。

## 会话切换

提交新会话后：阻止旧 command → 取消旧 load/fade → 停止旧 voice/BGM 或按 ADR 短淡出 → 释放旧 stream handle → reset script volume → 应用用户 bus preference。AudioBridge Node 可以保留，但所有播放状态与 generation 清零。

## 生命周期

Tween 存引用并 kill；signal 在 `_ExitTree` 断开；player pool 在 exit stop。应用后台时平台策略可暂停/静音并保存逻辑播放状态；恢复时是否继续位置需设备测试，不能用 Timer 假定精确。

## 测试

- A→B→C 快速换曲，A/B continuation 不停止 C。
- Play 与 Stop 同 tick；取消 loading；decode failure。
- session switch 时旧音频回调迟到。
- 脚本音量边界、负数/超范围行为与 XEmuera 差分。
- SFX pool 满、voice steal、相同音效 burst。
- compat mode A→B 不重叠、不偷 voice；enhanced mode 的差异被明确标记。
- 后台/恢复、设备音频中断。
- managed/native/audio stream/player/tween 数量稳定。
