using System.Drawing;
using System.Windows.Forms;
using WinTools.Core;
using WinTools.Core.Ui;

namespace WinTools.Tools.Text;

/// <summary>
/// Base64 编解码工具：实现 ITool，由 Shell 反射发现。
/// 输入 → 操作 → 结果 → 反馈，中文往返不乱码。
/// </summary>
[Tool(Order = 20)]
public sealed class Base64Tool : ITool
{
    public string Id => "base64-codec";
    public string Name => "Base64 编解码";
    public string Category => "文本工具";
    public Icon? Icon => null;

    public Control CreateUI()
    {
        // 操作条统一：间隙与上下留白都按设计像素算出来再缩放，
        // 直接用控件默认的 3px 边距在 200% 下不放大，一排按钮会挤在一起、且贴着条顶。
        static void AlignBar(FlowLayoutPanel bar, int rowH, int ctlH, int gap)
        {
            bar.Padding = UiTheme.Pad(0, (rowH - ctlH) / 2, 0, 0);
            foreach (Control c in bar.Controls) c.Margin = UiTheme.Pad(0, 0, gap, 0);
        }

        var root = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Bg };
        var header = new ToolHeader("Base64 编解码", "文本与 Base64 双向转换，中文往返不乱码。");

        // ---- 输入区 ----
        // 模式切换用一对自绘按钮而不是原生 ComboBox：下拉框的箭头按钮由系统绘制，暗色主题下压不住。
        var btnEncode = UiTheme.Button("文本 → Base64", UiTheme.ButtonKind.Primary, 150);
        var btnDecode = UiTheme.Button("Base64 → 文本", UiTheme.ButtonKind.Secondary, 150);
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
        // 操作条排在输入框下方（原型顺序），并关掉换行：定高条一旦换行，第二排按钮会被裁掉。
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
        // 按钮条 Dock=Bottom 贴在内容区底边：若保留内容区默认 14px 下内边距，
        // 那段留白会叠在按钮条下面，按钮看上去偏上、不居中。底部清零让条贴边，按钮在条内 5/5 居中。
        inputCard.Content.Padding = UiTheme.Pad(16, 14, 16, 0);
        inputCard.Content.Controls.Add(inputBox);
        inputCard.Content.Controls.Add(buttonBar);

        // ---- 结果区 ----
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
            var result = isEncode ? Base64Codec.Encode(inputBox.Text) : Base64Codec.Decode(inputBox.Text);
            if (result.Success)
            {
                resultBox.Text = result.Text;
                feedback.Clear();
            }
            else
            {
                resultBox.Clear();
                feedback.Show(FeedbackBar.Kind.Error,
                    $"{result.ErrorMessage}。建议：{(isEncode ? "确认输入内容不为空。" : "确认输入为标准 Base64 串（可含换行），字符集 A-Z a-z 0-9 + / =。")}");
            }
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

        inputBox.Text = "WinTools 工具箱 · 中文往返测试";

        root.Controls.Add(resultCard);
        root.Controls.Add(UiTheme.CardGap());
        root.Controls.Add(inputCard);
        root.Controls.Add(header);
        return root;
    }
}
