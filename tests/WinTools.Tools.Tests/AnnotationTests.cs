using System.Drawing;
using WinTools.Tools.Files;
using Xunit;

namespace WinTools.Tools.Tests;

/// <summary>
/// 标注纯逻辑单测：命令栈的撤销/重做/分支丢弃、几何归一化、马赛克像素化与渲染合成。
/// 全部用内存 Bitmap，不依赖显示器，也不需要真的弹出覆盖层。
/// </summary>
public class AnnotationTests
{
    private static Bitmap Solid(int w, int h, Color color)
    {
        var bmp = new Bitmap(w, h);
        using var g = Graphics.FromImage(bmp);
        g.Clear(color);
        return bmp;
    }

    // ── 命令栈 ────────────────────────────────────────────────
    [Fact]
    public void NewStack_IsEmpty_CannotUndoOrRedo()
    {
        var s = new AnnotationStack();
        Assert.Equal(0, s.Count);
        Assert.False(s.CanUndo);
        Assert.False(s.CanRedo);
        Assert.False(s.Undo());
        Assert.False(s.Redo());
    }

    [Fact]
    public void Push_ThenUndo_ReducesActiveAndEnablesRedo()
    {
        var s = new AnnotationStack();
        s.Push(new AnnotationStroke { Tool = AnnotationTool.Rectangle });
        s.Push(new AnnotationStroke { Tool = AnnotationTool.Arrow });
        Assert.Equal(2, s.Count);
        Assert.Equal(2, s.Active.Count);
        Assert.True(s.Undo());
        Assert.Single(s.Active);
        Assert.True(s.CanRedo);
        Assert.True(s.Redo());
        Assert.Equal(2, s.Active.Count);
        Assert.False(s.CanRedo);
    }

    [Fact]
    public void Push_AfterUndo_DiscardsRedoBranch()
    {
        var s = new AnnotationStack();
        s.Push(new AnnotationStroke { Tool = AnnotationTool.Rectangle });
        s.Push(new AnnotationStroke { Tool = AnnotationTool.Ellipse });
        s.Undo(); // 只剩 1 条活动
        s.Push(new AnnotationStroke { Tool = AnnotationTool.Pen }); // 丢弃 redo 分支
        Assert.Equal(2, s.Count);
        Assert.False(s.CanRedo);
        Assert.Equal(AnnotationTool.Pen, s.Active.Last().Tool);
    }

    [Fact]
    public void Clear_ResetsEverything()
    {
        var s = new AnnotationStack();
        s.Push(new AnnotationStroke { Tool = AnnotationTool.Text });
        s.Undo();
        s.Clear();
        Assert.Equal(0, s.Count);
        Assert.Equal(0, s.Cursor);
        Assert.False(s.CanUndo);
        Assert.False(s.CanRedo);
    }

    // ── 几何归一化 ────────────────────────────────────────────
    [Fact]
    public void Normalize_Rect_HandlesReverseDrag()
    {
        var r = AnnotationGeometry.Normalize(new Rectangle(200, 150, -80, -50));
        Assert.Equal(new Rectangle(120, 100, 80, 50), r);
    }

    [Fact]
    public void Normalize_TwoPoints_HandlesNegativeDirection()
    {
        var r = AnnotationGeometry.Normalize(new Point(300, 300), new Point(100, 120));
        Assert.Equal(new Rectangle(100, 120, 200, 180), r);
    }

    // ── 马赛克 ────────────────────────────────────────────────
    [Theory]
    [InlineData(-10, 2)]
    [InlineData(0, 2)]
    [InlineData(1, 2)]
    [InlineData(12, 12)]
    [InlineData(1000, 64)]
    public void ClampBlock_StaysInRange(int input, int expected) =>
        Assert.Equal(expected, MosaicEffect.ClampBlock(input));

    [Fact]
    public void Pixelate_KeepsRegionSize()
    {
        using var src = Solid(100, 80, Color.RoyalBlue);
        using var result = MosaicEffect.Pixelate(src, new Rectangle(10, 10, 50, 40), 8);
        Assert.Equal(50, result.Width);
        Assert.Equal(40, result.Height);
    }

    [Fact]
    public void Pixelate_UniformRegion_KeepsSameColor()
    {
        using var src = Solid(80, 80, Color.FromArgb(255, 40, 90, 160));
        using var result = MosaicEffect.Pixelate(src, new Rectangle(0, 0, 40, 40), 10);
        var c = result.GetPixel(20, 20);
        Assert.InRange(c.R, 35, 45);
        Assert.InRange(c.G, 85, 95);
        Assert.InRange(c.B, 155, 165);
    }

    [Fact]
    public void Pixelate_BlockLargerThanRegion_StillReturnsValidImage()
    {
        using var src = Solid(40, 40, Color.Green);
        using var result = MosaicEffect.Pixelate(src, new Rectangle(0, 0, 5, 5), 64);
        Assert.True(result.Width >= 1 && result.Height >= 1);
    }

    // ── 渲染合成 ──────────────────────────────────────────────
    [Fact]
    public void Render_NoStrokes_ReturnsCopyOfSource()
    {
        using var src = Solid(30, 20, Color.Yellow);
        using var result = AnnotationRenderer.Render(src, Array.Empty<AnnotationStroke>());
        Assert.Equal(src.Size, result.Size);
        Assert.Equal(Color.Yellow.ToArgb(), result.GetPixel(5, 5).ToArgb());
        result.SetPixel(0, 0, Color.Black); // 验证是副本，不影响源
        Assert.Equal(Color.Yellow.ToArgb(), src.GetPixel(0, 0).ToArgb());
    }

    [Fact]
    public void Render_RectangleStroke_PaintsOnCanvas()
    {
        using var src = Solid(60, 60, Color.White);
        var stroke = new AnnotationStroke
        {
            Tool = AnnotationTool.Rectangle, Color = Color.Red, Width = 4,
            Bounds = new Rectangle(10, 10, 30, 30)
        };
        using var result = AnnotationRenderer.Render(src, new[] { stroke });
        var c = result.GetPixel(10, 20);
        Assert.Equal(Color.Red.ToArgb(), c.ToArgb());
        Assert.Equal(Color.White.ToArgb(), result.GetPixel(30, 30).ToArgb());
    }

    [Fact]
    public void Render_ArrowStroke_PaintsLineAndHead()
    {
        using var src = Solid(80, 80, Color.White);
        var stroke = new AnnotationStroke
        {
            Tool = AnnotationTool.Arrow, Color = Color.Blue, Width = 3,
            Points = [new Point(10, 40), new Point(60, 40)]
        };
        using var result = AnnotationRenderer.Render(src, new[] { stroke });
        Assert.Equal(Color.Blue.ToArgb(), result.GetPixel(30, 40).ToArgb());
        Assert.Equal(Color.White.ToArgb(), result.GetPixel(5, 5).ToArgb());
    }

    [Fact]
    public void Render_MosaicStroke_OnlyChangesRegion()
    {
        using var src = new Bitmap(60, 60);
        using (var g = Graphics.FromImage(src))
        {
            g.Clear(Color.White);
            using var brush = new SolidBrush(Color.Red);
            for (int i = 0; i < 40; i += 4)
                for (int j = 0; j < 40; j += 4)
                    g.FillRectangle(brush, i, j, 2, 2);
        }
        var stroke = new AnnotationStroke
        {
            Tool = AnnotationTool.Mosaic, Bounds = new Rectangle(0, 0, 40, 40), BlockSize = 16
        };
        using var result = AnnotationRenderer.Render(src, new[] { stroke });
        // 区域外保持原样
        Assert.Equal(Color.White.ToArgb(), result.GetPixel(50, 50).ToArgb());
        // 区域内不再是纯白（红点被混进平均值）
        Assert.NotEqual(Color.White.ToArgb(), result.GetPixel(20, 20).ToArgb());
    }

    [Fact]
    public void Render_EmptyStroke_DoesNothing()
    {
        using var src = Solid(20, 20, Color.Cyan);
        using var result = AnnotationRenderer.Render(src,
            new[] { new AnnotationStroke { Tool = AnnotationTool.None } });
        Assert.Equal(Color.Cyan.ToArgb(), result.GetPixel(10, 10).ToArgb());
    }
}
