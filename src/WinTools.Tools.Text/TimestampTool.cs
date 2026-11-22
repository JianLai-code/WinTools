using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using WinTools.Core;
using WinTools.Core.Ui;

namespace WinTools.Tools.Text;

/// <summary>
/// 时间戳转换工具：Unix 秒/毫秒 ↔ 日期时间，本地与 UTC 对照。
/// 纯逻辑在 TimestampConverter，界面只负责模式切换、输入与结果展示。
/// </summary>
[Tool(Order = 35)]
public sealed class TimestampTool : ITool
{
    public string Id => "timestamp-converter";
    public string Name => "时间戳转换";
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
        var header = new ToolHeader("时间戳转换",
            "Unix 时间戳与日期时间互转；10 位按秒、13 位按毫秒自动识别，同时给出本地与 UTC 时间。");

        var btnToDate = UiTheme.Button("时间戳 → 日期", UiTheme.ButtonKind.Primary, 160);
        var btnToStamp = UiTheme.Button("日期 → 时间戳", UiTheme.ButtonKind.Secondary, 160);
        var inputBox = UiTheme.Input("例如 1700000000", 440);
        var lblInput = UiTheme.FieldLabel("输入");
        var mode = 0;
        void SetMode(int m)
        {
            mode = m;
            btnToDate.Variant = m == 0 ? UiTheme.ButtonKind.Primary : UiTheme.ButtonKind.Secondary;
            btnToStamp.Variant = m == 1 ? UiTheme.ButtonKind.Primary : UiTheme.ButtonKind.Secondary;
            inputBox.PlaceholderText = m == 0
                ? "例如 1700000000 或 1700000000123"
                : "例如 2026-11-21 18:00:00 或 2026-11-21T10:00:00Z";
        }
        btnToDate.Click += (_, _) => SetMode(0);
        btnToStamp.Click += (_, _) => SetMode(1);

        var inputRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, Height = UiTheme.Sx(42), WrapContents = false, Margin = Padding.Empty
        };
        inputRow.Controls.Add(lblInput);
        inputRow.Controls.Add(inputBox);
        lblInput.Margin = UiTheme.Pad(0, 9, 10, 0);
        inputBox.Margin = UiTheme.Pad(0, 5, 0, 0);

        var hint = UiTheme.HintLabel("日期不含偏移时按本机时区解释；带 Z 或 +08:00 时按显式偏移解析。");
        var hintRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, Height = UiTheme.Sx(22), WrapContents = false, Margin = Padding.Empty
        };
        hintRow.Controls.Add(hint);
        hint.Margin = UiTheme.Pad(0, 1, 0, 0);

        var btnConvert = UiTheme.Button("转换");
        var btnNow = UiTheme.Button("填入当前时间戳", UiTheme.ButtonKind.Secondary, 130);
        var btnClear = UiTheme.Button("清空", UiTheme.ButtonKind.Secondary);
        var buttonBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, Height = UiTheme.Sx(42), WrapContents = false, Margin = Padding.Empty
        };
        buttonBar.Controls.Add(btnConvert);
        buttonBar.Controls.Add(btnNow);
        buttonBar.Controls.Add(btnClear);
        AlignBar(buttonBar, 42, 32, 8);

        var modeRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, Height = UiTheme.Sx(42), WrapContents = false, Margin = Padding.Empty
        };
        modeRow.Controls.Add(btnToDate);
        modeRow.Controls.Add(btnToStamp);
        AlignBar(modeRow, 42, 32, 8);

        // 高度实算：表头 40 + 描边 2 + 上内边距 14 + 模式行 42 + 输入行 42 + 说明行 22 + 按钮条 42 = 204。
        // 此前 228 多出约 24px 空档，说明文字与按钮行之间隔着一条大缝。
        var inputCard = new CardPanel("输入") { Dock = DockStyle.Top, Height = UiTheme.Sx(206) };
        // 同 Base64：清零内容区下内边距，按钮条贴底，按钮在条内上下居中。
        inputCard.Content.Padding = UiTheme.Pad(16, 14, 16, 0);
        // 同向 Dock 后加的排更靠上：模式 → 输入 → 说明 → 按钮（按钮用 Bottom 固定贴底）。
        inputCard.Content.Controls.Add(buttonBar);
        inputCard.Content.Controls.Add(hintRow);
        inputCard.Content.Controls.Add(inputRow);
        inputCard.Content.Controls.Add(modeRow);

        var resultBox = UiTheme.MultilineInput(readOnly: true);
        var feedback = new FeedbackBar { Dock = DockStyle.Bottom };
        var resultCard = new CardPanel("转换结果") { Dock = DockStyle.Fill, Margin = new Padding(0) };
        resultCard.AddAction("复制", (_, _) => Copy());
        resultCard.Content.Controls.Add(resultBox);
        resultCard.Content.Controls.Add(feedback);

        void Copy()
        {
            if (string.IsNullOrEmpty(resultBox.Text))
            {
                feedback.Show(FeedbackBar.Kind.Warn, "暂无可复制的结果，请先转换。");
                return;
            }
            if (ClipboardUtil.TryCopy(resultBox.Text))
                feedback.Show(FeedbackBar.Kind.Success, "已复制到剪贴板。");
            else
                feedback.Show(FeedbackBar.Kind.Warn, "剪贴板被其他程序占用，复制失败，请重试。");
        }

        btnConvert.Click += (_, _) =>
        {
            var result = mode == 0
                ? TimestampConverter.FromTimestamp(inputBox.Text)
                : TimestampConverter.FromDateTime(inputBox.Text);
            if (result.Success)
            {
                resultBox.Text = result.Text;
                feedback.Clear();
            }
            else
            {
                resultBox.Clear();
                feedback.Show(FeedbackBar.Kind.Error,
                    $"{result.ErrorMessage}。建议：{(mode == 0 ? "输入整数秒或毫秒时间戳。" : "使用 yyyy-MM-dd HH:mm:ss 或 ISO 8601 格式。")}");
            }
        };

        btnNow.Click += (_, _) =>
        {
            SetMode(0);
            inputBox.Text = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
            resultBox.Clear();
            feedback.Show(FeedbackBar.Kind.Info, "已填入当前 Unix 秒级时间戳，点击「转换」查看对应日期。");
            inputBox.Focus();
        };

        btnClear.Click += (_, _) =>
        {
            inputBox.Text = string.Empty;
            resultBox.Clear();
            feedback.Clear();
            inputBox.Focus();
        };

        inputBox.Text = "1700000000";

        root.Controls.Add(resultCard);
        root.Controls.Add(UiTheme.CardGap());
        root.Controls.Add(inputCard);
        root.Controls.Add(header);
        return root;
    }
}
