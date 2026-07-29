using MinorShift.Emuera.Runtime.Config;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Linq;
using System.Runtime.CompilerServices;

namespace MinorShift.Emuera.UI.Game.Image;

internal sealed class GraphicsImage : AbstractImage
{
	//public Bitmap Bitmap;
	//public IntPtr GDIhDC { get; protected set; }
	//protected Graphics canvas;
	//protected IntPtr hBitmap;
	//protected IntPtr hDefaultImg;

	public GraphicsImage(int id)
	{
		ID = id;
		canvas = null;
		SKBitmap = null;
		//created = false;
		//locked = false;
	}
	public readonly int ID;
	Size size;
	Brush brush;
	Pen pen;
	SKFont font;
	#region EE_GDRAWTEXT
	FontStyle style;
	#endregion

	//Bitmap b;
	//Graphics canvas;
	// 当GraphicsImage是完全由图像拼接而成时，此处记录拼接图案的列表。
	// 可清理图片来减少内存使用。在使用时按照此列表组合
	public bool useImgList { get { return drawImgList != null; } }
	public List<Tuple<ASprite, Rectangle>> drawImgList;


	////bool created;
	////bool locked;
	//public void LockGraphics()
	//{
	//	//if (locked)
	//	//	return;
	//	//canvas = Graphics.FromImage(b);
	//	//locked = true;
	//}
	//public void UnlockGraphics()
	//{
	//	//if (!locked)
	//	//	return;
	//	//canvas.Dispose();
	//	//canvas = null;
	//	//locked = false;
	//}

	public SKBitmap RealBitmap;
	public override SKBitmap SKBitmap
	{
		set { RealBitmap = value; }
		get
		{
			Load();
			return RealBitmap;
		}
	}

	#region Bitmap書き込み・作成

	/// <summary>
	/// GCREATE(int ID, int width, int height)
	/// Graphicsの基礎となるBitmapを作成する。エラーチェックは呼び出し元でのみ行う
	/// </summary>
	public void GCreate(int x, int y, bool useGDI)
	{
		if (useGDI)
			throw new NotImplementedException();
		GDispose();
		RealBitmap = new SKBitmap(x, y);
		size = new Size(x, y);
		canvas = new SKCanvas(RealBitmap);
		drawImgList = [];
		lock (AppContents.tempLoadedGraphicsImages)
			AppContents.tempLoadedGraphicsImages.Add(this);
	}
	internal void GCreateFromF(SKBitmap bmp, bool useGDI)
	{
		if (useGDI)
			throw new NotImplementedException();
		GDispose();
		RealBitmap = new SKBitmap(bmp.Width, bmp.Height);
		size = new Size(bmp.Width, bmp.Height);
		canvas = new SKCanvas(RealBitmap);
		//g.DrawImage(bmp, 0, 0, bmp.Width, bmp.Height);
		canvas.DrawBitmap(bmp, new SKRect(0, 0, bmp.Width, bmp.Height));
	}

	/// <summary>
	/// GCLEAR(int ID, int cARGB)
	/// エラーチェックは呼び出し元でのみ行う
	/// </summary>
	public void GClear(Color c)
	{
		if (canvas == null)
			throw new NullReferenceException();
		canvas.Clear(c.ToSKColor());
	}

	#region EM_私家版_GCLEAR拡張
	public void GClear(Color c, int x, int y, int w, int h)
	{
		Load();
		if (canvas == null)
			throw new NullReferenceException();
		canvas.Save();
		canvas.ClipRect(SKRect.Create(x, y, w, h));
		canvas.Clear(c.ToSKColor());
		canvas.Restore();
		drawImgList = null;
	}
	#endregion

	/// <summary>
	/// GDRAWTEXTGDRAWTEXT int ID, str text, int x, int y
	/// エラーチェックは呼び出し元でのみ行う
	/// SkiaSharp失败时自动回退到GDI+渲染
	/// </summary>
	#region EE_GDRAWTEXT 元のソースコードにあったものを改良
	public void GDrawString(string text, int x, int y)
	{
		Load();
		if (canvas == null)
			throw new NullReferenceException();

		drawImgList = null;

		if (Config.TextDrawingMode == TextDrawingMode.GRAPHICS || Config.TextDrawingMode == TextDrawingMode.TEXTRENDERER)
		{
#if WINDOWS
			GDrawStringGDIFallback(text, x, y);
			return;
#else
			GDrawStringSkia(text, x, y);
			return;
#endif
		}

		try
		{
			GDrawStringSkia(text, x, y);
		}
		catch
		{
#if WINDOWS
			try
			{
				GDrawStringGDIFallback(text, x, y);
			}
			catch
			{
			}
#endif
		}
	}

	private void GDrawStringSkia(string text, int x, int y)
	{
		SKFont usingFont = font ?? Config.DefaultFont;

		using var paint = new SKPaint();
		Color textColor = brush != null ? ((SolidBrush)brush).Color : Config.ForeColor;
		paint.Color = textColor.ToSKColor();

		float currentX = x;
		var currentText = new System.Text.StringBuilder();
		SKTypeface currentTypeface = usingFont.Typeface;

		void DrawSegment()
		{
			if (currentText.Length > 0)
			{
				using var tempFont = new SKFont(currentTypeface, usingFont.Size) { Hinting = usingFont.Hinting, Edging = usingFont.Edging };

				float baselineY = y - tempFont.Metrics.Ascent;

				canvas.DrawText(currentText.ToString(), currentX, baselineY, tempFont, paint);

				using var measurePaint = new SKPaint { Typeface = currentTypeface, TextSize = usingFont.Size };
				currentX += measurePaint.MeasureText(currentText.ToString());

				currentText.Clear();
			}
		}

		foreach (char c in text)
		{
			SKTypeface charTypeface = currentTypeface;

			if (!charTypeface.ContainsGlyph(c))
			{
				charTypeface = FontFactory.GetFallbackTypefaceForChar(c) ?? usingFont.Typeface;
			}

			if (charTypeface != currentTypeface && currentText.Length > 0)
			{
				DrawSegment();
			}

			currentTypeface = charTypeface;
			currentText.Append(c);
		}

		DrawSegment();

		if (pen != null)
		{
			paint.Style = SKPaintStyle.Stroke;
			paint.StrokeWidth = pen.Width;
			paint.Color = pen.Color.ToSKColor();

			float baselineY = y - usingFont.Metrics.Ascent;
			canvas.DrawText(text, x, baselineY, usingFont, paint);
		}
	}

	private void GDrawStringGDIFallback(string text, int x, int y)
	{
		SKFont skFont = font ?? Config.DefaultFont;
		string fontName = skFont.Typeface?.FamilyName ?? Config.FontName;
		float fontSize = skFont.Size;
		Font gdiFont = FontFactory.GetGdiFont(fontName, style, fontSize);
		Color textColor = brush != null ? ((SolidBrush)brush).Color : Config.ForeColor;

		using var measurePaint = new SKPaint { Typeface = skFont.Typeface, TextSize = fontSize };
		float textWidth = measurePaint.MeasureText(text);
		int bmpW = Math.Max(1, (int)Math.Ceiling(textWidth) + 4);
		int bmpH = Math.Max(1, (int)Math.Ceiling(gdiFont.FontFamily.GetLineSpacing(gdiFont.Style) * gdiFont.Size / gdiFont.FontFamily.GetEmHeight(gdiFont.Style)) + 4);

		using var gdiBitmap = new Bitmap(bmpW, bmpH);
		using var g = Graphics.FromImage(gdiBitmap);
		g.Clear(Color.Transparent);
		g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
		g.SmoothingMode = SmoothingMode.AntiAlias;

		using var gdiBrush = new SolidBrush(textColor);
		g.DrawString(text, gdiFont, gdiBrush, 0, 0, StringFormat.GenericTypographic);

		if (pen != null)
		{
			using var penBrush = new SolidBrush(pen.Color);
			g.DrawString(text, gdiFont, penBrush, 0, 0, StringFormat.GenericTypographic);
		}

		using var skTemp = new SKBitmap(bmpW, bmpH, SKColorType.Bgra8888, SKAlphaType.Unpremul);
		var rect = new System.Drawing.Rectangle(0, 0, bmpW, bmpH);
		var bmpData = gdiBitmap.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
		try
		{
			unsafe
			{
				var dst = skTemp.GetPixels();
				var src = bmpData.Scan0;
				Buffer.MemoryCopy(src.ToPointer(), dst.ToPointer(), bmpW * bmpH * 4, bmpW * bmpH * 4);
			}
		}
		finally
		{
			gdiBitmap.UnlockBits(bmpData);
		}

		canvas.DrawBitmap(skTemp, x, y);
	}
	#endregion

	/// <summary>
	/// GDRAWTEXT int ID, str text, int x, int y, int width, int height
	/// エラーチェックは呼び出し元でのみ行う
	/// </summary>
	///
	/*
	public void GDrawString(string text, int x, int y, int width, int height)
	{
		Load();
		if (g == null)
			throw new NullReferenceException();

		drawImgList = null;

		Font usingFont = font;
		if (usingFont == null)
			usingFont = Config.DefaultFont;
		if (brush != null)
		{
			g.DrawString(text, usingFont, brush, new RectangleF(x, y, width, height));
		}
		else
		{
			using var b = new SolidBrush(Config.ForeColor);
			g.DrawString(text, usingFont, b, x, y);
		}
	}

	/// <summary>
	/// GDRAWRECTANGLE(int ID, int x, int y, int width, int height)
	/// エラーチェックは呼び出し元でのみ行う
	/// </summary>
	public void GDrawRectangle(Rectangle rect)
	{
		Load();
		if (g == null)
			throw new NullReferenceException();

		drawImgList = null;

		if (pen != null)
		{
			g.DrawRectangle(pen, rect);
		}
		else
		{
			using var p = new Pen(Config.ForeColor);
			g.DrawRectangle(p, rect);
		}
	}
	*/

	List<SKPoint> _points;

	public void GDrawPolygon()
	{
		Load();
		if (canvas == null)
			throw new NullReferenceException();
		if (_points == null)
			throw new NullReferenceException("DrawPolygonに渡されるPointsが空です");

		drawImgList = null;

		using var paint = new SKPaint();
		if (pen != null)
		{
			paint.Color = pen.Color.ToSKColor();
			paint.StrokeWidth = pen.Width;
		}
		paint.Style = SKPaintStyle.Stroke;
		canvas.DrawPoints(SKPointMode.Polygon, [.. _points, _points[0]], paint);
	}

	public void GFillPolygon()
	{
		Load();
		if (canvas == null)
			throw new NullReferenceException();
		if (_points == null)
			throw new NullReferenceException("FillPolygonに渡されるPointsが空です");

		drawImgList = null;

		using var paint = new SKPaint();
		if (brush != null)
		{
			var b = (SolidBrush)brush;
			paint.Color = b.Color.ToSKColor();
		}
		paint.Style = SKPaintStyle.Fill;

		var path = new SKPath();
		path.MoveTo(_points[0]);
		for (int i = 1; i < _points.Count; i++)
		{
			path.LineTo(_points[i]);
		}
		path.Close();
		canvas.DrawPath(path, paint);
	}

	public void GDrawPolygonAddPoint(SKPoint point)
	{
		if (canvas == null)
			throw new NullReferenceException();
		_points ??= [];
		_points.Add(point);
	}

	public void GDrawPolygonClearPoint()
	{
		if (canvas == null)
			throw new NullReferenceException();
		if (_points == null)
		{
			_points = [];
		}
		else
		{
			_points.Clear();
		}
	}

	/// <summary>
	/// GFILLRECTANGLE(int ID, int x, int y, int width, int height)
	/// エラーチェックは呼び出し元でのみ行う
	/// </summary>
	public void GFillRectangle(Rectangle rect)
	{
		Load();
		if (canvas == null)
			throw new NullReferenceException();

		drawImgList = null;

		if (brush != null)
		{
			using var paint = new SKPaint();
			var b = (SolidBrush)brush;
			paint.Color = b.Color.ToSKColor();
			canvas.DrawRect(rect.ToSKRect(), paint);
		}
		else
		{
			using var paint = new SKPaint
			{
				Color = Config.BackColor.ToSKColor()
			};
			canvas.DrawRect(rect.ToSKRect(), paint);
		}
	}

	/// <summary>
	/// GDRAWCIMG(int ID, str imgName, int destX, int destY, int destWidth, int destHeight)
	/// エラーチェックは呼び出し元でのみ行う
	/// </summary>
	public void GDrawCImg(ASprite img, Rectangle destRect)
	{
		Load();
		if (canvas == null)
			throw new NullReferenceException();

		if (useImgList)
		{
			if (img as SpriteG != null)
			{
				SpriteG imgG = img as SpriteG;
				if (imgG.isBaseImage(this))
				{
					drawImgList = null;
				}
				else
				{
					drawImgList.Add(new Tuple<ASprite, Rectangle>(img, destRect));
					if (drawImgList.Count > 50)
						drawImgList = null;
				}
			}
			else if (img as SpriteF != null)
			{
				drawImgList.Add(
					new Tuple<ASprite, Rectangle>(img, destRect)
				);

				if (drawImgList.Count > 50)
				{
					drawImgList = null;
				}
			}
			else
			{
				drawImgList = null;
			}
		}
		img.GraphicsDraw(canvas, destRect);
	}

	/// <summary>
	/// GDRAWCIMG(int ID, str imgName, int destX, int destY, int destWidth, int destHeight, float[][] cm)
	/// エラーチェックは呼び出し元でのみ行う
	/// </summary>
	public void GDrawCImg(ASprite img, Rectangle destRect, float[][] cm)
	{
		Load();
		if (canvas == null)
			throw new NullReferenceException();

		drawImgList = null;
		float[] skiaCM = [
			cm[0][0], cm[0][1], cm[0][2], cm[0][3], cm[0][4] * 255f,
			cm[1][0], cm[1][1], cm[1][2], cm[1][3], cm[1][4] * 255f,
			cm[2][0], cm[2][1], cm[2][2], cm[2][3], cm[2][4] * 255f,
			cm[3][0], cm[3][1], cm[3][2], cm[3][3], cm[3][4] * 255f,
		];
		using var filter = SKColorFilter.CreateColorMatrix(skiaCM);
		img.GraphicsDraw(canvas, destRect, filter);
	}

	/// <summary>
	/// GDRAWG(int ID, int srcID, int destX, int destY, int destWidth, int destHeight, int srcX, int srcY, int srcWidth, int srcHeight)
	/// エラーチェックは呼び出し元でのみ行う
	/// </summary>
	public void GDrawG(GraphicsImage srcGra, Rectangle destRect, Rectangle srcRect)
	{
		Load();
		if (canvas == null)
			throw new NullReferenceException();

		drawImgList = null;

		var src = srcGra.GetBitmap();
		canvas.DrawBitmap(src, srcRect.ToSKRect(), destRect.ToSKRect());
	}


	/// <summary>
	/// GDRAWG(int ID, int srcID, int destX, int destY, int destWidth, int destHeight, int srcX, int srcY, int srcWidth, int srcHeight, float[][] cm)
	/// エラーチェックは呼び出し元でのみ行う
	/// </summary>
	public void GDrawG(GraphicsImage srcGra, Rectangle destRect, Rectangle srcRect, float[][] cm)
	{
		Load();
		if (canvas == null)
			throw new NullReferenceException();

		drawImgList = null;

		var src = srcGra.GetBitmap();
		float[] skiaCM = [
			cm[0][0], cm[0][1], cm[0][2], cm[0][3], cm[0][4] * 255f,
			cm[1][0], cm[1][1], cm[1][2], cm[1][3], cm[1][4] * 255f,
			cm[2][0], cm[2][1], cm[2][2], cm[2][3], cm[2][4] * 255f,
			cm[3][0], cm[3][1], cm[3][2], cm[3][3], cm[3][4] * 255f,
		];
		using var filter = SKColorFilter.CreateColorMatrix(skiaCM);
		using var paint = new SKPaint { ColorFilter = filter };
		canvas.DrawBitmap(src, srcRect.ToSKRect(), destRect.ToSKRect(), paint);
	}


	/// <summary>
	/// GDRAWGWITHMASK(int ID, int srcID, int maskID, int destX, int destY)
	/// エラーチェックは呼び出し元でのみ行う
	/// </summary>
	public void GDrawGWithMask(GraphicsImage srcGra, GraphicsImage maskGra, Point destPoint)
	{
		Load();
		if (canvas == null)
			throw new NullReferenceException();

		drawImgList = null;

		// Create a temporary offscreen surface to handle the mask
		using var surface = SKSurface.Create(new SKImageInfo(srcGra.Width, srcGra.Height));
		var tempCanvas = surface.Canvas;

		// 1. Draw the source image
		tempCanvas.DrawBitmap(srcGra.GetBitmap(), 0, 0);

		// 2. Draw the mask using DstIn blend mode (preserves areas where both mask and source are opaque)
		using var maskPaint = new SKPaint { BlendMode = SKBlendMode.DstIn };
		tempCanvas.DrawBitmap(maskGra.GetBitmap(), 0, 0, maskPaint);

		// 3. Draw the result to the target canvas
		using var resultImage = surface.Snapshot();
		canvas.DrawImage(resultImage, destPoint.X, destPoint.Y);
	}

	#region EE_GDRAWGWITHROTATE
	/*
	/// <summary>
	/// GROTATE(int ID, int angle, int x, int y)
	/// </summary>
	public void GRotate(long a, int x, int y)
	{
		if (canvas == null)
			throw new NullReferenceException();
		float angle = a;
		canvas.TranslateTransform(-x, -y, MatrixOrder.Append);
		canvas.RotateTransform(angle, MatrixOrder.Append);
		canvas.TranslateTransform(x, y, MatrixOrder.Append);

		canvas.DrawImageUnscaled(Bitmap, 0, 0);
		//canvas.DrawImage(Bitmap, new Rectangle(Bitmap.Width, Bitmap.Height, Bitmap.Width, Bitmap.Height));
	}
	*/
	/// <summary>
	/// GDRAWGWITHROTATE
	/// </summary>
	public void GDrawGWithRotate(SKBitmap srcGra, long a, int x, int y)
	{
		if (canvas == null || srcGra == null)
			throw new NullReferenceException();
		float angle = a;
		canvas.RotateDegrees(angle, x, y);
		canvas.DrawBitmap(srcGra, 0, 0);
	}
	#endregion
	#region EE_GDRAWLINE
	public void GDrawLine(int fromX, int fromY, int destX, int destY)
	{
		if (canvas == null)
			throw new NullReferenceException();

		if (pen != null)
		{
			using var paint = new SKPaint
			{
				Color = pen.Color.ToSKColor(),
				StrokeWidth = pen.Width
			};
			switch (pen.DashCap)
			{
				case DashCap.Flat:
					paint.StrokeCap = SKStrokeCap.Butt;
					break;
				case DashCap.Round:
					paint.StrokeCap = SKStrokeCap.Round;
					break;
				case DashCap.Triangle:
					paint.StrokeCap = SKStrokeCap.Square;
					break;
			}
			SKPathEffect ds = SKPathEffect.CreateDash([1, 0], 0);
			switch (pen.DashStyle)
			{
				case DashStyle.Solid:
					ds = SKPathEffect.CreateDash([1, 0], 0);
					break;
				case DashStyle.Dash:
					ds = SKPathEffect.CreateDash([pen.Width*3, pen.Width], 0);
					break;
				case DashStyle.Dot:
					ds = SKPathEffect.CreateDash([pen.Width, pen.Width], 0);
					break;
				case DashStyle.DashDot:
					ds = SKPathEffect.CreateDash([pen.Width*3, pen.Width, pen.Width, pen.Width], 0);
					break;
				case DashStyle.DashDotDot:
					ds = SKPathEffect.CreateDash([pen.Width*3, pen.Width, pen.Width, pen.Width, pen.Width, pen.Width], 0);
					break;
			}
			paint.PathEffect = ds;
			canvas.DrawLine(fromX, fromY, destX, destY, paint);
		}
		else
		{
			using var paint = new SKPaint
			{
				Color = Config.ForeColor.ToSKColor()
			};
			canvas.DrawLine(fromX, fromY, destX, destY, paint);
		}
	}
	#endregion

	#region EE_GDASHSTYLE
	public void GDashStyle(long style, long cap)
	{
		if (canvas == null)
			throw new NullReferenceException();
		if (pen == null)
			pen = new Pen(Config.ForeColor);

		pen.DashStyle = (DashStyle)style;
		pen.DashCap = (DashCap)cap;
	}
	#endregion

	#region EE_GDRAWTEXT フォントスタイルも指定できるように
	// public void GSetFont(Font r)
	public void GSetFont(SKFont r, FontStyle fs)
	{
		// FontFactory.GetFont returns cached shared SKFont; do not Dispose it here.
		// FontFactory manages the lifecycle of all cached fonts.
		font = r;
		style = fs;
	}
	#endregion
	public void GSetBrush(Brush r)
	{
		if (brush != null)
			brush.Dispose();
		brush = r;
	}
	public void GSetPen(Pen r)
	{
		DashStyle style = DashStyle.Solid;
		DashCap cap = DashCap.Flat;

		if (pen != null)
		{
			style = pen.DashStyle;
			cap = pen.DashCap;
			pen.Dispose();
		}
		pen = r;
		pen.DashStyle = style;
		pen.DashCap = cap;
	}

	#region Bitmap読み込み・削除
	/// <summary>
	/// 未作成ならエラー
	/// </summary>
	public SKBitmap GetBitmap()
	{
		if (SKBitmap == null)
			throw new NullReferenceException();
		//UnlockGraphics();
		return SKBitmap;
	}
	/// <summary>
	/// GSETCOLOR(int ID, int cARGB, int x, int y)
	/// エラーチェックは呼び出し元でのみ行う
	/// </summary>
	public void GSetColor(Color c, int x, int y)
	{
		if (SKBitmap == null)
			throw new NullReferenceException();
		//UnlockGraphics();
		SKBitmap.SetPixel(x, y, c.ToSKColor());
	}

	/// <summary>
	/// GGETCOLOR(int ID, int x, int y)
	/// エラーチェックは呼び出し元でのみ行う。特に画像範囲内であるかどうかチェックすること
	/// </summary>
	public SKColor GGetColor(int x, int y)
	{
		if (SKBitmap == null)
			throw new NullReferenceException();
		//UnlockGraphics();
		return SKBitmap.GetPixel(x, y);
	}

	public void UnLoad()
	{
		if (RealBitmap == null)
			return;

		if (canvas != null)
			canvas.Dispose();
		if (RealBitmap != null)
			RealBitmap.Dispose();
		canvas = null;
		RealBitmap = null;
	}

	/// <summary>
	/// GDISPOSE(int ID)
	/// </summary>
	public void GDispose()
	{
		size = new Size(0, 0);
		drawImgList = null;
		_points = null;
		if (RealBitmap == null)
			return;
		if (canvas != null)
			canvas.Dispose();
		if (RealBitmap != null)
			RealBitmap.Dispose();
		if (brush != null)
			brush.Dispose();
		if (pen != null)
			pen.Dispose();
		if (font != null)
			font.Dispose();
		canvas = null;
		RealBitmap = null;
		brush = null;
		pen = null;
		font = null;
	}

	public override void Dispose()
	{
		GDispose();
		GC.SuppressFinalize(this);
	}

	~GraphicsImage()
	{
		Dispose();
	}
	#endregion

	#region 状態判定（Bitmap読み書きを伴わない）
	public override bool IsCreated { get { return canvas != null || useImgList; } }
	/// <summary>
	/// int GWIDTH(int ID)
	/// </summary>
	public int Width { get { return size.Width; } }
	/// <summary>
	/// int GHEIGHT(int ID)
	/// </summary>
	public int Height { get { return size.Height; } }

	#region EE_GDRAWTEXTに付随する様々な要素
	public string Fontname { get { return font?.Typeface?.FamilyName ?? ""; } }
	public int Fontsize { get { return font != null ? (int)font.Size : 0; } }

	public int Fontstyle
	{
		get
		{
			int ret = 0;
			if ((style & FontStyle.Bold) == FontStyle.Bold)
				ret |= 1;
			if ((style & FontStyle.Italic) == FontStyle.Italic)
				ret |= 2;
			if ((style & FontStyle.Strikeout) == FontStyle.Strikeout)
				ret |= 4;
			if ((style & FontStyle.Underline) == FontStyle.Underline)
				ret |= 8;
			return ret;
		}
	}

	public SKFont Fnt { get { return font; } }
	public Pen Pen { get { return pen; } }
	public Brush Brush { get { return brush; } }
	// Null-safe accessors for ERB-layer queries (GGETPEN/GGETPENWIDTH/GGETBRUSH)
	public long PenColorArgb { get { return pen != null ? pen.Color.ToArgb() & 0xffffffffL : 0L; } }
	public long PenWidth { get { return pen != null ? (long)pen.Width : 0L; } }
	public long BrushColorArgb { get { return brush != null ? ((SolidBrush)brush).Color.ToArgb() & 0xffffffffL : 0L; } }
	#endregion

	#endregion


	private static byte[] BytesFromBitmap(Bitmap bmp)
	{
		BitmapData bmpData = bmp.LockBits(
		  new Rectangle(0, 0, bmp.Width, bmp.Height),
		  ImageLockMode.ReadOnly,  // 書き込むときはReadAndWriteで
		  PixelFormat.Format32bppArgb
		);
		if (bmpData.Stride < 0)
			throw new Exception();//変な形式のが送られてくることはありえないはずだが一応
		byte[] pixels = new byte[bmpData.Stride * bmp.Height];
		try
		{
			nint ptr = bmpData.Scan0;
			Marshal.Copy(ptr, pixels, 0, pixels.Length);
		}
		finally
		{
			bmp.UnlockBits(bmpData);

		}
		return pixels;
	}

	/*
	/// <summary>
	/// GTOARRAY int ID, var array
	/// エラーチェックは呼び出し元でのみ行う
	/// <returns></returns>
	public bool GBitmapToInt64Array(long[,] array, int xstart, int ystart)
	{
		if (canvas == null || Bitmap == null)
			throw new NullReferenceException();
		int w = Bitmap.Width;
		int h = Bitmap.Height;
		if (xstart + w > array.GetLength(0) || ystart + h > array.GetLength(1))
			return false;
		Rectangle rect = new(0, 0, w, h);
		BitmapData bmpData =
			Bitmap.LockBits(rect, ImageLockMode.ReadOnly,
			PixelFormat.Format32bppArgb);
		nint ptr = bmpData.Scan0;
		byte[] rgbValues = new byte[w * h * 4];
		Marshal.Copy(ptr, rgbValues, 0, rgbValues.Length);
		Bitmap.UnlockBits(bmpData);
		int i = 0;
		for (int y = 0; y < h; y++)
		{
			for (int x = 0; x < w; x++)
			{
				array[x + xstart, y + ystart] =
				rgbValues[i++] + //B
				((long)rgbValues[i++] << 8) + //G
				((long)rgbValues[i++] << 16) + //R
				((long)rgbValues[i++] << 24);  //A
			}
		}
		return true;
	}


	/// <summary>
	/// GFROMARRAY int ID, var array
	/// エラーチェックは呼び出し元でのみ行う
	/// <returns></returns>
	public bool GByteArrayToBitmap(long[,] array, int xstart, int ystart)
	{
		if (canvas == null || Bitmap == null)
			throw new NullReferenceException();
		int w = Bitmap.Width;
		int h = Bitmap.Height;
		if (xstart + w > array.GetLength(0) || ystart + h > array.GetLength(1))
			return false;

		byte[] rgbValues = new byte[w * h * 4];
		int i = 0;
		for (int y = 0; y < h; y++)
		{
			for (int x = 0; x < w; x++)
			{
				long c = array[x + xstart, y + ystart];
				rgbValues[i++] = (byte)(c & 0xFF);//B
				rgbValues[i++] = (byte)(c >> 8 & 0xFF);//G
				rgbValues[i++] = (byte)(c >> 16 & 0xFF);//R
				rgbValues[i++] = (byte)(c >> 24 & 0xFF);//A
			}
		}
		Rectangle rect = new(0, 0, w, h);
		BitmapData bmpData =
			Bitmap.LockBits(rect, ImageLockMode.WriteOnly,
			PixelFormat.Format32bppArgb);
		nint ptr = bmpData.Scan0;
		Marshal.Copy(rgbValues, 0, ptr, rgbValues.Length);
		Bitmap.UnlockBits(bmpData);
		return true;
	}
	*/
	#endregion

	public void Load()
	{
		if (RealBitmap != null)
			return;

		if (drawImgList == null)
			return;

		RealBitmap = new SKBitmap(size.Width, size.Height);
		canvas = new SKCanvas(RealBitmap);

		foreach (Tuple<ASprite, Rectangle> tuple in drawImgList)
			tuple.Item1.GraphicsDraw(canvas, tuple.Item2);

		lock (AppContents.tempLoadedGraphicsImages)
			AppContents.tempLoadedGraphicsImages.Add(this);
		return;
	}

}
