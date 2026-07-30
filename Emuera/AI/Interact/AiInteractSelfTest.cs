using MinorShift.Emuera.AI.Compute;
using MinorShift.Emuera.AI.Traits;
using MinorShift.Emuera.GameView;
using MinorShift.Emuera.Runtime;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace MinorShift.Emuera.AI.Interact;

/// <summary>
/// P4 交互控制自动自检。
///
/// 由环境变量 ERA_AI_INTERACT_SELFTEST=1 触发，全程在界面线程上跑，结束后把报告写到
/// ERA_AI_INTERACT_SELFTEST_REPORT 指定的文件（默认 exe 目录下 ai_interact_selftest.txt）并关窗。
/// 不设环境变量时该类完全不激活，因此不影响玩家。
///
/// 验收目标（对应设计文档 S3.4 与 P4）：
///   - 会话模型带稳定 id，编辑与丢弃都按 id 定位，压缩后不会错位
///   - 引用自带文本快照：原消息被编辑或被淘汰之后，引用内容仍然是建立引用那一刻的样子
///   - 修改回复只动上下文文本，绝不回滚已写入的数值
///   - 丢弃一轮时 user + assistant 成对移除，不留孤立的玩家输入
///   - 交互指令三层校验各自生效：契约层 / 引擎状态层 / 执行层
///   - 模型只能触发声明过的命令；自由注入必须显式开放且声明范围
///   - auto_execute 默认关闭；开启后确实会立刻推进引擎
///   - 交互 schema 只在「写了 interact 段 + 启用 + 引擎在等输入」三条件同时成立时下发
///   - 交互内容解析失败绝不牵连 changes
///   - 终止后的三条处置（丢弃 / 保留部分 / 重试）都走得通
///
/// 自检会临时替换 exe 同目录的 ai_traits.json、改动 harness 的角色数值、并真的往引擎喂输入，
/// 结束时在 RestoreAll 中还原词条库与数值。
/// </summary>
internal static partial class AiInteractSelfTest
{
    private const string EnableEnv = "ERA_AI_INTERACT_SELFTEST";
    private const string ReportEnv = "ERA_AI_INTERACT_SELFTEST_REPORT";

    /// <summary>自检用角色号，对应 harness 的 CHARA1.CSV / CHARA2.CSV。</summary>
    private const long CharaA = 1;
    private const long CharaB = 2;

    /// <summary>harness 的 SYSTEM.ERB 约定：喂入这个值会切到 TONEINPUT（单字符等待）。</summary>
    private const string SwitchToOnePhrase = "777";

    private static readonly List<string> lines = [];
    private static int passed;
    private static int failed;

    private static System.Windows.Forms.Timer pollTimer;
    private static System.Windows.Forms.Timer abortTimer;

    /// <summary>
    /// 看门狗。H 组的异步链路靠 TurnCompleted 事件推进，任何一环没触发事件就会永久停住，
    /// 窗口不关、报告不落盘，只能靠外部杀进程——那时什么线索都没有。
    /// 有了它，卡住也会产出一份写到卡住那一步为止的报告。
    /// </summary>
    private static System.Windows.Forms.Timer watchdogTimer;
    private static int watchdogMs;
    private const int WatchdogLimitMs = 45000;
    private static EmueraConsole target;
    private static int elapsedMs;
    private static bool finished;

    private static string libraryPath;
    private static string libraryBackup;

    /// <summary>开跑前的原始数值，收尾时原样写回，避免自检把 harness 的存档改花。</summary>
    private static readonly List<AiValueChange> restoreBatch = [];

    /// <summary>最近一次带交互 schema 的副 API 请求，写进报告便于人工核对实际发出的内容。</summary>
    private static string requestSample;

    public static bool IsEnabled =>
        string.Equals(Environment.GetEnvironmentVariable(EnableEnv), "1", StringComparison.Ordinal);

    /// <summary>挂上轮询计时器，等脚本进入输入等待、且表达式求值器就绪后再开跑。</summary>
    public static void Arm(EmueraConsole console)
    {
        if (!IsEnabled || console == null)
            return;
        target = console;
        pollTimer = new System.Windows.Forms.Timer { Interval = 200 };
        pollTimer.Tick += Poll;
        pollTimer.Start();
    }

    private static void Poll(object sender, EventArgs e)
    {
        elapsedMs += 200;
        if (finished)
            return;

        bool ready = GlobalStatic.EMediator != null
                  && GlobalStatic.VEvaluator != null
                  && target.IsWaitInputState;
        if (!ready)
        {
            if (elapsedMs < 30000)
                return;
            Log("FATAL", "等待脚本进入输入等待状态超时，交互控制自检无法进行。");
            Finish();
            return;
        }

        pollTimer.Stop();
        StartWatchdog();
        RunSyncGroups();
    }

    private static void StartWatchdog()
    {
        watchdogTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        watchdogTimer.Tick += (s, e) =>
        {
            watchdogMs += 1000;
            if (finished || watchdogMs < WatchdogLimitMs)
                return;
            watchdogTimer.Stop();
            Log("FATAL", $"自检超过 {WatchdogLimitMs / 1000} 秒未结束，卡在串联阶段 chainStage={chainStage}，已强制收尾。");
            AiDispatcher.TurnCompleted -= OnChainCompleted;
            RestoreAll();
            Finish();
        };
        watchdogTimer.Start();
    }

    /// <summary>
    /// 同步组。跑完后进入 H 组的异步链路，异步链路由 TurnCompleted 事件驱动。
    /// 任何一步抛异常都要走到 Finish，否则窗口会一直挂着不退出。
    /// </summary>
    private static void RunSyncGroups()
    {
        try
        {
            Section("前置：自检环境");
            if (!CheckHarness())
            {
                RestoreAll();
                Finish();
                return;
            }

            libraryPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, AiTraitLibrary.FileName);
            libraryBackup = File.Exists(libraryPath) ? File.ReadAllText(libraryPath, Encoding.UTF8) : null;

            Section("A 组：会话模型（带 id 的消息）");
            TestConversationAssignsStableIds();
            TestConversationLocatesByIdNotIndex();
            TestConversationEditOnlyTouchesText();
            TestConversationRemovesRoundInPairs();
            TestConversationTrimsToMaxRounds();

            Section("B 组：引用（快照优先于 id）");
            TestQuoteKeepsSnapshotAgainstLaterEdit();
            TestQuoteSurvivesMessageEviction();
            TestQuoteRejectsDuplicateEmptyAndOverflow();
            TestQuoteComposePutsQuotesFirst();

            Section("C 组：interact 段静态校验");
            TestInteractStaticValidationCatchesMistakes();
            TestMissingAndDisabledInteractSection();

            Section("D 组：请求装配与交互 schema");
            Install(InteractLibraryJson, "交互测试库");
            TestSchemaOnlyWhenEngineWaiting();
            TestSchemaEnumeratesOnlyDeclaredCommands();
            TestSchemaOmitsInjectionFieldsWhenClosed();
            TestSchemaIncludesInjectionFieldsWhenDeclared();
            TestInteractPromptFragmentComesAfterComputeInstruction();

            Section("E 组：交互内容解析（坏的不牵连 changes）");
            TestParserReadsOptionsAndAction();
            TestParserToleratesBareStringOptions();
            TestParserKeepsChangesWhenInteractIsGarbage();
            TestParserRejectsUnknownActionKind();

            Section("F 组：契约层校验与选项清洗");
            Install(InteractLibraryJson, "交互测试库");
            TestValidateRejectsUndeclaredCommand();
            TestValidateRejectsInjectionWhenClosed();
            TestValidateRejectsInjectionOutOfRange();
            TestValidateAcceptsDeclaredInjection();
            TestValidateFlattensNewlineInInjectedText();
            TestSanitizeTrimsAndDropsOptions();

            Section("G 组：引擎状态层与执行层");
            Install(InteractLibraryJson, "交互测试库");
            TestEngineReadyRejectsTypeMismatch();
            TestExecuteReallyDrivesEngine();
            TestOnePhraseTruncationIsRejected();
            TestExecuteRejectedWhileLocked();
            TestExecuteRejectsSecondUse();

            Section("H 组：串联（替身后端）");
            Install(InteractLibraryJson, "交互测试库");
            StartInteractChain();
            return;
        }
        catch (Exception ex)
        {
            Log("FATAL", $"自检自身抛出异常：{ex}");
        }

        RestoreAll();
        Finish();
    }

    // ---------- 前置 ----------

    private static bool CheckHarness()
    {
        bool okA = GlobalStatic.VEvaluator.GetChara(CharaA) >= 0;
        bool okB = GlobalStatic.VEvaluator.GetChara(CharaB) >= 0;
        Check($"角色号 {CharaA} 已登录", okA);
        Check($"角色号 {CharaB} 已登录", okB);
        if (!okA || !okB)
        {
            Log("FATAL", "自检需要 harness 的 CHARA1/CHARA2，且 SYSTEM.ERB 已 ADDCHARA。");
            return false;
        }

        bool favorOk = AiVariableAccess.TryReadInt(Favor(CharaA), out long favor, out string favorError);
        Check($"角色 A 好感度为 50（实际 {(favorOk ? favor.ToString(CultureInfo.InvariantCulture) : Brief(favorError))}）",
            favorOk && favor == 50);
        if (!favorOk)
        {
            Log("FATAL", "harness 的 CFLAG.CSV 必须定义『好感度』。");
            return false;
        }

        // 引擎必须停在整数输入等待上。G 组要真的把载荷喂进去，
        // 状态不对的话后面所有执行层断言都会在错误前提下"侥幸通过"。
        Check("引擎处于输入等待状态", target.IsWaitInputState);
        Check($"引擎在等整数输入（实际 {target.NowInputType}）", target.NowInputType == InputType.IntValue);
        Check("引擎不是单字符等待（TINPUT 而非 TONEINPUT）", !target.IsWaintingOnePhrase);
        if (!target.IsWaitInputState || target.NowInputType != InputType.IntValue)
        {
            Log("FATAL", "harness 的 SYSTEM.ERB 必须停在 TINPUT 上，交互执行层无法验证。");
            return false;
        }

        restoreBatch.Clear();
        restoreBatch.Add(new AiValueChange { Target = Favor(CharaA), Op = "set", Value = favor });
        AiVariableAccess.TryReadInt(Stamina(CharaA), out long stamina, out _);
        restoreBatch.Add(new AiValueChange { Target = Stamina(CharaA), Op = "set", Value = stamina });
        return true;
    }

    // ---------- 词条库装载 ----------

    private static void Install(string json, string what)
    {
        File.WriteAllText(libraryPath, json, new UTF8Encoding(false));
        bool ok = AiTraitLibrary.Reload(out string summary);
        Check($"装载{what}成功（{Brief(summary)}）", ok);
    }

    private static void RestoreAll()
    {
        AiDispatcher.ComputeBackendOverride = null;
        AiDispatcher.MainBackendOverride = null;
        AiDispatcher.UseFakeBackend = true;
        AiComputeMemory.Clear();
        AiConversation.Clear();
        AiQuoteBox.Clear();
        AiDispatcher.TryDiscardPendingAction(out _);
        AiDispatcher.TryDiscardAbortedTurn(out _);

        if (restoreBatch.Count > 0 && !AiVariableAccess.TryApplyAll(restoreBatch, out string restoreError))
            Log("WARN", $"数值还原失败，请手工检查 harness：{restoreError}");

        if (libraryPath == null)
            return;
        try
        {
            if (libraryBackup != null)
                File.WriteAllText(libraryPath, libraryBackup, new UTF8Encoding(false));
            else if (File.Exists(libraryPath))
                File.Delete(libraryPath);
            AiTraitLibrary.Reload(out string summary);
            Log("INFO", $"已还原原词条库：{Brief(summary)}");
        }
        catch (Exception e)
        {
            Log("WARN", $"还原词条库失败，请手工检查 {libraryPath}：{e.Message}");
        }
    }
    // ---------- A 组：会话模型 ----------

    private static void TestConversationAssignsStableIds()
    {
        AiConversation.Clear();
        (AiMessage u1, AiMessage a1) = AiConversation.AddRound("t_000001", "玩家输入一", "回复一");
        (AiMessage u2, AiMessage a2) = AiConversation.AddRound("t_000002", "玩家输入二", "回复二");

        Check($"一轮记两条消息（实际 {AiConversation.Count} 条）", AiConversation.Count == 4);
        Check("id 单调递增且互不相同", u1.Id < a1.Id && a1.Id < u2.Id && u2.Id < a2.Id);
        Check("角色标注正确（user / assistant）",
            !u1.IsAssistant && a1.IsAssistant && !u2.IsAssistant && a2.IsAssistant);
        Check("turn_id 原样回填，便于与副 API 的轮次对上", a1.TurnId == "t_000001" && a2.TurnId == "t_000002");
        Check("新消息默认既未被编辑也未被中断", !a1.Edited && !a1.Interrupted);

        // 清空之后 id 不复用。复用会让「引用一条已被清掉的消息」意外命中新消息，
        // 那是最难查的一类错：界面显示的引用来源看着合理，内容却是另一段剧情。
        long lastId = a2.Id;
        AiConversation.Clear();
        (AiMessage u3, _) = AiConversation.AddRound("t_000003", "清空后的输入", "清空后的回复");
        Check($"清空历史之后 id 不复用（{lastId} → {u3.Id}）", u3.Id > lastId);
    }

    private static void TestConversationLocatesByIdNotIndex()
    {
        AiConversation.Clear();
        (AiMessage u1, AiMessage a1) = AiConversation.AddRound("t_1", "第一轮输入", "第一轮回复");
        (_, AiMessage a2) = AiConversation.AddRound("t_2", "第二轮输入", "第二轮回复");
        AiConversation.AddRound("t_3", "第三轮输入", "第三轮回复");

        int indexBefore = IndexOfMessage(a2.Id);
        Check($"移除前第二轮回复位于下标 {indexBefore}", indexBefore == 3);

        Check("移除第一轮成功", AiConversation.TryRemoveRound(a1.Id, out string error) && error == null);

        int indexAfter = IndexOfMessage(a2.Id);
        Check($"下标已经变了（{indexBefore} → {indexAfter}）—— 这正是不能用下标做标识的原因",
            indexAfter == 1 && indexAfter != indexBefore);

        AiMessage found = AiConversation.FindById(a2.Id);
        Check("按 id 仍然能定位到同一条消息", found != null && found.Id == a2.Id && found.Text == "第二轮回复");
        Check("被移除的消息按 id 查不到了", AiConversation.FindById(a1.Id) == null);
        Check("被移除的那条玩家输入也不在了（成对移除）", AiConversation.FindById(u1.Id) == null);
    }

    private static void TestConversationEditOnlyTouchesText()
    {
        AiConversation.Clear();
        SetValue(Favor(CharaA), 50);
        (AiMessage user, AiMessage assistant) = AiConversation.AddRound("t_edit", "玩家的原始输入", "AI 的原始回复");

        bool ok = AiConversation.TryEditAssistant(assistant.Id, "玩家改写后的回复", out string error);
        Check($"编辑 AI 回复成功（{Brief(error)}）", ok);
        Check("正文已替换", assistant.Text == "玩家改写后的回复");
        Check("标记为被玩家编辑过（上下文里要看得出来）", assistant.Edited);
        Check("玩家自己的输入未受影响", user.Text == "玩家的原始输入" && !user.Edited);

        // 这一条是模式 A 的核心取向：改文风不该动存档。
        Check($"编辑回复不回滚已写入的数值（好感度仍为 50，实际 {ReadValue(Favor(CharaA))}）",
            ReadValue(Favor(CharaA)) == 50);

        Check("不允许编辑玩家自己的输入",
            !AiConversation.TryEditAssistant(user.Id, "篡改玩家输入", out string userError)
            && userError != null && userError.Contains("只能编辑"));
        Check("改成空白被拒绝（空正文会让上下文出现一条没有内容的 assistant）",
            !AiConversation.TryEditAssistant(assistant.Id, "   ", out string blankError) && blankError != null);
        Check("编辑不存在的 id 被拒绝而不是静默成功",
            !AiConversation.TryEditAssistant(999999, "无主的正文", out string missingError)
            && missingError != null && missingError.Contains("999999"));
        Check("被拒绝的编辑没有改动原文", assistant.Text == "玩家改写后的回复");
    }

    private static void TestConversationRemovesRoundInPairs()
    {
        AiConversation.Clear();
        (AiMessage u1, AiMessage a1) = AiConversation.AddRound("t_1", "留下的输入", "留下的回复");
        (_, AiMessage a2) = AiConversation.AddRound("t_2", "要丢的输入", "要丢的回复");

        Check("移除前共 4 条消息", AiConversation.Count == 4);
        Check($"移除最后一轮成功", AiConversation.TryRemoveRound(a2.Id, out string error) && error == null);
        Check($"user + assistant 成对移除（实际剩 {AiConversation.Count} 条）", AiConversation.Count == 2);

        // 只删 assistant 会留下一条没有回应的玩家输入，下一轮模型会把它读成
        // "玩家说了话但我没理"，进而自行圆场——这是最容易被忽略的上下文污染。
        IReadOnlyList<AiMessage> rest = AiConversation.All;
        Check("剩下的正好是完整的一轮，没有孤立的玩家输入",
            rest.Count == 2 && !rest[0].IsAssistant && rest[1].IsAssistant
            && rest[0].Id == u1.Id && rest[1].Id == a1.Id);

        Check("拿玩家输入的 id 去丢弃会被拒绝（只认 assistant 的 id）",
            !AiConversation.TryRemoveRound(u1.Id, out string userError) && userError != null);
        Check("丢弃不存在的一轮被拒绝", !AiConversation.TryRemoveRound(888888, out _));
        Check("被拒绝的丢弃没有改动历史", AiConversation.Count == 2);
    }

    private static void TestConversationTrimsToMaxRounds()
    {
        AiConversation.Clear();
        int rounds = AiConversation.MaxRounds + 3;
        for (int i = 1; i <= rounds; i++)
            AiConversation.AddRound($"t_{i}", $"第 {i} 轮输入", $"第 {i} 轮回复");

        Check($"历史被裁到 {AiConversation.MaxRounds} 轮（{AiConversation.MaxRounds * 2} 条，实际 {AiConversation.Count} 条）",
            AiConversation.Count == AiConversation.MaxRounds * 2);

        // 裁剪也必须成对，否则窗口边界上会留下一条孤立消息。
        IReadOnlyList<AiMessage> all = AiConversation.All;
        Check("裁剪后第一条仍是玩家输入（成对淘汰，边界没有孤立消息）",
            all.Count > 0 && !all[0].IsAssistant);
        Check($"最早的 3 轮已被淘汰（现存最早是第 4 轮，实际「{Brief(all[0].Text)}」）",
            all[0].Text == "第 4 轮输入");
        Check("最新一轮仍在", all[^1].Text == $"第 {rounds} 轮回复");
        Check($"装 prompt 用的历史条数与会话一致（{AiDispatcher.History.Count}）",
            AiDispatcher.History.Count == AiConversation.Count);
    }

    // ---------- B 组：引用 ----------

    private static void TestQuoteKeepsSnapshotAgainstLaterEdit()
    {
        AiConversation.Clear();
        AiQuoteBox.Clear();
        (_, AiMessage assistant) = AiConversation.AddRound("t_q", "玩家输入", "原始的回复正文");

        Check($"引用一条回复成功（{Brief(QuoteError(assistant))}）", AiQuoteBox.Count == 1);
        Check("引用同时记下了来源 id", AiQuoteBox.Quotes[0].MessageId == assistant.Id);

        AiConversation.TryEditAssistant(assistant.Id, "被玩家改写过的正文", out _);

        AiQuote quote = AiQuoteBox.Quotes[0];
        // RISK-15：只存 id 的话，这里就会把改写后的文本当成"玩家引用的那段话"，
        // 而玩家想引用的恰恰是被他改掉的那一版。
        Check("引用内容仍是建立引用那一刻的快照", quote.Snapshot == "原始的回复正文");
        Check("原消息确实已被改写（对照组）", AiConversation.FindById(assistant.Id).Text == "被玩家改写过的正文");

        string composed = AiQuoteBox.Compose("接着这一段写", AiQuoteBox.Quotes);
        Check("装配 prompt 用的是快照", composed.Contains("原始的回复正文"));
        Check("装配 prompt 不会回头去查已被改写的原消息", !composed.Contains("被玩家改写过的正文"));
        Check("引用标签能显示来源方", quote.Label.StartsWith("AI：", StringComparison.Ordinal));
    }

    private static void TestQuoteSurvivesMessageEviction()
    {
        AiConversation.Clear();
        AiQuoteBox.Clear();
        (_, AiMessage assistant) = AiConversation.AddRound("t_old", "很早的输入", "很早以前的那段回复");
        long oldId = assistant.Id;
        AiQuoteBox.TryAdd(assistant, out _);

        for (int i = 1; i <= AiConversation.MaxRounds + 1; i++)
            AiConversation.AddRound($"t_new_{i}", $"新输入 {i}", $"新回复 {i}");

        Check("被引用的消息已被上下文窗口淘汰", AiConversation.FindById(oldId) == null);
        Check("引用条目仍然存在", AiQuoteBox.Count == 1);
        Check("引用内容不受淘汰影响", AiQuoteBox.Quotes[0].Snapshot == "很早以前的那段回复");
        Check("引用仍能拼进本轮输入", AiQuoteBox.Compose("继续", AiQuoteBox.Quotes).Contains("很早以前的那段回复"));
        Check("来源 id 保留下来（原消息没了也只影响界面定位，不影响装配）",
            AiQuoteBox.Quotes[0].MessageId == oldId);
    }

    private static void TestQuoteRejectsDuplicateEmptyAndOverflow()
    {
        AiConversation.Clear();
        AiQuoteBox.Clear();
        (_, AiMessage a1) = AiConversation.AddRound("t_1", "输入一", "回复一");
        (_, AiMessage a2) = AiConversation.AddRound("t_2", "输入二", "回复二");
        (_, AiMessage a3) = AiConversation.AddRound("t_3", "输入三", "回复三");
        (_, AiMessage a4) = AiConversation.AddRound("t_4", "输入四", "回复四");
        (AiMessage emptyUser, _) = AiConversation.AddRound("t_5", "", "");

        Check("引用第一条成功", AiQuoteBox.TryAdd(a1, out _));
        Check("重复引用同一条被拒绝而不是叠加两遍",
            !AiQuoteBox.TryAdd(a1, out string dupError) && dupError != null && dupError.Contains("已经在引用栏"));
        Check($"引用栏仍只有 1 条（实际 {AiQuoteBox.Count}）", AiQuoteBox.Count == 1);

        Check("空消息无法引用（引用一段空文本只会浪费 token）",
            !AiQuoteBox.TryAdd(emptyUser, out string emptyError) && emptyError != null && emptyError.Contains("空消息"));
        Check("引用 null 被拒绝而不是抛异常", !AiQuoteBox.TryAdd(null, out _));

        AiQuoteBox.TryAdd(a2, out _);
        AiQuoteBox.TryAdd(a3, out _);
        Check($"引用条数达到上限 {AiQuoteBox.MaxQuotes}", AiQuoteBox.Count == AiQuoteBox.MaxQuotes);
        Check($"超过 {AiQuoteBox.MaxQuotes} 条被拒绝（引用太多会挤占本轮指令的注意力）",
            !AiQuoteBox.TryAdd(a4, out string overflowError) && overflowError != null && overflowError.Contains("最多引用"));

        Check("移除一条引用成功", AiQuoteBox.TryRemoveAt(0));
        Check($"移除后剩 {AiQuoteBox.MaxQuotes - 1} 条", AiQuoteBox.Count == AiQuoteBox.MaxQuotes - 1);
        Check("移除越界下标被拒绝而不是抛异常", !AiQuoteBox.TryRemoveAt(99) && !AiQuoteBox.TryRemoveAt(-1));
        Check("腾出位置之后又能引用了", AiQuoteBox.TryAdd(a4, out _));
    }

    private static void TestQuoteComposePutsQuotesFirst()
    {
        AiConversation.Clear();
        AiQuoteBox.Clear();
        (AiMessage user, AiMessage assistant) = AiConversation.AddRound("t_c", "玩家先前的那句话", "AI 那段回复");
        AiQuoteBox.TryAdd(assistant, out _);
        AiQuoteBox.TryAdd(user, out _);

        string composed = AiQuoteBox.Compose("请把这一段写得更克制", AiQuoteBox.Quotes);
        int quoteIndex = composed.IndexOf("[引用上文", StringComparison.Ordinal);
        int instructionIndex = composed.IndexOf("玩家指令:", StringComparison.Ordinal);

        // 引用在前、指令在后：模型读到"要求是什么"时，"针对哪一段"已经交代完了。
        Check("引用拼在本轮输入的开头", quoteIndex >= 0 && instructionIndex > quoteIndex);
        Check("本轮指令原样保留", composed.Contains("玩家指令: 请把这一段写得更克制"));
        Check("两条引用都在", composed.Contains("AI 那段回复") && composed.Contains("玩家先前的那句话"));
        Check("引用标注了来源方（AI 的回复 / 玩家先前的输入）",
            composed.Contains("AI 的回复") && composed.Contains("玩家先前的输入"));
        Check("没有引用时输入原样透传", AiQuoteBox.Compose("裸输入", []) == "裸输入");
        Check("引用列表为 null 时也原样透传（防御空引用栏）", AiQuoteBox.Compose("裸输入", null) == "裸输入");

        AiQuoteBox.Clear();
        Check("清空引用栏", AiQuoteBox.Count == 0);
    }

    // ---------- C 组：interact 段静态校验 ----------

    private static void TestInteractStaticValidationCatchesMistakes()
    {
        Install(BrokenInteractLibraryJson, "写坏的交互契约库");

        // 每一条都是「不校验就会静默失效」的错误。引擎与模型都不会报错，
        // 表现出来只是"AI 从来不给选项"或"命令点了没反应"，没有诊断就无从下手。
        Check("报告 max_options = 0（所有选项都会被丢弃）", HasDiag("max_options"));
        Check("报告 option_max_chars = 0（选项会被截成空串）", HasDiag("option_max_chars"));
        Check("报告命令名重复（后一条永远选不中）", HasDiag("重复") && HasDiag("重名"));
        Check("报告命令缺少 command 名（模型无法引用它）", HasDiag("缺少 command"));
        Check("报告命令既无 value 又无 input（会往引擎喂空输入）", HasDiag("空载荷"));
        Check("报告命令同时写了 value 与 input（input 被静默忽略）", HasDiag("两个载荷"));
        Check("报告 input 含换行（一条命令会推进多步流程）", HasDiag("带换行") && HasDiag("换行"));
        Check("报告开了注入开关但没声明整数区间", HasDiag("input_int_range"));
        Check("报告开了注入开关但没声明字数上限", HasDiag("input_str_max_chars"));

        // 只报告不修正：静默修正会让人以为自己写对了。
        AiInteractTemplate broken = AiTraitLibrary.InteractTemplate;
        Check("校验不会偷偷改写词条库里的值（max_options 仍是 0）",
            broken != null && broken.MaxOptions == 0);
        Check("坏命令仍然留在库里（由执行期逐条拒绝，而不是加载期悄悄删掉）",
            broken.AllowedCommands.Count == 6);
    }

    private static void TestMissingAndDisabledInteractSection()
    {
        Install(NoInteractLibraryJson, "无 interact 段的库");
        Check("没写 interact 段时模板为 null（安静跳过，不报错也不崩）",
            AiTraitLibrary.InteractTemplate == null);
        Check("没写 interact 段时不产生交互相关诊断噪音", !HasDiag("interact."));

        var request = new AiActionRequest { Kind = AiActionKind.Command, Command = "抚摸" };
        Check("模板为 null 时交互指令一律被拒",
            !AiActionExecutor.TryValidate(null, request, "t_x", 1, out _, out string nullError)
            && nullError != null && nullError.Contains("未启用"));
        Check("模板为 null 时选项也一律清空",
            AiActionExecutor.Sanitize(null, [new AiOption { Label = "选项" }], out _).Count == 0);

        Install(DisabledInteractLibraryJson, "停用 interact 段的库");
        AiInteractTemplate disabled = AiTraitLibrary.InteractTemplate;
        Check("契约还在但 enabled = false", disabled != null && !disabled.Enabled);
        Check("停用时命令仍然读得到（只是不许用）", disabled.FindCommand("抚摸") != null);
        Check("停用时交互指令被拒",
            !AiActionExecutor.TryValidate(disabled, request, "t_x", 1, out _, out string disabledError)
            && disabledError != null);
        Check("停用时选项被整批忽略并说明原因",
            AiActionExecutor.Sanitize(disabled, [new AiOption { Label = "选项" }], out string note).Count == 0
            && note != null && note.Contains("未启用"));
        Check("停用时不会因为 allowed_commands 非空而报『无命令可触发』", !HasDiag("无命令可触发"));
    }
    // ---------- D 组：请求装配与交互 schema ----------

    private static void TestSchemaOnlyWhenEngineWaiting()
    {
        AiComputeRequest idle = AiComputeRequestBuilder.Build(CharaA, "引擎不在等输入的一轮", 101, false, out string idleError);
        Check($"引擎不在等输入时装配仍然成功（数值结算照跑，{Brief(idleError)}）", idle != null);
        if (idle == null)
            return;
        Check("引擎不在等输入时不下发交互指令", !idle.InteractEnabled);
        Check("schema 里没有 options 字段（结构上不存在，模型就填不出来）",
            !idle.SchemaJson.Contains("\"options\""));
        Check("schema 里没有 action 字段", !idle.SchemaJson.Contains("\"action\""));
        Check("system 指令里也没有交互说明（否则会诱导模型编一个做不到的动作）",
            !FirstContent(idle.Messages, "system").Contains("交互建议"));
        Check("数值部分不受影响，changes 仍在 schema 里", idle.SchemaJson.Contains("\"changes\""));

        AiComputeRequest waiting = AiComputeRequestBuilder.Build(CharaA, "引擎在等输入的一轮", 102, true, out string waitError);
        Check($"引擎在等输入时装配成功（{Brief(waitError)}）", waiting != null);
        if (waiting == null)
            return;
        Check("引擎在等输入时才下发交互指令", waiting.InteractEnabled);
        Check("schema 里出现 options", waiting.SchemaJson.Contains("\"options\""));
        Check("schema 里出现 action", waiting.SchemaJson.Contains("\"action\""));
        Check("契约对象被带进请求，供收尾时按同一份契约校验", waiting.Interact != null);
        Check("三参重载默认不下发交互指令（保守的一侧）",
            AiComputeRequestBuilder.Build(CharaA, "默认重载", 103, out _)?.InteractEnabled == false);

        requestSample = Dump(waiting);
    }

    private static void TestSchemaEnumeratesOnlyDeclaredCommands()
    {
        AiComputeRequest request = AiComputeRequestBuilder.Build(CharaA, "命令枚举", 104, true, out string error);
        Check($"装配成功（{Brief(error)}）", request != null);
        if (request == null)
            return;

        string schema = request.SchemaJson;
        Check("声明过的三条命令都在 enum 里",
            schema.Contains("\"抚摸\"") && schema.Contains("\"交谈\"") && schema.Contains("\"结束回合\""));
        Check("命令说明进了 schema，模型才知道每条命令是干什么的",
            schema.Contains("轻抚对方") && schema.Contains("与对方说话"));

        // 关键取向：模型看得到命令名，看不到载荷。它因此不需要理解 ERA 的命令编号体系
        // （那是幻觉高发区），也不可能通过编一个编号来触发未声明的行为。
        Check("载荷（COM 编号 11 / 12）不出现在 schema 里，模型无从臆造编号",
            !schema.Contains("11") || !schema.Contains("\"value\": 11"));
        Check("kind 枚举含 none 与 command", schema.Contains("\"none\"") && schema.Contains("\"command\""));
        Check("kind 的说明写明了 none 表示不推进流程", schema.Contains("none 表示本轮不推进流程"));
        Check("schema 是合法 JSON", IsJsonObject(schema));
        Check("选项上限与字数上限按词条库下发（3 条 / 10 字）",
            schema.Contains("最多 3 条") && schema.Contains("不超过 10 字"));
    }

    private static void TestSchemaOmitsInjectionFieldsWhenClosed()
    {
        AiComputeRequest request = AiComputeRequestBuilder.Build(CharaA, "注入关闭", 105, true, out _);
        Check("装配成功", request != null);
        if (request == null)
            return;

        string schema = request.SchemaJson;
        Check("注入关闭时 kind 枚举里没有 input_int", !schema.Contains("\"input_int\""));
        Check("注入关闭时 kind 枚举里没有 input_str", !schema.Contains("\"input_str\""));
        Check("注入关闭时不出现 text 字段（自由文本入口整个不存在）", !schema.Contains("kind=input_str 时的文本"));
        Check("命令触发仍然可用（关掉的只是自由注入这一条更危险的路）", schema.Contains("\"command\""));

        Install(InjectionNoRangeLibraryJson, "开了注入开关但没声明范围的库");
        AiComputeRequest noRange = AiComputeRequestBuilder.Build(CharaA, "只开开关", 106, true, out _);
        Check("装配成功", noRange != null);
        if (noRange == null)
            return;
        // 「开了开关就等于放行」是这一段最容易出的理解错误。范围没声明，入口就不该出现。
        Check("只开开关但没声明整数区间时，schema 里仍然没有 input_int",
            !noRange.SchemaJson.Contains("\"input_int\""));
        Check("只开开关但没声明字数上限时，schema 里仍然没有 input_str",
            !noRange.SchemaJson.Contains("\"input_str\""));
    }

    private static void TestSchemaIncludesInjectionFieldsWhenDeclared()
    {
        Install(InjectionLibraryJson, "自由注入已声明范围的库");
        AiComputeRequest request = AiComputeRequestBuilder.Build(CharaA, "注入开放", 107, true, out string error);
        Check($"装配成功（{Brief(error)}）", request != null);
        if (request == null)
            return;

        string schema = request.SchemaJson;
        Check("声明范围之后 input_int 才出现在 kind 枚举里", schema.Contains("\"input_int\""));
        Check("声明上限之后 input_str 才出现在 kind 枚举里", schema.Contains("\"input_str\""));
        Check("整数区间下发给模型（[0, 99]）", schema.Contains("[0, 99]"));
        Check("字数上限下发给模型（8 字）", schema.Contains("不超过 8 字"));
        Check("明确要求注入文本不得含换行（换行会被拆成多段输入）", schema.Contains("不得含换行"));
        Check("schema 仍是合法 JSON", IsJsonObject(schema));
    }

    private static void TestInteractPromptFragmentComesAfterComputeInstruction()
    {
        Install(InteractLibraryJson, "交互测试库");
        AiComputeRequest request = AiComputeRequestBuilder.Build(CharaA, "指令顺序", 108, true, out _);
        Check("装配成功", request != null);
        if (request == null)
            return;

        string instruction = FirstContent(request.Messages, "system");
        int computeIndex = instruction.IndexOf("数值结算引擎", StringComparison.Ordinal);
        int interactIndex = instruction.IndexOf("除了数值结算", StringComparison.Ordinal);

        // 顺序是刻意的：数值结算是副 API 的主职，交互只是附加能力。
        // 先说主职，避免模型把重心挪到"给选项"上而草率对待数值。
        Check("数值结算指令在前", computeIndex >= 0);
        Check("交互说明追加在数值指令之后而不是替换它", interactIndex > computeIndex);
        Check("交互说明保留了「不确定就填 none」这条铁律", instruction.Contains("不确定就填 none"));
        Check("交互说明讲清了为什么动作要保守（数值能撤销、流程不能）",
            instruction.Contains("数值写错能撤销"));
        Check("权威状态仍然是最后一份 system 消息（避免旧值被当成现值）",
            LastSystemContent(request.Messages).Contains("权威状态"));
        Check("本轮事件仍作为 user 消息在最后",
            request.Messages[^1].Role == "user" && request.Messages[^1].Content.Contains("指令顺序"));
    }

    // ---------- E 组：交互内容解析 ----------

    private static void TestParserReadsOptionsAndAction()
    {
        string json = """
            {
              "schema_version": "1.0",
              "turn_id": "t_000200",
              "changes": [ { "field": "好感度", "chara_no": 1, "op": "add", "value": 3 } ],
              "narrative_hint": "她稍微放松了一点",
              "options": [
                { "label": "继续抚摸", "hint": "顺着刚才的动作" },
                { "label": "换个话题" }
              ],
              "action": { "kind": "command", "command": "抚摸", "reason": "顺势而为" }
            }
            """;
        AiComputeResult parsed = AiComputeParser.Parse(json, "t_000200", out string error);
        Check($"解析成功（{Brief(error)}）", parsed != null);
        if (parsed == null)
            return;

        Check("changes 照常解析", parsed.Changes.Count == 1 && parsed.Changes[0].Value == 3);
        Check($"读到 2 条选项（实际 {parsed.Options.Count}）", parsed.Options.Count == 2);
        Check("选项文本正确", parsed.Options[0].Label == "继续抚摸" && parsed.Options[1].Label == "换个话题");
        Check("选项的补充说明也读到了", parsed.Options[0].Hint == "顺着刚才的动作");
        Check("读到命令动作", parsed.Action != null && parsed.Action.Kind == AiActionKind.Command);
        Check("命令名正确", parsed.Action.Command == "抚摸");
        Check("模型自述的理由留了下来（面板要显示给玩家看）", parsed.Action.Reason == "顺势而为");
        Check("解析阶段不做契约校验（命令是否在白名单要用当时的词条库判定）", parsed.InteractNote == null);
    }

    private static void TestParserToleratesBareStringOptions()
    {
        string json = """
            {
              "schema_version": "1.0",
              "turn_id": "t_000201",
              "changes": [],
              "options": [ "裸字符串选项", "  另一条  ", "", "   " ],
              "action": { "kind": "none" }
            }
            """;
        AiComputeResult parsed = AiComputeParser.Parse(json, "t_000201", out string error);
        Check($"解析成功（{Brief(error)}）", parsed != null);
        if (parsed == null)
            return;

        // 裸字符串是模型常见的省事写法，语义毫无歧义。
        // 因为形态不合就丢掉一条本来能用的选项，只是把成本转嫁给玩家。
        Check($"裸字符串选项被接受（实际 {parsed.Options.Count} 条）", parsed.Options.Count == 2);
        Check("首尾空白被清掉", parsed.Options[1].Label == "另一条");
        Check("空串与纯空白被丢掉", parsed.Options[0].Label == "裸字符串选项");
        Check("kind = none 时不产生动作（没提动作与提出不动作在语义上相同）", parsed.Action == null);
        Check("kind = none 不算错误", parsed.InteractNote == null);
    }

    private static void TestParserKeepsChangesWhenInteractIsGarbage()
    {
        string json = """
            {
              "schema_version": "1.0",
              "turn_id": "t_000202",
              "changes": [
                { "field": "好感度", "chara_no": 1, "op": "add", "value": 4, "reason": "正常的数值" }
              ],
              "options": "这里本该是数组",
              "action": [ "这里本该是对象" ]
            }
            """;
        AiComputeResult parsed = AiComputeParser.Parse(json, "t_000202", out string error);

        // 这是 E 组最重要的一条：交互与数值的后果不对等。
        // 数值写进存档，一条离谱就说明模型这一轮的理解不可靠；
        // 而坏选项最坏只是"这一轮没有按钮"。为它把算对的数值一起丢掉是亏的。
        Check($"交互部分写坏不影响整体解析（{Brief(error)}）", parsed != null);
        if (parsed == null)
            return;
        Check("changes 完整保留", parsed.Changes.Count == 1 && parsed.Changes[0].Value == 4);
        Check("坏的 options 被忽略", parsed.Options.Count == 0);
        Check("坏的 action 被忽略", parsed.Action == null);
        Check($"但把这件事记下来了（{Brief(parsed.InteractNote)}）", parsed.InteractNote != null);
        Check("说明里点明 options 类型不对", parsed.InteractNote.Contains("options 不是数组"));
        Check("说明里点明 action 类型不对", parsed.InteractNote.Contains("action 不是对象"));

        // 对照：changes 本身写坏时必须整批拒绝，取向与交互相反。
        string badChanges = """
            {
              "schema_version": "1.0",
              "turn_id": "t_000203",
              "changes": [ { "field": "好感度", "value": "三点五" } ],
              "options": [ "这条选项没问题" ]
            }
            """;
        AiComputeResult rejected = AiComputeParser.Parse(badChanges, "t_000203", out string badError);
        Check($"changes 写坏时整批拒绝（取向与交互相反，{Brief(badError)}）", rejected == null);
    }

    private static void TestParserRejectsUnknownActionKind()
    {
        string json = """
            {
              "schema_version": "1.0",
              "turn_id": "t_000204",
              "changes": [],
              "action": { "kind": "launch_missile", "reason": "臆造的动作类型" }
            }
            """;
        AiComputeResult parsed = AiComputeParser.Parse(json, "t_000204", out _);
        Check("解析未失败", parsed != null);
        if (parsed == null)
            return;
        Check("不认识的 kind 不产生动作", parsed.Action == null);
        Check($"记下了原因（{Brief(parsed.InteractNote)}）",
            parsed.InteractNote != null && parsed.InteractNote.Contains("launch_missile"));

        string missingCommand = """
            {
              "schema_version": "1.0",
              "turn_id": "t_000205",
              "changes": [],
              "action": { "kind": "command" }
            }
            """;
        AiComputeResult noName = AiComputeParser.Parse(missingCommand, "t_000205", out _);
        Check("kind = command 但没给命令名时不产生动作", noName != null && noName.Action == null);
        Check("并说明缺了 command 名", noName.InteractNote != null && noName.InteractNote.Contains("没给 command 名"));

        string badValue = """
            {
              "schema_version": "1.0",
              "turn_id": "t_000206",
              "changes": [],
              "action": { "kind": "input_int", "value": "不是数字" }
            }
            """;
        AiComputeResult badInt = AiComputeParser.Parse(badValue, "t_000206", out _);
        Check("input_int 的 value 不是整数时不产生动作", badInt != null && badInt.Action == null);
        Check("并说明 value 不是整数", badInt.InteractNote != null && badInt.InteractNote.Contains("value 不是整数"));

        string emptyText = """
            {
              "schema_version": "1.0",
              "turn_id": "t_000207",
              "changes": [],
              "action": { "kind": "input_str", "text": "   " }
            }
            """;
        AiComputeResult blank = AiComputeParser.Parse(emptyText, "t_000207", out _);
        Check("input_str 的 text 为空时不产生动作", blank != null && blank.Action == null);
        Check("并说明 text 为空", blank.InteractNote != null && blank.InteractNote.Contains("text 为空"));
    }
    // ---------- F 组：契约层校验与选项清洗 ----------

    private static void TestValidateRejectsUndeclaredCommand()
    {
        AiInteractTemplate template = AiTraitLibrary.InteractTemplate;
        Check("交互测试库已装载", template != null && template.Enabled);
        if (template == null)
            return;

        var undeclared = new AiActionRequest { Kind = AiActionKind.Command, Command = "脱衣服" };
        Check("未声明的命令被拒",
            !AiActionExecutor.TryValidate(template, undeclared, "t_f1", 201, out AiPendingAction pending, out string error)
            && pending == null);
        Check($"错误信息点名了那条命令（{Brief(error)}）", error != null && error.Contains("脱衣服"));

        var declared = new AiActionRequest { Kind = AiActionKind.Command, Command = "抚摸", Reason = "顺势而为" };
        Check($"声明过的命令通过（{Brief(QuoteValidate(template, declared, out AiPendingAction ok))}）", ok != null);
        if (ok == null)
            return;
        Check("载荷由本地查表得到，不是模型给的（11）", ok.Payload == "11");
        Check("载荷标记为数值型", ok.IsIntPayload);
        Check("描述面向玩家而不是面向引擎", ok.Description == "触发命令：抚摸");
        Check("轮次与票号被带上，日志里能对上是哪一次请求", ok.TurnId == "t_f1" && ok.Ticket == 200);
        Check("模型的理由被保留", ok.Reason == "顺势而为");
        Check("尚未执行", !ok.Consumed);

        var caseInsensitive = new AiActionRequest { Kind = AiActionKind.Command, Command = "抚摸" };
        Check("命令名查表不区分大小写（中文无影响，但英文命令名会用到）",
            AiActionExecutor.TryValidate(template, caseInsensitive, "t_f1", 202, out _, out _));
        Check("kind = none 直接放行且不产生待执行动作",
            AiActionExecutor.TryValidate(template, new AiActionRequest { Kind = AiActionKind.None }, "t_f1", 203,
                out AiPendingAction none, out _) && none == null);
        Check("request 为 null 时放行且不产生动作（模型没提动作是正常情况）",
            AiActionExecutor.TryValidate(template, null, "t_f1", 204, out AiPendingAction nullAction, out _)
            && nullAction == null);
    }

    private static void TestValidateRejectsInjectionWhenClosed()
    {
        AiInteractTemplate closed = AiTraitLibrary.InteractTemplate;
        Check("当前库的自由注入是关闭的", closed != null && !closed.AllowInputInjection);
        if (closed == null)
            return;

        var intInject = new AiActionRequest { Kind = AiActionKind.InputInt, Value = 5 };
        Check("注入关闭时整数注入被拒",
            !AiActionExecutor.TryValidate(closed, intInject, "t_f2", 205, out _, out string intError));
        Check($"错误信息指向 allow_input_injection（{Brief(intError)}）",
            intError != null && intError.Contains("allow_input_injection"));

        var strInject = new AiActionRequest { Kind = AiActionKind.InputStr, Text = "任意文本" };
        Check("注入关闭时字符串注入被拒",
            !AiActionExecutor.TryValidate(closed, strInject, "t_f2", 206, out _, out string strError)
            && strError != null);

        // 命令白名单不受影响：关掉的是"不查表直接喂"这条更危险的路。
        Check("注入关闭不影响声明过的命令",
            AiActionExecutor.TryValidate(closed, new AiActionRequest { Kind = AiActionKind.Command, Command = "交谈" },
                "t_f2", 207, out AiPendingAction cmd, out _) && cmd != null && cmd.Payload == "12");
    }

    private static void TestValidateRejectsInjectionOutOfRange()
    {
        Install(InjectionNoRangeLibraryJson, "开了注入开关但没声明范围的库");
        AiInteractTemplate noRange = AiTraitLibrary.InteractTemplate;
        Check("开关已开", noRange != null && noRange.AllowInputInjection);
        if (noRange == null)
            return;

        // 「开了开关就等于放行」是这一段最容易出的理解错误，必须单独验。
        Check("开了开关但没声明整数区间，整数注入仍被拒",
            !AiActionExecutor.TryValidate(noRange, new AiActionRequest { Kind = AiActionKind.InputInt, Value = 5 },
                "t_f3", 208, out _, out string intError));
        Check($"错误信息指向 input_int_range（{Brief(intError)}）",
            intError != null && intError.Contains("input_int_range"));
        Check("开了开关但没声明字数上限，字符串注入仍被拒",
            !AiActionExecutor.TryValidate(noRange, new AiActionRequest { Kind = AiActionKind.InputStr, Text = "文本" },
                "t_f3", 209, out _, out string strError));
        Check($"错误信息指向 input_str_max_chars（{Brief(strError)}）",
            strError != null && strError.Contains("input_str_max_chars"));

        Install(InjectionLibraryJson, "自由注入已声明范围的库");
        AiInteractTemplate ranged = AiTraitLibrary.InteractTemplate;
        Check("范围已声明为 [0, 99]", ranged != null && ranged.HasIntRange
            && ranged.IntRangeMin == 0 && ranged.IntRangeMax == 99);
        if (ranged == null)
            return;

        Check("超出上界被拒",
            !AiActionExecutor.TryValidate(ranged, new AiActionRequest { Kind = AiActionKind.InputInt, Value = 100 },
                "t_f3", 210, out _, out string highError) && highError != null && highError.Contains("100"));
        Check("低于下界被拒",
            !AiActionExecutor.TryValidate(ranged, new AiActionRequest { Kind = AiActionKind.InputInt, Value = -1 },
                "t_f3", 211, out _, out _));
        Check("边界值本身放行（区间是闭区间）",
            AiActionExecutor.TryValidate(ranged, new AiActionRequest { Kind = AiActionKind.InputInt, Value = 0 },
                "t_f3", 212, out _, out _)
            && AiActionExecutor.TryValidate(ranged, new AiActionRequest { Kind = AiActionKind.InputInt, Value = 99 },
                "t_f3", 213, out _, out _));
        Check("超过字数上限被拒",
            !AiActionExecutor.TryValidate(ranged, new AiActionRequest { Kind = AiActionKind.InputStr, Text = "一二三四五六七八九" },
                "t_f3", 214, out _, out string longError) && longError != null && longError.Contains("上限 8"));
        Check("空文本被拒（注入一个空串等于按了一次回车，语义完全不同）",
            !AiActionExecutor.TryValidate(ranged, new AiActionRequest { Kind = AiActionKind.InputStr, Text = "" },
                "t_f3", 215, out _, out _));
    }

    private static void TestValidateAcceptsDeclaredInjection()
    {
        AiInteractTemplate ranged = AiTraitLibrary.InteractTemplate;
        if (ranged == null)
        {
            Check("自由注入库已装载", false);
            return;
        }

        bool intOk = AiActionExecutor.TryValidate(ranged,
            new AiActionRequest { Kind = AiActionKind.InputInt, Value = 42, Reason = "选第 42 项" },
            "t_f4", 216, out AiPendingAction intAction, out string intError);
        Check($"范围内的整数注入通过（{Brief(intError)}）", intOk && intAction != null);
        if (intAction != null)
        {
            Check("载荷是十进制字符串（引擎入口只吃字符串）", intAction.Payload == "42");
            Check("标记为数值型，执行前要核对引擎在等数值", intAction.IsIntPayload);
            Check("描述写明了注入的值", intAction.Description == "输入数值：42");
        }

        bool strOk = AiActionExecutor.TryValidate(ranged,
            new AiActionRequest { Kind = AiActionKind.InputStr, Text = "爱丽丝" },
            "t_f4", 217, out AiPendingAction strAction, out string strError);
        Check($"上限内的字符串注入通过（{Brief(strError)}）", strOk && strAction != null);
        if (strAction != null)
        {
            Check("载荷原样保留", strAction.Payload == "爱丽丝");
            Check("标记为字符串型", !strAction.IsIntPayload);
        }
    }

    private static void TestValidateFlattensNewlineInInjectedText()
    {
        AiInteractTemplate ranged = AiTraitLibrary.InteractTemplate;
        if (ranged == null)
        {
            Check("自由注入库已装载", false);
            return;
        }

        // 换行是这一段最隐蔽的越权：引擎按 \n 拆分输入依次喂入，
        // 一条"文本注入"就能悄悄推进好几步流程。压成空格而不是拒绝，
        // 是因为模型换行往往只是排版习惯，语义上仍是一句话。
        bool ok = AiActionExecutor.TryValidate(ranged,
            new AiActionRequest { Kind = AiActionKind.InputStr, Text = "上\n下" },
            "t_f5", 218, out AiPendingAction action, out string error);
        Check($"含换行的注入文本被接受（{Brief(error)}）", ok && action != null);
        if (action == null)
            return;
        Check("换行被压成空格", action.Payload == "上 下");
        Check("载荷里不再有换行（否则会被拆成多段输入）",
            !action.Payload.Contains('\n') && !action.Payload.Contains('\r'));

        bool crlf = AiActionExecutor.TryValidate(ranged,
            new AiActionRequest { Kind = AiActionKind.InputStr, Text = "甲\r\n乙" },
            "t_f5", 219, out AiPendingAction crlfAction, out _);
        Check("CRLF 也被压平", crlf && crlfAction != null && !crlfAction.Payload.Contains('\r'));
    }

    private static void TestSanitizeTrimsAndDropsOptions()
    {
        Install(InteractLibraryJson, "交互测试库");
        AiInteractTemplate template = AiTraitLibrary.InteractTemplate;
        Check("上限为 3 条 / 10 字", template != null && template.MaxOptions == 3 && template.OptionMaxChars == 10);
        if (template == null)
            return;

        var raw = new List<AiOption>
        {
            new() { Label = "  正常选项  ", Hint = "带空白" },
            new() { Label = "这是一条明显超过十个字的很长选项" },
            new() { Label = "" },
            new() { Label = "正常选项" },
            new() { Label = "第三条" },
            new() { Label = "第四条" },
            new() { Label = "带\n换行" },
        };
        List<AiOption> clean = AiActionExecutor.Sanitize(template, raw, out string note);

        // 与 changes 相反的取向：坏的单条丢弃/截断，不整批拒绝。
        // 选项只是界面按钮，为一条超长选项把其余几条一起丢掉毫无收益。
        Check($"清洗后不超过上限 3 条（实际 {clean.Count}）", clean.Count == 3);
        Check("首尾空白被清掉", clean[0].Label == "正常选项");
        Check("补充说明保留", clean[0].Hint == "带空白");
        Check("超长选项被截断到 10 字而不是丢弃",
            clean.Count > 1 && clean[1].Label.Length == 10 && clean[1].Label == "这是一条明显超过十个");
        Check("重复选项去重（清洗后同名的算重复）", CountLabel(clean, "正常选项") == 1);
        Check($"清洗过程被记下来（{Brief(note)}）", note != null && note.Contains("丢弃") && note.Contains("截断"));
        Check("超出上限的部分被截掉尾部，不影响前面几条", clean[2].Label == "第三条");

        Check("空列表原样返回空", AiActionExecutor.Sanitize(template, [], out string emptyNote).Count == 0
            && emptyNote == null);
        Check("null 列表返回空而不是抛异常", AiActionExecutor.Sanitize(template, null, out _).Count == 0);

        var fine = new List<AiOption> { new() { Label = "短选项" } };
        Check("全部合规时不产生噪音说明",
            AiActionExecutor.Sanitize(template, fine, out string fineNote).Count == 1 && fineNote == null);
    }

    // ---------- G 组：引擎状态层与执行层 ----------

    private static void TestEngineReadyRejectsTypeMismatch()
    {
        AiInteractTemplate template = AiTraitLibrary.InteractTemplate;
        Check("交互测试库已装载", template != null);
        if (template == null)
            return;

        Check($"引擎停在整数输入等待上（实际 {target.NowInputType}）",
            target.IsWaitInputState && target.NowInputType == InputType.IntValue);

        var intAction = new AiPendingAction { Payload = "11", IsIntPayload = true, Description = "数值载荷" };
        Check($"数值载荷通过引擎状态层（{Brief(QuoteReady(intAction))}）",
            AiActionExecutor.IsEngineReady(target, intAction, out _));

        // 这一层不可省。ERA 对"不该来的输入"是静默失败：
        // 文本喂进 INPUT 会被直接丢掉，不报错，表现为"AI 说要做什么但什么都没发生"。
        var strAction = new AiPendingAction { Payload = "文本", IsIntPayload = false, Description = "文本载荷" };
        Check("引擎在等整数时文本载荷被拒",
            !AiActionExecutor.IsEngineReady(target, strAction, out string strError));
        Check($"错误信息说明会被静默丢弃（{Brief(strError)}）",
            strError != null && strError.Contains("静默丢弃"));

        Check("action 为 null 时被拒而不是抛异常",
            !AiActionExecutor.IsEngineReady(target, null, out string nullError) && nullError != null);

        var consumed = new AiPendingAction { Payload = "11", IsIntPayload = true, Consumed = true };
        Check("已处置过的动作被拒（一次性）",
            !AiActionExecutor.IsEngineReady(target, consumed, out string consumedError)
            && consumedError != null && consumedError.Contains("处置过"));

        Check("console 为 null 时被拒",
            !AiActionExecutor.IsEngineReady(null, intAction, out string consoleError) && consoleError != null);
    }

    private static void TestExecuteReallyDrivesEngine()
    {
        // 这是整个 P4 唯一真的把载荷喂进引擎的地方。前面所有校验都只是"判定可执行"，
        // 只有这里能证明"判定通过的东西确实会生效"。
        var action = new AiPendingAction
        {
            TurnId = "t_g1",
            Ticket = 301,
            Kind = AiActionKind.Command,
            Description = "触发命令：交谈",
            Payload = "12",
            IsIntPayload = true,
        };

        Check("执行前引擎在等输入", target.IsWaitInputState);
        bool ok = AiActionExecutor.TryExecute(target, action, out string error);
        Check($"执行成功（{Brief(error)}）", ok);
        Check("执行后标记为已消费", action.Consumed);

        // harness 的 SYSTEM.ERB 收到整数后打印并回到 TINPUT，所以引擎应当仍在等整数。
        Check($"引擎确实被推进了一步并回到输入等待（实际 {target.NowInputType}）",
            target.IsWaitInputState && target.NowInputType == InputType.IntValue);
        Check($"脚本真的收到了我们喂进去的值（RESULT 实际 {ReadValue("RESULT:0")}）", ReadValue("RESULT:0") == 12);
    }

    private static void TestOnePhraseTruncationIsRejected()
    {
        // 切到 TONEINPUT。截断是最容易被忽略的一类错：
        // "12" 被截成 "1" 之后仍然是个合法输入，引擎不报错，玩家看到的是"AI 触发了别的命令"。
        var switchAction = new AiPendingAction
        {
            Payload = SwitchToOnePhrase,
            IsIntPayload = true,
            Description = "切到单字符等待",
        };
        Check($"切换到单字符等待成功（{Brief(QuoteExecute(switchAction))}）", switchAction.Consumed);
        Check($"引擎现在是单字符等待（实际 IsWaintingOnePhrase={target.IsWaintingOnePhrase}）",
            target.IsWaitInputState && target.IsWaintingOnePhrase);

        var twoChars = new AiPendingAction { Payload = "12", IsIntPayload = true, Description = "两位数载荷" };
        Check("单字符等待下两位数载荷被拒（会被截成一位）",
            !AiActionExecutor.IsEngineReady(target, twoChars, out string error));
        Check($"错误信息点明会被截断（{Brief(error)}）", error != null && error.Contains("截断"));
        Check("被拒的动作没有被消费掉（还能等状态合适时再执行）", !twoChars.Consumed);

        var oneChar = new AiPendingAction { Payload = "5", IsIntPayload = true, Description = "一位数载荷" };
        Check("单字符载荷仍然放行", AiActionExecutor.IsEngineReady(target, oneChar, out _));
        Check($"执行单字符载荷成功（{Brief(QuoteExecute(oneChar))}）", oneChar.Consumed);
        Check($"脚本收到的单字符就是我们喂的 5（RESULT 实际 {ReadValue("RESULT:0")}）", ReadValue("RESULT:0") == 5);
        Check($"回到整数等待，后续断言的前提恢复（实际 {target.NowInputType}）",
            target.IsWaitInputState && !target.IsWaintingOnePhrase && target.NowInputType == InputType.IntValue);
    }

    private static void TestExecuteRejectedWhileLocked()
    {
        // 锁定期间引擎的全部输入入口都会拒绝（AiRequestLock 旁路 4）。
        // 执行层若不自己先判一次，PressEnterKey 会静默返回，
        // 表现为"日志说执行了但什么都没发生"——而 Consumed 已经置位，动作再也执行不了。
        long ticket = AiRequestLock.TryAcquire(target, out _);
        Check("取到锁（模拟请求进行中）", ticket != 0);

        var action = new AiPendingAction { Payload = "12", IsIntPayload = true, Description = "锁定期间的动作" };
        Check("锁定期间执行被拒", !AiActionExecutor.TryExecute(target, action, out string error));
        Check($"错误信息说明要等这一轮结束（{Brief(error)}）", error != null && error.Contains("等这一轮结束"));
        Check("被拒的动作没有被消费（解锁后仍可执行）", !action.Consumed);

        AiRequestLock.Release(ticket);
        Check("锁已释放", !AiRequestLock.IsLocked);
        Check("解锁后同一条动作可以执行", AiActionExecutor.TryExecute(target, action, out string afterError) && afterError == null);
    }

    private static void TestExecuteRejectsSecondUse()
    {
        var action = new AiPendingAction { Payload = "12", IsIntPayload = true, Description = "只能执行一次" };
        Check($"第一次执行成功（{Brief(QuoteExecute(action))}）", action.Consumed);
        Check("第二次执行被拒", !AiActionExecutor.TryExecute(target, action, out string error));
        Check($"错误信息说明已处置过（{Brief(error)}）", error != null && error.Contains("处置过"));

        // 调度器侧的两条一次性出口。
        AiDispatcher.TryDiscardPendingAction(out _);
        Check("没有待执行动作时执行被拒而不是静默成功",
            !AiDispatcher.TryExecutePendingAction(target, out string noneError) && noneError != null);
        Check("没有待执行动作时放弃也被拒",
            !AiDispatcher.TryDiscardPendingAction(out string discardError) && discardError != null);
    }
    // ---------- H 组：串联（替身后端） ----------

    private static int chainStage;
    private static int computeCallCount;
    private static string lastComputeJson;
    private static string mainInputSeen;
    private static int historyBeforeAbort;
    private static string abortedInput;
    private static AiPendingAction droppedAction;

    /// <summary>
    /// 串联链路的起点。后面每一步都由 TurnCompleted 事件推进，
    /// 因为一轮请求横跨界面线程与后台线程，只有事件回调里才能断言"这一轮真的完成了"。
    /// </summary>
    private static void StartInteractChain()
    {
        AiDispatcher.ClearHistory();
        AiDispatcher.UseFakeBackend = false;
        AiDispatcher.TurnCompleted += OnChainCompleted;
        SetValue(Favor(CharaA), 50);

        AiDispatcher.MainBackendOverride = messages =>
        {
            mainInputSeen = LastUserContent(messages);
            return "[自检替身] 叙事正文";
        };

        chainStage = 1;
        InstallComputeBackend("抚摸", """[ { "label": "继续", "hint": "顺着来" }, { "label": "停下" } ]""");
        Check("发起带交互内容的一轮成功", AiDispatcher.TryBeginTurn(target, "玩家摸了摸她的头"));
    }

    /// <summary>副 API 替身。命令名与选项由调用方指定，turn_id 原样回填。</summary>
    private static void InstallComputeBackend(string command, string optionsJson)
    {
        AiDispatcher.ComputeBackendOverride = request =>
        {
            computeCallCount++;
            string actionJson = command == null
                ? """{ "kind": "none" }"""
                : $$"""{ "kind": "command", "command": "{{command}}", "reason": "自检替身的理由" }""";
            lastComputeJson = $$"""
                {
                  "schema_version": "1.0",
                  "turn_id": "{{request.TurnId}}",
                  "changes": [
                    { "field": "好感度", "chara_no": 1, "op": "add", "value": 5, "reason": "自检替身" }
                  ],
                  "narrative_hint": "她的态度略有软化",
                  "options": {{optionsJson}},
                  "action": {{actionJson}}
                }
                """;
            return lastComputeJson;
        };
    }

    private static void OnChainCompleted(AiTurnResult result)
    {
        try
        {
            switch (chainStage)
            {
                case 1:
                    ChainStage1PendingNotAutoExecuted(result);
                    return;
                case 2:
                    ChainStage2QuoteAndBadAction(result);
                    return;
                case 3:
                    ChainStage3PendingSurvivesUntilNextTurn(result);
                    return;
                case 4:
                    ChainStage4NewTurnDropsStalePending(result);
                    return;
                case 5:
                    ChainStage5AutoExecuteDrivesEngine(result);
                    return;
                case 6:
                    ChainStage6RevisionReplacesInPlace(result);
                    return;
                case 7:
                    ChainStage7AbortKeepPartial(result);
                    return;
                case 8:
                    ChainStage8AbortRetry(result);
                    return;
                case 9:
                    ChainStage9RetriedTurnAndDropRound(result);
                    return;
            }
        }
        catch (Exception ex)
        {
            Log("FATAL", $"串联自检抛出异常：{ex}");
        }

        AiDispatcher.TurnCompleted -= OnChainCompleted;
        RestoreAll();
        Finish();
    }

    /// <summary>
    /// 第一步：auto_execute = false 时，动作只摆出来，绝不自己执行。
    /// 这是 P4 最重要的一条产品取向——数值写错能撤销，流程被推进无法撤销。
    /// </summary>
    private static void ChainStage1PendingNotAutoExecuted(AiTurnResult result)
    {
        Check($"本轮成功（{Brief(result.ErrorMessage)}）", result.Success);
        Check($"数值照常回写（好感度 50 → 55，实际 {ReadValue(Favor(CharaA))}）", ReadValue(Favor(CharaA)) == 55);
        Check($"选项被采纳（实际 {result.Options.Count} 条）", result.Options.Count == 2);
        Check("选项文本正确", result.Options[0].Label == "继续" && result.Options[1].Label == "停下");
        Check($"没有清洗噪音（{Brief(result.OptionNote)}）", result.OptionNote == null);

        Check("动作通过了三层校验里的前两层，进入待执行", result.PendingAction != null);
        if (result.PendingAction == null)
            return;
        Check("调度器持有同一个待执行动作", ReferenceEquals(AiDispatcher.PendingAction, result.PendingAction));
        Check("载荷由本地查表得到（抚摸 = 11）", result.PendingAction.Payload == "11");
        Check("面板文案面向玩家", result.PendingAction.Description == "触发命令：抚摸");
        Check("默认不自动执行（auto_execute = false）", !result.ActionAutoExecuted);
        Check("未被消费", !result.PendingAction.Consumed);
        Check($"没有跳过原因（{Brief(result.ActionSkipReason)}）", result.ActionSkipReason == null);
        Check("引擎没有被推进（RESULT 仍是 G 组留下的 12）", ReadValue("RESULT:0") == 12);
        Check("正文与 id 都记进了会话", result.AssistantMessageId != 0 && AiConversation.Count == 2);

        // 玩家点「执行动作」。
        bool executed = AiDispatcher.TryExecutePendingAction(target, out string error);
        Check($"玩家手动执行成功（{Brief(error)}）", executed);
        Check($"引擎真的被推进了（RESULT 实际 {ReadValue("RESULT:0")}）", ReadValue("RESULT:0") == 11);
        Check("执行后调度器不再持有待执行动作", AiDispatcher.PendingAction == null);
        Check("重复执行被拒绝", !AiDispatcher.TryExecutePendingAction(target, out _));
        Check("引擎回到输入等待，后续步骤的前提成立", target.IsWaitInputState);

        // 第二步：带引用发一轮，且副 API 提一条未声明的命令。
        chainStage = 2;
        AiMessage assistant = AiConversation.LastAssistant();
        Check("引用上一条 AI 回复成功", AiQuoteBox.TryAdd(assistant, out string quoteError) && quoteError == null);
        SetValue(Favor(CharaA), 50);
        InstallComputeBackend("脱衣服", """[ "第一条", "第一条", "这是一条会被截断的超长选项", "多出来的第四条" ]""");
        Check("发起带引用的一轮成功", AiDispatcher.TryBeginTurn(target, "接着上面那段写"));
    }

    /// <summary>
    /// 第二步：引用真的进了本轮输入；未声明的命令被拒但数值与选项不受牵连。
    /// </summary>
    private static void ChainStage2QuoteAndBadAction(AiTurnResult result)
    {
        Check($"本轮成功（{Brief(result.ErrorMessage)}）", result.Success);
        Check($"带上了 1 条引用（实际 {result.QuoteCount}）", result.QuoteCount == 1);
        Check("引用栏在发出时已清空（不会跨轮重复带上）", AiQuoteBox.Count == 0);
        Check("本轮实际送出的输入含引用前缀", result.RequestInput != null && result.RequestInput.Contains("[引用上文"));
        Check("引用拼在开头，玩家指令在后",
            result.RequestInput.IndexOf("[引用上文", StringComparison.Ordinal)
            < result.RequestInput.IndexOf("玩家指令:", StringComparison.Ordinal));
        Check("主 API 收到的正是带引用的那份输入",
            mainInputSeen != null && mainInputSeen.Contains("[引用上文") && mainInputSeen.Contains("接着上面那段写"));

        // 未声明的命令被拒 —— 但这不该牵连任何别的东西。
        Check("未声明的命令没有变成待执行动作", result.PendingAction == null && AiDispatcher.PendingAction == null);
        Check($"记下了被拒的原因（{Brief(result.ActionSkipReason)}）",
            result.ActionSkipReason != null && result.ActionSkipReason.Contains("脱衣服"));
        Check($"数值不受牵连（好感度 50 → 55，实际 {ReadValue(Favor(CharaA))}）", ReadValue(Favor(CharaA)) == 55);
        Check($"选项不受牵连（清洗后 {result.Options.Count} 条）", result.Options.Count == 3);
        Check($"选项清洗过程被记下（{Brief(result.OptionNote)}）", result.OptionNote != null);
        Check("重复选项被去重", CountLabel(result.Options, "第一条") == 1);
        Check("引擎没有被推进（被拒的动作不会偷偷生效）", ReadValue("RESULT:0") == 11);

        // 第三步：再产生一个合法动作，但这次不执行它。
        chainStage = 3;
        SetValue(Favor(CharaA), 50);
        InstallComputeBackend("交谈", "[]");
        Check("发起将留下未执行动作的一轮成功", AiDispatcher.TryBeginTurn(target, "她想说点什么"));
    }

    private static void ChainStage3PendingSurvivesUntilNextTurn(AiTurnResult result)
    {
        Check($"本轮成功（{Brief(result.ErrorMessage)}）", result.Success);
        Check("留下了待执行动作", result.PendingAction != null);
        if (result.PendingAction == null)
            return;
        Check("载荷是交谈的 12", result.PendingAction.Payload == "12");
        Check("没有选项时 Options 为空而不是 null", result.Options != null && result.Options.Count == 0);

        droppedAction = result.PendingAction;

        // 第四步：不执行它，直接发新请求。
        chainStage = 4;
        SetValue(Favor(CharaA), 50);
        InstallComputeBackend(null, "[]");
        Check("存在未执行动作时新请求不被拦住（与待处置事务不同）",
            AiDispatcher.TryBeginTurn(target, "玩家改主意了，做点别的"));
    }

    /// <summary>
    /// 第四步：新请求会作废上一轮没执行的动作。
    ///
    /// 这是 PendingAction 与 PendingTransaction 的分野：事务的数值已经落盘，
    /// 不处置就会失去可撤回性，所以它硬拦新请求；动作什么都没做过，
    /// 留着反而更糟——面板上那句"触发命令：交谈"早已不对应当前的剧情。
    /// </summary>
    private static void ChainStage4NewTurnDropsStalePending(AiTurnResult result)
    {
        Check($"本轮成功（{Brief(result.ErrorMessage)}）", result.Success);
        Check("上一轮的动作已被作废", AiDispatcher.PendingAction == null);
        Check("被作废的动作没有被执行掉（作废 ≠ 执行）", droppedAction != null && !droppedAction.Consumed);
        Check("引擎确实没有被那条作废的动作推进", ReadValue("RESULT:0") == 11);
        Check("本轮 kind = none，不产生新动作", result.PendingAction == null);
        Check($"kind = none 不算错误（{Brief(result.ActionSkipReason)}）", result.ActionSkipReason == null);
        Check($"数值照常回写（实际 {ReadValue(Favor(CharaA))}）", ReadValue(Favor(CharaA)) == 55);

        // 第五步：打开 auto_execute。
        chainStage = 5;
        Install(AutoExecuteLibraryJson, "自动执行库");
        SetValue(Favor(CharaA), 50);
        InstallComputeBackend("抚摸", "[]");
        Check("发起自动执行的一轮成功", AiDispatcher.TryBeginTurn(target, "顺势抚摸"));
    }

    /// <summary>
    /// 第五步：auto_execute = true 时动作立刻生效，且必须发生在锁释放之后。
    /// 锁定期间引擎的输入入口一律拒绝，在锁内执行会静默失败——
    /// 表现为"日志说执行了但什么都没发生"，而 Consumed 已置位，再也补不回来。
    /// </summary>
    private static void ChainStage5AutoExecuteDrivesEngine(AiTurnResult result)
    {
        Check($"本轮成功（{Brief(result.ErrorMessage)}）", result.Success);
        Check("动作已自动执行", result.ActionAutoExecuted);
        Check("自动执行后不再留待执行动作", AiDispatcher.PendingAction == null);
        Check("执行发生在锁释放之后", !AiRequestLock.IsLocked);
        Check($"引擎真的被推进了（这一份库的抚摸载荷是 33，实际 RESULT={ReadValue("RESULT:0")}）",
            ReadValue("RESULT:0") == 33);
        Check($"数值也照常回写（实际 {ReadValue(Favor(CharaA))}）", ReadValue(Favor(CharaA)) == 55);
        Check("引擎回到输入等待", target.IsWaitInputState);

        // 第六步：带修改指令重生成最后一条回复。
        chainStage = 6;
        Install(InteractLibraryJson, "交互测试库");
        historyBeforeAbort = AiConversation.Count;
        AiMessage assistant = AiConversation.LastAssistant();
        Check("有可修改的最后一条回复", assistant != null);
        if (assistant == null)
            return;
        droppedAction = null;
        AiDispatcher.MainBackendOverride = messages =>
        {
            mainInputSeen = LastSystemContent(messages);
            return "[自检替身] 改写后的正文";
        };
        Check("发起带指令重写成功",
            AiDispatcher.TryReviseLastResponse(target, "写得更克制一些", out string error) && error == null);
    }

    /// <summary>
    /// 第六步：修改回复是**就地替换**那条 assistant，不新增一轮，也不动数值。
    /// 新增一轮会在历史里留下"被否决的回复 + 一段修改要求"，
    /// 模型下一轮会把那次否决也当成剧情的一部分。
    /// </summary>
    private static void ChainStage6RevisionReplacesInPlace(AiTurnResult result)
    {
        Check($"重写成功（{Brief(result.ErrorMessage)}）", result.Success);
        Check("标记为修改而不是新的一轮", result.IsRevision);
        Check($"历史条数未增加（{historyBeforeAbort} → {AiConversation.Count}）",
            AiConversation.Count == historyBeforeAbort);

        AiMessage assistant = AiConversation.FindById(result.AssistantMessageId);
        Check("按 id 找回被改写的那条消息", assistant != null);
        if (assistant != null)
        {
            Check("正文已就地替换", assistant.Text.Contains("改写后的正文"));
            Check("模式 B 不标记为玩家手改（那是模式 A 的语义）", !assistant.Edited);
        }
        Check("修改指令作为最后一条 system 消息下发",
            mainInputSeen != null && mainInputSeen.Contains("写得更克制一些"));
        Check("指令明确要求只重写那一条、不要继续推进剧情",
            mainInputSeen.Contains("不要继续推进剧情"));
        Check($"重写绝不回滚已写入的数值（仍为 55，实际 {ReadValue(Favor(CharaA))}）",
            ReadValue(Favor(CharaA)) == 55);
        Check("重写不产生待执行动作", AiDispatcher.PendingAction == null);

        // 第七步：一轮被终止，验证「保留部分」这条处置。
        chainStage = 7;
        historyBeforeAbort = AiConversation.Count;
        SetValue(Favor(CharaA), 50);
        InstallComputeBackend("抚摸", "[]");
        AiDispatcher.MainBackendOverride = _ =>
        {
            System.Threading.Thread.Sleep(700);
            return "[自检替身] 终止前已经收到的正文";
        };
        Check("发起将被终止的一轮成功", AiDispatcher.TryBeginTurn(target, "将被终止的一轮"));
        StartAbortTimer();
    }

    /// <summary>
    /// 终止必须落在「副 API 已写完、主 API 还没返回」的窗口里。
    /// 用界面线程计时器而不是原地 Sleep：原地睡会堵住界面线程，
    /// 而副 API 的回写要回到界面线程执行，那样就永远等不到它写完。
    /// </summary>
    private static void StartAbortTimer()
    {
        abortTimer?.Stop();
        abortTimer = new System.Windows.Forms.Timer { Interval = 350 };
        abortTimer.Tick += (s, e) =>
        {
            abortTimer.Stop();
            Check($"终止前副 API 已完成回写（实际 {ReadValue(Favor(CharaA))}）", ReadValue(Favor(CharaA)) == 55);
            Check("终止请求被接受", AiDispatcher.Abort());
        };
        abortTimer.Start();
    }

    private static void ChainStage7AbortKeepPartial(AiTurnResult result)
    {
        Check("这一轮标记为 Aborted", result.Aborted);
        Check("不报告成功", !result.Success);
        Check("已写入的数值被撤回", result.ComputeRolledBack);
        Check($"好感度回到 50（实际 {ReadValue(Favor(CharaA))}）", ReadValue(Favor(CharaA)) == 50);
        Check("待执行动作一并作废（数值都退回去了，动作失去依据）", AiDispatcher.PendingAction == null);
        Check($"作废原因写进了结果（{Brief(result.ActionSkipReason)}）", result.ActionSkipReason != null);

        // 竞态：正文其实已经收到，只是终止请求先落到界面线程。
        // 那一轮已经被 RecordHistory 记进历史，必须撤出来——否则数值退回去了正文却留着，
        // 而且下面的「保留部分」会把同一段正文再加一遍。
        Check($"已记入历史的那一轮被撤出（{historyBeforeAbort} → {AiConversation.Count}）",
            AiConversation.Count == historyBeforeAbort);
        Check("正文没有丢，它留在终止记录里", AiDispatcher.LastAbortedTurn != null
            && !string.IsNullOrEmpty(AiDispatcher.LastAbortedTurn.PartialText));
        Check("终止记录留下了本轮实际送出的输入，供「重试」原样重发",
            AiDispatcher.LastAbortedTurn.UserInput == "将被终止的一轮");

        // 处置之一：保留部分。
        bool kept = AiDispatcher.TryKeepAbortedPartial(out string keepError);
        Check($"保留被终止的正文成功（{Brief(keepError)}）", kept);
        Check($"历史补回一轮（实际 {AiConversation.Count}）", AiConversation.Count == historyBeforeAbort + 2);
        AiMessage assistant = AiConversation.LastAssistant();
        Check("保留下来的正文标注为被中断（否则模型会当成完整叙事往下接）",
            assistant != null && assistant.Interrupted);
        Check("短记忆写明了这一轮被中断且数值未变",
            AiComputeMemory.All.Count > 0 && AiComputeMemory.All[^1].Summary.Contains("数值未变"));
        Check($"保留正文不会把数值也带回来（仍为 50，实际 {ReadValue(Favor(CharaA))}）",
            ReadValue(Favor(CharaA)) == 50);
        Check("同一轮不能再处置第二次（防止「保留」之后又「重试」算两次）",
            !AiDispatcher.TryRetryAbortedTurn(target, out string retryError)
            && retryError != null && retryError.Contains("处置过"));
        Check("丢弃这条已处置的记录成功（清掉入口）", AiDispatcher.TryDiscardAbortedTurn(out _));
        Check("没有终止记录时三条处置都被拒而不是静默成功",
            !AiDispatcher.TryKeepAbortedPartial(out _)
            && !AiDispatcher.TryRetryAbortedTurn(target, out _)
            && !AiDispatcher.TryDiscardAbortedTurn(out _));

        // 第八步：再终止一轮，这次验证「重试」。
        chainStage = 8;
        historyBeforeAbort = AiConversation.Count;
        abortedInput = "这一轮要重试";
        SetValue(Favor(CharaA), 50);
        AiDispatcher.MainBackendOverride = _ =>
        {
            System.Threading.Thread.Sleep(700);
            return "[自检替身] 会被丢掉的正文";
        };
        Check("发起第二次将被终止的一轮成功", AiDispatcher.TryBeginTurn(target, abortedInput));
        StartAbortTimer();
    }

    private static void ChainStage8AbortRetry(AiTurnResult result)
    {
        Check("第二次终止也标记为 Aborted", result.Aborted);
        Check($"数值再次被撤回（实际 {ReadValue(Favor(CharaA))}）", ReadValue(Favor(CharaA)) == 50);
        Check("留下了终止记录", AiDispatcher.LastAbortedTurn != null);

        chainStage = 9;
        SetValue(Favor(CharaA), 50);
        AiDispatcher.MainBackendOverride = messages =>
        {
            mainInputSeen = LastUserContent(messages);
            return "[自检替身] 重试后的正文";
        };
        bool retried = AiDispatcher.TryRetryAbortedTurn(target, out string error);
        Check($"重试被接受（{Brief(error)}）", retried);
        Check("重试之后终止记录已清掉", AiDispatcher.LastAbortedTurn == null);
    }

    private static void ChainStage9RetriedTurnAndDropRound(AiTurnResult result)
    {
        Check($"重试的一轮成功（{Brief(result.ErrorMessage)}）", result.Success);
        Check("拿到了新的正文", result.NarrativeText.Contains("重试后的正文"));
        // 复用原轮次的输入而不是让玩家重敲：引用栏在发出时已清空，
        // 重敲的内容与被终止的那一轮不是同一个请求。
        Check($"重试用的是原样的输入（实际「{Brief(result.RequestInput)}」）", result.RequestInput == abortedInput);
        Check("主 API 收到的也是那份输入", mainInputSeen != null && mainInputSeen.Contains(abortedInput));
        Check($"重试是完整的一轮，数值重新结算（实际 {ReadValue(Favor(CharaA))}）", ReadValue(Favor(CharaA)) == 55);
        Check($"历史增加一轮（{historyBeforeAbort} → {AiConversation.Count}）",
            AiConversation.Count == historyBeforeAbort + 2);
        Check($"副 API 被调用的总次数（{computeCallCount}）大于轮数，说明重试确实重新算过", computeCallCount >= 8);

        // 最后一步：丢弃最后一轮。数值绝不跟着回滚。
        int before = AiConversation.Count;
        long favorBefore = ReadValue(Favor(CharaA));
        bool dropped = AiDispatcher.TryDropLastRound(out string error);
        Check($"丢弃最后一轮成功（{Brief(error)}）", dropped);
        Check($"user + assistant 成对移除（{before} → {AiConversation.Count}）",
            AiConversation.Count == before - 2);
        IReadOnlyList<AiMessage> rest = AiConversation.All;
        Check("没有留下孤立的玩家输入", rest.Count == 0 || rest[^1].IsAssistant);
        Check($"丢弃对话不回滚数值（仍为 {favorBefore}，实际 {ReadValue(Favor(CharaA))}）",
            ReadValue(Favor(CharaA)) == favorBefore);

        while (AiDispatcher.TryDropLastRound(out _))
        {
        }
        Check("历史清空后再丢弃被拒而不是静默成功", !AiDispatcher.TryDropLastRound(out string emptyError)
            && emptyError != null);

        chainStage = 10;
        AiDispatcher.TurnCompleted -= OnChainCompleted;
        RestoreAll();
        Finish();
    }
    // ---------- 工具 ----------

    private static long Register(long charaNo) => GlobalStatic.VEvaluator.GetChara(charaNo);

    private static string Favor(long charaNo) => $"CFLAG:{Register(charaNo)}:好感度";

    private static string Stamina(long charaNo) => $"BASE:{Register(charaNo)}:0";

    private static long ReadValue(string target)
        => AiVariableAccess.TryReadInt(target, out long value, out _) ? value : long.MinValue;

    private static void SetValue(string target, long value)
    {
        var batch = new List<AiValueChange> { new() { Target = target, Op = "set", Value = value } };
        if (!AiVariableAccess.TryApplyAll(batch, out string error))
            Log("WARN", $"自检准备数值失败（{target} = {value}）：{error}");
    }

    private static int IndexOfMessage(long id)
    {
        IReadOnlyList<AiMessage> all = AiConversation.All;
        for (int i = 0; i < all.Count; i++)
        {
            if (all[i].Id == id)
                return i;
        }
        return -1;
    }

    private static int CountLabel(IReadOnlyList<AiOption> options, string label)
    {
        int count = 0;
        foreach (AiOption option in options)
        {
            if (string.Equals(option.Label, label, StringComparison.Ordinal))
                count++;
        }
        return count;
    }

    /// <summary>词条诊断里是否出现过某个关键词。用于验证静态校验真的报了那一条。</summary>
    private static bool HasDiag(string needle)
    {
        foreach (string entry in AiTraitLibrary.Diagnostics)
        {
            if (entry != null && entry.Contains(needle, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    /// <summary>调一次 TryAdd 并把错误带回来，方便写进断言描述。</summary>
    private static string QuoteError(AiMessage message)
    {
        AiQuoteBox.TryAdd(message, out string error);
        return error;
    }

    private static string QuoteValidate(AiInteractTemplate template, AiActionRequest request, out AiPendingAction pending)
    {
        AiActionExecutor.TryValidate(template, request, "t_f1", 200, out pending, out string error);
        return error;
    }

    private static string QuoteReady(AiPendingAction action)
    {
        AiActionExecutor.IsEngineReady(target, action, out string error);
        return error;
    }

    private static string QuoteExecute(AiPendingAction action)
    {
        AiActionExecutor.TryExecute(target, action, out string error);
        return error;
    }

    private static bool IsJsonObject(string json)
    {
        try
        {
            using System.Text.Json.JsonDocument doc = System.Text.Json.JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object;
        }
        catch
        {
            return false;
        }
    }

    private static string FirstContent(IReadOnlyList<ChatMessage> messages, string role)
    {
        foreach (ChatMessage m in messages)
        {
            if (string.Equals(m.Role, role, StringComparison.Ordinal))
                return m.Content ?? "";
        }
        return "";
    }

    private static string LastSystemContent(IReadOnlyList<ChatMessage> messages)
    {
        string last = null;
        foreach (ChatMessage m in messages)
        {
            if (string.Equals(m.Role, "system", StringComparison.Ordinal))
                last = m.Content;
        }
        return last;
    }

    private static string LastUserContent(IReadOnlyList<ChatMessage> messages)
    {
        string last = null;
        foreach (ChatMessage m in messages)
        {
            if (string.Equals(m.Role, "user", StringComparison.Ordinal))
                last = m.Content;
        }
        return last;
    }

    private static string Dump(AiComputeRequest request)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"turn_id：{request.TurnId}｜角色号：{request.CharaNo}｜交互指令已下发：{request.InteractEnabled}");
        if (request.Interact != null)
        {
            AiInteractTemplate t = request.Interact;
            sb.AppendLine($"  interact：启用 {t.Enabled}｜自动执行 {t.AutoExecute}｜选项上限 {t.MaxOptions} 条 / {t.OptionMaxChars} 字｜自由注入 {t.AllowInputInjection}");
            foreach (AiInteractCommand c in t.AllowedCommands)
                sb.AppendLine($"  命令 {c.Command} → 载荷「{c.Payload}」（{(c.IsIntPayload ? "数值" : "文本")}）");
        }
        sb.AppendLine("---- messages ----");
        foreach (ChatMessage m in request.Messages)
        {
            sb.AppendLine($"[{m.Role}]");
            sb.AppendLine(m.Content);
        }
        sb.AppendLine("---- function schema ----");
        sb.AppendLine(request.SchemaJson);
        return sb.ToString();
    }

    // ---------- 报告 ----------

    private static void Section(string title)
    {
        lines.Add("");
        lines.Add($"== {title} ==");
    }

    private static void Check(string description, bool condition)
    {
        if (condition)
        {
            passed++;
            lines.Add($"  PASS  {description}");
        }
        else
        {
            failed++;
            lines.Add($"  FAIL  {description}");
        }
    }

    private static void Log(string level, string message) => lines.Add($"  {level}  {message}");

    private static string Brief(string text)
    {
        if (string.IsNullOrEmpty(text))
            return "无";
        text = text.Replace("\r", "").Replace("\n", " ");
        return text.Length <= 56 ? text : text[..56] + "\u2026";
    }

    private static void Finish()
    {
        if (finished)
            return;
        finished = true;
        pollTimer?.Stop();
        abortTimer?.Stop();
        watchdogTimer?.Stop();

        var sb = new StringBuilder();
        sb.AppendLine("ERA-AI P4 交互控制自检报告");
        sb.AppendLine($"时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"结果：PASS={passed}  FAIL={failed}");
        foreach (string line in lines)
            sb.AppendLine(line);
        sb.AppendLine();
        sb.AppendLine("---- 一次带交互 schema 的副 API 请求（人工核对用） ----");
        sb.AppendLine(requestSample ?? "(未采集)");
        sb.AppendLine();
        sb.AppendLine("---- 调度器日志 ----");
        foreach (string line in AiDispatcher.Log)
            sb.AppendLine("  " + line);
        sb.AppendLine();
        sb.AppendLine("---- 运行期诊断（最后一组库） ----");
        foreach (string line in AiTraitDiagnostics.Entries)
            sb.AppendLine("  " + line);
        sb.AppendLine();
        sb.AppendLine(failed == 0 ? "INTERACT SELFTEST RESULT: OK" : "INTERACT SELFTEST RESULT: FAILED");

        string path = Environment.GetEnvironmentVariable(ReportEnv);
        if (string.IsNullOrWhiteSpace(path))
            path = Path.Combine(Program.ExeDir, "ai_interact_selftest.txt");
        try
        {
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }
        catch (Exception)
        {
        }

        target?.Window?.Close();
    }
}