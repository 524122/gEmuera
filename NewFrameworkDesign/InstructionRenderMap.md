# 指令、内置函数与效果映射

## 机械事实来源

兼容清单不能手工凭印象维护。生成器读取：

- `Emuera/GameProc/Function/FunctionIdentifier.cs` 的 `addFunction` 注册：指令名、ArgumentBuilder、扩展标志。
- `Emuera/GameProc/Function/BuiltInFunctionCode.cs`：可识别 code 集合。
- `Emuera/GameData/Function/Creator*.cs`：内置函数对象、返回类型、参数、可选参数和实现。
- `Instraction*.cs`、`Process.ScriptProc.cs`：运行状态变化和错误。

生成记录字段：`behaviorKey`、kind、name、case policy、参数类型/数量/可选起点、返回类型、flags、实现符号、Core effect、错误、状态变化、fixture、状态和差异原因。CI 比较生成集合，新增/删除行为必须显式分类。

## 已确认行为

| 名称 | 种类 | 源码符号 | 参数/结果 | Core/Bridge 映射 |
| --- | --- | --- | --- | --- |
| RESTART | instruction | `RESTART_Instruction` | void | VM 当前 frame JumpTo ParentLabelLine |
| UPCHECK | instruction | `Process.ScriptProc`, `UpdateInUpcheck` | void | Variable update + optional DisplayEffect |
| CUPCHECK | instruction | `CUpdateInUpcheck` | target int | 目标角色 update + optional DisplayEffect |
| QUIT_AND_RESTART | function/control | `Process.ScriptProc` | void | ApplicationEffect restart |
| FORCE_QUIT_AND_RESTART | function/control | 同上 | void | ApplicationEffect force restart |
| SETSOUNDVOLUME | instruction | `SETSOUNDVOLUME_Instruction` | int expression | AudioEffect set SFX logical volume |
| SETBGMVOLUME | instruction | `SETBGMVOLUME_Instruction` | int expression | AudioEffect set BGM logical volume |
| CBGCLEAR | built-in method | `CBGClearMethod` | `()->int`, returns 1 | ResourceEffect clear CBG |
| CBGREMOVERANGE | method | `CBGRemoveRangeMethod` | `(int,int)->int` | clear z range |
| CBGCLEARBUTTON | method | `CBGClearButtonMethod` | `()->int` | clear button map |
| CBGREMOVEBMAP | method | `CBGRemoveBMapMethod` | `()->int` | clear bitmap |
| CBGSETG | method | `CBGSetGraphicsMethod` | `(id,x,y,z)->int`; z != 0/int32 | set GraphicsImage/G |
| CBGSETBMAPG | method | `CBGSetBMapGMethod` | `(id)->int` | set button map from G |
| CBGSETSPRITE | method | `Creator.cs` → `CBGSetCIMGMethod` | `(name,x,y,z)->int` | set Sprite image；公共名与实现类名不同 |
| CBGSETBUTTONSPRITE | method | `Creator.cs` → `CBGSETButtonSpriteMethod` | 6–7 args（tooltip/返回路径仍需 fixture） | button normal/selected Sprite |

本工作区上游快照确认 `CBGSETSPRITE` 与 `CBGSETBUTTONSPRITE`，未注册公共名 `CBGSETCIMG`/`CBGSETBUTTONCIMG`。旧 gEmuera `Creator.cs` 则同时注册 `CBGSETSPRITE` 和别名 `CBGSETCIMG`，因此后者在 GEmueraSnake profile 是 `Legacy Extension`，不能全局删除，也不能反向写入 Upstream1808 profile。`CBG_CREATE`、`CBGSETIMG`、`CBGSETPOS`、`CBGSETBUTTONID`、`SETVOLUME`、`ISPLAYING` 等也必须按三集合/profile 分类，不能只凭上游快照全局删除。

## 输出路径

不能把所有外部行为笼统称为 Effect。每项必须标 `CoreImmediate`、`CommitThenProject`、`FireAndContinue`、`WaitPort` 或迁移期 `VmThreadBlockingBounded`，并记录 ordering point/failure return/thread owner。G 像素操作在 PixelStore 同步完成；Display/CBG 先提交逻辑 revision 再投影；保存、GSAVE/GLOAD、文本 I/O、枚举和 SQL 按 [ExecutionContract](ExecutionContract.md) 等待或有界阻塞；音频逐项由 fixture 裁决。

旧 gEmuera 的 float/SQL/NF/HOTKEY/MOUSEBUTTON/动态地图/图形扩展清单见 [GEmueraBaseline](GEmueraBaseline.md) 与 [InstructionInventory](InstructionInventory.md)，未分类项不得在迁移中丢弃。

## Graphics/CBG 所有权

- G 槽归 GameSession graphics state，动态创建/清空按会话生命周期。
- Sprite descriptor 归 ResourceCatalog；Bridge texture 是派生缓存。
- CBG layer 保存逻辑 handle、坐标、z、按钮值；不在 Core 保存 Texture。
- z/range、缺失资源和返回 0/抛错的差异由执行 fixture 确认。

## 测试生成

对每条行为自动产生 parse acceptance（大小写、必需/可选参数）、type error、range boundary 和最小 execute fixture。覆盖率以“生成矩阵行为数/源码注册行为数”报告，不能用总测试数量代替。
