using System.Windows.Forms;
using WinTools.Core.Ui;
using WinTools.Shell;
using Xunit;

namespace WinTools.DiscoveryTests;

/// <summary>
/// 集成测试：验证"工具自动发现"这一架构核心（阶段七集成测试第 1 项）。
/// 测试输出目录里有 WinTools.Tools.Hello.dll，发现器应能找到它。
/// 与 DpiScalingTests 同 Collection 串行：UiTheme.S 是静态全局，不能并行切换。
/// </summary>
[Collection("ui")]
public sealed class ToolDiscoveryTests
{
    [Fact]
    public void DiscoverFromDirectory_FindsHelloTool()
    {
        var dir = AppContext.BaseDirectory;
        var tools = ToolDiscovery.DiscoverFromDirectory(dir);

        Assert.Contains(tools, t => t.Id == "hello" && t.Name == "示例工具");
    }

    [Fact]
    public void DiscoverFromDirectory_FindsJsonFormatterTool()
    {
        // 迭代 1 新增的 JSON 格式化工具应能被自动发现，宿主代码零改动。
        var dir = AppContext.BaseDirectory;
        var tools = ToolDiscovery.DiscoverFromDirectory(dir);

        Assert.Contains(tools, t => t.Id == "json-formatter" && t.Name == "JSON 格式化");
    }

    [Fact]
    public void DiscoverFromDirectory_FindsBase64AndPasswordTools()
    {
        // 迭代 2 新增的两个文本工具应能被自动发现，宿主代码零改动。
        var dir = AppContext.BaseDirectory;
        var tools = ToolDiscovery.DiscoverFromDirectory(dir);

        Assert.Contains(tools, t => t.Id == "base64-codec" && t.Name == "Base64 编解码");
        Assert.Contains(tools, t => t.Id == "password-generator" && t.Name == "随机密码生成");
    }

    [Fact]
    public void DiscoverFromDirectory_FindsHashTool()
    {
        // 迭代 3 新增的哈希工具（首个文件类工具）应能被自动发现，宿主代码零改动。
        var tools = ToolDiscovery.DiscoverFromDirectory(AppContext.BaseDirectory);

        Assert.Contains(tools, t => t.Id == "hash-checker" && t.Name == "哈希校验" && t.Category == "文件工具");
    }

    [Fact]
    public void DiscoverFromDirectory_FindsSystemTools()
    {
        // 迭代 4 新增的两个系统工具应能被自动发现，宿主代码零改动。
        var tools = ToolDiscovery.DiscoverFromDirectory(AppContext.BaseDirectory);

        Assert.Contains(tools, t => t.Id == "system-info" && t.Name == "系统信息总览" && t.Category == "系统工具");
        Assert.Contains(tools, t => t.Id == "port-query" && t.Name == "端口占用查询" && t.Category == "系统工具");
        Assert.Contains(tools, t => t.Id == "network-info" && t.Name == "网络信息查询" && t.Category == "系统工具");
    }

    [Fact]
    public void DiscoverFromDirectory_FindsRenameTool()
    {
        // 迭代 5 新增的批量重命名工具（唯一写操作工具）应能被自动发现，宿主代码零改动。
        var tools = ToolDiscovery.DiscoverFromDirectory(AppContext.BaseDirectory);

        Assert.Contains(tools, t => t.Id == "batch-rename" && t.Name == "批量重命名" && t.Category == "文件工具");
    }

    [Fact]
    public void DiscoverFromDirectory_FindsNewUtilities()
    {
        // 后续新增的三个低成本工具也应被自动发现，宿主代码零改动。
        var tools = ToolDiscovery.DiscoverFromDirectory(AppContext.BaseDirectory);

        Assert.Contains(tools, t => t.Id == "url-codec" && t.Name == "URL 编解码" && t.Category == "文本工具");
        Assert.Contains(tools, t => t.Id == "timestamp-converter" && t.Name == "时间戳转换" && t.Category == "文本工具");
        Assert.Contains(tools, t => t.Id == "environment-variables" && t.Name == "环境变量查看" && t.Category == "系统工具");
    }

    [Fact]
    public void DiscoverFromDirectory_FindsNewBatchTools()
    {
        // 二维码 / 文本对比 / 文件编码转换应能被自动发现，宿主代码零改动。
        var tools = ToolDiscovery.DiscoverFromDirectory(AppContext.BaseDirectory);

        Assert.Contains(tools, t => t.Id == "qr-code-generator" && t.Name == "二维码生成");
        Assert.Contains(tools, t => t.Id == "text-diff" && t.Name == "文本对比");
        Assert.Contains(tools, t => t.Id == "file-encoding-converter" && t.Name == "文件编码转换");
    }

    [Fact]
    public void NewBatchTools_CreateUI_DoesNotThrow()
    {
        var tools = ToolDiscovery.DiscoverFromDirectory(AppContext.BaseDirectory);
        foreach (var id in new[] { "qr-code-generator", "text-diff", "file-encoding-converter" })
        {
            var tool = tools.FirstOrDefault(t => t.Id == id);
            Assert.NotNull(tool);
            Assert.NotNull(tool!.CreateUI());
        }
    }

    [Fact]
    public void DiscoverFromDirectory_FindsMediaAndDiskTools()
    {
        // 图片转换 / 批量压缩 / 文件夹大小分析应能被自动发现，宿主代码零改动。
        var tools = ToolDiscovery.DiscoverFromDirectory(AppContext.BaseDirectory);

        Assert.Contains(tools, t => t.Id == "image-format-converter" && t.Name == "图片格式转换");
        Assert.Contains(tools, t => t.Id == "image-compress" && t.Name == "图片批量压缩");
        Assert.Contains(tools, t => t.Id == "folder-size-analyzer" && t.Name == "文件夹大小分析");
    }

    [Fact]
    public void MediaAndDiskTools_CreateUI_DoesNotThrow()
    {
        var tools = ToolDiscovery.DiscoverFromDirectory(AppContext.BaseDirectory);
        foreach (var id in new[] { "image-format-converter", "image-compress", "folder-size-analyzer" })
        {
            var tool = tools.FirstOrDefault(t => t.Id == id);
            Assert.NotNull(tool);
            Assert.NotNull(tool!.CreateUI());
        }
    }

    [Fact]
    public void DiscoverFromDirectory_EmptyDirectory_ReturnsEmpty()
    {
        var emptyDir = Path.Combine(Path.GetTempPath(), $"wintools-empty-{Guid.NewGuid():N}");
        Directory.CreateDirectory(emptyDir);
        try
        {
            var tools = ToolDiscovery.DiscoverFromDirectory(emptyDir);
            Assert.Empty(tools);
        }
        finally
        {
            Directory.Delete(emptyDir);
        }
    }

    [Fact]
    public void DiscoverFromDirectory_FoundToolCanCreateUI()
    {
        var tools = ToolDiscovery.DiscoverFromDirectory(AppContext.BaseDirectory);
        var hello = tools.FirstOrDefault(t => t.Id == "hello");

        Assert.NotNull(hello);
        var ui = hello!.CreateUI();
        Assert.NotNull(ui);
    }

    [Fact]
    public void NewUtilities_CreateUI_DoesNotThrow()
    {
        var tools = ToolDiscovery.DiscoverFromDirectory(AppContext.BaseDirectory);
        foreach (var id in new[] { "url-codec", "timestamp-converter", "environment-variables" })
        {
            var tool = tools.FirstOrDefault(t => t.Id == id);
            Assert.NotNull(tool);
            Assert.NotNull(tool!.CreateUI());
        }
    }

    [Fact]
    public void EnvironmentVariables_CreateUI_ListIsDockedAndPopulated()
    {
        // 回归：列表曾漏设 Dock，数据已读到 51 项但界面区域空白。
        var tools = ToolDiscovery.DiscoverFromDirectory(AppContext.BaseDirectory);
        var tool = tools.First(t => t.Id == "environment-variables");
        using var ui = tool.CreateUI();

        var list = Descendants<ListView>(ui).Single();
        Assert.Equal(DockStyle.Fill, list.Dock);
        Assert.True(list.Items.Count > 0);
    }

    [Fact]
    public void TextDiff_CreateUI_ButtonsDockedBottomAndInputsFill()
    {
        // 回归：输入区与按钮行曾用两个固定高度，按钮行被挤出可视区，用户看不到「开始对比」。
        var tools = ToolDiscovery.DiscoverFromDirectory(AppContext.BaseDirectory);
        var tool = tools.First(t => t.Id == "text-diff");
        using var ui = tool.CreateUI();

        var buttonBar = Descendants<FlowLayoutPanel>(ui)
            .Single(p => p.Controls.Cast<Control>().Any(c => c.Text == "开始对比"));
        Assert.Equal(DockStyle.Bottom, buttonBar.Dock);

        var inputs = Descendants<TableLayoutPanel>(ui).Single();
        Assert.Equal(DockStyle.Fill, inputs.Dock);
    }

    [Fact]
    public void FileEncoding_CreateUI_SourceFieldsAllDocked()
    {
        // 回归：路径框与提示标签曾用绝对定位 (0,0)，与顶部停靠的按钮行重叠。
        var tools = ToolDiscovery.DiscoverFromDirectory(AppContext.BaseDirectory);
        var tool = tools.First(t => t.Id == "file-encoding-converter");
        using var ui = tool.CreateUI();

        var pathBox = Descendants<FluentInput>(ui).Single();
        Assert.Equal(DockStyle.Top, pathBox.Dock);

        var selectRow = Descendants<FlowLayoutPanel>(ui)
            .First(p => p.Controls.Cast<Control>().Any(c => c.Text == "选择文件"));
        Assert.Equal(DockStyle.Top, selectRow.Dock);

        // "尚未选择文件"提示应在选择文件按钮右侧（同一行容器内），不再单独占一行。
        var hint = Descendants<Label>(ui).Single(l => l.Text == "尚未选择文件");
        Assert.Same(selectRow, hint.Parent);
        var selectBtn = selectRow.Controls.Cast<Control>().First(c => c.Text == "选择文件");
        Assert.True(selectRow.Controls.GetChildIndex(hint) > selectRow.Controls.GetChildIndex(selectBtn),
            "提示标签应排在按钮之后（按钮右侧）。");
    }

    [Fact]
    public void FileEncoding_CreateUI_EncodingTableHasPercentRowAndConvertCard()
    {
        // 回归：编码选择表格曾缺 RowStyle，行高塌成 AutoSize，卡片被压扁后第二行编码按钮被裁掉；
        // 编码按钮宽度曾按字符数估宽，全角括号文案"UTF-8（无 BOM）"尾部被裁；
        // "转换并另存为…"应放回标题为"转换"的卡片里，卡片高度紧凑不留大面积空白。
        var tools = ToolDiscovery.DiscoverFromDirectory(AppContext.BaseDirectory);
        var tool = tools.First(t => t.Id == "file-encoding-converter");
        using var ui = tool.CreateUI();

        var encTable = Descendants<TableLayoutPanel>(ui).Single();
        Assert.Contains(encTable.RowStyles.Cast<RowStyle>(), s => s.SizeType == SizeType.Percent);

        var convertBtn = Descendants<Button>(ui).Single(b => b.Text == "转换并另存为…");
        // 层级：CardPanel → Content → convertRow → 按钮。
        var convertCard = Assert.IsType<CardPanel>(convertBtn.Parent!.Parent!.Parent);
        Assert.Equal("转换", convertCard.Title);
        Assert.Equal(DockStyle.Top, convertCard.Dock);
        Assert.True(convertCard.Height <= UiTheme.Sx(110), "转换卡片应保持紧凑高度，不要出现大片空白。");

        // 编码分段按钮宽度须容纳最长文案"UTF-16 LE（带 BOM）"。
        var widest = Descendants<Button>(ui)
            .Where(b => b.Text.StartsWith("UTF-16", StringComparison.Ordinal))
            .Select(b => b.Width)
            .Min();
        Assert.True(widest >= TextRenderer.MeasureText("UTF-16 LE（带 BOM）", UiTheme.Body).Width,
            "编码按钮宽度不足，文案会被裁掉尾部。");
    }

    [Fact]
    public void Base64_CreateUI_ButtonBarFlushWithCardBottom()
    {
        // 回归：内容区默认 14px 下内边距叠在按钮条下面，按钮看上去偏上、不居中。
        // 底部内边距应清零，让按钮条贴卡片底边，按钮在条内 5/5 居中。
        var tools = ToolDiscovery.DiscoverFromDirectory(AppContext.BaseDirectory);
        var tool = tools.First(t => t.Id == "base64-codec");
        using var ui = tool.CreateUI();

        var buttonBar = Descendants<FlowLayoutPanel>(ui)
            .Single(p => p.Controls.Cast<Control>().Any(c => c.Text == "转换"));
        Assert.Equal(DockStyle.Bottom, buttonBar.Dock);
        Assert.Equal(0, buttonBar.Parent!.Padding.Bottom);
    }

    [Fact]
    public void FolderSize_CreateUI_PathCardTallEnoughForAllRows()
    {
        // 回归：卡片高度 185 装不下"选择行 + 说明 + 路径框 + 进度条 + 操作行"，
        // Fill 的路径框被压扁且与按钮互相叠压。高度必须 >= 内容实算 214。
        var tools = ToolDiscovery.DiscoverFromDirectory(AppContext.BaseDirectory);
        var tool = tools.First(t => t.Id == "folder-size-analyzer");
        using var ui = tool.CreateUI();

        var pathCard = Descendants<CardPanel>(ui).Single(c => c.Title == "目标文件夹");
        Assert.True(pathCard.Height >= UiTheme.Sx(214), "目标文件夹卡片高度不足，路径框与按钮会互相叠压。");

        var scanBtn = Descendants<Button>(ui).Single(b => b.Text == "开始分析");
        var pathInput = Descendants<FluentInput>(ui).Single();
        Assert.False(pathInput.Bounds.IntersectsWith(scanBtn.Bounds), "路径输入框不得与开始分析按钮重叠。");
    }

    [Fact]
    public void PortQuery_CreateUI_NoRedundantFormatHint()
    {
        // 回归：输入格式说明整行文字冗长重复，已移除；占位符与"全部"按钮足以说明用法。
        var tools = ToolDiscovery.DiscoverFromDirectory(AppContext.BaseDirectory);
        var tool = tools.First(t => t.Id == "port-query");
        using var ui = tool.CreateUI();

        Assert.DoesNotContain(Descendants<Label>(ui),
            l => l.Text.Contains("可混合输入多个端口与区间", StringComparison.Ordinal));

        var inputCard = Descendants<CardPanel>(ui).Single(c => c.Title == "输入端口 / 范围");
        var inputBar = Descendants<FlowLayoutPanel>(ui)
            .First(p => p.Controls.Cast<Control>().Any(c => c.Text == "查询"));
        Assert.False(inputBar.Bottom > inputCard.Content.Bounds.Bottom, "输入条不得被卡片底边裁切。");
    }

    [Fact]
    public void Timestamp_CreateUI_HintRowSitsAboveButtonBar()
    {
        // 回归：输入卡 228 高多出约 24px 空档，说明文字与按钮行之间隔着一条大缝。
        // 收紧后说明行应紧贴按钮行上方（间距 ≤ 8）。
        var tools = ToolDiscovery.DiscoverFromDirectory(AppContext.BaseDirectory);
        var tool = tools.First(t => t.Id == "timestamp-converter");
        using var ui = tool.CreateUI();

        var hint = Descendants<Label>(ui)
            .Single(l => l.Text.Contains("按本机时区解释", StringComparison.Ordinal));
        Assert.NotNull(hint);
        // 回归的直接根因是卡片高度 228 多出约 24px 空档；高度按内容实算应为 206，
        // 用高度上限锁定（坐标跨父容器无法直接比较，离屏控件也未经历真实布局）。
        var inputCard = Descendants<CardPanel>(ui).Single(c => c.Title == "输入");
        Assert.True(inputCard.Height <= UiTheme.Sx(212),
            $"输入卡片过高（{inputCard.Height}px），说明文字与按钮行之间会出现大缝。");
    }

    [Fact]
    public void ImageCompress_CreateUI_HintRowIsContainerNotAutoSizeLabel()
    {
        // 回归：提示标签曾直接 Dock=Fill + AutoSize，卡片行数挤占 Fill 区塌陷后，
        // 标签按自身首选高度向下溢出，压在"开始压缩"主按钮上（截图里文字与按钮混叠）。
        // 修复后：提示行 Dock=Bottom 贴在操作行正上方，中间由 Fill 弹性空档吸收
        // 不同 DPI 下的行高偏差——提示永远不可能与按钮重叠，按钮也永远不会被挤出卡片。
        var tools = ToolDiscovery.DiscoverFromDirectory(AppContext.BaseDirectory);
        var tool = tools.First(t => t.Id == "image-compress");
        using var ui = tool.CreateUI();

        var hint = Descendants<Label>(ui).Single(l => l.Text == "尚未选择图片");
        var hintRow = Assert.IsType<FlowLayoutPanel>(hint.Parent);
        Assert.Equal(DockStyle.Bottom, hintRow.Dock);

        var btnBar = Descendants<FlowLayoutPanel>(ui)
            .Single(p => p.Controls.Cast<Control>().Any(c => c.Text == "开始压缩"));
        Assert.Equal(DockStyle.Bottom, btnBar.Dock);
        // 提示行与按钮行同属一个卡片内容区（提示行紧邻按钮行上方，同为 Bottom 停靠）。
        Assert.Same(btnBar.Parent, hintRow.Parent);

        var card = (CardPanel)btnBar.Parent!.Parent;
        // 实算需 表头40 + 内边距28 + 选择行38 + 质量行40 + 提示行24 + 操作行46 ≈ 216。
        Assert.True(card.Height >= UiTheme.Sx(216),
            $"图片列表卡片高度不足（{card.Height}px），行会被挤压。");
    }

    [Fact]
    public void Hash_CreateUI_ButtonBarEquidistantFromEdges()
    {
        // 回归：按钮行距拖放区 5px、距卡片底 19px（5+内容边距14），明显失衡。
        // 修复后：条高 46 = 上边距 14 + 按钮 32 + 下余 0，上下均为 14px。
        var tools = ToolDiscovery.DiscoverFromDirectory(AppContext.BaseDirectory);
        var tool = tools.First(t => t.Id == "hash-checker");
        using var ui = tool.CreateUI();

        var bar = Descendants<FlowLayoutPanel>(ui)
            .Single(p => p.Controls.Cast<Control>().Any(c => c.Text == "选择文件"));
        Assert.Equal(DockStyle.Bottom, bar.Dock);
        Assert.Equal(UiTheme.Sx(14), bar.Padding.Top);
        // 条高 46：上边距 14 + 按钮 32 后恰好用尽，底侧只剩卡片内容边距 14。
        Assert.Equal(UiTheme.Sx(46), bar.Height);
    }

    [Fact]
    public void Rename_CreateUI_RuleGridIsFixedHeightTopDock()
    {
        // 回归：规则网格曾 Dock=Fill，富余高度全部堆进网格底部，第三行与
        // "占位符"提示之间的空档随卡片高度变化。修复后网格固定 132（3×44）贴顶。
        var tools = ToolDiscovery.DiscoverFromDirectory(AppContext.BaseDirectory);
        var tool = tools.First(t => t.Id == "batch-rename");
        using var ui = tool.CreateUI();

        var grid = Descendants<TableLayoutPanel>(ui).Single();
        Assert.Equal(DockStyle.Top, grid.Dock);
        Assert.Equal(UiTheme.Sx(132), grid.Height);
        Assert.All(grid.RowStyles.Cast<RowStyle>(), s => Assert.Equal(SizeType.Absolute, s.SizeType));
    }

    [Fact]
    public void FolderSize_CreateUI_PathInputIsStandardHeightTopDock()
    {
        // 回归：路径输入框曾 Dock=Fill 被竖向拉成 ~44px 高的巨框，与其他工具的
        // 标准输入框（32px）不一致。修复后 Dock=Top 固定高度。
        var tools = ToolDiscovery.DiscoverFromDirectory(AppContext.BaseDirectory);
        var tool = tools.First(t => t.Id == "folder-size-analyzer");
        using var ui = tool.CreateUI();

        var pathInput = Descendants<FluentInput>(ui).Single();
        Assert.Equal(DockStyle.Top, pathInput.Dock);
        Assert.Equal(UiTheme.Sx(32), pathInput.Height);
    }

    [Fact]
    public void ImageCompress_CreateUI_QualityLabelsVerticallyCentered()
    {
        // 回归：压缩质量行标签写死 Margin.Top=7，在不同字体度量下偏离数字框中心。
        // 修复后按各控件实测高度计算上边距，标签与 32px 数字框几何居中。
        var tools = ToolDiscovery.DiscoverFromDirectory(AppContext.BaseDirectory);
        var tool = tools.First(t => t.Id == "image-compress");
        using var ui = tool.CreateUI();

        var numeric = Descendants<FluentNumeric>(ui).Single();
        var label = Descendants<Label>(ui).Single(l => l.Text == "压缩质量");
        Assert.Equal(UiTheme.Sx(32), numeric.Height);
        Assert.Equal((UiTheme.Sx(32) - label.Height) / 2, label.Margin.Top);
    }

    [Fact]
    public void ImageConvert_CreateUI_InfoHintSitsBesideSelectButton()
    {
        // 回归：格式支持说明曾单独占一行，且选择按钮与上方路径输入框贴得太近。
        // 说明移到按钮右侧同行显示，按钮行上内边距 ≥ 8 与输入框拉开距离。
        var tools = ToolDiscovery.DiscoverFromDirectory(AppContext.BaseDirectory);
        var tool = tools.First(t => t.Id == "image-format-converter");
        using var ui = tool.CreateUI();

        var hint = Descendants<Label>(ui)
            .Single(l => l.Text.Contains("不支持 WebP", StringComparison.Ordinal));
        var selectRow = Assert.IsType<FlowLayoutPanel>(hint.Parent);
        var btn = selectRow.Controls.Cast<Control>().Single(c => c.Text == "选择图片");
        // 提示在按钮右侧（FlowLayoutPanel 中排布序即 z 序，index 小者靠左）。
        Assert.True(selectRow.Controls.GetChildIndex(hint) > selectRow.Controls.GetChildIndex(btn));
        Assert.True(selectRow.Padding.Top >= UiTheme.Sx(8),
            $"选择图片按钮与输入框间距不足：{selectRow.Padding.Top}px。");
    }

    [Fact]
    public void ImageConvert_QualityNumericDisabled_KeepsFieldBgVisible()
    {
        // 回归：禁用态曾把底色清成 Surface，与卡片背景同色，数字框整体"隐形"，
        // 只剩悬浮的数字，看起来像被裁掉的黑块。禁用感应由 Muted 前景表达，框仍可见。
        var tools = ToolDiscovery.DiscoverFromDirectory(AppContext.BaseDirectory);
        var tool = tools.First(t => t.Id == "image-format-converter");
        using var ui = tool.CreateUI();

        var numeric = Descendants<FluentNumeric>(ui).Single();
        // 默认选中 PNG，质量框应为禁用态，但底色保持 FieldBg（与卡片区分、框可见）。
        Assert.False(numeric.Enabled);
        Assert.Equal(UiTheme.FieldBg, numeric.BackColor);
    }

    private static IEnumerable<T> Descendants<T>(Control parent) where T : Control
    {
        foreach (Control child in parent.Controls)
        {
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
}
