using GEmuera.Core.Session;

namespace GEmuera.Core.Governance;

public enum RuntimeLeaseKind
{
    Resource,
    Node,
    Rid,
    Task,
    Reservation,
    NativeHandle,
}

public readonly record struct RuntimeLeaseKey(
    RuntimeLeaseKind Kind,
    string Id,
    SessionGeneration Generation);

public sealed record RuntimeLedgerSnapshot(
    int ActiveLeases,
    int ActiveResources,
    int ActiveNodes,
    int ActiveRids,
    int ActiveTasks,
    int ActiveReservations,
    int ActiveNativeHandles,
    long FallbackCalls,
    long FaultCount);

public sealed class RuntimeLease : IDisposable
{
    private readonly RuntimeReleaseLedger _owner;
    private int _released;

    internal RuntimeLease(RuntimeReleaseLedger owner, RuntimeLeaseKey key)
    {
        _owner = owner;
        Key = key;
    }

    public RuntimeLeaseKey Key { get; }
    public bool IsReleased => Volatile.Read(ref _released) != 0;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _released, 1) == 0)
            _owner.Release(Key, this);
    }
}

/// <summary>
/// Runtime counterpart of the M7 cleanup ledger. It records ownership and
/// fallback/fault counters without deleting or force-collecting live objects.
/// </summary>
public sealed class RuntimeReleaseLedger : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<RuntimeLeaseKey, RuntimeLease> _leases = new();
    private long _fallbackCalls;
    private long _faultCount;
    private bool _disposed;

    public RuntimeLease Acquire(RuntimeLeaseKind kind, string id, SessionGeneration generation)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Runtime lease id is required.", nameof(id));
        if (generation.Value <= 0)
            throw new ArgumentOutOfRangeException(nameof(generation));
        lock (_gate)
        {
            EnsureOpen();
            var key = new RuntimeLeaseKey(kind, id.Trim(), generation);
            if (_leases.ContainsKey(key))
                throw new InvalidOperationException($"Runtime lease already exists: {kind}/{id}/{generation.Value}");
            var lease = new RuntimeLease(this, key);
            _leases.Add(key, lease);
            return lease;
        }
    }

    public void RecordFallback() => Interlocked.Increment(ref _fallbackCalls);
    public void RecordFault() => Interlocked.Increment(ref _faultCount);

    public RuntimeLedgerSnapshot Capture()
    {
        lock (_gate)
        {
            return new RuntimeLedgerSnapshot(
                _leases.Count,
                Count(RuntimeLeaseKind.Resource),
                Count(RuntimeLeaseKind.Node),
                Count(RuntimeLeaseKind.Rid),
                Count(RuntimeLeaseKind.Task),
                Count(RuntimeLeaseKind.Reservation),
                Count(RuntimeLeaseKind.NativeHandle),
                Interlocked.Read(ref _fallbackCalls),
                Interlocked.Read(ref _faultCount));
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _leases.Clear();
        }
    }

    internal void Release(RuntimeLeaseKey key, RuntimeLease lease)
    {
        lock (_gate)
        {
            if (_leases.TryGetValue(key, out var current) && ReferenceEquals(current, lease))
                _leases.Remove(key);
        }
    }

    private int Count(RuntimeLeaseKind kind) => _leases.Keys.Count(key => key.Kind == kind);

    private void EnsureOpen()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(RuntimeReleaseLedger));
    }
}
