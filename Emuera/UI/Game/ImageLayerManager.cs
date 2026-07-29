using MinorShift.Emuera.UI.Game.Image;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Drawing;

namespace MinorShift.Emuera.UI.Game.Rendering;

internal sealed class ImageLayer
{
	public ASprite Image { get; set; }
	public long Depth { get; set; }
	public int X { get; set; }
	public int Y { get; set; }
	public int Width { get; set; }
	public int Height { get; set; }
	public int Opacity { get; set; } = 255;
	public float[]? ColorMatrix { get; set; }
	public bool FollowScroll { get; set; }
	public int InitialScrollY { get; set; }
	public bool IsOffScreen { get; set; }
}

internal sealed class ImageLayerManager
{
	private readonly List<ImageLayer> _layers = new();

	public IReadOnlyList<ImageLayer> Layers => _layers;

	public void SetLayer(string spriteName, long depth, int x, int y,
		int width, int height, int opacity, float[]? colorMatrix, bool followScroll, int currentScrollY)
	{
		var sprite = AppContents.GetSprite(spriteName);
		if (sprite == null) return;

		_layers.Add(new ImageLayer
		{
			Image = sprite,
			Depth = depth,
			X = x,
			Y = y,
			Width = width,
			Height = height,
			Opacity = Math.Clamp(opacity, 0, 255),
			ColorMatrix = colorMatrix,
			FollowScroll = followScroll,
			InitialScrollY = currentScrollY
		});
	}

	public void ClearLayer(long depth)
	{
		_layers.RemoveAll(l => l.Depth == depth);
	}

	public void ClearAll()
	{
		_layers.Clear();
	}

	public bool Exists(long depth) => _layers.Exists(l => l.Depth == depth);

	public void DrawTo(SKCanvas canvas, int viewportW, int viewportH, int scrollY)
	{
		DrawLayersAtDepth(canvas, viewportW, viewportH, scrollY, null);
	}

	/// <summary>
	/// Draw ImageLayers at the specified depth only. If depth is null, draw all layers (legacy behavior).
	/// </summary>
	public void DrawLayersAtDepth(SKCanvas canvas, int viewportW, int viewportH, int scrollY, int? depth)
	{
		if (_layers.Count == 0) return;

		// Sort by depth (ascending), same depth preserves insertion order (stable sort)
		var sortedLayers = new List<ImageLayer>(_layers);
		sortedLayers.Sort((a, b) => a.Depth.CompareTo(b.Depth));

		foreach (var layer in sortedLayers)
		{
			// If a specific depth is requested, skip layers at other depths
			if (depth.HasValue && layer.Depth != depth.Value)
				continue;

			if (layer.Image == null || !layer.Image.IsCreated) continue;

			int drawW = layer.Width > 0 ? layer.Width : layer.Image.DestBaseSize.Width;
			int drawH = layer.Height > 0 ? layer.Height : layer.Image.DestBaseSize.Height;

			int drawX = layer.X;
			int drawY = layer.Y + viewportH - drawH;
			if (layer.FollowScroll)
				drawY -= (scrollY - layer.InitialScrollY);

			if (drawX + drawW <= 0 || drawX >= viewportW ||
				drawY + drawH <= 0 || drawY >= viewportH)
			{
				layer.IsOffScreen = true;
				if (layer.Image is SpriteAnime anime)
					anime.PauseAnimation();
				continue;
			}

			layer.IsOffScreen = false;
			if (layer.Image is SpriteAnime anime2)
				anime2.ResumeAnimation();

			var destRect = new Rectangle(drawX, drawY, drawW, drawH);
			using SKColorFilter? filter = BuildFilter(layer);
			layer.Image.GraphicsDraw(canvas, destRect, filter);
		}
	}

	/// <summary>
	/// Get all unique depth values used by current layers, sorted descending (same order as escapedParts).
	/// </summary>
	public List<int> GetDepths()
	{
		var depths = new HashSet<int>();
		foreach (var layer in _layers)
			depths.Add((int)layer.Depth);
		var result = new List<int>(depths);
		result.Sort((a, b) => -a.CompareTo(b)); // descending, same as escapedParts
		return result;
	}

	private static SKColorFilter? BuildFilter(ImageLayer layer)
	{
		if (layer.ColorMatrix != null)
			return SKColorFilter.CreateColorMatrix(layer.ColorMatrix);
		if (layer.Opacity < 255)
		{
			float alpha = layer.Opacity / 255.0f;
			return SKColorFilter.CreateColorMatrix([
				1, 0, 0, 0, 0,
				0, 1, 0, 0, 0,
				0, 0, 1, 0, 0,
				0, 0, 0, alpha, 0,
			]);
		}
		return null;
	}
}
