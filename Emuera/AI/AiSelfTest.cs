using MinorShift.Emuera.GameView;
using MinorShift.Emuera.Runtime.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace MinorShift.Emuera.AI;

/// <summary>
/// P0 自动自检。
///
/// 由环境变量 ERA_AI_SELFTEST=1 触发，全程在界面线程上跑，结束后把报告写到
/// ERA_AI_SELFTEST_REPORT 指定的文件（默认 exe 目录下 ai_selftest.txt）并关闭窗口。
/// 正常运行时该类完全不激活，因此不会影响玩家体验。
///
/// 验收目标（对应设计文档 P0）：
///   - 触发 → 异步等待 → 结果回注 → 变量写入 全链路可跑通
///   - 锁定期间无法制造第二次请求
///   - 四项旁路（计时器 / 键盘 latch / 按钮世代 / 内部输入回放）确实被封堵
///   - 任何异常路径都必定解锁
///   - 数值校验能拦住非法写入
/// </summary>
internal static class AiSelfTest
{
	private const string EnableEnv = "ERA_AI_SELFTEST";
	private const string ReportEnv = "ERA_AI_SELFTEST_REPORT";

	private static readonly List<string> lines = [];
	private static int passed;
	private static int failed;

	private static System.Windows.Forms.Timer pollTimer;
	private static EmueraConsole target;
	private static int elapsedMs;
	private static bool finished;

	public static bool IsEnabled =>
		string.Equals(Environment.GetEnvironmentVariable(EnableEnv), "1", StringComparison.Ordinal);

	/// <summary>
	/// 挂上轮询计时器，等到脚本进入输入等待、且表达式求值器就绪后再开跑。
	/// </summary>
	public static void Arm(EmueraConsole console)
	{
		Trace($"Arm called. enabled={IsEnabled} console={console != null}");
		if (!IsEnabled || console == null)
			return;
		target = console;
		pollTimer = new System.Windows.Forms.Timer { Interval = 200 };
		pollTimer.Tick += Poll;
		pollTimer.Start();
	}

	private static void Poll(object sender, EventArgs e)
	{
		elapsedMs += 200;
		if (finished)
			return;

		bool ready = GlobalStatic.EMediator != null && target.IsWaitInputState;
		if (elapsedMs % 1000 == 0)
			Trace($"poll {elapsedMs}ms ready={ready} emediator={GlobalStatic.EMediator != null} waitInput={target.IsWaitInputState} inProcess={target.IsInProcess}");
		if (!ready)
		{
			if (elapsedMs < 30000)
				return;
			Log("FATAL", "等待脚本进入输入等待状态超时，自检无法进行。");
			Finish();
			return;
		}

		pollTimer.Stop();
		RunAll();
	}

	private static void RunAll()
	{
		try
		{
			Section("A 组：锁的基本行为");
			TestInitialStateIsIdle();
			TestAcquireBlocksSecondAcquire();
			TestReleaseRestoresState();
			TestStaleTicketCannotRelease();

			Section("B 组：四项旁路封堵");
			TestTimerPaused();
			TestKeyLatchCleared();
			TestButtonGenerationInvalidated();
			TestInternalInputReplayRejected();

			Section("C 组：数值校验");
			TestValidationRejectsUnknownVariable();
			TestValidationRejectsNonWhitelisted();
			TestValidationRejectsConstant();
			TestValidationRejectsOutOfRange();
			TestValidationRejectsBadOperator();
			TestBatchIsAtomic();

			Section("D 组：完整异步链路（假后端）");
			StartAsyncRoundTrip();
			return;
		}
		catch (Exception ex)
		{
			Log("FATAL", $"自检自身抛出异常：{ex}");
		}
		Finish();
	}

	// ---------- A 组 ----------

	private static void TestInitialStateIsIdle()
	{
		Check("初始状态为 Idle", AiRequestLock.State == AiRequestState.Idle);
		Check("初始未锁定", !AiRequestLock.IsLocked);
		Check("初始 IsInProcess 为 false（脚本正在等待输入）", !target.IsInProcess);
	}

	private static void TestAcquireBlocksSecondAcquire()
	{
		long first = AiRequestLock.TryAcquire(target, out _);
		Check("首次取锁成功", first > 0);
		Check("取锁后 IsLocked 为 true", AiRequestLock.IsLocked);
		Check("取锁后 IsInProcess 被主守卫置为 true", target.IsInProcess);

		long second = AiRequestLock.TryAcquire(target, out _);
		Check("锁定期间二次取锁被拒绝", second == 0);

		AiRequestLock.Release(first);
	}

	private static void TestReleaseRestoresState()
	{
		long ticket = AiRequestLock.TryAcquire(target, out _);
		AiRequestLock.Release(ticket);
		Check("释放后状态回到 Idle", AiRequestLock.State == AiRequestState.Idle);
		Check("释放后 IsInProcess 恢复为 false", !target.IsInProcess);
		Check("释放后可再次取锁", ReacquireAndRelease());
	}

	private static bool ReacquireAndRelease()
	{
		long ticket = AiRequestLock.TryAcquire(target, out _);
		if (ticket == 0)
			return false;
		AiRequestLock.Release(ticket);
		return true;
	}

	private static void TestStaleTicketCannotRelease()
	{
		long ticket = AiRequestLock.TryAcquire(target, out _);
		AiRequestLock.Release(ticket - 1);
		bool stillLocked = AiRequestLock.IsLocked;
		AiRequestLock.Release(ticket);
		Check("过期票号无法解锁（防止旧回调误解锁新请求）", stillLocked);
	}

	// ---------- B 组 ----------

	private static void TestTimerPaused()
	{
		// 引擎的限时输入计时器会在 AI 回复期间超时并自动提交默认值。
		// 前置条件：自检脚本使用 TINPUTS 带超时，因此进入这里时计时器必须确实在跑，
		// 否则「取锁后计时器已停」只是侥幸通过而非真的封堵生效。
		bool runningBefore = target.IsRunningTimer;
		Check("前置条件：进入测试时限时输入计时器确实在运行", runningBefore);

		long ticket = AiRequestLock.TryAcquire(target, out _);
		bool stillRunning = target.PauseTimer();
		Check("取锁时已暂停限时输入计时器", !stillRunning);

		AiRequestLock.Release(ticket);
		Check("解锁后限时输入计时器已恢复", target.IsRunningTimer == runningBefore);
	}

	private static void TestKeyLatchCleared()
	{
		long ticket = AiRequestLock.TryAcquire(target, out _);
		// 模拟锁定期间玩家按键：latch 会被记录，若不清除，解锁后会被脚本读到。
		WinInput.SetKeyPressed(0x41); // 'A'
		bool latchedDuringLock = WinInput.ConsumeKeyLatch(0x41) == 1;
		WinInput.SetKeyPressed(0x41);
		AiRequestLock.Release(ticket);
		bool latchAfterRelease = WinInput.ConsumeKeyLatch(0x41) == 1;
		Check("锁定期间按键确实会留下 latch（说明该旁路真实存在）", latchedDuringLock);
		Check("解锁时已清除按键 latch", !latchAfterRelease);
		Check("解锁时已清除按键按下状态", WinInput.GetKeyState(0x41) == 0);
	}

	private static void TestButtonGenerationInvalidated()
	{
		// 无法直接读按钮世代，退而验证 Release 调用链不抛异常且状态干净。
		// forceUpdateGeneration 的效果由「解锁后旧按钮点击失效」保证，此处只做冒烟。
		long ticket = AiRequestLock.TryAcquire(target, out _);
		bool ok = true;
		try
		{
			AiRequestLock.Release(ticket);
		}
		catch (Exception e)
		{
			ok = false;
			Log("INFO", $"作废按钮世代时抛出异常：{e.Message}");
		}
		Check("解锁时作废按钮世代未抛异常", ok);
	}

	private static void TestInternalInputReplayRejected()
	{
		// 旁路 4：MainWindow 内有多处绕过 IsInProcess 直接驱动输入的路径。
		// 这里直接调用被封堵的核心入口，验证锁定期间不会驱动脚本。
		long ticket = AiRequestLock.TryAcquire(target, out _);
		Check("锁定期间 ShouldRejectInput 为 true", AiRequestLock.ShouldRejectInput());

		bool threw = false;
		try
		{
			target.PressEnterKey(false, "自检注入：这一行不应被脚本接收", true);
			target.PressPrimitiveKey(System.Windows.Forms.Keys.A, System.Windows.Forms.Keys.A, System.Windows.Forms.Keys.None);
		}
		catch (Exception e)
		{
			threw = true;
			Log("INFO", $"内部输入回放抛出异常：{e.Message}");
		}
		bool stillWaiting = target.IsWaitInputState;
		AiRequestLock.Release(ticket);

		Check("锁定期间调用内部输入入口不抛异常", !threw);
		Check("锁定期间内部输入未驱动脚本（仍停在输入等待）", stillWaiting);
	}

	// ---------- C 组 ----------

	private static void TestValidationRejectsUnknownVariable()
	{
		var change = new AiValueChange { Target = "根本不存在的变量名XYZ", Op = "set", Value = 1 };
		bool ok = AiVariableAccess.Validate(change, out string error);
		Check($"拒绝未知变量名（{Brief(error)}）", !ok);
	}

	private static void TestValidationRejectsNonWhitelisted()
	{
		// RESULT 是真实存在的可写变量，但属于控制流变量，不在白名单内，必须被拒。
		// 这里刻意不用 DAY：P1 把 DAY 纳入白名单后该断言会失效，换成永远不该开放的控制流变量更稳。
		var change = new AiValueChange { Target = "RESULT:0", Op = "set", Value = 999 };
		bool ok = AiVariableAccess.Validate(change, out string error);
		Check($"拒绝白名单外的真实变量（{Brief(error)}）", !ok);
	}

	private static void TestValidationRejectsConstant()
	{
		var change = new AiValueChange { Target = "GAMEBASE_VERSION", Op = "set", Value = 1 };
		bool ok = AiVariableAccess.Validate(change, out string error);
		Check($"拒绝写入常量（{Brief(error)}）", !ok);
	}

	private static void TestValidationRejectsOutOfRange()
	{
		var change = new AiValueChange { Target = "FLAG:99999999", Op = "set", Value = 1 };
		bool ok = AiVariableAccess.Validate(change, out string error);
		Check($"拒绝下标越界（{Brief(error)}）", !ok);
	}

	private static void TestValidationRejectsBadOperator()
	{
		var change = new AiValueChange { Target = "FLAG:0", Op = "drop_table", Value = 1 };
		bool ok = AiVariableAccess.Validate(change, out string error);
		Check($"拒绝白名单外的操作符（{Brief(error)}）", !ok);
	}

	private static void TestBatchIsAtomic()
	{
		if (!AiVariableAccess.TryReadInt("FLAG:1", out long before, out string readError))
		{
			Log("FATAL", $"读取 FLAG:1 失败：{readError}");
			failed++;
			return;
		}
		var batch = new List<AiValueChange>
		{
			new() { Target = "FLAG:1", Op = "set", Value = before + 42 },
			new() { Target = "FLAG:99999999", Op = "set", Value = 1 },
		};
		bool applied = AiVariableAccess.TryApplyAll(batch, out string error);
		AiVariableAccess.TryReadInt("FLAG:1", out long after, out _);
		Check($"含非法项的批次被整批拒绝（{Brief(error)}）", !applied);
		Check("整批拒绝时合法项也未被写入（原子性）", before == after);
	}

	// ---------- D 组 ----------

	private static long flagBeforeRoundTrip;
	private static int roundTripStage;

	private static void StartAsyncRoundTrip()
	{
		AiDispatcher.UseFakeBackend = true;
		AiDispatcher.FakeDelayMs = 600;
		AiDispatcher.TurnCompleted += OnRoundTripCompleted;

		AiVariableAccess.TryReadInt("FLAG:0", out flagBeforeRoundTrip, out _);
		roundTripStage = 1;

		bool started = AiDispatcher.TryBeginTurn(target, "自检：完整异步链路");
		Check("发起异步请求成功", started);
		Check("请求进行中处于锁定状态", AiRequestLock.IsLocked);
		Check("请求进行中 IsInProcess 为 true", target.IsInProcess);

		bool secondStarted = AiDispatcher.TryBeginTurn(target, "自检：并发第二次请求");
		Check("锁定期间无法发起第二次请求", !secondStarted);
	}

	private static void OnRoundTripCompleted(AiTurnResult result)
	{
		if (roundTripStage == 1)
		{
			Check("回注完成后已解锁", !AiRequestLock.IsLocked);
			Check("回注完成后 IsInProcess 恢复", !target.IsInProcess);
			Check("请求报告成功", result.Success);
			AiVariableAccess.TryReadInt("FLAG:0", out long after, out _);
			Check($"数值已回写（FLAG:0 {flagBeforeRoundTrip} → {after}）", after == flagBeforeRoundTrip + 1);
			Check("耗时符合假后端设定（不小于 500ms，说明确实走了异步等待）", result.ElapsedMs >= 500);

			// 第二阶段：验证终止路径不写数值且必定解锁。
			roundTripStage = 2;
			AiVariableAccess.TryReadInt("FLAG:0", out flagBeforeRoundTrip, out _);
			AiDispatcher.FakeDelayMs = 1500;
			bool started = AiDispatcher.TryBeginTurn(target, "自检：终止路径");
			Check("发起可终止的请求成功", started);
			bool aborted = AiDispatcher.Abort();
			Check("终止请求被接受", aborted);
			Check("终止后状态为 Aborting（收尾前）", AiRequestLock.State == AiRequestState.Aborting);
			return;
		}

		if (roundTripStage == 2)
		{
			Check("终止后已解锁", !AiRequestLock.IsLocked);
			Check("终止结果标记为 Aborted", result.Aborted);
			Check("终止的请求未报告成功", !result.Success);
			AiVariableAccess.TryReadInt("FLAG:0", out long after, out _);
			Check("终止路径未写入任何数值", after == flagBeforeRoundTrip);

			roundTripStage = 3;
			AiDispatcher.TurnCompleted -= OnRoundTripCompleted;
			Finish();
		}
	}

	// ---------- 报告 ----------

	private static void Section(string title)
	{
		lines.Add("");
		lines.Add($"== {title} ==");
	}

	private static void Check(string description, bool condition)
	{
		if (condition)
		{
			passed++;
			lines.Add($"  PASS  {description}");
		}
		else
		{
			failed++;
			lines.Add($"  FAIL  {description}");
		}
	}

	private static void Log(string level, string message) => lines.Add($"  {level}  {message}");

	private static void Trace(string message)
	{
		string path = Environment.GetEnvironmentVariable("ERA_AI_SELFTEST_TRACE");
		if (string.IsNullOrWhiteSpace(path))
			return;
		try
		{
			File.AppendAllText(path, $"[{DateTime.Now:HH:mm:ss.fff}] {message}" + Environment.NewLine, new UTF8Encoding(true));
		}
		catch (Exception)
		{
		}
	}

	private static string Brief(string error)
	{
		if (string.IsNullOrEmpty(error))
			return "无错误信息";
		return error.Length <= 40 ? error : error[..40] + "…";
	}

	private static void Finish()
	{
		if (finished)
			return;
		finished = true;
		pollTimer?.Stop();
		Trace($"Finish called. pass={passed} fail={failed}");

		var sb = new StringBuilder();
		sb.AppendLine("ERA-AI P0 自检报告");
		sb.AppendLine($"时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
		sb.AppendLine($"结果：PASS={passed}  FAIL={failed}");
		foreach (string line in lines)
			sb.AppendLine(line);
		sb.AppendLine();
		sb.AppendLine("---- 调度器日志 ----");
		foreach (string line in AiDispatcher.Log)
			sb.AppendLine("  " + line);
		sb.AppendLine();
		sb.AppendLine(failed == 0 ? "SELFTEST RESULT: OK" : "SELFTEST RESULT: FAILED");

		string path = Environment.GetEnvironmentVariable(ReportEnv);
		if (string.IsNullOrWhiteSpace(path))
			path = Path.Combine(Program.ExeDir, "ai_selftest.txt");
		try
		{
			File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
		}
		catch (Exception)
		{
		}

		target?.Window?.Close();
	}
}