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

        string endpoint = DeriveChatUrl(AiConfig.ApiEndpoint);
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
        {
            // 走到这里说明 HTTP 是成功的但响应体不是 chat completions。最常见的原因是端点填了
            // 网关的域名根路径，被首页接走并回了 HTML + 200。把这个原因写进消息里，
            // 否则玩家只看到"解析失败"，会误以为是模型的问题。
            throw new InvalidOperationException(
                $"无法解析 API 响应中的 content 字段（请求地址 {endpoint}）。" +
                "若上游返回的是网页而不是 JSON，说明端点应填写完整的 chat completions 路径。");
        }
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

        string endpoint = DeriveChatUrl(AiConfig.ComputeApiEndpoint);
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

        throw new InvalidOperationException(
            $"副 API 响应里既没有 tool_calls 也没有可用的 content（请求地址 {endpoint}）。" +
            "若上游返回的是网页而不是 JSON，说明端点应填写完整的 chat completions 路径。");
    }

    /// <summary>
    /// P5 上下文压缩：走副 API 端点与密钥，普通 Chat Completions（非 function calling）。
    /// 摘要属数据处理，不需要 tool_choice 强制。
    /// </summary>
    public static async Task<string> SummarizeAsync(
        IReadOnlyList<ChatMessage> messages,
        CancellationToken token)
    {
        string apiKey = AiConfig.GetComputeApiKeyPlain();
        if (string.IsNullOrEmpty(apiKey))
            throw new InvalidOperationException("副 API Key 未设置");

        string endpoint = DeriveChatUrl(AiConfig.ComputeApiEndpoint);
        if (string.IsNullOrWhiteSpace(endpoint))
            throw new InvalidOperationException("副 API 端点未设置");

        var requestBody = new ChatRequest
        {
            Model = AiConfig.ComputeModel ?? "gpt-4o-mini",
            Messages = messages,
            MaxTokens = AiConfig.ComputeMaxTokens > 0 ? AiConfig.ComputeMaxTokens : 800,
            Temperature = 0.3,
            Stream = false,
        };

        string json = JsonSerializer.Serialize(requestBody, SerializerOptions);

        string responseBody = await PostAsync(
            endpoint, apiKey, json,
            AiConfig.ComputeTimeoutSeconds > 0 ? AiConfig.ComputeTimeoutSeconds : 20,
            Math.Max(0, AiConfig.ComputeMaxRetries),
            token).ConfigureAwait(false);

        string content = TryExtractContent(responseBody);
        if (content == null)
            throw new InvalidOperationException(
                $"摘要 API 响应中无法提取 content 字段（请求地址 {endpoint}）");
        return content;
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
    /// <summary>
    /// 从上游拉取可用模型列表（GET /v1/models）。
    ///
    /// 为什么要单独推导 URL：配置里存的是 chat completions 端点（.../v1/chat/completions），
    /// 而模型列表在同一 base 下的 /v1/models。直接把 chat 端点拼 "/models" 会得到错误路径，
    /// 所以按 OpenAI 兼容惯例做一次路径替换。
    ///
    /// 兼容性：绝大多数 OpenAI 兼容服务（OpenAI / Azure 兼容层 / DeepSeek / 月之暗面 /
    /// 智谱 / 硅基流动 / OpenRouter / Ollama / LM Studio / vLLM / one-api 等）都实现了这个端点。
    /// 拿不到时调用方应退回手填，不能因此阻断配置流程。
    /// </summary>
    public static async Task<List<string>> ListModelsAsync(
        string chatEndpoint,
        string apiKey,
        CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(chatEndpoint))
            throw new InvalidOperationException("API 端点未设置");

        string modelsUrl = DeriveModelsUrl(chatEndpoint);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
        cts.CancelAfter(TimeSpan.FromSeconds(20));

        using var request = new HttpRequestMessage(HttpMethod.Get, modelsUrl);

        // 密钥可空：Ollama / LM Studio / vLLM 这类本地端点通常不校验 Authorization，
        // 强制要求密钥会让本地部署根本没法用这个功能。有则带上，没有就裸请求。
        if (!string.IsNullOrEmpty(apiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var response = await httpClient.SendAsync(request, cts.Token).ConfigureAwait(false);
        string body = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            string errorMsg = TryExtractErrorMessage(body) ?? $"HTTP {(int)response.StatusCode}";
            throw new HttpRequestException($"拉取模型列表失败：{errorMsg}");
        }

        var models = ParseModelList(body);
        if (models.Count == 0)
            throw new InvalidOperationException("上游返回了空的模型列表");
        return models;
    }

    /// <summary>
    /// 把 chat completions 端点换算成模型列表端点。
    /// 已知形态：
    ///   https://host/v1/chat/completions   → https://host/v1/models
    ///   https://host/api/v3/chat/completions → https://host/api/v3/models
    ///   https://host/v1/                   → https://host/v1/models
    ///   https://host                       → https://host/v1/models
    /// </summary>
    /// <summary>
    /// 把配置里填的端点换算成 chat completions 端点。
    ///
    /// 为什么需要这一层：设置对话框只校验 "http:// 或 https:// 开头"，所以配置里很容易只留一个
    /// base（如 https://gorouter.app）。直接 POST 到 base 上，网关会返回自己的管理页 HTML 加
    /// HTTP 200，于是「请求成功但解析不出 content」——表现成 AI 什么都没回，而不是报错，
    /// 极难排查。这里按 OpenAI 兼容惯例把 base 补全成 /v1/chat/completions。
    ///
    /// 已经写全的端点原样返回，不做任何改写：自建网关可能把路径挂在别的前缀下。
    ///   https://host                       → https://host/v1/chat/completions
    ///   https://host/v1                    → https://host/v1/chat/completions
    ///   https://host/api/v3                → https://host/api/v3/chat/completions
    ///   https://host/v1/chat/completions   → 原样
    ///   https://host/v1/responses          → 原样（非 chat 协议由调用方负责）
    /// </summary>
    internal static string DeriveChatUrl(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
            return endpoint;

        string url = endpoint.Trim().TrimEnd('/');

        // 已经指向某个具体动作的端点不再加工。只认这几种收尾：
        // 补全类（chat/completions、completions、responses）与 messages（Anthropic 原生协议）。
        if (url.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)
            || url.EndsWith("/completions", StringComparison.OrdinalIgnoreCase)
            || url.EndsWith("/responses", StringComparison.OrdinalIgnoreCase)
            || url.EndsWith("/messages", StringComparison.OrdinalIgnoreCase))
            return url;

        // 末段像版本号（v1 / v3 / v1beta）时视为 base + 版本，直接补动作路径；
        // 否则连版本段一起补。
        string tail = url[(url.LastIndexOf('/') + 1)..];
        bool looksLikeVersion = tail.Length >= 2
            && (tail[0] == 'v' || tail[0] == 'V')
            && char.IsDigit(tail[1]);
        return looksLikeVersion ? url + "/chat/completions" : url + "/v1/chat/completions";
    }

    internal static string DeriveModelsUrl(string chatEndpoint)
    {
        string url = chatEndpoint.Trim().TrimEnd('/');

        const string chatSuffix = "/chat/completions";
        if (url.EndsWith(chatSuffix, StringComparison.OrdinalIgnoreCase))
            return url[..^chatSuffix.Length] + "/models";

        // 有些服务把补全端点写成 /completions（无 /chat）。
        const string plainSuffix = "/completions";
        if (url.EndsWith(plainSuffix, StringComparison.OrdinalIgnoreCase))
            return url[..^plainSuffix.Length] + "/models";

        if (url.EndsWith("/models", StringComparison.OrdinalIgnoreCase))
            return url;

        // 只给了 base。带版本段的直接拼 /models，否则补一个 /v1。
        string tail = url[(url.LastIndexOf('/') + 1)..];
        bool looksLikeVersion = tail.Length >= 2
            && (tail[0] == 'v' || tail[0] == 'V')
            && char.IsDigit(tail[1]);
        return looksLikeVersion ? url + "/models" : url + "/v1/models";
    }

    /// <summary>
    /// 解析模型列表响应。同时兼容两种形态：
    ///   OpenAI 标准：{"data":[{"id":"gpt-4o"},...]}
    ///   少数实现：   {"models":["a","b"]} 或 {"models":[{"name":"a"}]}（如部分 Ollama 网关）
    /// </summary>
    private static List<string> ParseModelList(string json)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void TryAdd(string id)
        {
            if (!string.IsNullOrWhiteSpace(id) && seen.Add(id.Trim()))
                result.Add(id.Trim());
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            JsonElement array = default;
            bool hasArray = false;
            if (root.ValueKind == JsonValueKind.Array)
            {
                array = root;
                hasArray = true;
            }
            else if (root.TryGetProperty("data", out var dataNode) && dataNode.ValueKind == JsonValueKind.Array)
            {
                array = dataNode;
                hasArray = true;
            }
            else if (root.TryGetProperty("models", out var modelsNode) && modelsNode.ValueKind == JsonValueKind.Array)
            {
                array = modelsNode;
                hasArray = true;
            }

            if (!hasArray)
                return result;

            foreach (var item in array.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    TryAdd(item.GetString());
                    continue;
                }
                if (item.ValueKind != JsonValueKind.Object)
                    continue;
                if (item.TryGetProperty("id", out var idNode) && idNode.ValueKind == JsonValueKind.String)
                    TryAdd(idNode.GetString());
                else if (item.TryGetProperty("name", out var nameNode) && nameNode.ValueKind == JsonValueKind.String)
                    TryAdd(nameNode.GetString());
                else if (item.TryGetProperty("model", out var modelNode) && modelNode.ValueKind == JsonValueKind.String)
                    TryAdd(modelNode.GetString());
            }
        }
        catch
        {
        }

        result.Sort(StringComparer.OrdinalIgnoreCase);
        return result;
    }
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
