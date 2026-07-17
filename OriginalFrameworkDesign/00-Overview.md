# gEmuera-future 项目总览

## 项目定位

gEmuera-future 是一个基于 Godot 4.7 (C#) 的 ERA 游戏引擎模拟器，从 XEmuera (Xamarin.Forms) 移植而来。目标是在 Android/iOS/桌面端提供高性能的 ERA 游戏运行环境，重点优化移动端体验。

## 与原版 XEmuera 的关系

```
原版 Emuera (Windows WinForms + GDI+)
    │
    ▼
XEmuera (Xamarin.Forms + SkiaSharp) ← 参考设计文档
    │
    ▼
gEmuera-future (Godot 4.7 + C#)   ← 本项目
```

移植策略：
- 脚本引擎核心（ERB 解析/执行、变量系统、表达式求值）从 XEmuera 直接移植，保持逻辑一致
- 渲染层完全重写，替换 SkiaSharp 为 Godot 原生 Control/Canvas 混合渲染
- 平台层利用 Godot 跨平台能力，无需手动处理 Android/iOS 差异
- 新增 GPU 加速（ColorMatrix）、异步纹理加载、Canvas 批量绘制等性能优化

## 技术栈

| 层级 | 技术选型 | 说明 |
|------|----------|------|
| 游戏引擎 | Godot 4.7 | Mobile 渲染器，ETC2/ASTC 纹理压缩 |
| 编程语言 | C# (.NET) | 与原 XEmuera 保持语言一致，减少移植成本 |
| 渲染方式 | Control + Canvas2D 混合 | 简单行走 Canvas 快路径，复杂 HTML/div 走 Control 节点 |
| 线程模型 | 双线程 | 主线程(Godot渲染+输入) + 脚本线程(ERB执行) |
| 构建目标 | Mobile 优先 | Android ARM64, iOS, 兼容桌面 |

## 核心设计原则

1. **引擎核心不依赖 Godot API** — `Scripts/Emuera/` 下的代码可独立编译，通过桥接层与 Godot 交互
2. **双线程隔离** — ERB 脚本在后台线程执行，通过队列与主线程通信，避免阻塞 UI
3. **Canvas 优先渲染** — 普通文本行用 Canvas2D 批量绘制，减少节点数；复杂 HTML div 回退到 Control 节点
4. **异步资源加载** — 图片纹理在后台解码，主线程仅做 GPU 上传，避免帧卡顿
5. **移动端生命周期感知** — 后台暂停时降帧省电，恢复时平滑回到正常状态

## 文档索引

| 文档 | 说明 |
|------|------|
| [01-Architecture.md](./01-Architecture.md) | 分层架构设计与数据流 |
| [02-ModuleResponsibilities.md](./02-ModuleResponsibilities.md) | 各模块职责划分 |
| [03-CoreClasses.md](./03-CoreClasses.md) | 关键类与函数说明 |
| [04-ThreadingModel.md](./04-ThreadingModel.md) | 线程模型与跨线程通信 |
| [05-RenderingPipeline.md](./05-RenderingPipeline.md) | 渲染管线设计 |
| [06-ScriptEngine.md](./06-ScriptEngine.md) | ERB/CSV 脚本引擎 |
| [07-VariableSystem.md](./07-VariableSystem.md) | 变量系统与数据模型 |
| [08-InputSystem.md](./08-InputSystem.md) | 输入系统（触摸/虚拟光标/键盘） |
| [09-ResourceManagement.md](./09-ResourceManagement.md) | 资源管理与内存策略 |
| [10-PerformanceOptimization.md](./10-PerformanceOptimization.md) | 移动端性能优化 |
| [11-LifecycleManagement.md](./11-LifecycleManagement.md) | Godot 组件生命周期 |
| [12-DependencyGraph.md](./12-DependencyGraph.md) | 模块依赖关系图 |
| [13-BuildAndRun.md](./13-BuildAndRun.md) | 构建与运行指南 |
| [14-BridgeLayer.md](./14-BridgeLayer.md) | uEmuera 桥接层设计 |
| [15-Glossary.md](./15-Glossary.md) | 术语表 |
