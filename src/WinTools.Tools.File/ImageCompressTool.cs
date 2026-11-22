using System.Drawing;
using System.Windows.Forms;
using WinTools.Core;
using WinTools.Core.Ui;

namespace WinTools.Tools.Files;

/// <summary>I2 图片批量压缩：多张图片按质量重编码为 JPG，另存不覆盖，变大保留原图。实现 ITool。</summary>
[Tool(Order = 96)]
public sealed class ImageCompressTool : ITool
{
    public string Id => "image-compress";
    public string Name => "图片批量压缩";
    public string Category => "图片工具";
    public Icon? Icon => null;

    public Control CreateUI()
    {
        var root = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Bg };
        var header = new ToolHeader("图片批量压缩",
            "批量把图片压缩为 JPG 另存到新文件，质量可调；压缩后没变小的自动保留原图，绝不覆盖。");

        // ---- 文件选择卡片 ----
        var btnSelect = UiTheme.Button("选择图片", "folder", UiTheme.ButtonKind.Secondary, 120);
        var btnClear = UiTheme.Button("清空", "trash", UiTheme.ButtonKind.Secondary, 90);
        var btnCompress = UiTheme.Button("开始压缩", UiTheme.ButtonKind.Primary, 120);
        var qualityNumeric = new FluentNumeric { Minimum = 1, Maximum = 100, Size = UiTheme.Sz(90, 32) };
        qualityNumeric.Value = 75;

        var qualityHint = UiTheme.HintLabel("质量 1-100：数值越低体积越小；透明区会合成到白底");
        var selectBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, WrapContents = false, Margin = Padding.Empty,
            Padding = UiTheme.Pad(0, 0, 0, 8)
        };
        selectBar.Controls.Add(btnSelect);
        selectBar.Controls.Add(btnClear);
        selectBar.Controls.Add(btnCompress); // 开始压缩并入选择行，紧跟在"清空"之后
        var qualityBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, WrapContents = false, Margin = Padding.Empty,
            Padding = UiTheme.Pad(0, 0, 0, 8)
        };
        qualityBar.Controls.Add(UiTheme.FieldLabel("压缩质量"));
        qualityBar.Controls.Add(qualityNumeric);
        qualityBar.Controls.Add(qualityHint);
        // 行内控件高度不一（标签/说明 ~18-20px，数字框 32px）：按各自实测高度算上边距，
        // 全部与数字框几何居中（写死 7px 在不同 DPI/字体度量下会差出一截）。
        foreach (Control c in qualityBar.Controls)
            // (Sx(32) - c.Height) 已是设备像素，不能过 Pad 再缩放，直接构造 Padding。
            c.Margin = new Padding(0, Math.Max(0, (UiTheme.Sx(32) - c.Height) / 2), UiTheme.Sx(10), 0);
        foreach (Control c in selectBar.Controls) c.Margin = UiTheme.Pad(0, 0, 8, 0);

        var selectCard = new CardPanel("图片列表") { Dock = DockStyle.Top, Height = UiTheme.Sx(182) };
        // 后加的先贴边（先处理）：selectBar / qualityBar / hintRow 依次贴顶，顺序自上而下为
        // 选择行 → 质量行 → 提示行；fillSpacer(Fill) 最后加入 → 最先处理贴底，吸收下方空白。
        // 用户要求：提示行紧跟质量行，其间距与"选择行↔质量行"一致——两行都设 Padding.Bottom=8，
        // 故 qualityBar 底边距与 selectBar 底边距同为 8px。绝不能让 AutoSize 标签直接 Dock=Fill：
        // Fill 区塌陷时标签会按首选高度向下溢出（0.5.4 前的旧坑）。
        // 卡高实算：表头 40 + 内边距 28 + 选择行 40 + 质量行 40 + 提示行 24 ≈ 172，富余约 10px。
        var selectedLabel = UiTheme.HintLabel("尚未选择图片");
        selectedLabel.Margin = UiTheme.Pad(0, 2, 0, 0);
        var hintRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, Height = UiTheme.Sx(24), WrapContents = false, Margin = Padding.Empty
        };
        hintRow.Controls.Add(selectedLabel);
        var fillSpacer = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        selectCard.Content.Controls.Add(fillSpacer);   // Fill 先加：最先处理，占底部弹性区
        selectCard.Content.Controls.Add(hintRow);      // Top：贴质量行下方
        selectCard.Content.Controls.Add(qualityBar);   // Top
        selectCard.Content.Controls.Add(selectBar);    // Top：最后加的 Top 贴最顶

        // ---- 结果表格 ----
        var list = UiTheme.List();
        list.Dock = DockStyle.Fill;
        list.Columns.Add("文件", UiTheme.Sx(150));
        list.Columns.Add("原大小", UiTheme.Sx(90));
        list.Columns.Add("压缩后", UiTheme.Sx(90));
        list.Columns.Add("节省", UiTheme.Sx(90));
        list.Columns.Add("结果", UiTheme.Sx(220));

        var feedback = new FeedbackBar { Dock = DockStyle.Bottom };
        var resultCard = new CardPanel("压缩结果") { Dock = DockStyle.Fill, Margin = Padding.Empty };
        resultCard.HeaderIcon = "image-compress";
        resultCard.Content.Controls.Add(list);
        resultCard.Content.Controls.Add(feedback);

        var inputs = new List<(string Path, byte[] Bytes)>();

        void RefreshSelectedLabel()
        {
            selectedLabel.Text = inputs.Count == 0
                ? "尚未选择图片"
                : $"已选 {inputs.Count} 张，共 {ImageFormatCodec.FormatSize(inputs.Sum(i => i.Bytes.Length))}";
        }

        btnSelect.Click += (_, _) =>
        {
            using var dlg = new OpenFileDialog
            {
                Multiselect = true,
                Title = "选择要压缩的图片（可多选）",
                Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|所有文件|*.*"
            };
            if (dlg.ShowDialog() != DialogResult.OK) return;
            foreach (var path in dlg.FileNames)
            {
                if (inputs.Any(i => string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase))) continue;
                try { inputs.Add((path, System.IO.File.ReadAllBytes(path))); }
                catch (Exception ex) { feedback.Show(FeedbackBar.Kind.Warn, $"跳过 {Path.GetFileName(path)}：{ex.Message}"); }
            }
            RefreshSelectedLabel();
        };

        btnClear.Click += (_, _) =>
        {
            inputs.Clear();
            list.Items.Clear();
            feedback.Clear();
            RefreshSelectedLabel();
        };

        btnCompress.Click += (_, _) =>
        {
            if (inputs.Count == 0)
            {
                feedback.Show(FeedbackBar.Kind.Warn, "请先选择要压缩的图片。");
                return;
            }
            var quality = qualityNumeric.Value;
            var result = ImageCompressor.CompressFiles(
                inputs,
                quality,
                outputPathFor: path =>
                {
                    var dir = Path.GetDirectoryName(path) ?? "";
                    var name = Path.GetFileNameWithoutExtension(path);
                    return Path.Combine(dir, $"{name}_compressed.jpg");
                },
                write: (outPath, bytes) => System.IO.File.WriteAllBytes(outPath, bytes));

            list.BeginUpdate();
            list.Items.Clear();
            foreach (var item in result.Items)
            {
                var row = new ListViewItem(new[]
                {
                    item.Name,
                    ImageFormatCodec.FormatSize(item.OriginalBytes),
                    item.Written ? ImageFormatCodec.FormatSize(item.ResultBytes) : "—",
                    item.Written ? ImageFormatCodec.FormatSize(item.Delta) : "—",
                    item.Note ?? (item.Written ? "已压缩" : "跳过")
                });
                if (item.Written) row.ForeColor = UiTheme.Success;
                else if (item.Grew) row.ForeColor = UiTheme.Warn;
                else row.ForeColor = UiTheme.Muted;
                row.ToolTipText = item.Path;
                list.Items.Add(row);
            }
            list.EndUpdate();

            if (result.Succeeded == 0)
                feedback.Show(FeedbackBar.Kind.Warn,
                    $"没有写出新文件：{result.Skipped} 张未变小或失败，原图均已保留。");
            else
                feedback.Show(FeedbackBar.Kind.Success,
                    $"已压缩 {result.Succeeded} 张、跳过 {result.Skipped} 张，共节省 {ImageFormatCodec.FormatSize(result.TotalSaved)}；原图未改动。");
        };

        root.Controls.Add(resultCard);
        root.Controls.Add(UiTheme.CardGap());
        root.Controls.Add(selectCard);
        root.Controls.Add(header);
        return root;
    }
}
