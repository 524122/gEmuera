using System.Collections.ObjectModel;

namespace GEmuera.Core.Compatibility;

public sealed class ModuleDependencySnapshot
{
    public string ModuleId { get; }
    public string VersionRange { get; }

    public ModuleDependencySnapshot(string moduleId, string versionRange)
    {
        ModuleId = ContractText.RequiredIdentifier(moduleId, nameof(moduleId));
        VersionRange = ContractText.RequiredVersionRange(versionRange, nameof(versionRange));
    }
}

public sealed class DialectModuleSnapshot
{
    private readonly ReadOnlyCollection<ModuleDependencySnapshot> _dependencies;
    private readonly ReadOnlyCollection<string> _portTypeIds;

    public string ModuleId { get; }
    public string ModuleVersion { get; }
    public int ModuleApiVersion { get; }
    public IReadOnlyList<ModuleDependencySnapshot> Dependencies => _dependencies;
    public IReadOnlyList<string> PortTypeIds => _portTypeIds;

    public DialectModuleSnapshot(
        string moduleId,
        string moduleVersion,
        int moduleApiVersion,
        IEnumerable<ModuleDependencySnapshot>? dependencies = null,
        IEnumerable<string>? portTypeIds = null)
    {
        ModuleId = ContractText.RequiredIdentifier(moduleId, nameof(moduleId));
        ModuleVersion = ContractText.RequiredSemanticVersion(moduleVersion, nameof(moduleVersion));
        if (moduleApiVersion <= 0)
            throw new ArgumentOutOfRangeException(nameof(moduleApiVersion), "Module API version must be positive.");

        _dependencies = ContractCollections.UniqueSorted(
            dependencies ?? Array.Empty<ModuleDependencySnapshot>(),
            dependency => dependency.ModuleId,
            StringComparer.Ordinal,
            nameof(dependencies));
        _portTypeIds = ContractCollections.UniqueSortedStrings(
            portTypeIds ?? Array.Empty<string>(),
            value => ContractText.RequiredTypeIdentifier(value, nameof(portTypeIds)),
            nameof(portTypeIds));
        ModuleApiVersion = moduleApiVersion;
    }
}

public enum PortContractKind
{
    PolicyDecision,
    BridgeProjection,
    FrozenCatalogContribution,
}

public sealed class BehaviorPortSnapshot
{
    public string BehaviorKeyId { get; }
    public string PortTypeId { get; }
    public PortContractKind ContractKind { get; }
    public string DecisionOwner { get; }
    public string ConsumerContractId { get; }
    public string TargetModuleId { get; }
    public string FixtureId { get; }

    public BehaviorPortSnapshot(
        string behaviorKeyId,
        string portTypeId,
        PortContractKind contractKind,
        string decisionOwner,
        string consumerContractId,
        string targetModuleId,
        string fixtureId)
    {
        if (!Enum.IsDefined(contractKind))
            throw new ArgumentOutOfRangeException(nameof(contractKind), "Unknown port contract kind.");

        BehaviorKeyId = ContractText.RequiredVersionedIdentifier(behaviorKeyId, nameof(behaviorKeyId));
        PortTypeId = ContractText.RequiredTypeIdentifier(portTypeId, nameof(portTypeId));
        DecisionOwner = ContractText.Required(decisionOwner, nameof(decisionOwner));
        ConsumerContractId = ContractText.RequiredVersionedIdentifier(consumerContractId, nameof(consumerContractId));
        TargetModuleId = ContractText.RequiredIdentifier(targetModuleId, nameof(targetModuleId));
        FixtureId = ContractText.RequiredFixtureId(fixtureId, nameof(fixtureId));
        ContractKind = contractKind;
    }
}
