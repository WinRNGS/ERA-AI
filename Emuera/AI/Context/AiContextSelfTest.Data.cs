using MinorShift.Emuera.AI.Interact;
using System.Collections.Generic;

namespace MinorShift.Emuera.AI.Context;

/// <summary>
/// P5 自检用的测试数据。
/// </summary>
internal static class AiContextSelfTestData
{
    /// <summary>生成一组假的对话轮次，用于测试压缩逻辑。</summary>
    public static void PopulateConversation(int rounds)
    {
        AiConversation.Clear();
        for (int i = 1; i <= rounds; i++)
        {
            AiConversation.AddRound(
                $"test-turn-{i}",
                $"这是第 {i} 轮玩家的输入，角色走进了城堡的第 {i} 个房间。",
                $"这是第 {i} 轮 AI 的回复。城堡的第 {i} 个房间里有一面古老的镜子，映照出角色过去的记忆。这段描写大约占 50-80 个字符用于测试 token 估算。");
        }
    }

    /// <summary>生成长对话用于确保触发压缩阈值。</summary>
    public static void PopulateLongConversation(int rounds)
    {
        AiConversation.Clear();
        string longText = new string('测', 200);
        for (int i = 1; i <= rounds; i++)
        {
            AiConversation.AddRound(
                $"long-turn-{i}",
                $"玩家第{i}轮：{longText}",
                $"AI第{i}轮：{longText}");
        }
    }

    /// <summary>摘要 API 的替身：返回固定格式的摘要文本。</summary>
    public static string FakeSummarizer(string prompt)
    {
        return "【测试摘要】角色探索了城堡的多个房间，遇到了各种古老的镜子，每面镜子映照出不同的过去记忆。";
    }

    /// <summary>返回空文本的替身，模拟摘要失败。</summary>
    public static string EmptySummarizer(string prompt) => "";

    /// <summary>抛异常的替身，模拟网络错误。</summary>
    public static string ErrorSummarizer(string prompt)
        => throw new System.InvalidOperationException("模拟网络错误");

    /// <summary>生成一份标准词条库 JSON（含 context 段）。</summary>
    public const string TraitsJsonWithContext = """
{
  "version": 1,
  "traits": [],
  "context": {
    "context_window": 4096,
    "retain_rounds": 2,
    "trigger_ratio": 0.80,
    "target_ratio": 0.50,
    "enabled": true
  }
}
""";

    /// <summary>context 段被禁用的词条库 JSON。</summary>
    public const string TraitsJsonDisabled = """
{
  "version": 1,
  "traits": [],
  "context": {
    "context_window": 4096,
    "retain_rounds": 2,
    "enabled": false
  }
}
""";

    /// <summary>没有 context 段的词条库 JSON（使用默认值）。</summary>
    public const string TraitsJsonNoContext = """
{
  "version": 1,
  "traits": []
}
""";
}