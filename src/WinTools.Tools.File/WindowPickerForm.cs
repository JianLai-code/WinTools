using System.Drawing;
using System.Windows.Forms;
using WinTools.Core;
using WinTools.Core.Ui;

namespace WinTools.Tools.Files;

/// <summary>
/// 桌面窗口选择器：一个小浮层，列出桌面上打开的窗口（微信、浏览器、任务栏……），
/// 双击即可按该窗口的范围截图。
///
/// 做成浮层而不是常驻在工具页里，是因为工具页纵向空间有限：常驻表格会把
/// 「截图预览」卡压扁，而用户真正高频用的是"截图时自动识别"，浮层按需弹出更合适。
/// </summary>
public sealed class WindowPickerForm : Form
{
    private readonly FluentListView _list;
    private readonly Label _title;
    private readonly Button _refresh;

    /// <summary>用户选中的窗口。</summary>
    public WindowInfo? Picked { get; private set; }

    /// <summary>当前列出的窗口数量。</summary>
    public int Count => _list.Items.Count;

    /// <summary>列表控件（供探针检查渲染结果）。</summary>
    public ListView List => _list;

    public WindowPickerForm(IReadOnlyList<WindowInfo> windows)
    {
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = UiTheme.Panel;
        Padding = UiTheme.Pad(1, 1, 1, 1);

        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);

        var root = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Panel, Padding = UiTheme.Pad(14, 12, 14, 12) };

        _title = new Label
        {
            Text = "桌面窗口",
            Dock = DockStyle.Top,
            Height = UiTheme.Sx(26),
            Font = UiTheme.Bold,
            ForeColor = UiTheme.Text,
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleLeft
        };

        _refresh = new FluentButton
        {
            Text = "刷新",
            IconName = "refresh",
            Variant = UiTheme.ButtonKind.Subtle,
            Size = UiTheme.Sz(84, 28),
            Radius = UiTheme.RadiusMedium,
            Anchor = AnchorStyles.Right | AnchorStyles.Top
        };
        _refresh.Click += (_, _) => Reload();

        _list = new FluentListView { Dock = DockStyle.Fill };
        _list.Columns.Add("应用", UiTheme.Sx(110));
        _list.Columns.Add("窗口标题", UiTheme.Sx(260));
        _list.Columns.Add("尺寸", UiTheme.Sx(120));
        _list.DoubleClick += (_, _) => PickSelected();

        var hint = new Label
        {
            Text = "双击某个窗口即可按它的范围截图 · Esc 关闭",
            Dock = DockStyle.Bottom,
            Height = UiTheme.Sx(24),
            Font = UiTheme.FontAt(12),
            ForeColor = UiTheme.Muted,
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleLeft
        };

        // Dock 布局按 z 序从高到低分配：后加的先占空间。
        // 顺序必须是 Fill(列表) → Bottom(提示) → Top(标题栏)，
        // 否则标题栏会被列表盖住，或列表第一行被标题栏压住。
        var head = new Panel { Dock = DockStyle.Top, Height = UiTheme.Sx(34), BackColor = Color.Transparent };
        head.Controls.Add(_title);
        head.Controls.Add(_refresh);
        _title.Dock = DockStyle.Fill;

        root.Controls.Add(_list);
        root.Controls.Add(hint);
        root.Controls.Add(head);

        Controls.Add(root);
        Resize += (_, _) => UiTheme.Round(this, UiTheme.RadiusCard);
        UiTheme.Round(this, UiTheme.RadiusCard);

        SetWindows(windows);
        ClientSize = UiTheme.Sz(540, 300);
    }

    /// <summary>重新枚举并填充窗口列表。</summary>
    public void Reload() => SetWindows(ScreenshotRunner.ListWindows(SystemInformation.VirtualScreen));

    /// <summary>填充窗口列表。</summary>
    public void SetWindows(IReadOnlyList<WindowInfo> windows)
    {
        _list.BeginUpdate();
        try
        {
            _list.Items.Clear();
            foreach (var w in windows)
            {
                var item = new ListViewItem(w.AppName);
                item.SubItems.Add(w.Title);
                item.SubItems.Add($"{w.Bounds.Width}×{w.Bounds.Height}");
                item.Tag = w;
                _list.Items.Add(item);
            }
        }
        finally
        {
            _list.EndUpdate();
        }
        _title.Text = windows.Count > 0 ? $"桌面窗口 · {windows.Count} 个" : "桌面窗口 · 没有可截图的窗口";
    }

    private void PickSelected()
    {
        if (_list.SelectedItems.Count == 0) return;
        if (_list.SelectedItems[0].Tag is not WindowInfo w) return;
        Picked = w;
        Close();
    }

    protected override bool ShowWithoutActivation => true;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Escape) Close();
        else if (e.KeyCode == Keys.Enter) PickSelected();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        UiTheme.DrawRoundedBorder(e.Graphics, new Rectangle(0, 0, Width - 1, Height - 1),
            UiTheme.RadiusCard, UiTheme.Border);
    }
}
