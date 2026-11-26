using System.Drawing;
using WinTools.Tools.Files;
using Xunit;

namespace WinTools.Tools.Tests;

public class EmojiArtTests
{
    [Fact]
    public void Items_AreUniqueAndWellFormed()
    {
        Assert.Equal(16, EmojiArt.Items.Count);
        Assert.Equal(EmojiArt.Items.Count, EmojiArt.Items.Select(i => i.Key).Distinct().Count());
        Assert.All(EmojiArt.Items, item =>
        {
            Assert.False(string.IsNullOrWhiteSpace(item.Key));
            Assert.False(string.IsNullOrWhiteSpace(item.Name));
        });
    }

    [Fact]
    public void Draw_EveryEmoji_ProducesPixelsInsideBounds()
    {
        foreach (var item in EmojiArt.Items)
        {
            using var bmp = new Bitmap(72, 72);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.White);
                EmojiArt.Draw(g, item.Key, new RectangleF(4, 4, 64, 64));
            }

            int colored = 0;
            for (int y = 0; y < bmp.Height; y++)
                for (int x = 0; x < bmp.Width; x++)
                    if (bmp.GetPixel(x, y).ToArgb() != Color.White.ToArgb()) colored++;

            Assert.True(colored > 300, $"{item.Key} 只画出了 {colored} 个像素");
            // 目标矩形之外必须保持干净，说明缩放/平移没有越界
            Assert.Equal(Color.White.ToArgb(), bmp.GetPixel(0, 0).ToArgb());
            Assert.Equal(Color.White.ToArgb(), bmp.GetPixel(71, 71).ToArgb());
        }
    }

    [Fact]
    public void Draw_SmallSize_StillProducesVisibleArt()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.White);
            EmojiArt.Draw(g, "smile", new RectangleF(3, 3, 26, 26));
        }

        int colored = 0;
        for (int y = 0; y < bmp.Height; y++)
            for (int x = 0; x < bmp.Width; x++)
                if (bmp.GetPixel(x, y).ToArgb() != Color.White.ToArgb()) colored++;

        Assert.True(colored > 100, $"小尺寸只画出了 {colored} 个像素");
    }

    [Fact]
    public void Draw_UnknownKey_FallsBackWithoutThrowing()
    {
        using var bmp = new Bitmap(72, 72);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.White);
            EmojiArt.Draw(g, "not-an-emoji", new RectangleF(4, 4, 64, 64));
        }

        Assert.NotEqual(Color.White.ToArgb(), bmp.GetPixel(36, 20).ToArgb());
    }
}
