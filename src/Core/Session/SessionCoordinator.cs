using GEmuera.Core.Compatibility;

namespace GEmuera.Core.Session;

public enum SessionSwitchStatus
{
    Committed,
    Cancelled,
    Stale,
    Faulted,
}

/// <summary>
/// Result of candidate construction before activation. A prepared lease owns a
/// candidate until it is explicitly committed or disposed.
/// </summary>
public enum SessionPreparationStatus
{
    Prepared,
    Cancelled,
    Stale,
    Faulted,
}

/// <summary>
/// Immutable selection inputs for one session. Compatibility inputs live with
/// the selection so a bridge cannot accidentally construct a different plan
/// than the session it activates.
/// </summary>
public sealed record SessionSelection
{
    public SessionSelection(
        string gameId,
        string profileId,
        IEnumerable<string>? requestedModuleIds = null,
        IEnumerable<BehaviorPortSnapshot>? ports = null,
        IEnumerable<string>? capabilityIds = null,
        string? saveProfileId = null)
    {
        GameId = ContractTextForSession.RequiredIdentifier(gameId, nameof(gameId));
        ProfileId = ContractTextForSession.RequiredIdentifier(profileId, nameof(profileId));
        RequestedModuleIds = Array.AsReadOnly((requestedModuleIds ?? Array.Empty<string>())
            .Select(value => ContractTextForSession.RequiredIdentifier(value, nameof(requestedModuleIds)))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray());

        var copiedPorts = (ports ?? Array.Empty<BehaviorPortSnapshot>()).ToArray();
        if (copiedPorts.Any(port => port is null))
            throw new ArgumentException("Collection contains null.", nameof(ports));
        Ports = Array.AsReadOnly(copiedPorts);

        CapabilityIds = Array.AsReadOnly((capabilityIds ?? Array.Empty<string>())
            .Select(value => ContractText.RequiredVersionedIdentifier(value, nameof(capabilityIds)))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray());
        SaveProfileId = string.IsNullOrWhiteSpace(saveProfileId)
            ? null
            : ContractTextForSession.RequiredIdentifier(saveProfileId, nameof(saveProfileId));
    }

    public string GameId { get; }
    public string ProfileId { get; }
    public IReadOnlyList<string> RequestedModuleIds { get; }
    public IReadOnlyList<BehaviorPortSnapshot> Ports { get; }
    public IReadOnlyList<string> CapabilityIds { get; }
    public string? SaveProfileId { get; }

    public SessionSelection WithRequestedModules(IEnumerable<string> requestedModuleIds)
    {
        return new SessionSelection(
            GameId,
            ProfileId,
            requestedModuleIds,
            Ports,
            CapabilityIds,
            SaveProfileId);
    }

    public SessionSelection WithCompatibilityInputs(
        IEnumerable<BehaviorPortSnapshot>? ports,
        IEnumerable<string>? capabilityIds,
        string? saveProfileId)
    {
        return new SessionSelection(
            GameId,
            ProfileId,
            RequestedModuleIds,
            ports,
            capabilityIds,
            saveProfileId);
    }
}

public sealed record SessionSwitchResult(
    SessionSwitchStatus Status,
    SessionStamp Stamp,
    SessionCandidate? Session,
    Exception? Error)
{
    public bool IsCommitted => Status == SessionSwitchStatus.Committed;
}

public sealed record SessionPreparationResult(
    SessionPreparationStatus Status,
    SessionStamp Stamp,
    SessionSwitchLease? Lease,
    Exception? Error)
{
    public bool IsPrepared => Status == SessionPreparationStatus.Prepared && Lease is not null;

    public SessionSwitchResult ToSwitchResult()
    {
        return new SessionSwitchResult(
            Status switch
            {
                SessionPreparationStatus.Cancelled => SessionSwitchStatus.Cancelled,
                SessionPreparationStatus.Stale => SessionSwitchStatus.Stale,
                SessionPreparationStatus.Faulted => SessionSwitchStatus.Faulted,
                _ => throw new InvalidOperationException("A prepared session must be committed or disposed through its lease."),
            },
            Stamp,
            null,
            Error);
    }
}

public sealed class SessionCandidate : IAsyncDisposable
{
    private int _sealed;
    private int _disposed;

    public SessionCandidate(SessionSelection selection, SessionGeneration generation, CompatibilityPlan compatibility)
    {
        Selection = selection ?? throw new ArgumentNullException(nameof(selection));
        Generation = generation;
        Compatibility = compatibility ?? throw new ArgumentNullException(nameof(compatibility));
    }

    public SessionSelection Selection { get; }
    public SessionGeneration Generation { get; }
    public CompatibilityPlan Compatibility { get; }
    public bool IsSealed => Volatile.Read(ref _sealed) != 0;
    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    internal void Seal()
    {
        if (IsDisposed)
            throw new ObjectDisposedException(nameof(SessionCandidate));
        Interlocked.Exchange(ref _sealed, 1);
    }

    public ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _disposed, 1);
        return ValueTask.CompletedTask;
    }
}

public delegate ValueTask<SessionCandidate> SessionCandidateFactory(
    SessionSelection selection,
    SessionGeneration generation,
    CancellationToken cancellationToken);

/// <summary>
/// Owns an uncommitted session candidate. Bridge activation must finish before
/// <see cref="CommitAsync"/> is called. Disposing a lease releases an
/// uncommitted candidate without changing the current session.
/// </summary>
public sealed class SessionSwitchLease : IAsyncDisposable
{
    private readonly SessionCoordinator _owner;
    private readonly CancellationTokenSource _candidateCancellation;
    private readonly CancellationToken _callerCancellation;
    private readonly object _gate = new();
    private SessionCandidate? _candidate;
    private SessionSwitchResult? _finalResult;

    internal SessionSwitchLease(
        SessionCoordinator owner,
        SessionCandidate candidate,
        SessionStamp stamp,
        CancellationTokenSource candidateCancellation,
        CancellationToken callerCancellation)
    {
        _owner = owner;
        _candidate = candidate;
        Stamp = stamp;
        _candidateCancellation = candidateCancellation;
        _callerCancellation = callerCancellation;
    }

    public SessionStamp Stamp { get; }

    public SessionCandidate Candidate
    {
        get
        {
            lock (_gate)
            {
                return _candidate ?? throw new ObjectDisposedException(nameof(SessionSwitchLease));
            }
        }
    }

    public bool IsCurrent
    {
        get
        {
            lock (_gate)
            {
                return _finalResult is null && _owner.IsLatest(Stamp, _candidateCancellation.Token);
            }
        }
    }

    public ValueTask<SessionSwitchResult> CommitAsync()
    {
        return _owner.CommitLeaseAsync(this);
    }

    public ValueTask DisposeAsync()
    {
        return _owner.AbortLeaseAsync(this);
    }

    internal object Gate => _gate;
    internal SessionCandidate? CandidateUnsafe
    {
        get => _candidate;
        set => _candidate = value;
    }
    internal SessionSwitchResult? FinalResult
    {
        get => _finalResult;
        set => _finalResult = value;
    }
    internal CancellationToken CandidateCancellationToken => _candidateCancellation.Token;
    internal CancellationToken CallerCancellationToken => _callerCancellation;
    internal CancellationTokenSource CandidateCancellationSource => _candidateCancellation;
}

/// <summary>
/// Owns the application-level current session and the candidate/commit boundary.
/// Long-running candidate work is cancellable and never runs under the commit
/// lock. Bridges use <see cref="PrepareSwitchAsync"/> to activate a candidate
/// before its short atomic commit.
/// </summary>
public sealed class SessionCoordinator : IAsyncDisposable
{
    private readonly SessionCandidateFactory _factory;
    private readonly SessionGenerationClock _generationClock = new();
    private readonly object _commitGate = new();
    private CancellationTokenSource? _activeCandidateCancellation;
    private SessionCandidate? _current;
    private long _operationValue;
    private bool _disposed;

    public SessionCoordinator(SessionCandidateFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public SessionGeneration CurrentGeneration => _generationClock.Current;

    public SessionCandidate? Current
    {
        get
        {
            lock (_commitGate)
                return _current;
        }
    }

    /// <summary>
    /// Builds and validates a candidate outside the commit gate. The returned
    /// lease remains current only until a newer request publishes a generation.
    /// </summary>
    public async ValueTask<SessionPreparationResult> PrepareSwitchAsync(
        SessionSelection selection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selection);
        var candidateCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        SessionGeneration generation;
        SessionStamp stamp;
        CancellationTokenSource? previousCancellation;
        lock (_commitGate)
        {
            if (_disposed)
            {
                candidateCancellation.Dispose();
                throw new ObjectDisposedException(nameof(SessionCoordinator));
            }

            generation = _generationClock.PublishNext();
            stamp = new SessionStamp(generation, new SessionOperationId(Interlocked.Increment(ref _operationValue)));
            previousCancellation = _activeCandidateCancellation;
            _activeCandidateCancellation = candidateCancellation;
        }
        CancelSafely(previousCancellation);

        SessionCandidate? candidate = null;
        try
        {
            candidate = await _factory(selection, generation, candidateCancellation.Token).ConfigureAwait(false);
            if (candidate is null)
                throw new InvalidOperationException("Session candidate factory returned null.");
            if (candidate.Generation != generation)
                throw new InvalidOperationException("Session candidate generation does not match the switch request.");

            if (!IsLatest(stamp, candidateCancellation.Token))
            {
                await candidate.DisposeAsync().ConfigureAwait(false);
                candidate = null;
                ReleaseCandidateCancellation(candidateCancellation);
                return new SessionPreparationResult(
                    GetPreparationCancellationStatus(cancellationToken),
                    stamp,
                    null,
                    null);
            }

            var lease = new SessionSwitchLease(this, candidate, stamp, candidateCancellation, cancellationToken);
            candidate = null;
            return new SessionPreparationResult(SessionPreparationStatus.Prepared, stamp, lease, null);
        }
        catch (OperationCanceledException) when (candidateCancellation.IsCancellationRequested)
        {
            if (candidate is not null)
                await candidate.DisposeAsync().ConfigureAwait(false);
            ReleaseCandidateCancellation(candidateCancellation);
            return new SessionPreparationResult(
                GetPreparationCancellationStatus(cancellationToken),
                stamp,
                null,
                null);
        }
        catch (Exception error)
        {
            if (candidate is not null)
                await candidate.DisposeAsync().ConfigureAwait(false);
            ReleaseCandidateCancellation(candidateCancellation);
            return new SessionPreparationResult(SessionPreparationStatus.Faulted, stamp, null, error);
        }
    }

    /// <summary>
    /// Convenience path for Core-only callers that have no external activation
    /// work. Bridge callers should use the prepare/commit lease explicitly.
    /// </summary>
    public async ValueTask<SessionSwitchResult> SwitchGameAsync(
        SessionSelection selection,
        CancellationToken cancellationToken = default)
    {
        var preparation = await PrepareSwitchAsync(selection, cancellationToken).ConfigureAwait(false);
        if (!preparation.IsPrepared)
            return preparation.ToSwitchResult();

        await using var lease = preparation.Lease!;
        return await lease.CommitAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Cancels candidate construction/activation without changing Current.
    /// This is used by a bridge during host shutdown before it takes ownership
    /// of the legacy backend gate.
    /// </summary>
    public void CancelActiveCandidate()
    {
        CancellationTokenSource? active;
        lock (_commitGate)
            active = _activeCandidateCancellation;
        CancelSafely(active);
    }

    public async ValueTask DisposeAsync()
    {
        SessionCandidate? current;
        CancellationTokenSource? activeCancellation;
        lock (_commitGate)
        {
            if (_disposed)
                return;
            _disposed = true;
            activeCancellation = Interlocked.Exchange(ref _activeCandidateCancellation, null);
            current = _current;
            _current = null;
        }

        CancelSafely(activeCancellation);
        DisposeCancellation(activeCancellation);
        if (current is not null)
            await current.DisposeAsync().ConfigureAwait(false);
    }

    internal async ValueTask<SessionSwitchResult> CommitLeaseAsync(SessionSwitchLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);

        SessionCandidate? previous = null;
        SessionCandidate? discarded = null;
        SessionSwitchResult result;
        lock (lease.Gate)
        {
            if (lease.FinalResult is not null)
                return lease.FinalResult;

            var candidate = lease.CandidateUnsafe
                ?? throw new ObjectDisposedException(nameof(SessionSwitchLease));
            lock (_commitGate)
            {
                if (!IsLatestUnderCommitGate(lease.Stamp, lease.CandidateCancellationToken))
                {
                    discarded = candidate;
                    lease.CandidateUnsafe = null;
                    result = new SessionSwitchResult(
                        GetSwitchCancellationStatus(lease.CallerCancellationToken),
                        lease.Stamp,
                        null,
                        null);
                }
                else
                {
                    candidate.Seal();
                    previous = _current;
                    _current = candidate;
                    lease.CandidateUnsafe = null;
                    result = new SessionSwitchResult(SessionSwitchStatus.Committed, lease.Stamp, candidate, null);
                }
            }
            lease.FinalResult = result;
        }

        if (discarded is not null)
            await discarded.DisposeAsync().ConfigureAwait(false);
        if (previous is not null)
            await previous.DisposeAsync().ConfigureAwait(false);
        ReleaseCandidateCancellation(lease.CandidateCancellationSource);
        return result;
    }

    internal async ValueTask AbortLeaseAsync(SessionSwitchLease lease)
    {
        ArgumentNullException.ThrowIfNull(lease);

        SessionCandidate? discarded = null;
        lock (lease.Gate)
        {
            if (lease.FinalResult is not null)
                return;

            discarded = lease.CandidateUnsafe;
            lease.CandidateUnsafe = null;
            lease.FinalResult = new SessionSwitchResult(
                GetSwitchCancellationStatus(lease.CallerCancellationToken),
                lease.Stamp,
                null,
                null);
        }

        if (discarded is not null)
            await discarded.DisposeAsync().ConfigureAwait(false);
        ReleaseCandidateCancellation(lease.CandidateCancellationSource);
    }

    internal bool IsLatest(SessionStamp stamp, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return false;
        lock (_commitGate)
            return IsLatestUnderCommitGate(stamp, cancellationToken);
    }

    private bool IsLatestUnderCommitGate(SessionStamp stamp, CancellationToken cancellationToken)
    {
        return !cancellationToken.IsCancellationRequested &&
            !_disposed &&
            stamp.Generation == _generationClock.Current;
    }

    private SessionPreparationStatus GetPreparationCancellationStatus(CancellationToken callerCancellation)
    {
        return callerCancellation.IsCancellationRequested
            ? SessionPreparationStatus.Cancelled
            : SessionPreparationStatus.Stale;
    }

    private SessionSwitchStatus GetSwitchCancellationStatus(CancellationToken callerCancellation)
    {
        return callerCancellation.IsCancellationRequested
            ? SessionSwitchStatus.Cancelled
            : SessionSwitchStatus.Stale;
    }

    private void ReleaseCandidateCancellation(CancellationTokenSource candidateCancellation)
    {
        lock (_commitGate)
        {
            if (ReferenceEquals(_activeCandidateCancellation, candidateCancellation))
                _activeCandidateCancellation = null;
        }
        DisposeCancellation(candidateCancellation);
    }

    private static void CancelSafely(CancellationTokenSource? cancellation)
    {
        if (cancellation is null)
            return;
        try
        {
            cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // A completed candidate may dispose its linked source concurrently
            // with a newer request replacing it. Cancellation is already moot.
        }
    }

    private static void DisposeCancellation(CancellationTokenSource? cancellation)
    {
        if (cancellation is null)
            return;
        try
        {
            cancellation.Dispose();
        }
        catch (ObjectDisposedException)
        {
            // The owner that completed the candidate has already released it.
        }
    }
}

internal static class ContractTextForSession
{
    public static string RequiredIdentifier(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value must not be empty.", parameterName);
        var normalized = value.Trim();
        if (!System.Text.RegularExpressions.Regex.IsMatch(
                normalized,
                "^[a-z][a-z0-9.\\-]*$",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant))
            throw new ArgumentException($"Invalid identifier: {normalized}", parameterName);
        return normalized;
    }
}
