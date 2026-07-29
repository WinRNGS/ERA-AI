using MinorShift.Emuera.Runtime.Config;
using MinorShift.Emuera.Runtime.Utils;
using MinorShift.Emuera.Runtime.Utils.EvilMask;
using SkiaSharp;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using trerror = MinorShift.Emuera.Runtime.Utils.EvilMask.Lang.Error;

namespace MinorShift.Emuera.UI.Game.Image;

static class AppContents
{
	static ConcurrentDictionary<int, GraphicsImage> gList = [];

	private static SqliteConnection metaDb;

	public static HashSet<ConstImage> tempLoadedConstImages = [];
	public static HashSet<GraphicsImage> tempLoadedGraphicsImages = [];

	static AppContents()
	{
		metaDb = new SqliteConnection("Data Source=:memory:");
		metaDb.Open();
		using var cmd = metaDb.CreateCommand();
		cmd.CommandText = @"
			CREATE TABLE SpriteMeta (
				Name TEXT,
				FrameIndex INTEGER,
				FilePath TEXT,
				RectX INTEGER, RectY INTEGER, RectW INTEGER, RectH INTEGER,
				PosX INTEGER, PosY INTEGER,
				Delay INTEGER,
				DestW INTEGER, DestH INTEGER,
				IsAnime INTEGER,
				PRIMARY KEY (Name, FrameIndex)
			)";
		cmd.ExecuteNonQuery();
	}

	static public GraphicsImage GetGraphics(int i)
	{
		if (gList.TryGetValue(i, out GraphicsImage value))
			return value;
		GraphicsImage g = new(i);
		gList[i] = g;
		return g;
	}

	static public ASprite GetSprite(string name)
	{
		if (string.IsNullOrEmpty(name))
			return null;
		name = name.ToUpper(CultureInfo.InvariantCulture);

		if (activeSprites.TryGetValue(name, out ASprite sprite))
		{
			if (sprite is SpriteAnimated sa)
				AnimSpriteCache.Touch(sa.FilePath);
			return sprite;
		}

		return LoadSpriteFromMeta(name);
	}

	private class MetaRow
	{
		public string FilePath;
		public Rectangle Rect;
		public Point Pos;
		public int Delay;
		public Size DestSize;
		public bool IsAnimeHeader;
	}

	private static ASprite LoadSpriteFromMeta(string name)
	{
		using var cmd = metaDb.CreateCommand();
		cmd.CommandText = "SELECT * FROM SpriteMeta WHERE Name = @name ORDER BY FrameIndex ASC";
		cmd.Parameters.AddWithValue("@name", name);

		using var reader = cmd.ExecuteReader();
		List<MetaRow> rows = new List<MetaRow>();
		while (reader.Read())
		{
			rows.Add(new MetaRow
			{
				FilePath = reader.GetString(2),
				Rect = new Rectangle(reader.GetInt32(3), reader.GetInt32(4), reader.GetInt32(5), reader.GetInt32(6)),
				Pos = new Point(reader.GetInt32(7), reader.GetInt32(8)),
				Delay = reader.GetInt32(9),
				DestSize = new Size(reader.GetInt32(10), reader.GetInt32(11)),
				IsAnimeHeader = reader.GetInt32(12) == 1
			});
		}
		reader.Close();

		if (rows.Count == 0)
			return null;

		ASprite newSprite = null;

		if (rows[0].IsAnimeHeader)
		{
			Size destSize = rows[0].DestSize;
			SpriteAnime anime = new SpriteAnime(name, destSize);

			Dictionary<string, ConstImage> sharedConstImages = new();

			for (int i = 1; i < rows.Count; i++)
			{
				var r = rows[i];
				if (!File.Exists(r.FilePath)) continue;

				if (!sharedConstImages.TryGetValue(r.FilePath, out ConstImage img))
				{
					img = new ConstImage($"{name}_F{i}");
					img.CreateFrom(r.FilePath, false);
					sharedConstImages[r.FilePath] = img;
				}

				Rectangle fRect = r.Rect.Width == 0 ? new Rectangle(0, 0, img.Width, img.Height) : r.Rect;
				anime.AddFrame(img, fRect, r.Pos, r.Delay);
			}
			newSprite = anime;
		}
		else
		{
			var r = rows[0];
			if (!File.Exists(r.FilePath)) return null;

			if (AnimatedImageHelper.GetAnimInfo(r.FilePath, out int w, out int h, out int fCount, out int[] delays))
			{
				Size destSize = (r.DestSize.Width > 0 && r.DestSize.Height > 0) ? r.DestSize : r.Rect.Size;
				if (destSize.IsEmpty) destSize = new Size(w, h);

				newSprite = new SpriteAnimated(name, r.FilePath, r.Rect, destSize, w, h, fCount, delays);
			}
			else
			{
				ConstImage img = new ConstImage(name + "_BASE");
				img.CreateFrom(r.FilePath, false);

				Rectangle fRect = r.Rect.Width == 0 ? new Rectangle(0, 0, img.Width, img.Height) : r.Rect;
				Size destSize = (r.DestSize.Width > 0 && r.DestSize.Height > 0) ? r.DestSize : fRect.Size;
				newSprite = new SpriteF(name, img, fRect, r.Pos, destSize);
			}
		}

		if (newSprite != null)
		{
			activeSprites[name] = newSprite;
		}

		return newSprite;
	}

	static public void CreateSpriteG(string imgName, GraphicsImage parent, Rectangle rect, Point pos, Size destSize)
	{
		if (string.IsNullOrEmpty(imgName))
			return;
		imgName = imgName.ToUpper(CultureInfo.InvariantCulture);

		SpriteG newSprite = new SpriteG(imgName, parent, rect, pos, destSize);
		activeSprites[imgName] = newSprite;
	}

	static public void CreateSpriteAnime(string imgName, int w, int h)
	{
		if (string.IsNullOrEmpty(imgName))
			return;
		imgName = imgName.ToUpper(CultureInfo.InvariantCulture);
		SpriteAnime anime = new SpriteAnime(imgName, new Size(w, h));
		activeSprites[imgName] = anime;
	}

	public static bool CreateSpriteFromFileDynamic(string name, string filepath)
	{
		if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(filepath))
			return false;
		name = name.ToUpper(CultureInfo.InvariantCulture);

		if (activeSprites.TryGetValue(name, out _))
			return true;

		if (!File.Exists(filepath))
			return false;

		ASprite newSprite = null;
		if (AnimatedImageHelper.GetAnimInfo(filepath, out int w, out int h, out int fCount, out int[] delays))
		{
			newSprite = new SpriteAnimated(name, filepath, Rectangle.Empty, new Size(w, h), w, h, fCount, delays);
		}
		else
		{
			ConstImage img = new ConstImage(name + "_DYN");
			img.CreateFrom(filepath, false);
			newSprite = new SpriteF(name, img, new Rectangle(0, 0, img.Width, img.Height), Point.Empty, new Size(img.Width, img.Height));
		}

		if (newSprite != null)
		{
			activeSprites[name] = newSprite;
			return true;
		}
		return false;
	}

	static public bool GetSprite_OnlyCheckExists(string name)
	{
		if (string.IsNullOrEmpty(name))
			return false;
		name = name.ToUpper(CultureInfo.InvariantCulture);
		if (activeSprites.ContainsKey(name))
			return true;
		using var cmd = metaDb.CreateCommand();
		cmd.CommandText = "SELECT Name FROM SpriteMeta WHERE Name = @name";
		cmd.Parameters.AddWithValue("@name", name);
		using var reader = cmd.ExecuteReader();
		return reader.Read();
	}

	static public void SpriteDispose(string name)
	{
		if (string.IsNullOrEmpty(name))
			return;
		name = name.ToUpper(CultureInfo.InvariantCulture);
		if (activeSprites.TryRemove(name, out ASprite sprite))
		{
			sprite.Dispose();
		}
	}

	static public long SpriteDisposeAll(bool delCsvImage)
	{
		int sprites = activeSprites.Count;
		foreach (var s in activeSprites.Values)
			s.Dispose();
		activeSprites.Clear();

		SharedBitmapCache.Clear();

		return sprites;
	}

	static public Exception LoadContents(bool reload)
	{
		if (!Directory.Exists(Program.ContentDir))
			return null;
		try
		{
			if (reload)
			{
				using var clearCmd = metaDb.CreateCommand();
				clearCmd.CommandText = "DELETE FROM SpriteMeta";
				clearCmd.ExecuteNonQuery();

				SpriteDisposeAll(true);
			}

			var csvFiles = Directory.EnumerateFiles(Program.ContentDir, "*.csv", SearchOption.AllDirectories);
			using var trans = metaDb.BeginTransaction();
			using var insertCmd = metaDb.CreateCommand();
			insertCmd.Transaction = trans;
			insertCmd.CommandText = @"INSERT OR REPLACE INTO SpriteMeta
				(Name, FrameIndex, FilePath, RectX, RectY, RectW, RectH, PosX, PosY, Delay, DestW, DestH, IsAnime)
				VALUES (@name, @idx, @filepath, @rx, @ry, @rw, @rh, @px, @py, @delay, @dw, @dh, @isAnime)";

			var pName = insertCmd.Parameters.Add("@name", SqliteType.Text);
			var pIdx = insertCmd.Parameters.Add("@idx", SqliteType.Integer);
			var pPath = insertCmd.Parameters.Add("@filepath", SqliteType.Text);
			var pRx = insertCmd.Parameters.Add("@rx", SqliteType.Integer);
			var pRy = insertCmd.Parameters.Add("@ry", SqliteType.Integer);
			var pRw = insertCmd.Parameters.Add("@rw", SqliteType.Integer);
			var pRh = insertCmd.Parameters.Add("@rh", SqliteType.Integer);
			var pPx = insertCmd.Parameters.Add("@px", SqliteType.Integer);
			var pPy = insertCmd.Parameters.Add("@py", SqliteType.Integer);
			var pDelay = insertCmd.Parameters.Add("@delay", SqliteType.Integer);
			var pDw = insertCmd.Parameters.Add("@dw", SqliteType.Integer);
			var pDh = insertCmd.Parameters.Add("@dh", SqliteType.Integer);
			var pIsAnime = insertCmd.Parameters.Add("@isAnime", SqliteType.Integer);

			foreach (var path in csvFiles)
			{
				string directory = Path.GetDirectoryName(path) + "\\";
				string[] lines = File.ReadAllLines(path, EncodingHandler.DetectEncoding(path));

				Dictionary<string, int> frameCounters = new(Config.StrComper);

				foreach (var line in lines)
				{
					string str = line.Trim();
					if (str.Length == 0 || str.StartsWith(';'))
						continue;
					string[] tokens = str.Split(',');
					if (tokens.Length < 2)
						continue;

					string name = tokens[0].Trim().ToUpper(CultureInfo.InvariantCulture);
					string arg2 = tokens[1].Trim();

					pName.Value = name;
					pPath.Value = "";
					pRx.Value = 0; pRy.Value = 0; pRw.Value = 0; pRh.Value = 0;
					pPx.Value = 0; pPy.Value = 0; pDelay.Value = 0;
					pDw.Value = 0; pDh.Value = 0; pIsAnime.Value = 0;

					if (arg2.Equals("ANIME", StringComparison.OrdinalIgnoreCase))
					{
						if (tokens.Length >= 4)
						{
							int.TryParse(tokens[2], out int width);
							int.TryParse(tokens[3], out int height);
							if (width > 0 && height > 0)
							{
								pIdx.Value = -1;
								pDw.Value = width;
								pDh.Value = height;
								pIsAnime.Value = 1;
								insertCmd.ExecuteNonQuery();
								frameCounters[name] = 0;
							}
						}
						continue;
					}

					string fullPath = directory + arg2;
					pPath.Value = fullPath;

					if (tokens.Length >= 6)
					{
						int.TryParse(tokens[2], out int rx); pRx.Value = rx;
						int.TryParse(tokens[3], out int ry); pRy.Value = ry;
						int.TryParse(tokens[4], out int rw); pRw.Value = rw;
						int.TryParse(tokens[5], out int rh); pRh.Value = rh;
					}
					if (tokens.Length >= 8)
					{
						int.TryParse(tokens[6], out int px); pPx.Value = px;
						int.TryParse(tokens[7], out int py); pPy.Value = py;
					}
					if (tokens.Length >= 9)
					{
						int.TryParse(tokens[8], out int delay); pDelay.Value = delay;
					}
					if (tokens.Length >= 11)
					{
						int.TryParse(tokens[9], out int destW); pDw.Value = destW;
						int.TryParse(tokens[10], out int destH); pDh.Value = destH;
					}

					if (!frameCounters.ContainsKey(name))
						frameCounters[name] = 0;

					pIdx.Value = frameCounters[name]++;
					insertCmd.ExecuteNonQuery();
				}
			}
			trans.Commit();
		}
		catch (Exception e)
		{
			return e;
		}
		return null;
	}

	static public void UnloadContents()
	{
		SpriteDisposeAll(true);
		foreach (var graph in gList.Values)
			graph.GDispose();
		gList.Clear();
	}

	static public void UnloadGraphicList()
	{
		foreach (var graph in gList.Values)
			graph.GDispose();
		gList.Clear();

		List<string> keysToRemove = [];
		foreach (var kvp in activeSprites)
		{
			if (kvp.Value is SpriteG)
				keysToRemove.Add(kvp.Key);
			else if (kvp.Value is SpriteAnime anime && anime.HasGraphicsImageFrame())
				keysToRemove.Add(kvp.Key);
		}
		foreach (var key in keysToRemove)
		{
			if (activeSprites.TryRemove(key, out ASprite sprite))
				sprite.Dispose();
		}
	}

	static public void UnloadTempLoadedConstImageNames()
	{
		lock (tempLoadedConstImages)
		{
			foreach (ConstImage img in tempLoadedConstImages)
				img.Dispose();
			tempLoadedConstImages.Clear();
		}
	}

	static public void UnloadTempLoadedGraphicsImageNames()
	{
		lock (tempLoadedGraphicsImages)
		{
			foreach (GraphicsImage img in tempLoadedGraphicsImages)
				if (img.useImgList)
					img.UnLoad();
			tempLoadedGraphicsImages.Clear();
		}
	}

	private static ConcurrentDictionary<string, ASprite> activeSprites = new(Config.StrComper);
}