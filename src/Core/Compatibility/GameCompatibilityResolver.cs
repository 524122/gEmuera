using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;

namespace GEmuera.Core.Compatibility;

/// <summary>
/// 置信度使用离散等级，保证 resolver 能解释选择原因；不使用会掩盖冲突的浮点总分。
/// </summary>
public enum GameCompatibilityConfidence
{
    Unknown,
    CapabilityOnly,
    StrongFallback,
    Verified,
}

public enum GameCompatibilityResolutionStatus
{
    Unknown,
    Resolved,
    Ambiguous,
    Invalid,
}

public sealed record GameBaseProbeEvidence
{
    public GameBaseProbeEvidence(
        long? gameCode,
        string? title,
        bool isReadable,
        string? sourceDigest = null)
    {
        if (gameCode is < 0)
            throw new ArgumentOutOfRangeException(nameof(gameCode));

        GameCode = gameCode;
        Title = NormalizeNullable(title);
        IsReadable = isReadable;
        SourceDigest = string.IsNullOrWhiteSpace(sourceDigest) ? null : sourceDigest.Trim();
    }

    public long? GameCode { get; }
    public string? Title { get; }
    public bool IsReadable { get; }
    public string? SourceDigest { get; }

    private static string? NormalizeNullable(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}

public sealed record GameCompatibilityAnchorEvidence
{
    public GameCompatibilityAnchorEvidence(string anchorId, bool matched)
    {
        if (string.IsNullOrWhiteSpace(anchorId))
            throw new ArgumentException("Anchor id is required.", nameof(anchorId));

        AnchorId = anchorId.Trim().ToLowerInvariant();
        Matched = matched;
    }

    public string AnchorId { get; }
    public bool Matched { get; }
}

/// <summary>
/// Host 生成的不带路径证据。Core 不读取游戏目录，也不会把未完成扫描当成“能力不存在”。
/// </summary>
public sealed class GameCompatibilityProbeEvidence
{
    public GameCompatibilityProbeEvidence(
        long probeId,
        GameBaseProbeEvidence? gameBase,
        IEnumerable<GameCompatibilityAnchorEvidence>? anchors = null,
        IEnumerable<string>? warnings = null)
    {
        if (probeId <= 0)
            throw new ArgumentOutOfRangeException(nameof(probeId));

        ProbeId = probeId;
        GameBase = gameBase;
        Anchors = new ReadOnlyCollection<GameCompatibilityAnchorEvidence>(
            (anchors ?? Array.Empty<GameCompatibilityAnchorEvidence>())
                .Select(anchor => anchor ?? throw new ArgumentException("Anchors cannot contain null.", nameof(anchors)))
                .ToArray());
        Warnings = new ReadOnlyCollection<string>(
            (warnings ?? Array.Empty<string>())
                .Where(warning => !string.IsNullOrWhiteSpace(warning))
                .Select(warning => warning.Trim())
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray());
    }

    public long ProbeId { get; }
    public GameBaseProbeEvidence? GameBase { get; }
    public IReadOnlyList<GameCompatibilityAnchorEvidence> Anchors { get; }
    public IReadOnlyList<string> Warnings { get; }
}

public sealed record GameCompatibilityResolution
{
    public GameCompatibilityResolution(
        GameCompatibilityResolutionStatus status,
        GameCompatibilityConfidence confidence,
        string? gameFamilyId,
        string profileId,
        string ruleId,
        string reasonCode,
        IEnumerable<string>? evidenceIds = null)
    {
        Status = status;
        Confidence = confidence;
        GameFamilyId = string.IsNullOrWhiteSpace(gameFamilyId) ? null : gameFamilyId.Trim();
        ProfileId = ContractText.RequiredIdentifier(profileId, nameof(profileId));
        RuleId = ContractText.RequiredIdentifier(ruleId, nameof(ruleId));
        ReasonCode = ContractText.RequiredIdentifier(reasonCode, nameof(reasonCode));
        EvidenceIds = new ReadOnlyCollection<string>(
            (evidenceIds ?? Array.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim().ToLowerInvariant())
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray());
    }

    public GameCompatibilityResolutionStatus Status { get; }
    public GameCompatibilityConfidence Confidence { get; }
    public string? GameFamilyId { get; }
    public string ProfileId { get; }
    public string RuleId { get; }
    public string ReasonCode { get; }
    public IReadOnlyList<string> EvidenceIds { get; }

    /// <summary>
    /// 只有这些结果可以覆盖启动器标签的旧 profile 猜测；未知/冲突内容保留用户显式回退。
    /// </summary>
    public bool CanAutoSelect => Status == GameCompatibilityResolutionStatus.Resolved
        && Confidence is GameCompatibilityConfidence.Verified or GameCompatibilityConfidence.StrongFallback;
}

/// <summary>
/// 第一版自动选择切片的确定性内建规则。规则只能选择已有 profile，
/// 不在这里安装游戏专属 handler，也不允许游戏内容定义新规则。
/// </summary>
public static class BuiltInGameCompatibilityResolver
{
    public const string EraFlGameFamilyId = "erafl";
    public const string EraTwGameFamilyId = "eratw";
    public const string V24ProfileId = "v24pure";
    public const string SnakeProfileId = "snake";
    public const string EraFlProfileId = "erafl";

    private const long EraFlGameCode = 9224518;
    private const long EraTwGameCode = 7153;

    public static GameCompatibilityResolution Resolve(GameCompatibilityProbeEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        var matchedAnchors = new HashSet<string>(
            evidence.Anchors.Where(anchor => anchor.Matched).Select(anchor => anchor.AnchorId),
            StringComparer.OrdinalIgnoreCase);
        string normalizedTitle = NormalizeTitle(evidence.GameBase?.Title);
        bool hasEraFlEvidence = normalizedTitle == "ERAFL"
            || matchedAnchors.Contains("erafl.system-title")
            || matchedAnchors.Contains("erafl.init-loader")
            || matchedAnchors.Contains("erafl.usercom");
        bool hasEraTwEvidence = normalizedTitle.StartsWith("ERATHEWORLD", StringComparison.Ordinal)
            || matchedAnchors.Contains("eratw.version");

        long? code = evidence.GameBase?.GameCode;
        if (code == EraFlGameCode)
        {
            if (hasEraTwEvidence && !hasEraFlEvidence)
                return Ambiguous("builtin.erafl.v1", "game-code-title-conflict", "gamecode.9224518");
            if (!hasEraFlEvidence)
                return Unknown("builtin.erafl.v1", "game-code-without-corroboration");

            return Resolved(
                EraFlGameFamilyId,
                EraFlProfileId,
                GameCompatibilityConfidence.Verified,
                "builtin.erafl.v1",
                "erafl-profile",
                "gamecode.9224518",
                "erafl.title");
        }

        if (code == EraTwGameCode)
        {
            if (hasEraFlEvidence && !hasEraTwEvidence)
                return Ambiguous("builtin.eratw-snake.v1", "game-code-title-conflict", "gamecode.7153");
            if (!hasEraTwEvidence)
                return Unknown("builtin.eratw-snake.v1", "game-code-without-corroboration");

            return Resolved(
                EraTwGameFamilyId,
                SnakeProfileId,
                GameCompatibilityConfidence.Verified,
                "builtin.eratw-snake.v1",
                "eratw-snake-profile",
                "gamecode.7153",
                "eratw.title");
        }

        // GAMEBASE 缺失或代码为 0 都没有身份价值；只有规范化标题加两个独立锚点
        // 才允许进入 fallback，避免把单个能力 token 误当成游戏身份。
        // 无 GAMEBASE 时，标题证据必须来自脚本或 GAMEBASE 标题，并且还要有独立锚点。
        int eraFlAnchorCount = CountMatches(matchedAnchors, "erafl.system-title", "erafl.init-loader", "erafl.usercom");
        if (normalizedTitle == "ERAFL" && eraFlAnchorCount >= 2)
        {
            return Resolved(
                EraFlGameFamilyId,
                EraFlProfileId,
                GameCompatibilityConfidence.StrongFallback,
                "builtin.erafl.v1",
                "erafl-profile",
                "erafl.title",
                "erafl.fallback-anchors");
        }

        int eraTwAnchorCount = CountMatches(matchedAnchors, "eratw.version", "eratw.system-entry", "eratw.title");
        bool hasEraTwTitleAnchor = normalizedTitle.StartsWith("ERATHEWORLD", StringComparison.Ordinal)
            || matchedAnchors.Contains("eratw.title");
        if (hasEraTwTitleAnchor && eraTwAnchorCount >= 2)
        {
            return Resolved(
                EraTwGameFamilyId,
                SnakeProfileId,
                GameCompatibilityConfidence.StrongFallback,
                "builtin.eratw-snake.v1",
                "eratw-snake-profile",
                "eratw.title",
                "eratw.fallback-anchors");
        }

        return Unknown("builtin.game-identity.v1", "insufficient-identity-evidence");
    }

    public static string NormalizeTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return string.Empty;

        var builder = new StringBuilder(title.Length);
        foreach (char character in title.Normalize(NormalizationForm.FormKC))
        {
            if (char.IsLetterOrDigit(character))
                builder.Append(char.ToUpperInvariant(character));
        }
        return builder.ToString();
    }

    private static int CountMatches(HashSet<string> anchors, params string[] ids)
    {
        return ids.Count(anchors.Contains);
    }

    private static GameCompatibilityResolution Resolved(
        string gameFamilyId,
        string profileId,
        GameCompatibilityConfidence confidence,
        string ruleId,
        string reasonCode,
        params string[] evidenceIds)
    {
        return new GameCompatibilityResolution(
            GameCompatibilityResolutionStatus.Resolved,
            confidence,
            gameFamilyId,
            profileId,
            ruleId,
            reasonCode,
            evidenceIds);
    }

    private static GameCompatibilityResolution Unknown(string ruleId, string reasonCode)
    {
        return new GameCompatibilityResolution(
            GameCompatibilityResolutionStatus.Unknown,
            GameCompatibilityConfidence.Unknown,
            null,
            V24ProfileId,
            ruleId,
            reasonCode);
    }

    private static GameCompatibilityResolution Ambiguous(string ruleId, string reasonCode, params string[] evidenceIds)
    {
        return new GameCompatibilityResolution(
            GameCompatibilityResolutionStatus.Ambiguous,
            GameCompatibilityConfidence.Unknown,
            null,
            V24ProfileId,
            ruleId,
            reasonCode,
            evidenceIds);
    }
}
