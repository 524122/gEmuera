using Emuera.Compatibility.Packs;
using GEmuera.Core.Compatibility;

namespace CompatPackHostBindingProbe;

/// <summary>
/// 宿主绑定探针（负向夹具）：故意引用宿主内部程序集 GEmuera.Core（CompatibilityPlan 类型），
/// 用于钉住兼容包 ALC 允许清单的 fail-closed 行为——包在绑定期只允许见到 Emuera 契约程序集、
/// BCL 与包目录依赖；宿主内部程序集（如 GEmuera.Core）必须不可见，不允许"返回 null 落回
/// 默认解析"把宿主程序集泄露给包。
/// Core 类型访问放在实例构造器内：JIT 构造器解析 TypeRef "GEmuera.Core" 时即触发绑定，
/// 保证绑定失败发生在 CompatPackLoader.TryLoad 的入口实例化阶段（Activator.CreateInstance），
/// 而非首次成员调用，避免半加载句柄逃逸。
/// </summary>
public sealed class HostBindingProbePack : ICompatPack
{
    public HostBindingProbePack()
    {
        // 绑定需求制造点：typeof(CompatibilityPlan) 的 TypeRef 属 AssemblyRef "GEmuera.Core"。
        Type planType = typeof(CompatibilityPlan);
        if (planType.Assembly.GetName().Name != "GEmuera.Core")
            throw new InvalidOperationException("探针前置事实失效：CompatibilityPlan 不在 GEmuera.Core。");
        if (!CompatPackManifest.TryLoadFromAssembly(typeof(HostBindingProbePack).Assembly, out var manifest, out _))
            throw new InvalidOperationException("探针内嵌清单缺失。");
        Manifest = manifest!;
    }

    public CompatPackManifest Manifest { get; }

    public IReadOnlyList<ICompatPackContribution> Contributions { get; } = new ICompatPackContribution[]
    {
        new ProbeSurfaceContribution(),
    };
}

/// <summary>合法表面贡献（只依赖契约类型）：确保探针被拒载的原因是宿主绑定，而非清单/贡献语义错误。</summary>
public sealed class ProbeSurfaceContribution : ISurfaceContribution
{
    public string ContributionId => "community.host-binding-probe.surface";

    public void Apply(IInstructionSurfaceRegistry instructions, IFunctionSurfaceRegistry functions)
    {
        instructions.RegisterInstruction("SETANIMETIMER");
    }
}
