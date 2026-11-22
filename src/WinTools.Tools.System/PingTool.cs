using System.Drawing;
using System.Net.NetworkInformation;
using System.Windows.Forms;
using WinTools.Core;
using WinTools.Core.Ui;

namespace WinTools.Tools.SystemTools;

/// <summary>N1 Ping 测试：解析目标 → 多次探测 → 中文结果 + 丢包/延迟统计。普通权限、无需联网也能测本机与网关。</summary>
[Tool(Order = 85)]
public sealed class PingTool : ITool
{
    public string Id => "ping-test";
    public string Name => "Ping 测试";
    public string Category => "系统工具";
    public Icon? Icon => null;

    public Control CreateUI()
    {
        // 行内标签（AutoSize 约 18px）与数字框/按钮（32px）高度不同，统一贴顶会让标签偏高。
        // 按各控件实测高度补半差，100% 与 200% DPI 下都能真正垂直居中。
        static void CenterInRow(Control c, int rowHeight, int gap) =>
            c.Margin = new Padding(0, Math.Max(0, (rowHeight - c.Height) / 2), UiTheme.Sx(gap), 0);

        var root = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Bg };
        var header = new ToolHeader("Ping 测试",
            "测试到主机或 IP 的连通性与延迟；本机、网关等内网目标无需外网。结果可一键复制。");

        var targetInput = UiTheme.Input("127.0.0.1 或网关地址", 340);
        targetInput.Dock = DockStyle.Top;
        var hint = UiTheme.HintLabel("次数 1-20；超时按秒计。域名解析失败或防火墙拦截时会给出明确原因。");
        hint.AutoSize = false;
        hint.Dock = DockStyle.Top;
        hint.Height = UiTheme.Sx(24);
        hint.TextAlign = ContentAlignment.MiddleLeft;

        var countNumeric = new FluentNumeric
        {
            Minimum = 1, Maximum = PingChecker.MaxCount, Value = PingChecker.DefaultCount,
            Size = UiTheme.Sz(74, 32), Margin = UiTheme.Pad(0, 0, 8, 0)
        };
        var timeoutNumeric = new FluentNumeric
        {
            Minimum = 1, Maximum = 30, Value = 2,
            Size = UiTheme.Sz(74, 32), Margin = UiTheme.Pad(0, 0, 8, 0)
        };
        var btnStart = UiTheme.Button("开始测试", UiTheme.ButtonKind.Primary, 110);
        var btnCancel = UiTheme.Button("取消", UiTheme.ButtonKind.Secondary, 76);
        btnCancel.Enabled = false;
        var settingRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = UiTheme.Sx(46),
            WrapContents = false,
            Margin = Padding.Empty
        };
        settingRow.Controls.Add(UiTheme.FieldLabel("次数"));
        settingRow.Controls.Add(countNumeric);
        settingRow.Controls.Add(UiTheme.FieldLabel("超时（秒）"));
        settingRow.Controls.Add(timeoutNumeric);
        settingRow.Controls.Add(btnStart);
        settingRow.Controls.Add(btnCancel);
        foreach (Control c in settingRow.Controls)
            CenterInRow(c, settingRow.Height, 8);

        var inputCard = new CardPanel("目标与参数") { Dock = DockStyle.Top, Height = UiTheme.Sx(176) };
        inputCard.HeaderIcon = "ping";
        // 后加的更靠外：settingRow 贴底、hint 与 targetInput 依次贴顶。
        inputCard.Content.Controls.Add(targetInput);
        inputCard.Content.Controls.Add(settingRow);
        inputCard.Content.Controls.Add(hint);

        var resultBox = UiTheme.MultilineInput(readOnly: true);
        var feedback = new FeedbackBar { Dock = DockStyle.Bottom };
        var resultCard = new CardPanel("测试结果") { Dock = DockStyle.Fill, Margin = Padding.Empty };
        resultCard.HeaderIcon = "network";
        resultCard.AddAction("复制结果", (_, _) =>
        {
            if (resultBox.Text.Length == 0)
            {
                feedback.Show(FeedbackBar.Kind.Warn, "暂无可复制的结果，请先测试。");
                return;
            }
            var copied = ClipboardUtil.TryCopy(resultBox.Text);
            feedback.Show(copied ? FeedbackBar.Kind.Success : FeedbackBar.Kind.Warn,
                copied ? "已复制到剪贴板。" : "剪贴板被其他程序占用，复制失败，请重试。");
        });
        resultCard.Content.Controls.Add(resultBox);
        resultCard.Content.Controls.Add(feedback);

        System.Threading.CancellationTokenSource? cts = null;
        var busy = false;

        btnCancel.Click += (_, _) => cts?.Cancel();

        btnStart.Click += async (_, _) =>
        {
            if (busy) return;
            if (!PingChecker.TryValidateTarget(targetInput.Text, out var target, out var error))
            {
                feedback.Show(FeedbackBar.Kind.Warn, error);
                return;
            }
            busy = true;
            cts?.Dispose();
            cts = new System.Threading.CancellationTokenSource();
            var token = cts.Token;
            var timeoutMs = timeoutNumeric.Value * 1000;
            var count = countNumeric.Value;
            btnStart.Enabled = false;
            btnCancel.Enabled = true;
            resultBox.Clear();
            feedback.Show(FeedbackBar.Kind.Info,
                $"正在测试 {target}，共 {count} 次，每次间隔约 {PingChecker.DefaultIntervalMs} 毫秒，期间可点「取消」。");

            // 收集已完成的探测：本机 ping 很快，若不保留部分结果，取消后结果框是空的，用户会以为取消没生效。
            var collected = new List<PingProbeResult>();
            try
            {
                var summary = await PingChecker.RunAsync(target, count, async (seq, ct) =>
                {
                    using var ping = new Ping();
                    var reply = await ping.SendPingAsync(target, timeoutMs).WaitAsync(ct).ConfigureAwait(false);
                    var result = PingChecker.FromReply(seq, reply);
                    collected.Add(result);
                    return result;
                }, token, PingChecker.DefaultIntervalMs).ConfigureAwait(true);

                resultBox.Text = summary.Format();
                var kind = summary.Received == 0 ? FeedbackBar.Kind.Error
                    : summary.Lost > 0 ? FeedbackBar.Kind.Warn : FeedbackBar.Kind.Success;
                feedback.Show(kind, $"测试完成：{summary.Received}/{summary.Sent} 成功，丢包率 {summary.LossPercent:F0}%。");
            }
            catch (OperationCanceledException)
            {
                if (collected.Count > 0)
                    resultBox.Text = new PingSummary(target, collected).Format()
                        + "\r\n\r\n（已取消，以上为已完成的部分结果）";
                feedback.Show(FeedbackBar.Kind.Info, $"已取消测试（完成 {collected.Count}/{count} 次）。");
            }
            catch (PingException ex)
            {
                feedback.Show(FeedbackBar.Kind.Error, $"Ping 失败：{ex.Message}");
            }
            catch (Exception ex)
            {
                feedback.Show(FeedbackBar.Kind.Error, $"测试失败：{ex.Message}");
            }
            finally
            {
                busy = false;
                btnStart.Enabled = true;
                btnCancel.Enabled = false;
            }
        };

        targetInput.Text = "127.0.0.1";
        root.Controls.Add(resultCard);
        root.Controls.Add(UiTheme.CardGap());
        root.Controls.Add(inputCard);
        root.Controls.Add(header);
        return root;
    }
}
