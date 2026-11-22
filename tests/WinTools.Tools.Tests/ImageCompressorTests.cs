using System.Drawing;
using System.Drawing.Imaging;
using WinTools.Tools.Files;
using Xunit;

namespace WinTools.Tools.Tests;

public class ImageCompressorTests
{
    private static byte[] MakePhoto(int w, int h, int seed)
    {
        // 造一张有细节的图，低质量 JPG 压缩应明显变小
        using var bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb);
        var rnd = new Random(seed);
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                bmp.SetPixel(x, y, Color.FromArgb(rnd.Next(256), rnd.Next(256), rnd.Next(256)));
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    [Fact]
    public void CompressToBytes_ProducesJpeg()
    {
        var result = ImageCompressor.CompressToBytes(MakePhoto(40, 40, 1), 50);
        Assert.True(result.Success, result.ErrorMessage);
        Assert.NotNull(result.Bytes);
        Assert.Equal(new byte[] { 0xFF, 0xD8 }, result.Bytes![..2]);
    }

    [Fact]
    public void CompressFiles_LargePhoto_WrittenAndSmaller()
    {
        var bytes = MakePhoto(80, 80, 2);
        var written = new List<(string Path, byte[] Bytes)>();
        var result = ImageCompressor.CompressFiles(
            [("C:\\photos\\a.png", bytes)],
            30,
            p => Path.Combine(Path.GetDirectoryName(p)!, "a_compressed.jpg"),
            (p, b) => written.Add((p, b)));

        Assert.Single(result.Items);
        Assert.True(result.Items[0].Written, result.Items[0].Note);
        Assert.True(result.Items[0].ResultBytes < result.Items[0].OriginalBytes);
        Assert.Single(written);
        Assert.EndsWith("a_compressed.jpg", written[0].Path);
        Assert.Equal(1, result.Succeeded);
        Assert.True(result.TotalSaved > 0);
    }

    [Fact]
    public void CompressFiles_TinyImage_Grew_KeepsOriginalNoWrite()
    {
        // 1x1 图片转 JPG 通常变大：应保留原图、不写出
        var bytes = MakePhoto(1, 1, 3);
        var written = new List<(string Path, byte[] Bytes)>();
        var result = ImageCompressor.CompressFiles(
            [("C:\\photos\\tiny.png", bytes)],
            90,
            p => Path.Combine(Path.GetDirectoryName(p)!, "tiny_compressed.jpg"),
            (p, b) => written.Add((p, b)));

        Assert.Single(result.Items);
        Assert.False(result.Items[0].Written);
        Assert.Empty(written);
        Assert.Contains("保留原图", result.Items[0].Note);
        Assert.Equal(0, result.Succeeded);
        Assert.Equal(1, result.Skipped);
    }

    [Fact]
    public void CompressFiles_RefusesOverwriteSource()
    {
        var bytes = MakePhoto(80, 80, 4);
        var written = new List<(string Path, byte[] Bytes)>();
        var result = ImageCompressor.CompressFiles(
            [("C:\\photos\\a.png", bytes)],
            30,
            _ => "C:\\photos\\a.png",   // 输出=源，必须拒绝
            (p, b) => written.Add((p, b)));

        Assert.False(result.Items[0].Written);
        Assert.Empty(written);
        Assert.Contains("不覆盖", result.Items[0].Note);
    }

    [Fact]
    public void CompressFiles_InvalidFile_DoesNotAffectOthers()
    {
        var bad = new byte[] { 1, 2, 3 };
        var good = MakePhoto(80, 80, 5);
        var written = new List<(string Path, byte[] Bytes)>();
        var result = ImageCompressor.CompressFiles(
            [("bad.png", bad), ("good.png", good)],
            30,
            p => Path.ChangeExtension(p, "_compressed.jpg"),
            (p, b) => written.Add((p, b)));

        Assert.Equal(2, result.Items.Count);
        Assert.False(result.Items[0].Written);
        Assert.True(result.Items[1].Written);
        Assert.Single(written);
    }

    [Fact]
    public void CompressItem_DeltaAndGrew()
    {
        var item = new ImageCompressItem("C:\\x\\photo.png", 1000, 600, true, null);
        Assert.Equal(-400, item.Delta);
        Assert.False(item.Grew);
        Assert.Equal("photo.png", item.Name);
    }
}
