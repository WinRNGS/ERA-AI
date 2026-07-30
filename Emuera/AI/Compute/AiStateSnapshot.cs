using MinorShift.Emuera.AI.Traits;
using MinorShift.Emuera.Runtime.Script;
using MinorShift.Emuera.Runtime.Script.Statements.Variable;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace MinorShift.Emuera.AI.Compute;

/// <summary>
/// 权威数值状态快照。每轮完整传给副 API，对应 RISK-02（数值漂移）的核心缓解措施：
/// 副 API 永远不需要、也不允许从历史记忆推算当前值。
///
/// 字段定义复用 P2 的 prompt.state_fields，再并上 compute.extra_state_fields。
/// 两边共用同一份定义是有意为之——如果各写一份，主 API 看到的状态和副 API 看到的状态
/// 早晚会不一致，而这种不一致在运行期完全不报错。
///
/// 必须在界面线程调用：要读 ERA 变量。
/// </summary>
internal static class AiStateSnapshot
{
    /// <summary>
    /// 为指定角色生成快照。charaNo 为角色号（非登录号）。
    /// includeAllCharas 为 true 时把所有已登录角色都写进去。
    /// </summary>
    public static AiStateSnapshotData Build(long charaNo, bool includeAllCharas, out string error)
    {
        error = null;
        var data = new AiStateSnapshotData();

        if (GlobalStatic.EMediator == null || GlobalStatic.VEvaluator == null || GlobalStatic.VariableData == null)
        {
            error = "引擎尚未就绪，无法采集数值状态";
            return data;
        }

        List<AiStateField> fields = CollectFields();
        if (fields.Count == 0)
        {
            error = "未定义任何状态字段（prompt.state_fields 与 compute.extra_state_fields 都是空的）";
            return data;
        }

        foreach (AiStateField field in fields)
        {
            if (field == null || string.IsNullOrWhiteSpace(field.Expr))
                continue;
            if (IsCharaScoped(field.Expr))
                continue;
            if (TryRead(field.Expr, out string text, out string readError))
                data.Global[Label(field)] = text;
            else
                AiTraitDiagnostics.Report($"全局状态字段无法读取：{field.Expr}（{readError}）");
        }

        var targets = new List<long>();
        if (includeAllCharas)
        {
            foreach (CharacterData chara in GlobalStatic.VariableData.CharacterList)
                targets.Add(chara.NO);
        }
        else if (charaNo >= 0)
        {
            targets.Add(charaNo);
        }

        foreach (long no in targets)
        {
            long register = GlobalStatic.VEvaluator.GetChara(no);
            if (register < 0)
            {
                AiTraitDiagnostics.Report($"状态快照跳过未登录角色号 {no}");
                continue;
            }

            var entry = new AiStateCharaEntry
            {
                CharaNo = no,
                Name = ReadStr($"NAME:{register}"),
                CallName = ReadStr($"CALLNAME:{register}"),
            };

            foreach (AiStateField field in fields)
            {
                if (field == null || string.IsNullOrWhiteSpace(field.Expr) || !IsCharaScoped(field.Expr))
                    continue;
                string expr = Expand(field.Expr, register);
                if (TryRead(expr, out string text, out string readError))
                    entry.Fields[Label(field)] = text;
                else
                    AiTraitDiagnostics.Report($"角色状态字段无法读取：{expr}（{readError}）");
            }

            data.Charas.Add(entry);
        }

        if (data.Global.Count == 0 && data.Charas.Count == 0)
            error = "状态快照为空：全部字段都无法读取，请跑「显示词条诊断」核对 expr";
        return data;
    }

    /// <summary>prompt.state_fields + compute.extra_state_fields，按 label 去重（前者优先）。</summary>
    public static List<AiStateField> CollectFields()
    {
        var result = new List<AiStateField>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddAll(List<AiStateField> source)
        {
            if (source == null)
                return;
            foreach (AiStateField f in source)
            {
                if (f == null || string.IsNullOrWhiteSpace(f.Expr))
                    continue;
                if (!seen.Add(Label(f)))
                    continue;
                result.Add(f);
            }
        }

        AddAll(AiTraitLibrary.PromptTemplate?.StateFields);
        AddAll(AiTraitLibrary.ComputeTemplate?.ExtraStateFields);
        return result;
    }

    private static string Label(AiStateField field)
        => string.IsNullOrWhiteSpace(field.Label) ? field.Expr : field.Label.Trim();

    private static bool IsCharaScoped(string expr)
        => expr.Contains(AiTraitMatcher.CharaPlaceholder, StringComparison.OrdinalIgnoreCase);

    private static string Expand(string expr, long register)
        => expr.Replace(AiTraitMatcher.CharaPlaceholder, register.ToString(CultureInfo.InvariantCulture),
            StringComparison.OrdinalIgnoreCase);

    private static bool TryRead(string expr, out string text, out string error)
    {
        text = null;
        VariableTerm term = AiVariableAccess.Resolve(expr, out error);
        if (term == null)
            return false;
        try
        {
            text = term.GetEraType() == EraType.String
                ? term.GetStrValue(GlobalStatic.EMediator)
                : term.GetIntValue(GlobalStatic.EMediator).ToString(CultureInfo.InvariantCulture);
            return true;
        }
        catch (Exception e)
        {
            error = e.Message;
            return false;
        }
    }

    private static string ReadStr(string expr)
    {
        VariableTerm term = AiVariableAccess.Resolve(expr, out _);
        if (term == null || term.GetEraType() != EraType.String)
            return "";
        try
        {
            return term.GetStrValue(GlobalStatic.EMediator) ?? "";
        }
        catch
        {
            return "";
        }
    }
}

/// <summary>快照数据。刻意做成纯数据对象，可以安全地带到后台线程去序列化与发送。</summary>
internal sealed class AiStateSnapshotData
{
    /// <summary>全局项，如所持金、日期。</summary>
    public Dictionary<string, string> Global = new(StringComparer.Ordinal);

    /// <summary>角色项。</summary>
    public List<AiStateCharaEntry> Charas = [];

    public bool IsEmpty => Global.Count == 0 && Charas.Count == 0;

    /// <summary>
    /// 序列化为副 API 能吃的 JSON。写成缩进格式是有意的：
    /// 出问题时这段文本会原样进日志，人要能一眼看懂当时到底传了什么。
    /// </summary>
    public string ToJson()
    {
        var options = new JsonWriterOptions
        {
            Indented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        var buffer = new System.IO.MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, options))
        {
            writer.WriteStartObject();

            writer.WriteStartObject("global");
            foreach (KeyValuePair<string, string> kv in Global)
                writer.WriteString(kv.Key, kv.Value);
            writer.WriteEndObject();

            writer.WriteStartArray("charas");
            foreach (AiStateCharaEntry chara in Charas)
            {
                writer.WriteStartObject();
                writer.WriteNumber("chara_no", chara.CharaNo);
                writer.WriteString("name", chara.Name ?? "");
                writer.WriteString("call_name", chara.CallName ?? "");
                writer.WriteStartObject("fields");
                foreach (KeyValuePair<string, string> kv in chara.Fields)
                    writer.WriteString(kv.Key, kv.Value);
                writer.WriteEndObject();
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}

internal sealed class AiStateCharaEntry
{
    public long CharaNo;
    public string Name;
    public string CallName;
    public Dictionary<string, string> Fields = new(StringComparer.Ordinal);
}