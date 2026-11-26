using System.Drawing;
using System.Windows.Forms;
using WinTools.Core;
using WinTools.Core.Ui;

namespace WinTools.Tools.Files;

/// <summary>
/// 屏幕截图：全屏 / 区域框选 / 延时三种模式；截取后进入标注编辑（矩形、椭圆、箭头、画笔、
/// 文字、表情、马赛克、撤销），可保存到 PNG/JPG、置顶桌面或复制到剪贴板。
/// 纯本地抓屏与处理，无任何网络请求；截图过程会短暂隐藏工具窗口，结束后自动恢复。
///
/// 支持全局快捷键（默认 Ctrl+Shift+A 区域 / Ctrl+Shift+F 全屏）：程序收进托盘、主窗口隐藏后仍然生效，
/// 热键可在本页自定义并持久化到 %AppData%\WinTools\screen-capture.json。
/// </summary>
[Tool(Order = 97)]
public sealed class ScreenshotTool : ITool, IBackgroundTool, IToolCommand
{
    public string Id => "screen-capture";
    public string Name => "屏幕截图";
    public string Category => "图片工具";
    public Icon? Icon => null;

    /// <summary>宿主启动时注册全局热键；窗口隐藏不影响热键生效。</summary>
    public void Start(BackgroundToolContext context) => ScreenCaptureRuntime.Start(context);

    public void Stop() => ScreenCaptureRuntime.Stop();

    /// <summary>供托盘菜单等外部入口触发：capture-region / capture-full。</summary>
    public bool TryInvoke(string command) => ScreenCaptureRuntime.TryInvoke(command);

    public Control CreateUI()
    {
        var root = new Panel { Dock = DockStyle.Fill, BackColor = UiTheme.Bg };
        var header = new ToolHeader("屏幕截图",
            "全屏 / 区域 / 滚动 / 延时截图；滚动截图把长页面自动拼成一张长图。区域截图会自动识别桌面上打开的窗口（微信、浏览器、任务栏…），点编号即可选中。支持全局快捷键与截图后自动复制。编辑时可画框、箭头、画笔、文字、马赛克，复制或保存。纯本地处理，不上传。");

        // ---- 操作卡 ----
        var btnFull = UiTheme.Button("全屏截图", "apps", UiTheme.ButtonKind.Primary, 118);
        var btnRegion = UiTheme.Button("区域截图", "screenshot", UiTheme.ButtonKind.Secondary, 118);
        var btnScroll = UiTheme.Button("滚动截图", "scroll", UiTheme.ButtonKind.Secondary, 118);
        var btnDelay = UiTheme.Button("延时截图", "clock", UiTheme.ButtonKind.Secondary, 118);
        var delayNumeric = new FluentNumeric { Minimum = 1, Maximum = ScreenCapture.MaxDelaySeconds, Size = UiTheme.Sz(76, 32) };
        delayNumeric.Value = 3;

        var modeRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, WrapContents = false, Margin = Padding.Empty,
            Padding = UiTheme.Pad(0, 0, 0, 8)
        };
        modeRow.Controls.Add(btnFull);
        modeRow.Controls.Add(btnRegion);
        modeRow.Controls.Add(btnScroll);
        modeRow.Controls.Add(btnDelay);
        modeRow.Controls.Add(UiTheme.FieldLabel("延时秒数"));
        modeRow.Controls.Add(delayNumeric);
        foreach (Control c in modeRow.Controls)
            c.Margin = new Padding(0, Math.Max(0, (UiTheme.Sx(32) - c.Height) / 2), UiTheme.Sx(10), 0);

        var btnPng = UiTheme.Button("PNG", UiTheme.ButtonKind.Primary, 72);
        var btnJpg = UiTheme.Button("JPG", UiTheme.ButtonKind.Secondary, 72);
        var qualityNumeric = new FluentNumeric { Minimum = 1, Maximum = 100, Size = UiTheme.Sz(76, 32) };
        qualityNumeric.Value = 92;

        var formatRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, WrapContents = false, Margin = Padding.Empty,
            Padding = UiTheme.Pad(0, 0, 0, 6)
        };
        formatRow.Controls.Add(UiTheme.FieldLabel("保存格式"));
        formatRow.Controls.Add(btnPng);
        formatRow.Controls.Add(btnJpg);
        formatRow.Controls.Add(UiTheme.FieldLabel("JPG 质量"));
        formatRow.Controls.Add(qualityNumeric);
        formatRow.Controls.Add(UiTheme.HintLabel("质量 1-100；PNG 忽略此值"));
        foreach (Control c in formatRow.Controls)
            c.Margin = new Padding(0, Math.Max(0, (UiTheme.Sx(32) - c.Height) / 2), UiTheme.Sx(10), 0);

        var opCard = new CardPanel("截图设置") { Dock = DockStyle.Top, Height = UiTheme.Sx(180) };
        opCard.HeaderIcon = "screenshot";
        var btnWindows = UiTheme.Button("桌面窗口", "apps", UiTheme.ButtonKind.Secondary, 112);

        var hint = UiTheme.HintLabel("区域截图会自动识别桌面窗口并编号，点一下即可选中；滚动截图：框选后向下滚动自动拼成长图。");
        hint.Margin = UiTheme.Pad(0, 4, 0, 0);
        var hintRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, Height = UiTheme.Sx(32), WrapContents = false, Margin = Padding.Empty
        };
        hintRow.Controls.Add(btnWindows);
        hintRow.Controls.Add(hint);
        var opSpacer = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        opCard.Content.Controls.Add(opSpacer);
        opCard.Content.Controls.Add(hintRow);
        opCard.Content.Controls.Add(formatRow);
        opCard.Content.Controls.Add(modeRow);

        // ---- 全局快捷键卡 ----
        var settings = ScreenCaptureRuntime.Settings;

        var regionBox = new HotkeyBox();
        if (HotkeyGesture.TryParse(settings.RegionHotkey, out var regionGesture)) regionBox.Gesture = regionGesture;
        var fullBox = new HotkeyBox();
        if (HotkeyGesture.TryParse(settings.FullHotkey, out var fullGesture)) fullBox.Gesture = fullGesture;
        var scrollBox = new HotkeyBox();
        if (HotkeyGesture.TryParse(settings.ScrollHotkey, out var scrollGesture)) scrollBox.Gesture = scrollGesture;
        var autoCopy = new FluentCheckBox { Text = "截图完成后自动复制到剪贴板", Checked = settings.AutoCopy };

        var saveKeys = UiTheme.Button("保存并应用", "check", UiTheme.ButtonKind.Primary, 108);
        var resetKeys = UiTheme.Button("恢复默认", "refresh", UiTheme.ButtonKind.Subtle, 92);

        var keyRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, WrapContents = false, Margin = Padding.Empty,
        };
        keyRow.Controls.Add(UiTheme.FieldLabel("区域截图"));
        keyRow.Controls.Add(regionBox);
        keyRow.Controls.Add(UiTheme.FieldLabel("全屏截图"));
        keyRow.Controls.Add(fullBox);
        foreach (Control c in keyRow.Controls)
            c.Margin = new Padding(0, Math.Max(0, (UiTheme.Sx(32) - c.Height) / 2), UiTheme.Sx(10), 0);

        // 三个热键一排放不下（窗口较窄时会被裁切），拆成两行：第一行区域/全屏，第二行滚动 + 操作按钮。
        var keyRow2 = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, WrapContents = false,
            Margin = Padding.Empty, Padding = UiTheme.Pad(0, 8, 0, 0)
        };
        keyRow2.Controls.Add(UiTheme.FieldLabel("滚动截图"));
        keyRow2.Controls.Add(scrollBox);
        keyRow2.Controls.Add(saveKeys);
        keyRow2.Controls.Add(resetKeys);
        foreach (Control c in keyRow2.Controls)
            c.Margin = new Padding(0, Math.Max(0, (UiTheme.Sx(32) - c.Height) / 2), UiTheme.Sx(10), 0);

        var autoRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, WrapContents = false,
            Margin = Padding.Empty, Padding = UiTheme.Pad(0, 8, 0, 0)
        };
        autoRow.Controls.Add(autoCopy);

        var keyStatus = UiTheme.HintLabel("点输入框后直接按组合键录入；Delete 清空＝禁用该入口。热键在托盘后台依然生效。");
        var keyStatusRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, Height = UiTheme.Sx(26), WrapContents = false,
            Margin = Padding.Empty, Padding = UiTheme.Pad(0, 4, 0, 0)
        };
        keyStatusRow.Controls.Add(keyStatus);

        var hotkeyCard = new CardPanel("全局快捷键") { Dock = DockStyle.Top, Height = UiTheme.Sx(196) };
        hotkeyCard.HeaderIcon = "bolt";
        var keySpacer = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        hotkeyCard.Content.Controls.Add(keySpacer);
        hotkeyCard.Content.Controls.Add(keyStatusRow);
        hotkeyCard.Content.Controls.Add(autoRow);
        hotkeyCard.Content.Controls.Add(keyRow2);
        hotkeyCard.Content.Controls.Add(keyRow);

        // ---- 预览状态 ----
        Bitmap? current = null;
        string formatId = "png";

        // ---- 预览卡 ----
        var preview = new PictureBox
        {
            Dock = DockStyle.Fill,
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = UiTheme.FieldBg
        };
        var feedback = new FeedbackBar { Dock = DockStyle.Bottom };
        var previewCard = new CardPanel("截图预览") { Dock = DockStyle.Fill, Margin = Padding.Empty };
        previewCard.HeaderIcon = "image-format";
        var btnSave = previewCard.AddAction("另存为", "download", (_, _) => SaveCurrent());
        var btnCopy = previewCard.AddAction("复制图片", "copy", (_, _) => CopyCurrent());
        btnSave.Enabled = btnCopy.Enabled = false;
        previewCard.Content.Controls.Add(preview);
        previewCard.Content.Controls.Add(feedback);

        void SetCurrent(Bitmap? image, string sizeText)
        {
            var old = current;
            current = image;
            preview.Image = current;
            old?.Dispose();
            btnSave.Enabled = btnCopy.Enabled = current is not null;
            previewCard.Title = current is null ? "截图预览" : $"截图预览 · {sizeText}";
        }

        void SelectFormat(string id)
        {
            formatId = id;
            btnPng.Variant = id == "png" ? UiTheme.ButtonKind.Primary : UiTheme.ButtonKind.Secondary;
            btnJpg.Variant = id == "jpg" ? UiTheme.ButtonKind.Primary : UiTheme.ButtonKind.Secondary;
            qualityNumeric.Enabled = id == "jpg";
        }

        btnPng.Click += (_, _) => SelectFormat("png");
        btnJpg.Click += (_, _) => SelectFormat("jpg");
        SelectFormat("png");

        void HandleOutcome(CaptureOutcome outcome)
        {
            if (outcome.Completed && outcome.Image is not null)
            {
                var size = ScreenCapture.DescribeSize(outcome.Image.Size);
                SetCurrent(outcome.Image, size);
                feedback.Show(FeedbackBar.Kind.Success,
                    outcome.Copied
                        ? $"已完成 {size}，已自动复制到剪贴板；可另存为 {formatId.ToUpperInvariant()}"
                        : $"已完成 {size}；可另存为 {formatId.ToUpperInvariant()} 或复制到剪贴板");
                return;
            }
            feedback.Show(outcome.IsError ? FeedbackBar.Kind.Error : FeedbackBar.Kind.Info,
                outcome.Message.Length > 0 ? outcome.Message : "已放弃本次截图。");
        }

        // 截图流程与热键入口共用 ScreenshotRunner：两条入口的行为不会分叉。
        void Capture(CaptureMode mode)
        {
            HandleOutcome(ScreenshotRunner.Run(mode, (int)delayNumeric.Value, root.FindForm(), autoCopy.Checked));
        }

        /// <summary>按已知区域直接截图（从"桌面窗口"列表点进来时用），跳过框选环节。</summary>
        void CaptureRegion(Rectangle region)
        {
            HandleOutcome(ScreenshotRunner.CaptureRegion(region, root.FindForm(), autoCopy.Checked));
        }

        /// <summary>弹出桌面窗口选择器：列出桌面上打开的窗口，双击即按其范围截图。</summary>
        void PickWindow()
        {
            using var picker = new WindowPickerForm(
                ScreenshotRunner.ListWindows(SystemInformation.VirtualScreen));

            // 贴在按钮下方，并夹进可视区，避免浮层跑到屏幕外。
            var anchor = btnWindows.PointToScreen(new Point(0, btnWindows.Height + UiTheme.Sx(4)));
            var area = Screen.FromControl(btnWindows).WorkingArea;
            var x = Math.Clamp(anchor.X, area.Left, Math.Max(area.Left, area.Right - picker.Width));
            var y = Math.Min(anchor.Y, Math.Max(area.Top, area.Bottom - picker.Height));
            picker.Location = new Point(x, y);

            picker.ShowDialog(root.FindForm());
            if (picker.Picked is { } picked) CaptureRegion(picked.Bounds);
        }


        void ApplyKeySettings()
        {
            var next = new ScreenCaptureSettings
            {
                AutoCopy = autoCopy.Checked,
                RegionHotkey = regionBox.Gesture.ToString(),
                FullHotkey = fullBox.Gesture.ToString(),
                ScrollHotkey = scrollBox.Gesture.ToString(),
            };
            var result = ScreenCaptureRuntime.Apply(next);
            var ok = result.StartsWith("已", StringComparison.Ordinal);
            keyStatus.Text = result;
            keyStatus.ForeColor = ok ? UiTheme.Muted : UiTheme.Warn;
            feedback.Show(ok ? FeedbackBar.Kind.Success : FeedbackBar.Kind.Warn, result);
        }

        // 录入热键期间暂停全局热键，否则按下当前生效的组合会先把截图拉起来。
        regionBox.RecordingStarted += (_, _) => ScreenCaptureRuntime.Suspend();
        regionBox.RecordingEnded += (_, _) => ScreenCaptureRuntime.Resume();
        fullBox.RecordingStarted += (_, _) => ScreenCaptureRuntime.Suspend();
        fullBox.RecordingEnded += (_, _) => ScreenCaptureRuntime.Resume();
        scrollBox.RecordingStarted += (_, _) => ScreenCaptureRuntime.Suspend();
        scrollBox.RecordingEnded += (_, _) => ScreenCaptureRuntime.Resume();

        // 工具页会被宿主缓存复用：重新显示时从运行时回填，避免看到上一次的旧值。
        var syncing = false;
        void SyncFromRuntime()
        {
            syncing = true;
            try
            {
                var currentSettings = ScreenCaptureRuntime.Settings;
                regionBox.Gesture = HotkeyGesture.TryParse(currentSettings.RegionHotkey, out var r) ? r : HotkeyGesture.Empty;
                fullBox.Gesture = HotkeyGesture.TryParse(currentSettings.FullHotkey, out var f) ? f : HotkeyGesture.Empty;
                scrollBox.Gesture = HotkeyGesture.TryParse(currentSettings.ScrollHotkey, out var sc) ? sc : HotkeyGesture.Empty;
                autoCopy.Checked = currentSettings.AutoCopy;
            }
            finally { syncing = false; }
        }
        root.VisibleChanged += (_, _) =>
        {
            if (!root.Visible) return;
            SyncFromRuntime();
        };

        saveKeys.Click += (_, _) => ApplyKeySettings();
        autoCopy.CheckedChanged += (_, _) => { if (!syncing) ApplyKeySettings(); };
        resetKeys.Click += (_, _) =>
        {
            var defaults = ScreenCaptureRuntime.Defaults();
            regionBox.Gesture = HotkeyGesture.TryParse(defaults.RegionHotkey, out var r) ? r : HotkeyGesture.Empty;
            fullBox.Gesture = HotkeyGesture.TryParse(defaults.FullHotkey, out var f) ? f : HotkeyGesture.Empty;
            scrollBox.Gesture = HotkeyGesture.TryParse(defaults.ScrollHotkey, out var sc) ? sc : HotkeyGesture.Empty;
            autoCopy.Checked = defaults.AutoCopy;
            ApplyKeySettings();
        };

        void SaveCurrent()
        {
            if (current is null) return;
            var ext = formatId == "png" ? ".png" : ".jpg";
            var filter = formatId == "png" ? "PNG 图片|*.png" : "JPG 图片|*.jpg";
            using var dlg = new SaveFileDialog
            {
                Title = "保存截图",
                Filter = filter,
                DefaultExt = ext,
                FileName = ScreenCapture.BuildFileName(DateTime.Now, ext),
                OverwritePrompt = true
            };
            if (dlg.ShowDialog() != DialogResult.OK) return;
            try
            {
                var bytes = ScreenCapture.Encode(current, formatId, qualityNumeric.Value);
                System.IO.File.WriteAllBytes(dlg.FileName, bytes);
                feedback.Show(FeedbackBar.Kind.Success,
                    $"已保存：{dlg.FileName}（{ImageFormatCodec.FormatSize(bytes.Length)}）");
            }
            catch (Exception ex)
            {
                feedback.Show(FeedbackBar.Kind.Error, $"保存失败：{ex.Message}");
            }
        }

        void CopyCurrent()
        {
            if (current is null) return;
            if (ClipboardUtil.TryCopyImage(current))
                feedback.Show(FeedbackBar.Kind.Success, "截图已复制到剪贴板，可直接粘贴。");
            else
                feedback.Show(FeedbackBar.Kind.Warn,
                    "剪贴板被其它程序占用，复制失败；请稍后重试或改用「另存为」。");
        }

        // 事件注册统一放在最后：lambda 里引用的局部函数会捕获后面才声明的变量，
        // C# 不允许在变量确定赋值之前就创建这样的委托（CS0165）。
        btnWindows.Click += (_, _) => PickWindow();
        btnFull.Click += (_, _) => Capture(CaptureMode.FullScreen);
        btnRegion.Click += (_, _) => Capture(CaptureMode.Region);
        btnScroll.Click += (_, _) => Capture(CaptureMode.Scroll);
        btnDelay.Click += (_, _) => Capture(CaptureMode.Delayed);

        root.Controls.Add(previewCard);
        root.Controls.Add(UiTheme.CardGap());
        root.Controls.Add(hotkeyCard);
        root.Controls.Add(UiTheme.CardGap());
        root.Controls.Add(opCard);
        root.Controls.Add(header);
        return root;
    }
}
