using System.Security.Cryptography;
using System.Text;
using GEmuera.Core.Compatibility;
using GEmuera.Core.Session;
using static GEmuera.Core.Runtime.VmEffectContract;

namespace GEmuera.Core.Runtime;

public enum VmCompletionMode
{
    CoreImmediate,
    CommitThenProject,
    FireAndContinue,
    WaitPort,
    VmThreadBlockingBounded,
}

public enum VmExecutionState
{
    Created,
    Running,
    YieldedBudget,
    WaitingInput,
    WaitingPort,
    Completed,
    Faulted,
    Cancelled,
}

public enum VmStepStopReason
{
    BudgetExhausted,
    Waiting,
    Completed,
    Faulted,
    Cancelled,
}

public readonly record struct VmStepBudget
{
    public VmStepBudget(int maxInstructions, int maxWorkUnits)
    {
        if (maxInstructions <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxInstructions));
        if (maxWorkUnits <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxWorkUnits));
        MaxInstructions = maxInstructions;
        MaxWorkUnits = maxWorkUnits;
    }

    public int MaxInstructions { get; }
    public int MaxWorkUnits { get; }
}

public readonly record struct VmOperationId(long Value)
{
    public static VmOperationId None => new(0);
}

public sealed record VmFault(string Code, string Message, string? SourcePath = null, int? Line = null)
{
    public string Code { get; } = Required(Code, nameof(Code));
    public string Message { get; } = Required(Message, nameof(Message));

    private static string Required(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value must not be empty.", parameterName);
        return value.Trim();
    }
}

public abstract record VmEffect(long Sequence, VmCompletionMode CompletionMode)
{
    public long Sequence { get; } = Sequence > 0 ? Sequence : throw new ArgumentOutOfRangeException(nameof(Sequence));
}

public sealed record VmDisplayEffect : VmEffect
{
    public VmDisplayEffect(long sequence, string displayRevision)
        : base(sequence, VmCompletionMode.CommitThenProject)
    {
        DisplayRevision = RequiredText(displayRevision, nameof(displayRevision));
    }

    public string DisplayRevision { get; }
}

public sealed record VmInputEffect : VmEffect
{
    public VmInputEffect(long sequence, VmOperationId operationId, string inputKind)
        : base(sequence, VmCompletionMode.WaitPort)
    {
        OperationId = RequiredOperation(operationId, nameof(operationId));
        InputKind = RequiredText(inputKind, nameof(inputKind));
    }

    public VmOperationId OperationId { get; }
    public string InputKind { get; }
}

public sealed record VmPortEffect : VmEffect
{
    public VmPortEffect(long sequence, VmOperationId operationId, string portTypeId)
        : base(sequence, VmCompletionMode.WaitPort)
    {
        OperationId = RequiredOperation(operationId, nameof(operationId));
        PortTypeId = RequiredText(portTypeId, nameof(portTypeId));
    }

    public VmOperationId OperationId { get; }
    public string PortTypeId { get; }
}

public sealed record VmApplicationEffect : VmEffect
{
    public VmApplicationEffect(long sequence, VmOperationId operationId, string command)
        : base(sequence, VmCompletionMode.WaitPort)
    {
        OperationId = RequiredOperation(operationId, nameof(operationId));
        Command = RequiredText(command, nameof(command));
    }

    // Kept for the initial contract slice. New engines should always provide
    // the operation id explicitly so completions can be matched safely.
    public VmApplicationEffect(long sequence, string command)
        : this(sequence, new VmOperationId(sequence), command)
    {
    }

    public VmOperationId OperationId { get; }
    public string Command { get; }
}

internal static class VmEffectContract
{
    public static string RequiredText(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value must not be empty.", parameterName);
        return value.Trim();
    }

    public static VmOperationId RequiredOperation(VmOperationId operationId, string parameterName)
    {
        if (operationId.Value <= 0)
            throw new ArgumentOutOfRangeException(parameterName, "Operation id must be positive.");
        return operationId;
    }
}

public sealed record VmStepResult
{
    private static readonly IReadOnlyList<VmEffect> EmptyEffects = Array.Empty<VmEffect>();

    public VmStepResult(
        VmExecutionState state,
        VmStepStopReason stopReason,
        int instructionsExecuted,
        int workUnits,
        IReadOnlyList<VmEffect>? effects,
        VmFault? fault = null)
    {
        if (instructionsExecuted < 0)
            throw new ArgumentOutOfRangeException(nameof(instructionsExecuted));
        if (workUnits < 0)
            throw new ArgumentOutOfRangeException(nameof(workUnits));

        State = state;
        StopReason = stopReason;
        InstructionsExecuted = instructionsExecuted;
        WorkUnits = workUnits;
        Effects = CopyEffects(effects);
        Fault = fault;
    }

    public VmExecutionState State { get; }
    public VmStepStopReason StopReason { get; }
    public int InstructionsExecuted { get; }
    public int WorkUnits { get; }
    public IReadOnlyList<VmEffect> Effects { get; }
    public VmFault? Fault { get; }

    private static IReadOnlyList<VmEffect> CopyEffects(IReadOnlyList<VmEffect>? effects)
    {
        if (effects is null || effects.Count == 0)
            return EmptyEffects;

        var copy = new VmEffect[effects.Count];
        for (var index = 0; index < effects.Count; index++)
            copy[index] = effects[index] ?? throw new ArgumentException("Effects cannot contain null.", nameof(effects));
        return Array.AsReadOnly(copy);
    }
}

public sealed record VmCompletion(
    SessionStamp Session,
    VmOperationId OperationId,
    bool IsSuccess,
    string? Value,
    VmFault? Fault = null);

public sealed class ErbInterpreterModuleSupport
{
    public ErbInterpreterModuleSupport(string moduleId, string versionRange)
    {
        ModuleId = InterpreterContractText.RequiredIdentifier(moduleId, nameof(moduleId));
        VersionRange = InterpreterContractText.RequiredVersionRange(versionRange, nameof(versionRange));
    }

    public string ModuleId { get; }
    public string VersionRange { get; }
}

/// <summary>
/// Exact, reproducible interpreter selection identity. Engine id alone is not
/// sufficient because a game session must not silently move to a different
/// interpreter version when more than one trusted built-in is available.
/// </summary>
public sealed record ErbInterpreterKey
{
    public ErbInterpreterKey(string engineId, string engineVersion)
    {
        EngineId = InterpreterContractText.RequiredEngineId(engineId, nameof(engineId));
        EngineVersion = InterpreterContractText.RequiredSemanticVersion(engineVersion, nameof(engineVersion));
    }

    public string EngineId { get; }
    public string EngineVersion { get; }
    public string CanonicalId => EngineId + "@" + EngineVersion;

    public override string ToString() => CanonicalId;
}

public sealed class ErbInterpreterDescriptor
{
    private readonly IReadOnlyList<ErbInterpreterModuleSupport> _moduleSupport;
    private readonly IReadOnlyList<string> _requiredModuleIds;

    public ErbInterpreterDescriptor(
        string engineId,
        string engineVersion,
        int apiVersion,
        IEnumerable<ErbInterpreterModuleSupport> moduleSupport,
        IEnumerable<string> requiredModuleIds)
    {
        EngineId = InterpreterContractText.RequiredEngineId(engineId, nameof(engineId));
        EngineVersion = InterpreterContractText.RequiredSemanticVersion(engineVersion, nameof(engineVersion));
        Key = new ErbInterpreterKey(EngineId, EngineVersion);
        if (apiVersion <= 0)
            throw new ArgumentOutOfRangeException(nameof(apiVersion));
        ArgumentNullException.ThrowIfNull(moduleSupport);
        ArgumentNullException.ThrowIfNull(requiredModuleIds);
        _moduleSupport = CopyUniqueSorted(moduleSupport, item => item.ModuleId, nameof(moduleSupport));
        _requiredModuleIds = InterpreterContractText.UniqueSortedIdentifiers(requiredModuleIds, nameof(requiredModuleIds));
        if (_moduleSupport.Count == 0)
            throw new ArgumentException("An interpreter must declare at least one supported module.", nameof(moduleSupport));
        if (_requiredModuleIds.Count == 0)
            throw new ArgumentException("An interpreter must declare at least one required module id.", nameof(requiredModuleIds));
        if (_requiredModuleIds.Any(required => !_moduleSupport.Any(supported => supported.ModuleId == required)))
            throw new ArgumentException("Required module ids must be included in module support.", nameof(requiredModuleIds));
        ApiVersion = apiVersion;
        CanonicalHash = ComputeCanonicalHash();
    }

    public string EngineId { get; }
    public string EngineVersion { get; }
    public ErbInterpreterKey Key { get; }
    public int ApiVersion { get; }
    public IReadOnlyList<ErbInterpreterModuleSupport> ModuleSupport => _moduleSupport;
    public IReadOnlyList<string> RequiredModuleIds => _requiredModuleIds;
    public string CanonicalHash { get; }

    public bool Supports(CompatibilityPlan compatibility)
    {
        ArgumentNullException.ThrowIfNull(compatibility);
        var selectedModules = compatibility.Dialect.Modules;
        if (_requiredModuleIds.Any(required => !selectedModules.Any(module => module.ModuleId == required)))
            return false;
        foreach (var module in selectedModules)
        {
            var support = _moduleSupport.SingleOrDefault(item => item.ModuleId == module.ModuleId);
            if (support is null || !SemanticVersionRange.Contains(support.VersionRange, module.ModuleVersion))
                return false;
        }
        return true;
    }

    private string ComputeCanonicalHash()
    {
        var canonical = new StringBuilder()
            .Append("engine=").Append(EngineId).Append('\n')
            .Append("version=").Append(EngineVersion).Append('\n')
            .Append("api=").Append(ApiVersion).Append('\n');
        foreach (var module in _moduleSupport)
            canonical.Append("support=").Append(module.ModuleId).Append('|').Append(module.VersionRange).Append('\n');
        foreach (var moduleId in _requiredModuleIds)
            canonical.Append("required-module=").Append(moduleId).Append('\n');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()))).ToLowerInvariant();
    }

    private static IReadOnlyList<T> CopyUniqueSorted<T>(IEnumerable<T> values, Func<T, string> keySelector, string parameterName)
    {
        var copy = values.ToArray();
        if (copy.Any(value => value is null))
            throw new ArgumentException("Collection contains null.", parameterName);
        var duplicate = copy.GroupBy(keySelector, StringComparer.Ordinal).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new ArgumentException($"Duplicate collection key: {duplicate.Key}", parameterName);
        Array.Sort(copy, (left, right) => StringComparer.Ordinal.Compare(keySelector(left), keySelector(right)));
        return Array.AsReadOnly(copy);
    }
}

public interface IErbInterpreterFactory
{
    string EngineId { get; }
    ErbInterpreterDescriptor Descriptor { get; }

    /// <summary>
    /// Creates a host for the descriptor snapshot selected by a frozen catalog.
    /// Implementations must construct the host from <paramref name="registeredDescriptor"/>,
    /// rather than re-reading mutable registration state.
    /// </summary>
    IErbInterpreterHost Create(
        ErbInterpreterContext context,
        ErbInterpreterDescriptor registeredDescriptor);
}

/// <summary>
/// Read-only runtime view of a frozen, explicitly registered interpreter set.
/// Runtime consumers receive this view instead of a mutable registration
/// surface, so adding a new built-in cannot alter an existing session's
/// selection rules.
/// </summary>
public interface IErbInterpreterCatalog
{
    IReadOnlyList<ErbInterpreterDescriptor> Descriptors { get; }

    IErbInterpreterHost Create(
        ErbInterpreterKey engine,
        ErbInterpreterContext context);

    IErbInterpreterHost Create(
        string engineId,
        string engineVersion,
        ErbInterpreterContext context);
}

/// <summary>
/// Explicit engine selection point for built-in interpreters. This is an
/// application-owned catalog, not assembly scanning or process-wide semantics.
/// </summary>
public sealed class ErbInterpreterCatalog
{
    private readonly Dictionary<string, InterpreterRegistration> _registrations = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private FrozenErbInterpreterCatalog? _frozenCatalog;

    public bool IsFrozen
    {
        get
        {
            lock (_gate)
                return _frozenCatalog is not null;
        }
    }

    public void Register(IErbInterpreterFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        var engineId = InterpreterContractText.RequiredEngineId(factory.EngineId, nameof(factory));
        var descriptor = factory.Descriptor ?? throw new InvalidOperationException("Interpreter factory returned a null descriptor.");
        if (!string.Equals(descriptor.EngineId, engineId, StringComparison.Ordinal))
            throw new InvalidOperationException("Interpreter factory engine id does not match its descriptor.");
        var key = descriptor.Key;
        lock (_gate)
        {
            if (_frozenCatalog is not null)
                throw new InvalidOperationException("ERB interpreter catalog is frozen.");
            if (!_registrations.TryAdd(key.CanonicalId, new InterpreterRegistration(factory, descriptor)))
                throw new InvalidOperationException($"Duplicate ERB interpreter engine/version: {key}.");
        }
    }

    /// <summary>
    /// Freezes registration and returns the only catalog surface that should be
    /// passed to a session/runtime. Repeated calls return the same immutable
    /// view, preserving an exact descriptor snapshot.
    /// </summary>
    public IErbInterpreterCatalog Freeze()
    {
        lock (_gate)
        {
            if (_frozenCatalog is not null)
                return _frozenCatalog;

            var snapshot = _registrations.Values.ToArray();
            Array.Sort(snapshot, CompareRegistrations);
            _frozenCatalog = new FrozenErbInterpreterCatalog(snapshot);
            return _frozenCatalog;
        }
    }

    public IErbInterpreterHost Create(
        ErbInterpreterKey engine,
        ErbInterpreterContext context)
    {
        return GetFrozenCatalog().Create(engine, context);
    }

    public IErbInterpreterHost Create(
        string engineId,
        string engineVersion,
        ErbInterpreterContext context)
    {
        return GetFrozenCatalog().Create(engineId, engineVersion, context);
    }

    /// <summary>
    /// Compatibility convenience for a catalog containing exactly one version
    /// of an engine. New runtime code should select <see cref="ErbInterpreterKey"/>
    /// explicitly; an ambiguous engine id fails fast rather than choosing a
    /// version by registration order.
    /// </summary>
    public IErbInterpreterHost Create(
        string engineId,
        ErbInterpreterContext context)
    {
        var normalizedEngineId = InterpreterContractText.RequiredEngineId(engineId, nameof(engineId));
        return GetFrozenCatalog().CreateSingleVersion(normalizedEngineId, context);
    }

    private FrozenErbInterpreterCatalog GetFrozenCatalog()
    {
        lock (_gate)
        {
            if (_frozenCatalog is null)
                throw new InvalidOperationException("ERB interpreter catalog must be frozen before creating an interpreter.");
            return _frozenCatalog;
        }
    }

    private static int CompareRegistrations(InterpreterRegistration left, InterpreterRegistration right)
    {
        var engineComparison = StringComparer.Ordinal.Compare(left.Descriptor.EngineId, right.Descriptor.EngineId);
        return engineComparison != 0
            ? engineComparison
            : StringComparer.Ordinal.Compare(left.Descriptor.EngineVersion, right.Descriptor.EngineVersion);
    }

    private sealed record InterpreterRegistration(
        IErbInterpreterFactory Factory,
        ErbInterpreterDescriptor Descriptor);

    private sealed class FrozenErbInterpreterCatalog : IErbInterpreterCatalog
    {
        private readonly Dictionary<string, InterpreterRegistration> _registrationsByKey = new(StringComparer.Ordinal);
        private readonly Dictionary<string, IReadOnlyList<InterpreterRegistration>> _registrationsByEngineId = new(StringComparer.Ordinal);

        public FrozenErbInterpreterCatalog(IReadOnlyList<InterpreterRegistration> registrations)
        {
            ArgumentNullException.ThrowIfNull(registrations);
            var descriptors = new ErbInterpreterDescriptor[registrations.Count];
            var versionsByEngineId = new Dictionary<string, List<InterpreterRegistration>>(StringComparer.Ordinal);
            for (var index = 0; index < registrations.Count; index++)
            {
                var registration = registrations[index];
                var key = registration.Descriptor.Key;
                if (!_registrationsByKey.TryAdd(key.CanonicalId, registration))
                    throw new InvalidOperationException($"Duplicate frozen ERB interpreter engine/version: {key}.");
                if (!versionsByEngineId.TryGetValue(key.EngineId, out var versions))
                {
                    versions = new List<InterpreterRegistration>();
                    versionsByEngineId.Add(key.EngineId, versions);
                }
                versions.Add(registration);
                descriptors[index] = registration.Descriptor;
            }

            foreach (var pair in versionsByEngineId)
                _registrationsByEngineId.Add(pair.Key, Array.AsReadOnly(pair.Value.ToArray()));
            Descriptors = Array.AsReadOnly(descriptors);
        }

        public IReadOnlyList<ErbInterpreterDescriptor> Descriptors { get; }

        public IErbInterpreterHost Create(
            ErbInterpreterKey engine,
            ErbInterpreterContext context)
        {
            ArgumentNullException.ThrowIfNull(engine);
            ArgumentNullException.ThrowIfNull(context);
            if (!_registrationsByKey.TryGetValue(engine.CanonicalId, out var registration))
                throw new InvalidOperationException($"ERB interpreter engine/version '{engine}' was not registered.");
            return CreateRegistration(registration, context);
        }

        public IErbInterpreterHost Create(
            string engineId,
            string engineVersion,
            ErbInterpreterContext context)
        {
            return Create(new ErbInterpreterKey(engineId, engineVersion), context);
        }

        public IErbInterpreterHost CreateSingleVersion(
            string engineId,
            ErbInterpreterContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            if (!_registrationsByEngineId.TryGetValue(engineId, out var registrations))
                throw new InvalidOperationException($"ERB interpreter engine '{engineId}' was not registered.");
            if (registrations.Count != 1)
                throw new InvalidOperationException($"ERB interpreter engine '{engineId}' has multiple registered versions; select an exact engine version.");
            return CreateRegistration(registrations[0], context);
        }

        private static IErbInterpreterHost CreateRegistration(
            InterpreterRegistration registration,
            ErbInterpreterContext context)
        {
            if (!registration.Descriptor.Supports(context.Compatibility))
            {
                throw new InvalidOperationException(
                    $"ERB interpreter '{registration.Descriptor.Key}' does not support compatibility plan '{context.Compatibility.ProfileId}'.");
            }

            var interpreter = registration.Factory.Create(context, registration.Descriptor);
            if (interpreter is null)
                throw new InvalidOperationException($"ERB interpreter factory '{registration.Descriptor.Key}' returned null.");
            if (!ReferenceEquals(interpreter.Compatibility, context.Compatibility))
                throw new InvalidOperationException("Interpreter was created with a different CompatibilityPlan instance.");
            var hostDescriptor = interpreter.Descriptor;
            if (hostDescriptor is null ||
                hostDescriptor.Key != registration.Descriptor.Key ||
                !string.Equals(
                    hostDescriptor.CanonicalHash,
                    registration.Descriptor.CanonicalHash,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"ERB interpreter factory '{registration.Descriptor.Key}' returned a host whose descriptor does not match the frozen registration.");
            }
            return interpreter;
        }
    }
}

public sealed class ErbInterpreterContext
{
    public ErbInterpreterContext(
        CompatibilityPlan compatibility,
        string sourceIdentity,
        SessionStamp session = default)
    {
        Compatibility = compatibility ?? throw new ArgumentNullException(nameof(compatibility));
        SourceIdentity = string.IsNullOrWhiteSpace(sourceIdentity)
            ? throw new ArgumentException("Source identity must not be empty.", nameof(sourceIdentity))
            : sourceIdentity.Trim();
        Session = session;
    }

    public CompatibilityPlan Compatibility { get; }
    public string SourceIdentity { get; }
    public SessionStamp Session { get; }
}

/// <summary>
/// Stable boundary for both the legacy adapter and a future resumable VM.
/// Implementations own mutable VM state; callers only exchange immutable DTOs.
/// </summary>
public interface IErbInterpreterHost : IAsyncDisposable
{
    /// <summary>
    /// Immutable interpreter identity supplied by the frozen catalog at host
    /// construction. The catalog validates it before exposing the host.
    /// </summary>
    ErbInterpreterDescriptor Descriptor { get; }
    CompatibilityPlan Compatibility { get; }
    VmExecutionState State { get; }
    ValueTask<VmStepResult> StepAsync(VmStepBudget budget, CancellationToken cancellationToken = default);
    ValueTask<VmStepResult> ResumeAsync(VmCompletion completion, VmStepBudget budget, CancellationToken cancellationToken = default);
}

/// <summary>
/// Reusable state machine for future ERB engines. Dialect implementations only
/// provide instruction execution; this class owns step/resume legality,
/// generation checks, effect ordering and terminal-state handling.
/// </summary>
public abstract class ResumableErbInterpreterHost : IErbInterpreterHost
{
    private long _lastEffectSequence;
    private VmOperationId _pendingOperation = VmOperationId.None;
    private int _disposed;
    private int _executionInProgress;

    protected ResumableErbInterpreterHost(
        ErbInterpreterContext context,
        ErbInterpreterDescriptor descriptor)
    {
        Context = context ?? throw new ArgumentNullException(nameof(context));
        Descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
    }

    protected ErbInterpreterContext Context { get; }
    protected SessionStamp Session => Context.Session;
    protected VmOperationId PendingOperation => _pendingOperation;

    public ErbInterpreterDescriptor Descriptor { get; }
    public CompatibilityPlan Compatibility => Context.Compatibility;
    public VmExecutionState State { get; private set; } = VmExecutionState.Created;

    public ValueTask<VmStepResult> StepAsync(
        VmStepBudget budget,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        EnsureStepAllowed();
        BeginExecution();
        return ExecuteAndApplyAsync(
            budget,
            cancellationToken,
            resume: null);
    }

    public ValueTask<VmStepResult> ResumeAsync(
        VmCompletion completion,
        VmStepBudget budget,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(completion);
        if (State is not VmExecutionState.WaitingInput and not VmExecutionState.WaitingPort)
            throw new InvalidOperationException($"Interpreter is not waiting for completion (state={State}).");
        if (completion.Session != Session)
            throw new InvalidOperationException("Completion belongs to a different session generation or operation.");
        if (completion.OperationId != _pendingOperation || completion.OperationId == VmOperationId.None)
            throw new InvalidOperationException("Completion operation does not match the pending VM request.");
        if (!completion.IsSuccess && completion.Fault is null)
            throw new ArgumentException("A failed completion must carry a VmFault.", nameof(completion));

        BeginExecution();
        return ExecuteAndApplyAsync(budget, cancellationToken, completion);
    }

    protected abstract ValueTask<VmStepResult> ExecuteStepAsync(
        VmStepBudget budget,
        CancellationToken cancellationToken);

    protected virtual ValueTask<VmStepResult> ExecuteResumeAsync(
        VmCompletion completion,
        VmStepBudget budget,
        CancellationToken cancellationToken)
    {
        return ExecuteStepAsync(budget, cancellationToken);
    }

    public virtual ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _pendingOperation = VmOperationId.None;
            State = VmExecutionState.Cancelled;
        }
        return ValueTask.CompletedTask;
    }

    private async ValueTask<VmStepResult> ExecuteAndApplyAsync(
        VmStepBudget budget,
        CancellationToken cancellationToken,
        VmCompletion? resume)
    {
        try
        {
            if (IsDisposed)
                return CreateCancelledResult();
            State = VmExecutionState.Running;
            if (IsDisposed)
                return CreateCancelledResult();
            VmStepResult result;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                result = resume is null
                    ? await ExecuteStepAsync(budget, cancellationToken).ConfigureAwait(false)
                    : await ExecuteResumeAsync(resume, budget, cancellationToken).ConfigureAwait(false);
                if (result is null)
                    throw new InvalidOperationException("Interpreter execution returned null.");
            }
            catch (OperationCanceledException)
            {
                return CreateCancelledResult();
            }
            catch (VmContractException error)
            {
                if (IsDisposed)
                    return CreateCancelledResult();
                State = VmExecutionState.Faulted;
                return new VmStepResult(
                    VmExecutionState.Faulted,
                    VmStepStopReason.Faulted,
                    0,
                    0,
                    Array.Empty<VmEffect>(),
                    error.Fault);
            }
            catch (Exception)
            {
                if (IsDisposed)
                    return CreateCancelledResult();
                State = VmExecutionState.Faulted;
                _pendingOperation = VmOperationId.None;
                return new VmStepResult(
                    VmExecutionState.Faulted,
                    VmStepStopReason.Faulted,
                    0,
                    0,
                    Array.Empty<VmEffect>(),
                    new VmFault("vm.execution-unhandled", "Interpreter execution failed unexpectedly."));
            }

            try
            {
                if (IsDisposed)
                    return CreateCancelledResult();
                var applied = ApplyResult(result, budget);
                return IsDisposed ? CreateCancelledResult() : applied;
            }
            catch (VmContractException error)
            {
                if (IsDisposed)
                    return CreateCancelledResult();
                State = VmExecutionState.Faulted;
                _pendingOperation = VmOperationId.None;
                return new VmStepResult(
                    VmExecutionState.Faulted,
                    VmStepStopReason.Faulted,
                    result.InstructionsExecuted,
                    result.WorkUnits,
                    Array.Empty<VmEffect>(),
                    error.Fault);
            }
        }
        finally
        {
            Volatile.Write(ref _executionInProgress, 0);
        }
    }

    private VmStepResult ApplyResult(VmStepResult result, VmStepBudget budget)
    {
        var pendingOperation = ValidateResult(result, budget);
        State = result.State;
        _pendingOperation = pendingOperation;

        return result;
    }

    private VmOperationId ValidateResult(VmStepResult result, VmStepBudget budget)
    {
        if (result.State is VmExecutionState.Created or VmExecutionState.Running)
        {
            throw new VmContractException(new VmFault(
                "vm.result-state-invalid",
                "Interpreter execution cannot expose an internal created or running state."));
        }
        if (result.InstructionsExecuted > budget.MaxInstructions || result.WorkUnits > budget.MaxWorkUnits)
        {
            throw new VmContractException(new VmFault(
                "vm.budget-exceeded",
                "Interpreter execution exceeded the requested step budget."));
        }

        var expectedStopReason = result.State switch
        {
            VmExecutionState.YieldedBudget => VmStepStopReason.BudgetExhausted,
            VmExecutionState.WaitingInput or VmExecutionState.WaitingPort => VmStepStopReason.Waiting,
            VmExecutionState.Completed => VmStepStopReason.Completed,
            VmExecutionState.Faulted => VmStepStopReason.Faulted,
            VmExecutionState.Cancelled => VmStepStopReason.Cancelled,
            _ => result.StopReason,
        };
        if (result.State is not VmExecutionState.Running and not VmExecutionState.Created &&
            result.StopReason != expectedStopReason)
        {
            throw new VmContractException(new VmFault(
                "vm.stop-reason-mismatch",
                $"State '{result.State}' requires stop reason '{expectedStopReason}'."));
        }

        if (result.State == VmExecutionState.Faulted && result.Fault is null)
            throw new VmContractException(new VmFault("vm.fault-missing", "Faulted results must carry a VmFault."));
        if (result.State != VmExecutionState.Faulted && result.Fault is not null)
            throw new VmContractException(new VmFault("vm.unexpected-fault", "Only faulted results may carry a VmFault."));

        VmEffect? previous = null;
        VmEffect? waitEffect = null;
        var lastSequence = _lastEffectSequence;
        var waitEffectCount = 0;
        foreach (var effect in result.Effects)
        {
            if (effect is null)
                throw new VmContractException(new VmFault("vm.effect-null", "Execution results cannot contain null effects."));
            if (effect.Sequence <= lastSequence)
                throw new VmContractException(new VmFault("vm.effect-sequence", "Effect sequence must increase across resumptions."));
            if (previous is not null && effect.Sequence <= previous.Sequence)
                throw new VmContractException(new VmFault("vm.effect-order", "Effects must be ordered by sequence."));
            previous = effect;
            lastSequence = effect.Sequence;
            if (effect.CompletionMode != VmCompletionMode.WaitPort)
                continue;
            waitEffect = effect;
            waitEffectCount++;
        }
        if (result.State is VmExecutionState.WaitingInput or VmExecutionState.WaitingPort)
        {
            if (waitEffectCount != 1 || waitEffect is null)
                throw new VmContractException(new VmFault("vm.wait-effect-count", "A waiting result must contain exactly one wait effect."));
            if (result.State == VmExecutionState.WaitingInput && waitEffect is not VmInputEffect)
                throw new VmContractException(new VmFault("vm.wait-effect-kind", "WaitingInput requires a VmInputEffect."));
            if (result.State == VmExecutionState.WaitingPort &&
                waitEffect is not VmPortEffect and not VmApplicationEffect)
                throw new VmContractException(new VmFault("vm.wait-effect-kind", "WaitingPort requires a VmPortEffect."));
        }
        else if (waitEffectCount != 0)
        {
            throw new VmContractException(new VmFault("vm.wait-state-missing", "Wait effects require a waiting execution state."));
        }

        _lastEffectSequence = lastSequence;
        return waitEffect switch
        {
            VmInputEffect input => input.OperationId,
            VmPortEffect port => port.OperationId,
            VmApplicationEffect application => application.OperationId,
            _ => VmOperationId.None,
        };
    }

    private void EnsureStepAllowed()
    {
        if (State is VmExecutionState.WaitingInput or VmExecutionState.WaitingPort)
            throw new InvalidOperationException("Interpreter is waiting for a completion; call ResumeAsync.");
        if (State is VmExecutionState.Completed or VmExecutionState.Faulted or VmExecutionState.Cancelled)
            throw new InvalidOperationException($"Interpreter is terminal (state={State}).");
    }

    private void ThrowIfDisposed()
    {
        if (IsDisposed)
            throw new ObjectDisposedException(GetType().Name);
    }

    private bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    private VmStepResult CreateCancelledResult()
    {
        State = VmExecutionState.Cancelled;
        _pendingOperation = VmOperationId.None;
        return new VmStepResult(
            VmExecutionState.Cancelled,
            VmStepStopReason.Cancelled,
            0,
            0,
            Array.Empty<VmEffect>());
    }

    private void BeginExecution()
    {
        if (Interlocked.CompareExchange(ref _executionInProgress, 1, 0) != 0)
            throw new InvalidOperationException("Interpreter execution is already in progress on its owner thread.");
    }

    private sealed class VmContractException : Exception
    {
        public VmContractException(VmFault fault)
            : base(fault.Message)
        {
            Fault = fault;
        }

        public VmFault Fault { get; }
    }
}

internal static class InterpreterContractText
{
    public static string Required(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value must not be empty.", parameterName);
        return value.Trim();
    }

    public static string RequiredIdentifier(string? value, string parameterName)
    {
        var normalized = Required(value, parameterName);
        if (!System.Text.RegularExpressions.Regex.IsMatch(
                normalized,
                "^[a-z][a-z0-9.\\-]*$",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant))
        {
            throw new ArgumentException($"Invalid identifier: {normalized}", parameterName);
        }
        return normalized;
    }

    public static string RequiredSemanticVersion(string? value, string parameterName)
    {
        var normalized = Required(value, parameterName);
        if (!System.Text.RegularExpressions.Regex.IsMatch(
                normalized,
                "^[0-9]+\\.[0-9]+\\.[0-9]+$",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant))
        {
            throw new ArgumentException($"Invalid semantic version: {normalized}", parameterName);
        }
        return normalized;
    }

    public static string RequiredVersionRange(string? value, string parameterName)
    {
        var normalized = Required(value, parameterName);
        if (!SemanticVersionRange.IsValid(normalized))
            throw new ArgumentException($"Invalid semantic version range: {normalized}", parameterName);
        return normalized;
    }

    public static IReadOnlyList<string> UniqueSortedIdentifiers(IEnumerable<string> values, string parameterName)
    {
        var copy = values.Select(value => RequiredIdentifier(value, parameterName)).ToArray();
        if (copy.GroupBy(value => value, StringComparer.Ordinal).Any(group => group.Count() > 1))
            throw new ArgumentException("Collection contains duplicate identifiers.", parameterName);
        Array.Sort(copy, StringComparer.Ordinal);
        return Array.AsReadOnly(copy);
    }

    public static string RequiredEngineId(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Interpreter engine id must not be empty.", parameterName);
        var normalized = value.Trim();
        if (!System.Text.RegularExpressions.Regex.IsMatch(
                normalized,
                "^[a-z][a-z0-9.\\-]*$",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant))
        {
            throw new ArgumentException($"Invalid interpreter engine id: {normalized}", parameterName);
        }

        return normalized;
    }
}
