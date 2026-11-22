using System.Drawing;
using System.Windows.Forms;
using WinTools.Core;
using WinTools.Core.Ui;

namespace WinTools.Tools.SystemTools;

/// <summary>
/// 环境变量查看工具：只读展示当前进程的环境变量（继承用户与系统设置），
/// 支持按名称/值筛选、复制全部或复制选中。逻辑在 EnvironmentVariableCollector。
/// </summary>
[Tool(Order = 80)]
public sealed class EnvironmentVariableTool : ITool
{
    public string Id => "environment-variables";
    public string Name => "环境变量查看";
    public string Category => "系统工具";
    public Icon? Icon => null;

    public Control CreateUI()
    {
        var root = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Bg };
        var header = new ToolHeader("环境变量查看",
            "只读查看当前进程的环境变量（继承用户与系统设置），可按名称或值筛选，一键复制。");

        var list = UiTheme.List();
        list.Dock = DockStyle.Fill;
        list.Columns.Add("变量名", UiTheme.Sx(240));
        list.Columns.Add("变量值", UiTheme.Sx(560));

        var search = UiTheme.Input("筛选变量名或变量值（不区分大小写）", 300);
        var lblSearch = UiTheme.FieldLabel("筛选");
        var searchRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, Height = UiTheme.Sx(42), WrapContents = false, Margin = Padding.Empty
        };
        searchRow.Controls.Add(lblSearch);
        searchRow.Controls.Add(search);
        lblSearch.Margin = UiTheme.Pad(0, 9, 10, 0);
        search.Margin = UiTheme.Pad(0, 5, 0, 0);

        var all = new List<EnvironmentVariableItem>();
        var shown = new List<EnvironmentVariableItem>();

        var feedback = new FeedbackBar { Dock = DockStyle.Bottom };
        var card = new CardPanel("当前进程环境变量") { Dock = DockStyle.Fill, Margin = new Padding(0) };
        card.HeaderIcon = "environment";
        card.AddAction("刷新", "refresh", (_, _) => Reload());
        card.AddAction("复制全部", "copy", (_, _) => Copy(false));
        card.AddAction("复制选中", (_, _) => Copy(true));

        // Fill 先加（列表占满），再 Bottom 反馈条，最后 Top 筛选行。
        card.Content.Controls.Add(list);
        card.Content.Controls.Add(feedback);
        card.Content.Controls.Add(searchRow);

        void ApplyFilter()
        {
            shown = EnvironmentVariableCollector.Filter(all, search.Text).ToList();
            list.BeginUpdate();
            list.Items.Clear();
            foreach (var item in shown)
            {
                var row = new ListViewItem(item.Name);
                row.SubItems.Add(item.Value);
                list.Items.Add(row);
            }
            list.EndUpdate();
        }

        void Reload()
        {
            all = EnvironmentVariableCollector.Collect().ToList();
            ApplyFilter();
            feedback.Show(FeedbackBar.Kind.Success,
                $"读取完成，共 {all.Count} 项环境变量；当前显示 {shown.Count} 项。全程只读，不会修改系统配置。");
        }

        void Copy(bool selectedOnly)
        {
            IEnumerable<EnvironmentVariableItem> target;
            if (selectedOnly)
            {
                if (list.SelectedItems.Count == 0)
                {
                    feedback.Show(FeedbackBar.Kind.Warn, "请先在列表中选中一行，或改用「复制全部」。");
                    return;
                }
                target = list.SelectedItems.Cast<ListViewItem>()
                    .Where(x => x.Index >= 0 && x.Index < shown.Count)
                    .Select(x => shown[x.Index]);
            }
            else
            {
                if (shown.Count == 0)
                {
                    feedback.Show(FeedbackBar.Kind.Warn, "当前没有可复制的环境变量。");
                    return;
                }
                target = shown;
            }

            var text = EnvironmentVariableCollector.FormatForCopy(target);
            if (ClipboardUtil.TryCopy(text))
                feedback.Show(FeedbackBar.Kind.Success, $"已复制 {text.Split('\n').Length - 1} 条到剪贴板。");
            else
                feedback.Show(FeedbackBar.Kind.Warn, "剪贴板被其他程序占用，复制失败，请重试。");
        }

        search.TextChanged += (_, _) =>
        {
            ApplyFilter();
            feedback.Show(FeedbackBar.Kind.Info, $"筛选结果：{shown.Count} / {all.Count} 项。");
        };

        root.Controls.Add(card);
        root.Controls.Add(header);
        Reload();
        return root;
    }
}
