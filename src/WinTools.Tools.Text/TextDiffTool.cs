using System.Drawing;
using System.Windows.Forms;
using WinTools.Core;
using WinTools.Core.Ui;

namespace WinTools.Tools.Text;

/// <summary>文本对比工具：行级 LCS Diff，三种颜色高亮新增 / 删除 / 相同。实现 ITool。</summary>
[Tool(Order = 92)]
public sealed class TextDiffTool : ITool
{
    public string Id => "text-diff";
    public string Name => "文本对比";
    public string Category => "文本工具";
    public Icon? Icon => null;

    private static readonly Color AddedFore = Color.FromArgb(0x05, 0x96, 0x69);
    private static readonly Color RemovedFore = Color.FromArgb(0xDB, 0x27, 0x77);

    public Control CreateUI()
    {
        var root = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Bg };
        var header = new ToolHeader("文本对比", "逐行对比两段文本，绿色为新增、红色为删除，灰色为相同。");

        // 两栏输入（左右各占一半），用 TableLayoutPanel 保证等高
        var leftBox = UiTheme.MultilineInput();
        var rightBox = UiTheme.MultilineInput();
        var inputs = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2, RowCount = 1, Margin = Padding.Empty
        };
        inputs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        inputs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        var leftCard = new CardPanel("原文（旧）") { Dock = DockStyle.Fill, Margin = Padding.Empty };
        var rightCard = new CardPanel("新文（新）") { Dock = DockStyle.Fill, Margin = Padding.Empty };
        leftCard.Content.Controls.Add(leftBox);
        rightCard.Content.Controls.Add(rightBox);
        inputs.Controls.Add(leftCard, 0, 0);
        inputs.Controls.Add(rightCard, 1, 0);

        var btnCompare = UiTheme.Button("开始对比", UiTheme.ButtonKind.Primary, 120);
        var btnSwap = UiTheme.Button("交换两侧", UiTheme.ButtonKind.Secondary, 110);
        var btnClear = UiTheme.Button("清空", UiTheme.ButtonKind.Secondary, 80);
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, Height = UiTheme.Sx(48), WrapContents = false, Margin = Padding.Empty
        };
        bar.Padding = UiTheme.Pad(0, 8, 0, 0);
        bar.Controls.Add(btnCompare);
        bar.Controls.Add(btnSwap);
        bar.Controls.Add(btnClear);
        foreach (Control c in bar.Controls) c.Margin = UiTheme.Pad(0, 0, 8, 0);
        var inputCard = new CardPanel("输入") { Dock = DockStyle.Top, Height = UiTheme.Sx(250) };
        // 对齐 UrlCodecTool 的稳健模式：Fill 的输入区先加、Bottom 的按钮行后加，
        // 按钮行始终占据固定高度不会被输入框挤出可视区（之前两者都用固定高度会互相挤压）。
        inputCard.Content.Controls.Add(inputs);
        inputCard.Content.Controls.Add(bar);

        // 结果表格：三列（标记 / 旧行 / 新行 / 内容），内容列由 FluentListView 自动撑满
        var list = UiTheme.List();
        list.Dock = DockStyle.Fill;
        list.Columns.Add("标", UiTheme.Sx(44));
        list.Columns.Add("旧行", UiTheme.Sx(60));
        list.Columns.Add("新行", UiTheme.Sx(60));
        list.Columns.Add("内容", UiTheme.Sx(240));
        var feedback = new FeedbackBar { Dock = DockStyle.Bottom };
        string diffText = "";
        var resultCard = new CardPanel("差异") { Dock = DockStyle.Fill, Margin = Padding.Empty };
        resultCard.HeaderIcon = "diff";
        resultCard.AddAction("复制差异", "copy", (_, _) => Copy());
        resultCard.Content.Controls.Add(list);
        resultCard.Content.Controls.Add(feedback);

        void Compare()
        {
            var result = TextDiffer.Compare(leftBox.Text, rightBox.Text);
            if (result.ErrorMessage is not null)
            {
                feedback.Show(FeedbackBar.Kind.Error, result.ErrorMessage);
                return;
            }
            list.BeginUpdate();
            list.Items.Clear();
            var sb = new System.Text.StringBuilder();
            foreach (var line in result.Lines)
            {
                var mark = line.Kind switch
                {
                    DiffLineKind.Added => "+",
                    DiffLineKind.Removed => "-",
                    _ => " "
                };
                var item = new ListViewItem(mark);
                item.SubItems.Add(line.OldLine > 0 ? line.OldLine.ToString() : "");
                item.SubItems.Add(line.NewLine > 0 ? line.NewLine.ToString() : "");
                item.SubItems.Add(line.Text);
                if (line.Kind == DiffLineKind.Added) item.ForeColor = AddedFore;
                else if (line.Kind == DiffLineKind.Removed) item.ForeColor = RemovedFore;
                list.Items.Add(item);
                sb.Append(mark).Append(' ').Append(line.Text).Append('\n');
            }
            list.EndUpdate();
            diffText = sb.ToString();

            var note = result.Truncated ? $"（内容过长，仅对比前 {TextDiffer.MaxLines} 行）" : "";
            if (result.AddedCount == 0 && result.RemovedCount == 0)
                feedback.Show(FeedbackBar.Kind.Success, $"两段文本完全一致，共 {result.Lines.Count} 行。{note}");
            else
                feedback.Show(FeedbackBar.Kind.Info,
                    $"对比完成：新增 {result.AddedCount} 行，删除 {result.RemovedCount} 行，相同 {result.Lines.Count - result.AddedCount - result.RemovedCount} 行。{note}");
        }

        void Copy()
        {
            if (diffText.Length == 0)
            {
                feedback.Show(FeedbackBar.Kind.Warn, "请先对比，再复制差异结果。");
                return;
            }
            if (WinTools.Core.ClipboardUtil.TryCopy(diffText))
                feedback.Show(FeedbackBar.Kind.Success, "差异结果已复制到剪贴板。");
            else
                feedback.Show(FeedbackBar.Kind.Warn, "剪贴板被其他程序占用，复制失败，请重试。");
        }

        btnCompare.Click += (_, _) => Compare();
        btnSwap.Click += (_, _) =>
        {
            (leftBox.Text, rightBox.Text) = (rightBox.Text, leftBox.Text);
            feedback.Clear();
        };
        btnClear.Click += (_, _) =>
        {
            leftBox.Clear();
            rightBox.Clear();
            list.Items.Clear();
            diffText = "";
            feedback.Clear();
            leftBox.Focus();
        };

        root.Controls.Add(resultCard);
        root.Controls.Add(UiTheme.CardGap());
        root.Controls.Add(inputCard);
        root.Controls.Add(header);
        return root;
    }
}
