using MinorShift.Emuera.AI.Traits;
using MinorShift.Emuera.GameView;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace MinorShift.Emuera.AI.Compute;

/// <summary>
/// P3 副 API（计算通道）自动自检。
///
/// 由环境变量 ERA_AI_COMPUTE_SELFTEST=1 触发，全程在界面线程上跑，结束后把报告写到
/// ERA_AI_COMPUTE_SELFTEST_REPORT 指定的文件（默认 exe 目录下 ai_compute_selftest.txt）并关闭窗口。
/// 不设环境变量时该类完全不激活，因此不影响玩家。
///
/// 验收目标（对应设计文档 P3）：
///   - 权威状态快照能读到真实数值，且与主 API 的 state_fields 共用同一份定义
///   - 请求装配把可写字段变成 function schema 的 enum，写错的字段在装配阶段就被剔除
///   - 解析层对缺字段 / 类型不对 / turn_id 不匹配一律整批拒绝，绝不猜
///   - 校验层三道关（字段 / 幅度 / 区间）逐一生效，任一不过一个字节都不写
///   - 主副 API 串联时序正确：先算后叙，主 prompt 读到的是回写后的新值
///   - RISK-05 两条出路都通：仅重生成正文、回滚本轮数值
///   - 终止请求必须把已写入的数值撤回
///   - 配置写错时必须报错或安全降级，而不是静默改错数值
///
/// 自检会临时替换 exe 同目录的 ai_traits.json 并改动 harness 的角色数值，
/// 结束时在 finally 中还原两者。
/// </summary>
internal static partial class AiComputeSelfTest
{
    private const string EnableEnv = "ERA_AI_COMPUTE_SELFTEST";
    private const string ReportEnv = "ERA_AI_COMPUTE_SELFTEST_REPORT";

    /// <summary>自检用角色号，对应 harness 的 CHARA1.CSV / CHARA2.CSV。</summary>
    private const long CharaA = 1;
    private const long CharaB = 2;

    private static readonly List<string> lines = [];
    private static int passed;
    private static int failed;

    private static System.Windows.Forms.Timer pollTimer;
    private static System.Windows.Forms.Timer abortTimer;

    /// <summary>
    /// 看门狗。G 组的异步链路靠 TurnCompleted 事件推进，任何一环没触发事件就会永久停住，
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

    /// <summary>最近一次副 API 请求的预览，写进报告便于人工核对实际发出的内容。</summary>
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
            Log("FATAL", "等待脚本进入输入等待状态超时，副 API 自检无法进行。");
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
    /// 同步组。跑完后进入 F 组的异步链路，异步链路由 TurnCompleted 事件驱动。
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

            Section("A 组：权威状态快照");
            Install(ComputeLibraryJson, "副 API 测试库");
            TestSnapshotReadsRealValues();
            TestSnapshotSharesStateFieldsWithPrompt();
            TestSnapshotCoversAllCharasWhenAsked();
            TestSnapshotSkipsUnregisteredChara();

            Section("B 组：请求装配与 function schema");
            Install(ComputeLibraryJson, "副 API 测试库");
            TestRequestCarriesStateAndSchema();
            TestSchemaEnumeratesOnlyDeclaredFields();
            TestAuthoritativeStateComesAfterMemory();
            TestBrokenFieldsAreExcludedAtBuildTime();

            Section("C 组：输出解析（宁可整批拒绝，也不猜）");
            Install(ComputeLibraryJson, "副 API 测试库");
            TestParserAcceptsWellFormedOutput();
            TestParserRejectsBadEnvelope();
            TestParserRejectsBadValueTypes();
            TestParserToleratesModelQuirks();

            Section("D 组：校验与回写");
            Install(ComputeLibraryJson, "副 API 测试库");
            TestApplyWritesAndRecordsBefore();
            TestApplyRejectsOverDelta();
            TestApplyRejectsUndeclaredFieldAndOp();
            TestApplyRejectsDuplicateAndBadChara();
            TestApplyRespectsMaxChanges();
            TestApplyClampsOutOfRange();
            TestApplyRejectsOutOfRangeWhenConfigured();
            TestApplyRejectsInvertedRange();
            TestApplyHandlesGlobalField();
            TestRollbackRestoresValues();

            Section("E 组：短记忆窗口");
            TestMemoryKeepsAtMostFiveRounds();
            TestMemoryEntersPromptAsStaleContext();

            Section("F 组：配置写错必须报错或安全降级");
            TestMissingComputeSectionSkipsQuietly();
            TestDisabledComputeSectionSkips();
            TestStaticValidationCatchesComputeMistakes();

            Section("H 组：玩家手动调整与撤销");
            Install(ComputeLibraryJson, "副 API 测试库");
            TestManualEditableMirrorsWritableFields();
            TestManualEditIgnoresDeltaAndRange();
            TestManualEditStillRespectsEngineChecks();
            TestManualEditIsReversible();

            Section("G 组：主副 API 串联（替身后端）");
            Install(ComputeLibraryJson, "副 API 测试库");
            StartDualChannelChain();
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
        bool moneyOk = AiVariableAccess.TryReadInt("MONEY:0", out long money, out string moneyError);
        Check($"所持金为 5000（实际 {(moneyOk ? money.ToString(CultureInfo.InvariantCulture) : Brief(moneyError))}）",
            moneyOk && money == 5000);
        if (!favorOk || !moneyOk)
        {
            Log("FATAL", "harness 的 CFLAG.CSV 必须定义『好感度』『信頼』，SYSTEM.ERB 必须设置 MONEY。");
            return false;
        }

        // 收尾用的还原批次。自检会反复改这几项，跑完必须写回，否则下一次跑的前置断言就不成立了。
        Snapshot(Favor(CharaA));
        Snapshot(Trust(CharaA));
        Snapshot(Stamina(CharaA));
        Snapshot(Favor(CharaB));
        Snapshot("MONEY:0");
        return true;
    }

    private static void Snapshot(string target)
    {
        if (AiVariableAccess.TryReadInt(target, out long value, out _))
            restoreBatch.Add(new AiValueChange { Target = target, Op = "set", Value = value });
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
    // ---------- A 组：权威状态快照 ----------

    private static void TestSnapshotReadsRealValues()
    {
        AiStateSnapshotData snapshot = AiStateSnapshot.Build(CharaA, false, out string error);
        Check($"快照采集未报错（{Brief(error)}）", error == null);
        Check($"快照只含当前角色（实际 {snapshot.Charas.Count} 个）", snapshot.Charas.Count == 1);
        if (snapshot.Charas.Count != 1)
            return;

        AiStateCharaEntry entry = snapshot.Charas[0];
        Check($"快照里的角色号是角色号而不是登录号（实际 {entry.CharaNo}）", entry.CharaNo == CharaA);
        Check($"快照带上了角色名（实际 {entry.Name}）", entry.Name == "爱丽丝");
        Check("快照读到真实好感度 50", entry.Fields.TryGetValue("好感度", out string favor) && favor == "50");
        Check("快照读到真实体力 100", entry.Fields.TryGetValue("体力", out string stamina) && stamina == "100");
        Check("compute.extra_state_fields 的全局项进了快照（所持金 5000）",
            snapshot.Global.TryGetValue("所持金", out string money) && money == "5000");

        string json = snapshot.ToJson();
        Check("快照 JSON 含 chara_no 字段", json.Contains("\"chara_no\": 1"));
        Check("快照 JSON 含全局段", json.Contains("\"global\""));
        Check("快照 JSON 未泄漏 {CHARA} 占位符", !json.Contains("{CHARA}"));
    }

    private static void TestSnapshotSharesStateFieldsWithPrompt()
    {
        List<AiStateField> fields = AiStateSnapshot.CollectFields();
        Check($"状态字段 = prompt.state_fields 2 项 + extra 1 项（实际 {fields.Count}）", fields.Count == 3);
        Check("主 API 的 state_fields 被复用（好感度）", HasField(fields, "好感度"));
        Check("主 API 的 state_fields 被复用（体力）", HasField(fields, "体力"));
        Check("副 API 独有的 extra_state_fields 也在（所持金）", HasField(fields, "所持金"));

        // 主 API prompt 与副 API 快照必须读到同一个值，否则两边对"当前状态"的理解已经分叉。
        string prompt = AiPromptBuilder.Build(CharaA, out AiPromptBuildInfo info);
        AiStateSnapshotData snapshot = AiStateSnapshot.Build(CharaA, false, out _);
        string snapshotFavor = snapshot.Charas.Count > 0 && snapshot.Charas[0].Fields.TryGetValue("好感度", out string f) ? f : "?";
        Check($"主 API prompt 与副 API 快照的好感度一致（{snapshotFavor}）",
            info.UsedTraits && prompt.Contains($"好感度: {snapshotFavor}"));
        Check("副 API 的 extra_state_fields 不会漏进主 API prompt", !prompt.Contains("所持金"));
    }

    private static void TestSnapshotCoversAllCharasWhenAsked()
    {
        AiStateSnapshotData snapshot = AiStateSnapshot.Build(CharaA, true, out string error);
        Check($"include_all_charas 时覆盖两个角色（实际 {snapshot.Charas.Count}，{Brief(error)}）",
            snapshot.Charas.Count == 2);

        Install(AllCharasLibraryJson, "全角色快照测试库");
        AiComputeRequest request = AiComputeRequestBuilder.Build(CharaA, "测试事件", 7, out string buildError);
        Check($"装配成功（{Brief(buildError)}）", request != null);
        if (request != null)
        {
            Check("请求里的状态含角色 A", request.StateJson.Contains("爱丽丝"));
            Check("请求里的状态含角色 B", request.StateJson.Contains("贝拉"));
            Check("schema 的 chara_no 枚举含两个角色号",
                request.SchemaJson.Contains("\"enum\":[1,2]"));
        }
        Install(ComputeLibraryJson, "副 API 测试库");
    }

    private static void TestSnapshotSkipsUnregisteredChara()
    {
        AiTraitDiagnostics.Clear();
        AiStateSnapshotData snapshot = AiStateSnapshot.Build(9999, false, out _);
        Check("未登录角色号不会凭空造出角色项", snapshot.Charas.Count == 0);
        Check("未登录角色号留下运行期诊断",
            string.Join(" | ", AiTraitDiagnostics.Entries).Contains("跳过未登录角色号"));

        // 全局字段仍然读得到，所以快照不为空。此时若放行，副 API 会在没有任何角色的情况下被调用，
        // 它给出的 chara_no 只能是猜的。必须在装配阶段就拒绝。
        AiComputeRequest request = AiComputeRequestBuilder.Build(9999, "测试事件", 8, out string buildError);
        Check($"角色不在快照里时拒绝装配请求（{Brief(buildError)}）", request == null);
        Check("拒绝原因指明角色号", buildError != null && buildError.Contains("9999"));
    }

    // ---------- B 组：请求装配与 function schema ----------

    private static void TestRequestCarriesStateAndSchema()
    {
        AiComputeMemory.Clear();
        AiComputeRequest request = AiComputeRequestBuilder.Build(CharaA, "玩家轻轻摸了摸她的头", 42, out string error);
        Check($"装配成功（{Brief(error)}）", request != null);
        if (request == null)
            return;
        requestSample = Dump(request);

        Check($"turn_id 由票号派生（实际 {request.TurnId}）", request.TurnId == "t_000042");
        Check($"可写字段 4 个（实际 {request.Fields.Count}）", request.Fields.Count == 4);
        Check("权威状态含好感度读数", request.StateJson.Contains("\"好感度\": \"50\""));
        Check("schema 是合法 JSON", IsJsonObject(request.SchemaJson));

        Check($"messages 至少含 system 指令 + 权威状态 + 本轮输入（实际 {request.Messages.Count} 条）",
            request.Messages.Count == 3);
        Check("第一条是 system 指令", request.Messages[0].Role == "system"
            && request.Messages[0].Content.Contains("数值结算引擎"));
        Check("权威状态段落带唯一真值来源的措辞",
            request.Messages[1].Content.Contains("【权威状态｜唯一真值来源】"));
        Check("权威状态段落回填了 turn_id", request.Messages[1].Content.Contains("t_000042"));
        Check("最后一条是本轮事件", request.Messages[^1].Role == "user"
            && request.Messages[^1].Content.Contains("摸了摸她的头"));
    }

    private static void TestSchemaEnumeratesOnlyDeclaredFields()
    {
        AiComputeRequest request = AiComputeRequestBuilder.Build(CharaA, "测试事件", 43, out _);
        if (request == null)
        {
            Check("schema 断言前置：装配成功", false);
            return;
        }

        string schema = request.SchemaJson;
        Check("field 用 enum 约束（模型无法引用未声明字段）",
            schema.Contains("\"enum\":[\"好感度\",\"信赖\",\"体力\",\"所持金\"]"));
        Check("op 枚举只含词条库声明过的 add / set",
            schema.Contains("\"add\"") && schema.Contains("\"set\"") && !schema.Contains("\"mul\""));
        Check("chara_no 枚举只含快照里的角色号", schema.Contains("\"enum\":[1]"));
        Check("schema 要求回填 schema_version 与 turn_id",
            schema.Contains("\"required\":[\"schema_version\",\"turn_id\",\"changes\"]"));
        Check("字段说明里带上了单轮幅度上限，让模型先自我约束", schema.Contains("单轮幅度 ≤ 10"));
    }

    private static void TestAuthoritativeStateComesAfterMemory()
    {
        AiComputeMemory.Clear();
        AiComputeMemory.Add("t_000001", "第一轮事件", "好感度 40→45");
        AiComputeMemory.Add("t_000002", "第二轮事件", "好感度 45→50");

        AiComputeRequest request = AiComputeRequestBuilder.Build(CharaA, "第三轮事件", 44, out _);
        if (request == null)
        {
            Check("短记忆断言前置：装配成功", false);
            AiComputeMemory.Clear();
            return;
        }

        // 注意用带方括号的段落标题做锚点：system 指令本身也含「唯一真值来源」这几个字。
        int memoryIndex = IndexOfContent(request.Messages, "第一轮事件");
        int stateIndex = IndexOfContent(request.Messages, "【权威状态");
        Check($"短记忆已进入 messages（第 {memoryIndex} 条）", memoryIndex >= 0);
        Check($"权威状态排在短记忆之后（记忆 {memoryIndex} < 状态 {stateIndex}）",
            memoryIndex >= 0 && stateIndex > memoryIndex);
        Check("短记忆明确标注数值已过时，不得用于推算现值",
            memoryIndex >= 0 && request.Messages[memoryIndex].Content.Contains("已经过时"));
        AiComputeMemory.Clear();
    }

    private static void TestBrokenFieldsAreExcludedAtBuildTime()
    {
        Install(BrokenComputeLibraryJson, "含人为错误的 compute 段");
        AiTraitDiagnostics.Clear();
        AiComputeRequest request = AiComputeRequestBuilder.Build(CharaA, "测试事件", 45, out string error);
        Check($"坏字段被剔除后仍能装配（{Brief(error)}）", request != null);
        if (request != null)
        {
            Check($"7 条坏字段被剔到 3 条（实际 {request.Fields.Count}）", request.Fields.Count == 3);
            Check("缺 target 的字段被剔除", request.FindField("缺目标") == null);
            Check("角色维度变量没带下标的字段被剔除（否则会写到当前 TARGET 身上）",
                request.FindField("裸角色变量") == null);
            Check("声明了不支持操作符的字段被剔除", request.FindField("坏操作符") == null);
            Check("不在白名单里的变量被剔除", request.FindField("非白名单") == null);
            Check("field 重名时只保留先出现的一条", request.FindField("重名") != null);
            Check("写对的字段不受波及", request.FindField("好用的字段") != null);
            Check("坏字段不会出现在下发的 schema 里",
                !request.SchemaJson.Contains("缺目标") && !request.SchemaJson.Contains("非白名单"));
        }
        string diag = string.Join(" | ", AiTraitDiagnostics.Entries);
        Check($"剔除动作留下了运行期诊断（{Brief(diag)}）", diag.Contains("被排除"));
    }

    // ---------- C 组：输出解析 ----------

    private static void TestParserAcceptsWellFormedOutput()
    {
        string json = """
            {
              "schema_version": "1.0",
              "turn_id": "t_000100",
              "changes": [
                { "field": "好感度", "chara_no": 1, "op": "add", "value": 5, "reason": "被摸头后心情变好" }
              ],
              "narrative_hint": "她表面嫌弃但没有躲开",
              "warnings": ["无法确定她是否真的高兴"]
            }
            """;
        AiComputeResult result = AiComputeParser.Parse(json, "t_000100", out string error);
        Check($"合规输出解析成功（{Brief(error)}）", result != null);
        if (result == null)
            return;
        Check("解析出 1 条变更", result.Changes.Count == 1);
        Check("字段名正确", result.Changes[0].Field == "好感度");
        Check("角色号正确", result.Changes[0].CharaNo == 1);
        Check("操作符正确", result.Changes[0].Op == "add");
        Check("数值正确", result.Changes[0].Value == 5);
        Check("理由被保留下来（用于人工复盘）", result.Changes[0].Reason.Contains("摸头"));
        Check("结果提示被保留", result.NarrativeHint.Contains("嫌弃"));
        Check("模型自报的不确定项被保留", result.Warnings.Count == 1);
        Check("原始 JSON 被留存（排查时要能看到模型到底回了什么）", result.RawJson == json);
    }

    private static void TestParserRejectsBadEnvelope()
    {
        Check("空内容被拒绝", AiComputeParser.Parse("", "t_1", out _) == null);
        Check("非法 JSON 被拒绝", AiComputeParser.Parse("{\"changes\":", "t_1", out string e1) == null
            && e1.Contains("合法 JSON"));
        Check("顶层不是对象时被拒绝", AiComputeParser.Parse("[]", "t_1", out string e2) == null
            && e2.Contains("不是 JSON 对象"));
        Check("缺 changes 被拒绝",
            AiComputeParser.Parse("{\"turn_id\":\"t_1\"}", "t_1", out string e3) == null && e3.Contains("缺少 changes"));
        Check("changes 不是数组时被拒绝",
            AiComputeParser.Parse("{\"turn_id\":\"t_1\",\"changes\":{}}", "t_1", out string e4) == null
            && e4.Contains("不是数组"));

        // turn_id 不匹配意味着模型可能回的是上一轮的结果，写下去就是错轮次的数值。
        Check("turn_id 不匹配被拒绝",
            AiComputeParser.Parse("{\"turn_id\":\"t_000009\",\"changes\":[]}", "t_000010", out string e5) == null
            && e5.Contains("turn_id 不匹配"));
        Check("turn_id 缺失被拒绝",
            AiComputeParser.Parse("{\"changes\":[]}", "t_000010", out string e6) == null && e6.Contains("缺失"));
        Check("schema_version 不认识时被拒绝",
            AiComputeParser.Parse("{\"schema_version\":\"9.9\",\"turn_id\":\"t_1\",\"changes\":[]}", "t_1", out string e7) == null
            && e7.Contains("schema_version"));
        Check("空 changes 数组是合法的（本轮无实质事件）",
            AiComputeParser.Parse("{\"schema_version\":\"1.0\",\"turn_id\":\"t_1\",\"changes\":[]}", "t_1", out _)?.Changes.Count == 0);
    }

    private static void TestParserRejectsBadValueTypes()
    {
        Check("value 不是数字时整批拒绝",
            AiComputeParser.Parse("{\"turn_id\":\"t_1\",\"changes\":[{\"field\":\"好感度\",\"op\":\"add\",\"value\":\"很多\"}]}", "t_1", out string e1) == null
            && e1.Contains("不是整数"));
        Check("value 是真小数时整批拒绝（四舍五入等于替模型做决定）",
            AiComputeParser.Parse("{\"turn_id\":\"t_1\",\"changes\":[{\"field\":\"好感度\",\"op\":\"add\",\"value\":3.5}]}", "t_1", out string e2) == null
            && e2.Contains("不是整数"));
        Check("缺 field 时整批拒绝",
            AiComputeParser.Parse("{\"turn_id\":\"t_1\",\"changes\":[{\"op\":\"add\",\"value\":3}]}", "t_1", out string e3) == null
            && e3.Contains("缺少 field"));
        Check("changes 项不是对象时整批拒绝",
            AiComputeParser.Parse("{\"turn_id\":\"t_1\",\"changes\":[1]}", "t_1", out string e4) == null
            && e4.Contains("不是对象"));
    }

    private static void TestParserToleratesModelQuirks()
    {
        AiComputeResult r = AiComputeParser.Parse(
            "{\"turn_id\":\"t_1\",\"changes\":[{\"field\":\"好感度\",\"value\":\"3\"},{\"field\":\"体力\",\"value\":-2.0,\"chara_no\":\"1\"}]}",
            "t_1", out string error);
        Check($"数字写成字符串、带 .0 的整数都能接受（{Brief(error)}）", r != null && r.Changes.Count == 2);
        if (r == null || r.Changes.Count != 2)
            return;
        Check("字符串 \"3\" 被读成 3", r.Changes[0].Value == 3);
        Check("-2.0 被读成 -2", r.Changes[1].Value == -2);
        Check("省略 op 时默认 add", r.Changes[0].Op == "add");
        Check("省略 chara_no 时为 -1（后续由校验层判断该字段是否需要角色号）", r.Changes[0].CharaNo == -1);
        Check("chara_no 写成字符串也能读", r.Changes[1].CharaNo == 1);
    }

    // ---------- D 组：校验与回写 ----------

    private static void TestApplyWritesAndRecordsBefore()
    {
        SetValue(Favor(CharaA), 50);
        AiComputeRequest request = AiComputeRequestBuilder.Build(CharaA, "测试事件", 200, out _);
        AiComputeResult result = MakeResult("t_000200", Change("好感度", CharaA, "add", 5, "被摸头"));
        bool ok = AiComputeApplier.TryApply(request, result, out List<AiAppliedChange> applied, out string error);
        Check($"合规变更写入成功（{Brief(error)}）", ok);
        Check("已落盘 1 项", applied.Count == 1);
        if (applied.Count == 1)
        {
            Check("记录了写入前的值 50（回滚要用）", applied[0].Before == 50);
            Check("记录了写入后的值 55", applied[0].After == 55);
            Check("记录了 target 表达式且 {CHARA} 已换成登录号", applied[0].Target == Favor(CharaA));
        }
        Check($"变量确实变成 55（实际 {ReadValue(Favor(CharaA))}）", ReadValue(Favor(CharaA)) == 55);
        Check("摘要可读", AiComputeApplier.Summarize(applied) == "好感度 50→55");
    }

    private static void TestApplyRejectsOverDelta()
    {
        SetValue(Favor(CharaA), 50);
        AiComputeRequest request = AiComputeRequestBuilder.Build(CharaA, "测试事件", 201, out _);
        AiComputeResult result = MakeResult("t_000201", Change("好感度", CharaA, "add", 50, "编的"));
        bool ok = AiComputeApplier.TryApply(request, result, out _, out string error);
        Check($"超过单轮幅度上限被整批拒绝（{Brief(error)}）", !ok && error.Contains("超过上限"));
        Check("被拒绝时数值一个字节都没改", ReadValue(Favor(CharaA)) == 50);

        // 幅度上限对负向变化同样生效，否则"好感度 -80"这类幻觉能直接过关。
        AiComputeResult negative = MakeResult("t_000201", Change("好感度", CharaA, "add", -30, "编的"));
        Check("负向超幅同样被拒绝", !AiComputeApplier.TryApply(request, negative, out _, out _));
        Check("负向被拒后数值未变", ReadValue(Favor(CharaA)) == 50);
    }

    private static void TestApplyRejectsUndeclaredFieldAndOp()
    {
        SetValue(Favor(CharaA), 50);
        AiComputeRequest request = AiComputeRequestBuilder.Build(CharaA, "测试事件", 202, out _);

        bool undeclared = AiComputeApplier.TryApply(
            request, MakeResult("t_000202", Change("体重", CharaA, "add", 1, "臆造字段")), out _, out string e1);
        Check($"未声明字段被整批拒绝（{Brief(e1)}）", !undeclared && e1.Contains("未声明的字段"));

        bool badOp = AiComputeApplier.TryApply(
            request, MakeResult("t_000202", Change("好感度", CharaA, "mul", 2, "该字段只允许 add")), out _, out string e2);
        Check($"字段未授权的操作符被整批拒绝（{Brief(e2)}）", !badOp && e2.Contains("不允许操作符"));

        bool unknownOp = AiComputeApplier.TryApply(
            request, MakeResult("t_000202", Change("好感度", CharaA, "divide", 2, "根本不存在的操作符")), out _, out string e3);
        Check($"引擎不支持的操作符被整批拒绝（{Brief(e3)}）", !unknownOp);
        Check("以上三次拒绝都没有改动数值", ReadValue(Favor(CharaA)) == 50);
    }

    private static void TestApplyRejectsDuplicateAndBadChara()
    {
        SetValue(Favor(CharaA), 50);
        AiComputeRequest request = AiComputeRequestBuilder.Build(CharaA, "测试事件", 203, out _);

        bool dup = AiComputeApplier.TryApply(
            request,
            MakeResult("t_000203",
                Change("好感度", CharaA, "add", 3, "第一次"),
                Change("好感度", CharaA, "add", 4, "同一轮又改一次")),
            out _, out string e1);
        Check($"同一轮重复改同一变量被整批拒绝（{Brief(e1)}）", !dup && e1.Contains("重复改动"));

        bool noChara = AiComputeApplier.TryApply(
            request, MakeResult("t_000203", Change("好感度", -1, "add", 3, "漏了角色号")), out _, out string e2);
        Check($"角色维度字段缺 chara_no 被整批拒绝（{Brief(e2)}）", !noChara && e2.Contains("chara_no"));

        bool badChara = AiComputeApplier.TryApply(
            request, MakeResult("t_000203", Change("好感度", 9999, "add", 3, "不存在的角色")), out _, out string e3);
        Check($"未登录角色号被整批拒绝（{Brief(e3)}）", !badChara && e3.Contains("未登录"));
        Check("以上三次拒绝都没有改动数值", ReadValue(Favor(CharaA)) == 50);
    }

    private static void TestApplyRespectsMaxChanges()
    {
        SetValue(Favor(CharaA), 50);
        AiComputeRequest request = AiComputeRequestBuilder.Build(CharaA, "测试事件", 204, out _);
        AiComputeResult result = MakeResult("t_000204",
            Change("好感度", CharaA, "add", 1, ""),
            Change("信赖", CharaA, "add", 1, ""),
            Change("体力", CharaA, "add", 1, ""),
            Change("所持金", -1, "add", 1, ""),
            Change("好感度", CharaB, "add", 1, ""));
        bool ok = AiComputeApplier.TryApply(request, result, out _, out string error);
        Check($"一轮变更数超过 max_changes=4 时整批拒绝（{Brief(error)}）", !ok && error.Contains("超过上限"));
        Check("被拒绝时数值未变", ReadValue(Favor(CharaA)) == 50);
    }

    private static void TestApplyClampsOutOfRange()
    {
        SetValue(Favor(CharaA), 95);
        AiComputeRequest request = AiComputeRequestBuilder.Build(CharaA, "测试事件", 205, out _);
        AiTraitDiagnostics.Clear();
        bool ok = AiComputeApplier.TryApply(
            request, MakeResult("t_000205", Change("好感度", CharaA, "add", 10, "接近满值时继续上涨")),
            out List<AiAppliedChange> applied, out string error);
        Check($"on_out_of_range=clamp 时越界被钳到边界而不是整批拒绝（{Brief(error)}）", ok && applied.Count == 1);
        Check($"结果被钳到 max=100（实际 {ReadValue(Favor(CharaA))}）", ReadValue(Favor(CharaA)) == 100);
        Check("钳制留下了运行期诊断",
            string.Join(" | ", AiTraitDiagnostics.Entries).Contains("已钳到"));
        SetValue(Favor(CharaA), 50);
    }

    private static void TestApplyRejectsOutOfRangeWhenConfigured()
    {
        Install(RejectLibraryJson, "越界即拒绝的测试库");
        SetValue(Favor(CharaA), 50);
        AiComputeRequest request = AiComputeRequestBuilder.Build(CharaA, "测试事件", 206, out string buildError);
        Check($"装配成功（{Brief(buildError)}）", request != null);
        if (request != null)
        {
            bool ok = AiComputeApplier.TryApply(
                request, MakeResult("t_000206", Change("好感度", CharaA, "add", 10, "越过 max=55")),
                out _, out string error);
            Check($"on_out_of_range=reject 时越界整批拒绝（{Brief(error)}）", !ok && error.Contains("超出允许区间"));
            Check("被拒绝时数值未变", ReadValue(Favor(CharaA)) == 50);
        }
        Install(ComputeLibraryJson, "副 API 测试库");
    }

    private static void TestApplyRejectsInvertedRange()
    {
        Install(InvertedRangeLibraryJson, "区间写反的测试库");
        SetValue(Favor(CharaA), 50);
        AiComputeRequest request = AiComputeRequestBuilder.Build(CharaA, "测试事件", 207, out _);
        if (request == null)
        {
            Check("区间写反的断言前置：装配成功", false);
            Install(ComputeLibraryJson, "副 API 测试库");
            return;
        }
        bool ok = AiComputeApplier.TryApply(
            request, MakeResult("t_000207", Change("好感度", CharaA, "add", 1, "任何值都会越界")),
            out _, out string error);
        Check($"min > max 时任何值都被拒绝，而不是被钳到奇怪的值（{Brief(error)}）", !ok);
        Check("区间写反时数值未被改动", ReadValue(Favor(CharaA)) == 50);
        Check("静态校验已经提前报出区间写反",
            string.Join(" | ", AiTraitLibrary.Diagnostics).Contains("大于 max"));
        Install(ComputeLibraryJson, "副 API 测试库");
    }

    private static void TestApplyHandlesGlobalField()
    {
        SetValue("MONEY:0", 5000);
        AiComputeRequest request = AiComputeRequestBuilder.Build(CharaA, "测试事件", 208, out _);
        bool ok = AiComputeApplier.TryApply(
            request, MakeResult("t_000208", Change("所持金", -1, "add", -300, "买了点东西")),
            out List<AiAppliedChange> applied, out string error);
        Check($"全局字段（不带 {{CHARA}}）能正常写入（{Brief(error)}）", ok && applied.Count == 1);
        Check($"所持金 5000 → 4700（实际 {ReadValue("MONEY:0")}）", ReadValue("MONEY:0") == 4700);
        Check("全局字段忽略 chara_no", applied.Count == 1 && applied[0].Target == "MONEY:0");
        SetValue("MONEY:0", 5000);
    }

    private static void TestRollbackRestoresValues()
    {
        SetValue(Favor(CharaA), 50);
        SetValue("MONEY:0", 5000);
        AiComputeRequest request = AiComputeRequestBuilder.Build(CharaA, "测试事件", 209, out _);
        AiComputeApplier.TryApply(
            request,
            MakeResult("t_000209",
                Change("好感度", CharaA, "add", 5, ""),
                Change("所持金", -1, "add", -100, "")),
            out List<AiAppliedChange> applied, out _);
        Check("回滚前两项都已写入", ReadValue(Favor(CharaA)) == 55 && ReadValue("MONEY:0") == 4900);

        bool ok = AiComputeApplier.TryRollback(applied, out string error);
        Check($"回滚成功（{Brief(error)}）", ok);
        Check($"好感度回到 50（实际 {ReadValue(Favor(CharaA))}）", ReadValue(Favor(CharaA)) == 50);
        Check($"所持金回到 5000（实际 {ReadValue("MONEY:0")}）", ReadValue("MONEY:0") == 5000);
        Check("空回滚是安全的空操作", AiComputeApplier.TryRollback([], out _));
    }

    // ---------- E 组：短记忆窗口 ----------

    private static void TestMemoryKeepsAtMostFiveRounds()
    {
        AiComputeMemory.Clear();
        for (int i = 1; i <= 7; i++)
            AiComputeMemory.Add($"t_{i:D6}", $"第 {i} 轮事件", $"好感度 {i}→{i + 1}");
        Check($"短记忆最多保留 {AiComputeMemory.MaxRounds} 轮（实际 {AiComputeMemory.All.Count}）",
            AiComputeMemory.All.Count == AiComputeMemory.MaxRounds);
        Check("保留的是最近几轮", AiComputeMemory.All[^1].TurnId == "t_000007");
        Check("最早的几轮已被挤出", AiComputeMemory.All[0].TurnId == "t_000003");
        Check("Recent(2) 取最近两轮", AiComputeMemory.Recent(2).Count == 2
            && AiComputeMemory.Recent(2)[0].TurnId == "t_000006");
        Check("Recent(0) 返回空", AiComputeMemory.Recent(0).Count == 0);
    }

    private static void TestMemoryEntersPromptAsStaleContext()
    {
        AiComputeRequest request = AiComputeRequestBuilder.Build(CharaA, "本轮事件", 300, out _);
        if (request == null)
        {
            Check("短记忆截断断言前置：装配成功", false);
            AiComputeMemory.Clear();
            return;
        }
        int memoryIndex = IndexOfContent(request.Messages, "仅供理解剧情走向");
        Check("短记忆段落已进入 messages", memoryIndex >= 0);
        if (memoryIndex >= 0)
        {
            string memory = request.Messages[memoryIndex].Content;
            Check("memory_rounds=3 时只带最近 3 轮", CountOccurrences(memory, "- [t_") == 3);
            Check("带的是最近 3 轮而不是最早 3 轮",
                memory.Contains("t_000007") && !memory.Contains("t_000004"));
        }
        Check("短记忆不含权威数值字段名（存了就一定有人拿它当现值用）",
            memoryIndex < 0 || !request.Messages[memoryIndex].Content.Contains("权威状态"));
        AiComputeMemory.Clear();
    }

    // ---------- F 组：配置写错必须报错或安全降级 ----------

    private static void TestMissingComputeSectionSkipsQuietly()
    {
        Install(NoComputeLibraryJson, "没有 compute 段的词条库");
        Check("没有 compute 段时 ComputeTemplate 为 null", AiTraitLibrary.ComputeTemplate == null);
        AiComputeRequest request = AiComputeRequestBuilder.Build(CharaA, "测试事件", 400, out string error);
        Check($"没有 compute 段时拒绝装配并说明原因（{Brief(error)}）", request == null && error.Contains("没有 compute 段"));
        Check("没有 compute 段不算配置错误，静态校验不该因此报错", AiTraitLibrary.Diagnostics.Count == 0);

        // 主 API 不受影响：这是"只有叙事、不动数值"的正常降级形态。
        AiPromptBuilder.Build(CharaA, out AiPromptBuildInfo info);
        Check("没有 compute 段时主 API 的词条 prompt 照常工作", info.UsedTraits);
    }

    private static void TestDisabledComputeSectionSkips()
    {
        Install(DisabledComputeLibraryJson, "停用 compute 的词条库");
        Check("compute 段仍被解析出来", AiTraitLibrary.ComputeTemplate != null);
        AiComputeRequest request = AiComputeRequestBuilder.Build(CharaA, "测试事件", 401, out string error);
        Check($"enabled=false 时拒绝装配并说明原因（{Brief(error)}）",
            request == null && error.Contains("enabled = false"));
    }

    private static void TestStaticValidationCatchesComputeMistakes()
    {
        Install(BrokenComputeLibraryJson, "含人为错误的 compute 段");
        string all = string.Join(" | ", AiTraitLibrary.Diagnostics);
        Check($"静态校验：报出 field 重名（{Brief(all)}）", all.Contains("field 名重复"));
        Check("静态校验：报出缺 target", all.Contains("缺少 target"));
        Check("静态校验：报出 min > max", all.Contains("大于 max"));
        Check("静态校验：报出角色维度变量缺下标", all.Contains("必须带角色下标"));
        Check("静态校验：报出不支持的操作符", all.Contains("不支持的操作符"));
        Check("静态校验：报出不在白名单的变量", all.Contains("不在白名单"));
        Check("静态校验：报出 max_changes<=0 会拒绝一切变更", all.Contains("任何变更都会被拒绝"));
        Check("静态校验：报出 memory_rounds 超上限", all.Contains("超过上限"));
        Check("静态校验：报出 on_out_of_range 取值无法识别", all.Contains("on_out_of_range"));
    }

    // ---------- H 组：玩家手动调整与撤销 ----------

    /// <summary>
    /// 可手改范围必须与副 API 完全一致。两边各写一份配置，早晚会出现
    /// 「模型能改但玩家改不了」或反过来的情况，而那种不一致没有任何报错。
    /// </summary>
    private static void TestManualEditableMirrorsWritableFields()
    {
        List<AiEditableEntry> entries = AiManualEditor.CollectEditable(out string error);
        Check($"能列出可手改条目（{Brief(error)}）", entries.Count > 0);

        var fieldNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (AiEditableEntry entry in entries)
            fieldNames.Add(entry.Field.Field);
        Check("可手改字段与 writable_fields 一致（4 个）", fieldNames.Count == 4);
        Check("未声明的字段不会出现在可手改列表里", !fieldNames.Contains("体重"));

        Check("角色维度字段对每个已登录角色各列一条",
            CountEntries(entries, "好感度", CharaA) == 1 && CountEntries(entries, "好感度", CharaB) == 1);
        Check("全局字段只列一条且不带角色号", CountEntries(entries, "所持金", -1) == 1);
        Check("列出的是别的角色也能改，而不是只有当前 TARGET",
            FindEntry(entries, "好感度", CharaB) != null);

        AiEditableEntry favorB = FindEntry(entries, "好感度", CharaB);
        Check($"读到的当前值是真实值（角色 B 好感度 10，实际 {favorB?.Current}）", favorB != null && favorB.Current == 10);
        Check("角色维度条目的显示名带角色名", favorB != null && favorB.DisplayName.Contains("贝拉"));
    }

    /// <summary>
    /// 这是本组的核心断言：玩家的调整**不受** max_delta 与 min/max 约束。
    /// 那两道闸门是为了挡模型幻觉而设的，模型不知道自己在胡说，玩家知道自己在做什么。
    /// </summary>
    private static void TestManualEditIgnoresDeltaAndRange()
    {
        List<AiEditableEntry> entries = AiManualEditor.CollectEditable(out _);
        AiEditableEntry favor = FindEntry(entries, "好感度", CharaA);
        if (favor == null)
        {
            Check("手改前置：找到角色 A 的好感度条目", false);
            return;
        }

        // 好感度的 max_delta 是 10，副 API 从 50 改到 99 一定被整批拒绝。
        long delta = 99 - favor.Current;
        Check($"这个改动幅度对副 API 是超限的（{delta} > {favor.Field.MaxDelta}）", delta > favor.Field.MaxDelta);

        int memoryBefore = AiComputeMemory.All.Count;
        bool ok = AiManualEditor.TryApply(
            [new AiManualEdit { Field = favor.Field, CharaNo = CharaA, Value = 99 }],
            out List<AiAppliedChange> applied, out string error);
        Check($"手改超出单轮幅度上限仍然成功（{Brief(error)}）", ok);
        Check($"好感度被改成 99（实际 {ReadValue(Favor(CharaA))}）", ReadValue(Favor(CharaA)) == 99);
        Check("手改记录了写入前的值，所以同样可撤销",
            applied.Count == 1 && applied[0].Before == 50 && applied[0].After == 99);
        Check("手改进了短记忆（否则副 API 下一轮会试图圆这个跳变）",
            AiComputeMemory.All.Count == memoryBefore + 1);
        Check("短记忆写明了这是玩家手动调整",
            AiComputeMemory.All.Count > 0 && AiComputeMemory.All[^1].EventText.Contains("手动调整"));

        // 越出 min/max 同样放行：设计区间是给模型的参考，不是给玩家的锁。
        ok = AiManualEditor.TryApply(
            [new AiManualEdit { Field = favor.Field, CharaNo = CharaA, Value = 9999 }],
            out _, out error);
        Check($"手改越出设计区间仍然成功（{Brief(error)}）", ok);
        Check($"好感度被改成 9999（实际 {ReadValue(Favor(CharaA))}）", ReadValue(Favor(CharaA)) == 9999);

        SetValue(Favor(CharaA), 50);
    }

    /// <summary>允许作弊不等于允许写坏存档：引擎级校验对玩家一视同仁。</summary>
    private static void TestManualEditStillRespectsEngineChecks()
    {
        long before = ReadValue(Favor(CharaA));

        var brokenField = new AiComputeField { Field = "不存在的变量", Target = "CFLAG:{CHARA}:根本没有这个下标" };
        bool ok = AiManualEditor.TryApply(
            [new AiManualEdit { Field = brokenField, CharaNo = CharaA, Value = 1 }],
            out _, out string error);
        Check($"写不进去的变量被拒绝而不是静默失败（{Brief(error)}）", !ok);
        Check("被拒时数值未改动", ReadValue(Favor(CharaA)) == before);

        List<AiEditableEntry> entries = AiManualEditor.CollectEditable(out _);
        AiEditableEntry favor = FindEntry(entries, "好感度", CharaA);
        ok = AiManualEditor.TryApply(
            [new AiManualEdit { Field = favor.Field, CharaNo = 9999, Value = 1 }],
            out _, out error);
        Check($"未登录角色号被拒绝（{Brief(error)}）", !ok);

        ok = AiManualEditor.TryApply(
            [new AiManualEdit { Field = favor.Field, CharaNo = CharaA, Value = before }],
            out List<AiAppliedChange> applied, out error);
        Check($"填的值与当前值相同时不产生变更（{Brief(error)}）", ok && applied.Count == 0);
    }

    /// <summary>手改也走 AiAppliedChange，所以回滚机制原样适用。</summary>
    private static void TestManualEditIsReversible()
    {
        List<AiEditableEntry> entries = AiManualEditor.CollectEditable(out _);
        AiEditableEntry money = FindEntry(entries, "所持金", -1);
        if (money == null)
        {
            Check("手改回滚前置：找到所持金条目", false);
            return;
        }

        bool ok = AiManualEditor.TryApply(
            [new AiManualEdit { Field = money.Field, CharaNo = -1, Value = 123456 }],
            out List<AiAppliedChange> applied, out string error);
        Check($"全局字段手改成功（{Brief(error)}）", ok && applied.Count == 1);
        Check($"所持金变成 123456（实际 {ReadValue("MONEY:0")}）", ReadValue("MONEY:0") == 123456);

        Check($"手改可以原样回滚（{Brief(error)}）", AiComputeApplier.TryRollback(applied, out error));
        Check($"所持金回到 5000（实际 {ReadValue("MONEY:0")}）", ReadValue("MONEY:0") == 5000);
        AiComputeMemory.Clear();
    }

    private static AiEditableEntry FindEntry(List<AiEditableEntry> entries, string field, long charaNo)
    {
        foreach (AiEditableEntry entry in entries)
        {
            if (string.Equals(entry.Field.Field, field, StringComparison.Ordinal) && entry.CharaNo == charaNo)
                return entry;
        }
        return null;
    }

    private static int CountEntries(List<AiEditableEntry> entries, string field, long charaNo)
    {
        int count = 0;
        foreach (AiEditableEntry entry in entries)
        {
            if (string.Equals(entry.Field.Field, field, StringComparison.Ordinal) && entry.CharaNo == charaNo)
                count++;
        }
        return count;
    }

    // ---------- G 组：主副 API 串联 ----------

    private static int chainStage;
    private static string mainPromptSeen;
    private static string mainHintSeen;
    private static int computeCallCount;
    private static int computeCallsBeforeRegenerate;
    private static int memoryBeforeRegenerate;
    private static bool discardTested;

    private static void StartDualChannelChain()
    {
        AiDispatcher.ClearHistory();
        AiDispatcher.UseFakeBackend = false;
        AiDispatcher.TurnCompleted += OnChainCompleted;
        SetValue(Favor(CharaA), 50);

        // 副 API 替身：直接给出一份合规的 function call 参数，把 turn_id 原样回填。
        AiDispatcher.ComputeBackendOverride = request =>
        {
            computeCallCount++;
            return $$"""
                {
                  "schema_version": "1.0",
                  "turn_id": "{{request.TurnId}}",
                  "changes": [
                    { "field": "好感度", "chara_no": 1, "op": "add", "value": 5, "reason": "自检替身" }
                  ],
                  "narrative_hint": "她的态度略有软化"
                }
                """;
        };

        // 主 API 替身：把看到的 system prompt 与结算提示留下来，供断言「先算后叙」。
        AiDispatcher.MainBackendOverride = messages =>
        {
            mainPromptSeen = FirstContent(messages, "system");
            mainHintSeen = LastSystemContent(messages);
            return "[自检替身] 叙事正文";
        };

        chainStage = 1;
        Check("发起双通道请求成功", AiDispatcher.TryBeginTurn(target, "玩家轻轻摸了摸她的头"));
    }

    private static void OnChainCompleted(AiTurnResult result)
    {
        try
        {
            switch (chainStage)
            {
                case 1:
                    ChainStage1Normal(result);
                    return;
                case 2:
                    ChainStage2ComputeFailureDegrades(result);
                    return;
                case 3:
                    ChainStage3BadTurnIdRejected(result);
                    return;
                case 4:
                    ChainStage4NarrativeFailureLeavesTransaction(result);
                    return;
                case 5:
                    ChainStage5RegenerateKeepsValues(result);
                    return;
                case 6:
                    ChainStage6RollbackPending(result);
                    return;
                case 7:
                    ChainStage7DiscardKeepsValues(result);
                    return;
                case 8:
                    ChainStage8AbortRollsBack(result);
                    return;
            }
        }
        catch (Exception e)
        {
            Log("FATAL", $"串联自检抛出异常：{e}");
        }

        AiDispatcher.TurnCompleted -= OnChainCompleted;
        RestoreAll();
        Finish();
    }

    /// <summary>正常一轮：副 API 先写数值，主 API 的 prompt 必须已经读到新值。</summary>
    private static void ChainStage1Normal(AiTurnResult result)
    {
        Check($"本轮成功（{Brief(result.ErrorMessage)}）", result.Success);
        Check("副 API 被调用了一次", computeCallCount == 1);
        Check($"数值已回写 1 项（实际 {result.ComputeApplied.Count}）", result.ComputeApplied.Count == 1);
        Check($"好感度 50 → 55（实际 {ReadValue(Favor(CharaA))}）", ReadValue(Favor(CharaA)) == 55);
        Check("本轮没有跳过副 API", result.ComputeSkipReason == null);

        // 这是 P3 时序的核心断言：主 API 的 system prompt 必须含回写后的新值 55 而不是旧值 50。
        Check("主 API 的 prompt 读到的是回写后的新值 55",
            mainPromptSeen != null && mainPromptSeen.Contains("好感度: 55"));
        Check("主 API 的 prompt 没有停留在旧值 50", mainPromptSeen != null && !mainPromptSeen.Contains("好感度: 50"));
        Check("结算提示已作为独立 system 消息紧贴本轮输入",
            mainHintSeen != null && mainHintSeen.Contains("态度略有软化"));
        Check("结算提示要求正文不写具体数字", mainHintSeen != null && mainHintSeen.Contains("不要写出具体数字"));
        Check("副 API 结果提示进了 AiTurnResult", result.ComputeHint != null && result.ComputeHint.Contains("软化"));
        Check("观测信息记录了本轮往返", AiDispatcher.LastComputeInfo != null
            && AiDispatcher.LastComputeInfo.Applied.Count == 1);
        Check("本轮已写入短记忆", AiComputeMemory.All.Count == 1);

        // 撤销上一轮结算：正文与对话历史不动，只把数值退回去。
        // 面向「这轮叙事我喜欢，但结算不合理」——两条通道分开，没理由为改一个数字连正文一起重来。
        int historyBeforeUndo = AiDispatcher.History.Count;
        bool undone = AiDispatcher.TryUndoLastComputeApply(out string undoError);
        Check($"撤销上一轮结算成功（{Brief(undoError)}）", undone);
        Check($"好感度退回 50（实际 {ReadValue(Favor(CharaA))}）", ReadValue(Favor(CharaA)) == 50);
        Check("撤销不动对话历史（正文照旧保留）", AiDispatcher.History.Count == historyBeforeUndo);
        Check("撤销进了短记忆（否则副 API 下一轮会试图圆这个跳变）", AiComputeMemory.All.Count == 2);
        Check("短记忆写明了这是玩家撤销",
            AiComputeMemory.All[^1].EventText.Contains("撤销"));
        Check("重复撤销被拒绝，不会把更早的值写回去",
            !AiDispatcher.TryUndoLastComputeApply(out _));

        // 阶段二：副 API 调用失败，主 API 必须照常出正文，数值不动。
        chainStage = 2;
        SetValue(Favor(CharaA), 50);
        AiDispatcher.ComputeBackendOverride = _ =>
        {
            computeCallCount++;
            throw new InvalidOperationException("自检：模拟副 API 超时");
        };
        Check("发起副 API 失败的一轮成功", AiDispatcher.TryBeginTurn(target, "副 API 失败的一轮"));
    }

    private static void ChainStage2ComputeFailureDegrades(AiTurnResult result)
    {
        Check($"副 API 失败时整轮仍然成功（叙事优先，{Brief(result.ErrorMessage)}）", result.Success);
        Check("正文照常产出", result.NarrativeText.Contains("叙事正文"));
        Check($"本轮跳过了数值（{Brief(result.ComputeSkipReason)}）", result.ComputeSkipReason != null);
        Check("跳过原因指明是副 API 调用失败", result.ComputeSkipReason.Contains("副 API 调用失败"));
        Check("副 API 失败时数值一个字节都没改", ReadValue(Favor(CharaA)) == 50);
        Check("没有留下待处置事务", AiDispatcher.PendingTransaction == null);
        Check("主 API 没有收到结算提示", mainHintSeen == null || !mainHintSeen.Contains("结算结果"));
        Check("本轮没写入数值时撤销被拒绝而不是静默成功",
            !AiDispatcher.TryUndoLastComputeApply(out _));

        // 阶段三：副 API 回了别的轮次的 turn_id，必须整批拒绝。
        chainStage = 3;
        AiDispatcher.ComputeBackendOverride = _ =>
        {
            computeCallCount++;
            return """
                {
                  "schema_version": "1.0",
                  "turn_id": "t_999999",
                  "changes": [ { "field": "好感度", "chara_no": 1, "op": "add", "value": 5 } ]
                }
                """;
        };
        Check("发起 turn_id 错位的一轮成功", AiDispatcher.TryBeginTurn(target, "turn_id 错位的一轮"));
    }

    private static void ChainStage3BadTurnIdRejected(AiTurnResult result)
    {
        Check("turn_id 错位时整轮仍然成功", result.Success);
        Check($"数值被拒（{Brief(result.ComputeSkipReason)}）",
            result.ComputeSkipReason != null && result.ComputeSkipReason.Contains("turn_id 不匹配"));
        Check("turn_id 错位时数值未被改动", ReadValue(Favor(CharaA)) == 50);

        // 阶段四：数值写成功但主 API 失败（RISK-05）。
        chainStage = 4;
        AiDispatcher.ComputeBackendOverride = request =>
        {
            computeCallCount++;
            return $$"""
                {
                  "schema_version": "1.0",
                  "turn_id": "{{request.TurnId}}",
                  "changes": [ { "field": "好感度", "chara_no": 1, "op": "add", "value": 5, "reason": "自检替身" } ],
                  "narrative_hint": "她的态度略有软化"
                }
                """;
        };
        AiDispatcher.MainBackendOverride = _ => throw new InvalidOperationException("自检：模拟主 API 失败");
        Check("发起主 API 失败的一轮成功", AiDispatcher.TryBeginTurn(target, "主 API 失败的一轮"));
    }

    private static void ChainStage4NarrativeFailureLeavesTransaction(AiTurnResult result)
    {
        Check("主 API 失败时整轮报告失败", !result.Success);
        Check("标记了「数值已写但正文失败」", result.NarrativeFailedAfterApply);
        Check($"数值确实已经落盘（实际 {ReadValue(Favor(CharaA))}）", ReadValue(Favor(CharaA)) == 55);
        Check("没有自动回滚（已经发生的事不该凭空消失）", !result.ComputeRolledBack);

        AiPendingTransaction pending = AiDispatcher.PendingTransaction;
        Check("留下了待处置事务", pending != null);
        Check("事务里保留了写入前的值，回滚才有依据",
            pending != null && pending.Applied.Count == 1 && pending.Applied[0].Before == 50);
        Check("事务里保留了本轮输入与 prompt，重生成才有依据",
            pending != null && pending.UserInput.Contains("主 API 失败") && !string.IsNullOrEmpty(pending.SystemPrompt));

        // 这是本次加固的核心断言：PendingTransaction 只有一份，放行新请求会把它连同
        // Before 快照一起覆盖，那批已落盘的数值就再也回滚不了。宁可挡住这一轮。
        long favorBeforeBlocked = ReadValue(Favor(CharaA));
        Check("有未处置事务时新请求被拒绝（否则事务会被覆盖，数值失去可撤回性）",
            !AiDispatcher.TryBeginTurn(target, "这一轮不该被放行"));
        Check("被拒的新请求没有覆盖事务", AiDispatcher.PendingTransaction == pending);
        Check("被拒的新请求没有改动数值", ReadValue(Favor(CharaA)) == favorBeforeBlocked);
        Check("被拒的新请求没有占住锁", !AiRequestLock.IsLocked);
        Check("有未处置事务时撤销上一轮也被拒（该先处置事务）",
            !AiDispatcher.TryUndoLastComputeApply(out _));

        // 阶段五：仅重生成正文，数值保持不变。
        chainStage = 5;
        computeCallsBeforeRegenerate = computeCallCount;
        memoryBeforeRegenerate = AiComputeMemory.All.Count;
        AiDispatcher.MainBackendOverride = messages =>
        {
            mainPromptSeen = FirstContent(messages, "system");
            return "[自检替身] 重生成的正文";
        };
        Check("重生成被接受", AiDispatcher.TryRegenerateNarrative(target));
    }

    private static void ChainStage5RegenerateKeepsValues(AiTurnResult result)
    {
        Check($"重生成成功（{Brief(result.ErrorMessage)}）", result.Success);
        Check("拿到了新的正文", result.NarrativeText.Contains("重生成的正文"));
        Check($"重生成不再动数值（仍为 55，实际 {ReadValue(Favor(CharaA))}）", ReadValue(Favor(CharaA)) == 55);
        Check($"重生成没有再调一次副 API（调用次数仍为 {computeCallCount}）",
            computeCallCount == computeCallsBeforeRegenerate);
        Check("待处置事务已清除", AiDispatcher.PendingTransaction == null);
        Check($"重生成的这轮补进了短记忆（{memoryBeforeRegenerate} → {AiComputeMemory.All.Count}）",
            AiComputeMemory.All.Count == memoryBeforeRegenerate + 1);

        // 阶段六：再制造一次主 API 失败，然后选择回滚。
        chainStage = 6;
        SetValue(Favor(CharaA), 50);
        AiDispatcher.MainBackendOverride = _ => throw new InvalidOperationException("自检：再一次主 API 失败");
        Check("发起第二次主 API 失败的一轮成功", AiDispatcher.TryBeginTurn(target, "准备回滚的一轮"));
    }

    private static void ChainStage6RollbackPending(AiTurnResult result)
    {
        Check("再次留下待处置事务", AiDispatcher.PendingTransaction != null);
        Check($"数值已写入（实际 {ReadValue(Favor(CharaA))}）", ReadValue(Favor(CharaA)) == 55);

        bool ok = AiDispatcher.TryRollbackPending(out string error);
        Check($"回滚待处置事务成功（{Brief(error)}）", ok);
        Check($"数值回到请求前的 50（实际 {ReadValue(Favor(CharaA))}）", ReadValue(Favor(CharaA)) == 50);
        Check("回滚后事务已清除", AiDispatcher.PendingTransaction == null);
        Check("没有事务时回滚会被拒绝而不是静默成功", !AiDispatcher.TryRollbackPending(out _));
        Check("事务处置完之后新请求恢复放行", AiDispatcher.PendingTransaction == null);

        // 阶段七：第三条出路——保留数值、放弃正文。
        // 既然有未处置事务时会挡住新请求，就必须留一个「我认了，往下玩」的退出口，
        // 否则玩家会被自己的选择卡死在这一轮。
        chainStage = 7;
        SetValue(Favor(CharaA), 50);
        AiDispatcher.MainBackendOverride = _ => throw new InvalidOperationException("自检：第三次主 API 失败");
        Check("发起用于测试「保留数值」的一轮成功", AiDispatcher.TryBeginTurn(target, "准备保留数值的一轮"));
    }

    private static void ChainStage7DiscardKeepsValues(AiTurnResult result)
    {
        Check("第三次也留下了待处置事务", AiDispatcher.PendingTransaction != null);
        Check($"数值已写入（实际 {ReadValue(Favor(CharaA))}）", ReadValue(Favor(CharaA)) == 55);

        string turnId = AiDispatcher.PendingTransaction?.TurnId;
        bool ok = AiDispatcher.TryDiscardPending(out string error);
        Check($"放弃事务但保留数值成功（{Brief(error)}）", ok);
        Check($"数值保持在 55（实际 {ReadValue(Favor(CharaA))}）", ReadValue(Favor(CharaA)) == 55);
        Check("事务已清除", AiDispatcher.PendingTransaction == null);

        // 断言最后一条而不是条数：短记忆是 5 轮滚动窗口，跑到这一步早就满了，
        // 用 count + 1 判断会在窗口饱和后失效（这正是它第一次红的原因）。
        Check("保留数值这一轮补进了短记忆（数值留着就等于这一轮算发生过）",
            AiComputeMemory.All.Count > 0 && AiComputeMemory.All[^1].TurnId == turnId);
        Check("没有事务时放弃会被拒绝而不是静默成功", !AiDispatcher.TryDiscardPending(out _));
        discardTested = ok;

        // 阶段八：请求中途被玩家终止，已写入的数值必须撤回。
        chainStage = 8;
        SetValue(Favor(CharaA), 50);
        AiDispatcher.MainBackendOverride = _ =>
        {
            System.Threading.Thread.Sleep(700);
            return "[自检替身] 终止前不该被采用的正文";
        };
        Check("发起将被终止的一轮成功", AiDispatcher.TryBeginTurn(target, "将被终止的一轮"));
        StartAbortTimer();
    }

    /// <summary>
    /// 终止必须在副 API 已经写完、主 API 还没返回的窗口里发出。
    /// 用界面线程计时器延时触发，而不是原地 Sleep——原地睡会把界面线程堵住，
    /// 副 API 的回写要回到界面线程执行，那样就永远等不到它写完。
    /// </summary>
    private static void StartAbortTimer()
    {
        abortTimer = new System.Windows.Forms.Timer { Interval = 350 };
        abortTimer.Tick += (s, e) =>
        {
            abortTimer.Stop();
            Check($"终止前副 API 已完成回写（实际 {ReadValue(Favor(CharaA))}）", ReadValue(Favor(CharaA)) == 55);
            Check("终止请求被接受", AiDispatcher.Abort());
        };
        abortTimer.Start();
    }

    private static void ChainStage8AbortRollsBack(AiTurnResult result)
    {
        Check("终止的一轮标记为 Aborted", result.Aborted);
        Check("终止的一轮不报告成功", !result.Success);
        Check("终止时已写入的数值被撤回", result.ComputeRolledBack);
        Check($"好感度回到 50（实际 {ReadValue(Favor(CharaA))}）", ReadValue(Favor(CharaA)) == 50);
        Check("终止后锁已释放", !AiRequestLock.IsLocked);
        Check("终止后没有遗留待处置事务", AiDispatcher.PendingTransaction == null);
        Check("「保留数值」这条出路已验证过（防止链路提前短路跳过阶段七）", discardTested);

        chainStage = 9;
        AiDispatcher.TurnCompleted -= OnChainCompleted;
        RestoreAll();
        Finish();
    }

    // ---------- 工具 ----------

    private static long Register(long charaNo) => GlobalStatic.VEvaluator.GetChara(charaNo);

    private static string Favor(long charaNo) => $"CFLAG:{Register(charaNo)}:好感度";

    private static string Trust(long charaNo) => $"CFLAG:{Register(charaNo)}:信頼";

    private static string Stamina(long charaNo) => $"BASE:{Register(charaNo)}:0";

    private static long ReadValue(string target)
        => AiVariableAccess.TryReadInt(target, out long value, out _) ? value : long.MinValue;

    private static void SetValue(string target, long value)
    {
        var batch = new List<AiValueChange> { new() { Target = target, Op = "set", Value = value } };
        if (!AiVariableAccess.TryApplyAll(batch, out string error))
            Log("WARN", $"自检准备数值失败（{target} = {value}）：{error}");
    }

    private static AiComputeChange Change(string field, long charaNo, string op, long value, string reason)
        => new() { Field = field, CharaNo = charaNo, Op = op, Value = value, Reason = reason };

    private static AiComputeResult MakeResult(string turnId, params AiComputeChange[] changes)
    {
        var result = new AiComputeResult { SchemaVersion = AiComputeDefaults.SchemaVersion, TurnId = turnId };
        result.Changes.AddRange(changes);
        return result;
    }

    private static bool HasField(List<AiStateField> fields, string label)
    {
        foreach (AiStateField f in fields)
        {
            if (string.Equals(f.Label, label, StringComparison.Ordinal))
                return true;
        }
        return false;
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

    private static int IndexOfContent(List<ChatMessage> messages, string needle)
    {
        for (int i = 0; i < messages.Count; i++)
        {
            if (messages[i].Content != null && messages[i].Content.Contains(needle, StringComparison.Ordinal))
                return i;
        }
        return -1;
    }

    private static string FirstContent(IReadOnlyList<ChatMessage> messages, string role)
    {
        foreach (ChatMessage m in messages)
        {
            if (string.Equals(m.Role, role, StringComparison.Ordinal))
                return m.Content;
        }
        return null;
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

    private static int CountOccurrences(string text, string needle)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }
        return count;
    }

    private static string Dump(AiComputeRequest request)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"turn_id：{request.TurnId}｜角色号：{request.CharaNo}｜可写字段：{request.Fields.Count}");
        foreach (AiComputeField f in request.Fields)
            sb.AppendLine($"  字段 {f.Field} → {f.Target}｜区间 [{f.Min}, {f.Max}]｜单轮上限 {f.MaxDelta}｜op {string.Join("/", f.EffectiveOps)}");
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
        sb.AppendLine("ERA-AI P3 副 API（计算通道）自检报告");
        sb.AppendLine($"时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"结果：PASS={passed}  FAIL={failed}");
        foreach (string line in lines)
            sb.AppendLine(line);
        sb.AppendLine();
        sb.AppendLine("---- 一次完整的副 API 请求（人工核对用） ----");
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
        sb.AppendLine(failed == 0 ? "COMPUTE SELFTEST RESULT: OK" : "COMPUTE SELFTEST RESULT: FAILED");

        string path = Environment.GetEnvironmentVariable(ReportEnv);
        if (string.IsNullOrWhiteSpace(path))
            path = Path.Combine(Program.ExeDir, "ai_compute_selftest.txt");
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
