using MinorShift.Emuera.AI;
using MinorShift.Emuera.AI.Compute;
using MinorShift.Emuera.AI.Traits;
using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace MinorShift.Emuera.Forms;

/// <summary>
/// ERA-AI 的界面入口。AI 面板与菜单项。
/// 单独放在 partial 文件里，让对 MainWindow.cs 本体的改动最少。
/// </summary>
internal sealed partial class MainWindow
{
    private ToolStripMenuItem aiRootMenuItem;
    private ToolStripMenuItem aiSelfTestMenuItem;
    private ToolStripMenuItem aiAbortMenuItem;
    private ToolStripMenuItem aiShowLogMenuItem;
    private ToolStripMenuItem aiTogglePanelMenuItem;
    private ToolStripMenuItem aiSettingsMenuItem;
    private ToolStripMenuItem aiReloadTraitsMenuItem;
    private ToolStripMenuItem aiPreviewPromptMenuItem;
    private ToolStripMenuItem aiTraitDiagMenuItem;
    private ToolStripMenuItem aiPreviewComputeMenuItem;
    private ToolStripMenuItem aiComputeLogMenuItem;
    private ToolStripMenuItem aiManualEditMenuItem;
    private ToolStripMenuItem aiUndoComputeMenuItem;

    private SplitContainer aiSplitContainer;
    private AiPanel aiPanel;
    private bool aiPanelVisible;

    private void InstallAiMenu()
    {
        AiConfig.Load();
        AiTraitLibrary.Load();

        aiSelfTestMenuItem = new ToolStripMenuItem("发起测试请求(P0 假数据)");
        aiSelfTestMenuItem.Click += AiSelfTestMenuItem_Click;

        aiAbortMenuItem = new ToolStripMenuItem("终止当前请求") { Enabled = false };
        aiAbortMenuItem.Click += AiAbortMenuItem_Click;

        aiShowLogMenuItem = new ToolStripMenuItem("显示调度日志");
        aiShowLogMenuItem.Click += AiShowLogMenuItem_Click;

        aiTogglePanelMenuItem = new ToolStripMenuItem("显示/隐藏 AI 面板");
        aiTogglePanelMenuItem.Click += AiTogglePanelMenuItem_Click;

        aiSettingsMenuItem = new ToolStripMenuItem("AI 设置...");
        aiSettingsMenuItem.Click += AiSettingsMenuItem_Click;

        aiReloadTraitsMenuItem = new ToolStripMenuItem("重载词条库");
        aiReloadTraitsMenuItem.Click += AiReloadTraitsMenuItem_Click;

        aiPreviewPromptMenuItem = new ToolStripMenuItem("预览当前角色 prompt");
        aiPreviewPromptMenuItem.Click += AiPreviewPromptMenuItem_Click;

        aiTraitDiagMenuItem = new ToolStripMenuItem("显示词条诊断");
        aiTraitDiagMenuItem.Click += AiTraitDiagMenuItem_Click;

        aiPreviewComputeMenuItem = new ToolStripMenuItem("预览副 API 请求");
        aiPreviewComputeMenuItem.Click += AiPreviewComputeMenuItem_Click;

        aiComputeLogMenuItem = new ToolStripMenuItem("显示上轮副 API 往返");
        aiComputeLogMenuItem.Click += AiComputeLogMenuItem_Click;

        // 玩家侧的数值出口。放在菜单而不是只放面板，是因为不开 AI 面板也该改得到。
        aiManualEditMenuItem = new ToolStripMenuItem("手动调整数值...");
        aiManualEditMenuItem.Click += AiManualEditMenuItem_Click;

        aiUndoComputeMenuItem = new ToolStripMenuItem("撤销上轮数值结算");
        aiUndoComputeMenuItem.Click += AiUndoComputeMenuItem_Click;

        aiRootMenuItem = new ToolStripMenuItem("AI");
        aiRootMenuItem.DropDownItems.Add(aiTogglePanelMenuItem);
        aiRootMenuItem.DropDownItems.Add(aiSettingsMenuItem);
        aiRootMenuItem.DropDownItems.Add(new ToolStripSeparator());
        aiRootMenuItem.DropDownItems.Add(aiReloadTraitsMenuItem);
        aiRootMenuItem.DropDownItems.Add(aiPreviewPromptMenuItem);
        aiRootMenuItem.DropDownItems.Add(aiTraitDiagMenuItem);
        aiRootMenuItem.DropDownItems.Add(new ToolStripSeparator());
        aiRootMenuItem.DropDownItems.Add(aiPreviewComputeMenuItem);
        aiRootMenuItem.DropDownItems.Add(aiComputeLogMenuItem);
        aiRootMenuItem.DropDownItems.Add(aiManualEditMenuItem);
        aiRootMenuItem.DropDownItems.Add(aiUndoComputeMenuItem);
        aiRootMenuItem.DropDownItems.Add(new ToolStripSeparator());
        aiRootMenuItem.DropDownItems.Add(aiSelfTestMenuItem);
        aiRootMenuItem.DropDownItems.Add(aiAbortMenuItem);
        aiRootMenuItem.DropDownItems.Add(new ToolStripSeparator());
        aiRootMenuItem.DropDownItems.Add(aiShowLogMenuItem);

        menuStrip.Items.Insert(menuStrip.Items.Count - 1, aiRootMenuItem);

        AiDispatcher.TurnCompleted += AiDispatcher_TurnCompleted;
    }

    private void AiTogglePanelMenuItem_Click(object sender, EventArgs e)
    {
        ToggleAiPanel();
    }

    private void AiSettingsMenuItem_Click(object sender, EventArgs e)
    {
        using var dlg = new AiSettingsDialog();
        dlg.ShowDialog(this);
    }

    private void ToggleAiPanel()
    {
        if (aiPanelVisible)
        {
            HideAiPanel();
        }
        else
        {
            ShowAiPanel();
        }
    }

    private void ShowAiPanel()
    {
        if (aiPanelVisible)
            return;

        if (aiSplitContainer == null)
        {
            aiSplitContainer = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                FixedPanel = FixedPanel.Panel2,
                SplitterWidth = 4,
                BackColor = System.Drawing.Color.FromArgb(50, 50, 50),
            };

            var existingControls = new Control[Controls.Count];
            Controls.CopyTo(existingControls, 0);

            Controls.Clear();

            foreach (var ctrl in existingControls)
            {
                if (ctrl is MenuStrip || ctrl is StatusStrip)
                {
                    Controls.Add(ctrl);
                }
                else
                {
                    ctrl.Dock = DockStyle.Fill;
                    aiSplitContainer.Panel1.Controls.Add(ctrl);
                }
            }

            aiPanel = new AiPanel(console);
            aiSplitContainer.Panel2.Controls.Add(aiPanel);

            Controls.Add(aiSplitContainer);
            aiSplitContainer.SplitterDistance = (int)(ClientSize.Width * 0.6);
        }
        else
        {
            aiSplitContainer.Panel2Collapsed = false;
        }

        aiPanelVisible = true;
    }

    private void HideAiPanel()
    {
        if (!aiPanelVisible || aiSplitContainer == null)
            return;

        aiSplitContainer.Panel2Collapsed = true;
        aiPanelVisible = false;
    }

    private void AiReloadTraitsMenuItem_Click(object sender, EventArgs e)
    {
        AiTraitLibrary.Reload(out string summary);
        if (console == null)
            return;
        console.PrintSingleLine($"[AI] {summary}");
        foreach (string line in AiTraitLibrary.Diagnostics)
            console.PrintSingleLine($"[AI][词条库] {line}");
        console.RefreshStrings(true);
    }

    private void AiPreviewPromptMenuItem_Click(object sender, EventArgs e)
    {
        if (console == null)
            return;
        string prompt = AiPromptBuilder.BuildForCurrentTarget(out AiPromptBuildInfo info);
        console.PrintSingleLine("---- system prompt 预览 ----");
        if (!info.UsedTraits)
            console.PrintSingleLine($"[AI] 未使用词条（{info.FallbackReason}），以下为静态 prompt。");
        else
            console.PrintSingleLine($"[AI] 角色号 {info.CharaNo}（登录号 {info.Register}），命中 {info.Traits.Count} 条词条。");
        foreach (AiTraitInstance t in info.Traits)
            console.PrintSingleLine($"[AI] 命中：{t.Name}({t.Id}) 得分 {t.Score} 优先级 {t.Priority}");
        foreach (string line in (prompt ?? "").Replace("\r\n", "\n").Split('\n'))
            console.PrintSingleLine(line);
        console.PrintSingleLine($"---- 共 {(prompt ?? "").Length} 字{(info.Truncated ? "（已截断）" : "")} ----");
        console.RefreshStrings(true);
    }

    private void AiTraitDiagMenuItem_Click(object sender, EventArgs e)
    {
        if (console == null)
            return;
        console.PrintSingleLine($"---- 词条库：{AiTraitLibrary.LoadedPath}（{AiTraitLibrary.Count} 条）----");
        foreach (string line in AiTraitLibrary.Diagnostics)
            console.PrintSingleLine($"[静态校验] {line}");
        console.PrintSingleLine("---- 运行期诊断 ----");
        foreach (string line in AiTraitDiagnostics.Entries)
            console.PrintSingleLine(line);
        if (AiTraitLibrary.IsStale())
            console.PrintSingleLine("[提示] 磁盘上的词条库已被修改，可通过「重载词条库」生效。");
        console.RefreshStrings(true);
    }

    /// <summary>
    /// 按当前 TARGET 装一份副 API 请求并打印出来，但不发网络请求。
    /// 配 compute 段时必须先看这个：schema 里的 field 枚举、权威状态的实际读数、
    /// 以及哪些字段被静默排除了，都在这里一次看全。
    /// </summary>
    private void AiPreviewComputeMenuItem_Click(object sender, EventArgs e)
    {
        if (console == null)
            return;

        console.PrintSingleLine("---- 副 API 请求预览（不发送） ----");

        if (AiTraitLibrary.ComputeTemplate == null)
        {
            console.PrintSingleLine("[AI] 词条库里没有 compute 段，副 API 无契约可用。");
            console.RefreshStrings(true);
            return;
        }
        if (!AiConfig.IsComputeReady(out string configReason))
            console.PrintSingleLine($"[AI] 注意：当前配置下副 API 不会被调用（{configReason}）。以下仅为契约预览。");

        long charaNo = PreviewCharaNo(out string charaError);
        if (charaNo < 0)
        {
            console.PrintSingleLine($"[AI] 无法确定当前角色：{charaError}");
            console.RefreshStrings(true);
            return;
        }

        AiComputeRequest request = AiComputeRequestBuilder.Build(charaNo, "（预览用占位事件）", 0, out string buildError);
        if (request == null)
        {
            console.PrintSingleLine($"[AI] 装配失败：{buildError}");
            console.RefreshStrings(true);
            return;
        }

        console.PrintSingleLine($"[AI] 角色号 {request.CharaNo}｜可写字段 {request.Fields.Count} 个｜turn_id {request.TurnId}");
        foreach (AiComputeField f in request.Fields)
            console.PrintSingleLine($"[AI] 字段：{f.Field} → {f.Target}｜区间 [{f.Min}, {f.Max}]｜单轮上限 {f.MaxDelta}｜op {string.Join("/", f.EffectiveOps)}");

        console.PrintSingleLine("---- 权威状态快照 ----");
        PrintLines(request.StateJson);
        console.PrintSingleLine("---- 下发的 messages ----");
        foreach (ChatMessage m in request.Messages)
        {
            console.PrintSingleLine($"[{m.Role}]");
            PrintLines(m.Content);
        }
        console.PrintSingleLine("---- function schema ----");
        PrintLines(request.SchemaJson);
        console.RefreshStrings(true);
    }

    /// <summary>上一轮副 API 到底传了什么、回了什么、为什么没写。排查数值问题的第一站。</summary>
    private void AiComputeLogMenuItem_Click(object sender, EventArgs e)
    {
        if (console == null)
            return;

        AiComputeTurnInfo info = AiDispatcher.LastComputeInfo;
        console.PrintSingleLine("---- 上一轮副 API 往返 ----");
        if (info == null)
        {
            console.PrintSingleLine("[AI] 还没有副 API 往返记录。");
            console.RefreshStrings(true);
            return;
        }

        console.PrintSingleLine($"[AI] turn_id {info.TurnId}｜角色号 {info.CharaNo}｜可写字段 {info.FieldCount} 个");
        if (!info.Used)
            console.PrintSingleLine($"[AI] 本轮未改动数值：{info.SkipReason}");
        foreach (AiAppliedChange applied in info.Applied)
            console.PrintSingleLine($"[AI] 已写入 {applied}（{applied.Op} {applied.RequestedValue}）理由：{applied.Reason}");
        foreach (string warning in info.Warnings)
            console.PrintSingleLine($"[AI] 副 API 提醒：{warning}");
        if (!string.IsNullOrEmpty(info.NarrativeHint))
            console.PrintSingleLine($"[AI] 结果提示：{info.NarrativeHint}");

        if (!string.IsNullOrEmpty(info.StateJson))
        {
            console.PrintSingleLine("---- 当轮下发的权威状态 ----");
            PrintLines(info.StateJson);
        }
        if (!string.IsNullOrEmpty(info.RawJson))
        {
            console.PrintSingleLine("---- 副 API 原始输出 ----");
            PrintLines(info.RawJson);
        }

        AiPendingTransaction pending = AiDispatcher.PendingTransaction;
        if (pending != null)
        {
            console.PrintSingleLine($"[AI] 存在待处置事务（{pending.TurnId}）：数值已写入 {pending.Applied.Count} 项但正文失败（{pending.FailureReason}）。");
            console.PrintSingleLine("[AI] 在 AI 面板点「重生成」保留数值只重写正文，或点「回滚数值」撤回本轮数值。");
        }
        console.RefreshStrings(true);
    }

    /// <summary>
    /// 手动调整数值。开放这条路是明确的设计选择：玩家的乐趣不该被经济/战斗数值卡住，
    /// 也不该被一次自己都觉得不合理的 AI 结算绑住。
    /// 可改范围与副 API 完全一致（compute.writable_fields），但不受幅度与区间限制。
    /// </summary>
    private void AiManualEditMenuItem_Click(object sender, EventArgs e)
    {
        if (console == null)
            return;
        if (AiRequestLock.IsLocked)
        {
            console.PrintSingleLine("[AI] 请求进行中，等这一轮结束再调整数值。");
            console.RefreshStrings(true);
            return;
        }

        List<AiEditableEntry> entries = AiManualEditor.CollectEditable(out string error);
        if (entries.Count == 0)
        {
            console.PrintSingleLine($"[AI] 没有可调整的字段：{error}");
            console.RefreshStrings(true);
            return;
        }

        using var dlg = new AiManualEditDialog(entries);
        if (dlg.ShowDialog(this) != DialogResult.OK || dlg.Applied == null)
            return;

        foreach (AiAppliedChange applied in dlg.Applied)
            console.PrintSingleLine($"[AI] 手动调整 {applied}");
        console.PrintSingleLine("[AI] 本次调整已写入短记忆，副 API 下一轮会把它当成既定事实。");
        console.RefreshStrings(true);
    }

    /// <summary>
    /// 撤销上一轮副 API 写下的数值，正文与对话历史保持原样。
    /// 面向「这轮叙事我喜欢，但结算不合理」——两条通道本来就是分开的，
    /// 没有理由为了改一个数字连正文一起重来。
    /// </summary>
    private void AiUndoComputeMenuItem_Click(object sender, EventArgs e)
    {
        if (console == null)
            return;
        if (!AiDispatcher.TryUndoLastComputeApply(out string error))
        {
            console.PrintSingleLine($"[AI] 无法撤销：{error}");
            console.RefreshStrings(true);
            return;
        }

        AiComputeTurnInfo info = AiDispatcher.LastComputeInfo;
        foreach (AiAppliedChange applied in info.Applied)
            console.PrintSingleLine($"[AI] 已撤销 {applied.Field}：{applied.After} → {applied.Before}");
        console.PrintSingleLine("[AI] 正文与对话历史未改动。撤销已写入短记忆，副 API 下一轮不会试图圆这个跳变。");
        console.RefreshStrings(true);
    }

    /// <summary>预览用的角色号解析。与调度器同一套逻辑：TARGET 是登录号，现算现用。</summary>
    private long PreviewCharaNo(out string error)
    {
        error = null;
        if (GlobalStatic.VariableData == null)
        {
            error = "引擎尚未就绪";
            return -1;
        }
        if (!AiVariableAccess.TryReadInt("TARGET:0", out long register, out string readError))
        {
            error = $"无法读取 TARGET（{readError}）";
            return -1;
        }
        if (register < 0)
        {
            error = "当前没有调教对象（TARGET < 0）";
            return -1;
        }
        List<MinorShift.Emuera.Runtime.Script.Statements.Variable.CharacterData> list = GlobalStatic.VariableData.CharacterList;
        if (register >= list.Count)
        {
            error = $"TARGET={register} 超出已登录角色数 {list.Count}";
            return -1;
        }
        return list[(int)register].NO;
    }

    private void PrintLines(string text)
    {
        foreach (string line in (text ?? "").Replace("\r\n", "\n").Split('\n'))
            console.PrintSingleLine(line);
    }

    private void AiSelfTestMenuItem_Click(object sender, EventArgs e)
    {
        if (console == null)
            return;
        AiDispatcher.UseFakeBackend = true;
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
            console.PrintSingleLine($"[AI] 请求已终止（耗时 {result.ElapsedMs} 毫秒）{(result.ComputeRolledBack ? "，已回滚本轮数值" : "，未写入任何数值")}。");
        else if (!result.Success)
        {
            console.PrintSingleLine($"[AI] 请求失败：{result.ErrorMessage}");
            if (result.NarrativeFailedAfterApply)
                console.PrintSingleLine("[AI] 本轮数值已写入但正文失败，请在 AI 面板选择「重生成」或「回滚数值」。");
        }
        else
        {
            console.PrintSingleLine(result.NarrativeText);
            foreach (var change in result.Changes)
            {
                if (AiVariableAccess.TryReadInt(change.Target, out long value, out _))
                    console.PrintSingleLine($"[AI] 已回写 {change.Target} {change.Op} {change.Value}，当前值 {value}");
            }
            foreach (AiAppliedChange applied in result.ComputeApplied)
                console.PrintSingleLine($"[AI] 副 API 已回写 {applied}");
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
        if (aiManualEditMenuItem != null)
            aiManualEditMenuItem.Enabled = !locked;
        if (aiUndoComputeMenuItem != null)
        {
            AiComputeTurnInfo info = AiDispatcher.LastComputeInfo;
            aiUndoComputeMenuItem.Enabled = !locked
                && AiDispatcher.PendingTransaction == null
                && info != null && info.Applied.Count > 0 && !info.Undone;
        }
    }
}
