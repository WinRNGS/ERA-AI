using MinorShift.Emuera.GameData.Variable;
using MinorShift.Emuera.Runtime.Config;
using MinorShift.Emuera.Runtime.Script;
using MinorShift.Emuera.Runtime.Script.Data;
using MinorShift.Emuera.Runtime.Script.Parser;
using MinorShift.Emuera.Runtime.Script.Statements;
using MinorShift.Emuera.Runtime.Script.Statements.Expression;
using MinorShift.Emuera.Runtime.Script.Statements.Function;
using MinorShift.Emuera.Runtime.Script.Statements.Variable;
using MinorShift.Emuera.Runtime.Utils;
using MinorShift.Emuera.Runtime.Utils.EvilMask;
using MinorShift.Emuera.UI;
using MinorShift.Emuera.UI.Game;
using MinorShift.Emuera.UI.Game.Image;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Xml;
using trerror = MinorShift.Emuera.Runtime.Utils.EvilMask.Lang.Error;
// using MinorShift.Emuera.GameProc.Function;
namespace MinorShift.Emuera.GameData.Function;

internal static partial class FunctionMethodCreator
{
	#region EM_私家版_追加関数
	private sealed class HtmlStringLenMethod : FunctionMethod
	{
		public HtmlStringLenMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArrayEx = [
					new ArgTypeList{ ArgTypes = { ArgType.String, ArgType.Int}, OmitStart = 1 }
				];
			CanRestructure = true;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			int len = HtmlManager.HtmlLength(arguments[0].GetStrValue(exm));
			if (arguments.Count == 1 || arguments[1].GetIntValue(exm) == 0)
			{
				if (len >= 0)
					return 2 * len / Config.FontSize + ((2 * len % Config.FontSize != 0) ? 1 : 0);
				else
					return 2 * len / Config.FontSize - ((2 * len % Config.FontSize != 0) ? 1 : 0);
			}
			return len;
		}
	}
	private sealed class XmlGetMethod : FunctionMethod
	{
		public XmlGetMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArrayEx = [
					new ArgTypeList{ ArgTypes = { ArgType.Any, ArgType.String, ArgType.Int, ArgType.Int}, OmitStart = 2 },
					new ArgTypeList{ ArgTypes = { ArgType.Any, ArgType.String, ArgType.RefString1D, ArgType.Int}, OmitStart = 3 },
				];
			CanRestructure = false;
		}
		public XmlGetMethod(bool byname) : this()
		{
			byName = byname;
			argumentTypeArrayEx = [
					new ArgTypeList{ ArgTypes = { ArgType.Any, ArgType.String, ArgType.Int, ArgType.Int}, OmitStart = 2 },
					new ArgTypeList{ ArgTypes = { ArgType.Any, ArgType.String, ArgType.RefString1D, ArgType.Int}, OmitStart = 3 },
				];
		}
		private bool byName;
		private static void OutPutNode(XmlNode node, string[] array, int i, long style)
		{
			switch (style)
			{
				case 1: array[i] = node.InnerText; break;
				case 2: array[i] = node.InnerXml; break;
				case 3: array[i] = node.OuterXml; break;
				case 4: array[i] = node.Name; break;
				default: array[i] = node.Value; break;
			}
		}
		private static void OutPutNode(XmlNode node, SparseArray<string> array, int i, long style)
		{
			switch (style)
			{
				case 1: array[i] = node.InnerText; break;
				case 2: array[i] = node.InnerXml; break;
				case 3: array[i] = node.OuterXml; break;
				case 4: array[i] = node.Name; break;
				default: array[i] = node.Value; break;
			}
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			XmlDocument doc = null;
			XmlNodeList nodes = null;
			if (arguments[0].GetEraType() == EraType.Integer || (byName && arguments[0].GetEraType() == EraType.String))
			{
				var idx = arguments[0].GetEraType() == EraType.String ? arguments[0].GetStrValue(exm) : arguments[0].GetIntValue(exm).ToString();
				var dict = exm.VEvaluator.VariableData.DataXmlDocument;
				if (dict.TryGetValue(idx, out doc)) { }
				else return -1;
			}
			else
			{
				doc = new XmlDocument();
				var xml = arguments[0].GetStrValue(exm);
				try
				{
					doc.LoadXml(xml);
				}
				catch (XmlException e)
				{
					throw new CodeEE(string.Format(trerror.XmlGetError.Text, xml, e.Message));
				}
			}
			string path = arguments[1].GetStrValue(exm);
			try
			{
				nodes = doc.SelectNodes(path);
			}
			catch (System.Xml.XPath.XPathException e)
			{
				throw new CodeEE(string.Format(trerror.XmlGetPathError.Text, path, e.Message));
			}
			long outputStyle = arguments.Count == 4 ? arguments[3].GetIntValue(exm) : 0;

			if (arguments.Count >= 3)
			{
				if (arguments[2].GetEraType() == EraType.Integer && arguments[2].GetIntValue(exm) != 0)
				{
					for (int i = 0; i < Math.Min(nodes.Count, exm.VEvaluator.RESULTS_ARRAY.Length); i++)
						OutPutNode(nodes[i], exm.VEvaluator.RESULTS_ARRAY, i, outputStyle);
				}
				else
				{
					var arrObj = (arguments[2] as VariableTerm).Identifier.GetArray();
					if (arrObj is string[] arr)
					{
						for (int i = 0; i < Math.Min(nodes.Count, arr.Length); i++)
							OutPutNode(nodes[i], arr, i, outputStyle);
					}
					else
					{
						var sa = (SparseArray<string>)arrObj;
						for (int i = 0; i < Math.Min(nodes.Count, (int)sa.Length); i++)
							OutPutNode(nodes[i], sa, i, outputStyle);
					}
				}
			}
			return nodes.Count;
		}
	}
	private sealed class IsDefinedMethod : FunctionMethod
	{
		public IsDefinedMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = [EraType.String];
			CanRestructure = true;
		}

		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			return (GlobalStatic.IdentifierDictionary.GetMacro(arguments[0].GetStrValue(exm)) != null) ? 1 : 0;
		}
	}
	private sealed class EnumNameMethod : FunctionMethod
	{
		public enum EType
		{
			Function,
			Variable,
			Macro
		}
		public enum EAction
		{
			BeginsWith,
			EndsWith,
			With
		}
		private EType type;
		private EAction action;
		public EnumNameMethod(EType type, EAction act)
		{
			ReturnType = EraType.Integer;
			CanRestructure = false;
			argumentTypeArrayEx = [
					new ArgTypeList{ ArgTypes = { ArgType.String, ArgType.RefString1D }, OmitStart = 1 },
				];
			this.type = type;
			action = act;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			string arg = arguments[0].GetStrValue(exm).ToUpper(CultureInfo.InvariantCulture);
			string[] array = null;
			switch (type)
			{
				case EType.Function:
					array = GlobalStatic.Process.LabelDictionary.NoneventKeys;
					break;
				case EType.Variable:
					array = GlobalStatic.IdentifierDictionary.VarKeys;
					break;
				case EType.Macro:
					array = GlobalStatic.IdentifierDictionary.MacroKeys;
					break;
			}
			List<string> strs = [];
			if (arg.Length > 0)
				foreach (string item in array)
				{
					if (item.Length < arg.Length) continue;
					switch (action)
					{
						case EAction.BeginsWith:
							if (item.ToUpper(CultureInfo.InvariantCulture).IndexOf(arg, StringComparison.Ordinal) == 0) strs.Add(item);
							break;
						case EAction.EndsWith:
							if (item.ToUpper(CultureInfo.InvariantCulture).LastIndexOf(arg, StringComparison.Ordinal) == item.Length - arg.Length) strs.Add(item);
							break;
						case EAction.With:
							if (item.ToUpper(CultureInfo.InvariantCulture).IndexOf(arg, StringComparison.Ordinal) >= 0) strs.Add(item);
							break;
					}
				}
			// strs.Sort();
			string[] ret = strs.ToArray();
			int outputlength;
			if (arguments.Count == 2)
			{
				var arrObj = (arguments[1] as VariableTerm).Identifier.GetArray();
				if (arrObj is string[] output)
				{
					outputlength = Math.Min(output.Length, ret.Length);
					Array.Copy(ret, output, outputlength);
				}
				else
				{
					var sa = (SparseArray<string>)arrObj;
					outputlength = Math.Min((int)sa.Length, ret.Length);
					for (int i = 0; i < outputlength; i++)
						sa[i] = ret[i];
				}
			}
			else
			{
				var output = exm.VEvaluator.RESULTS_ARRAY;
				outputlength = Math.Min(output.Length, ret.Length);
				for (int i = 0; i < outputlength; i++)
					output[i] = ret[i];
			}
			return outputlength;
		}
	}
	private sealed class EnumFilesMethod : FunctionMethod
	{
		public EnumFilesMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArrayEx = [
					new ArgTypeList{ ArgTypes = { ArgType.String, ArgType.String, ArgType.Int, ArgType.RefString1D }, OmitStart = 1 },
				];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			var dir = Utils.GetValidPath(arguments[0].GetStrValue(exm));
			if (dir == null || !Directory.Exists(dir)) return -1;
			var pattern = arguments.Count > 1 ? arguments[1].GetStrValue(exm) : "*";
			var option = arguments.Count > 2
				? (arguments[2].GetIntValue(exm) == 0 ? SearchOption.TopDirectoryOnly : SearchOption.AllDirectories)
				: SearchOption.TopDirectoryOnly;
			string[] files;
			try
			{
				files = Directory.EnumerateFiles(dir, pattern, option).ToArray();
			}
			catch
			{
				return -1;
			}
			// Convert absolute paths to relative paths (relative to ExeDir)
			// so that LOADTEXT can accept them via GetValidPath
			// Aligned with upstream ee+em a4d3665 + 1c495b5
			for (int i = 0; i < files.Length; i++)
			{
				files[i] = Path.GetRelativePath(Program.ExeDir, files[i]);
			}
			int ret;
			if (arguments.Count == 4)
			{
				var arrObj = (arguments[3] as VariableTerm).Identifier.GetArray();
				if (arrObj is string[] output)
				{
					ret = Math.Min(files.Length, output.Length);
					Array.Copy(files, output, ret);
				}
				else
				{
					var sa = (SparseArray<string>)arrObj;
					ret = Math.Min(files.Length, (int)sa.Length);
					for (int i = 0; i < ret; i++)
						sa[i] = files[i];
				}
			}
			else
			{
				var output = exm.VEvaluator.RESULTS_ARRAY;
				ret = Math.Min(files.Length, output.Length);
				for (int i = 0; i < ret; i++)
					output[i] = files[i];
			}
			return ret;
		}
	}
	private sealed class GetVarMethod : FunctionMethod
	{
		public GetVarMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArrayEx = [
					new ArgTypeList{ ArgTypes = { ArgType.String, ArgType.Int }, OmitStart = 1 },
				];
			CanRestructure = false;
		}

		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			long defaultValue = arguments.Count > 1 && arguments[1] != null ? arguments[1].GetIntValue(exm) : 0;
			bool hasDefault = arguments.Count > 1 && arguments[1] != null;
			string name = arguments[0].GetStrValue(exm);

			WordCollection wc = LexicalAnalyzer.Analyse(new CharStream(name), LexEndWith.EoL, LexAnalyzeFlag.None);
			AExpression term = ExpressionParser.ReduceExpressionTerm(wc, TermEndWith.EoL);

			if (term is VariableTerm var)
			{
				if (var.Identifier == null)
					return hasDefault ? defaultValue : throw new CodeEE(string.Format(trerror.IsNotVar.Text, name));
				if (var.GetEraType() != EraType.Integer)
					return hasDefault ? defaultValue : throw new CodeEE(string.Format(trerror.IsNotInt.Text, name));
				return var.GetIntValue(exm);
			}
			return hasDefault ? defaultValue : throw new CodeEE(string.Format(trerror.IsNotVar.Text, name));
		}
	}
	private sealed class GetVarsMethod : FunctionMethod
	{
		public GetVarsMethod()
		{
			ReturnType = EraType.String;
			argumentTypeArrayEx = [
					new ArgTypeList{ ArgTypes = { ArgType.String, ArgType.String }, OmitStart = 1 },
				];
			CanRestructure = false;
		}
		public override string GetStrValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			string defaultValue = arguments.Count > 1 && arguments[1] != null ? arguments[1].GetStrValue(exm) : "";
			bool hasDefault = arguments.Count > 1 && arguments[1] != null;
			string name = arguments[0].GetStrValue(exm);

			WordCollection wc = LexicalAnalyzer.Analyse(new CharStream(name), LexEndWith.EoL, LexAnalyzeFlag.None);
			AExpression term = ExpressionParser.ReduceExpressionTerm(wc, TermEndWith.EoL);

			if (term is VariableTerm var)
			{
				if (var.Identifier == null)
					return hasDefault ? defaultValue : throw new CodeEE(string.Format(trerror.IsNotVar.Text, name));
				if (var.GetEraType() != EraType.String)
					return hasDefault ? defaultValue : throw new CodeEE(string.Format(trerror.IsNotStr.Text, name));
				return var.GetStrValue(exm);
			}
			return hasDefault ? defaultValue : throw new CodeEE(string.Format(trerror.IsNotVar.Text, name));
		}
	}
	private sealed class ExistVarMethod : FunctionMethod
	{
		public ExistVarMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArrayEx = [
					new ArgTypeList{ ArgTypes = { ArgType.String, ArgType.Int}, OmitStart = 1 },
				];
			CanRestructure = true;
		}

		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			VariableToken token = GlobalStatic.IdentifierDictionary.GetVariableToken(arguments[0].GetStrValue(exm), null, true);
			long mode = (arguments.Count > 1 && arguments[1] != null) ? arguments[1].GetIntValue(exm) : 0;
			if (mode == 0)
			{
				if (token != null)
				{
					long res = 0;
					switch (token.GetEraType())
					{
						case EraType.Integer: res |= 1; break;
						case EraType.String: res |= 2; break;
						case EraType.Float: res |= 32; break;
					}
					if (token.IsConst) res |= 4;
					if (token.IsArray2D) res |= 8;
					if (token.IsArray3D) res |= 16;
					return res;
				}
			}
			else
			{
				try
				{
					WordCollection temp_wc = LexicalAnalyzer.Analyse(new CharStream(arguments[0].GetStrValue(exm)), LexEndWith.EoL, LexAnalyzeFlag.None);
					AExpression temp_term = ExpressionParser.ReduceExpressionTerm(temp_wc, TermEndWith.EoL);
					return 1;
				}
				catch
				{
					return 0;
				}
			}
			return 0;
		}
	}
	private sealed class ArrayMultiSortExMethod : FunctionMethod
	{
		public ArrayMultiSortExMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArrayEx = [
					new ArgTypeList{ ArgTypes = { ArgType.String, ArgType.RefString1D, ArgType.Int, ArgType.Int | ArgType.DisallowVoid }, OmitStart = 2 },
					new ArgTypeList{ ArgTypes = { ArgType.RefInt1D, ArgType.RefString1D, ArgType.Int, ArgType.Int | ArgType.DisallowVoid }, OmitStart = 2 },
				];
			CanRestructure = false;
		}

		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			bool isAscending = arguments.Count < 3 || arguments[2] == null || arguments[2].GetIntValue(exm) != 0;
			long fixedLengthInput = arguments.Count < 4 ? -1 : arguments[3].GetIntValue(exm);
			if (fixedLengthInput == 0) return 0;
			if (fixedLengthInput < -1 || fixedLengthInput > int.MaxValue)
				throw new CodeEE($"[{Name}] fixedLength parameter must be between -1 and {int.MaxValue}");

			VariableTerm baseVar = arguments[0] is VariableTerm vt ? vt : GetConvertedTerm(exm, arguments[0].GetStrValue(exm));
			if (!baseVar.Identifier.IsArray1D)
				throw new CodeEE(string.Format(trerror.Not1DFuncArg.Text, Name, "1"));

			int[] sortedIndices = GetSortedIndices(baseVar, isAscending, (int)fixedLengthInput);

			var targetVarArrObj = (arguments[1] as VariableTerm).Identifier.GetArray();
			var targetVarNames = targetVarArrObj is SparseArray<string> sparseNames ? sparseNames.ToArray(sparseNames.Length) : (string[])targetVarArrObj;
			var targetTerms = targetVarNames.Select(name => GetConvertedTerm(exm, name)).ToList();

			foreach (var term in targetTerms)
			{
				if (!ApplySortToVariable(term, sortedIndices))
					return 0;
			}
			return 1;
		}

		private int[] GetSortedIndices(VariableTerm baseVar, bool ascending, int fixedLength)
		{
			switch (baseVar.GetEraType())
			{
				case EraType.Integer:
				{
					object arrObj = baseVar.Identifier.GetArray();
					long[] array = arrObj is SparseArray<long> sparse ? sparse.ToArray(sparse.Length) : (long[])arrObj;
					int length = fixedLength > 0 ? Math.Min(fixedLength, array.Length) : array.Length;
					if (fixedLength == -1)
					{
						for (int i = 0; i < length; i++)
						{
							if (array[i] == 0) { length = i; break; }
						}
					}
					var indices = Enumerable.Range(0, length).ToArray();
					if (ascending)
						Array.Sort(indices, (a, b) => array[a].CompareTo(array[b]));
					else
						Array.Sort(indices, (a, b) => array[b].CompareTo(array[a]));
					return indices;
				}
				case EraType.Float:
				{
					object arrObj = baseVar.Identifier.GetArray();
					double[] array = arrObj is SparseArray<double> sparse ? sparse.ToArray(sparse.Length) : (double[])arrObj;
					int length = fixedLength > 0 ? Math.Min(fixedLength, array.Length) : array.Length;
					if (fixedLength == -1)
					{
						for (int i = 0; i < length; i++)
						{
							if (array[i] == 0.0) { length = i; break; }
						}
					}
					var indices = Enumerable.Range(0, length).ToArray();
					if (ascending)
						Array.Sort(indices, (a, b) => array[a].CompareTo(array[b]));
					else
						Array.Sort(indices, (a, b) => array[b].CompareTo(array[a]));
					return indices;
				}
				default:
				{
					object arrObj = baseVar.Identifier.GetArray();
					string[] array = arrObj is SparseArray<string> sparse ? sparse.ToArray(sparse.Length) : (string[])arrObj;
					int length = fixedLength > 0 ? Math.Min(fixedLength, array.Length) : array.Length;
					if (fixedLength == -1)
					{
						for (int i = 0; i < length; i++)
						{
							if (string.IsNullOrEmpty(array[i])) { length = i; break; }
						}
					}
					var indices = Enumerable.Range(0, length).ToArray();
					if (ascending)
						Array.Sort(indices, (a, b) => string.Compare(array[a], array[b], StringComparison.Ordinal));
					else
						Array.Sort(indices, (a, b) => string.Compare(array[b], array[a], StringComparison.Ordinal));
					return indices;
				}
			}
		}

		private bool ApplySortToVariable(VariableTerm term, int[] indices)
		{
			object arrObj = term.Identifier.GetArray();
			if (arrObj is Array array)
			{
				int dim = array.Rank;
				if (dim < 1 || dim > 3)
					throw new ExeEE(trerror.AbnormalArray.Text);
				if (array.GetLength(0) < indices.Length) return false;

				Array clone = (Array)array.Clone();
				for (int i = 0; i < indices.Length; i++)
				{
					int src = indices[i];
					if (dim == 1)
						array.SetValue(clone.GetValue(src), i);
					else if (dim == 2)
					{
						for (int x = 0; x < array.GetLength(1); x++)
							array.SetValue(clone.GetValue(src, x), i, x);
					}
					else
					{
						for (int x = 0; x < array.GetLength(1); x++)
							for (int y = 0; y < array.GetLength(2); y++)
								array.SetValue(clone.GetValue(src, x, y), i, x, y);
					}
				}
			}
			else if (arrObj is SparseArray<long> saLong)
			{
				if ((int)saLong.Length < indices.Length) return false;
				var clone = saLong.ToArray(saLong.Length);
				for (int i = 0; i < indices.Length; i++)
					saLong[i] = clone[indices[i]];
			}
			else if (arrObj is SparseArray<string> saStr)
			{
				if ((int)saStr.Length < indices.Length) return false;
				var clone = saStr.ToArray(saStr.Length);
				for (int i = 0; i < indices.Length; i++)
					saStr[i] = clone[indices[i]];
			}
			return true;
		}

		private VariableTerm GetConvertedTerm(ExpressionMediator exm, string name)
		{
			WordCollection wc = LexicalAnalyzer.Analyse(new CharStream(name), LexEndWith.EoL, LexAnalyzeFlag.None);
			var term = ExpressionParser.ReduceExpressionTerm(wc, TermEndWith.EoL);
			var err = CheckVariableTerm(term, name);
			if (err != null) throw new CodeEE(err);
			return term as VariableTerm;
		}

		private string CheckVariableTerm(AExpression arg, string vname)
		{
			if (!(arg is VariableTerm varTerm) || varTerm.Identifier.IsCalc || varTerm.Identifier.IsConst)
				return string.Format(trerror.NotVarFunc.Text, Name, vname);
			if (varTerm.Identifier.IsCharacterData)
				return string.Format(trerror.IsCharaVarFunc.Text, Name, vname);
			if (!varTerm.Identifier.IsArray1D && !varTerm.Identifier.IsArray2D && !varTerm.Identifier.IsArray3D)
				return string.Format(trerror.NotDimVarFunc.Text, Name, vname);
			return null;
		}

		public override bool UniqueRestructure(ExpressionMediator exm, List<AExpression> arguments)
		{
			for (int i = 0; i < arguments.Count; i++)
				arguments[i] = arguments[i].Restructure(exm);
			return false;
		}
	}
	private sealed class SetVarMethod : FunctionMethod
	{
		public SetVarMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArrayEx = [
					new ArgTypeList{ ArgTypes = { ArgType.String, ArgType.Any, ArgType.Int }, OmitStart = 2 },
				];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			long defaultValue = arguments.Count > 2 && arguments[2] != null ? arguments[2].GetIntValue(exm) : 0;
			bool hasDefault = arguments.Count > 2 && arguments[2] != null;
			string name = arguments[0].GetStrValue(exm);

			WordCollection wc = LexicalAnalyzer.Analyse(new CharStream(name), LexEndWith.EoL, LexAnalyzeFlag.None);
			AExpression term = ExpressionParser.ReduceExpressionTerm(wc, TermEndWith.EoL);

			if (term is VariableTerm var)
			{
				if (var.Identifier == null || var.Identifier.IsConst)
					return hasDefault ? defaultValue : throw new CodeEE(string.Format(trerror.IsNotVar.Text, name));
				switch (var.GetEraType())
				{
					case EraType.String:
						if (arguments[1].GetEraType() != EraType.String)
							return hasDefault ? defaultValue : throw new CodeEE(string.Format(trerror.IsNotInt.Text, name));
						var.SetValue(arguments[1].GetStrValue(exm), exm);
						break;
					case EraType.Float:
						if (arguments[1].GetEraType() == EraType.Integer)
							var.SetValue((double)arguments[1].GetIntValue(exm), exm);
						else if (arguments[1].GetEraType() == EraType.Float)
							var.SetValue(arguments[1].GetFloatValue(exm), exm);
						else
							return hasDefault ? defaultValue : throw new CodeEE(string.Format(trerror.IsNotStr.Text, name));
						break;
					default:
						if (arguments[1].GetEraType() != EraType.Integer)
							return hasDefault ? defaultValue : throw new CodeEE(string.Format(trerror.IsNotStr.Text, name));
						var.SetValue(arguments[1].GetIntValue(exm), exm);
						break;
				}
				return 1;
			}
			return hasDefault ? defaultValue : throw new CodeEE(string.Format(trerror.IsNotVar.Text, name));
		}
	}
	private sealed class VarSetExMethod : FunctionMethod
	{
		public VarSetExMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArrayEx = [
					new ArgTypeList{ ArgTypes = { ArgType.String, ArgType.Any, ArgType.Int, ArgType.Int, ArgType.Int }, OmitStart = 2 },
				];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			string name = arguments[0].GetStrValue(exm);
			WordCollection wc = LexicalAnalyzer.Analyse(new CharStream(arguments[0].GetStrValue(exm)), LexEndWith.EoL, LexAnalyzeFlag.None);
			AExpression term = ExpressionParser.ReduceExpressionTerm(wc, TermEndWith.EoL);

			if (term is VariableTerm var)
			{
				if (var.Identifier == null || var.Identifier.IsConst)
					throw new CodeEE(string.Format(trerror.IsNotVar.Text, name));

				int start = (int)(arguments.Count >= 4 ? arguments[3].GetIntValue(exm) : 0);
				int end = (int)(arguments.Count == 5 ? arguments[4].GetIntValue(exm)
					: (var.Identifier.IsArray1D ? var.Identifier.GetLength()
					: (var.Identifier.IsArray2D ? var.Identifier.GetLength(1)
					: (var.Identifier.IsArray2D ? var.Identifier.GetLength(2) : 0))));
				bool setAllDims = arguments.Count >= 3 ? arguments[2].GetIntValue(exm) != 0 : true;
				switch (var.GetEraType())
				{
					case EraType.String:
					{
						var val = string.Empty;
						if (arguments.Count > 1 && arguments[1].GetEraType() != EraType.String)
							throw new CodeEE(string.Format(trerror.SetStrToInt.Text, name));
						if (arguments.Count > 1)
							val = arguments[1].GetStrValue(exm);
						if (var.Identifier.IsArray1D)
							var.Identifier.SetValueAll(val, start, end, 0);
						else if (var.Identifier.IsArray2D)
						{
							var array = var.Identifier.GetArray() as string[,];
							var idx1 = var.GetElementInt(0, exm);
							var idx2 = var.GetElementInt(1, exm);
							for (int i = Math.Max(start, (int)idx2); i < end; i++)
								array[idx1, i] = val;
						}
						if (var.Identifier.IsArray3D)
						{
							var idx1 = var.GetElementInt(0, exm);
							var idx2 = var.GetElementInt(1, exm);
							var idx3 = var.GetElementInt(2, exm);
							var array = var.Identifier.GetArray() as string[,,];
							for (int i = Math.Max(start, (int)idx3); i < end; i++)
								array[idx2, idx1, i] = val;
						}
						break;
					}
					case EraType.Float:
					{
						double val = 0.0;
						if (arguments.Count > 1)
						{
							if (arguments[1].GetEraType() == EraType.Integer)
								val = (double)arguments[1].GetIntValue(exm);
							else if (arguments[1].GetEraType() == EraType.Float)
								val = arguments[1].GetFloatValue(exm);
							else
								throw new CodeEE(string.Format(trerror.SetStrToFloat.Text, name));
						}
						if (var.Identifier.IsArray1D)
							var.Identifier.SetValueAll(val, start, end, 0);
						else if (var.Identifier.IsArray2D)
						{
							var array = var.Identifier.GetArray() as double[,];
							var idx1 = var.GetElementInt(0, exm);
							var idx2 = var.GetElementInt(1, exm);
							if (setAllDims)
							{
								for (int j = 0; j < array.GetLength(0); j++)
									for (int i = Math.Max(start, (int)idx2); i < end; i++)
										array[j, i] = val;
							}
							else
							{
								for (int i = Math.Max(start, (int)idx2); i < end; i++)
									array[idx1, i] = val;
							}
						}
						if (var.Identifier.IsArray3D)
						{
							var idx1 = var.GetElementInt(0, exm);
							var idx2 = var.GetElementInt(1, exm);
							var idx3 = var.GetElementInt(2, exm);
							var array = var.Identifier.GetArray() as double[,,];
							if (setAllDims)
							{
								for (int k = 0; k < array.GetLength(0); k++)
									for (int j = 0; j < array.GetLength(1); j++)
										for (int i = Math.Max(start, (int)idx3); i < end; i++)
											array[k, j, i] = val;
							}
							else
							{
								for (int i = Math.Max(start, (int)idx3); i < end; i++)
									array[idx2, idx1, i] = val;
							}
						}
						break;
					}
					default:
					{
						long val = 0;
						if (arguments.Count > 1 && arguments[1].GetEraType() != EraType.Integer)
						{
							if (arguments[1].GetEraType() == EraType.Float)
								throw new CodeEE(string.Format(trerror.SetFloatToInt.Text, name));
							else
								throw new CodeEE(string.Format(trerror.SetStrToInt.Text, name));
						}
						if (arguments.Count > 1)
							val = arguments[1].GetIntValue(exm);
						if (var.Identifier.IsArray1D)
							var.Identifier.SetValueAll(val, start, end, 0);
						else if (var.Identifier.IsArray2D)
						{
							var array = var.Identifier.GetArray() as long[,];
							var idx1 = var.GetElementInt(0, exm);
							var idx2 = var.GetElementInt(1, exm);
							if (setAllDims)
							{
								for (int j = 0; j < array.GetLength(0); j++)
									for (int i = Math.Max(start, (int)idx2); i < end; i++)
										array[j, i] = val;
							}
							else
							{
								for (int i = Math.Max(start, (int)idx2); i < end; i++)
									array[idx1, i] = val;
							}
						}
						if (var.Identifier.IsArray3D)
						{
							var idx1 = var.GetElementInt(0, exm);
							var idx2 = var.GetElementInt(1, exm);
							var idx3 = var.GetElementInt(2, exm);
							var array = var.Identifier.GetArray() as long[,,];
							if (setAllDims)
							{
								for (int k = 0; k < array.GetLength(0); k++)
									for (int j = 0; j < array.GetLength(1); j++)
										for (int i = Math.Max(start, (int)idx3); i < end; i++)
											array[k, j, i] = val;
							}
							else
							{
								for (int i = Math.Max(start, (int)idx3); i < end; i++)
									array[idx2, idx1, i] = val;
							}
						}
						break;
					}
				}
				return 1;
			}
			else
				throw new CodeEE(string.Format(trerror.IsNotVar.Text, name));
		}
	}
	private sealed class HtmlSubStringMethod : FunctionMethod
	{
		public HtmlSubStringMethod()
		{
			ReturnType = EraType.String;
			argumentTypeArray = [EraType.String, EraType.Integer];
			CanRestructure = false;
		}

		public override string GetStrValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			string str = arguments[0].GetStrValue(exm);
			string[] strs = HtmlManager.HtmlSubString(str, (int)arguments[1].GetIntValue(exm));
			var output = GlobalStatic.Process.VEvaluator.RESULTS_ARRAY;
			int outputlength = Math.Min(output.Length, strs.Length);
			for (int i = 0; i < outputlength; i++)
				output[i] = strs[i];
			return output[0];
		}
	}
	private sealed class HtmlStringLinesMethod : FunctionMethod
	{
		public HtmlStringLinesMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = [EraType.String, EraType.Integer];
			CanRestructure = false;
		}

		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			string str = arguments[0].GetStrValue(exm);
			if (string.IsNullOrEmpty(str)) return 0;
			var ret = 0;
			do
			{
				string[] strs = HtmlManager.HtmlSubString(str, (int)arguments[1].GetIntValue(exm));
				str = strs[1];
				ret++;
			} while (!string.IsNullOrEmpty(str));
			return ret;
		}
	}
	private sealed class RegexpMatchMethod : FunctionMethod
	{
		public RegexpMatchMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArrayEx = [
					new ArgTypeList{ ArgTypes = { ArgType.String, ArgType.String, ArgType.Int }, OmitStart = 2 },
					new ArgTypeList{ ArgTypes = { ArgType.String, ArgType.String, ArgType.RefInt, ArgType.RefString1D } },
				];
			CanRestructure = false;
		}

		static void Output(MatchCollection matches, Regex reg, string[] values)
		{
			var idx = 0;
			foreach (Match match in matches)
				foreach (var name in reg.GetGroupNames())
				{
					if (idx >= values.Length) return;
					values[idx] = match.Groups[name].Value;
					idx++;
				}
		}
		static void Output(MatchCollection matches, Regex reg, SparseArray<string> values)
		{
			var idx = 0;
			foreach (Match match in matches)
				foreach (var name in reg.GetGroupNames())
				{
					if (idx >= values.Length) return;
					values[idx] = match.Groups[name].Value;
					idx++;
				}
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			string baseString = arguments[0].GetStrValue(exm);
			Regex reg;
			try
			{
				reg = RegexFactory.GetRegex(arguments[1].GetStrValue(exm));
			}
			catch (ArgumentException e)
			{
				throw new CodeEE(string.Format(trerror.InvalidRegexArg.Text, Name, 2, e.Message));
			}
			var matches = reg.Matches(baseString);
			var ret = matches.Count;
			if (arguments.Count == 3 && arguments[2].GetIntValue(exm) != 0)
			{
				exm.VEvaluator.RESULT_ARRAY[1] = reg.GetGroupNumbers().Length;
				if (ret > 0) Output(matches, reg, exm.VEvaluator.RESULTS_ARRAY);
			}
			if (arguments.Count == 4)
			{
				(arguments[2] as VariableTerm).SetValue(reg.GetGroupNumbers().Length, exm);
				if (ret > 0)
				{
					var arrObj = (arguments[3] as VariableTerm).Identifier.GetArray();
					if (arrObj is string[] strArr)
						Output(matches, reg, strArr);
					else
						Output(matches, reg, (SparseArray<string>)arrObj);
				}
			}
			return ret;
		}
	}
	private sealed class XmlDocumentMethod : FunctionMethod
	{
		public enum Operation { Create, Check, Release };
		public XmlDocumentMethod(Operation type)
		{
			op = type;
			ReturnType = EraType.Integer;
			if (op == Operation.Create)
				argumentTypeArrayEx = [
						new ArgTypeList{ ArgTypes = { ArgType.Any, ArgType.String } },
					];
			else
				argumentTypeArrayEx = [
						new ArgTypeList{ ArgTypes = { ArgType.Any } },
					];
			CanRestructure = false;
		}
		private Operation op;
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			string idx = arguments[0].GetEraType() == EraType.String ? arguments[0].GetStrValue(exm) : arguments[0].GetIntValue(exm).ToString();
			var xmlDict = exm.VEvaluator.VariableData.DataXmlDocument;
			if (op == Operation.Create)
			{
				string xml = arguments[1].GetStrValue(exm);
				if (xmlDict.ContainsKey(idx))
				{
					return 0;
				}
				XmlDocument doc = new();
				try
				{
					doc.LoadXml(xml);
				}
				catch (XmlException e)
				{
					throw new CodeEE(string.Format(trerror.XmlGetError.Text, xml, e.Message));
				}
				xmlDict.Add(idx, doc);
			}
			else
			{
				if (xmlDict.ContainsKey(idx))
				{
					if (op == Operation.Check) return 1;
					xmlDict.Remove(idx);
				}
				else return 0;
			}
			return 1;
		}
	}
	private sealed class XmlSetMethod : FunctionMethod
	{
		public XmlSetMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArrayEx = [
					new ArgTypeList{ ArgTypes = { ArgType.Int, ArgType.String, ArgType.String, ArgType.Int, ArgType.Int }, OmitStart = 3 },
					new ArgTypeList{ ArgTypes = { ArgType.RefString, ArgType.String, ArgType.String, ArgType.Int, ArgType.Int }, OmitStart = 3 },
				];
			CanRestructure = false;
		}
		public XmlSetMethod(bool byname) : this()
		{
			byName = byname;
			argumentTypeArrayEx = [
					new ArgTypeList{ ArgTypes = { ArgType.String, ArgType.String, ArgType.String, ArgType.Int, ArgType.Int }, OmitStart = 3 },
				];
		}
		private bool byName;
		private static void SetNode(XmlNode node, string val, long style)
		{
			switch (style)
			{
				case 1: node.InnerText = val; break;
				case 2: node.InnerXml = val; break;
				default: node.Value = val; break;
			}
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			XmlDocument doc;
			bool saveToArg0 = true;
			if (arguments[0].GetEraType() == EraType.Integer || (byName && arguments[0].GetEraType() == EraType.String))
			{
				saveToArg0 = false;
				var idx = arguments[0].GetEraType() == EraType.String ? arguments[0].GetStrValue(exm) : arguments[0].GetIntValue(exm).ToString();
				var dict = exm.VEvaluator.VariableData.DataXmlDocument;
				if (dict.TryGetValue(idx, out doc)) { }
				else return -1;
			}
			else
			{
				string xml = arguments[0].GetStrValue(exm);
				doc = new XmlDocument();
				try
				{
					doc.LoadXml(xml);
				}
				catch (XmlException e)
				{
					throw new CodeEE(string.Format(trerror.XmlParseError.Text, Name, xml, e.Message));
				}
			}

			string path = arguments[1].GetStrValue(exm);
			XmlNodeList nodes = null;
			try
			{
				nodes = doc.SelectNodes(path);
			}
			catch (System.Xml.XPath.XPathException e)
			{
				throw new CodeEE(string.Format(trerror.XmlXPathParseError.Text, Name, path, e.Message));
			}
			bool setAllNodes = arguments.Count >= 4 ? arguments[3].GetIntValue(exm) != 0 : false;
			var style = arguments.Count == 5 ? arguments[4].GetIntValue(exm) : 0;
			if (style > 2 || style < 0) style = 0;
			var val = arguments[2].GetStrValue(exm);
			if (nodes.Count > 0)
			{
				if (nodes.Count != 1)
				{
					if (setAllNodes)
						for (int i = 0; i < nodes.Count; i++) SetNode(nodes[i], val, style);
				}
				else SetNode(nodes[0], val, style);
				if (saveToArg0)
				{
					(arguments[0] as VariableTerm).SetValue(doc.OuterXml, exm);
				}
			}
			return nodes.Count;
		}
	}
	private sealed class XmlToStrMethod : FunctionMethod
	{
		public XmlToStrMethod()
		{
			ReturnType = EraType.String;
			argumentTypeArrayEx = [
					new ArgTypeList{ ArgTypes = { ArgType.Any } },
				];
			CanRestructure = false;
		}
		public override string GetStrValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			string idx = arguments[0].GetEraType() == EraType.String ? arguments[0].GetStrValue(exm) : arguments[0].GetIntValue(exm).ToString();
			var xmlDict = exm.VEvaluator.VariableData.DataXmlDocument;
			if (!xmlDict.TryGetValue(idx, out var xmlVal)) return string.Empty;
			return xmlVal.OuterXml;
		}
	}
	private sealed class XmlAddNodeMethod : FunctionMethod
	{
		public enum Operation { Node, Attribute };
		private Operation op;
		private bool byName;

		public XmlAddNodeMethod(Operation op)
		{
			ReturnType = EraType.Integer;
			this.op = op;
			CanRestructure = false;

			if (op == Operation.Node)
				argumentTypeArrayEx = [
						new ArgTypeList{ ArgTypes = { ArgType.Int, ArgType.String, ArgType.String, ArgType.Int, ArgType.Int }, OmitStart = 3 },
						new ArgTypeList{ ArgTypes = { ArgType.RefString, ArgType.String, ArgType.String, ArgType.Int, ArgType.Int }, OmitStart = 3 }
					];
			else
				argumentTypeArrayEx = [
						new ArgTypeList{ ArgTypes = { ArgType.Int, ArgType.String, ArgType.String, ArgType.String, ArgType.Int, ArgType.Int }, OmitStart = 3 },
						new ArgTypeList{ ArgTypes = { ArgType.RefString, ArgType.String, ArgType.String, ArgType.String, ArgType.Int, ArgType.Int }, OmitStart = 3 }
					];
		}

		public XmlAddNodeMethod(Operation op, bool byname) : this(op)
		{
			byName = byname;
			if (op == Operation.Node)
				argumentTypeArrayEx = [
						new ArgTypeList{ ArgTypes = { ArgType.String, ArgType.String, ArgType.String, ArgType.Int, ArgType.Int }, OmitStart = 3 },
					];
			else
				argumentTypeArrayEx = [
						new ArgTypeList{ ArgTypes = { ArgType.String, ArgType.String, ArgType.String, ArgType.String, ArgType.Int, ArgType.Int }, OmitStart = 3 },
					];
		}

		bool Insert(XmlNode targetNode, XmlNode newChild, int method)
		{
			if (op == Operation.Node)
			{
				switch (method)
				{
					case 0: // Append
						targetNode.AppendChild(newChild); 
						break;
					case 1: // InsertBefore
						if (targetNode.ParentNode == null) return false;
						targetNode.ParentNode.InsertBefore(newChild, targetNode);
						break;
					case 2: // InsertAfter
						if (targetNode.ParentNode == null) return false;
						targetNode.ParentNode.InsertAfter(newChild, targetNode);
						break;
					default: return false;
				}
				return true;
			}
			else // Operation.Attribute
			{
				if (newChild is XmlAttribute newAttr)
				{
					// 如果 method > 0，目标必须是属性节点，因为要插在属性前后
					if (method > 0 && !(targetNode is XmlAttribute)) return false;
					
					switch (method)
					{
						case 0: // Append to Element
							if (targetNode is XmlElement elem)
								elem.Attributes.Append(newAttr);
							else
								return false; // 无法给非Element节点追加属性
							break;
						case 1: // InsertBefore Attribute
							if (targetNode is XmlAttribute attrBefore)
								attrBefore.OwnerElement.Attributes.InsertBefore(newAttr, attrBefore);
							break;
						case 2: // InsertAfter Attribute
							if (targetNode is XmlAttribute attrAfter)
								attrAfter.OwnerElement.Attributes.InsertAfter(newAttr, attrAfter);
							break;
						default: return false;
					}
					return true;
				}
			}
			return false;
		}

		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			XmlDocument doc;
			
			// 1. 解析参数位置
			// Node模式: XML, XPath, NewNodeXml, Method, SetAll
			// Attr模式: XML, XPath, Name, Value, Method, SetAll
			int methodPos = op == Operation.Node ? 4 : 5;
			int setAllPos = op == Operation.Node ? 5 : 6;

			int method = arguments.Count >= methodPos ? (int)arguments[methodPos - 1].GetIntValue(exm) : 0;
			if (method > 2 || method < 0) method = 0;
			
			bool setAllNodes = arguments.Count == setAllPos ? arguments[setAllPos - 1].GetIntValue(exm) != 0 : false;

			// 2. 加载 XML 文档
			bool saveToArg0 = true;
			if (arguments[0].GetEraType() == EraType.Integer || (byName && arguments[0].GetEraType() == EraType.String))
			{
				saveToArg0 = false;
				var idx = arguments[0].GetEraType() == EraType.String ? arguments[0].GetStrValue(exm) : arguments[0].GetIntValue(exm).ToString();
				var dict = exm.VEvaluator.VariableData.DataXmlDocument;
				if (dict.TryGetValue(idx, out doc)) { }
				else return -1;
			}
			else
			{
				string xmlStr = arguments[0].GetStrValue(exm);
				doc = new XmlDocument();
				try
				{
					doc.LoadXml(xmlStr);
				}
				catch (XmlException e)
				{
					throw new CodeEE(string.Format(trerror.XmlParseError.Text, Name, xmlStr, e.Message));
				}
			}

			// 3. 搜索目标节点
			string path = arguments[1].GetStrValue(exm);
			XmlNodeList nodes;
			try
			{
				nodes = doc.SelectNodes(path);
			}
			catch (System.Xml.XPath.XPathException e)
			{
				throw new CodeEE(string.Format(trerror.XmlXPathParseError.Text, Name, path, e.Message));
			}

			// 4. 执行添加逻辑
			if (nodes.Count > 0)
			{
				// 4.1 准备源数据（避免在循环中重复解析字符串）
				XmlNode sourceNodeForCopy = null; // 用于 Node 模式
				string attrName = null;           // 用于 Attribute 模式
				string attrValue = null;          // 用于 Attribute 模式

				if (op == Operation.Node)
				{
					var childNodeDoc = new XmlDocument();
					var xmlContent = arguments[2].GetStrValue(exm);
					try
					{
						childNodeDoc.LoadXml(xmlContent);
					}
					catch (XmlException e)
					{
						throw new CodeEE(string.Format(trerror.XmlParseError.Text, Name, xmlContent, e.Message));
					}
					sourceNodeForCopy = childNodeDoc.DocumentElement;
				}
				else
				{
					attrName = arguments[2].GetStrValue(exm);
					if (arguments.Count >= 4) attrValue = arguments[3].GetStrValue(exm);
				}

				// 定义一个本地函数来获取新的子节点副本
				// 核心修复点：每次调用都生成一个新的 XmlNode/XmlAttribute 对象
				XmlNode GetNewChild()
				{
					if (op == Operation.Node)
					{
						// ImportNode(..., true) 会深拷贝并正确处理 OwnerDocument
						return doc.ImportNode(sourceNodeForCopy, true);
					}
					else
					{
						var attr = doc.CreateAttribute(attrName);
						if (attrValue != null) attr.Value = attrValue;
						return attr;
					}
				}

				// 4.2 循环或单次执行
				if (nodes.Count == 1)
				{
					// 单个节点，直接执行
					// 如果 method > 0 但插入失败（例如试图给 Attribute 插入子节点），返回 0
					if (!Insert(nodes[0], GetNewChild(), method) && method > 0) return 0;
				}
				else
				{
					// 多个节点
					if (setAllNodes)
					{
						// 核心修复点：循环调用 GetNewChild()，保证每个目标节点都得到一个独立的新对象
						for (int i = 0; i < nodes.Count; i++)
						{
							Insert(nodes[i], GetNewChild(), method);
						}
					}
					else
					{
						// setAllNodes 为 0 且匹配多个时，什么都不做（符合文档描述）
					}
				}

				// 5. 如果是字符串模式，回写结果
				if (saveToArg0)
				{
					(arguments[0] as VariableTerm).SetValue(doc.OuterXml, exm);
				}
			}

			return nodes.Count;
		}
	}
	private sealed class XmlRemoveNodeMethod : FunctionMethod
	{
		public enum Operation { Node, Attribute };
		public XmlRemoveNodeMethod(Operation op)
		{
			ReturnType = EraType.Integer;
			argumentTypeArrayEx = [
						new ArgTypeList{ ArgTypes = { ArgType.Int, ArgType.String, ArgType.Int }, OmitStart = 2 },
						new ArgTypeList{ ArgTypes = { ArgType.RefString, ArgType.String, ArgType.Int }, OmitStart = 2 }
					];
			CanRestructure = false;
			this.op = op;
		}
		public XmlRemoveNodeMethod(Operation op, bool byname) : this(op)
		{
			byName = byname;
			argumentTypeArrayEx = [
						new ArgTypeList{ ArgTypes = { ArgType.String, ArgType.String, ArgType.Int }, OmitStart = 2 },
					];
		}
		private bool byName;
		Operation op;
		bool Remove(XmlNode node)
		{
			if (op == Operation.Attribute)
			{
				if (node is XmlAttribute attr)
				{
					attr.OwnerElement.Attributes.Remove(attr);
					return true;
				}
			}
			else
			{
				if (node.ParentNode != null)
				{
					var parent = node.ParentNode;
					node.ParentNode.RemoveChild(node);
					return true;
				}
			}
			return false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			XmlDocument doc;
			int method = arguments.Count >= 4 ? (int)arguments[3].GetIntValue(exm) : 0;
			if (method > 2 || method < 0) method = 0;
			bool saveToArg0 = true;
			if (arguments[0].GetEraType() == EraType.Integer || (byName && arguments[0].GetEraType() == EraType.String))
			{
				saveToArg0 = false;
				var idx = arguments[0].GetEraType() == EraType.String ? arguments[0].GetStrValue(exm) : arguments[0].GetIntValue(exm).ToString();
				var dict = exm.VEvaluator.VariableData.DataXmlDocument;
				if (dict.TryGetValue(idx, out doc)) { }
				else return -1;
			}
			else
			{
				string xml = arguments[0].GetStrValue(exm);
				doc = new XmlDocument();
				try
				{
					doc.LoadXml(xml);
				}
				catch (XmlException e)
				{
					throw new CodeEE(string.Format(trerror.XmlParseError.Text, Name, xml, e.Message));
				}
			}

			string path = arguments[1].GetStrValue(exm);
			XmlNodeList nodes;
			try
			{
				nodes = doc.SelectNodes(path);
			}
			catch (System.Xml.XPath.XPathException e)
			{
				throw new CodeEE(string.Format(trerror.XmlXPathParseError.Text, Name, path, e.Message));
			}
			if (nodes.Count > 0)
			{
				bool setAllNodes = arguments.Count == 3 ? arguments[2].GetIntValue(exm) != 0 : false;
				if (nodes.Count != 1)
				{
					if (setAllNodes)
						for (int i = 0; i < nodes.Count; i++) Remove(nodes[i]);
				}
				else if (!Remove(nodes[0])) return 0;
				if (saveToArg0)
				{
					(arguments[0] as VariableTerm).SetValue(doc.OuterXml, exm);
				}
			}
			return nodes.Count;
		}
	}
	private sealed class XmlReplaceMethod : FunctionMethod
	{
		public XmlReplaceMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArrayEx = [
						new ArgTypeList{ ArgTypes = { ArgType.Any, ArgType.String } },
						new ArgTypeList{ ArgTypes = { ArgType.Int, ArgType.String, ArgType.String, ArgType.Int }, OmitStart = 3 },
						new ArgTypeList{ ArgTypes = { ArgType.RefString, ArgType.String, ArgType.String, ArgType.Int }, OmitStart = 3 },
					];
			CanRestructure = false;
		}
		public XmlReplaceMethod(bool byname) : this()
		{
			byName = byname;
			argumentTypeArrayEx = [
						new ArgTypeList{ ArgTypes = { ArgType.String, ArgType.String, ArgType.String, ArgType.Int }, OmitStart = 3 },
					];
		}
		private bool byName;

		static bool Replace(XmlNode node, XmlNode newNode)
		{
			if (node.ParentNode != null)
			{
				node.ParentNode.ReplaceChild(newNode, node);
				return true;
			}
			return false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			XmlDocument newXml = new();
			{
				string xml = arguments.Count > 2 ? arguments[2].GetStrValue(exm) : arguments[1].GetStrValue(exm);
				try
				{
					newXml.LoadXml(xml);
				}
				catch (XmlException e)
				{
					throw new CodeEE(string.Format(trerror.XmlParseError.Text, Name, xml, e.Message));
				}
			}
			bool saveToArg0 = true;
			XmlDocument doc = null;
			if (arguments[0].GetEraType() == EraType.Integer || (byName && arguments[0].GetEraType() == EraType.String) || (arguments[0].GetEraType() == EraType.String && arguments.Count == 2))
			{
				saveToArg0 = false;
				var idx = arguments[0].GetEraType() == EraType.String ? arguments[0].GetStrValue(exm) : arguments[0].GetIntValue(exm).ToString();
				var dict = exm.VEvaluator.VariableData.DataXmlDocument;
				if (!dict.TryGetValue(idx, out _)) return -1;
				if (arguments.Count == 2)
				{
					dict[idx] = newXml;
					return 1;
				}
				doc = dict[idx];
			}
			else
			{
				string xml = arguments[0].GetStrValue(exm);
				doc = new XmlDocument();
				try
				{
					doc.LoadXml(xml);
				}
				catch (XmlException e)
				{
					throw new CodeEE(string.Format(trerror.XmlParseError.Text, Name, xml, e.Message));
				}
			}
			string path = arguments[1].GetStrValue(exm);
			XmlNodeList nodes;
			try
			{
				nodes = doc.SelectNodes(path);
			}
			catch (System.Xml.XPath.XPathException e)
			{
				throw new CodeEE(string.Format(trerror.XmlXPathParseError.Text, Name, path, e.Message));
			}
			if (nodes.Count > 0)
			{
				var newNode = newXml.DocumentElement;
				var child = doc.CreateNode(newNode.NodeType, newNode.Name, newNode.NamespaceURI);
				for (int i = 0; i < newNode.Attributes.Count; i++)
				{
					var xattr = newNode.Attributes[i];
					var attr = doc.CreateAttribute(xattr.Name);
					attr.Value = xattr.Value;
					child.Attributes.Append(attr);
				}
				child.InnerXml = newNode.InnerXml;
				bool setAllNodes = arguments.Count >= 4 ? arguments[3].GetIntValue(exm) != 0 : false;
				if (nodes.Count != 1)
				{
					if (setAllNodes)
						for (int i = 0; i < nodes.Count; i++) Replace(nodes[i], child);
				}
				else if (!Replace(nodes[0], child)) return 0;
				if (saveToArg0)
				{
					(arguments[0] as VariableTerm).SetValue(doc.OuterXml, exm);
				}
			}
			return nodes.Count;
		}
	}
	private sealed class ExistFileMethod : FunctionMethod
	{
		public ExistFileMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = [EraType.String];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			var filepath = Utils.GetValidPath(arguments[0].GetStrValue(exm));
			if (filepath != null && File.Exists(filepath)) return 1;
			return 0;
		}
	}
	private sealed class DataTableManagementMethod : FunctionMethod
	{
		public enum Operation { Create, Check, Release, Clear, Case };
		public DataTableManagementMethod(Operation type)
		{
			ReturnType = EraType.Integer;
			if (type == Operation.Case)
				argumentTypeArray = [EraType.String, EraType.Integer];
			else
				argumentTypeArray = [EraType.String];
			CanRestructure = false;
			op = type;
		}
		private Operation op;
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			string key = arguments[0].GetStrValue(exm);
			var dict = exm.VEvaluator.VariableData.DataDataTables;
			bool contains = dict.TryGetValue(key, out var existingDt);
			switch (op)
			{
				case Operation.Clear:
					{
						if (contains)
						{
							existingDt.Clear();
							return 1;
						}
						return -1;
					}
				case Operation.Case:
					{
						if (contains)
						{
							existingDt.CaseSensitive = arguments[1].GetIntValue(exm) == 0;
							return 1;
						}
						return -1;
					}
				case Operation.Check: { return contains ? 1 : 0; }
				case Operation.Release: { if (contains) dict.Remove(key); return 1; }
			}
			if (contains) return 0;
			var dt = new DataTable(key)
			{
				CaseSensitive = true
			};
			var c = dt.Columns.Add("id", typeof(long));
			c.AllowDBNull = false;
			c.Unique = true;
			dict[key] = dt;
			dt.PrimaryKey = [c];
			return 1;
		}
	}
	private sealed class DataTableColumnManagementMethod : FunctionMethod
	{
		public enum Operation { Create, Check, Remove, Names };
		public DataTableColumnManagementMethod(Operation type)
		{
			ReturnType = EraType.Integer;
			if (type == Operation.Create)
				argumentTypeArrayEx = [
						new ArgTypeList{ ArgTypes = { ArgType.String, ArgType.String, ArgType.Any, ArgType.Int }, OmitStart = 2 },
					];
			else if (type == Operation.Names)
				argumentTypeArrayEx = [
						new ArgTypeList{ ArgTypes = { ArgType.String, ArgType.RefString1D }, OmitStart = 1 },
					];
			else
				argumentTypeArray = [EraType.String, EraType.String];
			CanRestructure = false;
			op = type;
		}
		private Operation op;
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			string key = arguments[0].GetStrValue(exm);
			var dict = exm.VEvaluator.VariableData.DataDataTables;
			if (!dict.TryGetValue(key, out var dt))
				return -1;
			if (op == Operation.Names)
			{
				if (arguments.Count > 1 && arguments[1] is VariableTerm v)
				{
					var arrObj = v.Identifier.GetArray();
					if (arrObj is string[] output)
					{
						for (int i = 0; i < dt.Columns.Count; i++) output[i] = dt.Columns[i].ColumnName;
					}
					else
					{
						var sa = (SparseArray<string>)arrObj;
						for (int i = 0; i < dt.Columns.Count; i++) sa[i] = dt.Columns[i].ColumnName;
					}
				}
				else
				{
					var output = exm.VEvaluator.RESULTS_ARRAY;
					for (int i = 0; i < dt.Columns.Count; i++) output[i] = dt.Columns[i].ColumnName;
				}
				return dt.Columns.Count;
			}
			string cName = arguments[1].GetStrValue(exm);
			bool contains = dt.Columns.Contains(cName);
			switch (op)
			{
				case Operation.Check: { return contains ? Utils.DataTable.TypeToInt(dt.Columns[cName].DataType) : 0; }
				case Operation.Remove:
					{
						if (contains && cName.ToLower() != "id")
						{
							dt.Columns.Remove(cName);
							return 1;
						}
						return 0;
					}
			}
			if (contains) return 0;
			Type t = null;
			if (arguments.Count >= 3)
			{
				if (arguments[2].GetEraType() == EraType.String) t = Utils.DataTable.NameToType(arguments[2].GetStrValue(exm));
				else t = Utils.DataTable.IntToType(arguments[2].GetIntValue(exm));
				if (t == null)
				{
					throw new CodeEE(string.Format(trerror.UnsupportedType.Text, Name));
				}
			}
			bool nullable = arguments.Count == 4 ? arguments[3].GetIntValue(exm) != 0 : true;
			DataColumn dc;
			if (t != null) dc = dt.Columns.Add(cName, t);
			else dc = dt.Columns.Add(cName);
			dc.AllowDBNull = nullable;
			return 1;
		}
	}
	private sealed class DataTableRowSetMethod : FunctionMethod
	{
		public enum Operation { Add, Set };
		public DataTableRowSetMethod(Operation type)
		{
			ReturnType = EraType.Integer;
			if (type == Operation.Add)
				argumentTypeArrayEx = [
						new ArgTypeList{ ArgTypes = { ArgType.String, ArgType.VariadicString, ArgType.VariadicAny }, MatchVariadicGroup = true, OmitStart = 1 },
						new ArgTypeList{ ArgTypes = { ArgType.String, ArgType.RefString1D, ArgType.RefAny1D, ArgType.Int } },
					];
			else
				argumentTypeArrayEx = [
						new ArgTypeList{ ArgTypes = { ArgType.String, ArgType.Int, ArgType.VariadicString, ArgType.VariadicAny }, MatchVariadicGroup = true },
						new ArgTypeList{ ArgTypes = { ArgType.String, ArgType.Int, ArgType.RefString1D, ArgType.RefAny1D, ArgType.Int } },
					];
			CanRestructure = false;
			op = type;
		}
		private Operation op;
		void CheckName(DataTable dt, string name, string key)
		{
			if (name == "id")
				throw new CodeEE(string.Format(trerror.DTCanNotEditIdColumn.Text, Name, key));
			if (!dt.Columns.Contains(name))
				throw new CodeEE(string.Format(trerror.DTLackOfNamedColumn.Text, Name, key, name));
		}
		void SetValue(DataRow row, DataTable dt, string name, string key, ExpressionMediator exm, AExpression v)
		{
			CheckName(dt, name, key);
			if (v == null)
			{
				row[name] = DBNull.Value;
				return;
			}
			bool isString = dt.Columns[name].DataType == typeof(string);
			if (v.GetEraType() != (isString ? EraType.String : EraType.Integer))
				throw new CodeEE(string.Format(trerror.DTInvalidDataType.Text, Name, key, name));

			if (isString)
				row[name] = v.GetStrValue(exm);
			else
				row[name] = Utils.DataTable.ConvertInt(v.GetIntValue(exm), dt.Columns[name].DataType);
		}
		void SetValue(DataRow row, DataTable dt, string name, string key, string str)
		{
			CheckName(dt, name, key);
			if (dt.Columns[name].DataType != typeof(string))
				throw new CodeEE(string.Format(trerror.DTInvalidDataType.Text, Name, key, name));
			row[name] = str;
		}
		void SetValue(DataRow row, DataTable dt, string name, string key, long v)
		{
			CheckName(dt, name, key);
			if (dt.Columns[name].DataType == typeof(string))
				throw new CodeEE(string.Format(trerror.DTInvalidDataType.Text, Name, key, name));
			row[name] = Utils.DataTable.ConvertInt(v, dt.Columns[name].DataType);
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			var b = op == Operation.Add ? 0 : 1;
			string key = arguments[0].GetStrValue(exm);
			var dict = exm.VEvaluator.VariableData.DataDataTables;
			if (!dict.TryGetValue(key, out var dt))
				return -1;
			var cCount = 0L;
			DataRow row;
			if (op == Operation.Set)
			{
				var idx = arguments[1].GetIntValue(exm);
				if (dt.Rows.Find(idx) is DataRow r)
					row = r;
				else return -2;
			}
			else
			{
				row = dt.NewRow();
				row[0] = Utils.TimePoint();
			}
			if (arguments.Count == b + 4)
			{
				var namesObj = (arguments[b + 1] as VariableTerm).Identifier.GetArray();
				string[] names;
				SparseArray<string> namesSa;
				int namesLen;
				if (namesObj is string[] na)
				{
					names = na;
					namesSa = null;
					namesLen = names.Length;
				}
				else
				{
					names = null;
					namesSa = (SparseArray<string>)namesObj;
					namesLen = (int)namesSa.Length;
				}
				var count = Math.Min(namesLen, arguments[b + 3].GetIntValue(exm));
				if (arguments[b + 2].GetEraType() == EraType.String)
				{
					var valsObj = (arguments[b + 2] as VariableTerm).Identifier.GetArray();
					if (valsObj is string[] vals)
					{
						count = Math.Min(vals.Length, count);
						for (int i = 0; i < count; i++)
							SetValue(row, dt, names != null ? names[i] : namesSa[i], key, vals[i]);
					}
					else
					{
						var valsSa = (SparseArray<string>)valsObj;
						count = Math.Min((int)valsSa.Length, count);
						for (int i = 0; i < count; i++)
							SetValue(row, dt, names != null ? names[i] : namesSa[i], key, valsSa[i]);
					}
					cCount += count;
				}
				else
				{
					var valsObj = (arguments[b + 2] as VariableTerm).Identifier.GetArray();
					if (valsObj is long[] vals)
					{
						count = Math.Min(vals.Length, count);
						for (int i = 0; i < count; i++)
							SetValue(row, dt, names != null ? names[i] : namesSa[i], key, vals[i]);
					}
					else
					{
						var valsSa = (SparseArray<long>)valsObj;
						count = Math.Min((int)valsSa.Length, count);
						for (int i = 0; i < count; i++)
							SetValue(row, dt, names != null ? names[i] : namesSa[i], key, valsSa[i]);
					}
					cCount += count;
				}
			}
			else
			{
				var pos = b + 1;
				while (pos < arguments.Count)
				{
					var name = arguments[pos].GetStrValue(exm);
					SetValue(row, dt, name, key, exm, arguments[pos + 1]);
					pos += 2;
					cCount++;
				}
			}
			if (op == Operation.Add)
			{
				dt.Rows.Add(row);
				return (long)row[0];
			}
			return cCount;
		}
	}
	private sealed class DataTableLengthMethod : FunctionMethod
	{
		public enum Operation { Row, Column };
		public DataTableLengthMethod(Operation type)
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = [EraType.String];
			CanRestructure = false;
			op = type;
		}
		private Operation op;
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			string key = arguments[0].GetStrValue(exm);
			var dict = exm.VEvaluator.VariableData.DataDataTables;
			if (!dict.TryGetValue(key, out var dtRows)) return -1;
			return op == Operation.Row ? dtRows.Rows.Count : dtRows.Columns.Count;
		}
	}
	private sealed class DataTableRowRemoveMethod : FunctionMethod
	{
		public DataTableRowRemoveMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArrayEx = [
						new ArgTypeList{ ArgTypes = { ArgType.String, ArgType.Int } },
						new ArgTypeList{ ArgTypes = { ArgType.String, ArgType.RefInt1D, ArgType.Int } },
					];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			string key = arguments[0].GetStrValue(exm);
			var dict = exm.VEvaluator.VariableData.DataDataTables;
			if (!dict.TryGetValue(key, out var dt))
				return -1;
			DataRow[] rows;
			if (arguments.Count == 3)
			{
				StringBuilder sb = new();
				var arrObj = (arguments[1] as VariableTerm).Identifier.GetArray();
				int count;
				if (arrObj is long[] array)
				{
					count = Math.Min((int)arguments[2].GetIntValue(exm), array.Length);
					if (count <= 0) return 0;
					sb.Append('(');
					for (int i = 0; i < count; i++)
						sb.Append(i == 0 ? array[i].ToString() : "," + array[i]);
				}
				else
				{
					var sa = (SparseArray<long>)arrObj;
					count = Math.Min((int)arguments[2].GetIntValue(exm), (int)sa.Length);
					if (count <= 0) return 0;
					sb.Append('(');
					for (int i = 0; i < count; i++)
						sb.Append(i == 0 ? sa[i].ToString() : "," + sa[i]);
				}
				sb.Append(')');
				rows = dt.Select("id IN " + sb.ToString());
				if (rows == null) return 0;
			}
			else if (dt.Rows.Find(arguments[1].GetIntValue(exm)) is DataRow row)
				rows = [row];
			else return 0;
			foreach (var row in rows) dt.Rows.Remove(row);
			return rows.Length;
		}
	}
	private sealed class DataTableCellGetMethod : FunctionMethod
	{
		public enum Operation { Get, IsNull, Gets };
		public DataTableCellGetMethod(Operation type)
		{
			ReturnType = type == Operation.Gets ? EraType.String : EraType.Integer;
			argumentTypeArrayEx = [
						new ArgTypeList{ ArgTypes = { ArgType.String, ArgType.Int, ArgType.String, ArgType.Int }, OmitStart = 3 },
					];
			CanRestructure = false;
			op = type;
		}
		private Operation op;
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			var key = arguments[0].GetStrValue(exm);
			var dict = exm.VEvaluator.VariableData.DataDataTables;
			if (!dict.TryGetValue(key, out var dt))
				return op == Operation.IsNull ? -1 : 0;
			bool asId = arguments.Count == 4 ? arguments[3].GetIntValue(exm) != 0 : false;
			var idx = arguments[1].GetIntValue(exm);
			var name = arguments[2].GetStrValue(exm);
			if (asId)
			{
				if (dt.Rows.Find(idx) is DataRow row && dt.Columns.Contains(name))
				{
					var v = row[name];
					return op == Operation.Get ? v == DBNull.Value ? 0 : Convert.ToInt64(v) : (v == DBNull.Value ? 1 : 0);
				}
			}
			else
			{
				if (0 <= idx && idx < dt.Rows.Count && dt.Columns.Contains(name))
				{
					var v = dt.Rows[(int)idx][name];
					return op == Operation.Get ? v == DBNull.Value ? 0 : Convert.ToInt64(v) : (v == DBNull.Value ? 1 : 0);
				}
			}
			return op == Operation.IsNull ? -2 : 0;
		}
		public override string GetStrValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			var key = arguments[0].GetStrValue(exm);
			var dict = exm.VEvaluator.VariableData.DataDataTables;
			if (!dict.TryGetValue(key, out var dt))
				return string.Empty;
			bool asId = arguments.Count == 4 ? arguments[3].GetIntValue(exm) != 0 : false;
			var idx = arguments[1].GetIntValue(exm);
			var name = arguments[2].GetStrValue(exm);
			if (asId)
			{
				if (dt.Rows.Find(idx) is DataRow row && dt.Columns.Contains(name))
				{
					var v = row[name];
					if (v != DBNull.Value) return (string)v;
				}
			}
			else
			{
				if (0 <= idx && idx < dt.Rows.Count && dt.Columns.Contains(name))
				{
					var v = dt.Rows[(int)idx][name];
					if (v != DBNull.Value) return v.ToString();
				}
			}
			return string.Empty;
		}
	}
	private sealed class DataTableCellSetMethod : FunctionMethod
	{
		public DataTableCellSetMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArrayEx = [
					new ArgTypeList{ ArgTypes = { ArgType.String, ArgType.Int, ArgType.String, ArgType.Any, ArgType.Int }, OmitStart = 3 },
				];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			var key = arguments[0].GetStrValue(exm);
			var dict = exm.VEvaluator.VariableData.DataDataTables;
			if (!dict.TryGetValue(key, out var dt))
				return -1;
			bool asId = arguments.Count == 5 ? arguments[4].GetIntValue(exm) != 0 : false;
			var idx = arguments[1].GetIntValue(exm);
			var name = arguments[2].GetStrValue(exm);
			if (name.ToLower() == "id") return 0;
			var v = arguments.Count > 3 ? arguments[3] : null;
			DataRow row = null;
			if (asId) row = dt.Rows.Find(idx);
			else if (idx >= 0 && idx < dt.Rows.Count) row = dt.Rows[(int)idx];
			if (row != null && dt.Columns.Contains(name))
			{
				if (v == null) row[name] = DBNull.Value;
				else
				{
					bool isString = dt.Columns[name].DataType == typeof(string);
					bool isFloat = dt.Columns[name].DataType == typeof(double);
					if (isString)
					{
						if (v.GetEraType() != EraType.String) return -2;
						row[name] = v.GetStrValue(exm);
					}
					else if (isFloat)
					{
						if (v.GetEraType() == EraType.Integer)
							row[name] = (double)v.GetIntValue(exm);
						else if (v.GetEraType() == EraType.Float)
							row[name] = v.GetFloatValue(exm);
						else return -2;
					}
					else
					{
						if (v.GetEraType() != EraType.Integer) return -2;
						row[name] = Utils.DataTable.ConvertInt(v.GetIntValue(exm), dt.Columns[name].DataType);
					}
				}
				return 1;
			}
			return -3;
		}
	}
	private sealed class DataTableSelectMethod : FunctionMethod
	{
		public DataTableSelectMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArrayEx = [
					new ArgTypeList{ ArgTypes = { ArgType.String, ArgType.String, ArgType.String, ArgType.RefInt1D }, OmitStart = 1 },
				];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			var key = arguments[0].GetStrValue(exm);
			var dict = exm.VEvaluator.VariableData.DataDataTables;
			if (!dict.TryGetValue(key, out var dt))
				return -1;
			string filter = arguments.Count > 1 ? (arguments[1] != null ? arguments[1].GetStrValue(exm) : null) : null;
			string sort = arguments.Count > 2 ? (arguments[2] != null ? arguments[2].GetStrValue(exm) : null) : null;
			DataRow[] res;
			if (sort != null) res = dt.Select(filter, sort);
			else if (filter != null) res = dt.Select(filter);
			else res = dt.Select();
			bool toResult = arguments.Count != 4;
			if (toResult)
			{
				var output = GlobalStatic.VEvaluator.RESULT_ARRAY;
				if (res != null)
				{
					int count = Math.Min(res.Length, output.Length - 1);
					for (int i = 0; i < count; i++)
						output[i + 1] = (long)res[i][0];
					output[0] = res.Length;
					return res.Length;
				}
				output[0] = 0;
				return 0;
			}
			else
			{
				var arrObj = (arguments[3] as VariableTerm).Identifier.GetArray();
				if (arrObj is long[] output)
				{
					if (res != null)
					{
						int count = Math.Min(res.Length, output.Length);
						for (int i = 0; i < count; i++)
							output[i] = (long)res[i][0];
						return res.Length;
					}
				}
				else
				{
					var sa = (SparseArray<long>)arrObj;
					if (res != null)
					{
						int count = Math.Min(res.Length, (int)sa.Length);
						for (int i = 0; i < count; i++)
							sa[i] = (long)res[i][0];
						return res.Length;
					}
				}
				return 0;
			}
		}
	}
	private sealed class DataTableToXmlMethod : FunctionMethod
	{
		public DataTableToXmlMethod()
		{
			ReturnType = EraType.String;
			argumentTypeArrayEx = [
					new ArgTypeList{ ArgTypes = { ArgType.String, ArgType.RefString }, OmitStart = 1 },
				];
			CanRestructure = false;
		}
		public override string GetStrValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			var key = arguments[0].GetStrValue(exm);
			var dict = exm.VEvaluator.VariableData.DataDataTables;
			if (!dict.TryGetValue(key, out var dt))
				return string.Empty;
			var idx = arguments.Count > 1 ? 0 : 1;

			var sb = new StringBuilder();
			using (var sw = new StringWriter(sb))
			{
				dt.WriteXmlSchema(sw);
				if (arguments.Count > 1)
				{
					var arrObj = (arguments[1] as VariableTerm).Identifier.GetArray();
					if (arrObj is string[] sArr)
						sArr[0] = sb.ToString();
					else
						((SparseArray<string>)arrObj)[0] = sb.ToString();
				}
				else
					GlobalStatic.VEvaluator.RESULTS_ARRAY[1] = sb.ToString();
				sb.Clear();
				dt.WriteXml(sw);
				return sb.ToString();
			}
		}
	}
	private sealed class DataTableFromXmlMethod : FunctionMethod
	{
		public DataTableFromXmlMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = [EraType.String, EraType.String, EraType.String];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			var key = arguments[0].GetStrValue(exm);
			var dict = exm.VEvaluator.VariableData.DataDataTables;
			DataTable dt;
			try
			{
				dt = new DataTable(key);
				using (var reader = new StringReader(arguments[1].GetStrValue(exm)))
				{
					dt.ReadXmlSchema(reader);
				}
				using (var reader = new StringReader(arguments[2].GetStrValue(exm)))
				{
					dt.ReadXml(reader);
				}
			}
			catch
			{
				return 0;
			}
			if (dict.TryGetValue(key, out _)) dict[key] = dt;
			else dict.Add(key, dt);
			return 1;
		}
	}

	private sealed class MapManagementMethod : FunctionMethod
	{
		public enum Operation { Create, Check, Release };
		public MapManagementMethod(Operation type)
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = [EraType.String];
			CanRestructure = false;
			op = type;
		}
		private Operation op;
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			string key = arguments[0].GetStrValue(exm);
			var dict = exm.VEvaluator.VariableData.DataStringMaps;
			bool contains = dict.ContainsKey(key);
			switch (op)
			{
				case Operation.Check: { return contains ? 1 : 0; }
				case Operation.Release: { if (contains) dict.Remove(key); return 1; }
			}
			if (contains) return 0;
			dict[key] = [];
			return 1;
		}
	}
	private sealed class MapDataOperationMethod : FunctionMethod
	{
		public enum Operation { Set, Has, Remove, Clear, Size };
		public MapDataOperationMethod(Operation type)
		{
			ReturnType = EraType.Integer;
			switch (type)
			{
				case Operation.Set:
					argumentTypeArray = [EraType.String, EraType.String, EraType.String]; break;
				case Operation.Has:
				case Operation.Remove:
					argumentTypeArray = [EraType.String, EraType.String]; break;
				default:
					argumentTypeArray = [EraType.String]; break;
			}
			CanRestructure = false;
			op = type;
		}
		private Operation op;
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			var map = arguments[0].GetStrValue(exm);
			var dict = exm.VEvaluator.VariableData.DataStringMaps;
			if (!dict.TryGetValue(map, out var sMap)) return -1;
			if (op == Operation.Clear) sMap.Clear();
			else if (op == Operation.Size) return sMap.Count;
			else
			{
				var key = arguments[1].GetStrValue(exm);
				bool contains = sMap.ContainsKey(key);
				if (op == Operation.Has) return contains ? 1 : 0;
				if (op == Operation.Remove)
					sMap.Remove(key);
				else
					sMap[key] = arguments[2].GetStrValue(exm);
			}
			return 1;
		}
	}
	private sealed class MapGetStrMethod : FunctionMethod
	{
		public enum Operation { Get, ToXml, GetKeys };
		public MapGetStrMethod(Operation type)
		{
			ReturnType = EraType.String;
			switch (type)
			{
				case Operation.Get:
					argumentTypeArray = [EraType.String, EraType.String]; break;
				case Operation.ToXml:
					argumentTypeArray = [EraType.String]; break;
				case Operation.GetKeys:
					argumentTypeArrayEx = [
							new ArgTypeList{ ArgTypes = { ArgType.String, ArgType.Int }, OmitStart = 1 },
							new ArgTypeList{ ArgTypes = { ArgType.String, ArgType.RefString1D, ArgType.Int } },
						]; break;
			}
			CanRestructure = false;
			op = type;
		}
		private Operation op;
		public override string GetStrValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			var dict = exm.VEvaluator.VariableData.DataStringMaps;
			var map = arguments[0].GetStrValue(exm);
			if (!dict.TryGetValue(map, out var sMap)) return "";
			if (op == Operation.Get)
			{
				var key = arguments[1].GetStrValue(exm);
				if (sMap.TryGetValue(key, out var val)) return val;
				return "";
			}
			else if (op == Operation.GetKeys && arguments.Count > 1)
			{
				int count = 0;
				if (arguments.Count == 3)
				{
					var Term = arguments[1] as VariableTerm;
					if (arguments[2].GetIntValue(exm) == 0) return "";
					var arrObj = Term.Identifier.GetArray();
					if (arrObj is string[] array)
					{
						foreach (var k in sMap.Keys)
						{
							if (count >= array.Length) break;
							array[count] = k;
							count++;
						}
					}
					else
					{
						var sa = (SparseArray<string>)arrObj;
						foreach (var k in sMap.Keys)
						{
							if (count >= sa.Length) break;
							sa[count] = k;
							count++;
						}
					}
				}
				else if (arguments.Count == 2)

				{
					if (arguments[1].GetIntValue(exm) == 0) return "";
					var array = exm.VEvaluator.RESULTS_ARRAY;
					foreach (var k in sMap.Keys)
					{
						if (count >= array.Length) break;
						array[count] = k;
						count++;
					}
				}
				else return "";
				exm.VEvaluator.RESULT = sMap.Keys.Count;
				return arguments.Count == 2 ? exm.VEvaluator.RESULTS : "";
			}
			StringBuilder sb = new();
			if (op == Operation.GetKeys)
			{
				bool isNotEmpty = false;
				foreach (var k in sMap.Keys)
				{
					if (isNotEmpty) sb.Append(',').Append(k);
					else
					{
						isNotEmpty = true;
						sb.Append(k);
					}
				}
			}
			else
			{
				sb.Append("<map>");
				foreach (var p in sMap)
					sb.Append(string.Format("<p><k>{0}</k><v>{1}</v></p>", p.Key, p.Value));
				sb.Append("</map>");
			}
			return sb.ToString();
		}
	}
	private sealed class MapFromXmlMethod : FunctionMethod
	{
		public MapFromXmlMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = [EraType.String, EraType.String];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			var map = arguments[0].GetStrValue(exm);
			var dict = exm.VEvaluator.VariableData.DataStringMaps;
			if (!dict.TryGetValue(map, out var sMap)) return 0;
			var xml = arguments[1].GetStrValue(exm);
			XmlDocument doc = new();
			XmlNodeList nodes;
			try
			{
				doc.LoadXml(xml);
				nodes = doc.SelectNodes("/map/p");
			}
			catch (XmlException e)
			{
				throw new CodeEE(string.Format(trerror.XmlParseError.Text, Name, xml, e.Message));
			}
			for (int i = 0; i < nodes.Count; i++)
			{
				XmlNodeList key, val;
				var node = nodes[i];
				key = node.SelectNodes("./k");
				val = node.SelectNodes("./v");
				if (key.Count != 1 || val.Count != 1) continue;
				sMap[key[0].InnerText] = val[0].InnerXml;
			}
			return 1;
		}
	}

	private sealed class MapValuesMethod : FunctionMethod
	{
		public MapValuesMethod()
		{
			ReturnType = EraType.String;
			argumentTypeArrayEx = [
					new ArgTypeList{ ArgTypes = { ArgType.String, ArgType.Int }, OmitStart = 1 },
					new ArgTypeList{ ArgTypes = { ArgType.String, ArgType.RefString1D, ArgType.Int } },
				];
			CanRestructure = false;
		}
		public override string GetStrValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			var dict = exm.VEvaluator.VariableData.DataStringMaps;
			var map = arguments[0].GetStrValue(exm);
			if (!dict.TryGetValue(map, out var sMap)) return "";
			if (arguments.Count > 1)
			{
				int count = 0;
				if (arguments.Count == 3)
				{
					var Term = arguments[1] as VariableTerm;
					if (arguments[2].GetIntValue(exm) == 0) return "";
					var arrObj = Term.Identifier.GetArray();
					if (arrObj is string[] array)
					{
						foreach (var v in sMap.Values)
						{
							if (count >= array.Length) break;
							array[count] = v;
							count++;
						}
					}
					else
					{
						var sa = (SparseArray<string>)arrObj;
						foreach (var v in sMap.Values)
						{
							if (count >= sa.Length) break;
							sa[count] = v;
							count++;
						}
					}
				}
				else if (arguments.Count == 2)
				{
					if (arguments[1].GetIntValue(exm) == 0) return "";
					var array = exm.VEvaluator.RESULTS_ARRAY;
					foreach (var v in sMap.Values)
					{
						if (count >= array.Length) break;
						array[count] = v;
						count++;
					}
				}
				else return "";
				exm.VEvaluator.RESULT = sMap.Values.Count;
				return arguments.Count == 2 ? exm.VEvaluator.RESULTS : "";
			}
			StringBuilder sb = new();
			bool isNotEmpty = false;
			foreach (var v in sMap.Values)
			{
				if (isNotEmpty) sb.Append(',').Append(v);
				else
				{
					isNotEmpty = true;
					sb.Append(v);
				}
			}
			return sb.ToString();
		}
	}

	private sealed class MapMergeMethod : FunctionMethod
	{
		public MapMergeMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = [EraType.String, EraType.String];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			var dict = exm.VEvaluator.VariableData.DataStringMaps;
			var destMap = arguments[0].GetStrValue(exm);
			var srcMap = arguments[1].GetStrValue(exm);
			if (!dict.TryGetValue(destMap, out var dest) || !dict.TryGetValue(srcMap, out var src)) return 0;
			foreach (var kvp in src)
				dest[kvp.Key] = kvp.Value;
			return 1;
		}
	}

	private sealed class MapRemoveIfMethod : FunctionMethod
	{
		public MapRemoveIfMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = [EraType.String, EraType.String, EraType.String];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			var dict = exm.VEvaluator.VariableData.DataStringMaps;
			var map = arguments[0].GetStrValue(exm);
			if (!dict.TryGetValue(map, out var sMap)) return 0;
			var matchValue = arguments[1].GetStrValue(exm);
			var mode = arguments[2].GetStrValue(exm);
			List<string> toRemove = new();
			switch (mode)
			{
				case "KEY_CONTAINS":
					foreach (var k in sMap.Keys)
						if (k.Contains(matchValue)) toRemove.Add(k);
					break;
				case "KEY_PREFIX":
					foreach (var k in sMap.Keys)
						if (k.StartsWith(matchValue)) toRemove.Add(k);
					break;
				case "KEY_SUFFIX":
					foreach (var k in sMap.Keys)
						if (k.EndsWith(matchValue)) toRemove.Add(k);
					break;
				case "VAL_CONTAINS":
					foreach (var kvp in sMap)
						if (kvp.Value.Contains(matchValue)) toRemove.Add(kvp.Key);
					break;
				case "VAL_EQ":
					foreach (var kvp in sMap)
						if (kvp.Value == matchValue) toRemove.Add(kvp.Key);
					break;
				case "VAL_NE":
					foreach (var kvp in sMap)
						if (kvp.Value != matchValue) toRemove.Add(kvp.Key);
					break;
				default:
					return -1;
			}
			foreach (var k in toRemove)
				sMap.Remove(k);
			return toRemove.Count;
		}
	}

	private sealed class MapFindKeyMethod : FunctionMethod
	{
		public MapFindKeyMethod()
		{
			ReturnType = EraType.String;
			argumentTypeArray = [EraType.String, EraType.String, EraType.String];
			CanRestructure = false;
		}
		public override string GetStrValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			var dict = exm.VEvaluator.VariableData.DataStringMaps;
			var map = arguments[0].GetStrValue(exm);
			if (!dict.TryGetValue(map, out var sMap)) return "";
			var matchValue = arguments[1].GetStrValue(exm);
			var mode = arguments[2].GetStrValue(exm);
			StringBuilder sb = new();
			bool isNotEmpty = false;
			switch (mode)
			{
				case "KEY_CONTAINS":
					foreach (var k in sMap.Keys)
					{
						if (!k.Contains(matchValue)) continue;
						if (isNotEmpty) sb.Append(',');
						isNotEmpty = true;
						sb.Append(k);
					}
					break;
				case "KEY_PREFIX":
					foreach (var k in sMap.Keys)
					{
						if (!k.StartsWith(matchValue)) continue;
						if (isNotEmpty) sb.Append(',');
						isNotEmpty = true;
						sb.Append(k);
					}
					break;
				case "KEY_SUFFIX":
					foreach (var k in sMap.Keys)
					{
						if (!k.EndsWith(matchValue)) continue;
						if (isNotEmpty) sb.Append(',');
						isNotEmpty = true;
						sb.Append(k);
					}
					break;
				case "VAL_CONTAINS":
					foreach (var kvp in sMap)
					{
						if (!kvp.Value.Contains(matchValue)) continue;
						if (isNotEmpty) sb.Append(',');
						isNotEmpty = true;
						sb.Append(kvp.Key);
					}
					break;
				case "VAL_EQ":
					foreach (var kvp in sMap)
					{
						if (kvp.Value != matchValue) continue;
						if (isNotEmpty) sb.Append(',');
						isNotEmpty = true;
						sb.Append(kvp.Key);
					}
					break;
			}
			exm.VEvaluator.RESULT = sb.Length > 0 ? sb.ToString().Split(',').Length : 0;
			return sb.ToString();
		}
	}

	private sealed class MapToStringMethod : FunctionMethod
	{
		public MapToStringMethod()
		{
			ReturnType = EraType.String;
			argumentTypeArrayEx = [
					new ArgTypeList{ ArgTypes = { ArgType.String, ArgType.String, ArgType.String }, OmitStart = 1 },
				];
			CanRestructure = false;
		}
		public override string GetStrValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			var dict = exm.VEvaluator.VariableData.DataStringMaps;
			var map = arguments[0].GetStrValue(exm);
			if (!dict.TryGetValue(map, out var sMap)) return "";
			var sep = arguments.Count > 1 ? arguments[1].GetStrValue(exm) : ",";
			var kvSep = arguments.Count > 2 ? arguments[2].GetStrValue(exm) : "=";
			StringBuilder sb = new();
			bool isNotEmpty = false;
			foreach (var kvp in sMap)
			{
				if (isNotEmpty) sb.Append(sep);
				isNotEmpty = true;
				sb.Append(kvp.Key).Append(kvSep).Append(kvp.Value);
			}
			return sb.ToString();
		}
	}

	private sealed class MapFromStringMethod : FunctionMethod
	{
		public MapFromStringMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArrayEx = [
					new ArgTypeList{ ArgTypes = { ArgType.String, ArgType.String, ArgType.String, ArgType.String }, OmitStart = 2 },
				];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			var dict = exm.VEvaluator.VariableData.DataStringMaps;
			var map = arguments[0].GetStrValue(exm);
			if (!dict.TryGetValue(map, out var sMap)) return 0;
			var data = arguments[1].GetStrValue(exm);
			var sep = arguments.Count > 2 ? arguments[2].GetStrValue(exm) : ",";
			var kvSep = arguments.Count > 3 ? arguments[3].GetStrValue(exm) : "=";
			if (string.IsNullOrEmpty(data)) return 0;
			var entries = data.Split(new[] { sep }, StringSplitOptions.None);
			int count = 0;
			foreach (var entry in entries)
			{
				if (string.IsNullOrEmpty(entry)) continue;
				var idx = entry.IndexOf(kvSep);
				if (idx < 0) continue;
				var key = entry.Substring(0, idx);
				var val = entry.Substring(idx + kvSep.Length);
				sMap[key] = val;
				count++;
			}
			return count;
		}
	}

	private sealed class SqlEscapeMethod : FunctionMethod
	{
		public SqlEscapeMethod()
		{
			ReturnType = EraType.String;
			argumentTypeArray = [EraType.String];
			CanRestructure = true;
		}
		public override string GetStrValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			return SqlManager.Escape(arguments[0].GetStrValue(exm));
		}
	}

	private sealed class SqlExecuteNonQueryParamMethod : FunctionMethod
	{
		public SqlExecuteNonQueryParamMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArrayEx = [
					new ArgTypeList{ ArgTypes = { ArgType.String, ArgType.String, ArgType.VariadicString }, OmitStart = 2 },
				];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			var dbName = arguments[0].GetStrValue(exm);
			var sql = arguments[1].GetStrValue(exm);
			if (arguments.Count <= 2)
				return SqlManager.ExecuteNonQuery(dbName, sql);
			var paramValues = new string[arguments.Count - 2];
			for (int i = 2; i < arguments.Count; i++)
				paramValues[i - 2] = arguments[i]?.GetStrValue(exm) ?? null;
			return SqlManager.ExecuteNonQuery(dbName, sql, paramValues);
		}
	}

	private sealed class SqlExecuteReaderParamMethod : FunctionMethod
	{
		public SqlExecuteReaderParamMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArrayEx = [
					new ArgTypeList{ ArgTypes = { ArgType.String, ArgType.String, ArgType.VariadicString }, OmitStart = 2 },
				];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			var dbName = arguments[0].GetStrValue(exm);
			var sql = arguments[1].GetStrValue(exm);
			if (arguments.Count <= 2)
				return SqlManager.ExecuteReader(dbName, sql);
			var paramValues = new string[arguments.Count - 2];
			for (int i = 2; i < arguments.Count; i++)
				paramValues[i - 2] = arguments[i]?.GetStrValue(exm) ?? null;
			return SqlManager.ExecuteReader(dbName, sql, paramValues);
		}
	}

	private sealed class SqlExecuteScalarLongParamMethod : FunctionMethod
	{
		public SqlExecuteScalarLongParamMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArrayEx = [
					new ArgTypeList{ ArgTypes = { ArgType.String, ArgType.String, ArgType.VariadicString }, OmitStart = 2 },
				];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			var dbName = arguments[0].GetStrValue(exm);
			var sql = arguments[1].GetStrValue(exm);
			if (arguments.Count <= 2)
				return SqlManager.ExecuteScalarLong(dbName, sql);
			var paramValues = new string[arguments.Count - 2];
			for (int i = 2; i < arguments.Count; i++)
				paramValues[i - 2] = arguments[i]?.GetStrValue(exm) ?? null;
			return SqlManager.ExecuteScalarLong(dbName, sql, paramValues);
		}
	}

	private sealed class SqlExecuteScalarStringParamMethod : FunctionMethod
	{
		public SqlExecuteScalarStringParamMethod()
		{
			ReturnType = EraType.String;
			argumentTypeArrayEx = [
					new ArgTypeList{ ArgTypes = { ArgType.String, ArgType.String, ArgType.VariadicString }, OmitStart = 2 },
				];
			CanRestructure = false;
		}
		public override string GetStrValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			var dbName = arguments[0].GetStrValue(exm);
			var sql = arguments[1].GetStrValue(exm);
			if (arguments.Count <= 2)
				return SqlManager.ExecuteScalarString(dbName, sql);
			var paramValues = new string[arguments.Count - 2];
			for (int i = 2; i < arguments.Count; i++)
				paramValues[i - 2] = arguments[i]?.GetStrValue(exm) ?? null;
			return SqlManager.ExecuteScalarString(dbName, sql, paramValues);
		}
	}

	private sealed class MoveTextBoxMethod : FunctionMethod
	{
		public MoveTextBoxMethod(bool b = false)
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = [EraType.Integer, EraType.Integer, EraType.Integer];
			CanRestructure = false;
			resume = b;
		}
		bool resume;
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			if (resume) exm.Console.Window.ResetTextBoxPos();
			else exm.Console.Window.SetTextBoxPos(
				(int)arguments[0].GetIntValue(exm),
				(int)arguments[1].GetIntValue(exm),
				(int)arguments[2].GetIntValue(exm));
			return 1;
		}
	}
	#endregion

	#region CSVデータ関係
	private sealed class GetcharaMethod : FunctionMethod
	{
		public GetcharaMethod()
		{
			ReturnType = EraType.Integer;
			// argumentTypeArray = null;
			argumentTypeArrayEx = [
					new ArgTypeList{ ArgTypes = { ArgType.Int, ArgType.Int }, OmitStart = 1 },
				];
			CanRestructure = false;
		}

		//public override string CheckArgumentType(string name, IOperandTerm[] arguments)
		//{
		//	//通常２つ、１つ省略可能で１～２の引数が必要。
		//	if (arguments.Count < 1)
		//		return name + "関数には少なくとも1つの引数が必要です";
		//	if (arguments.Count > 2)
		//		return name + "関数の引数が多すぎます";

		//	if (arguments[0] == null)
		//		return name + "関数の1番目の引数は省略できません";
		//	if (arguments[0].GetOperandType() != EraType.Integer)
		//		return name + "関数の1番目の引数の型が正しくありません";
		//	//2は省略可能
		//	if ((arguments.Count == 2) && (arguments[1] != null) && (arguments[1].GetOperandType() != EraType.Integer))
		//		return name + "関数の2番目の引数の型が正しくありません";
		//	return null;
		//}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			long integer = arguments[0].GetIntValue(exm);
			if (!Config.CompatiSPChara)
			{
				//if ((arguments.Count > 1) && (arguments[1] != null) && (arguments[1].GetIntValue(exm) != 0))
				return exm.VEvaluator.GetChara(integer);
			}
			//以下互換性用の旧処理
			bool CheckSp = false;
			if ((arguments.Count > 1) && (arguments[1] != null) && (arguments[1].GetIntValue(exm) != 0))
				CheckSp = true;
			if (CheckSp)
			{
				long chara = exm.VEvaluator.GetChara_UseSp(integer, false);
				if (chara != -1)
					return chara;
				else
					return exm.VEvaluator.GetChara_UseSp(integer, true);
			}
			else
				return exm.VEvaluator.GetChara_UseSp(integer, false);
		}
	}

	private sealed class GetspcharaMethod : FunctionMethod
	{
		public GetspcharaMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = [EraType.Integer];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			if (!Config.CompatiSPChara)
				// throw new CodeEE("SPキャラ関係の機能は標準では使用できません(互換性オプション「SPキャラを使用する」をONにしてください)");
				throw new CodeEE(trerror.SPCharacterFeatureDisabled.Text);
			long integer = arguments[0].GetIntValue(exm);
			return exm.VEvaluator.GetChara_UseSp(integer, true);
		}
	}

	private sealed class CsvStrDataMethod : FunctionMethod
	{
		readonly CharacterStrData charaStr;
		public CsvStrDataMethod()
		{
			ReturnType = EraType.String;
			argumentTypeArray = null;
			charaStr = CharacterStrData.NAME;
			argumentTypeArrayEx = [
					new ArgTypeList{ ArgTypes = { ArgType.Int, ArgType.Int }, OmitStart = 1 },
				];
			CanRestructure = true;
		}
		public CsvStrDataMethod(CharacterStrData cStr)
		{
			ReturnType = EraType.String;
			// argumentTypeArray = null;
			argumentTypeArrayEx = [
					new ArgTypeList{ ArgTypes = { ArgType.Int, ArgType.Int }, OmitStart = 1 },
				];
			charaStr = cStr;
			CanRestructure = true;
		}
		//public override string CheckArgumentType(string name, IOperandTerm[] arguments)
		//{
		//	if (arguments.Count < 1)
		//		return name + "関数には少なくとも1つの引数が必要です";
		//	if (arguments.Count > 2)
		//		return name + "関数の引数が多すぎます";
		//	if (arguments[0] == null)
		//		return name + "関数の1番目の引数は省略できません";
		//	if (!arguments[0].IsInteger)
		//		return name + "関数の1番目の引数が数値ではありません";
		//	if (arguments.Count == 1)
		//		return null;
		//	if ((arguments[1] != null) && (arguments[1].GetOperandType() != EraType.Integer))
		//		return name + "関数の2番目の変数が数値ではありません";
		//	return null;
		//}
		public override string GetStrValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			long x = arguments[0].GetIntValue(exm);
			long y = (arguments.Count > 1 && arguments[1] != null) ? arguments[1].GetIntValue(exm) : 0;
			if (!Config.CompatiSPChara && y != 0)
				// throw new CodeEE("SPキャラ関係の機能は標準では使用できません(互換性オプション「SPキャラを使用する」をONにしてください)");
				throw new CodeEE(trerror.SPCharacterFeatureDisabled.Text);
			return exm.VEvaluator.GetCharacterStrfromCSVData(x, charaStr, y != 0, 0);
		}
	}

	private sealed class CsvcstrMethod : FunctionMethod
	{
		public CsvcstrMethod()
		{
			ReturnType = EraType.String;
			// argumentTypeArray = null;
			argumentTypeArrayEx = [
					new ArgTypeList{ ArgTypes = { ArgType.Int, ArgType.Int, ArgType.Int }, OmitStart = 2 },
				];
			CanRestructure = true;
		}
		//public override string CheckArgumentType(string name, IOperandTerm[] arguments)
		//{
		//	if (arguments.Count < 2)
		//		return name + "関数には少なくとも2つの引数が必要です";
		//	if (arguments.Count > 3)
		//		return name + "関数の引数が多すぎます";
		//	if (arguments[0] == null)
		//		return name + "関数の1番目の引数は省略できません";
		//	if (!arguments[0].IsInteger)
		//		return name + "関数の1番目の引数が数値ではありません";
		//	if (arguments[1] == null)
		//		return name + "関数の2番目の引数は省略できません";
		//	if (arguments[1].GetOperandType() != EraType.Integer)
		//		return name + "関数の2番目の変数が数値ではありません";
		//	if (arguments.Count == 2)
		//		return null;
		//	if ((arguments[2] != null) && (arguments[2].GetOperandType() != EraType.Integer))
		//		return name + "関数の3番目の変数が数値ではありません";
		//	return null;
		//}
		public override string GetStrValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			long x = arguments[0].GetIntValue(exm);
			long y = arguments[1].GetIntValue(exm);
			long z = (arguments.Count == 3 && arguments[2] != null) ? arguments[2].GetIntValue(exm) : 0;
			if (!Config.CompatiSPChara && z != 0)
				// throw new CodeEE("SPキャラ関係の機能は標準では使用できません(互換性オプション「SPキャラを使用する」をONにしてください)");
				throw new CodeEE(trerror.SPCharacterFeatureDisabled.Text);
			return exm.VEvaluator.GetCharacterStrfromCSVData(x, CharacterStrData.CSTR, z != 0, y);
		}
	}

	private sealed class CsvDataMethod : FunctionMethod
	{
		readonly CharacterIntData charaInt;
		public CsvDataMethod()
		{
			ReturnType = EraType.Integer;
			// argumentTypeArray = null;
			argumentTypeArrayEx = [
					new ArgTypeList{ ArgTypes = { ArgType.Int, ArgType.Int, ArgType.Int }, OmitStart = 2 },
				];
			charaInt = CharacterIntData.BASE;
			CanRestructure = true;
		}
		public CsvDataMethod(CharacterIntData cInt)
		{
			ReturnType = EraType.Integer;
			// argumentTypeArray = null;
			argumentTypeArrayEx = [
					new ArgTypeList{ ArgTypes = { ArgType.Int, ArgType.Int, ArgType.Int }, OmitStart = 2 },
				];
			charaInt = cInt;
			CanRestructure = true;
		}
		//public override string CheckArgumentType(string name, IOperandTerm[] arguments)
		//{
		//	if (arguments.Count < 2)
		//		return name + "関数には少なくとも2つの引数が必要です";
		//	if (arguments.Count > 3)
		//		return name + "関数の引数が多すぎます";
		//	if (arguments[0] == null)
		//		return name + "関数の1番目の引数は省略できません";
		//	if (!arguments[0].IsInteger)
		//		return name + "関数の1番目の引数が数値ではありません";
		//	if (arguments[1] == null)
		//		return name + "関数の2番目の引数は省略できません";
		//	if (arguments[1].GetOperandType() != EraType.Integer)
		//		return name + "関数の2番目の変数が数値ではありません";
		//	if (arguments.Count == 2)
		//		return null;
		//	if ((arguments[2] != null) && (arguments[2].GetOperandType() != EraType.Integer))
		//		return name + "関数の3番目の変数が数値ではありません";
		//	return null;
		//}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			long x = arguments[0].GetIntValue(exm);
			long y = arguments[1].GetIntValue(exm);
			long z = (arguments.Count == 3 && arguments[2] != null) ? arguments[2].GetIntValue(exm) : 0;
			if (!Config.CompatiSPChara && z != 0)
				// throw new CodeEE("SPキャラ関係の機能は標準では使用できません(互換性オプション「SPキャラを使用する」をONにしてください)");
				throw new CodeEE(trerror.SPCharacterFeatureDisabled.Text);
			return exm.VEvaluator.GetCharacterIntfromCSVData(x, charaInt, z != 0, y);
		}
	}

	private sealed class GetCsvNoMethod : FunctionMethod
	{
		private CharacterStrData _type;
		public GetCsvNoMethod(CharacterStrData data)
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = [EraType.String];
			CanRestructure = true;
			_type = data;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			var str = arguments[0].GetStrValue(exm);
			long ret;
			var b = _type switch
			{
				CharacterStrData.NAME => exm.VEvaluator.Constant.NameToTemplateMap.TryGetValue(str, out ret),
				CharacterStrData.NICKNAME => exm.VEvaluator.Constant.NicknameToTemplateMap.TryGetValue(str, out ret),
				CharacterStrData.CALLNAME => exm.VEvaluator.Constant.CallnameToTemplateMap.TryGetValue(str, out ret),
				CharacterStrData.MASTERNAME => exm.VEvaluator.Constant.MasternameToTemplateMap.TryGetValue(str, out ret),
				_ => throw new ExeEE("error")
			};
			if (!b)
				ret = -1;
			return ret;
		}
	}

	private sealed class FindcharaMethod : FunctionMethod
	{
		public FindcharaMethod(bool last)
		{
			ReturnType = EraType.Integer;
			// argumentTypeArray = null;
			argumentTypeArrayEx = [
					new ArgTypeList{ ArgTypes = { ArgType.CharacterData | ArgType.Any, ArgType.SameAsFirst, ArgType.Int, ArgType.Int }, OmitStart = 2 },
				];
			CanRestructure = false;
			isLast = last;
		}

		readonly bool isLast;
		//public override string CheckArgumentType(string name, IOperandTerm[] arguments)
		//{
		//	//通常3つ、1つ省略可能で2～3の引数が必要。
		//	if (arguments.Count < 2)
		//		return name + "関数には少なくとも2つの引数が必要です";
		//	if (arguments.Count > 4)
		//		return name + "関数の引数が多すぎます";

		//	if (arguments[0] == null)
		//		return name + "関数の1番目の引数は省略できません";
		//	if (!(arguments[0] is VariableTerm))
		//		return name + "関数の1番目の引数の型が正しくありません";
		//	if (!(((VariableTerm)arguments[0]).Identifier.IsCharacterData))
		//		return name + "関数の1番目の引数の変数がキャラクタ変数ではありません";
		//	if (arguments[1] == null)
		//		return name + "関数の2番目の引数は省略できません";
		//	if (arguments[1].GetOperandType() != arguments[0].GetOperandType())
		//		return name + "関数の2番目の引数の型が正しくありません";
		//	//3番目は省略可能
		//	if ((arguments.Count >= 3) && (arguments[2] != null) && (arguments[2].GetOperandType() != EraType.Integer))
		//		return name + "関数の3番目の引数の型が正しくありません";
		//	//4番目は省略可能
		//	if ((arguments.Count >= 4) && (arguments[3] != null) && (arguments[3].GetOperandType() != EraType.Integer))
		//		return name + "関数の4番目の引数の型が正しくありません";
		//	return null;
		//}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			VariableTerm vTerm = (VariableTerm)arguments[0];
			VariableToken varID = vTerm.Identifier;

			long elem = 0;
			if (vTerm.Identifier.IsArray1D)
				elem = vTerm.GetElementInt(1, exm);
			else if (vTerm.Identifier.IsArray2D)
			{
				elem = vTerm.GetElementInt(1, exm) << 32;
				elem += vTerm.GetElementInt(2, exm);
			}
			long startindex = 0;
			long lastindex = exm.VEvaluator.CHARANUM;
			if (arguments.Count >= 3 && arguments[2] != null)
				startindex = arguments[2].GetIntValue(exm);
			if (arguments.Count >= 4 && arguments[3] != null)
				lastindex = arguments[3].GetIntValue(exm);
			if (startindex < 0 || startindex >= exm.VEvaluator.CHARANUM)
				// throw new CodeEE((isLast ? "" : "") + "関数の第3引数(" + startindex.ToString() + ")はキャラクタ位置の範囲外です");
				throw new CodeEE(string.Format(trerror.CharacterIndexOutOfRange.Text, Name, 3, startindex));
			if (lastindex < 0 || lastindex > exm.VEvaluator.CHARANUM)
				// throw new CodeEE((isLast ? "" : "") + "関数の第4引数(" + lastindex.ToString() + ")はキャラクタ位置の範囲外です");
				throw new CodeEE(string.Format(trerror.CharacterIndexOutOfRange.Text, Name, 4, lastindex));
			long ret;
			switch (varID.GetEraType())
			{
				case EraType.String:
				{
					string word = arguments[1].GetStrValue(exm);
					ret = VariableEvaluator.FindChara(varID, elem, word, startindex, lastindex, isLast);
					break;
				}
				case EraType.Float:
				{
					double word = arguments[1].GetFloatValue(exm);
					ret = VariableEvaluator.FindChara(varID, elem, word, startindex, lastindex, isLast);
					break;
				}
				default:
				{
					long word = arguments[1].GetIntValue(exm);
					ret = VariableEvaluator.FindChara(varID, elem, word, startindex, lastindex, isLast);
					break;
				}
			}
			return ret;
		}
	}

	private sealed class ExistCsvMethod : FunctionMethod
	{
		public ExistCsvMethod()
		{
			ReturnType = EraType.Integer;
			// argumentTypeArray = null;
			argumentTypeArrayEx = [
					new ArgTypeList{ ArgTypes = { ArgType.Int, ArgType.Int }, OmitStart = 1 },
				];
			CanRestructure = true;
		}
		//public override string CheckArgumentType(string name, IOperandTerm[] arguments)
		//{
		//	if (arguments.Count < 1)
		//		return name + "関数には少なくとも1つの引数が必要です";
		//	if (arguments.Count > 2)
		//		return name + "関数の引数が多すぎます";
		//	if (arguments[0] == null)
		//		return name + "関数の1番目の引数は省略できません";
		//	if (!arguments[0].IsInteger)
		//		return name + "関数の1番目の引数が数値ではありません";
		//	if (arguments.Count == 1)
		//		return null;
		//	if ((arguments[1] != null) && (arguments[1].GetOperandType() != EraType.Integer))
		//		return name + "関数の2番目の変数が数値ではありません";
		//	return null;
		//}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			long no = arguments[0].GetIntValue(exm);
			bool isSp = (arguments.Count == 2 && arguments[1] != null) ? (arguments[1].GetIntValue(exm) != 0) : false;
			if (!Config.CompatiSPChara && isSp)
				// throw new CodeEE("SPキャラ関係の機能は標準では使用できません(互換性オプション「SPキャラを使用する」をONにしてください)");
				throw new CodeEE(trerror.SPCharacterFeatureDisabled.Text);

			return exm.VEvaluator.ExistCsv(no, isSp);
		}
	}
	#endregion

	#region 汎用処理系
	private sealed class VarsizeMethod : FunctionMethod
	{
		public VarsizeMethod()
		{
			ReturnType = EraType.Integer;
			// argumentTypeArray = null;
			argumentTypeArrayEx = [
					new ArgTypeList{ ArgTypes = { ArgType.String, ArgType.Int }, OmitStart = 1 },
				];
			CanRestructure = true;
			//1808beta009 参照型変数の追加によりちょっと面倒になった
			HasUniqueRestructure = true;
		}
		//public override string CheckArgumentType(string name, IOperandTerm[] arguments)
		//{
		//	if (arguments.Count < 1)
		//		return name + "関数には少なくとも1つの引数が必要です";
		//	if (arguments.Count > 2)
		//		return name + "関数の引数が多すぎます";
		//	if (arguments[0] == null)
		//		return name + "関数の1番目の引数は省略できません";
		//	if (!arguments[0].IsString)
		//		return name + "関数の1番目の引数が文字列ではありません";
		//	if (arguments[0] is SingleTerm)
		//	{
		//		string varName = ((SingleTerm)arguments[0]).Text;
		//		if (GlobalStatic.IdentifierDictionary.GetVariableToken(varName, null, true) == null)
		//			return name + "関数の1番目の引数が変数名ではありません";
		//	}
		//	if (arguments.Count == 1)
		//		return null;
		//	if ((arguments[1] != null) && (arguments[1].GetOperandType() != EraType.Integer))
		//		return name + "関数の2番目の変数が数値ではありません";
		//	if (arguments.Count == 2)
		//		return null;
		//	return null;
		//}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			VariableToken var = GlobalStatic.IdentifierDictionary.GetVariableToken(arguments[0].GetStrValue(exm), null, true);
			if (var == null)
				// throw new CodeEE("VARSIZEの1番目の引数(\"" + arguments[0].GetStrValue(exm) + "\")が変数名ではありません");
				throw new CodeEE(string.Format(trerror.NotVariableName.Text, Name, 1, arguments[0].GetStrValue(exm)));
			int dim = 0;
			if (arguments.Count == 2 && arguments[1] != null)
				dim = (int)arguments[1].GetIntValue(exm);
			if (Config.VarsizeDimConfig && dim > 0)
				dim--;
			return var.GetLength(dim);
		}
		public override bool UniqueRestructure(ExpressionMediator exm, List<AExpression> arguments)
		{
			arguments[0].Restructure(exm);
			if (arguments.Count > 1)
				arguments[1].Restructure(exm);
			if (arguments[0] is SingleTerm && (arguments.Count == 1 || arguments[1] is SingleTerm))
			{
				VariableToken var = GlobalStatic.IdentifierDictionary.GetVariableToken(arguments[0].GetStrValue(exm), null, true);
				if (var == null || var.IsReference)//可変長の場合は定数化できない
					return false;
				return true;
			}
			return false;
		}
	}

	private sealed class CheckfontMethod : FunctionMethod
	{
		public CheckfontMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = [EraType.String];
			CanRestructure = true;//起動中に変わることもそうそうないはず……
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			string str = arguments[0].GetStrValue(exm);
			using System.Drawing.Text.InstalledFontCollection ifc = new();
			long isInstalled = 0;
			foreach (FontFamily ff in ifc.Families)
			{
				#region EE_フォントファイル対応
				if (ff.Name == str)
				{
					isInstalled = 1;
					break;
				}
			}
			foreach (FontFamily ff in GlobalStatic.Pfc.Families)
			{
				if (ff.Name == str)
				{
					isInstalled = 1;
					break;
				}
			}
			#endregion
			return (isInstalled);
		}

	}

	private sealed class CheckdataMethod : FunctionMethod
	{
		public CheckdataMethod(EraSaveFileType type)
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = [EraType.Integer];
			CanRestructure = false;
			this.type = type;
		}

		readonly EraSaveFileType type;
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			long target = arguments[0].GetIntValue(exm);
			if (target < 0)
				// throw new CodeEE(Name + "の引数に負の値(" + target.ToString() + ")が指定されました");
				throw new CodeEE(string.Format(trerror.ArgIsNegative.Text, Name, 1, target));
			else if (target > int.MaxValue)
				// throw new CodeEE(Name + "の引数(" + target.ToString() + ")が大きすぎます");
				throw new CodeEE(string.Format(trerror.ArgIsTooLarge.Text, Name, 1, target));
			EraDataResult result = exm.VEvaluator.CheckData((int)target, type);
			exm.VEvaluator.RESULTS = result.DataMes;
			return (long)result.State;
		}
	}

	/// <summary>
	/// ファイル名をstringで指定する版・CHKVARDATAとCHKCHARADATAはこっちに分類
	/// </summary>
	private sealed class CheckdataStrMethod : FunctionMethod
	{
		public CheckdataStrMethod(EraSaveFileType type)
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = [EraType.String];
			CanRestructure = false;
			this.type = type;
		}

		readonly EraSaveFileType type;
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			string datFilename = arguments[0].GetStrValue(exm);
			EraDataResult result = exm.VEvaluator.CheckData(datFilename, type);
			exm.VEvaluator.RESULTS = result.DataMes;
			return (long)result.State;
		}
	}

	/// <summary>
	/// ファイル探索関数
	/// </summary>
	private sealed class FindFilesMethod : FunctionMethod
	{
		public FindFilesMethod(EraSaveFileType type)
		{
			ReturnType = EraType.Integer;
			// argumentTypeArray = null;
			argumentTypeArrayEx = [
					new ArgTypeList{ ArgTypes = { ArgType.String }, OmitStart = 0 },
				];
			CanRestructure = false;
			this.type = type;
		}

		readonly EraSaveFileType type;

		//public override string CheckArgumentType(string name, IOperandTerm[] arguments)
		//{
		//	if (arguments.Count > 1)
		//		return name + "関数の引数が多すぎます";
		//	if (arguments.Count == 0 || arguments[0] == null)
		//		return null;
		//	if (!arguments[0].IsString)
		//		return name + "関数の1番目の引数が文字列ではありません";
		//	return null;
		//}

		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			string pattern = "*";
			if (arguments.Count > 0 && arguments[0] != null)
				pattern = arguments[0].GetStrValue(exm);
			List<string> filepathes = VariableEvaluator.GetDatFiles(type == EraSaveFileType.CharVar, pattern);
			var results = exm.VEvaluator.VariableData.DataStringArray[(int)(VariableCode.RESULTS & VariableCode.__LOWERCASE__)];
			int resultsLen = exm.VEvaluator.VariableData.Constant.VariableStrArrayLength[(int)(VariableCode.RESULTS & VariableCode.__LOWERCASE__)];
			if (filepathes.Count <= resultsLen)
			{
				for (int i = 0; i < filepathes.Count; i++)
					results[i] = filepathes[i];
			}
			else
			{
				for (int i = 0; i < resultsLen; i++)
					results[i] = filepathes[i];
			}
			return filepathes.Count;
		}
	}


	private sealed class IsSkipMethod : FunctionMethod
	{
		public IsSkipMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = [];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			return exm.Process.SkipPrint ? 1L : 0L;
		}
	}

	private sealed class MesSkipMethod : FunctionMethod
	{
		public MesSkipMethod(bool warn)
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = null;
			CanRestructure = false;
			this.warn = warn;
		}

		readonly bool warn;
		public override string CheckArgumentType(string name, List<AExpression> arguments)
		{
			if (arguments.Count > 0)
				// return name + "関数の引数が多すぎます";
				return string.Format(trerror.TooManyFuncArgs.Text, name);
			if (warn)
				// ParserMediator.Warn("関数MOUSESKIP()は推奨されません。代わりに関数MESSKIP()を使用してください", GlobalStatic.Process.GetScaningLine(), 1, false, false, null);
				ParserMediator.Warn(string.Format(trerror.FuncDeprecated.Text, name, "MESSKIP"), GlobalStatic.Process.GetScaningLine(), 1, false, false, null);
			return null;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			return GlobalStatic.Console.MesSkip ? 1L : 0L;
		}
	}


	private sealed class GetColorMethod : FunctionMethod
	{
		public GetColorMethod(bool isDef)
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = [];
			CanRestructure = isDef;
			defaultColor = isDef;
		}

		readonly bool defaultColor;
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			Color color = defaultColor ? Config.ForeColor : GlobalStatic.Console.StringStyle.Color;
			return color.ToArgb() & 0xFFFFFF;
		}
	}

	private sealed class GetFocusColorMethod : FunctionMethod
	{
		public GetFocusColorMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = [];
			CanRestructure = true;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			return Config.FocusColor.ToArgb() & 0xFFFFFF;
		}
	}

	private sealed class GetBGColorMethod : FunctionMethod
	{
		public GetBGColorMethod(bool isDef)
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = [];
			CanRestructure = isDef;
			defaultColor = isDef;
		}

		readonly bool defaultColor;
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			var skc = defaultColor ? Config.BackColor.ToSKColor() : GlobalStatic.Console.bgColor;
			return ((long)skc.Red << 16 | (long)skc.Green << 8 | skc.Blue) & 0xFFFFFF;
		}
	}

	private sealed class GetStyleMethod : FunctionMethod
	{
		public GetStyleMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = [];
			CanRestructure = false;
		}

		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			FontStyle fontstyle = GlobalStatic.Console.StringStyle.FontStyle;
			long ret = 0;
			if ((fontstyle & FontStyle.Bold) == FontStyle.Bold)
				ret |= 1;
			if ((fontstyle & FontStyle.Italic) == FontStyle.Italic)
				ret |= 2;
			if ((fontstyle & FontStyle.Strikeout) == FontStyle.Strikeout)
				ret |= 4;
			if ((fontstyle & FontStyle.Underline) == FontStyle.Underline)
				ret |= 8;
			return ret;
		}
	}

	private sealed class GetPlatformMethod : FunctionMethod
	{
		public GetPlatformMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = [];
			CanRestructure = true;
		}

		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			if (OperatingSystem.IsWindows()) return 0;
			if (OperatingSystem.IsAndroid()) return 1;
			if (OperatingSystem.IsIOS()) return 2;
			if (OperatingSystem.IsMacOS()) return 3;
			if (OperatingSystem.IsLinux()) return 4;
			return 5; // Unknown
		}
	}

	private sealed class GetFontMethod : FunctionMethod
	{
		public GetFontMethod()
		{
			ReturnType = EraType.String;
			argumentTypeArray = [];
			CanRestructure = false;
		}
		public override string GetStrValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			return GlobalStatic.Console.StringStyle.Fontname;
		}
	}

	private sealed class BarStringMethod : FunctionMethod
	{
		public BarStringMethod()
		{
			ReturnType = EraType.String;
			argumentTypeArray = [EraType.Integer, EraType.Integer, EraType.Integer];
			CanRestructure = true;
		}
		public override string GetStrValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			long var = arguments[0].GetIntValue(exm);
			long max = arguments[1].GetIntValue(exm);
			long length = arguments[2].GetIntValue(exm);
			return ExpressionMediator.CreateBar(var, max, length);
		}
	}

	private sealed class CurrentAlignMethod : FunctionMethod
	{
		public CurrentAlignMethod()
		{
			ReturnType = EraType.String;
			argumentTypeArray = [];
			CanRestructure = false;
		}
		public override string GetStrValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			if (exm.Console.Alignment == DisplayLineAlignment.LEFT)
				return "LEFT";
			else if (exm.Console.Alignment == DisplayLineAlignment.CENTER)
				return "CENTER";
			else
				return "RIGHT";
		}
	}

	private sealed class CurrentRedrawMethod : FunctionMethod
	{
		public CurrentRedrawMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = [];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			return (exm.Console.Redraw == GameView.ConsoleRedraw.None) ? 0L : 1L;
		}
	}

	private sealed class ColorFromNameMethod : FunctionMethod
	{
		public ColorFromNameMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = [EraType.String];
			CanRestructure = true;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			string colorName = arguments[0].GetStrValue(exm);
			Color color = Color.FromName(colorName);
			int i;
			if (color.A > 0)
				i = (color.R << 16) + (color.G << 8) + color.B;
			else
			{
				if (colorName.Equals("transparent", StringComparison.OrdinalIgnoreCase))
					// throw new CodeEE("無色透明(Transparent)は色として指定できません");
					throw new CodeEE(trerror.TransparentUnsupported.Text);
				//throw new CodeEE("指定された色名\"" + colorName + "\"は無効な色名です");
				i = -1;
			}
			return i;
		}
	}

	private sealed class ColorFromRGBMethod : FunctionMethod
	{
		public ColorFromRGBMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = [EraType.Integer, EraType.Integer, EraType.Integer];
			CanRestructure = true;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			long r = arguments[0].GetIntValue(exm);
			if (r < 0 || r > 255)
				// throw new CodeEE("第１引数が0から255の範囲外です");
				throw new CodeEE(string.Format(trerror.ArgIsOutOfRange.Text, Name, 1, r, 0, 255));
			long g = arguments[1].GetIntValue(exm);
			if (g < 0 || g > 255)
				// throw new CodeEE("第２引数が0から255の範囲外です");
				throw new CodeEE(string.Format(trerror.ArgIsOutOfRange.Text, Name, 2, g, 0, 255));
			long b = arguments[2].GetIntValue(exm);
			if (b < 0 || b > 255)
				// throw new CodeEE("第３引数が0から255の範囲外です");
				throw new CodeEE(string.Format(trerror.ArgIsOutOfRange.Text, Name, 3, b, 0, 255));
			return (r << 16) + (g << 8) + b;
		}
	}
	/// <summary>
	/// 1810 作ったけど保留
	/// </summary>
	// 使われてない
	//private sealed class GetRefMethod : FunctionMethod
	//{
	//	public GetRefMethod()
	//	{
	//		ReturnType = EraType.String;
	//		argumentTypeArray = null;
	//		CanRestructure = false;
	//	}
	//	public override string CheckArgumentType(string name, IOperandTerm[] arguments)
	//	{
	//		if (arguments.Count < 1)
	//			return name + "関数には少なくとも1つの引数が必要です";
	//		if (arguments.Count > 1)
	//			return name + "関数の引数が多すぎます";
	//		if (arguments[0] == null)
	//			return name + "関数の1番目の引数は省略できません";
	//		if (!(arguments[0] is UserDefinedRefMethodNoArgTerm))
	//			return name + "関数の1番目の引数が関数参照ではありません";
	//		return null;
	//	}
	//	public override string GetStrValue(ExpressionMediator exm, IOperandTerm[] arguments)
	//	{
	//		return ((UserDefinedRefMethodNoArgTerm)arguments[0]).GetRefName();
	//	}
	//}
	#endregion

	#region 定数取得
	private sealed class MoneyStrMethod : FunctionMethod
	{
		public MoneyStrMethod()
		{
			ReturnType = EraType.String;
			// argumentTypeArray = null;
			argumentTypeArrayEx = [
					new ArgTypeList{ ArgTypes = { ArgType.Int, ArgType.String}, OmitStart = 1 }
				];
			CanRestructure = true;
		}
		//public override string CheckArgumentType(string name, IOperandTerm[] arguments)
		//{
		//	//通常2つ、1つ省略可能で1～2の引数が必要。
		//	if (arguments.Count < 1)
		//		return name + "関数には少なくとも1つの引数が必要です";
		//	if (arguments.Count > 2)
		//		return name + "関数の引数が多すぎます";
		//	if (arguments[0] == null)
		//		return name + "関数の1番目の引数は省略できません";
		//	if (arguments[0].GetOperandType() != EraType.Integer)
		//		return name + "関数の1番目の引数の型が正しくありません";
		//	if ((arguments.Count >= 2) && (arguments[1] != null) && (arguments[1].GetEraType() != EraType.String))
		//		return name + "関数の2番目の引数の型が正しくありません";
		//	return null;
		//}
		public override string GetStrValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			long money = arguments[0].GetIntValue(exm);
			if ((arguments.Count < 2) || (arguments[1] == null))
				return Config.MoneyFirst ? Config.MoneyLabel + money.ToString() : money.ToString() + Config.MoneyLabel;
			string format = arguments[1].GetStrValue(exm);
			string ret;
			try
			{
				ret = money.ToString(format);
			}
			catch (FormatException)
			{
				// throw new CodeEE("MONEYSTR関数の第2引数の書式指定が間違っています");
				throw new CodeEE(string.Format(trerror.InvalidFormat.Text, Name, 2));
			}
			return Config.MoneyFirst ? Config.MoneyLabel + ret : ret + Config.MoneyLabel;
		}
	}

	private sealed class GetPrintCPerLineMethod : FunctionMethod
	{
		public GetPrintCPerLineMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = [];
			CanRestructure = true;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			return Config.PrintCPerLine;
		}
	}

	private sealed class PrintCLengthMethod : FunctionMethod
	{
		public PrintCLengthMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = [];
			CanRestructure = true;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			return Config.PrintCLength;
		}
	}

	private sealed class GetSaveNosMethod : FunctionMethod
	{
		public GetSaveNosMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = [];
			CanRestructure = true;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			return Config.SaveDataNos;
		}
	}

	private sealed class GettimeMethod : FunctionMethod
	{
		public GettimeMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = [];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			long date = DateTime.Now.Year;
			date = date * 100 + DateTime.Now.Month;
			date = date * 100 + DateTime.Now.Day;
			date = date * 100 + DateTime.Now.Hour;
			date = date * 100 + DateTime.Now.Minute;
			date = date * 100 + DateTime.Now.Second;
			date = date * 1000 + DateTime.Now.Millisecond;
			return date;//17桁。2京くらい。
		}
	}

	private sealed class GettimesMethod : FunctionMethod
	{
		public GettimesMethod()
		{
			ReturnType = EraType.String;
			argumentTypeArray = [];
			CanRestructure = false;
		}
		public override string GetStrValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			return DateTime.Now.ToString("yyyy/MM/dd HH:mm:ss");
		}
	}

	private sealed class GetmsMethod : FunctionMethod
	{
		public GetmsMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = [];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			//西暦0001年1月1日からの経過時間をミリ秒で。
			return DateTime.Now.Ticks / 10000;
		}
	}

	private sealed class GetSecondMethod : FunctionMethod
	{
		public GetSecondMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = [];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			//西暦0001年1月1日からの経過時間を秒で。
			//Ticksは100ナノ秒単位であるが実際にはそんな精度はないので無駄。
			return DateTime.Now.Ticks / 10000000;
		}
	}
	#endregion

	#region 数学関数
	private sealed class RandMethod : FunctionMethod
	{
		public RandMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArrayEx = [
					new ArgTypeList{ ArgTypes = { ArgType.Any, ArgType.Any}, OmitStart = 1 }
				];
			CanRestructure = false;
		}

		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			long min = 0;
			long max;
			if (arguments.Count == 1)
				max = arguments[0].GetIntValue(exm);
			else
			{
				if (arguments[0] != null)
					min = arguments[0].GetIntValue(exm);
				max = arguments[1].GetIntValue(exm);
			}
			if (max <= min)
			{
				if (min == 0)
					throw new CodeEE(string.Format(trerror.NegativeMaximum.Text, Name, max));
				else
					throw new CodeEE(string.Format(trerror.MaximumLowerThanMinimum.Text, Name, max));
			}
			return exm.VEvaluator.GetNextRand(max - min) + min;
		}

		public override double GetFloatValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			double min = 0.0;
			double max;
			if (arguments.Count == 1)
				max = ToDouble(arguments[0], exm);
			else
			{
				if (arguments[0] != null)
					min = ToDouble(arguments[0], exm);
				max = ToDouble(arguments[1], exm);
			}
			if (max <= min)
			{
				if (min == 0.0)
					throw new CodeEE(string.Format(trerror.NegativeMaximum.Text, Name, max));
				else
					throw new CodeEE(string.Format(trerror.MaximumLowerThanMinimum.Text, Name, max));
			}
			return exm.VEvaluator.GetNextRandDouble() * (max - min) + min;
		}

		public override SingleTerm GetReturnValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			if (HasFloatArg(arguments))
				return new SingleFloatTerm(GetFloatValue(exm, arguments));
			return new SingleLongTerm(GetIntValue(exm, arguments));
		}
	}

	private sealed class MaxMethod : FunctionMethod
	{
		readonly bool isMax;
		public MaxMethod()
		{
			ReturnType = EraType.Integer;
			CanReturnFloat = true;
			argumentTypeArrayEx = [
					new ArgTypeList{ ArgTypes = { ArgType.Any, ArgType.VariadicAny}, OmitStart = 1 }
				];
			isMax = true;
			CanRestructure = false;
		}
		public MaxMethod(bool max)
		{
			ReturnType = EraType.Integer;
			CanReturnFloat = true;
			argumentTypeArrayEx = [
					new ArgTypeList{ ArgTypes = { ArgType.Any, ArgType.VariadicAny}, OmitStart = 1 }
				];
			isMax = max;
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			double ret = ToDouble(arguments[0], exm);

			for (int i = 1; i < arguments.Count; i++)
			{
				double newRet = ToDouble(arguments[i], exm);
				if (isMax)
				{
					if (ret < newRet)
						ret = newRet;
				}
				else
				{
					if (ret > newRet)
						ret = newRet;
				}
			}
			return (long)ret;
		}

		public override double GetFloatValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			double ret = ToDouble(arguments[0], exm);
			for (int i = 1; i < arguments.Count; i++)
			{
				double v = ToDouble(arguments[i], exm);
				if (isMax)
				{
					if (v > ret) ret = v;
				}
				else
				{
					if (v < ret) ret = v;
				}
			}
			return ret;
		}

		public override SingleTerm GetReturnValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			if (HasFloatArg(arguments))
				return new SingleFloatTerm(GetFloatValue(exm, arguments));
			return new SingleLongTerm(GetIntValue(exm, arguments));
		}
	}

	private sealed class AbsMethod : FunctionMethod
	{
		public AbsMethod()
		{
			ReturnType = EraType.Integer;
			CanReturnFloat = true;
			argumentTypeArrayEx = [
				new ArgTypeList{ ArgTypes = { ArgType.Any } }
			];
			CanRestructure = true;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			long ret = arguments[0].GetEraType() == EraType.Integer ? arguments[0].GetIntValue(exm) : (long)arguments[0].GetFloatValue(exm);
			if (ret == long.MinValue)
				throw new CodeEE(string.Format(trerror.MinInt64CanNotApplyABS.Text, Name, long.MinValue));
			return Math.Abs(ret);
		}
		public override double GetFloatValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			return Math.Abs(ToDouble(arguments[0], exm));
		}
		public override SingleTerm GetReturnValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			if (HasFloatArg(arguments))
				return new SingleFloatTerm(GetFloatValue(exm, arguments));
			return new SingleLongTerm(GetIntValue(exm, arguments));
		}
	}

	private sealed class PowerMethod : FunctionMethod
	{
		public PowerMethod()
		{
			ReturnType = EraType.Integer;
			CanReturnFloat = true;
			argumentTypeArrayEx = [
				new ArgTypeList{ ArgTypes = { ArgType.Any, ArgType.Any } }
			];
			CanRestructure = true;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			double pow = Math.Pow(ToDouble(arguments[0], exm), ToDouble(arguments[1], exm));
			if (double.IsNaN(pow))
				throw new CodeEE(string.Format(trerror.ResultIsNaN.Text, Name));
			else if (double.IsInfinity(pow))
				throw new CodeEE(string.Format(trerror.ResultIsInfinity.Text, Name));
			else if ((pow >= long.MaxValue) || (pow <= long.MinValue))
				throw new CodeEE(string.Format(trerror.ResultIsOutOfTheRangeOfInt64.Text, Name, pow));
			return (long)pow;
		}
		public override double GetFloatValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			double pow = Math.Pow(ToDouble(arguments[0], exm), ToDouble(arguments[1], exm));
			if (double.IsNaN(pow))
				throw new CodeEE(string.Format(trerror.ResultIsNaN.Text, Name));
			else if (double.IsInfinity(pow))
				throw new CodeEE(string.Format(trerror.ResultIsInfinity.Text, Name));
			return pow;
		}
		public override SingleTerm GetReturnValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			if (HasFloatArg(arguments))
				return new SingleFloatTerm(GetFloatValue(exm, arguments));
			return new SingleLongTerm(GetIntValue(exm, arguments));
		}
	}

	private sealed class SqrtMethod : FunctionMethod
	{
		public SqrtMethod()
		{
			ReturnType = EraType.Integer;
			CanReturnFloat = true;
			argumentTypeArrayEx = [
				new ArgTypeList{ ArgTypes = { ArgType.Any } }
			];
			CanRestructure = true;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			double ret = ToDouble(arguments[0], exm);
			if (ret < 0)
				throw new CodeEE(string.Format(trerror.ArgIsNegative.Text, Name, 1, ret));
			return (long)Math.Sqrt(ret);
		}
		public override double GetFloatValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			double ret = ToDouble(arguments[0], exm);
			if (ret < 0)
				throw new CodeEE(string.Format(trerror.ArgIsNegative.Text, Name, 1, ret));
			return Math.Sqrt(ret);
		}
		public override SingleTerm GetReturnValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			if (HasFloatArg(arguments))
				return new SingleFloatTerm(GetFloatValue(exm, arguments));
			return new SingleLongTerm(GetIntValue(exm, arguments));
		}
	}

	private sealed class CbrtMethod : FunctionMethod
	{
		public CbrtMethod()
		{
			ReturnType = EraType.Integer;
			CanReturnFloat = true;
			argumentTypeArrayEx = [
				new ArgTypeList{ ArgTypes = { ArgType.Any } }
			];
			CanRestructure = true;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			double ret = ToDouble(arguments[0], exm);
			if (ret < 0)
				throw new CodeEE(string.Format(trerror.ArgIsNegative.Text, Name, 1, ret));
			return (long)Math.Pow(ret, 1.0 / 3.0);
		}
		public override double GetFloatValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			double ret = ToDouble(arguments[0], exm);
			if (ret < 0)
				throw new CodeEE(string.Format(trerror.ArgIsNegative.Text, Name, 1, ret));
			return Math.Pow(ret, 1.0 / 3.0);
		}
		public override SingleTerm GetReturnValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			if (HasFloatArg(arguments))
				return new SingleFloatTerm(GetFloatValue(exm, arguments));
			return new SingleLongTerm(GetIntValue(exm, arguments));
		}
	}

	private sealed class LogMethod : FunctionMethod
	{
		readonly double Base;
		public LogMethod()
		{
			ReturnType = EraType.Integer;
			CanReturnFloat = true;
			argumentTypeArrayEx = [
				new ArgTypeList{ ArgTypes = { ArgType.Any } }
			];
			Base = Math.E;
			CanRestructure = true;
		}
		public LogMethod(double b)
		{
			ReturnType = EraType.Integer;
			CanReturnFloat = true;
			argumentTypeArrayEx = [
				new ArgTypeList{ ArgTypes = { ArgType.Any } }
			];
			Base = b;
			CanRestructure = true;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			double ret = ToDouble(arguments[0], exm);
			if (ret <= 0)
				throw new CodeEE(string.Format(trerror.ArgIsNotMoreThan0.Text, Name, 1, ret));
			double dret;
			if (Base == Math.E)
				dret = Math.Log(ret);
			else
				dret = Math.Log10(ret);
			if (double.IsNaN(dret))
				throw new CodeEE(string.Format(trerror.ResultIsNaN.Text, Name));
			else if (double.IsInfinity(dret))
				throw new CodeEE(string.Format(trerror.ResultIsInfinity.Text, Name));
			else if ((dret >= long.MaxValue) || (dret <= long.MinValue))
				throw new CodeEE(string.Format(trerror.ResultIsOutOfTheRangeOfInt64.Text, Name, dret));
			return (long)dret;
		}
		public override double GetFloatValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			double ret = ToDouble(arguments[0], exm);
			if (ret <= 0)
				throw new CodeEE(string.Format(trerror.ArgIsNotMoreThan0.Text, Name, 1, ret));
			double dret;
			if (Base == Math.E)
				dret = Math.Log(ret);
			else
				dret = Math.Log10(ret);
			if (double.IsNaN(dret))
				throw new CodeEE(string.Format(trerror.ResultIsNaN.Text, Name));
			else if (double.IsInfinity(dret))
				throw new CodeEE(string.Format(trerror.ResultIsInfinity.Text, Name));
			return dret;
		}
		public override SingleTerm GetReturnValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			if (HasFloatArg(arguments))
				return new SingleFloatTerm(GetFloatValue(exm, arguments));
			return new SingleLongTerm(GetIntValue(exm, arguments));
		}
	}

	private sealed class ExpMethod : FunctionMethod
	{
		public ExpMethod()
		{
			ReturnType = EraType.Integer;
			CanReturnFloat = true;
			argumentTypeArrayEx = [
				new ArgTypeList{ ArgTypes = { ArgType.Any } }
			];
			CanRestructure = true;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			double dret = Math.Exp(ToDouble(arguments[0], exm));
			if (double.IsNaN(dret))
				throw new CodeEE(string.Format(trerror.ResultIsNaN.Text, Name));
			else if (double.IsInfinity(dret))
				throw new CodeEE(string.Format(trerror.ResultIsInfinity.Text, Name));
			else if ((dret >= long.MaxValue) || (dret <= long.MinValue))
				throw new CodeEE(string.Format(trerror.ResultIsOutOfTheRangeOfInt64.Text, Name, dret));
			return (long)dret;
		}
		public override double GetFloatValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			double dret = Math.Exp(ToDouble(arguments[0], exm));
			if (double.IsNaN(dret))
				throw new CodeEE(string.Format(trerror.ResultIsNaN.Text, Name));
			else if (double.IsInfinity(dret))
				throw new CodeEE(string.Format(trerror.ResultIsInfinity.Text, Name));
			return dret;
		}
		public override SingleTerm GetReturnValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			if (HasFloatArg(arguments))
				return new SingleFloatTerm(GetFloatValue(exm, arguments));
			return new SingleLongTerm(GetIntValue(exm, arguments));
		}
	}

	private sealed class SignMethod : FunctionMethod
	{

		public SignMethod()
		{
			ReturnType = EraType.Integer;
			CanReturnFloat = true;
			argumentTypeArrayEx = [
				new ArgTypeList{ ArgTypes = { ArgType.Any } }
			];
			CanRestructure = true;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			return (long)Math.Sign(ToDouble(arguments[0], exm));
		}
		public override double GetFloatValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			return Math.Sign(ToDouble(arguments[0], exm));
		}
		public override SingleTerm GetReturnValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			if (HasFloatArg(arguments))
				return new SingleFloatTerm(GetFloatValue(exm, arguments));
			return new SingleLongTerm(GetIntValue(exm, arguments));
		}
	}

	private sealed class GetLimitMethod : FunctionMethod
	{
		public GetLimitMethod()
		{
			ReturnType = EraType.Integer;
			CanReturnFloat = true;
			argumentTypeArrayEx = [
				new ArgTypeList{ ArgTypes = { ArgType.Any, ArgType.Any, ArgType.Any } }
			];
			CanRestructure = true;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			double value = ToDouble(arguments[0], exm);
			double min = ToDouble(arguments[1], exm);
			double max = ToDouble(arguments[2], exm);
			if (value < min)
				return (long)min;
			if (value > max)
				return (long)max;
			return (long)value;
		}
		public override double GetFloatValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			double value = ToDouble(arguments[0], exm);
			double min = ToDouble(arguments[1], exm);
			double max = ToDouble(arguments[2], exm);
			if (value < min)
				return min;
			if (value > max)
				return max;
			return value;
		}
		public override SingleTerm GetReturnValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			if (HasFloatArg(arguments))
				return new SingleFloatTerm(GetFloatValue(exm, arguments));
			return new SingleLongTerm(GetIntValue(exm, arguments));
		}
	}

	private static double ToDouble(AExpression expr, ExpressionMediator exm)
	{
		return expr.GetEraType() == EraType.Integer ? expr.GetIntValue(exm) : expr.GetFloatValue(exm);
	}

	private static bool HasFloatArg(List<AExpression> arguments)
	{
		foreach (var arg in arguments)
		{
			if (arg != null && arg.GetEraType() == EraType.Float)
				return true;
		}
		return false;
	}

	private sealed class SinMethod : FunctionMethod
	{
		public SinMethod()
		{
			ReturnType = EraType.Integer;
			CanReturnFloat = true;
			argumentTypeArrayEx = [
				new ArgTypeList{ ArgTypes = { ArgType.Any } }
			];
			CanRestructure = true;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			double dret = Math.Sin(ToDouble(arguments[0], exm));
			if (double.IsNaN(dret))
				throw new CodeEE(string.Format(trerror.ResultIsNaN.Text, Name));
			else if (double.IsInfinity(dret))
				throw new CodeEE(string.Format(trerror.ResultIsInfinity.Text, Name));
			else if ((dret >= long.MaxValue) || (dret <= long.MinValue))
				throw new CodeEE(string.Format(trerror.ResultIsOutOfTheRangeOfInt64.Text, Name, dret));
			return (long)dret;
		}
		public override double GetFloatValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			double dret = Math.Sin(ToDouble(arguments[0], exm));
			if (double.IsNaN(dret))
				throw new CodeEE(string.Format(trerror.ResultIsNaN.Text, Name));
			else if (double.IsInfinity(dret))
				throw new CodeEE(string.Format(trerror.ResultIsInfinity.Text, Name));
			return dret;
		}
		public override SingleTerm GetReturnValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			if (HasFloatArg(arguments))
				return new SingleFloatTerm(GetFloatValue(exm, arguments));
			return new SingleLongTerm(GetIntValue(exm, arguments));
		}
	}

	private sealed class CosMethod : FunctionMethod
	{
		public CosMethod()
		{
			ReturnType = EraType.Integer;
			CanReturnFloat = true;
			argumentTypeArrayEx = [
				new ArgTypeList{ ArgTypes = { ArgType.Any } }
			];
			CanRestructure = true;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			double dret = Math.Cos(ToDouble(arguments[0], exm));
			if (double.IsNaN(dret))
				throw new CodeEE(string.Format(trerror.ResultIsNaN.Text, Name));
			else if (double.IsInfinity(dret))
				throw new CodeEE(string.Format(trerror.ResultIsInfinity.Text, Name));
			else if ((dret >= long.MaxValue) || (dret <= long.MinValue))
				throw new CodeEE(string.Format(trerror.ResultIsOutOfTheRangeOfInt64.Text, Name, dret));
			return (long)dret;
		}
		public override double GetFloatValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			double dret = Math.Cos(ToDouble(arguments[0], exm));
			if (double.IsNaN(dret))
				throw new CodeEE(string.Format(trerror.ResultIsNaN.Text, Name));
			else if (double.IsInfinity(dret))
				throw new CodeEE(string.Format(trerror.ResultIsInfinity.Text, Name));
			return dret;
		}
		public override SingleTerm GetReturnValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			if (HasFloatArg(arguments))
				return new SingleFloatTerm(GetFloatValue(exm, arguments));
			return new SingleLongTerm(GetIntValue(exm, arguments));
		}
	}

	private sealed class TanMethod : FunctionMethod
	{
		public TanMethod()
		{
			ReturnType = EraType.Integer;
			CanReturnFloat = true;
			argumentTypeArrayEx = [
				new ArgTypeList{ ArgTypes = { ArgType.Any } }
			];
			CanRestructure = true;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			double dret = Math.Tan(ToDouble(arguments[0], exm));
			if (double.IsNaN(dret))
				throw new CodeEE(string.Format(trerror.ResultIsNaN.Text, Name));
			else if (double.IsInfinity(dret))
				throw new CodeEE(string.Format(trerror.ResultIsInfinity.Text, Name));
			else if ((dret >= long.MaxValue) || (dret <= long.MinValue))
				throw new CodeEE(string.Format(trerror.ResultIsOutOfTheRangeOfInt64.Text, Name, dret));
			return (long)dret;
		}
		public override double GetFloatValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			double dret = Math.Tan(ToDouble(arguments[0], exm));
			if (double.IsNaN(dret))
				throw new CodeEE(string.Format(trerror.ResultIsNaN.Text, Name));
			else if (double.IsInfinity(dret))
				throw new CodeEE(string.Format(trerror.ResultIsInfinity.Text, Name));
			return dret;
		}
		public override SingleTerm GetReturnValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			if (HasFloatArg(arguments))
				return new SingleFloatTerm(GetFloatValue(exm, arguments));
			return new SingleLongTerm(GetIntValue(exm, arguments));
		}
	}

	private sealed class AsinMethod : FunctionMethod
	{
		public AsinMethod()
		{
			ReturnType = EraType.Integer;
			CanReturnFloat = true;
			argumentTypeArrayEx = [
				new ArgTypeList{ ArgTypes = { ArgType.Any } }
			];
			CanRestructure = true;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			double ret = ToDouble(arguments[0], exm);
			if (ret < -1.0 || ret > 1.0)
				throw new CodeEE(string.Format(trerror.ArgIsOutOfRange.Text, Name, 1, ret, -1, 1));
			double dret = Math.Asin(ret);
			if (double.IsNaN(dret))
				throw new CodeEE(string.Format(trerror.ResultIsNaN.Text, Name));
			return (long)dret;
		}
		public override double GetFloatValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			double ret = ToDouble(arguments[0], exm);
			if (ret < -1.0 || ret > 1.0)
				throw new CodeEE(string.Format(trerror.ArgIsOutOfRange.Text, Name, 1, ret, -1, 1));
			double dret = Math.Asin(ret);
			if (double.IsNaN(dret))
				throw new CodeEE(string.Format(trerror.ResultIsNaN.Text, Name));
			return dret;
		}
		public override SingleTerm GetReturnValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			if (HasFloatArg(arguments))
				return new SingleFloatTerm(GetFloatValue(exm, arguments));
			return new SingleLongTerm(GetIntValue(exm, arguments));
		}
	}

	private sealed class AcosMethod : FunctionMethod
	{
		public AcosMethod()
		{
			ReturnType = EraType.Integer;
			CanReturnFloat = true;
			argumentTypeArrayEx = [
				new ArgTypeList{ ArgTypes = { ArgType.Any } }
			];
			CanRestructure = true;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			double ret = ToDouble(arguments[0], exm);
			if (ret < -1.0 || ret > 1.0)
				throw new CodeEE(string.Format(trerror.ArgIsOutOfRange.Text, Name, 1, ret, -1, 1));
			double dret = Math.Acos(ret);
			if (double.IsNaN(dret))
				throw new CodeEE(string.Format(trerror.ResultIsNaN.Text, Name));
			return (long)dret;
		}
		public override double GetFloatValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			double ret = ToDouble(arguments[0], exm);
			if (ret < -1.0 || ret > 1.0)
				throw new CodeEE(string.Format(trerror.ArgIsOutOfRange.Text, Name, 1, ret, -1, 1));
			double dret = Math.Acos(ret);
			if (double.IsNaN(dret))
				throw new CodeEE(string.Format(trerror.ResultIsNaN.Text, Name));
			return dret;
		}
		public override SingleTerm GetReturnValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			if (HasFloatArg(arguments))
				return new SingleFloatTerm(GetFloatValue(exm, arguments));
			return new SingleLongTerm(GetIntValue(exm, arguments));
		}
	}

	private sealed class AtanMethod : FunctionMethod
	{
		public AtanMethod()
		{
			ReturnType = EraType.Integer;
			CanReturnFloat = true;
			argumentTypeArrayEx = [
				new ArgTypeList{ ArgTypes = { ArgType.Any } }
			];
			CanRestructure = true;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			double dret = Math.Atan(ToDouble(arguments[0], exm));
			if (double.IsNaN(dret))
				throw new CodeEE(string.Format(trerror.ResultIsNaN.Text, Name));
			return (long)dret;
		}
		public override double GetFloatValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			double dret = Math.Atan(ToDouble(arguments[0], exm));
			if (double.IsNaN(dret))
				throw new CodeEE(string.Format(trerror.ResultIsNaN.Text, Name));
			return dret;
		}
		public override SingleTerm GetReturnValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			if (HasFloatArg(arguments))
				return new SingleFloatTerm(GetFloatValue(exm, arguments));
			return new SingleLongTerm(GetIntValue(exm, arguments));
		}
	}

	private sealed class FloorMethod : FunctionMethod
	{
		public FloorMethod()
		{
			ReturnType = EraType.Integer;
			CanReturnFloat = true;
			argumentTypeArrayEx = [
				new ArgTypeList{ ArgTypes = { ArgType.Any } }
			];
			CanRestructure = true;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			double dret = Math.Floor(ToDouble(arguments[0], exm));
			if ((dret >= long.MaxValue) || (dret <= long.MinValue))
				throw new CodeEE(string.Format(trerror.ResultIsOutOfTheRangeOfInt64.Text, Name, dret));
			return (long)dret;
		}
		public override double GetFloatValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			return Math.Floor(ToDouble(arguments[0], exm));
		}
		public override SingleTerm GetReturnValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			if (HasFloatArg(arguments))
				return new SingleFloatTerm(GetFloatValue(exm, arguments));
			return new SingleLongTerm(GetIntValue(exm, arguments));
		}
	}

	private sealed class CeilMethod : FunctionMethod
	{
		public CeilMethod()
		{
			ReturnType = EraType.Integer;
			CanReturnFloat = true;
			argumentTypeArrayEx = [
				new ArgTypeList{ ArgTypes = { ArgType.Any } }
			];
			CanRestructure = true;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			double dret = Math.Ceiling(ToDouble(arguments[0], exm));
			if ((dret >= long.MaxValue) || (dret <= long.MinValue))
				throw new CodeEE(string.Format(trerror.ResultIsOutOfTheRangeOfInt64.Text, Name, dret));
			return (long)dret;
		}
		public override double GetFloatValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			return Math.Ceiling(ToDouble(arguments[0], exm));
		}
		public override SingleTerm GetReturnValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			if (HasFloatArg(arguments))
				return new SingleFloatTerm(GetFloatValue(exm, arguments));
			return new SingleLongTerm(GetIntValue(exm, arguments));
		}
	}

	private sealed class RoundMethod : FunctionMethod
	{
		public RoundMethod()
		{
			ReturnType = EraType.Integer;
			CanReturnFloat = true;
			argumentTypeArrayEx = [
				new ArgTypeList{ ArgTypes = { ArgType.Any } }
			];
			CanRestructure = true;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			double dret = Math.Round(ToDouble(arguments[0], exm), MidpointRounding.AwayFromZero);
			if ((dret >= long.MaxValue) || (dret <= long.MinValue))
				throw new CodeEE(string.Format(trerror.ResultIsOutOfTheRangeOfInt64.Text, Name, dret));
			return (long)dret;
		}
		public override double GetFloatValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			return Math.Round(ToDouble(arguments[0], exm), MidpointRounding.AwayFromZero);
		}
		public override SingleTerm GetReturnValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			if (HasFloatArg(arguments))
				return new SingleFloatTerm(GetFloatValue(exm, arguments));
			return new SingleLongTerm(GetIntValue(exm, arguments));
		}
	}

	private sealed class UncheckedAddMethod : FunctionMethod
	{
		public UncheckedAddMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = [EraType.Integer, EraType.Integer];
			CanRestructure = true;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			long a = arguments[0].GetIntValue(exm);
			long b = arguments[1].GetIntValue(exm);
			return unchecked(a + b);
		}
	}

	private sealed class UncheckedSubtractMethod : FunctionMethod
	{
		public UncheckedSubtractMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = [EraType.Integer, EraType.Integer];
			CanRestructure = true;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			long a = arguments[0].GetIntValue(exm);
			long b = arguments[1].GetIntValue(exm);
			return unchecked(a - b);
		}
	}

	private sealed class UncheckedMultiplyMethod : FunctionMethod
	{
		public UncheckedMultiplyMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = [EraType.Integer, EraType.Integer];
			CanRestructure = true;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			long a = arguments[0].GetIntValue(exm);
			long b = arguments[1].GetIntValue(exm);
			return unchecked(a * b);
		}
	}

	private sealed class UncheckedNegateMethod : FunctionMethod
	{
		public UncheckedNegateMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = [EraType.Integer];
			CanRestructure = true;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			long a = arguments[0].GetIntValue(exm);
			return unchecked(-a);
		}
	}
	#endregion

	#region 変数操作系
	private sealed class SumArrayMethod : FunctionMethod
	{
		readonly bool isCharaRange;
		public SumArrayMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArrayEx = [
					new ArgTypeList{ ArgTypes = { ArgType.RefAnyArray, ArgType.Int, ArgType.Int }, OmitStart = 1 },
				];
			isCharaRange = false;
			CanRestructure = false;
		}
		public SumArrayMethod(bool isChara)
		{
			ReturnType = EraType.Integer;
			argumentTypeArrayEx = [
					new ArgTypeList{ ArgTypes = { ArgType.CharacterData | ArgType.RefAnyArray | ArgType.AllowConstRef, ArgType.Int, ArgType.Int }, OmitStart = 1 }
				];
			isCharaRange = isChara;
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			VariableTerm varTerm = (VariableTerm)arguments[0];
			long index1 = (arguments.Count >= 2 && arguments[1] != null) ? arguments[1].GetIntValue(exm) : 0;
			long index2 = (arguments.Count == 3 && arguments[2] != null) ? arguments[2].GetIntValue(exm) : (isCharaRange ? exm.VEvaluator.CHARANUM : varTerm.GetLastLength());

			FixedVariableTerm p = varTerm.GetFixedVariableTerm(exm);
			if (!isCharaRange)
			{
				p.IsArrayRangeValid(index1, index2, "SUMARRAY", 2L, 3L);
				return VariableEvaluator.GetArraySum(p, index1, index2);
			}
			else
			{
				long charaNum = exm.VEvaluator.CHARANUM;
				if (index1 >= charaNum || index1 < 0 || index2 > charaNum || index2 < 0)
					throw new CodeEE(string.Format(trerror.CharacterRangeInvalid.Text, Name, index1, index2));
				return VariableEvaluator.GetArraySumChara(p, index1, index2);
			}
		}
		public override double GetFloatValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			VariableTerm varTerm = (VariableTerm)arguments[0];
			long index1 = (arguments.Count >= 2 && arguments[1] != null) ? arguments[1].GetIntValue(exm) : 0;
			long index2 = (arguments.Count == 3 && arguments[2] != null) ? arguments[2].GetIntValue(exm) : (isCharaRange ? exm.VEvaluator.CHARANUM : varTerm.GetLastLength());

			FixedVariableTerm p = varTerm.GetFixedVariableTerm(exm);
			if (!isCharaRange)
			{
				p.IsArrayRangeValid(index1, index2, "SUMARRAY", 2L, 3L);
				return VariableEvaluator.GetArraySumDouble(p, index1, index2);
			}
			else
			{
				long charaNum = exm.VEvaluator.CHARANUM;
				if (index1 >= charaNum || index1 < 0 || index2 > charaNum || index2 < 0)
					throw new CodeEE(string.Format(trerror.CharacterRangeInvalid.Text, Name, index1, index2));
				return VariableEvaluator.GetArraySumCharaDouble(p, index1, index2);
			}
		}
		public override SingleTerm GetReturnValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			VariableTerm varTerm = (VariableTerm)arguments[0];
			if (varTerm.Identifier.GetEraType() == EraType.Float)
				return new SingleFloatTerm(GetFloatValue(exm, arguments));
			return new SingleLongTerm(GetIntValue(exm, arguments));
		}
	}

	private sealed class MatchMethod : FunctionMethod
	{
		readonly bool isCharaRange;
		public MatchMethod()
		{
			ReturnType = EraType.Integer;
			// argumentTypeArray = null;
			argumentTypeArrayEx = [
					new ArgTypeList{ ArgTypes = { ArgType.RefAny1D | ArgType.AllowConstRef, ArgType.SameAsFirst, ArgType.Int, ArgType.Int }, OmitStart = 2 },
				];
			isCharaRange = false;
			CanRestructure = false;
			HasUniqueRestructure = true;
		}
		public MatchMethod(bool isChara)
		{
			ReturnType = EraType.Integer;
			// argumentTypeArray = null;
			argumentTypeArrayEx = [
					//new ArgTypeList{ ArgTypes = { ArgType.CharacterData | ArgType.RefAny1D | ArgType.AllowConstRef | ArgType.Any, ArgType.SameAsFirst, ArgType.Int, ArgType.Int }, OmitStart = 2 },
					new ArgTypeList{ ArgTypes = { ArgType.Any, ArgType.SameAsFirst, ArgType.Int, ArgType.Int }, OmitStart = 2 },
				];
			isCharaRange = isChara;
			CanRestructure = false;
			HasUniqueRestructure = true;
		}
		//public override string CheckArgumentType(string name, IOperandTerm[] arguments)
		//{
		//	if (arguments.Count < 2)
		//		return name + "関数には少なくとも2つの引数が必要です";
		//	if (arguments.Count > 4)
		//		return name + "関数の引数が多すぎます";
		//	if (arguments[0] == null)
		//		return name + "関数の1番目の引数は省略できません";
		//	if (!(arguments[0] is VariableTerm))
		//		return name + "関数の1番目の引数が変数ではありません";
		//	VariableTerm varToken = (VariableTerm)arguments[0];
		//	if (isCharaRange && !varToken.Identifier.IsCharacterData)
		//		return name + "関数の1番目の引数がキャラクタ変数ではありません";
		//	if (!isCharaRange && (varToken.Identifier.IsArray2D || varToken.Identifier.IsArray3D))
		//		return name + "関数は二重配列・三重配列には対応していません";
		//	if (!isCharaRange && !varToken.Identifier.IsArray1D)
		//		return name + "関数の1番目の引数が配列変数ではありません";
		//	if (arguments[1] == null)
		//		return name + "関数の2番目の引数は省略できません";
		//	if (arguments[1].GetOperandType() != arguments[0].GetOperandType())
		//		return name + "関数の1番目の引数と2番目の引数の型が異なります";
		//	if ((arguments.Count >= 3) && (arguments[2] != null) && (arguments[2].GetOperandType() != EraType.Integer))
		//		return name + "関数の3番目の引数の型が正しくありません";
		//	if ((arguments.Count >= 4) && (arguments[3] != null) && (arguments[3].GetOperandType() != EraType.Integer))
		//		return name + "関数の4番目の引数の型が正しくありません";
		//	return null;
		//}

		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			VariableTerm varTerm = arguments[0] as VariableTerm;
			long start = (arguments.Count > 2 && arguments[2] != null) ? arguments[2].GetIntValue(exm) : 0;
			long end = (arguments.Count > 3 && arguments[3] != null) ? arguments[3].GetIntValue(exm) : (isCharaRange ? exm.VEvaluator.CHARANUM : varTerm.GetLength());

			FixedVariableTerm p = varTerm.GetFixedVariableTerm(exm);
			if (!isCharaRange)
			{
				p.IsArrayRangeValid(start, end, "MATCH", 3L, 4L);
				if (arguments[0].GetEraType() == EraType.Integer)
				{
					long targetValue = arguments[1].GetIntValue(exm);
					return VariableEvaluator.GetMatch(p, targetValue, start, end);
				}
				else if (arguments[0].GetEraType() == EraType.Float)
				{
					double targetValue = arguments[1].GetFloatValue(exm);
					return VariableEvaluator.GetMatch(p, targetValue, start, end);
				}
				else
				{
					string targetStr = arguments[1].GetStrValue(exm);
					return VariableEvaluator.GetMatch(p, targetStr, start, end);
				}
			}
			else
			{
				long charaNum = exm.VEvaluator.CHARANUM;
				if (start >= charaNum || start < 0 || end > charaNum || end < 0)
					throw new CodeEE(string.Format(trerror.CharacterRangeInvalid.Text, Name, start, end));
				if (arguments[0].GetEraType() == EraType.Integer)
				{
					long targetValue = arguments[1].GetIntValue(exm);
					return VariableEvaluator.GetMatchChara(p, targetValue, start, end);
				}
				else if (arguments[0].GetEraType() == EraType.Float)
				{
					double targetValue = arguments[1].GetFloatValue(exm);
					return VariableEvaluator.GetMatchChara(p, targetValue, start, end);
				}
				else
				{
					string targetStr = arguments[1].GetStrValue(exm);
					return VariableEvaluator.GetMatchChara(p, targetStr, start, end);
				}
			}
		}

		public override bool UniqueRestructure(ExpressionMediator exm, List<AExpression> arguments)
		{
			arguments[0].Restructure(exm);
			for (int i = 1; i < arguments.Count; i++)
			{
				if (arguments[i] == null)
					continue;
				arguments[i] = arguments[i].Restructure(exm);
			}
			return false;
		}
	}

	private sealed class MatchAllMethod : FunctionMethod
	{
		private readonly bool _useStringName;

		public MatchAllMethod(bool useStringName)
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = null;
			CanRestructure = false;
			_useStringName = useStringName;
		}

		public override string CheckArgumentType(string name, List<AExpression> arguments)
		{
			if (arguments.Count < 2)
				return name + "関数には少なくとも2つの引数が必要です";
			if (arguments.Count > 5)
				return name + "関数の引数が多すぎます";

			if (_useStringName)
			{
				if (arguments[0] is not SingleStrTerm)
					return name + "関数の1番目の引数は文字列である必要があります";
			}
			else
			{
				if (arguments[0] is not VariableTerm)
					return name + "関数の1番目の引数は変数参照である必要があります";
			}

			if (arguments.Count >= 5 && arguments[4] is not VariableTerm)
				return name + "関数の5番目の引数は変数参照である必要があります";

			return null;
		}

		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			VariableToken token;
			if (_useStringName)
			{
				var varName = ((SingleStrTerm)arguments[0]).Str;
				token = GlobalStatic.IdentifierDictionary.GetVariableToken(varName, null, true);
				if (token == null)
					throw new CodeEE("変数 " + varName + " が見つかりません");
			}
			else
			{
				token = ((VariableTerm)arguments[0]).Identifier;
			}

			var valExpr = arguments[1];
			var type = valExpr.GetOperandType();

			long beg = 0;
			if (arguments.Count >= 3 && arguments[2] != null)
				beg = arguments[2].GetIntValue(exm);

			long len;
			if (token.IsCharacterData)
			{
				len = exm.VEvaluator.CHARANUM;
			}
			else
			{
				if (token.IsArray1D)
					len = token.GetLength(0);
				else
					len = 1;
			}

			long end = len;
			if (arguments.Count >= 4 && arguments[3] != null)
				end = arguments[3].GetIntValue(exm);

			if (beg < 0 || end < 0)
				throw new CodeEE("検索範囲に負の値が渡されました");
			if (beg > end)
				throw new CodeEE("検索範囲の指定が不正です");
			if (end > len)
				end = len;

			VariableTerm outArr = null;
			if (arguments.Count >= 5 && arguments[4] is VariableTerm vt)
				outArr = vt;

			var idxs = new long[2];
			int p = 0;
			long count = 0;

			if (type == typeof(long))
			{
				var val = valExpr.GetIntValue(exm);
				for (var i = beg; i < end; i++)
				{
					idxs[p] = i;
					if (val == token.GetIntValue(exm, idxs))
					{
						if (outArr != null)
						{
							var outLen = outArr.Identifier.GetLength(0);
							if (count < outLen)
								outArr.Identifier.SetValue(i, [count]);
						}
						count++;
					}
				}
			}
			else if (type == typeof(string))
			{
				var val = valExpr.GetStrValue(exm);
				for (var i = beg; i < end; i++)
				{
					idxs[p] = i;
					if (val == token.GetStrValue(exm, idxs))
					{
						if (outArr != null)
						{
							var outLen = outArr.Identifier.GetLength(0);
							if (count < outLen)
								outArr.Identifier.SetValue(i, [count]);
						}
						count++;
					}
				}
			}
			else
			{
				throw new ExeEE("MATCHALL: サポートされていない型です");
			}

			return count;
		}
	}

	private sealed class GroupMatchMethod : FunctionMethod
	{
		public GroupMatchMethod()
		{
			ReturnType = EraType.Integer;
			// argumentTypeArray = null;
			argumentTypeArrayEx = [
					new ArgTypeList{ ArgTypes = { ArgType.Any, ArgType.VariadicSameAsFirst } },
				];
			CanRestructure = false;
		}
		//public override string CheckArgumentType(string name, IOperandTerm[] arguments)
		//{
		//	if (arguments.Count < 2)
		//		return name + "関数には少なくとも2つの引数が必要です";
		//	if (arguments[0] == null)
		//		return name + "関数の1番目の引数は省略できません";
		//	Type baseType = arguments[0].GetOperandType();
		//	for (int i = 1; i < arguments.Count; i++)
		//	{
		//		if (arguments[i] == null)
		//			return name + "関数の" + (i + 1).ToString() + "番目の引数は省略できません";
		//		if (arguments[i].GetOperandType() != baseType)
		//			return name + "関数の" + (i + 1).ToString() + "番目の引数の型が正しくありません";
		//	}
		//	return null;
		//}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			long ret = 0;
			if (arguments[0].GetEraType() == EraType.Integer)
			{
				long baseValue = arguments[0].GetIntValue(exm);
				for (int i = 1; i < arguments.Count; i++)
				{
					if (baseValue == arguments[i].GetIntValue(exm))
						ret += 1;
				}
			}
			else if (arguments[0].GetEraType() == EraType.Float)
			{
				double baseValue = arguments[0].GetFloatValue(exm);
				for (int i = 1; i < arguments.Count; i++)
				{
					if (baseValue == arguments[i].GetFloatValue(exm))
						ret += 1;
				}
			}
			else
			{
				string baseString = arguments[0].GetStrValue(exm);
				for (int i = 1; i < arguments.Count; i++)
				{
					if (baseString == arguments[i].GetStrValue(exm))
						ret += 1;
				}
			}
			return ret;
		}
	}

	private sealed class NosamesMethod : FunctionMethod
	{
		public NosamesMethod()
		{
			ReturnType = EraType.Integer;
			// argumentTypeArray = null;
			argumentTypeArrayEx = [
					new ArgTypeList{ ArgTypes = { ArgType.Any, ArgType.VariadicSameAsFirst } },
				];
			CanRestructure = false;
		}
		//public override string CheckArgumentType(string name, IOperandTerm[] arguments)
		//{
		//	if (arguments.Count < 2)
		//		return name + "関数には少なくとも2つの引数が必要です";
		//	if (arguments[0] == null)
		//		return name + "関数の1番目の引数は省略できません";
		//	Type baseType = arguments[0].GetOperandType();
		//	for (int i = 1; i < arguments.Count; i++)
		//	{
		//		if (arguments[i] == null)
		//			return name + "関数の" + (i + 1).ToString() + "番目の引数は省略できません";
		//		if (arguments[i].GetOperandType() != baseType)
		//			return name + "関数の" + (i + 1).ToString() + "番目の引数の型が正しくありません";
		//	}
		//	return null;
		//}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			if (arguments[0].GetEraType() == EraType.Integer)
			{
				long[] valueArray = new long[arguments.Count];
				for (int i = 0; i < arguments.Count; i++)
				{
					valueArray[i] = arguments[i].GetIntValue(exm);
				}
				var resultArray = valueArray.Distinct();
				if (resultArray.Count() != arguments.Count)
					return 0L;
			}
			else if (arguments[0].GetEraType() == EraType.Float)
			{
				double[] valueArray = new double[arguments.Count];
				for (int i = 0; i < arguments.Count; i++)
				{
					valueArray[i] = arguments[i].GetFloatValue(exm);
				}
				var resultArray = valueArray.Distinct();
				if (resultArray.Count() != arguments.Count)
					return 0L;
			}
			else
			{
				string[] stringArray = new string[arguments.Count];
				for (int i = 0; i < arguments.Count; i++)
				{
					stringArray[i] = arguments[i].GetStrValue(exm);
				}
				var resultArray = stringArray.Distinct();
				if (resultArray.Count() != arguments.Count)
					return 0L;
			}
			return 1L;
		}
	}

	private sealed class AllsamesMethod : FunctionMethod
	{
		public AllsamesMethod()
		{
			ReturnType = EraType.Integer;
			// argumentTypeArray = null;
			argumentTypeArrayEx = [
					new ArgTypeList{ ArgTypes = { ArgType.Any, ArgType.VariadicSameAsFirst } },
				];
			CanRestructure = false;
		}
		//public override string CheckArgumentType(string name, IOperandTerm[] arguments)
		//{
		//	if (arguments.Count < 2)
		//		return name + "関数には少なくとも2つの引数が必要です";
		//	if (arguments[0] == null)
		//		return name + "関数の1番目の引数は省略できません";
		//	Type baseType = arguments[0].GetOperandType();
		//	for (int i = 1; i < arguments.Count; i++)
		//	{
		//		if (arguments[i] == null)
		//			return name + "関数の" + (i + 1).ToString() + "番目の引数は省略できません";
		//		if (arguments[i].GetOperandType() != baseType)
		//			return name + "関数の" + (i + 1).ToString() + "番目の引数の型が正しくありません";
		//	}
		//	return null;
		//}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			if (arguments[0].GetEraType() == EraType.Integer)
			{
				long baseValue = arguments[0].GetIntValue(exm);
				for (int i = 1; i < arguments.Count; i++)
				{
					if (baseValue != arguments[i].GetIntValue(exm))
						return 0L;
				}
			}
			else if (arguments[0].GetEraType() == EraType.Float)
			{
				double baseValue = arguments[0].GetFloatValue(exm);
				for (int i = 1; i < arguments.Count; i++)
				{
					if (baseValue != arguments[i].GetFloatValue(exm))
						return 0L;
				}
			}
			else
			{
				string baseValue = arguments[0].GetStrValue(exm);
				for (int i = 1; i < arguments.Count; i++)
				{
					if (baseValue != arguments[i].GetStrValue(exm))
						return 0L;
				}
			}
			return 1L;
		}
	}

	private sealed class MaxArrayMethod : FunctionMethod
	{
		readonly bool isCharaRange;
		readonly bool isMax;
		readonly string funcName;
		public MaxArrayMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArrayEx = [
					new ArgTypeList{ ArgTypes = { ArgType.RefAny1D | ArgType.AllowConstRef, ArgType.Int, ArgType.Int }, OmitStart = 1 },
				];
			isCharaRange = false;
			isMax = true;
			funcName = "MAXARRAY";
			CanRestructure = false;
		}
		public MaxArrayMethod(bool isChara)
		{
			ReturnType = EraType.Integer;
			argumentTypeArrayEx = [
					new ArgTypeList{ ArgTypes = { ArgType.CharacterData | ArgType.RefAny1D | ArgType.AllowConstRef, ArgType.Int, ArgType.Int }, OmitStart = 1 },
				];
			isCharaRange = isChara;
			isMax = true;
			if (isCharaRange)
				funcName = "MAXCARRAY";
			else
				funcName = "MAXARRAY";
			CanRestructure = false;
		}
		public MaxArrayMethod(bool isChara, bool isMaxFunc)
		{
			ReturnType = EraType.Integer;
			argumentTypeArrayEx = isChara
				? [
						new ArgTypeList{ ArgTypes = { ArgType.CharacterData | ArgType.RefAny1D | ArgType.AllowConstRef, ArgType.Int, ArgType.Int }, OmitStart = 1 },
				]
				: [
						new ArgTypeList{ ArgTypes = { ArgType.RefAny1D | ArgType.AllowConstRef, ArgType.Int, ArgType.Int }, OmitStart = 1 },
				];
			isCharaRange = isChara;
			isMax = isMaxFunc;
			funcName = (isMax ? "MAX" : "MIN") + (isCharaRange ? "C" : "") + "ARRAY";
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			VariableTerm vTerm = (VariableTerm)arguments[0];
			long start = (arguments.Count > 1 && arguments[1] != null) ? arguments[1].GetIntValue(exm) : 0;
			long end = (arguments.Count > 2 && arguments[2] != null) ? arguments[2].GetIntValue(exm) : (isCharaRange ? exm.VEvaluator.CHARANUM : vTerm.GetLength());
			FixedVariableTerm p = vTerm.GetFixedVariableTerm(exm);
			if (!isCharaRange)
			{
				p.IsArrayRangeValid(start, end, funcName, 2L, 3L);
				if (vTerm.Identifier.GetEraType() == EraType.Float)
					return (long)VariableEvaluator.GetMaxArrayDouble(p, start, end, isMax);
				return VariableEvaluator.GetMaxArray(p, start, end, isMax);
			}
			else
			{
				long charaNum = exm.VEvaluator.CHARANUM;
				if (start >= charaNum || start < 0 || end > charaNum || end < 0)
					throw new CodeEE(string.Format(trerror.CharacterRangeInvalid.Text, funcName, start, end));
				if (vTerm.Identifier.GetEraType() == EraType.Float)
					return (long)VariableEvaluator.GetMaxArrayCharaDouble(p, start, end, isMax);
				return VariableEvaluator.GetMaxArrayChara(p, start, end, isMax);
			}
		}
	}

	private sealed class GetbitMethod : FunctionMethod
	{
		public GetbitMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = [EraType.Integer, EraType.Integer];
			CanRestructure = true;
		}
		//public override string CheckArgumentType(string name, IOperandTerm[] arguments)
		//{
		//	string ret = base.CheckArgumentType(name, arguments);
		//	if (ret != null)
		//		return ret;
		//	if (arguments[1] is SingleTerm)
		//	{
		//		Int64 m = ((SingleTerm)arguments[1]).Int;
		//		if (m < 0 || m > 63)
		//			return "GETBIT関数の第２引数(" + m.ToString() + ")が範囲(０～６３)を超えています";
		//	}
		//	return null;
		//}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			long n = arguments[0].GetIntValue(exm);
			long m = arguments[1].GetIntValue(exm);
			if ((m < 0) || (m > 63))
				// throw new CodeEE("GETBIT関数の第２引数(" + m.ToString() + ")が範囲(０～６３)を超えています");
				throw new CodeEE(string.Format(trerror.ArgIsOutOfRange.Text, Name, 2, m, 0, 63));
			int mi = (int)m;
			return (n >> mi) & 1;
		}
	}

	private sealed class GetnumMethod : FunctionMethod
	{
		public GetnumMethod()
		{
			ReturnType = EraType.Integer;
			// argumentTypeArray = null;
			argumentTypeArrayEx = [
					new ArgTypeList{ ArgTypes = { ArgType.RefAny | ArgType.AllowConstRef, ArgType.String, ArgType.Int }, OmitStart = 2 },
				];
			CanRestructure = true;
			HasUniqueRestructure = true;
		}
		//public override string CheckArgumentType(string name, IOperandTerm[] arguments)
		//{
		//	if (arguments.Count != 2)
		//		return name + "関数には2つの引数が必要です";
		//	if (arguments[0] == null)
		//		return name + "関数の1番目の引数は省略できません";
		//	if (!(arguments[0] is VariableTerm))
		//		return name + "関数の1番目の引数の型が正しくありません";
		//	if (arguments[1] == null)
		//		return name + "関数の2番目の引数は省略できません";
		//	if (arguments[1].GetEraType() != EraType.String)
		//		return name + "関数の2番目の引数の型が正しくありません";
		//	return null;
		//}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			VariableTerm vToken = (VariableTerm)arguments[0];
			VariableCode varCode = vToken.Identifier.Code;
			string varname = "";
			#region EE_ERD
			if (arguments.Count > 2)
				varname = vToken.Identifier.Name + "@" + arguments[2].GetIntValue(exm);
			else
				varname = vToken.Identifier.Name;
			#endregion
			string key = arguments[1].GetStrValue(exm);
			#region EE_ERD
			// if (exm.VEvaluator.Constant.TryKeywordToInteger(out int ret, varCode, key, -1))
			if (exm.VEvaluator.Constant.TryKeywordToInteger(out int ret, varCode, key, -1, varname))
				#endregion
				return ret;
			else
				return -1;
		}
		public override bool UniqueRestructure(ExpressionMediator exm, List<AExpression> arguments)
		{
			arguments[1] = arguments[1].Restructure(exm);
			return arguments[1] is SingleTerm;
		}
	}

	private sealed class GetnumBMethod : FunctionMethod
	{
		public GetnumBMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = new EraType[] { EraType.String, EraType.String };
			CanRestructure = true;
		}
		/*
		public override string CheckArgumentType(string name, IOperandTerm[] arguments)
		{
			string errStr = base.CheckArgumentType(name, arguments);
			if (errStr != null)
				return errStr;
			if (arguments[0] == null)
				return name + "関数の1番目の引数は省略できません";
			if (arguments[0] is SingleTerm)
			{
				string varName = ((SingleTerm)arguments[0]).Text;
				if (GlobalStatic.IdentifierDictionary.GetVariableToken(varName, null, true) == null)
					return name + "関数の1番目の引数が変数名ではありません";
			}
			return null;
		}
		*/
		public override Int64 GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			VariableToken var = GlobalStatic.IdentifierDictionary.GetVariableToken(arguments[0].GetStrValue(exm), null, true);
			if (var == null)
				throw new CodeEE("GETNUMBの1番目の引数(\"" + arguments[0].GetStrValue(exm) + "\")が変数名ではありません");
			string key = arguments[1].GetStrValue(exm);
			#region EE_ERD
			//GETNUMBは使ってないのでテストしていない
			// if (exm.VEvaluator.Constant.TryKeywordToInteger(out int ret, var.Code, key, -1))
			if (exm.VEvaluator.Constant.TryKeywordToInteger(out int ret, var.Code, key, -1, arguments[0].GetStrValue(exm)))
			#endregion
				return ret;
			else
				return -1;
		}
	}

	private sealed class GetPalamLVMethod : FunctionMethod
	{
		public GetPalamLVMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = [EraType.Integer, EraType.Integer];
			CanRestructure = false;
		}
		//public override string CheckArgumentType(string name, IOperandTerm[] arguments)
		//{
		//	string errStr = base.CheckArgumentType(name, arguments);
		//	if (errStr != null)
		//		return errStr;
		//	if (arguments[0] == null)
		//		return name + "関数の1番目の引数は省略できません";
		//	return null;
		//}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			long value = arguments[0].GetIntValue(exm);
			long maxLv = arguments[1].GetIntValue(exm);

			return exm.VEvaluator.getPalamLv(value, maxLv);
		}
	}

	private sealed class GetExpLVMethod : FunctionMethod
	{
		public GetExpLVMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = [EraType.Integer, EraType.Integer];
			CanRestructure = false;
		}
		//public override string CheckArgumentType(string name, IOperandTerm[] arguments)
		//{
		//	string errStr = base.CheckArgumentType(name, arguments);
		//	if (errStr != null)
		//		return errStr;
		//	if (arguments[0] == null)
		//		return name + "関数の1番目の引数は省略できません";
		//	return null;
		//}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			long value = arguments[0].GetIntValue(exm);
			long maxLv = arguments[1].GetIntValue(exm);

			return exm.VEvaluator.getExpLv(value, maxLv);
		}
	}

	private sealed class FindElementMethod : FunctionMethod
	{
		public FindElementMethod(bool last)
		{
			ReturnType = EraType.Integer;
			// argumentTypeArray = null;
			argumentTypeArrayEx = [
					new ArgTypeList{ ArgTypes = { ArgType.RefAny1D | ArgType.AllowConstRef, ArgType.SameAsFirst, ArgType.Int, ArgType.Int, ArgType.Int }, OmitStart = 2 },
				];
			CanRestructure = true; //すべて定数項ならできるはず
			HasUniqueRestructure = true;
			isLast = last;
			funcName = isLast ? "FINDLASTELEMENT" : "FINDELEMENT";
		}

		readonly bool isLast;
		readonly string funcName;
		//public override string CheckArgumentType(string name, IOperandTerm[] arguments)
		//{
		//	if (arguments.Count < 2)
		//		return name + "関数には少なくとも2つの引数が必要です";
		//	if (arguments.Count > 5)
		//		return name + "関数の引数が多すぎます";
		//	if (arguments[0] == null)
		//		return name + "関数の1番目の引数は省略できません";
		//	if (!(arguments[0] is VariableTerm varToken))
		//		return name + "関数の1番目の引数が変数ではありません";
		//	if (varToken.Identifier.IsArray2D || varToken.Identifier.IsArray3D)
		//		return name + "関数は二重配列・三重配列には対応していません";
		//	if (!varToken.Identifier.IsArray1D)
		//		return name + "関数の1番目の引数が配列変数ではありません";
		//	Type baseType = arguments[0].GetOperandType();
		//	if (arguments[1] == null)
		//		return name + "関数の2番目の引数は省略できません";
		//	if (arguments[1].GetOperandType() != baseType)
		//		return name + "関数の2番目の引数の型が正しくありません";
		//	if ((arguments.Count >= 3) && (arguments[2] != null) && (arguments[2].GetOperandType() != EraType.Integer))
		//		return name + "関数の3番目の引数の型が正しくありません";
		//	if ((arguments.Count >= 4) && (arguments[3] != null) && (arguments[3].GetOperandType() != EraType.Integer))
		//		return name + "関数の4番目の引数の型が正しくありません";
		//	if ((arguments.Count >= 5) && (arguments[4] != null) && (arguments[4].GetOperandType() != EraType.Integer))
		//		return name + "関数の5番目の引数の型が正しくありません";
		//	return null;
		//}

		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			bool isExact = false;
			VariableTerm varTerm = (VariableTerm)arguments[0];

			long start = (arguments.Count > 2 && arguments[2] != null) ? arguments[2].GetIntValue(exm) : 0;
			long end = (arguments.Count > 3 && arguments[3] != null) ? arguments[3].GetIntValue(exm) : varTerm.GetLength();
			if (arguments.Count > 4 && arguments[4] != null)
				isExact = arguments[4].GetIntValue(exm) != 0;

			FixedVariableTerm p = varTerm.GetFixedVariableTerm(exm);
			p.IsArrayRangeValid(start, end, funcName, 3L, 4L);

			if (arguments[0].GetEraType() == EraType.Integer)
			{
				long targetValue = arguments[1].GetIntValue(exm);
				return VariableEvaluator.FindElement(p, targetValue, start, end, isExact, isLast);
			}
			else
			{
				Regex targetString;
				try
				{
					targetString = RegexFactory.GetRegex(arguments[1].GetStrValue(exm));
				}
				catch (ArgumentException e)
				{
					// throw new CodeEE("第2引数が正規表現として不正です");
					throw new CodeEE(string.Format(trerror.InvalidRegexArg.Text, Name, 2, e.Message));
				}
				return VariableEvaluator.FindElement(p, targetString, start, end, isExact, isLast);
			}
		}


		public override bool UniqueRestructure(ExpressionMediator exm, List<AExpression> arguments)
		{
			arguments[0].Restructure(exm);
			VariableTerm varToken = arguments[0] as VariableTerm;
			bool isConst = varToken.Identifier.IsConst;
			for (int i = 1; i < arguments.Count; i++)
			{
				if (arguments[i] == null)
					continue;
				arguments[i] = arguments[i].Restructure(exm);
				if (isConst && !(arguments[i] is SingleTerm))
					isConst = false;
			}
			return isConst;
		}
	}

	private sealed class InRangeMethod : FunctionMethod
	{
		public InRangeMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = [EraType.Integer, EraType.Integer, EraType.Integer];
			CanRestructure = true;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			long value = arguments[0].GetIntValue(exm);
			long min = arguments[1].GetIntValue(exm);
			long max = arguments[2].GetIntValue(exm);
			return ((value >= min) && (value <= max)) ? 1L : 0L;
		}
	}

	private sealed class InRangeArrayMethod : FunctionMethod
	{
		public InRangeArrayMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArrayEx = [
					new ArgTypeList{ ArgTypes = { ArgType.RefAny1D | ArgType.AllowConstRef, ArgType.Int, ArgType.Int, ArgType.Int, ArgType.Int }, OmitStart = 3 },
				];
			CanRestructure = false;
		}
		public InRangeArrayMethod(bool isChara)
		{
			ReturnType = EraType.Integer;
			argumentTypeArrayEx = [
					new ArgTypeList{ ArgTypes = { ArgType.CharacterData | ArgType.RefAny1D | ArgType.AllowConstRef, ArgType.Int, ArgType.Int, ArgType.Int, ArgType.Int }, OmitStart = 3 },
				];
			isCharaRange = isChara;
			CanRestructure = false;
		}
		private readonly bool isCharaRange;
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			VariableTerm varTerm = arguments[0] as VariableTerm;
			long start = (arguments.Count > 3 && arguments[3] != null) ? arguments[3].GetIntValue(exm) : 0;
			long end = (arguments.Count > 4 && arguments[4] != null) ? arguments[4].GetIntValue(exm) : (isCharaRange ? exm.VEvaluator.CHARANUM : varTerm.GetLength());

			FixedVariableTerm p = varTerm.GetFixedVariableTerm(exm);

			if (!isCharaRange)
			{
				p.IsArrayRangeValid(start, end, "INRANGEARRAY", 4L, 5L);
				if (varTerm.Identifier.GetEraType() == EraType.Float)
				{
					double min = arguments[1].GetFloatValue(exm);
					double max = arguments[2].GetFloatValue(exm);
					return VariableEvaluator.GetInRangeArrayDouble(p, min, max, start, end);
				}
				long minL = arguments[1].GetIntValue(exm);
				long maxL = arguments[2].GetIntValue(exm);
				return VariableEvaluator.GetInRangeArray(p, minL, maxL, start, end);
			}
			else
			{
				long charaNum = exm.VEvaluator.CHARANUM;
				if (start >= charaNum || start < 0 || end > charaNum || end < 0)
					throw new CodeEE(string.Format(trerror.CharacterRangeInvalid.Text, Name, start, end));
				if (varTerm.Identifier.GetEraType() == EraType.Float)
				{
					double min = arguments[1].GetFloatValue(exm);
					double max = arguments[2].GetFloatValue(exm);
					return VariableEvaluator.GetInRangeArrayCharaDouble(p, min, max, start, end);
				}
				long minL = arguments[1].GetIntValue(exm);
				long maxL = arguments[2].GetIntValue(exm);
				return VariableEvaluator.GetInRangeArrayChara(p, minL, maxL, start, end);
			}
		}
	}

	private sealed class ArrayMultiSortMethod : FunctionMethod
	{
		public ArrayMultiSortMethod()
		{
			ReturnType = EraType.Integer;
			// argumentTypeArray = null;
			argumentTypeArrayEx = [
					new ArgTypeList{ ArgTypes = { ArgType.RefAny1D, ArgType.RefAnyArray | ArgType.Variadic }, OmitStart = 1 },
				];
			CanRestructure = false;
			HasUniqueRestructure = true;
		}
		//public override string CheckArgumentType(string name, IOperandTerm[] arguments)
		//{
		//	if (arguments.Count < 2)
		//		return string.Format("{0}関数:少なくとも{1}の引数が必要です", name, 2);
		//	for (int i = 0; i < arguments.Count; i++)
		//	{
		//		if (arguments[i] == null)
		//			return string.Format("{0}関数:{1}番目の引数は省略できません", name, i + 1);
		//		if (!(arguments[i] is VariableTerm varTerm) || varTerm.Identifier.IsCalc || varTerm.Identifier.IsConst)
		//			return string.Format("{0}関数:{1}番目の引数が変数ではありません", name, i + 1);
		//		if (varTerm.Identifier.IsCharacterData)
		//			return string.Format("{0}関数:{1}番目の引数がキャラクタ変数です", name, i + 1);
		//		if (i == 0 && !varTerm.Identifier.IsArray1D)
		//			return string.Format("{0}関数:{1}番目の引数が一次元配列ではありません", name, i + 1);
		//		#region EM_私家版_ARRAYMSORT_三次元配列修正
		//		//if (!varTerm.Identifier.IsArray1D && !varTerm.Identifier.IsArray2D && !varTerm.Identifier.IsArray2D)
		//		if (!varTerm.Identifier.IsArray1D && !varTerm.Identifier.IsArray2D && !varTerm.Identifier.IsArray3D)
		//			return string.Format("{0}関数:{1}番目の引数が配列変数ではありません", name, i + 1);
		//		#endregion
		//	}
		//	return null;
		//}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			VariableTerm varTerm = arguments[0] as VariableTerm;
			int[] sortedArray;
			switch (varTerm.Identifier.GetEraType())
			{
				case EraType.Integer:
				{
					List<KeyValuePair<long, int>> sortList = [];
					object arrObj = varTerm.Identifier.GetArray();
					long[] array = arrObj is SparseArray<long> sparse ? sparse.ToArray(sparse.Length) : (long[])arrObj;
					for (int i = 0; i < array.Length; i++)
					{
						if (array[i] == 0)
							break;
						if (array[i] < long.MinValue || array[i] > long.MaxValue)
							return 0;
						sortList.Add(new KeyValuePair<long, int>(array[i], i));
					}
					sortList.Sort((a, b) => { return Math.Sign(a.Key - b.Key); });
					sortedArray = new int[sortList.Count];
					for (int i = 0; i < sortedArray.Length; i++)
						sortedArray[i] = sortList[i].Value;
					break;
				}
				case EraType.Float:
				{
					List<KeyValuePair<double, int>> sortList = [];
					object arrObj = varTerm.Identifier.GetArray();
					double[] array = arrObj is SparseArray<double> sparse ? sparse.ToArray(sparse.Length) : (double[])arrObj;
					for (int i = 0; i < array.Length; i++)
					{
						if (array[i] == 0.0)
							break;
						sortList.Add(new KeyValuePair<double, int>(array[i], i));
					}
					sortList.Sort((a, b) => a.Key.CompareTo(b.Key));
					sortedArray = new int[sortList.Count];
					for (int i = 0; i < sortedArray.Length; i++)
						sortedArray[i] = sortList[i].Value;
					break;
				}
				default:
				{
					List<KeyValuePair<string, int>> sortList = [];
					object arrObj = varTerm.Identifier.GetArray();
					string[] array = arrObj is SparseArray<string> sparse ? sparse.ToArray(sparse.Length) : (string[])arrObj;
					for (int i = 0; i < array.Length; i++)
					{
						if (string.IsNullOrEmpty(array[i]))
							break;
						sortList.Add(new KeyValuePair<string, int>(array[i], i));
					}
					sortList.Sort((a, b) => { return a.Key.CompareTo(b.Key); });
					sortedArray = new int[sortList.Count];
					for (int i = 0; i < sortedArray.Length; i++)
						sortedArray[i] = sortList[i].Value;
					break;
				}
			}
			foreach (VariableTerm term in arguments.Cast<VariableTerm>())
			{
				if (term.Identifier.IsArray1D)
				{
					switch (term.GetEraType())
					{
						case EraType.Integer:
						{
							object arrObj = term.Identifier.GetArray();
							if (arrObj is SparseArray<long> sparseArr)
							{
								var clone = sparseArr.ToArray(sparseArr.Length);
								if (sparseArr.Length < sortedArray.Length)
									return 0;
								for (int i = 0; i < sortedArray.Length; i++)
									sparseArr[i] = clone[sortedArray[i]];
							}
							else
							{
								var array = (long[])arrObj;
								var clone = (long[])array.Clone();
								if (array.Length < sortedArray.Length)
									return 0;
								for (int i = 0; i < sortedArray.Length; i++)
									array[i] = clone[sortedArray[i]];
							}
							break;
						}
						case EraType.Float:
						{
							object arrObj = term.Identifier.GetArray();
							if (arrObj is SparseArray<double> sparseArr)
							{
								var clone = sparseArr.ToArray(sparseArr.Length);
								if (sparseArr.Length < sortedArray.Length)
									return 0;
								for (int i = 0; i < sortedArray.Length; i++)
									sparseArr[i] = clone[sortedArray[i]];
							}
							else
							{
								var array = (double[])arrObj;
								var clone = (double[])array.Clone();
								if (array.Length < sortedArray.Length)
									return 0;
								for (int i = 0; i < sortedArray.Length; i++)
									array[i] = clone[sortedArray[i]];
							}
							break;
						}
						default:
						{
							object arrObj = term.Identifier.GetArray();
							if (arrObj is SparseArray<string> sparseArr)
							{
								var clone = sparseArr.ToArray(sparseArr.Length);
								if (sparseArr.Length < sortedArray.Length)
									return 0;
								for (int i = 0; i < sortedArray.Length; i++)
									sparseArr[i] = clone[sortedArray[i]];
							}
							else
							{
								var array = (string[])arrObj;
								var clone = (string[])array.Clone();
								if (array.Length < sortedArray.Length)
									return 0;
								for (int i = 0; i < sortedArray.Length; i++)
									array[i] = clone[sortedArray[i]];
							}
							break;
						}
					}
				}
				else if (term.Identifier.IsArray2D)
				{
					switch (term.GetEraType())
					{
						case EraType.Integer:
						{
							var array = (long[,])term.Identifier.GetArray();
							var clone = (long[,])array.Clone();
							if (array.GetLength(0) < sortedArray.Length)
								return 0;
							for (int i = 0; i < sortedArray.Length; i++)
								for (int x = 0; x < array.GetLength(1); x++)
									array[i, x] = clone[sortedArray[i], x];
							break;
						}
						case EraType.Float:
						{
							var array = (double[,])term.Identifier.GetArray();
							var clone = (double[,])array.Clone();
							if (array.GetLength(0) < sortedArray.Length)
								return 0;
							for (int i = 0; i < sortedArray.Length; i++)
								for (int x = 0; x < array.GetLength(1); x++)
									array[i, x] = clone[sortedArray[i], x];
							break;
						}
						default:
						{
							var array = (string[,])term.Identifier.GetArray();
							var clone = (string[,])array.Clone();
							if (array.GetLength(0) < sortedArray.Length)
								return 0;
							for (int i = 0; i < sortedArray.Length; i++)
								for (int x = 0; x < array.GetLength(1); x++)
									array[i, x] = clone[sortedArray[i], x];
							break;
						}
					}
				}
				else if (term.Identifier.IsArray3D)
				{
					switch (term.GetEraType())
					{
						case EraType.Integer:
						{
							var array = (long[,,])term.Identifier.GetArray();
							var clone = (long[,,])array.Clone();
							if (array.GetLength(0) < sortedArray.Length)
								return 0;
							for (int i = 0; i < sortedArray.Length; i++)
								for (int x = 0; x < array.GetLength(1); x++)
									for (int y = 0; y < array.GetLength(2); y++)
										array[i, x, y] = clone[sortedArray[i], x, y];
							break;
						}
						case EraType.Float:
						{
							var array = (double[,,])term.Identifier.GetArray();
							var clone = (double[,,])array.Clone();
							if (array.GetLength(0) < sortedArray.Length)
								return 0;
							for (int i = 0; i < sortedArray.Length; i++)
								for (int x = 0; x < array.GetLength(1); x++)
									for (int y = 0; y < array.GetLength(2); y++)
										array[i, x, y] = clone[sortedArray[i], x, y];
							break;
						}
						default:
						{
							var array = (string[,,])term.Identifier.GetArray();
							var clone = (string[,,])array.Clone();
							if (array.GetLength(0) < sortedArray.Length)
								return 0;
							for (int i = 0; i < sortedArray.Length; i++)
								for (int x = 0; x < array.GetLength(1); x++)
									for (int y = 0; y < array.GetLength(2); y++)
										array[i, x, y] = clone[sortedArray[i], x, y];
							break;
						}
					}
				}
				else { throw new ExeEE(trerror.AbnormalArray.Text); }
			}
			return 1;
		}
		public override bool UniqueRestructure(ExpressionMediator exm, List<AExpression> arguments)
		{
			for (int i = 0; i < arguments.Count; i++)
				arguments[i] = arguments[i].Restructure(exm);
			return false;
		}
	}
	#endregion

	#region 文字列操作系
	private sealed class StrlenMethod : FunctionMethod
	{
		public StrlenMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = [EraType.String];
			CanRestructure = true;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			string str = arguments[0].GetStrValue(exm);
			return LangManager.GetStrlenLang(str);
		}
	}

	private sealed class StrlenuMethod : FunctionMethod
	{
		public StrlenuMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = [EraType.String];
			CanRestructure = true;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			string str = arguments[0].GetStrValue(exm);
			return str.Length;
		}
	}

	private sealed class SubstringMethod : FunctionMethod
	{
		public SubstringMethod()
		{
			ReturnType = EraType.String;
			// argumentTypeArray = null;
			argumentTypeArrayEx = [
					new ArgTypeList{ ArgTypes = { ArgType.String, ArgType.Int, ArgType.Int}, OmitStart = 1 }
				];
			CanRestructure = true;
		}

		//public override string CheckArgumentType(string name, IOperandTerm[] arguments)
		//{
		//	//通常３つ、２つ省略可能で１～３の引数が必要。
		//	if (arguments.Count < 1)
		//		return name + "関数には少なくとも1つの引数が必要です";
		//	if (arguments.Count > 3)
		//		return name + "関数の引数が多すぎます";

		//	if (arguments[0] == null)
		//		return name + "関数の1番目の引数は省略できません";
		//	if (arguments[0].GetEraType() != EraType.String)
		//		return name + "関数の1番目の引数の型が正しくありません";
		//	//2、３は省略可能
		//	if ((arguments.Count >= 2) && (arguments[1] != null) && (arguments[1].GetOperandType() != EraType.Integer))
		//		return name + "関数の2番目の引数の型が正しくありません";
		//	if ((arguments.Count >= 3) && (arguments[2] != null) && (arguments[2].GetOperandType() != EraType.Integer))
		//		return name + "関数の3番目の引数の型が正しくありません";
		//	return null;
		//}
		public override string GetStrValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			string str = arguments[0].GetStrValue(exm);
			int start = 0;
			int length = -1;
			if ((arguments.Count >= 2) && (arguments[1] != null))
				start = (int)arguments[1].GetIntValue(exm);
			if ((arguments.Count >= 3) && (arguments[2] != null))
				length = (int)arguments[2].GetIntValue(exm);

			return LangManager.GetSubStringLang(str, start, length);
		}
	}

	private sealed class SubstringuMethod : FunctionMethod
	{
		public SubstringuMethod()
		{
			ReturnType = EraType.String;
			// argumentTypeArray = null;
			argumentTypeArrayEx = [
					new ArgTypeList{ ArgTypes = { ArgType.String, ArgType.Int, ArgType.Int}, OmitStart = 1 }
				];
			CanRestructure = true;
		}

		//public override string CheckArgumentType(string name, IOperandTerm[] arguments)
		//{
		//	//通常３つ、２つ省略可能で１～３の引数が必要。
		//	if (arguments.Count < 1)
		//		return name + "関数には少なくとも1つの引数が必要です";
		//	if (arguments.Count > 3)
		//		return name + "関数の引数が多すぎます";

		//	if (arguments[0] == null)
		//		return name + "関数の1番目の引数は省略できません";
		//	if (arguments[0].GetEraType() != EraType.String)
		//		return name + "関数の1番目の引数の型が正しくありません";
		//	//2、３は省略可能
		//	if ((arguments.Count >= 2) && (arguments[1] != null) && (arguments[1].GetOperandType() != EraType.Integer))
		//		return name + "関数の2番目の引数の型が正しくありません";
		//	if ((arguments.Count >= 3) && (arguments[2] != null) && (arguments[2].GetOperandType() != EraType.Integer))
		//		return name + "関数の3番目の引数の型が正しくありません";
		//	return null;
		//}
		public override string GetStrValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			string str = arguments[0].GetStrValue(exm);
			int start = 0;
			int length = -1;
			if ((arguments.Count >= 2) && (arguments[1] != null))
				start = (int)arguments[1].GetIntValue(exm);
			if ((arguments.Count >= 3) && (arguments[2] != null))
				length = (int)arguments[2].GetIntValue(exm);
			if ((start >= str.Length) || (length == 0))
				return "";
			if ((length < 0) || (length > str.Length))
				length = str.Length;
			if (start <= 0)
			{
				if (length == str.Length)
					return str;
				else
					start = 0;
			}
			if ((start + length) > str.Length)
				length = str.Length - start;

			return str.Substring(start, length);
		}
	}

	private sealed class StrfindMethod : FunctionMethod
	{
		public StrfindMethod(bool unicode)
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = null;
			// argumentTypeArray = null;
			argumentTypeArrayEx = [
					new ArgTypeList{ ArgTypes = { ArgType.String, ArgType.String, ArgType.Int}, OmitStart = 2 }
				];
			CanRestructure = true;
			this.unicode = unicode;
		}

		readonly bool unicode;
		//public override string CheckArgumentType(string name, IOperandTerm[] arguments)
		//{
		//	//通常３つ、１つ省略可能で２～３の引数が必要。
		//	if (arguments.Count < 2)
		//		return name + "関数には少なくとも2つの引数が必要です";
		//	if (arguments.Count > 3)
		//		return name + "関数の引数が多すぎます";
		//	if (arguments[0] == null)
		//		return name + "関数の1番目の引数は省略できません";
		//	if (arguments[0].GetEraType() != EraType.String)
		//		return name + "関数の1番目の引数の型が正しくありません";
		//	if (arguments[1] == null)
		//		return name + "関数の2番目の引数は省略できません";
		//	if (arguments[1].GetEraType() != EraType.String)
		//		return name + "関数の2番目の引数の型が正しくありません";
		//	//3つ目は省略可能
		//	if ((arguments.Count >= 3) && (arguments[2] != null) && (arguments[2].GetOperandType() != EraType.Integer))
		//		return name + "関数の3番目の引数の型が正しくありません";
		//	return null;
		//}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{

			string target = arguments[0].GetStrValue(exm);
			string word = arguments[1].GetStrValue(exm);
			int UFTstart = 0;
			if ((arguments.Count >= 3) && (arguments[2] != null))
			{
				if (unicode)
				{
					UFTstart = (int)arguments[2].GetIntValue(exm);
				}
				else
				{
					UFTstart = LangManager.GetUFTIndex(target, (int)arguments[2].GetIntValue(exm));
				}
			}
			if (UFTstart < 0 || UFTstart >= target.Length)
				return -1;
			int index = target.IndexOf(word, UFTstart, StringComparison.Ordinal);
			if (index > 0 && !unicode)
			{
				string subStr = target.Substring(0, index);
				index = LangManager.GetStrlenLang(subStr);
			}
			return index;
		}
	}

	private sealed class StrCountMethod : FunctionMethod
	{
		public StrCountMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = [EraType.String, EraType.String];
			CanRestructure = true;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			Regex reg;
			try
			{
				reg = RegexFactory.GetRegex(arguments[1].GetStrValue(exm));
			}
			catch (ArgumentException e)
			{
				// throw new CodeEE("第2引数が正規表現として不正です：" + e.Message);
				throw new CodeEE(string.Format(trerror.InvalidRegexArg.Text, Name, 2, e.Message));
			}
			return reg.Matches(arguments[0].GetStrValue(exm)).Count;
		}
	}

	private sealed class ToStrMethod : FunctionMethod
	{
		public ToStrMethod()
		{
			ReturnType = EraType.String;
			// argumentTypeArray = null;
			argumentTypeArrayEx = [
					new ArgTypeList{ ArgTypes = { ArgType.Int, ArgType.String }, OmitStart = 1 }
				];
			CanRestructure = true;
		}

		//public override string CheckArgumentType(string name, IOperandTerm[] arguments)
		//{
		//	//通常2つ、1つ省略可能で1～2の引数が必要。
		//	if (arguments.Count < 1)
		//		return name + "関数には少なくとも1つの引数が必要です";
		//	if (arguments.Count > 2)
		//		return name + "関数の引数が多すぎます";
		//	if (arguments[0] == null)
		//		return name + "関数の1番目の引数は省略できません";
		//	if (arguments[0].GetOperandType() != EraType.Integer)
		//		return name + "関数の1番目の引数の型が正しくありません";
		//	if ((arguments.Count >= 2) && (arguments[1] != null) && (arguments[1].GetEraType() != EraType.String))
		//		return name + "関数の2番目の引数の型が正しくありません";
		//	return null;
		//}
		public override string GetStrValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			long i = arguments[0].GetIntValue(exm);
			if ((arguments.Count < 2) || (arguments[1] == null))
				return i.ToString();
			string format = arguments[1].GetStrValue(exm);
			string ret;
			try
			{
				ret = i.ToString(format);
			}
			catch (FormatException)
			{
				// throw new CodeEE("TOSTR関数の書式指定が間違っています");
				throw new CodeEE(string.Format(trerror.InvalidFormat.Text, Name, 2));
			}
			return ret;
		}
	}

	private sealed class ToIntMethod : FunctionMethod
	{
		public ToIntMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = null;
			CanRestructure = true;
		}

		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			if (arguments[0].GetEraType() == EraType.Float)
				return (long)arguments[0].GetFloatValue(exm);
			string str = arguments[0].GetStrValue(exm);
			if (str == null || string.IsNullOrEmpty(str))
				return 0;
			if (str.Length < LangManager.GetStrlenLang(str))
				return 0;
			CharStream st = new(str);
			if (!char.IsDigit(st.Current) && st.Current != '+' && st.Current != '-')
				return 0;
			else if ((st.Current == '+' || st.Current == '-') && !char.IsDigit(st.Next))
				return 0;
			long ret = 0;
			try
			{
				ret = LexicalAnalyzer.ReadInt64(st, true);
			}
			catch (Exception)
			{
				return 0;
			}
			if (!st.EOS)
			{
				if (st.Current == '.')
				{
					st.ShiftNext();
					while (!st.EOS)
					{
						if (!char.IsDigit(st.Current))
							return 0;
						st.ShiftNext();
					}
				}
				else
					return 0;
			}
			return ret;
		}
	}

	private sealed class ToFloatMethod : FunctionMethod
	{
		public ToFloatMethod()
		{
			ReturnType = EraType.Float;
			argumentTypeArray = [EraType.String];
			CanRestructure = true;
		}

		public override double GetFloatValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			string str = arguments[0].GetStrValue(exm);
			if (str == null || string.IsNullOrEmpty(str))
				return 0.0;
			if (str.Length < LangManager.GetStrlenLang(str))
				return 0.0;
			if (double.TryParse(str, out double result))
				return result;
			return 0.0;
		}
	}

	private sealed class ToStrfMethod : FunctionMethod
	{
		public ToStrfMethod()
		{
			ReturnType = EraType.String;
			argumentTypeArrayEx = [
					new ArgTypeList{ ArgTypes = { ArgType.Float, ArgType.String }, OmitStart = 1 }
				];
			CanRestructure = true;
		}

		public override string GetStrValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			double value = arguments[0].GetFloatValue(exm);
			if (arguments.Count < 2 || arguments[1] == null)
				return value.ToString();
			string format = arguments[1].GetStrValue(exm);
			try
			{
				return value.ToString(format);
			}
			catch (FormatException)
			{
				throw new CodeEE(string.Format(trerror.InvalidFormat.Text, Name, 2));
			}
		}
	}

	enum StrFormType
	{
		Upper = 0,
		Lower = 1,
		Half = 2,
		Full = 3,
	};

	private sealed class StrChangeStyleMethod : FunctionMethod
	{
		readonly StrFormType strType;
		public StrChangeStyleMethod()
		{
			ReturnType = EraType.String;
			argumentTypeArray = [EraType.String];
			strType = StrFormType.Upper;
			CanRestructure = true;
		}
		public StrChangeStyleMethod(StrFormType type)
		{
			ReturnType = EraType.String;
			argumentTypeArray = [EraType.String];
			strType = type;
			CanRestructure = true;
		}
		public override string GetStrValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			string str = arguments[0].GetStrValue(exm);
			if (str == null || string.IsNullOrEmpty(str))
				return "";
			switch (strType)
			{
				case StrFormType.Upper:
					return str.ToUpper(CultureInfo.InvariantCulture);
				case StrFormType.Lower:
					return str.ToLower();
				case StrFormType.Half:
#if WINDOWS
					return Microsoft.VisualBasic.Strings.StrConv(str, Microsoft.VisualBasic.VbStrConv.Narrow, Config.Language);
#else
					return ToHalfWidth(str);
#endif
				case StrFormType.Full:
#if WINDOWS
					return Microsoft.VisualBasic.Strings.StrConv(str, Microsoft.VisualBasic.VbStrConv.Wide, Config.Language);
#else
					return ToFullWidth(str);
#endif
			}
			return "";
		}
	}

#if !WINDOWS
		static string ToFullWidth(string str)
		{
			var sb = new StringBuilder(str.Length);
			foreach (char c in str)
			{
				if (c >= '0' && c <= '9')
					sb.Append((char)(c + 0xFEE0));
				else if (c >= 'A' && c <= 'Z')
					sb.Append((char)(c + 0xFEE0));
				else if (c >= 'a' && c <= 'z')
					sb.Append((char)(c - 0x20 + 0xFEE0));
				else if (c == ' ')
					sb.Append('\u3000');
				else
					sb.Append(c);
			}
			return sb.ToString();
		}

		static string ToHalfWidth(string str)
		{
			var sb = new StringBuilder(str.Length);
			foreach (char c in str)
			{
				if (c >= '０' && c <= '９')
					sb.Append((char)(c - 0xFEE0));
				else if (c >= 'Ａ' && c <= 'Ｚ')
					sb.Append((char)(c - 0xFEE0));
				else if (c >= 'ａ' && c <= 'ｚ')
					sb.Append((char)(c + 0x20 - 0xFEE0));
				else if (c == '\u3000')
					sb.Append(' ');
				else if (c >= 'ｦ' && c <= 'ﾝ')
					sb.Append(c);
				else
					sb.Append(c);
			}
			return sb.ToString();
		}
#endif

	private sealed class LineIsEmptyMethod : FunctionMethod
	{
		public LineIsEmptyMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = [];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			return GlobalStatic.Console.EmptyLine ? 1L : 0L;
		}
	}

	#region EM_私家版_REPLACE拡張
	private sealed class ReplaceMethod : FunctionMethod
	{
		public ReplaceMethod()
		{
			ReturnType = EraType.String;
			// argumentTypeArray = new EraType[] { EraType.String, EraType.String, EraType.String };
			argumentTypeArrayEx = [
					new ArgTypeList{ ArgTypes = { ArgType.String, ArgType.String, ArgType.String, ArgType.Int }, OmitStart = 3 },
					new ArgTypeList{ ArgTypes = { ArgType.String, ArgType.String, ArgType.RefString1D | ArgType.AllowConstRef, ArgType.Int } },
				];
			HasUniqueRestructure = true;
			CanRestructure = false;
		}

		public override bool UniqueRestructure(ExpressionMediator exm, List<AExpression> arguments)
		{
			return arguments.Count < 4 || arguments[3].GetIntValue(exm) != 1;
		}
		//public override string CheckArgumentType(string name, IOperandTerm[] arguments)
		//{
		//	//通常2つ、1つ省略可能で1～2の引数が必要。
		//	if (arguments.Count < 3)
		//		return name + "関数には少なくとも3つの引数が必要です";
		//	if (arguments.Count > 4)
		//		return name + "関数の引数が多すぎます";
		//	for (int i = 0; i < 3; i++)
		//		if (arguments[i].GetEraType() != EraType.String)
		//			return string.Format("{0}関数:{1}番目の引数が文字列ではありません", name, i + 1);
		//	if (arguments.Count == 4 && arguments[3].GetOperandType() != EraType.Integer)
		//		return string.Format("{0}関数:4番目の引数が整数ではありません", name);
		//	return null;
		//}
		public override string GetStrValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			string baseString = arguments[0].GetStrValue(exm);
			Regex reg = null;
			int type = arguments.Count == 4 ? (int)arguments[3].GetIntValue(exm) : 0;
			if (type != 2)
			{
				try
				{
					reg = RegexFactory.GetRegex(arguments[1].GetStrValue(exm));
				}
				catch (ArgumentException e)
				{
					// throw new CodeEE("第２引数が正規表現として不正です：" + e.Message);
					throw new CodeEE(string.Format(trerror.InvalidRegexArg.Text, Name, 2, e.Message));
				}
			}
			if (arguments.Count == 4)
			{
				switch (type)
				{
					case 1:
						{
							if (!(arguments[2] is VariableTerm varTerm) || varTerm.Identifier.IsCalc || !varTerm.Identifier.IsArray1D || varTerm.Identifier.GetEraType() != EraType.String || varTerm.Identifier.IsConst)
								throw new CodeEE(string.Format(trerror.ArgIsNotNDStrArray.Text, Name, 3, 1));
							var itemsObj = (arguments[2] as VariableTerm).Identifier.GetArray();
							int idx = 0;
							if (itemsObj is string[] items)
							{
								return reg.Replace(baseString, (Match match) =>
								{
									if (idx < items.Length)
										return items[idx++];
									return string.Empty;
								});
							}
							else
							{
								var itemsSa = (SparseArray<string>)itemsObj;
								return reg.Replace(baseString, (Match match) =>
								{
									if (idx < itemsSa.Length)
										return itemsSa[idx++];
									return string.Empty;
								});
							}
						}
					case 2:
						{
							// 正規表現を使わず
							return baseString.Replace(arguments[1].GetStrValue(exm), arguments[2].GetStrValue(exm));
						}
				}
			}
			// type == 0 or > 2 or omitted.
			return reg.Replace(baseString, arguments[2].GetStrValue(exm));
		}
	}
	#endregion

	private sealed class UnicodeMethod : FunctionMethod
	{
		public UnicodeMethod()
		{
			ReturnType = EraType.String;
			argumentTypeArray = [EraType.Integer];
			CanRestructure = true;
		}
		public override string GetStrValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			long i = arguments[0].GetIntValue(exm);
			if ((i < 0) || (i > 0xFFFF))
				// throw new CodeEE("UNICODE関数に範囲外の値(" + i.ToString() + ")が渡されました");
				throw new CodeEE(string.Format(trerror.ArgIsOutOfRange.Text, Name, 1, i, 0, 0xFFFF));
			//改行関係以外の制御文字は警告扱いに変更
			//とはいえ、改行以外の制御文字を意図的に渡すのはそもそもコーディングに問題がありすぎるので、エラーでもいい気はする
			if ((i < 0x001F && i != 0x000A && i != 0x000D) || (i >= 0x007F && i <= 0x009F))
			{
				//コード実行中の場合
				if (GlobalStatic.Process.getCurrentLine != null)
					// GlobalStatic.Console.PrintSystemLine("注意:" + GlobalStatic.Process.getCurrentLine.Position.Value.Filename + "の" + GlobalStatic.Process.getCurrentLine.Position.Value.LineNo.ToString() + "行目でUNICODE関数に制御文字に対応する値(0x" + String.Format("{0:X}", i) + ")が渡されました");
					GlobalStatic.Console.PrintSystemLine(string.Format(trerror.WarnPrefix.Text,
						GlobalStatic.Process.getCurrentLine.Position.Value.Filename,
						GlobalStatic.Process.getCurrentLine.Position.Value.LineNo,
						string.Format(trerror.InvalidUnicode.Text, Name, i)));
				else
					//ParserMediator.Warn("UNICODE関数に制御文字に対応する値(0x" + String.Format("{0:X}", i) + ")が渡されました", GlobalStatic.Process.scaningLine, 1, false, false, null);
					ParserMediator.Warn(string.Format(trerror.InvalidUnicode.Text, Name, i), GlobalStatic.Process.scaningLine, 1, false, false, null);
				return "";
			}
			string s = new(new char[] { (char)i });

			return s;
		}
	}

	private sealed class UnicodeByteMethod : FunctionMethod
	{
		public UnicodeByteMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = [EraType.String];
			CanRestructure = true;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			string target = arguments[0].GetStrValue(exm);
			int length = Encoding.UTF32.GetEncoder().GetByteCount(target.ToCharArray(), 0, target.Length, false);
			byte[] bytes = new byte[length];
			Encoding.UTF32.GetEncoder().GetBytes(target.ToCharArray(), 0, target.Length, bytes, 0, false);
			long i = BitConverter.ToInt32(bytes, 0);

			return i;
		}
	}

	private sealed class ConvertIntMethod : FunctionMethod
	{
		public ConvertIntMethod()
		{
			ReturnType = EraType.String;
			argumentTypeArray = [EraType.Integer, EraType.Integer];
			CanRestructure = true;
		}
		public override string GetStrValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			long toBase = arguments[1].GetIntValue(exm);
			if ((toBase != 2) && (toBase != 8) && (toBase != 10) && (toBase != 16))
				// new CodeEE("CONVERT関数の第２引数は2, 8, 10, 16のいずれかでなければなりません");
				throw new CodeEE(string.Format(trerror.ArgShouldBeSpecificValue.Text, Name, 2, "2, 8, 10, 16"));
			return Convert.ToString(arguments[0].GetIntValue(exm), (int)toBase);
		}
	}

	private sealed class IsNumericMethod : FunctionMethod
	{
		public IsNumericMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = [EraType.String];
			CanRestructure = true;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			string baseStr = arguments[0].GetStrValue(exm);

			//全角文字があるなら数値ではない
			if (baseStr.Length < LangManager.GetStrlenLang(baseStr))
				return 0;
			CharStream st = new(baseStr);
			if (!char.IsDigit(st.Current) && st.Current != '+' && st.Current != '-')
				return 0;
			else if ((st.Current == '+' || st.Current == '-') && !char.IsDigit(st.Next))
				return 0;
			if (!LexicalAnalyzer.NumericCheck(st))
				return (0);
			if (!st.EOS)
			{
				if (st.Current == '.')
				{
					st.ShiftNext();
					while (!st.EOS)
					{
						if (!char.IsDigit(st.Current))
							return 0;
						st.ShiftNext();
					}
				}
				else
					return 0;
			}
			return 1;
		}
	}

	private sealed class EscapeMethod : FunctionMethod
	{
		public EscapeMethod()
		{
			ReturnType = EraType.String;
			argumentTypeArray = [EraType.String];
			CanRestructure = true;
		}
		public override string GetStrValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			return Regex.Escape(arguments[0].GetStrValue(exm));
		}
	}

	private sealed class EncodeToUniMethod : FunctionMethod
	{
		public EncodeToUniMethod()
		{
			ReturnType = EraType.Integer;
			// argumentTypeArray = new EraType[] { null };
			argumentTypeArrayEx = [
					new ArgTypeList{ ArgTypes = { ArgType.String, ArgType.Int }, OmitStart = 1 },
				];
			CanRestructure = true;
		}
		//public override string CheckArgumentType(string name, IOperandTerm[] arguments)
		//{
		//	//通常2つ、1つ省略可能で1～2の引数が必要。
		//	if (arguments.Count < 1)
		//		return name + "関数には少なくとも1つの引数が必要です";
		//	if (arguments.Count > 2)
		//		return name + "関数の引数が多すぎます";
		//	if (arguments[0] == null)
		//		return name + "関数の1番目の引数は省略できません";
		//	if (arguments[0].GetEraType() != EraType.String)
		//		return name + "関数の1番目の引数の型が正しくありません";
		//	if ((arguments.Count >= 2) && (arguments[1] != null) && (arguments[1].GetOperandType() != EraType.Integer))
		//		return name + "関数の2番目の引数の型が正しくありません";
		//	return null;
		//}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			string baseStr = arguments[0].GetStrValue(exm);
			if (baseStr.Length == 0)
				return -1;
			long position = (arguments.Count > 1 && arguments[1] != null) ? arguments[1].GetIntValue(exm) : 0;
			if (position < 0)
				// throw new CodeEE("ENCOIDETOUNI関数の第２引数(" + position.ToString() + ")が負の値です");
				throw new CodeEE(string.Format(trerror.ArgIsNegative.Text, Name, 2, position));
			if (position >= baseStr.Length)
				// throw new CodeEE("ENCOIDETOUNI関数の第２引数(" + position.ToString() + ")が第１引数の文字列(" + baseStr + ")の文字数を超えています");
				throw new CodeEE(string.Format(trerror.EncodeToUni2ndArgError.Text, Name, position, baseStr));
			return char.ConvertToUtf32(baseStr, (int)position);
		}
	}

	public sealed class CharAtMethod : FunctionMethod
	{
		public CharAtMethod()
		{
			ReturnType = EraType.String;
			argumentTypeArray = [EraType.String, EraType.Integer];
			CanRestructure = true;
		}
		public override string GetStrValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			string str = arguments[0].GetStrValue(exm);
			long pos = arguments[1].GetIntValue(exm);
			if (pos < 0 || pos >= str.Length)
				return "";
			return str[(int)pos].ToString();
		}
	}

	public sealed class GetLineStrMethod : FunctionMethod
	{
		public GetLineStrMethod()
		{
			ReturnType = EraType.String;
			argumentTypeArray = [EraType.String];
			CanRestructure = true;
		}
		public override string GetStrValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			string str = arguments[0].GetStrValue(exm);
			if (string.IsNullOrEmpty(str))
				// throw new CodeEE("GETLINESTR関数の引数が空文字列です");
				throw new CodeEE(string.Format(trerror.ArgIsEmptyString.Text, Name, 1));
			return exm.Console.getStBar(str);
		}
	}

	public sealed class StrFormMethod : FunctionMethod
	{
		public StrFormMethod()
		{
			ReturnType = EraType.String;
			argumentTypeArray = [EraType.String];
			HasUniqueRestructure = true;
			CanRestructure = true;
		}
		public override string GetStrValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			string str = arguments[0].GetStrValue(exm);
			string destStr;
			try
			{
				StrFormWord wt = LexicalAnalyzer.AnalyseFormattedString(new CharStream(str), FormStrEndWith.EoL, false);
				StrForm strForm = StrForm.FromWordToken(wt);
				destStr = strForm.GetString(exm);
			}
			catch (CodeEE e)
			{
				// throw new CodeEE("STRFORM関数:文字列\"" + str + "\"の展開エラー:" + e.Message);
				throw new CodeEE(string.Format(trerror.InvalidFormString.Text, Name, str, e.Message));
			}
			catch
			{
				// throw new CodeEE("STRFORM関数:文字列\"" + str+ "\"の展開処理中にエラーが発生しました");
				throw new CodeEE(string.Format(trerror.UnexectedFormStringErr.Text, Name, str));
			}
			return destStr;
		}
		public override bool UniqueRestructure(ExpressionMediator exm, List<AExpression> arguments)
		{
			arguments[0].Restructure(exm);
			//引数が文字列式等ならお手上げなので諦める
			if (!(arguments[0] is SingleTerm) && !(arguments[0] is VariableTerm))
				return false;
			//引数が確定値でない文字列変数なら無条件で不可（結果が可変なため）
			if ((arguments[0] is VariableTerm) && !((VariableTerm)arguments[0]).Identifier.IsConst)
				return false;
			string str = arguments[0].GetStrValue(exm);
			try
			{
				StrFormWord wt = LexicalAnalyzer.AnalyseFormattedString(new CharStream(str), FormStrEndWith.EoL, false);
				StrForm strForm = StrForm.FromWordToken(wt);
				if (!strForm.IsConst)
					return false;
			}
			catch
			{
				//パースできないのはエラーがあるかここではわからないからとりあえず考えない
				return false;
			}
			return true;
		}
	}

	public sealed class StrFormCheckMethod : FunctionMethod
	{
		public StrFormCheckMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = [EraType.String];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			string str = arguments[0].GetStrValue(exm);
			try
			{
				StrFormWord wt = LexicalAnalyzer.AnalyseFormattedString(new CharStream(str), FormStrEndWith.EoL, false);
				StrForm strForm = StrForm.FromWordToken(wt);
				strForm.GetString(exm);
				return 1;
			}
			catch
			{
				return 0;
			}
		}
	}

	public sealed class JoinMethod : FunctionMethod
	{
		public JoinMethod()
		{
			ReturnType = EraType.String;
			// argumentTypeArray = null;
			argumentTypeArrayEx = [
					new ArgTypeList{ ArgTypes = { ArgType.RefAnyArray | ArgType.AllowConstRef, ArgType.String, ArgType.Int, ArgType.Int }, OmitStart = 1 },
				];
			HasUniqueRestructure = true;
			CanRestructure = true;
		}
		//public override string CheckArgumentType(string name, IOperandTerm[] arguments)
		//{
		//	if (arguments.Count < 1)
		//		return name + "関数には少なくとも1つの引数が必要です";
		//	if (arguments.Count > 4)
		//		return name + "関数の引数が多すぎます";
		//	if (arguments[0] == null)
		//		return name + "関数の1番目の引数は省略できません";
		//	if (!(arguments[0] is VariableTerm))
		//		return name + "関数の1番目の引数が変数ではありません";
		//	VariableTerm varToken = (VariableTerm)arguments[0];
		//	if (!varToken.Identifier.IsArray1D && !varToken.Identifier.IsArray2D && !varToken.Identifier.IsArray3D)
		//		return name + "関数の1番目の引数が配列変数ではありません";
		//	if (arguments.Count == 1)
		//		return null;
		//	if ((arguments[1] != null) && (arguments[1].GetEraType() != EraType.String))
		//		return name + "関数の2番目の変数が文字列ではありません";
		//	if (arguments.Count == 2)
		//		return null;
		//	if ((arguments[2] != null) && (arguments[2].GetOperandType() != EraType.Integer))
		//		return name + "関数の3番目の変数が数値ではありません";
		//	if (arguments.Count == 3)
		//		return null;
		//	if ((arguments[3] != null) && (arguments[3].GetOperandType() != EraType.Integer))
		//		return name + "関数の4番目の変数が数値ではありません";
		//	return null;
		//}
		public override string GetStrValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			VariableTerm varTerm = (VariableTerm)arguments[0];
			string delimiter = (arguments.Count >= 2 && arguments[1] != null) ? arguments[1].GetStrValue(exm) : ",";
			long index1 = (arguments.Count >= 3 && arguments[2] != null) ? arguments[2].GetIntValue(exm) : 0;
			long index2 = (arguments.Count == 4 && arguments[3] != null) ? arguments[3].GetIntValue(exm) : varTerm.GetLastLength() - index1;

			FixedVariableTerm p = varTerm.GetFixedVariableTerm(exm);

			if (index2 < 0)
				// throw new CodeEE("STRJOINの第4引数(" + index2.ToString()+ ")が負の値になっています");
				throw new CodeEE(string.Format(trerror.ArgIsNegative.Text, Name, 4, index2));

			p.IsArrayRangeValid(index1, index1 + index2, "STRJOIN", 2L, 3L);
			return VariableEvaluator.GetJoinedStr(p, delimiter, index1, index2);
		}
		public override bool UniqueRestructure(ExpressionMediator exm, List<AExpression> arguments)
		{
			//第1変数は変数名なので、定数文字列変数だと事故が起こるので独自対応
			VariableTerm varTerm = (VariableTerm)arguments[0];
			bool canRerstructure = varTerm.Identifier.IsConst;
			for (int i = 1; i < arguments.Count; i++)
			{
				if (arguments[i] == null)
					continue;
				arguments[i] = arguments[i].Restructure(exm);
				canRerstructure &= arguments[i] is SingleTerm;
			}
			return canRerstructure;
		}
	}

	public sealed class GetConfigMethod : FunctionMethod
	{
		public GetConfigMethod(bool typeisInt)
		{
			if (typeisInt)
			{
				funcname = "GETCONFIG";
				ReturnType = EraType.Integer;
			}
			else
			{
				funcname = "GETCONFIGS";
				ReturnType = EraType.String;
			}
			argumentTypeArray = [EraType.String];
			CanRestructure = true;
		}
		private readonly string funcname;
		private SingleTerm GetSingleTerm(ExpressionMediator exm, List<AExpression> arguments)
		{
			string str = arguments[0].GetStrValue(exm);
			if (str == null || str.Length == 0)
				// throw new CodeEE(funcname + "関数に空文字列が渡されました");
				throw new CodeEE(string.Format(trerror.ArgIsEmptyString.Text, Name, 1));
			string errMes = null;
			SingleTerm term = ConfigData.GetConfigValueInERB(str, ref errMes);
			if (errMes != null)
				// throw new CodeEE(funcname + "関数:" + errMes);
				throw new CodeEE(errMes);
			return term;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			if (ReturnType != EraType.Integer)
				throw new ExeEE(funcname + "関数:不正な呼び出し");
			SingleTerm term = GetSingleTerm(exm, arguments);
			if (term is not SingleLongTerm singleLongTerm)
				// throw new CodeEE(funcname + "関数:型が違います（GETCONFIGS関数を使用してください）");
				throw new CodeEE(string.Format(trerror.InvalidType.Text, Name, "GETCONFIGS"));
			return singleLongTerm.Int;
		}
		public override string GetStrValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			if (ReturnType != EraType.String)
				throw new ExeEE(funcname + "関数:不正な呼び出し");
			SingleTerm term = GetSingleTerm(exm, arguments);
			if (term is not SingleStrTerm singleStrTerm)
				// throw new CodeEE(funcname + "関数:型が違います（GETCONFIG関数を使用してください）");
				throw new CodeEE(string.Format(trerror.InvalidType.Text, Name, "GETCONFIG"));
			return singleStrTerm.Str;
		}
	}
	#endregion

	#region html系

	private sealed class HtmlGetPrintedStrMethod : FunctionMethod
	{
		public HtmlGetPrintedStrMethod()
		{
			ReturnType = EraType.String;
			// argumentTypeArray = null;
			argumentTypeArrayEx = [
					new ArgTypeList{ ArgTypes = { ArgType.Int }, OmitStart = 0 }
				];
			CanRestructure = false;
		}

		//public override string CheckArgumentType(string name, IOperandTerm[] arguments)
		//{
		//	//通常１つ。省略可能。
		//	if (arguments.Count > 1)
		//		return name + "関数の引数が多すぎます";
		//	if (arguments.Count == 0|| arguments[0] == null)
		//		return null;
		//	if (arguments[0].GetOperandType() != EraType.Integer)
		//		return name + "関数の1番目の引数の型が正しくありません";
		//	return null;
		//}
		public override string GetStrValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			long lineNo = 0;
			if (arguments.Count > 0)
				lineNo = arguments[0].GetIntValue(exm);
			if (lineNo < 0)
				// throw new CodeEE("引数を0未満にできません");
				throw new CodeEE(string.Format(trerror.ArgIsNegative.Text, Name, 1, lineNo));
			ConsoleDisplayLine[] dispLines = exm.Console.GetDisplayLines(lineNo);
			if (dispLines == null)
				return "";
			return HtmlManager.DisplayLine2Html(dispLines, true);
		}
	}

	private sealed class HtmlPopPrintingStrMethod : FunctionMethod
	{
		public HtmlPopPrintingStrMethod()
		{
			ReturnType = EraType.String;
			argumentTypeArray = [];
			CanRestructure = false;
		}

		public override string GetStrValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			ConsoleDisplayLine[] dispLines = exm.Console.PopDisplayingLines();
			if (dispLines == null)
				return "";
			return HtmlManager.DisplayLine2Html(dispLines, false);
		}
	}

	private sealed class HtmlToPlainTextMethod : FunctionMethod
	{
		public HtmlToPlainTextMethod()
		{
			ReturnType = EraType.String;
			argumentTypeArray = [EraType.String];
			CanRestructure = false;
		}
		public override string GetStrValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			return HtmlManager.Html2PlainText(arguments[0].GetStrValue(exm));
		}
	}
	private sealed class HtmlEscapeMethod : FunctionMethod
	{
		public HtmlEscapeMethod()
		{
			ReturnType = EraType.String;
			argumentTypeArray = [EraType.String];
			CanRestructure = false;
		}
		public override string GetStrValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			return HtmlManager.Escape(arguments[0].GetStrValue(exm));
		}
	}
	#endregion

	#region 画像処理系
	/// <summary>
	/// argNo番目の引数をGraphicsImageのIDを示す整数値として読み取り、 GraphicsImage又はnullを返す。
	/// </summary>
	private static GraphicsImage ReadGraphics(string Name, ExpressionMediator exm, List<AExpression> arguments, int argNo)
	{
		long target = arguments[argNo].GetIntValue(exm);
		if (target < 0)//funcname + "関数:GraphicsIDに負の値(" + target.ToString() + ")が指定されました"
					   // throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGraphicsID0, Name, target));
			throw new CodeEE(string.Format(trerror.GIdIsNegative.Text, Name, target));
		else if (target > int.MaxValue)//funcname + "関数:GraphicsIDの値(" + target.ToString() + ")が大きすぎます"
									   // throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGraphicsID1, Name, target));
			throw new CodeEE(string.Format(trerror.GIdIsTooLarge.Text, Name, target));
		return AppContents.GetGraphics((int)target);
	}
	/// <summary>
	/// 引数で指定したIDのGraphicsImageを読み取り、 GraphicsImage又はnullを返す。
	/// </summary>
	public static GraphicsImage ReadGraphics(int target)
	{
		if (target < 0)//funcname + "関数:GraphicsIDに負の値(" + target.ToString() + ")が指定されました"
					   // throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGraphicsID0, Name, target));
			throw new CodeEE(string.Format(trerror.GIdIsNegative.Text, "HTML_PRINT", target));
		else if (target > int.MaxValue)//funcname + "関数:GraphicsIDの値(" + target.ToString() + ")が大きすぎます"
									   // throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGraphicsID1, Name, target));
			throw new CodeEE(string.Format(trerror.GIdIsTooLarge.Text, "HTML_PRINT", target));
		return AppContents.GetGraphics(target);
	}

	/// <summary>
	/// argNo番目の引数を整数値として読み取り、 アルファ値を含むColor構造体にして返す。
	/// </summary>
	private static Color ReadColor(string Name, ExpressionMediator exm, List<AExpression> arguments, int argNo)
	{
		long c64 = arguments[argNo].GetIntValue(exm);
		if (c64 < 0 || c64 > 0xFFFFFFFF)
			// throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodColorARGB0, Name, c64));
			throw new CodeEE(string.Format(trerror.InvalidColorARGB.Text, Name, c64));
		return Color.FromArgb((int)(c64 >> 24) & 0xFF, (int)(c64 >> 16) & 0xFF, (int)(c64 >> 8) & 0xFF, (int)c64 & 0xFF);
	}

	/// <summary>
	/// argNo番目を含む2つの引数を整数値として読み取り、Point形式にして返す。
	/// </summary>
	private static Point ReadPoint(string Name, ExpressionMediator exm, List<AExpression> arguments, int argNo)
	{
		long x64 = arguments[argNo].GetIntValue(exm);
		if (x64 < int.MinValue || x64 > int.MaxValue)
			// throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodDefaultArgumentOutOfRange0, Name,x64, argNo+1));
			throw new CodeEE(string.Format(trerror.ArgIsOutOfRange.Text, Name, argNo + 1, x64, int.MinValue, int.MaxValue));
		long y64 = arguments[argNo + 1].GetIntValue(exm);
		if (y64 < int.MinValue || y64 > int.MaxValue)
			// throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodDefaultArgumentOutOfRange0, Name,y64, argNo+1+1));
			throw new CodeEE(string.Format(trerror.ArgIsOutOfRange.Text, Name, argNo + 2, y64, int.MinValue, int.MaxValue));
		return new Point((int)x64, (int)y64);
	}

	/// <summary>
	/// argNo番目を含む4つの引数を整数値として読み取り、Rectangle形式にして返す。
	/// </summary>
	private static Rectangle ReadRectangle(string Name, ExpressionMediator exm, List<AExpression> arguments, int argNo)
	{
		long x64 = arguments[argNo].GetIntValue(exm);
		if (x64 < int.MinValue || x64 > int.MaxValue)
			// throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodDefaultArgumentOutOfRange0, Name, x64, argNo + 1));
			throw new CodeEE(string.Format(trerror.ArgIsOutOfRange.Text, Name, argNo + 1, x64, int.MinValue, int.MaxValue));
		long y64 = arguments[argNo + 1].GetIntValue(exm);
		if (y64 < int.MinValue || y64 > int.MaxValue)
			// throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodDefaultArgumentOutOfRange0, Name, y64, argNo + 1 + 1));
			throw new CodeEE(string.Format(trerror.ArgIsOutOfRange.Text, Name, argNo + 2, y64, int.MinValue, int.MaxValue));

		long w64 = arguments[argNo + 2].GetIntValue(exm);
		if (w64 < int.MinValue || w64 > int.MaxValue || w64 == 0)
			// throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodDefaultArgumentOutOfRange0, Name, w64, argNo + 2 + 1));
			throw new CodeEE(string.Format(trerror.ArgIsOutOfRangeExcept.Text, Name, argNo + 3, w64, int.MinValue, int.MaxValue, 0));
		long h64 = arguments[argNo + 3].GetIntValue(exm);
		if (h64 < int.MinValue || h64 > int.MaxValue || h64 == 0)
			// throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodDefaultArgumentOutOfRange0, Name, h64, argNo + 3 + 1));
			throw new CodeEE(string.Format(trerror.ArgIsOutOfRangeExcept.Text, Name, argNo + 4, h64, int.MinValue, int.MaxValue, 0));
		return new Rectangle((int)x64, (int)y64, (int)w64, (int)h64);
	}

	/// <summary>
	/// argNo番目の引数を5x5のカラーマトリクス配列変数として読み取り、 5x5のfloat[][]形式にして返す。
	/// </summary>
	private static float[][] ReadColormatrix(string Name, ExpressionMediator exm, List<AExpression> arguments, int argNo)
	{
		//数値型二次元以上配列変数のはず
		FixedVariableTerm p = ((VariableTerm)arguments[argNo]).GetFixedVariableTerm(exm);
		long e1, e2;
		float[][] cm = new float[5][];
		if (p.Identifier.IsArray2D)
		{
			long[,] array;
			if (p.Identifier.IsCharacterData)
			{
				array = p.Identifier.GetArrayChara((int)p.Index1) as long[,];
				e1 = p.Index2;
				e2 = p.Index3;
			}
			else
			{
				array = p.Identifier.GetArray() as long[,];
				e1 = p.Index1;
				e2 = p.Index2;
			}
			if (e1 < 0 || e2 < 0 || e1 + 5 > array.GetLength(0) || e2 + 5 > array.GetLength(1))
				// throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGColorMatrix0, Name, e1, e2));
				throw new CodeEE(string.Format(trerror.InvalidColorMatrix.Text, Name, e1, e2));
			for (int x = 0; x < 5; x++)
			{
				cm[x] = new float[5];
				for (int y = 0; y < 5; y++)
				{
					cm[x][y] = array[e1 + x, e2 + y] / 256f;
				}
			}
		}
		if (p.Identifier.IsArray3D)
		{
			long[,,] array; long e3;
			if (p.Identifier.IsCharacterData)
			{
				throw new NotImplCodeEE();
			}
			else
			{
				array = p.Identifier.GetArray() as long[,,];
				e1 = p.Index1;
				e2 = p.Index2;
				e3 = p.Index3;
			}
			if (e1 < 0 || e1 >= array.GetLength(0))
				// throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGColorMatrix0, Name, e2, e3));
				throw new CodeEE(string.Format(trerror.InvalidColorMatrix.Text, Name, e2, e3));
			if (e2 < 0 || e3 < 0 || e2 + 5 > array.GetLength(1) || e3 + 5 > array.GetLength(2))
				// throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGColorMatrix0, Name, e2, e3));
				throw new CodeEE(string.Format(trerror.InvalidColorMatrix.Text, Name, e2, e3));
			for (int x = 0; x < 5; x++)
			{
				cm[x] = new float[5];
				for (int y = 0; y < 5; y++)
				{
					cm[x][y] = array[e1, e2 + x, e3 + y] / 256f;
				}
			}
		}
		return cm;
	}

	public sealed class GraphicsStateMethod : FunctionMethod
	{
		public GraphicsStateMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = [EraType.Integer];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			if (Config.TextDrawingMode == TextDrawingMode.WINAPI)
				// throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGDIPLUSOnly, Name));
				throw new CodeEE(string.Format(trerror.GDIPlusOnly.Text, Name));
			GraphicsImage g = ReadGraphics(Name, exm, arguments, 0);
			if (!g.IsCreated)
				return 0;
			switch (Name)
			{
				case "GCREATED":
					return 1;
				case "GWIDTH":
					return g.Width;
				case "GHEIGHT":
					return g.Height;
				#region EE_GDRAWTEXTに付随する要素
				case "GGETFONTSIZE":
					return g.Fontsize;
				case "GGETFONTSTYLE":
					return g.Fontstyle;
				case "GGETPEN":
					return g.PenColorArgb;
				case "GGETPENWIDTH":
					return g.PenWidth;
				case "GGETBRUSH":
					return g.BrushColorArgb;
					#endregion
			}
			throw new ExeEE("GraphicsState:" + Name + ":異常な分岐");
		}
	}
	#region EE_GGETFONT
	public sealed class GraphicsStateStrMethod : FunctionMethod
	{
		public GraphicsStateStrMethod()
		{
			ReturnType = EraType.String;
			argumentTypeArray = [EraType.Integer];
			CanRestructure = false;
		}
		public override string GetStrValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			if (Config.TextDrawingMode == TextDrawingMode.WINAPI)
				// throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGDIPLUSOnly, Name));
				throw new CodeEE(string.Format(trerror.GDIPlusOnly.Text, Name));
			GraphicsImage g = ReadGraphics(Name, exm, arguments, 0);
			if (!g.IsCreated)
				return "";
			switch (Name)
			{
				case "GGETFONT":
					return g.Fontname;
			}
			throw new ExeEE("GraphicsState:" + Name + ":Abnormal branching");
		}
	}
	#endregion

	public sealed class GraphicsGetColorMethod : FunctionMethod
	{
		public GraphicsGetColorMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = [EraType.Integer, EraType.Integer, EraType.Integer];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			if (Config.TextDrawingMode == TextDrawingMode.WINAPI)
				// throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGDIPLUSOnly, Name));
				throw new CodeEE(string.Format(trerror.GDIPlusOnly.Text, Name));
			GraphicsImage g = ReadGraphics(Name, exm, arguments, 0);
			//失敗したら負の値を返す。他と戻り値違うけど仕方ないね
			if (!g.IsCreated)
				return -1;
			Point p = ReadPoint(Name, exm, arguments, 1);
			if (p.X < 0 || p.X >= g.Width || p.X < 0 || p.Y >= g.Height)
				return -1;
			var c = g.GGetColor(p.X, p.Y);
			//Color.ToArgb()はInt32の負の値をとることがあり、Int64にうまく変換できない?（と思ったが気のせいだった
			return ((long)c.Alpha << 24 | (long)c.Red << 16 | (long)c.Green << 8 | c.Blue) & 0xFFFFFFFFL;
		}
	}

	public sealed class GraphicsSetColorMethod : FunctionMethod
	{
		public GraphicsSetColorMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = [EraType.Integer, EraType.Integer, EraType.Integer, EraType.Integer];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			if (Config.TextDrawingMode == TextDrawingMode.WINAPI)
				// throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGDIPLUSOnly, Name));
				throw new CodeEE(string.Format(trerror.GDIPlusOnly.Text, Name));
			try
			{
				GraphicsImage g = ReadGraphics(Name, exm, arguments, 0);
				if (!g.IsCreated)
					return 0;
				Color c = ReadColor(Name, exm, arguments, 1);
				Point p = ReadPoint(Name, exm, arguments, 2);
				if (p.X < 0 || p.X >= g.Width || p.X < 0 || p.Y >= g.Height)
					return 0;
				g.GSetColor(c, p.X, p.Y);
				return 1;
			}
			catch (Exception ex)
			{
				if (ex is CodeEE) throw;
				throw new CodeEE(Name + ": " + ex.ToString());
			}
		}
	}

	public sealed class GraphicsSetBrushMethod : FunctionMethod
	{
		public GraphicsSetBrushMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = [EraType.Integer, EraType.Integer];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			if (Config.TextDrawingMode == TextDrawingMode.WINAPI)
				// throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGDIPLUSOnly, Name));
				throw new CodeEE(string.Format(trerror.GDIPlusOnly.Text, Name));
			try
			{
				GraphicsImage g = ReadGraphics(Name, exm, arguments, 0);
				if (!g.IsCreated)
					return 0;
				Color c = ReadColor(Name, exm, arguments, 1);
				g.GSetBrush(new SolidBrush(c));
				return 1;
			}
			catch (Exception ex)
			{
				if (ex is CodeEE) throw;
				throw new CodeEE(Name + ": " + ex.ToString());
			}
		}
	}

	#region EE_GDRAWTEXT追加に伴いGSETFONTを改良
	public sealed class GraphicsSetFontMethod : FunctionMethod
	{
		public GraphicsSetFontMethod()
		{
			ReturnType = EraType.Integer;
			// argumentTypeArray = new EraType[] { EraType.Integer, EraType.String, EraType.Integer };
			// argumentTypeArray = new EraType[] { EraType.Integer, EraType.String, EraType.Integer, EraType.Integer };
			argumentTypeArrayEx = [
					new ArgTypeList{ ArgTypes = { ArgType.Int, ArgType.String, ArgType.Int, ArgType.Int }, OmitStart = 2 }
				];
			CanRestructure = false;
		}
		//public override string CheckArgumentType(string name, IOperandTerm[] arguments)
		//{
		//	if (arguments.Count > 2)
		//		return null;
		//	return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentNum1, name, 2);
		//}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			if (Config.TextDrawingMode == TextDrawingMode.WINAPI)
				// throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGDIPLUSOnly, Name));
				throw new CodeEE(string.Format(trerror.GDIPlusOnly.Text, Name));
			try
			{
				GraphicsImage g = ReadGraphics(Name, exm, arguments, 0);
				if (!g.IsCreated)
					return 0;
				string fontname = arguments[1].GetStrValue(exm);
				long fontsize = arguments[2].GetIntValue(exm);
				FontStyle fs = FontStyle.Regular;
				if (arguments.Count > 3)
				{
					long style = arguments[3].GetIntValue(exm);

					if ((style & 1) != 0)
						fs |= FontStyle.Bold;
					if ((style & 2) != 0)
						fs |= FontStyle.Italic;
					if ((style & 4) != 0)
						fs |= FontStyle.Strikeout;
					if ((style & 8) != 0)
						fs |= FontStyle.Underline;
				}

				SKFont styledFont;
				try
				{
					#region EE_フォントファイル対応
					foreach (FontFamily ff in GlobalStatic.Pfc.Families)
					{
						if (ff.Name == fontname)
						{
							styledFont = FontFactory.GetFont(ff.Name, fs, fontsize);
							goto foundfont;
						}
					}
					// styledFont = new Font(fontname, fontsize, FontStyle.Regular, GraphicsUnit.Pixel);
					styledFont = FontFactory.GetFont(fontname, fs, fontsize);

				}
				catch
				{
					return 0;
				}
			foundfont:
				#endregion
				// canvas.GSetFont(styledFont);
				g.GSetFont(styledFont, fs);
				return 1;
			}
			catch (Exception ex)
			{
				if (ex is CodeEE) throw;
				throw new CodeEE(Name + ": " + ex.ToString());
			}
		}
	}
	#endregion

	public sealed class GraphicsSetPenMethod : FunctionMethod
	{
		public GraphicsSetPenMethod()
		{
			ReturnType = EraType.Integer;
			// 私家版のバグだと思う
			// argumentTypeArray = new EraType[] { EraType.Integer, EraType.Integer };
			argumentTypeArray = [EraType.Integer, EraType.Integer, EraType.Integer];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			if (Config.TextDrawingMode == TextDrawingMode.WINAPI)
				// throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGDIPLUSOnly, Name));
				throw new CodeEE(string.Format(trerror.GDIPlusOnly.Text, Name));
			try
			{
				GraphicsImage g = ReadGraphics(Name, exm, arguments, 0);
				if (!g.IsCreated)
					return 0;
				Color c = ReadColor(Name, exm, arguments, 1);
				long width = arguments[2].GetIntValue(exm);
				g.GSetPen(new Pen(c, width));
				return 1;
			}
			catch (Exception ex)
			{
				if (ex is CodeEE) throw;
				throw new CodeEE(Name + ": " + ex.ToString());
			}
		}
	}

	#region EE_GDASHSTYLE
	public sealed class GraphicsSetDashStyleMethod : FunctionMethod
	{
		public GraphicsSetDashStyleMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = [EraType.Integer, EraType.Integer, EraType.Integer];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			if (Config.TextDrawingMode == TextDrawingMode.WINAPI)
				// throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGDIPLUSOnly, Name));
				throw new CodeEE(string.Format(trerror.GDIPlusOnly.Text, Name));
			try
			{
				GraphicsImage g = ReadGraphics(Name, exm, arguments, 0);
				if (!g.IsCreated)
					return 0;

				g.GDashStyle(arguments[1].GetIntValue(exm), arguments[2].GetIntValue(exm));
				return 1;
			}
			catch (Exception ex)
			{
				if (ex is CodeEE) throw;
				throw new CodeEE(Name + ": " + ex.ToString());
			}
		}
	}
	#endregion

	#region EE_GDRAWTEXT
	public sealed class GraphicsDrawStringMethod : FunctionMethod
	{
		public GraphicsDrawStringMethod()
		{
			ReturnType = EraType.Integer;
			// argumentTypeArray = new EraType[] { EraType.Integer, EraType.String, EraType.Integer, EraType.Integer };
			argumentTypeArrayEx = [
					new ArgTypeList{ ArgTypes = { ArgType.Int, ArgType.String, ArgType.Int, ArgType.Int }, OmitStart = 2 }
				];
			CanRestructure = false;
		}

		//public override string CheckArgumentType(string name, IOperandTerm[] arguments)
		//{
		//	if (arguments.Count < 2)
		//		return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentNum1, name, 2);
		//	if (arguments.Count > 4)
		//		return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentNum2, name);
		//	if (arguments.Count != 2 && arguments.Count != 4)
		//		return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentNum0, name);

		//	for (int i = 0; i < arguments.Count; i++)
		//	{
		//		if (arguments[i] == null)
		//			return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentNotNullable0, name, i + 1);

		//		if (i < argumentTypeArray.Length && argumentTypeArray[i] != arguments[i].GetOperandType())
		//			return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentType0, name, i + 1);
		//	}
		//	if (arguments.Count <= 4)
		//		return null;
		//	return null;
		//}

		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			if (Config.TextDrawingMode == TextDrawingMode.WINAPI)
				// throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGDIPLUSOnly, Name));
				throw new CodeEE(string.Format(trerror.GDIPlusOnly.Text, Name));
			try
			{
				GraphicsImage g = ReadGraphics(Name, exm, arguments, 0);
				if (!g.IsCreated)
					return 0;
				string text = arguments[1].GetStrValue(exm);
				if (arguments.Count == 2)
				{
					g.GDrawString(text, 0, 0);
				}
				else if (arguments.Count == 4)
				{
					Point p = ReadPoint(Name, exm, arguments, 2);
					g.GDrawString(text, p.X, p.Y);
				}
				//生成する画像のサイズを取得
				try
				{
					var bitmap = new SKBitmap(16, 16);
					//Graphics canvas = Graphics.FromImage(bitmap);
					var graphics = new SKCanvas(bitmap);
					SKFont font = g.Fnt;
					using var paint = new SKPaint();
					if (font == null)
					{
						//font = new Font(Config.FontName, 100, GlobalStatic.Console.StringStyle.FontStyle, GraphicsUnit.Pixel);
						paint.Typeface = SKTypeface.FromFamilyName(Config.FontName);
						paint.TextSize = Config.FontSize;
						paint.Style = (SKPaintStyle)GlobalStatic.Console.StringStyle.FontStyle;
					}
					else
					{
						paint.Typeface = font.Typeface;
						paint.TextSize = font.Size;
						paint.Style = (SKPaintStyle)g.Fontstyle;
					}

					var size = paint.MeasureText(text);

					var resultArray = exm.VEvaluator.RESULT_ARRAY;
					resultArray[1] = (long)size;
					resultArray[2] = (long)paint.TextSize;

					bitmap.Dispose();
					graphics.Dispose();
				}
				catch
				{
					var resultArray = exm.VEvaluator.RESULT_ARRAY;
					resultArray[1] = 0;
					resultArray[2] = Config.FontSize;
				}
				return 1;
			}
			catch (Exception ex)
			{
				if (ex is CodeEE) throw;
				throw new CodeEE(Name + ": " + ex.ToString());
			}
		}
	}
	#endregion
	#region EE_GGETTEXTSIZE
	public sealed class GraphicsGetTextSizeMethod : FunctionMethod
	{
		public GraphicsGetTextSizeMethod()
		{
			ReturnType = EraType.Integer;
			// argumentTypeArray = new EraType[] { EraType.String, EraType.String, EraType.Integer, EraType.Integer };
			argumentTypeArrayEx = [
					new ArgTypeList{ ArgTypes = { ArgType.String, ArgType.String, ArgType.Int, ArgType.Int }, OmitStart = 3 }
				];
			CanRestructure = false;
		}
		//public override string CheckArgumentType(string name, IOperandTerm[] arguments)
		//{
		//	if (arguments.Count > 2)
		//		return null;
		//	return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentNum1, name, 2);
		//}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			if (Config.TextDrawingMode == TextDrawingMode.WINAPI)
				// throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGDIPLUSOnly, Name));
				throw new CodeEE(string.Format(trerror.GDIPlusOnly.Text, Name));
			try
			{
				string text = arguments[0].GetStrValue(exm);
				//生成する画像のサイズを取得
				string fontname = arguments[1].GetStrValue(exm);
				long fontsize = arguments[2].GetIntValue(exm);
				FontStyle fs = FontStyle.Regular;
				if (arguments.Count > 3)
				{
					long style = arguments[3].GetIntValue(exm);
					if ((style & 1) != 0)
						fs |= FontStyle.Bold;
					if ((style & 2) != 0)
						fs |= FontStyle.Italic;
					if ((style & 4) != 0)
						fs |= FontStyle.Strikeout;
					if ((style & 8) != 0)
						fs |= FontStyle.Underline;
				}
				Font fnt = new(fontname, fontsize, fs, GraphicsUnit.Pixel);
				var bitmap = new Bitmap(16, 16);
				//Graphics canvas = Graphics.FromImage(bitmap);
				var graphics = Graphics.FromImage(bitmap);
				var size = graphics.MeasureString(text, fnt, int.MaxValue, StringFormat.GenericTypographic);

				//TextRenderer
				//Size tsize = TextRenderer.MeasureText(canvas, text, fnt,
				//    new Size(2000, 2000), TextFormatFlags.NoPadding);
				var resultArray = exm.VEvaluator.RESULT_ARRAY;
				//resultArray[1] = (Int64)tsize.Width;
				resultArray[1] = (long)size.Height;
				return (long)size.Width;
			}
			catch (Exception ex)
			{
				if (ex is CodeEE) throw;
				throw new CodeEE(Name + ": " + ex.ToString());
			}
		}
	}
	#endregion
	#region EE_GDRAWGWITHROTATE
	// 使われてない
	//public sealed class GraphicsRotateMethod : FunctionMethod
	//{
	//	public GraphicsRotateMethod()
	//	{
	//		ReturnType = EraType.Integer;
	//		argumentTypeArray = new EraType[] { EraType.Integer, EraType.Integer, EraType.Integer, EraType.Integer };
	//		CanRestructure = false;
	//	}
	//	public override string CheckArgumentType(string name, IOperandTerm[] arguments)
	//	{
	//		if (arguments.Count < 2)
	//			return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentNum1, name, 2);
	//		if (arguments.Count > 4)
	//			return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentNum2, name);
	//		if (arguments.Count != 2 && arguments.Count != 4)
	//			return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentNum0, name);
	//		return null;
	//	}
	//	public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
	//	{
	//		if (Config.TextDrawingMode == TextDrawingMode.WINAPI)
	//			throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGDIPLUSOnly, Name));
	//		GraphicsImage canvas = ReadGraphics(Name, exm, arguments, 0);
	//		if (!canvas.IsCreated)
	//			return 0;
	//		Int64 angle = arguments[1].GetIntValue(exm);

	//		//座標省略してたらx/2,y/2で渡す
	//		if (arguments.Count == 2)
	//		{
	//			canvas.GRotate(angle, canvas.Width / 2, canvas.Height / 2);
	//		}
	//		else
	//		{
	//			Point p = ReadPoint(Name, exm, arguments, 2);
	//			canvas.GRotate(angle, p.X, p.Y);
	//		}
	//		return 1;
	//	}
	//}
	public sealed class GraphicsDrawGWithRotateMethod : FunctionMethod
	{
		public GraphicsDrawGWithRotateMethod()
		{
			ReturnType = EraType.Integer;
			// argumentTypeArray = new EraType[] { EraType.Integer, EraType.Integer, EraType.Integer, EraType.Integer, EraType.Integer };
			argumentTypeArrayEx = [
					new ArgTypeList{ ArgTypes = { ArgType.Int, ArgType.Int, ArgType.Int, ArgType.Int, ArgType.Int }, OmitStart = 3 }
				];
			CanRestructure = false;
		}
		//public override string CheckArgumentType(string name, IOperandTerm[] arguments)
		//{
		//	if (arguments.Count < 3)
		//		return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentNum1, name, 3);
		//	if (arguments.Count > 5)
		//		return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentNum2, name);
		//	if (arguments.Count != 3 && arguments.Count != 5)
		//		return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentNum0, name);
		//	return null;
		//}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			if (Config.TextDrawingMode == TextDrawingMode.WINAPI)
				// throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGDIPLUSOnly, Name));
				throw new CodeEE(string.Format(trerror.GDIPlusOnly.Text, Name));
			try
			{
				GraphicsImage dest = ReadGraphics(Name, exm, arguments, 0);
				if (!dest.IsCreated)
					return 0;
				GraphicsImage src = ReadGraphics(Name, exm, arguments, 1);
				if (!src.IsCreated)
					return 0;
				long angle = arguments[2].GetIntValue(exm);

				//座標省略してたらx/2,y/2で渡す
				if (arguments.Count == 3)
				{
					dest.GDrawGWithRotate(src.RealBitmap, angle, src.Width / 2, src.Height / 2);
				}
				else
				{
					Point p = ReadPoint(Name, exm, arguments, 3);
					dest.GDrawGWithRotate(src.RealBitmap, angle, p.X, p.Y);
				}
				return 1;
			}
			catch (Exception ex)
			{
				if (ex is CodeEE) throw;
				throw new CodeEE(Name + ": " + ex.ToString());
			}
		}
	}
	#endregion
	#region EE_失敗作
	//brushの参照がうまくいかないので保留
	/**
	public sealed class GraphicsGetBrushMethod : FunctionMethod
	{
		public GraphicsGetBrushMethod()
		{
			ReturnType = EraType.Integer;
			 argumentTypeArray = [EraType.Integer];
			CanRestructure = false;
		}
		public override Int64 GetIntValue(ExpressionMediator exm, IOperandTerm[] arguments)
		{
			Color c = 
			GraphicsImage canvas = ReadGraphics(Name, exm, arguments, 0);
			return (SolidBrush());
		}
	}
	**/
	#endregion
	#region EE_GDRAWLINE
	public sealed class GraphicsDrawLineMethod : FunctionMethod
	{
		public GraphicsDrawLineMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = [EraType.Integer, EraType.Integer, EraType.Integer, EraType.Integer, EraType.Integer];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			if (Config.TextDrawingMode == TextDrawingMode.WINAPI)
				throw new CodeEE(string.Format(trerror.GDIPlusOnly.Text, Name));
			try
			{
				GraphicsImage g = ReadGraphics(Name, exm, arguments, 0);
				if (!g.IsCreated)
					return 0;
				Point fromP = ReadPoint(Name, exm, arguments, 1);
				Point forP = ReadPoint(Name, exm, arguments, 3);
				g.GDrawLine(fromP.X, fromP.Y, forP.X, forP.Y);
				return 1;
			}
			catch (Exception ex)
			{
				if (ex is CodeEE) throw;
				throw new CodeEE(Name + ": " + ex.ToString());
			}
		}
	}
	#endregion

	public sealed class SpriteStateMethod : FunctionMethod
	{
		public SpriteStateMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = [EraType.String];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			string imgname = arguments[0].GetStrValue(exm);
			switch (Name)
			{
				case "SPRITECREATED":
					return AppContents.GetSprite_OnlyCheckExists(imgname) ? 1 : 0;
				case "SPRITEWIDTH":
				case "SPRITEHEIGHT":
				case "SPRITEPOSX":
				case "SPRITEPOSY":
					ASprite img = AppContents.GetSprite(imgname);
					if (img == null)
						return 0;
					if (Name == "SPRITEWIDTH")
						return img.DestBaseSize.Width;
					if (Name == "SPRITEHEIGHT")
						return img.DestBaseSize.Height;
					if (Name == "SPRITEPOSX")
						return img.DestBasePosition.X;
					return img.DestBasePosition.Y;
			}
			throw new ExeEE("SpriteStateMethod:" + Name + ":異常な分岐");
		}
	}

	public sealed class SpriteSetPosMethod : FunctionMethod
	{
		public SpriteSetPosMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = [EraType.String, EraType.Integer, EraType.Integer];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			string imgname = arguments[0].GetStrValue(exm);
			ASprite img = AppContents.GetSprite(imgname);
			if (img == null)
				return 0;
			Point p = ReadPoint(Name, exm, arguments, 1);
			switch (Name)
			{
				case "SPRITEMOVE":
					img.DestBasePosition.Offset(p);
					return 1;
				case "SPRITESETPOS":
					img.DestBasePosition = p;
					return 1;
			}
			throw new ExeEE("SpriteStateMethod:" + Name + ":異常な分岐");
		}
	}

	public sealed class SpriteGetColorMethod : FunctionMethod
	{
		public SpriteGetColorMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = [EraType.String, EraType.Integer, EraType.Integer];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			string imgname = arguments[0].GetStrValue(exm);
			ASprite img = AppContents.GetSprite(imgname);
			//他と違って失敗は0ではなく負の値
			if (img == null || !img.IsCreated)
				return -1;
			Point p = ReadPoint(Name, exm, arguments, 1);
			if (p.X < 0 || p.X >= img.DestBaseSize.Width)
				return -1;
			if (p.Y < 0 || p.Y >= img.DestBaseSize.Height)
				return -1;
			var c = img.SpriteGetColor(p.X, p.Y);
			//Color.ToArgb()はInt32の負の値をとることがあり、Int64にうまく変換できない？（と思ったが気のせいだった
			return (((long)c.Alpha) << 24 + c.Red << 16 + c.Green << 8 + c.Blue) & 0xFFFFFFFFL;
		}
	}

	public sealed class ClientSizeMethod : FunctionMethod
	{
		public ClientSizeMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = [];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			switch (Name)
			{
				case "CLIENTWIDTH":
					return exm.Console.ClientWidth;
				case "CLIENTHEIGHT":
					return exm.Console.ClientHeight;
			}
			throw new ExeEE("ClientSize:" + Name + ":異常な分岐");
		}
	}

	public sealed class GraphicsCreateMethod : FunctionMethod
	{
		public GraphicsCreateMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = [EraType.Integer, EraType.Integer, EraType.Integer];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			if (Config.TextDrawingMode == TextDrawingMode.WINAPI)
				// throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGDIPLUSOnly, Name));
				throw new CodeEE(string.Format(trerror.GDIPlusOnly.Text, Name));
			try
			{
				GraphicsImage g = ReadGraphics(Name, exm, arguments, 0);
				if (g.IsCreated)
				{
					System.Diagnostics.Debug.WriteLine($"[WARNING] GCREATE: GID {(int)arguments[0].GetIntValue(exm)} already exists (Width={g.Width}, Height={g.Height}), returning 0");
					return 0;
				}

				Point p = ReadPoint(Name, exm, arguments, 1);
				int width = p.X; int height = p.Y;
				if (width <= 0)//{0}関数:GraphicsのWidthに0以下の値({1})が指定されました
							   // throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGWidth0, Name, width));
					throw new CodeEE(string.Format(trerror.GParamIsNegative.Text, Name, "Width", width));
				else if (width > AbstractImage.MAX_IMAGESIZE)//{0}関数:GraphicsのWidthに{2}以上の値({1})が指定されました
															 // throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGWidth1, Name, width, AbstractImage.MAX_IMAGESIZE));
					throw new CodeEE(string.Format(trerror.GParamTooLarge.Text, Name, "Width", AbstractImage.MAX_IMAGESIZE, width));
				if (height <= 0)//{0}関数:GraphicsのHeightに0以下の値({1})が指定されました
								// throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGHeight0, Name, height));
					throw new CodeEE(string.Format(trerror.GParamIsNegative.Text, Name, "Height", height));
				else if (height > AbstractImage.MAX_IMAGESIZE)//{0}関数:GraphicsのHeightに{2}以上の値({1})が指定されました
															  // throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGHeight1, Name, height, AbstractImage.MAX_IMAGESIZE));
					throw new CodeEE(string.Format(trerror.GParamTooLarge.Text, Name, "Height", AbstractImage.MAX_IMAGESIZE, height));

				g.GCreate(width, height, false);
				return 1;
			}
			catch (Exception ex)
			{
				if (ex is CodeEE) throw;
				throw new CodeEE(Name + ": " + ex.ToString());
			}
		}
	}

	public sealed class GraphicsCreateFromFileMethod : FunctionMethod
	{
		public GraphicsCreateFromFileMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArrayEx = [
					new ArgTypeList{ ArgTypes = { ArgType.Int, ArgType.String, ArgType.Int }, OmitStart = 2 }
				];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			if (Config.TextDrawingMode == TextDrawingMode.WINAPI)
				// throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGDIPLUSOnly, Name));
				throw new CodeEE(string.Format(trerror.GDIPlusOnly.Text, Name));
			GraphicsImage g = ReadGraphics(Name, exm, arguments, 0);
			if (g.IsCreated)
				return 0;

			string filename = arguments[1].GetStrValue(exm);
			bool isRelative = false;
			if (arguments.Count > 2)
				isRelative = arguments[2].GetIntValue(exm) != 0;

			SKBitmap bmp = null;
			try
			{
				string filepath = filename;
				if (!Path.IsPathRooted(filepath))
				{
					if (isRelative)
						filepath = filename;
					else
						filepath = Program.ContentDir + filename;
				}
				if (!File.Exists(filepath))
					return 0;
				#region EM_私家版_webp
				bmp = SKBitmap.Decode(filepath);
				//bmp = Utils.LoadImage(filepath);
				if (bmp == null) return 0;
				#endregion
				if (bmp.Width > AbstractImage.MAX_IMAGESIZE || bmp.Height > AbstractImage.MAX_IMAGESIZE)
					return 0;
				g.GCreateFromF(bmp, Config.TextDrawingMode == TextDrawingMode.WINAPI);
			}
			catch (Exception e)
			{
				if (e is CodeEE)
					throw;
			}
			finally
			{
				if (bmp != null)
					bmp.Dispose();
			}
			//画像ファイルではなかった、などによる失敗
			if (!g.IsCreated)
				return 0;
			return 1;
		}
	}

	public sealed class GraphicsDisposeMethod : FunctionMethod
	{
		public GraphicsDisposeMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = [EraType.Integer];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			if (Config.TextDrawingMode == TextDrawingMode.WINAPI)
				// throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGDIPLUSOnly, Name));
				throw new CodeEE(string.Format(trerror.GDIPlusOnly.Text, Name));
			try
			{
				GraphicsImage g = ReadGraphics(Name, exm, arguments, 0);
				if (!g.IsCreated)
					return 0;
				g.GDispose();
				return 1;
			}
			catch (Exception ex)
			{
				if (ex is CodeEE) throw;
				throw new CodeEE(Name + ": " + ex.ToString());
			}
		}
	}
	/// <summary>
	/// SPRITECREATE(str imgName, int gID, int x, int y, int width, int height)
	/// SPRITECREATE(str imgName, int gID)
	/// </summary>
	public sealed class SpriteCreateMethod : FunctionMethod
	{
		public SpriteCreateMethod()
		{
			ReturnType = EraType.Integer;
			//  argumentTypeArray = [EraType.String];
			argumentTypeArrayEx = [
					new ArgTypeList{ ArgTypes = { ArgType.String, ArgType.Int } },
					new ArgTypeList{ ArgTypes = { ArgType.String, ArgType.Int, ArgType.Int, ArgType.Int, ArgType.Int, ArgType.Int } },
					// 新增：支持8个参数，最后两个为 destWidth, destHeight
					new ArgTypeList{ ArgTypes = { ArgType.String, ArgType.Int, ArgType.Int, ArgType.Int, ArgType.Int, ArgType.Int, ArgType.Int, ArgType.Int } },
					// 10参数: 截取 + 偏移 + 缩放 (SrcRect + Pos + DestSize)
					new ArgTypeList{ ArgTypes = { ArgType.String, ArgType.Int, ArgType.Int, ArgType.Int, ArgType.Int, ArgType.Int, ArgType.Int, ArgType.Int, ArgType.Int, ArgType.Int } },
				];
			CanRestructure = false;
		}
		//public override string CheckArgumentType(string name, IOperandTerm[] arguments)
		//{

		//	if (arguments.Count < 2)
		//		return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentNum1, name, 2);
		//	if (arguments.Count > 6)
		//		return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentNum2, name);
		//	if (arguments[0] == null)
		//		return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentNotNullable0, name, 0 + 1);
		//	if (arguments[1] == null)
		//		return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentNotNullable0, name, 1 + 1);
		//	if (arguments[0].GetEraType() != EraType.String)
		//		return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentType0, name, 0 + 1);
		//	if (arguments[1].GetOperandType() != EraType.Integer)
		//		return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentType0, name, 1 + 1);
		//	if (arguments.Count == 2)
		//		return null;
		//	if (arguments.Count != 6)
		//		return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentNum0, name);
		//	for (int i = 2; i < arguments.Count; i++)
		//	{
		//		if (arguments[i] == null)
		//			return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentNotNullable0, name, i + 1);
		//		if (arguments[i].GetOperandType() != EraType.Integer)
		//			return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentType0, name, i + 1);
		//	}
		//	return null;
		//}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			if (Config.TextDrawingMode == TextDrawingMode.WINAPI)
				// throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGDIPLUSOnly, Name));
				throw new CodeEE(string.Format(trerror.GDIPlusOnly.Text, Name));
			try
			{
				string imgname = arguments[0].GetStrValue(exm);
				if (string.IsNullOrEmpty(imgname))
					return 0;
				ASprite img = AppContents.GetSprite(imgname);
				if (img != null && img.IsCreated)
					return 0;
				GraphicsImage g = ReadGraphics(Name, exm, arguments, 1);
				if (!g.IsCreated)
					return 0;

				Rectangle rect = new(0, 0, g.Width, g.Height);
				Point pos = new Point(0, 0);
				Size destSize = new Size(g.Width, g.Height);
				
				if (arguments.Count >= 6)
				{//四角形は正でも負でもよいが親画像の外を指してはいけない
					rect = ReadRectangle(Name, exm, arguments, 2);
					// 默认情况下，目标尺寸 = 源矩形尺寸
					destSize = rect.Size;
					#region EM_私家版_SPRITECREATE範囲制限緩和
					//if (rect.X + rect.Width < 0 || rect.X + rect.Width > canvas.Width || rect.Y + rect.Height < 0 || rect.Y + rect.Height > canvas.Height)
					//	throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodCIMGCreateOutOfRange0, Name));
					if (!rect.IntersectsWith(new Rectangle(0, 0, g.Width, g.Height)))
						// throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodCIMGCreateOutOfRange0, Name));
						throw new CodeEE(string.Format(trerror.ImgRefOutOfRange.Text, Name));
					#endregion
				}
				// 处理偏移坐标 (PosX, PosY) - 参数索引 6, 7
				if (arguments.Count >= 8)
				{
					int px = (int)arguments[6].GetIntValue(exm);
					int py = (int)arguments[7].GetIntValue(exm);
					pos = new Point(px, py);
				}

				// 处理目标尺寸 (DestW, DestH) - 参数索引 8, 9
				if (arguments.Count == 10)
				{
					int dw = (int)arguments[8].GetIntValue(exm);
					int dh = (int)arguments[9].GetIntValue(exm);
					// 保持正数（虽然负数在某些绘图逻辑里可能意味着翻转，但在 Emuera CSV 解析里通常取绝对值或报错，这里做个绝对值处理比较安全）
					if (dw < 0) dw = -dw;
					if (dh < 0) dh = -dh;
					destSize = new Size(dw, dh);
				}
				// 调用更新后的 CreateSpriteG
				AppContents.CreateSpriteG(imgname, g, rect, pos, destSize);
				return 1;
			}
			catch (Exception ex)
			{
				if (ex is CodeEE) throw;
				throw new CodeEE(Name + ": " + ex.ToString());
			}
		}
	}

	public sealed class SpriteCreateFromFileMethod : FunctionMethod
	{
		public SpriteCreateFromFileMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArrayEx = [
					new ArgTypeList{ ArgTypes = { ArgType.String, ArgType.String, ArgType.Int }, OmitStart = 2 }
				];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			if (Config.TextDrawingMode == TextDrawingMode.WINAPI)
				throw new CodeEE(string.Format(trerror.GDIPlusOnly.Text, Name));
			string imgname = arguments[0].GetStrValue(exm);
			if (string.IsNullOrEmpty(imgname))
				return 0;

			string filename = arguments[1].GetStrValue(exm);
			bool isRelative = false;
			if (arguments.Count > 2)
				isRelative = arguments[2].GetIntValue(exm) != 0;

			try
			{
				string filepath = filename;
				if (!Path.IsPathRooted(filepath))
				{
					if (isRelative)
						filepath = filename;
					else
						filepath = Program.ContentDir + filename;
				}
				if (!File.Exists(filepath))
					return 0;

				if (AppContents.CreateSpriteFromFileDynamic(imgname, filepath))
					return 1;
			}
			catch (Exception e)
			{
				if (e is CodeEE)
					throw;
				return 0;
			}
			return 0;
		}
	}


	public sealed class SpriteDisposeMethod : FunctionMethod
	{
		public SpriteDisposeMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = [EraType.String];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			string imgname = arguments[0].GetStrValue(exm);
			ASprite img = AppContents.GetSprite(imgname);
			if (img == null || !img.IsCreated)
				return 0;
			AppContents.SpriteDispose(imgname);
			return 1;
		}
	}

	public sealed class SpriteDisposeAllMethod : FunctionMethod
	{
		public SpriteDisposeAllMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = [EraType.Integer];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			return AppContents.SpriteDisposeAll(arguments[0].GetIntValue(exm) != 0);
		}
	}


	/// <summary>
	/// GCLEAR(int ID, int cARGB)
	/// </summary>
	public sealed class GraphicsClearMethod : FunctionMethod
	{
		#region EM_私家版_GCLEAR拡張
		public GraphicsClearMethod()
		{
			ReturnType = EraType.Integer;
			// argumentTypeArray = new EraType[] { EraType.Integer, EraType.Integer };
			argumentTypeArrayEx = [
					new ArgTypeList{ ArgTypes = { ArgType.Int, ArgType.Int } },
					new ArgTypeList{ ArgTypes = { ArgType.Int, ArgType.Int, ArgType.Int, ArgType.Int, ArgType.Int, ArgType.Int } }
				];
			argumentTypeArray = null;
			CanRestructure = false;
		}
		//public override string CheckArgumentType(string name, IOperandTerm[] arguments)
		//{

		//	if (arguments.Count != 2 && arguments.Count != 6)
		//		return string.Format("{0}関数には2つもしくは6つの引数が必要です", name);
		//	for (int i = 0; i < arguments.Count; i++)
		//	{
		//		if (arguments[i] == null)
		//			return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentNotNullable0, name, i + 1);
		//		if (arguments[i].GetOperandType() != EraType.Integer)
		//			return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentType0, name, i + 1);
		//	}
		//	return null;
		//}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			if (Config.TextDrawingMode == TextDrawingMode.WINAPI)
				// throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGDIPLUSOnly, Name));
				throw new CodeEE(string.Format(trerror.GDIPlusOnly.Text, Name));
			try
			{
				GraphicsImage g = ReadGraphics(Name, exm, arguments, 0);
				Color c = ReadColor(Name, exm, arguments, 1);
				if (!g.IsCreated)
					return 0;
				if (arguments.Count == 2)
					g.GClear(c);
				else
					g.GClear(c, (int)arguments[2].GetIntValue(exm), (int)arguments[3].GetIntValue(exm), (int)arguments[4].GetIntValue(exm), (int)arguments[5].GetIntValue(exm));
				return 1;
			}
			catch (Exception ex)
			{
				if (ex is CodeEE) throw;
				throw new CodeEE(Name + ": " + ex.ToString());
			}
		}
		#endregion
	}

	/// <summary>
	/// GFILLRECTANGLE(int ID, int cARGB, int x, int y, int width, int height)
	/// </summary>
	public sealed class GraphicsFillRectangleMethod : FunctionMethod
	{
		public GraphicsFillRectangleMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = [EraType.Integer, EraType.Integer, EraType.Integer, EraType.Integer, EraType.Integer];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			if (Config.TextDrawingMode == TextDrawingMode.WINAPI)
				// throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGDIPLUSOnly, Name));
				throw new CodeEE(string.Format(trerror.GDIPlusOnly.Text, Name));
			try
			{
				GraphicsImage g = ReadGraphics(Name, exm, arguments, 0);
				if (!g.IsCreated)
					return 0;
				Rectangle rect = ReadRectangle(Name, exm, arguments, 1);
				g.GFillRectangle(rect);
				return 1;
			}
			catch (Exception ex)
			{
				if (ex is CodeEE) throw;
				throw new CodeEE(Name + ": " + ex.ToString());
			}
		}
	}

	/// <summary>
	/// G_POLYGON_DRAW(int ID)
	/// </summary>
	public sealed class GraphicsDrawPolygonMethod : FunctionMethod
	{
		public GraphicsDrawPolygonMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = [EraType.Integer];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			if (Config.TextDrawingMode == TextDrawingMode.WINAPI)
				throw new CodeEE(string.Format(trerror.GDIPlusOnly.Text, Name));
			try
			{
				GraphicsImage g = ReadGraphics(Name, exm, arguments, 0);
				if (!g.IsCreated)
					return 0;
				g.GDrawPolygon();
				return 1;
			}
			catch (Exception ex)
			{
				if (ex is CodeEE) throw;
				throw new CodeEE(Name + ": " + ex.ToString());
			}
		}
	}

	/// <summary>
	/// G_POLYGON_FILL(int ID)
	/// </summary>
	public sealed class GraphicsFillPolygonMethod : FunctionMethod
	{
		public GraphicsFillPolygonMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = [EraType.Integer];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			if (Config.TextDrawingMode == TextDrawingMode.WINAPI)
				throw new CodeEE(string.Format(trerror.GDIPlusOnly.Text, Name));
			try
			{
				GraphicsImage g = ReadGraphics(Name, exm, arguments, 0);
				if (!g.IsCreated)
					return 0;
				g.GFillPolygon();
				return 1;
			}
			catch (Exception ex)
			{
				if (ex is CodeEE) throw;
				throw new CodeEE(Name + ": " + ex.ToString());
			}
		}
	}

	/// <summary>
	/// G_POLYGON_POINT_ADD(int ID, int x, int y)
	/// </summary>
	public sealed class GraphicsPolygonPointAddMethod : FunctionMethod
	{
		public GraphicsPolygonPointAddMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = [EraType.Integer, EraType.Integer, EraType.Integer];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			if (Config.TextDrawingMode == TextDrawingMode.WINAPI)
				throw new CodeEE(string.Format(trerror.GDIPlusOnly.Text, Name));
			try
			{
				GraphicsImage g = ReadGraphics(Name, exm, arguments, 0);
				if (!g.IsCreated)
					return 0;
				Point p = ReadPoint(Name, exm, arguments, 1);
				g.GDrawPolygonAddPoint(new SKPoint(p.X, p.Y));
				return 1;
			}
			catch (Exception ex)
			{
				if (ex is CodeEE) throw;
				throw new CodeEE(Name + ": " + ex.ToString());
			}
		}
	}

	/// <summary>
	/// G_POLYGON_POINT_CLEAR(int ID)
	/// </summary>
	public sealed class GraphicsPolygonPointClearMethod : FunctionMethod
	{
		public GraphicsPolygonPointClearMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = [EraType.Integer];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			if (Config.TextDrawingMode == TextDrawingMode.WINAPI)
				throw new CodeEE(string.Format(trerror.GDIPlusOnly.Text, Name));
			try
			{
				GraphicsImage g = ReadGraphics(Name, exm, arguments, 0);
				if (!g.IsCreated)
					return 0;
				g.GDrawPolygonClearPoint();
				return 1;
			}
			catch (Exception ex)
			{
				if (ex is CodeEE) throw;
				throw new CodeEE(Name + ": " + ex.ToString());
			}
		}
	}

	/// <summary>
	/// GDRAWG(int ID, int srcID, int destX, int destY, int destWidth, int destHeight, int srcX, int srcY, int srcWidth, int srcHeight)
	/// GDRAWG(int ID, int srcID, int destX, int destY, int destWidth, int destHeight, int srcX, int srcY, int srcWidth, int srcHeight, var CM)
	/// </summary>
	public sealed class GraphicsDrawGMethod : FunctionMethod
	{
		public GraphicsDrawGMethod()
		{
			ReturnType = EraType.Integer;
			// argumentTypeArray = null;
			argumentTypeArrayEx = [
					new ArgTypeList{ ArgTypes = { ArgType.Int, ArgType.Int,
							ArgType.Int, ArgType.Int, ArgType.Int, ArgType.Int,
							ArgType.Int, ArgType.Int, ArgType.Int, ArgType.Int, ArgType.RefInt2D | ArgType.AllowConstRef }, OmitStart = 10 },
					new ArgTypeList{ ArgTypes = { ArgType.Int, ArgType.Int,
							ArgType.Int, ArgType.Int, ArgType.Int, ArgType.Int,
							ArgType.Int, ArgType.Int, ArgType.Int, ArgType.Int, ArgType.RefInt3D | ArgType.AllowConstRef }, OmitStart = 10 },
				];
			CanRestructure = false;
			HasUniqueRestructure = true;
		}

		//public override string CheckArgumentType(string name, IOperandTerm[] arguments)
		//{
		//	if (arguments.Count < 10)
		//		return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentNum1, name, 10);
		//	if (arguments.Count > 11)
		//		return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentNum2, name);
		//	for (int i = 0; i < 10; i++)
		//	{
		//		if (arguments[i] == null)
		//			return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentNotNullable0, name, i + 1);
		//		if (EraType.Integer != arguments[i].GetOperandType())
		//			return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentType0, name, i + 1);
		//	}
		//	if (arguments.Count == 10)
		//		return null;
		//	if (!(arguments[10] is VariableTerm varToken) || !varToken.IsInteger || (!varToken.Identifier.IsArray2D && !varToken.Identifier.IsArray3D))
		//		return string.Format(Properties.Resources.SyntaxErrMesMethodGraphicsColorMatrix0, name);
		//	return null;
		//}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			if (Config.TextDrawingMode == TextDrawingMode.WINAPI)
				// throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGDIPLUSOnly, Name));
				throw new CodeEE(string.Format(trerror.GDIPlusOnly.Text, Name));
			try
			{
				GraphicsImage dest = ReadGraphics(Name, exm, arguments, 0);
				if (!dest.IsCreated)
					return 0;
				GraphicsImage src = ReadGraphics(Name, exm, arguments, 1);
				if (!src.IsCreated)
					return 0;
				Rectangle destRect = ReadRectangle(Name, exm, arguments, 2);
				Rectangle srcRect = ReadRectangle(Name, exm, arguments, 6);
				if (arguments.Count == 10 || arguments[10] == null)
				{
					dest.GDrawG(src, destRect, srcRect);
					return 1;
				}
				float[][] cm = ReadColormatrix(Name, exm, arguments, 10);
				dest.GDrawG(src, destRect, srcRect, cm);
				return 1;
			}
			catch (Exception ex)
			{
				if (ex is CodeEE) throw;
				throw new CodeEE(Name + ": " + ex.ToString());
			}
		}

		public override bool UniqueRestructure(ExpressionMediator exm, List<AExpression> arguments)
		{
			for (int i = 0; i < arguments.Count; i++)
			{
				if (arguments[i] == null)
					continue;
				//11番目の引数はColorMatrixの配列を指しているので定数にしてはいけない
				if (i == 10)
					arguments[i].Restructure(exm);
				else
					arguments[i] = arguments[i].Restructure(exm);
			}
			return false;
		}
	}

	/// <summary>
	/// GDRAWGWITHMASK(int ID, int srcID, int maskID, int destX, int destY)
	/// </summary>
	public sealed class GraphicsDrawGWithMaskMethod : FunctionMethod
	{
		public GraphicsDrawGWithMaskMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = [EraType.Integer, EraType.Integer, EraType.Integer, EraType.Integer, EraType.Integer];
			CanRestructure = false;
		}


		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			if (Config.TextDrawingMode == TextDrawingMode.WINAPI)
				// throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGDIPLUSOnly, Name));
				throw new CodeEE(string.Format(trerror.GDIPlusOnly.Text, Name));
			try
			{
				GraphicsImage dest = ReadGraphics(Name, exm, arguments, 0);
				if (!dest.IsCreated)
					return 0;
				GraphicsImage src = ReadGraphics(Name, exm, arguments, 1);
				if (!src.IsCreated)
					return 0;
				GraphicsImage mask = ReadGraphics(Name, exm, arguments, 2);
				if (!mask.IsCreated)
					return 0;
				if (src.Width != mask.Width || src.Height != mask.Height)
					return 0;
				Point destPoint = ReadPoint(Name, exm, arguments, 3);
				if (destPoint.X + src.Width > dest.Width || destPoint.Y + src.Height > dest.Height)
					return 0;
				dest.GDrawGWithMask(src, mask, destPoint);
				return 1;
			}
			catch (Exception ex)
			{
				if (ex is CodeEE) throw;
				throw new CodeEE(Name + ": " + ex.ToString());
			}
		}


	}

	/// <summary>
	/// GDRAWCIMG(int ID, str imgName)
	/// GDRAWCIMG(int ID, str imgName, int destX, int destY)
	/// GDRAWCIMG(int ID, str imgName, int destX, int destY, int destWidth, int destHeight)
	/// GDRAWCIMG(int ID, str imgName, int destX, int destY, int destWidth, int destHeight, var CM)
	/// </summary>
	public sealed class GraphicsDrawSpriteMethod : FunctionMethod
	{
		public GraphicsDrawSpriteMethod()
		{
			ReturnType = EraType.Integer;
			// argumentTypeArray = new EraType[] { EraType.Integer, EraType.String, EraType.Integer, EraType.Integer, EraType.Integer, EraType.Integer };
			argumentTypeArrayEx = [
					new ArgTypeList{ ArgTypes = { ArgType.Int, ArgType.String } },
					new ArgTypeList{ ArgTypes = { ArgType.Int, ArgType.String, ArgType.Int, ArgType.Int } },
					new ArgTypeList{ ArgTypes = { ArgType.Int, ArgType.String, ArgType.Int, ArgType.Int, ArgType.Int, ArgType.Int, ArgType.RefInt2D | ArgType.AllowConstRef }, OmitStart = 6 },
					new ArgTypeList{ ArgTypes = { ArgType.Int, ArgType.String, ArgType.Int, ArgType.Int, ArgType.Int, ArgType.Int, ArgType.RefInt3D | ArgType.AllowConstRef }, OmitStart = 6 },
				];
			CanRestructure = false;
			HasUniqueRestructure = true;
		}

		//public override string CheckArgumentType(string name, IOperandTerm[] arguments)
		//{
		//	if (arguments.Count < 2)
		//		return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentNum1, name, 2);
		//	if (arguments.Count > 7)
		//		return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentNum2, name);
		//	if (arguments.Count != 2 && arguments.Count != 4 && arguments.Count != 6 && arguments.Count != 7)
		//		return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentNum0, name);

		//	for (int i = 0; i < arguments.Count; i++)
		//	{
		//		if (arguments[i] == null)
		//			return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentNotNullable0, name, i + 1);

		//		if (i < argumentTypeArray.Length && argumentTypeArray[i] != arguments[i].GetOperandType())
		//			return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentType0, name, i + 1);
		//	}
		//	if (arguments.Count <= 6)
		//		return null;
		//	if (!(arguments[6] is VariableTerm varToken) || !varToken.IsInteger || (!varToken.Identifier.IsArray2D && !varToken.Identifier.IsArray3D))
		//		return string.Format(Properties.Resources.SyntaxErrMesMethodGraphicsColorMatrix0, name);
		//	return null;
		//}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			if (Config.TextDrawingMode == TextDrawingMode.WINAPI)
				// throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGDIPLUSOnly, Name));
				throw new CodeEE(string.Format(trerror.GDIPlusOnly.Text, Name));
			try
			{
				GraphicsImage dest = ReadGraphics(Name, exm, arguments, 0);
				if (!dest.IsCreated)
					return 0;

				string imgname = arguments[1].GetStrValue(exm);
				ASprite img = AppContents.GetSprite(imgname);
				if (img == null || !img.IsCreated)
					return 0;

				Rectangle destRect = new(0, 0, img.DestBaseSize.Width, img.DestBaseSize.Height);
				if (arguments.Count == 2)
				{
					dest.GDrawCImg(img, destRect);
					return 1;
				}
				if (arguments.Count == 4)
				{
					Point p = ReadPoint(Name, exm, arguments, 2);
					destRect.X = p.X;
					destRect.Y = p.Y;
					dest.GDrawCImg(img, destRect);
					return 1;
				}
				if (arguments.Count == 6)
				{
					destRect = ReadRectangle(Name, exm, arguments, 2);
					dest.GDrawCImg(img, destRect);
					return 1;
				}
				//if (arguments.Count == 7)
				destRect = ReadRectangle(Name, exm, arguments, 2);
				float[][] cm = ReadColormatrix(Name, exm, arguments, 6);
				dest.GDrawCImg(img, destRect, cm);
				return 1;
			}
			catch (Exception ex)
			{
				if (ex is CodeEE) throw;
				throw new CodeEE(Name + ": " + ex.ToString());
			}
		}

		public override bool UniqueRestructure(ExpressionMediator exm, List<AExpression> arguments)
		{
			for (int i = 0; i < arguments.Count; i++)
			{
				if (arguments[i] == null)
					continue;
				//7番目の引数はColorMatrixの配列を指しているので定数にしてはいけない
				if (i == 6)
					arguments[i].Restructure(exm);
				else
					arguments[i] = arguments[i].Restructure(exm);
			}
			return false;
		}
	}

	/// <summary>
	/// int SPRITEANIMECREATE (string name, int width, int height)
	/// </summary>
	public sealed class SpriteAnimeCreateMethod : FunctionMethod
	{
		public SpriteAnimeCreateMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = [EraType.String, EraType.Integer, EraType.Integer];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			if (Config.TextDrawingMode == TextDrawingMode.WINAPI)
				// throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGDIPLUSOnly, Name));
				throw new CodeEE(string.Format(trerror.GDIPlusOnly.Text, Name));
			try
			{
				string imgname = arguments[0].GetStrValue(exm);
				if (string.IsNullOrEmpty(imgname))
					return 0;
				//リソースチェック・既に存在しているならば失敗
				ASprite img = AppContents.GetSprite(imgname);
				if (img != null && img.IsCreated)
					return 0;
				Point pos = ReadPoint(Name, exm, arguments, 1);
				if (pos.X <= 0)//{0}関数:GraphicsのWidthに0以下の値({1})が指定されました
							   // throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGWidth0, Name, pos.X));
					throw new CodeEE(string.Format(trerror.GParamIsNegative.Text, Name, "Width", pos.X));
				else if (pos.X > AbstractImage.MAX_IMAGESIZE)//{0}関数:GraphicsのWidthに{2}以上の値({1})が指定されました
															 // throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGWidth1, Name, pos.X, AbstractImage.MAX_IMAGESIZE));
					throw new CodeEE(string.Format(trerror.GParamTooLarge.Text, Name, "Width", AbstractImage.MAX_IMAGESIZE, pos.X));
				if (pos.Y <= 0)//{0}関数:GraphicsのHeightに0以下の値({1})が指定されました
							   // throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGHeight0, Name, pos.Y));
					throw new CodeEE(string.Format(trerror.GParamIsNegative.Text, Name, "Height", pos.Y));
				else if (pos.Y > AbstractImage.MAX_IMAGESIZE)//{0}関数:GraphicsのHeightに{2}以上の値({1})が指定されました
															 // throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGHeight1, Name, pos.Y, AbstractImage.MAX_IMAGESIZE));
					throw new CodeEE(string.Format(trerror.GParamTooLarge.Text, Name, "Height", AbstractImage.MAX_IMAGESIZE, pos.Y));
				AppContents.CreateSpriteAnime(imgname, pos.X, pos.Y);
				return 1;
			}
			catch (Exception ex)
			{
				if (ex is CodeEE) throw;
				throw new CodeEE(Name + ": " + ex.ToString());
			}
		}
	}


	/// <summary>
	/// SPRITEANIMEADDFRAME (string name, int graphID, int x, int y, int width, int height, int offsetx, int offsety, int delay)
	/// </summary>
	public sealed class SpriteAnimeAddFrameMethod : FunctionMethod
	{
		public SpriteAnimeAddFrameMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = [EraType.String, EraType.Integer, EraType.Integer, EraType.Integer, EraType.Integer, EraType.Integer, EraType.Integer, EraType.Integer, EraType.Integer];
			CanRestructure = false;
		}

		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			if (Config.TextDrawingMode == TextDrawingMode.WINAPI)
				// throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGDIPLUSOnly, Name));
				throw new CodeEE(string.Format(trerror.GDIPlusOnly.Text, Name));
			try
			{
				string imgname = arguments[0].GetStrValue(exm);
				if (string.IsNullOrEmpty(imgname))
					return 0;
				if (AppContents.GetSprite(imgname) == null)
					return 0;
				SpriteAnime img = AppContents.GetSprite(imgname) as SpriteAnime;
				if (img == null && !img.IsCreated)
					return 0;
				GraphicsImage g = ReadGraphics(Name, exm, arguments, 1);
				if (!g.IsCreated)
					return 0;
				Rectangle rect = ReadRectangle(Name, exm, arguments, 2);
				//四角形は正でなければならず、かつ親画像の外を指してはいけない
				if (rect.Width <= 0 || rect.Height <= 0 ||
					rect.X < 0 || rect.X + rect.Width > g.Width || rect.Y < 0 || rect.Y + rect.Height > g.Height)
					return 0;
				//throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodCIMGCreateOutOfRange0, Name));
				Point offset = ReadPoint(Name, exm, arguments, 6);
				long delay = arguments[8].GetIntValue(exm);
				if (delay <= 0 || delay > int.MaxValue)
					return 0;
				img.AddFrame(g, rect, offset, (int)delay);
				return 1;
			}
			catch (Exception ex)
			{
				if (ex is CodeEE) throw;
				throw new CodeEE(Name + ": " + ex.ToString());
			}
		}
	}


	/// <summary>
	/// CBGCLEAR
	/// </summary>
	public sealed class CBGClearMethod : FunctionMethod
	{
		public CBGClearMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = [];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			//if (Config.TextDrawingMode == TextDrawingMode.WINAPI)
			//	throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGDIPLUSOnly, Name));
			exm.Console.CBG_Clear();
			return 1;
		}
	}

	/// <summary>
	/// CBGREMOVERANGE(int zmin, int zmax)
	/// </summary>
	public sealed class CBGRemoveRangeMethod : FunctionMethod
	{
		public CBGRemoveRangeMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = [EraType.Integer, EraType.Integer];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{

			long x64 = arguments[0].GetIntValue(exm);
			long y64 = arguments[1].GetIntValue(exm);
			unchecked
			{
				exm.Console.CBG_ClearRange((int)x64, (int)y64);
			}
			return 1;
		}
	}
	/// <summary>
	/// CBGCLEARBUTTON
	/// </summary>
	public sealed class CBGClearButtonMethod : FunctionMethod
	{
		public CBGClearButtonMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = [];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			//if (Config.TextDrawingMode == TextDrawingMode.WINAPI)
			//	throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGDIPLUSOnly, Name));
			exm.Console.CBG_ClearButton();
			return 1;
		}
	}
	/// <summary>
	/// CBGREMOVEBMAP
	/// </summary>
	public sealed class CBGRemoveBMapMethod : FunctionMethod
	{
		public CBGRemoveBMapMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = [];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			//if (Config.TextDrawingMode == TextDrawingMode.WINAPI)
			//	throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGDIPLUSOnly, Name));
			exm.Console.CBG_ClearBMap();
			return 1;
		}
	}
	/// <summary>
	/// CBGSETG(int ID, int x, int y, int zdepth)
	/// </summary>
	public sealed class CBGSetGraphicsMethod : FunctionMethod
	{
		public CBGSetGraphicsMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = [EraType.Integer, EraType.Integer, EraType.Integer, EraType.Integer];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			if (Config.TextDrawingMode == TextDrawingMode.WINAPI)
				// throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGDIPLUSOnly, Name));
				throw new CodeEE(string.Format(trerror.GDIPlusOnly.Text, Name));

			GraphicsImage g = ReadGraphics(Name, exm, arguments, 0);
			if (!g.IsCreated || g.SKBitmap == null)
				return 0;
			Point p = ReadPoint(Name, exm, arguments, 1);
			long z64 = arguments[3].GetIntValue(exm);
			if (z64 < int.MinValue || z64 > int.MaxValue || z64 == 0)
				// throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodDefaultArgumentOutOfRange0, Name, z64, 3 + 1));
				throw new CodeEE(string.Format(trerror.ArgIsOutOfRangeExcept.Text, Name, 4, z64, int.MinValue, int.MaxValue, 0));
			exm.Console.CBG_SetGraphics(g, p.X, p.Y, (int)z64);
			return 1;

		}
	}

	/// <summary>
	/// CBGSETBMAPG(int ID, int x, int y, int zdepth)
	/// </summary>
	public sealed class CBGSetBMapGMethod : FunctionMethod
	{
		public CBGSetBMapGMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = [EraType.Integer];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			if (Config.TextDrawingMode == TextDrawingMode.WINAPI)
				// throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGDIPLUSOnly, Name));
				throw new CodeEE(string.Format(trerror.GDIPlusOnly.Text, Name));

			GraphicsImage g = ReadGraphics(Name, exm, arguments, 0);
			if (!g.IsCreated || g.SKBitmap == null)
				return 0;
			exm.Console.CBG_SetButtonMap(g);
			return 1;

		}
	}

	/// <summary>
	/// CBGSETCIMG / CBGSETSPRITE
	/// (str imgName, int x, int y, int zdepth, int width, int height, int opacity, var CM)
	/// 第2个参数（x）开始全部可以省略
	/// </summary>
	public sealed class CBGSetCIMGMethod : FunctionMethod
	{
		public CBGSetCIMGMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArrayEx = [
					new ArgTypeList{ ArgTypes = { ArgType.String, ArgType.Int, ArgType.Int, ArgType.Int, ArgType.Int, ArgType.Int, ArgType.Int, ArgType.Any }, OmitStart = 1 }
				];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			string imgname = arguments[0].GetStrValue(exm);
			ASprite img = AppContents.GetSprite(imgname);
			if (img == null || !img.IsCreated)
				return 0;

			int x = arguments.Count > 1 && arguments[1] != null ? (int)arguments[1].GetIntValue(exm) : 0;
			int y = arguments.Count > 2 && arguments[2] != null ? (int)arguments[2].GetIntValue(exm) : 0;
			int z64 = arguments.Count > 3 && arguments[3] != null ? (int)arguments[3].GetIntValue(exm) : 1;
			int width = arguments.Count > 4 && arguments[4] != null ? (int)arguments[4].GetIntValue(exm) : 0;
			int height = arguments.Count > 5 && arguments[5] != null ? (int)arguments[5].GetIntValue(exm) : 0;
			float opacity = arguments.Count > 6 && arguments[6] != null ? arguments[6].GetIntValue(exm) / 255.0f : 1.0f;
			float[]? colorMatrix = arguments.Count > 7 && arguments[7] != null ? ColorMatrixHelper.ReadFromVariableTerm(arguments[7], exm) : null;

			if (z64 < int.MinValue || z64 > int.MaxValue || z64 == 0)
				throw new CodeEE(string.Format(trerror.ArgIsOutOfRangeExcept.Text, Name, 4, z64, int.MinValue, int.MaxValue, 0));

			if (!exm.Console.CBG_SetImage(img, x, y, z64, width, height, opacity, colorMatrix))
				return 0;
			return 1;
		}
	}

	/// <summary>
	/// CBGSETBUTTONCIMG(int button, str imgName, str imgName, int x, int y,int zdepth str tooltipmes)
	/// </summary>
	public sealed class CBGSETButtonSpriteMethod : FunctionMethod
	{
		public CBGSETButtonSpriteMethod()
		{
			ReturnType = EraType.Integer;
			// argumentTypeArray = new EraType[] { EraType.Integer, EraType.String, EraType.String, EraType.Integer, EraType.Integer, EraType.Integer, EraType.String };
			argumentTypeArrayEx = [
					new ArgTypeList{ ArgTypes = { ArgType.Int, ArgType.String, ArgType.String, ArgType.Int, ArgType.Int, ArgType.Int, ArgType.String }, OmitStart = 6 },
				];
			CanRestructure = false;
		}
		//public override string CheckArgumentType(string name, IOperandTerm[] arguments)
		//{

		//	if (arguments.Count < 6)
		//		return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentNum1, name, 6);
		//	if (arguments.Count > 7)
		//		return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentNum2, name);
		//	if (arguments.Count != 6 && arguments.Count != 7)
		//		return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentNum0, name);

		//	for (int i = 0; i < arguments.Count; i++)
		//	{
		//		if (arguments[i] == null)
		//			return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentNotNullable0, name, i + 1);

		//		if (i < argumentTypeArray.Length && argumentTypeArray[i] != arguments[i].GetOperandType())
		//			return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentType0, name, i + 1);
		//	}
		//	return null;
		//}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			if (Config.TextDrawingMode == TextDrawingMode.WINAPI)
				// throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGDIPLUSOnly, Name));
				throw new CodeEE(string.Format(trerror.GDIPlusOnly.Text, Name));

			long b64 = arguments[0].GetIntValue(exm);
			if (b64 < 0 || b64 > 0xFFFFFF)
				return 0;
			string imgnameN = arguments[1].GetStrValue(exm);
			ASprite imgN = AppContents.GetSprite(imgnameN);
			string imgnameB = arguments[2].GetStrValue(exm);
			ASprite imgB = AppContents.GetSprite(imgnameB);

			Point p = ReadPoint(Name, exm, arguments, 3);
			long z64 = arguments[5].GetIntValue(exm);
			if (z64 < int.MinValue || z64 > int.MaxValue || z64 == 0)
				// throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodDefaultArgumentOutOfRange0, Name, z64, 5 + 1));
				throw new CodeEE(string.Format(trerror.ArgIsOutOfRangeExcept.Text, Name, 6, z64, int.MinValue, int.MaxValue, 0));
			string tooltip = null;
			if (arguments.Count > 6)
				tooltip = arguments[6].GetStrValue(exm);
			if (!exm.Console.CBG_SetButtonImage((int)b64, imgN, imgB, p.X, p.Y, (int)z64, tooltip))
				return 0;
			return 1;

		}
	}

	static readonly short[] keytoggle = new short[256];
	private sealed class GetKeyStateMethod : FunctionMethod
	{
		public GetKeyStateMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = [EraType.Integer];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			if (!exm.Console.IsActive)//アクティブでないならスルー
				return 0;
			long keycode = arguments[0].GetIntValue(exm);
			if (keycode < 0 || keycode > 255)
				return 0;
			short s = WinInput.GetKeyState((int)keycode);
			short toggle = keytoggle[keycode];
			keytoggle[keycode] = (short)((s & 1) + 1);//初期値0、トグル状態に応じて1か2を代入。
			switch (Name)
			{
				case "GETKEY": return (s < 0) ? 1 : 0;
				case "GETKEYTRIGGERED":
					{
						// Check latch first: this catches clicks where MouseDown+MouseUp
						// were both processed in the same DoEvents(), causing _keyState
						// to already be 0 by the time we read it.
						if (WinInput.ConsumeKeyLatch((int)keycode) != 0)
							return 1;
						return (s < 0) && (toggle != keytoggle[keycode]) ? 1 : 0;
					}
			}
			throw new ExeEE("異常な分岐");
		}
	}

	private sealed class MousePosMethod : FunctionMethod
	{
		public MousePosMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = [];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			switch (Name)
			{
				case "MOUSEX": return exm.Console.GetMousePosition().X;
				case "MOUSEY": return exm.Console.GetMousePosition().Y;
			}
			throw new ExeEE("異常な名前");
		}
	}
	#region EE_MOUSEB
	private sealed class MouseButtonMethod : FunctionMethod
	{
		public MouseButtonMethod()
		{
			ReturnType = EraType.String;
			argumentTypeArray = [];
			CanRestructure = false;
		}
		public override string GetStrValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			//if (exm.Console.SelectingButton != null)
			//	return exm.Console.SelectingButton.ToString();
			bool b = exm.Console.AlwaysRefresh;
			Point point = exm.Console.Window.MainPicBox.PointToClient(Control.MousePosition);
			exm.Console.AlwaysRefresh = true;
			if (exm.Console.Window.MainPicBox.ClientRectangle.Contains(point))
				exm.Console.MoveMouse(point);
			exm.Console.AlwaysRefresh = b;
			if (exm.Console.PointingSring != null)
			{
				if (!exm.Console.PointingSring.IsButton)
					return "";
				if (exm.Console.PointingSring.IsInteger)
					return exm.Console.PointingSring.Input.ToString();
				return exm.Console.PointingSring.Inputs;
			}
			return "";
		}
	}
	#endregion


	private sealed class IsActiveMethod : FunctionMethod
	{
		public IsActiveMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = [];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			return exm.Console.IsActive ? 1 : 0;
		}
	}

	private sealed class GetAnimeTimerMethod : FunctionMethod
	{
		public GetAnimeTimerMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = [];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			return exm.Console.AnimeTimer;
		}
	}

	/// <summary>
	/// int SAVETEXT str text, int fileNo{, int force_savdir, int force_UTF8}
	/// </summary>
	private sealed class SaveTextMethod : FunctionMethod
	{
		public SaveTextMethod()
		{
			ReturnType = EraType.Integer;
			// argumentTypeArray = new EraType[] { EraType.String ,EraType.Integer, EraType.Integer, EraType.Integer };
			argumentTypeArrayEx = [
					new ArgTypeList{ ArgTypes = { ArgType.String, ArgType.Any, ArgType.Int, ArgType.Int }, OmitStart = 2 },
				];
			CanRestructure = false;
		}
		//public override string CheckArgumentType(string name, IOperandTerm[] arguments)
		//{

		//	if (arguments.Count < 2)
		//		return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentNum1, name, 2);
		//	if (arguments.Count > 4)
		//		return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentNum2, name);
		//	for (int i = 0; i < arguments.Count; i++)
		//	{
		//		if (arguments[i] == null)
		//			return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentNotNullable0, name, i + 1);
		//		#region EM_私家版_LoadText＆SaveText機能拡張
		//		if (i == 1 && arguments[i].GetEraType() == EraType.String) continue;
		//		#endregion
		//		if (i < argumentTypeArray.Length && argumentTypeArray[i] != arguments[i].GetOperandType())
		//			return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentType0, name, i + 1);
		//	}
		//	return null;
		//}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			#region EM_私家版_LoadText＆SaveText機能拡張
			//string savText = arguments[0].GetStrValue(exm);
			//Int64 i64 = arguments[1].GetIntValue(exm);
			//if (i64 < 0 || i64 > int.MaxValue)
			//	return 0;
			//bool forceSavdir = arguments.Count > 2 && (arguments[2].GetIntValue(exm) != 0);
			//bool forceUTF8 = arguments.Count > 3 && (arguments[3].GetIntValue(exm) != 0);
			//int fileIndex = (int)i64;
			//string filepath = forceSavdir ?
			//	GetSaveDataPathText(fileIndex, Config.ForceSavDir) :
			//	GetSaveDataPathText(fileIndex, Config.SavDir);
			//Encoding encoding = forceUTF8 ?
			//	Encoding.GetEncoding("UTF-8") :
			//	Config.SaveEncode;
			//try
			//{
			//	if (forceSavdir)
			//		Config.ForceCreateSavDir();
			//	else
			//		Config.Config.CreateSavDir();
			//	System.IO.File.WriteAllText(filepath, savText, encoding);
			//}
			//catch { return 0; }
			string savText = arguments[0].GetStrValue(exm), filepath;
			long i64 = -1;
			bool forceSavdir = arguments.Count > 2 && (arguments[2].GetIntValue(exm) != 0);
			bool forceUTF8 = arguments.Count > 3 && (arguments[3].GetIntValue(exm) != 0);


			if (arguments[1].GetEraType() == EraType.Integer)
			{
				i64 = arguments[1].GetIntValue(exm);
				if (i64 < 0 || i64 > int.MaxValue)
					return 0;
				int fileIndex = (int)i64;
				filepath = forceSavdir ?
				GetSaveDataPathText(fileIndex, Config.ForceSavDir) :
				GetSaveDataPathText(fileIndex, Config.SavDir);
			}
			else
			{
				filepath = Utils.GetValidPath(arguments[1].GetStrValue(exm));
				if (filepath == null) return 0;
				string tmp = Path.HasExtension(filepath) ? Path.GetExtension(filepath).ToLower().Substring(1) : "";
				if (!Config.ValidExtension.Contains(tmp))
					filepath = Path.ChangeExtension(filepath, "txt");
				forceUTF8 = true;
			}

			// Encoding encoding = forceUTF8 ?
			// 	Encoding.GetEncoding("UTF-8") :
			// 	Config.SaveEncode;
			try
			{
				if (i64 >= 0)
				{
					if (forceSavdir)
						Config.ForceCreateSavDir();
					else
						Config.CreateSavDir();
				}
				else
				{
					if (filepath.LastIndexOf('\\') >= 0)
						Directory.CreateDirectory(filepath.Substring(0, filepath.LastIndexOf('\\')));
				}

				File.WriteAllText(filepath, savText, Config.SaveEncode);
			}
			catch { return 0; }
			#endregion
			return 1;
		}
	}
	/// <summary>
	/// str LOADTEXT int fileNo{, int force_savdir, int force_UTF8}
	/// </summary>
	private sealed class LoadTextMethod : FunctionMethod
	{
		public LoadTextMethod()
		{
			ReturnType = EraType.String;
			// argumentTypeArray = new EraType[] { EraType.Integer, EraType.Integer, EraType.Integer };
			argumentTypeArrayEx = [
					new ArgTypeList{ ArgTypes = { ArgType.Any, ArgType.Int, ArgType.Int }, OmitStart = 1 },
				];
			CanRestructure = false;
		}
		//public override string CheckArgumentType(string name, IOperandTerm[] arguments)
		//{

		//	if (arguments.Count < 1)
		//		return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentNum1, name, 1);
		//	if (arguments.Count > 3)
		//		return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentNum2, name);
		//	for (int i = 0; i < arguments.Count; i++)
		//	{
		//		if (arguments[i] == null)
		//			return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentNotNullable0, name, i + 1);
		//		#region EM_私家版_LoadText＆SaveText機能拡張
		//		if (i == 0 && arguments[i].GetEraType() == EraType.String) continue;
		//		#endregion
		//		if (i < argumentTypeArray.Length && argumentTypeArray[i] != arguments[i].GetOperandType())
		//			return string.Format(Properties.Resources.SyntaxErrMesMethodDefaultArgumentType0, name, i + 1);
		//	}
		//	return null;
		//}
		public override string GetStrValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			#region EM_私家版_LoadText＆SaveText機能拡張
			//Int64 i64 = arguments[0].GetIntValue(exm);
			//if (i64 < 0 || i64 > int.MaxValue)
			//	return "";
			//bool forceSavdir = arguments.Count > 1 && (arguments[1].GetIntValue(exm) != 0);
			//bool forceUTF8 = arguments.Count > 2 && (arguments[2].GetIntValue(exm) != 0);
			//int fileIndex = (int)i64;
			//string filepath = forceSavdir ?
			//	GetSaveDataPathText(fileIndex, Config.ForceSavDir) :
			//	GetSaveDataPathText(fileIndex, Config.SavDir);
			//Encoding encoding = forceUTF8 ?
			//	Encoding.GetEncoding("UTF-8") :
			//	Config.SaveEncode;
			//if (!System.IO.File.Exists(filepath))
			//	return "";
			//string ret;
			//try
			//{
			//	ret = System.IO.File.ReadAllText(filepath, encoding);
			//}
			//catch { return ""; }
			//return ret;
			string ret = "", filepath;
			long i64 = -1;
			bool forceSavdir = arguments.Count > 1 && (arguments[1].GetIntValue(exm) != 0);
			bool forceUTF8 = arguments.Count > 2 && (arguments[2].GetIntValue(exm) != 0);
			if (arguments[0].GetEraType() == EraType.Integer)
			{
				i64 = arguments[0].GetIntValue(exm);
				if (i64 < 0 || i64 > int.MaxValue)
					return "";
				int fileIndex = (int)i64;
				filepath = forceSavdir ?
				GetSaveDataPathText(fileIndex, Config.ForceSavDir) :
				GetSaveDataPathText(fileIndex, Config.SavDir);
			}
			else
			{
				filepath = Utils.GetValidPath(arguments[0].GetStrValue(exm));
				if (filepath == null) return string.Empty;
				string tmp = Path.HasExtension(filepath) ? Path.GetExtension(filepath).ToLower().Substring(1) : "";
				if (!Config.ValidExtension.Contains(tmp))
					return "";
			}

			if (!File.Exists(filepath))
				return "";
			try
			{
				ret = File.ReadAllText(filepath, EncodingHandler.DetectEncoding(filepath));
			}
			catch { return ""; }
			//一貫性の観点で\rには死んでもらう
			return ret.Replace("\r", "");
			#endregion
		}
	}



	private static string GetSaveDataPathText(int index, string dir) { return string.Format("{0}txt{1:00}.txt", dir, index); }
	private static string GetSaveDataPathGraphics(int index) { return string.Format("{0}img{1:0000}.png", Config.SavDir, index); }

	/// <summary>
	/// int GSAVE int ID, int fileNo
	/// </summary>
	public sealed class GraphicsSaveMethod : FunctionMethod
	{
		public GraphicsSaveMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = [EraType.Integer, EraType.Integer];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			if (Config.TextDrawingMode == TextDrawingMode.WINAPI)
				// throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGDIPLUSOnly, Name));
				throw new CodeEE(string.Format(trerror.GDIPlusOnly.Text, Name));
			GraphicsImage g = ReadGraphics(Name, exm, arguments, 0);
			if (!g.IsCreated)
				return 0;

			long i64 = arguments[1].GetIntValue(exm);
			if (i64 < 0 || i64 > int.MaxValue)
				return 0;

			string filepath = GetSaveDataPathGraphics((int)i64);
			try
			{
				Config.CreateSavDir();
#if WINDOWS
				g.SKBitmap.ToBitmap().Save(filepath);
#else
				using var fileStream = File.OpenWrite(filepath);
				if (g.SKBitmap.Encode(fileStream, SKEncodedImageFormat.Png, 100))
					return 1;
				return 0;
#endif
			}
			catch
			{
				return 0;
			}
			return 1;
		}
	}
	/// <summary>
	/// int GLOAD int ID, int fileNo
	/// </summary>
	public sealed class GraphicsLoadMethod : FunctionMethod
	{
		public GraphicsLoadMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = [EraType.Integer, EraType.Integer];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			if (Config.TextDrawingMode == TextDrawingMode.WINAPI)
				// throw new CodeEE(string.Format(Properties.Resources.RuntimeErrMesMethodGDIPLUSOnly, Name));
				throw new CodeEE(string.Format(trerror.GDIPlusOnly.Text, Name));
			GraphicsImage g = ReadGraphics(Name, exm, arguments, 0);
			if (g.IsCreated)
				return 0;

			long i64 = arguments[1].GetIntValue(exm);
			if (i64 < 0 || i64 > int.MaxValue)
				return 0;

			string filepath = GetSaveDataPathGraphics((int)i64);
			SKBitmap bmp = null;
			try
			{
				if (!File.Exists(filepath))
					return 0;
				#region EM_私家版_webp
				bmp = SKBitmap.Decode(filepath);
				//bmp = Utils.LoadImage(filepath);
				if (bmp == null) return 0;
				#endregion
				if (bmp.Width > AbstractImage.MAX_IMAGESIZE || bmp.Height > AbstractImage.MAX_IMAGESIZE)
					return 0;
				g.GCreateFromF(bmp, Config.TextDrawingMode == TextDrawingMode.WINAPI);
			}
			catch (Exception e)
			{
				if (e is CodeEE)
					throw;
			}
			finally
			{
				if (bmp != null)
					bmp.Dispose();
			}
			if (!g.IsCreated)
				return 0;
			return 1;
		}
	}

	#endregion

	#region EE_EXISTSOUND
	private sealed class ExistSoundMethod : FunctionMethod
	{
		public ExistSoundMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = [EraType.String];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			string str = arguments[0].GetStrValue(exm);
			string filepath = Program.MusicDir + str;
			if (Program.FileExists(ref filepath))
				return 1;
			return 0;
		}
	}
	#endregion

	#region EE_EXISTFUNCTION

	public sealed class ExistFunctionMethod : FunctionMethod
	{

		public ExistFunctionMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArrayEx = [
					new ArgTypeList{ ArgTypes = { ArgType.String, ArgType.Int}, OmitStart = 1 },
				];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			string functionname = arguments[0].GetStrValue(exm);
			if (arguments.Count == 1 || arguments[1].GetIntValue(exm) == 0)
			{
				FunctionLabelLine func;
				string searchKey = Config.StringComparison == StringComparison.OrdinalIgnoreCase ? functionname.ToUpper(CultureInfo.InvariantCulture) : functionname;
				if (Config.StringComparison == StringComparison.OrdinalIgnoreCase)
					func = GlobalStatic.LabelDictionary.GetNonEventLabel(functionname.ToUpper(CultureInfo.InvariantCulture));
				else
					func = GlobalStatic.LabelDictionary.GetNonEventLabel(functionname);
				if (func == null)
				{
					// 调用公开方法 TryLazyLoadErb
					// 这个方法会：
					// a. 检查 private LazyLoadingTable
					// b. 如果找到，加载文件并注册到 LabelDictionary
					// c. 返回 true (找到) 或 false (没找到)
					bool existsInLazyTable = GlobalStatic.Process.TryLazyLoadErb(searchKey);
					if (existsInLazyTable)
					{
						func = GlobalStatic.LabelDictionary.GetNonEventLabel(searchKey);
						if (func.IsMethod)
						{
							if (func.MethodType == EraType.String)
								return 3;
							else if (func.MethodType == EraType.Integer)
								return 2;

						}
						return 1; 
					}
					return 0;
				}
				if (func.IsMethod)
				{
					if (func.MethodType == EraType.String)
						return 3;
					else if (func.MethodType == EraType.Integer)
						return 2;

				}
				return 1;
			}
			else
			{
				foreach (string funcname in GlobalStatic.Process.LabelDictionary.NoneventKeys)
				{
					if (funcname.ToUpper(CultureInfo.InvariantCulture) == functionname.ToUpper(CultureInfo.InvariantCulture))
					{
						FunctionLabelLine func = GlobalStatic.LabelDictionary.GetNonEventLabel(funcname);

						if (func.IsMethod)
						{
							if (func.MethodType == EraType.String)
								return 3;
							else if (func.MethodType == EraType.Integer)
								return 2;

						}
						return 1;
					}
				}
				return 0;
			}
		}
	}
	#endregion

	#region EE_GETMEMORYUSAGE
	private sealed class GetUsingMemoryMethod : FunctionMethod
	{
		public GetUsingMemoryMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = [];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			try
			{
				using (System.Diagnostics.Process memory = System.Diagnostics.Process.GetCurrentProcess())
				{
					return memory.WorkingSet64;
				}
			}
			catch { return 0L; }
		}
	}
	#endregion
	#region EE_CLEARMEMORY
	private sealed class ClearMemoryMethod : FunctionMethod
	{
		public ClearMemoryMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = [];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			try
			{
				using (System.Diagnostics.Process destmemory = System.Diagnostics.Process.GetCurrentProcess())
				{
					long destmemorysize = destmemory.WorkingSet64;
					GC.Collect();
					using (System.Diagnostics.Process memory = System.Diagnostics.Process.GetCurrentProcess())
					{
						return destmemorysize - memory.WorkingSet64;
					}
				}
			}
			catch { return 0L; }
		}
	}
	#endregion
	#region EE_textbox拡張
	private sealed class GetTextBoxMethod : FunctionMethod
	{
		public GetTextBoxMethod()
		{
			ReturnType = EraType.String;
			argumentTypeArray = [];
			CanRestructure = false;
		}
		public override string GetStrValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			return exm.Console.Window.TextBox.Text;
		}
	}
	private sealed class ChangeTextBoxMethod : FunctionMethod
	{
		public ChangeTextBoxMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = [EraType.String];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			exm.Console.Window.ChangeTextBox(arguments[0].GetStrValue(exm));
			return 1;
		}
	}
	#endregion
	#region EE_GETERDNAME
	private sealed class ErdNameMethod : FunctionMethod
	{
		public ErdNameMethod()
		{
			ReturnType = EraType.String;
			// argumentTypeArray = null;
			argumentTypeArrayEx = [
					new ArgTypeList{ ArgTypes = { ArgType.RefAny | ArgType.AllowConstRef, ArgType.Int, ArgType.Int }, OmitStart = 2 },
				];
			CanRestructure = true;
			HasUniqueRestructure = true;
		}
		public override string GetStrValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			VariableTerm vToken = (VariableTerm)arguments[0];
			string varname = "";
			if (arguments.Count > 2)
				varname = vToken.Identifier.Name + "@" + arguments[2].GetIntValue(exm);
			else
				varname = vToken.Identifier.Name;
			long value = arguments[1].GetIntValue(exm);
			if (exm.VEvaluator.Constant.TryIntegerToKeyword(out string ret, value, varname))
				return ret;
			else
				return "";
		}
		public override bool UniqueRestructure(ExpressionMediator exm, List<AExpression> arguments)
		{
			arguments[1] = arguments[1].Restructure(exm);
			return arguments[1] is SingleTerm;
		}
	}
	#endregion
	#region EE_GETDISPLAYLINE
	private sealed class GetDisplayLineMethod : FunctionMethod
	{
		public GetDisplayLineMethod()
		{
			ReturnType = EraType.String;
			// argumentTypeArray = null;
			argumentTypeArrayEx = [
					new ArgTypeList{ ArgTypes = { ArgType.Int }},
				];
			CanRestructure = false;
		}
		public override string GetStrValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			long num = arguments[0].GetIntValue(exm);
			int count = exm.Console.DisplayLineList.Count;
			if (count == 0)
				return "";
			int index;
			if (num >= 0)
			{
				if (num >= count)
					return "";
				index = (int)num;
			}
			else
			{
				// 从最下往上数第 |n| 行: -1=最后一行, -2=倒数第二行
				if (num == long.MinValue || -num > count)
					return "";
				index = count + (int)num; // count - (-num) = count + num (num is negative)
			}
			return exm.Console.DisplayLineList[index].ToString();
		}
	}
	#endregion
	#region EE_GETDOINGFUNCTION
	private sealed class GetDoingFunctionMethod : FunctionMethod
	{
		public GetDoingFunctionMethod()
		{
			ReturnType = EraType.String;
			argumentTypeArray = [];
			CanRestructure = true;
		}
		public override string GetStrValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			LogicalLine line = exm.Process.GetScaningLine();
			if ((line == null) || (line.ParentLabelLine == null))
				return "";//システム待機中のデバッグモードから呼び出し
			return line.ParentLabelLine.LabelName;
		}
	}
	#endregion
	#region EE_SystemInput拡張
	private sealed class FlowInputMethod : FunctionMethod
	{
		public FlowInputMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArrayEx = new ArgTypeList[] {
					new() { ArgTypes = { ArgType.Int, ArgType.Int, ArgType.Int, ArgType.Int }, OmitStart = 1 },
				};
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{

			exm.Process.flowinputDef = arguments[0].GetIntValue(exm);
			if (arguments.Count > 1)
				exm.Process.flowinput = arguments[1].GetIntValue(exm) != 0 ? true : false ;
			if (arguments.Count > 2)
				exm.Process.flowinputCanSkip = arguments[2].GetIntValue(exm) != 0 ? true : false ;
			if (arguments.Count > 3)
				exm.Process.flowinputForceSkip = arguments[3].GetIntValue(exm) != 0 ? true : false;
			return 0;
		}
	}
	private sealed class FlowInputsMethod : FunctionMethod
	{
		public FlowInputsMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArrayEx = new ArgTypeList[] {
					new() { ArgTypes = { ArgType.Int, ArgType.String }, OmitStart = 1 },
				};
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{

			exm.Process.flowinputString = arguments[0].GetIntValue(exm) != 0 ? true : false ;
			if (arguments.Count > 1)
				exm.Process.flowinputDefString = arguments[1].GetStrValue(exm);
			return 0;
		}
	}
	#endregion

	#region 尊尼获加_SEQUENCEINPUT
	private sealed class SequenceInputMethod : FunctionMethod
	{
		public SequenceInputMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = [EraType.String];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			exm.Process.sequenceInputValue = arguments[0].GetStrValue(exm);
			exm.Process.hasSequenceInput = true;
			return 0;
		}
	}
	// 关闭所有输入（textbox + SEQUENCEINPUT）的宏解析。
	// 关闭后，PressEnterKey 不再调 parseInput，输入按字面多段（按真换行 \n 拆分）喂入。
	// ( ) 重复宏和 \e MesSkip 都不再被处理。
	private sealed class DisableInputMacroMethod : FunctionMethod
	{
		public DisableInputMacroMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = [];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			exm.Process.inputMacroEnabled = false;
			return 0;
		}
	}
	// 恢复宏解析（默认行为，与原版 PressEnterKey 一致）。
	private sealed class EnableInputMacroMethod : FunctionMethod
	{
		public EnableInputMacroMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = [];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			exm.Process.inputMacroEnabled = true;
			return 0;
		}
	}
	#endregion

	#region daughter-patch追加
	private sealed class GetMethMethod : FunctionMethod
	{
		public GetMethMethod()
		{
			ReturnType = EraType.Integer;
			// argumentTypeArray = null;
			argumentTypeArrayEx = new ArgTypeList[] {
					new ArgTypeList{ ArgTypes = { ArgType.String, ArgType.Int, ArgType.VariadicAny }, OmitStart = 1 },
				};
			CanRestructure = false;
		}

		public override Int64 GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			string name = arguments[0].GetStrValue(exm);
			List<AExpression> methArgs = new List<AExpression>(arguments.Skip(2).ToArray());
			var term = GlobalStatic.IdentifierDictionary.GetFunctionMethod(GlobalStatic.LabelDictionary, name, methArgs, true);

			if (term == null)
			{
				if (arguments.Count < 2 || arguments[1] == null)
					throw new CodeEE(string.Format(trerror.NotDefinedUserFunc.Text, name));
				else
					return arguments[1].GetIntValue(exm);
			}
			else if (term.GetEraType() != EraType.Integer)
				throw new CodeEE(string.Format(trerror.IsNotInt.Text, name));
			else
				return term.GetIntValue(exm);
		}
	}
	private sealed class GetMethsMethod : FunctionMethod
	{
		public GetMethsMethod()
		{
			ReturnType = EraType.String;
			// argumentTypeArray = null;
			argumentTypeArrayEx = new ArgTypeList[] {
					new ArgTypeList{ ArgTypes = { ArgType.String, ArgType.String, ArgType.VariadicAny }, OmitStart = 1 },
				};
			CanRestructure = false;
		}
		public override string GetStrValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			string name = arguments[0].GetStrValue(exm);
			List<AExpression> methArgs = new List<AExpression>(arguments.Skip(2).ToArray());
			var term = GlobalStatic.IdentifierDictionary.GetFunctionMethod(GlobalStatic.LabelDictionary, name, methArgs, true);

			if (term == null)
			{
				if (arguments.Count < 2 || arguments[1] == null)
					throw new CodeEE(string.Format(trerror.NotDefinedUserFunc.Text, name));
				else
					return arguments[1].GetStrValue(exm);
			}
			else if (term.GetEraType() != EraType.String)
				throw new CodeEE(string.Format(trerror.IsNotStr.Text, name));
			else
				return term.GetStrValue(exm);
		}
	}
	private sealed class ExistMethMethod : FunctionMethod
	{
		public ExistMethMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = new EraType[] { EraType.String };
			CanRestructure = true;
		}

		public override Int64 GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			string name = arguments[0].GetStrValue(exm);
			AExpression term;
			try
			{
				term = GlobalStatic.IdentifierDictionary.GetFunctionMethod(GlobalStatic.LabelDictionary, name, new List<AExpression>(), true);
			}
			catch (CodeEE)
			{
				return 0;
			}

			if (term == null)
			{
				return 0;
			}
			else
			{
				Int64 res = 0;
				switch (term.GetEraType())
				{
					case EraType.Integer: res |= 1; break;
					case EraType.String: res |= 2; break;
					case EraType.Float: res |= 32; break;
				}
				return res;
			}
		}
	}

	#endregion

	//HOTKEY STATE
	private sealed class HotkeyStateMethod : FunctionMethod
	{
		public HotkeyStateMethod()
		{
			ReturnType = EraType.Integer;
			// argumentTypeArray = null;
			argumentTypeArrayEx = [
					new ArgTypeList{ ArgTypes = { ArgType.Int, ArgType.Int}, OmitStart = 1 },
				];
			CanRestructure = false;
		}
		public override Int64 GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			Int64 argument0 = arguments[0].GetIntValue(exm);
			Int64 argument1 = arguments[1].GetIntValue(exm);
			GlobalStatic.Console.Window.hotkeyState.HotkeyStateSet((nint)argument0, (nint)argument1);
			return 0;
		}
	}

	private sealed class HotkeyStateInitMethod : FunctionMethod
	{
		public HotkeyStateInitMethod()
		{
			ReturnType = EraType.Integer;
			// argumentTypeArray = null;
			argumentTypeArrayEx = [
					new ArgTypeList{ ArgTypes = { ArgType.Int}, OmitStart = 1 },
				];
			CanRestructure = false;
		}
		public override Int64 GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			Int64 argument0 = arguments[0].GetIntValue(exm);
			GlobalStatic.Console.Window.hotkeyState.HotkeyStateInit((nint)argument0);
			return 0;
		}
	}
	#region EE_OUTPUTLOG拡張
	private sealed class OutputlogMethod : FunctionMethod
	{
		public OutputlogMethod()
		{
			ReturnType = EraType.Integer;
			// argumentTypeArray = null;
			argumentTypeArrayEx = [
					new ArgTypeList{ ArgTypes = { ArgType.String, ArgType.Int}, OmitStart = 0 },
				];
			CanRestructure = false;
		}
		public override Int64 GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			string filename = "";
			if (arguments.Count > 0)
				filename = arguments[0].GetStrValue(exm);
			bool hideInfo = false;
			if (arguments.Count > 1)
				hideInfo = arguments[1].GetIntValue(exm) == 1;

	
			exm.Console.OutputLog(filename, hideInfo);
			return 1;
		}

	}
	#endregion 

	#region 尊尼获加荣誉出品
	private sealed class GetSoundOrBgmInfoMethod : FunctionMethod
	{
		public GetSoundOrBgmInfoMethod()
		{
			ReturnType = EraType.Integer; 
			argumentTypeArrayEx = [
					new ArgTypeList { ArgTypes = { ArgType.Int, ArgType.Int }, OmitStart = 1 },
				];
			CanRestructure = false;
		}    
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			int channelId = (int)arguments[0].GetIntValue(exm);
			
			// 获取目标 Sound 对象
			Sound targetSound = null;
			if (channelId == -1)
			{
				targetSound = GlobalStatic.Bgm;
			}
			else if (channelId >= 0 && channelId < GlobalStatic.Sound.Length)
			{
				if (GlobalStatic.Sound[channelId] == null)
					GlobalStatic.Sound[channelId] = Sound.Factory();
				targetSound = GlobalStatic.Sound[channelId];
			}

			if (targetSound == null) return 0;

			// 如果省略了第二个参数，则返回所有值到 RESULT_ARRAY
			if (arguments.Count < 2 || arguments[1] == null)
			{
				// RESULT:0 = 总长度 (毫秒)
				exm.VEvaluator.RESULT_ARRAY[0] = (long)(targetSound.GetTotalTime() * 1000);
				// RESULT:1 = 当前时间 (毫秒)
				exm.VEvaluator.RESULT_ARRAY[1] = (long)(targetSound.GetCurrentTime() * 1000);
				// RESULT:2 = 播放状态 (0=已暂停, 1=播放中)
				exm.VEvaluator.RESULT_ARRAY[2] = targetSound.isPlaying() ? 1L : 0L;
				// RESULT:3 = 通道音量 (0-100)
				exm.VEvaluator.RESULT_ARRAY[3] = targetSound.getVolume();
				// RESULT:4 = 播放速度 (百分比，100为正常速度)
				exm.VEvaluator.RESULT_ARRAY[4] = (long)(targetSound.getSpeed() * 100);
				
				// 返回总长度作为函数返回值
				return exm.VEvaluator.RESULT_ARRAY[0];
			}
			else
			{
				// 根据第二个参数返回特定值
				int infoType = (int)arguments[1].GetIntValue(exm);
				switch (infoType)
				{
					case 1: // 总长度 (毫秒)
						return (long)(targetSound.GetTotalTime() * 1000);
					case 2: // 当前时间 (毫秒)
						return (long)(targetSound.GetCurrentTime() * 1000);
					case 3: // 播放状态 (0=已暂停, 1=播放中)
						return targetSound.isPlaying() ? 1L : 0L;
					case 4: // 通道音量 (0-100)
						return targetSound.getVolume();
					case 5: // 播放速度 (百分比，100为正常速度)
						return (long)(targetSound.getSpeed() * 100);
					default:
						return 0;
				}
			}
		}
	}

	private sealed class IsPlayingSoundMethod : FunctionMethod
	{
		public IsPlayingSoundMethod()
		{
			ReturnType = EraType.Integer; 
			argumentTypeArrayEx = [
					new ArgTypeList { ArgTypes = { ArgType.Int }, OmitStart = 0 },
				];
			CanRestructure = false;
		}	
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			int channelId = (int)arguments[0].GetIntValue(exm);
			if (channelId < 0 || channelId >= GlobalStatic.Sound.Length)
			{
				return -1;
			}
			// 检查指定通道是否正在播放
			if (GlobalStatic.Sound[channelId] != null && GlobalStatic.Sound[channelId].isPlaying())
			{
				return channelId;
			}
			return -1;
		}
	}
	private sealed class SoundControlMethod : FunctionMethod
	{
		public SoundControlMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArrayEx = [
					new ArgTypeList { ArgTypes = {ArgType.Int , ArgType.Int } },
					new ArgTypeList { ArgTypes = {ArgType.Int , ArgType.Int, ArgType.Int, ArgType.Int }, OmitStart = 3 },
				];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			// 获取第一个参数：音频通道号
			int channelId = (int)arguments[0].GetIntValue(exm);
			// 获取第二个参数：控制行为 (0=暂停, 1=恢复, 2=停止, 3=变速)
			int action = (int)arguments[1].GetIntValue(exm);
			
			// 检查通道号是否有效
			if (channelId < 0 || channelId >= GlobalStatic.Sound.Length)
			{
				return -1; // 无效通道号
			}
			
			// 确保通道已初始化
			if (GlobalStatic.Sound[channelId] == null)
			{
				GlobalStatic.Sound[channelId] = Sound.Factory();
			}
			
			// 根据控制行为执行相应操作
			if (arguments.Count == 2)
			{
				switch (action)
				{
					case 0: // 暂停
						GlobalStatic.Sound[channelId].pause();
						return 1;
					case 1: // 恢复播放
						GlobalStatic.Sound[channelId].resume();
						return 1;
					case 2: // 停止播放
						GlobalStatic.Sound[channelId].stop();
						return 1;
					default: // 无效的控制行为
						return -2;
				}
			}
			else
			{
				switch(action)
				{
					case 3: // 变速
						// 获取第三个参数：变速倍率
						// 修改：使用GetIntValue获取整数值，然后转换为float
						float speed = (float)arguments[2].GetIntValue(exm) / 100.0f;
						// 获取第四个参数：是否保持音调 (0=改变音调, 1=保持音调)
						// 如果没有提供第四个参数，默认保持音调不变
						bool preservePitch = true;
						// 存在第四个不为0的参数，音调改变
						if (arguments.Count >= 4 && arguments[3].GetIntValue(exm) != 0)
						{
							preservePitch = false;
						}
						// 设置音调保持模式
						GlobalStatic.Sound[channelId].SetPreservePitch(preservePitch);
						// 调用Sound类的setSpeed方法
						GlobalStatic.Sound[channelId].setSpeed(speed);
						return 1;
						
					default:
						return -2; // 无效的控制行为
				}
			}
		}
	}
	private sealed class IsPlayingBgmMethod : FunctionMethod
	{
		public IsPlayingBgmMethod()
		{
			ReturnType = EraType.Integer; 
			argumentTypeArray = [];
			CanRestructure = false;
		}	
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			// 检查通道是否存在且正在播放
			if (GlobalStatic.Bgm != null && GlobalStatic.Bgm.isPlaying())
			{
				return 1;
			}
			else
			{
				return 0;
			}
		}
	}
	private sealed class BgmControlMethod : FunctionMethod
	{
		public BgmControlMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArrayEx = [
					new ArgTypeList { ArgTypes = {ArgType.Int } },
					new ArgTypeList { ArgTypes = {ArgType.Int , ArgType.Int, ArgType.Int }, OmitStart = 2 },
				];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			// 获取第一个参数：控制行为 (0=暂停, 1=恢复, 2=停止, 3=变速)
			int action = (int)arguments[0].GetIntValue(exm);
			// 确保通道已初始化
			if (GlobalStatic.Bgm == null)
			{
				GlobalStatic.Bgm = Sound.Factory();
			}
			
			// 根据控制行为执行相应操作
			if (arguments.Count == 1)
			{
				switch (action)
				{
					case 0: // 暂停
						GlobalStatic.Bgm.pause();
						return 1;
						
					case 1: // 恢复播放
						GlobalStatic.Bgm.resume();
						return 1;
					case 2: // 停止播放
						GlobalStatic.Bgm.stop();
						return 1;
					default:
						return -2; // 无效的控制行为
				}
			}
			else
			{
			    switch (action)
			    {
			        case 3: // 变速
						// 获取第二个参数：变速倍率
						// 修改：使用GetIntValue获取整数值，然后转换为float
						float speed = (float)arguments[1].GetIntValue(exm) / 100.0f;
						// 获取第三个参数：是否保持音调 (0=改变音调, 1=保持音调)
						// 如果没有提供第三个参数，默认保持音调不变
						bool preservePitch = true;
						// 存在第三个不为0的参数，音调改变
						if (arguments.Count >= 3 && arguments[2].GetIntValue(exm) != 0)
						{
							preservePitch = false;
						}
						// 设置音调保持模式
						GlobalStatic.Bgm.SetPreservePitch(preservePitch);
						// 调用Sound类的setSpeed方法
						GlobalStatic.Bgm.setSpeed(speed);
						return 1;
						
					default:
						return -2; // 无效的控制行为
				}
			}
		}
	}

	/// <summary>
	/// int EVAL(string expression, int defaultValue = 0)
	/// 将字符串作为 ERB 整数表达式进行动态求值。如果解析或执行失败，返回默认值。
	/// </summary>
	private sealed class EvalMethod : FunctionMethod
	{
		public EvalMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArrayEx = [
				new ArgTypeList{ ArgTypes = { ArgType.String, ArgType.Int }, OmitStart = 1 },
			];
			CanRestructure = false; // 运行时动态解析，绝对不能在编译期 Restructure
		}

		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			string expressionStr = arguments[0].GetStrValue(exm);
			// 获取第二个参数作为默认值，如果省略则默认为 0
			long defaultValue = arguments.Count > 1 && arguments[1] != null ? arguments[1].GetIntValue(exm) : 0;

			if (string.IsNullOrWhiteSpace(expressionStr))
				return defaultValue;

			try
			{
				CharStream st = new CharStream(expressionStr);
				WordCollection wc = LexicalAnalyzer.Analyse(st, LexEndWith.EoL, LexAnalyzeFlag.None);
				AExpression term = ExpressionParser.ReduceExpressionTerm(wc, TermEndWith.EoL);

				if (term == null) return defaultValue;

				// 绑定当前上下文 (极其重要，解析 LOCAL 等变量)
				term = term.Restructure(exm);

				if (term.GetEraType() == EraType.Integer)
					return term.GetIntValue(exm);
				else if (term.GetEraType() == EraType.Float)
					return (long)term.GetFloatValue(exm);
				else
					return defaultValue;
			}
			catch (EmueraException)
			{
				// 捕获所有解析或运行时错误（如变量不存在、语法错误等），安全返回默认值
				return defaultValue;
			}
		}
	}

	/// <summary>
	/// string EVALS(string expression, string defaultValue = "")
	/// 将字符串作为 ERB 字符串表达式进行动态求值。如果解析或执行失败，返回默认值。
	/// </summary>
	private sealed class EvalSMethod : FunctionMethod
	{
		public EvalSMethod()
		{
			ReturnType = EraType.String;
			argumentTypeArrayEx = [
				new ArgTypeList{ ArgTypes = { ArgType.String, ArgType.String }, OmitStart = 1 },
			];
			CanRestructure = false;
		}

		public override string GetStrValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			string expressionStr = arguments[0].GetStrValue(exm);
			// 获取第二个参数作为默认值，如果省略则默认为空字符串
			string defaultValue = arguments.Count > 1 && arguments[1] != null ? arguments[1].GetStrValue(exm) : "";

			if (string.IsNullOrWhiteSpace(expressionStr))
				return defaultValue;

			try
			{
				CharStream st = new CharStream(expressionStr);
				WordCollection wc = LexicalAnalyzer.Analyse(st, LexEndWith.EoL, LexAnalyzeFlag.None);
				AExpression term = ExpressionParser.ReduceExpressionTerm(wc, TermEndWith.EoL);

				if (term == null) return defaultValue;

				term = term.Restructure(exm);

				if (term.GetEraType() == EraType.String)
					return term.GetStrValue(exm);
				else if (term.GetEraType() == EraType.Float)
					return term.GetFloatValue(exm).ToString();
				else
					return defaultValue;
			}
			catch (EmueraException)
			{
				return defaultValue;
			}
		}
	}

	private sealed class SqlConnectionOpenMethod : FunctionMethod
	{
		public SqlConnectionOpenMethod() { ReturnType = EraType.Integer; argumentTypeArray = new[] { EraType.String }; CanRestructure = false; }
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			return SqlManager.ConnectionOpen(arguments[0].GetStrValue(exm)) ? 1 : 0;
		}
	}

	private sealed class SqlConnectMethod : FunctionMethod
	{
		public SqlConnectMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArrayEx = new[] { new ArgTypeList { ArgTypes = { ArgType.String, ArgType.String }, OmitStart = 1 } };
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			string dbName = arguments[0].GetStrValue(exm);
			string connStr = arguments.Count > 1 && arguments[1] != null ? arguments[1].GetStrValue(exm) : "Data Source=:memory:";
			return SqlManager.Connect(dbName, connStr) ? 1 : 0;
		}
	}

	private sealed class SqlDisconnectMethod : FunctionMethod
	{
		public SqlDisconnectMethod() { ReturnType = EraType.Integer; argumentTypeArray = new[] { EraType.String }; CanRestructure = false; }
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			SqlManager.Disconnect(arguments[0].GetStrValue(exm));
			return 1;
		}
	}

	private sealed class SqlExecuteNonQueryMethod : FunctionMethod
	{
		public SqlExecuteNonQueryMethod() { ReturnType = EraType.Integer; argumentTypeArray = new[] { EraType.String, EraType.String }; CanRestructure = false; }
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			return SqlManager.ExecuteNonQuery(arguments[0].GetStrValue(exm), arguments[1].GetStrValue(exm));
		}
	}

	private sealed class SqlExecuteReaderMethod : FunctionMethod
	{
		public SqlExecuteReaderMethod() { ReturnType = EraType.Integer; argumentTypeArray = new[] { EraType.String, EraType.String }; CanRestructure = false; }
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			return SqlManager.ExecuteReader(arguments[0].GetStrValue(exm), arguments[1].GetStrValue(exm));
		}
	}

	private sealed class SqlReaderReadMethod : FunctionMethod
	{
		public SqlReaderReadMethod() { ReturnType = EraType.Integer; argumentTypeArray = new[] { EraType.Integer }; CanRestructure = false; }
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			return SqlManager.ReaderRead(arguments[0].GetIntValue(exm));
		}
	}

	private sealed class SqlReaderGetLongMethod : FunctionMethod
	{
		public SqlReaderGetLongMethod() { ReturnType = EraType.Integer; argumentTypeArray = new[] { EraType.Integer, EraType.Integer }; CanRestructure = false; }
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			return SqlManager.ReaderGetLong(arguments[0].GetIntValue(exm), (int)arguments[1].GetIntValue(exm));
		}
	}

	private sealed class SqlReaderGetStringMethod : FunctionMethod
	{
		public SqlReaderGetStringMethod() { ReturnType = EraType.String; argumentTypeArray = new[] { EraType.Integer, EraType.Integer }; CanRestructure = false; }
		public override string GetStrValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			return SqlManager.ReaderGetString(arguments[0].GetIntValue(exm), (int)arguments[1].GetIntValue(exm));
		}
	}

	private sealed class SqlReaderIsNullMethod : FunctionMethod
	{
		public SqlReaderIsNullMethod() { ReturnType = EraType.Integer; argumentTypeArray = new[] { EraType.Integer, EraType.Integer }; CanRestructure = false; }
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			return SqlManager.ReaderIsNull(arguments[0].GetIntValue(exm), (int)arguments[1].GetIntValue(exm));
		}
	}

	private sealed class SqlReaderCloseMethod : FunctionMethod
	{
		public SqlReaderCloseMethod() { ReturnType = EraType.Integer; argumentTypeArray = new[] { EraType.Integer }; CanRestructure = false; }
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			SqlManager.ReaderClose(arguments[0].GetIntValue(exm));
			return 1;
		}
	}
	private sealed class SqlExecuteScalarLongMethod : FunctionMethod
	{
		public SqlExecuteScalarLongMethod() { ReturnType = EraType.Integer; argumentTypeArray = new[] { EraType.String, EraType.String }; CanRestructure = false; }
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			return SqlManager.ExecuteScalarLong(arguments[0].GetStrValue(exm), arguments[1].GetStrValue(exm));
		}
	}

	private sealed class SqlExecuteScalarStringMethod : FunctionMethod
	{
		public SqlExecuteScalarStringMethod() { ReturnType = EraType.String; argumentTypeArray = new[] { EraType.String, EraType.String }; CanRestructure = false; }
		public override string GetStrValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			return SqlManager.ExecuteScalarString(arguments[0].GetStrValue(exm), arguments[1].GetStrValue(exm));
		}
	}
	private sealed class SqlImportMapXmlMethod : FunctionMethod
	{
		public SqlImportMapXmlMethod() { ReturnType = EraType.Integer; argumentTypeArray = new[] { EraType.String, EraType.String, EraType.String }; CanRestructure = false; }
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			return SqlManager.ImportMapXml(arguments[0].GetStrValue(exm), arguments[1].GetStrValue(exm), arguments[2].GetStrValue(exm));
		}
	}

	private sealed class SqlImportDtXmlMethod : FunctionMethod
	{
		public SqlImportDtXmlMethod() { ReturnType = EraType.Integer; argumentTypeArray = new[] { EraType.String, EraType.String, EraType.String, EraType.String }; CanRestructure = false; }
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			return SqlManager.ImportDtXml(arguments[0].GetStrValue(exm), arguments[1].GetStrValue(exm), arguments[2].GetStrValue(exm), arguments[3].GetStrValue(exm));
		}
	}

	private sealed class SqlExportMapXmlMethod : FunctionMethod
	{
		public SqlExportMapXmlMethod() { ReturnType = EraType.Integer; argumentTypeArray = new[] { EraType.String, EraType.String, EraType.String }; CanRestructure = false; }
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			return SqlManager.ExportMapXml(arguments[0].GetStrValue(exm), arguments[1].GetStrValue(exm), arguments[2].GetStrValue(exm));
		}
	}

	private sealed class SqlExportDtXmlMethod : FunctionMethod
	{
		public SqlExportDtXmlMethod() { ReturnType = EraType.Integer; argumentTypeArray = new[] { EraType.String, EraType.String, EraType.String, EraType.String }; CanRestructure = false; }
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			return SqlManager.ExportDtXml(arguments[0].GetStrValue(exm), arguments[1].GetStrValue(exm), arguments[2].GetStrValue(exm), arguments[3].GetStrValue(exm));
		}
	}

	private sealed class SqlImportXmlCustomMethod : FunctionMethod
	{
		public SqlImportXmlCustomMethod() { ReturnType = EraType.Integer; argumentTypeArray = new[] { EraType.String, EraType.String, EraType.String, EraType.String, EraType.String }; CanRestructure = false; }
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			return SqlManager.ImportXmlCustom(arguments[0].GetStrValue(exm), arguments[1].GetStrValue(exm), arguments[2].GetStrValue(exm), arguments[3].GetStrValue(exm), arguments[4].GetStrValue(exm));
		}
	}

	private sealed class BitSetMethod : FunctionMethod
	{
		public BitSetMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArrayEx = [
					new ArgTypeList { ArgTypes = { ArgType.RefInt1D, ArgType.Int, ArgType.Int, ArgType.Int }, OmitStart = 2 },
				];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			var arrObj = (arguments[0] as VariableTerm).Identifier.GetArray();
			long idx = arguments[1].GetIntValue(exm);
			long val = arguments.Count > 2 && arguments[2] != null ? arguments[2].GetIntValue(exm) : 1;
			long length = arguments.Count > 3 && arguments[3] != null ? arguments[3].GetIntValue(exm) : 1;
			if (arrObj is long[] array)
				BitArrayManager.BitSet(array, idx, val, length);
			else
			{
				var sa = (SparseArray<long>)arrObj;
				var tmp = sa.ToArray((int)sa.Length);
				BitArrayManager.BitSet(tmp, idx, val, length);
				sa.FromArray(tmp);
			}
			return 1;
		}
	}

	private sealed class BitGetMethod : FunctionMethod
	{
		public BitGetMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArrayEx = [
					new ArgTypeList { ArgTypes = { ArgType.RefInt1D, ArgType.Int }, OmitStart = 1 },
				];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			var arrObj = (arguments[0] as VariableTerm).Identifier.GetArray();
			long idx = arguments[1].GetIntValue(exm);
			if (arrObj is long[] array)
				return BitArrayManager.BitGet(array, idx);
			var sa = (SparseArray<long>)arrObj;
			return BitArrayManager.BitGet(sa.ToArray((int)sa.Length), idx);
		}
	}

	private sealed class BitToggleMethod : FunctionMethod
	{
		public BitToggleMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArrayEx = [
					new ArgTypeList { ArgTypes = { ArgType.RefInt1D, ArgType.Int }, OmitStart = 1 },
				];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			var arrObj = (arguments[0] as VariableTerm).Identifier.GetArray();
			long idx = arguments[1].GetIntValue(exm);
			if (arrObj is long[] array)
				return BitArrayManager.BitToggle(array, idx);
			var sa = (SparseArray<long>)arrObj;
			var tmp = sa.ToArray((int)sa.Length);
			var ret = BitArrayManager.BitToggle(tmp, idx);
			sa.FromArray(tmp);
			return ret;
		}
	}

	private sealed class BitIndexOfFirstMethod : FunctionMethod
	{
		public BitIndexOfFirstMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArrayEx = [
					new ArgTypeList { ArgTypes = { ArgType.RefInt1D, ArgType.Int }, OmitStart = 1 },
				];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			var arrObj = (arguments[0] as VariableTerm).Identifier.GetArray();
			long val = arguments.Count > 1 && arguments[1] != null ? arguments[1].GetIntValue(exm) : 0;
			if (arrObj is long[] array)
				return BitArrayManager.BitIndexOfFirst(array, val);
			var sa = (SparseArray<long>)arrObj;
			return BitArrayManager.BitIndexOfFirst(sa.ToArray((int)sa.Length), val);
		}
	}
	#endregion

	#region EM_私家版_动态渲染管线与质量控制 API
	private sealed class SetTextDrawingModeMethod : FunctionMethod
	{
		public SetTextDrawingModeMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = new EraType[] { EraType.Integer };
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			long mode = arguments[0].GetIntValue(exm);
			if (mode == 1 || mode == 3)
			{
				Config.TextDrawingMode = (TextDrawingMode)mode;
				return 1;
			}
			return 0;
		}
	}

	private sealed class GetTextDrawingModeMethod : FunctionMethod
	{
		public GetTextDrawingModeMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = Array.Empty<EraType>();
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			return (long)Config.TextDrawingMode;
		}
	}

	private sealed class GetSkiaQualityMethod : FunctionMethod
	{
		public GetSkiaQualityMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = new EraType[] { EraType.Integer };
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			long type = arguments[0].GetIntValue(exm);
			switch (type)
			{
				case 0: return (long)Config.ImageQuality;
				case 1: return (long)Config.FontHinting;
				case 2: return (long)Config.FontEdging;
				default: return -1;
			}
		}
	}
	#endregion

	#region B3 Float 反射/动态调用函数
	private sealed class GetVarFMethod : FunctionMethod
	{
		public GetVarFMethod()
		{
			ReturnType = EraType.Float;
			argumentTypeArrayEx = [
					new ArgTypeList{ ArgTypes = { ArgType.String, ArgType.Any }, OmitStart = 1 },
				];
			CanRestructure = false;
		}

		public override double GetFloatValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			double defaultValue = arguments.Count > 1 && arguments[1] != null ? arguments[1].GetFloatValue(exm) : 0.0;
			bool hasDefault = arguments.Count > 1 && arguments[1] != null;
			string name = arguments[0].GetStrValue(exm);

			WordCollection wc = LexicalAnalyzer.Analyse(new CharStream(name), LexEndWith.EoL, LexAnalyzeFlag.None);
			AExpression term = ExpressionParser.ReduceExpressionTerm(wc, TermEndWith.EoL);

			if (term is VariableTerm var)
			{
				if (var.Identifier == null)
					return hasDefault ? defaultValue : throw new CodeEE(string.Format(trerror.IsNotVar.Text, name));
				if (var.GetEraType() != EraType.Float)
					return hasDefault ? defaultValue : throw new CodeEE(string.Format(trerror.IsNotFloat.Text, name));
				return var.GetFloatValue(exm);
			}
			return hasDefault ? defaultValue : throw new CodeEE(string.Format(trerror.IsNotVar.Text, name));
		}
	}

	private sealed class GetMethFMethod : FunctionMethod
	{
		public GetMethFMethod()
		{
			ReturnType = EraType.Float;
			argumentTypeArrayEx = new ArgTypeList[] {
					new ArgTypeList{ ArgTypes = { ArgType.String, ArgType.Any, ArgType.VariadicAny }, OmitStart = 1 },
				};
			CanRestructure = false;
		}

		public override double GetFloatValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			string name = arguments[0].GetStrValue(exm);
			List<AExpression> methArgs = new List<AExpression>(arguments.Skip(2).ToArray());
			var term = GlobalStatic.IdentifierDictionary.GetFunctionMethod(GlobalStatic.LabelDictionary, name, methArgs, true);

			if (term == null)
			{
				if (arguments.Count < 2 || arguments[1] == null)
					throw new CodeEE(string.Format(trerror.NotDefinedUserFunc.Text, name));
				else
					return arguments[1].GetFloatValue(exm);
			}
			else if (term.GetEraType() != EraType.Float)
				throw new CodeEE(string.Format(trerror.IsNotFloat.Text, name));
			else
				return term.GetFloatValue(exm);
		}
	}

	private sealed class EvalFMethod : FunctionMethod
	{
		public EvalFMethod()
		{
			ReturnType = EraType.Float;
			argumentTypeArrayEx = [
				new ArgTypeList{ ArgTypes = { ArgType.String, ArgType.Any }, OmitStart = 1 },
			];
			CanRestructure = false;
		}

		public override double GetFloatValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			string expressionStr = arguments[0].GetStrValue(exm);
			double defaultValue = arguments.Count > 1 && arguments[1] != null ? arguments[1].GetFloatValue(exm) : 0.0;

			if (string.IsNullOrWhiteSpace(expressionStr))
				return defaultValue;

			try
			{
				CharStream st = new CharStream(expressionStr);
				WordCollection wc = LexicalAnalyzer.Analyse(st, LexEndWith.EoL, LexAnalyzeFlag.None);
				AExpression term = ExpressionParser.ReduceExpressionTerm(wc, TermEndWith.EoL);

				if (term == null) return defaultValue;

				term = term.Restructure(exm);

				if (term.GetEraType() == EraType.Float)
					return term.GetFloatValue(exm);
				else if (term.GetEraType() == EraType.Integer)
					return (double)term.GetIntValue(exm);
				else
					return defaultValue;
			}
			catch (EmueraException)
			{
				return defaultValue;
			}
		}
	}
	#endregion

	#region B3 Float DT 函数
	private sealed class DataTableCellGetFloatMethod : FunctionMethod
	{
		public DataTableCellGetFloatMethod()
		{
			ReturnType = EraType.Float;
			argumentTypeArrayEx = [
						new ArgTypeList{ ArgTypes = { ArgType.String, ArgType.Int, ArgType.String, ArgType.Int }, OmitStart = 3 },
					];
			CanRestructure = false;
		}

		public override double GetFloatValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			var key = arguments[0].GetStrValue(exm);
			var dict = exm.VEvaluator.VariableData.DataDataTables;
			if (!dict.TryGetValue(key, out var dt)) return 0.0;
			bool asId = arguments.Count == 4 ? arguments[3].GetIntValue(exm) != 0 : false;
			var idx = arguments[1].GetIntValue(exm);
			var name = arguments[2].GetStrValue(exm);
			if (asId)
			{
				if (dt.Rows.Find(idx) is DataRow row && dt.Columns.Contains(name))
				{
					var v = row[name];
					return v == DBNull.Value ? 0.0 : Convert.ToDouble(v);
				}
			}
			else
			{
				if (0 <= idx && idx < dt.Rows.Count && dt.Columns.Contains(name))
				{
					var v = dt.Rows[(int)idx][name];
					return v == DBNull.Value ? 0.0 : Convert.ToDouble(v);
				}
			}
			return 0.0;
		}
	}
	#endregion

	#region B3 Float SQL 函数
	private sealed class SqlReaderGetFloatMethod : FunctionMethod
	{
		public SqlReaderGetFloatMethod() { ReturnType = EraType.Float; argumentTypeArray = new[] { EraType.Integer, EraType.Integer }; CanRestructure = false; }
		public override double GetFloatValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			return SqlManager.ReaderGetFloat(arguments[0].GetIntValue(exm), (int)arguments[1].GetIntValue(exm));
		}
	}

	private sealed class SqlExecuteScalarFloatMethod : FunctionMethod
	{
		public SqlExecuteScalarFloatMethod() { ReturnType = EraType.Float; argumentTypeArray = new[] { EraType.String, EraType.String }; CanRestructure = false; }
		public override double GetFloatValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			return SqlManager.ExecuteScalarFloat(arguments[0].GetStrValue(exm), arguments[1].GetStrValue(exm));
		}
	}

	private sealed class SqlExecuteScalarFloatParamMethod : FunctionMethod
	{
		public SqlExecuteScalarFloatParamMethod()
		{
			ReturnType = EraType.Float;
			argumentTypeArrayEx = [
					new ArgTypeList{ ArgTypes = { ArgType.String, ArgType.String, ArgType.VariadicString }, OmitStart = 2 },
				];
			CanRestructure = false;
		}

		public override double GetFloatValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			var dbName = arguments[0].GetStrValue(exm);
			var sql = arguments[1].GetStrValue(exm);
			if (arguments.Count <= 2)
				return SqlManager.ExecuteScalarFloat(dbName, sql);
			var paramValues = new string[arguments.Count - 2];
			for (int i = 2; i < arguments.Count; i++)
				paramValues[i - 2] = arguments[i]?.GetStrValue(exm) ?? null;
			return SqlManager.ExecuteScalarFloat(dbName, sql, paramValues);
		}
	}
	#endregion

	private sealed class ArgLengthMethod : FunctionMethod
	{
		public ArgLengthMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArrayEx = [
					new ArgTypeList{ ArgTypes = { } },
				];
			CanRestructure = false;
		}

		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			return exm.Process.State.CurrentVariadicArgCount;
		}
	}

	public sealed class ExistsImageLayerMethod : FunctionMethod
	{
		public ExistsImageLayerMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = [EraType.Integer];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			long depth = arguments[0].GetIntValue(exm);
			return exm.Console.ExistsImageLayer(depth) ? 1 : 0;
		}
	}

	/// <summary>
	/// GETLINEY(lineNo) — 返回指定行号的物理 Y 坐标（左下原点，与 SETIMAGELAYER 坐标系一致）
	/// 坐标系：y=0 为窗口底部，负值向上。转换公式：y = GetLinePointY(lineNo) + LineHeight - ClientHeight
	/// </summary>
	public sealed class GetLineYMethod : FunctionMethod
	{
		public GetLineYMethod()
		{
			ReturnType = EraType.Integer;
			argumentTypeArray = [EraType.Integer];
			CanRestructure = false;
		}
		public override long GetIntValue(ExpressionMediator exm, List<AExpression> arguments)
		{
			long lineNo = arguments[0].GetIntValue(exm);
			if (lineNo < 0)
				throw new CodeEE(string.Format(trerror.ArgIsNegative.Text, Name, 1, lineNo));
			// GetLinePointY 返回自顶向下像素坐标（0在顶部），
			// SETIMAGELAYER 使用左下原点坐标系（0在底部，负值向上）。
			// 转换：y = pointY + LineHeight - ClientHeight
			// 效果：SETIMAGELAYER 传入此 y 值时，图片底边对齐行底边。
			int pointY = exm.Console.GetLinePointY((int)lineNo);
			return pointY + Config.LineHeight - exm.Console.ClientHeight;
		}
	}
}
