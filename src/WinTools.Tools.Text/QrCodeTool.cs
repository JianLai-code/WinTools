using System.Drawing;
using System.IO;
using System.Windows.Forms;
using WinTools.Core;
using WinTools.Core.Ui;

namespace WinTools.Tools.Text;

/// <summary>二维码生成工具：文本 / 链接 → PNG，可另存。实现 ITool，由 Shell 反射发现。</summary>
[Tool(Order = 90)]
public sealed class QrCodeTool : ITool
{
    public string Id => "qr-code-generator";
    public string Name => "二维码生成";
    public string Category => "文本工具";
    public Icon? Icon => null;

    public Control CreateUI()
    {
        var root = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Bg };
        var header = new ToolHeader("二维码生成", "把文本或链接生成二维码图片，全程本地计算，不上传。");

        // ---- 输入区 ----
        var inputBox = UiTheme.MultilineInput();
        inputBox.Text = "https://example.com";
        var btnGenerate = UiTheme.Button("生成二维码", UiTheme.ButtonKind.Primary, 130);
        var btnClear = UiTheme.Button("清空", UiTheme.ButtonKind.Secondary, 80);
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, Height = UiTheme.Sx(42), WrapContents = false, Margin = Padding.Empty
        };
        bar.Padding = UiTheme.Pad(0, 5, 0, 0);
        bar.Controls.Add(btnGenerate);
        bar.Controls.Add(btnClear);
        foreach (Control c in bar.Controls) c.Margin = UiTheme.Pad(0, 0, 8, 0);

        var inputCard = new CardPanel("内容") { Dock = DockStyle.Top, Height = UiTheme.Sx(180) };
        inputCard.HeaderIcon = "qrcode";
        inputCard.Content.Controls.Add(inputBox);
        inputCard.Content.Controls.Add(bar);

        // ---- 结果区 ----
        var picture = new PictureBox
        {
            Dock = DockStyle.Fill,
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.White,
        };
        var feedback = new FeedbackBar { Dock = DockStyle.Bottom };
        byte[]? currentPng = null;
        var resultCard = new CardPanel("二维码") { Dock = DockStyle.Fill, Margin = Padding.Empty };
        resultCard.HeaderIcon = "qrcode";
        resultCard.AddAction("保存 PNG", "download", (_, _) => Save());
        resultCard.AddAction("复制图片", "copy", (_, _) => Copy());
        resultCard.Content.Controls.Add(picture);
        resultCard.Content.Controls.Add(feedback);

        void Generate()
        {
            var result = QrCodec.Generate(inputBox.Text);
            if (!result.Success)
            {
                feedback.Show(FeedbackBar.Kind.Error, result.ErrorMessage ?? "生成失败");
                return;
            }
            // PictureBox 会持有 Image 流，先用 MemoryStream 加载并 Clone，避免流被回收后绘制失败
            using var ms = new MemoryStream(result.Png!);
            picture.Image?.Dispose();
            picture.Image = new Bitmap(ms);
            currentPng = result.Png;
            feedback.Show(FeedbackBar.Kind.Success,
                $"已生成 {result.ModuleCount}×{result.ModuleCount} 二维码，可保存 PNG 或复制图片。");
        }

        void Save()
        {
            if (currentPng is null)
            {
                feedback.Show(FeedbackBar.Kind.Warn, "请先生成二维码再保存。");
                return;
            }
            using var dlg = new SaveFileDialog
            {
                Title = "保存二维码图片", Filter = "PNG 图片|*.png", FileName = "二维码.png"
            };
            if (dlg.ShowDialog() != DialogResult.OK) return;
            try
            {
                File.WriteAllBytes(dlg.FileName, currentPng);
                feedback.Show(FeedbackBar.Kind.Success, $"已保存到：{dlg.FileName}");
            }
            catch (Exception ex)
            {
                feedback.Show(FeedbackBar.Kind.Error, $"保存失败：{ex.Message}");
            }
        }

        void Copy()
        {
            if (picture.Image is null)
            {
                feedback.Show(FeedbackBar.Kind.Warn, "请先生成二维码再复制。");
                return;
            }
            try
            {
                Clipboard.SetImage(picture.Image);
                feedback.Show(FeedbackBar.Kind.Success, "二维码图片已复制到剪贴板。");
            }
            catch (Exception ex)
            {
                feedback.Show(FeedbackBar.Kind.Warn, $"剪贴板被占用，复制失败：{ex.Message}");
            }
        }

        btnGenerate.Click += (_, _) => Generate();
        btnClear.Click += (_, _) =>
        {
            inputBox.Clear();
            picture.Image?.Dispose();
            picture.Image = null;
            currentPng = null;
            feedback.Clear();
            inputBox.Focus();
        };
        root.Disposed += (_, _) => picture.Image?.Dispose();

        root.Controls.Add(resultCard);
        root.Controls.Add(UiTheme.CardGap());
        root.Controls.Add(inputCard);
        root.Controls.Add(header);
        return root;
    }
}
