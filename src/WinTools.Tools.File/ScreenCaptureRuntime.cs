using System.Drawing;
using System.Windows.Forms;
using WinTools.Core;

namespace WinTools.Tools.Files;

/// <summary>
/// 截图的常驻运行时：持有设置、全局热键与最近一次结果。
///
/// 热键必须注册在进程级，而工具界面是"点开才创建、切走可能被缓存"的，
/// 所以状态不能挂在页面控件上——它得活在工具 DLL 的静态域里，由宿主的后台工具生命周期驱动。
/// </summary>
internal static class ScreenCaptureRuntime
{
    private static HotkeyWindow? _hotkeys;
    private static Form? _mainWindow;
    private static Action<string> _log = _ => { };
    private static int _regionId;
    private static int _fullId;
    private static int _scrollId;
    private static bool _suspended;

    public static ScreenCaptureSettings Settings { get; private set; } = new();

    public static bool Started { get; private set; }

    /// <summary>
    /// 最近一次截图结果。剪贴板只保存位图引用（不复制像素），
    /// 这张图必须保活到下一次截图，否则粘贴出来是空白/损坏。
    /// </summary>
    public static Bitmap? LastImage { get; private set; }

    public static ScreenCaptureSettings Defaults() => new();

    public static void Start(BackgroundToolContext context)
    {
        if (Started) Stop();
        _mainWindow = context.MainWindow;
        _log = context.Log ?? (_ => { });
        Settings = ScreenCaptureSettingsStore.Load();
        Started = true;
        _log($"截图全局热键：{RegisterAll()}");
    }

    /// <summary>保存设置并立即重新注册热键，返回给用户看的一句话结果。</summary>
    public static string Apply(ScreenCaptureSettings next)
    {
        var saved = ScreenCaptureSettingsStore.Save(next);
        Settings = next;
        var status = RegisterAll();
        return saved ? status : $"{status}（设置未能写入磁盘，本次运行仍生效）";
    }

    /// <summary>录入快捷键期间暂停所有截图热键，否则一按键就先触发了截图。</summary>
    public static void Suspend()
    {
        _suspended = true;
        ReleaseHotkeys();
    }

    public static void Resume()
    {
        if (!_suspended) return;
        _suspended = false;
        RegisterAll();
    }

    public static void Stop()
    {
        ReleaseHotkeys();
        _hotkeys?.Dispose();
        _hotkeys = null;
        LastImage?.Dispose();
        LastImage = null;
        Started = false;
    }

    public static bool TryInvoke(string command)
    {
        try
        {
            switch (command)
            {
                case "capture-region": TriggerRegion(); return true;
                case "capture-full": TriggerFull(); return true;
                case "capture-scroll": TriggerScroll(); return true;
                case "capture-window": TriggerWindowPicker(); return true;
                default: return false;
            }
        }
        catch (Exception ex)
        {
            _log($"截图命令 {command} 执行失败：{ex.Message}");
            return false;
        }
    }

    public static void TriggerRegion() => Trigger(CaptureMode.Region);

    public static void TriggerFull() => Trigger(CaptureMode.FullScreen);

    public static void TriggerScroll() => Trigger(CaptureMode.Scroll);

    /// <summary>
    /// 弹出桌面窗口选择器：列出桌面上打开的窗口（微信、浏览器、任务栏……），
    /// 选中后直接按它的范围截图。托盘入口用，主窗体此时是隐藏的。
    /// </summary>
    public static void TriggerWindowPicker()
    {
        var window = _mainWindow;
        if (window is { IsDisposed: false } && window.InvokeRequired)
        {
            window.BeginInvoke(new Action(TriggerWindowPicker));
            return;
        }

        using var picker = new WindowPickerForm(
            ScreenshotRunner.ListWindows(SystemInformation.VirtualScreen));

        var pt = Cursor.Position;
        var area = Screen.FromPoint(pt).WorkingArea;
        picker.Location = new Point(
            Math.Clamp(pt.X, area.Left, Math.Max(area.Left, area.Right - picker.Width)),
            Math.Clamp(pt.Y, area.Top, Math.Max(area.Top, area.Bottom - picker.Height)));

        picker.ShowDialog();
        if (picker.Picked is not { } picked) return;

        var outcome = ScreenshotRunner.CaptureRegion(picked.Bounds, null, Settings.AutoCopy);
        if (outcome.Image is not null)
        {
            LastImage?.Dispose();
            LastImage = outcome.Image;
        }
        CaptureToast.Show(outcome.Message, outcome.IsError ? 3200 : 2000);
    }

    private static void Trigger(CaptureMode mode)
    {
        // 热键回调本来就在 UI 线程；托盘菜单若来自其它线程再切回来。
        var window = _mainWindow;
        if (window is { IsDisposed: false } && window.InvokeRequired)
        {
            window.BeginInvoke(new Action(() => Trigger(mode)));
            return;
        }

        var outcome = ScreenshotRunner.Run(mode, 0, window, Settings.AutoCopy);
        if (outcome.Image is not null)
        {
            LastImage?.Dispose();
            LastImage = outcome.Image;
        }
        CaptureToast.Show(outcome.Message, outcome.IsError ? 3200 : 2000);
    }

    private static string RegisterAll()
    {
        ReleaseHotkeys();
        if (_suspended) return "热键已暂停";

        var errors = new List<string>();
        var region = ParseOrNull(Settings.RegionHotkey, "区域截图", errors);
        var full = ParseOrNull(Settings.FullHotkey, "全屏截图", errors);
        var scroll = ParseOrNull(Settings.ScrollHotkey, "滚动截图", errors);

        if (region is { } r && full is { } f && r == f)
        {
            errors.Add("区域与全屏不能使用同一个组合，已只保留区域");
            full = null;
        }
        if (scroll is { } s && ((region is { } r2 && s == r2) || (full is { } f2 && s == f2)))
        {
            errors.Add("滚动截图与其它入口用了同一个组合，已忽略滚动截图热键");
            scroll = null;
        }

        _hotkeys ??= new HotkeyWindow();
        _regionId = region is { } rg ? TryRegister(rg, "区域截图", () => Trigger(CaptureMode.Region), errors) : 0;
        _fullId = full is { } fg ? TryRegister(fg, "全屏截图", () => Trigger(CaptureMode.FullScreen), errors) : 0;
        _scrollId = scroll is { } sg ? TryRegister(sg, "滚动截图", () => Trigger(CaptureMode.Scroll), errors) : 0;

        if (errors.Count > 0) return "部分热键未生效：" + string.Join("；", errors);

        var labels = new List<string>(3);
        if (region is { } a) labels.Add($"区域 {a}");
        if (full is { } b) labels.Add($"全屏 {b}");
        if (scroll is { } c) labels.Add($"滚动 {c}");
        return labels.Count == 0 ? "已清空热键：只能用界面按钮截图" : "已生效 · " + string.Join(" · ", labels);
    }

    private static HotkeyGesture? ParseOrNull(string text, string name, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (HotkeyGesture.TryParse(text, out var gesture) && gesture.IsValid) return gesture;
        errors.Add($"{name}「{text}」不是合法组合");
        return null;
    }

    private static int TryRegister(HotkeyGesture gesture, string name, Action action, List<string> errors)
    {
        var id = _hotkeys!.Register(gesture, action, out var error);
        if (id == 0) errors.Add($"{name} {gesture} 注册失败：{error}");
        return id;
    }

    private static void ReleaseHotkeys()
    {
        if (_hotkeys is not null)
        {
            _hotkeys.Unregister(_regionId);
            _hotkeys.Unregister(_fullId);
            _hotkeys.Unregister(_scrollId);
        }
        _regionId = 0;
        _fullId = 0;
        _scrollId = 0;
    }
}
