using System;
using System.Collections.Generic;

namespace MinorShift.Emuera.AI.Traits;

/// <summary>
/// 冲突检测与消解。全部在本地完成，不消耗任何 token。
///
/// 三级冲突（对应设计文档 P2）：
///   硬冲突 hard —— 两条禁止共存。优先级低的一方整条丢弃；优先级相同时按 id 字典序保留靠前者，
///                   保证同一份词条库在同样输入下结果稳定可复现。
///   软冲突 soft —— 两条共存，但低优先级方的指定字段被抹掉（默认抹 speech_style 与 constraints，
///                   保留 description，因为描述性文本互相叠加通常不矛盾）。
///   条件冲突     —— 由 AiTraitMatcher 的 modifier 在匹配阶段处理，不在这里。
///
/// 冲突是无向的：只要任一方声明了冲突就生效，不要求双方都写。
/// </summary>
internal static class AiTraitConflictResolver
{
    private static readonly string[] DefaultSuppress = ["speech_style", "constraints"];

    public static List<AiTraitInstance> Resolve(List<AiTraitInstance> candidates)
    {
        if (candidates == null || candidates.Count <= 1)
            return candidates ?? [];

        var survivors = new List<AiTraitInstance>(candidates);

        // 第一轮：硬冲突。反复扫描直到没有可淘汰项，避免 A 淘汰 B 后 B 的冲突关系还残留影响。
        bool changed = true;
        while (changed)
        {
            changed = false;
            for (int i = 0; i < survivors.Count && !changed; i++)
            {
                for (int j = i + 1; j < survivors.Count; j++)
                {
                    AiTraitInstance a = survivors[i];
                    AiTraitInstance b = survivors[j];
                    if (!TryGetConflict(a, b, out AiTraitConflict rule, out bool declaredByA))
                        continue;
                    if (!IsHard(rule))
                        continue;

                    AiTraitInstance loser = PickLoser(a, b);
                    AiTraitDiagnostics.Report($"硬冲突：{a.Id} × {b.Id}，丢弃 {loser.Id}（声明方 {(declaredByA ? a.Id : b.Id)}）");
                    survivors.Remove(loser);
                    changed = true;
                    break;
                }
            }
        }

        // 第二轮：软冲突。只抑制字段，不淘汰。
        for (int i = 0; i < survivors.Count; i++)
        {
            for (int j = i + 1; j < survivors.Count; j++)
            {
                AiTraitInstance a = survivors[i];
                AiTraitInstance b = survivors[j];
                if (!TryGetConflict(a, b, out AiTraitConflict rule, out _))
                    continue;
                if (IsHard(rule))
                    continue;

                AiTraitInstance loser = PickLoser(a, b);
                Suppress(loser, rule);
            }
        }

        return survivors;
    }

    private static bool TryGetConflict(AiTraitInstance a, AiTraitInstance b, out AiTraitConflict rule, out bool declaredByA)
    {
        rule = null;
        declaredByA = false;
        if (a?.Trait == null || b?.Trait == null)
            return false;

        rule = FindRule(a.Trait, b.Id);
        if (rule != null)
        {
            declaredByA = true;
            return true;
        }
        rule = FindRule(b.Trait, a.Id);
        return rule != null;
    }

    private static AiTraitConflict FindRule(AiTrait trait, string otherId)
    {
        if (trait.Conflicts == null || string.IsNullOrEmpty(otherId))
            return null;
        foreach (AiTraitConflict c in trait.Conflicts)
        {
            if (c != null && string.Equals(c.With, otherId, StringComparison.OrdinalIgnoreCase))
                return c;
        }
        return null;
    }

    /// <summary>kind 写错时按 hard 处理：宁可少一条词条，也不要让矛盾人格同时进 prompt。</summary>
    private static bool IsHard(AiTraitConflict rule)
        => !string.Equals(rule.Kind, "soft", StringComparison.OrdinalIgnoreCase);

    /// <summary>先比 priority，再比本轮得分，最后按 id 字典序，保证结果确定。</summary>
    private static AiTraitInstance PickLoser(AiTraitInstance a, AiTraitInstance b)
    {
        if (a.Priority != b.Priority)
            return a.Priority < b.Priority ? a : b;
        if (a.Score != b.Score)
            return a.Score < b.Score ? a : b;
        return string.CompareOrdinal(a.Id, b.Id) > 0 ? a : b;
    }

    private static void Suppress(AiTraitInstance loser, AiTraitConflict rule)
    {
        IReadOnlyList<string> fields = rule.Suppress != null && rule.Suppress.Count > 0
            ? rule.Suppress
            : DefaultSuppress;

        foreach (string field in fields)
        {
            switch ((field ?? "").ToLowerInvariant())
            {
                case "description":
                    loser.Description = null;
                    break;
                case "speech_style":
                case "speechstyle":
                    loser.SpeechStyle = null;
                    break;
                case "constraints":
                    loser.Constraints.Clear();
                    break;
                default:
                    AiTraitDiagnostics.Report($"软冲突 suppress 字段无法识别：{field}");
                    break;
            }
        }
        AiTraitDiagnostics.Report($"软冲突：抑制 {loser.Id} 的 {string.Join("/", fields)}");
    }
}
