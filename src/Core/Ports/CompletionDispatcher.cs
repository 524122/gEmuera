using GEmuera.Core.Session;

namespace GEmuera.Core.Ports;

public interface IPortOwnerScheduler
{
    bool IsOwnerThread { get; }

    ValueTask EnqueueAsync(Func<ValueTask> callback, CancellationToken cancellationToken = default);
}

public enum CompletionDispatchStatus
{
    Accepted,
    Stale,
    Duplicate,
    Closed,
    Invalid,
    OwnerUnavailable,
    ContinuationFaulted,
}

public sealed record CompletionDispatchResult(CompletionDispatchStatus Status, PortFault? Fault = null)
{
    public bool WasApplied => Status == CompletionDispatchStatus.Accepted;
}

public sealed class CompletionDispatcher
{
    private readonly object _gate = new();
    private readonly IPortOwnerScheduler _owner;
    private readonly Dictionary<PortOperationKey, TaskCompletionSource<CompletionDispatchResult>> _queued = new();
    private readonly HashSet<PortOperationKey> _completed = new();
    private SessionGeneration _generation;
    private bool _accepting = true;

    public CompletionDispatcher(SessionGeneration generation, IPortOwnerScheduler owner)
    {
        _generation = generation;
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
    }

    public SessionGeneration Generation
    {
        get
        {
            lock (_gate)
                return _generation;
        }
    }

    public bool IsClosed
    {
        get
        {
            lock (_gate)
                return !_accepting;
        }
    }

    public void AdvanceGeneration(SessionGeneration generation)
    {
        List<TaskCompletionSource<CompletionDispatchResult>> stale;
        lock (_gate)
        {
            if (!_accepting)
                throw new ObjectDisposedException(nameof(CompletionDispatcher));
            if (generation.Value <= _generation.Value)
                throw new ArgumentOutOfRangeException(nameof(generation), "Session generation must advance.");

            _generation = generation;
            _completed.Clear();
            stale = _queued
                .Where(pair => pair.Key.Generation != generation)
                .Select(pair => pair.Value)
                .ToList();
            foreach (var key in _queued.Keys.Where(key => key.Generation != generation).ToArray())
                _queued.Remove(key);
        }

        foreach (var completion in stale)
        {
            completion.TrySetResult(new CompletionDispatchResult(
                CompletionDispatchStatus.Stale,
                new PortFault(PortErrorCode.StaleCompletion, "Completion belongs to an older session generation.")));
        }
    }

    public void Close()
    {
        TaskCompletionSource<CompletionDispatchResult>[] pending;
        lock (_gate)
        {
            if (!_accepting)
                return;
            _accepting = false;
            pending = _queued.Values.ToArray();
            _queued.Clear();
        }

        foreach (var completion in pending)
        {
            completion.TrySetResult(new CompletionDispatchResult(
                CompletionDispatchStatus.Closed,
                new PortFault(PortErrorCode.LifecycleReset, "The session completion dispatcher is closed.")));
        }
    }

    public ValueTask<CompletionDispatchResult> DispatchAsync<TArguments, TPayload>(
        PortRequest<TArguments> request,
        PortCompletion<TPayload> completion,
        Func<PortCompletion<TPayload>, ValueTask> continuation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(completion);
        ArgumentNullException.ThrowIfNull(continuation);

        if (request.Key != completion.Key || request.PortType != completion.PortType || request.Capability != completion.Capability)
        {
            return ValueTask.FromResult(new CompletionDispatchResult(
                CompletionDispatchStatus.Invalid,
                new PortFault(PortErrorCode.InvalidRequest, "Completion does not match its request.")));
        }

        var source = new TaskCompletionSource<CompletionDispatchResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            if (!_accepting)
                return ValueTask.FromResult(ClosedResult());
            if (request.Generation != _generation)
                return ValueTask.FromResult(StaleResult());
            if (_completed.Contains(request.Key) || _queued.ContainsKey(request.Key))
                return ValueTask.FromResult(DuplicateResult());
            _queued.Add(request.Key, source);
        }

        try
        {
            var enqueue = _owner.EnqueueAsync(
                () => ApplyOnOwnerAsync(request, completion, continuation, source),
                cancellationToken);
            if (enqueue.IsCompletedSuccessfully)
            {
                enqueue.GetAwaiter().GetResult();
                return new ValueTask<CompletionDispatchResult>(source.Task);
            }
            return AwaitEnqueueAsync(enqueue, request.Key, source);
        }
        catch (OperationCanceledException)
        {
            RemoveQueued(request.Key, source);
            source.TrySetResult(new CompletionDispatchResult(
                CompletionDispatchStatus.OwnerUnavailable,
                new PortFault(PortErrorCode.Cancelled, "Owner scheduling was cancelled.")));
            return new ValueTask<CompletionDispatchResult>(source.Task);
        }
        catch (Exception exception)
        {
            RemoveQueued(request.Key, source);
            source.TrySetResult(new CompletionDispatchResult(
                CompletionDispatchStatus.OwnerUnavailable,
                new PortFault(PortErrorCode.OwnerUnavailable, exception.Message, true)));
            return new ValueTask<CompletionDispatchResult>(source.Task);
        }
    }

    private async ValueTask<CompletionDispatchResult> AwaitEnqueueAsync(
        ValueTask enqueue,
        PortOperationKey key,
        TaskCompletionSource<CompletionDispatchResult> source)
    {
        try
        {
            await enqueue.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            RemoveQueued(key, source);
            source.TrySetResult(new CompletionDispatchResult(
                CompletionDispatchStatus.OwnerUnavailable,
                new PortFault(PortErrorCode.OwnerUnavailable, exception.Message, true)));
        }

        return await source.Task.ConfigureAwait(false);
    }

    private async ValueTask ApplyOnOwnerAsync<TArguments, TPayload>(
        PortRequest<TArguments> request,
        PortCompletion<TPayload> completion,
        Func<PortCompletion<TPayload>, ValueTask> continuation,
        TaskCompletionSource<CompletionDispatchResult> source)
    {
        CompletionDispatchResult? decision = null;
        lock (_gate)
        {
            if (!_queued.Remove(request.Key))
            {
                decision = request.Generation == _generation ? DuplicateResult() : StaleResult();
            }
            else if (!_accepting)
            {
                decision = ClosedResult();
            }
            else if (request.Generation != _generation)
            {
                decision = StaleResult();
            }
            else
            {
                _completed.Add(request.Key);
            }
        }

        if (decision is not null)
        {
            source.TrySetResult(decision);
            return;
        }

        try
        {
            await continuation(completion).ConfigureAwait(true);
            source.TrySetResult(new CompletionDispatchResult(CompletionDispatchStatus.Accepted));
        }
        catch (Exception exception)
        {
            source.TrySetResult(new CompletionDispatchResult(
                CompletionDispatchStatus.ContinuationFaulted,
                new PortFault(PortErrorCode.InvalidState, exception.Message)));
        }
    }

    private void RemoveQueued(PortOperationKey key, TaskCompletionSource<CompletionDispatchResult> source)
    {
        lock (_gate)
        {
            if (_queued.TryGetValue(key, out var current) && ReferenceEquals(current, source))
                _queued.Remove(key);
        }
    }

    private CompletionDispatchResult ClosedResult() => new(
        CompletionDispatchStatus.Closed,
        new PortFault(PortErrorCode.LifecycleReset, "The session completion dispatcher is closed."));

    private CompletionDispatchResult StaleResult() => new(
        CompletionDispatchStatus.Stale,
        new PortFault(PortErrorCode.StaleCompletion, "Completion belongs to an inactive session generation."));

    private CompletionDispatchResult DuplicateResult() => new(
        CompletionDispatchStatus.Duplicate,
        new PortFault(PortErrorCode.DuplicateOperation, "Operation completion was already submitted."));
}
