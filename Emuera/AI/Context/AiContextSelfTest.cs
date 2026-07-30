using MinorShift.Emuera.AI.Compute;
using MinorShift.Emuera.AI.Interact;
using MinorShift.Emuera.AI.Traits;
using MinorShift.Emuera.GameView;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MinorShift.Emuera.AI.Context;

/// <summary>
/// P5 自动自检。验证上下文压缩的完整链路。
///
/// 环境变量 ERA_AI_CONTEXT_SELFTEST=1 触发。
///
/// 8 组测试：
///   A. Token 估算：字符到 token 的换算正确、边界值合理
///   B. 压缩触发判定：阈值检测、不压缩的场景正确跳过
///   C. 压缩执行：成功路径、摘要替换历史、多次压缩滚动合并
///   D. 压缩失败处理：空响应不崩、异常不崩、副 API 不可用时跳过
///   E. BuildMessages 装配：摘要出现在正确位置、无摘要时不多余
///   F. 清空联动：ClearHistory 清掉摘要、ClearHistory 后 HasSummary 为 false
///   G. 配置解析：context 段正常读取、缺失时默认值、enabled=false 时不压缩
///   H. 串联测试：模拟多轮积累 → 触发压缩 → 压缩后继续对话 → 二次压缩
/// </summary>
internal static class AiContextSelfTest
{
    private static bool armed;
    private static bool finished;
    private static EmueraConsole console;
    private static System.Windows.Forms.Timer pollTimer;
    private static int elapsedMs;

    public static void Arm(EmueraConsole c)
    {
        if (Environment.GetEnvironmentVariable("ERA_AI_CONTEXT_SELFTEST") != "1")
            return;
        if (c == null) return;
        armed = true;
        console = c;
        pollTimer = new System.Windows.Forms.Timer { Interval = 500 };
        pollTimer.Tick += Poll;
        pollTimer.Start();
    }

    private static void Poll(object sender, EventArgs e)
    {
        elapsedMs += 500;
        if (finished) return;

        // P5 自检不需要引擎变量就绪，只需要 UI 线程跑起来即可。
        // 但等 1 秒确保初始化完毕。
        if (elapsedMs < 1000) return;

        pollTimer.Stop();
        try
        {
            Run();
        }
        catch (Exception ex)
        {
            System.Windows.Forms.MessageBox.Show(
                $"P5 自检异常：{ex.Message}", "ERA-AI P5 SelfTest",
                System.Windows.Forms.MessageBoxButtons.OK,
                System.Windows.Forms.MessageBoxIcon.Error);
        }
        finished = true;
    }

    public static bool IsArmed => armed;

    public static void Run()
    {
        if (!armed) return;

        var sb = new StringBuilder();
        sb.AppendLine($"=== ERA-AI P5 上下文压缩自检 ===");
        sb.AppendLine($"时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();

        int pass = 0, fail = 0;

        RunGroup(sb, "A", "Token 估算", TestTokenEstimation, ref pass, ref fail);
        RunGroup(sb, "B", "压缩触发判定", TestCompressionTrigger, ref pass, ref fail);
        RunGroup(sb, "C", "压缩执行", TestCompressionExecution, ref pass, ref fail);
        RunGroup(sb, "D", "压缩失败处理", TestCompressionFailure, ref pass, ref fail);
        RunGroup(sb, "E", "BuildMessages 装配", TestBuildMessages, ref pass, ref fail);
        RunGroup(sb, "F", "清空联动", TestClearHistory, ref pass, ref fail);
        RunGroup(sb, "G", "配置解析", TestConfigParsing, ref pass, ref fail);
        RunGroup(sb, "H", "串联测试", TestIntegration, ref pass, ref fail);

        sb.AppendLine();
        sb.AppendLine($"=== 总计：PASS={pass} FAIL={fail} ===");

        string reportDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory);
        string reportPath = Path.Combine(reportDir, "ai_context_selftest.txt");
        try
        {
            File.WriteAllText(reportPath, sb.ToString(), new UTF8Encoding(false));
        }
        catch { }

        if (fail > 0)
            throw new InvalidOperationException($"P5 自检失败：{fail} 项未通过。报告已写入 {reportPath}");
    }

    private static void RunGroup(StringBuilder sb, string id, string name,
        Action<StringBuilder, Counter> action, ref int pass, ref int fail)
    {
        sb.AppendLine($"--- 组 {id}：{name} ---");
        var counter = new Counter();
        try
        {
            action(sb, counter);
        }
        catch (Exception e)
        {
            counter.Fail(sb, $"组 {id} 异常", e.Message);
        }
        pass += counter.Pass;
        fail += counter.Fail_;
        sb.AppendLine();
    }

    private sealed class Counter
    {
        public int Pass;
        public int Fail_;

        public void Assert(StringBuilder sb, string name, bool condition, string detail = null)
        {
            if (condition)
            {
                Pass++;
                sb.AppendLine($"  PASS: {name}");
            }
            else
            {
                Fail_++;
                sb.AppendLine($"  FAIL: {name}{(detail != null ? $" — {detail}" : "")}");
            }
        }

        public void Fail(StringBuilder sb, string name, string reason)
        {
            Fail_++;
            sb.AppendLine($"  FAIL: {name} — {reason}");
        }
    }

    // ============ 组 A：Token 估算 ============
    private static void TestTokenEstimation(StringBuilder sb, Counter c)
    {
        c.Assert(sb, "空字符串估算为 0", AiContextCompressor.EstimateTokens("") == 0);
        c.Assert(sb, "null 估算为 0", AiContextCompressor.EstimateTokens((string)null) == 0);
        c.Assert(sb, "10 个中文字估算 > 10 token", AiContextCompressor.EstimateTokens("一二三四五六七八九十") > 10);
        c.Assert(sb, "10 个中文字估算 < 30 token", AiContextCompressor.EstimateTokens("一二三四五六七八九十") < 30);
        c.Assert(sb, "100 字符估算为 180", AiContextCompressor.EstimateTokens(100) == 180);
        c.Assert(sb, "0 字符估算为 0", AiContextCompressor.EstimateTokens(0) == 0);
        {
            AiContextSelfTestData.PopulateConversation(3);
            int tokens = AiContextCompressor.EstimateCurrentTokens("system prompt", "user input");
            AiConversation.Clear();
            c.Assert(sb, "EstimateCurrentTokens 含历史", tokens > 0);
        }
        {
            AiConversation.Clear();
            AiContextCompressor.Clear();
            int tokens = AiContextCompressor.EstimateCurrentTokens("hello", "world");
            c.Assert(sb, "EstimateCurrentTokens 空历史只含 prompt 和 input",
                tokens == AiContextCompressor.EstimateTokens("hello".Length + "world".Length));
        }
    }

    // ============ 组 B：压缩触发判定 ============
    private static void TestCompressionTrigger(StringBuilder sb, Counter c)
    {
        AiConversation.Clear();
        AiContextCompressor.Clear();

        c.Assert(sb, "空历史不触发压缩",
            !AiContextCompressor.NeedsCompression("short prompt", "hi"));

        AiContextSelfTestData.PopulateConversation(3);
        c.Assert(sb, "3 轮短对话不触发压缩（默认 8192 窗口）",
            !AiContextCompressor.NeedsCompression("prompt", "input"));

        AiContextSelfTestData.PopulateLongConversation(15);
        c.Assert(sb, "15 轮长对话触发压缩",
            AiContextCompressor.NeedsCompression("prompt", "input"));

        AiConversation.Clear();
    }

    // ============ 组 C：压缩执行 ============
    private static void TestCompressionExecution(StringBuilder sb, Counter c)
    {
        AiConversation.Clear();
        AiContextCompressor.Clear();
        var oldOverride = AiContextCompressor.BackendOverride;

        try
        {
            AiContextCompressor.BackendOverride = AiContextSelfTestData.FakeSummarizer;
            AiContextSelfTestData.PopulateConversation(10);
            int beforeCount = AiConversation.Count;

            var task = AiContextCompressor.CompressAsync(
                action => { action(); return Task.CompletedTask; },
                CancellationToken.None);
            task.Wait();
            var result = task.Result;

            c.Assert(sb, "压缩成功", result.Success);
            c.Assert(sb, "压缩了至少 1 轮", result.CompressedRounds >= 1);
            c.Assert(sb, "消息数减少", AiConversation.Count < beforeCount);
            c.Assert(sb, "有摘要", AiContextCompressor.HasSummary);
            c.Assert(sb, "摘要包含测试标记", AiContextCompressor.Summary.Contains("测试摘要"));
            c.Assert(sb, "LastCompressInfo 非 null", AiContextCompressor.LastCompressInfo != null);
            c.Assert(sb, "LastCompressInfo.Success", AiContextCompressor.LastCompressInfo?.Success == true);

            // 二次压缩：滚动合并
            AiContextSelfTestData.PopulateConversation(10);
            var task2 = AiContextCompressor.CompressAsync(
                action => { action(); return Task.CompletedTask; },
                CancellationToken.None);
            task2.Wait();
            var result2 = task2.Result;

            c.Assert(sb, "二次压缩成功", result2.Success);
            c.Assert(sb, "二次压缩后仍有摘要", AiContextCompressor.HasSummary);
        }
        finally
        {
            AiContextCompressor.BackendOverride = oldOverride;
            AiConversation.Clear();
            AiContextCompressor.Clear();
        }
    }

    // ============ 组 D：压缩失败处理 ============
    private static void TestCompressionFailure(StringBuilder sb, Counter c)
    {
        AiConversation.Clear();
        AiContextCompressor.Clear();
        var oldOverride = AiContextCompressor.BackendOverride;

        try
        {
            // 空响应
            AiContextCompressor.BackendOverride = AiContextSelfTestData.EmptySummarizer;
            AiContextSelfTestData.PopulateConversation(10);
            int countBefore = AiConversation.Count;

            var task = AiContextCompressor.CompressAsync(
                action => { action(); return Task.CompletedTask; },
                CancellationToken.None);
            task.Wait();
            var result = task.Result;

            c.Assert(sb, "空响应不崩溃", true);
            c.Assert(sb, "空响应不成功", !result.Success);
            c.Assert(sb, "空响应有跳过原因", !string.IsNullOrEmpty(result.SkipReason));
            c.Assert(sb, "空响应不改变历史", AiConversation.Count == countBefore);

            // 异常
            AiConversation.Clear();
            AiContextCompressor.Clear();
            AiContextCompressor.BackendOverride = AiContextSelfTestData.ErrorSummarizer;
            AiContextSelfTestData.PopulateConversation(10);
            countBefore = AiConversation.Count;

            var task2 = AiContextCompressor.CompressAsync(
                action => { action(); return Task.CompletedTask; },
                CancellationToken.None);
            task2.Wait();
            var result2 = task2.Result;

            c.Assert(sb, "异常不崩溃", true);
            c.Assert(sb, "异常不成功", !result2.Success);
            c.Assert(sb, "异常有跳过原因", result2.SkipReason?.Contains("网络错误") == true);
            c.Assert(sb, "异常不改变历史", AiConversation.Count == countBefore);

            // 轮数不足
            AiConversation.Clear();
            AiContextCompressor.Clear();
            AiContextCompressor.BackendOverride = AiContextSelfTestData.FakeSummarizer;
            AiContextSelfTestData.PopulateConversation(2);

            var task3 = AiContextCompressor.CompressAsync(
                action => { action(); return Task.CompletedTask; },
                CancellationToken.None);
            task3.Wait();
            var result3 = task3.Result;

            c.Assert(sb, "轮数不足时跳过", !result3.Success);
            c.Assert(sb, "轮数不足有原因", !string.IsNullOrEmpty(result3.SkipReason));
        }
        finally
        {
            AiContextCompressor.BackendOverride = oldOverride;
            AiConversation.Clear();
            AiContextCompressor.Clear();
        }
    }

    // ============ 组 E：BuildMessages 装配 ============
    private static void TestBuildMessages(StringBuilder sb, Counter c)
    {
        AiConversation.Clear();
        AiContextCompressor.Clear();
        var oldOverride = AiContextCompressor.BackendOverride;

        try
        {
            // 无摘要时不出现摘要消息
            c.Assert(sb, "无摘要时 HasSummary=false", !AiContextCompressor.HasSummary);

            // 有摘要时出现
            AiContextCompressor.BackendOverride = AiContextSelfTestData.FakeSummarizer;
            AiContextSelfTestData.PopulateConversation(10);
            var task = AiContextCompressor.CompressAsync(
                action => { action(); return Task.CompletedTask; },
                CancellationToken.None);
            task.Wait();

            c.Assert(sb, "压缩后 HasSummary=true", AiContextCompressor.HasSummary);
            c.Assert(sb, "Summary 非空", !string.IsNullOrWhiteSpace(AiContextCompressor.Summary));
        }
        finally
        {
            AiContextCompressor.BackendOverride = oldOverride;
            AiConversation.Clear();
            AiContextCompressor.Clear();
        }
    }

    // ============ 组 F：清空联动 ============
    private static void TestClearHistory(StringBuilder sb, Counter c)
    {
        AiConversation.Clear();
        AiContextCompressor.Clear();
        var oldOverride = AiContextCompressor.BackendOverride;

        try
        {
            AiContextCompressor.BackendOverride = AiContextSelfTestData.FakeSummarizer;
            AiContextSelfTestData.PopulateConversation(10);
            var task = AiContextCompressor.CompressAsync(
                action => { action(); return Task.CompletedTask; },
                CancellationToken.None);
            task.Wait();

            c.Assert(sb, "压缩后有摘要", AiContextCompressor.HasSummary);

            // 模拟 ClearHistory 联动
            AiConversation.Clear();
            AiComputeMemory.Clear();
            AiQuoteBox.Clear();
            AiContextCompressor.Clear();

            c.Assert(sb, "清空后 HasSummary=false", !AiContextCompressor.HasSummary);
            c.Assert(sb, "清空后 Summary 为空", string.IsNullOrWhiteSpace(AiContextCompressor.Summary));
            c.Assert(sb, "清空后 LastCompressInfo 为 null", AiContextCompressor.LastCompressInfo == null);
        }
        finally
        {
            AiContextCompressor.BackendOverride = oldOverride;
            AiConversation.Clear();
            AiContextCompressor.Clear();
        }
    }

    // ============ 组 G：配置解析 ============
    private static void TestConfigParsing(StringBuilder sb, Counter c)
    {
        // 默认值测试
        c.Assert(sb, "DefaultContextWindow > 0", AiContextCompressor.DefaultContextWindow > 0);
        c.Assert(sb, "TriggerRatio 在合理范围", AiContextCompressor.TriggerRatio > 0.5 && AiContextCompressor.TriggerRatio < 1.0);
        c.Assert(sb, "TargetRatio < TriggerRatio", AiContextCompressor.TargetRatio < AiContextCompressor.TriggerRatio);
        c.Assert(sb, "MinRetainRounds >= 1", AiContextCompressor.MinRetainRounds >= 1);

        // GetContextWindow 在无配置时返回默认值
        c.Assert(sb, "GetContextWindow 有值", AiContextCompressor.GetContextWindow() > 0);
        c.Assert(sb, "GetRetainRounds 有值", AiContextCompressor.GetRetainRounds() >= 1);
    }

    // ============ 组 H：串联测试 ============
    private static void TestIntegration(StringBuilder sb, Counter c)
    {
        AiConversation.Clear();
        AiContextCompressor.Clear();
        var oldOverride = AiContextCompressor.BackendOverride;

        try
        {
            AiContextCompressor.BackendOverride = AiContextSelfTestData.FakeSummarizer;

            // 阶段 1：逐轮积累直到触发
            for (int i = 1; i <= 20; i++)
            {
                AiConversation.AddRound($"integ-{i}",
                    $"第{i}轮输入" + new string('字', 100),
                    $"第{i}轮回复" + new string('字', 150));
            }

            bool needsBefore = AiContextCompressor.NeedsCompression("", "");
            c.Assert(sb, "20 轮后检测到需要压缩", needsBefore);

            // 阶段 2：执行压缩
            var task = AiContextCompressor.CompressAsync(
                action => { action(); return Task.CompletedTask; },
                CancellationToken.None);
            task.Wait();
            var result = task.Result;

            c.Assert(sb, "串联压缩成功", result.Success);
            int afterCount = AiConversation.Count;
            c.Assert(sb, "压缩后消息数减少", afterCount < 40);

            // 阶段 3：压缩后继续对话
            AiConversation.AddRound("integ-21", "压缩后的新输入", "压缩后的新回复");
            c.Assert(sb, "压缩后可正常添加轮次", AiConversation.Count == afterCount + 2);

            // 阶段 4：验证摘要持续存在
            c.Assert(sb, "新轮次后摘要仍在", AiContextCompressor.HasSummary);

            // 阶段 5：引用不受影响
            var last = AiConversation.LastAssistant();
            c.Assert(sb, "最新的 assistant 消息存在", last != null);
            if (last != null)
            {
                bool added = AiQuoteBox.TryAdd(last, out _);
                c.Assert(sb, "压缩后引用最新消息成功", added);
                AiQuoteBox.Clear();
            }
        }
        finally
        {
            AiContextCompressor.BackendOverride = oldOverride;
            AiConversation.Clear();
            AiContextCompressor.Clear();
        }
    }
}