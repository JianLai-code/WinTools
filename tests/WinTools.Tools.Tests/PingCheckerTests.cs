using System.Net;
using System.Net.NetworkInformation;
using WinTools.Tools.SystemTools;
using Xunit;

namespace WinTools.Tools.Tests;

public class PingCheckerTests
{
    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("  localhost  ", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("https://example.com", false)]
    [InlineData("bad host", false)]
    [InlineData("*.example.com", false)]
    public void TryValidateTarget_AcceptsHostOrIpOnly(string input, bool expected)
    {
        var ok = PingChecker.TryValidateTarget(input, out var target, out var error);
        Assert.Equal(expected, ok);
        if (ok)
        {
            Assert.Equal(input.Trim(), target);
            Assert.Equal(string.Empty, error);
        }
        else Assert.NotEmpty(error);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(7, 7)]
    [InlineData(999, PingChecker.MaxCount)]
    public void ClampCount_StaysInRange(int input, int expected) =>
        Assert.Equal(expected, PingChecker.ClampCount(input));

    [Fact]
    public async Task RunAsync_MixedReplies_ComputesLossAndLatency()
    {
        var replies = new[]
        {
            new PingProbeResult(1, "127.0.0.1", 10, IPStatus.Success),
            new PingProbeResult(2, "127.0.0.1", 30, IPStatus.Success),
            new PingProbeResult(3, "-", null, IPStatus.TimedOut),
            new PingProbeResult(4, "127.0.0.1", 20, IPStatus.Success),
        };
        var i = 0;
        var summary = await PingChecker.RunAsync("127.0.0.1", replies.Length, (_, _) =>
            Task.FromResult(replies[i++]));

        Assert.Equal(4, summary.Sent);
        Assert.Equal(3, summary.Received);
        Assert.Equal(1, summary.Lost);
        Assert.Equal(25, summary.LossPercent);
        Assert.Equal(10, summary.MinMs);
        Assert.Equal(30, summary.MaxMs);
        Assert.Equal(20, summary.AvgMs);
        Assert.Contains("超时", summary.Format());
        Assert.Contains("丢包率 25%", summary.Format());
    }

    [Fact]
    public async Task RunAsync_AllFailures_NoLatencyAndNoDivisionByZero()
    {
        var summary = await PingChecker.RunAsync("10.255.255.1", 2, (_, _) =>
            Task.FromResult(new PingProbeResult(1, "-", null, IPStatus.DestinationHostUnreachable)));

        Assert.Equal(0, summary.Received);
        Assert.Equal(100, summary.LossPercent);
        Assert.Null(summary.MinMs);
        Assert.Null(summary.AvgMs);
        Assert.Contains("目标主机不可达", summary.Format());
    }

    [Fact]
    public async Task RunAsync_Canceled_StopsBeforeNextProbe()
    {
        using var cts = new CancellationTokenSource();
        var calls = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            PingChecker.RunAsync("localhost", 4, (_, _) =>
            {
                calls++;
                cts.Cancel();
                return Task.FromResult(new PingProbeResult(1, "::1", 1, IPStatus.Success));
            }, cts.Token));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task RunAsync_WithInterval_WaitsBetweenProbesButNotAfterLast()
    {
        var probeTimes = new List<long>();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        await PingChecker.RunAsync("127.0.0.1", 3, (seq, _) =>
        {
            probeTimes.Add(clock.ElapsedMilliseconds);
            return Task.FromResult(new PingProbeResult(seq, "::1", 1, IPStatus.Success));
        }, intervalMs: 60);

        Assert.Equal(3, probeTimes.Count);
        // 第 2、3 次探测前各等了一个间隔；两次间隔都存在且大致相等（放宽下界避免 CI 抖动）。
        Assert.True(probeTimes[1] >= 40, $"第一次间隔太短：{probeTimes[1]}ms");
        Assert.True(probeTimes[2] - probeTimes[1] >= 40, $"第二次间隔太短：{probeTimes[2] - probeTimes[1]}ms");
        // 总耗时不应包含最后一次之后的等待（否则会多出一个完整间隔）。
        Assert.True(clock.ElapsedMilliseconds - probeTimes[2] < 40,
            $"最后一次探测后仍等待：{clock.ElapsedMilliseconds - probeTimes[2]}ms");
    }

    [Fact]
    public async Task RunAsync_CancelDuringInterval_StopsImmediatelyWithoutNextProbe()
    {
        using var cts = new CancellationTokenSource();
        var calls = 0;
        var task = PingChecker.RunAsync("127.0.0.1", 20, (seq, _) =>
        {
            calls++;
            if (calls == 1) cts.CancelAfter(20); // 第一次探测完成后、间隔等待期间取消
            return Task.FromResult(new PingProbeResult(seq, "::1", 1, IPStatus.Success));
        }, cts.Token, intervalMs: 5000);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Describe_CommonFailures_AreChineseAndActionable()
    {
        Assert.Equal("超时：目标未响应，检查防火墙或目标是否在线", PingChecker.Describe(IPStatus.TimedOut));
        Assert.Equal("成功", PingChecker.Describe(IPStatus.Success));
        Assert.Contains("网络", PingChecker.Describe(IPStatus.DestinationNetworkUnreachable));
    }
}
