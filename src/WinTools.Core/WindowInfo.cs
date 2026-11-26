using System.Drawing;

namespace WinTools.Core;

/// <summary>桌面窗口的归类，用于在截图时给用户一句人话（"微信"、"浏览器"、"任务栏"）。</summary>
public enum WindowCategory
{
    Other,
    Browser,
    Chat,
    Editor,
    Terminal,
    Media,
    Office,
    Design,
    System,
    TaskBar,
}

/// <summary>
/// 桌面上一个可被截图的窗口。
///
/// 坐标约定与截图工具保持一致：<see cref="Bounds"/> 是**物理屏幕像素**，来自
/// GetWindowRect / DwmGetWindowAttribute，不是覆盖层客户区坐标，也不能乘 UiTheme.S。
/// 多显示器时坐标可能是负数（左/上副屏）。
/// </summary>
public sealed class WindowInfo
{
    public WindowInfo(IntPtr handle, string title, string processName, string appName,
        WindowCategory category, Rectangle bounds, bool isTaskBar, bool isForeground)
    {
        Handle = handle;
        Title = title ?? "";
        ProcessName = processName ?? "";
        AppName = string.IsNullOrWhiteSpace(appName) ? "未知程序" : appName;
        Category = category;
        Bounds = bounds;
        IsTaskBar = isTaskBar;
        IsForeground = isForeground;
    }

    public IntPtr Handle { get; }
    public string Title { get; }
    public string ProcessName { get; }
    /// <summary>中文应用名，如"微信"、"浏览器"、"任务栏"。</summary>
    public string AppName { get; }
    public WindowCategory Category { get; }
    /// <summary>窗口在屏幕上的矩形（物理像素）。</summary>
    public Rectangle Bounds { get; }
    public bool IsTaskBar { get; }
    /// <summary>是否为截图瞬间的前台窗口。</summary>
    public bool IsForeground { get; }

    /// <summary>列表里显示的一行：应用名 + 窗口标题。</summary>
    public string DisplayLabel
    {
        get
        {
            var title = Title.Trim();
            return title.Length == 0 ? AppName : $"{AppName} · {title}";
        }
    }
}

/// <summary>
/// 按进程名把窗口翻译成中文应用名与类别。
///
/// 这一层刻意做成纯函数（只吃字符串、只吐结果），这样微信/浏览器/任务栏这些
/// 映射规则可以单测，不必真的开一个微信窗口来验证。
/// </summary>
public static class WindowClassifier
{
    private static readonly Dictionary<string, (string App, WindowCategory Category)> Map =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // 聊天
            ["wechat"] = ("微信", WindowCategory.Chat),
            ["weixin"] = ("微信", WindowCategory.Chat),
            ["wechatappex"] = ("微信", WindowCategory.Chat),
            ["wechatplayer"] = ("微信", WindowCategory.Chat),
            ["weixin_legacy"] = ("微信", WindowCategory.Chat),
            ["wecom"] = ("企业微信", WindowCategory.Chat),
            ["wxwork"] = ("企业微信", WindowCategory.Chat),
            ["wechatwork"] = ("企业微信", WindowCategory.Chat),
            ["qq"] = ("QQ", WindowCategory.Chat),
            ["tim"] = ("TIM", WindowCategory.Chat),
            ["qqprotect"] = ("QQ", WindowCategory.Chat),
            ["dingtalk"] = ("钉钉", WindowCategory.Chat),
            ["feishu"] = ("飞书", WindowCategory.Chat),
            ["lark"] = ("飞书", WindowCategory.Chat),
            ["telegram"] = ("Telegram", WindowCategory.Chat),
            ["discord"] = ("Discord", WindowCategory.Chat),
            ["slack"] = ("Slack", WindowCategory.Chat),

            // 浏览器
            ["chrome"] = ("Chrome 浏览器", WindowCategory.Browser),
            ["msedge"] = ("Edge 浏览器", WindowCategory.Browser),
            ["firefox"] = ("Firefox 浏览器", WindowCategory.Browser),
            ["brave"] = ("Brave 浏览器", WindowCategory.Browser),
            ["opera"] = ("Opera 浏览器", WindowCategory.Browser),
            ["vivaldi"] = ("Vivaldi 浏览器", WindowCategory.Browser),
            ["arc"] = ("Arc 浏览器", WindowCategory.Browser),
            ["chromium"] = ("Chromium 浏览器", WindowCategory.Browser),
            ["360se"] = ("360 安全浏览器", WindowCategory.Browser),
            ["360chrome"] = ("360 极速浏览器", WindowCategory.Browser),
            ["qqbrowser"] = ("QQ 浏览器", WindowCategory.Browser),
            ["sogouexplorer"] = ("搜狗浏览器", WindowCategory.Browser),
            ["maxthon"] = ("傲游浏览器", WindowCategory.Browser),
            ["liebao"] = ("猎豹浏览器", WindowCategory.Browser),
            ["waterfox"] = ("Waterfox 浏览器", WindowCategory.Browser),
            ["librewolf"] = ("LibreWolf 浏览器", WindowCategory.Browser),
            ["whale"] = (" Whale 浏览器", WindowCategory.Browser),

            // 代码编辑器
            ["code"] = ("VS Code", WindowCategory.Editor),
            ["code - insiders"] = ("VS Code Insiders", WindowCategory.Editor),
            ["devenv"] = ("Visual Studio", WindowCategory.Editor),
            ["idea64"] = ("IntelliJ IDEA", WindowCategory.Editor),
            ["idea"] = ("IntelliJ IDEA", WindowCategory.Editor),
            ["pycharm64"] = ("PyCharm", WindowCategory.Editor),
            ["webstorm64"] = ("WebStorm", WindowCategory.Editor),
            ["goland64"] = ("GoLand", WindowCategory.Editor),
            ["rider64"] = ("Rider", WindowCategory.Editor),
            ["sublime_text"] = ("Sublime Text", WindowCategory.Editor),
            ["notepad++"] = ("Notepad++", WindowCategory.Editor),
            ["notepad3"] = ("Notepad3", WindowCategory.Editor),
            ["hbuilderx"] = ("HBuilderX", WindowCategory.Editor),
            ["typora"] = ("Typora", WindowCategory.Editor),

            // 终端
            ["windowsterminal"] = ("Windows 终端", WindowCategory.Terminal),
            ["cmd"] = ("命令提示符", WindowCategory.Terminal),
            ["powershell"] = ("PowerShell", WindowCategory.Terminal),
            ["pwsh"] = ("PowerShell", WindowCategory.Terminal),
            ["alacritty"] = ("Alacritty", WindowCategory.Terminal),
            ["wezterm"] = ("WezTerm", WindowCategory.Terminal),
            ["hyper"] = ("Hyper", WindowCategory.Terminal),
            ["mintty"] = ("Git Bash", WindowCategory.Terminal),

            // 办公
            ["excel"] = ("Excel", WindowCategory.Office),
            ["winword"] = ("Word", WindowCategory.Office),
            ["powerpnt"] = ("PowerPoint", WindowCategory.Office),
            ["outlook"] = ("Outlook", WindowCategory.Office),
            ["onenote"] = ("OneNote", WindowCategory.Office),
            ["msaccess"] = ("Access", WindowCategory.Office),
            ["visio"] = ("Visio", WindowCategory.Office),
            ["wps"] = ("WPS", WindowCategory.Office),
            ["et"] = ("WPS 表格", WindowCategory.Office),
            ["wpp"] = ("WPS 演示", WindowCategory.Office),
            ["wpspdf"] = ("WPS PDF", WindowCategory.Office),

            // 影音
            ["vlc"] = ("VLC", WindowCategory.Media),
            ["potplayer"] = ("PotPlayer", WindowCategory.Media),
            ["mpc-hc"] = ("MPC-HC", WindowCategory.Media),
            ["qqplayer"] = ("QQ 播放器", WindowCategory.Media),
            ["thunderplayer"] = ("迅雷播放器", WindowCategory.Media),
            ["kmplayer"] = ("KMPlayer", WindowCategory.Media),
            ["spotify"] = ("Spotify", WindowCategory.Media),
            ["obs64"] = ("OBS", WindowCategory.Media),
            ["potplayermini64"] = ("PotPlayer", WindowCategory.Media),

            // 设计
            ["figma"] = ("Figma", WindowCategory.Design),
            ["photoshop"] = ("Photoshop", WindowCategory.Design),
            ["illustrator"] = ("Illustrator", WindowCategory.Design),
            ["sketch"] = ("Sketch", WindowCategory.Design),
            ["xmind"] = ("XMind", WindowCategory.Design),
            ["figma_figma"] = ("Figma", WindowCategory.Design),

            // 系统
            ["explorer"] = ("文件资源管理器", WindowCategory.System),
            ["taskmgr"] = ("任务管理器", WindowCategory.System),
            ["control"] = ("控制面板", WindowCategory.System),
            ["mmc"] = ("管理工具", WindowCategory.System),
            ["regedit"] = ("注册表编辑器", WindowCategory.System),
            ["searchhost"] = ("Windows 搜索", WindowCategory.System),
            ["startmenuexperiencehost"] = ("开始菜单", WindowCategory.System),
            ["shellexperiencehost"] = ("Windows 外壳", WindowCategory.System),
            ["textinputhost"] = ("Windows 输入法", WindowCategory.System),
            ["applicationframehost"] = ("Windows 应用", WindowCategory.System),
            ["snipaste"] = ("Snipaste", WindowCategory.System),
            ["pixpin"] = ("PixPin", WindowCategory.System),
            ["sharex"] = ("ShareX", WindowCategory.System),
            ["fscapture"] = (" FSCapture", WindowCategory.System),
            ["lightshot"] = ("Lightshot", WindowCategory.System),
        };

    /// <summary>任务栏的窗口类名（主任务栏 / 副屏任务栏）。</summary>
    public static bool IsTaskBarClass(string? className) =>
        className is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";

    /// <summary>
    /// 进程名 → (中文应用名, 类别)。进程名不带扩展名；未知程序原样返回，类别为 Other。
    /// </summary>
    public static (string App, WindowCategory Category) Classify(string? processName, bool isTaskBar = false)
    {
        if (isTaskBar) return ("任务栏", WindowCategory.TaskBar);

        var name = (processName ?? "").Trim();
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        if (name.Length == 0) return ("未知程序", WindowCategory.Other);

        return Map.TryGetValue(name, out var hit)
            ? hit
            : (name, WindowCategory.Other);
    }
}
