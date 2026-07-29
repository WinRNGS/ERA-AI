using MinorShift.Emuera.Runtime.Script;
using MinorShift.Emuera.Runtime.Script.Statements.Expression;
using System.Collections.Generic;

namespace MinorShift.Emuera.Runtime.Script.Statements.Function;

internal sealed class FunctionMethodTerm : AExpression
{
	public FunctionMethodTerm(FunctionMethod meth, List<AExpression> args)
		: base(ResolveEraType(meth, args))
	{
		method = meth;
		arguments = args;
	}

	private static EraType ResolveEraType(FunctionMethod meth, List<AExpression> args)
	{
		if (meth.CanReturnFloat)
		{
			// 动态返回类型：若任一参数为 Float 则返回 Float，否则返回 Integer
			foreach (var arg in args)
			{
				if (arg != null && arg.GetEraType() == EraType.Float)
					return EraType.Float;
			}
			return EraType.Integer;
		}
		return meth.ReturnType == EraType.Integer ? EraType.Integer
			: meth.ReturnType == EraType.Float ? EraType.Float
			: EraType.String;
	}

	private FunctionMethod method;
	private List<AExpression> arguments;

	/// <summary>
	/// 覆盖基类 GetEraType()，对 CanReturnFloat 函数动态检查参数类型。
	/// 基类 eraType 在构造时确定，但重构后参数类型可能变化（如 SingleFloatTerm 替换原表达式），
	/// 因此需要运行时重新检查。
	/// </summary>
	public override EraType GetEraType()
	{
		if (method.CanReturnFloat)
		{
			foreach (var arg in arguments)
			{
				if (arg != null && arg.GetEraType() == EraType.Float)
					return EraType.Float;
			}
			return EraType.Integer;
		}
		return eraType;
	}

	public override long GetIntValue(ExpressionMediator exm)
	{
		return method.GetIntValue(exm, arguments);
	}
	public override string GetStrValue(ExpressionMediator exm)
	{
		return method.GetStrValue(exm, arguments);
	}
	public override double GetFloatValue(ExpressionMediator exm)
	{
		return method.GetFloatValue(exm, arguments);
	}
	public override SingleTerm GetValue(ExpressionMediator exm)
	{
		return method.GetReturnValue(exm, arguments);
	}

	public override AExpression Restructure(ExpressionMediator exm)
	{
		if (method.HasUniqueRestructure)
		{
			if (method.UniqueRestructure(exm, [.. arguments]) && method.CanRestructure)
				return GetValue(exm);
			return this;
		}
		bool argIsConst = true;
		for (int i = 0; i < arguments.Count; i++)
		{
			if (arguments[i] == null)
				continue;
			arguments[i] = arguments[i].Restructure(exm);
			argIsConst &= arguments[i] is SingleTerm;
		}
		if (method.CanRestructure && argIsConst)
			return GetValue(exm);
		return this;

	}

}
