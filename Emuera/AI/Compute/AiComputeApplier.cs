using MinorShift.Emuera.AI.Traits;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MinorShift.Emuera.AI.Compute;

/// <summary>
/// 副 API 输出的校验与回写。必须在界面线程调用。
///
/// 三层校验，任一层不过整批拒绝（对应 RISK-01 幻觉污染存档）：
///   1. 字段层：field 必须在本轮 schema 的字段表里；op 必须是该字段允许的；角色号必须已登录。
///   2. 幅度层：|变化量| 不得超过 max_delta。这一层是挡幻觉的主力——
///      "好感度 +3" 与 "好感度 +3000" 在语义上都成立，只有幅度能区分出后者是胡说。
///   3. 区间层：最终值必须落在 [min, max]。越界按 on_out_of_range 处置（默认 clamp）。
///
/// 写入走 AiVariableAccess.TryApplyAll，因此白名单、类型、下标越界这些引擎级校验一个都不少。
/// 本类算出的都是最终值，统一以 op=set 提交，避免"校验时算的是一个值、写入时引擎又算一次"的偏差。
/// </summary>
internal static class AiComputeApplier
{
    /// <summary>
    /// 校验并回写。成功返回 true，applied 为已落盘的变更（含写入前的值，可用于回滚）。
    /// 失败返回 false，error 说明原因，且保证一个字节都没写进去。
    /// </summary>
    public static bool TryApply(
        AiComputeRequest request,
        AiComputeResult result,
        out List<AiAppliedChange> applied,
        out string error)
    {
        applied = [];
        error = null;

        if (request == null || result == null)
        {
            error = "副 API 请求或结果为空";
            return false;
        }

        if (result.Changes.Count == 0)
            return true;

        int maxChanges = request.Template?.MaxChanges ?? 0;
        if (maxChanges > 0 && result.Changes.Count > maxChanges)
        {
            error = $"副 API 一次提交 {result.Changes.Count} 项变更，超过上限 {maxChanges}，整批拒绝";
            return false;
        }

        bool clamp = !string.Equals(request.Template?.OnOutOfRange, "reject", StringComparison.OrdinalIgnoreCase);

        var pending = new List<AiAppliedChange>();
        var targetsSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (AiComputeChange change in result.Changes)
        {
            AiComputeField field = request.FindField(change.Field);
            if (field == null)
            {
                error = $"副 API 引用了未声明的字段「{change.Field}」，整批拒绝";
                return false;
            }

            if (!IsOpAllowed(field, change.Op))
            {
                error = $"字段「{field.Field}」不允许操作符 {change.Op}（允许：{string.Join("/", field.EffectiveOps)}），整批拒绝";
                return false;
            }
            if (!AiVariableAccess.IsAllowedOp(change.Op))
            {
                error = $"不支持的操作符 {change.Op}，整批拒绝";
                return false;
            }

            if (!TryResolveTarget(field, change, out string target, out string resolveError))
            {
                error = resolveError;
                return false;
            }

            // 同一变量被改两次时，第二次是基于旧值算的，写下去必然有一个被吞掉。
            // 与其静默取后者，不如整批拒绝并让模型的问题暴露出来。
            if (!targetsSeen.Add(target))
            {
                error = $"副 API 在同一轮里重复改动 {target}（字段「{field.Field}」），整批拒绝";
                return false;
            }

            if (!AiVariableAccess.TryReadInt(target, out long before, out string readError))
            {
                error = $"无法读取 {target} 的当前值（{readError}），整批拒绝";
                return false;
            }

            if (!TryComputeFinalValue(field, change, before, clamp, out long after, out string computeError))
            {
                error = computeError;
                return false;
            }

            pending.Add(new AiAppliedChange
            {
                Field = field.Field,
                Target = target,
                Op = change.Op,
                RequestedValue = change.Value,
                Before = before,
                After = after,
                Reason = change.Reason,
            });
        }

        var batch = new List<AiValueChange>(pending.Count);
        foreach (AiAppliedChange item in pending)
            batch.Add(new AiValueChange { Target = item.Target, Op = "set", Value = item.After });

        if (!AiVariableAccess.TryApplyAll(batch, out error))
            return false;

        applied = pending;
        return true;
    }

    /// <summary>
    /// 回滚一批已写入的变更（RISK-05：副 API 已写、主 API 失败时的补偿路径）。
    /// 用写入前的快照值直接 set 回去。必须在界面线程调用。
    /// </summary>
    public static bool TryRollback(IReadOnlyList<AiAppliedChange> applied, out string error)
    {
        error = null;
        if (applied == null || applied.Count == 0)
            return true;

        var batch = new List<AiValueChange>(applied.Count);
        for (int i = applied.Count - 1; i >= 0; i--)
            batch.Add(new AiValueChange { Target = applied[i].Target, Op = "set", Value = applied[i].Before });

        return AiVariableAccess.TryApplyAll(batch, out error);
    }

    /// <summary>
    /// 玩家手动调整数值。走的是和副 API 完全不同的一条路：
    /// 只保留字段层白名单与引擎级校验，**故意不做 max_delta 与 min/max 检查**。
    ///
    /// 为什么对玩家放宽：那两道闸门是为了挡模型幻觉而设的——模型不知道自己在胡说，
    /// 玩家知道自己在做什么。把「经济/战斗数值不合我意」变成一个可以直接改掉的问题，
    /// 比让玩家卡在一个他认为不合理的结算上更符合这个项目的目的。
    ///
    /// 返回的 applied 同样带 Before，因此手动调整也是可撤销的。必须在界面线程调用。
    /// </summary>
    public static bool TryApplyManual(
        IReadOnlyList<AiManualEdit> edits,
        out List<AiAppliedChange> applied,
        out string error)
    {
        applied = [];
        error = null;
        if (edits == null || edits.Count == 0)
            return true;

        var pending = new List<AiAppliedChange>();
        var targetsSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (AiManualEdit edit in edits)
        {
            if (edit?.Field == null)
            {
                error = "手动调整项为空";
                return false;
            }

            var change = new AiComputeChange { Field = edit.Field.Field, CharaNo = edit.CharaNo, Op = "set", Value = edit.Value };
            if (!TryResolveTarget(edit.Field, change, out string target, out string resolveError))
            {
                error = resolveError;
                return false;
            }
            if (!targetsSeen.Add(target))
            {
                error = $"同一次调整里重复设置了 {target}";
                return false;
            }
            if (!AiVariableAccess.TryReadInt(target, out long before, out string readError))
            {
                error = $"无法读取 {target} 的当前值（{readError}）";
                return false;
            }
            if (before == edit.Value)
                continue;

            pending.Add(new AiAppliedChange
            {
                Field = edit.Field.Field,
                Target = target,
                Op = "set",
                RequestedValue = edit.Value,
                Before = before,
                After = edit.Value,
                Reason = "玩家手动调整",
            });
        }

        if (pending.Count == 0)
            return true;

        var batch = new List<AiValueChange>(pending.Count);
        foreach (AiAppliedChange item in pending)
            batch.Add(new AiValueChange { Target = item.Target, Op = "set", Value = item.After });

        // 引擎级校验（白名单、类型、下标）仍然一个都不少：允许作弊不等于允许写坏存档。
        if (!AiVariableAccess.TryApplyAll(batch, out error))
            return false;

        applied = pending;
        return true;
    }

    private static bool IsOpAllowed(AiComputeField field, string op)
    {
        if (string.IsNullOrWhiteSpace(op))
            return false;
        foreach (string allowed in field.EffectiveOps)
        {
            if (string.Equals(allowed, op, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// 把字段模板里的 {CHARA} 换成登录号。
    /// 角色号 → 登录号的换算每轮现算，绝不缓存（RISK-21：登录号会随增删角色漂移）。
    /// </summary>
    private static bool TryResolveTarget(AiComputeField field, AiComputeChange change, out string target, out string error)
    {
        target = null;
        error = null;

        if (!field.IsCharaScoped)
        {
            target = field.Target;
            return true;
        }

        long charaNo = change.CharaNo;
        if (charaNo < 0)
        {
            error = $"字段「{field.Field}」是角色维度，但副 API 没给出有效 chara_no，整批拒绝";
            return false;
        }
        if (GlobalStatic.VEvaluator == null)
        {
            error = "引擎尚未就绪，无法换算角色号";
            return false;
        }

        long register = GlobalStatic.VEvaluator.GetChara(charaNo);
        if (register < 0)
        {
            error = $"副 API 指定的角色号 {charaNo} 未登录，整批拒绝";
            return false;
        }

        target = field.Target.Replace(AiTraitMatcher.CharaPlaceholder,
            register.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);
        return true;
    }

    private static bool TryComputeFinalValue(
        AiComputeField field,
        AiComputeChange change,
        long before,
        bool clamp,
        out long after,
        out string error)
    {
        error = null;
        try
        {
            after = change.Op.ToLowerInvariant() switch
            {
                "add" => checked(before + change.Value),
                "mul" => checked(before * change.Value),
                _ => change.Value,
            };
        }
        catch (OverflowException)
        {
            after = 0;
            error = $"字段「{field.Field}」的运算溢出（当前 {before}，{change.Op} {change.Value}），整批拒绝";
            return false;
        }

        long delta = Math.Abs(after - before);
        if (field.MaxDelta > 0 && delta > field.MaxDelta)
        {
            error = $"字段「{field.Field}」单轮变动 {delta} 超过上限 {field.MaxDelta}（{before} → {after}），整批拒绝";
            return false;
        }

        if (after < field.Min || after > field.Max)
        {
            // 区间写反（min > max）时任何值都越界，且钳制无解（Math.Clamp 会直接抛）。
            // 这是配置错误而不是模型问题，必须整批拒绝并把配置错误说清楚。
            if (field.Min > field.Max)
            {
                error = $"字段「{field.Field}」的 min({field.Min}) 大于 max({field.Max})，配置写反了，任何值都无法写入";
                return false;
            }
            if (!clamp)
            {
                error = $"字段「{field.Field}」的结果 {after} 超出允许区间 [{RangeText(field)}]，整批拒绝";
                return false;
            }
            long clamped = Math.Clamp(after, field.Min, field.Max);
            AiTraitDiagnostics.Report($"字段「{field.Field}」结果 {after} 超出 [{RangeText(field)}]，已钳到 {clamped}");
            after = clamped;
        }

        return true;
    }

    private static string RangeText(AiComputeField field)
    {
        string min = field.Min == long.MinValue ? "-∞" : field.Min.ToString(CultureInfo.InvariantCulture);
        string max = field.Max == long.MaxValue ? "+∞" : field.Max.ToString(CultureInfo.InvariantCulture);
        return $"{min}, {max}";
    }

    /// <summary>
    /// 反向摘要：把「撤销」这件事写成短记忆能读懂的一行。
    /// 撤销必须进短记忆，否则副 API 下一轮会看到一个没有来由的数值跳变，
    /// 然后试图"解释"它——那正是数值漂移的开端。
    /// </summary>
    public static string SummarizeReverse(IReadOnlyList<AiAppliedChange> applied)
    {
        if (applied == null || applied.Count == 0)
            return "无数值变化";
        var sb = new StringBuilder();
        foreach (AiAppliedChange item in applied)
        {
            if (sb.Length > 0)
                sb.Append('、');
            sb.Append($"{item.Field} {item.After}→{item.Before}（撤销）");
        }
        return sb.ToString();
    }

    /// <summary>把已写入的变更摆成一行摘要，用于短记忆与日志。</summary>
    public static string Summarize(IReadOnlyList<AiAppliedChange> applied)
    {
        if (applied == null || applied.Count == 0)
            return "无数值变化";
        var sb = new StringBuilder();
        foreach (AiAppliedChange item in applied)
        {
            if (sb.Length > 0)
                sb.Append('、');
            sb.Append($"{item.Field} {item.Before}→{item.After}");
        }
        return sb.ToString();
    }
}