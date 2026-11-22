using System.Drawing;
using System.Windows.Forms;
using WinTools.Core;
using WinTools.Core.Ui;

namespace WinTools.Tools.Text;

/// <summary>文本统计：字符 / 非空白 / 行 / 词 / 中文字符 / UTF-8 字节，输入变化即时更新。</summary>
[Tool(Order = 36)]
public sealed class TextStatsTool : ITool
{
    public string Id => "text-stats";
    public string Name => "文本统计";
    public string Category => "文本工具";
    public Icon? Icon => null;

    public Control CreateUI()
    {
        var root = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Bg };
        var header = new ToolHeader("文本统计",
            "粘贴文本即时查看字符数、行数、词数与 UTF-8 占用；中文按字计词，emoji 按一个字符计。");

        var inputBox = UiTheme.MultilineInput();
        var inputCard = new CardPanel("输入文本") { Dock = DockStyle.Top, Height = UiTheme.Sx(230) };
        inputCard.HeaderIcon = "text";
        inputCard.Content.Controls.Add(inputBox);

        TextStatsResult last = TextStats.Analyze(string.Empty);
        var list = UiTheme.List();
        list.Dock = DockStyle.Fill;
        list.Columns.Add("项目", UiTheme.Sx(210));
        list.Columns.Add("数值", UiTheme.Sx(150));

        var feedback = new FeedbackBar { Dock = DockStyle.Bottom };
        var resultCard = new CardPanel("统计结果") { Dock = DockStyle.Fill, Margin = Padding.Empty };
        resultCard.HeaderIcon = "list";
        resultCard.AddAction("复制统计", (_, _) =>
        {
            feedback.Show(ClipboardUtil.TryCopy(TextStats.Format(last))
                ? FeedbackBar.Kind.Success : FeedbackBar.Kind.Warn,
                "已复制统计结果。");
        });
        resultCard.Content.Controls.Add(list);
        resultCard.Content.Controls.Add(feedback);

        void Refresh()
        {
            last = TextStats.Analyze(inputBox.Text);
            list.Items.Clear();
            list.Items.Add(new ListViewItem(new[] { "字符数（含空白）", last.Characters.ToString() }));
            list.Items.Add(new ListViewItem(new[] { "非空白字符", last.NonWhitespace.ToString() }));
            list.Items.Add(new ListViewItem(new[] { "行数", last.Lines.ToString() }));
            list.Items.Add(new ListViewItem(new[] { "词数（中文按字计）", last.Words.ToString() }));
            list.Items.Add(new ListViewItem(new[] { "中日韩字符", last.ChineseChars.ToString() }));
            list.Items.Add(new ListViewItem(new[] { "UTF-8 字节", last.Utf8Bytes.ToString() }));
        }

        inputBox.TextChanged += (_, _) => Refresh();
        inputBox.Text = "WinTools 文本统计示例：你好，世界！";
        Refresh();

        root.Controls.Add(resultCard);
        root.Controls.Add(UiTheme.CardGap());
        root.Controls.Add(inputCard);
        root.Controls.Add(header);
        return root;
    }
}
