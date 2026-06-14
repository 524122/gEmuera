using Godot;
using MinorShift._Library;
using MinorShift.Emuera;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

/// <summary>
/// Godot 宿主启动组件，负责把启动器选择转换成 Emuera 核心可用的运行目录，
/// 并加载跨核心共用的编码配置映射。它只处理启动数据，不创建 UI。
/// </summary>
public sealed partial class EmueraStartupComponent : Node
{
    [Signal]
    public delegate void StatusChangedEventHandler(string status);

    const string DefaultFallbackEraPath = "res://eraAkumaMaid0.305-CH-正式版";

    static readonly object configMapCacheLock = new object();
    static Dictionary<string, string> cachedShiftJisToUtf8Map;
    static Dictionary<string, string> cachedUtf8ZhCnToUtf8Map;

    public string CurrentGamePath { get; private set; }
    public string CurrentCoreProfile { get; private set; }

    public async Task<bool> PrepareAsync()
    {
        await WaitForProcessFrameAsync();
        if (!IsInsideTree())
            return false;

        EmitStatus("正在准备游戏目录...");
        ResolveAndApplyGamePath();

        await WaitForProcessFrameAsync();
        if (!IsInsideTree())
            return false;

        EmitStatus("Loading config...");
        LoadConfigMaps();

        await WaitForProcessFrameAsync();
        if (!IsInsideTree())
            return false;

        // 启动组件是核心全局状态重置的边界：UI 还未创建，后台线程也未启动，
        // 此时重置不会打断正在运行的 Emuera 脚本或 Godot 控件树。
        GlobalStatic.Reset();
        return true;
    }

    void ResolveAndApplyGamePath()
    {
        string eraPath = FirstWindow.ResolveStartupGamePath();
        if (string.IsNullOrEmpty(eraPath) || !uEmuera.Utils.DirectoryExists(eraPath))
            eraPath = ProjectSettings.GlobalizePath(DefaultFallbackEraPath);

        if (!string.IsNullOrEmpty(eraPath) && uEmuera.Utils.DirectoryExists(eraPath))
            Sys.ExeDir = uEmuera.Utils.NormalizePath(eraPath + "/");
        else
            Sys.ExeDir = uEmuera.Utils.NormalizePath(OS.GetExecutablePath().GetBaseDir() + "/");

        CurrentGamePath = Sys.ExeDir;
        CurrentCoreProfile = FirstWindow.SelectedCoreProfileName;
        GenericUtils.NotifyGamePathSelected(CurrentGamePath, CurrentCoreProfile);
    }

    void LoadConfigMaps()
    {
        lock (configMapCacheLock)
        {
            if (cachedShiftJisToUtf8Map != null && cachedUtf8ZhCnToUtf8Map != null)
            {
                uEmuera.Utils.SetSHIFTJIS_to_UTF8Dict(cachedShiftJisToUtf8Map);
                uEmuera.Utils.SetUTF8ZHCN_to_UTF8Dict(cachedUtf8ZhCnToUtf8Map);
                return;
            }
        }

        char[] split = { '\r', '\n' };
        const string shiftjisPath = "res://Text/emuera_config_shiftjis.bytes";
        const string utf8Path = "res://Text/emuera_config_utf8.txt";
        const string utf8CnPath = "res://Text/emuera_config_utf8_zhcn.txt";

        if (!FileAccess.FileExists(shiftjisPath) ||
            !FileAccess.FileExists(utf8Path) ||
            !FileAccess.FileExists(utf8CnPath))
            return;

        byte[] shiftjisBytes = FileAccess.GetFileAsBytes(shiftjisPath);
        string utf8Text = FileAccess.GetFileAsString(utf8Path);
        string utf8CnText = FileAccess.GetFileAsString(utf8CnPath);

        List<string> jisMd5Strings = GenericUtils.CalcMd5List(shiftjisBytes);
        List<string> utf8Strings = BuildNonEmptyLines(utf8Text, split);
        List<string> utf8CnStrings = BuildNonEmptyLines(utf8CnText, split);

        if (jisMd5Strings.Count == 0 || utf8Strings.Count == 0)
            return;

        var jisMap = new Dictionary<string, string>();
        int jisCount = Math.Min(jisMd5Strings.Count, utf8Strings.Count);
        for (int i = 0; i < jisCount; ++i)
            jisMap[jisMd5Strings[i]] = utf8Strings[i];

        var utf8CnMap = new Dictionary<string, string>();
        int utf8CnCount = Math.Min(utf8CnStrings.Count, utf8Strings.Count);
        for (int i = 0; i < utf8CnCount; ++i)
            utf8CnMap[utf8CnStrings[i]] = utf8Strings[i];

        lock (configMapCacheLock)
        {
            // res://Text 配置映射在进程内不变化，缓存后重启游戏不再重复读盘和构建字典。
            cachedShiftJisToUtf8Map ??= jisMap;
            cachedUtf8ZhCnToUtf8Map ??= utf8CnMap;
            uEmuera.Utils.SetSHIFTJIS_to_UTF8Dict(cachedShiftJisToUtf8Map);
            uEmuera.Utils.SetUTF8ZHCN_to_UTF8Dict(cachedUtf8ZhCnToUtf8Map);
        }
    }

    static List<string> BuildNonEmptyLines(string text, char[] split)
    {
        var lines = new List<string>();
        if (string.IsNullOrEmpty(text))
            return lines;

        foreach (string line in text.Split(split, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!string.IsNullOrWhiteSpace(line))
                lines.Add(line);
        }
        return lines;
    }

    void EmitStatus(string status)
    {
        EmitSignal(SignalName.StatusChanged, status);
    }

    async Task WaitForProcessFrameAsync()
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
}
