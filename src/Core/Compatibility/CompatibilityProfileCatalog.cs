using System.Collections.ObjectModel;

namespace GEmuera.Core.Compatibility;

/// <summary>
/// Immutable, application-owned declaration of one supported compatibility
/// profile and the root dialect modules it selects. This is deliberately not a
/// content resolver: it neither examines a game directory nor loads code.
/// </summary>
public sealed class CompatibilityProfileDefinition
{
    private readonly ReadOnlyCollection<string> _rootModuleIds;

    public CompatibilityProfileDefinition(
        string profileId,
        IEnumerable<string> rootModuleIds)
    {
        ArgumentNullException.ThrowIfNull(rootModuleIds);
        ProfileId = ContractText.RequiredIdentifier(profileId, nameof(profileId));
        _rootModuleIds = ContractCollections.UniqueSortedStrings(
            rootModuleIds,
            value => ContractText.RequiredIdentifier(value, nameof(rootModuleIds)),
            nameof(rootModuleIds));
        if (_rootModuleIds.Count == 0)
        {
            throw new ArgumentException(
                "A compatibility profile must select at least one root module.",
                nameof(rootModuleIds));
        }
    }

    public string ProfileId { get; }

    /// <summary>
    /// Root modules only. <see cref="DialectModuleCatalog"/> resolves their
    /// dependency closure in a deterministic order when it builds a plan.
    /// </summary>
    public IReadOnlyList<string> RootModuleIds => _rootModuleIds;
}

/// <summary>
/// Trusted, compile-time profile allowlist. Future distributions may register
/// additional built-in definitions at application composition time; runtime
/// assembly discovery and game-content probing remain outside this boundary.
/// </summary>
public sealed class CompatibilityProfileCatalog
{
    private readonly object _gate = new();
    private readonly Dictionary<string, CompatibilityProfileDefinition> _profiles =
        new(StringComparer.Ordinal);
    private bool _frozen;

    public bool IsFrozen
    {
        get
        {
            lock (_gate)
                return _frozen;
        }
    }

    /// <summary>
    /// Closes application composition before a session starts. Profiles remain
    /// compile-time declarations rather than a runtime content resolver.
    /// </summary>
    public void Freeze()
    {
        lock (_gate)
            _frozen = true;
    }

    public void Register(CompatibilityProfileDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        lock (_gate)
        {
            if (_frozen)
                throw new InvalidOperationException("Compatibility profile catalog is frozen.");
            if (!_profiles.TryAdd(definition.ProfileId, definition))
            {
                throw new InvalidOperationException(
                    $"Duplicate compatibility profile: {definition.ProfileId}.");
            }
        }
    }

    public CompatibilityProfileDefinition Resolve(string profileId)
    {
        var id = ContractText.RequiredIdentifier(profileId, nameof(profileId));
        lock (_gate)
        {
            if (!_profiles.TryGetValue(id, out var definition))
            {
                throw new InvalidOperationException(
                    $"Compatibility profile '{id}' was not registered.");
            }

            return definition;
        }
    }

    public bool TryResolve(string profileId, out CompatibilityProfileDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            definition = null!;
            return false;
        }

        var id = profileId.Trim();
        if (!System.Text.RegularExpressions.Regex.IsMatch(
                id,
                "^[a-z][a-z0-9.\\-]*$",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant))
        {
            definition = null!;
            return false;
        }

        lock (_gate)
            return _profiles.TryGetValue(id, out definition!);
    }
}
