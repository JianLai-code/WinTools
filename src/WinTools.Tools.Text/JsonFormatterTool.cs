using System.Drawing;
using System.Windows.Forms;
using WinTools.Core;
using WinTools.Core.Ui;

namespace WinTools.Tools.Text;

/// <summary>
/// JSON 格式化工具：实现 ITool，由 Shell 反射发现。
/// 界面遵循阶段四规范：输入 → 操作 → 结果 → 反馈（错误就地显示，不弹窗打断）。
/// </summary>
[Tool(Order = 10)]
public sealed class JsonFormatterTool : ITool
{
    public string Id => "json-formatter";
    public string Name => "JSON 格式化";
    public string Category => "文本工具";
    public Icon? Icon => null;

    public Control CreateUI()
    {
        // 操作条统一：间隙与上下留白都按设计像素算出来再缩放，
        // 直接用控件默认的 3px 边距在 200% 下不放大，一排按钮就会挤在一起、且贴着条顶。
        static void AlignBar(FlowLayoutPanel bar, int rowH, int ctlH, int gap)
        {
            bar.Padding = UiTheme.Pad(0, (rowH - ctlH) / 2, 0, 0);
            foreach (Control c in bar.Controls) c.Margin = UiTheme.Pad(0, 0, gap, 0);
        }

        var root = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Bg, Padding = new Padding(0) };

        // ---- 工具头 ----
        var header = new ToolHeader("JSON 格式化 / 校验", "粘贴 JSON 一键美化；语法错误会指出行号与原因，中文不乱码。");

        // ---- 输入区 ----
        var inputBox = UiTheme.MultilineInput();
        var btnFormat = UiTheme.Button("格式化");
        var btnCopy = UiTheme.Button("复制结果", UiTheme.ButtonKind.Secondary);
        var btnClear = UiTheme.Button("清空", UiTheme.ButtonKind.Secondary);
        // 操作条排在输入框下方（与原型一致）；定高条关掉换行，否则窄面板会把按钮挤进看不见的第二行。
        var buttonBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = UiTheme.Sx(42),
            AutoSize = false,
            WrapContents = false,
            Margin = Padding.Empty
        };
        buttonBar.Controls.Add(btnFormat);
        buttonBar.Controls.Add(btnCopy);
        buttonBar.Controls.Add(btnClear);
        AlignBar(buttonBar, 42, 32, 8);

        var inputCard = new CardPanel("输入 JSON") { Dock = DockStyle.Top, Height = UiTheme.Sx(250) };
        // 同 Base64：清零内容区下内边距，按钮条贴底，按钮在条内上下居中。
        inputCard.Content.Padding = UiTheme.Pad(16, 14, 16, 0);
        inputCard.Content.Controls.Add(inputBox);   // Fill
        inputCard.Content.Controls.Add(buttonBar);  // Bottom

        // ---- 结果区（标题栏内置复制按钮，符合原型）----
        var resultBox = UiTheme.MultilineInput(readOnly: true);
        var feedback = new FeedbackBar { Dock = DockStyle.Bottom };
        var resultCard = new CardPanel("格式化结果") { Dock = DockStyle.Fill, Margin = new Padding(0) };
        resultCard.AddAction("复制", (_, _) => CopyResult());
        resultCard.Content.Controls.Add(resultBox);
        resultCard.Content.Controls.Add(feedback);

        // ---- 事件 ----
        btnFormat.Click += (_, _) =>
        {
            var result = JsonFormatter.Format(inputBox.Text);
            if (result.Success)
            {
                resultBox.Text = result.Text;
                feedback.Clear();
            }
            else
            {
                resultBox.Clear();
                var location = result.LineNumber.HasValue ? $"（第 {result.LineNumber} 行）" : "";
                feedback.Show(FeedbackBar.Kind.Error,
                    $"格式错误{location}：{result.ErrorMessage}。建议：检查括号、引号是否配对，或逗号是否多余。");
            }
        };

        void CopyResult()
        {
            if (string.IsNullOrEmpty(resultBox.Text))
            {
                feedback.Show(FeedbackBar.Kind.Warn, "暂无可复制的内容，请先格式化。");
                return;
            }
            if (ClipboardUtil.TryCopy(resultBox.Text))
                feedback.Show(FeedbackBar.Kind.Success, "已复制到剪贴板。");
            else
                feedback.Show(FeedbackBar.Kind.Warn, "剪贴板被其他程序占用，复制失败，请重试。");
        }

        btnCopy.Click += (_, _) => CopyResult();
        btnClear.Click += (_, _) =>
        {
            inputBox.Clear();
            resultBox.Clear();
            feedback.Clear();
            inputBox.Focus();
        };

        // 预填示例，打开即可体验（对应原型默认载入样例）
        inputBox.Text = "{ \"name\":\"工具箱\", \"version\":1.0, \"tools\":[\"哈希\",\"JSON\"] }";

        // Dock 规则：Fill 先加，Top 后加
        root.Controls.Add(resultCard);
        root.Controls.Add(UiTheme.CardGap());
        root.Controls.Add(inputCard);
        root.Controls.Add(header);
        return root;
    }
}
