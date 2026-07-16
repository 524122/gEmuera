using GEmuera.Core.Compatibility;
using GEmuera.Core.Parsing;
using GEmuera.Core.Resources;
using GEmuera.Core.Runtime;
using GEmuera.Core.Save;
using GEmuera.Core.Session;
using GEmuera.Core.State;

namespace GEmuera.Core.Application;

public enum GameSessionState
{
    Created,
    Starting,
    Running,
    Stopping,
    Stopped,
    Faulted,
}

/// <summary>
/// Immutable inputs used to assemble one session. The application layer owns
/// composition; the legacy host remains a separate adapter.
/// </summary>
public sealed record GameSessionOptions
{
    public GameSessionOptions(
        SessionSelection selection,
        CompatibilityPlan compatibility,
        SessionGeneration generation,
        ContentToken source,
        long resourceMemoryCapacityBytes = 256 * 1024 * 1024)
    {
        Selection = selection ?? throw new ArgumentNullException(nameof(selection));
        Compatibility = compatibility ?? throw new ArgumentNullException(nameof(compatibility));
        if (generation.Value <= 0)
            throw new ArgumentOutOfRangeException(nameof(generation));
        if (string.IsNullOrWhiteSpace(source.Value))
            throw new ArgumentException("A session source token is required.", nameof(source));
        if (resourceMemoryCapacityBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(resourceMemoryCapacityBytes));

        Generation = generation;
        Source = source;
        ResourceMemoryCapacityBytes = resourceMemoryCapacityBytes;
    }

    public SessionSelection Selection { get; }
    public CompatibilityPlan Compatibility { get; }
    public SessionGeneration Generation { get; }
    public ContentToken Source { get; }
    public long ResourceMemoryCapacityBytes { get; }
}

public sealed record GameSessionSnapshot(
    SessionGeneration Generation,
    string GameId,
    string ProfileId,
    string PlanHash,
    GameSessionState State,
    long VariableSequence,
    int PixelSurfaceCount,
    int ResourceCount,
    string? SaveProfileId);

/// <summary>
/// Session-scoped data and services. It is intentionally a CLR object rather
/// than a Godot Node; bridges project its immutable snapshots to the scene tree.
/// </summary>
public sealed class GameSession : IAsyncDisposable
{
    private int _state = (int)GameSessionState.Created;
    private int _disposed;

    public GameSession(GameSessionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Options = options;
        Generation = options.Generation;
        Selection = options.Selection;
        Compatibility = options.Compatibility;
        CompatibilityView = new DialectPlanConsumer(Compatibility);
        Variables = new VariableStore(Generation);
        PixelStore = new PixelStore();
        ResourceRuntime = new ResourceRuntime(Generation, options.ResourceMemoryCapacityBytes);
        var profile = new SaveProfileId(options.Compatibility.SaveProfileId ?? "gemuera.default");
        SaveService = new SaveService(Variables, new DeterministicSaveCodec(profile), Generation);
        CoreAdapter = new LegacyCoreAdapter(new CoreSessionBoundary(Generation, Compatibility, new ContentToken(options.Source.Value)));
    }

    public GameSessionOptions Options { get; }
    public SessionGeneration Generation { get; }
    public SessionSelection Selection { get; }
    public CompatibilityPlan Compatibility { get; }
    public DialectPlanConsumer CompatibilityView { get; }
    public VariableStore Variables { get; }
    public PixelStore PixelStore { get; }
    public ResourceRuntime ResourceRuntime { get; }
    public SaveService SaveService { get; }
    public LegacyCoreAdapter CoreAdapter { get; }
    public GameSessionState State => (GameSessionState)Volatile.Read(ref _state);
    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    public void Start()
    {
        EnsureNotDisposed();
        if (Interlocked.CompareExchange(ref _state, (int)GameSessionState.Starting, (int)GameSessionState.Created) != (int)GameSessionState.Created)
            throw new InvalidOperationException($"Session cannot start from state {State}.");
        Volatile.Write(ref _state, (int)GameSessionState.Running);
    }

    public ErbParseResult Parse(string source, string sourceId)
    {
        EnsureRunning();
        var token = new ContentToken(string.IsNullOrWhiteSpace(sourceId) ? Options.Source.Value : sourceId);
        return CoreAdapter.Parse(source ?? throw new ArgumentNullException(nameof(source)), token);
    }

    public PixelHandle CreateSurface(int width, int height, PixelFormat format = PixelFormat.Rgba8888StraightAlpha)
    {
        EnsureRunning();
        return PixelStore.CreateEmpty(width, height, format, sourceToken: new SourceToken(Options.Source.Value));
    }

    public GameSessionSnapshot CaptureSnapshot()
    {
        return new GameSessionSnapshot(
            Generation,
            Selection.GameId,
            Selection.ProfileId,
            Compatibility.CanonicalHash,
            State,
            Variables.Sequence,
            PixelStore.Capture().Surfaces.Count,
            ResourceRuntime.Count,
            SaveService.Profile.Value);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        Volatile.Write(ref _state, (int)GameSessionState.Stopping);
        SaveService.Dispose();
        ResourceRuntime.Dispose();
        PixelStore.Dispose();
        Volatile.Write(ref _state, (int)GameSessionState.Stopped);
        await ValueTask.CompletedTask;
    }

    private void EnsureRunning()
    {
        EnsureNotDisposed();
        if (State != GameSessionState.Running)
            throw new InvalidOperationException($"Session is not running (state={State}).");
    }

    private void EnsureNotDisposed()
    {
        if (IsDisposed)
            throw new ObjectDisposedException(nameof(GameSession));
    }
}

public sealed record RuntimeSwitchResult(
    SessionSwitchStatus Status,
    SessionStamp Stamp,
    GameSession? Session,
    Exception? Error)
{
    public bool IsCommitted => Status == SessionSwitchStatus.Committed;
}

/// <summary>
/// Application-level composition root for the Core prototype. It uses the
/// existing candidate/commit coordinator and never exposes filesystem paths to
/// the Core plan builder.
/// </summary>
public sealed class CoreApplicationRuntime : IAsyncDisposable
{
    private readonly SessionCoordinator _coordinator;
    private readonly object _gate = new();
    private GameSession? _current;
    private int _disposed;

    public CoreApplicationRuntime()
    {
        _coordinator = new SessionCoordinator(BuildCandidateAsync);
    }

    public GameSession? Current
    {
        get
        {
            lock (_gate)
                return _current;
        }
    }

    public SessionGeneration CurrentGeneration => _coordinator.CurrentGeneration;

    public async ValueTask<RuntimeSwitchResult> SwitchAsync(
        SessionSelection selection,
        ContentToken source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selection);
        EnsureNotDisposed();

        var result = await _coordinator.SwitchGameAsync(selection, cancellationToken).ConfigureAwait(false);
        if (!result.IsCommitted || result.Session is null)
            return new RuntimeSwitchResult(result.Status, result.Stamp, null, result.Error);

        GameSession next;
        try
        {
            next = new GameSession(new GameSessionOptions(selection, result.Session.Compatibility, result.Session.Generation, source));
            next.Start();
        }
        catch (Exception error)
        {
            return new RuntimeSwitchResult(SessionSwitchStatus.Faulted, result.Stamp, null, error);
        }

        GameSession? previous;
        lock (_gate)
        {
            previous = _current;
            _current = next;
        }

        if (previous is not null)
            await previous.DisposeAsync().ConfigureAwait(false);
        return new RuntimeSwitchResult(result.Status, result.Stamp, next, null);
    }

    public async ValueTask StopAsync()
    {
        GameSession? current;
        lock (_gate)
        {
            current = _current;
            _current = null;
        }
        if (current is not null)
            await current.DisposeAsync().ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        await StopAsync().ConfigureAwait(false);
        await _coordinator.DisposeAsync().ConfigureAwait(false);
    }

    private static ValueTask<SessionCandidate> BuildCandidateAsync(
        SessionSelection selection,
        SessionGeneration generation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var modules = BuiltInDialectCatalog.CreateLegacyBaseline();
        var profiles = BuiltInDialectCatalog.CreateLegacyProfileCatalog();
        var profile = profiles.Resolve(selection.ProfileId);
        var roots = selection.RequestedModuleIds.Count == 0 ? profile.RootModuleIds : selection.RequestedModuleIds;
        var plan = new CompatibilityPlanBuilder(modules).Build(
            selection.ProfileId,
            roots,
            selection.Ports,
            selection.CapabilityIds,
            selection.SaveProfileId);
        return ValueTask.FromResult(new SessionCandidate(selection, generation, plan));
    }

    private void EnsureNotDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(CoreApplicationRuntime));
    }
}
