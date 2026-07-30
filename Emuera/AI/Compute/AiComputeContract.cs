using MinorShift.Emuera.AI.Traits;
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MinorShift.Emuera.AI.Compute;

/// <summary>
/// 副 API（计算通道）的声明式契约。写在 ai_traits.json 的顶层 "compute" 段里，改完点「重载词条库」生效，不需要编译。
///
/// 为什么和词条库同一个文件：P2 已经在 prompt.state_fields 里定义了「哪些数值算权威状态」，
/// 副 API 必须用同一份定义，否则主副两边对「当前状态」的理解会漂移（这是 RISK-02 的根因之一）。
/// 放在同一个文件里，人工改一处即可，不存在两份定义不同步的可能。
///
/// 关键设计：副 API 不输出 ERA 变量表达式，只输出这里声明的 field 名 + chara_no。
/// 变量表达式由本地按 writable_fields 的 target 模板拼出来。因此模型不可能写出未声明的变量，
/// 也不需要理解 ERA 的变量语法（这两件事都是幻觉的高发区）。
/// </summary>
internal sealed class AiComputeTemplate
{
    /// <summary>整段开关。false 时即使配置了副 API 端点也不会调用。</summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>副 API 的 system 指令。留空用 AiComputeDefaults.SystemPrompt。</summary>
    [JsonPropertyName("system_prompt")]
    public string SystemPrompt { get; set; }

    /// <summary>短记忆轮数（设计已定 3-5）。仅用于回溯，绝不用于推断当前值。</summary>
    [JsonPropertyName("memory_rounds")]
    public int MemoryRounds { get; set; } = 4;

    /// <summary>单轮允许的变更条数上限。超出即整批拒绝，用于挡住"一口气改二十项"的幻觉输出。</summary>
    [JsonPropertyName("max_changes")]
    public int MaxChanges { get; set; } = 8;

    /// <summary>true 时把所有已登录角色都写进状态快照；默认只写当前 TARGET，省 token。</summary>
    [JsonPropertyName("include_all_charas")]
    public bool IncludeAllCharas { get; set; }

    /// <summary>
    /// 最终值越界时的处置：clamp（钳到边界并记警告，默认）或 reject（整批拒绝）。
    /// 默认取 clamp 是因为「好感度 98 再 +5」这种越界属于正常游戏进程，整批拒绝会让玩家白等一轮；
    /// 真正的幻觉信号是 max_delta 超限，那一项一律整批拒绝。
    /// </summary>
    [JsonPropertyName("on_out_of_range")]
    public string OnOutOfRange { get; set; } = "clamp";

    /// <summary>
    /// 副 API 需要、但主 API prompt 不需要的状态项（例如所持金）。
    /// 结构与 prompt.state_fields 完全相同：expr 含 {CHARA} 即为角色维度，否则为全局项。
    /// </summary>
    [JsonPropertyName("extra_state_fields")]
    public List<AiStateField> ExtraStateFields { get; set; } = [];

    /// <summary>允许副 API 改动的字段声明。没写在这里的东西，副 API 一律改不到。</summary>
    [JsonPropertyName("writable_fields")]
    public List<AiComputeField> WritableFields { get; set; } = [];
}

/// <summary>
/// 一个可写字段的声明。这是数值回写的第二层白名单：
/// 第一层是 AiVariableAccess.WritableNames（变量名级，全局生效），
/// 第二层是这里（字段级，按游戏配置），两层都过了才会真正写入。
/// </summary>
internal sealed class AiComputeField
{
    /// <summary>副 API 使用的字段名，会作为 JSON schema 的 enum 值下发。用中文即可，例如「好感度」。</summary>
    [JsonPropertyName("field")]
    public string Field { get; set; }

    /// <summary>对应的 ERA 变量表达式。含 {CHARA} 时按角色维度处理，{CHARA} 会被换成登录号。</summary>
    [JsonPropertyName("target")]
    public string Target { get; set; }

    /// <summary>写入后允许的最小值。不写则不限。</summary>
    [JsonPropertyName("min")]
    public long Min { get; set; } = long.MinValue;

    /// <summary>写入后允许的最大值。不写则不限。</summary>
    [JsonPropertyName("max")]
    public long Max { get; set; } = long.MaxValue;

    /// <summary>单轮允许的最大变动幅度（绝对值）。0 表示不限。超出即整批拒绝——这是挡幻觉的主力。</summary>
    [JsonPropertyName("max_delta")]
    public long MaxDelta { get; set; }

    /// <summary>允许的操作符，留空等于 ["add","set"]。</summary>
    [JsonPropertyName("ops")]
    public List<string> Ops { get; set; } = [];

    /// <summary>给模型看的字段说明，会写进 JSON schema 的 description。</summary>
    [JsonPropertyName("description")]
    public string Description { get; set; }

    /// <summary>给人看的备注，不进 prompt。</summary>
    [JsonPropertyName("note")]
    public string Note { get; set; }

    public bool IsCharaScoped => Target != null
        && Target.Contains(AiTraitMatcher.CharaPlaceholder, StringComparison.OrdinalIgnoreCase);

    /// <summary>该字段实际允许的操作符集合。</summary>
    public IReadOnlyList<string> EffectiveOps => Ops != null && Ops.Count > 0 ? Ops : DefaultOps;

    private static readonly string[] DefaultOps = ["add", "set"];
}

/// <summary>副 API 输出的单条变更（解析后的原始意图，尚未校验）。</summary>
internal sealed class AiComputeChange
{
    /// <summary>字段名，必须能在 writable_fields 里找到。</summary>
    public string Field;

    /// <summary>角色号（非登录号）。全局字段忽略此值。</summary>
    public long CharaNo = -1;

    public string Op = "add";
    public long Value;

    /// <summary>模型自述的理由，仅用于日志与排查，不影响写入。</summary>
    public string Reason;
}

/// <summary>副 API 一次输出的解析结果。</summary>
internal sealed class AiComputeResult
{
    public string SchemaVersion;
    public string TurnId;
    public List<AiComputeChange> Changes = [];

    /// <summary>供主 API 参考的结果提示，纯文本，不参与写入。</summary>
    public string NarrativeHint;

    /// <summary>副 API 自报的不确定项。</summary>
    public List<string> Warnings = [];

    /// <summary>原始 JSON，写进日志便于复盘。</summary>
    public string RawJson;
}

/// <summary>
/// 玩家手动指定的一次数值调整。
///
/// 与 AiComputeChange 分成两个类型是有意的：两者的信任级别不同，走的校验路径也不同。
/// 副 API 的变更要过三层校验（含幅度与区间），玩家的调整只过字段白名单与引擎级校验。
/// 用同一个类型会让"这一条该按哪套规则校验"变成运行期才能知道的事。
/// </summary>
internal sealed class AiManualEdit
{
    /// <summary>要改的字段声明。只能是 compute.writable_fields 里的字段。</summary>
    public AiComputeField Field;

    /// <summary>角色号（非登录号）。全局字段填 -1。</summary>
    public long CharaNo = -1;

    /// <summary>玩家指定的最终值（不是增量）。</summary>
    public long Value;
}

/// <summary>手动调整界面用的一行：某个字段在某个角色身上的当前值。</summary>
internal sealed class AiEditableEntry
{
    public AiComputeField Field;
    public long CharaNo = -1;
    public string CharaName;
    public string Target;
    public long Current;

    /// <summary>界面上的显示名。全局字段不带角色名。</summary>
    public string DisplayName => CharaNo < 0 ? Field.Field : $"{CharaName}·{Field.Field}";
}

/// <summary>已经落盘的一条变更。保留写入前的值，使回滚成为可能（RISK-05）。</summary>
internal sealed class AiAppliedChange
{
    public string Field;
    public string Target;
    public string Op;
    public long RequestedValue;
    public long Before;
    public long After;
    public string Reason;

    public override string ToString()
        => $"{Field}（{Target}）{Before} → {After}";
}

/// <summary>副 API 的内置默认文案与常量。</summary>
internal static class AiComputeDefaults
{
    /// <summary>schema 版本。改动输出契约时同步升版本，解析层会拒绝不认识的版本。</summary>
    public const string SchemaVersion = "1.0";

    /// <summary>function calling 的函数名。</summary>
    public const string FunctionName = "apply_changes";

    public const string SystemPrompt = """
你是 ERA 游戏的数值结算引擎。你的唯一任务是把本轮事件换算成数值变更，并调用 apply_changes 函数返回。

铁律：
1. 当前数值以本轮 system 消息里的「权威状态」为唯一真值来源。历史记忆只用于理解剧情走向，绝不用来推算当前值。
2. 只能改动 apply_changes 的 field 枚举里列出的字段，不要臆造字段名。
3. turn_id 必须原样回填本轮给你的值。
4. 变更幅度要与事件强度相称。没有实质事件时返回空的 changes 数组，不要为了有输出而硬编数值。
5. 不要输出叙事文本。narrative_hint 只写一句结果提示，供叙事模型参考。
""";
}