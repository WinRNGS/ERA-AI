using MinorShift.Emuera.AI.Interact;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace MinorShift.Emuera.Forms;

/// <summary>
/// 从会话历史里挑一条消息。引用与编辑都要指定"哪一条"。
///
/// 列表里显示消息 id 而不只是序号：id 是跨轮稳定的标识，上下文压缩（P5）之后
/// 序号会变但 id 不会。玩家看得到 id，报问题时也能对得上日志。
/// </summary>
internal sealed class AiMessagePickerDialog : Form
{
    private readonly ListBox list;
    private readonly List<AiMessage> items;

    public AiMessage Selected =>
        list.SelectedIndex >= 0 && list.SelectedIndex < items.Count ? items[list.SelectedIndex] : null;

    /// <summary>过滤之后还剩几条可选。为 0 时调用方应直接提示而不是弹一个空列表。</summary>
    public int Count => items.Count;

    public AiMessagePickerDialog(string title, string hintText, IReadOnlyList<AiMessage> messages, bool assistantOnly)
    {
        items = [];
        foreach (AiMessage m in messages)
        {
            if (assistantOnly && !m.IsAssistant)
                continue;
            items.Add(m);
        }

        Text = title;
        Size = new Size(620, 420);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.SizableToolWindow;
        BackColor = Color.FromArgb(40, 40, 40);
        ForeColor = Color.FromArgb(220, 220, 220);
        Font = new Font("Microsoft YaHei UI", 9f);

        var hint = new Label
        {
            Dock = DockStyle.Top,
            Height = 34,
            Text = hintText,
            ForeColor = Color.FromArgb(170, 170, 170),
            Padding = new Padding(10, 6, 10, 0),
        };

        list = new ListBox
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(30, 30, 30),
            ForeColor = Color.FromArgb(225, 225, 225),
            BorderStyle = BorderStyle.None,
            IntegralHeight = false,
        };
        // 倒序显示：要引用或编辑的几乎总是最近几条，把它们放在最上面省一次滚动。
        for (int i = items.Count - 1; i >= 0; i--)
            list.Items.Add(Describe(items[i]));
        // 同时把 items 也倒过来，保证下标与显示一致。
        items.Reverse();
        if (list.Items.Count > 0)
            list.SelectedIndex = 0;
        list.DoubleClick += (s, e) =>
        {
            if (Selected != null)
            {
                DialogResult = DialogResult.OK;
                Close();
            }
        };

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 44,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(5),
        };
        var cancel = new Button { Text = "取消", Width = 84, FlatStyle = FlatStyle.Flat, ForeColor = Color.White };
        cancel.Click += (s, e) => { DialogResult = DialogResult.Cancel; Close(); };
        var ok = new Button
        {
            Text = "确定",
            Width = 84,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(60, 130, 80),
            ForeColor = Color.White,
        };
        ok.Click += (s, e) =>
        {
            if (Selected == null)
            {
                MessageBox.Show(this, "请先选一条消息。", title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);

        Controls.Add(list);
        Controls.Add(buttons);
        Controls.Add(hint);
        CancelButton = cancel;
    }

    private static string Describe(AiMessage m)
    {
        string who = m.IsAssistant ? "AI" : "你";
        string flags = "";
        if (m.Edited)
            flags += "（已编辑）";
        if (m.Interrupted)
            flags += "（被中断）";
        string flat = (m.Text ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
        if (flat.Length > 64)
            flat = flat[..64] + "\u2026";
        return $"#{m.Id} [{who}]{flags} {flat}";
    }
}