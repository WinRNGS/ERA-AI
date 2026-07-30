using MinorShift.Emuera.Runtime.Script;
using MinorShift.Emuera.Runtime.Script.Statements.Variable;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace MinorShift.Emuera.AI.Traits;

/// <summary>
/// 角色词条匹配引擎。输入角色号，输出该角色本轮命中的词条实例。
///
/// 必须在界面线程调用：内部要读 ERA 变量，变量层无同步保护。
///
/// 定位方式（对应 RISK-21）：外部一律传"角色号"（chara*.csv 的番号 NO），
/// 匹配时才临时换算成登录号。禁止跨轮缓存登录号，因为增删角色会让登录号漂移。
/// </summary>
internal static class AiTraitMatcher
{
    /// <summary>单角色最多命中的词条数。设计已定 3-5，取上限 5。</summary>
    public const int MaxTraitsPerChara = 5;

    /// <summary>表达式里代表"当前角色登录号"的占位符。</summary>
    public const string CharaPlaceholder = "{CHARA}";

    /// <summary>
    /// 匹配指定角色。charaNo 为角色号。返回按得分降序排列的词条实例，最多 MaxTraitsPerChara 条。
    /// </summary>
    public static List<AiTraitInstance> Match(long charaNo, out string error)
    {
        error = null;
        var result = new List<AiTraitInstance>();

        if (GlobalStatic.EMediator == null || GlobalStatic.VEvaluator == null)
        {
            error = "引擎尚未就绪，无法匹配词条";
            return result;
        }

        long register = GlobalStatic.VEvaluator.GetChara(charaNo);
        if (register < 0)
        {
            error = $"角色号 {charaNo} 未登录，无法匹配词条";
            return result;
        }

        var candidates = new List<AiTraitInstance>();
        foreach (AiTrait trait in AiTraitLibrary.All)
        {
            if (!trait.Enabled)
                continue;

            AiTraitNpcOverride ov = FindOverride(trait, charaNo);
            bool forced = ov != null && ov.Force;

            long weight = trait.Match?.Weight ?? 100;
            if (!forced)
            {
                if (!EvaluateRule(trait.Match, register, out bool hit) || !hit)
                    continue;
            }

            var instance = new AiTraitInstance
            {
                Trait = trait,
                Description = trait.Description,
                SpeechStyle = trait.SpeechStyle,
                Constraints = trait.Constraints != null ? new List<string>(trait.Constraints) : [],
                Score = weight + trait.Priority,
            };

            ApplyOverride(instance, ov);
            if (!ApplyModifiers(instance, trait, register))
                continue;

            candidates.Add(instance);
        }

        List<AiTraitInstance> resolved = AiTraitConflictResolver.Resolve(candidates);
        resolved.Sort(static (a, b) =>
        {
            int byScore = b.Score.CompareTo(a.Score);
            if (byScore != 0)
                return byScore;
            int byPriority = b.Priority.CompareTo(a.Priority);
            return byPriority != 0 ? byPriority : string.CompareOrdinal(a.Id, b.Id);
        });

        if (resolved.Count > MaxTraitsPerChara)
            resolved.RemoveRange(MaxTraitsPerChara, resolved.Count - MaxTraitsPerChara);
        return resolved;
    }

    private static AiTraitNpcOverride FindOverride(AiTrait trait, long charaNo)
    {
        if (trait.OverrideNpcs == null)
            return null;
        foreach (AiTraitNpcOverride o in trait.OverrideNpcs)
        {
            if (o != null && o.CharaNo == charaNo)
                return o;
        }
        return null;
    }

    private static void ApplyOverride(AiTraitInstance instance, AiTraitNpcOverride ov)
    {
        if (ov == null)
            return;
        if (!string.IsNullOrWhiteSpace(ov.Description))
            instance.Description = ov.Description;
        if (!string.IsNullOrWhiteSpace(ov.SpeechStyle))
            instance.SpeechStyle = ov.SpeechStyle;
        if (ov.Constraints != null && ov.Constraints.Count > 0)
            instance.Constraints = new List<string>(ov.Constraints);
        instance.Score += ov.WeightBonus;
    }

    /// <summary>返回 false 表示该词条被 suppress 修改器抑制，应整条丢弃。</summary>
    private static bool ApplyModifiers(AiTraitInstance instance, AiTrait trait, long register)
    {
        if (trait.Modifiers == null)
            return true;

        foreach (AiTraitModifier m in trait.Modifiers)
        {
            if (m == null || m.When == null || string.IsNullOrWhiteSpace(m.Effect))
                continue;
            if (!EvaluateCondition(m.When, register, out bool hit) || !hit)
                continue;

            switch (m.Effect.ToLowerInvariant())
            {
                case "suppress":
                    return false;
                case "weight":
                    instance.Score += m.Value;
                    break;
                case "description":
                    instance.Description = m.Text;
                    break;
                case "speech_style":
                case "speechstyle":
                    instance.SpeechStyle = m.Text;
                    break;
                case "add_constraint":
                case "addconstraint":
                    if (!string.IsNullOrWhiteSpace(m.Text))
                        instance.Constraints.Add(m.Text);
                    break;
            }
        }
        return true;
    }

    private static bool EvaluateRule(AiTraitMatchRule rule, long register, out bool hit)
    {
        hit = false;
        if (rule == null)
            return true;

        if (rule.Always)
        {
            hit = true;
            return true;
        }

        bool hasAny = (rule.All != null && rule.All.Count > 0)
                   || (rule.Any != null && rule.Any.Count > 0)
                   || (rule.None != null && rule.None.Count > 0);
        if (!hasAny)
            return true;

        if (rule.All != null)
        {
            foreach (AiTraitCondition c in rule.All)
            {
                if (!EvaluateCondition(c, register, out bool ok))
                    return false;
                if (!ok)
                    return true;
            }
        }

        if (rule.None != null)
        {
            foreach (AiTraitCondition c in rule.None)
            {
                if (!EvaluateCondition(c, register, out bool ok))
                    return false;
                if (ok)
                    return true;
            }
        }

        if (rule.Any != null && rule.Any.Count > 0)
        {
            bool anyHit = false;
            foreach (AiTraitCondition c in rule.Any)
            {
                if (!EvaluateCondition(c, register, out bool ok))
                    return false;
                if (ok)
                {
                    anyHit = true;
                    break;
                }
            }
            if (!anyHit)
                return true;
        }

        hit = true;
        return true;
    }

    /// <summary>
    /// 求值单个条件。返回 false 表示条件本身写错（变量名拼错、越界等），
    /// 此时调用方应放弃该词条而不是当成"未命中"，避免写错的条件被静默忽略。
    /// </summary>
    public static bool EvaluateCondition(AiTraitCondition condition, long register, out bool hit)
    {
        hit = false;
        if (condition == null || string.IsNullOrWhiteSpace(condition.Expr))
            return false;

        string expr = Expand(condition.Expr, register);
        VariableTerm term = AiVariableAccess.Resolve(expr, out string error);
        if (term == null)
        {
            AiTraitDiagnostics.Report($"条件表达式无法解析：{expr}（{error}）");
            return false;
        }

        try
        {
            if (term.GetEraType() == EraType.String)
            {
                string actual = term.GetStrValue(GlobalStatic.EMediator) ?? "";
                string expected = condition.Text ?? "";
                hit = (condition.Op ?? "eq").ToLowerInvariant() switch
                {
                    "ne" or "!=" => !string.Equals(actual, expected, StringComparison.Ordinal),
                    "contains" => actual.Contains(expected, StringComparison.Ordinal),
                    "notcontains" => !actual.Contains(expected, StringComparison.Ordinal),
                    _ => string.Equals(actual, expected, StringComparison.Ordinal),
                };
                return true;
            }

            long value = term.GetIntValue(GlobalStatic.EMediator);
            hit = (condition.Op ?? ">=").ToLowerInvariant() switch
            {
                ">" => value > condition.Value,
                "<" => value < condition.Value,
                "<=" => value <= condition.Value,
                "==" or "eq" => value == condition.Value,
                "!=" or "ne" => value != condition.Value,
                "between" => value >= condition.Value && value <= condition.Value2,
                _ => value >= condition.Value,
            };
            return true;
        }
        catch (Exception e)
        {
            AiTraitDiagnostics.Report($"条件求值失败：{expr}（{e.Message}）");
            return false;
        }
    }

    private static string Expand(string expr, long register)
    {
        if (string.IsNullOrEmpty(expr))
            return expr;
        return expr.Replace(CharaPlaceholder, register.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// 运行期诊断收集。词条写错时不能静默忽略，否则人工调词条会完全看不到反馈。
/// </summary>
internal static class AiTraitDiagnostics
{
    private static readonly object gate = new();
    private static readonly List<string> entries = [];

    public static IReadOnlyList<string> Entries
    {
        get { lock (gate) return entries.ToArray(); }
    }

    public static void Report(string message)
    {
        if (string.IsNullOrEmpty(message))
            return;
        lock (gate)
        {
            entries.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
            if (entries.Count > 200)
                entries.RemoveRange(0, entries.Count - 200);
        }
    }

    public static void Clear()
    {
        lock (gate)
            entries.Clear();
    }
}
