using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace MinorShift.Emuera.AI.Security;

/// <summary>
/// P6 注入防护。
///
/// ERA 场景下的"注入"不是 SQL 注入，而是 prompt injection：
/// 玩家在对话输入中试图覆盖 system prompt、改变 AI 行为模式、或骗 AI 吐出设定。
///
/// 设计取向：
///   1. 不做硬拦截——ERA 是角色扮演游戏，用户有权在对话中说任何话。
///   2. 但做**标记与转义**：把用户输入里的可疑控制序列标记出来（role injection markers），
///      让 system prompt 里的指令始终比用户输入权威。
///   3. 严格保证请求装配时 role 字段不可被用户文本覆盖。
///
/// 防护层级：
///   A. 用户输入清洗（本类）：移除控制序列、标记可疑模式。
///   B. 请求装配隔离（在 AiDispatcher.BuildMessages 中强制）：
///      用户输入永远只能是 role=user 的 content，不允许冒充 system/assistant。
///   C. 输出清洗（在 AiDispatcher 已有 P4 选项清洗基础上扩展）。
/// </summary>
internal static class AiInputSanitizer
{
    /// <summary>
    /// 用户输入的最大允许长度。超过此长度截断。
    /// 8192 字符（约 4000-6000 token）对 ERA 的单轮文本输入已是极端上限。
    /// </summary>
    public const int MaxInputLength = 8192;

    /// <summary>
    /// 清洗用户输入。返回清洗后的文本和警告列表。
    /// 即使有警告，文本仍然可以使用（已标记/转义）。
    /// </summary>
    public static SanitizeResult Sanitize(string input)
    {
        var result = new SanitizeResult();

        if (string.IsNullOrEmpty(input))
        {
            result.CleanText = "";
            return result;
        }

        string text = input;

        // 1. 长度截断
        if (text.Length > MaxInputLength)
        {
            text = text[..MaxInputLength];
            result.Warnings.Add($"输入超出 {MaxInputLength} 字符上限，已截断");
        }

        // 2. 移除零宽字符与隐藏 Unicode（常见的 prompt injection 手段）
        text = RemoveInvisibleCharacters(text);

        // 3. 检测并转义 role injection 标记
        text = NeutralizeRoleMarkers(text, result.Warnings);

        // 4. 移除控制字符（保留换行和制表符）
        text = RemoveControlCharacters(text);

        // 5. 规范化换行
        text = text.Replace("\r\n", "\n").Replace("\r", "\n");

        // 6. 限制连续换行（防通过大量空行稀释 prompt attention）
        text = CollapseExcessiveNewlines(text, 5);

        // 7. 二次长度收敛。
        // 步骤 3 的 role marker 转义会让文本变长（[SYSTEM] -> [_SYSTEM_] 增长 2 字符），
        // 所以第 1 步的截断结果可能被重新顶破上限。这里兜底再截一次，
        // 保证 CleanText.Length <= MaxInputLength 是本方法的硬约束。
        if (text.Length > MaxInputLength)
        {
            text = text[..MaxInputLength];
            if (!result.Warnings.Contains($"输入超出 {MaxInputLength} 字符上限，已截断"))
                result.Warnings.Add($"输入超出 {MaxInputLength} 字符上限，已截断");
        }

        result.CleanText = text;
        return result;
    }

    /// <summary>
    /// 检测是否含有可疑的 prompt injection 模式。不拦截，仅标记。
    /// 返回 true 表示输入需要更高的警惕性（可在日志中记录）。
    /// </summary>
    public static bool DetectSuspiciousPatterns(string input, out List<string> detections)
    {
        detections = new List<string>();

        if (string.IsNullOrEmpty(input))
            return false;

        // 常见 prompt injection 特征
        var patterns = new (string pattern, string description)[]
        {
            (@"(?i)(忽略|无视|遗忘|放弃|丢弃)(之前|上面|以上|先前|前面)的?(所有|全部|一切)?(指令|指示|设定|规则|限制|prompt|instructions)",
             "尝试覆盖系统指令"),
            (@"(?i)(你现在是|你的新角色是|从现在起你是|assume the role|you are now|ignore previous)",
             "尝试重新定义角色"),
            (@"(?i)(输出|打印|显示|告诉我|repeat|print|output)(你的|系统|system).*(prompt|指令|设定|指示)",
             "尝试获取系统设定"),
            (@"(?i)\[SYSTEM\]|\[INST\]|\<\|im_start\|system\>|\<\|system\|>",
             "注入系统角色标记"),
            (@"(?i)<<SYS>>|### System|### Instruction|<s>\[INST\]",
             "注入 Llama/ChatML 格式标记"),
            (@"(?i)(DAN|jailbreak|bypass|Developer Mode|Override Mode)",
             "已知越狱指令关键词"),
        };

        foreach (var (pattern, description) in patterns)
        {
            if (Regex.IsMatch(input, pattern))
                detections.Add(description);
        }

        return detections.Count > 0;
    }

    /// <summary>
    /// 移除零宽字符和不可见 Unicode。保留普通空格、换行、制表符。
    /// </summary>
    private static string RemoveInvisibleCharacters(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            // 保留常见可见字符和普通空白
            if (c == '\n' || c == '\r' || c == '\t' || c == ' ')
            {
                sb.Append(c);
                continue;
            }

            // 移除零宽字符
            if (c == '\u200B' || c == '\u200C' || c == '\u200D' ||
                c == '\uFEFF' || c == '\u2060' || c == '\u2061' ||
                c == '\u2062' || c == '\u2063' || c == '\u2064' ||
                c == '\u180E' || c == '\u00AD')
            {
                continue;
            }

            // 移除 Unicode 方向控制字符
            if (c >= '\u202A' && c <= '\u202E')
                continue;
            if (c >= '\u2066' && c <= '\u2069')
                continue;

            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>
    /// 中和 role injection 标记。这些是各种 LLM 框架的消息分隔符。
    /// 不删除（可能是正常讨论），而是在前面加前缀让 LLM 将其视为普通文本。
    /// </summary>
    private static string NeutralizeRoleMarkers(string text, List<string> warnings)
    {
        // 需要中和的标记及其替换
        var markers = new (string pattern, string replacement)[]
        {
            (@"\[SYSTEM\]", "[_SYSTEM_]"),
            (@"\[INST\]", "[_INST_]"),
            (@"\[/INST\]", "[/_INST_]"),
            (@"<\|im_start\|>", "<|_im_start_|>"),
            (@"<\|im_end\|>", "<|_im_end_|>"),
            (@"<\|system\|>", "<|_system_|>"),
            (@"<\|user\|>", "<|_user_|>"),
            (@"<\|assistant\|>", "<|_assistant_|>"),
            (@"<<SYS>>", "<<_SYS_>>"),
            (@"<</SYS>>", "<</_SYS_>>"),
        };

        string result = text;
        bool anyMatch = false;

        foreach (var (pattern, replacement) in markers)
        {
            if (Regex.IsMatch(result, pattern, RegexOptions.IgnoreCase))
            {
                result = Regex.Replace(result, pattern, replacement, RegexOptions.IgnoreCase);
                anyMatch = true;
            }
        }

        if (anyMatch)
            warnings.Add("输入含有消息角色分隔符，已转义");

        return result;
    }

    /// <summary>
    /// 移除控制字符，保留换行与制表符。
    /// </summary>
    private static string RemoveControlCharacters(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (char.IsControl(c) && c != '\n' && c != '\r' && c != '\t')
                continue;
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>
    /// 折叠过多的连续换行。
    /// </summary>
    private static string CollapseExcessiveNewlines(string text, int maxConsecutive)
    {
        string max = new('\n', maxConsecutive);
        string excess = max + "\n";
        while (text.Contains(excess))
            text = text.Replace(excess, max);
        return text;
    }
}

/// <summary>
/// 输入清洗的结果。
/// </summary>
internal sealed class SanitizeResult
{
    public string CleanText = "";
    public List<string> Warnings = new();
    public bool HasWarnings => Warnings.Count > 0;
}
