using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace WinTools.Core.Ui;

/// <summary>
/// 页面级滚动容器：用自绘细滚动条替代 WinForms 原生粗灰滚动条。
/// 内容加到 Canvas（绝对定位或流式均可），本控件负责测量内容高度、
/// 响应滚轮/拖拽/轨道点击，并在右侧 14px 槽位画一条 6px 宽的圆角滑块。
/// </summary>
public sealed class FluentScrollPanel : Panel
{
    // 以下均为设计像素，落到坐标前用 UiTheme.Sx 折算
    private const int BarSlot = 14;
    private const int ThumbW = 6;
    private const int ThumbMin = 44;
    private const int TrackInset = 8;
    private const int WheelStep = 72;

    private readonly ScrollCanvas _canvas = new();
    private int _offset;
    private int _contentHeight;
    private bool _overflow;
    private bool _hoverThumb;
    private bool _dragThumb;
    private Rectangle _thumbRect;
    private Rectangle _trackRect;
    private int _dragAnchorY;
    private int _dragAnchorOffset;
    // M17：已挂滚轮/ControlAdded 的控件集合，防止动态嵌套时重复挂接。
    private readonly HashSet<Control> _wheelHooked = new();

    /// <summary>内容承载面板：仅用于把双缓冲样式设上，避免外部误用。</summary>
    private sealed class ScrollCanvas : Panel
    {
        public ScrollCanvas() =>
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    public FluentScrollPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw, true);
        BackColor = UiTheme.Bg;
        _canvas.BackColor = UiTheme.Bg;
        Controls.Add(_canvas);
        HookWheel(_canvas);
        UiTheme.ThemeChanged += OnThemeChanged;
    }

    private void OnThemeChanged()
    {
        // M18：容器底色也要跟随主题，否则切主题后滚动区残留旧色。
        BackColor = UiTheme.Bg;
        _canvas.BackColor = UiTheme.Bg;
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) UiTheme.ThemeChanged -= OnThemeChanged;
        base.Dispose(disposing);
    }

    /// <summary>内容承载区：把页面控件加到这里。</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Panel Canvas => _canvas;

    /// <summary>内容可用宽度（已扣除滚动条槽位），布局计算用它而不是 ClientSize.Width。</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int ContentWidth => Math.Max(UiTheme.Sx(80), _canvas.Width);

    private int ViewportHeight => Math.Max(1, ClientSize.Height);

    private static int TW => UiTheme.Sx(ThumbW);
    private static Padding CanvasPadding => UiTheme.Pad(0, 0, 0, 24);

    /// <summary>重新测量内容高度并夹紧滚动位置。宿主在重排后调用。</summary>
    public void RelayoutContent()
    {
        int w = Math.Max(UiTheme.Sx(80), ClientSize.Width - UiTheme.Sx(BarSlot));
        _canvas.Width = w;

        int need = CanvasPadding.Bottom;
        foreach (Control c in _canvas.Controls)
            need = Math.Max(need, c.Bottom + c.Margin.Bottom);
        _contentHeight = Math.Max(need, ViewportHeight);

        _overflow = _contentHeight > ViewportHeight;
        _offset = Math.Clamp(_offset, 0, Math.Max(0, _contentHeight - ViewportHeight));
        _canvas.SetBounds(0, -_offset, w, _contentHeight);
        ComputeBars();
        Invalidate();
    }

    private void ComputeBars()
    {
        int slot = UiTheme.Sx(BarSlot), inset = UiTheme.Sx(TrackInset);
        _trackRect = new Rectangle(Width - slot + (slot - TW) / 2, inset,
            TW, Math.Max(0, ClientSize.Height - inset * 2));
        if (!_overflow) { _thumbRect = Rectangle.Empty; return; }

        int range = Math.Max(1, _contentHeight - ViewportHeight);
        int h = Math.Max(UiTheme.Sx(ThumbMin), (int)((long)_trackRect.Height * ViewportHeight / _contentHeight));
        h = Math.Min(h, _trackRect.Height);
        int y = _trackRect.Top + (int)((long)(_trackRect.Height - h) * _offset / range);
        _thumbRect = new Rectangle(_trackRect.X, y, TW, h);
    }

    /// <summary>滚轮事件在子控件上也要能滚页，因此递归挂接；
    /// 自身可滚动的控件（文本框、表格）保持独立滚动，不劫持。</summary>
    private void HookWheel(Control root)
    {
        if (_wheelHooked.Contains(root)) return;
        _wheelHooked.Add(root);
        if (root is not (TextBoxBase or ListView or DataGridView or RichTextBox))
            root.MouseWheel += (_, e) => ScrollBy(-(e.Delta / 120) * UiTheme.Sx(WheelStep));
        // M17：后续动态加入的后代控件也要挂接——原实现只在加入 Canvas 那一刻挂当时的后代，
        // 先加空 Panel 再往里填子控件时，这些孙级控件滚轮不滚页。
        root.ControlAdded += OnDescendantAdded;
        foreach (Control c in root.Controls) HookWheel(c);
    }

    private void OnDescendantAdded(object? sender, ControlEventArgs e)
    {
        if (e.Control is not null) HookWheel(e.Control);
    }

    private void ScrollBy(int delta)
    {
        int max = Math.Max(0, _contentHeight - ViewportHeight);
        int next = Math.Clamp(_offset + delta, 0, max);
        if (next == _offset) return;
        _offset = next;
        _canvas.Top = -_offset;
        ComputeBars();
        Invalidate();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        ScrollBy(-(e.Delta / 120) * UiTheme.Sx(WheelStep));
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        RelayoutContent();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || !_overflow) return;
        if (_thumbRect.Contains(e.Location))
        {
            _dragThumb = true;
            _dragAnchorY = e.Y;
            _dragAnchorOffset = _offset;
            Capture = true;
            Invalidate();
        }
        else if (_trackRect.Contains(e.Location))
        {
            // 轨道点击：翻页
            ScrollBy(e.Y > _thumbRect.Bottom ? ViewportHeight / 2 : -ViewportHeight / 2);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        bool hover = _overflow && _thumbRect.Contains(e.Location);
        if (hover != _hoverThumb)
        {
            _hoverThumb = hover;
            Invalidate();
        }
        if (!_dragThumb) return;
        int range = Math.Max(1, _contentHeight - ViewportHeight);
        int trackFree = Math.Max(1, _trackRect.Height - _thumbRect.Height);
        int delta = (int)((long)(e.Y - _dragAnchorY) * range / trackFree);
        ScrollBy(_dragAnchorOffset + delta - _offset);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!_dragThumb) return;
        _dragThumb = false;
        Capture = false;
        Invalidate();
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        // 拖拽中切窗口/按 Esc 会让捕获被系统收走，MouseUp 不一定再来；
        // 不在这里复位，滑块会一直处于"幽灵拖拽"状态。
        if (!Capture && _dragThumb)
        {
            _dragThumb = false;
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (!_overflow) return;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using (var track = new SolidBrush(UiTheme.Border))
        using (var tp = UiTheme.RoundedPath(_trackRect, TW / 2f))
            g.FillPath(track, tp);

        var color = _dragThumb || _hoverThumb ? UiTheme.Stroke : UiTheme.Muted;
        using (var b = new SolidBrush(color))
        using (var p = UiTheme.RoundedPath(_thumbRect, TW / 2f))
            g.FillPath(b, p);
    }

    public void ScrollToTop() => ScrollBy(-_offset);
}
