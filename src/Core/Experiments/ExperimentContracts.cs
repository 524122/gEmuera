using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using GEmuera.Core.Runtime;
using GEmuera.Core.Session;

namespace GEmuera.Core.Experiments;

/// <summary>
/// M6 flags are independent values. The default keeps every candidate path
/// disabled and leaves the accessibility fallback enabled.
/// </summary>
public readonly record struct FeatureFlags(
    bool Scheduler,
    bool Renderer,
    bool DualRun,
    bool Accessibility)
{
    public static FeatureFlags Default => new(
        Scheduler: false,
        Renderer: false,
        DualRun: false,
        Accessibility: true);

    public string CanonicalValue => string.Join(
        ",",
        $"accessibility={(Accessibility ? "on" : "off")}",
        $"dual-run={(DualRun ? "on" : "off")}",
        $"renderer={(Renderer ? "on" : "off")}",
        $"scheduler={(Scheduler ? "on" : "off")}");
}

public enum EvidenceStatus
{
    ExperimentalOnly,
    Blocked,
    ReportInvalidated,
}

/// <summary>
/// Reproducibility identity for one M6 experiment instance. It contains
/// identities rather than filesystem routes, so constructing it cannot alter
/// the production route or resolve a default game path.
/// </summary>
public sealed record ExperimentIdentity
{
    public ExperimentIdentity(
        string experimentId,
        string experimentVersion,
        string sourceIdentity,
        string toolchainIdentity,
        string runtimeIdentity,
        string fixtureIdentity,
        string profileId,
        string deviceId,
        string releaseArtifact,
        int warmupRuns,
        int sampleCount,
        long seed,
        SessionStamp session,
        FeatureFlags flags)
    {
        ExperimentId = ExperimentContractText.RequiredToken(experimentId, nameof(experimentId));
        ExperimentVersion = ExperimentContractText.RequiredToken(experimentVersion, nameof(experimentVersion));
        SourceIdentity = ExperimentContractText.Required(sourceIdentity, nameof(sourceIdentity));
        ToolchainIdentity = ExperimentContractText.Required(toolchainIdentity, nameof(toolchainIdentity));
        RuntimeIdentity = ExperimentContractText.Required(runtimeIdentity, nameof(runtimeIdentity));
        FixtureIdentity = ExperimentContractText.Required(fixtureIdentity, nameof(fixtureIdentity));
        ProfileId = ExperimentContractText.RequiredToken(profileId, nameof(profileId));
        DeviceId = ExperimentContractText.RequiredToken(deviceId, nameof(deviceId));
        ReleaseArtifact = ExperimentContractText.Required(releaseArtifact, nameof(releaseArtifact));
        if (warmupRuns < 0)
            throw new ArgumentOutOfRangeException(nameof(warmupRuns));
        if (sampleCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleCount));
        if (session.Generation.Value <= 0 || session.OperationId.Value <= 0)
            throw new ArgumentException("Experiment sessions require positive generation and operation ids.", nameof(session));

        WarmupRuns = warmupRuns;
        SampleCount = sampleCount;
        Seed = seed;
        Session = session;
        Flags = flags;
        CanonicalHash = ExperimentContractText.Sha256(Canonicalize());
    }

    public string ExperimentId { get; }
    public string ExperimentVersion { get; }
    public string SourceIdentity { get; }
    public string ToolchainIdentity { get; }
    public string RuntimeIdentity { get; }
    public string FixtureIdentity { get; }
    public string ProfileId { get; }
    public string DeviceId { get; }
    public string ReleaseArtifact { get; }
    public int WarmupRuns { get; }
    public int SampleCount { get; }
    public long Seed { get; }
    public SessionStamp Session { get; }
    public FeatureFlags Flags { get; }
    public string CanonicalHash { get; }

    private string Canonicalize()
    {
        return string.Join(
            "\n",
            $"device={DeviceId}",
            $"experiment={ExperimentId}",
            $"experiment-version={ExperimentVersion}",
            $"fixture={FixtureIdentity}",
            $"flags={Flags.CanonicalValue}",
            $"profile={ProfileId}",
            $"release-artifact={ReleaseArtifact}",
            $"runtime={RuntimeIdentity}",
            $"sample-count={SampleCount}",
            $"seed={Seed}",
            $"session-generation={Session.Generation.Value}",
            $"session-operation={Session.OperationId.Value}",
            $"source={SourceIdentity}",
            $"toolchain={ToolchainIdentity}",
            $"warmup-runs={WarmupRuns}");
    }
}

public readonly record struct PerformanceThresholds
{
    public PerformanceThresholds(
        double maxP95Milliseconds,
        double maxP99Milliseconds,
        long maxRssBytes,
        double maxGpuMilliseconds)
    {
        if (!double.IsFinite(maxP95Milliseconds) || maxP95Milliseconds < 0)
            throw new ArgumentOutOfRangeException(nameof(maxP95Milliseconds));
        if (!double.IsFinite(maxP99Milliseconds) || maxP99Milliseconds < maxP95Milliseconds)
            throw new ArgumentOutOfRangeException(nameof(maxP99Milliseconds));
        if (maxRssBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(maxRssBytes));
        if (!double.IsFinite(maxGpuMilliseconds) || maxGpuMilliseconds < 0)
            throw new ArgumentOutOfRangeException(nameof(maxGpuMilliseconds));

        MaxP95Milliseconds = maxP95Milliseconds;
        MaxP99Milliseconds = maxP99Milliseconds;
        MaxRssBytes = maxRssBytes;
        MaxGpuMilliseconds = maxGpuMilliseconds;
    }

    public double MaxP95Milliseconds { get; }
    public double MaxP99Milliseconds { get; }
    public long MaxRssBytes { get; }
    public double MaxGpuMilliseconds { get; }
}

/// <summary>
/// Frozen M6 assumptions and evidence prerequisites. Thresholds describe the
/// experiment; they do not turn metric samples into a production approval.
/// </summary>
public sealed record ExperimentDefinition
{
    public ExperimentDefinition(
        ExperimentIdentity identity,
        string hypothesis,
        string baselineArtifact,
        string rollbackArtifact,
        PerformanceThresholds thresholds,
        YieldabilityAuditInventory yieldabilityAudit)
    {
        Identity = identity ?? throw new ArgumentNullException(nameof(identity));
        Hypothesis = ExperimentContractText.Required(hypothesis, nameof(hypothesis));
        BaselineArtifact = ExperimentContractText.Required(baselineArtifact, nameof(baselineArtifact));
        RollbackArtifact = ExperimentContractText.Required(rollbackArtifact, nameof(rollbackArtifact));
        if (string.Equals(BaselineArtifact, RollbackArtifact, StringComparison.Ordinal))
            throw new ArgumentException("Baseline and rollback artifacts must be independently identified.", nameof(rollbackArtifact));
        Thresholds = thresholds;
        YieldabilityAudit = yieldabilityAudit ?? throw new ArgumentNullException(nameof(yieldabilityAudit));
    }

    public ExperimentIdentity Identity { get; }
    public string Hypothesis { get; }
    public string BaselineArtifact { get; }
    public string RollbackArtifact { get; }
    public PerformanceThresholds Thresholds { get; }
    public YieldabilityAuditInventory YieldabilityAudit { get; }
    public EvidenceStatus EvidenceStatus => EvidenceStatus.ExperimentalOnly;

    public void EnsureRunnable()
    {
        YieldabilityAudit.EnsureRunnable();
    }
}

public sealed record MetricSample
{
    public MetricSample(
        string experimentIdentityHash,
        string metricName,
        int ordinal,
        double value)
    {
        ExperimentIdentityHash = ExperimentContractText.RequiredSha256(experimentIdentityHash, nameof(experimentIdentityHash));
        MetricName = ExperimentContractText.RequiredToken(metricName, nameof(metricName));
        if (ordinal < 0)
            throw new ArgumentOutOfRangeException(nameof(ordinal));
        if (!double.IsFinite(value) || value < 0)
            throw new ArgumentOutOfRangeException(nameof(value));
        Ordinal = ordinal;
        Value = value;
    }

    public string ExperimentIdentityHash { get; }
    public string MetricName { get; }
    public int Ordinal { get; }
    public double Value { get; }
}

public sealed record MetricPercentiles
{
    internal MetricPercentiles(
        string experimentIdentityHash,
        string metricName,
        int sampleCount,
        double p50,
        double p95,
        double p99)
    {
        ExperimentIdentityHash = experimentIdentityHash;
        MetricName = metricName;
        SampleCount = sampleCount;
        P50 = p50;
        P95 = p95;
        P99 = p99;
    }

    public string ExperimentIdentityHash { get; }
    public string MetricName { get; }
    public int SampleCount { get; }
    public double P50 { get; }
    public double P95 { get; }
    public double P99 { get; }
    public EvidenceStatus EvidenceStatus => EvidenceStatus.ExperimentalOnly;
}

/// <summary>
/// Immutable metric series using nearest-rank percentiles. The report is
/// explicitly experimental and deliberately has no production-pass property.
/// </summary>
public sealed class MetricSeries
{
    private readonly IReadOnlyList<MetricSample> _samples;

    public MetricSeries(IEnumerable<MetricSample> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        var copy = samples.ToArray();
        if (copy.Length == 0)
            throw new ArgumentException("At least one metric sample is required.", nameof(samples));
        if (copy.Any(sample => sample is null))
            throw new ArgumentException("Metric samples cannot contain null.", nameof(samples));

        var first = copy[0];
        if (copy.Any(sample => !string.Equals(sample.ExperimentIdentityHash, first.ExperimentIdentityHash, StringComparison.Ordinal) ||
                               !string.Equals(sample.MetricName, first.MetricName, StringComparison.Ordinal)))
        {
            throw new ArgumentException("A metric series must contain one identity and metric name.", nameof(samples));
        }
        if (copy.GroupBy(sample => sample.Ordinal).Any(group => group.Count() != 1))
            throw new ArgumentException("Metric sample ordinals must be unique.", nameof(samples));

        Array.Sort(copy, (left, right) => left.Ordinal.CompareTo(right.Ordinal));
        _samples = new ReadOnlyCollection<MetricSample>(copy);
        IdentityHash = first.ExperimentIdentityHash;
        MetricName = first.MetricName;
    }

    public string IdentityHash { get; }
    public string MetricName { get; }
    public IReadOnlyList<MetricSample> Samples => _samples;

    public MetricPercentiles Summarize()
    {
        var values = _samples.Select(sample => sample.Value).OrderBy(value => value).ToArray();
        return new MetricPercentiles(
            IdentityHash,
            MetricName,
            values.Length,
            NearestRank(values, 0.50),
            NearestRank(values, 0.95),
            NearestRank(values, 0.99));
    }

    private static double NearestRank(IReadOnlyList<double> sortedValues, double percentile)
    {
        var rank = Math.Max(1, (int)Math.Ceiling(sortedValues.Count * percentile));
        return sortedValues[rank - 1];
    }
}

public sealed record CooperativeVmStep
{
    internal CooperativeVmStep(
        ExperimentIdentity identity,
        long traceOrdinal,
        VmStepResult result)
    {
        Identity = identity;
        TraceOrdinal = traceOrdinal;
        Result = result;
    }

    public ExperimentIdentity Identity { get; }
    public long TraceOrdinal { get; }
    public VmStepResult Result { get; }
    public VmExecutionState State => Result.State;
    public VmStepStopReason StopReason => Result.StopReason;
    public int InstructionsExecuted => Result.InstructionsExecuted;
    public int WorkUnits => Result.WorkUnits;
    public IReadOnlyList<VmEffect> Effects => Result.Effects;
    public VmFault? Fault => Result.Fault;
}

/// <summary>
/// Pairing contract for dual-run experiments. The two hosts must be created
/// independently by the caller; this type only verifies immutable input
/// equivalence and never shares a cache, Resource, or session state.
/// </summary>
public sealed record DualRunContract
{
    public DualRunContract(ExperimentIdentity baseline, ExperimentIdentity candidate)
    {
        Baseline = baseline ?? throw new ArgumentNullException(nameof(baseline));
        Candidate = candidate ?? throw new ArgumentNullException(nameof(candidate));
        if (!Baseline.Flags.DualRun || !Candidate.Flags.DualRun)
            throw new ArgumentException("Both identities must explicitly enable the dual-run flag.");
        if (!string.Equals(Baseline.FixtureIdentity, Candidate.FixtureIdentity, StringComparison.Ordinal) ||
            !string.Equals(Baseline.ProfileId, Candidate.ProfileId, StringComparison.Ordinal) ||
            !string.Equals(Baseline.DeviceId, Candidate.DeviceId, StringComparison.Ordinal) ||
            Baseline.Seed != Candidate.Seed)
        {
            throw new ArgumentException("Dual-run identities must use the same fixture, profile, device, and seed.");
        }
        if (Baseline.Session == Candidate.Session)
            throw new ArgumentException("Dual-run sessions must have distinct generation/operation stamps.");
    }

    public ExperimentIdentity Baseline { get; }
    public ExperimentIdentity Candidate { get; }
}

internal static class ExperimentContractText
{
    public static string Required(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value must not be empty.", parameterName);
        return value.Trim();
    }

    public static string RequiredToken(string? value, string parameterName)
    {
        var normalized = Required(value, parameterName);
        if (!Regex.IsMatch(normalized, "^[A-Za-z0-9][A-Za-z0-9._-]*$", RegexOptions.CultureInvariant))
            throw new ArgumentException($"Invalid contract token: {normalized}", parameterName);
        return normalized;
    }

    public static string RequiredSha256(string? value, string parameterName)
    {
        var normalized = Required(value, parameterName).ToLowerInvariant();
        if (!Regex.IsMatch(normalized, "^[0-9a-f]{64}$", RegexOptions.CultureInvariant))
            throw new ArgumentException("Value must be a SHA-256 hex digest.", parameterName);
        return normalized;
    }

    public static string Sha256(string canonicalText)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalText))).ToLowerInvariant();
    }
}
