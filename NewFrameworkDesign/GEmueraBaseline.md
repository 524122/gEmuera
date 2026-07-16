# 可运行 gEmuera 基线与兼容资产

## 定位

新模拟器不是对旧 gEmuera 的逐文件整理，也不是只按上游 XEmuera 绿地重写。它以三层证据共同约束实现：

1. **上游 XEmuera**：原版指令、存档、变量和错误语义的来源。
2. **可运行 gEmuera**：已经解决 Godot、Snake、eraFL、Android、渲染和输入问题的工程资产。
3. **实际游戏 fixture**：Snake、eraFL 及其他代表游戏的脚本、存档、截图、输入时间线和性能报告。

上游行为和 gEmuera 扩展冲突时，不按“谁更新”简单覆盖。矩阵分别记录 `upstreamStatus`、`legacyStatus`、`targetStatus`：原版入口默认保持上游语义；被实际游戏依赖的 gEmuera 扩展进入明确的兼容 profile；纯体验增强必须可关闭。

保留的是**可观察行为和已经验证的工程经验**，不是旧代码形态。GlobalStatic、GenericUtils 静态桥、EmueraContent 巨类、默认 AssemblyLoadContext 插件、广泛 Android 存储权限和混杂 uEmuera/Godot 类型都是待迁移债务；它们只通过有期限 façade 存活，不能进入新 Core 公共契约。

## 审计对象身份

旧工程只作为只读参考，根目录为 `E:/MyCode/GodotCode/gEmuera-future`。当前命令行无法从其 `.git` 解析 commit，因此暂以文件哈希固定本次审计对象。

| 文件 | 已确认事实 | SHA-256 |
| --- | --- | --- |
| `project.godot` | C#、Mobile、Godot feature `4.7`、主场景 `first_window.tscn`、Mobile renderer | `352A962F2D6567CC88256FD202DE0E7B9A05BD4A84B4D3FD09954E528C51C409` |
| `gemuera-c#.csproj` | `Godot.NET.Sdk/4.7.0`；桌面 net8.0、Android net9.0；SQLite/SkiaSharp | `2E17B20C585AE2EBF8014C836C7252B8BED2CEB437264287214A0B7F8A68A61C` |
| `CODE_MAP.md` | 扫描到 168 个 `Scripts/**/*.cs`；记录 Snake/eraFL/Android 修复 | `2BE4D4A6506C73E9F50DB2FEFED33069CE1371157A7BD2C521BC1027EA5EC901` |
| `VariableCode.cs` | LOCALF/ARGF/RESULTF、REFF/2D/3D | `FEF289C551A6FAAF165A73104C18691F4A4A4DEED31EFEF3338500E9AB434B0E` |
| `ConsoleDivPart.cs` | 完整 div 布局和子行模型 | `7DF080236204BB308C7D05245451014A63EFDFD921887A5DC390CA3B0C02D6E3` |
| `ConsoleImagePart.cs` | `src/srcb`、定位、翻转、ColorMatrix | `7F029D6168BDF11D8DF0A867F69FDF6F6392A955CDED1B110C0F46B0BE4BAF6A` |
| `GraphicsImage.cs` | 脚本可观察的 CPU 图像操作和稳定显示快照 | `03DDB46D95932B1F18CA0790B0CA9D7D6F49FE596AC73B3B819B2800D23ED3C3` |
| `EmueraThread.cs` | 专用后台解释器线程与输入唤醒 | `A82807A2D5D567D363857FD90C7085BD069EE78FB6B59DF464125F7959DEF095` |
| `VirtualCursor.cs` | Android 可见虚拟光标、拖动、左右中键 | `70D70B3DEC846CBE42F2EA75FA5DAD478C84337A9F65EF425172B361339FB2CE` |

这些哈希只证明源码身份，不证明每项功能已通过目标设备。旧项目“可以运行”是迁移起点；正式基线仍要归档 APK、游戏目录哈希、操作步骤和日志。

## 必须保留的语言与数据扩展

| 能力 | 旧工程证据 | 新架构要求 |
| --- | --- | --- |
| 小数类型 | `EraType.Float`、`#FUNCTIONF`、LOCALF/ARGF/RESULTF、REFF/2D/3D | `VmValue.Float`、小数标量/数组/REF/局部/返回值是一级类型 |
| 小数稀疏数组 | `SparseArray<double>` 及数组统计/排序路径 | 1D 稀疏接口覆盖 long/string/double；2D/3D 保持兼容布局 |
| 小数存档扩展 | `EraBinaryDataReader/Writer` float array 与 VariableData 路径 | 单独标记 gEmuera/Snake 扩展类型；不能冒充纯上游 1808 子集 |
| VarExt 保存域 | `ConstantData` + `RuntimeDataStore` | Map/XML/DataTable 分 SAVE/GLOBAL/STATIC；切换/读档清理按域执行 |
| SQLite | `Creator.Method.Sql.cs`、`SnakeSqlManager`、`ModernSqlManager` | capability/profile；同步返回语义、事务和 Android native library 都需 fixture |
| scoped VARI/VARS | `UseScopedVariableInstruction` | profile 配置；默认值由实际游戏基线决定 |
| lazy ERB | `Process.LazyLoading.cs` | 保留索引/按需完整解析语义和 Android 性能基线 |

## 必须保留的显示、刷新与输入资产

| 能力 | 旧工程证据 | 不能退化的契约 |
| --- | --- | --- |
| 复杂 div | `ConsoleDivPart` 的 X/Y/Width/Height/Depth/Background/Box/Display/Children | Display DTO 必须保留嵌套盒模型、定位和跨行溢出，不能降为换行 |
| 图片双态 | `ConsoleImagePart.ResourceName/ButtonResourceName` | normal/selected 两个资源 key、size/position/display/flip/ColorMatrix |
| 混合后端 | `EmueraContent.Canvas.cs` + Control fallback | 普通行可走 Canvas；复杂 div 可回退 Control；feature flag 可切全 Controls |
| 行级差分 | `ApplyTextChanges`、`dataOnlyLines` | append/update/remove/data-only 分开，按钮 generation 更新不强制重建视觉节点 |
| 滚动事务 | `EmueraDisplayScrollMode`、动态地图 scope | FollowBottom/PreserveViewport/KeepChoicesVisible 和 anchor 明确化 |
| 动态地图 | `DynamicMapFunctionScoped`、BitmapCache metadata | 地图重绘批次原子提交，不展示中间半帧 |
| Android 虚拟光标 | `VirtualCursor` | 可见光标；拖动与点击判定；L/R/M；缩放手势优先 |
| MOUSEBUTTON 双通道 | `SetPointingButton` + Canvas visual hover | ERB 查询状态和 Godot 高亮必须来自同一命中结果 |
| NF 输入 | TINPUTNF/TINPUTSNF/TONEINPUTNF/TONEINPUTSNF、`InputRequest.NoFocus` | NoFocus 是请求契约字段，不是 UI 私有选项 |
| HOTKEY | `HotkeyState`、Creator 注册 | 初始化、状态数组、Ctrl+D/硬件键盘 profile 和错误语义 |
| setting.json | JSONConfig/Data | Snake/v24 选项与通用 GameConfig 分层合并，切换不泄漏 |

## GraphicsImage 经验资产

旧工程已经证明 G 并非“只写渲染命令”：GWIDTH/GHEIGHT/GGETCOLOR 等会立即读回，GSETCOLOR/GDRAW* 的结果可被下一条表达式观察。`GraphicsImage` 还维护 `DisplayRevision` 和稳定快照，避免 UI 上传中间状态。

新架构因此选择：**Session `PixelStore` 是所有脚本可观察图片的唯一 CPU 像素真相；Godot Texture/RID 只是 revision 投影**。完整契约见 [ExecutionContract](ExecutionContract.md) 和 [ResourceSystem](ResourceSystem.md)。

## 插件安全事实

旧 `PluginManager` 从 `Plugins/*.dll` 使用默认 `AssemblyLoadContext` 加载，允许反射调用，并把 Process、变量、Console 和 shell-open 能力暴露给插件。这等同于给插件当前进程和用户账号的完整权限，不是沙箱。

目标策略：

- 外部 DLL 在 Android/iOS、未信任游戏包和默认桌面 profile 中禁用。
- 桌面“受信任插件模式”必须由用户对每个 DLL 哈希显式授权，清楚提示其拥有完整进程权限；签名只证明发布者身份，不能证明安全。
- 内置浏览器/LLM 等能力从外部插件机制拆出，进入受权限检查的 Platform capability。
- 真正不可信插件若未来支持，必须进独立进程/受限 broker；在同一 .NET 进程内不宣称可靠沙箱。

## 基线 fixture 组合

每个兼容行为可引用一个或多个来源：

```text
UP-*  上游 XEmuera 最小行为
GE-*  旧 gEmuera 扩展/修复行为
SN-*  Snake 游戏回归
FL-*  eraFL 显示/输入/地图回归
AN-*  Android APK 真机行为
```

优先建立的套件：FUNCTIONF/REFF/小数存档；NF/HOTKEY；VarExt/SQL；嵌套 div/srcb；G 同步读写；动态地图 data-only/滚动；虚拟光标左右中键；lazy ERB；setting.json profile。只有 target 与对应来源的状态、输出、错误、顺序和视觉报告一致，才能把该 profile 条目标为 Compatible。

## 治理缺口

旧 `_CLAUDE.md` 要求先读 `IDEAS.md`，但审计根目录不存在该文件；`CODE_MAP.md` 的目录树仍声称它存在。旧 `AGENT.md` 又要求结束后删除测试文件，与长期 TDD/回归资产冲突。新项目规则以 [MigrationPlan](MigrationPlan.md) 的测试治理为准：临时诊断可删，正式 unit/integration/fixture/golden/benchmark 必须版本化保留。
