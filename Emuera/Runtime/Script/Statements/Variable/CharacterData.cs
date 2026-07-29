using MinorShift.Emuera.GameData.Variable;
using MinorShift.Emuera.Runtime.Script;
using MinorShift.Emuera.Runtime.Script.Data;
using MinorShift.Emuera.Runtime.Utils;
using System;
using System.Collections.Generic;
using trerror = MinorShift.Emuera.Runtime.Utils.EvilMask.Lang.Error;

namespace MinorShift.Emuera.Runtime.Script.Statements.Variable;

internal sealed class CharacterData : IDisposable
{
	readonly long[] dataInteger;
	readonly string[] dataString;
	readonly double[] dataFloat;
	readonly SparseArray<long>[] dataIntegerArray;
	readonly SparseArray<string>[] dataStringArray;
	readonly SparseArray<double>[] dataFloatArray;
	readonly long[][,] dataIntegerArray2D;
	readonly string[][,] dataStringArray2D;
	readonly double[][,] dataFloatArray2D;
	public long[] DataInteger { get { return dataInteger; } }
	public string[] DataString { get { return dataString; } }
	public double[] DataFloat { get { return dataFloat; } }
	public SparseArray<long>[] DataIntegerArray { get { return dataIntegerArray; } }
	public SparseArray<string>[] DataStringArray { get { return dataStringArray; } }
	public SparseArray<double>[] DataFloatArray { get { return dataFloatArray; } }
	public long[][,] DataIntegerArray2D { get { return dataIntegerArray2D; } }
	public string[][,] DataStringArray2D { get { return dataStringArray2D; } }
	public double[][,] DataFloatArray2D { get { return dataFloatArray2D; } }

	public List<object> UserDefCVarDataList { get; set; }

	public CharacterData(ConstantData constant, VariableData varData)
	{
		dataInteger = new long[(int)VariableCode.__COUNT_CHARACTER_INTEGER__];
		dataString = new string[(int)VariableCode.__COUNT_CHARACTER_STRING__];
		dataFloat = Array.Empty<double>();
		dataIntegerArray = new SparseArray<long>[(int)VariableCode.__COUNT_CHARACTER_INTEGER_ARRAY__];
		dataStringArray = new SparseArray<string>[(int)VariableCode.__COUNT_CHARACTER_STRING_ARRAY__];
		dataFloatArray = Array.Empty<SparseArray<double>>();
		dataIntegerArray2D = new long[(int)VariableCode.__COUNT_CHARACTER_INTEGER_ARRAY_2D__][,];
		dataStringArray2D = [];
		dataFloatArray2D = Array.Empty<double[,]>();
		for (int i = 0; i < dataIntegerArray.Length; i++)
		{
			dataIntegerArray[i] = new SparseArray<long>();
			dataIntegerArray[i].Length = constant.CharacterIntArrayLength[i];
		}
		for (int i = 0; i < dataStringArray.Length; i++)
		{
			dataStringArray[i] = new SparseArray<string>();
			dataStringArray[i].Length = constant.CharacterStrArrayLength[i];
		}
		for (int i = 0; i < dataFloatArray.Length; i++)
		{
			dataFloatArray[i] = new SparseArray<double>();
			dataFloatArray[i].Length = constant.CharacterFloatArrayLength[i];
		}
		for (int i = 0; i < dataIntegerArray2D.Length; i++)
		{
			long length64 = constant.CharacterIntArray2DLength[i];
			int length = (int)(length64 >> 32);
			int length2 = (int)(length64 & 0x7FFFFFFF);
			dataIntegerArray2D[i] = new long[length, length2];
		}
		for (int i = 0; i < dataStringArray2D.Length; i++)
		{
			long length64 = constant.CharacterStrArray2DLength[i];
			int length = (int)(length64 >> 32);
			int length2 = (int)(length64 & 0x7FFFFFFF);
			dataStringArray2D[i] = new string[length, length2];
		}
		for (int i = 0; i < dataFloatArray2D.Length; i++)
		{
			long length64 = constant.CharacterFloatArray2DLength[i];
			int length = (int)(length64 >> 32);
			int length2 = (int)(length64 & 0x7FFFFFFF);
			dataFloatArray2D[i] = new double[length, length2];
		}
		UserDefCVarDataList = [];
		for (int i = 0; i < varData.UserDefinedCharaVarList.Count; i++)
		{
			UserDefinedVariableData d = varData.UserDefinedCharaVarList[i].DimData;
			object array = null;
			if (d.TypeIsStr)
			{
				switch (d.Dimension)
				{
					case 1:
						array = new SparseArray<string> { Length = d.Lengths[0] };
						break;
					case 2:
						array = new string[d.Lengths[0], d.Lengths[1]];
						break;
					case 3:
						array = new string[d.Lengths[0], d.Lengths[1], d.Lengths[2]];
						break;
				}
			}
			else if (d.TypeIsFloat)
			{
				switch (d.Dimension)
				{
					case 1:
						array = new SparseArray<double> { Length = d.Lengths[0] };
						break;
					case 2:
						array = new double[d.Lengths[0], d.Lengths[1]];
						break;
					case 3:
						array = new double[d.Lengths[0], d.Lengths[1], d.Lengths[2]];
						break;
				}
			}
			else
			{
				switch (d.Dimension)
				{
					case 1:
						array = new SparseArray<long> { Length = d.Lengths[0] };
						break;
					case 2:
						array = new long[d.Lengths[0], d.Lengths[1]];
						break;
					case 3:
						array = new long[d.Lengths[0], d.Lengths[1], d.Lengths[2]];
						break;
				}
			}
			if (array == null)
				throw new ExeEE("");
			UserDefCVarDataList.Add(array);
		}
	}


	public CharacterData(ConstantData constant, CharacterTemplate tmpl, VariableData varData)
		: this(constant, varData)
	{

		dataInteger[(int)VariableCode.__LOWERCASE__ & (int)VariableCode.NO] = tmpl.No;
		dataString[(int)VariableCode.__LOWERCASE__ & (int)VariableCode.NAME] = tmpl.Name;
		dataString[(int)VariableCode.__LOWERCASE__ & (int)VariableCode.CALLNAME] = tmpl.Callname;
		dataString[(int)VariableCode.__LOWERCASE__ & (int)VariableCode.NICKNAME] = tmpl.Nickname;
		dataString[(int)VariableCode.__LOWERCASE__ & (int)VariableCode.MASTERNAME] = tmpl.Mastername;
		var array = dataIntegerArray[(int)VariableCode.__LOWERCASE__ & (int)VariableCode.MAXBASE];
		var array2 = dataIntegerArray[(int)VariableCode.__LOWERCASE__ & (int)VariableCode.BASE];
		foreach (KeyValuePair<int, long> pair in tmpl.Maxbase)
		{
			array[pair.Key] = pair.Value;
			array2[pair.Key] = pair.Value;
		}
		array = dataIntegerArray[(int)VariableCode.__LOWERCASE__ & (int)VariableCode.MARK];
		foreach (KeyValuePair<int, long> pair in tmpl.Mark)
			array[pair.Key] = pair.Value;
		array = dataIntegerArray[(int)VariableCode.__LOWERCASE__ & (int)VariableCode.EXP];
		foreach (KeyValuePair<int, long> pair in tmpl.Exp)
			array[pair.Key] = pair.Value;
		array = dataIntegerArray[(int)VariableCode.__LOWERCASE__ & (int)VariableCode.ABL];
		foreach (KeyValuePair<int, long> pair in tmpl.Abl)
			array[pair.Key] = pair.Value;
		array = dataIntegerArray[(int)VariableCode.__LOWERCASE__ & (int)VariableCode.TALENT];
		foreach (KeyValuePair<int, long> pair in tmpl.Talent)
			array[pair.Key] = pair.Value;
		array = dataIntegerArray[(int)VariableCode.__LOWERCASE__ & (int)VariableCode.RELATION];
		for (int i = 0; i < array.Length; i++)
			array[i] = Config.Config.RelationDef;
		foreach (KeyValuePair<int, long> pair in tmpl.Relation)
			array[pair.Key] = pair.Value;
		array = dataIntegerArray[(int)VariableCode.__LOWERCASE__ & (int)VariableCode.CFLAG];
		foreach (KeyValuePair<int, long> pair in tmpl.CFlag)
			array[pair.Key] = pair.Value;
		array = dataIntegerArray[(int)VariableCode.__LOWERCASE__ & (int)VariableCode.EQUIP];
		foreach (KeyValuePair<int, long> pair in tmpl.Equip)
			array[pair.Key] = pair.Value;
		array = dataIntegerArray[(int)VariableCode.__LOWERCASE__ & (int)VariableCode.JUEL];
		foreach (KeyValuePair<int, long> pair in tmpl.Juel)
			array[pair.Key] = pair.Value;
		var arrays = dataStringArray[(int)VariableCode.__LOWERCASE__ & (int)VariableCode.CSTR];
		foreach (KeyValuePair<int, string> pair in tmpl.CStr)
			arrays[pair.Key] = pair.Value;
		/*
		//tmpl.Maxbase.CopyTo(dataIntegerArray[(int)VariableCode.__LOWERCASE__ & (int)VariableCode.MAXBASE], 0);
		Buffer.BlockCopy(tmpl.Maxbase, 0, dataIntegerArray[(int)VariableCode.__LOWERCASE__ & (int)VariableCode.MAXBASE], 0, 8 * constant.CharacterIntArrayLength[(int)VariableCode.__LOWERCASE__ & (int)VariableCode.MAXBASE]);
		//tmpl.Maxbase.CopyTo(dataIntegerArray[(int)VariableCode.__LOWERCASE__ & (int)VariableCode.BASE], 0);
		Buffer.BlockCopy(tmpl.Maxbase, 0, dataIntegerArray[(int)VariableCode.__LOWERCASE__ & (int)VariableCode.BASE], 0, 8 * constant.CharacterIntArrayLength[(int)VariableCode.__LOWERCASE__ & (int)VariableCode.BASE]);

		//tmpl.Mark.CopyTo(dataIntegerArray[(int)VariableCode.__LOWERCASE__ & (int)VariableCode.MARK], 0);
		Buffer.BlockCopy(tmpl.Mark, 0, dataIntegerArray[(int)VariableCode.__LOWERCASE__ & (int)VariableCode.MARK], 0, 8 * constant.CharacterIntArrayLength[(int)VariableCode.__LOWERCASE__ & (int)VariableCode.MARK]);
		//tmpl.Exp.CopyTo(dataIntegerArray[(int)VariableCode.__LOWERCASE__ & (int)VariableCode.EXP], 0);
		Buffer.BlockCopy(tmpl.Exp, 0, dataIntegerArray[(int)VariableCode.__LOWERCASE__ & (int)VariableCode.EXP], 0, 8 * constant.CharacterIntArrayLength[(int)VariableCode.__LOWERCASE__ & (int)VariableCode.EXP]);
		//tmpl.Abl.CopyTo(dataIntegerArray[(int)VariableCode.__LOWERCASE__ & (int)VariableCode.ABL], 0);
		Buffer.BlockCopy(tmpl.Abl, 0, dataIntegerArray[(int)VariableCode.__LOWERCASE__ & (int)VariableCode.ABL], 0, 8 * constant.CharacterIntArrayLength[(int)VariableCode.__LOWERCASE__ & (int)VariableCode.ABL]);
		//tmpl.Talent.CopyTo(dataIntegerArray[(int)VariableCode.__LOWERCASE__ & (int)VariableCode.TALENT], 0);
		Buffer.BlockCopy(tmpl.Talent, 0, dataIntegerArray[(int)VariableCode.__LOWERCASE__ & (int)VariableCode.TALENT], 0, 8 * constant.CharacterIntArrayLength[(int)VariableCode.__LOWERCASE__ & (int)VariableCode.TALENT]);
		//tmpl.Relation.CopyTo(dataIntegerArray[(int)VariableCode.__LOWERCASE__ & (int)VariableCode.RELATION], 0);
		Buffer.BlockCopy(tmpl.Relation, 0, dataIntegerArray[(int)VariableCode.__LOWERCASE__ & (int)VariableCode.RELATION], 0, 8 * constant.CharacterIntArrayLength[(int)VariableCode.__LOWERCASE__ & (int)VariableCode.RELATION]);
		//tmpl.CFlag.CopyTo(dataIntegerArray[(int)VariableCode.__LOWERCASE__ & (int)VariableCode.CFLAG], 0);
		Buffer.BlockCopy(tmpl.CFlag, 0, dataIntegerArray[(int)VariableCode.__LOWERCASE__ & (int)VariableCode.CFLAG], 0, 8 * constant.CharacterIntArrayLength[(int)VariableCode.__LOWERCASE__ & (int)VariableCode.CFLAG]);
		//tmpl.Equip.CopyTo(dataIntegerArray[(int)VariableCode.__LOWERCASE__ & (int)VariableCode.EQUIP], 0);
		Buffer.BlockCopy(tmpl.Equip, 0, dataIntegerArray[(int)VariableCode.__LOWERCASE__ & (int)VariableCode.EQUIP], 0, 8 * constant.CharacterIntArrayLength[(int)VariableCode.__LOWERCASE__ & (int)VariableCode.EQUIP]);
		//tmpl.Juel.CopyTo(dataIntegerArray[(int)VariableCode.__LOWERCASE__ & (int)VariableCode.JUEL], 0);
		Buffer.BlockCopy(tmpl.Juel, 0, dataIntegerArray[(int)VariableCode.__LOWERCASE__ & (int)VariableCode.JUEL], 0, 8 * constant.CharacterIntArrayLength[(int)VariableCode.__LOWERCASE__ & (int)VariableCode.JUEL]);

		tmpl.CStr.CopyTo(dataStringArray[(int)VariableCode.__LOWERCASE__ & (int)VariableCode.CSTR], 0);
		*/
	}

	public static int[] CharacterVarLength(VariableCode code, ConstantData constant)
	{
		int[] ret = null;
		var desc = VariableDescriptor.FromCode(code, "");
		int i = (int)(code & VariableCode.__LOWERCASE__);
		if (i >= 0xF0)
			return null;
		long length64;
		if (desc.IsInteger)
		{
			switch (desc.Dimension)
			{
				case VariableDimension.Scalar: ret = []; break;
				case VariableDimension.Array1D: ret = [constant.CharacterIntArrayLength[i]]; break;
				case VariableDimension.Array2D:
					ret = new int[2];
					length64 = constant.CharacterIntArray2DLength[i];
					ret[0] = (int)(length64 >> 32);
					ret[1] = (int)(length64 & 0x7FFFFFFF);
					break;
				case VariableDimension.Array3D: throw new NotImplCodeEE();
			}
		}
		else if (desc.IsFloat)
		{
			switch (desc.Dimension)
			{
				case VariableDimension.Scalar: ret = []; break;
				case VariableDimension.Array1D: ret = [constant.CharacterFloatArrayLength[i]]; break;
				case VariableDimension.Array2D:
					ret = new int[2];
					length64 = constant.CharacterFloatArray2DLength[i];
					ret[0] = (int)(length64 >> 32);
					ret[1] = (int)(length64 & 0x7FFFFFFF);
					break;
				case VariableDimension.Array3D: throw new NotImplCodeEE();
			}
		}
		else if (desc.IsString)
		{
			switch (desc.Dimension)
			{
				case VariableDimension.Scalar: ret = []; break;
				case VariableDimension.Array1D: ret = [constant.CharacterStrArrayLength[i]]; break;
				case VariableDimension.Array2D:
					ret = new int[2];
					length64 = constant.CharacterStrArray2DLength[i];
					ret[0] = (int)(length64 >> 32);
					ret[1] = (int)(length64 & 0x7FFFFFFF);
					break;
				case VariableDimension.Array3D: throw new NotImplCodeEE();
			}
		}
		return ret;
	}
	public void CopyTo(CharacterData other, VariableData varData)
	{
		for (int i = 0; i < dataInteger.Length; i++)
			other.dataInteger[i] = dataInteger[i];
		for (int i = 0; i < dataString.Length; i++)
			other.dataString[i] = dataString[i];
		for (int i = 0; i < dataFloat.Length; i++)
			other.dataFloat[i] = dataFloat[i];

		for (int i = 0; i < dataIntegerArray.Length; i++)
		{
			int len = dataIntegerArray[i].Length;
			for (int j = 0; j < len; j++)
				other.dataIntegerArray[i][j] = dataIntegerArray[i][j];
		}
		for (int i = 0; i < dataStringArray.Length; i++)
		{
			int len = dataStringArray[i].Length;
			for (int j = 0; j < len; j++)
				other.dataStringArray[i][j] = dataStringArray[i][j];
		}
		for (int i = 0; i < dataFloatArray.Length; i++)
		{
			int len = dataFloatArray[i].Length;
			for (int j = 0; j < len; j++)
				other.dataFloatArray[i][j] = dataFloatArray[i][j];
		}

		for (int i = 0; i < dataIntegerArray2D.Length; i++)
		{
			int length1 = dataIntegerArray2D[i].GetLength(0);
			int length2 = dataIntegerArray2D[i].GetLength(1);
			for (int j = 0; j < length1; j++)
				for (int k = 0; k < length2; k++)
					other.dataIntegerArray2D[i][j, k] = dataIntegerArray2D[i][j, k];
		}
		for (int i = 0; i < dataStringArray2D.Length; i++)
		{
			int length1 = dataStringArray2D[i].GetLength(0);
			int length2 = dataStringArray2D[i].GetLength(1);
			for (int j = 0; j < length1; j++)
				for (int k = 0; k < length2; k++)
					other.dataStringArray2D[i][j, k] = dataStringArray2D[i][j, k];
		}
		for (int i = 0; i < dataFloatArray2D.Length; i++)
		{
			int length1 = dataFloatArray2D[i].GetLength(0);
			int length2 = dataFloatArray2D[i].GetLength(1);
			for (int j = 0; j < length1; j++)
				for (int k = 0; k < length2; k++)
					other.dataFloatArray2D[i][j, k] = dataFloatArray2D[i][j, k];
		}
		if (UserDefCVarDataList.Count > 0)
		{
			foreach (UserDefinedCharaVariableToken var in varData.UserDefinedCharaVarList)
			{
				if (!var.IsCharacterData)
					continue;
				var eraType = var.GetEraType();
				if (eraType == EraType.String)
				{
					if (var.IsArray1D)
					{
						var src = (SparseArray<string>)UserDefCVarDataList[var.ArrayIndex];
						var dst = (SparseArray<string>)other.UserDefCVarDataList[var.ArrayIndex];
						dst.Clear();
						for (int i = 0; i < src.Length; i++)
							dst[i] = src[i];
					}
					else if (var.IsArray2D)
					{
						int length1 = ((string[,])UserDefCVarDataList[var.ArrayIndex]).GetLength(0);
						int length2 = ((string[,])UserDefCVarDataList[var.ArrayIndex]).GetLength(1);
						for (int i = 0; i < length1; i++)
							for (int j = 0; j < length2; j++)
								((string[,])other.UserDefCVarDataList[var.ArrayIndex])[i, j] = ((string[,])UserDefCVarDataList[var.ArrayIndex])[i, j];
					}
				}
				else if (eraType == EraType.Integer)
				{
					if (var.IsArray1D)
					{
						var src = (SparseArray<long>)UserDefCVarDataList[var.ArrayIndex];
						var dst = (SparseArray<long>)other.UserDefCVarDataList[var.ArrayIndex];
						dst.Clear();
						for (int i = 0; i < src.Length; i++)
							dst[i] = src[i];
					}
					else if (var.IsArray2D)
					{
						int length1 = ((long[,])UserDefCVarDataList[var.ArrayIndex]).GetLength(0);
						int length2 = ((long[,])UserDefCVarDataList[var.ArrayIndex]).GetLength(1);
						for (int i = 0; i < length1; i++)
							for (int j = 0; j < length2; j++)
								((long[,])other.UserDefCVarDataList[var.ArrayIndex])[i, j] = ((long[,])UserDefCVarDataList[var.ArrayIndex])[i, j];
					}
				}
				else if (eraType == EraType.Float)
				{
					if (var.IsArray1D)
					{
						var src = (SparseArray<double>)UserDefCVarDataList[var.ArrayIndex];
						var dst = (SparseArray<double>)other.UserDefCVarDataList[var.ArrayIndex];
						dst.Clear();
						for (int i = 0; i < src.Length; i++)
							dst[i] = src[i];
					}
					else if (var.IsArray2D)
					{
						int length1 = ((double[,])UserDefCVarDataList[var.ArrayIndex]).GetLength(0);
						int length2 = ((double[,])UserDefCVarDataList[var.ArrayIndex]).GetLength(1);
						for (int i = 0; i < length1; i++)
							for (int j = 0; j < length2; j++)
								((double[,])other.UserDefCVarDataList[var.ArrayIndex])[i, j] = ((double[,])UserDefCVarDataList[var.ArrayIndex])[i, j];
					}
				}
			}
		}
	}

	const int strCount = (int)VariableCode.__COUNT_SAVE_CHARACTER_STRING__;
	const int intCount = (int)VariableCode.__COUNT_SAVE_CHARACTER_INTEGER__;
	const int floatCount = (int)VariableCode.__COUNT_SAVE_CHARACTER_FLOAT__;
	const int intArrayCount = (int)VariableCode.__COUNT_SAVE_CHARACTER_INTEGER_ARRAY__;
	const int strArrayCount = (int)VariableCode.__COUNT_SAVE_CHARACTER_STRING_ARRAY__;
	const int floatArrayCount = (int)VariableCode.__COUNT_SAVE_CHARACTER_FLOAT_ARRAY__;

	public void SaveToStream(EraDataWriter writer)
	{

		for (int i = 0; i < strCount; i++)
			writer.Write(dataString[i]);
		for (int i = 0; i < intCount; i++)
			writer.Write(dataInteger[i]);
		for (int i = 0; i < floatCount; i++)
			writer.Write(dataFloat[i]);
		for (int i = 0; i < intArrayCount; i++)
			writer.Write(dataIntegerArray[i].ToArray(dataIntegerArray[i].Length));
		for (int i = 0; i < strArrayCount; i++)
			writer.Write(dataStringArray[i].ToArray(dataStringArray[i].Length));
		for (int i = 0; i < floatArrayCount; i++)
			writer.Write(dataFloatArray[i].ToArray(dataFloatArray[i].Length));
	}

	public void LoadFromStream(EraDataReader reader)
	{

		for (int i = 0; i < strCount; i++)
			dataString[i] = reader.ReadString();
		for (int i = 0; i < intCount; i++)
			dataInteger[i] = reader.ReadInt64();
		for (int i = 0; i < floatCount; i++)
			dataFloat[i] = reader.ReadDouble();
		for (int i = 0; i < intArrayCount; i++)
		{
			var arr = new long[dataIntegerArray[i].Length];
			reader.ReadInt64Array(arr);
			dataIntegerArray[i].FromArray(arr);
		}
		for (int i = 0; i < strArrayCount; i++)
		{
			var arr = new string[dataStringArray[i].Length];
			reader.ReadStringArray(arr);
			dataStringArray[i].FromArray(arr);
		}
		for (int i = 0; i < floatArrayCount; i++)
		{
			var arr = new double[dataFloatArray[i].Length];
			reader.ReadDoubleArray(arr);
			dataFloatArray[i].FromArray(arr);
		}
	}
	public void SaveToStreamExtended(EraDataWriter writer)
	{
		List<VariableCode> codeList;

		//dataString
		codeList = VariableIdentifier.GetExtSaveList(VariableKind.String, VariableDimension.Scalar);
		foreach (VariableCode code in codeList)
			writer.WriteExtended(code.ToString(), dataString[(int)VariableCode.__LOWERCASE__ & (int)code]);
		writer.EmuSeparete();

		//datainteger
		codeList = VariableIdentifier.GetExtSaveList(VariableKind.Integer, VariableDimension.Scalar);
		foreach (VariableCode code in codeList)
			writer.WriteExtended(code.ToString(), dataInteger[(int)VariableCode.__LOWERCASE__ & (int)code]);
		writer.EmuSeparete();

		//dataFloat
		codeList = VariableIdentifier.GetExtSaveList(VariableKind.Float, VariableDimension.Scalar);
		foreach (VariableCode code in codeList)
			writer.WriteExtended(code.ToString(), dataFloat[(int)VariableCode.__LOWERCASE__ & (int)code]);
		writer.EmuSeparete();

		//dataStringArray
		codeList = VariableIdentifier.GetExtSaveList(VariableKind.String, VariableDimension.Array1D);
		foreach (VariableCode code in codeList)
		{
			int idx = (int)VariableCode.__LOWERCASE__ & (int)code;
			writer.WriteExtended(code.ToString(), dataStringArray[idx].ToArray(dataStringArray[idx].Length));
		}
		writer.EmuSeparete();

		//dataIntegerArray
		codeList = VariableIdentifier.GetExtSaveList(VariableKind.Integer, VariableDimension.Array1D);
		foreach (VariableCode code in codeList)
		{
			int idx = (int)VariableCode.__LOWERCASE__ & (int)code;
			writer.WriteExtended(code.ToString(), dataIntegerArray[idx].ToArray(dataIntegerArray[idx].Length));
		}
		writer.EmuSeparete();

		//dataFloatArray
		codeList = VariableIdentifier.GetExtSaveList(VariableKind.Float, VariableDimension.Array1D);
		foreach (VariableCode code in codeList)
		{
			int idx = (int)VariableCode.__LOWERCASE__ & (int)code;
			writer.WriteExtended(code.ToString(), dataFloatArray[idx].ToArray(dataFloatArray[idx].Length));
		}
		writer.EmuSeparete();

		//dataStringArray2D
		codeList = VariableIdentifier.GetExtSaveList(VariableKind.String, VariableDimension.Array2D);
		foreach (VariableCode code in codeList)
			writer.WriteExtended(code.ToString(), dataStringArray2D[(int)VariableCode.__LOWERCASE__ & (int)code]);
		writer.EmuSeparete();

		//dataIntegerArray2D
		codeList = VariableIdentifier.GetExtSaveList(VariableKind.Integer, VariableDimension.Array2D);
		foreach (VariableCode code in codeList)
			writer.WriteExtended(code.ToString(), dataIntegerArray2D[(int)VariableCode.__LOWERCASE__ & (int)code]);
		writer.EmuSeparete();

		//dataFloatArray2D
		codeList = VariableIdentifier.GetExtSaveList(VariableKind.Float, VariableDimension.Array2D);
		foreach (VariableCode code in codeList)
			writer.WriteExtended(code.ToString(), dataFloatArray2D[(int)VariableCode.__LOWERCASE__ & (int)code]);
		writer.EmuSeparete();
	}

	public void LoadFromStreamExtended(EraDataReader reader)
	{
		Dictionary<string, string> strDic = reader.ReadStringExtended();
		Dictionary<string, long> intDic = reader.ReadInt64Extended();
		Dictionary<string, double> floatDic = reader.ReadDoubleExtended();
		Dictionary<string, List<string>> strListDic = reader.ReadStringArrayExtended();
		Dictionary<string, List<long>> intListDic = reader.ReadInt64ArrayExtended();
		Dictionary<string, List<double>> floatListDic = reader.ReadDoubleArrayExtended();
		Dictionary<string, List<string[]>> str2DListDic = reader.ReadStringArray2DExtended();
		Dictionary<string, List<long[]>> int2DListDic = reader.ReadInt64Array2DExtended();
		Dictionary<string, List<double[]>> float2DListDic = reader.ReadDoubleArray2DExtended();

		List<VariableCode> codeList;

		codeList = VariableIdentifier.GetExtSaveList(VariableKind.String, VariableDimension.Scalar);
		foreach (VariableCode code in codeList)
			if (strDic.ContainsKey(code.ToString()))
				dataString[(int)VariableCode.__LOWERCASE__ & (int)code] = strDic[code.ToString()];

		codeList = VariableIdentifier.GetExtSaveList(VariableKind.Integer, VariableDimension.Scalar);
		foreach (VariableCode code in codeList)
			if (intDic.ContainsKey(code.ToString()))
				dataInteger[(int)VariableCode.__LOWERCASE__ & (int)code] = intDic[code.ToString()];

		codeList = VariableIdentifier.GetExtSaveList(VariableKind.Float, VariableDimension.Scalar);
		foreach (VariableCode code in codeList)
			if (floatDic.ContainsKey(code.ToString()))
				dataFloat[(int)VariableCode.__LOWERCASE__ & (int)code] = floatDic[code.ToString()];


		codeList = VariableIdentifier.GetExtSaveList(VariableKind.String, VariableDimension.Array1D);
		foreach (VariableCode code in codeList)
			if (strListDic.ContainsKey(code.ToString()))
				copyListToSparseArray(strListDic[code.ToString()], dataStringArray[(int)VariableCode.__LOWERCASE__ & (int)code]);

		codeList = VariableIdentifier.GetExtSaveList(VariableKind.Integer, VariableDimension.Array1D);
		foreach (VariableCode code in codeList)
			if (intListDic.ContainsKey(code.ToString()))
				copyListToSparseArray(intListDic[code.ToString()], dataIntegerArray[(int)VariableCode.__LOWERCASE__ & (int)code]);

		codeList = VariableIdentifier.GetExtSaveList(VariableKind.Float, VariableDimension.Array1D);
		foreach (VariableCode code in codeList)
			if (floatListDic.ContainsKey(code.ToString()))
				copyListToSparseArray(floatListDic[code.ToString()], dataFloatArray[(int)VariableCode.__LOWERCASE__ & (int)code]);

		//dataStringArray2D
		codeList = VariableIdentifier.GetExtSaveList(VariableKind.String, VariableDimension.Array2D);
		foreach (VariableCode code in codeList)
			if (int2DListDic.ContainsKey(code.ToString()))
				copyListToArray2D(str2DListDic[code.ToString()], dataStringArray2D[(int)VariableCode.__LOWERCASE__ & (int)code]);

		//dataIntegerArray2D
		codeList = VariableIdentifier.GetExtSaveList(VariableKind.Integer, VariableDimension.Array2D);
		foreach (VariableCode code in codeList)
			if (int2DListDic.ContainsKey(code.ToString()))
				copyListToArray2D(int2DListDic[code.ToString()], dataIntegerArray2D[(int)VariableCode.__LOWERCASE__ & (int)code]);

		//dataFloatArray2D
		codeList = VariableIdentifier.GetExtSaveList(VariableKind.Float, VariableDimension.Array2D);
		foreach (VariableCode code in codeList)
			if (float2DListDic.ContainsKey(code.ToString()))
				copyListToArray2D(float2DListDic[code.ToString()], dataFloatArray2D[(int)VariableCode.__LOWERCASE__ & (int)code]);
	}

	public void LoadFromStreamExtended_Old1802(EraDataReader reader)
	{
		Dictionary<string, string> strDic = reader.ReadStringExtended();
		Dictionary<string, long> intDic = reader.ReadInt64Extended();
		Dictionary<string, List<string>> strListDic = reader.ReadStringArrayExtended();
		Dictionary<string, List<long>> intListDic = reader.ReadInt64ArrayExtended();

		List<VariableCode> codeList;

		codeList = VariableIdentifier.GetExtSaveList(VariableKind.String, VariableDimension.Scalar);
		foreach (VariableCode code in codeList)
			if (strDic.ContainsKey(code.ToString()))
				dataString[(int)VariableCode.__LOWERCASE__ & (int)code] = strDic[code.ToString()];

		codeList = VariableIdentifier.GetExtSaveList(VariableKind.Integer, VariableDimension.Scalar);
		foreach (VariableCode code in codeList)
			if (intDic.ContainsKey(code.ToString()))
				dataInteger[(int)VariableCode.__LOWERCASE__ & (int)code] = intDic[code.ToString()];


		codeList = VariableIdentifier.GetExtSaveList(VariableKind.String, VariableDimension.Array1D);
		foreach (VariableCode code in codeList)
			if (strListDic.ContainsKey(code.ToString()))
				copyListToSparseArray(strListDic[code.ToString()], dataStringArray[(int)VariableCode.__LOWERCASE__ & (int)code]);

		codeList = VariableIdentifier.GetExtSaveList(VariableKind.Integer, VariableDimension.Array1D);
		foreach (VariableCode code in codeList)
			if (intListDic.ContainsKey(code.ToString()))
				copyListToSparseArray(intListDic[code.ToString()], dataIntegerArray[(int)VariableCode.__LOWERCASE__ & (int)code]);

	}

	public void SaveToStreamBinary(EraBinaryDataWriter writer, VariableData varData)
	{
		//eramaker変数の保存
		foreach (KeyValuePair<string, VariableToken> pair in varData.GetVarTokenDic())
		{
			VariableToken var = pair.Value;
			if (!var.IsSavedata || !var.IsCharacterData || var.IsGlobal)
				continue;
			VariableCode code = var.Code;
			int CodeInt = var.CodeInt;
			var desc = VariableDescriptor.FromCode(code, "");
			if (desc.IsInteger)
			{
				switch (desc.Dimension)
				{
					case VariableDimension.Scalar:
						writer.WriteWithKey(code.ToString(), dataInteger[CodeInt]);
						break;
					case VariableDimension.Array1D:
						if (dataIntegerArray[CodeInt] != null)
							writer.WriteWithKey(code.ToString(), dataIntegerArray[CodeInt].ToArray(dataIntegerArray[CodeInt].Length));
						break;
					case VariableDimension.Array2D:
						if (dataIntegerArray2D[CodeInt] != null)
							writer.WriteWithKey(code.ToString(), dataIntegerArray2D[CodeInt]);
						break;
				}
			}
			else if (desc.IsString)
			{
				switch (desc.Dimension)
				{
					case VariableDimension.Scalar:
						writer.WriteWithKey(code.ToString(), dataString[CodeInt]);
						break;
					case VariableDimension.Array1D:
						if (dataStringArray[CodeInt] != null)
							writer.WriteWithKey(code.ToString(), dataStringArray[CodeInt].ToArray(dataStringArray[CodeInt].Length));
						break;
					case VariableDimension.Array2D:
						if (dataStringArray2D[CodeInt] != null)
							writer.WriteWithKey(code.ToString(), dataStringArray2D[CodeInt]);
						break;
				}
			}
			else if (desc.IsFloat)
			{
				switch (desc.Dimension)
				{
					case VariableDimension.Scalar:
						writer.WriteWithKey(code.ToString(), dataFloat[CodeInt]);
						break;
					case VariableDimension.Array1D:
						if (dataFloatArray[CodeInt] != null)
							writer.WriteWithKey(code.ToString(), dataFloatArray[CodeInt].ToArray(dataFloatArray[CodeInt].Length));
						break;
					case VariableDimension.Array2D:
						if (dataFloatArray2D[CodeInt] != null)
							writer.WriteWithKey(code.ToString(), dataFloatArray2D[CodeInt]);
						break;
				}
			}
		}

		//1813追加
		if (UserDefCVarDataList.Count != 0)
		{
			writer.WriteSeparator();
			//#DIM宣言変数の保存
			foreach (UserDefinedCharaVariableToken var in varData.UserDefinedCharaVarList)
			{
				if (!var.IsSavedata || !var.IsCharacterData || var.IsGlobal)
					continue;
				var data = UserDefCVarDataList[var.ArrayIndex];
				if (data is SparseArray<long> sparseLong)
					writer.WriteWithKey(var.Name, sparseLong.ToArray(sparseLong.Length));
				else if (data is SparseArray<string> sparseStr)
					writer.WriteWithKey(var.Name, sparseStr.ToArray(sparseStr.Length));
				else if (data is SparseArray<double> sparseFloat)
					writer.WriteWithKey(var.Name, sparseFloat.ToArray(sparseFloat.Length));
				else
					writer.WriteWithKey(var.Name, data);
			}
		}

		writer.WriteEOC();
	}

	public void LoadFromStreamBinary(EraBinaryDataReader reader)
	{
		int codeInt = 0;
		bool userDefineData = false;
		while (true)
		{
			KeyValuePair<string, EraSaveDataType> nameAndType = reader.ReadVariableCode();
			VariableToken vToken = null;
			object array = null;
			if (nameAndType.Key != null)
			{
				if (!GlobalStatic.IdentifierDictionary.getVarTokenIsForbid(nameAndType.Key))
					vToken = GlobalStatic.IdentifierDictionary.GetVariableToken(nameAndType.Key, null, false);
				if (userDefineData)
				{
					array = vToken == null || !vToken.IsSavedata || !vToken.IsCharacterData || !(vToken is UserDefinedCharaVariableToken token)
						? null
						: UserDefCVarDataList[token.ArrayIndex];
					vToken = null;
				}
				else
				{
					if (vToken != null)
						codeInt = (int)VariableCode.__LOWERCASE__ & (int)vToken.Code;
					array = null;
				}
			}
			switch (nameAndType.Value)
			{
				case EraSaveDataType.Separator:
					userDefineData = true;
					continue;
				case EraSaveDataType.EOF:
				case EraSaveDataType.EOC:
					goto whilebreak;
				case EraSaveDataType.Int:
					if (vToken == null || vToken.GetEraType() != EraType.Integer || vToken.Dimension != 0)
						reader.ReadInt();
					else
						dataInteger[codeInt] = reader.ReadInt();
					break;
				case EraSaveDataType.Str:
					if (vToken == null || vToken.GetEraType() != EraType.String || vToken.Dimension != 0)
						reader.ReadString();
					else
						dataString[codeInt] = reader.ReadString();
					break;
				case EraSaveDataType.IntArray:
					if (userDefineData && array != null)
					{
						var sparseArr = array as SparseArray<long>;
						if (sparseArr != null)
						{
							var tmpArr = new long[sparseArr.Length];
							reader.ReadIntArray(tmpArr, true);
							sparseArr.FromArray(tmpArr);
						}
						else
							reader.ReadIntArray(array as long[], true);
					}
					else if (vToken == null || vToken.GetEraType() != EraType.Integer || vToken.Dimension != 1)
						reader.ReadIntArray(null, true);
					else
					{
						var tmpArr = new long[dataIntegerArray[codeInt].Length];
						reader.ReadIntArray(tmpArr, true);
						dataIntegerArray[codeInt].FromArray(tmpArr);
					}
					break;
				case EraSaveDataType.StrArray:
					if (userDefineData && array != null)
					{
						var sparseArr = array as SparseArray<string>;
						if (sparseArr != null)
						{
							var tmpArr = new string[sparseArr.Length];
							reader.ReadStrArray(tmpArr, true);
							sparseArr.FromArray(tmpArr);
						}
						else
							reader.ReadStrArray(array as string[], true);
					}
					else if (vToken == null || vToken.GetEraType() != EraType.String || vToken.Dimension != 1)
						reader.ReadStrArray(null, true);
					else
					{
						var tmpArr = new string[dataStringArray[codeInt].Length];
						reader.ReadStrArray(tmpArr, true);
						dataStringArray[codeInt].FromArray(tmpArr);
					}
					break;
				case EraSaveDataType.IntArray2D:
					if (userDefineData && array != null)
						reader.ReadIntArray2D(array as long[,], true);
					else if (vToken == null || vToken.GetEraType() != EraType.Integer || vToken.Dimension != 2)
						reader.ReadIntArray2D(null, true);
					else
						reader.ReadIntArray2D(dataIntegerArray2D[codeInt], true);
					break;
				case EraSaveDataType.StrArray2D:
					if (userDefineData && array != null)
						reader.ReadStrArray2D(array as string[,], true);
					else if (vToken == null || vToken.GetEraType() != EraType.String || vToken.Dimension != 2)
						reader.ReadStrArray2D(null, true);
					else
						reader.ReadStrArray2D(dataStringArray2D[codeInt], true);
					break;
				//case EraSaveDataType.IntArray3D:
				//    if (vToken == null || vToken.GetEraType() != EraType.Integer || vToken.Dimension != 3)
				//        reader.ReadIntArray3D(null, true);
				//    else
				//        reader.ReadIntArray3D(dataIntegerArray3D[codeInt], true);
				//    break;
				//case EraSaveDataType.StrArray3D:
				//    if (vToken == null || vToken.GetEraType() != EraType.String || vToken.Dimension != 3)
				//        reader.ReadStrArray3D(null, true);
				//    else
				//        reader.ReadStrArray3D(dataStringArray3D[codeInt], true);
				//    break;
				case EraSaveDataType.Float:
					if (vToken == null || vToken.GetEraType() != EraType.Float || vToken.Dimension != 0)
						reader.ReadDouble();
					else
						dataFloat[codeInt] = reader.ReadDouble();
					break;
				case EraSaveDataType.FloatArray:
					if (userDefineData && array != null)
					{
						var sparseArr = array as SparseArray<double>;
						if (sparseArr != null)
						{
							int saveLen = reader.ReadInt32();
							var tmpArr = new double[sparseArr.Length];
							int copyLen = Math.Min(saveLen, sparseArr.Length);
							for (int i = 0; i < copyLen; i++)
								tmpArr[i] = reader.ReadDouble();
							for (int i = copyLen; i < saveLen; i++)
								reader.ReadDouble();
							sparseArr.FromArray(tmpArr);
						}
						else
						{
							int saveLen = reader.ReadInt32();
							for (int i = 0; i < saveLen; i++) reader.ReadDouble();
						}
					}
					else if (vToken == null || vToken.GetEraType() != EraType.Float || vToken.Dimension != 1)
					{
						int len = reader.ReadInt32();
						for (int i = 0; i < len; i++) reader.ReadDouble();
					}
					else
					{
						int saveLen = reader.ReadInt32();
						var tmpArr = new double[dataFloatArray[codeInt].Length];
						int copyLen = Math.Min(saveLen, tmpArr.Length);
						for (int i = 0; i < copyLen; i++)
							tmpArr[i] = reader.ReadDouble();
						for (int i = copyLen; i < saveLen; i++)
							reader.ReadDouble();
						dataFloatArray[codeInt].FromArray(tmpArr);
					}
					break;
				case EraSaveDataType.FloatArray2D:
					{
						int d0 = reader.ReadInt32(); int d1 = reader.ReadInt32();
						int total = d0 * d1;
						if (userDefineData && array is double[,] arr2D)
						{
							int d0Copy = Math.Min(d0, arr2D.GetLength(0));
							int d1Copy = Math.Min(d1, arr2D.GetLength(1));
							for (int i = 0; i < d0Copy; i++)
								for (int j = 0; j < d1Copy; j++)
									arr2D[i, j] = reader.ReadDouble();
							for (int k = d0Copy * d1Copy; k < total; k++)
								reader.ReadDouble();
						}
						else if (vToken != null && vToken.GetEraType() == EraType.Float && vToken.Dimension == 2)
						{
							var target = dataFloatArray2D[codeInt];
							int d0Copy = Math.Min(d0, target.GetLength(0));
							int d1Copy = Math.Min(d1, target.GetLength(1));
							for (int i = 0; i < d0Copy; i++)
								for (int j = 0; j < d1Copy; j++)
									target[i, j] = reader.ReadDouble();
							for (int k = d0Copy * d1Copy; k < total; k++)
								reader.ReadDouble();
						}
						else
						{
							for (int i = 0; i < total; i++) reader.ReadDouble();
						}
					}
					break;
				case EraSaveDataType.FloatArray3D:
					{
						int d0 = reader.ReadInt32(); int d1 = reader.ReadInt32(); int d2 = reader.ReadInt32();
						int total = d0 * d1 * d2;
						if (userDefineData && array is double[,,] arr3D)
						{
							int d0Copy = Math.Min(d0, arr3D.GetLength(0));
							int d1Copy = Math.Min(d1, arr3D.GetLength(1));
							int d2Copy = Math.Min(d2, arr3D.GetLength(2));
							for (int i = 0; i < d0Copy; i++)
								for (int j = 0; j < d1Copy; j++)
									for (int k = 0; k < d2Copy; k++)
										arr3D[i, j, k] = reader.ReadDouble();
							for (int k = d0Copy * d1Copy * d2Copy; k < total; k++)
								reader.ReadDouble();
						}
						else
						{
							for (int i = 0; i < total; i++) reader.ReadDouble();
						}
					}
					break;
				default:
					throw new FileEE(trerror.AbnormalData.Text);
			}
		}
	whilebreak:
		return;
	}


	private static void copyListToArray<T>(List<T> srcList, T[] destArray)
	{
		int count = Math.Min(srcList.Count, destArray.Length);
		srcList.CopyTo(0, destArray, 0, count);
	}

	private static void copyListToSparseArray<T>(List<T> srcList, SparseArray<T> destArray)
	{
		destArray.Clear();
		for (int i = 0; i < srcList.Count; i++)
			destArray[i] = srcList[i];
	}
	private static void copyListToArray2D<T>(List<T[]> srcList, T[,] destArray)
	{
		int countX = Math.Min(srcList.Count, destArray.GetLength(0));
		int dLength = destArray.GetLength(1);
		for (int x = 0; x < countX; x++)
		{
			T[] srcArray = srcList[x];
			int countY = Math.Min(srcArray.Length, dLength);
			for (int y = 0; y < countY; y++)
			{
				destArray[x, y] = srcArray[y];
			}
		}
	}

	public void setValueAll(int varInt, long value)
	{
		dataInteger[varInt] = value;
	}

	public void setValueAll(int varInt, string value)
	{
		dataString[varInt] = value;
	}

	public void setValueAll(int varInt, double value)
	{
		dataFloat[varInt] = value;
	}

	public void setValueAll1D(int varInt, long value, int start, int end)
	{
		var array = dataIntegerArray[varInt];
		for (int i = start; i < end; i++)
			array[i] = value;
	}

	public void setValueAll1D(int varInt, string value, int start, int end)
	{
		var array = dataStringArray[varInt];
		for (int i = start; i < end; i++)
			array[i] = value;
	}

	public void setValueAll1D(int varInt, double value, int start, int end)
	{
		var array = dataFloatArray[varInt];
		for (int i = start; i < end; i++)
			array[i] = value;
	}

	public void setValueAll2D(int varInt, long value)
	{
		long[,] array = dataIntegerArray2D[varInt];
		int a1 = array.GetLength(0);
		int a2 = array.GetLength(1);
		for (int i = 0; i < a1; i++)
			for (int j = 0; j < a2; j++)
				array[i, j] = value;
	}

	public void setValueAll2D(int varInt, string value)
	{
		string[,] array = dataStringArray2D[varInt];
		int a1 = array.GetLength(0);
		int a2 = array.GetLength(1);
		for (int i = 0; i < a1; i++)
			for (int j = 0; j < a2; j++)
				array[i, j] = value;
	}

	public void setValueAll2D(int varInt, double value)
	{
		double[,] array = dataFloatArray2D[varInt];
		int a1 = array.GetLength(0);
		int a2 = array.GetLength(1);
		for (int i = 0; i < a1; i++)
			for (int j = 0; j < a2; j++)
				array[i, j] = value;
	}

	#region IDisposable メンバ

	public void Dispose()
	{
		for (int i = 0; i < dataIntegerArray.Length; i++)
			dataIntegerArray[i].Clear();
		for (int i = 0; i < dataStringArray.Length; i++)
			dataStringArray[i].Clear();
		for (int i = 0; i < dataFloatArray.Length; i++)
			dataFloatArray[i].Clear();
		for (int i = 0; i < dataIntegerArray2D.Length; i++)
			dataIntegerArray2D[i] = null;
	}

	#endregion
	public SparseArray<long> CFlag
	{
		get { return dataIntegerArray[(int)VariableCode.__LOWERCASE__ & (int)VariableCode.CFLAG]; }
	}
	public long NO
	{
		get { return dataInteger[(int)VariableCode.__LOWERCASE__ & (int)VariableCode.NO]; }
	}

	#region sort
	public IComparable temp_SortKey;
	public int temp_CurrentOrder;
	//Comparison<CharacterData>
	public static int AscCharacterComparison(CharacterData x, CharacterData y)
	{
		int ret = x.temp_SortKey.CompareTo(y.temp_SortKey);
		if (ret != 0)
			return ret;
		return x.temp_CurrentOrder.CompareTo(y.temp_CurrentOrder);
	}
	public static int DescCharacterComparison(CharacterData x, CharacterData y)
	{
		int ret = x.temp_SortKey.CompareTo(y.temp_SortKey);
		if (ret != 0)
			return -ret;
		return x.temp_CurrentOrder.CompareTo(y.temp_CurrentOrder);
	}

	public void SetSortKey(VariableToken sortkey, long elem64)
	{
		//チェック済み
		//if (!sortkey.IsCharacterData)
		//    throw new ExeEE("キャラクタ変数でない");
		var sortkeyEraType = sortkey.GetEraType();
		if (sortkeyEraType == EraType.String)
		{
			if (sortkey.IsArray2D)
			{
				string[,] array = sortkey is UserDefinedCharaVariableToken token
					? (string[,])UserDefCVarDataList[token.ArrayIndex]
					: dataStringArray2D[sortkey.CodeInt];
				int elem1 = (int)(elem64 >> 32);
				int elem2 = (int)(elem64 & 0x7FFFFFFF);
				if (elem1 < 0 || elem1 >= array.GetLength(0) || elem2 < 0 || elem2 >= array.GetLength(1))
					throw new CodeEE(trerror.OoRSortKey.Text);
				temp_SortKey = array[elem1, elem2];
			}
			else if (sortkey.IsArray1D)
			{
				int len;
				string val;
				if (sortkey is UserDefinedCharaVariableToken token)
				{
					string[] array = (string[])UserDefCVarDataList[token.ArrayIndex];
					len = array.Length;
					val = array[(int)elem64];
				}
				else
				{
					var array = dataStringArray[sortkey.CodeInt];
					len = array.Length;
					val = array[(int)elem64];
				}
				if (elem64 < 0 || elem64 >= len)
					throw new CodeEE(trerror.OoRSortKey.Text);
				if (val != null)
					temp_SortKey = val;
				else
					temp_SortKey = "";
			}
			else
			{
				//ユーザー定義キャラ変数は非配列がない
				if (dataString[sortkey.CodeInt] != null)
					temp_SortKey = dataString[sortkey.CodeInt];
				else
					temp_SortKey = "";
			}
		}
		else if (sortkeyEraType == EraType.Integer)
		{
			if (sortkey.IsArray2D)
			{
				long[,] array = sortkey is UserDefinedCharaVariableToken token
					? (long[,])UserDefCVarDataList[token.ArrayIndex]
					: dataIntegerArray2D[sortkey.CodeInt];
				int elem1 = (int)(elem64 >> 32);
				int elem2 = (int)(elem64 & 0x7FFFFFFF);
				if (elem1 < 0 || elem1 >= array.GetLength(0) || elem2 < 0 || elem2 >= array.GetLength(1))
					throw new CodeEE(trerror.OoRSortKey.Text);
				temp_SortKey = array[elem1, elem2];
			}
			else if (sortkey.IsArray1D)
			{
				int len;
				long val;
				if (sortkey is UserDefinedCharaVariableToken token2)
				{
					long[] array = (long[])UserDefCVarDataList[token2.ArrayIndex];
					len = array.Length;
					val = array[(int)elem64];
				}
				else
				{
					var array = dataIntegerArray[sortkey.CodeInt];
					len = array.Length;
					val = array[(int)elem64];
				}
				if (elem64 < 0 || elem64 >= len)
					throw new CodeEE(trerror.OoRSortKey.Text);
				temp_SortKey = val;
			}
			else
			{
				temp_SortKey = dataInteger[sortkey.CodeInt];
			}
		}
	}
	#endregion
}
