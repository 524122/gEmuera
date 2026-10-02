namespace CommunityPackTemplate;

/// <summary>
/// 社区数据包模板的定位标记：仅用于测试/宿主取得包程序集路径（typeof(...).Assembly.Location）。
/// 本程序集故意不包含 ICompatPack 实现类——manifest-only 数据包不需要任何 C# 入口，
/// 表面/能力声明全部来自内嵌的 compatpack.manifest.json。
/// </summary>
public sealed class CommunityPackTemplateMarker
{
}
