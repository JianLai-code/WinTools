using System.Drawing;
using System.Text;
using System.Windows.Forms;
using WinTools.Core;
using WinTools.Core.Ui;

namespace WinTools.Tools.SystemTools;

/// <summary>
/// 系统信息总览工具：一屏显示 CPU/内存/系统/磁盘，全部只读、逐项容错、结构化复制。
/// 界面按阶段四/原型重做：工具头 + 键值卡片 + 语义反馈条。
/// </summary>
[Tool(Order = 50)]
public sealed class SystemInfoTool : ITool
{
    public string Id => "system-info";
    public string Name => "系统信息总览";
    public string Category => "系统工具";
    public Icon? Icon => null;

    public Control CreateUI()
    {
        var root = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Bg };
        var header = new ToolHeader("系统信息总览", "一屏看清 CPU / 内存 / 系统版本 / 磁盘，普通权限即可读取，一键复制。");

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            AutoScroll = true,
            Padding = new Padding(0)
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, UiTheme.Sx(120)));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        List<SystemInfoItem> current = new();

        var feedback = new FeedbackBar { Dock = DockStyle.Bottom };
        var infoCard = new CardPanel("本机概要") { Dock = DockStyle.Fill, Margin = new Padding(0) };
        // 操作按钮与网络信息查询统一放在卡片头右侧：带矢量图标的小号次按钮。
        // 内边距沿用 CardPanel 默认值：改成 14/6 会让这一页的键值列与其它工具页的控件列对不齐
        infoCard.Content.Controls.Add(table);
        infoCard.Content.Controls.Add(feedback);

        void Render()
        {
            foreach (Control c in table.Controls.Cast<Control>().ToList()) c.Dispose();
            table.Controls.Clear();
            table.RowStyles.Clear();
            current = SystemInfoCollector.Collect();
            table.RowCount = current.Count + 1;

            for (var i = 0; i < current.Count; i++)
            {
                var item = current[i];
                table.RowStyles.Add(new RowStyle(SizeType.Absolute, UiTheme.Sx(40)));
                var nameLabel = new Label
                {
                    Text = item.Name, Dock = DockStyle.Fill,
                    Font = UiTheme.Bold, ForeColor = UiTheme.Muted,
                    AutoEllipsis = true,
                    Padding = UiTheme.Pad(0, 0, 8, 0),
                    TextAlign = ContentAlignment.MiddleLeft
                };
                var valueLabel = new Label
                {
                    Text = item.Value, Dock = DockStyle.Fill,
                    Font = UiTheme.Body,
                    ForeColor = item.Available ? UiTheme.Text : UiTheme.Error,
                    // CPU 型号这类长值超出列宽时以省略号收尾，不再被硬裁掉半截
                    AutoEllipsis = true,
                    TextAlign = ContentAlignment.MiddleLeft
                };
                table.Controls.Add(nameLabel, 0, i);
                table.Controls.Add(valueLabel, 1, i);
            }

            // 余量单独给一行空行：否则 TableLayoutPanel 会把多出来的高度摊进各字段行，行距被拉散
            table.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            int failed = current.Count(c => !c.Available);
            if (failed == 0)
                feedback.Show(FeedbackBar.Kind.Success, "所有字段读取成功。若某项失败会显示「不可用」，不影响其他字段。");
            else
                feedback.Show(FeedbackBar.Kind.Warn, $"有 {failed} 项读取失败（标红显示），其余字段正常。");
        }

        void Copy()
        {
            if (current.Count == 0) Render();
            var sb = new StringBuilder();
            sb.AppendLine("=== 系统信息总览 ===");
            foreach (var item in current) sb.AppendLine($"{item.Name}：{item.Value}");
            sb.AppendLine($"收集时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            if (ClipboardUtil.TryCopy(sb.ToString()))
                feedback.Show(FeedbackBar.Kind.Success, "已复制全部信息到剪贴板。");
            else
                feedback.Show(FeedbackBar.Kind.Warn, "剪贴板被其他程序占用，复制失败，请重试。");
        }

        // AddAction 的 FlowDirection 是 RightToLeft：先加「刷新」再加「复制」，
        // 视觉上就是右侧「刷新」、左侧「复制」，与网络信息卡片完全一致。
        infoCard.AddAction("刷新", "refresh", (_, _) => Render());
        infoCard.AddAction("复制", "copy", (_, _) => Copy());

        root.Controls.Add(infoCard);
        root.Controls.Add(header);
        Render();
        return root;
    }
}
