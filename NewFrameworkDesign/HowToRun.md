# 工具链锁定、构建、导出与平台能力

## 三个环境必须分开

| 环境 | 已确认事实 | 用途 |
| --- | --- | --- |
| 当前设计/上游工作区 `E:/XEmuera-master` | 无 project.godot/目标 Godot App/global.json；XEmuera 主项目不是目标新工程 | 上游源码和 FrameworkDesign |
| 旧可运行 gEmuera `E:/MyCode/GodotCode/gEmuera-future` | 存在 project.godot、csproj、export preset；实际声明 Godot.NET.Sdk 4.7.0、桌面 net8、Android net9、Mobile renderer、Android preset | 迁移起点与 legacy fixture runner |
| 未来新架构 | 已有 `src/Core` 合同切片和 smoke/架构守卫；完整 Contracts/façade/ToolchainLock 尚未完成 | 按 MigrationPlan 在旧项目内增量建立 |

旧工程文件存在不等于目标架构已构建，也不等于 Android 当前通过。本次只读审计未运行 Godot/APK；旧 `project.godot` 的 4.7 是 legacy observation，不自动成为未来锁定版本。目标版本必须结合旧兼容、C# mobile/export 和实机验证裁决。

## ToolchainLock.json

```json
{
  "godot": {"version":"UNRESOLVED", "flavor":"dotnet", "editor_sha256":""},
  "godotSharp": {"version":"SAME_AS_GODOT"},
  "dotnetSdk": {"version":"UNRESOLVED", "rollForward":"disable"},
  "nugetLock": "packages.lock.json",
  "android": {"targetSdk":"UNRESOLVED", "compileSdk":"", "minSdk":"", "jdk":"", "gradle":""},
  "ios": {"xcode":"UNRESOLVED", "deploymentTarget":""},
  "exportTemplates": {"version":"SAME_AS_GODOT", "sha256":""}
}
```

版本选择必须引用该 release 的 Godot C# 绑定和官方平台文档，并由最小构建验证；不能仅改文档字符串。选定后提交 `global.json`、NuGet lock、export presets 和 artifact hash。

## 能力矩阵

| 能力 | Core | Desktop | Android | iOS | 当前 |
| --- | --- | --- | --- | --- | --- |
| C# build | 无 Godot SDK；`src/Core` 合同切片已通过离线 build | Godot .NET editor/export | Godot .NET export | Godot→Xcode | Core 合同通过；目标 ToolchainLock/完整 Core 未验证 |
| CP932 provider | Core test | test | test | test | 未验证目标 runtime |
| 动态代码/reflection | 设计尽量避免 | smoke | 裁剪/AOT smoke | AOT smoke | 未验证 |
| 外部游戏包 | content source interface | native picker/path | SAF/content URI | document picker/security scope | 仅设计 |
| 写存档 | storage port | user:// | user:// | sandbox user:// | 仅设计 |
| 外部 Emuera DLL | 不进入 Core | 默认禁用；可选完全信任逐 hash 授权 | Unsupported | Unsupported | legacy 有实现，目标安全策略已定 |
| 平台原生桥 | 不引用 | optional wrapper | SAF/SQLite native | iOS picker | 未完成目标 smoke |
| 渲染 backend | DTO only | Forward+/Compatibility 候选 | Mobile/Compatibility | Mobile | ADR 未定 |

## 迁移期与锁定后命令

```powershell
dotnet --version                         # 必须等于 global.json lock
dotnet build gemuera-c#.csproj -c Debug # M0 旧工程基线；从旧仓库根运行
dotnet restore --locked-mode
dotnet build src/Core/GEmuera.Core.csproj -c Release --no-restore
dotnet run --project tools/core-contracts/CoreContractSmoke.csproj -c Release --no-restore
powershell -NoProfile -ExecutionPolicy Bypass -File tools/core-contracts/Test-CoreArchitecture.ps1 -ProjectRoot .
dotnet test tests/Core/XEmuera.Godot.Core.Tests.csproj -c Release --no-build
dotnet test tests/Architecture/XEmuera.Godot.ArchitectureTests.csproj -c Release
godot --headless --path . --editor --quit-after 1       # M0/M1 legacy smoke
godot --headless --path src/App --editor --quit-after 1 # 远期拆分后
godot --headless --path src/App --export-release "Windows Desktop" reports/build/windows/XEmuera.exe
```

命令中的 `godot` path/preset 由 lock manifest 生成。没有实际执行和 artifact hash 时报告不得标 Passed。Debug editor 用于开发；性能数据来自 Release export/APK。

## M1 runner-only canary 比较

以下命令只通过显式 `legacy_runner.tscn` 在启动 `main.tscn` 前注入 `sessionIsolationMode`；它会把游戏复制到各自的隔离目录，依次运行 baseline→canary→baseline，并要求 only-canary 诊断和三段 semantic hash 同时符合预期：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/legacy-runner/Invoke-SessionIsolationCanaryBaseline.ps1 `
  -GodotPath "E:\Godot_v4.7-stable_mono_win64" `
  -GameRoot "<Era game root>" `
  -Profile v24pure `
  -OutputDirectory "<empty artifact directory>" `
  -RepeatCount 3
```

此报告是独立进程 startup/exit 的 canary 回退比较；不是同进程 A/B/A、静态隔离、Parser/VM handler/typed-policy behavior、Android 或 M1 gate 通过证据。Parser 初始化的 descriptor registry presence guard 由独立 contract 覆盖。普通 `project.godot -> first_window.tscn` 启动不读取 runner 字段，默认 `migration.session_isolation=false` 不变。

同一游戏的 runner-only 同进程 ABA 重启观察使用：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/legacy-runner/Invoke-SessionIsolationInProcessAba.ps1 `
  -GodotPath "E:\Godot_v4.7-stable_mono_win64" `
  -GameRoot "<Era game root>" `
  -OutputDirectory "<empty output directory>" `
  -RepeatCount 3
```

它仅在输入前的首等待对同一配置游戏连续 restart 两次，要求 generation 2/3、三次逻辑 fingerprint 和 repeat semantic hash 一致，并保留零 mutation 证据。它不证明跨游戏/profile、异步晚到 completion、全部 static、Android 或 M1 gate。

跨游戏/profile 的 runner-only A→B→A 观察使用：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/legacy-runner/Invoke-SessionIsolationInProcessCrossAba.ps1 `
  -GodotPath "E:\Godot_v4.7-stable_mono_win64" `
  -GameRoot "<A game root>" `
  -Profile v24pure `
  -AlternateGameRoot "<B game root>" `
  -AlternateProfile snake `
  -OutputDirectory "<empty output directory>" `
  -RepeatCount 1 `
  -UseDisplayServer
```

该入口在 `main.tscn` 创建前冻结 host-side A/B route，只允许 runner 选择已注册的 opaque `(gameId, profile)`，并要求 A 的第 1/3 fingerprint 相同、generation 2/3、两份副本均零 mutation。它仍只是首等待、无输入的观测，不表示所有 static 已隔离、可以在 UI 中热切换、已完成 Android/Parser/VM 接线或 M1 gate。

## 命令分层与 AI 快反馈

上一节列出的是迁移期与锁定后的完整命令集合，不应由每一次 AI 编辑全部执行。命令选择遵循 [AIDevelopmentWorkflow](AIDevelopmentWorkflow.md)：文档变更只跑文档守卫；纯 Core 修改先跑对应 build、Core contract smoke 或定向单元测试；单一 Godot 场景只跑相关带窗口场景套件；真实游戏、可视 Godot、export、APK、设备和性能仅在 work package 或发布门触发。

任务包必须只引用当前仓库实际存在且可复现的命令。本文中 tests、src/App、完整 ApiSmoke 等远期路径在落地前不能被 AI 当作失败后反复重试的默认命令；缺失时应记录 Uncovered 或测试基础设施缺口，再按阶段计划补齐。

## Core 无 Godot 验证

在不安装 Godot 的干净 runner 只 checkout Core/Tests/fixtures，运行 locked restore/build/test。检查 assembly references 和 Core csproj 无 GodotSharp。该 job 是 P0 gate。

## Godot ApiSmoke

最小项目编译并运行：文本 DrawString/TextLine/TextServer 实际签名、Theme/type variation、Signal/Variant/CallDeferred、Worker completion、AudioStreamPlayer、FileAccess、ResourceLoader threaded、DisplayServer safe area、application pause/resume。每个完整文档代码示例要么链接 smoke symbol，要么标伪代码。

## Desktop export

验证 Windows（后续按目标加 Linux/macOS）：启动、HiDPI/缩放、窗口/全屏、physical key、native picker、user:// 存档、关闭请求期间 save flush、无控制台 release。macOS 分发需 codesign/notarization 证据。静态工具模式可启用 low processor usage，但不能影响 VM 调度语义。

## Android

不请求广泛存储权限访问任意目录。流程：启动系统 document tree/file picker → 真实 callback 得 content URI → 请求/验证 persistable read permission（若 provider 支持）→ bounded stream copy 到 `user://imports/<generation>.tmp` → 验证 → commit。覆盖取消、权限撤销、provider 无 size、短读、空间不足、后台/进程终止和恢复。

旧 export preset 当前开启 `manage_external_storage`、read/write external storage，且打包 arm64 SQLite native library。这是 M0 必须记录的 legacy capability，不是目标方案。迁移先证明旧 Snake/eraFL 路径如何取得，再以 SAF/import cache feature flag 替换；在同一发布中保留明确回退和迁移提示，不能直接删除旧路径导致游戏目录不可见。SQLite Android smoke 必须验证 native library、连接、reader 和进程恢复。

目标 SDK、JDK、Gradle、Godot export template 必须锁定。使用 Mobile/Compatibility renderer 取决于原型；启用适当 ETC2/ASTC app asset 导入。后台通知时暂停 VM/降低刷新，先完成/记录关键 journal，不做长阻塞 I/O 导致 ANR。

## iOS

Godot 导出 Xcode 工程后验证 C#、AOT/裁剪和原生 picker plugin。document picker 回调取得 security-scoped URL；开始 access、流式复制到 sandbox、finally 停止 access。测试权限/书签失效、取消、空间不足、后台 suspension、系统杀进程和恢复。没有 macOS/Xcode/真机记录前保持 Uncovered。

## export smoke

每个平台 artifact 安装/启动后自动输出脱敏 capability report，加载内置最小 fixture、运行 VM/HTML/输入、写读临时存档、退出。报告包含 artifact SHA-256、工具链 lock、exit code 和日志。编辑器可运行不是 export 通过。

## 版本升级清单

1. 更新候选 lock 并保留旧 lock baseline。
2. locked restore/Core tests/architecture。
3. Godot ApiSmoke 与全部 C# warnings。
4. 差分 fixture；不得因版本升级改变兼容结果。
5. 三平台 export smoke、裁剪/AOT、原生插件。
6. 性能/内存 baseline diff。
7. 官方 breaking change/release note 链接、许可证与包体积更新。
8. 评审通过后替换主 lock。

## 许可证与密钥

第三方包/字体/decoder/plugin 记录名称、版本、license、source URL 和随包义务。keystore、Apple certificate、密码只在 CI secret，绝不进仓库/文档。release export 排除 fixture、设计文档和调试命令，除非许可证要求随包。

## 文档门禁命令

设计文档 PR 至少运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/doc-guards/Invoke-DocGuard.ps1 `
  -ReportPath artifacts/doc-guard-report.json
```

CI 必须保留 JSON artifact 和进程退出码。此命令不需要 Godot/.NET/JDK，因此不能替代 ApiSmoke、export、fixture 或真机门禁。
