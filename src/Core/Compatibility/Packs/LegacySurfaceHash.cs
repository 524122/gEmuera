using System.Security.Cryptography;
using System.Text;
using GEmuera.Core.Compatibility;

namespace GEmuera.Core.Compatibility.Packs;

/// <summary>
/// v24 基线表面快照哈希（E2-R6）：把生成清单 <see cref="LegacyDialectInventories"/> 的
/// v24 指令/函数名册折叠成一个确定性 SHA256 指纹，供兼容包 manifest 的
/// <c>baseSurfaceHash</c> 精确对账（声明非空且不等于当前基线即拒载）。
/// 唯一事实源是引擎投影（生成清单由 LegacyDialectInventoryGenerator 再生）；本哈希是镜像的
/// 指纹——生成清单任何变化都会改变哈希值，仓内钉子测试（LegacySurfaceHashTests）先红，
/// 强制先走 generator 流程再审视包声明的哈希。
/// </summary>
public static class LegacySurfaceHash
{
    /// <summary>
    /// 计算 v24 基线表面哈希。规范化算法（第三方可用任意语言复算）：
    /// <list type="number">
    /// <item>取 <see cref="LegacyDialectInventories.V24InstructionNames"/>，按 Ordinal 排序；</item>
    /// <item>取 <see cref="LegacyDialectInventories.V24Functions"/>，每条折叠为
    /// <c>Name|ReturnType</c> 复合串，按 Ordinal 排序；</item>
    /// <item>拼接文本（UTF-8、LF、无 BOM）：
    /// <c>instruction=&lt;名1&gt;,&lt;名2&gt;,…\nfunction=&lt;名|返回类型&gt;,&lt;…&gt;\n</c>
    /// （两行均以单个 \n 结尾；逗号分隔、无空格）。</item>
    /// <item>对文本取 SHA256，输出小写十六进制 64 字符。</item>
    /// </list>
    /// 工作示例（v24 清单当前内容下，文本首行以
    /// <c>instruction=ABS,ADDCHARA,ADDCOPYCHARA,…</c> 开头，第二行以
    /// <c>function=ABS|Integer,ALLSAMES|Integer,ARRAYMSORT|Integer,…</c> 开头）。
    /// 纯函数：同一生成清单输入恒得同一输出，不读取文件/环境/时钟。
    /// </summary>
    public static string ComputeV24SurfaceHash()
    {
        // OrderBy 产出新序列，不改写生成清单的静态数组本体。
        string[] instructions = LegacyDialectInventories.V24InstructionNames
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        string[] functions = LegacyDialectInventories.V24Functions
            .Select(entry => entry.Name + "|" + entry.ReturnType)
            .OrderBy(composite => composite, StringComparer.Ordinal)
            .ToArray();

        string text = "instruction=" + string.Join(",", instructions) + "\n"
            + "function=" + string.Join(",", functions) + "\n";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }
}
