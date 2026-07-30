using MinorShift.Emuera.Runtime.Utils;
using System;
using System.Collections.Generic;
using trerror = MinorShift.Emuera.Runtime.Utils.EvilMask.Lang.Error;

namespace MinorShift.Emuera.Runtime.Script.Statements.Variable;

//1756 全ての機能をVariableTokenとManagerに委譲、消滅
//……しようと思ったがConstantDataから参照されているので捨て切れなかった。
/// <summary>
/// VariableCodeのラッパー
/// </summary>
internal sealed class VariableIdentifier
{
	private VariableIdentifier(VariableCode code)
	{
		this.code = code;
		descriptor = VariableDescriptorTable.GetDescriptorByCode(code);
	}
	private VariableIdentifier(VariableCode code, string scope)
	{
		this.code = code;
		this.scope = scope;
		descriptor = VariableDescriptorTable.GetDescriptorByCode(code);
	}
	readonly VariableCode code;
	readonly string scope;
	readonly VariableDescriptor descriptor;
	public VariableCode Code
	{ get { return code; } }
	public string Scope
	{ get { return scope; } }
	public int CodeInt
	{ get { return (int)(code & VariableCode.__LOWERCASE__); } }
	public VariableCode CodeFlag
	{ get { return code & VariableCode.__UPPERCASE__; } }

	public VariableDescriptor Descriptor
	{ get { return descriptor; } }

	public bool IsNull
	{
		get
		{
			return code == VariableCode.__NULL__;
		}
	}
	public bool IsCharacterData
	{
		get
		{
			return descriptor.Attributes.HasFlag(VariableAttribute.CharacterData);
		}
	}
	public bool IsInteger
	{
		get
		{
			return descriptor.Kind == VariableKind.Integer;
		}
	}
	public bool IsString
	{
		get
		{
			return descriptor.Kind == VariableKind.String;
		}
	}
	public bool IsFloat
	{
		get
		{
			return descriptor.Kind == VariableKind.Float;
		}
	}
	public bool IsArray1D
	{
		get
		{
			return descriptor.Dimension == VariableDimension.Array1D;
		}
	}
	public bool IsArray2D
	{
		get
		{
			return descriptor.Dimension == VariableDimension.Array2D;
		}
	}
	public bool IsArray3D
	{
		get
		{
			return descriptor.Dimension == VariableDimension.Array3D;
		}
	}
	public bool Readonly
	{
		get
		{
			return (code & VariableCode.__UNCHANGEABLE__) == VariableCode.__UNCHANGEABLE__;
		}
	}
	public bool IsCalc
	{
		get
		{
			return descriptor.Attributes.HasFlag(VariableAttribute.Calc);
		}
	}
	public bool IsLocal
	{
		get
		{
			return descriptor.Attributes.HasFlag(VariableAttribute.Local);
		}
	}
	public bool CanForbid
	{
		get
		{
			return descriptor.Attributes.HasFlag(VariableAttribute.CanForbid);
		}
	}

	public EraType GetEraType()
	{
		return descriptor.Kind switch
		{
			VariableKind.Integer => EraType.Integer,
			VariableKind.String => EraType.String,
			VariableKind.Float => EraType.Float,
			_ => EraType.Integer
		};
	}

	readonly static Dictionary<string, VariableCode> nameDic = [];
	readonly static Dictionary<string, VariableCode> localvarNameDic = [];
	readonly static Dictionary<(VariableKind, VariableDimension, bool), List<VariableCode>> extSaveListDic = [];

	static VariableIdentifier()
	{
		var array = Enum.GetValues<VariableCode>();

		nameDic.Add(Enum.GetName(VariableCode.__FILE__), VariableCode.__FILE__);
		nameDic.Add(Enum.GetName(VariableCode.__LINE__), VariableCode.__LINE__);
		nameDic.Add(Enum.GetName(VariableCode.__FUNCTION__), VariableCode.__FUNCTION__);
		foreach (var code in array)
		{
			var key = Enum.GetName(code);
			if (key == null || key.StartsWith("__") && key.EndsWith("__"))
				continue;
			if (nameDic.ContainsKey(key))
				continue;
#if DEBUG
			if ((code & VariableCode.__ARRAY_2D__) == VariableCode.__ARRAY_2D__)
			{
				if ((code & VariableCode.__ARRAY_1D__) == VariableCode.__ARRAY_1D__)
					throw new ExeEE("ARRAY2DとARRAY1Dは排他");
			}
			{
				var desc = VariableDescriptor.FromCode(code, key);
				if ((desc.Kind & (VariableKind.Integer | VariableKind.String | VariableKind.Float)) == 0)
					throw new ExeEE("INTEGER, STRING, FLOATのどれかは必須");
				if (desc.Kind != VariableKind.Integer && desc.Kind != VariableKind.String && desc.Kind != VariableKind.Float)
					throw new ExeEE("KindはInteger, String, Floatのいずれか一つでなければならない");
			}
			if ((code & VariableCode.__EXTENDED__) != VariableCode.__EXTENDED__)
			{
				if ((code & VariableCode.__SAVE_EXTENDED__) == VariableCode.__SAVE_EXTENDED__)
					throw new ExeEE("SAVE_EXTENDEDにはEXTENDEDフラグ必須");
				if ((code & VariableCode.__LOCAL__) == VariableCode.__LOCAL__)
					throw new ExeEE("LOCALにはEXTENDEDフラグ必須");
				if ((code & VariableCode.__GLOBAL__) == VariableCode.__GLOBAL__)
					throw new ExeEE("GLOBALにはEXTENDEDフラグ必須");
				if ((code & VariableCode.__ARRAY_2D__) == VariableCode.__ARRAY_2D__)
					throw new ExeEE("ARRAY2DにはEXTENDEDフラグ必須");
			}
			if (((code & VariableCode.__SAVE_EXTENDED__) == VariableCode.__SAVE_EXTENDED__)
				&& ((code & VariableCode.__UNCHANGEABLE__) == VariableCode.__UNCHANGEABLE__))
				throw new ExeEE("CALCとSAVE_EXTENDEDは排他");
			if (((code & VariableCode.__SAVE_EXTENDED__) == VariableCode.__SAVE_EXTENDED__)
				&& ((code & VariableCode.__CALC__) == VariableCode.__CALC__))
				throw new ExeEE("UNCHANGEABLEとSAVE_EXTENDEDは排他");
			if (((code & VariableCode.__SAVE_EXTENDED__) == VariableCode.__SAVE_EXTENDED__)
				&& ((code & VariableCode.__ARRAY_2D__) == VariableCode.__ARRAY_2D__)
				&& ((code & VariableCode.__STRING__) == VariableCode.__STRING__))
				throw new ExeEE("STRINGかつARRAY2DのSAVE_EXTENDEDは未実装");
#endif
			nameDic.Add(key, code);

			if ((code & VariableCode.__LOCAL__) == VariableCode.__LOCAL__)
				localvarNameDic.Add(key, code);
			if ((code & VariableCode.__SAVE_EXTENDED__) == VariableCode.__SAVE_EXTENDED__)
			{
				var desc = VariableDescriptor.FromCode(code, key);
				var dicKey = (desc.Kind, desc.Dimension, desc.Attributes.HasFlag(VariableAttribute.CharacterData));
				if (!extSaveListDic.ContainsKey(dicKey))
					extSaveListDic.Add(dicKey, []);
				extSaveListDic[dicKey].Add(code);
			}
		}
	}

	public static List<VariableCode> GetExtSaveList(VariableCode flag)
	{
		var desc = VariableDescriptor.FromCode(flag, "");
		var dicKey = (desc.Kind, desc.Dimension, desc.Attributes.HasFlag(VariableAttribute.CharacterData));
		if (!extSaveListDic.TryGetValue(dicKey, out List<VariableCode> value))
			return [];
		return value;
	}

	/// <summary>
	/// 拡張セーブ対象変数の一覧。
	/// キャラクタ変数と非キャラクタ変数は格納先の配列が別なので、
	/// isCharacterDataで区別しなければ添字が食い違いセーブが壊れる。
	/// </summary>
	public static List<VariableCode> GetExtSaveList(VariableKind kind, VariableDimension dim, bool isCharacterData)
	{
		var dicKey = (kind, dim, isCharacterData);
		if (!extSaveListDic.TryGetValue(dicKey, out List<VariableCode> value))
			return [];
		return value;
	}

	public static VariableIdentifier GetVariableId(VariableCode code)
	{
		return new VariableIdentifier(code);
	}

	public static VariableIdentifier GetVariableId(string key)
	{
		return GetVariableId(key, null);
	}
	public static VariableIdentifier GetVariableId(string key, string subStr)
	{
		VariableCode ret;
		if (string.IsNullOrEmpty(key))
			return null;
		if (subStr != null)
		{
			if (localvarNameDic.TryGetValue(key, out ret))
				return new VariableIdentifier(ret, subStr);
			if (nameDic.ContainsKey(key))
				throw new CodeEE(string.Format(trerror.UsedAtForGlobalVar.Text, key));
			throw new CodeEE(trerror.InvalidAt.Text);
		}
		if (nameDic.TryGetValue(key, out ret))
			return new VariableIdentifier(ret);
		else
			return null;
	}
	public override string ToString()
	{
		return code.ToString();
	}
}
