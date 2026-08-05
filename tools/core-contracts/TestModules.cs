using GEmuera.Core.Compatibility;
using GEmuera.Core.Runtime;
using GEmuera.Core.Session;

namespace CoreContractSmoke;

internal sealed class TestDialectModule : IDialectModule
{
    public TestDialectModule(
        DialectModuleDefinition definition,
        IReadOnlyList<IDialectContribution> contributions)
    {
        Definition = definition;
        Contributions = contributions;
    }

    public DialectModuleDefinition Definition { get; }
    public IReadOnlyList<IDialectContribution> Contributions { get; }
}

internal sealed class TestInstructionContribution : IInstructionContribution
{
    private readonly InstructionDescriptor _descriptor;

    public TestInstructionContribution(string contributionId, InstructionDescriptor descriptor)
    {
        ContributionId = contributionId;
        _descriptor = descriptor;
    }

    public string ContributionId { get; }

    public void Apply(InstructionRegistryBuilder builder)
    {
        builder.Register(_descriptor);
    }
}

internal sealed class TestFunctionContribution : IFunctionContribution
{
    private readonly FunctionDescriptor _descriptor;

    public TestFunctionContribution(string contributionId, FunctionDescriptor descriptor)
    {
        ContributionId = contributionId;
        _descriptor = descriptor;
    }

    public string ContributionId { get; }

    public void Apply(FunctionRegistryBuilder builder)
    {
        builder.Register(_descriptor);
    }
}

internal sealed class TestInterpreterFactory : IErbInterpreterFactory
{
    public TestInterpreterFactory(string engineId)
        : this(InterpreterTestDescriptors.V24(engineId))
    {
    }

    public TestInterpreterFactory(ErbInterpreterDescriptor descriptor)
    {
        Descriptor = descriptor;
        EngineId = descriptor.EngineId;
    }

    public string EngineId { get; }
    public ErbInterpreterDescriptor Descriptor { get; }

    public IErbInterpreterHost Create(
        ErbInterpreterContext context,
        ErbInterpreterDescriptor registeredDescriptor)
    {
        return new TestInterpreterHost(registeredDescriptor, context.Compatibility);
    }
}

internal sealed class MutableDescriptorInterpreterFactory : IErbInterpreterFactory
{
    private ErbInterpreterDescriptor _descriptor;

    public MutableDescriptorInterpreterFactory(ErbInterpreterDescriptor descriptor)
    {
        _descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        EngineId = descriptor.EngineId;
    }

    public string EngineId { get; }
    public ErbInterpreterDescriptor Descriptor => _descriptor;

    public void ReplaceDescriptor(ErbInterpreterDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (!string.Equals(descriptor.EngineId, EngineId, StringComparison.Ordinal))
            throw new ArgumentException("Replacement descriptor must retain the factory engine id.", nameof(descriptor));
        _descriptor = descriptor;
    }

    public IErbInterpreterHost Create(
        ErbInterpreterContext context,
        ErbInterpreterDescriptor registeredDescriptor)
    {
        return new TestInterpreterHost(registeredDescriptor, context.Compatibility);
    }
}

/// <summary>
/// Deliberately ignores the descriptor selected by the frozen catalog. This
/// verifies that a trusted factory cannot silently return a host for another
/// engine version after registration.
/// </summary>
internal sealed class DescriptorIgnoringTestInterpreterFactory : IErbInterpreterFactory
{
    private readonly ErbInterpreterDescriptor _createdDescriptor;

    public DescriptorIgnoringTestInterpreterFactory(
        ErbInterpreterDescriptor descriptor,
        ErbInterpreterDescriptor createdDescriptor)
    {
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        _createdDescriptor = createdDescriptor ?? throw new ArgumentNullException(nameof(createdDescriptor));
        EngineId = Descriptor.EngineId;
    }

    public string EngineId { get; }
    public ErbInterpreterDescriptor Descriptor { get; }

    public IErbInterpreterHost Create(
        ErbInterpreterContext context,
        ErbInterpreterDescriptor registeredDescriptor)
    {
        return new TestInterpreterHost(_createdDescriptor, context.Compatibility);
    }
}

internal sealed class TestInterpreterHost : IErbInterpreterHost
{
    public TestInterpreterHost(
        ErbInterpreterDescriptor descriptor,
        CompatibilityPlan compatibility)
    {
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
        Compatibility = compatibility ?? throw new ArgumentNullException(nameof(compatibility));
    }

    public ErbInterpreterDescriptor Descriptor { get; }
    public CompatibilityPlan Compatibility { get; }
    public VmExecutionState State => VmExecutionState.Created;

    public ValueTask<VmStepResult> StepAsync(VmStepBudget budget, CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(new VmStepResult(
            VmExecutionState.YieldedBudget,
            VmStepStopReason.BudgetExhausted,
            0,
            0,
            Array.Empty<VmEffect>()));
    }

    public ValueTask<VmStepResult> ResumeAsync(VmCompletion completion, VmStepBudget budget, CancellationToken cancellationToken = default)
    {
        return StepAsync(budget, cancellationToken);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal enum ResumableHostMode
{
    BudgetThenInputThenComplete,
    InvalidSequence,
    InvalidWaitResult,
    InvalidInternalState,
    InvalidBudget,
    Throwing,
    Blocking,
}

internal sealed class ResumableTestInterpreterHost : ResumableErbInterpreterHost
{
    private readonly ResumableHostMode _mode;
    private int _step;

    public TaskCompletionSource<bool> ExecutionEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource<bool> ExecutionRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ResumableTestInterpreterHost(
        ErbInterpreterContext context,
        ErbInterpreterDescriptor descriptor,
        ResumableHostMode mode)
        : base(context, descriptor)
    {
        _mode = mode;
    }

    protected override async ValueTask<VmStepResult> ExecuteStepAsync(
        VmStepBudget budget,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_mode == ResumableHostMode.Blocking)
        {
            ExecutionEntered.TrySetResult(true);
            await ExecutionRelease.Task.WaitAsync(cancellationToken);
            return new VmStepResult(
                VmExecutionState.YieldedBudget,
                VmStepStopReason.BudgetExhausted,
                1,
                1,
                Array.Empty<VmEffect>());
        }
        _step++;
        return _mode switch
        {
            ResumableHostMode.BudgetThenInputThenComplete when _step == 1 =>
                new VmStepResult(
                    VmExecutionState.YieldedBudget,
                    VmStepStopReason.BudgetExhausted,
                    1,
                    1,
                    Array.Empty<VmEffect>()),
            ResumableHostMode.BudgetThenInputThenComplete when _step == 2 =>
                new VmStepResult(
                    VmExecutionState.WaitingInput,
                    VmStepStopReason.Waiting,
                    1,
                    1,
                    new VmEffect[] { new VmInputEffect(1, new VmOperationId(7), "string") }),
            ResumableHostMode.BudgetThenInputThenComplete =>
                new VmStepResult(
                    VmExecutionState.Completed,
                    VmStepStopReason.Completed,
                    1,
                    1,
                    new VmEffect[] { new VmDisplayEffect(2, "display:1") }),
            ResumableHostMode.InvalidSequence =>
                new VmStepResult(
                    VmExecutionState.YieldedBudget,
                    VmStepStopReason.BudgetExhausted,
                    1,
                    1,
                    new VmEffect[] { new VmDisplayEffect(1, "duplicate") }),
            ResumableHostMode.InvalidWaitResult =>
                new VmStepResult(
                    VmExecutionState.WaitingInput,
                    VmStepStopReason.Waiting,
                    1,
                    1,
                    new VmEffect[] { new VmDisplayEffect(1, "missing-input") }),
            ResumableHostMode.InvalidInternalState =>
                new VmStepResult(
                    VmExecutionState.Running,
                    VmStepStopReason.BudgetExhausted,
                    1,
                    1,
                    Array.Empty<VmEffect>()),
            ResumableHostMode.InvalidBudget =>
                new VmStepResult(
                    VmExecutionState.YieldedBudget,
                    VmStepStopReason.BudgetExhausted,
                    budget.MaxInstructions + 1,
                    budget.MaxWorkUnits + 1,
                    Array.Empty<VmEffect>()),
            ResumableHostMode.Throwing => throw new InvalidOperationException("Synthetic engine failure."),
            _ => throw new InvalidOperationException("Unsupported test host mode."),
        };
    }

    protected override ValueTask<VmStepResult> ExecuteResumeAsync(
        VmCompletion completion,
        VmStepBudget budget,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!completion.IsSuccess)
        {
            return ValueTask.FromResult(new VmStepResult(
                VmExecutionState.Faulted,
                VmStepStopReason.Faulted,
                0,
                0,
                Array.Empty<VmEffect>(),
                completion.Fault));
        }

        _step = 2;
        return ExecuteStepAsync(budget, cancellationToken);
    }
}

internal sealed class ResumableTestInterpreterFactory : IErbInterpreterFactory
{
    private readonly ResumableHostMode _mode;

    public ResumableTestInterpreterFactory(string engineId, ResumableHostMode mode)
    {
        Descriptor = InterpreterTestDescriptors.V24(engineId);
        EngineId = Descriptor.EngineId;
        _mode = mode;
    }

    public string EngineId { get; }
    public ErbInterpreterDescriptor Descriptor { get; }

    public IErbInterpreterHost Create(
        ErbInterpreterContext context,
        ErbInterpreterDescriptor registeredDescriptor)
    {
        return new ResumableTestInterpreterHost(context, registeredDescriptor, _mode);
    }
}

internal static class InterpreterTestDescriptors
{
    public static ErbInterpreterDescriptor V24(string engineId, string engineVersion = "1.0.0")
    {
        return new ErbInterpreterDescriptor(
            engineId,
            engineVersion,
            1,
            new[] { new ErbInterpreterModuleSupport("gemuera.v24", "[1.0.0,2.0.0)") },
            new[] { "gemuera.v24" });
    }

    public static ErbInterpreterDescriptor Snake(string engineId, string engineVersion)
    {
        return new ErbInterpreterDescriptor(
            engineId,
            engineVersion,
            1,
            new[]
            {
                new ErbInterpreterModuleSupport("gemuera.v24", "[1.0.0,2.0.0)"),
                new ErbInterpreterModuleSupport("game.snake", "[1.0.0,2.0.0)"),
            },
            new[] { "gemuera.v24", "game.snake" });
    }
}

internal sealed class TestLegacyBackend : ILegacySessionBackend
{
    private readonly List<string> _events = new();

    public bool IsRunning { get; private set; }
    public IReadOnlyList<string> Events => _events;
    public bool FailNextStart { get; set; }
    public bool IgnoreStartCancellation { get; set; }
    public TaskCompletionSource<bool>? StartGate { get; set; }
    public TaskCompletionSource<bool>? StartEntered { get; set; }

    public async ValueTask StartAsync(SessionSelection selection, CompatibilityPlan compatibility, SessionStamp stamp, CancellationToken cancellationToken = default)
    {
        if (!IgnoreStartCancellation)
            cancellationToken.ThrowIfCancellationRequested();

        var gate = StartGate;
        if (gate is not null)
        {
            StartEntered?.TrySetResult(true);
            if (IgnoreStartCancellation)
                await gate.Task;
            else
                await gate.Task.WaitAsync(cancellationToken);
        }

        if (!IgnoreStartCancellation)
            cancellationToken.ThrowIfCancellationRequested();
        if (FailNextStart)
        {
            FailNextStart = false;
            throw new InvalidOperationException("Configured legacy backend start failure.");
        }
        if (IsRunning)
            throw new InvalidOperationException("Legacy backend is already running.");
        IsRunning = true;
        _events.Add($"start:{selection.GameId}:{compatibility.ProfileId}:{stamp.Generation.Value}");
        return;
    }

    public ValueTask StopAsync(SessionStamp stamp, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (IsRunning)
            _events.Add($"stop:{stamp.Generation.Value}");
        IsRunning = false;
        return ValueTask.CompletedTask;
    }
}
