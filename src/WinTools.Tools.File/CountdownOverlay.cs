using System.Drawing;
using System.Windows.Forms;
using WinTools.Core.Ui;

namespace WinTools.Tools.Files;

/// <summary>
/// 延时截图的倒计时浮层：顶层小窗显示剩余秒数，计时到 0 返回 OK，Esc 取消。
/// 宿主窗口在倒计时开始前已隐藏，本浮层是屏幕上唯一可见的工具窗体。
/// 坐标/尺寸用物理像素（与 Screen 坐标同域），字号走 UiTheme.FONT 阶梯按 DPI 缩放。
/// </summary>
public sealed class CountdownOverlay : Form
{
    private readonly System.Windows.Forms.Timer _timer;
    private readonly int _total;
    private int _remaining;
    private bool _finished;

    public CountdownOverlay(int seconds)
    {
        _total = Math.Clamp(seconds, 1, ScreenCapture.MaxDelaySeconds);
        _remaining = _total;

        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        KeyPreview = true;
        // 必须先声明支持透明背景，再赋 Transparent，否则 WinForms 构造时抛
        // ArgumentException「控件不支持透明的背景色」，导致延时截图入口整体不可用。
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Size = UiTheme.Sz(280, 118);

        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        Location = new Point(area.X + (area.Width - Width) / 2, area.Y + UiTheme.Sx(64));


        _timer = new System.Windows.Forms.Timer { Interval = 1000 };
        _timer.Tick += (_, _) =>
        {
            _remaining--;
            if (_remaining <= 0) Finish(DialogResult.OK);
            else Invalidate();
        };
        _timer.Start();
    }

    private void Finish(DialogResult result)
    {
        if (_finished) return;
        _finished = true;
        _timer.Stop();
        DialogResult = result;
        Close();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Escape) Finish(DialogResult.Cancel);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);
        UiTheme.FillSurface(g, rect, UiTheme.RadiusCard,
            Color.FromArgb(240, 14, 22, 34), Color.FromArgb(200, 90, 120, 165));

        var number = _remaining.ToString();
        TextRenderer.DrawText(g, number, UiTheme.FontAt(36, bold: true),
            new Rectangle(0, UiTheme.Sx(6), Width, UiTheme.Sx(48)), Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        var tip = $"秒后截取全屏（共 {_total} 秒）  ·  Esc 取消";
        TextRenderer.DrawText(g, tip, UiTheme.FontAt(12),
            new Rectangle(0, UiTheme.Sx(66), Width, UiTheme.Sx(26)), UiTheme.TextSecondary,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer.Dispose();
        base.Dispose(disposing);
    }
}
