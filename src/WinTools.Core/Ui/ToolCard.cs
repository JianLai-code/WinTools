using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace WinTools.Core.Ui;

/// <summary>
/// 实心工具卡片。两种形态：
/// Large —— 分类网格与搜索结果：染色图标块 + 名称 + 两行描述 + 快捷键徽章 + 收藏星标；
/// Compact —— 首页快速入口：图标块 + 名称 + 右对齐快捷键的单行卡。
///
/// 整张卡自绘（含文字），保证切主题、悬停、按下三种状态之间不会出现子控件残留的旧色。
/// 宽度由外部网格决定，布局全部按实际尺寸计算，因此同列的卡片天然对齐。
/// 控件尺寸 = 表面尺寸 + 间距余量，网格排布请用 LargeCardSize / CompactCardSize 作为最小单元。
/// </summary>
public sealed class ToolCard : Panel
{
    /// <summary>卡片间距余量：左右 / 上 / 下（设计像素，取值时已按 DPI 缩放）。</summary>
    public static int InsetX => UiTheme.Sx(10);
    public static int InsetTop => UiTheme.Sx(8);
    public static int InsetBottom => UiTheme.Sx(12);

    /// <summary>表面高度（不含余量）。</summary>
    public static int LargeSurfaceH => UiTheme.Sx(134);
    public static int CompactSurfaceH => UiTheme.Sx(54);

    /// <summary>网格单元最小宽度：宽于此则拉伸填满列，窄于此则减少列数。</summary>
    public static Size LargeCardSize => UiTheme.Sz(256, 154);
    public static Size CompactCardSize => UiTheme.Sz(256, 74);

    private readonly ITool _tool;
    private readonly ToolMeta.Meta _meta;
    private readonly bool _compact;
    private Color _accent;
    private readonly Action<ITool> _onClick;
    private readonly Action<ITool, bool> _onToggleFavorite;
    private bool _favorite;
    private bool _hover;
    private bool _down;
    private Rectangle _starRect;

    public ITool Tool => _tool;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Favorite => _favorite;

    public ToolCard(ITool tool, bool compact, bool favorite,
        Action<ITool> onClick, Action<ITool, bool> onToggleFavorite)
    {
        _tool = tool;
        _meta = ToolMeta.For(tool);
        _compact = compact;
        _favorite = favorite;
        _accent = UiTheme.Accent(_meta.AccentIndex);
        _onClick = onClick;
        _onToggleFavorite = onToggleFavorite;

        Cursor = Cursors.Hand;
        Margin = Padding.Empty;
        TabStop = false;
        Size = compact ? CompactCardSize : LargeCardSize;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer, true);

        UiTheme.ThemeChanged += OnThemeChanged;
    }

    private void OnThemeChanged()
    {
        // M18：强调色是构造时快照，若此卡片未随页面重建，切主题后要重算色相。
        _accent = UiTheme.Accent(_meta.AccentIndex);
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) UiTheme.ThemeChanged -= OnThemeChanged;
        base.Dispose(disposing);
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = _down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        _down = false;
        Invalidate();
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left) return;
        if (!_compact && _starRect.Contains(e.Location))
        {
            _favorite = !_favorite;
            Invalidate();
            _onToggleFavorite(_tool, _favorite);
            return;
        }
        _onClick(_tool);
    }

    /// <summary>自绘控件不铺父底色：整张画布每帧重画，透明会把双缓冲残留露出来。</summary>
    protected override void OnPaintBackground(PaintEventArgs e) { }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        // 必须从父容器取底色：自身 BackColor 是 WinForms 默认系统灰，
        // 用它清屏会把卡片四周余量涂成灰块（深色主题下就是一圈突兀的"黑框"）。
        g.Clear(UiTheme.Backdrop(Parent));

        var rect = new Rectangle(InsetX, InsetTop,
            Math.Max(UiTheme.Sx(40), Width - InsetX * 2),
            Math.Max(UiTheme.Sx(32), Height - InsetTop - InsetBottom));
        var fill = _down ? UiTheme.Blend(UiTheme.Shadow, UiTheme.Panel, 0.12f) : UiTheme.Panel;
        UiTheme.FillSurface(g, rect, UiTheme.RadiusCard, fill,
            _hover || _down ? UiTheme.Stroke : UiTheme.Border);

        if (_compact) PaintCompact(g, rect);
        else PaintLarge(g, rect);
    }

    private void PaintCompact(Graphics g, Rectangle rect)
    {
        int pad = UiTheme.Sx(12), tile = UiTheme.Sx(30);
        var tileRect = new Rectangle(rect.X + pad, rect.Y + (rect.Height - tile) / 2, tile, tile);
        UiTheme.IconTile(g, tileRect, _accent, _meta.Icon, 0.58f);

        int scW = UiTheme.Sx(52);
        // 名称右界按快捷键实际占位算，留 8 设计像素间隙；固定 58 会让省略号压到快捷键上
        int rightReserve = string.IsNullOrEmpty(_meta.Shortcut) ? pad : pad + scW + UiTheme.Sx(8);
        int nameX = tileRect.Right + UiTheme.Sx(11);
        var nameRect = new Rectangle(nameX, rect.Y, Math.Max(UiTheme.Sx(30), rect.Right - nameX - rightReserve), rect.Height);
        TextRenderer.DrawText(g, _tool.Name, UiTheme.Bold, nameRect, UiTheme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

        if (string.IsNullOrEmpty(_meta.Shortcut)) return;
        var sc = new Rectangle(rect.Right - pad - scW, rect.Y, scW, rect.Height);
        TextRenderer.DrawText(g, _meta.Shortcut, UiTheme.Caption, sc, UiTheme.Muted,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    private void PaintLarge(Graphics g, Rectangle rect)
    {
        int pad = UiTheme.Sx(14), tile = UiTheme.Sx(34);
        var tileRect = new Rectangle(rect.X + pad, rect.Y + pad, tile, tile);
        UiTheme.IconTile(g, tileRect, _accent, _meta.Icon);

        int star = UiTheme.Sx(22);
        _starRect = new Rectangle(rect.Right - pad - star, rect.Y + pad, star, star);
        // _starRect 已经是物理坐标，内缩只能缩放后再相加；直接塞进 Rcf 会被二次乘 S，星标跑到卡外
        float starInset = UiTheme.Sxf(2);
        IconFactory.Draw(g, _favorite ? "star-filled" : "star",
            new RectangleF(_starRect.X + starInset, _starRect.Y + starInset,
                star - starInset * 2, star - starInset * 2),
            _favorite ? UiTheme.Warn : _hover ? UiTheme.TextSecondary : UiTheme.Muted);

        int nameX = tileRect.Right + UiTheme.Sx(12);
        int nameH = UiTheme.Sx(22);
        // 名称与图标块共用一条水平中线：写死 +2 在 200% 下文字明显浮高
        var nameRect = new Rectangle(nameX, tileRect.Y + (tile - nameH) / 2,
            _starRect.X - nameX - UiTheme.Sx(6), nameH);
        TextRenderer.DrawText(g, _tool.Name, UiTheme.CardTitle, nameRect, UiTheme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

        var descRect = new Rectangle(rect.X + pad, tileRect.Bottom + UiTheme.Sx(10),
            rect.Width - pad * 2, UiTheme.Sx(34));
        TextRenderer.DrawText(g, _meta.Description, UiTheme.Small, descRect, UiTheme.Muted,
            TextFormatFlags.Left | TextFormatFlags.Top |
            TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);

        var badge = string.IsNullOrEmpty(_meta.Shortcut) ? _meta.Group : _meta.Shortcut;
        PaintChip(g, badge, new Rectangle(rect.X + pad, rect.Bottom - pad - UiTheme.Sx(20), 0, UiTheme.Sx(20)));
    }

    /// <summary>徽章比卡片表面亮一档，用实心 Layer 色，不靠透明度叠色。</summary>
    private static void PaintChip(Graphics g, string text, Rectangle origin)
    {
        var size = TextRenderer.MeasureText(g, text, UiTheme.Caption, new Size(0, origin.Height),
            TextFormatFlags.NoPadding);
        var rect = new Rectangle(origin.X, origin.Y, size.Width + UiTheme.Sx(16), origin.Height);
        int radius = UiTheme.RadiusCard;
        using (var path = UiTheme.RoundedPath(rect, radius))
        using (var b = new SolidBrush(UiTheme.Layer))
            g.FillPath(b, path);
        UiTheme.DrawRoundedBorder(g, rect, radius, UiTheme.Border);
        TextRenderer.DrawText(g, text, UiTheme.Caption, rect, UiTheme.TextSecondary,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }
}
