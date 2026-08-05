using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using GEmuera.Core.Session;

namespace GEmuera.Core.Ports;

public readonly record struct PortTypeId
{
    public PortTypeId(string value)
    {
        Value = PortContractText.RequiredTypeId(value, nameof(value));
    }

    public string Value { get; }

    public override string ToString() => Value;
}

public readonly record struct CapabilityId
{
    public CapabilityId(string value)
    {
        Value = PortContractText.RequiredCapabilityId(value, nameof(value));
    }

    public string Value { get; }

    public override string ToString() => Value;
}

public readonly record struct PortOperationKey(SessionGeneration Generation, SessionOperationId OperationId);

public enum PortOwnerKind
{
    Vm,
    Session,
    Lifecycle,
}

public sealed record PortOwner
{
    public PortOwner(PortOwnerKind kind, string ownerId)
    {
        Kind = kind;
        OwnerId = PortContractText.RequiredIdentifier(ownerId, nameof(ownerId));
    }

    public PortOwnerKind Kind { get; }
    public string OwnerId { get; }
}

public enum PortCompletionMode
{
    CoreImmediate,
    WaitPort,
}

public enum PortCancellationMode
{
    NotCancellable,
    Cooperative,
    CancelAndDrain,
}

public enum PortTimeoutMode
{
    Fault,
    UseFallback,
}

public sealed record PortTimeout
{
    public PortTimeout(TimeSpan duration, PortTimeoutMode mode)
    {
        Duration = Validate(duration);
        Mode = mode;
    }

    public TimeSpan Duration { get; }
    public PortTimeoutMode Mode { get; }

    private static TimeSpan Validate(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero || duration == Timeout.InfiniteTimeSpan)
            throw new ArgumentOutOfRangeException(nameof(duration), "Port timeout must be finite and positive.");
        return duration;
    }
}

public sealed record PortPayloadLimits
{
    public PortPayloadLimits(long maxRequestBytes, long maxCompletionBytes)
    {
        MaxRequestBytes = Validate(maxRequestBytes, nameof(maxRequestBytes));
        MaxCompletionBytes = Validate(maxCompletionBytes, nameof(maxCompletionBytes));
    }

    public long MaxRequestBytes { get; }
    public long MaxCompletionBytes { get; }

    private static long Validate(long value, string parameterName)
    {
        if (value <= 0)
            throw new ArgumentOutOfRangeException(parameterName, "Payload limit must be positive.");
        return value;
    }
}

public enum PortErrorCode
{
    InvalidRequest,
    Unsupported,
    PermissionDenied,
    Cancelled,
    TimedOut,
    StaleCompletion,
    DuplicateOperation,
    Busy,
    PayloadTooLarge,
    IoFailure,
    CorruptData,
    Conflict,
    SecurityRevoked,
    RecoveryRequired,
    LifecycleReset,
    OwnerUnavailable,
    InvalidState,
}

public sealed record PortFault
{
    public PortFault(PortErrorCode code, string message, bool isTransient = false)
    {
        Code = code;
        Message = PortContractText.Required(message, nameof(message));
        IsTransient = isTransient;
    }

    public PortErrorCode Code { get; }
    public string Message { get; }
    public bool IsTransient { get; }
}

public sealed record PortCancellationContract
{
    public PortCancellationContract(PortCancellationMode mode, bool completionAfterCancel, TimeSpan drainTimeout)
    {
        Mode = mode;
        CompletionAfterCancel = completionAfterCancel;
        DrainTimeout = ValidateDrainTimeout(drainTimeout);
    }

    public PortCancellationMode Mode { get; }
    public bool CompletionAfterCancel { get; }
    public TimeSpan DrainTimeout { get; }

    private static TimeSpan ValidateDrainTimeout(TimeSpan value)
    {
        if (value < TimeSpan.Zero || value == Timeout.InfiniteTimeSpan)
            throw new ArgumentOutOfRangeException(nameof(value), "Drain timeout must be zero or finite.");
        return value;
    }
}

public sealed record PortRequest<TArguments>
{
    public PortRequest(
        PortTypeId portType,
        CapabilityId capability,
        SessionGeneration generation,
        SessionOperationId operationId,
        TArguments arguments)
    {
        if (operationId == SessionOperationId.None)
            throw new ArgumentException("A port request must have an operation id.", nameof(operationId));
        if (arguments is null)
            throw new ArgumentNullException(nameof(arguments));

        PortType = portType;
        Capability = capability;
        Generation = generation;
        OperationId = operationId;
        Arguments = arguments;
    }

    public PortTypeId PortType { get; }
    public CapabilityId Capability { get; }
    public SessionGeneration Generation { get; }
    public SessionOperationId OperationId { get; }
    public PortOperationKey Key => new(Generation, OperationId);
    public SessionStamp Stamp => new(Generation, OperationId);
    public TArguments Arguments { get; }
}

public sealed record PortCompletion<TPayload>
{
    public PortCompletion(
        PortTypeId portType,
        CapabilityId capability,
        SessionGeneration generation,
        SessionOperationId operationId,
        TPayload? payload,
        PortFault? fault)
    {
        if (operationId == SessionOperationId.None)
            throw new ArgumentException("A port completion must have an operation id.", nameof(operationId));
        if (fault is null && payload is null && default(TPayload) is null)
            throw new ArgumentException("A successful completion must have a payload or a non-nullable payload type.", nameof(payload));
        if (fault is not null && payload is not null)
            throw new ArgumentException("A faulted completion cannot carry a payload.", nameof(payload));

        PortType = portType;
        Capability = capability;
        Generation = generation;
        OperationId = operationId;
        Payload = payload;
        Fault = fault;
    }

    public PortTypeId PortType { get; }
    public CapabilityId Capability { get; }
    public SessionGeneration Generation { get; }
    public SessionOperationId OperationId { get; }
    public PortOperationKey Key => new(Generation, OperationId);
    public SessionStamp Stamp => new(Generation, OperationId);
    public TPayload? Payload { get; }
    public PortFault? Fault { get; }
    public bool IsSuccess => Fault is null;

    public static PortCompletion<TPayload> Succeeded(
        PortRequest<object?> request,
        TPayload payload)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new PortCompletion<TPayload>(request.PortType, request.Capability, request.Generation, request.OperationId, payload, null);
    }

    public static PortCompletion<TPayload> From<TArguments>(PortRequest<TArguments> request, TPayload payload)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new PortCompletion<TPayload>(request.PortType, request.Capability, request.Generation, request.OperationId, payload, null);
    }

    public static PortCompletion<TPayload> Failed<TArguments>(PortRequest<TArguments> request, PortFault fault)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(fault);
        return new PortCompletion<TPayload>(request.PortType, request.Capability, request.Generation, request.OperationId, default, fault);
    }
}

internal static class PortContractText
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
        if (!Regex.IsMatch(normalized, "^[a-z][a-z0-9.\\-]*$", RegexOptions.CultureInvariant))
            throw new ArgumentException($"Invalid identifier: {normalized}", parameterName);
        return normalized;
    }

    public static string RequiredTypeId(string? value, string parameterName)
    {
        var normalized = Required(value, parameterName);
        if (!Regex.IsMatch(normalized, "^I[A-Za-z][A-Za-z0-9]*$", RegexOptions.CultureInvariant))
            throw new ArgumentException($"Invalid port type id: {normalized}", parameterName);
        return normalized;
    }

    public static string RequiredCapabilityId(string? value, string parameterName)
    {
        var normalized = Required(value, parameterName);
        if (!Regex.IsMatch(normalized, "^[a-z][a-z0-9.\\-]*\\.v[0-9]+$", RegexOptions.CultureInvariant))
            throw new ArgumentException($"Invalid capability id: {normalized}", parameterName);
        return normalized;
    }
}
