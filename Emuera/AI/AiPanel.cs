using MinorShift.Emuera.AI;
using MinorShift.Emuera.AI.Compute;
using MinorShift.Emuera.AI.Traits;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace MinorShift.Emuera.Forms;

/// <summary>
/// AI 对话面板。独立的文本渲染路径，不走 ERB 输出管线。
/// 包含：输出区（RichTextBox）、输入框、操作按钮（发送/终止/清空/设置）。
/// </summary>
internal sealed class AiPanel : UserControl
{
    private readonly RichTextBox outputBox;
    private readonly TextBox inputBox;
    private readonly Button sendButton;
    private readonly Button abortButton;
    private readonly Button clearButton;
    private readonly Button settingsButton;
    private readonly Button regenerateButton;
    private readonly Button rollbackButton;
    private readonly Button keepButton;
    private readonly Panel buttonPanel;
    private readonly Label statusLabel;

    private readonly GameView.EmueraConsole console;

    public AiPanel(GameView.EmueraConsole console)
    {
        this.console = console;
        Dock = DockStyle.Fill;
        BackColor = Color.FromArgb(30, 30, 30);

        outputBox = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BackColor = Color.FromArgb(25, 25, 25),
            ForeColor = Color.FromArgb(220, 220, 220),
            Font = new Font("Microsoft YaHei UI", 10f),
            BorderStyle = BorderStyle.None,
            ScrollBars = RichTextBoxScrollBars.Vertical,
        };

        statusLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 24,
            Text = "AI 对话面板",
            ForeColor = Color.FromArgb(160, 160, 160),
            BackColor = Color.FromArgb(40, 40, 40),
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(6, 0, 0, 0),
            Font = new Font("Microsoft YaHei UI", 9f),
        };

        var inputPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 128,
            Padding = new Padding(4),
            BackColor = Color.FromArgb(35, 35, 35),
        };

        inputBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            BackColor = Color.FromArgb(45, 45, 45),
            ForeColor = Color.FromArgb(230, 230, 230),
            Font = new Font("Microsoft YaHei UI", 10f),
            BorderStyle = BorderStyle.FixedSingle,
            ScrollBars = ScrollBars.Vertical,
        };
        inputBox.KeyDown += InputBox_KeyDown;

        // 用会换行的两列布局而不是单列 Dock=Top：待处置事务的按钮出现时一共 6 个，
        // 单列在这个高度里放不下，会静默被裁掉（按钮存在但看不见，最难查的一类界面问题）。
        // FlowLayoutPanel 还有个好处：Visible=false 的按钮不占位，收起时不留空洞。
        buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            Width = 178,
            Padding = new Padding(2),
            FlowDirection = FlowDirection.TopDown,
            WrapContents = true,
        };

        sendButton = MakeButton("发送", Color.FromArgb(60, 130, 80));
        sendButton.Click += SendButton_Click;

        abortButton = MakeButton("终止", Color.FromArgb(160, 60, 60));
        abortButton.Enabled = false;
        abortButton.Click += AbortButton_Click;

        clearButton = MakeButton("清空", Color.FromArgb(80, 80, 100));
        clearButton.Click += ClearButton_Click;

        settingsButton = MakeButton("设置", Color.FromArgb(80, 100, 80));
        settingsButton.Click += SettingsButton_Click;

        // 数值已写入但正文失败时才出现（RISK-05）。两个按钮对应两条出路：
        // 保留数值只重写正文，或撤回本轮数值。
        regenerateButton = MakeButton("重生成", Color.FromArgb(70, 110, 150));
        regenerateButton.Visible = false;
        regenerateButton.Click += RegenerateButton_Click;

        rollbackButton = MakeButton("回滚数值", Color.FromArgb(150, 110, 60));
        rollbackButton.Visible = false;
        rollbackButton.Click += RollbackButton_Click;

        // 第三条出路。有未处置事务时新请求会被挡住，所以必须给一个
        // 「数值我认了、正文不要了」的退出口，否则玩家会被自己的选择卡死在这一轮。
        keepButton = MakeButton("保留数值", Color.FromArgb(110, 110, 110));
        keepButton.Visible = false;
        keepButton.Click += KeepButton_Click;

        // FlowLayoutPanel 按添加顺序排布，所以这里的顺序就是从上到下、换列后继续的顺序。
        buttonPanel.Controls.Add(sendButton);
        buttonPanel.Controls.Add(abortButton);
        buttonPanel.Controls.Add(regenerateButton);
        buttonPanel.Controls.Add(rollbackButton);
        buttonPanel.Controls.Add(keepButton);
        buttonPanel.Controls.Add(clearButton);
        buttonPanel.Controls.Add(settingsButton);

        inputPanel.Controls.Add(inputBox);
        inputPanel.Controls.Add(buttonPanel);

        Controls.Add(outputBox);
        Controls.Add(inputPanel);
        Controls.Add(statusLabel);

        AiDispatcher.TurnCompleted += OnTurnCompleted;
    }

    private static Button MakeButton(string text, Color backColor)
    {
        return new Button
        {
            Text = text,
            Width = 84,
            Height = 28,
            FlatStyle = FlatStyle.Flat,
            BackColor = backColor,
            ForeColor = Color.White,
            Font = new Font("Microsoft YaHei UI", 9f),
            Margin = new Padding(1),
        };
    }

    private void InputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter && !e.Shift)
        {
            e.SuppressKeyPress = true;
            DoSend();
        }
    }

    private void SendButton_Click(object sender, EventArgs e) => DoSend();

    private void AbortButton_Click(object sender, EventArgs e)
    {
        AiDispatcher.Abort();
        UpdateButtonState();
    }

    private void ClearButton_Click(object sender, EventArgs e)
    {
        AiDispatcher.ClearHistory();
        outputBox.Clear();
        AppendOutput("[系统] 对话历史与副 API 短记忆已清空。\n", Color.Gray);
    }

    private void RegenerateButton_Click(object sender, EventArgs e)
    {
        if (!AiDispatcher.TryRegenerateNarrative(console))
        {
            AppendOutput("[提示] 无法重生成（没有待处置事务或已有请求进行中）。\n", Color.Yellow);
            return;
        }
        AppendOutput("[系统] 保留本轮数值，重新生成正文…\n", Color.Gray);
        statusLabel.Text = "重新生成正文...";
        UpdateButtonState();
    }

    private void RollbackButton_Click(object sender, EventArgs e)
    {
        if (!AiDispatcher.TryRollbackPending(out string error))
        {
            AppendOutput($"[错误] 回滚失败：{error}\n", Color.OrangeRed);
            return;
        }
        AppendOutput("[系统] 本轮数值已回滚到请求前的状态。\n", Color.Gray);
        statusLabel.Text = "已回滚本轮数值";
        UpdateButtonState();
    }

    private void KeepButton_Click(object sender, EventArgs e)
    {
        if (!AiDispatcher.TryDiscardPending(out string error))
        {
            AppendOutput($"[提示] {error}\n", Color.Yellow);
            return;
        }
        AppendOutput("[系统] 已保留本轮数值，放弃这一轮的正文。\n", Color.Gray);
        statusLabel.Text = "已保留本轮数值";
        UpdateButtonState();
    }

    private void SettingsButton_Click(object sender, EventArgs e)
    {
        using var dlg = new AiSettingsDialog();
        dlg.ShowDialog(FindForm());
    }

    private void DoSend()
    {
        string text = inputBox.Text?.Trim();
        if (string.IsNullOrEmpty(text))
            return;

        if (!AiConfig.IsReady(out string reason))
        {
            AppendOutput($"[错误] {reason}，请先在设置中配置。\n", Color.OrangeRed);
            return;
        }

        if (AiDispatcher.PendingTransaction != null)
        {
            AppendOutput("[提示] 上一轮数值已写入但正文失败，请先选择「重生成」「回滚数值」或「保留数值」。\n", Color.Yellow);
            return;
        }

        AiDispatcher.UseFakeBackend = false;

        if (!AiDispatcher.TryBeginTurn(console, text))
        {
            AppendOutput("[提示] 已有请求进行中，请等待完成。\n", Color.Yellow);
            return;
        }

        AppendOutput($"[你] {text}\n", Color.FromArgb(130, 200, 255));
        ReportPromptInfo();
        inputBox.Clear();
        statusLabel.Text = "AI 正在思考...";
        UpdateButtonState();
    }

    private void OnTurnCompleted(AiTurnResult result)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => OnTurnCompleted(result));
            return;
        }

        ReportComputeInfo(result);

        if (result.Aborted)
        {
            AppendOutput("[已终止]\n", Color.Gray);
            statusLabel.Text = "已终止";
        }
        else if (!result.Success)
        {
            AppendOutput($"[错误] {result.ErrorMessage}\n", Color.OrangeRed);
            if (result.NarrativeFailedAfterApply)
                AppendOutput("[注意] 本轮数值已写入存档但正文生成失败。请选择「重生成」只重写正文、「回滚数值」撤回本轮数值，或「保留数值」放弃正文继续。处置之前无法发起新请求，否则这批数值会失去可撤回性。\n",
                    Color.FromArgb(230, 170, 90));
            statusLabel.Text = "请求失败";
        }
        else
        {
            AppendOutput($"[AI] {result.NarrativeText}\n", Color.FromArgb(200, 230, 200));
            statusLabel.Text = $"完成（{result.ElapsedMs}ms）";
        }

        UpdateButtonState();
    }

    /// <summary>
    /// 把副 API 本轮的结果摆出来。数值是否被改动、为什么没改，玩家必须看得见，
    /// 否则"存档悄悄变了"和"存档其实没变"从界面上无法区分。
    /// </summary>
    private void ReportComputeInfo(AiTurnResult result)
    {
        if (result.ComputeRolledBack)
        {
            AppendOutput("[数值] 本轮数值已回滚，存档未变。\n", Color.FromArgb(160, 160, 160));
            return;
        }

        if (result.ComputeApplied != null && result.ComputeApplied.Count > 0)
        {
            AppendOutput($"[数值] 已写入 {result.ComputeApplied.Count} 项：{AiComputeApplier.Summarize(result.ComputeApplied)}\n",
                Color.FromArgb(150, 190, 220));
        }
        else if (!string.IsNullOrEmpty(result.ComputeSkipReason))
        {
            AppendOutput($"[数值] 本轮未改动（{result.ComputeSkipReason}）\n", Color.FromArgb(160, 160, 160));
        }

        if (result.ComputeWarnings != null)
        {
            foreach (string warning in result.ComputeWarnings)
                AppendOutput($"[数值·提醒] {warning}\n", Color.FromArgb(200, 180, 120));
        }
    }

    /// <summary>
    /// 把本轮实际使用的词条摆出来。调词条时必须看得见「这一轮到底命中了什么」，
    /// 否则改完 ai_traits.json 只能靠猜。走了兜底 prompt 时同样要说明原因。
    /// </summary>
    private void ReportPromptInfo()
    {
        AiPromptBuildInfo info = AiDispatcher.LastPromptInfo;
        if (info == null)
        {
            AppendOutput("[词条] 未启用词条 prompt，使用设置里的兜底文本。\n", Color.FromArgb(140, 140, 140));
            return;
        }
        if (!info.UsedTraits)
        {
            AppendOutput($"[词条] 未使用词条（{info.FallbackReason}），已退回兜底 prompt。\n", Color.FromArgb(200, 180, 120));
            return;
        }

        var names = new List<string>();
        foreach (AiTraitInstance t in info.Traits)
            names.Add($"{t.Name}({t.Score})");
        string truncated = info.Truncated ? "，已按字数上限截断" : "";
        AppendOutput($"[词条] 角色号 {info.CharaNo}｜命中 {string.Join("、", names)}｜prompt {info.Prompt?.Length ?? 0} 字{truncated}\n",
            Color.FromArgb(140, 140, 140));
    }

    private void AppendOutput(string text, Color color)
    {
        outputBox.SelectionStart = outputBox.TextLength;
        outputBox.SelectionLength = 0;
        outputBox.SelectionColor = color;
        outputBox.AppendText(text);
        outputBox.ScrollToCaret();
    }

    private void UpdateButtonState()
    {
        bool locked = AiRequestLock.IsLocked;
        sendButton.Enabled = !locked;
        abortButton.Enabled = locked;

        bool hasPending = AiDispatcher.PendingTransaction != null;
        regenerateButton.Visible = hasPending;
        rollbackButton.Visible = hasPending;
        keepButton.Visible = hasPending;
        regenerateButton.Enabled = hasPending && !locked;
        rollbackButton.Enabled = hasPending && !locked;
        keepButton.Enabled = hasPending && !locked;

        // 有未处置事务时发送会被调度器挡住，这里同步禁用，避免玩家点了才知道。
        sendButton.Enabled = !locked && !hasPending;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            AiDispatcher.TurnCompleted -= OnTurnCompleted;
        base.Dispose(disposing);
    }
}
