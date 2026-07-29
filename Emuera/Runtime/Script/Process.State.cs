using MinorShift.Emuera.GameData.Variable;
using GameProcProcess = MinorShift.Emuera.GameProc.Process;
using trerror = MinorShift.Emuera.Runtime.Utils.EvilMask.Lang.Error;
using trsl = MinorShift.Emuera.Runtime.Utils.EvilMask.Lang.SystemLine;
using System;
using System.Diagnostics;
using System.Drawing;
using MinorShift.Emuera.GameView;
using System.Collections.Generic;
using MinorShift.Emuera.Runtime.Script.Statements;
using MinorShift.Emuera.Runtime.Script.Statements.Expression;
using MinorShift.Emuera.Runtime.Script.Statements.Variable;
using MinorShift.Emuera.Runtime.Utils;
using MinorShift.Emuera.UI.Game.Image;

namespace MinorShift.Emuera.Runtime.Script;

//1756 インナークラス解除して一般に開放

internal enum SystemStateCode
{
	__CAN_SAVE__ = 0x10000,//セーブロード画面を呼び出し可能か？
	__CAN_BEGIN__ = 0x20000,//BEGIN命令を呼び出し可能か？
	Title_Begin = 0,//初期状態
	Openning = 1,//最初の入力待ち
	Train_Begin = 0x10,//BEGIN TRAINから。
	Train_CallEventTrain = 0x11,//@EVENTTRAINの呼び出し中。スキップ可能
	Train_CallShowStatus = 0x12,//@SHOW_STATUSの呼び出し中
	Train_CallComAbleXX = 0x13,//@COM_ABLExxの呼び出し中。スキップの場合、RETURN 1とする。
	Train_CallShowUserCom = 0x14,//@SHOW_USERCOMの呼び出し中
	Train_WaitInput = 0x15,//入力待ち状態。選択が実行可能ならEVENTCOMからCOMxx、そうでなければ@USERCOMにRESULTを渡す
	Train_CallEventCom = 0x16 | __CAN_BEGIN__,//@EVENTCOMの呼び出し中

	Train_CallComXX = 0x17 | __CAN_BEGIN__,//@COMxxの呼び出し中
	Train_CallSourceCheck = 0x18 | __CAN_BEGIN__,//@SOURCE_CHECKの呼び出し中
	Train_CallEventComEnd = 0x19 | __CAN_BEGIN__,//@EVENTCOMENDの呼び出し中。スキップ可能。Train_CallEventTrainへ帰る。@USERCOMの呼び出し中もここ

	Train_DoTrain = 0x1A,

	AfterTrain_Begin = 0x20 | __CAN_BEGIN__,//BEGIN AFTERTRAINから。@EVENTENDを呼び出してNormalへ。

	Ablup_Begin = 0x30,//BEGIN ABLUPから。
	Ablup_CallShowJuel = 0x31,//@SHOW_JUEL
	Ablup_CallShowAblupSelect = 0x32,//@SHOW_ABLUP_SELECT
	Ablup_WaitInput = 0x33,//
	Ablup_CallAblupXX = 0x34 | __CAN_BEGIN__,//@ABLUPxxがない場合は、@USERABLUPにRESULTを渡す。Ablup_CallShowJuelへ戻る。

	Turnend_Begin = 0x40 | __CAN_BEGIN__,//BEGIN TURNENDから。@EVENTTURNENDを呼び出してNormalへ。

	Shop_Begin = 0x50 | __CAN_SAVE__,//BEGIN SHOPから
	Shop_CallEventShop = 0x51 | __CAN_BEGIN__ | __CAN_SAVE__,//@EVENTSHOPの呼び出し中。スキップ可能
	Shop_CallShowShop = 0x52 | __CAN_SAVE__,//@SHOW_SHOPの呼び出し中
	Shop_WaitInput = 0x53 | __CAN_SAVE__,//入力待ち状態。アイテムが存在するならEVENTBUYにBOUGHT、そうでなければ@USERSHOPにRESULTを渡す
	Shop_CallEventBuy = 0x54 | __CAN_BEGIN__ | __CAN_SAVE__,//@USERSHOPまた@EVENTBUYはの呼び出し中

	SaveGame_Begin = 0x100,//SAVEGAMEから
	SaveGame_WaitInput = 0x101,//入力待ち
	SaveGame_WaitInputOverwrite = 0x102,//上書きの許可待ち
	SaveGame_CallSaveInfo = 0x103,//@SAVEINFO呼び出し中。20回。
	LoadGame_Begin = 0x110,//LOADGAMEから
	LoadGame_WaitInput = 0x111,//入力待ち
	LoadGameOpenning_Begin = 0x120,//最初に[1]を選択したとき。
	LoadGameOpenning_WaitInput = 0x121,//入力待ち


	//AutoSave_Begin = 0x200,
	AutoSave_CallSaveInfo = 0x201,
	AutoSave_CallUniqueAutosave = 0x202,
	AutoSave_Skipped = 0x203,

	LoadData_DataLoaded = 0x210,//データロード直後
	LoadData_CallSystemLoad = 0x211 | __CAN_BEGIN__,//データロード直後
	LoadData_CallEventLoad = 0x212 | __CAN_BEGIN__,//@EVENTLOADの呼び出し中。スキップ可能

	Openning_TitleLoadgame = 0x220,

	System_Reloaderb = 0x230,
	First_Begin = 0x240,

	Normal = 0xFFFF | __CAN_BEGIN__ | __CAN_SAVE__,//特に何でもないとき。ScriptEndに達したらエラー
}

internal enum BeginType
{
	NULL = 0,
	SHOP = 2,
	TRAIN = 3,
	AFTERTRAIN = 4,
	ABLUP = 5,
	TURNEND = 6,
	FIRST = 7,
	TITLE = 8,
}

internal sealed class ProcessState
{
	public ProcessState(EmueraConsole console)
	{
		if (Program.DebugMode)//DebugModeでなければ知らなくて良い
			this.console = console;
	}
	readonly EmueraConsole console;
	readonly List<CalledFunction> functionList = [];
	private LogicalLine currentLine;
	private string pendingThrowMessage;
	private bool inBeforeError;
	private bool skipBeforeError;
	private bool inBeforeThrow;
	private InstructionLine pendingThrowLine;
	private Exception pendingErrorException;
	private LogicalLine pendingErrorCurrentLine;
	private bool pendingErrorSystemProc;

	private readonly Stack<ExecutionContext> _contextStack = new();
	private Stack<ExecutionContext> _savedContextStack;
	public ExecutionContext CurrentContext => _contextStack.Count > 0 ? _contextStack.Peek() : _savedContextStack?.Count > 0 ? _savedContextStack.Peek() : null;
	public IEnumerable<ExecutionContext> ContextStack => _contextStack.Count > 0 ? _contextStack : _savedContextStack ?? _contextStack;

	public ExecutionContext FindContextByLabel(string labelName)
	{
		var stack = _contextStack.Count > 0 ? _contextStack : _savedContextStack;
		if (stack != null)
		{
			foreach (var ctx in stack.ToArray())
			{
				if (ctx.Function != null && ctx.Function.LabelName == labelName)
					return ctx;
			}
		}
		return null;
	}

	public void PushContext(ExecutionContext ctx) => _contextStack.Push(ctx);
	public ExecutionContext PopContext() => _contextStack.Count > 0 ? _contextStack.Pop() : null;
	public int ContextStackCount => _contextStack.Count;

	public (int funcCount, int ctxCount, LogicalLine currentLine) CaptureCallState() => (functionList.Count, _contextStack.Count, CurrentLine);

	public void RollbackToState(int targetFuncCount, int targetCtxCount, LogicalLine targetCurrentLine)
	{
		while (functionList.Count > targetFuncCount)
		{
			var called = functionList[functionList.Count - 1];
			if (called.CurrentLabel.hasPrivDynamicVar)
				called.CurrentLabel.ScopeOut();
			functionList.RemoveAt(functionList.Count - 1);
		}
		while (_contextStack.Count > targetCtxCount)
		{
			var ctx = _contextStack.Pop();
			ctx?.Dispose();
		}
		CurrentLine = targetCurrentLine;
	}

	//private LogicalLine nextLine;
	public int lineCount;
	public int currentMin;
	//private bool sequential;

	public bool ScriptEnd
	{
		get
		{
			return functionList.Count == currentMin;
		}
	}

	public int functionCount
	{
		get
		{
			return functionList.Count;
		}
	}

	public IReadOnlyList<CalledFunction> FunctionList => functionList;

	public int CurrentVariadicArgCount
	{
		get
		{
			if (functionList.Count == 0) return 0;
			return functionList[^1].VariadicArgCount;
		}
	}

	public string PendingThrowMessage { get { return pendingThrowMessage; } set { pendingThrowMessage = value; } }
	public bool HasPendingThrow { get { return pendingThrowMessage != null; } }
	public void ClearPendingThrow() { pendingThrowMessage = null; }
	public InstructionLine PendingThrowLine { get { return pendingThrowLine; } set { pendingThrowLine = value; } }

	public bool InBeforeError { get { return inBeforeError; } set { inBeforeError = value; } }
	public bool SkipBeforeError { get { return skipBeforeError; } set { skipBeforeError = value; } }
	public bool InBeforeThrow { get { return inBeforeThrow; } set { inBeforeThrow = value; } }

	public Exception PendingErrorException { get { return pendingErrorException; } set { pendingErrorException = value; } }
	public LogicalLine PendingErrorCurrentLine { get { return pendingErrorCurrentLine; } set { pendingErrorCurrentLine = value; } }
	public bool PendingErrorSystemProc { get { return pendingErrorSystemProc; } set { pendingErrorSystemProc = value; } }

	SystemStateCode sysStateCode = SystemStateCode.Title_Begin;
	BeginType begintype = BeginType.NULL;
	public bool isBegun { get { return begintype != BeginType.NULL; } }

	public LogicalLine CurrentLine { get { return currentLine; } set { currentLine = value; } }
	public LogicalLine ErrorLine
	{
		get
		{
			//if (RunningLine != null)
			//	return RunningLine;
			return currentLine;
		}
	}

	//IF文中でELSEIF文の中身をチェックするなどCurrentLineと作業中のLineが違う時にセットする
	//public LogicalLine RunningLine { get; set; }
	//1755a 呼び出し元消滅
	//public bool Sequential { get { return sequential; } }
	public CalledFunction CurrentCalled
	{
		get
		{
			//実行関数なしの状態は一部のシステムINPUT以外では存在しないのでGOTO系の処理でしかここに来ない関係上、前提を満たしようがない
			//if (functionList.Count == 0)
			//    throw new ExeEE("実行中関数がない");
			return functionList[^1];
		}
	}
	public SystemStateCode SystemState
	{
		get { return sysStateCode; }
		set { sysStateCode = value; }
	}

	public void ShiftNextLine()
	{
		if (currentLine == null)
			return;
		currentLine = currentLine.NextLine;
		//nextLine = nextLine.NextLine;
		//RunningLine = null;
		//sequential = true;
		//GlobalStatic.Process.lineCount++;
		lineCount++;
	}

	/// <summary>
	/// 関数内の移動。JUMPではなくGOTOやIF文など
	/// </summary>
	/// <param name="line"></param>
	public void JumpTo(LogicalLine line)
	{
		currentLine = line;
		lineCount++;
		//sequential = false;
		//ShfitNextLine();
	}
	#region EE_FORCE_QUIT系
	// public void SetBegin(string keyword)
	public void SetBegin(string keyword, bool force)
	{//TrimとToUpper済みのはず
		switch (keyword)
		{
			case "SHOP":
				AppContents.UnloadTempLoadedConstImageNames();
				AppContents.UnloadTempLoadedGraphicsImageNames();
				SetBegin(BeginType.SHOP, force); return;
			case "TRAIN":
				SetBegin(BeginType.TRAIN, force); return;
			case "AFTERTRAIN":
				SetBegin(BeginType.AFTERTRAIN, force); return;
			case "ABLUP":
				SetBegin(BeginType.ABLUP, force); return;
			case "TURNEND":
				SetBegin(BeginType.TURNEND, force); return;
			case "FIRST":
				AppContents.UnloadTempLoadedConstImageNames();
				AppContents.UnloadTempLoadedGraphicsImageNames();
				SetBegin(BeginType.FIRST, force); return;
			case "TITLE":
				SetBegin(BeginType.TITLE, force); return;
		}
		throw new CodeEE(string.Format(trerror.InvalidBeginArg.Text, keyword));
	}

	//public void SetBegin(BeginType type)
	public void SetBegin(BeginType type, bool force)
	{
		string errmes;
		switch (type)
		{
			case BeginType.SHOP:
			case BeginType.TRAIN:
			case BeginType.AFTERTRAIN:
			case BeginType.ABLUP:
			case BeginType.TURNEND:
			case BeginType.FIRST:
				if (force == true) break;
				if ((sysStateCode & SystemStateCode.__CAN_BEGIN__) != SystemStateCode.__CAN_BEGIN__)
				{
					errmes = "BEGIN";
					goto err;
				}
				break;
			//1.729 BEGIN TITLEはどこでも使えるように
			case BeginType.TITLE:
				break;
				//BEGINの処理中でチェック済み
				//default:
				//    throw new ExeEE("不適当なBEGIN呼び出し");
		}
		begintype = type;
		return;
	err:
		CalledFunction func = functionList[0];
		string funcName = func.FunctionName;
		throw new CodeEE(string.Format(trerror.CanNotUseInstruction.Text, funcName, errmes));
	}
	#endregion

	public void SaveLoadData(bool saveData)
	{

		if (saveData)
			sysStateCode = SystemStateCode.SaveGame_Begin;
		else
			sysStateCode = SystemStateCode.LoadGame_Begin;
		//ClearFunctionList();
		return;
	}

	public void ClearFunctionList()
	{
		if (GameProcProcess.DebugLogEnabled) GameProcProcess.DebugLog(string.Format("[ClearFunctionList] clearing {0} functions\n", functionList.Count));
		if (Program.DebugMode && !isClone && GlobalStatic.Process.MethodStack() == 0)
			console.DebugClearTraceLog();
		foreach (CalledFunction called in functionList)
			if (called.CurrentLabel.hasPrivDynamicVar)
				called.CurrentLabel.ScopeOut();
		while (_contextStack.Count > 0)
		{
			var ctx = _contextStack.Pop();
			ctx.Dispose();
		}
		functionList.Clear();
		begintype = BeginType.NULL;
		if (GameProcProcess.DebugLogEnabled) GameProcProcess.DebugLog("[ClearFunctionList] done\n");
	}

	public void ClearFunctionListPreserveTrace()
	{
		if (GameProcProcess.DebugLogEnabled) GameProcProcess.DebugLog(string.Format("[ClearFunctionListPreserveTrace] clearing {0} functions\n", functionList.Count));
		foreach (CalledFunction called in functionList)
			if (called.CurrentLabel.hasPrivDynamicVar)
				called.CurrentLabel.ScopeOut();
		while (_contextStack.Count > 0)
		{
			var ctx = _contextStack.Pop();
			ctx.Dispose();
		}
		functionList.Clear();
		begintype = BeginType.NULL;
		if (GameProcProcess.DebugLogEnabled) GameProcProcess.DebugLog("[ClearFunctionListPreserveTrace] done\n");
	}

	public bool calledWhenNormal = true;
	/// <summary>
	/// BEGIN命令によるプログラム状態の変化
	/// </summary>
	/// <param name="key"></param>
	/// <returns></returns>
	public void Begin()
	{
		//@EVENTSHOPからの呼び出しは一旦破棄
		if (sysStateCode == SystemStateCode.Shop_CallEventShop)
			return;

		switch (begintype)
		{
			case BeginType.SHOP:
				if (sysStateCode == SystemStateCode.Normal)
					calledWhenNormal = true;
				else
					calledWhenNormal = false;
				sysStateCode = SystemStateCode.Shop_Begin;
				break;
			case BeginType.TRAIN:
				sysStateCode = SystemStateCode.Train_Begin;
				break;
			case BeginType.AFTERTRAIN:
				sysStateCode = SystemStateCode.AfterTrain_Begin;
				break;
			case BeginType.ABLUP:
				sysStateCode = SystemStateCode.Ablup_Begin;
				break;
			case BeginType.TURNEND:
				sysStateCode = SystemStateCode.Turnend_Begin;
				break;
			case BeginType.FIRST:
				sysStateCode = SystemStateCode.First_Begin;
				break;
			case BeginType.TITLE:
				sysStateCode = SystemStateCode.Title_Begin;
				break;
				//セット時に判定してるので、ここには来ないはず
				//default:
				//    throw new ExeEE("不適当なBEGIN呼び出し");
		}
		if (Program.DebugMode)
		{
			console.DebugClearTraceLog();
			console.DebugAddTraceLog("BEGIN:" + begintype.ToString());
		}
		foreach (CalledFunction called in functionList)
			if (called.CurrentLabel.hasPrivDynamicVar)
				called.CurrentLabel.ScopeOut();
		while (_contextStack.Count > 0)
		{
			var ctx = _contextStack.Pop();
			ctx.Dispose();
		}
		functionList.Clear();
		begintype = BeginType.NULL;
		return;
	}
	/// <param name="type"></param>
	public void Begin(BeginType type)
	{
		begintype = type;
		sysStateCode = SystemStateCode.Title_Begin;
		Begin();
	}

	public LogicalLine GetCurrentReturnAddress
	{
		get
		{
			if (functionList.Count == currentMin)
				return null;
			return functionList[^1].ReturnAddress;
		}
	}

	public LogicalLine GetReturnAddressSequensial(int curerntDepth)
	{
		if (functionList.Count == currentMin)
			return null;
		return functionList[functionList.Count - curerntDepth - 1].ReturnAddress;
	}

	public string Scope
	{
		get
		{
			//スクリプトの実行中処理からしか呼び出されないので、ここはない…はず
			//if (functionList.Count == 0)
			//{
			//    throw new ExeEE("実行中の関数が存在しません");
			//}
			if (functionList.Count == 0)
				return null;//1756 デバッグコマンドから呼び出されるようになったので
			return functionList[^1].FunctionName;
		}
	}

	public void Return(long ret)
	{
		CalledFunction called = functionList[^1];
		if (IsFunctionMethod && !(called.IsEvent && (called.FunctionName == "BEFORE_THROW" || called.FunctionName == "BEFORE_ERROR")))
		{
			if (GameProcProcess.DebugLogEnabled) GameProcProcess.DebugLog(string.Format("[Return] IsFunctionMethod fast path, called={0} IsEvent={1}, delegating to ReturnF\n",
				called.FunctionName, called.IsEvent));
			ReturnF(null);
			return;
		}
		if (GameProcProcess.DebugLogEnabled) GameProcProcess.DebugLog(string.Format("[Return] called={0} IsEvent={1} IsJump={2} functionList.Count={3} inBeforeError={4} pendingErrorException={5} pendingThrowMessage={6}\n",
			called.FunctionName, called.IsEvent, called.IsJump, functionList.Count, inBeforeError, pendingErrorException != null, pendingThrowMessage != null));
		if (called.IsJump)
		{
			if (called.TopLabel.hasPrivDynamicVar)
				called.TopLabel.ScopeOut();
			var popped = PopContext();
			popped?.Dispose();
			functionList.Remove(called);
			if (Program.DebugMode)
				console.DebugRemoveTraceLog();
			Return(ret);
			return;
		}
		if (!called.IsEvent)
		{
			if (called.TopLabel.hasPrivDynamicVar)
				called.TopLabel.ScopeOut();
			var popped = PopContext();
			popped?.Dispose();
			currentLine = null;
		}
		else
		{
			if (called.CurrentLabel.hasPrivDynamicVar)
				called.CurrentLabel.ScopeOut();
			var popped = PopContext();
			popped?.Dispose();
			if (called.IsOnly)
				called.FinishEvent();
			else if (called.HasSingleFlag && ret == 1)
				called.ShiftNextGroup();
			else
				called.ShiftNext();
			currentLine = called.CurrentLabel;
			if (called.CurrentLabel != null)
			{
				lineCount++;
				if (called.CurrentLabel.hasPrivDynamicVar)
					called.CurrentLabel.ScopeIn();
				PushContext(new ExecutionContext(called.CurrentLabel, CurrentContext));
			}
		}
		if (Program.DebugMode)
			console.DebugRemoveTraceLog();
		//関数終了
		if (currentLine == null)
		{
			if (GameProcProcess.DebugLogEnabled) GameProcProcess.DebugLog(string.Format("[Return-関数終了] called={0} IsEvent={1} pendingThrowMessage={2} pendingErrorException={3}\n",
				called.FunctionName, called.IsEvent, pendingThrowMessage != null, pendingErrorException != null));
			// BEFORE_ERROR / BEFORE_THROW special handling
			if (called.IsEvent && called.FunctionName == "BEFORE_THROW")
			{
				if (pendingThrowMessage != null)
				{
					string msg = pendingThrowMessage;
					functionList.RemoveAt(functionList.Count - 1);
					pendingThrowMessage = null;
					inBeforeThrow = false;
					skipBeforeError = true;
					throw new CodeEE(msg, pendingThrowLine?.Position);
				}
				inBeforeThrow = false;
			}
			if (called.IsEvent && called.FunctionName == "BEFORE_ERROR")
			{
				functionList.RemoveAt(functionList.Count - 1);
				inBeforeError = true;
				if (pendingThrowMessage != null)
				{
					string msg = pendingThrowMessage;
					ScriptPosition? pos = pendingThrowLine?.Position;
					pendingThrowMessage = null;
					pendingThrowLine = null;
					pendingErrorException = null;
					throw new CodeEE(msg, pos);
				}
				if (pendingErrorException != null)
				{
					Exception ec = pendingErrorException;
					ScriptPosition? pos = (ec is EmueraException ee) ? ee.Position : pendingErrorCurrentLine?.Position;
					pendingErrorException = null;
					throw new CodeEE(ec.Message, pos);
				}
				pendingErrorException = null;
				throw new CodeEE("BEFORE_ERROR finished but no pending error");
			}
			currentLine = called.ReturnAddress;
			functionList.RemoveAt(functionList.Count - 1);
			if (GameProcProcess.DebugLogEnabled) GameProcProcess.DebugLog(string.Format("[Return-関数終了] removed from list, currentLine={0} functionList.Count={1}\n",
				currentLine != null ? currentLine.Position.ToString() : "null", functionList.Count));
			if (currentLine == null)
			{
				//この時点でfunctionListは空のはず
				//functionList.Clear();//全て終了。stateEndProcessに処理を返す
				if (begintype != BeginType.NULL)//BEGIN XXが行なわれていれば
				{
					Begin();
				}
				return;
			}
			lineCount++;
			//ShfitNextLine();
			return;
		}
		else if (Program.DebugMode)
		{
			FunctionLabelLine label = called.CurrentLabel;
			if (label != null && currentLine != null && currentLine.Position.HasValue)
			{
				console.DebugAddTraceLog(string.Format(trsl.DebugTraceCall.Text, label.LabelName, label.Position.Value.Filename, label.Position.Value.LineNo, currentLine.Position.Value.LineNo));
			}
		}
		if (GameProcProcess.DebugLogEnabled) GameProcProcess.DebugLog(string.Format("[Return] event continued, currentLine={0} Label={1}\n",
			currentLine != null ? currentLine.Position.ToString() : "null",
			called.CurrentLabel != null ? called.CurrentLabel.LabelName : "null"));
		lineCount++;
		//ShfitNextLine();
		return;
	}

	public void IntoFunction(CalledFunction call, UserDefinedFunctionArgument srcArgs, ExpressionMediator exm)
	{
		if (GameProcProcess.DebugLogEnabled) GameProcProcess.DebugLog(string.Format("[IntoFunction] name={0} IsEvent={1} IsJump={2} functionList.Count before={3}\n",
			call.FunctionName, call.IsEvent, call.IsJump, functionList.Count));

		if (call.IsEvent)
		{
			bool isBeforeEvent = call.FunctionName == "BEFORE_THROW" || call.FunctionName == "BEFORE_ERROR";
			if (!isBeforeEvent)
			{
				foreach (CalledFunction called in functionList)
				{
					if (called.IsEvent)
						throw new CodeEE(trerror.CalleventBeforeFinishEvent.Text);
				}
			}
		}
		if (Program.DebugMode)
		{
			FunctionLabelLine label = call.CurrentLabel;
			if (exm != null)
				{
					long callingLineNo = -1; // 默认值，表示无法获取
					// 添加空值检查：确保 exm.Process、exm.Process.getCurrentLine、
					// currentProcLine.Position 及其 Value 都不为 null
					LogicalLine currentProcLine = exm.Process.getCurrentLine;
					if (currentProcLine != null && currentProcLine.Position.HasValue) // 使用 HasValue 检查 ScriptPosition?
					{
						callingLineNo = currentProcLine.Position.Value.LineNo;
					}

					if (call.IsJump)
						console.DebugAddTraceLog(string.Format(trsl.DebugTraceJump2.Text, label.LabelName, label.Position.Value.Filename, label.Position.Value.LineNo, callingLineNo));
					else
						console.DebugAddTraceLog(string.Format(trsl.DebugTraceCall2.Text, label.LabelName, label.Position.Value.Filename, label.Position.Value.LineNo, callingLineNo));
				}
				else // 这个 else 分支也需要检查 call.ReturnAddress.ParentLabelLine.Position
				{
					if (call.IsJump)
						console.DebugAddTraceLog(string.Format(trsl.DebugTraceJump.Text, label.LabelName, label.Position.Value.Filename, label.Position.Value.LineNo));
					else
					{
						string trace = $"CALL @{label.LabelName}:{label.Position.Value.Filename}:{label.Position.Value.LineNo}";
						// 同样为 call.ReturnAddress.ParentLabelLine.Position 添加空值检查
						if (call.ReturnAddress != null && call.ReturnAddress.ParentLabelLine != null && call.ReturnAddress.ParentLabelLine.Position.HasValue) // 使用 HasValue
							trace += $" at @{call.ReturnAddress.ParentLabelLine.LabelName}:{call.ReturnAddress.ParentLabelLine.Position.Value.Filename}:{call.ReturnAddress.Position.Value.LineNo}";
						console.DebugAddTraceLog(trace);
					}
				}
		}
		if (srcArgs != null)
			srcArgs.SetTransporter(exm);
		var ctx = new ExecutionContext(call.TopLabel, CurrentContext);
		PushContext(ctx);

		if (srcArgs != null && call.TopLabel.VariadicArgIndex >= 0)
		{
			var variadicArg = srcArgs.Arguments[call.TopLabel.VariadicArgIndex] as VariadicArgTerm;
			if (variadicArg != null)
			{
				VariableTerm destArg = call.TopLabel.Arg[call.TopLabel.VariadicArgIndex];
				int requiredSize = destArg.getEl1forArg + variadicArg.Count;
				if (destArg.Identifier.Code == VariableCode.ARG && (ctx.ArgIntegers == null || requiredSize > ctx.ArgIntegers.Length))
				{
					long[] newArr = new long[requiredSize];
					if (ctx.ArgIntegers != null) Array.Copy(ctx.ArgIntegers, newArr, ctx.ArgIntegers.Length);
					ctx.ArgIntegers = newArr;
				}
				else if (destArg.Identifier.Code == VariableCode.ARGS && (ctx.ArgStrings == null || requiredSize > ctx.ArgStrings.Length))
				{
					string[] newArr = new string[requiredSize];
					if (ctx.ArgStrings != null) Array.Copy(ctx.ArgStrings, newArr, ctx.ArgStrings.Length);
					ctx.ArgStrings = newArr;
				}
				else if (destArg.Identifier.Code == VariableCode.ARGF && (ctx.ArgFloats == null || requiredSize > ctx.ArgFloats.Length))
				{
					double[] newArr = new double[requiredSize];
					if (ctx.ArgFloats != null) Array.Copy(ctx.ArgFloats, newArr, ctx.ArgFloats.Length);
					ctx.ArgFloats = newArr;
				}
			}
		}

		if (srcArgs != null)
		{
			if (call.TopLabel.hasPrivDynamicVar)
				call.TopLabel.ScopeIn();
			for (int i = 0; i < call.TopLabel.Arg.Length; i++)
			{
				if (srcArgs.Arguments[i] != null)
				{
					if (call.TopLabel.Arg[i].Identifier.IsReference)
					{
						if (!srcArgs.TransporterElementRef[i].IsNull)
							((ReferenceToken)call.TopLabel.Arg[i].Identifier).SetRef(srcArgs.TransporterElementRef[i]);
						else if (srcArgs.TransporterRef[i] != null)
							((ReferenceToken)call.TopLabel.Arg[i].Identifier).SetRef(srcArgs.TransporterRef[i]);
						else if (call.TopLabel.Arg[i].Identifier.IsOut)
							((ReferenceToken)call.TopLabel.Arg[i].Identifier).SetNullRef();
					}
					else if (srcArgs.Arguments[i] is VariadicArgTerm variadic)
					{
						int baseIdx = call.TopLabel.Arg[i].getEl1forArg;
						call.VariadicArgCount = variadic.Count;
						bool destIsFloat = call.TopLabel.Arg[i].GetEraType() == EraType.Float;
						for (int j = 0; j < variadic.Count; j++)
						{
							var arg = variadic[j];
							if (arg == null) continue;
							if (destIsFloat && arg.GetEraType() == EraType.Integer)
								call.TopLabel.Arg[i].Identifier.SetValue((double)arg.GetIntValue(exm), [baseIdx + j]);
							else if (arg.GetEraType() == EraType.Integer)
								call.TopLabel.Arg[i].Identifier.SetValue(arg.GetIntValue(exm), [baseIdx + j]);
							else if (arg.GetEraType() == EraType.Float)
								call.TopLabel.Arg[i].Identifier.SetValue(arg.GetFloatValue(exm), [baseIdx + j]);
							else
								call.TopLabel.Arg[i].Identifier.SetValue(arg.GetStrValue(exm), [baseIdx + j]);
						}
					}
					else if (call.TopLabel.Arg[i].GetEraType() == EraType.Float)
					{
						if (srcArgs.Arguments[i].GetEraType() == EraType.Integer)
							call.TopLabel.Arg[i].SetValue((double)srcArgs.TransporterInt[i], exm);
						else
							call.TopLabel.Arg[i].SetValue(srcArgs.TransporterFloat[i], exm);
					}
					else if (call.TopLabel.Arg[i].GetEraType() == EraType.Integer)
					{
						if (srcArgs.Arguments[i].GetEraType() == EraType.Float)
							call.TopLabel.Arg[i].SetValue((long)srcArgs.TransporterFloat[i], exm);
						else
							call.TopLabel.Arg[i].SetValue(srcArgs.TransporterInt[i], exm);
					}
					else
						call.TopLabel.Arg[i].SetValue(srcArgs.TransporterStr[i], exm);
				}
			}
		}
		else
		{
			if (call.TopLabel.hasPrivDynamicVar)
				call.TopLabel.ScopeIn();
		}
		functionList.Add(call);
		if (GameProcProcess.DebugLogEnabled) GameProcProcess.DebugLog(string.Format("[IntoFunction] added, functionList.Count after={0}\n", functionList.Count));
		//sequential = false;
		currentLine = call.CurrentLabel;
		lineCount++;
		//ShfitNextLine();
	}

	#region userdifinedmethod
	public bool IsFunctionMethod
	{
		get
		{
			if (currentMin >= functionList.Count)
				return false;
			return functionList[currentMin].TopLabel.IsMethod;
		}
	}

	public SingleTerm MethodReturnValue;

	public void ReturnF(SingleTerm ret)
	{
		if (GameProcProcess.DebugLogEnabled) GameProcProcess.DebugLog(string.Format("[ReturnF] called={0} functionList.Count={1}\n",
			functionList.Count > 0 ? functionList[^1].FunctionName : "empty", functionList.Count));
		if (functionList.Count == 0)
			return;
		//読み込み時のチェック済みのはず
		//if (!IsFunctionMethod)
		//    throw new ExeEE("ReturnFと#FUNCTIONのチェックがおかしい");
		//sequential = false;//いずれにしろ順列ではない。
		//呼び出し元はRETURNFコマンドか関数終了時のみ
		//if (functionList.Count == 0)
		//    throw new ExeEE("実行中の関数が存在しません");
		//非イベント呼び出しなので、これは起こりえない
		//else if (functionList.Count != 1)
		//    throw new ExeEE("関数が複数ある");
		if (Program.DebugMode)
		{
			console.DebugRemoveTraceLog();
		}
		//OutはGetValue側で行う
		//functionList[0].TopLabel.Out();
		currentLine = functionList[^1].ReturnAddress;
		functionList.RemoveAt(functionList.Count - 1);
		//nextLine = null;
		MethodReturnValue = ret;
		return;
	}

	#endregion

	bool isClone;
	public bool IsClone { get { return isClone; } set { isClone = value; } }

	// functionListのコピーを必要とする呼び出し元が無かったのでコピーしないことにする。
	public ProcessState Clone()
	{
		ProcessState ret = new(console)
		{
			isClone = true,
			//どうせ消すからコピー不要
			//foreach (CalledFunction func in functionList)
			//	ret.functionList.Add(func.Clone());
			currentLine = currentLine,
			//ret.nextLine = this.nextLine;
			//ret.sequential = this.sequential;
			sysStateCode = sysStateCode,
			begintype = begintype
		};
		ret._savedContextStack = _contextStack;
		//ret.MethodReturnValue = this.MethodReturnValue;
		return ret;

	}
	//public ProcessState CloneForFunctionMethod()
	//{
	//    ProcessState ret = new ProcessState(console);
	//    ret.isClone = true;

	//    //どうせ消すからコピー不要
	//    //foreach (CalledFunction func in functionList)
	//    //	ret.functionList.Add(func.Clone());
	//    ret.currentLine = this.currentLine;
	//    ret.nextLine = this.nextLine;
	//    //ret.sequential = this.sequential;
	//    ret.sysStateCode = this.sysStateCode;
	//    ret.begintype = this.begintype;
	//    //ret.MethodReturnValue = this.MethodReturnValue;
	//    return ret;
	//}
}
