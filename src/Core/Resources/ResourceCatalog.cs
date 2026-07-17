using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using GEmuera.Core.Session;

namespace GEmuera.Core.Resources;

public readonly record struct ResourceKey
{
    public ResourceKey(string value)
    {
        var normalized = value?.Trim().ToLowerInvariant() ?? throw new ArgumentNullException(nameof(value));
        if (!Regex.IsMatch(normalized, "^[a-z][a-z0-9._-]*$", RegexOptions.CultureInvariant))
            throw new ArgumentException("Invalid logical resource key.", nameof(value));
        Value = normalized;
    }
    public string Value { get; }
    public override string ToString() => Value;
}

public sealed record ResourceDescriptor
{
    public ResourceDescriptor(ResourceKey key, SourceToken source, string relativeSource, PixelRect? crop = null, long maxDecodedBytes = 64 * 1024 * 1024, IEnumerable<ResourceKey>? animationFrames = null)
    {
        if (!source.IsValid) throw new ArgumentException("A resource descriptor requires a validated source token.", nameof(source));
        var normalized = relativeSource?.Trim().Replace('\\', '/') ?? throw new ArgumentNullException(nameof(relativeSource));
        if (string.IsNullOrWhiteSpace(normalized) || normalized.StartsWith("/", StringComparison.Ordinal) || normalized.Contains("..", StringComparison.Ordinal) || normalized.Contains(':'))
            throw new ArgumentException("Resource source must be a relative, containment-safe token.", nameof(relativeSource));
        if (maxDecodedBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxDecodedBytes));
        Key = key; Source = source; RelativeSource = normalized; Crop = crop; MaxDecodedBytes = maxDecodedBytes;
        var frames = (animationFrames ?? Array.Empty<ResourceKey>()).Distinct().OrderBy(value => value.Value, StringComparer.Ordinal).ToArray();
        AnimationFrames = new ReadOnlyCollection<ResourceKey>(frames);
    }
    public ResourceKey Key { get; }
    public SourceToken Source { get; }
    public string RelativeSource { get; }
    public PixelRect? Crop { get; }
    public long MaxDecodedBytes { get; }
    public IReadOnlyList<ResourceKey> AnimationFrames { get; }
}

public sealed class ResourceCatalog
{
    private readonly IReadOnlyDictionary<ResourceKey, ResourceDescriptor> _entries;
    public ResourceCatalog(IEnumerable<ResourceDescriptor> entries)
    {
        ArgumentNullException.ThrowIfNull(entries); var list = entries.ToArray();
        if (list.Any(entry => entry is null)) throw new ArgumentException("Resource entries cannot contain null.", nameof(entries));
        if (list.GroupBy(entry => entry.Key).Any(group => group.Count() > 1)) throw new ArgumentException("Logical resource keys must be unique.", nameof(entries));
        list = list.OrderBy(entry => entry.Key.Value, StringComparer.Ordinal).ToArray();
        _entries = new ReadOnlyDictionary<ResourceKey, ResourceDescriptor>(list.ToDictionary(entry => entry.Key));
        foreach (var entry in list)
            foreach (var frame in entry.AnimationFrames)
                if (!_entries.ContainsKey(frame))
                    throw new ArgumentException($"Animation frame '{frame}' is not registered.", nameof(entries));
    }
    public IReadOnlyDictionary<ResourceKey, ResourceDescriptor> Entries => _entries;
    public bool TryGet(ResourceKey key, out ResourceDescriptor? descriptor) => _entries.TryGetValue(key, out descriptor);
    public ResourceDescriptor GetRequired(ResourceKey key) => TryGet(key, out var value) ? value! : throw new KeyNotFoundException($"Resource is not registered: {key}");
}

public readonly record struct MemoryBudgetSnapshot(long CapacityBytes, long ReservedBytes, int ActiveReservations)
{
    public long AvailableBytes => CapacityBytes - ReservedBytes;
}

public sealed class MemoryBudget : IDisposable
{
    private readonly object _gate = new();
    private readonly long _capacity;
    private long _reserved;
    private int _active;
    private int _disposed;
    public MemoryBudget(long capacityBytes) { if (capacityBytes <= 0) throw new ArgumentOutOfRangeException(nameof(capacityBytes)); _capacity = capacityBytes; }
    public MemoryBudgetSnapshot Snapshot { get { lock (_gate) return new(_capacity, _reserved, _active); } }
    public bool TryReserve(long bytes, out MemoryReservation? reservation)
    {
        if (bytes <= 0) throw new ArgumentOutOfRangeException(nameof(bytes));
        lock (_gate)
        {
            if (_disposed != 0 || bytes > _capacity - _reserved) { reservation = null; return false; }
            _reserved = checked(_reserved + bytes); _active++; reservation = new MemoryReservation(this, bytes); return true;
        }
    }
    internal void Release(long bytes)
    {
        lock (_gate) { if (bytes <= 0 || bytes > _reserved || _active <= 0) throw new InvalidOperationException("Memory reservation ledger is inconsistent."); _reserved -= bytes; _active--; }
    }
    public void Dispose() { lock (_gate) { if (_reserved != 0 || _active != 0) throw new InvalidOperationException("Memory budget disposed with active reservations."); _disposed = 1; } }
}

public sealed class MemoryReservation : IDisposable
{
    private readonly MemoryBudget _owner; private readonly long _bytes; private int _released;
    internal MemoryReservation(MemoryBudget owner, long bytes) { _owner = owner; _bytes = bytes; }
    public long Bytes => _bytes;
    public bool IsReleased => Volatile.Read(ref _released) != 0;
    public void Dispose() { if (Interlocked.Exchange(ref _released, 1) == 0) _owner.Release(_bytes); }
}

public enum ResourceProjectionState { Unrequested, Decoding, CpuReady, UploadPending, Projected, Evictable, Disposed }
public readonly record struct ResourceUploadDescriptor
{
    public ResourceUploadDescriptor(PixelHandle handle, PixelRevision revision, SessionGeneration generation, SourceToken source, long cpuBytes)
    {
        if (!handle.IsValid) throw new ArgumentOutOfRangeException(nameof(handle));
        if (!revision.IsValid) throw new ArgumentOutOfRangeException(nameof(revision));
        if (!source.IsValid) throw new ArgumentException("Source token is required.", nameof(source));
        if (cpuBytes <= 0) throw new ArgumentOutOfRangeException(nameof(cpuBytes));
        Handle = handle; Revision = revision; Generation = generation; Source = source; CpuBytes = cpuBytes;
    }
    public PixelHandle Handle { get; }
    public PixelRevision Revision { get; }
    public SessionGeneration Generation { get; }
    public SourceToken Source { get; }
    public long CpuBytes { get; }
}

public enum ResourceUploadDecision { Accepted, StaleGeneration, OlderRevision, Disposed }

/// <summary>Core-side ledger for main-thread upload requests; it never creates platform resources.</summary>
public sealed class ResourceBridgeLedger : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<PixelHandle, ResourceUploadDescriptor> _latest = new();
    private SessionGeneration _generation;
    private bool _disposed;
    public ResourceBridgeLedger(SessionGeneration generation) { _generation = generation; }
    public SessionGeneration Generation { get { lock (_gate) return _generation; } }
    public ResourceUploadDecision Accept(ResourceUploadDescriptor descriptor)
    {
        lock (_gate)
        {
            if (_disposed) return ResourceUploadDecision.Disposed;
            if (descriptor.Generation != _generation) return ResourceUploadDecision.StaleGeneration;
            if (_latest.TryGetValue(descriptor.Handle, out var current) && descriptor.Revision.Value <= current.Revision.Value) return ResourceUploadDecision.OlderRevision;
            _latest[descriptor.Handle] = descriptor; return ResourceUploadDecision.Accepted;
        }
    }
    public void AdvanceGeneration(SessionGeneration generation) { lock (_gate) { if (_disposed) throw new ObjectDisposedException(nameof(ResourceBridgeLedger)); if (generation.Value <= _generation.Value) throw new ArgumentOutOfRangeException(nameof(generation)); _generation = generation; _latest.Clear(); } }
    public bool TryGet(PixelHandle handle, out ResourceUploadDescriptor descriptor) { lock (_gate) return _latest.TryGetValue(handle, out descriptor); }
    public void Dispose() { lock (_gate) { _disposed = true; _latest.Clear(); } }
}
