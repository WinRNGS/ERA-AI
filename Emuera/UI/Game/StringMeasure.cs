using MinorShift.Emuera.Runtime.Config;
using System;
using System.Drawing;
using System.Windows.Forms;
using SkiaSharp;

namespace MinorShift.Emuera.UI.Game;

/// <summary>
/// テキスト長計測装置
/// 1819 必要になるたびにCreateGraphicsする方式をやめて 미리用意해둔 Graphicsを準備しておくことにする
/// </summary>
internal sealed class StringMeasure : IDisposable
{
	public static int GetDisplayLength(ReadOnlySpan<char> chars, SKFont f)
	{
		using var paint = new SKPaint();
		paint.Typeface = f.Typeface;
		paint.TextSize = f.Size;
		return (int)paint.MeasureText(chars.ToString());
	}


	bool disposed;
	public void Dispose()
	{
		if (disposed)
			return;
		disposed = true;
	}
}
