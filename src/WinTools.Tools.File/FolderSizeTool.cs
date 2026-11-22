using System.Drawing;
using System.Windows.Forms;
using WinTools.Core;
using WinTools.Core.Ui;

namespace WinTools.Tools.Files;

/// <summary>F3 文件夹大小分析：只读统计一级子项递归大小，异步可取消，按大小降序。实现 ITool。</summary>
[Tool(Order = 97)]
public sealed class FolderSizeTool : ITool
{
    public string Id => "folder-size-analyzer";
    public string Name => "文件夹大小分析";
    public string Category => "文件工具";
    public Icon? Icon => null;

    public Control CreateUI()
    {
        var root = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Bg };
        var header = new ToolHeader("文件夹大小分析",
            "选一个文件夹，快速看出哪个子目录最占空间；只读统计，不改动任何文件。");

        // ---- 路径卡片 ----
        // Dock=Top 固定标准高度 32（此前 Dock=Fill 被竖向拉成 ~44px 的巨框，与其他工具输入框不一致）
        var pathBox = UiTheme.Input("尚未选择文件夹…", 440);
        pathBox.ReadOnly = true;
        pathBox.Dock = DockStyle.Top;
        // 固定高度 26（文字 18 + 下侧 8 间隙），与上下行的间距统一为 8
        var hintLabel = UiTheme.HintLabel("统计根目录下一级子项的递归大小；无权限子目录跳过并标注");
        hintLabel.AutoSize = false;
        hintLabel.Dock = DockStyle.Top;
        hintLabel.Height = UiTheme.Sx(26);
        hintLabel.TextAlign = ContentAlignment.MiddleLeft;
        var btnSelect = UiTheme.Button("选择文件夹", "folder", UiTheme.ButtonKind.Secondary, 130);
        var btnScan = UiTheme.Button("开始分析", UiTheme.ButtonKind.Primary, 110);
        var btnCancel = UiTheme.Button("取消", UiTheme.ButtonKind.Secondary, 80);
        btnCancel.Visible = false;

        var selectRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, WrapContents = false, Margin = Padding.Empty,
            Padding = UiTheme.Pad(0, 0, 0, 8)
        };
        selectRow.Controls.Add(btnSelect);
        foreach (Control c in selectRow.Controls) c.Margin = UiTheme.Pad(0, 0, 8, 0);
        var actionRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, Height = UiTheme.Sx(40), WrapContents = false, Margin = Padding.Empty
        };
        actionRow.Controls.Add(btnScan);
        actionRow.Controls.Add(btnCancel);
        foreach (Control c in actionRow.Controls) c.Margin = UiTheme.Pad(0, 0, 8, 0);

        var progressBar = new ProgressBar
        {
            Dock = DockStyle.Bottom, Height = UiTheme.Sx(8), Minimum = 0, Maximum = 100,
            Style = ProgressBarStyle.Marquee, Visible = false, Margin = UiTheme.Pad(0, 6, 0, 0)
        };

        // 高度实算：表头 40 + 描边 2 + 内容上下内边距 28 + 选择行 40 + 提示 26 + 输入框 32 + 操作行 40 ≈ 188。
        // 空闲时富余 8px 恰好是输入框与按钮行的间距；扫描时进度条占的 8px 正好吃掉这份富余，两种状态都不挤压。
        var pathCard = new CardPanel("目标文件夹") { Dock = DockStyle.Top, Height = UiTheme.Sx(216) };
        pathCard.HeaderIcon = "folder";
        // 后加的先贴边：selectRow 最后加贴最上，hint、pathBox 依次向下；actionRow/进度条贴底。
        pathCard.Content.Controls.Add(pathBox);     // Top（最后处理，排在提示下方）
        pathCard.Content.Controls.Add(actionRow);   // Bottom：进度条出现时它被顶到进度条之上
        pathCard.Content.Controls.Add(progressBar); // Bottom 贴底
        pathCard.Content.Controls.Add(hintLabel);   // Top
        pathCard.Content.Controls.Add(selectRow);   // Top 最上

        // ---- 结果表格 ----
        var list = UiTheme.List();
        list.Dock = DockStyle.Fill;
        list.Columns.Add("子目录", UiTheme.Sx(200));
        list.Columns.Add("总大小", UiTheme.Sx(100));
        list.Columns.Add("文件数", UiTheme.Sx(80));
        list.Columns.Add("占比", UiTheme.Sx(80));
        list.Columns.Add("备注", UiTheme.Sx(120));

        var feedback = new FeedbackBar { Dock = DockStyle.Bottom };
        var resultCard = new CardPanel("占用明细") { Dock = DockStyle.Fill, Margin = Padding.Empty };
        resultCard.HeaderIcon = "folder-size";
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
                Description = "选择要分析大小的文件夹",
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
            btnSelect.Enabled = btnScan.Enabled = false;
            btnCancel.Enabled = true;
            btnCancel.Visible = true;
            progressBar.Visible = true;
            feedback.Clear();
            list.Items.Clear();

            try
            {
                var result = await Task.Run(() => FolderSizeAnalyzer.Analyze(target, token), token);
                if (token.IsCancellationRequested) return;

                foreach (var node in result.Children)
                {
                    var pct = result.TotalBytes == 0 ? 0 : node.TotalBytes * 100.0 / result.TotalBytes;
                    var row = new ListViewItem(new[]
                    {
                        node.Name,
                        FolderSizeAnalyzer.FormatSize(node.TotalBytes),
                        node.TotalFiles.ToString(),
                        $"{pct:F1}%",
                        node.HasDenied ? "部分无权访问" : ""
                    });
                    if (node.HasDenied) row.ForeColor = UiTheme.Warn;
                    row.ToolTipText = node.Path;
                    list.Items.Add(row);
                }
                var denied = result.DeniedCount > 0 ? $"；{result.DeniedCount} 处无权限已跳过" : "";
                feedback.Show(FeedbackBar.Kind.Success,
                    $"分析完成：{result.TotalDirs} 个子目录、{result.TotalFiles} 个文件，共 {FolderSizeAnalyzer.FormatSize(result.TotalBytes)}{denied}。");
            }
            catch (OperationCanceledException)
            {
                feedback.Show(FeedbackBar.Kind.Info, "已取消分析。");
            }
            catch (Exception ex)
            {
                feedback.Show(FeedbackBar.Kind.Error, $"分析失败：{ex.Message}");
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
        root.Controls.Add(pathCard);
        root.Controls.Add(header);
        return root;
    }
}
