using System.Drawing;
using System.Drawing.Imaging;
using WinTools.Tools.Files;
using Xunit;

namespace WinTools.Tools.Tests;

public class ExamPhotoProcessorTests
{
    private static readonly Color Red = Color.FromArgb(255, 0, 0);
    private static readonly Color Skin = Color.FromArgb(220, 170, 150);
    private static readonly Color Blue = Color.FromArgb(0x43, 0x8E, 0xDB);

    private static byte[] ToPng(Bitmap bitmap)
    {
        using var ms = new MemoryStream();
        bitmap.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    private static byte[] MakeBackdropPhoto(int w, int h, Color background, params (Color Color, int X, int Y, int Size)[] blocks)
    {
        using var bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(background);
        foreach (var (color, x, y, size) in blocks)
        {
            using var brush = new SolidBrush(color);
            g.FillRectangle(brush, x, y, size, size);
        }
        return ToPng(bmp);
    }

    private static Color PixelAt(byte[] jpeg, int x, int y)
    {
        using var ms = new MemoryStream(jpeg, writable: false);
        using var image = new Bitmap(ms);
        return image.GetPixel(x, y);
    }

    private static void AssertNear(Color actual, Color expected, int delta = 26)
    {
        Assert.InRange(actual.R, expected.R - delta, expected.R + delta);
        Assert.InRange(actual.G, expected.G - delta, expected.G + delta);
        Assert.InRange(actual.B, expected.B - delta, expected.B + delta);
    }

    [Fact]
    public void Process_RedBackdrop_TurnsBlueAndKeepsSkin()
    {
        var source = MakeBackdropPhoto(60, 80, Red, (Skin, 15, 25, 30));
        var result = ExamPhotoProcessor.Process(source, new ExamPhotoOptions
        {
            TargetSize = null,
            TargetBytes = 200_000,
            SourceBackdrop = ExamBackdropSource.Red,
            TargetBackdrop = Blue
        });

        Assert.True(result.Success, result.ErrorMessage);
        Assert.True(result.ReplacedPixels > 0);
        AssertNear(PixelAt(result.Bytes!, 2, 2), Blue);
        AssertNear(PixelAt(result.Bytes!, 30, 40), Skin);
    }

    [Fact]
    public void Process_IsolatedRedClothing_NotReplacedByFloodFill()
    {
        // 红衣服被肤色区域包住，不与画面边缘连通；即使颜色相同也必须保留。
        var source = MakeBackdropPhoto(60, 80, Red,
            (Skin, 10, 20, 40),
            (Red, 24, 34, 12));

        var result = ExamPhotoProcessor.Process(source, new ExamPhotoOptions
        {
            TargetSize = null,
            TargetBytes = 200_000,
            SourceBackdrop = ExamBackdropSource.Red,
            TargetBackdrop = Blue
        });

        Assert.True(result.Success, result.ErrorMessage);
        AssertNear(PixelAt(result.Bytes!, 2, 2), Blue);
        AssertNear(PixelAt(result.Bytes!, 30, 40), Red);
    }

    [Fact]
    public void Process_WhiteBackdrop_TurnsBlue()
    {
        var source = MakeBackdropPhoto(60, 80, Color.White, (Skin, 15, 25, 30));
        var result = ExamPhotoProcessor.Process(source, new ExamPhotoOptions
        {
            TargetSize = null,
            TargetBytes = 200_000,
            SourceBackdrop = ExamBackdropSource.White,
            TargetBackdrop = Blue
        });

        Assert.True(result.Success, result.ErrorMessage);
        Assert.True(result.ReplacedPixels > 0);
        AssertNear(PixelAt(result.Bytes!, 2, 2), Blue);
    }

    [Fact]
    public void Process_WhiteShirtTouchingBottomEdge_StaysWhite()
    {
        // 白衬衫贴着画面下边缘且与白底同色：旧逻辑从底边直接把它当背景换掉。
        var shirt = Color.FromArgb(245, 245, 245);
        var source = MakeBackdropPhoto(60, 80, Color.White,
            (Skin, 15, 12, 30),
            (shirt, 8, 45, 44));

        var result = ExamPhotoProcessor.Process(source, new ExamPhotoOptions
        {
            TargetSize = null,
            TargetBytes = 200_000,
            SourceBackdrop = ExamBackdropSource.White,
            TargetBackdrop = Blue
        });

        Assert.True(result.Success, result.ErrorMessage);
        Assert.True(result.ProtectedPixels > 0);
        AssertNear(PixelAt(result.Bytes!, 2, 2), Blue);      // 头顶背景仍换色
        AssertNear(PixelAt(result.Bytes!, 0, 70), Blue);     // 衬衫外侧背景仍换色
        AssertNear(PixelAt(result.Bytes!, 30, 65), shirt);   // 衬衫保持白色
    }

    [Fact]
    public void Process_LightGrayShirt_StaysUnchanged()
    {
        // 浅灰衣物和白底颜色接近，同样由主体轮廓保护。
        var shirt = Color.FromArgb(205, 205, 205);
        var source = MakeBackdropPhoto(60, 80, Color.White,
            (Skin, 15, 12, 30),
            (shirt, 10, 44, 40));

        var result = ExamPhotoProcessor.Process(source, new ExamPhotoOptions
        {
            TargetSize = null,
            TargetBytes = 200_000,
            SourceBackdrop = ExamBackdropSource.White,
            TargetBackdrop = Blue
        });

        Assert.True(result.Success, result.ErrorMessage);
        AssertNear(PixelAt(result.Bytes!, 30, 70), shirt);
    }

    [Fact]
    public void Process_DarkJacketWithWhiteShirt_NoWhiteStripBesideShoulders()
    {
        // 头顶窄、肩膀宽的常见构图：深色西装给出准确轮廓，
        // 外扩若不回收，西装两侧会留一条白背景没换成蓝。
        var jacket = Color.FromArgb(35, 35, 38);
        var shirt = Color.FromArgb(245, 245, 245);
        var source = MakeBackdropPhoto(120, 160, Color.White,
            (Skin, 45, 18, 30),
            (jacket, 20, 60, 30),
            (jacket, 70, 60, 30),
            (shirt, 50, 60, 20),
            (shirt, 52, 120, 36));

        var result = ExamPhotoProcessor.Process(source, new ExamPhotoOptions
        {
            TargetSize = null,
            TargetBytes = 300_000,
            SourceBackdrop = ExamBackdropSource.White,
            TargetBackdrop = Blue
        });

        Assert.True(result.Success, result.ErrorMessage);
        AssertNear(PixelAt(result.Bytes!, 5, 80), Blue);      // 西装左侧背景
        AssertNear(PixelAt(result.Bytes!, 112, 80), Blue);    // 西装右侧背景
        AssertNear(PixelAt(result.Bytes!, 60, 90), shirt);    // 白衬衫不被换色
        AssertNear(PixelAt(result.Bytes!, 60, 140), shirt);   // 贴底的白衬衫
    }

    [Fact]
    public void Process_AntialiasedHalo_IsReplacedInsteadOfProtected()
    {
        // 轮廓外的浅灰抗锯齿像素曾是白边来源：它们既不够白（旧阈值 160）又不够暗（旧主体阈值 200），
        // 被当成主体保护下来。现在只要偏中性且亮度 >=100 就按背景替换。
        using var bmp = new Bitmap(100, 100, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.White);
            using var brush = new SolidBrush(Color.FromArgb(30, 30, 30));
            g.FillRectangle(brush, 40, 0, 20, 100);
        }
        bmp.SetPixel(39, 50, Color.FromArgb(199, 191, 189));
        bmp.SetPixel(38, 50, Color.FromArgb(158, 140, 140));
        var source = ToPng(bmp);

        var result = ExamPhotoProcessor.Process(source, new ExamPhotoOptions
        {
            TargetSize = null,
            TargetBytes = 200_000,
            SourceBackdrop = ExamBackdropSource.White,
            TargetBackdrop = Blue
        });

        Assert.True(result.Success, result.ErrorMessage);
        AssertNear(PixelAt(result.Bytes!, 38, 50), Blue);
        AssertNear(PixelAt(result.Bytes!, 39, 50), Blue);
        AssertNear(PixelAt(result.Bytes!, 45, 50), Color.FromArgb(30, 30, 30));
    }

    [Fact]
    public void Process_BlueBackdrop_TurnsWhiteAndKeepsSkin()
    {
        var source = MakeBackdropPhoto(60, 80, Blue, (Skin, 15, 25, 30));
        var result = ExamPhotoProcessor.Process(source, new ExamPhotoOptions
        {
            TargetSize = null,
            TargetBytes = 200_000,
            SourceBackdrop = ExamBackdropSource.Blue,
            TargetBackdrop = Color.White
        });

        Assert.True(result.Success, result.ErrorMessage);
        Assert.True(result.ReplacedPixels > 0);
        AssertNear(PixelAt(result.Bytes!, 2, 2), Color.White);
        AssertNear(PixelAt(result.Bytes!, 30, 40), Skin);
    }

    [Fact]
    public void Process_SourceAndTargetCanBothBeRed_BlueOrWhite()
    {
        // 三种底色两两互换都要能工作；这里抽查蓝→红与红→白。
        var blueToRed = ExamPhotoProcessor.Process(
            MakeBackdropPhoto(60, 80, Blue, (Skin, 15, 25, 30)),
            new ExamPhotoOptions
            {
                TargetSize = null, TargetBytes = 200_000,
                SourceBackdrop = ExamBackdropSource.Blue, TargetBackdrop = Red
            });
        Assert.True(blueToRed.Success, blueToRed.ErrorMessage);
        AssertNear(PixelAt(blueToRed.Bytes!, 2, 2), Red);

        var redToWhite = ExamPhotoProcessor.Process(
            MakeBackdropPhoto(60, 80, Red, (Skin, 15, 25, 30)),
            new ExamPhotoOptions
            {
                TargetSize = null, TargetBytes = 200_000,
                SourceBackdrop = ExamBackdropSource.Red, TargetBackdrop = Color.White
            });
        Assert.True(redToWhite.Success, redToWhite.ErrorMessage);
        AssertNear(PixelAt(redToWhite.Bytes!, 2, 2), Color.White);
    }

    [Fact]
    public void Process_OriginalSizeOrCustomSize_AreHonored()
    {
        var source = MakeBackdropPhoto(120, 200, Red);

        var original = ExamPhotoProcessor.Process(source, new ExamPhotoOptions
        {
            TargetSize = null, TargetBytes = 200_000
        });
        Assert.True(original.Success, original.ErrorMessage);
        Assert.Equal(120, original.Width);
        Assert.Equal(200, original.Height);

        var custom = ExamPhotoProcessor.Process(source, new ExamPhotoOptions
        {
            TargetSize = new Size(100, 120), TargetBytes = 200_000
        });
        Assert.True(custom.Success, custom.ErrorMessage);
        Assert.Equal(100, custom.Width);
        Assert.Equal(120, custom.Height);
    }

    [Fact]
    public void Process_NoSizeLimit_UsesFixedHighQuality()
    {
        using var bmp = new Bitmap(300, 400, PixelFormat.Format24bppRgb);
        var rnd = new Random(11);
        for (var y = 0; y < bmp.Height; y++)
            for (var x = 0; x < bmp.Width; x++)
                bmp.SetPixel(x, y, Color.FromArgb(rnd.Next(256), rnd.Next(256), rnd.Next(256)));
        var source = ToPng(bmp);

        var result = ExamPhotoProcessor.Process(source, new ExamPhotoOptions
        {
            TargetSize = null, TargetBytes = 0
        });

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(95, result.Quality);
        Assert.False(result.Downscaled);
        Assert.True(result.Bytes!.Length > 30 * 1024);
        Assert.Contains(result.Notes, n => n.Contains("不限制大小"));
    }

    [Fact]
    public void SizePresets_IncludeCommonExamSpecs()
    {
        var sizes = ExamPhotoProcessor.SizePresets.Select(p => p.Size).ToList();
        Assert.Contains(new Size(295, 413), sizes); // 一寸
        Assert.Contains(new Size(413, 579), sizes); // 二寸
        Assert.Contains(new Size(260, 378), sizes); // 小一寸
    }

    [Fact]
    public void Process_ResizesTo295x413_WithoutStretching()
    {
        var source = MakeBackdropPhoto(120, 200, Red);
        var result = ExamPhotoProcessor.Process(source, new ExamPhotoOptions
        {
            ReplaceBackdrop = false,
            TargetSize = new Size(295, 413),
            TargetBytes = 200_000
        });

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(295, result.Width);
        Assert.Equal(413, result.Height);
        using var ms = new MemoryStream(result.Bytes!, writable: false);
        using var image = new Bitmap(ms);
        Assert.Equal(295, image.Width);
        Assert.Equal(413, image.Height);
    }

    [Fact]
    public void Process_NoisyPhoto_FitsWithin30Kb()
    {
        using var bmp = new Bitmap(400, 600, PixelFormat.Format24bppRgb);
        var rnd = new Random(7);
        for (var y = 0; y < bmp.Height; y++)
            for (var x = 0; x < bmp.Width; x++)
                bmp.SetPixel(x, y, Color.FromArgb(rnd.Next(256), rnd.Next(256), rnd.Next(256)));
        var source = ToPng(bmp);
        Assert.True(source.Length > 30 * 1024);

        var result = ExamPhotoProcessor.Process(source, new ExamPhotoOptions
        {
            ReplaceBackdrop = false,
            TargetBytes = 30 * 1024
        });

        Assert.True(result.Success, result.ErrorMessage);
        Assert.NotNull(result.Bytes);
        Assert.True(result.Bytes!.Length <= 30 * 1024, $"实际 {result.Bytes.Length} 字节");
        Assert.Equal(new byte[] { 0xFF, 0xD8 }, result.Bytes[..2]);
        Assert.InRange(result.Quality, 10, 100);
    }

    [Fact]
    public void Process_ReplaceDisabled_DoesNotTouchBackdrop()
    {
        var source = MakeBackdropPhoto(60, 80, Red);
        var result = ExamPhotoProcessor.Process(source, new ExamPhotoOptions
        {
            ReplaceBackdrop = false,
            TargetSize = null,
            TargetBytes = 200_000
        });

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(0, result.ReplacedPixels);
        AssertNear(PixelAt(result.Bytes!, 2, 2), Red);
    }

    [Fact]
    public void Process_InvalidOrEmptySource_FailsWithoutBytes()
    {
        var bad = ExamPhotoProcessor.Process([1, 2, 3]);
        Assert.False(bad.Success);
        Assert.Null(bad.Bytes);
        Assert.False(string.IsNullOrEmpty(bad.ErrorMessage));

        var empty = ExamPhotoProcessor.Process([]);
        Assert.False(empty.Success);
        Assert.Null(empty.Bytes);
    }

    [Fact]
    public void Strength_RedAndWhite_DistinguishBackdropFromSkin()
    {
        Assert.Equal(1f, ExamPhotoProcessor.Strength(Red.ToArgb(), ExamBackdropSource.Red, 40));
        Assert.Equal(0f, ExamPhotoProcessor.Strength(Skin.ToArgb(), ExamBackdropSource.Red, 40));
        Assert.Equal(0f, ExamPhotoProcessor.Strength(Color.White.ToArgb(), ExamBackdropSource.Red, 40));
        Assert.Equal(1f, ExamPhotoProcessor.Strength(Color.White.ToArgb(), ExamBackdropSource.White, 40));
        Assert.Equal(0f, ExamPhotoProcessor.Strength(Skin.ToArgb(), ExamBackdropSource.White, 40));
        Assert.Equal(0f, ExamPhotoProcessor.Strength(Color.Black.ToArgb(), ExamBackdropSource.White, 100));
        Assert.Equal(1f, ExamPhotoProcessor.Strength(Blue.ToArgb(), ExamBackdropSource.Blue, 40));
        Assert.Equal(0f, ExamPhotoProcessor.Strength(Color.White.ToArgb(), ExamBackdropSource.Blue, 40));
        Assert.Equal(0f, ExamPhotoProcessor.Strength(Skin.ToArgb(), ExamBackdropSource.Blue, 40));
    }

    [Fact]
    public void Strength_WhiteSource_PaleWarmSkinHighlightIsNeverBackdrop()
    {
        // 真实案例量出的高光脸颊颜色：亮度高、色彩离散度低，但暖调（r-b）明显。
        // 没有暖调保护时它们会被 flood fill 吃成横带（v0.8.0 缺陷）。
        Assert.Equal(0f, ExamPhotoProcessor.Strength(Color.FromArgb(238, 209, 206).ToArgb(), ExamBackdropSource.White, 40));
        Assert.Equal(0f, ExamPhotoProcessor.Strength(Color.FromArgb(235, 206, 203).ToArgb(), ExamBackdropSource.White, 40));
        Assert.Equal(0f, ExamPhotoProcessor.Strength(Color.FromArgb(220, 184, 173).ToArgb(), ExamBackdropSource.White, 40));
        // 中性白与冷灰仍按背景替换。
        Assert.Equal(1f, ExamPhotoProcessor.Strength(Color.FromArgb(255, 255, 255).ToArgb(), ExamBackdropSource.White, 40));
        Assert.Equal(1f, ExamPhotoProcessor.Strength(Color.FromArgb(200, 196, 196).ToArgb(), ExamBackdropSource.White, 40));
    }

    [Fact]
    public void Process_WhiteSource_WarmSkinStripConnectedToEdgeIsKept()
    {
        // 半边画面是暖调苍白肤色条（贴边、与白背景连通），
        // 换蓝底时白背景一侧变蓝，肤色条必须原样保留。
        const int w = 80, h = 80;
        using var bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.White);
            using var skin = new SolidBrush(Color.FromArgb(240, 210, 200));
            g.FillRectangle(skin, 40, 0, 40, h);
        }
        var result = ExamPhotoProcessor.Process(ToPng(bmp), new ExamPhotoOptions
        {
            TargetSize = null,
            TargetBytes = 200_000,
            SourceBackdrop = ExamBackdropSource.White,
            TargetBackdrop = Blue,
            Tolerance = 40
        });

        Assert.True(result.Success, result.ErrorMessage);
        AssertNear(PixelAt(result.Bytes!, 10, 40), Blue);
        AssertNear(PixelAt(result.Bytes!, 60, 20), Color.FromArgb(240, 210, 200));
        AssertNear(PixelAt(result.Bytes!, 60, 60), Color.FromArgb(240, 210, 200));
    }

    [Fact]
    public void TryParseColor_AcceptsHexOnly()
    {
        Assert.True(ExamPhotoProcessor.TryParseColor("#438EDB", out var color));
        Assert.Equal(Blue.ToArgb(), color.ToArgb());
        Assert.True(ExamPhotoProcessor.TryParseColor("ffffff", out var white));
        Assert.Equal(Color.White.ToArgb(), white.ToArgb());
        Assert.False(ExamPhotoProcessor.TryParseColor("blue", out _));
        Assert.False(ExamPhotoProcessor.TryParseColor("#12345", out _));
        Assert.False(ExamPhotoProcessor.TryParseColor(null, out _));
    }
}
