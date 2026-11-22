using System.Collections;
using WinTools.Tools.SystemTools;
using Xunit;

namespace WinTools.Tools.Tests;

/// <summary>
/// 环境变量读取/筛选/复制格式的纯逻辑单测。用注入的字典代替真实环境，稳定可复现。
/// </summary>
public sealed class EnvironmentVariableTests
{
    private static Hashtable Sample() => new()
    {
        ["Path"] = "C:\\Windows",
        ["alpha"] = "one",
        ["Beta"] = "TWO",
        ["EMPTY"] = null!,
        [""] = "should-be-skipped",
    };

    [Fact]
    public void CollectFrom_SortsByNameCaseInsensitive_AndSkipsEmptyName()
    {
        var items = EnvironmentVariableCollector.CollectFrom(Sample());

        Assert.Equal(new[] { "alpha", "Beta", "EMPTY", "Path" },
            items.Select(x => x.Name));
        Assert.DoesNotContain(items, x => x.Name.Length == 0);
    }

    [Fact]
    public void CollectFrom_NullValue_BecomesEmptyString()
    {
        var items = EnvironmentVariableCollector.CollectFrom(Sample());

        Assert.Equal(string.Empty, items.Single(x => x.Name == "EMPTY").Value);
    }

    [Fact]
    public void Filter_ByNameOrValue_IsCaseInsensitive()
    {
        var items = EnvironmentVariableCollector.CollectFrom(Sample());

        Assert.Equal("Beta", Assert.Single(EnvironmentVariableCollector.Filter(items, "beta")).Name);
        Assert.Equal("Path", Assert.Single(EnvironmentVariableCollector.Filter(items, "windows")).Name);
    }

    [Fact]
    public void Filter_EmptyKeyword_ReturnsAll()
    {
        var items = EnvironmentVariableCollector.CollectFrom(Sample());

        Assert.Equal(items.Count, EnvironmentVariableCollector.Filter(items, "   ").Count);
    }

    [Fact]
    public void FormatForCopy_ProducesNameEqualsValueLines()
    {
        var items = EnvironmentVariableCollector.Filter(
            EnvironmentVariableCollector.CollectFrom(Sample()), "alpha");

        var text = EnvironmentVariableCollector.FormatForCopy(items);

        Assert.Equal("alpha=one" + Environment.NewLine, text);
    }

    [Fact]
    public void Collect_ReadsCurrentProcessEnvironment()
    {
        var items = EnvironmentVariableCollector.Collect();

        Assert.NotEmpty(items);
        Assert.Contains(items, x => x.Name.Equals("Path", StringComparison.OrdinalIgnoreCase));
    }
}
