using MinorShift.Emuera.AI.Compute;
using MinorShift.Emuera.AI.Context;
using MinorShift.Emuera.AI.Interact;
using MinorShift.Emuera.AI.Security;
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

    /// <summary>
    /// P4：本轮通过校验、等待执行的交互指令。只在界面线程读写。
    ///
    /// 与 PendingTransaction 一样只保留一份，但两者的语义不同：事务是「已经写下去了、必须处置」，
    /// 待执行动作是「还没做、可以不做」。所以它不拦新请求——发新请求时直接作废掉即可，
    /// 因为放弃一个没执行的动作不会留下任何不一致状态。
    /// </summary>
    public static AiPendingAction PendingAction { get; private set; }

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
    public static IReadOnlyList<ChatMessage> History => AiConversation.ToChatMessages();

    /// <summary>带 id 的会话历史快照（P4）。引用与编辑都要按 id 定位。</summary>
    public static IReadOnlyList<AiMessage> Messages => AiConversation.All;

    /// <summary>
    /// 清空对话历史。副 API 的短记忆与引用栏一并清掉。
    ///
    /// 短记忆必须一起清：两边不同步会让副 API 看到主 API 已经忘掉的剧情。
    /// 引用栏也必须一起清：引用指向的消息全没了，留着只会把一段无主的旧文本
    /// 拼进下一轮输入，而模型完全无法判断那段话的来历。
    /// </summary>
    public static void ClearHistory()
    {
        AiConversation.Clear();
        AiComputeMemory.Clear();
        AiQuoteBox.Clear();
        Context.AiContextCompressor.Clear();
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

        // 上一轮摆出来没执行的动作在这里作废。放弃一个还没执行的动作不留下任何不一致状态，
        // 而留着它跨轮存在会更糟：面板上那句"触发命令：抚摸"早已不对应当前的剧情与引擎状态。
        if (PendingAction != null)
        {
            Append($"新请求开始，作废上一轮未执行的动作（{PendingAction.Description}）");
            PendingAction = null;
        }

        // 上一次被终止的记录到这里就没意义了：玩家已经用「发新请求」表达了处置意图。
        LastAbortedTurn = null;

        // P6：输入清洗。在取锁之后、组装引用之前对玩家原始输入做安全清洗。
        // 不硬拦截（ERA 是 RP 游戏，用户有权说任何话），但做标记、转义与长度限制。
        var sanitizeResult = AiInputSanitizer.Sanitize(userInput);
        userInput = sanitizeResult.CleanText;
        if (sanitizeResult.HasWarnings)
        {
            foreach (string warning in sanitizeResult.Warnings)
                Append($"[P6-输入清洗] {warning}");
        }
        // 检测可疑 prompt injection 模式（仅记日志，不拦截）
        if (AiInputSanitizer.DetectSuspiciousPatterns(userInput, out var detections))
        {
            foreach (string det in detections)
                Append($"[P6-注入检测] {det}");
        }

        // 引用在这里定型：取快照、拼进本轮输入、清空引用栏。
        // 必须在界面线程做（AiQuoteBox 只允许界面线程访问），也必须在取锁之后做——
        // 取锁失败时不该把玩家攒好的引用清掉。
        IReadOnlyList<AiQuote> quotes = AiQuoteBox.Quotes;
        string composedInput = AiQuoteBox.Compose(userInput, quotes);
        int quoteCount = quotes.Count;
        if (quoteCount > 0)
        {
            AiQuoteBox.Clear();
            Append($"本轮带上 {quoteCount} 条引用");
        }

        Append($"请求开始 ticket={ticket} input={Truncate(userInput, 40)}");

        var stopwatch = Stopwatch.StartNew();

        _ = Task.Run(async () =>
        {
            AiTurnResult result;
            try
            {
                result = UseFakeBackend
                    ? await RunFakeBackendAsync(ticket, composedInput, token).ConfigureAwait(false)
                    : await RunRealBackendAsync(console, ticket, composedInput, token).ConfigureAwait(false);
                result.QuoteCount = quoteCount;
                result.RequestInput = composedInput;
            }
            catch (OperationCanceledException)
            {
                result = new AiTurnResult
                {
                    Ticket = ticket,
                    TurnId = TurnIdOf(ticket),
                    Success = false,
                    Aborted = true,
                    ErrorMessage = "已被玩家终止",
                    RequestInput = composedInput,
                    QuoteCount = quoteCount,
                };
            }
            catch (Exception e)
            {
                result = new AiTurnResult
                {
                    Ticket = ticket,
                    TurnId = TurnIdOf(ticket),
                    Success = false,
                    ErrorMessage = e.Message,
                    RequestInput = composedInput,
                    QuoteCount = quoteCount,
                };
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
                // 待执行动作同样属于「这一轮」的产物，必须一并作废。
                // 数值都退回去了还留着一个动作，等于让玩家去推进一个已经不存在的剧情。
                DropPendingActionOnAbort(result, "请求已终止");
                // 留一份记录，让「丢弃 / 保留部分 / 重试」三条处置都有据可依（设计文档 S3.4.2）。
                // 默认处置是丢弃，所以这里什么都不写进历史——记录只是让另外两条走得通。
                RecordAbortedTurn(result);
                Append($"请求终止 ticket={result.Ticket}，未留下数值变更");
                return;
            }

            if (!result.Success)
            {
                // 正文都没拿到就没有"本轮剧情"，动作失去依据，一律作废。
                // 注意数值不在这里回滚：数值已落盘时走的是待处置事务（RISK-05），由玩家处置。
                DropPendingActionOnAbort(result, "本轮请求失败");
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

            // 自动执行必须在 Release 之后：锁定期间引擎的全部输入入口一律拒绝
            // （AiRequestLock 旁路 4），在锁内调用 PressEnterKey 会静默返回，
            // 表现为"日志说执行了但什么都没发生"。
            TryAutoExecute(console, result);

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
    /// 记下被终止的这一轮，供三条处置使用。必须在界面线程调用。
    ///
    /// 这里要顺手把已经记进历史的那一轮撤出来。存在这种竞态：主 API 已经返回、
    /// RunRealBackendAsync 已经调过 RecordHistory，玩家的终止请求才落到界面线程。
    /// 终止的语义是「这一轮不算发生过」——数值都退回去了，历史里却留着正文就自相矛盾，
    /// 而且随后「保留部分」还会把同一段正文再加一遍。正文本身不会丢，它在 PartialText 里。
    /// </summary>
    private static void RecordAbortedTurn(AiTurnResult result)
    {
        if (result.IsRevision)
            return;
        if (result.AssistantMessageId != 0 && AiConversation.TryRemoveRound(result.AssistantMessageId, out _))
        {
            // 短记忆里那条摘要也要撤掉。留着等于告诉副 API「这段剧情已经结算过」，
            // 而数值其实已经回滚，它下一轮会在一个不存在的结算基础上继续推演。
            AiComputeMemory.TryRemoveLast(result.TurnId);
            Append($"终止时撤出已记入历史的这一轮 id={result.AssistantMessageId}（正文保留在终止记录里）");
        }
        LastAbortedTurn = new AiAbortedTurn
        {
            Ticket = result.Ticket,
            TurnId = result.TurnId,
            UserInput = result.RequestInput,
            // 非流式下这里通常为空。只有"正文已收到但终止请求先落地"的竞态里才有内容，
            // 那时正文其实是完整的，因此「保留部分」并非无用的出路。
            PartialText = result.NarrativeText,
        };
    }

    /// <summary>作废本轮的待执行动作。必须在界面线程调用。</summary>
    private static void DropPendingActionOnAbort(AiTurnResult result, string why)
    {
        if (PendingAction == null || PendingAction.Ticket != result.Ticket)
            return;
        Append($"{why}，作废本轮待执行动作（{PendingAction.Description}）");
        PendingAction = null;
        result.PendingAction = null;
        result.ActionSkipReason = string.IsNullOrEmpty(result.ActionSkipReason)
            ? $"{why}，本轮动作已作废"
            : $"{result.ActionSkipReason}；{why}，本轮动作已作废";
    }

    /// <summary>
    /// auto_execute = true 时立即执行本轮动作。必须在界面线程、且锁已释放之后调用。
    ///
    /// 默认关着（AiInteractTemplate.AutoExecute 默认 false）是有意的：数值写错能撤销，
    /// 流程被推进无法撤销——ERA 没有流程级回退。愿意让 AI 自己开车的人再去打开它。
    /// </summary>
    private static void TryAutoExecute(EmueraConsole console, AiTurnResult result)
    {
        AiPendingAction action = result.PendingAction;
        if (action == null || action.Consumed || PendingAction != action)
            return;
        AiInteractTemplate interact = AiTraitLibrary.InteractTemplate;
        if (interact == null || !interact.Enabled || !interact.AutoExecute)
            return;

        if (!AiActionExecutor.TryExecute(console, action, out string error))
        {
            result.ActionSkipReason = string.IsNullOrEmpty(result.ActionSkipReason)
                ? $"自动执行失败：{error}"
                : $"{result.ActionSkipReason}；自动执行失败：{error}";
            Append($"自动执行交互指令失败 ticket={result.Ticket}：{error}");
            // 失败的动作留在 PendingAction 里，玩家可以修好状态后手动点「执行动作」。
            // 但 Consumed 已被 TryExecute 置位的情况例外——那说明它已经喂进引擎了。
            if (action.Consumed)
                PendingAction = null;
            return;
        }

        result.ActionAutoExecuted = true;
        PendingAction = null;
        Append($"已自动执行交互指令 ticket={result.Ticket}：{action.Description}");
    }

    /// <summary>
    /// 玩家点「执行动作」。必须在界面线程调用。
    ///
    /// 执行前重新过一遍引擎状态层：从本轮收尾到玩家点下去可能过了很久，
    /// 引擎早就换了状态。校验时能做的事，现在未必还能做。
    /// </summary>
    public static bool TryExecutePendingAction(EmueraConsole console, out string error)
    {
        error = null;
        AiPendingAction action = PendingAction;
        if (action == null)
        {
            error = "没有待执行的动作";
            return false;
        }
        if (action.Consumed)
        {
            error = "这个动作已经执行过了";
            PendingAction = null;
            return false;
        }
        if (!AiActionExecutor.TryExecute(console, action, out error))
        {
            Append($"执行交互指令失败：{error}");
            if (action.Consumed)
                PendingAction = null;
            return false;
        }
        PendingAction = null;
        Append($"已执行交互指令：{action.Description}");
        return true;
    }

    /// <summary>玩家放弃待执行动作。必须在界面线程调用。</summary>
    public static bool TryDiscardPendingAction(out string error)
    {
        error = null;
        AiPendingAction action = PendingAction;
        if (action == null)
        {
            error = "没有待执行的动作";
            return false;
        }
        PendingAction = null;
        Append($"玩家放弃了待执行动作：{action.Description}");
        return true;
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

        // ---------- P5：上下文压缩检测 ----------
        // 在装配 messages 之前检测是否需要压缩。压缩成功后再装配，这样 BuildMessages 读到的
        // 会话历史已经是压缩后的，不会超出窗口。压缩失败不阻断流程——退化为 P4 的硬截断行为。
        bool contextEnabled = AiTraitLibrary.ContextTemplate?.Enabled ?? true;
        if (contextEnabled && Context.AiContextCompressor.NeedsCompression(systemPrompt, userInput))
        {
            Append($"上下文压缩触发 ticket={ticket}");
            try
            {
                var compressResult = await Context.AiContextCompressor.CompressAsync(
                    action => InvokeOnUiThreadAsync(console, () => { action(); return true; }, token),
                    token).ConfigureAwait(false);
                if (compressResult.Success)
                    Append($"上下文压缩完成：{compressResult.CompressedRounds} 轮 → {compressResult.SummaryChars} 字摘要");
                else if (!string.IsNullOrEmpty(compressResult.SkipReason))
                    Append($"上下文压缩跳过：{compressResult.SkipReason}");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                Append($"上下文压缩失败（不影响本轮请求）：{e.Message}");
            }
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
            var aiError = AiErrorReporter.Classify(e, "主API叙事");
            Append(AiErrorReporter.FormatDiagnostic(aiError, e));
            result.Success = false;
            result.ErrorMessage = $"{aiError.UserMessage} {aiError.Suggestion}".Trim();
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

        result.AssistantMessageId = RecordHistory(result.TurnId, userInput, response);
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
            // engineWaitingInput 必须在界面线程现读：它决定本轮要不要把交互 schema 下发给模型。
            // 引擎没在等输入时下发，只会诱导模型编一个必定在执行阶段被拒的动作。
            AiComputeRequest built = AiComputeRequestBuilder.Build(
                charaNo, userInput, ticket, console.IsWaitInputState, out string buildError);
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
        // 交互指令的契约层与引擎状态层校验一并放在这一跳里——它同样要读引擎状态，
        // 而且合在一次往返里可以保证「数值与动作出自同一时刻的引擎状态」。
        var outcome = await InvokeOnUiThreadAsync(console, () =>
        {
            bool ok = AiComputeApplier.TryApply(request, parsed, out List<AiAppliedChange> applied, out string applyError);
            var made = new ApplyOutcome { Ok = ok, Applied = applied, Error = applyError };
            ResolveInteractOnUiThread(console, request, parsed, ticket, made);
            return made;
        }, token).ConfigureAwait(false);

        result.ComputeWarnings = parsed.Warnings;
        info.Warnings = parsed.Warnings;
        info.NarrativeHint = parsed.NarrativeHint;

        // 交互产物与数值互不牵连：数值被整批拒绝时选项和动作照样成立，反之亦然。
        result.Options = outcome.Options;
        result.OptionNote = outcome.OptionNote;
        result.PendingAction = outcome.Action;
        result.ActionSkipReason = outcome.ActionSkipReason;
        info.Options = outcome.Options;
        info.ActionDescription = outcome.Action?.Description;
        info.ActionSkipReason = outcome.ActionSkipReason;

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

        /// <summary>P4：清洗后的选项。</summary>
        public List<AiOption> Options = [];

        public string OptionNote;

        /// <summary>P4：通过契约层与引擎状态层校验的待执行动作。</summary>
        public AiPendingAction Action;

        public string ActionSkipReason;
    }

    /// <summary>
    /// 交互产物的落地。必须在界面线程调用（要读引擎状态、要设 PendingAction）。
    ///
    /// 顺序是刻意的：先清洗选项（纯本地、不会失败），再校验动作。
    /// 动作要过契约层 + 引擎状态层两道，且**此处一律不执行**——
    /// 执行入口在锁定期间会拒绝一切输入（AiRequestLock 旁路 4），
    /// 所以自动执行也必须推迟到锁释放之后（见 ApplyOnUiThread）。
    /// </summary>
    private static void ResolveInteractOnUiThread(
        EmueraConsole console, AiComputeRequest request, AiComputeResult parsed, long ticket, ApplyOutcome outcome)
    {
        AiInteractTemplate interact = request.Interact;

        // 本轮没把交互 schema 下发给模型，却收到了交互内容：说明模型在自作主张。
        // 一律不采纳，并把这件事说出来——静默丢弃会让人以为"模型从不提交互建议"。
        if (!request.InteractEnabled)
        {
            if (parsed.Options.Count > 0 || parsed.Action != null)
            {
                outcome.ActionSkipReason = "本轮未开放交互指令（interact 段未启用或引擎不在等待输入），模型给出的交互内容已忽略";
                Append($"忽略本轮交互内容 ticket={ticket}：未开放交互指令");
            }
            return;
        }

        outcome.Options = AiActionExecutor.Sanitize(interact, parsed.Options, out string optionNote);
        outcome.OptionNote = optionNote;

        var reasons = new List<string>();
        if (!string.IsNullOrEmpty(parsed.InteractNote))
            reasons.Add(parsed.InteractNote);

        if (parsed.Action != null)
        {
            if (!AiActionExecutor.TryValidate(interact, parsed.Action, request.TurnId, ticket,
                    out AiPendingAction pending, out string validateError))
            {
                reasons.Add(validateError);
                Append($"交互指令未通过契约层 ticket={ticket}：{validateError}");
            }
            else if (pending != null)
            {
                // 引擎状态层。这里就检查而不是等到执行时，是为了让面板能立刻说明
                // 「模型提了动作但现在做不了」，而不是摆一个点下去必然失败的按钮。
                if (!AiActionExecutor.IsEngineReady(console, pending, out string engineError))
                {
                    reasons.Add(engineError);
                    Append($"交互指令未通过引擎状态层 ticket={ticket}：{engineError}");
                }
                else
                {
                    outcome.Action = pending;
                    PendingAction = pending;
                    Append($"交互指令待执行 ticket={ticket}：{pending.Description}");
                }
            }
        }

        if (reasons.Count > 0)
            outcome.ActionSkipReason = string.Join("；", reasons);
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
                result.AssistantMessageId = RecordHistory(pending.TurnId, pending.UserInput, response);
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

    /// <summary>
    /// 被终止的那一轮。终止后的三条处置（丢弃 / 保留部分 / 重试）都要用它。
    /// 只在界面线程读写。与 PendingTransaction 不同，它**不拦新请求**——
    /// 默认处置就是"丢弃"，不处置也不会留下任何不一致状态。
    /// </summary>
    public static AiAbortedTurn LastAbortedTurn { get; private set; }

    /// <summary>
    /// 终止处置·保留部分：把已接收到的不完整正文写进历史，标注为「被中断」。
    /// 必须在界面线程调用。
    ///
    /// 非流式下这一条只在竞态里有用：正文已经收到、但玩家的终止请求先落地。
    /// 那时正文其实是完整的，丢掉它反而更可惜，所以保留这条出路。
    /// 标注 Interrupted 是为了让上下文里能看出"这一段是被打断的"，
    /// 否则模型会把一段可能截断的文字当成完整叙事往下接。
    /// </summary>
    public static bool TryKeepAbortedPartial(out string error)
    {
        error = null;
        AiAbortedTurn aborted = LastAbortedTurn;
        if (aborted == null)
        {
            error = "没有被终止的回合";
            return false;
        }
        if (string.IsNullOrWhiteSpace(aborted.PartialText))
        {
            error = "这一轮终止时还没有收到任何正文，没有可保留的内容";
            return false;
        }
        if (aborted.Handled)
        {
            error = "这一轮已经处置过了";
            return false;
        }

        (AiMessage _, AiMessage assistant) = AiConversation.AddRound(
            aborted.TurnId, aborted.UserInput, aborted.PartialText);
        assistant.Interrupted = true;
        aborted.Handled = true;
        // 数值在终止时已经回滚，所以短记忆里要写明"这一轮被打断且数值未变"，
        // 否则副 API 会以为这段剧情已经结算过。
        AiComputeMemory.Add(aborted.TurnId, aborted.UserInput, "这一轮被玩家中断，正文不完整，数值未变");
        Append($"保留被终止的正文（{aborted.PartialText.Length} 字，已标注为被中断）");
        return true;
    }

    /// <summary>
    /// 终止处置·重试：以完全相同的输入重新发一轮。必须在界面线程调用。
    ///
    /// 复用原轮次的输入（含引用前缀）而不是让玩家重敲：引用栏在发出时就已清空，
    /// 重敲的内容与被终止的那一轮不是同一个请求。
    /// 副 API 会重新跑一遍——它的数值在终止时已经回滚，所以这不是重复结算。
    /// </summary>
    public static bool TryRetryAbortedTurn(EmueraConsole console, out string error)
    {
        error = null;
        AiAbortedTurn aborted = LastAbortedTurn;
        if (aborted == null)
        {
            error = "没有被终止的回合";
            return false;
        }
        if (aborted.Handled)
        {
            error = "这一轮已经处置过了";
            return false;
        }
        if (string.IsNullOrWhiteSpace(aborted.UserInput))
        {
            error = "被终止的那一轮没有留下输入，无法重试";
            return false;
        }
        if (!TryBeginTurn(console, aborted.UserInput))
        {
            error = "无法重试（已有请求进行中或存在未处置事务）";
            return false;
        }
        aborted.Handled = true;
        Append($"重试被终止的一轮：{Truncate(aborted.UserInput, 40)}");
        return true;
    }

    /// <summary>终止处置·丢弃（默认）。只是把记录清掉，本来就没写进任何地方。</summary>
    public static bool TryDiscardAbortedTurn(out string error)
    {
        error = null;
        if (LastAbortedTurn == null)
        {
            error = "没有被终止的回合";
            return false;
        }
        LastAbortedTurn = null;
        Append("已丢弃被终止的那一轮，会话历史未变");
        return true;
    }

    /// <summary>
    /// 修改回复·模式 A：直接编辑某条 AI 回复的正文。必须在界面线程调用。
    ///
    /// **纯本地操作，不发网络请求，也绝不回滚已写入的数值。** 数值与正文是两条通道：
    /// 玩家嫌这段文字写得不好而重写它，不该让存档跟着变；要撤数值请走「撤销上轮数值结算」。
    /// 因为不发请求，所以锁定期间也允许（设计文档 S3.4.1：编辑历史 → IDLE）。
    /// </summary>
    public static bool TryEditResponse(long messageId, string newText, out string error)
    {
        if (!AiConversation.TryEditAssistant(messageId, newText, out error))
        {
            Append($"编辑回复失败：{error}");
            return false;
        }
        Append($"已编辑回复 id={messageId}（{newText.Length} 字），数值未受影响");
        return true;
    }

    /// <summary>
    /// 修改回复·模式 B：带修改指令重新生成最后一条 AI 回复。必须在界面线程调用。
    ///
    /// 与「重生成」（TryRegenerateNarrative）的区别：那一条是 RISK-05 的补偿路径，
    /// 只在数值已写入但正文失败时可用；这一条面向"正文拿到了但玩家不满意"，
    /// 任何一轮之后都能用，且**同样不动已写入的数值**——重写的是叙述，不是已经发生的事。
    ///
    /// 实现上不新增一轮，而是就地替换那条 assistant 消息。否则历史里会留下
    /// 一段"被否决的回复 + 一段修改要求"，模型下一轮会把那次否决也当成剧情的一部分。
    /// </summary>
    public static bool TryReviseLastResponse(EmueraConsole console, string instruction, out string error)
    {
        error = null;
        if (console == null)
        {
            error = "引擎尚未就绪";
            return false;
        }
        if (string.IsNullOrWhiteSpace(instruction))
        {
            error = "请先写下你希望怎么改";
            return false;
        }
        if (PendingTransaction != null)
        {
            error = "存在未处置的待处置事务，请先处置它（重生成 / 回滚数值 / 保留数值）";
            return false;
        }

        AiMessage assistant = AiConversation.LastAssistant();
        if (assistant == null)
        {
            error = "还没有可修改的 AI 回复";
            return false;
        }
        AiMessage user = AiConversation.UserOf(assistant);

        long ticket = AiRequestLock.TryAcquire(console, out CancellationToken token);
        if (ticket == 0)
        {
            error = "已有请求进行中";
            return false;
        }

        // 历史里保留旧回复，让模型看得到"要改的是哪一段"；修改指令作为独立 system 消息
        // 排在最后，明确要求只重写那一条，否则模型会把它当成新的剧情推进继续往下写。
        var messages = new List<ChatMessage>();
        string systemPrompt = BuildSystemPromptOnUiThread();
        if (!string.IsNullOrWhiteSpace(systemPrompt))
            messages.Add(new ChatMessage { Role = "system", Content = systemPrompt });
        messages.AddRange(AiConversation.ToChatMessages());
        messages.Add(new ChatMessage
        {
            Role = "system",
            Content = "玩家对你上面最后一条回复不满意，要求重写它。"
                + "请只重写那一条回复本身，不要继续推进剧情、不要复述玩家的要求、不要解释你改了什么。"
                + "本轮已经结算的数值不会变，所以不要在重写时改变既定事实，只改叙述。"
                + $"玩家的修改要求：{instruction.Trim()}",
        });

        long messageId = assistant.Id;
        string oldText = assistant.Text;
        var stopwatch = Stopwatch.StartNew();
        Append($"带修改指令重生成 ticket={ticket} id={messageId} 指令={Truncate(instruction, 40)}");

        _ = Task.Run(async () =>
        {
            var result = new AiTurnResult
            {
                Ticket = ticket,
                TurnId = assistant.TurnId,
                IsRevision = true,
                AssistantMessageId = messageId,
            };
            try
            {
                string response = MainBackendOverride != null
                    ? MainBackendOverride(messages)
                    : await AiBackend.ChatAsync(messages, token).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(response))
                    throw new InvalidOperationException("模型返回空正文，已保留原回复");
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
            }
            result.ElapsedMs = stopwatch.ElapsedMilliseconds;
            CompleteRevisionOnUiThread(console, result, messageId, oldText, user?.Text);
        });

        return true;
    }

    /// <summary>
    /// 修改回复的收尾。单独一条路径而不是复用 ApplyOnUiThread，因为语义不同：
    /// 这里不写数值、不新增轮次、失败时必须原样保留旧回复。
    /// </summary>
    private static void CompleteRevisionOnUiThread(
        EmueraConsole console, AiTurnResult result, long messageId, string oldText, string userText)
    {
        var window = console.Window;
        if (window == null || !window.Created)
        {
            AiRequestLock.Release(result.Ticket);
            return;
        }

        void Finish()
        {
            try
            {
                if (result.Ticket != AiRequestLock.CurrentTicket)
                {
                    Append($"丢弃过期的修改结果 ticket={result.Ticket}");
                    return;
                }
                if (result.Aborted || AiRequestLock.IsAborting)
                {
                    result.Success = false;
                    result.Aborted = true;
                    Append($"修改回复被终止 ticket={result.Ticket}，原回复保持不变");
                    return;
                }
                if (!result.Success)
                {
                    Append($"修改回复失败 ticket={result.Ticket}：{result.ErrorMessage}，原回复保持不变");
                    return;
                }
                if (!AiConversation.TryReplaceAssistant(messageId, result.NarrativeText))
                {
                    // 消息在等待期间被淘汰或清空了。此时不新增一条，因为那会凭空多出
                    // 一段没有对应玩家输入的 assistant 消息。
                    result.Success = false;
                    result.ErrorMessage = "原回复已不在对话历史里（可能已被清空），本次修改未采用";
                    Append($"修改回复无法落地 ticket={result.Ticket}：{result.ErrorMessage}");
                    return;
                }
                // 短记忆里补一条：副 API 下一轮看到的剧情摘要必须跟着改，
                // 否则它会按被否决的那版叙事继续结算。
                AiComputeMemory.Add($"{result.TurnId}_rev", userText ?? "（玩家要求重写上一条回复）",
                    "玩家要求重写了上一条叙事，数值未变");
                Append($"修改回复完成 ticket={result.Ticket} id={messageId}：{oldText?.Length ?? 0} 字 → {result.NarrativeText.Length} 字");
            }
            catch (Exception e)
            {
                result.Success = false;
                result.ErrorMessage = e.Message;
                Append($"修改回复收尾异常 ticket={result.Ticket}：{e.Message}");
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

        try
        {
            if (window.InvokeRequired)
                window.BeginInvoke((Action)Finish);
            else
                Finish();
        }
        catch (Exception e)
        {
            Append($"修改回复回注调度失败：{e.Message}");
            AiRequestLock.Release(result.Ticket);
        }
    }

    /// <summary>
    /// 丢弃最后一轮（user + assistant 成对移除）。必须在界面线程调用。
    ///
    /// 成对移除是必须的：只删 assistant 会留下一个没有回应的 user 消息，
    /// 模型下一轮会把它读成"玩家说了话但我没理"，从而自行圆场。
    /// 同样**不动已写入的数值**——要撤数值请走「撤销上轮数值结算」，两件事分开做。
    /// </summary>
    public static bool TryDropLastRound(out string error)
    {
        error = null;
        if (AiRequestLock.IsLocked)
        {
            error = "请求进行中，等这一轮结束再丢弃";
            return false;
        }
        AiMessage assistant = AiConversation.LastAssistant();
        if (assistant == null)
        {
            error = "没有可丢弃的回合";
            return false;
        }
        if (!AiConversation.TryRemoveRound(assistant.Id, out error))
            return false;
        Append($"已丢弃最后一轮对话（id={assistant.Id}），已写入的数值未受影响");
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

    /// <summary>记一轮往返，返回 assistant 消息的 id（供引用与编辑定位）。</summary>
    private static long RecordHistory(string turnId, string userInput, string response)
    {
        (AiMessage _, AiMessage assistant) = AiConversation.AddRound(turnId, userInput, response);
        return assistant.Id;
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

        // 装配顺序（设计文档）：词条 prompt + 数值状态 → 历史摘要 → 最近 M 轮原文 → 本轮输入
        if (!string.IsNullOrWhiteSpace(systemPrompt))
            messages.Add(new ChatMessage { Role = "system", Content = systemPrompt });

        // P5：历史摘要段。存在时插在历史原文之前，为模型提供早期剧情的压缩记忆。
        if (Context.AiContextCompressor.HasSummary)
        {
            messages.Add(new ChatMessage
            {
                Role = "system",
                Content = $"【早期剧情摘要】{Context.AiContextCompressor.Summary.Trim()}",
            });
        }

        messages.AddRange(AiConversation.ToChatMessages());

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

    /// <summary>P4：本轮采纳的选项。</summary>
    public List<AiOption> Options = [];

    /// <summary>P4：本轮待执行动作的描述。为空表示没有动作。</summary>
    public string ActionDescription;

    /// <summary>P4：交互内容被丢弃的原因。</summary>
    public string ActionSkipReason;

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

/// <summary>
/// 被终止的一轮（P4，设计文档 S3.4.2）。三条处置都从这里取原始材料。
///
/// 与 AiPendingTransaction 的区别是「是否留下了必须处置的状态」：
/// 事务的数值已经落盘，不处置就会失去可撤回性，所以它硬拦新请求；
/// 终止记录什么都没写下（数值已在终止时回滚），默认处置就是丢弃，因此不拦任何操作。
/// </summary>
internal sealed class AiAbortedTurn
{
    public long Ticket;
    public string TurnId;

    /// <summary>被终止那一轮实际送出的输入（含引用前缀）。重试时原样重发。</summary>
    public string UserInput;

    /// <summary>终止时已收到的正文。非流式下通常为空。</summary>
    public string PartialText;

    /// <summary>已经选过一条处置。防止「保留部分」之后又「重试」，把同一轮算两次。</summary>
    public bool Handled;
}