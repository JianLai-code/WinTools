namespace WinTools.Tools.SystemTools;

/// <summary>
/// 端口查询结果。对应 S2-AC1~AC4。
/// 三种状态：非法输入 / 空闲 / 占用（带进程名与 PID）。
/// </summary>
/// <param name="Port">查询的端口号（-1 表示非法输入）</param>
/// <param name="Status">状态：invalid / free / occupied / error</param>
/// <param name="ProcessName">占用进程名；空闲或未知时为 null</param>
/// <param name="Pid">占用进程 PID；无则为 null</param>
/// <param name="Message">展示给用户的说明文字</param>
public sealed record PortQueryResult(
    int Port,
    string Status,
    string? ProcessName,
    int? Pid,
    string Message)
{
    public static PortQueryResult Invalid(string message) => new(-1, "invalid", null, null, message);

    public static PortQueryResult Free(int port) => new(port, "free", null, null, $"端口 {port} 未被占用");

    public static PortQueryResult Occupied(int port, string processName, int pid)
        => new(port, "occupied", processName, pid, $"端口 {port} 被 {processName}（PID {pid}）占用");

    public static PortQueryResult Error(int port, string message) => new(port, "error", null, null, message);
}
