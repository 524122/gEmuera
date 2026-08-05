# NewFrameworkDesign 文档守卫

该守卫把 `docs/NewFrameworkDesign/AcceptanceTraceability.md` 中的静态检查变成可执行门禁。它只读取设计文档；除非显式传入 `-ReportPath`，否则不会写文件。

## 本地运行

在仓库根目录执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/doc-guards/Invoke-DocGuard.ps1
```

需要保存机器可读报告时：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/doc-guards/Invoke-DocGuard.ps1 `
  -ReportPath artifacts/doc-guard-report.json
```

检查失败时进程返回非零退出码。报告包含 schema/guard 版本、设计文档 manifest SHA-256、文档数量、行数、字节数、错误和警告；CI 应把 JSON 作为 artifact 归档，不要手工复制其中的动态计数到设计文档。

## 检查范围

- 必需权威文档、唯一一级标题、空文档、UTF-8 U+FFFD。
- 相对 Markdown 链接、fenced code block、Markdown 表格列数。
- 旧的 36/37 文档计数、“五根主梁”和无条件兼容完成声明。
- README、M0–M2、存档 profile、Android 旧路径、Display 队列等不可删除的生产基线条款。
- `KnownLimitations` 与 `CompatibilityMatrix` 中 L-026/L-027/L-028 的显式 key 关系。
- 上游 1808 字节协议只能由 `SaveFormat.md` 声明为唯一权威定义。

守卫验证的是文档结构和关键约束存在性，不证明运行时兼容、APK 可用、真机性能或安全限制已经实现。

