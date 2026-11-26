using System.Drawing;
using System.Drawing.Drawing2D;

namespace WinTools.Tools.Files;

/// <summary>
/// 内置表情图案：全部用 GDI+ 矢量绘制，不依赖系统 emoji 字体。
/// 系统字体在不同 Windows 版本上对彩色 emoji 的支持不一致，GDI 渲染常退化成
/// 黑白字形或豆腐块；这里用统一的 64×64 设计坐标自绘，选项面板与标注渲染共用。
/// </summary>
public static class EmojiArt
{
    /// <summary>一个可选表情：Key 存进标注命令，Name 用于悬停提示。</summary>
    public sealed record Item(string Key, string Name);

    /// <summary>工具栏可选的表情，顺序即面板中的排列顺序。</summary>
    public static readonly IReadOnlyList<Item> Items =
    [
        new("smile", "微笑"),
        new("laugh", "大笑"),
        new("love", "爱心眼"),
        new("cool", "酷"),
        new("cry", "流泪"),
        new("angry", "生气"),
        new("thumbsup", "点赞"),
        new("thumbsdown", "点踩"),
        new("heart", "爱心"),
        new("party", "庆祝"),
        new("check", "对勾"),
        new("cross", "叉号"),
        new("star", "星星"),
        new("fire", "火焰"),
        new("bulb", "灵感"),
        new("pray", "拜托")
    ];

    /// <summary>设计坐标边长；绘制时按目标矩形等比缩放。</summary>
    public const float DesignSize = 64f;

    private static readonly Color Face = Color.FromArgb(255, 204, 77);
    private static readonly Color FaceEdge = Color.FromArgb(224, 168, 46);
    private static readonly Color Ink = Color.FromArgb(74, 58, 42);
    private static readonly Color Mouth = Color.FromArgb(107, 62, 46);
    private static readonly Color Tongue = Color.FromArgb(255, 120, 130);
    private static readonly Color HeartRed = Color.FromArgb(232, 69, 95);
    private static readonly Color Skin = Color.FromArgb(247, 201, 72);
    private static readonly Color SkinEdge = Color.FromArgb(217, 166, 46);
    private static readonly Color Green = Color.FromArgb(52, 199, 89);
    private static readonly Color Red = Color.FromArgb(255, 69, 58);
    private static readonly Color Dark = Color.FromArgb(44, 48, 54);
    private static readonly Color GrayBase = Color.FromArgb(184, 191, 201);
    private static readonly Color GrayLine = Color.FromArgb(128, 138, 152);

    /// <summary>把表情画到目标矩形内；key 未知时退化成一个问号脸，不抛异常。</summary>
    public static void Draw(Graphics g, string key, RectangleF rect)
    {
        if (rect.Width <= 0 || rect.Height <= 0) return;

        var state = g.Save();
        try
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            float scale = Math.Min(rect.Width, rect.Height) / DesignSize;
            g.TranslateTransform(rect.X, rect.Y);
            g.ScaleTransform(scale, scale);

            switch (key)
            {
                case "smile": DrawSmile(g); break;
                case "laugh": DrawLaugh(g); break;
                case "love": DrawLove(g); break;
                case "cool": DrawCool(g); break;
                case "cry": DrawCry(g); break;
                case "angry": DrawAngry(g); break;
                case "thumbsup": DrawThumbsUp(g); break;
                case "thumbsdown": DrawThumbsDown(g); break;
                case "heart": DrawHeart(g); break;
                case "party": DrawParty(g); break;
                case "check": DrawCheck(g); break;
                case "cross": DrawCross(g); break;
                case "star": DrawStar(g); break;
                case "fire": DrawFire(g); break;
                case "bulb": DrawBulb(g); break;
                case "pray": DrawPray(g); break;
                default: DrawUnknown(g); break;
            }
        }
        finally
        {
            g.Restore(state);
        }
    }

    // ── 脸谱 ──────────────────────────────────────────────

    private static void DrawSmile(Graphics g)
    {
        DrawFace(g);
        using var ink = new SolidBrush(Ink);
        g.FillEllipse(ink, 19.5f, 24, 6.5f, 9.5f);
        g.FillEllipse(ink, 38, 24, 6.5f, 9.5f);
        using var mouth = MouthPen();
        g.DrawArc(mouth, 18, 30, 28, 22, 20, 140);
    }

    private static void DrawLaugh(Graphics g)
    {
        DrawFace(g);
        using var ink = MouthPen();
        // 闭眼笑：两条向上的弧
        g.DrawArc(ink, 14, 20, 13, 12, 200, 140);
        g.DrawArc(ink, 37, 20, 13, 12, 200, 140);

        using var mouth = new SolidBrush(Mouth);
        using var mouthPath = RoundRect(16, 37, 32, 18, 9);
        g.FillPath(mouth, mouthPath);
        using var teeth = new SolidBrush(Color.White);
        g.FillRectangle(teeth, 19, 39, 26, 5.5f);
        using var tongue = new SolidBrush(Tongue);
        g.FillEllipse(tongue, 25, 47.5f, 14, 7);
    }

    private static void DrawLove(Graphics g)
    {
        DrawFace(g);
        using var red = new SolidBrush(HeartRed);
        using (var left = HeartPath(new RectangleF(15, 21, 15, 13)))
            g.FillPath(red, left);
        using (var right = HeartPath(new RectangleF(34, 21, 15, 13)))
            g.FillPath(red, right);
        using var mouth = MouthPen();
        g.DrawArc(mouth, 21, 35, 22, 18, 20, 140);
    }

    private static void DrawCool(Graphics g)
    {
        DrawFace(g);
        using var dark = new SolidBrush(Dark);
        using (var left = RoundRect(12, 22, 18, 13, 4.5f))
            g.FillPath(dark, left);
        using (var right = RoundRect(34, 22, 18, 13, 4.5f))
            g.FillPath(dark, right);
        using var bridge = new Pen(Dark, 3) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(bridge, 29, 27.5f, 35, 27.5f);
        g.DrawLine(bridge, 13, 27, 6.5f, 24);
        g.DrawLine(bridge, 51, 27, 57.5f, 24);
        using var glint = new Pen(Color.FromArgb(150, 190, 210), 2) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(glint, 17, 25.5f, 22, 25.5f);
        g.DrawLine(glint, 39, 25.5f, 44, 25.5f);
        using var mouth = MouthPen();
        g.DrawArc(mouth, 22, 38, 20, 16, 20, 140);
    }

    private static void DrawCry(Graphics g)
    {
        DrawFace(g);
        using var ink = new SolidBrush(Ink);
        g.FillEllipse(ink, 19.5f, 23, 6.5f, 9);
        g.FillEllipse(ink, 38, 23, 6.5f, 9);
        using var tear = new SolidBrush(Color.FromArgb(79, 168, 232));
        using (var left = TeardropPath(new RectangleF(20, 34, 7, 11)))
            g.FillPath(tear, left);
        using (var right = TeardropPath(new RectangleF(37, 34, 7, 11)))
            g.FillPath(tear, right);
        using var mouthBrush = new SolidBrush(Mouth);
        g.FillEllipse(mouthBrush, 27, 44, 10, 9);
    }

    private static void DrawAngry(Graphics g)
    {
        using var face = new SolidBrush(Color.FromArgb(255, 107, 90));
        using var edge = new Pen(Color.FromArgb(224, 75, 58), 2f);
        g.FillEllipse(face, 5, 5, 54, 54);
        g.DrawEllipse(edge, 5, 5, 54, 54);

        using var brow = new Pen(Ink, 4.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(brow, 16, 23, 28, 31);
        g.DrawLine(brow, 48, 23, 36, 31);
        using var ink = new SolidBrush(Ink);
        g.FillEllipse(ink, 21, 31, 6, 6);
        g.FillEllipse(ink, 37, 31, 6, 6);
        using var mouth = MouthPen();
        g.DrawArc(mouth, 21, 43, 22, 14, 200, 140);
    }

    private static void DrawUnknown(Graphics g)
    {
        DrawFace(g);
        using var ink = new SolidBrush(Ink);
        g.FillEllipse(ink, 19.5f, 24, 6.5f, 8);
        g.FillEllipse(ink, 38, 24, 6.5f, 8);
        // 用矢量画问号，避免 GDI 文本在缩放坐标里错位
        using var pen = new Pen(Ink, 4.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawArc(pen, 23, 33, 18, 16, 180, 200);
        g.DrawLine(pen, 32, 44, 32, 47);
        g.FillEllipse(ink, 29.5f, 49, 5, 5);
    }

    private static void DrawFace(Graphics g)
    {
        using var face = new SolidBrush(Face);
        using var edge = new Pen(FaceEdge, 2f);
        g.FillEllipse(face, 5, 5, 54, 54);
        g.DrawEllipse(edge, 5, 5, 54, 54);
    }

    // ── 手势 ──────────────────────────────────────────────

    private static void DrawThumbsUp(Graphics g)
    {
        using var skin = new SolidBrush(Skin);
        using var edge = new Pen(SkinEdge, 2f);
        using (var thumb = RoundRect(24, 10, 12, 26, 6))
        {
            g.FillPath(skin, thumb);
            g.DrawPath(edge, thumb);
        }
        using (var palm = RoundRect(14, 29, 33, 24, 7))
        {
            g.FillPath(skin, palm);
            g.DrawPath(edge, palm);
        }
        using var line = new Pen(SkinEdge, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(line, 22, 35, 22, 48);
        g.DrawLine(line, 28, 34, 28, 49);
        g.DrawLine(line, 34, 34, 34, 48);
        g.DrawLine(line, 40, 34, 40, 46);
    }

    private static void DrawThumbsDown(Graphics g)
    {
        var state = g.Save();
        g.TranslateTransform(64, 64);
        g.RotateTransform(180);
        DrawThumbsUp(g);
        g.Restore(state);
    }

    private static void DrawPray(Graphics g)
    {
        DrawHand(g, 28, 33, 12);
        DrawHand(g, 36, 33, -12);
    }

    private static void DrawHand(Graphics g, float cx, float cy, float angle)
    {
        var state = g.Save();
        g.TranslateTransform(cx, cy);
        g.RotateTransform(angle);
        using var skin = new SolidBrush(Skin);
        using var edge = new Pen(SkinEdge, 2f);
        using var hand = RoundRect(-8, -28, 16, 52, 8);
        g.FillPath(skin, hand);
        g.DrawPath(edge, hand);
        using var line = new Pen(SkinEdge, 1.4f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(line, -2, -20, -2, 12);
        g.DrawLine(line, 3, -18, 3, 12);
        g.Restore(state);
    }

    // ── 物件 ──────────────────────────────────────────────

    private static void DrawHeart(Graphics g)
    {
        using var path = HeartPath(new RectangleF(8, 10, 48, 44));
        using var fill = new SolidBrush(HeartRed);
        using var edge = new Pen(Color.FromArgb(198, 42, 68), 2f);
        g.FillPath(fill, path);
        g.DrawPath(edge, path);
        using var shine = new SolidBrush(Color.FromArgb(110, 255, 255, 255));
        g.FillEllipse(shine, 18, 19, 9, 7);
    }

    private static void DrawParty(Graphics g)
    {
        using var cone = new SolidBrush(Color.FromArgb(245, 166, 35));
        using var edge = new Pen(Color.FromArgb(217, 138, 18), 2f);
        using var path = new GraphicsPath();
        path.AddPolygon([new PointF(7, 57), new PointF(26, 26), new PointF(35, 35)]);
        g.FillPath(cone, path);
        g.DrawPath(edge, path);

        using var stripe = new Pen(Color.FromArgb(255, 209, 102), 2.4f)
        { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(stripe, 12, 50, 22, 34);
        g.DrawLine(stripe, 18, 53, 29, 36);

        DrawConfetti(g, 41, 13, 5, Color.FromArgb(52, 199, 89));
        DrawConfetti(g, 51, 22, 4, Color.FromArgb(79, 168, 232));
        DrawConfetti(g, 39, 31, 4.5f, Color.FromArgb(232, 69, 95));
        DrawConfetti(g, 50, 38, 3.5f, Color.FromArgb(255, 204, 77));
        DrawConfetti(g, 45, 48, 3.5f, Color.FromArgb(155, 120, 235));
    }

    private static void DrawConfetti(Graphics g, float x, float y, float size, Color color)
    {
        using var brush = new SolidBrush(color);
        g.FillEllipse(brush, x, y, size, size);
    }

    private static void DrawCheck(Graphics g)
    {
        using var circle = new SolidBrush(Green);
        g.FillEllipse(circle, 5, 5, 54, 54);
        using var white = new Pen(Color.White, 7f)
        { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        using var path = new GraphicsPath();
        path.AddLines([new PointF(17, 33), new PointF(27.5f, 43.5f), new PointF(47, 21)]);
        g.DrawPath(white, path);
    }

    private static void DrawCross(Graphics g)
    {
        using var circle = new SolidBrush(Red);
        g.FillEllipse(circle, 5, 5, 54, 54);
        using var white = new Pen(Color.White, 7f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawLine(white, 20, 20, 44, 44);
        g.DrawLine(white, 44, 20, 20, 44);
    }

    private static void DrawStar(Graphics g)
    {
        using var path = new GraphicsPath();
        path.AddPolygon(StarPoints(32, 33, 27, 11.5f, 5));
        using var fill = new SolidBrush(Color.FromArgb(255, 204, 77));
        using var edge = new Pen(Color.FromArgb(224, 168, 46), 2f) { LineJoin = LineJoin.Round };
        g.FillPath(fill, path);
        g.DrawPath(edge, path);
    }

    private static void DrawFire(Graphics g)
    {
        using var outer = new GraphicsPath();
        outer.StartFigure();
        outer.AddBezier(32, 58, 18, 53, 13, 39, 22, 26);
        outer.AddBezier(22, 26, 25, 32, 27, 30, 27, 22);
        outer.AddBezier(27, 22, 34, 28, 34, 20, 38, 12);
        outer.AddBezier(38, 12, 40, 24, 49, 28, 49, 40);
        outer.AddBezier(49, 40, 53, 51, 43, 57, 32, 58);
        outer.CloseFigure();
        using var gradient = new LinearGradientBrush(new PointF(32, 58), new PointF(32, 12),
            Color.FromArgb(255, 183, 3), Color.FromArgb(251, 86, 7));
        g.FillPath(gradient, outer);
        using var edge = new Pen(Color.FromArgb(232, 89, 12), 1.8f);
        g.DrawPath(edge, outer);

        using var inner = new GraphicsPath();
        inner.StartFigure();
        inner.AddBezier(32, 54, 23, 50, 21, 41, 28, 33);
        inner.AddBezier(28, 33, 30, 38, 33, 37, 33, 31);
        inner.AddBezier(33, 31, 39, 37, 43, 40, 43, 45);
        inner.AddBezier(43, 45, 44, 51, 38, 54, 32, 54);
        inner.CloseFigure();
        using var innerFill = new SolidBrush(Color.FromArgb(255, 209, 102));
        g.FillPath(innerFill, inner);
    }

    private static void DrawBulb(Graphics g)
    {
        using var ray = new Pen(Color.FromArgb(245, 166, 35), 2.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        for (int i = 0; i < 8; i++)
        {
            double a = Math.PI * 2 * i / 8 - Math.PI / 2;
            float x1 = 32 + (float)Math.Cos(a) * 21;
            float y1 = 29 + (float)Math.Sin(a) * 21;
            float x2 = 32 + (float)Math.Cos(a) * 27;
            float y2 = 29 + (float)Math.Sin(a) * 27;
            g.DrawLine(ray, x1, y1, x2, y2);
        }
        using var bulb = new SolidBrush(Color.FromArgb(255, 211, 92));
        using var edge = new Pen(FaceEdge, 2f);
        g.FillEllipse(bulb, 19, 8, 26, 26);
        g.DrawEllipse(edge, 19, 8, 26, 26);
        using var shine = new SolidBrush(Color.FromArgb(120, 255, 255, 255));
        g.FillEllipse(shine, 24, 13, 7, 6);
        using var baseFill = new SolidBrush(GrayBase);
        using var baseEdge = new Pen(GrayLine, 1.6f);
        using (var b = RoundRect(25, 33, 14, 10, 2))
        {
            g.FillPath(baseFill, b);
            g.DrawPath(baseEdge, b);
        }
        using var line = new Pen(GrayLine, 1.5f);
        g.DrawLine(line, 27, 37, 37, 37);
        g.DrawLine(line, 27, 40, 37, 40);
    }

    // ── 几何辅助 ──────────────────────────────────────────

    private static Pen MouthPen() => new(Ink, 3.2f)
    { StartCap = LineCap.Round, EndCap = LineCap.Round };

    private static GraphicsPath RoundRect(float x, float y, float w, float h, float radius)
    {
        float d = Math.Min(Math.Min(radius, w / 2), h / 2) * 2;
        var path = new GraphicsPath();
        path.AddArc(x, y, d, d, 180, 90);
        path.AddArc(x + w - d, y, d, d, 270, 90);
        path.AddArc(x + w - d, y + h - d, d, d, 0, 90);
        path.AddArc(x, y + h - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static GraphicsPath HeartPath(RectangleF r)
    {
        float w = r.Width, h = r.Height;
        var p = new GraphicsPath();
        p.StartFigure();
        p.AddBezier(r.Left + w * 0.50f, r.Top + h * 0.20f,
                    r.Left + w * 0.18f, r.Top - h * 0.08f,
                    r.Left - w * 0.02f, r.Top + h * 0.28f,
                    r.Left + w * 0.12f, r.Top + h * 0.50f);
        p.AddBezier(r.Left + w * 0.12f, r.Top + h * 0.50f,
                    r.Left + w * 0.25f, r.Top + h * 0.70f,
                    r.Left + w * 0.42f, r.Top + h * 0.82f,
                    r.Left + w * 0.50f, r.Top + h * 0.96f);
        p.AddBezier(r.Left + w * 0.50f, r.Top + h * 0.96f,
                    r.Left + w * 0.58f, r.Top + h * 0.82f,
                    r.Left + w * 0.75f, r.Top + h * 0.70f,
                    r.Left + w * 0.88f, r.Top + h * 0.50f);
        p.AddBezier(r.Left + w * 0.88f, r.Top + h * 0.50f,
                    r.Left + w * 1.02f, r.Top + h * 0.28f,
                    r.Left + w * 0.82f, r.Top - h * 0.08f,
                    r.Left + w * 0.50f, r.Top + h * 0.20f);
        p.CloseFigure();
        return p;
    }

    private static GraphicsPath TeardropPath(RectangleF r)
    {
        float cx = r.Left + r.Width / 2;
        var p = new GraphicsPath();
        p.StartFigure();
        p.AddBezier(cx, r.Top,
                    r.Right, r.Top + r.Height * 0.45f,
                    r.Right, r.Bottom - r.Height * 0.22f,
                    cx, r.Bottom);
        p.AddBezier(cx, r.Bottom,
                    r.Left, r.Bottom - r.Height * 0.22f,
                    r.Left, r.Top + r.Height * 0.45f,
                    cx, r.Top);
        p.CloseFigure();
        return p;
    }

    private static PointF[] StarPoints(float cx, float cy, float outer, float inner, int count)
    {
        var points = new PointF[count * 2];
        for (int i = 0; i < points.Length; i++)
        {
            double angle = -Math.PI / 2 + Math.PI * i / count;
            float radius = i % 2 == 0 ? outer : inner;
            points[i] = new PointF(
                cx + (float)Math.Cos(angle) * radius,
                cy + (float)Math.Sin(angle) * radius);
        }
        return points;
    }
}
