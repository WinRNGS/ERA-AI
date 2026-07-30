using MinorShift.Emuera.AI;
using MinorShift.Emuera.AI.Context;
using MinorShift.Emuera.AI.Interact;
using MinorShift.Emuera.AI.Security;
using MinorShift.Emuera.AI.Traits;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MinorShift.Emuera.Forms;

/// <summary>
/// AI 设置对话框（P6 完善版）。
///
/// P6 新增：
///   - 密钥格式验证（设置时即检查格式，拒绝明显错误）。
///   - 连接测试按钮（向端点发轻量请求验证密钥与连通性）。
///   - 上下文压缩 tab（展示当前配置与快速说明）。
///   - 输入验证（端点格式、非空等）。
/// </summary>
internal sealed class AiSettingsDialog : Form
{
    private readonly TextBox txtEndpoint;
    private readonly ComboBox cmbModel;
    private readonly Button btnFetchModels;
    private readonly TextBox txtApiKey;
    private readonly NumericUpDown numMaxTokens;
    private readonly NumericUpDown numTimeout;
    private readonly NumericUpDown numRetries;
    private readonly NumericUpDown numTemperature;
    private readonly TextBox txtSystemPrompt;
    private readonly CheckBox chkUseTraitPrompt;

    private readonly CheckBox chkUseComputeApi;
    private readonly TextBox txtComputeEndpoint;
    private readonly ComboBox cmbComputeModel;
    private readonly Button btnFetchComputeModels;
    private readonly CheckBox chkComputeReuseKey;
    private readonly TextBox txtComputeApiKey;
    private readonly NumericUpDown numComputeMaxTokens;
    private readonly NumericUpDown numComputeTimeout;
    private readonly NumericUpDown numComputeRetries;

    private readonly Label lblContextInfo;
    private Button btnTestMain;
    private Button btnTestCompute;

    public AiSettingsDialog()
    {
        Text = "AI 设置";
        Size = new Size(580, 660);
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
        var mainLayout = MakeLayout(11);
        int row = 0;

        mainLayout.Controls.Add(MakeLabel("API 端点:"), 0, row);
        txtEndpoint = MakeTextBox(AiConfig.ApiEndpoint);
        mainLayout.Controls.Add(txtEndpoint, 1, row++);

        mainLayout.Controls.Add(MakeLabel("模型:"), 0, row);
        cmbModel = MakeModelCombo(AiConfig.Model, AiConfig.CachedModels);
        btnFetchModels = MakeSmallButton("拉取列表");
        btnFetchModels.Click += BtnFetchModels_Click;
        mainLayout.Controls.Add(MakeComboWithButton(cmbModel, btnFetchModels), 1, row++);

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

        // 连接测试按钮
        mainLayout.Controls.Add(MakeLabel(""), 0, row);
        btnTestMain = new Button
        {
            Text = "测试连接",
            Width = 100,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(60, 80, 130),
            ForeColor = Color.White,
        };
        btnTestMain.Click += BtnTestMain_Click;
        mainLayout.Controls.Add(btnTestMain, 1, row++);

        mainPage.Controls.Add(mainLayout);

        // ---------- 副 API ----------
        var computePage = new TabPage("副 API（计算）") { BackColor = Color.FromArgb(40, 40, 40) };
        var computeLayout = MakeLayout(10);
        row = 0;

        computeLayout.Controls.Add(MakeLabel("启用:"), 0, row);
        chkUseComputeApi = MakeCheckBox(
            "启用副 API 负责数值结算（关闭则只跑叙事，数值不动）", AiConfig.UseComputeApi);
        computeLayout.Controls.Add(chkUseComputeApi, 1, row++);

        computeLayout.Controls.Add(MakeLabel("API 端点:"), 0, row);
        txtComputeEndpoint = MakeTextBox(AiConfig.ComputeApiEndpoint);
        computeLayout.Controls.Add(txtComputeEndpoint, 1, row++);

        computeLayout.Controls.Add(MakeLabel("模型:"), 0, row);
        cmbComputeModel = MakeModelCombo(AiConfig.ComputeModel, AiConfig.CachedComputeModels);
        btnFetchComputeModels = MakeSmallButton("拉取列表");
        btnFetchComputeModels.Click += BtnFetchComputeModels_Click;
        computeLayout.Controls.Add(MakeComboWithButton(cmbComputeModel, btnFetchComputeModels), 1, row++);

        computeLayout.Controls.Add(MakeLabel("复用主密钥:"), 0, row);
        chkComputeReuseKey = MakeCheckBox(
            "复用主 API 密钥（取消勾选可单独设副 API 密钥）", AiConfig.ComputeReusesMainKey);
        computeLayout.Controls.Add(chkComputeReuseKey, 1, row++);

        computeLayout.Controls.Add(MakeLabel("副 API Key:"), 0, row);
        txtComputeApiKey = MakeTextBox("");
        txtComputeApiKey.UseSystemPasswordChar = true;
        if (AiConfig.HasComputeApiKey)
            txtComputeApiKey.PlaceholderText = "(已设置，留空不修改)";
        computeLayout.Controls.Add(txtComputeApiKey, 1, row++);

        computeLayout.Controls.Add(MakeLabel("Max Tokens:"), 0, row);
        numComputeMaxTokens = MakeNumeric(1, 4096, AiConfig.ComputeMaxTokens);
        computeLayout.Controls.Add(numComputeMaxTokens, 1, row++);

        computeLayout.Controls.Add(MakeLabel("超时(秒):"), 0, row);
        numComputeTimeout = MakeNumeric(5, 60, AiConfig.ComputeTimeoutSeconds);
        computeLayout.Controls.Add(numComputeTimeout, 1, row++);

        computeLayout.Controls.Add(MakeLabel("重试次数:"), 0, row);
        numComputeRetries = MakeNumeric(0, 5, AiConfig.ComputeMaxRetries);
        computeLayout.Controls.Add(numComputeRetries, 1, row++);

        // 连接测试按钮
        computeLayout.Controls.Add(MakeLabel(""), 0, row);
        btnTestCompute = new Button
        {
            Text = "测试连接",
            Width = 100,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(60, 80, 130),
            ForeColor = Color.White,
        };
        btnTestCompute.Click += BtnTestCompute_Click;
        computeLayout.Controls.Add(btnTestCompute, 1, row++);

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

        // ---------- 上下文压缩 ----------
        var contextPage = new TabPage("上下文压缩") { BackColor = Color.FromArgb(40, 40, 40) };
        var contextLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 1,
            Padding = new Padding(10),
            BackColor = Color.FromArgb(40, 40, 40),
        };
        contextLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        contextLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        lblContextInfo = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(200, 200, 200),
            Text = BuildContextInfoText(),
            AutoSize = false,
        };
        contextLayout.Controls.Add(lblContextInfo, 0, 0);
        contextPage.Controls.Add(contextLayout);

        tabs.TabPages.Add(mainPage);
        tabs.TabPages.Add(computePage);
        tabs.TabPages.Add(contextPage);

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
        // P6：输入验证
        string endpoint = txtEndpoint.Text.Trim();
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            ShowWarning("主 API 端点不能为空。");
            return;
        }
        if (!endpoint.StartsWith("http://") && !endpoint.StartsWith("https://"))
        {
            ShowWarning("主 API 端点必须以 http:// 或 https:// 开头。");
            return;
        }

        string model = cmbModel.Text.Trim();
        if (string.IsNullOrWhiteSpace(model))
        {
            ShowWarning("模型名称不能为空。");
            return;
        }

        // P6：密钥格式验证
        string key = txtApiKey.Text.Trim();
        if (!string.IsNullOrEmpty(key))
        {
            if (!AiKeyManager.ValidateKeyFormat(key, out string keyReason))
            {
                ShowWarning($"主 API 密钥格式异常：{keyReason}");
                return;
            }
        }

        string computeKey = txtComputeApiKey.Text.Trim();
        if (!string.IsNullOrEmpty(computeKey))
        {
            if (!AiKeyManager.ValidateKeyFormat(computeKey, out string ckReason))
            {
                ShowWarning($"副 API 密钥格式异常：{ckReason}");
                return;
            }
        }

        // 副 API 端点验证（仅在启用时检查）
        if (chkUseComputeApi.Checked)
        {
            string cEndpoint = txtComputeEndpoint.Text.Trim();
            if (string.IsNullOrWhiteSpace(cEndpoint))
            {
                ShowWarning("副 API 已启用，但端点为空。");
                return;
            }
            if (!cEndpoint.StartsWith("http://") && !cEndpoint.StartsWith("https://"))
            {
                ShowWarning("副 API 端点必须以 http:// 或 https:// 开头。");
                return;
            }
        }

        // 保存
        AiConfig.ApiEndpoint = endpoint;
        AiConfig.Model = model;
        AiConfig.MaxTokens = (int)numMaxTokens.Value;
        AiConfig.TimeoutSeconds = (int)numTimeout.Value;
        AiConfig.MaxRetries = (int)numRetries.Value;
        AiConfig.Temperature = (double)numTemperature.Value / 100.0;
        AiConfig.SystemPrompt = txtSystemPrompt.Text;
        AiConfig.UseTraitPrompt = chkUseTraitPrompt.Checked;

        if (!string.IsNullOrEmpty(key))
            AiConfig.SetApiKey(key);

        AiConfig.UseComputeApi = chkUseComputeApi.Checked;
        AiConfig.ComputeApiEndpoint = txtComputeEndpoint.Text.Trim();
        AiConfig.ComputeModel = cmbComputeModel.Text.Trim();
        AiConfig.ComputeMaxTokens = (int)numComputeMaxTokens.Value;
        AiConfig.ComputeTimeoutSeconds = (int)numComputeTimeout.Value;
        AiConfig.ComputeMaxRetries = (int)numComputeRetries.Value;
        AiConfig.ComputeReusesMainKey = chkComputeReuseKey.Checked;

        if (!string.IsNullOrEmpty(computeKey))
            AiConfig.SetComputeApiKey(computeKey);

        AiConfig.Save();
        Close();
    }

    private async void BtnTestMain_Click(object sender, EventArgs e)
    {
        btnTestMain.Enabled = false;
        btnTestMain.Text = "测试中...";
        try
        {
            string endpoint = txtEndpoint.Text.Trim();
            string key = txtApiKey.Text.Trim();
            if (string.IsNullOrEmpty(key))
                key = AiConfig.GetApiKeyPlain();

            if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrEmpty(key))
            {
                ShowWarning("需要填写端点和密钥才能测试连接。");
                return;
            }

            string result = await TestConnectionAsync(endpoint, key, cmbModel.Text.Trim());
            MessageBox.Show(result, "主 API 连接测试", MessageBoxButtons.OK,
                result.StartsWith("成功", StringComparison.Ordinal) ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        finally
        {
            btnTestMain.Enabled = true;
            btnTestMain.Text = "测试连接";
        }
    }

    private async void BtnTestCompute_Click(object sender, EventArgs e)
    {
        btnTestCompute.Enabled = false;
        btnTestCompute.Text = "测试中...";
        try
        {
            string endpoint = txtComputeEndpoint.Text.Trim();
            string key = txtComputeApiKey.Text.Trim();
            if (string.IsNullOrEmpty(key))
            {
                if (chkComputeReuseKey.Checked)
                {
                    key = txtApiKey.Text.Trim();
                    if (string.IsNullOrEmpty(key))
                        key = AiConfig.GetApiKeyPlain();
                }
                else
                {
                    key = AiConfig.GetComputeApiKeyPlain();
                }
            }

            if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrEmpty(key))
            {
                ShowWarning("需要填写端点和密钥才能测试连接。");
                return;
            }

            string result = await TestConnectionAsync(endpoint, key, cmbComputeModel.Text.Trim());
            MessageBox.Show(result, "副 API 连接测试", MessageBoxButtons.OK,
                result.StartsWith("成功", StringComparison.Ordinal) ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        finally
        {
            btnTestCompute.Enabled = true;
            btnTestCompute.Text = "测试连接";
        }
    }

    /// <summary>
    /// 向端点发送一个最小的 chat completions 请求来验证连通性与认证。
    ///
    /// 只看 HTTP 状态码是不够的：把 base（如 https://host）当成端点填进来时，网关会用自己的
    /// 管理页回一个 HTTP 200 text/html，那时"测试连接成功"是假阳性——真正发请求时正文解析
    /// 不出 content，表现成 AI 一句话都不回。所以这里必须确认响应体真的是 chat completions。
    /// </summary>
    private static async Task<string> TestConnectionAsync(string endpoint, string apiKey, string model)
    {
        string requestUrl = AiBackend.DeriveChatUrl(endpoint);
        string urlNote = string.Equals(requestUrl, endpoint?.Trim()?.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)
            ? ""
            : $"{Environment.NewLine}实际请求地址：{requestUrl}（已按 OpenAI 兼容惯例补全路径）";

        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

            // 用用户实际选中的模型去测，才能同时验出「模型名写错」这一类问题。
            string testModel = string.IsNullOrWhiteSpace(model) ? "gpt-4o-mini" : model;
            string body = System.Text.Json.JsonSerializer.Serialize(new
            {
                model = testModel,
                messages = new[] { new { role = "user", content = "hi" } },
                max_tokens = 1,
            });
            using var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");

            using var response = await client.PostAsync(requestUrl, content).ConfigureAwait(false);
            int code = (int)response.StatusCode;
            string respBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                if (LooksLikeChatCompletion(respBody))
                    return $"成功！服务端返回 {code}，连接与认证均正常。{urlNote}";

                return $"服务端返回 {code}，但响应体不是 chat completions 格式。" +
                       $"{Environment.NewLine}端点很可能指错了（例如只填了域名，被网关的首页接走）。" +
                       $"{Environment.NewLine}请填写完整的 chat completions 端点，如 {requestUrl}。{urlNote}";
            }

            string safeBody = AiErrorReporter.Sanitize(respBody);
            if (safeBody.Length > 200)
                safeBody = safeBody[..200] + "…";

            return code switch
            {
                401 => $"认证失败（{code}）：密钥可能不正确或已过期。{urlNote}",
                403 => $"权限被拒（{code}）：密钥可能没有 chat completions 权限。{urlNote}",
                404 => $"路径不存在（{code}）：端点写错了，请确认上游的 chat completions 路径。{urlNote}",
                429 => $"限流（{code}）：请求过于频繁，稍后再试。{urlNote}",
                _ => $"服务端返回 {code}：{safeBody}{urlNote}",
            };
        }
        catch (TaskCanceledException)
        {
            return "连接超时（15 秒），请检查端点是否正确或网络是否可达。";
        }
        catch (HttpRequestException ex)
        {
            string msg = AiErrorReporter.Sanitize(ex.Message);
            return $"网络错误：{msg}";
        }
        catch (Exception ex)
        {
            return $"测试失败：{AiErrorReporter.Sanitize(ex.Message)}";
        }
    }

    /// <summary>
    /// 判断响应体是否真的是 chat completions 响应。只要能取到 choices[0].message 就算通过——
    /// max_tokens=1 时 content 可能是空串，不能拿 content 非空当条件。
    /// </summary>
    private static bool LooksLikeChatCompletion(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
            return false;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(responseBody);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object)
                return false;
            if (!doc.RootElement.TryGetProperty("choices", out var choices)
                || choices.ValueKind != System.Text.Json.JsonValueKind.Array
                || choices.GetArrayLength() == 0)
                return false;
            return choices[0].TryGetProperty("message", out _)
                || choices[0].TryGetProperty("delta", out _)
                || choices[0].TryGetProperty("text", out _);
        }
        catch
        {
            return false;
        }
    }

    private static string BuildContextInfoText()
    {
        var template = AiTraitLibrary.ContextTemplate;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("上下文压缩参数由 ai_traits.json 的 context 段控制。");
        sb.AppendLine("修改后点击「AI → 重载词条库」生效。");
        sb.AppendLine();
        sb.AppendLine("当前配置：");
        sb.AppendLine();

        if (template != null)
        {
            sb.AppendLine($"  启用：{(template.Enabled ? "是" : "否")}");
            sb.AppendLine($"  上下文窗口：{template.ContextWindow} token");
            sb.AppendLine($"  触发阈值：{template.TriggerRatio:P0}（达到后触发压缩）");
            sb.AppendLine($"  目标比例：{template.TargetRatio:P0}（压缩后目标占比）");
            sb.AppendLine($"  最少保留轮数：{template.RetainRounds}");
        }
        else
        {
            sb.AppendLine("  （使用默认值：context_window=8192, trigger_ratio=80%, target_ratio=50%, retain_rounds=3）");
        }

        sb.AppendLine();
        sb.AppendLine("说明：");
        sb.AppendLine("  - context_window 应与实际使用的模型窗口一致。");
        sb.AppendLine("    GPT-4o：128k，但建议设 16384（实际有效利用窗口）。");
        sb.AppendLine("    本地模型：通常 4096-8192。");
        sb.AppendLine("  - 触发阈值建议 0.75-0.85，太低会频繁压缩，太高可能超窗口。");
        sb.AppendLine("  - 压缩生成摘要走副 API 通道，副 API 未启用时压缩不可用。");
        sb.AppendLine();
        sb.AppendLine($"当前状态：有摘要={AiContextCompressor.HasSummary}，" +
                      $"当前对话轮数≈{AiConversation.Count / 2}");
        return sb.ToString();
    }

    private static void ShowWarning(string message)
    {
        MessageBox.Show(message, "验证提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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

    private async void BtnFetchModels_Click(object sender, EventArgs e)
    {
        string key = txtApiKey.Text.Trim();
        if (string.IsNullOrEmpty(key))
            key = AiConfig.GetApiKeyPlain();
        await FetchModelsIntoAsync(cmbModel, btnFetchModels, txtEndpoint.Text.Trim(), key, isCompute: false);
    }

    private async void BtnFetchComputeModels_Click(object sender, EventArgs e)
    {
        // 副 API 的密钥来源顺序与 BtnTestCompute_Click 保持一致：
        // 本框输入 → 勾了复用则取主框/主配置 → 否则取副配置。
        string key = txtComputeApiKey.Text.Trim();
        if (string.IsNullOrEmpty(key))
        {
            if (chkComputeReuseKey.Checked)
            {
                key = txtApiKey.Text.Trim();
                if (string.IsNullOrEmpty(key))
                    key = AiConfig.GetApiKeyPlain();
            }
            else
            {
                key = AiConfig.GetComputeApiKeyPlain();
            }
        }
        await FetchModelsIntoAsync(cmbComputeModel, btnFetchComputeModels, txtComputeEndpoint.Text.Trim(), key, isCompute: true);
    }

    /// <summary>
    /// 拉取模型列表并填进下拉框。
    ///
    /// 失败一律不清空既有候选：上游临时不可用时，缓存里的旧列表仍然比空列表有用。
    /// 拉取成功后立刻写进 AiConfig 缓存并落盘，这样下次开窗即使离线也有候选可选。
    /// </summary>
    private async Task FetchModelsIntoAsync(ComboBox combo, Button button, string endpoint, string apiKey, bool isCompute)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            ShowWarning("请先填写 API 端点。");
            return;
        }
        // 不强制要求密钥：本地端点（Ollama / LM Studio / vLLM）通常不校验认证。
        string previous = combo.Text;
        button.Enabled = false;
        button.Text = "拉取中...";
        try
        {
            List<string> models = await AiBackend.ListModelsAsync(endpoint, apiKey, CancellationToken.None);

            combo.BeginUpdate();
            try
            {
                combo.Items.Clear();
                foreach (string id in models)
                    combo.Items.Add(id);
            }
            finally
            {
                combo.EndUpdate();
            }

            // 原先选中的模型若仍在列表里就保持不变，否则不擅自替换用户的输入，
            // 只是把列表摆出来让用户自己挑。
            combo.Text = previous;

            if (isCompute)
                AiConfig.CachedComputeModels = models;
            else
                AiConfig.CachedModels = models;
            AiConfig.Save();

            bool stillThere = models.Contains(previous, StringComparer.OrdinalIgnoreCase);
            string tail = stillThere
                ? $"当前选择「{previous}」仍然可用。"
                : (string.IsNullOrWhiteSpace(previous)
                    ? "请从下拉列表中选择一个模型。"
                    : $"注意：当前填写的「{previous}」不在返回列表中，请确认或重新选择。");

            MessageBox.Show(
                $"成功拉取 {models.Count} 个模型。{Environment.NewLine}{tail}",
                "拉取模型列表",
                MessageBoxButtons.OK,
                stillThere || string.IsNullOrWhiteSpace(previous) ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"拉取失败：{AiErrorReporter.Sanitize(ex.Message)}{Environment.NewLine}{Environment.NewLine}"
                + $"实际请求地址：{SafeDeriveModelsUrl(endpoint)}{Environment.NewLine}"
                + "若上游不提供 /v1/models，可直接在下拉框里手动输入模型名。",
                "拉取模型列表",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        finally
        {
            button.Enabled = true;
            button.Text = "拉取列表";
        }
    }

    private static string SafeDeriveModelsUrl(string endpoint)
    {
        try
        {
            return AiBackend.DeriveModelsUrl(endpoint);
        }
        catch
        {
            return "(无法推导)";
        }
    }

    /// <summary>
    /// 可编辑下拉框。保留手填能力是刻意的：部分自建/代理端点不实现 /v1/models，
    /// 也有服务返回的列表里不含实际可用的别名，这时必须允许直接输入。
    /// </summary>
    private static ComboBox MakeModelCombo(string current, List<string> cached)
    {
        var combo = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDown,
            BackColor = Color.FromArgb(50, 50, 50),
            ForeColor = Color.FromArgb(220, 220, 220),
            FlatStyle = FlatStyle.Flat,
            AutoCompleteMode = AutoCompleteMode.SuggestAppend,
            AutoCompleteSource = AutoCompleteSource.ListItems,
        };
        if (cached != null)
        {
            foreach (string id in cached)
                combo.Items.Add(id);
        }
        combo.Text = current ?? "";
        return combo;
    }

    private static Button MakeSmallButton(string text)
    {
        return new Button
        {
            Text = text,
            Dock = DockStyle.Fill,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(60, 80, 130),
            ForeColor = Color.White,
        };
    }

    /// <summary>把下拉框与右侧按钮塞进一格：布局是两列 TableLayout，一格只放一个控件。</summary>
    private static Control MakeComboWithButton(ComboBox combo, Button button)
    {
        var host = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0),
            BackColor = Color.FromArgb(40, 40, 40),
        };
        host.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        host.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        host.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        host.Controls.Add(combo, 0, 0);
        host.Controls.Add(button, 1, 0);
        return host;
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
