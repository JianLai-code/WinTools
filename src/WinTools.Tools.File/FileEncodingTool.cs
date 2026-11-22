using System.Drawing;
using System.IO;
using System.Windows.Forms;
using WinTools.Core;
using WinTools.Core.Ui;

namespace WinTools.Tools.Files;

/// <summary>文件编码转换工具：UTF-8 / GBK / GB18030 等互转，另存为新文件不覆盖原文件。实现 ITool。</summary>
[Tool(Order = 94)]
public sealed class FileEncodingTool : ITool
{
    public string Id => "file-encoding-converter";
    public string Name => "文件编码转换";
    public string Category => "文件工具";
    public Icon? Icon => null;

    public Control CreateUI()
    {
        var root = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Bg };
        var header = new ToolHeader("文件编码转换", "在 UTF-8 / GBK / GB18030 / UTF-16 之间转换文本文件，结果另存不覆盖原文件。");

        // ---- 源文件区 ----
        var pathBox = UiTheme.Input("选择要转换的文本文件…", 440);
        pathBox.ReadOnly = true;
        pathBox.Dock = DockStyle.Top;
        var btnSelect = UiTheme.Button("选择文件", "folder", UiTheme.ButtonKind.Secondary, 120);
        // "尚未选择文件"不再单独占一行，改放在"选择文件"按钮右侧。
        var detectLabel = UiTheme.HintLabel("尚未选择文件");
        var selectRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, WrapContents = false, Margin = Padding.Empty,
            // 上 8 与路径输入框拉开间距，下 8 留出卡片底部呼吸感。
            Padding = UiTheme.Pad(0, 8, 0, 8)
        };
        selectRow.Controls.Add(btnSelect);
        selectRow.Controls.Add(detectLabel);
        btnSelect.Margin = Padding.Empty;
        // 提示标签按 32 高按钮垂直居中（顶部留 8）。
        detectLabel.Margin = UiTheme.Pad(12, 8, 0, 0);

        var fileCard = new CardPanel("源文件") { Dock = DockStyle.Top, Height = UiTheme.Sx(152) };
        fileCard.HeaderIcon = "folder";
        // Add 顺序即 Dock 的层叠顺序：后加的先贴边。目标自上而下为 路径框 → 选择按钮行，
        // 因此按相反顺序添加；两者都显式 Dock，避免绝对定位 (0,0) 互相重叠。
        fileCard.Content.Controls.Add(selectRow);
        fileCard.Content.Controls.Add(pathBox);

        // ---- 编码选择区：源 / 目标各一组分段按钮 ----
        var sourceButtons = BuildEncodingButtons(out var getSource);
        var targetButtons = BuildEncodingButtons(out var getTarget, "utf-8");
        var encTable = new TableLayoutPanel
        {
            // 高度按最多 3 行按钮留量（窄窗口下 7 个编码按钮会折成 3 行）：
            // 表头 40 + 内容区上下内边距 28 + 3 行 × (按钮 30 + 下边距 6) + 圆角描边余量。
            Dock = DockStyle.Top, Height = UiTheme.Sx(184), ColumnCount = 2, RowCount = 1, Margin = Padding.Empty
        };
        encTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        encTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        // 必须显式 Percent 行高：缺省行样式是 AutoSize，行高会塌成卡片的首选高，
        // 卡片被压扁后第二行编码按钮被裁掉、表格剩余高度变成下方空白。
        encTable.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        var srcCard = new CardPanel("源编码（自动检测，可手动指定）") { Dock = DockStyle.Fill, Margin = Padding.Empty };
        var dstCard = new CardPanel("目标编码") { Dock = DockStyle.Fill, Margin = Padding.Empty };
        srcCard.Content.Controls.Add(sourceButtons);
        dstCard.Content.Controls.Add(targetButtons);
        encTable.Controls.Add(srcCard, 0, 0);
        encTable.Controls.Add(dstCard, 1, 0);

        // ---- 操作区：恢复"转换"卡片（紧凑高度，仅按钮 + 提示，不再留大面积空白）；反馈条贴窗口底 ----
        var btnConvert = UiTheme.Button("转换并另存为…", UiTheme.ButtonKind.Primary, 150);
        var feedback = new FeedbackBar { Dock = DockStyle.Bottom };
        var convertRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty
        };
        convertRow.Controls.Add(btnConvert);
        convertRow.Controls.Add(UiTheme.HintLabel("提示：二进制文件会被拒绝；转换结果保留原文件名并加编码后缀。"));
        btnConvert.Margin = Padding.Empty;
        convertRow.Controls[1].Margin = UiTheme.Pad(12, 8, 0, 0);
        var convertCard = new CardPanel("转换") { Dock = DockStyle.Top, Height = UiTheme.Sx(102) };
        convertCard.HeaderIcon = "bolt";
        convertCard.Content.Controls.Add(convertRow);

        byte[]? sourceBytes = null;
        EncodingDetection? detection = null;

        btnSelect.Click += (_, _) =>
        {
            using var dlg = new OpenFileDialog { Title = "选择要转换的文本文件", Filter = "文本文件|*.txt;*.csv;*.json;*.xml;*.md;*.ini;*.log;*.html;*.htm;*.yml;*.yaml|所有文件|*.*" };
            if (dlg.ShowDialog() != DialogResult.OK) return;
            try
            {
                sourceBytes = System.IO.File.ReadAllBytes(dlg.FileName);
                pathBox.Text = dlg.FileName;
                detection = TextEncodingConverter.Detect(sourceBytes);
                detectLabel.Text = detection.IsBinary
                    ? $"检测结果：{detection.Reason}（将拒绝转换）"
                    : $"检测结果：{detection.Reason}";
                detectLabel.ForeColor = detection.IsBinary ? Color.FromArgb(0xDB, 0x27, 0x77) : UiTheme.Muted;
                feedback.Clear();
            }
            catch (Exception ex)
            {
                feedback.Show(FeedbackBar.Kind.Error, $"读取文件失败：{ex.Message}");
            }
        };

        btnConvert.Click += (_, _) =>
        {
            if (sourceBytes is null)
            {
                feedback.Show(FeedbackBar.Kind.Error, "请先选择要转换的文件。");
                return;
            }
            detection ??= TextEncodingConverter.Detect(sourceBytes);
            if (detection.IsBinary || detection.Encoding is null)
            {
                feedback.Show(FeedbackBar.Kind.Error, $"拒绝转换：{detection.Reason}。本工具只处理文本文件。");
                return;
            }
            var src = getSource();
            var dst = getTarget();
            // 源编码：优先用户手动指定（非 auto），否则用检测结果
            var from = src is null ? detection.Encoding : src.Encoding;
            var to = dst!.Encoding;

            var result = TextEncodingConverter.Convert(sourceBytes, from, to);
            if (!result.Success)
            {
                feedback.Show(FeedbackBar.Kind.Error, result.ErrorMessage ?? "转换失败");
                return;
            }

            var original = pathBox.Text;
            var ext = Path.GetExtension(original);
            var name = Path.GetFileNameWithoutExtension(original) + $"_{to.WebName}{ext}";
            using var dlg = new SaveFileDialog
            {
                Title = "另存转换后的文件（不会覆盖原文件）",
                Filter = "文本文件|*.*",
                FileName = name,
                InitialDirectory = Path.GetDirectoryName(original)
            };
            if (dlg.ShowDialog() != DialogResult.OK) return;
            if (string.Equals(Path.GetFullPath(dlg.FileName), Path.GetFullPath(original), StringComparison.OrdinalIgnoreCase))
            {
                feedback.Show(FeedbackBar.Kind.Error, "为保护原文件，不能覆盖源文件本身，请换一个文件名。");
                return;
            }
            try
            {
                System.IO.File.WriteAllBytes(dlg.FileName, result.Bytes!);
                var warn = result.HasReplacement ? "（部分字符无法表示，已用替代符，建议改用 UTF-8）" : "";
                feedback.Show(FeedbackBar.Kind.Success, $"已转换并保存到：{dlg.FileName}{warn}");
            }
            catch (Exception ex)
            {
                feedback.Show(FeedbackBar.Kind.Error, $"保存失败：{ex.Message}");
            }
        };

        // Dock 从后往前贴边：期望视觉自上而下为 表头 → 源文件 → 编码选择 → 转换卡片，反馈条贴底。
        root.Controls.Add(feedback);
        root.Controls.Add(convertCard);
        root.Controls.Add(UiTheme.CardGap());
        root.Controls.Add(encTable);
        root.Controls.Add(UiTheme.CardGap());
        root.Controls.Add(fileCard);
        root.Controls.Add(header);
        return root;
    }

    /// <summary>构建一组编码分段按钮；末尾追加"自动检测"仅用于源编码。</summary>
    private static FlowLayoutPanel BuildEncodingButtons(out Func<EncodingOption?> getSelected, string defaultId = "auto")
    {
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Margin = Padding.Empty };
        var buttons = new List<(FluentButton Btn, EncodingOption? Opt)>();
        var ids = new List<string> { "auto" };
        ids.AddRange(TextEncodingConverter.Options.Select(o => o.Id));
        foreach (var id in ids)
        {
            var opt = id == "auto" ? null : TextEncodingConverter.Find(id);
            var label = id == "auto" ? "自动" : opt!.Display;
            // 宽度按实际渲染文本量：全角括号/汉字接近两个半角宽，按字符数估宽
            // 会把"UTF-8（无 BOM）"裁掉尾部。MeasureText 返回设备像素，字体已按 DPI 缩放。
            int textWidth = TextRenderer.MeasureText(label, UiTheme.Body).Width + UiTheme.Sx(26);
            var btn = UiTheme.Button(label, UiTheme.ButtonKind.Secondary, 96, 30);
            btn.Size = new Size(textWidth, UiTheme.Sx(30));
            panel.Controls.Add(btn);
            buttons.Add((btn, opt));
            foreach (Control c in panel.Controls) c.Margin = UiTheme.Pad(0, 0, 6, 6);
        }
        void Select((FluentButton Btn, EncodingOption? Opt) chosen)
        {
            foreach (var (btn, _) in buttons)
                btn.Variant = btn == chosen.Btn ? UiTheme.ButtonKind.Primary : UiTheme.ButtonKind.Secondary;
        }
        var defaultEntry = buttons.FirstOrDefault(b => (b.Opt?.Id ?? "auto") == defaultId);
        Select(defaultEntry.Btn is null ? buttons[0] : defaultEntry);
        foreach (var entry in buttons)
            entry.Btn.Click += (_, _) => Select(entry);
        getSelected = () => buttons.First(b => b.Btn.Variant == UiTheme.ButtonKind.Primary).Opt;
        return panel;
    }
}
