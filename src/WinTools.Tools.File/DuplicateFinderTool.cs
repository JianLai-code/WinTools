using System.Drawing;
using System.Windows.Forms;
using WinTools.Core;
using WinTools.Core.Ui;

namespace WinTools.Tools.Files;

/// <summary>F4 重复文件查找：按大小粗筛 + SHA-256 精查，只读报告，绝不删除或移动任何文件。</summary>
[Tool(Order = 98)]
public sealed class DuplicateFinderTool : ITool
{
    public string Id => "duplicate-finder";
    public string Name => "重复文件查找";
    public string Category => "文件工具";
    public Icon? Icon => null;

    public Control CreateUI()
    {
        // 标签/提示与 32px 数字框、按钮高度不同，统一贴顶会显得不齐。
        // 按各控件实测高度补半差，100% / 200% DPI 下都能真正垂直居中。
        static void CenterInRow(Control c, int rowHeight, int gap) =>
            c.Margin = new Padding(0, Math.Max(0, (rowHeight - c.Height) / 2), UiTheme.Sx(gap), 0);

        var root = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Bg };
        var header = new ToolHeader("重复文件查找",
            "在指定目录里找出内容相同的文件；先按大小筛选再比对 SHA-256，只生成报告，不删除任何文件。");

        var pathBox = UiTheme.Input("尚未选择文件夹…", 440);
        pathBox.ReadOnly = true;
        pathBox.Dock = DockStyle.Top;
        var hint = UiTheme.HintLabel("只读扫描；无权限子目录跳过并标注。找到后请自行核对再决定如何处理。");
        hint.AutoSize = false;
        hint.Size = UiTheme.Sz(520, 26);
        hint.TextAlign = ContentAlignment.MiddleLeft;
        var btnSelect = UiTheme.Button("选择文件夹", "folder", UiTheme.ButtonKind.Secondary, 130);
        var btnScan = UiTheme.Button("开始查找", UiTheme.ButtonKind.Primary, 110);
        var btnCancel = UiTheme.Button("取消", UiTheme.ButtonKind.Secondary, 76);
        btnCancel.Visible = false;
        var minNumeric = new FluentNumeric
        {
            Minimum = 0, Maximum = 4096, Value = 1,
            Size = UiTheme.Sz(88, 32)
        };
        var minLabel = UiTheme.FieldLabel("最小大小（MB）");

        var selectRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, WrapContents = false,
            Margin = Padding.Empty, Padding = UiTheme.Pad(0, 0, 0, 8)
        };
        selectRow.Controls.Add(btnSelect);
        // 提示文字紧跟「选择文件夹」按钮，不再单独占一行。
        selectRow.Controls.Add(hint);
        var selectRowHeight = UiTheme.Sx(32);
        CenterInRow(btnSelect, selectRowHeight, 8);
        CenterInRow(hint, selectRowHeight, 0);

        var actionRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, Height = UiTheme.Sx(42),
            WrapContents = false, Margin = Padding.Empty
        };
        actionRow.Controls.Add(minLabel);
        actionRow.Controls.Add(minNumeric);
        actionRow.Controls.Add(btnScan);
        actionRow.Controls.Add(btnCancel);
        foreach (Control c in actionRow.Controls)
            CenterInRow(c, actionRow.Height, 8);

        var progressBar = new ProgressBar
        {
            Dock = DockStyle.Bottom, Height = UiTheme.Sx(8), Minimum = 0, Maximum = 100,
            Style = ProgressBarStyle.Marquee, Visible = false, Margin = UiTheme.Pad(0, 6, 0, 0)
        };

        // 提示并入选择行后少了一行，卡片按“表头40 + 内边距28 + 选择行40 + 路径32 + 进度8 + 操作行42”收紧。
        var inputCard = new CardPanel("扫描范围") { Dock = DockStyle.Top, Height = UiTheme.Sx(190) };
        inputCard.HeaderIcon = "folder";
        inputCard.Content.Controls.Add(pathBox);
        inputCard.Content.Controls.Add(actionRow);
        inputCard.Content.Controls.Add(progressBar);
        inputCard.Content.Controls.Add(selectRow);

        var list = UiTheme.List();
        list.Dock = DockStyle.Fill;
        list.Columns.Add("文件", UiTheme.Sx(170));
        list.Columns.Add("单份大小", UiTheme.Sx(90));
        list.Columns.Add("份数", UiTheme.Sx(60));
        list.Columns.Add("可释放", UiTheme.Sx(90));
        list.Columns.Add("路径", UiTheme.Sx(280));

        var feedback = new FeedbackBar { Dock = DockStyle.Bottom };
        var lastGroups = new List<DuplicateGroup>();
        string Report() => DuplicateFinder.BuildReport(lastGroups);

        var resultCard = new CardPanel("重复分组") { Dock = DockStyle.Fill, Margin = Padding.Empty };
        resultCard.HeaderIcon = "duplicate";
        resultCard.AddAction("复制报告", (_, _) =>
        {
            if (list.Items.Count == 0)
            {
                feedback.Show(FeedbackBar.Kind.Warn, "暂无可复制的报告，请先扫描。");
                return;
            }
            feedback.Show(ClipboardUtil.TryCopy(Report())
                ? FeedbackBar.Kind.Success : FeedbackBar.Kind.Warn,
                "已复制重复文件报告。");
        });
        resultCard.Content.Controls.Add(list);
        resultCard.Content.Controls.Add(feedback);

        System.Threading.CancellationTokenSource? cts = null;
        var busy = false;
        var target = "";

        btnSelect.Click += (_, _) =>
        {
            if (busy) return;
            using var dlg = new FolderBrowserDialog
            {
                Description = "选择要查找重复文件的文件夹",
                ShowNewFolderButton = false
            };
            if (dlg.ShowDialog() != DialogResult.OK) return;
            target = dlg.SelectedPath;
            pathBox.Text = target;
            feedback.Clear();
        };

        btnCancel.Click += (_, _) => cts?.Cancel();

        btnScan.Click += async (_, _) =>
        {
            if (busy) return;
            if (string.IsNullOrWhiteSpace(target) || !Directory.Exists(target))
            {
                feedback.Show(FeedbackBar.Kind.Warn, "请先选择一个有效的文件夹。");
                return;
            }
            busy = true;
            cts?.Dispose();
            cts = new System.Threading.CancellationTokenSource();
            var token = cts.Token;
            var minBytes = minNumeric.Value * 1024L * 1024L;
            btnSelect.Enabled = btnScan.Enabled = false;
            btnCancel.Enabled = true;
            btnCancel.Visible = true;
            progressBar.Visible = true;
            feedback.Clear();
            list.Items.Clear();
            lastGroups.Clear();

            try
            {
                var denied = new List<string>();
                var files = await Task.Run(
                    () => DuplicateFinder.EnumerateFiles(target, token, d => denied.Add(d)), token).ConfigureAwait(true);
                var groups = await Task.Run(
                    () => DuplicateFinder.Find(files, p => DuplicateFinder.ComputeSha256(p), minBytes, token),
                    token).ConfigureAwait(true);

                lastGroups.AddRange(groups);
                foreach (var g in groups)
                {
                    var row = new ListViewItem(new[]
                    {
                        Path.GetFileName(g.Paths[0]),
                        DuplicateFinder.FormatSize(g.Size),
                        g.Paths.Count.ToString(),
                        DuplicateFinder.FormatSize(g.Size * (g.Paths.Count - 1L)),
                        string.Join("；", g.Paths)
                    });
                    row.ToolTipText = string.Join(Environment.NewLine, g.Paths);
                    list.Items.Add(row);
                }
                var reclaim = groups.Sum(g => g.Size * (g.Paths.Count - 1L));
                var skipped = denied.Count > 0 ? $"；{denied.Count} 处目录无权限已跳过" : "";
                feedback.Show(groups.Count == 0 ? FeedbackBar.Kind.Info : FeedbackBar.Kind.Success,
                    $"扫描完成：{files.Count} 个文件，发现 {groups.Count} 组重复（可释放 {DuplicateFinder.FormatSize(reclaim)}）{skipped}。");
            }
            catch (OperationCanceledException)
            {
                feedback.Show(FeedbackBar.Kind.Info, "已取消扫描。");
            }
            catch (Exception ex)
            {
                feedback.Show(FeedbackBar.Kind.Error, $"扫描失败：{ex.Message}");
            }
            finally
            {
                busy = false;
                btnSelect.Enabled = btnScan.Enabled = true;
                btnCancel.Visible = false;
                progressBar.Visible = false;
            }
        };

        root.Controls.Add(resultCard);
        root.Controls.Add(UiTheme.CardGap());
        root.Controls.Add(inputCard);
        root.Controls.Add(header);
        return root;
    }
}
