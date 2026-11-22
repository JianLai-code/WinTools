using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace WinTools.Core.Ui;

/// <summary>
/// 圆角小徽章：快捷键、分类名等短文本。自绘填充与描边，尺寸随文字自动伸展，
/// 高度锁定 20px，可直接放进 FlowLayoutPanel 参与流式布局。
/// </summary>
public sealed class Chip : Label
{
    private Color _fill = UiTheme.Layer;
    private Color? _stroke;
    private bool _fillCustomized;

    public Chip()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer, true);
        AutoSize = true;
        Font = UiTheme.Caption;
        ForeColor = UiTheme.TextSecondary;
        Padding = UiTheme.Pad(8, 0, 8, 0);
        Radius = UiTheme.RadiusSmall;
        UiTheme.ThemeChanged += OnThemeChanged;
    }

    private void OnThemeChanged()
    {
        // M18：仅刷新未被调用方显式指定的默认填充色，保留自定义徽章色。
        // ForeColor 由宿主（MainForm.OnThemeChanged）对常驻 Chip 显式更新，此处不重复处理。
        if (!_fillCustomized) _fill = UiTheme.Layer;
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) UiTheme.ThemeChanged -= OnThemeChanged;
        base.Dispose(disposing);
    }

    /// <summary>同 FluentButton：用父容器底色铺底，避免透明背景回拷祖先内容。</summary>
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        using var b = new SolidBrush(UiTheme.Backdrop(Parent));
        e.Graphics.FillRectangle(b, ClientRectangle);
    }

    private int _radius;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Radius
    {
        get => _radius;
        set { _radius = value; Invalidate(); }
    }

    /// <summary>徽章底色。</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color Fill
    {
        get => _fill;
        set { _fill = value; _fillCustomized = true; Invalidate(); }
    }

    /// <summary>描边色；null 表示无边。</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color? Stroke
    {
        get => _stroke;
        set { _stroke = value; Invalidate(); }
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var baseSize = base.GetPreferredSize(proposedSize);
        return new Size(baseSize.Width, Math.Max(baseSize.Height, UiTheme.Sx(20)));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var path = UiTheme.RoundedPath(rect, Radius))
        using (var b = new SolidBrush(_fill))
            g.FillPath(b, path);

        if (_stroke.HasValue)
            UiTheme.DrawRoundedBorder(g, rect, Radius, _stroke.Value);

        TextRenderer.DrawText(g, Text, Font,
            new Rectangle(Padding.Left, 0, Math.Max(0, Width - Padding.Horizontal), Height),
            ForeColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }
}
