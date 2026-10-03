using System.Threading;
using Emuera.Compatibility.Packs;
using GEmuera.Core.Compatibility;

namespace GEmuera.Core.Compatibility.Packs;

/// <summary>
/// 会话边界的包程序集租约：能被确定性回收的包资源抽象（ALC、入口实例等）。
/// <see cref="CompatPackHandle"/> 与 <see cref="CompatPackSet"/> 都实现本接口，
/// 会话对象只依赖该抽象完成逐一回收（设计 §5.4）。
/// </summary>
public interface ICompatPackLease
{
    /// <summary>回收租约持有的包程序集资源。实现必须幂等（重复调用为 no-op，不得抛出）。</summary>
    void Unload();
}

/// <summary>
/// 一次会话成功加载的包集合所有者（设计 §5.4，计划 E2-R2）：plan 是纯数据，但包
/// 程序集（ALC/入口实例/委托）需要确定性的存活边界。宿主把一次成功加载的句柄集合
/// 包成本对象并与计划一同绑定到会话；Stop/Restart/退出时 <see cref="Dispose"/> 对
/// 每个租约恰好调用一次 Unload（<see cref="Interlocked"/> 保证幂等）。未启用任何包
/// 的会话不构造本对象，天然回到纯 v24。
/// </summary>
public sealed class CompatPackSession : IDisposable
{
    private readonly ICompatPackLease[] leases;
    private readonly IReadOnlyList<CompatPackHandle> handles;
    private int disposed;

    /// <summary>
    /// 接管一次成功加载的句柄集合：此后句柄由本对象独占释放，调用方不得再自行
    /// UnloadAll（双重回收由幂等兜底，但所有权归属本对象）。
    /// </summary>
    public CompatPackSession(CompatibilityPlan plan, IReadOnlyList<CompatPackHandle> handles)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(handles);
        Plan = plan;
        this.handles = handles;
        leases = handles.Where(handle => handle is not null).Cast<ICompatPackLease>().ToArray();
    }

    /// <summary>
    /// 测试 seam：以任意租约构造（Unload 计数验证）；此时 <see cref="Handles"/> 为空。
    /// 宿主接线一律走句柄构造器。
    /// </summary>
    internal CompatPackSession(CompatibilityPlan plan, IReadOnlyList<ICompatPackLease> leases)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(leases);
        Plan = plan;
        handles = Array.Empty<CompatPackHandle>();
        this.leases = leases.Where(lease => lease is not null).ToArray();
    }

    /// <summary>本会话绑定的组装计划（纯数据，与句柄同一次加载产出）。</summary>
    public CompatibilityPlan Plan { get; }

    /// <summary>会话持有的句柄清单（测试 seam 构造时为空清单）。</summary>
    public IReadOnlyList<CompatPackHandle> Handles => handles;

    /// <summary>是否已释放；幂等 Dispose 只对首次调用生效。</summary>
    public bool IsDisposed => Volatile.Read(ref disposed) != 0;

    /// <summary>对每个租约恰好调用一次 Unload；重复调用为 no-op。首次调用后本实例作废。</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;
        foreach (ICompatPackLease lease in leases)
            lease.Unload();
    }
}
