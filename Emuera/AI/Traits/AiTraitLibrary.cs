using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace MinorShift.Emuera.AI.Traits;

/// <summary>
/// 词条库。负责从磁盘加载 ai_traits.json、校验、建索引、支持手动热重载。
///
/// 位置决策（P1 遗留未决项，此处定为）：
///   1. 优先读 exe 同目录的 ai_traits.json —— 跟着 exe 走，换游戏不用重配。
///   2. 若不存在，读游戏 CSV 目录（csv\ai_traits.json）—— 允许每个游戏各带一套。
///   3. 两处都没有时，自动在 exe 同目录写出一份内置默认库，保证首次运行即可用。
///
/// 热重载决策：不用 FileSystemWatcher（编辑器写盘会触发多次、可能读到半截文件），
/// 改为菜单手动触发 Reload()，配合文件写入时间对比。语义明确、可预期。
/// </summary>
internal static class AiTraitLibrary
{
    public const string FileName = "ai_traits.json";

    private static readonly object gate = new();
    private static AiTraitFile file = new();
    private static readonly Dictionary<string, AiTrait> byId = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<string> diagnostics = [];
    private static string loadedPath;
    private static DateTime loadedStamp;

    /// <summary>当前已加载的词条（含被停用的）。</summary>
    public static IReadOnlyList<AiTrait> All
    {
        get { lock (gate) return file.Traits.ToArray(); }
    }

    /// <summary>加载/校验过程中的诊断信息。给人看，用于排查 JSON 写错。</summary>
    public static IReadOnlyList<string> Diagnostics
    {
        get { lock (gate) return diagnostics.ToArray(); }
    }

    public static string LoadedPath
    {
        get { lock (gate) return loadedPath; }
    }

    public static int Count
    {
        get { lock (gate) return file.Traits.Count; }
    }

    /// <summary>prompt 骨架。词条库没写 prompt 段时为 null，由 AiPromptBuilder 兜底。</summary>
    public static AiPromptTemplate PromptTemplate
    {
        get { lock (gate) return file.Prompt; }
    }

    /// <summary>副 API 契约。词条库没写 compute 段时为 null，此时副 API 不会被调用。</summary>
    public static Compute.AiComputeTemplate ComputeTemplate
    {
        get { lock (gate) return file.Compute; }
    }

    public static AiTrait Find(string id)
    {
        if (string.IsNullOrEmpty(id))
            return null;
        lock (gate)
            return byId.TryGetValue(id, out AiTrait t) ? t : null;
    }

    /// <summary>按标签取词条。tag 为空时返回全部启用项。</summary>
    public static List<AiTrait> ByTag(string tag)
    {
        var result = new List<AiTrait>();
        lock (gate)
        {
            foreach (AiTrait t in file.Traits)
            {
                if (!t.Enabled)
                    continue;
                if (string.IsNullOrEmpty(tag))
                {
                    result.Add(t);
                    continue;
                }
                if (t.Tags == null)
                    continue;
                foreach (string s in t.Tags)
                {
                    if (string.Equals(s, tag, StringComparison.OrdinalIgnoreCase))
                    {
                        result.Add(t);
                        break;
                    }
                }
            }
        }
        return result;
    }

    /// <summary>首次加载。找不到文件时写出内置默认库。</summary>
    public static void Load()
    {
        string path = ResolvePath(out bool needBootstrap);
        if (needBootstrap)
        {
            try
            {
                File.WriteAllText(path, AiTraitDefaults.Json, new UTF8Encoding(false));
            }
            catch (Exception e)
            {
                lock (gate)
                    diagnostics.Add($"默认词条库写入失败（{path}）：{e.Message}");
            }
        }
        LoadFrom(path);
    }

    /// <summary>手动热重载。返回 false 表示失败，原因在 Diagnostics 里。</summary>
    public static bool Reload(out string summary)
    {
        string path = ResolvePath(out _);
        bool ok = LoadFrom(path);
        lock (gate)
        {
            int enabled = 0;
            foreach (AiTrait t in file.Traits)
                if (t.Enabled)
                    enabled++;
            summary = ok
                ? $"已重载 {file.Traits.Count} 条词条（启用 {enabled}），来源 {path}"
                : $"重载失败：{(diagnostics.Count > 0 ? diagnostics[^1] : "未知原因")}";
        }
        return ok;
    }

    /// <summary>文件在磁盘上比内存里新，说明有人改过。</summary>
    public static bool IsStale()
    {
        lock (gate)
        {
            if (loadedPath == null || !File.Exists(loadedPath))
                return false;
            try
            {
                return File.GetLastWriteTimeUtc(loadedPath) > loadedStamp;
            }
            catch
            {
                return false;
            }
        }
    }

    private static string ResolvePath(out bool needBootstrap)
    {
        needBootstrap = false;
        string exeDir = AppDomain.CurrentDomain.BaseDirectory;
        string primary = Path.Combine(exeDir, FileName);
        if (File.Exists(primary))
            return primary;

        try
        {
            string csvDir = Program.CsvDir;
            if (!string.IsNullOrEmpty(csvDir))
            {
                string secondary = Path.Combine(csvDir, FileName);
                if (File.Exists(secondary))
                    return secondary;
            }
        }
        catch
        {
        }

        needBootstrap = true;
        return primary;
    }

    private static bool LoadFrom(string path)
    {
        var parsed = new AiTraitFile();
        var localDiag = new List<string>();
        bool ok = false;
        try
        {
            if (!File.Exists(path))
            {
                localDiag.Add($"词条库不存在：{path}");
            }
            else
            {
                string json = File.ReadAllText(path, Encoding.UTF8);
                parsed = JsonSerializer.Deserialize<AiTraitFile>(json, SerializerOptions) ?? new AiTraitFile();
                ok = true;
            }
        }
        catch (Exception e)
        {
            localDiag.Add($"词条库解析失败（{path}）：{e.Message}");
            parsed = new AiTraitFile();
        }

        Validate(parsed, localDiag);

        lock (gate)
        {
            file = parsed;
            byId.Clear();
            foreach (AiTrait t in parsed.Traits)
            {
                if (string.IsNullOrWhiteSpace(t.Id))
                    continue;
                byId[t.Id] = t;
            }
            diagnostics.Clear();
            diagnostics.AddRange(localDiag);
            loadedPath = path;
            try
            {
                loadedStamp = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.UtcNow;
            }
            catch
            {
                loadedStamp = DateTime.UtcNow;
            }
        }
        return ok;
    }

    /// <summary>
    /// 静态校验。只报告不修正，因为静默修正会让人以为自己写对了。
    /// </summary>
    private static void Validate(AiTraitFile target, List<string> diag)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (AiTrait t in target.Traits)
            if (t != null && !string.IsNullOrWhiteSpace(t.Id))
                ids.Add(t.Id);

        // 重复 id 的判定必须按文件里的先后顺序决定去留，所以先正向扫一遍标出要丢的下标，
        // 再反向删除。若直接在反向循环里用 HashSet 去重，留下的会是文件里靠后的一条，
        // 与诊断信息「后出现的一条已丢弃」相反，人工排查时极易误判。
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var duplicateIndices = new HashSet<int>();
        for (int i = 0; i < target.Traits.Count; i++)
        {
            AiTrait t = target.Traits[i];
            if (t == null || string.IsNullOrWhiteSpace(t.Id))
                continue;
            if (!seen.Add(t.Id))
                duplicateIndices.Add(i);
        }

        for (int i = target.Traits.Count - 1; i >= 0; i--)
        {
            AiTrait t = target.Traits[i];
            if (t == null)
            {
                target.Traits.RemoveAt(i);
                diag.Add($"第 {i} 条为空，已丢弃。");
                continue;
            }
            if (string.IsNullOrWhiteSpace(t.Id))
            {
                diag.Add($"词条「{t.Name}」缺少 id，已丢弃。");
                target.Traits.RemoveAt(i);
                continue;
            }
            if (duplicateIndices.Contains(i))
            {
                diag.Add($"词条 id 重复：{t.Id}，后出现的一条已丢弃（保留文件中先出现的那条）。");
                target.Traits.RemoveAt(i);
                continue;
            }
            if (string.IsNullOrWhiteSpace(t.Name))
                t.Name = t.Id;
            if (string.IsNullOrWhiteSpace(t.Description) && string.IsNullOrWhiteSpace(t.SpeechStyle))
                diag.Add($"词条 {t.Id} 既无 description 也无 speech_style，进入 prompt 后不产生任何效果。");

            foreach (AiTraitConflict c in t.Conflicts)
            {
                if (string.IsNullOrWhiteSpace(c.With))
                {
                    diag.Add($"词条 {t.Id} 有一条 conflicts 缺少 with。");
                    continue;
                }
                if (!ids.Contains(c.With))
                    diag.Add($"词条 {t.Id} 的冲突对象 {c.With} 不存在（id 写错或对方已删除）。");
                if (!string.Equals(c.Kind, "hard", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(c.Kind, "soft", StringComparison.OrdinalIgnoreCase))
                    diag.Add($"词条 {t.Id} 的冲突 kind=\"{c.Kind}\" 无法识别，按 hard 处理。");
            }

            foreach (AiTraitModifier m in t.Modifiers)
            {
                if (m.When == null || string.IsNullOrWhiteSpace(m.When.Expr))
                    diag.Add($"词条 {t.Id} 有一条 modifier 缺少 when.expr，永不生效。");
                if (string.IsNullOrWhiteSpace(m.Effect))
                    diag.Add($"词条 {t.Id} 有一条 modifier 缺少 effect，永不生效。");
            }

            foreach (AiTraitNpcOverride o in t.OverrideNpcs)
            {
                if (o.CharaNo < 0)
                    diag.Add($"词条 {t.Id} 有一条 override_npcs 缺少有效 chara_no。");
            }
        }

        ValidateCompute(target.Compute, diag);
    }

    /// <summary>
    /// 副 API 契约的静态校验。与词条一样：只报告不修正。
    /// 这里每一条都是「写错了不校验就会静默失效」的项，尤其是 field 重名与 target 缺 {CHARA}。
    /// </summary>
    private static void ValidateCompute(Compute.AiComputeTemplate compute, List<string> diag)
    {
        if (compute == null)
            return;

        if (compute.MemoryRounds < 0)
            diag.Add($"compute.memory_rounds 为负数（{compute.MemoryRounds}），按 0 处理（不带短记忆）。");
        if (compute.MemoryRounds > Compute.AiComputeMemory.MaxRounds)
            diag.Add($"compute.memory_rounds={compute.MemoryRounds} 超过上限 {Compute.AiComputeMemory.MaxRounds}，实际只会带 {Compute.AiComputeMemory.MaxRounds} 轮。");
        if (compute.MaxChanges <= 0)
            diag.Add($"compute.max_changes 为 {compute.MaxChanges}，副 API 的任何变更都会被拒绝。");
        if (!string.Equals(compute.OnOutOfRange, "clamp", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(compute.OnOutOfRange, "reject", StringComparison.OrdinalIgnoreCase))
            diag.Add($"compute.on_out_of_range=\"{compute.OnOutOfRange}\" 无法识别，按 clamp 处理。");

        if (compute.WritableFields == null || compute.WritableFields.Count == 0)
        {
            if (compute.Enabled)
                diag.Add("compute.writable_fields 为空，副 API 无字段可改，等同于停用。");
            return;
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Compute.AiComputeField f in compute.WritableFields)
        {
            if (f == null)
            {
                diag.Add("compute.writable_fields 里有一项为空，已忽略。");
                continue;
            }
            if (string.IsNullOrWhiteSpace(f.Field))
            {
                diag.Add("compute.writable_fields 有一项缺少 field 名，副 API 无法引用它。");
                continue;
            }
            if (!names.Add(f.Field))
                diag.Add($"compute.writable_fields 的 field 名重复：{f.Field}，后一条永远不会被选中。");
            if (string.IsNullOrWhiteSpace(f.Target))
            {
                diag.Add($"compute 字段 {f.Field} 缺少 target，写入必定失败。");
                continue;
            }
            if (f.Min > f.Max)
                diag.Add($"compute 字段 {f.Field} 的 min({f.Min}) 大于 max({f.Max})，任何值都会越界。");
            if (f.MaxDelta < 0)
                diag.Add($"compute 字段 {f.Field} 的 max_delta 为负数，按不限处理。");
            foreach (string op in f.EffectiveOps)
            {
                if (!AiVariableAccess.IsAllowedOp(op))
                    diag.Add($"compute 字段 {f.Field} 声明了不支持的操作符 \"{op}\"，使用时会被拒绝。");
            }
            if (!AiVariableAccess.IsWritableTargetName(f.Target, out string reason))
                diag.Add($"compute 字段 {f.Field} 的 target 不可写：{reason}");
        }
    }

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };
}
