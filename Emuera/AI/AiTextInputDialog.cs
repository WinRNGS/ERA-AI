using System;
using System.Drawing;
using System.Windows.Forms;

namespace MinorShift.Emuera.Forms;

/// <summary>
/// 一个多行文本输入对话框。P4 的「编辑回复」与「要求重写」都用它。
///
/// 单独抽出来而不是各写一个：两者的界面需求完全相同（一段说明 + 一个多行框 + 确定/取消），
/// 差别只在标题与初始文本。编辑回复要能看到并改动原文，所以必须是多行且可预填。
/// </summary>
internal sealed class AiTextInputDialog : Form
{
    private readonly TextBox editor;

    public string Value => editor.Text;

    public AiTextInputDialog(string title, string hintText, string initialText, int height = 320)
    {
        Text = title;
        Size = new Size(560, height);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.SizableToolWindow;
        MinimizeBox = false;
        MaximizeBox = false;
        BackColor = Color.FromArgb(40, 40, 40);
        ForeColor = Color.FromArgb(220, 220, 220);
        Font = new Font("Microsoft YaHei UI", 9f);

        var hint = new Label
        {
            Dock = DockStyle.Top,
            Height = 40,
            Text = hintText,
            ForeColor = Color.FromArgb(170, 170, 170),
            Padding = new Padding(10, 6, 10, 0),
        };

        editor = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            AcceptsReturn = true,
            BackColor = Color.FromArgb(50, 50, 50),
            ForeColor = Color.FromArgb(230, 230, 230),
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Microsoft YaHei UI", 10f),
            Text = initialText ?? "",
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
            if (string.IsNullOrWhiteSpace(editor.Text))
            {
                MessageBox.Show(this, "内容不能为空。", title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);

        Controls.Add(editor);
        Controls.Add(buttons);
        Controls.Add(hint);

        AcceptButton = null;   // 多行输入里回车要换行，不能当确定
        CancelButton = cancel;
    }
}