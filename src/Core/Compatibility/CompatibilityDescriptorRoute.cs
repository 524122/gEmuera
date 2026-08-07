using System.Collections.ObjectModel;

namespace GEmuera.Core.Compatibility;

/// <summary>
/// Creates the immutable adapter view from a frozen descriptor plan to an
/// already-built legacy handler registry. The adapter never creates or
/// replaces handlers; it only makes the selected descriptor keys reachable.
/// </summary>
public sealed class CompatibilityDescriptorRoute<TInstruction, TFunction>
{
    private CompatibilityDescriptorRoute(
        IReadOnlyDictionary<string, TInstruction> instructions,
        IReadOnlyDictionary<string, TFunction> functions)
    {
        Instructions = instructions;
        Functions = functions;
    }

    public IReadOnlyDictionary<string, TInstruction> Instructions { get; }
    public IReadOnlyDictionary<string, TFunction> Functions { get; }

    public static CompatibilityDescriptorRoute<TInstruction, TFunction> Create(
        CompatibilityPlan plan,
        IReadOnlyDictionary<string, TInstruction> legacyInstructions,
        IReadOnlyDictionary<string, TFunction> legacyFunctions)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(legacyInstructions);
        ArgumentNullException.ThrowIfNull(legacyFunctions);

        // Keep the comparer of the legacy handler registry so mixed-case
        // instruction spellings resolve exactly as the upstream engine does.
        var instructions = new Dictionary<string, TInstruction>(
            GetLegacyComparer(legacyInstructions));
        foreach (var descriptor in plan.Dialect.Instructions.Values)
        {
            if (!legacyInstructions.TryGetValue(descriptor.Name, out var handler))
            {
                throw new InvalidOperationException(
                    $"Compatibility plan instruction '{descriptor.Name}' is not registered by the legacy parser.");
            }
            instructions.Add(descriptor.Name, handler);
        }

        var functions = new Dictionary<string, TFunction>(
            GetLegacyComparer(legacyFunctions));
        foreach (var descriptor in plan.Dialect.Functions.Values)
        {
            if (!legacyFunctions.TryGetValue(descriptor.Name, out var handler))
            {
                throw new InvalidOperationException(
                    $"Compatibility plan function '{descriptor.Name}' is not registered by the legacy parser.");
            }
            functions.Add(descriptor.Name, handler);
        }

        return new CompatibilityDescriptorRoute<TInstruction, TFunction>(
            new ReadOnlyDictionary<string, TInstruction>(instructions),
            new ReadOnlyDictionary<string, TFunction>(functions));
    }

    private static IEqualityComparer<string> GetLegacyComparer<TValue>(IReadOnlyDictionary<string, TValue> registry)
    {
        return registry is Dictionary<string, TValue> dictionary
            ? dictionary.Comparer
            : StringComparer.Ordinal;
    }
}
