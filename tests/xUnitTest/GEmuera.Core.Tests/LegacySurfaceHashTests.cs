using GEmuera.Core.Compatibility.Packs;
using Xunit;

namespace GEmuera.Core.Tests;

/// <summary>
/// v24 基线表面哈希（<see cref="LegacySurfaceHash.ComputeV24SurfaceHash"/>）的稳定性钉子：
/// 期望值是仓库内固定的 64 位小写十六进制常量——生成清单
///（LegacyDialectInventories.Generated.cs）不变时哈希不变；清单一旦漂移（引擎注册表变化后
/// 未重新走 generator 流程），本测试先红，强制先再生清单再审视常量（E2-R6）。
/// </summary>
public class LegacySurfaceHashTests
{
    // 由 LegacyDialectInventories 当前内容（v24 指令/函数清单）经规范化算法一次算得后钉住。
    const string PinnedV24SurfaceHash = "8024d88063bd63d08d202e56565e353b7669c9c2b4506f235d8989214f0a78a1";

    [Fact]
    public void LegacySurfaceHash_V24_IsStable()
    {
        string hash = LegacySurfaceHash.ComputeV24SurfaceHash();

        // 形态约束：小写十六进制、恰好 64 字符（SHA256）。
        Assert.Matches("^[0-9a-f]{64}$", hash);

        // 钉住的常量：生成清单不变则恒等；清单变化时此处先红。
        Assert.Equal(PinnedV24SurfaceHash, hash);

        // 纯函数确定性：重复计算同值。
        Assert.Equal(hash, LegacySurfaceHash.ComputeV24SurfaceHash());
    }
}
