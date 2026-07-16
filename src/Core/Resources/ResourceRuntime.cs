using GEmuera.Core.Session;

namespace GEmuera.Core.Resources;

public enum ResourceAdmissionStatus
{
    Accepted,
    Duplicate,
    BudgetExceeded,
    StaleGeneration,
    Closed,
}

public sealed class ResourceLease : IDisposable
{
    private readonly ResourceRuntime _owner;
    private readonly MemoryReservation _reservation;
    private int _released;

    internal ResourceLease(ResourceRuntime owner, ResourceKey key, long bytes, MemoryReservation reservation)
    {
        _owner = owner;
        Key = key;
        ReservedBytes = bytes;
        _reservation = reservation;
    }

    public ResourceKey Key { get; }
    public long ReservedBytes { get; }
    public bool IsReleased => Volatile.Read(ref _released) != 0;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _released, 1) == 0)
        {
            _reservation.Dispose();
            _owner.Release(Key, this);
        }
    }
}

public sealed record ResourceRuntimeEntry(
    ResourceDescriptor Descriptor,
    ResourceLease Lease,
    SessionGeneration Generation,
    ResourceProjectionState State);

/// <summary>
/// Session-scoped resource admission and upload ledger. Godot resources are
/// deliberately absent; a bridge consumes accepted upload descriptors later.
/// </summary>
public sealed class ResourceRuntime : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<ResourceKey, ResourceRuntimeEntry> _entries = new();
    private readonly MemoryBudget _budget;
    private readonly ResourceBridgeLedger _bridge;
    private SessionGeneration _generation;
    private bool _closed;

    public ResourceRuntime(SessionGeneration generation, long memoryCapacityBytes)
    {
        if (generation.Value <= 0)
            throw new ArgumentOutOfRangeException(nameof(generation));
        _generation = generation;
        _budget = new MemoryBudget(memoryCapacityBytes);
        _bridge = new ResourceBridgeLedger(generation);
    }

    public SessionGeneration Generation
    {
        get { lock (_gate) return _generation; }
    }

    public int Count
    {
        get { lock (_gate) return _entries.Count; }
    }

    public MemoryBudgetSnapshot Memory => _budget.Snapshot;

    public ResourceAdmissionStatus TryRegister(
        ResourceDescriptor descriptor,
        long decodedBytes,
        out ResourceLease? lease)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (decodedBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(decodedBytes));

        lock (_gate)
        {
            if (_closed)
            {
                lease = null;
                return ResourceAdmissionStatus.Closed;
            }
            if (_entries.ContainsKey(descriptor.Key))
            {
                lease = null;
                return ResourceAdmissionStatus.Duplicate;
            }
            if (decodedBytes > descriptor.MaxDecodedBytes || !_budget.TryReserve(decodedBytes, out var reservation) || reservation is null)
            {
                lease = null;
                return ResourceAdmissionStatus.BudgetExceeded;
            }

            lease = new ResourceLease(this, descriptor.Key, decodedBytes, reservation);
            _entries.Add(descriptor.Key, new ResourceRuntimeEntry(
                descriptor,
                lease,
                _generation,
                ResourceProjectionState.Decoding));
            return ResourceAdmissionStatus.Accepted;
        }
    }

    public ResourceUploadDecision AcceptUpload(ResourceUploadDescriptor descriptor)
    {
        lock (_gate)
        {
            if (_closed)
                return ResourceUploadDecision.Disposed;
            if (!_entries.Values.Any(entry => entry.Descriptor.Source == descriptor.Source))
                return ResourceUploadDecision.Disposed;
        }
        return _bridge.Accept(descriptor);
    }

    public bool TryGet(ResourceKey key, out ResourceRuntimeEntry? entry)
    {
        lock (_gate)
            return _entries.TryGetValue(key, out entry);
    }

    public void MarkProjected(ResourceKey key, PixelRevision revision)
    {
        if (!revision.IsValid)
            throw new ArgumentOutOfRangeException(nameof(revision));
        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out var entry))
                throw new KeyNotFoundException($"Resource is not registered: {key}");
            _entries[key] = entry with { State = ResourceProjectionState.Projected };
        }
    }

    public void AdvanceGeneration(SessionGeneration generation)
    {
        List<ResourceLease> leases;
        lock (_gate)
        {
            EnsureOpen();
            if (generation.Value <= _generation.Value)
                throw new ArgumentOutOfRangeException(nameof(generation));
            leases = _entries.Values.Select(entry => entry.Lease).ToList();
            _entries.Clear();
            _generation = generation;
        }

        foreach (var lease in leases)
            lease.Dispose();
        _bridge.AdvanceGeneration(generation);
    }

    public void Dispose()
    {
        List<ResourceLease> leases;
        lock (_gate)
        {
            if (_closed)
                return;
            _closed = true;
            leases = _entries.Values.Select(entry => entry.Lease).ToList();
            _entries.Clear();
        }

        foreach (var lease in leases)
            lease.Dispose();
        _bridge.Dispose();
        _budget.Dispose();
    }

    internal void Release(ResourceKey key, ResourceLease lease)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var entry) && ReferenceEquals(entry.Lease, lease))
                _entries.Remove(key);
        }
    }

    private void EnsureOpen()
    {
        if (_closed)
            throw new ObjectDisposedException(nameof(ResourceRuntime));
    }
}
