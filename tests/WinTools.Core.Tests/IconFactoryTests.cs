using System.Drawing;
using WinTools.Core.Ui;
using Xunit;

namespace WinTools.Core.Tests;

/// <summary>截图标注工具栏使用的图标必须已注册且能正常绘制。</summary>
public sealed class IconFactoryTests
{
    private static readonly string[] ToolbarIcons =
    [
        "annot-rect", "annot-ellipse", "annot-smile", "annot-arrow", "annot-pen",
        "annot-mosaic", "annot-text", "annot-undo", "annot-redo", "annot-copy",
        "annot-save", "annot-pin", "annot-close", "annot-done"
    ];

    [Theory]
    [InlineData("annot-rect")]
    [InlineData("annot-ellipse")]
    [InlineData("annot-smile")]
    [InlineData("annot-arrow")]
    [InlineData("annot-pen")]
    [InlineData("annot-mosaic")]
    [InlineData("annot-text")]
    [InlineData("annot-undo")]
    [InlineData("annot-redo")]
    [InlineData("annot-copy")]
    [InlineData("annot-save")]
    [InlineData("annot-pin")]
    [InlineData("annot-close")]
    [InlineData("annot-done")]
    public void ToolbarIcon_IsRegistered(string name)
    {
        Assert.True(IconFactory.Has(name), $"缺少图标：{name}");
    }

    /// <summary>滚动截图入口用的图标：必须注册，且在 20px 下真的画得出笔画。</summary>
    [Fact]
    public void ScrollIcon_IsRegisteredAndHasInk()
    {
        Assert.True(IconFactory.Has("scroll"));
        using var bitmap = new Bitmap(20, 20);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(Color.Transparent);
            IconFactory.Draw(g, "scroll", new RectangleF(0, 0, 20, 20), Color.Black, 1.45f);
        }
        var ink = 0;
        for (var y = 0; y < 20; y++)
            for (var x = 0; x < 20; x++)
                if (bitmap.GetPixel(x, y).A > 40) ink++;
        Assert.True(ink > 40, $"滚动图标着色像素过少：{ink}");
    }

    [Fact]
    public void ToolbarIcons_CanAllBeDrawn()
    {
        using var bitmap = new Bitmap(32, 32);
        using var g = Graphics.FromImage(bitmap);
        foreach (var name in ToolbarIcons)
            IconFactory.Draw(g, name, new RectangleF(0, 0, 32, 32), Color.FromArgb(52, 62, 72));
    }
}
