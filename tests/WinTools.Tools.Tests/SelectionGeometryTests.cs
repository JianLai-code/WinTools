using System.Drawing;
using WinTools.Tools.Files;
using Xunit;

namespace WinTools.Tools.Tests;

/// <summary>
/// 选区移动/缩放几何纯函数单测：手柄命中、八向调整、最小尺寸、边界钳制。
/// 不依赖屏幕或窗体，可在 CI 里稳定跑。
/// </summary>
public class SelectionGeometryTests
{
    private static readonly Rectangle Bounds = new(0, 0, 500, 400);
    private static readonly Rectangle Rect = new(100, 80, 200, 120); // Right=300 Bottom=200

    // ── 手柄 ────────────────────────────────────────────────
    [Fact]
    public void Handles_ReturnsEightPointsInEnumOrder()
    {
        var p = SelectionGeometry.Handles(Rect);
        Assert.Equal(8, p.Length);
        Assert.Equal(new Point(100, 80), p[0]);                 // TopLeft
        Assert.Equal(new Point(200, 80), p[1]);                 // Top
        Assert.Equal(new Point(300, 80), p[2]);                 // TopRight
        Assert.Equal(new Point(300, 140), p[3]);                // Right
        Assert.Equal(new Point(300, 200), p[4]);                // BottomRight
        Assert.Equal(new Point(200, 200), p[5]);                // Bottom
        Assert.Equal(new Point(100, 200), p[6]);                // BottomLeft
        Assert.Equal(new Point(100, 140), p[7]);                // Left
    }

    [Theory]
    [InlineData(100, 80, SelectionHandle.TopLeft)]
    [InlineData(200, 80, SelectionHandle.Top)]
    [InlineData(300, 80, SelectionHandle.TopRight)]
    [InlineData(300, 140, SelectionHandle.Right)]
    [InlineData(300, 200, SelectionHandle.BottomRight)]
    [InlineData(200, 200, SelectionHandle.Bottom)]
    [InlineData(100, 200, SelectionHandle.BottomLeft)]
    [InlineData(100, 140, SelectionHandle.Left)]
    public void HitTest_FindsEveryHandle(int x, int y, SelectionHandle expected)
    {
        Assert.Equal(expected, SelectionGeometry.HitTest(Rect, new Point(x, y)));
    }

    [Fact]
    public void HitTest_ToleranceAcceptsNearbyPoint()
    {
        Assert.Equal(SelectionHandle.TopLeft, SelectionGeometry.HitTest(Rect, new Point(105, 86), 8));
        Assert.Equal(SelectionHandle.None, SelectionGeometry.HitTest(Rect, new Point(105, 86), 2));
    }

    [Fact]
    public void HitTest_CornerWinsOverEdge_AndInteriorIsNotAHandle()
    {
        Assert.Equal(SelectionHandle.TopLeft, SelectionGeometry.HitTest(Rect, new Point(102, 82)));
        Assert.Equal(SelectionHandle.None, SelectionGeometry.HitTest(Rect, new Point(180, 120)));
        Assert.Equal(SelectionHandle.None, SelectionGeometry.HitTest(Rect, new Point(20, 20)));
    }

    [Fact]
    public void Contains_UsesInclusiveEdges()
    {
        Assert.True(SelectionGeometry.Contains(Rect, new Point(100, 80)));
        Assert.True(SelectionGeometry.Contains(Rect, new Point(300, 200)));
        Assert.False(SelectionGeometry.Contains(Rect, new Point(99, 80)));
        Assert.False(SelectionGeometry.Contains(Rect, new Point(301, 200)));
    }

    // ── 移动 ────────────────────────────────────────────────
    [Fact]
    public void Move_ShiftsWithoutResizing()
    {
        var moved = SelectionGeometry.Move(Rect, 25, -15, Bounds);
        Assert.Equal(new Rectangle(125, 65, 200, 120), moved);
    }

    [Theory]
    [InlineData(-999, -999, 0, 0)]
    [InlineData(999, 999, 300, 280)]
    public void Move_ClampsInsideBounds(int dx, int dy, int x, int y)
    {
        var moved = SelectionGeometry.Move(Rect, dx, dy, Bounds);
        Assert.Equal(new Rectangle(x, y, 200, 120), moved);
    }

    // ── 缩放 ────────────────────────────────────────────────
    [Theory]
    [InlineData(SelectionHandle.TopLeft, 20, 10, 120, 90, 300, 200)]
    [InlineData(SelectionHandle.Top, 20, 10, 100, 90, 300, 200)]
    [InlineData(SelectionHandle.TopRight, 20, 10, 100, 90, 320, 200)]
    [InlineData(SelectionHandle.Right, 20, 10, 100, 80, 320, 200)]
    [InlineData(SelectionHandle.BottomRight, 20, 10, 100, 80, 320, 210)]
    [InlineData(SelectionHandle.Bottom, 20, 10, 100, 80, 300, 210)]
    [InlineData(SelectionHandle.BottomLeft, 20, 10, 120, 80, 300, 210)]
    [InlineData(SelectionHandle.Left, 20, 10, 120, 80, 300, 200)]
    public void Resize_MovesOnlyTheHandledEdges(SelectionHandle handle, int dx, int dy,
        int x, int y, int right, int bottom)
    {
        var r = SelectionGeometry.Resize(Rect, handle, dx, dy, Bounds);
        Assert.Equal(new Rectangle(x, y, right - x, bottom - y), r);
    }

    [Fact]
    public void Resize_NoneHandle_KeepsRectangle()
    {
        Assert.Equal(Rect, SelectionGeometry.Resize(Rect, SelectionHandle.None, 50, 50, Bounds));
    }

    [Theory]
    [InlineData(SelectionHandle.TopLeft)]
    [InlineData(SelectionHandle.Right)]
    [InlineData(SelectionHandle.Bottom)]
    public void Resize_RespectsMinimumSide(SelectionHandle handle)
    {
        var r = SelectionGeometry.Resize(Rect, handle, -999, -999, Bounds);
        Assert.True(r.Width >= SelectionGeometry.MinSide);
        Assert.True(r.Height >= SelectionGeometry.MinSide);
    }

    [Fact]
    public void Resize_NeverLeavesBounds()
    {
        foreach (SelectionHandle h in Enum.GetValues<SelectionHandle>())
        {
            var r = SelectionGeometry.Resize(Rect, h, 999, 999, Bounds);
            Assert.True(Bounds.Contains(r), $"handle {h} escaped bounds: {r}");
            var r2 = SelectionGeometry.Resize(Rect, h, -999, -999, Bounds);
            Assert.True(Bounds.Contains(r2), $"handle {h} escaped bounds: {r2}");
        }
    }

    [Fact]
    public void Resize_WithTinyBounds_StillFitsInside()
    {
        var tiny = new Rectangle(0, 0, 30, 30);
        var r = SelectionGeometry.Resize(new Rectangle(5, 5, 20, 20), SelectionHandle.BottomRight, 99, 99, tiny, 10);
        Assert.Equal(new Rectangle(5, 5, 25, 25), r);
        Assert.True(tiny.Contains(r));
    }

    [Fact]
    public void HandlePoint_ReturnsEmptyForNone()
    {
        Assert.Equal(Point.Empty, SelectionGeometry.HandlePoint(Rect, SelectionHandle.None));
        Assert.Equal(new Point(300, 200), SelectionGeometry.HandlePoint(Rect, SelectionHandle.BottomRight));
    }
}
