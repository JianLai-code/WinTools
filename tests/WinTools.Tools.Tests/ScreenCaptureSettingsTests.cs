using WinTools.Core;
using WinTools.Tools.Files;
using Xunit;

namespace WinTools.Tools.Tests;

/// <summary>
/// 截图工具的全局热键/自动复制设置与后台能力接口。
/// 这里只覆盖纯逻辑与契约，不触碰热键注册（那需要真实消息循环）与磁盘。
/// </summary>
public class ScreenCaptureSettingsTests
{
    [Fact]
    public void Defaults_UseWeChatStyleHotkeysAndAutoCopy()
    {
        var settings = new ScreenCaptureSettings();
        Assert.True(settings.AutoCopy);
        Assert.Equal("Ctrl+Shift+A", settings.RegionHotkey);
        Assert.Equal("Ctrl+Shift+F", settings.FullHotkey);
        Assert.Equal("Ctrl+Shift+S", settings.ScrollHotkey);
    }

    [Fact]
    public void DefaultHotkeys_AreParseableAndValid()
    {
        var settings = new ScreenCaptureSettings();
        Assert.True(HotkeyGesture.TryParse(settings.RegionHotkey, out var region));
        Assert.True(HotkeyGesture.TryParse(settings.FullHotkey, out var full));
        Assert.True(HotkeyGesture.TryParse(settings.ScrollHotkey, out var scroll));
        Assert.True(region.IsValid);
        Assert.True(full.IsValid);
        Assert.True(scroll.IsValid);
        Assert.NotEqual(region, full);
        Assert.NotEqual(region, scroll);
        Assert.NotEqual(full, scroll);
    }

    [Fact]
    public void ScreenshotTool_ExposesBackgroundAndCommandCapabilities()
    {
        var tool = new ScreenshotTool();
        Assert.IsAssignableFrom<IBackgroundTool>(tool);
        Assert.IsAssignableFrom<IToolCommand>(tool);
        Assert.Equal("screen-capture", tool.Id);
    }

    [Fact]
    public void TryInvoke_IgnoresUnknownCommand()
    {
        // 未知命令必须返回 false 交给宿主兜底，且不能触发任何截图副作用。
        Assert.False(new ScreenshotTool().TryInvoke("no-such-command"));
    }
}
