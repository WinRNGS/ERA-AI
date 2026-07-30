using System;
using System.Collections.Generic;
using System.Text.Json;

namespace MinorShift.Emuera.AI.Compute;

/// <summary>
/// 副 API 输出的解析层。跑在后台线程，只做文本 → 数据结构的转换，不读也不写 ERA 变量。
///
/// 原则：**宁可整批拒绝，也不猜模型的意思**。
/// 缺字段、类型不对、turn_id 不匹配都直接失败，因为"猜错"的代价是把错误数值写进存档，
/// 而"整批拒绝"的代价只是本轮不改数值。两者不对等。
/// </summary>
internal static class AiComputeParser
{
    public static AiComputeResult Parse(string json, string expectedTurnId, out string error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            error = "副 API 返回内容为空";
            return null;
        }

        var result = new AiComputeResult { RawJson = json };
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                error = $"副 API 输出不是 JSON 对象（实际 {root.ValueKind}）";
                return null;
            }

            result.SchemaVersion = ReadString(root, "schema_version");
            if (!string.IsNullOrEmpty(result.SchemaVersion) &&
                !string.Equals(result.SchemaVersion, AiComputeDefaults.SchemaVersion, StringComparison.Ordinal))
            {
                error = $"schema_version 不匹配：期望 {AiComputeDefaults.SchemaVersion}，实际 {result.SchemaVersion}";
                return null;
            }

            result.TurnId = ReadString(root, "turn_id");
            if (!string.IsNullOrEmpty(expectedTurnId) &&
                !string.Equals(result.TurnId, expectedTurnId, StringComparison.Ordinal))
            {
                // 不一致意味着模型回的可能是上一轮的结果，写下去就是错轮次的数值。
                error = $"turn_id 不匹配：期望 {expectedTurnId}，实际 {(string.IsNullOrEmpty(result.TurnId) ? "缺失" : result.TurnId)}";
                return null;
            }

            result.NarrativeHint = ReadString(root, "narrative_hint");

            if (root.TryGetProperty("warnings", out JsonElement warnings) && warnings.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement w in warnings.EnumerateArray())
                {
                    string text = w.ValueKind == JsonValueKind.String ? w.GetString() : w.ToString();
                    if (!string.IsNullOrWhiteSpace(text))
                        result.Warnings.Add(text.Trim());
                }
            }

            if (!root.TryGetProperty("changes", out JsonElement changes))
            {
                error = "副 API 输出缺少 changes 字段";
                return null;
            }
            if (changes.ValueKind != JsonValueKind.Array)
            {
                error = $"changes 不是数组（实际 {changes.ValueKind}）";
                return null;
            }

            int index = 0;
            foreach (JsonElement item in changes.EnumerateArray())
            {
                index++;
                if (item.ValueKind != JsonValueKind.Object)
                {
                    error = $"changes[{index - 1}] 不是对象";
                    return null;
                }

                string field = ReadString(item, "field");
                if (string.IsNullOrWhiteSpace(field))
                {
                    error = $"changes[{index - 1}] 缺少 field";
                    return null;
                }

                if (!TryReadInt(item, "value", out long value))
                {
                    error = $"changes[{index - 1}]（{field}）的 value 不是整数";
                    return null;
                }

                string op = ReadString(item, "op");
                if (string.IsNullOrWhiteSpace(op))
                    op = "add";

                long charaNo = TryReadInt(item, "chara_no", out long parsedNo) ? parsedNo : -1;

                result.Changes.Add(new AiComputeChange
                {
                    Field = field.Trim(),
                    CharaNo = charaNo,
                    Op = op.Trim(),
                    Value = value,
                    Reason = ReadString(item, "reason"),
                });
            }

            return result;
        }
        catch (JsonException e)
        {
            error = $"副 API 输出不是合法 JSON：{e.Message}";
            return null;
        }
    }

    private static string ReadString(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out JsonElement value))
            return null;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => value.ToString(),
        };
    }

    /// <summary>
    /// 整数读取。模型经常把数字写成字符串（"3"）或带小数点（3.0），
    /// 这两种都接受；真正的小数（3.5）拒绝，因为 ERA 整数变量容不下它，四舍五入等于替模型做决定。
    /// </summary>
    private static bool TryReadInt(JsonElement parent, string name, out long value)
    {
        value = 0;
        if (!parent.TryGetProperty(name, out JsonElement element))
            return false;

        switch (element.ValueKind)
        {
            case JsonValueKind.Number:
                if (element.TryGetInt64(out value))
                    return true;
                if (element.TryGetDouble(out double d) && Math.Abs(d % 1) < double.Epsilon)
                {
                    value = (long)d;
                    return true;
                }
                return false;
            case JsonValueKind.String:
                string text = element.GetString();
                if (long.TryParse(text, out value))
                    return true;
                if (double.TryParse(text, out double parsed) && Math.Abs(parsed % 1) < double.Epsilon)
                {
                    value = (long)parsed;
                    return true;
                }
                return false;
            default:
                return false;
        }
    }
}