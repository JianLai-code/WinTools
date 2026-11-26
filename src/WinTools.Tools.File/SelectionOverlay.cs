using System.Drawing;
using System.Windows.Forms;
using WinTools.Core;
using WinTools.Core.Ui;

namespace WinTools.Tools.Files;

/// <summary>
/// 区域框选覆盖层：铺满整个虚拟屏幕（含负坐标副屏），把抓好的底图铺在窗体上，
/// 再盖一层半透明暗色；选区内重绘亮底图并描边，做到"所见即所得"——确认后
/// 直接从同一张底图裁剪，不会因为二次抓屏把光标/界面变化截进去。
///
/// 坐标全部是物理屏幕像素：Location/Size 取自 SystemInformation.VirtualScreen，
/// 与 Graphics.CopyFromScreen 同一坐标系。PerMonitorV2 下绝不能乘 UiTheme.S。
/// 装饰（边框/文字/手柄）才用 UiTheme 的缩放助手。
///
/// 交互分三段：
/// 1) 框选/识别：拖动拉框，或点编号/按数字键选中识别到的窗口；识别框悬停时整块高亮。
/// 2) 调整：松手后选区保留，八个手柄可改上下左右边界，框内拖动可整体移动，框外拖动重画。
/// 3) 确认：双击选区或 Enter 落定；Esc / 右键取消。
/// </summary>
public sealed class SelectionOverlay : Form
{
    /// <summary>最多显示多少个候选窗口（再多用户也用不过来）。</summary>
    public const int MaxCandidates = 12;
    /// <summary>数字键可直接选中的候选个数。</summary>
    public const int QuickPickCount = 9;
    /// <summary>选区最小边长（物理像素），防止拖手柄把选区压成一条线。</summary>
    private const int MinSide = 16;

    /// <summary>当前鼠标动作；手柄优先级高于框内移动。</summary>
    private enum DragMode
    {
        None, Draw, Move,
        ResizeLeft, ResizeRight, ResizeTop, ResizeBottom,
        ResizeTopLeft, ResizeTopRight, ResizeBottomLeft, ResizeBottomRight,
    }

    private readonly Bitmap _backdrop;
    private readonly Rectangle _virtualScreen;
    private readonly string _hint;
    private readonly IReadOnlyList<WindowInfo> _candidates;
    private readonly Rectangle[] _candidateRects;
    private readonly List<Rectangle> _badgeRects = new();

    private DragMode _drag = DragMode.None;
    private DragMode _hoverMode = DragMode.None;
    private Point _dragOrigin;
    private Rectangle _dragStartSelection;
    private Point? _drawStart;
    private Point _downPoint;
    private Rectangle _selection;
    private bool _hasSelection;
    private int _activeIndex = -1;
    private int _hoverIndex = -1;

    /// <summary>确认后的选区（物理屏幕坐标）；取消时为空。</summary>
    public Rectangle SelectionScreen { get; private set; }

    /// <summary>
    /// </summary>
    /// <param name="hint">顶部提示文案；不传时用普通区域截图的文案。</param>
    /// <param name="candidates">桌面上可识别的窗口（物理屏幕坐标）；为空则纯手动框选。</param>
    /// <param name="initialSelection">预置选区（物理屏幕坐标），例如从桌面窗口列表点选的结果；
    /// 传了就先画好这个选区并允许继续调整，而不是直接截图。</param>
    public SelectionOverlay(Bitmap backdrop, Rectangle virtualScreen, string? hint = null,
        IReadOnlyList<WindowInfo>? candidates = null, Rectangle? initialSelection = null)
    {
        _backdrop = backdrop;
        _virtualScreen = virtualScreen;
        _candidates = candidates is null || candidates.Count == 0
            ? Array.Empty<WindowInfo>()
            : candidates.Take(MaxCandidates).ToArray();
        _candidateRects = _candidates.Select(w => ToLocal(w.Bounds)).ToArray();

        // 自定义文案（如滚动截图）只描述本模式的目的，通用操作说明统一在这里补上，
        // 免得某个入口漏写"怎么确认"——选区现在要显式确认，旧文案会误导用户。
        var lead = string.IsNullOrWhiteSpace(hint) ? "拖动框选 / 点编号窗口" : hint.Trim();
        _hint = lead + "  ·  拖边调大小、拖内部可移动  ·  单击确认  ·  Esc 取消";
        // 有候选窗口时把用法写清楚：用户看到一堆编号框，得知道能点什么。
        if (_candidates.Count > 0) _hint += "  ·  数字键 1~9 选中窗口";

        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        ControlBox = false;
        Text = string.Empty;
        BackColor = Color.Black;
        Cursor = Cursors.Cross;
        KeyPreview = true;

        Location = virtualScreen.Location;
        Size = virtualScreen.Size;

        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint, true);

        // 预置选区（窗口列表点选）也要能拖边/移动：先画成"已定选区"状态等用户确认。
        if (initialSelection is { } preset && ScreenCapture.IsValidSelection(preset))
        {
            var local = ToLocal(preset);
            local.Intersect(ClientRectangle);
            if (ScreenCapture.IsValidSelection(local))
            {
                _selection = local;
                _hasSelection = true;
            }
        }
    }

    /// <summary>屏幕物理坐标 → 覆盖层客户区坐标。</summary>
    private Rectangle ToLocal(Rectangle screen) => new(
        screen.X - _virtualScreen.X, screen.Y - _virtualScreen.Y, screen.Width, screen.Height);

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left)
        {
            if (e.Button == MouseButtons.Right) Cancel();
            return;
        }

        _downPoint = e.Location;
        _dragOrigin = e.Location;

        if (_hasSelection)
        {
            // 手柄与整体拖动都从按下瞬间的选区快照出发；漏了这一步会拿空矩形去算边界。
            _dragStartSelection = _selection;
            var handle = HitHandle(e.Location);
            if (handle != DragMode.None)
            {
                _drag = handle;
                return;
            }
            if (_selection.Contains(e.Location))
            {
                _drag = DragMode.Move;
                return;
            }
            // 框外按下：放弃旧选区，重新拉一个
            ClearSelection();
        }

        _drawStart = e.Location;
        _drag = DragMode.Draw;
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (_drag == DragMode.Draw && _drawStart is { } start)
        {
            _selection = ScreenCapture.NormalizeSelection(start, e.Location, ClientRectangle);
            Invalidate();
            return;
        }

        if (_drag == DragMode.Move)
        {
            _selection = MoveSelection(_dragStartSelection, e.Location);
            Invalidate();
            return;
        }

        if (IsResizeMode(_drag))
        {
            _selection = ResizeSelection(_drag, _dragStartSelection, e.Location);
            Invalidate();
            return;
        }

        // 没在拖动：已定选区时给手柄/框内的光标反馈；否则悬停候选窗口高亮
        if (_hasSelection)
        {
            _hoverMode = HitHandle(e.Location);
            Cursor = _hoverMode != DragMode.None ? CursorFor(_hoverMode)
                : _selection.Contains(e.Location) ? Cursors.SizeAll
                : Cursors.Cross;
            _hoverIndex = -1;
            Invalidate();
            return;
        }

        var hover = HitCandidate(e.Location);
        if (hover != _hoverIndex)
        {
            _hoverIndex = hover;
            Invalidate();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left || _drag == DragMode.None) return;

        if (_drag == DragMode.Draw)
        {
            var moved = Math.Abs(e.Location.X - _downPoint.X) + Math.Abs(e.Location.Y - _downPoint.Y);
            if (moved <= 6)
            {
                // 单击（几乎没拖动）落在候选窗口上：把它变成可调整选区，再让用户微调/确认
                var hit = HitCandidate(e.Location);
                if (hit >= 0)
                {
                    AdoptCandidate(hit);
                    _drag = DragMode.None;
                    Invalidate();
                    return;
                }
            }

            var rect = ScreenCapture.NormalizeSelection(_downPoint, e.Location, ClientRectangle);
            if (!ScreenCapture.IsValidSelection(rect))
            {
                // 误点（没拖动或拖得太小）：重置为未框选，让用户重新拖，不直接结束
                ClearSelection();
                _drag = DragMode.None;
                Invalidate();
                return;
            }
            _selection = rect;
            _hasSelection = true;
        }

        var wasMove = _drag == DragMode.Move;
        var tapMove = Math.Abs(e.Location.X - _downPoint.X) + Math.Abs(e.Location.Y - _downPoint.Y);
        _drag = DragMode.None;
        // 单击直接确认：框选/点选之后不用再按 Enter。
        // 只有按下与抬起几乎同点才算单击；拖动手柄或整体移动过就仍是调整状态。
        if (wasMove && tapMove <= 6)
        {
            Confirm();
            return;
        }
        // 双击选区内部也确认（兼容习惯双击的用户）。
        if (e.Clicks >= 2 && _hasSelection && _selection.Contains(e.Location))
        {
            Confirm();
            return;
        }
        Invalidate();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Escape)
        {
            Cancel();
        }
        else if (e.KeyCode == Keys.Enter)
        {
            if (_hasSelection) Confirm();
        }
        else if (e.KeyCode == Keys.Tab && !_hasSelection && _candidates.Count > 0)
        {
            // Tab 循环切换候选（Shift+Tab 反向）
            var step = e.Shift ? -1 : 1;
            _activeIndex = _activeIndex < 0
                ? (step > 0 ? 0 : _candidates.Count - 1)
                : (_activeIndex + step + _candidates.Count) % _candidates.Count;
            e.Handled = true;
            Invalidate();
        }
        else if (!_hasSelection && _candidates.Count > 0 && IsQuickPickKey(e.KeyCode, out var index))
        {
            // 数字键 1~9：选中对应窗口并进入可调整状态，按 Enter 落定
            AdoptCandidate(index);
            e.Handled = true;
            Invalidate();
        }
    }

    /// <summary>数字键 1~9 映射到候选下标（0 基）。</summary>
    private static bool IsQuickPickKey(Keys key, out int index)
    {
        index = -1;
        if (key >= Keys.D1 && key <= Keys.D9)
        {
            index = key - Keys.D1;
            return index < QuickPickCount;
        }
        if (key >= Keys.NumPad1 && key <= Keys.NumPad9)
        {
            index = key - Keys.NumPad1;
            return index < QuickPickCount;
        }
        return false;
    }

    /// <summary>把第 index 个候选窗口作为当前选区（可继续拖边/移动）。</summary>
    private void AdoptCandidate(int index)
    {
        if (index < 0 || index >= _candidateRects.Length) return;
        var r = _candidateRects[index];
        if (r.Width <= 0 || r.Height <= 0) return;
        _activeIndex = index;
        _selection = Rectangle.Intersect(r, ClientRectangle);
        _hasSelection = ScreenCapture.IsValidSelection(_selection);
        _hoverIndex = -1;
        _drawStart = null;
    }

    private void ClearSelection()
    {
        _selection = Rectangle.Empty;
        _hasSelection = false;
        _hoverMode = DragMode.None;
        _drawStart = null;
    }

    private void Confirm()
    {
        if (!_hasSelection || !ScreenCapture.IsValidSelection(_selection)) return;
        SelectionScreen = new Rectangle(_selection.X + _virtualScreen.X,
            _selection.Y + _virtualScreen.Y, _selection.Width, _selection.Height);
        DialogResult = DialogResult.OK;
    }

    private void Cancel()
    {
        SelectionScreen = Rectangle.Empty;
        DialogResult = DialogResult.Cancel;
    }

    /// <summary>客户区坐标命中了哪个候选窗口（取最上层/面积最小的那个优先）。</summary>
    private int HitCandidate(Point p)
    {
        for (var i = 0; i < _candidateRects.Length; i++)
        {
            if (_candidateRects[i].Contains(p)) return i;
        }
        return -1;
    }

    private static bool IsResizeMode(DragMode mode) => mode is
        DragMode.ResizeLeft or DragMode.ResizeRight or DragMode.ResizeTop or DragMode.ResizeBottom or
        DragMode.ResizeTopLeft or DragMode.ResizeTopRight or DragMode.ResizeBottomLeft or DragMode.ResizeBottomRight;

    /// <summary>手柄边长（物理像素，与坐标同单位）；命中范围略大一点更好拖。</summary>
    private static int HandleSize => 8;

    private static bool Near(Point p, int x, int y, int reach) =>
        Math.Abs(p.X - x) <= reach && Math.Abs(p.Y - y) <= reach;

    /// <summary>命中手柄：四角优先，其次四条边，最后才是框内移动。</summary>
    private DragMode HitHandle(Point p)
    {
        if (!_hasSelection) return DragMode.None;
        var r = _selection;
        int reach = Math.Max(HandleSize, UiTheme.Sx(9));
        if (Near(p, r.Left, r.Top, reach)) return DragMode.ResizeTopLeft;
        if (Near(p, r.Right - 1, r.Top, reach)) return DragMode.ResizeTopRight;
        if (Near(p, r.Left, r.Bottom - 1, reach)) return DragMode.ResizeBottomLeft;
        if (Near(p, r.Right - 1, r.Bottom - 1, reach)) return DragMode.ResizeBottomRight;
        if (p.X >= r.Left - reach && p.X <= r.Right + reach && Math.Abs(p.Y - r.Top) <= reach)
            return DragMode.ResizeTop;
        if (p.X >= r.Left - reach && p.X <= r.Right + reach && Math.Abs(p.Y - (r.Bottom - 1)) <= reach)
            return DragMode.ResizeBottom;
        if (p.Y >= r.Top - reach && p.Y <= r.Bottom + reach && Math.Abs(p.X - r.Left) <= reach)
            return DragMode.ResizeLeft;
        if (p.Y >= r.Top - reach && p.Y <= r.Bottom + reach && Math.Abs(p.X - (r.Right - 1)) <= reach)
            return DragMode.ResizeRight;
        return DragMode.None;
    }

    private static Cursor CursorFor(DragMode mode) => mode switch
    {
        DragMode.ResizeLeft or DragMode.ResizeRight => Cursors.SizeWE,
        DragMode.ResizeTop or DragMode.ResizeBottom => Cursors.SizeNS,
        DragMode.ResizeTopLeft or DragMode.ResizeBottomRight => Cursors.SizeNWSE,
        DragMode.ResizeTopRight or DragMode.ResizeBottomLeft => Cursors.SizeNESW,
        _ => Cursors.Cross,
    };

    private int Clamp(int value, int min, int max) => Math.Min(Math.Max(value, min), max);

    /// <summary>整体平移选区，保持尺寸并限制在屏幕内。</summary>
    private Rectangle MoveSelection(Rectangle start, Point p)
    {
        int dx = p.X - _dragOrigin.X;
        int dy = p.Y - _dragOrigin.Y;
        int x = Clamp(start.X + dx, 0, Math.Max(0, ClientSize.Width - start.Width));
        int y = Clamp(start.Y + dy, 0, Math.Max(0, ClientSize.Height - start.Height));
        return new Rectangle(x, y, start.Width, start.Height);
    }

    /// <summary>按手柄方向改边界；对边保持不动，并夹在屏幕范围内。</summary>
    private Rectangle ResizeSelection(DragMode mode, Rectangle start, Point p)
    {
        int dx = p.X - _dragOrigin.X;
        int dy = p.Y - _dragOrigin.Y;
        int left = start.Left, top = start.Top, right = start.Right, bottom = start.Bottom;
        switch (mode)
        {
            case DragMode.ResizeLeft:
                left = Clamp(left + dx, 0, right - MinSide); break;
            case DragMode.ResizeRight:
                right = Clamp(right + dx, left + MinSide, ClientSize.Width); break;
            case DragMode.ResizeTop:
                top = Clamp(top + dy, 0, bottom - MinSide); break;
            case DragMode.ResizeBottom:
                bottom = Clamp(bottom + dy, top + MinSide, ClientSize.Height); break;
            case DragMode.ResizeTopLeft:
                left = Clamp(left + dx, 0, right - MinSide);
                top = Clamp(top + dy, 0, bottom - MinSide); break;
            case DragMode.ResizeTopRight:
                right = Clamp(right + dx, left + MinSide, ClientSize.Width);
                top = Clamp(top + dy, 0, bottom - MinSide); break;
            case DragMode.ResizeBottomLeft:
                left = Clamp(left + dx, 0, right - MinSide);
                bottom = Clamp(bottom + dy, top + MinSide, ClientSize.Height); break;
            case DragMode.ResizeBottomRight:
                right = Clamp(right + dx, left + MinSide, ClientSize.Width);
                bottom = Clamp(bottom + dy, top + MinSide, ClientSize.Height); break;
        }
        return Rectangle.FromLTRB(left, top, right, bottom);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
        g.DrawImage(_backdrop, new Rectangle(0, 0, _backdrop.Width, _backdrop.Height));

        using (var shade = new SolidBrush(Color.FromArgb(140, 8, 12, 20)))
            g.FillRectangle(shade, ClientRectangle);

        DrawCandidates(g);

        // 选区内重画亮底图，覆盖暗色层——这就是最终会保存的像素
        if (_hasSelection && ScreenCapture.IsValidSelection(_selection))
            DrawSelection(g, _selection);

        if (!_hasSelection && _drag == DragMode.Draw && ScreenCapture.IsValidSelection(_selection))
        {
            // 拖动过程中也点亮选区，所见即所得
            DrawSelection(g, _selection, handles: false);
        }

        DrawHint(g);
    }

    /// <summary>已放置的序号牌位置：用来避免两个编号叠在一起。</summary>
    private void DrawCandidates(Graphics g)
    {
        _badgeRects.Clear();
        // 选区定下来之后候选只留淡虚线，不再抢视觉焦点
        var interactive = !_hasSelection;
        for (var i = 0; i < _candidates.Count; i++)
        {
            var r = _candidateRects[i];
            if (r.Width <= 0 || r.Height <= 0) continue;
            var active = interactive && (i == _activeIndex || i == _hoverIndex);

            if (active)
            {
                // 智能识别到的窗口被指到时整块提亮：和用户亲手框选的效果一致
                g.DrawImage(_backdrop, r, r, GraphicsUnit.Pixel);
            }

            using (var pen = new Pen(active ? UiTheme.Primary : Color.FromArgb(130, 190, 210, 235),
                       active ? UiTheme.PenW(2) : 1f))
            {
                pen.DashStyle = active
                    ? System.Drawing.Drawing2D.DashStyle.Solid
                    : System.Drawing.Drawing2D.DashStyle.Dash;
                g.DrawRectangle(pen, r.X, r.Y, Math.Max(0, r.Width - 1), Math.Max(0, r.Height - 1));
            }

            if (!interactive) continue;

            DrawBadge(g, PlaceBadge(r), i + 1, active);
            if (active)
            {
                // 应用名和尺寸合在一个标签里，放在框内右下角：
                // 放框外上方会压住左上角的提示条（候选框常常就是从屏幕左上角开始的）。
                DrawCandidateLabel(g, r, _candidates[i]);
            }
        }
    }

    /// <summary>
    /// 给候选窗口安排序号牌的位置。两个窗口完全重叠时（常见于多个全屏应用），
    /// 编号牌会叠成一坨只看得见一个，这里往右错开，保证每个编号都露得出来。
    /// </summary>
    private Rectangle PlaceBadge(Rectangle candidate)
    {
        int d = UiTheme.Sx(18);
        var pos = new Rectangle(candidate.X + UiTheme.Sx(2), candidate.Y + UiTheme.Sx(2), d, d);
        for (var guard = 0; guard < 24 && _badgeRects.Any(b => b.IntersectsWith(pos)); guard++)
            pos = new Rectangle(pos.X + d + UiTheme.Sx(3), pos.Y, d, d);
        _badgeRects.Add(pos);
        return pos;
    }

    /// <summary>候选窗口左上角的编号牌。</summary>
    private void DrawBadge(Graphics g, Rectangle slot, int number, bool active)
    {
        int d = UiTheme.Sx(18);
        int bx = slot.X;
        int by = slot.Y;
        using (var fill = new SolidBrush(active ? UiTheme.Primary : Color.FromArgb(210, 24, 32, 46)))
            g.FillEllipse(fill, bx, by, d, d);
        using (var pen = new Pen(Color.FromArgb(200, 210, 225, 245), 1f))
            g.DrawEllipse(pen, bx, by, d, d);

        var text = number.ToString();
        TextRenderer.DrawText(g, text, UiTheme.FontAt(11),
            new Rectangle(bx, by, d, d), Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    /// <summary>
    /// 当前候选的应用名与尺寸标签（画在框内右下角）。
    /// 放在框外上方会盖住屏幕左上角的提示条——候选框经常就是从 (0,0) 开始的整屏窗口。
    /// </summary>
    private void DrawCandidateLabel(Graphics g, Rectangle candidate, WindowInfo info)
    {
        var text = $"{info.AppName} · {candidate.Width}×{candidate.Height}";
        var size = TextRenderer.MeasureText(text, UiTheme.FontAt(12));
        int w = size.Width + UiTheme.Sx(16);
        int h = size.Height + UiTheme.Sx(8);
        int x = Math.Max(candidate.X, candidate.Right - w - UiTheme.Sx(6));
        int y = Math.Max(candidate.Y, candidate.Bottom - h - UiTheme.Sx(6));
        var box = new Rectangle(x, y, w, h);
        UiTheme.FillSurface(g, box, UiTheme.RadiusMedium,
            Color.FromArgb(236, 14, 22, 34), Color.FromArgb(150, 120, 150, 190));
        TextRenderer.DrawText(g, text, UiTheme.FontAt(12), box, Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    /// <summary>已定选区的亮底图、边框、八个调整手柄和尺寸牌。</summary>
    private void DrawSelection(Graphics g, Rectangle rect, bool handles = true)
    {
        g.DrawImage(_backdrop, rect, rect, GraphicsUnit.Pixel);
        using var pen = new Pen(UiTheme.Primary, UiTheme.PenW(2))
        {
            DashStyle = System.Drawing.Drawing2D.DashStyle.Solid
        };
        g.DrawRectangle(pen, rect.X, rect.Y,
            Math.Max(0, rect.Width - 1), Math.Max(0, rect.Height - 1));

        if (handles)
        {
            int d = HandleSize;
            Span<Point> points =
            [
                new(rect.Left, rect.Top), new(rect.Right - 1, rect.Top),
                new(rect.Left, rect.Bottom - 1), new(rect.Right - 1, rect.Bottom - 1),
                new(rect.Left + rect.Width / 2, rect.Top),
                new(rect.Left + rect.Width / 2, rect.Bottom - 1),
                new(rect.Left, rect.Top + rect.Height / 2),
                new(rect.Right - 1, rect.Top + rect.Height / 2),
            ];
            using var fill = new SolidBrush(Color.FromArgb(250, 250, 253, 255));
            using var edge = new Pen(UiTheme.Primary, 1f);
            foreach (var pt in points)
            {
                var box = new Rectangle(pt.X - d / 2, pt.Y - d / 2, d, d);
                g.FillRectangle(fill, box);
                g.DrawRectangle(edge, box.X, box.Y, box.Width - 1, box.Height - 1);
            }
        }
        DrawSizeTag(g, rect);
    }

    private void DrawHint(Graphics g)
    {
        int pad = UiTheme.Sx(12);
        int h = UiTheme.Sx(34);
        var font = UiTheme.FontAt(13);
        // 提示文案随模式变化（滚动截图的更长），按实际文字宽度自适应，别把字裁掉。
        var measured = TextRenderer.MeasureText(_hint, font, new Size(int.MaxValue, int.MaxValue),
            TextFormatFlags.NoPadding);
        var box = new Rectangle(UiTheme.Sx(24), UiTheme.Sx(24),
            Math.Min(ClientSize.Width - UiTheme.Sx(48), measured.Width + pad * 2 + UiTheme.Sx(8)), h);
        if (box.Width <= 0 || box.Height <= 0) return;
        UiTheme.FillSurface(g, box, UiTheme.RadiusLarge,
            Color.FromArgb(232, 18, 26, 40), Color.FromArgb(180, 120, 150, 190));
        var text = new Rectangle(box.X + pad, box.Y, box.Width - pad * 2, box.Height);
        TextRenderer.DrawText(g, _hint, font,
            text, Color.FromArgb(240, 244, 250),
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    private void DrawSizeTag(Graphics g, Rectangle rect)
    {
        var text = ScreenCapture.DescribeSize(rect.Size);
        var size = TextRenderer.MeasureText(text, UiTheme.FontAt(12));
        int w = size.Width + UiTheme.Sx(16);
        int h = size.Height + UiTheme.Sx(8);
        int x = Math.Min(rect.X, Math.Max(0, ClientSize.Width - w));
        int y = rect.Y - h - UiTheme.Sx(6);
        if (y < 0) y = Math.Min(ClientSize.Height - h, rect.Bottom + UiTheme.Sx(6));
        var box = new Rectangle(x, y, w, h);
        UiTheme.FillSurface(g, box, UiTheme.RadiusMedium,
            Color.FromArgb(236, 14, 22, 34), Color.FromArgb(150, 120, 150, 190));
        TextRenderer.DrawText(g, text, UiTheme.FontAt(12), box, Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }
}
