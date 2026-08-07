using GEmuera.Core.Session;

namespace GEmuera.Core.Ports;

public sealed class RuntimePortManifestBuilder
{
    private readonly List<PortManifestEntry> _entries = new();
    private bool _frozen;

    public bool IsFrozen => _frozen;

    public void Add(PortManifestEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (_frozen)
            throw new InvalidOperationException("Runtime port manifest is frozen.");
        if (_entries.Any(existing => existing.PortType == entry.PortType))
            throw new InvalidOperationException($"Duplicate runtime port: {entry.PortType.Value}");
        _entries.Add(entry);
    }

    public PortManifest Freeze()
    {
        if (_frozen)
            throw new InvalidOperationException("Runtime port manifest is already frozen.");
        _frozen = true;
        return new PortManifest(_entries);
    }
}

public readonly record struct RuntimePortStamp(
    SessionGeneration Generation,
    SessionOperationId OperationId)
{
    public SessionStamp Session => new(Generation, OperationId);
}

/// <summary>
/// Session-scoped composition of the typed port contracts. It keeps the
/// manifest and dispatcher together so stale completions cannot cross a
/// generation boundary.
/// </summary>
public sealed class RuntimePortHub : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly InlinePortOwnerScheduler _ownerScheduler;
    private readonly CompletionDispatcher _dispatcher;
    private readonly InputCoordinator _input;
    private readonly AudioGenerationGate _audio;
    private long _operation;
    private bool _closed;

    public RuntimePortHub(SessionGeneration generation, PortManifest? manifest = null, int? ownerThreadId = null)
    {
        if (generation.Value <= 0)
            throw new ArgumentOutOfRangeException(nameof(generation));
        Manifest = manifest ?? DefaultPortManifest.CreateDefault();
        _ownerScheduler = new InlinePortOwnerScheduler(ownerThreadId ?? Environment.CurrentManagedThreadId);
        _dispatcher = new CompletionDispatcher(generation, _ownerScheduler);
        _input = new InputCoordinator(generation);
        _audio = new AudioGenerationGate(generation);
    }

    public PortManifest Manifest { get; }
    public SessionGeneration Generation => _dispatcher.Generation;
    public CompletionDispatcher Dispatcher => _dispatcher;
    public bool IsClosed => _dispatcher.IsClosed;

    public PortRequest<TArguments> CreateRequest<TArguments>(
        PortTypeId portType,
        CapabilityId capability,
        TArguments arguments)
    {
        lock (_gate)
        {
            EnsureOpen();
            var entry = Manifest.GetRequired(portType);
            if (entry.Capability != capability)
                throw new InvalidOperationException($"Port '{portType.Value}' does not provide capability '{capability.Value}'.");
            var operation = new SessionOperationId(Interlocked.Increment(ref _operation));
            return new PortRequest<TArguments>(portType, capability, Generation, operation, arguments);
        }
    }

    public InputSubmissionStatus SubmitInput(NormalizedAction action)
    {
        lock (_gate)
        {
            EnsureOpen();
            return _input.Submit(action);
        }
    }

    public bool AcceptAudio(SessionGeneration generation) => !_closed && _audio.Accept(generation);

    public ValueTask<CompletionDispatchResult> DispatchAsync<TArguments, TPayload>(
        PortRequest<TArguments> request,
        PortCompletion<TPayload> completion,
        Func<PortCompletion<TPayload>, ValueTask> continuation,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_closed)
                return ValueTask.FromResult(new CompletionDispatchResult(
                    CompletionDispatchStatus.Closed,
                    new PortFault(PortErrorCode.LifecycleReset, "Runtime port hub is closed.")));
        }
        return _dispatcher.DispatchAsync(request, completion, continuation, cancellationToken);
    }

    public void AdvanceGeneration(SessionGeneration generation)
    {
        lock (_gate)
        {
            EnsureOpen();
            _dispatcher.AdvanceGeneration(generation);
            _input.AdvanceGeneration(generation);
            _audio.Reset(generation);
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_closed)
                return ValueTask.CompletedTask;
            _closed = true;
            _dispatcher.Close();
            _input.Dispose();
        }
        return ValueTask.CompletedTask;
    }

    private void EnsureOpen()
    {
        if (_closed)
            throw new ObjectDisposedException(nameof(RuntimePortHub));
    }
}

/// <summary>
/// Minimal owner scheduler used by the prototype and by headless Core hosts.
/// A real Godot bridge replaces this with a main-thread queue.
/// </summary>
public sealed class InlinePortOwnerScheduler : IPortOwnerScheduler
{
    private readonly int _ownerThreadId;

    public InlinePortOwnerScheduler(int ownerThreadId)
    {
        if (ownerThreadId <= 0)
            throw new ArgumentOutOfRangeException(nameof(ownerThreadId));
        _ownerThreadId = ownerThreadId;
    }

    public bool IsOwnerThread => Environment.CurrentManagedThreadId == _ownerThreadId;

    public ValueTask EnqueueAsync(Func<ValueTask> callback, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(callback);
        cancellationToken.ThrowIfCancellationRequested();
        if (IsOwnerThread)
            return callback();
        return new ValueTask(Task.Run(async () => await callback().ConfigureAwait(false), cancellationToken));
    }
}
