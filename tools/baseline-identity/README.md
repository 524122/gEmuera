# M0 Baseline Identity

`M0-ID-01` 在不修改旧 gEmuera 行为的前提下，为源码树、配置、资源、字体、工具链、运行产物和游戏目录生成 SHA-256 身份报告。主报告固定为 `identity.json`，详细字节来源保存在同目录的 `*.manifest.json`。

## 运行

最小运行会成功生成部分报告，并把未提供的 APK、游戏目录、Godot/JDK/Android 工具链明确列入 `uncovered`：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/baseline-identity/Invoke-BaselineIdentity.ps1 `
  -OutputDirectory artifacts/baseline-identity
```

完整采集示例：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/baseline-identity/Invoke-BaselineIdentity.ps1 `
  -OutputDirectory artifacts/baseline-identity `
  -GameRoot "E:\Godot_v4.6.2-stable_mono_win64\snake\EraTW-Magic_DLC-update_base" `
  -ArtifactPath "E:\artifacts\gemuera.apk" `
  -GodotExecutable "E:\Godot\Godot_v4.7-stable_mono_win64.exe" `
  -AndroidSdkRoot "$env:ANDROID_SDK_ROOT" `
  -AndroidNdkRoot "$env:ANDROID_NDK_ROOT" `
  -ExportTemplatesRoot "E:\Godot\templates\4.7.stable"
```

显式传入但不存在的游戏、产物、可执行文件或工具链目录会返回非零退出码；未传入的可选证据会产生 `Partial` 报告，而不会伪装成 `Complete`。源码树清单排除 `.git`、`.godot`、`.codegraph`、本地 Agent/IDE 状态、构建输出、依赖缓存、`action_maps`、报告目录及 APK/AAB 等非源码产物。清单使用 UTF-8 路径长度前缀和 ordinal 路径排序，不受系统区域设置影响；无可用 Git 元数据时，`source-tree.manifest.json` 的 canonical SHA-256 是权威源码身份。

## 验证

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/baseline-identity/Test-BaselineIdentity.ps1
```

测试验证：排除目录变化不会改变源码身份、源码字节变化一定改变身份、缺失的显式产物返回非零退出码。报告结构由 [identity.schema.json](identity.schema.json) 固定。
