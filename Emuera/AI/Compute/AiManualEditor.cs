using MinorShift.Emuera.AI.Traits;
using MinorShift.Emuera.Runtime.Script.Statements.Variable;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace MinorShift.Emuera.AI.Compute;

/// <summary>
/// 玩家手动调整数值的入口。必须在界面线程调用（要读写 ERA 变量）。
///
/// 为什么要有这一条路：这个项目的目的是让玩家玩得开心，而不是被经济/战斗系统的难度、
/// 或者一次自己都觉得不合理的 AI 结算卡住。允许玩家改数值不是妥协，是明确的设计选择。
///
/// 与副 API 通道的关系：
///   - 可改范围相同（都限于 compute.writable_fields），所以不会因为开放手改而多出一堆
///     "只有玩家能碰"的变量，配置仍然只有一处。
///   - 校验强度不同。副 API 要过 max_delta 与 min/max，玩家不过——那两道闸门是为了挡
///     模型幻觉，而模型不知道自己在胡说，玩家知道自己在做什么。
///   - 引擎级校验（白名单、类型、下标越界）对两者一视同仁：允许作弊不等于允许写坏存档。
///   - 手改同样产出带 Before 的 AiAppliedChange，因此和副 API 的写入一样可撤销。
/// </summary>
internal static class AiManualEditor
{
    /// <summary>
    /// 当前可手动调整的全部条目。范围与副 API 完全一致：compute.writable_fields
    /// 里通过静态筛选的字段，乘以「全局项一条 + 每个已登录角色一条」。
    ///
    /// 刻意列出所有已登录角色而不是只列当前 TARGET：手动调整的使用场景往往就是
    /// "刚才那个角色的数值不对"，而那时 TARGET 可能已经换人了。
    /// </summary>
    public static List<AiEditableEntry> CollectEditable(out string error)
    {
        error = null;
        var result = new List<AiEditableEntry>();

        AiComputeTemplate template = AiTraitLibrary.ComputeTemplate;
        if (template == null)
        {
            error = "词条库里没有 compute 段，没有声明任何可调整字段";
            return result;
        }
        if (GlobalStatic.VEvaluator == null || GlobalStatic.VariableData == null)
        {
            error = "引擎尚未就绪";
            return result;
        }

        // 这里不看 template.Enabled：停用副 API 是"别让模型改"，不是"别让玩家改"。
        List<AiComputeField> fields = AiComputeRequestBuilder.CollectValidFields(template);
        if (fields.Count == 0)
        {
            error = "compute.writable_fields 里没有一条可用字段（见词条诊断）";
            return result;
        }

        foreach (AiComputeField field in fields)
        {
            if (field.IsCharaScoped)
                continue;
            if (!AiVariableAccess.TryReadInt(field.Target, out long value, out string readError))
            {
                AiTraitDiagnostics.Report($"手动调整跳过全局字段 {field.Field}：{readError}");
                continue;
            }
            result.Add(new AiEditableEntry
            {
                Field = field,
                CharaNo = -1,
                Target = field.Target,
                Current = value,
            });
        }

        foreach (CharacterData chara in GlobalStatic.VariableData.CharacterList)
        {
            long register = GlobalStatic.VEvaluator.GetChara(chara.NO);
            if (register < 0)
                continue;

            foreach (AiComputeField field in fields)
            {
                if (!field.IsCharaScoped)
                    continue;
                string target = field.Target.Replace(AiTraitMatcher.CharaPlaceholder,
                    register.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);
                if (!AiVariableAccess.TryReadInt(target, out long value, out string readError))
                {
                    AiTraitDiagnostics.Report($"手动调整跳过 {target}：{readError}");
                    continue;
                }
                result.Add(new AiEditableEntry
                {
                    Field = field,
                    CharaNo = chara.NO,
                    CharaName = ReadName(register, chara.NO),
                    Target = target,
                    Current = value,
                });
            }
        }

        if (result.Count == 0)
            error = "没有任何可读的可调整字段，请跑「显示词条诊断」核对 target";
        return result;
    }

    /// <summary>
    /// 提交一批手动调整。edits 里的 Value 是最终值而不是增量。
    /// 成功时 applied 带写入前的值，可用于撤销。
    /// </summary>
    public static bool TryApply(IReadOnlyList<AiManualEdit> edits, out List<AiAppliedChange> applied, out string error)
    {
        if (!AiComputeApplier.TryApplyManual(edits, out applied, out error))
            return false;
        if (applied.Count == 0)
            return true;

        // 手改进短记忆：副 API 下一轮会看到这些数值，若不解释来源它会试图"圆"这个跳变，
        // 那正是数值漂移的开端。写成"玩家手动调整"能让它把这次变化当成既定事实接受。
        AiComputeMemory.Add($"manual_{DateTime.Now:HHmmss}", "玩家手动调整了数值",
            AiComputeApplier.Summarize(applied));

        // 手改之后上一轮的 Before 快照不再对应"上一轮写入前"的状态，
        // 拿它去撤销会把手改的结果一起抹掉。所以手改就作废撤销。
        AiDispatcher.InvalidateUndo();
        return true;
    }

    private static string ReadName(long register, long charaNo)
    {
        if (AiVariableAccess.TryReadStr($"NAME:{register}", out string name, out _) && !string.IsNullOrEmpty(name))
            return name;
        return $"角色{charaNo}";
    }
}