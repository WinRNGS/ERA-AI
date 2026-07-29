using MinorShift.Emuera.GameView;
using MinorShift.Emuera.Runtime.Utils;
using System;
using System.Threading;

namespace MinorShift.Emuera.AI;

/// <summary>
/// AI 请求的状态机。
/// </summary>
internal enum AiRequestState
{
	Idle = 0,
	Locked = 1,
	Aborting = 2,
}

/// <summary>
/// 硬锁定（Request Lock）。
///
/// 设计依据：Emuera 的脚本执行与全部输入入口都跑在界面线程上，且 8 个界面输入入口与调试对话框
/// 统一检查 EmueraConsole.IsInProcess。因此主守卫只需让 IsInProcess 在 AI 请求期间返回 true。
///
/// 但仅改 IsInProcess 不够，必须同时封堵四个旁路，漏一个锁就形同虚设：
///   1. 限时输入的后台计时器（会在 AI 回复期间超时并自动提交默认值）→ PauseTimer / ResumeTimer
///   2. 键盘状态与 latch 数组（不经过状态机，锁定期间按键会留下触发记录）→ ClearLatches / ResetAllKeys
///   3. 按钮世代（锁定前渲染的旧按钮点击仍然有效）→ forceUpdateGeneration
///   4. 内部输入回放路径（几处绕过界面入口直接驱动输入）→ 在 EmueraConsole 核心入口二次校验
///
/// 除 IsLocked 之外的所有成员都只允许在界面线程调用。
/// </summary>
internal static class AiRequestLock
{
	private static int state = (int)AiRequestState.Idle;

	private static EmueraConsole lockedConsole;
	private static bool timerWasRunning;
	private static CancellationTokenSource cts;
	private static long currentTicket;

	/// <summary>
	/// 锁定中。可从任意线程读取（IsInProcess 会在界面线程频繁访问）。
	/// </summary>
	public static bool IsLocked
	{
		get
		{
			int s = Volatile.Read(ref state);
			return s == (int)AiRequestState.Locked || s == (int)AiRequestState.Aborting;
		}
	}

	/// <summary>已请求终止，但请求尚未收尾。</summary>
	public static bool IsAborting => Volatile.Read(ref state) == (int)AiRequestState.Aborting;

	public static AiRequestState State => (AiRequestState)Volatile.Read(ref state);

	/// <summary>当前请求的票号，用于丢弃过期回调。0 表示无请求。</summary>
	public static long CurrentTicket => Interlocked.Read(ref currentTicket);

	/// <summary>
	/// 尝试获取锁。必须在界面线程调用。
	/// 成功返回票号（>0）与取消令牌；失败返回 0。
	/// </summary>
	public static long TryAcquire(EmueraConsole console, out CancellationToken token)
	{
		token = CancellationToken.None;
		if (console == null)
			return 0;
		AssertUiThread(console);

		if (Interlocked.CompareExchange(ref state, (int)AiRequestState.Locked, (int)AiRequestState.Idle) != (int)AiRequestState.Idle)
			return 0;

		lockedConsole = console;

		// 旁路 1：暂停限时输入计时器，防止 AI 回复期间超时自动提交默认值。
		timerWasRunning = console.PauseTimer();

		// 旁路 2（进入时）：清掉进入锁定前残留的按键 latch，避免解锁后被脚本读到。
		WinInput.ClearLatches();

		cts = new CancellationTokenSource();
		token = cts.Token;
		return Interlocked.Increment(ref currentTicket);
	}

	/// <summary>
	/// 请求终止当前请求。可从界面线程调用（例如 Esc 键或终止按钮）。
	/// 真正的解锁仍由 Release 完成。
	/// </summary>
	public static bool RequestAbort()
	{
		if (Interlocked.CompareExchange(ref state, (int)AiRequestState.Aborting, (int)AiRequestState.Locked) != (int)AiRequestState.Locked)
			return false;
		try
		{
			cts?.Cancel();
		}
		catch (ObjectDisposedException)
		{
		}
		return true;
	}

	/// <summary>
	/// 释放锁。必须在界面线程调用，且必须在任何异常路径上都能到达，否则界面永久锁死。
	/// </summary>
	public static void Release(long ticket)
	{
		if (ticket == 0 || Interlocked.Read(ref currentTicket) != ticket)
			return;
		if (Volatile.Read(ref state) == (int)AiRequestState.Idle)
			return;

		EmueraConsole console = lockedConsole;
		try
		{
			if (console != null)
			{
				// 旁路 2（退出时）：清除锁定期间累积的按键状态、toggle 与 latch。
				WinInput.ResetAllKeys();
				// 旁路 3：作废按钮世代，让锁定前渲染的旧按钮点击失效。
				console.forceUpdateGeneration();
			}
		}
		finally
		{
			try
			{
				// 旁路 1（恢复）：仅当此前确实在运行且仍处于限时输入等待时才恢复计时器。
				console?.ResumeTimer(timerWasRunning);
			}
			finally
			{
				lockedConsole = null;
				timerWasRunning = false;
				try
				{
					cts?.Dispose();
				}
				catch (ObjectDisposedException)
				{
				}
				cts = null;
				Volatile.Write(ref state, (int)AiRequestState.Idle);
			}
		}

		// 解锁后统一整段刷新一次，避免默认低帧率下界面停留在旧内容。
		console?.RefreshStrings(true);
	}

	/// <summary>
	/// 旁路 4 的守卫：核心输入入口在锁定期间必须拒绝执行。
	/// </summary>
	public static bool ShouldRejectInput() => IsLocked;

	private static void AssertUiThread(EmueraConsole console)
	{
		var window = console.Window;
		if (window != null && window.Created && window.InvokeRequired)
			throw new InvalidOperationException("AiRequestLock 只能在界面线程调用。");
	}
}