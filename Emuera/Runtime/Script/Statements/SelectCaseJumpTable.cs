using MinorShift.Emuera.Runtime.Script.Statements.Expression;
using System.Collections.Generic;
using trerror = MinorShift.Emuera.Runtime.Utils.EvilMask.Lang.Error;

namespace MinorShift.Emuera.Runtime.Script.Statements;

internal sealed class SelectCaseJumpTable
{
	private readonly Dictionary<string, InstructionLine> _strTable;
	private readonly Dictionary<long, InstructionLine> _intTable;
	private readonly Dictionary<double, InstructionLine> _floatTable;
	private InstructionLine _caseElseLine;
	private readonly InstructionLine _endSelectLine;
	private readonly EraType _type;

	private SelectCaseJumpTable(EraType type, InstructionLine endSelectLine)
	{
		_type = type;
		_endSelectLine = endSelectLine;
		_strTable = type == EraType.String ? new Dictionary<string, InstructionLine>() : null;
		_intTable = type == EraType.Integer ? new Dictionary<long, InstructionLine>() : null;
		_floatTable = type == EraType.Float ? new Dictionary<double, InstructionLine>() : null;
	}

	public static SelectCaseJumpTable TryBuild(InstructionLine selectLine, EraType selectType)
	{
		if (selectType != EraType.Integer && selectType != EraType.String && selectType != EraType.Float)
			return null;

		InstructionLine endSelectLine = selectLine.JumpTo as InstructionLine;
		var table = new SelectCaseJumpTable(selectType, endSelectLine);

		foreach (var caseLine in selectLine.IfCaseList)
		{
			if (caseLine.IsError)
				return null;
			if (caseLine.FunctionCode == FunctionCode.CASEELSE)
			{
				table._caseElseLine = caseLine;
				continue;
			}

			var caseArg = caseLine.Argument as CaseArgument;
			if (caseArg == null)
				return null;

			foreach (var caseExp in caseArg.CaseExps)
			{
				if (caseExp.CaseType != CaseExpressionType.Normal)
					return null;

				AExpression leftTerm = caseExp.LeftTerm;
				if (leftTerm == null)
					return null;
				if (!leftTerm.IsConst)
				{
					try
					{
						AExpression restructured = leftTerm.Restructure(null);
						if (restructured is SingleTerm st)
							leftTerm = st;
						else
							return null;
					}
					catch
					{
						return null;
					}
				}

				if (selectType == EraType.Integer)
				{
					long val = leftTerm.GetIntValue(null);
					if (table._intTable.TryGetValue(val, out var existingInt))
					{
						var prevPos = existingInt.Position;
						string prevCaseId = prevPos.HasValue ? $"{prevPos.Value.Filename}:{prevPos.Value.LineNo}" : "?";
						ParserMediator.Warn(string.Format(trerror.DuplicateCaseValue.Text, val, prevCaseId), caseLine, 1, false, false);
						continue;
					}
					table._intTable.Add(val, caseLine);
				}
				else if (selectType == EraType.String)
				{
					string val = leftTerm.GetStrValue(null);
					if (table._strTable.TryGetValue(val, out var existingStr))
					{
						var prevPos = existingStr.Position;
						string prevCaseId = prevPos.HasValue ? $"{prevPos.Value.Filename}:{prevPos.Value.LineNo}" : "?";
						ParserMediator.Warn(string.Format(trerror.DuplicateCaseValue.Text, val, prevCaseId), caseLine, 1, false, false);
						continue;
					}
					table._strTable.Add(val, caseLine);
				}
				else
				{
					double val = leftTerm.GetFloatValue(null);
					if (table._floatTable.ContainsKey(val))
						return null;
					table._floatTable.Add(val, caseLine);
				}
			}
		}

		return table;
	}

	public InstructionLine Lookup(long value)
	{
		if (_intTable.TryGetValue(value, out var line))
			return line;
		return _caseElseLine ?? _endSelectLine;
	}

	public InstructionLine Lookup(string value)
	{
		if (_strTable.TryGetValue(value, out var line))
			return line;
		return _caseElseLine ?? _endSelectLine;
	}

	public InstructionLine Lookup(double value)
	{
		if (_floatTable.TryGetValue(value, out var line))
			return line;
		return _caseElseLine ?? _endSelectLine;
	}
}