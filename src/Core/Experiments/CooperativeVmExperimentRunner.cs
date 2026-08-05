using GEmuera.Core.Runtime;

namespace GEmuera.Core.Experiments;

/// <summary>
/// M6-only execution shell around the existing immutable VM host contract.
/// It is an explicitly enabled candidate runner; it never selects or replaces
/// the legacy host when the scheduler flag is off.
/// </summary>
public sealed class CooperativeVmExperimentRunner : IAsyncDisposable
{
    private readonly M6ExperimentDefinition _definition;
    private readonly IErbInterpreterHost _host;
    private readonly object _stateGate = new();
    private long _lastEffectSequence;
    private long _pendingOperation;
    private long _traceOrdinal;
    private int _state = (int)VmExecutionState.Created;
    private int _executionInProgress;
    private int _disposed;

    public CooperativeVmExperimentRunner(
        M6ExperimentDefinition definition,
        IErbInterpreterHost host)
    {
        _definition = definition ?? throw new ArgumentNullException(nameof(definition));
        ArgumentNullException.ThrowIfNull(host);
        _host = host ?? throw new ArgumentNullException(nameof(host));
        if (_host.Compatibility is null)
            throw new ArgumentException("The VM host must expose an immutable compatibility plan.", nameof(host));
        _definition.EnsureRunnable();
    }

    public M6ExperimentIdentity Identity => _definition.Identity;
    public YieldabilityAuditInventory YieldabilityAudit => _definition.YieldabilityAudit;
    public VmExecutionState State => (VmExecutionState)Volatile.Read(ref _state);
    public VmOperationId PendingOperation => new(Volatile.Read(ref _pendingOperation));
    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    public ValueTask<M6CooperativeVmStep> StepAsync(
        VmStepBudget budget,
        CancellationToken cancellationToken = default)
    {
        EnsureEnabled();
        EnsureStepAllowed();
        BeginExecution();
        return ExecuteAsync(budget, cancellationToken, completion: null);
    }

    public ValueTask<M6CooperativeVmStep> ResumeAsync(
        VmCompletion completion,
        VmStepBudget budget,
        CancellationToken cancellationToken = default)
    {
        EnsureEnabled();
        ArgumentNullException.ThrowIfNull(completion);
        EnsureResumeAllowed(completion);
        BeginExecution();
        return ExecuteAsync(budget, cancellationToken, completion);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        lock (_stateGate)
        {
            Volatile.Write(ref _state, (int)VmExecutionState.Cancelled);
            Volatile.Write(ref _pendingOperation, 0);
        }
        while (Volatile.Read(ref _executionInProgress) != 0)
            await Task.Delay(1).ConfigureAwait(false);
        await _host.DisposeAsync().ConfigureAwait(false);
    }

    private async ValueTask<M6CooperativeVmStep> ExecuteAsync(
        VmStepBudget budget,
        CancellationToken cancellationToken,
        VmCompletion? completion)
    {
        try
        {
            if (IsDisposed)
                return CreateObservation(CreateCancelledResult());

            VmStepResult result;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                result = completion is null
                    ? await _host.StepAsync(budget, cancellationToken).ConfigureAwait(false)
                    : await _host.ResumeAsync(completion, budget, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                result = CreateCancelledResult();
            }
            catch (Exception error)
            {
                result = CreateFaultedResult(
                    "m6.cooperative-host-fault",
                    error.GetType().Name);
            }

            if (IsDisposed)
                result = CreateCancelledResult();

            ValidateAndApply(result, budget);
            return CreateObservation(result);
        }
        finally
        {
            Volatile.Write(ref _executionInProgress, 0);
        }
    }

    private void EnsureEnabled()
    {
        ThrowIfDisposed();
        if (!_definition.Identity.Flags.Scheduler)
        {
            throw new InvalidOperationException(
                "The cooperative scheduler experiment is disabled; the legacy host remains the selected route.");
        }
    }

    private void EnsureStepAllowed()
    {
        var state = State;
        if (state is VmExecutionState.WaitingInput or VmExecutionState.WaitingPort)
            throw new InvalidOperationException("The VM is waiting for a completion; use ResumeAsync.");
        if (state is VmExecutionState.Completed or VmExecutionState.Faulted or VmExecutionState.Cancelled)
            throw new InvalidOperationException($"The VM is terminal (state={state}).");
    }

    private void EnsureResumeAllowed(VmCompletion completion)
    {
        ThrowIfDisposed();
        var state = State;
        if (state is not VmExecutionState.WaitingInput and not VmExecutionState.WaitingPort)
            throw new InvalidOperationException($"The VM is not waiting for completion (state={state}).");
        if (completion.Session != Identity.Session)
            throw new InvalidOperationException("Completion generation/session does not match the experiment identity.");
        if (completion.OperationId.Value <= 0 || completion.OperationId.Value != Volatile.Read(ref _pendingOperation))
            throw new InvalidOperationException("Completion operation does not match the pending operation.");
        if (!completion.IsSuccess && completion.Fault is null)
            throw new ArgumentException("A failed completion must carry a VmFault.", nameof(completion));
    }

    private void ValidateAndApply(VmStepResult result, VmStepBudget budget)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.State is VmExecutionState.Created or VmExecutionState.Running)
            throw ContractViolation("m6.vm.internal-state", "Created and Running are not public step states.");
        if (result.InstructionsExecuted > budget.MaxInstructions || result.WorkUnits > budget.MaxWorkUnits)
            throw ContractViolation("m6.vm.budget-exceeded", "The step exceeded its immutable budget.");
        if (result.InstructionsExecuted < 0 || result.WorkUnits < 0)
            throw ContractViolation("m6.vm.negative-budget-use", "Step counters cannot be negative.");

        var expectedStopReason = result.State switch
        {
            VmExecutionState.YieldedBudget => VmStepStopReason.BudgetExhausted,
            VmExecutionState.WaitingInput or VmExecutionState.WaitingPort => VmStepStopReason.Waiting,
            VmExecutionState.Completed => VmStepStopReason.Completed,
            VmExecutionState.Faulted => VmStepStopReason.Faulted,
            VmExecutionState.Cancelled => VmStepStopReason.Cancelled,
            _ => result.StopReason,
        };
        if (result.StopReason != expectedStopReason)
            throw ContractViolation("m6.vm.stop-reason", "Step state and stop reason do not agree.");
        if (result.State == VmExecutionState.Faulted && result.Fault is null)
            throw ContractViolation("m6.vm.fault-missing", "Faulted steps require a typed fault.");
        if (result.State != VmExecutionState.Faulted && result.Fault is not null)
            throw ContractViolation("m6.vm.unexpected-fault", "Only faulted steps may carry a typed fault.");

        VmEffect? waitEffect = null;
        var waitCount = 0;
        VmEffect? previous = null;
        var sequence = Volatile.Read(ref _lastEffectSequence);
        foreach (var effect in result.Effects)
        {
            if (effect is null)
                throw ContractViolation("m6.vm.effect-null", "Step effects cannot contain null.");
            if (effect.Sequence <= sequence || (previous is not null && effect.Sequence <= previous.Sequence))
                throw ContractViolation("m6.vm.effect-order", "Effect sequence must increase across all steps.");
            sequence = effect.Sequence;
            previous = effect;
            if (effect.CompletionMode == VmCompletionMode.WaitPort)
            {
                waitEffect = effect;
                waitCount++;
            }
        }

        if (result.State is VmExecutionState.WaitingInput or VmExecutionState.WaitingPort)
        {
            if (waitCount != 1 || waitEffect is null)
                throw ContractViolation("m6.vm.wait-effect-count", "A waiting step requires exactly one wait effect.");
            if (result.State == VmExecutionState.WaitingInput && waitEffect is not VmInputEffect)
                throw ContractViolation("m6.vm.wait-effect-kind", "WaitingInput requires VmInputEffect.");
            if (result.State == VmExecutionState.WaitingPort && waitEffect is not VmPortEffect and not VmApplicationEffect)
                throw ContractViolation("m6.vm.wait-effect-kind", "WaitingPort requires a port/application effect.");
        }
        else if (waitCount != 0)
        {
            throw ContractViolation("m6.vm.wait-state-missing", "A wait effect requires a waiting state.");
        }

        var pending = waitEffect switch
        {
            VmInputEffect input => input.OperationId.Value,
            VmPortEffect port => port.OperationId.Value,
            VmApplicationEffect application => application.OperationId.Value,
            _ => 0,
        };
        Volatile.Write(ref _lastEffectSequence, sequence);
        Volatile.Write(ref _pendingOperation, pending);
        Volatile.Write(ref _state, (int)result.State);
    }

    private M6CooperativeVmStep CreateObservation(VmStepResult result)
    {
        var traceOrdinal = Interlocked.Increment(ref _traceOrdinal);
        return new M6CooperativeVmStep(Identity, traceOrdinal, result);
    }

    private void BeginExecution()
    {
        if (Interlocked.CompareExchange(ref _executionInProgress, 1, 0) != 0)
            throw new InvalidOperationException("Cooperative VM execution is already in progress.");
    }

    private void ThrowIfDisposed()
    {
        if (IsDisposed)
            throw new ObjectDisposedException(nameof(CooperativeVmExperimentRunner));
    }

    private static InvalidOperationException ContractViolation(string code, string message)
    {
        return new InvalidOperationException($"{code}: {message}");
    }

    private static VmStepResult CreateCancelledResult()
    {
        return new VmStepResult(
            VmExecutionState.Cancelled,
            VmStepStopReason.Cancelled,
            0,
            0,
            Array.Empty<VmEffect>());
    }

    private static VmStepResult CreateFaultedResult(string code, string detail)
    {
        return new VmStepResult(
            VmExecutionState.Faulted,
            VmStepStopReason.Faulted,
            0,
            0,
            Array.Empty<VmEffect>(),
            new VmFault(code, $"Cooperative VM host failed: {detail}"));
    }
}
