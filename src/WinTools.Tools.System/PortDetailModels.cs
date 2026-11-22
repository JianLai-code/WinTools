using System.Globalization;

namespace WinTools.Tools.SystemTools;

/// <summary>netstat 输出中的一条连接记录（TCP/UDP 统一表示）。</summary>
/// <param name="Protocol">TCP / UDP</param>
/// <param name="LocalAddress">本地监听地址，如 0.0.0.0、[::]、127.0.0.1</param>
/// <param name="LocalPort">本地端口</param>
/// <param name="RemoteAddress">远端地址；UDP 通常为 *:*</param>
/// <param name="State">连接状态；UDP 无固定状态，记为 "—"</param>
/// <param name="Pid">占用进程 PID</param>
public sealed record PortConnection(
    string Protocol,
    string LocalAddress,
    int LocalPort,
    string RemoteAddress,
    string State,
    int Pid)
{
    public bool IsListening => State.Equals("LISTENING", StringComparison.OrdinalIgnoreCase);

    /// <summary>是否监听在所有网卡（含 IPv6 通配）。</summary>
    public bool IsWildcard =>
        LocalAddress is "0.0.0.0" or "::" or "[::]" or "*";

    /// <summary>是否只监听本地回环。</summary>
    public bool IsLoopback =>
        LocalAddress.StartsWith("127.", StringComparison.Ordinal)
        || LocalAddress is "::1" or "[::1]";

    public string Endpoint => $"{LocalAddress}:{LocalPort}";
}

/// <summary>进程全量信息；任何字段读取失败都保留其他字段，并汇总到 Note。</summary>
public sealed record ProcessDetail(
    int Pid,
    string Name,
    string? ExecutablePath,
    string? CommandLine,
    DateTime? StartTime,
    int? ParentPid,
    string? Owner,
    long? WorkingSetBytes,
    int? ThreadCount,
    int? HandleCount,
    string Note)
{
    public static ProcessDetail Unknown(int pid, string note)
        => new(pid, "未知进程", null, null, null, null, null, null, null, null, note);

    /// <summary>进程运行时长；系统没有端口级计时器，用它近似端口占用时长。</summary>
    public TimeSpan? Age => StartTime is { } start
        ? TimeSpan.FromTicks(Math.Max(0, DateTime.Now.Ticks - start.Ticks))
        : null;
}

/// <summary>单个被占用端口的完整画像。</summary>
public sealed record PortOccupancy(
    int Port,
    IReadOnlyList<PortConnection> Connections,
    IReadOnlyList<ProcessDetail> Processes,
    string RiskLevel,
    IReadOnlyList<string> RiskReasons)
{
    public IReadOnlyList<int> Pids => Connections.Select(c => c.Pid).Distinct().OrderBy(p => p).ToArray();

    public string Protocols => string.Join(" / ",
        Connections.Select(c => c.Protocol).Distinct().OrderBy(p => p));

    public string LocalAddresses => string.Join("、",
        Connections.Select(c => c.Endpoint).Distinct().OrderBy(s => s));

    public string RemoteAddresses => string.Join("、",
        Connections.Where(c => !string.IsNullOrWhiteSpace(c.RemoteAddress) && c.RemoteAddress != "*:*")
            .Select(c => c.RemoteAddress).Distinct().Take(3));

    /// <summary>状态按数量聚合，如「LISTENING×2、ESTABLISHED×3」。</summary>
    public string States => string.Join("、", Connections
        .GroupBy(c => c.State)
        .OrderByDescending(g => g.Count())
        .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
        .Select(g => g.Count() > 1 ? $"{g.Key}×{g.Count()}" : g.Key));

    /// <summary>占用时长按最早启动的进程估算。</summary>
    public TimeSpan? Age => Processes
        .Select(p => p.Age)
        .Where(a => a.HasValue)
        .OrderByDescending(a => a!.Value)
        .FirstOrDefault();
}

/// <summary>批量详情查询结果：Occupancies 只含被占用的端口，其余视为空闲。</summary>
public sealed record PortQueryDetailResult(
    IReadOnlyList<PortOccupancy> Occupancies,
    string Status,
    string Message,
    int RequestedCount)
{
    public static PortQueryDetailResult Ok(IReadOnlyList<PortOccupancy> occupancies, int requestedCount)
        => new(occupancies, "ok", string.Empty, requestedCount);

    public static PortQueryDetailResult Error(string message, int requestedCount = 0)
        => new(Array.Empty<PortOccupancy>(), "error", message, requestedCount);

    public int OccupiedCount => Occupancies.Count;
    public int FreeCount => Math.Max(0, RequestedCount - Occupancies.Count);
    public int HighRiskCount => Occupancies.Count(o => o.RiskLevel == "high");
    public int MediumRiskCount => Occupancies.Count(o => o.RiskLevel == "medium");
}

/// <summary>把时长格式化成中文可读形式。</summary>
public static class PortDetailFormat
{
    public static string FormatAge(TimeSpan? age)
    {
        if (age is not { } span) return "未知（进程启动时间不可读）";
        if (span.TotalDays >= 1)
            return $"{(int)span.TotalDays} 天 {span.Hours:00}:{span.Minutes:00}:{span.Seconds:00}";
        if (span.TotalHours >= 1)
            return $"{(int)span.TotalHours} 小时 {span.Minutes:00} 分 {span.Seconds:00} 秒";
        if (span.TotalMinutes >= 1)
            return $"{(int)span.TotalMinutes} 分 {span.Seconds:00} 秒";
        return $"{span.Seconds} 秒";
    }

    public static string FormatBytes(long? bytes)
        => bytes is { } value ? $"{value / 1024.0 / 1024.0:F1} MB" : "未知";

    public static string FormatTime(DateTime? time)
        => time is { } value ? value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.CurrentCulture) : "未知";
}
