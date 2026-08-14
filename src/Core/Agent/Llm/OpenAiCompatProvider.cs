using System.Text;
using System.Text.Json;

namespace GEmuera.Core.Agent.Llm;

/// <summary>
/// OpenAI 兼容协议客户端（DeepSeek / GLM / Qwen 兼容模式 / Ollama /v1 / OpenRouter 等通用）。
/// 构造注入 HttpClient 便于测试替身；baseUrl/apiKey/model 三元组覆盖 95% provider。
/// 任何故障（网络/非 2xx/超时/解析失败）一律返回 Ok=false，不抛异常。
/// </summary>
public sealed class OpenAiCompatProvider : ILlmProvider
{
    readonly HttpClient http;
    readonly string baseUrl;
    readonly string apiKey;
    readonly string model;

    public OpenAiCompatProvider(HttpClient http, string baseUrl, string apiKey, string model)
    {
        this.http = http ?? throw new ArgumentNullException(nameof(http));
        this.baseUrl = (baseUrl ?? "").TrimEnd('/');
        this.apiKey = apiKey ?? "";
        this.model = model ?? "";
    }

    public async Task<LlmResult> CompleteAsync(LlmRequest request, CancellationToken ct)
    {
        if (request is null || request.Messages is null || request.Messages.Count == 0)
            return LlmResult.Failure("empty request");

        var payload = new
        {
            model,
            messages = request.Messages.Select(m => new { role = m.Role, content = m.Content }),
            max_tokens = request.MaxTokens
        };
        string body = JsonSerializer.Serialize(payload);

        HttpResponseMessage? response = null;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, baseUrl + "/chat/completions")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + apiKey);

            response = await http.SendAsync(req, ct).ConfigureAwait(false);
            string responseText = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
                return LlmResult.Failure($"http {(int)response.StatusCode}");

            return ParseResponse(responseText);
        }
        catch (OperationCanceledException)
        {
            return LlmResult.Failure("timeout");
        }
        catch (Exception error)
        {
            return LlmResult.Failure(error.GetType().Name.ToLowerInvariant() + ": " + error.Message);
        }
        finally
        {
            response?.Dispose();
        }
    }

    static LlmResult ParseResponse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                return LlmResult.Failure("no choices in response");

            var message = choices[0].TryGetProperty("message", out var msg) ? msg : default;
            string content = message.ValueKind != JsonValueKind.Undefined
                && message.TryGetProperty("content", out var contentEl)
                && contentEl.ValueKind == JsonValueKind.String
                    ? contentEl.GetString() ?? ""
                    : "";

            int tokens = root.TryGetProperty("usage", out var usage)
                && usage.TryGetProperty("total_tokens", out var tokenEl)
                && tokenEl.ValueKind == JsonValueKind.Number
                && tokenEl.TryGetInt32(out int t)
                    ? t : 0;

            return new LlmResult(true, content, string.Empty, tokens);
        }
        catch (JsonException error)
        {
            return LlmResult.Failure("bad json: " + error.Message);
        }
    }
}
