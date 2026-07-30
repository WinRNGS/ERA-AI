using MinorShift.Emuera.AI;
using MinorShift.Emuera.AI.Compute;
using MinorShift.Emuera.AI.Interact;
using MinorShift.Emuera.AI.Traits;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace MinorShift.Emuera.Forms;

/// <summary>
/// AI 对话面板。独立的文本渲染路径，不走 ERB 输出管线。
///
/// 自下而上的布局：输入区 → 引用栏 → 待执行动作栏 → 终止处置栏 → 选项栏 → 输出区。
/// 四个中间栏都是按需出现（Visible = false 的 docked 控件不占位），所以平时的版面
/// 与 P3 时期一样干净，只在真的有引用/动作/选项时才多出一条。
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
    private readonly Button quoteButton;
    private readonly Button editButton;
    private readonly Button reviseButton;
    private readonly FlowLayoutPanel buttonPanel;
    private readonly Label statusLabel;

    /// <summary>引用栏。玩家攒好的引用会在下一次发送时拼到输入开头。</summary>
    private readonly FlowLayoutPanel quotePanel;

    /// <summary>选项栏。副 API 建议的下一步，点了只是把文本填进输入框。</summary>
    private readonly FlowLayoutPanel optionPanel;

    /// <summary>待执行动作栏。默认不自动执行，等玩家点「执行动作」。</summary>
    private readonly Panel actionPanel;
    private readonly Label actionLabel;

    /// <summary>终止处置栏：丢弃 / 保留部分 / 重试（设计文档 S3.4.2）。</summary>
    private readonly Panel abortPanel;
    private readonly Label abortLabel;

    private readonly ToolTip toolTip = new();

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
            Height = 190,
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

        // 用会换行的两列布局而不是单列 Dock=Top：P4 把按钮加到了 11 个，
        // 单列在这个高度里放不下，会静默被裁掉（按钮存在但看不见，最难查的一类界面问题）。
        // FlowLayoutPanel 还有个好处：Visible=false 的按钮不占位，收起时不留空洞。
        // 178 宽 = 两列 × 84 + 边距；190 高 = 六行 × 30，共 12 个位置，够放全部 11 个按钮。
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

        // 引用与编辑：纯本地操作，不发网络请求，所以锁定期间也允许
        // （设计文档 S3.4.1：编辑历史 / 增删引用 → IDLE）。
        quoteButton = MakeButton("引用...", Color.FromArgb(70, 100, 130));
        quoteButton.Click += QuoteButton_Click;
        toolTip.SetToolTip(quoteButton, $"引用一条历史消息，最多 {AiQuoteBox.MaxQuotes} 条。引用会拼在下次发送的开头。");

        editButton = MakeButton("编辑回复", Color.FromArgb(90, 90, 130));
        editButton.Click += EditButton_Click;
        toolTip.SetToolTip(editButton, "直接改写某条 AI 回复的文字。只动上下文，不会回滚已写入的数值。");

        reviseButton = MakeButton("要求重写", Color.FromArgb(100, 90, 140));
        reviseButton.Click += ReviseButton_Click;
        toolTip.SetToolTip(reviseButton, "带修改要求重新生成最后一条回复。已结算的数值不变。");

        clearButton = MakeButton("清空", Color.FromArgb(80, 80, 100));
        clearButton.Click += ClearButton_Click;

        settingsButton = MakeButton("设置", Color.FromArgb(80, 100, 80));
        settingsButton.Click += SettingsButton_Click;

        // 数值已写入但正文失败时才出现（RISK-05）。三个按钮对应三条出路：
        // 保留数值只重写正文、撤回本轮数值、或认下数值放弃正文。
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
        buttonPanel.Controls.Add(quoteButton);
        buttonPanel.Controls.Add(editButton);
        buttonPanel.Controls.Add(reviseButton);
        buttonPanel.Controls.Add(clearButton);
        buttonPanel.Controls.Add(settingsButton);

        inputPanel.Controls.Add(inputBox);
        inputPanel.Controls.Add(buttonPanel);

        quotePanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 34,
            Padding = new Padding(4, 3, 4, 3),
            BackColor = Color.FromArgb(38, 44, 52),
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoScroll = true,
            Visible = false,
        };

        optionPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 34,
            Padding = new Padding(4, 3, 4, 3),
            BackColor = Color.FromArgb(36, 46, 40),
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoScroll = true,
            Visible = false,
        };

        actionPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 34,
            BackColor = Color.FromArgb(52, 44, 34),
            Visible = false,
        };
        actionLabel = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(235, 205, 150),
            Padding = new Padding(6, 0, 0, 0),
        };
        var actionRun = MakeButton("执行动作", Color.FromArgb(150, 110, 60));
        actionRun.Dock = DockStyle.Right;
        actionRun.Click += ActionRunButton_Click;
        var actionDrop = MakeButton("放弃动作", Color.FromArgb(90, 90, 90));
        actionDrop.Dock = DockStyle.Right;
        actionDrop.Click += ActionDropButton_Click;
        actionPanel.Controls.Add(actionLabel);
        actionPanel.Controls.Add(actionDrop);
        actionPanel.Controls.Add(actionRun);

        abortPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 34,
            BackColor = Color.FromArgb(48, 40, 40),
            Visible = false,
        };
        abortLabel = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(210, 180, 180),
            Padding = new Padding(6, 0, 0, 0),
            Text = "这一轮已终止。默认不写入历史，你也可以保留已收到的内容或原样重试。",
        };
        var abortRetry = MakeButton("重试", Color.FromArgb(70, 110, 150));
        abortRetry.Dock = DockStyle.Right;
        abortRetry.Click += AbortRetryButton_Click;
        var abortKeep = MakeButton("保留部分", Color.FromArgb(110, 110, 110));
        abortKeep.Dock = DockStyle.Right;
        abortKeep.Click += AbortKeepButton_Click;
        var abortDrop = MakeButton("丢弃", Color.FromArgb(90, 90, 90));
        abortDrop.Dock = DockStyle.Right;
        abortDrop.Click += AbortDropButton_Click;
        abortPanel.Controls.Add(abortLabel);
        abortPanel.Controls.Add(abortDrop);
        abortPanel.Controls.Add(abortKeep);
        abortPanel.Controls.Add(abortRetry);

        // 停靠顺序 = 添加顺序的倒序：最后加的贴边最外。所以 inputPanel 必须在
        // 四个中间栏之后加，才会落在最底部；outputBox 第一个加才能吃掉剩余空间。
        Controls.Add(outputBox);
        Controls.Add(optionPanel);
        Controls.Add(abortPanel);
        Controls.Add(actionPanel);
        Controls.Add(quotePanel);
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
        RefreshQuoteBar();
        ClearOptions();
        AppendOutput("[系统] 对话历史、副 API 短记忆与引用栏已清空。\n", Color.Gray);
        UpdateButtonState();
    }

    // ---------- 引用 ----------

    private void QuoteButton_Click(object sender, EventArgs e)
    {
        IReadOnlyList<AiMessage> messages = AiDispatcher.Messages;
        if (messages.Count == 0)
        {
            AppendOutput("[提示] 还没有可引用的消息。\n", Color.Yellow);
            return;
        }
        using var dlg = new AiMessagePickerDialog("引用历史消息",
            $"选一条要引用的消息（最多 {AiQuoteBox.MaxQuotes} 条，会拼在下次发送的开头）。", messages, false);
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK || dlg.Selected == null)
            return;
        if (!AiQuoteBox.TryAdd(dlg.Selected, out string error))
        {
            AppendOutput($"[提示] {error}\n", Color.Yellow);
            return;
        }
        RefreshQuoteBar();
    }

    /// <summary>
    /// 重画引用栏。每条引用做成一个按钮，点一下即移除——
    /// 引用是个临时暂存区，移除必须比添加更容易。
    /// </summary>
    private void RefreshQuoteBar()
    {
        foreach (Control c in quotePanel.Controls)
            c.Dispose();
        quotePanel.Controls.Clear();

        IReadOnlyList<AiQuote> quotes = AiQuoteBox.Quotes;
        if (quotes.Count == 0)
        {
            quotePanel.Visible = false;
            return;
        }

        quotePanel.Controls.Add(new Label
        {
            Text = $"引用({quotes.Count}/{AiQuoteBox.MaxQuotes})：",
            AutoSize = true,
            ForeColor = Color.FromArgb(160, 190, 220),
            Margin = new Padding(2, 6, 2, 0),
        });

        for (int i = 0; i < quotes.Count; i++)
        {
            int index = i;
            var chip = new Button
            {
                Text = $"\u2715 {quotes[i].Label}",
                AutoSize = true,
                Height = 24,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(56, 68, 82),
                ForeColor = Color.FromArgb(225, 235, 245),
                Font = new Font("Microsoft YaHei UI", 8.5f),
                Margin = new Padding(2, 2, 2, 2),
            };
            toolTip.SetToolTip(chip, "点击移除这条引用\n" + Brief(quotes[index].Snapshot, 200));
            chip.Click += (s, e) =>
            {
                AiQuoteBox.TryRemoveAt(index);
                RefreshQuoteBar();
            };
            quotePanel.Controls.Add(chip);
        }
        quotePanel.Visible = true;
    }

    // ---------- 选项 ----------

    private void ClearOptions()
    {
        foreach (Control c in optionPanel.Controls)
            c.Dispose();
        optionPanel.Controls.Clear();
        optionPanel.Visible = false;
    }

    /// <summary>
    /// 摆出本轮的选项。点了只是把文本填进输入框而不是直接发送——
    /// 选项是 AI 的建议，玩家应该还有机会在它后面补一句自己的话。
    /// </summary>
    private void ShowOptions(List<AiOption> options)
    {
        ClearOptions();
        if (options == null || options.Count == 0)
            return;

        optionPanel.Controls.Add(new Label
        {
            Text = "下一步：",
            AutoSize = true,
            ForeColor = Color.FromArgb(160, 210, 170),
            Margin = new Padding(2, 6, 2, 0),
        });

        foreach (AiOption option in options)
        {
            string label = option.Label;
            var button = new Button
            {
                Text = label,
                AutoSize = true,
                Height = 24,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(56, 82, 62),
                ForeColor = Color.FromArgb(230, 245, 232),
                Font = new Font("Microsoft YaHei UI", 8.5f),
                Margin = new Padding(2, 2, 2, 2),
            };
            if (!string.IsNullOrWhiteSpace(option.Hint))
                toolTip.SetToolTip(button, option.Hint);
            button.Click += (s, e) =>
            {
                inputBox.Text = label;
                inputBox.SelectionStart = inputBox.TextLength;
                inputBox.Focus();
            };
            optionPanel.Controls.Add(button);
        }
        optionPanel.Visible = true;
    }

    // ---------- 待执行动作 ----------

    private void ActionRunButton_Click(object sender, EventArgs e)
    {
        if (!AiDispatcher.TryExecutePendingAction(console, out string error))
        {
            AppendOutput($"[动作] 执行失败：{error}\n", Color.OrangeRed);
            UpdateButtonState();
            return;
        }
        AppendOutput("[动作] 已执行，流程已推进。\n", Color.FromArgb(230, 200, 140));
        UpdateButtonState();
    }

    private void ActionDropButton_Click(object sender, EventArgs e)
    {
        if (!AiDispatcher.TryDiscardPendingAction(out string error))
        {
            AppendOutput($"[提示] {error}\n", Color.Yellow);
            return;
        }
        AppendOutput("[动作] 已放弃本轮动作，流程未推进。\n", Color.Gray);
        UpdateButtonState();
    }

    // ---------- 终止处置 ----------

    private void AbortDropButton_Click(object sender, EventArgs e)
    {
        if (!AiDispatcher.TryDiscardAbortedTurn(out string error))
        {
            AppendOutput($"[提示] {error}\n", Color.Yellow);
            return;
        }
        AppendOutput("[系统] 已丢弃被终止的这一轮，会话历史未变。\n", Color.Gray);
        UpdateButtonState();
    }

    private void AbortKeepButton_Click(object sender, EventArgs e)
    {
        if (!AiDispatcher.TryKeepAbortedPartial(out string error))
        {
            AppendOutput($"[提示] {error}\n", Color.Yellow);
            return;
        }
        AppendOutput("[系统] 已把被终止时收到的正文写进历史，并标注为「被中断」。\n", Color.Gray);
        UpdateButtonState();
    }

    private void AbortRetryButton_Click(object sender, EventArgs e)
    {
        if (!AiDispatcher.TryRetryAbortedTurn(console, out string error))
        {
            AppendOutput($"[提示] {error}\n", Color.Yellow);
            return;
        }
        AppendOutput("[系统] 正在用完全相同的输入重试这一轮…\n", Color.Gray);
        statusLabel.Text = "AI 正在思考...";
        UpdateButtonState();
    }

    // ---------- 修改回复 ----------

    private void EditButton_Click(object sender, EventArgs e)
    {
        IReadOnlyList<AiMessage> messages = AiDispatcher.Messages;
        using var picker = new AiMessagePickerDialog("编辑 AI 回复",
            "选一条要改写的 AI 回复。只影响上下文文本，已写入的数值不会跟着变。", messages, true);
        if (picker.Count == 0)
        {
            AppendOutput("[提示] 还没有可编辑的 AI 回复。\n", Color.Yellow);
            return;
        }
        if (picker.ShowDialog(FindForm()) != DialogResult.OK || picker.Selected == null)
            return;

        AiMessage target = picker.Selected;
        using var dlg = new AiTextInputDialog("编辑 AI 回复",
            "改完点确定。这只改上下文里的文字，不会回滚数值——要撤数值请用「撤销上轮数值结算」。",
            target.Text, 400);
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK)
            return;
        if (!AiDispatcher.TryEditResponse(target.Id, dlg.Value, out string error))
        {
            AppendOutput($"[提示] {error}\n", Color.Yellow);
            return;
        }
        AppendOutput($"[系统] 已改写 #{target.Id} 的回复（{dlg.Value.Length} 字）。数值未受影响。\n", Color.Gray);
        AppendOutput($"[AI·改写后] {dlg.Value}\n", Color.FromArgb(190, 215, 190));
    }

    private void ReviseButton_Click(object sender, EventArgs e)
    {
        using var dlg = new AiTextInputDialog("要求重写上一条回复",
            "写下你希望怎么改。AI 只会重写那一条回复，已结算的数值不变。", "", 260);
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK)
            return;
        if (!AiDispatcher.TryReviseLastResponse(console, dlg.Value, out string error))
        {
            AppendOutput($"[提示] {error}\n", Color.Yellow);
            return;
        }
        AppendOutput($"[你·修改要求] {dlg.Value}\n", Color.FromArgb(150, 180, 230));
        statusLabel.Text = "正在按要求重写...";
        UpdateButtonState();
    }

    // ---------- 待处置事务的三条出路 ----------

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

        int quoteCount = AiQuoteBox.Count;
        if (!AiDispatcher.TryBeginTurn(console, text))
        {
            AppendOutput("[提示] 已有请求进行中，请等待完成。\n", Color.Yellow);
            return;
        }

        if (quoteCount > 0)
            AppendOutput($"[引用] 本轮带上 {quoteCount} 条引用。\n", Color.FromArgb(150, 185, 215));
        AppendOutput($"[你] {text}\n", Color.FromArgb(130, 200, 255));
        ReportPromptInfo();
        inputBox.Clear();
        // 引用已在调度器里定型并清空，界面同步收起引用栏。
        RefreshQuoteBar();
        ClearOptions();
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

        // 修改回复走的是另一条语义：不新增一轮，也不涉及数值与交互。
        if (result.IsRevision)
        {
            if (result.Aborted)
            {
                AppendOutput("[已终止] 原回复保持不变。\n", Color.Gray);
                statusLabel.Text = "已终止";
            }
            else if (!result.Success)
            {
                AppendOutput($"[错误] 重写失败：{result.ErrorMessage}（原回复保持不变）\n", Color.OrangeRed);
                statusLabel.Text = "重写失败";
            }
            else
            {
                AppendOutput($"[AI·重写] {result.NarrativeText}\n", Color.FromArgb(200, 230, 200));
                statusLabel.Text = $"已重写（{result.ElapsedMs}ms）";
            }
            UpdateButtonState();
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

        ReportInteractInfo(result);
        ShowOptions(result.Options);
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
    /// 交互产物的可见化。被丢弃的原因必须说出来——
    /// 否则"模型没提建议"和"提了但被我们丢了"在界面上一模一样，而这两件事的排查方向完全不同。
    /// </summary>
    private void ReportInteractInfo(AiTurnResult result)
    {
        if (!string.IsNullOrEmpty(result.OptionNote))
            AppendOutput($"[选项] {result.OptionNote}\n", Color.FromArgb(160, 160, 160));

        if (result.ActionAutoExecuted)
        {
            AppendOutput($"[动作] 已自动执行：{result.PendingAction?.Description}（interact.auto_execute = true）\n",
                Color.FromArgb(230, 200, 140));
        }
        else if (result.PendingAction != null)
        {
            string reason = string.IsNullOrWhiteSpace(result.PendingAction.Reason)
                ? "" : $"（理由：{result.PendingAction.Reason}）";
            AppendOutput($"[动作] AI 建议：{result.PendingAction.Description}{reason}。点「执行动作」才会推进流程。\n",
                Color.FromArgb(230, 200, 140));
        }

        if (!string.IsNullOrEmpty(result.ActionSkipReason))
            AppendOutput($"[动作] 未采用：{result.ActionSkipReason}\n", Color.FromArgb(180, 165, 130));
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

    private static string Brief(string text, int max)
    {
        if (string.IsNullOrEmpty(text))
            return "";
        return text.Length <= max ? text : text[..max] + "\u2026";
    }

    private void UpdateButtonState()
    {
        bool locked = AiRequestLock.IsLocked;
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

        // 引用是纯本地操作，锁定期间照样可用（S3.4.1）。编辑同理。
        quoteButton.Enabled = AiDispatcher.Messages.Count > 0;
        editButton.Enabled = AiDispatcher.Messages.Count > 0;
        // 重写要发请求，所以受锁与事务约束。
        reviseButton.Enabled = !locked && !hasPending && AiDispatcher.Messages.Count > 0;

        AiPendingAction action = AiDispatcher.PendingAction;
        if (action != null && !action.Consumed)
        {
            actionLabel.Text = $"AI 建议：{action.Description}";
            actionPanel.Visible = true;
            foreach (Control c in actionPanel.Controls)
            {
                if (c is Button b)
                    b.Enabled = !locked;
            }
        }
        else
        {
            actionPanel.Visible = false;
        }

        AiAbortedTurn aborted = AiDispatcher.LastAbortedTurn;
        if (aborted != null && !aborted.Handled && !locked)
        {
            abortLabel.Text = string.IsNullOrWhiteSpace(aborted.PartialText)
                ? "这一轮已终止，数值已回滚。可以原样重试，或直接丢弃。"
                : $"这一轮已终止（收到 {aborted.PartialText.Length} 字，数值已回滚）。可保留这段内容、原样重试，或丢弃。";
            abortPanel.Visible = true;
        }
        else
        {
            abortPanel.Visible = false;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            AiDispatcher.TurnCompleted -= OnTurnCompleted;
            toolTip.Dispose();
        }
        base.Dispose(disposing);
    }
}