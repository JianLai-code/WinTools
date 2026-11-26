using System.Drawing;
using WinTools.Core.Ui;
using Xunit;

namespace WinTools.Core.Tests;

/// <summary>
/// 圆角路径的边界：控件在 WmCreate 阶段会以 0×0 触发一次尺寸变更，
/// 若这里抛 ArgumentException，悬停提示等浮层首次显示就会失败（0.13.0 实测踩到）。
/// </summary>
public sealed class UiThemeRoundedPathTests
{
    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(0f, 20f)]
    [InlineData(20f, 0f)]
    [InlineData(1f, 1f)]
    [InlineData(3f, 40f)]
    public void 非正或极小尺寸不抛异常(float w, float h)
    {
        using var path = UiTheme.RoundedPath(new RectangleF(0, 0, w, h), 6);
        Assert.NotNull(path);
    }

    [Fact]
    public void 圆角半径不超过短边一半()
    {
        using var path = UiTheme.RoundedPath(new RectangleF(0, 0, 100, 20), 40);
        var bounds = path.GetBounds();
        Assert.True(bounds.Width <= 100.5f, $"路径宽度 {bounds.Width} 超出矩形");
        Assert.True(bounds.Height <= 20.5f, $"路径高度 {bounds.Height} 超出矩形");
    }
}
