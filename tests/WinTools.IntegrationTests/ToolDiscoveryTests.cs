using WinTools.Shell;
using Xunit;

namespace WinTools.IntegrationTests;

/// <summary>
/// 集成测试：验证"工具自动发现"这一架构核心（阶段七集成测试第 1 项）。
/// 测试输出目录里有 WinTools.Tools.Hello.dll，发现器应能找到它。
/// </summary>
public sealed class ToolDiscoveryTests
{
    [Fact]
    public void DiscoverFromDirectory_FindsHelloTool()
    {
        var dir = AppContext.BaseDirectory;
        var tools = ToolDiscovery.DiscoverFromDirectory(dir);

        Assert.Contains(tools, t => t.Id == "hello" && t.Name == "示例工具");
    }

    [Fact]
    public void DiscoverFromDirectory_FindsJsonFormatterTool()
    {
        // 迭代 1 新增的 JSON 格式化工具应能被自动发现，宿主代码零改动。
        var dir = AppContext.BaseDirectory;
        var tools = ToolDiscovery.DiscoverFromDirectory(dir);

        Assert.Contains(tools, t => t.Id == "json-formatter" && t.Name == "JSON 格式化");
    }

    [Fact]
    public void DiscoverFromDirectory_EmptyDirectory_ReturnsEmpty()
    {
        var emptyDir = Path.Combine(Path.GetTempPath(), $"wintools-empty-{Guid.NewGuid():N}");
        Directory.CreateDirectory(emptyDir);
        try
        {
            var tools = ToolDiscovery.DiscoverFromDirectory(emptyDir);
            Assert.Empty(tools);
        }
        finally
        {
            Directory.Delete(emptyDir);
        }
    }

    [Fact]
    public void DiscoverFromDirectory_FoundToolCanCreateUI()
    {
        var tools = ToolDiscovery.DiscoverFromDirectory(AppContext.BaseDirectory);
        var hello = tools.FirstOrDefault(t => t.Id == "hello");

        Assert.NotNull(hello);
        var ui = hello!.CreateUI();
        Assert.NotNull(ui);
    }
}
