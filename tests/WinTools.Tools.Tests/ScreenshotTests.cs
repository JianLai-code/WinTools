using System.Drawing;
using System.Drawing.Imaging;
using WinTools.Tools.Files;
using Xunit;

namespace WinTools.Tools.Tests;

/// <summary>
/// 截图工具纯逻辑单测。抓屏通过 Func&lt;Rectangle, Bitmap&gt; 注入，不依赖测试机显示器，
/// 也不真的把宿主窗口隐藏——所有坐标用普通 Rectangle 验证即可（物理像素语义在 UI 层保证）。
/// </summary>
public class ScreenshotTests
{
    // ── 延时钳制 ──────────────────────────────────────────────
    [Theory]
    [InlineData(-5, 0)]
    [InlineData(0, 0)]
    [InlineData(3, 3)]
    [InlineData(999, ScreenCapture.MaxDelaySeconds)]
    public void ClampDelay_StaysInRange(int input, int expected) =>
        Assert.Equal(expected, ScreenCapture.ClampDelay(input));

    // ── 选区归一化 ────────────────────────────────────────────
    [Fact]
    public void NormalizeSelection_ReverseDrag_ProducesPositiveRect()
    {
        var screen = new Rectangle(0, 0, 1920, 1080);
        var rect = ScreenCapture.NormalizeSelection(new Point(400, 300), new Point(120, 90), screen);
        Assert.Equal(new Rectangle(120, 90, 280, 210), rect);
    }

    [Fact]
    public void NormalizeSelection_OutOfBounds_ClampsToScreen()
    {
        var screen = new Rectangle(-800, 0, 2720, 1080); // 副屏在左，含负坐标
        var rect = ScreenCapture.NormalizeSelection(new Point(-1200, -50), new Point(3000, 2000), screen);
        Assert.Equal(new Rectangle(-800, 0, 2720, 1080), rect);
        Assert.True(rect.Width > 0 && rect.Height > 0);
    }

    [Fact]
    public void NormalizeSelection_ForwardDrag_KeepsCoordinates()
    {
        var screen = new Rectangle(0, 0, 800, 600);
        var rect = ScreenCapture.NormalizeSelection(new Point(10, 20), new Point(200, 300), screen);
        Assert.Equal(new Rectangle(10, 20, 190, 280), rect);
    }

    [Theory]
    [InlineData(0, 0, 4, 4, false)]   // 太小，视为误点
    [InlineData(7, 10, 8, 10, true)]  // 刚好达到最小边长
    [InlineData(0, 0, 100, 100, true)]
    public void IsValidSelection_RespectsMinimumSide(int x, int y, int w, int h, bool expected) =>
        Assert.Equal(expected, ScreenCapture.IsValidSelection(new Rectangle(x, y, w, h)));

    // ── 抓屏（注入假实现）─────────────────────────────────────
    [Fact]
    public void Capture_WithFakeGrabber_ReturnsImage()
    {
        var asked = Rectangle.Empty;
        var result = ScreenCapture.Capture(new Rectangle(1, 2, 30, 40), r =>
        {
            asked = r;
            return new Bitmap(r.Width, r.Height);
        });
        Assert.True(result.Success, result.ErrorMessage);
        Assert.NotNull(result.Image);
        Assert.Equal(30, result.Image!.Width);
        Assert.Equal(40, result.Image.Height);
        Assert.Equal(new Rectangle(1, 2, 30, 40), asked);
        result.Image.Dispose();
    }

    [Fact]
    public void Capture_GrabberThrows_ReturnsFriendlyError()
    {
        var result = ScreenCapture.Capture(new Rectangle(0, 0, 10, 10),
            _ => throw new InvalidOperationException("boom"));
        Assert.False(result.Success);
        Assert.Contains("boom", result.ErrorMessage);
        Assert.Null(result.Image);
    }

    [Fact]
    public void Capture_EmptyBounds_FailsWithoutCallingGrabber()
    {
        var called = false;
        var result = ScreenCapture.Capture(new Rectangle(0, 0, 0, 10), _ => { called = true; return new Bitmap(1, 1); });
        Assert.False(result.Success);
        Assert.False(called);
    }

    [Fact]
    public void Capture_GrabberReturnsEmptyBitmap_Fails()
    {
        var result = ScreenCapture.Capture(new Rectangle(0, 0, 10, 10), _ => new Bitmap(1, 1));
        // 1x1 是合法图：这里验证的是空引用分支
        Assert.True(result.Success);
        result.Image?.Dispose();

        var nullResult = ScreenCapture.Capture(new Rectangle(0, 0, 10, 10), _ => null!);
        Assert.False(nullResult.Success);
        Assert.Contains("空", nullResult.ErrorMessage);
    }

    // ── 裁剪 ─────────────────────────────────────────────────
    [Fact]
    public void Crop_InsideBounds_ReturnsExactRegion()
    {
        using var bmp = MakeSolid(40, 30, Color.Red);
        var result = ScreenCapture.Crop(bmp, new Rectangle(5, 7, 10, 12));
        Assert.True(result.Success, result.ErrorMessage);
        Assert.NotNull(result.Image);
        Assert.Equal(10, result.Image!.Width);
        Assert.Equal(12, result.Image.Height);
        Assert.Equal(Color.Red.ToArgb(), result.Image.GetPixel(0, 0).ToArgb());
        result.Image.Dispose();
    }

    [Fact]
    public void Crop_NoIntersection_Fails()
    {
        using var bmp = MakeSolid(40, 30, Color.Blue);
        var result = ScreenCapture.Crop(bmp, new Rectangle(100, 100, 10, 10));
        Assert.False(result.Success);
        Assert.Contains("交集", result.ErrorMessage);
    }

    [Fact]
    public void Crop_PartiallyOutside_ClipsToBitmap()
    {
        using var bmp = MakeSolid(40, 30, Color.Green);
        var result = ScreenCapture.Crop(bmp, new Rectangle(35, 25, 20, 20));
        Assert.True(result.Success);
        Assert.Equal(5, result.Image!.Width);
        Assert.Equal(5, result.Image.Height);
        result.Image.Dispose();
    }

    // ── 文件名 ────────────────────────────────────────────────
    [Fact]
    public void BuildFileName_UsesTimestampAndExtension()
    {
        var now = new DateTime(2026, 11, 25, 8, 22, 5);
        Assert.Equal("截图_20261125_082205.png", ScreenCapture.BuildFileName(now, "png"));
        Assert.Equal("截图_20261125_082205.jpg", ScreenCapture.BuildFileName(now, ".jpg"));
    }

    [Fact]
    public void BuildFileName_SequenceGreaterThanOne_AppendsSuffix()
    {
        var now = new DateTime(2026, 11, 25, 8, 22, 5);
        Assert.Equal("截图_20261125_082205_2.png", ScreenCapture.BuildFileName(now, ".png", 2));
    }

    // ── 编码 ─────────────────────────────────────────────────
    [Fact]
    public void Encode_Png_ProducesPngSignature()
    {
        using var bmp = MakeSolid(8, 8, Color.Yellow);
        var bytes = ScreenCapture.Encode(bmp, "png");
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, bytes[..4]);
    }

    [Fact]
    public void Encode_Jpg_ProducesJpegSignature()
    {
        using var bmp = MakeSolid(16, 16, Color.Cyan);
        var bytes = ScreenCapture.Encode(bmp, "jpg", 80);
        Assert.Equal(new byte[] { 0xFF, 0xD8 }, bytes[..2]);
    }

    [Fact]
    public void DescribeSize_FormatsPixels()
    {
        Assert.Equal("1920 × 1080 px", ScreenCapture.DescribeSize(new Size(1920, 1080)));
    }

    private static Bitmap MakeSolid(int w, int h, Color color)
    {
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(color);
        return bmp;
    }
}
