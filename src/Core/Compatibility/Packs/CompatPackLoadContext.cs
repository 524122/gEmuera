using System.Reflection;
using System.Runtime.Loader;

namespace GEmuera.Core.Compatibility.Packs;

/// <summary>
/// 每包一个的可回收加载上下文（isCollectible，会话边界 Unload）。契约-only 允许清单
/// （fail-closed，docs/designs/compat-pack-interface.md §5.2；与宿主 PluginLoadContext
/// （Scripts/Emuera/Runtime/Utils/PluginSystem）同源的姊妹实现，规则更严）：
///   1. Emuera/emuera → 契约程序集（src/EmueraFacade，与包共享同一类型标识；.NET 绑定
///      按简单名匹配，2026-09-04 Phase C 复现实验）；
///   2. netstandard/System.*/Microsoft.* → 宿主已加载的同名框架程序集（不在回调里发起
///      绑定；未命中返回 null 由运行时默认解析接手，BCL 名字无宿主泄露面）；
///   3. 其余名字 → 包目录探测 &lt;name&gt;.dll，命中即仅在本 ALC 内加载；
///   4. 仍未命中 → 抛出 FileLoadException，不再返回 null 落回运行时默认解析（默认解析
///      可见宿主全部已加载程序集，等于信任边界 §6 的"可见面"约束失效）。
///   宿主内部程序集（如 GEmuera.Core）对包不可见：包需要 Core 数据类型时必须经契约
///   程序集暴露或签新契约，不允许 ALC 回落（2026-10-03 硬化，探针测试钉住）。
/// </summary>
internal sealed class CompatPackLoadContext : AssemblyLoadContext
{
    static readonly Assembly contractAssembly = typeof(Emuera.Compatibility.Packs.ICompatPack).Assembly;

    readonly string packDirectory;

    public CompatPackLoadContext(string packAssemblyPath)
        : base(name: "CompatPack:" + Path.GetFileNameWithoutExtension(packAssemblyPath), isCollectible: true)
    {
        packDirectory = Path.GetDirectoryName(Path.GetFullPath(packAssemblyPath)) ?? string.Empty;
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        string name = assemblyName.Name ?? "";
        if (name == "Emuera" || name == "emuera")
            return contractAssembly;
        if (name == "netstandard" || name.StartsWith("System.", StringComparison.Ordinal)
            || name.StartsWith("Microsoft.", StringComparison.Ordinal))
        {
            foreach (Assembly loaded in AssemblyLoadContext.Default.Assemblies)
            {
                if (string.Equals(loaded.GetName().Name, name, StringComparison.Ordinal))
                    return loaded;
            }
            return null;
        }
        string candidate = Path.Combine(packDirectory, name + ".dll");
        if (File.Exists(candidate))
            return LoadFromAssemblyPath(candidate);
        throw new FileLoadException(
            "兼容包绑定了允许清单之外的程序集 '" + name + "'：包只允许引用 Emuera 契约程序集"
            + "（Emuera/emuera）、BCL（netstandard/System.*/Microsoft.*）与包目录内的依赖程序集；"
            + "宿主内部程序集（如 GEmuera.Core）对包不可见。需要 Core 数据类型时请经契约程序集"
            + "暴露或另签契约（docs/designs/compat-pack-interface.md §5.2）。", name);
    }
}
