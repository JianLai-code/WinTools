using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace WinTools.Core;

/// <summary>
/// 一个窗口的原始探测结果。抽成结构体是为了让过滤规则能单测——不必真的开窗口。
/// </summary>
public readonly record struct WindowProbe
{
    /// <summary>record struct 带属性默认值时必须显式声明无参构造。</summary>
    public WindowProbe() { }

    public bool Visible { get; init; }
    public bool Minimized { get; init; }
    public bool ToolWindow { get; init; }
    public bool IsTaskBar { get; init; }
    public string ClassName { get; init; } = "";
    public Rectangle Bounds { get; init; }
    public int ProcessId { get; init; }
}

/// <summary>
/// 枚举桌面上打开的窗口，供截图时自动识别可框选区域（微信界面、浏览器、任务栏……）。
///
/// 屏幕本身没有"窗口"概念，只能问系统要：EnumWindows 列出所有顶层窗口，
/// 再逐个问它是否可见、是否最小化、在屏幕哪里、属于哪个程序。
///
/// 坐标是物理像素（GetWindowRect / DWM 在 PerMonitorV2 进程里返回的就是物理坐标），
/// 与截图工具的裁剪坐标同一套系，不能乘 UiTheme.S。
/// </summary>
public static class WindowEnumerator
{
    private const int GwlExstyle = -20;
    private const int WsExToolWindow = 0x00000080;
    private const int DwmwaExtendedFrameBounds = 9;
    /// <summary>普通窗口的最小尺寸：比这更小的多半是辅助窗/托盘气泡。</summary>
    public const int MinWindowWidth = 120;
    public const int MinWindowHeight = 80;
    /// <summary>任务栏较扁，单独放宽高度限制。</summary>
    public const int MinTaskBarWidth = 200;
    public const int MinTaskBarHeight = 20;

    /// <summary>桌面壁纸/开始菜单这类"铺满全屏但不是应用"的窗口，排除掉。</summary>
    private static readonly HashSet<string> DesktopClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman", "WorkerW", "Windows.UI.Core.CoreWindow", "Button", "Default IME", "MSCTFIME UI",
    };

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
        public int Width => Right - Left;
        public int Height => Bottom - Top;
        public Rectangle ToRectangle() => Rectangle.FromLTRB(Left, Top, Right, Bottom);
    }

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute,
        out RECT pvAttribute, int cbAttribute);

    /// <summary>
    /// 列出桌面上所有可作为截图目标的窗口。
    /// </summary>
    /// <param name="screenBounds">虚拟屏幕范围（物理像素），用来裁掉跑到屏外的窗口。</param>
    /// <param name="excludeProcessId">要排除的进程（一般是截图工具自己，否则会把自己框进去）。</param>
    public static IReadOnlyList<WindowInfo> List(Rectangle screenBounds, int? excludeProcessId = null)
    {
        var current = excludeProcessId ?? CurrentProcessId();
        var foreground = GetForegroundWindow();
        var found = new List<WindowInfo>();

        EnumWindows((hWnd, _) =>
        {
            try
            {
                var probe = Probe(hWnd);
                if (!IsSelectable(probe, screenBounds, current)) return true;

                var raw = FrameBounds(hWnd);
                var clipped = Rectangle.Intersect(raw, screenBounds);
                if (clipped.Width <= 0 || clipped.Height <= 0) return true;

                var className = probe.ClassName;
                var isTaskBar = WindowClassifier.IsTaskBarClass(className);
                var title = ReadTitle(hWnd);
                var processName = ReadProcessName(probe.ProcessId);
                var (app, category) = WindowClassifier.Classify(processName, isTaskBar);

                found.Add(new WindowInfo(
                    hWnd, title, processName, app, category, clipped,
                    isTaskBar, hWnd == foreground));
            }
            catch
            {
                // 枚举过程中窗口可能随时关闭，个别窗口取属性失败不影响其它窗口。
            }
            return true;
        }, IntPtr.Zero);

        // 矩形完全相同的窗口只保留最上面那个（z 序靠前）。
        // 全屏应用（浏览器、WorkBuddy 这类）经常彼此完全重叠，列出来只会让编号牌叠成一坨。
        var deduped = found
            .GroupBy(w => w.Bounds)
            .Select(g => g.First())
            .ToList();

        // 稳定排序：前台窗口排最前，任务栏这类贴边窗口排最后，其余保持 z 序（上层在上面）。
        return deduped
            .OrderBy(w => w.IsForeground ? 0 : 1)
            .ThenBy(w => w.IsTaskBar ? 1 : 0)
            .ToList();
    }

    /// <summary>
    /// 过滤规则：决定一个窗口值不值得作为截图候选。
    /// 抽成纯函数以便单测（见 WindowEnumeratorTests）。
    /// </summary>
    public static bool IsSelectable(WindowProbe probe, Rectangle screenBounds, int currentProcessId)
    {
        if (!probe.Visible) return false;          // 隐藏窗口截不到
        if (probe.Minimized) return false;        // 最小化的窗口在任务栏上，没有实际界面
        // 任务栏本身带 WS_EX_TOOLWINDOW，但它是用户明确想截的"底部工具栏"，要豁免。
        if (probe.ToolWindow && !probe.IsTaskBar) return false;   // 托盘气泡、悬浮条之类
        if (probe.ProcessId == currentProcessId) return false;   // 截图工具自己
        if (DesktopClasses.Contains(probe.ClassName)) return false;   // 桌面壁纸、开始菜单

        var b = probe.Bounds;
        if (b.Width <= 0 || b.Height <= 0) return false;
        if (!Intersects(b, screenBounds)) return false;   // 完全在屏外

        if (probe.IsTaskBar)
            return b.Width >= MinTaskBarWidth && b.Height >= MinTaskBarHeight;

        return b.Width >= MinWindowWidth && b.Height >= MinWindowHeight;
    }

    /// <summary>两个矩形是否有面积大于 0 的交集。</summary>
    private static bool Intersects(Rectangle a, Rectangle b)
    {
        var r = Rectangle.Intersect(a, b);
        return r.Width > 0 && r.Height > 0;
    }

    private static WindowProbe Probe(IntPtr hWnd)
    {
        var exStyle = GetWindowLongPtr(hWnd, GwlExstyle).ToInt64();
        GetWindowThreadProcessId(hWnd, out var pid);
        GetWindowRect(hWnd, out var rect);
        var className = ReadClassName(hWnd);

        return new WindowProbe
        {
            Visible = IsWindowVisible(hWnd),
            Minimized = IsIconic(hWnd),
            ToolWindow = (exStyle & WsExToolWindow) != 0,
            IsTaskBar = WindowClassifier.IsTaskBarClass(className),
            ClassName = className,
            Bounds = rect.ToRectangle(),
            ProcessId = (int)pid,
        };
    }

    /// <summary>
    /// 取窗口在屏幕上"看得见的那一块"。
    ///
    /// GetWindowRect 会把 DWM 投下的不可见阴影边框也算进去（每边常多出 8~16px），
    /// 用它框窗口就会连带截进窗口外的桌面/别的窗口。DWM 的扩展边框正好排除了这部分，
    /// 但它在远程会话 / 虚拟桌面偶尔会返回离谱坐标。因此：先取 GetWindowRect 作基准，
    /// 只有 DWM 结果落在基准附近（见 IsPlausibleFrame）才采用，否则退回基准。
    /// </summary>
    private static Rectangle FrameBounds(IntPtr hWnd)
    {
        if (!GetWindowRect(hWnd, out var rect)) return Rectangle.Empty;
        var raw = rect.ToRectangle();
        if (DwmGetWindowAttribute(hWnd, DwmwaExtendedFrameBounds, out var dwm,
                Marshal.SizeOf<RECT>()) == 0)
        {
            var extended = dwm.ToRectangle();
            if (IsPlausibleFrame(raw, extended)) return extended;
        }
        return raw;
    }

    /// <summary>
    /// DWM 扩展边框是否可信：必须不比 GetWindowRect 大、四边都贴着原矩形、
    /// 每边最多收缩 32px。抽成纯函数以便单测——真机上 DWM 抽风很难复现。
    /// </summary>
    public static bool IsPlausibleFrame(Rectangle raw, Rectangle extended)
    {
        if (extended.Width <= 0 || extended.Height <= 0) return false;
        if (extended.Width > raw.Width || extended.Height > raw.Height) return false;
        const int tolerance = 2;   // DWM 与 GetWindowRect 的取整误差
        const int maxInset = 32;   // 阴影边框再厚也不会超过这个数
        return extended.Left >= raw.Left - tolerance &&
               extended.Top >= raw.Top - tolerance &&
               extended.Right <= raw.Right + tolerance &&
               extended.Bottom <= raw.Bottom + tolerance &&
               extended.Left - raw.Left <= maxInset &&
               extended.Top - raw.Top <= maxInset &&
               raw.Right - extended.Right <= maxInset &&
               raw.Bottom - extended.Bottom <= maxInset;
    }

    private static string ReadTitle(IntPtr hWnd)
    {
        var length = GetWindowTextLength(hWnd);
        if (length <= 0) return "";
        var sb = new StringBuilder(length + 1);
        GetWindowText(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private static string ReadClassName(IntPtr hWnd)
    {
        var sb = new StringBuilder(256);
        GetClassName(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private static string ReadProcessName(int processId)
    {
        if (processId <= 0) return "";
        try
        {
            using var p = Process.GetProcessById(processId);
            return p.ProcessName;
        }
        catch
        {
            return "";
        }
    }

    private static int CurrentProcessId()
    {
        try { return Environment.ProcessId; }
        catch { return 0; }
    }
}
