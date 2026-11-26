using System.Drawing;
using System.Windows.Forms;
using WinTools.Core;

namespace WinTools.Tools.Files;

/// <summary>
/// 置顶桌面浮窗：把一张图以无边框AlwaysOnTop窗体贴在桌面上。
/// 拖动移动、滚轮缩放、右键菜单关闭/复制/还原大小；不抢焦点（ShowWithoutActivation）。
/// </summary>
public sealed class PinnedImageForm : Form
{
    private readonly Bitmap _image;
    private double _scale = 1.0;
    private const double MinScale = 0.15;
    private const double MaxScale = 4.0;
    private Point _dragOrigin;
    private bool _dragging;

    /// <summary>图片快照由调用方传入；本窗体持有并负责 Dispose。</summary>
    public PinnedImageForm(Bitmap image, Point? location = null)
    {
        _image = image;
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        ControlBox = false;
        BackColor = Color.FromArgb(24, 26, 32);
        Text = "WinTools 贴图";

        int w = Math.Min(_image.Width, Screen.PrimaryScreen?.WorkingArea.Width ?? 1200);
        int h = (int)Math.Round(_image.Height * (w / (double)_image.Width));
        w = Math.Max(120, w);
        h = Math.Max(80, Math.Min(h, 900));
        ClientSize = new Size(w, h);

        if (location is null)
        {
            var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
            Location = new Point(area.X + area.Width - w - 40, area.Y + 40);
        }
        else Location = location.Value;

        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);

        var menu = new ContextMenuStrip();
        menu.Items.Add("复制图片", null, (_, _) => ClipboardUtil.TryCopyImage(_image));
        menu.Items.Add("还原大小", null, (_, _) => { _scale = 1.0; ApplyScale(); });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("关闭", null, (_, _) => Close());
        ContextMenuStrip = menu;
    }

    protected override bool ShowWithoutActivation => true;

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        _dragging = true;
        _dragOrigin = new Point(e.X - Left, e.Y - Top);
        Cursor = Cursors.SizeAll;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging) Location = new Point(MousePosition.X - _dragOrigin.X, MousePosition.Y - _dragOrigin.Y);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left) return;
        _dragging = false;
        Cursor = Cursors.Default;
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        double factor = e.Delta > 0 ? 1.15 : 1 / 1.15;
        _scale = Math.Clamp(_scale * factor, MinScale, MaxScale);
        ApplyScale();
    }

    private void ApplyScale()
    {
        int w = Math.Max(80, (int)Math.Round(_image.Width * _scale));
        int h = Math.Max(60, (int)Math.Round(_image.Height * _scale));
        ClientSize = new Size(w, h);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        e.Graphics.DrawImage(_image, ClientRectangle);
        using var pen = new Pen(Color.FromArgb(180, 120, 140, 170));
        e.Graphics.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _image.Dispose();
        base.Dispose(disposing);
    }
}
