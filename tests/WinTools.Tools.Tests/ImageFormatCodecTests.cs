using System.Drawing;
using System.Drawing.Imaging;
using WinTools.Tools.Files;
using Xunit;

namespace WinTools.Tools.Tests;

public class ImageFormatCodecTests
{
    private static byte[] MakePng(int w, int h, Color color, bool alpha = false)
    {
        using var bmp = new Bitmap(w, h, alpha ? PixelFormat.Format32bppArgb : PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(color);
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    [Fact]
    public void Formats_ContainsFiveSupported()
    {
        var ids = ImageFormatCodec.Formats.Select(f => f.Id).ToArray();
        Assert.Equal(new[] { "png", "jpg", "bmp", "gif", "tiff" }, ids);
        Assert.DoesNotContain("webp", ids);  // 本机 GDI+ 不支持 WebP，明确不提供
    }

    [Fact]
    public void Find_KnownAndUnknown()
    {
        Assert.Equal("JPG", ImageFormatCodec.Find("jpg")!.Display);
        Assert.Null(ImageFormatCodec.Find("webp"));
    }

    [Theory]
    [InlineData("a.PNG", "png")]
    [InlineData("photo.JpEg", "jpg")]
    [InlineData("x.tiff", "tiff")]
    [InlineData("x.webp", null)]
    public void IdFromExtension_Works(string path, string? expected)
        => Assert.Equal(expected, ImageFormatCodec.IdFromExtension(path));

    [Fact]
    public void Convert_PngToJpg_Succeeds()
    {
        var png = MakePng(8, 8, Color.Red);
        var result = ImageFormatCodec.Convert(png, ImageFormatCodec.Find("jpg")!);
        Assert.True(result.Success, result.ErrorMessage);
        Assert.NotNull(result.Bytes);
        Assert.Equal(8, result.Width);
        Assert.Equal(8, result.Height);
        // JPEG 文件以 FFD8 开头
        Assert.Equal(new byte[] { 0xFF, 0xD8 }, result.Bytes![..2]);
    }

    [Fact]
    public void Convert_PngToPng_PreservesAlpha_NoFlatten()
    {
        var png = MakePng(8, 8, Color.FromArgb(128, 0, 255, 0), alpha: true);
        var result = ImageFormatCodec.Convert(png, ImageFormatCodec.Find("png")!);
        Assert.True(result.Success, result.ErrorMessage);
        Assert.False(result.AlphaFlattened);  // 目标支持透明，不填白
        var sig = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        Assert.Equal(sig, result.Bytes![..8]);
    }

    [Fact]
    public void Convert_AlphaPngToJpg_FlattensAlpha()
    {
        var png = MakePng(8, 8, Color.FromArgb(64, 255, 0, 0), alpha: true);
        var result = ImageFormatCodec.Convert(png, ImageFormatCodec.Find("jpg")!);
        Assert.True(result.Success, result.ErrorMessage);
        Assert.True(result.AlphaFlattened);  // 目标不支持透明，已合成白底
    }

    [Fact]
    public void Convert_InvalidBytes_Fails()
    {
        var result = ImageFormatCodec.Convert(new byte[] { 1, 2, 3, 4 }, ImageFormatCodec.Find("png")!);
        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public void Convert_EmptySource_Fails()
    {
        var result = ImageFormatCodec.Convert([], ImageFormatCodec.Find("png")!);
        Assert.False(result.Success);
        Assert.Contains("空", result.ErrorMessage);
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1023, "1023 B")]
    [InlineData(2048, "2.0 KB")]
    public void FormatSize_Works(long bytes, string expected)
        => Assert.Equal(expected, ImageFormatCodec.FormatSize(bytes));
}
