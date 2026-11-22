using System.Diagnostics;
using System.Management;

namespace WinTools.Tools.SystemTools;

/// <summary>
/// 读取进程全量信息：优先 WMI（命令行 / 路径 / 启动时间 / 所有者），
/// 任一项失败则用 System.Diagnostics.Process 补齐；全部失败仍保留 PID。
/// 权限不足是常态（系统进程、其他用户进程），因此逐字段容错而不中断查询。
/// </summary>
public static class ProcessDetailReader
{
    public static ProcessDetail Read(int pid)
    {
        var notes = new List<string>();
        string? name = null;
        string? path = null;
        string? commandLine = null;
        string? owner = null;
        DateTime? startTime = null;
        int? parentPid = null;
        int? threadCount = null;
        int? handleCount = null;
        long? workingSet = null;

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, ExecutablePath, CommandLine, CreationDate, ParentProcessId, " +
                "WorkingSetSize, ThreadCount, HandleCount FROM Win32_Process " +
                $"WHERE ProcessId = {pid}");
            using var collection = searcher.Get();
            foreach (ManagementObject obj in collection)
            {
                name = GetString(obj, "Name");
                path = GetString(obj, "ExecutablePath");
                commandLine = GetString(obj, "CommandLine");
                parentPid = GetInt(obj, "ParentProcessId");
                workingSet = GetLong(obj, "WorkingSetSize");
                threadCount = GetInt(obj, "ThreadCount");
                handleCount = GetInt(obj, "HandleCount");
                startTime = GetDate(obj, "CreationDate");
                owner = GetOwner(obj);
                break;
            }
        }
        catch (Exception ex)
        {
            notes.Add($"WMI 读取受限：{ex.Message}");
        }

        // WMI 在权限不足时可能整批失败，用 Process 对象兜底补齐基础字段。
        try
        {
            using var process = Process.GetProcessById(pid);
            name ??= Try(() => process.ProcessName);
            startTime ??= TryDate(() => process.StartTime);
            path ??= Try(() => process.MainModule?.FileName);
            threadCount ??= Try(() => process.Threads.Count);
            workingSet ??= Try(() => process.WorkingSet64);
            handleCount ??= Try(() => process.HandleCount);
        }
        catch (Exception ex)
        {
            notes.Add($"进程快照读取受限：{ex.Message}");
        }

        return new ProcessDetail(
            pid,
            name ?? "未知进程",
            path,
            commandLine,
            startTime,
            parentPid,
            owner,
            workingSet,
            threadCount,
            handleCount,
            string.Join("；", notes));
    }

    private static string? GetString(ManagementObject obj, string key)
        => obj[key] as string is { Length: > 0 } value ? value : null;

    private static int? GetInt(ManagementObject obj, string key)
    {
        var value = obj[key];
        return value is null ? null : Convert.ToInt32(value);
    }

    private static long? GetLong(ManagementObject obj, string key)
    {
        var value = obj[key];
        return value is null ? null : Convert.ToInt64(value);
    }

    private static DateTime? GetDate(ManagementObject obj, string key)
        => GetString(obj, key) is { } text
            ? Safe(() => ManagementDateTimeConverter.ToDateTime(text))
            : null;

    private static string? GetOwner(ManagementObject obj)
    {
        try
        {
            var args = new object[] { "", "", "" };
            obj.InvokeMethod("GetOwner", args);
            var user = args[0] as string;
            if (string.IsNullOrWhiteSpace(user)) return null;
            var domain = args[1] as string;
            return string.IsNullOrWhiteSpace(domain) ? user : $"{domain}\\{user}";
        }
        catch
        {
            return null; // 所有者读取常被权限策略拒绝，不影响其他字段
        }
    }

    private static T? Try<T>(Func<T> action) where T : class? => Safe(action);

    private static int? Try(Func<int> action) => Safe(action);

    private static long? Try(Func<long> action) => Safe(action);

    private static DateTime? TryDate(Func<DateTime> action) => Safe(action);

    private static T? Safe<T>(Func<T> action)
    {
        try { return action(); }
        catch { return default; }
    }
}
