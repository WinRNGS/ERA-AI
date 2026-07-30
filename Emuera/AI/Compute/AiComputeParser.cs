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

            // P4：交互建议。刻意放在 changes 之后、且不影响返回值——
            // 交互是附加能力，写坏了顶多"这一轮没有选项/不推进流程"，
            // 没有理由因此把已经合规的 changes 一起丢掉。
            ParseInteract(root, result);

            return result;
        }
        catch (JsonException e)
        {
            error = $"副 API 输出不是合法 JSON：{e.Message}";
            return null;
        }
    }

    /// <summary>
    /// 解析 options 与 action。**任何失败都只是"这一部分不采用"，绝不让整批 changes 报废。**
    ///
    /// 这与 changes 的取向刻意相反。理由是后果不对等：changes 写进存档，一条离谱就说明模型
    /// 这一轮的理解不可靠，采纳其余项等于赌运气；而选项只是界面按钮、动作还要过引擎状态层与
    /// 玩家确认，最坏后果是"这一轮没推进流程"。为一条坏选项把已经算对的数值一起丢掉是亏的。
    ///
    /// 注意这里只做「JSON → 数据结构」，不做契约校验（命令是否在白名单、注入是否开放等）。
    /// 那些必须在界面线程用当时的词条库判定，见 AiActionExecutor.TryValidate。
    /// </summary>
    private static void ParseInteract(JsonElement root, AiComputeResult result)
    {
        var notes = new List<string>();

        if (root.TryGetProperty("options", out JsonElement options))
        {
            if (options.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in options.EnumerateArray())
                {
                    // 允许两种形态：{"label": "..."} 与裸字符串。后者是模型常见的省事写法，
                    // 语义毫无歧义，没必要因为形态不合就丢掉一条本来能用的选项。
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        string bare = item.GetString();
                        if (!string.IsNullOrWhiteSpace(bare))
                            result.Options.Add(new Interact.AiOption { Label = bare.Trim() });
                        continue;
                    }
                    if (item.ValueKind != JsonValueKind.Object)
                        continue;
                    string label = ReadString(item, "label");
                    if (string.IsNullOrWhiteSpace(label))
                        continue;
                    result.Options.Add(new Interact.AiOption
                    {
                        Label = label.Trim(),
                        Hint = ReadString(item, "hint"),
                    });
                }
            }
            else if (options.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
            {
                notes.Add($"options 不是数组（实际 {options.ValueKind}），本轮选项已忽略");
            }
        }

        if (root.TryGetProperty("action", out JsonElement action))
        {
            if (action.ValueKind == JsonValueKind.Object)
            {
                string kindText = ReadString(action, "kind");
                // kind 缺失时按 none 处理而不是报错：没提出动作与提出"不动作"在语义上相同，
                // 而"宁可不给"正是我们希望的默认行为。
                if (!string.IsNullOrWhiteSpace(kindText))
                {
                    string kind = kindText.Trim().ToLowerInvariant();
                    switch (kind)
                    {
                        case "none":
                            break;
                        case "command":
                            {
                                string command = ReadString(action, "command");
                                if (string.IsNullOrWhiteSpace(command))
                                {
                                    notes.Add("action.kind = command 但没给 command 名，动作已忽略");
                                    break;
                                }
                                result.Action = new Interact.AiActionRequest
                                {
                                    Kind = Interact.AiActionKind.Command,
                                    Command = command.Trim(),
                                    Reason = ReadString(action, "reason"),
                                };
                                break;
                            }
                        case "input_int":
                            {
                                if (!TryReadInt(action, "value", out long value))
                                {
                                    notes.Add("action.kind = input_int 但 value 不是整数，动作已忽略");
                                    break;
                                }
                                result.Action = new Interact.AiActionRequest
                                {
                                    Kind = Interact.AiActionKind.InputInt,
                                    Value = value,
                                    Reason = ReadString(action, "reason"),
                                };
                                break;
                            }
                        case "input_str":
                            {
                                string text = ReadString(action, "text");
                                if (string.IsNullOrWhiteSpace(text))
                                {
                                    notes.Add("action.kind = input_str 但 text 为空，动作已忽略");
                                    break;
                                }
                                result.Action = new Interact.AiActionRequest
                                {
                                    Kind = Interact.AiActionKind.InputStr,
                                    Text = text,
                                    Reason = ReadString(action, "reason"),
                                };
                                break;
                            }
                        default:
                            notes.Add($"action.kind 不认识（{kindText}），动作已忽略");
                            break;
                    }
                }
            }
            else if (action.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
            {
                notes.Add($"action 不是对象（实际 {action.ValueKind}），动作已忽略");
            }
        }

        if (notes.Count > 0)
            result.InteractNote = string.Join("；", notes);
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