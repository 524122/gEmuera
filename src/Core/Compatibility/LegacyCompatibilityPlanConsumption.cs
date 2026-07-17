using System.Collections.ObjectModel;

namespace GEmuera.Core.Compatibility;

/// <summary>
/// The legacy bridge is still the behavior owner during M1, but it must not
/// silently ignore a descriptor selected by a frozen compatibility plan. This
/// adapter validates the boundary against the registry that the legacy parser
/// actually built and returns an immutable observation for diagnostics.
/// </summary>
public sealed record LegacyCompatibilityConsumptionSnapshot(
    string ProfileId,
    string PlanHash,
    string DialectHash,
    int InstructionDescriptorCount,
    int FunctionDescriptorCount,
    int PortCount,
    int CapabilityCount)
{
    public bool HasDescriptorOverrides =>
        InstructionDescriptorCount > 0 || FunctionDescriptorCount > 0;
}

public static class LegacyCompatibilityPlanConsumption
{
    public static LegacyCompatibilityConsumptionSnapshot Validate(
        CompatibilityPlan plan,
        IEnumerable<string> legacyInstructionNames,
        IEnumerable<string> legacyFunctionNames)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(legacyInstructionNames);
        ArgumentNullException.ThrowIfNull(legacyFunctionNames);

        var instructions = NormalizeLegacyNames(legacyInstructionNames, nameof(legacyInstructionNames));
        var functions = NormalizeLegacyNames(legacyFunctionNames, nameof(legacyFunctionNames));

        foreach (var descriptor in plan.Dialect.Instructions.Values)
        {
            EnsureSelectedModule(plan, descriptor.ModuleId, $"instruction '{descriptor.Name}'");
            if (!instructions.Contains(descriptor.Name))
            {
                throw new InvalidOperationException(
                    $"Compatibility plan instruction '{descriptor.Name}' is not registered by the legacy parser.");
            }
        }

        foreach (var descriptor in plan.Dialect.Functions.Values)
        {
            EnsureSelectedModule(plan, descriptor.ModuleId, $"function '{descriptor.Name}'");
            if (!functions.Contains(descriptor.Name))
            {
                throw new InvalidOperationException(
                    $"Compatibility plan function '{descriptor.Name}' is not registered by the legacy parser.");
            }
        }

        return new LegacyCompatibilityConsumptionSnapshot(
            plan.ProfileId,
            plan.CanonicalHash,
            plan.Dialect.CanonicalHash,
            plan.Dialect.Instructions.Count,
            plan.Dialect.Functions.Count,
            plan.Dialect.Ports.Count,
            plan.CapabilityIds.Count);
    }

    private static HashSet<string> NormalizeLegacyNames(
        IEnumerable<string> names,
        string parameterName)
    {
        var normalized = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in names)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Legacy registry contains an empty name.", parameterName);
            normalized.Add(name.Trim().ToUpperInvariant());
        }
        return normalized;
    }

    private static void EnsureSelectedModule(
        CompatibilityPlan plan,
        string moduleId,
        string descriptorLabel)
    {
        if (!plan.Dialect.Modules.Any(module =>
                string.Equals(module.ModuleId, moduleId, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                $"Compatibility plan {descriptorLabel} claims unselected module '{moduleId}'.");
        }
    }
}
