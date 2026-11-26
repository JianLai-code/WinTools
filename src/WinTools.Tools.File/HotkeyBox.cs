using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using WinTools.Core;
using WinTools.Core.Ui;

namespace WinTools.Tools.Files;

/// <summary>
/// 快捷键录入框：获得焦点后直接按组合键录入，Delete / Backspace 清空，Esc 退出录入。
///
/// 录入期间必须暂停已注册的全局热键——否则用户按下当前生效的组合时，
/// WM_HOTKEY 会先把截图流程拉起来，录入框根本收不到按键。
/// 暂停/恢复由使用方挂在 RecordingStarted / RecordingEnded 上。
/// </summary>
internal sealed class HotkeyBox : Control
{
    private HotkeyGesture _gesture = HotkeyGesture.Empty;
    private string? _hint;
    private bool _hover;

    /// <summary>用户修改了组合。</summary>
    public event EventHandler? GestureChanged;

    /// <summary>进入录入态（需要暂停全局热键）。</summary>
    public event EventHandler? RecordingStarted;

    /// <summary>离开录入态（恢复全局热键）。</summary>
    public event EventHandler? RecordingEnded;

    public HotkeyBox()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                 ControlStyles.Selectable, true);
        Cursor = Cursors.Hand;
        TabStop = true;
        Size = UiTheme.Sz(168, 32);
        UiTheme.ThemeChanged += OnThemeChanged;
    }

    /// <summary>当前组合。internal 是有意为之：属性类型不是可设计序列化的基础类型，避免设计器报 WFO1000。</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal HotkeyGesture Gesture
    {
        get => _gesture;
        set
        {
            if (_gesture == value) return;
            _gesture = value;
            _hint = null;
            Invalidate();
        }
    }

    private void OnThemeChanged() => Invalidate();

    protected override void Dispose(bool disposing)
    {
        if (disposing) UiTheme.ThemeChanged -= OnThemeChanged;
        base.Dispose(disposing);
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        _hint = null;
        RecordingStarted?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        _hint = null;
        RecordingEnded?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    /// <summary>方向键、Tab 等都要当作普通按键收下来，不能被容器当成焦点导航吃掉。</summary>
    protected override bool IsInputKey(Keys keyData) => true;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // 全部按键都在这里消化：不能让它冒泡成界面快捷键（比如 Ctrl+S 保存）。
        e.Handled = e.SuppressKeyPress = true;

        var key = e.KeyCode;
        if (key == Keys.Escape)
        {
            _hint = null;
            Invalidate();
            Parent?.SelectNextControl(this, true, true, true, true);
            return;
        }
        if (key is Keys.Delete or Keys.Back)
        {
            Commit(HotkeyGesture.Empty);
            return;
        }
        if (!HotkeyGesture.IsBindableKey(key))
        {
            _hint = "这个键不能作为主键，换一个";
            Invalidate();
            return;
        }

        var gesture = HotkeyGesture.FromKeyData(e.KeyData);
        if (!gesture.HasModifier)
        {
            _hint = "至少加一个 Ctrl / Alt / Shift";
            Invalidate();
            return;
        }
        Commit(gesture);
    }

    private void Commit(HotkeyGesture gesture)
    {
        _gesture = gesture;
        _hint = null;
        Invalidate();
        GestureChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(UiTheme.Backdrop(Parent));

        var ring = new Rectangle(0, 0, Width - 1, Height - 1);
        var border = Focused ? UiTheme.Primary : _hover ? UiTheme.Stroke : UiTheme.Border;
        UiTheme.FillSurface(g, ring, UiTheme.RadiusMedium, UiTheme.FieldBg, border);
        if (Focused)
            UiTheme.DrawRoundedBorder(g, ring, UiTheme.RadiusMedium, UiTheme.Primary, 2);

        var text = _hint ?? (_gesture.IsEmpty ? "点击后按快捷键" : _gesture.ToString());
        var color = _hint is not null ? UiTheme.Warn : _gesture.IsEmpty ? UiTheme.Muted : UiTheme.Text;
        var area = new Rectangle(UiTheme.Sx(10), 0, Math.Max(0, Width - UiTheme.Sx(20)), Height);
        TextRenderer.DrawText(g, text, UiTheme.Body, area, color,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
            TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
    }
}
