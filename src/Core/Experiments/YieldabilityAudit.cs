using System.Collections.ObjectModel;

namespace GEmuera.Core.Experiments;

public enum YieldabilityPathCategory
{
    Regex,
    Html,
    ErbLazyLoad,
    Csv,
    Xml,
    Map,
    DataTable,
    Sqlite,
    FileScanTextIo,
    GZipSave,
    ImageDecode,
    GraphicsSpriteCbg,
    Plugin,
    Logging,
    LargeCollection,
}

public enum YieldabilityDisposition
{
    Yieldable,
    BoundedSynchronous,
    Rejected,
}

/// <summary>
/// One auditable path record. Rejected is intentionally a first-class value:
/// an incomplete audit cannot be mistaken for an approved cooperative path.
/// </summary>
public sealed record YieldabilityAuditRecord
{
    public YieldabilityAuditRecord(
        string pathId,
        YieldabilityPathCategory category,
        YieldabilityDisposition disposition,
        double maxSynchronousMilliseconds,
        bool splittable,
        bool hasCancellationPoint,
        bool exposesIntermediateState,
        long reservedBytes,
        double? androidP99Milliseconds,
        string evidenceHash)
    {
        PathId = ExperimentContractText.RequiredToken(pathId, nameof(pathId));
        Category = category;
        Disposition = disposition;
        if (!double.IsFinite(maxSynchronousMilliseconds) || maxSynchronousMilliseconds < 0)
            throw new ArgumentOutOfRangeException(nameof(maxSynchronousMilliseconds));
        if (reservedBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(reservedBytes));
        if (androidP99Milliseconds is < 0 || (androidP99Milliseconds.HasValue && !double.IsFinite(androidP99Milliseconds.Value)))
            throw new ArgumentOutOfRangeException(nameof(androidP99Milliseconds));

        MaxSynchronousMilliseconds = maxSynchronousMilliseconds;
        Splittable = splittable;
        HasCancellationPoint = hasCancellationPoint;
        ExposesIntermediateState = exposesIntermediateState;
        ReservedBytes = reservedBytes;
        AndroidP99Milliseconds = androidP99Milliseconds;
        EvidenceHash = ExperimentContractText.RequiredSha256(evidenceHash, nameof(evidenceHash));

        if (disposition == YieldabilityDisposition.Yieldable && (!splittable || !hasCancellationPoint))
            throw new ArgumentException("Yieldable paths require splitting and cancellation evidence.", nameof(disposition));
        if (disposition == YieldabilityDisposition.BoundedSynchronous && maxSynchronousMilliseconds <= 0)
            throw new ArgumentException("Bounded synchronous paths require a positive measured bound.", nameof(maxSynchronousMilliseconds));
    }

    public string PathId { get; }
    public YieldabilityPathCategory Category { get; }
    public YieldabilityDisposition Disposition { get; }
    public double MaxSynchronousMilliseconds { get; }
    public bool Splittable { get; }
    public bool HasCancellationPoint { get; }
    public bool ExposesIntermediateState { get; }
    public long ReservedBytes { get; }
    public double? AndroidP99Milliseconds { get; }
    public string EvidenceHash { get; }

    public bool IsAccepted => Disposition is YieldabilityDisposition.Yieldable or YieldabilityDisposition.BoundedSynchronous;
}

public sealed record YieldabilityAuditEvaluation
{
    internal YieldabilityAuditEvaluation(IReadOnlyList<string> blockerCodes)
    {
        BlockerCodes = blockerCodes;
    }

    public bool IsAccepted => BlockerCodes.Count == 0;
    public IReadOnlyList<string> BlockerCodes { get; }
}

/// <summary>
/// Complete, immutable yieldability inventory for M6-EXP-02. It does not
/// infer missing categories from static references; callers must provide an
/// explicit record for every path in scope.
/// </summary>
public sealed class YieldabilityAuditInventory
{
    private readonly IReadOnlyList<YieldabilityAuditRecord> _records;

    public YieldabilityAuditInventory(IEnumerable<YieldabilityAuditRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        var copy = records.ToArray();
        if (copy.Length == 0)
            throw new ArgumentException("A cooperative experiment requires an explicit yieldability inventory.", nameof(records));
        if (copy.Any(record => record is null))
            throw new ArgumentException("Audit records cannot contain null.", nameof(records));
        if (copy.GroupBy(record => record.PathId, StringComparer.Ordinal).Any(group => group.Count() > 1))
            throw new ArgumentException("Audit path ids must be unique.", nameof(records));
        Array.Sort(copy, (left, right) => StringComparer.Ordinal.Compare(left.PathId, right.PathId));
        _records = new ReadOnlyCollection<YieldabilityAuditRecord>(copy);
    }

    public IReadOnlyList<YieldabilityAuditRecord> Records => _records;
    public IReadOnlyList<string> UnacceptedPathIds => _records
        .Where(record => !record.IsAccepted)
        .Select(record => record.PathId)
        .ToArray();

    public YieldabilityAuditEvaluation Evaluate()
    {
        var blockers = new List<string>();
        foreach (var record in _records)
        {
            if (!record.IsAccepted)
                blockers.Add($"yieldability.rejected:{record.PathId}");
            if (record.AndroidP99Milliseconds is null)
                blockers.Add($"yieldability.android-p99-missing:{record.PathId}");
            if (record.ReservedBytes == 0)
                blockers.Add($"yieldability.memory-reservation-missing:{record.PathId}");
            if (!record.ExposesIntermediateState && record.Disposition == YieldabilityDisposition.Yieldable)
                blockers.Add($"yieldability.observable-state-missing:{record.PathId}");
        }

        return new YieldabilityAuditEvaluation(Array.AsReadOnly(blockers.ToArray()));
    }

    public void EnsureRunnable()
    {
        EnsureCategories(Enum.GetValues<YieldabilityPathCategory>());
        var evaluation = Evaluate();
        if (!evaluation.IsAccepted)
        {
            throw new InvalidOperationException(
                $"Yieldability audit is not complete: {string.Join(",", evaluation.BlockerCodes)}");
        }
    }

    public void EnsureCategories(IEnumerable<YieldabilityPathCategory> requiredCategories)
    {
        ArgumentNullException.ThrowIfNull(requiredCategories);
        var required = requiredCategories.Distinct().ToArray();
        var actual = _records.Select(record => record.Category).ToHashSet();
        var missing = required.Where(category => !actual.Contains(category)).ToArray();
        if (missing.Length != 0)
            throw new InvalidOperationException($"Yieldability audit is missing categories: {string.Join(",", missing)}");
    }
}
