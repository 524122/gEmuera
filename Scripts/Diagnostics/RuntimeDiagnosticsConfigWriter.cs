using System;
using System.Globalization;
using System.Text;
using Godot;

namespace gEmuera.Diagnostics
{
    /// <summary>
    /// 企业级说明：运行时诊断配置写入器只负责把强类型配置序列化到 user://config.toml。
    /// Android/APK 中 res:// 是只读资源包，运行时面板必须写入 user://，下一次启动会由 Loader 优先读取。
    /// 本类不访问 UI，不参与日志路由，避免配置持久化逻辑与界面和热路径日志耦合。
    /// </summary>
    public static class RuntimeDiagnosticsConfigWriter
    {
        public const string UserConfigPath = "user://config.toml";

        public static bool SaveUserConfig(RuntimeDiagnosticsConfig config, out string savedPath, out string errorMessage)
        {
            savedPath = UserConfigPath;
            errorMessage = "";
            try
            {
                string text = BuildToml(config ?? RuntimeDiagnosticsConfig.CreateDefault());
                using var file = Godot.FileAccess.Open(UserConfigPath, Godot.FileAccess.ModeFlags.Write);
                if (file == null)
                {
                    errorMessage = Godot.FileAccess.GetOpenError().ToString();
                    return false;
                }
                file.StoreString(text);
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.GetType().Name + ": " + ex.Message;
                return false;
            }
        }

        public static string BuildToml(RuntimeDiagnosticsConfig c)
        {
            var sb = new StringBuilder(12000);
            sb.AppendLine("# gEmuera 运行时诊断配置");
            sb.AppendLine("# 企业级说明：本文件由运行时调试面板生成，保存到 user://config.toml。");
            sb.AppendLine("# Android/APK 默认应保持高频日志关闭；排查完成后请关闭 debug_model 和具体模块。");
            sb.AppendLine("# GDPrint 页会显示 game:// 解析后的启动游戏目录；诊断日志默认写在游戏目录，避免用户寻找 user://。");
            sb.AppendLine("# active_debug_model 是 TOML 根字段，必须位于第一个 section 之前。");
            sb.AppendLine("active_debug_model = " + Str(c.ActiveDebugModel));
            sb.AppendLine();
            sb.AppendLine("[quick_debug]");
            sb.AppendLine("# 人用简化入口。normal 面向 APK 常规游玩；custom 表示完全按下方专家细项执行。");
            sb.AppendLine("enabled = " + Bool(c.QuickDebugEnabled));
            sb.AppendLine("preset = " + Str(c.QuickDebugPreset));
            sb.AppendLine("language = " + Str(c.QuickDebugLanguage));
            sb.AppendLine("apk_safe = " + Bool(c.QuickDebugApkSafe));
            sb.AppendLine("mirror_non_error_to_godot = " + Bool(c.QuickDebugMirrorNonErrorToGodot));
            sb.AppendLine("runtime_panel = " + Bool(c.QuickDebugRuntimePanel));
            sb.AppendLine("diagnostic_package = " + Bool(c.QuickDebugDiagnosticPackage));
            sb.AppendLine();
            sb.AppendLine("# 这些是 preset 之外的额外开启项；false 表示不额外开启，不反向关闭 preset 已开启项。");
            sb.AppendLine("[quick_debug.modules]");
            sb.AppendLine("touch = " + Bool(c.QuickModules.Touch));
            sb.AppendLine("input = " + Bool(c.QuickModules.Input));
            sb.AppendLine("image = " + Bool(c.QuickModules.Image));
            sb.AppendLine("ui_layout = " + Bool(c.QuickModules.UiLayout));
            sb.AppendLine("resource = " + Bool(c.QuickModules.Resource));
            sb.AppendLine("load_save = " + Bool(c.QuickModules.LoadSave));
            sb.AppendLine("android_storage = " + Bool(c.QuickModules.AndroidStorage));
            sb.AppendLine("performance_sampling = " + Bool(c.QuickModules.PerformanceSampling));
            sb.AppendLine("snapshot = " + Bool(c.QuickModules.Snapshot));
            sb.AppendLine("input_replay = " + Bool(c.QuickModules.InputReplay));
            sb.AppendLine();

            WriteDebugModel(sb, "debug_model_zh_cn", c.DebugModelZhCn, "中文诊断档，面向本地排查。");
            WriteDebugModel(sb, "debug_model_jp", c.DebugModelJp, "日文诊断档，用于对照日文资源。");
            WriteDebugModel(sb, "debug_model_en", c.DebugModelEn, "英文诊断档，用于英文报告或外部工具分析。");

            sb.AppendLine("[logging]");
            sb.AppendLine("# 全局最低日志等级。APK 推荐 error，需要诊断时临时改为 debug。");
            sb.AppendLine("# export_directory 默认 game://，表示本次启动的游戏目录。");
            sb.AppendLine("level = " + Str(c.LoggingLevel));
            sb.AppendLine("mirror_non_error_to_godot = " + Bool(c.LoggingMirrorNonErrorToGodot));
            sb.AppendLine("diagnostic_ring_capacity = " + c.LoggingDiagnosticRingCapacity.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("max_message_chars = " + c.LoggingMaxMessageChars.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("export_directory = " + Str(c.LoggingExportDirectory));
            sb.AppendLine("schema_version = " + c.LoggingSchemaVersion.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("session_id_format = " + Str(c.LoggingSessionIdFormat));
            sb.AppendLine();

            sb.AppendLine("[logging.categories]");
            sb.AppendLine("# 一级类别开关。debug_model 启用后才会输出低于 error 的类别日志。");
            sb.AppendLine("general = " + Bool(c.Categories.General));
            sb.AppendLine("sprite = " + Bool(c.Categories.Sprite));
            sb.AppendLine("audio = " + Bool(c.Categories.Audio));
            sb.AppendLine("input = " + Bool(c.Categories.Input));
            sb.AppendLine("script = " + Bool(c.Categories.Script));
            sb.AppendLine("ui = " + Bool(c.Categories.UI));
            sb.AppendLine("file_system = " + Bool(c.Categories.FileSystem));
            sb.AppendLine("load = " + Bool(c.Categories.Load));
            sb.AppendLine("save = " + Bool(c.Categories.Save));
            sb.AppendLine("config = " + Bool(c.Categories.Config));
            sb.AppendLine("performance = " + Bool(c.Categories.Performance));
            sb.AppendLine("touch = " + Bool(c.Categories.Touch));
            sb.AppendLine("statement_recognition = " + Bool(c.Categories.StatementRecognition));
            sb.AppendLine();

            sb.AppendLine("[logging.correlation]");
            sb.AppendLine("enabled = " + Bool(c.CorrelationEnabled));
            sb.AppendLine("session_id = " + Bool(c.CorrelationSessionId));
            sb.AppendLine("input_id = " + Bool(c.CorrelationInputId));
            sb.AppendLine("render_batch_id = " + Bool(c.CorrelationRenderBatchId));
            sb.AppendLine("line_part_id = " + Bool(c.CorrelationLinePartId));
            sb.AppendLine("image_id = " + Bool(c.CorrelationImageId));
            sb.AppendLine();

            sb.AppendLine("[logging.rate_limit]");
            sb.AppendLine("# 高频日志限流。移动端不建议关闭。");
            sb.AppendLine("enabled = " + Bool(c.RateLimitEnabled));
            sb.AppendLine("default_per_second = " + c.RateLimitDefaultPerSecond.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("default_per_frame = " + c.RateLimitDefaultPerFrame.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("per_event_per_second = " + c.RateLimitPerEventPerSecond.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("record_dropped_count = " + Bool(c.RateLimitRecordDroppedCount));
            sb.AppendLine();

            sb.AppendLine("[logging.redaction]");
            sb.AppendLine("# 路径、脚本文本和用户输入脱敏。默认开启。");
            sb.AppendLine("enabled = " + Bool(c.RedactionEnabled));
            sb.AppendLine("normalize_paths = " + Bool(c.RedactionNormalizePaths));
            sb.AppendLine("path_mode = " + Str(c.RedactionPathMode));
            sb.AppendLine("max_path_chars = " + c.RedactionMaxPathChars.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("max_script_text_chars = " + c.RedactionMaxScriptTextChars.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("max_user_text_chars = " + c.RedactionMaxUserTextChars.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("replace_newlines = " + Bool(c.RedactionReplaceNewlines));
            sb.AppendLine("hash_sensitive_text = " + Bool(c.RedactionHashSensitiveText));
            sb.AppendLine();

            sb.AppendLine("[debug.touch]");
            sb.AppendLine("enabled = " + Bool(c.TouchEnabled));
            sb.AppendLine("pointer = " + Bool(c.TouchPointer));
            sb.AppendLine("drag = " + Bool(c.TouchDrag));
            sb.AppendLine("pinch = " + Bool(c.TouchPinch));
            sb.AppendLine("inertia = " + Bool(c.TouchInertia));
            sb.AppendLine("scroll = " + Bool(c.TouchScroll));
            sb.AppendLine("drag_interval_ms = " + c.TouchDragIntervalMs.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("core_burst_line_count = " + c.TouchCoreBurstLineCount.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine();

            sb.AppendLine("[debug.statement_recognition]");
            sb.AppendLine("enabled = " + Bool(c.StatementRecognitionEnabled));
            sb.AppendLine("erb_load = " + Bool(c.StatementRecognitionErbLoad));
            sb.AppendLine("logical_line = " + Bool(c.StatementRecognitionLogicalLine));
            sb.AppendLine("expression = " + Bool(c.StatementRecognitionExpression));
            sb.AppendLine("variable = " + Bool(c.StatementRecognitionVariable));
            sb.AppendLine("function_call = " + Bool(c.StatementRecognitionFunctionCall));
            sb.AppendLine("max_lines_per_burst = " + c.StatementRecognitionMaxLinesPerBurst.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine();

            sb.AppendLine("[debug.input]");
            sb.AppendLine("enabled = " + Bool(c.InputDebugEnabled));
            sb.AppendLine("submit = " + Bool(c.InputDebugSubmit));
            sb.AppendLine("consume = " + Bool(c.InputDebugConsume));
            sb.AppendLine("button = " + Bool(c.InputDebugButton));
            sb.AppendLine();

            sb.AppendLine("[debug.performance]");
            sb.AppendLine("enabled = " + Bool(c.PerformanceEnabled));
            sb.AppendLine("startup_timing = " + Bool(c.PerformanceStartupTiming));
            sb.AppendLine("ui_queue = " + Bool(c.PerformanceUiQueue));
            sb.AppendLine("resource_loading = " + Bool(c.PerformanceResourceLoading));
            sb.AppendLine();

            sb.AppendLine("[debug.load]");
            sb.AppendLine("enabled = " + Bool(c.LoadDebugEnabled));
            sb.AppendLine("erb = " + Bool(c.LoadDebugErb));
            sb.AppendLine("csv = " + Bool(c.LoadDebugCsv));
            sb.AppendLine("resources = " + Bool(c.LoadDebugResources));
            sb.AppendLine();

            sb.AppendLine("[debug.save]");
            sb.AppendLine("enabled = " + Bool(c.SaveDebugEnabled));
            sb.AppendLine("read = " + Bool(c.SaveDebugRead));
            sb.AppendLine("write = " + Bool(c.SaveDebugWrite));
            sb.AppendLine("compatibility_fallback = " + Bool(c.SaveDebugCompatibilityFallback));
            sb.AppendLine();

            sb.AppendLine("[debug.resource]");
            sb.AppendLine("enabled = " + Bool(c.ResourceDebugEnabled));
            sb.AppendLine("sprite = " + Bool(c.ResourceDebugSprite));
            sb.AppendLine("audio = " + Bool(c.ResourceDebugAudio));
            sb.AppendLine("sqlite = " + Bool(c.ResourceDebugSqlite));
            sb.AppendLine();

            sb.AppendLine("[debug.image]");
            sb.AppendLine("enabled = " + Bool(c.ImageDebugEnabled));
            sb.AppendLine("resolve = " + Bool(c.ImageDebugResolve));
            sb.AppendLine("texture = " + Bool(c.ImageDebugTexture));
            sb.AppendLine("render_rect = " + Bool(c.ImageDebugRenderRect));
            sb.AppendLine("cache = " + Bool(c.ImageDebugCache));
            sb.AppendLine("color_matrix = " + Bool(c.ImageDebugColorMatrix));
            sb.AppendLine("log_success = " + Bool(c.ImageDebugLogSuccess));
            sb.AppendLine("max_records_per_frame = " + c.ImageDebugMaxRecordsPerFrame.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine();

            sb.AppendLine("[debug.ui_layout]");
            sb.AppendLine("enabled = " + Bool(c.UiLayoutEnabled));
            sb.AppendLine("target_rect = " + Bool(c.UiLayoutTargetRect));
            sb.AppendLine("actual_rect = " + Bool(c.UiLayoutActualRect));
            sb.AppendLine("mismatch_only = " + Bool(c.UiLayoutMismatchOnly));
            sb.AppendLine("mismatch_threshold_px = " + c.UiLayoutMismatchThresholdPx.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("text = " + Bool(c.UiLayoutText));
            sb.AppendLine("button = " + Bool(c.UiLayoutButton));
            sb.AppendLine("image = " + Bool(c.UiLayoutImage));
            sb.AppendLine("shape = " + Bool(c.UiLayoutShape));
            sb.AppendLine("html_div = " + Bool(c.UiLayoutHtmlDiv));
            sb.AppendLine("html_img = " + Bool(c.UiLayoutHtmlImg));
            sb.AppendLine("cbg = " + Bool(c.UiLayoutCbg));
            sb.AppendLine("include_text = " + Bool(c.UiLayoutIncludeText));
            sb.AppendLine("max_text_chars = " + c.UiLayoutMaxTextChars.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("max_records_per_frame = " + c.UiLayoutMaxRecordsPerFrame.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine();

            sb.AppendLine("[debug.lifecycle]");
            sb.AppendLine("enabled = " + Bool(c.LifecycleEnabled));
            sb.AppendLine("android_pause_resume = " + Bool(c.LifecycleAndroidPauseResume));
            sb.AppendLine();

            sb.AppendLine("[debug.runtime_panel]");
            sb.AppendLine("# 开启后主运行场景会显示日志配置悬浮窗入口。");
            sb.AppendLine("enabled = " + Bool(c.RuntimePanelEnabled));
            sb.AppendLine("allow_runtime_toggle = " + Bool(c.RuntimePanelAllowRuntimeToggle));
            sb.AppendLine("persist_changes = " + Bool(c.RuntimePanelPersistChanges));
            sb.AppendLine("show_active_modules = " + Bool(c.RuntimePanelShowActiveModules));
            sb.AppendLine("show_ring_buffer_stats = " + Bool(c.RuntimePanelShowRingBufferStats));
            sb.AppendLine();

            sb.AppendLine("[diagnostic_package]");
            sb.AppendLine("enabled = " + Bool(c.DiagnosticPackageEnabled));
            sb.AppendLine("include_log = " + Bool(c.DiagnosticPackageIncludeLog));
            sb.AppendLine("include_config_snapshot = " + Bool(c.DiagnosticPackageIncludeConfigSnapshot));
            sb.AppendLine("include_device_info = " + Bool(c.DiagnosticPackageIncludeDeviceInfo));
            sb.AppendLine("include_screen_info = " + Bool(c.DiagnosticPackageIncludeScreenInfo));
            sb.AppendLine("include_godot_info = " + Bool(c.DiagnosticPackageIncludeGodotInfo));
            sb.AppendLine("include_apk_info = " + Bool(c.DiagnosticPackageIncludeApkInfo));
            sb.AppendLine("include_game_path = " + Bool(c.DiagnosticPackageIncludeGamePath));
            sb.AppendLine("include_error_summary = " + Bool(c.DiagnosticPackageIncludeErrorSummary));
            sb.AppendLine();

            sb.AppendLine("[diagnostic_summary]");
            sb.AppendLine("enabled = " + Bool(c.DiagnosticSummaryEnabled));
            sb.AppendLine("top_event_ids = " + c.DiagnosticSummaryTopEventIds.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("top_ui_layout_mismatches = " + c.DiagnosticSummaryTopUiLayoutMismatches.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("top_image_failures = " + c.DiagnosticSummaryTopImageFailures.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine();

            sb.AppendLine("[debug.ui_overlay]");
            sb.AppendLine("enabled = " + Bool(c.UiOverlayEnabled));
            sb.AppendLine("target_rect = " + Bool(c.UiOverlayTargetRect));
            sb.AppendLine("actual_rect = " + Bool(c.UiOverlayActualRect));
            sb.AppendLine("mismatch = " + Bool(c.UiOverlayMismatch));
            sb.AppendLine("image_rect = " + Bool(c.UiOverlayImageRect));
            sb.AppendLine("button_rect = " + Bool(c.UiOverlayButtonRect));
            sb.AppendLine("max_drawn_rects = " + c.UiOverlayMaxDrawnRects.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine();

            sb.AppendLine("[debug.reference]");
            sb.AppendLine("enabled = " + Bool(c.ReferenceEnabled));
            sb.AppendLine("reference_name = " + Str(c.ReferenceName));
            sb.AppendLine("expected_from_reference = " + Bool(c.ReferenceExpectedFromReference));
            sb.AppendLine("reference_note = " + Bool(c.ReferenceNote));
            sb.AppendLine();

            sb.AppendLine("[diagnostic_breadcrumb]");
            sb.AppendLine("enabled = " + Bool(c.BreadcrumbEnabled));
            sb.AppendLine("# 默认写入启动游戏目录下的 gemuera-last-session-breadcrumb.log。");
            sb.AppendLine("path = " + Str(c.BreadcrumbPath));
            sb.AppendLine("max_bytes = " + c.BreadcrumbMaxBytes.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("write_on_startup = " + Bool(c.BreadcrumbWriteOnStartup));
            sb.AppendLine("write_on_config_loaded = " + Bool(c.BreadcrumbWriteOnConfigLoaded));
            sb.AppendLine("write_on_game_path_selected = " + Bool(c.BreadcrumbWriteOnGamePathSelected));
            sb.AppendLine("write_on_severe_error = " + Bool(c.BreadcrumbWriteOnSevereError));
            sb.AppendLine("write_on_export = " + Bool(c.BreadcrumbWriteOnExport));
            sb.AppendLine("write_on_shutdown = " + Bool(c.BreadcrumbWriteOnShutdown));
            sb.AppendLine();

            sb.AppendLine("[debug.input_replay]");
            sb.AppendLine("enabled = " + Bool(c.InputReplayEnabled));
            sb.AppendLine("max_events = " + c.InputReplayMaxEvents.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("capture_touch = " + Bool(c.InputReplayCaptureTouch));
            sb.AppendLine("capture_button = " + Bool(c.InputReplayCaptureButton));
            sb.AppendLine("capture_keyboard = " + Bool(c.InputReplayCaptureKeyboard));
            sb.AppendLine("capture_wait_state = " + Bool(c.InputReplayCaptureWaitState));
            sb.AppendLine("capture_timing = " + Bool(c.InputReplayCaptureTiming));
            sb.AppendLine("max_text_chars = " + c.InputReplayMaxTextChars.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine();

            sb.AppendLine("[diagnostic_retention]");
            sb.AppendLine("enabled = " + Bool(c.RetentionEnabled));
            sb.AppendLine("# 默认只清理启动游戏目录下 gEmuera 生成的诊断文件，不会清理游戏资源。");
            sb.AppendLine("directory = " + Str(c.RetentionDirectory));
            sb.AppendLine("max_packages = " + c.RetentionMaxPackages.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("max_total_mb = " + c.RetentionMaxTotalMb.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("cleanup_on_startup = " + Bool(c.RetentionCleanupOnStartup));
            sb.AppendLine("cleanup_before_export = " + Bool(c.RetentionCleanupBeforeExport));
            sb.AppendLine("protect_current_session = " + Bool(c.RetentionProtectCurrentSession));
            sb.AppendLine();

            sb.AppendLine("[debug.android_storage]");
            sb.AppendLine("enabled = " + Bool(c.AndroidStorageEnabled));
            sb.AppendLine("log_permissions = " + Bool(c.AndroidStorageLogPermissions));
            sb.AppendLine("log_game_scan = " + Bool(c.AndroidStorageLogGameScan));
            sb.AppendLine("log_path_selection = " + Bool(c.AndroidStorageLogPathSelection));
            sb.AppendLine("log_read_write_failures = " + Bool(c.AndroidStorageLogReadWriteFailures));
            sb.AppendLine("log_scoped_storage = " + Bool(c.AndroidStorageLogScopedStorage));
            sb.AppendLine("max_path_records = " + c.AndroidStorageMaxPathRecords.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine();

            sb.AppendLine("[debug.performance_sampling]");
            sb.AppendLine("enabled = " + Bool(c.PerformanceSamplingEnabled));
            sb.AppendLine("sample_interval_ms = " + c.PerformanceSamplingIntervalMs.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("include_fps = " + Bool(c.PerformanceSamplingIncludeFps));
            sb.AppendLine("include_frame_ms = " + Bool(c.PerformanceSamplingIncludeFrameMs));
            sb.AppendLine("include_ui_queue = " + Bool(c.PerformanceSamplingIncludeUiQueue));
            sb.AppendLine("include_texture_queue = " + Bool(c.PerformanceSamplingIncludeTextureQueue));
            sb.AppendLine("include_ring_buffer = " + Bool(c.PerformanceSamplingIncludeRingBuffer));
            sb.AppendLine("include_dropped_count = " + Bool(c.PerformanceSamplingIncludeDroppedCount));
            sb.AppendLine("include_memory = " + Bool(c.PerformanceSamplingIncludeMemory));
            sb.AppendLine();

            sb.AppendLine("[diagnostic_snapshot]");
            sb.AppendLine("enabled = " + Bool(c.SnapshotEnabled));
            sb.AppendLine("include_screenshot = " + Bool(c.SnapshotIncludeScreenshot));
            sb.AppendLine("include_layout_snapshot = " + Bool(c.SnapshotIncludeLayoutSnapshot));
            sb.AppendLine("include_visible_ui_rects = " + Bool(c.SnapshotIncludeVisibleUiRects));
            sb.AppendLine("include_image_rects = " + Bool(c.SnapshotIncludeImageRects));
            sb.AppendLine("max_nodes = " + c.SnapshotMaxNodes.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("max_file_kb = " + c.SnapshotMaxFileKb.ToString(CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        static void WriteDebugModel(StringBuilder sb, string sectionName, RuntimeDiagnosticsConfig.DebugModelProfile model, string note)
        {
            sb.AppendLine("[" + sectionName + "]");
            sb.AppendLine("# " + note);
            sb.AppendLine("enabled = " + Bool(model.Enabled));
            sb.AppendLine("language = " + Str(model.Language));
            sb.AppendLine("log_level = " + Str(model.LogLevel));
            sb.AppendLine("mirror_to_godot = " + Bool(model.MirrorToGodot));
            sb.AppendLine("scroll_trace = " + Bool(model.ScrollTrace));
            sb.AppendLine();
        }

        static string Bool(bool value) => value ? "true" : "false";

        static string Str(string value)
        {
            value ??= "";
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }
    }
}
