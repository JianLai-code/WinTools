using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace WinTools.Core.Ui;

/// <summary>
/// Windows 11 风格自绘按钮：圆角表面 + 语义变体 + 悬停/按下/键盘聚焦三态反馈，
/// 支持在文字左侧绘制矢量图标。继承 Button，因此可直接替换所有 UiTheme.Button 调用点。
/// </summary>
public class FluentButton : Button
{
    private bool _hover, _pressed, _focusRing;
    // 焦点环只该由键盘导航触发：鼠标点击后 WinForms 也会 GotFocus，
    // 若据此画环，浅色主题下的深灰描边就会像没擦干净的黑框留在按钮上。
    private static bool _keyboardInput;
    private string? _iconName;
    private UiTheme.ButtonKind _variant = UiTheme.ButtonKind.Primary;
    private int _radius = UiTheme.RadiusMedium;

    public FluentButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Font = UiTheme.Body;
        Size = UiTheme.Sz(88, 32);
        Cursor = Cursors.Hand;
        UiTheme.ThemeChanged += OnThemeChanged;
    }

    /// <summary>
    /// 只填父容器底色，不用 BackColor=Transparent：WinForms 的透明背景会沿父链回拷
    /// 祖先内容，在停靠/自动尺寸容器里会把别的控件文字"印"进按钮里。
    /// </summary>
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        using var b = new SolidBrush(UiTheme.Backdrop(Parent));
        e.Graphics.FillRectangle(b, ClientRectangle);
    }

    [Browsable(true)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string? IconName
    {
        get => _iconName;
        set { _iconName = value; Invalidate(); }
    }

    [Browsable(true)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public UiTheme.ButtonKind Variant
    {
        get => _variant;
        set { _variant = value; Invalidate(); }
    }

    [Browsable(true)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Radius
    {
        get => _radius;
        set { _radius = value; Invalidate(); }
    }

    private int _iconGap = UiTheme.Sx(6);

    /// <summary>图标与文字间距（设计像素 6，取值时已按 DPI 缩放）。</summary>
    [Browsable(true)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int IconGap
    {
        get => _iconGap;
        set { _iconGap = value; Invalidate(); }
    }

    private Color? _hoverOverride;

    /// <summary>覆写悬停/按下填充色（窗口关闭按钮的红色悬停用）。</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color? HoverOverride
    {
        get => _hoverOverride;
        set { _hoverOverride = value; Invalidate(); }
    }

    private Color? _pressOverride;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color? PressOverride
    {
        get => _pressOverride;
        set { _pressOverride = value; Invalidate(); }
    }

    private Color? _foregroundOverride;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color? ForegroundOverride
    {
        get => _foregroundOverride;
        set { _foregroundOverride = value; Invalidate(); }
    }

    private void OnThemeChanged() => Invalidate();

    protected override void Dispose(bool disposing)
    {
        if (disposing) UiTheme.ThemeChanged -= OnThemeChanged;
        base.Dispose(disposing);
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        _keyboardInput = false;
        _focusRing = false;
        _pressed = true;
        Invalidate();
        base.OnMouseDown(e);
    }
    protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        _keyboardInput = true;
        if (Focused && !_focusRing) { _focusRing = true; Invalidate(); }
        base.OnKeyDown(e);
    }
    protected override void OnGotFocus(EventArgs e)
    {
        _focusRing = _keyboardInput;
        Invalidate();
        base.OnGotFocus(e);
    }
    protected override void OnLostFocus(EventArgs e) { _focusRing = false; Invalidate(); base.OnLostFocus(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        // 整块铺满父底色：双缓冲画布可能残留别的控件文字，只按 ClipRectangle 填会盖不住
        using (var bg = new SolidBrush(UiTheme.Backdrop(Parent)))
            g.FillRectangle(bg, ClientRectangle);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        // 底材：让父容器背景透出来（Ghost/Subtle 变体不填充表面）
        var whole = new Rectangle(0, 0, Width, Height);
        var (fill, stroke, fore, strokeStrongBottom) = Resolve();

        // 描边留出半个线宽（按 DPI 放大），避免边缘被裁
        var ins = UiTheme.Sx(1);
        var rect = new Rectangle(ins, ins, Math.Max(1, Width - ins * 2), Math.Max(1, Height - ins * 2));
        if (fill.HasValue)
            using (var b = new SolidBrush(fill.Value))
                using (var path = UiTheme.RoundedPath(rect, _radius))
                    g.FillPath(b, path);

        if (stroke.HasValue)
        {
            UiTheme.DrawRoundedBorder(g, rect, _radius, stroke.Value);
            if (strokeStrongBottom)
            {
                // Win11 标准按钮底边描边略重，形成"厚度"；坐标按线宽居中，与圆角描边压在同行像素上
                var w = UiTheme.PenW();
                using var bpen = new Pen(UiTheme.Stroke, w);
                g.DrawLine(bpen, rect.X + _radius, UiTheme.Edge(rect.Bottom, w),
                    rect.Right - _radius, UiTheme.Edge(rect.Bottom, w));
            }
        }

        if (_focusRing && Enabled && ShowFocusCues)
        {
            var ring = UiTheme.Sx(2);
            var inner = new Rectangle(rect.X + ring, rect.Y + ring,
                Math.Max(1, rect.Width - ring * 2), Math.Max(1, rect.Height - ring * 2));
            // 键盘焦点提示用主色细环，不再用深灰粗环——后者在浅底上像残留的黑框。
            UiTheme.DrawRoundedBorder(g, inner, Math.Max(UiTheme.Sx(2), _radius - ring),
                UiTheme.Primary);
        }

        DrawContent(g, whole, fore);
    }

    private void DrawContent(Graphics g, Rectangle whole, Color fore)
    {
        var textSize = string.IsNullOrEmpty(Text) ? Size.Empty : TextRenderer.MeasureText(g, Text, Font);
        int iconSize = _iconName is null ? 0 : Math.Clamp(Height - UiTheme.Sx(14), UiTheme.Sx(12), UiTheme.Sx(20));
        int gap = _iconName is not null && textSize.Width > 0 ? IconGap : 0;
        int total = iconSize + gap + textSize.Width;
        int x = (whole.Width - total) / 2;
        int cy = whole.Height / 2;

        if (_iconName is not null)
        {
            var tint = Enabled ? fore : UiTheme.Muted;
            IconFactory.Draw(g, _iconName, new RectangleF(x, cy - iconSize / 2f, iconSize, iconSize), tint);
            x += iconSize + gap;
        }
        if (textSize.Width > 0)
        {
            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter |
                        TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
            var tr = new Rectangle(x, 0, textSize.Width, whole.Height);
            TextRenderer.DrawText(g, Text, Font, tr, Enabled ? fore : UiTheme.Muted, flags);
        }
    }

    /// <summary>按变体与状态解析填充/描边/文字色，再套用窗口按钮类的覆写。</summary>
    private (Color? Fill, Color? Stroke, Color Fore, bool BottomStroke) Resolve()
    {
        var r = ResolveVariant();
        Color? fill = r.Fill;
        var fore = r.Fore;
        if (_hover && HoverOverride.HasValue) fill = HoverOverride;
        if (_pressed && PressOverride.HasValue) fill = PressOverride;
        if ((_hover || _pressed) && ForegroundOverride.HasValue) fore = ForegroundOverride.Value;
        return (fill, r.Stroke, fore, r.BottomStroke);
    }

    private (Color? Fill, Color? Stroke, Color Fore, bool BottomStroke) ResolveVariant()
    {
        bool active = _hover || _pressed;
        return _variant switch
        {
            UiTheme.ButtonKind.Primary => (
                _pressed ? UiTheme.PrimaryPress : _hover ? UiTheme.PrimaryHover : UiTheme.Primary,
                null, Color.White, false),

            UiTheme.ButtonKind.Danger => (
                _pressed ? UiTheme.Error : _hover ? ControlPaint.Light(UiTheme.Error, 0.12f) : UiTheme.Error,
                null, Color.White, false),

            UiTheme.ButtonKind.AccentSoft => (
                _pressed ? UiTheme.Border : active ? UiTheme.ActiveBg : UiTheme.PrimarySoft,
                null, UiTheme.IsDark ? UiTheme.PrimaryHover : UiTheme.Primary, false),

            UiTheme.ButtonKind.Ghost => (
                _pressed ? UiTheme.Border : _hover ? UiTheme.Hover : (Color?)null,
                null, UiTheme.Muted, false),

            UiTheme.ButtonKind.Subtle => (
                _pressed ? UiTheme.Border : _hover ? UiTheme.Hover : (Color?)null,
                null, UiTheme.TextSecondary, false),

            _ => ( // Secondary / 标准按钮
                _pressed ? UiTheme.ButtonPress : _hover ? UiTheme.ButtonHover : UiTheme.ButtonBg,
                _hover ? UiTheme.Stroke : UiTheme.Border,
                UiTheme.Text, true),
        };
    }
}
