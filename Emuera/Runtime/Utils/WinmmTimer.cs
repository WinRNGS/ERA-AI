using System;
using System.Runtime.InteropServices;

namespace MinorShift.Emuera.Runtime.Utils;

/// <summary>
/// wrapされたtimer。外からは、このTickCountだけを呼び出す。
/// </summary>
internal sealed class WinmmTimer
{
	static WinmmTimer()
	{
#if WINDOWS
		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
		{
			instance = new WinmmTimer();
		}
#endif
	}

#if WINDOWS
	private WinmmTimer()
	{
		var result = mm_BeginPeriod(1);
		if (result != 0)
			System.Diagnostics.Debug.WriteLine($"WinmmTimer: timeBeginPeriod failed with result {result}");
	}
	~WinmmTimer()
	{
		var result = mm_EndPeriod(1);
		if (result != 0)
			System.Diagnostics.Debug.WriteLine($"WinmmTimer: timeEndPeriod failed with result {result}");
	}
#endif

	/// <summary>
	/// 起動時にBeginPeriod、終了時にEndPeriodを呼び出すためだけのインスタンス。
	/// staticなデストラクタがあればいらないんだけど
	/// </summary>
	private static volatile WinmmTimer instance;

	/// <summary>
	/// timeGetTime()。Windows が起動してから経過した時間(ms)。一周して0になる可能性に注意。
	/// </summary>
	public static uint TickCount
	{
		get
		{
#if WINDOWS
			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
			{
				try { return mm_GetTime(); }
				catch { return (uint)Environment.TickCount64; }
			}
#endif
			return (uint)Environment.TickCount64;
		}
	}

	/// <summary>
	/// 現在のフレームの描画に使うためのミリ秒数
	/// </summary>
	public static uint CurrentFrameTime;
	/// <summary>
	/// フレーム描画開始合図の時点でのミリ秒を固定するための数値
	/// </summary>
	public static void FrameStart() { CurrentFrameTime = TickCount; }

#if WINDOWS
	[DllImport("winmm.dll", EntryPoint = "timeGetTime")]
	private static extern uint mm_GetTime();
	[DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
	private static extern uint mm_BeginPeriod(uint uMilliseconds);
	[DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
	private static extern uint mm_EndPeriod(uint uMilliseconds);
#endif
}
