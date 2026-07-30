namespace MinorShift.Emuera.AI.Traits;

/// <summary>
/// 内置默认词条库。仅在 exe 同目录与 csv 目录都找不到 ai_traits.json 时写出一份，
/// 之后所有修改都改磁盘上的 JSON，不需要动这里。
///
/// 这份默认库的作用是"可运行的示例"，覆盖了全部语法特性：
///   硬冲突（傲娇 × 坦率）、软冲突（傲娇 × 冷淡）、条件冲突（好感度高时冷淡失效）、
///   always 全局词条、状态类词条、prompt 骨架与数值状态字段，
///   P3 的 compute 段（副 API 可写字段、幅度上限、额外状态项），
///   以及 P4 的 interact 段（命令白名单、选项上限、自由注入开关）。
///
/// interact.allowed_commands 里的 value 是**示例值**，几乎肯定与你的游戏不符。
/// 填错不会报错——引擎会把它当成一个正常的输入提交上去，于是流程推进到别的地方。
/// 换游戏第一件事是跑「预览交互契约」核对这些编号。
/// </summary>
internal static class AiTraitDefaults
{
    public const string Json = """
        {
          "version": 1,
          "prompt": {
            "layout": "你是 ERA 游戏的叙事 AI，负责以第三人称描写场景、角色言行与心理。\n当前登场角色：{NAME}（呼称：{CALLNAME}，角色号 {CHARA_NO}）。\n\n{TRAITS}\n\n{SPEECH}\n\n{CONSTRAINTS}\n\n{STATE}\n\n写作要求：只描写这一轮发生的事；正文控制在 300 字以内；不要替玩家决定玩家的行动；不要输出任何数值或系统提示。",
            "trait_header": "【人物特征】",
            "speech_header": "【说话风格】",
            "constraint_header": "【行为约束】",
            "state_header": "【当前数值状态（权威值，不要自行推算）】",
            "max_chars": 1200,
            "global_rules": [
              "所有数值变化交由系统处理，正文里不要写出具体数字。",
              "角色不知道自己身处游戏中，不要出现元叙事。"
            ],
            "state_fields": [
              {
                "label": "好感度",
                "expr": "CFLAG:{CHARA}:好感度",
                "note": "若本游戏 csv\\CFLAG.CSV 里没有『好感度』这一项，改成实际名称或直接写数字下标 CFLAG:{CHARA}:12"
              },
              {
                "label": "体力",
                "expr": "BASE:{CHARA}:0",
                "note": "BASE:0 通常是体力，随游戏而异"
              },
              {
                "label": "气力",
                "expr": "BASE:{CHARA}:1"
              }
            ]
          },
          "compute": {
            "enabled": true,
            "memory_rounds": 4,
            "max_changes": 6,
            "include_all_charas": false,
            "on_out_of_range": "clamp",
            "extra_state_fields": [
              {
                "label": "所持金",
                "expr": "MONEY:0",
                "note": "副 API 要结算花销，所以要看得到钱；主 API 的 prompt 里没有这一项"
              }
            ],
            "writable_fields": [
              {
                "field": "好感度",
                "target": "CFLAG:{CHARA}:好感度",
                "min": 0,
                "max": 100,
                "max_delta": 10,
                "ops": [
                  "add"
                ],
                "description": "对主角的好感，0-100",
                "note": "max_delta 是挡幻觉的主力：一轮对话不可能让好感度动 10 点以上。换游戏时先核对 CFLAG.CSV 里『好感度』这个名字是否存在"
              },
              {
                "field": "信赖",
                "target": "CFLAG:{CHARA}:信頼",
                "min": 0,
                "max": 100,
                "max_delta": 8,
                "ops": [
                  "add"
                ],
                "description": "对主角的信赖，0-100",
                "note": "注意下标名是日文『信頼』，随游戏 CSV 而异"
              },
              {
                "field": "体力",
                "target": "BASE:{CHARA}:0",
                "min": 0,
                "max": 100,
                "max_delta": 40,
                "ops": [
                  "add",
                  "set"
                ],
                "description": "当前体力，0-100",
                "note": "允许 set 是因为休息事件会直接回满"
              },
              {
                "field": "所持金",
                "target": "MONEY:0",
                "min": 0,
                "max": 99999999,
                "max_delta": 100000,
                "ops": [
                  "add"
                ],
                "description": "主角所持金，用于结算花销与收入",
                "note": "全局字段，target 里不含 {CHARA}，副 API 提交时 chara_no 填 -1"
              }
            ]
          },
          "interact": {
            "enabled": true,
            "auto_execute": false,
            "max_options": 4,
            "option_max_chars": 24,
            "allow_input_injection": false,
            "input_int_range": [],
            "input_str_max_chars": 0,
            "allowed_commands": [
              {
                "command": "结束本回合",
                "value": 0,
                "description": "结束当前调教/交谈回合，回到指令选择",
                "note": "COM 编号随游戏而异。换游戏第一件事是核对这些 value 与实际的指令编号是否对得上——填错不会报错，只会推进到别的地方去"
              },
              {
                "command": "抚摸头部",
                "value": 1,
                "description": "轻抚对方的头，安抚性质的接触",
                "note": "示例值。value 是提交给引擎的 COM 编号，模型只看得到 command 名，看不到这个数字"
              },
              {
                "command": "交谈",
                "value": 2,
                "description": "与对方说话，推进对话",
                "note": "示例值，请按实际游戏的 COM 编号改"
              }
            ]
          },
          "context": {
            "context_window": 8192,
            "retain_rounds": 3,
            "trigger_ratio": 0.80,
            "target_ratio": 0.50,
            "enabled": true
          },
          "traits": [
            {
              "id": "tsundere",
              "name": "傲娇",
              "tags": [
                "性格",
                "恋爱"
              ],
              "priority": 55,
              "description": "为了掩饰害羞与腼腆而做出态度强硬高傲、言行表里不一的行为。平常说话带刺、拒绝承认自己的在意，但在特定条件下会露出黏人、依赖的一面。这种反差本身就是她表达好感的方式。",
              "speech_style": "对话以否认与反问为主，常用「谁、谁要……」「才不是为了你」这类句式；被戳中心事时提高音量或转移话题；独处或情绪缓和时语速放慢、句尾变软。",
              "constraints": [
                "不要让她直接说出「我喜欢你」这类坦白台词",
                "态度软化必须给出触发原因，不能无缘无故变温顺"
              ],
              "match": {
                "all": [
                  {
                    "expr": "CFLAG:{CHARA}:好感度",
                    "op": "between",
                    "value": 20,
                    "value2": 100,
                    "note": "好感度太低时她根本不在意你，谈不上掩饰。上界放到 100，『太亲近就不必掩饰』交给下面的 modifier 判定；若把上界写成 80，那条 modifier 永远不可能触发"
                  }
                ],
                "any": [
                  {
                    "expr": "TALENT:{CHARA}:傲慢",
                    "op": ">=",
                    "value": 1
                  },
                  {
                    "expr": "TALENT:{CHARA}:羞恥",
                    "op": ">=",
                    "value": 1
                  }
                ],
                "weight": 120
              },
              "conflicts": [
                {
                  "with": "honest",
                  "kind": "hard",
                  "note": "坦率与傲娇是同一维度的两端，不能共存"
                },
                {
                  "with": "cold",
                  "kind": "soft",
                  "suppress": [
                    "speech_style"
                  ],
                  "note": "冷淡可以共存，但说话风格以傲娇为准"
                }
              ],
              "modifiers": [
                {
                  "when": {
                    "expr": "CFLAG:{CHARA}:好感度",
                    "op": ">",
                    "value": 85
                  },
                  "effect": "suppress",
                  "note": "关系足够近之后不再需要掩饰"
                },
                {
                  "when": {
                    "expr": "MARK:{CHARA}:従順",
                    "op": ">=",
                    "value": 3
                  },
                  "effect": "add_constraint",
                  "text": "在两人独处时会主动靠近，但被指出来立刻否认"
                }
              ],
              "override_npcs": [],
              "enabled": true,
              "note": "示例词条。这是最典型的『可人工改写』节点：想让傲娇更凶就改 speech_style，想改变命中范围就改 match.all 的区间。"
            },
            {
              "id": "honest",
              "name": "坦率",
              "tags": [
                "性格",
                "恋爱"
              ],
              "priority": 55,
              "description": "想什么就说什么，喜欢与讨厌都摆在脸上。不擅长隐藏情绪，也不觉得有必要隐藏。",
              "speech_style": "句子短、结论先行，情绪直接写在台词里；被夸会老实高兴，被冒犯会当场说出来。",
              "constraints": [
                "不要让她说反话或口是心非"
              ],
              "match": {
                "all": [
                  {
                    "expr": "TALENT:{CHARA}:素直",
                    "op": ">=",
                    "value": 1
                  }
                ],
                "weight": 110
              },
              "conflicts": [
                {
                  "with": "tsundere",
                  "kind": "hard"
                }
              ],
              "modifiers": [],
              "override_npcs": [],
              "enabled": true
            },
            {
              "id": "cold",
              "name": "冷淡",
              "tags": [
                "性格",
                "态度"
              ],
              "priority": 40,
              "description": "对他人保持距离，表情与语气都缺少起伏。不主动开启话题，回应简短。",
              "speech_style": "多用短句与省略，很少提问；即使同意也不加语气词。",
              "constraints": [
                "不要主动发起亲密接触"
              ],
              "match": {
                "all": [
                  {
                    "expr": "CFLAG:{CHARA}:好感度",
                    "op": "<=",
                    "value": 25
                  }
                ],
                "weight": 100
              },
              "conflicts": [],
              "modifiers": [
                {
                  "when": {
                    "expr": "MARK:{CHARA}:従順",
                    "op": ">=",
                    "value": 3
                  },
                  "effect": "suppress",
                  "note": "条件冲突示例：已经足够顺从的角色不会再表现冷淡。注意条件必须落在本词条 match 命中的范围之内才可能触发——若写成『好感度>60』，因为 match 已限定好感度<=25，这条修改器永远是死配置"
                }
              ],
              "override_npcs": [],
              "enabled": true
            },
            {
              "id": "clingy",
              "name": "黏人",
              "tags": [
                "性格",
                "恋爱"
              ],
              "priority": 45,
              "description": "情感表达外放，喜欢待在对方身边，独处久了会不安。",
              "speech_style": "频繁呼唤对方称呼，喜欢确认「还会陪我吗」；句尾常带撒娇语气。",
              "constraints": [
                "不要让她因为一次冷落就彻底放弃"
              ],
              "match": {
                "all": [
                  {
                    "expr": "CFLAG:{CHARA}:好感度",
                    "op": ">=",
                    "value": 70
                  }
                ],
                "weight": 105
              },
              "conflicts": [
                {
                  "with": "cold",
                  "kind": "hard"
                }
              ],
              "modifiers": [],
              "override_npcs": [],
              "enabled": true
            },
            {
              "id": "exhausted",
              "name": "疲惫",
              "tags": [
                "状态",
                "身体"
              ],
              "priority": 30,
              "description": "体力已经见底，注意力涣散，任何动作都比平时慢半拍。",
              "speech_style": "话少、停顿多，容易答非所问。",
              "constraints": [
                "不要描写高强度动作",
                "允许中途走神或话说一半"
              ],
              "match": {
                "all": [
                  {
                    "expr": "BASE:{CHARA}:0",
                    "op": "<=",
                    "value": 20,
                    "note": "BASE:0 通常是体力"
                  }
                ],
                "weight": 90
              },
              "conflicts": [],
              "modifiers": [],
              "override_npcs": [],
              "enabled": true,
              "note": "状态类词条示例。状态类 priority 建议低于性格类，避免抢掉人格描写的位置。"
            },
            {
              "id": "baseline",
              "name": "基础基调",
              "tags": [
                "全局"
              ],
              "priority": 10,
              "description": "叙事保持克制、具体，优先写动作与可观察的细节，少写抽象心理概括。",
              "speech_style": "",
              "constraints": [
                "每轮至少给出一个可供玩家回应的着力点"
              ],
              "match": {
                "always": true,
                "weight": 10
              },
              "conflicts": [],
              "modifiers": [],
              "override_npcs": [],
              "enabled": true,
              "note": "always=true 的全局词条。priority 故意压到最低，只在还有名额时才占位。"
            }
          ]
        }
        """;
}
