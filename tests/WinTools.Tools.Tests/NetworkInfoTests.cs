using System.Net.NetworkInformation;
using WinTools.Tools.SystemTools;
using Xunit;

namespace WinTools.Tools.Tests;

/// <summary>
/// 网络信息工具的纯逻辑测试：字段容错、真实采集不抛异常、格式化函数正确。
/// 不依赖外网、不依赖管理员权限，可稳定重复运行。
/// </summary>
public sealed class NetworkInfoTests
{
    [Fact]
    public void CollectFrom_WhenFieldThrows_MarksUnavailableAndKeepsOthers()
    {
        var sources = new Dictionary<string, Func<string>>
        {
            ["正常字段"] = () => "正常值",
            ["故障字段"] = () => throw new InvalidOperationException("模拟读取失败"),
            ["另一个字段"] = () => "另一个值"
        };

        var result = NetworkInfoCollector.CollectFrom(sources);

        Assert.Equal(3, result.Count);
        var broken = result.Single(i => i.Name == "故障字段");
        Assert.False(broken.Available);
        Assert.Equal("不可用", broken.Value);
        Assert.True(result.Single(i => i.Name == "正常字段").Available);
        Assert.Equal("正常值", result.Single(i => i.Name == "正常字段").Value);
        Assert.True(result.Single(i => i.Name == "另一个字段").Available);
    }

    [Fact]
    public void CollectFrom_EmptyValue_MarksUnavailable()
    {
        var sources = new Dictionary<string, Func<string>>
        {
            ["空字段"] = () => "   "
        };

        var result = NetworkInfoCollector.CollectFrom(sources);

        Assert.False(result[0].Available);
        Assert.Equal("不可用", result[0].Value);
    }

    [Fact]
    public void Collect_ReturnsCoreFields_WithoutThrowing()
    {
        var result = NetworkInfoCollector.Collect();

        Assert.Contains(result.Summary, i => i.Name == "计算机名");
        Assert.Contains(result.Summary, i => i.Name == "主 IPv4 地址");
        Assert.Contains(result.Summary, i => i.Name == "默认网关");
        Assert.Contains(result.Summary, i => i.Name == "DNS 服务器");
        Assert.Contains(result.Summary, i => i.Name == "网卡数量");
        Assert.All(result.Summary, i => Assert.False(string.IsNullOrWhiteSpace(i.Value)));
        Assert.NotNull(result.Adapters);
    }

    [Fact]
    public void Collect_AdaptersContainExpectedFields()
    {
        var result = NetworkInfoCollector.Collect();

        foreach (var adapter in result.Adapters)
        {
            Assert.False(string.IsNullOrWhiteSpace(adapter.Name));
            if (!adapter.Available) continue; // 单块网卡失败时仍保留占位，不影响其他网卡
            Assert.Contains(adapter.Fields, f => f.Name == "IPv4 地址");
            Assert.Contains(adapter.Fields, f => f.Name == "默认网关");
            Assert.Contains(adapter.Fields, f => f.Name == "DNS 服务器");
            Assert.Contains(adapter.Fields, f => f.Name == "MAC 地址");
        }
    }

    [Fact]
    public void DescribeAdapters_SkipsLoopbackInterface()
    {
        var all = NetworkInfoCollector.SafeGetInterfaces();
        if (!all.Any(n => n.NetworkInterfaceType == NetworkInterfaceType.Loopback)) return;

        var result = NetworkInfoCollector.DescribeAdapters(all);

        Assert.DoesNotContain(result, a => a.Kind == "回环");
    }

    [Fact]
    public void FormatAddresses_DeduplicatesAndUsesEmptyPlaceholder()
    {
        Assert.Equal("未配置",
            NetworkInfoCollector.FormatAddresses(new[] { "", "  " }, "未配置"));
        Assert.Equal("1.1.1.1、8.8.8.8",
            NetworkInfoCollector.FormatAddresses(new[] { "1.1.1.1", "8.8.8.8", "1.1.1.1" }));
    }

    [Theory]
    [InlineData(0, "未知")]
    [InlineData(1_500_000, "1.5 Mbps")]
    [InlineData(2_000_000_000, "2 Gbps")]
    public void FormatSpeed_FormatsHumanReadable(long bits, string expected)
    {
        Assert.Equal(expected, NetworkInfoCollector.FormatSpeed(bits));
    }

    [Fact]
    public void FormatMac_FormatsBytesAndHandlesMissingAddress()
    {
        var address = new PhysicalAddress(new byte[] { 0x00, 0x1A, 0x2B, 0x3C, 0x4D, 0x5E });

        Assert.Equal("00-1A-2B-3C-4D-5E", NetworkInfoCollector.FormatMac(address));
        Assert.Equal("无（虚拟网卡）", NetworkInfoCollector.FormatMac(null));
    }
}
