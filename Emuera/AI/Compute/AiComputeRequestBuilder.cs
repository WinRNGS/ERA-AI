using MinorShift.Emuera.AI.Traits;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace MinorShift.Emuera.AI.Compute;

/// <summary>
/// 副 API 的请求装配。必须在界面线程调用（要读 ERA 变量做状态快照）。
///
/// 装配结果 <see cref="AiComputeRequest"/> 是纯数据，可以安全地带到后台线程去发请求。
/// 这条边界是整个 P3 的关键：界面线程只做"读变量 → 装数据"和"校验 → 写变量"，
/// 中间的网络与解析全在后台线程，且后台线程手上只有纯数据，碰不到变量层。
/// </summary>
internal static class AiComputeRequestBuilder
{
    /// <summary>
    /// 为本轮事件装配副 API 请求。charaNo 为角色号；失败时返回 null 并给出原因。
    /// </summary>
    public static AiComputeRequest Build(long charaNo, string eventText, long ticket, out string error)
    {
        error = null;

        AiComputeTemplate template = AiTraitLibrary.ComputeTemplate;
        if (template == null)
        {
            error = "词条库里没有 compute 段，副 API 无契约可用";
            return null;
        }
        if (!template.Enabled)
        {
            error = "compute.enabled = false，副 API 已在词条库层面停用";
            return null;
        }

        List<AiComputeField> fields = CollectValidFields(template);
        if (fields.Count == 0)
        {
            error = "compute.writable_fields 里没有一条可用字段（见词条诊断）";
            return null;
        }

        AiStateSnapshotData snapshot = AiStateSnapshot.Build(charaNo, template.IncludeAllCharas, out string snapshotError);
        if (snapshot.IsEmpty)
        {
            error = $"数值状态快照为空，拒绝调用副 API（{snapshotError ?? "无可读字段"}）";
            return null;
        }

        // 本轮角色必须出现在快照里。只要有全局字段（例如所持金）可读，快照就不算空，
        // 因此"角色未登录"不会被上面那道检查挡住。这时若放行，schema 的 chara_no 枚举是空的，
        // 模型只能自己编一个角色号 —— 那等于把数值写到不确定的人身上。
        if (charaNo >= 0 && !HasChara(snapshot, charaNo))
        {
            error = $"角色号 {charaNo} 不在数值状态快照里（未登录或被跳过），拒绝调用副 API";
            return null;
        }

        var request = new AiComputeRequest
        {
            Ticket = ticket,
            TurnId = $"t_{ticket:D6}",
            CharaNo = charaNo,
            Fields = fields,
            Template = template,
            EventText = eventText ?? "",
            StateJson = snapshot.ToJson(),
            SchemaJson = BuildSchema(fields, snapshot),
        };

        request.Messages = BuildMessages(request, template);
        return request;
    }

    private static bool HasChara(AiStateSnapshotData snapshot, long charaNo)
    {
        foreach (AiStateCharaEntry entry in snapshot.Charas)
        {
            if (entry.CharaNo == charaNo)
                return true;
        }
        return false;
    }

    /// <summary>
    /// 只保留通过静态校验的字段。写错的字段在这里就被剔掉，不会进入 schema，
    /// 因此模型不可能引用到一个坏字段——错误在配置阶段就被拦住，而不是等到写入时才失败。
    /// </summary>
    public static List<AiComputeField> CollectValidFields(AiComputeTemplate template)
    {
        var result = new List<AiComputeField>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (template.WritableFields == null)
            return result;

        foreach (AiComputeField f in template.WritableFields)
        {
            if (f == null || string.IsNullOrWhiteSpace(f.Field) || string.IsNullOrWhiteSpace(f.Target))
                continue;
            if (!seen.Add(f.Field))
                continue;
            if (!AiVariableAccess.IsWritableTargetName(f.Target, out string reason))
            {
                AiTraitDiagnostics.Report($"compute 字段 {f.Field} 被排除：{reason}");
                continue;
            }
            bool anyOpOk = false;
            foreach (string op in f.EffectiveOps)
            {
                if (AiVariableAccess.IsAllowedOp(op))
                {
                    anyOpOk = true;
                    break;
                }
            }
            if (!anyOpOk)
            {
                AiTraitDiagnostics.Report($"compute 字段 {f.Field} 被排除：没有任何受支持的操作符");
                continue;
            }
            result.Add(f);
        }
        return result;
    }

    /// <summary>
    /// 生成 function calling 的 parameters schema。
    /// field 用 enum 而不是自由字符串，是为了让模型在结构上就无法引用未声明的字段。
    /// </summary>
    private static string BuildSchema(List<AiComputeField> fields, AiStateSnapshotData snapshot)
    {
        var ops = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (AiComputeField f in fields)
        {
            foreach (string op in f.EffectiveOps)
            {
                if (AiVariableAccess.IsAllowedOp(op))
                    ops.Add(op.ToLowerInvariant());
            }
        }

        var charaNos = new List<long>();
        foreach (AiStateCharaEntry chara in snapshot.Charas)
            charaNos.Add(chara.CharaNo);

        var buffer = new System.IO.MemoryStream();
        using (var w = new Utf8JsonWriter(buffer, new JsonWriterOptions
        {
            Indented = false,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }))
        {
            w.WriteStartObject();
            w.WriteString("type", "object");

            w.WriteStartObject("properties");

            w.WriteStartObject("schema_version");
            w.WriteString("type", "string");
            w.WriteString("description", $"固定填 {AiComputeDefaults.SchemaVersion}");
            w.WriteEndObject();

            w.WriteStartObject("turn_id");
            w.WriteString("type", "string");
            w.WriteString("description", "原样回填本轮 system 消息里给出的 turn_id");
            w.WriteEndObject();

            w.WriteStartObject("changes");
            w.WriteString("type", "array");
            w.WriteString("description", "本轮数值变更；没有实质事件时返回空数组");
            w.WriteStartObject("items");
            w.WriteString("type", "object");
            w.WriteStartObject("properties");

            w.WriteStartObject("field");
            w.WriteString("type", "string");
            w.WriteStartArray("enum");
            foreach (AiComputeField f in fields)
                w.WriteStringValue(f.Field);
            w.WriteEndArray();
            w.WriteString("description", BuildFieldDescription(fields));
            w.WriteEndObject();

            w.WriteStartObject("chara_no");
            w.WriteString("type", "integer");
            if (charaNos.Count > 0)
            {
                w.WriteStartArray("enum");
                foreach (long no in charaNos)
                    w.WriteNumberValue(no);
                w.WriteEndArray();
            }
            w.WriteString("description", "角色号，取自权威状态里的 chara_no；全局字段填 -1");
            w.WriteEndObject();

            w.WriteStartObject("op");
            w.WriteString("type", "string");
            w.WriteStartArray("enum");
            foreach (string op in ops)
                w.WriteStringValue(op);
            w.WriteEndArray();
            w.WriteString("description", "add 为增量（可为负），set 为直接赋值，mul 为倍乘");
            w.WriteEndObject();

            w.WriteStartObject("value");
            w.WriteString("type", "integer");
            w.WriteString("description", "整数。op=add 时是变化量，op=set 时是目标值");
            w.WriteEndObject();

            w.WriteStartObject("reason");
            w.WriteString("type", "string");
            w.WriteString("description", "一句话说明依据，便于人工复盘");
            w.WriteEndObject();

            w.WriteEndObject();
            w.WriteStartArray("required");
            w.WriteStringValue("field");
            w.WriteStringValue("op");
            w.WriteStringValue("value");
            w.WriteEndArray();
            w.WriteEndObject();
            w.WriteEndObject();

            w.WriteStartObject("narrative_hint");
            w.WriteString("type", "string");
            w.WriteString("description", "一句结果提示，供叙事模型参考；不要写具体数字");
            w.WriteEndObject();

            w.WriteStartObject("warnings");
            w.WriteString("type", "array");
            w.WriteStartObject("items");
            w.WriteString("type", "string");
            w.WriteEndObject();
            w.WriteString("description", "你自己不确定的地方");
            w.WriteEndObject();

            w.WriteEndObject();

            w.WriteStartArray("required");
            w.WriteStringValue("schema_version");
            w.WriteStringValue("turn_id");
            w.WriteStringValue("changes");
            w.WriteEndArray();

            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static string BuildFieldDescription(List<AiComputeField> fields)
    {
        var sb = new StringBuilder("可改字段：");
        for (int i = 0; i < fields.Count; i++)
        {
            AiComputeField f = fields[i];
            if (i > 0)
                sb.Append('；');
            sb.Append(f.Field);
            if (!string.IsNullOrWhiteSpace(f.Description))
                sb.Append('=').Append(f.Description.Trim());
            if (f.MaxDelta > 0)
                sb.Append($"（单轮幅度 ≤ {f.MaxDelta}）");
        }
        return sb.ToString();
    }

    private static List<ChatMessage> BuildMessages(AiComputeRequest request, AiComputeTemplate template)
    {
        var messages = new List<ChatMessage>
        {
            new()
            {
                Role = "system",
                Content = string.IsNullOrWhiteSpace(template.SystemPrompt)
                    ? AiComputeDefaults.SystemPrompt
                    : template.SystemPrompt,
            },
        };

        // 短记忆在权威状态之前：让"当前值"成为模型看到的最后一份状态信息，
        // 避免它把旧轮次里的数值当成现值（RISK-02）。
        int rounds = Math.Max(0, Math.Min(template.MemoryRounds, AiComputeMemory.MaxRounds));
        IReadOnlyList<AiComputeMemoryEntry> memory = AiComputeMemory.Recent(rounds);
        if (memory.Count > 0)
        {
            var sb = new StringBuilder("以下是最近几轮的结算记录，仅供理解剧情走向，其中的数值已经过时，不要用来推算当前值：\n");
            foreach (AiComputeMemoryEntry entry in memory)
                sb.AppendLine($"- [{entry.TurnId}] 事件：{entry.EventText}｜结果：{entry.Summary}");
            messages.Add(new ChatMessage { Role = "system", Content = sb.ToString().TrimEnd() });
        }

        messages.Add(new ChatMessage
        {
            Role = "system",
            Content = $"""
本轮 turn_id：{request.TurnId}
schema_version：{AiComputeDefaults.SchemaVersion}

【权威状态｜唯一真值来源】
{request.StateJson}
""",
        });

        messages.Add(new ChatMessage
        {
            Role = "user",
            Content = $"本轮事件：{request.EventText}",
        });

        return messages;
    }
}

/// <summary>
/// 一次副 API 请求的全部输入。纯数据，可跨线程传递。
/// Fields 与 Template 是词条库对象的引用，只读不写。
/// </summary>
internal sealed class AiComputeRequest
{
    public long Ticket;
    public string TurnId;
    public long CharaNo;
    public string EventText;
    public string StateJson;
    public string SchemaJson;
    public List<ChatMessage> Messages = [];
    public List<AiComputeField> Fields = [];
    public AiComputeTemplate Template;

    public AiComputeField FindField(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;
        foreach (AiComputeField f in Fields)
        {
            if (string.Equals(f.Field, name, StringComparison.OrdinalIgnoreCase))
                return f;
        }
        return null;
    }
}

/// <summary>
/// 副 API 的短记忆窗口。独立于主 API 对话历史（两者的用途与生命周期都不同）。
/// 只存"事件 + 结算摘要"，不存权威数值——存了数值就一定会有人拿它当现值用。
/// </summary>
internal static class AiComputeMemory
{
    /// <summary>设计已定 3-5 轮，取上限 5。</summary>
    public const int MaxRounds = 5;

    private static readonly object gate = new();
    private static readonly List<AiComputeMemoryEntry> entries = [];

    public static IReadOnlyList<AiComputeMemoryEntry> All
    {
        get { lock (gate) return entries.ToArray(); }
    }

    public static IReadOnlyList<AiComputeMemoryEntry> Recent(int count)
    {
        if (count <= 0)
            return Array.Empty<AiComputeMemoryEntry>();
        lock (gate)
        {
            int start = Math.Max(0, entries.Count - count);
            return entries.GetRange(start, entries.Count - start).ToArray();
        }
    }

    public static void Add(string turnId, string eventText, string summary)
    {
        lock (gate)
        {
            entries.Add(new AiComputeMemoryEntry
            {
                TurnId = turnId,
                EventText = Trim(eventText, 80),
                Summary = Trim(summary, 160),
            });
            while (entries.Count > MaxRounds)
                entries.RemoveAt(0);
        }
    }

    public static void Clear()
    {
        lock (gate)
            entries.Clear();
    }

    private static string Trim(string text, int max)
    {
        if (string.IsNullOrEmpty(text))
            return "";
        text = text.Replace("\r", " ").Replace("\n", " ").Trim();
        return text.Length <= max ? text : text[..max] + "\u2026";
    }
}

internal sealed class AiComputeMemoryEntry
{
    public string TurnId;
    public string EventText;
    public string Summary;
}