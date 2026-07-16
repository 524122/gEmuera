using GEmuera.Core.Session;

namespace GEmuera.Core.Ports;

public sealed class RuntimeStorageAdapter : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, byte[]> _contents = new(StringComparer.Ordinal);
    private readonly SessionGeneration _generation;
    private bool _disposed;

    public RuntimeStorageAdapter(SessionGeneration generation)
    {
        if (generation.Value <= 0)
            throw new ArgumentOutOfRangeException(nameof(generation));
        _generation = generation;
    }

    public SessionGeneration Generation => _generation;

    public void Write(StorageContentToken token, ReadOnlySpan<byte> content)
    {
        lock (_gate)
        {
            EnsureOpen();
            if (content.Length > 16 * 1024 * 1024)
                throw new InvalidOperationException("Storage payload exceeds the prototype limit.");
            _contents[token.ProviderId + ":" + token.OpaqueId] = content.ToArray();
        }
    }

    public bool TryRead(StorageContentToken token, out byte[] content)
    {
        lock (_gate)
        {
            if (_disposed || !_contents.TryGetValue(token.ProviderId + ":" + token.OpaqueId, out var value))
            {
                content = Array.Empty<byte>();
                return false;
            }
            content = (byte[])value.Clone();
            return true;
        }
    }

    public void Clear()
    {
        lock (_gate)
            _contents.Clear();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _contents.Clear();
        }
    }

    private void EnsureOpen()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(RuntimeStorageAdapter));
    }
}

public sealed class RuntimeLifecycleAdapter
{
    private readonly object _gate = new();
    private SessionGeneration _generation;
    private bool _paused;
    private bool _exiting;

    public RuntimeLifecycleAdapter(SessionGeneration generation)
    {
        if (generation.Value <= 0)
            throw new ArgumentOutOfRangeException(nameof(generation));
        _generation = generation;
    }

    public LifecycleSignal Snapshot
    {
        get
        {
            lock (_gate)
                return new LifecycleSignal(_generation, _paused, _exiting);
        }
    }

    public void SetPaused(bool paused)
    {
        lock (_gate)
            _paused = paused;
    }

    public void SetExiting()
    {
        lock (_gate)
            _exiting = true;
    }

    public void AdvanceGeneration(SessionGeneration generation)
    {
        lock (_gate)
        {
            if (generation.Value <= _generation.Value)
                throw new ArgumentOutOfRangeException(nameof(generation));
            _generation = generation;
            _paused = false;
            _exiting = false;
        }
    }
}
