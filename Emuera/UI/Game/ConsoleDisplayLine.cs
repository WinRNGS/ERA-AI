using MinorShift.Emuera.Runtime;
using MinorShift.Emuera.Runtime.Config;
using System.Drawing;
using System.Text;
using SkiaSharp;

namespace MinorShift.Emuera.UI.Game;

internal enum DisplayLineLastState
{
	None = 0,
	Normal = 1,
	Selected = 2,
	BackLog = 3,
}

internal enum DisplayLineAlignment
{
	LEFT = 0,
	CENTER = 1,
	RIGHT = 2,
}
/// <summary>
/// 表示行。1つ以上のボタン（ConsoleButtonString）からなる
/// </summary>
internal sealed class ConsoleDisplayLine
{

	//public ConsoleDisplayLine(EmueraConsole parentWindow, ConsoleButtonString[] buttons, bool isLogical, bool temporary)
	public ConsoleDisplayLine(ConsoleButtonString[] buttons, bool isLogical, bool temporary, bool lineEnd = true)
	{
		//parent = parentWindow;
		if (buttons == null)
		{
			//これはthis.buttonの間違い？
			buttons = [];
			return;
		}
		this.buttons = buttons;
		foreach (ConsoleButtonString button in buttons)
			button.ParentLine = this;
		IsLogicalLine = isLogical;
		IsTemporary = temporary;
		IsLineEnd = lineEnd;
	}
	public int LineNo = -1;

	///論理行の最初となる場合だけtrue。表示の都合で改行された2行目以降はfalse
	readonly public bool IsLogicalLine = true;
	readonly public bool IsTemporary;
	public bool IsLineEnd = true;
	//EmueraConsole parent;
	ConsoleButtonString[] buttons;
	DisplayLineAlignment align;
	public ConsoleButtonString[] Buttons { get { return buttons; } }
	public DisplayLineAlignment Align { get { return align; } }
	bool aligned;
	//Bitmap Cache
	public bool bitmapCacheEnabled;
	public void SetAlignment(DisplayLineAlignment align, int customWidth = -1/*, int xOffset = 0*/)
	{
		if (aligned)
			return;
		aligned = true;
		this.align = align;
		if (buttons.Length == 0)
			return;
		//DisplayLineの幅
		int width = 0;
		foreach (ConsoleButtonString button in buttons)
			width += button.Width;
		//現在位置
		int pointX = buttons[0].PointX;

		//目標位置
		int movetoX = 0;

		if (align == DisplayLineAlignment.LEFT)
		{
			if (IsLogicalLine)
			{
				if (pointX != 0)
					return; // 位置固定に対応
			}
		}
		#region EM_私家版_HTML_divタグ
		else if (align == DisplayLineAlignment.CENTER)
			#region EE_div各要素の修正
			movetoX = (customWidth > 0 ? customWidth : Config.DrawableWidth) / 2 - width / 2;
		#endregion
		else if (align == DisplayLineAlignment.RIGHT)
			#region EE_div各要素の修正
			movetoX = (customWidth > 0 ? customWidth : Config.DrawableWidth) - width;
		#endregion
		#endregion
		//移動距離
		int shiftX = movetoX - pointX;
		if (shiftX != 0)
			ShiftPositionX(shiftX);
	}

	public void ShiftPositionX(int shiftX)
	{
		foreach (ConsoleButtonString button in buttons)
			button.ShiftPositionX(shiftX);
	}

	public void ChangeStr(ConsoleButtonString[] newButtons)
	{
		buttons = null;
		foreach (ConsoleButtonString button in newButtons)
			button.ParentLine = this;
		buttons = newButtons;
	}

	public static void Clear(Brush brush, Graphics graph, int pointY)
	{
		Rectangle rect = new(0, pointY, Config.WindowX, Config.LineHeight);
		graph.FillRectangle(brush, rect);
	}

	//public ConsoleButtonString GetPointingButton(int pointX)
	//{
	//	////1815 優先順位を逆順にする
	//	////後から描画されるボタンが優先されるように
	//	for (int i = 0; i < buttons.Length; i++)
	//	{
	//		ConsoleButtonString button = buttons[buttons.Length - i - 1];
	//		if ((button.PointX <= pointX) && (button.PointX + button.Width >= pointX))
	//			return button;
	//	}
	//	//foreach (ConsoleButtonString button in buttons)
	//	//{
	//	//    if ((button.PointX <= pointX) && (button.PointX + button.Width >= pointX))
	//	//        return button;
	//	//}
	//	return null;
	//}

	public void DrawTo(SKCanvas graph, int pointY, bool isBackLog, bool force, TextDrawingMode mode)
	{
		Color? bgColor = GlobalStatic.Console?.TextBackgroundColor;
		if (bgColor.HasValue)
		{
			bool hasText = false;
			foreach (ConsoleButtonString button in buttons)
			{
				if (button.StrArray == null) continue;
				foreach (AConsoleDisplayNode node in button.StrArray)
				{
					if (node is ConsoleStyledString css && !string.IsNullOrWhiteSpace(css.Text))
					{
						hasText = true;
						break;
					}
				}
				if (hasText) break;
			}
			if (hasText)
			{
				int lineWidth = GlobalStatic.Console?.ClientWidth ?? 1280;
				var c = bgColor.Value;
				using var backPaint = new SKPaint { Color = new SKColor(c.R, c.G, c.B, c.A) };
				graph.DrawRect(SKRect.Create(0, pointY, lineWidth, Config.LineHeight), backPaint);
			}
		}
		foreach (ConsoleButtonString button in buttons)
			button.DrawTo(graph, new SKPoint(-1, pointY), isBackLog, mode);
	}

	readonly static StringBuilder builder = new();
	public override string ToString()
	{
		if (buttons == null)
			return "";
		builder.Clear();
		foreach (var button in buttons)
			builder.Append(button.ToString());
		return builder.ToString();
	}
	#region EM_私家版_描画拡張
	public StringBuilder BuildString(StringBuilder builder)
	{
		if (buttons != null)
			foreach (ConsoleButtonString button in buttons)
				builder.Append(button.ToString());
		return builder;
	}
	#endregion
}
