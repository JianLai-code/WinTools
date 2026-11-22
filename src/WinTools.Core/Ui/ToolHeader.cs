using System.Drawing;
using System.Windows.Forms;

namespace WinTools.Core.Ui;

/// <summary>
/// 工具页统一头部：图标块 + 页面标题 + 灰色描述 + 底部分隔线。
/// 每个工具页第一屏结构一致；宿主打开工具时会把 ToolMeta 的图标与色相注入 IconName/Accent，
/// 让标题块和首页卡片用的是同一套视觉。
///
/// 子控件用绝对坐标而不是 Dock：Dock 的可用高度要等整条停靠链算完才知道，
/// 标题+描述+间隙一旦超过总高，描述就会被压扁成半行字。
/// </summary>
public sealed class ToolHeader : Panel
{
    // 设计像素：分隔线纵坐标 / 图标块边长，落地前统一乘 UiTheme.S
    private const int LineY = 77;
    private const int BelowLine = 12;
    private const int Tile = 34;
    private readonly Label _title;
    private readonly Label _desc;

    public ToolHeader(string title, string description)
    {
        Dock = DockStyle.Top;
        // 线下方留 12 设计像素：贴着线起笔的第一张卡片会和它叠成一条粗线
        Height = UiTheme.Sx(LineY + BelowLine);
        BackColor = UiTheme.Bg;

        _title = new Label
        {
            Text = title,
            Font = UiTheme.Title,
            ForeColor = UiTheme.Text,
            TextAlign = ContentAlignment.MiddleLeft,
            BackColor = Color.Transparent
        };
        _desc = new Label
        {
            Text = description,
            Font = UiTheme.Body,
            ForeColor = UiTheme.Muted,
            TextAlign = ContentAlignment.MiddleLeft,
            BackColor = Color.Transparent,
            AutoEllipsis = true
        };
        Controls.Add(_title);
        Controls.Add(_desc);
        UiTheme.ThemeChanged += OnThemeChanged;

        Resize += (_, _) => LayoutChildren();
        Paint += (_, e) =>
        {
            var g = e.Graphics;
            if (IconName is { Length: > 0 } icon)
                UiTheme.IconTile(g, UiTheme.Rc(0, 8, Tile, Tile), Accent ?? UiTheme.Primary, icon);
            var w = UiTheme.PenW();
            using var pen = new Pen(UiTheme.Border, w);
            var y = UiTheme.Edge(UiTheme.Sx(LineY), w);
            g.DrawLine(pen, 0, y, Width, y);
        };
        LayoutChildren();
    }

    private string? _iconName;

    /// <summary>标题块左侧的矢量图标名；由宿主按 ToolMeta 注入，为空则不画图标块。</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string? IconName
    {
        get => _iconName;
        set
        {
            if (_iconName == value) return;
            _iconName = value;
            LayoutChildren(); // M16：图标注入后标题左偏移要同步，否则压在图标块上
            Invalidate();
        }
    }

    private Color? _accent;

    /// <summary>图标块色相，与首页卡片同一索引取色。</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color? Accent
    {
        get => _accent;
        set
        {
            if (_accent == value) return;
            _accent = value;
            Invalidate();
        }
    }

    private void OnThemeChanged()
    {
        BackColor = UiTheme.Bg;                 // M18：主题切换后同步底色
        _title.ForeColor = UiTheme.Text;
        _desc.ForeColor = UiTheme.Muted;
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) UiTheme.ThemeChanged -= OnThemeChanged;
        base.Dispose(disposing);
    }

    private void LayoutChildren()
    {
        int left = IconName is { Length: > 0 } ? UiTheme.Sx(Tile + 14) : 0;
        int w = Math.Max(0, ClientSize.Width - left);
        // 标题与图标块（8..42）同一垂直中线，描述与标题左边缘同一条竖线
        _title.SetBounds(left, UiTheme.Sx(8), w, UiTheme.Sx(34));
        _desc.SetBounds(left, UiTheme.Sx(44), w, UiTheme.Sx(22));
    }
}
