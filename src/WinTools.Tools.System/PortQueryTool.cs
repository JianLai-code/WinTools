using System.Drawing;
using System.Windows.Forms;
using WinTools.Core;
using WinTools.Core.Ui;

namespace WinTools.Tools.SystemTools;

/// <summary>
/// 端口占用查询工具：支持单端口、逗号列表（80,443）、区间（1-20）与留空全量（1-65535）。
/// 界面按阶段四/原型重做：工具头 + 输入卡片 + 大号结果卡片 + 语义反馈条。
/// </summary>
[Tool(Order = 60)]
public sealed class PortQueryTool : ITool
{
    public string Id => "port-query";
    public string Name => "端口占用查询";
    public string Category => "系统工具";
    public Icon? Icon => null;

    public Control CreateUI()
    {
        // 一排控件按"实测高度"补上半差居中：单行输入框的实际高度由字体决定（见 UiTheme.Input 的说明），
        // 写死偏移只在 100% 下对得上，200% 时标签会浮到框子上沿之外。
        static void CenterInRow(Control c, int rowHeight, int gap) =>
            c.Margin = new Padding(0, Math.Max(0, (rowHeight - c.Height) / 2), UiTheme.Sx(gap), 0);

        var root = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Bg };
        var header = new ToolHeader("端口占用查询",
            "支持 80,443、1-20 批量查询；留空则扫描 1-65535 全部端口。");

        // ---- 输入区 ----
        // 一排里的按钮统一 34 设计像素高，标签与输入框按实测高度同一条中线对齐
        var portBox = UiTheme.Input("例如 80,443 或 1-20；留空查全部", 180);
        var btnQuery = UiTheme.Button("查询", UiTheme.ButtonKind.Primary, 88, 34);
        var lblPort = new Label
        {
            Text = "端口 / 范围（1-65535）", AutoSize = true,
            Font = UiTheme.Bold, ForeColor = UiTheme.Text
        };
        var inputBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, Height = UiTheme.Sx(42), WrapContents = false, Margin = Padding.Empty
        };
        inputBar.Controls.Add(lblPort);
        inputBar.Controls.Add(portBox);
        inputBar.Controls.Add(btnQuery);
        foreach (var p in new[] { 80, 443, 8080 })
        {
            var quick = UiTheme.Button(p.ToString(), UiTheme.ButtonKind.Secondary, 56, 34);
            quick.Click += (_, _) => portBox.Text = p.ToString();
            inputBar.Controls.Add(quick);
        }
        // 留空即全量扫描，给一个显式入口省去用户猜测"不输入会怎样"。
        // 事件在 DoQueryAsync 声明之后挂，避免捕获尚未赋值的局部变量。
        var btnAll = UiTheme.Button("全部", UiTheme.ButtonKind.Secondary, 56, 34);
        inputBar.Controls.Add(btnAll);
        // 条内每件控件各自居中：间隙 8 设计像素，标签与框之间留 10
        foreach (Control c in inputBar.Controls)
            CenterInRow(c, inputBar.Height, ReferenceEquals(c, lblPort) ? 10 : 8);

        // 卡片高度按内容实算：表头 40 + 描边 2 + 内容上下内边距 28 + 输入条 42 = 112。
        // 冗长的输入格式说明文字已移除——占位符与"全部"按钮足以说明用法。
        var inputCard = new CardPanel("输入端口 / 范围") { Dock = DockStyle.Top, Height = UiTheme.Sx(116) };
        inputCard.Content.Controls.Add(inputBar);

        // ---- 结果区（大号摘要 + 可滚动明细）----
        var resultLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = UiTheme.Sx(44),
            Font = UiTheme.FontAt(14F, bold: true),
            ForeColor = UiTheme.Muted,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Text = "输入端口或范围后点击查询"
        };
        var resultBox = UiTheme.MultilineInput(readOnly: true);
        resultBox.Text =
            "支持批量输入：80,443 / 1-20 / 80,443,8000-8010；留空查询全部 1-65535 端口。" + Environment.NewLine +
            "占用端口会输出协议、监听地址、连接状态、PID、进程全量信息、占用时长与风险标记；空闲端口仅在数量较少时逐项列出。";
        var feedback = new FeedbackBar { Dock = DockStyle.Bottom };
        var resultCard = new CardPanel("查询结果") { Dock = DockStyle.Fill, Margin = new Padding(0) };
        resultCard.AddAction("复制", (_, _) => CopyResult());
        // 内边距沿用 CardPanel 默认值，与其余工具页的卡片同一条左边界
        resultCard.Content.Controls.Add(resultBox);
        resultCard.Content.Controls.Add(resultLabel);
        resultCard.Content.Controls.Add(feedback);

        void CopyResult()
        {
            if (string.IsNullOrEmpty(resultBox.Text))
            {
                feedback.Show(FeedbackBar.Kind.Warn, "暂无可复制的内容，请先查询。");
                return;
            }
            if (ClipboardUtil.TryCopy(resultBox.Text))
                feedback.Show(FeedbackBar.Kind.Success, "已复制到剪贴板。");
            else
                feedback.Show(FeedbackBar.Kind.Warn, "剪贴板被其他程序占用，复制失败，请重试。");
        }

        void ShowInvalid(string message)
        {
            resultLabel.Text = message;
            resultLabel.ForeColor = UiTheme.Error;
            resultBox.Clear();
            feedback.Show(FeedbackBar.Kind.Error, message);
        }

        void ShowBatch(PortQueryDetailResult batch, string inputLabel, IReadOnlyList<int> requestedPorts)
        {
            if (batch.Status == "error")
            {
                ShowInvalid(batch.Message);
                return;
            }

            var high = batch.HighRiskCount;
            resultLabel.Text =
                $"已查询 {batch.RequestedCount} 个端口：占用 {batch.OccupiedCount}" +
                (high > 0 ? $"（高危 {high}）" : string.Empty) +
                $"，空闲 {batch.FreeCount}";
            resultLabel.ForeColor = high > 0
                ? UiTheme.Error
                : batch.OccupiedCount > 0 ? UiTheme.Warn : UiTheme.Success;

            var lines = new List<string>
            {
                $"查询范围：{inputLabel}（共 {batch.RequestedCount} 个端口）",
                string.Empty
            };

            if (batch.OccupiedCount == 0)
            {
                lines.Add("没有发现被占用的端口。");
            }
            else
            {
                lines.Add("【占用端口详情】");
                // 全量扫描时占用端口可能有上百个，限制展开数量，避免文本框卡顿。
                var shown = batch.Occupancies.Take(100).ToList();
                foreach (var item in shown)
                {
                    var level = PortRiskEvaluator.LevelText(item.RiskLevel);
                    lines.Add(string.Empty);
                    lines.Add($"━━ 端口 {item.Port}　【{level}】━━");
                    foreach (var reason in item.RiskReasons)
                        lines.Add($"  风险：{reason}");
                    lines.Add($"  协议：{item.Protocols}");
                    lines.Add($"  本地监听：{item.LocalAddresses}");
                    lines.Add($"  连接状态：{item.States}");
                    if (!string.IsNullOrEmpty(item.RemoteAddresses))
                        lines.Add($"  远端地址：{item.RemoteAddresses}");
                    lines.Add($"  PID：{string.Join("、", item.Pids)}");

                    foreach (var process in item.Processes)
                    {
                        lines.Add($"  ── 进程 PID {process.Pid} ──");
                        lines.Add($"    进程名：{process.Name}");
                        lines.Add($"    可执行路径：{process.ExecutablePath ?? "未知（权限不足或进程已退出）"}");
                        lines.Add($"    命令行：{process.CommandLine ?? "未知"}");
                        lines.Add($"    启动时间：{PortDetailFormat.FormatTime(process.StartTime)}");
                        lines.Add($"    占用时长：{PortDetailFormat.FormatAge(process.Age)}（按进程运行时长估算）");
                        lines.Add($"    父进程 PID：{process.ParentPid?.ToString() ?? "未知"}");
                        lines.Add($"    所有者：{process.Owner ?? "未知"}");
                        lines.Add($"    内存 / 线程 / 句柄：{PortDetailFormat.FormatBytes(process.WorkingSetBytes)} / {process.ThreadCount?.ToString() ?? "未知"} / {process.HandleCount?.ToString() ?? "未知"}");
                        if (!string.IsNullOrEmpty(process.Note))
                            lines.Add($"    备注：{process.Note}");
                    }
                }
                if (batch.Occupancies.Count > shown.Count)
                    lines.Add($"…… 其余 {batch.Occupancies.Count - shown.Count} 个占用端口未展开，可缩小查询范围查看。");
            }

            if (batch.FreeCount > 0)
            {
                lines.Add(string.Empty);
                if (batch.RequestedCount <= 50)
                {
                    lines.Add("【空闲端口】");
                    var occupiedPorts = batch.Occupancies.Select(o => o.Port).ToHashSet();
                    foreach (var port in requestedPorts.Where(p => !occupiedPorts.Contains(p)))
                        lines.Add($"  {port,-6} 未被占用");
                }
                else
                {
                    lines.Add($"其余 {batch.FreeCount} 个端口未被占用（数量较多，未逐项列出）。");
                }
            }

            lines.Add(string.Empty);
            lines.Add("说明：风险标记基于端口与监听地址的规则提示，仅供排查参考，不等同于漏洞扫描结论；系统不提供端口级精确计时，占用时长按进程启动时间估算。");
            resultBox.Text = string.Join(Environment.NewLine, lines);
            resultBox.SelectionStart = 0;
            resultBox.ScrollToCaret();
            feedback.Show(
                high > 0 ? FeedbackBar.Kind.Error
                    : batch.OccupiedCount > 0 ? FeedbackBar.Kind.Warn : FeedbackBar.Kind.Success,
                high > 0
                    ? $"发现 {high} 个高危端口，请重点核查。"
                    : batch.OccupiedCount > 0
                        ? $"发现 {batch.OccupiedCount} 个被占用的端口，详见上方详情。"
                        : $"查询完成，{batch.RequestedCount} 个端口均未被占用。");
        }

        // netstat 可能耗时数秒：放到线程池执行，UI 不冻结；busy 防止 Enter/连点重入。
        var busy = false;
        async Task DoQueryAsync()
        {
            if (busy) return;
            if (!PortQuery.TryParsePorts(portBox.Text, out var ports, out var message))
            {
                ShowInvalid(message);
                return;
            }

            var inputLabel = string.IsNullOrWhiteSpace(portBox.Text)
                ? "全部端口（1-65535）"
                : portBox.Text.Trim();
            busy = true;
            btnQuery.Enabled = false;
            btnAll.Enabled = false;
            resultLabel.Text = "正在查询…";
            resultLabel.ForeColor = UiTheme.Muted;
            resultBox.Clear();
            feedback.Clear();
            try
            {
                ShowBatch(await Task.Run(() => PortQuery.QueryDetailedPorts(ports)), inputLabel, ports);
            }
            catch (Exception ex)
            {
                ShowInvalid($"查询失败：{ex.Message}");
            }
            finally
            {
                busy = false;
                btnQuery.Enabled = true;
                btnAll.Enabled = true;
            }
        }

        btnQuery.Click += async (_, _) => await DoQueryAsync();
        btnAll.Click += async (_, _) =>
        {
            portBox.Text = string.Empty;
            await DoQueryAsync();
        };
        portBox.KeyDown += async (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            await DoQueryAsync();
        };

        root.Controls.Add(resultCard);
        root.Controls.Add(UiTheme.CardGap());
        root.Controls.Add(inputCard);
        root.Controls.Add(header);
        return root;
    }
}
