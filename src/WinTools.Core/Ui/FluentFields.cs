using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace WinTools.Core.Ui;

/// <summary>
/// uxtheme 子主题开关。原生滚动条、下拉箭头这类由系统绘制的部件不吃 BackColor/ForeColor，
/// 只能靠 DarkMode_Explorer 子主题切换，纯自绘的控件不需要调用。
/// </summary>
public static class NativeTheme
{
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hWnd, string? subApp, string? subIdList);

    public static void Apply(Control c)
    {
        if (!c.IsHandleCreated) return;
        SetWindowTheme(c.Handle, UiTheme.IsDark ? "DarkMode_Explorer" : "", null);
    }
}

/// <summary>
/// 圆角输入框。继承 TextBox，因此 Text / Clear() / Multiline 等既有调用点全部照旧可用。
/// 用 BorderStyle.None 去掉系统直角硬框，改为 Region 裁剪 + 自绘柔边；
/// 单行文本的水平内缩通过 EM_SETMARGINS 实现（Padding 对单行 EDIT 无效）。
/// </summary>
public class FluentTextBox : TextBox
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

    private const int EM_SETMARGINS = 0x00D3;
    private const int EC_LEFTMARGIN = 0x0001;
    private const int EC_RIGHTMARGIN = 0x0002;
    private const int EM_SETCUEBANNER = 0x1501;
    private static int TextInset => UiTheme.Sx(9);

    private bool _focused;
    private int _radius = UiTheme.RadiusMedium;
    // 复合输入框 FluentInput 内部复用的裸 EDIT：边框与圆角由外层容器绘制，自身关闭，
    // 避免套两层框。独立使用（UiTheme.MultilineInput 等）时保持默认 true，行为不变。
    private bool _borderVisible = true;

    private Color? _foregroundOverride;

    /// <summary>显式指定的文字色；主题切换时保留它，不被通用主文本色覆盖。</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color? ForegroundOverride
    {
        get => _foregroundOverride;
        set { _foregroundOverride = value; ForeColor = value ?? UiTheme.Text; }
    }

    public FluentTextBox()
    {
        BorderStyle = BorderStyle.None;
        Font = UiTheme.Body;
        SetColors();
        UiTheme.ThemeChanged += OnThemeChanged;
    }

    /// <summary>是否自绘边框与底边线；作为复合输入框的内部 EDIT 时由外层画框，设为 false。</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool BorderVisible
    {
        get => _borderVisible;
        set { _borderVisible = value; Invalidate(); }
    }

    [Browsable(true)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Radius
    {
        get => _radius;
        set { _radius = value; ApplyShape(); Invalidate(); }
    }

    private void OnThemeChanged()
    {
        SetColors();
        NativeTheme.Apply(this);
        Invalidate();
    }

    private void SetColors()
    {
        BackColor = ReadOnly ? UiTheme.Surface : UiTheme.FieldBg;
        ForeColor = _foregroundOverride ?? UiTheme.Text;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) UiTheme.ThemeChanged -= OnThemeChanged;
        base.Dispose(disposing);
    }

    protected override void OnGotFocus(EventArgs e) { _focused = true; Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { _focused = false; Invalidate(); base.OnLostFocus(e); }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        ApplyShape();
        ApplyMargins();
    }

    protected override void OnMultilineChanged(EventArgs e)
    {
        base.OnMultilineChanged(e);
        ApplyMargins();
    }

    private void ApplyShape()
    {
        if (Width <= 0 || Height <= 0) return;
        using var path = UiTheme.RoundedPath(new Rectangle(0, 0, Width, Height), _radius);
        UiTheme.SetRegion(this, path);
    }

    private void ApplyMargins()
    {
        if (!IsHandleCreated) return;
        var lParam = (IntPtr)((TextInset << 16) | TextInset);
        SendMessage(Handle, EM_SETMARGINS, (IntPtr)(EC_LEFTMARGIN | EC_RIGHTMARGIN), lParam);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyMargins();
        NativeTheme.Apply(this);
        // 系统 cue banner 在部分宿主容器里不绘制，占位符统一改由 OnPaint 自绘，这里把它关掉避免重影。
        SendMessage(Handle, EM_SETCUEBANNER, (IntPtr)1, string.Empty);
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        if (_borderVisible)
        {
            var color = _focused ? UiTheme.Primary : UiTheme.Border;
            UiTheme.DrawRoundedBorder(g, rect, _radius, color, _focused ? 2 : 1);
            if (!_focused && Enabled)
            {
                // Win11 输入框底边略重，形成层次
                var w = UiTheme.PenW();
                using var pen = new Pen(UiTheme.Stroke, w);
                var y = UiTheme.Edge(rect.Bottom, w);
                g.DrawLine(pen, rect.X + _radius, y, rect.Right - _radius, y);
            }
        }
        if (Enabled && Text.Length == 0 && PlaceholderText.Length > 0)
        {
            var area = new Rectangle(TextInset, 0, Math.Max(0, Width - TextInset * 2), Height);
            TextRenderer.DrawText(g, PlaceholderText, Font, area, UiTheme.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }
}

/// <summary>
/// 单行输入框（垂直居中版）。原生单行 EDIT 的文字按客户区顶部排版，控件一撑高就贴顶；
/// 这里由外层容器画 32px 圆角框，内部 EDIT 收窄到字体行高再几何居中，文字与占位提示同处中线。
/// 对外仍是一个 Control：Text / TextChanged / KeyDown / Height 等成员与 FluentTextBox 一致，
/// 调用方（加入 FlowLayoutPanel、读 Text、订阅键盘）无需任何改动。
/// </summary>
public class FluentInput : Panel
{
    private const int FieldHeight = 32;
    private const int Inset = 9;

    private readonly FluentTextBox _edit;
    private bool _focused;

    public FluentInput(string placeholder = "", int width = 220)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer, true);
        BackColor = Color.Transparent;
        // Size 赋值会触发 OnSizeChanged → LayoutEdit，必须在 _edit 就位之后，
        // 否则 LayoutEdit 访问未初始化的 _edit 直接空引用。
        var size = UiTheme.Sz(width, FieldHeight);

        _edit = new FluentTextBox
        {
            BorderVisible = false,
            PlaceholderText = placeholder,
            BorderStyle = BorderStyle.None,
            Dock = DockStyle.None,
            Margin = Padding.Empty,
        };
        Controls.Add(_edit);
        _edit.Enter += (_, _) => { _focused = true; Invalidate(); };
        _edit.Leave += (_, _) => { _focused = false; Invalidate(); };
        // 事件/文本转发到外层，调用方订阅的仍是本控件
        _edit.TextChanged += (_, e) => TextChanged?.Invoke(this, e);
        _edit.KeyDown += (_, e) => KeyDown?.Invoke(this, e);
        UiTheme.ThemeChanged += OnThemeChanged;
        // 尺寸赋值要在 EDIT 就位后：Layout 里要量它的 PreferredHeight。
        Size = size;
        MinimumSize = size;
        LayoutEdit();
    }

    public new event EventHandler? TextChanged;
    public new event KeyEventHandler? KeyDown;

    /// <summary>输入文本；读写都转发到内部 EDIT，输入校验逻辑完全不变。</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public new string Text
    {
        get => _edit.Text;
        set => _edit.Text = value ?? string.Empty;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string PlaceholderText
    {
        get => _edit.PlaceholderText;
        set { _edit.PlaceholderText = value; _edit.Invalidate(); }
    }

    public new bool Focused => _edit.Focused;

    /// <summary>只读模式：用于展示路径等由程序填入、不允许手改的内容。</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool ReadOnly
    {
        get => _edit.ReadOnly;
        set { _edit.ReadOnly = value; Invalidate(); }
    }

    public new bool Focus() => _edit.Focus();

    private void OnThemeChanged() => Invalidate();

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        _edit.Enabled = Enabled;
        Invalidate();
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        LayoutEdit();
    }

    /// <summary>
    /// 内部 EDIT 按字体实测行高收窄，垂直居中于外层框；水平内缩与独立 FluentTextBox 一致。
    /// 高度按内容取小值，保障不同字号/显示缩放下文字始终在同一条中线。
    /// </summary>
    private void LayoutEdit()
    {
        if (_edit is null || Width <= 0 || Height <= 0) return;
        int inset = UiTheme.Sx(Inset);
        int h = Math.Min(_edit.PreferredHeight, Height);
        if (h <= 0) return;
        _edit.SetBounds(inset, (Height - h) / 2,
            Math.Max(UiTheme.Sx(20), Width - inset * 2), h);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        UiTheme.FillSurface(e.Graphics, rect, UiTheme.RadiusMedium,
            UiTheme.FieldBg, _focused ? UiTheme.Primary : UiTheme.Border);
        if (!_focused && Enabled)
        {
            var w = UiTheme.PenW();
            using var pen = new Pen(UiTheme.Stroke, w);
            var y = UiTheme.Edge(rect.Bottom, w);
            e.Graphics.DrawLine(pen, rect.X + UiTheme.RadiusMedium, y, rect.Right - UiTheme.RadiusMedium, y);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) UiTheme.ThemeChanged -= OnThemeChanged;
        base.Dispose(disposing);
    }
}

/// <summary>
/// 圆角数据表格。去掉系统直角边框，改为 Region 裁剪 + 自绘柔边，
/// 行高、隔行底色与选中态统一按 Win11 列表规范（OwnerDraw 自绘表头与行）。
/// </summary>
public class FluentListView : ListView
{
    private int _radius = UiTheme.RadiusMedium;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string? win);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr h, out RECT r);

    public FluentListView()
    {
        BorderStyle = BorderStyle.None;
        View = View.Details;
        FullRowSelect = true;
        GridLines = false;
        MultiSelect = false;
        HeaderStyle = ColumnHeaderStyle.Nonclickable;
        Font = UiTheme.Mono;
        BackColor = UiTheme.Panel;
        ForeColor = UiTheme.Text;
        // 不要用 SmallImageList 塞透明占位图来抬行高：Details 视图一旦挂了 ImageList，
        // DrawItem / DrawSubItem / DrawColumnHeader 全部不触发，数据正常但表格一片空白。
        // 行高由 Mono 字体决定，自绘负责内边距与隔行底色。
        UiTheme.ThemeChanged += OnThemeChanged;
        DrawColumnHeader += OnHeader;
        DrawItem += OnRow;
        DrawSubItem += OnCell;
        // 先挂自绘回调再开启 OwnerDraw：属性变更时控件会缓存回调状态，顺序颠倒更稳妥。
        OwnerDraw = true;
    }

    [Browsable(true)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Radius
    {
        get => _radius;
        set { _radius = value; ApplyShape(); Invalidate(); }
    }

    private void OnThemeChanged()
    {
        BackColor = UiTheme.Panel;
        ForeColor = UiTheme.Text;
        NativeTheme.Apply(this);
        Invalidate();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        NativeTheme.Apply(this);
    }

    /// <summary>
    /// 深色主题（DarkMode_Explorer）下原生控件会在表头以下的空白区域按列边界画出分隔竖线，
    /// 行区域被 OwnerDraw 整行覆盖不受影响，只有"最后一行到底边"的空档露出来。
    /// 在原生绘制完成后，把这段空档铺成面板底色，竖线即消失。
    /// WM_PRINT（DrawToBitmap / 打印路径）的绘制目标是消息传入的 HDC，需单独覆盖。
    /// </summary>
    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        try
        {
            if (m.Msg == 0x000F) // WM_PAINT：画到窗口 DC
            {
                using var g = Graphics.FromHwnd(Handle);
                PaintOverEmptyArea(g);
            }
            else if (m.Msg == 0x0317 && m.WParam != IntPtr.Zero) // WM_PRINT：画到调用方的 HDC
            {
                using var g = Graphics.FromHdc(m.WParam);
                PaintOverEmptyArea(g);
            }
        }
        catch { /* 覆盖层纯装饰，失败不影响数据 */ }
    }

    private void PaintOverEmptyArea(Graphics g)
    {
        if (Width <= 0 || Height <= 0 || Columns.Count == 0 || !IsHandleCreated) return;
        var header = FindWindowEx(Handle, IntPtr.Zero, "SysHeader32", null);
        if (header == IntPtr.Zero || !GetWindowRect(header, out var hr)) return;
        var topLeft = PointToClient(new Point(hr.Left, hr.Top));
        var bottomRight = PointToClient(new Point(hr.Right, hr.Bottom));
        int rowsBottom = bottomRight.Y;
        if (Items.Count > 0)
            rowsBottom = Math.Max(rowsBottom, Items[Items.Count - 1].GetBounds(ItemBoundsPortion.Entire).Bottom);
        int clientH = ClientSize.Height;
        if (rowsBottom >= clientH) return;
        using var b = new SolidBrush(UiTheme.Panel);
        g.FillRectangle(b, 0, rowsBottom, ClientSize.Width, clientH - rowsBottom);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            UiTheme.ThemeChanged -= OnThemeChanged;
        }
        base.Dispose(disposing);
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        ApplyShape();
        StretchLastColumn();
    }

    /// <summary>
    /// 末列撑满剩余宽度。OwnerDraw 只对列区间回调，最后一列右侧的表头余量仍由系统绘制，
    /// 暗色主题下会露出一截原生白边。
    /// </summary>
    private void StretchLastColumn()
    {
        if (Columns.Count == 0) return;
        int used = 0;
        for (var i = 0; i < Columns.Count - 1; i++) used += Columns[i].Width;
        var w = ClientSize.Width - used;
        if (w > UiTheme.Sx(60)) Columns[^1].Width = w;
    }

    private void ApplyShape()
    {
        if (Width <= 0 || Height <= 0) return;
        using var path = UiTheme.RoundedPath(new Rectangle(0, 0, Width, Height), _radius);
        UiTheme.SetRegion(this, path);
    }

    private static Padding CellPad => UiTheme.Pad(10, 0, 6, 0);

    private void OnHeader(object? sender, DrawListViewColumnHeaderEventArgs e)
    {
        using (var bg = new SolidBrush(UiTheme.TableHead))
            e.Graphics.FillRectangle(bg, e.Bounds);
        var w = UiTheme.PenW();
        using (var pen = new Pen(UiTheme.RowLine, w))
        {
            var y = UiTheme.Edge(e.Bounds.Bottom, w);
            e.Graphics.DrawLine(pen, e.Bounds.Left, y, e.Bounds.Right, y);
        }
        var text = new Rectangle(
            e.Bounds.X + CellPad.Left, e.Bounds.Y,
            Math.Max(0, e.Bounds.Width - CellPad.Horizontal), e.Bounds.Height);
        TextRenderer.DrawText(e.Graphics, e.Header?.Text ?? string.Empty, UiTheme.Bold, text, UiTheme.TextSecondary,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
            TextFormatFlags.NoPadding);
    }

    private void OnRow(object? sender, DrawListViewItemEventArgs e)
    {
        var item = e.Item;
        if (item is null) return;
        bool sel = item.Selected;
        var row = new Rectangle(0, e.Bounds.Top, ClientSize.Width, e.Bounds.Height);
        Color fill = sel ? UiTheme.PrimarySoft
            : item.Index % 2 == 1 ? UiTheme.Surface
            : UiTheme.Panel;
        using (var b = new SolidBrush(fill))
            e.Graphics.FillRectangle(b, row);
        var w = UiTheme.PenW();
        using var pen = new Pen(UiTheme.RowLine, w);
        var y = UiTheme.Edge(row.Bottom, w);
        e.Graphics.DrawLine(pen, row.Left, y, row.Right, y);
    }

    private void OnCell(object? sender, DrawListViewSubItemEventArgs e)
    {
        var item = e.Item;
        if (item is null) return;
        // 每个单元格都用表头同一内缩起笔，否则第 2 列起表头文字比数据靠左一截
        var text = new Rectangle(
            e.Bounds.X + CellPad.Left, e.Bounds.Y,
            Math.Max(0, e.Bounds.Width - CellPad.Horizontal), e.Bounds.Height);
        // 行/单元格显式设过色（与列表自身 ForeColor 不同）时按它画，用于"未变更/失败"这类弱化行
        Color fore = item.Selected ? UiTheme.Primary
            : !item.ForeColor.IsEmpty && !item.ForeColor.Equals(ForeColor) ? item.ForeColor
            : UiTheme.Text;
        TextRenderer.DrawText(e.Graphics, e.SubItem?.Text ?? string.Empty, Font, text, fore,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
            TextFormatFlags.NoPadding);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        UiTheme.DrawRoundedBorder(e.Graphics, new Rectangle(0, 0, Width - 1, Height - 1), _radius, UiTheme.Border);
    }
}

/// <summary>
/// 自绘复选框。原生方框走系统浅色视觉样式，在深色主题里是一块刺眼的白，和整页色板脱节；
/// 这里勾选态用主色实心 + 白色对勾，未勾选用表面色 + 描边，与按钮/输入框同一套圆角语言。
/// </summary>
public class FluentCheckBox : CheckBox
{
    private const int Box = 18;
    private const int Gap = 8;
    private bool _hover;

    public FluentCheckBox()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        Font = UiTheme.Body;
        ForeColor = UiTheme.TextSecondary;
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        AutoSize = true;
        UiTheme.ThemeChanged += OnThemeChanged;
        MouseEnter += (_, _) => { _hover = true; Invalidate(); };
        MouseLeave += (_, _) => { _hover = false; Invalidate(); };
    }

    private void OnThemeChanged()
    {
        ForeColor = UiTheme.TextSecondary;
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) UiTheme.ThemeChanged -= OnThemeChanged;
        base.Dispose(disposing);
    }

    protected override void OnCheckedChanged(EventArgs e)
    {
        base.OnCheckedChanged(e);
        Invalidate();
    }

    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    public override Size GetPreferredSize(Size proposedSize)
    {
        int box = UiTheme.Sx(Box);
        var text = Text.Length == 0
            ? Size.Empty
            : TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding);
        return new Size(box + UiTheme.Sx(Gap) + text.Width, Math.Max(box, text.Height));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(UiTheme.Backdrop(Parent));
        int box = UiTheme.Sx(Box);
        var r = new Rectangle(0, (Height - box) / 2, box, box);
        if (Checked)
        {
            UiTheme.FillSurface(g, r, UiTheme.RadiusSmall,
                Enabled ? UiTheme.Primary : UiTheme.Surface, null);
            float inset = box * 0.2f;
            IconFactory.Draw(g, "check",
                new RectangleF(r.X + inset, r.Y + inset, box - inset * 2, box - inset * 2), Color.White);
        }
        else
        {
            UiTheme.FillSurface(g, r, UiTheme.RadiusSmall, Enabled ? UiTheme.FieldBg : UiTheme.Surface,
                _hover && Enabled ? UiTheme.Stroke : UiTheme.Border);
        }
        if (Focused && ShowFocusCues)
            UiTheme.DrawRoundedBorder(g, new Rectangle(r.X - UiTheme.Sx(2), r.Y - UiTheme.Sx(2),
                box + UiTheme.Sx(4), box + UiTheme.Sx(4)), UiTheme.RadiusMedium, UiTheme.TextSecondary, 2);
        if (Text.Length == 0) return;
        int left = r.Right + UiTheme.Sx(Gap);
        TextRenderer.DrawText(g, Text, Font,
            new Rectangle(left, 0, Math.Max(0, Width - left), Height),
            Enabled ? ForeColor : UiTheme.Muted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
            TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
    }
}

/// <summary>
/// 主题化整数步进框，整控件自绘。
/// 之前继承 NumericUpDown 走不通：编辑框与上下按钮是系统子窗口，既不吃 BackColor，
/// 也不给本体留出描边空间（Padding 在 UpDownBase 的布局里不起作用），
/// 且高度只按字体算，和同排的 UiTheme.Input 比就是一高一矮。
/// 现在是"圆角面板 + 无边框编辑框 + 自绘箭头列"，高度、描边、聚焦态与 FluentTextBox 同一套规范。
/// </summary>
public class FluentNumeric : Panel
{
    private const int FieldHeight = 32;
    private const int StripWidth = 20;
    private const int TextInset = 9;

    private readonly TextBox _edit;
    private Rectangle _up, _down;
    private int _hovered;
    private bool _focused;
    private int _minimum;
    private int _maximum = 999999;
    private int _value;

    public event EventHandler? ValueChanged;

    public FluentNumeric()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = UiTheme.FieldBg;
        _edit = new TextBox
        {
            BorderStyle = BorderStyle.None,
            Font = UiTheme.Body,
            BackColor = UiTheme.FieldBg,
            ForeColor = UiTheme.Text,
            TextAlign = HorizontalAlignment.Right,
        };
        Controls.Add(_edit);
        _edit.GotFocus += (_, _) => { _focused = true; Invalidate(); };
        _edit.LostFocus += (_, _) => { _focused = false; Commit(); Invalidate(); };
        _edit.KeyDown += OnEditKeyDown;
        _edit.KeyPress += OnEditKeyPress;
        _edit.MouseWheel += (_, e) => OnMouseWheel(e);
        UiTheme.ThemeChanged += OnThemeChanged;
        // 尺寸赋值要在编辑框就位之后：Size 会走 OnSizeChanged → LayoutParts，那里要量编辑框的字体高。
        Size = UiTheme.Sz(90, FieldHeight);
        MinimumSize = new Size(0, UiTheme.Sx(FieldHeight));
        SetValue(0);
    }

    [Browsable(true)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Minimum
    {
        get => _minimum;
        set { _minimum = value; if (_maximum < _minimum) _maximum = _minimum; SetValue(_value); }
    }

    [Browsable(true)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Maximum
    {
        get => _maximum;
        set { _maximum = value; if (_minimum > _maximum) _minimum = _maximum; SetValue(_value); }
    }

    [Browsable(true)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Value
    {
        get => _value;
        set => SetValue(value);
    }

    private void SetValue(int v)
    {
        var clamped = Math.Clamp(v, _minimum, _maximum);
        _edit.Text = clamped.ToString();
        _edit.SelectionStart = _edit.TextLength;
        if (clamped == _value) return;
        _value = clamped;
        ValueChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    private void Commit()
    {
        if (int.TryParse(_edit.Text, out var v)) SetValue(v);
        else SetValue(_value);
    }

    private void Step(int delta) => SetValue(Math.Clamp(_value + delta, _minimum, _maximum));

    private void OnEditKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Up) Step(1);
        else if (e.KeyCode == Keys.Down) Step(-1);
        else if (e.KeyCode == Keys.Enter) Commit();
        else return;
        e.Handled = e.SuppressKeyPress = true;
    }

    private void OnEditKeyPress(object? sender, KeyPressEventArgs e)
    {
        if (char.IsControl(e.KeyChar)) return;
        if (!char.IsDigit(e.KeyChar) && !(e.KeyChar == '-' && _minimum < 0)) e.Handled = true;
    }

    private void OnThemeChanged() => ApplyStateColors();

    /// <summary>
    /// 面板与内部编辑框的底色必须同步：两者不一致时子窗口会露出一块更深的矩形，像两个框叠在一起。
    /// 禁用态仍用 FieldBg：若清成 Surface 会与卡片背景同色，整个框"隐形"，只剩悬浮的数字。
    /// 禁用感由 Muted 前景 + 常规边框表达。
    /// </summary>
    private void ApplyStateColors()
    {
        BackColor = UiTheme.FieldBg;
        _edit.BackColor = UiTheme.FieldBg;
        _edit.ForeColor = Enabled ? UiTheme.Text : UiTheme.Muted;
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) UiTheme.ThemeChanged -= OnThemeChanged;
        base.Dispose(disposing);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        _edit.Enabled = Enabled;
        ApplyStateColors();
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        LayoutParts();
    }

    /// <summary>
    /// 编辑框在 32 设计像素的框内按字体实测高垂直居中，箭头列贴右内缘；
    /// Region 裁圆角会把子窗口一并裁掉，直角边不会漏出来。
    /// </summary>
    private void LayoutParts()
    {
        if (Width <= 0 || Height <= 0) return;
        var ring = new Rectangle(0, 0, Width - 1, Height - 1);
        int strip = UiTheme.Sx(StripWidth);
        int stripLeft = Math.Max(UiTheme.Sx(TextInset), ring.Right - strip);
        // L23：极小高度下夹住上下半区，避免出现负高度矩形导致箭头画到错误位置。
        var stripHeight = Math.Max(0, (ring.Height - UiTheme.PenW() * 2) / 2);
        _up = new Rectangle(stripLeft, UiTheme.PenW(), strip, stripHeight);
        _down = new Rectangle(stripLeft, _up.Bottom, strip, Math.Max(0, ring.Height - _up.Bottom - UiTheme.PenW()));
        int inset = UiTheme.Sx(TextInset);
        int h = Math.Min(_edit.PreferredHeight, Height);
        _edit.SetBounds(inset, (Height - h) / 2, Math.Max(0, stripLeft - inset - UiTheme.Sx(4)), h);
        using var path = UiTheme.RoundedPath(ring, UiTheme.RadiusMedium);
        UiTheme.SetRegion(this, path);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (_up.Contains(e.Location)) { Commit(); Step(1); return; }
        if (_down.Contains(e.Location)) { Commit(); Step(-1); return; }
        if (!_edit.Focused) _edit.Focus();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var hit = _up.Contains(e.Location) ? 1 : _down.Contains(e.Location) ? -1 : 0;
        if (hit != _hovered) { _hovered = hit; Invalidate(_up); Invalidate(_down); }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hovered != 0) { _hovered = 0; Invalidate(_up); Invalidate(_down); }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        Step(e.Delta > 0 ? 1 : -1);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(UiTheme.FieldBg);
        var ring = new Rectangle(0, 0, Width - 1, Height - 1);
        var color = _focused ? UiTheme.Primary : UiTheme.Border;
        UiTheme.DrawRoundedBorder(g, ring, UiTheme.RadiusMedium, color, _focused ? 2 : 1);
        if (_up.Width <= 0 || _up.Height <= 0 || _down.Height <= 0) return;
        var w = UiTheme.PenW();
        using (var pen = new Pen(UiTheme.Border, w))
        {
            var x = UiTheme.Edge(_up.Left, w);
            g.DrawLine(pen, x, UiTheme.Sx(5), x, Math.Max(UiTheme.Sx(5), ring.Height - UiTheme.Sx(5)));
        }
        DrawChevron(g, _up, true, Chevron(_hovered == 1));
        DrawChevron(g, _down, false, Chevron(_hovered == -1));
    }

    private Color Chevron(bool hover) => !Enabled ? UiTheme.Muted : hover ? UiTheme.Primary : UiTheme.TextSecondary;

    private static void DrawChevron(Graphics g, Rectangle r, bool up, Color color)
    {
        float cx = r.X + r.Width / 2f;
        float cy = r.Y + r.Height / 2f;
        float dx = UiTheme.Sxf(3.4f);
        float dy = UiTheme.Sxf(2.4f);
        using var pen = new Pen(color, Math.Max(1f, UiTheme.Sxf(1.4f)))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };
        var pts = up
            ? new[] { new PointF(cx - dx, cy + dy), new PointF(cx, cy - dy), new PointF(cx + dx, cy + dy) }
            : new[] { new PointF(cx - dx, cy - dy), new PointF(cx, cy + dy), new PointF(cx + dx, cy - dy) };
        g.DrawLines(pen, pts);
    }
}
