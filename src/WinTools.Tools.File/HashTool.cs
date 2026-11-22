using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using WinTools.Core;
using WinTools.Core.Ui;

namespace WinTools.Tools.Files;

/// <summary>
/// 哈希校验工具：实现 ITool，由 Shell 反射发现。
/// 拖放/选择多个文件异步计算 MD5/SHA1/SHA256，进度不卡界面，单文件失败只影响自己。
/// 界面按阶段四/原型重做：工具头 + 拖放卡片 + 结果表格卡片 + 语义反馈条。
/// </summary>
[Tool(Order = 40)]
public sealed class HashTool : ITool
{
    public string Id => "hash-checker";
    public string Name => "哈希校验";
    public string Category => "文件工具";
    public Icon? Icon => null;

    public Control CreateUI()
    {
        var root = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Bg };
        var header = new ToolHeader("哈希校验", "拖入一个或多个文件，立即计算 MD5 / SHA1 / SHA256。大文件显示进度且不卡界面。");

        // ---- 拖放区（卡片内）----
        // 图标与文案自绘：子 Label 铺满容器会吞掉鼠标消息，父容器的悬停高亮和点击浏览就收不到事件；
        // 绝对坐标居中在控件还没布局时算出的是负坐标，图标会跑到可见区外。
        var dropZone = new Panel
        {
            Dock = DockStyle.Fill,
            AllowDrop = true,
            BackColor = UiTheme.Surface,
            Cursor = Cursors.Hand,
            Padding = UiTheme.Pad(16, 12, 16, 12)
        };
        const string dropGlyph = "⇪";
        const string dropHint = "拖入文件，或点击「选择文件」浏览\n支持多选 · 大文件显示进度且不卡界面";
        var glyphFont = UiTheme.FontAt(30F, family: "Segoe UI");
        var dropHover = false;
        // 圆角与卡片同族，直角矩形在圆角卡里就是那颗"过锐的拐角"
        dropZone.Resize += (_, _) => UiTheme.Round(dropZone, UiTheme.RadiusLarge);
        dropZone.Paint += (_, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var w = UiTheme.PenW();
            var frame = new RectangleF(w / 2f, w / 2f,
                Math.Max(0, dropZone.Width - w), Math.Max(0, dropZone.Height - w));
            // 拖放区规范样式是虚线框；描边贴着圆角路径走，不顶到卡片圆角
            using (var pen = new Pen(dropHover ? UiTheme.Primary : UiTheme.Stroke, w) { DashStyle = DashStyle.Dash })
            using (var path = UiTheme.RoundedPath(frame, UiTheme.RadiusLarge))
                g.DrawPath(pen, path);

            var iconSize = TextRenderer.MeasureText(g, dropGlyph, glyphFont);
            int gap = UiTheme.Sx(10);
            var hintSize = TextRenderer.MeasureText(g, dropHint, UiTheme.Body,
                new Size(Math.Max(0, dropZone.Width - dropZone.Padding.Horizontal), 0),
                TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
            // 图标+文案整体垂直居中：写死 y 值在 200% 下会压在上半区
            int block = iconSize.Height + gap + hintSize.Height;
            int top = Math.Max(dropZone.Padding.Top, (dropZone.Height - block) / 2);
            TextRenderer.DrawText(g, dropGlyph, glyphFont,
                new Rectangle(0, top, dropZone.Width, iconSize.Height), UiTheme.Primary,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, dropHint, UiTheme.Body,
                new Rectangle(dropZone.Padding.Left, top + iconSize.Height + gap,
                    Math.Max(0, dropZone.Width - dropZone.Padding.Horizontal), hintSize.Height),
                UiTheme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.WordBreak);
        };

        void SetDropHover(bool hover)
        {
            dropHover = hover;
            dropZone.BackColor = hover ? UiTheme.PrimarySoft : UiTheme.Surface;
            dropZone.Invalidate();
        }
        dropZone.MouseEnter += (_, _) => SetDropHover(true);
        dropZone.MouseLeave += (_, _) => SetDropHover(false);

        var btnSelect = UiTheme.Button("选择文件");
        var btnCopy = UiTheme.Button("复制结果", UiTheme.ButtonKind.Secondary);
        var btnClear = UiTheme.Button("清空列表", UiTheme.ButtonKind.Secondary);
        var btnCancel = UiTheme.Button("取消", UiTheme.ButtonKind.Secondary);
        btnCancel.Visible = false; // M20：仅计算中显示，给用户中止大文件的入口
        // 操作条排在拖放区下方，与其余工具页"输入 → 操作"的顺序一致
        var buttonBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, Height = UiTheme.Sx(46), WrapContents = false, Margin = Padding.Empty
        };
        buttonBar.Controls.Add(btnSelect);
        buttonBar.Controls.Add(btnCopy);
        buttonBar.Controls.Add(btnClear);
        buttonBar.Controls.Add(btnCancel);
        // 高度 46 = 上边距 14 + 按钮 32 + 下余 0：与拖放区的间距 14 和卡片内容下边距 14 一致，
        // 按钮行到上下边界等距（此前上 5 下 19 明显失衡）。
        buttonBar.Padding = UiTheme.Pad(0, 14, 0, 0);
        foreach (Control c in buttonBar.Controls) c.Margin = UiTheme.Pad(0, 0, 8, 0);

        var progressBar = new ProgressBar
        {
            Dock = DockStyle.Bottom,
            Height = UiTheme.Sx(8),
            Minimum = 0,
            Maximum = 100,
            Style = ProgressBarStyle.Continuous,
            Visible = false,
            Margin = UiTheme.Pad(0, 6, 0, 0)   // 只留上方的间隙，停靠宽度不受默认 3px 边距影响
        };

        // 高度实算：表头 40 + 描边 2 + 内边距 28 + 按钮条 46 + 拖放区余量 ≈ 216；
        // 210 会在按钮条加高后压矮拖放区，把两行提示的第二行裁掉。
        var dropCard = new CardPanel("文件选择") { Dock = DockStyle.Top, Height = UiTheme.Sx(216) };
        dropCard.Content.Controls.Add(dropZone);     // Fill
        dropCard.Content.Controls.Add(buttonBar);    // Bottom
        dropCard.Content.Controls.Add(progressBar);  // Bottom 后加的贴底

        // ---- 结果表格 ----
        var list = UiTheme.List();
        list.Dock = DockStyle.Fill;
        list.Columns.Add("文件", UiTheme.Sx(130));
        list.Columns.Add("MD5", UiTheme.Sx(180));
        list.Columns.Add("SHA1", UiTheme.Sx(210));
        list.Columns.Add("SHA256", UiTheme.Sx(240));
        list.Columns.Add("状态", UiTheme.Sx(70));

        var feedback = new FeedbackBar { Dock = DockStyle.Bottom };
        var resultCard = new CardPanel("校验结果") { Dock = DockStyle.Fill, Margin = new Padding(0) };
        resultCard.AddAction("复制", (_, _) => Copy());
        // 表格保留卡片内边距：贴零边距会让表头分隔线和直角边框顶进卡片的圆角区
        resultCard.Content.Controls.Add(list);
        resultCard.Content.Controls.Add(feedback);

        // ---- 异步处理（业务逻辑保持不变）----
        var busy = false;   // 防止计算中重复选择/拖放，或被"清空列表"打断出错位反馈
        System.Threading.CancellationTokenSource? cts = null;
        async Task ProcessFilesAsync(IEnumerable<string> paths)
        {
            if (busy) return;
            var fileList = paths.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
            if (fileList.Count == 0) return;

            busy = true;
            cts?.Dispose();
            cts = new System.Threading.CancellationTokenSource();
            var token = cts.Token;
            feedback.Clear();
            btnSelect.Enabled = btnCopy.Enabled = btnClear.Enabled = false;
            btnCancel.Enabled = true;
            btnCancel.Visible = true;
            progressBar.Visible = true;
            progressBar.Value = 0;

            var canceled = false;
            try
            {
                for (var i = 0; i < fileList.Count; i++)
                {
                    if (token.IsCancellationRequested) { canceled = true; break; }
                    var path = fileList[i];
                    var name = Path.GetFileName(path);
                    var item = new ListViewItem(new[] { name, "", "", "", "计算中…" });
                    list.Items.Add(item);
                    list.EnsureVisible(item.Index);

                    // Progress<T> 自带 UI 线程封送，DoEvents 只会在 await 处放大消息重入。
                    var rowProgress = new Progress<int>(p => item.SubItems[4].Text = $"{p}%");
                    var result = await FileHasher.ComputeAsync(path, rowProgress, token);
                    if (token.IsCancellationRequested)
                    {
                        // 取消时移除本行半成品，不留"计算中…"残行。
                        item.Remove();
                        canceled = true;
                        break;
                    }
                    progressBar.Value = (int)((i + 1) * 100.0 / fileList.Count);

                    if (result.Success)
                    {
                        item.SubItems[1].Text = result.Md5;
                        item.SubItems[2].Text = result.Sha1;
                        item.SubItems[3].Text = result.Sha256;
                        item.SubItems[4].Text = "完成";
                    }
                    else
                    {
                        item.SubItems[4].Text = "失败";
                        item.ForeColor = UiTheme.Error;
                        item.ToolTipText = result.ErrorMessage;
                        for (var c = 1; c <= 3; c++) item.SubItems[c].Text = "—";
                    }
                }
                if (!canceled)
                    feedback.Show(FeedbackBar.Kind.Success, $"校验完成，共处理 {fileList.Count} 个文件。");
            }
            catch (OperationCanceledException)
            {
                canceled = true;
            }
            catch (Exception ex)
            {
                feedback.Show(FeedbackBar.Kind.Error, $"计算失败：{ex.Message}");
            }
            finally
            {
                if (canceled) feedback.Show(FeedbackBar.Kind.Warn, $"已取消，本次未完成全部 {fileList.Count} 个文件。");
                busy = false;
                btnSelect.Enabled = btnCopy.Enabled = btnClear.Enabled = true;
                btnCancel.Visible = false;
                progressBar.Visible = false;
            }
        }

        void Copy()
        {
            if (list.Items.Count == 0)
            {
                feedback.Show(FeedbackBar.Kind.Warn, "暂无可复制的内容，请先计算文件哈希。");
                return;
            }
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("文件\tMD5\tSHA1\tSHA256\t状态");
            foreach (ListViewItem item in list.Items)
                sb.AppendLine(string.Join("\t", item.SubItems.Cast<ListViewItem.ListViewSubItem>().Select(s => s.Text)));
            if (ClipboardUtil.TryCopy(sb.ToString()))
                feedback.Show(FeedbackBar.Kind.Success, $"已复制 {list.Items.Count} 个文件的结果到剪贴板。");
            else
                feedback.Show(FeedbackBar.Kind.Warn, "剪贴板被其他程序占用，复制失败，请重试。");
        }

        btnSelect.Click += async (_, _) =>
        {
            // L05：async void 的异常无法被外层捕获，包一层兜底避免升级为进程级崩溃。
            try
            {
                if (busy) return;
                using var dialog = new OpenFileDialog { Multiselect = true, Title = "选择要计算哈希的文件", Filter = "所有文件|*.*" };
                if (dialog.ShowDialog() == DialogResult.OK) await ProcessFilesAsync(dialog.FileNames);
            }
            catch (Exception ex) { feedback.Show(FeedbackBar.Kind.Error, $"选择文件失败：{ex.Message}"); }
        };
        dropZone.DragEnter += (_, e) =>
            e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
        dropZone.DragDrop += async (_, e) =>
        {
            try
            {
                if (e.Data?.GetData(DataFormats.FileDrop) is string[] files) await ProcessFilesAsync(files);
            }
            catch (Exception ex) { feedback.Show(FeedbackBar.Kind.Error, $"拖放失败：{ex.Message}"); }
        };
        dropZone.Click += (_, _) => { if (!busy) btnSelect.PerformClick(); };
        btnCopy.Click += (_, _) => { if (!busy) Copy(); };
        btnCancel.Click += (_, _) =>
        {
            // M20：请求取消；实际停止在 ComputeAsync 的读取循环/consumer 检查点生效。
            cts?.Cancel();
            btnCancel.Enabled = false;
        };
        btnClear.Click += (_, _) =>
        {
            if (busy) return;
            list.Items.Clear();
            progressBar.Visible = false;
            feedback.Clear();
        };
        root.Disposed += (_, _) => cts?.Dispose();

        root.Controls.Add(resultCard);
        root.Controls.Add(UiTheme.CardGap());
        root.Controls.Add(dropCard);
        root.Controls.Add(header);
        return root;
    }
}
