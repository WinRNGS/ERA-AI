using MinorShift.Emuera.Runtime.Script;
using MinorShift.Emuera.Runtime.Script.Parser;
using MinorShift.Emuera.Runtime.Script.Statements;
using MinorShift.Emuera.Runtime.Script.Statements.Expression;
using MinorShift.Emuera.Runtime.Script.Statements.Variable;
using MinorShift.Emuera.Runtime.Utils;
using System;
using System.Collections.Generic;

namespace MinorShift.Emuera.AI;

/// <summary>
/// 数据访问层：按变量名读写 ERA 运行期变量。
///
/// 三条铁律：
///   1. 只允许在界面线程调用。变量层底层是普通字典，无任何同步保护，跨线程写会破坏内部结构。
///   2. 写入前必过白名单 + 常量检查 + 类型检查 + 下标越界检查。引擎对写错的变量名与越界下标
///      都是静默失败（表达式求值返回默认值、稀疏数组越界不抛异常），不校验等于放弃正确性。
///   3. 整批要么全写、要么全不写。任一项校验失败即拒绝整批，避免存档进入半更新状态。
/// </summary>
internal static class AiVariableAccess
{
    /// <summary>
    /// 允许 AI 写入的变量名白名单（不含下标部分）。
    /// 按实际 ERA 游戏变量分类补全。只允许有实际游戏意义的存档/角色变量，
    /// 不允许控制流变量（RESULT/TARGET/SELECTCOM 等）与常量（*NAME 系列）。
    /// </summary>
    private static readonly HashSet<string> WritableNames = new(StringComparer.OrdinalIgnoreCase)
    {
        // ========== 全局整数数组 ==========
        "FLAG",
        "TFLAG",
        "MONEY",
        "ITEM",
        "DAY",
        "TIME",
        "UP",
        "DOWN",
        "LOSEBASE",
        "EJAC",
        "PBAND",
        "BOUGHT",
        "ITEMSALES",
        "PREVCOM",
        "NEXTCOM",
        "ASSIPLAY",
        "GLOBAL",

        // ========== 全局字符串数组 ==========
        "STR",
        "SAVESTR",
        "TSTR",
        "GLOBALS",

        // ========== 角色整数数组（CHARACTER_DATA） ==========
        "BASE",
        "MAXBASE",
        "ABL",
        "TALENT",
        "EXP",
        "MARK",
        "PALAM",
        "SOURCE",
        "EX",
        "CFLAG",
        "JUEL",
        "RELATION",
        "EQUIP",
        "TEQUIP",
        "STAIN",
        "GOTJUEL",
        "NOWEX",
        "DOWNBASE",
        "CUP",
        "CDOWN",
        "TCVAR",

        // ========== 角色字符串 ==========
        "CSTR",
        "NICKNAME",
        "MASTERNAME",

        // ========== 角色二维数组 ==========
        "CDFLAG",

        // ========== 全局二维整数数组 ==========
        "DITEMTYPE",
        "DA",
        "DB",
        "DC",
        "DD",
        "DE",

        // ========== 全局三维整数数组 ==========
        "TA",
        "TB",
    };

    private static readonly HashSet<string> AllowedOps = new(StringComparer.OrdinalIgnoreCase)
    {
        "set",
        "add",
        "mul",
    };

    /// <summary>
    /// 角色维度变量名。写这些变量必须带角色下标，否则引擎会落到当前 TARGET 上——
    /// 这不是报错，而是"写到了别人身上"，属于最难查的一类问题，所以配置阶段就要提醒。
    /// </summary>
    private static readonly HashSet<string> CharaScopedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "BASE", "MAXBASE", "ABL", "TALENT", "EXP", "MARK", "PALAM", "SOURCE", "EX",
        "CFLAG", "JUEL", "RELATION", "EQUIP", "TEQUIP", "STAIN", "GOTJUEL", "NOWEX",
        "DOWNBASE", "CUP", "CDOWN", "TCVAR", "CSTR", "NICKNAME", "MASTERNAME", "CDFLAG",
    };

    public static bool IsWritableName(string name) => name != null && WritableNames.Contains(name);

    public static bool IsAllowedOp(string op) => op != null && AllowedOps.Contains(op);

    /// <summary>
    /// 只按变量名做静态可写性检查，不解析表达式、不读变量，因此可以在引擎就绪之前调用
    /// （词条库在 InstallAiMenu 阶段就要加载，那时 IdentifierDictionary 还没建好）。
    /// 完整校验仍由 Validate 在真正写入前执行。
    /// </summary>
    public static bool IsWritableTargetName(string target, out string reason)
    {
        reason = null;
        if (string.IsNullOrWhiteSpace(target))
        {
            reason = "target 为空";
            return false;
        }

        string head = target.Trim();
        int colon = head.IndexOf(':');
        string name = (colon >= 0 ? head[..colon] : head).Trim();
        if (name.Length == 0)
        {
            reason = $"无法从 \"{target}\" 中识别变量名";
            return false;
        }
        if (!IsWritableName(name))
        {
            reason = $"变量 {name} 不在白名单内（见 AiVariableAccess.WritableNames）";
            return false;
        }
        if (CharaScopedNames.Contains(name) && colon < 0)
        {
            reason = $"{name} 是角色维度变量，target 必须带角色下标（用 {{CHARA}} 占位），否则会写到当前 TARGET 身上";
            return false;
        }
        return true;
    }

    /// <summary>
    /// 解析变量表达式字符串为 VariableTerm。失败返回 null 并给出原因。
    /// 复用引擎自带的词法与表达式解析，因此支持 CFLAG:5:好感度 这类带角色维度与命名下标的写法。
    /// </summary>
    public static VariableTerm Resolve(string target, out string error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(target))
        {
            error = "变量名为空";
            return null;
        }
        try
        {
            WordCollection wc = LexicalAnalyzer.Analyse(new CharStream(target), LexEndWith.EoL, LexAnalyzeFlag.None);
            AExpression term = ExpressionParser.ReduceExpressionTerm(wc, TermEndWith.EoL);
            if (term is not VariableTerm variable || variable.Identifier == null)
            {
                error = $"不是可识别的变量：{target}";
                return null;
            }
            return variable;
        }
        catch (Exception e)
        {
            error = $"变量名解析失败（{target}）：{e.Message}";
            return null;
        }
    }

    /// <summary>
    /// 校验单条变更，不做任何写入。
    /// </summary>
    public static bool Validate(AiValueChange change, out string error)
    {
        error = null;
        if (change == null)
        {
            error = "变更项为 null";
            return false;
        }
        if (!AllowedOps.Contains(change.Op ?? ""))
        {
            error = $"不允许的操作符：{change.Op}";
            return false;
        }

        VariableTerm variable = Resolve(change.Target, out error);
        if (variable == null)
            return false;

        var token = variable.Identifier;
        if (!IsWritableName(token.Name))
        {
            error = $"变量不在白名单内：{token.Name}";
            return false;
        }
        if (token.IsConst)
        {
            error = $"变量为常量，不可写入：{token.Name}";
            return false;
        }

        EraType type = variable.GetEraType();
        if (change.IsStringAssign)
        {
            if (type != EraType.String)
            {
                error = $"变量 {change.Target} 不是字符串型";
                return false;
            }
            if (!string.Equals(change.Op, "set", StringComparison.OrdinalIgnoreCase))
            {
                error = "字符串变量仅支持 set";
                return false;
            }
        }
        else if (type != EraType.Integer)
        {
            error = $"变量 {change.Target} 不是整数型";
            return false;
        }

        if (!CheckIndexInRange(variable, out error))
            return false;

        try
        {
            if (change.IsStringAssign)
                _ = variable.GetStrValue(GlobalStatic.EMediator);
            else
                _ = variable.GetIntValue(GlobalStatic.EMediator);
        }
        catch (Exception e)
        {
            error = $"变量 {change.Target} 访问失败：{e.Message}";
            return false;
        }
        return true;
    }

    /// <summary>
    /// 显式做下标越界检查。引擎的 CheckElement 会对越界下标抛 CodeEE。
    /// </summary>
    private static bool CheckIndexInRange(VariableTerm variable, out string error)
    {
        error = null;
        var token = variable.Identifier;
        int count = variable.ArgumentCount;
        if (count == 0)
            return true;
        try
        {
            var indices = new long[Math.Max(count, 3)];
            for (int i = 0; i < count; i++)
                indices[i] = variable.GetElementInt(i, GlobalStatic.EMediator);
            token.CheckElement(indices);
        }
        catch (Exception e)
        {
            error = $"变量 {token.Name} 下标越界或不可访问：{e.Message}";
            return false;
        }
        return true;
    }

    /// <summary>
    /// 原子应用一批变更。必须在界面线程调用。
    /// 先整批校验，全部通过后才逐条写入；任一项校验失败则整批拒绝。
    /// </summary>
    public static bool TryApplyAll(IReadOnlyList<AiValueChange> changes, out string error)
    {
        error = null;
        if (changes == null || changes.Count == 0)
            return true;
        if (GlobalStatic.EMediator == null)
        {
            error = "表达式求值器尚未就绪";
            return false;
        }

        var resolved = new List<(VariableTerm Variable, AiValueChange Change)>(changes.Count);
        foreach (var change in changes)
        {
            if (!Validate(change, out error))
                return false;
            VariableTerm variable = Resolve(change.Target, out error);
            if (variable == null)
                return false;
            resolved.Add((variable, change));
        }

        var exm = GlobalStatic.EMediator;
        foreach (var (variable, change) in resolved)
        {
            try
            {
                if (change.IsStringAssign)
                {
                    variable.SetValue(change.StrValue ?? "", exm);
                    continue;
                }
                long current = variable.GetIntValue(exm);
                long next = change.Op.ToLowerInvariant() switch
                {
                    "add" => current + change.Value,
                    "mul" => current * change.Value,
                    _ => change.Value,
                };
                variable.SetValue(next, exm);
            }
            catch (Exception e)
            {
                error = $"写入 {change.Target} 失败：{e.Message}";
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// 读取字符串变量的快照值。失败返回 false，不抛出。
    /// </summary>
    public static bool TryReadStr(string target, out string value, out string error)
    {
        value = "";
        VariableTerm variable = Resolve(target, out error);
        if (variable == null)
            return false;
        if (variable.GetEraType() != EraType.String)
        {
            error = $"变量 {target} 不是字符串型";
            return false;
        }
        if (GlobalStatic.EMediator == null)
        {
            error = "表达式求值器尚未就绪";
            return false;
        }
        try
        {
            value = variable.GetStrValue(GlobalStatic.EMediator) ?? "";
            return true;
        }
        catch (Exception e)
        {
            error = $"读取 {target} 失败：{e.Message}";
            return false;
        }
    }

    /// <summary>
    /// 读取整数变量的快照值。失败返回 false，不抛出。
    /// </summary>
    public static bool TryReadInt(string target, out long value, out string error)
    {
        value = 0;
        VariableTerm variable = Resolve(target, out error);
        if (variable == null)
            return false;
        if (variable.GetEraType() != EraType.Integer)
        {
            error = $"变量 {target} 不是整数型";
            return false;
        }
        if (GlobalStatic.EMediator == null)
        {
            error = "表达式求值器尚未就绪";
            return false;
        }
        try
        {
            value = variable.GetIntValue(GlobalStatic.EMediator);
            return true;
        }
        catch (Exception e)
        {
            error = $"读取 {target} 失败：{e.Message}";
            return false;
        }
    }
}
