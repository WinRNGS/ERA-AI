using MinorShift.Emuera.Runtime.Script;
using MinorShift.Emuera.Runtime.Script.Data;
using System;

namespace MinorShift.Emuera.Runtime.Script.Statements.Expression;

internal sealed class NullTerm : AExpression
{
	public NullTerm(long i)
		: base(EraType.Integer)
	{
	}

	public NullTerm(string s)
		: base(EraType.String)
	{
	}
}

/// <summary>
/// 項。一単語だけ。
/// </summary>
internal class SingleTerm : AExpression
{
	protected SingleTerm(EraType et)
		: base(et)
	{

	}

	public override bool IsConst
	{
		get { return true; }
	}

	public override AExpression Restructure(ExpressionMediator exm)
	{
		return this;
	}
}

internal sealed class SingleStrTerm : SingleTerm
{
	public SingleStrTerm(string s)
		: base(EraType.String)
	{
		sValue = s;
	}
	string sValue;
	public override string GetStrValue(ExpressionMediator exm)
	{
		return sValue;
	}
	public override SingleTerm GetValue(ExpressionMediator exm)
	{
		return this;
	}
	public string Str
	{
		get
		{
			return sValue;
		}
	}
	public override string ToString()
	{
		return sValue.ToString();
	}
}


internal sealed class SingleLongTerm : SingleTerm
{
	public SingleLongTerm(long i)
		: base(EraType.Integer)
	{
		iValue = i;
	}
	readonly long iValue;

	public override long GetIntValue(ExpressionMediator exm)
	{
		return iValue;
	}
	public override SingleTerm GetValue(ExpressionMediator exm)
	{
		return this;
	}

	public long Int
	{
		get
		{
			return iValue;
		}
	}
	public override string ToString()
	{
		return iValue.ToString();
	}

	public override AExpression Restructure(ExpressionMediator exm)
	{
		return this;
	}
}

internal sealed class SingleFloatTerm : SingleTerm
{
	public SingleFloatTerm(double d)
		: base(EraType.Float)
	{
		fValue = d;
	}
	readonly double fValue;

	public override double GetFloatValue(ExpressionMediator exm)
	{
		return fValue;
	}
	public override long GetIntValue(ExpressionMediator exm)
	{
		return (long)fValue;
	}
	public override SingleTerm GetValue(ExpressionMediator exm)
	{
		return this;
	}

	public double Float
	{
		get
		{
			return fValue;
		}
	}
	public override string ToString()
	{
		return fValue.ToString();
	}

	public override AExpression Restructure(ExpressionMediator exm)
	{
		return this;
	}
}


/// <summary>
/// 項。一単語だけ。
/// </summary>
internal sealed class StrFormTerm : AExpression
{
	public StrFormTerm(StrForm sf)
		: base(EraType.String)
	{
		sfValue = sf;
	}
	readonly StrForm sfValue;

	public StrForm StrForm
	{
		get
		{
			return sfValue;
		}
	}

	public override string GetStrValue(ExpressionMediator exm)
	{
		return sfValue.GetString(exm);
	}
	public override SingleTerm GetValue(ExpressionMediator exm)
	{
		return new SingleStrTerm(sfValue.GetString(exm));
	}

	public override AExpression Restructure(ExpressionMediator exm)
	{
		sfValue.Restructure(exm);
		if (sfValue.IsConst)
			return new SingleStrTerm(sfValue.GetString(exm));
		AExpression term = sfValue.GetAExpression();
		if (term != null)
			return term;
		return this;
	}
}
