using MinorShift.Emuera.UI.Game;
using System.Collections.Generic;

namespace MinorShift.Emuera.Runtime.Utils.EvilMask;

internal sealed class ConsoleEscapedParts
{
	private readonly struct EscapedPartRecord
	{
		public readonly int Line;
		public readonly int Depth;
		public readonly int Top;
		public readonly int Bottom;
		public readonly long Id;
		public readonly sbyte DivType;
		public readonly AConsoleDisplayNode Part;

		public EscapedPartRecord(int line, int depth, int top, int bottom, long id, sbyte divType, AConsoleDisplayNode part)
		{
			Line = line;
			Depth = depth;
			Top = top;
			Bottom = bottom;
			Id = id;
			DivType = divType;
			Part = part;
		}
	}
	
	// 修正 3: 指定初始容量 256，减少 List 早期扩容的内存开销
	static readonly List<EscapedPartRecord> records = new List<EscapedPartRecord>(256);
	
	static bool getOnce;
	static int lastTop, lastBottom, lastGeneration;
	public static bool Changed { get; private set; }

	public static bool TestedInRange(int top, int bottom, int gen)
	{
		return getOnce && top == lastTop && bottom == lastBottom && gen == lastGeneration;
	}

	public static void Clear()
	{
		getOnce = false;
		records.Clear();
		Changed = false;
	}

	public static void Add(AConsoleDisplayNode part, int line, int depth, int top, int bottom)
	{
		var id = Utils.TimePoint();
		sbyte divType = 0;
		
		if (part is ConsoleDivPart div)
			divType = (sbyte)((!div.IsRelative ? 2 : 0) | 1);

		records.Add(new EscapedPartRecord(line, depth, top, bottom, id, divType, part));
		
		Changed = true;
	}

	public static void Remove(int line)
	{
		int removed = records.RemoveAll(r => r.Line >= line);
		if (removed > 0) Changed = true;
	}

	public static void RemoveAt(int line)
	{
		int removed = records.RemoveAll(r => r.Line == line);
		if (removed > 0) Changed = true;
	}

	public static void GetPartsInRange(int top, int bottom, int gen, Dictionary<int, List<AConsoleDisplayNode>> rmap)
	{
		if (GlobalStatic.Console?.GetLineNo > Config.Config.MaxLog)
		{
			var correction = GlobalStatic.Console.GetLineNo - Config.Config.MaxLog;
			top += correction;
			bottom += correction;
		}
		if (rmap == null) return;
		rmap.Clear();

		// 修正 2: 抛弃 LINQ，使用原生 foreach 遍历，速度最快
		foreach (var r in records)
		{
			if ((r.DivType & 2) != 0 || (r.Top <= bottom + 1 && r.Bottom >= top && 
			(r.DivType != 0 || top > r.Line || r.Line > bottom + 1)))
			{
				if (!rmap.TryGetValue(r.Depth, out var list))
				{
					list = new List<AConsoleDisplayNode>();
					rmap[r.Depth] = list;
				}
				list.Add(r.Part);
			}
		}

		getOnce = true;
		lastTop = top; lastBottom = bottom; lastGeneration = gen;
		Changed = false;
	}
}
