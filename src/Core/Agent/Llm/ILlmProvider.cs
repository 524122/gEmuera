namespace GEmuera.Core.Agent.Llm;

/// <summary>单条对话消息（role: system / user / assistant）。</summary>
public sealed record LlmChatMessage(string Role, string Content);

/// <summary>一次补全请求：消息列表 + max_tokens 上限。</summary>
public sealed record LlmRequest(IReadOnlyList<LlmChatMessage> Messages, int MaxTokens);

/// <summary>
/// 补全结果。失败永远以 Ok=false 表达（Error 带原因），禁止抛异常穿透——
/// 离线兜底不变量要求调用方在 provider 任何故障下都能走原版降级路径。
/// </summary>
public sealed record LlmResult(bool Ok, string Content, string Error, int TokensUsed)
{
    public static LlmResult Failure(string error) => new(false, string.Empty, error, 0);
}

/// <summary>LLM Provider 抽象。统一 OpenAI 兼容协议（/chat/completions）。</summary>
public interface ILlmProvider
{
    Task<LlmResult> CompleteAsync(LlmRequest request, CancellationToken ct);
}
