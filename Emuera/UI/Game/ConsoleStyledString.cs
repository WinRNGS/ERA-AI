using MinorShift.Emuera.Runtime.Config;
using MinorShift.Emuera.Runtime.Config.JSON;
using MinorShift.Emuera.GameProc.Function;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows.Forms;

namespace MinorShift.Emuera.UI.Game;

public enum DisplayMode
{
	Relative,
	Absolute,//EM+EE互換
	AbsoluteLeftBottom,
	AbsoluteLeftTop
}

public struct TextsWithFont
{
	public string Text;
	public SKFont Font;
	public float Width;
	public float offsetY;
}

public struct GdiTextsWithFont
{
	public string Text;
	public Font Font;
	public float Width;
}

/// <summary>
/// 装飾付文字列。stringとStringStyleからなる。
/// </summary>
internal sealed class ConsoleStyledString : AConsoleColoredPart
{
	private ConsoleStyledString() { }

	public TextDrawingMode? RenderMode { get; private set; }
	public SkiaSharpFontEdging? FontEdging { get; private set; }
	public SkiaSharpFontHinting? FontHinting { get; private set; }
	public float? FontSize { get; private set; }
	public FontVerticalAlign? VerticalAlign { get; private set; }

	public ConsoleStyledString(string str, StringStyle style)
	{
		Text = str;
		StringStyle = style;
		RenderMode = null;
		FontEdging = null;
		FontHinting = null;
		FontSize = null;

		Font = FontFactory.GetFont(style.Fontname, style.FontStyle, FontSize, FontEdging, FontHinting);
		if (Font == null)
		{
			Error = true;
			return;
		}

		string fontName = style.Fontname ?? Config.FontName;
		IsRasterFont = FontFactory.IsRasterFont(fontName);
		if (IsRasterFont)
		{
			GdiFont = FontFactory.GetGdiFont(fontName, style.FontStyle, Font.Size);
			BuildGdiFallbacks();
		}

		BuildFallbacks();

		Color = style.Color;
		BackgroundColor = style.BackgroundColor;
		ButtonColor = style.ButtonColor;
		colorChanged = style.ColorChanged;
		if (!colorChanged && Color != Config.ForeColor)
			colorChanged = true;
		PointX = -1;
		Width = -1;
	}

	public ConsoleStyledString(string str, StringStyle style, TextDrawingMode? renderMode, SkiaSharpFontEdging? edging, SkiaSharpFontHinting? hinting)
	{
		Text = str;
		StringStyle = style;
		RenderMode = renderMode ?? Config.TextDrawingMode;
		FontEdging = edging;
		FontHinting = hinting;

		var actualEdging = edging ?? Config.FontEdging;
		var actualHinting = hinting ?? Config.FontHinting;

		Font = FontFactory.GetFont(style.Fontname, style.FontStyle, null, actualEdging, actualHinting);
		if (Font == null)
		{
			Error = true;
			return;
		}

		string fontName = style.Fontname ?? Config.FontName;
		IsRasterFont = FontFactory.IsRasterFont(fontName);

		if (RenderMode == TextDrawingMode.TEXTRENDERER && IsRasterFont)
		{
			GdiFont = FontFactory.GetGdiFont(fontName, style.FontStyle, Font.Size);
			BuildGdiFallbacks();
		}

		BuildFallbacks();

		Color = style.Color;
		BackgroundColor = style.BackgroundColor;
		ButtonColor = style.ButtonColor;
		colorChanged = style.ColorChanged;
		if (!colorChanged && Color != Config.ForeColor)
			colorChanged = true;
		PointX = -1;
		Width = -1;
	}

	public ConsoleStyledString(string str, StringStyle style, TextDrawingMode? renderMode, SkiaSharpFontEdging? edging, SkiaSharpFontHinting? hinting, float? fontSize, FontVerticalAlign? verticalAlign)
	{
		Text = str;
		StringStyle = style;
		RenderMode = renderMode ?? Config.TextDrawingMode;
		FontEdging = edging;
		FontHinting = hinting;
		FontSize = fontSize;
		VerticalAlign = verticalAlign;

		var actualEdging = edging ?? Config.FontEdging;
		var actualHinting = hinting ?? Config.FontHinting;
		var actualFontSize = fontSize ?? Config.FontSize;

		Font = FontFactory.GetFont(style.Fontname, style.FontStyle, actualFontSize, actualEdging, actualHinting);
		if (Font == null)
		{
			Error = true;
			return;
		}

		string fontName = style.Fontname ?? Config.FontName;
		IsRasterFont = FontFactory.IsRasterFont(fontName);

		if (RenderMode == TextDrawingMode.TEXTRENDERER && IsRasterFont)
		{
			GdiFont = FontFactory.GetGdiFont(fontName, style.FontStyle, Font.Size);
			BuildGdiFallbacks();
		}

		BuildFallbacks();

		Color = style.Color;
		BackgroundColor = style.BackgroundColor;
		ButtonColor = style.ButtonColor;
		colorChanged = style.ColorChanged;
		if (!colorChanged && Color != Config.ForeColor)
			colorChanged = true;
		PointX = -1;
		Width = -1;
	}

	private void BuildGdiFallbacks()
	{
		if (GdiFont == null) return;

		var gdiTextsWithFontList = new List<GdiTextsWithFont>();

		TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
		var size = TextRenderer.MeasureText(Text, GdiFont, new System.Drawing.Size(int.MaxValue, int.MaxValue), flags);

		gdiTextsWithFontList.Add(new GdiTextsWithFont
		{
			Text = Text,
			Font = GdiFont,
			Width = size.Width
		});

		_gdiTexts = gdiTextsWithFontList;
	}

	private void BuildFallbacks()
	{
		if (Font == null) return;

		bool hasSurrogate = false;
		for (int i = 0; i < Text.Length; i++)
		{
			if (char.IsSurrogate(Text[i])) { hasSurrogate = true; break; }
		}

		if (!GlobalStatic.Console.strictFontFallback && !hasSurrogate && Font.ContainsGlyphs(Text))
		{
			_texts = null;
			return;
		}

		var textsWithFontList = new List<TextsWithFont>();
		var currentText = new StringBuilder();
		SKTypeface currentTypeface = null;

		var enumerator = System.Globalization.StringInfo.GetTextElementEnumerator(Text);
		while (enumerator.MoveNext())
		{
			string grapheme = enumerator.GetTextElement();
			int codepoint = char.ConvertToUtf32(grapheme, 0);

			SKTypeface glyphTypeface = Font.Typeface;

			if (!Font.ContainsGlyphs(grapheme))
			{
				glyphTypeface = FontFactory.GetFallbackTypefaceForCodepoint(codepoint, Font.Typeface.FamilyName) ?? Font.Typeface;
			}

			if (currentTypeface == null)
				currentTypeface = glyphTypeface;

			if (glyphTypeface != currentTypeface)
			{
				if (currentText.Length > 0)
				{
					var tempFont = new SKFont(currentTypeface, Font.Size) { Hinting = Font.Hinting, Edging = Font.Edging };
					textsWithFontList.Add(CreateTextWithFont(tempFont, currentText.ToString()));
					currentText.Clear();
				}
				currentTypeface = glyphTypeface;
			}
			currentText.Append(grapheme);
		}

		if (currentText.Length > 0)
		{
			var tempFont = new SKFont(currentTypeface, Font.Size) { Hinting = Font.Hinting, Edging = Font.Edging };
			textsWithFontList.Add(CreateTextWithFont(tempFont, currentText.ToString()));
		}

		_texts = textsWithFontList;
	}

	private SKFont FindFontForChar(char c, FontStyle style)
	{
		if (Font != null && Font.ContainsGlyph(c))
			return Font;
		var fallbackTypeface = FontFactory.GetFallbackTypefaceForChar(c);
		return new SKFont(fallbackTypeface, Config.FontSize)
		{
			Hinting = (SKFontHinting)Config.FontHinting,
			Edging = (SKFontEdging)Config.FontEdging
		};
	}

	TextsWithFont CreateTextWithFont(SKFont font, string t)
	{
		var textsWithFont = new TextsWithFont()
		{
			Text = t,
			Font = font
		};
		using var paint = new SKPaint();
		paint.Typeface = font.Typeface;
		paint.TextSize = font.Size;
		textsWithFont.Width = paint.MeasureText(textsWithFont.Text);
		return textsWithFont;
	}
	public SKFont Font { get; private set; }
	SKFont _fallbackFont;
	List<TextsWithFont> _texts;//フォントフォールバック用
	List<GdiTextsWithFont> _gdiTexts;
	public Font GdiFont { get; private set; }
	public bool IsRasterFont { get; private set; }

	public StringStyle StringStyle { get; private set; }
	public override bool CanDivide
	{
		get { return true; }
	}
	//単一のボタンフラグ
	//public bool IsButton { get; set; }
	//indexの文字数の前方文字列とindex以降の後方文字列に分割
	public ConsoleStyledString DivideAt(int index, StringMeasure sm)
	{
		//if ((index <= 0)||(index > Text.Length)||this.Error)
		//	return null;
		ConsoleStyledString ret = DivideAt(index);
		if (ret == null)
			return null;
		SetWidth(sm, XsubPixel);
		ret.SetWidth(sm, XsubPixel);
		return ret;
	}
	public ConsoleStyledString DivideAt(int index)
	{
		if (index <= 0 || index > Text.Length || Error)
			return null;
		string str = Text[index..];
		Text = Text[..index];

		BuildFallbacks();

		ConsoleStyledString ret = new ConsoleStyledString(str, this.StringStyle, this.RenderMode, this.FontEdging, this.FontHinting, this.FontSize, this.VerticalAlign)
		{
			XsubPixel = this.XsubPixel
		};
		return ret;
	}

	public override void SetWidth(StringMeasure sm, float subPixel)
	{
		if (Error)
		{
			Width = 0;
			return;
		}

		if (RenderMode == TextDrawingMode.TEXTRENDERER && GdiFont != null && _gdiTexts != null)
		{
			var offsetX = 0.0f;
			foreach (var text in _gdiTexts) offsetX += text.Width;
			Width = (int)offsetX;
		}
		else if (_texts == null)
		{
			Width = StringMeasure.GetDisplayLength(Text, Font);
		}
		else
		{
			var offsetX = 0.0f;
			foreach (var text in _texts) offsetX += text.Width;
			Width = (int)offsetX;
		}

		XsubPixel = subPixel;
		Size = new SKSize(Width, Config.LineHeight);

		#region EmuEra-Rikaichan
		if (!rikaichaned && Config.RikaiEnabled)
		{
			rikaichaned = true;
			int len = Text.Length;
			Ends = new int[len];
			for (int i = 0; i < len; i++)
			{
				string temp = Text.Substring(0, i + 1);
				Ends[i] = StringMeasure.GetDisplayLength(temp, Font);
			}

		}
		#endregion
	}

#if WINDOWS
	private static SKBitmap CreateSkBitmapFromGdiBitmap(System.Drawing.Bitmap gdiBitmap)
	{
		var width = gdiBitmap.Width;
		var height = gdiBitmap.Height;
		var skBitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);

		var rect = new System.Drawing.Rectangle(0, 0, width, height);
		var bitmapData = gdiBitmap.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

		try
		{
			unsafe
			{
				var dstPixels = skBitmap.GetPixels();
				var srcPixels = bitmapData.Scan0;
				var bytesPerPixel = 4;
				var totalBytes = width * height * bytesPerPixel;
				Buffer.MemoryCopy(srcPixels.ToPointer(), dstPixels.ToPointer(), totalBytes, totalBytes);
				int pixelCount = width * height;
				uint* px = (uint*)dstPixels.ToPointer();
				for (int i = 0; i < pixelCount; i++)
				{
					uint p = px[i];
					uint a = p >> 24;
					if (a == 0 || a == 255)
						continue;
					uint b = (p & 0xFF) * a / 255;
					uint g = ((p >> 8) & 0xFF) * a / 255;
					uint r = ((p >> 16) & 0xFF) * a / 255;
					px[i] = (a << 24) | (r << 16) | (g << 8) | b;
				}
			}
		}
		finally
		{
			gdiBitmap.UnlockBits(bitmapData);
		}

		return skBitmap;
	}
#endif

	public override void DrawTo(SKCanvas graph, SKPoint origin, bool isSelecting, bool isFocus, bool isBackLog, TextDrawingMode mode, bool isButton = false)
	{
		if (Error)
			return;
		var color = Color;
		SKColor? backcolor = null;
		if (isFocus)
		{
			if (JSONConfig.Data.UseButtonFocusBackgroundColor)
			{
				if (!(Color.Yellow.R == color.R &&
					Color.Yellow.G == color.G &&
					Color.Yellow.B == color.B) &&
					!string.IsNullOrWhiteSpace(Text))
				{
					backcolor = SKColors.Gray;
				}
			}
			color = ButtonColor;
		}
		else if (BackgroundColor.HasValue)
		{
			backcolor = BackgroundColor.Value.ToSKColor();
		}
		else if (isBackLog && !colorChanged)
		{
			color = Config.LogColor;
		}

	#region EM_私家版_描画拡張
	bool isAntialias;
	if (FontEdging.HasValue)
		isAntialias = FontEdging.Value != SkiaSharpFontEdging.Alias;
	else
		isAntialias = !IsRasterFont;
	using var paint = new SKPaint
	{
		Color = color.ToSKColor(),
		IsAntialias = isAntialias
	};

	var point = new SKPoint(PointX + Config.DrawingParam_ShapePositionShift, origin.Y);
	// valign 偏移：top 不偏移，middle/bottom 按行高偏移
	if (VerticalAlign == FontVerticalAlign.Middle)
		point.Offset(0, (Config.LineHeight - Font.Metrics.Descent + Font.Metrics.Ascent) / 2f);
	else if (VerticalAlign == FontVerticalAlign.Bottom)
		point.Offset(0, Config.LineHeight - Font.Metrics.Descent + Font.Metrics.Ascent);
	if (origin.X == -1)//旧来の位置決め方式
	{
		point.X = PointX + Config.DrawingParam_ShapePositionShift;
	}
	Point = point;

	#region EM_私家版_GDI渲染扩展
#if WINDOWS
	bool useGdiRender = (RenderMode == TextDrawingMode.TEXTRENDERER || RenderMode == null) && IsRasterFont && GdiFont != null;
	if (useGdiRender)
	{
		TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.PreserveGraphicsClipping;

		if (backcolor.HasValue)
		{
			using var backBrush = new SolidBrush(System.Drawing.Color.FromArgb(backcolor.Value.Alpha, backcolor.Value.Red, backcolor.Value.Green, backcolor.Value.Blue));
			using var bitmap = new System.Drawing.Bitmap((int)Width + 2, (int)Font.Size + 2);
			using (var g = System.Drawing.Graphics.FromImage(bitmap))
			{
				g.FillRectangle(backBrush, 0, 0, bitmap.Width, bitmap.Height);
			}
			using var skBitmap = CreateSkBitmapFromGdiBitmap(bitmap);
			graph.DrawBitmap(skBitmap, new SKPoint(point.X, point.Y - Font.Size));
		}

		if (_gdiTexts != null)
		{
			foreach (var gdiText in _gdiTexts)
			{
				using var bitmap = new System.Drawing.Bitmap((int)gdiText.Width + 2, (int)Font.Size + 2);
				using (var g = System.Drawing.Graphics.FromImage(bitmap))
				{
					g.TextRenderingHint = IsRasterFont
						? System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit
						: System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
					TextRenderer.DrawText(g, gdiText.Text, gdiText.Font, new System.Drawing.Point(0, 0), color, flags);
				}
				using var skBitmap = CreateSkBitmapFromGdiBitmap(bitmap);
				var skPoint = new SKPoint(point.X, point.Y - gdiText.Font.Height + gdiText.Font.Size);
				graph.DrawBitmap(skBitmap, skPoint);
				point.Offset(gdiText.Width, 0);
			}
		}
		else
		{
			using var bitmap = new System.Drawing.Bitmap((int)Width, (int)Font.Size);
			using (var g = System.Drawing.Graphics.FromImage(bitmap))
			{
				g.TextRenderingHint = IsRasterFont
					? System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit
					: System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
				TextRenderer.DrawText(g, Text, GdiFont, new System.Drawing.Point(0, 0), color, flags);
			}
			using var skBitmap = CreateSkBitmapFromGdiBitmap(bitmap);
			graph.DrawBitmap(skBitmap, new SKPoint(point.X, point.Y));
		}
		return;
	}
#endif
	#endregion

	if (backcolor.HasValue)
	{
		var size = new SKSize(Width, Font.Size);
		using var backPaint = new SKPaint() { Color = backcolor.Value };
		graph.DrawRect(SKRect.Create(point, size), backPaint);
	}

	if (_texts == null)
	{
		point.Offset(0, -Font.Metrics.Ascent);
		graph.DrawText(Text, point.X, point.Y, Font, paint);
	}
	else
	{
		foreach (var text in _texts)
		{
			var offsetPoint = point with { Y = point.Y - text.Font.Metrics.Ascent };
			graph.DrawText(text.Text, offsetPoint.X, offsetPoint.Y, text.Font, paint);

			point.Offset(text.Width, 0);
		}
	}

	if (StringStyle.HasUnderline)
	{
		var underlinePosition = Point.Value;
		underlinePosition.Offset(0, -Font.Metrics.Top + (Font.Metrics.UnderlinePosition ?? 0));

		var width = paint.StrokeWidth;
		paint.StrokeWidth = Font.Metrics.UnderlineThickness ?? 1;
		graph.DrawLine(underlinePosition, underlinePosition + new SKPoint(Width, 0), paint);
		paint.StrokeWidth = width;
	}

	if (StringStyle.HasStrikeout)
	{
		var strikeoutPosition = Point.Value;
		strikeoutPosition.Offset(0, -Font.Metrics.Top + (Font.Metrics.StrikeoutPosition ?? 0));

		var width = paint.StrokeWidth;
		paint.StrokeWidth = Font.Metrics.StrikeoutThickness ?? 1;
		graph.DrawLine(strikeoutPosition, strikeoutPosition + new SKPoint(Width, 0), paint);
		paint.StrokeWidth = width;
	}

	#endregion
}

	//Bitmap Cache
	public void DrawToBitmap(SKCanvas graph, bool isSelecting, bool isBackLog, TextDrawingMode mode, int xOffset)
	{
		if (Error)
			return;
		Color color = Color;
		if (isSelecting)
			color = ButtonColor;
		else if (isBackLog && !colorChanged)
			color = Config.LogColor;

		#region EM_私家版_GDI渲染扩展
#if WINDOWS
		bool useGdiRenderBitmap = (RenderMode == TextDrawingMode.TEXTRENDERER || RenderMode == null) && IsRasterFont && GdiFont != null;
		if (useGdiRenderBitmap)
		{
			TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.PreserveGraphicsClipping;
			float startX = xOffset + Config.DrawingParam_ShapePositionShift;
			var point = new SKPoint(startX, 0);

			if (_gdiTexts != null)
			{
				foreach (var gdiText in _gdiTexts)
				{
					using var bitmap = new System.Drawing.Bitmap((int)gdiText.Width + 2, (int)Font.Size + 2);
					using (var g = System.Drawing.Graphics.FromImage(bitmap))
					{
						g.TextRenderingHint = IsRasterFont
							? System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit
							: System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
						TextRenderer.DrawText(g, gdiText.Text, gdiText.Font, new System.Drawing.Point(0, 0), color, flags);
					}
					using var skBitmap = CreateSkBitmapFromGdiBitmap(bitmap);
					var skPoint = point with { Y = point.Y - gdiText.Font.Height + gdiText.Font.Size };
					graph.DrawBitmap(skBitmap, skPoint);
					point.Offset(gdiText.Width, 0);
				}
			}
			else
			{
				using var bitmap = new System.Drawing.Bitmap((int)Width, (int)Font.Size);
				using (var g = System.Drawing.Graphics.FromImage(bitmap))
				{
					g.TextRenderingHint = IsRasterFont
						? System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit
						: System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
					TextRenderer.DrawText(g, Text, GdiFont, new System.Drawing.Point(0, 0), color, flags);
				}
				using var skBitmap = CreateSkBitmapFromGdiBitmap(bitmap);
				graph.DrawBitmap(skBitmap, point);
			}
			return;
		}
#endif
		#endregion

		#region EM_私家版_描画拡張
		bool isAntialias;
		if (FontEdging.HasValue)
			isAntialias = FontEdging.Value != SkiaSharpFontEdging.Alias;
		else
			isAntialias = !IsRasterFont;
		using var bitmapPaint = new SKPaint {
			Color = color.ToSKColor(),
			IsAntialias = isAntialias
		};

		float gdiStartX = xOffset + Config.DrawingParam_ShapePositionShift;

		if (_texts == null)
		{
			graph.DrawText(Text, gdiStartX, -Font.Metrics.Ascent, Font, bitmapPaint);
		}
		else
		{
			float currentX = gdiStartX;
			foreach (var text in _texts)
			{
				graph.DrawText(text.Text, currentX, -text.Font.Metrics.Ascent, text.Font, bitmapPaint);
				currentX += text.Width;
			}
		}
		#endregion
	}
}
