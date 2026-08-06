using GEmuera.Core.Session;

namespace GEmuera.Core.Experiments;

public enum PrototypeSchedulerMode
{
    Legacy,
    CooperativeExperimental,
}

public enum PrototypeStepStatus
{
    Disabled,
    Yielded,
    Completed,
    Cancelled,
    Faulted,
}

public sealed record PrototypeStepResult(
    PrototypeStepStatus Status,
    int WorkUnits,
    long TraceOrdinal,
    SessionStamp Session,
    string? Fault = null);

/// <summary>
/// Small M6 scheduler shell. It is an explicit experiment and does not alter
/// the production legacy runner when its feature flag is disabled.
/// </summary>
public sealed class PrototypeScheduler : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly FeatureFlags _flags;
    private readonly SessionStamp _session;
    private long _trace;
    private bool _disposed;

    public PrototypeScheduler(SessionStamp session, FeatureFlags flags = default)
    {
        if (session.Generation.Value <= 0 || session.OperationId.Value <= 0)
            throw new ArgumentException("Prototype scheduler requires a live session stamp.", nameof(session));
        _session = session;
        _flags = flags == default ? FeatureFlags.Default : flags;
        Mode = _flags.Scheduler ? PrototypeSchedulerMode.CooperativeExperimental : PrototypeSchedulerMode.Legacy;
    }

    public PrototypeSchedulerMode Mode { get; }
    public bool IsDisposed { get { lock (_gate) return _disposed; } }
    public FeatureFlags Flags => _flags;

    public async ValueTask<PrototypeStepResult> StepAsync(
        int maxWorkUnits,
        Func<int, CancellationToken, ValueTask<int>> step,
        CancellationToken cancellationToken = default)
    {
        if (maxWorkUnits <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxWorkUnits));
        ArgumentNullException.ThrowIfNull(step);

        lock (_gate)
        {
            if (_disposed)
                return Result(PrototypeStepStatus.Cancelled, 0, "scheduler.disposed");
            if (!_flags.Scheduler)
                return Result(PrototypeStepStatus.Disabled, 0);
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var work = await step(maxWorkUnits, cancellationToken).ConfigureAwait(false);
            if (work < 0 || work > maxWorkUnits)
                return Result(PrototypeStepStatus.Faulted, 0, "scheduler.budget");
            return Result(work == 0 || work < maxWorkUnits ? PrototypeStepStatus.Yielded : PrototypeStepStatus.Completed, work);
        }
        catch (OperationCanceledException)
        {
            return Result(PrototypeStepStatus.Cancelled, 0);
        }
        catch (Exception error)
        {
            return Result(PrototypeStepStatus.Faulted, 0, error.GetType().Name);
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
            _disposed = true;
        return ValueTask.CompletedTask;
    }

    private PrototypeStepResult Result(PrototypeStepStatus status, int work, string? fault = null)
    {
        return new PrototypeStepResult(status, work, Interlocked.Increment(ref _trace), _session, fault);
    }
}

public sealed class PrototypeRendererExperimentGate
{
    private readonly object _gate = new();
    private FeatureFlags _flags;
    private string? _activeArtifact;

    public PrototypeRendererExperimentGate(FeatureFlags flags = default)
    {
        _flags = flags == default ? FeatureFlags.Default : flags;
    }

    public bool IsEnabled { get { lock (_gate) return _flags.Renderer && _activeArtifact is not null; } }
    public string? ActiveArtifact { get { lock (_gate) return _activeArtifact; } }

    public bool TryEnable(string artifactId)
    {
        if (string.IsNullOrWhiteSpace(artifactId))
            throw new ArgumentException("Renderer artifact id is required.", nameof(artifactId));
        lock (_gate)
        {
            if (!_flags.Renderer)
                return false;
            _activeArtifact = artifactId.Trim();
            return true;
        }
    }

    public void Rollback()
    {
        lock (_gate)
            _activeArtifact = null;
    }
}
