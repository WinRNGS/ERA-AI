using MinorShift.Emuera.AI;
using System;
using System.Windows.Forms;

namespace MinorShift.Emuera.Forms;

/// <summary>
/// ERA-AI 的 P0 自检入口。
/// 单独放在 partial 文件里，让对 MainWindow.cs 本体的改动只有构造函数中的一行调用，
/// 便于后续上游合并时定位与回滚。
/// </summary>
internal sealed partial class MainWindow
{
	private ToolStripMenuItem aiRootMenuItem;
	private ToolStripMenuItem aiSelfTestMenuItem;
	private ToolStripMenuItem aiAbortMenuItem;
	private ToolStripMenuItem aiShowLogMenuItem;

	private void InstallAiMenu()
	{
		aiSelfTestMenuItem = new ToolStripMenuItem("发起测试请求(P0 假数据)");
		aiSelfTestMenuItem.Click += AiSelfTestMenuItem_Click;

		aiAbortMenuItem = new ToolStripMenuItem("终止当前请求") { Enabled = false };
		aiAbortMenuItem.Click += AiAbortMenuItem_Click;

		aiShowLogMenuItem = new ToolStripMenuItem("显示调度日志");
		aiShowLogMenuItem.Click += AiShowLogMenuItem_Click;

		aiRootMenuItem = new ToolStripMenuItem("AI");
		aiRootMenuItem.DropDownItems.Add(aiSelfTestMenuItem);
		aiRootMenuItem.DropDownItems.Add(aiAbortMenuItem);
		aiRootMenuItem.DropDownItems.Add(new ToolStripSeparator());
		aiRootMenuItem.DropDownItems.Add(aiShowLogMenuItem);

		menuStrip.Items.Insert(menuStrip.Items.Count - 1, aiRootMenuItem);

		AiDispatcher.TurnCompleted += AiDispatcher_TurnCompleted;
	}

	private void AiSelfTestMenuItem_Click(object sender, EventArgs e)
	{
		if (console == null)
			return;
		if (!AiDispatcher.TryBeginTurn(console, "P0 自检：验证异步回注与硬锁定"))
		{
			console.PrintSingleLine("[AI] 已有请求进行中，硬锁定拒绝了本次触发。");
			console.RefreshStrings(true);
			return;
		}
		UpdateAiMenuState();
		console.PrintSingleLine($"[AI] 请求已发出（约 {AiDispatcher.FakeDelayMs} 毫秒）。锁定期间界面应保持可拖动、可滚动，但不接受任何输入。");
		console.RefreshStrings(true);
	}

	private void AiAbortMenuItem_Click(object sender, EventArgs e)
	{
		if (!AiDispatcher.Abort())
			return;
		console?.PrintSingleLine("[AI] 已请求终止，等待收尾。");
		console?.RefreshStrings(true);
	}

	private void AiShowLogMenuItem_Click(object sender, EventArgs e)
	{
		if (console == null)
			return;
		console.PrintSingleLine("---- AI 调度日志 ----");
		foreach (string line in AiDispatcher.Log)
			console.PrintSingleLine(line);
		console.PrintSingleLine($"---- 当前状态：{AiRequestLock.State} ----");
		console.RefreshStrings(true);
	}

	private void AiDispatcher_TurnCompleted(AiTurnResult result)
	{
		UpdateAiMenuState();
		if (console == null)
			return;

		if (result.Aborted)
			console.PrintSingleLine($"[AI] 请求已终止（耗时 {result.ElapsedMs} 毫秒），未写入任何数值。");
		else if (!result.Success)
			console.PrintSingleLine($"[AI] 请求失败：{result.ErrorMessage}");
		else
		{
			console.PrintSingleLine(result.NarrativeText);
			foreach (var change in result.Changes)
			{
				if (AiVariableAccess.TryReadInt(change.Target, out long value, out _))
					console.PrintSingleLine($"[AI] 已回写 {change.Target} {change.Op} {change.Value}，当前值 {value}");
			}
			console.PrintSingleLine($"[AI] 完成，耗时 {result.ElapsedMs} 毫秒。");
		}
		console.RefreshStrings(true);
	}

	private void UpdateAiMenuState()
	{
		bool locked = AiRequestLock.IsLocked;
		if (aiSelfTestMenuItem != null)
			aiSelfTestMenuItem.Enabled = !locked;
		if (aiAbortMenuItem != null)
			aiAbortMenuItem.Enabled = locked;
	}
}