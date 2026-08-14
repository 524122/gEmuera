namespace GEmuera.Core.Agent.Cache;

/// <summary>
/// 口上预取缓存：情境 key → 预生成文本。
/// 写入方：主检查点预取批处理（后台线程）；读取方：解释器线程（CALLSHARP AI_PREFETCH_GET）。
/// 跨线程访问，全部操作加锁。
/// </summary>
public sealed class KoujouPrefetchCache
{
    readonly object gate = new();
    readonly Dictionary<string, string> entries = new(StringComparer.Ordinal);

    public int Count
    {
        get { lock (gate) return entries.Count; }
    }

    public void Put(string contextKey, string text)
    {
        if (string.IsNullOrEmpty(contextKey))
            return;
        lock (gate) entries[contextKey] = text ?? "";
    }

    public bool TryGet(string contextKey, out string text)
    {
        lock (gate)
        {
            if (entries.TryGetValue(contextKey ?? "", out string value))
            {
                text = value;
                return true;
            }
        }
        text = "";
        return false;
    }

    public void Clear()
    {
        lock (gate) entries.Clear();
    }
}
