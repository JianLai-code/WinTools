using System.Drawing;
using System.Windows.Forms;
using WinTools.Core;
using WinTools.Core.Ui;

namespace WinTools.Tools.Text;

/// <summary>
/// 随机密码生成工具：实现 ITool，由 Shell 反射发现。
/// 参数 → 生成 → 结果 → 强度反馈。
/// </summary>
[Tool(Order = 30)]
public sealed class PasswordTool : ITool
{
    public string Id => "password-generator";
    public string Name => "随机密码生成";
    public string Category => "文本工具";
    public Icon? Icon => null;

    public Control CreateUI()
    {
        // 行内居中：用控件实测高度补上半差，而不是写死 Padding 上边距——
        // 写死的偏移只在 100% 下对得上，200% 时文字会浮到输入框上沿之外。
        static void CenterInRow(Control c, int rowHeight, int gap) =>
            c.Margin = new Padding(0, Math.Max(0, (rowHeight - c.Height) / 2), UiTheme.Sx(gap), 0);

        var root = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Bg };
        var header = new ToolHeader("随机密码生成", "生成强密码，可定制长度与字符集，一键复制。使用密码学安全随机数。");

        // ---- 参数区 ----
        var numLength = new FluentNumeric
        {
            Minimum = PasswordOptions.MinLength,
            Maximum = PasswordOptions.MaxLength,
            Value = PasswordOptions.DefaultLength,
            Width = UiTheme.Sx(90)
        };
        var lblLength = new Label { Text = "密码长度", AutoSize = true, Font = UiTheme.Bold, ForeColor = UiTheme.Text };
        var lengthRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, Height = UiTheme.Sx(40), WrapContents = false,
            Margin = UiTheme.Pad(0, 0, 0, 4)
        };
        CenterInRow(lblLength, lengthRow.Height, 12);
        CenterInRow(numLength, lengthRow.Height, 0);
        lengthRow.Controls.Add(lblLength);
        lengthRow.Controls.Add(numLength);

        var chkUpper = new FluentCheckBox { Text = "大写 A-Z", Checked = true };
        var chkLower = new FluentCheckBox { Text = "小写 a-z", Checked = true };
        var chkDigit = new FluentCheckBox { Text = "数字 0-9", Checked = true };
        var chkSymbol = new FluentCheckBox { Text = "符号 !@#…", Checked = true };
        var chkNoAmbiguous = new FluentCheckBox { Text = "排除易混淆字符（O/0/I/l/1）", Checked = true };

        var setRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, Height = UiTheme.Sx(38), AutoSize = false, WrapContents = false,
            Margin = Padding.Empty
        };
        // 四个字符集复选框一排：间隙统一缩放，行内按实测高度居中
        setRow.Padding = new Padding(0, Math.Max(0, (UiTheme.Sx(38) - chkUpper.Height) / 2), 0, 0);
        foreach (var chk in new Control[] { chkUpper, chkLower, chkDigit, chkSymbol })
        {
            chk.Margin = UiTheme.Pad(0, 0, 16, 0);
            setRow.Controls.Add(chk);
        }

        // 排除项文字长，单独一行；不再用绝对 Location 压第二排，避免行高随字体变化时叠字。
        var setRow2 = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, Height = UiTheme.Sx(34), AutoSize = false, WrapContents = false,
            Margin = Padding.Empty
        };
        setRow2.Padding = new Padding(0, Math.Max(0, (UiTheme.Sx(34) - chkNoAmbiguous.Height) / 2), 0, 0);
        chkNoAmbiguous.Margin = Padding.Empty;
        setRow2.Controls.Add(chkNoAmbiguous);

        var setsPanel = new Panel { Dock = DockStyle.Top, Height = UiTheme.Sx(72), Padding = new Padding(0) };
        // Panel 内两行停靠：后加的排上面 → 字符集 / 排除项
        setsPanel.Controls.Add(setRow2);
        setsPanel.Controls.Add(setRow);

        var btnGenerate = UiTheme.Button("生成密码");
        var btnCopy = UiTheme.Button("复制密码", UiTheme.ButtonKind.Secondary);
        // 操作条贴卡片底，和其余工具页一致：留在参数下方就不会有一截说不清的空白
        var buttonBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, Height = UiTheme.Sx(42), WrapContents = false, Margin = Padding.Empty
        };
        buttonBar.Controls.Add(btnGenerate);
        buttonBar.Controls.Add(btnCopy);
        // 间隙与上下留白统一按设计像素缩放，按钮才不会贴着条顶挤在一起
        buttonBar.Padding = UiTheme.Pad(0, (42 - 32) / 2, 0, 0);
        foreach (Control c in buttonBar.Controls) c.Margin = UiTheme.Pad(0, 0, 8, 0);

        var paramCard = new CardPanel("生成参数") { Dock = DockStyle.Top, Height = UiTheme.Sx(250) };
        // Dock.Top 后加的排上面：按期望视觉顺序的【反序】添加 → 长度 / 字符 / 按钮
        paramCard.Content.Controls.Add(buttonBar);
        paramCard.Content.Controls.Add(setsPanel);
        paramCard.Content.Controls.Add(lengthRow);

        // ---- 结果区 ----
        var passwordBox = new FluentTextBox
        {
            Multiline = true,
            ReadOnly = true,
            Dock = DockStyle.Fill,
            Font = UiTheme.MonoBig,
            ForegroundOverride = UiTheme.Primary,
            WordWrap = true,
            TextAlign = HorizontalAlignment.Center
        };
        var feedback = new FeedbackBar { Dock = DockStyle.Bottom };
        var resultCard = new CardPanel("生成的密码") { Dock = DockStyle.Fill, Margin = new Padding(0) };
        resultCard.AddAction("复制", (_, _) => Copy());
        resultCard.Content.Controls.Add(passwordBox);
        resultCard.Content.Controls.Add(feedback);

        void Copy()
        {
            if (string.IsNullOrEmpty(passwordBox.Text))
            {
                feedback.Show(FeedbackBar.Kind.Warn, "暂无可复制的密码，请先生成。");
                return;
            }
            if (ClipboardUtil.TryCopy(passwordBox.Text))
                feedback.Show(FeedbackBar.Kind.Success, "已复制到剪贴板。");
            else
                feedback.Show(FeedbackBar.Kind.Warn, "剪贴板被其他程序占用，复制失败，请重试。");
        }

        btnGenerate.Click += (_, _) =>
        {
            var options = new PasswordOptions(
                (int)numLength.Value, chkUpper.Checked, chkLower.Checked,
                chkDigit.Checked, chkSymbol.Checked, chkNoAmbiguous.Checked);
            var result = PasswordGenerator.Generate(options);
            if (result.Success)
            {
                passwordBox.Text = result.Password;
                var bits = PasswordGenerator.EstimateEntropyBits(options);
                feedback.Show(FeedbackBar.Kind.Success,
                    $"强度：{PasswordGenerator.StrengthLabel(bits)}（约 {bits:0} bit）· 实际长度 {result.Password.Length}");
            }
            else
            {
                passwordBox.Clear();
                feedback.Show(FeedbackBar.Kind.Error, $"{result.ErrorMessage}。建议：至少勾选一种字符类型。");
            }
        };

        btnCopy.Click += (_, _) => Copy();

        root.Controls.Add(resultCard);
        root.Controls.Add(UiTheme.CardGap());
        root.Controls.Add(paramCard);
        root.Controls.Add(header);
        return root;
    }
}
