using System.Collections.ObjectModel;
using System.Text.RegularExpressions;

namespace GEmuera.Core.Governance;

public enum RemovalLifecycleStatus { Active, CanaryOnly, ZeroTrafficObserved, RetiredPendingEvidence, Removed }
public sealed record RemovalInventoryEntry
{
	public RemovalInventoryEntry(string ownerId, string path, RemovalLifecycleStatus status, IEnumerable<string> dependentPackages, long invocationCount)
	{
		OwnerId = Required(ownerId, nameof(ownerId)); Path = Required(path, nameof(path)); if (invocationCount < 0) throw new ArgumentOutOfRangeException(nameof(invocationCount));
		var dependencies = (dependentPackages ?? throw new ArgumentNullException(nameof(dependentPackages))).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
		if (dependencies.Length == 0) throw new ArgumentException("Removal entries must identify dependent packages.", nameof(dependentPackages));
		Status = status; InvocationCount = invocationCount; DependentPackages = new ReadOnlyCollection<string>(dependencies);
	}
	public string OwnerId { get; }
	public string Path { get; }
	public RemovalLifecycleStatus Status { get; }
	public IReadOnlyList<string> DependentPackages { get; }
	public long InvocationCount { get; }
	private static string Required(string? value, string name) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value is required.", name) : value.Trim();
}

public sealed record ReleaseCycle
{
	public ReleaseCycle(string artifactId, DateTimeOffset startedUtc, DateTimeOffset endedUtc, long fallbackCalls, long faultCount, string reportHash)
	{
		ArtifactId = Required(artifactId, nameof(artifactId));
		if (endedUtc < startedUtc) throw new ArgumentException("Release cycle cannot end before it starts.");
		if (fallbackCalls < 0 || faultCount < 0) throw new ArgumentOutOfRangeException(nameof(fallbackCalls));
		StartedUtc = startedUtc; EndedUtc = endedUtc; FallbackCalls = fallbackCalls; FaultCount = faultCount; ReportHash = RequiredHash(reportHash, nameof(reportHash));
	}
	public string ArtifactId { get; }
	public DateTimeOffset StartedUtc { get; }
	public DateTimeOffset EndedUtc { get; }
	public long FallbackCalls { get; }
	public long FaultCount { get; }
	public string ReportHash { get; }
	private static string Required(string? value, string name) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value is required.", name) : value.Trim();
	internal static string RequiredHash(string? value, string name) => value is not null && Regex.IsMatch(value.Trim().ToLowerInvariant(), "^[a-f0-9]{64}$", RegexOptions.CultureInvariant) ? value.Trim().ToLowerInvariant() : throw new ArgumentException("Expected SHA-256 hash.", name);
}

public sealed record RollbackDrill(string StableArtifact, string RestoreCommand, bool Verified, string ReportHash)
{
	public string StableArtifact { get; } = string.IsNullOrWhiteSpace(StableArtifact) ? throw new ArgumentException("Stable artifact is required.", nameof(StableArtifact)) : StableArtifact.Trim();
	public string RestoreCommand { get; } = string.IsNullOrWhiteSpace(RestoreCommand) ? throw new ArgumentException("Restore command is required.", nameof(RestoreCommand)) : RestoreCommand.Trim();
	public string ReportHash { get; } = ReleaseCycle.RequiredHash(ReportHash, nameof(ReportHash));
}

public sealed record RemovalApproval(string PreparedBy, string ReviewedBy, string ApprovedBy, DateTimeOffset DecidedUtc, string Conclusion, IReadOnlyList<string> ReportHashes)
{
	public string PreparedBy { get; } = Required(PreparedBy, nameof(PreparedBy)); public string ReviewedBy { get; } = Required(ReviewedBy, nameof(ReviewedBy)); public string ApprovedBy { get; } = Required(ApprovedBy, nameof(ApprovedBy)); public string Conclusion { get; } = Required(Conclusion, nameof(Conclusion));
	public IReadOnlyList<string> ReportHashes { get; } = new ReadOnlyCollection<string>((ReportHashes ?? throw new ArgumentNullException(nameof(ReportHashes))).Select(hash => ReleaseCycle.RequiredHash(hash, nameof(ReportHashes))).Distinct(StringComparer.Ordinal).ToArray());
	private static string Required(string? value, string name) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value is required.", name) : value.Trim();
}

public sealed class RemovalPacket
{
	private readonly IReadOnlyList<RemovalInventoryEntry> _entries; private readonly IReadOnlyList<ReleaseCycle> _cycles;
	public RemovalPacket(string packetId, IEnumerable<RemovalInventoryEntry> entries, IEnumerable<ReleaseCycle> cycles, RollbackDrill rollback, RemovalApproval approval)
	{
		PacketId = string.IsNullOrWhiteSpace(packetId) ? throw new ArgumentException("Packet id is required.", nameof(packetId)) : packetId.Trim();
		_entries = new ReadOnlyCollection<RemovalInventoryEntry>((entries ?? throw new ArgumentNullException(nameof(entries))).ToArray()); _cycles = new ReadOnlyCollection<ReleaseCycle>((cycles ?? throw new ArgumentNullException(nameof(cycles))).ToArray());
		if (_entries.Count == 0 || _cycles.Count == 0) throw new ArgumentException("Removal packet requires inventory and release evidence."); Rollback = rollback ?? throw new ArgumentNullException(nameof(rollback)); Approval = approval ?? throw new ArgumentNullException(nameof(approval));
	}
	public string PacketId { get; }
	public IReadOnlyList<RemovalInventoryEntry> Entries => _entries; public IReadOnlyList<ReleaseCycle> Cycles => _cycles; public RollbackDrill Rollback { get; }
	public RemovalApproval Approval { get; }
	public bool CanMarkRemoved()
	{
		if (_cycles.Count < 2 || _cycles.TakeLast(2).Any(cycle => cycle.FallbackCalls != 0 || cycle.FaultCount != 0)) return false;
		if (_entries.Any(entry => entry.Status != RemovalLifecycleStatus.ZeroTrafficObserved && entry.Status != RemovalLifecycleStatus.RetiredPendingEvidence)) return false;
		return Rollback.Verified && string.Equals(Approval.Conclusion, "Approved", StringComparison.Ordinal);
	}
}
