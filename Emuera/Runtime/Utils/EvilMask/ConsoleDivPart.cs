using MinorShift.Emuera.Runtime.Config;
using MinorShift.Emuera.UI.Game;
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text;
using static MinorShift.Emuera.Runtime.Utils.EvilMask.Shape;
using static MinorShift.Emuera.Runtime.Utils.EvilMask.Utils;
using SkiaSharp;
using SkiaSharp.Views.Desktop;


namespace MinorShift.Emuera.Runtime.Utils.EvilMask;

class ConsoleDivPart : AConsoleDisplayNode
{
	public ConsoleDivPart(MixedNum xPos, MixedNum yPos, MixedNum width, MixedNum height, int depth, int color, StyledBoxModel box, bool isRelative, DisplayMode displayMode, ConsoleDisplayLine[] childs)
	{
		backgroundColor = color != int.MinValue ? Color.FromArgb(color) : Color.Transparent;
		StringBuilder sb = new();
		width.num = Math.Abs(width.num);
		if (height != null)
			height.num = Math.Abs(height.num);
		sb.Append("<div");
		AddTagMixedNumArg(sb, "xpos", xPos);
		AddTagMixedNumArg(sb, "ypos", yPos);
		AddTagMixedNumArg(sb, "width", width);
		AddColorParam(sb, "color", backgroundColor);
		AddTagMixedNumArg(sb, "height", height);
		if (box != null)
		{
			AddTagMixedParam(sb, "margin", box.margin);
			MixedNum4ToInt4(box.margin, ref margin);
			AddTagMixedParam(sb, "padding", box.padding);
			MixedNum4ToInt4(box.padding, ref padding);
			AddTagMixedParam(sb, "border", box.border);
			MixedNum4ToInt4(box.border, ref border);
			AddTagMixedParam(sb, "radius", box.radius);
			MixedNum4ToInt4(box.radius, ref radius);
			if (box.color != null)
			{
				borderColors = new Color[4];
				for (int i = 0; i < 4; i++)
					borderColors[i] = Color.FromArgb(box.color[i]);
				AddColorParam4(sb, "bcolor", borderColors);
			}
			else if (box.border != null)
			{
				// WinForms original uses Config.ForeColor when bcolor is omitted
				borderColors = new Color[4];
				for (int i = 0; i < 4; i++)
					borderColors[i] = Config.Config.ForeColor;
			}
		}
		sb.Append('>');
		altHeadTag = sb.ToString();
		Text = string.Empty;
		xOffset = MixedNum.ToPixel(xPos, 0);
		#region EE_div各要素の修正
		if (margin != null) divXOffset += margin[Direction.Left];
		if (padding != null) divXOffset += padding[Direction.Left];
		if (border != null) divXOffset += border[Direction.Left];
		#endregion
		PointY = MixedNum.ToPixel(yPos, 0);

		#region EE_div各要素の修正
		if (margin != null) yOffset += margin[Direction.Top];
		if (padding != null) yOffset += padding[Direction.Top];
		if (border != null) yOffset += border[Direction.Top];
		#endregion

		this.width = MixedNum.ToPixel(width, 0);
		children = childs;
		Depth = depth;
		IsRelative = isRelative;
		Display = displayMode;

		// height auto: calculate from content lines + padding/border
		if (height != null)
		{
			Height = MixedNum.ToPixel(height, 0);
		}
		else
		{
			int padTop = 0, padBottom = 0;
			if (padding != null) { padTop = padding[Direction.Top]; padBottom = padding[Direction.Bottom]; }
			if (border != null) { padTop += border[Direction.Top]; padBottom += border[Direction.Bottom]; }
			Height = childs.Length * Config.Config.LineHeight + padTop + padBottom;
		}
		
		ShiftChildrenX(PointX + xOffset + divXOffset);
	}
	int pointX;
	int xOffset;
	#region EE_div各要素の修正
	int divXOffset;
	int yOffset;
	#endregion
	int width;
	public override int PointX
	{
		get { return pointX; }
		set
		{
			var diff = value - pointX;
			pointX = value;
			#region EE_div各要素の修正
			//foreach (var child in children)
			//    child.ShiftPositionX(value + xOffset + divXOffset);
			ShiftChildrenX(diff);
			#endregion
		}
	}
	int PointY;
	int Height;
	int[] margin, padding, radius, border;
	Color[] borderColors;
	Color backgroundColor;
	string altHeadTag;
	readonly ConsoleDisplayLine[] children;
	public bool IsEscaped { get; set; }
	public override int Top { get { return PointY; } }
	public override int Bottom { get { return PointY + Height; } }
	public bool IsRelative { get; private set; }
	public DisplayMode Display { get; private set; }
	public ConsoleDisplayLine[] Children { get { return children; } }

	public override bool CanDivide => false;
	public ConsoleButtonString TestChildHitbox(int pointX, int pointY, int relPointY)
	{
		ConsoleButtonString pointing = null;
		var rect = new Rectangle(PointX + xOffset, relPointY + PointY + yOffset, width, Height);
		
		// 如果鼠标根本不在这个 Div 内部，直接返回
		if (!rect.Contains(pointX, pointY)) return null;

		for (int i = 0; i < children.Length; i++)
		{
			var line = children[i];
			// 计算该行的基准 Y 坐标
			int actualRelPointY = rect.Y + (i * Config.Config.LineHeight);

			// 倒序遍历该行的按钮（与原逻辑保持一致，后画的在最上层）
			for (int b = line.Buttons.Length - 1; b >= 0; b--)
			{
				ConsoleButtonString button = line.Buttons[b];
				if (button == null || button.StrArray == null) continue;

				// 快速 X 轴包围盒测试
				if (pointX >= button.PointX && pointX <= button.PointX + button.Width)
				{
					foreach (AConsoleDisplayNode part in button.StrArray)
					{
						if (part == null) continue;
						
						// 精确命中测试
						if (pointX >= part.PointX && pointX <= part.PointX + part.Width &&
							pointY >= actualRelPointY + part.Top && pointY <= actualRelPointY + part.Bottom)
						{
							pointing = button;
							if (pointing.IsButton)
								return pointing;
						}
					}
				}
			}
		}
		return pointing;
	}
	public override void DrawTo(SKCanvas graph, SKPoint point, bool isSelecting, bool isBackLog, bool isFocus, TextDrawingMode mode, bool isButton = false)
	{
		if (GlobalStatic.EMediator.Console.Window == null) return;
		var rect = IsRelative
			? new Rectangle(PointX + xOffset, (int)point.Y + PointY, width + 2, Height)
			: Display switch
			{
				DisplayMode.AbsoluteLeftTop => new Rectangle(xOffset, PointY, width + 2, Height),
				DisplayMode.AbsoluteLeftBottom => new Rectangle(xOffset, GlobalStatic.EMediator.Console.Window.MainPicBox.Height - PointY - Height, width + 2, Height),
				_ => new Rectangle(xOffset, GlobalStatic.EMediator.Console.Window.MainPicBox.Height - PointY - Height, width + 2, Height)
			}; // 何故か+2pxが必要，なぞ

		// Save the current canvas state before clipping
		graph.Save();

		if (margin != null)
			rect = new Rectangle(rect.X + margin[Direction.Left], rect.Y + margin[Direction.Top],
				 rect.Width - margin[Direction.Left] - margin[Direction.Right], rect.Height - margin[Direction.Top] - margin[Direction.Bottom]);
		//graph.SetClip(rect, CombineMode.Replace);
		graph.ClipRect(new SKRect(rect.Left, rect.Top, rect.Right, rect.Bottom));

		// 绘制背景色
		if (backgroundColor != Color.Transparent)
		{
			using var backPaint = new SKPaint { Color = backgroundColor.ToSKColor() };
			graph.DrawRect(new SKRect(rect.Left, rect.Top, rect.Right, rect.Bottom), backPaint);
		}

		// 绘制边框
		if (border != null && borderColors != null)
		{
			// 绘制上边框
			if (border[Direction.Top] > 0 && borderColors[Direction.Top] != Color.Transparent)
			{
				using var borderPaint = new SKPaint { Color = borderColors[Direction.Top].ToSKColor() };
				graph.DrawRect(new SKRect(rect.Left, rect.Top, rect.Right, rect.Top + border[Direction.Top]), borderPaint);
			}
			// 绘制右边框
			if (border[Direction.Right] > 0 && borderColors[Direction.Right] != Color.Transparent)
			{
				using var borderPaint = new SKPaint { Color = borderColors[Direction.Right].ToSKColor() };
				graph.DrawRect(new SKRect(rect.Right - border[Direction.Right], rect.Top, rect.Right, rect.Bottom), borderPaint);
			}
			// 绘制下边框
			if (border[Direction.Bottom] > 0 && borderColors[Direction.Bottom] != Color.Transparent)
			{
				using var borderPaint = new SKPaint { Color = borderColors[Direction.Bottom].ToSKColor() };
				graph.DrawRect(new SKRect(rect.Left, rect.Bottom - border[Direction.Bottom], rect.Right, rect.Bottom), borderPaint);
			}
			// 绘制左边框
			if (border[Direction.Left] > 0 && borderColors[Direction.Left] != Color.Transparent)
			{
				using var borderPaint = new SKPaint { Color = borderColors[Direction.Left].ToSKColor() };
				graph.DrawRect(new SKRect(rect.Left, rect.Top, rect.Left + border[Direction.Left], rect.Bottom), borderPaint);
			}
		}

		if (border != null)
			rect = new Rectangle(rect.X + border[Direction.Left], rect.Y + border[Direction.Top],
				 rect.Width - border[Direction.Left] - border[Direction.Right], rect.Height - border[Direction.Top] - border[Direction.Bottom]);

		if (padding != null)
			rect = new Rectangle(rect.X + padding[Direction.Left], rect.Y + padding[Direction.Top],
				 rect.Width - padding[Direction.Left] - padding[Direction.Right], rect.Height - padding[Direction.Top] - padding[Direction.Bottom]);

		//graph.SetClip(rect, CombineMode.Replace);
		graph.ClipRect(new SKRect(rect.Left, rect.Top, rect.Right, rect.Bottom));

		point.Y = rect.Y;
		foreach (var child in children)
		{
			child.DrawTo(graph, (int)point.Y, isBackLog, true, mode);
			point.Y += Config.Config.LineHeight;
		}
		//graph.ResetClip();
		graph.Restore();
	}

    private void ShiftChildrenX(int diff)
	{
		foreach (var child in children)
			child.ShiftPositionX(diff);
	}

	public override void SetWidth(StringMeasure sm, float subPixel)
	{
	}

	public override string ToString()
	{
		var sb = new StringBuilder();
		sb.Append(altHeadTag);
		foreach (var line in children)
		{
			line.BuildString(sb);
			sb.Append("\r\n");
		}
		sb.Append("</div>");
		return sb.ToString();
	}
	public override StringBuilder BuildString(StringBuilder sb)
	{
		sb.Append(altHeadTag);
		foreach (var line in children)
		{
			line.BuildString(sb);
			sb.Append("\r\n");
		}
		sb.Append("</div>");
		return sb;
	}
}
