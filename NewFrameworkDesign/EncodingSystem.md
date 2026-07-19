# 文本编码、流读取与语料

## XEmuera 检测算法

唯一事实源 `XEmuera/EncodingHelper.cs`：

1. 若流可 seek，保存原 position 并 seek 到 0。
2. 读取前三字节；`EF BB BF` 时选择 `UTF8Encoding(true,true)`。
3. 否则用 `UTF8Encoding(false,true)` 与 StreamReader 完整 `ReadToEnd`；invalid byte sequence 抛异常。
4. 严格 UTF-8 完整成功则选择 UTF-8；异常回退 `Encoding.GetEncoding("SHIFT-JIS")`。
5. finally 恢复原 position。

文件打开异常也回退 Shift-JIS。该算法不对字符频率打分。合法且同时可解释的字节只要严格 UTF-8 成功，就选择 UTF-8。

## Core EncodingService

所有 ERB、ERH、CSV、游戏配置和外部文本通过同一服务；Godot `FileAccess.GetLine` 不直接读取未知编码内容。接口接收受限 Stream/ReadOnlyMemory，返回 `DecodedText(EncodingKind, Text, HadBom)` 或 typed fault。

非 seekable content URI 流不能先读后回退同一流：Bridge 在总大小上限内复制到候选文件或 bounded rewind buffer，然后 Core 对完整内容检测。不得为了检测把无界网络/URI 流一次性读入内存。

## Code page 注册

.NET 运行时必须在应用启动 smoke test 中验证 `Encoding.GetEncoding("SHIFT-JIS")`。如果锁定 runtime 需要 `CodePagesEncodingProvider.Instance`，由 AppBootstrap 在任何游戏文件读取前注册；Core 测试独立验证 provider 存在。平台不支持时返回 `EncodingProviderUnavailable`，不可默默使用系统默认编码。

## BOM 与换行

UTF-8 BOM 不进入首行文本。UTF-16/32 BOM 没有原版证据，默认按严格 UTF-8 失败后 CP932 解释或被语料确认；不能擅自新增自动检测并称兼容。保留原换行信息供 SourcePosition，解析层再规范 CRLF/LF。

## 错误和限制

文件/行/字符串遵循[SecurityLimits](SecurityLimits.md)。严格 UTF-8 失败并回退 CP932 后，如 CP932 decoder 也失败，返回 `InvalidTextEncoding`，记录文件逻辑 token 和 byte offset，不记录私有绝对路径或全文。

## 差分语料

| 类别 | 样本 |
| --- | --- |
| UTF-8 | ASCII、CJK、emoji、组合字符、无 BOM |
| BOM | UTF-8 BOM、空文件、仅 BOM |
| CP932 | 日文假名/汉字、半角片假名、扩展字符 |
| 歧义 | 同时是合法 UTF-8 与有意义 CP932 的字节 |
| 非法 UTF-8 | lone continuation、overlong、surrogate、>U+10FFFF |
| 截断 | 2/3/4-byte sequence 在 EOF 截断 |
| 平台流 | seekable/non-seekable、短读、取消、打开失败 |

每个 fixture 记录 bytes SHA-256、XEmuera 选择编码、行结果和异常。脚本、CSV 与配置必须共用同一 corpus runner。

## 文本宽度

编码选择与视觉宽度完全分离。全角/半角不按固定比例计算；Bridge 使用锁定字体的 shaping/measurement，覆盖 CJK/Latin/emoji/RTL，详见[RenderingSystem](RenderingSystem.md)。
