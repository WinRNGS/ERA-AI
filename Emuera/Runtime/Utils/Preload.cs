using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using trerror = MinorShift.Emuera.Runtime.Utils.EvilMask.Lang.Error;

namespace MinorShift.Emuera.Runtime.Utils;

static partial class Preload
{
    // 继续使用 ConcurrentDictionary，保持多线程并行读取的高性能
    static ConcurrentDictionary<string, string[]> files = new(StringComparer.OrdinalIgnoreCase);

    public static string[] GetFileLines(string path)
    {
        return files[path];
    }

    public static bool TryGetFileLines(string path, out string[] lines)
    {
        return files.TryGetValue(path, out lines);
    }

    private static string[] ReadAndDecodeFile(string path)
    {
        try
        {
            // ReadWrite共享模式：防止因为玩家用记事本开着文件导致游戏加载报错
            using var fileStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            
            int length = (int)fileStream.Length;
            if (length == 0) return [""];

            // 一次性将整个文件读入内存（极大减少硬盘IO时间）
            var buffer = new byte[length];
            fileStream.ReadExactly(buffer, 0, length);
            
            // 在内存中检测编码
            using var ms = new MemoryStream(buffer);
            Encoding encoding = EncodingHandler.DetectEncoding(ms);
            
            // 检测完编码后，确保游标归零
            ms.Position = 0;

            // 使用内存流交由 StreamReader 处理。
            // 它能完美剥离各种 BOM（包括 UTF-8 和 UTF-16），并安全地处理所有特殊换行符
            using var sr = new StreamReader(ms, encoding, detectEncodingFromByteOrderMarks: true);
            var lines = new List<string>();
            string line;
            while ((line = sr.ReadLine()) != null)
            {
                lines.Add(line);
            }
            return lines.ToArray();
        }
        catch (IOException)
        {
            // 文件被独占锁定时，退回到你的安全备用逻辑
            ParserMediator.Warn(string.Format(trerror.FileUsingOtherProcess.Text, path), new ScriptPosition(path, 0), 0, "");
            return File.ReadAllLines(path, EncodingHandler.UTF8BOMEncoding);
        }
        catch (Exception)
        {
            // 捕获任何乱码解码失败导致的崩溃，报出异常并跳过该文件（还原你之前的防护机制）
            ParserMediator.Warn(trerror.AbnormalEncode.Text, new ScriptPosition(path, 0), 0, "");
            return null;
        }
    }

    public static async Task Load(string path)
    {
        var startTime = DateTime.Now;
        Debug.WriteLine($"Load: {path} : Start");

        var dir = new DirectoryInfo(path);
        if (dir.Exists)
        {
            await Task.Run(() =>
            {
                dir.EnumerateFiles("*", SearchOption.AllDirectories)
                .AsParallel()
                .Where(x =>
                {
                    var ext = x.Extension;
                    return ext.Equals(".csv", StringComparison.OrdinalIgnoreCase) ||
                            ext.Equals(".erb", StringComparison.OrdinalIgnoreCase) ||
                            ext.Equals(".erh", StringComparison.OrdinalIgnoreCase) ||
                            ext.Equals(".erd", StringComparison.OrdinalIgnoreCase) ||
                            ext.Equals(".als", StringComparison.OrdinalIgnoreCase);
                }).ForAll((childPath) =>
                {
                    string filePath = childPath.FullName;
                    var value = ReadAndDecodeFile(filePath);
                    
                    if (value != null)
                    {
                        // ConcurrentDictionary 自带线程安全，不需要 lock
                        files[filePath] = value;
                    }
                });
            });
        }
        else
        {
            var value = ReadAndDecodeFile(path);
            if (value != null)
            {
                files[path] = value;
            }
        }

        Debug.WriteLine($"Load: {path} : End in {(DateTime.Now - startTime).TotalMilliseconds}ms");
    }

    public static async Task Load(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            await Load(path);
        }
    }

    public static void Clear()
    {
        files.Clear();
    }
}