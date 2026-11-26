using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace WinTools.Tools.Files;

/// <summary>标注工具类型。None 表示不绘制（移动/取消）。</summary>
public enum AnnotationTool
{
    None, Rectangle, Ellipse, Arrow, Pen, Text, Emoji, Mosaic
}

/// <summary>一次绘制操作的几何与样式；纯数据，不含 GDI 资源，便于单测。</summary>
public sealed class AnnotationStroke
{
    public AnnotationTool Tool { get; init; }
    public Color Color { get; init; } = Color.Red;
    public int Width { get; init; } = 3;
    /// <summary>矩形/椭圆/马赛克的区域；文字/表情的落点存于 Bounds.Location。</summary>
    public Rectangle Bounds { get; init; }
    /// <summary>箭头两点或画笔轨迹（图像坐标）。</summary>
    public Point[] Points { get; init; } = [];
    /// <summary>文字内容或表情字符。</summary>
    public string Text { get; init; } = string.Empty;
    /// <summary>文字字号（像素）。</summary>
    public int FontSize { get; init; } = 24;
    /// <summary>马赛克块大小（像素），越大越糊。</summary>
    public int BlockSize { get; init; } = 12;
}

/// <summary>
/// 标注命令栈：一次绘制 = 一个 Stroke；撤销/重做只动游标，重绘时从底图叠加 0..Cursor-1。
/// 马赛克是破坏性像素化，故每次重绘都从原始底图 Clone 开始——绝不原地修改底图，
/// 否则撤销无法恢复。底图所有权归调用方，本类不 Dispose 它。
/// </summary>
public sealed class AnnotationStack
{
    private readonly List<AnnotationStroke> _strokes = new();
    private int _cursor;

    public int Count => _strokes.Count;
    public int Cursor => _cursor;
    public bool CanUndo => _cursor > 0;
    public bool CanRedo => _cursor < _strokes.Count;
    public IReadOnlyList<AnnotationStroke> Active => _strokes.Take(_cursor).ToList();

    /// <summary>新增绘制；若当前处于撤销状态，丢弃 redo 分支后再追加。</summary>
    public void Push(AnnotationStroke stroke)
    {
        if (_cursor < _strokes.Count) _strokes.RemoveRange(_cursor, _strokes.Count - _cursor);
        _strokes.Add(stroke);
        _cursor = _strokes.Count;
    }

    public bool Undo()
    {
        if (!CanUndo) return false;
        _cursor--;
        return true;
    }

    public bool Redo()
    {
        if (!CanRedo) return false;
        _cursor++;
        return true;
    }

    public void Clear()
    {
        _strokes.Clear();
        _cursor = 0;
    }
}

/// <summary>标注渲染纯逻辑：把命令叠加到底图副本上。可直接在单测里传入假 Bitmap 验证。</summary>
public static class AnnotationRenderer
{
    /// <summary>合成底图与活动命令，返回新位图（调用方负责 Dispose）。</summary>
    public static Bitmap Render(Bitmap source, IEnumerable<AnnotationStroke> strokes)
    {
        var result = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(result))
        {
            g.DrawImage(source, 0, 0);
            foreach (var s in strokes) Draw(g, source, s);
        }
        return result;
    }

    /// <summary>绘制单条命令；公开以便覆盖层预览正在拖拽的临时图形。</summary>
    public static void Draw(Graphics g, Bitmap source, AnnotationStroke s)
    {
        if (s.Tool == AnnotationTool.None) return;
        switch (s.Tool)
        {
            case AnnotationTool.Rectangle:
            case AnnotationTool.Ellipse:
                using (var pen = new Pen(s.Color, Math.Max(1, s.Width)))
                {
                    var r = AnnotationGeometry.Normalize(s.Bounds);
                    if (s.Tool == AnnotationTool.Rectangle)
                        g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
                    else
                        g.DrawEllipse(pen, r.X, r.Y, r.Width, r.Height);
                }
                break;
            case AnnotationTool.Arrow:
                if (s.Points.Length >= 2) DrawArrow(g, s.Points[0], s.Points[1], s.Color, s.Width);
                break;
            case AnnotationTool.Pen:
                if (s.Points.Length >= 2)
                    using (var pen = new Pen(s.Color, Math.Max(1, s.Width))
                           { StartCap = LineCap.Round, LineJoin = LineJoin.Round })
                        g.DrawLines(pen, s.Points);
                break;
            case AnnotationTool.Text:
                if (!string.IsNullOrEmpty(s.Text))
                    using (var font = new Font("Microsoft YaHei", Math.Max(6, s.FontSize), FontStyle.Bold, GraphicsUnit.Pixel))
                        TextRenderer.DrawText(g, s.Text, font,
                            new Point(s.Bounds.Left, s.Bounds.Top), s.Color,
                            TextFormatFlags.NoPadding);
                break;
            case AnnotationTool.Emoji:
                if (!string.IsNullOrEmpty(s.Text))
                {
                    int side = Math.Clamp(s.FontSize, 12, 256);
                    EmojiArt.Draw(g, s.Text,
                        new RectangleF(s.Bounds.Left, s.Bounds.Top, side, side));
                }
                break;
            case AnnotationTool.Mosaic:
                var r2 = AnnotationGeometry.Normalize(s.Bounds);
                var clipped = Rectangle.Intersect(r2, new Rectangle(0, 0, source.Width, source.Height));
                if (clipped.Width > 0 && clipped.Height > 0)
                {
                    var mosaic = MosaicEffect.Pixelate(source, clipped, s.BlockSize);
                    g.DrawImage(mosaic, clipped.Location);
                    mosaic.Dispose();
                }
                break;
        }
    }

    /// <summary>画带箭头的直线；箭头长度随线宽缩放，短线自动缩短避免过头。</summary>
    public static void DrawArrow(Graphics g, Point from, Point to, Color color, int width)
    {
        using var pen = new Pen(color, Math.Max(1, width)) { EndCap = LineCap.Round };
        g.DrawLine(pen, from, to);
        double dx = to.X - from.X, dy = to.Y - from.Y;
        double angle = Math.Atan2(dy, dx);
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1) return;
        int head = (int)Math.Clamp(width * 3.5, 8, Math.Max(8, len / 2));
        double spread = 0.45;
        var p1 = new Point((int)(to.X - head * Math.Cos(angle - spread)), (int)(to.Y - head * Math.Sin(angle - spread)));
        var p2 = new Point((int)(to.X - head * Math.Cos(angle + spread)), (int)(to.Y - head * Math.Sin(angle + spread)));
        using var brush = new SolidBrush(color);
        g.FillPolygon(brush, [to, p1, p2]);
    }
}

/// <summary>标注几何纯函数。</summary>
public static class AnnotationGeometry
{
    /// <summary>把起止两点归一化为矩形（反向拖动自动纠正，不裁剪——裁剪交给调用方）。</summary>
    public static Rectangle Normalize(Rectangle rect) =>
        Rectangle.FromLTRB(Math.Min(rect.Left, rect.Right), Math.Min(rect.Top, rect.Bottom),
            Math.Max(rect.Left, rect.Right), Math.Max(rect.Top, rect.Bottom));

    public static Rectangle Normalize(Point start, Point end) =>
        Normalize(new Rectangle(start.X, start.Y, end.X - start.X, end.Y - start.Y));
}

/// <summary>选区调整手柄：四角 + 四边中点。</summary>
public enum SelectionHandle
{
    None, TopLeft, Top, TopRight, Right, BottomRight, Bottom, BottomLeft, Left
}

/// <summary>
/// 选区移动/缩放的几何纯函数。输入一律是图像坐标（与底图像素一一对应），
/// 由覆盖层负责把屏幕坐标换算进来；本类不依赖 WinForms，可直接单测。
/// </summary>
public static class SelectionGeometry
{
    /// <summary>选区最小边长，太小则手柄无法操作。</summary>
    public const int MinSide = 16;
    /// <summary>手柄命中半径（像素）。</summary>
    public const int HitTolerance = 10;

    /// <summary>按枚举顺序返回八个手柄的坐标：左上、上、右上、右、右下、下、左下、左。</summary>
    public static Point[] Handles(Rectangle r) =>
    [
        new(r.Left, r.Top),
        new(r.Left + r.Width / 2, r.Top),
        new(r.Right, r.Top),
        new(r.Right, r.Top + r.Height / 2),
        new(r.Right, r.Bottom),
        new(r.Left + r.Width / 2, r.Bottom),
        new(r.Left, r.Bottom),
        new(r.Left, r.Top + r.Height / 2)
    ];

    /// <summary>把手柄枚举映射回 Handles 数组下标。</summary>
    private static int IndexOf(SelectionHandle handle) => handle switch
    {
        SelectionHandle.TopLeft => 0,
        SelectionHandle.Top => 1,
        SelectionHandle.TopRight => 2,
        SelectionHandle.Right => 3,
        SelectionHandle.BottomRight => 4,
        SelectionHandle.Bottom => 5,
        SelectionHandle.BottomLeft => 6,
        SelectionHandle.Left => 7,
        _ => -1
    };

    public static Point HandlePoint(Rectangle r, SelectionHandle handle)
    {
        int i = IndexOf(handle);
        return i < 0 ? Point.Empty : Handles(r)[i];
    }

    /// <summary>
    /// 命中测试：手柄优先（四角比四边优先），其次是选区内部（返回 None 表示可整体移动），
    /// 都不命中时返回 <see cref="SelectionHandle.None"/>，由调用方决定是否在选区外重新框选。
    /// </summary>
    public static SelectionHandle HitTest(Rectangle rect, Point p, int tolerance = HitTolerance)
    {
        int t = Math.Max(1, tolerance);
        var points = Handles(rect);
        // 先四角（索引 0/2/4/6），后四边，避免角点被边抢走
        foreach (int i in new[] { 0, 2, 4, 6, 1, 3, 5, 7 })
        {
            var hp = points[i];
            if (Math.Abs(p.X - hp.X) <= t && Math.Abs(p.Y - hp.Y) <= t)
                return (SelectionHandle)(i + 1);
        }
        return SelectionHandle.None;
    }

    /// <summary>点是否落在选区内（含边框），用于区分"移动选区"与"选区外重新框选"。</summary>
    public static bool Contains(Rectangle rect, Point p) =>
        p.X >= rect.Left && p.X <= rect.Right && p.Y >= rect.Top && p.Y <= rect.Bottom;

    /// <summary>整体移动选区，始终留在 bounds 内。</summary>
    public static Rectangle Move(Rectangle rect, int dx, int dy, Rectangle bounds)
    {
        int x = Math.Clamp(rect.X + dx, bounds.Left, Math.Max(bounds.Left, bounds.Right - rect.Width));
        int y = Math.Clamp(rect.Y + dy, bounds.Top, Math.Max(bounds.Top, bounds.Bottom - rect.Height));
        return new Rectangle(x, y, rect.Width, rect.Height);
    }

    /// <summary>
    /// 按手柄方向缩放选区。被拖动的一侧跟随指针，其余三侧保持不动；
    /// 越过对侧时按最小边长停住，整体不会跑出 bounds。
    /// </summary>
    public static Rectangle Resize(Rectangle rect, SelectionHandle handle, int dx, int dy,
        Rectangle bounds, int minSide = MinSide)
    {
        if (IndexOf(handle) < 0) return rect;
        int min = Math.Max(1, minSide);
        int left = rect.Left, top = rect.Top, right = rect.Right, bottom = rect.Bottom;

        if (handle is SelectionHandle.TopLeft or SelectionHandle.Left or SelectionHandle.BottomLeft)
            left += dx;
        if (handle is SelectionHandle.TopLeft or SelectionHandle.Top or SelectionHandle.TopRight)
            top += dy;
        if (handle is SelectionHandle.TopRight or SelectionHandle.Right or SelectionHandle.BottomRight)
            right += dx;
        if (handle is SelectionHandle.BottomLeft or SelectionHandle.Bottom or SelectionHandle.BottomRight)
            bottom += dy;

        // 最小边长：只回推被拖动的那一侧
        if (right - left < min)
        {
            if (handle is SelectionHandle.TopLeft or SelectionHandle.Left or SelectionHandle.BottomLeft)
                left = right - min;
            else
                right = left + min;
        }
        if (bottom - top < min)
        {
            if (handle is SelectionHandle.TopLeft or SelectionHandle.Top or SelectionHandle.TopRight)
                top = bottom - min;
            else
                bottom = top + min;
        }

        // 裁剪到外层边界（bounds 通常比最小边长大得多）
        left = Math.Clamp(left, bounds.Left, bounds.Right - min);
        right = Math.Clamp(right, left + min, bounds.Right);
        top = Math.Clamp(top, bounds.Top, bounds.Bottom - min);
        bottom = Math.Clamp(bottom, top + min, bounds.Bottom);
        return Rectangle.FromLTRB(left, top, right, bottom);
    }
}

/// <summary>马赛克纯算法：把区域内像素按块取平均，得到像素化位图。</summary>
public static class MosaicEffect
{
    /// <summary>块大小钳制到 2..64。</summary>
    public static int ClampBlock(int size) => Math.Clamp(size, 2, 64);

    /// <summary>
    /// 把 source 中 region 区域像素化，返回与区域等大的新位图（调用方负责 Dispose）。
    /// 原理：先缩到 region/block 的小图（双线性采样），再用 NearestNeighbor 放大回原尺寸。
    /// </summary>
    public static Bitmap Pixelate(Bitmap source, Rectangle region, int blockSize)
    {
        int block = ClampBlock(blockSize);
        int w = Math.Max(1, (region.Width + block - 1) / block);
        int h = Math.Max(1, (region.Height + block - 1) / block);
        var small = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        var result = new Bitmap(Math.Max(1, region.Width), Math.Max(1, region.Height), PixelFormat.Format32bppArgb);
        try
        {
            using (var sg = Graphics.FromImage(small))
            {
                sg.InterpolationMode = InterpolationMode.HighQualityBilinear;
                sg.PixelOffsetMode = PixelOffsetMode.Half;
                sg.DrawImage(source, new Rectangle(0, 0, w, h), region, GraphicsUnit.Pixel);
            }
            using (var rg = Graphics.FromImage(result))
            {
                rg.InterpolationMode = InterpolationMode.NearestNeighbor;
                rg.PixelOffsetMode = PixelOffsetMode.Half;
                rg.DrawImage(small, new Rectangle(0, 0, result.Width, result.Height));
            }
            return result;
        }
        catch
        {
            result.Dispose();
            throw;
        }
        finally { small.Dispose(); }
    }
}
