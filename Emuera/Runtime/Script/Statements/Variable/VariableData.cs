using MinorShift.Emuera.Runtime.Config;
using MinorShift.Emuera.Runtime.Script;
using MinorShift.Emuera.Runtime.Script.Data;
using MinorShift.Emuera.Runtime.Script.Statements.Variable;
using MinorShift.Emuera.Runtime.Utils;
using System;
using System.Collections.Generic;
using System.Data;
using System.Xml;
using trerror = MinorShift.Emuera.Runtime.Utils.EvilMask.Lang.Error;

namespace MinorShift.Emuera.GameData.Variable;

/// <summary>
/// 変数全部
/// </summary>
internal sealed partial class VariableData : IDisposable
{
	#region EM_私家版_XMLDocument_連想配列
	public Dictionary<string, XmlDocument> DataXmlDocument { get; set; } = [];
	public Dictionary<string, Dictionary<string, string>> DataStringMaps { get; set; } = [];
	public Dictionary<string, DataTable> DataDataTables { get; set; } = [];
	#endregion
	readonly long[] dataInteger;
	readonly string[] dataString;
	readonly double[] dataFloat;
	readonly SparseArray<long>[] dataIntegerArray;
	readonly SparseArray<string>[] dataStringArray;
	readonly double[][] dataFloatArray;
	readonly long[][,] dataIntegerArray2D;
	readonly string[][,] dataStringArray2D;
	readonly double[][,] dataFloatArray2D;
	readonly long[][,,] dataIntegerArray3D;
	readonly string[][,,] dataStringArray3D;
	readonly double[][,,] dataFloatArray3D;
	//readonly VariableLocal<Int64, Int64Calculator> localVars;
	//readonly VariableLocal<string, StringCalculator> localString;
	//readonly VariableLocal<Int64, Int64Calculator> argVars;
	//readonly VariableLocal<string, StringCalculator> argString;
	readonly List<CharacterData> characterList;
	public long[] DataInteger { get { return dataInteger; } }
	public string[] DataString { get { return dataString; } }
	public double[] DataFloat { get { return dataFloat; } }
	public SparseArray<long>[] DataIntegerArray { get { return dataIntegerArray; } }
	public SparseArray<string>[] DataStringArray { get { return dataStringArray; } }
	public double[][] DataFloatArray { get { return dataFloatArray; } }
	public long[][,] DataIntegerArray2D { get { return dataIntegerArray2D; } }
	public string[][,] DataStringArray2D { get { return dataStringArray2D; } }
	public double[][,] DataFloatArray2D { get { return dataFloatArray2D; } }
	public long[][,,] DataIntegerArray3D { get { return dataIntegerArray3D; } }
	public string[][,,] DataStringArray3D { get { return dataStringArray3D; } }
	public double[][,,] DataFloatArray3D { get { return dataFloatArray3D; } }
	//public VariableLocal<Int64, Int64Calculator> LocalVars { get { return localVars; } }
	//public VariableLocal<string, StringCalculator> LocalString { get { return localString; } }
	//public VariableLocal<Int64, Int64Calculator> ArgVars { get { return argVars; } }
	//public VariableLocal<string, StringCalculator> ArgString { get { return argString; } }
	public List<CharacterData> CharacterList { get { return characterList; } }
	readonly GameBase gamebase;
	readonly ConstantData constant;
	internal GameBase GameBase { get { return gamebase; } }
	internal ConstantData Constant { get { return constant; } }

	public long LastLoadVersion = -1;
	public long LastLoadNo = -1;
	public string LastLoadText = "";

	readonly Dictionary<string, VariableToken> varTokenDic = new(Config.StrComper);
	readonly Dictionary<string, VariableLocal> localvarTokenDic = new(Config.StrComper);

	/// <summary>
	/// ユーザー変数のうちStaticかつ非Globalなもの。ERHでのDIM(非GLOBAL) と関数でのDIM (STATIC)の両方。ロードやリセットで初期化が必要。キャラクタ変数は除く。
	/// </summary>
	List<UserDefinedVariableToken> userDefinedStaticVarList = [];
	/// <summary>
	/// ユーザー広域変数のうちグローバル属性持ち。
	/// </summary>
	List<UserDefinedVariableToken> userDefinedGlobalVarList = [];
	/// <summary>
	/// ユーザー広域変数のうちセーブされるもの。グローバル、キャラクタ変数は除く。
	/// </summary>
	List<UserDefinedVariableToken>[] userDefinedSaveVarList = new List<UserDefinedVariableToken>[9];
	/// <summary>
	/// ユーザー広域変数のうち、グローバルかつセーブされるもの。
	/// </summary>
	List<UserDefinedVariableToken>[] userDefinedGlobalSaveVarList = new List<UserDefinedVariableToken>[9];
	/// <summary>
	/// ユーザー広域変数のうち、キャラクタ変数であるもの。初期化やセーブされるかどうかはCharacterDataの方で判断。
	/// </summary>
	public List<UserDefinedCharaVariableToken> UserDefinedCharaVarList = [];

	public VariableData(GameBase gamebase, ConstantData constant)
	{
		this.gamebase = gamebase;
		this.constant = constant;
		characterList = [];
		//localVars = new VariableLocal<Int64, Int64Calculator>(constant.VariableIntArrayLength[(int)(VariableCode.__LOWERCASE__ & VariableCode.LOCAL)]);
		//localString = new VariableLocal<string, StringCalculator>(constant.VariableStrArrayLength[(int)(VariableCode.__LOWERCASE__ & VariableCode.LOCALS)]);
		//argVars = new VariableLocal<Int64, Int64Calculator>(constant.VariableIntArrayLength[(int)(VariableCode.__LOWERCASE__ & VariableCode.ARG)]);
		//argString = new VariableLocal<string, StringCalculator>(constant.VariableStrArrayLength[(int)(VariableCode.__LOWERCASE__ & VariableCode.ARGS)]);
		dataInteger = [];

		dataIntegerArray = new SparseArray<long>[(int)VariableCode.__COUNT_INTEGER_ARRAY__];
		for (int i = 0; i < dataIntegerArray.Length; i++)
		{
			dataIntegerArray[i] = new SparseArray<long>();
			dataIntegerArray[i].Length = constant.VariableIntArrayLength[i];
		}

		dataString = new string[(int)VariableCode.__COUNT_STRING__];

		dataStringArray = new SparseArray<string>[(int)VariableCode.__COUNT_STRING_ARRAY__];

		for (int i = 0; i < dataStringArray.Length; i++)
		{
			dataStringArray[i] = new SparseArray<string>();
			dataStringArray[i].Length = constant.VariableStrArrayLength[i];
		}


		dataIntegerArray2D = new long[(int)VariableCode.__COUNT_INTEGER_ARRAY_2D__][,];
		for (int i = 0; i < dataIntegerArray2D.Length; i++)
		{
			long length64 = constant.VariableIntArray2DLength[i];
			int length = (int)(length64 >> 32);
			int length2 = (int)(length64 & 0x7FFFFFFF);
			dataIntegerArray2D[i] = new long[length, length2];
		}
		dataStringArray2D = [];
		for (int i = 0; i < dataStringArray2D.Length; i++)
		{
			long length64 = constant.VariableStrArray2DLength[i];
			int length = (int)(length64 >> 32);
			int length2 = (int)(length64 & 0x7FFFFFFF);
			dataStringArray2D[i] = new string[length, length2];
		}
		dataIntegerArray3D = new long[(int)VariableCode.__COUNT_INTEGER_ARRAY_3D__][,,];
		for (int i = 0; i < dataIntegerArray3D.Length; i++)
		{
			long length64 = constant.VariableIntArray3DLength[i];
			int length = (int)(length64 >> 40);
			int length2 = (int)((length64 >> 20) & 0xFFFFF);
			int length3 = (int)(length64 & 0xFFFFF);
			dataIntegerArray3D[i] = new long[length, length2, length3];
		}
		dataStringArray3D = [];
		for (int i = 0; i < dataStringArray3D.Length; i++)
		{
			long length64 = constant.VariableStrArray3DLength[i];
			int length = (int)(length64 >> 40);
			int length2 = (int)((length64 >> 20) & 0xFFFFF);
			int length3 = (int)(length64 & 0xFFFFF);
			dataStringArray3D[i] = new string[length, length2, length3];
		}

		dataFloat = new double[(int)VariableCode.__COUNT_STRING__];
		dataFloatArray = new double[(int)VariableCode.__COUNT_STRING_ARRAY__][];
		for (int i = 0; i < dataFloatArray.Length; i++)
			dataFloatArray[i] = [];
		dataFloatArray2D = Array.Empty<double[,]>();
		dataFloatArray3D = Array.Empty<double[,,]>();
		for (int i = 0; i < 9; i++)
		{
			userDefinedSaveVarList[i] = [];
			userDefinedGlobalSaveVarList[i] = [];
		}



		SetDefaultValue(constant);

		varTokenDic.Add("DAY", new Int1DVariableToken(VariableCode.DAY, this));
		varTokenDic.Add("MONEY", new Int1DVariableToken(VariableCode.MONEY, this));
		varTokenDic.Add("ITEM", new Int1DVariableToken(VariableCode.ITEM, this));
		varTokenDic.Add("FLAG", new Int1DVariableToken(VariableCode.FLAG, this));
		varTokenDic.Add("TFLAG", new Int1DVariableToken(VariableCode.TFLAG, this));
		varTokenDic.Add("UP", new Int1DVariableToken(VariableCode.UP, this));
		varTokenDic.Add("PALAMLV", new Int1DVariableToken(VariableCode.PALAMLV, this));
		varTokenDic.Add("EXPLV", new Int1DVariableToken(VariableCode.EXPLV, this));
		varTokenDic.Add("EJAC", new Int1DVariableToken(VariableCode.EJAC, this));
		varTokenDic.Add("DOWN", new Int1DVariableToken(VariableCode.DOWN, this));
		varTokenDic.Add("RESULT", new Int1DVariableToken(VariableCode.RESULT, this));
		varTokenDic.Add("RESULTF", new FloatScalarVariableToken(VariableCode.RESULTF, this));
		varTokenDic.Add("COUNT", new Int1DVariableToken(VariableCode.COUNT, this));
		varTokenDic.Add("TARGET", new Int1DVariableToken(VariableCode.TARGET, this));
		varTokenDic.Add("ASSI", new Int1DVariableToken(VariableCode.ASSI, this));
		varTokenDic.Add("MASTER", new Int1DVariableToken(VariableCode.MASTER, this));
		varTokenDic.Add("NOITEM", new Int1DVariableToken(VariableCode.NOITEM, this));
		varTokenDic.Add("LOSEBASE", new Int1DVariableToken(VariableCode.LOSEBASE, this));
		varTokenDic.Add("SELECTCOM", new Int1DVariableToken(VariableCode.SELECTCOM, this));
		varTokenDic.Add("ASSIPLAY", new Int1DVariableToken(VariableCode.ASSIPLAY, this));
		varTokenDic.Add("PREVCOM", new Int1DVariableToken(VariableCode.PREVCOM, this));
		varTokenDic.Add("TIME", new Int1DVariableToken(VariableCode.TIME, this));
		varTokenDic.Add("ITEMSALES", new Int1DVariableToken(VariableCode.ITEMSALES, this));
		varTokenDic.Add("PLAYER", new Int1DVariableToken(VariableCode.PLAYER, this));
		varTokenDic.Add("NEXTCOM", new Int1DVariableToken(VariableCode.NEXTCOM, this));
		varTokenDic.Add("PBAND", new Int1DVariableToken(VariableCode.PBAND, this));
		varTokenDic.Add("BOUGHT", new Int1DVariableToken(VariableCode.BOUGHT, this));
		varTokenDic.Add("A", new Int1DVariableToken(VariableCode.A, this));
		varTokenDic.Add("B", new Int1DVariableToken(VariableCode.B, this));
		varTokenDic.Add("C", new Int1DVariableToken(VariableCode.C, this));
		varTokenDic.Add("D", new Int1DVariableToken(VariableCode.D, this));
		varTokenDic.Add("E", new Int1DVariableToken(VariableCode.E, this));
		varTokenDic.Add("F", new Int1DVariableToken(VariableCode.F, this));
		varTokenDic.Add("G", new Int1DVariableToken(VariableCode.G, this));
		varTokenDic.Add("H", new Int1DVariableToken(VariableCode.H, this));
		varTokenDic.Add("I", new Int1DVariableToken(VariableCode.I, this));
		varTokenDic.Add("J", new Int1DVariableToken(VariableCode.J, this));
		varTokenDic.Add("K", new Int1DVariableToken(VariableCode.K, this));
		varTokenDic.Add("L", new Int1DVariableToken(VariableCode.L, this));
		varTokenDic.Add("M", new Int1DVariableToken(VariableCode.M, this));
		varTokenDic.Add("N", new Int1DVariableToken(VariableCode.N, this));
		varTokenDic.Add("O", new Int1DVariableToken(VariableCode.O, this));
		varTokenDic.Add("P", new Int1DVariableToken(VariableCode.P, this));
		varTokenDic.Add("Q", new Int1DVariableToken(VariableCode.Q, this));
		varTokenDic.Add("R", new Int1DVariableToken(VariableCode.R, this));
		varTokenDic.Add("S", new Int1DVariableToken(VariableCode.S, this));
		varTokenDic.Add("T", new Int1DVariableToken(VariableCode.T, this));
		varTokenDic.Add("U", new Int1DVariableToken(VariableCode.U, this));
		varTokenDic.Add("V", new Int1DVariableToken(VariableCode.V, this));
		varTokenDic.Add("W", new Int1DVariableToken(VariableCode.W, this));
		varTokenDic.Add("X", new Int1DVariableToken(VariableCode.X, this));
		varTokenDic.Add("Y", new Int1DVariableToken(VariableCode.Y, this));
		varTokenDic.Add("Z", new Int1DVariableToken(VariableCode.Z, this));

		varTokenDic.Add("GLOBAL", new Int1DVariableToken(VariableCode.GLOBAL, this));
		varTokenDic.Add("RANDDATA", new Int1DVariableToken(VariableCode.RANDDATA, this));

		varTokenDic.Add("SAVESTR", new Str1DVariableToken(VariableCode.SAVESTR, this));
		varTokenDic.Add("TSTR", new Str1DVariableToken(VariableCode.TSTR, this));
		varTokenDic.Add("STR", new Str1DVariableToken(VariableCode.STR, this));
		varTokenDic.Add("RESULTS", new Str1DVariableToken(VariableCode.RESULTS, this));
		varTokenDic.Add("GLOBALS", new Str1DVariableToken(VariableCode.GLOBALS, this));

		varTokenDic.Add("SAVEDATA_TEXT", new StrVariableToken(VariableCode.SAVEDATA_TEXT, this));

		varTokenDic.Add("ISASSI", new CharaIntVariableToken(VariableCode.ISASSI, this));
		varTokenDic.Add("NO", new CharaIntVariableToken(VariableCode.NO, this));

		varTokenDic.Add("BASE", new CharaInt1DVariableToken(VariableCode.BASE, this));
		varTokenDic.Add("MAXBASE", new CharaInt1DVariableToken(VariableCode.MAXBASE, this));
		varTokenDic.Add("ABL", new CharaInt1DVariableToken(VariableCode.ABL, this));
		varTokenDic.Add("TALENT", new CharaInt1DVariableToken(VariableCode.TALENT, this));
		varTokenDic.Add("EXP", new CharaInt1DVariableToken(VariableCode.EXP, this));
		varTokenDic.Add("MARK", new CharaInt1DVariableToken(VariableCode.MARK, this));
		varTokenDic.Add("PALAM", new CharaInt1DVariableToken(VariableCode.PALAM, this));
		varTokenDic.Add("SOURCE", new CharaInt1DVariableToken(VariableCode.SOURCE, this));
		varTokenDic.Add("EX", new CharaInt1DVariableToken(VariableCode.EX, this));
		varTokenDic.Add("CFLAG", new CharaInt1DVariableToken(VariableCode.CFLAG, this));
		varTokenDic.Add("JUEL", new CharaInt1DVariableToken(VariableCode.JUEL, this));
		varTokenDic.Add("RELATION", new CharaInt1DVariableToken(VariableCode.RELATION, this));
		varTokenDic.Add("EQUIP", new CharaInt1DVariableToken(VariableCode.EQUIP, this));
		varTokenDic.Add("TEQUIP", new CharaInt1DVariableToken(VariableCode.TEQUIP, this));
		varTokenDic.Add("STAIN", new CharaInt1DVariableToken(VariableCode.STAIN, this));
		varTokenDic.Add("GOTJUEL", new CharaInt1DVariableToken(VariableCode.GOTJUEL, this));
		varTokenDic.Add("NOWEX", new CharaInt1DVariableToken(VariableCode.NOWEX, this));
		varTokenDic.Add("DOWNBASE", new CharaInt1DVariableToken(VariableCode.DOWNBASE, this));
		varTokenDic.Add("CUP", new CharaInt1DVariableToken(VariableCode.CUP, this));
		varTokenDic.Add("CDOWN", new CharaInt1DVariableToken(VariableCode.CDOWN, this));
		varTokenDic.Add("TCVAR", new CharaInt1DVariableToken(VariableCode.TCVAR, this));

		varTokenDic.Add("NAME", new CharaStrVariableToken(VariableCode.NAME, this));
		varTokenDic.Add("CALLNAME", new CharaStrVariableToken(VariableCode.CALLNAME, this));
		varTokenDic.Add("NICKNAME", new CharaStrVariableToken(VariableCode.NICKNAME, this));
		varTokenDic.Add("MASTERNAME", new CharaStrVariableToken(VariableCode.MASTERNAME, this));

		varTokenDic.Add("CSTR", new CharaStr1DVariableToken(VariableCode.CSTR, this));

		varTokenDic.Add("CDFLAG", new CharaInt2DVariableToken(VariableCode.CDFLAG, this));

		varTokenDic.Add("DITEMTYPE", new Int2DVariableToken(VariableCode.DITEMTYPE, this));
		varTokenDic.Add("DA", new Int2DVariableToken(VariableCode.DA, this));
		varTokenDic.Add("DB", new Int2DVariableToken(VariableCode.DB, this));
		varTokenDic.Add("DC", new Int2DVariableToken(VariableCode.DC, this));
		varTokenDic.Add("DD", new Int2DVariableToken(VariableCode.DD, this));
		varTokenDic.Add("DE", new Int2DVariableToken(VariableCode.DE, this));

		varTokenDic.Add("TA", new Int3DVariableToken(VariableCode.TA, this));
		varTokenDic.Add("TB", new Int3DVariableToken(VariableCode.TB, this));


		varTokenDic.Add("ITEMPRICE", new Int1DConstantToken(VariableCode.ITEMPRICE, this, constant.ItemPrice));
		varTokenDic.Add("ABLNAME", new Str1DConstantToken(VariableCode.ABLNAME, this));
		varTokenDic.Add("TALENTNAME", new Str1DConstantToken(VariableCode.TALENTNAME, this));
		varTokenDic.Add("EXPNAME", new Str1DConstantToken(VariableCode.EXPNAME, this));
		varTokenDic.Add("MARKNAME", new Str1DConstantToken(VariableCode.MARKNAME, this));
		varTokenDic.Add("PALAMNAME", new Str1DConstantToken(VariableCode.PALAMNAME, this));
		varTokenDic.Add("ITEMNAME", new Str1DConstantToken(VariableCode.ITEMNAME, this));
		varTokenDic.Add("TRAINNAME", new Str1DConstantToken(VariableCode.TRAINNAME, this));
		varTokenDic.Add("BASENAME", new Str1DConstantToken(VariableCode.BASENAME, this));
		varTokenDic.Add("SOURCENAME", new Str1DConstantToken(VariableCode.SOURCENAME, this));
		varTokenDic.Add("EXNAME", new Str1DConstantToken(VariableCode.EXNAME, this));
		varTokenDic.Add("EQUIPNAME", new Str1DConstantToken(VariableCode.EQUIPNAME, this));
		varTokenDic.Add("TEQUIPNAME", new Str1DConstantToken(VariableCode.TEQUIPNAME, this));
		varTokenDic.Add("FLAGNAME", new Str1DConstantToken(VariableCode.FLAGNAME, this));
		varTokenDic.Add("TFLAGNAME", new Str1DConstantToken(VariableCode.TFLAGNAME, this));
		varTokenDic.Add("CFLAGNAME", new Str1DConstantToken(VariableCode.CFLAGNAME, this));
		varTokenDic.Add("TCVARNAME", new Str1DConstantToken(VariableCode.TCVARNAME, this));
		varTokenDic.Add("CSTRNAME", new Str1DConstantToken(VariableCode.CSTRNAME, this));
		varTokenDic.Add("STAINNAME", new Str1DConstantToken(VariableCode.STAINNAME, this));

		varTokenDic.Add("CDFLAGNAME1", new Str1DConstantToken(VariableCode.CDFLAGNAME1, this));
		varTokenDic.Add("CDFLAGNAME2", new Str1DConstantToken(VariableCode.CDFLAGNAME2, this));
		varTokenDic.Add("STRNAME", new Str1DConstantToken(VariableCode.STRNAME, this));
		varTokenDic.Add("TSTRNAME", new Str1DConstantToken(VariableCode.TSTRNAME, this));
		varTokenDic.Add("SAVESTRNAME", new Str1DConstantToken(VariableCode.SAVESTRNAME, this));
		varTokenDic.Add("GLOBALNAME", new Str1DConstantToken(VariableCode.GLOBALNAME, this));
		varTokenDic.Add("GLOBALSNAME", new Str1DConstantToken(VariableCode.GLOBALSNAME, this));

		#region EE_CSV機能拡張
		varTokenDic.Add("DAYNAME", new Str1DConstantToken(VariableCode.DAYNAME, this));
		varTokenDic.Add("TIMENAME", new Str1DConstantToken(VariableCode.TIMENAME, this));
		varTokenDic.Add("MONEYNAME", new Str1DConstantToken(VariableCode.MONEYNAME, this));
		#endregion

		StrConstantToken token = new(VariableCode.GAMEBASE_AUTHOR, this, gamebase.ScriptAutherName);
		varTokenDic.Add("GAMEBASE_AUTHER", token);
		varTokenDic.Add("GAMEBASE_AUTHOR", token);
		varTokenDic.Add("GAMEBASE_INFO", new StrConstantToken(VariableCode.GAMEBASE_INFO, this, gamebase.ScriptDetail));
		varTokenDic.Add("GAMEBASE_YEAR", new StrConstantToken(VariableCode.GAMEBASE_YEAR, this, gamebase.ScriptYear));
		varTokenDic.Add("GAMEBASE_TITLE", new StrConstantToken(VariableCode.GAMEBASE_TITLE, this, gamebase.ScriptTitle));
		#region EE_UPDATECHECK
		varTokenDic.Add("GAMEBASE_URL", new StrConstantToken(VariableCode.GAMEBASE_URL, this, gamebase.UpdateCheckURL));
		varTokenDic.Add("GAMEBASE_VERSIONNAME", new StrConstantToken(VariableCode.GAMEBASE_URL, this, gamebase.VersionName));
		#endregion


		varTokenDic.Add("GAMEBASE_GAMECODE", new IntConstantToken(VariableCode.GAMEBASE_GAMECODE, this, gamebase.ScriptUniqueCode));
		varTokenDic.Add("GAMEBASE_VERSION", new IntConstantToken(VariableCode.GAMEBASE_VERSION, this, gamebase.ScriptVersion));
		varTokenDic.Add("GAMEBASE_ALLOWVERSION", new IntConstantToken(VariableCode.GAMEBASE_ALLOWVERSION, this, gamebase.ScriptCompatibleMinVersion));
		varTokenDic.Add("GAMEBASE_DEFAULTCHARA", new IntConstantToken(VariableCode.GAMEBASE_DEFAULTCHARA, this, gamebase.DefaultCharacter));
		varTokenDic.Add("GAMEBASE_NOITEM", new IntConstantToken(VariableCode.GAMEBASE_NOITEM, this, gamebase.DefaultNoItem));

		VariableToken rand = null;
		if (Config.CompatiRAND)
			rand = new CompatiRandToken(VariableCode.RAND, this);
		else
			rand = new RandToken(VariableCode.RAND, this);
		varTokenDic.Add("RAND", rand);
		varTokenDic.Add("CHARANUM", new CHARANUM_Token(VariableCode.CHARANUM, this));


		varTokenDic.Add("LASTLOAD_TEXT", new LASTLOAD_TEXT_Token(VariableCode.LASTLOAD_TEXT, this));
		varTokenDic.Add("LASTLOAD_VERSION", new LASTLOAD_VERSION_Token(VariableCode.LASTLOAD_VERSION, this));
		varTokenDic.Add("LASTLOAD_NO", new LASTLOAD_NO_Token(VariableCode.LASTLOAD_NO, this));
		varTokenDic.Add("LINECOUNT", new LINECOUNT_Token(VariableCode.LINECOUNT, this));
		varTokenDic.Add("ISTIMEOUT", new ISTIMEOUTToken(VariableCode.ISTIMEOUT, this));
		varTokenDic.Add("__INT_MAX__", new __INT_MAX__Token(VariableCode.__INT_MAX__, this));
		varTokenDic.Add("__INT_MIN__", new __INT_MIN__Token(VariableCode.__INT_MIN__, this));
		varTokenDic.Add("EMUERA_VERSION", new EMUERA_VERSIONToken(VariableCode.EMUERA_VERSION, this));

		varTokenDic.Add("WINDOW_TITLE", new WINDOW_TITLE_Token(VariableCode.WINDOW_TITLE, this));
		varTokenDic.Add("MONEYLABEL", new MONEYLABEL_Token(VariableCode.MONEYLABEL, this));
		varTokenDic.Add("DRAWLINESTR", new DRAWLINESTR_Token(VariableCode.DRAWLINESTR, this));
		if (!Program.DebugMode)
		{
			varTokenDic.Add("__FILE__", new EmptyStrToken(VariableCode.__FILE__, this));
			varTokenDic.Add("__FUNCTION__", new EmptyStrToken(VariableCode.__FUNCTION__, this));
			varTokenDic.Add("__LINE__", new EmptyIntToken(VariableCode.__LINE__, this));
		}
		else
		{
			varTokenDic.Add("__FILE__", new Debug__FILE__Token(VariableCode.__FILE__, this));
			varTokenDic.Add("__FUNCTION__", new Debug__FUNCTION__Token(VariableCode.__FUNCTION__, this));
			varTokenDic.Add("__LINE__", new Debug__LINE__Token(VariableCode.__LINE__, this));
		}

		int size = constant.VariableIntArrayLength[(int)(VariableCode.__LOWERCASE__ & VariableCode.LOCAL)];
		localvarTokenDic.Add("LOCAL", new VariableLocal(VariableCode.LOCAL, size, CreateLocalInt));
		size = constant.VariableIntArrayLength[(int)(VariableCode.__LOWERCASE__ & VariableCode.ARG)];
		localvarTokenDic.Add("ARG", new VariableLocal(VariableCode.ARG, size, CreateLocalInt));
		size = constant.VariableStrArrayLength[(int)(VariableCode.__LOWERCASE__ & VariableCode.LOCALS)];
		localvarTokenDic.Add("LOCALS", new VariableLocal(VariableCode.LOCALS, size, CreateLocalStr));
		size = constant.VariableStrArrayLength[(int)(VariableCode.__LOWERCASE__ & VariableCode.ARGS)];
		localvarTokenDic.Add("ARGS", new VariableLocal(VariableCode.ARGS, size, CreateLocalStr));

		size = constant.VariableFloatArrayLength[(int)(VariableCode.__LOWERCASE__ & VariableCode.LOCALF)];
		localvarTokenDic.Add("LOCALF", new VariableLocal(VariableCode.LOCALF, size, CreateLocalFloat));
		size = constant.VariableFloatArrayLength[(int)(VariableCode.__LOWERCASE__ & VariableCode.ARGF)];
		localvarTokenDic.Add("ARGF", new VariableLocal(VariableCode.ARGF, size, CreateLocalFloat));

	}

	private LocalInt1DVariableToken CreateLocalInt(VariableCode varCode, string subKey, int size)
	{
		return new LocalInt1DVariableToken(varCode, this, subKey, size);
	}
	private LocalStr1DVariableToken CreateLocalStr(VariableCode varCode, string subKey, int size)
	{
		return new LocalStr1DVariableToken(varCode, this, subKey, size);
	}
	private LocalFloat1DVariableToken CreateLocalFloat(VariableCode varCode, string subKey, int size)
	{
		return new LocalFloat1DVariableToken(varCode, this, subKey, size);
	}
	public Dictionary<string, VariableToken> GetVarTokenDicClone()
	{
		return new(varTokenDic, Config.StrComper);
	}
	public Dictionary<string, VariableToken> GetVarTokenDic()
	{
		return varTokenDic;
	}
	public Dictionary<string, VariableLocal> GetLocalvarTokenDic() { return localvarTokenDic; }
	public VariableToken GetSystemVariableToken(string str)
	{
		return varTokenDic[str];
	}


	public UserDefinedCharaVariableToken CreateUserDefCharaVariable(UserDefinedVariableData data, DimLineWC dimline)
	{
		UserDefinedCharaVariableToken ret = null;
		if (data.CharaData)
		{
			int index = UserDefinedCharaVarList.Count;
			if (data.TypeIsStr)
				switch (data.Dimension)
				{
					case 1: ret = new UserDefinedCharaStr1DVariableToken(data, this, index); break;
					case 2: ret = new UserDefinedCharaStr2DVariableToken(data, this, index); break;
					default: throw new ExeEE(trerror.AbnormalVarDeclaration.Text);
				}
			else
				switch (data.Dimension)
				{
					case 1: ret = new UserDefinedCharaInt1DVariableToken(data, this, index); break;
					case 2: ret = new UserDefinedCharaInt2DVariableToken(data, this, index); break;
					default: throw new ExeEE(trerror.AbnormalVarDeclaration.Text);
				}
		}
		if (constant.IsDefinedCsvVar(data.Name))
			ParserMediator.Warn(string.Format(trerror.IsDefinedCsvVariable.Text, data.Name), dimline.SC, 1);

		UserDefinedCharaVarList.Add(ret);
		return ret;
	}
	public UserDefinedVariableToken CreateUserDefVariable(UserDefinedVariableData data, DimLineWC dimline)
	{
		UserDefinedVariableToken ret;
		if (data.Reference)
		{
			if (data.TypeIsStr)
				switch (data.Dimension)
				{
					case 0: ret = new ReferenceStrScalarToken(data); break;
					case 1: ret = new ReferenceStr1DToken(data); break;
					case 2: ret = new ReferenceStr2DToken(data); break;
					case 3: ret = new ReferenceStr3DToken(data); break;
					default: throw new ExeEE(trerror.AbnormalVarDeclaration.Text);
				}
			else if (data.TypeIsFloat)
				switch (data.Dimension)
				{
					case 0: ret = new ReferenceFloatScalarToken(data); break;
					default: throw new ExeEE(trerror.AbnormalVarDeclaration.Text);
				}
			else
				switch (data.Dimension)
				{
					case 0: ret = new ReferenceIntScalarToken(data); break;
					case 1: ret = new ReferenceInt1DToken(data); break;
					case 2: ret = new ReferenceInt2DToken(data); break;
					case 3: ret = new ReferenceInt3DToken(data); break;
					default: throw new ExeEE(trerror.AbnormalVarDeclaration.Text);
				}
			if (data.Out)
				ret.IsOut = true;
		}
		else if (data.TypeIsFloat)
			switch (data.Dimension)
			{
				case 1: ret = new StaticFloat1DVariableToken(data); break;
				default: throw new ExeEE(trerror.AbnormalVarDeclaration.Text);
			}
		else if (data.TypeIsStr)
			switch (data.Dimension)
			{
				case 1: ret = new StaticStr1DVariableToken(data); break;
				case 2: ret = new StaticStr2DVariableToken(data); break;
				case 3: ret = new StaticStr3DVariableToken(data); break;
				default: throw new ExeEE(trerror.AbnormalVarDeclaration.Text);
			}
		else
			switch (data.Dimension)
			{
				case 1: ret = new StaticInt1DVariableToken(data); break;
				case 2: ret = new StaticInt2DVariableToken(data); break;
				case 3: ret = new StaticInt3DVariableToken(data); break;
				default: throw new ExeEE(trerror.AbnormalVarDeclaration.Text);
			}
		if (constant.IsDefinedCsvVar(data.Name))
			ParserMediator.Warn(string.Format(trerror.IsDefinedCsvVariable.Text, data.Name), dimline.SC, 1);
		if (ret.IsGlobal)
			userDefinedGlobalVarList.Add(ret);
		else
			userDefinedStaticVarList.Add(ret);
		if (ret.IsSavedata)
		{
			int type = (ret.Dimension - 1) * 3;
			if (ret.IsString)
				type += 0;
			else if (ret.IsInteger)
				type += 1;
			else
				type += 2;
			if (ret.IsGlobal)
				userDefinedGlobalSaveVarList[type].Add(ret);
			else
				userDefinedSaveVarList[type].Add(ret);
		}
		return ret;
	}

	public UserDefinedVariableToken CreatePrivateVariable(UserDefinedVariableData data)
	{
		UserDefinedVariableToken ret;
		if (data.Reference)
		{
			if (data.TypeIsFloat)
			{
				switch (data.Dimension)
				{
					case 1: ret = new PrivateFloat1DVariableToken(data); break;
					default: throw new ExeEE(trerror.AbnormalVarDeclaration.Text);
				}
			}
			else if (data.TypeIsStr)
			{
				switch (data.Dimension)
				{
					case 0: ret = new ReferenceStrScalarToken(data); break;
					case 1: ret = new ReferenceStr1DToken(data); break;
					case 2: ret = new ReferenceStr2DToken(data); break;
					case 3: ret = new ReferenceStr3DToken(data); break;
					default: throw new ExeEE(trerror.AbnormalVarDeclaration.Text);
				}
			}
			else
			{
				switch (data.Dimension)
				{
					case 0: ret = new ReferenceIntScalarToken(data); break;
					case 1: ret = new ReferenceInt1DToken(data); break;
					case 2: ret = new ReferenceInt2DToken(data); break;
					case 3: ret = new ReferenceInt3DToken(data); break;
					default: throw new ExeEE(trerror.AbnormalVarDeclaration.Text);
				}
			}
			if (data.Out)
				ret.IsOut = true;
		}
		else if (data.Static)
		{
			if (data.TypeIsFloat)
			{
				switch (data.Dimension)
				{
					case 1: ret = new StaticFloat1DVariableToken(data); break;
					default: throw new ExeEE(trerror.AbnormalVarDeclaration.Text);
				}
			}
			else if (data.TypeIsStr)
			{
				switch (data.Dimension)
				{
					case 1: ret = new StaticStr1DVariableToken(data); break;
					case 2: ret = new StaticStr2DVariableToken(data); break;
					case 3: ret = new StaticStr3DVariableToken(data); break;
					default: throw new ExeEE(trerror.AbnormalVarDeclaration.Text);
				}
			}
			else
			{
				switch (data.Dimension)
				{
					case 1: ret = new StaticInt1DVariableToken(data); break;
					case 2: ret = new StaticInt2DVariableToken(data); break;
					case 3: ret = new StaticInt3DVariableToken(data); break;
					default: throw new ExeEE(trerror.AbnormalVarDeclaration.Text);
				}
			}
			userDefinedStaticVarList.Add(ret);
		}
		else
		{
			if (data.TypeIsFloat)
			{
				switch (data.Dimension)
				{
					case 1: ret = new PrivateFloat1DVariableToken(data); break;
					default: throw new ExeEE(trerror.AbnormalVarDeclaration.Text);
				}
			}
			else if (data.TypeIsStr)
			{
				switch (data.Dimension)
				{
					case 1: ret = new PrivateStr1DVariableToken(data); break;
					case 2: ret = new PrivateStr2DVariableToken(data); break;
					case 3: ret = new PrivateStr3DVariableToken(data); break;
					default: throw new ExeEE(trerror.AbnormalVarDeclaration.Text);
				}
			}
			else
			{
				switch (data.Dimension)
				{
					case 1: ret = new PrivateInt1DVariableToken(data); break;
					case 2: ret = new PrivateInt2DVariableToken(data); break;
					case 3: ret = new PrivateInt3DVariableToken(data); break;
					default: throw new ExeEE(trerror.AbnormalVarDeclaration.Text);
				}
			}
		}
		return ret;
	}

	public void SetDefaultGlobalValue()
	{
		dataIntegerArray[(int)VariableCode.__LOWERCASE__ & (int)VariableCode.GLOBAL].Clear();
		dataStringArray[(int)VariableCode.__LOWERCASE__ & (int)VariableCode.GLOBALS].Clear();
		foreach (UserDefinedVariableToken var in userDefinedGlobalVarList)
			var.SetDefault();
	}

	public void SetDefaultLocalValue()
	{
		foreach (VariableLocal local in localvarTokenDic.Values)
			local.SetDefault();
		foreach (UserDefinedVariableToken var in userDefinedStaticVarList)
			var.SetDefault();
	}

	public void ClearLocalValue()
	{
		foreach (VariableLocal local in localvarTokenDic.Values)
			local.Clear();
	}


	/// <summary>
	/// ローカルとグローバル以外初期化
	/// </summary>
	public void SetDefaultValue(ConstantData constant)
	{

		for (int i = 0; i < dataInteger.Length; i++)
			dataInteger[i] = 0;

		for (int i = 0; i < dataIntegerArray.Length; i++)
		{
			switch (i)
			{
				case (int)(VariableCode.__LOWERCASE__ & VariableCode.GLOBAL):
					break;
				case (int)(VariableCode.__LOWERCASE__ & VariableCode.ITEMPRICE):
					dataIntegerArray[i].FromArray(constant.ItemPrice);
					break;
				default:
					dataIntegerArray[i].Clear();
					break;
			}
		}

		for (int i = 0; i < dataString.Length; i++)
			dataString[i] = null;

		for (int i = 0; i < dataFloat.Length; i++)
			dataFloat[i] = 0.0;

		for (int i = 0; i < dataStringArray.Length; i++)
		{
			switch (i)
			{
				case (int)(VariableCode.__LOWERCASE__ & VariableCode.GLOBALS):
					break;
				case (int)(VariableCode.__LOWERCASE__ & VariableCode.STR):
					{
						string[] csvStrData = constant.GetCsvNameList(VariableCode.__DUMMY_STR__);
						dataStringArray[i].FromArray(csvStrData);
						break;
					}
				default:
					dataStringArray[i].Clear();
					break;
			}
		}
		for (int i = 0; i < dataIntegerArray2D.Length; i++)
		{
			long[,] array2D = dataIntegerArray2D[i];
			int length0 = array2D.GetLength(0);
			int length1 = array2D.GetLength(1);
			for (int x = 0; x < length0; x++)
				for (int y = 0; y < length1; y++)
					array2D[x, y] = 0;
		}
		for (int i = 0; i < dataStringArray2D.Length; i++)
		{
			string[,] array2D = dataStringArray2D[i];
			int length0 = array2D.GetLength(0);
			int length1 = array2D.GetLength(1);
			for (int x = 0; x < length0; x++)
				for (int y = 0; y < length1; y++)
					array2D[x, y] = null;
		}
		for (int i = 0; i < dataIntegerArray3D.Length; i++)
		{
			long[,,] array3D = dataIntegerArray3D[i];
			int length0 = array3D.GetLength(0);
			int length1 = array3D.GetLength(1);
			int length2 = array3D.GetLength(2);
			for (int x = 0; x < length0; x++)
				for (int y = 0; y < length1; y++)
					for (int z = 0; z < length2; z++)
						array3D[x, y, z] = 0;
		}
		for (int i = 0; i < dataStringArray3D.Length; i++)
		{
			string[,,] array3D = dataStringArray3D[i];
			int length0 = array3D.GetLength(0);
			int length1 = array3D.GetLength(1);
			int length2 = array3D.GetLength(2);
			for (int x = 0; x < length0; x++)
				for (int y = 0; y < length1; y++)
					for (int z = 0; z < length2; z++)
						array3D[x, y, z] = null;
		}

		var palamlv = dataIntegerArray[(int)VariableCode.__LOWERCASE__ & (int)VariableCode.PALAMLV];
		List<long> defPalam = Config.PalamLvDef;
		for (int i = 0; i < defPalam.Count; i++)
			palamlv[i] = defPalam[i];

		var explv = dataIntegerArray[(int)VariableCode.__LOWERCASE__ & (int)VariableCode.EXPLV];
		List<long> defExpLv = Config.ExpLvDef;
		for (int i = 0; i < defExpLv.Count; i++)
			explv[i] = defExpLv[i];

		dataIntegerArray[(int)VariableCode.__LOWERCASE__ & (int)VariableCode.ASSI][0] = -1;
		dataIntegerArray[(int)VariableCode.__LOWERCASE__ & (int)VariableCode.TARGET][0] = 1;
		dataIntegerArray[(int)VariableCode.__LOWERCASE__ & (int)VariableCode.PBAND][0] = Config.PbandDef;
		dataIntegerArray[(int)VariableCode.__LOWERCASE__ & (int)VariableCode.EJAC][0] = 10000;

		LastLoadVersion = -1;
		LastLoadNo = -1;
		LastLoadText = "";
	}


	const int strCount = (int)VariableCode.__COUNT_SAVE_STRING__;
	const int intCount = (int)VariableCode.__COUNT_SAVE_INTEGER__;
	const int intArrayCount = (int)VariableCode.__COUNT_SAVE_INTEGER_ARRAY__;
	const int strArrayCount = (int)VariableCode.__COUNT_SAVE_STRING_ARRAY__;
	public void SaveToStream(EraDataWriter writer)
	{

		for (int i = 0; i < strCount; i++)
			writer.Write(dataString[i]);
		for (int i = 0; i < intCount; i++)
			writer.Write(dataInteger[i]);
		for (int i = 0; i < intArrayCount; i++)
			writer.Write(dataIntegerArray[i].ToArray(constant.VariableIntArrayLength[i]));
		for (int i = 0; i < strArrayCount; i++)
			writer.Write(dataStringArray[i].ToArray(constant.VariableStrArrayLength[i]));

		for (int i = 0; i < dataFloat.Length; i++)
			writer.Write(dataFloat[i]);
		for (int i = 0; i < dataFloatArray.Length; i++)
			writer.Write(dataFloatArray[i]);
	}

	public void LoadFromStream(EraDataReader reader)
	{

		for (int i = 0; i < strCount; i++)
			dataString[i] = reader.ReadString();
		for (int i = 0; i < intCount; i++)
			dataInteger[i] = reader.ReadInt64();
		for (int i = 0; i < intArrayCount; i++)
		{
			var arr = new long[constant.VariableIntArrayLength[i]];
			reader.ReadInt64Array(arr);
			dataIntegerArray[i].FromArray(arr);
		}
		for (int i = 0; i < strArrayCount; i++)
		{
			var arr = new string[constant.VariableStrArrayLength[i]];
			reader.ReadStringArray(arr);
			dataStringArray[i].FromArray(arr);
		}

		for (int i = 0; i < dataFloat.Length; i++)
			dataFloat[i] = reader.ReadDouble();
		for (int i = 0; i < dataFloatArray.Length; i++)
			reader.ReadDoubleArray(dataFloatArray[i]);
	}

	public void SaveToStreamExtended(EraDataWriter writer)
	{
		List<VariableCode> codeList;

		//dataString
		codeList = VariableIdentifier.GetExtSaveList(VariableKind.String, VariableDimension.Scalar, false);
		foreach (VariableCode code in codeList)
			writer.WriteExtended(code.ToString(), dataString[(int)VariableCode.__LOWERCASE__ & (int)code]);
		writer.EmuSeparete();

		//datainteger
		codeList = VariableIdentifier.GetExtSaveList(VariableKind.Integer, VariableDimension.Scalar, false);
		foreach (VariableCode code in codeList)
			writer.WriteExtended(code.ToString(), dataInteger[(int)VariableCode.__LOWERCASE__ & (int)code]);
		writer.EmuSeparete();

		//dataStringArray
		codeList = VariableIdentifier.GetExtSaveList(VariableKind.String, VariableDimension.Array1D, false);
		foreach (VariableCode code in codeList)
		{
			int idx = (int)VariableCode.__LOWERCASE__ & (int)code;
			writer.WriteExtended(code.ToString(), dataStringArray[idx].ToArray(constant.VariableStrArrayLength[idx]));
		}
		writer.EmuSeparete();

		//dataIntegerArray
		codeList = VariableIdentifier.GetExtSaveList(VariableKind.Integer, VariableDimension.Array1D, false);
		foreach (VariableCode code in codeList)
		{
			int idx = (int)VariableCode.__LOWERCASE__ & (int)code;
			writer.WriteExtended(code.ToString(), dataIntegerArray[idx].ToArray(constant.VariableIntArrayLength[idx]));
		}
		writer.EmuSeparete();

		//dataStringArray2D
		//StringArray2Dの保存は未実装
		codeList = VariableIdentifier.GetExtSaveList(VariableKind.String, VariableDimension.Array2D, false);
		foreach (VariableCode code in codeList)
			writer.WriteExtended(code.ToString(), dataStringArray2D[(int)VariableCode.__LOWERCASE__ & (int)code]);
		writer.EmuSeparete();

		//dataIntegerArray2D
		codeList = VariableIdentifier.GetExtSaveList(VariableKind.Integer, VariableDimension.Array2D, false);
		foreach (VariableCode code in codeList)
			writer.WriteExtended(code.ToString(), dataIntegerArray2D[(int)VariableCode.__LOWERCASE__ & (int)code]);
		writer.EmuSeparete();

		//dataStringArray3D
		//StringArray3Dの保存は未実装
		codeList = VariableIdentifier.GetExtSaveList(VariableKind.String, VariableDimension.Array3D, false);
		foreach (VariableCode code in codeList)
			writer.WriteExtended(code.ToString(), dataStringArray3D[(int)VariableCode.__LOWERCASE__ & (int)code]);
		writer.EmuSeparete();

		//dataIntegerArray3D
		codeList = VariableIdentifier.GetExtSaveList(VariableKind.Integer, VariableDimension.Array3D, false);
		foreach (VariableCode code in codeList)
			writer.WriteExtended(code.ToString(), dataIntegerArray3D[(int)VariableCode.__LOWERCASE__ & (int)code]);
		writer.EmuSeparete();

		for (int i = 0; i < 9; i++)
		{
			foreach (UserDefinedVariableToken var in userDefinedSaveVarList[i])
			{
				switch (i)
				{
					case 0: writer.WriteExtended(var.Name, var.GetArray() is SparseArray<string> ss0 ? ss0.ToArray(ss0.Length) : (string[])var.GetArray()); break;
					case 1: writer.WriteExtended(var.Name, var.GetArray() is SparseArray<long> sl1 ? sl1.ToArray(sl1.Length) : (long[])var.GetArray()); break;
					case 2: writer.WriteExtended(var.Name, (double[])var.GetArray()); break;
					case 3: writer.WriteExtended(var.Name, (string[,])var.GetArray()); break;
					case 4: writer.WriteExtended(var.Name, (long[,])var.GetArray()); break;
					case 5: writer.WriteExtended(var.Name, (double[,])var.GetArray()); break;
					case 6: writer.WriteExtended(var.Name, (string[,,])var.GetArray()); break;
					case 7: writer.WriteExtended(var.Name, (long[,,])var.GetArray()); break;
					case 8: writer.WriteExtended(var.Name, (double[,,])var.GetArray()); break;
				}
			}
			writer.EmuSeparete();
		}
	}


	public void LoadFromStreamExtended(EraDataReader reader, int version)
	{
		Dictionary<string, string> strDic = reader.ReadStringExtended();
		Dictionary<string, long> intDic = reader.ReadInt64Extended();
		Dictionary<string, List<string>> strListDic = reader.ReadStringArrayExtended();
		Dictionary<string, List<long>> intListDic = reader.ReadInt64ArrayExtended();
		Dictionary<string, List<string[]>> str2DListDic = reader.ReadStringArray2DExtended();
		Dictionary<string, List<long[]>> int2DListDic = reader.ReadInt64Array2DExtended();
		Dictionary<string, List<List<string[]>>> str3DListDic = reader.ReadStringArray3DExtended();
		Dictionary<string, List<List<long[]>>> int3DListDic = reader.ReadInt64Array3DExtended();
		List<VariableCode> codeList;

		codeList = VariableIdentifier.GetExtSaveList(VariableKind.String, VariableDimension.Scalar, false);
		foreach (VariableCode code in codeList)
			if (strDic.ContainsKey(code.ToString()))
				dataString[(int)VariableCode.__LOWERCASE__ & (int)code] = strDic[code.ToString()];

		codeList = VariableIdentifier.GetExtSaveList(VariableKind.Integer, VariableDimension.Scalar, false);
		foreach (VariableCode code in codeList)
			if (intDic.ContainsKey(code.ToString()))
				dataInteger[(int)VariableCode.__LOWERCASE__ & (int)code] = intDic[code.ToString()];


		codeList = VariableIdentifier.GetExtSaveList(VariableKind.String, VariableDimension.Array1D, false);
		foreach (VariableCode code in codeList)
			if (strListDic.ContainsKey(code.ToString()))
				copyListToSparseArray(strListDic[code.ToString()], dataStringArray[(int)VariableCode.__LOWERCASE__ & (int)code]);

		codeList = VariableIdentifier.GetExtSaveList(VariableKind.Integer, VariableDimension.Array1D, false);
		foreach (VariableCode code in codeList)
			if (intListDic.ContainsKey(code.ToString()))
				copyListToSparseArray(intListDic[code.ToString()], dataIntegerArray[(int)VariableCode.__LOWERCASE__ & (int)code]);

		codeList = VariableIdentifier.GetExtSaveList(VariableKind.String, VariableDimension.Array2D, false);
		foreach (VariableCode code in codeList)
			if (str2DListDic.ContainsKey(code.ToString()))
				copyListToArray2D(str2DListDic[code.ToString()], dataStringArray2D[(int)VariableCode.__LOWERCASE__ & (int)code]);

		codeList = VariableIdentifier.GetExtSaveList(VariableKind.Integer, VariableDimension.Array2D, false);
		foreach (VariableCode code in codeList)
			if (int2DListDic.ContainsKey(code.ToString()))
				copyListToArray2D(int2DListDic[code.ToString()], dataIntegerArray2D[(int)VariableCode.__LOWERCASE__ & (int)code]);

		codeList = VariableIdentifier.GetExtSaveList(VariableKind.String, VariableDimension.Array3D, false);
		foreach (VariableCode code in codeList)
			if (str3DListDic.ContainsKey(code.ToString()))
				copyListToArray3D(str3DListDic[code.ToString()], dataStringArray3D[(int)VariableCode.__LOWERCASE__ & (int)code]);

		codeList = VariableIdentifier.GetExtSaveList(VariableKind.Integer, VariableDimension.Array3D, false);
		foreach (VariableCode code in codeList)
			if (int3DListDic.ContainsKey(code.ToString()))
				copyListToArray3D(int3DListDic[code.ToString()], dataIntegerArray3D[(int)VariableCode.__LOWERCASE__ & (int)code]);

		if (version < 1808)
			return;

		strListDic = reader.ReadStringArrayExtended();
		intListDic = reader.ReadInt64ArrayExtended();
		var dblListDic = reader.ReadDoubleArrayExtended();
		str2DListDic = reader.ReadStringArray2DExtended();
		int2DListDic = reader.ReadInt64Array2DExtended();
		var dbl2DListDic = reader.ReadDoubleArray2DExtended();
		str3DListDic = reader.ReadStringArray3DExtended();
		int3DListDic = reader.ReadInt64Array3DExtended();
		var dbl3DListDic = reader.ReadDoubleArray3DExtended();
		List<UserDefinedVariableToken> varList;

		int i = 0;
		varList = userDefinedSaveVarList[i]; i++;
		foreach (UserDefinedVariableToken var in varList)
			if (strListDic.TryGetValue(var.Name, out List<string> value))
			{
				object arrObj = var.GetArray();
				if (arrObj is SparseArray<string> sparse)
					copyListToSparseArray(value, sparse);
				else
					copyListToArray(value, (string[])arrObj);
			}

		varList = userDefinedSaveVarList[i]; i++;
		foreach (UserDefinedVariableToken var in varList)
			if (intListDic.TryGetValue(var.Name, out List<long> value))
			{
				object arrObj = var.GetArray();
				if (arrObj is SparseArray<long> sparse)
					copyListToSparseArray(value, sparse);
				else
					copyListToArray(value, (long[])arrObj);
			}

		varList = userDefinedSaveVarList[i]; i++;
		foreach (UserDefinedVariableToken var in varList)
			if (dblListDic.TryGetValue(var.Name, out List<double> value))
				copyListToArray(value, (double[])var.GetArray());

		varList = userDefinedSaveVarList[i]; i++;
		foreach (UserDefinedVariableToken var in varList)
			if (str2DListDic.TryGetValue(var.Name, out List<string[]> value))
				copyListToArray2D(value, (string[,])var.GetArray());

		varList = userDefinedSaveVarList[i]; i++;
		foreach (UserDefinedVariableToken var in varList)
			if (int2DListDic.TryGetValue(var.Name, out List<long[]> value))
				copyListToArray2D(value, (long[,])var.GetArray());

		varList = userDefinedSaveVarList[i]; i++;
		foreach (UserDefinedVariableToken var in varList)
			if (dbl2DListDic.TryGetValue(var.Name, out List<double[]> value))
				copyListToArray2D(value, (double[,])var.GetArray());

		varList = userDefinedSaveVarList[i]; i++;
		foreach (UserDefinedVariableToken var in varList)
			if (str3DListDic.TryGetValue(var.Name, out List<List<string[]>> value))
				copyListToArray3D(value, (string[,,])var.GetArray());

		varList = userDefinedSaveVarList[i]; i++;
		foreach (UserDefinedVariableToken var in varList)
			if (int3DListDic.TryGetValue(var.Name, out List<List<long[]>> value))
				copyListToArray3D(value, (long[,,])var.GetArray());

		varList = userDefinedSaveVarList[i];
		foreach (UserDefinedVariableToken var in varList)
			if (dbl3DListDic.TryGetValue(var.Name, out List<List<double[]>> value))
				copyListToArray3D(value, (double[,,])var.GetArray());
	}

	private static void copyListToArray<T>(List<T> srcList, T[] destArray)
	{
		int count = Math.Min(srcList.Count, destArray.Length);
		for (int i = 0; i < count; i++)
		{
			destArray[i] = srcList[i];
		}
	}

	private static void copyListToSparseArray<T>(List<T> srcList, SparseArray<T> destArray)
	{
		destArray.Clear();
		for (int i = 0; i < srcList.Count; i++)
		{
			destArray[i] = srcList[i];
		}
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
	private static void copyListToArray3D<T>(List<List<T[]>> srcList, T[,,] destArray)
	{
		int countX = Math.Min(srcList.Count, destArray.GetLength(0));
		int dLength1 = destArray.GetLength(1);
		int dLength2 = destArray.GetLength(2);
		for (int x = 0; x < countX; x++)
		{
			List<T[]> srcArray = srcList[x];
			int countY = Math.Min(srcArray.Count, dLength1);
			for (int y = 0; y < countY; y++)
			{
				T[] baseArray = srcArray[y];
				int countZ = Math.Min(baseArray.Length, dLength2);
				for (int z = 0; z < countZ; z++)
				{
					destArray[x, y, z] = baseArray[z];
				}
			}
		}
	}


	public void SaveGlobalToStream(EraDataWriter writer)
	{
		int globalIdx = (int)(VariableCode.__LOWERCASE__ & VariableCode.GLOBAL);
		int globalsIdx = (int)(VariableCode.__LOWERCASE__ & VariableCode.GLOBALS);
		writer.Write(dataIntegerArray[globalIdx].ToArray(constant.VariableIntArrayLength[globalIdx]));
		writer.Write(dataStringArray[globalsIdx].ToArray(constant.VariableStrArrayLength[globalsIdx]));
	}

	public void LoadGlobalFromStream(EraDataReader reader)
	{
		int globalIdx = (int)(VariableCode.__LOWERCASE__ & VariableCode.GLOBAL);
		int globalsIdx = (int)(VariableCode.__LOWERCASE__ & VariableCode.GLOBALS);
		var intArr = new long[constant.VariableIntArrayLength[globalIdx]];
		reader.ReadInt64Array(intArr);
		dataIntegerArray[globalIdx].FromArray(intArr);
		var strArr = new string[constant.VariableStrArrayLength[globalsIdx]];
		reader.ReadStringArray(strArr);
		dataStringArray[globalsIdx].FromArray(strArr);
	}

	public void SaveGlobalToStream1808(EraDataWriter writer)
	{
		for (int i = 0; i < 9; i++)
		{
			foreach (UserDefinedVariableToken var in userDefinedGlobalSaveVarList[i])
			{
				switch (i)
				{
					case 0: writer.WriteExtended(var.Name, var.GetArray() is SparseArray<string> gss0 ? gss0.ToArray(gss0.Length) : (string[])var.GetArray()); break;
					case 1: writer.WriteExtended(var.Name, var.GetArray() is SparseArray<long> gsl1 ? gsl1.ToArray(gsl1.Length) : (long[])var.GetArray()); break;
					case 2: writer.WriteExtended(var.Name, (double[])var.GetArray()); break;
					case 3: writer.WriteExtended(var.Name, (string[,])var.GetArray()); break;
					case 4: writer.WriteExtended(var.Name, (long[,])var.GetArray()); break;
					case 5: writer.WriteExtended(var.Name, (double[,])var.GetArray()); break;
					case 6: writer.WriteExtended(var.Name, (string[,,])var.GetArray()); break;
					case 7: writer.WriteExtended(var.Name, (long[,,])var.GetArray()); break;
					case 8: writer.WriteExtended(var.Name, (double[,,])var.GetArray()); break;
				}
			}
			writer.EmuSeparete();
		}
	}

	public void LoadGlobalFromStream1808(EraDataReader reader)
	{
		Dictionary<string, List<string>> strListDic = reader.ReadStringArrayExtended();
		Dictionary<string, List<long>> intListDic = reader.ReadInt64ArrayExtended();
		var dblListDic = reader.ReadDoubleArrayExtended();
		Dictionary<string, List<string[]>> str2DListDic = reader.ReadStringArray2DExtended();
		Dictionary<string, List<long[]>> int2DListDic = reader.ReadInt64Array2DExtended();
		var dbl2DListDic = reader.ReadDoubleArray2DExtended();
		Dictionary<string, List<List<string[]>>> str3DListDic = reader.ReadStringArray3DExtended();
		Dictionary<string, List<List<long[]>>> int3DListDic = reader.ReadInt64Array3DExtended();
		var dbl3DListDic = reader.ReadDoubleArray3DExtended();

		List<UserDefinedVariableToken> varList;

		int i = 0;
		varList = userDefinedGlobalSaveVarList[i]; i++;
		foreach (UserDefinedVariableToken var in varList)
			if (strListDic.TryGetValue(var.Name, out List<string> value))
			{
				object arrObj = var.GetArray();
				if (arrObj is SparseArray<string> sparse)
					copyListToSparseArray(value, sparse);
				else
					copyListToArray(value, (string[])arrObj);
			}

		varList = userDefinedGlobalSaveVarList[i]; i++;
		foreach (UserDefinedVariableToken var in varList)
			if (intListDic.TryGetValue(var.Name, out List<long> value))
			{
				object arrObj = var.GetArray();
				if (arrObj is SparseArray<long> sparse)
					copyListToSparseArray(value, sparse);
				else
					copyListToArray(value, (long[])arrObj);
			}

		varList = userDefinedGlobalSaveVarList[i]; i++;
		foreach (UserDefinedVariableToken var in varList)
			if (dblListDic.TryGetValue(var.Name, out List<double> value))
				copyListToArray(value, (double[])var.GetArray());

		varList = userDefinedGlobalSaveVarList[i]; i++;
		foreach (UserDefinedVariableToken var in varList)
			if (str2DListDic.TryGetValue(var.Name, out List<string[]> value))
				copyListToArray2D(value, (string[,])var.GetArray());

		varList = userDefinedGlobalSaveVarList[i]; i++;
		foreach (UserDefinedVariableToken var in varList)
			if (int2DListDic.TryGetValue(var.Name, out List<long[]> value))
				copyListToArray2D(value, (long[,])var.GetArray());

		varList = userDefinedGlobalSaveVarList[i]; i++;
		foreach (UserDefinedVariableToken var in varList)
			if (dbl2DListDic.TryGetValue(var.Name, out List<double[]> value))
				copyListToArray2D(value, (double[,])var.GetArray());

		varList = userDefinedGlobalSaveVarList[i]; i++;
		foreach (UserDefinedVariableToken var in varList)
			if (str3DListDic.TryGetValue(var.Name, out List<List<string[]>> value))
				copyListToArray3D(value, (string[,,])var.GetArray());

		varList = userDefinedGlobalSaveVarList[i]; i++;
		foreach (UserDefinedVariableToken var in varList)
			if (int3DListDic.TryGetValue(var.Name, out List<List<long[]>> value))
				copyListToArray3D(value, (long[,,])var.GetArray());

		varList = userDefinedGlobalSaveVarList[i];
		foreach (UserDefinedVariableToken var in varList)
			if (dbl3DListDic.TryGetValue(var.Name, out List<List<double[]>> value))
				copyListToArray3D(value, (double[,,])var.GetArray());
	}

	#region EM_私家版_セーブ拡張
	public void SaveGlobalEMDataToStreamBinary(EraBinaryDataWriter writer)
	{
		foreach (var key in GlobalStatic.ConstantData.GlobalSaveMaps)
		{
			if (DataStringMaps.TryGetValue(key, out var map1))
			{
				writer.WriteWithKey(key, map1);
			}
		}
		foreach (var key in GlobalStatic.ConstantData.GlobalSaveXmls)
		{
			if (DataXmlDocument.TryGetValue(key, out var xml1))
			{
				writer.WriteWithKey(key, xml1);
			}
		}
		foreach (var key in GlobalStatic.ConstantData.GlobalSaveDTs)
		{
			if (DataDataTables.TryGetValue(key, out var dt1))
			{
				writer.WriteWithKey(key, dt1);
			}
		}
	}
	public void SaveEMDataToStreamBinary(EraBinaryDataWriter writer)
	{
		foreach (var key in GlobalStatic.ConstantData.SaveMaps)
		{
			if (DataStringMaps.TryGetValue(key, out var map2))
			{
				writer.WriteWithKey(key, map2);
			}
		}
		foreach (var key in GlobalStatic.ConstantData.SaveXmls)
		{
			if (DataXmlDocument.TryGetValue(key, out var xml2))
			{
				writer.WriteWithKey(key, xml2);
			}
		}
		foreach (var key in GlobalStatic.ConstantData.SaveDTs)
		{
			if (DataDataTables.TryGetValue(key, out var dt2))
			{
				writer.WriteWithKey(key, dt2);
			}
		}
	}
	#endregion

	public void SaveGlobalToStreamBinary(EraBinaryDataWriter writer)
	{
		foreach (KeyValuePair<string, VariableToken> pair in varTokenDic)
		{
			VariableToken var = pair.Value;
			if (var.IsSavedata && !var.IsCharacterData && var.IsGlobal)
			{
				object arr = var.GetArray();
				if (arr is SparseArray<long> sparseLong)
					writer.WriteWithKey(pair.Key, sparseLong.ToArray(sparseLong.Length));
				else if (arr is SparseArray<string> sparseStr)
					writer.WriteWithKey(pair.Key, sparseStr.ToArray(sparseStr.Length));
				else
					writer.WriteWithKey(pair.Key, arr);
			}
		}
		foreach (UserDefinedVariableToken var in userDefinedGlobalVarList)
		{
			if (var.IsSavedata)
			{
				object arr = var.GetArray();
				if (arr is SparseArray<long> sparseLong)
					writer.WriteWithKey(var.Name, sparseLong.ToArray(sparseLong.Length));
				else if (arr is SparseArray<string> sparseStr)
					writer.WriteWithKey(var.Name, sparseStr.ToArray(sparseStr.Length));
				else
					writer.WriteWithKey(var.Name, arr);
			}
		}
	}

	public void SaveToStreamBinary(EraBinaryDataWriter writer)
	{
		foreach (KeyValuePair<string, VariableToken> pair in varTokenDic)
		{
			VariableToken var = pair.Value;
			if (var.IsSavedata && !var.IsCharacterData && !var.IsGlobal)
			{
				object arr = var.GetArray();
				if (arr is SparseArray<long> sparseLong)
					writer.WriteWithKey(pair.Key, sparseLong.ToArray(sparseLong.Length));
				else if (arr is SparseArray<string> sparseStr)
					writer.WriteWithKey(pair.Key, sparseStr.ToArray(sparseStr.Length));
				else
					writer.WriteWithKey(pair.Key, arr);
			}
		}
		foreach (UserDefinedVariableToken var in userDefinedStaticVarList)
		{
			if (var.IsSavedata)
			{
				object arr = var.GetArray();
				if (arr is SparseArray<long> sparseLong)
					writer.WriteWithKey(var.Name, sparseLong.ToArray(sparseLong.Length));
				else if (arr is SparseArray<string> sparseStr)
					writer.WriteWithKey(var.Name, sparseStr.ToArray(sparseStr.Length));
				else
					writer.WriteWithKey(var.Name, arr);
			}
		}
	}

	public void LoadFromStreamBinary(EraBinaryDataReader bReader)
	{
		while (LoadVariableBinary(bReader)) { }
	}

	#region EE_RESETDATA、RESETGLOBAL、LOADDATA、LOADGLOBAL時にMap、Xml、DataTableを適切に削除するように
	public void RemoveEMSaveData()
	{
		foreach (var key in GlobalStatic.ConstantData.SaveMaps)
		{
			if (DataStringMaps.TryGetValue(key, out var map))
			{
				map.Clear();
			}
		}
		foreach (var key in GlobalStatic.ConstantData.SaveXmls)
		{
			if (DataXmlDocument.ContainsKey(key))
			{
				DataXmlDocument.Remove(key);
			}
		}
		foreach (var key in GlobalStatic.ConstantData.SaveDTs)
		{
			if (DataDataTables.TryGetValue(key, out var dt))
			{
				dt.Clear();
			}
		}
	}

	public void RemoveEMGlobalData()
	{
		foreach (var key in GlobalStatic.ConstantData.GlobalSaveMaps)
		{
			if (DataStringMaps.TryGetValue(key, out var map))
			{
				map.Clear();
			}
		}
		foreach (var key in GlobalStatic.ConstantData.GlobalSaveXmls)
		{
			if (DataXmlDocument.ContainsKey(key))
			{
				DataXmlDocument.Remove(key);
			}
		}
		foreach (var key in GlobalStatic.ConstantData.GlobalSaveDTs)
		{
			if (DataDataTables.TryGetValue(key, out var dt))
			{
				dt.Clear();
			}
		}
	}
	public void RemoveEMStaticData()
	{
		foreach (var key in GlobalStatic.ConstantData.StaticMaps)
		{
			if (DataStringMaps.TryGetValue(key, out var map))
			{
				map.Clear();
			}
		}
		foreach (var key in GlobalStatic.ConstantData.StaticXmls)
		{
			if (DataXmlDocument.ContainsKey(key))
			{
				DataXmlDocument.Remove(key);
			}
		}
		foreach (var key in GlobalStatic.ConstantData.StaticDTs)
		{
			if (DataDataTables.TryGetValue(key, out var dt))
			{
				dt.Clear();
			}
		}
	}
	#endregion
	/// <summary>
	/// 1808 キャラクタ型でない変数を一つ読む
	/// ファイル終端の場合はfalseを返す
	/// </summary>
	/// <param name="reader"></param>
	public bool LoadVariableBinary(EraBinaryDataReader reader)
	{
		KeyValuePair<string, EraSaveDataType> nameAndType = reader.ReadVariableCode();
		VariableToken vToken = null;
		if (nameAndType.Key != null && !GlobalStatic.IdentifierDictionary.getVarTokenIsForbid(nameAndType.Key))
			vToken = GlobalStatic.IdentifierDictionary.GetVariableToken(nameAndType.Key, null, false);
		if (vToken != null && (vToken.IsCharacterData || vToken.IsConst || vToken.IsPrivate || vToken.IsLocal || vToken.IsCalc))
			vToken = null;
		switch (nameAndType.Value)
		{
			#region EM_私家版_セーブ拡張
			case EraSaveDataType.Map:
				{
					var key = reader.ReadString();
					var dict = reader.ReadMap();
					if (GlobalStatic.ConstantData.SaveMaps.Contains(key) || GlobalStatic.ConstantData.GlobalSaveMaps.Contains(key))
					{
						DataStringMaps[key] = dict;
					}
					break;
				}
			case EraSaveDataType.Xml:
				{
					var key = reader.ReadString();
					var doc = reader.ReadXml();
					if (GlobalStatic.ConstantData.SaveXmls.Contains(key) || GlobalStatic.ConstantData.GlobalSaveXmls.Contains(key))
					{
						DataXmlDocument[key] = doc;
					}
					break;
				}
			case EraSaveDataType.DT:
				{
					var key = reader.ReadString();
					var dt = reader.ReadDataTable();
					if (GlobalStatic.ConstantData.SaveDTs.Contains(key) || GlobalStatic.ConstantData.GlobalSaveDTs.Contains(key))
					{
						DataDataTables[key] = dt;
					}
					break;
				}
			#endregion
			case EraSaveDataType.EOF:
				return false;
			case EraSaveDataType.Int:
				if (vToken == null || !vToken.IsInteger || vToken.Dimension != 0)
					reader.ReadInt();//該当変数なし、or型不一致なら読み捨てる
				else
					vToken.SetValue(reader.ReadInt(), null);
				break;
			case EraSaveDataType.Str:
				if (vToken == null || !vToken.IsString || vToken.Dimension != 0)
					reader.ReadString();
				else
					vToken.SetValue(reader.ReadString(), null);
				break;
			case EraSaveDataType.IntArray:
				if (vToken == null || !vToken.IsInteger || vToken.Dimension != 1)
					reader.ReadIntArray(null, true);
				else
				{
					object arrObj = vToken.GetArray();
					if (arrObj is SparseArray<long> sparse)
					{
						long[] tmp = reader.ReadIntArrayIntoNew(true);
						sparse.Length = tmp.Length;
						sparse.FromArray(tmp);
					}
					else
						reader.ReadIntArray((long[])arrObj, true);
				}
				break;
			case EraSaveDataType.IntArray2D:
				if (vToken == null || !vToken.IsInteger || vToken.Dimension != 2)
					reader.ReadIntArray2D(null, true);
				else
					reader.ReadIntArray2D((long[,])vToken.GetArray(), true);
				break;
			case EraSaveDataType.IntArray3D:
				if (vToken == null || !vToken.IsInteger || vToken.Dimension != 3)
					reader.ReadIntArray3D(null, true);
				else
					reader.ReadIntArray3D((long[,,])vToken.GetArray(), true);
				break;
			case EraSaveDataType.StrArray:
				if (vToken == null || !vToken.IsString || vToken.Dimension != 1)
					reader.ReadStrArray(null, true);
				else
				{
					object arrObj = vToken.GetArray();
					if (arrObj is SparseArray<string> sparse)
					{
						string[] tmp = reader.ReadStrArrayIntoNew(true);
						sparse.Length = tmp.Length;
						sparse.FromArray(tmp);
					}
					else
						reader.ReadStrArray((string[])arrObj, true);
				}
				break;
			case EraSaveDataType.StrArray2D:
				if (vToken == null || !vToken.IsString || vToken.Dimension != 2)
					reader.ReadStrArray2D(null, true);
				else
					reader.ReadStrArray2D((string[,])vToken.GetArray(), true);
				break;
			case EraSaveDataType.StrArray3D:
				if (vToken == null || !vToken.IsString || vToken.Dimension != 3)
					reader.ReadStrArray3D(null, true);
				else
					reader.ReadStrArray3D((string[,,])vToken.GetArray(), true);
				break;
			case EraSaveDataType.Float:
				if (vToken == null || !vToken.IsFloat || vToken.Dimension != 0)
					reader.ReadDouble();
				else
					vToken.SetValue(reader.ReadDouble(), null);
				break;
			case EraSaveDataType.FloatArray:
				if (vToken == null || !vToken.IsFloat || vToken.Dimension != 1)
				{
					int len = reader.ReadInt32();
					for (int i = 0; i < len; i++) reader.ReadDouble();
				}
				else
				{
					int len = reader.ReadInt32();
					object arrObj = vToken.GetArray();
					if (arrObj is SparseArray<double> sparse)
					{
						double[] tmp = new double[len];
						for (int i = 0; i < len; i++) tmp[i] = reader.ReadDouble();
						sparse.Length = tmp.Length;
						sparse.FromArray(tmp);
					}
					else
					{
						double[] arr = (double[])arrObj;
						for (int i = 0; i < Math.Min(len, arr.Length); i++) arr[i] = reader.ReadDouble();
						for (int i = Math.Min(len, arr.Length); i < len; i++) reader.ReadDouble();
					}
				}
				break;
			case EraSaveDataType.FloatArray2D:
				if (vToken == null || !vToken.IsFloat || vToken.Dimension != 2)
				{
					int d0 = reader.ReadInt32(); int d1 = reader.ReadInt32();
					for (int i = 0; i < d0 * d1; i++) reader.ReadDouble();
				}
				else
				{
					int d0 = reader.ReadInt32(); int d1 = reader.ReadInt32();
					double[,] arr = (double[,])vToken.GetArray();
					int len0 = Math.Min(d0, arr.GetLength(0));
					int len1 = Math.Min(d1, arr.GetLength(1));
					for (int i0 = 0; i0 < len0; i0++)
						for (int i1 = 0; i1 < len1; i1++)
							arr[i0, i1] = reader.ReadDouble();
					for (int i = len0 * len1; i < d0 * d1; i++) reader.ReadDouble();
				}
				break;
			case EraSaveDataType.FloatArray3D:
				if (vToken == null || !vToken.IsFloat || vToken.Dimension != 3)
				{
					int d0 = reader.ReadInt32(); int d1 = reader.ReadInt32(); int d2 = reader.ReadInt32();
					for (int i = 0; i < d0 * d1 * d2; i++) reader.ReadDouble();
				}
				else
				{
					int d0 = reader.ReadInt32(); int d1 = reader.ReadInt32(); int d2 = reader.ReadInt32();
					double[,,] arr = (double[,,])vToken.GetArray();
					int len0 = Math.Min(d0, arr.GetLength(0));
					int len1 = Math.Min(d1, arr.GetLength(1));
					int len2 = Math.Min(d2, arr.GetLength(2));
					for (int i0 = 0; i0 < len0; i0++)
						for (int i1 = 0; i1 < len1; i1++)
							for (int i2 = 0; i2 < len2; i2++)
								arr[i0, i1, i2] = reader.ReadDouble();
					for (int i = len0 * len1 * len2; i < d0 * d1 * d2; i++) reader.ReadDouble();
				}
				break;
			default:
				throw new FileEE(trerror.AbnormalData.Text);
		}
		return true;
	}
	#region IDisposable メンバ

	public void Dispose()
	{
		ClearLocalValue();
		for (int i = 0; i < dataIntegerArray.Length; i++)
			dataIntegerArray[i].Clear();
		for (int i = 0; i < dataStringArray.Length; i++)
			dataStringArray[i].Clear();
		for (int i = 0; i < characterList.Count; i++)
			characterList[i].Dispose();
		characterList.Clear();
	}

	#endregion

}
