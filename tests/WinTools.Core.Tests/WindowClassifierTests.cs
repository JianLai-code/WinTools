using WinTools.Core;
using Xunit;

namespace WinTools.Core.Tests;

/// <summary>
/// 窗口自动识别的两条核心规则：
/// 1) 进程名 → 中文应用名与类别（微信、浏览器、任务栏…）；
/// 2) 哪些窗口值得作为截图候选（过滤不可见/最小化/工具窗/自己/壁纸/太小）。
/// </summary>
public class WindowClassifierTests
{
    [Theory]
    [InlineData("WeChat", "微信", WindowCategory.Chat)]
    [InlineData("wechat", "微信", WindowCategory.Chat)]
    [InlineData("Weixin", "微信", WindowCategory.Chat)]
    [InlineData("WXWork", "企业微信", WindowCategory.Chat)]
    [InlineData("QQ", "QQ", WindowCategory.Chat)]
    [InlineData("DingTalk", "钉钉", WindowCategory.Chat)]
    [InlineData("Feishu", "飞书", WindowCategory.Chat)]
    public void Classify_KnownChatApps(string process, string app, WindowCategory category)
    {
        var (name, cat) = WindowClassifier.Classify(process);
        Assert.Equal(app, name);
        Assert.Equal(category, cat);
    }

    [Theory]
    [InlineData("chrome", "Chrome 浏览器")]
    [InlineData("msedge", "Edge 浏览器")]
    [InlineData("firefox", "Firefox 浏览器")]
    [InlineData("360se", "360 安全浏览器")]
    [InlineData("QQBrowser", "QQ 浏览器")]
    public void Classify_KnownBrowsers(string process, string app)
    {
        var (name, cat) = WindowClassifier.Classify(process);
        Assert.Equal(app, name);
        Assert.Equal(WindowCategory.Browser, cat);
    }

    [Theory]
    [InlineData("Code", "VS Code", WindowCategory.Editor)]
    [InlineData("WindowsTerminal", "Windows 终端", WindowCategory.Terminal)]
    [InlineData("EXCEL", "Excel", WindowCategory.Office)]
    [InlineData("vlc", "VLC", WindowCategory.Media)]
    [InlineData("Figma", "Figma", WindowCategory.Design)]
    [InlineData("explorer", "文件资源管理器", WindowCategory.System)]
    public void Classify_OtherCategories(string process, string app, WindowCategory category)
    {
        var (name, cat) = WindowClassifier.Classify(process);
        Assert.Equal(app, name);
        Assert.Equal(category, cat);
    }

    [Fact]
    public void Classify_TaskBarWins_OverProcessName()
    {
        var (name, cat) = WindowClassifier.Classify("explorer", isTaskBar: true);
        Assert.Equal("任务栏", name);
        Assert.Equal(WindowCategory.TaskBar, cat);
    }

    [Theory]
    [InlineData("chrome.exe", "Chrome 浏览器")]
    [InlineData("Chrome.EXE", "Chrome 浏览器")]
    public void Classify_StripsExeSuffix(string process, string app)
    {
        var (name, _) = WindowClassifier.Classify(process);
        Assert.Equal(app, name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Classify_EmptyProcess_FallsBack(string? process)
    {
        var (name, cat) = WindowClassifier.Classify(process);
        Assert.Equal("未知程序", name);
        Assert.Equal(WindowCategory.Other, cat);
    }

    [Fact]
    public void Classify_UnknownProcess_KeepsRawName()
    {
        var (name, cat) = WindowClassifier.Classify("MyOwnApp");
        Assert.Equal("MyOwnApp", name);
        Assert.Equal(WindowCategory.Other, cat);
    }

    [Theory]
    [InlineData("Shell_TrayWnd", true)]
    [InlineData("Shell_SecondaryTrayWnd", true)]
    [InlineData("WeChatMainWndForPC", false)]
    [InlineData("", false)]
    public void IsTaskBarClass_DetectsTaskBar(string className, bool expected)
    {
        Assert.Equal(expected, WindowClassifier.IsTaskBarClass(className));
    }
}

/// <summary>候选窗口的过滤规则。</summary>
public class WindowEnumeratorTests
{
    private static readonly Rectangle Screen = new(0, 0, 1920, 1080);
    /// <summary>当作"别人家的进程"。</summary>
    private const int OtherProcess = 777;
    /// <summary>当作"截图工具自己"。</summary>
    private const int SelfProcess = 999;

    private static WindowProbe Probe(
        bool visible = true, bool minimized = false, bool toolWindow = false,
        bool isTaskBar = false, string className = "SomeWnd",
        Rectangle? bounds = null, int processId = OtherProcess) => new()
    {
        Visible = visible,
        Minimized = minimized,
        ToolWindow = toolWindow,
        IsTaskBar = isTaskBar,
        ClassName = className,
        Bounds = bounds ?? new Rectangle(100, 100, 800, 600),
        ProcessId = processId,
    };

    [Fact]
    public void IsSelectable_NormalWindow_Keeps()
    {
        Assert.True(WindowEnumerator.IsSelectable(Probe(), Screen, SelfProcess));
    }

    [Fact]
    public void IsSelectable_HiddenWindow_Drops()
    {
        Assert.False(WindowEnumerator.IsSelectable(Probe(visible: false), Screen, SelfProcess));
    }

    [Fact]
    public void IsSelectable_MinimizedWindow_Drops()
    {
        Assert.False(WindowEnumerator.IsSelectable(Probe(minimized: true), Screen, SelfProcess));
    }

    [Fact]
    public void IsSelectable_ToolWindow_Drops()
    {
        Assert.False(WindowEnumerator.IsSelectable(Probe(toolWindow: true), Screen, SelfProcess));
    }

    [Fact]
    public void IsSelectable_OwnProcess_Drops()
    {
        Assert.False(WindowEnumerator.IsSelectable(Probe(processId: SelfProcess), Screen, SelfProcess));
    }

    [Theory]
    [InlineData("Progman")]
    [InlineData("WorkerW")]
    [InlineData("Windows.UI.Core.CoreWindow")]
    public void IsSelectable_DesktopShellWindows_Drop(string className)
    {
        Assert.False(WindowEnumerator.IsSelectable(
            Probe(className: className, bounds: new Rectangle(0, 0, 1920, 1080)), Screen, SelfProcess));
    }

    [Fact]
    public void IsSelectable_OutsideScreen_Drops()
    {
        Assert.False(WindowEnumerator.IsSelectable(
            Probe(bounds: new Rectangle(3000, 3000, 800, 600)), Screen, SelfProcess));
    }

    [Fact]
    public void IsSelectable_PartiallyOutside_Keeps()
    {
        Assert.True(WindowEnumerator.IsSelectable(
            Probe(bounds: new Rectangle(1800, 100, 800, 600)), Screen, SelfProcess));
    }

    [Fact]
    public void IsSelectable_TooSmall_Drops()
    {
        Assert.False(WindowEnumerator.IsSelectable(
            Probe(bounds: new Rectangle(10, 10, 60, 40)), Screen, SelfProcess));
    }

    [Fact]
    public void IsSelectable_ShortButWideWindow_Drops()
    {
        // 高度不足 80 的普通窗口（如某个窄条）不算界面
        Assert.False(WindowEnumerator.IsSelectable(
            Probe(bounds: new Rectangle(0, 0, 800, 50)), Screen, SelfProcess));
    }

    [Fact]
    public void IsSelectable_TaskBar_ShorterAllowed()
    {
        // 任务栏只有 48px 高，但它是用户明确想要的"底部工具栏"，不能按普通窗口的高度下限过滤掉
        Assert.True(WindowEnumerator.IsSelectable(
            Probe(isTaskBar: true, className: "Shell_TrayWnd",
                bounds: new Rectangle(0, 1032, 1920, 48)), Screen, SelfProcess));
    }

    [Fact]
    public void IsSelectable_TaskBarWithToolWindowStyle_Keeps()
    {
        // 真实的任务栏（Shell_TrayWnd）扩展样式带 WS_EX_TOOLWINDOW，
        // 若按工具窗口一刀切掉，用户就截不到"电脑底部的工具栏"了。
        Assert.True(WindowEnumerator.IsSelectable(
            Probe(toolWindow: true, isTaskBar: true, className: "Shell_TrayWnd",
                bounds: new Rectangle(0, 1032, 1920, 48)), Screen, SelfProcess));
    }

    [Fact]
    public void IsSelectable_TaskBarTooNarrow_Drops()
    {
        Assert.False(WindowEnumerator.IsSelectable(
            Probe(isTaskBar: true, className: "Shell_TrayWnd",
                bounds: new Rectangle(0, 1032, 100, 48)), Screen, SelfProcess));
    }

    [Fact]
    public void IsSelectable_ZeroSize_Drops()
    {
        Assert.False(WindowEnumerator.IsSelectable(
            Probe(bounds: Rectangle.Empty), Screen, SelfProcess));
    }

    // ── DWM 可见边框的可信性校验 ─────────────────────────────
    private static readonly Rectangle RawFrame = new(100, 80, 620, 480);

    [Fact]
    public void IsPlausibleFrame_ShadowInset_Accepted()
    {
        // 常见的 DWM 阴影：每边缩进 8px，正是要采用的场景
        Assert.True(WindowEnumerator.IsPlausibleFrame(
            RawFrame, new Rectangle(108, 88, 604, 464)));
    }

    [Fact]
    public void IsPlausibleFrame_Identical_Accepted()
    {
        Assert.True(WindowEnumerator.IsPlausibleFrame(RawFrame, RawFrame));
    }

    [Fact]
    public void IsPlausibleFrame_BiggerThanRaw_Rejected()
    {
        // 远程/虚拟桌面上 DWM 曾报出比窗口大得多的矩形，必须退回 GetWindowRect
        Assert.False(WindowEnumerator.IsPlausibleFrame(
            RawFrame, new Rectangle(80, 60, 1186, 620)));
    }

    [Fact]
    public void IsPlausibleFrame_PositionDrift_Rejected()
    {
        // 尺寸差不多但位置漂到屏幕另一角，同样不可信
        Assert.False(WindowEnumerator.IsPlausibleFrame(
            RawFrame, new Rectangle(900, 800, 604, 464)));
    }

    [Fact]
    public void IsPlausibleFrame_HugeInset_Rejected()
    {
        Assert.False(WindowEnumerator.IsPlausibleFrame(
            RawFrame, new Rectangle(200, 180, 420, 280)));
    }

    [Fact]
    public void IsPlausibleFrame_Empty_Rejected()
    {
        Assert.False(WindowEnumerator.IsPlausibleFrame(RawFrame, Rectangle.Empty));
    }
}
