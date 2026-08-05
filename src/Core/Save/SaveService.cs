using System.Text.RegularExpressions;
using GEmuera.Core.State;

namespace GEmuera.Core.Save;

public enum SaveTransactionState
{
    Prepared,
    Committed,
    RolledBack,
    Failed,
}

public sealed class SaveCandidate : IDisposable
{
    private int _state;

    internal SaveCandidate(SaveSnapshot snapshot, byte[] payload)
    {
        Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        Payload = payload is null ? throw new ArgumentNullException(nameof(payload)) : (byte[])payload.Clone();
    }

    public SaveSnapshot Snapshot { get; }
    public byte[] Payload { get; }
    public SaveTransactionState State => (SaveTransactionState)Volatile.Read(ref _state);
    public bool IsOpen => State == SaveTransactionState.Prepared;

    internal void Mark(SaveTransactionState state)
    {
        if (Interlocked.CompareExchange(ref _state, (int)state, (int)SaveTransactionState.Prepared) != (int)SaveTransactionState.Prepared)
            throw new InvalidOperationException("Save candidate is no longer open.");
    }

    public void Dispose()
    {
        if (IsOpen)
            Interlocked.CompareExchange(ref _state, (int)SaveTransactionState.RolledBack, (int)SaveTransactionState.Prepared);
    }
}

public interface ISaveBlobStore
{
    ValueTask WriteAtomicAsync(string slot, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default);
    ValueTask<byte[]?> ReadAsync(string slot, CancellationToken cancellationToken = default);
    ValueTask DeleteAsync(string slot, CancellationToken cancellationToken = default);
}

/// <summary>
/// Desktop/local adapter for the pure save service. The caller supplies the
/// already-authorized root; slot names never become arbitrary paths.
/// </summary>
public sealed class FileSaveBlobStore : ISaveBlobStore
{
    private readonly string _root;

    public FileSaveBlobStore(string authorizedRoot)
    {
        if (string.IsNullOrWhiteSpace(authorizedRoot))
            throw new ArgumentException("Authorized save root is required.", nameof(authorizedRoot));
        _root = Path.GetFullPath(authorizedRoot);
        Directory.CreateDirectory(_root);
    }

    public async ValueTask WriteAtomicAsync(string slot, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default)
    {
        var target = Resolve(slot);
        var temporary = target + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await File.WriteAllBytesAsync(temporary, payload.ToArray(), cancellationToken).ConfigureAwait(false);
            File.Move(temporary, target, true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    public ValueTask<byte[]?> ReadAsync(string slot, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = Resolve(slot);
        return ValueTask.FromResult<byte[]?>(File.Exists(path) ? File.ReadAllBytes(path) : null);
    }

    public ValueTask DeleteAsync(string slot, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = Resolve(slot);
        if (File.Exists(path))
            File.Delete(path);
        return ValueTask.CompletedTask;
    }

    private string Resolve(string slot)
    {
        if (string.IsNullOrWhiteSpace(slot) || !Regex.IsMatch(slot.Trim(), "^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$", RegexOptions.CultureInvariant))
            throw new ArgumentException("Save slot contains unsupported characters.", nameof(slot));
        return Path.Combine(_root, slot.Trim() + ".sav");
    }
}

/// <summary>
/// Candidate-based save pipeline. Encoding and variable commit happen before
/// any target replacement, so malformed loads cannot mutate the live store.
/// </summary>
public sealed class SaveService : IDisposable
{
    private readonly VariableStore _variables;
    private readonly ISaveCodec _codec;
    private readonly Session.SessionGeneration _generation;
    private int _disposed;

    public SaveService(VariableStore variables, ISaveCodec codec, Session.SessionGeneration generation)
    {
        _variables = variables ?? throw new ArgumentNullException(nameof(variables));
        _codec = codec ?? throw new ArgumentNullException(nameof(codec));
        if (generation.Value <= 0)
            throw new ArgumentOutOfRangeException(nameof(generation));
        _generation = generation;
        Profile = codec.Profile;
    }

    public SaveProfileId Profile { get; }
    public Session.SessionGeneration Generation => _generation;
    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    public SaveCandidate CaptureCandidate()
    {
        EnsureOpen();
        var snapshot = SaveSnapshot.From(_variables, Profile);
        if (snapshot.Generation != _generation)
            throw new InvalidOperationException("Save snapshot belongs to a different session generation.");
        return new SaveCandidate(snapshot, _codec.Encode(snapshot));
    }

    public SaveSnapshot DecodeCandidate(ReadOnlySpan<byte> payload)
    {
        EnsureOpen();
        var snapshot = _codec.Decode(payload, Profile);
        if (snapshot.Generation != _generation)
            throw new InvalidDataException("Save snapshot belongs to a different session generation.");
        return snapshot;
    }

    public async ValueTask CommitAsync(SaveCandidate candidate, ISaveBlobStore store, string slot, CancellationToken cancellationToken = default)
    {
        EnsureOpen();
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(store);
        if (!candidate.IsOpen || candidate.Snapshot.Generation != _generation)
            throw new InvalidOperationException("Save candidate is stale or already closed.");

        try
        {
            await store.WriteAtomicAsync(slot, candidate.Payload, cancellationToken).ConfigureAwait(false);
            candidate.Mark(SaveTransactionState.Committed);
        }
        catch
        {
            if (candidate.IsOpen)
                candidate.Mark(SaveTransactionState.Failed);
            throw;
        }
    }

    public void CommitLoad(SaveSnapshot snapshot)
    {
        EnsureOpen();
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Generation != _generation || snapshot.Profile != Profile)
            throw new InvalidOperationException("Save snapshot does not belong to this session/profile.");
        var variableSnapshot = new VariableSnapshot(snapshot.Generation, snapshot.Sequence, snapshot.Values);
        using var candidate = _variables.PrepareLoad(variableSnapshot);
        _variables.CommitLoad(candidate, _generation);
    }

    public void Dispose() => Interlocked.Exchange(ref _disposed, 1);

    private void EnsureOpen()
    {
        if (IsDisposed)
            throw new ObjectDisposedException(nameof(SaveService));
    }
}
