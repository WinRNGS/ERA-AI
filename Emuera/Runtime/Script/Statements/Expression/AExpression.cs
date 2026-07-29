using System;
using MinorShift.Emuera.Runtime.Script;

namespace MinorShift.Emuera.Runtime.Script.Statements.Expression;

internal abstract class AExpression
{
	public AExpression(EraType et)
	{
		eraType = et;
	}

	public Type GetOperandType()
	{
		return eraType switch
		{
			EraType.Integer => typeof(long),
			EraType.String => typeof(string),
			EraType.Float => typeof(double),
			_ => typeof(void)
		};
	}

	public virtual EraType GetEraType()
	{
		return eraType;
	}

	public virtual long GetIntValue(ExpressionMediator exm)
	{
		return 0;
	}
	public virtual string GetStrValue(ExpressionMediator exm)
	{
		return "";
	}
	public virtual double GetFloatValue(ExpressionMediator exm)
	{
		return 0.0;
	}
	public virtual SingleTerm GetValue(ExpressionMediator exm)
	{
		if (eraType == EraType.Integer)
			return new SingleLongTerm(0);
		else if (eraType == EraType.String)
			return new SingleStrTerm("");
		else
			return new SingleFloatTerm(0.0);
	}
	public bool IsInteger
	{
		get { return eraType == EraType.Integer; }
	}
	public bool IsString
	{
		get { return eraType == EraType.String; }
	}
	public bool IsFloat
	{
		get { return eraType == EraType.Float; }
	}
	public virtual bool IsConst
	{
		get { return false; }
	}
	protected readonly EraType eraType;

	public virtual AExpression Restructure(ExpressionMediator exm)
	{
		return this;
	}
}
