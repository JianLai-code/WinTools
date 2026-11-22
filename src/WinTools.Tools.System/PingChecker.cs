using System.Net;
using System.Net.NetworkInformation;
using System.Text;

namespace WinTools.Tools.SystemTools;

/// <summary>单次探测结果。Status 保留枚举便于测试，RoundTripMs 仅在成功时有值。</summary>
public sealed record PingProbeResult(int Sequence, string Address, long? RoundTripMs, IPStatus Status)
{
    public bool Success => Status == IPStatus.Success;
}

/// <summary>一组探测的汇总。统计与格式化都是纯函数，不依赖真实网络。</summary>
public sealed record PingSummary(string Target, IReadOnlyList<PingProbeResult> Replies)
{
    public int Sent => Replies.Count;
    public int Received => Replies.Count(r => r.Success);
    public int Lost => Sent - Received;
    public double LossPercent => Sent == 0 ? 0 : Lost * 100.0 / Sent;

    private IReadOnlyList<long> Times => Replies.Where(r => r.Success && r.RoundTripMs.HasValue)
        .Select(r => r.RoundTripMs!.Value).ToList();

    public long? MinMs { get { var t = Times; return t.Count == 0 ? null : t.Min(); } }
    public long? MaxMs { get { var t = Times; return t.Count == 0 ? null : t.Max(); } }
    public double? AvgMs { get { var t = Times; return t.Count == 0 ? null : t.Average(); } }

    public string Format()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"目标：{Target}");
        foreach (var r in Replies)
        {
            var detail = r.Success
                ? $"{r.RoundTripMs} ms（{r.Address}）"
                : PingChecker.Describe(r.Status);
            sb.AppendLine($"#{r.Sequence}  {detail}");
        }
        sb.AppendLine("—");
        sb.AppendLine($"发送 {Sent}，接收 {Received}，丢失 {Lost}（丢包率 {LossPercent:F0}%）");
        if (Received > 0)
        {
            var min = MinMs!.Value;
            var max = MaxMs!.Value;
            var avg = AvgMs!.Value;
            sb.AppendLine($"最短 {min} ms，最长 {max} ms，平均 {avg:F1} ms");
        }
        return sb.ToString().TrimEnd();
    }
}

/// <summary>
/// Ping 测试的纯逻辑层：输入校验、探测编排、统计汇总都不碰控件。
/// 真实网络调用由界面层以 probe 委托注入，单测用假 probe 即可覆盖全部统计路径。
/// </summary>
public static class PingChecker
{
    public const int MaxCount = 20;
    public const int DefaultCount = 4;
    public const int DefaultTimeoutMs = 2000;
    /// <summary>探测之间的间隔。本机/网关几乎零延迟，没有间隔时整轮测试几十毫秒就结束，取消按钮形同虚设。</summary>
    public const int DefaultIntervalMs = 400;

    /// <summary>目标只接受主机名或 IP，拒绝空串、空白与明显非法的通配符串。</summary>
    public static bool TryValidateTarget(string? input, out string target, out string error)
    {
        target = (input ?? string.Empty).Trim();
        if (target.Length == 0)
        {
            error = "请输入目标主机名或 IP 地址。";
            return false;
        }
        if (target.Length > 253 || target.Any(char.IsWhiteSpace) || target.Contains('*') || target.Contains('/'))
        {
            error = "目标格式不正确，请只输入主机名或 IP 地址（不要带协议或路径）。";
            return false;
        }
        error = string.Empty;
        return true;
    }

    public static int ClampCount(int count) => Math.Clamp(count, 1, MaxCount);

    /// <summary>
    /// 按序执行 count 次探测；取消在每次探测前和间隔等待中都检查，不让取消后的结果混入。
    /// intervalMs 为探测之间的间隔（毫秒），>0 时也会响应取消；默认 0 保持同步单测的行为。
    /// </summary>
    public static async Task<PingSummary> RunAsync(
        string target,
        int count,
        Func<int, CancellationToken, Task<PingProbeResult>> probe,
        CancellationToken cancellationToken = default,
        int intervalMs = 0)
    {
        ArgumentNullException.ThrowIfNull(probe);
        var results = new List<PingProbeResult>();
        for (var i = 1; i <= ClampCount(count); i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await probe(i, cancellationToken).ConfigureAwait(false));
            // 最后一次探测后不再等待；间隔本身可取消，取消时立即抛出，不会把后续探测混进来。
            if (intervalMs > 0 && i < ClampCount(count))
                await Task.Delay(intervalMs, cancellationToken).ConfigureAwait(false);
        }
        return new PingSummary(target, results);
    }

    /// <summary>把真实 PingReply 转成可测试的结果对象。</summary>
    public static PingProbeResult FromReply(int sequence, PingReply reply) => new(
        sequence,
        reply.Address?.ToString() ?? "未知",
        reply.Status == IPStatus.Success ? reply.RoundtripTime : null,
        reply.Status);

    /// <summary>Ping 状态的中文说明，失败时给用户可操作的提示而不是枚举名。</summary>
    public static string Describe(IPStatus status) => status switch
    {
        IPStatus.Success => "成功",
        IPStatus.TimedOut => "超时：目标未响应，检查防火墙或目标是否在线",
        IPStatus.DestinationHostUnreachable => "目标主机不可达：检查本机路由或网关",
        IPStatus.DestinationNetworkUnreachable => "目标网络不可达：检查网络连接",
        IPStatus.DestinationPortUnreachable => "目标端口不可达",
        IPStatus.NoResources => "系统资源不足，请稍后重试",
        IPStatus.BadDestination => "目标地址无效",
        IPStatus.BadOption => "Ping 选项无效",
        IPStatus.BadRoute => "路由无效",
        IPStatus.TtlExpired => "超过生存时间（TTL）",
        IPStatus.TtlReassemblyTimeExceeded => "分片重组超时",
        IPStatus.ParameterProblem => "目标主机参数错误",
        IPStatus.SourceQuench => "目标主机要求降低发送速率",
        IPStatus.UnrecognizedNextHeader => "目标主机无法识别协议头",
        _ => $"失败：{status}"
    };
}
