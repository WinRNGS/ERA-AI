using MinorShift.Emuera.Runtime.Script;
using MinorShift.Emuera.Runtime.Script.Statements.Variable;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MinorShift.Emuera.AI.Traits;

/// <summary>
/// system prompt 装配。把命中的词条摆成主 API 能吃的 system prompt。
///
/// 装配顺序与 P5 上下文压缩的最终顺序保持一致：
///   词条 prompt + 数值状态 → 历史摘要 → 最近 M 轮原文 → 本轮输入
/// 本类只负责前半段（词条 + 数值状态），历史与本轮输入由 AiDispatcher.BuildMessages 接在后面。
///
/// 必须在界面线程调用：要读 ERA 变量。
/// </summary>
internal static class AiPromptBuilder
{
    /// <summary>内置默认骨架。词条库里没写 prompt.layout 时用这个。</summary>
    public const string DefaultLayout = """
你是 ERA 游戏的叙事 AI，负责以第三人称描写场景与角色言行。
当前登场角色：{NAME}（呼称：{CALLNAME}，角色号 {CHARA_NO}）。

{TRAITS}

{SPEECH}

{CONSTRAINTS}

{STATE}

写作要求：只写这一轮发生的事，正文控制在 300 字以内，不要替玩家决定玩家的行动。
""";

    /// <summary>
    /// 为指定角色生成 system prompt。charaNo 为角色号（非登录号）。
    /// 失败时返回 AiConfig.SystemPrompt 作为兜底，并把原因写进 diagnostics。
    /// </summary>
    public static string Build(long charaNo, out AiPromptBuildInfo info)
    {
        info = new AiPromptBuildInfo { CharaNo = charaNo };

        if (GlobalStatic.EMediator == null || GlobalStatic.VEvaluator == null)
        {
            info.FallbackReason = "引擎尚未就绪";
            return AiConfig.SystemPrompt;
        }

        long register = GlobalStatic.VEvaluator.GetChara(charaNo);
        if (register < 0)
        {
            info.FallbackReason = $"角色号 {charaNo} 未登录";
            return AiConfig.SystemPrompt;
        }
        info.Register = register;

        List<AiTraitInstance> hits = AiTraitMatcher.Match(charaNo, out string matchError);
        if (matchError != null)
            AiTraitDiagnostics.Report(matchError);
        info.Traits = hits;

        if (hits.Count == 0)
        {
            info.FallbackReason = "该角色未命中任何词条";
            return AiConfig.SystemPrompt;
        }

        AiPromptTemplate template = ResolveTemplate();
        string prompt = Compose(template, register, charaNo, hits);

        if (template.MaxChars > 0 && prompt.Length > template.MaxChars)
        {
            prompt = prompt[..template.MaxChars];
            AiTraitDiagnostics.Report($"system prompt 超过 {template.MaxChars} 字，已截断。建议减少命中词条数或压缩描述。");
            info.Truncated = true;
        }

        info.Prompt = prompt;
        return prompt;
    }

    /// <summary>
    /// 为引擎当前的调教对象（TARGET）生成 system prompt。必须在界面线程调用。
    /// TARGET 存的是登录号，这里换算成角色号后再走 Build，保证下游一律用角色号。
    /// </summary>
    public static string BuildForCurrentTarget(out AiPromptBuildInfo info)
    {
        info = new AiPromptBuildInfo();
        if (GlobalStatic.EMediator == null || GlobalStatic.VariableData == null)
        {
            info.FallbackReason = "引擎尚未就绪";
            return AiConfig.SystemPrompt;
        }

        long register;
        if (!AiVariableAccess.TryReadInt("TARGET:0", out register, out string error))
        {
            info.FallbackReason = $"无法读取 TARGET（{error}）";
            return AiConfig.SystemPrompt;
        }
        if (register < 0)
        {
            info.FallbackReason = "当前没有调教对象（TARGET < 0）";
            return AiConfig.SystemPrompt;
        }

        var list = GlobalStatic.VariableData.CharacterList;
        if (register >= list.Count)
        {
            info.FallbackReason = $"TARGET={register} 超出已登录角色数 {list.Count}";
            return AiConfig.SystemPrompt;
        }

        return Build(list[(int)register].NO, out info);
    }

    private static AiPromptTemplate ResolveTemplate()
        => AiTraitLibrary.PromptTemplate ?? new AiPromptTemplate();

    private static string Compose(AiPromptTemplate template, long register, long charaNo, List<AiTraitInstance> hits)
    {
        string traitBlock = BuildTraitBlock(template, hits);
        string speechBlock = BuildSpeechBlock(template, hits);
        string constraintBlock = BuildConstraintBlock(template, hits);
        string stateBlock = BuildStateBlock(template, register);

        string layout = string.IsNullOrWhiteSpace(template.Layout) ? DefaultLayout : template.Layout;
        var sb = new StringBuilder(layout);
        Replace(sb, "{NAME}", ReadStr($"NAME:{register}", ""));
        Replace(sb, "{CALLNAME}", ReadStr($"CALLNAME:{register}", ""));
        Replace(sb, "{CHARA_NO}", charaNo.ToString(CultureInfo.InvariantCulture));
        Replace(sb, "{TRAITS}", traitBlock);
        Replace(sb, "{SPEECH}", speechBlock);
        Replace(sb, "{CONSTRAINTS}", constraintBlock);
        Replace(sb, "{STATE}", stateBlock);

        if (template.GlobalRules != null && template.GlobalRules.Count > 0)
        {
            sb.AppendLine();
            foreach (string rule in template.GlobalRules)
            {
                if (!string.IsNullOrWhiteSpace(rule))
                    sb.AppendLine("- " + rule.Trim());
            }
        }

        return Tidy(sb.ToString());
    }

    private static string BuildTraitBlock(AiPromptTemplate template, List<AiTraitInstance> hits)
    {
        var sb = new StringBuilder();
        foreach (AiTraitInstance t in hits)
        {
            if (string.IsNullOrWhiteSpace(t.Description))
                continue;
            sb.AppendLine($"- {t.Name}：{t.Description.Trim()}");
        }
        return WithHeader(template.TraitHeader, sb.ToString());
    }

    private static string BuildSpeechBlock(AiPromptTemplate template, List<AiTraitInstance> hits)
    {
        var sb = new StringBuilder();
        foreach (AiTraitInstance t in hits)
        {
            if (string.IsNullOrWhiteSpace(t.SpeechStyle))
                continue;
            sb.AppendLine($"- {t.SpeechStyle.Trim()}");
        }
        return WithHeader(template.SpeechHeader, sb.ToString());
    }

    private static string BuildConstraintBlock(AiPromptTemplate template, List<AiTraitInstance> hits)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var sb = new StringBuilder();
        foreach (AiTraitInstance t in hits)
        {
            foreach (string c in t.Constraints)
            {
                if (string.IsNullOrWhiteSpace(c) || !seen.Add(c.Trim()))
                    continue;
                sb.AppendLine($"- {c.Trim()}");
            }
        }
        return WithHeader(template.ConstraintHeader, sb.ToString());
    }

    /// <summary>
    /// 数值状态段落。P3 副 API 需要权威数值时会复用同一批字段，避免两边定义漂移。
    /// </summary>
    private static string BuildStateBlock(AiPromptTemplate template, long register)
    {
        if (template.StateFields == null || template.StateFields.Count == 0)
            return "";

        var sb = new StringBuilder();
        foreach (AiStateField field in template.StateFields)
        {
            if (field == null || string.IsNullOrWhiteSpace(field.Expr))
                continue;
            string expr = field.Expr.Replace(AiTraitMatcher.CharaPlaceholder,
                register.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);
            string label = string.IsNullOrWhiteSpace(field.Label) ? expr : field.Label;

            VariableTerm term = AiVariableAccess.Resolve(expr, out string error);
            if (term == null)
            {
                AiTraitDiagnostics.Report($"状态字段无法解析：{expr}（{error}）");
                continue;
            }
            try
            {
                string text = term.GetEraType() == EraType.String
                    ? term.GetStrValue(GlobalStatic.EMediator)
                    : term.GetIntValue(GlobalStatic.EMediator).ToString(CultureInfo.InvariantCulture);
                sb.AppendLine($"- {label}: {text}");
            }
            catch (Exception e)
            {
                AiTraitDiagnostics.Report($"状态字段读取失败：{expr}（{e.Message}）");
            }
        }
        return WithHeader(template.StateHeader, sb.ToString());
    }

    private static string WithHeader(string header, string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return "";
        return string.IsNullOrWhiteSpace(header)
            ? body.TrimEnd()
            : header.Trim() + "\n" + body.TrimEnd();
    }

    private static string ReadStr(string expr, string fallback)
    {
        VariableTerm term = AiVariableAccess.Resolve(expr, out _);
        if (term == null || term.GetEraType() != EraType.String)
            return fallback;
        try
        {
            return term.GetStrValue(GlobalStatic.EMediator) ?? fallback;
        }
        catch
        {
            return fallback;
        }
    }

    private static void Replace(StringBuilder sb, string token, string value)
        => sb.Replace(token, value ?? "");

    /// <summary>压掉空段落留下的连续空行，避免 prompt 里出现大片空白浪费 token。</summary>
    private static string Tidy(string text)
    {
        var sb = new StringBuilder(text.Length);
        int blank = 0;
        foreach (string raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            string line = raw.TrimEnd();
            if (line.Length == 0)
            {
                if (++blank > 1)
                    continue;
            }
            else
            {
                blank = 0;
            }
            sb.Append(line).Append('\n');
        }
        return sb.ToString().Trim();
    }
}

/// <summary>一次装配的观测信息。供面板与日志展示，不进 prompt。</summary>
internal sealed class AiPromptBuildInfo
{
    public long CharaNo;
    public long Register = -1;
    public List<AiTraitInstance> Traits = [];
    public string Prompt;
    public bool Truncated;

    /// <summary>非空表示走了兜底 prompt（静态 AiConfig.SystemPrompt）。</summary>
    public string FallbackReason;

    public bool UsedTraits => FallbackReason == null;
}
