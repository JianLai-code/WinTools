using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace WinTools.Services.Logging;

/// <summary>
/// 极简文本日志：写入 %AppData%\WinTools\logs\ 下按日期分割的文件。
/// 阶段五要求"日志脱敏"——此处只记录调用方传入的消息，调用方不应把完整文件路径、
/// 密码、用户数据写进来。本类不做联网、不采集任何信息。
/// </summary>
public sealed class TextLogger
{
    // 进程内锁 + 命名互斥体：前者防本进程并发，后者防多实例同时写同一文件。
    private static readonly object Gate = new();
    private static readonly Mutex ProcessMutex = CreateMutex();
    private const long MaxFileBytes = 5L * 1024 * 1024; // 单文件 5 MB 后轮转
    private const int MaxRotations = 3;                  // 保留 .1 ~ .3 共 3 个历史文件
    private const int MaxMessageChars = 4000;            // 单条消息上限，防止一次写入巨型行

    private readonly string _logDir;
    private readonly bool _enabled;

    public TextLogger()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _logDir = Path.Combine(appData, "WinTools", "logs");
        try
        {
            Directory.CreateDirectory(_logDir);
            _enabled = true;
        }
        catch (Exception ex)
        {
            // 日志目录不可用不应导致应用启动失败；降级为禁用日志。
            Debug.WriteLine($"WinTools 日志目录不可用，日志已禁用: {ex.Message}");
            _enabled = false;
        }
    }

    /// <summary>互斥体名按日志目录派生，避免不同用户/不同目录互相干扰。</summary>
    private static Mutex CreateMutex()
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(Environment.UserName));
        var tag = Convert.ToHexString(hash.AsSpan(0, 8));
        try
        {
            return new Mutex(false, $@"Local\WinToolsLog_{tag}");
        }
        catch
        {
            // 无法创建命名互斥体时退回一个不跨进程的互斥体，功能不受影响。
            return new Mutex(false);
        }
    }

    public void Info(string message) => Write("INFO", message);
    public void Warn(string message) => Write("WARN", message);
    public void Error(string message) => Write("ERROR", message);

    private void Write(string level, string message)
    {
        if (!_enabled) return;
        try
        {
            // 净化换行：防止消息（如 ex.Message）伪造整行日志；同时截断超长内容。
            var safe = (message ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ');
            if (safe.Length > MaxMessageChars) safe = safe[..MaxMessageChars] + "…[已截断]";
            var file = Path.Combine(_logDir, $"wintools-{DateTime.Today:yyyyMMdd}.log");
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {safe}{Environment.NewLine}";

            lock (Gate)
            {
                var acquired = false;
                try
                {
                    acquired = ProcessMutex.WaitOne(2000);
                    RotateIfNeeded(file);
                    using var fs = new FileStream(file, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                    using var writer = new StreamWriter(fs, new UTF8Encoding(false));
                    writer.Write(line);
                }
                finally
                {
                    if (acquired) ProcessMutex.ReleaseMutex();
                }
            }
        }
        catch (Exception ex)
        {
            // 日志失败绝不影响主功能，但保留一条诊断线索，避免完全静默。
            Debug.WriteLine($"WinTools 日志写入失败: {ex.Message}");
        }
    }

    private static void RotateIfNeeded(string file)
    {
        FileInfo info;
        try
        {
            info = new FileInfo(file);
            if (!info.Exists || info.Length < MaxFileBytes) return;
        }
        catch
        {
            return;
        }

        try
        {
            // .3 -> 删除, .2 -> .3, .1 -> .2, 当前 -> .1
            var oldest = $"{file}.{MaxRotations}";
            if (File.Exists(oldest)) File.Delete(oldest);
            for (var i = MaxRotations - 1; i >= 1; i--)
            {
                var src = $"{file}.{i}";
                if (File.Exists(src)) File.Move(src, $"{file}.{i + 1}", overwrite: true);
            }
            File.Move(file, $"{file}.1", overwrite: true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WinTools 日志轮转失败: {ex.Message}");
        }
    }
}
