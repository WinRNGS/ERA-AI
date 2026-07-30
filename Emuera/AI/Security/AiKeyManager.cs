using System;
using System.Security.Cryptography;
using System.Text;

namespace MinorShift.Emuera.AI.Security;

/// <summary>
/// 密钥安全加固（P6）。
///
/// 设计原则：
///   1. 磁盘存储必须加密（DPAPI，P0 已实现）。
///   2. 内存中尽量减少明文停留时间：解密后立即使用，用完立即清零。
///   3. 绝不在日志、错误消息、界面（除设置对话框）暴露明文或密文。
///   4. 设置新密钥时显式清除旧密文，避免多份密钥残留在配置文件里。
///
/// 与 AiConfig 的分工：
///   - AiConfig 负责配置项的持久化与属性访问（DPAPI 加密/解密仍在那里）。
///   - AiKeyManager 负责密钥的生命周期管理（验证、轮换、内存清零）。
/// </summary>
internal static class AiKeyManager
{
    /// <summary>
    /// 验证密钥格式。OpenAI 密钥通常以 sk- 开头，其他服务商各有特征。
    /// 这里做最基础的检查：非空、长度合理、无明显的异常字符。
    /// </summary>
    public static bool ValidateKeyFormat(string plainKey, out string reason)
    {
        reason = null;

        if (string.IsNullOrWhiteSpace(plainKey))
        {
            reason = "密钥为空";
            return false;
        }

        string trimmed = plainKey.Trim();

        if (trimmed.Length < 20)
        {
            reason = "密钥过短（少于 20 字符），可能不是有效的 API 密钥";
            return false;
        }

        if (trimmed.Length > 512)
        {
            reason = "密钥过长（超过 512 字符），可能输入错误";
            return false;
        }

        // 检查是否包含明显的注入攻击特征（换行符、控制字符）
        foreach (char c in trimmed)
        {
            if (char.IsControl(c) && c != '\t')
            {
                reason = "密钥包含非法控制字符";
                return false;
            }
        }

        // 检查是否包含明显的路径或 URL（常见误输入）
        if (trimmed.Contains("://") || trimmed.Contains("\\") || trimmed.Contains("/"))
        {
            reason = "密钥不应包含 URL 或文件路径";
            return false;
        }

        return true;
    }

    /// <summary>
    /// 安全地使用密钥：获取明文 → 使用 → 立即清零。
    /// 返回操作结果，出错时返回 default(T) 且设置 error。
    /// </summary>
    public static T UseKey<T>(string encryptedKey, Func<string, T> action, out string error)
    {
        error = null;
        string plainKey = null;

        try
        {
            plainKey = DecryptKey(encryptedKey);
            if (plainKey == null)
            {
                error = "密钥解密失败（可能用户账户变更或密钥损坏）";
                return default;
            }

            return action(plainKey);
        }
        catch (Exception e)
        {
            error = $"使用密钥时异常：{e.Message}";
            return default;
        }
        finally
        {
            // 立即清零明文密钥
            if (plainKey != null)
                ClearString(ref plainKey);
        }
    }

    /// <summary>
    /// 解密密钥。失败时返回 null。
    /// </summary>
    private static string DecryptKey(string encryptedBase64)
    {
        if (string.IsNullOrEmpty(encryptedBase64))
            return null;

        try
        {
            byte[] encrypted = Convert.FromBase64String(encryptedBase64);
            byte[] plain = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            string result = Encoding.UTF8.GetString(plain);
            Array.Clear(plain, 0, plain.Length); // 清零字节数组
            return result;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 尽力清零字符串内存。C# 字符串是不可变的，这个方法只能清零引用，
    /// 无法保证 GC 前原字符串内存一定被覆盖。但聊胜于无。
    /// </summary>
    private static void ClearString(ref string value)
    {
        if (value == null)
            return;

        // 创建全零字符串，让原字符串尽快进入 GC 队列
        value = new string('\0', value.Length);
        value = null;
    }

    /// <summary>
    /// 测试密钥是否能正常解密。供配置页"测试连接"用。
    /// </summary>
    public static bool CanDecrypt(string encryptedKey)
    {
        if (string.IsNullOrEmpty(encryptedKey))
            return false;
        string plain = DecryptKey(encryptedKey);
        bool result = plain != null;
        if (plain != null)
            ClearString(ref plain);
        return result;
    }

    /// <summary>
    /// 脱敏处理：只保留前 7 字符 + 后 4 字符，中间打码。
    /// 用于日志与错误提示，让用户能认出是哪把密钥，但不泄露完整内容。
    /// </summary>
    public static string MaskKey(string plainKey)
    {
        if (string.IsNullOrEmpty(plainKey))
            return "[空密钥]";

        if (plainKey.Length <= 11)
            return new string('*', plainKey.Length);

        string prefix = plainKey[..7];
        string suffix = plainKey[^4..];
        return $"{prefix}***{suffix}";
    }

    /// <summary>
    /// 从错误消息中移除密钥。支持多个密钥同时过滤。
    /// AiBackend 已有 SanitizeError，但那个是私有的。这里提供统一接口。
    /// </summary>
    public static string SanitizeErrorMessage(string message, params string[] plainKeys)
    {
        if (string.IsNullOrEmpty(message))
            return message;

        string result = message;
        foreach (string key in plainKeys)
        {
            if (!string.IsNullOrEmpty(key) && result.Contains(key))
                result = result.Replace(key, "[REDACTED]");
        }
        return result;
    }
}
