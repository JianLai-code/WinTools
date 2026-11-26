using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using WinTools.Core.Ui;

namespace WinTools.Tools.Files;

/// <summary>
/// 截图结果提示条：热键触发时主窗口是隐藏的，没有可写反馈的界面，
/// 用一条自动消失的浮条告诉用户"复制成功 / 已取消 / 剪贴板被占用"。
/// 不抢焦点（WS_EX_NOACTIVATE），所以不会打断用户正在操作的程序。
/// </summary>
internal sealed class CaptureToast : Form
{
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;

    private readonly string _message;
    private readonly System.Windows.Forms.Timer _timer = new();

    private CaptureToast(string message, int milliseconds)
    {
        _message = message;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(32, 36, 43);
        DoubleBuffered = true;

        var textSize = TextRenderer.MeasureText(message, UiTheme.Body, new Size(UiTheme.Sx(560), UiTheme.Sx(80)),
            TextFormatFlags.NoPadding | TextFormatFlags.WordBreak);
        Size = new Size(Math.Min(textSize.Width + UiTheme.Sx(36), UiTheme.Sx(600)), UiTheme.Sx(42));

        var screen = Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new Point(screen.Right - Width - UiTheme.Sx(24), screen.Bottom - Height - UiTheme.Sx(24));

        _timer.Interval = Math.Max(800, milliseconds);
        _timer.Tick += (_, _) => Close();
        Click += (_, _) => Close();
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

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _timer.Start();
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if (Width <= 0 || Height <= 0) return;
        using var path = UiTheme.RoundedPath(new Rectangle(0, 0, Width, Height), UiTheme.Sx(10));
        Region = new Region(path);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var background = new SolidBrush(Color.FromArgb(32, 36, 43));
        using var path = UiTheme.RoundedPath(new Rectangle(0, 0, Width, Height), UiTheme.Sx(10));
        g.FillPath(background, path);
        TextRenderer.DrawText(g, _message, UiTheme.Body,
            new Rectangle(UiTheme.Sx(18), 0, Width - UiTheme.Sx(36), Height),
            Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
            TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Stop();
            _timer.Dispose();
        }
        base.Dispose(disposing);
    }

    /// <summary>弹出一条自动消失的提示。空消息直接忽略；任何异常都不该影响截图主流程。</summary>
    public static void Show(string message, int milliseconds = 2000)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        try
        {
            new CaptureToast(message, milliseconds).Show();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"截图提示条显示失败: {ex.Message}");
        }
    }
}
