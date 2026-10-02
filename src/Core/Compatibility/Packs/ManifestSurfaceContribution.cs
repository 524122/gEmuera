using Emuera.Compatibility.Packs;

namespace GEmuera.Core.Compatibility.Packs;

/// <summary>
/// 将 manifest 的静态表面名单（add/hide instructions/functions）适配成标准
/// ISurfaceContribution。这样数据包无需自带任何 C# 入口代码，也能参与规则校验与组装；
/// 同时保证校验/组装两段回放使用同一份声明数据（不会出现程序集代码与清单漂移）。
/// </summary>
internal sealed class ManifestSurfaceContribution : ICompatPackContribution, ISurfaceContribution
{
    readonly CompatPackSurface surface;
    readonly IReadOnlyDictionary<string, string> knownFunctionReturnTypes;

    public ManifestSurfaceContribution(
        CompatPackSurface surface,
        IReadOnlyDictionary<string, string> knownFunctionReturnTypes)
    {
        this.surface = surface ?? throw new ArgumentNullException(nameof(surface));
        this.knownFunctionReturnTypes = knownFunctionReturnTypes
            ?? throw new ArgumentNullException(nameof(knownFunctionReturnTypes));
    }

    public string ContributionId => "manifest.surface";

    public void Apply(IInstructionSurfaceRegistry instructions, IFunctionSurfaceRegistry functions)
    {
        ArgumentNullException.ThrowIfNull(instructions);
        ArgumentNullException.ThrowIfNull(functions);

        foreach (string name in surface.AddInstructions)
            instructions.RegisterInstruction(name);
        foreach (string name in surface.HideInstructions)
            instructions.HideInstruction(name);
        foreach (string name in surface.AddFunctions)
        {
            string normalized = (name ?? "").Trim().ToUpperInvariant();
            string returnType = normalized.Length > 0
                && knownFunctionReturnTypes.TryGetValue(normalized, out string? known)
                ? known
                : "Unknown";
            functions.RegisterFunction(name, returnType);
        }
        foreach (string name in surface.HideFunctions)
            functions.HideFunction(name);
    }
}

/// <summary>
/// 无程序集入口代码的数据包入口：只承载内嵌清单；真正的表面贡献由加载器按清单合成。
/// 兼容包协议仍要求程序集 + 内嵌清单，但不强制包作者编写/执行任何 C# 类型。
/// </summary>
internal sealed class ManifestOnlyCompatPack : ICompatPack
{
    public ManifestOnlyCompatPack(CompatPackManifest manifest)
    {
        Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
    }

    public CompatPackManifest Manifest { get; }

    public IReadOnlyList<ICompatPackContribution> Contributions { get; }
        = Array.Empty<ICompatPackContribution>();
}
