namespace WinTools.Tools.SystemTools;

/// <summary>
/// 批量端口查询结果。Results 已按端口号升序排列且去重。
/// Status：ok（查询完成）/ error（解析或采集失败）。
/// </summary>
/// <param name="Results">逐端口结果；空闲端口只记录状态，不记录进程信息</param>
/// <param name="Status">ok / error</param>
/// <param name="Message">错误说明；成功时为空</param>
/// <param name="RequestedCount">本次实际查询的端口数量</param>
public sealed record PortQueryBatchResult(
    IReadOnlyList<PortQueryResult> Results,
    string Status,
    string Message,
    int RequestedCount)
{
    public static PortQueryBatchResult Ok(IReadOnlyList<PortQueryResult> results, int requestedCount)
        => new(results, "ok", string.Empty, requestedCount);

    public static PortQueryBatchResult Error(string message, int requestedCount = 0)
        => new(Array.Empty<PortQueryResult>(), "error", message, requestedCount);

    public int OccupiedCount => Results.Count(r => r.Status == "occupied");

    public int FreeCount => Results.Count(r => r.Status == "free");
}
