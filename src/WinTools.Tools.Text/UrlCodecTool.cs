using System.Drawing;
using System.Windows.Forms;
using WinTools.Core;
using WinTools.Core.Ui;

namespace WinTools.Tools.Text;

/// <summary>
/// URL 编解码工具：文本 → URL（空格为 %20）/ URL → 文本（+ 按空格处理）。
/// 纯逻辑在 UrlCodec，界面只负责收集输入、展示结果与反馈。
/// </summary>
[Tool(Order = 25)]
public sealed class UrlCodecTool : ITool
{
    public string Id => "url-codec";
    public string Name => "URL 编解码";
    public string Category => "文本工具";
    public Icon? Icon => null;

    public Control CreateUI()
    {
        static void AlignBar(FlowLayoutPanel bar, int rowH, int ctlH, int gap)
        {
            bar.Padding = UiTheme.Pad(0, (rowH - ctlH) / 2, 0, 0);
            foreach (Control c in bar.Controls) c.Margin = UiTheme.Pad(0, 0, gap, 0);
        }

        var root = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Bg };
        var header = new ToolHeader("URL 编解码",
            "文本与 URL 编码双向转换；空格编码为 %20，解码时 + 按空格处理，中文不乱码。");

        var btnEncode = UiTheme.Button("文本 → URL", UiTheme.ButtonKind.Primary, 150);
        var btnDecode = UiTheme.Button("URL → 文本", UiTheme.ButtonKind.Secondary, 150);
        var mode = 0;
        void SetMode(int m)
        {
            mode = m;
            btnEncode.Variant = m == 0 ? UiTheme.ButtonKind.Primary : UiTheme.ButtonKind.Secondary;
            btnDecode.Variant = m == 1 ? UiTheme.ButtonKind.Primary : UiTheme.ButtonKind.Secondary;
        }
        btnEncode.Click += (_, _) => SetMode(0);
        btnDecode.Click += (_, _) => SetMode(1);

        var inputBox = UiTheme.MultilineInput();
        var btnConvert = UiTheme.Button("转换");
        var btnSwap = UiTheme.Button("交换", UiTheme.ButtonKind.Secondary);
        var btnClear = UiTheme.Button("清空", UiTheme.ButtonKind.Secondary);

        var buttonBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = UiTheme.Sx(42),
            WrapContents = false,
            Margin = Padding.Empty
        };
        buttonBar.Controls.Add(btnEncode);
        buttonBar.Controls.Add(btnDecode);
        buttonBar.Controls.Add(btnConvert);
        buttonBar.Controls.Add(btnSwap);
        buttonBar.Controls.Add(btnClear);
        AlignBar(buttonBar, 42, 32, 8);

        var inputCard = new CardPanel("输入") { Dock = DockStyle.Top, Height = UiTheme.Sx(230) };
        // 同 Base64：清零内容区下内边距，按钮条贴底，按钮在条内上下居中。
        inputCard.Content.Padding = UiTheme.Pad(16, 14, 16, 0);
        inputCard.Content.Controls.Add(inputBox);
        inputCard.Content.Controls.Add(buttonBar);

        var resultBox = UiTheme.MultilineInput(readOnly: true);
        var feedback = new FeedbackBar { Dock = DockStyle.Bottom };
        var resultCard = new CardPanel("结果") { Dock = DockStyle.Fill, Margin = new Padding(0) };
        resultCard.AddAction("复制", (_, _) => Copy());
        resultCard.Content.Controls.Add(resultBox);
        resultCard.Content.Controls.Add(feedback);

        void Copy()
        {
            if (string.IsNullOrEmpty(resultBox.Text))
            {
                feedback.Show(FeedbackBar.Kind.Warn, "暂无可复制的内容，请先转换。");
                return;
            }
            if (ClipboardUtil.TryCopy(resultBox.Text))
                feedback.Show(FeedbackBar.Kind.Success, "已复制到剪贴板。");
            else
                feedback.Show(FeedbackBar.Kind.Warn, "剪贴板被其他程序占用，复制失败，请重试。");
        }

        btnConvert.Click += (_, _) =>
        {
            var isEncode = mode == 0;
            var result = isEncode ? UrlCodec.Encode(inputBox.Text) : UrlCodec.Decode(inputBox.Text);
            if (result.Success)
            {
                resultBox.Text = result.Text;
                feedback.Clear();
                return;
            }
            resultBox.Clear();
            feedback.Show(FeedbackBar.Kind.Error,
                $"{result.ErrorMessage}。建议：{(isEncode ? "确认输入内容不为空且不含孤立代理项。" : "确认 % 后跟两位十六进制数，且内容为有效 UTF-8。")}");
        };

        btnSwap.Click += (_, _) =>
        {
            if (string.IsNullOrEmpty(resultBox.Text))
            {
                feedback.Show(FeedbackBar.Kind.Warn, "暂无结果可交换，请先转换。");
                return;
            }
            inputBox.Text = resultBox.Text;
            SetMode(mode == 0 ? 1 : 0);
            resultBox.Clear();
            feedback.Clear();
            inputBox.Focus();
        };

        btnClear.Click += (_, _) =>
        {
            inputBox.Clear();
            resultBox.Clear();
            feedback.Clear();
            inputBox.Focus();
        };

        inputBox.Text = "https://example.com/search?q=你好 WinTools";

        root.Controls.Add(resultCard);
        root.Controls.Add(UiTheme.CardGap());
        root.Controls.Add(inputCard);
        root.Controls.Add(header);
        return root;
    }
}
