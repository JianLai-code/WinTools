using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using WinTools.Core.Ui;

namespace WinTools.Tools.Files;

/// <summary>
/// 标注工具栏的纯图标按钮：选中态用绿色填充 + 白色图标（参考微信截图），
/// 有二级选项的按钮在右下角带一个小三角。自身不持有业务状态，
/// Selected / ArrowVisible 由 AnnotationOverlay 在切换工具时更新。
/// </summary>
internal sealed class IconToolButton : Control
{
    private bool _selected;
    private bool _arrowVisible;
    private bool _hover;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal string IconName { get; init; } = "info";
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Color IconColor { get; init; } = Color.FromArgb(52, 62, 72);

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool Selected
    {
        get => _selected;
        set { if (_selected == value) return; _selected = value; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool ArrowVisible
    {
        get => _arrowVisible;
        set { if (_arrowVisible == value) return; _arrowVisible = value; Invalidate(); }
    }

    /// <summary>悬停提示回调：由编辑窗口统一管理一个置顶提示浮层。</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Action<IconToolButton>? HoverEnter { get; init; }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Action<IconToolButton>? HoverLeave { get; init; }

    public IconToolButton(string iconName)
    {
        IconName = iconName;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint, true);
        Cursor = Cursors.Hand;
        // 微信截图工具栏的按钮约 30×30（100% 缩放），比之前的 44×40 收敛很多
        Size = UiTheme.Sz(30, 30);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hover = true; Invalidate();
        HoverEnter?.Invoke(this);
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = false; Invalidate();
        HoverLeave?.Invoke(this);
        base.OnMouseLeave(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Color.White);
        if (_selected)
        {
            UiTheme.FillSurface(g, ClientRectangle, UiTheme.RadiusMedium,
                Color.FromArgb(38, 174, 96), Color.FromArgb(60, 200, 118));
        }
        else if (_hover)
        {
            UiTheme.FillSurface(g, ClientRectangle, UiTheme.RadiusMedium,
                Color.FromArgb(236, 242, 248), Color.FromArgb(224, 232, 240));
        }

        // 图标固定 20×20 设计像素并居中；显式给细线宽，避免默认算法在小图标上把线画粗
        int side = UiTheme.Sx(20);
        var iconRect = new RectangleF((ClientSize.Width - side) / 2f, (ClientSize.Height - side) / 2f, side, side);
        IconFactory.Draw(g, IconName, iconRect, _selected ? Color.White : IconColor, 1.45f);

        // 不再绘制右下角的小三角：有二级选项的按钮保持图标本身即可，
        // 选项通过悬停提示文案（如「矩形（颜色 / 粗细）」）与点击浮出面板表达。
    }
}

/// <summary>
/// 工具栏悬停文字提示。编辑窗口本身是 TopMost，普通 ToolTip 会被它挡住，
/// 因此这里用一个同样置顶、但不抢焦点的小窗体来显示提示。
/// </summary>
internal sealed class HoverTip : Form
{
    private readonly Form _owner;
    private string _text = string.Empty;

    public HoverTip(Form owner)
    {
        _owner = owner;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        ControlBox = false;
        KeyPreview = false;
        BackColor = Color.FromArgb(38, 42, 48);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint, true);
    }

    protected override bool ShowWithoutActivation => true;

    protected override void OnClientSizeChanged(EventArgs e)
    {
        base.OnClientSizeChanged(e);
        // 窗体创建期（WmCreate）会先触发一次尺寸变更，此时 ClientSize 可能是 0×0；
        // 直接算圆角路径会让 GDI+ 的 AddArc 收到非法参数抛 ArgumentException。
        // UiTheme.Round 自带非正尺寸保护，并负责释放旧 Region。
        if (ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
        UiTheme.Round(this, UiTheme.Sx(6));
    }

    /// <summary>在 anchor 下方显示提示，并钳进当前屏幕工作区。</summary>
    public void ShowTip(Control anchor, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        _text = text;

        var font = UiTheme.FontAt(12);
        var measured = TextRenderer.MeasureText(text, font);
        var size = new Size(measured.Width + UiTheme.Sx(22), measured.Height + UiTheme.Sx(14));
        if (ClientSize != size) ClientSize = size;

        var p = anchor.PointToScreen(Point.Empty);
        int x = p.X + (anchor.Width - size.Width) / 2;
        int y = p.Y + anchor.Height + UiTheme.Sx(8);
        var work = Screen.FromPoint(p).WorkingArea;
        if (x < work.Left) x = work.Left;
        if (x + size.Width > work.Right) x = work.Right - size.Width;
        if (y + size.Height > work.Bottom) y = p.Y - size.Height - UiTheme.Sx(8);
        Location = new Point(x, y);

        if (!Visible) Show(_owner);
        else Refresh();
    }

    public void HideTip()
    {
        if (Visible) Hide();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        TextRenderer.DrawText(g, _text, UiTheme.FontAt(12), ClientRectangle, Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }
}

/// <summary>选项面板里的圆点按钮：颜色样本或马赛克块大小，选中时画外圈。</summary>
internal sealed class DotOption : Control
{
    private bool _selected;
    private bool _hover;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Color Fill { get; init; } = Color.Red;
    /// <summary>马赛克块大小；0 表示纯色样本（非马赛克选项）。</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal int BlockSize { get; init; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool Selected
    {
        get => _selected;
        set { if (_selected == value) return; _selected = value; Invalidate(); }
    }

    private readonly HoverTip? _tipHost;
    private readonly string _tip;

    public DotOption(Color fill, int blockSize = 0, HoverTip? tipHost = null, string tip = "")
    {
        Fill = fill;
        BlockSize = blockSize;
        _tipHost = tipHost;
        _tip = tip;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint, true);
        Cursor = Cursors.Hand;
        Size = UiTheme.Sz(22, 22);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hover = true; Invalidate();
        _tipHost?.ShowTip(this, _tip);
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = false; Invalidate();
        _tipHost?.HideTip();
        base.OnMouseLeave(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Color.White);
        var inset = _selected ? 3 : 4;
        var r = new Rectangle(inset, inset, ClientSize.Width - inset * 2, ClientSize.Height - inset * 2);

        if (BlockSize > 0)
        {
            using var brush = new SolidBrush(Color.FromArgb(170, 182, 196));
            g.FillRectangle(brush, r);
            int cell = Math.Max(3, r.Width / (BlockSize > 8 ? 6 : BlockSize > 4 ? 4 : 3));
            using var dark = new SolidBrush(Color.FromArgb(90, 104, 120));
            for (int y = r.Top; y < r.Bottom; y += cell)
                for (int x = r.Left; x < r.Right; x += cell)
                    g.FillRectangle(dark, x, y, Math.Max(1, cell - 2), Math.Max(1, cell - 2));
        }
        else
        {
            using var brush = new SolidBrush(Fill);
            g.FillEllipse(brush, r);
        }

        if (_selected || _hover)
        {
            using var pen = new Pen(_selected ? Color.FromArgb(38, 174, 96) : Color.FromArgb(150, 162, 176), 1.5f);
            g.DrawEllipse(pen, new Rectangle(1, 1, ClientSize.Width - 3, ClientSize.Height - 3));
        }
    }
}

/// <summary>表情选项：用内置矢量图案绘制，不依赖系统 emoji 字体。</summary>
internal sealed class EmojiOption : Control
{
    private bool _selected;
    private bool _hover;
    private readonly HoverTip? _tipHost;
    private readonly string _tip;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal string EmojiKey { get; init; } = "smile";

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool Selected
    {
        get => _selected;
        set { if (_selected == value) return; _selected = value; Invalidate(); }
    }

    public EmojiOption(string key, string tip, HoverTip? tipHost = null)
    {
        EmojiKey = key;
        _tip = tip;
        _tipHost = tipHost;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint, true);
        Cursor = Cursors.Hand;
        Size = UiTheme.Sz(30, 30);
        Margin = new Padding(UiTheme.Sx(2));
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hover = true; Invalidate();
        _tipHost?.ShowTip(this, _tip);
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = false; Invalidate();
        _tipHost?.HideTip();
        base.OnMouseLeave(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Color.White);
        if (_selected)
            UiTheme.FillSurface(g, ClientRectangle, UiTheme.RadiusSmall,
                Color.FromArgb(238, 248, 242), Color.FromArgb(38, 174, 96));
        else if (_hover)
            UiTheme.FillSurface(g, ClientRectangle, UiTheme.RadiusSmall,
                Color.FromArgb(240, 245, 250), Color.FromArgb(220, 230, 242));

        int pad = UiTheme.Sx(3);
        var r = new RectangleF(pad, pad, ClientSize.Width - pad * 2f, ClientSize.Height - pad * 2f);
        EmojiArt.Draw(g, EmojiKey, r);
    }
}

/// <summary>选项面板里的线条粗细按钮。</summary>
internal sealed class WidthOption : Control
{
    private bool _selected;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal int StrokeWidth { get; init; } = 3;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal bool Selected
    {
        get => _selected;
        set { if (_selected == value) return; _selected = value; Invalidate(); }
    }

    private readonly HoverTip? _tipHost;
    private readonly string _tip;

    public WidthOption(int strokeWidth, HoverTip? tipHost = null, string tip = "")
    {
        StrokeWidth = strokeWidth;
        _tipHost = tipHost;
        _tip = tip;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint, true);
        Cursor = Cursors.Hand;
        Size = UiTheme.Sz(24, 22);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _tipHost?.ShowTip(this, _tip);
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _tipHost?.HideTip();
        base.OnMouseLeave(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(_selected ? Color.FromArgb(238, 248, 242) : Color.White);
        using var pen = new Pen(_selected ? Color.FromArgb(38, 174, 96) : Color.FromArgb(52, 62, 72),
            Math.Max(1, StrokeWidth)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        int y = ClientSize.Height / 2;
        g.DrawLine(pen, 3, y, ClientSize.Width - 3, y);
        if (_selected)
        {
            using var border = new Pen(Color.FromArgb(38, 174, 96), 1f);
            g.DrawRectangle(border, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        }
    }
}

/// <summary>选项面板里的圆角文字按钮（用于"一键打码"）。</summary>
internal sealed class PopupActionButton : Control
{
    private bool _hover;
    private readonly HoverTip? _tipHost;
    private readonly string _tip;

    public PopupActionButton(string text, HoverTip? tipHost = null, string tip = "")
    {
        Text = text;
        _tipHost = tipHost;
        _tip = tip;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint, true);
        Cursor = Cursors.Hand;
        Size = UiTheme.Sz(84, 24);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hover = true; Invalidate();
        _tipHost?.ShowTip(this, _tip);
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = false; Invalidate();
        _tipHost?.HideTip();
        base.OnMouseLeave(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Color.White);
        using var pen = new Pen(Color.FromArgb(38, 174, 96), UiTheme.PenW(1));
        g.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        if (_hover)
        {
            using var hoverBrush = new SolidBrush(Color.FromArgb(236, 248, 240));
            g.FillRectangle(hoverBrush, 1, 1, ClientSize.Width - 2, ClientSize.Height - 2);
        }
        TextRenderer.DrawText(g, Text, UiTheme.FontAt(12), ClientRectangle, Color.FromArgb(38, 174, 96),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }
}

/// <summary>
/// 点击工具按钮后在下方浮出的二级选项面板。无边框、不抢焦点；
/// 内容由 AnnotationOverlay 按需填充，面板自身只负责定位与关闭。
/// </summary>
internal sealed class ToolOptionPopup : Form
{
    public ToolOptionPopup()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        ControlBox = false;
        KeyPreview = false;
        BackColor = Color.White;
        Padding = UiTheme.Pad(6, 6, 6, 6);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
    }

    /// <summary>把面板放在 anchor 正下方，并保持在屏幕工作区内。</summary>
    public void ShowUnder(Control anchor)
    {
        var p = anchor.PointToScreen(Point.Empty);
        int x = p.X;
        int y = p.Y + anchor.Height + UiTheme.Sx(4);
        var screen = Screen.FromPoint(p).WorkingArea;
        // 先显示一次以拿到 AutoSize 后的真实尺寸，再钳进屏幕
        Show();
        if (x + Width > screen.Right) x = Math.Max(screen.Left, screen.Right - Width);
        if (y + Height > screen.Bottom) y = Math.Max(screen.Top, p.Y - Height - UiTheme.Sx(4));
        Location = new Point(x, y);
        Refresh();
    }
}
