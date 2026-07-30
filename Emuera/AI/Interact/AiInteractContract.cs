using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MinorShift.Emuera.AI.Interact;

/// <summary>
/// 交互控制契约。写在 ai_traits.json 顶层的 "interact" 段，与 prompt / traits / compute 平级，
/// 改完点「重载词条库」生效，不需要编译。
///
/// 这一段回答的是「AI 除了说话，还能对游戏流程做什么」。设计沿用 compute 段的同一套思路：
///   1. 模型只输出**声明过的名字**（选项文本 + 命令名），绝不输出 ERA 的原始输入值。
///      真正喂给引擎的载荷由本地按 allowed_commands 查表得到——模型因此不可能触发
///      一个没被声明的命令，也不需要理解 ERA 的命令编号体系（幻觉高发区）。
///   2. 没写在 allowed_commands 里的东西一律触发不了。compute.writable_fields 之于变量，
///      等价于 interact.allowed_commands 之于命令。
///   3. 自由输入注入（不查表、直接把数字/文本喂进 INPUT）必须显式声明取值范围或字数上限，
///      没声明就一律拒绝。这条比命令白名单更危险，所以默认是关着的。
/// </summary>
internal sealed class AiInteractTemplate
{
    /// <summary>整段开关。false 时副 API 的输出里即使带了交互指令也一律忽略。</summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// 交互指令是否在本轮结束后自动执行。默认 false：只摆出来，等玩家点「执行」。
    ///
    /// 默认不自动执行是有意的。数值写错了还能撤销，流程一旦被推进（进了下一个场景、
    /// 花掉了一次行动机会）就不存在"撤销"这回事——ERA 没有流程级的回退。
    /// 所以这里的默认值取保守的一侧，愿意让 AI 自己开车的人再去打开它。
    /// </summary>
    [JsonPropertyName("auto_execute")]
    public bool AutoExecute { get; set; }

    /// <summary>单轮允许提出的选项条数上限。超出即整批丢弃选项（不影响正文与数值）。</summary>
    [JsonPropertyName("max_options")]
    public int MaxOptions { get; set; } = 4;

    /// <summary>选项文本的字数上限。太长的选项在面板上放不下，也说明模型在写正文而不是写选项。</summary>
    [JsonPropertyName("option_max_chars")]
    public int OptionMaxChars { get; set; } = 24;

    /// <summary>
    /// 是否允许自由输入注入（input_int / input_str）。默认 false。
    /// 打开之后还必须声明 input_int_range / input_str_max_chars，否则对应类型仍然被拒。
    /// </summary>
    [JsonPropertyName("allow_input_injection")]
    public bool AllowInputInjection { get; set; }

    /// <summary>
    /// 自由整数注入的允许区间，写成 [min, max]。不写则整数注入一律被拒。
    /// 这不是防幻觉的"幅度上限"，而是"这个游戏的输入框合法取值范围"，两回事。
    /// </summary>
    [JsonPropertyName("input_int_range")]
    public List<long> InputIntRange { get; set; } = [];

    /// <summary>自由字符串注入的字数上限。0 或不写则字符串注入一律被拒。</summary>
    [JsonPropertyName("input_str_max_chars")]
    public int InputStrMaxChars { get; set; }

    /// <summary>
    /// 允许 AI 触发的命令白名单。模型只看得到 command 名，看不到 value / input。
    /// </summary>
    [JsonPropertyName("allowed_commands")]
    public List<AiInteractCommand> AllowedCommands { get; set; } = [];

    public bool HasIntRange => InputIntRange != null && InputIntRange.Count == 2 && InputIntRange[0] <= InputIntRange[1];

    public long IntRangeMin => HasIntRange ? InputIntRange[0] : 0;

    public long IntRangeMax => HasIntRange ? InputIntRange[1] : 0;

    public AiInteractCommand FindCommand(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || AllowedCommands == null)
            return null;
        foreach (AiInteractCommand c in AllowedCommands)
        {
            if (c != null && string.Equals(c.Command, name, StringComparison.OrdinalIgnoreCase))
                return c;
        }
        return null;
    }
}

/// <summary>
/// 一条可触发的命令。这是「命令触发」这个交互原语的声明式定义。
///
/// ERA 侧的现实是：绝大多数"命令"就是在等待输入时提交一个数字（COM 编号），
/// 少数是提交一个字符串。所以这里只有两种载荷，二者填其一。
/// </summary>
internal sealed class AiInteractCommand
{
    /// <summary>模型看到的命令名，会作为 JSON schema 的 enum 值下发。用中文即可。</summary>
    [JsonPropertyName("command")]
    public string Command { get; set; }

    /// <summary>整数载荷（COM 编号等）。与 input 二者填其一。</summary>
    [JsonPropertyName("value")]
    public long? Value { get; set; }

    /// <summary>字符串载荷。与 value 二者填其一。</summary>
    [JsonPropertyName("input")]
    public string Input { get; set; }

    /// <summary>给模型看的说明，进 function schema。</summary>
    [JsonPropertyName("description")]
    public string Description { get; set; }

    /// <summary>给人看的备注，不进 prompt。</summary>
    [JsonPropertyName("note")]
    public string Note { get; set; }

    public bool IsIntPayload => Value.HasValue;

    /// <summary>真正喂给引擎的文本。整数载荷也要转成字符串，因为引擎入口只吃字符串。</summary>
    public string Payload => Value.HasValue
        ? Value.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
        : Input ?? "";
}

/// <summary>交互指令的种类。</summary>
internal enum AiActionKind
{
    /// <summary>不推进流程。绝大多数轮次都应该是这个。</summary>
    None = 0,

    /// <summary>触发白名单里的一条命令。</summary>
    Command = 1,

    /// <summary>自由整数注入。</summary>
    InputInt = 2,

    /// <summary>自由字符串注入。</summary>
    InputStr = 3,
}

/// <summary>副 API 提出的一条交互指令（解析后的原始意图，尚未校验）。</summary>
internal sealed class AiActionRequest
{
    public AiActionKind Kind = AiActionKind.None;

    /// <summary>Kind = Command 时的命令名。</summary>
    public string Command;

    /// <summary>Kind = InputInt 时的数值。</summary>
    public long Value;

    /// <summary>Kind = InputStr 时的文本。</summary>
    public string Text;

    /// <summary>模型自述的理由，仅用于日志与面板展示。</summary>
    public string Reason;
}

/// <summary>副 API 提出的一个玩家选项。纯 UI 建议，点了之后只是把文本填进输入框。</summary>
internal sealed class AiOption
{
    public string Label = "";

    /// <summary>可选的补充说明，显示为按钮的 tooltip。</summary>
    public string Hint;
}

/// <summary>
/// 已经通过契约层与引擎状态层校验、等待执行的交互指令。
///
/// 为什么要有这个中间对象而不是直接执行：校验发生在本轮收尾时（锁还没释放），
/// 而执行必须发生在锁释放之后（引擎入口在锁定期间一律拒绝输入，见 AiRequestLock 旁路 4）。
/// 中间这一步隔开了"判定可执行"和"真的执行"，也让「等玩家点一下」成为可能。
/// </summary>
internal sealed class AiPendingAction
{
    /// <summary>产生它的轮次，与 turn_id 一致。</summary>
    public string TurnId;

    /// <summary>产生它的票号。用于在日志里对上是哪一次请求。</summary>
    public long Ticket;

    public AiActionKind Kind;

    /// <summary>面板上给玩家看的一句话，例如「触发命令：抚摸」。</summary>
    public string Description = "";

    /// <summary>真正喂给引擎的文本。校验通过时就已经定下来，执行时不再重新推导。</summary>
    public string Payload = "";

    /// <summary>这条载荷是数值型还是字符串型。执行前要再核对一次引擎在等哪种输入。</summary>
    public bool IsIntPayload;

    public string Reason;

    /// <summary>已执行或已放弃。一次性，重复执行一律拒绝。</summary>
    public bool Consumed;
}

/// <summary>交互契约的内置常量与默认文案。</summary>
internal static class AiInteractDefaults
{
    /// <summary>
    /// 追加给副 API 的指令片段。只在 interact 段启用、且引擎确实在等输入时才下发——
    /// 引擎没在等输入的时候告诉模型"你可以推进流程"，只会诱导它编一个动作出来。
    /// </summary>
    public const string SystemPromptFragment = """
除了数值结算，本轮你还可以提出交互建议：

- options：给玩家 2-4 个下一步的短选项（每条不超过规定字数）。这只是建议，玩家点了才算。
- action：推进游戏流程的动作。只有在本轮事件明确指向某个动作时才给，否则填 kind="none"。
  action.command 只能取 command 枚举里列出的名字，不要臆造，也不要自己写命令编号。

铁律：不确定就填 none。选项可以多给，动作宁可不给——数值写错能撤销，流程被推进无法撤销。
""";
}