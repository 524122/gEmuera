using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using GEmuera.Core.Session;

namespace GEmuera.Core.Ports;

public enum InputDeviceKind { Keyboard, Controller, Mouse, Touch, VirtualCursor }
public enum PointerButton { None = 0, Left = 1, Right = 2, Middle = 4 }
public readonly record struct NormalizedAction
{
    public NormalizedAction(string actionId, InputDeviceKind device, float x, float y, PointerButton button, SessionStamp stamp)
    {
        if (string.IsNullOrWhiteSpace(actionId)) throw new ArgumentException("Action id is required.", nameof(actionId));
        if (!float.IsFinite(x) || !float.IsFinite(y)) throw new ArgumentOutOfRangeException(nameof(x));
        if (stamp.OperationId == SessionOperationId.None) throw new ArgumentException("Input action requires an operation id.", nameof(stamp));
        ActionId = actionId.Trim(); Device = device; X = x; Y = y; Button = button; Stamp = stamp;
    }
    public string ActionId { get; }
    public InputDeviceKind Device { get; }
    public float X { get; }
    public float Y { get; }
    public PointerButton Button { get; }
    public SessionStamp Stamp { get; }
}
public enum InputSubmissionStatus { Accepted, Stale, Duplicate, Closed }
public sealed class InputCoordinator : IDisposable
{
    private readonly object _gate = new(); private readonly HashSet<SessionOperationId> _operations = new(); private SessionGeneration _generation; private bool _closed;
    public InputCoordinator(SessionGeneration generation) { _generation = generation; }
    public InputSubmissionStatus Submit(NormalizedAction action)
    {
        lock (_gate) { if (_closed) return InputSubmissionStatus.Closed; if (action.Stamp.Generation != _generation) return InputSubmissionStatus.Stale; return _operations.Add(action.Stamp.OperationId) ? InputSubmissionStatus.Accepted : InputSubmissionStatus.Duplicate; }
    }
    public void AdvanceGeneration(SessionGeneration generation) { lock (_gate) { if (_closed) throw new ObjectDisposedException(nameof(InputCoordinator)); if (generation.Value <= _generation.Value) throw new ArgumentOutOfRangeException(nameof(generation)); _generation = generation; _operations.Clear(); } }
    public void Dispose() { lock (_gate) { _closed = true; _operations.Clear(); } }
}

public readonly record struct StorageContentToken
{
    public StorageContentToken(string providerId, string opaqueId)
    {
        ProviderId = Required(providerId, nameof(providerId)); OpaqueId = Required(opaqueId, nameof(opaqueId));
        if (!Regex.IsMatch(ProviderId, "^[a-z][a-z0-9._-]*$", RegexOptions.CultureInvariant)) throw new ArgumentException("Invalid storage provider id.", nameof(providerId));
    }
    public string ProviderId { get; }
    public string OpaqueId { get; }
    private static string Required(string? value, string name) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value is required.", name) : value.Trim();
}
public enum ImportJournalState { Created, Writing, Verified, Committed, Revoked, RecoveryRequired, Recovered }
public sealed class ImportJournal
{
    private readonly object _gate = new(); private ImportJournalState _state = ImportJournalState.Created; private long _bytes;
    public ImportJournal(StorageContentToken token, SessionGeneration generation) { Token = token; Generation = generation; }
    public StorageContentToken Token { get; }
    public SessionGeneration Generation { get; }
    public ImportJournalState State { get { lock (_gate) return _state; } }
    public long BytesWritten { get { lock (_gate) return _bytes; } }
    public void BeginWrite() { lock (_gate) { Ensure(ImportJournalState.Created); _state = ImportJournalState.Writing; } }
    public void Append(long bytes) { lock (_gate) { Ensure(ImportJournalState.Writing); if (bytes <= 0) throw new ArgumentOutOfRangeException(nameof(bytes)); _bytes = checked(_bytes + bytes); } }
    public void MarkVerified() { lock (_gate) { Ensure(ImportJournalState.Writing); if (_bytes == 0) throw new InvalidOperationException("Cannot verify an empty import."); _state = ImportJournalState.Verified; } }
    public void Commit() { lock (_gate) { Ensure(ImportJournalState.Verified); _state = ImportJournalState.Committed; } }
    public void Revoke() { lock (_gate) { if (_state == ImportJournalState.Created || _state == ImportJournalState.Committed || _state == ImportJournalState.Writing || _state == ImportJournalState.Verified) _state = ImportJournalState.Revoked; } }
    public void MarkRecoveryRequired() { lock (_gate) { if (_state != ImportJournalState.Committed) _state = ImportJournalState.RecoveryRequired; } }
    public void Recover() { lock (_gate) { Ensure(ImportJournalState.RecoveryRequired); _state = ImportJournalState.Recovered; } }
    private void Ensure(ImportJournalState expected) { if (_state != expected) throw new InvalidOperationException($"Import journal expected {expected}, actual {_state}."); }
}

public enum PortLeaseState { Open, Completed, Cancelled, Disposed }
public sealed class DatabaseOperationLease : IDisposable
{
    private int _state;
    public PortOperationKey Key { get; }
    public DatabaseOperationLease(PortOperationKey key) { if (key.OperationId == SessionOperationId.None) throw new ArgumentException("Operation id is required.", nameof(key)); Key = key; }
    public PortLeaseState State => (PortLeaseState)Volatile.Read(ref _state);
    public void Complete() { Transition(PortLeaseState.Completed); }
    public void Cancel() { Transition(PortLeaseState.Cancelled); }
    public void Dispose() { Interlocked.Exchange(ref _state, (int)PortLeaseState.Disposed); }
    private void Transition(PortLeaseState state) { if (Interlocked.CompareExchange(ref _state, (int)state, 0) != 0) throw new InvalidOperationException("Database operation lease is no longer open."); }
}

public readonly record struct LifecycleSignal(SessionGeneration Generation, bool IsPaused, bool IsExiting);
public sealed class AudioGenerationGate
{
    private SessionGeneration _generation; public AudioGenerationGate(SessionGeneration generation) { _generation = generation; }
    public SessionGeneration Generation => _generation;
    public bool Accept(SessionGeneration generation) => generation == _generation;
    public void Reset(SessionGeneration generation) { if (generation.Value <= _generation.Value) throw new ArgumentOutOfRangeException(nameof(generation)); _generation = generation; }
}

public sealed class EraFlCapabilityPack
{
    private readonly IReadOnlyList<CapabilityId> _capabilities;
    public EraFlCapabilityPack(IEnumerable<CapabilityId> capabilities) { ArgumentNullException.ThrowIfNull(capabilities); var list = capabilities.Distinct().OrderBy(value => value.Value, StringComparer.Ordinal).ToArray(); if (list.Length == 0) throw new ArgumentException("At least one capability is required.", nameof(capabilities)); _capabilities = new ReadOnlyCollection<CapabilityId>(list); }
    public IReadOnlyList<CapabilityId> Capabilities => _capabilities;
    public bool Contains(CapabilityId capability) => _capabilities.Contains(capability);
    public static EraFlCapabilityPack Compose(IEnumerable<CapabilityId> capabilities) => new(capabilities);
}
