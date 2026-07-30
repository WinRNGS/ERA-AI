namespace MinorShift.Emuera.AI.Compute;

/// <summary>
/// P3 自检用的词条库。每组测试装载各自的一份，避免相互干扰。
///
/// 全部基于 work\p3-harness 的最小游戏环境：
///   角色 1 爱丽丝：好感度 50、信頼 0、体力 100
///   角色 2 贝拉：  好感度 10、体力 100
///   全局：MONEY = 5000（由 SYSTEM.ERB 设置）
/// </summary>
internal static partial class AiComputeSelfTest
{
    /// <summary>正常的 compute 段。越界按 clamp 处置。</summary>
    private const string ComputeLibraryJson = """
        {
          "version": 1,
          "prompt": {
            "state_fields": [
              { "label": "好感度", "expr": "CFLAG:{CHARA}:好感度" },
              { "label": "体力", "expr": "BASE:{CHARA}:0" }
            ]
          },
          "compute": {
            "enabled": true,
            "memory_rounds": 3,
            "max_changes": 4,
            "include_all_charas": false,
            "on_out_of_range": "clamp",
            "extra_state_fields": [
              { "label": "所持金", "expr": "MONEY:0" }
            ],
            "writable_fields": [
              { "field": "好感度", "target": "CFLAG:{CHARA}:好感度", "min": 0, "max": 100, "max_delta": 10, "ops": ["add"], "description": "对主角的好感" },
              { "field": "信赖", "target": "CFLAG:{CHARA}:信頼", "min": 0, "max": 100, "max_delta": 8, "ops": ["add"] },
              { "field": "体力", "target": "BASE:{CHARA}:0", "min": 0, "max": 100, "max_delta": 40, "ops": ["add", "set"] },
              { "field": "所持金", "target": "MONEY:0", "min": 0, "max": 99999999, "max_delta": 100000, "ops": ["add"] }
            ]
          },
          "traits": [
            {
              "id": "baseline",
              "name": "基础基调",
              "priority": 10,
              "description": "叙事保持克制。",
              "match": { "always": true, "weight": 10 }
            }
          ]
        }
        """;

    /// <summary>越界改为整批拒绝。用于验证 on_out_of_range 两种取值都真的生效。</summary>
    private const string RejectLibraryJson = """
        {
          "version": 1,
          "prompt": {
            "state_fields": [
              { "label": "好感度", "expr": "CFLAG:{CHARA}:好感度" }
            ]
          },
          "compute": {
            "enabled": true,
            "memory_rounds": 0,
            "max_changes": 4,
            "on_out_of_range": "reject",
            "writable_fields": [
              { "field": "好感度", "target": "CFLAG:{CHARA}:好感度", "min": 0, "max": 55, "max_delta": 50, "ops": ["add"] }
            ]
          },
          "traits": [
            { "id": "baseline", "name": "基调", "description": "占位。", "match": { "always": true } }
          ]
        }
        """;

    /// <summary>include_all_charas = true，验证快照能覆盖全部已登录角色。</summary>
    private const string AllCharasLibraryJson = """
        {
          "version": 1,
          "prompt": {
            "state_fields": [
              { "label": "好感度", "expr": "CFLAG:{CHARA}:好感度" }
            ]
          },
          "compute": {
            "enabled": true,
            "memory_rounds": 0,
            "include_all_charas": true,
            "writable_fields": [
              { "field": "好感度", "target": "CFLAG:{CHARA}:好感度", "min": 0, "max": 100, "max_delta": 10, "ops": ["add"] }
            ]
          },
          "traits": [
            { "id": "baseline", "name": "基调", "description": "占位。", "match": { "always": true } }
          ]
        }
        """;

    /// <summary>
    /// 人为写错的 compute 段。每一条都对应一类"不校验就会静默失效"的错误：
    ///   dup_field    field 名重复 —— 后一条永远选不中
    ///   no_target    缺 target —— 写入必定失败
    ///   bad_range    min > max —— 任何值都越界
    ///   naked_chara  角色维度变量没带下标 —— 会写到当前 TARGET 身上，最难查
    ///   bad_op       声明了不支持的操作符
    ///   not_listed   变量不在白名单里
    /// </summary>
    private const string BrokenComputeLibraryJson = """
        {
          "version": 1,
          "prompt": {
            "state_fields": [
              { "label": "好感度", "expr": "CFLAG:{CHARA}:好感度" }
            ]
          },
          "compute": {
            "enabled": true,
            "memory_rounds": 99,
            "max_changes": 0,
            "on_out_of_range": "explode",
            "writable_fields": [
              { "field": "重名", "target": "CFLAG:{CHARA}:好感度", "ops": ["add"] },
              { "field": "重名", "target": "CFLAG:{CHARA}:信頼", "ops": ["add"] },
              { "field": "缺目标", "ops": ["add"] },
              { "field": "区间反了", "target": "BASE:{CHARA}:0", "min": 100, "max": 10, "ops": ["add"] },
              { "field": "裸角色变量", "target": "CFLAG", "ops": ["add"] },
              { "field": "坏操作符", "target": "MONEY:0", "ops": ["divide"] },
              { "field": "非白名单", "target": "RESULT:0", "ops": ["add"] },
              { "field": "好用的字段", "target": "MONEY:0", "min": 0, "max": 999999, "max_delta": 1000, "ops": ["add"] }
            ]
          },
          "traits": [
            { "id": "baseline", "name": "基调", "description": "占位。", "match": { "always": true } }
          ]
        }
        """;

    /// <summary>完全没有 compute 段。副 API 必须安静地跳过，而不是报错或崩。</summary>
    private const string NoComputeLibraryJson = """
        {
          "version": 1,
          "prompt": {
            "state_fields": [
              { "label": "好感度", "expr": "CFLAG:{CHARA}:好感度" }
            ]
          },
          "traits": [
            { "id": "baseline", "name": "基调", "description": "占位。", "match": { "always": true } }
          ]
        }
        """;

    /// <summary>compute.enabled = false。契约在但被停用。</summary>
    private const string DisabledComputeLibraryJson = """
        {
          "version": 1,
          "prompt": {
            "state_fields": [
              { "label": "好感度", "expr": "CFLAG:{CHARA}:好感度" }
            ]
          },
          "compute": {
            "enabled": false,
            "writable_fields": [
              { "field": "好感度", "target": "CFLAG:{CHARA}:好感度", "min": 0, "max": 100, "max_delta": 10, "ops": ["add"] }
            ]
          },
          "traits": [
            { "id": "baseline", "name": "基调", "description": "占位。", "match": { "always": true } }
          ]
        }
        """;
    /// <summary>
    /// 区间写反（min > max）但字段本身通过了静态可写性检查。
    /// 单独一组：这类配置错误在装配阶段拦不住（变量名合法），只能在校验阶段体现为"任何值都被拒"。
    /// </summary>
    private const string InvertedRangeLibraryJson = """
        {
          "version": 1,
          "prompt": {
            "state_fields": [
              { "label": "好感度", "expr": "CFLAG:{CHARA}:好感度" }
            ]
          },
          "compute": {
            "enabled": true,
            "memory_rounds": 0,
            "max_changes": 4,
            "on_out_of_range": "clamp",
            "writable_fields": [
              { "field": "好感度", "target": "CFLAG:{CHARA}:好感度", "min": 100, "max": 10, "max_delta": 50, "ops": ["add"] }
            ]
          },
          "traits": [
            { "id": "baseline", "name": "基调", "description": "占位。", "match": { "always": true } }
          ]
        }
        """;
}