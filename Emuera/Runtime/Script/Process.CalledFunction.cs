using MinorShift.Emuera.GameData.Function;
using MinorShift.Emuera.GameData.Variable;
using MinorShift.Emuera.GameProc;
using MinorShift.Emuera.Runtime.Config;
using MinorShift.Emuera.Runtime.Script.Data;
using MinorShift.Emuera.Runtime.Script.Statements;
using MinorShift.Emuera.Runtime.Script.Statements.Expression;
using MinorShift.Emuera.Runtime.Script.Statements.Function;
using MinorShift.Emuera.Runtime.Script.Statements.Variable;
using MinorShift.Emuera.Runtime.Utils;
using System;
using System.Collections.Generic;
using trerror = MinorShift.Emuera.Runtime.Utils.EvilMask.Lang.Error;

namespace MinorShift.Emuera.Runtime.Script;

internal sealed class UserDefinedFunctionArgument
{
	public UserDefinedFunctionArgument(AExpression[] srcArgs, VariableTerm[] destArgs)
	{
		Arguments = srcArgs;
		TransporterInt = new long[Arguments.Length];
		TransporterStr = new string[Arguments.Length];
		TransporterFloat = new double[Arguments.Length];
		TransporterRef = new object[Arguments.Length];
		TransporterElementRef = new ElementRefInfo[Arguments.Length];
		isRef = new bool[Arguments.Length];
		refDestDimension = new int[Arguments.Length];
		for (int i = 0; i < Arguments.Length; i++)
		{
			isRef[i] = destArgs[i].Identifier.IsReference;
			refDestDimension[i] = destArgs[i].Identifier.Dimension;
		}
	}
	public readonly AExpression[] Arguments;
	public readonly long[] TransporterInt;
	public readonly string[] TransporterStr;
	public readonly double[] TransporterFloat;
	public readonly object[] TransporterRef;
	public readonly ElementRefInfo[] TransporterElementRef;
	public readonly bool[] isRef;
	public readonly int[] refDestDimension;
	public void SetTransporter(ExpressionMediator exm)
	{
		for (int i = 0; i < Arguments.Length; i++)
		{
			if (Arguments[i] == null)
				continue;
			if (isRef[i])
			{
				VariableTerm vTerm = (VariableTerm)Arguments[i];
				if (vTerm.Identifier is NullRefTerm)
				{
					continue;
				}
				
				if (refDestDimension[i] > 0)
				{
					if (vTerm.Identifier.IsCharacterData)
					{
						long charaNo = vTerm.GetElementInt(0, exm);
						if (charaNo < 0 || charaNo >= GlobalStatic.VariableData.CharacterList.Count)
							throw new CodeEE(string.Format(trerror.OoRCharaVarArg.Text, vTerm.Identifier.Name, "1", charaNo.ToString()));
						TransporterRef[i] = vTerm.Identifier.GetArrayChara((int)charaNo);
					}
					else if (vTerm.Identifier is ReferenceToken refToken)
					{
						if (refToken.IsOut && refToken.IsNullRef)
							continue;
						TransporterRef[i] = refToken.GetArray();
					}
					else
					{
						TransporterRef[i] = vTerm.Identifier.GetArray();
					}
				}
				else
				{
					if (vTerm.Identifier is ReferenceToken refToken && refToken.HasElementRef)
					{
						TransporterElementRef[i] = refToken.GetElementRef();
					}
					else if (vTerm.Identifier is ReferenceToken refTokenOut && refTokenOut.IsOut && refTokenOut.IsNullRef)
					{
						continue;
					}
					else
					{
						long[] indices = new long[vTerm.Identifier.Dimension];
						for (int d = 0; d < vTerm.Identifier.Dimension; d++)
						{
							if (vTerm is FixedVariableTerm || d < vTerm.ArgumentCount)
								indices[d] = vTerm.GetElementInt(d, exm);
							else
								indices[d] = 0;
						}
						TransporterElementRef[i] = new ElementRefInfo(vTerm.Identifier, indices);
					}
				}
			}
			else if (Arguments[i] is VariadicArgTerm)
				continue;
			else if (Arguments[i].GetEraType() == EraType.Integer)
				TransporterInt[i] = Arguments[i].GetIntValue(exm);
			else if (Arguments[i].GetEraType() == EraType.Float)
				TransporterFloat[i] = Arguments[i].GetFloatValue(exm);
			else
				TransporterStr[i] = Arguments[i].GetStrValue(exm);
		}
	}
	public UserDefinedFunctionArgument Restructure(ExpressionMediator exm)
	{
		for (int i = 0; i < Arguments.Length; i++)
		{
			if (Arguments[i] == null)
				continue;
			if (isRef[i])
				Arguments[i].Restructure(exm);
			else
				Arguments[i] = Arguments[i].Restructure(exm);
		}
		return this;
	}
}

/// <summary>
/// 現在呼び出し中の関数
/// イベント関数を除いて実行中に内部状態は変化しないので使いまわしても良い
/// </summary>
internal sealed class CalledFunction
{
	private CalledFunction(string label) { FunctionName = label; }
	public static CalledFunction CallEventFunction(Process parent, string label, LogicalLine retAddress)
	{
		CalledFunction called = new(label)
		{
			//List<FunctionLabelLine> newLabelList = new List<FunctionLabelLine>();
			Finished = false,
			eventLabelList = parent.LabelDictionary.GetEventLabels(label)
		};
		if (called.eventLabelList == null)
		{
			FunctionLabelLine line = parent.LabelDictionary.GetNonEventLabel(label);
			if (parent.LabelDictionary.GetNonEventLabel(label) != null)
			{
				throw new CodeEE(string.Format(trerror.CalleventToNonEventFunc.Text, label, line.Position.Value.Filename, line.Position.Value.LineNo));
			}
			return null;
		}
		called.counter = -1;
		called.group = 0;
		called.ShiftNext();
		called.TopLabel = called.CurrentLabel;
		called.returnAddress = retAddress;
		called.IsEvent = true;
		return called;
	}

	public static CalledFunction CallFunction(Process parent, string label, LogicalLine retAddress)
	{
		CalledFunction called = new(label)
		{
			Finished = false
		};
		FunctionLabelLine labelline = parent.LabelDictionary.GetNonEventLabel(label);
		
		// Lazy Loading Table에서 가져오기 시도
		if (labelline == null)
		{
			if (parent.TryLazyLoadErb(label))
				labelline = parent.LabelDictionary.GetNonEventLabel(label);
		}

		if (labelline == null)
		{
			if (parent.LabelDictionary.GetEventLabels(label) != null)
			{
				throw new CodeEE(string.Format(trerror.CallToEventFunc.Text, label, Config.Config.GetConfigName(ConfigCode.CompatiCallEvent)));
			}
			return null;
		}
		else if (labelline.IsMethod)
		{
			throw new CodeEE(string.Format(trerror.CallToUserFunc.Text, labelline.LabelName, labelline.Position.Value.Filename, labelline.Position.Value.LineNo.ToString()));
		}
		called.TopLabel = labelline;
		called.CurrentLabel = labelline;
		called.returnAddress = retAddress;
		called.IsEvent = false;
		return called;
	}

	public static CalledFunction CreateCalledFunctionMethod(FunctionLabelLine labelline, string label)
	{
		CalledFunction called = new(label)
		{
			TopLabel = labelline,
			CurrentLabel = labelline,
			returnAddress = null,
			IsEvent = false
		};
		return called;
	}


	static FunctionMethod tostrMethod;
	/// <summary>
	/// 1803beta005 予め引数の数を合わせて規定値を代入しておく
	/// 1806+v6.99 式中関数の引数に無効な#DIM変数を与えている場合に例外になるのを修正
	/// 1808beta009 REF型に対応
	/// </summary>
	public UserDefinedFunctionArgument ConvertArg(List<AExpression> srcArgs, out string errMes)
	{
		errMes = null;
		if (TopLabel.IsError)
		{
			errMes = TopLabel.ErrMes;
			return null;
		}
		FunctionLabelLine func = TopLabel;
		int variadicIndex = func.VariadicArgIndex;
		int fixedArgCount = variadicIndex >= 0 ? variadicIndex : func.Arg.Length;
		AExpression[] convertedArg = new AExpression[func.Arg.Length];
		AExpression term;
		VariableTerm destArg;
		for (int i = 0; i < fixedArgCount; i++)
		{
			term = i < srcArgs.Count ? srcArgs[i] : null;
			destArg = func.Arg[i];
			if (destArg.Identifier.IsReference)
			{
				if (term == null)
				{
					if (destArg.Identifier.IsOut)
					{
						term = new VariableTerm(new NullRefTerm(!destArg.Identifier.IsString, destArg.Identifier.IsFloat), []);
						convertedArg[i] = term;
						continue;
					}
					errMes = string.Format(trerror.CanNotOmitRefArg.Text, func.LabelName, (i + 1).ToString());
					return null;
				}
				VariableTerm vTerm = term as VariableTerm;
				if (vTerm == null)
				{
					errMes = string.Format(trerror.RequireArrayBecauseRefArg.Text, func.LabelName, (i + 1).ToString());
					return null;
				}
				if (destArg.Identifier.Dimension == 0)
				{
					if (!((ReferenceToken)destArg.Identifier).MatchType(vTerm.Identifier, true, true, out errMes))
					{
						errMes = string.Format(trerror.NumberOfArg.Text, func.LabelName, (i + 1).ToString(), errMes);
						return null;
					}
				}
				else
				{
					if (vTerm.Identifier.Dimension == 0)
					{
						errMes = string.Format(trerror.RequireArrayBecauseRefArg.Text, func.LabelName, (i + 1).ToString());
						return null;
					}
					if (!((ReferenceToken)destArg.Identifier).MatchType(vTerm.Identifier, false, false, out errMes))
					{
						errMes = string.Format(trerror.NumberOfArg.Text, func.LabelName, (i + 1).ToString(), errMes);
						return null;
					}
				}
			}
			else if (term == null)
			{
				term = func.Def[i];
				if (term == null && !Config.Config.CompatiFuncArgOptional)
				{
					errMes = string.Format(trerror.CanNotOmitArgWithMessage.Text, func.LabelName, (i + 1).ToString(), Config.Config.GetConfigName(ConfigCode.CompatiFuncArgOptional));
					return null;
				}
			}
			else if (term.GetOperandType() != destArg.GetOperandType())
			{
				if (term.GetEraType() == EraType.String)
				{
					errMes = string.Format(trerror.CanNotConvertStrToInt.Text, func.LabelName, (i + 1).ToString());
					return null;
				}
				else if (destArg.GetEraType() == EraType.Float && term.GetEraType() == EraType.Integer)
				{
				}
				else if (destArg.GetEraType() == EraType.Integer && term.GetEraType() == EraType.Float)
				{
					errMes = string.Format(trerror.CanNotConvertFloatToInt.Text, func.LabelName, (i + 1).ToString());
					return null;
				}
				else
				{
					if (!Config.Config.CompatiFuncArgAutoConvert)
					{
						errMes = string.Format(trerror.CanNotConvertIntToStr.Text, func.LabelName, (i + 1).ToString(), Config.Config.GetConfigName(ConfigCode.CompatiFuncArgAutoConvert));
						return null;
					}
					if (tostrMethod == null)
						tostrMethod = FunctionMethodCreator.GetMethodList()["TOSTR"];
					term = new FunctionMethodTerm(tostrMethod, [term]);
				}
			}
			convertedArg[i] = term;
		}

		if (variadicIndex >= 0)
		{
			destArg = func.Arg[variadicIndex];
			UserDifinedFunctionDataArgType variadicType;
			if (destArg.IsString)
				variadicType = UserDifinedFunctionDataArgType.Str;
			else if (destArg.GetEraType() == EraType.Float)
				variadicType = UserDifinedFunctionDataArgType.Float;
			else
				variadicType = UserDifinedFunctionDataArgType.Int;
			List<AExpression> variadicArgs = new List<AExpression>();
			for (int i = variadicIndex; i < srcArgs.Count; i++)
			{
				term = srcArgs[i];
				if (term.GetOperandType() != destArg.GetOperandType())
				{
					if (term.GetEraType() == EraType.String)
					{
						errMes = string.Format(trerror.CanNotConvertStrToInt.Text, func.LabelName, (i + 1).ToString());
						return null;
					}
					else if (destArg.GetEraType() == EraType.Float && term.GetEraType() == EraType.Integer)
					{
					}
					else if (destArg.GetEraType() == EraType.Integer && term.GetEraType() == EraType.Float)
					{
						errMes = string.Format(trerror.CanNotConvertFloatToInt.Text, func.LabelName, (i + 1).ToString());
						return null;
					}
					else
					{
						if (!Config.Config.CompatiFuncArgAutoConvert)
						{
							errMes = string.Format(trerror.CanNotConvertIntToStr.Text, func.LabelName, (i + 1).ToString(), Config.Config.GetConfigName(ConfigCode.CompatiFuncArgAutoConvert));
							return null;
						}
						if (tostrMethod == null)
							tostrMethod = FunctionMethodCreator.GetMethodList()["TOSTR"];
						term = new FunctionMethodTerm(tostrMethod, [term]);
					}
				}
				variadicArgs.Add(term);
			}
			convertedArg[variadicIndex] = new VariadicArgTerm(variadicArgs, variadicType);
		}

		return new UserDefinedFunctionArgument(convertedArg, func.Arg);
	}

	public LogicalLine CallLabel(Process parent, string label)
	{
		return parent.LabelDictionary.GetLabelDollar(label, CurrentLabel);
	}

	public void updateRetAddress(LogicalLine line)
	{
		returnAddress = line;
	}

	public CalledFunction Clone()
	{
		CalledFunction called = new(FunctionName)
		{
			eventLabelList = eventLabelList,
			CurrentLabel = CurrentLabel,
			TopLabel = TopLabel,
			group = group,
			IsEvent = IsEvent,

			counter = counter,
			returnAddress = returnAddress
		};
		return called;
	}

	List<FunctionLabelLine>[] eventLabelList;
	public FunctionLabelLine CurrentLabel { get; private set; }
	public FunctionLabelLine TopLabel { get; private set; }
	int counter = -1;
	int group;
	LogicalLine returnAddress;
	public readonly string FunctionName = "";
	public bool IsJump { get; set; }
	public bool Finished { get; private set; }
	public LogicalLine ReturnAddress
	{
		get { return returnAddress; }
	}
	public bool IsEvent { get; private set; }
	public int VariadicArgCount { get; set; }

	public bool HasSingleFlag
	{
		get
		{
			if (CurrentLabel == null)
				return false;
			return CurrentLabel.IsSingle;
		}
	}


	#region イベント関数専用
	public void ShiftNext()
	{
		while (true)
		{
			counter++;
			if (eventLabelList[group].Count > counter)
			{
				CurrentLabel = eventLabelList[group][counter];
				return;
			}
			group++;
			counter = -1;
			if (group >= 4)
			{
				CurrentLabel = null;
				return;
			}
		}
	}

	public void ShiftNextGroup()
	{
		counter = -1;
		group++;
		if (group >= 4)
		{
			CurrentLabel = null;
			return;
		}
		ShiftNext();
	}

	public void FinishEvent()
	{
		group = 4;
		counter = -1;
		CurrentLabel = null;
		return;
	}

	public bool IsOnly
	{
		get { return CurrentLabel.IsOnly; }
	}
	#endregion
}
