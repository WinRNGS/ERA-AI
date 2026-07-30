using MinorShift.Emuera.AI.Compute;
using MinorShift.Emuera.AI.Traits;
using MinorShift.Emuera.GameView;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace MinorShift.Emuera.AI;

/// <summary>
/// AI 请求调度器。整套方案的地基。
///
/// 线程契约（违反任意一条都会破坏 ERA 数据结构）：
///   - 网络与解析：后台线程。只产出纯数据，绝不触碰变量或界面。
///   - 读变量、写变量、装 prompt：一律界面线程。跨线程访问变量层等于破坏数据结构。
///   - 锁的获取与释放：界面线程。任何异常路径都必须走到 Release，否则界面永久锁死。
///
/// P3 起本类串联双通道，时序为「先算后叙」（设计文档 S3.2）：
///   界面线程 装副 API 请求（含权威状态快照）
///        → 后台线程 副 API 计算
///        → 界面线程 校验 + 回写数值
///        → 界面线程 装主 API prompt（此时读到的已是新值）
///        → 后台线程 主 API 叙事
///        → 界面线程 收尾
/// 每一次跨线程都通过 InvokeOnUiThreadAsync 显式往返，读写变量的代码全部落在界面线程一侧。
/// </summary>
internal static class AiDispatcher
{
    /// <summary>回注完成后的通知，供 AI 面板刷新用。在界面线程触发。</summary>
    public static event Action<AiTurnResult> TurnCompleted;

    /// <summary>P0 自检开关：为 true 时使用假数据而不发真实网络请求。</summary>
    public static bool UseFakeBackend = true;

    /// <summary>假后端的模拟耗时，用于验证锁定期间界面不冻结。</summary>
    public static int FakeDelayMs = 1500;

    /// <summary>
    /// 副 API 的替身。非 null 时不发网络请求，直接用它的返回值当作 function call 参数 JSON。
    /// 仅供 P3 自检使用，正常运行为 null。
    /// </summary>
    public static Func<AiComputeRequest, string> ComputeBackendOverride;

    /// <summary>
    /// 主 API 的替身。非 null 时不发网络请求。抛异常即模拟主 API 失败。
    /// 仅供 P3 自检使用，正常运行为 null。
    /// </summary>
    public static Func<IReadOnlyList<ChatMessage>, string> MainBackendOverride;

    private static readonly object logGate = new();
    private static readonly List<string> log = [];

    /// <summary>最近一次装配的 system prompt 观测信息。界面线程读写，供面板与日志展示。</summary>
    public static AiPromptBuildInfo LastPromptInfo { get; private set; }

    /// <summary>最近一次副 API 往返的观测信息。界面线程读写。</summary>
    public static AiComputeTurnInfo LastComputeInfo { get; private set; }

    private static readonly object historyGate = new();
    private static readonly List<ChatMessage> conversationHistory = [];
    private const int MaxHistoryRounds = 20;

    /// <summary>
    /// 数值已写入但叙事失败时留下的待处置事务（RISK-05）。
    /// 只在界面线程读写。非 null 表示存档已变但玩家没拿到正文，必须显式处置。
    /// </summary>
    public static AiPendingTransaction PendingTransaction { get; private set; }

    public static IReadOnlyList<string> Log
    {
        get
        {
            lock (logGate)
                return log.ToArray();
        }
    }

    /// <summary>获取对话历史的只读快照。</summary>
    public static IReadOnlyList<ChatMessage> History
    {
        get
        {
            lock (historyGate)
                return conversationHistory.ToArray();
        }
    }

    /// <summary>清空对话历史。副 API 的短记忆一并清掉，否则两边窗口会错位。</summary>
    public static void ClearHistory()
    {
        lock (historyGate)
            conversationHistory.Clear();
        AiComputeMemory.Clear();
    }

    private static void Append(string line)
    {
        lock (logGate)
        {
            log.Add($"[{DateTime.Now:HH:mm:ss.fff}] {line}");
            if (log.Count > 500)
                log.RemoveRange(0, log.Count - 500);
        }
    }

    /// <summary>
    /// 发起一次 AI 请求。必须在界面线程调用。
    /// 返回 false 表示锁被占用或环境未就绪，调用方不应重试。
    /// </summary>
    public static bool TryBeginTurn(EmueraConsole console, string userInput)
    {
        if (console == null)
            return false;

        // 未处置的待处置事务必须先处置掉才能发新请求。
        // PendingTransaction 只有一份，放行新请求会把它连同 Before 快照一起覆盖，
        // 那批已经落盘的数值就再也回滚不了了。宁可挡住这一轮，也不能让存档失去可撤回性。
        if (PendingTransaction != null)
        {
            Append("拒绝：上一轮数值已写入但正文失败，请先在 AI 面板选择「重生成」「回滚数值」或「保留数值」");
            return false;
        }

        long ticket = AiRequestLock.TryAcquire(console, out CancellationToken token);
        if (ticket == 0)
        {
            Append("拒绝：已有请求进行中（硬锁定生效）");
            return false;
        }

        Append($"请求开始 ticket={ticket} input={Truncate(userInput, 40)}");

        var stopwatch = Stopwatch.StartNew();

        _ = Task.Run(async () =>
        {
            AiTurnResult result;
            try
            {
                result = UseFakeBackend
                    ? await RunFakeBackendAsync(ticket, userInput, token).ConfigureAwait(false)
                    : await RunRealBackendAsync(console, ticket, userInput, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                result = new AiTurnResult { Ticket = ticket, Success = false, Aborted = true, ErrorMessage = "已被玩家终止" };
            }
            catch (Exception e)
            {
                result = new AiTurnResult { Ticket = ticket, Success = false, ErrorMessage = e.Message };
            }
            result.ElapsedMs = stopwatch.ElapsedMilliseconds;
            CompleteOnUiThread(console, result);
        });

        return true;
    }

    /// <summary>请求终止当前请求。必须在界面线程调用。</summary>
    public static bool Abort()
    {
        bool ok = AiRequestLock.RequestAbort();
        Append(ok ? "收到终止请求" : "终止请求被忽略（当前无进行中的请求）");
        return ok;
    }

    /// <summary>
    /// 把结果切回界面线程完成收尾。这是唯一允许写变量与打印的地方。
    /// </summary>
    private static void CompleteOnUiThread(EmueraConsole console, AiTurnResult result)
    {
        var window = console.Window;
        if (window == null || !window.Created)
        {
            AiRequestLock.Release(result.Ticket);
            return;
        }

        Action finish = () => ApplyOnUiThread(console, result);
        try
        {
            if (window.InvokeRequired)
                window.BeginInvoke(finish);
            else
                finish();
        }
        catch (Exception e)
        {
            Append($"回注调度失败：{e.Message}");
            AiRequestLock.Release(result.Ticket);
        }
    }

    private static void ApplyOnUiThread(EmueraConsole console, AiTurnResult result)
    {
        try
        {
            if (result.Ticket != AiRequestLock.CurrentTicket)
            {
                Append($"丢弃过期结果 ticket={result.Ticket}");
                return;
            }

            bool aborted = result.Aborted || AiRequestLock.IsAborting;
            if (aborted)
            {
                result.Success = false;
                result.Aborted = true;
                // 终止的语义是「这一轮不算发生过」，所以副 API 已经写下的数值必须撤回。
                RollbackIfNeeded(result, "请求已终止");
                Append($"请求终止 ticket={result.Ticket}，未留下数值变更");
                return;
            }

            if (!result.Success)
            {
                Append($"请求失败 ticket={result.Ticket}：{result.ErrorMessage}");
                return;
            }

            if (!AiVariableAccess.TryApplyAll(result.Changes, out string error))
            {
                result.Success = false;
                result.ErrorMessage = error;
                Append($"数值回写被拒绝 ticket={result.Ticket}：{error}");
                return;
            }

            Append($"请求完成 ticket={result.Ticket}，写入 {result.Changes.Count} 项，耗时 {result.ElapsedMs}ms");
        }
        catch (Exception e)
        {
            result.Success = false;
            result.ErrorMessage = e.Message;
            Append($"回注异常 ticket={result.Ticket}：{e.Message}");
        }
        finally
        {
            AiRequestLock.Release(result.Ticket);
            try
            {
                TurnCompleted?.Invoke(result);
            }
            catch (Exception e)
            {
                Append($"完成通知异常：{e.Message}");
            }
        }
    }

    /// <summary>
    /// P0 假后端：不发网络请求，只模拟延迟并产出可校验的假数据。
    /// </summary>
    private static async Task<AiTurnResult> RunFakeBackendAsync(long ticket, string userInput, CancellationToken token)
    {
        await Task.Delay(FakeDelayMs, token).ConfigureAwait(false);
        return new AiTurnResult
        {
            Ticket = ticket,
            TurnId = TurnIdOf(ticket),
            Success = true,
            NarrativeText = $"[假数据] 收到输入「{Truncate(userInput, 60)}」，这是一段用于验证回注链路的占位正文。",
            Changes =
            [
                new AiValueChange { Target = "FLAG:0", Op = "add", Value = 1 },
            ],
        };
    }

    /// <summary>
    /// 真实链路：副 API 计算 → 回写 → 主 API 叙事。
    /// 副 API 不可用时自动退化为主 API 单通道，数值不动。
    /// </summary>
    private static async Task<AiTurnResult> RunRealBackendAsync(
        EmueraConsole console, long ticket, string userInput, CancellationToken token)
    {
        var result = new AiTurnResult { Ticket = ticket, TurnId = TurnIdOf(ticket) };

        if (MainBackendOverride == null && !AiConfig.IsReady(out string reason))
        {
            result.Success = false;
            result.ErrorMessage = reason;
            return result;
        }

        var info = new AiComputeTurnInfo { TurnId = result.TurnId };
        string systemPrompt = null;

        // 终止必须在这一层捕获，不能让异常穿到 TryBeginTurn 的兜底 catch 去：
        // 那里只会造一个空的 AiTurnResult，本轮已经写下的数值会随之丢失引用，再也无法回滚。
        try
        {
            // ---------- 阶段一：副 API 计算并回写 ----------
            await RunComputeStageAsync(console, ticket, userInput, result, info, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();

            // ---------- 阶段二：装主 API prompt（必须在回写之后，才能读到本轮新值） ----------
            systemPrompt = await InvokeOnUiThreadAsync(console, () =>
            {
                string prompt = BuildSystemPromptOnUiThread();
                SetLastComputeInfo(info);
                return prompt;
            }, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            result.Success = false;
            result.Aborted = true;
            result.ErrorMessage = "已被玩家终止";
            return result;
        }

        var messages = BuildMessages(userInput, systemPrompt, result.ComputeHint);

        // ---------- 阶段三：主 API 叙事 ----------
        string response;
        try
        {
            response = MainBackendOverride != null
                ? MainBackendOverride(messages)
                : await AiBackend.ChatAsync(messages, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            result.Success = false;
            result.Aborted = true;
            result.ErrorMessage = "已被玩家终止";
            return result;
        }
        catch (Exception e)
        {
            // RISK-05：数值已经落盘但正文没拿到。此时不能静默丢弃，也不宜自动回滚——
            // 回滚会让「已经发生的事」凭空消失。留一份待处置事务，由玩家选择重生成或回滚。
            result.Success = false;
            result.ErrorMessage = e.Message;
            if (result.ComputeApplied.Count > 0)
            {
                result.NarrativeFailedAfterApply = true;
                await InvokeOnUiThreadAsync(console, () =>
                {
                    PendingTransaction = new AiPendingTransaction
                    {
                        Ticket = ticket,
                        TurnId = result.TurnId,
                        UserInput = userInput,
                        SystemPrompt = systemPrompt,
                        ComputeHint = result.ComputeHint,
                        Applied = result.ComputeApplied,
                        FailureReason = e.Message,
                    };
                    return true;
                }, CancellationToken.None).ConfigureAwait(false);
                Append($"主 API 失败但数值已写入 ticket={ticket}，已留下待处置事务（RISK-05）");
            }
            return result;
        }

        RecordHistory(userInput, response);
        AiComputeMemory.Add(result.TurnId, userInput, AiComputeApplier.Summarize(result.ComputeApplied));

        result.Success = true;
        result.NarrativeText = response;
        result.Changes = [];
        return result;
    }

    /// <summary>
    /// 副 API 阶段。任何失败都只是「本轮不改数值」，不会让整轮请求失败——
    /// 叙事能用总比整轮报废好。失败原因记在 ComputeSkipReason 里，面板会显示。
    /// </summary>
    private static async Task RunComputeStageAsync(
        EmueraConsole console,
        long ticket,
        string userInput,
        AiTurnResult result,
        AiComputeTurnInfo info,
        CancellationToken token)
    {
        bool overridden = ComputeBackendOverride != null;
        if (!overridden && !AiConfig.IsComputeReady(out string notReady))
        {
            result.ComputeSkipReason = notReady;
            info.SkipReason = notReady;
            return;
        }

        AiComputeRequest request = await InvokeOnUiThreadAsync(console, () =>
        {
            long charaNo = ResolveCurrentCharaNo(out string charaError);
            if (charaNo < 0)
            {
                info.SkipReason = charaError;
                return null;
            }
            AiComputeRequest built = AiComputeRequestBuilder.Build(charaNo, userInput, ticket, out string buildError);
            if (built == null)
                info.SkipReason = buildError;
            return built;
        }, token).ConfigureAwait(false);

        if (request == null)
        {
            result.ComputeSkipReason = info.SkipReason ?? "副 API 请求装配失败";
            return;
        }

        info.CharaNo = request.CharaNo;
        info.StateJson = request.StateJson;
        info.FieldCount = request.Fields.Count;

        string raw;
        try
        {
            raw = overridden
                ? ComputeBackendOverride(request)
                : await AiBackend.ComputeAsync(
                    request.Messages, AiComputeDefaults.FunctionName, request.SchemaJson, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            result.ComputeSkipReason = $"副 API 调用失败：{e.Message}";
            info.SkipReason = result.ComputeSkipReason;
            Append($"副 API 调用失败 ticket={ticket}：{e.Message}");
            return;
        }

        info.RawJson = raw;

        AiComputeResult parsed = AiComputeParser.Parse(raw, request.TurnId, out string parseError);
        if (parsed == null)
        {
            result.ComputeSkipReason = $"副 API 输出无法采用：{parseError}";
            info.SkipReason = result.ComputeSkipReason;
            Append($"副 API 输出被拒绝 ticket={ticket}：{parseError}");
            return;
        }

        token.ThrowIfCancellationRequested();

        // 校验与回写必须在界面线程：要读旧值、要写变量。
        var outcome = await InvokeOnUiThreadAsync(console, () =>
        {
            bool ok = AiComputeApplier.TryApply(request, parsed, out List<AiAppliedChange> applied, out string applyError);
            return new ApplyOutcome { Ok = ok, Applied = applied, Error = applyError };
        }, token).ConfigureAwait(false);

        result.ComputeWarnings = parsed.Warnings;
        info.Warnings = parsed.Warnings;
        info.NarrativeHint = parsed.NarrativeHint;

        if (!outcome.Ok)
        {
            result.ComputeSkipReason = $"数值校验未通过，本轮不改数值：{outcome.Error}";
            info.SkipReason = result.ComputeSkipReason;
            Append($"副 API 数值被整批拒绝 ticket={ticket}：{outcome.Error}");
            return;
        }

        result.ComputeApplied = outcome.Applied;
        result.ComputeHint = parsed.NarrativeHint;
        info.Applied = outcome.Applied;
        Append($"副 API 已回写 {outcome.Applied.Count} 项 ticket={ticket}：{AiComputeApplier.Summarize(outcome.Applied)}");
    }

    private sealed class ApplyOutcome
    {
        public bool Ok;
        public List<AiAppliedChange> Applied = [];
        public string Error;
    }

    /// <summary>
    /// 「仅重生成文本」：数值保持不变，只重发主 API。必须在界面线程调用。
    /// 对应设计文档 S3.5 对 RISK-05 的处置建议。
    /// </summary>
    public static bool TryRegenerateNarrative(EmueraConsole console)
    {
        AiPendingTransaction pending = PendingTransaction;
        if (pending == null)
        {
            Append("重生成被忽略：没有待处置事务");
            return false;
        }
        if (console == null)
            return false;

        long ticket = AiRequestLock.TryAcquire(console, out CancellationToken token);
        if (ticket == 0)
        {
            Append("重生成被拒绝：已有请求进行中");
            return false;
        }

        PendingTransaction = null;
        var stopwatch = Stopwatch.StartNew();
        var messages = BuildMessages(pending.UserInput, pending.SystemPrompt, pending.ComputeHint);

        _ = Task.Run(async () =>
        {
            var result = new AiTurnResult
            {
                Ticket = ticket,
                TurnId = pending.TurnId,
                ComputeApplied = pending.Applied,
            };
            try
            {
                string response = MainBackendOverride != null
                    ? MainBackendOverride(messages)
                    : await AiBackend.ChatAsync(messages, token).ConfigureAwait(false);
                RecordHistory(pending.UserInput, response);
                AiComputeMemory.Add(pending.TurnId, pending.UserInput, AiComputeApplier.Summarize(pending.Applied));
                result.Success = true;
                result.NarrativeText = response;
            }
            catch (OperationCanceledException)
            {
                result.Success = false;
                result.Aborted = true;
                result.ErrorMessage = "已被玩家终止";
            }
            catch (Exception e)
            {
                result.Success = false;
                result.ErrorMessage = e.Message;
                result.NarrativeFailedAfterApply = pending.Applied.Count > 0;
                await InvokeOnUiThreadAsync(console, () =>
                {
                    // 仍然失败：把事务放回去，玩家可以再试或选择回滚。
                    PendingTransaction = pending;
                    pending.FailureReason = e.Message;
                    return true;
                }, CancellationToken.None).ConfigureAwait(false);
            }
            result.ElapsedMs = stopwatch.ElapsedMilliseconds;
            CompleteOnUiThread(console, result);
        });

        return true;
    }

    /// <summary>回滚待处置事务里的数值。必须在界面线程调用。</summary>
    public static bool TryRollbackPending(out string error)
    {
        error = null;
        AiPendingTransaction pending = PendingTransaction;
        if (pending == null)
        {
            error = "没有待处置事务";
            return false;
        }
        if (!AiComputeApplier.TryRollback(pending.Applied, out error))
        {
            Append($"待处置事务回滚失败：{error}");
            return false;
        }
        PendingTransaction = null;
        MarkUndone(pending.TurnId);
        Append($"待处置事务已回滚 {pending.Applied.Count} 项（{pending.TurnId}）");
        return true;
    }

    /// <summary>
    /// 放弃待处置事务，但保留已经写入的数值。必须在界面线程调用。
    ///
    /// 这是「重生成」「回滚」之外的第三条出路。既然有未处置事务时会挡住新请求，
    /// 就必须留一条「我不重生成也不认为该撤回，直接往下玩」的退出路径，
    /// 否则玩家会被自己的选择卡死在这一轮。
    /// </summary>
    public static bool TryDiscardPending(out string error)
    {
        error = null;
        AiPendingTransaction pending = PendingTransaction;
        if (pending == null)
        {
            error = "没有待处置事务";
            return false;
        }

        // 数值留着就等于「这一轮算发生过」，所以要补进短记忆，
        // 否则副 API 下一轮会看到一个没有来由的数值跳变。
        AiComputeMemory.Add(pending.TurnId, pending.UserInput, AiComputeApplier.Summarize(pending.Applied));
        PendingTransaction = null;
        Append($"待处置事务已放弃，保留已写入的 {pending.Applied.Count} 项数值（{pending.TurnId}）");
        return true;
    }

    /// <summary>
    /// 撤销上一轮副 API 写下的数值，正文与对话历史都保持原样。必须在界面线程调用。
    ///
    /// 面向的是「这轮叙事我喜欢，但结算不合理」这种诉求：叙事和数值本来就是两条通道，
    /// 没有理由强迫玩家为了改一个数字连正文一起重来。
    /// 只允许撤销一次，重复撤销会把更早的值当成"写入前"写回去。
    /// </summary>
    public static bool TryUndoLastComputeApply(out string error)
    {
        error = null;
        if (AiRequestLock.IsLocked)
        {
            error = "请求进行中，等这一轮结束再撤销";
            return false;
        }
        if (PendingTransaction != null)
        {
            error = "存在未处置的待处置事务，请先处置它（重生成 / 回滚 / 保留数值）";
            return false;
        }

        AiComputeTurnInfo info = LastComputeInfo;
        if (info == null || info.Applied.Count == 0)
        {
            error = "上一轮没有写入任何数值，无可撤销";
            return false;
        }
        if (info.Undone)
        {
            error = "上一轮的数值已经撤销过了，再撤一次会把更早的值写回去";
            return false;
        }

        if (!AiComputeApplier.TryRollback(info.Applied, out error))
        {
            Append($"撤销上一轮数值失败：{error}");
            return false;
        }

        info.Undone = true;
        AiComputeMemory.Add($"{info.TurnId}_undo", "玩家撤销了上一轮的数值结算",
            AiComputeApplier.SummarizeReverse(info.Applied));
        Append($"上一轮数值已撤销 {info.Applied.Count} 项（{info.TurnId}）");
        return true;
    }

    /// <summary>终止或收尾阶段的数值撤回。必须在界面线程调用。</summary>
    private static void RollbackIfNeeded(AiTurnResult result, string why)
    {
        if (result.ComputeApplied == null || result.ComputeApplied.Count == 0)
            return;
        if (AiComputeApplier.TryRollback(result.ComputeApplied, out string error))
        {
            result.ComputeRolledBack = true;
            MarkUndone(result.TurnId);
            Append($"{why}，已回滚 {result.ComputeApplied.Count} 项数值");
            return;
        }
        result.ErrorMessage = $"{result.ErrorMessage}（数值回滚失败：{error}）";
        Append($"{why}，但数值回滚失败：{error}");
    }

    /// <summary>
    /// 手动改过数值之后作废「撤销上轮结算」。必须在界面线程调用。
    ///
    /// 理由：撤销是拿 Before 快照直接 set 回去的。玩家手改之后，那份 Before 对应的
    /// 已经不是"上一轮写入前"的状态了，再撤一次会把手改的结果一起抹掉——
    /// 那不是玩家点「撤销」时期待的事。
    /// </summary>
    public static void InvalidateUndo()
    {
        AiComputeTurnInfo info = LastComputeInfo;
        if (info != null)
            info.Undone = true;
    }

    /// <summary>
    /// 把某一轮的观测信息标记为「数值已撤销」。
    /// 任何一条撤回路径（终止回滚、事务回滚、玩家手动撤销）都必须走这里，
    /// 否则「撤销上轮数值结算」会对同一批已经撤回的变更再撤一次，把更早的值写回去。
    /// </summary>
    private static void MarkUndone(string turnId)
    {
        AiComputeTurnInfo info = LastComputeInfo;
        if (info != null && (turnId == null || string.Equals(info.TurnId, turnId, StringComparison.Ordinal)))
            info.Undone = true;
    }

    /// <summary>
    /// 当前调教对象的角色号。TARGET 存的是登录号，这里现算现用，不缓存（RISK-21）。
    /// 必须在界面线程调用。
    /// </summary>
    private static long ResolveCurrentCharaNo(out string error)
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
        var list = GlobalStatic.VariableData.CharacterList;
        if (register >= list.Count)
        {
            error = $"TARGET={register} 超出已登录角色数 {list.Count}";
            return -1;
        }
        return list[(int)register].NO;
    }

    /// <summary>
    /// 在界面线程上执行一段读写变量的代码并把结果带回后台线程。
    /// 用 BeginInvoke + TaskCompletionSource 而不是 Invoke：Invoke 会同步阻塞后台线程，
    /// 一旦界面线程此刻也在等后台线程就死锁。
    /// </summary>
    private static Task<T> InvokeOnUiThreadAsync<T>(EmueraConsole console, Func<T> work, CancellationToken token)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var window = console?.Window;
        if (window == null || !window.Created)
        {
            tcs.SetException(new InvalidOperationException("主窗口已关闭，无法回到界面线程"));
            return tcs.Task;
        }

        void Run()
        {
            try
            {
                if (token.IsCancellationRequested)
                    tcs.TrySetCanceled(token);
                else
                    tcs.TrySetResult(work());
            }
            catch (Exception e)
            {
                tcs.TrySetException(e);
            }
        }

        try
        {
            if (window.InvokeRequired)
                window.BeginInvoke((Action)Run);
            else
                Run();
        }
        catch (Exception e)
        {
            tcs.TrySetException(e);
        }
        return tcs.Task;
    }

    private static void SetLastComputeInfo(AiComputeTurnInfo info) => LastComputeInfo = info;

    private static void RecordHistory(string userInput, string response)
    {
        lock (historyGate)
        {
            conversationHistory.Add(new ChatMessage { Role = "user", Content = userInput });
            conversationHistory.Add(new ChatMessage { Role = "assistant", Content = response });
            while (conversationHistory.Count > MaxHistoryRounds * 2)
            {
                conversationHistory.RemoveAt(0);
                conversationHistory.RemoveAt(0);
            }
        }
    }

    private static string TurnIdOf(long ticket) => $"t_{ticket:D6}";

    /// <summary>
    /// 在界面线程装配 system prompt。词条系统失败时自动退回 AiConfig.SystemPrompt，
    /// 保证词条库写坏也不会让对话完全不可用。
    /// </summary>
    private static string BuildSystemPromptOnUiThread()
    {
        if (!AiConfig.UseTraitPrompt)
        {
            LastPromptInfo = null;
            return AiConfig.SystemPrompt;
        }

        try
        {
            string prompt = AiPromptBuilder.BuildForCurrentTarget(out AiPromptBuildInfo info);
            LastPromptInfo = info;
            if (info.UsedTraits)
                Append($"词条 prompt 已装配：角色号 {info.CharaNo}，命中 {info.Traits.Count} 条，{prompt.Length} 字");
            else
                Append($"词条 prompt 未启用，退回静态 prompt：{info.FallbackReason}");
            return prompt;
        }
        catch (Exception e)
        {
            LastPromptInfo = new AiPromptBuildInfo { FallbackReason = e.Message };
            Append($"词条 prompt 装配异常，退回静态 prompt：{e.Message}");
            return AiConfig.SystemPrompt;
        }
    }

    /// <summary>
    /// 组装完整 messages 列表。顺序与 P5 上下文压缩的最终顺序一致：
    /// 词条 prompt + 数值状态 → 历史 → 本轮结算提示 → 本轮输入。
    /// 结算提示紧贴本轮输入，避免模型把它当成历史里的旧信息。
    /// </summary>
    private static List<ChatMessage> BuildMessages(string userInput, string systemPrompt, string computeHint)
    {
        var messages = new List<ChatMessage>();

        if (!string.IsNullOrWhiteSpace(systemPrompt))
            messages.Add(new ChatMessage { Role = "system", Content = systemPrompt });

        lock (historyGate)
        {
            messages.AddRange(conversationHistory);
        }

        if (!string.IsNullOrWhiteSpace(computeHint))
        {
            messages.Add(new ChatMessage
            {
                Role = "system",
                Content = $"本轮结算结果（已写入存档，请让正文与之一致，但不要写出具体数字）：{computeHint.Trim()}",
            });
        }

        messages.Add(new ChatMessage { Role = "user", Content = userInput });
        return messages;
    }

    private static string Truncate(string value, int max)
    {
        if (string.IsNullOrEmpty(value))
            return "";
        return value.Length <= max ? value : value[..max] + "\u2026";
    }
}

/// <summary>
/// 一次副 API 往返的观测信息。供面板与日志展示，不进 prompt。
/// 出问题时要能回答「传了什么、模型回了什么、为什么没写」，所以原始 JSON 也留着。
/// </summary>
internal sealed class AiComputeTurnInfo
{
    public string TurnId;
    public long CharaNo = -1;
    public int FieldCount;
    public string StateJson;
    public string RawJson;
    public string NarrativeHint;
    public List<AiAppliedChange> Applied = [];
    public List<string> Warnings = [];

    /// <summary>非空表示副 API 本轮未改动任何数值。</summary>
    public string SkipReason;

    /// <summary>已写入的数值是否被玩家撤销过。防止重复撤销把更早的值当成"写入前"写回去。</summary>
    public bool Undone;

    public bool Used => SkipReason == null;
}

/// <summary>
/// 数值已写入但叙事失败留下的事务（RISK-05）。
/// 保留写入前的值，因此两条出路都走得通：仅重生成文本，或回滚本轮数值。
/// </summary>
internal sealed class AiPendingTransaction
{
    public long Ticket;
    public string TurnId;
    public string UserInput;
    public string SystemPrompt;
    public string ComputeHint;
    public List<AiAppliedChange> Applied = [];
    public string FailureReason;
}