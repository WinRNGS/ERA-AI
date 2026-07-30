using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MinorShift.Emuera.AI.Traits;

/// <summary>
/// 词条库文件的根节点。对应 ai_traits.json 的顶层对象。
///
/// 人工修改指南（重要）：
///   - 本文件只定义"字段有哪些"。真正的内容全部在 exe 同目录的 ai_traits.json 里，改 JSON 不需要重新编译。
///   - 字段名在 JSON 中使用蛇形小写（如 speech_style），读取时大小写不敏感、允许注释与尾逗号。
/// </summary>
internal sealed class AiTraitFile
{
    /// <summary>格式版本。当前为 1。升级格式时用于兼容旧文件。</summary>
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    /// <summary>词条列表。</summary>
    [JsonPropertyName("traits")]
    public List<AiTrait> Traits { get; set; } = [];

    /// <summary>prompt 骨架。留空时使用内置默认骨架。</summary>
    [JsonPropertyName("prompt")]
    public AiPromptTemplate Prompt { get; set; }

    /// <summary>
    /// 副 API（计算通道）契约。P3 新增，定义副 API 能改哪些字段、幅度上限与额外状态项。
    /// 留空时副 API 不会被调用，主 API 单通道照常工作。
    /// </summary>
    [JsonPropertyName("compute")]
    public Compute.AiComputeTemplate Compute { get; set; }
}

/// <summary>
/// prompt 骨架。决定"命中的词条被摆成什么样的 system prompt"。
///
/// 这是最常被人工调整的部分：改措辞、改段落顺序、加全局铁律，都在这里，改完重载即可，不用编译。
/// 支持的占位符：
///   {NAME}        角色名（NAME）
///   {CALLNAME}    呼称（CALLNAME）
///   {CHARA_NO}    角色号
///   {TRAITS}      词条段落（由 description 组装）
///   {SPEECH}      说话风格段落
///   {CONSTRAINTS} 行为约束段落
///   {STATE}       数值状态段落
/// </summary>
internal sealed class AiPromptTemplate
{
    /// <summary>整体骨架。为空时用内置默认值。</summary>
    [JsonPropertyName("layout")]
    public string Layout { get; set; }

    /// <summary>各段落的小标题。</summary>
    [JsonPropertyName("trait_header")]
    public string TraitHeader { get; set; } = "【人物特征】";
    [JsonPropertyName("speech_header")]
    public string SpeechHeader { get; set; } = "【说话风格】";
    [JsonPropertyName("constraint_header")]
    public string ConstraintHeader { get; set; } = "【行为约束】";
    [JsonPropertyName("state_header")]
    public string StateHeader { get; set; } = "【当前数值状态】";

    /// <summary>全局铁律。与词条无关，每轮都会附加，适合放"不要替玩家做决定"这类硬规则。</summary>
    [JsonPropertyName("global_rules")]
    public List<string> GlobalRules { get; set; } = [];

    /// <summary>要写进 {STATE} 的数值项。expr 支持 {CHARA} 占位符。</summary>
    [JsonPropertyName("state_fields")]
    public List<AiStateField> StateFields { get; set; } = [];

    /// <summary>system prompt 的字符上限。超出时按段落尾部截断，默认 1200（设计目标 500-1000 字）。</summary>
    [JsonPropertyName("max_chars")]
    public int MaxChars { get; set; } = 1200;
}

/// <summary>数值状态里的一项，形如「好感度: 42」。</summary>
internal sealed class AiStateField
{
    [JsonPropertyName("label")]
    public string Label { get; set; }
    [JsonPropertyName("expr")]
    public string Expr { get; set; }
    [JsonPropertyName("note")]
    public string Note { get; set; }
}

/// <summary>
/// 单条词条。一个词条 = 一段可复用的人物特征描述 + 它的命中条件 + 它与别的词条怎么共存。
///
/// 举例（傲娇）：
///   id            = "tsundere"
///   name          = "傲娇"
///   description   = "为掩饰害羞而表现出强硬高傲、言行表里不一……"
///   speech_style  = "平时说话带刺、否认自己的在意……"
///   match         = 好感度处于中段且'羞耻'素质不为 0 时命中
///   conflicts     = 与"坦率"硬冲突，与"冷淡"软冲突
///   modifiers     = 好感度 > 80 时抑制本词条（已经不需要掩饰了）
/// </summary>
internal sealed class AiTrait
{
    /// <summary>唯一标识，英文小写下划线。改 id 会让引用它的 conflicts 失效，改名请同步。</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; }

    /// <summary>展示名，会直接出现在 prompt 里。中文即可。</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; }

    /// <summary>分类标签，用于人工检索与按类抽取，如 ["性格", "恋爱"]。不参与命中判定。</summary>
    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = [];

    /// <summary>优先级。数字越大越优先，冲突时决定谁被抑制。建议：核心人格 80、常规性格 50、状态 30。</summary>
    [JsonPropertyName("priority")]
    public int Priority { get; set; } = 50;

    /// <summary>词条描述文本。进入 prompt 的主体内容。</summary>
    [JsonPropertyName("description")]
    public string Description { get; set; }

    /// <summary>说话风格指令。软冲突时可能被抑制。</summary>
    [JsonPropertyName("speech_style")]
    public string SpeechStyle { get; set; }

    /// <summary>行为约束列表。软冲突时可能被抑制。</summary>
    [JsonPropertyName("constraints")]
    public List<string> Constraints { get; set; } = [];

    /// <summary>命中条件。为 null 或空表示"永不自动命中"（只能被 override_npcs 强制挂上）。</summary>
    [JsonPropertyName("match")]
    public AiTraitMatchRule Match { get; set; }

    /// <summary>与其他词条的冲突规则。</summary>
    [JsonPropertyName("conflicts")]
    public List<AiTraitConflict> Conflicts { get; set; } = [];

    /// <summary>条件修改器。基于数值区间在运行期改写权重或抑制自身。</summary>
    [JsonPropertyName("modifiers")]
    public List<AiTraitModifier> Modifiers { get; set; } = [];

    /// <summary>固定 NPC 定制。命中的角色号会用这里的文本覆盖通用文本。</summary>
    [JsonPropertyName("override_npcs")]
    public List<AiTraitNpcOverride> OverrideNpcs { get; set; } = [];

    /// <summary>置 false 可临时停用而不删除。</summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>给人看的备注，不进入 prompt。</summary>
    [JsonPropertyName("note")]
    public string Note { get; set; }
}

/// <summary>
/// 命中规则。三个列表的语义：all 全部满足、any 至少一条满足、none 全部不满足。
/// 三个都为空视为不可自动命中。
/// </summary>
internal sealed class AiTraitMatchRule
{
    /// <summary>true 表示无条件命中（用于全局基调词条）。为 true 时忽略 all / any / none。</summary>
    [JsonPropertyName("always")]
    public bool Always { get; set; }

    [JsonPropertyName("all")]
    public List<AiTraitCondition> All { get; set; } = [];
    [JsonPropertyName("any")]
    public List<AiTraitCondition> Any { get; set; } = [];
    [JsonPropertyName("none")]
    public List<AiTraitCondition> None { get; set; } = [];

    /// <summary>命中后的基础分。与 priority 一起决定抽取顺序，默认 100。</summary>
    [JsonPropertyName("weight")]
    public long Weight { get; set; } = 100;
}

/// <summary>
/// 单个条件。本质是"取一个 ERA 变量的值，和一个常量比较"。
///
/// expr 直接写 ERA 变量表达式，支持命名下标与角色维度，例如：
///   "CFLAG:{CHARA}:好感度"   —— {CHARA} 会被替换成本轮角色的登录号
///   "TALENT:{CHARA}:素直"
///   "FLAG:120"
/// op 可用：>= <= > < == != between（配合 value / value2）
///          eq ne contains notcontains（字符串比较，配合 text）
/// </summary>
internal sealed class AiTraitCondition
{
    [JsonPropertyName("expr")]
    public string Expr { get; set; }
    [JsonPropertyName("op")]
    public string Op { get; set; } = ">=";

    /// <summary>整数比较的右值。</summary>
    [JsonPropertyName("value")]
    public long Value { get; set; }

    /// <summary>between 的上界（含）。</summary>
    [JsonPropertyName("value2")]
    public long Value2 { get; set; }

    /// <summary>字符串比较的右值。非空时按字符串比较，忽略 value。</summary>
    [JsonPropertyName("text")]
    public string Text { get; set; }

    [JsonPropertyName("note")]
    public string Note { get; set; }
}

/// <summary>
/// 冲突规则。
///   kind = "hard"：两者禁止共存，低优先级方整条被丢弃。
///   kind = "soft"：两者共存，但低优先级方的 suppress 字段被抹掉（默认抹 speech_style 与 constraints）。
/// </summary>
internal sealed class AiTraitConflict
{
    /// <summary>冲突对象的词条 id。</summary>
    [JsonPropertyName("with")]
    public string With { get; set; }

    /// <summary>hard 或 soft。写错时按 hard 处理并给出诊断。</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "hard";

    /// <summary>软冲突时要抑制的字段，可选值：description / speech_style / constraints。</summary>
    [JsonPropertyName("suppress")]
    public List<string> Suppress { get; set; } = [];

    [JsonPropertyName("note")]
    public string Note { get; set; }
}

/// <summary>
/// 条件修改器。when 满足时执行 effect。
///   effect = "suppress"      整条失效（例：好感度 > 80 时"冷淡"失效）
///   effect = "weight"        权重加上 value（可为负）
///   effect = "description"   用 text 替换描述
///   effect = "speech_style"  用 text 替换说话风格
///   effect = "add_constraint" 追加一条 text 到约束列表
/// </summary>
internal sealed class AiTraitModifier
{
    [JsonPropertyName("when")]
    public AiTraitCondition When { get; set; }
    [JsonPropertyName("effect")]
    public string Effect { get; set; }
    [JsonPropertyName("value")]
    public long Value { get; set; }
    [JsonPropertyName("text")]
    public string Text { get; set; }
    [JsonPropertyName("note")]
    public string Note { get; set; }
}

/// <summary>
/// 固定 NPC 定制。用角色号（CSV 的角色番号 NO）定位，不用登录号——登录号会随增删角色漂移。
/// </summary>
internal sealed class AiTraitNpcOverride
{
    /// <summary>角色号。对应 chara*.csv 的番号，等价于脚本里的 NO。</summary>
    [JsonPropertyName("chara_no")]
    public long CharaNo { get; set; } = -1;

    /// <summary>非空则覆盖 description。</summary>
    [JsonPropertyName("description")]
    public string Description { get; set; }

    /// <summary>非空则覆盖 speech_style。</summary>
    [JsonPropertyName("speech_style")]
    public string SpeechStyle { get; set; }

    /// <summary>非空则覆盖 constraints。</summary>
    [JsonPropertyName("constraints")]
    public List<string> Constraints { get; set; }

    /// <summary>权重加成。让该 NPC 的这条词条更容易被选中。</summary>
    [JsonPropertyName("weight_bonus")]
    public long WeightBonus { get; set; }

    /// <summary>true 时无视 match 条件，对该 NPC 强制命中。</summary>
    [JsonPropertyName("force")]
    public bool Force { get; set; }

    [JsonPropertyName("note")]
    public string Note { get; set; }
}

/// <summary>
/// 一条词条在"本轮、这个角色身上"的实例化结果。
/// 修改器与冲突抑制都作用在这个对象上，不会污染词条库本体。
/// </summary>
internal sealed class AiTraitInstance
{
    public AiTrait Trait { get; set; }
    public long Score { get; set; }
    public string Description { get; set; }
    public string SpeechStyle { get; set; }
    public List<string> Constraints { get; set; } = [];

    public string Id => Trait?.Id;
    public string Name => Trait?.Name;
    public int Priority => Trait?.Priority ?? 0;
}
