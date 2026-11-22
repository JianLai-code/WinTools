using System.Drawing;
using System.Text;
using System.Windows.Forms;
using WinTools.Core;
using WinTools.Core.Ui;

namespace WinTools.Tools.SystemTools;

/// <summary>
/// 本机网络信息查询工具：一屏显示主机名 / IP / 网关 / DNS / 网卡明细，
/// 全部只读、逐项容错、结构化复制。纯逻辑在 NetworkInfoCollector，界面只负责呈现。
/// </summary>
[Tool(Order = 70)]
public sealed class NetworkInfoTool : ITool
{
    public string Id => "network-info";
    public string Name => "网络信息查询";
    public string Category => "系统工具";
    public Icon? Icon => null;

    public Control CreateUI()
    {
        var root = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Bg };
        var header = new ToolHeader("网络信息查询",
            "查看本机 IP 地址、子网掩码、默认网关、DNS 与逐网卡信息；只读本机配置，不访问外网。");

        var feedback = new FeedbackBar { Dock = DockStyle.Bottom };
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = UiTheme.Panel };
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            BackColor = UiTheme.Panel,
            Margin = Padding.Empty
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, UiTheme.Sx(130)));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        scroll.Controls.Add(table);

        // 状态先声明再挂事件：C# 要求 lambda 捕获的局部变量在捕获点已赋值。
        NetworkInfoSnapshot? current = null;
        Button? refreshBtn = null;
        var busy = false;
        var row = 0;
        var tip = new ToolTip();
        root.Disposed += (_, _) => tip.Dispose();

        var card = new CardPanel("网络信息") { Dock = DockStyle.Fill, Margin = new Padding(0) };
        card.HeaderIcon = "network-info";
        refreshBtn = card.AddAction("刷新", "refresh", (_, _) => _ = RefreshAsync());
        card.AddAction("复制", "copy", (_, _) => Copy());
        card.Content.Controls.Add(scroll);
        card.Content.Controls.Add(feedback);

        int AddRow(int height)
        {
            var index = row++;
            table.RowCount = row;
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, UiTheme.Sx(height)));
            return index;
        }

        void AddSection(string text, Color color)
        {
            var index = AddRow(34);
            var label = new Label
            {
                Text = text, Dock = DockStyle.Fill, Font = UiTheme.Bold,
                ForeColor = color, BackColor = Color.Transparent,
                AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft
            };
            table.Controls.Add(label, 0, index);
            table.SetColumnSpan(label, 2);
        }

        void AddField(NetworkInfoItem item)
        {
            var index = AddRow(38);
            var name = new Label
            {
                Text = item.Name, Dock = DockStyle.Fill, Font = UiTheme.Bold,
                ForeColor = UiTheme.Muted, BackColor = Color.Transparent, AutoEllipsis = true,
                Padding = UiTheme.Pad(0, 0, 8, 0), TextAlign = ContentAlignment.MiddleLeft
            };
            var value = new Label
            {
                Text = item.Value, Dock = DockStyle.Fill, Font = UiTheme.Body,
                ForeColor = item.Available ? UiTheme.Text : UiTheme.Error,
                BackColor = Color.Transparent,
                AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft
            };
            if (!string.IsNullOrEmpty(item.Value)) tip.SetToolTip(value, item.Value);
            table.Controls.Add(name, 0, index);
            table.Controls.Add(value, 1, index);
        }

        void AddAdapterHeader(NetworkAdapterInfo adapter)
        {
            var index = AddRow(36);
            var label = new Label
            {
                Text = $"{adapter.Name}　·　{adapter.Kind}　·　{(adapter.Connected ? "已连接" : "未连接")}",
                Dock = DockStyle.Fill,
                Font = UiTheme.FontAt(13F, bold: true),
                ForeColor = adapter.Connected ? UiTheme.Success : UiTheme.TextSecondary,
                BackColor = Color.Transparent,
                AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft
            };
            tip.SetToolTip(label, label.Text);
            table.Controls.Add(label, 0, index);
            table.SetColumnSpan(label, 2);
        }

        void Apply(NetworkInfoSnapshot snapshot)
        {
            current = snapshot;
            tip.RemoveAll();
            table.SuspendLayout();
            foreach (var c in table.Controls.Cast<Control>().ToList()) c.Dispose();
            table.Controls.Clear();
            table.RowStyles.Clear();
            row = 0;

            AddSection("本机概要", UiTheme.Primary);
            foreach (var item in snapshot.Summary) AddField(item);

            AddSection($"网卡明细（{snapshot.Adapters.Count} 块）", UiTheme.Primary);
            if (snapshot.Adapters.Count == 0)
            {
                AddField(NetworkInfoItem.Ok("说明", "未发现非回环网卡，或当前环境不提供网卡信息。"));
            }
            else
            {
                foreach (var adapter in snapshot.Adapters)
                {
                    AddAdapterHeader(adapter);
                    foreach (var item in adapter.Fields) AddField(item);
                    AddRow(10); // 网卡之间的留白
                }
            }

            table.ResumeLayout(true);

            var failed = snapshot.Summary.Count(i => !i.Available) +
                         snapshot.Adapters.Sum(a => a.Available
                             ? a.Fields.Count(f => !f.Available)
                             : 1);
            if (failed == 0)
                feedback.Show(FeedbackBar.Kind.Success,
                    "读取完成。未访问外网；若某项失败会标红显示，不影响其他字段。");
            else
                feedback.Show(FeedbackBar.Kind.Warn,
                    $"读取完成，有 {failed} 项不可用（标红显示），其余信息正常。");
        }

        async Task RefreshAsync()
        {
            if (busy) return;
            busy = true;
            if (refreshBtn is not null) refreshBtn.Enabled = false;
            feedback.Show(FeedbackBar.Kind.Info, "正在读取本机网络信息…");
            try
            {
                // 枚举网卡在虚拟网卡较多时可能耗时，放到线程池，避免界面冻结。
                var snapshot = await Task.Run(NetworkInfoCollector.Collect);
                Apply(snapshot);
            }
            catch (Exception ex)
            {
                feedback.Show(FeedbackBar.Kind.Error, $"读取失败：{ex.Message}");
            }
            finally
            {
                busy = false;
                if (refreshBtn is not null) refreshBtn.Enabled = true;
            }
        }

        void Copy()
        {
            if (current is null)
            {
                feedback.Show(FeedbackBar.Kind.Warn, "信息尚未读取完成，请稍后再试。");
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine("=== 本机网络信息 ===");
            foreach (var item in current.Summary) sb.AppendLine($"{item.Name}：{item.Value}");
            foreach (var adapter in current.Adapters)
            {
                sb.AppendLine();
                sb.AppendLine($"--- 网卡：{adapter.Name}（{adapter.Kind}，{(adapter.Connected ? "已连接" : "未连接")}）---");
                foreach (var item in adapter.Fields) sb.AppendLine($"{item.Name}：{item.Value}");
            }
            sb.AppendLine($"收集时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");

            if (ClipboardUtil.TryCopy(sb.ToString()))
                feedback.Show(FeedbackBar.Kind.Success, "已复制全部网络信息到剪贴板。");
            else
                feedback.Show(FeedbackBar.Kind.Warn, "剪贴板被其他程序占用，复制失败，请重试。");
        }

        root.Controls.Add(card);
        root.Controls.Add(header);
        _ = RefreshAsync();
        return root;
    }
}
