using System.Drawing;
using System.Drawing.Drawing2D;

namespace WinTools.Core.Ui;

/// <summary>
/// 矢量图标库。所有图标按 24x24 设计栅格用 GDI+ 路径绘制，再等比缩放到目标矩形，
/// 因此不依赖任何图标字体（缺字不会变成方块），任意 DPI 与任意尺寸都保持锐利。
/// 图标名即语义，工具卡片、导航、按钮、反馈条共用同一套图形语言。
/// </summary>
public static class IconFactory
{
    private const float Grid = 24f;

    private delegate void Glyph(Graphics g, RectangleF r, float w, Color c);

    // ── 栅格坐标换算 ────────────────────────────────────────────
    private static PointF P(RectangleF r, float x, float y)
        => new(r.X + x * r.Width / Grid, r.Y + y * r.Height / Grid);

    private static RectangleF R(RectangleF r, float x, float y, float w, float h)
        => new(r.X + x * r.Width / Grid, r.Y + y * r.Height / Grid,
               w * r.Width / Grid, h * r.Height / Grid);

    private static Pen Stroke(float w, Color c) => new(c, w)
    {
        StartCap = LineCap.Round,
        EndCap = LineCap.Round,
        LineJoin = LineJoin.Round
    };

    private static void Line(Graphics g, Pen p, RectangleF r, float x1, float y1, float x2, float y2)
        => g.DrawLine(p, P(r, x1, y1), P(r, x2, y2));

    private static void Circle(Graphics g, Pen p, RectangleF r, float cx, float cy, float rad)
    {
        var b = R(r, cx - rad, cy - rad, rad * 2, rad * 2);
        g.DrawEllipse(p, b);
    }

    private static void FillCircle(Graphics g, Color c, RectangleF r, float cx, float cy, float rad)
    {
        using var b = new SolidBrush(c);
        g.FillEllipse(b, R(r, cx - rad, cy - rad, rad * 2, rad * 2));
    }

    private static void Arc(Graphics g, Pen p, RectangleF r, float cx, float cy, float rad,
        float start, float sweep)
        => g.DrawArc(p, R(r, cx - rad, cy - rad, rad * 2, rad * 2), start, sweep);

    private static void Poly(Graphics g, Pen p, RectangleF r, params float[] xy)
    {
        var pts = new PointF[xy.Length / 2];
        for (var i = 0; i < pts.Length; i++) pts[i] = P(r, xy[2 * i], xy[2 * i + 1]);
        g.DrawLines(p, pts);
    }

    private static void ClosedPoly(Graphics g, Pen p, RectangleF r, params float[] xy)
    {
        var pts = new PointF[xy.Length / 2];
        for (var i = 0; i < pts.Length; i++) pts[i] = P(r, xy[2 * i], xy[2 * i + 1]);
        g.DrawPolygon(p, pts);
    }

    private static void RR(Graphics g, Pen p, RectangleF r, float x, float y, float w, float h, float rad)
    {
        using var path = Path(R(r, x, y, w, h), rad * r.Width / Grid);
        g.DrawPath(p, path);
    }

    private static void FillRR(Graphics g, Color c, RectangleF r, float x, float y, float w, float h, float rad)
    {
        using var path = Path(R(r, x, y, w, h), rad * r.Width / Grid);
        using var b = new SolidBrush(c);
        g.FillPath(b, path);
    }

    private static GraphicsPath Path(RectangleF b, float radius)
    {
        var path = new GraphicsPath();
        var d = Math.Min(radius * 2, Math.Min(b.Width, b.Height));
        if (d <= 0.5f) { path.AddRectangle(b); return path; }
        path.AddArc(b.X, b.Y, d, d, 180, 90);
        path.AddArc(b.Right - d, b.Y, d, d, 270, 90);
        path.AddArc(b.Right - d, b.Bottom - d, d, d, 0, 90);
        path.AddArc(b.X, b.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>箭头：从 (x1,y1) 指向 (x2,y2)，head 控制两翼长度。</summary>
    private static void Arrow(Graphics g, Pen p, RectangleF r, float x1, float y1, float x2, float y2, float head = 3f)
    {
        Line(g, p, r, x1, y1, x2, y2);
        double dx = x2 - x1, dy = y2 - y1;
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 0.01) return;
        dx /= len; dy /= len;
        const double a = 0.42; // 翼与杆的夹角
        double ca = Math.Cos(a), sa = Math.Sin(a);
        float bx = (float)(x2 - head * (dx * ca + dy * sa));
        float by = (float)(y2 - head * (dy * ca - dx * sa));
        float cx = (float)(x2 - head * (dx * ca - dy * sa));
        float cy = (float)(y2 - head * (dy * ca + dx * sa));
        Line(g, p, r, bx, by, x2, y2);
        Line(g, p, r, cx, cy, x2, y2);
    }

    /// <summary>星形轮廓：points 个角，inner 为谷点半径，filled 为真时填充。</summary>
    private static void Star(Graphics g, Pen p, RectangleF r, float cx, float cy,
        float outer, float inner, int points, bool filled)
    {
        var pts = new PointF[points * 2];
        for (var i = 0; i < points * 2; i++)
        {
            double ang = -Math.PI / 2 + i * Math.PI / points;
            float rad = i % 2 == 0 ? outer : inner;
            pts[i] = P(r, cx + rad * (float)Math.Cos(ang), cy + rad * (float)Math.Sin(ang));
        }
        if (filled) { using var b = new SolidBrush(p.Color); g.FillPolygon(b, pts); }
        else g.DrawPolygon(p, pts);
    }

    // ── 图标注册表 ──────────────────────────────────────────────
    private static readonly Dictionary<string, Glyph> Registry = new()
    {
        // 工具图标（每个工具语义唯一）
        ["json"] = (g, r, w, c) =>
        {
            using var p = Stroke(w, c);
            Poly(g, p, r, 9.6f, 4, 8, 4, 8, 10.6f, 5.6f, 12, 8, 13.4f, 8, 20, 9.6f, 20);
            Poly(g, p, r, 14.4f, 4, 16, 4, 16, 10.6f, 18.4f, 12, 16, 13.4f, 16, 20, 14.4f, 20);
        },
        ["base64"] = (g, r, w, c) =>
        {
            using var p = Stroke(w, c);
            Arrow(g, p, r, 5, 9, 19, 9, 3.4f);
            Arrow(g, p, r, 19, 15, 5, 15, 3.4f);
        },
        ["key"] = (g, r, w, c) =>
        {
            using var p = Stroke(w, c);
            Circle(g, p, r, 8.5f, 8.5f, 4.6f);
            Circle(g, p, r, 8.5f, 8.5f, 1.2f);
            Line(g, p, r, 11.8f, 11.8f, 19.5f, 19.5f);
            Line(g, p, r, 16.4f, 19.6f, 18.6f, 21.8f);
            Line(g, p, r, 19.6f, 16.4f, 21.8f, 18.6f);
        },
        ["fingerprint"] = (g, r, w, c) =>
        {
            using var p = Stroke(w, c);
            Arc(g, p, r, 12, 13, 8.4f, 205, 130);
            Arc(g, p, r, 12, 13, 5.6f, 215, 130);
            Arc(g, p, r, 12, 13, 2.8f, 230, 120);
            Line(g, p, r, 6.4f, 18.5f, 5.2f, 21.5f);
            Line(g, p, r, 17.6f, 18.5f, 18.8f, 21.5f);
        },
        ["tag"] = (g, r, w, c) =>
        {
            using var p = Stroke(w, c);
            ClosedPoly(g, p, r, 4, 4, 13.4f, 4, 20, 10.6f, 12, 18.6f, 4, 10.6f);
            FillCircle(g, c, r, 9.4f, 9.4f, 1.5f);
        },
        ["chip"] = (g, r, w, c) =>
        {
            using var p = Stroke(w, c);
            RR(g, p, r, 7, 7, 10, 10, 2);
            RR(g, p, r, 10.4f, 10.4f, 3.2f, 3.2f, 1);
            for (var i = 0; i < 3; i++)
            {
                float t = 9.5f + i * 2.5f;
                Line(g, p, r, t, 7, t, 4);
                Line(g, p, r, t, 17, t, 20);
                Line(g, p, r, 7, t, 4, t);
                Line(g, p, r, 17, t, 20, t);
            }
        },
        ["port"] = (g, r, w, c) =>
        {
            using var p = Stroke(w, c);
            RR(g, p, r, 4.5f, 9, 15, 8.5f, 2);
            Line(g, p, r, 8.5f, 9, 8.5f, 5);
            Line(g, p, r, 15.5f, 9, 15.5f, 5);
            Line(g, p, r, 12, 9, 12, 4.2f);
            Line(g, p, r, 8, 17.5f, 8, 20.5f);
            Line(g, p, r, 12, 17.5f, 12, 20.5f);
            Line(g, p, r, 16, 17.5f, 16, 20.5f);
        },
        ["network-info"] = (g, r, w, c) =>
        {
            using var p = Stroke(w, c);
            Circle(g, p, r, 11.5f, 12.5f, 8.2f);
            g.DrawEllipse(p, R(r, 4.4f, 12.5f, 14.2f, 16.8f));
            Line(g, p, r, 3.5f, 9.5f, 19.5f, 9.5f);
            Line(g, p, r, 3.5f, 15.5f, 19.5f, 15.5f);
            // 右上角亮点：与纯分类 network 图标区分，表达"查看 / 探测"。
            FillCircle(g, c, r, 18.4f, 6f, 2.2f);
        },
        // 两个交叠的环 + 连接线：URL 链接语义，避免与分类图标 network 混淆。
        ["url"] = (g, r, w, c) =>
        {
            using var p = Stroke(w, c);
            Arc(g, p, r, 8.6f, 15.4f, 5.2f, 30, 250);
            Arc(g, p, r, 15.4f, 8.6f, 5.2f, 210, 250);
            Line(g, p, r, 10.4f, 13.6f, 13.6f, 10.4f);
        },
        // 圆角面板 + 左侧大括号 + 三行键值：环境变量列表语义。
        ["environment"] = (g, r, w, c) =>
        {
            using var p = Stroke(w, c);
            RR(g, p, r, 3.4f, 3.4f, 17.2f, 17.2f, 2.4f);
            Line(g, p, r, 7.8f, 8.2f, 7.8f, 15.8f);
            Line(g, p, r, 5.8f, 8.2f, 7.8f, 8.2f);
            Line(g, p, r, 5.8f, 15.8f, 7.8f, 15.8f);
            Line(g, p, r, 11.4f, 8.6f, 18.2f, 8.6f);
            Line(g, p, r, 11.4f, 12f, 16.4f, 12f);
            Line(g, p, r, 11.4f, 15.4f, 18.2f, 15.4f);
        },
        // 三个定位角 + 内嵌数据点：二维码语义。
        ["qrcode"] = (g, r, w, c) =>
        {
            using var p = Stroke(w, c);
            // 左上 / 右上 / 左下三个"回"字定位框
            foreach (var (cx, cy) in new[] { (6f, 6f), (18f, 6f), (6f, 18f) })
            {
                RR(g, p, r, cx - 4, cy - 4, 8, 8, 1.2f);
                FillRR(g, c, r, cx - 1.6f, cy - 1.6f, 3.2f, 3.2f, 0.5f);
            }
            // 右下数据区小方块阵列
            FillCircle(g, c, r, 14f, 14f, 1.1f);
            FillCircle(g, c, r, 18f, 14f, 1.1f);
            FillCircle(g, c, r, 14f, 18f, 1.1f);
            FillCircle(g, c, r, 18f, 18f, 1.1f);
        },
        // 两栏对照：左侧减号（删除）、右侧加号（新增），中间分隔线。
        ["diff"] = (g, r, w, c) =>
        {
            using var p = Stroke(w, c);
            Line(g, p, r, 12, 4, 12, 20);
            RR(g, p, r, 3.5f, 6f, 6.5f, 12f, 1);
            RR(g, p, r, 14f, 6f, 6.5f, 12f, 1);
            Line(g, p, r, 5.5f, 12, 9, 12);
            Line(g, p, r, 16.2f, 12, 19.7f, 12);
            Line(g, p, r, 18, 9.8f, 18, 14.2f);
        },
        // 文件 + 双向箭头：编码互转语义。
        ["encoding"] = (g, r, w, c) =>
        {
            using var p = Stroke(w, c);
            Poly(g, p, r, 5, 3, 14, 3, 18, 7, 18, 10);
            Poly(g, p, r, 14, 3, 14, 7, 18, 7);
            Arrow(g, p, r, 6, 13, 18, 13, 2.6f);
            Arrow(g, p, r, 18, 18, 6, 18, 2.6f);
        },
        // 图片 + 右箭头：格式转换语义（源图换成另一种封装）。
        ["image-format"] = (g, r, w, c) =>
        {
            using var p = Stroke(w, c);
            RR(g, p, r, 3f, 5f, 12f, 12f, 1.6f);
            Circle(g, p, r, 6.2f, 8.4f, 1.2f);
            Poly(g, p, r, 4.5f, 15f, 8f, 11.2f, 10.5f, 13.6f);
            Arrow(g, p, r, 16f, 11f, 21f, 11f, 2.4f);
        },
        // 图片 + 两侧向内箭头 + 缩小 glyph：批量压缩语义。
        ["image-compress"] = (g, r, w, c) =>
        {
            using var p = Stroke(w, c);
            RR(g, p, r, 8f, 3.5f, 10f, 10f, 1.6f);
            Circle(g, p, r, 11f, 6.6f, 1f);
            Poly(g, p, r, 9.5f, 12f, 12.5f, 9.2f, 15f, 11.4f);
            Line(g, p, r, 5.5f, 16.5f, 5.5f, 20.5f);
            Line(g, p, r, 3.8f, 18.5f, 7.2f, 18.5f);
            Line(g, p, r, 20.5f, 16.5f, 20.5f, 20.5f);
            Line(g, p, r, 18.8f, 18.5f, 22.2f, 18.5f);
        },
        // 文件夹 + 三根高低不同的柱子：大小分析语义。
        ["folder-size"] = (g, r, w, c) =>
        {
            using var p = Stroke(w, c);
            Poly(g, p, r, 3.5f, 8f, 3.5f, 5f, 9f, 5f, 11f, 7.4f, 20.5f, 7.4f, 20.5f, 20f, 3.5f, 20f);
            FillRR(g, c, r, 7f, 14f, 2.6f, 4f, 0.5f);
            FillRR(g, c, r, 11.6f, 10.8f, 2.6f, 7.2f, 0.5f);
            FillRR(g, c, r, 16.2f, 12.4f, 2.6f, 5.6f, 0.5f);
        },

        ["ping"] = (g, r, w, c) =>
        {
            using var p = Stroke(w, c);
            FillCircle(g, c, r, 5.5f, 18.5f, 1.7f);
            Arc(g, p, r, 5.5f, 18.5f, 5.2f, 310, 90);
            Arc(g, p, r, 5.5f, 18.5f, 9.4f, 305, 100);
            Arrow(g, p, r, 10, 14, 20, 4, 3.2f);
        },
        ["duplicate"] = (g, r, w, c) =>
        {
            using var p = Stroke(w, c);
            RR(g, p, r, 8f, 8f, 12f, 12f, 2f);
            RR(g, p, r, 4f, 4f, 12f, 12f, 2f);
            Line(g, p, r, 7.5f, 10f, 12.5f, 10f);
            Line(g, p, r, 10f, 7.5f, 10f, 12.5f);
        },
        ["stats"] = (g, r, w, c) =>
        {
            using var p = Stroke(w, c);
            Line(g, p, r, 4, 19.5f, 20, 19.5f);
            Line(g, p, r, 4, 19.5f, 4, 4.5f);
            RR(g, p, r, 7f, 12f, 3f, 7.5f, 0.8f);
            RR(g, p, r, 12f, 8f, 3f, 11.5f, 0.8f);
            RR(g, p, r, 17f, 14f, 3f, 5.5f, 0.8f);
        },
        ["uuid"] = (g, r, w, c) =>
        {
            using var p = Stroke(w, c);
            RR(g, p, r, 3f, 8.5f, 18f, 7f, 3.5f);
            Line(g, p, r, 8.5f, 8.5f, 8.5f, 15.5f);
            Line(g, p, r, 12f, 8.5f, 12f, 15.5f);
            Line(g, p, r, 15.5f, 8.5f, 15.5f, 15.5f);
        },
        ["sparkle"] = (g, r, w, c) =>

        {
            using var p = Stroke(w, c);
            Star(g, p, r, 11, 11, 7.4f, 2.4f, 4, true);
            Star(g, p, r, 18.6f, 18.2f, 3.6f, 1.1f, 4, true);
        },

        // 分类图标
        ["system"] = (g, r, w, c) =>
        {
            using var p = Stroke(w, c);
            RR(g, p, r, 3, 5, 18, 12, 2.2f);
            Line(g, p, r, 9, 20, 15, 20);
            Line(g, p, r, 12, 17, 12, 20);
        },
        ["folder"] = (g, r, w, c) =>
        {
            using var p = Stroke(w, c);
            Poly(g, p, r, 3.5f, 19, 3.5f, 7, 9.5f, 7, 11.5f, 9.4f, 20.5f, 9.4f, 20.5f, 19);
        },
        ["text"] = (g, r, w, c) =>
        {
            using var p = Stroke(w, c);
            RR(g, p, r, 4.5f, 3, 15, 18, 2.2f);
            for (var i = 0; i < 4; i++) Line(g, p, r, 8, 8 + i * 3.2f, 16, 8 + i * 3.2f);
        },
        ["network"] = (g, r, w, c) =>
        {
            using var p = Stroke(w, c);
            Circle(g, p, r, 12, 12, 8.6f);
            g.DrawEllipse(p, R(r, 4.6f, 12, 14.8f, 17.2f));
            Line(g, p, r, 3.6f, 9, 20.4f, 9);
            Line(g, p, r, 3.6f, 15, 20.4f, 15);
        },
        ["code"] = (g, r, w, c) =>
        {
            using var p = Stroke(w, c);
            Poly(g, p, r, 8.4f, 6.5f, 3.5f, 12, 8.4f, 17.5f);
            Poly(g, p, r, 15.6f, 6.5f, 20.5f, 12, 15.6f, 17.5f);
            Line(g, p, r, 13.6f, 4.5f, 10.4f, 19.5f);
        },
        ["media"] = (g, r, w, c) =>
        {
            using var p = Stroke(w, c);
            RR(g, p, r, 3, 4.5f, 18, 15, 2.4f);
            Poly(g, p, r, 10, 8.5f, 10, 15.5f, 15.5f, 12);
        },

        // 界面图标
        ["search"] = (g, r, w, c) =>
        {
            using var p = Stroke(w, c);
            Circle(g, p, r, 10.5f, 10.5f, 6.2f);
            Line(g, p, r, 15.2f, 15.2f, 20, 20);
        },
        ["sun"] = (g, r, w, c) =>
        {
            using var p = Stroke(w, c);
            Circle(g, p, r, 12, 12, 4.4f);
            for (var i = 0; i < 8; i++)
            {
                double a = i * Math.PI / 4;
                float dx = (float)Math.Cos(a), dy = (float)Math.Sin(a);
                Line(g, p, r, 12 + 7 * dx, 12 + 7 * dy, 12 + 9.6f * dx, 12 + 9.6f * dy);
            }
        },
        ["moon"] = (g, r, w, c) =>
        {
            using var p = Stroke(w, c);
            using var path = new GraphicsPath();
            path.AddBezier(
                P(r, 15.5f, 3.4f), P(r, 8.5f, 4.6f), P(r, 4, 10), P(r, 8.6f, 18.4f));
            path.AddBezier(
                P(r, 8.6f, 18.4f), P(r, 13, 22.4f), P(r, 20.4f, 19.6f), P(r, 19.4f, 12.4f));
            path.AddBezier(
                P(r, 19.4f, 12.4f), P(r, 17.4f, 13.6f), P(r, 15.4f, 12.6f), P(r, 15.5f, 3.4f));
            g.DrawPath(p, path);
        },
        ["star"] = (g, r, w, c) =>
        {
            using var p = Stroke(w, c);
            Star(g, p, r, 12, 12, 8.6f, 3.6f, 5, false);
        },
        ["star-filled"] = (g, r, w, c) =>
        {
            using var p = Stroke(w, c);
            Star(g, p, r, 12, 12, 8.6f, 3.6f, 5, true);
        },
        ["clock"] = (g, r, w, c) =>
        {
            using var p = Stroke(w, c);
            Circle(g, p, r, 12, 12, 8.4f);
            Poly(g, p, r, 12, 7, 12, 12, 15.6f, 14);
        },
        ["apps"] = (g, r, w, c) =>
        {
            using var p = Stroke(w * 0.9f, c);
            RR(g, p, r, 4, 4, 6.4f, 6.4f, 1.6f);
            RR(g, p, r, 13.6f, 4, 6.4f, 6.4f, 1.6f);
            RR(g, p, r, 4, 13.6f, 6.4f, 6.4f, 1.6f);
            RR(g, p, r, 13.6f, 13.6f, 6.4f, 6.4f, 1.6f);
        },
        ["gear"] = (g, r, w, c) =>
        {
            using var p = Stroke(w * 0.86f, c);
            var pts = new List<PointF>();
            for (var i = 0; i < 8; i++)
            {
                float t = i * 45f;
                foreach (var (deg, rad) in new[] { (-14f, 9.4f), (14f, 9.4f), (14f, 6.9f), (31f, 6.9f) })
                    pts.Add(Polar(r, 12, 12, t + deg, rad));
            }
            g.DrawPolygon(p, pts.ToArray());
            Circle(g, p, r, 12, 12, 3.1f);
        },
        ["back"] = (g, r, w, c) =>
        {
            using var p = Stroke(w, c);
            Poly(g, p, r, 14.5f, 5.5f, 7, 12, 14.5f, 18.5f);
            Line(g, p, r, 7.4f, 12, 19.5f, 12);
        },
        ["chevron-right"] = (g, r, w, c) =>
        {
            using var p = Stroke(w, c);
            Poly(g, p, r, 9, 5, 16, 12, 9, 19);
        },
        ["chevron-down"] = (g, r, w, c) =>
        {
            using var p = Stroke(w, c);
            Poly(g, p, r, 5, 9, 12, 16, 19, 9);
        },
        ["popout"] = (g, r, w, c) =>
        {
            using var p = Stroke(w, c);
            Poly(g, p, r, 13, 4.5f, 4.5f, 4.5f, 4.5f, 19.5f, 19.5f, 19.5f, 19.5f, 11);
            Arrow(g, p, r, 11, 13, 20, 4, 3.4f);
        },
        ["sort"] = (g, r, w, c) =>
        {
            using var p = Stroke(w, c);
            Arrow(g, p, r, 8, 19, 8, 5, 3.2f);
            Arrow(g, p, r, 16, 5, 16, 19, 3.2f);
        },
        ["plus"] = (g, r, w, c) =>
        {
            using var p = Stroke(w, c);
            Line(g, p, r, 12, 5, 12, 19);
            Line(g, p, r, 5, 12, 19, 12);
        },
        ["close"] = (g, r, w, c) =>
        {
            using var p = Stroke(w, c);
            Line(g, p, r, 6, 6, 18, 18);
            Line(g, p, r, 18, 6, 6, 18);
        },
        ["minimize"] = (g, r, w, c) =>
        {
            using var p = Stroke(w, c);
            Line(g, p, r, 5.5f, 12, 18.5f, 12);
        },
        ["maximize"] = (g, r, w, c) =>
        {
            using var p = Stroke(w * 0.9f, c);
            RR(g, p, r, 5.5f, 5.5f, 13, 13, 1.6f);
        },
        ["restore"] = (g, r, w, c) =>
        {
            using var p = Stroke(w * 0.9f, c);
            RR(g, p, r, 4, 8, 12, 12, 1.6f);
            Poly(g, p, r, 8, 8, 8, 4, 20, 4, 20, 16, 16, 16);
        },
        ["check"] = (g, r, w, c) =>
        {
            using var p = Stroke(w * 1.1f, c);
            Poly(g, p, r, 4.5f, 12.5f, 9.5f, 17.5f, 19.5f, 6.5f);
        },
        ["warning"] = (g, r, w, c) =>
        {
            using var p = Stroke(w, c);
            ClosedPoly(g, p, r, 12, 3.6f, 21.2f, 19.6f, 2.8f, 19.6f);
            Line(g, p, r, 12, 9.5f, 12, 14);
            FillCircle(g, c, r, 12, 17, 0.95f);
        },
        ["error"] = (g, r, w, c) =>
        {
            using var p = Stroke(w, c);
            Circle(g, p, r, 12, 12, 8.4f);
            Line(g, p, r, 8.8f, 8.8f, 15.2f, 15.2f);
            Line(g, p, r, 15.2f, 8.8f, 8.8f, 15.2f);
        },
        ["info"] = (g, r, w, c) =>
        {
            using var p = Stroke(w, c);
            Circle(g, p, r, 12, 12, 8.4f);
            Line(g, p, r, 12, 11, 12, 16.6f);
            FillCircle(g, c, r, 12, 7.8f, 1.05f);
        },
        ["copy"] = (g, r, w, c) =>
        {
            using var p = Stroke(w, c);
            RR(g, p, r, 8.5f, 8.5f, 11.5f, 11.5f, 2);
            Poly(g, p, r, 15.5f, 5.5f, 5.5f, 5.5f, 5.5f, 15.5f);
        },
        ["trash"] = (g, r, w, c) =>
        {
            using var p = Stroke(w, c);
            Poly(g, p, r, 4, 7.5f, 20, 7.5f);
            Poly(g, p, r, 6.5f, 7.5f, 7.6f, 20.5f, 16.4f, 20.5f, 17.5f, 7.5f);
            Line(g, p, r, 9.5f, 4.5f, 14.5f, 4.5f);
            Line(g, p, r, 10.5f, 11, 10.9f, 17);
            Line(g, p, r, 13.5f, 11, 13.1f, 17);
        },
        ["refresh"] = (g, r, w, c) =>
        {
            using var p = Stroke(w, c);
            Arc(g, p, r, 12, 12, 8.2f, -55, 250);
            var tip = P(r, 12 + 8.2f * (float)Math.Cos(-55 * Math.PI / 180),
                              12 + 8.2f * (float)Math.Sin(-55 * Math.PI / 180));
            using var q = Stroke(w, c);
            g.DrawLine(q, tip, P(r, 20.6f, 8.4f));
            g.DrawLine(q, tip, P(r, 16.4f, 4.6f));
        },
        ["download"] = (g, r, w, c) =>
        {
            using var p = Stroke(w, c);
            Arrow(g, p, r, 12, 4, 12, 15, 4f);
            Poly(g, p, r, 5, 16, 5, 20, 19, 20, 19, 16);
        },
        ["file"] = (g, r, w, c) =>
        {
            using var p = Stroke(w, c);
            Poly(g, p, r, 15, 3.5f, 6, 3.5f, 6, 20.5f, 18, 20.5f, 18, 7);
            Line(g, p, r, 13.4f, 3.6f, 13.4f, 7.4f);
            Line(g, p, r, 13.4f, 7.4f, 17.8f, 7.4f);
        },
        ["pin"] = (g, r, w, c) =>
        {
            using var p = Stroke(w, c);
            Poly(g, p, r, 7, 10.5f, 17, 10.5f);
            Poly(g, p, r, 8.6f, 10.5f, 9.2f, 4, 14.8f, 4, 15.4f, 10.5f);
            Line(g, p, r, 12, 10.5f, 12, 20.5f);
        },
        ["bolt"] = (g, r, w, c) =>
        {
            using var p = Stroke(w, c);
            ClosedPoly(g, p, r, 13.4f, 2.6f, 5.6f, 13.6f, 10.4f, 13.6f, 9.4f, 21.4f, 18.4f, 9.6f, 13f, 9.6f);
        },
        ["history"] = (g, r, w, c) =>
        {
            using var p = Stroke(w, c);
            Arc(g, p, r, 12, 12.6f, 8.2f, 30, 300);
            Poly(g, p, r, 3.4f, 8.6f, 3.6f, 13.4f, 8.4f, 13);
            Poly(g, p, r, 12, 8, 12, 13, 16, 15);
        },
        ["panel-left"] = (g, r, w, c) =>
        {
            using var p = Stroke(w * 0.9f, c);
            RR(g, p, r, 3.5f, 4.5f, 17, 15, 2.4f);
            Line(g, p, r, 10, 4.5f, 10, 19.5f);
        },
        ["lock"] = (g, r, w, c) =>
        {
            using var p = Stroke(w, c);
            RR(g, p, r, 5, 10.5f, 14, 10, 2.4f);
            Arc(g, p, r, 12, 10.5f, 4.4f, 180, 180);
            Line(g, p, r, 12, 14.5f, 12, 17);
        },
        ["shield"] = (g, r, w, c) =>
        {
            using var p = Stroke(w, c);
            Poly(g, p, r, 4.5f, 6, 12, 3.2f, 19.5f, 6, 19.5f, 12.5f, 12, 20.8f, 4.5f, 12.5f, 4.5f, 6);
            Poly(g, p, r, 8.8f, 11.8f, 11.4f, 14.4f, 15.6f, 9.4f);
        },
        ["heart"] = (g, r, w, c) =>
        {
            using var p = Stroke(w, c);
            g.DrawArc(p, R(r, 4.2f, 4.6f, 6.6f, 6.6f), 180, 200);
            g.DrawArc(p, R(r, 13.2f, 4.6f, 6.6f, 6.6f), -20, 200);
            Line(g, p, r, 4.6f, 9.6f, 12, 19.6f);
            Line(g, p, r, 19.4f, 9.6f, 12, 19.6f);
        },
        ["grid"] = (g, r, w, c) =>
        {
            using var p = Stroke(w * 0.9f, c);
            RR(g, p, r, 3.5f, 3.5f, 7.2f, 7.2f, 1.6f);
            RR(g, p, r, 13.3f, 3.5f, 7.2f, 7.2f, 1.6f);
            RR(g, p, r, 3.5f, 13.3f, 7.2f, 7.2f, 1.6f);
            RR(g, p, r, 13.3f, 13.3f, 7.2f, 7.2f, 1.6f);
        },
        ["list"] = (g, r, w, c) =>
        {
            using var p = Stroke(w, c);
            for (var i = 0; i < 3; i++)
            {
                float y = 6.5f + i * 5.5f;
                FillCircle(g, c, r, 5, y, 1.15f);
                Line(g, p, r, 9, y, 20, y);
            }
        },
        ["logo"] = (g, r, w, c) =>
        {
            // 工具箱：盖子上提手 + 箱体 + 中央卡扣
            using var p = Stroke(w * 1.05f, c);
            Poly(g, p, r, 8.6f, 6.5f, 8.6f, 4.2f, 15.4f, 4.2f, 15.4f, 6.5f);
            RR(g, p, r, 3.2f, 6.5f, 17.6f, 13.5f, 2.6f);
            Line(g, p, r, 3.2f, 12, 20.8f, 12);
            RR(g, p, r, 10.2f, 10.2f, 3.6f, 3.6f, 1.2f);
        },
    };

    private static PointF Polar(RectangleF r, float cx, float cy, float deg, float rad)
    {
        double a = deg * Math.PI / 180.0;
        return P(r, cx + rad * (float)Math.Cos(a), cy + rad * (float)Math.Sin(a));
    }

    /// <summary>是否存在该图标名；未注册的名字会回落到通用图标。</summary>
    public static bool Has(string name) => Registry.ContainsKey(name);

    /// <summary>
    /// 在 bounds 内居中绘制图标。stroke 省略时按图标尺寸自动取 1.55/16 比例，
    /// 保证小图标线条不断、大图标线条不粗。
    /// </summary>
    public static void Draw(Graphics g, string name, RectangleF bounds, Color color, float? stroke = null)
    {
        if (!Registry.TryGetValue(name, out var glyph)) glyph = Registry["info"];

        var mode = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var side = Math.Min(bounds.Width, bounds.Height);
        var square = new RectangleF(
            bounds.X + (bounds.Width - side) / 2f,
            bounds.Y + (bounds.Height - side) / 2f, side, side);
        var w = stroke.HasValue ? UiTheme.Sxf(stroke.Value) : Math.Max(UiTheme.Sxf(1.25), side * 0.096f);
        // 图标内容留 8% 内边距，避免描边被容器裁掉
        var inner = RectangleF.Inflate(square, -side * 0.08f, -side * 0.08f);
        try { glyph(g, inner, w, color); }
        finally { g.SmoothingMode = mode; }
    }
}
