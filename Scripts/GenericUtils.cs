using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Godot;
using MinorShift.Emuera.GameView;

public enum EmueraLogLevel
{
    Debug = 0,
    Info = 1,
    Warn = 2,
    Error = 3,
    None = 4
}

[Flags]
public enum EmueraLogCategory
{
    None = 0,
    General = 1 << 0,
    Sprite = 1 << 1,
    Audio = 1 << 2,
    Input = 1 << 3,
    Script = 1 << 4,
    UI = 1 << 5,
    FileSystem = 1 << 6,
    Load = 1 << 7,
    Save = 1 << 8,
    Config = 1 << 9,
    Performance = 1 << 10,
    All = int.MaxValue
}

internal static class GenericUtils
{
    static readonly ConcurrentQueue<LogRecord> logQueue = new ConcurrentQueue<LogRecord>();
    static readonly ConcurrentQueue<Action> uiQueue = new ConcurrentQueue<Action>();
    static int mainThreadId = -1;
    static int pendingUiActions = 0;
    static int pendingDisplayActions = 0;
    static int uiFrameGeneration = 0;
    const ulong AndroidUiBudgetUsec = 9000;
    const ulong DesktopUiBudgetUsec = 7000;
    const int AndroidMaxUiActionsPerFrame = 128;
    const int DesktopMaxUiActionsPerFrame = 96;
    const int SnakeSoundChannelCount = 10;
    static readonly object snakeAudioLock = new object();
    static readonly SnakeAudioState[] snakeSounds = CreateSnakeAudioStates();
    static readonly SnakeAudioState snakeBgm = new SnakeAudioState();
    static readonly object inputStateLock = new object();
    static uEmuera.Drawing.Point pointerPosition = uEmuera.Drawing.Point.Empty;
    static string pointingButtonInput = "";
    static long pointingButtonGeneration = long.MinValue;
    static bool pointingButtonActive = false;
    static int scrollTraceEnabled = OS.IsDebugBuild() ? 1 : 0;
    static int scrollTraceSequence = 0;
    static int scrollTraceCoreLinesRemaining = 0;
    const string ScrollTracePrefix = "[SCROLL_TRACE]";
    const int ScrollTraceCoreBurstLineCount = 120;
    const int DiagnosticLogCapacity = 1000;
    const int MaxLogMessageChars = 8192;
    static readonly object diagnosticLogLock = new object();
    static readonly LogRecord[] diagnosticLogRing = new LogRecord[DiagnosticLogCapacity];
    static int diagnosticLogStart = 0;
    static int diagnosticLogCount = 0;
    static long diagnosticLogSequence = 0;
#if DEBUG || GEMUERA_DIAGNOSTIC_LOGS
    const bool VerboseLogBuild = true;
#else
    const bool VerboseLogBuild = false;
#endif
    static int runtimeLogLevel = (int)(OS.IsDebugBuild() ? EmueraLogLevel.Debug : EmueraLogLevel.Error);
    static int runtimeLogCategories = (int)EmueraLogCategory.All;
    static int mirrorNonErrorLogsToGodot = OS.IsDebugBuild() ? 1 : 0;

    public static bool HasPendingUIWork => Volatile.Read(ref pendingUiActions) > 0;
    public static bool HasPendingDisplayWork => Volatile.Read(ref pendingDisplayActions) > 0;
    public static int UiFrameGeneration => Volatile.Read(ref uiFrameGeneration);
    public static bool ScrollTraceEnabled
    {
        get => Volatile.Read(ref scrollTraceEnabled) != 0;
        set => Volatile.Write(ref scrollTraceEnabled, value ? 1 : 0);
    }

    readonly struct LogRecord
    {
        public readonly long Sequence;
        public readonly DateTimeOffset UtcTime;
        public readonly long MonoMs;
        public readonly EmueraLogLevel Level;
        public readonly EmueraLogCategory Category;
        public readonly int ThreadId;
        public readonly string Source;
        public readonly string Member;
        public readonly int Line;
        public readonly string Message;

        public LogRecord(long sequence, DateTimeOffset utcTime, long monoMs, EmueraLogLevel level,
            EmueraLogCategory category, int threadId, string source, string member, int line, string message)
        {
            Sequence = sequence;
            UtcTime = utcTime;
            MonoMs = monoMs;
            Level = level;
            Category = category;
            ThreadId = threadId;
            Source = source ?? "";
            Member = member ?? "";
            Line = line;
            Message = message ?? "";
        }

        public string FormatForGodot()
        {
            return $"[{Level.ToString().ToUpperInvariant()}][{Category}] {Source}:{Line} {Member} | {Message}";
        }

        public string FormatForExport()
        {
            return "seq=" + Sequence.ToString("D6")
                + " utc=" + UtcTime.ToString("O")
                + " mono_ms=" + MonoMs
                + " level=" + Level.ToString().ToUpperInvariant()
                + " category=" + Category
                + " thread=" + ThreadId
                + " source=" + Source + ":" + Line
                + " member=" + Member
                + " message=\"" + EscapeForSingleLine(Message) + "\"";
        }
    }

    sealed class SnakeAudioState
    {
        public string Path;
        public bool Playing;
        public bool PendingStart;
        public bool Paused;
        public int Volume = 100;
        public int Speed = 100;
        public long StartedAtMs;
        public long TotalMs;
        public long LastKnownCurrentMs;
    }

    public struct SnakeAudioInfo
    {
        public long TotalMs;
        public long CurrentMs;
        public long Playing;
        public long Volume;
        public long Speed;
    }

    static SnakeAudioState[] CreateSnakeAudioStates()
    {
        var states = new SnakeAudioState[SnakeSoundChannelCount];
        for (int i = 0; i < states.Length; i++)
            states[i] = new SnakeAudioState();
        return states;
    }

    public static void SetMainThread()
    {
        mainThreadId = System.Environment.CurrentManagedThreadId;
    }

    static bool IsMainThread()
    {
        return mainThreadId < 0 || System.Environment.CurrentManagedThreadId == mainThreadId;
    }

    public static bool IsOnMainThread()
    {
        return IsMainThread();
    }

    public static void SetPointerPosition(float x, float y)
    {
        var point = new uEmuera.Drawing.Point((int)MathF.Round(x), (int)MathF.Round(y));
        lock (inputStateLock)
            pointerPosition = point;
        uEmuera.Forms.Control.MousePosition = point;
    }

    public static uEmuera.Drawing.Point GetPointerPosition()
    {
        lock (inputStateLock)
            return pointerPosition;
    }

    public static void SetPointingButton(string input, long generation)
    {
        lock (inputStateLock)
        {
            pointingButtonInput = input ?? "";
            pointingButtonGeneration = generation;
            pointingButtonActive = true;
        }
    }

    public static void ClearPointingButton(long generation = long.MinValue)
    {
        lock (inputStateLock)
        {
            if (generation != long.MinValue && pointingButtonGeneration != generation)
                return;
            pointingButtonInput = "";
            pointingButtonGeneration = long.MinValue;
            pointingButtonActive = false;
        }
    }

    public static string GetPointingButtonInput()
    {
        lock (inputStateLock)
            return pointingButtonActive ? pointingButtonInput ?? "" : "";
    }

    public static void FlushLogs()
    {
        int maxLogs = OS.IsDebugBuild() ? 64 : 16;
        int count = 0;
        while (count < maxLogs && logQueue.TryDequeue(out var item))
        {
            WriteLog(item);
            count++;
        }
    }

    public static void FlushUI()
    {
        int maxActions = OS.GetName() == "Android" ? AndroidMaxUiActionsPerFrame : DesktopMaxUiActionsPerFrame;
        ulong budgetUsec = OS.GetName() == "Android" ? AndroidUiBudgetUsec : DesktopUiBudgetUsec;
        ulong startUsec = Time.GetTicksUsec();
        int count = 0;
        while (count < maxActions && uiQueue.TryDequeue(out var action))
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Error(EmueraLogCategory.UI, () => $"[UI Queue] {ex}");
            }
            count++;
            if (Time.GetTicksUsec() - startUsec >= budgetUsec)
                break;
        }
        Interlocked.Increment(ref uiFrameGeneration);
    }

    public static void WaitForUiFrameAfter(int generation, int timeoutMs)
    {
        if (IsMainThread())
            return;
        long deadline = GetTickMs() + Math.Max(0, timeoutMs);
        while (Volatile.Read(ref uiFrameGeneration) <= generation && GetTickMs() < deadline)
            Thread.Sleep(1);
    }

    public static void WaitForDisplayWorkDrained(int timeoutMs)
    {
        if (IsMainThread())
            return;
        long deadline = GetTickMs() + Math.Max(0, timeoutMs);
        while (Volatile.Read(ref pendingDisplayActions) > 0 && GetTickMs() < deadline)
            Thread.Sleep(1);
    }

    static void EnqueueUI(Action action, bool displayWork = false)
    {
        Interlocked.Increment(ref pendingUiActions);
        if (displayWork)
            Interlocked.Increment(ref pendingDisplayActions);

        uiQueue.Enqueue(() =>
        {
            try
            {
                action();
            }
            finally
            {
                if (displayWork)
                    Interlocked.Decrement(ref pendingDisplayActions);
                Interlocked.Decrement(ref pendingUiActions);
            }
        });
    }

    public static EmueraLogLevel RuntimeLogLevel
    {
        get => (EmueraLogLevel)Volatile.Read(ref runtimeLogLevel);
        set => Volatile.Write(ref runtimeLogLevel, (int)value);
    }

    public static EmueraLogCategory RuntimeLogCategories
    {
        get => (EmueraLogCategory)Volatile.Read(ref runtimeLogCategories);
        set => Volatile.Write(ref runtimeLogCategories, (int)value);
    }

    public static bool MirrorNonErrorLogsToGodot
    {
        get => Volatile.Read(ref mirrorNonErrorLogsToGodot) != 0;
        set => Volatile.Write(ref mirrorNonErrorLogsToGodot, value ? 1 : 0);
    }

    public static void InitializeLogging()
    {
        if (VerboseLogBuild && HasVerboseCommandLine())
        {
            RuntimeLogLevel = EmueraLogLevel.Debug;
            RuntimeLogCategories = EmueraLogCategory.All;
            MirrorNonErrorLogsToGodot = true;
        }
    }

    /// <summary>
    /// Central runtime gate for all diagnostic logs. Normal Release APKs compile out
    /// non-error call sites; this gate remains for Debug and diagnostic APKs where
    /// players may enable only the categories needed for a bug report.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsLogEnabled(EmueraLogLevel level, EmueraLogCategory category = EmueraLogCategory.General)
    {
        if (level == EmueraLogLevel.None)
            return false;
        if (!VerboseLogBuild && level < EmueraLogLevel.Error)
            return false;
        if ((int)level < Volatile.Read(ref runtimeLogLevel))
            return false;
        if (level < EmueraLogLevel.Error
            && category != EmueraLogCategory.None
            && (Volatile.Read(ref runtimeLogCategories) & (int)category) == 0)
        {
            return false;
        }
        return true;
    }

    [Conditional("DEBUG")]
    [Conditional("GEMUERA_DIAGNOSTIC_LOGS")]
    public static void Debug(object content,
        EmueraLogCategory category = EmueraLogCategory.General,
        [CallerMemberName] string member = "",
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0)
    {
        LogInternal(EmueraLogLevel.Debug, category, content, null, member, file, line);
    }

    [Conditional("DEBUG")]
    [Conditional("GEMUERA_DIAGNOSTIC_LOGS")]
    public static void Debug(EmueraLogCategory category, Func<string> messageFactory,
        [CallerMemberName] string member = "",
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0)
    {
        LogInternal(EmueraLogLevel.Debug, category, null, messageFactory, member, file, line);
    }

    [Conditional("DEBUG")]
    [Conditional("GEMUERA_DIAGNOSTIC_LOGS")]
    public static void Info(object content,
        EmueraLogCategory category = EmueraLogCategory.General,
        [CallerMemberName] string member = "",
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0)
    {
        LogInternal(EmueraLogLevel.Info, category, content, null, member, file, line);
    }

    [Conditional("DEBUG")]
    [Conditional("GEMUERA_DIAGNOSTIC_LOGS")]
    public static void Info(EmueraLogCategory category, Func<string> messageFactory,
        [CallerMemberName] string member = "",
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0)
    {
        LogInternal(EmueraLogLevel.Info, category, null, messageFactory, member, file, line);
    }

    [Conditional("DEBUG")]
    [Conditional("GEMUERA_DIAGNOSTIC_LOGS")]
    public static void Warn(object content,
        EmueraLogCategory category = EmueraLogCategory.General,
        [CallerMemberName] string member = "",
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0)
    {
        LogInternal(EmueraLogLevel.Warn, category, content, null, member, file, line);
    }

    [Conditional("DEBUG")]
    [Conditional("GEMUERA_DIAGNOSTIC_LOGS")]
    public static void Warn(EmueraLogCategory category, Func<string> messageFactory,
        [CallerMemberName] string member = "",
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0)
    {
        LogInternal(EmueraLogLevel.Warn, category, null, messageFactory, member, file, line);
    }

    public static void Error(object content,
        EmueraLogCategory category = EmueraLogCategory.General,
        [CallerMemberName] string member = "",
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0)
    {
        LogInternal(EmueraLogLevel.Error, category, content, null, member, file, line);
    }

    public static void Error(EmueraLogCategory category, Func<string> messageFactory,
        [CallerMemberName] string member = "",
        [CallerFilePath] string file = "",
        [CallerLineNumber] int line = 0)
    {
        LogInternal(EmueraLogLevel.Error, category, null, messageFactory, member, file, line);
    }

    public static void LogFromBridge(EmueraLogLevel level, EmueraLogCategory category, object content,
        Func<string> messageFactory, string member, string file, int line)
    {
        LogInternal(level, category, content, messageFactory, member, file, line);
    }

    static void LogInternal(EmueraLogLevel level, EmueraLogCategory category, object content,
        Func<string> messageFactory, string member, string file, int line)
    {
        if (!IsLogEnabled(level, category))
            return;

        string message = BuildLogMessage(content, messageFactory);
        category = NormalizeLogCategory(category, message);
        var record = new LogRecord(
            Interlocked.Increment(ref diagnosticLogSequence),
            DateTimeOffset.UtcNow,
            (long)Time.GetTicksMsec(),
            level,
            category,
            System.Environment.CurrentManagedThreadId,
            NormalizeSourcePath(file),
            member,
            line,
            message);

        AppendDiagnosticLog(record);
        if (ShouldMirrorToGodot(level))
        {
            if (IsMainThread())
                WriteLog(record);
            else
                logQueue.Enqueue(record);
        }
    }

    static string BuildLogMessage(object content, Func<string> messageFactory)
    {
        string message;
        try
        {
            message = messageFactory != null ? messageFactory() : content?.ToString();
        }
        catch (Exception ex)
        {
            message = "[LOGGER] message factory failed: " + ex.GetType().Name + ": " + ex.Message;
        }
        return ClipLogMessage(message);
    }

    static bool ShouldMirrorToGodot(EmueraLogLevel level)
    {
        return level >= EmueraLogLevel.Error || Volatile.Read(ref mirrorNonErrorLogsToGodot) != 0;
    }

    static void WriteLog(LogRecord record)
    {
        string message = record.FormatForGodot();
        switch (record.Level)
        {
            case EmueraLogLevel.Warn:
                GD.PushWarning(message);
                break;
            case EmueraLogLevel.Error:
                GD.PushError(message);
                break;
            default:
                GD.Print(message);
                break;
        }
    }

    static void AppendDiagnosticLog(LogRecord record)
    {
        lock (diagnosticLogLock)
        {
            int index = (diagnosticLogStart + diagnosticLogCount) % DiagnosticLogCapacity;
            if (diagnosticLogCount == DiagnosticLogCapacity)
            {
                diagnosticLogRing[diagnosticLogStart] = record;
                diagnosticLogStart = (diagnosticLogStart + 1) % DiagnosticLogCapacity;
                return;
            }
            diagnosticLogRing[index] = record;
            diagnosticLogCount++;
        }
    }

    static LogRecord[] SnapshotDiagnosticLog()
    {
        lock (diagnosticLogLock)
        {
            var snapshot = new LogRecord[diagnosticLogCount];
            for (int i = 0; i < snapshot.Length; i++)
                snapshot[i] = diagnosticLogRing[(diagnosticLogStart + i) % DiagnosticLogCapacity];
            return snapshot;
        }
    }

    public static string GetDefaultDiagnosticLogPath(string stamp = null)
    {
        if (string.IsNullOrEmpty(stamp))
            stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        return $"user://diagnostics/gemuera-{stamp}-diagnostic.log";
    }

    /// <summary>
    /// Writes the in-memory diagnostic ring buffer only when the user asks for it.
    /// This avoids persistent storage I/O during Android gameplay while still
    /// producing a source/line-oriented report that humans and AI tools can inspect.
    /// </summary>
    public static bool ExportDiagnosticLog(string path, out string errorMessage)
    {
        errorMessage = "";
        if (string.IsNullOrEmpty(path))
            path = GetDefaultDiagnosticLogPath();

        try
        {
            string report = BuildDiagnosticReport();
            if (path.Contains("://", StringComparison.Ordinal))
            {
                if (!EnsureGodotDirectoryForFile(path, out errorMessage))
                    return false;
                using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Write);
                if (file == null)
                {
                    errorMessage = Godot.FileAccess.GetOpenError().ToString();
                    return false;
                }
                file.StoreString(report);
                return true;
            }

            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(path, report, Encoding.UTF8);
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = ex.GetType().Name + ": " + ex.Message;
            return false;
        }
    }

    static string BuildDiagnosticReport()
    {
        var snapshot = SnapshotDiagnosticLog();
        var builder = new StringBuilder(snapshot.Length * 160 + 512);
        builder.AppendLine("# gEmuera diagnostic log");
        builder.AppendLine("# GeneratedUtc=" + DateTimeOffset.UtcNow.ToString("O"));
        builder.AppendLine("# Platform=" + OS.GetName()
            + " DebugBuild=" + OS.IsDebugBuild()
            + " VerboseLogBuild=" + VerboseLogBuild
            + " RuntimeLevel=" + RuntimeLogLevel
            + " Categories=" + RuntimeLogCategories
            + " Capacity=" + DiagnosticLogCapacity);
        builder.AppendLine("# Columns: seq utc mono_ms level category thread source member message");
        builder.AppendLine();
        foreach (var record in snapshot)
            builder.AppendLine(record.FormatForExport());
        return builder.ToString();
    }

    static bool EnsureGodotDirectoryForFile(string path, out string errorMessage)
    {
        errorMessage = "";
        string normalized = path.Replace('\\', '/');
        int slash = normalized.LastIndexOf('/');
        if (slash < 0)
            return true;
        string dirPath = normalized.Substring(0, slash);
        if (dirPath.EndsWith("://", StringComparison.Ordinal))
            return true;

        if (dirPath.StartsWith("user://", StringComparison.OrdinalIgnoreCase))
        {
            using var root = DirAccess.Open("user://");
            if (root == null)
            {
                errorMessage = DirAccess.GetOpenError().ToString();
                return false;
            }
            string relative = dirPath.Substring("user://".Length);
            var result = root.MakeDirRecursive(relative);
            if (result != Godot.Error.Ok)
            {
                errorMessage = result.ToString();
                return false;
            }
            return true;
        }

        var absoluteResult = DirAccess.MakeDirRecursiveAbsolute(dirPath);
        if (absoluteResult != Godot.Error.Ok)
        {
            errorMessage = absoluteResult.ToString();
            return false;
        }
        return true;
    }

    static string NormalizeSourcePath(string file)
    {
        if (string.IsNullOrEmpty(file))
            return "<unknown>";
        string normalized = file.Replace('\\', '/');
        int scripts = normalized.LastIndexOf("/Scripts/", StringComparison.OrdinalIgnoreCase);
        if (scripts >= 0)
            return normalized.Substring(scripts + 1);
        return Path.GetFileName(normalized);
    }

    static EmueraLogCategory NormalizeLogCategory(EmueraLogCategory category, string message)
    {
        if (category != EmueraLogCategory.General || string.IsNullOrEmpty(message))
            return category;
        if (message.StartsWith("[IMG]", StringComparison.Ordinal) || message.Contains("[SpriteManager]", StringComparison.Ordinal))
            return EmueraLogCategory.Sprite;
        if (message.StartsWith("[AUDIO]", StringComparison.Ordinal))
            return EmueraLogCategory.Audio;
        if (message.StartsWith("[LOADSAVE]", StringComparison.Ordinal))
            return EmueraLogCategory.Save;
        if (message.StartsWith("[LOAD]", StringComparison.Ordinal) || message.StartsWith("[LOADTIME]", StringComparison.Ordinal))
            return EmueraLogCategory.Load;
        if (message.StartsWith("[PROC]", StringComparison.Ordinal))
            return EmueraLogCategory.Script;
        if (message.StartsWith("[CONFIG]", StringComparison.Ordinal))
            return EmueraLogCategory.Config;
        if (message.StartsWith("[FS]", StringComparison.Ordinal))
            return EmueraLogCategory.FileSystem;
        if (message.StartsWith("[UI Queue]", StringComparison.Ordinal))
            return EmueraLogCategory.UI;
        if (message.StartsWith(ScrollTracePrefix, StringComparison.Ordinal))
            return EmueraLogCategory.Script;
        return category;
    }

    static string ClipLogMessage(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "";
        value = value.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
        if (value.Length <= MaxLogMessageChars)
            return value;
        return value.Substring(0, MaxLogMessageChars) + "...<truncated>";
    }

    static string EscapeForSingleLine(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "";
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    static bool HasVerboseCommandLine()
    {
        foreach (string arg in OS.GetCmdlineArgs())
        {
            if (string.Equals(arg, "--verbose", StringComparison.OrdinalIgnoreCase)
                || string.Equals(arg, "-v", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    public static void ScrollTrace(string category, string message)
    {
        if (!ScrollTraceEnabled || !IsLogEnabled(EmueraLogLevel.Debug, EmueraLogCategory.Script))
            return;
        int seq = Interlocked.Increment(ref scrollTraceSequence);
        string formatted = $"{ScrollTracePrefix} #{seq} t={GetTickMs()} {category}: {message}";
        LogInternal(EmueraLogLevel.Debug, EmueraLogCategory.Script, formatted, null, nameof(ScrollTrace), "Scripts/GenericUtils.cs", 0);
    }

    public static void StartScrollTraceCoreWindow(string reason)
    {
        if (!ScrollTraceEnabled)
            return;
        Interlocked.Exchange(ref scrollTraceCoreLinesRemaining, ScrollTraceCoreBurstLineCount);
        ScrollTrace("core", $"window_start lines={ScrollTraceCoreBurstLineCount} reason={ClipTrace(reason)}");
    }

    public static bool TryConsumeScrollTraceCoreLine()
    {
        if (!ScrollTraceEnabled || !IsLogEnabled(EmueraLogLevel.Debug, EmueraLogCategory.Script))
            return false;
        while (true)
        {
            int current = Volatile.Read(ref scrollTraceCoreLinesRemaining);
            if (current <= 0)
                return false;
            if (Interlocked.CompareExchange(ref scrollTraceCoreLinesRemaining, current - 1, current) == current)
                return true;
        }
    }

    public static string ClipTrace(string value, int maxLength = 96)
    {
        if (string.IsNullOrEmpty(value))
            return "";
        value = value.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
        if (value.Length <= maxLength)
            return value;
        return value.Substring(0, maxLength) + "...";
    }

    public static List<string> CalcMd5List(byte[] bytes)
    {
        return CalcMd5ListForConfig(bytes);
    }

    public static List<string> CalcMd5ListForConfig(byte[] bytes)
    {
        var result = new List<string>();
        int start = 0;

        using(var md5 = MD5.Create())
        {
            for (int i = 0; i <= bytes.Length; i++)
            {
                if (i < bytes.Length && bytes[i] != 0x0A)
                    continue;

                int len = i - start;
                if (len > 0 && bytes[start + len - 1] == 0x0D)
                    len--;

                if (len > 0 && !IsWhiteSpaceBytes(bytes, start, len))
                {
                    var hash = md5.ComputeHash(bytes, start, len);
                    var sb = new StringBuilder();
                    for(int h = 0; h < hash.Length; h++)
                        sb.Append(hash[h].ToString("x2"));
                    result.Add(sb.ToString());
                }

                start = i + 1;
            }
        }

        return result;
    }

    static bool IsWhiteSpaceBytes(byte[] bytes, int start, int len)
    {
        for (int i = 0; i < len; i++)
        {
            byte b = bytes[start + i];
            if (b != (byte)' ' && b != (byte)'\t')
            {
                return false;
            }
        }
        return true;
    }

    // Shim bridge methods — connected to EmueraContent
    // All UI operations are queued when called from background thread
    public static void SetBackgroundColor(uEmuera.Drawing.Color color)
    {
        if (IsMainThread())
            EmueraContent.instance?.SetBackgroundColor(color);
        else
            EnqueueUI(() => EmueraContent.instance?.SetBackgroundColor(color));
    }

    public static void ClearText()
    {
        EnqueueUI(() => EmueraContent.instance?.Clear(), true);
    }

    public static int GetTextMaxLineNo()
    {
        return EmueraContent.instance?.GetMaxLineNo() ?? 0;
    }

    public static int GetTextMinLineNo()
    {
        return EmueraContent.instance?.GetMinLineNo() ?? 0;
    }

    public static ConsoleDisplayLine GetText(int lineno)
    {
        return EmueraContent.instance?.GetLine(lineno);
    }

    public static void RemoveTextCount(int count)
    {
        EnqueueUI(() => EmueraContent.instance?.RemoveBottomLines(count), true);
    }

    public static void AddText(ConsoleDisplayLine line, bool update)
    {
        EnqueueUI(() => EmueraContent.instance?.AddLine(line, update), true);
    }

    public static void AddTexts(IReadOnlyList<(ConsoleDisplayLine Line, bool Update)> lines)
    {
        EnqueueUI(() => EmueraContent.instance?.AddLines(lines), true);
    }

    public static void ApplyTextChanges(int removeBottomCount, IReadOnlyList<(ConsoleDisplayLine Line, bool Update)> lines, bool update, int lastButtonGeneration)
    {
        EnqueueUI(() => EmueraContent.instance?.ApplyTextChanges(removeBottomCount, lines, update, lastButtonGeneration), true);
    }

    public static void SetLastButtonGeneration(int generation)
    {
        EnqueueUI(() => EmueraContent.instance?.SetLastButtonGeneration(generation), true);
    }

    public static void TextUpdate()
    {
        EnqueueUI(() => EmueraContent.instance?.UpdateDisplay(), true);
    }

    public static void ShowIsInProcess(bool show)
    {
        EnqueueUI(() => EmueraContent.instance?.ShowIsInProcess(show));
    }

    public static void RefreshCBG(EmueraConsole console)
    {
        if (console == null)
            return;
        var cbgList = console.GetCBGList();
        EnqueueUI(() => EmueraContent.instance?.RefreshCBG(cbgList), true);
    }

    public static void PlaySoundFile(string path, bool loop)
    {
        PlaySoundFile(path, loop ? -1 : 1);
    }

    public static void PlaySoundFile(string path, int repeat)
    {
        int channel = 0;
        lock (snakeAudioLock)
        {
            for (int i = 0; i < snakeSounds.Length; i++)
            {
                if (!snakeSounds[i].Playing)
                {
                    channel = i;
                    break;
                }
            }
            var state = snakeSounds[channel];
            state.Path = path;
            state.Playing = true;
            state.PendingStart = true;
            state.Paused = false;
            state.StartedAtMs = 0;
            state.TotalMs = 0;
            state.LastKnownCurrentMs = 0;
        }
        EnqueueUI(() => EmueraContent.instance?.PlaySoundFile(path, repeat, channel));
    }

    public static void StopSounds()
    {
        lock (snakeAudioLock)
        {
            foreach (var state in snakeSounds)
            {
                state.Playing = false;
                state.PendingStart = false;
                state.Paused = false;
                state.LastKnownCurrentMs = 0;
            }
        }
        EnqueueUI(() => EmueraContent.instance?.StopSounds());
    }

    public static void PlayBgmFile(string path)
    {
        lock (snakeAudioLock)
        {
            snakeBgm.Path = path;
            snakeBgm.Playing = true;
            snakeBgm.PendingStart = true;
            snakeBgm.Paused = false;
            snakeBgm.StartedAtMs = 0;
            snakeBgm.TotalMs = 0;
            snakeBgm.LastKnownCurrentMs = 0;
        }
        EnqueueUI(() => EmueraContent.instance?.PlayBgmFile(path));
    }

    public static void StopBgm()
    {
        lock (snakeAudioLock)
        {
            snakeBgm.Playing = false;
            snakeBgm.PendingStart = false;
            snakeBgm.Paused = false;
            snakeBgm.LastKnownCurrentMs = 0;
        }
        EnqueueUI(() => EmueraContent.instance?.StopBgm());
    }

    public static void SetSoundVolume(int volume)
    {
        lock (snakeAudioLock)
        {
            foreach (var state in snakeSounds)
                state.Volume = ClampEraVolume(volume);
        }
        EnqueueUI(() => EmueraContent.instance?.SetSoundVolume(volume));
    }

    public static void SetBgmVolume(int volume)
    {
        lock (snakeAudioLock)
            snakeBgm.Volume = ClampEraVolume(volume);
        EnqueueUI(() => EmueraContent.instance?.SetBgmVolume(volume));
    }

    public static bool SoundFileExists(string name)
    {
        string path = ResolveSoundPath(name);
        return !string.IsNullOrEmpty(path) && uEmuera.Utils.FileExists(path);
    }

    public static int FindPlayingSound(int channel)
    {
        lock (snakeAudioLock)
        {
            if (channel >= 0)
                return channel < snakeSounds.Length && snakeSounds[channel].Playing && !snakeSounds[channel].Paused ? channel : -1;
            for (int i = 0; i < snakeSounds.Length; i++)
            {
                if (snakeSounds[i].Playing && !snakeSounds[i].Paused)
                    return i;
            }
        }
        return -1;
    }

    public static bool IsPlayingBgm()
    {
        lock (snakeAudioLock)
            return snakeBgm.Playing && !snakeBgm.Paused;
    }

    public static int ControlSound(int channel, int action, int speed = 100)
    {
        if (channel < 0 || channel >= SnakeSoundChannelCount)
            return -1;
        lock (snakeAudioLock)
        {
            SnakeAudioState state = snakeSounds[channel];
            switch (action)
            {
                case 0:
                    state.Paused = true;
                    state.LastKnownCurrentMs = GetCurrentAudioMs(state);
                    EnqueueUI(() => EmueraContent.instance?.PauseSoundChannel(channel, true));
                    return 1;
                case 1:
                    state.Paused = false;
                    if (!string.IsNullOrEmpty(state.Path))
                        state.Playing = true;
                    state.StartedAtMs = GetTickMs() - ScaleFromPlaybackMs(state.LastKnownCurrentMs, state.Speed);
                    EnqueueUI(() => EmueraContent.instance?.PauseSoundChannel(channel, false));
                    return 1;
                case 2:
                    state.Playing = false;
                    state.PendingStart = false;
                    state.Paused = false;
                    state.LastKnownCurrentMs = 0;
                    EnqueueUI(() => EmueraContent.instance?.StopSoundChannel(channel));
                    return 1;
                case 3:
                    state.LastKnownCurrentMs = GetCurrentAudioMs(state);
                    state.Speed = Math.Max(1, speed);
                    state.StartedAtMs = GetTickMs() - ScaleFromPlaybackMs(state.LastKnownCurrentMs, state.Speed);
                    EnqueueUI(() => EmueraContent.instance?.SetSoundChannelSpeed(channel, state.Speed / 100.0f));
                    return 1;
                default:
                    return -2;
            }
        }
    }

    public static int ControlBgm(int action, int speed = 100)
    {
        lock (snakeAudioLock)
        {
            switch (action)
            {
                case 0:
                    snakeBgm.LastKnownCurrentMs = GetCurrentAudioMs(snakeBgm);
                    snakeBgm.Paused = true;
                    EnqueueUI(() => EmueraContent.instance?.PauseBgm(true));
                    return 1;
                case 1:
                    snakeBgm.Paused = false;
                    if (!string.IsNullOrEmpty(snakeBgm.Path))
                        snakeBgm.Playing = true;
                    snakeBgm.StartedAtMs = GetTickMs() - ScaleFromPlaybackMs(snakeBgm.LastKnownCurrentMs, snakeBgm.Speed);
                    EnqueueUI(() => EmueraContent.instance?.PauseBgm(false));
                    return 1;
                case 2:
                    snakeBgm.Playing = false;
                    snakeBgm.PendingStart = false;
                    snakeBgm.Paused = false;
                    snakeBgm.LastKnownCurrentMs = 0;
                    EnqueueUI(() => EmueraContent.instance?.StopBgm());
                    return 1;
                case 3:
                    snakeBgm.LastKnownCurrentMs = GetCurrentAudioMs(snakeBgm);
                    snakeBgm.Speed = Math.Max(1, speed);
                    snakeBgm.StartedAtMs = GetTickMs() - ScaleFromPlaybackMs(snakeBgm.LastKnownCurrentMs, snakeBgm.Speed);
                    EnqueueUI(() => EmueraContent.instance?.SetBgmSpeed(snakeBgm.Speed / 100.0f));
                    return 1;
                default:
                    return -2;
            }
        }
    }

    public static SnakeAudioInfo GetAudioInfo(int channel)
    {
        lock (snakeAudioLock)
        {
            SnakeAudioState state = channel == -1 ? snakeBgm : channel >= 0 && channel < snakeSounds.Length ? snakeSounds[channel] : null;
            if (state == null)
                return default;
            return new SnakeAudioInfo
            {
                TotalMs = state.TotalMs,
                CurrentMs = GetCurrentAudioMs(state),
                Playing = state.Playing && !state.Paused ? 1 : 0,
                Volume = state.Volume,
                Speed = state.Speed
            };
        }
    }

    public static void NotifySoundPlaybackStarted(int channel, string path, long totalMs)
    {
        if (channel < 0 || channel >= SnakeSoundChannelCount)
            return;
        lock (snakeAudioLock)
        {
            var state = snakeSounds[channel];
            if (!string.Equals(state.Path, path, StringComparison.OrdinalIgnoreCase))
                return;
            state.PendingStart = false;
            state.Playing = true;
            state.Paused = false;
            state.StartedAtMs = GetTickMs();
            state.TotalMs = Math.Max(0, totalMs);
            state.LastKnownCurrentMs = 0;
        }
    }

    public static void NotifySoundPlaybackFailed(int channel, string path)
    {
        if (channel < 0 || channel >= SnakeSoundChannelCount)
            return;
        lock (snakeAudioLock)
        {
            var state = snakeSounds[channel];
            if (!string.Equals(state.Path, path, StringComparison.OrdinalIgnoreCase))
                return;
            state.Playing = false;
            state.PendingStart = false;
            state.Paused = false;
            state.LastKnownCurrentMs = 0;
        }
    }

    public static void NotifySoundPlaybackPosition(int channel, double currentSec, double totalSec, bool playing)
    {
        if (channel < 0 || channel >= SnakeSoundChannelCount)
            return;
        lock (snakeAudioLock)
        {
            var state = snakeSounds[channel];
            state.LastKnownCurrentMs = Math.Max(0, (long)(currentSec * 1000.0));
            if (totalSec > 0)
                state.TotalMs = (long)(totalSec * 1000.0);
            if (!state.PendingStart)
                state.Playing = playing;
            if (playing)
                state.StartedAtMs = GetTickMs() - ScaleFromPlaybackMs(state.LastKnownCurrentMs, state.Speed);
        }
    }

    public static void NotifyBgmPlaybackStarted(string path, long totalMs)
    {
        lock (snakeAudioLock)
        {
            if (!string.Equals(snakeBgm.Path, path, StringComparison.OrdinalIgnoreCase))
                return;
            snakeBgm.PendingStart = false;
            snakeBgm.Playing = true;
            snakeBgm.Paused = false;
            snakeBgm.StartedAtMs = GetTickMs();
            snakeBgm.TotalMs = Math.Max(0, totalMs);
            snakeBgm.LastKnownCurrentMs = 0;
        }
    }

    public static void NotifyBgmPlaybackFailed(string path)
    {
        lock (snakeAudioLock)
        {
            if (!string.Equals(snakeBgm.Path, path, StringComparison.OrdinalIgnoreCase))
                return;
            snakeBgm.Playing = false;
            snakeBgm.PendingStart = false;
            snakeBgm.Paused = false;
            snakeBgm.LastKnownCurrentMs = 0;
        }
    }

    public static void NotifyBgmPlaybackPosition(double currentSec, double totalSec, bool playing)
    {
        lock (snakeAudioLock)
        {
            snakeBgm.LastKnownCurrentMs = Math.Max(0, (long)(currentSec * 1000.0));
            if (totalSec > 0)
                snakeBgm.TotalMs = (long)(totalSec * 1000.0);
            if (!snakeBgm.PendingStart)
                snakeBgm.Playing = playing;
            if (playing)
                snakeBgm.StartedAtMs = GetTickMs() - ScaleFromPlaybackMs(snakeBgm.LastKnownCurrentMs, snakeBgm.Speed);
        }
    }

    static long GetCurrentAudioMs(SnakeAudioState state)
    {
        if (state == null || !state.Playing)
            return 0;
        if (state.PendingStart || state.StartedAtMs <= 0 || state.Paused)
            return Math.Max(0, state.LastKnownCurrentMs);
        long elapsedMs = Math.Max(0, GetTickMs() - state.StartedAtMs);
        return elapsedMs * Math.Max(1, state.Speed) / 100;
    }

    static long ScaleFromPlaybackMs(long playbackMs, int speed)
    {
        return playbackMs * 100 / Math.Max(1, speed);
    }

    public static string ResolveSoundPath(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;
        if (System.IO.Path.IsPathRooted(name))
            return name;
        string exeDir = MinorShift.Emuera.Program.ExeDir ?? "";
        string[] candidates =
        {
            System.IO.Path.Combine(exeDir, "sound", name),
            System.IO.Path.Combine(exeDir, "Sound", name),
            System.IO.Path.Combine(exeDir, name),
            System.IO.Path.Combine("sound", name),
            name
        };
        foreach (string candidate in candidates)
        {
            string resolved = uEmuera.Utils.ResolveExistingFilePath(candidate);
            if (!string.IsNullOrEmpty(resolved) && uEmuera.Utils.FileExists(resolved))
                return resolved;
        }
        string soundDir = System.IO.Path.Combine(exeDir, "sound");
        if (uEmuera.Utils.DirectoryExists(soundDir))
        {
            string found = uEmuera.Utils.FindFileRecursive(soundDir, name);
            if (!string.IsNullOrEmpty(found) && uEmuera.Utils.FileExists(found))
                return found;
            found = FindSimilarSoundFile(soundDir, name);
            if (!string.IsNullOrEmpty(found) && uEmuera.Utils.FileExists(found))
            {
                Info(EmueraLogCategory.Audio, () => $"[AUDIO] Resolved similar sound \"{name}\" -> \"{found}\"");
                return found;
            }
        }
        return System.IO.Path.GetFullPath(candidates[0]);
    }

    static string FindSimilarSoundFile(string soundDir, string requestedName)
    {
        if (string.IsNullOrEmpty(soundDir) || string.IsNullOrEmpty(requestedName))
            return null;
        string requestedBase = System.IO.Path.GetFileNameWithoutExtension(requestedName);
        string requestedExt = System.IO.Path.GetExtension(requestedName);
        if (string.IsNullOrEmpty(requestedBase))
            return null;

        string best = null;
        int bestScore = 0;
        try
        {
            foreach (string file in System.IO.Directory.EnumerateFiles(soundDir, "*", System.IO.SearchOption.AllDirectories))
            {
                string ext = System.IO.Path.GetExtension(file);
                if (!IsPlayableAudioExtension(ext))
                    continue;
                if (!string.IsNullOrEmpty(requestedExt) && !string.Equals(requestedExt, ext, StringComparison.OrdinalIgnoreCase))
                    continue;
                string candidateBase = System.IO.Path.GetFileNameWithoutExtension(file);
                int score = SharedCjkCharScore(requestedBase, candidateBase);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = file;
                }
            }
        }
        catch
        {
            return null;
        }
        return bestScore >= 3 ? best : null;
    }

    static bool IsPlayableAudioExtension(string ext)
    {
        return string.Equals(ext, ".wav", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".ogg", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".mp3", StringComparison.OrdinalIgnoreCase);
    }

    static int SharedCjkCharScore(string left, string right)
    {
        if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right))
            return 0;
        var seen = new HashSet<char>();
        int score = 0;
        foreach (char c in left)
        {
            if (!IsCjk(c) || !seen.Add(c))
                continue;
            if (right.IndexOf(c) >= 0)
                score++;
        }
        return score;
    }

    static bool IsCjk(char c)
    {
        return (c >= 0x3400 && c <= 0x9FFF) || (c >= 0xF900 && c <= 0xFAFF);
    }

    static int ClampEraVolume(int volume)
    {
        if (volume < 0)
            return 0;
        if (volume > 100)
            return 100;
        return volume;
    }

    static long GetTickMs()
    {
        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    internal static void SetHtmlIsland(MinorShift.Emuera.GameView.ConsoleDisplayLine[] lines)
    {
        EnqueueUI(() => EmueraContent.instance?.SetHtmlIsland(lines));
    }

    internal static void ClearHtmlIsland()
    {
        EnqueueUI(() => EmueraContent.instance?.ClearHtmlIsland());
    }

    public static void RestartGame()
    {
        EnqueueUI(() => EmueraContent.instance?.RequestRestartFromErb());
    }
}
