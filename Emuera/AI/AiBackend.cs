using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace MinorShift.Emuera.AI;

/// <summary>
/// OpenAI Chat Completions 协议的非流式客户端。
/// 仅使用 BCL 自带的 HttpClient + System.Text.Json，不新增 NuGet 依赖。
/// 密钥从 AiConfig 获取，不在任何日志中暴露。
/// </summary>
internal static class AiBackend
{
    private static readonly HttpClient httpClient = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    /// <summary>
    /// 发起一次 Chat Completions 请求（非流式）。
    /// messages 是完整的对话历史（含 system/user/assistant）。
    /// 返回 assistant 回复正文；失败抛出异常。
    /// </summary>
    public static async Task<string> ChatAsync(
        IReadOnlyList<ChatMessage> messages,
        CancellationToken token)
    {
        string apiKey = AiConfig.GetApiKeyPlain();
        if (string.IsNullOrEmpty(apiKey))
            throw new InvalidOperationException("API Key 未设置");

        string endpoint = AiConfig.ApiEndpoint;
        if (string.IsNullOrWhiteSpace(endpoint))
            throw new InvalidOperationException("API 端点未设置");

        var requestBody = new ChatRequest
        {
            Model = AiConfig.Model ?? "gpt-4o-mini",
            Messages = messages,
            MaxTokens = AiConfig.MaxTokens > 0 ? AiConfig.MaxTokens : 600,
            Temperature = AiConfig.Temperature,
            Stream = false,
        };

        string json = JsonSerializer.Serialize(requestBody, SerializerOptions);

        string responseBody = await PostAsync(
            endpoint, apiKey, json,
            AiConfig.TimeoutSeconds > 0 ? AiConfig.TimeoutSeconds : 30,
            Math.Max(0, AiConfig.MaxRetries),
            token).ConfigureAwait(false);

        string content = TryExtractContent(responseBody);
        if (content == null)
            throw new InvalidOperationException("无法解析 API 响应中的 content 字段");
        return content;
    }

    /// <summary>
    /// 副 API（计算通道）请求。用 function calling 约束输出，返回函数调用参数的原始 JSON 字符串。
    ///
    /// 为什么用 function calling 而不是 response_format：兼容性更好（多数第三方兼容端点支持 tools，
    /// 但对 json_schema 的支持参差不齐），且能顺带拿到 tool_choice 强制调用。
    /// 端点不支持 tools 时会在响应里退化成普通 content，这里对两种形态都做了解析。
    /// </summary>
    public static async Task<string> ComputeAsync(
        IReadOnlyList<ChatMessage> messages,
        string functionName,
        string parametersSchemaJson,
        CancellationToken token)
    {
        string apiKey = AiConfig.GetComputeApiKeyPlain();
        if (string.IsNullOrEmpty(apiKey))
            throw new InvalidOperationException("副 API Key 未设置");

        string endpoint = AiConfig.ComputeApiEndpoint;
        if (string.IsNullOrWhiteSpace(endpoint))
            throw new InvalidOperationException("副 API 端点未设置");

        string json = BuildComputeRequestJson(messages, functionName, parametersSchemaJson);

        string responseBody = await PostAsync(
            endpoint, apiKey, json,
            AiConfig.ComputeTimeoutSeconds > 0 ? AiConfig.ComputeTimeoutSeconds : 20,
            Math.Max(0, AiConfig.ComputeMaxRetries),
            token).ConfigureAwait(false);

        string arguments = TryExtractToolArguments(responseBody, functionName);
        if (arguments != null)
            return arguments;

        // 端点不支持 tools 时的退路：模型把 JSON 写在 content 里。
        string content = TryExtractContent(responseBody);
        string stripped = StripCodeFence(content);
        if (!string.IsNullOrWhiteSpace(stripped))
            return stripped;

        throw new InvalidOperationException("副 API 响应里既没有 tool_calls 也没有可用的 content");
    }

    /// <summary>
    /// 手写请求体而不是靠 POCO 序列化：schema 是运行期从 ai_traits.json 生成的动态 JSON，
    /// 用 POCO 就得为它套一层 JsonElement，反而更绕。
    /// </summary>
    private static string BuildComputeRequestJson(
        IReadOnlyList<ChatMessage> messages,
        string functionName,
        string parametersSchemaJson)
    {
        var buffer = new System.IO.MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }))
        {
            writer.WriteStartObject();
            writer.WriteString("model", AiConfig.ComputeModel ?? "gpt-4o-mini");

            writer.WriteStartArray("messages");
            foreach (ChatMessage m in messages)
            {
                writer.WriteStartObject();
                writer.WriteString("role", m.Role);
                writer.WriteString("content", m.Content ?? "");
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            writer.WriteNumber("max_tokens", AiConfig.ComputeMaxTokens > 0 ? AiConfig.ComputeMaxTokens : 800);
            // 计算通道要的是确定性，不是创造力。温度固定为 0，不开放配置。
            writer.WriteNumber("temperature", 0);
            writer.WriteBoolean("stream", false);

            writer.WriteStartArray("tools");
            writer.WriteStartObject();
            writer.WriteString("type", "function");
            writer.WriteStartObject("function");
            writer.WriteString("name", functionName);
            writer.WriteString("description", "把本轮事件换算成 ERA 数值变更并提交。");
            writer.WritePropertyName("parameters");
            using (var schema = JsonDocument.Parse(parametersSchemaJson))
                schema.RootElement.WriteTo(writer);
            writer.WriteEndObject();
            writer.WriteEndObject();
            writer.WriteEndArray();

            writer.WriteStartObject("tool_choice");
            writer.WriteString("type", "function");
            writer.WriteStartObject("function");
            writer.WriteString("name", functionName);
            writer.WriteEndObject();
            writer.WriteEndObject();

            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>
    /// 共用的 POST + 超时 + 重试。主副 API 各带自己的超时与重试次数，
    /// 但重试语义必须一致（429 与 5xx 才重试，4xx 立即失败），否则两条通道的失败表现会不一样。
    /// </summary>
    private static async Task<string> PostAsync(
        string endpoint,
        string apiKey,
        string json,
        int timeoutSec,
        int maxRetries,
        CancellationToken token)
    {
        Exception lastError = null;

        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            token.ThrowIfCancellationRequested();

            if (attempt > 0)
                await Task.Delay(Math.Min(attempt * 1000, 3000), token).ConfigureAwait(false);

            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
                cts.CancelAfter(TimeSpan.FromSeconds(timeoutSec));

                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");

                using var response = await httpClient.SendAsync(request, cts.Token).ConfigureAwait(false);

                string responseBody = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    string errorMsg = TryExtractErrorMessage(responseBody) ?? $"HTTP {(int)response.StatusCode}";
                    if ((int)response.StatusCode == 429 || (int)response.StatusCode >= 500)
                    {
                        lastError = new HttpRequestException(errorMsg);
                        continue;
                    }
                    throw new HttpRequestException(errorMsg);
                }

                return responseBody;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                lastError = new TimeoutException($"请求超时（{timeoutSec}秒）");
                continue;
            }
            catch (HttpRequestException e)
            {
                lastError = e;
                if (attempt < maxRetries)
                    continue;
            }
        }

        throw lastError ?? new InvalidOperationException("请求失败（未知原因）");
    }

    /// <summary>取出首个 tool_call 的 arguments。函数名不匹配时返回 null，避免解析到别的工具调用。</summary>
    private static string TryExtractToolArguments(string json, string functionName)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                return null;
            if (!choices[0].TryGetProperty("message", out var message))
                return null;
            if (!message.TryGetProperty("tool_calls", out var calls) || calls.GetArrayLength() == 0)
                return null;

            foreach (var call in calls.EnumerateArray())
            {
                if (!call.TryGetProperty("function", out var fn))
                    continue;
                if (fn.TryGetProperty("name", out var name) &&
                    !string.Equals(name.GetString(), functionName, StringComparison.Ordinal))
                    continue;
                if (fn.TryGetProperty("arguments", out var args))
                {
                    string text = args.ValueKind == JsonValueKind.String ? args.GetString() : args.GetRawText();
                    if (!string.IsNullOrWhiteSpace(text))
                        return text;
                }
            }
        }
        catch
        {
        }
        return null;
    }

    /// <summary>剥掉 ```json ``` 围栏。模型在不支持 tools 的端点上很常这么包一层。</summary>
    private static string StripCodeFence(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;
        string trimmed = text.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
            return trimmed;
        int firstBreak = trimmed.IndexOf('\n');
        if (firstBreak < 0)
            return trimmed;
        string body = trimmed[(firstBreak + 1)..];
        int fence = body.LastIndexOf("```", StringComparison.Ordinal);
        return (fence >= 0 ? body[..fence] : body).Trim();
    }

    private static string TryExtractContent(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
            {
                var first = choices[0];
                if (first.TryGetProperty("message", out var message) &&
                    message.TryGetProperty("content", out var content))
                {
                    return content.GetString();
                }
            }
        }
        catch
        {
        }
        return null;
    }

    private static string TryExtractErrorMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("error", out var error) &&
                error.TryGetProperty("message", out var msg))
            {
                string text = msg.GetString();
                return SanitizeError(text);
            }
        }
        catch
        {
        }
        return null;
    }

    /// <summary>从错误消息中移除可能的密钥片段。主副两把密钥都要过一遍。</summary>
    private static string SanitizeError(string message)
    {
        if (string.IsNullOrEmpty(message))
            return message;
        foreach (string key in new[] { AiConfig.GetApiKeyPlain(), AiConfig.GetComputeApiKeyPlain() })
        {
            if (!string.IsNullOrEmpty(key) && message.Contains(key))
                message = message.Replace(key, "[REDACTED]");
        }
        return message;
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

internal sealed class ChatMessage
{
    [JsonPropertyName("role")]
    public string Role { get; set; }

    [JsonPropertyName("content")]
    public string Content { get; set; }
}

internal sealed class ChatRequest
{
    [JsonPropertyName("model")]
    public string Model { get; set; }

    [JsonPropertyName("messages")]
    public IReadOnlyList<ChatMessage> Messages { get; set; }

    [JsonPropertyName("max_tokens")]
    public int MaxTokens { get; set; }

    [JsonPropertyName("temperature")]
    public double Temperature { get; set; }

    [JsonPropertyName("stream")]
    public bool Stream { get; set; }
}
