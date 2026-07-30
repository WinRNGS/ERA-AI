using MinorShift.Emuera.GameView;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MinorShift.Emuera.AI.Traits;

/// <summary>
/// P2 词条系统自动自检。
///
/// 由环境变量 ERA_AI_TRAIT_SELFTEST=1 触发，全程在界面线程上跑，结束后把报告写到
/// ERA_AI_TRAIT_SELFTEST_REPORT 指定的文件（默认 exe 目录下 ai_trait_selftest.txt）并关闭窗口。
/// 不设环境变量时该类完全不激活，因此不影响玩家。
///
/// 验收目标（对应设计文档 P2）：
///   - 词条库能加载、能热重载、静态校验能报出人为写错
///   - 匹配引擎按角色变量正确命中与落空，未登录角色号报错而非静默返回空
///   - 单角色命中数被 MaxTraitsPerChara 截断，且排序稳定可复现
///   - 三级冲突（硬 / 软 / 条件）各自生效
///   - 修改器四种 effect 作用在实例上而不污染词条库本体
///   - override_npcs 能按角色号强制命中并覆盖文本
///   - prompt 装配产出完整段落、遵守字数上限、失败时退回静态 prompt
///
/// 每组测试装载各自的小词条库，避免「单角色最多 5 条」的截断干扰断言。
/// 自检会临时替换 exe 同目录的 ai_traits.json，结束时在 finally 中还原。
/// </summary>
internal static partial class AiTraitSelfTest
{
    private const string EnableEnv = "ERA_AI_TRAIT_SELFTEST";
    private const string ReportEnv = "ERA_AI_TRAIT_SELFTEST_REPORT";

    /// <summary>自检用角色号，对应 harness 的 CHARA1.CSV / CHARA2.CSV。</summary>
    private const long CharaA = 1;
    private const long CharaB = 2;

    private static readonly List<string> lines = [];
    private static int passed;
    private static int failed;

    private static System.Windows.Forms.Timer pollTimer;
    private static EmueraConsole target;
    private static int elapsedMs;
    private static bool finished;

    private static string libraryPath;
    private static string libraryBackup;

    /// <summary>内置默认库对角色 A 装出的 prompt，写进报告便于人工核对实际发给模型的文本。</summary>
    private static string promptSample;

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
            Log("FATAL", "等待脚本进入输入等待状态超时，词条自检无法进行。");
            Finish();
            return;
        }

        pollTimer.Stop();
        RunAll();
    }

    private static void RunAll()
    {
        try
        {
            Section("前置：自检环境");
            if (!CheckHarness())
            {
                Finish();
                return;
            }

            libraryPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, AiTraitLibrary.FileName);
            libraryBackup = File.Exists(libraryPath) ? File.ReadAllText(libraryPath, Encoding.UTF8) : null;

            Section("A 组：内置默认库与热重载");
            TestBootstrapLibraryIsUsable();

            Section("B 组：匹配引擎");
            Install(MatchLibraryJson, "匹配测试库");
            TestMatchHitsExpectedTraits();
            TestMatchMissesUnqualifiedTraits();
            TestUnregisteredCharaReportsError();

            Section("C 组：命中数上限与排序");
            Install(CapLibraryJson, "上限测试库");
            TestMaxTraitsPerCharaIsCapped();

            Section("D 组：冲突消解");
            Install(ConflictLibraryJson, "冲突测试库");
            TestHardConflictDropsLowerPriority();
            TestHardConflictIsRepeatable();
            TestSoftConflictSuppressesFieldsOnly();

            Section("E 组：条件修改器");
            Install(ModifierLibraryJson, "修改器测试库");
            TestModifierSuppress();
            TestModifierWeight();
            TestModifierAddConstraint();
            TestModifierDoesNotMutateLibrary();

            Section("F 组：固定 NPC 覆盖");
            Install(NpcLibraryJson, "NPC 覆盖测试库");
            TestNpcOverrideForcesHit();
            TestNpcOverrideDoesNotLeakToOtherChara();

            Section("G 组：prompt 装配");
            Install(PromptLibraryJson, "prompt 测试库");
            TestPromptContainsAllSections();
            TestPromptRespectsMaxChars();
            TestPromptFallsBackWhenNoTraits();
            TestBuildForCurrentTargetUsesCharaNo();

            Section("H 组：写错时必须报错而不是静默忽略");
            Install(BrokenLibraryJson, "含人为错误的词条库");
            TestStaticValidationCatchesMistakes();
            TestBadConditionDropsTraitWithDiagnostic();
        }
        catch (Exception ex)
        {
            Log("FATAL", $"自检自身抛出异常：{ex}");
        }
        finally
        {
            RestoreLibrary();
        }
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

        bool named = AiVariableAccess.Resolve($"CFLAG:{Register(CharaA)}:好感度", out string error) != null;
        Check($"CFLAG 命名下标『好感度』可解析（{Brief(error)}）", named);
        if (!named)
        {
            Log("FATAL", "harness 的 CFLAG.CSV 必须定义『好感度』，否则匹配条件全部无法求值。");
            return false;
        }

        bool read = AiVariableAccess.TryReadInt($"CFLAG:{Register(CharaA)}:好感度", out long favor, out _);
        Check($"角色 A 的好感度为 50（实际 {(read ? favor.ToString() : "读取失败")}）", read && favor == 50);
        return read && favor == 50;
    }

    // ---------- 词条库装载 ----------

    private static void Install(string json, string what)
    {
        File.WriteAllText(libraryPath, json, new UTF8Encoding(false));
        bool ok = AiTraitLibrary.Reload(out string summary);
        Check($"装载{what}成功（{Brief(summary)}）", ok);
    }

    private static void RestoreLibrary()
    {
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

    // ---------- A 组 ----------

    private static void TestBootstrapLibraryIsUsable()
    {
        Check("首次运行已在 exe 同目录写出词条库", File.Exists(libraryPath));
        Check($"内置默认库已加载（{AiTraitLibrary.Count} 条）", AiTraitLibrary.Count > 0);
        Check("内置默认库能查到示例词条 tsundere", AiTraitLibrary.Find("tsundere") != null);
        Check($"内置默认库静态校验无报错（{Brief(string.Join(" | ", AiTraitLibrary.Diagnostics))}）",
            AiTraitLibrary.Diagnostics.Count == 0);
        Check("加载路径指向 exe 同目录", string.Equals(AiTraitLibrary.LoadedPath, libraryPath, StringComparison.OrdinalIgnoreCase));
        Check("刚加载完不应被判定为已过期", !AiTraitLibrary.IsStale());

        // 内置默认库不只要能解析，还必须对真实角色数据产生效果。
        // 否则「默认库能跑」只是 JSON 语法正确，条件写错了照样一条都不命中。
        List<AiTraitInstance> defaults = AiTraitMatcher.Match(CharaA, out string defaultError);
        Check($"内置默认库对真实角色确实命中词条（{defaults.Count} 条，{Brief(defaultError)}）", defaults.Count > 0);
        Check("内置默认库让角色 A 命中 tsundere（好感度 50 + 羞恥素质）", Has(defaults, "tsundere"));
        Check("内置默认库的 always 词条 baseline 生效", Has(defaults, "baseline"));

        string defaultPrompt = AiPromptBuilder.Build(CharaA, out AiPromptBuildInfo defaultInfo);
        promptSample = defaultPrompt;
        Check($"内置默认库能装出可用 prompt（{defaultPrompt.Length} 字）", defaultInfo.UsedTraits && defaultPrompt.Length > 100);
        Check("内置默认库的 prompt 含数值状态读数", defaultPrompt.Contains("好感度: 50"));
        Check("内置默认库的 prompt 未触发字数截断", !defaultInfo.Truncated);
    }

    // ---------- B 组 ----------

    private static void TestMatchHitsExpectedTraits()
    {
        List<AiTraitInstance> hits = AiTraitMatcher.Match(CharaA, out string error);
        Check($"角色 A 匹配未报错（{Brief(error)}）", error == null);
        Check("角色 A 命中 tsundere（好感度 50 在区间内且有羞恥素质）", Has(hits, "tsundere"));
        Check("角色 A 命中 always 词条 baseline", Has(hits, "baseline"));
        AiTraitInstance ts = Find(hits, "tsundere");
        Check($"tsundere 得分 = weight 120 + priority 55（实际 {ts?.Score}）", ts != null && ts.Score == 175);
    }

    private static void TestMatchMissesUnqualifiedTraits()
    {
        List<AiTraitInstance> hits = AiTraitMatcher.Match(CharaA, out _);
        Check("角色 A 未命中 clingy（好感度 50 < 70）", !Has(hits, "clingy"));
        Check("角色 A 未命中 exhausted（体力 100 > 20）", !Has(hits, "exhausted"));
        Check("角色 A 未命中 honest（无素直素质，any 条件不满足）", !Has(hits, "honest"));

        List<AiTraitInstance> hitsB = AiTraitMatcher.Match(CharaB, out _);
        Check("角色 B 命中 honest（有素直素质）", Has(hitsB, "honest"));
        Check("角色 B 命中 cold（好感度 10 <= 25）", Has(hitsB, "cold"));
        Check("角色 B 未命中 tsundere（无傲慢与羞恥素质）", !Has(hitsB, "tsundere"));
        Check("none 条件生效：角色 B 未命中 needs_no_favor（好感度不为 0）", !Has(hitsB, "needs_no_favor"));
    }

    private static void TestUnregisteredCharaReportsError()
    {
        List<AiTraitInstance> hits = AiTraitMatcher.Match(9999, out string error);
        Check("未登录角色号返回空结果", hits.Count == 0);
        Check($"未登录角色号给出明确错误而非静默空结果（{Brief(error)}）", !string.IsNullOrEmpty(error));
    }

    // ---------- C 组 ----------

    private static void TestMaxTraitsPerCharaIsCapped()
    {
        List<AiTraitInstance> hits = AiTraitMatcher.Match(CharaA, out _);
        Check($"库中 8 条 always 词条被截断到上限 {AiTraitMatcher.MaxTraitsPerChara}（实际 {hits.Count}）",
            hits.Count == AiTraitMatcher.MaxTraitsPerChara);
        Check("命中结果按得分降序", IsSortedByScoreDesc(hits));
        Check("保留的是得分最高的 5 条（cap80 → cap40）", Ids(hits) == "cap80,cap70,cap60,cap50,cap40");
        Check("被截断掉的是得分最低的 cap10", !Has(hits, "cap10"));
    }

    // ---------- D 组 ----------

    private static void TestHardConflictDropsLowerPriority()
    {
        List<AiTraitInstance> hits = AiTraitMatcher.Match(CharaA, out _);
        Check("硬冲突：高优先级方保留（always_high priority 90）", Has(hits, "always_high"));
        Check("硬冲突：低优先级方被整条丢弃（always_low priority 20）", !Has(hits, "always_low"));
        Check("硬冲突是无向的：只有一方声明也生效", Has(hits, "always_high") && !Has(hits, "always_low"));
    }

    private static void TestHardConflictIsRepeatable()
    {
        string first = Ids(AiTraitMatcher.Match(CharaA, out _));
        string second = Ids(AiTraitMatcher.Match(CharaA, out _));
        Check($"同样输入两次匹配结果完全一致（{first}）", string.Equals(first, second, StringComparison.Ordinal));
    }

    private static void TestSoftConflictSuppressesFieldsOnly()
    {
        List<AiTraitInstance> hits = AiTraitMatcher.Match(CharaA, out _);
        AiTraitInstance loser = Find(hits, "soft_lose");
        AiTraitInstance winner = Find(hits, "soft_win");
        Check("软冲突：双方都保留在结果中", loser != null && winner != null);
        if (loser == null || winner == null)
            return;
        Check("软冲突：低优先级方的 speech_style 被抹掉", string.IsNullOrEmpty(loser.SpeechStyle));
        Check("软冲突：低优先级方的 description 未被波及", !string.IsNullOrEmpty(loser.Description));
        Check("软冲突：suppress 只写了 speech_style，故 constraints 保留", loser.Constraints.Count > 0);
        Check("软冲突：高优先级方完全不受影响", !string.IsNullOrEmpty(winner.SpeechStyle));
    }

    // ---------- E 组 ----------

    private static void TestModifierSuppress()
    {
        List<AiTraitInstance> hits = AiTraitMatcher.Match(CharaA, out _);
        Check("修改器 suppress：条件成立时整条词条消失", !Has(hits, "mod_suppress"));
        Check("修改器 suppress：条件不成立时词条保留", Has(hits, "mod_keep"));
    }

    private static void TestModifierWeight()
    {
        AiTraitInstance boosted = Find(AiTraitMatcher.Match(CharaA, out _), "mod_weight");
        Check("修改器 weight：词条仍然保留", boosted != null);
        if (boosted == null)
            return;
        Check($"修改器 weight：得分 = weight 50 + priority 60 + value 500（实际 {boosted.Score}）", boosted.Score == 610);
    }

    private static void TestModifierAddConstraint()
    {
        AiTraitInstance t = Find(AiTraitMatcher.Match(CharaA, out _), "mod_constraint");
        Check("修改器 add_constraint：词条保留", t != null);
        if (t == null)
            return;
        Check("修改器 add_constraint：追加的约束已进入实例", t.Constraints.Contains("由修改器追加的约束"));
        Check("修改器 add_constraint：原有约束仍在", t.Constraints.Contains("原本就有的约束"));
    }

    private static void TestModifierDoesNotMutateLibrary()
    {
        AiTraitMatcher.Match(CharaA, out _);
        AiTraitMatcher.Match(CharaA, out _);

        AiTrait rawConstraint = AiTraitLibrary.Find("mod_constraint");
        Check($"反复匹配不污染词条库的 constraints（库中 {rawConstraint?.Constraints.Count} 条）",
            rawConstraint != null && rawConstraint.Constraints.Count == 1);

        AiTrait rawDesc = AiTraitLibrary.Find("mod_desc");
        AiTraitInstance instance = Find(AiTraitMatcher.Match(CharaA, out _), "mod_desc");
        Check("修改器 description：实例描述已被替换",
            instance != null && instance.Description == "被修改器替换后的描述");
        Check("修改器 description：库里的原始描述未被改写",
            rawDesc != null && rawDesc.Description == "原始描述");
    }

    // ---------- F 组 ----------

    private static void TestNpcOverrideForcesHit()
    {
        List<AiTraitInstance> hits = AiTraitMatcher.Match(CharaA, out _);
        AiTraitInstance t = Find(hits, "npc_only");
        Check("override_npcs：force=true 让不可能命中的词条对指定角色命中", t != null);
        if (t == null)
            return;
        Check("override_npcs：description 被该 NPC 的定制文本覆盖", t.Description == "只给 1 号角色的定制描述");
        Check("override_npcs：speech_style 被覆盖", t.SpeechStyle == "只给 1 号角色的定制语气");
        Check("override_npcs：constraints 被整体替换",
            t.Constraints.Count == 1 && t.Constraints[0] == "只给 1 号角色的定制约束");
        Check($"override_npcs：weight_bonus 300 已计入得分（实际 {t.Score}）", t.Score == 400);
    }

    private static void TestNpcOverrideDoesNotLeakToOtherChara()
    {
        List<AiTraitInstance> hitsB = AiTraitMatcher.Match(CharaB, out _);
        Check("override_npcs：不影响其他角色号", !Has(hitsB, "npc_only"));
        AiTraitInstance shared = Find(hitsB, "npc_tuned");
        Check("未被 override 的角色拿到的是通用文本", shared != null && shared.Description == "通用描述");
        AiTraitInstance tuned = Find(AiTraitMatcher.Match(CharaA, out _), "npc_tuned");
        Check("被 override 的角色拿到的是定制文本", tuned != null && tuned.Description == "角色 1 专用描述");
    }

    // ---------- G 组 ----------

    private static void TestPromptContainsAllSections()
    {
        string prompt = AiPromptBuilder.Build(CharaA, out AiPromptBuildInfo info);
        Check($"prompt 使用了词条而非兜底（{Brief(info.FallbackReason)}）", info.UsedTraits);
        Check("prompt 的 {NAME} 已替换为角色名", prompt.Contains("爱丽丝"));
        Check("prompt 的 {CALLNAME} 已替换为呼称", prompt.Contains("爱丽"));
        Check($"prompt 的 {{CHARA_NO}} 已替换为角色号 {CharaA}", prompt.Contains($"角色号 {CharaA}"));
        Check("prompt 含人物特征段落", prompt.Contains("【人物特征】"));
        Check("prompt 含说话风格段落", prompt.Contains("【说话风格】"));
        Check("prompt 含行为约束段落", prompt.Contains("【行为约束】"));
        Check("prompt 含数值状态段落", prompt.Contains("【当前数值状态】"));
        Check("prompt 的数值状态含权威好感度读数 50", prompt.Contains("好感度: 50"));
        Check("prompt 的数值状态含体力读数 100", prompt.Contains("体力: 100"));
        Check("prompt 含全局铁律", prompt.Contains("这是一条全局铁律"));
        Check("prompt 无未替换的占位符残留",
            !prompt.Contains("{TRAITS}") && !prompt.Contains("{SPEECH}")
            && !prompt.Contains("{CONSTRAINTS}") && !prompt.Contains("{STATE}"));
        Check("prompt 里重复的约束已去重", CountOccurrences(prompt, "重复出现的约束") == 1);
        Check("无法解析的状态字段被跳过而不是写进 prompt", !prompt.Contains("这不是变量"));
        Check("无法解析的状态字段留下了运行期诊断",
            string.Join(" | ", AiTraitDiagnostics.Entries).Contains("状态字段无法解析"));
    }

    private static void TestPromptRespectsMaxChars()
    {
        AiPromptBuilder.Build(CharaA, out AiPromptBuildInfo info);
        int max = AiTraitLibrary.PromptTemplate?.MaxChars ?? 0;
        Check($"prompt 长度不超过 max_chars={max}（实际 {info.Prompt?.Length ?? 0}）",
            max > 0 && (info.Prompt?.Length ?? 0) <= max);
        Check("本组 prompt 未触发截断", !info.Truncated);
    }

    private static void TestPromptFallsBackWhenNoTraits()
    {
        string prompt = AiPromptBuilder.Build(9999, out AiPromptBuildInfo info);
        Check("未登录角色号走兜底 prompt", !info.UsedTraits);
        Check($"兜底原因已记录（{Brief(info.FallbackReason)}）", !string.IsNullOrEmpty(info.FallbackReason));
        Check("兜底 prompt 就是 AiConfig.SystemPrompt", prompt == AiConfig.SystemPrompt);
    }

    private static void TestBuildForCurrentTargetUsesCharaNo()
    {
        string prompt = AiPromptBuilder.BuildForCurrentTarget(out AiPromptBuildInfo info);
        Check($"BuildForCurrentTarget 走了词条路径（{Brief(info.FallbackReason)}）", info.UsedTraits);
        Check($"TARGET 登录号 0 被换算为角色号 {CharaA}（实际 {info.CharaNo}）", info.CharaNo == CharaA);
        Check("结果与直接按角色号装配一致", prompt == AiPromptBuilder.Build(CharaA, out _));
    }

    // ---------- H 组 ----------

    private static void TestStaticValidationCatchesMistakes()
    {
        string all = string.Join(" | ", AiTraitLibrary.Diagnostics);
        Check($"静态校验：报出重复 id（{Brief(all)}）", all.Contains("id 重复"));
        Check("静态校验：报出指向不存在词条的 conflicts", all.Contains("不存在"));
        Check("静态校验：报出无法识别的 conflicts kind", all.Contains("无法识别"));
        Check("静态校验：报出缺 effect 的 modifier", all.Contains("缺少 effect"));
        Check("静态校验：报出缺 when.expr 的 modifier", all.Contains("缺少 when.expr"));
        Check("静态校验：报出既无描述也无语气的空词条", all.Contains("不产生任何效果"));
        Check("静态校验：报出缺 id 的词条", all.Contains("缺少 id"));
        Check("静态校验：报出 override_npcs 缺 chara_no", all.Contains("有效 chara_no"));
        Check($"缺 id 与重复 id 的两条被丢弃，库中剩 5 条（实际 {AiTraitLibrary.Count}）", AiTraitLibrary.Count == 5);
        Check("id 重复时保留先出现的那条", AiTraitLibrary.Find("dup")?.Description == "先出现的一条");
    }

    private static void TestBadConditionDropsTraitWithDiagnostic()
    {
        AiTraitDiagnostics.Clear();
        List<AiTraitInstance> hits = AiTraitMatcher.Match(CharaA, out _);
        Check("条件表达式写错时该词条不进 prompt", !Has(hits, "bad_expr"));
        Check("同库中写对的词条不受影响", Has(hits, "good_expr"));
        string diag = string.Join(" | ", AiTraitDiagnostics.Entries);
        Check($"条件表达式写错时留下运行期诊断（{Brief(diag)}）", diag.Contains("条件表达式无法解析"));
    }

    // ---------- 工具 ----------

    private static long Register(long charaNo) => GlobalStatic.VEvaluator.GetChara(charaNo);

    private static bool Has(List<AiTraitInstance> hits, string id) => Find(hits, id) != null;

    private static AiTraitInstance Find(List<AiTraitInstance> hits, string id)
    {
        foreach (AiTraitInstance t in hits)
        {
            if (string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase))
                return t;
        }
        return null;
    }

    private static string Ids(List<AiTraitInstance> hits)
    {
        var sb = new StringBuilder();
        foreach (AiTraitInstance t in hits)
        {
            if (sb.Length > 0)
                sb.Append(',');
            sb.Append(t.Id);
        }
        return sb.ToString();
    }

    private static bool IsSortedByScoreDesc(List<AiTraitInstance> hits)
    {
        for (int i = 1; i < hits.Count; i++)
        {
            if (hits[i - 1].Score < hits[i].Score)
                return false;
        }
        return true;
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

        var sb = new StringBuilder();
        sb.AppendLine("ERA-AI P2 词条系统自检报告");
        sb.AppendLine($"时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"结果：PASS={passed}  FAIL={failed}");
        foreach (string line in lines)
            sb.AppendLine(line);
        sb.AppendLine();
        sb.AppendLine("---- 内置默认库为角色 A 装出的 system prompt ----");
        sb.AppendLine(promptSample ?? "(未采集)");
        sb.AppendLine();
        sb.AppendLine("---- 词条运行期诊断（最后一组库） ----");
        foreach (string line in AiTraitDiagnostics.Entries)
            sb.AppendLine("  " + line);
        sb.AppendLine();
        sb.AppendLine(failed == 0 ? "TRAIT SELFTEST RESULT: OK" : "TRAIT SELFTEST RESULT: FAILED");

        string path = Environment.GetEnvironmentVariable(ReportEnv);
        if (string.IsNullOrWhiteSpace(path))
            path = Path.Combine(Program.ExeDir, "ai_trait_selftest.txt");
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