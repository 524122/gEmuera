# 外部内容安全限制与错误契约

本章是存档、游戏包、CSV、HTML、图片、音频、插件和并发任务的统一限制。旧表中的 512 MiB 单图、1 GiB PCM/解压和一亿数组元素不能作为 Android 默认值；限制采用设备档位、会话累计预算和原子 reserve，单对象上限只是其中一层。

## 内存预算器

`MemoryBudget` 属于 GameSession，分别计 compressed source、CPU pixels、temporary decode、VM arrays、save/decompress、audio PCM、Godot native estimate 和 GPU estimate。所有大分配执行 `TryReserve(category,bytes,operationId)`；成功后持 reservation token，失败则驱逐、排队或返回 typed fault，`finally`/owner dispose 释放。并发任务预留计入总量，不能让多个各自合法对象同时 OOM。

| 档位 | 选择依据 | 会话可控内存初始上限 | 单图 decode | 单音频 PCM | GZip/存档 candidate | 数组元素总量 |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| Mobile-Low | capability probe/用户强制 | 256 MiB | 64 MiB | 96 MiB | 128 MiB | 8,000,000 |
| Mobile-Mid | 默认 Android 候选 | 512 MiB | 128 MiB | 192 MiB | 256 MiB | 16,000,000 |
| Mobile-High | 实机压力通过 | 768 MiB | 192 MiB | 256 MiB | 384 MiB | 32,000,000 |
| Desktop | 64-bit + 可用内存探测 | 2 GiB | 512 MiB | 1 GiB | 1 GiB | 100,000,000 |

这些是待设备报告调整的安全 ceiling，不是预分配量或性能承诺。动态档位只能降低，提升需要重启候选会话并显式确认；不得仅依据 `GC.GetTotalMemory` 选择档位。

## 默认限制

| 资源 | 默认硬上限 | 拒绝时错误 | 清理行为 |
| --- | ---: | --- | --- |
| 导入递归深度 | 32 层 | `ImportDepthExceeded` | 取消候选导入、关闭枚举句柄 |
| 文件总数 | 20,000 | `ImportFileCountExceeded` | 删除未提交的 `user://imports/<generation>.tmp` |
| 单文件原始大小 | 256 MiB | `FileTooLarge` | 不进入解码器 |
| 包总原始大小 | 4 GiB | `ImportSourceBudgetExceeded` | 回滚候选目录 |
| 总展开/复制量 | 8 GiB | `ExpandedBudgetExceeded` | 停止流式复制并清临时文件 |
| 路径长度 | 1024 UTF-16 code units | `PathTooLong` | 拒绝条目 |
| 单路径段 | 255 code units | `PathSegmentTooLong` | 拒绝条目 |
| CSV 单行 | 1 MiB | `CsvLineTooLong` | 中止当前 CSV；候选包失败 |
| CSV 字段数 | 64 | `CsvFieldCountExceeded` | 中止当前 CSV |
| CSV 单字段 | 256 KiB | `CsvFieldTooLong` | 中止当前 CSV |
| HTML 输入 | 4 MiB/次 | `MarkupTooLarge` | 不修改 Display 历史 |
| HTML 嵌套深度 | 128 | `MarkupDepthExceeded` | 丢弃当前输出事务 |
| HTML 标签/属性数 | 100,000/输入、64/标签 | `MarkupComplexityExceeded` | 丢弃当前输出事务 |
| 普通字符串 | 16 MiB | `StringTooLarge` | 存档/脚本候选失败 |
| 资源引用链 | 64 | `ResourceCycleOrDepth` | 返回占位符并记录一次去重错误 |
| 图片宽/高 | 8192 px | `ImageDimensionExceeded` | 解码前拒绝；兼容模式另行记录 |
| 图片像素 | 67,108,864 | `ImagePixelBudgetExceeded` | 解码前拒绝 |
| 动画帧 | 4096/动画 | `AnimationFrameCountExceeded` | 丢弃动画候选 |
| 单资源解码内存 | 见设备档位 | `DecodeBudgetExceeded` | 取消解码并释放 reservation |
| 音频时长 | 6 小时 | `AudioDurationExceeded` | 不创建 AudioStream |
| 音频解码 PCM | 见设备档位 | `AudioDecodeBudgetExceeded` | 取消并释放 decoder/reservation |
| 存档文件 | 512 MiB | `SaveFileTooLarge` | 读前拒绝 |
| 存档 key | 64 KiB | `SaveKeyTooLong` | 候选解析失败 |
| 存档单字符串 | 16 MiB | `SaveStringTooLong` | 候选解析失败 |
| 存档数组总元素 | 见设备档位 | `SaveArrayBudgetExceeded` | 分配前拒绝 |
| GZip 展开输出 | 见设备档位，且不超过表中 desktop ceiling | `DecompressedSizeExceeded` | 停止解压并删除临时输出 |
| GZip 压缩比 | 200:1 | `CompressionRatioExceeded` | 停止解压 |

这些上限必须在进入大分配、图片/音频 decoder、GZip copy 和数组构造前检查。多个维度的乘积使用 checked 64 位运算，溢出即 `IntegerOverflow`。

## 外部 DLL 与系统能力

游戏包完全不可信，因此发现 `Plugins/*.dll` 不自动加载。Android/iOS 为 Unsupported；桌面默认 Disabled。受信任桌面模式按 DLL SHA-256 单独授权并提示“代码拥有当前用户/进程完整权限”；publisher 签名只能辅助身份与撤销，不能作为沙箱。内置 shell-open/browser、LLM、文件和网络能力通过 allowlisted PlatformCapability，不直接暴露给任意游戏。未来若要运行不可信插件，只能使用独立进程 broker 和最小 typed protocol。

## 路径规范化算法

所有本地文件入口共用以下算法：

1. 将用户选择的根解析为平台原生 canonical root；根本身必须存在且是目录。
2. 拒绝 NUL、控制字符、绝对子路径、驱动器/UNC 注入和 `.`/`..` 逃逸。
3. 组合 root 与相对路径后调用平台 canonicalization；Windows 统一检查重解析点，Unix 检查符号链接。
4. 用平台适当的大小写比较确认 `candidate == root` 或以 `root + separator` 开头；字符串前缀检查不能替代路径段检查。
5. 打开文件后再次验证实际句柄/最终路径，防止检查与使用之间的链接替换。
6. 逻辑资源 key 与物理路径分离；key 不可直接拼接为文件路径。

拒绝 `http:`、`https:`、`file:`、`data:`、`javascript:` 及自定义协议。HTML/BBCode `meta` 只允许内部结构化命令，例如 `{kind:"button", id:123}`，不接受任意字符串 URL。

## Android content URI

`content://` 不是本地路径，不能调用 canonical path。`IContentUriPort` 只公开：取得持久读权限、打开只读流、查询显示名/大小、复制到 generation 临时目录、验证完成后原子重命名。URI 权限失效返回 `ContentPermissionRevoked`；进程恢复后必须重新验证 persisted permission，不能假定旧 Stream 仍有效。

## iOS security-scoped URL

Bridge 取得 document picker 回调后调用原生插件开始 security-scoped access，流式复制到会话候选目录，并在 `finally` 中停止 access。应用挂起/终止期间未提交的复制由 journal 标识；恢复时只允许重试或删除，不能把半文件作为游戏包。

## 存档路径与 slot

slot 是经过范围校验的整数或受限标识符，不接受路径。文件名由 SaveService 生成，不由脚本直接提供。原版兼容存档根与扩展导出根分离；扩展格式不能被原版加载入口探测。

## 安全模式与兼容模式

如果 XEmuera 对某项只警告而继续（例如超过 8192 的图片），默认安全模式拒绝并在兼容矩阵标记 `IntentionalDifference(Security)`。可选兼容模式也必须受像素、解码内存和平台总预算约束，不能完全取消硬上限。

用户可见模式必须是显式、可审计的三层选择，而不是隐藏配置：

| 模式 | 默认 | 可放宽项 | 永不放宽 | UI/报告 |
| --- | --- | --- | --- | --- |
| Safe | 是 | 无 | 路径越界、整数溢出、会话总预算、未授权代码、bomb 上限 | 首次拒绝显示限制、资源和迁移建议 |
| LegacyCompatibility | 按游戏 hash 显式开启 | 仅有 fixture 的警告型单对象限制，在设备预算内提高 | 上述永不放宽项；移动端 DLL | 显示预计内存、旧/新阈值、风险；记录 plan hash |
| TrustedDesktopPlugin | 否，仅桌面 | 指定 DLL hash 的同进程执行 | 移动/AOT、未授权依赖、hash 变化 | 强提示完整进程权限和需重启撤销 |

兼容选择属于 GameSession，不是全局永久开关；切换游戏、内容 hash 变化、应用/codec 大版本升级时重新确认。拒绝结果必须给出 `CompatibilityMatrix` key、当前/所需上限、设备档位和可逆操作，不能只显示“加载失败”。

### Android 存储迁移期

M0–M2 保持旧 Android 外部存储/目录选择路径不变，只归档行为和权限证据。SAF 在后续阶段以独立 flag/canary 并行加入：先复制到 generation 候选目录并验证，再提交；在游戏导入、资源读取、存档导出、权限撤销、后台恢复和低空间真机报告通过前，不得删除旧可用路径。旧路径可因目标 SDK/平台政策被标为受限或只读，但必须给用户迁移提示、导出/备份入口和失败后的回退方案。

## 恶意语料

测试种子至少包括路径穿越、大小写碰撞、符号链接环、超长 CSV、巨图、并发 decode reservation、音频时长伪造、嵌套 HTML、循环引用、数组溢出、截断/高比率 GZip、未授权/换 hash/依赖劫持插件和 shell-open URL/path。移动测试必须记录峰值 RSS/native/GPU、系统 memory warning 与 OOM 前的拒绝行为。
