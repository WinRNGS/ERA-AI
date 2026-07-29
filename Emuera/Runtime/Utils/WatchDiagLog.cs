using System;
using System.Collections.Generic;

namespace MinorShift.Emuera.Runtime.Utils
{
	/// <summary>
	/// 调试窗口监视表达式诊断日志。独立于 UI 层，供 Runtime 层调用。
	/// 日志在 DebugDialog 关闭时由 Flush() 写入文件。
	/// </summary>
	public static class WatchDiagLog
	{
		private static readonly List<string> _log = [];
		private static readonly string LogPath = Program.DebugDir + "watch_diag.log";

		public static void Log(string msg)
		{
			_log.Add(string.Format("[{0}] {1}", DateTime.Now.ToString("HH:mm:ss.fff"), msg));
		}

		public static void Flush()
		{
			if (_log.Count == 0) return;
			try
			{
				System.IO.File.AppendAllLines(LogPath, _log);
				_log.Clear();
			}
			catch { }
		}
	}
}
