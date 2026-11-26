using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using WinTools.Core;
using WinTools.Core.Ui;

namespace WinTools.Shell;

/// <summary>
/// 宿主托盘与后台工具生命周期。
///
/// 全局截图热键的意义在于"窗口不在前台也能按"，所以关闭主窗口不再退出进程，而是收进托盘：
/// 托盘常驻 + 隐藏窗口 = 热键一直有效。真正退出走托盘菜单，或由系统关机触发。
/// </summary>
public sealed partial class MainForm
{
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private NotifyIcon? _tray;
    private ContextMenuStrip? _trayMenu;
    private HotkeyWindow? _shellHotkeys;
    private readonly List<IBackgroundTool> _backgroundTools = new();
    private bool _exiting;
    private bool _trayTipShown;

    private void SetupTray()
    {
        _trayMenu = new ContextMenuStrip { ShowImageMargin = false };

        var open = new ToolStripMenuItem("打开主界面");
        open.Click += (_, _) => RestoreFromTray();

        var capture = new ToolStripMenuItem("区域截图");
        capture.Click += (_, _) => InvokeScreenshot("capture-region");

        var captureFull = new ToolStripMenuItem("全屏截图");
        captureFull.Click += (_, _) => InvokeScreenshot("capture-full");

        var captureScroll = new ToolStripMenuItem("滚动截图");
        captureScroll.Click += (_, _) => InvokeScreenshot("capture-scroll");

        var captureWindow = new ToolStripMenuItem("窗口截图…");
        captureWindow.Click += (_, _) => InvokeScreenshot("capture-window");

        var exit = new ToolStripMenuItem("退出 WinTools");
        exit.Click += (_, _) => ExitApplication();

        _trayMenu.Items.Add(open);
        _trayMenu.Items.Add(capture);
        _trayMenu.Items.Add(captureFull);
        _trayMenu.Items.Add(captureScroll);
        _trayMenu.Items.Add(captureWindow);
        _trayMenu.Items.Add(new ToolStripSeparator());
        _trayMenu.Items.Add(exit);

        _tray = new NotifyIcon
        {
            Icon = BuildTrayIcon(),
            Text = "WinTools 轻量级工具箱",
            Visible = true,
            ContextMenuStrip = _trayMenu
        };
        _tray.DoubleClick += (_, _) => RestoreFromTray();

        // 侧栏一直写着「Ctrl Shift T 唤起」，这里把它落成系统级热键：窗口隐藏时也能唤醒。
        try
        {
            _shellHotkeys = new HotkeyWindow();
            HotkeyGesture.TryParse("Ctrl+Shift+T", out var gesture);
            var id = _shellHotkeys.Register(gesture, RestoreFromTray, out var error);
            if (id == 0) _logger.Warn($"托盘唤起热键注册失败：{error}");
        }
        catch (Exception ex)
        {
            _logger.Warn($"托盘唤起热键初始化失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 托盘图标：程序没有随包 ico，这里按品牌色现画一个（圆角底 + 截图轮廓），
    /// 通过 GetHicon 转成 Icon 后必须 DestroyIcon，否则每个进程都会漏一个 GDI 图标句柄。
    /// </summary>
    private static Icon BuildTrayIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var path = UiTheme.RoundedPath(new Rectangle(0, 0, 32, 32), 8);
            using var brush = new SolidBrush(UiTheme.Primary);
            g.FillPath(brush, path);
            IconFactory.Draw(g, "screenshot", new RectangleF(6, 6, 20, 20), Color.White, 1.8f);
        }
        var handle = bmp.GetHicon();
        try
        {
            using var temp = Icon.FromHandle(handle);
            return (Icon)temp.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private void HideToTray()
    {
        Hide();
        if (_trayTipShown) return;
        _trayTipShown = true;
        try
        {
            _tray?.ShowBalloonTip(4000, "WinTools 还在后台运行",
                "Ctrl+Shift+A 区域 · Ctrl+Shift+F 全屏 · Ctrl+Shift+S 滚动截图 · 窗口截图可按窗口范围截取 · 双击托盘图标打开主界面",
                ToolTipIcon.Info);
        }
        catch
        {
            // 气泡提示在部分系统设置下会被禁用，不影响功能。
        }
    }

    private void RestoreFromTray()
    {
        if (IsDisposed) return;
        if (!Visible) Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
        BringToFront();
    }

    /// <summary>
    /// 托盘菜单里的截图项：不硬编码具体工具，只按约定 Id 找工具并用命令接口触发；
    /// 工具没有实现命令接口时退回打开主界面，行为始终可预期。
    /// </summary>
    private void InvokeScreenshot(string command)
    {
        var tool = _tools.FirstOrDefault(t => t.Id == "screen-capture");
        if (tool is IToolCommand commands && commands.TryInvoke(command)) return;
        RestoreFromTray();
    }

    private void ExitApplication()
    {
        _exiting = true;
        Close();
    }

    private void StartBackgroundTools()
    {
        var context = new BackgroundToolContext
        {
            MainWindow = this,
            Log = message => _logger.Info(message),
        };
        foreach (var tool in _tools)
        {
            if (tool is not IBackgroundTool background) continue;
            try
            {
                background.Start(context);
                _backgroundTools.Add(background);
            }
            catch (Exception ex)
            {
                // 一个后台工具起不来不该拖垮整个 Shell。
                _logger.Error($"后台工具 {tool.Id} 启动失败: {ex.Message}");
            }
        }
    }

    private void StopBackgroundTools()
    {
        foreach (var tool in _backgroundTools)
        {
            try { tool.Stop(); }
            catch (Exception ex) { _logger.Error($"后台工具停止失败: {ex.Message}"); }
        }
        _backgroundTools.Clear();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _logger.Info($"关闭请求：原因={e.CloseReason}，主动退出={_exiting}");
        // 用户点关闭 / Alt+F4 → 收进托盘继续待命，否则进程一退全局热键就失效。
        if (!_exiting && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            HideToTray();
            _logger.Info("已取消关闭，收进托盘后台待命");
            return;
        }
        base.OnFormClosing(e);
    }

    /// <summary>窗体真正关闭（托盘退出 / 系统关机）时的托盘与后台资源清理。</summary>
    private void DisposeTrayAndBackground()
    {
        StopBackgroundTools();
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
            _tray = null;
        }
        _trayMenu?.Dispose();
        _trayMenu = null;
        _shellHotkeys?.Dispose();
        _shellHotkeys = null;
    }
}
