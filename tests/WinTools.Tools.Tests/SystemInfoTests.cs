using WinTools.Tools.SystemTools;
using Xunit;

namespace WinTools.Tools.Tests;

/// <summary>
/// SystemInfoCollector 单元测试，实现阶段七 S1 容错单测清单与 S1-AC4。
/// 关键：通过 CollectFrom 注入读取源，验证"某项失败只影响该项"。
/// </summary>
public sealed class SystemInfoTests
{
    // 阶段七 Read_WhenFieldThrows_MarksUnavailable + S1-AC4
    [Fact]
    public void CollectFrom_WhenFieldThrows_MarksUnavailableAndKeepsOthers()
    {
        var sources = new Dictionary<string, Func<string>>
        {
            ["正常字段"] = () => "正常值",
            ["故障字段"] = () => throw new InvalidOperationException("模拟读取失败"),
            ["另一个字段"] = () => "另一个值"
        };

        var result = SystemInfoCollector.CollectFrom(sources);

        Assert.Equal(3, result.Count);
        var broken = result.Single(i => i.Name == "故障字段");
        Assert.False(broken.Available);
        Assert.Equal("不可用", broken.Value);
        // 其余字段不受影响
        Assert.True(result.Single(i => i.Name == "正常字段").Available);
        Assert.Equal("正常值", result.Single(i => i.Name == "正常字段").Value);
        Assert.True(result.Single(i => i.Name == "另一个字段").Available);
    }

    // 空值也标记为不可用（读到了但无有效内容）
    [Fact]
    public void CollectFrom_EmptyValue_MarksUnavailable()
    {
        var sources = new Dictionary<string, Func<string>>
        {
            ["空字段"] = () => string.Empty
        };

        var result = SystemInfoCollector.CollectFrom(sources);

        Assert.False(result[0].Available);
        Assert.Equal("不可用", result[0].Value);
    }

    // 多个字段同时失败，全部各自标记不可用，不抛出
    [Fact]
    public void CollectFrom_MultipleFailures_DoesNotThrow()
    {
        var sources = new Dictionary<string, Func<string>>
        {
            ["A"] = () => throw new Exception("fail A"),
            ["B"] = () => throw new Exception("fail B")
        };

        var result = SystemInfoCollector.CollectFrom(sources);

        Assert.All(result, item =>
        {
            Assert.False(item.Available);
            Assert.Equal("不可用", item.Value);
        });
    }

    // S1-AC1：真实 Collect() 包含核心字段且自身不抛异常（普通权限即可）
    [Fact]
    public void Collect_ReturnsCoreFields_WithoutThrowing()
    {
        var result = SystemInfoCollector.Collect();

        // 至少包含阶段二要求的核心字段
        Assert.Contains(result, i => i.Name == "CPU 型号");
        Assert.Contains(result, i => i.Name == "物理内存");
        Assert.Contains(result, i => i.Name == "操作系统");
        Assert.Contains(result, i => i.Name == "磁盘容量");
        // 每项都有名称与值（即使不可用也有"不可用"占位）
        Assert.All(result, i => Assert.False(string.IsNullOrWhiteSpace(i.Value)));
    }

    // 字节格式化辅助函数的正确性
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(1024 * 1024, "1 MB")]
    [InlineData(1024L * 1024 * 1024, "1 GB")]
    public void FormatBytes_FormatsHumanReadable(long bytes, string expected)
    {
        Assert.Equal(expected, SystemInfoCollector.FormatBytes(bytes));
    }
}
