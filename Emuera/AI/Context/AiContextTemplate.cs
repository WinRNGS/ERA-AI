using System.Text.Json.Serialization;

namespace MinorShift.Emuera.AI.Context;

/// <summary>
/// 上下文压缩契约（ai_traits.json 的 context 段）。
/// 定义上下文窗口大小、压缩策略与保留参数。
///
/// 示例配置：
/// "context": {
///   "context_window": 8192,
///   "retain_rounds": 4,
///   "trigger_ratio": 0.80,
///   "target_ratio": 0.50,
///   "enabled": true
/// }
/// </summary>
internal sealed class AiContextTemplate
{
    /// <summary>
    /// 上下文窗口 token 上限。对应所用模型的 context_length。
    /// GPT-4o-mini 默认 128k 但实际有效利用约 8k-16k。
    /// 大多数第三方/本地模型的有效窗口在 4k-8k。
    /// </summary>
    [JsonPropertyName("context_window")]
    public int ContextWindow { get; set; } = AiContextCompressor.DefaultContextWindow;

    /// <summary>
    /// 至少保留最近几轮不压缩。M 轮原文区。
    /// 过小会让模型丢失最近剧情的细节；过大会让压缩收益太小。
    /// </summary>
    [JsonPropertyName("retain_rounds")]
    public int RetainRounds { get; set; } = AiContextCompressor.MinRetainRounds;

    /// <summary>
    /// 触发压缩的阈值比例（0.5 - 0.95）。token 占用达到此比例时开始压缩。
    /// </summary>
    [JsonPropertyName("trigger_ratio")]
    public double TriggerRatio { get; set; } = AiContextCompressor.TriggerRatio;

    /// <summary>
    /// 压缩后的目标占用比例（0.3 - 0.7）。压缩要把占用降到这个水位以下。
    /// </summary>
    [JsonPropertyName("target_ratio")]
    public double TargetRatio { get; set; } = AiContextCompressor.TargetRatio;

    /// <summary>
    /// 是否启用上下文压缩。关闭后历史满了会被 AiConversation.Trim 硬截断（P4 行为）。
    /// 默认开启。
    /// </summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;
}