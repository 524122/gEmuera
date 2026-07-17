using GEmuera.Core.Compatibility;
using GEmuera.Core.Parsing;
using GEmuera.Core.Session;

namespace GEmuera.Core.Runtime;

public sealed record CoreSessionBoundary(SessionGeneration Generation, CompatibilityPlan Compatibility, ContentToken Source)
{
    public CompatibilityPlan Compatibility { get; } = Compatibility ?? throw new ArgumentNullException(nameof(Compatibility));
}

public interface ILegacyCoreAdapter
{
    CoreSessionBoundary Boundary { get; }
    ErbParseResult Parse(string source, ContentToken token);
}

/// <summary>
/// Candidate-only bridge. It owns no legacy static state and does not select a
/// production path; the host may use it for a side-by-side Core comparison.
/// </summary>
public sealed class LegacyCoreAdapter : ILegacyCoreAdapter
{
    private readonly ErbParser _parser = new();
    public LegacyCoreAdapter(CoreSessionBoundary boundary) { Boundary = boundary ?? throw new ArgumentNullException(nameof(boundary)); }
    public CoreSessionBoundary Boundary { get; }
    public ErbParseResult Parse(string source, ContentToken token) => _parser.Parse(source, token);
}
