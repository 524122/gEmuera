using GEmuera.Core.Compatibility;
using GEmuera.Core.Compatibility.Packs;
using Xunit;

namespace GEmuera.Core.Tests;

/// <summary>
/// 会话持有 pack set 与确定性 Unload（docs/designs/compat-pack-interface.md §5.4，
/// 计划 E2-R2）：成功加载的句柄集合由会话绑定对象独占，Dispose 幂等且对每个租约
/// 恰好调用一次 Unload——把包程序集的存活从 GC 猜测改为确定性边界。
/// </summary>
public class CompatPackSessionTests
{
    private sealed class CountingLease : ICompatPackLease
    {
        public int UnloadCount { get; private set; }

        public void Unload() => UnloadCount++;
    }

    [Fact]
    public void Dispose_UnloadsEachLeaseOnceIdempotently()
    {
        CompatibilityPlan plan = BuiltInDialectCatalog.CreateLegacySessionPlan("v24pure");
        var leases = new[] { new CountingLease(), new CountingLease(), new CountingLease() };

        using var session = new CompatPackSession(plan, leases);

        Assert.False(session.IsDisposed);
        session.Dispose();
        Assert.True(session.IsDisposed);
        session.Dispose();
        Assert.All(leases, lease => Assert.Equal(1, lease.UnloadCount));
    }
}
