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

        var instructions = new Dictionary<string, TInstruction>(StringComparer.Ordinal);
        foreach (var descriptor in plan.Dialect.Instructions.Values)
        {
            if (!legacyInstructions.TryGetValue(descriptor.Name, out var handler))
            {
                throw new InvalidOperationException(
                    $"Compatibility plan instruction '{descriptor.Name}' is not registered by the legacy parser.");
            }
            instructions.Add(descriptor.Name, handler);
        }

        var functions = new Dictionary<string, TFunction>(StringComparer.Ordinal);
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
}
