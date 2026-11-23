using System.Drawing;
using System.Windows.Forms;
using WinTools.Core;
using WinTools.Core.Ui;

namespace WinTools.Tools.Files;

/// <summary>
/// 公考/事业单位报名照片处理：红/蓝/白底互换、尺寸规格化、限体积，另存不覆盖。
/// 实现 ITool，宿主零改动即可自动发现。
/// </summary>
[Tool(Order = 97)]
public sealed class ExamPhotoTool : ITool
{
    public string Id => "exam-photo";
    public string Name => "报名照片处理";
    public string Category => "图片工具";
    public Icon? Icon => null;

    public Control CreateUI()
    {
        var root = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Bg };
        var header = new ToolHeader("报名照片处理",
            "红 / 蓝 / 白底任意互换，可选原图尺寸或一寸、二寸等规格，压缩到指定 KB；全程本地处理，另存新文件不覆盖原图。");

        // ---- 参数区控件：先全部声明，本地函数才能安全捕获（手册迭代 7 的 CS0841 坑）----
        var btnSelect = UiTheme.Button("选择照片", "folder", UiTheme.ButtonKind.Secondary, 120);
        var btnProcess = UiTheme.Button("开始处理", UiTheme.ButtonKind.Primary, 110);
        var btnSave = UiTheme.Button("另存结果", "download", UiTheme.ButtonKind.Secondary, 110);
        var btnReset = UiTheme.Button("重置", "trash", UiTheme.ButtonKind.Secondary, 90);

        var chkReplace = new FluentCheckBox { Text = "替换背景", Checked = true, AutoSize = true };
        var btnSrcRed = UiTheme.Button("红底", UiTheme.ButtonKind.Subtle, 54);
        var btnSrcBlue = UiTheme.Button("蓝底", UiTheme.ButtonKind.Subtle, 54);
        var btnSrcWhite = UiTheme.Button("白底", UiTheme.ButtonKind.Subtle, 54);
        var toleranceNumeric = new FluentNumeric { Minimum = 0, Maximum = 100, Size = UiTheme.Sz(90, 32) };
        toleranceNumeric.Value = 40;

        var btnBlue = UiTheme.Button("蓝底", UiTheme.ButtonKind.Subtle, 54);
        var btnWhite = UiTheme.Button("白底", UiTheme.ButtonKind.Subtle, 54);
        var btnRed = UiTheme.Button("红底", UiTheme.ButtonKind.Subtle, 54);
        var colorInput = UiTheme.Input("#438EDB", 110);

        var sizeCombo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat,
            Size = UiTheme.Sz(180, 32),
            Font = UiTheme.Body,
            ForeColor = UiTheme.Text,
            BackColor = UiTheme.FieldBg
        };
        sizeCombo.Items.Add("原图尺寸（不裁剪）");
        foreach (var (name, _) in ExamPhotoProcessor.SizePresets) sizeCombo.Items.Add(name);
        sizeCombo.Items.Add("自定义尺寸");
        sizeCombo.SelectedIndex = 1; // 一寸

        var widthNumeric = new FluentNumeric { Minimum = 1, Maximum = 2000, Size = UiTheme.Sz(80, 32) };
        var heightNumeric = new FluentNumeric { Minimum = 1, Maximum = 2000, Size = UiTheme.Sz(80, 32) };
        widthNumeric.Value = 295;
        heightNumeric.Value = 413;
        var customRow = Row(UiTheme.FieldLabel("自定义尺寸"), widthNumeric, UiTheme.HintLabel("×"),
            heightNumeric, UiTheme.HintLabel("像素，宽 × 高；等比居中裁剪，不拉伸变形"));
        customRow.Visible = false;

        var sizeNumeric = new FluentNumeric { Minimum = 1, Maximum = 1000, Size = UiTheme.Sz(90, 32) };
        sizeNumeric.Value = 30;
        var chkNoLimit = new FluentCheckBox { Text = "不限制大小", Checked = false, AutoSize = true };

        var selectRow = Row(btnSelect, btnProcess, btnSave, btnReset);
        var modeRow = Row(chkReplace, UiTheme.FieldLabel("源底色"), btnSrcRed, btnSrcBlue, btnSrcWhite,
            UiTheme.FieldLabel("容差"), toleranceNumeric,
            UiTheme.HintLabel("0-100，先选对源底色；肤色不会被误换"));
        var colorRow = Row(UiTheme.FieldLabel("目标底色"), btnBlue, btnWhite, btnRed, colorInput,
            UiTheme.HintLabel("支持 #RRGGBB；蓝 #438EDB，白 #FFFFFF，红 #FF0000"));
        var sizeRow = Row(UiTheme.FieldLabel("尺寸规格"), sizeCombo, chkNoLimit,
            UiTheme.FieldLabel("大小上限"), sizeNumeric, UiTheme.HintLabel("KB；勾「不限制」则不压缩体积"));

        // 体积不限制时禁掉数字框；选自定义规格才显示宽高输入。
        void SyncOptions()
        {
            sizeNumeric.Enabled = !chkNoLimit.Checked;
            customRow.Visible = sizeCombo.SelectedIndex == sizeCombo.Items.Count - 1;
        }
        chkNoLimit.CheckedChanged += (_, _) => SyncOptions();
        sizeCombo.SelectedIndexChanged += (_, _) => SyncOptions();

        var paramCard = new CardPanel("处理参数") { Dock = DockStyle.Top, Height = UiTheme.Sx(280) };
        // Fill 先加，再加 Bottom 说明、最后逐行 Top；同向 Dock 后加的贴更上方。
        var filler = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        paramCard.Content.Controls.Add(filler);
        paramCard.Content.Controls.Add(sizeRow);
        paramCard.Content.Controls.Add(customRow);
        paramCard.Content.Controls.Add(colorRow);
        paramCard.Content.Controls.Add(modeRow);
        paramCard.Content.Controls.Add(selectRow);
        SyncOptions();

        // ---- 预览与结果 ----
        var preview = new PictureBox
        {
            Dock = DockStyle.Fill,
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = UiTheme.Layer,
            Margin = Padding.Empty
        };
        var noteLabel = UiTheme.HintLabel("选择一张证件照开始处理");
        noteLabel.Dock = DockStyle.Top;
        noteLabel.Margin = UiTheme.Pad(0, 0, 0, 8);
        var feedback = new FeedbackBar { Dock = DockStyle.Bottom };
        var resultCard = new CardPanel("预览与结果") { Dock = DockStyle.Fill, Margin = Padding.Empty };
        resultCard.HeaderIcon = "id-card";
        resultCard.Content.Controls.Add(preview);
        resultCard.Content.Controls.Add(feedback);
        resultCard.Content.Controls.Add(noteLabel);

        byte[]? sourceBytes = null;
        byte[]? resultBytes = null;
        Image? previewImage = null;
        var sourcePath = string.Empty;
        var sourceBackdrop = ExamBackdropSource.Red;

        void SelectSource(ExamBackdropSource value)
        {
            sourceBackdrop = value;
            btnSrcRed.Variant = value == ExamBackdropSource.Red ? UiTheme.ButtonKind.Primary : UiTheme.ButtonKind.Subtle;
            btnSrcBlue.Variant = value == ExamBackdropSource.Blue ? UiTheme.ButtonKind.Primary : UiTheme.ButtonKind.Subtle;
            btnSrcWhite.Variant = value == ExamBackdropSource.White ? UiTheme.ButtonKind.Primary : UiTheme.ButtonKind.Subtle;
        }

        void SelectTarget(Color value)
        {
            colorInput.Text = $"#{value.R:X2}{value.G:X2}{value.B:X2}";
            var argb = value.ToArgb();
            btnBlue.Variant = argb == Color.FromArgb(0x43, 0x8E, 0xDB).ToArgb()
                ? UiTheme.ButtonKind.Primary : UiTheme.ButtonKind.Subtle;
            btnWhite.Variant = argb == Color.White.ToArgb()
                ? UiTheme.ButtonKind.Primary : UiTheme.ButtonKind.Subtle;
            btnRed.Variant = argb == Color.FromArgb(255, 0, 0).ToArgb()
                ? UiTheme.ButtonKind.Primary : UiTheme.ButtonKind.Subtle;
        }

        SelectSource(ExamBackdropSource.Red);
        SelectTarget(Color.FromArgb(0x43, 0x8E, 0xDB));

        void ShowPreview(byte[] bytes)
        {
            using var ms = new MemoryStream(bytes, writable: false);
            var image = Image.FromStream(ms);
            previewImage?.Dispose();
            previewImage = new Bitmap(image);
            preview.Image = previewImage;
        }

        void Reset()
        {
            sourceBytes = null;
            resultBytes = null;
            sourcePath = string.Empty;
            previewImage?.Dispose();
            previewImage = null;
            preview.Image = null;
            noteLabel.Text = "选择一张证件照开始处理";
            feedback.Clear();
        }

        btnSelect.Click += (_, _) =>
        {
            using var dlg = new OpenFileDialog
            {
                Title = "选择报名照片",
                Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|所有文件|*.*"
            };
            if (dlg.ShowDialog() != DialogResult.OK) return;
            try
            {
                sourceBytes = System.IO.File.ReadAllBytes(dlg.FileName);
                sourcePath = dlg.FileName;
                resultBytes = null;
                ShowPreview(sourceBytes);
                noteLabel.Text = $"已载入 {Path.GetFileName(dlg.FileName)}（{ImageFormatCodec.FormatSize(sourceBytes.Length)}），点「开始处理」预览结果。";
                feedback.Show(FeedbackBar.Kind.Info, "原图只在内存中读取，处理后会另存为新文件。");
            }
            catch (Exception ex)
            {
                feedback.Show(FeedbackBar.Kind.Error, $"读取失败：{ex.Message}");
            }
        };

        btnProcess.Click += (_, _) =>
        {
            if (sourceBytes is null)
            {
                feedback.Show(FeedbackBar.Kind.Warn, "请先选择照片。");
                return;
            }
            if (!ExamPhotoProcessor.TryParseColor(colorInput.Text, out var parsed))
            {
                feedback.Show(FeedbackBar.Kind.Warn, "目标底色不是有效的 #RRGGBB，例如 #438EDB。");
                return;
            }
            SelectTarget(parsed);

            Size? targetSize = sizeCombo.SelectedIndex switch
            {
                0 => null,
                var i when i == sizeCombo.Items.Count - 1 =>
                    new Size((int)widthNumeric.Value, (int)heightNumeric.Value),
                _ => ExamPhotoProcessor.SizePresets[sizeCombo.SelectedIndex - 1].Size
            };

            var options = new ExamPhotoOptions
            {
                TargetBytes = chkNoLimit.Checked ? 0 : (int)sizeNumeric.Value * 1024,
                TargetSize = targetSize,
                ReplaceBackdrop = chkReplace.Checked,
                SourceBackdrop = sourceBackdrop,
                TargetBackdrop = parsed,
                Tolerance = toleranceNumeric.Value
            };
            var result = ExamPhotoProcessor.Process(sourceBytes, options);
            if (!result.Success || result.Bytes is null)
            {
                resultBytes = null;
                feedback.Show(FeedbackBar.Kind.Error, result.ErrorMessage ?? "处理失败。");
                return;
            }

            resultBytes = result.Bytes;
            ShowPreview(resultBytes);
            noteLabel.Text = $"{result.Width}×{result.Height} · {ImageFormatCodec.FormatSize(resultBytes.Length)} · 质量 {result.Quality}"
                + (string.IsNullOrEmpty(result.Notes.FirstOrDefault()) ? string.Empty : " · " + string.Join("；", result.Notes));
            feedback.Show(FeedbackBar.Kind.Success, "处理完成，预览已更新。确认后点「另存结果」，原图不会被覆盖。");
        };

        btnSave.Click += (_, _) =>
        {
            if (resultBytes is null)
            {
                feedback.Show(FeedbackBar.Kind.Warn, "请先处理照片，再另存结果。");
                return;
            }
            var suggested = string.IsNullOrEmpty(sourcePath)
                ? "photo_exam.jpg"
                : Path.Combine(Path.GetDirectoryName(sourcePath) ?? "",
                    Path.GetFileNameWithoutExtension(sourcePath) + "_exam.jpg");
            using var dlg = new SaveFileDialog
            {
                Title = "另存处理后的照片",
                Filter = "JPG 图片|*.jpg;*.jpeg",
                FileName = suggested
            };
            if (dlg.ShowDialog() != DialogResult.OK) return;
            if (string.Equals(Path.GetFullPath(dlg.FileName), Path.GetFullPath(sourcePath), StringComparison.OrdinalIgnoreCase))
            {
                feedback.Show(FeedbackBar.Kind.Warn, "输出路径与源图相同，已拒绝覆盖原图，请换个文件名。");
                return;
            }
            try
            {
                System.IO.File.WriteAllBytes(dlg.FileName, resultBytes);
                feedback.Show(FeedbackBar.Kind.Success, $"已保存：{dlg.FileName}");
            }
            catch (Exception ex)
            {
                feedback.Show(FeedbackBar.Kind.Error, $"保存失败：{ex.Message}");
            }
        };

        btnSrcRed.Click += (_, _) => SelectSource(ExamBackdropSource.Red);
        btnSrcBlue.Click += (_, _) => SelectSource(ExamBackdropSource.Blue);
        btnSrcWhite.Click += (_, _) => SelectSource(ExamBackdropSource.White);
        btnBlue.Click += (_, _) => SelectTarget(Color.FromArgb(0x43, 0x8E, 0xDB));
        btnWhite.Click += (_, _) => SelectTarget(Color.White);
        btnRed.Click += (_, _) => SelectTarget(Color.FromArgb(255, 0, 0));
        btnReset.Click += (_, _) => Reset();

        root.Controls.Add(resultCard);
        root.Controls.Add(UiTheme.CardGap());
        root.Controls.Add(paramCard);
        root.Controls.Add(header);
        return root;
    }

    /// <summary>一行参数：按数字框高度给同行标签/勾选框做几何居中。</summary>
    private static FlowLayoutPanel Row(params Control[] controls)
    {
        var row = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = UiTheme.Pad(0, 0, 0, 8)
        };
        foreach (var c in controls)
        {
            c.Margin = new Padding(0, Math.Max(0, (UiTheme.Sx(32) - c.Height) / 2), UiTheme.Sx(10), 0);
            row.Controls.Add(c);
        }
        return row;
    }
}
