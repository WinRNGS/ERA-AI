using MinorShift.Emuera.GameView;
using MinorShift.Emuera.Runtime;
using System;
using System.Collections.Generic;

namespace MinorShift.Emuera.AI.Interact;

/// <summary>
/// 交互指令的校验与执行。必须在界面线程调用（要读引擎状态、要驱动输入入口）。
///
/// 三层校验，与 P3 数值回写同构（compute.writable_fields 之于变量，等价于
/// interact.allowed_commands 之于命令），任一层不过就**只丢弃这条指令**：
///
///   1. 契约层：kind 认识、命令在白名单里、自由注入已显式开放且在声明范围内。
///   2. 引擎状态层：引擎此刻确实在等输入，且在等的类型与载荷类型对得上。
///      这一层不可省。ERA 对"不该来的输入"是静默失败——数字喂进 INPUTS 会变成一个字符串，
///      字符串喂进 INPUT 会被直接丢掉，两者都不报错，表现为"AI 说要做什么但什么都没发生"。
///   3. 执行层：锁必须已经释放，指令不能被执行过第二次，且执行前重新核对一次引擎状态。
///      从"本轮收尾时校验"到"玩家点执行"之间可能过了很久，引擎早就换了状态。
///
/// 为什么丢弃单条而不是整批拒绝：交互指令不写存档。数值写错会污染存档，
/// 所以那边一颗老鼠屎坏一锅汤；这里丢掉一条动作的代价只是"这一轮没推进流程"，
/// 而连带把数值一起拒掉反而是更大的损失。
/// </summary>
internal static class AiActionExecutor
{
    /// <summary>
    /// 契约层校验。通过后产出一条待执行指令；kind = none 时返回 true 但 pending 为 null。
    /// 不接触引擎状态，因此可以在装配预览里直接调用。
    /// </summary>
    public static bool TryValidate(
        AiInteractTemplate template,
        AiActionRequest request,
        string turnId,
        long ticket,
        out AiPendingAction pending,
        out string error)
    {
        pending = null;
        error = null;

        if (request == null || request.Kind == AiActionKind.None)
            return true;
        if (template == null || !template.Enabled)
        {
            error = "词条库的 interact 段未启用，交互指令已忽略";
            return false;
        }

        switch (request.Kind)
        {
            case AiActionKind.Command:
                {
                    AiInteractCommand command = template.FindCommand(request.Command);
                    if (command == null)
                    {
                        error = $"副 API 提出了未声明的命令「{request.Command}」，已忽略";
                        return false;
                    }
                    if (!command.IsIntPayload && string.IsNullOrEmpty(command.Input))
                    {
                        error = $"命令「{command.Command}」在词条库里既没有 value 也没有 input，无法执行";
                        return false;
                    }
                    pending = new AiPendingAction
                    {
                        TurnId = turnId,
                        Ticket = ticket,
                        Kind = AiActionKind.Command,
                        Description = $"触发命令：{command.Command}",
                        Payload = command.Payload,
                        IsIntPayload = command.IsIntPayload,
                        Reason = request.Reason,
                    };
                    return true;
                }

            case AiActionKind.InputInt:
                {
                    if (!template.AllowInputInjection)
                    {
                        error = "自由输入注入未开放（interact.allow_input_injection = false），已忽略";
                        return false;
                    }
                    if (!template.HasIntRange)
                    {
                        error = "interact.input_int_range 未声明合法区间，整数注入一律拒绝";
                        return false;
                    }
                    if (request.Value < template.IntRangeMin || request.Value > template.IntRangeMax)
                    {
                        error = $"注入值 {request.Value} 超出声明区间 [{template.IntRangeMin}, {template.IntRangeMax}]，已忽略";
                        return false;
                    }
                    pending = new AiPendingAction
                    {
                        TurnId = turnId,
                        Ticket = ticket,
                        Kind = AiActionKind.InputInt,
                        Description = $"输入数值：{request.Value}",
                        Payload = request.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        IsIntPayload = true,
                        Reason = request.Reason,
                    };
                    return true;
                }

            case AiActionKind.InputStr:
                {
                    if (!template.AllowInputInjection)
                    {
                        error = "自由输入注入未开放（interact.allow_input_injection = false），已忽略";
                        return false;
                    }
                    if (template.InputStrMaxChars <= 0)
                    {
                        error = "interact.input_str_max_chars 未声明，字符串注入一律拒绝";
                        return false;
                    }
                    string text = request.Text ?? "";
                    if (text.Length == 0)
                    {
                        error = "字符串注入的内容为空，已忽略";
                        return false;
                    }
                    if (text.Length > template.InputStrMaxChars)
                    {
                        error = $"注入文本 {text.Length} 字，超过上限 {template.InputStrMaxChars}，已忽略";
                        return false;
                    }
                    // 换行会被引擎当成多段输入依次喂进去（PressEnterKey 按 \n 拆分），
                    // 那等于一条指令偷偷推进了好几步流程。压成空格，绝不放行。
                    if (text.Contains('\n') || text.Contains('\r'))
                        text = text.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ');
                    pending = new AiPendingAction
                    {
                        TurnId = turnId,
                        Ticket = ticket,
                        Kind = AiActionKind.InputStr,
                        Description = $"输入文本：{text}",
                        Payload = text,
                        IsIntPayload = false,
                        Reason = request.Reason,
                    };
                    return true;
                }

            default:
                error = $"无法识别的交互指令类型 {request.Kind}，已忽略";
                return false;
        }
    }

    /// <summary>
    /// 引擎状态层校验。回答的是「现在把这条载荷喂进去，引擎会正确接住吗」。
    /// 必须在界面线程调用。
    /// </summary>
    public static bool IsEngineReady(EmueraConsole console, AiPendingAction action, out string error)
    {
        error = null;
        if (action == null)
        {
            error = "没有待执行的交互指令";
            return false;
        }
        if (action.Consumed)
        {
            error = "这条交互指令已经处置过了";
            return false;
        }
        if (console == null || !console.Enabled)
        {
            error = "引擎尚未就绪";
            return false;
        }
        if (!console.IsWaitInputState)
        {
            error = "引擎当前不在等待输入（脚本正在运行或已结束），无法注入";
            return false;
        }

        InputType type = console.NowInputType;
        if (action.IsIntPayload)
        {
            if (type is not (InputType.IntValue or InputType.IntButton or InputType.AnyValue))
            {
                error = $"引擎在等 {type} 类型的输入，数值载荷喂进去会被静默丢弃";
                return false;
            }
        }
        else
        {
            if (type is not (InputType.StrValue or InputType.StrButton or InputType.AnyValue))
            {
                error = $"引擎在等 {type} 类型的输入，文本载荷喂进去会被静默丢弃";
                return false;
            }
        }

        // ONEINPUT 系只吃一个字符，多余的会被 PressEnterKey 截掉。
        // 截断后的值往往仍然合法（"12" 变成 "1"），所以这是最容易被忽略的一类错。
        if (console.IsWaintingOnePhrase && action.Payload != null && action.Payload.Length > 1)
        {
            error = $"引擎在等单字符输入，载荷「{action.Payload}」会被截断，拒绝执行";
            return false;
        }

        return true;
    }

    /// <summary>
    /// 执行一条待执行指令。必须在界面线程调用，且必须在锁已释放之后。
    ///
    /// 走 PressEnterKey 而不是自己拼状态机：那是引擎唯一完整的输入入口，
    /// 宏展开、计时器停止、按钮世代更新、消息跳过全在里面。绕过它就得把这些全抄一遍。
    /// </summary>
    public static bool TryExecute(EmueraConsole console, AiPendingAction action, out string error)
    {
        error = null;
        if (AiRequestLock.IsLocked)
        {
            // 锁定期间引擎的全部输入入口都会拒绝（AiRequestLock 旁路 4），
            // 这里若放行，PressEnterKey 会静默返回，表现为"点了执行但什么都没发生"。
            error = "AI 请求进行中，等这一轮结束再执行";
            return false;
        }
        if (!IsEngineReady(console, action, out error))
            return false;

        action.Consumed = true;
        try
        {
            // changedByMouse = true：按字面整段喂入，不解析输入宏。
            // AI 产出的文本里出现半角括号是常事，让它走 parseInput 等于允许模型间接调用宏。
            console.PressEnterKey(false, action.Payload ?? "", true);
            return true;
        }
        catch (Exception e)
        {
            error = $"执行交互指令失败：{e.Message}";
            return false;
        }
    }

    /// <summary>
    /// 选项清洗。超长的截断、空的丢掉、重复的去重、超量的截掉尾部。
    ///
    /// 这里刻意不做"整批拒绝"：选项只是界面上的按钮，点了才把文本填进输入框，
    /// 既不写存档也不推进流程。为了一条超长选项把其余三条一起丢掉毫无收益。
    /// </summary>
    public static List<AiOption> Sanitize(AiInteractTemplate template, List<AiOption> options, out string note)
    {
        note = null;
        var result = new List<AiOption>();
        if (options == null || options.Count == 0)
            return result;
        if (template == null || !template.Enabled)
        {
            note = "interact 段未启用，本轮选项已忽略";
            return result;
        }

        int maxOptions = template.MaxOptions > 0 ? template.MaxOptions : 4;
        int maxChars = template.OptionMaxChars > 0 ? template.OptionMaxChars : 24;
        int dropped = 0;
        int truncated = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (AiOption option in options)
        {
            string label = option?.Label?.Replace("\r", " ").Replace("\n", " ").Trim();
            if (string.IsNullOrEmpty(label))
            {
                dropped++;
                continue;
            }
            if (label.Length > maxChars)
            {
                label = label[..maxChars];
                truncated++;
            }
            if (!seen.Add(label))
            {
                dropped++;
                continue;
            }
            if (result.Count >= maxOptions)
            {
                dropped++;
                continue;
            }
            result.Add(new AiOption { Label = label, Hint = option.Hint });
        }

        if (dropped > 0 || truncated > 0)
            note = $"选项清洗：丢弃 {dropped} 条，截断 {truncated} 条（上限 {maxOptions} 条 / {maxChars} 字）";
        return result;
    }
}