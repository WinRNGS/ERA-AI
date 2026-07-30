using System;
using System.Collections.Generic;

namespace MinorShift.Emuera.AI.Interact;

/// <summary>
/// 会话消息。P4 把 P3 的裸 ChatMessage 列表换成带 id 的消息模型。
///
/// 为什么必须有 id：引用与修改回复都要指向"某一条具体的消息"。用列表下标做标识
/// 在上下文压缩（P5）之后一定会错位——那时下标 3 已经不是原来那条了。
/// </summary>
internal sealed class AiMessage
{
    /// <summary>会话内唯一，单调递增。跨轮稳定，不随列表增删变化。</summary>
    public long Id;

    /// <summary>user / assistant。system 不进会话历史（每轮现装）。</summary>
    public string Role;

    public string Text = "";

    /// <summary>产生这条消息的轮次号，与副 API 的 turn_id 一致。</summary>
    public string TurnId;

    /// <summary>被玩家直接编辑过。只影响上下文文本，不代表数值被改过。</summary>
    public bool Edited;

    /// <summary>被中断的不完整内容。目前非流式下不会出现，留给将来开流式用。</summary>
    public bool Interrupted;

    public DateTime CreatedAt = DateTime.Now;

    public bool IsAssistant => string.Equals(Role, "assistant", StringComparison.Ordinal);

    public ChatMessage ToChatMessage() => new() { Role = Role, Content = Text ?? "" };
}

/// <summary>
/// 一条引用。指向某条历史消息，同时**自带一份文本快照**。
///
/// 设计文档 S3.4.3 原本写的是"引用记录指向 id 而非文本副本"，但 RISK-15 说得更准：
/// 被引用的消息可能因为上下文压缩（P5）被淘汰，也可能被玩家编辑掉。那时只剩 id
/// 就什么都还原不出来了。所以这里两样都留：id 用于在界面上定位与高亮，
/// 快照用于装配 prompt。装配时一律用快照，不回头去查原消息。
/// </summary>
internal sealed class AiQuote
{
    /// <summary>被引用消息的 id。原消息已不存在时仍保留，仅用于显示来源。</summary>
    public long MessageId;

    public string Role;

    /// <summary>引用建立那一刻的文本副本。装配 prompt 只认这一份。</summary>
    public string Snapshot = "";

    /// <summary>界面上的短标签。</summary>
    public string Label => $"{(string.Equals(Role, "assistant", StringComparison.Ordinal) ? "AI" : "你")}：{Brief(Snapshot, 24)}";

    private static string Brief(string text, int max)
    {
        if (string.IsNullOrEmpty(text))
            return "";
        string flat = text.Replace("\r", " ").Replace("\n", " ").Trim();
        return flat.Length <= max ? flat : flat[..max] + "\u2026";
    }
}

/// <summary>
/// 会话历史。只允许界面线程写，读取一律返回快照数组，因此后台线程拿到的都是纯数据。
///
/// 与副 API 短记忆（AiComputeMemory）的关系：两者互不替代。这里存的是叙事原文，
/// 短记忆存的是结算摘要。清空时两边一起清（AiDispatcher.ClearHistory），
/// 否则副 API 会看到主 API 已经忘掉的剧情。
/// </summary>
internal static class AiConversation
{
    /// <summary>保留的最大轮数。与 P3 的 MaxHistoryRounds 一致。</summary>
    public const int MaxRounds = 20;

    private static readonly object gate = new();
    private static readonly List<AiMessage> messages = [];
    private static long nextId;

    public static IReadOnlyList<AiMessage> All
    {
        get { lock (gate) return messages.ToArray(); }
    }

    public static int Count
    {
        get { lock (gate) return messages.Count; }
    }

    /// <summary>记录一轮完整往返。返回这一轮的两条消息，供引用与编辑定位。</summary>
    public static (AiMessage User, AiMessage Assistant) AddRound(string turnId, string userInput, string response)
    {
        lock (gate)
        {
            var user = new AiMessage
            {
                Id = ++nextId,
                Role = "user",
                Text = userInput ?? "",
                TurnId = turnId,
            };
            var assistant = new AiMessage
            {
                Id = ++nextId,
                Role = "assistant",
                Text = response ?? "",
                TurnId = turnId,
            };
            messages.Add(user);
            messages.Add(assistant);
            Trim();
            return (user, assistant);
        }
    }

    public static AiMessage FindById(long id)
    {
        lock (gate)
        {
            foreach (AiMessage m in messages)
            {
                if (m.Id == id)
                    return m;
            }
        }
        return null;
    }

    /// <summary>最后一条 assistant 消息。没有则返回 null。</summary>
    public static AiMessage LastAssistant()
    {
        lock (gate)
        {
            for (int i = messages.Count - 1; i >= 0; i--)
            {
                if (messages[i].IsAssistant)
                    return messages[i];
            }
        }
        return null;
    }

    /// <summary>某条 assistant 消息对应的本轮玩家输入。重生成时要拿它当输入。</summary>
    public static AiMessage UserOf(AiMessage assistant)
    {
        if (assistant == null)
            return null;
        lock (gate)
        {
            int index = messages.IndexOf(assistant);
            for (int i = index - 1; i >= 0; i--)
            {
                if (!messages[i].IsAssistant)
                    return messages[i];
            }
        }
        return null;
    }

    /// <summary>
    /// 直接改写一条 assistant 消息的文本（修改回复模式 A）。
    /// 只动上下文，绝不回滚已写入的数值——数值与正文是两条通道，
    /// 改文风不该让存档跟着变。要撤数值请走「撤销上轮数值结算」。
    /// </summary>
    public static bool TryEditAssistant(long id, string newText, out string error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(newText))
        {
            error = "修改后的正文不能为空";
            return false;
        }
        lock (gate)
        {
            foreach (AiMessage m in messages)
            {
                if (m.Id != id)
                    continue;
                if (!m.IsAssistant)
                {
                    error = "只能编辑 AI 的回复，玩家自己的输入请重新发送";
                    return false;
                }
                m.Text = newText;
                m.Edited = true;
                return true;
            }
        }
        error = $"找不到 id={id} 的消息（可能已被上下文淘汰）";
        return false;
    }

    /// <summary>替换某条 assistant 消息的正文（模式 B 重生成后调用），不标记为玩家编辑。</summary>
    public static bool TryReplaceAssistant(long id, string newText)
    {
        lock (gate)
        {
            foreach (AiMessage m in messages)
            {
                if (m.Id == id && m.IsAssistant)
                {
                    m.Text = newText ?? "";
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// 移除某一轮（user + assistant 成对移除）。终止后选择「丢弃」时用。
    /// 成对移除是必须的：只删 assistant 会留下一个没有回应的 user 消息，
    /// 下一轮模型会把它当成"上文里玩家说了话但我没理"，从而自行圆场。
    /// </summary>
    public static bool TryRemoveRound(long assistantId, out string error)
    {
        error = null;
        lock (gate)
        {
            int index = -1;
            for (int i = 0; i < messages.Count; i++)
            {
                if (messages[i].Id == assistantId && messages[i].IsAssistant)
                {
                    index = i;
                    break;
                }
            }
            if (index < 0)
            {
                error = "找不到要丢弃的这一轮";
                return false;
            }
            messages.RemoveAt(index);
            if (index - 1 >= 0 && !messages[index - 1].IsAssistant)
                messages.RemoveAt(index - 1);
            return true;
        }
    }

    /// <summary>装配 prompt 用的历史。</summary>
    public static List<ChatMessage> ToChatMessages()
    {
        var result = new List<ChatMessage>();
        lock (gate)
        {
            foreach (AiMessage m in messages)
                result.Add(m.ToChatMessage());
        }
        return result;
    }

    public static void Clear()
    {
        lock (gate)
            messages.Clear();
    }

    private static void Trim()
    {
        while (messages.Count > MaxRounds * 2)
            messages.RemoveAt(0);
    }
}

/// <summary>
/// 待发送的引用栏。纯本地状态，增删引用不触发任何网络请求，
/// 因此锁定期间也允许操作（设计文档 S3.4.1：编辑历史 / 增删引用 → IDLE）。
/// 只允许界面线程调用。
/// </summary>
internal static class AiQuoteBox
{
    /// <summary>一次最多引用几条。多了会挤占本轮输入的注意力，也把 token 吃光。</summary>
    public const int MaxQuotes = 3;

    private static readonly List<AiQuote> quotes = [];

    public static IReadOnlyList<AiQuote> Quotes => quotes.ToArray();

    public static int Count => quotes.Count;

    /// <summary>引用一条消息。重复引用同一条会被忽略而不是叠加两遍。</summary>
    public static bool TryAdd(AiMessage message, out string error)
    {
        error = null;
        if (message == null)
        {
            error = "要引用的消息不存在";
            return false;
        }
        if (string.IsNullOrWhiteSpace(message.Text))
        {
            error = "空消息无法引用";
            return false;
        }
        if (quotes.Count >= MaxQuotes)
        {
            error = $"一次最多引用 {MaxQuotes} 条，请先移除一条";
            return false;
        }
        foreach (AiQuote q in quotes)
        {
            if (q.MessageId == message.Id)
            {
                error = "这一条已经在引用栏里了";
                return false;
            }
        }
        quotes.Add(new AiQuote
        {
            MessageId = message.Id,
            Role = message.Role,
            // 建立引用的这一刻就把文本抄下来，之后原消息被编辑或被淘汰都不影响本次引用。
            Snapshot = message.Text,
        });
        return true;
    }

    public static bool TryRemoveAt(int index)
    {
        if (index < 0 || index >= quotes.Count)
            return false;
        quotes.RemoveAt(index);
        return true;
    }

    public static void Clear() => quotes.Clear();

    /// <summary>
    /// 把引用拼到本轮输入的开头。格式与设计文档 S3.4.3 的示例一致。
    /// 拼在开头而不是结尾：让"针对哪一段"先于"要求是什么"出现，
    /// 模型读到指令时上文已经明确。
    /// </summary>
    public static string Compose(string userInput, IReadOnlyList<AiQuote> used)
    {
        if (used == null || used.Count == 0)
            return userInput ?? "";
        var sb = new System.Text.StringBuilder();
        foreach (AiQuote q in used)
        {
            string who = string.Equals(q.Role, "assistant", StringComparison.Ordinal) ? "AI 的回复" : "玩家先前的输入";
            sb.AppendLine($"[引用上文·{who}]{q.Snapshot?.Trim()}");
        }
        sb.AppendLine();
        sb.Append("玩家指令: ").Append(userInput ?? "");
        return sb.ToString();
    }
}