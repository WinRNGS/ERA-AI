using MinorShift.Emuera.AI.Traits;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MinorShift.Emuera.AI.Context;

/// <summary>
/// P5 上下文压缩器。当会话历史 token 接近窗口上限时，把旧轮次摘要化以释放空间。
///
/// 设计取向（来自 ERA-AI-WORK 2.md）：
///   - 压缩归副 API 承担（主 API 专注文风）。
///   - 触发条件：历史 token 达约 80% 可用窗口，而非固定轮数。
///   - 装配顺序：词条 prompt + 数值状态 → 历史摘要 → 最近 M 轮原文 → 本轮输入（含引用）。
///   - 副 API 短记忆不参与压缩（它本来就只有 3-5 轮摘要）。
///   - 引用快照不受压缩影响（快照是值拷贝，设计就是为了这一点）。
///
/// 实现策略：
///   1. 每轮请求装配前检测 token 占用。
///   2. 超过 80% 阈值时，把最早的 N 轮对话送给副 API 做摘要。
///   3. 摘要替换掉原始的 N 轮消息，以一条 system 消息的形式插入历史区域头部。
///   4. 多次压缩的摘要可以滚动合并（新摘要覆盖旧摘要）。
///
/// 线程安全：
///   - 读历史、计算 token：界面线程。
///   - 发摘要请求到副 API：后台线程。
///   - 写回摘要结果：通过 InvokeOnUiThread 回到界面线程。
/// </summary>
internal static class AiContextCompressor
{
    /// <summary>上下文窗口 token 上限。对应模型的 context_length，用户可在 ai_traits.json 的 context 段配置。</summary>
    public const int DefaultContextWindow = 8192;

    /// <summary>触发压缩的阈值比例。达到此比例时开始压缩最老的轮次。</summary>
    public const double TriggerRatio = 0.80;

    /// <summary>压缩后目标占用比例。压缩要把占用降到这个水位以下，留出足够余量。</summary>
    public const double TargetRatio = 0.50;

    /// <summary>至少保留最近几轮不压缩。保证模型总能看到最近的对话上下文。</summary>
    public const int MinRetainRounds = 3;

    /// <summary>当前累积的历史摘要文本。每次压缩后更新，装配 prompt 时插在历史段落开头。</summary>
    private static string summary = "";
    private static readonly object gate = new();

    /// <summary>最近一次压缩的诊断信息。</summary>
    public static AiContextCompressInfo LastCompressInfo { get; private set; }

    public static string Summary
    {
        get { lock (gate) return summary; }
    }

    /// <summary>是否有摘要可用。有摘要时装配 prompt 要把它插在历史前面。</summary>
    public static bool HasSummary
    {
        get { lock (gate) return !string.IsNullOrWhiteSpace(summary); }
    }

    /// <summary>
    /// 检测当前会话是否需要压缩。在界面线程调用。
    /// 返回 true 时调用方应该启动压缩流程。
    /// </summary>
    public static bool NeedsCompression(string systemPrompt, string currentInput)
    {
        int window = GetContextWindow();
        int threshold = (int)(window * TriggerRatio);

        int used = EstimateCurrentTokens(systemPrompt, currentInput);
        return used >= threshold;
    }

    /// <summary>
    /// 估算当前请求装配后的总 token 数（粗略估算：1 中文字 ≈ 2 token，1 英文词 ≈ 1.3 token，
    /// 简化为字符数 / 2 作为保守上界——中文为主的场景下偏大，英文为主时偏小，但始终是安全侧）。
    /// </summary>
    public static int EstimateCurrentTokens(string systemPrompt, string currentInput)
    {
        int chars = 0;

        if (!string.IsNullOrEmpty(systemPrompt))
            chars += systemPrompt.Length;

        lock (gate)
        {
            if (!string.IsNullOrWhiteSpace(summary))
                chars += summary.Length;
        }

        var history = Interact.AiConversation.All;
        foreach (var msg in history)
        {
            if (!string.IsNullOrEmpty(msg.Text))
                chars += msg.Text.Length;
        }

        if (!string.IsNullOrEmpty(currentInput))
            chars += currentInput.Length;

        return EstimateTokens(chars);
    }

    /// <summary>字符数转 token 估计值。中文为主时 1 字 ≈ 1.5-2 token，取 1.8 作为保守值。</summary>
    public static int EstimateTokens(int charCount)
    {
        return (int)(charCount * 1.8);
    }

    /// <summary>估算单段文本的 token 数。</summary>
    public static int EstimateTokens(string text)
    {
        return string.IsNullOrEmpty(text) ? 0 : EstimateTokens(text.Length);
    }

    /// <summary>
    /// 执行一次压缩：选出要压缩的旧轮次，发给副 API 做摘要，用摘要替代原文。
    ///
    /// 必须在后台线程调用。内部通过 invokeOnUi 切回界面线程读写会话历史。
    /// </summary>
    public static async Task<AiContextCompressResult> CompressAsync(
        Func<Action, Task> invokeOnUi,
        CancellationToken token)
    {
        var result = new AiContextCompressResult();

        List<Interact.AiMessage> toCompress = null;
        string existingSummary = null;

        // 在界面线程选出要压缩的消息
        await invokeOnUi(() =>
        {
            var all = Interact.AiConversation.All;
            int totalRounds = all.Count / 2;
            int retainRounds = Math.Max(MinRetainRounds, totalRounds / 3);
            int compressRounds = totalRounds - retainRounds;

            if (compressRounds <= 0)
            {
                result.SkipReason = "会话轮次不足，无需压缩";
                return;
            }

            toCompress = new List<Interact.AiMessage>();
            int msgCount = compressRounds * 2;
            for (int i = 0; i < msgCount && i < all.Count; i++)
                toCompress.Add(all[i]);

            lock (gate)
                existingSummary = summary;
        }).ConfigureAwait(false);

        if (toCompress == null || toCompress.Count == 0)
            return result;

        result.CompressedMessageCount = toCompress.Count;
        result.CompressedRounds = toCompress.Count / 2;

        // 构建摘要请求
        string requestText = BuildSummaryPrompt(existingSummary, toCompress);
        result.RequestChars = requestText.Length;

        // 发给副 API
        string summaryResponse;
        try
        {
            summaryResponse = await RequestSummaryAsync(requestText, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            result.SkipReason = "摘要请求被取消";
            return result;
        }
        catch (Exception e)
        {
            result.SkipReason = $"摘要请求失败：{e.Message}";
            return result;
        }

        if (string.IsNullOrWhiteSpace(summaryResponse))
        {
            result.SkipReason = "摘要返回空内容";
            return result;
        }

        result.SummaryChars = summaryResponse.Length;

        // 回到界面线程：移除已压缩的消息并更新摘要
        await invokeOnUi(() =>
        {
            // 从旧到新移除已压缩的轮次
            foreach (var msg in toCompress)
            {
                if (msg.IsAssistant)
                    Interact.AiConversation.TryRemoveRound(msg.Id, out _);
            }

            lock (gate)
                summary = summaryResponse.Trim();

            result.Success = true;
        }).ConfigureAwait(false);

        LastCompressInfo = new AiContextCompressInfo
        {
            Timestamp = DateTime.Now,
            CompressedRounds = result.CompressedRounds,
            SummaryLength = summaryResponse.Length,
            Success = result.Success,
        };

        return result;
    }

    /// <summary>
    /// 向副 API 发送摘要请求。摘要属于数据处理，走副 API 的端点与密钥。
    /// 如果有替身钩子（自检用），直接走替身。
    /// </summary>
    private static async Task<string> RequestSummaryAsync(string prompt, CancellationToken token)
    {
        if (BackendOverride != null)
            return BackendOverride(prompt);

        var messages = new List<ChatMessage>
        {
            new() { Role = "system", Content = SummarySystemPrompt },
            new() { Role = "user", Content = prompt },
        };

        string apiKey = AiConfig.GetComputeApiKeyPlain();
        if (string.IsNullOrEmpty(apiKey))
            throw new InvalidOperationException("副 API Key 未设置，无法执行上下文压缩");

        string endpoint = AiConfig.ComputeApiEndpoint;
        if (string.IsNullOrWhiteSpace(endpoint))
            throw new InvalidOperationException("副 API 端点未设置，无法执行上下文压缩");

        return await AiBackend.SummarizeAsync(messages, token).ConfigureAwait(false);
    }

    private const string SummarySystemPrompt =
        "你是一个叙事摘要助手。你的任务是把一段 ERA 游戏的对话历史压缩成简洁的剧情摘要。"
        + "要求：保留所有关键事件、角色情感变化、重要对话要点；去掉重复的修饰和过渡语句；"
        + "用第三人称客观叙述；长度控制在原文的 1/3 以内；不要添加原文没有的信息。";

    /// <summary>构建发给摘要 API 的 prompt。</summary>
    private static string BuildSummaryPrompt(string existingSummary, List<Interact.AiMessage> messages)
    {
        var sb = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(existingSummary))
        {
            sb.AppendLine("【已有的历史摘要】");
            sb.AppendLine(existingSummary.Trim());
            sb.AppendLine();
            sb.AppendLine("【需要合并进摘要的新对话】");
        }
        else
        {
            sb.AppendLine("【需要压缩的对话历史】");
        }

        foreach (var msg in messages)
        {
            string role = msg.IsAssistant ? "AI" : "玩家";
            sb.AppendLine($"{role}：{msg.Text?.Trim()}");
        }

        sb.AppendLine();
        sb.AppendLine("请把以上对话（以及已有摘要，如果有的话）合并压缩成一段简洁的剧情摘要。");
        return sb.ToString();
    }

    /// <summary>获取配置的上下文窗口大小。</summary>
    public static int GetContextWindow()
    {
        var template = AiTraitLibrary.ContextTemplate;
        return template != null && template.ContextWindow > 0
            ? template.ContextWindow
            : DefaultContextWindow;
    }

    /// <summary>获取配置的最近保留轮数（M 轮原文）。</summary>
    public static int GetRetainRounds()
    {
        var template = AiTraitLibrary.ContextTemplate;
        return template != null && template.RetainRounds > 0
            ? template.RetainRounds
            : MinRetainRounds;
    }

    /// <summary>清空摘要。ClearHistory 时一并调用。</summary>
    public static void Clear()
    {
        lock (gate)
            summary = "";
        LastCompressInfo = null;
    }

    /// <summary>
    /// 替身钩子。非 null 时不发网络请求，用它的返回值当摘要结果。仅供自检使用。
    /// </summary>
    public static Func<string, string> BackendOverride;
}

/// <summary>一次压缩操作的结果。</summary>
internal sealed class AiContextCompressResult
{
    public bool Success;
    public string SkipReason;
    public int CompressedRounds;
    public int CompressedMessageCount;
    public int RequestChars;
    public int SummaryChars;
}

/// <summary>最近一次压缩的观测信息。供面板展示。</summary>
internal sealed class AiContextCompressInfo
{
    public DateTime Timestamp;
    public int CompressedRounds;
    public int SummaryLength;
    public bool Success;
}