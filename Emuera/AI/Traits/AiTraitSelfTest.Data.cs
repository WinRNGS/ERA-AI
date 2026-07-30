namespace MinorShift.Emuera.AI.Traits;

/// <summary>
/// P2 词条自检用的测试词条库。每组测试装载一份最小库，避免「单角色最多 5 条」的截断
/// 干扰断言，也让每条断言的前提一眼可见。
///
/// 这些库依赖 work\p2-harness 里的最小游戏数据：
///   角色号 1 = 爱丽丝（呼称 爱丽）：傲慢 1、羞恥 1、好感度 50、体力 100、気力 100、従順 1
///   角色号 2 = 贝拉：素直 1、好感度 10、体力 100、気力 100
/// </summary>
internal static partial class AiTraitSelfTest
{
    /// <summary>B 组：命中与落空。刻意让角色 A 与角色 B 命中不同词条。</summary>
    private const string MatchLibraryJson = """
        {
          "version": 1,
          "traits": [
            {
              "id": "tsundere",
              "name": "傲娇",
              "priority": 55,
              "description": "为掩饰害羞而态度强硬高傲、言行表里不一。",
              "speech_style": "以否认与反问为主。",
              "match": {
                "all": [
                  { "expr": "CFLAG:{CHARA}:好感度", "op": "between", "value": 20, "value2": 100 }
                ],
                "any": [
                  { "expr": "TALENT:{CHARA}:傲慢", "op": ">=", "value": 1 },
                  { "expr": "TALENT:{CHARA}:羞恥", "op": ">=", "value": 1 }
                ],
                "weight": 120
              }
            },
            {
              "id": "honest",
              "name": "坦率",
              "priority": 55,
              "description": "想什么就说什么。",
              "match": {
                "all": [ { "expr": "TALENT:{CHARA}:素直", "op": ">=", "value": 1 } ],
                "weight": 110
              }
            },
            {
              "id": "cold",
              "name": "冷淡",
              "priority": 40,
              "description": "对他人保持距离。",
              "match": {
                "all": [ { "expr": "CFLAG:{CHARA}:好感度", "op": "<=", "value": 25 } ],
                "weight": 100
              }
            },
            {
              "id": "clingy",
              "name": "黏人",
              "priority": 45,
              "description": "喜欢待在对方身边。",
              "match": {
                "all": [ { "expr": "CFLAG:{CHARA}:好感度", "op": ">=", "value": 70 } ],
                "weight": 105
              }
            },
            {
              "id": "exhausted",
              "name": "疲惫",
              "priority": 30,
              "description": "体力见底。",
              "match": {
                "all": [ { "expr": "BASE:{CHARA}:0", "op": "<=", "value": 20 } ],
                "weight": 90
              }
            },
            {
              "id": "needs_no_favor",
              "name": "毫无交集",
              "priority": 35,
              "description": "验证 none 语义：好感度只要不为 0 就不该命中。",
              "match": {
                "none": [ { "expr": "CFLAG:{CHARA}:好感度", "op": ">=", "value": 1 } ],
                "weight": 95
              }
            },
            {
              "id": "baseline",
              "name": "基础基调",
              "priority": 10,
              "description": "全局基调。",
              "match": { "always": true, "weight": 10 }
            }
          ]
        }
        """;

    /// <summary>C 组：命中数上限与排序。8 条 always，得分等于 priority。</summary>
    private const string CapLibraryJson = """
        {
          "version": 1,
          "traits": [
            { "id": "cap10", "name": "十", "priority": 10, "description": "十", "match": { "always": true, "weight": 0 } },
            { "id": "cap20", "name": "二十", "priority": 20, "description": "二十", "match": { "always": true, "weight": 0 } },
            { "id": "cap30", "name": "三十", "priority": 30, "description": "三十", "match": { "always": true, "weight": 0 } },
            { "id": "cap40", "name": "四十", "priority": 40, "description": "四十", "match": { "always": true, "weight": 0 } },
            { "id": "cap50", "name": "五十", "priority": 50, "description": "五十", "match": { "always": true, "weight": 0 } },
            { "id": "cap60", "name": "六十", "priority": 60, "description": "六十", "match": { "always": true, "weight": 0 } },
            { "id": "cap70", "name": "七十", "priority": 70, "description": "七十", "match": { "always": true, "weight": 0 } },
            { "id": "cap80", "name": "八十", "priority": 80, "description": "八十", "match": { "always": true, "weight": 0 } }
          ]
        }
        """;

    /// <summary>D 组：硬冲突与软冲突。全部 always，保证冲突一定同时命中。</summary>
    private const string ConflictLibraryJson = """
        {
          "version": 1,
          "traits": [
            {
              "id": "always_high",
              "name": "高优先",
              "priority": 90,
              "description": "硬冲突的赢家。",
              "speech_style": "赢家语气。",
              "match": { "always": true, "weight": 0 },
              "conflicts": [ { "with": "always_low", "kind": "hard", "note": "只有这一方声明，验证冲突无向" } ]
            },
            {
              "id": "always_low",
              "name": "低优先",
              "priority": 20,
              "description": "硬冲突的败者，应被整条丢弃。",
              "speech_style": "败者语气。",
              "match": { "always": true, "weight": 0 }
            },
            {
              "id": "soft_win",
              "name": "软冲突赢家",
              "priority": 70,
              "description": "软冲突的高优先方。",
              "speech_style": "以我的语气为准。",
              "constraints": [ "赢家的约束" ],
              "match": { "always": true, "weight": 0 },
              "conflicts": [ { "with": "soft_lose", "kind": "soft", "suppress": [ "speech_style" ] } ]
            },
            {
              "id": "soft_lose",
              "name": "软冲突败者",
              "priority": 30,
              "description": "软冲突的低优先方，描述应保留。",
              "speech_style": "这段语气应该被抹掉。",
              "constraints": [ "败者的约束应该保留" ],
              "match": { "always": true, "weight": 0 }
            }
          ]
        }
        """;

    /// <summary>E 组：四种修改器 effect。when 用「好感度 >= 0」表示必然成立。</summary>
    private const string ModifierLibraryJson = """
        {
          "version": 1,
          "traits": [
            {
              "id": "mod_suppress",
              "name": "会被抑制",
              "priority": 50,
              "description": "条件成立时整条应消失。",
              "match": { "always": true, "weight": 0 },
              "modifiers": [
                { "when": { "expr": "CFLAG:{CHARA}:好感度", "op": ">=", "value": 0 }, "effect": "suppress" }
              ]
            },
            {
              "id": "mod_keep",
              "name": "不会被抑制",
              "priority": 50,
              "description": "条件不成立时应保留。",
              "match": { "always": true, "weight": 0 },
              "modifiers": [
                { "when": { "expr": "CFLAG:{CHARA}:好感度", "op": ">=", "value": 99999 }, "effect": "suppress" }
              ]
            },
            {
              "id": "mod_weight",
              "name": "加权",
              "priority": 60,
              "description": "得分应为 50 + 60 + 500。",
              "match": { "always": true, "weight": 50 },
              "modifiers": [
                { "when": { "expr": "CFLAG:{CHARA}:好感度", "op": ">=", "value": 0 }, "effect": "weight", "value": 500 }
              ]
            },
            {
              "id": "mod_constraint",
              "name": "追加约束",
              "priority": 50,
              "description": "约束应从 1 条变 2 条，且库本体保持 1 条。",
              "constraints": [ "原本就有的约束" ],
              "match": { "always": true, "weight": 0 },
              "modifiers": [
                {
                  "when": { "expr": "CFLAG:{CHARA}:好感度", "op": ">=", "value": 0 },
                  "effect": "add_constraint",
                  "text": "由修改器追加的约束"
                }
              ]
            },
            {
              "id": "mod_desc",
              "name": "改写描述",
              "priority": 50,
              "description": "原始描述",
              "match": { "always": true, "weight": 0 },
              "modifiers": [
                {
                  "when": { "expr": "CFLAG:{CHARA}:好感度", "op": ">=", "value": 0 },
                  "effect": "description",
                  "text": "被修改器替换后的描述"
                }
              ]
            }
          ]
        }
        """;

    /// <summary>F 组：固定 NPC 覆盖。npc_only 的 match 永不成立，只能靠 force 命中。</summary>
    private const string NpcLibraryJson = """
        {
          "version": 1,
          "traits": [
            {
              "id": "npc_only",
              "name": "专属词条",
              "priority": 50,
              "description": "通用描述（不该出现在角色 1 身上）。",
              "speech_style": "通用语气（不该出现在角色 1 身上）。",
              "constraints": [ "通用约束（不该出现在角色 1 身上）" ],
              "match": {
                "all": [ { "expr": "CFLAG:{CHARA}:好感度", "op": ">=", "value": 99999 } ],
                "weight": 50
              },
              "override_npcs": [
                {
                  "chara_no": 1,
                  "description": "只给 1 号角色的定制描述",
                  "speech_style": "只给 1 号角色的定制语气",
                  "constraints": [ "只给 1 号角色的定制约束" ],
                  "weight_bonus": 300,
                  "force": true
                }
              ]
            },
            {
              "id": "npc_tuned",
              "name": "部分定制",
              "priority": 50,
              "description": "通用描述",
              "match": { "always": true, "weight": 0 },
              "override_npcs": [
                { "chara_no": 1, "description": "角色 1 专用描述" }
              ]
            }
          ]
        }
        """;

    /// <summary>G 组：prompt 装配。含一个故意写错的状态字段，验证跳过并留诊断。</summary>
    private const string PromptLibraryJson = """
        {
          "version": 1,
          "prompt": {
            "layout": "叙事 AI 测试骨架。\n当前登场角色：{NAME}（呼称：{CALLNAME}，角色号 {CHARA_NO}）。\n\n{TRAITS}\n\n{SPEECH}\n\n{CONSTRAINTS}\n\n{STATE}",
            "trait_header": "【人物特征】",
            "speech_header": "【说话风格】",
            "constraint_header": "【行为约束】",
            "state_header": "【当前数值状态】",
            "max_chars": 1200,
            "global_rules": [ "这是一条全局铁律。" ],
            "state_fields": [
              { "label": "好感度", "expr": "CFLAG:{CHARA}:好感度" },
              { "label": "体力", "expr": "BASE:{CHARA}:0" },
              { "label": "坏字段", "expr": "这不是变量", "note": "故意写错，验证被跳过且留下诊断" }
            ]
          },
          "traits": [
            {
              "id": "p_first",
              "name": "第一条",
              "priority": 60,
              "description": "第一条的描述。",
              "speech_style": "第一条的语气。",
              "constraints": [ "第一条的约束", "重复出现的约束" ],
              "match": { "always": true, "weight": 0 }
            },
            {
              "id": "p_second",
              "name": "第二条",
              "priority": 50,
              "description": "第二条的描述。",
              "speech_style": "第二条的语气。",
              "constraints": [ "重复出现的约束" ],
              "match": { "always": true, "weight": 0 }
            }
          ]
        }
        """;

    /// <summary>
    /// H 组：故意写坏的库。每一处错误都对应静态校验的一条诊断，
    /// 用来保证「人工改错了会看到反馈」而不是静默失效。
    /// </summary>
    private const string BrokenLibraryJson = """
        {
          "version": 1,
          "traits": [
            {
              "name": "没有 id 的词条",
              "description": "应被丢弃并给出诊断。",
              "match": { "always": true, "weight": 0 }
            },
            {
              "id": "dup",
              "name": "重复 id 的第一条",
              "description": "先出现的一条",
              "match": { "always": true, "weight": 0 }
            },
            {
              "id": "dup",
              "name": "重复 id 的第二条",
              "description": "后出现的一条",
              "match": { "always": true, "weight": 0 }
            },
            {
              "id": "kind_typo",
              "name": "冲突写错",
              "description": "冲突对象不存在且 kind 拼错。",
              "match": { "always": true, "weight": 0 },
              "conflicts": [ { "with": "幽灵词条", "kind": "weird" } ]
            },
            {
              "id": "mod_broken",
              "match": { "always": true, "weight": 0 },
              "modifiers": [ { "note": "既没有 when.expr 也没有 effect" } ],
              "override_npcs": [ { "description": "缺少 chara_no" } ]
            },
            {
              "id": "bad_expr",
              "name": "条件写错",
              "description": "变量名拼错，应整条丢弃并留诊断。",
              "match": {
                "all": [ { "expr": "CFLAG:{CHARA}:根本没有这个下标名", "op": ">=", "value": 1 } ],
                "weight": 0
              }
            },
            {
              "id": "good_expr",
              "name": "同库中写对的词条",
              "description": "不应被邻居的错误波及。",
              "match": { "always": true, "weight": 0 }
            }
          ]
        }
        """;
}