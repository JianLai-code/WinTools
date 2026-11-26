using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;
using WinTools.Core.Ui;

namespace WinTools.Tools.Files;

/// <summary>
/// 滚动截图的采集浮条：用户框好区域后，它贴在选区外侧定时抓取同一块屏幕，
/// 由 <see cref="ScrollStitcher"/> 决定每帧有多少是新内容。用户只管往下滚，完成时得到一张长图。
///
/// 不抢焦点（WS_EX_NOACTIVATE）：键盘/滚轮始终留在被截的程序里，不会因为点了浮条就打断滚动。
/// 浮条优先放在选区下方，其次上方；实在没地方（比如选区占满整屏）才压在选区上，
/// 此时每次抓屏前把自身透明度归零，避免把自己拍进长图。
/// </summary>
public sealed class ScrollCaptureForm : Form
{
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private readonly Rectangle _region;
    private readonly ScrollStitcher _stitcher;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 300 };
    private readonly FluentButton _done;
    private readonly FluentButton _cancel;

    private bool _overlapsRegion;
    private bool _capturing;
    /// <summary>显示前的活动窗口：个别系统上 WS_EX_NOACTIVATE 仍会被抢焦点，OnShown 里还回去。</summary>
    private IntPtr _previousForeground;
    private bool _finished;
    private string _hint = "向下滚动页面，内容会自动拼接";

    /// <summary>完成后的长图（调用方负责 Dispose）；取消时为 null。</summary>
    public Bitmap? Result { get; private set; }

    /// <summary>给用户看的一句话结果，例如"已拼接 960 × 4230 px"。</summary>
    public string FinishMessage { get; private set; } = "";

    public ScrollCaptureForm(Rectangle region)
    {
        if (region.Width <= 0 || region.Height <= 0) throw new ArgumentException("选区为空。", nameof(region));
        _region = region;
        _stitcher = new ScrollStitcher(region.Width);

        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        ControlBox = false;
        BackColor = UiTheme.Surface;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint, true);

        _done = UiTheme.Button("完成", "check", UiTheme.ButtonKind.Primary, 84);
        _done.Click += (_, _) => Finish();
        _cancel = UiTheme.Button("取消", UiTheme.ButtonKind.Subtle, 64);
        _cancel.Click += (_, _) => Cancel();

        Controls.Add(_done);
        Controls.Add(_cancel);

        // 尺寸最后设置：OnSizeChanged 会立刻排版按钮，此时控件必须已存在。
        ClientSize = UiTheme.Sz(452, 64);
        PlaceNearRegion();
        _timer.Tick += (_, _) => OnTick();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WsExNoActivate | WsExToolWindow;
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        _previousForeground = GetForegroundWindow();
        base.OnHandleCreated(e);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        // 有些系统/远程会话里，即使带 WS_EX_NOACTIVATE，显示顶层窗口仍会夺走前台；
        // 抢了就把焦点还给用户原来在操作的窗口，滚轮/键盘才能继续作用在页面上。
        if (_previousForeground != IntPtr.Zero && GetForegroundWindow() == Handle)
            SetForegroundWindow(_previousForeground);
        // 立刻抓一帧当底图：用户往往在框选后马上就开始滚动。
        BeginInvoke(new Action(OnTick));
        _timer.Start();
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if (Width <= 0 || Height <= 0) return;
        UiTheme.Round(this, UiTheme.RadiusLarge);
        LayoutButtons();
    }

    private void LayoutButtons()
    {
        var pad = UiTheme.Sx(12);
        var gap = UiTheme.Sx(8);
        var y = (Height - _done.Height) / 2;
        _done.Location = new Point(Width - pad - _done.Width, y);
        _cancel.Location = new Point(_done.Left - gap - _cancel.Width, y);
    }

    /// <summary>贴在选区外侧；四周都没空间时压在选区底部，并标记为需要在抓屏时隐藏自己。</summary>
    private void PlaceNearRegion()
    {
        var work = Screen.FromRectangle(_region).WorkingArea;
        var gap = UiTheme.Sx(10);
        var edge = UiTheme.Sx(8);

        var x = Math.Clamp(_region.X + (_region.Width - Width) / 2,
            work.Left + edge, Math.Max(work.Left + edge, work.Right - Width - edge));

        int y;
        if (_region.Bottom + gap + Height <= work.Bottom) y = _region.Bottom + gap;
        else if (_region.Top - gap - Height >= work.Top) y = _region.Top - gap - Height;
        else y = Math.Clamp(_region.Bottom - Height - gap,
            work.Top + edge, Math.Max(work.Top + edge, work.Bottom - Height - edge));

        Location = new Point(x, y);
        _overlapsRegion = _region.IntersectsWith(new Rectangle(x, y, Width, Height));
    }

    private void OnTick()
    {
        if (_finished || _capturing || IsDisposed) return;
        _capturing = true;
        Bitmap? frame = null;
        try
        {
            frame = Grab();
            var result = _stitcher.Add(frame);
            Apply(result);
        }
        catch (Exception ex)
        {
            Stop("滚动截图中断：" + ex.Message);
        }
        finally
        {
            frame?.Dispose();
            _capturing = false;
        }
    }

    private Bitmap Grab()
    {
        // 浮条压在选区里时必须先把自己藏掉；Opacity 归零是分层窗口整体透明，
        // 不改变布局也不会触发重排，比 Hide/Show 抖动小。
        if (_overlapsRegion)
        {
            Opacity = 0;
            Application.DoEvents();
            Thread.Sleep(24);
        }
        try
        {
            var bmp = new Bitmap(_region.Width, _region.Height, PixelFormat.Format32bppArgb);
            try
            {
                using var g = Graphics.FromImage(bmp);
                g.CopyFromScreen(_region.X, _region.Y, 0, 0, _region.Size, CopyPixelOperation.SourceCopy);
                return bmp;
            }
            catch
            {
                bmp.Dispose();
                throw;
            }
        }
        finally
        {
            if (_overlapsRegion) Opacity = 1;
        }
    }

    private void Apply(StitchResult result)
    {
        switch (result.Status)
        {
            case StitchStatus.Appended:
                _hint = _stitcher.Frames <= 1
                    ? "向下滚动页面，内容会自动拼接"
                    : "继续滚动；到底后点「完成」";
                break;
            case StitchStatus.NoNewContent:
                _hint = _stitcher.Height > 0 ? "等待滚动…" : "正在抓取首屏…";
                break;
            case StitchStatus.NoOverlap:
                _hint = "这一帧对不上：滚动太快，或选区里有动态内容";
                break;
            case StitchStatus.HeightLimit:
                Stop("已达到最大拼接高度，自动结束。");
                return;
        }
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);
        UiTheme.FillSurface(g, new Rectangle(0, 0, Width, Height), UiTheme.RadiusLarge,
            UiTheme.Surface, UiTheme.Border);

        var pad = UiTheme.Sx(16);
        var textWidth = Math.Max(UiTheme.Sx(40), _cancel.Left - pad - UiTheme.Sx(8));
        var size = _stitcher.Height > 0
            ? $"{_stitcher.Width} × {_stitcher.Height} px"
            : "准备中";
        var titleRect = new Rectangle(pad, UiTheme.Sx(10), textWidth, UiTheme.Sx(20));
        TextRenderer.DrawText(g, $"滚动截图 · {size}", UiTheme.FontAt(13, bold: true), titleRect,
            UiTheme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
            TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);

        var hintRect = new Rectangle(pad, UiTheme.Sx(32), textWidth, UiTheme.Sx(20));
        var warn = _hint.StartsWith("这一帧", StringComparison.Ordinal);
        TextRenderer.DrawText(g, _hint, UiTheme.FontAt(12), hintRect,
            warn ? UiTheme.Warn : UiTheme.Muted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
            TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
    }

    private void Finish()
    {
        try
        {
            var image = _stitcher.Compose();
            Result = image;
            Stop($"已拼接 {image.Width} × {image.Height} px");
        }
        catch (InvalidOperationException)
        {
            Stop("还没有抓到画面，已取消。", cancel: true);
        }
    }

    private void Cancel() => Stop("已取消滚动截图。", cancel: true);

    /// <summary>停下来：有内容就以 OK 结束并带走长图，否则按取消处理。</summary>
    private void Stop(string message, bool cancel = false)
    {
        if (_finished) return;
        _finished = true;
        _timer.Stop();
        FinishMessage = message;
        DialogResult = cancel || Result is null ? DialogResult.Cancel : DialogResult.OK;
        Close();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _finished = true;
        _timer.Stop();
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Stop();
            _timer.Dispose();
            _stitcher.Dispose();
        }
        base.Dispose(disposing);
    }
}
