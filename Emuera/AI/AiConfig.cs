using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MinorShift.Emuera.AI;

/// <summary>
/// AI 模块配置。密钥使用 DPAPI (ProtectedData) 加密存储，不得出现在日志与错误提示中。
/// 配置文件保存在 exe 同目录下的 ai_config.json。
/// </summary>
internal static class AiConfig
{
    private static readonly object gate = new();
    private static AiConfigData data;
    private static string configPath;

    public static string ApiEndpoint
    {
        get { lock (gate) return data.ApiEndpoint; }
        set { lock (gate) data.ApiEndpoint = value; }
    }

    public static string Model
    {
        get { lock (gate) return data.Model; }
        set { lock (gate) data.Model = value; }
    }

    public static int MaxTokens
    {
        get { lock (gate) return data.MaxTokens; }
        set { lock (gate) data.MaxTokens = value; }
    }

    public static int TimeoutSeconds
    {
        get { lock (gate) return data.TimeoutSeconds; }
        set { lock (gate) data.TimeoutSeconds = value; }
    }

    public static int MaxRetries
    {
        get { lock (gate) return data.MaxRetries; }
        set { lock (gate) data.MaxRetries = value; }
    }

    public static string SystemPrompt
    {
        get { lock (gate) return data.SystemPrompt; }
        set { lock (gate) data.SystemPrompt = value; }
    }

    public static double Temperature
    {
        get { lock (gate) return data.Temperature; }
        set { lock (gate) data.Temperature = value; }
    }

    /// <summary>
    /// 是否启用词条系统动态生成 system prompt。
    /// 关闭后退回 SystemPrompt 静态文本，便于对照排查词条库问题。
    /// </summary>
    public static bool UseTraitPrompt
    {
        get { lock (gate) return data.UseTraitPrompt; }
        set { lock (gate) data.UseTraitPrompt = value; }
    }

    /// <summary>
    /// 是否启用副 API（计算通道）。关掉后主 API 单通道照常工作，数值一律不动，
    /// 用于对照排查「到底是叙事出问题还是数值出问题」。
    /// </summary>
    public static bool UseComputeApi
    {
        get { lock (gate) return data.UseComputeApi; }
        set { lock (gate) data.UseComputeApi = value; }
    }

    public static string ComputeApiEndpoint
    {
        get { lock (gate) return data.ComputeApiEndpoint; }
        set { lock (gate) data.ComputeApiEndpoint = value; }
    }

    public static string ComputeModel
    {
        get { lock (gate) return data.ComputeModel; }
        set { lock (gate) data.ComputeModel = value; }
    }

    public static int ComputeMaxTokens
    {
        get { lock (gate) return data.ComputeMaxTokens; }
        set { lock (gate) data.ComputeMaxTokens = value; }
    }

    public static int ComputeTimeoutSeconds
    {
        get { lock (gate) return data.ComputeTimeoutSeconds; }
        set { lock (gate) data.ComputeTimeoutSeconds = value; }
    }

    public static int ComputeMaxRetries
    {
        get { lock (gate) return data.ComputeMaxRetries; }
        set { lock (gate) data.ComputeMaxRetries = value; }
    }

    /// <summary>
    /// 副 API 是否复用主 API 的密钥。多数人主副用同一家服务商，默认复用省一次输入；
    /// 换成不同服务商时取消勾选并单独填。
    /// </summary>
    public static bool ComputeReusesMainKey
    {
        get { lock (gate) return data.ComputeReusesMainKey; }
        set { lock (gate) data.ComputeReusesMainKey = value; }
    }

    /// <summary>
    /// 上次从上游拉取到的主 API 模型列表。持久化的目的是：离线或限流时设置面板
    /// 依然能给出下拉候选，不必每次开窗都打一次网络请求。
    /// 返回副本，避免调用方改动内部集合。
    /// </summary>
    public static List<string> CachedModels
    {
        get { lock (gate) return new List<string>(data.CachedModels ?? new List<string>()); }
        set { lock (gate) data.CachedModels = value == null ? new List<string>() : new List<string>(value); }
    }

    /// <summary>副 API 的模型列表缓存。主副可能是不同服务商，所以分开存。</summary>
    public static List<string> CachedComputeModels
    {
        get { lock (gate) return new List<string>(data.CachedComputeModels ?? new List<string>()); }
        set { lock (gate) data.CachedComputeModels = value == null ? new List<string>() : new List<string>(value); }
    }
    public static bool HasApiKey
    {
        get { lock (gate) return !string.IsNullOrEmpty(data.EncryptedApiKey); }
    }

    public static bool HasComputeApiKey
    {
        get
        {
            lock (gate)
            {
                return data.ComputeReusesMainKey
                    ? !string.IsNullOrEmpty(data.EncryptedApiKey)
                    : !string.IsNullOrEmpty(data.EncryptedComputeApiKey);
            }
        }
    }

    public static void SetComputeApiKey(string plainKey)
    {
        lock (gate)
            data.EncryptedComputeApiKey = Encrypt(plainKey);
    }

    public static string GetComputeApiKeyPlain()
    {
        bool reuse;
        lock (gate)
            reuse = data.ComputeReusesMainKey;
        if (reuse)
            return GetApiKeyPlain();
        lock (gate)
            return Decrypt(data.EncryptedComputeApiKey);
    }

    /// <summary>
    /// 副 API 是否可用。返回 false 时调度器跳过计算通道，主 API 照常跑。
    /// 这是有意的降级而不是报错：叙事能用总比整轮失败好。
    /// </summary>
    public static bool IsComputeReady(out string reason)
    {
        reason = null;
        if (!UseComputeApi)
        {
            reason = "副 API 未启用";
            return false;
        }
        if (string.IsNullOrWhiteSpace(ComputeApiEndpoint))
        {
            reason = "副 API 端点未设置";
            return false;
        }
        if (!HasComputeApiKey)
        {
            reason = ComputeReusesMainKey ? "副 API 复用主密钥，但主 API Key 未设置" : "副 API Key 未设置";
            return false;
        }
        return true;
    }

    public static void SetApiKey(string plainKey)
    {
        lock (gate)
            data.EncryptedApiKey = Encrypt(plainKey);
    }

    public static string GetApiKeyPlain()
    {
        lock (gate)
            return Decrypt(data.EncryptedApiKey);
    }

    private static string Encrypt(string plainKey)
    {
        if (string.IsNullOrWhiteSpace(plainKey))
            return null;
        byte[] plain = Encoding.UTF8.GetBytes(plainKey);
        byte[] encrypted = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(encrypted);
    }

    private static string Decrypt(string encryptedBase64)
    {
        if (string.IsNullOrEmpty(encryptedBase64))
            return null;
        try
        {
            byte[] encrypted = Convert.FromBase64String(encryptedBase64);
            byte[] plain = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch
        {
            return null;
        }
    }

    public static void Load()
    {
        configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ai_config.json");
        lock (gate)
        {
            data = new AiConfigData();
            if (File.Exists(configPath))
            {
                try
                {
                    string json = File.ReadAllText(configPath, Encoding.UTF8);
                    data = JsonSerializer.Deserialize<AiConfigData>(json) ?? new AiConfigData();
                }
                catch
                {
                    data = new AiConfigData();
                }
            }
        }
    }

    public static void Save()
    {
        if (configPath == null)
            return;
        lock (gate)
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(data, options);
                File.WriteAllText(configPath, json, Encoding.UTF8);
            }
            catch
            {
            }
        }
    }

    public static bool IsReady(out string reason)
    {
        reason = null;
        if (!HasApiKey)
        {
            reason = "API Key 未设置";
            return false;
        }
        if (string.IsNullOrWhiteSpace(ApiEndpoint))
        {
            reason = "API 端点未设置";
            return false;
        }
        return true;
    }

    private sealed class AiConfigData
    {
        public string ApiEndpoint { get; set; } = "https://api.openai.com/v1/chat/completions";
        public string Model { get; set; } = "gpt-4o-mini";
        public int MaxTokens { get; set; } = 600;
        public int TimeoutSeconds { get; set; } = 30;
        public int MaxRetries { get; set; } = 1;
        public string SystemPrompt { get; set; } = "你是一个ERA游戏的叙事AI，负责生成角色对话与场景描写。保持简洁，控制在300字以内。";
        public double Temperature { get; set; } = 0.8;
        public bool UseTraitPrompt { get; set; } = true;
        public string EncryptedApiKey { get; set; }

        // ---------- 副 API（计算通道，P3） ----------
        public bool UseComputeApi { get; set; }
        public string ComputeApiEndpoint { get; set; } = "https://api.openai.com/v1/chat/completions";
        public string ComputeModel { get; set; } = "gpt-4o-mini";
        public int ComputeMaxTokens { get; set; } = 800;
        public int ComputeTimeoutSeconds { get; set; } = 20;
        public int ComputeMaxRetries { get; set; } = 1;
        public bool ComputeReusesMainKey { get; set; } = true;
        public string EncryptedComputeApiKey { get; set; }

        // ---------- 模型列表缓存（从上游 /v1/models 拉取后落盘） ----------
        public List<string> CachedModels { get; set; } = new();
        public List<string> CachedComputeModels { get; set; } = new();
    }
}
