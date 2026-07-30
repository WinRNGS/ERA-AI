using MinorShift.Emuera.AI;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace MinorShift.Emuera.Forms;

/// <summary>
/// AI 设置对话框：主 API（叙事）与副 API（计算）各一页。
/// 密钥仅在此处输入，加密存储，不在其他任何日志与界面中暴露。
///
/// P3 起分页：两条通道的配置项加起来太多，堆在一页里找不到东西。
/// </summary>
internal sealed class AiSettingsDialog : Form
{
    private readonly TextBox txtEndpoint;
    private readonly TextBox txtModel;
    private readonly TextBox txtApiKey;
    private readonly NumericUpDown numMaxTokens;
    private readonly NumericUpDown numTimeout;
    private readonly NumericUpDown numRetries;
    private readonly NumericUpDown numTemperature;
    private readonly TextBox txtSystemPrompt;
    private readonly CheckBox chkUseTraitPrompt;

    private readonly CheckBox chkUseComputeApi;
    private readonly TextBox txtComputeEndpoint;
    private readonly TextBox txtComputeModel;
    private readonly CheckBox chkComputeReuseKey;
    private readonly TextBox txtComputeApiKey;
    private readonly NumericUpDown numComputeMaxTokens;
    private readonly NumericUpDown numComputeTimeout;
    private readonly NumericUpDown numComputeRetries;

    public AiSettingsDialog()
    {
        Text = "AI 设置";
        Size = new Size(560, 620);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = Color.FromArgb(40, 40, 40);
        ForeColor = Color.FromArgb(220, 220, 220);
        Font = new Font("Microsoft YaHei UI", 9f);

        var tabs = new TabControl { Dock = DockStyle.Fill };

        // ---------- 主 API ----------
        var mainPage = new TabPage("主 API（叙事）") { BackColor = Color.FromArgb(40, 40, 40) };
        var mainLayout = MakeLayout(10);
        int row = 0;

        mainLayout.Controls.Add(MakeLabel("API 端点:"), 0, row);
        txtEndpoint = MakeTextBox(AiConfig.ApiEndpoint);
        mainLayout.Controls.Add(txtEndpoint, 1, row++);

        mainLayout.Controls.Add(MakeLabel("模型:"), 0, row);
        txtModel = MakeTextBox(AiConfig.Model);
        mainLayout.Controls.Add(txtModel, 1, row++);

        mainLayout.Controls.Add(MakeLabel("API Key:"), 0, row);
        txtApiKey = MakeTextBox("");
        txtApiKey.UseSystemPasswordChar = true;
        if (AiConfig.HasApiKey)
            txtApiKey.PlaceholderText = "(已设置，留空不修改)";
        mainLayout.Controls.Add(txtApiKey, 1, row++);

        mainLayout.Controls.Add(MakeLabel("Max Tokens:"), 0, row);
        numMaxTokens = MakeNumeric(1, 8192, AiConfig.MaxTokens);
        mainLayout.Controls.Add(numMaxTokens, 1, row++);

        mainLayout.Controls.Add(MakeLabel("超时(秒):"), 0, row);
        numTimeout = MakeNumeric(5, 120, AiConfig.TimeoutSeconds);
        mainLayout.Controls.Add(numTimeout, 1, row++);

        mainLayout.Controls.Add(MakeLabel("重试次数:"), 0, row);
        numRetries = MakeNumeric(0, 5, AiConfig.MaxRetries);
        mainLayout.Controls.Add(numRetries, 1, row++);

        mainLayout.Controls.Add(MakeLabel("Temperature:"), 0, row);
        numTemperature = MakeNumeric(0, 200, (int)(AiConfig.Temperature * 100));
        mainLayout.Controls.Add(numTemperature, 1, row++);

        mainLayout.Controls.Add(MakeLabel("词条 Prompt:"), 0, row);
        chkUseTraitPrompt = MakeCheckBox(
            "启用词条系统动态生成 system prompt（关闭则用下方静态文本）", AiConfig.UseTraitPrompt);
        mainLayout.Controls.Add(chkUseTraitPrompt, 1, row++);

        mainLayout.Controls.Add(MakeLabel("兜底 Prompt:"), 0, row);
        txtSystemPrompt = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            Text = AiConfig.SystemPrompt,
            BackColor = Color.FromArgb(50, 50, 50),
            ForeColor = Color.FromArgb(220, 220, 220),
            BorderStyle = BorderStyle.FixedSingle,
            ScrollBars = ScrollBars.Vertical,
        };
        mainLayout.Controls.Add(txtSystemPrompt, 1, row++);
        mainLayout.RowStyles[row - 1] = new RowStyle(SizeType.Percent, 100);

        mainPage.Controls.Add(mainLayout);

        // ---------- 副 API ----------
        var computePage = new TabPage("副 API（计算）") { BackColor = Color.FromArgb(40, 40, 40) };
        var computeLayout = MakeLayout(9);
        row = 0;

        computeLayout.Controls.Add(MakeLabel("启用:"), 0, row);
        chkUseComputeApi = MakeCheckBox(
            "启用副 API 负责数值结算（关闭则只跑叙事，数值不动）", AiConfig.UseComputeApi);
        computeLayout.Controls.Add(chkUseComputeApi, 1, row++);

        computeLayout.Controls.Add(MakeLabel("API 端点:"), 0, row);
        txtComputeEndpoint = MakeTextBox(AiConfig.ComputeApiEndpoint);
        computeLayout.Controls.Add(txtComputeEndpoint, 1, row++);

        computeLayout.Controls.Add(MakeLabel("模型:"), 0, row);
        txtComputeModel = MakeTextBox(AiConfig.ComputeModel);
        computeLayout.Controls.Add(txtComputeModel, 1, row++);

        computeLayout.Controls.Add(MakeLabel("密钥:"), 0, row);
        chkComputeReuseKey = MakeCheckBox("复用主 API 的密钥", AiConfig.ComputeReusesMainKey);
        chkComputeReuseKey.CheckedChanged += (s, e) => txtComputeApiKey.Enabled = !chkComputeReuseKey.Checked;
        computeLayout.Controls.Add(chkComputeReuseKey, 1, row++);

        computeLayout.Controls.Add(MakeLabel("副 API Key:"), 0, row);
        txtComputeApiKey = MakeTextBox("");
        txtComputeApiKey.UseSystemPasswordChar = true;
        txtComputeApiKey.Enabled = !AiConfig.ComputeReusesMainKey;
        if (AiConfig.HasComputeApiKey && !AiConfig.ComputeReusesMainKey)
            txtComputeApiKey.PlaceholderText = "(已设置，留空不修改)";
        computeLayout.Controls.Add(txtComputeApiKey, 1, row++);

        computeLayout.Controls.Add(MakeLabel("Max Tokens:"), 0, row);
        numComputeMaxTokens = MakeNumeric(64, 4096, AiConfig.ComputeMaxTokens);
        computeLayout.Controls.Add(numComputeMaxTokens, 1, row++);

        computeLayout.Controls.Add(MakeLabel("超时(秒):"), 0, row);
        numComputeTimeout = MakeNumeric(5, 120, AiConfig.ComputeTimeoutSeconds);
        computeLayout.Controls.Add(numComputeTimeout, 1, row++);

        computeLayout.Controls.Add(MakeLabel("重试次数:"), 0, row);
        numComputeRetries = MakeNumeric(0, 5, AiConfig.ComputeMaxRetries);
        computeLayout.Controls.Add(numComputeRetries, 1, row++);

        var hint = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(170, 170, 170),
            Text = "副 API 的温度固定为 0（计算通道要确定性，不开放配置）。\r\n"
                 + "「能改哪些数值、幅度上限多少」在 ai_traits.json 的 compute 段里配置，\r\n"
                 + "改完点「AI → 重载词条库」生效，再用「AI → 预览副 API 请求」核对实际下发内容。",
        };
        computeLayout.Controls.Add(hint, 1, row);
        computeLayout.RowStyles[row] = new RowStyle(SizeType.Percent, 100);

        computePage.Controls.Add(computeLayout);

        tabs.TabPages.Add(mainPage);
        tabs.TabPages.Add(computePage);

        var btnPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 44,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(5),
        };

        var btnCancel = new Button { Text = "取消", Width = 80, FlatStyle = FlatStyle.Flat, ForeColor = Color.White };
        btnCancel.Click += (s, e) => Close();

        var btnSave = new Button { Text = "保存", Width = 80, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(60, 130, 80), ForeColor = Color.White };
        btnSave.Click += BtnSave_Click;

        btnPanel.Controls.Add(btnCancel);
        btnPanel.Controls.Add(btnSave);

        Controls.Add(tabs);
        Controls.Add(btnPanel);
    }

    private void BtnSave_Click(object sender, EventArgs e)
    {
        AiConfig.ApiEndpoint = txtEndpoint.Text.Trim();
        AiConfig.Model = txtModel.Text.Trim();
        AiConfig.MaxTokens = (int)numMaxTokens.Value;
        AiConfig.TimeoutSeconds = (int)numTimeout.Value;
        AiConfig.MaxRetries = (int)numRetries.Value;
        AiConfig.Temperature = (double)numTemperature.Value / 100.0;
        AiConfig.SystemPrompt = txtSystemPrompt.Text;
        AiConfig.UseTraitPrompt = chkUseTraitPrompt.Checked;

        string key = txtApiKey.Text.Trim();
        if (!string.IsNullOrEmpty(key))
            AiConfig.SetApiKey(key);

        AiConfig.UseComputeApi = chkUseComputeApi.Checked;
        AiConfig.ComputeApiEndpoint = txtComputeEndpoint.Text.Trim();
        AiConfig.ComputeModel = txtComputeModel.Text.Trim();
        AiConfig.ComputeMaxTokens = (int)numComputeMaxTokens.Value;
        AiConfig.ComputeTimeoutSeconds = (int)numComputeTimeout.Value;
        AiConfig.ComputeMaxRetries = (int)numComputeRetries.Value;
        AiConfig.ComputeReusesMainKey = chkComputeReuseKey.Checked;

        string computeKey = txtComputeApiKey.Text.Trim();
        if (!string.IsNullOrEmpty(computeKey))
            AiConfig.SetComputeApiKey(computeKey);

        AiConfig.Save();
        Close();
    }

    private static TableLayoutPanel MakeLayout(int rowCount)
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = rowCount,
            Padding = new Padding(10),
            BackColor = Color.FromArgb(40, 40, 40),
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < rowCount; i++)
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        return layout;
    }

    private static CheckBox MakeCheckBox(string text, bool checkedState)
    {
        return new CheckBox
        {
            Text = text,
            Dock = DockStyle.Fill,
            Checked = checkedState,
            ForeColor = Color.FromArgb(200, 200, 200),
            AutoSize = false,
        };
    }

    private static Label MakeLabel(string text)
    {
        return new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(200, 200, 200),
        };
    }

    private static TextBox MakeTextBox(string value)
    {
        return new TextBox
        {
            Dock = DockStyle.Fill,
            Text = value ?? "",
            BackColor = Color.FromArgb(50, 50, 50),
            ForeColor = Color.FromArgb(220, 220, 220),
            BorderStyle = BorderStyle.FixedSingle,
        };
    }

    private static NumericUpDown MakeNumeric(int min, int max, int value)
    {
        return new NumericUpDown
        {
            Dock = DockStyle.Left,
            Width = 100,
            Minimum = min,
            Maximum = max,
            Value = Math.Clamp(value, min, max),
            BackColor = Color.FromArgb(50, 50, 50),
            ForeColor = Color.FromArgb(220, 220, 220),
        };
    }
}