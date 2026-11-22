using System.Drawing;
using System.Windows.Forms;
using WinTools.Core;
using WinTools.Core.Ui;

namespace WinTools.Tools.Files;

/// <summary>I1 图片格式转换：单张图片在 PNG/JPG/BMP/GIF/TIFF 间互转，JPG 可调质量。实现 ITool。</summary>
[Tool(Order = 95)]
public sealed class ImageConvertTool : ITool
{
    public string Id => "image-format-converter";
    public string Name => "图片格式转换";
    public string Category => "图片工具";
    public Icon? Icon => null;

    public Control CreateUI()
    {
        static void AlignBar(FlowLayoutPanel bar, int gap)
        {
            foreach (Control c in bar.Controls) c.Margin = UiTheme.Pad(0, 0, gap, 0);
        }

        var root = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Bg };
        var header = new ToolHeader("图片格式转换",
            "在 PNG / JPG / BMP / GIF / TIFF 之间转换，另存不覆盖原图；透明区转 JPG / BMP 时填白。");

        // ---- 源文件卡片 ----
        var pathBox = UiTheme.Input("尚未选择图片…", 440);
        pathBox.ReadOnly = true;
        pathBox.Dock = DockStyle.Top;
        var infoLabel = UiTheme.HintLabel("支持 PNG / JPG / BMP / GIF / TIFF；本机系统组件不支持 WebP");
        var btnSelect = UiTheme.Button("选择图片", "folder", UiTheme.ButtonKind.Secondary, 120);
        var selectRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, WrapContents = false, Margin = Padding.Empty,
            // 上 8 与路径输入框拉开间距，下 6 与卡片底留呼吸感。
            Padding = UiTheme.Pad(0, 8, 0, 6)
        };
        selectRow.Controls.Add(btnSelect);
        selectRow.Controls.Add(infoLabel);
        AlignBar(selectRow, 8);
        // 说明文字与按钮同行：比 32px 按钮矮，下移 ≈7px 垂直居中，左侧与按钮留 12px。
        infoLabel.Margin = UiTheme.Pad(12, 7, 0, 0);
        var fileCard = new CardPanel("源图片") { Dock = DockStyle.Top, Height = UiTheme.Sx(156) };
        fileCard.HeaderIcon = "file";
        // 后加的先贴边：目标视觉 路径框 → 选择按钮 + 说明
        fileCard.Content.Controls.Add(selectRow);
        fileCard.Content.Controls.Add(pathBox);

        // ---- 目标格式 + 质量 ----
        var targetButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Margin = Padding.Empty };
        var formatButtons = new List<(FluentButton Btn, ImageFormatSpec Spec)>();
        foreach (var spec in ImageFormatCodec.Formats)
        {
            var btn = UiTheme.Button(spec.Display, UiTheme.ButtonKind.Secondary, 78, 30);
            targetButtons.Controls.Add(btn);
            formatButtons.Add((btn, spec));
        }
        foreach (Control c in targetButtons.Controls) c.Margin = UiTheme.Pad(0, 10, 6, 6);

        var qualityNumeric = new FluentNumeric { Minimum = 1, Maximum = 100, Size = UiTheme.Sz(90, 32) };
        qualityNumeric.Value = 90;
        var qualityHint = UiTheme.HintLabel("JPG 质量 1-100，越高越清晰、体积越大（默认 90）");
        var qualityRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, WrapContents = false, Margin = Padding.Empty,
            Padding = UiTheme.Pad(0, 4, 0, 0)
        };
        qualityRow.Controls.Add(qualityNumeric);
        qualityRow.Controls.Add(qualityHint);
        foreach (Control c in qualityRow.Controls) c.Margin = UiTheme.Pad(0, 0, 10, 0);
        // 说明文字比 32px 的数字框矮，压上顶会显得参差，下移到与数字框垂直居中。
        qualityHint.Margin = UiTheme.Pad(0, 7, 10, 0);

        var selected = formatButtons[0];
        void SelectFormat((FluentButton Btn, ImageFormatSpec Spec) chosen)
        {
            selected = chosen;
            foreach (var (btn, _) in formatButtons)
                btn.Variant = btn == chosen.Btn ? UiTheme.ButtonKind.Primary : UiTheme.ButtonKind.Secondary;
            qualityNumeric.Enabled = chosen.Spec.Id == "jpg";
            qualityHint.Text = chosen.Spec.Id == "jpg"
                ? "JPG 质量 1-100，越高越清晰、体积越大（默认 90）"
                : $"输出 {chosen.Spec.Display}，质量参数仅对 JPG 生效";
        }

        var optionsCard = new CardPanel("目标格式") { Dock = DockStyle.Top, Height = UiTheme.Sx(165) };
        // Fill 必须先加、Dock 控件后加：否则后加的 Fill 会占满内容区，把质量行挤掉（已踩坑）。
        optionsCard.Content.Controls.Add(targetButtons); // Fill 先加
        optionsCard.Content.Controls.Add(qualityRow);    // Top 后加贴顶
        SelectFormat(selected);

        // ---- 转换/保存卡片 ----
        var feedback = new FeedbackBar { Dock = DockStyle.Bottom };
        var previewBox = new PictureBox
        {
            Dock = DockStyle.Fill,
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = UiTheme.FieldBg,
            Margin = Padding.Empty,
            MinimumSize = UiTheme.Sz(0, 120)   // 未加载图片时不被压成一条线
        };
        var btnSave = UiTheme.Button("另存为…", "download", UiTheme.ButtonKind.Primary, 120);
        var actionBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, WrapContents = false, Margin = Padding.Empty,
            Padding = UiTheme.Pad(0, 0, 0, 8)
        };
        actionBar.Controls.Add(btnSave);
        var resultCard = new CardPanel("预览与另存") { Dock = DockStyle.Fill, Margin = Padding.Empty };
        resultCard.Content.Controls.Add(previewBox);  // Fill 先加
        resultCard.Content.Controls.Add(actionBar);   // Top 后加
        resultCard.Content.Controls.Add(feedback);    // Bottom 最后加贴底

        byte[]? sourceBytes = null;
        byte[]? convertedBytes = null;
        string sourcePath = "";

        void RenderPreview(byte[] bytes)
        {
            previewBox.Image?.Dispose();
            using var ms = new MemoryStream(bytes, writable: false);
            previewBox.Image = new Bitmap(ms);
        }

        void ConvertNow()
        {
            if (sourceBytes is null)
            {
                feedback.Show(FeedbackBar.Kind.Warn, "请先选择一张图片。");
                return;
            }
            var result = ImageFormatCodec.Convert(sourceBytes, selected.Spec, qualityNumeric.Value);
            if (!result.Success || result.Bytes is null)
            {
                feedback.Show(FeedbackBar.Kind.Error, result.ErrorMessage ?? "转换失败");
                return;
            }
            convertedBytes = result.Bytes;
            RenderPreview(convertedBytes);
            var alpha = result.AlphaFlattened ? "；透明区已合成到白底" : "";
            feedback.Show(FeedbackBar.Kind.Success,
                $"转换完成：{result.Width}×{result.Height}，{result.DetectedFormat ?? "未知"} → {selected.Spec.Display}，" +
                $"{ImageFormatCodec.FormatSize(sourceBytes.Length)} → {ImageFormatCodec.FormatSize(convertedBytes.Length)}{alpha}。");
        }

        btnSelect.Click += (_, _) =>
        {
            using var dlg = new OpenFileDialog
            {
                Title = "选择要转换的图片",
                Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|所有文件|*.*"
            };
            if (dlg.ShowDialog() != DialogResult.OK) return;
            try
            {
                sourceBytes = System.IO.File.ReadAllBytes(dlg.FileName);
                sourcePath = dlg.FileName;
                pathBox.Text = dlg.FileName;
                var detected = ImageFormatCodec.DetectByExtension(dlg.FileName) ?? "未知格式";
                infoLabel.Text = $"已识别：{detected} · {ImageFormatCodec.FormatSize(sourceBytes.Length)}";
                // 若扩展名能匹配到某个目标格式，自动选中它之外的首个格式，避免"原地转同格式"
                var srcId = ImageFormatCodec.IdFromExtension(dlg.FileName);
                var better = srcId is null ? formatButtons[0]
                    : formatButtons.FirstOrDefault(b => b.Spec.Id != srcId);
                if (better.Btn is not null) SelectFormat(better);
                ConvertNow();
            }
            catch (Exception ex)
            {
                feedback.Show(FeedbackBar.Kind.Error, $"读取图片失败：{ex.Message}");
            }
        };

        foreach (var entry in formatButtons)
            entry.Btn.Click += (_, _) =>
            {
                SelectFormat(entry);
                if (sourceBytes is not null) ConvertNow();
            };
        qualityNumeric.ValueChanged += (_, _) => { if (sourceBytes is not null) ConvertNow(); };

        btnSave.Click += (_, _) =>
        {
            if (convertedBytes is null)
            {
                feedback.Show(FeedbackBar.Kind.Warn, "请先选择图片并完成转换。");
                return;
            }
            var name = Path.GetFileNameWithoutExtension(sourcePath) + selected.Spec.Extension;
            using var dlg = new SaveFileDialog
            {
                Title = "另存转换后的图片（不会覆盖原图）",
                Filter = $"{selected.Spec.Display} 图片|*{selected.Spec.Extension}",
                FileName = name,
                InitialDirectory = Path.GetDirectoryName(sourcePath)
            };
            if (dlg.ShowDialog() != DialogResult.OK) return;
            if (string.Equals(Path.GetFullPath(dlg.FileName), Path.GetFullPath(sourcePath), StringComparison.OrdinalIgnoreCase))
            {
                feedback.Show(FeedbackBar.Kind.Error, "为保护原图，不能覆盖源文件本身，请换一个文件名。");
                return;
            }
            try
            {
                System.IO.File.WriteAllBytes(dlg.FileName, convertedBytes);
                feedback.Show(FeedbackBar.Kind.Success, $"已保存到：{dlg.FileName}");
            }
            catch (Exception ex)
            {
                feedback.Show(FeedbackBar.Kind.Error, $"保存失败：{ex.Message}");
            }
        };

        root.Controls.Add(resultCard);
        root.Controls.Add(UiTheme.CardGap());
        root.Controls.Add(optionsCard);
        root.Controls.Add(UiTheme.CardGap());
        root.Controls.Add(fileCard);
        root.Controls.Add(header);
        return root;
    }
}
