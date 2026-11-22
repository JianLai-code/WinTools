using WinTools.Shell;
using Xunit;

namespace WinTools.DiscoveryTests;

/// <summary>
/// 阶段三验收的"关键反证"：工具的去留完全由 DLL 是否存在决定，宿主源码零改动。
///  simulates: 放入 DLL → 发现工具；删掉 DLL → 工具消失。
/// </summary>
[Collection("ui")]
public sealed class DiscoveryContrastTests
{
    [Fact]
    public void AddingAndRemovingDll_ChangesDiscovery_WithoutHostChanges()
    {
        // 建一个隔离的临时目录，模拟"应用目录"。
        var dir = Path.Combine(Path.GetTempPath(), $"wintools-contrast-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            // 步骤 1：空目录 —— 发现不到任何工具。
            Assert.Empty(ToolDiscovery.DiscoverFromDirectory(dir));

            // 步骤 2：把工具 DLL 复制进来 —— 立刻能发现。
            var sourceDll = Path.Combine(AppContext.BaseDirectory, "WinTools.Tools.Hello.dll");
            var targetDll = Path.Combine(dir, "WinTools.Tools.Hello.dll");
            File.Copy(sourceDll, targetDll);
            var afterAdd = ToolDiscovery.DiscoverFromDirectory(dir);
            Assert.Contains(afterAdd, t => t.Id == "hello");

            // 步骤 3：删掉 DLL —— 工具随之消失，宿主代码一行未改。
            File.Delete(targetDll);
            var afterRemove = ToolDiscovery.DiscoverFromDirectory(dir);
            Assert.DoesNotContain(afterRemove, t => t.Id == "hello");
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}
