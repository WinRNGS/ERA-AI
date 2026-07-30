using System;
using System.Collections.Generic;
using System.Text;

namespace MinorShift.Emuera.AI.Security;

/// <summary>
/// P6 统一错误报告。
///
/// 目标：
///   1. 所有面向用户的错误消息经过脱敏，永不泄露密钥或内部路径。
///   2. 所有错误路径记录到 AiDispatcher.Log 供开发者排查，但日志也脱敏。
///   3. 各阶段的错误都有明确的阶段标签，方便定位问题在哪一步。
///   4. 压缩器的 SkipReason 纳入统一管道（P5 留给 P6 的接口）。
///
/// 错误类别（对应不同的用户提示与处置建议）：
///   - Config：配置缺失/格式错误，建议去设置页修正。
///   - Network：网络超时/连接失败，建议检查端点或网络。
///   - Auth：认证失败（401/403），建议检查密钥。
///   - RateLimit：限流（429），建议稍后重试。
///   - Parse：响应格式异常，建议检查模型/端点是否支持协议。
///   - Validation：输入/输出校验失败，属正常防护拦截。
///   - Internal：不应发生的内部错误，bug。
/// </summary>
internal static class AiErrorReporter
{
    /// <summary>
    /// 根据异常生成面向用户的安全错误消息。自动脱敏。
    /// </summary>
    public static AiError Classify(Exception exception, string phase)
    {
        if (exception == null)
            return new AiError(AiErrorKind.Internal, phase, "未知错误");

        string rawMessage = exception.Message ?? "";
        string safeMessage = Sanitize(rawMessage);

        // 根据异常类型与消息内容分类
        if (exception is OperationCanceledException)
            return new AiError(AiErrorKind.Cancelled, phase, "请求被取消");

        if (exception is System.Net.Http.HttpRequestException httpEx)
        {
            if (rawMessage.Contains("401") || rawMessage.Contains("Unauthorized"))
                return new AiError(AiErrorKind.Auth, phase, "认证失败，请检查 API 密钥是否正确");
            if (rawMessage.Contains("403") || rawMessage.Contains("Forbidden"))
                return new AiError(AiErrorKind.Auth, phase, "权限被拒绝，请检查 API 密钥权限");
            if (rawMessage.Contains("429") || rawMessage.Contains("Too Many Requests"))
                return new AiError(AiErrorKind.RateLimit, phase, "请求频率超限，请稍后重试");
            if (rawMessage.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
                rawMessage.Contains("timed out", StringComparison.OrdinalIgnoreCase))
                return new AiError(AiErrorKind.Network, phase, "请求超时，请检查网络连接或增大超时时间");

            return new AiError(AiErrorKind.Network, phase, $"网络请求失败：{safeMessage}");
        }

        if (exception is TimeoutException)
            return new AiError(AiErrorKind.Network, phase, "请求超时");

        if (exception is InvalidOperationException invOp)
        {
            string msg = invOp.Message;
            if (msg.Contains("Key") || msg.Contains("密钥"))
                return new AiError(AiErrorKind.Config, phase, safeMessage);
            if (msg.Contains("端点") || msg.Contains("Endpoint"))
                return new AiError(AiErrorKind.Config, phase, safeMessage);
            if (msg.Contains("解析") || msg.Contains("content"))
                return new AiError(AiErrorKind.Parse, phase, safeMessage);
        }

        if (exception is System.Text.Json.JsonException)
            return new AiError(AiErrorKind.Parse, phase, "API 响应格式异常，请检查端点是否支持 OpenAI Chat Completions 协议");

        return new AiError(AiErrorKind.Internal, phase, safeMessage);
    }

    /// <summary>
    /// 从 HTTP 状态码归类。在 AiBackend.PostAsync 中的错误响应时使用。
    /// </summary>
    public static AiErrorKind ClassifyHttpStatus(int statusCode)
    {
        return statusCode switch
        {
            401 => AiErrorKind.Auth,
            403 => AiErrorKind.Auth,
            429 => AiErrorKind.RateLimit,
            >= 500 and < 600 => AiErrorKind.Network,
            408 => AiErrorKind.Network,
            _ => AiErrorKind.Network,
        };
    }

    /// <summary>
    /// 从原始错误消息中脱敏密钥与内部路径。
    /// </summary>
    public static string Sanitize(string message)
    {
        if (string.IsNullOrEmpty(message))
            return message;

        string result = message;

        // 移除可能泄露的密钥（sk- 开头的长字符串）
        result = System.Text.RegularExpressions.Regex.Replace(
            result, @"sk-[A-Za-z0-9_\-]{20,}", "[REDACTED]");

        // 移除可能的路径信息（Windows 路径）
        result = System.Text.RegularExpressions.Regex.Replace(
            result, @"[A-Z]:\\[^\s""']+", "[PATH]");

        // 移除主副密钥明文（如果某处不慎把密钥拼进了错误消息）
        string mainKey = null, computeKey = null;
        try { mainKey = AiConfig.GetApiKeyPlain(); } catch { }
        try { computeKey = AiConfig.GetComputeApiKeyPlain(); } catch { }

        if (!string.IsNullOrEmpty(mainKey) && result.Contains(mainKey))
            result = result.Replace(mainKey, "[REDACTED]");
        if (!string.IsNullOrEmpty(computeKey) && result.Contains(computeKey))
            result = result.Replace(computeKey, "[REDACTED]");

        return result;
    }

    /// <summary>
    /// 生成完整的诊断行（写入 AiDispatcher.Log）。比 UserMessage 更详细。
    /// </summary>
    public static string FormatDiagnostic(AiError error, Exception exception = null)
    {
        var sb = new StringBuilder();
        sb.Append($"[P6-{error.Kind}] 阶段={error.Phase}：{error.UserMessage}");
        if (exception != null)
            sb.Append($" | 异常类型={exception.GetType().Name}");
        return sb.ToString();
    }
}

internal enum AiErrorKind
{
    Config,
    Network,
    Auth,
    RateLimit,
    Parse,
    Validation,
    Cancelled,
    Internal,
}

internal sealed class AiError
{
    public AiErrorKind Kind;
    public string Phase;
    public string UserMessage;

    public AiError(AiErrorKind kind, string phase, string message)
    {
        Kind = kind;
        Phase = phase;
        UserMessage = message;
    }

    /// <summary>给用户的建议动作。</summary>
    public string Suggestion => Kind switch
    {
        AiErrorKind.Config => "请在「AI → AI 设置」中检查并修正配置。",
        AiErrorKind.Auth => "请在「AI → AI 设置」中检查 API 密钥是否正确。",
        AiErrorKind.Network => "请检查网络连接，或增大超时设置。",
        AiErrorKind.RateLimit => "请稍等片刻后重试。",
        AiErrorKind.Parse => "请检查 API 端点是否兼容 OpenAI Chat Completions 协议。",
        AiErrorKind.Validation => "输入或输出未通过校验，属正常防护行为。",
        AiErrorKind.Cancelled => "",
        _ => "如反复出现请联系开发者。",
    };

    public override string ToString() => $"[{Kind}] {Phase}：{UserMessage}";
}
