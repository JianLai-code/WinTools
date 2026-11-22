using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace WinTools.Core.Ui;

/// <summary>
/// Win11 分区卡片：圆角表面 + 次级表头 + 内容区。工具页的"输入 / 结果 / 参数"容器。
/// 表头与内容都被父级圆角 Region 裁剪，因此顶部两角自然成圆，无需额外处理。
/// </summary>
public sealed class CardPanel : Panel
{
    private const int HeadHeight = 40;
    private readonly Panel _header;
    private readonly Label _title;
    private readonly FlowLayoutPanel _actions;

    public CardPanel(string title, int headerHeight = HeadHeight)
    {
        BackColor = UiTheme.Panel;
        BorderStyle = BorderStyle.None;
        // 表头与内容铺满客户区会把本体 Paint 画的那圈描边完全盖住，只剩四个圆角露线。
        // 让子面板内缩一个线宽，描边才成完整一圈，卡片边线不再"只有拐角"。
        Padding = new Padding(UiTheme.PenW());
        Margin = UiTheme.Pad(0, 0, 0, 12);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);

        _header = new Panel
        {
            Dock = DockStyle.Top,
            Height = UiTheme.Sx(headerHeight),
            BackColor = UiTheme.TableHead,
            Padding = UiTheme.Pad(16, 0, 10, 0)
        };
        _header.Paint += (_, e) =>
        {
            var w = UiTheme.PenW();
            using var pen = new Pen(UiTheme.RowLine, w);
            var y = UiTheme.Edge(_header.Height, w);
            e.Graphics.DrawLine(pen, 0, y, _header.Width, y);
            if (_headerIcon is null) return;
            int side = UiTheme.Sx(16);
            var box = new RectangleF(UiTheme.Sx(16), (_header.Height - side) / 2f, side, side);
            IconFactory.Draw(e.Graphics, _headerIcon, box, UiTheme.TextSecondary);
        };

        _title = new Label
        {
            Text = title,
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = UiTheme.Bold,
            ForeColor = UiTheme.Text,
            BackColor = Color.Transparent
        };
        _actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = Color.Transparent
        };
        // Fill 先加、Right 后加：标题占满操作按钮剩下的宽度，卡片再窄也不会让按钮压在标题上
        _header.Controls.Add(_title);
        _header.Controls.Add(_actions);

        Content = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = UiTheme.Pad(16, 14, 16, 14),
            BackColor = UiTheme.Panel
        };

        Controls.Add(Content);   // Fill 先加
        Controls.Add(_header);   // Top 后加

        Paint += (_, e) =>
            UiTheme.DrawRoundedBorder(e.Graphics, new Rectangle(0, 0, Width - 1, Height - 1),
                UiTheme.RadiusCard, UiTheme.Border);
        Resize += (_, _) => UiTheme.Round(this, UiTheme.RadiusCard);
        UiTheme.ThemeChanged += OnThemeChanged;
    }

    private void OnThemeChanged()
    {
        // M18：卡片表面/表头/标题的配色在构造时快照，切主题后需同步刷新。
        BackColor = UiTheme.Panel;
        Content.BackColor = UiTheme.Panel;
        _header.BackColor = UiTheme.TableHead;
        _title.ForeColor = UiTheme.Text;
        _header.Invalidate();
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) UiTheme.ThemeChanged -= OnThemeChanged;
        base.Dispose(disposing);
    }

    /// <summary>内容区：把实际控件加到这里。</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Panel Content { get; }

    /// <summary>表头右侧操作区（放复制按钮等）。</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public FlowLayoutPanel Actions => _actions;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Title
    {
        get => _title.Text;
        set => _title.Text = value;
    }

    private string? _headerIcon;

    /// <summary>表头图标名（IconFactory 注册名），传 null 不显示。</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string? HeaderIcon
    {
        get => _headerIcon;
        set
        {
            _headerIcon = value;
            _title.Padding = value is null ? Padding.Empty : UiTheme.Pad(26, 0, 0, 0);
            _header.Invalidate();
        }
    }

    /// <summary>加一个小号次按钮到表头右侧。</summary>
    public Button AddAction(string text, EventHandler onClick)
        => AddAction(text, null, onClick);

    /// <summary>加一个带矢量图标的小按钮到表头右侧。</summary>
    public Button AddAction(string text, string? icon, EventHandler onClick)
    {
        var width = string.IsNullOrEmpty(icon) ? 66 : 84;
        var btn = new FluentButton
        {
            Text = text,
            IconName = icon,
            Variant = UiTheme.ButtonKind.Subtle,
            Size = UiTheme.Sz(width, 28),
            Radius = UiTheme.RadiusMedium,
            Margin = UiTheme.Pad(0, 6, 6, 0)
        };
        btn.Click += onClick;
        _actions.Controls.Add(btn);
        return btn;
    }
}
