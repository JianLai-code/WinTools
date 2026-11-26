using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using WinTools.Tools.Files;
using Xunit;

namespace WinTools.Tools.Tests;

/// <summary>
/// 滚动拼接的纯逻辑测试：用合成"长页面"切帧喂给拼接器，断言成品与原始长图逐像素一致。
/// 覆盖正常滚动、吸顶头部、未滚动、跳帧、高度上限与空白页。
/// </summary>
public sealed class ScrollStitcherTests
{
    private const int Width = 96;

    [Fact]
    public void 连续滚动_拼接结果与原始长图一致()
    {
        using var source = Noise(Width, 1200, seed: 7);
        using var stitcher = new ScrollStitcher(Width);

        const int frameHeight = 240;
        const int step = 60;
        var lastBottom = 0;

        for (var top = 0; top + frameHeight <= source.Height; top += step)
        {
            using var frame = Slice(source, top, frameHeight);
            var result = stitcher.Add(frame);
            Assert.Equal(StitchStatus.Appended, result.Status);
            lastBottom = top + frameHeight;
        }

        Assert.Equal(lastBottom, stitcher.Height);
        Assert.Equal(1200, stitcher.Height);

        using var composed = stitcher.Compose();
        Assert.Equal(Width, composed.Width);
        Assert.Equal(lastBottom, composed.Height);
        AssertRowsEqual(source, 0, composed, 0, lastBottom, "连续滚动");
    }

    [Fact]
    public void 吸顶头部_成品里只保留一次()
    {
        using var content = Noise(Width, 1200, seed: 11);
        using var header = Noise(Width, 40, seed: 99);
        using var stitcher = new ScrollStitcher(Width);

        const int frameHeight = 240;
        const int stickyHeight = 40;
        const int viewport = frameHeight - stickyHeight;
        const int step = 80;
        var lastOffset = 0;

        for (var offset = 0; offset + viewport <= content.Height; offset += step)
        {
            using var slice = Slice(content, offset, viewport);
            using var frame = new Bitmap(Width, frameHeight, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(frame))
            {
                g.DrawImageUnscaled(header, 0, 0);
                g.DrawImageUnscaled(slice, 0, stickyHeight);
            }
            stitcher.Add(frame);
            lastOffset = offset;
        }

        var expectedHeight = stickyHeight + lastOffset + viewport;
        Assert.Equal(expectedHeight, stitcher.Height);

        using var composed = stitcher.Compose();
        AssertRowsEqual(header, 0, composed, 0, stickyHeight, "吸顶头部");
        AssertRowsEqual(content, 0, composed, stickyHeight, lastOffset + viewport, "吸顶后的正文");
    }

    [Fact]
    public void 画面未变化_不追加也不报错()
    {
        using var frame = Noise(Width, 240, seed: 3);
        using var stitcher = new ScrollStitcher(Width);

        Assert.Equal(StitchStatus.Appended, stitcher.Add(frame).Status);
        var again = stitcher.Add(frame);

        Assert.Equal(StitchStatus.NoNewContent, again.Status);
        Assert.Equal(0, again.AddedRows);
        Assert.Equal(240, stitcher.Height);
        Assert.Equal(2, stitcher.Frames);
    }

    [Fact]
    public void 滚动过快无重叠_报无重叠且不追加()
    {
        using var source = Noise(Width, 1200, seed: 21);
        using var stitcher = new ScrollStitcher(Width);

        using (var first = Slice(source, 0, 240)) stitcher.Add(first);
        using (var jumped = Slice(source, 600, 240))
        {
            var result = stitcher.Add(jumped);
            Assert.Equal(StitchStatus.NoOverlap, result.Status);
        }

        Assert.Equal(240, stitcher.Height);
    }

    [Fact]
    public void 达到高度上限_停止追加并报告上限()
    {
        using var source = Noise(Width, 1200, seed: 31);
        using var stitcher = new ScrollStitcher(Width, maxHeight: 300);

        const int frameHeight = 240;
        var statuses = new List<StitchStatus>();
        for (var top = 0; top + frameHeight <= 600; top += 60)
        {
            using var frame = Slice(source, top, frameHeight);
            statuses.Add(stitcher.Add(frame).Status);
        }

        Assert.Equal(300, stitcher.Height);
        Assert.True(statuses.Contains(StitchStatus.HeightLimit), "应报告高度上限");
        Assert.All(statuses.Skip(1), s => Assert.True(
            s is StitchStatus.Appended or StitchStatus.NoNewContent or StitchStatus.HeightLimit,
            $"不应出现 {s}"));
    }

    [Fact]
    public void 空白页面_重复帧不会无限增长()
    {
        using var blank = new Bitmap(Width, 240, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(blank)) g.Clear(Color.White);
        using var stitcher = new ScrollStitcher(Width);

        for (var i = 0; i < 5; i++) stitcher.Add(blank);

        Assert.Equal(240, stitcher.Height);
    }

    [Fact]
    public void 帧宽不一致_抛出明确异常()
    {
        using var stitcher = new ScrollStitcher(Width);
        using var wrong = Noise(Width * 2, 100, seed: 5);
        var ex = Assert.Throws<ArgumentException>(() => stitcher.Add(wrong));
        Assert.Contains("宽度", ex.Message);
    }

    [Fact]
    public void 未追加任何帧时_合成抛异常()
    {
        using var stitcher = new ScrollStitcher(Width);
        Assert.Throws<InvalidOperationException>(() => stitcher.Compose());
    }

    // ── 合成素材 ──────────────────────────────────────────

    /// <summary>逐像素随机噪声：每行都不同且都有细节，便于定位重叠。</summary>
    private static Bitmap Noise(int width, int height, int seed)
    {
        var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        var data = bmp.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly,
            PixelFormat.Format32bppArgb);
        try
        {
            var buffer = new byte[data.Stride * height];
            var random = new Random(seed);
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var i = y * data.Stride + x * 4;
                    buffer[i] = (byte)random.Next(256);
                    buffer[i + 1] = (byte)random.Next(256);
                    buffer[i + 2] = (byte)random.Next(256);
                    buffer[i + 3] = 255;
                }
            }
            Marshal.Copy(buffer, 0, data.Scan0, buffer.Length);
        }
        finally
        {
            bmp.UnlockBits(data);
        }
        return bmp;
    }

    private static Bitmap Slice(Bitmap source, int top, int height) =>
        source.Clone(new Rectangle(0, top, source.Width, height), PixelFormat.Format32bppArgb);

    private static void AssertRowsEqual(Bitmap expected, int expectedTop, Bitmap actual, int actualTop,
        int rows, string what)
    {
        var e = expected.LockBits(new Rectangle(0, 0, expected.Width, expected.Height),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var a = actual.LockBits(new Rectangle(0, 0, actual.Width, actual.Height),
                ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var eb = new byte[e.Stride];
                var ab = new byte[a.Stride];
                for (var y = 0; y < rows; y++)
                {
                    Marshal.Copy(IntPtr.Add(e.Scan0, (expectedTop + y) * e.Stride), eb, 0, e.Stride);
                    Marshal.Copy(IntPtr.Add(a.Scan0, (actualTop + y) * a.Stride), ab, 0, a.Stride);
                    for (var x = 0; x < expected.Width; x++)
                    {
                        var i = x * 4;
                        var same = eb[i] == ab[i] && eb[i + 1] == ab[i + 1] && eb[i + 2] == ab[i + 2];
                        Assert.True(same, $"{what}：第 {y} 行第 {x} 列像素不一致");
                    }
                }
            }
            finally
            {
                actual.UnlockBits(a);
            }
        }
        finally
        {
            expected.UnlockBits(e);
        }
    }
}
