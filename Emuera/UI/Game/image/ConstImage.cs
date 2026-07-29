using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using SkiaSharp;

namespace MinorShift.Emuera.UI.Game.Image;

/// <summary>
/// 全局共享静态位图缓存池
/// 解决 Sprite Sheet (大图拼接) 模式下，同一文件被多次解码导致的内存爆炸问题
/// </summary>
public static class SharedBitmapCache
{
	private static readonly Dictionary<string, SKBitmap> cache = new();
	private static readonly LinkedList<string> lru = new();
	private const int MaxCache = 200;
	private static readonly object cacheLock = new object();

	public static SKBitmap Get(string filepath)
	{
		if (string.IsNullOrEmpty(filepath)) return null;
		lock (cacheLock)
		{
			if (cache.TryGetValue(filepath, out var bmp))
			{
				lru.Remove(filepath);
				lru.AddLast(filepath);
				return bmp;
			}

			try
			{
				bmp = SKBitmap.Decode(filepath);
				if (bmp == null) return null;

				if (cache.Count >= MaxCache)
				{
					var oldest = lru.First.Value;
					lru.RemoveFirst();
					if (cache.Remove(oldest, out var oldBmp))
					{
						oldBmp.Dispose();
					}
				}

				cache[filepath] = bmp;
				lru.AddLast(filepath);
				return bmp;
			}
			catch { return null; }
		}
	}

	public static bool GetInfo(string filepath, out int width, out int height)
	{
		width = height = 0;
		if (string.IsNullOrEmpty(filepath)) return false;

		lock (cacheLock)
		{
			if (cache.TryGetValue(filepath, out var bmp))
			{
				width = bmp.Width;
				height = bmp.Height;
				return true;
			}
		}

		try
		{
			using var codec = SKCodec.Create(filepath);
			if (codec != null)
			{
				width = codec.Info.Width;
				height = codec.Info.Height;
				return true;
			}
		}
		catch { }
		return false;
	}

	public static void Set(string filepath, SKBitmap bmp)
	{
		if (string.IsNullOrEmpty(filepath) || bmp == null) return;
		lock (cacheLock)
		{
			if (cache.TryGetValue(filepath, out var oldBmp))
			{
				oldBmp.Dispose();
				lru.Remove(filepath);
			}
			else if (cache.Count >= MaxCache)
			{
				var oldest = lru.First.Value;
				lru.RemoveFirst();
				if (cache.Remove(oldest, out var evictedBmp))
				{
					evictedBmp.Dispose();
				}
			}

			cache[filepath] = bmp;
			lru.AddLast(filepath);
		}
	}

	public static void Clear()
	{
		lock (cacheLock)
		{
			foreach (var bmp in cache.Values) bmp.Dispose();
			cache.Clear();
			lru.Clear();
		}
	}
}

internal sealed class ConstImage : AbstractImage
{
	public ConstImage(string name)
	{
		Name = name;
	}

	public readonly string Name;
	private string filepath;

	public int Width;
	public int Height;

	public string FilePath => filepath;

	internal void CreateFrom(string filepath, bool useGDI)
	{
		if (!string.IsNullOrEmpty(this.filepath)) throw new Exception();
		this.filepath = filepath;

		if (SharedBitmapCache.GetInfo(filepath, out int w, out int h))
		{
			Width = w;
			Height = h;
		}
	}

	internal void CreateFrom(SKBitmap bmp, string filepath, bool useGDI)
	{
		if (!string.IsNullOrEmpty(this.filepath)) throw new Exception();
		this.filepath = filepath;
		this.Width = bmp.Width;
		this.Height = bmp.Height;

		SharedBitmapCache.Set(filepath, bmp);
	}

	public override void Dispose()
	{
		if (canvas != null)
		{
			canvas.Dispose();
			canvas = null;
		}
	}

	public override bool IsCreated => !string.IsNullOrEmpty(filepath);

	public override SKBitmap SKBitmap
	{
		get
		{
			return SharedBitmapCache.Get(filepath);
		}
		set { }
	}
}