using System.Drawing;
using System.Drawing.Imaging;
using System.Threading;
using System.Windows.Forms;
using WinTools.Core;
using WinTools.Core.Ui;

namespace WinTools.Tools.Files;

/// <summary>工具栏「滚动截图」的回调结果：成功时带长图，取消时带一句原因（可为空）。</summary>
public readonly record struct ScrollCaptureReply(Bitmap? Image, string Message = "");

/// <summary>
/// 参考微信截图的独立标注编辑窗口：窗口尺寸贴合图片本身，图片下方紧接一条白色图标工具栏，
/// 点有选项的工具会在按钮下方浮出二级面板（颜色 / 粗细 / 马赛克块大小 + 一键打码 / 字号 / 表情）。
///
/// 与旧版"全屏覆盖层"的区别：这里不再铺满整屏、不再显示整屏底图与选区遮罩——
/// 传入的 image 已经是裁剪后的成品区域，所有标注坐标都相对这张图片（图像坐标）。
/// 大图会等比缩放显示以适配屏幕，但命令与输出始终按原图像素，所见即所得。
/// </summary>
public sealed class AnnotationOverlay : Form
{
    private static readonly Color[] Palette =
    [
        Color.FromArgb(230, 55, 55), Color.FromArgb(250, 200, 40),
        Color.FromArgb(60, 190, 110), Color.FromArgb(70, 140, 235),
        Color.White, Color.Black
    ];
    private static readonly int[] Widths = [2, 3, 5, 8];
    private static readonly int[] FontSizes = [16, 26, 40, 64];
    private static readonly int[] MosaicBlocks = [4, 8, 16];
    /// <summary>表情落到图上的边长（图像像素）。</summary>
    private const int EmojiSize = 44;
    private static readonly Color Accent = Color.FromArgb(38, 174, 96);

    private readonly Bitmap _image;
    private readonly Rectangle _bounds;
    private readonly AnnotationStack _stack = new();
    private Bitmap _working;
    private Rectangle _imageRect;
    private double _scale = 1.0;
    /// <summary>超长图（滚动截图）的垂直视口：显示从图像第几行开始、一次显示多少行。</summary>
    private int _viewTop;
    private int _viewRows;
    private bool _pannable;

    // 绘制状态
    private AnnotationTool _tool = AnnotationTool.Rectangle;
    private Color _color = Palette[0];
    private int _width = 3;
    private int _fontSize = 26;
    private int _blockSize = 8;
    private string _emoji = EmojiArt.Items[0].Key;
    private Point? _dragStart;
    private Point _dragCurrent;
    private List<Point>? _penPoints;

    /// <summary>完成后的标注图（调用方负责 Dispose）；取消或退出时为 null。</summary>
    public Bitmap? ResultImage { get; private set; }
    /// <summary>退出后是否需要把当前图钉到桌面。</summary>
    public bool PinRequested { get; private set; }
    /// <summary>当前图片对应的屏幕区域（物理像素）；为空表示无法从工具栏再发起滚动截图。</summary>
    public Rectangle? ScreenRegion { get; }
    /// <summary>工具栏「滚动截图」的宿主回调：给定屏幕区域重新采集，返回长图或取消原因。</summary>
    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(
        System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Func<Rectangle, ScrollCaptureReply>? ScrollCapture { get; set; }
    /// <summary>滚动截图成功后交回宿主的图片（此时 DialogResult 为 Retry）；所有权归宿主。</summary>
    public Bitmap? ScrollResult { get; private set; }

    private Panel _bar = null!;
    private readonly Dictionary<AnnotationTool, IconToolButton> _toolButtons = new();
    private readonly Dictionary<string, IconToolButton> _actionButtons = new();
    private IconToolButton? _undo;
    private IconToolButton? _redo;
    private ToolOptionPopup? _popup;
    private readonly HoverTip _hoverTip;
    /// <summary>正在图片上就地编辑的文字框；同一时刻最多一个。</summary>
    private TextBox? _textEditor;

    // 画布内的短暂提示
    private string _toast = string.Empty;
    private DateTime _toastUntil;
    private readonly System.Windows.Forms.Timer _toastTimer = new() { Interval = 300 };

    /// <summary>
    /// 以已经裁剪好的截图进入编辑。image 的所有权转移给本窗体（由本窗体 Dispose）。
    /// 窗口尺寸按图片等比缩放计算，居中于当前显示器工作区。
    /// </summary>
    /// <param name="screenRegion">这张图在屏幕上的位置；传了才允许用工具栏「滚动截图」接着往下截。</param>
    public AnnotationOverlay(Bitmap image, Rectangle? screenRegion = null)
    {
        _image = image ?? throw new ArgumentNullException(nameof(image));
        ScreenRegion = screenRegion;
        _bounds = new Rectangle(0, 0, image.Width, image.Height);
        _working = AnnotationRenderer.Render(_image, _stack.Active);

        var screen = Screen.FromPoint(Cursor.Position).WorkingArea;
        int pad = UiTheme.Sx(14), gap = UiTheme.Sx(10), barH = UiTheme.Sx(40);
        int minBarW = UiTheme.Sx(600); // 容纳 7 个标注工具 + 滚动截图 + 7 个操作按钮
        double availW = Math.Max(UiTheme.Sx(80), screen.Width - pad * 2 - UiTheme.Sx(40));
        double availH = Math.Max(UiTheme.Sx(80), screen.Height - pad * 2 - gap - barH - UiTheme.Sx(40));
        _scale = Math.Min(1.0, Math.Min(availW / image.Width, availH / image.Height));
        _scale = Math.Max(0.05, _scale);

        // 超长图（滚动截图）如果按整张适配，宽度会被压成一条窄带，连字都看不清。
        // 这种情况改成"按宽度适配 + 垂直平移"，用滚轮上下查看，标注仍然是原图像素。
        if (image.Width * _scale < UiTheme.Sx(380) && image.Height * _scale >= availH - 1)
        {
            _scale = Math.Min(1.0, availW / image.Width);
            _pannable = true;
        }

        int imgW = Math.Max(1, (int)Math.Round(image.Width * _scale));
        int imgH = _pannable
            ? Math.Max(1, Math.Min((int)Math.Round(image.Height * _scale), (int)Math.Floor(availH)))
            : Math.Max(1, (int)Math.Round(image.Height * _scale));
        _viewRows = _pannable
            ? Math.Min(image.Height, Math.Max(1, (int)Math.Ceiling(imgH / _scale)))
            : image.Height;
        int barW = Math.Max(imgW, minBarW);
        // 图片与窗体左右/上边贴合，不再留深色画布黑边；底部只保留工具栏与一点下留白。
        int clientW = barW;
        int clientH = imgH + gap + barH + pad;
        _imageRect = new Rectangle((clientW - imgW) / 2, 0, imgW, imgH);

        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        ControlBox = false;
        KeyPreview = true;
        BackColor = Color.FromArgb(42, 44, 48);
        ClientSize = new Size(clientW, clientH);
        Location = new Point(
            screen.Left + Math.Max(0, (screen.Width - clientW) / 2),
            screen.Top + Math.Max(0, (screen.Height - clientH) / 2));
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);

        _toastTimer.Tick += (_, _) =>
        {
            if (_toast.Length > 0 && DateTime.Now >= _toastUntil)
            {
                _toast = string.Empty;
                Invalidate();
            }
        };
        _toastTimer.Start();
        _hoverTip = new HoverTip(this);

        BuildToolbar(new Rectangle(0, imgH + gap, barW, barH));
        SelectTool(AnnotationTool.Rectangle);
    }

    private static bool IsDrawTool(AnnotationTool tool) => tool is
        AnnotationTool.Rectangle or AnnotationTool.Ellipse or AnnotationTool.Arrow or
        AnnotationTool.Pen or AnnotationTool.Mosaic;

    /// <summary>该工具是否有二级选项面板。</summary>
    private static bool HasOptions(AnnotationTool tool) => tool is not AnnotationTool.None;

    private IconToolButton AddButton(Control row, string key, string icon, string tip, Action<IconToolButton> onClick,
        AnnotationTool? tool = null)
    {
        var b = new IconToolButton(icon)
        {
            HoverEnter = c => _hoverTip.ShowTip(c, tip),
            HoverLeave = _ => _hoverTip.HideTip()
        };
        b.Click += (_, _) => { _hoverTip.HideTip(); HideOptions(); CommitTextEditor(); onClick(b); };
        row.Controls.Add(b);
        if (tool is not null) _toolButtons[tool.Value] = b;
        else _actionButtons[key] = b;
        return b;
    }

    private static Panel Separator() => new()
    {
        Size = UiTheme.Sz(1, 20),
        BackColor = Color.FromArgb(205, 212, 220),
        Margin = new Padding(UiTheme.Sx(4), UiTheme.Sx(5), UiTheme.Sx(4), 0)
    };

    private void BuildToolbar(Rectangle barRect)
    {
        _bar = new Panel
        {
            Location = barRect.Location,
            Size = barRect.Size,
            BackColor = Color.White,
            Padding = UiTheme.Pad(5, 5, 5, 5)
        };
        Controls.Add(_bar);

        var row = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false, AutoScroll = false, Margin = Padding.Empty
        };
        _bar.Controls.Add(row);

        void Tool(AnnotationTool tool, string key, string icon, string tip) =>
            AddButton(row, key, icon, tip, b => { SelectTool(tool); ShowOptions(tool, b); }, tool);

        // 参考图顺序：矩形 / 椭圆 / 表情 / 箭头 / 画笔 / 马赛克 / 文字
        Tool(AnnotationTool.Rectangle, "rect", "annot-rect", "矩形（颜色 / 粗细）");
        Tool(AnnotationTool.Ellipse, "ellipse", "annot-ellipse", "椭圆（颜色 / 粗细）");
        Tool(AnnotationTool.Emoji, "emoji", "annot-smile", "表情（内置图案）");
        Tool(AnnotationTool.Arrow, "arrow", "annot-arrow", "箭头（颜色 / 粗细）");
        Tool(AnnotationTool.Pen, "pen", "annot-pen", "画笔（颜色 / 粗细）");
        Tool(AnnotationTool.Mosaic, "mosaic", "annot-mosaic", "马赛克（块大小 / 一键打码）");
        Tool(AnnotationTool.Text, "text", "annot-text", "文字（点击图片直接输入，回车落笔）");

        // 滚动截图不是标注工具，而是"换一种截法"：按当前区域重新连续抓屏拼成长图。
        // 放在工具组末尾自成一段，避免和颜色/粗细那类选项混在一起。
        row.Controls.Add(Separator());
        AddButton(row, "scroll", "scroll", "滚动截图（框住长页面，向下滚动自动拼接）",
            _ => BeginScrollCapture());

        row.Controls.Add(Separator());

        _undo = AddButton(row, "undo", "annot-undo", "撤销 Ctrl+Z", _ => { if (_stack.Undo()) Rebuild(); });
        _redo = AddButton(row, "redo", "annot-redo", "重做 Ctrl+Y", _ => { if (_stack.Redo()) Rebuild(); });
        AddButton(row, "copy", "annot-copy", "复制 Ctrl+C（可粘贴到微信 / 钉钉）", _ => CopyImage());
        AddButton(row, "save", "annot-save", "保存 Ctrl+S", _ => SaveImage());
        AddButton(row, "pin", "annot-pin", "置顶到桌面", _ => { PinRequested = true; Finish(); });
        AddButton(row, "cancel", "annot-close", "退出并丢弃 Esc", _ => Cancel());
        AddButton(row, "done", "annot-done", "完成 Enter", _ => Finish());

        foreach (Control c in row.Controls)
            if (c.Margin == Padding.Empty)
                c.Margin = new Padding(UiTheme.Sx(2), UiTheme.Sx(5), UiTheme.Sx(2), 0);

        // 让整排按钮在白条里居中：按钮原先从左端排起，右侧留大片空白，
        // 视觉上工具栏显得比图片短。居中后与图片左右对齐、观感更完整。
        int contentW = 0;
        foreach (Control c in row.Controls)
            contentW += c.Width + c.Margin.Left + c.Margin.Right;
        int leftPad = Math.Max(0, (row.ClientSize.Width - contentW) / 2);
        row.Padding = new Padding(leftPad, 0, 0, 0);
    }

    private void ShowToast(string text)
    {
        _toast = text;
        _toastUntil = DateTime.Now.AddSeconds(2.2);
        Invalidate();
    }

    /// <summary>点工具按钮后在下方浮出该工具的选项面板。</summary>
    private void ShowOptions(AnnotationTool tool, IconToolButton anchor)
    {
        if (!HasOptions(tool)) return;
        HideOptions();

        var popup = new ToolOptionPopup();
        _popup = popup;
        var row = new FlowLayoutPanel
        {
            Location = new Point(popup.Padding.Left, popup.Padding.Top),
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false, AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = Padding.Empty
        };
        popup.Controls.Add(row);

        if (tool == AnnotationTool.Emoji)
        {
            // 8 列 × 2 行，全部用内置矢量图案，不依赖系统 emoji 字体
            row.AutoSize = false;
            row.WrapContents = true;
            row.Size = UiTheme.Sz(292, 76);
            foreach (var item in EmojiArt.Items)
            {
                var option = new EmojiOption(item.Key, item.Name, _hoverTip)
                {
                    Selected = item.Key == _emoji
                };
                option.Click += (_, _) =>
                {
                    _emoji = item.Key;
                    HideOptions();
                    SelectTool(AnnotationTool.Emoji);
                    ShowToast($"已选「{item.Name}」，点击画面放置");
                };
                row.Controls.Add(option);
            }
            popup.ShowUnder(anchor);
            return;
        }

        string[] colorNames = ["红色", "黄色", "绿色", "蓝色", "白色", "黑色"];
        for (int i = 0; i < Palette.Length; i++)
        {
            var c = Palette[i];
            // 必须把名称复制到循环体内的局部变量：lambda 捕获 for 的 i 时循环早已结束，
            // 点击颜色按钮会拿到 i == Palette.Length 而抛 IndexOutOfRangeException。
            var name = colorNames[i];
            var dot = new DotOption(c, tipHost: _hoverTip, tip: name)
            {
                Selected = c.ToArgb() == _color.ToArgb()
            };
            dot.Click += (_, _) => { _color = c; HideOptions(); ShowToast($"已切换颜色：{name}"); };
            row.Controls.Add(dot);
        }

        if (tool == AnnotationTool.Mosaic)
        {
            row.Controls.Add(Separator());
            foreach (var size in MosaicBlocks)
            {
                var dot = new DotOption(Color.Transparent, size, _hoverTip, $"马赛克块 {size}") { Selected = size == _blockSize };
                dot.Click += (_, _) => { _blockSize = size; HideOptions(); ShowToast($"马赛克块大小 {size}"); };
                row.Controls.Add(dot);
            }
            var quick = new PopupActionButton("一键打码", _hoverTip, "对整张图片一键打码");
            quick.Click += (_, _) => { HideOptions(); QuickMosaic(); };
            row.Controls.Add(quick);
        }
        else if (tool == AnnotationTool.Text)
        {
            row.Controls.Add(Separator());
            foreach (var size in FontSizes)
            {
                var b = new IconToolButton("annot-text")
                {
                    Size = UiTheme.Sz(26, 20), Selected = size == _fontSize,
                    HoverEnter = c => _hoverTip.ShowTip(c, $"字号 {size}"),
                    HoverLeave = _ => _hoverTip.HideTip()
                };
                b.Click += (_, _) => { _fontSize = size; ApplyEditorFont(); HideOptions(); ShowToast($"字号 {size}"); };
                row.Controls.Add(b);
            }
        }
        else
        {
            row.Controls.Add(Separator());
            foreach (var w in Widths)
            {
                var wb = new WidthOption(w, _hoverTip, $"粗细 {w}") { Selected = w == _width };
                wb.Click += (_, _) => { _width = w; HideOptions(); ShowToast($"粗细 {w}"); };
                row.Controls.Add(wb);
            }
        }

        popup.ShowUnder(anchor);
    }

    private void HideOptions()
    {
        _hoverTip.HideTip();
        if (_popup is null) return;
        _popup.Close();
        _popup.Dispose();
        _popup = null;
    }

    /// <summary>对整张图一键打码，作为一条可撤销的命令压入栈。</summary>
    private void QuickMosaic()
    {
        _stack.Push(new AnnotationStroke
        {
            Tool = AnnotationTool.Mosaic, Bounds = _bounds, BlockSize = _blockSize
        });
        Rebuild();
        ShowToast($"已一键打码（块大小 {_blockSize}）");
    }

    private void SelectTool(AnnotationTool tool)
    {
        _tool = tool;
        _dragStart = null;
        Cursor = tool == AnnotationTool.None ? Cursors.Default : Cursors.Cross;
        foreach (var (t, b) in _toolButtons)
        {
            b.Selected = t == tool;
            b.ArrowVisible = HasOptions(t);
        }
        foreach (var b in _actionButtons.Values) b.Selected = false;
        if (_undo is not null)
        {
            _undo.Enabled = _stack.CanUndo;
            _redo!.Enabled = _stack.CanRedo;
        }
        Invalidate();
    }

    /// <summary>在点击位置就地放一个文字输入框：直接打字，Enter 落笔，Esc 取消。</summary>
    private void StartTextEditor(Point clientPoint)
    {
        CancelTextEditor();
        int fontPx = Math.Max(8, (int)Math.Round(_fontSize * _scale));
        int w = UiTheme.Sx(200), h = fontPx + UiTheme.Sx(12);
        // 输入框贴着落点，但不越过图片右/下边界，保证始终可见可编辑。
        var loc = new Point(
            Math.Min(clientPoint.X, Math.Max(_imageRect.X, _imageRect.Right - w)),
            Math.Min(clientPoint.Y, Math.Max(_imageRect.Y, _imageRect.Bottom - h)));
        var editor = new TextBox
        {
            Location = loc,
            Size = new Size(w, h),
            Font = new Font("Microsoft YaHei", fontPx, FontStyle.Bold, GraphicsUnit.Pixel),
            ForeColor = _color,
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle
        };
        editor.KeyDown += (_, k) =>
        {
            if (k.KeyCode == Keys.Enter) { CommitTextEditor(); k.SuppressKeyPress = true; }
            else if (k.KeyCode == Keys.Escape) { CancelTextEditor(); k.SuppressKeyPress = true; }
        };
        _textEditor = editor;
        Controls.Add(editor);
        editor.BringToFront();
        BeginInvoke(new Action(() =>
        {
            if (!editor.IsDisposed) { editor.Focus(); editor.Select(); }
        }));
    }

    /// <summary>二级面板改字号时，同步正在编辑的文字框。</summary>
    private void ApplyEditorFont()
    {
        if (_textEditor is null) return;
        int fontPx = Math.Max(8, (int)Math.Round(_fontSize * _scale));
        var old = _textEditor.Font;
        _textEditor.Font = new Font("Microsoft YaHei", fontPx, FontStyle.Bold, GraphicsUnit.Pixel);
        _textEditor.Height = fontPx + UiTheme.Sx(12);
        old.Dispose();
    }

    /// <summary>把输入框里的文字作为一条可撤销命令落到图上。</summary>
    private void CommitTextEditor()
    {
        var editor = _textEditor;
        if (editor is null) return;
        _textEditor = null;
        var text = editor.Text.Trim();
        var color = editor.ForeColor;
        var origin = ToImage(editor.Location);
        editor.Dispose();
        if (text.Length == 0) return;
        _stack.Push(new AnnotationStroke
        {
            Tool = AnnotationTool.Text, Color = color, Text = text,
            FontSize = _fontSize, Bounds = new Rectangle(origin, Size.Empty)
        });
        Rebuild();
    }

    private void CancelTextEditor()
    {
        if (_textEditor is null) return;
        var editor = _textEditor;
        _textEditor = null;
        editor.Dispose();
    }

    private void Rebuild()
    {
        _working.Dispose();
        _working = AnnotationRenderer.Render(_image, _stack.Active);
        SelectTool(_tool);
    }

    /// <summary>窗口客户区坐标 → 图像像素坐标（显示可能缩小、超长图还带垂直偏移）。</summary>
    private Point ToImage(Point p) => new(
        (int)Math.Round((p.X - _imageRect.X) / _scale),
        (int)Math.Round((p.Y - _imageRect.Y) / _scale) + _viewTop);

    private bool OnImage(Point p) => p.X >= _imageRect.X && p.Y >= _imageRect.Y &&
                                     p.X < _imageRect.Right && p.Y < _imageRect.Bottom;

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        HideOptions();
        // 正在图上打字时，点到别处先落笔，再按新位置继续操作。
        if (_textEditor is not null && !_textEditor.Bounds.Contains(e.Location)) CommitTextEditor();
        if (!OnImage(e.Location)) return;
        if (e.Button == MouseButtons.Right)
        {
            if (_stack.Undo()) Rebuild();
            return;
        }
        if (e.Button != MouseButtons.Left) return;

        var p = ToImage(e.Location);
        if (_tool == AnnotationTool.None) return;

        if (_tool == AnnotationTool.Text)
        {
            StartTextEditor(e.Location);
            return;
        }
        if (_tool == AnnotationTool.Emoji)
        {
            _stack.Push(new AnnotationStroke
            {
                Tool = AnnotationTool.Emoji, Text = _emoji, FontSize = EmojiSize,
                Bounds = new Rectangle(p.X, p.Y, 0, 0)
            });
            Rebuild();
            return;
        }
        if (IsDrawTool(_tool))
        {
            _dragStart = p;
            _dragCurrent = p;
            if (_tool == AnnotationTool.Pen) _penPoints = [p];
        }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (!_pannable) return;
        var maxTop = Math.Max(0, _image.Height - _viewRows);
        var step = Math.Max(1, _viewRows / 8);
        var next = Math.Clamp(_viewTop - Math.Sign(e.Delta) * step, 0, maxTop);
        if (next == _viewTop) return;
        _viewTop = next;
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragStart is null) return;
        _dragCurrent = ToImage(e.Location);
        if (_tool == AnnotationTool.Pen)
            _penPoints?.Add(_dragCurrent);
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_dragStart is null || e.Button != MouseButtons.Left) return;
        var start = _dragStart.Value;
        _dragStart = null;

        if (_tool == AnnotationTool.Pen)
        {
            var pts = _penPoints ?? [];
            _penPoints = null;
            if (pts.Count >= 2)
                _stack.Push(new AnnotationStroke { Tool = AnnotationTool.Pen, Color = _color, Width = _width, Points = pts.ToArray() });
        }
        else
        {
            var rect = AnnotationGeometry.Normalize(start, _dragCurrent);
            // 箭头允许纯水平 / 垂直拖动：只需两点间有足够位移，不要求宽高都 ≥2。
            // 矩形 / 椭圆 / 马赛克仍需面状区域，避免误点生成 1px 细线。
            var distance = Math.Abs(_dragCurrent.X - start.X) + Math.Abs(_dragCurrent.Y - start.Y);
            var meaningful = _tool == AnnotationTool.Arrow ? distance >= 2 : rect.Width >= 2 && rect.Height >= 2;
            if (meaningful)
                _stack.Push(new AnnotationStroke
                {
                    Tool = _tool, Color = _color, Width = _width,
                    Bounds = rect, Points = _tool == AnnotationTool.Arrow ? [start, _dragCurrent] : [],
                    BlockSize = _blockSize
                });
        }
        Rebuild();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        // 正在图上打字时，按键交给输入框处理：回车落笔、Esc 取消；
        // 不能让窗体的 Enter=完成 / Ctrl+Z=撤销抢走输入。
        if (_textEditor is not null)
        {
            if (e.KeyCode == Keys.Escape) { CancelTextEditor(); e.SuppressKeyPress = true; }
            return;
        }
        if (e.Control && e.KeyCode == Keys.Z)
        {
            if (e.Shift ? _stack.Redo() : _stack.Undo()) Rebuild();
            e.SuppressKeyPress = true;
        }
        else if (e.Control && e.KeyCode == Keys.Y)
        {
            if (_stack.Redo()) Rebuild();
            e.SuppressKeyPress = true;
        }
        else if (e.Control && e.KeyCode == Keys.C)
        {
            CopyImage();
            e.SuppressKeyPress = true;
        }
        else if (e.Control && e.KeyCode == Keys.S)
        {
            SaveImage();
            e.SuppressKeyPress = true;
        }
        else if (e.KeyCode == Keys.Enter) { Finish(); e.SuppressKeyPress = true; }
        else if (e.KeyCode == Keys.Escape) { Cancel(); e.SuppressKeyPress = true; }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);

        if (_pannable)
        {
            var source = new Rectangle(0, _viewTop, _working.Width,
                Math.Min(_viewRows, _working.Height - _viewTop));
            g.DrawImage(_working, _imageRect, source, GraphicsUnit.Pixel);
        }
        else
        {
            g.DrawImage(_working, _imageRect);
        }
        using (var frame = new Pen(Accent, UiTheme.PenW(2)))
            g.DrawRectangle(frame, _imageRect.X, _imageRect.Y,
                Math.Max(0, _imageRect.Width - 1), Math.Max(0, _imageRect.Height - 1));

        if (_pannable)
        {
            var maxTop = Math.Max(0, _image.Height - _viewRows);
            if (maxTop > 0)
            {
                var track = _imageRect.Height;
                var thumb = Math.Max(UiTheme.Sx(24), (int)(track * (double)_viewRows / _image.Height));
                var thumbY = _imageRect.Y + (int)((track - thumb) * (double)_viewTop / maxTop);
                using var brush = new SolidBrush(Color.FromArgb(72, 255, 255, 255));
                g.FillRectangle(brush, _imageRect.Right - UiTheme.Sx(5), thumbY, UiTheme.Sx(3), thumb);
            }
        }

        if (_dragStart is not null && IsDrawTool(_tool))
        {
            var preview = new AnnotationStroke
            {
                Tool = _tool, Color = _color, Width = _width,
                Bounds = AnnotationGeometry.Normalize(_dragStart.Value, _dragCurrent),
                Points = _tool == AnnotationTool.Arrow ? [_dragStart.Value, _dragCurrent] : [],
                BlockSize = _blockSize
            };
            if (_tool == AnnotationTool.Pen && _penPoints is { Count: > 0 })
                preview = new AnnotationStroke
                {
                    Tool = AnnotationTool.Pen, Color = _color, Width = _width,
                    Points = _penPoints.ToArray()
                };
            var state = g.Save();
            g.SetClip(_imageRect);
            // 预览坐标是图像像素：先缩放到显示比例，再叠加超长图的垂直偏移。
            g.TranslateTransform(_imageRect.X, _imageRect.Y - (float)(_viewTop * _scale));
            g.ScaleTransform((float)_scale, (float)_scale);
            AnnotationRenderer.Draw(g, _working, preview);
            g.Restore(state);
        }

        if (_toast.Length > 0 && DateTime.Now < _toastUntil) DrawToast(g);
    }

    private void DrawToast(Graphics g)
    {
        var font = UiTheme.FontAt(13);
        var size = TextRenderer.MeasureText(_toast, font);
        int w = size.Width + UiTheme.Sx(24);
        int h = size.Height + UiTheme.Sx(12);
        var box = new Rectangle((ClientSize.Width - w) / 2, _bar.Top - h - UiTheme.Sx(10), w, h);
        UiTheme.FillSurface(g, box, UiTheme.RadiusMedium,
            Color.FromArgb(238, 18, 24, 34), Color.FromArgb(140, 110, 130, 160));
        TextRenderer.DrawText(g, _toast, font, box, Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    /// <summary>成品就是整张传入的图像（已裁剪），不再做二次裁剪。</summary>
    private Bitmap CreateResult() => (Bitmap)_working.Clone();

    private void SaveImage()
    {
        using var dlg = new SaveFileDialog
        {
            Title = "保存标注截图",
            Filter = "PNG 图片|*.png|JPG 图片|*.jpg",
            DefaultExt = ".png",
            FileName = ScreenCapture.BuildFileName(DateTime.Now, ".png"),
            OverwritePrompt = true
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var id = dlg.FilterIndex == 2 ? "jpg" : "png";
            using var result = CreateResult();
            System.IO.File.WriteAllBytes(dlg.FileName, ScreenCapture.Encode(result, id));
            ShowToast($"已保存：{dlg.FileName}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"保存失败：{ex.Message}", "屏幕截图", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void CopyImage()
    {
        try
        {
            using var result = CreateResult();
            ShowToast(ClipboardUtil.TryCopyImage(result)
                ? $"已复制 {ScreenCapture.DescribeSize(result.Size)}，可粘贴到微信 / 钉钉"
                : "复制失败：剪贴板被其它程序占用，请改用保存");
        }
        catch (Exception ex)
        {
            ShowToast($"复制失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 工具栏「滚动截图」：先把自己藏起来（否则采集到的就是编辑器盖住的那块），
    /// 交给宿主按同一块屏幕区域连续抓屏；成功后以 Retry 结束本次编辑，由宿主带着长图重开编辑。
    /// 取消则原样恢复，已画的标注不会丢。
    /// </summary>
    private void BeginScrollCapture()
    {
        if (ScrollCapture is null || ScreenRegion is not { } region)
        {
            ShowToast("这张图没有对应的屏幕区域，无法继续滚动截图");
            return;
        }

        Hide();
        // 编辑器刚藏起来时那块区域还是旧像素，等桌面重绘完再抓，否则首帧会带一圈白边/阴影。
        Application.DoEvents();
        Thread.Sleep(80);
        var reply = ScrollCapture(region);

        if (reply.Image is null)
        {
            if (IsDisposed) return;
            Show();
            Activate();
            ShowToast(reply.Message.Length > 0 ? reply.Message : "已取消滚动截图");
            return;
        }

        ScrollResult = reply.Image;
        DialogResult = DialogResult.Retry;
        Close();
    }

    private void Finish()
    {
        HideOptions();
        ResultImage = CreateResult();
        DialogResult = DialogResult.OK;
        Close();
    }

    private void Cancel()
    {
        HideOptions();
        ResultImage?.Dispose();
        ResultImage = null;
        DialogResult = DialogResult.Cancel;
        Close();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _toastTimer.Stop();
            _toastTimer.Dispose();
            HideOptions();
            CancelTextEditor();
            _hoverTip.Dispose();
            _image.Dispose();
            _working.Dispose();
        }
        base.Dispose(disposing);
    }
}
