# ERA-AI 词条与 Prompt 维护指南

**这份文档的用途**：让人和 AI 都能安全地修改角色人格、说话风格与 prompt，而**不需要重新编译程序**。

**一句话上手**：所有可调内容都在 exe 同目录的 `ai_traits.json` 里；改完在游戏菜单点 `AI → 重载词条库` 生效；不确定改对没有，点 `AI → 预览当前角色 prompt` 看实际发给模型的文本。

> 文中引用的 `outputs\P*-交付说明.md` 是各阶段的技术交接文档，**只存在于开发者本地工作目录，不随仓库分发**。日常维护词条与 prompt 不需要它们；只有在要改 C# 代码时才需要，届时请向项目维护者索取。

---

## 零、给 AI 读者的执行须知

如果你是接手这个项目的 AI，改词条前请按顺序做这几件事：

1. **先读盘再改**。词条库的真身是 `ai_traits.json`，不是 `AiTraitDefaults.cs`。改 `.cs` 里的默认库只影响「首次运行时写出的那一份」，对已经存在 JSON 的环境毫无效果。
2. **改完必须验证**。运行 P2 自检（命令见第八节），或者至少让用户点一次「预览当前角色 prompt」。词条写错**几乎不会报错**，只会安静地不生效——这是这套系统最大的坑。
3. **不要为了改文案去动 C# 代码**。文案、语气、约束、prompt 骨架、数值状态字段全部在 JSON 里。需要动 C# 的只有第七节列出的那几种情况。
4. **改动要能被回滚**。JSON 里给每条词条留了 `note` 字段，写清「为什么这么设」。`enabled: false` 可以临时停用而不删除。

---

## 一、一条词条长什么样

以「傲娇」为例。这就是用户提到的那种典型场景——一个人格标签背后需要有描述、有语气、有触发条件、有与其它人格的关系：

```json
{
  "id": "tsundere",
  "name": "傲娇",
  "tags": ["性格", "恋爱"],
  "priority": 55,
  "description": "为了掩饰害羞与腼腆而做出态度强硬高傲、言行表里不一的行为。平常说话带刺、拒绝承认自己的在意，但在特定条件下会露出黏人、依赖的一面。这种反差本身就是她表达好感的方式。",
  "speech_style": "对话以否认与反问为主，常用「谁、谁要……」「才不是为了你」这类句式；被戳中心事时提高音量或转移话题；独处或情绪缓和时语速放慢、句尾变软。",
  "constraints": [
    "不要让她直接说出「我喜欢你」这类坦白台词",
    "态度软化必须给出触发原因，不能无缘无故变温顺"
  ],
  "match": {
    "all": [
      { "expr": "CFLAG:{CHARA}:好感度", "op": "between", "value": 20, "value2": 100 }
    ],
    "any": [
      { "expr": "TALENT:{CHARA}:傲慢", "op": ">=", "value": 1 },
      { "expr": "TALENT:{CHARA}:羞恥", "op": ">=", "value": 1 }
    ],
    "weight": 120
  },
  "conflicts": [
    { "with": "honest", "kind": "hard" },
    { "with": "cold", "kind": "soft", "suppress": ["speech_style"] }
  ],
  "modifiers": [
    {
      "when": { "expr": "CFLAG:{CHARA}:好感度", "op": ">", "value": 85 },
      "effect": "suppress",
      "note": "关系够近之后不再需要掩饰"
    }
  ],
  "override_npcs": [],
  "enabled": true,
  "note": "想让她更凶就改 speech_style；想改变命中范围就改 match.all 的区间"
}
```

### 各字段的修改指向

| 想改什么 | 改哪个字段 |
| --- | --- |
| 这个人格「是什么」的定义 | `description` |
| 说话的腔调、句式、习惯 | `speech_style` |
| 绝对不能做的事 | `constraints` |
| 什么样的角色会有这个人格 | `match` |
| 和别的人格能不能共存 | `conflicts` |
| 什么情况下这个人格暂时失效或加强 | `modifiers` |
| 只想给某个特定 NPC 定制 | `override_npcs` |
| 多个人格抢名额时谁优先 | `priority` 与 `match.weight` |
| 临时停用不删除 | `enabled: false` |

`tags` 只是给人检索用的分类标签，**不参与命中判定**。想改命中范围，改 `match`，改 tag 没有任何效果。

---

## 二、`match`：什么角色会命中这个词条

命中判定读的是 ERA 的真实变量值，不是猜的。

```json
"match": {
  "all":  [ ... ],   // 全部满足
  "any":  [ ... ],   // 至少一条满足
  "none": [ ... ],   // 全部不满足
  "always": false,   // true = 无条件命中，忽略上面三个
  "weight": 100      // 基础分，与 priority 一起决定抽取顺序
}
```

三个列表可以同时写，语义是 `all AND any AND none` 全部成立才命中。三个都为空且 `always` 不为 true 的词条**永不自动命中**（只能靠 `override_npcs.force` 挂上）。

### 单个条件怎么写

```json
{ "expr": "CFLAG:{CHARA}:好感度", "op": ">=", "value": 60 }
```

- `expr` 直接写 ERA 变量表达式。`{CHARA}` 会被替换成本轮角色的登录号。
  - 常用形式：`CFLAG:{CHARA}:好感度`、`TALENT:{CHARA}:素直`、`ABL:{CHARA}:奉仕`、`MARK:{CHARA}:従順`、`BASE:{CHARA}:0`、`FLAG:120`
  - 命名下标（`好感度` 这种）来自游戏 `csv\` 目录下对应的 CSV：`CFLAG` 看 `CFLAG.CSV`、`TALENT` 看 `TALENT.CSV`、`MARK` 看 `MARK.CSV`、`ABL` 看 `ABL.CSV`、`BASE` 看 `BASE.CSV`。**换游戏必须核对这些名字。**
  - 不确定名字对不对时可以直接写数字下标，例如 `CFLAG:{CHARA}:12`。
- 整数比较 `op`：`>=`（默认）、`<=`、`>`、`<`、`==`、`!=`、`between`（配合 `value` 与 `value2`，两端都含）
- 字符串比较：把值写在 `text` 而不是 `value` 里，`op` 用 `eq` / `ne` / `contains` / `notcontains`。适用于 `CSTR` / `NAME` 这类字符串变量。
- `note` 随便写，不进 prompt，只给人看。

### 三个必须记住的坑

**坑 1：变量名写错不会报错，只会让整条词条静默失效。**
引擎对拼错的变量名和越界下标都是静默失败。本系统在这一点上做了补救：条件求值失败时会**丢弃该词条**并在「显示词条诊断」里留一条 `条件表达式无法解析`。所以改完 `expr` 一定要看一次诊断。

**坑 2：`modifier` 的 `when` 必须落在 `match` 命中范围之内，否则是永不触发的死配置。**
这是最容易犯且完全没有报错的错误。反例：

```json
"match":     { "all": [ { "expr": "CFLAG:{CHARA}:好感度", "op": "<=", "value": 25 } ] },
"modifiers": [ { "when": { "expr": "CFLAG:{CHARA}:好感度", "op": ">", "value": 60 }, "effect": "suppress" } ]
```

`match` 已经限定好感度 ≤ 25 才命中，那么「好感度 > 60」这个 modifier 永远不可能触发。写 modifier 前先问一句：**这个条件和 match 有交集吗？**

**坑 3：`CFLAG:0` 在很多 ERA 游戏里被引擎当作 SP 角色标志位。**
不要把自定义含义放在 `CFLAG:0`。

---

## 三、`conflicts`：人格之间怎么共存

冲突判定全部在本地完成，**不消耗任何 token**。

| 类型 | 写法 | 效果 |
| --- | --- | --- |
| 硬冲突 | `{ "with": "honest", "kind": "hard" }` | 两者禁止共存。`priority` 低的一方**整条被丢弃** |
| 软冲突 | `{ "with": "cold", "kind": "soft", "suppress": ["speech_style"] }` | 两者共存，但低优先级方的指定字段被抹掉 |
| 条件冲突 | 不写在 `conflicts` 里，用 `modifiers` 的 `suppress` 实现 | 见下一节 |

规则细节：

- **冲突是无向的**：只要任一方声明了冲突就生效，不要求双方都写。写一边即可，写两边也不会出错。
- `suppress` 可选值：`description` / `speech_style` / `constraints`。不写时默认抹 `speech_style` 与 `constraints`，保留 `description`（描述性文本互相叠加通常不矛盾）。
- `kind` 拼错时按 **hard** 处理，并在诊断里报「无法识别」。宁可少一条词条，也不要让矛盾人格同时进 prompt。
- 优先级相同时按本轮得分比，得分也相同时按 `id` 字典序——保证同一份词条库在同样输入下结果**完全可复现**。
- `with` 指向不存在的 id 时会在静态校验里报「不存在」，但不阻止加载。

典型用法：「傲娇」与「坦率」是同一维度的两端 → 硬冲突。「傲娇」与「冷淡」可以叠加（表面冷淡内心在意），但说话风格得有个主导 → 软冲突，抑制低优先级方的 `speech_style`。

---

## 四、`modifiers`：随数值变化的动态调整

```json
{
  "when":   { "expr": "CFLAG:{CHARA}:好感度", "op": ">", "value": 85 },
  "effect": "suppress",
  "value":  0,
  "text":   "",
  "note":   "为什么这么设"
}
```

| `effect` | 作用 | 用哪个参数 |
| --- | --- | --- |
| `suppress` | 整条词条失效 | 无 |
| `weight` | 得分加上 `value`（可为负） | `value` |
| `description` | 用 `text` 替换描述 | `text` |
| `speech_style` | 用 `text` 替换说话风格 | `text` |
| `add_constraint` | 往约束列表追加一条 `text` | `text` |

一条词条可以有多个 modifier，按顺序依次判定。修改器作用在**本轮的实例**上，**不会污染词条库本体**——下一轮重新匹配时还是从原始定义开始。

用 `suppress` 实现「条件冲突」：好感度足够高时让「冷淡」失效，比硬写一条冲突规则更贴近实际（不是人格互斥，是状态变了）。

再次提醒：`when` 必须与 `match` 有交集，否则是死配置。

---

## 五、`override_npcs`：给特定 NPC 开小灶

通用词条覆盖大多数角色，个别重要 NPC 需要专属文案时用这个，不必为一个角色单独建一条词条。

```json
"override_npcs": [
  {
    "chara_no": 12,
    "description": "只给 12 号角色的定制描述",
    "speech_style": "只给 12 号角色的定制语气",
    "constraints": ["只给 12 号角色的定制约束"],
    "weight_bonus": 300,
    "force": true,
    "note": "为什么给这个角色开小灶"
  }
]
```

- `chara_no` 是**角色号**，即 `chara*.csv` 里的 `番号` / `NO`，也就是脚本里的 `NO`。**不是登录号**——登录号会随角色增删漂移。
- 四个文本字段留空则沿用通用文本，只填想改的那几个。`constraints` 是整体替换而非追加。
- `weight_bonus` 让这条词条对该 NPC 更容易被选中（名额只有 5 个）。
- `force: true` 时**无视 `match` 条件**强制命中。配合一个永不成立的 `match`（例如 `好感度 >= 99999`），就能做出「只有这个 NPC 才有」的专属词条。

---

## 六、`prompt`：改 system prompt 的骨架

这是**最常被人工调整**的部分。改措辞、改段落顺序、加全局铁律都在这里，改完重载即可。

```json
"prompt": {
  "layout": "你是 ERA 游戏的叙事 AI……\n当前登场角色：{NAME}（呼称：{CALLNAME}，角色号 {CHARA_NO}）。\n\n{TRAITS}\n\n{SPEECH}\n\n{CONSTRAINTS}\n\n{STATE}\n\n写作要求：……",
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
    { "label": "好感度", "expr": "CFLAG:{CHARA}:好感度" },
    { "label": "体力",   "expr": "BASE:{CHARA}:0" },
    { "label": "气力",   "expr": "BASE:{CHARA}:1" }
  ]
}
```

### 占位符

| 占位符 | 替换为 |
| --- | --- |
| `{NAME}` | 角色名（`NAME`） |
| `{CALLNAME}` | 呼称（`CALLNAME`） |
| `{CHARA_NO}` | 角色号 |
| `{TRAITS}` | 由命中词条的 `description` 组装的段落 |
| `{SPEECH}` | 由 `speech_style` 组装的段落 |
| `{CONSTRAINTS}` | 由 `constraints` 组装的段落（自动去重） |
| `{STATE}` | 由 `state_fields` 读出的权威数值段落 |

`layout` 留空时使用内置默认骨架。段落内容为空时该段落连标题一起省略，随后的连续空行会被压掉，不会在 prompt 里留大片空白浪费 token。

### `global_rules`

与词条无关、每轮都附加的硬规则。适合放「不要替玩家做决定」「不要输出数值」这类底线。它们会追加在 `layout` 之后。

### `state_fields`：告诉模型权威数值

这是防数值漂移的关键——让模型看到当前真实值，而不是从对话历史里推断。

- `expr` 支持 `{CHARA}` 占位符，写法与 `match` 的条件相同。
- **换游戏时这一段大概率要改**。默认假定了 `CFLAG:好感度`、`BASE:0`（体力）、`BASE:1`（气力）。
- 无法解析的字段会被**跳过**并在诊断里留一条 `状态字段无法解析`，不会让整个 prompt 失败。所以接新游戏后第一件事是点一次「显示词条诊断」。
- P3 的副 API 会复用这份 `state_fields` 定义，改这里等于同时改了两边，不会出现主副 API 状态定义不一致。

### `max_chars`

system prompt 的字符上限，默认 1200（设计目标 500–1000 字）。超出时从尾部截断并在诊断里提示。频繁触发截断说明命中词条太多或描述太长，应该压缩文本或降低某些词条的 `priority`。

---

## 七、名额、优先级与得分

单个角色最多命中 **5 条**词条（`AiTraitMatcher.MaxTraitsPerChara`）。超出时按得分保留最高的 5 条。

**得分 = `match.weight` + `priority` + `override_npcs.weight_bonus` + 所有生效的 `weight` 修改器**

优先级建议分层（默认库就是这么分的）：

| 层 | `priority` | 例子 |
| --- | --- | --- |
| 核心人格 | 80 | 角色的立身之本，绝不该被挤掉 |
| 常规性格 | 50 | 傲娇、坦率、冷淡 |
| 状态 | 30 | 疲惫、兴奋 |
| 全局基调 | 10 | `always: true` 的写作风格约定，只在还有空位时才进 |

`priority` 有两个作用：决定抽取顺序，以及**冲突时谁被抑制**。所以调 `priority` 会同时影响这两件事，改之前先想清楚是想改「谁进 prompt」还是想改「谁在冲突中赢」。如果只想改抽取顺序，改 `match.weight` 更安全。

新增 `always: true` 的词条时要注意：它每轮都命中，会占掉一个名额。默认库把 `baseline` 压到 `priority: 10` 就是为了让它排在最后。

---

## 八、改完怎么验证

### 日常改动（改文案、改条件、加词条）

1. 编辑 exe 同目录的 `ai_traits.json`（支持 `//` 注释和尾逗号，字段名大小写不敏感）
2. 游戏内菜单 `AI → 重载词条库` —— 会打印加载条数与全部静态校验诊断
3. 菜单 `AI → 显示词条诊断` —— 看有没有 `条件表达式无法解析` / `状态字段无法解析`
4. 菜单 `AI → 预览当前角色 prompt` —— 逐行打印实际发给模型的 prompt，以及命中了哪几条词条、各自得分
5. AI 面板发一条消息，输出区会打印 `[词条] 角色号 1｜命中 傲娇(175)、基础基调(20)｜prompt 516 字`

**「预览当前角色 prompt」是最重要的一步。** 词条系统的所有错误都表现为「prompt 里少了点什么」，而不是报错。

### 改了 C# 代码之后

跑 P2 词条自检（103 项，覆盖匹配、冲突、修改器、NPC 覆盖、prompt 装配、静态校验）：

```powershell
cd C:\Users\WinRN\Documents\ERA-AI\ERA-AI-1\work\emuera_lazyloading_selfmodified_version-main-skiasharp
dotnet build .\Emuera\Emuera.csproj -c Debug -p:Platform=x64 -p:RuntimeIdentifiers=win-x64 --source "$env:USERPROFILE\.nuget\packages"

$out = ".\Emuera\artifacts\bin\Emuera\debug"
$harness = "C:\Users\WinRN\Documents\ERA-AI\ERA-AI-1\work\p2-harness"
$env:ERA_AI_TRAIT_SELFTEST = "1"
$env:ERA_AI_TRAIT_SELFTEST_REPORT = "$harness\ai_trait_selftest.txt"
Start-Process -FilePath "$out\Emuera.exe" -ArgumentList "--ExeDir","$harness" -WorkingDirectory $out -WindowStyle Hidden
```

程序自动跑完退出，报告写到指定文件，末尾附带实际装出的完整 system prompt。自检会临时替换 `ai_traits.json` 并在结束时还原。

改了 `compute` 段相关的 C# 代码，还要跑 P3 副 API 自检（266 项，命令见 `outputs\P3-交付说明.md` 第六节）。

**三套自检不能同时开启**——它们抢界面线程、抢 `ai_traits.json`，还会互相踩数值。跑之前先把另外两个环境变量清掉：

```powershell
Remove-Item Env:ERA_AI_SELFTEST -ErrorAction SilentlyContinue
Remove-Item Env:ERA_AI_TRAIT_SELFTEST -ErrorAction SilentlyContinue
Remove-Item Env:ERA_AI_COMPUTE_SELFTEST -ErrorAction SilentlyContinue
```

顺便也跑一下 P0 自检（44 项，锁定与回注），确认没碰坏基础链路：

```powershell
$env:ERA_AI_SELFTEST = "1"
$env:ERA_AI_SELFTEST_REPORT = "C:\Users\WinRN\Documents\ERA-AI\ERA-AI-1\work\ai_selftest.txt"
Start-Process -FilePath "$out\Emuera.exe" -WorkingDirectory $out -WindowStyle Hidden
```

---

## 九、什么时候必须改 C# 代码

绝大多数需求不需要动代码。以下情况例外：

| 需求 | 改哪里 |
| --- | --- |
| 加新的比较运算符（比如 `in [1,2,3]`） | `AiTraitMatcher.EvaluateCondition` 的 `switch` |
| 加新的 `effect` 类型 | `AiTraitMatcher.ApplyModifiers` 的 `switch` + `AiTraitModifier` 的注释 |
| 加新的 `suppress` 字段 | `AiTraitConflictResolver.Suppress` 的 `switch` |
| 改命中数上限（5） | `AiTraitMatcher.MaxTraitsPerChara` |
| 加新的 prompt 占位符 | `AiPromptBuilder.Compose` 的 `Replace` 调用 |
| 改词条库的查找路径 | `AiTraitLibrary.ResolvePath` |
| 改静态校验规则 | `AiTraitLibrary.Validate` |
| 改内置默认库（只影响首次运行写出的那份） | `AiTraitDefaults.Json` |
| 改角色号→登录号的换算 | `AiPromptBuilder.BuildForCurrentTarget` |

改完这些必须重新构建，并跑 P2 自检。加了新的语法特性时，请在 `AiTraitSelfTest.Data.cs` 里补一份对应的测试库和断言——**这套系统的错误几乎都是静默的，没有自检就等于没有验证**。

### 铁律（改代码时不能违反）

1. **`AiTraitMatcher` 与 `AiPromptBuilder` 只能在界面线程调用。** 它们要读 ERA 变量，变量层没有任何同步保护，跨线程读写会破坏数据结构。
2. **不要新增 NuGet 包。** 本机无法访问 nuget.org，只能用 BCL 自带的 `HttpClient` + `System.Text.Json`。
3. **写错要报错，不要静默修正。** 静默修正会让人以为自己写对了。所有校验只报告不修正，这是有意为之。

---

## 十、常见问题速查

| 现象 | 大概率原因 |
| --- | --- |
| 改了 JSON 没反应 | 忘了点「重载词条库」；或改的是 `csv\ai_traits.json` 而程序读的是 exe 同目录那份（看诊断里的「来源」路径） |
| 某条词条死活不命中 | `match` 的变量名或命名下标在本游戏里不存在 → 看「显示词条诊断」的 `条件表达式无法解析` |
| 某个 modifier 好像没生效 | `when` 与 `match` 没有交集，是死配置 |
| prompt 里少了「说话风格」 | 该词条被软冲突抑制了 `speech_style`；或者命中的词条都没写这个字段 |
| prompt 里少了某条词条 | 被硬冲突淘汰、被 modifier 的 `suppress` 抑制，或者得分不够挤进前 5 |
| prompt 被截断 | 超过 `max_chars`，压缩描述或降低部分词条 `priority` |
| 数值状态段落缺了某一项 | 该 `state_fields` 的 `expr` 在本游戏里无法解析 → 看诊断 |
| 在库尾加了一条同 id 词条，前面那条不见了 | id 重复时保留文件中**先**出现的那条，后出现的被丢弃并留诊断 |
| 换了游戏后全部词条都不命中 | `CFLAG` / `TALENT` / `MARK` / `BASE` 的命名下标随游戏而异，必须按新游戏的 `csv\` 重新核对 |
| 完全不想用词条系统 | `AI → AI 设置` 里取消「启用词条系统」，退回静态兜底 prompt |

---

## 十一、文件位置一览

| 内容 | 路径 |
| --- | --- |
| 词条库（真身，改这个） | exe 同目录 `ai_traits.json` |
| 词条库（备选位置） | 游戏 `csv\ai_traits.json`（仅当 exe 同目录那份不存在时才读） |
| AI 配置（端点/模型/密钥/兜底 prompt/词条开关） | exe 同目录 `ai_config.json` |
| 内置默认词条库（首次运行的模板） | `Emuera\AI\Traits\AiTraitDefaults.cs` |
| 数据结构定义（字段注释即文档） | `Emuera\AI\Traits\AiTrait.cs` |
| P2 自检最小游戏环境 | `work\p2-harness\{csv,erb}` |
| P2 自检报告 | `work\p2-harness\ai_trait_selftest.txt` |
| P3 自检最小游戏环境 | `work\p3-harness\{csv,erb}` |
| P3 自检报告 | `work\p3-harness\ai_compute_selftest.txt` |
| P0 自检报告 | `work\ai_selftest.txt` |
| P2 技术交付文档 | `outputs\P2-交付说明.md` |
| P3 技术交付文档（副 API 与回写） | `outputs\P3-交付说明.md` |
---

## 十二、空白模板与新增一条词条的完整流程

### 空白模板（直接复制到 `traits` 数组里改）

```json
{
  "id": "唯一英文小写id_不要与现有重复",
  "name": "中文显示名",
  "tags": ["性格"],
  "priority": 50,
  "description": "这个人格是什么。写给模型看的定义，一到三句，讲清行为倾向与内在动机。",
  "speech_style": "怎么说话。句式、口头禅、语速、什么情况下语气变化。",
  "constraints": ["绝对不要做的事，一条一句，用否定式"],
  "match": {
    "all": [
      { "expr": "CFLAG:{CHARA}:好感度", "op": ">=", "value": 30 }
    ],
    "any": [],
    "none": [],
    "weight": 100
  },
  "conflicts": [],
  "modifiers": [],
  "override_npcs": [],
  "enabled": true,
  "note": "为什么加这条、以后想调整该从哪里下手"
}
```

只有 `id` 是必填且必须唯一。其余字段留空/删掉都能跑，但 `match` 三个列表全空且 `always` 不为 true 时这条词条永不命中。

### 六步流程

1. **想清楚这条词条要在什么条件下出现。** 先决定 `match`，再写文案——反过来容易写出一条永不命中的漂亮文案。
2. **确认变量名在本游戏里真实存在。** 打开游戏 `csv\` 目录对应的 CSV 文件核对命名下标；拿不准就直接写数字下标。
3. **填 `description` / `speech_style` / `constraints`。** 三者分工：是什么 / 怎么说 / 不许做。不要把说话风格塞进 description，装配时它们进的是不同段落。
4. **检查与现有词条的关系。** 同一维度的两端写 `hard`，可叠加但要争夺语气主导权的写 `soft`。同维度词条忘了写冲突，会出现「又傲娇又坦率」的矛盾 prompt。
5. **定 `priority`。** 照第七节的四层来：核心人格 80 / 常规性格 50 / 状态 30 / 全局基调 10。不确定就填 50。
6. **验证。** 菜单 `AI → 重载词条库` → `显示词条诊断` → `预览当前角色 prompt`，确认这条确实出现在 prompt 里。

### 写文案的几条经验

- **描述行为，不要描述标签。** ✗「她是个傲娇」 ✓「拒绝承认自己的在意，被戳中心事时提高音量转移话题」。模型对具体行为的服从度远高于抽象形容词。
- **给出触发条件而不是结论。** ✗「她会变温柔」 ✓「独处或情绪缓和时语速放慢、句尾变软」。
- **约束用否定式且可判定。** ✗「保持人物性格一致」 ✓「不要让她直接说出『我喜欢你』这类坦白台词」。前者模型无法据此判断某句话是否违规。
- **一条词条只讲一件事。** 想让角色同时有多个侧面，写多条让它们叠加，而不是把所有内容塞进一条 `description`——叠加时才能靠 `conflicts` 和 `modifiers` 精细控制。
- **控制长度。** 五条词条共享 1200 字上限，单条 `description` 超过 120 字就该考虑拆分或压缩。

---

## 十三、现有词条清单与节点定位速查

默认库自带 6 条词条。下表是**改动入口索引**：想调哪个行为，直接查到 id，然后在 `ai_traits.json` 里搜这个 id 定位到那个 JSON 对象。

| id | 名称 | tags | priority | 命中条件 | 冲突关系 | 修改器 |
| --- | --- | --- | --- | --- | --- | --- |
| `tsundere` | 傲娇 | 性格/恋爱 | 55 | 好感度 20–100 **且**（傲慢≥1 **或** 羞恥≥1） | 与 `honest` 硬冲突；与 `cold` 软冲突（抑制其 `speech_style`） | 好感度>85 → 整条失效；従順≥3 → 追加一条约束 |
| `honest` | 坦率 | 性格/恋爱 | 55 | 素直≥1 | 与 `tsundere` 硬冲突 | 无 |
| `cold` | 冷淡 | 性格/态度 | 40 | 好感度≤25 | 被 `clingy` 硬冲突淘汰 | 従順≥3 → 整条失效 |
| `clingy` | 黏人 | 性格/恋爱 | 45 | 好感度≥70 | 与 `cold` 硬冲突 | 无 |
| `exhausted` | 疲惫 | 状态/身体 | 30 | 体力（`BASE:0`）≤20 | 无 | 无 |
| `baseline` | 基础基调 | 全局 | 10 | `always: true`，每轮必中 | 无 | 无 |

### 常见诉求 → 该动哪个节点

| 我想…… | 打开 `ai_traits.json`，搜索 | 改这个字段 |
| --- | --- | --- |
| 让傲娇角色说话更凶 | `"id": "tsundere"` | `speech_style` |
| 改「傲娇」这个词的定义本身 | `"id": "tsundere"` | `description` |
| 让傲娇在更低好感度就出现 | `"id": "tsundere"` | `match.all` 里 `between` 的 `value`（下界 20） |
| 改「关系够近就不再掩饰」的门槛 | `"id": "tsundere"` | 第一个 modifier 的 `when.value`（85） |
| 允许傲娇和坦率共存 | `"id": "tsundere"` | 删掉 `conflicts` 里 `with: honest` 那条，或把 `hard` 改成 `soft` |
| 让冷淡的判定范围更宽 | `"id": "cold"` | `match.all` 的 `value`（25） |
| 让疲惫更早触发 | `"id": "exhausted"` | `match.all` 的 `value`（20） |
| 改每轮都生效的叙事基调 | `"id": "baseline"` | `description` / `constraints` |
| 改 system prompt 的段落顺序或措辞 | `"layout"` | `prompt.layout` |
| 改各段落的标题文字 | `"trait_header"` | `prompt.*_header` |
| 加一条全局铁律 | `"global_rules"` | 往数组里加一句 |
| 改 prompt 里展示哪些数值 | `"state_fields"` | 增删数组元素 |
| 放宽/收紧 prompt 长度 | `"max_chars"` | `prompt.max_chars`（默认 1200） |
| 临时关掉某条词条做对照 | 该词条的 `id` | `"enabled": false` |
| 完全绕过词条系统 | 不改 JSON | 菜单 `AI → AI 设置` 取消「启用词条系统」 |
| 给某个 NPC 专属人格 | 该词条的 `override_npcs` | 见第五节 |

### 换到另一个 ERA 游戏时的最小改动清单

默认库假定了下面这些命名下标。换游戏时按新游戏的 `csv\` 目录逐项核对，不存在的改名或改成数字下标：

| 用到的表达式 | 出现在 | 来源 CSV |
| --- | --- | --- |
| `CFLAG:{CHARA}:好感度` | `tsundere` / `cold` / `clingy` 的 match、`state_fields` | `CFLAG.CSV` |
| `TALENT:{CHARA}:傲慢`、`TALENT:{CHARA}:羞恥` | `tsundere` 的 match | `TALENT.CSV` |
| `TALENT:{CHARA}:素直` | `honest` 的 match | `TALENT.CSV` |
| `MARK:{CHARA}:従順` | `tsundere` / `cold` 的 modifier | `MARK.CSV` |
| `BASE:{CHARA}:0`（体力）、`BASE:{CHARA}:1`（气力） | `exhausted` 的 match、`state_fields` | `BASE.CSV` |

核对完必做一次 `显示词条诊断`：凡是解析失败的表达式都会在这里列出来，这是唯一能发现「变量名写错」的途径。

---

## 十四、`compute` 段：副 API 可写字段的日常维护

`compute` 段写在 `ai_traits.json` 顶层，与 `prompt`、`traits` 平级，管的是**副 API（计算通道）能改哪些数值、能改多狠**。它和词条放在同一个文件里，是为了复用 `prompt.state_fields` 的定义——主 API 和副 API 必须对「当前状态」用同一份定义，否则两边会漂移。

技术设计与串联流程见 `outputs\P3-交付说明.md`，这里只讲怎么改。

### 整段长什么样

```json
"compute": {
  "enabled": true,
  "memory_rounds": 4,
  "max_changes": 6,
  "include_all_charas": false,
  "on_out_of_range": "clamp",
  "extra_state_fields": [
    { "label": "所持金", "expr": "MONEY:0", "note": "副 API 要结算花销，所以要看得到钱" }
  ],
  "writable_fields": [
    {
      "field": "好感度",
      "target": "CFLAG:{CHARA}:好感度",
      "min": 0,
      "max": 100,
      "max_delta": 10,
      "ops": ["add"],
      "description": "对主角的好感，0-100",
      "note": "为什么定这个上限"
    }
  ]
}
```

### 段级字段

| 字段 | 含义 | 怎么定 |
| --- | --- | --- |
| `enabled` | 整段开关 | `false` 时即使 AI 设置里开了副 API 也不会调用。想临时停掉数值结算做对照，改这里 |
| `memory_rounds` | 短记忆轮数 | 3–5。上限硬编在代码里（`AiComputeMemory.MaxRounds` = 5），写超了只会带 5 轮并留一条诊断 |
| `max_changes` | 单轮变更条数上限 | 按「一轮事件合理能牵动几个数值」定，宁少勿多。填 0 或负数等于拒绝一切变更 |
| `include_all_charas` | 是否把所有已登录角色写进状态快照 | 默认 `false` 只写当前 `TARGET`，省 token。有多角色互动结算需求才开 |
| `on_out_of_range` | 最终值越界的处置 | `clamp`（默认，钳到边界）或 `reject`（整批拒绝）。除非你的游戏里越界代表严重逻辑错误，否则别改成 `reject` |

### `writable_fields` 的每一项

| 字段 | 必填 | 说明 |
| --- | --- | --- |
| `field` | 是 | 副 API 看到的字段名，会作为 schema 的 enum 值下发。用中文即可。**重名时只有先出现的那条生效**，后一条被静默丢弃（诊断里会报） |
| `target` | 是 | ERA 变量表达式。含 `{CHARA}` 即角色维度，装配时换成登录号 |
| `min` / `max` | 否 | 写入后允许的取值区间。不写则不限。**写反（`min > max`）时该字段任何值都写不进去** |
| `max_delta` | 否 | 单轮允许的最大变动幅度（绝对值），0 表示不限 |
| `ops` | 否 | 留空等于 `["add", "set"]`。只想让模型做增量就显式写 `["add"]` |
| `description` | 否 | 进 schema 的字段说明，模型看得到。写清语义与取值范围 |
| `note` | 否 | 只给人看，不进 prompt。写「为什么定这个上限」 |

### `max_delta` 是最重要的一项，值得单独说

三层校验里，字段层挡「字段名不存在」，区间层挡「结果越界」，**只有幅度层能挡住「数值合法但离谱」**。「好感度 +3」和「好感度 +3000」在字段层完全一样，区间层也只会把后者钳到 100（看起来还挺合理），只有 `max_delta` 能识别出后者是模型在胡说。

定值的方法：想一想「一轮对话/一次事件，这个数值最多可能变多少」，就填那个数。图省事写大等于放弃这道防线。

参考：好感度这类累积型情感值 5–10；体力这类会被休息事件回满的，允许 `set` 并把 `max_delta` 放宽到量程的一半（如 40）；金钱按游戏经济规模定。

### 新增一个可写字段的五步

1. **确认变量在本游戏里真实存在。** 打开游戏 `csv\` 目录对应的 CSV 核对命名下标，拿不准就写数字下标。
2. **确认它在 `AiVariableAccess.WritableNames` 白名单里。** 不在的话字段会被静默剔除（见下），需要改 C# 代码——判断标准见第九节。
3. **写 `target`。角色维度变量必须带 `{CHARA}`。** 这是最容易错也最难查的一步：漏了 `{CHARA}` 会变成「写到当前 TARGET 身上」，单角色场景下看起来完全正常。
4. **定 `min` / `max` / `max_delta` / `ops`。** `max_delta` 按上面的方法定，别留空。
5. **验证。** 菜单 `AI → 重载词条库` → `显示词条诊断` → `预览副 API 请求`，确认这个字段出现在「可写字段」列表和下发的 schema 里。

### 改完怎么验证

| 想确认什么 | 用哪个菜单 |
| --- | --- |
| 配置有没有写错 | `AI → 显示词条诊断`（`compute` 的九类静态校验都在这里） |
| 字段有没有真的下发给模型 | `AI → 预览副 API 请求`（列出可写字段、权威状态、完整 schema，不发请求） |
| 上一轮为什么没改数值 | `AI → 显示上轮副 API 往返`（模型原始输出 + 跳过原因 + 已写入项及理由） |

排查顺序：先看「显示上轮副 API 往返」定位是「模型没提」还是「提了被拒」；如果字段压根没出现在 schema 里，再看「预览副 API 请求」和诊断。

### 常见问题速查

| 现象 | 大概率原因 |
| --- | --- |
| 某个数值永远改不了，也不报错 | 该字段被静态筛选静默剔除了。看「显示词条诊断」，常见原因是变量不在白名单、角色维度变量漏了 `{CHARA}`、`ops` 里没有一个受支持的操作符 |
| 数值改了但改到了别人身上 | `target` 漏了 `{CHARA}`，写成了当前 `TARGET` |
| 明明是合理的变化却被整批拒绝 | `max_delta` 定得太紧，或 `max_changes` 太小。看「显示上轮副 API 往返」里的拒绝原因，那句话会写清是哪一项超了多少 |
| 结果总被钳在边界 | 正常。`on_out_of_range` 默认 `clamp`，诊断里会留一条钳制记录 |
| 该字段任何值都写不进去 | `min > max` 写反了。诊断会报 |
| 副 API 压根没被调用 | AI 设置里没开、端点/密钥没配、`compute.enabled` 为 false、当前没有 `TARGET`、或角色未登录。「显示上轮副 API 往返」的跳过原因会写明是哪一种 |
| 模型报出的数值与实际不一致 | 检查短记忆有没有被塞进权威数值。短记忆只该存「事件 + 结算摘要」，存了具体数值就一定会被当成现值 |
| 数值写了但正文失败，面板浮出三个按钮，发送变灰 | 这是设计好的待处置事务（RISK-05）。三条出路：「重生成」保数值只重写正文、「回滚数值」撤回本轮数值、「保留数值」认下数值直接往下玩。**处置之前发不出新请求**（硬拦截，因为事务只保留一份，被覆盖后就再也回滚不了），选一个即可继续 |
| 这轮的正文我喜欢，但结算不合理 | 用 `AI → 撤销上轮数值结算`，只撤数值不动正文。只能撤一次 |
| 想直接把某个数值改成我想要的值 | 用 `AI → 手动调整数值...`，见下一小节 |

### 玩家手动调整数值（允许"作弊"是有意的）

`AI → 手动调整数值...` 打开一个列表，每个可改字段一行，直接填最终值提交。

这条路的存在是明确的设计取向：**这套系统的目的是让玩家玩得开心，不是让玩家被经济/战斗系统的难度、或者一次自己都觉得不合理的 AI 结算卡住。** 所以不要以「防作弊」为理由给它加限制。

维护时需要知道的四件事：

- **可改范围就是 `compute.writable_fields`**，与副 API 完全一致。想让玩家能手改一个新字段，就把它加进 `writable_fields`（步骤见上面的「新增一个可写字段的五步」）——没有第二份配置。
- **`min` / `max` / `max_delta` 对手改只是显示参考，不强制。** 对话框右侧会显示「当前值｜设计区间｜AI 单轮 ≤ N」让玩家知道设计意图，但填多少都能提交。这两道闸门是挡模型幻觉的，模型不知道自己在胡说，玩家知道自己在做什么。
- **引擎级校验照旧生效。** 变量名白名单、类型、下标越界一个都不少。允许作弊不等于允许写坏存档，所以写不进去的条目会明确报错而不是静默失败。
- **`compute.enabled: false` 不会关掉手改。** 停用副 API 的语义是「别让模型改」，不是「别让玩家改」。真要完全关掉手改，只能清空 `writable_fields`（不推荐）。

副作用有两个，都是有意的：手改会写进副 API 短记忆（标注为「玩家手动调整」，否则副 API 下一轮会试图"圆"这个没有来由的跳变），并且会作废「撤销上轮数值结算」（撤销靠 `Before` 快照，手改之后那份快照已经不对应「上一轮写入前」了）。
### 换游戏时 `compute` 段的改动清单

| 默认库里的表达式 | 出现在 | 来源 CSV |
| --- | --- | --- |
| `CFLAG:{CHARA}:好感度` | `writable_fields` 的「好感度」 | `CFLAG.CSV` |
| `CFLAG:{CHARA}:信頼` | `writable_fields` 的「信赖」 | `CFLAG.CSV`（注意下标名是日文） |
| `BASE:{CHARA}:0` | `writable_fields` 的「体力」 | `BASE.CSV` |
| `MONEY:0` | `writable_fields` 的「所持金」、`extra_state_fields` | 引擎内置全局变量 |

核对完必做一次「显示词条诊断」+「预览副 API 请求」：前者报配置错误，后者确认字段真的进了 schema。
