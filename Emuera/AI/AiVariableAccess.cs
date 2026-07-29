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
	/// P0 阶段先放开少量通用变量用于自检；接入副 API 前必须按实际游戏的 CSV 补全并收紧。
	/// </summary>
	private static readonly HashSet<string> WritableNames = new(StringComparer.OrdinalIgnoreCase)
	{
		"FLAG",
		"CFLAG",
		"TFLAG",
		"MONEY",
		"ABL",
		"EXP",
		"MARK",
		"TALENT",
		"CSTR",
		"STR",
	};

	private static readonly HashSet<string> AllowedOps = new(StringComparer.OrdinalIgnoreCase)
	{
		"set",
		"add",
		"mul",
	};

	public static bool IsWritableName(string name) => name != null && WritableNames.Contains(name);

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

		// 越界检查。
		// 关键：引擎对越界【读】是静默返回默认值（稀疏数组不抛异常），对越界【写】也不抛异常，
		// 数据存进去之后会在存档时被静默丢弃。因此不能靠 try-read 来探测越界，
		// 必须显式调用引擎自带的下标检查（CheckElement）。这是 RISK-23 的唯一可靠拦法。
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