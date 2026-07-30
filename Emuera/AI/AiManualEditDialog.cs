using MinorShift.Emuera.AI;
using MinorShift.Emuera.AI.Compute;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace MinorShift.Emuera.Forms;

/// <summary>
/// 手动调整数值的对话框。
///
/// 为什么做成一个列表而不是自由输入变量名：可改范围必须和副 API 完全一致
/// （都限于 compute.writable_fields），否则「能改什么」就有了两份定义。
/// 列表也顺带解决了「玩家不知道变量叫什么」的问题。
///
/// 界面上刻意显示每个字段的建议区间与副 API 的单轮幅度上限，但**不强制**：
/// 那是给模型的约束，玩家看一眼知道设计意图就够了，改不改由他决定。
/// </summary>
internal sealed class AiManualEditDialog : Form
{
    private readonly List<AiEditableEntry> entries;
    private readonly List<NumericUpDown> editors = [];

    /// <summary>点了保存并且确实写入了变更时非空。</summary>
    public List<AiAppliedChange> Applied { get; private set; }

    public AiManualEditDialog(List<AiEditableEntry> entries)
    {
        this.entries = entries;

        Text = "手动调整数值";
        Size = new Size(520, Math.Min(640, 150 + entries.Count * 34));
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = Color.FromArgb(40, 40, 40);
        ForeColor = Color.FromArgb(220, 220, 220);
        Font = new Font("Microsoft YaHei UI", 9f);

        var hint = new Label
        {
            Dock = DockStyle.Top,
            Height = 46,
            ForeColor = Color.FromArgb(170, 170, 170),
            Padding = new Padding(10, 6, 10, 0),
            Text = "直接填写想要的最终值。这里不受副 API 的单轮幅度与区间限制——\r\n"
                 + "括号里的范围只是设计意图的参考，改成什么由你决定。",
        };

        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(6) };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 3,
            RowCount = entries.Count,
            AutoSize = true,
            BackColor = Color.FromArgb(40, 40, 40),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        for (int i = 0; i < entries.Count; i++)
        {
            AiEditableEntry entry = entries[i];
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));

            layout.Controls.Add(new Label
            {
                Text = entry.DisplayName,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.FromArgb(215, 215, 215),
            }, 0, i);

            // 上下界放到 long 的极值：允许作弊的意思就是不在这里设卡。
            // 真正的安全边界是 AiVariableAccess 的引擎级校验，写不进去的值那里会拦。
            var editor = new NumericUpDown
            {
                Dock = DockStyle.Fill,
                Minimum = decimal.MinValue,
                Maximum = decimal.MaxValue,
                Value = entry.Current,
                ThousandsSeparator = false,
                BackColor = Color.FromArgb(50, 50, 50),
                ForeColor = Color.FromArgb(230, 230, 230),
                BorderStyle = BorderStyle.FixedSingle,
            };
            editors.Add(editor);
            layout.Controls.Add(editor, 1, i);

            layout.Controls.Add(new Label
            {
                Text = Describe(entry),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.FromArgb(150, 150, 150),
            }, 2, i);
        }
        scroll.Controls.Add(layout);

        var btnPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 44,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(5),
        };
        var btnCancel = new Button { Text = "取消", Width = 80, FlatStyle = FlatStyle.Flat, ForeColor = Color.White };
        btnCancel.Click += (s, e) => Close();
        var btnSave = new Button
        {
            Text = "写入",
            Width = 80,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(60, 130, 80),
            ForeColor = Color.White,
        };
        btnSave.Click += BtnSave_Click;
        btnPanel.Controls.Add(btnCancel);
        btnPanel.Controls.Add(btnSave);

        Controls.Add(scroll);
        Controls.Add(btnPanel);
        Controls.Add(hint);
    }

    private static string Describe(AiEditableEntry entry)
    {
        AiComputeField field = entry.Field;
        string range = field.Min == long.MinValue && field.Max == long.MaxValue
            ? "无区间"
            : $"设计区间 [{Bound(field.Min)}, {Bound(field.Max)}]";
        string delta = field.MaxDelta > 0 ? $"，AI 单轮 ≤ {field.MaxDelta}" : "";
        return $"当前 {entry.Current}｜{range}{delta}";
    }

    private static string Bound(long value)
    {
        if (value == long.MinValue)
            return "-∞";
        if (value == long.MaxValue)
            return "+∞";
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private void BtnSave_Click(object sender, EventArgs e)
    {
        var edits = new List<AiManualEdit>();
        for (int i = 0; i < entries.Count; i++)
        {
            long wanted = (long)editors[i].Value;
            if (wanted == entries[i].Current)
                continue;
            edits.Add(new AiManualEdit
            {
                Field = entries[i].Field,
                CharaNo = entries[i].CharaNo,
                Value = wanted,
            });
        }

        if (edits.Count == 0)
        {
            Close();
            return;
        }

        if (!AiManualEditor.TryApply(edits, out List<AiAppliedChange> applied, out string error))
        {
            MessageBox.Show(this, $"写入失败，一项都没有改动：\n{error}", "手动调整",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        Applied = applied;
        DialogResult = DialogResult.OK;
        Close();
    }
}