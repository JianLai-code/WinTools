using System.Drawing;
using System.Windows.Forms;
using WinTools.Core;
using WinTools.Core.Ui;

namespace WinTools.Tools.Text;

/// <summary>UUID 生成：v4 随机 UUID，可选格式、大小写与数量，一键复制全部。</summary>
[Tool(Order = 37)]
public sealed class UuidGeneratorTool : ITool
{
    public string Id => "uuid-generator";
    public string Name => "UUID 生成";
    public string Category => "文本工具";
    public Icon? Icon => null;

    public Control CreateUI()
    {
        // 行内标签/复选框与 32px 数字框、按钮高度不同，统一贴顶会让标签偏高。
        // 按各控件实测高度补半差，100% / 200% DPI 下都能真正垂直居中。
        static void CenterInRow(Control c, int rowHeight, int gap) =>
            c.Margin = new Padding(0, Math.Max(0, (rowHeight - c.Height) / 2), UiTheme.Sx(gap), 0);

        var root = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Bg };
        var header = new ToolHeader("UUID 生成",
            "生成密码学安全的随机 UUID（v4）；支持带连字符、无连字符、花括号格式与大写，可一次生成多个。");

        var countNumeric = new FluentNumeric
        {
            Minimum = 1, Maximum = UuidGenerator.MaxCount, Value = 10,
            Size = UiTheme.Sz(88, 32)
        };
        var uppercase = new FluentCheckBox { Text = "大写", AutoSize = true };

        var formatButtons = new[]
        {
            UiTheme.Button("带连字符", UiTheme.ButtonKind.Primary, 104),
            UiTheme.Button("无连字符", UiTheme.ButtonKind.Secondary, 104),
            UiTheme.Button("花括号", UiTheme.ButtonKind.Secondary, 90)
        };
        var btnGenerate = UiTheme.Button("生成", UiTheme.ButtonKind.Primary, 90);

        var countRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, WrapContents = false,
            Margin = Padding.Empty, Padding = UiTheme.Pad(0, 0, 0, 8)
        };
        countRow.Controls.Add(UiTheme.FieldLabel("生成数量"));
        countRow.Controls.Add(countNumeric);
        countRow.Controls.Add(uppercase);
        // 格式按钮紧跟在「大写」之后，与数量、大小写同排。
        foreach (var b in formatButtons) countRow.Controls.Add(b);

        var rowHeight = UiTheme.Sx(32);
        CenterInRow(countRow.Controls[0], rowHeight, 8);
        CenterInRow(countNumeric, rowHeight, 8);
        CenterInRow(uppercase, rowHeight, 10);
        for (var i = 0; i < formatButtons.Length; i++)
            CenterInRow(formatButtons[i], rowHeight, i == formatButtons.Length - 1 ? 0 : 8);

        var actionRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, Height = UiTheme.Sx(46), WrapContents = false,
            Margin = Padding.Empty, Padding = UiTheme.Pad(0, 6, 0, 0)
        };
        actionRow.Controls.Add(btnGenerate);

        // 少了一行格式按钮，卡片按“表头 40 + 内边距 28 + 参数行 40 + 操作行 46”收紧。
        var settingsCard = new CardPanel("生成参数") { Dock = DockStyle.Top, Height = UiTheme.Sx(168) };
        settingsCard.HeaderIcon = "key";
        settingsCard.Content.Controls.Add(actionRow);
        settingsCard.Content.Controls.Add(countRow);

        var format = UuidFormat.Dashed;
        void SetFormat(int index)
        {
            format = (UuidFormat)index;
            for (var i = 0; i < formatButtons.Length; i++)
                formatButtons[i].Variant = i == index
                    ? UiTheme.ButtonKind.Primary : UiTheme.ButtonKind.Secondary;
        }
        for (var i = 0; i < formatButtons.Length; i++)
        {
            var idx = i;
            formatButtons[i].Click += (_, _) => SetFormat(idx);
        }

        var resultBox = UiTheme.MultilineInput(readOnly: true);
        var feedback = new FeedbackBar { Dock = DockStyle.Bottom };
        var resultCard = new CardPanel("生成结果") { Dock = DockStyle.Fill, Margin = Padding.Empty };
        resultCard.HeaderIcon = "code";
        resultCard.AddAction("复制全部", (_, _) =>
        {
            if (resultBox.Text.Length == 0)
            {
                feedback.Show(FeedbackBar.Kind.Warn, "暂无可复制的内容，请先生成。");
                return;
            }
            feedback.Show(ClipboardUtil.TryCopy(resultBox.Text)
                ? FeedbackBar.Kind.Success : FeedbackBar.Kind.Warn, "已复制到剪贴板。");
        });
        resultCard.Content.Controls.Add(resultBox);
        resultCard.Content.Controls.Add(feedback);

        btnGenerate.Click += (_, _) =>
        {
            var values = UuidGenerator.Generate(countNumeric.Value, format, uppercase.Checked);
            resultBox.Text = string.Join(Environment.NewLine, values);
            feedback.Show(FeedbackBar.Kind.Success, $"已生成 {values.Length} 个 UUID。");
        };

        root.Controls.Add(resultCard);
        root.Controls.Add(UiTheme.CardGap());
        root.Controls.Add(settingsCard);
        root.Controls.Add(header);
        SetFormat(0);
        return root;
    }
}
