using System.Collections.ObjectModel;
using GEmuera.Core.Session;

namespace GEmuera.Core.State;

public enum VariableScope { Global, Character, Local, Argument, Result, VarExt }
public enum CoreValueKind { Integer, Float, String, Reference }

public readonly record struct VariableKey
{
    public VariableKey(VariableScope scope, string name, int index = 0)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Variable name is required.", nameof(name));
        if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
        Scope = scope; Name = name.Trim().ToUpperInvariant(); Index = index;
    }
    public VariableScope Scope { get; }
    public string Name { get; }
    public int Index { get; }
}

public readonly record struct CoreValue
{
    private CoreValue(CoreValueKind kind, long integer, double floating, string? text, long reference)
    { Kind = kind; Integer = integer; Float = floating; Text = text; Reference = reference; }
    public CoreValueKind Kind { get; }
    public long Integer { get; }
    public double Float { get; }
    public string? Text { get; }
    public long Reference { get; }
    public static CoreValue FromInteger(long value) => new(CoreValueKind.Integer, value, 0, null, 0);
    public static CoreValue FromFloat(double value) => double.IsFinite(value) ? new(CoreValueKind.Float, 0, value, null, 0) : throw new ArgumentOutOfRangeException(nameof(value));
    public static CoreValue FromString(string value) => new(CoreValueKind.String, 0, 0, value ?? throw new ArgumentNullException(nameof(value)), 0);
    public static CoreValue FromReference(long value) => new(CoreValueKind.Reference, 0, 0, null, value);
}

public sealed class VariableSnapshot
{
    private readonly IReadOnlyDictionary<VariableKey, CoreValue> _values;
    internal VariableSnapshot(SessionGeneration generation, long sequence, IEnumerable<KeyValuePair<VariableKey, CoreValue>> values)
    {
        Generation = generation; Sequence = sequence;
        _values = new ReadOnlyDictionary<VariableKey, CoreValue>(values.ToDictionary(pair => pair.Key, pair => pair.Value));
    }
    public SessionGeneration Generation { get; }
    public long Sequence { get; }
    public IReadOnlyDictionary<VariableKey, CoreValue> Values => _values;
    public bool TryGet(VariableKey key, out CoreValue value) => _values.TryGetValue(key, out value);
}

public sealed class VariableCandidate : IDisposable
{
    private readonly Dictionary<VariableKey, CoreValue> _values;
    private int _committed;
    internal VariableCandidate(VariableSnapshot snapshot)
    { Source = snapshot; _values = snapshot.Values.ToDictionary(pair => pair.Key, pair => pair.Value); }
    public VariableSnapshot Source { get; }
    public bool IsCommitted => Volatile.Read(ref _committed) != 0;
    public IReadOnlyDictionary<VariableKey, CoreValue> Values => new ReadOnlyDictionary<VariableKey, CoreValue>(_values);
    public void Set(VariableKey key, CoreValue value) { EnsureOpen(); _values[key] = value; }
    public void Remove(VariableKey key) { EnsureOpen(); _values.Remove(key); }
    internal IReadOnlyDictionary<VariableKey, CoreValue> Freeze() { EnsureOpen(); return new ReadOnlyDictionary<VariableKey, CoreValue>(_values.ToDictionary()); }
    internal void MarkCommitted() { if (Interlocked.Exchange(ref _committed, 1) != 0) throw new InvalidOperationException("Variable candidate was already committed."); }
    public void Dispose() => Interlocked.Exchange(ref _committed, 1);
    private void EnsureOpen() { if (IsCommitted) throw new InvalidOperationException("Variable candidate is closed."); }
    internal void EnsureOpenForCommit() => EnsureOpen();
}

/// <summary>Session-scoped, owner-thread mutable state with immutable load candidates.</summary>
public sealed class VariableStore
{
    private readonly object _gate = new();
    private readonly Dictionary<VariableKey, CoreValue> _values = new();
    private readonly int _ownerThreadId;
    private long _sequence;
    private SessionGeneration _generation;
    public VariableStore(SessionGeneration generation, int? ownerThreadId = null) { _generation = generation; _ownerThreadId = ownerThreadId ?? Environment.CurrentManagedThreadId; }
    public SessionGeneration Generation { get { lock (_gate) return _generation; } }
    public long Sequence => Interlocked.Read(ref _sequence);
    public void Set(VariableKey key, CoreValue value) { EnsureOwner(); lock (_gate) { _values[key] = value; Interlocked.Increment(ref _sequence); } }
    public bool Remove(VariableKey key) { EnsureOwner(); lock (_gate) { var removed = _values.Remove(key); if (removed) Interlocked.Increment(ref _sequence); return removed; } }
    public bool TryGet(VariableKey key, out CoreValue value) { lock (_gate) return _values.TryGetValue(key, out value); }
    public VariableSnapshot Capture() { lock (_gate) return new VariableSnapshot(_generation, _sequence, _values); }
    public VariableCandidate PrepareLoad(VariableSnapshot snapshot) { ArgumentNullException.ThrowIfNull(snapshot); return new VariableCandidate(snapshot); }
    public void CommitLoad(VariableCandidate candidate, SessionGeneration expectedGeneration)
    {
        EnsureOwner(); ArgumentNullException.ThrowIfNull(candidate);
        lock (_gate)
        {
            if (_generation != expectedGeneration || candidate.Source.Generation != expectedGeneration)
                throw new InvalidOperationException("Variable candidate belongs to a different session generation.");
            candidate.EnsureOpenForCommit();
            var values = candidate.Freeze(); _values.Clear(); foreach (var pair in values) _values.Add(pair.Key, pair.Value);
            Interlocked.Exchange(ref _sequence, Math.Max(_sequence + 1, candidate.Source.Sequence)); candidate.MarkCommitted();
        }
    }
    public void AdvanceGeneration(SessionGeneration generation) { EnsureOwner(); lock (_gate) { if (generation.Value <= _generation.Value) throw new ArgumentOutOfRangeException(nameof(generation)); _generation = generation; _values.Clear(); Interlocked.Exchange(ref _sequence, 0); } }
    private void EnsureOwner() { if (Environment.CurrentManagedThreadId != _ownerThreadId) throw new InvalidOperationException("VariableStore mutation must run on its owner thread."); }
}
