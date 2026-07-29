using System;
using MinorShift.Emuera.Runtime.Script;
using MinorShift.Emuera.Runtime.Script.Statements.Function;
using MinorShift.Emuera.Runtime.Script.Statements.Variable;
using MinorShift.Emuera.Runtime.Utils;
using MinorShift.Emuera.Runtime.Utils.EvilMask;
using System.Collections.Generic;
using System.Text;
using trerror = MinorShift.Emuera.Runtime.Utils.EvilMask.Lang.Error;

namespace MinorShift.Emuera.Runtime.Script.Statements.Expression;

/// <summary>
/// 引数のチェック、戻り値の型チェック等は全て呼び出し元が責任を負うこと。
/// </summary>
internal abstract class OperatorMethod : FunctionMethod
{
	public OperatorMethod()
	{
		argumentTypeArray = null;
	}
	public override string CheckArgumentType(string name, List<AExpression> arguments) { throw new ExeEE("型チェックは呼び出し元が行うこと"); }
}

internal static class OperatorMethodManager
{
	readonly static Dictionary<OperatorCode, OperatorMethod> unaryDic = [];
	readonly static Dictionary<OperatorCode, OperatorMethod> unaryAfterDic = [];
	readonly static Dictionary<OperatorCode, OperatorMethod> binaryIntIntDic = [];
	readonly static Dictionary<OperatorCode, OperatorMethod> binaryStrStrDic = [];
	readonly static Dictionary<OperatorCode, OperatorMethod> binaryFloatFloatDic = [];
	readonly static Dictionary<OperatorCode, OperatorMethod> binaryMixedFloatDic;
	readonly static OperatorMethod binaryMultIntStr;
	readonly static OperatorMethod ternaryIntIntInt;
	readonly static OperatorMethod ternaryIntStrStr;
	readonly static OperatorMethod ternaryIntFloatFloat;

	static OperatorMethodManager()
	{
		unaryDic[OperatorCode.Plus] = new PlusInt();
		unaryDic[OperatorCode.Minus] = new MinusInt();
		unaryDic[OperatorCode.Not] = new NotInt();
		unaryDic[OperatorCode.BitNot] = new BitNotInt();
		unaryDic[OperatorCode.Increment] = new IncrementInt();
		unaryDic[OperatorCode.Decrement] = new DecrementInt();

		unaryAfterDic[OperatorCode.Increment] = new IncrementAfterInt();
		unaryAfterDic[OperatorCode.Decrement] = new DecrementAfterInt();

		binaryIntIntDic[OperatorCode.Plus] = new PlusIntInt();
		binaryIntIntDic[OperatorCode.Minus] = new MinusIntInt();
		binaryIntIntDic[OperatorCode.Mult] = new MultIntInt();
		binaryIntIntDic[OperatorCode.Div] = new DivIntInt();
		binaryIntIntDic[OperatorCode.Mod] = new ModIntInt();
		binaryIntIntDic[OperatorCode.Equal] = new EqualIntInt();
		binaryIntIntDic[OperatorCode.Greater] = new GreaterIntInt();
		binaryIntIntDic[OperatorCode.Less] = new LessIntInt();
		binaryIntIntDic[OperatorCode.GreaterEqual] = new GreaterEqualIntInt();
		binaryIntIntDic[OperatorCode.LessEqual] = new LessEqualIntInt();
		binaryIntIntDic[OperatorCode.NotEqual] = new NotEqualIntInt();
		binaryIntIntDic[OperatorCode.And] = new AndIntInt();
		binaryIntIntDic[OperatorCode.Or] = new OrIntInt();
		binaryIntIntDic[OperatorCode.Xor] = new XorIntInt();
		binaryIntIntDic[OperatorCode.Nand] = new NandIntInt();
		binaryIntIntDic[OperatorCode.Nor] = new NorIntInt();
		binaryIntIntDic[OperatorCode.BitAnd] = new BitAndIntInt();
		binaryIntIntDic[OperatorCode.BitOr] = new BitOrIntInt();
		binaryIntIntDic[OperatorCode.BitXor] = new BitXorIntInt();
		binaryIntIntDic[OperatorCode.RightShift] = new RightShiftIntInt();
		binaryIntIntDic[OperatorCode.LeftShift] = new LeftShiftIntInt();

		binaryStrStrDic[OperatorCode.Plus] = new PlusStrStr();
		binaryStrStrDic[OperatorCode.Equal] = new EqualStrStr();
		binaryStrStrDic[OperatorCode.Greater] = new GreaterStrStr();
		binaryStrStrDic[OperatorCode.Less] = new LessStrStr();
		binaryStrStrDic[OperatorCode.GreaterEqual] = new GreaterEqualStrStr();
		binaryStrStrDic[OperatorCode.LessEqual] = new LessEqualStrStr();
		binaryStrStrDic[OperatorCode.NotEqual] = new NotEqualStrStr();

		binaryFloatFloatDic[OperatorCode.Plus] = new PlusFloatFloat();
		binaryFloatFloatDic[OperatorCode.Minus] = new MinusFloatFloat();
		binaryFloatFloatDic[OperatorCode.Mult] = new MultFloatFloat();
		binaryFloatFloatDic[OperatorCode.Div] = new DivFloatFloat();
		binaryFloatFloatDic[OperatorCode.Equal] = new EqualFloatFloat();
		binaryFloatFloatDic[OperatorCode.NotEqual] = new NotEqualFloatFloat();
		binaryFloatFloatDic[OperatorCode.Less] = new LessFloatFloat();
		binaryFloatFloatDic[OperatorCode.Greater] = new GreaterFloatFloat();
		binaryFloatFloatDic[OperatorCode.LessEqual] = new LessEqualFloatFloat();
		binaryFloatFloatDic[OperatorCode.GreaterEqual] = new GreaterEqualFloatFloat();

		var mixedPlus = new PlusMixedFloat();
		var mixedMinus = new MinusMixedFloat();
		var mixedMult = new MultMixedFloat();
		var mixedDiv = new DivMixedFloat();
		var mixedEqual = new EqualMixedFloat();
		var mixedNotEqual = new NotEqualMixedFloat();
		var mixedLess = new LessMixedFloat();
		var mixedGreater = new GreaterMixedFloat();
		var mixedLessEqual = new LessEqualMixedFloat();
		var mixedGreaterEqual = new GreaterEqualMixedFloat();
		binaryMixedFloatDic = new Dictionary<OperatorCode, OperatorMethod>
		{
			[OperatorCode.Plus] = mixedPlus,
			[OperatorCode.Minus] = mixedMinus,
			[OperatorCode.Mult] = mixedMult,
			[OperatorCode.Div] = mixedDiv,
			[OperatorCode.Equal] = mixedEqual,
			[OperatorCode.NotEqual] = mixedNotEqual,
			[OperatorCode.Less] = mixedLess,
			[OperatorCode.Greater] = mixedGreater,
			[OperatorCode.LessEqual] = mixedLessEqual,
			[OperatorCode.GreaterEqual] = mixedGreaterEqual,
		};

		unaryDic[OperatorCode.Plus] = new PlusInt();
		unaryDic[OperatorCode.Minus] = new MinusInt();

		binaryMultIntStr = new MultStrInt();
		ternaryIntIntInt = new TernaryIntIntInt();
		ternaryIntStrStr = new TernaryIntStrStr();
		ternaryIntFloatFloat = new TernaryIntFloatFloat();
	}



	public static AExpression ReduceUnaryTerm(OperatorCode op, AExpression o1)
	{
		OperatorMethod method = null;
		if (op == OperatorCode.Increment || op == OperatorCode.Decrement)
		{
			if (!(o1 is VariableTerm var))
				throw new CodeEE(trerror.IncrementNonVar.Text);
			if (var.Identifier.IsConst)
				throw new CodeEE(trerror.IncrementConst.Text);
		}
		if (o1.GetEraType() == EraType.Integer)
		{
			if (op == OperatorCode.Plus)
				return o1;
			if (unaryDic.TryGetValue(op, out OperatorMethod value))
				method = value;
		}
		else if (o1.GetEraType() == EraType.Float)
		{
			if (op == OperatorCode.Plus)
				return o1;
			if (op == OperatorCode.Minus)
				method = new MinusFloat();
		}
		if (method != null)
			return new FunctionMethodTerm(method, [o1]);
		string errMes;
		if (o1.GetEraType() == EraType.Integer)
			errMes = trerror.NumericType.Text;
		else if (o1.GetEraType() == EraType.String)
			errMes = trerror.StringType.Text;
		else if (o1.GetEraType() == EraType.Float)
			errMes = trerror.FloatType.Text;
		else
			errMes = trerror.UnknownType.Text;
		errMes = string.Format(trerror.CanNotAppliedUnaryOp.Text, errMes, OperatorManager.ToOperatorString(op));
		throw new CodeEE(errMes);
	}

	public static AExpression ReduceUnaryAfterTerm(OperatorCode op, AExpression o1)
	{
		OperatorMethod method = null;
		if (op == OperatorCode.Increment || op == OperatorCode.Decrement)
		{
			if (!(o1 is VariableTerm var))
				throw new CodeEE(trerror.IncrementNonVar.Text);
			if (var.Identifier.IsConst)
				throw new CodeEE(trerror.IncrementConst.Text);
		}
		if (o1.GetEraType() == EraType.Integer)
		{
			if (unaryAfterDic.TryGetValue(op, out OperatorMethod value))
				method = value;
		}
		if (method != null)
			return new FunctionMethodTerm(method, [o1]);
		string errMes;
		if (o1.GetEraType() == EraType.Integer)
			errMes = trerror.NumericType.Text;
		else if (o1.GetEraType() == EraType.String)
			errMes = trerror.StringType.Text;
		else
			errMes = trerror.UnknownType.Text;
		errMes = string.Format(trerror.CanNotAppliedUnaryOp.Text, errMes, OperatorManager.ToOperatorString(op));
		throw new CodeEE(errMes);
	}

	public static AExpression ReduceBinaryTerm(OperatorCode op, AExpression left, AExpression right)
	{
		OperatorMethod method = null;
		var lType = left.GetEraType();
		var rType = right.GetEraType();
		if (lType == EraType.Integer && rType == EraType.Integer)
		{
			if (binaryIntIntDic.TryGetValue(op, out OperatorMethod value))
				method = value;
		}
		else if (lType == EraType.String && rType == EraType.String)
		{
			if (binaryStrStrDic.TryGetValue(op, out OperatorMethod value))
				method = value;
		}
		else if (lType == EraType.Integer && rType == EraType.String
			 || lType == EraType.String && rType == EraType.Integer)
		{
			if (op == OperatorCode.Mult)
				method = binaryMultIntStr;
		}
		else if (lType == EraType.Float && rType == EraType.Float)
		{
			if (binaryFloatFloatDic.TryGetValue(op, out OperatorMethod value))
				method = value;
		}
		else if (lType == EraType.Integer && rType == EraType.Float
			 || lType == EraType.Float && rType == EraType.Integer)
		{
			if (binaryMixedFloatDic.TryGetValue(op, out OperatorMethod value))
				method = value;
		}
		if (method != null)
			return new FunctionMethodTerm(method, [left, right]);
		string typeName1, typeName2, errMes;
		if (lType == EraType.Integer)
			typeName1 = trerror.NumericType.Text;
		else if (lType == EraType.String)
			typeName1 = trerror.StringType.Text;
		else if (lType == EraType.Float)
			typeName1 = trerror.FloatType.Text;
		else
			typeName1 = trerror.UnknownType.Text;
		if (rType == EraType.Integer)
			typeName2 = trerror.NumericType.Text;
		else if (rType == EraType.String)
			typeName2 = trerror.StringType.Text;
		else if (rType == EraType.Float)
			typeName2 = trerror.FloatType.Text;
		else
			typeName2 = trerror.UnknownType.Text;
		errMes = string.Format(trerror.CanNotAppliedBinaryOp.Text, typeName1, typeName2, OperatorManager.ToOperatorString(op));
		throw new CodeEE(errMes);
	}

	public static AExpression ReduceTernaryTerm(AExpression o1, AExpression o2, AExpression o3)
	{
		OperatorMethod method = null;
		var t1 = o1.GetEraType();
		var t2 = o2.GetEraType();
		var t3 = o3.GetEraType();
		if (t1 == EraType.Integer && t2 == EraType.Integer && t3 == EraType.Integer)
			method = ternaryIntIntInt;
		else if (t1 == EraType.Integer && t2 == EraType.String && t3 == EraType.String)
			method = ternaryIntStrStr;
		else if (t1 == EraType.Integer && t2 == EraType.Float && t3 == EraType.Float)
			method = ternaryIntFloatFloat;
		if (method != null)
			return new FunctionMethodTerm(method, [o1, o2, o3]);
		throw new CodeEE(trerror.InvalidTernaryOp.Text);

	}

	#region OperatorMethod SubClasses

	private sealed class PlusIntInt : OperatorMethod
	{
		public PlusIntInt()
		{
			CanRestructure = true;
			ReturnType = EraType.Integer;
		}

		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			return SafeArithmetic.SafeAdd(arguments[0].GetIntValue(exm), arguments[1].GetIntValue(exm));
		}
	}

	private sealed class PlusStrStr : OperatorMethod
	{
		public PlusStrStr()
		{
			CanRestructure = true;
			ReturnType = EraType.String;
			argumentTypeArray = [EraType.String, EraType.String];
		}

		public override string GetStrValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			return arguments[0].GetStrValue(exm) + arguments[1].GetStrValue(exm);
		}
	}

	private sealed class MinusIntInt : OperatorMethod
	{
		public MinusIntInt()
		{
			CanRestructure = true;
			ReturnType = EraType.Integer;
		}

		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			return SafeArithmetic.SafeSubtract(arguments[0].GetIntValue(exm), arguments[1].GetIntValue(exm));
		}
	}

	private sealed class MultIntInt : OperatorMethod
	{
		public MultIntInt()
		{
			CanRestructure = true;
			ReturnType = EraType.Integer;
		}

		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			return SafeArithmetic.SafeMultiply(arguments[0].GetIntValue(exm), arguments[1].GetIntValue(exm));
		}
	}

	private sealed class MultStrInt : OperatorMethod
	{
		public MultStrInt()
		{
			CanRestructure = true;
			ReturnType = EraType.String;
		}
		public override string GetStrValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			string str;
			long value;
			if (arguments[0].GetEraType() == EraType.Integer)
			{
				value = arguments[0].GetIntValue(exm);
				str = arguments[1].GetStrValue(exm);
			}
			else
			{
				str = arguments[0].GetStrValue(exm);
				value = arguments[1].GetIntValue(exm);
			}
			if (value < 0)
				throw new CodeEE(string.Format(trerror.MultiplyNegativeToStr.Text, value.ToString()));
			if (value >= 10000)
				throw new CodeEE(string.Format(trerror.Multiply10kToStr.Text, value.ToString()));
			if (string.IsNullOrEmpty(str) || value == 0)
				return "";
			StringBuilder builder = new()
			{
				Capacity = str.Length * (int)value
			};
			for (int i = 0; i < value; i++)
			{
				builder.Append(str);
			}
			return builder.ToString();
		}
	}

	private sealed class DivIntInt : OperatorMethod
	{
		public DivIntInt()
		{
			CanRestructure = true;
			ReturnType = EraType.Integer;
		}

		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			return SafeArithmetic.SafeDivide(arguments[0].GetIntValue(exm), arguments[1].GetIntValue(exm));
		}
	}

	private sealed class ModIntInt : OperatorMethod
	{
		public ModIntInt()
		{
			CanRestructure = true;
			ReturnType = EraType.Integer;
		}

		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			return SafeArithmetic.SafeModulo(arguments[0].GetIntValue(exm), arguments[1].GetIntValue(exm));
		}
	}


	private sealed class EqualIntInt : OperatorMethod
	{
		public EqualIntInt()
		{
			CanRestructure = true;
			ReturnType = EraType.Integer;
		}

		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			if (arguments[0].GetIntValue(exm) == arguments[1].GetIntValue(exm))
				return 1L;
			return 0L;
		}

	}

	private sealed class EqualStrStr : OperatorMethod
	{
		public EqualStrStr()
		{
			CanRestructure = true;
			ReturnType = EraType.Integer;
		}

		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			if (arguments[0].GetStrValue(exm) == arguments[1].GetStrValue(exm))
				return 1L;
			return 0L;
		}
	}

	private sealed class NotEqualIntInt : OperatorMethod
	{
		public NotEqualIntInt()
		{
			CanRestructure = true;
			ReturnType = EraType.Integer;
		}

		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			if (arguments[0].GetIntValue(exm) != arguments[1].GetIntValue(exm))
				return 1L;
			return 0L;
		}
	}

	private sealed class NotEqualStrStr : OperatorMethod
	{
		public NotEqualStrStr()
		{
			CanRestructure = true;
			ReturnType = EraType.Integer;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			if (arguments[0].GetStrValue(exm) != arguments[1].GetStrValue(exm))
				return 1L;
			return 0L;
		}

	}

	private sealed class GreaterIntInt : OperatorMethod
	{
		public GreaterIntInt()
		{
			CanRestructure = true;
			ReturnType = EraType.Integer;
		}

		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			if (arguments[0].GetIntValue(exm) > arguments[1].GetIntValue(exm))
				return 1L;
			return 0L;
		}
	}

	private sealed class GreaterStrStr : OperatorMethod
	{
		public GreaterStrStr()
		{
			CanRestructure = true;
			ReturnType = EraType.Integer;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			int c = string.Compare(arguments[0].GetStrValue(exm), arguments[1].GetStrValue(exm), Config.Config.SCExpression);
			if (c > 0)
				return 1L;
			return 0L;
		}
	}
	private sealed class LessIntInt : OperatorMethod
	{
		public LessIntInt()
		{
			CanRestructure = true;
			ReturnType = EraType.Integer;
		}

		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			if (arguments[0].GetIntValue(exm) < arguments[1].GetIntValue(exm))
				return 1L;
			return 0L;
		}
	}
	private sealed class LessStrStr : OperatorMethod
	{
		public LessStrStr()
		{
			CanRestructure = true;
			ReturnType = EraType.Integer;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			int c = string.Compare(arguments[0].GetStrValue(exm), arguments[1].GetStrValue(exm), Config.Config.SCExpression);
			if (c < 0)
				return 1L;
			return 0L;
		}

	}

	private sealed class GreaterEqualIntInt : OperatorMethod
	{
		public GreaterEqualIntInt()
		{
			CanRestructure = true;
			ReturnType = EraType.Integer;
		}

		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			if (arguments[0].GetIntValue(exm) >= arguments[1].GetIntValue(exm))
				return 1L;
			return 0L;
		}
	}

	private sealed class GreaterEqualStrStr : OperatorMethod
	{
		public GreaterEqualStrStr()
		{
			CanRestructure = true;
			ReturnType = EraType.Integer;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			int c = string.Compare(arguments[0].GetStrValue(exm), arguments[1].GetStrValue(exm), Config.Config.SCExpression);
			if (c >= 0)
				return 1L;
			return 0L;
		}
	}
	private sealed class LessEqualIntInt : OperatorMethod
	{
		public LessEqualIntInt()
		{
			CanRestructure = true;
			ReturnType = EraType.Integer;
		}

		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			if (arguments[0].GetIntValue(exm) <= arguments[1].GetIntValue(exm))
				return 1L;
			return 0L;
		}

	}
	private sealed class LessEqualStrStr : OperatorMethod
	{
		public LessEqualStrStr()
		{
			CanRestructure = true;
			ReturnType = EraType.Integer;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			int c = string.Compare(arguments[0].GetStrValue(exm), arguments[1].GetStrValue(exm), Config.Config.SCExpression);
			if (c <= 0)
				return 1L;
			return 0L;
		}
	}

	private sealed class AndIntInt : OperatorMethod
	{
		public AndIntInt()
		{
			CanRestructure = true;
			ReturnType = EraType.Integer;
		}

		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			if (arguments[0].GetIntValue(exm) != 0 && arguments[1].GetIntValue(exm) != 0)
				return 1L;
			return 0L;
		}

	}

	private sealed class OrIntInt : OperatorMethod
	{
		public OrIntInt()
		{
			CanRestructure = true;
			ReturnType = EraType.Integer;
		}

		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			if (arguments[0].GetIntValue(exm) != 0 || arguments[1].GetIntValue(exm) != 0)
				return 1L;
			return 0L;
		}
	}

	private sealed class XorIntInt : OperatorMethod
	{
		public XorIntInt()
		{
			CanRestructure = true;
			ReturnType = EraType.Integer;
		}

		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			long i1 = arguments[0].GetIntValue(exm);
			long i2 = arguments[1].GetIntValue(exm);
			if (i1 == 0 && i2 != 0 || i1 != 0 && i2 == 0)
				return 1L;
			return 0L;
		}

	}

	private sealed class NandIntInt : OperatorMethod
	{
		public NandIntInt()
		{
			CanRestructure = true;
			ReturnType = EraType.Integer;
		}

		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			if (arguments[0].GetIntValue(exm) == 0 || arguments[1].GetIntValue(exm) == 0)
				return 1L;
			return 0L;
		}

	}

	private sealed class NorIntInt : OperatorMethod
	{
		public NorIntInt()
		{
			CanRestructure = true;
			ReturnType = EraType.Integer;
		}

		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			if (arguments[0].GetIntValue(exm) == 0 && arguments[1].GetIntValue(exm) == 0)
				return 1L;
			return 0L;
		}
	}

	private sealed class BitAndIntInt : OperatorMethod
	{
		public BitAndIntInt()
		{
			CanRestructure = true;
			ReturnType = EraType.Integer;
		}

		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			return arguments[0].GetIntValue(exm) & arguments[1].GetIntValue(exm);
		}
	}

	private sealed class BitOrIntInt : OperatorMethod
	{
		public BitOrIntInt()
		{
			CanRestructure = true;
			ReturnType = EraType.Integer;
		}

		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			return arguments[0].GetIntValue(exm) | arguments[1].GetIntValue(exm);
		}
	}

	private sealed class BitXorIntInt : OperatorMethod
	{
		public BitXorIntInt()
		{
			CanRestructure = true;
			ReturnType = EraType.Integer;
		}

		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			return arguments[0].GetIntValue(exm) ^ arguments[1].GetIntValue(exm);
		}
	}

	private sealed class RightShiftIntInt : OperatorMethod
	{
		public RightShiftIntInt()
		{
			CanRestructure = true;
			ReturnType = EraType.Integer;
		}

		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			return arguments[0].GetIntValue(exm) >> (int)arguments[1].GetIntValue(exm);
		}
	}

	private sealed class LeftShiftIntInt : OperatorMethod
	{
		public LeftShiftIntInt()
		{
			CanRestructure = true;
			ReturnType = EraType.Integer;
		}

		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			return arguments[0].GetIntValue(exm) << (int)arguments[1].GetIntValue(exm);
		}
	}

	private sealed class PlusInt : OperatorMethod
	{
		public PlusInt()
		{
			CanRestructure = true;
			ReturnType = EraType.Integer;
		}

		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			return arguments[0].GetIntValue(exm);
		}
	}

	private sealed class MinusInt : OperatorMethod
	{
		public MinusInt()
		{
			CanRestructure = true;
			ReturnType = EraType.Integer;
		}

		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			return SafeArithmetic.SafeNegate(arguments[0].GetIntValue(exm));
		}
	}

	private sealed class NotInt : OperatorMethod
	{
		public NotInt()
		{
			CanRestructure = true;
			ReturnType = EraType.Integer;
		}

		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			if (arguments[0].GetIntValue(exm) == 0)
				return 1L;
			return 0L;
		}
	}
	private sealed class BitNotInt : OperatorMethod
	{
		public BitNotInt()
		{
			CanRestructure = true;
			ReturnType = EraType.Integer;
		}

		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			return ~arguments[0].GetIntValue(exm);
		}
	}

	private sealed class IncrementInt : OperatorMethod
	{
		public IncrementInt()
		{
			CanRestructure = false;
			ReturnType = EraType.Integer;
		}

		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			VariableTerm var = (VariableTerm)arguments[0];
			try
			{
				return var.ChangeValue(1L, exm);
			}
			catch (OverflowException)
			{
				GlobalStatic.EMediator.Console.PrintWarning(
					"整数溢出: ++操作", default(ScriptPosition), 1);
				return long.MaxValue;
			}
		}
	}
	private sealed class DecrementInt : OperatorMethod
	{
		public DecrementInt()
		{
			CanRestructure = false;
			ReturnType = EraType.Integer;
		}

		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			VariableTerm var = (VariableTerm)arguments[0];
			try
			{
				return var.ChangeValue(-1L, exm);
			}
			catch (OverflowException)
			{
				GlobalStatic.EMediator.Console.PrintWarning(
					"整数溢出: --操作", default(ScriptPosition), 1);
				return long.MinValue;
			}
		}
	}
	private sealed class IncrementAfterInt : OperatorMethod
	{
		public IncrementAfterInt()
		{
			CanRestructure = false;
			ReturnType = EraType.Integer;
		}

		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			VariableTerm var = (VariableTerm)arguments[0];
			try
			{
				return var.ChangeValue(1L, exm) - 1;
			}
			catch (OverflowException)
			{
				GlobalStatic.EMediator.Console.PrintWarning(
					"整数溢出: ++操作(後置)", default(ScriptPosition), 1);
				return long.MaxValue - 1;
			}
		}
	}

	private sealed class DecrementAfterInt : OperatorMethod
	{
		public DecrementAfterInt()
		{
			CanRestructure = false;
			ReturnType = EraType.Integer;
		}

		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			VariableTerm var = (VariableTerm)arguments[0];
			try
			{
				return var.ChangeValue(-1L, exm) + 1;
			}
			catch (OverflowException)
			{
				GlobalStatic.EMediator.Console.PrintWarning(
					"整数溢出: --操作(後置)", default(ScriptPosition), 1);
				return long.MinValue + 1;
			}
		}
	}


	private sealed class TernaryIntIntInt : OperatorMethod
	{
		public TernaryIntIntInt()
		{
			CanRestructure = true;
			ReturnType = EraType.Integer;
		}

		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			return arguments[0].GetIntValue(exm) != 0 ? arguments[1].GetIntValue(exm) : arguments[2].GetIntValue(exm);
		}
	}

	private sealed class TernaryIntStrStr : OperatorMethod
	{
		public TernaryIntStrStr()
		{
			CanRestructure = true;
			ReturnType = EraType.String;
		}

		public override string GetStrValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			return arguments[0].GetIntValue(exm) != 0 ? arguments[1].GetStrValue(exm) : arguments[2].GetStrValue(exm);
		}
	}

	private sealed class TernaryIntFloatFloat : OperatorMethod
	{
		public TernaryIntFloatFloat()
		{
			CanRestructure = true;
			ReturnType = EraType.Float;
		}

		public override double GetFloatValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			return arguments[0].GetIntValue(exm) != 0 ? arguments[1].GetFloatValue(exm) : arguments[2].GetFloatValue(exm);
		}
	}

	private sealed class PlusFloatFloat : OperatorMethod
	{
		public PlusFloatFloat()
		{
			CanRestructure = true;
			ReturnType = EraType.Float;
		}

		public override double GetFloatValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			return arguments[0].GetFloatValue(exm) + arguments[1].GetFloatValue(exm);
		}
	}

	private sealed class MinusFloatFloat : OperatorMethod
	{
		public MinusFloatFloat()
		{
			CanRestructure = true;
			ReturnType = EraType.Float;
		}

		public override double GetFloatValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			return arguments[0].GetFloatValue(exm) - arguments[1].GetFloatValue(exm);
		}
	}

	private sealed class MultFloatFloat : OperatorMethod
	{
		public MultFloatFloat()
		{
			CanRestructure = true;
			ReturnType = EraType.Float;
		}

		public override double GetFloatValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			return arguments[0].GetFloatValue(exm) * arguments[1].GetFloatValue(exm);
		}
	}

	private sealed class DivFloatFloat : OperatorMethod
	{
		public DivFloatFloat()
		{
			CanRestructure = true;
			ReturnType = EraType.Float;
		}

		public override double GetFloatValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			double divisor = arguments[1].GetFloatValue(exm);
			if (divisor == 0.0)
				throw new CodeEE(trerror.DivideByZero.Text);
			return arguments[0].GetFloatValue(exm) / divisor;
		}
	}

	private sealed class EqualFloatFloat : OperatorMethod
	{
		public EqualFloatFloat()
		{
			CanRestructure = true;
			ReturnType = EraType.Integer;
		}

		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			return arguments[0].GetFloatValue(exm) == arguments[1].GetFloatValue(exm) ? 1L : 0L;
		}
	}

	private sealed class NotEqualFloatFloat : OperatorMethod
	{
		public NotEqualFloatFloat()
		{
			CanRestructure = true;
			ReturnType = EraType.Integer;
		}

		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			return arguments[0].GetFloatValue(exm) != arguments[1].GetFloatValue(exm) ? 1L : 0L;
		}
	}

	private sealed class LessFloatFloat : OperatorMethod
	{
		public LessFloatFloat()
		{
			CanRestructure = true;
			ReturnType = EraType.Integer;
		}

		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			return arguments[0].GetFloatValue(exm) < arguments[1].GetFloatValue(exm) ? 1L : 0L;
		}
	}

	private sealed class GreaterFloatFloat : OperatorMethod
	{
		public GreaterFloatFloat()
		{
			CanRestructure = true;
			ReturnType = EraType.Integer;
		}

		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			return arguments[0].GetFloatValue(exm) > arguments[1].GetFloatValue(exm) ? 1L : 0L;
		}
	}

	private sealed class LessEqualFloatFloat : OperatorMethod
	{
		public LessEqualFloatFloat()
		{
			CanRestructure = true;
			ReturnType = EraType.Integer;
		}

		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			return arguments[0].GetFloatValue(exm) <= arguments[1].GetFloatValue(exm) ? 1L : 0L;
		}
	}

	private sealed class GreaterEqualFloatFloat : OperatorMethod
	{
		public GreaterEqualFloatFloat()
		{
			CanRestructure = true;
			ReturnType = EraType.Integer;
		}

		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			return arguments[0].GetFloatValue(exm) >= arguments[1].GetFloatValue(exm) ? 1L : 0L;
		}
	}

	private sealed class PlusMixedFloat : OperatorMethod
	{
		public PlusMixedFloat()
		{
			CanRestructure = true;
			ReturnType = EraType.Float;
		}

		public override double GetFloatValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			double a = arguments[0].GetEraType() == EraType.Integer ? arguments[0].GetIntValue(exm) : arguments[0].GetFloatValue(exm);
			double b = arguments[1].GetEraType() == EraType.Integer ? arguments[1].GetIntValue(exm) : arguments[1].GetFloatValue(exm);
			return a + b;
		}
	}

	private sealed class MinusMixedFloat : OperatorMethod
	{
		public MinusMixedFloat()
		{
			CanRestructure = true;
			ReturnType = EraType.Float;
		}

		public override double GetFloatValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			double a = arguments[0].GetEraType() == EraType.Integer ? arguments[0].GetIntValue(exm) : arguments[0].GetFloatValue(exm);
			double b = arguments[1].GetEraType() == EraType.Integer ? arguments[1].GetIntValue(exm) : arguments[1].GetFloatValue(exm);
			return a - b;
		}
	}

	private sealed class MultMixedFloat : OperatorMethod
	{
		public MultMixedFloat()
		{
			CanRestructure = true;
			ReturnType = EraType.Float;
		}

		public override double GetFloatValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			double a = arguments[0].GetEraType() == EraType.Integer ? arguments[0].GetIntValue(exm) : arguments[0].GetFloatValue(exm);
			double b = arguments[1].GetEraType() == EraType.Integer ? arguments[1].GetIntValue(exm) : arguments[1].GetFloatValue(exm);
			return a * b;
		}
	}

	private sealed class DivMixedFloat : OperatorMethod
	{
		public DivMixedFloat()
		{
			CanRestructure = true;
			ReturnType = EraType.Float;
		}

		public override double GetFloatValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			double a = arguments[0].GetEraType() == EraType.Integer ? arguments[0].GetIntValue(exm) : arguments[0].GetFloatValue(exm);
			double b = arguments[1].GetEraType() == EraType.Integer ? arguments[1].GetIntValue(exm) : arguments[1].GetFloatValue(exm);
			if (b == 0.0)
				throw new CodeEE(trerror.DivideByZero.Text);
			return a / b;
		}
	}

	private sealed class EqualMixedFloat : OperatorMethod
	{
		public EqualMixedFloat()
		{
			CanRestructure = true;
			ReturnType = EraType.Integer;
		}

		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			double a = arguments[0].GetEraType() == EraType.Integer ? arguments[0].GetIntValue(exm) : arguments[0].GetFloatValue(exm);
			double b = arguments[1].GetEraType() == EraType.Integer ? arguments[1].GetIntValue(exm) : arguments[1].GetFloatValue(exm);
			return a == b ? 1L : 0L;
		}
	}

	private sealed class NotEqualMixedFloat : OperatorMethod
	{
		public NotEqualMixedFloat()
		{
			CanRestructure = true;
			ReturnType = EraType.Integer;
		}

		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			double a = arguments[0].GetEraType() == EraType.Integer ? arguments[0].GetIntValue(exm) : arguments[0].GetFloatValue(exm);
			double b = arguments[1].GetEraType() == EraType.Integer ? arguments[1].GetIntValue(exm) : arguments[1].GetFloatValue(exm);
			return a != b ? 1L : 0L;
		}
	}

	private sealed class LessMixedFloat : OperatorMethod
	{
		public LessMixedFloat()
		{
			CanRestructure = true;
			ReturnType = EraType.Integer;
		}

		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			double a = arguments[0].GetEraType() == EraType.Integer ? arguments[0].GetIntValue(exm) : arguments[0].GetFloatValue(exm);
			double b = arguments[1].GetEraType() == EraType.Integer ? arguments[1].GetIntValue(exm) : arguments[1].GetFloatValue(exm);
			return a < b ? 1L : 0L;
		}
	}

	private sealed class GreaterMixedFloat : OperatorMethod
	{
		public GreaterMixedFloat()
		{
			CanRestructure = true;
			ReturnType = EraType.Integer;
		}

		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			double a = arguments[0].GetEraType() == EraType.Integer ? arguments[0].GetIntValue(exm) : arguments[0].GetFloatValue(exm);
			double b = arguments[1].GetEraType() == EraType.Integer ? arguments[1].GetIntValue(exm) : arguments[1].GetFloatValue(exm);
			return a > b ? 1L : 0L;
		}
	}

	private sealed class LessEqualMixedFloat : OperatorMethod
	{
		public LessEqualMixedFloat()
		{
			CanRestructure = true;
			ReturnType = EraType.Integer;
		}

		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			double a = arguments[0].GetEraType() == EraType.Integer ? arguments[0].GetIntValue(exm) : arguments[0].GetFloatValue(exm);
			double b = arguments[1].GetEraType() == EraType.Integer ? arguments[1].GetIntValue(exm) : arguments[1].GetFloatValue(exm);
			return a <= b ? 1L : 0L;
		}
	}

	private sealed class GreaterEqualMixedFloat : OperatorMethod
	{
		public GreaterEqualMixedFloat()
		{
			CanRestructure = true;
			ReturnType = EraType.Integer;
		}

		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			double a = arguments[0].GetEraType() == EraType.Integer ? arguments[0].GetIntValue(exm) : arguments[0].GetFloatValue(exm);
			double b = arguments[1].GetEraType() == EraType.Integer ? arguments[1].GetIntValue(exm) : arguments[1].GetFloatValue(exm);
			return a >= b ? 1L : 0L;
		}
	}

	private sealed class MinusFloat : OperatorMethod
	{
		public MinusFloat()
		{
			CanRestructure = true;
			ReturnType = EraType.Float;
		}

		public override double GetFloatValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			return -arguments[0].GetFloatValue(exm);
		}
	}

	#endregion
}
