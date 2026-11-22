using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using WinTools.Core.Ui;
using WinTools.Services.Logging;

namespace WinTools.Shell;

internal static class Program
{
    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(Point pt, uint flags);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();

    private const uint MonitorDefaultToPrimary = 0x00000001;
    private const int EffectiveDpi = 0;

    [STAThread]
    private static void Main()
    {
        // 先让运行时把进程切成 PerMonitorV2，再读 DPI：顺序反了拿到的是 96。
        ApplicationConfiguration.Initialize();
        UiTheme.SetScaleFromDpi(PrimaryMonitorDpi());

        // 全局异常兜底：避免任一事件处理器抛出的异常静默终止整个进程，日志服务自身已吞异常不会二次崩溃。
        var logger = new TextLogger();
        Application.ThreadException += (_, e) => logger.Error($"UI 线程未处理异常: {e.Exception}");
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            logger.Error($"进程未处理异常: {(e.ExceptionObject is Exception ex ? ex.ToString() : e.ExceptionObject)}");
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            logger.Error($"未观察的任务异常: {e.Exception}");
            e.SetObserved();
        };

        Application.Run(new MainForm());
    }

    /// <summary>
    /// PerMonitorV2 进程里 GetDpiForSystem 返回的是开机时的系统 DPI，多屏/改过缩放时可能是 96，
    /// 真实倍率挂在显示器上，所以按主显示器取有效 DPI。
    /// </summary>
    private static float PrimaryMonitorDpi()
    {
        var monitor = MonitorFromPoint(new Point(0, 0), MonitorDefaultToPrimary);
        if (monitor != IntPtr.Zero &&
            GetDpiForMonitor(monitor, EffectiveDpi, out var dpiX, out _) == 0 && dpiX >= 96)
            return dpiX;
        var system = GetDpiForSystem();
        return system == 0 ? 96f : system;
    }
}
