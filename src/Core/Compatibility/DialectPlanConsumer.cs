using System.Collections.ObjectModel;

namespace GEmuera.Core.Compatibility;

/// <summary>Read-only projection used by Core consumers; no global profile lookup is possible.</summary>
public sealed class DialectPlanConsumer
{
    private readonly CompatibilityPlan _plan;
    private readonly IReadOnlyDictionary<string, InstructionDescriptor> _instructions;
    private readonly IReadOnlyDictionary<string, FunctionDescriptor> _functions;
    public DialectPlanConsumer(CompatibilityPlan plan)
    {
        _plan = plan ?? throw new ArgumentNullException(nameof(plan));
        _instructions = new ReadOnlyDictionary<string, InstructionDescriptor>(plan.Dialect.Instructions.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
        _functions = new ReadOnlyDictionary<string, FunctionDescriptor>(plan.Dialect.Functions.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
    }
    public string ProfileId => _plan.ProfileId;
    public string PlanHash => _plan.CanonicalHash;
    public IReadOnlyList<DialectModuleSnapshot> Modules => _plan.Dialect.Modules;
    public IReadOnlyDictionary<string, InstructionDescriptor> Instructions => _instructions;
    public IReadOnlyDictionary<string, FunctionDescriptor> Functions => _functions;
    public bool TryGetInstruction(string name, out InstructionDescriptor descriptor) => _instructions.TryGetValue(name.Trim().ToUpperInvariant(), out descriptor!);
    public bool TryGetFunction(string name, out FunctionDescriptor descriptor) => _functions.TryGetValue(name.Trim().ToUpperInvariant(), out descriptor!);
}
