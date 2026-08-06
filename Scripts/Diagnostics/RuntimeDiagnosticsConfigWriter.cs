using System;
using System.Text;
using Godot;

namespace gEmuera.Diagnostics
{
    /// <summary>
    /// 运行时诊断配置写入器。只写出精简开关，避免面板保存后重新生成旧版专家配置。
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
            c ??= RuntimeDiagnosticsConfig.CreateDefault();
            var sb = new StringBuilder(512);
            sb.AppendLine("# gEmuera 运行时日志/诊断配置");
            sb.AppendLine("# enabled=false 表示日志/诊断系统完全关闭。");
            sb.AppendLine("# migration.session_isolation 仅在下次启动时生效；默认 false 保持旧版启动链。");
            sb.AppendLine("[migration]");
            sb.AppendLine("session_isolation = " + Bool(c.MigrationSessionIsolationEnabled));
            sb.AppendLine();
            sb.AppendLine("[logging]");
            sb.AppendLine("enabled = " + Bool(c.LoggingEnabled));
            sb.AppendLine("level = \"" + c.LoggingLevel + "\"");
            sb.AppendLine("file_sink = " + Bool(c.FileSinkEnabled));
            sb.AppendLine("file_sink_level = \"" + c.FileSinkLevel + "\"");
            sb.AppendLine("panel_visible = " + Bool(c.RuntimePanelEnabled));
            sb.AppendLine();
            sb.AppendLine("touch = " + Bool(c.TouchEnabled));
            sb.AppendLine("input = " + Bool(c.InputDebugEnabled));
            sb.AppendLine("image = " + Bool(c.ImageDebugEnabled));
            sb.AppendLine("ui_layout = " + Bool(c.UiLayoutEnabled));
            sb.AppendLine("dynamic_map = " + Bool(c.DynamicMapDebugEnabled));
            sb.AppendLine("resource = " + Bool(c.ResourceDebugEnabled));
            sb.AppendLine("load_save = " + Bool(c.LoadDebugEnabled || c.SaveDebugEnabled));
            sb.AppendLine("android_storage = " + Bool(c.AndroidStorageEnabled));
            sb.AppendLine("performance = " + Bool(c.PerformanceEnabled || c.PerformanceSamplingEnabled));
            sb.AppendLine("statement_recognition = " + Bool(c.StatementRecognitionEnabled));
            // 分类掩码必须持久化，否则设置页的“日志分类”勾选重启/热重载后全部还原为关，
            // minimal 展开（DisableAllDiagnostics）也会把掩码清空（见 loader 的恢复逻辑）。
            sb.AppendLine();
            sb.AppendLine("[logging.categories]");
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
            return sb.ToString();
        }

        static string Bool(bool value) => value ? "true" : "false";
    }
}
