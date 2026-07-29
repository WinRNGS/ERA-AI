using MinorShift.Emuera.GameView;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace MinorShift.Emuera.AI;

/// <summary>
/// AI 请求调度器。整套方案的地基。
///
/// 线程契约（违反任意一条都会破坏 ERA 数据结构）：
///   - 网络与解析：后台线程。只产出 AiTurnResult，绝不触碰变量或界面。
///   - 结果回注：必须切回界面线程。做法照抄引擎限时输入计时器超时后的写法，即 window.Invoke。
///   - 锁的获取与释放：界面线程。任何异常路径都必须走到 Release，否则界面永久锁死。
/// </summary>
internal static class AiDispatcher
{
	/// <summary>回注完成后的通知，供 AI 面板刷新用。在界面线程触发。</summary>
	public static event Action<AiTurnResult> TurnCompleted;

	/// <summary>P0 自检开关：为 true 时使用假数据而不发真实网络请求。</summary>
	public static bool UseFakeBackend = true;

	/// <summary>假后端的模拟耗时，用于验证锁定期间界面不冻结。</summary>
	public static int FakeDelayMs = 1500;

	private static readonly object logGate = new();
	private static readonly List<string> log = [];

	public static IReadOnlyList<string> Log
	{
		get
		{
			lock (logGate)
				return log.ToArray();
		}
	}

	private static void Append(string line)
	{
		lock (logGate)
		{
			log.Add($"[{DateTime.Now:HH:mm:ss.fff}] {line}");
			if (log.Count > 500)
				log.RemoveRange(0, log.Count - 500);
		}
	}

	/// <summary>
	/// 发起一次 AI 请求。必须在界面线程调用。
	/// 返回 false 表示锁被占用或环境未就绪，调用方不应重试。
	/// </summary>
	public static bool TryBeginTurn(EmueraConsole console, string userInput)
	{
		if (console == null)
			return false;

		long ticket = AiRequestLock.TryAcquire(console, out CancellationToken token);
		if (ticket == 0)
		{
			Append("拒绝：已有请求进行中（硬锁定生效）");
			return false;
		}

		Append($"请求开始 ticket={ticket} input={Truncate(userInput, 40)}");
		var stopwatch = Stopwatch.StartNew();

		_ = Task.Run(async () =>
		{
			AiTurnResult result;
			try
			{
				result = UseFakeBackend
					? await RunFakeBackendAsync(ticket, userInput, token).ConfigureAwait(false)
					: await RunRealBackendAsync(ticket, userInput, token).ConfigureAwait(false);
			}
			catch (OperationCanceledException)
			{
				result = new AiTurnResult { Ticket = ticket, Success = false, Aborted = true, ErrorMessage = "已被玩家终止" };
			}
			catch (Exception e)
			{
				result = new AiTurnResult { Ticket = ticket, Success = false, ErrorMessage = e.Message };
			}
			result.ElapsedMs = stopwatch.ElapsedMilliseconds;
			CompleteOnUiThread(console, result);
		});

		return true;
	}

	/// <summary>请求终止当前请求。必须在界面线程调用。</summary>
	public static bool Abort()
	{
		bool ok = AiRequestLock.RequestAbort();
		Append(ok ? "收到终止请求" : "终止请求被忽略（当前无进行中的请求）");
		return ok;
	}

	/// <summary>
	/// 把结果切回界面线程完成收尾。这是唯一允许写变量与打印的地方。
	/// </summary>
	private static void CompleteOnUiThread(EmueraConsole console, AiTurnResult result)
	{
		var window = console.Window;
		if (window == null || !window.Created)
		{
			// 窗口已销毁，无法切回界面线程；此时不写变量，只解锁以免状态残留。
			AiRequestLock.Release(result.Ticket);
			return;
		}

		Action finish = () => ApplyOnUiThread(console, result);
		try
		{
			if (window.InvokeRequired)
				window.BeginInvoke(finish);
			else
				finish();
		}
		catch (Exception e)
		{
			Append($"回注调度失败：{e.Message}");
			AiRequestLock.Release(result.Ticket);
		}
	}

	private static void ApplyOnUiThread(EmueraConsole console, AiTurnResult result)
	{
		try
		{
			if (result.Ticket != AiRequestLock.CurrentTicket)
			{
				Append($"丢弃过期结果 ticket={result.Ticket}");
				return;
			}

			bool aborted = result.Aborted || AiRequestLock.IsAborting;
			if (aborted)
			{
				Append($"请求终止 ticket={result.Ticket}，不写入任何数值");
				result.Success = false;
				result.Aborted = true;
				return;
			}

			if (!result.Success)
			{
				Append($"请求失败 ticket={result.Ticket}：{result.ErrorMessage}");
				return;
			}

			if (!AiVariableAccess.TryApplyAll(result.Changes, out string error))
			{
				// 整批拒绝：宁可不写，也不让幻觉数值污染存档。
				result.Success = false;
				result.ErrorMessage = error;
				Append($"数值回写被拒绝 ticket={result.Ticket}：{error}");
				return;
			}

			Append($"请求完成 ticket={result.Ticket}，写入 {result.Changes.Count} 项，耗时 {result.ElapsedMs}ms");
		}
		catch (Exception e)
		{
			result.Success = false;
			result.ErrorMessage = e.Message;
			Append($"回注异常 ticket={result.Ticket}：{e.Message}");
		}
		finally
		{
			// 无论如何都必须解锁。
			AiRequestLock.Release(result.Ticket);
			try
			{
				TurnCompleted?.Invoke(result);
			}
			catch (Exception e)
			{
				Append($"完成通知异常：{e.Message}");
			}
		}
	}

	/// <summary>
	/// P0 假后端：不发网络请求，只模拟延迟并产出可校验的假数据。
	/// 目的是在不牵扯业务逻辑的前提下单独验证线程时序与锁的正确性。
	/// </summary>
	private static async Task<AiTurnResult> RunFakeBackendAsync(long ticket, string userInput, CancellationToken token)
	{
		await Task.Delay(FakeDelayMs, token).ConfigureAwait(false);
		return new AiTurnResult
		{
			Ticket = ticket,
			Success = true,
			NarrativeText = $"[假数据] 收到输入「{Truncate(userInput, 60)}」，这是一段用于验证回注链路的占位正文。",
			Changes =
			[
				new AiValueChange { Target = "FLAG:0", Op = "add", Value = 1 },
			],
		};
	}

	/// <summary>
	/// 真实后端占位。P1 起接入主 API，P3 起接入副 API。
	/// 实现时只允许使用 BCL 自带的 HttpClient 与 System.Text.Json，不得新增 NuGet 依赖
	/// （本机无法访问 nuget.org，只能离线还原）。
	/// </summary>
	private static Task<AiTurnResult> RunRealBackendAsync(long ticket, string userInput, CancellationToken token)
	{
		return Task.FromResult(new AiTurnResult
		{
			Ticket = ticket,
			Success = false,
			ErrorMessage = "真实后端尚未接入（P1 阶段实现）",
		});
	}

	private static string Truncate(string value, int max)
	{
		if (string.IsNullOrEmpty(value))
			return "";
		return value.Length <= max ? value : value[..max] + "…";
	}
}