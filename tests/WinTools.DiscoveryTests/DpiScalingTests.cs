using System.Windows.Forms;
using WinTools.Core;
using WinTools.Core.Ui;
using WinTools.Shell;
using Xunit;

namespace WinTools.DiscoveryTests;

/// <summary>
/// 高 DPI 回归：布局助手（Pad/Sz/Sx）只接受 100% 设计像素，内部统一缩放。
/// 曾经有人把 Sx() 的结果再传进 Pad() 造成双重缩放——100% 开发屏上数值碰巧不变，
/// 200% 用户屏上边距翻倍、按钮被裁一半。本类把缩放切到 200% 再建 UI，让双重缩放现形。
/// UiTheme.S 是静态全局，测试必须与 ToolDiscoveryTests 串行（同 Collection）。
/// </summary>
[Collection("ui")]
public sealed class DpiScalingTests
{
    [Fact]
    public void HashAt200Dpi_ButtonBarPaddingNotDoubleScaled()
    {
        var tools = CreateToolsAt200Dpi();
        try
        {
            var tool = tools.First(t => t.Id == "hash-checker");
            using var ui = tool.CreateUI();

            var bar = Descendants<FlowLayoutPanel>(ui)
                .Single(p => p.Controls.Cast<Control>().Any(c => c.Text == "选择文件"));
            // 双重缩放时这里是 Sx(28) 而不是 Sx(14)，按钮会被推到条的底边外裁掉一半。
            Assert.Equal(UiTheme.Sx(14), bar.Padding.Top);
            Assert.Equal(UiTheme.Sx(46), bar.Height);
            // 上边距 + 按钮高恰好用尽条高：按钮下边缘不越过条底边（等距设计的前提）。
            Assert.True(bar.Padding.Top + UiTheme.Sx(32) <= bar.Height,
                $"200% 下按钮会被条底边裁切：padding {bar.Padding.Top} + 按钮 {UiTheme.Sx(32)} > 条高 {bar.Height}。");
        }
        finally
        {
            UiTheme.SetScaleFromDpi(96f);
        }
    }

    [Fact]
    public void ImageCompressAt200Dpi_QualityLabelsVerticallyCentered()
    {
        var tools = CreateToolsAt200Dpi();
        try
        {
            var tool = tools.First(t => t.Id == "image-compress");
            using var ui = tool.CreateUI();

            var numeric = Descendants<FluentNumeric>(ui).Single();
            var label = Descendants<Label>(ui).Single(l => l.Text == "压缩质量");
            Assert.Equal(UiTheme.Sx(32), numeric.Height);
            Assert.Equal(Math.Max(0, (UiTheme.Sx(32) - label.Height) / 2), label.Margin.Top);
        }
        finally
        {
            UiTheme.SetScaleFromDpi(96f);
        }
    }

    [Fact]
    public void FolderSizeAt200Dpi_UniformRowGaps()
    {
        var tools = CreateToolsAt200Dpi();
        try
        {
            var tool = tools.First(t => t.Id == "folder-size-analyzer");
            using var ui = tool.CreateUI();

            var pathInput = Descendants<FluentInput>(ui).Single();
            Assert.Equal(DockStyle.Top, pathInput.Dock);
            Assert.Equal(UiTheme.Sx(32), pathInput.Height);
        }
        finally
        {
            UiTheme.SetScaleFromDpi(96f);
        }
    }

    private static IReadOnlyList<ITool> CreateToolsAt200Dpi()
    {
        UiTheme.SetScaleFromDpi(192f);
        return ToolDiscovery.DiscoverFromDirectory(AppContext.BaseDirectory);
    }

    private static IEnumerable<T> Descendants<T>(Control parent) where T : Control
    {
        foreach (Control child in parent.Controls)
        {
            if (child is T matched) yield return matched;
            foreach (var sub in Descendants<T>(child)) yield return sub;
        }
    }
}
