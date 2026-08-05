using System.Collections.ObjectModel;

namespace GEmuera.Core.Ports;

public enum FallbackAdapterKind
{
    Legacy,
    Disabled,
    Uncovered,
}

public sealed record FallbackAdapterDescriptor
{
    public FallbackAdapterDescriptor(
        FallbackAdapterKind kind,
        string adapterId,
        string reason,
        bool PreservesScriptObservableOrder)
    {
        Kind = kind;
        AdapterId = PortContractText.RequiredIdentifier(adapterId, nameof(adapterId));
        Reason = PortContractText.Required(reason, nameof(reason));
        this.PreservesScriptObservableOrder = PreservesScriptObservableOrder;
    }

    public FallbackAdapterKind Kind { get; }
    public string AdapterId { get; }
    public string Reason { get; }
    public bool PreservesScriptObservableOrder { get; }
}

public sealed record PortManifestEntry
{
    public PortManifestEntry(
        PortTypeId portType,
        CapabilityId capability,
        string version,
        PortOwner owner,
        PortCompletionMode completionMode,
        PortPayloadLimits payloadLimits,
        PortCancellationContract cancellation,
        PortTimeout timeout,
        IEnumerable<PortErrorCode> errorCodes,
        FallbackAdapterDescriptor fallbackAdapter,
        bool AffectsScriptObservableOrder,
        int MaxConcurrency = 1)
    {
        PortType = portType;
        Capability = capability;
        Version = PortContractTextExtensions.RequiredSemanticVersion(version, nameof(version));
        Owner = owner ?? throw new ArgumentNullException(nameof(owner));
        CompletionMode = completionMode;
        PayloadLimits = payloadLimits ?? throw new ArgumentNullException(nameof(payloadLimits));
        Cancellation = cancellation ?? throw new ArgumentNullException(nameof(cancellation));
        Timeout = timeout ?? throw new ArgumentNullException(nameof(timeout));
        FallbackAdapter = fallbackAdapter ?? throw new ArgumentNullException(nameof(fallbackAdapter));
        if (MaxConcurrency <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaxConcurrency));

        var codes = (errorCodes ?? throw new ArgumentNullException(nameof(errorCodes))).Distinct().OrderBy(value => value).ToArray();
        if (codes.Length == 0)
            throw new ArgumentException("At least one error code is required.", nameof(errorCodes));
        ErrorCodes = new ReadOnlyCollection<PortErrorCode>(codes);
        this.AffectsScriptObservableOrder = AffectsScriptObservableOrder;
        this.MaxConcurrency = MaxConcurrency;
    }

    public PortTypeId PortType { get; }
    public CapabilityId Capability { get; }
    public string Version { get; }
    public PortOwner Owner { get; }
    public PortCompletionMode CompletionMode { get; }
    public PortPayloadLimits PayloadLimits { get; }
    public PortCancellationContract Cancellation { get; }
    public PortTimeout Timeout { get; }
    public IReadOnlyList<PortErrorCode> ErrorCodes { get; }
    public FallbackAdapterDescriptor FallbackAdapter { get; }
    public bool AffectsScriptObservableOrder { get; }
    public int MaxConcurrency { get; }
}

public sealed class PortManifest
{
    private readonly ReadOnlyCollection<PortManifestEntry> _entries;
    private readonly IReadOnlyDictionary<PortTypeId, PortManifestEntry> _byPortType;

    public PortManifest(IEnumerable<PortManifestEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var list = entries.ToList();
        if (list.Any(entry => entry is null))
            throw new ArgumentException("Manifest entries must not contain null.", nameof(entries));
        var duplicate = list.GroupBy(entry => entry.PortType).FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new ArgumentException($"Duplicate port type: {duplicate.Key}", nameof(entries));

        list.Sort((left, right) => string.CompareOrdinal(left.PortType.Value, right.PortType.Value));
        _entries = new ReadOnlyCollection<PortManifestEntry>(list);
        _byPortType = new ReadOnlyDictionary<PortTypeId, PortManifestEntry>(
            list.ToDictionary(entry => entry.PortType));
    }

    public IReadOnlyList<PortManifestEntry> Entries => _entries;

    public bool TryGet(PortTypeId portType, out PortManifestEntry entry) => _byPortType.TryGetValue(portType, out entry!);

    public PortManifestEntry GetRequired(PortTypeId portType)
    {
        if (!TryGet(portType, out var entry))
            throw new KeyNotFoundException($"Port type is not registered: {portType.Value}");
        return entry;
    }

    public bool Supports(CapabilityId capability) => _entries.Any(entry => entry.Capability == capability);
}

internal static class PortContractTextExtensions
{
    public static string RequiredSemanticVersion(string? value, string parameterName)
    {
        var normalized = PortContractText.Required(value, parameterName);
        var parts = normalized.Split('.');
        if (parts.Length != 3 || parts.Any(part => part.Length == 0 || !part.All(char.IsAsciiDigit)))
            throw new ArgumentException($"Invalid semantic version: {normalized}", parameterName);
        return normalized;
    }
}
