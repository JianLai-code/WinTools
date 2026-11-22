using System.Drawing;
using System.Windows.Forms;
using WinTools.Core;
using WinTools.Core.Ui;

namespace WinTools.Tools.Hello;

/// <summary>
/// 验证用空工具：不含任何业务逻辑，只用来证明"自动发现 + 零改宿主"。
/// 保留作为新增工具的模板——界面也示范统一的 ToolHeader + CardPanel 范式。
/// </summary>
[Tool(Order = 999)]
public sealed class HelloTool : ITool
{
    public string Id => "hello";
    public string Name => "示例工具";
    public string Category => "其他";
    public Icon? Icon => null;

    public Control CreateUI()
    {
        var root = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Bg };
        var header = new ToolHeader("示例工具", "这是一个验证插件发现机制的空工具，也可作为新增工具的模板。");

        var card = new CardPanel("自动发现验证") { Dock = DockStyle.Fill };
        var message = new Label
        {
            Text = "如果你看到这段话，说明工具已被自动发现。\n\n" +
                   "宿主（WinTools.Shell）代码一行未改，这个工具就出现在了左侧导航里。\n" +
                   "新增工具时，照着本项目的结构复制一份即可，样式会自动与全工具箱保持一致。",
            Dock = DockStyle.Fill,
            Font = UiTheme.Body,
            ForeColor = UiTheme.Text,
            TextAlign = ContentAlignment.TopLeft
        };
        card.Content.Controls.Add(message);

        root.Controls.Add(card);
        root.Controls.Add(header);
        return root;
    }
}
