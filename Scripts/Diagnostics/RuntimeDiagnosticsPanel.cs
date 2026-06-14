using System;
using System.Text;
using Godot;

namespace gEmuera.Diagnostics
{
    /// <summary>
    /// 企业级说明：运行时诊断面板是配置编辑 UI，不承载日志路由和业务诊断逻辑。
    /// 面板只在 debug.runtime_panel.enabled=true 时实例化，避免普通 APK 游玩期增加节点和布局成本。
    /// 所有改动写入 user://config.toml，保存后可热重载轻量开关；结构性参数建议重启后生效。
    /// </summary>
    public partial class RuntimeDiagnosticsPanel : PanelContainer
    {
        RuntimeDiagnosticsConfig config;
        VBoxContainer optionRoot;
        TabContainer optionTabs;
        MarginContainer panelMargin;
        Label titleLabel;
        Label statusLabel;
        Label statsLabel;
        Label consoleUserPathLabel;
        TextEdit consoleTextEdit;
        Control optionBody;
        bool collapsed;
        bool addingQuickOptions;
        bool quickOptionsDirty;
        bool expertOptionsDirty;

        public bool FloatingMode { get; set; }
        public event Action HideRequested;
        public event Action CloseRequested;

        public override void _Ready()
        {
            BuildPanel();
            RefreshResponsiveLayout();
        }

        public override void _Notification(int what)
        {
            if (what == NotificationResized)
                RefreshResponsiveLayout();
        }

        public static void AttachFloatingTo(Node parent)
        {
            var currentConfig = global::GenericUtils.GetRuntimeDiagnosticsConfig()
                ?? RuntimeDiagnosticsConfigLoader.Load().Config;
            if (parent == null || currentConfig == null || !currentConfig.RuntimePanelEnabled)
                return;

            var layer = new CanvasLayer { Layer = 100 };
            parent.AddChild(layer);

            var host = new FloatingDiagnosticsHost();
            host.SetAnchorsPreset(LayoutPreset.FullRect);
            host.MouseFilter = MouseFilterEnum.Ignore;
            layer.AddChild(host);
            host.TreeExiting += () => layer.QueueFree();
        }

        void BuildPanel()
        {
            AddThemeStyleboxOverride("panel", CreatePanelStyle());
            SizeFlagsHorizontal = SizeFlags.ExpandFill;
            SizeFlagsVertical = SizeFlags.ExpandFill;
            MouseFilter = MouseFilterEnum.Stop;

            panelMargin = new MarginContainer();
            panelMargin.MouseFilter = MouseFilterEnum.Pass;
            ApplyMargin(panelMargin, 12, 12);
            AddChild(panelMargin);

            var root = new VBoxContainer();
            root.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            root.SizeFlagsVertical = SizeFlags.ExpandFill;
            root.AddThemeConstantOverride("separation", 10);
            root.MouseFilter = MouseFilterEnum.Pass;
            panelMargin.AddChild(root);

            root.AddChild(CreateHeader());

            statusLabel = CreateSmallLabel("");
            root.AddChild(statusLabel);

            statsLabel = CreateSmallLabel("");
            root.AddChild(statsLabel);

            optionBody = new VBoxContainer();
            optionBody.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            optionBody.SizeFlagsVertical = SizeFlags.ExpandFill;
            root.AddChild(optionBody);

            // 企业级说明：调试配置项数量很多，手机横屏下一条长列表难以扫描。
            // TabContainer 只分组 UI 节点，不改变配置模型和日志路由；每个页签独立滚动，避免切换模块时丢失主面板操作区。
            optionTabs = new TabContainer();
            optionTabs.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            optionTabs.SizeFlagsVertical = SizeFlags.ExpandFill;
            optionTabs.CustomMinimumSize = FloatingMode ? new Vector2(0, 160) : new Vector2(0, 180);
            optionTabs.MouseFilter = MouseFilterEnum.Stop;
            optionBody.AddChild(optionTabs);

            LoadConfigIntoPanel();
        }

        Control CreateHeader()
        {
            var header = new VBoxContainer();
            header.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            header.AddThemeConstantOverride("separation", 8);

            titleLabel = new Label();
            titleLabel.Text = FloatingMode ? "日志诊断悬浮窗" : "日志系统调试";
            titleLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            titleLabel.MouseFilter = MouseFilterEnum.Ignore;
            titleLabel.AddThemeFontSizeOverride("font_size", 20);
            titleLabel.AddThemeColorOverride("font_color", new Color(0.96f, 0.98f, 1.0f));
            header.AddChild(titleLabel);

            var actions = new HFlowContainer();
            actions.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            actions.AddThemeConstantOverride("h_separation", 8);
            actions.AddThemeConstantOverride("v_separation", 8);
            header.AddChild(actions);

            var reload = CreateActionButton("重读", "重新读取 user://config.toml 或 res://config.toml。");
            reload.Pressed += LoadConfigIntoPanel;
            actions.AddChild(reload);

            var save = CreateActionButton("保存", "写入 user://config.toml，并在主运行场景中热重载轻量开关。");
            save.Pressed += SaveConfigFromPanel;
            actions.AddChild(save);

            var export = CreateActionButton("导出", "导出当前 ring buffer 诊断日志。进入主运行场景前可能没有运行时日志。");
            export.Pressed += ExportDiagnosticLog;
            actions.AddChild(export);

            var exportPackage = CreateActionButton("导出包", "导出完整诊断包，包含日志、配置快照、设备、屏幕、Godot、APK、游戏和摘要信息。");
            exportPackage.Pressed += ExportDiagnosticPackage;
            actions.AddChild(exportPackage);

            if (FloatingMode)
            {
                var collapse = CreateActionButton("隐藏", "隐藏悬浮弹窗，保留悬浮球入口。");
                collapse.Pressed += ToggleCollapsed;
                actions.AddChild(collapse);

                var close = CreateActionButton("关闭", "关闭本次悬浮窗，不修改配置文件。");
                close.Pressed += () => CloseRequested?.Invoke();
                actions.AddChild(close);
            }

            return header;
        }

        Button CreateActionButton(string text, string tooltip)
        {
            var button = new Button();
            button.Text = text;
            button.TooltipText = tooltip;
            button.CustomMinimumSize = new Vector2(88, 44);
            button.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
            button.AddThemeFontSizeOverride("font_size", 15);
            return button;
        }

        void RefreshResponsiveLayout()
        {
            // 企业级说明：诊断面板面向 APK 实机排障，横屏手机、折叠屏和桌面窗口尺寸差异较大。
            // 这里仅调整诊断面板自身的边距、触摸控件高度和滚动区最小高度，不触碰业务 UI 布局。
            Vector2 viewportSize = GetViewport()?.GetVisibleRect().Size ?? Size;
            bool compact = viewportSize.X < 900 || viewportSize.Y < 620;
            int outerMargin = compact ? 8 : 12;
            int rootGap = compact ? 8 : 10;
            int listGap = compact ? 8 : 12;

            if (panelMargin != null)
                ApplyMargin(panelMargin, outerMargin, outerMargin);
            ApplyTabListSpacing(listGap);
            if (titleLabel != null)
                titleLabel.AddThemeFontSizeOverride("font_size", compact ? 18 : 20);
            if (optionTabs != null)
            {
                // 企业级说明：这里使用较小的最小高度，让外层 Container 根据可用屏幕分配剩余空间。
                // 不能把 ScrollContainer/TabContainer 写成固定大高度，否则手机横屏下会把启动器页面撑出屏幕。
                int minHeight = FloatingMode
                    ? (compact ? 120 : 160)
                    : (compact ? 140 : 180);
                optionTabs.CustomMinimumSize = new Vector2(0, minHeight);
                ApplyTabScrollMinimumHeight(minHeight);
            }

            if (FloatingMode && viewportSize.X > 0)
            {
                float maxWidth = Mathf.Max(360.0f, viewportSize.X - outerMargin * 2.0f);
                CustomMinimumSize = new Vector2(Mathf.Min(520.0f, maxWidth), 220);
            }

            var root = panelMargin?.GetChildCount() > 0 ? panelMargin.GetChild(0) as VBoxContainer : null;
            root?.AddThemeConstantOverride("separation", rootGap);
        }

        void LoadConfigIntoPanel()
        {
            var loadResult = RuntimeDiagnosticsConfigLoader.Load();
            config = loadResult.Config ?? RuntimeDiagnosticsConfig.CreateDefault();
            RebuildOptions();
            SetStatus(loadResult.FileFound
                ? "已读取配置：" + BuildPathDisplay(loadResult.LoadedFrom)
                : "未找到配置文件，当前显示默认值。保存后会生成 " + BuildPathDisplay(RuntimeDiagnosticsConfigWriter.UserConfigPath));
            if (!string.IsNullOrEmpty(loadResult.ErrorMessage))
                SetStatus(loadResult.ErrorMessage);
            quickOptionsDirty = false;
            expertOptionsDirty = false;
            RefreshStats();
        }

        void RebuildOptions()
        {
            ClearChildren(optionTabs);
            optionRoot = null;
            consoleUserPathLabel = null;
            consoleTextEdit = null;

            BeginTab("快捷");
            BuildQuickDebugTab();

            BeginTab("GDPrint");
            BuildGdPrintTab();

            BeginTab("基础");
            AddSection("基础开关", "先启用调试档，再打开具体模块。APK 排查时只开启正在定位的最小范围。");
            AddChoice("active_debug_model", "当前调试档",
                "选择 zh_cn/jp/en 三个诊断入口之一。只影响当前使用哪组 debug_model 配置。",
                new[] { "debug_model_zh_cn", "debug_model_jp", "debug_model_en" },
                () => config.ActiveDebugModel,
                value => config.ActiveDebugModel = value);
            AddCheck("[debug_model_zh_cn] enabled", "启用中文调试档",
                "打开后使用中文诊断档的 log_level、mirror_to_godot、scroll_trace；关闭时 APK 默认只保留 error。",
                () => config.DebugModelZhCn.Enabled,
                value => config.DebugModelZhCn.Enabled = value);
            AddCheck("[debug_model_jp] enabled", "启用日文调试档",
                "用于对照日文资源或前辈模拟器行为。通常不作为手机默认配置。",
                () => config.DebugModelJp.Enabled,
                value => config.DebugModelJp.Enabled = value);
            AddCheck("[debug_model_en] enabled", "启用英文调试档",
                "用于生成英文报告或交给外部工具分析。通常不作为手机默认配置。",
                () => config.DebugModelEn.Enabled,
                value => config.DebugModelEn.Enabled = value);
            AddChoice("[debug_model_zh_cn] log_level", "中文调试档等级",
                "debug 记录最多，error 最适合 APK 常规游玩。开启 debug 前请确认只打开必要模块。",
                new[] { "error", "warn", "info", "debug", "none" },
                () => config.DebugModelZhCn.LogLevel,
                value => config.DebugModelZhCn.LogLevel = value);
            AddCheck("[debug_model_zh_cn] mirror_to_godot", "镜像到 Godot 控制台",
                "打开后非 Error 日志也会输出到 Godot 控制台，便于桌面调试；手机上可能产生刷屏成本。",
                () => config.DebugModelZhCn.MirrorToGodot,
                value => config.DebugModelZhCn.MirrorToGodot = value);
            AddCheck("[debug_model_zh_cn] scroll_trace", "旧滚动追踪",
                "开启旧 ScrollTrace 链路，可观察滚动与核心行窗口；日志量较大，建议短时间使用。",
                () => config.DebugModelZhCn.ScrollTrace,
                value => config.DebugModelZhCn.ScrollTrace = value);

            AddSection("全局日志", "控制最低等级、类别、ring buffer 和限流。");
            AddChoice("[logging] level", "默认最低等级",
                "debug/info/warn/error/none。debug_model 未启用时使用该值；APK 默认 error。",
                new[] { "error", "warn", "info", "debug", "none" },
                () => config.LoggingLevel,
                value => config.LoggingLevel = value);
            AddCheck("[logging] mirror_non_error_to_godot", "全局镜像非 Error",
                "将 debug/info/warn 同步到 Godot 控制台。手机实机一般关闭，只在桌面排查时打开。",
                () => config.LoggingMirrorNonErrorToGodot,
                value => config.LoggingMirrorNonErrorToGodot = value);
            AddInt("[logging] diagnostic_ring_capacity", "ring buffer 容量",
                "内存中保留的最近日志数量。修改后建议重启生效；容量越大越占内存。",
                64, 10000, 64,
                () => config.LoggingDiagnosticRingCapacity,
                value => config.LoggingDiagnosticRingCapacity = value);
            AddCheck("[logging.rate_limit] enabled", "启用限流",
                "限制每帧和每事件日志数量，防止触摸/布局/图片日志把手机帧率打爆。",
                () => config.RateLimitEnabled,
                value => config.RateLimitEnabled = value);
            AddInt("[logging.rate_limit] per_event_per_second", "单事件每秒上限",
                "每个 event_id 每秒最多记录多少条。数值越高，复盘越详细，移动端开销也越高。",
                1, 1000, 1,
                () => config.RateLimitPerEventPerSecond,
                value => config.RateLimitPerEventPerSecond = value);
            AddInt("[logging.rate_limit] default_per_frame", "每帧总日志上限",
                "限制一帧内写入 ring buffer 的总量。手机建议保持较低，避免一帧内大量分配。",
                1, 1000, 1,
                () => config.RateLimitDefaultPerFrame,
                value => config.RateLimitDefaultPerFrame = value);
            AddInt("[logging.rate_limit] default_per_second", "全局每秒上限",
                "所有事件合计的每秒建议上限，作为后续扩展的全局保护预算。",
                1, 5000, 10,
                () => config.RateLimitDefaultPerSecond,
                value => config.RateLimitDefaultPerSecond = value);
            AddInt("[logging] max_message_chars", "单条消息最大字符数",
                "限制异常长 message，防止脚本长行、路径列表或堆栈信息撑爆诊断文件。",
                128, 65536, 128,
                () => config.LoggingMaxMessageChars,
                value => config.LoggingMaxMessageChars = value);
            AddText("[logging] export_directory", "诊断导出目录",
                "默认 game://，表示本次启动的游戏目录。APK 中不要写入 res://，否则导出会失败。",
                () => config.LoggingExportDirectory,
                value => config.LoggingExportDirectory = value);
            AddText("[logging] session_id_format", "会话 ID 时间格式",
                "用于生成每次启动的 session_id。建议保持 yyyyMMdd-HHmmss，便于按时间排序。",
                () => config.LoggingSessionIdFormat,
                value => config.LoggingSessionIdFormat = value);

            AddSection("关联与脱敏", "关联字段帮助复盘一次输入、渲染批次、图片和行片段；脱敏字段控制隐私与日志长度。");
            AddCheck("[logging.correlation] enabled", "启用关联字段",
                "关闭后诊断报告更短，但较难把输入、图片、UI 布局和脚本行串起来。",
                () => config.CorrelationEnabled,
                value => config.CorrelationEnabled = value);
            AddCheck("[logging.correlation] session_id", "记录会话 ID",
                "用于区分不同启动会话，建议保持开启。",
                () => config.CorrelationSessionId,
                value => config.CorrelationSessionId = value);
            AddCheck("[logging.correlation] input_id", "记录输入 ID",
                "用于把一次触摸/按钮/键盘输入与后续日志关联。",
                () => config.CorrelationInputId,
                value => config.CorrelationInputId = value);
            AddCheck("[logging.correlation] render_batch_id", "记录渲染批次 ID",
                "用于定位某批 EmueraContent 输出对应的 UI/图片日志。",
                () => config.CorrelationRenderBatchId,
                value => config.CorrelationRenderBatchId = value);
            AddCheck("[logging.correlation] line_part_id", "记录行片段 ID",
                "用于未来把 ERB 输出行片段与实际 UI 控件绑定。",
                () => config.CorrelationLinePartId,
                value => config.CorrelationLinePartId = value);
            AddCheck("[logging.correlation] image_id", "记录图片 ID",
                "用于把图片路径解析、纹理加载和 UI 矩形串联。",
                () => config.CorrelationImageId,
                value => config.CorrelationImageId = value);
            AddCheck("[logging.redaction] enabled", "启用脱敏",
                "关闭会输出更多原始文本和路径，不建议在手机问题包中关闭。",
                () => config.RedactionEnabled,
                value => config.RedactionEnabled = value);
            AddCheck("[logging.redaction] normalize_paths", "规范化路径",
                "把路径格式统一，减少 Windows/Android 路径差异对比成本。",
                () => config.RedactionNormalizePaths,
                value => config.RedactionNormalizePaths = value);
            AddText("[logging.redaction] path_mode", "路径脱敏模式",
                "默认 basename_and_root，只保留定位所需的根和文件名信息。",
                () => config.RedactionPathMode,
                value => config.RedactionPathMode = value);
            AddInt("[logging.redaction] max_path_chars", "路径最大字符数",
                "超过该长度会截断，避免深层 Android 路径刷屏。",
                16, 2048, 8,
                () => config.RedactionMaxPathChars,
                value => config.RedactionMaxPathChars = value);
            AddInt("[logging.redaction] max_script_text_chars", "脚本文本最大字符数",
                "ERB/表达式文本的截断长度。数值越大，隐私和日志体积风险越高。",
                16, 4096, 8,
                () => config.RedactionMaxScriptTextChars,
                value => config.RedactionMaxScriptTextChars = value);
            AddInt("[logging.redaction] max_user_text_chars", "用户输入最大字符数",
                "输入日志只保留摘要，不记录完整用户输入。建议保持较低。",
                0, 512, 4,
                () => config.RedactionMaxUserTextChars,
                value => config.RedactionMaxUserTextChars = value);
            AddCheck("[logging.redaction] replace_newlines", "替换换行符",
                "把多行文本压成单行，避免结构化日志被脚本文本破坏。",
                () => config.RedactionReplaceNewlines,
                value => config.RedactionReplaceNewlines = value);
            AddCheck("[logging.redaction] hash_sensitive_text", "敏感文本哈希",
                "预留选项，用于后续把敏感文本转为哈希而非明文输出。",
                () => config.RedactionHashSensitiveText,
                value => config.RedactionHashSensitiveText = value);

            AddSection("一级类别", "类别开关决定哪些模块的 debug/info/warn 可以通过路由。");
            AddCheck("[logging.categories] touch", "触摸类别",
                "允许 TOUCH.* 日志通过。还需要开启 debug.touch 的具体子开关。",
                () => config.Categories.Touch,
                value => config.Categories.Touch = value);
            AddCheck("[logging.categories] input", "输入类别",
                "允许 INPUT.* 日志通过，用于排查输入提交和消费。",
                () => config.Categories.Input,
                value => config.Categories.Input = value);
            AddCheck("[logging.categories] sprite", "图片/精灵类别",
                "允许图片解析、纹理解码、渲染目标等日志通过。",
                () => config.Categories.Sprite,
                value => config.Categories.Sprite = value);
            AddCheck("[logging.categories] ui", "UI 类别",
                "允许 UI_LAYOUT.* 等界面几何诊断通过。",
                () => config.Categories.UI,
                value => config.Categories.UI = value);
            AddCheck("[logging.categories] script", "脚本类别",
                "允许脚本/旧滚动追踪相关日志通过。核心执行路径较热，谨慎开启 debug。",
                () => config.Categories.Script,
                value => config.Categories.Script = value);
            AddCheck("[logging.categories] statement_recognition", "ERB 语句识别类别",
                "允许解析/识别诊断通过。该类日志可能极密集，默认关闭。",
                () => config.Categories.StatementRecognition,
                value => config.Categories.StatementRecognition = value);
            AddCheck("[logging.categories] performance", "性能类别",
                "允许启动耗时、队列、采样类日志通过。",
                () => config.Categories.Performance,
                value => config.Categories.Performance = value);

            BeginTab("热路径");
            AddSection("触摸日志", "排查手机点击、拖动、双指缩放、惯性滚动。");
            AddCheck("[debug.touch] enabled", "触摸总开关",
                "总开关关闭时 pointer/drag/pinch/inertia 全部无效。默认关闭以保护手机帧率。",
                () => config.TouchEnabled,
                value => config.TouchEnabled = value);
            AddCheck("[debug.touch] pointer", "点击按下/释放",
                "记录 TOUCH.POINTER.PRESS/RELEASE，用于排查按钮点击、空白点击和输入门控。",
                () => config.TouchPointer,
                value => config.TouchPointer = value);
            AddCheck("[debug.touch] drag", "拖动滚动",
                "记录 TOUCH.DRAG.START/MOVE。MOVE 会按间隔采样，仍可能较多。",
                () => config.TouchDrag,
                value => config.TouchDrag = value);
            AddCheck("[debug.touch] pinch", "双指缩放",
                "记录 TOUCH.PINCH.START/END，用于检查多点触控是否进入缩放模式。",
                () => config.TouchPinch,
                value => config.TouchPinch = value);
            AddCheck("[debug.touch] inertia", "惯性滚动",
                "记录 TOUCH.INERTIA.START/STOP，用于排查松手后滚动速度和停止条件。",
                () => config.TouchInertia,
                value => config.TouchInertia = value);
            AddCheck("[debug.touch] scroll", "滚动状态",
                "预留触摸滚动细分开关，用于后续把滚动修正、边界和回弹单独控制。",
                () => config.TouchScroll,
                value => config.TouchScroll = value);
            AddInt("[debug.touch] drag_interval_ms", "拖动采样间隔 ms",
                "drag move 日志的最小间隔。数值越小越详细，手机日志量越大。",
                16, 2000, 1,
                () => config.TouchDragIntervalMs,
                value => config.TouchDragIntervalMs = value);
            AddInt("[debug.touch] core_burst_line_count", "核心行突发数量",
                "旧 ScrollTrace 打开后，输入附近最多追踪多少条核心行。数值越大，脚本热路径日志越多。",
                0, 2000, 10,
                () => config.TouchCoreBurstLineCount,
                value => config.TouchCoreBurstLineCount = value);

            AddSection("语句识别日志", "默认仅保留配置项。未明确定位到解析问题前，不建议开启表达式/变量/函数调用。");
            AddCheck("[debug.statement_recognition] enabled", "语句识别总开关",
                "控制 ERB 加载、逻辑行、表达式、变量和函数识别日志。",
                () => config.StatementRecognitionEnabled,
                value => config.StatementRecognitionEnabled = value);
            AddCheck("[debug.statement_recognition] erb_load", "ERB 加载识别",
                "用于定位 ERB 文件加载、编码和预处理阶段的问题。",
                () => config.StatementRecognitionErbLoad,
                value => config.StatementRecognitionErbLoad = value);
            AddCheck("[debug.statement_recognition] logical_line", "逻辑行识别",
                "用于观察脚本逻辑行切分。日志可能较密集。",
                () => config.StatementRecognitionLogicalLine,
                value => config.StatementRecognitionLogicalLine = value);
            AddCheck("[debug.statement_recognition] expression", "表达式识别",
                "用于定位表达式解析。该路径很热，手机实机只建议短时开启。",
                () => config.StatementRecognitionExpression,
                value => config.StatementRecognitionExpression = value);
            AddCheck("[debug.statement_recognition] variable", "变量识别",
                "用于定位变量名、数组和作用域识别问题。可能产生大量日志。",
                () => config.StatementRecognitionVariable,
                value => config.StatementRecognitionVariable = value);
            AddCheck("[debug.statement_recognition] function_call", "函数调用识别",
                "用于定位函数/命令识别问题。开启前请确认需要追踪的范围。",
                () => config.StatementRecognitionFunctionCall,
                value => config.StatementRecognitionFunctionCall = value);
            AddInt("[debug.statement_recognition] max_lines_per_burst", "每次突发最大行数",
                "限制一次解析诊断最多记录多少行，防止 ERB 热路径刷屏。",
                1, 5000, 10,
                () => config.StatementRecognitionMaxLinesPerBurst,
                value => config.StatementRecognitionMaxLinesPerBurst = value);

            AddSection("输入日志", "排查按钮/键盘/触摸转成 Emuera 输入后的流向。");
            AddCheck("[debug.input] enabled", "输入总开关",
                "控制 INPUT.SUBMIT/CONSUME 等结构化日志。",
                () => config.InputDebugEnabled,
                value => config.InputDebugEnabled = value);
            AddCheck("[debug.input] submit", "提交输入",
                "记录输入进入模拟器线程前的摘要，不记录完整用户文本。",
                () => config.InputDebugSubmit,
                value => config.InputDebugSubmit = value);
            AddCheck("[debug.input] consume", "消费输入",
                "记录核心等待输入时是否取走了提交值。",
                () => config.InputDebugConsume,
                value => config.InputDebugConsume = value);
            AddCheck("[debug.input] button", "按钮输入",
                "预留按钮链路开关，用于后续更细粒度按钮诊断。",
                () => config.InputDebugButton,
                value => config.InputDebugButton = value);

            BeginTab("资源性能");
            AddSection("加载/存档/资源/性能", "这些是低频流程开关，主要用于启动、读写、资源加载阶段定位。");
            AddCheck("[debug.performance] enabled", "性能诊断总开关",
                "控制启动耗时、UI 队列、资源加载等性能类日志。",
                () => config.PerformanceEnabled,
                value => config.PerformanceEnabled = value);
            AddCheck("[debug.performance] startup_timing", "启动耗时",
                "记录启动阶段耗时，适合定位 APK 首屏或加载慢。",
                () => config.PerformanceStartupTiming,
                value => config.PerformanceStartupTiming = value);
            AddCheck("[debug.performance] ui_queue", "UI 队列",
                "记录 UI 任务队列状态，用于定位输出卡顿或主线程积压。",
                () => config.PerformanceUiQueue,
                value => config.PerformanceUiQueue = value);
            AddCheck("[debug.performance] resource_loading", "资源加载耗时",
                "记录资源加载阶段摘要，用于定位图片、音频、数据库加载慢。",
                () => config.PerformanceResourceLoading,
                value => config.PerformanceResourceLoading = value);
            AddCheck("[debug.load] enabled", "加载诊断总开关",
                "控制 ERB/CSV/resources 加载流程诊断。",
                () => config.LoadDebugEnabled,
                value => config.LoadDebugEnabled = value);
            AddCheck("[debug.load] erb", "ERB 加载",
                "用于定位 ERB 文件发现、读取和解析入口问题。",
                () => config.LoadDebugErb,
                value => config.LoadDebugErb = value);
            AddCheck("[debug.load] csv", "CSV 加载",
                "用于定位 CSV 数据读取问题。",
                () => config.LoadDebugCsv,
                value => config.LoadDebugCsv = value);
            AddCheck("[debug.load] resources", "resources 加载",
                "用于定位 resources 目录扫描和资源发现问题。",
                () => config.LoadDebugResources,
                value => config.LoadDebugResources = value);
            AddCheck("[debug.save] enabled", "存档诊断总开关",
                "控制存档读写和兼容回退日志。",
                () => config.SaveDebugEnabled,
                value => config.SaveDebugEnabled = value);
            AddCheck("[debug.save] read", "读档",
                "记录读档入口和失败摘要，不记录完整存档内容。",
                () => config.SaveDebugRead,
                value => config.SaveDebugRead = value);
            AddCheck("[debug.save] write", "写档",
                "记录写档入口和失败摘要。手机上只建议排查时开启。",
                () => config.SaveDebugWrite,
                value => config.SaveDebugWrite = value);
            AddCheck("[debug.save] compatibility_fallback", "兼容回退",
                "记录旧格式或兼容路径回退，用于定位不同游戏存档兼容问题。",
                () => config.SaveDebugCompatibilityFallback,
                value => config.SaveDebugCompatibilityFallback = value);
            AddCheck("[debug.resource] enabled", "资源诊断总开关",
                "控制 sprite/audio/sqlite 等资源层日志。",
                () => config.ResourceDebugEnabled,
                value => config.ResourceDebugEnabled = value);
            AddCheck("[debug.resource] sprite", "Sprite 资源",
                "用于定位图片资源缓存、占位图和路径问题。",
                () => config.ResourceDebugSprite,
                value => config.ResourceDebugSprite = value);
            AddCheck("[debug.resource] audio", "Audio 资源",
                "用于定位音频资源发现和加载问题。",
                () => config.ResourceDebugAudio,
                value => config.ResourceDebugAudio = value);
            AddCheck("[debug.resource] sqlite", "SQLite 资源",
                "用于定位数据库资源初始化和访问问题。",
                () => config.ResourceDebugSqlite,
                value => config.ResourceDebugSqlite = value);

            AddSection("图片日志", "排查资源路径、纹理解码、渲染目标矩形和缓存。");
            AddCheck("[debug.image] enabled", "图片总开关",
                "总开关关闭时图片子项全部无效。默认关闭，避免图片热路径日志开销。",
                () => config.ImageDebugEnabled,
                value => config.ImageDebugEnabled = value);
            AddCheck("[debug.image] resolve", "路径解析失败",
                "记录 IMAGE.RESOLVE.FAIL，路径会脱敏，适合定位资源找不到。",
                () => config.ImageDebugResolve,
                value => config.ImageDebugResolve = value);
            AddCheck("[debug.image] texture", "纹理解码失败",
                "记录 IMAGE.TEXTURE.LOAD_FAIL，用于定位格式、损坏文件或平台解码问题。",
                () => config.ImageDebugTexture,
                value => config.ImageDebugTexture = value);
            AddCheck("[debug.image] render_rect", "图片目标矩形",
                "记录 IMAGE.RENDER.TARGET，用于对比 ERB 图片输出目标位置和尺寸。",
                () => config.ImageDebugRenderRect,
                value => config.ImageDebugRenderRect = value);
            AddCheck("[debug.image] cache", "图片缓存",
                "预留图片缓存命中/失效日志开关。缓存日志可能很密集。",
                () => config.ImageDebugCache,
                value => config.ImageDebugCache = value);
            AddCheck("[debug.image] color_matrix", "ColorMatrix",
                "预留图片色彩矩阵诊断开关，用于定位 GPU/CPU 色彩处理差异。",
                () => config.ImageDebugColorMatrix,
                value => config.ImageDebugColorMatrix = value);
            AddCheck("[debug.image] log_success", "记录成功加载",
                "记录 IMAGE.TEXTURE.LOAD_OK。日志量较大，只建议短时开启。",
                () => config.ImageDebugLogSuccess,
                value => config.ImageDebugLogSuccess = value);
            AddInt("[debug.image] max_records_per_frame", "图片每帧上限",
                "图片诊断每帧最多记录条数。值越高越详细，也越容易影响手机帧时间。",
                1, 200, 1,
                () => config.ImageDebugMaxRecordsPerFrame,
                value => config.ImageDebugMaxRecordsPerFrame = value);

            BeginTab("界面");
            AddSection("UI 几何日志", "对比 ERB/UI 语法目标位置与 EmueraContent 中实际 Control 位置。");
            AddCheck("[debug.ui_layout] enabled", "UI 几何总开关",
                "控制 UI_LAYOUT.* 日志。默认关闭，开启后会在下一帧采样实际 Control rect。",
                () => config.UiLayoutEnabled,
                value => config.UiLayoutEnabled = value);
            AddCheck("[debug.ui_layout] mismatch_only", "只记录差异",
                "开启时只记录超过阈值的 mismatch；关闭后可记录 target_rect 和 actual_rect。",
                () => config.UiLayoutMismatchOnly,
                value => config.UiLayoutMismatchOnly = value);
            AddCheck("[debug.ui_layout] target_rect", "记录目标矩形",
                "mismatch_only=false 时生效，记录 ERB/UI 生成时的目标矩形。",
                () => config.UiLayoutTargetRect,
                value => config.UiLayoutTargetRect = value);
            AddCheck("[debug.ui_layout] actual_rect", "记录实际矩形",
                "mismatch_only=false 时生效，记录 Godot 下一帧布局后的实际矩形。",
                () => config.UiLayoutActualRect,
                value => config.UiLayoutActualRect = value);
            AddInt("[debug.ui_layout] mismatch_threshold_px", "差异阈值 px",
                "任一坐标或尺寸差异超过该像素值时记录 UI_LAYOUT.MISMATCH。",
                0, 128, 1,
                () => config.UiLayoutMismatchThresholdPx,
                value => config.UiLayoutMismatchThresholdPx = value);
            AddCheck("[debug.ui_layout] button", "按钮几何",
                "跟踪 ERB 按钮目标矩形与实际按钮 rect。",
                () => config.UiLayoutButton,
                value => config.UiLayoutButton = value);
            AddCheck("[debug.ui_layout] image", "图片几何",
                "跟踪图片输出目标矩形与实际 TextureRect rect。",
                () => config.UiLayoutImage,
                value => config.UiLayoutImage = value);
            AddCheck("[debug.ui_layout] text", "文本几何预留",
                "预留文本布局追踪开关。当前主要覆盖按钮和图片。",
                () => config.UiLayoutText,
                value => config.UiLayoutText = value);
            AddCheck("[debug.ui_layout] shape", "形状几何预留",
                "预留形状/绘制命令目标矩形追踪开关。",
                () => config.UiLayoutShape,
                value => config.UiLayoutShape = value);
            AddCheck("[debug.ui_layout] html_div", "HTML div 几何预留",
                "预留 HTML div 转换后的 UI 几何追踪开关。",
                () => config.UiLayoutHtmlDiv,
                value => config.UiLayoutHtmlDiv = value);
            AddCheck("[debug.ui_layout] html_img", "HTML img 几何预留",
                "预留 HTML img 转换后的图片几何追踪开关。",
                () => config.UiLayoutHtmlImg,
                value => config.UiLayoutHtmlImg = value);
            AddCheck("[debug.ui_layout] cbg", "CBG 几何预留",
                "预留背景图层/CBG 相关矩形追踪开关。",
                () => config.UiLayoutCbg,
                value => config.UiLayoutCbg = value);
            AddCheck("[debug.ui_layout] include_text", "包含文本摘要",
                "开启后未来文本几何日志可带短文本摘要；默认关闭以避免隐私和日志体积风险。",
                () => config.UiLayoutIncludeText,
                value => config.UiLayoutIncludeText = value);
            AddInt("[debug.ui_layout] max_text_chars", "文本摘要最大字符数",
                "UI 文本摘要截断长度。只有 include_text=true 时才有意义。",
                0, 512, 4,
                () => config.UiLayoutMaxTextChars,
                value => config.UiLayoutMaxTextChars = value);
            AddInt("[debug.ui_layout] max_records_per_frame", "UI 每帧上限",
                "UI 几何诊断每帧最多记录条数。大量按钮页面建议保持较低。",
                1, 500, 1,
                () => config.UiLayoutMaxRecordsPerFrame,
                value => config.UiLayoutMaxRecordsPerFrame = value);

            AddSection("生命周期与可视覆盖层", "生命周期用于 Android 后台恢复；覆盖层用于未来可视化绘制矩形。");
            AddCheck("[debug.lifecycle] enabled", "生命周期总开关",
                "控制 Android pause/resume 等生命周期日志。",
                () => config.LifecycleEnabled,
                value => config.LifecycleEnabled = value);
            AddCheck("[debug.lifecycle] android_pause_resume", "Android 暂停/恢复",
                "记录 APK 进入后台和恢复前台，用于定位恢复后卡顿、输入失效等问题。",
                () => config.LifecycleAndroidPauseResume,
                value => config.LifecycleAndroidPauseResume = value);
            AddCheck("[debug.ui_overlay] enabled", "UI 覆盖层总开关",
                "预留可视化矩形覆盖层。默认关闭，避免遮挡手机实际画面。",
                () => config.UiOverlayEnabled,
                value => config.UiOverlayEnabled = value);
            AddCheck("[debug.ui_overlay] target_rect", "绘制目标矩形",
                "覆盖层显示 ERB/UI 目标矩形。",
                () => config.UiOverlayTargetRect,
                value => config.UiOverlayTargetRect = value);
            AddCheck("[debug.ui_overlay] actual_rect", "绘制实际矩形",
                "覆盖层显示 Godot 实际 Control 矩形。",
                () => config.UiOverlayActualRect,
                value => config.UiOverlayActualRect = value);
            AddCheck("[debug.ui_overlay] mismatch", "绘制差异矩形",
                "覆盖层只标出 mismatch，更适合手机小屏排查。",
                () => config.UiOverlayMismatch,
                value => config.UiOverlayMismatch = value);
            AddCheck("[debug.ui_overlay] image_rect", "绘制图片矩形",
                "覆盖层标记图片节点矩形。",
                () => config.UiOverlayImageRect,
                value => config.UiOverlayImageRect = value);
            AddCheck("[debug.ui_overlay] button_rect", "绘制按钮矩形",
                "覆盖层标记按钮节点矩形。",
                () => config.UiOverlayButtonRect,
                value => config.UiOverlayButtonRect = value);
            AddInt("[debug.ui_overlay] max_drawn_rects", "最大绘制矩形数",
                "限制覆盖层一帧最多绘制多少个矩形，防止复杂页面过度绘制。",
                1, 2000, 8,
                () => config.UiOverlayMaxDrawnRects,
                value => config.UiOverlayMaxDrawnRects = value);

            AddSection("运行时悬浮窗", "这是计划文档中的 P0 项。开启后主运行场景会显示日志配置悬浮窗。");
            AddCheck("[debug.runtime_panel] enabled", "显示悬浮窗",
                "开启后进入游戏主场景时自动挂载日志诊断悬浮窗入口。",
                () => config.RuntimePanelEnabled,
                value => config.RuntimePanelEnabled = value);
            AddCheck("[debug.runtime_panel] allow_runtime_toggle", "允许运行时切换",
                "允许在运行中保存并热重载轻量日志开关。关闭后应只允许下次启动生效。",
                () => config.RuntimePanelAllowRuntimeToggle,
                value => config.RuntimePanelAllowRuntimeToggle = value);
            AddCheck("[debug.runtime_panel] persist_changes", "保留运行时改动",
                "开启表示面板改动会写入 user://config.toml。当前保存按钮总是显式持久化。",
                () => config.RuntimePanelPersistChanges,
                value => config.RuntimePanelPersistChanges = value);
            AddCheck("[debug.runtime_panel] show_ring_buffer_stats", "显示 ring buffer 状态",
                "显示当前日志环形缓冲区数量、容量和覆盖次数，便于判断是否被日志刷满。",
                () => config.RuntimePanelShowRingBufferStats,
                value => config.RuntimePanelShowRingBufferStats = value);
            AddCheck("[debug.runtime_panel] show_active_modules", "显示启用模块",
                "用于后续在悬浮窗顶部展示当前开启的日志模块摘要。",
                () => config.RuntimePanelShowActiveModules,
                value => config.RuntimePanelShowActiveModules = value);

            BeginTab("导出");
            AddSection("诊断包与保留策略", "用于把问题现场打包给人工或 AI 复盘。");
            AddCheck("[diagnostic_package] enabled", "允许导出诊断包",
                "开启后可导出日志、配置快照、设备信息和摘要。导出由用户主动触发。",
                () => config.DiagnosticPackageEnabled,
                value => config.DiagnosticPackageEnabled = value);
            AddCheck("[diagnostic_package] include_config_snapshot", "包含配置快照",
                "诊断包中写入当前配置摘要，方便确认当时开了哪些日志模块。",
                () => config.DiagnosticPackageIncludeConfigSnapshot,
                value => config.DiagnosticPackageIncludeConfigSnapshot = value);
            AddCheck("[diagnostic_package] include_log", "包含日志",
                "诊断包中包含 ring buffer 导出的结构化日志。",
                () => config.DiagnosticPackageIncludeLog,
                value => config.DiagnosticPackageIncludeLog = value);
            AddCheck("[diagnostic_package] include_device_info", "包含设备信息",
                "写入平台、设备和运行环境摘要，便于区分手机型号和桌面环境。",
                () => config.DiagnosticPackageIncludeDeviceInfo,
                value => config.DiagnosticPackageIncludeDeviceInfo = value);
            AddCheck("[diagnostic_package] include_screen_info", "包含屏幕信息",
                "写入分辨率、窗口和屏幕信息，便于定位布局问题。",
                () => config.DiagnosticPackageIncludeScreenInfo,
                value => config.DiagnosticPackageIncludeScreenInfo = value);
            AddCheck("[diagnostic_package] include_godot_info", "包含 Godot 信息",
                "写入 Godot 版本和构建环境摘要。",
                () => config.DiagnosticPackageIncludeGodotInfo,
                value => config.DiagnosticPackageIncludeGodotInfo = value);
            AddCheck("[diagnostic_package] include_apk_info", "包含 APK 信息",
                "写入 APK/平台相关摘要，用于区分桌面和手机导出行为。",
                () => config.DiagnosticPackageIncludeApkInfo,
                value => config.DiagnosticPackageIncludeApkInfo = value);
            AddCheck("[diagnostic_package] include_game_path", "包含游戏路径",
                "记录脱敏后的游戏目录信息，便于定位 Android 路径和资源根目录。",
                () => config.DiagnosticPackageIncludeGamePath,
                value => config.DiagnosticPackageIncludeGamePath = value);
            AddCheck("[diagnostic_package] include_error_summary", "包含错误摘要",
                "输出错误计数和高频事件摘要，方便快速判断问题集中点。",
                () => config.DiagnosticPackageIncludeErrorSummary,
                value => config.DiagnosticPackageIncludeErrorSummary = value);
            AddCheck("[diagnostic_summary] enabled", "启用摘要",
                "导出时生成 Top 事件、UI mismatch 和图片失败摘要。",
                () => config.DiagnosticSummaryEnabled,
                value => config.DiagnosticSummaryEnabled = value);
            AddInt("[diagnostic_summary] top_event_ids", "Top 事件数量",
                "导出摘要中列出多少个高频 event_id。",
                1, 200, 1,
                () => config.DiagnosticSummaryTopEventIds,
                value => config.DiagnosticSummaryTopEventIds = value);
            AddInt("[diagnostic_summary] top_ui_layout_mismatches", "Top UI 差异数量",
                "导出摘要中列出多少个 UI_LAYOUT.MISMATCH。",
                1, 200, 1,
                () => config.DiagnosticSummaryTopUiLayoutMismatches,
                value => config.DiagnosticSummaryTopUiLayoutMismatches = value);
            AddInt("[diagnostic_summary] top_image_failures", "Top 图片失败数量",
                "导出摘要中列出多少个图片失败事件。",
                1, 200, 1,
                () => config.DiagnosticSummaryTopImageFailures,
                value => config.DiagnosticSummaryTopImageFailures = value);
            AddCheck("[debug.reference] enabled", "参考模拟器标记",
                "启用后可在诊断报告中记录 uEmuera/XEmuera 等对照来源。",
                () => config.ReferenceEnabled,
                value => config.ReferenceEnabled = value);
            AddText("[debug.reference] reference_name", "参考名称",
                "例如 uEmuera-0.2.9d 或 XEmuera-0.5.1，用于报告对照。",
                () => config.ReferenceName,
                value => config.ReferenceName = value);
            AddCheck("[debug.reference] expected_from_reference", "参考期望行为",
                "标记当前日志是否用于对照前辈模拟器的期望行为。",
                () => config.ReferenceExpectedFromReference,
                value => config.ReferenceExpectedFromReference = value);
            AddCheck("[debug.reference] reference_note", "参考备注",
                "预留字段，用于后续在报告中附带参考差异说明。",
                () => config.ReferenceNote,
                value => config.ReferenceNote = value);
            AddCheck("[diagnostic_breadcrumb] enabled", "启用面包屑",
                "写入少量关键生命周期信息，作为崩溃或无法导出完整日志时的保底现场。",
                () => config.BreadcrumbEnabled,
                value => config.BreadcrumbEnabled = value);
            AddText("[diagnostic_breadcrumb] path", "面包屑路径",
                "默认 game://gemuera-last-session-breadcrumb.log，即启动游戏目录下的保底日志。",
                () => config.BreadcrumbPath,
                value => config.BreadcrumbPath = value);
            AddInt("[diagnostic_breadcrumb] max_bytes", "面包屑最大字节",
                "限制保底日志大小，防止长期运行无限增长。",
                1024, 1048576, 1024,
                () => config.BreadcrumbMaxBytes,
                value => config.BreadcrumbMaxBytes = value);
            AddCheck("[diagnostic_breadcrumb] write_on_startup", "启动时写面包屑",
                "记录应用启动关键点。",
                () => config.BreadcrumbWriteOnStartup,
                value => config.BreadcrumbWriteOnStartup = value);
            AddCheck("[diagnostic_breadcrumb] write_on_config_loaded", "配置加载后写面包屑",
                "记录配置加载结果，便于判断是否读取了 user://config.toml。",
                () => config.BreadcrumbWriteOnConfigLoaded,
                value => config.BreadcrumbWriteOnConfigLoaded = value);
            AddCheck("[diagnostic_breadcrumb] write_on_game_path_selected", "选择游戏路径后写面包屑",
                "记录脱敏后的游戏路径选择事件。",
                () => config.BreadcrumbWriteOnGamePathSelected,
                value => config.BreadcrumbWriteOnGamePathSelected = value);
            AddCheck("[diagnostic_breadcrumb] write_on_severe_error", "严重错误时写面包屑",
                "Error 级别问题发生时写入保底记录。",
                () => config.BreadcrumbWriteOnSevereError,
                value => config.BreadcrumbWriteOnSevereError = value);
            AddCheck("[diagnostic_breadcrumb] write_on_export", "导出前后写面包屑",
                "记录诊断日志导出动作。",
                () => config.BreadcrumbWriteOnExport,
                value => config.BreadcrumbWriteOnExport = value);
            AddCheck("[diagnostic_breadcrumb] write_on_shutdown", "退出时写面包屑",
                "预留退出阶段记录，用于定位关闭/返回标题问题。",
                () => config.BreadcrumbWriteOnShutdown,
                value => config.BreadcrumbWriteOnShutdown = value);
            AddCheck("[debug.input_replay] enabled", "启用输入复现缓冲",
                "保存最近 N 条输入摘要，帮助复现点击/按钮/等待状态问题。",
                () => config.InputReplayEnabled,
                value => config.InputReplayEnabled = value);
            AddInt("[debug.input_replay] max_events", "输入复现事件数",
                "保留最近多少条输入摘要。数值越大，占用内存越多。",
                1, 5000, 10,
                () => config.InputReplayMaxEvents,
                value => config.InputReplayMaxEvents = value);
            AddCheck("[debug.input_replay] capture_touch", "捕获触摸",
                "记录触摸输入摘要。",
                () => config.InputReplayCaptureTouch,
                value => config.InputReplayCaptureTouch = value);
            AddCheck("[debug.input_replay] capture_button", "捕获按钮",
                "记录 UI 按钮输入摘要。",
                () => config.InputReplayCaptureButton,
                value => config.InputReplayCaptureButton = value);
            AddCheck("[debug.input_replay] capture_keyboard", "捕获键盘",
                "记录键盘输入摘要，不记录完整文本。",
                () => config.InputReplayCaptureKeyboard,
                value => config.InputReplayCaptureKeyboard = value);
            AddCheck("[debug.input_replay] capture_wait_state", "捕获等待状态",
                "记录当时是否处于等待输入/等待回车/任意键状态。",
                () => config.InputReplayCaptureWaitState,
                value => config.InputReplayCaptureWaitState = value);
            AddCheck("[debug.input_replay] capture_timing", "捕获时间",
                "记录输入相对时间，便于复现触摸节奏。",
                () => config.InputReplayCaptureTiming,
                value => config.InputReplayCaptureTiming = value);
            AddInt("[debug.input_replay] max_text_chars", "输入文本摘要长度",
                "输入复现中保留的文本最大长度。建议较低。",
                0, 512, 4,
                () => config.InputReplayMaxTextChars,
                value => config.InputReplayMaxTextChars = value);
            AddCheck("[diagnostic_retention] enabled", "启用诊断保留策略",
                "限制启动游戏目录下 gEmuera 诊断文件的数量和总大小。",
                () => config.RetentionEnabled,
                value => config.RetentionEnabled = value);
            AddText("[diagnostic_retention] directory", "诊断保留目录",
                "默认 game://，表示启动游戏目录。清理逻辑只处理 gEmuera 诊断文件，不会清理游戏资源。",
                () => config.RetentionDirectory,
                value => config.RetentionDirectory = value);
            AddInt("[diagnostic_retention] max_packages", "最大诊断包数量",
                "超过数量后清理旧诊断包。",
                1, 1000, 1,
                () => config.RetentionMaxPackages,
                value => config.RetentionMaxPackages = value);
            AddInt("[diagnostic_retention] max_total_mb", "最大总大小 MB",
                "限制诊断目录总占用，防止手机存储被日志填满。",
                1, 4096, 1,
                () => config.RetentionMaxTotalMb,
                value => config.RetentionMaxTotalMb = value);
            AddCheck("[diagnostic_retention] cleanup_on_startup", "启动时清理",
                "应用启动时按保留策略清理旧诊断文件。",
                () => config.RetentionCleanupOnStartup,
                value => config.RetentionCleanupOnStartup = value);
            AddCheck("[diagnostic_retention] cleanup_before_export", "导出前清理",
                "导出新诊断包前先清理旧文件。",
                () => config.RetentionCleanupBeforeExport,
                value => config.RetentionCleanupBeforeExport = value);
            AddCheck("[diagnostic_retention] protect_current_session", "保护当前会话",
                "清理旧文件时尽量不删除当前会话相关文件。",
                () => config.RetentionProtectCurrentSession,
                value => config.RetentionProtectCurrentSession = value);
            AddCheck("[debug.android_storage] enabled", "Android 存储诊断",
                "记录权限、目录扫描、路径选择和读写失败摘要，用于排查手机 scoped storage 问题。",
                () => config.AndroidStorageEnabled,
                value => config.AndroidStorageEnabled = value);
            AddCheck("[debug.android_storage] log_permissions", "记录权限状态",
                "记录 Android 文件访问权限检查和请求结果。",
                () => config.AndroidStorageLogPermissions,
                value => config.AndroidStorageLogPermissions = value);
            AddCheck("[debug.android_storage] log_game_scan", "记录游戏扫描",
                "记录游戏目录扫描摘要，用于定位路径层级和目录识别问题。",
                () => config.AndroidStorageLogGameScan,
                value => config.AndroidStorageLogGameScan = value);
            AddCheck("[debug.android_storage] log_path_selection", "记录路径选择",
                "记录用户选择或自动恢复的游戏路径摘要。",
                () => config.AndroidStorageLogPathSelection,
                value => config.AndroidStorageLogPathSelection = value);
            AddCheck("[debug.android_storage] log_read_write_failures", "记录读写失败",
                "记录 Android 文件读写失败摘要，帮助定位 scoped storage 限制。",
                () => config.AndroidStorageLogReadWriteFailures,
                value => config.AndroidStorageLogReadWriteFailures = value);
            AddCheck("[debug.android_storage] log_scoped_storage", "记录 scoped storage",
                "记录与 Android 分区存储相关的路径/权限判断。",
                () => config.AndroidStorageLogScopedStorage,
                value => config.AndroidStorageLogScopedStorage = value);
            AddInt("[debug.android_storage] max_path_records", "最大路径记录数",
                "限制一次扫描中最多保留多少条路径摘要。",
                1, 5000, 1,
                () => config.AndroidStorageMaxPathRecords,
                value => config.AndroidStorageMaxPathRecords = value);
            AddCheck("[debug.performance_sampling] enabled", "性能采样",
                "按固定间隔记录 FPS、帧时间、队列和 ring buffer 摘要。默认关闭。",
                () => config.PerformanceSamplingEnabled,
                value => config.PerformanceSamplingEnabled = value);
            AddInt("[debug.performance_sampling] sample_interval_ms", "采样间隔 ms",
                "性能采样间隔。手机建议 1000ms 或更长，避免采样本身成为噪声。",
                250, 10000, 250,
                () => config.PerformanceSamplingIntervalMs,
                value => config.PerformanceSamplingIntervalMs = value);
            AddCheck("[debug.performance_sampling] include_fps", "采样 FPS",
                "在性能采样中记录 FPS。",
                () => config.PerformanceSamplingIncludeFps,
                value => config.PerformanceSamplingIncludeFps = value);
            AddCheck("[debug.performance_sampling] include_frame_ms", "采样帧耗时",
                "在性能采样中记录帧耗时摘要。",
                () => config.PerformanceSamplingIncludeFrameMs,
                value => config.PerformanceSamplingIncludeFrameMs = value);
            AddCheck("[debug.performance_sampling] include_ui_queue", "采样 UI 队列",
                "在性能采样中记录 UI 队列摘要。",
                () => config.PerformanceSamplingIncludeUiQueue,
                value => config.PerformanceSamplingIncludeUiQueue = value);
            AddCheck("[debug.performance_sampling] include_texture_queue", "采样纹理队列",
                "在性能采样中记录纹理/图片队列摘要。",
                () => config.PerformanceSamplingIncludeTextureQueue,
                value => config.PerformanceSamplingIncludeTextureQueue = value);
            AddCheck("[debug.performance_sampling] include_ring_buffer", "采样 ring buffer",
                "在性能采样中记录日志缓冲区占用。",
                () => config.PerformanceSamplingIncludeRingBuffer,
                value => config.PerformanceSamplingIncludeRingBuffer = value);
            AddCheck("[debug.performance_sampling] include_dropped_count", "采样丢弃计数",
                "记录限流丢弃的日志数量。",
                () => config.PerformanceSamplingIncludeDroppedCount,
                value => config.PerformanceSamplingIncludeDroppedCount = value);
            AddCheck("[debug.performance_sampling] include_memory", "采样内存",
                "记录内存摘要。后续接入平台内存指标时使用。",
                () => config.PerformanceSamplingIncludeMemory,
                value => config.PerformanceSamplingIncludeMemory = value);
            AddCheck("[diagnostic_snapshot] enabled", "启用诊断快照",
                "预留现场快照功能。截图/布局快照可能增加文件体积和隐私风险。",
                () => config.SnapshotEnabled,
                value => config.SnapshotEnabled = value);
            AddCheck("[diagnostic_snapshot] include_screenshot", "包含截图",
                "导出快照时包含屏幕截图。默认关闭，避免隐私风险。",
                () => config.SnapshotIncludeScreenshot,
                value => config.SnapshotIncludeScreenshot = value);
            AddCheck("[diagnostic_snapshot] include_layout_snapshot", "包含布局快照",
                "导出 UI 节点和矩形摘要，用于定位布局问题。",
                () => config.SnapshotIncludeLayoutSnapshot,
                value => config.SnapshotIncludeLayoutSnapshot = value);
            AddCheck("[diagnostic_snapshot] include_visible_ui_rects", "包含可见 UI 矩形",
                "布局快照中只记录可见 UI 控件矩形。",
                () => config.SnapshotIncludeVisibleUiRects,
                value => config.SnapshotIncludeVisibleUiRects = value);
            AddCheck("[diagnostic_snapshot] include_image_rects", "包含图片矩形",
                "布局快照中记录图片节点矩形。",
                () => config.SnapshotIncludeImageRects,
                value => config.SnapshotIncludeImageRects = value);
            AddInt("[diagnostic_snapshot] max_nodes", "最大节点数",
                "限制布局快照遍历的最大节点数量，避免复杂场景导出过大。",
                1, 10000, 10,
                () => config.SnapshotMaxNodes,
                value => config.SnapshotMaxNodes = value);
            AddInt("[diagnostic_snapshot] max_file_kb", "最大文件 KB",
                "限制单个快照文件大小，避免手机存储压力。",
                1, 102400, 64,
                () => config.SnapshotMaxFileKb,
                value => config.SnapshotMaxFileKb = value);
        }

        void BuildQuickDebugTab()
        {
            // 企业级说明：快捷页是面向人工 APK 排障的第一入口，只暴露 preset 和少量额外模块。
            // 它写入 quick_debug 后由加载器一次性展开为专家细项，业务热路径仍只读取强类型布尔开关。
            addingQuickOptions = true;
            try
            {
                AddSection("人用简化调试", "一般手机/APK 排查只改本页。需要完全手动控制时，把 preset 设为 custom 后再改专家页。");
                AddCheck("[quick_debug] enabled", "启用快捷层",
                    "开启后 preset 会覆盖相关专家细项；关闭后完全按专家页执行。",
                    () => config.QuickDebugEnabled,
                    value => config.QuickDebugEnabled = value);
                AddChoice("[quick_debug] preset", "排查场景",
                    "normal 最安全；touch_input 查触摸输入；image_ui 查图片和界面矩形；resource_storage 查资源/存储；performance 查性能；apk_issue 查 APK 环境；custom 使用专家细项。",
                    new[] { "normal", "touch_input", "image_ui", "resource_storage", "performance", "apk_issue", "custom" },
                    () => config.QuickDebugPreset,
                    value => config.QuickDebugPreset = value);
                AddChoice("[quick_debug] language", "调试语言",
                    "决定 active_debug_model 映射到 zh_cn/jp/en 哪一套调试档。",
                    new[] { "zh_cn", "jp", "en" },
                    () => config.QuickDebugLanguage,
                    value => config.QuickDebugLanguage = value);
                AddCheck("[quick_debug] apk_safe", "APK 安全模式",
                    "默认开启。开启后避免把非错误日志大量镜像到 Godot 控制台，降低手机刷屏和分配成本。",
                    () => config.QuickDebugApkSafe,
                    value => config.QuickDebugApkSafe = value);
                AddCheck("[quick_debug] runtime_panel", "显示悬浮调试窗",
                    "开启后进入主运行场景时显示悬浮入口。",
                    () => config.QuickDebugRuntimePanel,
                    value => config.QuickDebugRuntimePanel = value);
                AddCheck("[quick_debug] mirror_non_error_to_godot", "镜像非错误到 Godot",
                    "仅在 apk_safe=false 时生效。手机实机通常不建议开启。",
                    () => config.QuickDebugMirrorNonErrorToGodot,
                    value => config.QuickDebugMirrorNonErrorToGodot = value);
                AddCheck("[quick_debug] diagnostic_package", "允许导出诊断包",
                    "导出是手动动作，不会持续写盘。普通排查优先看 GDPrint 页，需要发给别人复盘时再导出。",
                    () => config.QuickDebugDiagnosticPackage,
                    value => config.QuickDebugDiagnosticPackage = value);

                AddSection("额外模块", "这些开关只会在 preset 之外额外开启模块；false 不会反向关闭 preset 已开启的模块。");
                AddCheck("[quick_debug.modules] touch", "触摸",
                    "额外开启触摸点击/拖动基础日志。",
                    () => config.QuickModules.Touch,
                    value => config.QuickModules.Touch = value);
                AddCheck("[quick_debug.modules] input", "输入",
                    "额外开启提交、消费和按钮输入日志。",
                    () => config.QuickModules.Input,
                    value => config.QuickModules.Input = value);
                AddCheck("[quick_debug.modules] image", "图片",
                    "额外开启图片路径、纹理和目标矩形诊断。",
                    () => config.QuickModules.Image,
                    value => config.QuickModules.Image = value);
                AddCheck("[quick_debug.modules] ui_layout", "界面矩形",
                    "额外开启 UI 目标矩形与实际矩形差异诊断。",
                    () => config.QuickModules.UiLayout,
                    value => config.QuickModules.UiLayout = value);
                AddCheck("[quick_debug.modules] resource", "资源",
                    "额外开启 Sprite、Audio、SQLite 等资源诊断。",
                    () => config.QuickModules.Resource,
                    value => config.QuickModules.Resource = value);
                AddCheck("[quick_debug.modules] load_save", "读写/存档",
                    "额外开启 ERB/CSV/resources 加载和读写存档诊断。",
                    () => config.QuickModules.LoadSave,
                    value => config.QuickModules.LoadSave = value);
                AddCheck("[quick_debug.modules] android_storage", "Android 存储",
                    "额外开启权限、扫描、路径选择和读写失败诊断；默认适合 APK 排查。",
                    () => config.QuickModules.AndroidStorage,
                    value => config.QuickModules.AndroidStorage = value);
                AddCheck("[quick_debug.modules] performance_sampling", "性能采样",
                    "额外开启低频性能采样。采样间隔会保持移动端安全下限。",
                    () => config.QuickModules.PerformanceSampling,
                    value => config.QuickModules.PerformanceSampling = value);
                AddCheck("[quick_debug.modules] snapshot", "诊断快照",
                    "额外允许导出截图/布局等快照信息。",
                    () => config.QuickModules.Snapshot,
                    value => config.QuickModules.Snapshot = value);
                AddCheck("[quick_debug.modules] input_replay", "输入复现",
                    "额外开启轻量输入复现缓冲，用于还原触摸/按钮顺序。",
                    () => config.QuickModules.InputReplay,
                    value => config.QuickModules.InputReplay = value);
            }
            finally
            {
                addingQuickOptions = false;
            }
        }

        void BuildGdPrintTab()
        {
            // 企业级说明：GDPrint 页只读取内存 ring buffer，不主动订阅业务事件，也不持续写盘。
            // 手机排查优先在这里看最近日志；只有需要把现场发给他人或 AI 复盘时，才手动点击导出。
            AddSection("GDPrint 日志窗口", "显示内存 ring buffer 最近日志，行为接近 Godot 输出面板。导出按钮放在这里，避免普通用户把“导出”误解为必做步骤。");

            consoleUserPathLabel = CreateSmallLabel("");
            optionRoot.AddChild(consoleUserPathLabel);

            var actions = new HFlowContainer();
            actions.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            actions.AddThemeConstantOverride("h_separation", 8);
            actions.AddThemeConstantOverride("v_separation", 8);
            optionRoot.AddChild(actions);

            var refresh = CreateActionButton("刷新", "从内存 ring buffer 重新读取最近日志，不访问磁盘。");
            refresh.Pressed += RefreshConsoleLog;
            actions.AddChild(refresh);

            var export = CreateActionButton("导出日志", "把当前 ring buffer 写入启动游戏目录，按钮触发时才写盘。");
            export.Pressed += ExportDiagnosticLog;
            actions.AddChild(export);

            var exportPackage = CreateActionButton("导出包", "需要完整设备/配置/日志现场时再使用。普通排查通常只看窗口或导出日志即可。");
            exportPackage.Pressed += ExportDiagnosticPackage;
            actions.AddChild(exportPackage);

            consoleTextEdit = new TextEdit();
            consoleTextEdit.Editable = false;
            consoleTextEdit.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            consoleTextEdit.SizeFlagsVertical = SizeFlags.ExpandFill;
            consoleTextEdit.CustomMinimumSize = new Vector2(0, FloatingMode ? 220 : 320);
            consoleTextEdit.AddThemeFontSizeOverride("font_size", 13);
            optionRoot.AddChild(consoleTextEdit);

            RefreshUserDirectoryLabel();
            RefreshConsoleLog();
        }

        void BeginTab(string title)
        {
            if (optionTabs == null)
                return;

            // 企业级说明：每个页签只持有当前配置分组的轻量 Control 节点。
            // 不做虚拟列表，是因为配置项数量有限；独立 ScrollContainer 可让手机用户切换模块时保留顶部保存/导出操作。
            var scroll = new ScrollContainer();
            scroll.Name = title;
            scroll.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
            scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
            scroll.VerticalScrollMode = ScrollContainer.ScrollMode.Auto;
            scroll.FollowFocus = true;
            scroll.ScrollDeadzone = 10;
            scroll.MouseFilter = MouseFilterEnum.Stop;
            optionTabs.AddChild(scroll);

            optionRoot = new VBoxContainer();
            optionRoot.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            optionRoot.AddThemeConstantOverride("separation", 12);
            optionRoot.MouseFilter = MouseFilterEnum.Pass;
            scroll.AddChild(optionRoot);
        }

        void EnsureTabRoot()
        {
            if (optionRoot == null)
                BeginTab("常规");
        }

        void ApplyTabListSpacing(int spacing)
        {
            if (optionTabs == null)
                return;

            foreach (Node tab in optionTabs.GetChildren())
            {
                if (tab is ScrollContainer scroll && scroll.GetChildCount() > 0 && scroll.GetChild(0) is VBoxContainer root)
                    root.AddThemeConstantOverride("separation", spacing);
            }
        }

        void ApplyTabScrollMinimumHeight(int minHeight)
        {
            if (optionTabs == null)
                return;

            foreach (Node tab in optionTabs.GetChildren())
            {
                if (tab is ScrollContainer scroll)
                    scroll.CustomMinimumSize = new Vector2(0, minHeight);
            }
        }

        void AddSection(string title, string description)
        {
            EnsureTabRoot();
            var box = new VBoxContainer();
            box.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            box.AddThemeConstantOverride("separation", 4);
            box.MouseFilter = MouseFilterEnum.Pass;
            optionRoot.AddChild(box);

            var titleLabel = new Label();
            titleLabel.Text = title;
            titleLabel.MouseFilter = MouseFilterEnum.Ignore;
            titleLabel.AddThemeFontSizeOverride("font_size", 17);
            titleLabel.AddThemeColorOverride("font_color", new Color(1.0f, 0.96f, 0.82f));
            box.AddChild(titleLabel);

            var descLabel = CreateSmallLabel(description);
            box.AddChild(descLabel);
        }

        void AddCheck(string key, string title, string description, Func<bool> getter, Action<bool> setter)
        {
            bool quickOption = addingQuickOptions;
            var row = CreateOptionRow(key, title, description);
            var check = new CheckBox();
            check.Text = title;
            check.ButtonPressed = getter();
            check.TooltipText = key;
            check.CustomMinimumSize = new Vector2(0, 44);
            check.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            check.AddThemeFontSizeOverride("font_size", 15);
            check.Toggled += value =>
            {
                MarkOptionDirty(quickOption);
                setter(value);
            };
            row.AddChild(check);
            AddOptionDescription(row, key, description);
        }

        void AddChoice(string key, string title, string description, string[] values, Func<string> getter, Action<string> setter)
        {
            bool quickOption = addingQuickOptions;
            var row = CreateOptionRow(key, title, description);
            var line = new HBoxContainer();
            line.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            line.AddThemeConstantOverride("separation", 8);
            row.AddChild(line);

            var label = CreateOptionTitle(title, key);
            line.AddChild(label);

            var option = new OptionButton();
            option.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            option.CustomMinimumSize = new Vector2(220, 44);
            string current = getter() ?? "";
            int selected = 0;
            for (int i = 0; i < values.Length; i++)
            {
                option.AddItem(values[i], i);
                if (string.Equals(values[i], current, StringComparison.OrdinalIgnoreCase))
                    selected = i;
            }
            option.Select(selected);
            option.ItemSelected += index =>
            {
                MarkOptionDirty(quickOption);
                setter(values[(int)index]);
            };
            line.AddChild(option);
            AddOptionDescription(row, key, description);
        }

        void AddText(string key, string title, string description, Func<string> getter, Action<string> setter)
        {
            bool quickOption = addingQuickOptions;
            var row = CreateOptionRow(key, title, description);
            var line = new HBoxContainer();
            line.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            line.AddThemeConstantOverride("separation", 8);
            row.AddChild(line);

            var label = CreateOptionTitle(title, key);
            line.AddChild(label);

            var edit = new LineEdit();
            edit.Text = getter() ?? "";
            edit.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            edit.CustomMinimumSize = new Vector2(220, 44);
            edit.TooltipText = key;
            edit.TextChanged += text =>
            {
                MarkOptionDirty(quickOption);
                setter(text);
            };
            line.AddChild(edit);
            AddOptionDescription(row, key, description);
        }

        void AddInt(string key, string title, string description, int min, int max, int step, Func<int> getter, Action<int> setter)
        {
            bool quickOption = addingQuickOptions;
            var row = CreateOptionRow(key, title, description);
            var line = new HBoxContainer();
            line.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            line.AddThemeConstantOverride("separation", 8);
            row.AddChild(line);

            var label = CreateOptionTitle(title, key);
            line.AddChild(label);

            var spin = new SpinBox();
            spin.MinValue = min;
            spin.MaxValue = max;
            spin.Step = step;
            spin.Value = getter();
            spin.CustomMinimumSize = new Vector2(170, 44);
            spin.ValueChanged += value =>
            {
                MarkOptionDirty(quickOption);
                setter((int)Math.Round(value));
            };
            line.AddChild(spin);
            AddOptionDescription(row, key, description);
        }

        void MarkOptionDirty(bool quickOption)
        {
            if (quickOption)
                quickOptionsDirty = true;
            else
                expertOptionsDirty = true;
        }

        VBoxContainer CreateOptionRow(string key, string title, string description)
        {
            EnsureTabRoot();
            var panel = new PanelContainer();
            panel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            panel.MouseFilter = MouseFilterEnum.Pass;
            panel.AddThemeStyleboxOverride("panel", CreateRowStyle());
            optionRoot.AddChild(panel);

            var margin = new MarginContainer();
            margin.MouseFilter = MouseFilterEnum.Pass;
            ApplyMargin(margin, 10, 8);
            panel.AddChild(margin);

            var row = new VBoxContainer();
            row.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            row.AddThemeConstantOverride("separation", 4);
            row.MouseFilter = MouseFilterEnum.Pass;
            margin.AddChild(row);

            return row;
        }

        void AddOptionDescription(VBoxContainer row, string key, string description)
        {
            var desc = CreateSmallLabel(key + "\n" + description);
            desc.TooltipText = key;
            row.AddChild(desc);
        }

        Label CreateOptionTitle(string title, string key)
        {
            var label = new Label();
            label.Text = title;
            label.TooltipText = key;
            label.CustomMinimumSize = new Vector2(190, 0);
            label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            label.MouseFilter = MouseFilterEnum.Ignore;
            label.AddThemeFontSizeOverride("font_size", 15);
            label.AddThemeColorOverride("font_color", new Color(0.93f, 0.96f, 0.98f));
            return label;
        }

        Label CreateSmallLabel(string text)
        {
            var label = new Label();
            label.Text = text;
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            label.MouseFilter = MouseFilterEnum.Ignore;
            label.AddThemeFontSizeOverride("font_size", 13);
            label.AddThemeColorOverride("font_color", new Color(0.72f, 0.8f, 0.86f));
            return label;
        }

        void SaveConfigFromPanel()
        {
            if (config == null)
                return;

            // 企业级说明：快捷页和专家页的保存语义不同。
            // 只改快捷页时必须保留 quick_debug.preset；改专家细项时如果仍停留在非 custom preset，
            // 下一次加载会被快捷层重新覆盖，导致用户误判为保存失败，因此仅在专家页发生改动时自动切到 custom。
            bool switchedToCustom = false;
            if (expertOptionsDirty
                && config.QuickDebugEnabled
                && !string.Equals(config.QuickDebugPreset, "custom", StringComparison.OrdinalIgnoreCase))
            {
                config.QuickDebugPreset = "custom";
                switchedToCustom = true;
            }

            if (!RuntimeDiagnosticsConfigWriter.SaveUserConfig(config, out string path, out string error))
            {
                SetStatus("保存失败：" + error);
                return;
            }

            string reloadError = "";
            bool hotReloaded = false;
            if (global::GenericUtils.GetRuntimeDiagnosticsConfig() != null)
                hotReloaded = global::GenericUtils.ReloadRuntimeDiagnosticsConfig(out reloadError);

            SetStatus(hotReloaded
                ? "已保存并热重载：" + BuildPathDisplay(path) + (switchedToCustom ? "；专家项已自动切到 quick_debug.preset=custom" : "")
                : string.IsNullOrEmpty(reloadError)
                    ? "已保存：" + BuildPathDisplay(path) + "。进入游戏或重启后生效。" + (switchedToCustom ? " 专家项已自动切到 quick_debug.preset=custom。" : "")
                    : "已保存，但热重载失败：" + reloadError);
            quickOptionsDirty = false;
            expertOptionsDirty = false;
            RefreshUserDirectoryLabel();
            RefreshConsoleLog();
            RefreshStats();
        }

        void ExportDiagnosticLog()
        {
            if (global::GenericUtils.GetRuntimeDiagnosticsConfig() == null)
            {
                SetStatus("当前还未进入主运行场景，没有可导出的运行时 ring buffer。");
                return;
            }

            string path = global::GenericUtils.GetDefaultDiagnosticLogPath();
            if (global::GenericUtils.ExportDiagnosticLog(path, out string error))
                SetStatus("已导出诊断日志：" + BuildPathDisplay(path));
            else
                SetStatus("导出失败：" + error);
            RefreshUserDirectoryLabel();
            RefreshConsoleLog();
            RefreshStats();
        }

        void ExportDiagnosticPackage()
        {
            if (global::GenericUtils.GetRuntimeDiagnosticsConfig() == null)
            {
                SetStatus("当前还未进入主运行场景，无法生成完整运行时诊断包。");
                return;
            }

            if (global::GenericUtils.ExportDiagnosticPackage(null, out string error))
                SetStatus("已导出诊断包：" + BuildPathDisplay(DiagnosticLogExporter.GameDirectoryPathPrefix));
            else
                SetStatus("导出诊断包失败：" + error);
            RefreshUserDirectoryLabel();
            RefreshConsoleLog();
            RefreshStats();
        }

        void ToggleCollapsed()
        {
            if (FloatingMode)
            {
                HideRequested?.Invoke();
                return;
            }
            collapsed = !collapsed;
            optionBody.Visible = !collapsed;
        }

        void RefreshStats()
        {
            if (statsLabel == null)
                return;
            statsLabel.Text = "ring buffer: "
                + DiagnosticLogSinks.RingCount + "/"
                + DiagnosticLogSinks.RingCapacity
                + " overwritten=" + DiagnosticLogSinks.OverwrittenTotal
                + " dropped=" + DiagnosticLogRouter.GetDroppedTotal()
                + " | 游戏目录=" + DiagnosticLogExporter.GetCurrentGameDirectoryForDisplay();
        }

        void SetStatus(string text)
        {
            if (statusLabel != null)
                statusLabel.Text = text ?? "";
        }

        void RefreshUserDirectoryLabel()
        {
            if (consoleUserPathLabel == null)
                return;

            consoleUserPathLabel.Text =
                "启动游戏目录：" + DiagnosticLogExporter.GetCurrentGameDirectoryForDisplay()
                + "\n日志文件：" + BuildPathDisplay(global::GenericUtils.GetDefaultDiagnosticLogPath())
                + "\n诊断包目录：" + BuildPathDisplay(DiagnosticLogExporter.GameDirectoryPathPrefix);
        }

        void RefreshConsoleLog()
        {
            if (consoleTextEdit == null)
                return;

            var records = DiagnosticLogSinks.Snapshot();
            if (records.Length == 0)
            {
                consoleTextEdit.Text = "暂无运行时日志。进入游戏主场景后，日志会先写入内存 ring buffer；点击“刷新”可重新读取。";
                RefreshStats();
                return;
            }

            const int MaxConsoleRecords = 200;
            int start = Math.Max(0, records.Length - MaxConsoleRecords);
            var sb = new StringBuilder(Math.Min(records.Length - start, MaxConsoleRecords) * 160);
            sb.AppendLine("显示最近 " + (records.Length - start) + " / " + records.Length + " 条 ring buffer 日志；完整内容请点“导出日志”。");
            for (int i = start; i < records.Length; i++)
            {
                var r = records[i];
                sb.Append(r.Seq.ToString("D6"))
                    .Append(" ")
                    .Append(r.Level.ToString().ToUpperInvariant())
                    .Append(" ")
                    .Append(r.Category)
                    .Append(" ")
                    .Append(r.EventId)
                    .Append(" | ")
                    .Append(r.Message);
                if (!string.IsNullOrEmpty(r.Data))
                    sb.Append(" | ").Append(r.Data);
                sb.AppendLine();
            }
            consoleTextEdit.Text = sb.ToString();
            RefreshStats();
        }

        static string BuildPathDisplay(string godotPath)
        {
            if (string.IsNullOrWhiteSpace(godotPath))
                return "";
            string absolute = ToAbsolutePath(godotPath);
            if (string.IsNullOrWhiteSpace(absolute) || string.Equals(absolute, godotPath, StringComparison.OrdinalIgnoreCase))
                return godotPath;
            return godotPath + "（实际：" + absolute + "）";
        }

        static string ToAbsolutePath(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path))
                    return "";
                if (path.StartsWith(DiagnosticLogExporter.GameDirectoryPathPrefix, StringComparison.OrdinalIgnoreCase))
                    return DiagnosticLogExporter.ResolvePathForDisplay(path);
                if (path.StartsWith("user://", StringComparison.OrdinalIgnoreCase)
                    || path.StartsWith("res://", StringComparison.OrdinalIgnoreCase))
                    return ProjectSettings.GlobalizePath(path);
                return path;
            }
            catch
            {
                return path ?? "";
            }
        }

        static void ClearChildren(Node node)
        {
            if (node == null)
                return;
            foreach (Node child in node.GetChildren())
            {
                node.RemoveChild(child);
                child.QueueFree();
            }
        }

        static void ApplyMargin(MarginContainer margin, int horizontal, int vertical)
        {
            if (margin == null)
                return;
            margin.AddThemeConstantOverride("margin_left", horizontal);
            margin.AddThemeConstantOverride("margin_top", vertical);
            margin.AddThemeConstantOverride("margin_right", horizontal);
            margin.AddThemeConstantOverride("margin_bottom", vertical);
        }

        StyleBoxFlat CreatePanelStyle()
        {
            var style = new StyleBoxFlat();
            style.BgColor = new Color(0.085f, 0.098f, 0.105f, 0.96f);
            style.BorderColor = new Color(0.32f, 0.42f, 0.44f);
            style.SetBorderWidthAll(1);
            style.SetCornerRadiusAll(8);
            style.ContentMarginLeft = 8;
            style.ContentMarginRight = 8;
            style.ContentMarginTop = 8;
            style.ContentMarginBottom = 8;
            return style;
        }

        StyleBoxFlat CreateRowStyle()
        {
            var style = new StyleBoxFlat();
            style.BgColor = new Color(0.115f, 0.13f, 0.135f, 0.95f);
            style.BorderColor = new Color(0.2f, 0.26f, 0.27f);
            style.SetBorderWidthAll(1);
            style.SetCornerRadiusAll(6);
            return style;
        }
    }

    /// <summary>
    /// 企业级说明：悬浮诊断入口只负责调试 UI 的显示、隐藏和窗口尺寸调整。
    /// 它不读取业务状态、不写配置、不接入输入模拟器链路；全屏根节点保持 Ignore，避免悬浮入口以外区域拦截游戏触摸。
    /// </summary>
    public sealed partial class FloatingDiagnosticsHost : Control
    {
        const float BallSize = 64.0f;
        const float ViewportMargin = 12.0f;
        const float ResizeHandleThickness = 28.0f;
        const float ResizeCornerSize = 46.0f;
        const float MinPanelWidth = 360.0f;
        const float MinPanelHeight = 260.0f;
        const ulong ResizeLongPressMs = 420;
        const int EdgeLeft = 1;
        const int EdgeRight = 2;
        const int EdgeTop = 4;
        const int EdgeBottom = 8;

        readonly Control[] resizeHandles = new Control[8];
        readonly int[] resizeMasks =
        {
            EdgeLeft, EdgeRight, EdgeTop, EdgeBottom,
            EdgeLeft | EdgeTop, EdgeRight | EdgeTop, EdgeLeft | EdgeBottom, EdgeRight | EdgeBottom
        };

        RuntimeDiagnosticsPanel panel;
        Button ballButton;
        bool panelVisible;
        bool resizePending;
        bool resizing;
        int resizePointerIndex = int.MinValue;
        int resizeMask;
        ulong resizePressMs;
        Vector2 resizeStartPointer;
        Vector2 resizeStartPanelPosition;
        Vector2 resizeStartPanelSize;

        public override void _Ready()
        {
            MouseFilter = MouseFilterEnum.Ignore;
            BuildFloatingUi();
            CallDeferred(MethodName.PlaceInitialFloatingControls);
        }

        public override void _Notification(int what)
        {
            if (what != NotificationResized)
                return;

            ClampBallToViewport();
            if (panelVisible)
            {
                ClampPanelToViewport();
                UpdateResizeHandles();
            }
        }

        void BuildFloatingUi()
        {
            ballButton = new Button();
            ballButton.Text = "调";
            ballButton.TooltipText = "打开/隐藏日志诊断悬浮窗";
            ballButton.FocusMode = FocusModeEnum.None;
            ballButton.CustomMinimumSize = new Vector2(BallSize, BallSize);
            ballButton.Size = new Vector2(BallSize, BallSize);
            ballButton.MouseFilter = MouseFilterEnum.Stop;
            ballButton.AddThemeFontSizeOverride("font_size", 22);
            ApplyFloatingBallStyle(ballButton);
            ballButton.Pressed += TogglePanel;
            AddChild(ballButton);

            panel = new RuntimeDiagnosticsPanel
            {
                FloatingMode = true,
                Visible = false,
                MouseFilter = MouseFilterEnum.Stop,
                CustomMinimumSize = new Vector2(MinPanelWidth, MinPanelHeight)
            };
            panel.HideRequested += () => SetPanelVisible(false);
            panel.CloseRequested += QueueFree;
            AddChild(panel);

            for (int i = 0; i < resizeHandles.Length; i++)
            {
                int mask = resizeMasks[i];
                var handle = new ColorRect();
                handle.Color = new Color(0.42f, 0.7f, 0.72f, 0.20f);
                handle.MouseFilter = MouseFilterEnum.Stop;
                handle.Visible = false;
                handle.TooltipText = "长按后拖动可调整悬浮窗大小";
                handle.GuiInput += inputEvent => HandleResizeInput(inputEvent, mask);
                resizeHandles[i] = handle;
                AddChild(handle);
            }
        }

        void PlaceInitialFloatingControls()
        {
            Vector2 viewportSize = GetViewportSize();
            ballButton.Size = new Vector2(BallSize, BallSize);
            ballButton.Position = new Vector2(
                Mathf.Max(ViewportMargin, viewportSize.X - BallSize - ViewportMargin),
                Mathf.Max(ViewportMargin, viewportSize.Y * 0.5f - BallSize * 0.5f));

            Vector2 initialSize = new Vector2(
                Mathf.Min(760.0f, Mathf.Max(MinPanelWidth, viewportSize.X - ViewportMargin * 2.0f)),
                Mathf.Min(640.0f, Mathf.Max(MinPanelHeight, viewportSize.Y - ViewportMargin * 2.0f)));
            panel.Size = initialSize;
            panel.Position = new Vector2(
                Mathf.Max(ViewportMargin, viewportSize.X - initialSize.X - ViewportMargin),
                ViewportMargin);
            SetPanelVisible(false);
        }

        void TogglePanel()
        {
            SetPanelVisible(!panelVisible);
        }

        void SetPanelVisible(bool visible)
        {
            panelVisible = visible;
            panel.Visible = visible;
            ballButton.Text = visible ? "×" : "调";
            if (visible)
                ClampPanelToViewport();
            UpdateResizeHandles();
        }

        void HandleResizeInput(InputEvent inputEvent, int mask)
        {
            if (!panelVisible)
                return;

            if (inputEvent is InputEventScreenTouch touch)
            {
                if (touch.Pressed)
                    StartResize(mask, touch.Position, touch.Index);
                else if (resizePointerIndex == touch.Index)
                    EndResize();
                AcceptEvent();
                return;
            }

            if (inputEvent is InputEventScreenDrag drag)
            {
                if (resizePointerIndex == drag.Index)
                    UpdateResize(drag.Position);
                AcceptEvent();
                return;
            }

            if (inputEvent is InputEventMouseButton mouseButton && mouseButton.ButtonIndex == MouseButton.Left)
            {
                if (mouseButton.Pressed)
                    StartResize(mask, GetViewport().GetMousePosition(), -1);
                else if (resizePointerIndex == -1)
                    EndResize();
                AcceptEvent();
                return;
            }

            if (inputEvent is InputEventMouseMotion mouseMotion
                && resizePointerIndex == -1
                && (((int)mouseMotion.ButtonMask & (int)MouseButtonMask.Left) != 0))
            {
                UpdateResize(GetViewport().GetMousePosition());
                AcceptEvent();
            }
        }

        void StartResize(int mask, Vector2 pointerPosition, int pointerIndex)
        {
            resizePending = true;
            resizing = false;
            resizePointerIndex = pointerIndex;
            resizeMask = mask;
            resizePressMs = Time.GetTicksMsec();
            resizeStartPointer = pointerPosition;
            resizeStartPanelPosition = panel.Position;
            resizeStartPanelSize = panel.Size;
        }

        void EndResize()
        {
            resizePending = false;
            resizing = false;
            resizePointerIndex = int.MinValue;
        }

        void UpdateResize(Vector2 pointerPosition)
        {
            if (!resizePending)
                return;

            // 企业级说明：边框长按后才进入 resize，避免普通点按页签、按钮或滚动时误触修改窗口大小。
            if (!resizing)
            {
                if (Time.GetTicksMsec() - resizePressMs < ResizeLongPressMs)
                    return;
                resizing = true;
            }

            ApplyResize(pointerPosition - resizeStartPointer);
        }

        void ApplyResize(Vector2 delta)
        {
            Rect2 bounds = GetFloatingBounds();
            float minWidth = Mathf.Min(MinPanelWidth, bounds.Size.X);
            float minHeight = Mathf.Min(MinPanelHeight, bounds.Size.Y);

            float left = resizeStartPanelPosition.X;
            float top = resizeStartPanelPosition.Y;
            float right = left + resizeStartPanelSize.X;
            float bottom = top + resizeStartPanelSize.Y;

            if ((resizeMask & EdgeLeft) != 0)
                left += delta.X;
            if ((resizeMask & EdgeRight) != 0)
                right += delta.X;
            if ((resizeMask & EdgeTop) != 0)
                top += delta.Y;
            if ((resizeMask & EdgeBottom) != 0)
                bottom += delta.Y;

            if (right - left < minWidth)
            {
                if ((resizeMask & EdgeLeft) != 0)
                    left = right - minWidth;
                else
                    right = left + minWidth;
            }
            if (bottom - top < minHeight)
            {
                if ((resizeMask & EdgeTop) != 0)
                    top = bottom - minHeight;
                else
                    bottom = top + minHeight;
            }

            if (left < bounds.Position.X)
                left = bounds.Position.X;
            if (top < bounds.Position.Y)
                top = bounds.Position.Y;
            if (right > bounds.Position.X + bounds.Size.X)
                right = bounds.Position.X + bounds.Size.X;
            if (bottom > bounds.Position.Y + bounds.Size.Y)
                bottom = bounds.Position.Y + bounds.Size.Y;

            if (right - left < minWidth)
                right = Mathf.Min(bounds.Position.X + bounds.Size.X, left + minWidth);
            if (bottom - top < minHeight)
                bottom = Mathf.Min(bounds.Position.Y + bounds.Size.Y, top + minHeight);

            panel.Position = new Vector2(left, top);
            panel.Size = new Vector2(right - left, bottom - top);
            UpdateResizeHandles();
        }

        void ClampPanelToViewport()
        {
            Rect2 bounds = GetFloatingBounds();
            Vector2 size = panel.Size;
            size.X = Mathf.Clamp(size.X, Mathf.Min(MinPanelWidth, bounds.Size.X), bounds.Size.X);
            size.Y = Mathf.Clamp(size.Y, Mathf.Min(MinPanelHeight, bounds.Size.Y), bounds.Size.Y);

            Vector2 pos = panel.Position;
            pos.X = Mathf.Clamp(pos.X, bounds.Position.X, bounds.Position.X + bounds.Size.X - size.X);
            pos.Y = Mathf.Clamp(pos.Y, bounds.Position.Y, bounds.Position.Y + bounds.Size.Y - size.Y);
            panel.Position = pos;
            panel.Size = size;
        }

        void ClampBallToViewport()
        {
            if (ballButton == null)
                return;

            Vector2 viewportSize = GetViewportSize();
            Vector2 max = new Vector2(
                Mathf.Max(ViewportMargin, viewportSize.X - ballButton.Size.X - ViewportMargin),
                Mathf.Max(ViewportMargin, viewportSize.Y - ballButton.Size.Y - ViewportMargin));
            ballButton.Position = new Vector2(
                Mathf.Clamp(ballButton.Position.X, ViewportMargin, max.X),
                Mathf.Clamp(ballButton.Position.Y, ViewportMargin, max.Y));
        }

        void UpdateResizeHandles()
        {
            if (panel == null)
                return;

            bool visible = panelVisible && panel.Visible;
            foreach (Control handle in resizeHandles)
                handle.Visible = visible;
            if (!visible)
                return;

            Vector2 p = panel.Position;
            Vector2 s = panel.Size;
            float t = ResizeHandleThickness;
            float c = ResizeCornerSize;
            float half = t * 0.5f;

            SetHandleRect(0, p.X - half, p.Y + c, t, Mathf.Max(0.0f, s.Y - c * 2.0f));
            SetHandleRect(1, p.X + s.X - half, p.Y + c, t, Mathf.Max(0.0f, s.Y - c * 2.0f));
            SetHandleRect(2, p.X + c, p.Y - half, Mathf.Max(0.0f, s.X - c * 2.0f), t);
            SetHandleRect(3, p.X + c, p.Y + s.Y - half, Mathf.Max(0.0f, s.X - c * 2.0f), t);
            SetHandleRect(4, p.X - half, p.Y - half, c, c);
            SetHandleRect(5, p.X + s.X - c + half, p.Y - half, c, c);
            SetHandleRect(6, p.X - half, p.Y + s.Y - c + half, c, c);
            SetHandleRect(7, p.X + s.X - c + half, p.Y + s.Y - c + half, c, c);
        }

        void SetHandleRect(int index, float x, float y, float width, float height)
        {
            Control handle = resizeHandles[index];
            handle.Position = new Vector2(x, y);
            handle.Size = new Vector2(width, height);
        }

        Rect2 GetFloatingBounds()
        {
            Vector2 viewportSize = GetViewportSize();
            return new Rect2(
                new Vector2(ViewportMargin, ViewportMargin),
                new Vector2(
                    Mathf.Max(BallSize, viewportSize.X - ViewportMargin * 2.0f),
                    Mathf.Max(BallSize, viewportSize.Y - ViewportMargin * 2.0f)));
        }

        Vector2 GetViewportSize()
        {
            Vector2 viewportSize = GetViewport()?.GetVisibleRect().Size ?? Size;
            if (viewportSize.X <= 0.0f || viewportSize.Y <= 0.0f)
                viewportSize = Size;
            return viewportSize;
        }

        static void ApplyFloatingBallStyle(Button button)
        {
            var normal = CreateBallStyle(new Color(0.16f, 0.38f, 0.44f, 0.94f));
            var hover = CreateBallStyle(new Color(0.20f, 0.46f, 0.52f, 0.98f));
            var pressed = CreateBallStyle(new Color(0.10f, 0.30f, 0.36f, 1.0f));
            button.AddThemeStyleboxOverride("normal", normal);
            button.AddThemeStyleboxOverride("hover", hover);
            button.AddThemeStyleboxOverride("pressed", pressed);
            button.AddThemeColorOverride("font_color", new Color(0.95f, 1.0f, 1.0f));
        }

        static StyleBoxFlat CreateBallStyle(Color color)
        {
            var style = new StyleBoxFlat();
            style.BgColor = color;
            style.BorderColor = new Color(0.72f, 0.92f, 0.95f, 0.85f);
            style.SetBorderWidthAll(2);
            style.SetCornerRadiusAll(32);
            return style;
        }
    }
}
