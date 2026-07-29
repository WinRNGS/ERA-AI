using MinorShift.Emuera.Runtime.Utils;
using MinorShift.Emuera.Runtime.Config;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using trerror = MinorShift.Emuera.Runtime.Utils.EvilMask.Lang.Error;
using SkiaSharp;
using SkiaSharp.Views.Desktop;

namespace MinorShift.Emuera.UI.Game.Image;


internal abstract class ASprite : AContentItem, IDisposable
{
	public ASprite(string name, Size size)
		: base(name)
	{
		if (size.Width < 0)
			size.Width = -size.Width;
		if (size.Height < 0)
			size.Height = -size.Height;
		DestBaseSize = size;
	}
	public abstract SKColor SpriteGetColor(int x, int y);
	/// <summary>
	/// 出力される標準のサイズ。正の値のみ。
	/// </summary>
	public readonly Size DestBaseSize;

	/// <summary>
	/// 出力時の位置調整。拡大縮小して出力する場合には同じ比率で調整する。
	/// </summary>
	public Point DestBasePosition;


	public abstract void GraphicsDraw(SKCanvas g, Point offset);
	public abstract void GraphicsDraw(SKCanvas g, Rectangle destRect);
	public abstract void GraphicsDraw(SKCanvas g, Rectangle destRect, SKColorFilter attr);
	public abstract void Dispose();
	public void Move(Point point) { DestBasePosition.Offset(point); }
}


internal abstract class ASpriteSingle : ASprite
{
	public ASpriteSingle(string name, AbstractImage img, Rectangle rect)
		: base(name, rect.Size)
	{
		SrcRectangle = rect;
		BaseImage = img;
	}
	public ASpriteSingle(string name, AbstractImage img, Rectangle rect, Size destSize)
		: base(name, destSize)
	{
		SrcRectangle = rect;
		BaseImage = img;
	}
	public AbstractImage BaseImage;

	/// <summary>
	/// ソース画像上の位置を指定する四角形。Width, Heightは負の値をとり得る
	/// </summary>
	public readonly Rectangle SrcRectangle;
	private SKBitmap Bitmap
	{
		get
		{
			if (BaseImage != null && BaseImage.IsCreated)
				return BaseImage.SKBitmap;
			return null;
		}
	}

	public override bool IsCreated
	{
		get { return BaseImage != null && BaseImage.IsCreated; }
	}
	public override SKColor SpriteGetColor(int x, int y)
	{
		var bmp = Bitmap;
		if (bmp == null)
			return SKColors.Transparent;
		int bmpX = x + SrcRectangle.X;
		int bmpY = y + SrcRectangle.Y;
		if (bmpX < 0 || bmpX >= bmp.Width || bmpY < 0 || bmpY >= bmp.Height)
			return SKColors.Transparent;

		return bmp.GetPixel(bmpX, bmpY);
	}
	public override void Dispose()
	{
		BaseImage = null;
	}

	SKPaint _paint = new() {
		FilterQuality = (SKFilterQuality)Config.ImageQuality
	};

	public override void GraphicsDraw(SKCanvas g, Point offset)
	{
		var bmp = Bitmap;
		if (bmp == null) return;
		offset.Offset(DestBasePosition);
		_paint.FilterQuality = (SKFilterQuality)Config.ImageQuality;
		g.DrawBitmap(bmp, SrcRectangle.ToSKRect(), SKRect.Create(offset.ToSKPoint(), SrcRectangle.Size.ToSKSize()), _paint);
	}
	public override void GraphicsDraw(SKCanvas g, Rectangle destRect)
	{
		var bmp = Bitmap;
		if (bmp == null) return;
		if (!DestBasePosition.IsEmpty)
		{
			destRect.X = destRect.X + DestBasePosition.X * destRect.Width / DestBaseSize.Width;
			destRect.Y = destRect.Y + DestBasePosition.Y * destRect.Height / DestBaseSize.Height;
			destRect.Width = destRect.Width * SrcRectangle.Width / DestBaseSize.Width;
			destRect.Height = destRect.Height * SrcRectangle.Height / DestBaseSize.Height;
		}
		var sx = Math.Sign(destRect.Width);
		var sy = Math.Sign(destRect.Height);
		if (sx != 1 || sy != 1)
		{
			var absW = Math.Abs(destRect.Width);
			var absH = Math.Abs(destRect.Height);
			using var flippedBitmap = new SKBitmap(absW, absH);
			using var canvas = new SKCanvas(flippedBitmap);
			canvas.Scale(sx, sy, absW / 2, absH / 2);
			_paint.FilterQuality = (SKFilterQuality)Config.ImageQuality;
			canvas.DrawBitmap(bmp, SrcRectangle.ToSKRect(), SKRect.Create(0, 0, absW, absH), _paint);
			var point = destRect.Location.ToSKPoint();
			if (sx < 0) point.X -= absW;
			if (sy < 0) point.Y -= absH;
			g.DrawBitmap(flippedBitmap, point, _paint);
		}
		else
		{
			_paint.FilterQuality = (SKFilterQuality)Config.ImageQuality;
			g.DrawBitmap(bmp, SrcRectangle.ToSKRect(), destRect.ToSKRect(), _paint);
		}
	}

	public override void GraphicsDraw(SKCanvas g, Rectangle destRect, SKColorFilter attr)
	{
		var bmp = Bitmap;
		if (bmp == null) return;
		if (!DestBasePosition.IsEmpty)
		{
			destRect.X = destRect.X + DestBasePosition.X * destRect.Width / DestBaseSize.Width;
			destRect.Y = destRect.Y + DestBasePosition.Y * destRect.Height / DestBaseSize.Height;
			destRect.Width = destRect.Width * SrcRectangle.Width / DestBaseSize.Width;
			destRect.Height = destRect.Height * SrcRectangle.Height / DestBaseSize.Height;
		}
		var sx = Math.Sign(destRect.Width);
		var sy = Math.Sign(destRect.Height);
		_paint.FilterQuality = (SKFilterQuality)Config.ImageQuality;
		_paint.ColorFilter = attr;
		if (sx != 1 || sy != 1)
		{
			var absW = Math.Abs(destRect.Width);
			var absH = Math.Abs(destRect.Height);
			using var flippedBitmap = new SKBitmap(absW, absH);
			using var canvas = new SKCanvas(flippedBitmap);
			canvas.Scale(sx, sy, absW / 2, absH / 2);
			canvas.DrawBitmap(bmp, SrcRectangle.ToSKRect(), SKRect.Create(0, 0, absW, absH), _paint);
			var point = destRect.Location.ToSKPoint();
			if (sx < 0) point.X -= absW;
			if (sy < 0) point.Y -= absH;
			g.DrawBitmap(flippedBitmap, point, _paint);
		}
		else
		{
			g.DrawBitmap(bmp, SrcRectangle.ToSKRect(), destRect.ToSKRect(), _paint);
		}
		_paint.ColorFilter = null;
	}

}

/// <summary>
/// ERB中で作るGを元にしたSprite。GDI非対応
/// </summary>
internal sealed class SpriteG : ASpriteSingle
{
	public readonly int SourceGID;

	public SpriteG(string name, GraphicsImage gra, Rectangle rect)
		: base(name, gra, rect)
	{
		SourceGID = gra.ID;
	}
	public SpriteG(string name, GraphicsImage gra, Rectangle rect, Point pos, Size destSize) : base(name, gra, rect, destSize)
	{
		DestBasePosition = pos;
		SourceGID = gra.ID;
	}
	public bool useImgList { get { return (BaseImage as GraphicsImage)?.useImgList ?? false; } }
	public List<Tuple<ASprite, Rectangle>> drawImgList { get { return (BaseImage as GraphicsImage)?.drawImgList; } }
	public bool isBaseImage(GraphicsImage gImg)
	{
		return BaseImage as GraphicsImage == gImg;
	}
}

/// <summary>
/// ConstImage(csvから作るファイル占有型ベースイメージ)をもとにしたSprite
/// </summary>
internal sealed class SpriteF : ASpriteSingle
{
	public SpriteF(string name, ConstImage image, Rectangle rect, Point pos, Size destSize)
		: base(name, image, rect, destSize)
	{
		DestBasePosition = pos;
	}
}

/// <summary>
/// AnimeするSprite。中身はほぼSprite
/// </summary>
internal sealed class SpriteAnime : ASprite
{
	public SpriteAnime(string name, Size size)
		: base(name, size)
	{
		FrameList = [];
		totaltime = 0;
	}
	private sealed class AnimeFrame : IDisposable
	{
		public int index;
		public AbstractImage BaseImage;
		public Rectangle SrcRectangle;
		public Point Offset;
		public int DelayTimeMs;
		public void Normalize(Size parentSize)
		{
			Rectangle rect = Rectangle.Intersect(new Rectangle(Offset, SrcRectangle.Size), new Rectangle(new Point(), parentSize));
			if (rect.IsEmpty)
			{
				BaseImage = null;
				return;
			}
			Offset.X = rect.X;
			Offset.Y = rect.Y;
			SrcRectangle.Width = rect.Width;
			SrcRectangle.Height = rect.Height;
		}
		public void Dispose()
		{
			BaseImage = null;
		}
	}
	List<AnimeFrame> FrameList;
	public long totaltime;

	internal bool AddFrame(AbstractImage parentImage, Rectangle rect, Point pos, int delay)
	{
		AnimeFrame frame = new()
		{
			index = FrameList.Count,
			BaseImage = parentImage,
			SrcRectangle = rect,
			Offset = pos
		};

		if (delay <= 0)
			delay = 1;
		frame.DelayTimeMs = delay;
		frame.Normalize(DestBaseSize);
		totaltime += delay;
		FrameList.Add(frame);
		return true;
	}

	/// <summary>
	/// アニメの経過時間を削除して最初からやり直す
	/// </summary>
	internal void ResetTime()
	{
		startTime = 0;
		lastFrameTime = 0;
		lastFrame = -1;
	}

	long startTime;
	long lastFrameTime;
	int lastFrame = -1;
	private bool _paused;
	private long _pausedElapsed;

	internal void PauseAnimation()
	{
		if (_paused) return;
		_paused = true;
		if (lastFrame >= 0)
			_pausedElapsed = (long)Stopwatch.GetElapsedTime(startTime).TotalMilliseconds;
	}

	internal void ResumeAnimation()
	{
		if (!_paused) return;
		_paused = false;
		if (lastFrame >= 0)
			startTime = Stopwatch.GetTimestamp() - (long)(_pausedElapsed * Stopwatch.Frequency / 1000);
	}

	private AnimeFrame GetCurrentFrame()
	{
		if (totaltime <= 0)
			return null;
#if DEBUG
		if (FrameList.Count == 0)
			throw new ExeEE(trerror.EmptyFramelist.Text);
		if (lastFrame >= FrameList.Count)
			throw new ExeEE(trerror.OoRLasframe.Text);
#endif
		var now = Stopwatch.GetTimestamp();

		if (lastFrame == -1)
		{
			startTime = now;
			lastFrameTime = now;
			lastFrame = 0;
			return FrameList[0];
		}

		if (Stopwatch.GetElapsedTime(lastFrameTime, now).TotalMilliseconds < 1 && lastFrame >= 0)
			return FrameList[lastFrame];

		lastFrameTime = now;
		long elapsedTime = (long)Stopwatch.GetElapsedTime(startTime, now).TotalMilliseconds % totaltime;
		foreach (AnimeFrame frame in FrameList)
		{
			elapsedTime -= frame.DelayTimeMs;
			if (elapsedTime <= 0)
			{
				lastFrame = frame.index;
				return frame;
			}
		}
		throw new ExeEE(trerror.SpriteTimeOut.Text);
	}

	public override bool IsCreated
	{
		get { return true; }
	}

	internal bool HasGraphicsImageFrame()
	{
		foreach (var frame in FrameList)
			if (frame.BaseImage is GraphicsImage)
				return true;
		return false;
	}

	public override void Dispose()
	{
		foreach (var frame in FrameList)
			frame.Dispose();
		FrameList.Clear();
		totaltime = 0;
		lastFrame = -1;
	}


	public override SKColor SpriteGetColor(int x, int y)
	{
		throw new NotSupportedException();
		//Bitmap bmp = this.Bitmap;
		//if (bmp == null)
		//	return Color.Transparent;
		//int bmpX = x + SrcRectangle.X;
		//int bmpY = y + SrcRectangle.Y;
		//if (bmpX < 0 || bmpX >= bmp.Width || bmpY < 0 || bmpY >= bmp.Height)
		//	return Color.Transparent;

		//return bmp.GetPixel(bmpX, bmpY);
	}


	public override void GraphicsDraw(SKCanvas g, Point offset)
	{
		AnimeFrame frame = GetCurrentFrame();
		if (frame == null || frame.BaseImage == null || !frame.BaseImage.IsCreated)
			return;
		offset.Offset(DestBasePosition);
		offset.Offset(frame.Offset);
		Rectangle destRect = new(offset, frame.SrcRectangle.Size);
		using SKPaint paint = new() { FilterQuality = (SKFilterQuality)Config.ImageQuality };
		g.DrawBitmap(frame.BaseImage.SKBitmap, frame.SrcRectangle.ToSKRect(), 
			SKRect.Create(offset.ToSKPoint(), frame.SrcRectangle.Size.ToSKSize()), paint);
	}

	public override void GraphicsDraw(SKCanvas g, Rectangle destRect)
	{
		AnimeFrame frame = GetCurrentFrame();
		if (frame == null || frame.BaseImage == null || !frame.BaseImage.IsCreated)
			return;
		if (!DestBasePosition.IsEmpty)
		{
			destRect.X = destRect.X + (DestBasePosition.X + frame.Offset.X) * destRect.Width / DestBaseSize.Width;
			destRect.Y = destRect.Y + (DestBasePosition.Y + frame.Offset.Y) * destRect.Height / DestBaseSize.Height;
			destRect.Width = frame.SrcRectangle.Width * destRect.Width / DestBaseSize.Width;
			destRect.Height = frame.SrcRectangle.Height * destRect.Height / DestBaseSize.Height;
		}
		using SKPaint paint = new() { FilterQuality = (SKFilterQuality)Config.ImageQuality };
		g.DrawBitmap(frame.BaseImage.SKBitmap, frame.SrcRectangle.ToSKRect(), destRect.ToSKRect(), paint);
	}

	public override void GraphicsDraw(SKCanvas g, Rectangle destRect, SKColorFilter attr)
	{
		AnimeFrame frame = GetCurrentFrame();
		if (frame == null || frame.BaseImage == null || !frame.BaseImage.IsCreated)
			return;
		if (!DestBasePosition.IsEmpty)
		{
			destRect.X = destRect.X + (DestBasePosition.X + frame.Offset.X) * destRect.Width / DestBaseSize.Width;
			destRect.Y = destRect.Y + (DestBasePosition.Y + frame.Offset.Y) * destRect.Height / DestBaseSize.Height;
			destRect.Width = frame.SrcRectangle.Width * destRect.Width / DestBaseSize.Width;
			destRect.Height = frame.SrcRectangle.Height * destRect.Height / DestBaseSize.Height;
		}
		using SKPaint paint = new() { FilterQuality = (SKFilterQuality)Config.ImageQuality, ColorFilter = attr };
		g.DrawBitmap(frame.BaseImage.SKBitmap, frame.SrcRectangle.ToSKRect(), destRect.ToSKRect(), paint);
	}
}

internal sealed class SpriteAnimated : ASprite, IFileBacked
{
	private readonly string filepath;
	private readonly int frameCount;
	private readonly int totalDuration;
	private readonly int[] frameTimestamps;

	private SKBitmap[] frames;
	private bool isEvicted;
	private long startTime;
	private readonly Rectangle srcRect;

	public SpriteAnimated(string name, string filepath, Rectangle srcRect, Size destSize, int width, int height, int count, int[] delays)
		: base(name, destSize)
	{
		this.filepath = filepath;
		this.srcRect = srcRect.IsEmpty ? new Rectangle(0, 0, width, height) : srcRect;
		this.frameCount = count;

		this.frameTimestamps = new int[count];
		int cumulative = 0;
		for (int i = 0; i < count; i++)
		{
			cumulative += delays[i];
			this.frameTimestamps[i] = cumulative;
		}
		this.totalDuration = cumulative;

		this.frames = Array.Empty<SKBitmap>();
		this.isEvicted = true;
		this.startTime = Stopwatch.GetTimestamp();

		if (frameCount > 1)
		{
			AnimSpriteCache.RegisterSprite(this);
			AnimSpriteCache.Add(filepath, this);
		}
	}

	private void EnsureLoaded()
	{
		if (frameCount <= 0) return;
		if (!isEvicted && frames.Length > 0) return;

		var decoded = AnimatedImageHelper.Decode(filepath);
		if (decoded != null)
		{
			frames = new SKBitmap[decoded.Count];
			for (int i = 0; i < decoded.Count; i++)
				frames[i] = decoded[i].Bitmap;
		}
		isEvicted = false;
	}

	public string FilePath => filepath;
	public bool IsEvicted => isEvicted;
	public int FrameCount => frameCount;
	public override bool IsCreated => frameCount > 0;

	public SKBitmap GetFrame(int index)
	{
		EnsureLoaded();
		if (frameCount > 1) AnimSpriteCache.Touch(filepath);
		if (index < 0 || index >= frameCount || frames == null || frames.Length == 0) return null;
		return frames[index];
	}

	public int GetCurrentFrameIndex()
	{
		if (totalDuration <= 0 || frameCount <= 0) return 0;
		if (startTime == 0) startTime = Stopwatch.GetTimestamp();

		long elapsed = (long)Stopwatch.GetElapsedTime(startTime).TotalMilliseconds % totalDuration;
		for (int i = 0; i < frameTimestamps.Length; i++)
		{
			if (elapsed < frameTimestamps[i]) return i;
		}
		return frameCount - 1;
	}

	public void Evict()
	{
		if (frames == null) return;
		foreach (var frame in frames) frame?.Dispose();
		frames = Array.Empty<SKBitmap>();
		isEvicted = true;
	}

	public void Reload()
	{
		if (!isEvicted) return;
		startTime = Stopwatch.GetTimestamp();
		EnsureLoaded();
		if (frameCount > 1) AnimSpriteCache.Touch(filepath);
	}

	public override void Dispose()
	{
		Evict();
		if (frameCount > 1) AnimSpriteCache.Evict(filepath);
	}

	public override void GraphicsDraw(SKCanvas g, Rectangle destRect)
	{
		if (isEvicted || frames.Length == 0) EnsureLoaded();
		var frame = GetFrame(GetCurrentFrameIndex());
		if (frame != null)
		{
			using SKPaint paint = new() { FilterQuality = (SKFilterQuality)Config.ImageQuality };
			g.DrawBitmap(frame, srcRect.ToSKRect(), destRect.ToSKRect(), paint);
		}
	}

	public override void GraphicsDraw(SKCanvas g, Point offset) { throw new NotSupportedException(); }
	public override void GraphicsDraw(SKCanvas g, Rectangle destRect, SKColorFilter attr)
	{
		if (isEvicted || frames.Length == 0) EnsureLoaded();
		var frame = GetFrame(GetCurrentFrameIndex());
		if (frame != null)
		{
			using SKPaint paint = new() { FilterQuality = (SKFilterQuality)Config.ImageQuality, ColorFilter = attr };
			g.DrawBitmap(frame, srcRect.ToSKRect(), destRect.ToSKRect(), paint);
		}
	}
	public override SKColor SpriteGetColor(int x, int y) { throw new NotSupportedException(); }
}

internal sealed class AnimSpriteCache
{
	private static readonly LinkedList<string> lruOrder = new();
	private static readonly Dictionary<string, LinkedListNode<string>> lruNodes = new();
	private static readonly Dictionary<string, WeakReference<IFileBacked>> spriteRefs = new();
	private static readonly object lockObj = new();
	private const int MaxAnimations = 6;

	public static void Add(string filepath, IFileBacked sprite)
	{
		lock (lockObj)
		{
			if (lruNodes.TryGetValue(filepath, out var node))
			{
				lruOrder.Remove(node);
				lruNodes[filepath] = lruOrder.AddLast(filepath);
				spriteRefs[filepath] = new WeakReference<IFileBacked>(sprite);
				return;
			}
			while (lruOrder.Count >= MaxAnimations)
			{
				var oldest = lruOrder.First;
				if (oldest != null)
				{
					lruOrder.RemoveFirst();
					var oldFilepath = oldest.Value;
					lruNodes.Remove(oldFilepath);
					if (spriteRefs.TryGetValue(oldFilepath, out var weakRef) && weakRef.TryGetTarget(out var oldSprite))
						oldSprite.Evict();
					spriteRefs.Remove(oldFilepath);
				}
			}
			lruNodes[filepath] = lruOrder.AddLast(filepath);
			spriteRefs[filepath] = new WeakReference<IFileBacked>(sprite);
		}
	}

	public static void RegisterSprite(IFileBacked sprite)
	{
		if (sprite == null) return;
		lock (lockObj)
		{
			var filepath = sprite.FilePath;
			if (lruNodes.ContainsKey(filepath))
				spriteRefs[filepath] = new WeakReference<IFileBacked>(sprite);
		}
	}

	public static void Touch(string filepath)
	{
		lock (lockObj)
		{
			if (!lruNodes.TryGetValue(filepath, out var node)) return;
			lruOrder.Remove(node);
			lruNodes[filepath] = lruOrder.AddLast(filepath);
		}
	}

	public static void Evict(string filepath)
	{
		lock (lockObj)
		{
			if (!lruNodes.TryGetValue(filepath, out var node)) return;
			lruOrder.Remove(node);
			lruNodes.Remove(filepath);
			if (spriteRefs.TryGetValue(filepath, out var weakRef) && weakRef.TryGetTarget(out var sprite))
				sprite.Evict();
			spriteRefs.Remove(filepath);
		}
	}
}

public static class ImageResourceCache
{
	private static readonly LinkedList<string> lruOrder = new();
	private static readonly Dictionary<string, LinkedListNode<string>> lruNodes = new();
	private static readonly object cacheLock = new object();
	private static int maxCacheFiles = 200;

	public static void Touch(string filepath)
	{
		lock (cacheLock)
		{
			if (!lruNodes.TryGetValue(filepath, out var node)) return;
			lruOrder.Remove(node);
			lruNodes[filepath] = lruOrder.AddLast(filepath);
		}
	}

	public static void Add(string filepath)
	{
		lock (cacheLock)
		{
			if (lruNodes.ContainsKey(filepath))
			{
				Touch(filepath);
				return;
			}
			while (lruOrder.Count >= maxCacheFiles) EvictOldest();
			var node = lruOrder.AddLast(filepath);
			lruNodes[filepath] = node;
		}
	}

	private static void EvictOldest()
	{
		if (lruOrder.Count == 0) return;
		var oldest = lruOrder.First.Value;
		lruOrder.RemoveFirst();
		lruNodes.Remove(oldest);
		OnFileEvicted?.Invoke(oldest);
	}

	public static void Evict(string filepath)
	{
		lock (cacheLock)
		{
			if (!lruNodes.TryGetValue(filepath, out var node)) return;
			lruOrder.Remove(node);
			lruNodes.Remove(filepath);
			OnFileEvicted?.Invoke(filepath);
		}
	}

	public static event Action<string> OnFileEvicted;
}
