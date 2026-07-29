using MinorShift.Emuera.Runtime.Script.Data;
using System.Collections.Generic;

namespace MinorShift.Emuera.Runtime.Script.Statements.Expression;

internal sealed class VariadicArgTerm : AExpression
{
	private readonly List<AExpression> _args;
	private readonly UserDifinedFunctionDataArgType _type;

	public VariadicArgTerm(List<AExpression> args, UserDifinedFunctionDataArgType type)
		: base((type & UserDifinedFunctionDataArgType.__BaseType) switch
		{
			UserDifinedFunctionDataArgType.Str => EraType.String,
			UserDifinedFunctionDataArgType.Float => EraType.Float,
			_ => EraType.Integer
		})
	{
		_args = args;
		_type = type;
	}

	public int Count => _args.Count;

	public AExpression this[int index] => index >= 0 && index < _args.Count ? _args[index] : null;

	public long GetArgInt(ExpressionMediator exm, int index)
	{
		if (index < 0 || index >= _args.Count) return 0;
		return _args[index].GetIntValue(exm);
	}

	public string GetArgStr(ExpressionMediator exm, int index)
	{
		if (index < 0 || index >= _args.Count) return "";
		return _args[index].GetStrValue(exm);
	}

	public override AExpression Restructure(ExpressionMediator exm)
	{
		for (int i = 0; i < _args.Count; i++)
			_args[i] = _args[i].Restructure(exm);
		return this;
	}
}
