using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace WinTools.Core.Ui;

/// <summary>
/// 语义色反馈条：统一"矢量图标 + 一句话原因 + 建议"的就地提示，不弹窗。
/// 成功绿 / 错误红 / 警告黄 / 信息灰，颜色 + 图标 + 文字三通道（无障碍）。
/// 整条自绘（圆角表面 + 左侧语义色条），因此没有 WinForms 直角硬边框。
/// 放在工具根容器里，Dock = Top 或 Bottom 均可。
/// </summary>
public sealed class FeedbackBar : Panel
{
    // 设计像素：行高 / 上下留白 / 最小高 / 文字左缩进
    private const int LineHeight = 20;
    private const int VerticalPadding = 22;
    private const int MinHeight = 40;
    private const int TextLeft = 44;

    private Kind _kind = Kind.Info;
    private string _message = string.Empty;
    private int _fittedFor = -1;

    public FeedbackBar()
    {
        Height = 0;
        Visible = false;
        Margin = UiTheme.Pad(0, 10, 0, 6);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }

    /// <summary>设置反馈内容与语义色，自动显示；高度按实际换行行数自适应。</summary>
    public void Show(Kind kind, string message)
    {
        _kind = kind;
        _message = message ?? string.Empty;
        _fittedFor = -1;
        FitHeight();
        Visible = true;
        Invalidate();
    }

    public void Clear()
    {
        Visible = false;
        Height = 0;
        _message = string.Empty;
        _fittedFor = -1;
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if (Visible && Width > 0 && Width != _fittedFor) FitHeight();
    }

    /// <summary>
    /// 按当前宽度量一次真实换行行数。用 '\n' 计数不行：一句长文案会被 WordBreak 折成两行，
    /// 条子还是单行高度，第二行就被底边切掉半截。
    /// </summary>
    private void FitHeight()
    {
        _fittedFor = Width;
        int one = TextRenderer.MeasureText("国", UiTheme.Body, Size.Empty, TextFormatFlags.NoPadding).Height;
        if (one <= 0) one = UiTheme.Sx(LineHeight);

        int lines = 1;
        int left = UiTheme.Sx(TextLeft);
        int avail = Width - left - UiTheme.Sx(14);
        if (_message.Length > 0 && avail > UiTheme.Sx(40))
        {
            var wrapped = TextRenderer.MeasureText(_message, UiTheme.Body, new Size(avail, one * 64),
                TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
            lines = Math.Max(1, (int)Math.Ceiling(wrapped.Height / (double)one));
        }
        Height = Math.Max(UiTheme.Sx(MinHeight), lines * one + UiTheme.Sx(VerticalPadding));
    }

    private (Color Bg, Color Fg, string Icon) Palette() => _kind switch
    {
        Kind.Success => (UiTheme.SuccessBg, UiTheme.Success, "check"),
        Kind.Error => (UiTheme.ErrorBg, UiTheme.Error, "error"),
        Kind.Warn => (UiTheme.WarnBg, UiTheme.Warn, "warning"),
        _ => (UiTheme.InfoBg, UiTheme.TextSecondary, "info"),
    };

    public enum Kind { Info, Success, Warn, Error }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (!Visible || string.IsNullOrEmpty(_message)) return;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var (bg, fg, icon) = Palette();
        var rect = new Rectangle(0, 0, Width - 1, Height - 1);

        UiTheme.FillSurface(g, rect, UiTheme.RadiusLarge, bg, UiTheme.Blend(fg, bg, 0.42f));

        // 左侧语义色条
        var stripe = new Rectangle(0, 0, UiTheme.Sx(4), Height - 1);
        using (var sp = UiTheme.RoundedPath(new Rectangle(0, 0, UiTheme.Sx(10), Height - 1), UiTheme.RadiusLarge))
        {
            g.SetClip(stripe);
            using var b = new SolidBrush(fg);
            g.FillPath(b, sp);
            g.ResetClip();
        }

        int side = UiTheme.Sx(18);
        IconFactory.Draw(g, icon, new RectangleF(UiTheme.Sx(16), (Height - side) / 2f, side, side), fg);

        int left = UiTheme.Sx(TextLeft);
        var textRect = new Rectangle(left, UiTheme.Sx(6),
            Math.Max(0, Width - left - UiTheme.Sx(14)), Math.Max(0, Height - UiTheme.Sx(12)));
        TextRenderer.DrawText(g, _message, UiTheme.Body, textRect, fg,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak |
            TextFormatFlags.NoPadding);
    }
}
