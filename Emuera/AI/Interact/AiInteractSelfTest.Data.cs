namespace MinorShift.Emuera.AI.Interact;

/// <summary>
/// P4 自检用的词条库。每组测试装载各自的一份，避免相互干扰。
///
/// 全部基于 work\p4-harness 的最小游戏环境：
///   角色 1 爱丽丝：好感度 50、信頼 0、体力 100
///   角色 2 贝拉：  好感度 10、体力 100
///   全局：MONEY = 5000
///   SYSTEM.ERB：TINPUT 循环（整数等待）；喂入 777 会切到 TONEINPUT（单字符等待）
/// </summary>
internal static partial class AiInteractSelfTest
{
    /// <summary>
    /// 标准 interact 段：命令白名单 3 条，自由注入关闭，不自动执行。
    /// compute 段与 P3 保持一致，让交互与数值能在同一轮里一起断言。
    /// </summary>
    private const string InteractLibraryJson = """
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
            "on_out_of_range": "clamp",
            "writable_fields": [
              { "field": "好感度", "target": "CFLAG:{CHARA}:好感度", "min": 0, "max": 100, "max_delta": 10, "ops": ["add"], "description": "对主角的好感" },
              { "field": "体力", "target": "BASE:{CHARA}:0", "min": 0, "max": 100, "max_delta": 40, "ops": ["add", "set"] }
            ]
          },
          "interact": {
            "enabled": true,
            "auto_execute": false,
            "max_options": 3,
            "option_max_chars": 10,
            "allow_input_injection": false,
            "allowed_commands": [
              { "command": "抚摸", "value": 11, "description": "轻抚对方" },
              { "command": "交谈", "value": 12, "description": "与对方说话" },
              { "command": "结束回合", "value": 0, "description": "结束本回合" }
            ]
          },
          "traits": [
            { "id": "baseline", "name": "基调", "priority": 10, "description": "叙事保持克制。", "match": { "always": true, "weight": 10 } }
          ]
        }
        """;

    /// <summary>auto_execute = true。验证「摆出来等玩家点」与「立刻执行」两种取向都真的生效。</summary>
    private const string AutoExecuteLibraryJson = """
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
            "writable_fields": [
              { "field": "好感度", "target": "CFLAG:{CHARA}:好感度", "min": 0, "max": 100, "max_delta": 10, "ops": ["add"] }
            ]
          },
          "interact": {
            "enabled": true,
            "auto_execute": true,
            "max_options": 4,
            "option_max_chars": 24,
            "allowed_commands": [
              { "command": "抚摸", "value": 33, "description": "轻抚对方（载荷刻意与标准库不同，便于断言是这一份库生效）" }
            ]
          },
          "traits": [
            { "id": "baseline", "name": "基调", "description": "占位。", "match": { "always": true } }
          ]
        }
        """;

    /// <summary>
    /// 自由输入注入已开放，且两种类型都声明了范围。
    /// 这是最危险的一档配置，所以要单独一组验证「声明了才放行、声明范围外仍拒」。
    /// </summary>
    private const string InjectionLibraryJson = """
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
            "writable_fields": [
              { "field": "好感度", "target": "CFLAG:{CHARA}:好感度", "min": 0, "max": 100, "max_delta": 10, "ops": ["add"] }
            ]
          },
          "interact": {
            "enabled": true,
            "auto_execute": false,
            "allow_input_injection": true,
            "input_int_range": [0, 99],
            "input_str_max_chars": 8,
            "allowed_commands": [
              { "command": "抚摸", "value": 11 }
            ]
          },
          "traits": [
            { "id": "baseline", "name": "基调", "description": "占位。", "match": { "always": true } }
          ]
        }
        """;

    /// <summary>
    /// 注入开关开了但没声明范围。必须仍然拒绝两种注入——
    /// 「开了开关就等于放行」是这一段最容易出的理解错误。
    /// </summary>
    private const string InjectionNoRangeLibraryJson = """
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
            "writable_fields": [
              { "field": "好感度", "target": "CFLAG:{CHARA}:好感度", "min": 0, "max": 100, "max_delta": 10, "ops": ["add"] }
            ]
          },
          "interact": {
            "enabled": true,
            "allow_input_injection": true,
            "allowed_commands": [
              { "command": "抚摸", "value": 11 }
            ]
          },
          "traits": [
            { "id": "baseline", "name": "基调", "description": "占位。", "match": { "always": true } }
          ]
        }
        """;

    /// <summary>interact.enabled = false。契约在但被停用，一切交互内容都要被忽略。</summary>
    private const string DisabledInteractLibraryJson = """
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
            "writable_fields": [
              { "field": "好感度", "target": "CFLAG:{CHARA}:好感度", "min": 0, "max": 100, "max_delta": 10, "ops": ["add"] }
            ]
          },
          "interact": {
            "enabled": false,
            "allowed_commands": [
              { "command": "抚摸", "value": 11 }
            ]
          },
          "traits": [
            { "id": "baseline", "name": "基调", "description": "占位。", "match": { "always": true } }
          ]
        }
        """;

    /// <summary>完全没有 interact 段。必须安静地跳过，而不是报错或崩。</summary>
    private const string NoInteractLibraryJson = """
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
    /// 人为写错的 interact 段。每一条都对应一类"不校验就会静默失效"的错误：
    ///   max_options = 0        所有选项都会被丢掉
    ///   option_max_chars = 0   选项文本会被截成空串
    ///   命令名重复             后一条永远选不中
    ///   命令缺 command 名      模型无法引用
    ///   命令既无 value 又无 input   触发时喂一个空输入
    ///   命令同时有 value 与 input   实际只用 value，另一个被静默忽略
    ///   input 含换行           引擎会拆成多段输入，一条命令推进多步流程
    ///   注入开关开了但没声明范围
    /// </summary>
    private const string BrokenInteractLibraryJson = """
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
            "writable_fields": [
              { "field": "好感度", "target": "CFLAG:{CHARA}:好感度", "min": 0, "max": 100, "max_delta": 10, "ops": ["add"] }
            ]
          },
          "interact": {
            "enabled": true,
            "max_options": 0,
            "option_max_chars": 0,
            "allow_input_injection": true,
            "allowed_commands": [
              { "command": "重名", "value": 1 },
              { "command": "重名", "value": 2 },
              { "value": 3 },
              { "command": "空载荷" },
              { "command": "两个载荷", "value": 5, "input": "文本" },
              { "command": "带换行", "input": "第一段\n第二段" }
            ]
          },
          "traits": [
            { "id": "baseline", "name": "基调", "description": "占位。", "match": { "always": true } }
          ]
        }
        """;
}