using GEmuera.Core.Compatibility;

namespace GEmuera.Core.Session;

/// <summary>
/// Adapter boundary for the existing global/static Emuera runtime. The backend
/// owns legacy objects; this facade owns selection, plan construction,
/// generation, activation ordering, and rollback.
/// </summary>
public interface ILegacySessionBackend
{
    bool IsRunning { get; }

    ValueTask StartAsync(
        SessionSelection selection,
        CompatibilityPlan compatibility,
        SessionStamp stamp,
        CancellationToken cancellationToken = default);

    ValueTask StopAsync(
        SessionStamp stamp,
        CancellationToken cancellationToken = default);
}

public sealed record LegacySessionSwitchResult(
    SessionSwitchResult Session,
    bool BackendStarted,
    Exception? BackendError)
{
    /// <summary>
    /// True when an uncommitted candidate was removed and the prior legacy
    /// backend was started again. It is never used to claim parser/VM isolation.
    /// </summary>
    public bool PreviousBackendRestored { get; init; }

    public bool IsCommitted => Session.IsCommitted && BackendStarted;
}

/// <summary>
/// Provides a transactional shell around the old process-wide runtime. A
/// candidate becomes Current only after the backend accepts it; a failed or
/// stale activation keeps (or restores) the old backend and old Current.
/// </summary>
public sealed class LegacySessionFacade : IAsyncDisposable
{
    private readonly CompatibilityPlanBuilder _planBuilder;
    private readonly CompatibilityProfileCatalog _profileCatalog;
    private readonly ILegacySessionBackend _backend;
    private readonly SessionCoordinator _coordinator;
    private readonly SemaphoreSlim _backendGate = new(1, 1);
    private int _backendStarted;
    private long _backendGenerationValue;
    private bool _disposed;

    public LegacySessionFacade(
        CompatibilityPlanBuilder planBuilder,
        ILegacySessionBackend backend)
        : this(
            planBuilder,
            BuiltInDialectCatalog.CreateLegacyProfileCatalog(),
            backend)
    {
    }

    public LegacySessionFacade(
        CompatibilityPlanBuilder planBuilder,
        CompatibilityProfileCatalog profileCatalog,
        ILegacySessionBackend backend)
    {
        _planBuilder = planBuilder ?? throw new ArgumentNullException(nameof(planBuilder));
        _profileCatalog = profileCatalog ?? throw new ArgumentNullException(nameof(profileCatalog));
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        _profileCatalog.Freeze();
        _coordinator = new SessionCoordinator(BuildCandidateAsync);
    }

    public static LegacySessionFacade CreateLegacyBaseline(ILegacySessionBackend backend)
    {
        var dialectCatalog = BuiltInDialectCatalog.CreateLegacyBaseline();
        return new LegacySessionFacade(
            new CompatibilityPlanBuilder(dialectCatalog),
            BuiltInDialectCatalog.CreateLegacyProfileCatalog(),
            backend);
    }

    public SessionCandidate? Current => _coordinator.Current;
    public CompatibilityPlan? CurrentPlan => Current?.Compatibility;
    public SessionGeneration CurrentGeneration => _coordinator.CurrentGeneration;
    public SessionGeneration BackendGeneration => new(Interlocked.Read(ref _backendGenerationValue));
    public bool IsBackendRunning => BackendStarted || _backend.IsRunning;

    /// <summary>
    /// Builds a frozen plan, activates the legacy backend, then atomically
    /// commits the candidate. Optional inputs are folded into SessionSelection
    /// rather than being accepted and silently discarded.
    /// </summary>
    public async ValueTask<LegacySessionSwitchResult> SwitchAsync(
        SessionSelection selection,
        IEnumerable<string>? requestedModuleIds,
        IEnumerable<BehaviorPortSnapshot>? ports = null,
        IEnumerable<string>? capabilities = null,
        string? saveProfileId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ThrowIfDisposed();

        var effectiveSelection = new SessionSelection(
            selection.GameId,
            selection.ProfileId,
            requestedModuleIds ?? selection.RequestedModuleIds,
            ports ?? selection.Ports,
            capabilities ?? selection.CapabilityIds,
            saveProfileId ?? selection.SaveProfileId);
        var preparation = await _coordinator
            .PrepareSwitchAsync(effectiveSelection, cancellationToken)
            .ConfigureAwait(false);
        if (!preparation.IsPrepared)
            return new LegacySessionSwitchResult(preparation.ToSwitchResult(), false, preparation.Error);

        await using var lease = preparation.Lease!;
        try
        {
            await _backendGate.WaitAsync(lease.CandidateCancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (lease.CandidateCancellationToken.IsCancellationRequested)
        {
            return new LegacySessionSwitchResult(BuildTerminalResult(lease), false, null);
        }

        try
        {
            // A newer candidate may have been prepared while this request was
            // waiting. Never stop a live backend for an obsolete candidate.
            if (!lease.IsCurrent)
                return new LegacySessionSwitchResult(BuildTerminalResult(lease), false, null);

            var candidate = lease.Candidate;
            var previous = CaptureBackendSnapshot(_coordinator.Current);
            try
            {
                if (previous.WasRunning)
                    await StopBackendAsync(previous.Stamp, CancellationToken.None).ConfigureAwait(false);

                await _backend.StartAsync(
                    candidate.Selection,
                    candidate.Compatibility,
                    lease.Stamp,
                    lease.CandidateCancellationToken).ConfigureAwait(false);
                SetBackendState(true, candidate.Generation);
            }
            catch (OperationCanceledException) when (lease.CandidateCancellationToken.IsCancellationRequested)
            {
                var rollbackError = await RestorePreviousBackendAsync(previous, lease.Stamp).ConfigureAwait(false);
                return CreateUncommittedResult(lease, previous, rollbackError, null);
            }
            catch (Exception error)
            {
                var rollbackError = await RestorePreviousBackendAsync(previous, lease.Stamp).ConfigureAwait(false);
                return CreateUncommittedResult(lease, previous, rollbackError, error);
            }

            // StartAsync may be unable to cancel an old thread-based backend.
            // Generation remains the final authority before committing Current.
            if (!lease.IsCurrent)
            {
                var rollbackError = await RestorePreviousBackendAsync(previous, lease.Stamp).ConfigureAwait(false);
                return CreateUncommittedResult(lease, previous, rollbackError, null);
            }

            var commit = await lease.CommitAsync().ConfigureAwait(false);
            if (!commit.IsCommitted)
            {
                var rollbackError = await RestorePreviousBackendAsync(previous, lease.Stamp).ConfigureAwait(false);
                return CreateUncommittedResult(lease, previous, rollbackError, null, commit);
            }

            return new LegacySessionSwitchResult(commit, true, null);
        }
        finally
        {
            _backendGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        _coordinator.CancelActiveCandidate();

        await _backendGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (BackendStarted || _backend.IsRunning)
                await StopBackendAsync(
                    new SessionStamp(BackendGeneration, SessionOperationId.None),
                    CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                await _coordinator.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                _backendGate.Release();
                _backendGate.Dispose();
            }
        }
    }

    private bool BackendStarted => Volatile.Read(ref _backendStarted) != 0;

    private void SetBackendState(bool started, SessionGeneration generation)
    {
        Interlocked.Exchange(ref _backendGenerationValue, generation.Value);
        Volatile.Write(ref _backendStarted, started ? 1 : 0);
    }

    private async ValueTask StopBackendAsync(SessionStamp stamp, CancellationToken cancellationToken)
    {
        await _backend.StopAsync(stamp, cancellationToken).ConfigureAwait(false);
        SetBackendState(false, SessionGeneration.Initial);
    }

    private BackendSnapshot CaptureBackendSnapshot(SessionCandidate? current)
    {
        var wasRunning = BackendStarted || _backend.IsRunning;
        var backendGeneration = BackendGeneration;
        var generation = backendGeneration != SessionGeneration.Initial
            ? backendGeneration
            : current?.Generation ?? SessionGeneration.Initial;
        return new BackendSnapshot(
            current,
            wasRunning,
            new SessionStamp(generation, SessionOperationId.None));
    }

    private async ValueTask<Exception?> RestorePreviousBackendAsync(
        BackendSnapshot previous,
        SessionStamp activeStamp)
    {
        Exception? rollbackError = null;
        try
        {
            if (BackendStarted || _backend.IsRunning)
                await StopBackendAsync(activeStamp, CancellationToken.None).ConfigureAwait(false);

            if (!previous.WasRunning || previous.Session is null)
                return null;

            await _backend.StartAsync(
                previous.Session.Selection,
                previous.Session.Compatibility,
                previous.Stamp,
                CancellationToken.None).ConfigureAwait(false);
            SetBackendState(true, previous.Session.Generation);
        }
        catch (Exception error)
        {
            rollbackError = error;
            if (_backend.IsRunning && previous.Session is not null)
            {
                SetBackendState(true, previous.Session.Generation);
            }
            else
            {
                SetBackendState(false, SessionGeneration.Initial);
            }
        }

        return rollbackError;
    }

    private LegacySessionSwitchResult CreateUncommittedResult(
        SessionSwitchLease lease,
        BackendSnapshot previous,
        Exception? rollbackError,
        Exception? activationError,
        SessionSwitchResult? terminalResult = null)
    {
        var error = CombineErrors(activationError, rollbackError);
        var session = error is null
            ? terminalResult ?? BuildTerminalResult(lease)
            : new SessionSwitchResult(SessionSwitchStatus.Faulted, lease.Stamp, null, error);
        return new LegacySessionSwitchResult(session, false, error)
        {
            PreviousBackendRestored = rollbackError is null && previous.WasRunning && previous.Session is not null,
        };
    }

    private static Exception? CombineErrors(Exception? activationError, Exception? rollbackError)
    {
        if (activationError is null)
            return rollbackError;
        if (rollbackError is null)
            return activationError;
        return new AggregateException(activationError, rollbackError);
    }

    private static SessionSwitchResult BuildTerminalResult(SessionSwitchLease lease)
    {
        return new SessionSwitchResult(
            lease.CallerCancellationToken.IsCancellationRequested
                ? SessionSwitchStatus.Cancelled
                : SessionSwitchStatus.Stale,
            lease.Stamp,
            null,
            null);
    }

    private ValueTask<SessionCandidate> BuildCandidateAsync(
        SessionSelection selection,
        SessionGeneration generation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var requestedModules = selection.RequestedModuleIds.Count > 0
            ? selection.RequestedModuleIds
            : _profileCatalog.Resolve(selection.ProfileId).RootModuleIds;
        var compatibility = _planBuilder.Build(
            selection.ProfileId,
            requestedModules,
            selection.Ports,
            selection.CapabilityIds,
            selection.SaveProfileId);
        return ValueTask.FromResult(new SessionCandidate(selection, generation, compatibility));
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(LegacySessionFacade));
    }

    private sealed record BackendSnapshot(
        SessionCandidate? Session,
        bool WasRunning,
        SessionStamp Stamp);
}
