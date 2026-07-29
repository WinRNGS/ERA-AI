using System;
using System.Collections.Generic;
using System.Drawing;
using SkiaSharp;

namespace MinorShift.Emuera
{
	/// <summary>
	/// 跨平台互操作抽象层（委托模式）。
	/// 内核代码通过委托调用平台功能，宿主程序在启动时初始化各委托。
	/// WinForms 宿主在 Program.Main() 中设置，Xamarin 宿主在 AndroidHost 中设置。
	/// </summary>
	public static class PlatformInterop
	{
		public static Action DoEvents { get; set; } = () => { };

		public static Action<string> ShowMessage { get; set; } = _ => { };

		public static Action<string, string> ShowMessageWithTitle { get; set; } = (_, __) => { };

		public static Func<string, string, bool> ShowQuestion { get; set; } = (_, __) => false;

		public static Func<string, string, float, FontStyle, float> MeasureTextWidth { get; set; } = (_, __, ___, ____) => 0f;

		public static Func<string, string, float, FontStyle, (float Width, float Height)> MeasureText { get; set; } = (_, __, ___, ____) => (0f, 0f);

		public static Action<string> SetClipboardText { get; set; } = _ => { };

		public static Func<string> GetProductVersion { get; set; } = () => "0.0.0.0";

		public static Func<Point> GetMousePosition { get; set; } = () => Point.Empty;

		public static Func<string, SKBitmap> DecodeBitmapFallback { get; set; } = _ => null;

		public static Func<string, (int Width, int Height)?> GetBitmapInfoFallback { get; set; } = _ => null;

		public static Func<string, List<(SKBitmap Bitmap, int DelayMs)>> DecodeAnimatedFallback { get; set; } = _ => null;

		public static Func<string, (int Width, int Height, int FrameCount, int[] Delays)?> GetAnimInfoFallback { get; set; } = _ => null;

		public static Action<string> DebugLog { get; set; } = msg => System.Diagnostics.Debug.WriteLine(msg);
	}
}
