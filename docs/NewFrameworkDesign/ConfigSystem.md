# 配置来源、默认值与会话隔离

## 配置域

| 域 | 所有者 | 示例 | 跨游戏 |
| --- | --- | --- | --- |
| AppSettings | AppBootstrap | 语言、诊断同意、最近选择 | 是 |
| UserPreferences | 应用设置服务 | Theme、字体缩放、音量、键位 | 是，但不改变兼容语义 |
| GameConfig | GameSession | emuera.config 与游戏行为字段 | 否 |
| CompatibilityPlan | GameSession | 组合式 dialect modules、typed policies、lazy、VARI/VARS、存档 codec/capabilities | 否 |
| EffectivePlatformConfig | Bridge | safe area、renderer capability、质量档 | 重算 |

GameConfig 不是 Autoload singleton。每次 SwitchGame 先由 resolver 根据用户 pin、精确 fingerprint、受限 manifest 和 feature probe 得到候选 `CompatibilityPlan`，再调用 `GameConfigDefaults.CreateComplete(plan)`，依次应用传统 config、旧 gEmuera `setting.json` 和当前游戏 override；缺文件/缺字段保持该计划的完整默认，绝不沿用上一会话对象。解析/冲突规则见 [DialectExtensionSystem](DialectExtensionSystem.md)。

## 加载流水线

```text
complete defaults
 → resolve/freeze compatibility plan
 → EncodingService read current game config
 → parse key/value with source position
 → parse setting.json with explicit schema/version
 → validate type/range/known key
 → build immutable GameConfig candidate
 → validate cross-field invariants
 → include in candidate GameSession commit
```

解析错误分为 fatal 与 warning，兼容行为由 XEmuera fixture 决定。未知字段保留诊断但不执行任意动态代码。可能为 Shift-JIS 的文件不得绕过 EncodingService。

## Schema

每个字段在 schema 中记录 key、类型、完整默认值、范围、XEmuera 源码符号、是否影响兼容、平台映射和迁移策略。生成文档表与测试数据，避免配置类、UI 和 parser 各维护一份默认值。

旧 gEmuera `JSONConfigData` 已确认字段包括 `UseButtonFocusBackgroundColor`、`UseNewRandom`、`UseScopedVariableInstruction` 和 `RenderingBackend`。它们进入 v24/Snake compatibility plan：显示类字段可投影到 View；随机数和 scoped variable 会改变脚本结果，必须进入 plan hash、fixture manifest 和诊断，不得当普通 UI 偏好。

## 变更传播

会话内兼容配置在启动后默认只读；脚本允许改变的项通过明确命令更新 owner 并发 C# event。UserPreferences 变化由 MainOrchestrator 向下调用 Theme/Input/Audio components，不让 Core 引用 UI。

Theme 切换在根 Control 替换 Theme resource；子节点继承并通过 type variation 表示语义样式。字体/缩放变化使 rendering measurement cache revision 递增并触发可见索引重排。

## 安全与隐私

用户配置写 `user://` 对应应用目录，采用事务临时文件；游戏包内配置视为不可信，受文件/行/字段上限。诊断只记录 key 与错误码，不记录私有路径和字段可能包含的用户文本。

## 测试

- 游戏 A 覆盖字段，切换到缺该字段的 B，B 恢复完整默认。
- 缺配置、空配置、重复 key、未知 key、非法数值/编码。
- Theme/字体缩放只影响 View，不改变 VM 差分状态。
- 快速切换时 A 的异步解析结果不能提交到 B。
- setting.json 缺失/部分字段/旧字段；Snake/v24 profile 默认值与旧工程一致。
- `UseNewRandom`、`UseScopedVariableInstruction` 和 RenderingBackend 被记录到差分报告。
- 同一构建加入 Snake/其他模块后，未选择它们的 v24 plan hash、默认值和执行结果保持不变。
- 伪造/未知 manifest key、冲突模块、弱 marker 和用户 pin 优先级产生稳定 ResolutionReport。
