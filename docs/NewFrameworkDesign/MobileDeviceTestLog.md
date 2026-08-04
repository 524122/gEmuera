# 移动端真机测试记录（gEmuera × eratw）

> 本文档是“完全做好 ERB 方言接口”交付项中的移动端证据入口。
> 记录规则：只填写**实际观察到的数据**；没有设备/日志的指标保持 `待测`，不允许用模拟器数据冒充真机结果。
> 真机测试后由 AI 根据用户汇报补全本文档，并把结果同步到 `tools/dialect-inventory/README.md` 的移动端边界章节。

## 1. 测试对象

| 项 | 值 |
| --- | --- |
| APK | `android/build/gemuera-debug.apk` |
| APK SHA256 | `22738565cfd3fcb918c3e0fdf3bd7238712cf78508741861fd087c5795e78581` |
| APK 大小 | 139.06 MB |
| 架构 | arm64-v8a |
| 签名 | debug keystore；v2/v3 校验通过 |
| Godot | 4.7-stable mono（GL Compatibility renderer） |
| 游戏 | eratw-sub-modding-develop（CSV 228 / ERB 4638 / resources 2860 文件） |
| 游戏路径（Android） | `/storage/emulated/0/emuera/snake/eratw/` |
| 启动 Profile | snake |

## 2. 测试设备

| 项 | 值 |
| --- | --- |
| 设备型号 | 待测 |
| Android 版本 | 待测 |
| SoC / 内存 | 待测 |
| 系统语言 | 待测 |

## 3. 安装与启动

| 检查项 | 结果 |
| --- | --- |
| APK 安装 | 待测 |
| 存储权限授予 | 待测 |
| 启动器识别 eratw | 待测 |
| snake 标签进入游戏 | 待测 |
| 冷启动到主界面耗时 | 待测（ms） |
| 首次进入游戏（ERB/CSV 加载）耗时 | 待测（s） |

## 4. 游戏内运行

| 指标 | 结果 |
| --- | --- |
| 主界面渲染 | 待测 |
| 文本滚动 / 打印 | 待测 |
| 按钮输入响应 | 待测 |
| 资源图片/精灵显示 | 待测 |
| 存档 / 读档 | 待测 |
| 平均帧率 | 待测（fps） |
| 峰值帧率 / 最低帧率 | 待测（fps） |
| 内存占用（PSS） | 待测（MB） |
| 连续游玩 10 分钟内存增长 | 待测（MB） |
| 崩溃 / ANR | 待测 |

## 5. ERB 方言接口专项检查

| 检查项 | 结果 |
| --- | --- |
| v24/VARI、VARS 私有变量解析 | 待测 |
| Snake 专用指令（CBGSETSPRITE/GSETPEN/RESUMETEXTBOX 等） | 待测 |
| MAP / DataTable / XML 函数 | 待测 |
| BITMAP_CACHE_ENABLE 指令可见性 | 待测 |
| 解析报错 / 脚本警告数量 | 待测 |
| 报错样例（如有） | 待测 |

## 6. 已完成的本地验证基线（模拟器/构建，仅供对比）

| 项 | 结果 |
| --- | --- |
| 桌面构建 | 0 警告 / 0 错误 |
| 运行时注册表 smoke | v24 指令 561 / 函数 266；Snake 指令 666 / 函数 347 |
| 反射双上游差异 | v24/Snake 声明与行为矩阵 0 mismatch（声明级） |
| 上游差异报告 | publicKey/visibility/参数返回类型 Passed；runtimeExecution=Partial（完整行为验证需真机 ERB fixture） |
| 模拟器（无窗口 swiftshader） | 冷启动 6.9s；GLES3 uniform 超限导致黑屏（模拟器软渲染限制，非应用缺陷） |
| 模拟器（host GPU） | 应用正常渲染；因用户改用真机未继续模拟器内游戏流程 |

## 7. 结论与后续

- 待真机数据补全后填写结论；`runtimeExecution` 门禁在真机游戏流程证据出现前保持 `Partial`。